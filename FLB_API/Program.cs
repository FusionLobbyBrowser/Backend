using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using CommandLine;

using FLB_API.Blacklist;
using FLB_API.Discord;
using FLB_API.Managers;

using FusionAPI;
using FusionAPI.Handlers;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;

using Spectre.Console;

using ILogger = FusionAPI.Data.Interfaces.ILogger;

namespace FLB_API
{
    public static partial class Program
    {
        public const string Version = "1.0.0";

        internal static Serilog.Core.Logger? Logger { get; private set; }

        internal static LobbyListResponse? Lobbies { get; private set; }

        internal static LobbyListResponse? FriendsOnlyLobbies { get; private set; }

        internal static List<Platform> Platforms { get; } = [];

        internal static DateTime Uptime { get; private set; }

        internal static Settings? Settings { get; private set; }

        internal static BlacklistManager? Blacklist { get; private set; }

        internal static IMAPManager? ImapManager { get; private set; }

        internal static CancellationTokenSource? AuthCancel { get; private set; }

        internal static string CurrentDir => AppDomain.CurrentDomain.BaseDirectory;

        internal static JsonSerializerOptions JsonSettingsOptions { get; } = new() { WriteIndented = true };

        public static Options? Options { get; private set; }

        public static async Task Main(string[] args)
        {
            var parser = new Parser(with =>
            {
                with.CaseInsensitiveEnumValues = true;
                with.AutoHelp = true;
                with.AutoVersion = true;
                with.HelpWriter = Console.Out;
            });
            var result = parser.ParseArguments<Options>(args);
            if (result.Tag == ParserResultType.NotParsed)
                return;

            Options = result.Value;
            WriteHeader();
            AnsiConsole.WriteLine();
            var builder = WebApplication.CreateBuilder(args);

            if (!Directory.Exists(Path.Combine(CurrentDir, "Logs")))
                Directory.CreateDirectory(Path.Combine(CurrentDir, "Logs"));

            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Spectre(restrictedToMinimumLevel: (LogEventLevel)(Options?.LogLevel ?? (Settings?.LogLevel ?? FusionAPI.Data.Interfaces.ILogger.LogLevel.Info)))
                .WriteTo.File(Path.Combine(CurrentDir, "Logs", "log-.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Add services to the container.

            try
            {
                await Setup();
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
                        await context.Response.WriteAsJsonAsync(details, CancellationToken.None);
                    };
                })

                .AddSteam(options => options.ApplicationKey = Settings?.Steam?.WebApi?.WebToken);

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
                    if (ctx.ProblemDetails.Status == 404 && string.IsNullOrWhiteSpace(ctx.ProblemDetails.Detail) && ctx.ProblemDetails.Title == "Not Found")
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

            await app.RunAsync();

            await token.CancelAsync();
            token.Dispose();
        }

        private static async Task Setup()
        {
            LoadSettings();
            if (Options?.Setup == true || (!Settings?.Setup ?? true))
                await UserSetup();

            Dictionary<string, string> metadata = [];

            Fusion steamClient;
            var isSteamKit = string.Equals(Options?.SteamHandler ?? Settings?.Steam?.Handler, "SteamKit", StringComparison.OrdinalIgnoreCase);
            if (isSteamKit)
            {
                steamClient = new Fusion(new SteamKitHandler());
                metadata = await SetupSteamKit(steamClient);
            }
            else
            {
                Logger?.Information("Connecting with Steamworks");
                steamClient = new Fusion(new SteamworksHandler());
            }

            Platforms.Add(await Platform.CreateAndInit(steamClient, PlatformType.Steam, Settings?.LogLevel ?? ILogger.LogLevel.Info, !isSteamKit, metadata));
            Logger?.Information("Successfully initialized Steam Fusion API! Initializing EOS (Epic Online Services)...");

            Platforms.Add(await Platform.CreateAndInit(new Fusion(new EpicHandler()), PlatformType.Epic, Settings?.LogLevel ?? ILogger.LogLevel.Info, true, []));
            Logger?.Information("Successfully initialized EOS API");

            ThumbnailManager.Logger = new Logger(Settings?.LogLevel ?? ILogger.LogLevel.Info, "ModIO");

            if (Settings?.ModIo?.Enabled != false)
                await ThumbnailManager.Setup();

            Uptime = DateTime.UtcNow;
            Blacklist = new BlacklistManager(new Logger(Settings?.LogLevel ?? ILogger.LogLevel.Info, "Blacklist"));
            await Blacklist.RegisterFromAssembly(typeof(Program).Assembly);
        }

        private static readonly List<CredentialStep> steps =
        [
            new("Discord", "cyan", async () =>
            {
                AnsiConsole.MarkupLine("First, we need to set up the Discord Bot. Please provide the bot token.");
                AnsiConsole.MarkupLine("To get a bot token, you will need to [lightgreen]create[/] a bot on the Discord Developer Portal ([link=https://discord.com/developers/applications]https://discord.com/developers/applications[/]) and copy the token from the Bot section");
                AnsiConsole.MarkupLine("Note: For the bot to work properly, it needs to have the necessary permissions: 4503876653074432 (this is a bot permission integer)");
                var discordPrompt =
                    new TextPrompt<string>(
                            $" -> Discord Bot Token {Markup.Escape("[type 'disable' if you do not wish to use the discord bot]")}")
                        .Validate((
                            prompt) => TokenUtils.TryValidateToken(prompt) || prompt == "disable" ? ValidationResult.Success() : ValidationResult.Error("[red]The contents of the token are invalid! Provide a valid one or disable the feature.[/]"))
                        .DefaultValue(Settings?.Discord?.IsValid() == true ? Settings?.Discord?.Token! : "disable");

                var discordToken = await AnsiConsole.PromptAsync(discordPrompt);
                if (discordToken == "disable")
                {
                    Settings?.Discord?.Enabled = false;
                    Settings?.Discord?.Token = "disable";
                    AnsiConsole.MarkupLine("First feature [red]disabled[/]! Let's see how many others will suffer a fate like this");
                }
                else
                {
                    Settings?.Discord?.Enabled = true;
                    Settings?.Discord?.Token = discordToken;
                    AnsiConsole.MarkupLine("Onto the next one!");
                }
            }, () => Settings?.Discord?.Enabled == true && TokenUtils.TryValidateToken(Settings?.Discord?.Token ?? string.Empty)),
            new("SteamKit", "IndianRed1", async () =>
            {
                AnsiConsole.MarkupLine("If you wish to use SteamKit, you will need to provide the necessary credentials. Enter the username and password to a Steam account. Any will do, you can create one just for this");
                AnsiConsole.MarkupLine("Do note that to allow for a more seamless experience without any user input, you will need to input your email credentials so choose the email for the steam account wisely (preferably a Gmail one)");
                AnsiConsole.MarkupLine("If you wish to use only Steamworks, just put anything here");
                var username = await AskAsync<string>(" -> Username", Settings?.Steam?.SteamKitAuth?.Username ?? null);
                var passView = new TextPrompt<string>(" -> Password").Secret('*');
                if(!string.IsNullOrWhiteSpace(Settings?.Steam?.SteamKitAuth?.Password))
                    passView = passView.DefaultValue(Settings?.Steam?.SteamKitAuth?.Password ?? string.Empty);
                var password = await AnsiConsole.PromptAsync(passView);

                Settings?.Steam?.SteamKitAuth?.Username = username;
                Settings?.Steam?.SteamKitAuth?.Password = password;
            }, () => !string.IsNullOrWhiteSpace(Settings?.Steam?.SteamKitAuth?.Username) && !string.IsNullOrWhiteSpace(Settings?.Steam?.SteamKitAuth?.Password)),
            new("Steam Web API", "LightCyan3", async () =>
            {
                AnsiConsole.MarkupLine("To display Steam profiles on the website from Steam, you need to provide the [yellow]Steam Web API key![/]");
                AnsiConsole.MarkupLine("You can find it here: [link=https://steamcommunity.com/dev/apikey]https://steamcommunity.com/dev/apikey[/]");
                var webApiKey = await AskAsync($" -> Key {Markup.Escape("[type 'disable' if you do not wish for Steam profiles to be displayed on the website]")}", Settings?.Steam?.WebApi?.IsValid() == true ? Settings?.Steam?.WebApi?.WebToken : "disable");
                if (webApiKey == "disable")
                {
                    Settings?.Steam?.WebApi?.Enabled = false;
                    Settings?.Steam?.WebApi?.WebToken = "your-token";
                }
                else
                {
                    Settings?.Steam?.WebApi?.Enabled = true;
                    Settings?.Steam?.WebApi?.WebToken = webApiKey;
                }
            }, () => Settings?.Steam?.WebApi?.IsValid() == true),
            new("IMAP (Email)", "DarkOrange", async () =>
            {
                AnsiConsole.MarkupLine("Now to make everything work smoothly and hands-free, we need your email. For the easiest setup we recommend a Gmail account");
                AnsiConsole.MarkupLine("This will be used to automatically handle the authentication of Steam (if you are using SteamKit) and ModIO, so you need to have the same email for those two accounts.");
                AnsiConsole.MarkupLine("For this we recommend an email created just for this purpose, as the program might have an issue finding the right message if you have a lot of these.");
                AnsiConsole.MarkupLine("Gmail is recommended as it provides an easy way to do this (See [link=https://www.lifewire.com/what-are-the-gmail-imap-settings-1170852]https://www.lifewire.com/what-are-the-gmail-imap-settings-1170852[/])");
                AnsiConsole.MarkupLine("If you do not wish to use this leave the fields empty, but it is recommended for a hands-free experience!");
                var host = await AskAsync<string>(" -> Host: ", Settings?.Imap?.Host ?? "imap.gmail.com");
                var port = await AskAsync<int>(" -> Port: ", Settings?.Imap?.Port ?? 993);
                var mailUsername = await AskAsync<string>(" -> Username: ", Settings?.Imap?.Username ?? string.Empty);
                var mailPassView = new TextPrompt<string>(" -> Password: ").Secret('*');
                if(!string.IsNullOrWhiteSpace(Settings?.Imap?.Password))
                    mailPassView = mailPassView.DefaultValue(Settings?.Imap?.Password ?? string.Empty);
                var mailPassword = await AnsiConsole.PromptAsync(mailPassView);

                Settings?.Imap?.Host = host;
                Settings?.Imap?.Port = port;
                Settings?.Imap?.Username = mailUsername;
                Settings?.Imap?.Password = mailPassword;
            }, () => Settings?.Imap?.IsValid() == true),
            new("ModIO", "DeepSkyBlue3_1", async () =>
            {
                AnsiConsole.MarkupLine("Well now here comes the last one, but a tricky one - ModIO. This is responsible for providing the thumbnails of the modded levels/avatars");
                AnsiConsole.MarkupLine("Let's start with the easy one - [DodgerBlue1]Personal Access Token[/]! This is only required for the fetching of mods from [yellow]collections[/]. This is used for the 'furry' filter you may have noticed on the website.");
                AnsiConsole.MarkupLine("You can get it here: https://mod.io/me/access#tokens - When creating uncheck all permissions, set the scope to [lightgreen]Read[/] and the expiry date to the longest possible period. (Will implement automatic renewal later on)");
                var personalAccessToken = await AskAsync($" -> Personal Access Token {Markup.Escape("[type 'disable' if you do not wish for the filter to work]: ")}", Settings?.ModIo?.PersonalAccessToken?.Content ?? "disable");
                Settings?.ModIo?.PersonalAccessToken = personalAccessToken == "disable" ? Token.Disabled : new Token(personalAccessToken, -1);

                const string yes = "Let me forget about any issue! (This will require access to an email address and will log in to provided ModIO account)";
                const string no = "I don't trust you. (You will need to provide an access token and replace it yourself)";
                const string disable = "Disable entirely. (This will disable the ModIO integration and thumbnails for mod.io levels/avatars will not be available)";

                var choice = await AnsiConsole.PromptAsync(new SelectionPrompt<string>()
                    .Title($"{Environment.NewLine}Well here's the tricky part - the tokens used for fetching thumbnails [bold red]don't last forever[/] and may need to be replaced. Do you wish to provide additional credentials to allow for automatic renewal or wish to replace the tokens yourself every now and then?")
                    .AddChoices(yes, no, disable).DefaultValue(Settings?.ModIo?.Auth?.Use == true || Settings?.ModIo?.Auth == null ? yes : no));

                if (choice == no)
                {
                    AnsiConsole.MarkupLine(
                    $"{Environment.NewLine}That's a shame, but it's understandable. Let's get the access token then!");
                    ThumbnailManager.Logger = new SpectreLogger();
                    var success = await ThumbnailManager.Authenticate(false, false);
                    AnsiConsole.MarkupLine(success
                        ? "[lightgreen]Successfully authenticated with ModIO![/]"
                        : "[red]Failed to authenticate with ModIO, well that's unfortunate[/]");
                    Settings?.ModIo?.Auth?.Use = false;
                    Settings?.ModIo?.Enabled = true;
                }
                else if (choice == yes)
                {
                    AnsiConsole.MarkupLine(
                        $"{Environment.NewLine}This should be rather simple if you have a mod.io account linked to the same email as you set up in IMAP (if you did... relaunch the program if you haven't and wish to use this)");
                    var guess = !string.IsNullOrWhiteSpace(Settings?.ModIo?.Auth?.Email) ? Settings?.ModIo?.Auth?.Email : GuessModIoEmail(Settings?.Imap);
                    string modIoEmail = await AskAsync(" -> Email", guess);
                    Settings?.ModIo?.Auth?.Email = modIoEmail;
                    Settings?.ModIo?.Auth?.Use = true;
                    Settings?.ModIo?.Enabled = true;
                }else if(choice == disable){
                    AnsiConsole.MarkupLine(
                        $"{Environment.NewLine}That's a shame, but it's understandable. ModIO will be disabled and thumbnails for mod.io levels/avatars will [red]not be available[/]");
                    Settings?.ModIo?.Enabled = false;
                }

                var cache = await AskAsync<ulong>("How long would you like for the external thumbnails to be [yellow]cached[/] for ([lightgreen]to avoid ratelimits[/], in seconds): ", Settings?.ModIo?.CacheExpireTime ?? 1800);
                Settings?.ModIo?.CacheExpireTime = cache;
                var cleanUp = await AskAsync<int>("How often do you wish to clean up [red]old and unused[/] external thumbnails ([lightgreen]to save on RAM[/], in seconds): ", Settings?.ModIo?.CleanupInterval ?? 3600);
                Settings?.ModIo?.CleanupInterval = cleanUp;
            }, () => Settings?.ModIo?.Token != Token.Empty && Settings?.ModIo?.PersonalAccessToken != Token.Empty)

        ];

        private static async Task<T> AskAsync<T>(string prompt, T? defaultValue, CancellationToken token = default)
        {
            var promptView = new TextPrompt<T>(prompt);
            if (defaultValue != null && (typeof(T) != typeof(string) || !string.IsNullOrWhiteSpace(defaultValue?.ToString())))
                promptView.DefaultValue(defaultValue);
            return await AnsiConsole.PromptAsync(promptView, token);
        }

        private static async Task UserSetup()
        {
            Console.Clear();
            AnsiConsole.WriteLine();
            WriteHeader();
            AnsiConsole.Write(Align.Center(new Markup($"{Environment.NewLine}Thanks for using Fusion Lobby Browser (or in short, FLB)! It seems that you are running the application for the first time!")));
            AnsiConsole.Write(Align.Center(new Markup($"I'll help you [yellow]configure the settings[/] so that you can get it running right away!{Environment.NewLine}")));

            AnsiConsole.MarkupLine($"{Environment.NewLine}Okay, first the most basic thing - [cyan]the interval[/]! The lobbies don't get fetched on every request, to avoid [red]rate limits[/] we need to store them and update every few seconds");
            AnsiConsole.MarkupLine("Epic Games allows for a maximum of 30 requests per minute, while the Steam API does not clearly state its rate limits - just keep in mind that on every fetch there are 2 requests made, one for public lobbies and one for friends only.");
            AnsiConsole.MarkupLine("That makes for 15 requests per minute for Epic Games, a safe and tested interval is 30 seconds, though you can try using lower values, but things might break");
            var interval =
                await AnsiConsole.AskAsync(
                    $"{Environment.NewLine} -> How often do you wish to fetch the lobbies, keep the rate limits in mind! (in seconds): ", 30, CancellationToken.None);
            Settings?.Interval = interval;

            AnsiConsole.MarkupLine($"{Environment.NewLine}Now the next thing - [IndianRed1]Steam[/]! There are currently two ways to access Steam's API - using SteamKit or Steamworks, but what's the difference?");
            AnsiConsole.MarkupLine("Steamworks is by far the simplest, doesn't require any additional setup for credentials, BUT it requires to have the Steam app open with a logged in account, which may be hard on a dedicated server");
            AnsiConsole.MarkupLine("On the other side SteamKit is more complex to set up and will require additional steps, but it doesn't require the Steam app to be running. You will need to provide the login credentials (username and password) as well as provide access to the email to automatically authenticate with 2FA if enabled");

            var steamHandler = await AnsiConsole.PromptAsync(new SelectionPrompt<string>()
                .Title($"{Environment.NewLine}{Environment.NewLine}So now's the question - which one would you like to use? This can be changed later in the configuration file")
                .AddChoices("Steamworks", "SteamKit").DefaultValue(Settings?.Steam?.Handler), CancellationToken.None);
            Settings?.Steam?.Handler = steamHandler;

            AnsiConsole.MarkupLine($"{Environment.NewLine}Now the next thing - [yellow]the credentials[/]! This is gonna take some time to set up if you're not prepared already");
            AnsiConsole.MarkupLine($"This will consist of [CornflowerBlue]{steps.Count} different steps[/], some if not most you will be able to skip but this will break some functionality on the website!");
            AnsiConsole.MarkupLine("[Chartreuse1]Let's get started![/]");

            int index = 0;
            foreach (var step in steps)
                await step.Run(index++, steps.Count);

            AnsiConsole.Clear();
            Settings?.Setup = true;
            SaveSettings();

            AnsiConsole.MarkupLine("That's everything! You're all set! Launching program in 3 seconds");
            await Task.Delay(3000, CancellationToken.None);
            Console.Clear();
        }

        private static void WriteHeader()
        {
            var font = FigletFont.Load(Path.Combine(CurrentDir, "ansi.flf"));
            var figlet = new FigletText(font, "FLB")
                .Color(Color.Cyan)
                .Justify(Justify.Center);
            AnsiConsole.Write(figlet);
            AnsiConsole.Write(Align.Center(new Markup($"{Environment.NewLine}Author: [bold CornflowerBlue]HAHOOS[/] • Version: [bold yellow]v{Program.Version}[/]")));
        }

        public static string? GuessModIoEmail(ImapAuth? auth)
        {
            if (auth?.IsValid() != true)
                return null;

            var emailValidate = EmailValidationRegex;
            var getHost = GetHostRegex;
            if (auth.Host == "imap.gmail.com")
                return emailValidate.IsMatch(auth.Username!) ? auth.Username : $"{auth.Username}@gmail.com";

            return emailValidate.IsMatch(auth.Username!) ? auth.Username : $"{auth.Username}@{getHost.Match(auth.Host!).Groups["host"].Value}";
        }

        public static string ReplaceRegex(this string text, string pattern, string replacement)
            => Regex.Replace(text, pattern, replacement, RegexOptions.None, TimeSpan.FromSeconds(3));

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
                    return await AnsiConsole.PromptAsync(new TextPrompt<string>("[bold yellow] > Enter the code from your authenticator[/]"), AuthCancel.Token);
                },
                async (email, _) => await GetCodeFromEmail(client.Handler.Logger, email)
                );
            if (string.IsNullOrWhiteSpace(Settings?.Steam?.SteamKitAuth?.Username) || string.IsNullOrWhiteSpace(Settings?.Steam?.SteamKitAuth?.Password))
            {
                metadata.Add("username", await AnsiConsole.AskAsync<string>("[bold yellow]Steam Username[/]", CancellationToken.None));
                metadata.Add("password", await AnsiConsole.AskAsync<string>("[bold yellow]Steam Password[/]", CancellationToken.None));
            }
            else
            {
                metadata.Add("username", Settings.Steam.SteamKitAuth.Username);
                metadata.Add("password", Settings.Steam.SteamKitAuth.Password);
            }
            Logger?.Information("Using provided login to authenticate");
            return metadata;
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
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        List<CustomLobbyInfo> friendsOnly = [];
                        List<Task<CustomLobbyInfo[]>> platforms = [];
                        Platforms.ForEach(x => platforms.Add(x.FetchLobbies()));

                        await Task.WhenAll(platforms);

                        foreach (var lobbies in from platform in platforms where platform.IsCompletedSuccessfully select platform.Result)
                            friendsOnly.AddRange(lobbies);

                        List<CustomLobbyInfo> allLobbies = [];
                        Platforms.ForEach(x => allLobbies.AddRange(x.Lobbies?.Lobbies ?? []));

                        var latest = Platforms.OrderByDescending(x => x.Client?.Handler?.LastFetch ?? DateTimeOffset.FromUnixTimeMilliseconds(1)).FirstOrDefault();

                        FriendsOnlyLobbies = await CreateResponse([.. friendsOnly], latest?.Client?.Handler?.LastFetch ?? Uptime, Settings?.Interval ?? 30);
                        Lobbies = await CreateResponse([.. allLobbies], latest?.Client?.Handler?.LastFetch ?? Uptime, Settings?.Interval ?? 30);

                        stopwatch.Stop();
                        Logger?.Information("Combined all available lobbies ({0})", Lobbies.Lobbies.Length);
                        Logger?.Information("Fetching lobbies took {0}ms", stopwatch.ElapsedMilliseconds);
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
                    Logger?.Warning("None of the platforms are initialized, skipping lobby fetch...");
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
                if (Settings?.Discord?.Enabled == true && Options?.DisableDiscord != true)
                {
                    Logger?.Information("Setting up discord bot");
                    _ = DiscordBotManager.Setup();
                }
            }
        }

        public static async Task<LobbyListResponse> CreateResponse(CustomLobbyInfo[] lobbies, DateTimeOffset date, int interval = 30, string[]? friends = null)
        {
            var converted = await lobbies.Select(x => x.Convert()).Where(async x => Blacklist == null || await Blacklist.IsAllowed(x));
            return new LobbyListResponse([.. converted], date, interval, friends ?? []) { CurrentTime = DateTimeOffset.Now.ToUnixTimeSeconds() };
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
                return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email})[/]"), AuthCancel.Token);
            }
            else
            {
                try
                {
                    logger?.Info("Using IMAP to fetch Steam Auth Code...");
                    await Task.Delay((int)3.5f * 1000, CancellationToken.None);
                    ImapManager ??= new IMAPManager(Settings!.Imap!, logger, true);
                    var code = await ImapManager.GetCodeAsync(IMAPManager.GetSteamRegEx());
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        logger?.Error("Failed to retrieve the code from email, please check the email and type in the code manually");
                        AuthCancel = new CancellationTokenSource();
                        return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email})[/]"), AuthCancel.Token);
                    }
                    logger?.Info("Successfully retrieved Steam Auth Code from email");
                    return code;
                }
                catch (Exception ex)
                {
                    logger?.Error("Failed to retrieve the code from email", ex);
                    logger?.Info("Falling back to manual input...");
                    AuthCancel = new CancellationTokenSource();
                    return await AnsiConsole.PromptAsync(new TextPrompt<string>($"[bold yellow] > Enter the code sent to your email ({email})[/]"), AuthCancel.Token);
                }
            }
        }

        public static bool ImapEmpty()
            => string.IsNullOrWhiteSpace(Settings?.Imap?.Host) ||
               string.IsNullOrWhiteSpace(Settings?.Imap?.Username) ||
               string.IsNullOrWhiteSpace(Settings?.Imap?.Password);

        private static void LoadSettings()
        {
            try
            {
                Logger?.Information("Loading settings...");
                var path = Options?.ConfigPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                if (!File.Exists(path))
                {
                    Logger?.Information("Settings file is missing, creating...");
                    using var stream = File.CreateText(path);
                    var serialized = JsonSerializer.Serialize(Settings.Default, JsonSettingsOptions);
                    stream.Write(serialized);
                    stream.Flush();
                    stream.Close();
                    Settings = Settings.Default;
                }
                else
                {
                    Settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonSettingsOptions);
                    Logger?.Information("Successfully loaded settings!");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to set settings from file");
            }
        }

        public static void SaveSettings()
        {
            try
            {
                var path = Options?.ConfigPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                Logger?.Information("Saving settings");
                using var stream = File.CreateText(path);
                var serialized = JsonSerializer.Serialize(Settings, JsonSettingsOptions);
                stream.Write(serialized);
                stream.Flush();
                stream.Close();
                Logger?.Information("Successfully saved settings!");
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to save settings to file");
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

        [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
        private static partial Regex EmailValidationRegex { get; }

        [GeneratedRegex(@"^(?:[a-zA-Z0-9-]+\.)*(?<host>[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})$")]
        private static partial Regex GetHostRegex { get; }
    }

    public class CredentialStep(string name, string color, Func<Task> callback, Func<bool> isConfigured)
    {
        public Func<Task> Callback { get; set; } = callback;

        public Func<bool> IsConfigured { get; set; } = isConfigured;

        public string Name { get; set; } = name;

        public string Color { get; set; } = color;

        public async Task Run(int stepNum, int totalSteps)
        {
            AnsiConsole.MarkupLine($"{Environment.NewLine}{Environment.NewLine}[bold {Color}]{Markup.Escape($"[{stepNum + 1}/{totalSteps}]")} {Name}[/]");
            if (IsConfigured.Invoke())
            {
                var prompt = await AnsiConsole.PromptAsync(new SelectionPrompt<string>()
                    .Title($"{Environment.NewLine}It seems like this has already been configured! Would you like to reconfigure?")
                    .AddChoices("Yes", "No"));
                if (prompt == "Yes")
                    await Callback.Invoke();
            }
            else
            {
                await Callback.Invoke();
            }
        }
    }
}