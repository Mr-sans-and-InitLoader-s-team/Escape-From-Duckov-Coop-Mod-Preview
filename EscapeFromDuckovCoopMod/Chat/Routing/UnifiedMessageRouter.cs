using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using EscapeFromDuckovCoopMod.Chat.Network;
using EscapeFromDuckovCoopMod.Chat.Services;
using EscapeFromDuckovCoopMod.Chat.Managers;

namespace EscapeFromDuckovCoopMod.Chat.Routing
{
    /// <summary>
    /// 统一消息路由器
    /// 支持本地、主机和客机模式的消息路由和转换
    /// </summary>
    public class UnifiedMessageRouter : MonoBehaviour
    {
        #region 字段和属性

        /// <summary>
        /// 聊天管理器引用
        /// </summary>
        private ChatManager _chatManager;

        /// <summary>
        /// 消息转换器
        /// </summary>
        private MessageConverter _messageConverter;

        /// <summary>
        /// 基础消息路由器（用于网络模式）
        /// </summary>
        private MessageRouter _baseMessageRouter;

        /// <summary>
        /// 消息验证器
        /// </summary>
        private MessageValidator _messageValidator;

        /// <summary>
        /// 消息去重器
        /// </summary>
        private MessageDeduplicator _messageDeduplicator;

        /// <summary>
        /// 路由统计信息
        /// </summary>
        private RoutingStatistics _routingStats;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 当前路由模式
        /// </summary>
        public RoutingMode CurrentMode { get; private set; } = RoutingMode.Local;

        /// <summary>
        /// 是否启用调试日志
        /// </summary>
        [SerializeField] private bool _enableDebugLog = true;

        #endregion

        #region 事件

        /// <summary>
        /// 消息路由成功事件
        /// </summary>
        public event Action<ChatMessage, RoutingResult> OnMessageRouted;

        /// <summary>
        /// 消息路由失败事件
        /// </summary>
        public event Action<ChatMessage, string> OnMessageRoutingFailed;

        /// <summary>
        /// 消息转换事件
        /// </summary>
        public event Action<ChatMessage, ChatMessage> OnMessageConverted;

        /// <summary>
        /// 路由模式变化事件
        /// </summary>
        public event Action<RoutingMode, RoutingMode> OnRoutingModeChanged;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化统一消息路由器
        /// </summary>
        /// <param name="chatManager">聊天管理器</param>
        public void Initialize(ChatManager chatManager)
        {
            try
            {
                _chatManager = chatManager ?? throw new ArgumentNullException(nameof(chatManager));

                // 初始化组件
                InitializeComponents();

                // 订阅聊天管理器事件
                SubscribeChatManagerEvents();

                IsInitialized = true;
                LogInfo("统一消息路由器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化统一消息路由器时发生异常: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 初始化组件
        /// </summary>
        private void InitializeComponents()
        {
            // 初始化消息转换器
            _messageConverter = new MessageConverter();

            // 初始化消息验证器
            _messageValidator = new MessageValidator();

            // 初始化消息去重器
            _messageDeduplicator = new MessageDeduplicator();

            // 初始化路由统计
            _routingStats = new RoutingStatistics();

            // 创建基础消息路由器
            var routerGO = new GameObject("BaseMessageRouter");
            routerGO.transform.SetParent(transform);
            _baseMessageRouter = routerGO.AddComponent<MessageRouter>();

            LogDebug("统一消息路由器组件初始化完成");
        }

        #endregion

        #region 路由模式管理

        /// <summary>
        /// 设置路由模式
        /// </summary>
        /// <param name="mode">路由模式</param>
        /// <param name="networkManager">网络管理器（网络模式需要）</param>
        public void SetRoutingMode(RoutingMode mode, NetworkManager networkManager = null)
        {
            try
            {
                var oldMode = CurrentMode;
                CurrentMode = mode;

                LogInfo($"路由模式变化: {oldMode} -> {mode}");

                // 根据模式配置路由器
                switch (mode)
                {
                    case RoutingMode.Local:
                        ConfigureLocalMode();
                        break;

                    case RoutingMode.Host:
                        ConfigureHostMode(networkManager);
                        break;

                    case RoutingMode.Client:
                        ConfigureClientMode(networkManager);
                        break;
                }

                OnRoutingModeChanged?.Invoke(oldMode, mode);
            }
            catch (Exception ex)
            {
                LogError($"设置路由模式时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 配置本地模式
        /// </summary>
        private void ConfigureLocalMode()
        {
            // 本地模式不需要网络路由器
            if (_baseMessageRouter != null)
            {
                _baseMessageRouter.Cleanup();
            }

            LogDebug("已配置本地路由模式");
        }

        /// <summary>
        /// 配置主机模式
        /// </summary>
        /// <param name="networkManager">网络管理器</param>
        private void ConfigureHostMode(NetworkManager networkManager)
        {
            if (networkManager == null)
            {
                throw new ArgumentNullException(nameof(networkManager), "主机模式需要网络管理器");
            }

            // 初始化基础消息路由器
            _baseMessageRouter.Initialize(networkManager);

            LogDebug("已配置主机路由模式");
        }

        /// <summary>
        /// 配置客机模式
        /// </summary>
        /// <param name="networkManager">网络管理器</param>
        private void ConfigureClientMode(NetworkManager networkManager)
        {
            if (networkManager == null)
            {
                throw new ArgumentNullException(nameof(networkManager), "客机模式需要网络管理器");
            }

            // 初始化基础消息路由器
            _baseMessageRouter.Initialize(networkManager);

            LogDebug("已配置客机路由模式");
        }

        #endregion

        #region 消息路由接口

        /// <summary>
        /// 路由消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="routingOptions">路由选项</param>
        /// <returns>路由结果</returns>
        public async Task<RoutingResult> RouteMessage(ChatMessage message, MessageRoutingOptions routingOptions = null)
        {
            if (!IsInitialized)
            {
                return new RoutingResult { Success = false, ErrorMessage = "路由器未初始化" };
            }

            if (message == null)
            {
                return new RoutingResult { Success = false, ErrorMessage = "消息不能为空" };
            }

            try
            {
                // 使用默认路由选项
                routingOptions = routingOptions ?? new MessageRoutingOptions();

                LogDebug($"开始路由消息 ({CurrentMode}): {message.GetDisplayText()}");

                // 验证消息
                var validationResult = _messageValidator.ValidateMessage(message);
                if (!validationResult.IsValid)
                {
                    return new RoutingResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"消息验证失败: {validationResult.ErrorMessage}" 
                    };
                }

                // 检查消息重复
                if (routingOptions.EnableDeduplication && _messageDeduplicator.IsDuplicate(message))
                {
                    return new RoutingResult 
                    { 
                        Success = false, 
                        ErrorMessage = "检测到重复消息" 
                    };
                }

                // 根据当前模式路由消息
                var result = await RouteMessageByMode(message, routingOptions);

                // 更新统计信息
                UpdateRoutingStatistics(result);

                // 触发事件
                if (result.Success)
                {
                    OnMessageRouted?.Invoke(message, result);
                }
                else
                {
                    OnMessageRoutingFailed?.Invoke(message, result.ErrorMessage);
                }

                return result;
            }
            catch (Exception ex)
            {
                LogError($"路由消息时发生异常: {ex.Message}");
                return new RoutingResult 
                { 
                    Success = false, 
                    ErrorMessage = $"路由异常: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// 根据模式路由消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="options">路由选项</param>
        /// <returns>路由结果</returns>
        private async Task<RoutingResult> RouteMessageByMode(ChatMessage message, MessageRoutingOptions options)
        {
            switch (CurrentMode)
            {
                case RoutingMode.Local:
                    return await RouteLocalMessage(message, options);

                case RoutingMode.Host:
                    return await RouteHostMessage(message, options);

                case RoutingMode.Client:
                    return await RouteClientMessage(message, options);

                default:
                    return new RoutingResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"不支持的路由模式: {CurrentMode}" 
                    };
            }
        }

        /// <summary>
        /// 路由本地消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="options">路由选项</param>
        /// <returns>路由结果</returns>
        private async Task<RoutingResult> RouteLocalMessage(ChatMessage message, MessageRoutingOptions options)
        {
            try
            {
                // 本地模式直接通过聊天管理器处理
                _chatManager.ReceiveNetworkMessage(message);

                return new RoutingResult
                {
                    Success = true,
                    RouteId = message.Id,
                    RoutingMode = RoutingMode.Local,
                    ProcessedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new RoutingResult
                {
                    Success = false,
                    ErrorMessage = $"本地路由失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 路由主机消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="options">路由选项</param>
        /// <returns>路由结果</returns>
        private async Task<RoutingResult> RouteHostMessage(ChatMessage message, MessageRoutingOptions options)
        {
            try
            {
                // 转换为网络消息
                var networkMessage = _messageConverter.ConvertLocalToNetwork(message);
                if (networkMessage == null)
                {
                    return new RoutingResult
                    {
                        Success = false,
                        ErrorMessage = "消息转换失败"
                    };
                }

                // 触发消息转换事件
                OnMessageConverted?.Invoke(message, networkMessage);

                // 通过基础路由器发送
                string routeId;
                if (options.TargetClientId != null)
                {
                    // 单播消息
                    routeId = _baseMessageRouter.SendUnicastMessage(
                        networkMessage, 
                        options.TargetClientId, 
                        options.Priority, 
                        options.RequireAcknowledgment);
                }
                else
                {
                    // 广播消息
                    routeId = _baseMessageRouter.BroadcastMessage(
                        networkMessage, 
                        options.Priority, 
                        options.RequireAcknowledgment);
                }

                // 同时在本地显示
                _chatManager.ReceiveNetworkMessage(message);

                return new RoutingResult
                {
                    Success = !string.IsNullOrEmpty(routeId),
                    RouteId = routeId,
                    RoutingMode = RoutingMode.Host,
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = string.IsNullOrEmpty(routeId) ? "网络路由失败" : null
                };
            }
            catch (Exception ex)
            {
                return new RoutingResult
                {
                    Success = false,
                    ErrorMessage = $"主机路由失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 路由客机消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="options">路由选项</param>
        /// <returns>路由结果</returns>
        private async Task<RoutingResult> RouteClientMessage(ChatMessage message, MessageRoutingOptions options)
        {
            try
            {
                // 转换为网络消息
                var networkMessage = _messageConverter.ConvertLocalToNetwork(message);
                if (networkMessage == null)
                {
                    return new RoutingResult
                    {
                        Success = false,
                        ErrorMessage = "消息转换失败"
                    };
                }

                // 触发消息转换事件
                OnMessageConverted?.Invoke(message, networkMessage);

                // 客机只能发送给主机
                var routeId = _baseMessageRouter.SendUnicastMessage(
                    networkMessage, 
                    "host", // 主机ID
                    options.Priority, 
                    options.RequireAcknowledgment);

                // 在本地显示发送的消息
                _chatManager.ReceiveNetworkMessage(message);

                return new RoutingResult
                {
                    Success = !string.IsNullOrEmpty(routeId),
                    RouteId = routeId,
                    RoutingMode = RoutingMode.Client,
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = string.IsNullOrEmpty(routeId) ? "客机路由失败" : null
                };
            }
            catch (Exception ex)
            {
                return new RoutingResult
                {
                    Success = false,
                    ErrorMessage = $"客机路由失败: {ex.Message}"
                };
            }
        }

        #endregion

        #region 消息接收处理

        /// <summary>
        /// 处理接收到的网络消息
        /// </summary>
        /// <param name="networkMessage">网络消息</param>
        /// <param name="senderId">发送者ID</param>
        public void HandleIncomingNetworkMessage(ChatMessage networkMessage, string senderId)
        {
            try
            {
                if (networkMessage == null)
                {
                    LogWarning("接收到空的网络消息");
                    return;
                }

                LogDebug($"处理接收消息: {networkMessage.GetDisplayText()}, 发送者: {senderId}");

                // 验证消息
                var validationResult = _messageValidator.ValidateMessage(networkMessage);
                if (!validationResult.IsValid)
                {
                    LogWarning($"接收消息验证失败: {validationResult.ErrorMessage}");
                    return;
                }

                // 检查重复
                if (_messageDeduplicator.IsDuplicate(networkMessage))
                {
                    LogDebug($"检测到重复消息，跳过: {networkMessage.Id}");
                    return;
                }

                // 转换为显示消息
                var displayMessage = _messageConverter.ConvertNetworkToDisplay(networkMessage);
                if (displayMessage == null)
                {
                    LogWarning("网络消息转换为显示消息失败");
                    return;
                }

                // 触发消息转换事件
                OnMessageConverted?.Invoke(networkMessage, displayMessage);

                // 通过聊天管理器显示消息
                _chatManager.ReceiveNetworkMessage(displayMessage);

                // 更新统计信息
                _routingStats.TotalMessagesReceived++;
            }
            catch (Exception ex)
            {
                LogError($"处理接收网络消息时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 事件订阅

        /// <summary>
        /// 订阅聊天管理器事件
        /// </summary>
        private void SubscribeChatManagerEvents()
        {
            if (_chatManager != null)
            {
                _chatManager.OnChatModeChanged += HandleChatModeChanged;
            }
        }

        /// <summary>
        /// 取消订阅聊天管理器事件
        /// </summary>
        private void UnsubscribeChatManagerEvents()
        {
            if (_chatManager != null)
            {
                _chatManager.OnChatModeChanged -= HandleChatModeChanged;
            }
        }

        /// <summary>
        /// 处理聊天模式变化
        /// </summary>
        /// <param name="oldMode">旧模式</param>
        /// <param name="newMode">新模式</param>
        private void HandleChatModeChanged(ChatManager.ChatMode oldMode, ChatManager.ChatMode newMode)
        {
            // 将聊天模式映射到路由模式
            var routingMode = newMode switch
            {
                ChatManager.ChatMode.Local => RoutingMode.Local,
                ChatManager.ChatMode.Host => RoutingMode.Host,
                ChatManager.ChatMode.Client => RoutingMode.Client,
                _ => RoutingMode.Local
            };

            SetRoutingMode(routingMode, NetworkManager.Instance);
        }

        #endregion

        #region 统计信息

        /// <summary>
        /// 更新路由统计信息
        /// </summary>
        /// <param name="result">路由结果</param>
        private void UpdateRoutingStatistics(RoutingResult result)
        {
            _routingStats.TotalMessagesProcessed++;

            if (result.Success)
            {
                _routingStats.TotalMessagesRouted++;
            }
            else
            {
                _routingStats.TotalMessagesFailed++;
            }
        }

        /// <summary>
        /// 获取路由统计信息
        /// </summary>
        /// <returns>路由统计信息</returns>
        public RoutingStatistics GetRoutingStatistics()
        {
            return _routingStats.Clone();
        }

        /// <summary>
        /// 重置路由统计信息
        /// </summary>
        public void ResetRoutingStatistics()
        {
            _routingStats.Reset();
            LogInfo("路由统计信息已重置");
        }

        #endregion

        #region 清理

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            try
            {
                // 取消事件订阅
                UnsubscribeChatManagerEvents();

                // 清理组件
                _baseMessageRouter?.Cleanup();
                _messageDeduplicator?.Cleanup();

                IsInitialized = false;
                LogInfo("统一消息路由器已清理");
            }
            catch (Exception ex)
            {
                LogError($"清理统一消息路由器时发生异常: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[UnifiedMessageRouter] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[UnifiedMessageRouter] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[UnifiedMessageRouter] {message}");
        }

        private void LogDebug(string message)
        {
            if (_enableDebugLog)
            {
                Debug.Log($"[UnifiedMessageRouter][DEBUG] {message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// 路由模式枚举
    /// </summary>
    public enum RoutingMode
    {
        /// <summary>
        /// 本地模式
        /// </summary>
        Local,

        /// <summary>
        /// 主机模式
        /// </summary>
        Host,

        /// <summary>
        /// 客机模式
        /// </summary>
        Client
    }

    /// <summary>
    /// 消息路由选项
    /// </summary>
    public class MessageRoutingOptions
    {
        /// <summary>
        /// 目标客机ID（null表示广播）
        /// </summary>
        public string TargetClientId { get; set; }

        /// <summary>
        /// 消息优先级
        /// </summary>
        public MessagePriority Priority { get; set; } = MessagePriority.Normal;

        /// <summary>
        /// 是否需要确认
        /// </summary>
        public bool RequireAcknowledgment { get; set; } = false;

        /// <summary>
        /// 是否启用去重
        /// </summary>
        public bool EnableDeduplication { get; set; } = true;

        /// <summary>
        /// 路由超时时间（毫秒）
        /// </summary>
        public int TimeoutMs { get; set; } = 10000;
    }

    /// <summary>
    /// 路由结果
    /// </summary>
    public class RoutingResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 路由ID
        /// </summary>
        public string RouteId { get; set; }

        /// <summary>
        /// 路由模式
        /// </summary>
        public RoutingMode RoutingMode { get; set; }

        /// <summary>
        /// 处理时间
        /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 路由统计信息
    /// </summary>
    public class RoutingStatistics
    {
        /// <summary>
        /// 总处理消息数
        /// </summary>
        public long TotalMessagesProcessed { get; set; }

        /// <summary>
        /// 总路由消息数
        /// </summary>
        public long TotalMessagesRouted { get; set; }

        /// <summary>
        /// 总接收消息数
        /// </summary>
        public long TotalMessagesReceived { get; set; }

        /// <summary>
        /// 总失败消息数
        /// </summary>
        public long TotalMessagesFailed { get; set; }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            TotalMessagesProcessed = 0;
            TotalMessagesRouted = 0;
            TotalMessagesReceived = 0;
            TotalMessagesFailed = 0;
        }

        /// <summary>
        /// 克隆统计信息
        /// </summary>
        /// <returns>统计信息副本</returns>
        public RoutingStatistics Clone()
        {
            return new RoutingStatistics
            {
                TotalMessagesProcessed = TotalMessagesProcessed,
                TotalMessagesRouted = TotalMessagesRouted,
                TotalMessagesReceived = TotalMessagesReceived,
                TotalMessagesFailed = TotalMessagesFailed
            };
        }
    }
}