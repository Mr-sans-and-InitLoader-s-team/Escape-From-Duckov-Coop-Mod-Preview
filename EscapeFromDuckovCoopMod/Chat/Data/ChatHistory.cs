using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Data
{
    /// <summary>
    /// 聊天历史管理类
    /// </summary>
    [Serializable]
    public class ChatHistory
    {
        [SerializeField] private List<ChatMessage> messages;
        [SerializeField] private int maxMessages;
        [SerializeField] private DateTime createdAt;
        [SerializeField] private DateTime lastUpdated;

        /// <summary>
        /// 消息列表
        /// </summary>
        public IReadOnlyList<ChatMessage> Messages => messages?.AsReadOnly() ?? new List<ChatMessage>().AsReadOnly();

        /// <summary>
        /// 最大消息数量
        /// </summary>
        public int MaxMessages 
        { 
            get => maxMessages; 
            set => maxMessages = Math.Max(1, value); 
        }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt => createdAt;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated => lastUpdated;

        /// <summary>
        /// 当前消息数量
        /// </summary>
        public int Count => messages?.Count ?? 0;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="maxMessages">最大消息数量</param>
        public ChatHistory(int maxMessages = 100)
        {
            this.maxMessages = Math.Max(1, maxMessages);
            messages = new List<ChatMessage>();
            createdAt = DateTime.UtcNow;
            lastUpdated = DateTime.UtcNow;
        }

        /// <summary>
        /// 添加消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void AddMessage(ChatMessage message)
        {
            if (message == null || !message.IsValid())
            {
                Debug.LogWarning("尝试添加无效消息到历史记录");
                return;
            }

            messages.Add(message);
            lastUpdated = DateTime.UtcNow;

            // 保持消息数量限制
            if (messages.Count > maxMessages)
            {
                var removeCount = messages.Count - maxMessages;
                messages.RemoveRange(0, removeCount);
            }
        }

        /// <summary>
        /// 批量添加消息
        /// </summary>
        /// <param name="messagesToAdd">消息列表</param>
        public void AddMessages(IEnumerable<ChatMessage> messagesToAdd)
        {
            if (messagesToAdd == null)
                return;

            foreach (var message in messagesToAdd)
            {
                if (message != null && message.IsValid())
                {
                    messages.Add(message);
                }
            }

            lastUpdated = DateTime.UtcNow;

            // 保持消息数量限制
            if (messages.Count > maxMessages)
            {
                var removeCount = messages.Count - maxMessages;
                messages.RemoveRange(0, removeCount);
            }
        }

        /// <summary>
        /// 获取最近的消息
        /// </summary>
        /// <param name="count">消息数量</param>
        /// <returns>最近的消息列表</returns>
        public List<ChatMessage> GetRecentMessages(int count)
        {
            if (count <= 0 || messages == null || messages.Count == 0)
                return new List<ChatMessage>();

            return messages.TakeLast(Math.Min(count, messages.Count)).ToList();
        }

        /// <summary>
        /// 获取指定时间范围内的消息
        /// </summary>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>时间范围内的消息列表</returns>
        public List<ChatMessage> GetMessagesByTimeRange(DateTime startTime, DateTime endTime)
        {
            if (messages == null)
                return new List<ChatMessage>();

            return messages.Where(m => m.Timestamp >= startTime && m.Timestamp <= endTime).ToList();
        }

        /// <summary>
        /// 获取指定用户的消息
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>用户的消息列表</returns>
        public List<ChatMessage> GetMessagesByUser(ulong userId)
        {
            if (messages == null)
                return new List<ChatMessage>();

            return messages.Where(m => m.Sender?.SteamId == userId).ToList();
        }

        /// <summary>
        /// 搜索包含指定内容的消息
        /// </summary>
        /// <param name="searchText">搜索文本</param>
        /// <param name="ignoreCase">是否忽略大小写</param>
        /// <returns>匹配的消息列表</returns>
        public List<ChatMessage> SearchMessages(string searchText, bool ignoreCase = true)
        {
            if (string.IsNullOrEmpty(searchText) || messages == null)
                return new List<ChatMessage>();

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return messages.Where(m => !string.IsNullOrEmpty(m.Content) && 
                                      m.Content.Contains(searchText, comparison)).ToList();
        }

        /// <summary>
        /// 清空所有消息
        /// </summary>
        public void Clear()
        {
            messages?.Clear();
            lastUpdated = DateTime.UtcNow;
        }

        /// <summary>
        /// 移除指定消息
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <returns>是否移除成功</returns>
        public bool RemoveMessage(string messageId)
        {
            if (string.IsNullOrEmpty(messageId) || messages == null)
                return false;

            var messageToRemove = messages.FirstOrDefault(m => m.Id == messageId);
            if (messageToRemove != null)
            {
                messages.Remove(messageToRemove);
                lastUpdated = DateTime.UtcNow;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 移除指定时间之前的消息
        /// </summary>
        /// <param name="cutoffTime">截止时间</param>
        /// <returns>移除的消息数量</returns>
        public int RemoveMessagesOlderThan(DateTime cutoffTime)
        {
            if (messages == null)
                return 0;

            var messagesToRemove = messages.Where(m => m.Timestamp < cutoffTime).ToList();
            foreach (var message in messagesToRemove)
            {
                messages.Remove(message);
            }

            if (messagesToRemove.Count > 0)
            {
                lastUpdated = DateTime.UtcNow;
            }

            return messagesToRemove.Count;
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        /// <returns>历史统计信息</returns>
        public ChatHistoryStats GetStats()
        {
            if (messages == null || messages.Count == 0)
            {
                return new ChatHistoryStats();
            }

            var stats = new ChatHistoryStats
            {
                TotalMessages = messages.Count,
                OldestMessageTime = messages.Min(m => m.Timestamp),
                NewestMessageTime = messages.Max(m => m.Timestamp),
                UniqueUsers = messages.Where(m => m.Sender != null)
                                   .Select(m => m.Sender.SteamId)
                                   .Distinct()
                                   .Count(),
                MessagesByType = messages.GroupBy(m => m.Type)
                                       .ToDictionary(g => g.Key, g => g.Count())
            };

            return stats;
        }

        /// <summary>
        /// 序列化为JSON字符串
        /// </summary>
        /// <returns>JSON字符串</returns>
        public string ToJson()
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    DateFormatHandling = DateFormatHandling.IsoDateFormat,
                    Formatting = Formatting.None
                };
                return JsonConvert.SerializeObject(this, settings);
            }
            catch (Exception ex)
            {
                Debug.LogError($"序列化聊天历史失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 从JSON字符串反序列化
        /// </summary>
        /// <param name="json">JSON字符串</param>
        /// <returns>聊天历史对象</returns>
        public static ChatHistory FromJson(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                    return new ChatHistory();

                var settings = new JsonSerializerSettings
                {
                    DateFormatHandling = DateFormatHandling.IsoDateFormat
                };
                return JsonConvert.DeserializeObject<ChatHistory>(json, settings) ?? new ChatHistory();
            }
            catch (Exception ex)
            {
                Debug.LogError($"反序列化聊天历史失败: {ex.Message}");
                return new ChatHistory();
            }
        }

        /// <summary>
        /// 保存到文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否保存成功</returns>
        public bool SaveToFile(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = ToJson();
                if (string.IsNullOrEmpty(json))
                    return false;

                File.WriteAllText(filePath, json);
                Debug.Log($"聊天历史已保存到: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"保存聊天历史失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从文件加载
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>聊天历史对象</returns>
        public static ChatHistory LoadFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"聊天历史文件不存在: {filePath}");
                    return new ChatHistory();
                }

                var json = File.ReadAllText(filePath);
                var history = FromJson(json);
                Debug.Log($"聊天历史已从文件加载: {filePath}，消息数量: {history.Count}");
                return history;
            }
            catch (Exception ex)
            {
                Debug.LogError($"加载聊天历史失败: {ex.Message}");
                return new ChatHistory();
            }
        }

        /// <summary>
        /// 合并另一个聊天历史
        /// </summary>
        /// <param name="other">另一个聊天历史</param>
        public void Merge(ChatHistory other)
        {
            if (other?.messages == null)
                return;

            // 按时间戳合并消息，避免重复
            var existingIds = new HashSet<string>(messages.Select(m => m.Id));
            var newMessages = other.messages.Where(m => !existingIds.Contains(m.Id)).ToList();

            AddMessages(newMessages);

            // 重新排序消息
            messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            Debug.Log($"合并聊天历史完成，新增消息: {newMessages.Count}");
        }

        /// <summary>
        /// 创建副本
        /// </summary>
        /// <returns>聊天历史副本</returns>
        public ChatHistory Clone()
        {
            var clone = new ChatHistory(maxMessages)
            {
                createdAt = createdAt
            };

            if (messages != null)
            {
                clone.messages.AddRange(messages);
            }

            clone.lastUpdated = lastUpdated;
            return clone;
        }
    }

    /// <summary>
    /// 聊天历史统计信息
    /// </summary>
    [Serializable]
    public class ChatHistoryStats
    {
        /// <summary>
        /// 总消息数
        /// </summary>
        public int TotalMessages { get; set; }

        /// <summary>
        /// 最旧消息时间
        /// </summary>
        public DateTime OldestMessageTime { get; set; }

        /// <summary>
        /// 最新消息时间
        /// </summary>
        public DateTime NewestMessageTime { get; set; }

        /// <summary>
        /// 唯一用户数
        /// </summary>
        public int UniqueUsers { get; set; }

        /// <summary>
        /// 按类型分组的消息数量
        /// </summary>
        public Dictionary<MessageType, int> MessagesByType { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ChatHistoryStats()
        {
            MessagesByType = new Dictionary<MessageType, int>();
        }
    }
}