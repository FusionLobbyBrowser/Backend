using System.Text.RegularExpressions;

using MailKit.Net.Imap;
using MailKit.Search;

using MimeKit;

namespace FLB_API.Managers
{
    public partial class IMAPManager(string host, int port, Logger? logger = null)
    {
        public const string Subject = "Your Steam account: Access from";

        public string Host { get; set; } = host;
        public int Port { get; set; } = port;

        public Logger? Logger { get; set; } = logger;

        public ImapClient? Client { get; set; }

        public void LogIn(string username, string password)
        {
            Logger?.Info("Logging in to IMAP server {0}:{1} with user {2}", Host, Port, username);
            Client = new ImapClient();
            AddEvents();
            Client.Connect(Host, Port, MailKit.Security.SecureSocketOptions.Auto);
            Client.AuthenticationMechanisms.Remove("XOAUTH2");
            Client.Authenticate(username, password);
        }

        public async Task<string?> GetCodeAsync(float delay = 5, int maxTries = -1)
        {
            int tries = 0;
            while (Client?.IsAuthenticated == true && Client?.IsConnected == true && Client.Inbox != null)
            {
                Logger?.Info("Checking inbox for email regarding the Steam Auth Code...");
                await Client.Inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);
                foreach (var msg in (await Client.Inbox.SearchAsync(SearchQuery.All)).Reverse().Take(5).ToList())
                {
                    try
                    {
                        var message = await Client.Inbox.GetMessageAsync(msg);
                        if (HandleMessage(message) is string code && !string.IsNullOrWhiteSpace(code))
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
                    Logger?.Warning("Maximum number of tries reached ({0}). Stopping search for Steam Auth Code.", maxTries);
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }
            return null;
        }

        private string? HandleMessage(MimeMessage message)
        {
            if (message?.Subject?.StartsWith(Subject, StringComparison.OrdinalIgnoreCase) == true)
            {
                Logger?.Info("Found an email containing the code! Extracting...");
                return ExtractCode(message);
            }
            return null;
        }

        private string? ExtractCode(MimeMessage message)
        {
            if (message.BodyParts.FirstOrDefault(x => x.ContentType.MimeType == "text/plain") is TextPart body)
            {
                var code = SteamAuthCode().Match(body.Text)?.Groups?["code"];
                if (code?.Success == true)
                {
                    Logger?.Info("Extracted the Steam Auth Code: {0}", code.Value);
                    return code.Value;
                }
            }
            return null;
        }

        private void AddEvents()
        {
            if (Client == null)
                return;

            Client.Authenticated += (_, e) => Logger?.Info("IMAP Authenticated");
            Client.Connected += (_, e) => Logger?.Info("Connected to IMAP");
            Client.Disconnected += (_, e) => Logger?.Info("Disconnected from IMAP");
        }

        [GeneratedRegex("Login Code\\n(?'code'.*)", RegexOptions.Multiline)]
        private static partial Regex SteamAuthCode();
    }
}