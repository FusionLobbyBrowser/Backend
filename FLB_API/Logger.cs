using Spectre.Console;

using ILogger = FusionAPI.Interfaces.ILogger;
using LogLevel = FusionAPI.Interfaces.ILogger.LogLevel;

namespace FLB_API
{
    public class Logger : ILogger
    {
        public LogLevel Level { get; set; }

        public string Prefix { get; set; }

        public Logger()
        {
            this.Level = LogLevel.Info;
            this.Prefix = string.Empty;
        }

        public Logger(LogLevel level)
        {
            this.Level = level;
            this.Prefix = string.Empty;
        }

        public Logger(string prefix)
        {
            this.Prefix = prefix;
            this.Level = LogLevel.Info;
        }

        public Logger(LogLevel level, string prefix)
        {
            this.Level = level;
            this.Prefix = prefix;
        }

        public void Error(string message, params object[] args)
        {
            if (Level > LogLevel.Error)
                return;

            var msg = string.Format(message, args);
            Program.Logger?.Error($"{FormatPrefix()}{msg}");
        }

        public void Info(string message, params object[] args)
        {
            if (Level > LogLevel.Info)
                return;

            var msg = string.Format(message, args);
            Program.Logger?.Information($"{FormatPrefix()}{msg}");
        }

        public void Trace(string message, params object[] args)
        {
            if (Level > LogLevel.Trace)
                return;

            var msg = string.Format(message, args);
            Program.Logger?.Verbose($"{FormatPrefix()}{msg}");
        }

        public void Warning(string message, params object[] args)
        {
            if (Level > LogLevel.Warning)
                return;

            var msg = string.Format(message, args);
            Program.Logger?.Warning($"{FormatPrefix()}{msg}");
        }

        private string FormatPrefix() => $"{(!string.IsNullOrWhiteSpace(Prefix) ? $"[[{Prefix}]] " : string.Empty)}";
    }
}