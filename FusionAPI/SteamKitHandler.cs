using FusionAPI.Interfaces;

using SteamKit2;
using SteamKit2.Authentication;

using static SteamKit2.SteamMatchmaking;
using static SteamKit2.SteamMatchmaking.Lobby;

namespace FusionAPI
{
    public class SteamKitHandler : ISteamHandler
    {
        public SteamKit2.SteamClient? SteamClient { get; }

        public CallbackManager? CallbackManager { get; }

        public SteamUser? SteamUser { get; }

        private IReadOnlyList<SteamFriends.FriendsListCallback.Friend>? FriendsList;

        private readonly SteamMatchmaking? Matchmaking;

        private string? Username, Password;

        public bool IsLoggedOn { get; private set; }

        public bool IsInitialized => SteamClient?.IsConnected == true && IsLoggedOn;

        public Func<TwoFactorType, string>? TwoFactorRequired;

        private string? TwoFactorCode, AuthCode;

        private ILogger? Logger;

        private string previouslyStoredGuardData = null;


        public SteamKitHandler()
        {
            SteamClient = new SteamKit2.SteamClient();
            Matchmaking = SteamClient.GetHandler<SteamMatchmaking>();

            SteamUser = SteamClient.GetHandler<SteamUser>();
            if (SteamUser == null)
                throw new InvalidOperationException("Failed to get SteamUser handler from SteamKit!");

            CallbackManager = new CallbackManager(SteamClient);
            CallbackManager.Subscribe<SteamKit2.SteamClient.ConnectedCallback>(ConnectedCallback);
            CallbackManager.Subscribe<SteamKit2.SteamClient.DisconnectedCallback>(DisconnectedCallback);
            CallbackManager.Subscribe<SteamKit2.SteamUser.LoggedOnCallback>(LoggedOnCallback);
            CallbackManager.Subscribe<SteamKit2.SteamUser.LoggedOffCallback>(LoggedOffCallback);
            CallbackManager.Subscribe<SteamKit2.SteamFriends.FriendsListCallback>(FriendsListCallback);

            Task.Run(async () =>
            {
                while (true)
                {
                    // in order for the callbacks to get routed, they need to be handled by the manager
                    CallbackManager.RunCallbacks();
                    await Task.Delay(50);
                }
            });
        }

        private void FriendsListCallback(SteamFriends.FriendsListCallback callback)
        {
            FriendsList = callback.FriendList;
        }

        private async void ConnectedCallback(SteamKit2.SteamClient.ConnectedCallback callback)
        {
            Logger?.Info("Connected to Steam, logging in as {0}...", Username ?? "N/A");
            var authSession = await SteamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new()
            {
                Username = Username,
                Password = Password,
                ClientOSType = EOSType.Win11,
                DeviceFriendlyName = "Fusion Lobby Browser",
                IsPersistentSession = true,
                PlatformType = SteamKit2.Internal.EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
                Authenticator = new UserConsoleAuthenticator(),
                GuardData = previouslyStoredGuardData,

            });
            var pollResponse = await authSession.PollingWaitForResultAsync();
            if (pollResponse.NewGuardData != null)
            {
                previouslyStoredGuardData = pollResponse.NewGuardData;
            }
            SteamUser?.LogOn(new SteamKit2.SteamUser.LogOnDetails()
            {
                Username = pollResponse.AccountName,
                AccessToken = pollResponse.RefreshToken,
                ShouldRememberPassword = true,
            });
        }

        private void DisconnectedCallback(SteamKit2.SteamClient.DisconnectedCallback callback)
        {
            Logger?.Info("Disconnected from Steam.");
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                Logger?.Info("Reconnecting to Steam...");
                SteamClient?.Connect();

            });
        }

        private void LoggedOnCallback(SteamKit2.SteamUser.LoggedOnCallback callback)
        {
            bool isSteamGuard = callback.Result == EResult.AccountLogonDenied;
            bool is2FA = callback.Result == EResult.AccountLoginDeniedNeedTwoFactor;
            bool needsTwoFactor = isSteamGuard || is2FA;
            TwoFactorType type = !is2FA ? TwoFactorType.Email : TwoFactorType.Authenticator;
            string stringType = type == TwoFactorType.Email ? "Steam Guard (Email)" : "Two-Factor Authentication (Authenticator)";
            if (needsTwoFactor)
            {
                IsLoggedOn = false;
                Logger?.Warning("This account is protected by {0}.", stringType);
                NewCodeNeeded(type);

                Logger?.Info("Disconnection from Steam will now happen. A reconnect will be attempted with the provided code...");
                // It still would have disconnected, but to be sure that it does
                SteamClient?.Disconnect();

                return;
            }
            else if (callback.Result == EResult.InvalidLoginAuthCode)
            {
                IsLoggedOn = false;
                Logger?.Warning("The {0} code provided was invalid or has expired. Please provide a new one.", !string.IsNullOrEmpty(AuthCode) ? "Steam Guard (Email)" : "Two-Factor Authentication (Authenticator)");
                NewCodeNeeded(type);

                Logger?.Info("Disconnection from Steam will now happen. A reconnect will be attempted with the provided code...");
                // It still would have disconnected, but to be sure that it does
                SteamClient?.Disconnect();

                return;
            }

            if (callback.Result != SteamKit2.EResult.OK)
            {
                IsLoggedOn = false;
                Logger?.Error("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);
                return;
            }
            IsLoggedOn = true;
            Logger?.Info("Successfully logged in to Steam as {0}!", Username ?? "N/A");
        }

        private void NewCodeNeeded(TwoFactorType type)
        {
            string stringType = type == TwoFactorType.Email ? "Steam Guard (Email)" : "Two-Factor Authentication (Authenticator)";
            var code = TwoFactorRequired?.Invoke(type);
            if (string.IsNullOrEmpty(code))
            {
                Logger?.Error("No {0} code provided, cannot log in.", stringType);
                return;
            }
            if (type == TwoFactorType.Email)
                AuthCode = code;
            else
                TwoFactorCode = code;
        }

        private void LoggedOffCallback(SteamKit2.SteamUser.LoggedOffCallback callback)
        {
            IsLoggedOn = false;
            Logger?.Info("Logged off from Steam.");
        }

        public async Task<IMatchmakingLobby[]> GetLobbies()
        {
            List<Filter> filters = [
                    new DistanceFilter(ELobbyDistanceFilter.Worldwide),
                    new SlotsAvailableFilter(int.MaxValue),
                    new StringFilter(LobbyConstants.HasServerOpenKey, ELobbyComparison.Equal, bool.TrueString)
                ];
            if (Matchmaking == null)
                return [];

            var task = Matchmaking.GetLobbyList(Fusion.AppID, filters, maxLobbies: int.MaxValue);
            GetLobbyListCallback? lobbies = null;
            if (task != null)
                lobbies = await task.ToTask().WaitAsync(CancellationToken.None);

            if (lobbies != null && lobbies.Result == SteamKit2.EResult.OK)
                return [.. ProcessLobbies(lobbies.Lobbies)];
            return [];
        }

        private List<IMatchmakingLobby> ProcessLobbies(List<SteamMatchmaking.Lobby> lobbies)
        {
            var list = new List<IMatchmakingLobby>();
            lobbies.ForEach(lobby => list.Add(new SteamKitLobby(lobby, SteamClient)));
            return list;

        }

        public bool IsFriend(ulong id)
            => FriendsList?.Any(f => f.SteamID.ConvertToUInt64() == id) == true;

        public async Task Init(ILogger logger, Dictionary<string, string> metadata)
        {
            if (!metadata.TryGetValue("username", out string? value1) || !metadata.TryGetValue("password", out string? value))
                throw new AuthenticationException("SteamKitHandler requires 'username' and 'password' in metadata to initialize!");

            Logger = logger;
            Username = value1;
            Password = value;
            if (SteamClient?.IsConnected == true)
                SteamClient.Disconnect();

            Logger.Info("Connecting to Steam...");
            SteamClient?.Connect();
            while (!IsInitialized)
                await Task.Delay(250);

        }

        internal class SteamKitLobby(SteamMatchmaking.Lobby lobby, SteamClient? client) : IMatchmakingLobby
        {
            public ulong Owner => lobby?.OwnerSteamID?.ConvertToUInt64() ?? 0;

            public bool IsOwnerMe => client?.SteamID == lobby.OwnerSteamID;

            public string GetData(string key)
            {
                if (lobby.Metadata.ContainsKey(key))
                    return lobby.Metadata[key];
                return string.Empty;
            }
        }

        public enum TwoFactorType
        {
            Email,
            Authenticator
        }
    }
}
