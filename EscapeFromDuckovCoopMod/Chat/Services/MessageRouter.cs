using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using EscapeFromDuckovCoopMod.Chat.Network;

namespace EscapeFromDuckovCoopMod.Chat.Services
{
    /// <summary>
    /// 消息路由系统
    /// 负责消息的路由、分发、优先级管理和确认机制
    /// </summary>
    public class MessageRouter : MonoBehaviour
    {
        #region 字段和属性

        /// <summary>
        /// 网络管理器
        /// </summary>
        private NetworkManager _networkManager;

        /// <summary>
        /// 消息队列（按优先级排序）
        /// </summary>
        private readonly PriorityQueue<RoutedMessage> _messageQueue = new PriorityQueue<RoutedMessage>();

        /// <summary>
        /// 等待确认的消息
        /// </summary>
        private readonly ConcurrentDictionary<string, PendingMessage> _pendingMessages = new ConcurrentDictionary<string, PendingMessage>();

        /// <summary>
        /// 消息处理器映射
        /// </summary>
        private readonly Dictionary<MessageType, List<IMessageHandler>> _messageHandlers = new Dictionary<MessageType, List<IMessageHandler>>();

        /// <summary>
        /// 消息统计信息
        /// </summary>
        private readonly MessageStatistics _statistics = new MessageStatistics();

        /// <summary>
        /// 是否正在处理消息
        /// </summary>
        private bool _isProcessingMessages;

        /// <summary>
        /// 消息处理定时器
        /// </summary>
        private float _processingTimer;

        /// <summary>
        /// 消息处理间隔（秒）
        /// </summary>
        private const float PROCESSING_INTERVAL = 0.1f;

        /// <summary>
        /// 消息确认超时时间（秒）
        /// </summary>
        private const float ACK_TIMEOUT_SECONDS = 10.0f;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        private const int MAX_RETRY_COUNT = 3;

        #endregion

        #region 事件

        /// <summary>
        /// 消息路由事件
        /// </summary>
        public event Action<RoutedMessage> OnMessageRouted;

        /// <summary>
        /// 消息发送成功事件
        /// </summary>
        public event Action<string, string> OnMessageSent;

        /// <summary>
        /// 消息发送失败事件
        /// </summary>
        public event Action<string, string> OnMessageSendFailed;

        /// <summary>
        /// 消息确认事件
        /// </summary>
        public event Action<string> OnMessageAcknowledged;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化消息路由器
        /// </summary>
        /// <param name="networkManager">网络管理器</param>
        public void Initialize(NetworkManager networkManager)
        {
            try
            {
                _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));

                // 订阅网络事件
                _networkManager.OnMessageReceived += HandleIncomingMessage;

                // 初始化消息处理器
                InitializeMessageHandlers();

                LogInfo("消息路由器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化消息路由器时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化消息处理器
        /// </summary>
        private void InitializeMessageHandlers()
        {
            // 为每种消息类型初始化处理器列表
            foreach (MessageType messageType in Enum.GetValues(typeof(MessageType)))
            {
                _messageHandlers[messageType] = new List<IMessageHandler>();
            }
        }

        #endregion

        #region 消息路由和分发

        /// <summary>
        /// 路由消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="targetId">目标客机ID，null表示广播</param>
        /// <param name="priority">消息优先级</param>
        /// <param name="requireAck">是否需要确认</param>
        /// <returns>消息路由ID</returns>
        public string RouteMessage(ChatMessage message, string targetId = null, MessagePriority priority = MessagePriority.Normal, bool requireAck = false)
        {
            try
            {
                if (message == null || !message.IsValid())
                {
                    LogWarning("无效的消息，跳过路由");
                    return null;
                }

                // 创建路由消息
                var routedMessage = new RoutedMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Message = message,
                    TargetId = targetId,
                    Priority = priority,
                    RequireAck = requireAck,
                    CreatedTime = DateTime.UtcNow,
                    RetryCount = 0
                };

                // 添加到消息队列
                _messageQueue.Enqueue(routedMessage);

                // 更新统计信息
                _statistics.TotalMessagesRouted++;

                LogDebug($"消息已加入路由队列: {routedMessage.Id}, 优先级: {priority}");

                // 触发消息路由事件
                OnMessageRouted?.Invoke(routedMessage);

                return routedMessage.Id;
            }
            catch (Exception ex)
            {
                LogError($"路由消息时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 广播消息给所有客机
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="priority">消息优先级</param>
        /// <param name="requireAck">是否需要确认</param>
        /// <returns>消息路由ID</returns>
        public string BroadcastMessage(ChatMessage message, MessagePriority priority = MessagePriority.Normal, bool requireAck = false)
        {
            return RouteMessage(message, null, priority, requireAck);
        }

        /// <summary>
        /// 发送单播消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="targetId">目标客机ID</param>
        /// <param name="priority">消息优先级</param>
        /// <param name="requireAck">是否需要确认</param>
        /// <returns>消息路由ID</returns>
        public string SendUnicastMessage(ChatMessage message, string targetId, MessagePriority priority = MessagePriority.Normal, bool requireAck = false)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                LogWarning("单播消息的目标ID不能为空");
                return null;
            }

            return RouteMessage(message, targetId, priority, requireAck);
        }

        #endregion

        #region 消息处理

        private void Update()
        {
            if (!_isProcessingMessages)
            {
                _processingTimer += Time.deltaTime;

                if (_processingTimer >= PROCESSING_INTERVAL)
                {
                    _processingTimer = 0f;
                    ProcessMessageQueue();
                }
            }

            // 检查待确认消息超时
            CheckPendingMessageTimeouts();
        }

        /// <summary>
        /// 处理消息队列
        /// </summary>
        private async void ProcessMessageQueue()
        {
            if (_isProcessingMessages || _messageQueue.Count == 0)
                return;

            try
            {
                _isProcessingMessages = true;

                // 处理队列中的消息（按优先级）
                while (_messageQueue.Count > 0)
                {
                    var routedMessage = _messageQueue.Dequeue();
                    await ProcessRoutedMessage(routedMessage);
                }
            }
            catch (Exception ex)
            {
                LogError($"处理消息队列时发生异常: {ex.Message}");
            }
            finally
            {
                _isProcessingMessages = false;
            }
        }

        /// <summary>
        /// 处理路由消息
        /// </summary>
        /// <param name="routedMessage">路由消息</param>
        private async Task ProcessRoutedMessage(RoutedMessage routedMessage)
        {
            try
            {
                // 序列化消息
                var messageData = System.Text.Encoding.UTF8.GetBytes(routedMessage.Message.ToJson());

                // 发送消息
                bool success;
                if (string.IsNullOrEmpty(routedMessage.TargetId))
                {
                    // 广播消息
                    success = await _networkManager.BroadcastMessage(messageData);
                }
                else
                {
                    // 单播消息
                    success = await _networkManager.SendMessage(messageData, routedMessage.TargetId);
                }

                if (success)
                {
                    // 发送成功
                    _statistics.TotalMessagesSent++;

                    if (routedMessage.RequireAck)
                    {
                        // 添加到待确认列表
                        var pendingMessage = new PendingMessage
                        {
                            RoutedMessage = routedMessage,
                            SentTime = DateTime.UtcNow,
                            TimeoutTime = DateTime.UtcNow.AddSeconds(ACK_TIMEOUT_SECONDS)
                        };

                        _pendingMessages[routedMessage.Id] = pendingMessage;
                    }

                    // 触发消息发送成功事件
                    OnMessageSent?.Invoke(routedMessage.Id, routedMessage.TargetId);

                    LogDebug($"消息发送成功: {routedMessage.Id}");
                }
                else
                {
                    // 发送失败，尝试重试
                    await HandleMessageSendFailure(routedMessage);
                }
            }
            catch (Exception ex)
            {
                LogError($"处理路由消息时发生异常: {ex.Message}");
                await HandleMessageSendFailure(routedMessage);
            }
        }

        /// <summary>
        /// 处理消息发送失败
        /// </summary>
        /// <param name="routedMessage">路由消息</param>
        private async Task HandleMessageSendFailure(RoutedMessage routedMessage)
        {
            try
            {
                routedMessage.RetryCount++;
                _statistics.TotalMessagesFailed++;

                if (routedMessage.RetryCount < MAX_RETRY_COUNT)
                {
                    // 重新加入队列进行重试
                    routedMessage.Priority = MessagePriority.High; // 提高重试消息的优先级
                    _messageQueue.Enqueue(routedMessage);

                    LogWarning($"消息发送失败，将重试: {routedMessage.Id}, 重试次数: {routedMessage.RetryCount}");
                }
                else
                {
                    // 超过最大重试次数，放弃发送
                    LogError($"消息发送失败，已达到最大重试次数: {routedMessage.Id}");

                    // 触发消息发送失败事件
                    OnMessageSendFailed?.Invoke(routedMessage.Id, routedMessage.TargetId);
                }
            }
            catch (Exception ex)
            {
                LogError($"处理消息发送失败时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 消息确认机制

        /// <summary>
        /// 处理消息确认
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="senderId">发送者ID</param>
        public void HandleMessageAcknowledgment(string messageId, string senderId)
        {
            try
            {
                if (_pendingMessages.TryRemove(messageId, out var pendingMessage))
                {
                    _statistics.TotalMessagesAcknowledged++;

                    // 触发消息确认事件
                    OnMessageAcknowledged?.Invoke(messageId);

                    LogDebug($"收到消息确认: {messageId}, 发送者: {senderId}");
                }
                else
                {
                    LogWarning($"收到未知消息的确认: {messageId}");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理消息确认时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查待确认消息超时
        /// </summary>
        private void CheckPendingMessageTimeouts()
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var timeoutMessages = new List<string>();

                foreach (var kvp in _pendingMessages)
                {
                    if (currentTime > kvp.Value.TimeoutTime)
                    {
                        timeoutMessages.Add(kvp.Key);
                    }
                }

                // 处理超时的消息
                foreach (var messageId in timeoutMessages)
                {
                    if (_pendingMessages.TryRemove(messageId, out var pendingMessage))
                    {
                        LogWarning($"消息确认超时: {messageId}");

                        // 尝试重新发送
                        _ = HandleMessageSendFailure(pendingMessage.RoutedMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"检查待确认消息超时时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 消息处理器管理

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        /// <param name="messageType">消息类型</param>
        /// <param name="handler">消息处理器</param>
        public void RegisterMessageHandler(MessageType messageType, IMessageHandler handler)
        {
            try
            {
                if (handler == null)
                {
                    LogWarning("消息处理器不能为空");
                    return;
                }

                if (!_messageHandlers.ContainsKey(messageType))
                {
                    _messageHandlers[messageType] = new List<IMessageHandler>();
                }

                _messageHandlers[messageType].Add(handler);

                LogInfo($"已注册消息处理器: {messageType} -> {handler.GetType().Name}");
            }
            catch (Exception ex)
            {
                LogError($"注册消息处理器时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 取消注册消息处理器
        /// </summary>
        /// <param name="messageType">消息类型</param>
        /// <param name="handler">消息处理器</param>
        public void UnregisterMessageHandler(MessageType messageType, IMessageHandler handler)
        {
            try
            {
                if (_messageHandlers.ContainsKey(messageType))
                {
                    _messageHandlers[messageType].Remove(handler);
                    LogInfo($"已取消注册消息处理器: {messageType} -> {handler.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                LogError($"取消注册消息处理器时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 网络消息处理

        /// <summary>
        /// 处理接收到的网络消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="senderId">发送者ID</param>
        private void HandleIncomingMessage(byte[] data, string senderId)
        {
            try
            {
                // 反序列化消息
                var messageJson = System.Text.Encoding.UTF8.GetString(data);
                var chatMessage = ChatMessage.FromJson(messageJson);

                if (chatMessage == null || !chatMessage.IsValid())
                {
                    LogWarning($"收到无效消息，发送者: {senderId}");
                    return;
                }

                _statistics.TotalMessagesReceived++;

                // 调用相应的消息处理器
                if (_messageHandlers.ContainsKey(chatMessage.Type))
                {
                    var handlers = _messageHandlers[chatMessage.Type];
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            handler.HandleMessage(chatMessage, senderId);
                        }
                        catch (Exception ex)
                        {
                            LogError($"消息处理器执行失败: {handler.GetType().Name}, 错误: {ex.Message}");
                        }
                    }
                }

                LogDebug($"处理接收消息: {chatMessage.Type}, 发送者: {senderId}");
            }
            catch (Exception ex)
            {
                LogError($"处理接收消息时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 统计信息

        /// <summary>
        /// 获取消息统计信息
        /// </summary>
        /// <returns>消息统计信息</returns>
        public MessageStatistics GetStatistics()
        {
            return _statistics.Clone();
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            _statistics.Reset();
            LogInfo("消息统计信息已重置");
        }

        #endregion

        #region 清理

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            try
            {
                // 取消订阅网络事件
                if (_networkManager != null)
                {
                    _networkManager.OnMessageReceived -= HandleIncomingMessage;
                }

                // 清理消息队列和待确认消息
                _messageQueue.Clear();
                _pendingMessages.Clear();

                // 清理消息处理器
                _messageHandlers.Clear();

                LogInfo("消息路由器已清理");
            }
            catch (Exception ex)
            {
                LogError($"清理消息路由器时发生异常: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[MessageRouter] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[MessageRouter] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[MessageRouter] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[MessageRouter][DEBUG] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 消息优先级枚举
    /// </summary>
    public enum MessagePriority
    {
        /// <summary>
        /// 低优先级
        /// </summary>
        Low = 0,

        /// <summary>
        /// 普通优先级
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 高优先级
        /// </summary>
        High = 2,

        /// <summary>
        /// 紧急优先级
        /// </summary>
        Critical = 3
    }

    /// <summary>
    /// 路由消息类
    /// </summary>
    public class RoutedMessage
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 聊天消息
        /// </summary>
        public ChatMessage Message { get; set; }

        /// <summary>
        /// 目标客机ID
        /// </summary>
        public string TargetId { get; set; }

        /// <summary>
        /// 消息优先级
        /// </summary>
        public MessagePriority Priority { get; set; }

        /// <summary>
        /// 是否需要确认
        /// </summary>
        public bool RequireAck { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }
    }

    /// <summary>
    /// 待确认消息类
    /// </summary>
    public class PendingMessage
    {
        /// <summary>
        /// 路由消息
        /// </summary>
        public RoutedMessage RoutedMessage { get; set; }

        /// <summary>
        /// 发送时间
        /// </summary>
        public DateTime SentTime { get; set; }

        /// <summary>
        /// 超时时间
        /// </summary>
        public DateTime TimeoutTime { get; set; }
    }

    /// <summary>
    /// 消息处理器接口
    /// </summary>
    public interface IMessageHandler
    {
        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="senderId">发送者ID</param>
        void HandleMessage(ChatMessage message, string senderId);
    }

    /// <summary>
    /// 消息统计信息类
    /// </summary>
    public class MessageStatistics
    {
        /// <summary>
        /// 总路由消息数
        /// </summary>
        public long TotalMessagesRouted { get; set; }

        /// <summary>
        /// 总发送消息数
        /// </summary>
        public long TotalMessagesSent { get; set; }

        /// <summary>
        /// 总接收消息数
        /// </summary>
        public long TotalMessagesReceived { get; set; }

        /// <summary>
        /// 总失败消息数
        /// </summary>
        public long TotalMessagesFailed { get; set; }

        /// <summary>
        /// 总确认消息数
        /// </summary>
        public long TotalMessagesAcknowledged { get; set; }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            TotalMessagesRouted = 0;
            TotalMessagesSent = 0;
            TotalMessagesReceived = 0;
            TotalMessagesFailed = 0;
            TotalMessagesAcknowledged = 0;
        }

        /// <summary>
        /// 克隆统计信息
        /// </summary>
        /// <returns>统计信息副本</returns>
        public MessageStatistics Clone()
        {
            return new MessageStatistics
            {
                TotalMessagesRouted = TotalMessagesRouted,
                TotalMessagesSent = TotalMessagesSent,
                TotalMessagesReceived = TotalMessagesReceived,
                TotalMessagesFailed = TotalMessagesFailed,
                TotalMessagesAcknowledged = TotalMessagesAcknowledged
            };
        }
    }

    /// <summary>
    /// 优先级队列实现
    /// </summary>
    public class PriorityQueue<T> where T : RoutedMessage
    {
        private readonly List<T> _items = new List<T>();

        /// <summary>
        /// 队列中的元素数量
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// 入队
        /// </summary>
        /// <param name="item">元素</param>
        public void Enqueue(T item)
        {
            _items.Add(item);
            _items.Sort((x, y) => y.Priority.CompareTo(x.Priority)); // 按优先级降序排序
        }

        /// <summary>
        /// 出队
        /// </summary>
        /// <returns>优先级最高的元素</returns>
        public T Dequeue()
        {
            if (_items.Count == 0)
                throw new InvalidOperationException("队列为空");

            var item = _items[0];
            _items.RemoveAt(0);
            return item;
        }

        /// <summary>
        /// 清空队列
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }
    }
}