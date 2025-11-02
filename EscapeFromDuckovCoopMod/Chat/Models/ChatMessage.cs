using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace EscapeFromDuckovCoopMod.Chat.Models
{
    /// <summary>
    /// 聊天消息数据模型
    /// </summary>
    [Serializable]
    public class ChatMessage
    {
        /// <summary>
        /// 消息唯一标识符
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 发送者信息
        /// </summary>
        public UserInfo Sender { get; set; }

        /// <summary>
        /// 消息时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageType Type { get; set; }

        /// <summary>
        /// 消息元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ChatMessage()
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.UtcNow;
            Metadata = new Dictionary<string, object>();
            Type = MessageType.Normal;
        }

        /// <summary>
        /// 带参数的构造函数
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <param name="sender">发送者信息</param>
        /// <param name="type">消息类型</param>
        public ChatMessage(string content, UserInfo sender, MessageType type = MessageType.Normal) : this()
        {
            Content = content;
            Sender = sender;
            Type = type;
        }

        /// <summary>
        /// 序列化为JSON字符串
        /// </summary>
        /// <returns>JSON字符串</returns>
        public string ToJson()
        {
            try
            {
                return JsonConvert.SerializeObject(this);
            }
            catch (Exception ex)
            {
                Debug.LogError($"聊天消息序列化失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 从JSON字符串反序列化
        /// </summary>
        /// <param name="json">JSON字符串</param>
        /// <returns>聊天消息对象</returns>
        public static ChatMessage FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<ChatMessage>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"聊天消息反序列化失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 验证消息是否有效
        /// </summary>
        /// <returns>是否有效</returns>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Content) && 
                   Sender != null && 
                   !string.IsNullOrEmpty(Id);
        }

        /// <summary>
        /// 获取格式化的显示文本
        /// </summary>
        /// <returns>格式化的消息文本</returns>
        public string GetDisplayText()
        {
            var timeStr = Timestamp.ToString("HH:mm");
            
            switch (Type)
            {
                case MessageType.System:
                    return $"[{timeStr}] [系统] {Content}";
                case MessageType.Join:
                    return $"[{timeStr}] [系统] {Sender?.UserName ?? "未知用户"} 加入了房间";
                case MessageType.Leave:
                    return $"[{timeStr}] [系统] {Sender?.UserName ?? "未知用户"} 离开了房间";
                case MessageType.Error:
                    return $"[{timeStr}] [错误] {Content}";
                default:
                    return $"[{timeStr}] {Sender?.UserName ?? "未知用户"}: {Content}";
            }
        }
    }
}