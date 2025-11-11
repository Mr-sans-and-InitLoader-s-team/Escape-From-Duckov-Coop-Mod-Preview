// 定义性能监测编译符号
// #define ENABLE_PERFORMANCE_MONITORING

using System.Collections.Concurrent;

namespace EscapeFromDuckovCoopMod.Utils.NetHelper
{
    /// <summary>
    /// 网络消息消费器 - 线程安全的消息接收和处理管理器
    /// 支持主线程处理和后台线程处理两种模式
    /// 使用消息处理器注册模式，解耦消息接收和处理逻辑
    /// </summary>
    public class NetMessageConsumer : MonoBehaviour
    {
        #region 单例模式

        private static NetMessageConsumer _instance;
        private static readonly object _instanceLock = new object();

        public static NetMessageConsumer Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            var gameObject = new GameObject("COOP_MOD_NetMessageConsumer");
                            DontDestroyOnLoad(gameObject);
                            _instance = gameObject.AddComponent<NetMessageConsumer>();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region 消息队列

        /// <summary>
        /// 主线程处理队列 - 在 Unity Update 中处理
        /// 适用于需要访问 Unity API 的消息（如生成 GameObject、修改 Transform 等）
        /// </summary>
        private readonly ConcurrentQueue<ReceivedMessage> _mainThreadQueue = new ConcurrentQueue<ReceivedMessage>();

        /// <summary>
        /// 后台线程处理队列 - 在独立线程中处理
        /// 适用于纯计算逻辑、数据处理等不依赖 Unity API 的消息
        /// </summary>
        private readonly ConcurrentQueue<ReceivedMessage> _backgroundQueue = new ConcurrentQueue<ReceivedMessage>();

        /// <summary>
        /// NetPacketReader 对象池 - 避免频繁创建
        /// </summary>
        private readonly ConcurrentBag<NetDataReader> _readerPool = new ConcurrentBag<NetDataReader>();

        private const int MAX_POOL_SIZE = 32; // 对象池最大容量

        private const int MAX_PROCESS_PER_FRAME = 300; // Unity主线程每帧最多处理300条消息，防止卡顿

        private const int BATCH_SIZE = 50; //后台线程每次处理的消息批量大小

        #endregion

        #region 消息处理器注册

        /// <summary>
        /// 消息处理器委托
        /// </summary>
        /// <param name="peer">发送消息的网络节点</param>
        /// <param name="reader">消息读取器</param>
        /// <param name="deliveryMethod">投递方式</param>
        public delegate void MessageHandler(NetPeer peer, NetDataReader reader, byte channelNumber, DeliveryMethod deliveryMethod);

        /// <summary>
        /// 主线程消息处理器字典 - 按操作码 (OpCode) 注册
        /// </summary>
        private readonly ConcurrentDictionary<Op, MessageHandler> _mainThreadHandlers = new ConcurrentDictionary<Op, MessageHandler>();

        /// <summary>
        /// 后台线程消息处理器字典 - 按操作码注册
        /// </summary>
        private readonly ConcurrentDictionary<Op, MessageHandler> _backgroundHandlers = new ConcurrentDictionary<Op, MessageHandler>();

        /// <summary>
        /// 默认消息处理器 - 当没有找到对应操作码的处理器时调用
        /// </summary>
        private MessageHandler _defaultHandler;

        #endregion

        #region 线程控制

        /// <summary>
        /// 后台处理线程
        /// </summary>
        private Thread _backgroundThread;

        /// <summary>
        /// 线程运行标志
        /// </summary>
        private volatile bool _isRunning;

        /// <summary>
        /// 取消令牌源
        /// </summary>
        private CancellationTokenSource _cancellationTokenSource;

        #endregion

        #region 统计信息

        /// <summary>
        /// 分 OP 码的主线程处理计数
        /// </summary>
        private readonly ConcurrentDictionary<Op, int> _mainThreadProcessCountByOp = new ConcurrentDictionary<Op, int>();

        /// <summary>
        /// 分 OP 码的后台线程处理计数
        /// </summary>
        private readonly ConcurrentDictionary<Op, int> _backgroundProcessCountByOp = new ConcurrentDictionary<Op, int>();

        /// <summary>
        /// 分 OP 码的接收消息数量统计
        /// </summary>
        private readonly ConcurrentDictionary<Op, int> _receivedCountByOp = new ConcurrentDictionary<Op, int>();

        /// <summary>
        /// 处理失败计数
        /// </summary>
        private int _failedProcessCount;

        /// <summary>
        /// 未知消息类型计数
        /// </summary>
        private int _unknownMessageCount;

        #endregion

        #region 消息数据结构

        /// <summary>
        /// 接收到的消息
        /// </summary>
        public struct ReceivedMessage
        {
            /// <summary>发送者网络节点</summary>
            public NetPeer Peer;

            /// <summary>消息数据（已复制）</summary>
            public byte[] Data;

            /// <summary>投递方式</summary>
            public DeliveryMethod DeliveryMethod;

            ///// <summary>接收时间</summary>
            //public float ReceiveTime;

            /// <summary>操作码</summary>
            public Op OpCode;

            /// <summary>优先级</summary>
            public MessagePriority Priority;
        }

        /// <summary>
        /// 消息处理模式
        /// </summary>
        public enum ProcessMode
        {
            /// <summary>在 Unity 主线程处理（可访问 Unity API）</summary>
            MainThread,

            /// <summary>在后台线程处理（不可访问 Unity API，性能更好）</summary>
            Background
        }

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            InitializeWorkerThread();
            RegisterDefaultHandlers();

#if ENABLE_PERFORMANCE_MONITORING
            // 启动状态监控线程
            StartStatusMonitorThread();
            // 启动性能监控看门狗线程
            var watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "PerformanceWatchdog" };
            watchdogThread.Start();
#endif
        }

        private void Update()
        {
            // 处理主线程队列
            ProcessMainThreadQueue();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        #endregion

        #region 初始化和关闭

        /// <summary>
        /// 初始化后台工作线程
        /// </summary>
        private void InitializeWorkerThread()
        {
            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _backgroundThread = new Thread(BackgroundWorker)
            {
                Name = "NetMessageConsumer-BackgroundThread",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.Normal
            };

            _backgroundThread.Start();
            Debug.Log("[NetMessageConsumer] 后台处理线程已启动");
        }

        /// <summary>
        /// 启动状态监控线程
        /// </summary>
        private void StartStatusMonitorThread()
        {
            Task.Run(() =>
            {
                int time = 0;
                while (_isRunning)
                {
                    if (time >= 120000) // 每2分钟输出一次完整信息
                    {
                        time = 0;
                        LogStatistics(true);
                    }

                    Thread.Sleep(10000);
                    time += 10000;
                    LogStatistics(false);
                }
            });
        }

        /// <summary>
        /// 关闭消费器，清理资源
        /// </summary>
        private void Shutdown()
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            // 等待后台线程结束
            if (_backgroundThread != null && _backgroundThread.IsAlive)
            {
                if (!_backgroundThread.Join(TimeSpan.FromSeconds(2)))
                {
                    Debug.LogWarning("[NetMessageConsumer] 后台线程未能在超时内结束");
                }
            }

            _cancellationTokenSource?.Dispose();

            // 清理对象池
            _readerPool.Clear();

            // 输出最终统计
            LogStatistics(true);
            Debug.Log("[NetMessageConsumer] 已关闭");
        }

        #endregion

        #region 对象池管理

        /// <summary>
        /// 从池中获取 NetDataReader
        /// </summary>
        private NetDataReader GetReader(byte[] data)
        {
            NetDataReader reader;

            if (_readerPool.TryTake(out reader))
            {
                reader.SetSource(data);
            }
            else
            {
                reader = new NetDataReader(data);
            }

            return reader;
        }

        /// <summary>
        /// 归还 NetDataReader 到池中
        /// </summary>
        private void ReturnReader(NetDataReader reader)
        {
            if (reader == null) return;

            if (_readerPool.Count < MAX_POOL_SIZE)
            {
                _readerPool.Add(reader);
            }
        }

        #endregion

        #region 消息处理器注册

        /// <summary>
        /// 注册主线程消息处理器
        /// </summary>
        /// <param name="opCode">操作码</param>
        /// <param name="handler">处理器方法</param>
        /// <returns>是否注册成功</returns>
        public bool RegisterMainThreadHandler(Op opCode, MessageHandler handler)
        {
            if (handler == null)
            {
                Debug.LogError($"[NetMessageConsumer] RegisterMainThreadHandler: handler 为空");
                return false;
            }

            var success = _mainThreadHandlers.TryAdd(opCode, handler);

            if (success)
            {
                Debug.Log($"[NetMessageConsumer] 已注册主线程处理器: OpCode={opCode}");
            }
            else
            {
                Debug.LogWarning($"[NetMessageConsumer] 主线程处理器已存在: OpCode={opCode}");
            }

            return success;
        }

        /// <summary>
        /// 注册后台线程消息处理器
        /// ⚠️ 注意：后台处理器不能访问 Unity API
        /// </summary>
        /// <param name="opCode">操作码</param>
        /// <param name="handler">处理器方法</param>
        /// <returns>是否注册成功</returns>
        public bool RegisterBackgroundHandler(Op opCode, MessageHandler handler)
        {
            if (handler == null)
            {
                Debug.LogError($"[NetMessageConsumer] RegisterBackgroundHandler: handler 为空");
                return false;
            }

            var success = _backgroundHandlers.TryAdd(opCode, handler);

            if (success)
            {
                Debug.Log($"[NetMessageConsumer] 已注册后台处理器: OpCode={opCode}");
            }
            else
            {
                Debug.LogWarning($"[NetMessageConsumer] 后台处理器已存在: OpCode={opCode}");
            }

            return success;
        }

        /// <summary>
        /// 注销主线程消息处理器
        /// </summary>
        public bool UnregisterMainThreadHandler(Op opCode)
        {
            var success = _mainThreadHandlers.TryRemove(opCode, out _);

            if (success)
            {
                Debug.Log($"[NetMessageConsumer] 已注销主线程处理器: OpCode={opCode}");
            }

            return success;
        }

        /// <summary>
        /// 注销后台线程消息处理器
        /// </summary>
        public bool UnregisterBackgroundHandler(Op opCode)
        {
            var success = _backgroundHandlers.TryRemove(opCode, out _);

            if (success)
            {
                Debug.Log($"[NetMessageConsumer] 已注销后台处理器: OpCode={opCode}");
            }

            return success;
        }

        /// <summary>
        /// 设置默认消息处理器（处理未注册的消息类型）
        /// </summary>
        public void SetDefaultHandler(MessageHandler handler)
        {
            _defaultHandler = handler;
            Debug.Log("[NetMessageConsumer] 已设置默认消息处理器");
        }

        /// <summary>
        /// 清除所有处理器
        /// </summary>
        public void ClearAllHandlers()
        {
            _mainThreadHandlers.Clear();
            _backgroundHandlers.Clear();
            _defaultHandler = null;
            Debug.Log("[NetMessageConsumer] 已清除所有消息处理器");
        }

        #endregion

        #region 消息接收接口

        /// <summary>
        /// 【核心接口】处理接收到的网络消息
        /// 由 NetService.OnNetworkReceive 调用
        /// </summary>
        /// <param name="peer">发送者</param>
        /// <param name="reader">消息读取器</param>
        /// <param name="channelNumber">通道编号</param>
        /// <param name="deliveryMethod">投递方式</param>
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (peer == null || reader == null || reader.AvailableBytes == 0)
            {
                Debug.LogWarning("[NetMessageConsumer] OnNetworkReceive: 无效的消息");
                return;
            }

            try
            {
                // 读取操作码
                Op opCode = (Op)reader.GetByte();

                // 统计接收数量
                _receivedCountByOp.AddOrUpdate(opCode, 1, (_, count) => count + 1);

                // Debug.Log($"[NetMessageConsumer] 收到消息: OpCode={opCode}, From={peer.EndPoint}, Channel={channelNumber}, DeliveryMethod={deliveryMethod}");

                // 复制剩余数据（避免 reader 被重用）
                byte[] data = reader.GetRemainingBytes();

                // 创建消息对象
                var message = new ReceivedMessage
                {
                    Peer = peer,
                    Data = data,
                    DeliveryMethod = deliveryMethod,
                    OpCode = opCode,
                    Priority = (MessagePriority)channelNumber
                };

                // 根据处理器类型分发到不同队列
                if (_mainThreadHandlers.ContainsKey(opCode))
                {
                    _mainThreadQueue.Enqueue(message);
                }
                else if (_backgroundHandlers.ContainsKey(opCode))
                {
                    _backgroundQueue.Enqueue(message);
                }
                else
                {
                    // 未注册的消息类型，发送到主线程（使用默认处理器）
                    _mainThreadQueue.Enqueue(message);
                    Interlocked.Increment(ref _unknownMessageCount);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetMessageConsumer] OnNetworkReceive 异常: {ex.Message}\n{ex.StackTrace}");
                Interlocked.Increment(ref _failedProcessCount);
            }
        }

        /// <summary>
        /// 手动将消息加入主线程队列
        /// </summary>
        public void EnqueueMainThread(NetPeer peer, NetPacketReader reader, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (peer == null || reader == null || reader.AvailableBytes == 0)
            {
                Debug.LogWarning("[NetMessageConsumer] EnqueueMainThread: 无效的消息");
                return;
            }
            try
            {
                // 读取操作码
                Op opCode = (Op)reader.GetByte();

                // 统计接收数量
                _receivedCountByOp.AddOrUpdate(opCode, 1, (_, count) => count + 1);

                // 复制剩余数据（避免 reader 被重用）
                byte[] data = reader.GetRemainingBytes();

                var message = new ReceivedMessage
                {
                    Peer = peer,
                    Data = data,
                    DeliveryMethod = deliveryMethod,
                    OpCode = opCode,
                    Priority = priority
                };

                _mainThreadQueue.Enqueue(message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetMessageConsumer] EnqueueMainThread 异常: {ex.Message}\n{ex.StackTrace}");
                Interlocked.Increment(ref _failedProcessCount);
            }
        }

        /// <summary>
        /// 手动将消息加入后台队列
        /// </summary>
        public void EnqueueBackground(NetPeer peer, NetPacketReader reader, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (peer == null || reader == null || reader.AvailableBytes == 0)
            {
                Debug.LogWarning("[NetMessageConsumer] EnqueueBackground: 无效的消息");
                return;
            }
            try
            {
                // 读取操作码
                Op opCode = (Op)reader.GetByte();

                // 统计接收数量
                _receivedCountByOp.AddOrUpdate(opCode, 1, (_, count) => count + 1);

                // 复制剩余数据（避免 reader 被重用）
                byte[] data = reader.GetRemainingBytes();

                var message = new ReceivedMessage
                {
                    Peer = peer,
                    Data = data,
                    DeliveryMethod = deliveryMethod,
                    OpCode = opCode,
                    Priority = priority
                };

                _backgroundQueue.Enqueue(message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetMessageConsumer] EnqueueBackground 异常: {ex.Message}\n{ex.StackTrace}");
                Interlocked.Increment(ref _failedProcessCount);
            }
        }

        #endregion

        #region 性能监测

#if ENABLE_PERFORMANCE_MONITORING
        /// <summary>
        /// 分 OP 码的处理总时间（毫秒）
        /// </summary>
        private readonly ConcurrentDictionary<Op, long> _totalProcessTimeByOp = new ConcurrentDictionary<Op, long>();

        /// <summary>
        /// 分 OP 码的最大处理耗时（毫秒）
        /// </summary>
        private readonly ConcurrentDictionary<Op, long> _maxProcessTimeByOp = new ConcurrentDictionary<Op, long>();

        [ThreadStatic] private static System.Diagnostics.Stopwatch sw;
        //[ThreadStatic] private static Op currentOp;
        [ThreadStatic] private static CancellationTokenSource cts;
        private static ConcurrentQueue<(Op, CancellationToken)> timeoutQueue = new ConcurrentQueue<(Op, CancellationToken)>();

        private static void WatchdogLoop()
        {
            while (true)
            {
                if (timeoutQueue.TryDequeue(out var item))
                {
                    var (op, token) = item;
                    int timeoutMs = 0;

                    while (!token.WaitHandle.WaitOne(3000))
                    {
                        timeoutMs += 3000;
                        Debug.LogError($"[NetMessageConsumer] 处理消息 {op} 超时 ({timeoutMs}ms)");
                    }
                }
                else
                {
                    // 队列为空时短暂休眠,避免空转消耗 CPU
                    Thread.Sleep(10);
                }
            }
        }

        public static void StartWatch(Op op)
        {
            sw ??= new System.Diagnostics.Stopwatch();

            cts?.Cancel(); // 取消之前的监控
            cts = new CancellationTokenSource();
            //currentOp = op;

            sw.Restart();
            timeoutQueue.Enqueue((op, cts.Token));
        }

        public static void StopWatch(Op op)
        {
            sw.Stop();
            cts?.Cancel(); // 正常结束时取消监控

            long elapsedMs = sw.ElapsedMilliseconds;

            // 更新总时间
            Instance._totalProcessTimeByOp.AddOrUpdate(op, elapsedMs, (_, total) => total + elapsedMs);

            // 更新最大耗时
            Instance._maxProcessTimeByOp.AddOrUpdate(op, elapsedMs, (_, max) => Math.Max(max, elapsedMs));

            if (elapsedMs > 50)
            {
                Debug.LogWarning($"[NetMessageConsumer] 处理消息 {op} 耗时过长,花费 {elapsedMs} ms");
            }
        }

        /// <summary>
        /// 获取指定 OP 码的平均处理耗时（毫秒）
        /// </summary>
        public double GetAverageProcessTime(Op op)
        {
            if (!_totalProcessTimeByOp.TryGetValue(op, out long totalTime))
                return 0;

            int mainCount = _mainThreadProcessCountByOp.TryGetValue(op, out int mCount) ? mCount : 0;
            int bgCount = _backgroundProcessCountByOp.TryGetValue(op, out int bCount) ? bCount : 0;
            int totalCount = mainCount + bgCount;

            return totalCount > 0 ? (double)totalTime / totalCount : 0;
        }

        /// <summary>
        /// 获取指定 OP 码的最大处理耗时（毫秒）
        /// </summary>
        public long GetMaxProcessTime(Op op)
        {
            return _maxProcessTimeByOp.TryGetValue(op, out long maxTime) ? maxTime : 0;
        }

        /// <summary>
        /// 获取所有 OP 码的性能统计信息
        /// </summary>
        public Dictionary<Op, (int processedCount, long totalTime, long maxTime, double avgTime)> GetPerformanceStatistics()
        {
            var result = new Dictionary<Op, (int, long, long, double)>();

            // 合并所有出现过的 OP 码
            var allOps = new HashSet<Op>();
            allOps.UnionWith(_mainThreadProcessCountByOp.Keys);
            allOps.UnionWith(_backgroundProcessCountByOp.Keys);
            allOps.UnionWith(_totalProcessTimeByOp.Keys);

            foreach (var op in allOps)
            {
                int mainCount = _mainThreadProcessCountByOp.TryGetValue(op, out int mCount) ? mCount : 0;
                int bgCount = _backgroundProcessCountByOp.TryGetValue(op, out int bCount) ? bCount : 0;
                int totalCount = mainCount + bgCount;

                long totalTime = _totalProcessTimeByOp.TryGetValue(op, out long tTime) ? tTime : 0;
                long maxTime = _maxProcessTimeByOp.TryGetValue(op, out long mxTime) ? mxTime : 0;
                double avgTime = totalCount > 0 ? (double)totalTime / totalCount : 0;

                result[op] = (totalCount, totalTime, maxTime, avgTime);
            }

            return result;
        }
#endif

        #endregion

        #region 队列处理

        /// <summary>
        /// 处理主线程队列（在 Unity Update 中调用）
        /// </summary>
        private void ProcessMainThreadQueue()
        {
            int processedCount = 0;

            while (processedCount < MAX_PROCESS_PER_FRAME && _mainThreadQueue.TryDequeue(out var message))
            {
                ProcessMessage(message, ProcessMode.MainThread);
                processedCount++;
            }
        }

        /// <summary>
        /// 后台线程工作循环
        /// </summary>
        private void BackgroundWorker()
        {
            var token = _cancellationTokenSource.Token;

            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    int processedCount = 0;

                    while (processedCount < BATCH_SIZE && _backgroundQueue.TryDequeue(out var message))
                    {
                        ProcessMessage(message, ProcessMode.Background);
                        processedCount++;
                    }

                    // 如果队列为空，休眠避免空转
                    if (processedCount == 0)
                    {
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetMessageConsumer] 后台线程异常: {ex}");
                }
            }

            Debug.Log("[NetMessageConsumer] 后台线程已退出");
        }

        /// <summary>
        /// 处理单条消息
        /// </summary>
        private void ProcessMessage(ReceivedMessage message, ProcessMode mode)
        {
            NetDataReader reader = null;
            bool processAttempted = false;  // ✨ 新增:标记是否尝试过处理

            try
            {
                // 创建 reader
                reader = GetReader(message.Data);

                // 查找处理器
                MessageHandler handler = null;

                if (mode == ProcessMode.MainThread)
                {
                    if (!_mainThreadHandlers.TryGetValue(message.OpCode, out handler))
                    {
                        handler = _defaultHandler;
                    }
                }
                else
                {
                    if (!_backgroundHandlers.TryGetValue(message.OpCode, out handler))
                    {
                        handler = _defaultHandler;
                    }
                }

                // 执行处理器
                if (handler != null)
                {
                    processAttempted = true;  // ✨ 标记已尝试处理

#if ENABLE_PERFORMANCE_MONITORING
                    StartWatch(message.OpCode);
#endif

                    handler.Invoke(message.Peer, reader, (byte)message.Priority, message.DeliveryMethod);

#if ENABLE_PERFORMANCE_MONITORING
                    StopWatch(message.OpCode);
#endif

                    // 更新统计
                    if (mode == ProcessMode.MainThread)
                    {
                        _mainThreadProcessCountByOp.AddOrUpdate(message.OpCode, 1, (_, count) => count + 1);
                    }
                    else
                    {
                        _backgroundProcessCountByOp.AddOrUpdate(message.OpCode, 1, (_, count) => count + 1);
                    }
                }
                else
                {
                    // ✨ 没有找到处理器也算作"已尝试处理"
                    processAttempted = true;

                    // 更新统计(避免 pending 一直累积)
                    if (mode == ProcessMode.MainThread)
                    {
                        _mainThreadProcessCountByOp.AddOrUpdate(message.OpCode, 1, (_, count) => count + 1);
                    }
                    else
                    {
                        _backgroundProcessCountByOp.AddOrUpdate(message.OpCode, 1, (_, count) => count + 1);
                    }

                    Debug.LogWarning($"[NetMessageConsumer] 未找到处理器: OpCode={message.OpCode}, Mode={mode}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetMessageConsumer] 处理消息失败: OpCode={message.OpCode}, Mode={mode}\n{ex.Message}\n{ex.StackTrace}");
                Interlocked.Increment(ref _failedProcessCount);

                // ✨ 新增:即使失败,也要更新处理计数,避免 pending 累积
                if (processAttempted)
                {
                    if (mode == ProcessMode.MainThread)
                    {
                        _mainThreadProcessCountByOp.AddOrUpdate(message.OpCode, 1, (_, count) => count + 1);
                    }
                    else
                    {
                        _backgroundProcessCountByOp.AddOrUpdate(message.OpCode, 1, (_, count) => count + 1);
                    }
                }
            }
            finally
            {
                // 归还 reader
                if (reader != null)
                {
                    ReturnReader(reader);
                }
            }
        }

        #endregion

        #region 默认处理器注册

        /// <summary>
        /// 注册项目中的默认消息处理器
        /// </summary>
        private void RegisterDefaultHandlers()
        {
            // 设置默认处理器
            SetDefaultHandler((peer, reader, channelNumber, deliveryMethod) =>
            {
                Debug.LogWarning($"[NetMessageConsumer] 收到未处理的消息，来自: {peer.EndPoint}");
            });

            Debug.Log("[NetMessageConsumer] 默认处理器已注册");
        }

        #endregion

        #region 统计和调试

        /// <summary>
        /// 获取当前队列大小
        /// </summary>
        public (int mainThread, int background) GetQueueSize()
        {
            return (_mainThreadQueue.Count, _backgroundQueue.Count);
        }

        /// <summary>
        /// 获取处理统计信息
        /// </summary>
        public (int mainThread, int background, int failed, int unknown) GetStatistics()
        {
            int mainThreadTotal = _mainThreadProcessCountByOp.Values.Sum();
            int backgroundTotal = _backgroundProcessCountByOp.Values.Sum();
            return (mainThreadTotal, backgroundTotal, _failedProcessCount, _unknownMessageCount);
        }

        /// <summary>
        /// 获取指定 OP 码的未处理消息数量
        /// </summary>
        public int GetPendingMessageCount(Op op)
        {
            int received = _receivedCountByOp.TryGetValue(op, out int r) ? r : 0;
            int mainProcessed = _mainThreadProcessCountByOp.TryGetValue(op, out int m) ? m : 0;
            int bgProcessed = _backgroundProcessCountByOp.TryGetValue(op, out int b) ? b : 0;

            return Math.Max(0, received - mainProcessed - bgProcessed);
        }

        /// <summary>
        /// 获取所有 OP 码的接收和处理统计
        /// </summary>
        public Dictionary<Op, (int received, int mainProcessed, int bgProcessed, int pending)> GetMessageStatisticsByOp()
        {
            var result = new Dictionary<Op, (int, int, int, int)>();

            var allOps = new HashSet<Op>();
            allOps.UnionWith(_receivedCountByOp.Keys);
            allOps.UnionWith(_mainThreadProcessCountByOp.Keys);
            allOps.UnionWith(_backgroundProcessCountByOp.Keys);

            foreach (var op in allOps)
            {
                int received = _receivedCountByOp.TryGetValue(op, out int r) ? r : 0;
                int mainProcessed = _mainThreadProcessCountByOp.TryGetValue(op, out int m) ? m : 0;
                int bgProcessed = _backgroundProcessCountByOp.TryGetValue(op, out int b) ? b : 0;
                int pending = Math.Max(0, received - mainProcessed - bgProcessed);

                result[op] = (received, mainProcessed, bgProcessed, pending);
            }

            return result;
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            _mainThreadProcessCountByOp.Clear();
            _backgroundProcessCountByOp.Clear();
            _receivedCountByOp.Clear();
            _failedProcessCount = 0;
            _unknownMessageCount = 0;

#if ENABLE_PERFORMANCE_MONITORING
            _totalProcessTimeByOp.Clear();
            _maxProcessTimeByOp.Clear();
#endif
        }

        /// <summary>
        /// 清空所有队列（调试用）
        /// </summary>
        public void ClearQueues()
        {
            while (_mainThreadQueue.TryDequeue(out _)) { }
            while (_backgroundQueue.TryDequeue(out _)) { }
            Debug.Log("[NetMessageConsumer] 所有队列已清空");
        }

        /// <summary>
        /// 获取已注册的处理器信息
        /// </summary>
        public (int mainThreadHandlers, int backgroundHandlers) GetHandlerCount()
        {
            return (_mainThreadHandlers.Count, _backgroundHandlers.Count);
        }

        /// <summary>
        /// 输出统计和性能信息到日志
        /// </summary>
        private void LogStatistics(bool completeInformation)
        {
            var (mainThreadCount, backgroundThreadCount, failedCount, unknownCount) = GetStatistics();
            var (mainThreadQueueCount, backgroundQueueCount) = GetQueueSize();

            Debug.LogWarning($"[NetMessageConsumer] ========== 状态统计 ==========");
            Debug.LogWarning($"[NetMessageConsumer] 处理统计 - 主线程: {mainThreadCount}, 后台: {backgroundThreadCount}, 失败: {failedCount}, 未知: {unknownCount}");
            Debug.LogWarning($"[NetMessageConsumer] 队列大小 - 主线程: {mainThreadQueueCount}, 后台: {backgroundQueueCount}");

            // 输出分 OP 码的消息统计
            var messageStats = GetMessageStatisticsByOp();
            if (messageStats.Count > 0)
            {
                Debug.LogWarning($"[NetMessageConsumer] ========== 消息统计 (按 OP 码) ==========");
                if (completeInformation)
                {
                    foreach (var kvp in messageStats.OrderByDescending(x => x.Value.received))
                    {
                        var (received, mainProcessed, bgProcessed, pending) = kvp.Value;
                        Debug.LogWarning($"[NetMessageConsumer] {kvp.Key}: 接收={received}, 主线程处理={mainProcessed}, 后台处理={bgProcessed}, 待处理={pending}");
                    }
                }
                else
                {
                    foreach (var kvp in messageStats.Where(x => x.Value.pending > 0).OrderByDescending(x => x.Value.pending))
                    {
                        var (received, mainProcessed, bgProcessed, pending) = kvp.Value;
                        Debug.LogWarning($"[NetMessageConsumer] {kvp.Key}: 接收={received}, 主线程处理={mainProcessed}, 后台处理={bgProcessed}, 待处理={pending}");
                    }
                }
            }

#if ENABLE_PERFORMANCE_MONITORING
            if (completeInformation)
            {
                // 输出性能统计
                var perfStats = GetPerformanceStatistics();
                if (perfStats.Count > 0)
                {
                    Debug.LogWarning($"[NetMessageConsumer] ========== 性能统计 (按 OP 码) ==========");
                    foreach (var kvp in perfStats.OrderByDescending(x => x.Value.avgTime))
                    {
                        var (processedCount, totalTime, maxTime, avgTime) = kvp.Value;
                        Debug.LogWarning($"[NetMessageConsumer] {kvp.Key}: 处理次数={processedCount}, 总耗时={totalTime}ms, 最大耗时={maxTime}ms, 平均耗时={avgTime:F2}ms");
                    }
                }
            }
#endif
            Debug.LogWarning($"[NetMessageConsumer] =====================================");
        }

        #endregion
    }
}