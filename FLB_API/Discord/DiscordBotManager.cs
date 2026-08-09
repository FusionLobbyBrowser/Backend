using FLB_API.Discord.Commands;
using FLB_API.Discord.Interactions;

using FusionAPI.Data.Containers;

using NetCord;
using NetCord.Gateway;
using NetCord.Logging;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

using LogLevel = NetCord.Logging.LogLevel;

namespace FLB_API.Discord
{
    public static class DiscordBotManager
    {
        public static GatewayClient? Client { get; private set; }

        public static Logger? Logger { get; private set; }

        public static async Task Setup()
        {
            Logger = new Logger("DISCORD");
            if (string.IsNullOrWhiteSpace(Program.Settings?.DiscordBotToken))
            {
                Logger.Error("The discord bot token cannot be empty!");
                return;
            }

            Client = new GatewayClient(new BotToken(Program.Settings.DiscordBotToken), new GatewayClientConfiguration
            {
                Intents = null,
                Logger = new SerilogLogger(Logger) { Level = LogLevel.Trace },
            });
            ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext> applicationCommandService = new();
            applicationCommandService.AddModule<LobbiesCommandModule>();
            applicationCommandService.AddModule<OtherCommandModule>();

            ComponentInteractionService<ButtonInteractionContext> interactionService = new();
            interactionService.AddModule<LobbiesInteractionModule>();
            interactionService.AddModule<UniversalInteractionModule>();

            Client.InteractionCreate += async interaction =>
            {
                IExecutionResult result;

                switch (interaction)
                {
                    // Check if the interaction is an application command interaction
                    case ApplicationCommandInteraction applicationCommandInteraction:
                        result = await applicationCommandService.ExecuteAsync(new ApplicationCommandContext(applicationCommandInteraction, Client));
                        break;

                    case ButtonInteraction buttonInteraction:
                        result = await interactionService.ExecuteAsync(new ButtonInteractionContext(buttonInteraction, Client));
                        break;

                    case AutocompleteInteraction autocompleteInteraction:
                        result = await applicationCommandService.ExecuteAutocompleteAsync(new AutocompleteInteractionContext(autocompleteInteraction, Client));
                        break;

                    default:
                        return;
                }

                // Check if the execution failed
                if (result is not IFailResult failResult)
                    return;

                // Return the error message to the user if the execution failed
                try
                {
                    await interaction.SendResponseAsync(InteractionCallback.Message(Error(failResult.Message, "Unexpected Error!", true)));
                }
                catch
                {
                    // ignored
                }
            };
            Client.Ready += async ready => await Status();

            await applicationCommandService.RegisterCommandsAsync(Client.Rest, Client.Id);
            await Client.StartAsync();
        }

        public static async Task Status()
        {
            if (Client != null)
                await Client.UpdatePresenceAsync(
                    new PresenceProperties(UserStatusType.Online)
                    {
                        Activities = [new UserActivityProperties($"over {Program.Lobbies?.Lobbies?.Length ?? 0} lobbies!", UserActivityType.Watching)]
                    });
        }

        public static string GetLobbyName(this LobbyInfo lobby)
            => string.IsNullOrWhiteSpace(lobby.LobbyName) ? $"{LobbiesCommandModule.RemoveUnityRichText(lobby.LobbyHostName)}'s Lobby" : LobbiesCommandModule.RemoveUnityRichText(lobby.LobbyName);

        public static InteractionMessageProperties Error(string message, string title = "Error!", bool showReportMsg = false)
        {
            if (showReportMsg)
                message += "\n\nIf the issue persists, contact me by DMing me on discord (@hahoos) or report the issue on [Github](https://github.com/FusionLobbyBrowser/Backend)!";
            return new InteractionMessageProperties().AddEmbeds(new EmbedProperties()
            {
                Title = title,
                Description = message,
                Url = "https://fusion.hahoos.dev/",
                Color = new Color(255, 82, 38),
                Timestamp = DateTimeOffset.Now,
            }).WithFlags(MessageFlags.Ephemeral);
        }
    }

    public class SerilogLogger(Logger? logger = null) : IGatewayLogger, IRestLogger, IVoiceLogger
    {
        public Logger Logger = logger ?? new Logger("DISCORD");

        public LogLevel Level { get; set; } = LogLevel.Trace;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= Level;

        public void Log<TState>(LogLevel logLevel, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            switch (logLevel)
            {
                case LogLevel.Information:
                    Logger?.Info(formatter.Invoke(state, exception));
                    break;

                case LogLevel.Warning:
                    Logger?.Warning(formatter.Invoke(state, exception));
                    break;

                case LogLevel.Error:
                    Logger?.Error(formatter.Invoke(state, exception));
                    break;

                case LogLevel.Debug:
                    Logger?.Debug(formatter.Invoke(state, exception));
                    break;

                case LogLevel.Trace:
                    Logger?.Trace(formatter.Invoke(state, exception));
                    break;

                case LogLevel.Critical:
                    Logger?.Error($"[CRITICAL] {formatter.Invoke(state, exception)}");
                    break;

                case LogLevel.None:
                    Logger?.Info($"[NONE] {formatter.Invoke(state, exception)}");
                    break;
            }
        }
    }
}