using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    public class ConsoleLogHandler : ILogHandler, ILogHandler<Log>, ILogHandler<LabelLog>
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
                ResetColor(); // 重置颜色

                // 打印标签
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"<{log.Label}> ");

                var logLevel = log.Level;

                switch (logLevel)
                {
                    case LogLevel.None or LogLevel.Custom:
                        Console.WriteLine(log.Message);
                        break;
                    case LogLevel.Info or LogLevel.Trace or LogLevel.Debug:
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"[{logLevel}] ");
                        ResetColor();
                        Console.WriteLine(log.Message);
                        break;
                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[{logLevel}] {log.Message}");
                        break;
                    case LogLevel.Error or LogLevel.Fatal:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[{logLevel}] {log.Message}");
                        break;
                }

                ResetColor();
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
                ResetColor(); // 重置颜色

                switch (logLevel)
                {
                    case LogLevel.None or LogLevel.Custom:
                        Console.WriteLine(parseToString);
                        break;
                    case LogLevel.Info or LogLevel.Trace or LogLevel.Debug:
                        Console.ForegroundColor = ConsoleColor.Cyan;
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
    }
}
