// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025 Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
// YOU MUST NOT use this software for commercial purposes.
// YOU MUST NOT use this software to run a headless game server.
// YOU MUST include a conspicuous notice of attribution to
// Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.

namespace EscapeFromDuckovCoopMod.Utils
{
    /// <summary>
    /// ✅ 异步消息队列 - 将网络消息处理从接收线程移到主线程，分批处理避免卡顿
    /// 
    /// 问题原因：
    /// - OnNetworkReceive 在网络线程中同步执行，大量消息会阻塞主线程
    /// - 农场镇等大地图有数百个战利品箱，场景加载时会发送大量 LOOT_STATE 消息
    /// - 客户端逐个处理导致严重帧率下降
    /// 
    /// 解决方案：
    /// - 将消息缓存到队列，在 Update 中分批处理
    /// - 每帧处理数量限制（默认 10 个），防止帧率波动
    /// - 场景加载期间启用批量模式（每帧处理 50 个），加速同步
    /// </summary>
    public class AsyncMessageQueue : MonoBehaviour
    {
        public static AsyncMessageQueue Instance { get; private set; }

        // 消息队列
        private readonly Queue<QueuedMessage> _messageQueue = new Queue<QueuedMessage>();
        private readonly object _queueLock = new object();

        // 处理速率控制（✅ 进一步优化：配合协程异步处理）
        private int _messagesPerFrame = 50; // 正常模式：每帧处理 50 个消息
        private const int BULK_MODE_MESSAGES_PER_FRAME = 200; // 批量模式：每帧处理 200 个消息（配合协程异步，大幅提升）
        private const float BULK_MODE_DURATION = 30f; // 批量模式持续 30 秒（覆盖整个场景加载和初期同步）

        private bool _bulkMode = false; // ✅ 默认禁用，在 Op.SCENE_BEGIN_LOAD 时启用
        private float _bulkModeEndTime = 0f;

        // 性能统计
        private int _totalProcessed = 0;
        private int _totalQueued = 0;
        private int _currentQueueSize = 0;
        private float _lastStatsLogTime = 0f;
        private const float STATS_LOG_INTERVAL = 5f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Debug.Log("[AsyncQueue] 初始化完成，等待场景加载时启用批量模式");
        }

        /// <summary>
        /// 将消息加入队列（接受 NetPacketReader）
        /// </summary>
        public void EnqueueMessage(Action<NetDataReader> handler, NetDataReader reader)
        {
            if (handler == null || reader == null) return;

            // 复制 reader 数据，因为原始 reader 会被回收
            var bytes = new byte[reader.AvailableBytes];
            reader.GetBytes(bytes, reader.AvailableBytes);

            var message = new QueuedMessage
            {
                Handler = handler,
                Data = bytes,
                EnqueueTime = Time.realtimeSinceStartup
            };

            lock (_queueLock)
            {
                _messageQueue.Enqueue(message);
                _totalQueued++;
                _currentQueueSize = _messageQueue.Count;
            }
        }

        /// <summary>
        /// 启用批量处理模式（场景加载时调用）
        /// </summary>
        public void EnableBulkMode()
        {
            _bulkMode = true;
            _bulkModeEndTime = Time.realtimeSinceStartup + BULK_MODE_DURATION;
            _messagesPerFrame = BULK_MODE_MESSAGES_PER_FRAME;
            Debug.Log($"[AsyncQueue] 启用批量处理模式，每帧处理 {_messagesPerFrame} 个消息，持续 {BULK_MODE_DURATION} 秒");
        }

        /// <summary>
        /// 禁用批量处理模式
        /// </summary>
        public void DisableBulkMode()
        {
            _bulkMode = false;
            _messagesPerFrame = 30;
            Debug.Log("[AsyncQueue] 切换回正常处理模式，每帧处理 30 个消息");
        }

        private void Update()
        {
            // 检查批量模式是否超时
            if (_bulkMode && Time.realtimeSinceStartup >= _bulkModeEndTime)
            {
                DisableBulkMode();
            }

            // 处理消息队列
            ProcessMessages();

            // 定期输出性能统计
            if (Time.realtimeSinceStartup - _lastStatsLogTime >= STATS_LOG_INTERVAL)
            {
                LogStats();
                _lastStatsLogTime = Time.realtimeSinceStartup;
            }
        }

        private void ProcessMessages()
        {
            int processed = 0;
            var startTime = Time.realtimeSinceStartup;

            while (processed < _messagesPerFrame)
            {
                QueuedMessage message;
                lock (_queueLock)
                {
                    if (_messageQueue.Count == 0) break;
                    message = _messageQueue.Dequeue();
                    _currentQueueSize = _messageQueue.Count;
                }

                try
                {
                    // 创建临时 reader 并执行处理逻辑
                    var tempReader = new NetDataReader(message.Data);
                    message.Handler(tempReader);
                    _totalProcessed++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AsyncQueue] 处理消息失败: {ex}");
                }

                processed++;

                // ✅ 优化：配合协程异步处理，时间预算提高到 15ms（批量模式）或 10ms（正常模式）
                // 启动协程的开销很小，可以处理更多消息
                // 60fps = 16.67ms/帧，批量模式留 1.67ms 给渲染，正常模式留 6.67ms
                float timeLimit = _bulkMode ? 0.015f : 0.010f;
                if (Time.realtimeSinceStartup - startTime > timeLimit)
                {
                    break;
                }
            }
        }

        private void LogStats()
        {
            if (_totalQueued > 0)
            {
                Debug.Log($"[AsyncQueue] 统计 - 队列大小: {_currentQueueSize}, 已处理: {_totalProcessed}, 已入队: {_totalQueued}, 模式: {(_bulkMode ? "批量" : "正常")}");
            }
        }

        /// <summary>
        /// ✅ 清空消息队列（场景切换时调用，避免处理旧场景消息）
        /// </summary>
        public void ClearQueue()
        {
            lock (_queueLock)
            {
                int count = _messageQueue.Count;
                _messageQueue.Clear();
                _currentQueueSize = 0;

                if (count > 0)
                {
                    Debug.Log($"[AsyncQueue] 清空队列，丢弃 {count} 条消息（场景切换）");
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 队列中的消息
        /// </summary>
        private struct QueuedMessage
        {
            public Action<NetDataReader> Handler;
            public byte[] Data;
            public float EnqueueTime;
        }
    }
}

