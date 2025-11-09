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

using System;
using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

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

        // 处理速率控制
        private int _messagesPerFrame = 30; // 正常模式：每帧处理 30 个消息（优化后）
        private const int BULK_MODE_MESSAGES_PER_FRAME = 100; // 批量模式：每帧处理 100 个消息（大幅提升）
        private const float BULK_MODE_DURATION = 20f; // 批量模式持续 20 秒（延长以覆盖整个场景加载）

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
        public void EnqueueMessage(Action<NetDataReader> handler, NetPacketReader reader)
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

                // ✅ 优化：单帧时间预算增加到 10ms（批量模式）或 8ms（正常模式）
                // 60fps = 16.67ms/帧，留出 6-8ms 给渲染和其他逻辑
                float timeLimit = _bulkMode ? 0.010f : 0.008f;
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

