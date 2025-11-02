using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 聊天面板UI组件
    /// </summary>
    public class ChatPanel : MonoBehaviour
    {
        [Header("聊天面板组件")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private Transform messageContainer;
        [SerializeField] private GameObject messageItemPrefab;
        [SerializeField] private TextMeshProUGUI promptText;

        [Header("面板设置")]
        [SerializeField] private int maxVisibleMessages = 50;
        [SerializeField] private float autoScrollDelay = 0.1f;

        private List<ChatMessageItem> messageItems = new List<ChatMessageItem>();
        private bool isAutoScrollEnabled = true;

        /// <summary>
        /// 初始化聊天面板
        /// </summary>
        public void Initialize()
        {
            if (scrollRect == null)
            {
                scrollRect = GetComponentInChildren<ScrollRect>();
            }

            if (messageContainer == null && scrollRect != null)
            {
                messageContainer = scrollRect.content;
            }

            if (promptText != null)
            {
                promptText.text = "按 Enter 键开始聊天...";
            }

            // 清空现有消息
            ClearMessages();

            Debug.Log("聊天面板初始化完成");
        }

        /// <summary>
        /// 添加消息到面板
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void AddMessage(ChatMessage message)
        {
            if (message == null || !message.IsValid())
            {
                Debug.LogWarning("尝试添加无效的聊天消息");
                return;
            }

            // 创建消息项
            var messageItem = CreateMessageItem(message);
            if (messageItem != null)
            {
                messageItems.Add(messageItem);

                // 限制消息数量
                if (messageItems.Count > maxVisibleMessages)
                {
                    RemoveOldestMessage();
                }

                // 自动滚动到底部
                if (isAutoScrollEnabled)
                {
                    ScrollToBottom();
                }
            }
        }

        /// <summary>
        /// 批量添加消息
        /// </summary>
        /// <param name="messages">消息列表</param>
        public void AddMessages(List<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return;

            foreach (var message in messages)
            {
                AddMessage(message);
            }
        }

        /// <summary>
        /// 清空所有消息
        /// </summary>
        public void ClearMessages()
        {
            // 销毁现有消息项
            foreach (var item in messageItems)
            {
                if (item != null && item.gameObject != null)
                {
                    DestroyImmediate(item.gameObject);
                }
            }

            messageItems.Clear();

            // 清空容器中的所有子对象
            if (messageContainer != null)
            {
                for (int i = messageContainer.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(messageContainer.GetChild(i).gameObject);
                }
            }
        }

        /// <summary>
        /// 滚动到底部
        /// </summary>
        public void ScrollToBottom()
        {
            if (scrollRect != null)
            {
                // 延迟滚动以确保布局更新完成
                Invoke(nameof(DoScrollToBottom), autoScrollDelay);
            }
        }

        /// <summary>
        /// 设置自动滚动
        /// </summary>
        /// <param name="enabled">是否启用自动滚动</param>
        public void SetAutoScroll(bool enabled)
        {
            isAutoScrollEnabled = enabled;
        }

        /// <summary>
        /// 显示聊天面板
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 隐藏聊天面板
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 设置提示文本
        /// </summary>
        /// <param name="text">提示文本</param>
        public void SetPromptText(string text)
        {
            if (promptText != null)
            {
                promptText.text = text;
            }
        }

        /// <summary>
        /// 创建消息项
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>消息项组件</returns>
        private ChatMessageItem CreateMessageItem(ChatMessage message)
        {
            if (messageItemPrefab == null || messageContainer == null)
            {
                Debug.LogError("消息项预制体或容器未设置");
                return null;
            }

            var messageObj = Instantiate(messageItemPrefab, messageContainer);
            var messageItem = messageObj.GetComponent<ChatMessageItem>();

            if (messageItem == null)
            {
                messageItem = messageObj.AddComponent<ChatMessageItem>();
            }

            messageItem.SetMessage(message);
            return messageItem;
        }

        /// <summary>
        /// 移除最旧的消息
        /// </summary>
        private void RemoveOldestMessage()
        {
            if (messageItems.Count > 0)
            {
                var oldestItem = messageItems[0];
                messageItems.RemoveAt(0);

                if (oldestItem != null && oldestItem.gameObject != null)
                {
                    DestroyImmediate(oldestItem.gameObject);
                }
            }
        }

        /// <summary>
        /// 执行滚动到底部
        /// </summary>
        private void DoScrollToBottom()
        {
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        /// <summary>
        /// 检测用户是否在滚动
        /// </summary>
        private void Update()
        {
            if (scrollRect != null)
            {
                // 如果用户手动滚动到非底部位置，暂时禁用自动滚动
                if (scrollRect.verticalNormalizedPosition > 0.1f)
                {
                    isAutoScrollEnabled = false;
                }
                else if (scrollRect.verticalNormalizedPosition <= 0.1f)
                {
                    isAutoScrollEnabled = true;
                }
            }
        }

        /// <summary>
        /// 获取当前消息数量
        /// </summary>
        /// <returns>消息数量</returns>
        public int GetMessageCount()
        {
            return messageItems.Count;
        }

        /// <summary>
        /// 检查面板是否可见
        /// </summary>
        /// <returns>是否可见</returns>
        public bool IsVisible()
        {
            return gameObject.activeInHierarchy;
        }
    }
}