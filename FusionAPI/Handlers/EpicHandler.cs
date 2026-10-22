using System.Reflection;
using System.Runtime.InteropServices;

using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

using FusionAPI.Data;
using FusionAPI.Data.Containers;
using FusionAPI.Data.Enums;
using FusionAPI.Data.Interfaces;
using FusionAPI.EOS;
using FusionAPI.Epic;

namespace FusionAPI.Handlers
{
    public class EpicHandler : IMatchmakingHandler
    {
        private static bool DllResolverConfigured { get; set; } = false;

        public bool IsInitialized => Runtime?.IsInitialized ?? false;

        internal EpicRuntime? Runtime { get; private set; }

        public ILogger? Logger { get; private set; }

        public DateTimeOffset LastFetch { get; private set; } = DateTimeOffset.UtcNow;

        private IntPtr LibraryHandle { get; set; } = IntPtr.Zero;

        public string ID => "Epic";

        public async Task<IMatchmakingLobby[]> GetLobbies(bool publicLobbies = true, bool friendsOnlyLobbies = false)
        {
            if (!IsInitialized)
            {
                Logger?.Warning("Handler/Runtime is not initialized!");
                return [];
            }

            if (Runtime == null)
            {
                Logger?.Error("Handler is not initialized! (Runtime is null)");
                return [];
            }

            if (Runtime.Lobby == null || Runtime.Connect == null || Runtime.Platform == null)
            {
                Logger?.Error("Handler is not initialized! (Lobby/Connect/Platform is null)");
                return [];
            }

            var createLobbySearchOptions = new CreateLobbySearchOptions
            {
                MaxResults = 200
            };

            var result = Runtime.Lobby.LobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out var searchHandle);
            if (result != Result.Success || searchHandle == null)
            {
                Logger?.Error($"Failed to create lobby search: {result}");
                return [];
            }

            SetParameter(ref searchHandle, LobbyKeys.HAS_LOBBY_OPEN_KEY, bool.TrueString, ComparisonOp.Equal);
            SetParameter(ref searchHandle, LobbyKeys.IDENTIFIER_KEY, bool.TrueString, ComparisonOp.Equal);
            SetParameter(ref searchHandle, LobbyKeys.GAME_KEY, "BONELAB", ComparisonOp.Equal);

            SetParameter(ref searchHandle, LobbyKeys.PRIVACY_KEY, ((int)ServerPrivacy.PUBLIC).ToString(), ComparisonOp.Equal);

            var lobbySearchFindOptions = new LobbySearchFindOptions
            {
                LocalUserId = Runtime?.Connect?.LocalUserId
            };

            var tcs = new TaskCompletionSource<IMatchmakingLobby[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            searchHandle.Find(ref lobbySearchFindOptions, null, (ref info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Logger?.Error($"EOS lobby search failed: {info.ResultCode}");
                    if (!tcs.TrySetResult([]))
                        Logger?.Error($"Failed to set result, current task state: {Enum.GetName(tcs.Task.Status)}");
                    searchHandle.Release();
                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                var lobbyCount = searchHandle.GetSearchResultCount(ref countOptions);

#if DEBUG
                Logger?.Info($"Lobbies Found: {lobbyCount}");
#endif

                List<IMatchmakingLobby> lobbies = [with((int)lobbyCount)];

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

                    var processed = ProcessSingleLobby(lobbyDetails);
                    if (processed != null)
                        lobbies.Add(processed);
                }

                searchHandle.Release();

                if (!tcs.TrySetResult([.. lobbies]))
                    Logger?.Error($"Failed to set result, current task state: {Enum.GetName(tcs.Task.Status)}");
            });

            LastFetch = DateTimeOffset.UtcNow;

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
                Logger?.Error($"Failed to set lobby search parameter: {result}");
        }

        private EpicLobby? ProcessSingleLobby(LobbyDetails lobbyDetails)
        {
            if (Runtime == null)
                return null;

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

            if (metadata is { HasLobbyOpen: true })
                return networkLobby;

            networkLobby.Dispose();
            return null;
        }

        private LobbyMetadataInfo? ReadMetadata(IMatchmakingLobby lobby)
        {
            try
            {
                return LobbyMetadataInfo.Read(lobby);
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to read lobby metadata", ex);
                return null;
            }
        }

        public async Task Init(ILogger logger, Dictionary<string, string> metadata)
        {
            Logger = logger;

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            const string linuxFormat = "libEOSSDK-Linux{0}-Shipping.so";

            var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "Arm64" : string.Empty;
            var name = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? string.Format(linuxFormat, architecture) : "EOSSDK-Win64-Shipping.dll";

            var path = Path.Combine(baseDirectory, name);
            Logger.Info("Loading SDK from " + path);
            LibraryHandle = DllTools.LoadLibrary(path);

            if (LibraryHandle == IntPtr.Zero)
                throw new DllNotFoundException($"Unable to load EOS SDK native library. Tried {name} in {baseDirectory}");

            if (!DllResolverConfigured)
            {
                SetDllImportResolver();
                DllResolverConfigured = true;
            }

            Logger.Info("EOS SDK loaded from " + path);

            Runtime = new EpicRuntime();

            var success = await Runtime.InitializeAsync(Logger);

            if (success)
                Logger.Info("EOS initialized.");
            else
                Logger.Error("EOS initialization failed.");
        }

        public Task DeInit()
        {
            try
            {
                Runtime?.Shutdown();
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        private void SetDllImportResolver()
            => NativeLibrary.SetDllImportResolver(typeof(Common).Assembly, ResolverCallback);

        private IntPtr ResolverCallback(string name, Assembly assembly, DllImportSearchPath? path)
            => name.Contains("EOSSDK", StringComparison.OrdinalIgnoreCase) ? LibraryHandle : IntPtr.Zero;
    }

    internal class EpicLobby(EpicRuntime runtime, LobbyDetails lobbyDetails, ProductUserId owner) : IMatchmakingLobby, IDisposable
    {
        public string Owner => (Utf8String)owner;

        public bool IsOwnerMe => ((Utf8String)Runtime?.Connect?.LocalUserId) == Owner;

        private LobbyDetails? Details { get; set; } = lobbyDetails;

        private EpicRuntime Runtime { get; } = runtime;

        private bool _disposed;

        ~EpicLobby()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            Details?.Release();
            Details = null;
            _disposed = true;
        }

        public bool TryGetData(string key, out string value)
        {
            value = EOS.EpicLobby.GetAttribute(Details, key);
            return !string.IsNullOrWhiteSpace(value);
        }

        public string GetData(string key)
        {
            TryGetData(key, out var value);
            return value;
        }
    }
}