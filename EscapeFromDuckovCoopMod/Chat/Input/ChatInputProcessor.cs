using System;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Input
{
    /// <summary>
    /// 聊天输入处理器
    /// </summary>
    public class ChatInputProcessor
    {
        /// <summary>
        /// 消息处理完成事件
        /// </summary>
        public event Action<ChatMessage> OnMessageProcessed;

        /// <summary>
        /// 输入验证失败事件
        /// </summary>
        public event Action<string> OnValidationFailed;

        /// <summary>
        /// 消息发送频率限制事件
        /// </summary>
        public event Action OnRateLimited;

        private DateTime lastMessageTime = DateTime.MinValue;
        private float messageInterval = 1.0f; // 消息发送间隔（秒）
        private UserInfo currentUser;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="user">当前用户信息</param>
        public ChatInputProcessor(UserInfo user)
        {
            currentUser = user;
        }

        /// <summary>
        /// 设置当前用户
        /// </summary>
        /// <param name="user">用户信息</param>
        public void SetCurrentUser(UserInfo user)
        {
            currentUser = user;
        }

        /// <summary>
        /// 设置消息发送间隔
        /// </summary>
        /// <param name="interval">间隔时间（秒）</param>
        public void SetMessageInterval(float interval)
        {
            messageInterval = Math.Max(0.1f, interval);
        }

        /// <summary>
        /// 处理输入消息
        /// </summary>
        /// <param name="inputText">输入文本</param>
        /// <returns>是否处理成功</returns>
        public bool ProcessInput(string inputText)
        {
            try
            {
                // 检查发送频率
                if (!ChatInputValidator.CheckMessageFrequency(lastMessageTime, messageInterval))
                {
                    OnRateLimited?.Invoke();
                    Debug.LogWarning("消息发送过于频繁");
                    return false;
                }

                // 验证输入
                var validationResult = ChatInputValidator.ValidateMessage(inputText);
                if (!validationResult.IsValid)
                {
                    OnValidationFailed?.Invoke(validationResult.ErrorMessage);
                    Debug.LogWarning($"消息验证失败: {validationResult.ErrorMessage}");
                    return false;
                }

                // 创建聊天消息
                var chatMessage = CreateChatMessage(validationResult.CleanedMessage);
                if (chatMessage == null)
                {
                    OnValidationFailed?.Invoke("创建消息失败");
                    return false;
                }

                // 更新最后发送时间
                lastMessageTime = DateTime.UtcNow;

                // 触发消息处理完成事件
                OnMessageProcessed?.Invoke(chatMessage);

                Debug.Log($"消息处理成功: {chatMessage.Content}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理输入消息时发生错误: {ex.Message}");
                OnValidationFailed?.Invoke("消息处理失败");
                return false;
            }
        }

        /// <summary>
        /// 创建聊天消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <returns>聊天消息对象</returns>
        private ChatMessage CreateChatMessage(string content)
        {
            if (currentUser == null)
            {
                Debug.LogError("当前用户信息为空，无法创建消息");
                return null;
            }

            var message = new ChatMessage
            {
                Content = content,
                Sender = currentUser,
                Type = MessageType.Normal,
                Timestamp = DateTime.UtcNow
            };

            // 添加额外的元数据
            message.Metadata["processed_at"] = DateTime.UtcNow;
            message.Metadata["client_version"] = Application.version;

            return message;
        }

        /// <summary>
        /// 创建系统消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <param name="messageType">消息类型</param>
        /// <returns>系统消息对象</returns>
        public ChatMessage CreateSystemMessage(string content, MessageType messageType = MessageType.System)
        {
            var systemUser = new UserInfo
            {
                SteamId = 0,
                UserName = "系统",
                DisplayName = "系统"
            };

            var message = new ChatMessage
            {
                Content = content,
                Sender = systemUser,
                Type = messageType,
                Timestamp = DateTime.UtcNow
            };

            message.Metadata["is_system"] = true;
            return message;
        }

        /// <summary>
        /// 创建用户加入消息
        /// </summary>
        /// <param name="user">加入的用户</param>
        /// <returns>加入消息对象</returns>
        public ChatMessage CreateUserJoinMessage(UserInfo user)
        {
            if (user == null)
                return null;

            var message = new ChatMessage
            {
                Content = $"{user.GetDisplayName()} 加入了房间",
                Sender = user,
                Type = MessageType.Join,
                Timestamp = DateTime.UtcNow
            };

            message.Metadata["user_action"] = "join";
            return message;
        }

        /// <summary>
        /// 创建用户离开消息
        /// </summary>
        /// <param name="user">离开的用户</param>
        /// <returns>离开消息对象</returns>
        public ChatMessage CreateUserLeaveMessage(UserInfo user)
        {
            if (user == null)
                return null;

            var message = new ChatMessage
            {
                Content = $"{user.GetDisplayName()} 离开了房间",
                Sender = user,
                Type = MessageType.Leave,
                Timestamp = DateTime.UtcNow
            };

            message.Metadata["user_action"] = "leave";
            return message;
        }

        /// <summary>
        /// 创建错误消息
        /// </summary>
        /// <param name="errorContent">错误内容</param>
        /// <returns>错误消息对象</returns>
        public ChatMessage CreateErrorMessage(string errorContent)
        {
            var systemUser = new UserInfo
            {
                SteamId = 0,
                UserName = "系统",
                DisplayName = "系统"
            };

            var message = new ChatMessage
            {
                Content = errorContent,
                Sender = systemUser,
                Type = MessageType.Error,
                Timestamp = DateTime.UtcNow
            };

            message.Metadata["is_error"] = true;
            return message;
        }

        /// <summary>
        /// 检查是否可以发送消息
        /// </summary>
        /// <returns>是否可以发送</returns>
        public bool CanSendMessage()
        {
            return ChatInputValidator.CheckMessageFrequency(lastMessageTime, messageInterval);
        }

        /// <summary>
        /// 获取距离下次可发送消息的剩余时间
        /// </summary>
        /// <returns>剩余时间（秒）</returns>
        public float GetTimeUntilNextMessage()
        {
            var timeSinceLastMessage = (DateTime.UtcNow - lastMessageTime).TotalSeconds;
            var remainingTime = messageInterval - timeSinceLastMessage;
            return Math.Max(0f, (float)remainingTime);
        }

        /// <summary>
        /// 重置发送时间限制
        /// </summary>
        public void ResetRateLimit()
        {
            lastMessageTime = DateTime.MinValue;
        }

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        /// <returns>当前用户信息</returns>
        public UserInfo GetCurrentUser()
        {
            return currentUser;
        }
    }
}