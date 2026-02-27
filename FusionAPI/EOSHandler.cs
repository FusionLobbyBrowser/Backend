using System.Collections;

using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

using FusionAPI.EOS.Auth;
using FusionAPI.EOS.Core;
using FusionAPI.Interfaces;

namespace FusionAPI
{
    public class EOSHandler : IMatchmakingHandler
    {
        public bool IsInitialized => EOSManager?.IsInitialized ?? false;

        public EOSManager EOSManager { get; private set; }

        public EOSAuthManager AuthManager { get; private set; }

        public ILogger Logger { get; private set; }

        public async Task<IMatchmakingLobby[]> GetLobbies(bool includePrivate = false)
        {
            var options = new CreateLobbySearchOptions()
            {
                MaxResults = uint.MaxValue
            };
            LobbySearch searchHandle = null;
            var res = EOSInterfaces.Lobby?.CreateLobbySearch(ref options, out searchHandle);

            if (searchHandle == null)
            {
                Logger.Error("LobbySearch handle is null after creation");
                return [];
            }

            if (res != Result.Success)
            {
                Logger.Error($"Failed to create lobby search handle: {res}");
                return [];
            }

            var findOptions = new LobbySearchFindOptions
            {
                LocalUserId = AuthManager.LocalUserId
            };

            List<IMatchmakingLobby> lobbies = [];
            bool finished = false;

            searchHandle?.Find(ref findOptions, null, (ref info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Logger.Error($"Failed to find lobbies: {info.ResultCode}");
                    finished = true;
                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                var lobbyCount = searchHandle.GetSearchResultCount(ref countOptions);

                if (lobbyCount == 0)
                {
                    Logger.Trace("No lobbies found.");
                    finished = true;
                    return;
                }
                var lobbyDetailsToProcess = new List<(uint Index, LobbyDetails Details)>((int)lobbyCount);

                // First pass: Copy all lobby details (fast)
                for (uint i = 0; i < lobbyCount; i++)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions
                    {
                        LobbyIndex = i
                    };

                    if (searchHandle.CopySearchResultByIndex(ref copyOptions, out var lobbyDetails) == Result.Success &&
                        lobbyDetails != null)
                    {
                        lobbyDetailsToProcess.Add((i, lobbyDetails));
                    }
                }

                // Second pass: Process lobby details (can be parallelized, but EOS callbacks are single-threaded)
                foreach (var (index, lobbyDetails) in lobbyDetailsToProcess)
                {
                    var lobbyInfo = ProcessSingleLobby(lobbyDetails);
                    if (lobbyInfo != null)
                    {
                        lobbies.Add(lobbyInfo);
                    }
                    else
                    {
                        lobbyDetails.Release();
                    }
                }
            });

            while (!finished)
                await Task.Delay(50);

            return [.. lobbies];
        }

        private IMatchmakingLobby? ProcessSingleLobby(LobbyDetails lobbyDetails)
        {
            // Quick validation: Check owner exists
            var ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
            var ownerId = lobbyDetails.GetLobbyOwner(ref ownerOptions);

            if (ownerId == null)
                return null; // Dead lobby

            // Get lobby info
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            if (lobbyDetails.CopyInfo(ref infoOptions, out var lobbyInfo) != Result.Success || !lobbyInfo.HasValue)
                return null;

            var networkLobby = new EpicLobby(lobbyDetails, this, lobbyInfo.Value.LobbyId);

            // Validate server is open
            if (!networkLobby.TryGetData(LobbyKeys.HasLobbyOpenKey, out var hasServerOpen) ||
                hasServerOpen != bool.TrueString)
                return null;

            // Read metadata

            var metadata = ReadMetadata(networkLobby);

#if !DEBUG
        if (metadata.LobbyInfo.LobbyHostID == PlayerIDManager.LocalPlatformID)
            return null;
#endif

            if (metadata == null)
                return null;

            if (!metadata.HasLobbyOpen)
                return null;

            return networkLobby;
        }

        private LobbyMetadataInfo ReadMetadata(IMatchmakingLobby lobby)
        {
            try
            {
                return LobbyMetadataInfo.Read(lobby);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read lobby metadata", ex);
                return new() { HasLobbyOpen = false };
            }
        }

        public async Task Init(ILogger logger, Dictionary<string, string> metadata)
        {
            Logger = logger;
            AuthManager = new EOSAuthManager(logger);
            EOSManager = new EOSManager(AuthManager, logger);

            bool finished = false;
            EOSManager.InitializeAsync((x) =>
            {
                finished = true;
                Logger.Info("EOS Initialized");
            });
            while (!finished)
                await Task.Delay(50);
        }

        public bool IsFriend(string id)
            => false;
    }

    internal class EpicLobby : IMatchmakingLobby
    {
        public string Owner => GetOwner();

        public bool IsOwnerMe => ((Utf8String)_eosHandler.AuthManager.LocalUserId) == Owner;

        private LobbyDetails _details;

        private EOSHandler _eosHandler;

        private string _lobbyId;

        public EpicLobby(LobbyDetails details, EOSHandler handler, string lobbyId)
        {
            _details = details;
            _eosHandler = handler;
            _lobbyId = lobbyId;
        }

        public bool TryGetData(string key, out string value)
        {
            value = string.Empty;

            if (_details == null)
                return false;

            var options = new LobbyDetailsCopyAttributeByKeyOptions
            {
                AttrKey = key
            };

            var result = _details.CopyAttributeByKey(ref options, out var attribute);

            if (result == Result.Success && attribute.HasValue)
            {
                value = attribute.Value.Data?.Value.AsUtf8 ?? string.Empty;
                return !string.IsNullOrEmpty(value);
            }

            return false;
        }

        public string GetData(string key)
        {
            TryGetData(key, out var value);
            return value;
        }

        private string GetOwner()
        {
            var options = new LobbyDetailsGetLobbyOwnerOptions();
            var id = _details?.GetLobbyOwner(ref options);
            if (id == null)
                return string.Empty;

            return (Utf8String)id;
        }
    }
}