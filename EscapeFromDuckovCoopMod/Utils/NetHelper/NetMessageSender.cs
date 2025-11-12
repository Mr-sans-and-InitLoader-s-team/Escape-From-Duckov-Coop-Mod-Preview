using System.Collections.Concurrent;

namespace EscapeFromDuckovCoopMod.Utils.NetHelper
{
    /// <summary>
    /// 网络消息发送器 - 线程安全的消息发送管理器
    /// 支持主线程发送和异步后台发送两种模式
    /// </summary>
    public class NetMessageSender : MonoBehaviour
    {
        #region 单例模式

        private static NetMessageSender _instance;
        private static readonly object _instanceLock = new object();

        public static NetMessageSender Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            var gameObject = new GameObject("COOP_MOD_NetMessageSender");
                            DontDestroyOnLoad(gameObject);
                            _instance = gameObject.AddComponent<NetMessageSender>();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region 队列和池化

        /// <summary>
        /// 主线程发送队列 - 在 Unity Update 中处理
        /// </summary>
        private readonly ConcurrentQueue<QueuedMessage> _mainThreadQueue = new ConcurrentQueue<QueuedMessage>();

        /// <summary>
        /// 后台线程发送队列 - 在独立线程中处理
        /// </summary>
        private readonly ConcurrentQueue<QueuedMessage> _backgroundQueue = new ConcurrentQueue<QueuedMessage>();

        private const int MAX_SEND_PER_FRAME = 100; // Unity主线程每帧最多发送的消息数，防止卡顿

        private const int BATCH_SIZE = 50; // 后台线程每次发送的消息批量大小

        #endregion

        #region 线程控制

        /// <summary>
        /// 后台发送线程
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
        /// 发送统计 - 主线程发送计数
        /// </summary>
        private int _mainThreadSendCount;

        /// <summary>
        /// 发送统计 - 后台线程发送计数
        /// </summary>
        private int _backgroundSendCount;

        /// <summary>
        /// 发送统计 - 失败计数
        /// </summary>
        private int _failedSendCount;

        #endregion

        #region 消息数据结构

        /// <summary>
        /// 队列中的消息
        /// </summary>
        public struct QueuedMessage
        {
            /// <summary>目标网络节点</summary>
            public NetPeer Peer;

            /// <summary>序列化后的数据</summary>
            public byte[] Data;

            /// <summary>投递方式（可靠/不可靠等）</summary>
            public DeliveryMethod DeliveryMethod;

            /// <summary>入队时间（用于超时检测）</summary>
            public float EnqueueTime;

            /// <summary>消息优先级</summary>
            public MessagePriority Priority;
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
                Name = "NetMessageSender-BackgroundThread",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.Normal
            };

            _backgroundThread.Start();
            Debug.Log("[NetMessageSender] 后台发送线程已启动");
        }

        /// <summary>
        /// 关闭发送器，清理资源
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
                    Debug.LogWarning("[NetMessageSender] 后台线程未能在超时内结束");
                }
            }

            _cancellationTokenSource?.Dispose();

            Debug.Log($"[NetMessageSender] 已关闭 - 主线程发送: {_mainThreadSendCount}, 后台发送: {_backgroundSendCount}, 失败: {_failedSendCount}");
        }

        #endregion

        #region 发送接口 - 主线程

        /// <summary>
        /// 【主线程】立即发送消息（在当前帧发送）
        /// ⚠️ 注意此方法的调用线程
        /// </summary>
        public void SendImmediate(NetPeer peer, NetDataWriter writer, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (peer == null || writer == null)
            {
                Debug.LogError("[NetMessageSender] SendImmediate: peer 或 writer 为空");
                Interlocked.Increment(ref _failedSendCount);
                return;
            }

            try
            {
                peer.Send(writer, (byte)priority, deliveryMethod);
                Interlocked.Increment(ref _mainThreadSendCount);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetMessageSender] SendImmediate 失败: {ex.Message}");
                Interlocked.Increment(ref _failedSendCount);
            }
        }

        /// <summary>
        /// 【主线程】将消息加入主线程队列（在下一帧 Update 中发送）
        /// 适用于需要在 Unity 主线程发送但不要求立即发送的场景
        /// </summary>
        public void EnqueueMainThread(NetPeer peer, NetDataWriter writer, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (peer == null || writer == null)
            {
                Debug.LogError("[NetMessageSender] EnqueueMainThread: peer 或 writer 为空");
                return;
            }

            var data = writer.CopyData(); // 复制数据，避免 writer 被重用
            var message = new QueuedMessage
            {
                Peer = peer,
                Data = data,
                DeliveryMethod = deliveryMethod,
                EnqueueTime = Time.time,
                Priority = priority,
            };

            _mainThreadQueue.Enqueue(message);
        }

        #endregion

        #region 发送接口 - 后台线程

        /// <summary>
        /// 【后台线程】将消息加入后台队列（异步发送）
        /// 线程安全，可在任意线程调用
        /// ⚠️ 注意：LiteNetLib 的 Send 方法本身是线程安全的
        /// </summary>
        public void EnqueueBackground(NetPeer peer, NetDataWriter writer, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (peer == null || writer == null)
            {
                Debug.LogError("[NetMessageSender] EnqueueBackground: peer 或 writer 为空");
                return;
            }

            var data = writer.CopyData();
            var message = new QueuedMessage
            {
                Peer = peer,
                Data = data,
                DeliveryMethod = deliveryMethod,
                EnqueueTime = Time.realtimeSinceStartup, // 后台线程使用 realtimeSinceStartup
                Priority = priority,
            };

            _backgroundQueue.Enqueue(message);
        }

        /// <summary>
        /// 【后台线程】异步发送消息（使用 Task）
        /// 适用于不需要排队的高优先级消息
        /// </summary>
        public Task SendAsync(NetPeer peer, NetDataWriter writer, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (peer == null || writer == null)
            {
                Debug.LogError("[NetMessageSender] SendAsync: peer 或 writer 为空");
                Interlocked.Increment(ref _failedSendCount);
                return Task.CompletedTask;
            }

            var data = writer.CopyData();

            return Task.Run(() =>
            {
                try
                {
                    peer.Send(data, (byte)priority, deliveryMethod);
                    Interlocked.Increment(ref _backgroundSendCount);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetMessageSender] SendAsync 失败: {ex.Message}");
                    Interlocked.Increment(ref _failedSendCount);
                }
            }, _cancellationTokenSource.Token);
        }

        #endregion

        #region 广播接口

        /// <summary>
        /// 【主线程】向所有连接的客户端广播消息
        /// </summary>
        public void BroadcastMainThread(NetDataWriter writer, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (NetService.Instance == null || !NetService.Instance.IsServer)
            {
                Debug.LogWarning("[NetMessageSender] BroadcastMainThread: 仅服务端可以广播");
                return;
            }

            foreach (var peer in NetService.Instance.playerStatuses.Keys)
            {
                if (peer != null && peer.ConnectionState == ConnectionState.Connected)
                {
                    EnqueueMainThread(peer, writer, priority, deliveryMethod);
                }
            }
        }

        /// <summary>
        /// 【后台线程】向所有连接的客户端广播消息
        /// </summary>
        public void BroadcastBackground(NetDataWriter writer, MessagePriority priority, DeliveryMethod deliveryMethod)
        {
            if (NetService.Instance == null || !NetService.Instance.IsServer)
            {
                Debug.LogWarning("[NetMessageSender] BroadcastBackground: 仅服务端可以广播");
                return;
            }

            foreach (var peer in NetService.Instance.playerStatuses.Keys)
            {
                if (peer != null && peer.ConnectionState == ConnectionState.Connected)
                {
                    EnqueueBackground(peer, writer, priority, deliveryMethod);
                }
            }
        }

        #endregion

        #region 队列处理

        /// <summary>
        /// 处理主线程发送队列（在 Unity Update 中调用）
        /// </summary>
        private void ProcessMainThreadQueue()
        {
            int processedCount = 0;


            while (processedCount < MAX_SEND_PER_FRAME && _mainThreadQueue.TryDequeue(out var message))
            {
                try
                {
                    if (message.Peer != null && message.Peer.ConnectionState == ConnectionState.Connected)
                    {
                        message.Peer.Send(message.Data, (byte)message.Priority, message.DeliveryMethod);
                        Interlocked.Increment(ref _mainThreadSendCount);
                    }
                    else
                    {
                        Debug.LogWarning($"[NetMessageSender] 跳过无效连接: {message.Peer?.EndPoint}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetMessageSender] 主线程发送失败: {ex.Message}");
                    Interlocked.Increment(ref _failedSendCount);
                }

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
                    // 批量处理消息
                    int processedCount = 0;

                    while (processedCount < BATCH_SIZE && _backgroundQueue.TryDequeue(out var message))
                    {
                        try
                        {
                            if (message.Peer != null && message.Peer.ConnectionState == ConnectionState.Connected)
                            {
                                message.Peer.Send(message.Data, (byte)message.Priority, message.DeliveryMethod);
                                Interlocked.Increment(ref _backgroundSendCount);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[NetMessageSender] 后台发送失败: {ex.Message}");
                            Interlocked.Increment(ref _failedSendCount);
                        }

                        processedCount++;
                    }

                    // 如果队列为空，休眠一段时间避免空转
                    if (processedCount == 0)
                    {
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetMessageSender] 后台线程异常: {ex}");
                }
            }

            Debug.Log("[NetMessageSender] 后台线程已退出");
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
        /// 获取发送统计信息
        /// </summary>
        public (int mainThread, int background, int failed) GetStatistics()
        {
            return (_mainThreadSendCount, _backgroundSendCount, _failedSendCount);
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            _mainThreadSendCount = 0;
            _backgroundSendCount = 0;
            _failedSendCount = 0;
        }

        /// <summary>
        /// 清空所有队列（调试用）
        /// </summary>
        public void ClearQueues()
        {
            while (_mainThreadQueue.TryDequeue(out _)) { }
            while (_backgroundQueue.TryDequeue(out _)) { }
            Debug.Log("[NetMessageSender] 所有队列已清空");
        }

        #endregion
    }
}
