using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using FLB_API.Discord;
using FLB_API.Managers;
using FLB_API.Statistics;

using FusionAPI;
using FusionAPI.Data.Containers;
using FusionAPI.Interfaces;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using Serilog;
using Serilog.Sinks.Spectre;

using Spectre.Console;

using ILogger = FusionAPI.Interfaces.ILogger;

namespace FLB_API
{
    public static class Program
    {
        public static Fusion? SteamClient { get; private set; }

        public static Fusion? EpicClient { get; private set; }

        internal static Serilog.Core.Logger? Logger { get; private set; }

        internal static Logger? SteamLogger { get; private set; }

        internal static LobbyListResponse? SteamLobbies { get; private set; }

        internal static LobbyListResponse? EpicLobbies { get; private set; }

        internal static LobbyListResponse? Lobbies { get; private set; }

        internal static LobbyListResponse? FriendsOnlyLobbies { get; private set; }

        internal static DateTime Uptime { get; private set; }

        internal static Settings? Settings { get; private set; }

        internal static IMAPManager? ImapManager { get; private set; }

        internal static CancellationTokenSource? AuthCancel { get; private set; }

        internal static StatisticsManager Statistics { get; private set; }

        internal static Settings DefaultSettings { get; } = new()
        {
            Interval = 30,
            ModIoToken = "your-token",
            Authentication = new Auth()
            {
                Username = "",
                Password = "",
            },
            Imap = new ImapAuth()
            {
                Host = "imap.gmail.com",
                Port = 993,
                Username = "",
                Password = ""
            }
        };

        internal static List<IMatchmakingHandler> Handlers { get; } = [];

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

                ILogger.LogLevel level;
                string choice;
                var preferences = false;

                if (Settings?.Preferences?.Use == true)
                {
                    preferences = true;
                    Logger?.Information("Using saved preferences");
                    level = Settings.Preferences.LogLevel;
                    choice = Settings.Preferences.AuthHandler;
                    Logger?.Information("Selected log level: {0}, Selected service: {1}", level.ToString(), choice);
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
                    SteamClient = new Fusion(new SteamKitHandler());
                    metadata = await SetupSteamKit();
                }
                else
                {
                    Logger?.Information("Connecting with Steamworks");
                    SteamClient = new Fusion(new SteamworksHandler());
                }
                Handlers.Add(SteamClient.Handler);

                SteamLogger = new Logger(level, "Steam");
                await SteamClient.Initialize(SteamLogger, metadata);
                Logger?.Information("Successfully initialized Steam Fusion API! Initializing EOS (Epic Online Services)...");
                EpicClient = new Fusion(new EOSHandler());
                var eosLogger = new Logger(level, "EOS");
                await EpicClient.Initialize(eosLogger, []);
                Logger?.Information("Successfully initialized EOS API");
                Handlers.Add(EpicClient.Handler);
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

            builder.Services
                .AddAuthentication(options => options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.None;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.ExpireTimeSpan = TimeSpan.FromDays(30);
                    options.SlidingExpiration = true;
                    options.Cookie.Name = "SteamAuth";
                    options.LoginPath = "/steam/login";
                    options.LogoutPath = "/steam/logout";

                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                })
                .AddSteam(options => options.ApplicationKey = Settings?.SteamWebApiToken);

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            var app = builder.Build();

            app.UseHttpsRedirection();

            List<string> origins = [
                "https://fusion.hahoos.dev",
                "https://hoodrp.com",
                "https://www.hoodrp.com"
            ];

            if (app.Environment.IsDevelopment())
                origins.Add("http://localhost:5500");

            app.UseCors((policyBuilder) =>
                policyBuilder
                    .WithOrigins([.. origins])
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
            );

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
                app.MapOpenApi();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            if (Settings?.StoreStatistics == true && !string.IsNullOrWhiteSpace(Settings?.ConnectionString))
            {
                Statistics = new StatisticsManager(new Logger("STATISTICS"));
                await Statistics.Init(Settings.ConnectionString);
                await Statistics.RegisterFromAssembly(Assembly.GetExecutingAssembly());
                _ = Statistics.Migrate();
            }

            var token = new CancellationTokenSource();
            _ = GetLobbies(token.Token);

            _ = ModIOManager.Setup();

            await app.RunAsync();

            await token.CancelAsync();
            token.Dispose();
        }

        public static string ReplaceRegex(this string text, string pattern, string replacement)
            => Regex.Replace(text, pattern, replacement);

        private static async Task<Dictionary<string, string>> SetupSteamKit()
        {
            Dictionary<string, string> metadata = [];
            Logger?.Information("Connecting with SteamKit");
            if (SteamClient == null)
                return [];
            ((SteamKitHandler)SteamClient.Handler).Authenticator = new CustomUserAuth(
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

        private static async Task<Tuple<ILogger.LogLevel, string>> AskUser()
        {
            // I FUCKING HATE VISUAL STUDIO, WHY DO I HAVE TO DISABLE 3 FUCKING WARNINGS.
#pragma warning disable RCS1222
#pragma warning disable IDE0079
#pragma warning disable S3878
            var level = await AnsiConsole.PromptAsync(new SelectionPrompt<ILogger.LogLevel>()
                .Title("Logger level for the service responsible for connecting to:")
                .AddChoices(
                [
                    ILogger.LogLevel.Trace,
                    ILogger.LogLevel.Info,
                    ILogger.LogLevel.Warning,
                    ILogger.LogLevel.Error
                ])
                );
            Logger?.Information("Selected log level: {0}", level.ToString());

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
            Logger?.Information("Selected service: {0}", choice);
            return new Tuple<ILogger.LogLevel, string>(level, choice);
        }

        private static async Task SavePreferences(ILogger.LogLevel level, string choice)
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
                if (SteamClient is { Handler.IsInitialized: true })
                {
                    try
                    {
                        List<LobbyInfo> friendsOnly = [];
                        var steamTask = Task.Run(async () =>
                        {
                            if (SteamClient.Handler?.IsInitialized == true)
                            {
                                SteamLobbies = new LobbyListResponse(await SteamClient.FetchLobbies("Steam") ?? [],
                                    SteamClient.Handler.LastFetch, Settings?.Interval ?? 30);
                                friendsOnly = (await SteamClient.FetchLobbies("Steam", true)).ToList() ?? [];
                            }
                            else
                            {
                                Logger?.Warning("Steam Client is not initialized, skipping lobby fetch...");
                            }
                        }, token);

                        var epicTask = Task.Run(async () =>
                        {
                            if (EpicClient is { Handler.IsInitialized: true })
                                EpicLobbies = new LobbyListResponse(await EpicClient.FetchLobbies("EOS") ?? [],
                                    EpicClient.Handler.LastFetch, Settings?.Interval ?? 30);
                            else
                                Logger?.Warning("EOS Client is not initialized, skipping lobby fetch...");
                        }, token);

                        await Task.WhenAll(steamTask, epicTask);

                        FriendsOnlyLobbies = new LobbyListResponse([.. friendsOnly], SteamClient?.Handler?.LastFetch ?? Uptime, Settings?.Interval ?? 30);
                        Lobbies = new LobbyListResponse((SteamLobbies?.Lobbies ?? []).Concat(EpicLobbies?.Lobbies ?? []).ToArray<LobbyInfo>() ?? [], EpicClient?.Handler?.LastFetch ?? Uptime, Settings?.Interval ?? 30);

                        Logger?.Information("Combined all available lobbies ({0})", Lobbies.Lobbies.Length);
                        if (Settings?.StoreStatistics == true &&
                            !string.IsNullOrWhiteSpace(Settings?.ConnectionString) && Statistics != null)
                        {
                            _ = Statistics.Analyze(Lobbies, false);
                            if (Statistics.IsMigrating)
                                Statistics.AdditionalToMigrate.Add(Lobbies);
                        }

                        if (DiscordBotManager.Client != null && DiscordBotManager.Client.Status == NetCord.Gateway.WebSocketStatus.Ready)
                        {
                            await DiscordBotManager.Status();
                        }
                        else
                        {
                            if (Settings?.Preferences?.LaunchDiscordBot == true)
                            {
                                Logger?.Information("Setting up discord bot");
                                _ = DiscordBotManager.Setup();
                            }
                        }
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

        private static async Task<LobbyInfo[]> FetchLobbies(this Fusion? client, string name, bool friendsOnly = false)
        {
            if (client == null)
                return [];

            Logger?.Information($"Fetching {name} lobbies... {(friendsOnly ? "(Friends Only)" : "(Public)")}");
            LobbyInfo[] lobbies;
            try
            {
                if (!friendsOnly)
                    lobbies = await client.GetLobbies();
                else
                    lobbies = await client.GetLobbies(publicLobbies: false, friendsOnlyLobbies: true);
            }
            catch (Exception e)
            {
                Logger?.Error(e, $"Failed to fetch lobbies from {name} {(friendsOnly ? "(Friends Only)" : "(Public)")}");
                return [];
            }

            Logger?.Information($"Successfully fetched {name} lobbies ({lobbies.Length})... {(friendsOnly ? "(Friends Only)" : "(Public)")}");

            return lobbies;
        }

        private static async Task<string> GetCodeFromEmail(string email, bool previousCodeWasIncorrect)
        {
            if (AuthCancel != null)
                await AuthCancel.CancelAsync();

            if (ImapEmpty())
            {
                SteamLogger?.Warning("Empty IMAP Configuration, falling back to manual input...");
                AuthCancel = new CancellationTokenSource();
                return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
            }
            else
            {
                try
                {
                    SteamLogger?.Info("Using IMAP to fetch Steam Auth Code...");
                    await Task.Delay((int)3.5f * 1000);
                    ImapManager ??= new IMAPManager(
                        Settings!.Imap!.Host!,
                        Settings.Imap.Port,
                        SteamLogger
                        );

                    ImapManager.LogIn(Settings!.Imap!.Username!, Settings!.Imap!.Password!);
                    var code = await ImapManager.GetCodeAsync();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        SteamLogger?.Error("Failed to retrieve the code from email, please check the email and type in the code manually");
                        AuthCancel = new CancellationTokenSource();
                        return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
                    }
                    SteamLogger?.Info("Successfully retrieved Steam Auth Code from email");
                    return code;
                }
                catch (Exception ex)
                {
                    SteamLogger?.Error("Failed to retrieve the code from email", ex);
                    SteamLogger?.Info("Falling back to manual input...");
                    AuthCancel = new CancellationTokenSource();
                    return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
                }
            }
        }

        private static bool ImapEmpty()
            => string.IsNullOrWhiteSpace(Settings?.Imap?.Host) ||
               string.IsNullOrWhiteSpace(Settings?.Imap?.Username) ||
               string.IsNullOrWhiteSpace(Settings?.Imap?.Password);

        private static void LoadSettings()
        {
            try
            {
                Logger?.Information("Loading settings...");
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
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

        internal static ContentResult CreateResult(string message, int statusCode = 200, string contentType = "text/plain")
        {
            return new ContentResult()
            {
                StatusCode = statusCode,
                Content = message,
                ContentType = contentType
            };
        }
    }
}