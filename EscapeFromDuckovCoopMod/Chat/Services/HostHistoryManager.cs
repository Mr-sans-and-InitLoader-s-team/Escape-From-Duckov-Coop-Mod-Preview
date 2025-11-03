using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using EscapeFromDuckovCoopMod.Chat.Data;

namespace EscapeFromDuckovCoopMod.Chat.Services
{
    /// <summary>
    /// 主机端聊天历史管理器
    /// 提供历史存储、压缩优化、同步准备等功能
    /// </summary>
    public class HostHistoryManager : MonoBehaviour
    {
        #region 字段和属性

        /// <summary>
        /// 聊天历史
        /// </summary>
        private ChatHistory _chatHistory;

        /// <summary>
        /// 历史配置
        /// </summary>
        private HostHistoryConfig _config;

        /// <summary>
        /// 压缩历史缓存
        /// </summary>
        private byte[] _compressedHistoryCache;

        /// <summary>
        /// 缓存是否有效
        /// </summary>
        private bool _isCacheValid;

        /// <summary>
        /// 最后压缩时间
        /// </summary>
        private DateTime _lastCompressionTime;

        /// <summary>
        /// 历史文件路径
        /// </summary>
        private string _historyFilePath;

        /// <summary>
        /// 备份文件路径
        /// </summary>
        private string _backupFilePath;

        /// <summary>
        /// 自动清理定时器
        /// </summary>
        private float _cleanupTimer;

        /// <summary>
        /// 自动备份定时器
        /// </summary>
        private float _backupTimer;

        /// <summary>
        /// 是否有未保存的更改
        /// </summary>
        private bool _hasUnsavedChanges;

        #endregion

        #region 事件

        /// <summary>
        /// 历史消息添加事件
        /// </summary>
        public event Action<ChatMessage> OnMessageAdded;

        /// <summary>
        /// 历史清理事件
        /// </summary>
        public event Action<int> OnHistoryCleaned;

        /// <summary>
        /// 历史备份事件
        /// </summary>
        public event Action<bool> OnHistoryBackedUp;

        /// <summary>
        /// 历史压缩事件
        /// </summary>
        public event Action<int, int> OnHistoryCompressed; // 原始大小, 压缩后大小

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化主机历史管理器
        /// </summary>
        /// <param name="config">历史配置</param>
        public void Initialize(HostHistoryConfig config = null)
        {
            try
            {
                _config = config ?? new HostHistoryConfig();
                
                // 设置文件路径
                SetupFilePaths();

                // 初始化聊天历史
                _chatHistory = new ChatHistory(_config.MaxHistoryMessages);

                // 加载现有历史
                LoadHistory();

                // 重置定时器
                _cleanupTimer = 0f;
                _backupTimer = 0f;

                LogInfo("主机历史管理器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化主机历史管理器时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置文件路径
        /// </summary>
        private void SetupFilePaths()
        {
            var dataPath = Path.Combine(Application.persistentDataPath, "ChatData", "Host");
            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }

            _historyFilePath = Path.Combine(dataPath, "host_chat_history.json");
            _backupFilePath = Path.Combine(dataPath, "Backups");

            if (!Directory.Exists(_backupFilePath))
            {
                Directory.CreateDirectory(_backupFilePath);
            }
        }

        #endregion

        #region 历史管理

        /// <summary>
        /// 添加消息到历史
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void AddMessage(ChatMessage message)
        {
            try
            {
                if (message == null || !message.IsValid())
                {
                    LogWarning("尝试添加无效消息到历史");
                    return;
                }

                _chatHistory.AddMessage(message);
                _hasUnsavedChanges = true;
                _isCacheValid = false; // 缓存失效

                // 触发消息添加事件
                OnMessageAdded?.Invoke(message);

                LogDebug($"消息已添加到主机历史: {message.Sender?.UserName} -> {message.Content}");
            }
            catch (Exception ex)
            {
                LogError($"添加消息到历史时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 批量添加消息到历史
        /// </summary>
        /// <param name="messages">消息列表</param>
        public void AddMessages(IEnumerable<ChatMessage> messages)
        {
            try
            {
                if (messages == null)
                    return;

                int addedCount = 0;
                foreach (var message in messages)
                {
                    if (message != null && message.IsValid())
                    {
                        _chatHistory.AddMessage(message);
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    _hasUnsavedChanges = true;
                    _isCacheValid = false;
                    LogDebug($"批量添加了 {addedCount} 条消息到主机历史");
                }
            }
            catch (Exception ex)
            {
                LogError($"批量添加消息到历史时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取完整聊天历史
        /// </summary>
        /// <returns>聊天历史</returns>
        public ChatHistory GetFullHistory()
        {
            return _chatHistory?.Clone();
        }

        /// <summary>
        /// 获取最近的消息
        /// </summary>
        /// <param name="count">消息数量</param>
        /// <returns>最近的消息列表</returns>
        public List<ChatMessage> GetRecentMessages(int count)
        {
            return _chatHistory?.GetRecentMessages(count) ?? new List<ChatMessage>();
        }

        /// <summary>
        /// 获取消息数量
        /// </summary>
        /// <returns>消息数量</returns>
        public int GetMessageCount()
        {
            return _chatHistory?.Count ?? 0;
        }

        /// <summary>
        /// 清空历史记录
        /// </summary>
        public void Clear()
        {
            try
            {
                _chatHistory?.Clear();
                _hasUnsavedChanges = true;
                _isCacheValid = false;

                LogInfo("主机聊天历史已清空");
            }
            catch (Exception ex)
            {
                LogError($"清空历史记录时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 历史存储和加载

        /// <summary>
        /// 加载历史记录
        /// </summary>
        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var loadedHistory = ChatHistory.LoadFromFile(_historyFilePath);
                    if (loadedHistory != null)
                    {
                        _chatHistory = loadedHistory;
                        _chatHistory.MaxMessages = _config.MaxHistoryMessages;
                        LogInfo($"主机历史加载完成，消息数量: {_chatHistory.Count}");
                    }
                }
                else
                {
                    LogInfo("未找到现有历史文件，创建新的历史记录");
                }

                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                LogError($"加载历史记录时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存历史记录
        /// </summary>
        /// <param name="force">是否强制保存</param>
        /// <returns>保存是否成功</returns>
        public bool SaveHistory(bool force = false)
        {
            try
            {
                if (!force && !_hasUnsavedChanges)
                {
                    LogDebug("没有未保存的更改，跳过保存");
                    return true;
                }

                if (_chatHistory == null)
                {
                    LogWarning("聊天历史为空，无法保存");
                    return false;
                }

                bool success = _chatHistory.SaveToFile(_historyFilePath);
                if (success)
                {
                    _hasUnsavedChanges = false;
                    LogInfo("主机聊天历史保存成功");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"保存历史记录时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 历史压缩和优化

        /// <summary>
        /// 获取压缩的历史数据
        /// </summary>
        /// <returns>压缩的历史数据</returns>
        public async Task<byte[]> GetCompressedHistory()
        {
            try
            {
                // 如果缓存有效且未过期，直接返回缓存
                if (_isCacheValid && _compressedHistoryCache != null)
                {
                    var cacheAge = (DateTime.UtcNow - _lastCompressionTime).TotalMinutes;
                    if (cacheAge < _config.CompressionCacheMinutes)
                    {
                        LogDebug("返回缓存的压缩历史数据");
                        return _compressedHistoryCache;
                    }
                }

                // 重新压缩历史数据
                return await CompressHistoryData();
            }
            catch (Exception ex)
            {
                LogError($"获取压缩历史数据时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 压缩历史数据
        /// </summary>
        /// <returns>压缩后的数据</returns>
        private async Task<byte[]> CompressHistoryData()
        {
            try
            {
                if (_chatHistory == null)
                    return null;

                // 序列化历史数据
                var historyJson = _chatHistory.ToJson();
                if (string.IsNullOrEmpty(historyJson))
                    return null;

                var originalData = System.Text.Encoding.UTF8.GetBytes(historyJson);
                var originalSize = originalData.Length;

                // 使用 GZip 压缩
                using (var memoryStream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                    {
                        await gzipStream.WriteAsync(originalData, 0, originalData.Length);
                    }

                    _compressedHistoryCache = memoryStream.ToArray();
                    _isCacheValid = true;
                    _lastCompressionTime = DateTime.UtcNow;

                    var compressedSize = _compressedHistoryCache.Length;
                    var compressionRatio = (1.0 - (double)compressedSize / originalSize) * 100;

                    LogInfo($"历史数据压缩完成: {originalSize} -> {compressedSize} 字节 (压缩率: {compressionRatio:F1}%)");

                    // 触发压缩事件
                    OnHistoryCompressed?.Invoke(originalSize, compressedSize);

                    return _compressedHistoryCache;
                }
            }
            catch (Exception ex)
            {
                LogError($"压缩历史数据时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解压历史数据
        /// </summary>
        /// <param name="compressedData">压缩的数据</param>
        /// <returns>解压后的聊天历史</returns>
        public static async Task<ChatHistory> DecompressHistoryData(byte[] compressedData)
        {
            try
            {
                if (compressedData == null || compressedData.Length == 0)
                    return null;

                using (var memoryStream = new MemoryStream(compressedData))
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                using (var resultStream = new MemoryStream())
                {
                    await gzipStream.CopyToAsync(resultStream);
                    var decompressedData = resultStream.ToArray();
                    var historyJson = System.Text.Encoding.UTF8.GetString(decompressedData);

                    return ChatHistory.FromJson(historyJson);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"解压历史数据时发生异常: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 历史清理和维护

        /// <summary>
        /// 清理旧消息
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>清理的消息数量</returns>
        public int CleanupOldMessages(int daysToKeep = -1)
        {
            try
            {
                if (_chatHistory == null)
                    return 0;

                var keepDays = daysToKeep > 0 ? daysToKeep : _config.HistoryRetentionDays;
                var cutoffTime = DateTime.UtcNow.AddDays(-keepDays);

                int removedCount = _chatHistory.RemoveMessagesOlderThan(cutoffTime);

                if (removedCount > 0)
                {
                    _hasUnsavedChanges = true;
                    _isCacheValid = false;

                    LogInfo($"清理了 {removedCount} 条旧消息（保留 {keepDays} 天）");

                    // 触发清理事件
                    OnHistoryCleaned?.Invoke(removedCount);
                }

                return removedCount;
            }
            catch (Exception ex)
            {
                LogError($"清理旧消息时发生异常: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 优化历史存储
        /// </summary>
        public void OptimizeStorage()
        {
            try
            {
                if (_chatHistory == null)
                    return;

                // 移除重复消息
                var originalCount = _chatHistory.Count;
                // 这里可以实现去重逻辑，暂时跳过

                // 清理旧消息
                var removedCount = CleanupOldMessages();

                // 如果有变化，保存历史
                if (removedCount > 0)
                {
                    SaveHistory(true);
                }

                LogInfo($"存储优化完成，移除了 {removedCount} 条消息");
            }
            catch (Exception ex)
            {
                LogError($"优化历史存储时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 历史备份

        /// <summary>
        /// 创建历史备份
        /// </summary>
        /// <returns>备份是否成功</returns>
        public bool CreateBackup()
        {
            try
            {
                if (_chatHistory == null || _chatHistory.Count == 0)
                {
                    LogWarning("没有历史数据需要备份");
                    return false;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"host_chat_backup_{timestamp}.json";
                var backupFilePath = Path.Combine(_backupFilePath, backupFileName);

                bool success = _chatHistory.SaveToFile(backupFilePath);

                if (success)
                {
                    LogInfo($"历史备份创建成功: {backupFileName}");

                    // 清理旧备份
                    CleanupOldBackups();
                }

                // 触发备份事件
                OnHistoryBackedUp?.Invoke(success);

                return success;
            }
            catch (Exception ex)
            {
                LogError($"创建历史备份时发生异常: {ex.Message}");
                OnHistoryBackedUp?.Invoke(false);
                return false;
            }
        }

        /// <summary>
        /// 清理旧备份文件
        /// </summary>
        private void CleanupOldBackups()
        {
            try
            {
                if (!Directory.Exists(_backupFilePath))
                    return;

                var backupFiles = Directory.GetFiles(_backupFilePath, "host_chat_backup_*.json");
                if (backupFiles.Length <= _config.MaxBackupFiles)
                    return;

                // 按创建时间排序，删除最旧的文件
                Array.Sort(backupFiles, (x, y) => File.GetCreationTime(x).CompareTo(File.GetCreationTime(y)));

                int filesToDelete = backupFiles.Length - _config.MaxBackupFiles;
                for (int i = 0; i < filesToDelete; i++)
                {
                    File.Delete(backupFiles[i]);
                    LogDebug($"删除旧备份文件: {Path.GetFileName(backupFiles[i])}");
                }

                LogInfo($"清理了 {filesToDelete} 个旧备份文件");
            }
            catch (Exception ex)
            {
                LogError($"清理旧备份文件时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 同步准备

        /// <summary>
        /// 准备历史同步数据
        /// </summary>
        /// <param name="clientId">客机ID</param>
        /// <param name="maxMessages">最大消息数量</param>
        /// <returns>同步数据包</returns>
        public async Task<HistorySyncPacket> PrepareHistorySync(string clientId, int maxMessages = -1)
        {
            try
            {
                if (_chatHistory == null)
                {
                    return new HistorySyncPacket
                    {
                        ClientId = clientId,
                        Success = false,
                        ErrorMessage = "历史数据不可用"
                    };
                }

                var messageCount = maxMessages > 0 ? Math.Min(maxMessages, _chatHistory.Count) : _chatHistory.Count;
                var messages = _chatHistory.GetRecentMessages(messageCount);

                // 创建临时历史对象用于同步
                var syncHistory = new ChatHistory(messageCount);
                syncHistory.AddMessages(messages);

                // 压缩同步数据
                var compressedData = await CompressSyncData(syncHistory);

                var syncPacket = new HistorySyncPacket
                {
                    ClientId = clientId,
                    MessageCount = messages.Count,
                    CompressedData = compressedData,
                    OriginalSize = System.Text.Encoding.UTF8.GetBytes(syncHistory.ToJson()).Length,
                    CompressedSize = compressedData?.Length ?? 0,
                    Success = compressedData != null,
                    Timestamp = DateTime.UtcNow
                };

                if (!syncPacket.Success)
                {
                    syncPacket.ErrorMessage = "数据压缩失败";
                }

                LogInfo($"为客机 {clientId} 准备历史同步: {syncPacket.MessageCount} 条消息, 压缩后 {syncPacket.CompressedSize} 字节");

                return syncPacket;
            }
            catch (Exception ex)
            {
                LogError($"准备历史同步时发生异常: {ex.Message}");
                return new HistorySyncPacket
                {
                    ClientId = clientId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 压缩同步数据
        /// </summary>
        /// <param name="history">聊天历史</param>
        /// <returns>压缩后的数据</returns>
        private async Task<byte[]> CompressSyncData(ChatHistory history)
        {
            try
            {
                var historyJson = history.ToJson();
                if (string.IsNullOrEmpty(historyJson))
                    return null;

                var originalData = System.Text.Encoding.UTF8.GetBytes(historyJson);

                using (var memoryStream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                    {
                        await gzipStream.WriteAsync(originalData, 0, originalData.Length);
                    }
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                LogError($"压缩同步数据时发生异常: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 定时任务

        private void Update()
        {
            if (_config == null)
                return;

            // 自动清理定时器
            if (_config.AutoCleanupEnabled)
            {
                _cleanupTimer += Time.deltaTime;
                if (_cleanupTimer >= _config.AutoCleanupIntervalHours * 3600f)
                {
                    _cleanupTimer = 0f;
                    CleanupOldMessages();
                }
            }

            // 自动备份定时器
            if (_config.AutoBackupEnabled)
            {
                _backupTimer += Time.deltaTime;
                if (_backupTimer >= _config.AutoBackupIntervalHours * 3600f)
                {
                    _backupTimer = 0f;
                    CreateBackup();
                }
            }
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
                // 保存未保存的更改
                if (_hasUnsavedChanges)
                {
                    SaveHistory(true);
                }

                // 清理缓存
                _compressedHistoryCache = null;
                _isCacheValid = false;

                LogInfo("主机历史管理器已清理");
            }
            catch (Exception ex)
            {
                LogError($"清理主机历史管理器时发生异常: {ex.Message}");
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
            Debug.Log($"[HostHistoryManager] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[HostHistoryManager] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[HostHistoryManager] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[HostHistoryManager][DEBUG] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 主机历史配置类
    /// </summary>
    public class HostHistoryConfig
    {
        /// <summary>
        /// 最大历史消息数量
        /// </summary>
        public int MaxHistoryMessages { get; set; } = 1000;

        /// <summary>
        /// 历史保留天数
        /// </summary>
        public int HistoryRetentionDays { get; set; } = 30;

        /// <summary>
        /// 压缩缓存有效时间（分钟）
        /// </summary>
        public int CompressionCacheMinutes { get; set; } = 10;

        /// <summary>
        /// 是否启用自动清理
        /// </summary>
        public bool AutoCleanupEnabled { get; set; } = true;

        /// <summary>
        /// 自动清理间隔（小时）
        /// </summary>
        public float AutoCleanupIntervalHours { get; set; } = 24f;

        /// <summary>
        /// 是否启用自动备份
        /// </summary>
        public bool AutoBackupEnabled { get; set; } = true;

        /// <summary>
        /// 自动备份间隔（小时）
        /// </summary>
        public float AutoBackupIntervalHours { get; set; } = 6f;

        /// <summary>
        /// 最大备份文件数量
        /// </summary>
        public int MaxBackupFiles { get; set; } = 10;
    }

    /// <summary>
    /// 历史同步数据包
    /// </summary>
    public class HistorySyncPacket
    {
        /// <summary>
        /// 客机ID
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// 消息数量
        /// </summary>
        public int MessageCount { get; set; }

        /// <summary>
        /// 压缩后的数据
        /// </summary>
        public byte[] CompressedData { get; set; }

        /// <summary>
        /// 原始数据大小
        /// </summary>
        public int OriginalSize { get; set; }

        /// <summary>
        /// 压缩后数据大小
        /// </summary>
        public int CompressedSize { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 压缩率
        /// </summary>
        public double CompressionRatio => OriginalSize > 0 ? (1.0 - (double)CompressedSize / OriginalSize) * 100 : 0;
    }
}