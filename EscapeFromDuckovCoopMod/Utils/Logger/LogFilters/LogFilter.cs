using EscapeFromDuckovCoopMod.Utils.Logger.Core;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogFilters
{
    /// <summary>
    /// 基于委托的默认日志过滤器实现
    /// </summary>
    /// <remarks>
    /// 支持任意日志类型
    /// 基于 Copy-On-Write 实现线程安全
    /// </remarks>
    public class LogFilter : ILogFilter
    {
        private volatile Dictionary<Type, object> _typedFiltersSnapshot = new Dictionary<Type, object>();

        private readonly object _filtersSync = new object();

        /// <summary>
        /// 判断传入的日志对象是否符合其类型对应的过滤条件
        /// </summary>
        /// <typeparam name="TLog">日志类型</typeparam>
        /// <param name="log">日志对象</param>
        /// <returns>是否满足过滤条件</returns>
        public bool Filter<TLog>(TLog log) where TLog : struct, ILog
        {
            var filtersSnapshot = _typedFiltersSnapshot;
            if (filtersSnapshot.TryGetValue(typeof(TLog), out var filterObj))
            {
                var filter = (LogFilter<TLog>)filterObj;
                return filter.Filter(log);
            }
            return true;
        }

        /// <summary>
        /// 为指定日志类型添加过滤条件
        /// </summary>
        /// <typeparam name="TLog"></typeparam>
        /// <param name="logFilter"></param>
        /// <returns></returns>
        public LogFilter AddFilter<TLog>(ILogFilter<TLog> logFilter) where TLog : struct, ILog
        {
            if (logFilter == null) return this;
            lock (_filtersSync)
            {
                var oldFilters = _typedFiltersSnapshot;
                var logType = typeof(TLog);
                if (oldFilters.TryGetValue(logType, out var existingFilterObj))
                {
                    var existingFilter = (LogFilter<TLog>)existingFilterObj;
                    existingFilter.AddFilter(logFilter);
                }
                else
                {
                    var newLogFilter = new LogFilter<TLog>().AddFilter(logFilter);
                    var newFilters = new Dictionary<Type, object>(oldFilters)
                    {
                        [logType] = newLogFilter
                    };
                    _typedFiltersSnapshot = newFilters;
                }
            }
            return this;
        }

        /// <summary>
        /// 为指定日志类型移除过滤条件
        /// </summary>
        /// <typeparam name="TLog"></typeparam>
        /// <param name="logFilter"></param>
        /// <returns></returns>
        public LogFilter RemoveFilter<TLog>(ILogFilter<TLog> logFilter) where TLog : struct, ILog
        {
            if (logFilter == null) return this;
            lock (_filtersSync)
            {
                var oldFilters = _typedFiltersSnapshot;
                var logType = typeof(TLog);
                if (oldFilters.TryGetValue(logType, out var existingFilterObj))
                {
                    var existingFilter = (LogFilter<TLog>)existingFilterObj;
                    existingFilter.RemoveFilter(logFilter);
                    if (existingFilter.IsEmpty)
                    {
                        var newFilters = new Dictionary<Type, object>(oldFilters);
                        newFilters.Remove(logType);
                        _typedFiltersSnapshot = newFilters;
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// 为指定日志类型添加过滤条件
        /// </summary>
        /// <typeparam name="TLog"></typeparam>
        /// <param name="filterFunc"></param>
        /// <returns></returns>
        public LogFilter AddFilter<TLog>(Func<TLog, bool> filterFunc) where TLog : struct, ILog
        {
            if (filterFunc == null) return this;
            lock (_filtersSync)
            {
                var oldFilters = _typedFiltersSnapshot;
                var logType = typeof(TLog);
                if (oldFilters.TryGetValue(logType, out var existingFilterObj))
                {
                    var existingFilter = (LogFilter<TLog>)existingFilterObj;
                    existingFilter.AddFilter(filterFunc);
                }
                else
                {
                    var newLogFilter = new LogFilter<TLog>().AddFilter(filterFunc);
                    var newFilters = new Dictionary<Type, object>(oldFilters)
                    {
                        [logType] = newLogFilter
                    };
                    _typedFiltersSnapshot = newFilters;
                }
            }
            return this;
        }

        /// <summary>
        /// 为指定日志类型移除过滤条件
        /// </summary>
        /// <typeparam name="TLog"></typeparam>
        /// <param name="filterFunc"></param>
        /// <returns></returns>
        public LogFilter RemoveFilter<TLog>(Func<TLog, bool> filterFunc) where TLog : struct, ILog
        {
            if (filterFunc == null) return this;
            lock (_filtersSync)
            {
                var oldFilters = _typedFiltersSnapshot;
                var logType = typeof(TLog);
                if (oldFilters.TryGetValue(logType, out var existingFilterObj))
                {
                    var existingFilter = (LogFilter<TLog>)existingFilterObj;
                    existingFilter.RemoveFilter(filterFunc);
                    if (existingFilter.IsEmpty)
                    {
                        var newFilters = new Dictionary<Type, object>(oldFilters);
                        newFilters.Remove(logType);
                        _typedFiltersSnapshot = newFilters;
                    }
                }
            }
            return this;
        }
    }

    /// <summary>
    /// 基于委托的默认日志过滤器实现
    /// </summary>
    /// <remarks>
    /// 支持特定日志类型
    /// 基于 Copy-On-Write 实现线程安全
    /// </remarks>
    public class LogFilter<TLog> : ILogFilter<TLog> where TLog : struct, ILog
    {
        private volatile Func<TLog, bool>[] _filtersSnapshot = Array.Empty<Func<TLog, bool>>();
        private readonly object _filtersSync = new object();

        public bool IsEmpty => _filtersSnapshot.Length == 0;

        public bool Filter(TLog log)
        {
            var filtersSnapshot = _filtersSnapshot;
            for (int i = 0; i < filtersSnapshot.Length; i++)
            {
                if (!filtersSnapshot[i](log)) return false;
            }
            return true;
        }

        public LogFilter<TLog> AddFilter(ILogFilter<TLog> logFilter)
        {
            if (logFilter == null || logFilter == this) return this;
            return AddFilter(logFilter.Filter);
        }

        public LogFilter<TLog> RemoveFilter(ILogFilter<TLog> logFilter)
        {
            if (logFilter == null || logFilter == this) return this;
            return RemoveFilter(logFilter.Filter);
        }

        public LogFilter<TLog> AddFilter(Func<TLog, bool> filterFunc)
        {
            if (filterFunc == null) return this;
            lock (_filtersSync)
            {
                var oldFilters = _filtersSnapshot;
                int oldLength = oldFilters.Length;
                var newFilters = new Func<TLog, bool>[oldLength + 1];
                Array.Copy(oldFilters, 0, newFilters, 0, oldLength);
                newFilters[oldLength] = filterFunc;
                _filtersSnapshot = newFilters;
            }
            return this;
        }

        public LogFilter<TLog> RemoveFilter(Func<TLog, bool> filterFunc)
        {
            if (filterFunc == null) return this;
            lock (_filtersSync)
            {
                var oldFilters = _filtersSnapshot;
                int oldLength = oldFilters.Length;
                int index = Array.IndexOf(oldFilters, filterFunc);
                if (index < 0) return this;
                if (oldLength == 1)
                {
                    _filtersSnapshot = Array.Empty<Func<TLog, bool>>();
                    return this;
                }
                var newFilters = new Func<TLog, bool>[oldLength - 1];
                if (index > 0)
                {
                    Array.Copy(oldFilters, 0, newFilters, 0, index);
                }
                if (index < oldLength - 1)
                {
                    Array.Copy(oldFilters, index + 1, newFilters, index, oldLength - index - 1);
                }
                _filtersSnapshot = newFilters;
            }
            return this;
        }
    }
}
