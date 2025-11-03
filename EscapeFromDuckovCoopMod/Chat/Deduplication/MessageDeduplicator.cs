using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Routing
{
    /// <summary>
    /// 消息去重器
    /// 负责检测和防止重复消息的处理
    /// </summary>
    public class MessageDeduplicator : IDisposable
    {
        #region 字段和属性

        /// <summary>
        /// 消息指纹缓存（消息ID -> 指纹信息）
        /// </summary>
        private readonly Dictionary<string, MessageFingerprint> _messageCache;

        /// <summary>
        /// 内容哈希缓存（内容哈希 -> 消息ID列表）
        /// </summary>
        private readonly Dictionary<string, List<string>> _contentHashCache;

        /// <summary>
        /// 去重配置
        /// </summary>
        private DeduplicationConfig _config;

        /// <summary>
        /// 去重统计信息
        /// </summary>
        private DeduplicationStatistics _statistics;

        /// <summary>
        /// 最后一次清理时间
        /// </summary>
        private DateTime _lastCleanupTime;

        /// <summary>
        /// 哈希算法提供者
        /// </summary>
        private readonly SHA256 _hashProvider;

        /// <summary>
        /// 是否已释放资源
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化消息去重器
        /// </summary>
        public MessageDeduplicator()
        {
            _messageCache = new Dictionary<string, MessageFingerprint>();
            _contentHashCache = new Dictionary<string, List<string>>();
            _config = new DeduplicationConfig();
            _statistics = new DeduplicationStatistics();
            _lastCleanupTime = DateTime.UtcNow;
            _hashProvider = SHA256.Create();

            LogDebug("消息去重器已初始化");
        }

        #endregion

        #region 主要去重方法

        /// <summary>
        /// 检查消息是否重复
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>是否重复</returns>
        public bool IsDuplicate(ChatMessage message)
        {
            if (message == null)
            {
                LogWarning("消息为空，无法检查重复");
                return false;
            }

            try
            {
                _statistics.TotalCheckAttempts++;

                // 定期清理过期缓存
                PerformPeriodicCleanup();

                // 检查消息ID重复
                if (CheckIdDuplicate(message))
                {
                    _statistics.TotalDuplicatesDetected++;
                    _statistics.IdDuplicates++;
                    LogDebug($"检测到ID重复消息: {message.Id}");
                    return true;
                }

                // 检查内容重复
                if (_config.EnableContentDeduplication && CheckContentDuplicate(message))
                {
                    _statistics.TotalDuplicatesDetected++;
                    _statistics.ContentDuplicates++;
                    LogDebug($"检测到内容重复消息: {message.Id}");
                    return true;
                }

                // 检查时间窗口内的重复
                if (_config.EnableTimeWindowDeduplication && CheckTimeWindowDuplicate(message))
                {
                    _statistics.TotalDuplicatesDetected++;
                    _statistics.TimeWindowDuplicates++;
                    LogDebug($"检测到时间窗口重复消息: {message.Id}");
                    return true;
                }

                // 消息不重复，记录到缓存
                RecordMessage(message);
                _statistics.TotalUniqueMessages++;

                return false;
            }
            catch (Exception ex)
            {
                LogError($"检查消息重复时发生异常: {ex.Message}");
                _statistics.TotalCheckErrors++;
                return false; // 出错时不阻止消息
            }
        }

        #endregion

        #region 具体重复检查方法

        /// <summary>
        /// 检查消息ID重复
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>是否重复</returns>
        private bool CheckIdDuplicate(ChatMessage message)
        {
            if (!_config.EnableIdDeduplication)
                return false;

            return _messageCache.ContainsKey(message.Id);
        }

        /// <summary>
        /// 检查内容重复
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>是否重复</returns>
        private bool CheckContentDuplicate(ChatMessage message)
        {
            if (string.IsNullOrEmpty(message.Content))
                return false;

            // 计算内容哈希
            var contentHash = ComputeContentHash(message);

            // 检查是否存在相同内容的消息
            if (_contentHashCache.ContainsKey(contentHash))
            {
                var existingMessageIds = _contentHashCache[contentHash];
                
                // 检查是否来自同一发送者
                if (_config.OnlyDeduplicateSameSender)
                {
                    foreach (var existingId in existingMessageIds)
                    {
                        if (_messageCache.ContainsKey(existingId))
                        {
                            var existingFingerprint = _messageCache[existingId];
                            if (existingFingerprint.SenderId == message.Sender?.SteamId.ToString())
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }

                return true; // 存在相同内容的消息
            }

            return false;
        }

        /// <summary>
        /// 检查时间窗口内的重复
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>是否重复</returns>
        private bool CheckTimeWindowDuplicate(ChatMessage message)
        {
            var currentTime = DateTime.UtcNow;
            var windowStart = currentTime.AddSeconds(-_config.TimeWindowSeconds);

            // 查找时间窗口内的相似消息
            foreach (var fingerprint in _messageCache.Values)
            {
                if (fingerprint.Timestamp >= windowStart && fingerprint.Timestamp <= currentTime)
                {
                    // 检查是否为相似消息
                    if (IsSimilarMessage(message, fingerprint))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 检查是否为相似消息
        /// </summary>
        /// <param name="message">当前消息</param>
        /// <param name="fingerprint">已存在消息的指纹</param>
        /// <returns>是否相似</returns>
        private bool IsSimilarMessage(ChatMessage message, MessageFingerprint fingerprint)
        {
            // 检查发送者
            if (_config.OnlyDeduplicateSameSender)
            {
                var currentSenderId = message.Sender?.SteamId.ToString();
                if (currentSenderId != fingerprint.SenderId)
                {
                    return false;
                }
            }

            // 检查内容相似度
            if (!string.IsNullOrEmpty(message.Content) && !string.IsNullOrEmpty(fingerprint.ContentHash))
            {
                var currentContentHash = ComputeContentHash(message);
                if (currentContentHash == fingerprint.ContentHash)
                {
                    return true;
                }

                // 检查内容相似度（简单的编辑距离）
                if (_config.EnableSimilarityCheck)
                {
                    var similarity = CalculateContentSimilarity(message.Content, fingerprint.OriginalContent);
                    if (similarity >= _config.SimilarityThreshold)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region 消息记录和缓存管理

        /// <summary>
        /// 记录消息到缓存
        /// </summary>
        /// <param name="message">聊天消息</param>
        private void RecordMessage(ChatMessage message)
        {
            try
            {
                var contentHash = ComputeContentHash(message);
                var fingerprint = new MessageFingerprint
                {
                    MessageId = message.Id,
                    ContentHash = contentHash,
                    OriginalContent = _config.StoreOriginalContent ? message.Content : null,
                    SenderId = message.Sender?.SteamId.ToString(),
                    Timestamp = message.Timestamp,
                    RecordedAt = DateTime.UtcNow
                };

                // 记录到消息缓存
                _messageCache[message.Id] = fingerprint;

                // 记录到内容哈希缓存
                if (!_contentHashCache.ContainsKey(contentHash))
                {
                    _contentHashCache[contentHash] = new List<string>();
                }
                _contentHashCache[contentHash].Add(message.Id);

                LogDebug($"已记录消息指纹: {message.Id}");
            }
            catch (Exception ex)
            {
                LogError($"记录消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行定期清理
        /// </summary>
        private void PerformPeriodicCleanup()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCleanupTime).TotalMinutes >= _config.CleanupIntervalMinutes)
            {
                _lastCleanupTime = now;
                CleanupExpiredEntries();
            }
        }

        /// <summary>
        /// 清理过期的缓存条目
        /// </summary>
        private void CleanupExpiredEntries()
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-_config.CacheExpiryMinutes);
                var expiredIds = new List<string>();

                // 查找过期的消息
                foreach (var kvp in _messageCache)
                {
                    if (kvp.Value.RecordedAt < cutoffTime)
                    {
                        expiredIds.Add(kvp.Key);
                    }
                }

                // 移除过期的消息
                foreach (var expiredId in expiredIds)
                {
                    RemoveMessageFromCache(expiredId);
                }

                // 清理空的内容哈希条目
                var emptyHashKeys = _contentHashCache
                    .Where(kvp => kvp.Value.Count == 0)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var emptyKey in emptyHashKeys)
                {
                    _contentHashCache.Remove(emptyKey);
                }

                if (expiredIds.Count > 0)
                {
                    LogDebug($"清理过期缓存条目: {expiredIds.Count} 个");
                    _statistics.TotalCacheCleanups++;
                }
            }
            catch (Exception ex)
            {
                LogError($"清理过期缓存时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 从缓存中移除消息
        /// </summary>
        /// <param name="messageId">消息ID</param>
        private void RemoveMessageFromCache(string messageId)
        {
            if (_messageCache.TryGetValue(messageId, out var fingerprint))
            {
                // 从消息缓存中移除
                _messageCache.Remove(messageId);

                // 从内容哈希缓存中移除
                if (_contentHashCache.ContainsKey(fingerprint.ContentHash))
                {
                    _contentHashCache[fingerprint.ContentHash].Remove(messageId);
                }
            }
        }

        #endregion

        #region 哈希和相似度计算

        /// <summary>
        /// 计算消息内容哈希
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>内容哈希</returns>
        private string ComputeContentHash(ChatMessage message)
        {
            if (string.IsNullOrEmpty(message.Content))
                return string.Empty;

            try
            {
                // 标准化内容（移除多余空白、转换为小写）
                var normalizedContent = NormalizeContent(message.Content);

                // 如果配置了包含发送者信息，则加入发送者ID
                if (_config.IncludeSenderInHash && message.Sender != null)
                {
                    normalizedContent = $"{message.Sender.SteamId}:{normalizedContent}";
                }

                // 计算哈希
                var contentBytes = Encoding.UTF8.GetBytes(normalizedContent);
                var hashBytes = _hashProvider.ComputeHash(contentBytes);
                return Convert.ToBase64String(hashBytes);
            }
            catch (Exception ex)
            {
                LogError($"计算内容哈希时发生异常: {ex.Message}");
                return message.Content?.GetHashCode().ToString() ?? string.Empty;
            }
        }

        /// <summary>
        /// 标准化消息内容
        /// </summary>
        /// <param name="content">原始内容</param>
        /// <returns>标准化内容</returns>
        private string NormalizeContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            // 转换为小写
            content = content.ToLowerInvariant();

            // 移除多余的空白字符
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ");

            // 移除首尾空白
            content = content.Trim();

            return content;
        }

        /// <summary>
        /// 计算内容相似度
        /// </summary>
        /// <param name="content1">内容1</param>
        /// <param name="content2">内容2</param>
        /// <returns>相似度（0-1）</returns>
        private double CalculateContentSimilarity(string content1, string content2)
        {
            if (string.IsNullOrEmpty(content1) || string.IsNullOrEmpty(content2))
                return 0.0;

            // 使用简单的编辑距离算法
            var distance = CalculateLevenshteinDistance(content1, content2);
            var maxLength = Math.Max(content1.Length, content2.Length);

            if (maxLength == 0)
                return 1.0;

            return 1.0 - (double)distance / maxLength;
        }

        /// <summary>
        /// 计算编辑距离
        /// </summary>
        /// <param name="s1">字符串1</param>
        /// <param name="s2">字符串2</param>
        /// <returns>编辑距离</returns>
        private int CalculateLevenshteinDistance(string s1, string s2)
        {
            var len1 = s1.Length;
            var len2 = s2.Length;
            var matrix = new int[len1 + 1, len2 + 1];

            for (int i = 0; i <= len1; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= len2; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[len1, len2];
        }

        #endregion

        #region 配置管理

        /// <summary>
        /// 设置去重配置
        /// </summary>
        /// <param name="config">去重配置</param>
        public void SetDeduplicationConfig(DeduplicationConfig config)
        {
            _config = config ?? new DeduplicationConfig();
            LogDebug("去重配置已更新");
        }

        /// <summary>
        /// 获取去重配置
        /// </summary>
        /// <returns>去重配置</returns>
        public DeduplicationConfig GetDeduplicationConfig()
        {
            return _config;
        }

        #endregion

        #region 统计信息

        /// <summary>
        /// 获取去重统计信息
        /// </summary>
        /// <returns>去重统计信息</returns>
        public DeduplicationStatistics GetStatistics()
        {
            return _statistics.Clone();
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            _statistics.Reset();
            LogDebug("去重统计信息已重置");
        }

        /// <summary>
        /// 获取缓存信息
        /// </summary>
        /// <returns>缓存信息</returns>
        public CacheInfo GetCacheInfo()
        {
            return new CacheInfo
            {
                MessageCacheSize = _messageCache.Count,
                ContentHashCacheSize = _contentHashCache.Count,
                LastCleanupTime = _lastCleanupTime,
                CacheExpiryMinutes = _config.CacheExpiryMinutes
            };
        }

        #endregion

        #region 清理和资源释放

        /// <summary>
        /// 清理所有缓存
        /// </summary>
        public void Cleanup()
        {
            try
            {
                _messageCache.Clear();
                _contentHashCache.Clear();
                LogDebug("去重器缓存已清理");
            }
            catch (Exception ex)
            {
                LogError($"清理去重器时发生异常: {ex.Message}");
            }
        }

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
                    Cleanup();
                    _hashProvider?.Dispose();
                    LogDebug("消息去重器资源已释放");
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~MessageDeduplicator()
        {
            Dispose(false);
        }

        #endregion

        #region 日志方法

        private void LogDebug(string message)
        {
            Debug.Log($"[MessageDeduplicator][DEBUG] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[MessageDeduplicator] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[MessageDeduplicator] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 消息指纹信息
    /// </summary>
    public class MessageFingerprint
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// 内容哈希
        /// </summary>
        public string ContentHash { get; set; }

        /// <summary>
        /// 原始内容（可选）
        /// </summary>
        public string OriginalContent { get; set; }

        /// <summary>
        /// 发送者ID
        /// </summary>
        public string SenderId { get; set; }

        /// <summary>
        /// 消息时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 记录时间
        /// </summary>
        public DateTime RecordedAt { get; set; }
    }

    /// <summary>
    /// 去重配置
    /// </summary>
    public class DeduplicationConfig
    {
        /// <summary>
        /// 是否启用ID去重
        /// </summary>
        public bool EnableIdDeduplication { get; set; } = true;

        /// <summary>
        /// 是否启用内容去重
        /// </summary>
        public bool EnableContentDeduplication { get; set; } = true;

        /// <summary>
        /// 是否启用时间窗口去重
        /// </summary>
        public bool EnableTimeWindowDeduplication { get; set; } = true;

        /// <summary>
        /// 是否启用相似度检查
        /// </summary>
        public bool EnableSimilarityCheck { get; set; } = false;

        /// <summary>
        /// 只对同一发送者去重
        /// </summary>
        public bool OnlyDeduplicateSameSender { get; set; } = false;

        /// <summary>
        /// 哈希计算时是否包含发送者信息
        /// </summary>
        public bool IncludeSenderInHash { get; set; } = false;

        /// <summary>
        /// 是否存储原始内容
        /// </summary>
        public bool StoreOriginalContent { get; set; } = false;

        /// <summary>
        /// 时间窗口大小（秒）
        /// </summary>
        public int TimeWindowSeconds { get; set; } = 60;

        /// <summary>
        /// 相似度阈值（0-1）
        /// </summary>
        public double SimilarityThreshold { get; set; } = 0.8;

        /// <summary>
        /// 缓存过期时间（分钟）
        /// </summary>
        public int CacheExpiryMinutes { get; set; } = 60;

        /// <summary>
        /// 清理间隔（分钟）
        /// </summary>
        public int CleanupIntervalMinutes { get; set; } = 10;
    }

    /// <summary>
    /// 去重统计信息
    /// </summary>
    public class DeduplicationStatistics
    {
        /// <summary>
        /// 总检查尝试次数
        /// </summary>
        public long TotalCheckAttempts { get; set; }

        /// <summary>
        /// 总检测到的重复消息数
        /// </summary>
        public long TotalDuplicatesDetected { get; set; }

        /// <summary>
        /// 总唯一消息数
        /// </summary>
        public long TotalUniqueMessages { get; set; }

        /// <summary>
        /// ID重复数
        /// </summary>
        public long IdDuplicates { get; set; }

        /// <summary>
        /// 内容重复数
        /// </summary>
        public long ContentDuplicates { get; set; }

        /// <summary>
        /// 时间窗口重复数
        /// </summary>
        public long TimeWindowDuplicates { get; set; }

        /// <summary>
        /// 总检查错误次数
        /// </summary>
        public long TotalCheckErrors { get; set; }

        /// <summary>
        /// 总缓存清理次数
        /// </summary>
        public long TotalCacheCleanups { get; set; }

        /// <summary>
        /// 重复检测率
        /// </summary>
        public double DuplicateRate => TotalCheckAttempts > 0 ? 
            (double)TotalDuplicatesDetected / TotalCheckAttempts * 100 : 0;

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            TotalCheckAttempts = 0;
            TotalDuplicatesDetected = 0;
            TotalUniqueMessages = 0;
            IdDuplicates = 0;
            ContentDuplicates = 0;
            TimeWindowDuplicates = 0;
            TotalCheckErrors = 0;
            TotalCacheCleanups = 0;
        }

        /// <summary>
        /// 克隆统计信息
        /// </summary>
        /// <returns>统计信息副本</returns>
        public DeduplicationStatistics Clone()
        {
            return new DeduplicationStatistics
            {
                TotalCheckAttempts = TotalCheckAttempts,
                TotalDuplicatesDetected = TotalDuplicatesDetected,
                TotalUniqueMessages = TotalUniqueMessages,
                IdDuplicates = IdDuplicates,
                ContentDuplicates = ContentDuplicates,
                TimeWindowDuplicates = TimeWindowDuplicates,
                TotalCheckErrors = TotalCheckErrors,
                TotalCacheCleanups = TotalCacheCleanups
            };
        }
    }

    /// <summary>
    /// 缓存信息
    /// </summary>
    public class CacheInfo
    {
        /// <summary>
        /// 消息缓存大小
        /// </summary>
        public int MessageCacheSize { get; set; }

        /// <summary>
        /// 内容哈希缓存大小
        /// </summary>
        public int ContentHashCacheSize { get; set; }

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
            return $"消息缓存: {MessageCacheSize}, 哈希缓存: {ContentHashCacheSize}, " +
                   $"最后清理: {LastCleanupTime:HH:mm:ss}, 过期时间: {CacheExpiryMinutes}分钟";
        }
    }
}