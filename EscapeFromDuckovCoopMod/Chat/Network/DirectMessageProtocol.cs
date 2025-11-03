using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 直连消息协议
    /// 处理消息的分片、重组、确认和错误恢复
    /// </summary>
    public class DirectMessageProtocol : IDisposable
    {
        #region 常量定义

        /// <summary>
        /// 最大分片大小（字节）
        /// </summary>
        private const int MAX_FRAGMENT_SIZE = 1024; // 1KB per fragment

        /// <summary>
        /// 消息头大小（字节）
        /// </summary>
        private const int MESSAGE_HEADER_SIZE = 20;

        /// <summary>
        /// 确认超时时间（毫秒）
        /// </summary>
        private const int ACK_TIMEOUT_MS = 5000;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        private const int MAX_RETRY_COUNT = 3;

        /// <summary>
        /// 消息缓存过期时间（毫秒）
        /// </summary>
        private const int MESSAGE_CACHE_EXPIRE_MS = 30000;

        #endregion

        #region 字段和属性

        /// <summary>
        /// 消息序列号
        /// </summary>
        private uint _messageSequence = 0;

        /// <summary>
        /// 分片序列号
        /// </summary>
        private uint _fragmentSequence = 0;

        /// <summary>
        /// 待确认的消息
        /// </summary>
        private readonly Dictionary<uint, DirectPendingMessage> _pendingMessages = new Dictionary<uint, DirectPendingMessage>();

        /// <summary>
        /// 接收中的消息片段
        /// </summary>
        private readonly Dictionary<uint, ReceivingMessage> _receivingMessages = new Dictionary<uint, ReceivingMessage>();

        /// <summary>
        /// 消息发送回调
        /// </summary>
        public Func<byte[], Task<bool>> SendRawDataCallback { get; set; }

        /// <summary>
        /// 是否已释放
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 事件

        /// <summary>
        /// 消息接收完成事件
        /// </summary>
        public event Action<byte[], string> OnMessageReceived;

        /// <summary>
        /// 消息发送失败事件
        /// </summary>
        public event Action<uint, string> OnMessageSendFailed;

        /// <summary>
        /// 协议错误事件
        /// </summary>
        public event Action<string> OnProtocolError;

        #endregion

        #region 消息发送

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="requireAck">是否需要确认</param>
        /// <returns>消息ID</returns>
        public async Task<uint> SendMessage(byte[] data, bool requireAck = true)
        {
            if (_disposed || data == null || data.Length == 0)
            {
                return 0;
            }

            try
            {
                var messageId = ++_messageSequence;
                
                LogDebug($"开始发送消息: ID={messageId}, 大小={data.Length}字节, 需要确认={requireAck}");

                // 如果消息较小，直接发送
                if (data.Length <= MAX_FRAGMENT_SIZE - MESSAGE_HEADER_SIZE)
                {
                    return await SendSingleMessage(messageId, data, requireAck);
                }
                else
                {
                    return await SendFragmentedMessage(messageId, data, requireAck);
                }
            }
            catch (Exception ex)
            {
                LogError($"发送消息时发生异常: {ex.Message}");
                OnProtocolError?.Invoke($"发送消息异常: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 发送单个消息
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="data">消息数据</param>
        /// <param name="requireAck">是否需要确认</param>
        /// <returns>消息ID</returns>
        private async Task<uint> SendSingleMessage(uint messageId, byte[] data, bool requireAck)
        {
            var message = new DirectMessage
            {
                MessageId = messageId,
                MessageType = DirectMessageType.Single,
                FragmentIndex = 0,
                TotalFragments = 1,
                DataLength = (uint)data.Length,
                RequireAck = requireAck,
                Data = data
            };

            var packetData = SerializeMessage(message);
            var success = await SendRawData(packetData);

            if (success && requireAck)
            {
                // 添加到待确认列表
                _pendingMessages[messageId] = new DirectPendingMessage
                {
                    MessageId = messageId,
                    Data = data,
                    RequireAck = requireAck,
                    SendTime = DateTime.UtcNow,
                    RetryCount = 0
                };

                // 启动确认超时检查
                _ = Task.Delay(ACK_TIMEOUT_MS).ContinueWith(async _ => await CheckAckTimeout(messageId));
            }

            return success ? messageId : 0;
        }

        /// <summary>
        /// 发送分片消息
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="data">消息数据</param>
        /// <param name="requireAck">是否需要确认</param>
        /// <returns>消息ID</returns>
        private async Task<uint> SendFragmentedMessage(uint messageId, byte[] data, bool requireAck)
        {
            var fragmentSize = MAX_FRAGMENT_SIZE - MESSAGE_HEADER_SIZE;
            var totalFragments = (uint)Math.Ceiling((double)data.Length / fragmentSize);
            
            LogDebug($"发送分片消息: ID={messageId}, 总片数={totalFragments}");

            bool allSuccess = true;

            for (uint i = 0; i < totalFragments; i++)
            {
                var offset = (int)(i * fragmentSize);
                var length = Math.Min(fragmentSize, data.Length - offset);
                var fragmentData = new byte[length];
                Array.Copy(data, offset, fragmentData, 0, length);

                var fragment = new DirectMessage
                {
                    MessageId = messageId,
                    MessageType = DirectMessageType.Fragment,
                    FragmentIndex = i,
                    TotalFragments = totalFragments,
                    DataLength = (uint)length,
                    RequireAck = requireAck && (i == totalFragments - 1), // 只有最后一片需要确认
                    Data = fragmentData
                };

                var packetData = SerializeMessage(fragment);
                var success = await SendRawData(packetData);

                if (!success)
                {
                    allSuccess = false;
                    LogError($"发送分片失败: 消息ID={messageId}, 分片={i}/{totalFragments}");
                }

                // 分片间稍作延迟，避免网络拥塞
                if (i < totalFragments - 1)
                {
                    await Task.Delay(10);
                }
            }

            if (allSuccess && requireAck)
            {
                // 添加到待确认列表
                _pendingMessages[messageId] = new DirectPendingMessage
                {
                    MessageId = messageId,
                    Data = data,
                    RequireAck = requireAck,
                    SendTime = DateTime.UtcNow,
                    RetryCount = 0
                };

                // 启动确认超时检查
                _ = Task.Delay(ACK_TIMEOUT_MS).ContinueWith(async _ => await CheckAckTimeout(messageId));
            }

            return allSuccess ? messageId : 0;
        }

        /// <summary>
        /// 发送原始数据
        /// </summary>
        /// <param name="data">数据</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendRawData(byte[] data)
        {
            if (SendRawDataCallback == null)
            {
                LogError("发送回调未设置");
                return false;
            }

            return await SendRawDataCallback(data);
        }

        #endregion

        #region 消息接收

        /// <summary>
        /// 处理接收到的数据
        /// </summary>
        /// <param name="data">接收到的数据</param>
        /// <param name="senderId">发送者ID</param>
        public void ProcessReceivedData(byte[] data, string senderId)
        {
            if (_disposed || data == null || data.Length < MESSAGE_HEADER_SIZE)
            {
                return;
            }

            try
            {
                var message = DeserializeMessage(data);
                if (message == null)
                {
                    LogError("消息反序列化失败");
                    return;
                }

                LogDebug($"收到消息: ID={message.MessageId}, 类型={message.MessageType}, 分片={message.FragmentIndex}/{message.TotalFragments}");

                switch (message.MessageType)
                {
                    case DirectMessageType.Single:
                        ProcessSingleMessage(message, senderId);
                        break;
                    case DirectMessageType.Fragment:
                        ProcessFragmentMessage(message, senderId);
                        break;
                    case DirectMessageType.Ack:
                        ProcessAckMessage(message);
                        break;
                    case DirectMessageType.Nack:
                        ProcessNackMessage(message);
                        break;
                    default:
                        LogWarning($"未知消息类型: {message.MessageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogError($"处理接收数据时发生异常: {ex.Message}");
                OnProtocolError?.Invoke($"处理接收数据异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理单个消息
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="senderId">发送者ID</param>
        private void ProcessSingleMessage(DirectMessage message, string senderId)
        {
            // 发送确认
            if (message.RequireAck)
            {
                _ = SendAckMessage(message.MessageId, true);
            }

            // 触发消息接收事件
            OnMessageReceived?.Invoke(message.Data, senderId);
        }

        /// <summary>
        /// 处理分片消息
        /// </summary>
        /// <param name="message">消息分片</param>
        /// <param name="senderId">发送者ID</param>
        private void ProcessFragmentMessage(DirectMessage message, string senderId)
        {
            var messageId = message.MessageId;

            // 获取或创建接收中的消息
            if (!_receivingMessages.ContainsKey(messageId))
            {
                _receivingMessages[messageId] = new ReceivingMessage
                {
                    MessageId = messageId,
                    TotalFragments = message.TotalFragments,
                    ReceivedFragments = new Dictionary<uint, byte[]>(),
                    SenderId = senderId,
                    StartTime = DateTime.UtcNow
                };
            }

            var receivingMessage = _receivingMessages[messageId];

            // 添加分片
            receivingMessage.ReceivedFragments[message.FragmentIndex] = message.Data;

            LogDebug($"收到分片: 消息ID={messageId}, 分片={message.FragmentIndex}, 已收到={receivingMessage.ReceivedFragments.Count}/{message.TotalFragments}");

            // 检查是否收到所有分片
            if (receivingMessage.ReceivedFragments.Count == message.TotalFragments)
            {
                // 重组消息
                var completeMessage = ReassembleMessage(receivingMessage);
                if (completeMessage != null)
                {
                    // 发送确认（如果需要）
                    if (message.RequireAck)
                    {
                        _ = SendAckMessage(messageId, true);
                    }

                    // 触发消息接收事件
                    OnMessageReceived?.Invoke(completeMessage, senderId);
                }
                else
                {
                    LogError($"消息重组失败: ID={messageId}");
                    
                    // 发送否定确认
                    if (message.RequireAck)
                    {
                        _ = SendAckMessage(messageId, false);
                    }
                }

                // 清理接收中的消息
                _receivingMessages.Remove(messageId);
            }
        }

        /// <summary>
        /// 重组消息
        /// </summary>
        /// <param name="receivingMessage">接收中的消息</param>
        /// <returns>完整消息数据</returns>
        private byte[] ReassembleMessage(ReceivingMessage receivingMessage)
        {
            try
            {
                var totalLength = receivingMessage.ReceivedFragments.Values.Sum(f => f.Length);
                var completeData = new byte[totalLength];
                var offset = 0;

                // 按分片索引顺序重组
                for (uint i = 0; i < receivingMessage.TotalFragments; i++)
                {
                    if (!receivingMessage.ReceivedFragments.ContainsKey(i))
                    {
                        LogError($"缺少分片: 消息ID={receivingMessage.MessageId}, 分片={i}");
                        return null;
                    }

                    var fragmentData = receivingMessage.ReceivedFragments[i];
                    Array.Copy(fragmentData, 0, completeData, offset, fragmentData.Length);
                    offset += fragmentData.Length;
                }

                LogDebug($"消息重组成功: ID={receivingMessage.MessageId}, 总大小={totalLength}字节");
                return completeData;
            }
            catch (Exception ex)
            {
                LogError($"重组消息时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 处理确认消息
        /// </summary>
        /// <param name="message">确认消息</param>
        private void ProcessAckMessage(DirectMessage message)
        {
            var messageId = message.MessageId;
            
            if (_pendingMessages.ContainsKey(messageId))
            {
                _pendingMessages.Remove(messageId);
                LogDebug($"收到消息确认: ID={messageId}");
            }
        }

        /// <summary>
        /// 处理否定确认消息
        /// </summary>
        /// <param name="message">否定确认消息</param>
        private void ProcessNackMessage(DirectMessage message)
        {
            var messageId = message.MessageId;
            
            if (_pendingMessages.ContainsKey(messageId))
            {
                var pendingMessage = _pendingMessages[messageId];
                
                LogWarning($"收到否定确认: ID={messageId}, 重试次数={pendingMessage.RetryCount}");
                
                // 重试发送
                if (pendingMessage.RetryCount < MAX_RETRY_COUNT)
                {
                    pendingMessage.RetryCount++;
                    pendingMessage.SendTime = DateTime.UtcNow;
                    
                    _ = Task.Run(async () => await RetryMessage(pendingMessage));
                }
                else
                {
                    // 超过最大重试次数，标记为失败
                    _pendingMessages.Remove(messageId);
                    OnMessageSendFailed?.Invoke(messageId, "超过最大重试次数");
                }
            }
        }

        #endregion

        #region 确认和重试

        /// <summary>
        /// 发送确认消息
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="success">是否成功</param>
        private async Task SendAckMessage(uint messageId, bool success)
        {
            try
            {
                var ackMessage = new DirectMessage
                {
                    MessageId = messageId,
                    MessageType = success ? DirectMessageType.Ack : DirectMessageType.Nack,
                    FragmentIndex = 0,
                    TotalFragments = 1,
                    DataLength = 0,
                    RequireAck = false,
                    Data = new byte[0]
                };

                var packetData = SerializeMessage(ackMessage);
                await SendRawData(packetData);

                LogDebug($"发送{(success ? "确认" : "否定确认")}: ID={messageId}");
            }
            catch (Exception ex)
            {
                LogError($"发送确认消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查确认超时
        /// </summary>
        /// <param name="messageId">消息ID</param>
        private async Task CheckAckTimeout(uint messageId)
        {
            try
            {
                if (_pendingMessages.ContainsKey(messageId))
                {
                    var pendingMessage = _pendingMessages[messageId];
                    var elapsed = DateTime.UtcNow - pendingMessage.SendTime;
                    
                    if (elapsed.TotalMilliseconds >= ACK_TIMEOUT_MS)
                    {
                        LogWarning($"确认超时: ID={messageId}, 重试次数={pendingMessage.RetryCount}");
                        
                        if (pendingMessage.RetryCount < MAX_RETRY_COUNT)
                        {
                            pendingMessage.RetryCount++;
                            pendingMessage.SendTime = DateTime.UtcNow;
                            
                            await RetryMessage(pendingMessage);
                        }
                        else
                        {
                            // 超过最大重试次数
                            _pendingMessages.Remove(messageId);
                            OnMessageSendFailed?.Invoke(messageId, "确认超时");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"检查确认超时时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 重试发送消息
        /// </summary>
        /// <param name="pendingMessage">待发送消息</param>
        private async Task RetryMessage(DirectPendingMessage pendingMessage)
        {
            try
            {
                LogInfo($"重试发送消息: ID={pendingMessage.MessageId}, 第{pendingMessage.RetryCount}次重试");
                
                var messageId = await SendMessage(pendingMessage.Data, pendingMessage.RequireAck);
                
                if (messageId == 0)
                {
                    // 重试失败
                    if (pendingMessage.RetryCount >= MAX_RETRY_COUNT)
                    {
                        _pendingMessages.Remove(pendingMessage.MessageId);
                        OnMessageSendFailed?.Invoke(pendingMessage.MessageId, "重试发送失败");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"重试发送消息时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 消息序列化

        /// <summary>
        /// 序列化消息
        /// </summary>
        /// <param name="message">消息</param>
        /// <returns>序列化后的数据</returns>
        private byte[] SerializeMessage(DirectMessage message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // 写入消息头
                writer.Write(message.MessageId);           // 4 bytes
                writer.Write((byte)message.MessageType);   // 1 byte
                writer.Write(message.FragmentIndex);       // 4 bytes
                writer.Write(message.TotalFragments);      // 4 bytes
                writer.Write(message.DataLength);          // 4 bytes
                writer.Write(message.RequireAck);          // 1 byte
                writer.Write((ushort)0);                   // 2 bytes reserved

                // 写入数据
                if (message.Data != null && message.Data.Length > 0)
                {
                    writer.Write(message.Data);
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// 反序列化消息
        /// </summary>
        /// <param name="data">序列化数据</param>
        /// <returns>消息对象</returns>
        private DirectMessage DeserializeMessage(byte[] data)
        {
            try
            {
                using (var stream = new MemoryStream(data))
                using (var reader = new BinaryReader(stream))
                {
                    var message = new DirectMessage
                    {
                        MessageId = reader.ReadUInt32(),
                        MessageType = (DirectMessageType)reader.ReadByte(),
                        FragmentIndex = reader.ReadUInt32(),
                        TotalFragments = reader.ReadUInt32(),
                        DataLength = reader.ReadUInt32(),
                        RequireAck = reader.ReadBoolean()
                    };

                    // 跳过保留字节
                    reader.ReadUInt16();

                    // 读取数据
                    if (message.DataLength > 0)
                    {
                        message.Data = reader.ReadBytes((int)message.DataLength);
                    }
                    else
                    {
                        message.Data = new byte[0];
                    }

                    return message;
                }
            }
            catch (Exception ex)
            {
                LogError($"反序列化消息时发生异常: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 清理和维护

        /// <summary>
        /// 清理过期的消息
        /// </summary>
        public void CleanupExpiredMessages()
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredPending = _pendingMessages.Where(kvp => 
                    (now - kvp.Value.SendTime).TotalMilliseconds > MESSAGE_CACHE_EXPIRE_MS).ToList();

                foreach (var kvp in expiredPending)
                {
                    _pendingMessages.Remove(kvp.Key);
                    OnMessageSendFailed?.Invoke(kvp.Key, "消息过期");
                    LogWarning($"清理过期待确认消息: ID={kvp.Key}");
                }

                var expiredReceiving = _receivingMessages.Where(kvp => 
                    (now - kvp.Value.StartTime).TotalMilliseconds > MESSAGE_CACHE_EXPIRE_MS).ToList();

                foreach (var kvp in expiredReceiving)
                {
                    _receivingMessages.Remove(kvp.Key);
                    LogWarning($"清理过期接收消息: ID={kvp.Key}");
                }
            }
            catch (Exception ex)
            {
                LogError($"清理过期消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取协议统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        public ProtocolStatistics GetStatistics()
        {
            return new ProtocolStatistics
            {
                PendingMessagesCount = _pendingMessages.Count,
                ReceivingMessagesCount = _receivingMessages.Count,
                NextMessageId = _messageSequence + 1,
                NextFragmentId = _fragmentSequence + 1
            };
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[DirectMessageProtocol] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[DirectMessageProtocol] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[DirectMessageProtocol] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[DirectMessageProtocol][DEBUG] {message}");
        }

        #endregion

        #region IDisposable 实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的具体实现
        /// </summary>
        /// <param name="disposing">是否正在释放</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _pendingMessages.Clear();
                    _receivingMessages.Clear();
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~DirectMessageProtocol()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// 直连消息类型枚举
    /// </summary>
    public enum DirectMessageType : byte
    {
        /// <summary>
        /// 单个消息
        /// </summary>
        Single = 1,

        /// <summary>
        /// 消息分片
        /// </summary>
        Fragment = 2,

        /// <summary>
        /// 确认消息
        /// </summary>
        Ack = 3,

        /// <summary>
        /// 否定确认消息
        /// </summary>
        Nack = 4
    }

    /// <summary>
    /// 直连消息类
    /// </summary>
    public class DirectMessage
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        public uint MessageId { get; set; }

        /// <summary>
        /// 消息类型
        /// </summary>
        public DirectMessageType MessageType { get; set; }

        /// <summary>
        /// 分片索引
        /// </summary>
        public uint FragmentIndex { get; set; }

        /// <summary>
        /// 总分片数
        /// </summary>
        public uint TotalFragments { get; set; }

        /// <summary>
        /// 数据长度
        /// </summary>
        public uint DataLength { get; set; }

        /// <summary>
        /// 是否需要确认
        /// </summary>
        public bool RequireAck { get; set; }

        /// <summary>
        /// 消息数据
        /// </summary>
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// 待确认消息类
    /// </summary>
    public class DirectPendingMessage
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        public uint MessageId { get; set; }

        /// <summary>
        /// 消息数据
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// 是否需要确认
        /// </summary>
        public bool RequireAck { get; set; }

        /// <summary>
        /// 发送时间
        /// </summary>
        public DateTime SendTime { get; set; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }
    }

    /// <summary>
    /// 接收中的消息类
    /// </summary>
    public class ReceivingMessage
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        public uint MessageId { get; set; }

        /// <summary>
        /// 总分片数
        /// </summary>
        public uint TotalFragments { get; set; }

        /// <summary>
        /// 已接收的分片
        /// </summary>
        public Dictionary<uint, byte[]> ReceivedFragments { get; set; }

        /// <summary>
        /// 发送者ID
        /// </summary>
        public string SenderId { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
    }

    /// <summary>
    /// 协议统计信息类
    /// </summary>
    public class ProtocolStatistics
    {
        /// <summary>
        /// 待确认消息数量
        /// </summary>
        public int PendingMessagesCount { get; set; }

        /// <summary>
        /// 接收中消息数量
        /// </summary>
        public int ReceivingMessagesCount { get; set; }

        /// <summary>
        /// 下一个消息ID
        /// </summary>
        public uint NextMessageId { get; set; }

        /// <summary>
        /// 下一个分片ID
        /// </summary>
        public uint NextFragmentId { get; set; }

        public override string ToString()
        {
            return $"待确认: {PendingMessagesCount}, 接收中: {ReceivingMessagesCount}, 下一个消息ID: {NextMessageId}";
        }
    }
}