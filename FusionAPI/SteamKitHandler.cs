using FusionAPI.Data.Enums;
using FusionAPI.Interfaces;

using SteamKit2;
using SteamKit2.Authentication;

using static SteamKit2.SteamMatchmaking;
using static SteamKit2.SteamMatchmaking.Lobby;

namespace FusionAPI
{
    public class SteamKitHandler : ISteamHandler
    {
        public SteamClient? SteamClient { get; }

        public CallbackManager? CallbackManager { get; }

        public SteamUser? SteamUser { get; }

        private IReadOnlyList<SteamFriends.FriendsListCallback.Friend>? FriendsList;

        private readonly SteamMatchmaking? Matchmaking;

        private string? Username, Password;

        public bool IsLoggedOn { get; private set; }

        public bool IsInitialized => SteamClient?.IsConnected == true && IsLoggedOn;

        public Func<TwoFactorType, string>? TwoFactorRequired { get; set; }

        public IAuthenticator? Authenticator { get; set; }

        private ILogger? Logger;

        private string? previouslyStoredGuardData;

        public SteamKitHandler()
        {
            SteamClient = new SteamClient();
            Matchmaking = SteamClient.GetHandler<SteamMatchmaking>();

            SteamUser = SteamClient.GetHandler<SteamUser>();
            if (SteamUser == null)
                throw new InvalidOperationException("Failed to get SteamUser handler from SteamKit!");

            CallbackManager = new CallbackManager(SteamClient);
            CallbackManager.Subscribe<SteamClient.ConnectedCallback>(ConnectedCallback);
            CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(DisconnectedCallback);
            CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(LoggedOnCallback);
            CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(LoggedOffCallback);
            CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(FriendsListCallback);

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

        private async void ConnectedCallback(SteamClient.ConnectedCallback callback)
        {
            try
            {
                Logger?.Info("Connected to Steam, logging in as {0}...", Username ?? "N/A");

                if (SteamClient == null)
                    return;

                var authSession = await SteamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new()
                {
                    Username = Username,
                    Password = Password,
                    ClientOSType = EOSType.Win11,
                    DeviceFriendlyName = "Fusion Lobby Browser",
                    IsPersistentSession = true,
                    PlatformType = SteamKit2.Internal.EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
                    Authenticator = Authenticator ?? new UserConsoleAuthenticator(),
                    GuardData = previouslyStoredGuardData,
                });

                var pollResponse = await authSession.PollingWaitForResultAsync();
                if (pollResponse.NewGuardData != null)
                    previouslyStoredGuardData = pollResponse.NewGuardData;

                SteamUser?.LogOn(new SteamUser.LogOnDetails()
                {
                    Username = pollResponse.AccountName,
                    AccessToken = pollResponse.RefreshToken,
                    ShouldRememberPassword = true,
                });
            }
            catch (Exception ex)
            {
                Logger?.Error("An error occurred during Steam authentication: {0}", ex);
                SteamClient?.Disconnect();
            }
        }

        private void DisconnectedCallback(SteamClient.DisconnectedCallback callback)
        {
            Logger?.Info("Disconnected from Steam.");
            Task.Run(async () => await ReconnectAsync());
        }

        private async Task ReconnectAsync(bool ignoreAdditionalDelay = true)
        {
            const int reconnectDelay = 5000;
            await Task.Delay(reconnectDelay);
            if (SteamClient == null || SteamClient?.IsConnected == true)
                return;
            Logger?.Info("Reconnecting to Steam...");
            try
            {
                SteamClient?.Connect();
            }
            catch (Exception ex)
            {
                Logger?.Error("An error occurred while reconnecting to Steam: {0}", ex);
                if (ignoreAdditionalDelay)
                    return;
                Logger?.Error("The reconnection will be delayed by a minute");
                await Task.Delay(60000 - reconnectDelay);
            }
        }

        private void LoggedOnCallback(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                IsLoggedOn = false;
                Logger?.Error("Unable to logon to Steam: {0} / {1}", callback.Result, callback.ExtendedResult);
                return;
            }
            IsLoggedOn = true;
            Logger?.Info("Successfully logged in to Steam as {0}!", Username ?? "N/A");
        }

        private void LoggedOffCallback(SteamUser.LoggedOffCallback callback)
        {
            IsLoggedOn = false;
            Logger?.Info("Logged off from Steam.");
        }

        public async Task<IMatchmakingLobby[]> GetLobbies(bool includePrivate = false)
        {
            List<Filter> filters = [
                    new DistanceFilter(ELobbyDistanceFilter.Worldwide),
                    new SlotsAvailableFilter(int.MaxValue),
                    new StringFilter(LobbyKeys.HasLobbyOpenKey, ELobbyComparison.Equal, bool.TrueString),
                    new StringFilter(LobbyKeys.IdentifierKey, ELobbyComparison.Equal, bool.TrueString),
                    new StringFilter(LobbyKeys.GameKey, ELobbyComparison.Equal, "BONELAB"),
                ];
            if (!includePrivate)
            {
                filters.Add(new NumericalFilter(LobbyKeys.PrivacyKey, ELobbyComparison.NotEqual, (int)ServerPrivacy.PRIVATE));
                filters.Add(new NumericalFilter(LobbyKeys.PrivacyKey, ELobbyComparison.NotEqual, (int)ServerPrivacy.LOCKED));
                filters.Add(new NumericalFilter(LobbyKeys.PrivacyKey, ELobbyComparison.NotEqual, (int)ServerPrivacy.FRIENDS_ONLY));
            }

            if (Matchmaking == null)
                return [];

            var task = Matchmaking.GetLobbyList(Fusion.AppID, filters, maxLobbies: int.MaxValue);
            GetLobbyListCallback? lobbies = null;
            if (task != null)
                lobbies = await task.ToTask().WaitAsync(CancellationToken.None);

            if (lobbies != null && lobbies.Result == EResult.OK)
                return [.. ProcessLobbies(lobbies.Lobbies)];
            return [];
        }

        private List<IMatchmakingLobby> ProcessLobbies(List<Lobby> lobbies)
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

            try
            {
                Logger.Info("Connecting to Steam...");
                SteamClient?.Connect();
            }
            catch (Exception ex)
            {
                Logger.Error("An error occurred while connecting to Steam: {0}", ex);
                Logger.Error("A reconnection attempt will be made in 30 seconds...");
                await Task.Delay(30000);
                while (!IsInitialized)
                    await ReconnectAsync(false);
            }
            while (!IsInitialized)
                await Task.Delay(250);
        }

        internal class SteamKitLobby(Lobby lobby, SteamClient? client) : IMatchmakingLobby
        {
            public ulong Owner => lobby?.OwnerSteamID?.ConvertToUInt64() ?? 0;

            public bool IsOwnerMe => client?.SteamID == lobby.OwnerSteamID;

            public string GetData(string key)
            {
                if (lobby.Metadata.ContainsKey(key))
                    return lobby.Metadata[key];
                return string.Empty;
            }

            public bool TryGetData(string key, out string value)
            {
                if (lobby.Metadata.ContainsKey(key))
                {
                    value = lobby.Metadata[key];
                    return !string.IsNullOrEmpty(value);
                }
                value = string.Empty;
                return false;
            }
        }

        public enum TwoFactorType
        {
            Email,
            Authenticator
        }
    }

    public class CustomUserAuth(Func<Task<bool>> acceptDeviceConfirmation, Func<bool, Task<string>> getDeviceCode, Func<string, bool, Task<string>> getEmailCode) : IAuthenticator
    {
        private Func<Task<bool>> AcceptDeviceConfirmation { get; } = acceptDeviceConfirmation;

        private Func<bool, Task<string>> GetDeviceCode { get; } = getDeviceCode;

        private Func<string, bool, Task<string>> GetEmailCode { get; } = getEmailCode;

        public async Task<bool> AcceptDeviceConfirmationAsync()
            => await AcceptDeviceConfirmation.Invoke();

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
            => await GetDeviceCode.Invoke(previousCodeWasIncorrect);

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
            => await GetEmailCode.Invoke(email, previousCodeWasIncorrect);
    }
}