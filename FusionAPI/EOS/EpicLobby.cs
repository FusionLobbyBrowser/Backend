using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace FusionAPI.EOS;

internal class EpicLobby : EpicInterface
{
    internal EpicRuntime Runtime;
    internal LobbyInterface LobbyInterface;
    internal ProductUserId? LocalUserId;

    internal EpicLobby(EpicRuntime eosRuntime, LobbyInterface lobbyInterface, ProductUserId? localUserId)
    {
        Runtime = eosRuntime;
        LobbyInterface = lobbyInterface;
        LocalUserId = localUserId;
    }

    internal static string GetAttribute(LobbyDetails? lobbyDetails, string key)
    {
        if (lobbyDetails == null)
            return string.Empty;

        var lobbyDetailsCopyAttributeByKeyOptions = new LobbyDetailsCopyAttributeByKeyOptions
        {
            AttrKey = key
        };

        var result = lobbyDetails.CopyAttributeByKey(ref lobbyDetailsCopyAttributeByKeyOptions, out var attribute);
        if (result == Result.Success && attribute.HasValue)
            return attribute.Value.Data?.Value.AsUtf8 ?? string.Empty;

        return string.Empty;
    }
}