using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

using FusionAPI.EOS.Core;

namespace FusionAPI.EOS.Auth;

internal class EOSOculusAuth : EOSAuthInterface
{
    internal override ExternalAccountType AccountType => ExternalAccountType.Oculus;

    internal override ExternalCredentialType CredentialType => ExternalCredentialType.DeviceidAccessToken;

    internal override bool AllowNullToken => true;

    internal override bool LoginWithDisplayName => true;

    internal override Task<string?> GetDisplayNameAsync()
    {
        return Task.FromResult<string?>("Fusion Lobby Browser");
    }

    internal override Task<string?> GetLoginTicketAsync()
    {
        var connect = EOSInterfaces.Connect;
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var createOptions = new CreateDeviceIdOptions
        {
            DeviceModel = Environment.MachineName,
        };

        connect.CreateDeviceId(ref createOptions, null, (ref CreateDeviceIdCallbackInfo data) =>
        {
            if (data.ResultCode == Result.Success || data.ResultCode == Result.DuplicateNotAllowed)
            {
                tcs.SetResult(string.Empty); // DeviceId auth passes an empty token
            }
            else
            {
                tcs.SetResult(null);
                throw new InvalidOperationException($"Failed to create device ID for Oculus authentication: {data.ResultCode}");
            }
        });

        return tcs.Task;
    }
}