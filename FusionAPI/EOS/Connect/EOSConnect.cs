using System.Collections;

using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

using FusionAPI.Interfaces;

namespace FusionAPI.EOS.Connect;

internal class EOSConnect : EOSInterface
{
    internal ConnectInterface ConnectInterface;
    internal ProductUserId LocalUserId;
    internal string LocalDisplayName;
    internal ulong ExpirationNotificationId;

    internal ILogger Logger;

    internal EOSConnect(ConnectInterface connectInterface)
    {
        ConnectInterface = connectInterface;
    }

    internal override async Task<bool> InitializeAsync(ILogger logger, ThreadDispatcher dispatcher)
    {
        Logger = logger;
        var loginSuccess = await LoginAsync();
        return loginSuccess;
    }

    private async Task<bool> LoginAsync()
    {
        var createDeviceIdOptions = new CreateDeviceIdOptions
        {
            DeviceModel = Environment.MachineName
        };

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConnectInterface.CreateDeviceId(ref createDeviceIdOptions, null, ((ref CreateDeviceIdCallbackInfo data) =>
        {
            if (data.ResultCode is Result.Success or Result.DuplicateNotAllowed)
            {
                tcs.SetResult(string.Empty);
            }
            else
            {
                tcs.SetResult(null);
                Logger.Error($"CreateDeviceId failed: {data.ResultCode}");
            }
        }));

        var token = await tcs.Task;

        if (token == null)
        {
            Logger.Error($"Failed to retrieve token");
            return false;
        }

        LocalDisplayName = "FLB";

        var loginOptions = new LoginOptions
        {
            Credentials = new Credentials
            {
                Type = ExternalCredentialType.DeviceidAccessToken,
                Token = string.Empty,
            },
            UserLoginInfo = new UserLoginInfo
            {
                DisplayName = string.IsNullOrWhiteSpace(LocalDisplayName) ? "Unknown" : LocalDisplayName
            }
        };

        var loginTcs = new TaskCompletionSource<(bool success, ContinuanceToken? continuance)>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConnectInterface.Login(ref loginOptions, null, (ref LoginCallbackInfo data) =>
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

        if (continuanceToken != null)
        {
            var createUserOptions = new CreateUserOptions
            {
                ContinuanceToken = continuanceToken
            };

            var createUserFinished = false;
            var connnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConnectInterface.CreateUser(ref createUserOptions, null, (ref CreateUserCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    LocalUserId = data.LocalUserId;
                    connnectTcs.SetResult(true);
                }
                else
                {
                    Logger.Error($"CreateUser failed: {data.ResultCode}");
                    connnectTcs.SetResult(false);
                }

                createUserFinished = true;
            });

            await connnectTcs.Task;
        }

        var success = LocalUserId != null;
        if (success)
            RegisterAuthExpiration();

        return success;
    }

    private void RegisterAuthExpiration()
    {
        UnregisterAuthExpiration();
        var options = new AddNotifyAuthExpirationOptions();
        ExpirationNotificationId = ConnectInterface.AddNotifyAuthExpiration(ref options, null, (ref AuthExpirationCallbackInfo _) => RefreshTokenAsync().ConfigureAwait(false));
        return;

        async Task RefreshTokenAsync()
        {
            var success = await LoginAsync();
            if (success)
            {
                Logger.Info("EOS token refreshed successfully.");
                RegisterAuthExpiration();
            }
            else
            {
                Logger.Error("EOS token refresh failed - attempting to re-authenticate...");
                LocalUserId = null;
                var authSuccess = await LoginAsync();
                if (authSuccess)
                {
                    Logger.Info($"Logged back in successfully!");
                }
                else
                {
                    Logger.Error("Failed to re-authenticate");
                    LocalUserId = null;
                }
            }
        }
    }

    private void UnregisterAuthExpiration()
    {
        if (ExpirationNotificationId == Common.INVALID_NOTIFICATIONID)
            return;

        ConnectInterface.RemoveNotifyAuthExpiration(ExpirationNotificationId);
        ExpirationNotificationId = Common.INVALID_NOTIFICATIONID;
    }
}