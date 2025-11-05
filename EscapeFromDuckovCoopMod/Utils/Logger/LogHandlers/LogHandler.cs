using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    /// <summary>
    /// 基于委托的默认日志处理器实现
    /// </summary>
    /// <remarks>
    /// 支持任意日志类型
    /// 基于 Copy-On-Write 实现线程安全
    /// </remarks>
    public class LogHandler : ILogHandler
    {
        private volatile Dictionary<Type, object> _typedHandlersSnapshot = new Dictionary<Type, object>();

        private readonly object _handlersSync = new object();

        public void Log<TLog>(TLog log) where TLog : struct, ILog
        {
            var handlersSnapshot = _typedHandlersSnapshot;
            if (handlersSnapshot.TryGetValue(typeof(TLog), out var handlerObj))
            {
                var handler = (LogHandler<TLog>)handlerObj;
                handler.Log(log);
            }
        }

        public LogHandler AddHandler<TLog>(ILogHandler<TLog> logHandler) where TLog : struct, ILog
        {
            if (logHandler == null) return this;
            lock (_handlersSync)
            {
                var oldHandlers = _typedHandlersSnapshot;
                var logType = typeof(TLog);
                if (oldHandlers.TryGetValue(logType, out var existingHandlerObj))
                {
                    var existingHandler = (LogHandler<TLog>)existingHandlerObj;
                    existingHandler.AddHandler(logHandler);
                }
                else
                {
                    var newLogHandler = new LogHandler<TLog>().AddHandler(logHandler);
                    var newHandlers = new Dictionary<Type, object>(oldHandlers)
                    {
                        [logType] = newLogHandler
                    };
                    _typedHandlersSnapshot = newHandlers;
                }
            }
            return this;
        }

        public LogHandler RemoveHandler<TLog>(ILogHandler<TLog> logHandler) where TLog : struct, ILog
        {
            if (logHandler == null) return this;
            lock (_handlersSync)
            {
                var oldHandlers = _typedHandlersSnapshot;
                var logType = typeof(TLog);
                if (oldHandlers.TryGetValue(logType, out var existingHandlerObj))
                {
                    var existingHandler = (LogHandler<TLog>)existingHandlerObj;
                    existingHandler.RemoveHandler(logHandler);
                    if (existingHandler.IsEmpty)
                    {
                        var newHandlers = new Dictionary<Type, object>(oldHandlers);
                        newHandlers.Remove(logType);
                        _typedHandlersSnapshot = newHandlers;
                    }
                }
            }
            return this;
        }

        public LogHandler AddHandler<TLog>(Action<TLog> handler) where TLog : struct, ILog
        {
            if (handler == null) return this;
            lock (_handlersSync)
            {
                var oldHandlers = _typedHandlersSnapshot;
                var logType = typeof(TLog);
                if (oldHandlers.TryGetValue(logType, out var existingHandlerObj))
                {
                    var existingHandler = (LogHandler<TLog>)existingHandlerObj;
                    existingHandler.AddHandler(handler);
                }
                else
                {
                    var newLogHandler = new LogHandler<TLog>().AddHandler(handler);
                    var newHandlers = new Dictionary<Type, object>(oldHandlers)
                    {
                        [logType] = newLogHandler
                    };
                    _typedHandlersSnapshot = newHandlers;
                }
            }
            return this;
        }

        public LogHandler RemoveHandler<TLog>(Action<TLog> handler) where TLog : struct, ILog
        {
            if (handler == null) return this;
            lock (_handlersSync)
            {
                var oldHandlers = _typedHandlersSnapshot;
                var logType = typeof(TLog);
                if (oldHandlers.TryGetValue(logType, out var existingHandlerObj))
                {
                    var existingHandler = (LogHandler<TLog>)existingHandlerObj;
                    existingHandler.RemoveHandler(handler);
                    if (existingHandler.IsEmpty)
                    {
                        var newHandlers = new Dictionary<Type, object>(oldHandlers);
                        newHandlers.Remove(logType);
                        _typedHandlersSnapshot = newHandlers;
                    }
                }
            }
            return this;
        }
    }

    /// <summary>
    /// 基于委托的默认日志处理器实现
    /// </summary>
    /// <remarks>
    /// 支持特定日志类型
    /// 基于 Copy-On-Write 实现线程安全
    /// </remarks>
    public class LogHandler<TLog> : ILogHandler<TLog> where TLog : struct, ILog
    {
        private volatile Action<TLog> _handlersSnapshot;
        private readonly object _handlersSync = new object();

        public bool IsEmpty => _handlersSnapshot == null;

        public void Log(TLog log)
        {
            var handlersSnapshot = _handlersSnapshot;
            handlersSnapshot?.Invoke(log);
        }

        public LogHandler<TLog> AddHandler(ILogHandler<TLog> logHandler)
        {
            if (logHandler == null || logHandler == this) return this;
            return AddHandler(logHandler.Log);
        }

        public LogHandler<TLog> RemoveHandler(ILogHandler<TLog> logHandler)
        {
            if (logHandler == null || logHandler == this) return this;
            return RemoveHandler(logHandler.Log);
        }

        public LogHandler<TLog> AddHandler(Action<TLog> handler)
        {
            if (handler == null) return this;
            lock (_handlersSync)
            {
                var oldHandlers = _handlersSnapshot;
                var newHandlers = oldHandlers + handler;
                _handlersSnapshot = newHandlers;
            }
            return this;
        }

        public LogHandler<TLog> RemoveHandler(Action<TLog> handler)
        {
            if (handler == null) return this;
            lock (_handlersSync)
            {
                var oldHandlers = _handlersSnapshot;
                var newHandlers = oldHandlers - handler;
                _handlersSnapshot = newHandlers;
            }
            return this;
        }
    }
}
