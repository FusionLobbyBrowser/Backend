using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FusionAPI
{
    public static class LobbyKeys
    {
        /// <summary>
        /// The key to identify that this is a Fusion lobby.
        /// </summary>
        public const string IDENTIFIER_KEY = "MarrowFusion";

        /// <summary>
        /// The key to identify that the lobby is open and joinable.
        /// </summary>
        public const string HAS_LOBBY_OPEN_KEY = "HasLobbyOpen";

        /// <summary>
        /// The key to identify the array containing all keys for the lobby.
        /// </summary>
        public const string KEY_COLLECTION_KEY = "KeyCollection";

        /// <summary>
        /// The key for a lobby's code. The value should always be uppercase to allow for case insensitivity.
        /// </summary>
        public const string LOBBY_CODE_KEY = "LobbyCode";

        /// <summary>
        /// The key for a lobby's privacy.
        /// </summary>
        public const string PRIVACY_KEY = "Privacy";

        /// <summary>
        /// The key to get if a lobby is full.
        /// </summary>
        public const string FULL_KEY = "Full";

        /// <summary>
        /// The key for a lobby's major version.
        /// </summary>
        public const string VERSION_MAJOR_KEY = "VersionMajor";

        /// <summary>
        /// The key for a lobby's minor version.
        /// </summary>
        public const string VERSION_MINOR_KEY = "VersionMinor";

        /// <summary>
        /// The key for a lobby's game.
        /// </summary>
        public const string GAME_KEY = "Game";
    }
}