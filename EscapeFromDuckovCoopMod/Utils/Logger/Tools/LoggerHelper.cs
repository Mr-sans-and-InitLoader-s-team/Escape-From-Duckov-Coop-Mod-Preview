using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;

namespace EscapeFromDuckovCoopMod.Utils.Logger.Tools
{
    public class LoggerHelper
    {
        // 可以在此次修改初始化逻辑，以添加更多的日志处理器或修改过滤器
        private static readonly Lazy<LogHandlers.Logger> _instance =
            new Lazy<LogHandlers.Logger>(() =>
            {
                var logger = new LogHandlers.Logger();
                logger.AddHandler(new ConsoleLogHandler());

                // 打印标签日志示例：
                //logger.Log(new LabelLog(LogLevel.Info, "标签日志", "Label"));
                // 或者通过扩展方法
                //logger.Log(LogLevel.Info, "标签日志", "Label");
                //logger.LogInfo("标签日志", "Label");

                // 过滤器示例：不允许输出 None 和 Info 级别的日志
                //logger.Filter.AddFilter<Log>((log) =>
                //{
                //    return log.Level is not (LogLevel.None or LogLevel.Info);
                //});

                return logger;
            }
            , LazyThreadSafetyMode.ExecutionAndPublication);

        public static LogHandlers.Logger Instance => _instance.Value;

        // 替代掉 Debug.Log 之类的玩意
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
