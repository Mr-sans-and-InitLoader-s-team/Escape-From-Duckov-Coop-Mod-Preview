using System;
using System.Collections.Generic;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using EscapeFromDuckovCoopMod.Chat.Services;
using EscapeFromDuckovCoopMod.Chat.Input;

namespace EscapeFromDuckovCoopMod.Chat.Managers
{
    /// <summary>
    /// 本地聊天管理器
    /// </summary>
    public class LocalChatManager : MonoBehaviour
    {
        [Header("聊天设置")]
        [SerializeField] private int maxMessages = 100;
        [SerializeField] private float messageInterval = 1.0f;
        [SerializeField] private bool enableDebugLog = false;

        /// <summary>
        /// 消息发送事件
        /// </summary>
        public event Action<ChatMessage> OnMessageSent;

        /// <summary>
        /// 消息接收事件
        /// </summary>
        public event Action<ChatMessage> OnMessageReceived;

        /// <summary>
        /// 消息验证失败事件
        /// </summary>
        public event Action<string> OnMessageValidationFailed;

        /// <summary>
        /// 发送频率限制事件
        /// </summary>
        public event Action OnMessageRateLimited;

        private readonly List<ChatMessage> messageHistory = new List<ChatMessage>();
        private ISteamUserService steamUserService;
        private ChatInputProcessor inputProcessor;
        private UserInfo currentUser;
        private bool isInitialized = false;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static LocalChatManager Instance { get; private set; }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// 获取消息历史
        /// </summary>
        public IReadOnlyList<ChatMessage> MessageHistory => messageHistory.AsReadOnly();

        /// <summary>
        /// 获取当前用户
        /// </summary>
        public UserInfo CurrentUser => currentUser;

        /// <summary>
        /// Awake时设置单例
        /// </summary>
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Debug.LogWarning("检测到重复的LocalChatManager实例，销毁当前实例");
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 初始化本地聊天管理器
        /// </summary>
        public async void Initialize()
        {
            if (isInitialized)
            {
                Debug.LogWarning("LocalChatManager已经初始化");
                return;
            }

            try
            {
                LogDebug("开始初始化本地聊天管理器");

                // 初始化Steam用户服务
                steamUserService = new SteamUserService();
                await steamUserService.InitializeSteamAPI();

                // 获取当前用户信息
                currentUser = await steamUserService.GetCurrentUserInfo();
                if (currentUser == null)
                {
                    Debug.LogError("无法获取当前用户信息");
                    return;
                }

                // 初始化输入处理器
                inputProcessor = new ChatInputProcessor(currentUser);
                inputProcessor.SetMessageInterval(messageInterval);
                inputProcessor.OnMessageProcessed += HandleMessageProcessed;
                inputProcessor.OnValidationFailed += HandleValidationFailed;
                inputProcessor.OnRateLimited += HandleRateLimited;

                isInitialized = true;
                LogDebug($"本地聊天管理器初始化完成，当前用户: {currentUser.GetDisplayName()}");

                // 发送系统欢迎消息
                var welcomeMessage = inputProcessor.CreateSystemMessage($"欢迎 {currentUser.GetDisplayName()}！");
                AddMessageToHistory(welcomeMessage);
                OnMessageReceived?.Invoke(welcomeMessage);
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化本地聊天管理器时发生异常: {ex.Message}");
                isInitialized = false;
            }
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <returns>是否发送成功</returns>
        public bool SendChatMessage(string content)
        {
            if (!isInitialized)
            {
                Debug.LogWarning("LocalChatManager未初始化");
                return false;
            }

            if (inputProcessor == null)
            {
                Debug.LogError("输入处理器未初始化");
                return false;
            }

            LogDebug($"尝试发送消息: {content}");
            return inputProcessor.ProcessInput(content);
        }

        /// <summary>
        /// 接收消息（用于网络消息）
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void ReceiveMessage(ChatMessage message)
        {
            if (!isInitialized)
            {
                Debug.LogWarning("LocalChatManager未初始化");
                return;
            }

            if (message == null || !message.IsValid())
            {
                Debug.LogWarning("接收到无效消息");
                return;
            }

            AddMessageToHistory(message);
            OnMessageReceived?.Invoke(message);
            LogDebug($"接收消息: {message.GetDisplayText()}");
            
            // 临时调试：直接在控制台显示聊天消息
            Debug.Log($"[CHAT] {message.Sender?.DisplayName ?? message.Sender?.UserName ?? "未知"}: {message.Content}");
            
            // 直接通知 ModUI 显示消息
            var modUI = ModUI.Instance;
            if (modUI != null)
            {
                string displayText = message.GetDisplayText();
                modUI.AddChatMessage(displayText);
                LogDebug($"消息已添加到 ModUI: {displayText}");
            }
            else
            {
                Debug.LogWarning("[LocalChatManager] ModUI 实例未找到，无法显示消息");
            }
        }

        /// <summary>
        /// 批量接收消息
        /// </summary>
        /// <param name="messages">消息列表</param>
        public void ReceiveMessages(List<ChatMessage> messages)
        {
            if (!isInitialized || messages == null)
                return;

            foreach (var message in messages)
            {
                if (message != null && message.IsValid())
                {
                    AddMessageToHistory(message);
                    OnMessageReceived?.Invoke(message);
                }
            }

            LogDebug($"批量接收消息: {messages.Count}条");
        }

        /// <summary>
        /// 清空消息历史
        /// </summary>
        public void ClearMessageHistory()
        {
            messageHistory.Clear();
            LogDebug("消息历史已清空");
        }

        /// <summary>
        /// 获取最近的消息
        /// </summary>
        /// <param name="count">消息数量</param>
        /// <returns>最近的消息列表</returns>
        public List<ChatMessage> GetRecentMessages(int count)
        {
            if (count <= 0)
                return new List<ChatMessage>();

            var startIndex = Math.Max(0, messageHistory.Count - count);
            var recentMessages = new List<ChatMessage>();

            for (int i = startIndex; i < messageHistory.Count; i++)
            {
                recentMessages.Add(messageHistory[i]);
            }

            return recentMessages;
        }

        /// <summary>
        /// 获取消息数量
        /// </summary>
        /// <returns>消息数量</returns>
        public int GetMessageCount()
        {
            return messageHistory.Count;
        }

        /// <summary>
        /// 检查是否可以发送消息
        /// </summary>
        /// <returns>是否可以发送</returns>
        public bool CanSendMessage()
        {
            return isInitialized && inputProcessor != null && inputProcessor.CanSendMessage();
        }

        /// <summary>
        /// 获取距离下次可发送消息的剩余时间
        /// </summary>
        /// <returns>剩余时间（秒）</returns>
        public float GetTimeUntilNextMessage()
        {
            if (inputProcessor == null)
                return 0f;

            return inputProcessor.GetTimeUntilNextMessage();
        }

        /// <summary>
        /// 设置消息发送间隔
        /// </summary>
        /// <param name="interval">间隔时间（秒）</param>
        public void SetMessageInterval(float interval)
        {
            messageInterval = interval;
            if (inputProcessor != null)
            {
                inputProcessor.SetMessageInterval(interval);
            }
        }

        /// <summary>
        /// 刷新当前用户信息
        /// </summary>
        public async void RefreshCurrentUser()
        {
            if (steamUserService == null)
                return;

            try
            {
                var updatedUser = await steamUserService.GetCurrentUserInfo();
                if (updatedUser != null)
                {
                    currentUser = updatedUser;
                    if (inputProcessor != null)
                    {
                        inputProcessor.SetCurrentUser(currentUser);
                    }
                    LogDebug($"用户信息已更新: {currentUser.GetDisplayName()}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"刷新用户信息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建系统消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <param name="messageType">消息类型</param>
        /// <returns>系统消息</returns>
        public ChatMessage CreateSystemMessage(string content, MessageType messageType = MessageType.System)
        {
            if (inputProcessor == null)
            {
                return new ChatMessage(content, new UserInfo(0, "系统"), messageType);
            }

            return inputProcessor.CreateSystemMessage(content, messageType);
        }

        /// <summary>
        /// 创建错误消息
        /// </summary>
        /// <param name="errorContent">错误内容</param>
        /// <returns>错误消息</returns>
        public ChatMessage CreateErrorMessage(string errorContent)
        {
            if (inputProcessor == null)
            {
                return new ChatMessage(errorContent, new UserInfo(0, "系统"), MessageType.Error);
            }

            return inputProcessor.CreateErrorMessage(errorContent);
        }

        /// <summary>
        /// 处理消息处理完成事件
        /// </summary>
        /// <param name="message">处理完成的消息</param>
        private void HandleMessageProcessed(ChatMessage message)
        {
            AddMessageToHistory(message);
            OnMessageSent?.Invoke(message);
            OnMessageReceived?.Invoke(message); // 本地模式下，发送的消息也要显示
            LogDebug($"消息处理完成: {message.GetDisplayText()}");
        }

        /// <summary>
        /// 处理验证失败事件
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        private void HandleValidationFailed(string errorMessage)
        {
            OnMessageValidationFailed?.Invoke(errorMessage);
            LogDebug($"消息验证失败: {errorMessage}");

            // 创建错误提示消息
            var errorChatMessage = CreateErrorMessage($"发送失败: {errorMessage}");
            AddMessageToHistory(errorChatMessage);
            OnMessageReceived?.Invoke(errorChatMessage);
        }

        /// <summary>
        /// 处理频率限制事件
        /// </summary>
        private void HandleRateLimited()
        {
            OnMessageRateLimited?.Invoke();
            LogDebug("消息发送被频率限制");

            var remainingTime = GetTimeUntilNextMessage();
            var rateLimitMessage = CreateSystemMessage($"发送过于频繁，请等待 {remainingTime:F1} 秒后再试");
            AddMessageToHistory(rateLimitMessage);
            OnMessageReceived?.Invoke(rateLimitMessage);
        }

        /// <summary>
        /// 添加消息到历史记录
        /// </summary>
        /// <param name="message">聊天消息</param>
        private void AddMessageToHistory(ChatMessage message)
        {
            if (message == null)
                return;

            messageHistory.Add(message);

            // 限制消息数量
            if (messageHistory.Count > maxMessages)
            {
                var removeCount = messageHistory.Count - maxMessages;
                messageHistory.RemoveRange(0, removeCount);
                LogDebug($"移除了 {removeCount} 条旧消息");
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
                Debug.Log($"[LocalChatManager] {message}");
            }
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            if (inputProcessor != null)
            {
                inputProcessor.OnMessageProcessed -= HandleMessageProcessed;
                inputProcessor.OnValidationFailed -= HandleValidationFailed;
                inputProcessor.OnRateLimited -= HandleRateLimited;
            }

            if (steamUserService != null)
            {
                steamUserService.ShutdownSteamAPI();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 静态方法：创建LocalChatManager实例
        /// </summary>
        /// <returns>LocalChatManager实例</returns>
        public static LocalChatManager CreateInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("LocalChatManager");
            var manager = go.AddComponent<LocalChatManager>();
            return manager;
        }

        /// <summary>
        /// 静态方法：获取或创建实例
        /// </summary>
        /// <returns>LocalChatManager实例</returns>
        public static LocalChatManager GetOrCreateInstance()
        {
            if (Instance == null)
            {
                return CreateInstance();
            }
            return Instance;
        }
    }
}