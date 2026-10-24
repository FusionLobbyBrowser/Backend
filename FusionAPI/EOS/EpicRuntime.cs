using FusionAPI.Data.Interfaces;

namespace FusionAPI.EOS;

internal class EpicRuntime
{
    internal ILogger? Logger { get; set; }

    internal EpicPlatform? Platform { get; private set; }
    internal EpicConnect? Connect { get; private set; }
    internal EpicLobby? Lobby { get; private set; }

    internal ThreadDispatcher? Dispatcher { get; private set; }

    public bool IsInitialized { get; private set; }

    internal Task<bool> InitializeAsync(ILogger? logger)
    {
        if (IsInitialized)
        {
            Logger?.Warning("EOS is already initialized");
            return Task.FromResult(true);
        }

        IsInitialized = false;
        Logger = logger;
        Dispatcher = new ThreadDispatcher();
        return Dispatcher.RunOnThreadAsync(async () =>
        {
            Platform = new EpicPlatform();
            var platformSuccess = await Platform.InitializeAsync(Logger, Dispatcher);
            if (!platformSuccess || Platform.PlatformInterface == null)
                return false;

#if DEBUG
            Logger?.Info("Initialized Platform");
#endif

            Connect = new EpicConnect(Platform.PlatformInterface.GetConnectInterface());
            var connectSuccess = await Connect.InitializeAsync(Logger, Dispatcher);
            if (!connectSuccess)
            {
                Shutdown();
                return false;
            }

#if DEBUG
            Logger?.Info("Initialized Connect");
#endif

            Lobby = new EpicLobby(this, Platform.PlatformInterface.GetLobbyInterface(), Connect.LocalUserId);
            var lobbySuccess = await Lobby.InitializeAsync(Logger, Dispatcher);
            if (!lobbySuccess)
            {
                Shutdown();
                return false;
            }

#if DEBUG
            Logger?.Info("Initialized Lobby");
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