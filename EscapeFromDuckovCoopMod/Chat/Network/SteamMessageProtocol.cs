using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// Steam 消息协议处理器
    /// 负责消息的序列化、反序列化和协议处理
    /// </summary>
    public static class SteamMessageProtocol
    {
        #region 常量定义
        
        /// <summary>
        /// 协议版本
        /// </summary>
        public const byte PROTOCOL_VERSION = 1;
        
        /// <summary>
        /// 消息头大小（字节）
        /// </summary>
        public const int MESSAGE_HEADER_SIZE = 8;
        
        /// <summary>
        /// 最大消息内容大小（字节）
        /// </summary>
        public const int MAX_MESSAGE_CONTENT_SIZE = 1024 * 1024 - MESSAGE_HEADER_SIZE; // 1MB - 头部大小
        
        #endregion
        
        #region 消息序列化
        
        /// <summary>
        /// 序列化网络消息
        /// </summary>
        /// <param name="message">网络消息</param>
        /// <returns>序列化后的字节数组</returns>
        public static byte[] SerializeMessage(SteamNetworkMessage message)
        {
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    // 写入消息头
                    WriteMessageHeader(writer, message);
                    
                    // 写入消息内容
                    WriteMessageContent(writer, message);
                    
                    return stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"序列化 Steam 消息时发生异常: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 反序列化网络消息
        /// </summary>
        /// <param name="data">字节数据</param>
        /// <returns>网络消息对象</returns>
        public static SteamNetworkMessage DeserializeMessage(byte[] data)
        {
            try
            {
                if (data == null || data.Length < MESSAGE_HEADER_SIZE)
                {
                    Debug.LogWarning("消息数据无效或过短");
                    return null;
                }
                
                using (var stream = new MemoryStream(data))
                using (var reader = new BinaryReader(stream))
                {
                    // 读取消息头
                    var message = ReadMessageHeader(reader);
                    if (message == null)
                    {
                        return null;
                    }
                    
                    // 读取消息内容
                    ReadMessageContent(reader, message);
                    
                    return message;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"反序列化 Steam 消息时发生异常: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 写入消息头
        /// </summary>
        /// <param name="writer">二进制写入器</param>
        /// <param name="message">消息对象</param>
        private static void WriteMessageHeader(BinaryWriter writer, SteamNetworkMessage message)
        {
            writer.Write(PROTOCOL_VERSION);                    // 1字节：协议版本
            writer.Write((byte)message.Type);                 // 1字节：消息类型
            writer.Write((ushort)message.Flags);              // 2字节：消息标志
            writer.Write((uint)message.Timestamp.Ticks);      // 4字节：时间戳
        }
        
        /// <summary>
        /// 写入消息内容
        /// </summary>
        /// <param name="writer">二进制写入器</param>
        /// <param name="message">消息对象</param>
        private static void WriteMessageContent(BinaryWriter writer, SteamNetworkMessage message)
        {
            // 序列化载荷数据
            string payloadJson = "";
            if (message.Payload != null)
            {
                payloadJson = JsonConvert.SerializeObject(message.Payload);
            }
            
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            
            // 检查内容大小
            if (payloadBytes.Length > MAX_MESSAGE_CONTENT_SIZE)
            {
                throw new InvalidOperationException($"消息内容过大: {payloadBytes.Length} > {MAX_MESSAGE_CONTENT_SIZE}");
            }
            
            // 写入发送者ID
            var senderBytes = Encoding.UTF8.GetBytes(message.SenderId ?? "");
            writer.Write((ushort)senderBytes.Length);
            writer.Write(senderBytes);
            
            // 写入载荷长度和数据
            writer.Write((uint)payloadBytes.Length);
            writer.Write(payloadBytes);
        }
        
        /// <summary>
        /// 读取消息头
        /// </summary>
        /// <param name="reader">二进制读取器</param>
        /// <returns>消息对象</returns>
        private static SteamNetworkMessage ReadMessageHeader(BinaryReader reader)
        {
            var version = reader.ReadByte();
            if (version != PROTOCOL_VERSION)
            {
                Debug.LogWarning($"不支持的协议版本: {version}");
                return null;
            }
            
            var messageType = (SteamMessageType)reader.ReadByte();
            var flags = (SteamMessageFlags)reader.ReadUInt16();
            var timestampTicks = reader.ReadUInt32();
            
            return new SteamNetworkMessage
            {
                Type = messageType,
                Flags = flags,
                Timestamp = new DateTime(timestampTicks)
            };
        }
        
        /// <summary>
        /// 读取消息内容
        /// </summary>
        /// <param name="reader">二进制读取器</param>
        /// <param name="message">消息对象</param>
        private static void ReadMessageContent(BinaryReader reader, SteamNetworkMessage message)
        {
            // 读取发送者ID
            var senderLength = reader.ReadUInt16();
            var senderBytes = reader.ReadBytes(senderLength);
            message.SenderId = Encoding.UTF8.GetString(senderBytes);
            
            // 读取载荷数据
            var payloadLength = reader.ReadUInt32();
            if (payloadLength > 0)
            {
                var payloadBytes = reader.ReadBytes((int)payloadLength);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);
                
                // 根据消息类型反序列化载荷
                message.Payload = DeserializePayload(message.Type, payloadJson);
            }
        }
        
        /// <summary>
        /// 反序列化载荷数据
        /// </summary>
        /// <param name="messageType">消息类型</param>
        /// <param name="payloadJson">载荷JSON</param>
        /// <returns>载荷对象</returns>
        private static object DeserializePayload(SteamMessageType messageType, string payloadJson)
        {
            try
            {
                if (string.IsNullOrEmpty(payloadJson))
                {
                    return null;
                }
                
                switch (messageType)
                {
                    case SteamMessageType.ChatMessage:
                        return JsonConvert.DeserializeObject<Models.ChatMessage>(payloadJson);
                    case SteamMessageType.UserJoined:
                    case SteamMessageType.UserLeft:
                        return JsonConvert.DeserializeObject<Models.UserInfo>(payloadJson);
                    case SteamMessageType.HistoryRequest:
                        return JsonConvert.DeserializeObject<HistoryRequestPayload>(payloadJson);
                    case SteamMessageType.HistoryResponse:
                        return JsonConvert.DeserializeObject<HistoryResponsePayload>(payloadJson);
                    case SteamMessageType.Heartbeat:
                        return JsonConvert.DeserializeObject<HeartbeatPayload>(payloadJson);
                    default:
                        return JsonConvert.DeserializeObject<object>(payloadJson);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"反序列化载荷数据时发生异常: {ex.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region 消息验证
        
        /// <summary>
        /// 验证消息是否有效
        /// </summary>
        /// <param name="message">消息对象</param>
        /// <returns>是否有效</returns>
        public static bool ValidateMessage(SteamNetworkMessage message)
        {
            if (message == null)
            {
                return false;
            }
            
            // 检查消息类型
            if (!Enum.IsDefined(typeof(SteamMessageType), message.Type))
            {
                Debug.LogWarning($"无效的消息类型: {message.Type}");
                return false;
            }
            
            // 检查发送者ID
            if (string.IsNullOrEmpty(message.SenderId))
            {
                Debug.LogWarning("消息发送者ID为空");
                return false;
            }
            
            // 检查时间戳
            var timeDiff = DateTime.UtcNow - message.Timestamp;
            if (Math.Abs(timeDiff.TotalMinutes) > 5) // 允许5分钟的时间差
            {
                Debug.LogWarning($"消息时间戳异常: {timeDiff.TotalMinutes}分钟");
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// 计算消息校验和
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>校验和</returns>
        public static uint CalculateChecksum(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return 0;
            }
            
            uint checksum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                checksum = (checksum << 1) ^ data[i];
            }
            
            return checksum;
        }
        
        #endregion
        
        #region 消息压缩
        
        /// <summary>
        /// 压缩消息数据
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <returns>压缩后的数据</returns>
        public static byte[] CompressMessage(byte[] data)
        {
            try
            {
                // 简单的压缩实现，实际项目中可以使用更高效的压缩算法
                if (data == null || data.Length < 100) // 小于100字节不压缩
                {
                    return data;
                }
                
                // TODO: 实现实际的压缩算法（如GZip）
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogError($"压缩消息时发生异常: {ex.Message}");
                return data;
            }
        }
        
        /// <summary>
        /// 解压缩消息数据
        /// </summary>
        /// <param name="compressedData">压缩数据</param>
        /// <returns>解压后的数据</returns>
        public static byte[] DecompressMessage(byte[] compressedData)
        {
            try
            {
                // TODO: 实现实际的解压缩算法
                return compressedData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"解压缩消息时发生异常: {ex.Message}");
                return compressedData;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Steam 网络消息
    /// </summary>
    [Serializable]
    public class SteamNetworkMessage
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public SteamMessageType Type { get; set; }
        
        /// <summary>
        /// 发送者ID
        /// </summary>
        public string SenderId { get; set; }
        
        /// <summary>
        /// 消息载荷
        /// </summary>
        public object Payload { get; set; }
        
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 消息标志
        /// </summary>
        public SteamMessageFlags Flags { get; set; }
        
        /// <summary>
        /// 消息ID（用于去重和确认）
        /// </summary>
        public string MessageId { get; set; }
        
        public SteamNetworkMessage()
        {
            Timestamp = DateTime.UtcNow;
            MessageId = Guid.NewGuid().ToString();
            Flags = SteamMessageFlags.None;
        }
        
        /// <summary>
        /// 创建聊天消息
        /// </summary>
        /// <param name="chatMessage">聊天消息</param>
        /// <param name="senderId">发送者ID</param>
        /// <returns>网络消息</returns>
        public static SteamNetworkMessage CreateChatMessage(Models.ChatMessage chatMessage, string senderId)
        {
            return new SteamNetworkMessage
            {
                Type = SteamMessageType.ChatMessage,
                SenderId = senderId,
                Payload = chatMessage,
                Flags = SteamMessageFlags.RequireAck
            };
        }
        
        /// <summary>
        /// 创建用户加入消息
        /// </summary>
        /// <param name="userInfo">用户信息</param>
        /// <param name="senderId">发送者ID</param>
        /// <returns>网络消息</returns>
        public static SteamNetworkMessage CreateUserJoinedMessage(Models.UserInfo userInfo, string senderId)
        {
            return new SteamNetworkMessage
            {
                Type = SteamMessageType.UserJoined,
                SenderId = senderId,
                Payload = userInfo
            };
        }
        
        /// <summary>
        /// 创建用户离开消息
        /// </summary>
        /// <param name="userInfo">用户信息</param>
        /// <param name="senderId">发送者ID</param>
        /// <returns>网络消息</returns>
        public static SteamNetworkMessage CreateUserLeftMessage(Models.UserInfo userInfo, string senderId)
        {
            return new SteamNetworkMessage
            {
                Type = SteamMessageType.UserLeft,
                SenderId = senderId,
                Payload = userInfo
            };
        }
        
        /// <summary>
        /// 创建心跳消息
        /// </summary>
        /// <param name="senderId">发送者ID</param>
        /// <returns>网络消息</returns>
        public static SteamNetworkMessage CreateHeartbeatMessage(string senderId)
        {
            return new SteamNetworkMessage
            {
                Type = SteamMessageType.Heartbeat,
                SenderId = senderId,
                Payload = new HeartbeatPayload { Timestamp = DateTime.UtcNow }
            };
        }
    }
    
    /// <summary>
    /// Steam 消息类型枚举
    /// </summary>
    public enum SteamMessageType : byte
    {
        /// <summary>
        /// 聊天消息
        /// </summary>
        ChatMessage = 1,
        
        /// <summary>
        /// 历史请求
        /// </summary>
        HistoryRequest = 2,
        
        /// <summary>
        /// 历史响应
        /// </summary>
        HistoryResponse = 3,
        
        /// <summary>
        /// 用户加入
        /// </summary>
        UserJoined = 4,
        
        /// <summary>
        /// 用户离开
        /// </summary>
        UserLeft = 5,
        
        /// <summary>
        /// 心跳消息
        /// </summary>
        Heartbeat = 6,
        
        /// <summary>
        /// 确认消息
        /// </summary>
        Acknowledgment = 7,
        
        /// <summary>
        /// 连接请求
        /// </summary>
        ConnectRequest = 8,
        
        /// <summary>
        /// 连接响应
        /// </summary>
        ConnectResponse = 9
    }
    
    /// <summary>
    /// Steam 消息标志枚举
    /// </summary>
    [Flags]
    public enum SteamMessageFlags : ushort
    {
        /// <summary>
        /// 无标志
        /// </summary>
        None = 0,
        
        /// <summary>
        /// 需要确认
        /// </summary>
        RequireAck = 1,
        
        /// <summary>
        /// 压缩消息
        /// </summary>
        Compressed = 2,
        
        /// <summary>
        /// 加密消息
        /// </summary>
        Encrypted = 4,
        
        /// <summary>
        /// 优先级消息
        /// </summary>
        Priority = 8,
        
        /// <summary>
        /// 系统消息
        /// </summary>
        System = 16
    }
    
    /// <summary>
    /// 历史请求载荷
    /// </summary>
    [Serializable]
    public class HistoryRequestPayload
    {
        /// <summary>
        /// 请求的消息数量
        /// </summary>
        public int MessageCount { get; set; } = 50;
        
        /// <summary>
        /// 起始时间
        /// </summary>
        public DateTime? StartTime { get; set; }
        
        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }
    }
    
    /// <summary>
    /// 历史响应载荷
    /// </summary>
    [Serializable]
    public class HistoryResponsePayload
    {
        /// <summary>
        /// 历史消息列表
        /// </summary>
        public List<Models.ChatMessage> Messages { get; set; } = new List<Models.ChatMessage>();
        
        /// <summary>
        /// 是否还有更多消息
        /// </summary>
        public bool HasMore { get; set; }
        
        /// <summary>
        /// 总消息数
        /// </summary>
        public int TotalCount { get; set; }
    }
    
    /// <summary>
    /// 心跳载荷
    /// </summary>
    [Serializable]
    public class HeartbeatPayload
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 连接质量信息
        /// </summary>
        public int Quality { get; set; } = 100;
    }
}