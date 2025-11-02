using System;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.UI;

namespace EscapeFromDuckovCoopMod.Chat.Input
{
    /// <summary>
    /// 聊天输入处理器 - 实现IInputHandler接口
    /// </summary>
    public class ChatInputHandler : MonoBehaviour, IInputHandler
    {
        [Header("输入处理设置")]
        [SerializeField] private int inputPriority = 1000;
        [SerializeField] private bool enableVisualFeedback = true;
        [SerializeField] private bool enableDebugLog = false;

        /// <summary>
        /// 输入状态改变事件
        /// </summary>
        public event Action<bool> OnInputStateChanged;

        private ChatUIManager chatUIManager;
        private GlobalInputManager globalInputManager;
        private GameInputBlocker gameInputBlocker;
        private ChatStatusIndicator statusIndicator;
        private bool isInputActive = false;
        private bool isRegistered = false;

        /// <summary>
        /// 检查输入是否激活
        /// </summary>
        public bool IsInputActive => isInputActive;

        /// <summary>
        /// 初始化聊天输入处理器
        /// </summary>
        public void Initialize()
        {
            try
            {
                // 获取管理器引用
                chatUIManager = ChatUIManager.Instance;
                globalInputManager = GlobalInputManager.Instance;
                gameInputBlocker = GameInputBlocker.Instance;

                // 查找状态指示器
                statusIndicator = FindObjectOfType<ChatStatusIndicator>();

                // 注册到全局输入管理器
                RegisterToGlobalInputManager();

                // 设置事件监听
                SetupEventListeners();

                LogDebug("聊天输入处理器初始化完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化聊天输入处理器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否可以处理输入
        /// </summary>
        /// <returns>是否可以处理</returns>
        public bool CanHandleInput()
        {
            return chatUIManager != null && chatUIManager.IsInitialized;
        }

        /// <summary>
        /// 处理输入事件
        /// </summary>
        /// <param name="inputEvent">输入事件</param>
        /// <returns>是否处理成功</returns>
        public bool HandleInput(InputEvent inputEvent)
        {
            if (inputEvent == null || inputEvent.IsHandled)
                return false;

            switch (inputEvent.Type)
            {
                case InputEventType.KeyDown:
                    return HandleKeyDown(inputEvent);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 处理器被激活时调用
        /// </summary>
        public void OnActivated()
        {
            LogDebug("聊天输入处理器已激活");
        }

        /// <summary>
        /// 处理器被停用时调用
        /// </summary>
        public void OnDeactivated()
        {
            LogDebug("聊天输入处理器已停用");
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update()
        {
            // 检查输入状态变化
            CheckInputStateChange();
        }

        /// <summary>
        /// 处理按键按下事件
        /// </summary>
        /// <param name="inputEvent">输入事件</param>
        /// <returns>是否处理成功</returns>
        private bool HandleKeyDown(InputEvent inputEvent)
        {
            switch (inputEvent.KeyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    return HandleEnterKey();

                case KeyCode.Escape:
                    return HandleEscapeKey();

                default:
                    return false;
            }
        }

        /// <summary>
        /// 处理Enter键
        /// </summary>
        /// <returns>是否处理成功</returns>
        private bool HandleEnterKey()
        {
            if (chatUIManager == null)
                return false;

            // 如果聊天输入未激活，激活它
            if (!isInputActive)
            {
                ActivateChatInput();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 处理Escape键
        /// </summary>
        /// <returns>是否处理成功</returns>
        private bool HandleEscapeKey()
        {
            if (chatUIManager == null)
                return false;

            // 如果聊天输入已激活，关闭它
            if (isInputActive)
            {
                DeactivateChatInput();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 激活聊天输入
        /// </summary>
        private void ActivateChatInput()
        {
            if (chatUIManager != null)
            {
                chatUIManager.ShowInputOverlay();
                LogDebug("聊天输入已激活");
            }
        }

        /// <summary>
        /// 停用聊天输入
        /// </summary>
        private void DeactivateChatInput()
        {
            if (chatUIManager != null)
            {
                chatUIManager.HideInputOverlay();
                LogDebug("聊天输入已停用");
            }
        }

        /// <summary>
        /// 注册到全局输入管理器
        /// </summary>
        private void RegisterToGlobalInputManager()
        {
            if (globalInputManager != null && !isRegistered)
            {
                globalInputManager.RegisterInputHandler(this, inputPriority);
                isRegistered = true;
                LogDebug("已注册到全局输入管理器");
            }
        }

        /// <summary>
        /// 从全局输入管理器注销
        /// </summary>
        private void UnregisterFromGlobalInputManager()
        {
            if (globalInputManager != null && isRegistered)
            {
                globalInputManager.UnregisterInputHandler(this);
                isRegistered = false;
                LogDebug("已从全局输入管理器注销");
            }
        }

        /// <summary>
        /// 设置事件监听
        /// </summary>
        private void SetupEventListeners()
        {
            if (chatUIManager != null)
            {
                chatUIManager.OnInputStateChanged += HandleChatInputStateChanged;
            }
        }

        /// <summary>
        /// 移除事件监听
        /// </summary>
        private void RemoveEventListeners()
        {
            if (chatUIManager != null)
            {
                chatUIManager.OnInputStateChanged -= HandleChatInputStateChanged;
            }
        }

        /// <summary>
        /// 处理聊天输入状态改变
        /// </summary>
        /// <param name="isActive">是否激活</param>
        private void HandleChatInputStateChanged(bool isActive)
        {
            var previousState = isInputActive;
            isInputActive = isActive;

            // 更新输入模式
            if (globalInputManager != null)
            {
                var inputMode = isActive ? InputMode.Chat : InputMode.Game;
                globalInputManager.SetInputMode(inputMode);
            }

            // 更新游戏输入阻止状态
            if (gameInputBlocker != null)
            {
                gameInputBlocker.SetInputBlocked(isActive);
            }

            // 更新视觉反馈
            UpdateVisualFeedback(isActive);

            // 触发事件
            if (previousState != isActive)
            {
                OnInputStateChanged?.Invoke(isActive);
                LogDebug($"聊天输入状态改变: {isActive}");
            }
        }

        /// <summary>
        /// 更新视觉反馈
        /// </summary>
        /// <param name="isActive">是否激活</param>
        private void UpdateVisualFeedback(bool isActive)
        {
            if (!enableVisualFeedback || statusIndicator == null)
                return;

            if (isActive)
            {
                statusIndicator.ShowInputActive();
            }
            else
            {
                statusIndicator.ShowNormal();
            }
        }

        /// <summary>
        /// 检查输入状态变化
        /// </summary>
        private void CheckInputStateChange()
        {
            if (chatUIManager == null)
                return;

            bool currentInputState = chatUIManager.IsInputActive;
            if (isInputActive != currentInputState)
            {
                HandleChatInputStateChanged(currentInputState);
            }
        }

        /// <summary>
        /// 显示错误状态
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        public void ShowError(string errorMessage)
        {
            if (enableVisualFeedback && statusIndicator != null)
            {
                statusIndicator.ShowError(errorMessage);
            }
            LogDebug($"显示错误: {errorMessage}");
        }

        /// <summary>
        /// 显示警告状态
        /// </summary>
        /// <param name="warningMessage">警告消息</param>
        public void ShowWarning(string warningMessage)
        {
            if (enableVisualFeedback && statusIndicator != null)
            {
                statusIndicator.ShowWarning(warningMessage);
            }
            LogDebug($"显示警告: {warningMessage}");
        }

        /// <summary>
        /// 设置输入优先级
        /// </summary>
        /// <param name="priority">优先级</param>
        public void SetInputPriority(int priority)
        {
            if (inputPriority == priority)
                return;

            inputPriority = priority;

            // 重新注册以更新优先级
            if (isRegistered)
            {
                UnregisterFromGlobalInputManager();
                RegisterToGlobalInputManager();
            }
        }

        /// <summary>
        /// 启用或禁用视觉反馈
        /// </summary>
        /// <param name="enabled">是否启用</param>
        public void SetVisualFeedbackEnabled(bool enabled)
        {
            enableVisualFeedback = enabled;
            
            if (!enabled && statusIndicator != null)
            {
                statusIndicator.Hide();
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
                Debug.Log($"[ChatInputHandler] {message}");
            }
        }

        /// <summary>
        /// 组件启用时注册
        /// </summary>
        private void OnEnable()
        {
            if (!isRegistered)
            {
                RegisterToGlobalInputManager();
            }
        }

        /// <summary>
        /// 组件禁用时注销
        /// </summary>
        private void OnDisable()
        {
            UnregisterFromGlobalInputManager();
        }

        /// <summary>
        /// 组件销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            RemoveEventListeners();
            UnregisterFromGlobalInputManager();
        }
    }
}