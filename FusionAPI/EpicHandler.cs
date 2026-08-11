using System.Reflection;
using System.Runtime.InteropServices;

using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

using FusionAPI.Data.Enums;
using FusionAPI.EOS;
using FusionAPI.Epic;
using FusionAPI.Interfaces;

namespace FusionAPI
{
    public class EOSHandler : IMatchmakingHandler
    {
        private static bool DLLResolverConfigured { get; set; } = false;

        public bool IsInitialized => Runtime.IsInitialized;

        internal EOSRuntime Runtime { get; private set; }

        public ILogger Logger { get; private set; }

        private DateTimeOffset _lastFetch = DateTimeOffset.UtcNow;

        public DateTimeOffset LastFetch => _lastFetch;

        private IntPtr EOSHandle { get; set; } = IntPtr.Zero;

        public string ID => "Epic";

        public async Task<IMatchmakingLobby[]> GetLobbies(bool publicLobbies = true, bool friendsOnlyLobbies = false)
        {
            var createLobbySearchOptions = new CreateLobbySearchOptions
            {
                MaxResults = 200
            };

            var result = Runtime.Lobby.LobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out var searchHandle);
            if (result != Result.Success || searchHandle == null)
            {
                Logger.Error($"Failed to create lobby search: {result}");
                return [];
            }

            SetParameter(ref searchHandle, LobbyKeys.HAS_LOBBY_OPEN_KEY, bool.TrueString, ComparisonOp.Equal);
            SetParameter(ref searchHandle, LobbyKeys.IDENTIFIER_KEY, bool.TrueString, ComparisonOp.Equal);
            SetParameter(ref searchHandle, LobbyKeys.GAME_KEY, "BONELAB", ComparisonOp.Equal);

            SetParameter(ref searchHandle, LobbyKeys.PRIVACY_KEY, ((int)ServerPrivacy.PUBLIC).ToString(), ComparisonOp.Equal);

            var lobbySearchFindOptions = new LobbySearchFindOptions
            {
                LocalUserId = Runtime.Connect.LocalUserId
            };

            var tcs = new TaskCompletionSource<IMatchmakingLobby[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            searchHandle.Find(ref lobbySearchFindOptions, null, (ref LobbySearchFindCallbackInfo info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Logger.Error($"EOS lobby search failed: {info.ResultCode}");
                    if (!tcs.TrySetResult([]))
                        Logger.Error($"Failed to set result, current task state: {Enum.GetName(tcs.Task.Status)}");
                    searchHandle.Release();
                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                var lobbyCount = searchHandle.GetSearchResultCount(ref countOptions);

#if DEBUG
                Logger.Info($"Lobbies Found: {lobbyCount}");
#endif

                List<IMatchmakingLobby> lobbies = new((int)lobbyCount);

                for (uint i = 0; i < lobbyCount; i++)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions
                    {
                        LobbyIndex = i
                    };

                    if (searchHandle.CopySearchResultByIndex(ref copyOptions, out var lobbyDetails) != Result.Success || lobbyDetails == null)
                        continue;

                    var infoOptions = new LobbyDetailsCopyInfoOptions();
                    if (lobbyDetails.CopyInfo(ref infoOptions, out var lobbyInfo) != Result.Success || !lobbyInfo.HasValue || lobbyInfo.Value.LobbyOwnerUserId == null)
                    {
                        lobbyDetails.Release();
                        continue;
                    }

                    var processed = ProcessSingleLobby(lobbyDetails, publicLobbies, friendsOnlyLobbies);
                    if (processed != null)
                        lobbies.Add(processed);
                }

                searchHandle.Release();

                if (!tcs.TrySetResult([.. lobbies]))
                    Logger.Error($"Failed to set result, current task state: {Enum.GetName(tcs.Task.Status)}");
            });

            _lastFetch = DateTimeOffset.UtcNow;

            return await tcs.Task;
        }

        private void SetParameter(ref LobbySearch searchHandle, string key, string value, ComparisonOp comparisonOp)
        {
            var lobbySearchSetParameterOptions = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData
                {
                    Key = key,
                    Value = value,
                },
                ComparisonOp = comparisonOp,
            };

            var result = searchHandle.SetParameter(ref lobbySearchSetParameterOptions);
            if (result != Result.Success)
                Logger.Error($"Failed to set lobby search parameter: {result}");
        }

        private EpicLobby? ProcessSingleLobby(LobbyDetails lobbyDetails, bool publicLobbies = true, bool friendsOnlyLobbies = false)
        {
            var ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
            var ownerId = lobbyDetails.GetLobbyOwner(ref ownerOptions);

            if (ownerId == null)
            {
                lobbyDetails.Release();
                return null;
            }

            var infoOptions = new LobbyDetailsCopyInfoOptions();
            if (lobbyDetails.CopyInfo(ref infoOptions, out var lobbyInfo) != Result.Success || !lobbyInfo.HasValue)
                return null;

            var networkLobby = new EpicLobby(Runtime, lobbyDetails, ownerId);

            if (!networkLobby.TryGetData(LobbyKeys.HAS_LOBBY_OPEN_KEY, out var hasServerOpen) ||
                hasServerOpen != bool.TrueString)
            {
                networkLobby.Dispose();
                return null;
            }

            var metadata = ReadMetadata(networkLobby);

            if (metadata == null)
            {
                networkLobby.Dispose();
                return null;
            }

            if (!metadata.HasLobbyOpen)
            {
                networkLobby.Dispose();
                return null;
            }

            return networkLobby;
        }

        private LobbyMetadataInfo? ReadMetadata(IMatchmakingLobby lobby)
        {
            try
            {
                return LobbyMetadataInfo.Read(lobby);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read lobby metadata", ex);
                return null;
            }
        }

        public async Task Init(ILogger logger, Dictionary<string, string> metadata)
        {
            Logger = logger;

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string name;

            const string linuxFormat = "libEOSSDK-Linux{0}-Shipping.so";

            name = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? string.Format(linuxFormat, RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "Arm64" : string.Empty) : "EOSSDK-Win64-Shipping.dll";

            var path = Path.Combine(baseDirectory, name);
            Logger.Info("Loading SDK from " + path);
            EOSHandle = DllTools.LoadLibrary(path);

            if (EOSHandle == IntPtr.Zero)
                throw new DllNotFoundException($"Unable to load EOS SDK native library. Tried {name} in {baseDirectory}");

            if (!DLLResolverConfigured)
            {
                SetDLLImportResolver();
                DLLResolverConfigured = true;
            }

            Logger.Info("EOS SDK loaded from " + path);

            Runtime = new EOSRuntime();

            var success = await Runtime.InitializeAsync(Logger);

            if (success)
            {
                Logger.Info("EOS initialized.");
            }
            else
            {
                Logger.Error("EOS initialization failed.");
            }
        }

        private void SetDLLImportResolver()
            => NativeLibrary.SetDllImportResolver(typeof(Common).Assembly, ResolverCallback);

        private IntPtr ResolverCallback(string name, Assembly assembly, DllImportSearchPath? path)
            => name.Contains("EOSSDK", StringComparison.OrdinalIgnoreCase) ? EOSHandle : IntPtr.Zero;
    }

    internal class EpicLobby(EOSRuntime runtime, LobbyDetails lobbyDetails, ProductUserId owner) : IMatchmakingLobby, IDisposable
    {
        public string Owner => (Utf8String)owner;

        public bool IsOwnerMe => ((Utf8String)Runtime.Connect.LocalUserId) == Owner;

        private LobbyDetails Details { get; set; } = lobbyDetails;

        private EOSRuntime Runtime { get; } = runtime;

        ~EpicLobby()
        {
            Details?.Release();
            Details = null;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Details?.Release();
            Details = null;
        }

        public bool TryGetData(string key, out string value)
        {
            value = Runtime.Lobby.GetAttribute(Details, key);
            return !string.IsNullOrWhiteSpace(value);
        }

        public string GetData(string key)
        {
            TryGetData(key, out var value);
            return value;
        }
    }
}