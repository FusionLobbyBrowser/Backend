using System.Text.Json;

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

        internal static Settings DefaultSettings { get; } = new()
        {
            Interval = 30,
            ModIO_Token = "your-token",
            Authentication = new Auth()
            {
                Username = "",
                Password = "",
            }
        };

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            try
            {
                LoadSettings();
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
                AnsiConsole.MarkupLine("[grey]Selected log level: [/]" + level.ToString());

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
                AnsiConsole.MarkupLine("[grey]Selected service: [/]" + choice);

                Dictionary<string, string> metadata = [];

                if (choice.StartsWith("SteamKit"))
                {
                    AnsiConsole.MarkupLine("[grey]Connecting with SteamKit[/]");
                    FusionClient = new Fusion(new SteamKitHandler());
                    ((SteamKitHandler)FusionClient.Handler).Authenticator = new CustomUserAuth(
                        () =>
                        {
                            AnsiConsole.MarkupLine("[bold yellow] > Awaiting device confirmation on Steam Guard, press any key when accepted...[/]");
                            Console.ReadKey(true);
                            return Task.FromResult(true);
                        },
                        (_) => AnsiConsole.PromptAsync(new TextPrompt<string>("[bold yellow] > Enter the code from your authenticator: [/]")),
                        (email, _) => AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"))
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
                    AnsiConsole.MarkupLine("[grey]Using provided login to authenticate[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[lime]Connecting with Steamworks[/]");
                    FusionClient = new Fusion(new SteamworksHandler());
                }

                var logger = new Logger(level);
                await FusionClient.Initialize(logger, metadata);
                AnsiConsole.MarkupLine("[lime]Successfully initialized Fusion API[/]");
                Uptime = DateTime.UtcNow;
            }
            catch (Exception e)
            {
                AnsiConsole.MarkupLine("[red]Failed to initialize Fusion API, exception: [/]");
                AnsiConsole.WriteException(e);
                AnsiConsole.MarkupLine("[red]Press any key to quit the program[/]");
                Console.ReadKey(false);
                return;
            }

            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Spectre(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

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
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            app.MapControllers();

            var token = new CancellationTokenSource();
            _ = GetLobbies(token.Token);

            await app.RunAsync();

            await token.CancelAsync();
            token.Dispose();
        }

        private static async Task GetLobbies(CancellationToken token)
        {
            LoadSettings();
            while (FusionClient != null && FusionClient.Handler?.IsInitialized == true && !token.IsCancellationRequested)
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
                await Task.Delay((Settings?.Interval ?? 30) * 1000, token);
            }
        }

        private static void LoadSettings()
        {
            try
            {
                if (Logger != null)
                    Logger?.Information("Loading settings...");
                else
                    AnsiConsole.WriteLine("Loading settings...");
                var path = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
                if (!File.Exists(path))
                {
                    if (Logger != null)
                        Logger?.Information("Settings file is missing, creating new and exiting application...");
                    else
                        AnsiConsole.WriteLine("Settings file is missing, creating new and exiting application...");
                    using var stream = File.CreateText(path);
                    var serialized = JsonSerializer.Serialize(DefaultSettings);
                    stream.Write(serialized);
                    stream.Flush();
                    stream.Close();
                    Environment.Exit(0);
                    return;
                }
                else
                {
                    Settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
                    if (Logger != null)
                        Logger?.Information("Successfully loaded settings!");
                    else
                        AnsiConsole.WriteLine("Successfully loaded settings!");
                }
            }
            catch (Exception ex)
            {
                if (Logger != null)
                    Logger?.Error(ex, "Failed to set settings from file");
                else
                    AnsiConsole.WriteException(ex);
            }

        }
    }
}
