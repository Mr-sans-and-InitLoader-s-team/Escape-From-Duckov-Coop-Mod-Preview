using EscapeFromDuckovCoopMod.Utils.Logger.Core;
using System.Collections.Concurrent;
using ILogHandler = EscapeFromDuckovCoopMod.Utils.Logger.Core.ILogHandler;
using ThreadPriority = System.Threading.ThreadPriority;

namespace EscapeFromDuckovCoopMod.Utils.Logger.LogHandlers
{
    /// <summary>
    /// 异步日志处理器装饰器（通用版本）
    /// </summary>
    /// <remarks>
    /// <para>使用独立的后台线程处理日志输出，避免调用线程阻塞在 I/O 操作上</para>
    /// <para>适用于多线程打日志场景 | 高频日志场景（> 1000 次/秒）| I/O 密集型处理器（如文件日志）</para>
    /// <para><b>注意：</b></para>
    /// <list type="bullet">
    /// <item>日志输出存在异步延迟</item>
    /// <item>队列满时会根据 <see cref="OverflowStrategy"/> 策略处理</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var consoleHandler = new ConsoleLogHandler();
    /// var asyncHandler = new LogHandlerAsyncDecorator(consoleHandler, queueCapacity: 1000);
    /// logger.AddHandler(asyncHandler);
    /// 
    /// // 程序退出时
    /// asyncHandler.Dispose(); // 等待所有日志写入完成
    /// </code>
    /// </example>
    public class LogHandlerAsyncDecorator : ILogHandler, IDisposable, IDecorator<ILogHandler>
    {
        /// <summary>
        /// 队列溢出时的处理策略
        /// </summary>
        public enum OverflowStrategy : byte
        {
            /// <summary>
            /// 阻塞调用线程，直到队列有空间（默认）
            /// </summary>
            Block,

            /// <summary>
            /// 丢弃新日志，立即返回
            /// </summary>
            DropNew,

            /// <summary>
            /// 丢弃队列中最旧的日志，插入新日志
            /// </summary>
            DropOldest
        }

        /// <summary>
        /// 被装饰的日志处理器
        /// </summary>
        public ILogHandler LogHandler { get; }

        public ILogHandler Inner => LogHandler;

        /// <summary>
        /// 队列溢出策略
        /// </summary>
        public OverflowStrategy Strategy { get; }

        /// <summary>
        /// 队列容量
        /// </summary>
        public int QueueCapacity { get; }

        /// <summary>
        /// 当前队列中待处理的日志数量
        /// </summary>
        public int PendingCount => _logQueue.Count;

        private volatile bool _isRunning = true;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        private volatile int _droppedCount = 0;

        /// <summary>
        /// 已丢弃的日志数量（仅在 DropNew 或 DropOldest 策略下有效）
        /// </summary>
        public int DroppedCount => _droppedCount;

        private readonly BlockingCollection<AsyncLog> _logQueue;

        private readonly Thread _consumerThread;

        /// <summary>
        /// 创建异步日志处理器装饰器
        /// </summary>
        /// <param name="logHandler">要装饰的日志处理器</param>
        /// <param name="queueCapacity">队列容量，默认 1000。建议根据日志频率调整：
        /// <list type="bullet">
        /// <item>低频（&lt; 100/s）：100-500</item>
        /// <item>中频（100-1000/s）：500-2000</item>
        /// <item>高频（&gt; 1000/s）：2000-10000</item>
        /// </list>
        /// </param>
        /// <param name="strategy">队列溢出策略，默认为阻塞</param>
        /// <param name="threadName">后台线程名称，用于调试</param>
        /// <exception cref="ArgumentNullException">logHandler 为 null</exception>
        /// <exception cref="ArgumentOutOfRangeException">queueCapacity 小于 1</exception>
        public static LogHandlerAsyncDecorator CreateDecorator<TLogHandler>(TLogHandler logHandler, int queueCapacity = 1000, OverflowStrategy strategy = OverflowStrategy.Block, string threadName = "AsyncLogWorker")
            where TLogHandler : ILogHandler, ILogHandler<AsyncLog>
        {
            return new LogHandlerAsyncDecorator(logHandler, queueCapacity, strategy, threadName);
        }

        private LogHandlerAsyncDecorator(ILogHandler logHandler, int queueCapacity, OverflowStrategy strategy, string threadName)
        {
            if (queueCapacity < 1) throw new ArgumentOutOfRangeException(nameof(queueCapacity), "队列容量必须至少为 1");
            LogHandler = logHandler ?? throw new ArgumentNullException(nameof(logHandler));

            QueueCapacity = queueCapacity;
            Strategy = strategy;

            // 使用 BlockingCollection 包装 ConcurrentQueue 以支持阻塞操作
            _logQueue = new BlockingCollection<AsyncLog>(new ConcurrentQueue<AsyncLog>(), queueCapacity);

            // 启动后台消费线程
            _consumerThread = new Thread(ProcessQueue)
            {
                IsBackground = true, // 后台线程，不会阻止程序退出
                Name = threadName,
                Priority = ThreadPriority.BelowNormal // 降低优先级，避免影响主逻辑
            };
            _consumerThread.Start();
        }

        /// <summary>
        /// 记录日志（异步）
        /// </summary>
        /// <typeparam name="TLog">日志类型</typeparam>
        /// <param name="log">日志对象</param>
        /// <remarks>
        /// 此方法会立即返回，实际的日志处理在后台线程执行
        /// 如果队列已满，行为取决于 <see cref="Strategy"/>。
        /// </remarks>
        public void Log<TLog>(TLog log) where TLog : struct, ILog
        {
            if (!_isRunning) return; // 已停止，忽略新日志

            // 捕获日志副本，避免外部修改影响异步处理
            var logCopy = log;

            // 创建异步执行的委托
            Action<ILogHandler> logAction = (handler) =>
            {
                try
                {
                    // 优先调用特化版本
                    if (handler is ILogHandler<TLog> typedHandler)
                    {
                        typedHandler.Log(logCopy);
                    }
                    else
                    {
                        handler.Log(logCopy);
                    }
                }
                catch (Exception ex)
                {
                    // 处理器抛出异常，写入标准错误流（避免递归）
                    Console.Error.WriteLine($"[AsyncLogDecorator] LogHandler 异常: {ex.Message}");
                }
            };

            // 根据策略处理队列满的情况
            switch (Strategy)
            {
                case OverflowStrategy.Block:
                    try
                    {
                        _logQueue.Add(new AsyncLog(logAction)); // 阻塞直到队列有空间
                    }
                    catch (InvalidOperationException)
                    {
                        // 队列已完成添加（正在关闭）
                    }
                    break;

                case OverflowStrategy.DropNew:
                    if (!_logQueue.TryAdd(new AsyncLog(logAction)))
                    {
                        Interlocked.Increment(ref _droppedCount);
                    }
                    break;

                case OverflowStrategy.DropOldest:
                    while (!_logQueue.TryAdd(new AsyncLog(logAction)))
                    {
                        // 尝试移除最旧的元素
                        if (_logQueue.TryTake(out _))
                        {
                            Interlocked.Increment(ref _droppedCount);
                        }
                        else
                        {
                            break; // 队列已空或已关闭
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// 后台线程的消费循环
        /// </summary>
        private void ProcessQueue()
        {
            try
            {
                ILogHandler<AsyncLog> asyncLogHandler = LogHandler as ILogHandler<AsyncLog>;
                // 持续从队列中取出并执行日志操作
                foreach (var asyncLog in _logQueue.GetConsumingEnumerable())
                {
                    asyncLogHandler.Log(asyncLog);
                }
            }
            catch (Exception ex)
            {
                // 消费线程异常（理论上不应发生）
                Console.Error.WriteLine($"[AsyncLogDecorator] 消费者线程崩溃: {ex.Message}");
            }
        }

        /// <summary>
        /// 立即刷新所有待处理的日志
        /// </summary>
        /// <param name="timeout">超时时间（毫秒），-1 表示无限等待</param>
        /// <returns>是否在超时前完成刷新</returns>
        /// <remarks>
        /// 调用此方法会阻塞当前线程，直到队列为空或超时。
        /// </remarks>
        public bool Flush(int timeout = 5000)
        {
            if (!_isRunning) return true;

            var startTime = Environment.TickCount;
            while (_logQueue.Count > 0)
            {
                Thread.Sleep(10);
                if (timeout > 0 && Environment.TickCount - startTime > timeout)
                {
                    return false; // 超时
                }
            }
            return true;
        }

        /// <summary>
        /// 释放资源，等待所有日志处理完成
        /// </summary>
        /// <remarks>
        /// <para>调用此方法会：</para>
        /// <list type="number">
        /// <item>停止接受新日志</item>
        /// <item>等待队列中的所有日志处理完成</item>
        /// <item>终止后台线程</item>
        /// </list>
        /// <para>建议在程序退出前调用，确保日志不丢失。</para>
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的内部实现
        /// </summary>
        /// <param name="disposing">是否由 Dispose 方法调用</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isRunning) return; // 已释放

            _isRunning = false;

            if (disposing)
            {
                // 停止接受新日志
                _logQueue.CompleteAdding();

                // 等待消费线程处理完所有日志（最多等待 5 秒）
                if (!_consumerThread.Join(5000))
                {
                    Console.Error.WriteLine("[AsyncLogDecorator] 消费者线程未能在规定时间内完成任务，强制终止");
                }

                // 释放队列资源
                _logQueue.Dispose();

                // 如果被装饰的处理器实现了 IDisposable，也释放它
                if (LogHandler is IDisposable disposableHandler)
                {
                    disposableHandler.Dispose();
                }
            }
        }

        /// <summary>
        /// 析构函数，确保资源释放
        /// </summary>
        ~LogHandlerAsyncDecorator()
        {
            Dispose(false);
        }

        public struct AsyncLog : ILog
        {
            public LogLevel Level => LogLevel.Custom;

            public DateTime Timestamp { get; }

            public Action<ILogHandler> LogAction { get; }

            public AsyncLog(Action<ILogHandler> logAction)
            {
                LogAction = logAction;
                Timestamp = DateTime.Now;
            }

            public string ParseToString()
            {
                return String.Empty;
            }
        }
    }

    /// <summary>
    /// 异步日志处理器装饰器（特化版本）
    /// </summary>
    /// <typeparam name="TLog">日志类型</typeparam>
    /// <remarks>
    /// 功能与 <see cref="LogHandlerAsyncDecorator"/> 相同，但仅处理特定类型的日志
    /// </remarks>
    public class LogHandlerAsyncDecorator<TLog> : ILogHandler<TLog>, IDisposable, IDecorator<ILogHandler<TLog>>
        where TLog : struct, ILog
    {
        /// <summary>
        /// 队列溢出时的处理策略
        /// </summary>
        public enum OverflowStrategy
        {
            /// <summary>
            /// 阻塞调用线程，直到队列有空间（默认）
            /// </summary>
            Block,

            /// <summary>
            /// 丢弃新日志，立即返回
            /// </summary>
            DropNew,

            /// <summary>
            /// 丢弃队列中最旧的日志，插入新日志
            /// </summary>
            DropOldest
        }

        /// <summary>
        /// 被装饰的日志处理器
        /// </summary>
        public ILogHandler<TLog> LogHandler { get; }

        public ILogHandler<TLog> Inner => LogHandler;

        /// <summary>
        /// 队列溢出策略
        /// </summary>
        public OverflowStrategy Strategy { get; }

        /// <summary>
        /// 队列容量
        /// </summary>
        public int QueueCapacity { get; }

        /// <summary>
        /// 当前队列中待处理的日志数量
        /// </summary>
        public int PendingCount => _logQueue.Count;

        private volatile bool _isRunning = true;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        private volatile int _droppedCount = 0;

        /// <summary>
        /// 已丢弃的日志数量（仅在 DropNew 或 DropOldest 策略下有效）
        /// </summary>
        public int DroppedCount => _droppedCount;

        private readonly BlockingCollection<AsyncLog> _logQueue;

        private readonly Thread _consumerThread;

        /// <summary>
        /// 创建异步日志处理器装饰器
        /// </summary>
        /// <param name="logHandler">要装饰的日志处理器</param>
        /// <param name="queueCapacity">队列容量，默认 1000。建议根据日志频率调整：
        /// <list type="bullet">
        /// <item>低频（&lt; 100/s）：100-500</item>
        /// <item>中频（100-1000/s）：500-2000</item>
        /// <item>高频（&gt; 1000/s）：2000-10000</item>
        /// </list>
        /// </param>
        /// <param name="strategy">队列溢出策略，默认为阻塞</param>
        /// <param name="threadName">后台线程名称，用于调试</param>
        /// <exception cref="ArgumentNullException">logHandler 为 null</exception>
        /// <exception cref="ArgumentOutOfRangeException">queueCapacity 小于 1</exception>
        public static LogHandlerAsyncDecorator<TLog> CreateDecorator<TLogHandler>(TLogHandler logHandler, int queueCapacity = 1000, OverflowStrategy strategy = OverflowStrategy.Block, string threadName = "AsyncLogWorker")
            where TLogHandler : ILogHandler<TLog>, ILogHandler<AsyncLog>
        {
            return new LogHandlerAsyncDecorator<TLog>(logHandler, queueCapacity, strategy, threadName);
        }

        private LogHandlerAsyncDecorator(ILogHandler<TLog> logHandler, int queueCapacity, OverflowStrategy strategy, string threadName)
        {
            if (queueCapacity < 1) throw new ArgumentOutOfRangeException(nameof(queueCapacity), "队列容量必须至少为 1");
            LogHandler = logHandler ?? throw new ArgumentNullException(nameof(logHandler));

            QueueCapacity = queueCapacity;
            Strategy = strategy;

            // 使用 BlockingCollection 包装 ConcurrentQueue 以支持阻塞操作
            _logQueue = new BlockingCollection<AsyncLog>(new ConcurrentQueue<AsyncLog>(), queueCapacity);

            // 启动后台消费者线程
            _consumerThread = new Thread(ProcessQueue)
            {
                IsBackground = true, // 后台线程，不会阻止程序退出
                Name = threadName,
                Priority = ThreadPriority.BelowNormal // 降低优先级，避免影响主逻辑
            };
            _consumerThread.Start();
        }

        /// <summary>
        /// 记录日志（异步）
        /// </summary>
        /// <param name="log">日志对象</param>
        /// <remarks>
        /// 此方法会立即返回，实际的日志处理在后台线程执行
        /// 如果队列已满，行为取决于 <see cref="Strategy"/>。
        /// </remarks>
        public void Log(TLog log)
        {
            if (!_isRunning) return; // 已停止，忽略新日志

            // 根据策略处理队列满的情况
            switch (Strategy)
            {
                case OverflowStrategy.Block:
                    try
                    {
                        _logQueue.Add(new AsyncLog(log)); // 阻塞直到队列有空间
                    }
                    catch (InvalidOperationException)
                    {
                        // 队列已完成添加
                    }
                    break;

                case OverflowStrategy.DropNew:
                    if (!_logQueue.TryAdd(new AsyncLog(log)))
                    {
                        Interlocked.Increment(ref _droppedCount);
                    }
                    break;

                case OverflowStrategy.DropOldest:
                    while (!_logQueue.TryAdd(new AsyncLog(log)))
                    {
                        // 尝试移除最旧的元素
                        if (_logQueue.TryTake(out _))
                        {
                            Interlocked.Increment(ref _droppedCount);
                        }
                        else
                        {
                            break; // 队列已空或已关闭
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// 后台线程的消费循环
        /// </summary>
        private void ProcessQueue()
        {
            try
            {
                ILogHandler<AsyncLog> asyncLogHandler = LogHandler as ILogHandler<AsyncLog>;

                // 持续从队列中取出并执行日志操作
                foreach (var log in _logQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        asyncLogHandler.Log(log);
                    }
                    catch (Exception ex)
                    {
                        // 消费线程异常（理论上不应发生）
                        Console.Error.WriteLine($"[AsyncLogDecorator<{typeof(TLog).Name}>] LogHandler 异常: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AsyncLogDecorator<{typeof(TLog).Name}>] 消费者线程已崩溃: {ex.Message}");
            }
        }

        /// <summary>
        /// 立即刷新所有待处理的日志
        /// </summary>
        /// <param name="timeout">超时时间（毫秒），-1 表示无限等待</param>
        /// <returns>是否在超时前完成刷新</returns>
        /// <remarks>
        /// 调用此方法会阻塞当前线程，直到队列为空或超时
        /// </remarks>
        public bool Flush(int timeout = 5000)
        {
            if (!_isRunning) return true;

            var startTime = Environment.TickCount;
            while (_logQueue.Count > 0)
            {
                Thread.Sleep(10);
                if (timeout > 0 && Environment.TickCount - startTime > timeout)
                {
                    return false; // 超时
                }
            }
            return true;
        }

        /// <summary>
        /// 释放资源，等待所有日志处理完成
        /// </summary>
        /// <remarks>
        /// <para>调用此方法会：</para>
        /// <list type="number">
        /// <item>停止接受新日志</item>
        /// <item>等待队列中的所有日志处理完成</item>
        /// <item>终止后台线程</item>
        /// </list>
        /// <para>建议在程序退出前调用，确保日志不丢失</para>
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的内部实现
        /// </summary>
        /// <param name="disposing">是否由 Dispose 方法调用</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isRunning) return; // 已释放

            _isRunning = false;

            if (disposing)
            {
                // 停止接受新日志
                _logQueue.CompleteAdding();

                // 等待消费线程处理完所有日志（最多等待 5 秒）
                if (!_consumerThread.Join(5000))
                {
                    Console.Error.WriteLine($"[AsyncLogDecorator<{typeof(TLog).Name}>] 消费者线程未能在规定时间内完成任务，强制终止");
                }

                // 释放队列资源
                _logQueue.Dispose();

                // 如果被装饰的处理器实现了 IDisposable，也释放它
                if (LogHandler is IDisposable disposableHandler)
                {
                    disposableHandler.Dispose();
                }
            }
        }

        /// <summary>
        /// 析构函数，确保资源释放
        /// </summary>
        ~LogHandlerAsyncDecorator()
        {
            Dispose(false);
        }

        public struct AsyncLog : ILog
        {
            public LogLevel Level => LogLevel.Custom;

            public DateTime Timestamp { get; }

            public TLog InternalLog { get; }

            public AsyncLog(TLog log)
            {
                InternalLog = log;
                Timestamp = DateTime.Now;
            }

            public string ParseToString()
            {
                return String.Empty;
            }
        }
    }
}
