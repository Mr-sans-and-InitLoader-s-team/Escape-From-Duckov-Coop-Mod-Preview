using EscapeFromDuckovCoopMod.Utils.Logger.Core;

namespace EscapeFromDuckovCoopMod.Utils.Logger.Logs
{
    /// <summary>
    /// 日志记录的基本实现
    /// </summary>
    public struct Log : ILog
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; }

        public Log(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public string ParseToString()
        {
            return Message;
        }
    }

    /// <summary>
    /// 针对 Logger 提供对 Log 结构的扩展方法
    /// </summary>
    public static class LogExtensions
    {
        public static LogHandlers.Logger Log(this LogHandlers.Logger logger, LogLevel logLevel, string message)
        {
            return logger.Log(new Log(logLevel, message));
        }
        public static LogHandlers.Logger Log(this LogHandlers.Logger logger, string message)
        {
            return logger.Log(new Log(LogLevel.None, message));
        }
        public static LogHandlers.Logger LogInfo(this LogHandlers.Logger logger, string message)
        {
            return logger.Log(new Log(LogLevel.Info, message));
        }
        public static LogHandlers.Logger LogTrace(this LogHandlers.Logger logger, string message)
        {
            return logger.Log(new Log(LogLevel.Trace, message));
        }
        public static LogHandlers.Logger LogDebug(this LogHandlers.Logger logger, string message)
        {
            return logger.Log(new Log(LogLevel.Debug, message));
        }
        public static LogHandlers.Logger LogWarning(this LogHandlers.Logger logger, string message)
        {
            return logger.Log(new Log(LogLevel.Warning, message));
        }
        public static LogHandlers.Logger LogError(this LogHandlers.Logger logger, string message)
        {
            return logger.Log(new Log(LogLevel.Error, message));
        }
        public static LogHandlers.Logger LogFatal(this LogHandlers.Logger logger, string message)
        {
            return logger.Log(new Log(LogLevel.Fatal, message));
        }

    }
}
