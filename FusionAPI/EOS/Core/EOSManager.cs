using Epic.OnlineServices;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;

using FusionAPI.EOS.Auth;
using FusionAPI.Epic;
using FusionAPI.Interfaces;

namespace FusionAPI.EOS.Core;

internal class EOSManager : IDisposable
{
    private const int TickIntervalMs = 50;

    private readonly EOSAuthManager _authManager;
    private readonly EOSThreadDispatcher _dispatcher;
    private readonly ILogger _logger;

    private CancellationTokenSource? _tickCts;
    internal bool IsInitialized { get; private set; }

    internal EOSManager(EOSAuthManager authManager, ILogger logger)
    {
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = new EOSThreadDispatcher();
    }

    internal Task<bool> InitializeAsync()
        => _dispatcher.RunOnEOSThreadAsync(async () =>
        {
            if (IsInitialized)
            {
                _logger.Warning("EOS is already initialized");
                return true;
            }

            if (!InitializePlatform())
                return false;

            if (!InitializeInterfaces())
            {
                Shutdown();
                return false;
            }

            ConfigureLogging();

            StartTicker();

            bool loginSuccess = await _authManager.LoginAsync();

            if (!loginSuccess)
            {
                Shutdown();
                return false;
            }

            IsInitialized = true;
            return true;
        })
        .Unwrap();

    internal void Shutdown()
    {
        _tickCts?.Cancel();
        _tickCts = null;

        IsInitialized = false;
        EOSInterfaces.Shutdown();
    }

    public void Dispose()
    {
        Shutdown();
        _dispatcher.Dispose();
    }

    private bool InitializePlatform()
    {
        var initializeOptions = new InitializeOptions
        {
            ProductName = EOSCredentials.ProductName,
            ProductVersion = EOSCredentials.ProductVersion,
        };

        var result = PlatformInterface.Initialize(ref initializeOptions);

        if (result != Result.Success && result != Result.AlreadyConfigured)
        {
            _logger.Error($"Failed to initialize EOS Platform: {result}");
            return false;
        }

        return true;
    }

    private bool InitializeInterfaces()
    {
        var options = new Options
        {
            ProductId = EOSCredentials.ProductId,
            SandboxId = EOSCredentials.SandboxId,
            DeploymentId = EOSCredentials.DeploymentId,
            ClientCredentials = new ClientCredentials
            {
                ClientId = EOSCredentials.ClientId,
                ClientSecret = EOSCredentials.ClientSecret,
            },
            Flags = PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay,
        };

        var platform = PlatformInterface.Create(ref options);

        if (platform == null)
        {
            _logger.Error("Failed to create EOS Platform Interface");
            return false;
        }

        EOSInterfaces.Initialize(platform);

        if (!EOSInterfaces.ValidateInterfaces())
        {
            _logger.Error("Failed to get one or more EOS interfaces");
            return false;
        }

        return true;
    }

    private void ConfigureLogging()
    {
        LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Info);
        LoggingInterface.SetCallback((ref LogMessage message) => _logger.Info($"[{Enum.GetName(typeof(LogLevel), message.Level)?.ToUpper() ?? "N/A"}] {message.Message}"));
    }

    private void StartTicker()
    {
        _tickCts = new CancellationTokenSource();
        var token = _tickCts.Token;

        _dispatcher.Post(async () =>
        {
            while (!token.IsCancellationRequested && EOSInterfaces.IsInitialized)
            {
                try
                {
                    EOSInterfaces.Platform?.Tick();
                }
                catch (Exception ex)
                {
                    _logger.Trace("Error ticking EOS platform", ex);
                }

                await Task.Delay(TickIntervalMs, token).ConfigureAwait(true);
            }
        });
    }
}