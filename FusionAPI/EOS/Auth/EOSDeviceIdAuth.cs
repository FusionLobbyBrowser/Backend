using Epic.OnlineServices;
using Epic.OnlineServices.Connect;

using FusionAPI.EOS.Core;
using FusionAPI.Interfaces;

namespace FusionAPI.EOS.Auth;

/// <summary>
/// Handles EOS Device ID creation and management.
/// </summary>
public class EOSDeviceIdAuth
{
    private ILogger Logger { get; set; }

    public EOSDeviceIdAuth(ILogger logger)
        => Logger = logger;

    public async Task<bool> CreateDeviceIdAsync()
    {
        var connect = EOSInterfaces.Connect;
        if (connect == null)
        {
            Logger.Error("ConnectInterface is null when creating device ID");
            return false;
        }

        bool finished = false;
        bool success = false;

        var options = new CreateDeviceIdOptions
        {
            DeviceModel = GetDeviceModel(),
        };

        connect.CreateDeviceId(ref options, null, (ref CreateDeviceIdCallbackInfo data) =>
        {
            success = data.ResultCode == Result.Success ||
                      data.ResultCode == Result.DuplicateNotAllowed;

            if (!success)
            {
                Logger.Error($"CreateDeviceId failed: {data.ResultCode}");
            }
            else
            {
                Logger.Trace("CreateDeviceId succeeded");
            }

            finished = true;
        });

        while (!finished)
            await Task.Yield();

        return success;
    }

    private static string GetDeviceModel()
        => "PC";
}