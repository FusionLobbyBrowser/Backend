using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using FLB_API.Controllers;
using FLB_API.Managers;

using FusionAPI.Data.Containers;

using FuzzySharp;

using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

using Platform = FLB_API.Controllers.LobbyListController.Platform;

namespace FLB_API.Discord
{
    [SlashCommand("lobbies", "Lobbies command")]
    public partial class LobbiesCommandModule : ApplicationCommandModule<ApplicationCommandContext>
    {
        public static readonly Dictionary<string, LobbyListResponse> Response = [];

        [SubSlashCommand("check", "Check the currently available lobbies in LabFusion!")]
        public static InteractionMessageProperties Check(
            [SlashCommandParameter(Name = "platform", Description = "What platform to display the lobbies from")] Platform platform)
            => Internal_Check(platform, 1);

        internal static InteractionMessageProperties Internal_Check(Platform platform, int page = 1)
        {
            var error = GetLobbies(out LobbyListResponse? list, platform);
            if (error != null)
                return error;

            const int maxPerPage = 5;
            int pages = (int)Math.Floor((double)list!.Lobbies.Length / maxPerPage);

            var lobbies = new List<LobbyInfo>(list.Lobbies).OrderByDescending(x => x.PlayerCount).ToArray();
            if (page > pages)
                page = pages;

            lobbies = GetPage(lobbies, page, maxPerPage);

            string description =
$@"# {GetEmoji(CustomEmoji.LabFusion)} Lobbies ({list.Lobbies.Length})

Those are the currently available lobbies on LabFusion. Please note that the lobbies are fetched every {list.Interval} seconds, so it may not be that accurate.";
            var container = new ComponentContainerProperties();
            container.AddComponents(new TextDisplayProperties(description));
            container.AddComponents(new ComponentSeparatorProperties());

            foreach (var l in lobbies)
            {
                var host = RemoveUnityRichText(l.LobbyHostName ?? "N/A");
                var gamemode = RemoveUnityRichText(l.GamemodeTitle);
                var d = RemoveUnityRichText(l.LobbyDescription);

                var lobbyContainer = new ComponentContainerProperties();

                var section = new ComponentSectionProperties(
                    new ComponentSectionThumbnailProperties(
                        new ComponentMediaProperties(
                            GetThumbnailURL(l.LevelModID, l.LevelBarcode))));
                section.AddComponents(new TextDisplayProperties($"## {(l.LobbyPlatform == "Steam" ? GetEmoji(CustomEmoji.Steam) : GetEmoji(CustomEmoji.EpicGames))} {l.GetLobbyName()}"));
                var details = new StringBuilder(
@$"{(string.IsNullOrWhiteSpace(d) ? string.Empty : d)}

**{GetEmoji(CustomEmoji.IDBadge)} ID:** {l.LobbyID}
**{GetEmoji(CustomEmoji.User)} Host:** {host}
**{GetEmoji(CustomEmoji.Map)} Level:** {(l.LevelModID != -1 ? $"[{RemoveUnityRichText(l.LevelTitle ?? "N/A")}](https://mod.io/search/mods/{l.LevelModID})" : $"{RemoveUnityRichText(l.LevelTitle ?? "N/A")}")}
**{GetEmoji(CustomEmoji.PuzzlePiece)} Gamemode:** {(string.IsNullOrWhiteSpace(gamemode) ? "Sandbox" : gamemode)}
**{GetEmoji(CustomEmoji.Clock)} First discovered:** <t:{l.LobbyUptime}:R>
**{GetEmoji(CustomEmoji.Users)} Players [{l.PlayerCount}/{l.MaxPlayers}]:**
");
                foreach (var p in l.PlayerList.Players)
                {
                    var nickname = RemoveUnityRichText(p.Nickname);
                    var username = RemoveUnityRichText(p.Username);
                    details.AppendLine($"   {(string.IsNullOrWhiteSpace(nickname) ? (username ?? "N/A") : nickname)}");
                }

                section.AddComponents(new TextDisplayProperties(details.ToString()));
                lobbyContainer.AddComponents(section);
                container.AddComponents(lobbyContainer);
                container.AddComponents(new ComponentSeparatorProperties());
            }
            container.AddComponents(new TextDisplayProperties(LastRefresh(list)));
            var row = new ActionRowProperties();
            row.AddComponents(new ButtonProperties($"button_previous:{(int)platform}:{page}", "Previous Page", EmojiProperties.Custom((ulong)CustomEmoji.CaretLeft), ButtonStyle.Primary));
            row.AddComponents(new LinkButtonProperties("https://fusion.hahoos.dev/", "Website", EmojiProperties.Custom((ulong)CustomEmoji.Link)));
            row.AddComponents(new ButtonProperties($"button_currentPage:{(int)platform}", $"Page {page}/{pages}", ButtonStyle.Secondary));
            row.AddComponents(new ButtonProperties($"button_refresh:{(int)platform}:{page}", "Refresh", ButtonStyle.Secondary));
            row.AddComponents(new ButtonProperties($"button_next:{(int)platform}:{page}:{pages}", "Next Page", EmojiProperties.Custom((ulong)CustomEmoji.CaretRight), ButtonStyle.Primary));
            var row2 = new ActionRowProperties();
            row2.AddComponents(new ButtonProperties("button_removeMsg", EmojiProperties.Custom((ulong)CustomEmoji.XMark), ButtonStyle.Danger));
            container.AddComponents(row);
            container.AddComponents(row2);

            return new InteractionMessageProperties()
                .WithComponents([container])
                .WithFlags(MessageFlags.IsComponentsV2);
        }

        private static string LastRefresh(LobbyListResponse res)
            => $"Data from <t:{res.Date}:R>. If this shows that more than **{res.Interval} seconds ago**, make sure to **refresh** for up-to-date information!";

        [SubSlashCommand("info", "Get more details about a lobby!")]
        public static async Task<InteractionMessageProperties> Info(
            [SlashCommandParameter(Name = "id", Description = "Input an ID of a lobby or select from the available choices (only 25 lobbies with most players)",
            AutocompleteProviderType = typeof(InfoAutoComplete))] string id)
            => await Internal_Info(id, 1);

        internal static async Task<InteractionMessageProperties> Internal_Info(string id, int page = 1)
        {
            var error = GetLobbies(out LobbyListResponse? list);
            if (error != null)
                return error;

            var l = list!.Lobbies.FirstOrDefault(x => x.LobbyID == id);
            if (l == null)
                return DiscordBotManager.Error($"Could not find a lobby with the ID `{id}`");

            const int maxPerPage = 5;
            int pages = (int)Math.Ceiling((double)l.PlayerList.Players.Length / maxPerPage);

            if (page > pages)
                page = pages;

            var players = GetPage(l.PlayerList.Players, page, maxPerPage);

            var host = RemoveUnityRichText(l.LobbyHostName ?? "N/A");
            var gamemode = RemoveUnityRichText(l.GamemodeTitle);
            var d = RemoveUnityRichText(l.LobbyDescription);

            var container = new ComponentContainerProperties();

            container.AddComponents(new TextDisplayProperties($"# {(l.LobbyPlatform == "Steam" ? GetEmoji(CustomEmoji.Steam) : GetEmoji(CustomEmoji.EpicGames))} {l.GetLobbyName()}"));
            var details =
@$"{(string.IsNullOrWhiteSpace(d) ? string.Empty : d)}
**{GetEmoji(CustomEmoji.IDBadge)} ID:** {l.LobbyID}
**{GetEmoji(CustomEmoji.User)} Host:** {host}
**{GetEmoji(CustomEmoji.Map)} Level:** {(l.LevelModID != -1 ? $"[{RemoveUnityRichText(l.LevelTitle ?? "N/A")}](https://mod.io/search/mods/{l.LevelModID})" : $"{RemoveUnityRichText(l.LevelTitle ?? "N/A")}")}
**{GetEmoji(CustomEmoji.PuzzlePiece)} Gamemode:** {(string.IsNullOrWhiteSpace(gamemode) ? "Sandbox" : gamemode)}
**{GetEmoji(CustomEmoji.Clock)} First discovered:** <t:{l.LobbyUptime}:R>
";
            container.AddComponents(new TextDisplayProperties(details));
            container.AddComponents(new MediaGalleryProperties([new MediaGalleryItemProperties(GetThumbnailURL(l.LevelModID, l.LevelBarcode))]));
            container.AddComponents(new TextDisplayProperties($"# **{GetEmoji(CustomEmoji.Users)} Players [{l.PlayerCount}/{l.MaxPlayers}]:**"));
            container.AddComponents(new ComponentSeparatorProperties());
            foreach (var p in players)
            {
                container.AddComponents(await CreatePlayerSection(p, l));
                container.AddComponents(new ComponentSeparatorProperties());
            }

            container.AddComponents(new TextDisplayProperties(LastRefresh(list)));

            var row = new ActionRowProperties();
            row.AddComponents(new ButtonProperties($"button_previousInfo:{id}:{page}", "Previous Page", EmojiProperties.Custom(1522003884036591738), ButtonStyle.Primary));
            row.AddComponents(new ButtonProperties($"button_currentPageInfo:{id}", $"Page {page}/{pages}", ButtonStyle.Secondary));
            row.AddComponents(new ButtonProperties($"button_nextInfo:{id}:{page}:{pages}", "Next Page", EmojiProperties.Custom(1522003882522448093), ButtonStyle.Primary));

            var row2 = new ActionRowProperties();
            row2.AddComponents(new LinkButtonProperties($"https://fusion.hahoos.dev/?lobby={l.LobbyID}", "View on Website", EmojiProperties.Custom(1522004581687623740)));
            row2.AddComponents(new ButtonProperties($"button_refreshInfo:{l.LobbyID}:{page}", "Refresh", ButtonStyle.Secondary));
            row2.AddComponents(new ButtonProperties("button_removeMsg", EmojiProperties.Custom((ulong)CustomEmoji.XMark), ButtonStyle.Danger));

            container.AddComponents(row);
            container.AddComponents(row2);
            return new InteractionMessageProperties()
                .WithComponents([container])
                .WithFlags(MessageFlags.IsComponentsV2);
        }

        private static async Task<IComponentContainerComponentProperties[]> CreatePlayerSection(PlayerInfo p, LobbyInfo l)
        {
            var list = new List<IComponentContainerComponentProperties>();
            var nickname = RemoveUnityRichText(p.Nickname);
            var username = RemoveUnityRichText(p.Username);
            var d = RemoveUnityRichText(p.Description);
            var name = string.IsNullOrWhiteSpace(nickname) ? (username ?? "N/A") : nickname;
            bool isAvatarWrong = await ModIOManager.IsNSFW(p.AvatarModID);
            string avatar;
            if (!isAvatarWrong)
                avatar = (p.AvatarModID != -1 ? $"[{RemoveUnityRichText(p.AvatarTitle ?? "N/A")}](https://mod.io/search/mods/{p.AvatarModID})" : $"{RemoveUnityRichText(p.AvatarTitle ?? "N/A")}");
            else
                avatar = "[NSFW]";
            var thumbnail = GetThumbnailURL(p.AvatarModID,
                    p.AvatarModID == -1 && ThumbnailController.Vanilla.ContainsKey(p.AvatarTitle) ? p.AvatarTitle : string.Empty);

            bool result = Uri.TryCreate(thumbnail, UriKind.Absolute, out Uri? uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

            var section = new ComponentSectionProperties(
                new ComponentSectionThumbnailProperties(
                    thumbnail));
            if (result)
                list.Add(section);
            var details =
@$"## {name}
{(string.IsNullOrWhiteSpace(d) ? string.Empty : d)}{(string.IsNullOrWhiteSpace(nickname) ? string.Empty : $"\n**{GetEmoji(CustomEmoji.CircleUser)} Nickname:** {nickname}")}
**{GetEmoji(CustomEmoji.IDBadge)} Username:** {username}
**{GetEmoji(CustomEmoji.IDCard)} ID:** {p.PlatformID}
**{GetEmoji(CustomEmoji.UserAvatar)} Avatar:** {avatar}{(l.LobbyPlatform == "Steam" ? $"\n**{GetEmoji(CustomEmoji.Steam)} Steam Profile:** [View](https://steamcommunity.com/profiles/{p.PlatformID})" : string.Empty)}";
            var detailsProp = new TextDisplayProperties(details);
            if (result)
                section.AddComponents(detailsProp);
            else
                list.Add(detailsProp);
            return [.. list];
        }

        public static string FixBase64(string base64)
        {
            if (base64.Length % 4 != 0)
                base64 += ("===")[..(4 - (base64.Length % 4))];
            return base64.Replace("-", "+").Replace("_", "/");
        }

        public static string ToBrokenBase64(string text)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).ReplaceRegex(@"\+", "-").ReplaceRegex(@"\/", "_").ReplaceRegex(@"\=+$", "");

        public static InteractionMessageProperties? GetLobbies(out LobbyListResponse? lobbies, Platform platform = Platform.All)
        {
            lobbies = null;
            if (platform != Platform.All)
            {
                var handler = platform == Platform.Steam ? Program.FusionClient : Program.EOSClient;
                if (handler?.Handler.IsInitialized != true)
                    return DiscordBotManager.Error($"Server is not connected to {Enum.GetName(platform)}.");
            }
            else
            {
                if (Program.FusionClient?.Handler.IsInitialized != true)
                    return DiscordBotManager.Error("Server is not connected to Steam.");
                else if (Program.EOSClient?.Handler.IsInitialized != true)
                    return DiscordBotManager.Error("Server is not connected to Epic.");
            }

            LobbyListResponse? list;
            if (platform == Platform.Steam)
                list = Program.SteamLobbies;
            else if (platform == Platform.Epic)
                list = Program.EOSLobbies;
            else
                list = Program.Lobbies;

            if (string.IsNullOrWhiteSpace(list?.JSON))
                return DiscordBotManager.Error("The server did not fetch lobbies yet");

            lobbies = list;

            return null;
        }

        public static T[] GetPage<T>(T[] list, int page, int pageSize)
            => [.. new List<T>(list).Skip((page - 1) * pageSize).Take(pageSize)];

        public static string RemoveUnityRichText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return UnityRichText().Replace(text, string.Empty);
        }

        public static string GetThumbnailURL(long modId, string? barcode = "")
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return $"https://fusionapi.hahoos.dev/thumbnail/{modId}";
            else
                return $"https://fusionapi.hahoos.dev/thumbnail/{modId}?barcode={barcode}";
        }

        public static string GetEmoji(CustomEmoji emoji)
            => $"<:{(Enum.GetName(emoji) ?? "unknown").ToLower()}:{(long)emoji}>";

        public enum CustomEmoji : long
        {
            LabFusion = 1521944985690181823,
            Map = 1521946391960879214,
            User = 1521946393629950033,
            PuzzlePiece = 1521946396268433580,
            Clock = 1521946397337981060,
            Steam = 1521947742782361782,
            Users = 1521947988119781457,
            EpicGames = 1521998404341731448,
            CaretRight = 1522003882522448093,
            CaretLeft = 1522003884036591738,
            Link = 1522004581687623740,
            Play = 1522237377551269969,
            Info = 1522238640166670507,
            IDCard = 1522662343270596809,
            CircleUser = 1522662344856174602,
            Book = 1522662345745236019,
            UserAvatar = 1522662347355848734,
            IDBadge = 1522663008252465274,
            XMark = 1524101018814779442,
        }

        [GeneratedRegex("<(.*?)>")]
        private static partial Regex UnityRichText();
    }

    public class InfoAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
    {
        public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
        {
            var input = option.Value;
            var list = new List<ApplicationCommandOptionChoiceProperties>();

            if (Program.Lobbies?.Lobbies == null)
                return new([]);

            var lobbies = new List<LobbyInfo>(Program.Lobbies?.Lobbies!)?.OrderByDescending(x => x.PlayerCount).ToList() ?? [];
            if (!string.IsNullOrWhiteSpace(input))
                lobbies = [.. lobbies.Where(x => Fuzz.PartialRatio(input, x.GetLobbyName()) >= 35)];
            foreach (var item in lobbies.Take(25))
            {
                try
                {
                    const int maxChoiceLength = 100;
                    var host = LobbiesCommandModule.RemoveUnityRichText(item.LobbyHostName);
                    var other = $" [{item.PlayerCount}/{item.MaxPlayers}] [Host: {TruncateAtWord(host, 20)}] [{item.LobbyPlatform}]";
                    var res = $"{TruncateAtWord(item.GetLobbyName(), maxChoiceLength, other.Length)}{other}";
                    if (res.Length <= maxChoiceLength)
                        list.Add(new(res, item.LobbyID));
                    else
                        Program.Logger?.Warning($"Choice has more than {maxChoiceLength} allowed characters! ({res.Length})\nResult: {res}");
                }
                catch (Exception ex)
                {
                    Program.Logger?.Error(ex, $"An exception occurred while generating a choice for lobby {item.LobbyID}");
                }
            }
            return new(list);
        }

        public static string TruncateAtWord(string input, int maxLength, int additional = 0)
        {
            var length = maxLength - additional;

            // this shouldn't happen
            if (length < 0)
                length = 0;

            if (input == null || input.Length < length)
                return input ?? string.Empty;

            int iNextSpace = input.LastIndexOf(' ', length);

            return string.Format("{0}...", input[..((iNextSpace > 0) ? iNextSpace : length)].Trim());
        }
    }

    [method: JsonConstructor]
    public struct Payload(string code, string layer)
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = code;

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = layer;
    }
}