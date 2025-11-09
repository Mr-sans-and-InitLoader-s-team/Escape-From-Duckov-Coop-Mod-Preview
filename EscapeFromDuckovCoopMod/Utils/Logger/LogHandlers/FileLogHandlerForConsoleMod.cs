using Duckov.Modding;
using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    public class FileLogHandlerForConsoleMod : ILogHandler, ILogHandler<Log>, ILogHandler<LabelLog>, ILogHandler<LogHandlerAsyncDecorator.AsyncLog>
    {
        private StreamWriter _logFileWriter;
        private StreamWriter _latestLogWriter;

        public static ILogHandler Instance { get; private set; }

        /// <summary>
        /// 本方法会在 LoggerHelper.Instance 初始化时调用<br/>
        /// 注意不要在本方法内部直接调用 LoggerHelper.Instance
        /// </summary>
        /// <param name="logger"></param>
        public static void Init(Logger logger)
        {
            // 首先检测已加载 Mod 中是否有控制台Mod
            if (ModManager.IsModActive(new ModInfo { name = "控制台Mod" }, out var modBehaviour) && modBehaviour.info.publishedFileId == 3589089241)
            {
                if (Instance is null)
                {
                    var fileLogHandlerForConsoleMod = new FileLogHandlerForConsoleMod();
                    fileLogHandlerForConsoleMod._logFileWriter = AccessTools.StaticFieldRefAccess<StreamWriter>(modBehaviour.GetType(), "logFileWriter");
                    fileLogHandlerForConsoleMod._latestLogWriter = AccessTools.StaticFieldRefAccess<StreamWriter>(modBehaviour.GetType(), "latestLogWriter");

                    if (fileLogHandlerForConsoleMod._logFileWriter is null || fileLogHandlerForConsoleMod._latestLogWriter is null)
                    {
                        UnityEngine.Debug.LogError("无法获取 StreamWriter，放弃初始化 FileLogHandlerForConsoleMod");
                        return;
                    }

                    // 使用异步装饰器包装
                    Instance = LogHandlerAsyncDecorator.CreateDecorator(fileLogHandlerForConsoleMod);
                    logger.AddHandler(Instance);
                }
            }

            ModManager.OnModActivated += ModManager_OnModActivated;

            ModManager.OnModWillBeDeactivated += ModManager_OnModWillBeDeactivated;

            static void ModManager_OnModActivated(ModInfo info, Duckov.Modding.ModBehaviour modBehaviour)
            {
                // 如果激活的是控制台Mod
                if (info.name == "控制台Mod" || info.publishedFileId == 3589089241)
                {
                    if (Instance is null)
                    {
                        var fileLogHandlerForConsoleMod = new FileLogHandlerForConsoleMod();
                        fileLogHandlerForConsoleMod._logFileWriter = AccessTools.StaticFieldRefAccess<StreamWriter>(modBehaviour.GetType(), "logFileWriter");
                        fileLogHandlerForConsoleMod._latestLogWriter = AccessTools.StaticFieldRefAccess<StreamWriter>(modBehaviour.GetType(), "latestLogWriter");

                        if (fileLogHandlerForConsoleMod._logFileWriter is null || fileLogHandlerForConsoleMod._latestLogWriter is null)
                        {
                            UnityEngine.Debug.LogError("无法获取 StreamWriter，放弃初始化 FileLogHandlerForConsoleMod");
                            return;
                        }

                        // 使用异步装饰器包装
                        Instance = LogHandlerAsyncDecorator.CreateDecorator(fileLogHandlerForConsoleMod);
                        LoggerHelper.Instance.AddHandler(Instance);
                    }
                }
            }

            static void ModManager_OnModWillBeDeactivated(ModInfo info, Duckov.Modding.ModBehaviour modBehaviour)
            {
                // 如果停用的是控制台Mod
                if (info.name == "控制台Mod" || info.publishedFileId == 3589089241)
                {
                    if (Instance is not null)
                    {
                        LoggerHelper.Instance.TryRemoveHandler(Instance);

                        if (Instance is IDecorator<ILogHandler> decorator)
                        {
                            if (decorator.GetRoot() is FileLogHandlerForConsoleMod fileLogHandler)
                            {
                                fileLogHandler._logFileWriter = null;
                                fileLogHandler._latestLogWriter = null;
                            }
                        }
                        else if (Instance is FileLogHandlerForConsoleMod fileLogHandler)
                        {
                            fileLogHandler._logFileWriter = null;
                            fileLogHandler._latestLogWriter = null;
                        }
                        Instance = null;
                    }
                }
            }
        }


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
            Log(log.Level, log.ParseToString());
        }

        public void Log(LogHandlerAsyncDecorator.AsyncLog log)
        {
            // 写入时间戳
            WriteLog($"[{log.Timestamp:HH:mm:ss}] ");

            log.LogAction(this);
        }

        public void Log(LogLevel logLevel, string parseToString)
        {
            if (logLevel is LogLevel.None or LogLevel.Custom)
            {
                WriteLineLog(parseToString);
            }
            else
            {
                WriteLineLog($"[{logLevel}] {parseToString}");
            }
        }

        private void WriteLog(string message)
        {
            // 写入到外部程序集的 StreamWriter
            if (_logFileWriter is not null)
            {
                try
                {
                    _logFileWriter.Write(message);
                }
                catch
                {
                    // 忽略写入错误
                }
            }

            if (_latestLogWriter is not null)
            {
                try
                {
                    _latestLogWriter.Write(message);
                }
                catch
                {
                    // 忽略写入错误
                }
            }
        }

        private void WriteLineLog(string message)
        {
            // 写入到外部程序集的 StreamWriter
            if (_logFileWriter is not null)
            {
                try
                {
                    _logFileWriter.WriteLine(message);
                }
                catch
                {
                    // 忽略写入错误
                }
            }

            if (_latestLogWriter is not null)
            {
                try
                {
                    _latestLogWriter.WriteLine(message);
                }
                catch
                {
                    // 忽略写入错误
                }
            }
        }
    }
}
