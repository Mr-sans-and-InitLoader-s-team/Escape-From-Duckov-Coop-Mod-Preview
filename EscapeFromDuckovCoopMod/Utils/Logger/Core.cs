namespace EscapeFromDuckovCoopMod.Utils.Logger.Core
{
    /// <summary>
    /// 日志等级
    /// </summary>
    public enum LogLevel : byte
    {
        None = 0,
        Info = 1,
        Trace = 2,
        Debug = 3,
        Warning = 4,
        Error = 5,
        Fatal = 6,
        Custom = 7
    }

    /// <summary>
    /// 日志接口
    /// </summary>
    /// <remarks>
    /// 定义日志的基本成员
    /// </remarks>
    public interface ILog
    {
        LogLevel Level { get; }
        string ParseToString();
    }

    /// <summary>
    /// 日志处理器接口（通用版本）
    /// </summary>
    public interface ILogHandler
    {
        void Log<TLog>(TLog log) where TLog : struct, ILog;
    }

    /// <summary>
    /// 日志处理器接口（特化版本）
    /// </summary>
    /// <typeparam name="TLog">要处理的日志类型</typeparam>
    public interface ILogHandler<TLog> where TLog : struct, ILog
    {
        void Log(TLog log);
    }

    /// <summary>
    /// 日志过滤器接口（通用版本）
    /// </summary>
    public interface ILogFilter
    {
        bool Filter<TLog>(TLog log) where TLog : struct, ILog;
    }

    /// <summary>
    /// 日志过滤器接口（特化版本）
    /// </summary>
    /// <typeparam name="TLog">要过滤的日志类型</typeparam>
    public interface ILogFilter<TLog> where TLog : struct, ILog
    {
        bool Filter(TLog log);
    }


    // ===========以下没做完整支持===========
    /// <summary>
    /// 日志增强器接口（通用版本）
    /// </summary>
    public interface ILogEnricher
    {
        TLog Enrich<TLog>(TLog log) where TLog : struct, ILog;
    }

    /// <summary>
    /// 日志增强器接口（特化版本）
    /// </summary>
    /// <typeparam name="TLog"></typeparam>
    public interface ILogEnricher<TLog> where TLog : struct, ILog
    {
        TLog Enrich(TLog log);
    }

    /// <summary>
    /// 日志格式化器接口（通用版本）
    /// </summary>
    public interface ILogFormatter
    {
        string Format<TLog>(TLog log) where TLog : struct, ILog;
    }

    /// <summary>
    /// 日志格式化器接口（特化版本）
    /// </summary>
    /// <typeparam name="TLog">要格式化的日志类型</typeparam>
    public interface ILogFormatter<TLog> where TLog : struct, ILog
    {
        string Format(TLog log);
    }


    /*
    /// <summary>
    /// 日志处理器抽象基类
    /// </summary>
    /// <typeparam name="TLogHandler">具体的子类类型</typeparam>
    public abstract class LogHandler<TLogHandler> : ILogHandlerOld
        where TLogHandler : LogHandler<TLogHandler>
    {
        bool ILogHandlerOld.IsEnabled => IsEnabledInternalMethod();

        protected abstract bool IsEnabledInternalMethod();

        public abstract void OnInit();
        public abstract void OnDispose();

        protected abstract ILogHandlerFilter<TLogHandler> IFilter { get; }
        protected abstract ILogHandlerProxy<TLogHandler> IProxyHandler { get; }

        public abstract void DefaultHandler<TLog>(TLog log) where TLog : struct, ILog;

        public virtual void Log<TLog>(TLog log) where TLog : struct, ILog
        {
            // 先按数组快照方式获取，热路径零分配
            if (IFilter.TryGetFilter<TLog>(out var filterArray))
            {
                var tempHandler = (TLogHandler)this;
                for (int i = 0; i < filterArray.Length; i++)
                {
                    if (!filterArray[i](log, tempHandler)) return;
                }
            }

            if (IProxyHandler.TryGetProxy<TLog>(out var proxyHandler))
            {
                proxyHandler(log, (TLogHandler)this);
            }
            else
            {
                DefaultHandler(log);
            }
        }
    }

    /// <summary>
    /// 日志处理器代理器接口
    /// </summary>
    /// <typeparam name="TLogHandler">日志处理器类型</typeparam>
    public interface ILogHandlerProxy<TLogHandler> where TLogHandler : ILogHandlerOld
    {
        bool TryGetProxy<TLog>(out Action<TLog, TLogHandler> action) where TLog : struct, ILog;
    }

    /// <summary>
    /// 日志处理器代理器默认实现
    /// </summary>
    /// <typeparam name="TLogHandler">日志处理器类型</typeparam>
    public class LogHandlerProxy<TLogHandler> : ILogHandlerProxy<TLogHandler> where TLogHandler : ILogHandlerOld
    {
        private volatile Dictionary<Type, Delegate> _proxySnapshot = new Dictionary<Type, Delegate>();
        private readonly object _sync = new object();

        public LogHandlerProxy<TLogHandler> AddProxy<TLog>(Action<TLog, TLogHandler> action) where TLog : struct, ILog
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (_sync)
            {
                var old = _proxySnapshot;
                var newDic = new Dictionary<Type, Delegate>(old);
                var logType = typeof(TLog);
                if (newDic.TryGetValue(logType, out var existing))
                {
                    var combined = (Action<TLog, TLogHandler>)existing + action;
                    newDic[logType] = combined;
                }
                else
                {
                    newDic.Add(logType, action);
                }
                _proxySnapshot = newDic;
            }
            return this;
        }

        public LogHandlerProxy<TLogHandler> RemoveProxy<TLog>(Action<TLog, TLogHandler> action) where TLog : struct, ILog
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (_sync)
            {
                var old = _proxySnapshot;
                var logType = typeof(TLog);
                if (!old.TryGetValue(logType, out var existing)) return this;

                var combined = (Action<TLog, TLogHandler>)existing - action;
                var newDic = new Dictionary<Type, Delegate>(old);
                if (combined == null)
                    newDic.Remove(logType);
                else
                    newDic[logType] = combined;

                _proxySnapshot = newDic;
            }
            return this;
        }

        public LogHandlerProxy<TLogHandler> ClearProxys()
        {
            lock (_sync)
            {
                _proxySnapshot = new Dictionary<Type, Delegate>();
            }
            return this;
        }

        public bool TryGetProxy<TLog>(out Action<TLog, TLogHandler> action) where TLog : struct, ILog
        {
            var snapshot = _proxySnapshot;
            if (snapshot.TryGetValue(typeof(TLog), out var d))
            {
                action = (Action<TLog, TLogHandler>)d;
                return true;
            }
            action = null;
            return false;
        }

        public bool ContainsProxy<TLog>() where TLog : struct, ILog
        {
            return _proxySnapshot.ContainsKey(typeof(TLog));
        }
    }

    /// <summary>
    /// 日志处理器过滤器接口
    /// </summary>
    /// <typeparam name="TLogHandler">日志处理器类型</typeparam>
    public interface ILogHandlerFilter<TLogHandler> where TLogHandler : ILogHandlerOld
    {
        bool TryGetFilter<TLog>(out Func<TLog, TLogHandler, bool>[] funcArray) where TLog : struct, ILog;
    }

    /// <summary>
    /// 日志处理器过滤器默认实现
    /// </summary>
    /// <typeparam name="TLogHandler">日志处理器类型</typeparam>
    public class LogHandlerFilter<TLogHandler> : ILogHandlerFilter<TLogHandler> where TLogHandler : ILogHandlerOld
    {
        private volatile Dictionary<Type, object> _filterSnapshot = new Dictionary<Type, object>();
        private readonly object _sync = new object();

        public LogHandlerFilter<TLogHandler> AddFilter<TLog>(Func<TLog, bool> func) where TLog : struct, ILog
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            return AddFilter<TLog>((log, handler) => func(log));
        }

        public LogHandlerFilter<TLogHandler> AddFilter<TLog>(Func<TLog, TLogHandler, bool> func) where TLog : struct, ILog
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            lock (_sync)
            {
                var old = _filterSnapshot;
                var newDic = new Dictionary<Type, object>(old);
                var logType = typeof(TLog);
                if (newDic.TryGetValue(logType, out var obj))
                {
                    var oldArr = (Func<TLog, TLogHandler, bool>[])obj;
                    var newArr = new Func<TLog, TLogHandler, bool>[oldArr.Length + 1];
                    Array.Copy(oldArr, newArr, oldArr.Length);
                    newArr[^1] = func;
                    newDic[logType] = newArr;
                }
                else
                {
                    newDic[logType] = new Func<TLog, TLogHandler, bool>[] { func };
                }
                _filterSnapshot = newDic;
            }
            return this;
        }

        public LogHandlerFilter<TLogHandler> RemoveFilter<TLog>(Func<TLog, bool> func) where TLog : struct, ILog
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            return RemoveFilter<TLog>((log, handler) => func(log));
        }

        public LogHandlerFilter<TLogHandler> RemoveFilter<TLog>(Func<TLog, TLogHandler, bool> func) where TLog : struct, ILog
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            lock (_sync)
            {
                var old = _filterSnapshot;
                var logType = typeof(TLog);
                if (!old.TryGetValue(logType, out var obj)) return this;

                var oldArr = (Func<TLog, TLogHandler, bool>[])obj;
                int idx = Array.IndexOf(oldArr, func);
                if (idx < 0) return this;

                var newDic = new Dictionary<Type, object>(old);
                if (oldArr.Length == 1)
                {
                    newDic.Remove(logType);
                }
                else
                {
                    var newArr = new Func<TLog, TLogHandler, bool>[oldArr.Length - 1];
                    if (idx > 0) Array.Copy(oldArr, 0, newArr, 0, idx);
                    if (idx < oldArr.Length - 1) Array.Copy(oldArr, idx + 1, newArr, idx, oldArr.Length - idx - 1);
                    newDic[logType] = newArr;
                }
                _filterSnapshot = newDic;
            }
            return this;
        }

        public LogHandlerFilter<TLogHandler> ClearFilters()
        {
            lock (_sync)
            {
                _filterSnapshot = new Dictionary<Type, object>();
            }
            return this;
        }


        public bool TryGetFilter<TLog>(out Func<TLog, TLogHandler, bool>[] funcArray) where TLog : struct, ILog
        {
            var snapshot = _filterSnapshot;
            if (snapshot.TryGetValue(typeof(TLog), out var obj))
            {
                funcArray = (Func<TLog, TLogHandler, bool>[])obj; // 返回不可变数组快照
                return true;
            }
            funcArray = null;
            return false;
        }

        public bool ContainsFilter<TLog>() where TLog : struct, ILog
        {
            return _filterSnapshot.ContainsKey(typeof(TLog));
        }
    }
    */
}