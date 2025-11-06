using System;
using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;

namespace EscapeFromDuckovCoopMod.Utils.Logger.Tools
{
    public class LoggerHelper
    {
        private static readonly Lazy<LogHandlers.Logger> _instance =
            new Lazy<LogHandlers.Logger>(() =>
            {
                var logger = new LogHandlers.Logger();
                logger.AddHandler(new ConsoleLogHandler());
                return logger;
            }
            , LazyThreadSafetyMode.ExecutionAndPublication);

        public static LogHandlers.Logger Instance => _instance.Value;

        public static void Log(string message)
        {
            Instance.Log(new Log(LogLevel.Info, message));
        }

        public static void LogWarning(string message)
        {
            Instance.Log(new Log(LogLevel.Warning, message));
        }

        public static void LogError(string message)
        {
            Instance.Log(new Log(LogLevel.Error, message));
        }

        public static void LogException(Exception exception)
        {
            Instance.Log(new Log(LogLevel.Error, exception.ToString()));
        }
    }
}

