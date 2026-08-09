using Epic.OnlineServices;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;

using FusionAPI.Interfaces;

namespace FusionAPI.EOS.Platform;

internal class EOSPlatform : EOSInterface
{
    private const string ProductName = "Fusion";
    private const string ProductVersion = "0.0.1";
    private const string ProductId = "29e074d5b4724f3bb01f26b7e33d2582";
    private const string ClientId = "xyza78915hKqxe2TNTavpq2sxBDvJ9AH";
    private const string ClientSecret = "SWDxYlWWsEgvmD0o3qAm2RMZoSZzOfYo5yvX/uikH94";
    private const string SandboxId = "26f32d66d87f4dfeb4a7449b776a41f1";
    private const string DeploymentId = "f3fdf691aa6c4004abdb1e19665c1429";
    private const PlatformFlags Flags = PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay;
    private const float TickInterval = 1f / 20f;

    internal PlatformInterface? PlatformInterface;
    internal ILogger? Logger;

    internal override async Task<bool> InitializeAsync(ILogger logger, ThreadDispatcher dispatcher)
    {
        Logger = logger;
        if (!InitializePlatform())
            return false;

        if (!CreatePlatform(out PlatformInterface))
            return false;

#if DEBUG
        LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Info);
        LoggingInterface.SetCallback((ref LogMessage message) =>
        {
            switch (message.Level)
            {
                case LogLevel.Info:
                    Logger?.Info($"[{message.Category}] {message.Message}");
                    break;

                case LogLevel.Warning:
                    Logger?.Warning($"[{message.Category}] {message.Message}");
                    break;

                case LogLevel.Error:
                    Logger?.Error($"[{message.Category}] {message.Message}");
                    break;

                case LogLevel.Fatal:
                    Logger?.Error($"[FATAL] [{message.Category}] {message.Message}");
                    break;

                case LogLevel.Off:
                case LogLevel.Verbose:
                case LogLevel.VeryVerbose:
                    Logger?.Trace($"[{message.Category}] {message.Message}");
                    break;

                default:
                    Logger?.Trace($"[{message.Category}] {message.Message}");
                    break;
            }
        });
#endif

        InitializeTicker(dispatcher);

        return true;
    }

    private bool InitializePlatform()
    {
        var initializeOptions = new InitializeOptions
        {
            ProductName = ProductName,
            ProductVersion = ProductVersion
        };

        var initializeResult = PlatformInterface.Initialize(ref initializeOptions);
        if (initializeResult is Result.Success or Result.AlreadyConfigured) return true;
        Logger?.Error($"Failed to initialize EOS Platform: {initializeResult}");
        return false;
    }

    private bool CreatePlatform(out PlatformInterface platformInterface)
    {
        var options = new Options
        {
            ProductId = ProductId,
            SandboxId = SandboxId,
            DeploymentId = DeploymentId,
            ClientCredentials = new ClientCredentials
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret
            },
            Flags = Flags
        };

        var platform = PlatformInterface.Create(ref options);
        if (platform == null)
        {
            Logger?.Error("Failed to create EOS Platform");
            platformInterface = null;
            return false;
        }

        platformInterface = platform;

        return true;
    }

    internal override void Tick()
    {
        PlatformInterface?.Tick();
    }

    internal void InitializeTicker(ThreadDispatcher dispatcher)
    {
        dispatcher.Post(async () =>
        {
            while (PlatformInterface != null)
            {
                await Task.Delay(50).ConfigureAwait(true);
                try
                {
                    PlatformInterface?.Tick();
                }
                catch (Exception ex)
                {
                    Logger?.Error("ticking EOS platform", ex);
                }
            }

#if DEBUG
            Logger?.Info("EOS Platform ticker stopped");
#endif
        });
    }

    internal override void Shutdown()
    {
        PlatformInterface?.Release();
        PlatformInterface = null;
    }
}