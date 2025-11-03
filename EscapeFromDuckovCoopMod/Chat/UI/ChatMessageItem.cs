using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 聊天消息项UI组件
    /// </summary>
    public class ChatMessageItem : MonoBehaviour
    {
        [Header("消息显示组件")]
        [SerializeField] private TextMeshProUGUI userNameText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private TextMeshProUGUI timestampText;
        [SerializeField] private Image backgroundImage;

        [Header("样式设置")]
        [SerializeField] private Color normalMessageColor = Color.white;
        [SerializeField] private Color systemMessageColor = Color.yellow;
        [SerializeField] private Color errorMessageColor = Color.red;
        [SerializeField] private Color joinLeaveMessageColor = Color.green;

        private ChatMessage currentMessage;

        /// <summary>
        /// 设置消息内容
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void SetMessage(ChatMessage message)
        {
            if (message == null)
            {
                Debug.LogWarning("尝试设置空的聊天消息");
                return;
            }

            currentMessage = message;
            UpdateDisplay();
        }

        /// <summary>
        /// 更新显示内容
        /// </summary>
        private void UpdateDisplay()
        {
            if (currentMessage == null)
                return;

            // 设置时间戳
            if (timestampText != null)
            {
                timestampText.text = currentMessage.Timestamp.ToString("HH:mm");
            }

            // 根据消息类型设置显示内容和样式
            switch (currentMessage.Type)
            {
                case MessageType.Normal:
                    SetNormalMessage();
                    break;
                case MessageType.System:
                    SetSystemMessage();
                    break;
                case MessageType.Join:
                    SetJoinMessage();
                    break;
                case MessageType.Leave:
                    SetLeaveMessage();
                    break;
                case MessageType.Error:
                    SetErrorMessage();
                    break;
                default:
                    SetNormalMessage();
                    break;
            }
        }

        /// <summary>
        /// 设置普通消息显示
        /// </summary>
        private void SetNormalMessage()
        {
            if (userNameText != null)
            {
                userNameText.text = currentMessage.Sender?.GetDisplayName() ?? "未知用户";
                userNameText.color = normalMessageColor;
                userNameText.gameObject.SetActive(true);
            }

            if (messageText != null)
            {
                messageText.text = currentMessage.Content;
                messageText.color = normalMessageColor;
            }

            SetBackgroundColor(normalMessageColor, 0.1f);
        }

        /// <summary>
        /// 设置系统消息显示
        /// </summary>
        private void SetSystemMessage()
        {
            if (userNameText != null)
            {
                userNameText.text = "[系统]";
                userNameText.color = systemMessageColor;
                userNameText.gameObject.SetActive(true);
            }

            if (messageText != null)
            {
                messageText.text = currentMessage.Content;
                messageText.color = systemMessageColor;
                messageText.fontStyle = FontStyles.Italic;
            }

            SetBackgroundColor(systemMessageColor, 0.1f);
        }

        /// <summary>
        /// 设置加入消息显示
        /// </summary>
        private void SetJoinMessage()
        {
            if (userNameText != null)
            {
                userNameText.gameObject.SetActive(false);
            }

            if (messageText != null)
            {
                messageText.text = $"[系统] {currentMessage.Sender?.GetDisplayName() ?? "未知用户"} 加入了房间";
                messageText.color = joinLeaveMessageColor;
                messageText.fontStyle = FontStyles.Italic;
            }

            SetBackgroundColor(joinLeaveMessageColor, 0.1f);
        }

        /// <summary>
        /// 设置离开消息显示
        /// </summary>
        private void SetLeaveMessage()
        {
            if (userNameText != null)
            {
                userNameText.gameObject.SetActive(false);
            }

            if (messageText != null)
            {
                messageText.text = $"[系统] {currentMessage.Sender?.GetDisplayName() ?? "未知用户"} 离开了房间";
                messageText.color = joinLeaveMessageColor;
                messageText.fontStyle = FontStyles.Italic;
            }

            SetBackgroundColor(joinLeaveMessageColor, 0.1f);
        }

        /// <summary>
        /// 设置错误消息显示
        /// </summary>
        private void SetErrorMessage()
        {
            if (userNameText != null)
            {
                userNameText.text = "[错误]";
                userNameText.color = errorMessageColor;
                userNameText.gameObject.SetActive(true);
            }

            if (messageText != null)
            {
                messageText.text = currentMessage.Content;
                messageText.color = errorMessageColor;
                messageText.fontStyle = FontStyles.Bold;
            }

            SetBackgroundColor(errorMessageColor, 0.1f);
        }

        /// <summary>
        /// 设置背景颜色
        /// </summary>
        /// <param name="color">颜色</param>
        /// <param name="alpha">透明度</param>
        private void SetBackgroundColor(Color color, float alpha)
        {
            if (backgroundImage != null)
            {
                var bgColor = color;
                bgColor.a = alpha;
                backgroundImage.color = bgColor;
            }
        }

        /// <summary>
        /// 获取当前消息
        /// </summary>
        /// <returns>当前消息</returns>
        public ChatMessage GetMessage()
        {
            return currentMessage;
        }

        /// <summary>
        /// 初始化组件引用
        /// </summary>
        private void Awake()
        {
            // 如果没有手动设置组件引用，尝试自动查找
            if (userNameText == null)
                userNameText = transform.Find("UserName")?.GetComponent<TextMeshProUGUI>();
            
            if (messageText == null)
                messageText = transform.Find("Message")?.GetComponent<TextMeshProUGUI>();
            
            if (timestampText == null)
                timestampText = transform.Find("Timestamp")?.GetComponent<TextMeshProUGUI>();
            
            if (backgroundImage == null)
                backgroundImage = GetComponent<Image>();
        }
    }
}