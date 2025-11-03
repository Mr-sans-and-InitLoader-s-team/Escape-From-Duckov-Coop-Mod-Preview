using System;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.UI;

namespace EscapeFromDuckovCoopMod.Chat.Input
{
    /// <summary>
    /// 全局输入监听器
    /// </summary>
    public class GlobalInputListener : MonoBehaviour
    {
        [Header("输入设置")]
        [SerializeField] private KeyCode chatTriggerKey = KeyCode.Return;
        [SerializeField] private KeyCode closeChatKey = KeyCode.Escape;
        [SerializeField] private bool enableDebugLog = false;

        /// <summary>
        /// 聊天触发事件
        /// </summary>
        public event Action OnChatTriggered;

        /// <summary>
        /// 聊天关闭事件
        /// </summary>
        public event Action OnChatClosed;

        /// <summary>
        /// 输入状态改变事件
        /// </summary>
        public event Action<bool> OnInputStateChanged;

        private bool isChatInputActive = false;
        private ChatUIManager chatUIManager;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static GlobalInputListener Instance { get; private set; }

        /// <summary>
        /// 检查聊天输入是否激活
        /// </summary>
        public bool IsChatInputActive => isChatInputActive;

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
                Debug.LogWarning("检测到重复的GlobalInputListener实例，销毁当前实例");
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 初始化输入监听器
        /// </summary>
        private void Initialize()
        {
            // 获取ChatUIManager引用
            chatUIManager = ChatUIManager.Instance;
            if (chatUIManager == null)
            {
                // 如果ChatUIManager还没有创建，等待它创建
                StartCoroutine(WaitForChatUIManager());
            }
            else
            {
                SetupChatUIManagerEvents();
            }

            LogDebug("全局输入监听器初始化完成");
        }

        /// <summary>
        /// 等待ChatUIManager创建
        /// </summary>
        /// <returns>协程</returns>
        private System.Collections.IEnumerator WaitForChatUIManager()
        {
            while (chatUIManager == null)
            {
                chatUIManager = ChatUIManager.Instance;
                yield return new WaitForSeconds(0.1f);
            }

            SetupChatUIManagerEvents();
            LogDebug("ChatUIManager连接成功");
        }

        /// <summary>
        /// 设置ChatUIManager事件
        /// </summary>
        private void SetupChatUIManagerEvents()
        {
            if (chatUIManager != null)
            {
                chatUIManager.OnInputStateChanged += HandleInputStateChanged;
            }
        }

        /// <summary>
        /// 处理输入状态改变
        /// </summary>
        /// <param name="isActive">是否激活</param>
        private void HandleInputStateChanged(bool isActive)
        {
            isChatInputActive = isActive;
            OnInputStateChanged?.Invoke(isActive);
            LogDebug($"聊天输入状态改变: {isActive}");
        }

        /// <summary>
        /// Update中处理输入检测
        /// </summary>
        private void Update()
        {
            HandleKeyboardInput();
        }

        /// <summary>
        /// 处理键盘输入
        /// </summary>
        private void HandleKeyboardInput()
        {
            // 检测聊天触发键
            if (UnityEngine.Input.GetKeyDown(chatTriggerKey))
            {
                HandleChatTriggerKey();
            }

            // 检测聊天关闭键
            if (UnityEngine.Input.GetKeyDown(closeChatKey))
            {
                HandleChatCloseKey();
            }
        }

        /// <summary>
        /// 处理聊天触发键
        /// </summary>
        private void HandleChatTriggerKey()
        {
            // 如果聊天输入已经激活，不处理
            if (isChatInputActive)
            {
                LogDebug("聊天输入已激活，忽略触发键");
                return;
            }

            // 检查是否有其他UI阻止聊天输入
            if (IsOtherUIBlocking())
            {
                LogDebug("其他UI正在阻止聊天输入");
                return;
            }

            // 触发聊天输入
            TriggerChatInput();
        }

        /// <summary>
        /// 处理聊天关闭键
        /// </summary>
        private void HandleChatCloseKey()
        {
            // 只有在聊天输入激活时才处理关闭键
            if (isChatInputActive)
            {
                CloseChatInput();
            }
        }

        /// <summary>
        /// 触发聊天输入
        /// </summary>
        public void TriggerChatInput()
        {
            if (chatUIManager != null)
            {
                chatUIManager.ShowInputOverlay();
                OnChatTriggered?.Invoke();
                LogDebug("聊天输入已触发");
            }
            else
            {
                Debug.LogWarning("ChatUIManager未找到，无法触发聊天输入");
            }
        }

        /// <summary>
        /// 关闭聊天输入
        /// </summary>
        public void CloseChatInput()
        {
            if (chatUIManager != null)
            {
                chatUIManager.HideInputOverlay();
                OnChatClosed?.Invoke();
                LogDebug("聊天输入已关闭");
            }
        }

        /// <summary>
        /// 检查是否有其他UI阻止聊天输入
        /// </summary>
        /// <returns>是否被阻止</returns>
        private bool IsOtherUIBlocking()
        {
            // 检查是否有模态对话框或其他全屏UI
            // 这里可以根据游戏的具体UI系统进行扩展
            
            // 检查是否有输入框正在使用
            var currentSelected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (currentSelected != null)
            {
                var inputField = currentSelected.GetComponent<TMPro.TMP_InputField>();
                if (inputField != null)
                {
                    LogDebug("检测到其他输入框正在使用");
                    return true;
                }
            }

            // 检查游戏是否暂停
            if (Time.timeScale == 0)
            {
                LogDebug("游戏已暂停，阻止聊天输入");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 设置聊天触发键
        /// </summary>
        /// <param name="keyCode">键码</param>
        public void SetChatTriggerKey(KeyCode keyCode)
        {
            chatTriggerKey = keyCode;
            LogDebug($"聊天触发键设置为: {keyCode}");
        }

        /// <summary>
        /// 设置聊天关闭键
        /// </summary>
        /// <param name="keyCode">键码</param>
        public void SetChatCloseKey(KeyCode keyCode)
        {
            closeChatKey = keyCode;
            LogDebug($"聊天关闭键设置为: {keyCode}");
        }

        /// <summary>
        /// 启用或禁用输入监听
        /// </summary>
        /// <param name="enabled">是否启用</param>
        public void SetInputListenerEnabled(bool enabled)
        {
            this.enabled = enabled;
            LogDebug($"输入监听器{(enabled ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 强制设置输入状态
        /// </summary>
        /// <param name="isActive">是否激活</param>
        public void ForceSetInputState(bool isActive)
        {
            if (isChatInputActive != isActive)
            {
                isChatInputActive = isActive;
                OnInputStateChanged?.Invoke(isActive);
                LogDebug($"强制设置输入状态: {isActive}");
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
                Debug.Log($"[GlobalInputListener] {message}");
            }
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            if (chatUIManager != null)
            {
                chatUIManager.OnInputStateChanged -= HandleInputStateChanged;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 静态方法：创建GlobalInputListener实例
        /// </summary>
        /// <returns>GlobalInputListener实例</returns>
        public static GlobalInputListener CreateInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("GlobalInputListener");
            var listener = go.AddComponent<GlobalInputListener>();
            return listener;
        }

        /// <summary>
        /// 静态方法：获取或创建实例
        /// </summary>
        /// <returns>GlobalInputListener实例</returns>
        public static GlobalInputListener GetOrCreateInstance()
        {
            if (Instance == null)
            {
                return CreateInstance();
            }
            return Instance;
        }
    }
}