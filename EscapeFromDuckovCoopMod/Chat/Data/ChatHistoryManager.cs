using System;
using System.IO;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Data
{
    /// <summary>
    /// 聊天历史管理器
    /// </summary>
    public class ChatHistoryManager : MonoBehaviour
    {
        [Header("存储设置")]
        [SerializeField] private string historyFileName = "chat_history.json";
        [SerializeField] private int maxHistoryMessages = 500;
        [SerializeField] private bool autoSave = true;
        [SerializeField] private float autoSaveInterval = 30f; // 自动保存间隔（秒）
        [SerializeField] private bool enableDebugLog = false;

        /// <summary>
        /// 历史加载完成事件
        /// </summary>
        public event Action<ChatHistory> OnHistoryLoaded;

        /// <summary>
        /// 历史保存完成事件
        /// </summary>
        public event Action<bool> OnHistorySaved;

        private ChatHistory chatHistory;
        private string historyFilePath;
        private float lastSaveTime;
        private bool isDirty = false; // 标记是否有未保存的更改

        /// <summary>
        /// 单例实例
        /// </summary>
        public static ChatHistoryManager Instance { get; private set; }

        /// <summary>
        /// 获取聊天历史
        /// </summary>
        public ChatHistory History => chatHistory;

        /// <summary>
        /// 检查是否有未保存的更改
        /// </summary>
        public bool HasUnsavedChanges => isDirty;

        /// <summary>
        /// Awake时设置单例
        /// </summary>
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Initialize();
            }
            else if (Instance != this)
            {
                Debug.LogWarning("检测到重复的ChatHistoryManager实例，销毁当前实例");
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 初始化历史管理器
        /// </summary>
        private void Initialize()
        {
            try
            {
                // 设置历史文件路径
                var dataPath = Path.Combine(Application.persistentDataPath, "ChatData");
                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                }
                historyFilePath = Path.Combine(dataPath, historyFileName);

                // 加载历史记录
                LoadHistory();

                LogDebug($"聊天历史管理器初始化完成，文件路径: {historyFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化聊天历史管理器失败: {ex.Message}");
                chatHistory = new ChatHistory(maxHistoryMessages);
            }
        }

        /// <summary>
        /// 加载聊天历史
        /// </summary>
        public void LoadHistory()
        {
            try
            {
                if (File.Exists(historyFilePath))
                {
                    chatHistory = ChatHistory.LoadFromFile(historyFilePath);
                    if (chatHistory == null)
                    {
                        chatHistory = new ChatHistory(maxHistoryMessages);
                    }
                    else
                    {
                        // 更新最大消息数量设置
                        chatHistory.MaxMessages = maxHistoryMessages;
                    }
                    LogDebug($"聊天历史加载完成，消息数量: {chatHistory.Count}");
                }
                else
                {
                    chatHistory = new ChatHistory(maxHistoryMessages);
                    LogDebug("创建新的聊天历史");
                }

                isDirty = false;
                OnHistoryLoaded?.Invoke(chatHistory);
            }
            catch (Exception ex)
            {
                Debug.LogError($"加载聊天历史失败: {ex.Message}");
                chatHistory = new ChatHistory(maxHistoryMessages);
                OnHistoryLoaded?.Invoke(chatHistory);
            }
        }

        /// <summary>
        /// 保存聊天历史
        /// </summary>
        /// <param name="force">是否强制保存</param>
        /// <returns>是否保存成功</returns>
        public bool SaveHistory(bool force = false)
        {
            if (!force && !isDirty)
            {
                LogDebug("没有未保存的更改，跳过保存");
                return true;
            }

            try
            {
                if (chatHistory == null)
                {
                    Debug.LogWarning("聊天历史为空，无法保存");
                    return false;
                }

                bool success = chatHistory.SaveToFile(historyFilePath);
                if (success)
                {
                    isDirty = false;
                    lastSaveTime = Time.time;
                    LogDebug("聊天历史保存成功");
                }

                OnHistorySaved?.Invoke(success);
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"保存聊天历史失败: {ex.Message}");
                OnHistorySaved?.Invoke(false);
                return false;
            }
        }

        /// <summary>
        /// 添加消息到历史
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void AddMessage(ChatMessage message)
        {
            if (chatHistory == null || message == null)
                return;

            chatHistory.AddMessage(message);
            isDirty = true;
            LogDebug($"消息已添加到历史: {message.Content}");
        }

        /// <summary>
        /// 批量添加消息到历史
        /// </summary>
        /// <param name="messages">消息列表</param>
        public void AddMessages(System.Collections.Generic.IEnumerable<ChatMessage> messages)
        {
            if (chatHistory == null || messages == null)
                return;

            chatHistory.AddMessages(messages);
            isDirty = true;
            LogDebug("批量消息已添加到历史");
        }

        /// <summary>
        /// 清空历史记录
        /// </summary>
        public void ClearHistory()
        {
            if (chatHistory == null)
                return;

            chatHistory.Clear();
            isDirty = true;
            LogDebug("聊天历史已清空");
        }

        /// <summary>
        /// 获取最近的消息
        /// </summary>
        /// <param name="count">消息数量</param>
        /// <returns>最近的消息列表</returns>
        public System.Collections.Generic.List<ChatMessage> GetRecentMessages(int count)
        {
            if (chatHistory == null)
                return new System.Collections.Generic.List<ChatMessage>();

            return chatHistory.GetRecentMessages(count);
        }

        /// <summary>
        /// 搜索消息
        /// </summary>
        /// <param name="searchText">搜索文本</param>
        /// <param name="ignoreCase">是否忽略大小写</param>
        /// <returns>匹配的消息列表</returns>
        public System.Collections.Generic.List<ChatMessage> SearchMessages(string searchText, bool ignoreCase = true)
        {
            if (chatHistory == null)
                return new System.Collections.Generic.List<ChatMessage>();

            return chatHistory.SearchMessages(searchText, ignoreCase);
        }

        /// <summary>
        /// 获取历史统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        public ChatHistoryStats GetHistoryStats()
        {
            if (chatHistory == null)
                return new ChatHistoryStats();

            return chatHistory.GetStats();
        }

        /// <summary>
        /// 清理旧消息
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>清理的消息数量</returns>
        public int CleanupOldMessages(int daysToKeep = 30)
        {
            if (chatHistory == null || daysToKeep <= 0)
                return 0;

            var cutoffTime = DateTime.UtcNow.AddDays(-daysToKeep);
            int removedCount = chatHistory.RemoveMessagesOlderThan(cutoffTime);

            if (removedCount > 0)
            {
                isDirty = true;
                LogDebug($"清理了 {removedCount} 条旧消息");
            }

            return removedCount;
        }

        /// <summary>
        /// 导出历史记录
        /// </summary>
        /// <param name="exportPath">导出路径</param>
        /// <returns>是否导出成功</returns>
        public bool ExportHistory(string exportPath)
        {
            if (chatHistory == null)
                return false;

            try
            {
                return chatHistory.SaveToFile(exportPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"导出聊天历史失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 导入历史记录
        /// </summary>
        /// <param name="importPath">导入路径</param>
        /// <param name="merge">是否合并到现有历史</param>
        /// <returns>是否导入成功</returns>
        public bool ImportHistory(string importPath, bool merge = true)
        {
            try
            {
                var importedHistory = ChatHistory.LoadFromFile(importPath);
                if (importedHistory == null)
                    return false;

                if (merge && chatHistory != null)
                {
                    chatHistory.Merge(importedHistory);
                }
                else
                {
                    chatHistory = importedHistory;
                    chatHistory.MaxMessages = maxHistoryMessages;
                }

                isDirty = true;
                LogDebug($"聊天历史导入成功，消息数量: {chatHistory.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"导入聊天历史失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置最大历史消息数量
        /// </summary>
        /// <param name="maxMessages">最大消息数量</param>
        public void SetMaxHistoryMessages(int maxMessages)
        {
            this.maxHistoryMessages = Math.Max(1, maxMessages);
            if (chatHistory != null)
            {
                chatHistory.MaxMessages = this.maxHistoryMessages;
            }
        }

        /// <summary>
        /// Update中处理自动保存
        /// </summary>
        private void Update()
        {
            if (autoSave && isDirty && Time.time - lastSaveTime >= autoSaveInterval)
            {
                SaveHistory();
            }
        }

        /// <summary>
        /// 应用程序暂停时保存
        /// </summary>
        /// <param name="pauseStatus">暂停状态</param>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && isDirty)
            {
                SaveHistory(true);
            }
        }

        /// <summary>
        /// 应用程序焦点改变时保存
        /// </summary>
        /// <param name="hasFocus">是否有焦点</param>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && isDirty)
            {
                SaveHistory(true);
            }
        }

        /// <summary>
        /// 应用程序退出时保存
        /// </summary>
        private void OnApplicationQuit()
        {
            if (isDirty)
            {
                SaveHistory(true);
            }
        }

        /// <summary>
        /// 调试日志输出
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogDebug(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log($"[ChatHistoryManager] {message}");
            }
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            // 最后一次保存
            if (isDirty)
            {
                SaveHistory(true);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 静态方法：创建ChatHistoryManager实例
        /// </summary>
        /// <returns>ChatHistoryManager实例</returns>
        public static ChatHistoryManager CreateInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("ChatHistoryManager");
            var manager = go.AddComponent<ChatHistoryManager>();
            return manager;
        }

        /// <summary>
        /// 静态方法：获取或创建实例
        /// </summary>
        /// <returns>ChatHistoryManager实例</returns>
        public static ChatHistoryManager GetOrCreateInstance()
        {
            if (Instance == null)
            {
                return CreateInstance();
            }
            return Instance;
        }
    }
}