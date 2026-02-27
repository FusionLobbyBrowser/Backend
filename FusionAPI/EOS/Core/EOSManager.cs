using Epic.OnlineServices;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;

using FusionAPI.EOS.Auth;
using FusionAPI.Interfaces;

using System.Collections;

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

    public IEnumerator InitializeAsync(Action<bool> onComplete)
    {
        if (_isInitialized)
        {
            Logger.Warning("EOS is already initialized");
            onComplete?.Invoke(true);
            yield break;
        }

        if (!InitializePlatform())
        {
            onComplete?.Invoke(false);
            yield break;
        }

        if (!InitializeInterfaces())
        {
            Shutdown();
            onComplete?.Invoke(false);
            yield break;
        }

#if DEBUG
        ConfigureLogging();
#endif

        _ = TickerTask();

        // Perform login
        bool loginComplete = false;
        bool loginSuccess = false;

        yield return _authManager.LoginAsync(success =>
        {
            loginSuccess = success;
            loginComplete = true;
        });

        while (!loginComplete)
            yield return null;

        if (!loginSuccess)
        {
            Shutdown();
            onComplete?.Invoke(false);
            yield break;
        }

        _isInitialized = true;
        onComplete?.Invoke(true);
    }

    public void Shutdown()
    {
        _isInitialized = false;
        EOSInterfaces.Shutdown();
    }

    private bool InitializePlatform()
    {
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
        LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Off);
        LoggingInterface.SetCallback((ref LogMessage message) => Logger.Info($"[EOS] {message.Message}"));
    }

    private async Task TickerTask()
    {
        while (EOSInterfaces.IsInitialized)
        {
            try
            {
                EOSInterfaces.Platform?.Tick();
            }
            catch (Exception ex)
            {
                Logger.Error("ticking EOS platform", ex);
            }

            await Task.Delay((int)TickInterval * 1000);
        }
    }
}