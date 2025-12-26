using Spectre.Console;

using ILogger = FusionAPI.Interfaces.ILogger;
using LogLevel = FusionAPI.Interfaces.ILogger.LogLevel;

namespace FLB_API
{
    internal class Logger : ILogger
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
            string markup = $"[red] >{FormatPrefix()} [[ERROR]] {Markup.Escape(msg)}[/]";
            AnsiConsole.MarkupLine(markup);
        }

        public void Info(string message, params object[] args)
        {
            if (Level > LogLevel.Info)
                return;

            var msg = string.Format(message, args);
            string markup = $"[grey] >{FormatPrefix()} [[INFO]] {Markup.Escape(msg)}[/]";
            AnsiConsole.MarkupLine(markup);
        }

        public void Trace(string message, params object[] args)
        {
            if (Level > LogLevel.Trace)
                return;

            var msg = string.Format(message, args);
            string markup = $"[dodgerblue1] >{FormatPrefix()} [[TRACE]] {Markup.Escape(msg)}[/]";
            AnsiConsole.MarkupLine(markup);
        }

        public void Warning(string message, params object[] args)
        {
            if (Level > LogLevel.Warning)
                return;

            var msg = string.Format(message, args);
            string markup = $"[yellow] >{FormatPrefix()} [[WARN]] {Markup.Escape(msg)}[/]";
            AnsiConsole.MarkupLine(markup);
        }

        private string FormatPrefix() => $"{(!string.IsNullOrWhiteSpace(Prefix) ? $" [[{Prefix}]]" : string.Empty)}";
    }
}
