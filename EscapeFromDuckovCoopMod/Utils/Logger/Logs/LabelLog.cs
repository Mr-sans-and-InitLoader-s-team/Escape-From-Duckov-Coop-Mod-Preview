using EscapeFromDuckovCoopMod.Utils.Logger.Core;

namespace EscapeFromDuckovCoopMod.Utils.Logger.Logs
{
    /// <summary>
    /// 带标签的日志
    /// </summary>
    public struct LabelLog : ILog
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string Label { get; set; }

        public LabelLog(LogLevel level, string message, string label)
        {
            Level = level;
            Message = message;
            Label = label;
        }

        public string ParseToString()
        {
            return $"<{Label}> {Message}";
        }
    }

    /// <summary>
    /// 针对 Logger 提供对 LabelLog 结构的扩展方法
    /// </summary>
    public static class LabelLogExtensions
    {
        public static LogHandlers.Logger Log(this LogHandlers.Logger logger, LogLevel logLevel, string message, string label)
        {
            return logger.Log(new LabelLog(logLevel, message, label));
        }
        public static LogHandlers.Logger Log(this LogHandlers.Logger logger, string message, string label)
        {
            return logger.Log(new LabelLog(LogLevel.None, message, label));
        }
        public static LogHandlers.Logger LogInfo(this LogHandlers.Logger logger, string message, string label)
        {
            return logger.Log(new LabelLog(LogLevel.Info, message, label));
        }
        public static LogHandlers.Logger LogTrace(this LogHandlers.Logger logger, string message, string label)
        {
            return logger.Log(new LabelLog(LogLevel.Trace, message, label));
        }
        public static LogHandlers.Logger LogDebug(this LogHandlers.Logger logger, string message, string label)
        {
            return logger.Log(new LabelLog(LogLevel.Debug, message, label));
        }
        public static LogHandlers.Logger LogWarning(this LogHandlers.Logger logger, string message, string label)
        {
            return logger.Log(new LabelLog(LogLevel.Warning, message, label));
        }
        public static LogHandlers.Logger LogError(this LogHandlers.Logger logger, string message, string label)
        {
            return logger.Log(new LabelLog(LogLevel.Error, message, label));
        }
        public static LogHandlers.Logger LogFatal(this LogHandlers.Logger logger, string message, string label)
        {
            return logger.Log(new LabelLog(LogLevel.Fatal, message, label));
        }
    }
}