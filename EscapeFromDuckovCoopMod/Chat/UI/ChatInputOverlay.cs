using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 聊天输入覆盖层UI组件 - 使用OnGUI实现
    /// </summary>
    public class ChatInputOverlay : MonoBehaviour
    {
        [Header("覆盖层设置")]
        [SerializeField] private Color maskColor = new Color(0, 0, 0, 0.7f);
        [SerializeField] private int maxMessageLength = 200;

        /// <summary>
        /// 消息发送事件
        /// </summary>
        public event Action<string> OnMessageSent;

        /// <summary>
        /// 覆盖层关闭事件
        /// </summary>
        public event Action OnOverlayClosed;

        private bool isVisible = false;
        private string inputText = "";
        private bool shouldFocusInput = false;

        /// <summary>
        /// 初始化覆盖层
        /// </summary>
        public void Initialize()
        {
            // 初始状态为隐藏
            isVisible = false;
            inputText = "";
            
            Debug.Log("聊天输入覆盖层初始化完成");
        }

        /// <summary>
        /// 显示覆盖层
        /// </summary>
        public void Show()
        {
            if (isVisible)
                return;

            isVisible = true;
            inputText = "";
            shouldFocusInput = true;

            Debug.Log("聊天输入覆盖层已显示");
        }

        /// <summary>
        /// 隐藏覆盖层
        /// </summary>
        public void Hide()
        {
            if (!isVisible)
                return;

            isVisible = false;
            inputText = "";
            shouldFocusInput = false;

            OnOverlayClosed?.Invoke();

            Debug.Log("聊天输入覆盖层已隐藏");
        }

        /// <summary>
        /// 切换覆盖层显示状态
        /// </summary>
        public void Toggle()
        {
            if (isVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        /// <summary>
        /// 检查覆盖层是否可见
        /// </summary>
        /// <returns>是否可见</returns>
        public bool IsVisible()
        {
            return isVisible;
        }

        /// <summary>
        /// 获取当前输入文本
        /// </summary>
        /// <returns>输入文本</returns>
        public string GetInputText()
        {
            return inputText;
        }

        /// <summary>
        /// 设置输入文本
        /// </summary>
        /// <param name="text">文本内容</param>
        public void SetInputText(string text)
        {
            inputText = text ?? string.Empty;
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        private void SendMessage()
        {
            Debug.Log($"[ChatInputOverlay] SendMessage被调用，当前输入文本: '{inputText}'");
            
            var message = inputText.Trim();

            // 验证消息内容
            if (string.IsNullOrEmpty(message))
            {
                Debug.Log("[ChatInputOverlay] 消息内容为空，不发送");
                return;
            }

            Debug.Log($"[ChatInputOverlay] 准备发送消息: '{message}'");

            // 触发消息发送事件
            OnMessageSent?.Invoke(message);

            // 隐藏覆盖层
            Hide();
        }

        /// <summary>
        /// 处理键盘输入
        /// </summary>
        private void Update()
        {
            if (!isVisible)
                return;

            // 阻止所有游戏输入事件
            BlockGameInput();
        }
        
        /// <summary>
        /// 阻止游戏输入事件
        /// </summary>
        private void BlockGameInput()
        {
            // 阻止所有输入事件传递到游戏
            UnityEngine.Input.ResetInputAxes();
        }
        
        /// <summary>
        /// 处理键盘输入事件
        /// </summary>
        private void HandleKeyboardInput()
        {
            var currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    Debug.Log($"[ChatInputOverlay] Enter键被按下，当前输入文本: '{inputText}'");
                    SendMessage();
                    currentEvent.Use(); // 消费事件，防止传递到游戏
                }
                else if (currentEvent.keyCode == KeyCode.Escape)
                {
                    Debug.Log("[ChatInputOverlay] ESC键被按下，关闭覆盖层");
                    Hide();
                    currentEvent.Use(); // 消费事件，防止传递到游戏
                }
                else
                {
                    // 消费所有其他按键事件，防止传递到游戏
                    currentEvent.Use();
                }
            }
        }

        /// <summary>
        /// OnGUI绘制聊天输入界面
        /// </summary>
        private void OnGUI()
        {
            if (!isVisible)
                return;

            // 绘制半透明背景遮罩
            GUI.color = maskColor;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 计算输入对话框位置和大小
            float dialogWidth = 400f;
            float dialogHeight = 120f;
            float dialogX = (Screen.width - dialogWidth) * 0.5f;
            float dialogY = (Screen.height - dialogHeight) * 0.5f;

            // 绘制输入对话框背景
            var dialogRect = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
            GUI.Box(dialogRect, "");

            // 绘制标题
            var titleRect = new Rect(dialogX + 10, dialogY + 10, dialogWidth - 20, 25);
            GUI.Label(titleRect, "聊天输入", GUI.skin.label);

            // 绘制输入框
            var inputRect = new Rect(dialogX + 10, dialogY + 40, dialogWidth - 20, 25);
            GUI.SetNextControlName("ChatInput");
            
            var newInputText = GUI.TextField(inputRect, inputText, maxMessageLength);
            if (newInputText != inputText)
            {
                inputText = newInputText;
            }

            // 自动聚焦到输入框
            if (shouldFocusInput)
            {
                GUI.FocusControl("ChatInput");
                shouldFocusInput = false;
            }

            // 处理键盘事件 - 在TextField更新之后
            HandleKeyboardInput();

            // 绘制发送按钮
            var sendButtonRect = new Rect(dialogX + dialogWidth - 80, dialogY + 75, 70, 25);
            if (GUI.Button(sendButtonRect, "发送"))
            {
                SendMessage();
            }

            // 绘制取消按钮
            var cancelButtonRect = new Rect(dialogX + dialogWidth - 160, dialogY + 75, 70, 25);
            if (GUI.Button(cancelButtonRect, "取消"))
            {
                Hide();
            }

            // 绘制提示文本
            var hintRect = new Rect(dialogX + 10, dialogY + 75, 200, 25);
            GUI.Label(hintRect, "按 Enter 发送，ESC 取消", GUI.skin.label);
        }
    }
}