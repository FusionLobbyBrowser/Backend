using Spectre.Console;

using ILogger = FusionAPI.Interfaces.ILogger;
using LogLevel = FusionAPI.Interfaces.ILogger.LogLevel;

namespace FLB_API
{
    public class Logger(LogLevel level, string prefix) : ILogger
    {
        public LogLevel Level { get; set; } = level;

        public string Prefix { get; set; } = prefix;

        public Logger() : this(LogLevel.Info, string.Empty)
        {
        }

        public Logger(LogLevel level) : this(level, string.Empty)
        {
        }

        public Logger(string prefix) : this(LogLevel.Info, prefix)
        {
        }

        public void Error(string message, params object[] args)
        {
            if (Level > LogLevel.Error)
                return;

            Program.Logger?.Error(Format(message, args));
        }

        public void Error(string message, Exception ex, params object[] args)
        {
            if (Level > LogLevel.Error)
                return;

            Program.Logger?.Error(ex, Format(message, args));
        }

        public void Info(string message, params object[] args)
        {
            if (Level > LogLevel.Info)
                return;

            Program.Logger?.Information(Format(message, args));
        }

        public void Debug(string message, params object[] args)
        {
            if (Level > LogLevel.Debug)
                return;

            Program.Logger?.Debug(Format(message, args));
        }

        public void Trace(string message, params object[] args)
        {
            if (Level > LogLevel.Trace)
                return;

            Program.Logger?.Verbose(Format(message, args));
        }

        public void Warning(string message, params object[] args)
        {
            if (Level > LogLevel.Warning)
                return;

            Program.Logger?.Warning(Format(message, args));
        }

        private string Format(string message, params object[] args)
        {
            var msg = string.Format(message, args);
            return FormatPrefix() + msg;
        }

        private string FormatPrefix() => $"{(!string.IsNullOrWhiteSpace(Prefix) ? $"[[{Prefix}]] " : string.Empty)}";
    }
}