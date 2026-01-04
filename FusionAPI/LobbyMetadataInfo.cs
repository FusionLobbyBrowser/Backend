using System.Text.Json;

using FusionAPI.Data.Containers;
using FusionAPI.Data.Enums;
using FusionAPI.Interfaces;

using Steamworks.Data;

namespace FusionAPI
{
    public class LobbyMetadataInfo
    {
        public static readonly LobbyMetadataInfo Empty = new()
        {
            LobbyInfo = null,
            HasLobbyOpen = false,
            ClientHasLevel = false,
            LobbyCode = null,
            Privacy = ServerPrivacy.PUBLIC,
            Full = false,
            VersionMajor = 0,
            VersionMinor = 0,
            Game = null,
        };

        public LobbyInfo LobbyInfo { get; set; }

        public bool HasLobbyOpen { get; set; }

        public bool ClientHasLevel { get; set; }

        public string LobbyCode { get; set; }

        public ServerPrivacy Privacy { get; set; }

        public bool Full { get; set; }

        public int VersionMajor { get; set; }

        public int VersionMinor { get; set; }

        public string Game { get; set; }

        public static LobbyMetadataInfo Read(IMatchmakingLobby lobby)
        {
            var info = new LobbyMetadataInfo()
            {
                HasLobbyOpen = lobby.GetData(LobbyKeys.HasLobbyOpenKey) == bool.TrueString,
                LobbyCode = lobby.GetData(LobbyKeys.LobbyCodeKey),
                Game = lobby.GetData(LobbyKeys.GameKey),
                Full = lobby.GetData(LobbyKeys.FullKey) == bool.TrueString,
            };

            if (lobby.TryGetData(LobbyKeys.PrivacyKey, out var rawPrivacy) && int.TryParse(rawPrivacy, out var privacyInt))
            {
                info.Privacy = (ServerPrivacy)privacyInt;
            }

            if (lobby.TryGetData(LobbyKeys.VersionMajorKey, out var rawVersionMajor) && int.TryParse(rawVersionMajor, out var versionMajorInt))
            {
                info.VersionMajor = versionMajorInt;
            }

            if (lobby.TryGetData(LobbyKeys.VersionMinorKey, out var rawVersionMinor) && int.TryParse(rawVersionMinor, out var versionMinorInt))
            {
                info.VersionMinor = versionMinorInt;
            }

            // Check if we can get the main lobby info
            if (lobby.TryGetData(nameof(LobbyInfo), out var json))
            {
                try
                {
                    info.LobbyInfo = JsonSerializer.Deserialize<LobbyInfo>(json);
                }
                catch
                {
                    info.HasLobbyOpen = false;
                }
            }
            else
            {
                info.HasLobbyOpen = false;
            }

            return info;
        }

        internal static bool TryGetData(IMatchmakingLobby lobby, string key, out string value)
        {
            value = lobby.GetData(key);
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}