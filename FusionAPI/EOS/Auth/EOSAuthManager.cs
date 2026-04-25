using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

using FusionAPI.EOS.Core;
using FusionAPI.Interfaces;

namespace FusionAPI.EOS.Auth;

internal class EOSAuthManager
{
    internal const string UnknownDisplayName = "Unknown";

    internal ProductUserId? LocalUserId { get; private set; }

    private readonly EOSAuthInterface _authInterface;
    private bool IsLoggedIn => LocalUserId != null;
    private ulong _expirationNotificationId;

    private ILogger Logger { get; }

    internal EOSAuthManager(ILogger logger)
    {
        _authInterface = new EOSOculusAuth();
        Logger = logger;
    }

    internal async Task<bool> LoginAsync()
    {
        bool success = await LoginWithInterfaceAsync();

        if (success)
        {
            RegisterAuthExpiration();

            Logger.Info($"Logged in successfully! PUID = {LocalUserId}");
        }

        return success;
    }

    internal void Shutdown()
    {
        _authInterface.OnShutdown();
        UnregisterAuthExpiration();
    }

    internal async Task<string> GetDisplayNameAsync()
    {
        if (!IsLoggedIn)
            return UnknownDisplayName;

        string? displayName = await _authInterface.GetDisplayNameAsync();
        return !string.IsNullOrEmpty(displayName) ? displayName! : UnknownDisplayName;
    }

    private async Task<bool> LoginWithInterfaceAsync()
    {
        var connect = EOSInterfaces.Connect;
        if (connect == null)
        {
            Logger.Error("ConnectInterface is null");
            return false;
        }

        string? platformToken = await _authInterface.GetLoginTicketAsync();

        if (!_authInterface.AllowNullToken && string.IsNullOrEmpty(platformToken))
        {
            Logger.Error($"Failed to retrieve token for {_authInterface.AccountType}");
            return false;
        }

        var loginOptions = new LoginOptions
        {
            Credentials = new Credentials
            {
                Type = _authInterface.CredentialType,
                Token = platformToken,
            }
        };

        if (_authInterface.LoginWithDisplayName)
        {
            string displayName = await GetDisplayNameAsync();

            loginOptions.UserLoginInfo = new UserLoginInfo
            {
                DisplayName = displayName
            };
        }

        var loginTcs = new TaskCompletionSource<(bool success, ContinuanceToken? continuance)>(TaskCreationOptions.RunContinuationsAsynchronously);

        connect.Login(ref loginOptions, null, (ref LoginCallbackInfo data) =>
        {
            switch (data.ResultCode)
            {
                case Result.Success:
                    LocalUserId = data.LocalUserId;
                    loginTcs.SetResult((true, null));
                    break;

                case Result.InvalidUser:
                    loginTcs.SetResult((false, data.ContinuanceToken));
                    break;

                default:
                    Logger.Error($"EOS Login failed: {data.ResultCode}");
                    loginTcs.SetResult((false, null));
                    break;
            }
        });

        var (loginSuccess, continuanceToken) = await loginTcs.Task;

        if (loginSuccess)
            return true;

        if (continuanceToken != null)
            return await CreateUserAsync(continuanceToken);

        return false;
    }

    private async Task<bool> CreateUserAsync(ContinuanceToken token)
    {
        var connect = EOSInterfaces.Connect;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new CreateUserOptions { ContinuanceToken = token };

        connect.CreateUser(ref options, null, (ref CreateUserCallbackInfo data) =>
        {
            if (data.ResultCode == Result.Success)
            {
                LocalUserId = data.LocalUserId;
                tcs.SetResult(true);
            }
            else
            {
                Logger.Error($"EOS CreateUser failed: {data.ResultCode}");
                tcs.SetResult(false);
            }
        });

        return await tcs.Task;
    }

    private void RegisterAuthExpiration()
    {
        UnregisterAuthExpiration();

        var expirationOptions = new AddNotifyAuthExpirationOptions();
        _expirationNotificationId = EOSInterfaces.Connect.AddNotifyAuthExpiration(
            ref expirationOptions, null,
            (ref AuthExpirationCallbackInfo _) =>
            {
                Logger.Info("EOS token expiring - starting refresh...");

                RefreshTokenAsync();
            }
        );
    }

    private void UnregisterAuthExpiration()
    {
        if (_expirationNotificationId != 0)
        {
            EOSInterfaces.Connect.RemoveNotifyAuthExpiration(_expirationNotificationId);
            _expirationNotificationId = 0;
        }
    }

    private async Task RefreshTokenAsync()
    {
        Logger.Info("Refreshing EOS token...");

        bool success = await LoginWithInterfaceAsync();

        if (success)
        {
            Logger.Info("EOS token refreshed successfully.");
            RegisterAuthExpiration();
        }
        else
        {
            Logger.Error("EOS token refresh failed - user may need to re-authenticate.");
            LocalUserId = null;
        }
    }
}