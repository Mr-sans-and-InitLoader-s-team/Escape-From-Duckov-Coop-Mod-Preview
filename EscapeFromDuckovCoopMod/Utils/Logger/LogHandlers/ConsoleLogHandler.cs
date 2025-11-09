using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    public class ConsoleLogHandler : ILogHandler, ILogHandler<Log>, ILogHandler<LabelLog>, ILogHandler<LogHandlerAsyncDecorator.AsyncLog>
    {
        public void Log<TLog>(TLog log) where TLog : struct, ILog
        {
            Log(log.Level, log.ParseToString());
        }

        public void Log(Log log)
        {
            Log(log.Level, log.ParseToString());
        }

        public void Log(LabelLog log)
        {
            try
            {
                // PrintTimestamp();

                // 打印日志等级
                var logLevelStr = $"[{log.Level}] ";

                switch (log.Level)
                {
                    case LogLevel.None or LogLevel.Custom:
                        break;
                    case LogLevel.Info or LogLevel.Trace or LogLevel.Debug:
                        Console.ForegroundColor = ConsoleColor.Green; // 绿色
                        Console.Write(logLevelStr);
                        break;
                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow; // 黄色
                        Console.Write(logLevelStr);
                        break;
                    case LogLevel.Error or LogLevel.Fatal:
                        Console.ForegroundColor = ConsoleColor.Red; // 红色
                        Console.Write(logLevelStr);
                        break;
                }

                // 打印标签
                Console.ForegroundColor = ConsoleColor.Cyan; // 青色
                Console.Write($"<{log.Label}> ");

                // 打印日志内容
                switch (log.Level)
                {
                    case LogLevel.None or LogLevel.Custom:
                        ResetColor();
                        Console.WriteLine(log.Message);
                        break;
                    case LogLevel.Info or LogLevel.Trace or LogLevel.Debug:
                        ResetColor();
                        Console.WriteLine(log.Message);
                        break;
                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(log.Message);
                        break;
                    case LogLevel.Error or LogLevel.Fatal:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(log.Message);
                        break;
                }

                ResetColor();
            }
            catch (System.IO.IOException)
            {
                // 控制台不可用时忽略
            }
        }

        public void Log(LogHandlerAsyncDecorator.AsyncLog log)
        {
            try
            {
                // 打印时间戳
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($"[{log.Timestamp:HH:mm:ss}] ");

                log.LogAction.Invoke(this);
            }
            catch (System.IO.IOException)
            {
                // 控制台不可用时忽略
            }
        }

        public void Log(LogLevel logLevel, string parseToString)
        {
            try
            {
                // PrintTimestamp();

                switch (logLevel)
                {
                    case LogLevel.None or LogLevel.Custom:
                        ResetColor();
                        Console.WriteLine(parseToString);
                        break;
                    case LogLevel.Info or LogLevel.Trace or LogLevel.Debug:
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write($"[{logLevel}] ");
                        ResetColor();
                        Console.WriteLine(parseToString);
                        break;
                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[{logLevel}] {parseToString}");
                        break;
                    case LogLevel.Error or LogLevel.Fatal:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[{logLevel}] {parseToString}");
                        break;
                }

                ResetColor();
            }
            catch (System.IO.IOException)
            {
                // 控制台不可用时忽略
            }
        }

        private void ResetColor()
        {
            Console.ForegroundColor = ConsoleColor.White;
        }

        private void PrintTimestamp()
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
        }
    }
}
