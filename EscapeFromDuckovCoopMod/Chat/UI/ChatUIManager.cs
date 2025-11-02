using System;
using System.Collections.Generic;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 聊天UI管理器 - 单例模式
    /// </summary>
    public class ChatUIManager : MonoBehaviour
    {
        [Header("UI组件引用")]
        [SerializeField] private ChatPanel chatPanel;
        [SerializeField] private ChatInputOverlay inputOverlay;

        [Header("管理器设置")]
        [SerializeField] private bool autoInitialize = true;
        [SerializeField] private bool debugMode = false;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static ChatUIManager Instance { get; private set; }

        /// <summary>
        /// 消息发送事件
        /// </summary>
        public event Action<string> OnMessageSent;

        /// <summary>
        /// 输入状态改变事件
        /// </summary>
        public event Action<bool> OnInputStateChanged;

        private bool isInitialized = false;
        private bool isInputActive = false;

        /// <summary>
        /// 获取聊天面板
        /// </summary>
        public ChatPanel ChatPanel => chatPanel;

        /// <summary>
        /// 获取输入覆盖层
        /// </summary>
        public ChatInputOverlay InputOverlay => inputOverlay;

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// 检查输入是否激活
        /// </summary>
        public bool IsInputActive => isInputActive;

        /// <summary>
        /// Awake时设置单例
        /// </summary>
        private void Awake()
        {
            // 单例模式实现
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);

                if (autoInitialize)
                {
                    Initialize();
                }
            }
            else if (Instance != this)
            {
                Debug.LogWarning("检测到重复的ChatUIManager实例，销毁当前实例");
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 初始化聊天UI管理器
        /// </summary>
        public void Initialize()
        {
            if (isInitialized)
            {
                Debug.LogWarning("ChatUIManager已经初始化");
                return;
            }

            try
            {
                // 查找UI组件
                FindUIComponents();

                // 初始化聊天面板
                if (chatPanel != null)
                {
                    chatPanel.Initialize();
                    LogDebug("聊天面板初始化完成");
                }
                else
                {
                    Debug.LogError("聊天面板组件未找到");
                }

                // 初始化输入覆盖层
                if (inputOverlay != null)
                {
                    inputOverlay.Initialize();
                    inputOverlay.OnMessageSent += HandleMessageSent;
                    inputOverlay.OnOverlayClosed += HandleOverlayClosed;
                    LogDebug("输入覆盖层初始化完成");
                }
                else
                {
                    Debug.LogError("输入覆盖层组件未找到");
                }

                // 订阅 LocalChatManager 的消息接收事件
                SubscribeToChatEvents();

                isInitialized = true;
                Debug.Log("ChatUIManager初始化完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"ChatUIManager初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅聊天事件
        /// </summary>
        private void SubscribeToChatEvents()
        {
            try
            {
                var localChatManager = Managers.LocalChatManager.Instance;
                if (localChatManager != null)
                {
                    localChatManager.OnMessageReceived += HandleChatMessageReceived;
                    LogDebug("已订阅 LocalChatManager 的消息接收事件");
                }
                else
                {
                    Debug.LogWarning("LocalChatManager 实例未找到，将在稍后重试订阅");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"订阅聊天事件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理聊天消息接收
        /// </summary>
        /// <param name="message">聊天消息</param>
        private void HandleChatMessageReceived(ChatMessage message)
        {
            if (message == null)
                return;

            try
            {
                LogDebug($"收到聊天消息: {message.GetDisplayText()}");
                
                // 添加消息到 UI
                AddMessage(message);
                
                // 通知 ModUI 更新聊天消息显示
                ModUI.Instance?.AddChatMessage(message.GetDisplayText());
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理聊天消息时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示聊天面板
        /// </summary>
        public void ShowChatPanel()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            if (chatPanel != null)
            {
                chatPanel.Show();
                LogDebug("聊天面板已显示");
            }
        }

        /// <summary>
        /// 隐藏聊天面板
        /// </summary>
        public void HideChatPanel()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            if (chatPanel != null)
            {
                chatPanel.Hide();
                LogDebug("聊天面板已隐藏");
            }
        }

        /// <summary>
        /// 显示输入覆盖层
        /// </summary>
        public void ShowInputOverlay()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            // 使用新的独立弹窗输入系统
            ShowInputDialog();
        }
        
        /// <summary>
        /// 显示独立输入对话框
        /// </summary>
        private void ShowInputDialog()
        {
            try
            {
                LogDebug("显示独立聊天输入对话框");
                
                // 使用Windows API弹窗获取输入
                string userInput = ChatInputDialog.ShowInputDialog(
                    title: "聊天输入", 
                    prompt: "请输入聊天消息:", 
                    defaultText: ""
                );
                
                if (!string.IsNullOrEmpty(userInput) && userInput != "请使用发送按钮发送消息")
                {
                    // 用户输入了有效内容，触发消息发送事件
                    LogDebug($"用户通过弹窗输入消息: {userInput}");
                    OnMessageSent?.Invoke(userInput);
                }
                else
                {
                    LogDebug("用户取消了聊天输入或使用发送按钮");
                    
                    // 如果弹窗方式不可用，回退到原有的覆盖层方式
                    if (inputOverlay != null && !inputOverlay.IsVisible())
                    {
                        inputOverlay.Show();
                        isInputActive = true;
                        OnInputStateChanged?.Invoke(true);
                        LogDebug("回退到覆盖层输入方式");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatUIManager] 显示输入对话框时发生错误: {ex.Message}");
                
                // 发生错误时回退到原有方式
                if (inputOverlay != null && !inputOverlay.IsVisible())
                {
                    inputOverlay.Show();
                    isInputActive = true;
                    OnInputStateChanged?.Invoke(true);
                    LogDebug("错误回退到覆盖层输入方式");
                }
            }
        }

        /// <summary>
        /// 隐藏输入覆盖层
        /// </summary>
        public void HideInputOverlay()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            if (inputOverlay != null && inputOverlay.IsVisible())
            {
                inputOverlay.Hide();
                isInputActive = false;
                OnInputStateChanged?.Invoke(false);
                LogDebug("输入覆盖层已隐藏");
            }
        }

        /// <summary>
        /// 切换输入覆盖层显示状态
        /// </summary>
        public void ToggleInputOverlay()
        {
            if (inputOverlay != null)
            {
                if (inputOverlay.IsVisible())
                {
                    HideInputOverlay();
                }
                else
                {
                    ShowInputOverlay();
                }
            }
        }

        /// <summary>
        /// 添加消息到聊天面板
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void AddMessage(ChatMessage message)
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            // 由于我们使用ModUI的OnGUI显示消息，这里不需要调用ChatPanel
            // 只记录日志表示消息已被处理
            LogDebug($"消息已接收: {message?.GetDisplayText() ?? "null"}");
        }

        /// <summary>
        /// 批量添加消息
        /// </summary>
        /// <param name="messages">消息列表</param>
        public void AddMessages(List<ChatMessage> messages)
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            if (messages != null)
            {
                // 由于我们使用ModUI的OnGUI显示消息，这里不需要调用ChatPanel
                LogDebug($"批量消息已接收: {messages.Count}条");
            }
        }

        /// <summary>
        /// 清空所有消息
        /// </summary>
        public void ClearMessages()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            if (chatPanel != null)
            {
                chatPanel.ClearMessages();
                LogDebug("已清空所有消息");
            }
        }

        /// <summary>
        /// 滚动到最新消息
        /// </summary>
        public void ScrollToLatest()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ChatUIManager未初始化");
                return;
            }

            if (chatPanel != null)
            {
                chatPanel.ScrollToBottom();
                LogDebug("滚动到最新消息");
            }
        }

        /// <summary>
        /// 设置聊天面板提示文本
        /// </summary>
        /// <param name="text">提示文本</param>
        public void SetPromptText(string text)
        {
            if (chatPanel != null)
            {
                chatPanel.SetPromptText(text);
            }
        }

        /// <summary>
        /// 获取当前消息数量
        /// </summary>
        /// <returns>消息数量</returns>
        public int GetMessageCount()
        {
            // 由于消息显示在ModUI中，这里返回0
            // 实际的消息数量由ModUI管理
            return 0;
        }

        /// <summary>
        /// 检查聊天面板是否可见
        /// </summary>
        /// <returns>是否可见</returns>
        public bool IsChatPanelVisible()
        {
            return chatPanel != null && chatPanel.IsVisible();
        }

        /// <summary>
        /// 处理消息发送事件
        /// </summary>
        /// <param name="message">消息内容</param>
        private void HandleMessageSent(string message)
        {
            LogDebug($"收到发送消息请求: {message}");
            OnMessageSent?.Invoke(message);
        }

        /// <summary>
        /// 处理覆盖层关闭事件
        /// </summary>
        private void HandleOverlayClosed()
        {
            isInputActive = false;
            OnInputStateChanged?.Invoke(false);
            LogDebug("输入覆盖层已关闭");
        }

        /// <summary>
        /// 查找或创建UI组件
        /// </summary>
        private void FindUIComponents()
        {
            // 如果没有手动设置组件，尝试自动查找或创建
            if (chatPanel == null)
            {
                chatPanel = FindObjectOfType<ChatPanel>();
                if (chatPanel == null)
                {
                    // 创建一个简单的聊天面板组件
                    var panelGO = new GameObject("ChatPanel");
                    panelGO.transform.SetParent(transform);
                    chatPanel = panelGO.AddComponent<ChatPanel>();
                    LogDebug("动态创建ChatPanel组件");
                }
            }

            if (inputOverlay == null)
            {
                inputOverlay = FindObjectOfType<ChatInputOverlay>();
                if (inputOverlay == null)
                {
                    // 创建一个简单的输入覆盖层组件
                    var overlayGO = new GameObject("ChatInputOverlay");
                    overlayGO.transform.SetParent(transform);
                    inputOverlay = overlayGO.AddComponent<ChatInputOverlay>();
                    LogDebug("动态创建ChatInputOverlay组件");
                }
            }
        }

        /// <summary>
        /// 调试日志输出
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogDebug(string message)
        {
            if (debugMode)
            {
                Debug.Log($"[ChatUIManager] {message}");
            }
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            if (inputOverlay != null)
            {
                inputOverlay.OnMessageSent -= HandleMessageSent;
                inputOverlay.OnOverlayClosed -= HandleOverlayClosed;
            }

            // 取消订阅聊天事件
            var localChatManager = Managers.LocalChatManager.Instance;
            if (localChatManager != null)
            {
                localChatManager.OnMessageReceived -= HandleChatMessageReceived;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 静态方法：创建ChatUIManager实例
        /// </summary>
        /// <returns>ChatUIManager实例</returns>
        public static ChatUIManager CreateInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("ChatUIManager");
            var manager = go.AddComponent<ChatUIManager>();
            return manager;
        }

        /// <summary>
        /// 静态方法：获取或创建实例
        /// </summary>
        /// <returns>ChatUIManager实例</returns>
        public static ChatUIManager GetOrCreateInstance()
        {
            if (Instance == null)
            {
                return CreateInstance();
            }
            return Instance;
        }
    }
}