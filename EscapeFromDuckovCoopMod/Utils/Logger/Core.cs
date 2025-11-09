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

    public interface IDecorator<T> where T : class
    {
        T Inner { get; }

        T GetRoot()
        {
            T current = Inner;
            while (current is IDecorator<T> decorator)
            {
                current = decorator.Inner;
            }
            return current;
        }
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

}