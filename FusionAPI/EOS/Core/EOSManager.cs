using System.Collections;
using System.Diagnostics;

using Epic.OnlineServices;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;

using FusionAPI.EOS.Auth;
using FusionAPI.Interfaces;

namespace FusionAPI.EOS.Core;

/// <summary>
/// Manages EOS SDK initialization, lifecycle, and ticking.
/// </summary>
public class EOSManager
{
    private const float TickInterval = 1f / 20f;

    private readonly EOSAuthManager _authManager;
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;

    private ILogger Logger { get; }

    public EOSManager(EOSAuthManager authManager, ILogger logger)
    {
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        Logger = logger;
    }

    public async Task<bool> InitializeAsync()
    {
        Logger.Info("Initializing EOS Manager...");
        if (_isInitialized)
        {
            Logger.Warning("EOS is already initialized");
            return true;
        }

        if (!InitializePlatform())
        {
            Logger.Error("Failed to initialize EOS Platform");
            return true;
        }

        if (!InitializeInterfaces())
        {
            Logger.Error("Failed to initialize EOS Interfaces");
            Shutdown();
            return false;
        }

#if DEBUG
        ConfigureLogging();
#endif

        while (!EOSInterfaces.IsInitialized)
            await Task.Yield();

        TickerTask();

        Logger.Info("Logging into EOS...");

        var loginSuccess = await _authManager.LoginAsync();

        Logger.Info($"Login complete. Success: {loginSuccess}");

        if (!loginSuccess)
        {
            Shutdown();
            return false;
        }

        _isInitialized = true;
        return true;
    }

    public void Shutdown()
    {
        _isInitialized = false;
        EOSInterfaces.Shutdown();
    }

    private bool InitializePlatform()
    {
        Logger.Info("Initializing EOS Platform...");
        var initializeOptions = new InitializeOptions
        {
            ProductName = EOSCredentials.ProductName,
            ProductVersion = EOSCredentials.ProductVersion
        };

        var result = PlatformInterface.Initialize(ref initializeOptions);

        if (result != Result.Success && result != Result.AlreadyConfigured)
        {
            Logger.Error($"Failed to initialize EOS Platform: {result}");
            return false;
        }

        return true;
    }

    private bool InitializeInterfaces()
    {
        Logger.Info("Initializing EOS Interfaces...");
        var options = new Options
        {
            ProductId = EOSCredentials.ProductId,
            SandboxId = EOSCredentials.SandboxId,
            DeploymentId = EOSCredentials.DeploymentId,
            ClientCredentials = new ClientCredentials
            {
                ClientId = EOSCredentials.ClientId,
                ClientSecret = EOSCredentials.ClientSecret
            },
            Flags = PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay
        };

        var platform = PlatformInterface.Create(ref options);

        if (platform == null)
        {
            Logger.Error("Failed to create EOS Platform Interface");
            return false;
        }

        EOSInterfaces.Initialize(platform);

        if (!EOSInterfaces.ValidateInterfaces())
        {
            Logger.Error("Failed to get one or more EOS interfaces");
            return false;
        }

        return true;
    }

    private void ConfigureLogging()
    {
        Logger.Info("Configuring EOS logging...");
        LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Off);
        LoggingInterface.SetCallback((ref message) => Logger.Info($"[EOS] {message.Message}"));
    }

    private void TickerTask()
    {
        Stopwatch _tickStopwatch = Stopwatch.StartNew();
        while (EOSInterfaces.IsInitialized)
        {
            if (!_tickStopwatch.IsRunning)
                _tickStopwatch.Start();

            if ((_tickStopwatch.ElapsedMilliseconds / 1000) >= TickInterval)
            {
                _tickStopwatch.Reset();

                try
                {
                    Logger.Info("tick");
                    EOSInterfaces.Platform?.Tick();
                }
                catch (Exception ex)
                {
                    Logger.Error("ticking EOS platform", ex);
                }
            }
        }
    }
}