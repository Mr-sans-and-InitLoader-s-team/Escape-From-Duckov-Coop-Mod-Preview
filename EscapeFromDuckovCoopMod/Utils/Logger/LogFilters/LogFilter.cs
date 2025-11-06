using System;
using System.Collections.Generic;
using EscapeFromDuckovCoopMod.Utils.Logger.Core;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogFilters
{
    public class LogFilter : ILogFilter
    {
        private volatile Dictionary<Type, object> _typedFiltersSnapshot = new Dictionary<Type, object>();
        private readonly object _filtersSync = new object();

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

