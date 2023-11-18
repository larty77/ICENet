using System;
using System.Collections.Generic;

namespace ICENet.Core.Helpers
{
    public enum LogType
    {
        Info,
        Error,
    }

    public sealed class Logger
    {
        private static readonly Dictionary<LogType, Action<string>> _logMethods;

        static Logger()
        {
            _logMethods = new Dictionary<LogType, Action<string>>
            {
                [LogType.Info] = null!,
                [LogType.Error] = null!,
            };
        }

        public static void AddInfoListener(Action<string> infoLog) => _logMethods[LogType.Info] += infoLog ?? throw new ArgumentException(nameof(infoLog));

        public static void RemoveInfoListener(Action<string> infoLog)
        {
            if (infoLog is null) throw new ArgumentNullException(nameof(infoLog));

            if (_logMethods.TryGetValue(LogType.Info, out Action<string> logMethod))
            {
                logMethod -= infoLog;
                _logMethods[LogType.Info] = logMethod!;
            }
        }

        public static void AddErrorListener(Action<string> errorLog) => _logMethods[LogType.Error] += errorLog ?? throw new ArgumentException(nameof(errorLog));

        public static void RemoveErrorListener(Action<string> errorLog)
        {
            if (errorLog is null) throw new ArgumentNullException(nameof(errorLog));

            if (_logMethods.TryGetValue(LogType.Error, out Action<string> logMethod))
            {
                logMethod -= errorLog;
                _logMethods[LogType.Error] = logMethod!;
            }
        }

        internal static void Log(LogType logType, string header, string message)
        {
            try
            {
                header = string.IsNullOrEmpty(header) ? string.Empty : $"[{header}]: ";
                _logMethods.TryGetValue(logType, out Action<string> logMethod);
                logMethod?.Invoke($"{header}{message}");
            }
            catch {  }
        }
    }
}
