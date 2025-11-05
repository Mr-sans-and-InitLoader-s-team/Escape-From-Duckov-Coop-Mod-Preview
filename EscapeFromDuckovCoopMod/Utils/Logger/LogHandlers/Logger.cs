using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.LogFilters;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    public class Logger : ILogHandler
    {
        /// <summary>
        /// 存储日志处理器的快照，保证读取时的线程安全
        /// </summary>
        private volatile ILogHandler[] _logHandlersSnapshot = Array.Empty<ILogHandler>();

        private readonly object _handlersSync = new object();

        /// <summary>
        /// 作用于 Logger 的日志过滤器
        /// </summary>
        public LogFilter Filter { get; } = new LogFilter();

        void ILogHandler.Log<TLog>(TLog log) => Log(log);

        public Logger Log<TLog>(TLog log) where TLog : struct, ILog
        {
            // 首先通过过滤器过滤日志
            if (!Filter.Filter(log)) return this;

            var handlersSnapshot = _logHandlersSnapshot;
            for (int i = 0; i < handlersSnapshot.Length; i++)
            {
                var handler = handlersSnapshot[i];

                if (handler is ILogHandler<TLog> typedHandler)
                {
                    typedHandler.Log(log);
                    continue;
                }
                handler.Log(log);
            }
            return this;
        }

        #region 与 LogHandler 相关的操作
        public Logger AddHandler(ILogHandler logHandler)
        {
            if (logHandler == null || logHandler == this) return this;
            lock (_handlersSync)
            {
                var oldHandlers = _logHandlersSnapshot;
                int oldLength = oldHandlers.Length;
                var newHandlers = new ILogHandler[oldLength + 1];
                Array.Copy(oldHandlers, 0, newHandlers, 0, oldLength);
                newHandlers[oldLength] = logHandler;
                _logHandlersSnapshot = newHandlers;
            }
            return this;
        }

        public bool TryRemoveHandler(ILogHandler logHandler)
        {
            if (logHandler == null) return false;
            lock (_handlersSync)
            {
                var oldHandlers = _logHandlersSnapshot;
                int oldLength = oldHandlers.Length;
                int index = Array.IndexOf(oldHandlers, logHandler);
                if (index < 0) return false;
                if (oldLength == 1)
                {
                    _logHandlersSnapshot = Array.Empty<ILogHandler>();
                }
                else
                {
                    var newHandlers = new ILogHandler[oldLength - 1];
                    if (index > 0)
                    {
                        Array.Copy(oldHandlers, 0, newHandlers, 0, index);
                    }
                    if (index < oldLength - 1)
                    {
                        Array.Copy(oldHandlers, index + 1, newHandlers, index, oldLength - index - 1);
                    }
                    _logHandlersSnapshot = newHandlers;
                }
            }
            return true;
        }

        public bool TryRemoveHandler<TLogHandler>(out IReadOnlyList<TLogHandler> removedHandlers) where TLogHandler : ILogHandler
        {
            lock (_handlersSync)
            {
                var oldHandlers = _logHandlersSnapshot;
                var newHandlers = new List<ILogHandler>(oldHandlers.Length);
                var removedList = new List<TLogHandler>();
                for (int i = 0; i < oldHandlers.Length; i++)
                {
                    var handler = oldHandlers[i];
                    if (handler is TLogHandler typedHandler)
                    {
                        removedList.Add(typedHandler);
                    }
                    else
                    {
                        newHandlers.Add(handler);
                    }
                }
                if (removedList.Count == 0)
                {
                    removedHandlers = null;
                    return false;
                }
                _logHandlersSnapshot = newHandlers.ToArray();
                removedHandlers = removedList;
                return true;
            }
        }

        public bool TryRemoveHandler(Func<ILogHandler, bool> matcher, out IReadOnlyList<ILogHandler> removedHandlers)
        {
            if (matcher == null)
            {
                removedHandlers = null;
                return false;
            }
            lock (_handlersSync)
            {
                var oldHandlers = _logHandlersSnapshot;
                var newHandlers = new List<ILogHandler>(oldHandlers.Length);
                var removedList = new List<ILogHandler>();
                for (int i = 0; i < oldHandlers.Length; i++)
                {
                    var handler = oldHandlers[i];
                    if (matcher(handler))
                    {
                        removedList.Add(handler);
                    }
                    else
                    {
                        newHandlers.Add(handler);
                    }
                }

                if (removedList.Count == 0)
                {
                    removedHandlers = null;
                    return false;
                }
                _logHandlersSnapshot = newHandlers.ToArray();
                removedHandlers = removedList;
                return true;
            }
        }

        public bool TryGetHandler<TLogHandler>(out IReadOnlyList<TLogHandler> handlers) where TLogHandler : ILogHandler
        {
            var foundHandlers = new List<TLogHandler>();
            var handlersSnapshot = _logHandlersSnapshot;
            for (int i = 0; i < handlersSnapshot.Length; i++)
            {
                var handler = handlersSnapshot[i];
                if (handler is TLogHandler typedHandler)
                {
                    foundHandlers.Add(typedHandler);
                }
            }
            if (foundHandlers.Count == 0)
            {
                handlers = null;
                return false;
            }
            handlers = foundHandlers;
            return true;
        }

        public bool TryGetHandler(Func<ILogHandler, bool> matcher, out IReadOnlyList<ILogHandler> handlers)
        {
            if (matcher == null)
            {
                handlers = null;
                return false;
            }
            var foundHandlers = new List<ILogHandler>();
            var handlersSnapshot = _logHandlersSnapshot;
            for (int i = 0; i < handlersSnapshot.Length; i++)
            {
                var handler = handlersSnapshot[i];
                if (matcher(handler))
                {
                    foundHandlers.Add(handler);
                }
            }
            if (foundHandlers.Count == 0)
            {
                handlers = null;
                return false;
            }
            handlers = foundHandlers;
            return true;
        }
        #endregion
    }
}