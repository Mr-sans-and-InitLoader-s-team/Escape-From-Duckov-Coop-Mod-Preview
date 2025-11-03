using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Managers
{
    /// <summary>
    /// 消息转换器
    /// 负责在本地消息、网络消息和显示消息之间进行转换
    /// </summary>
    public class MessageConverter
    {
        #region 字段和属性

        /// <summary>
        /// 消息重复检测缓存
        /// </summary>
        private readonly Dictionary<string, DateTime> _messageCache = new Dictionary<string, DateTime>();

        /// <summary>
        /// 缓存清理间隔（分钟）
        /// </summary>
        private const int CACHE_CLEANUP_INTERVAL_MINUTES = 10;

        /// <summary>
        /// 消息缓存过期时间（分钟）
        /// </summary>
        private const int MESSAGE_CACHE_EXPIRY_MINUTES = 30;

        /// <summary>
        /// 最后一次缓存清理时间
        /// </summary>
        private DateTime _lastCacheCleanup = DateTime.UtcNow;

        /// <summary>
        /// 是否启用调试日志
        /// </summary>
        private bool _enableDebugLog = true;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化消息转换器
        /// </summary>
        public MessageConverter()
        {
            LogDebug("消息转换器已初始化");
        }

        #endregion

        #region 本地消息转换

        /// <summary>
        /// 将本地消息转换为网络消息
        /// </summary>
        /// <param name="localMessage">本地消息</param>
        /// <returns>网络消息</returns>
        public ChatMessage ConvertLocalToNetwork(ChatMessage localMessage)
        {
            if (localMessage == null)
            {
                LogWarning("本地消息为空，无法转换");
                return null;
            }

            try
            {
                // 创建网络消息副本
                var networkMessage = new ChatMessage
                {
                    Id = localMessage.Id,
                    Content = ValidateAndSanitizeContent(localMessage.Content),
                    Sender = CloneUserInfo(localMessage.Sender),
                    Type = localMessage.Type,
                    Timestamp = localMessage.Timestamp,
                    Metadata = CloneMetadata(localMessage.Metadata)
                };

                // 添加网络传输相关的元数据
                AddNetworkMetadata(networkMessage);

                LogDebug($"本地消息已转换为网络消息: {networkMessage.Id}");
                return networkMessage;
            }
            catch (Exception ex)
            {
                LogError($"转换本地消息到网络消息时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 批量转换本地消息为网络消息
        /// </summary>
        /// <param name="localMessages">本地消息列表</param>
        /// <returns>网络消息列表</returns>
        public List<ChatMessage> ConvertLocalToNetwork(List<ChatMessage> localMessages)
        {
            if (localMessages == null || localMessages.Count == 0)
            {
                return new List<ChatMessage>();
            }

            var networkMessages = new List<ChatMessage>();

            foreach (var localMessage in localMessages)
            {
                var networkMessage = ConvertLocalToNetwork(localMessage);
                if (networkMessage != null)
                {
                    networkMessages.Add(networkMessage);
                }
            }

            LogDebug($"批量转换本地消息: {localMessages.Count} -> {networkMessages.Count}");
            return networkMessages;
        }

        #endregion

        #region 网络消息转换

        /// <summary>
        /// 将网络消息转换为显示消息
        /// </summary>
        /// <param name="networkMessage">网络消息</param>
        /// <returns>显示消息</returns>
        public ChatMessage ConvertNetworkToDisplay(ChatMessage networkMessage)
        {
            if (networkMessage == null)
            {
                LogWarning("网络消息为空，无法转换");
                return null;
            }

            try
            {
                // 检查消息重复
                if (IsDuplicateMessage(networkMessage))
                {
                    LogDebug($"检测到重复消息，跳过: {networkMessage.Id}");
                    return null;
                }

                // 验证消息格式
                if (!ValidateNetworkMessage(networkMessage))
                {
                    LogWarning($"网络消息验证失败: {networkMessage.Id}");
                    return null;
                }

                // 创建显示消息
                var displayMessage = new ChatMessage
                {
                    Id = networkMessage.Id,
                    Content = ProcessDisplayContent(networkMessage.Content),
                    Sender = ProcessUserInfo(networkMessage.Sender),
                    Type = networkMessage.Type,
                    Timestamp = networkMessage.Timestamp,
                    Metadata = ProcessDisplayMetadata(networkMessage.Metadata)
                };

                // 记录消息到缓存
                RecordMessageInCache(networkMessage);

                LogDebug($"网络消息已转换为显示消息: {displayMessage.Id}");
                return displayMessage;
            }
            catch (Exception ex)
            {
                LogError($"转换网络消息到显示消息时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 批量转换网络消息为显示消息
        /// </summary>
        /// <param name="networkMessages">网络消息列表</param>
        /// <returns>显示消息列表</returns>
        public List<ChatMessage> ConvertNetworkToDisplay(List<ChatMessage> networkMessages)
        {
            if (networkMessages == null || networkMessages.Count == 0)
            {
                return new List<ChatMessage>();
            }

            var displayMessages = new List<ChatMessage>();

            foreach (var networkMessage in networkMessages)
            {
                var displayMessage = ConvertNetworkToDisplay(networkMessage);
                if (displayMessage != null)
                {
                    displayMessages.Add(displayMessage);
                }
            }

            LogDebug($"批量转换网络消息: {networkMessages.Count} -> {displayMessages.Count}");
            return displayMessages;
        }

        #endregion

        #region 消息验证和过滤

        /// <summary>
        /// 验证和清理消息内容
        /// </summary>
        /// <param name="content">原始内容</param>
        /// <returns>清理后的内容</returns>
        private string ValidateAndSanitizeContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            // 移除危险字符
            content = content.Replace('\0', ' '); // 移除空字符
            content = content.Replace('\r', ' '); // 移除回车符
            content = content.Replace('\n', ' '); // 移除换行符
            content = content.Replace('\t', ' '); // 移除制表符

            // 限制长度
            const int maxLength = 500;
            if (content.Length > maxLength)
            {
                content = content.Substring(0, maxLength) + "...";
            }

            // 移除首尾空白
            content = content.Trim();

            return content;
        }

        /// <summary>
        /// 验证网络消息格式
        /// </summary>
        /// <param name="message">网络消息</param>
        /// <returns>是否有效</returns>
        private bool ValidateNetworkMessage(ChatMessage message)
        {
            if (message == null)
                return false;

            // 检查必要字段
            if (string.IsNullOrEmpty(message.Id))
            {
                LogWarning("网络消息缺少ID");
                return false;
            }

            if (message.Sender == null)
            {
                LogWarning("网络消息缺少发送者信息");
                return false;
            }

            if (string.IsNullOrEmpty(message.Sender.UserName))
            {
                LogWarning("网络消息发送者缺少用户名");
                return false;
            }

            // 检查时间戳合理性
            var now = DateTime.UtcNow;
            var timeDiff = Math.Abs((now - message.Timestamp).TotalMinutes);
            if (timeDiff > 60) // 超过1小时的消息可能有问题
            {
                LogWarning($"网络消息时间戳异常: {message.Timestamp}, 当前时间: {now}");
                // 不直接拒绝，但记录警告
            }

            return true;
        }

        /// <summary>
        /// 检查是否为重复消息
        /// </summary>
        /// <param name="message">消息</param>
        /// <returns>是否重复</returns>
        private bool IsDuplicateMessage(ChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Id))
                return false;

            // 清理过期缓存
            CleanupExpiredCache();

            // 检查消息是否已存在
            if (_messageCache.ContainsKey(message.Id))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 将消息记录到缓存
        /// </summary>
        /// <param name="message">消息</param>
        private void RecordMessageInCache(ChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Id))
                return;

            _messageCache[message.Id] = DateTime.UtcNow;
        }

        /// <summary>
        /// 清理过期的消息缓存
        /// </summary>
        private void CleanupExpiredCache()
        {
            var now = DateTime.UtcNow;

            // 检查是否需要清理
            if ((now - _lastCacheCleanup).TotalMinutes < CACHE_CLEANUP_INTERVAL_MINUTES)
                return;

            _lastCacheCleanup = now;

            // 移除过期的缓存项
            var expiredKeys = _messageCache
                .Where(kvp => (now - kvp.Value).TotalMinutes > MESSAGE_CACHE_EXPIRY_MINUTES)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _messageCache.Remove(key);
            }

            if (expiredKeys.Count > 0)
            {
                LogDebug($"清理过期消息缓存: {expiredKeys.Count} 项");
            }
        }

        #endregion

        #region 内容处理

        /// <summary>
        /// 处理显示内容
        /// </summary>
        /// <param name="content">原始内容</param>
        /// <returns>处理后的内容</returns>
        private string ProcessDisplayContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            // 这里可以添加更多的显示处理逻辑
            // 例如：表情符号转换、链接处理、特殊格式等

            return content.Trim();
        }

        /// <summary>
        /// 处理用户信息
        /// </summary>
        /// <param name="userInfo">原始用户信息</param>
        /// <returns>处理后的用户信息</returns>
        private UserInfo ProcessUserInfo(UserInfo userInfo)
        {
            if (userInfo == null)
                return null;

            // 创建用户信息副本并进行处理
            var processedUser = CloneUserInfo(userInfo);

            // 确保用户名不为空
            if (string.IsNullOrEmpty(processedUser.UserName))
            {
                processedUser.UserName = $"Player_{processedUser.SteamId}";
            }

            // 确保显示名不为空
            if (string.IsNullOrEmpty(processedUser.DisplayName))
            {
                processedUser.DisplayName = processedUser.UserName;
            }

            return processedUser;
        }

        /// <summary>
        /// 处理显示元数据
        /// </summary>
        /// <param name="metadata">原始元数据</param>
        /// <returns>处理后的元数据</returns>
        private Dictionary<string, object> ProcessDisplayMetadata(Dictionary<string, object> metadata)
        {
            if (metadata == null)
                return new Dictionary<string, object>();

            var processedMetadata = new Dictionary<string, object>();

            foreach (var kvp in metadata)
            {
                // 过滤敏感或不需要的元数据
                if (IsDisplayMetadata(kvp.Key))
                {
                    processedMetadata[kvp.Key] = kvp.Value;
                }
            }

            return processedMetadata;
        }

        /// <summary>
        /// 检查是否为显示相关的元数据
        /// </summary>
        /// <param name="key">元数据键</param>
        /// <returns>是否为显示元数据</returns>
        private bool IsDisplayMetadata(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            // 定义允许显示的元数据键
            var allowedKeys = new[]
            {
                "DisplayColor",
                "MessageStyle",
                "Priority",
                "IsEdited",
                "EditTime"
            };

            return allowedKeys.Contains(key);
        }

        #endregion

        #region 网络元数据处理

        /// <summary>
        /// 添加网络传输相关的元数据
        /// </summary>
        /// <param name="message">消息</param>
        private void AddNetworkMetadata(ChatMessage message)
        {
            if (message.Metadata == null)
            {
                message.Metadata = new Dictionary<string, object>();
            }

            // 添加网络传输时间戳
            message.Metadata["NetworkTimestamp"] = DateTime.UtcNow;

            // 添加消息版本信息
            message.Metadata["MessageVersion"] = "1.0";

            // 添加传输标识
            message.Metadata["TransmissionId"] = Guid.NewGuid().ToString();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 克隆用户信息
        /// </summary>
        /// <param name="original">原始用户信息</param>
        /// <returns>克隆的用户信息</returns>
        private UserInfo CloneUserInfo(UserInfo original)
        {
            if (original == null)
                return null;

            return new UserInfo
            {
                SteamId = original.SteamId,
                UserName = original.UserName,
                DisplayName = original.DisplayName,
                Status = original.Status,
                LastSeen = original.LastSeen
            };
        }

        /// <summary>
        /// 克隆元数据
        /// </summary>
        /// <param name="original">原始元数据</param>
        /// <returns>克隆的元数据</returns>
        private Dictionary<string, object> CloneMetadata(Dictionary<string, object> original)
        {
            if (original == null)
                return new Dictionary<string, object>();

            var cloned = new Dictionary<string, object>();
            foreach (var kvp in original)
            {
                cloned[kvp.Key] = kvp.Value;
            }

            return cloned;
        }

        #endregion

        #region 公共接口方法

        /// <summary>
        /// 清理消息缓存
        /// </summary>
        public void ClearMessageCache()
        {
            _messageCache.Clear();
            LogDebug("消息缓存已清空");
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        /// <returns>缓存统计信息</returns>
        public MessageCacheStats GetCacheStats()
        {
            CleanupExpiredCache();

            return new MessageCacheStats
            {
                TotalCachedMessages = _messageCache.Count,
                LastCleanupTime = _lastCacheCleanup,
                CacheExpiryMinutes = MESSAGE_CACHE_EXPIRY_MINUTES
            };
        }

        /// <summary>
        /// 设置调试日志开关
        /// </summary>
        /// <param name="enabled">是否启用</param>
        public void SetDebugLogEnabled(bool enabled)
        {
            _enableDebugLog = enabled;
        }

        #endregion

        #region 日志方法

        private void LogDebug(string message)
        {
            if (_enableDebugLog)
            {
                Debug.Log($"[MessageConverter][DEBUG] {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[MessageConverter] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[MessageConverter] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 消息缓存统计信息
    /// </summary>
    public class MessageCacheStats
    {
        /// <summary>
        /// 缓存的消息总数
        /// </summary>
        public int TotalCachedMessages { get; set; }

        /// <summary>
        /// 最后清理时间
        /// </summary>
        public DateTime LastCleanupTime { get; set; }

        /// <summary>
        /// 缓存过期时间（分钟）
        /// </summary>
        public int CacheExpiryMinutes { get; set; }

        public override string ToString()
        {
            return $"缓存消息: {TotalCachedMessages}, 最后清理: {LastCleanupTime:HH:mm:ss}, 过期时间: {CacheExpiryMinutes}分钟";
        }
    }
}