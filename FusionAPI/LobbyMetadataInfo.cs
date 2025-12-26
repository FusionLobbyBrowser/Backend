using System.Text.Json;

using FusionAPI.Data.Containers;
using FusionAPI.Interfaces;

using Steamworks.Data;

namespace FusionAPI
{
    public class LobbyMetadataInfo
    {
        public LobbyInfo? LobbyInfo { get; set; }

        public bool HasServerOpen { get; set; }

        internal static LobbyMetadataInfo Read(IMatchmakingLobby lobby)
        {
            try
            {
                var info = new LobbyMetadataInfo()
                {
                    HasServerOpen = lobby.GetData(LobbyConstants.HasServerOpenKey) == bool.TrueString,
                };

                // Check if we can get the main lobby info
                if (TryGetData(lobby, nameof(LobbyInfo), out var json))
                {
                    try
                    {
                        info.LobbyInfo = JsonSerializer.Deserialize<LobbyInfo>(json);
                    }
                    catch
                    {
                        info.HasServerOpen = false;
                    }
                }
                else
                {
                    info.HasServerOpen = false;
                }

                return info;
            }
            catch (Exception)
            {
                return new LobbyMetadataInfo()
                {
                    HasServerOpen = false
                };
            }
        }

        internal static bool TryGetData(IMatchmakingLobby lobby, string key, out string value)
        {
            value = lobby.GetData(key);
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}