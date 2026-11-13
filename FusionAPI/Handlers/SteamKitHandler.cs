using FusionAPI.Data;
using FusionAPI.Data.Enums;
using FusionAPI.Data.Interfaces;

using SteamKit2;
using SteamKit2.Authentication;

using static SteamKit2.SteamMatchmaking;
using static SteamKit2.SteamMatchmaking.Lobby;

namespace FusionAPI.Handlers
{
    public class SteamKitHandler : IMatchmakingHandler
    {
        public SteamClient? SteamClient { get; }

        public CallbackManager? CallbackManager { get; }

        public SteamUser? SteamUser { get; }

        private IReadOnlyList<SteamFriends.FriendsListCallback.Friend>? FriendsList { get; set; }

        private SteamMatchmaking? Matchmaking { get; }

        private string? Username { get; set; }

        private string? Password { get; set; }

        public bool IsLoggedOn { get; private set; }

        public bool IsInitialized => SteamClient?.IsConnected == true && IsLoggedOn;

        public Func<TwoFactorType, string>? TwoFactorRequired { get; set; }

        public IAuthenticator? Authenticator { get; set; }

        public ILogger? Logger { get; private set; }

        private string? _previouslyStoredGuardData;

        private long ReconnectTime { get; set; } = -1;

        public DateTimeOffset LastFetch { get; private set; } = DateTimeOffset.UtcNow;

        private CancellationTokenSource? CancellationTokenSource { get; set; }

        private CancellationTokenSource? DeInitCancel { get; set; }

        public string ID => "Steam";

        public SteamKitHandler()
        {
            SteamClient = new SteamClient();
            Matchmaking = SteamClient.GetHandler<SteamMatchmaking>();

            SteamUser = SteamClient.GetHandler<SteamUser>() ?? throw new InvalidOperationException("Failed to get SteamUser handler from SteamKit!");
            CallbackManager = new CallbackManager(SteamClient);
            CallbackManager.Subscribe<SteamClient.ConnectedCallback>(ConnectedCallback);
            CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(DisconnectedCallback);
            CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(LoggedOnCallback);
            CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(LoggedOffCallback);
            CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(FriendsListCallback);
        }

        private async Task? CallbackLoop()
        {
            if (CancellationTokenSource == null)
                return;

            var copy = CancellationTokenSource.Token;
            while (!copy.IsCancellationRequested)
            {
                // In order for the callbacks to get routed, they need to be handled by the manager
                CallbackManager?.RunCallbacks();
                await Task.Delay(50, copy);
            }
        }

        private void FriendsListCallback(SteamFriends.FriendsListCallback callback)
        {
            FriendsList = callback.FriendList;
        }

        private async Task ConnectedCallback(SteamClient.ConnectedCallback callback)
        {
            try
            {
                if (CancellationTokenSource == null)
                    return;

                var copy = CancellationTokenSource.Token;

                Logger?.Info("Connected to Steam, logging in as {0}...", Username ?? "N/A");

                if (SteamClient == null)
                    return;

                var authSession = await SteamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = Username,
                    Password = Password,
                    ClientOSType = EOSType.Win11,
                    DeviceFriendlyName = "Fusion Lobby Browser",
                    IsPersistentSession = true,
                    PlatformType = SteamKit2.Internal.EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
                    Authenticator = Authenticator ?? new UserConsoleAuthenticator(),
                    GuardData = _previouslyStoredGuardData,
                });

                var pollResponse = await authSession.PollingWaitForResultAsync(copy);
                if (pollResponse.NewGuardData != null)
                    _previouslyStoredGuardData = pollResponse.NewGuardData;

                if (copy.IsCancellationRequested)
                    return;

                SteamUser?.LogOn(new SteamUser.LogOnDetails()
                {
                    Username = pollResponse.AccountName,
                    AccessToken = pollResponse.RefreshToken,
                    ShouldRememberPassword = true,
                });
            }
            catch (AuthenticationException ex)
            {
                if (ex.Result == EResult.RateLimitExceeded)
                {
                    Logger?.Error("Authentication failed due to rate limit, retrying after 5 mins");
                    ReconnectTime = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();
                }
                else
                {
                    Logger?.Error("Authentication failed: {0}", ex);
                }

                SteamClient?.Disconnect();
            }
            catch (Exception ex)
            {
                Logger?.Error("An error occurred during Steam authentication: {0}", ex);
                SteamClient?.Disconnect();
            }
        }

        private async Task DisconnectedCallback(SteamClient.DisconnectedCallback callback)
        {
            Logger?.Info("Disconnected from Steam.");
            if (!callback.UserInitiated)
                await ReconnectAsync();
        }

        private async Task ReconnectAsync(bool ignoreAdditionalDelay = true)
        {
            if (CancellationTokenSource == null)
                return;

            var copy = CancellationTokenSource.Token;
            const int reconnectDelay = 5000;
            if (ReconnectTime > 0)
            {
                await Task.Delay(reconnectDelay, copy);
            }
            else
            {
                while (ReconnectTime > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < ReconnectTime)
                    await Task.Delay(50, copy);
            }
            if (SteamClient == null || SteamClient?.IsConnected == true || copy.IsCancellationRequested)
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
                await Task.Delay(60000 - reconnectDelay, copy);
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
            Logger?.Info("Logged in to Steam as {0}!", Username ?? "N/A");
        }

        private void LoggedOffCallback(SteamUser.LoggedOffCallback callback)
        {
            IsLoggedOn = false;
            Logger?.Info("Logged off from Steam.");
        }

        public async Task<IMatchmakingLobby[]> GetLobbies(bool publicLobbies = true, bool friendsOnlyLobbies = false)
        {
            List<Filter> filters = [
                    new DistanceFilter(ELobbyDistanceFilter.Worldwide),
                    new SlotsAvailableFilter(int.MaxValue),
                    new StringFilter(LobbyKeys.HAS_LOBBY_OPEN_KEY, ELobbyComparison.Equal, bool.TrueString),
                    new StringFilter(LobbyKeys.IDENTIFIER_KEY, ELobbyComparison.Equal, bool.TrueString),
                    new StringFilter(LobbyKeys.GAME_KEY, ELobbyComparison.Equal, "BONELAB"),
                ];
            if (publicLobbies)
                filters.Add(new NumericalFilter(LobbyKeys.PRIVACY_KEY, ELobbyComparison.Equal, (int)ServerPrivacy.PUBLIC));
            if (friendsOnlyLobbies)
                filters.Add(new NumericalFilter(LobbyKeys.PRIVACY_KEY, ELobbyComparison.Equal, (int)ServerPrivacy.FRIENDS_ONLY));

            if (Matchmaking == null)
                return [];

            var task = Matchmaking.GetLobbyList(Fusion.APP_ID, filters, maxLobbies: int.MaxValue);
            GetLobbyListCallback? lobbies = null;
            if (task != null)
                lobbies = await task.ToTask();

            if (lobbies is not { Result: EResult.OK })
                return [];
            LastFetch = DateTimeOffset.UtcNow;
            IMatchmakingLobby[] processed = [.. ProcessLobbies(lobbies.Lobbies)];
            return processed;
        }

        private List<IMatchmakingLobby> ProcessLobbies(List<Lobby> lobbies)
        {
            var list = new List<IMatchmakingLobby>();
            lobbies.ForEach(lobby => list.Add(new SteamKitLobby(lobby, SteamClient)));
            return list;
        }

        public async Task Init(ILogger? logger, Dictionary<string, string> metadata)
        {
            if (CancellationTokenSource != null)
                await CancellationTokenSource.CancelAsync();

            if (DeInitCancel != null)
                await DeInitCancel.CancelAsync();

            CancellationTokenSource = new CancellationTokenSource();
            var copy = CancellationTokenSource.Token;
            if (!metadata.TryGetValue("username", out var value1) || !metadata.TryGetValue("password", out string? value))
                throw new AuthenticationException("SteamKitHandler requires 'username' and 'password' in metadata to initialize!");

            Logger = logger;
            Username = value1;
            Password = value;
            if (SteamClient?.IsConnected == true)
                SteamClient.Disconnect();

            try
            {
                Logger?.Info("Connecting to Steam...");
                SteamClient?.Connect();
            }
            catch (Exception ex)
            {
                Logger?.Error("An error occurred while connecting to Steam: {0}", ex);
                Logger?.Error("A reconnection attempt will be made in 30 seconds...");
                await Task.Delay(30000, copy);
                while (!IsInitialized && !copy.IsCancellationRequested)
                    await ReconnectAsync(false);
            }

            _ = Task.Run(CallbackLoop, copy);

            while (!IsInitialized)
                await Task.Delay(250, copy);
        }

        public async Task DeInit()
        {
            if (DeInitCancel != null)
                await DeInitCancel.CancelAsync();
            DeInitCancel = new CancellationTokenSource();
            var copy = DeInitCancel.Token;

            if (CancellationTokenSource != null)
                await CancellationTokenSource.CancelAsync();
            SteamClient?.Disconnect();

            while (IsInitialized && !copy.IsCancellationRequested)
                await Task.Delay(250, copy);
        }

        internal class SteamKitLobby(Lobby lobby, SteamClient? client) : IMatchmakingLobby
        {
            public string Owner => lobby?.OwnerSteamID?.ToString() ?? "0";

            public bool IsOwnerMe => client?.SteamID == lobby.OwnerSteamID;

            public string GetData(string key)
            {
                return lobby.Metadata.TryGetValue(key, out var data) ? data : string.Empty;
            }

            public bool TryGetData(string key, out string value)
            {
                if (lobby.Metadata.TryGetValue(key, out var data))
                {
                    value = data;
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