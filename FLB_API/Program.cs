using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using FLB_API.Blacklist;
using FLB_API.Discord;
using FLB_API.Managers;

using FusionAPI;
using FusionAPI.Handlers;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

using Serilog;
using Serilog.Sinks.Spectre;

using Spectre.Console;

using ILogger = FusionAPI.Data.Interfaces.ILogger;

namespace FLB_API
{
    public static class Program
    {
        internal static Serilog.Core.Logger? Logger { get; private set; }

        internal static LobbyListResponse? Lobbies { get; private set; }

        internal static LobbyListResponse? FriendsOnlyLobbies { get; private set; }

        internal static List<Platform> Platforms { get; } = [];

        internal static DateTime Uptime { get; private set; }

        internal static Settings? Settings { get; private set; }

        internal static BlacklistManager? Blacklist { get; private set; }

        internal static IMAPManager? ImapManager { get; private set; }

        internal static CancellationTokenSource? AuthCancel { get; private set; }

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

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Spectre()
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

                Fusion steamClient;
                if (choice.StartsWith("SteamKit"))
                {
                    steamClient = new Fusion(new SteamKitHandler());
                    metadata = await SetupSteamKit(steamClient);
                }
                else
                {
                    Logger?.Information("Connecting with Steamworks");
                    steamClient = new Fusion(new SteamworksHandler());
                }

                Platforms.Add(await Platform.CreateAndInit(steamClient, PlatformType.Steam, level, metadata));
                Logger?.Information("Successfully initialized Steam Fusion API! Initializing EOS (Epic Online Services)...");

                Platforms.Add(await Platform.CreateAndInit(new Fusion(new EpicHandler()), PlatformType.Epic, level, []));
                Logger?.Information("Successfully initialized EOS API");

                Uptime = DateTime.UtcNow;
                Blacklist = new BlacklistManager(new Logger(level, "Blacklist"));
                await Blacklist.RegisterFromAssembly(typeof(Program).Assembly);
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

                    options.Events.OnRedirectToLogin = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        var details = new ProblemDetails()
                        {
                            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                            Detail = "You are not authorized to access this endpoint.",
                            Title = "Not Authorized",
                            Status = StatusCodes.Status401Unauthorized,
                            Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
                            Extensions = new Dictionary<string, object?>(){
                                { "traceId", context.HttpContext.TraceIdentifier },
                                { "timestamp", DateTime.UtcNow },
                            }
                        };
                        await context.Response.WriteAsJsonAsync(details);
                    };
                })

                .AddSteam(options => options.ApplicationKey = Settings?.SteamWebApiToken);

            builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddOpenApi(options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1);
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Fusion Lobby Browser API",
                    Version = "v1",
                    Description = "An API for fetching LabFusion lobbies"
                });
            });
            builder.Services.AddProblemDetails(options =>
            {
                options.CustomizeProblemDetails = ctx =>
                {
                    if (ctx.ProblemDetails.Status == 404)
                        ctx.ProblemDetails.Detail = "This endpoint does not exist.";
                    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
                    ctx.ProblemDetails.Extensions["timestamp"] = DateTime.UtcNow;
                    ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
                };
            });

            var app = builder.Build();

            app.UseHttpsRedirection();

            List<string> origins = [
                "https://fusion.hahoos.dev",
                "https://hoodrp.com",
                "https://www.hoodrp.com"
            ];

            if (app.Environment.IsDevelopment())
                origins.AddRange(["http://localhost:3000"]);

            app.UseCors((policyBuilder) =>
                policyBuilder
                    .WithOrigins([.. origins])
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
            );

            app.MapOpenApi();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseSwagger();
            app.MapControllers();

            app.UseSwaggerUI();
            app.UseStatusCodePages();

            var token = new CancellationTokenSource();
            _ = GetLobbies(token.Token);

            _ = ThumbnailManager.Setup();

            await app.RunAsync();

            await token.CancelAsync();
            token.Dispose();
        }

        public static string ReplaceRegex(this string text, string pattern, string replacement)
            => Regex.Replace(text, pattern, replacement);

        private static async Task<Dictionary<string, string>> SetupSteamKit(Fusion? client)
        {
            Dictionary<string, string> metadata = [];
            Logger?.Information("Connecting with SteamKit");
            if (client == null)
                return [];
            ((SteamKitHandler)client.Handler).Authenticator = new CustomUserAuth(
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
                async (email, _) => await GetCodeFromEmail(client.Handler.Logger, email)
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
                if (Platforms.Any(x => x?.Client?.Handler?.IsInitialized == true))
                {
                    try
                    {
                        List<CustomLobbyInfo> friendsOnly = [];
                        List<Task<CustomLobbyInfo[]>> platforms = [];
                        Platforms.ForEach(x => platforms.Add(x.FetchLobbies()));

                        await Task.WhenAll(platforms);

                        foreach (var lobbies in from platform in platforms where platform.IsCompletedSuccessfully select platform.Result)
                            friendsOnly.AddRange(lobbies);

                        List<CustomLobbyInfo> allLobbies = [];
                        Platforms.ForEach(x => allLobbies.AddRange(x.Lobbies?.Lobbies ?? []));

                        FriendsOnlyLobbies = await CreateResponse([.. friendsOnly], Platforms[0]?.Client?.Handler?.LastFetch ?? Uptime, Settings?.Interval ?? 30);
                        Lobbies = await CreateResponse([.. allLobbies], Platforms[0]?.Client?.Handler?.LastFetch ?? Uptime, Settings?.Interval ?? 30);

                        Logger?.Information("Combined all available lobbies ({0})", Lobbies.Lobbies.Length);
                        await HandleDiscordBot();
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

        private static async Task HandleDiscordBot()
        {
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
        }

        public static async Task<LobbyListResponse> CreateResponse(CustomLobbyInfo[] lobbies, DateTimeOffset date, int interval = 30, string[]? friends = null)
        {
            var converted = await lobbies.Select(x => x.Convert()).Where(async x => Blacklist == null || await Blacklist.IsAllowed(x));
            return new LobbyListResponse([.. converted], date, interval, friends ?? []);
        }

        public static async Task<IEnumerable<T>> Where<T>(this IEnumerable<T> source, Func<T, Task<bool>> predicate)
        {
            var results = new ConcurrentQueue<T>();
            var tasks = source.Select(
                async x =>
                {
                    if (await predicate(x))
                        results.Enqueue(x);
                });
            await Task.WhenAll(tasks);
            return results;
        }

        private static async Task<string> GetCodeFromEmail(ILogger? logger, string email)
        {
            if (AuthCancel != null)
                await AuthCancel.CancelAsync();

            if (ImapEmpty())
            {
                logger?.Warning("Empty IMAP Configuration, falling back to manual input...");
                AuthCancel = new CancellationTokenSource();
                return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
            }
            else
            {
                try
                {
                    logger?.Info("Using IMAP to fetch Steam Auth Code...");
                    await Task.Delay((int)3.5f * 1000);
                    ImapManager ??= new IMAPManager(
                        Settings!.Imap!.Host!,
                        Settings.Imap.Port,
                        logger
                        );

                    ImapManager.LogIn(Settings!.Imap!.Username!, Settings!.Imap!.Password!);
                    var code = await ImapManager.GetCodeAsync();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        logger?.Error("Failed to retrieve the code from email, please check the email and type in the code manually");
                        AuthCancel = new CancellationTokenSource();
                        return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email}): [/]"), AuthCancel.Token);
                    }
                    logger?.Info("Successfully retrieved Steam Auth Code from email");
                    return code;
                }
                catch (Exception ex)
                {
                    logger?.Error("Failed to retrieve the code from email", ex);
                    logger?.Info("Falling back to manual input...");
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