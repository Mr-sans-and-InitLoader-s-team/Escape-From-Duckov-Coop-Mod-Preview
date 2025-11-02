using System;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;

namespace EscapeFromDuckovCoopMod.Chat.Services
{
    /// <summary>
    /// 聊天消息处理器
    /// </summary>
    public class ChatMessageHandler : IMessageHandler
    {
        private readonly HostChatService _hostService;

        public ChatMessageHandler(HostChatService hostService)
        {
            _hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
        }

        public void HandleMessage(ChatMessage message, string senderId)
        {
            try
            {
                // 验证发送者是否已连接
                if (!_hostService.IsClientConnected(senderId))
                {
                    Debug.LogWarning($"[ChatMessageHandler] 收到未连接客机的消息: {senderId}");
                    return;
                }

                // 更新客机活动时间
                var client = _hostService.GetClientInfo(senderId);
                if (client != null)
                {
                    client.LastActivity = DateTime.UtcNow;
                }

                // 验证消息内容
                if (string.IsNullOrEmpty(message.Content) || message.Content.Length > 500)
                {
                    Debug.LogWarning($"[ChatMessageHandler] 无效的消息内容，发送者: {senderId}");
                    return;
                }

                Debug.Log($"[ChatMessageHandler] 处理聊天消息: {message.Sender?.UserName} -> {message.Content}");

                // 聊天消息会通过消息路由器自动广播给其他客机
                // 这里可以添加额外的处理逻辑，如内容过滤、统计等
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatMessageHandler] 处理聊天消息时发生异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 系统消息处理器
    /// </summary>
    public class SystemMessageHandler : IMessageHandler
    {
        private readonly HostChatService _hostService;

        public SystemMessageHandler(HostChatService hostService)
        {
            _hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
        }

        public void HandleMessage(ChatMessage message, string senderId)
        {
            try
            {
                Debug.Log($"[SystemMessageHandler] 处理系统消息: {message.Content}");

                // 系统消息通常由服务器生成，客机发送的系统消息需要验证
                if (!string.IsNullOrEmpty(senderId))
                {
                    Debug.LogWarning($"[SystemMessageHandler] 客机尝试发送系统消息: {senderId}");
                    return;
                }

                // 处理系统消息逻辑
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SystemMessageHandler] 处理系统消息时发生异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 用户加入消息处理器
    /// </summary>
    public class JoinMessageHandler : IMessageHandler
    {
        private readonly HostChatService _hostService;

        public JoinMessageHandler(HostChatService hostService)
        {
            _hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
        }

        public void HandleMessage(ChatMessage message, string senderId)
        {
            try
            {
                Debug.Log($"[JoinMessageHandler] 处理用户加入消息: {message.Sender?.UserName}");

                // 用户加入消息通常由服务器在客机连接时自动生成
                // 这里可以添加额外的处理逻辑
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JoinMessageHandler] 处理用户加入消息时发生异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 用户离开消息处理器
    /// </summary>
    public class LeaveMessageHandler : IMessageHandler
    {
        private readonly HostChatService _hostService;

        public LeaveMessageHandler(HostChatService hostService)
        {
            _hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
        }

        public void HandleMessage(ChatMessage message, string senderId)
        {
            try
            {
                Debug.Log($"[LeaveMessageHandler] 处理用户离开消息: {message.Sender?.UserName}");

                // 用户离开消息通常由服务器在客机断开时自动生成
                // 这里可以添加额外的处理逻辑
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LeaveMessageHandler] 处理用户离开消息时发生异常: {ex.Message}");
            }
        }
    }
}