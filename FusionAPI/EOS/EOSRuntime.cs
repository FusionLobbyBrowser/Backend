using FusionAPI.EOS.Connect;
using FusionAPI.EOS.Lobby;
using FusionAPI.EOS.Platform;
using FusionAPI.Interfaces;

namespace FusionAPI.EOS;

internal class EOSRuntime
{
    internal ILogger Logger { get; set; }

    internal EOSPlatform Platform { get; private set; }
    internal EOSConnect Connect { get; private set; }
    internal EOSLobby Lobby { get; private set; }

    internal ThreadDispatcher Dispatcher { get; private set; }

    public bool IsInitialized { get; private set; }

    internal Task<bool> InitializeAsync(ILogger logger)
    {
        if (IsInitialized)
        {
            Logger.Warning("EOS is already initialized");
            return Task.FromResult(true);
        }

        IsInitialized = false;
        Logger = logger;
        Dispatcher = new ThreadDispatcher();
        return Dispatcher.RunOnThreadAsync(async () =>
        {
            Platform = new EOSPlatform();
            var platformSuccess = await Platform.InitializeAsync(Logger, Dispatcher);
            if (!platformSuccess)
                return false;

#if DEBUG
            Logger.Info("Initialized Platform");
#endif

            Connect = new EOSConnect(Platform.PlatformInterface.GetConnectInterface());
            var connectSuccess = await Connect.InitializeAsync(Logger, Dispatcher);
            if (!connectSuccess)
            {
                Shutdown();
                return false;
            }

#if DEBUG
            Logger.Info("Initialized Connect");
#endif

            Lobby = new EOSLobby(this, Platform.PlatformInterface.GetLobbyInterface(), Connect.LocalUserId);
            var lobbySuccess = await Lobby.InitializeAsync(Logger, Dispatcher);
            if (!lobbySuccess)
            {
                Shutdown();
                return false;
            }

#if DEBUG
            Logger.Info("Initialized Lobby");
#endif

            IsInitialized = true;
            return true;
        });
    }

    internal void Tick()
    {
        Platform?.Tick();
        Connect?.Tick();
        Lobby?.Tick();
    }

    internal void Shutdown()
    {
        IsInitialized = false;
        Lobby?.Shutdown();
        Connect?.Shutdown();
        Platform?.Shutdown();
    }
}