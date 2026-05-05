using System.Reflection;
using System.Runtime.InteropServices;

using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

using FusionAPI.EOS.Auth;
using FusionAPI.EOS.Core;
using FusionAPI.Epic;
using FusionAPI.Interfaces;

namespace FusionAPI
{
    public class EOSHandler : IMatchmakingHandler
    {
        private static bool DLLResolverConfigured { get; set; } = false;

        public bool IsInitialized => EOSManager?.IsInitialized ?? false;

        internal EOSManager EOSManager { get; private set; }

        internal EOSAuthManager AuthManager { get; private set; }

        public ILogger Logger { get; private set; }

        private DateTime _lastFetch = DateTime.Now;

        public DateTime LastFetch => _lastFetch;

        private IntPtr EOSHandle { get; set; } = IntPtr.Zero;

        public async Task<IMatchmakingLobby[]> GetLobbies(bool includePrivate = false)
        {
            var options = new CreateLobbySearchOptions
            {
                MaxResults = 200
            };

            LobbySearch searchHandle = null;

            if (!AuthManager.IsLoggedIn)
            {
                Logger.Error("Failed to get lobbies, not logged into EOS!");
                return [];
            }

            var res = EOSInterfaces.Lobby?.CreateLobbySearch(ref options, out searchHandle);

            if (res != Result.Success)
            {
                Logger.Error($"Failed to create lobby search handle: {res}");
                return [];
            }

            var identifierParam = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData
                {
                    Key = LobbyKeys.IdentifierKey,
                    Value = bool.TrueString,
                },
                ComparisonOp = ComparisonOp.Equal,
            };
            searchHandle.SetParameter(ref identifierParam);

            if (searchHandle == null)
            {
                Logger.Error("LobbySearch handle is null after creation");
                return [];
            }

            var findOptions = new LobbySearchFindOptions
            {
                LocalUserId = AuthManager.LocalUserId
            };

            var tcs = new TaskCompletionSource<IMatchmakingLobby[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            searchHandle.Find(ref findOptions, null, (ref info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Logger.Error($"Failed to find lobbies: {info.ResultCode}");
                    if (!tcs.TrySetResult([]))
                        Logger.Error($"Failed to set result, current task state: {Enum.GetName(tcs.Task.Status)}");
                    return;
                }

                var countOptions = new LobbySearchGetSearchResultCountOptions();
                var lobbyCount = searchHandle.GetSearchResultCount(ref countOptions);

                if (lobbyCount == 0)
                {
                    Logger.Trace("No lobbies found.");
                    if (!tcs.TrySetResult([]))
                        Logger.Error($"Failed to set result, current task state: {Enum.GetName(tcs.Task.Status)}");
                    return;
                }

                var lobbyDetailsToProcess = new List<LobbyDetails>((int)lobbyCount);

                for (uint i = 0; i < lobbyCount; i++)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions
                    {
                        LobbyIndex = i
                    };

                    if (searchHandle.CopySearchResultByIndex(ref copyOptions, out var lobbyDetails) == Result.Success
                        && lobbyDetails != null)
                    {
                        lobbyDetailsToProcess.Add(lobbyDetails);
                    }
                }

                var lobbies = new List<IMatchmakingLobby>(lobbyDetailsToProcess.Count);

                foreach (var lobbyDetails in lobbyDetailsToProcess)
                {
                    var lobby = ProcessSingleLobby(lobbyDetails);
                    if (lobby != null)
                        lobbies.Add(lobby);
                    else
                        lobbyDetails.Release();
                }

                if (!tcs.TrySetResult([.. lobbies]))
                    Logger.Error($"Failed to set result, current task state: {Enum.GetName(tcs.Task.Status)}");
            });

            _lastFetch = DateTime.Now;
            return await tcs.Task;
        }

        private EpicLobby? ProcessSingleLobby(LobbyDetails lobbyDetails)
        {
            var ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
            var ownerId = lobbyDetails.GetLobbyOwner(ref ownerOptions);

            if (ownerId == null)
                return null;

            var infoOptions = new LobbyDetailsCopyInfoOptions();
            if (lobbyDetails.CopyInfo(ref infoOptions, out var lobbyInfo) != Result.Success || !lobbyInfo.HasValue)
                return null;

            var networkLobby = new EpicLobby(lobbyDetails, this);

            if (!networkLobby.TryGetData(LobbyKeys.HasLobbyOpenKey, out var hasServerOpen) ||
                hasServerOpen != bool.TrueString)
            {
                return null;
            }

            var metadata = ReadMetadata(networkLobby);

            if (metadata == null)
                return null;

            if (!metadata.HasLobbyOpen)
                return null;

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

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                name = string.Format(linuxFormat, IsARM() ? "Arm64" : string.Empty);
            else
                name = "EOSSDK-Win64-Shipping.dll";

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

            AuthManager = new EOSAuthManager(logger);
            EOSManager = new EOSManager(AuthManager, logger);

            bool success = await EOSManager.InitializeAsync();

            if (success)
                Logger.Info("EOS initialized.");
            else
                Logger.Error("EOS initialization failed.");
        }

        private static bool IsARM()
            => RuntimeInformation.OSArchitecture == Architecture.Arm64;

        private void SetDLLImportResolver()
            => NativeLibrary.SetDllImportResolver(typeof(Common).Assembly, ResolverCallback);

        private IntPtr ResolverCallback(string name, Assembly assembly, DllImportSearchPath? path)
            => name.Contains("EOSSDK", StringComparison.OrdinalIgnoreCase) ? EOSHandle : IntPtr.Zero;

        public bool IsFriend(string id)
            => false;
    }

    internal class EpicLobby(LobbyDetails details, EOSHandler handler) : IMatchmakingLobby
    {
        public string Owner => GetOwner();

        public bool IsOwnerMe => ((Utf8String)EOSHandler.AuthManager.LocalUserId) == Owner;

        private LobbyDetails Details { get; } = details;

        private EOSHandler EOSHandler { get; } = handler;

        public bool TryGetData(string key, out string value)
        {
            value = string.Empty;

            if (Details == null)
                return false;

            var options = new LobbyDetailsCopyAttributeByKeyOptions
            {
                AttrKey = key
            };

            var result = Details.CopyAttributeByKey(ref options, out var attribute);

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
            var id = Details?.GetLobbyOwner(ref options);
            if (id == null)
                return string.Empty;

            return (Utf8String)id;
        }
    }
}