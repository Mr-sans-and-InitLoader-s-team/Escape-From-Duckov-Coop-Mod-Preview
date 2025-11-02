using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// Steam P2P 可靠传输管理器
    /// 提供消息确认、重传和去重功能
    /// </summary>
    public class SteamReliableTransmission
    {
        #region 常量定义
        
        /// <summary>
        /// 消息确认超时时间（毫秒）
        /// </summary>
        private const int ACK_TIMEOUT_MS = 5000;
        
        /// <summary>
        /// 最大重传次数
        /// </summary>
        private const int MAX_RETRY_COUNT = 3;
        
        /// <summary>
        /// 消息去重缓存大小
        /// </summary>
        private const int DEDUP_CACHE_SIZE = 1000;
        
        /// <summary>
        /// 清理间隔（毫秒）
        /// </summary>
        private const int CLEANUP_INTERVAL_MS = 30000; // 30秒
        
        #endregion
        
        #region 字段和属性
        
        /// <summary>
        /// 待确认的消息列表
        /// </summary>
        private readonly Dictionary<string, PendingMessage> _pendingMessages = new Dictionary<string, PendingMessage>();
        
        /// <summary>
        /// 消息去重缓存
        /// </summary>
        private readonly HashSet<string> _processedMessages = new HashSet<string>();
        
        /// <summary>
        /// 去重缓存队列（用于维护缓存大小）
        /// </summary>
        private readonly Queue<string> _dedupQueue = new Queue<string>();
        
        /// <summary>
        /// 消息发送回调
        /// </summary>
        public Func<CSteamID, byte[], bool> SendMessageCallback { get; set; }
        
        /// <summary>
        /// 消息接收事件
        /// </summary>
        public event Action<SteamNetworkMessage, CSteamID> OnMessageReceived;
        
        /// <summary>
        /// 消息发送失败事件
        /// </summary>
        public event Action<string, CSteamID, string> OnMessageSendFailed;
        
        /// <summary>
        /// 最后清理时间
        /// </summary>
        private DateTime _lastCleanupTime = DateTime.UtcNow;
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 发送可靠消息
        /// </summary>
        /// <param name="message">网络消息</param>
        /// <param name="targetId">目标Steam ID</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendReliableMessage(SteamNetworkMessage message, CSteamID targetId)
        {
            try
            {
                // 序列化消息
                var messageData = SteamMessageProtocol.SerializeMessage(message);
                if (messageData == null)
                {
                    LogError("消息序列化失败");
                    return false;
                }
                
                // 如果消息需要确认，添加到待确认列表
                if (message.Flags.HasFlag(SteamMessageFlags.RequireAck))
                {
                    var pendingMessage = new PendingMessage
                    {
                        Message = message,
                        TargetId = targetId,
                        MessageData = messageData,
                        SendTime = DateTime.UtcNow,
                        RetryCount = 0
                    };
                    
                    _pendingMessages[message.MessageId] = pendingMessage;
                }
                
                // 发送消息
                bool success = SendMessageCallback?.Invoke(targetId, messageData) ?? false;
                
                if (!success)
                {
                    LogError($"发送消息失败: {message.MessageId}");
                    
                    // 如果不需要确认，直接触发失败事件
                    if (!message.Flags.HasFlag(SteamMessageFlags.RequireAck))
                    {
                        OnMessageSendFailed?.Invoke(message.MessageId, targetId, "发送失败");
                    }
                }
                
                return success;
            }
            catch (Exception ex)
            {
                LogError($"发送可靠消息时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        /// <param name="messageData">消息数据</param>
        /// <param name="senderId">发送者Steam ID</param>
        public void ProcessReceivedMessage(byte[] messageData, CSteamID senderId)
        {
            try
            {
                // 反序列化消息
                var message = SteamMessageProtocol.DeserializeMessage(messageData);
                if (message == null)
                {
                    LogWarning("消息反序列化失败");
                    return;
                }
                
                // 验证消息
                if (!SteamMessageProtocol.ValidateMessage(message))
                {
                    LogWarning($"消息验证失败: {message.MessageId}");
                    return;
                }
                
                // 处理确认消息
                if (message.Type == SteamMessageType.Acknowledgment)
                {
                    ProcessAcknowledgment(message);
                    return;
                }
                
                // 检查消息去重
                if (IsMessageProcessed(message.MessageId))
                {
                    LogDebug($"重复消息，跳过处理: {message.MessageId}");
                    
                    // 如果需要确认，重新发送确认
                    if (message.Flags.HasFlag(SteamMessageFlags.RequireAck))
                    {
                        SendAcknowledgment(message.MessageId, senderId);
                    }
                    return;
                }
                
                // 添加到去重缓存
                AddToDeduplicationCache(message.MessageId);
                
                // 如果需要确认，发送确认消息
                if (message.Flags.HasFlag(SteamMessageFlags.RequireAck))
                {
                    SendAcknowledgment(message.MessageId, senderId);
                }
                
                // 触发消息接收事件
                OnMessageReceived?.Invoke(message, senderId);
                
                LogDebug($"处理消息完成: {message.Type}, ID: {message.MessageId}");
            }
            catch (Exception ex)
            {
                LogError($"处理接收消息时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新可靠传输（需要定期调用）
        /// </summary>
        public void Update()
        {
            try
            {
                // 检查待确认消息的超时
                CheckPendingMessageTimeouts();
                
                // 定期清理
                if ((DateTime.UtcNow - _lastCleanupTime).TotalMilliseconds >= CLEANUP_INTERVAL_MS)
                {
                    CleanupExpiredData();
                    _lastCleanupTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                LogError($"更新可靠传输时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清理所有待处理的消息
        /// </summary>
        public void Clear()
        {
            _pendingMessages.Clear();
            _processedMessages.Clear();
            _dedupQueue.Clear();
        }
        
        #endregion
        
        #region 私有方法
        
        /// <summary>
        /// 处理确认消息
        /// </summary>
        /// <param name="ackMessage">确认消息</param>
        private void ProcessAcknowledgment(SteamNetworkMessage ackMessage)
        {
            try
            {
                if (ackMessage.Payload is string ackMessageId)
                {
                    if (_pendingMessages.ContainsKey(ackMessageId))
                    {
                        _pendingMessages.Remove(ackMessageId);
                        LogDebug($"收到消息确认: {ackMessageId}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"处理确认消息时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 发送确认消息
        /// </summary>
        /// <param name="messageId">要确认的消息ID</param>
        /// <param name="targetId">目标Steam ID</param>
        private void SendAcknowledgment(string messageId, CSteamID targetId)
        {
            try
            {
                var ackMessage = new SteamNetworkMessage
                {
                    Type = SteamMessageType.Acknowledgment,
                    SenderId = SteamUser.GetSteamID().m_SteamID.ToString(),
                    Payload = messageId,
                    Flags = SteamMessageFlags.None // 确认消息不需要再次确认
                };
                
                var ackData = SteamMessageProtocol.SerializeMessage(ackMessage);
                if (ackData != null)
                {
                    SendMessageCallback?.Invoke(targetId, ackData);
                    LogDebug($"发送确认消息: {messageId}");
                }
            }
            catch (Exception ex)
            {
                LogError($"发送确认消息时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 检查待确认消息的超时
        /// </summary>
        private void CheckPendingMessageTimeouts()
        {
            var currentTime = DateTime.UtcNow;
            var timeoutMessages = new List<string>();
            
            foreach (var kvp in _pendingMessages)
            {
                var messageId = kvp.Key;
                var pendingMessage = kvp.Value;
                
                var elapsed = (currentTime - pendingMessage.SendTime).TotalMilliseconds;
                
                if (elapsed >= ACK_TIMEOUT_MS)
                {
                    if (pendingMessage.RetryCount < MAX_RETRY_COUNT)
                    {
                        // 重试发送
                        RetryMessage(pendingMessage);
                    }
                    else
                    {
                        // 超过最大重试次数，标记为失败
                        timeoutMessages.Add(messageId);
                        OnMessageSendFailed?.Invoke(messageId, pendingMessage.TargetId, "确认超时");
                        LogWarning($"消息发送失败，超过最大重试次数: {messageId}");
                    }
                }
            }
            
            // 移除超时的消息
            foreach (var messageId in timeoutMessages)
            {
                _pendingMessages.Remove(messageId);
            }
        }
        
        /// <summary>
        /// 重试发送消息
        /// </summary>
        /// <param name="pendingMessage">待发送消息</param>
        private void RetryMessage(PendingMessage pendingMessage)
        {
            try
            {
                pendingMessage.RetryCount++;
                pendingMessage.SendTime = DateTime.UtcNow;
                
                bool success = SendMessageCallback?.Invoke(pendingMessage.TargetId, pendingMessage.MessageData) ?? false;
                
                LogDebug($"重试发送消息: {pendingMessage.Message.MessageId}, 次数: {pendingMessage.RetryCount}, 结果: {success}");
            }
            catch (Exception ex)
            {
                LogError($"重试发送消息时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 检查消息是否已处理
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <returns>是否已处理</returns>
        private bool IsMessageProcessed(string messageId)
        {
            return _processedMessages.Contains(messageId);
        }
        
        /// <summary>
        /// 添加到去重缓存
        /// </summary>
        /// <param name="messageId">消息ID</param>
        private void AddToDeduplicationCache(string messageId)
        {
            _processedMessages.Add(messageId);
            _dedupQueue.Enqueue(messageId);
            
            // 维护缓存大小
            while (_dedupQueue.Count > DEDUP_CACHE_SIZE)
            {
                var oldMessageId = _dedupQueue.Dequeue();
                _processedMessages.Remove(oldMessageId);
            }
        }
        
        /// <summary>
        /// 清理过期数据
        /// </summary>
        private void CleanupExpiredData()
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var expiredMessages = new List<string>();
                
                // 清理过期的待确认消息
                foreach (var kvp in _pendingMessages)
                {
                    var elapsed = (currentTime - kvp.Value.SendTime).TotalMilliseconds;
                    if (elapsed > ACK_TIMEOUT_MS * (MAX_RETRY_COUNT + 1))
                    {
                        expiredMessages.Add(kvp.Key);
                    }
                }
                
                foreach (var messageId in expiredMessages)
                {
                    _pendingMessages.Remove(messageId);
                    LogDebug($"清理过期消息: {messageId}");
                }
                
                LogDebug($"清理完成，待确认消息数: {_pendingMessages.Count}, 去重缓存数: {_processedMessages.Count}");
            }
            catch (Exception ex)
            {
                LogError($"清理过期数据时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 日志方法
        
        private void LogDebug(string message)
        {
            Debug.Log($"[SteamReliableTransmission] {message}");
        }
        
        private void LogWarning(string message)
        {
            Debug.LogWarning($"[SteamReliableTransmission] {message}");
        }
        
        private void LogError(string message)
        {
            Debug.LogError($"[SteamReliableTransmission] {message}");
        }
        
        #endregion
    }
    
    /// <summary>
    /// 待确认消息
    /// </summary>
    internal class PendingMessage
    {
        /// <summary>
        /// 网络消息
        /// </summary>
        public SteamNetworkMessage Message { get; set; }
        
        /// <summary>
        /// 目标Steam ID
        /// </summary>
        public CSteamID TargetId { get; set; }
        
        /// <summary>
        /// 序列化后的消息数据
        /// </summary>
        public byte[] MessageData { get; set; }
        
        /// <summary>
        /// 发送时间
        /// </summary>
        public DateTime SendTime { get; set; }
        
        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }
    }
}