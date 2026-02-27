using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

using FusionAPI.EOS.Core;
using FusionAPI.Interfaces;

using System.Collections;

namespace FusionAPI.EOS.Auth;

/// <summary>
/// Manages EOS authentication flow.
/// </summary>
public class EOSAuthManager
{
    private readonly EOSDeviceIdAuth _deviceIdAuth;

    public ProductUserId LocalUserId { get; private set; }

    private ILogger Logger { get; set; }

    public bool IsLoggedIn => LocalUserId != null;

    public EOSAuthManager(ILogger logger)
    {
        _deviceIdAuth = new EOSDeviceIdAuth(logger);
        Logger = logger;
    }

    public IEnumerator LoginAsync(Action<bool> onComplete)
    {
        // Step 1: Create device ID
        bool deviceIdSuccess = false;

        yield return _deviceIdAuth.CreateDeviceIdAsync(success => deviceIdSuccess = success);

        if (!deviceIdSuccess)
        {
            onComplete?.Invoke(false);
            yield break;
        }

        // Step 2: Login with device ID
        bool loginSuccess = false;

        yield return LoginWithDeviceIdAsync(success => loginSuccess = success);

        if (loginSuccess)
            RegisterAuthExpiration();

        onComplete?.Invoke(loginSuccess);
    }

    private IEnumerator LoginWithDeviceIdAsync(Action<bool> onComplete)
    {
        var connect = EOSInterfaces.Connect;
        if (connect == null)
        {
            Logger.Error("ConnectInterface is null when logging in");
            onComplete?.Invoke(false);
            yield break;
        }

        // Get username
        const string username = "FusionLobbyBrowser";

        // Attempt login
        bool finished = false;
        bool success = false;
        ContinuanceToken continuanceToken = null;

        var loginOptions = new LoginOptions
        {
            Credentials = new Credentials
            {
                Type = ExternalCredentialType.DeviceidAccessToken,
                Token = null,
            },
            UserLoginInfo = new UserLoginInfo
            {
                DisplayName = username
            },
        };

        connect.Login(ref loginOptions, null, (ref LoginCallbackInfo data) =>
        {
            switch (data.ResultCode)
            {
                case Result.Success:
                    LocalUserId = data.LocalUserId;
#if DEBUG
                    Logger.Info($"Logged in successfully! PUID = {LocalUserId}");
#endif
                    success = true;
                    finished = true;
                    break;

                case Result.InvalidUser:
                    continuanceToken = data.ContinuanceToken;
                    break;

                default:
                    Logger.Error($"Login failed: {data.ResultCode}");
                    finished = true;
                    break;
            }
        });

        while (!finished && continuanceToken == null)
            yield return null;

        // Create user if needed
        if (continuanceToken != null)
        {
            yield return CreateUserAsync(continuanceToken, result =>
            {
                success = result;
                finished = true;
            });
        }

        while (!finished)
            yield return null;

        onComplete?.Invoke(success);
    }

    private IEnumerator CreateUserAsync(ContinuanceToken token, Action<bool> onComplete)
    {
        var connect = EOSInterfaces.Connect;
        if (connect == null)
        {
            Logger.Error("ConnectInterface is null when creating user");
            onComplete?.Invoke(false);
            yield break;
        }

        bool finished = false;
        bool success = false;

        var options = new CreateUserOptions
        {
            ContinuanceToken = token
        };

        connect.CreateUser(ref options, null, (ref CreateUserCallbackInfo data) =>
        {
            if (data.ResultCode == Result.Success)
            {
                LocalUserId = data.LocalUserId;
                Logger.Info($"User created successfully! PUID = {LocalUserId}");
                success = true;
            }
            else
            {
                Logger.Error($"CreateUser failed: {data.ResultCode}");
            }
            finished = true;
        });

        while (!finished)
            yield return null;

        onComplete?.Invoke(success);
    }

    private void RegisterAuthExpiration()
    {
        var notifyAuthExpirationOptions = new AddNotifyAuthExpirationOptions();
        EOSInterfaces.Connect.AddNotifyAuthExpiration(ref notifyAuthExpirationOptions, null, AuthExpirationCallback);
    }

    private void AuthExpirationCallback(ref AuthExpirationCallbackInfo data)
    {
        var loginOptions = new LoginOptions
        {
            Credentials = new Credentials
            {
                Type = ExternalCredentialType.DeviceidAccessToken,
                Token = null,
            },
            UserLoginInfo = new UserLoginInfo
            {
                DisplayName = "Fusion Lobby Browser"
            },
        };

        EOSInterfaces.Connect.Login(ref loginOptions, null, (ref LoginCallbackInfo data) =>
        {
            switch (data.ResultCode)
            {
                case Result.Success:
#if DEBUG
                    Logger.Info("Token refreshed!");
#endif
                    break;

                default:
                    Logger.Error($"Token refresh failed with result: {data.ResultCode}");
                    break;
            }
        });
    }
}