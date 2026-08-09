using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace FusionAPI.EOS.Lobby;

internal class EOSLobby : EOSInterface
{
    internal EOSRuntime Runtime;
    internal LobbyInterface LobbyInterface;
    internal ProductUserId LocalUserId;

    internal EOSLobby(EOSRuntime eosRuntime, LobbyInterface lobbyInterface, ProductUserId localUserId)
    {
        Runtime = eosRuntime;
        LobbyInterface = lobbyInterface;
        LocalUserId = localUserId;
    }

    internal string GetAttribute(LobbyDetails lobbyDetails, string key)
    {
        var lobbyDetailsCopyAttributeByKeyOptions = new LobbyDetailsCopyAttributeByKeyOptions
        {
            AttrKey = key
        };

        var result = lobbyDetails.CopyAttributeByKey(ref lobbyDetailsCopyAttributeByKeyOptions, out var attribute);
        if (result == Result.Success && attribute.HasValue)
        {
            return attribute.Value.Data?.Value.AsUtf8 ?? string.Empty;
        }

        return string.Empty;
    }
}