using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using EscapeFromDuckovCoopMod.Utils.Logger.LogFilters;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    public class LogHandlerFilterDecorator : ILogHandler, IDecorator<ILogHandler>
    {
        public LogFilter Filter { get; }

        public ILogHandler LogHandler { get; }

        public ILogHandler Inner => LogHandler;

        public LogHandlerFilterDecorator(ILogHandler logHandler)
        {
            Filter = new LogFilter();
            LogHandler = logHandler;
        }

        public void Log<TLog>(TLog log) where TLog : struct, ILog
        {
            if (!Filter.Filter(log)) return;

            if (LogHandler is ILogHandler<TLog> typedHandler)
            {
                typedHandler.Log(log);
                return;
            }
            LogHandler.Log(log);
        }
    }

    public class LogHandlerFilterDecorator<TLog> : ILogHandler<TLog> where TLog : struct, ILog
    {
        public LogFilter<TLog> Filter { get; }

        public ILogHandler<TLog> LogHandler { get; }

        public LogHandlerFilterDecorator(ILogHandler<TLog> logHandler)
        {
            Filter = new LogFilter<TLog>();
            LogHandler = logHandler;
        }

        public void Log(TLog log)
        {
            if (!Filter.Filter(log)) return;

            LogHandler.Log(log);
        }
    }
}
