using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FLB_API.Blacklist.Modules
{
    public class WordBlacklist : IBlacklistModule
    {
        public string Id => "WordBlacklist";

        public WordBlacklistFile? Config { get; private set; }

        private BlacklistManager? Instance { get; set; }

        private FusionAPI.Interfaces.ILogger? Logger { get; set; }

        private bool Deinitialized = false;

        public async Task<bool> InitAsync(BlacklistManager manager, FusionAPI.Interfaces.ILogger logger)
        {
            Instance = manager;
            Logger = logger;

            _ = Task.Factory.StartNew(async () =>
            {
                while (!Deinitialized)
                {
                    LoadWords();
                    await Task.Delay(30 * 1000);
                }
            });
            return true;
        }

        public Task<bool> DeInitAsync()
        {
            Deinitialized = true;
            return Task.FromResult(true);
        }

        public async Task<bool> IsLobbyAllowed(CustomLobbyInfo info)
        {
            if (Config?.Words == null || Config.Words.Count == 0)
                return true;

            foreach (var word in Config.Words)
            {
                if (word == null || string.IsNullOrWhiteSpace(word.Match))
                    continue;

                const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Multiline;
                Regex regex = word.Type switch
                {
                    MatchType.WholeWord => new Regex(@"\b" + Regex.Escape(word.Match ?? "") + @"\b", options),
                    MatchType.RegEx => new Regex(word.Match, options),
                    MatchType.Contains => new Regex(Regex.Escape(word.Match ?? ""), options),
                    _ => new Regex(Regex.Escape(word.Match ?? ""), options),
                };
                if (regex.IsMatch(info.LobbyName ?? string.Empty))
                    return false;
            }

            return true;
        }

        private void LoadWords()
        {
            try
            {
                Logger?.Info("Loading blacklisted words...");
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blacklistedWords.json");
                if (!File.Exists(path))
                {
                    Logger?.Info("Blacklisted words file is missing, creating...");
                    using var stream = File.CreateText(path);
                    var serialized = JsonSerializer.Serialize<WordBlacklistFile>(new());
                    stream.Write(serialized);
                    stream.Flush();
                    stream.Close();
                }
                else
                {
                    Config = JsonSerializer.Deserialize<WordBlacklistFile>(File.ReadAllText(path));
                    Logger?.Info($"Successfully loaded blacklisted words! ({Config?.Words?.Count ?? 0} words)");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to set blacklisted words from file", ex);
            }
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class WordBlacklistFile
    {
        [JsonPropertyName("words")]
        public List<BlacklistedWord> Words { get; set; } = [];
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class BlacklistedWord
    {
        [JsonPropertyName("match")]
        public string? Match { get; set; }

        [JsonIgnore]
        public MatchType Type { get; set; }

        [JsonPropertyName("type")]
        public string StringType
        {
            get
            {
                return (Enum.GetName(Type) ?? "Contains").ToLower();
            }
            set
            {
                if (Enum.TryParse<MatchType>(value, true, out var matchType))
                    Type = matchType;
                else
                    Type = MatchType.Contains;
            }
        }
    }

    public enum MatchType
    {
        Contains,
        WholeWord,
        RegEx,
    }
}