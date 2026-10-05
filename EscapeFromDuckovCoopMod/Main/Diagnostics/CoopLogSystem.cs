using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

internal static class CoopLogSystem
{
    private static bool _installed;
    private static ILogHandler _networkDiagnosticHandler;

    // Connection lifecycle and explicitly requested F10 reports must remain
    // available even when the release build suppresses ordinary verbose logs.
    internal static void WriteNetworkDiagnostic(string message)
    {
        var handler = _networkDiagnosticHandler ?? Debug.unityLogger.logHandler;
        handler?.LogFormat(LogType.Log, null, "{0}", message);
    }

    public static void Install()
    {
        if (_installed)
            return;

        _installed = true;
        _networkDiagnosticHandler = Debug.unityLogger?.logHandler;

        if (BuildInfo.RuntimeVerboseLoggingEnabled)
            return;

        var logger = Debug.unityLogger;
        if (logger?.logHandler == null || logger.logHandler is ReleaseLogHandler)
            return;

        logger.logHandler = new ReleaseLogHandler(logger.logHandler);
    }

    private sealed class ReleaseLogHandler : ILogHandler
    {
        private readonly ILogHandler _inner;

        public ReleaseLogHandler(ILogHandler inner)
        {
            _inner = inner;
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (!ShouldWrite(logType))
                return;

            _inner?.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (!BuildInfo.RuntimeCriticalLoggingEnabled)
                return;

            _inner?.LogException(exception, context);
        }

        private static bool ShouldWrite(LogType logType)
        {
            if (BuildInfo.RuntimeVerboseLoggingEnabled)
                return true;

            return BuildInfo.RuntimeCriticalLoggingEnabled
                   && (logType == LogType.Error || logType == LogType.Exception || logType == LogType.Assert);
        }
    }
}
