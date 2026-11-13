using System.Text.RegularExpressions;

using MailKit.Net.Imap;
using MailKit.Search;

using MimeKit;

namespace FLB_API.Managers
{
    public partial class IMAPManager
    {
        public const string STEAM_SUBJECT = "Your Steam account: Access from";

        public const string MODIO_SUBJECT = "mod.io Security Code";

        public string Host { get; set; }
        public int Port { get; set; }

        public FusionAPI.Data.Interfaces.ILogger? Logger { get; set; }

        public IMAPManager(string host, int port, FusionAPI.Data.Interfaces.ILogger? logger = null)
        {
            Host = host;
            Port = port;
            Logger = logger;
        }

        public IMAPManager(ImapAuth auth, FusionAPI.Data.Interfaces.ILogger? logger = null, bool login = true)
        {
            Host = auth.Host!;
            Port = auth.Port!;
            Logger = logger;
            if (login) LogIn(auth);
        }

        public ImapClient? Client { get; set; }

        public void LogIn(string username, string password)
        {
            Logger?.Info("Logging in to IMAP server {0}:{1} with user {2}", Host, Port, username);
            Client = new ImapClient();
            AddEvents();
            Client.Connect(Host, Port);
            Client.AuthenticationMechanisms.Remove("XOAUTH2");
            Client.Authenticate(username, password);
        }

        public void LogIn(ImapAuth auth)
            => LogIn(auth.Username!, auth.Password!);

        public async Task<string?> GetCodeAsync(Regex regex, string codeName = "Steam Auth Code", string subject = STEAM_SUBJECT, float delay = 5, int maxTries = -1)
        {
            var tries = 0;
            while (Client is { IsAuthenticated: true, IsConnected: true })
            {
                Logger?.Info("Checking inbox for email regarding the {0}...", codeName);
                await Client.Inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);
                foreach (var msg in (await Client.Inbox.SearchAsync(SearchQuery.All)).Reverse().Take(5).ToList())
                {
                    try
                    {
                        var message = await Client.Inbox.GetMessageAsync(msg);
                        if (HandleMessage(message, regex, codeName, subject) is { } code && !string.IsNullOrWhiteSpace(code))
                            return code;
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error("Error while fetching email", ex);
                    }
                }
                tries++;
                if (tries >= maxTries && maxTries != -1)
                {
                    Logger?.Warning("Maximum number of tries reached ({0}). Stopping search for {1}.", maxTries, codeName);
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }
            return null;
        }

        private string? HandleMessage(MimeMessage message, Regex regex, string codeName = "Steam Auth Code", string subject = STEAM_SUBJECT)
        {
            if (message?.Subject?.StartsWith(subject, StringComparison.OrdinalIgnoreCase) != true)
                return null;
            Logger?.Info("Found an email containing the code! Extracting...");
            return ExtractCode(message, regex, codeName);
        }

        private string? ExtractCode(MimeMessage message, Regex regex, string codeName = "Steam Auth Code")
        {
            if (message.BodyParts.FirstOrDefault(x => x.ContentType.MimeType == "text/plain") is not TextPart body)
            {
                Logger?.Error("Could not find a plain version of the email");
                return null;
            }

            var code = regex.Match(body.Text)?.Groups?["code"];
            if (code?.Success != true)
            {
                Logger?.Error("Could not find the code");
                return null;
            }

            Logger?.Info("Extracted the {0}: {1}", codeName, code.Value);
            return code.Value;
        }

        private void AddEvents()
        {
            if (Client == null)
                return;

            Client.Authenticated += (_, e) => Logger?.Info("IMAP Authenticated");
            Client.Connected += (_, e) => Logger?.Info("Connected to IMAP");
            Client.Disconnected += (_, e) => Logger?.Info("Disconnected from IMAP");
        }

        public static Regex GetSteamRegEx() => SteamAuthCode();

        public static Regex GetModIoRegEx() => ModIoAuthCode();

        [GeneratedRegex(@"Login Code\n(?'code'.*)", RegexOptions.Multiline)]
        private static partial Regex SteamAuthCode();

        [GeneratedRegex(@"age or older:\n\n(?'code'.*)", RegexOptions.Multiline)]
        private static partial Regex ModIoAuthCode();
    }
}