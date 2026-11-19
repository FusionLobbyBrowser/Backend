using CommandLine;

namespace FLB_API
{
    public class Options
    {
        [Option("logLevel", Required = false, HelpText = "Set the log level for the application, this only affects the console output (Values: Debug, Info, Warning, Error)")]
        public FusionAPI.Data.Interfaces.ILogger.LogLevel? LogLevel { get; set; }

        [Option("steamHandler", Required = false, HelpText = "Set the Steam handler to use for Steam authentication (SteamKit or Steamworks)")]
        public string? SteamHandler { get; set; }

        [Option("config", Required = false, HelpText = "Path to the config file to use")]
        public string? ConfigPath { get; set; }

        [Option("setup", Required = false, HelpText = "Start the setup process regardless of whether the configuration has already been set up")]
        public bool Setup { get; set; } = false;
    }
}