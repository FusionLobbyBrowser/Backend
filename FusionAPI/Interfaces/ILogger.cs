using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FusionAPI.Interfaces
{
    public interface ILogger
    {
        public LogLevel Level { get; set; }

        public string Prefix { get; set; }

        public void Error(string message, params object[] args);
        public void Info(string message, params object[] args);
        public void Trace(string message, params object[] args);
        public void Warning(string message, params object[] args);


        public enum LogLevel
        {
            Trace = 0,
            Info = 1,
            Warning = 2,
            Error = 3,
        }
    }
}
