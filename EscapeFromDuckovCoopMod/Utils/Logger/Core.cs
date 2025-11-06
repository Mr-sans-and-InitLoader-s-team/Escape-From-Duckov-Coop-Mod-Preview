namespace EscapeFromDuckovCoopMod.Utils.Logger.Core
{
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

    public interface ILog
    {
        LogLevel Level { get; }
        string ParseToString();
    }

    public interface ILogHandler
    {
        void Log<TLog>(TLog log) where TLog : struct, ILog;
    }

    public interface ILogHandler<TLog> where TLog : struct, ILog
    {
        void Log(TLog log);
    }

    public interface ILogFilter
    {
        bool Filter<TLog>(TLog log) where TLog : struct, ILog;
    }

    public interface ILogFilter<TLog> where TLog : struct, ILog
    {
        bool Filter(TLog log);
    }

    public interface ILogEnricher
    {
        TLog Enrich<TLog>(TLog log) where TLog : struct, ILog;
    }

    public interface ILogEnricher<TLog> where TLog : struct, ILog
    {
        TLog Enrich(TLog log);
    }

    public interface ILogFormatter
    {
        string Format<TLog>(TLog log) where TLog : struct, ILog;
    }

    public interface ILogFormatter<TLog> where TLog : struct, ILog
    {
        string Format(TLog log);
    }
}

