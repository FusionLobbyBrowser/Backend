using System.Reflection.Emit;
using System.Text.Json;

using FLB_API.Managers;

using FusionAPI;
using FusionAPI.Data.Containers;

using Serilog;
using Serilog.Sinks.Spectre;

using Spectre.Console;

namespace FLB_API
{
    public static class Program
    {
        public static Fusion? FusionClient { get; private set; }

        internal static Serilog.Core.Logger? Logger { get; private set; }

        internal static LobbyInfo[]? Lobbies { get; private set; }

        internal static DateTime Date { get; private set; } = DateTime.UtcNow;

        internal static PlayerCount? PlayerCount { get; private set; }

        internal static DateTime Uptime { get; private set; }

        internal static Settings? Settings { get; private set; }

        internal static IMAPManager? ImapManager { get; private set; }

        internal static CancellationTokenSource? AuthCancel { get; private set; }

        internal static Settings DefaultSettings { get; } = new()
        {
            Interval = 30,
            ModIO_Token = "your-token",
            Authentication = new Auth()
            {
                Username = "",
                Password = "",
            },
            IMAP = new ImapAuth()
            {
                Host = "imap.gmail.com",
                Port = 993,
                Username = "",
                Password = ""
            }
        };

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Spectre(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Add services to the container.

            try
            {
                LoadSettings();

                FusionAPI.Interfaces.ILogger.LogLevel level;
                string choice;
                bool preferences = false;

                if (Settings?.Preferences?.Use == true)
                {
                    preferences = true;
                    Logger?.Information("Using saved preferences");
                    level = Settings.Preferences.LogLevel;
                    choice = Settings.Preferences.AuthHandler;
                    Logger?.Information("Selected log level: " + level.ToString() + ", Selected service: " + choice);
                }
                else
                {
                    var choices = await AskUser();
                    level = choices.Item1;
                    choice = choices.Item2;
                }

                if (!preferences && Settings?.Preferences?.Use != false)
                {
                    var answer = await AnsiConsole.ConfirmAsync("[bold yellow]Would you like to save these settings to settings.json for next launch?[/]", true);
                    if (answer && Settings != null)
                        await SavePreferences(level, choice);
                }

                Dictionary<string, string> metadata = [];

                if (choice.StartsWith("SteamKit"))
                {
                    FusionClient = new Fusion(new SteamKitHandler());
                    metadata = await SetupSteamKit();
                }
                else
                {
                    Logger?.Information("Connecting with Steamworks");
                    FusionClient = new Fusion(new SteamworksHandler());
                }

                var logger = new Logger(level);
                await FusionClient.Initialize(logger, metadata);
                Logger?.Information("Successfully initialized Fusion API");
                Uptime = DateTime.UtcNow;
            }
            catch (Exception e)
            {
                Logger?.Error(e, "Failed to initialize Fusion API");
                AnsiConsole.MarkupLine("[red]Press any key to quit the program[/]");
                Console.ReadKey(false);
                return;
            }

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Logger);

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            var app = builder.Build();

            app.UseCors((builder) =>
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
            );

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
                app.MapOpenApi();

            app.UseHttpsRedirection();

            app.MapControllers();

            var token = new CancellationTokenSource();
            _ = GetLobbies(token.Token);

            await app.RunAsync();

            await token.CancelAsync();
            token.Dispose();
        }

        private static async Task<Dictionary<string, string>> SetupSteamKit()
        {
            Dictionary<string, string> metadata = [];
            Logger?.Information("Connecting with SteamKit");
            if (FusionClient == null)
                return [];
            ((SteamKitHandler)FusionClient.Handler).Authenticator = new CustomUserAuth(
                () =>
                {
                    AuthCancel?.Cancel();
                    AnsiConsole.MarkupLine("[bold yellow] > Awaiting device confirmation on Steam Guard, press any key when accepted...[/]");
                    Console.ReadKey(true);
                    return Task.FromResult(true);
                },
                async (_) =>
                {
                    if (AuthCancel != null)
                        await AuthCancel.CancelAsync();
                    AuthCancel = new CancellationTokenSource();
                    return await AnsiConsole.PromptAsync(new TextPrompt<string>("[bold yellow] > Enter the code from your authenticator: [/]"), AuthCancel.Token);
                },
                GetCodeFromEmail
                );
            if (string.IsNullOrWhiteSpace(Settings?.Authentication?.Username) || string.IsNullOrWhiteSpace(Settings?.Authentication?.Password))
            {
                metadata.Add("username", await AnsiConsole.PromptAsync(new TextPrompt<string>("[bold yellow]Steam Username:[/] ")));
                metadata.Add("password", await AnsiConsole.PromptAsync(new TextPrompt<string>("[bold yellow]Steam Password:[/] ")));
            }
            else
            {
                metadata.Add("username", Settings.Authentication.Username);
                metadata.Add("password", Settings.Authentication.Password);
            }
            Logger?.Information("Using provided login to authenticate");
            return metadata;
        }

        private static async Task<Tuple<FusionAPI.Interfaces.ILogger.LogLevel, string>> AskUser()
        {
            // I FUCKING HATE VISUAL STUDIO, WHY DO I HAVE TO DISABLE 3 FUCKING WARNINGS.
#pragma warning disable RCS1222
#pragma warning disable IDE0079
#pragma warning disable S3878
            var level = await AnsiConsole.PromptAsync(new SelectionPrompt<FusionAPI.Interfaces.ILogger.LogLevel>()
                .Title("Logger level for the service responsible for connecting to:")
                .AddChoices(
                [
                        FusionAPI.Interfaces.ILogger.LogLevel.Trace,
                        FusionAPI.Interfaces.ILogger.LogLevel.Info,
                        FusionAPI.Interfaces.ILogger.LogLevel.Warning,
                        FusionAPI.Interfaces.ILogger.LogLevel.Error
                ])
                );
            Logger?.Information("Selected log level: " + level.ToString());

            var choice = await AnsiConsole.PromptAsync(new SelectionPrompt<string>()
                .Title("Select how to connect to the Steam API:")
                .AddChoices(
                [
                    "Steamworks (no auth, requires steam client open)",
                        "SteamKit (requires auth, steam client not required)"
                ]));
#pragma warning restore S3878
#pragma warning restore IDE0079
#pragma warning restore RCS1222
            Logger?.Information("Selected service: " + choice);
            return new(level, choice);
        }

        private static async Task SavePreferences(FusionAPI.Interfaces.ILogger.LogLevel level, string choice)
        {
            if (Settings == null)
                return;

            Logger?.Information("Saving settings...");
            var path = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
            Settings.Preferences = new Preferences()
            {
                Use = true,
                LogLevel = level,
                AuthHandler = choice.StartsWith("SteamKit") ? "SteamKit" : "Steamworks"
            };
            await using var stream = File.CreateText(path);
            var serialized = JsonSerializer.Serialize(Settings);
            await stream.WriteAsync(serialized);
            await stream.FlushAsync();
            stream.Close();
            Logger?.Information("Successfully saved settings!");
        }

        private static async Task GetLobbies(CancellationToken token)
        {
            LoadSettings();
            while (!token.IsCancellationRequested)
            {
                if (FusionClient != null && FusionClient.Handler?.IsInitialized == true)
                {
                    try
                    {
                        Logger?.Information("Fetching lobbies...");
                        var lobbies = await FusionClient.GetLobbies(includeFull: true, includePrivate: false, includeSelf: true);
                        int players = 0;
                        foreach (var lobby in lobbies)
                            players += lobby.PlayerCount;
                        PlayerCount = new(players, lobbies.Length);
                        Date = DateTime.UtcNow;
                        Lobbies = lobbies;
                        Logger?.Information($"Successfully fetched lobbies ({lobbies.Length})...");
                        LoadSettings();
                    }
                    catch (Exception e)
                    {
                        Logger?.Error(e, "Failed to fetch LabFusion lobbies.");
                    }
                }
                else
                {
                    Logger?.Warning("Fusion Client is not initialized, skipping lobby fetch...");
                }
                await Task.Delay((Settings?.Interval ?? 30) * 1000, token);
            }
        }

        private static async Task<string> GetCodeFromEmail(string email, bool previousCodeWasIncorrect)
        {
            if (AuthCancel != null)
                await AuthCancel.CancelAsync();

            if (IMAPEmpty())
            {
                Logger?.Warning("Empty IMAP Configuration, falling back to manual input...");
                AuthCancel = new CancellationTokenSource();
                return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
            }
            else
            {
                try
                {
                    Logger?.Information("Using IMAP to fetch Steam Auth Code...");
                    ImapManager ??= new IMAPManager(
                        Settings!.IMAP!.Host!,
                        Settings.IMAP.Port,
                        Logger
                        );

                    ImapManager.LogIn(Settings!.IMAP!.Username!, Settings!.IMAP!.Password!);
                    var code = await ImapManager.GetCodeAsync();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        Logger?.Error("Failed to retrieve the code from email, please check the email and type in the code manually");
                        AuthCancel = new CancellationTokenSource();
                        return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
                    }
                    Logger?.Information("Successfully retrieved Steam Auth Code from email");
                    return code;
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "Failed to retrieve the code from email");
                    Logger?.Information("Falling back to manual input...");
                    AuthCancel = new CancellationTokenSource();
                    return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
                }
            }
        }

        private static bool IMAPEmpty()
            => string.IsNullOrWhiteSpace(Settings?.IMAP?.Host) ||
               string.IsNullOrWhiteSpace(Settings?.IMAP?.Username) ||
               string.IsNullOrWhiteSpace(Settings?.IMAP?.Password);

        private static void LoadSettings()
        {
            try
            {
                Logger?.Information("Loading settings...");
                var path = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
                if (!File.Exists(path))
                {
                    Logger?.Information("Settings file is missing, creating new and exiting application...");
                    using var stream = File.CreateText(path);
                    var serialized = JsonSerializer.Serialize(DefaultSettings);
                    stream.Write(serialized);
                    stream.Flush();
                    stream.Close();
                    Environment.Exit(0);
                }
                else
                {
                    Settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
                    Logger?.Information("Successfully loaded settings!");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to set settings from file");
            }
        }
    }
}