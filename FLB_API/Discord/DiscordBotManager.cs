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

            Client = new GatewayClient(new BotToken(Program.Settings.DiscordBotToken), new()
            {
                Intents = default,
                Logger = new SerilogLogger()
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

                // Check if the interaction is an application command interaction
                if (interaction is ApplicationCommandInteraction applicationCommandInteraction)
                    result = await applicationCommandService.ExecuteAsync(new ApplicationCommandContext(applicationCommandInteraction, Client));
                else if (interaction is ButtonInteraction buttonInteraction)
                    result = await interactionService.ExecuteAsync(new ButtonInteractionContext(buttonInteraction, Client));
                else if (interaction is AutocompleteInteraction autocompleteInteraction)
                    result = await applicationCommandService.ExecuteAutocompleteAsync(new AutocompleteInteractionContext(autocompleteInteraction, Client));
                else
                    return;

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
                }
            };
            Client.Ready += async ready => await Status();

            await applicationCommandService.RegisterCommandsAsync(Client.Rest, Client.Id);
            await Client.StartAsync();
        }

        public static async Task Status()
        { 
            if(Client != null)
                await Client.UpdatePresenceAsync(
                    new PresenceProperties(UserStatusType.Online) {
                        Activities = [new($"over {Program.Lobbies?.Lobbies?.Length ?? 0} lobbies!", UserActivityType.Watching)] }); 
        }

        public static string GetLobbyName(this LobbyInfo lobby)
            => string.IsNullOrWhiteSpace(lobby.LobbyName) ? $"{LobbiesCommandModule.RemoveUnityRichText(lobby.LobbyHostName)}'s Lobby" : LobbiesCommandModule.RemoveUnityRichText(lobby.LobbyName);

        public static InteractionMessageProperties Error(string message, string title = "Error!", bool showReportMsg = false)
        {
            if (showReportMsg)
                message += "\n\nIf the issue persists, contact me by DMing me on discord (@hahoos) or report the issue on [github](https://github.com/FusionLobbyBrowser/Backend)!";
            return new InteractionMessageProperties().AddEmbeds(new EmbedProperties()
            {
                Title = title,
                Description = message,
                Url = "https://fusion.hahoos.dev/",
                Color = new(255, 82, 38),
                Timestamp = DateTimeOffset.Now,
            }).WithFlags(MessageFlags.Ephemeral);
        }
    }

    public class SerilogLogger : IGatewayLogger, IRestLogger, IVoiceLogger
    {
        public LogLevel Level { get; set; } = LogLevel.Trace;

        public bool IsEnabled(NetCord.Logging.LogLevel logLevel)
            => logLevel >= Level;

        public void Log<TState>(NetCord.Logging.LogLevel logLevel, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            const string prefix = "[DISCORD]";
            if (logLevel == LogLevel.Information)
                Program.Logger?.Information($"{prefix} " + formatter.Invoke(state, exception));
            else if (logLevel == LogLevel.Warning)
                Program.Logger?.Warning($"{prefix} " + formatter.Invoke(state, exception));
            else if (logLevel == LogLevel.Error)
                Program.Logger?.Error($"{prefix} " + formatter.Invoke(state, exception));
            else if (logLevel == LogLevel.Debug)
                Program.Logger?.Debug($"{prefix} " + formatter.Invoke(state, exception));
            else if (logLevel == LogLevel.Trace)
                Program.Logger?.Debug($"{prefix} [TRACE] {formatter.Invoke(state, exception)}");
            else if (logLevel == LogLevel.Critical)
                Program.Logger?.Error($"{prefix} [CRITICAL] {formatter.Invoke(state, exception)}");
            else if (logLevel == LogLevel.None)
                Program.Logger?.Information($"{prefix} [NONE] {formatter.Invoke(state, exception)}");
        }
    }
}