using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.Logs;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    public class UnityLogHandler : ILogHandler, ILogHandler<LabelLog>
    {
        public void Log<TLog>(TLog log) where TLog : struct, ILog
        {
            Log(log.Level, log.ParseToString());
        }

        public void Log(LabelLog log)
        {
            Log(log.Level, $"<color=#36FFA5><{log.Label}></color> {log.Message}");
        }

        public void Log(LogLevel logLevel, string parseToString)
        {
            switch (logLevel)
            {
                case LogLevel.None or LogLevel.Custom:
                    UnityEngine.Debug.Log(parseToString);
                    break;
                case LogLevel.Info or LogLevel.Trace or LogLevel.Debug:
                    UnityEngine.Debug.Log($"<color=#00FFFF>[{logLevel.ToString()}]</color> {parseToString}");
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning($"<color=yellow>[{logLevel.ToString()}]</color> {parseToString}");
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError($"<color=red>[{logLevel.ToString()}]</color> {parseToString}");
                    break;
                case LogLevel.Fatal:
                    UnityEngine.Debug.LogError($"<b><color=red>[{logLevel.ToString()}]</color></b> {parseToString}");
#if UNITY_EDITOR
                    UnityEngine.Debug.Break();
#endif
                    break;
            }
        }
    }
}