using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using EscapeFromDuckovCoopMod.Chat.Services;
using EscapeFromDuckovCoopMod.Chat.Network;
using EscapeFromDuckovCoopMod.Chat.Data;

namespace EscapeFromDuckovCoopMod.Chat.Managers
{
    /// <summary>
    /// 统一聊天管理器
    /// 支持本地模式和网络模式的切换，提供统一的聊天接口
    /// </summary>
    public class ChatManager : MonoBehaviour
    {
        #region 枚举定义

        /// <summary>
        /// 聊天模式枚举
        /// </summary>
        public enum ChatMode
        {
            /// <summary>
            /// 本地模式 - 仅本地聊天显示
            /// </summary>
            Local,
            
            /// <summary>
            /// 主机模式 - 作为聊天服务主机
            /// </summary>
            Host,
            
            /// <summary>
            /// 客机模式 - 连接到聊天服务主机
            /// </summary>
            Client
        }

        #endregion

        #region 单例模式

        private static ChatManager _instance;

        /// <summary>
        /// 聊天管理器单例实例
        /// </summary>
        public static ChatManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ChatManager");
                    _instance = go.AddComponent<ChatManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        #endregion

        #region 字段和属性

        /// <summary>
        /// 当前聊天模式
        /// </summary>
        public ChatMode CurrentMode { get; private set; } = ChatMode.Local;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 是否已连接（网络模式下）
        /// </summary>
        public bool IsConnected => CurrentMode switch
        {
            ChatMode.Local => true,
            ChatMode.Host => _hostChatService?.IsServiceRunning ?? false,
            ChatMode.Client => _clientChatHandler?.IsConnectedToHost ?? false,
            _ => false
        };

        /// <summary>
        /// 当前用户信息
        /// </summary>
        public UserInfo CurrentUser { get; private set; }

        /// <summary>
        /// 本地聊天管理器
        /// </summary>
        private LocalChatManager _localChatManager;

        /// <summary>
        /// 主机聊天服务
        /// </summary>
        private HostChatService _hostChatService;

        /// <summary>
        /// 客机聊天处理器
        /// </summary>
        private ClientChatHandler _clientChatHandler;

        /// <summary>
        /// Steam 用户服务
        /// </summary>
        private ISteamUserService _steamUserService;

        /// <summary>
        /// 聊天历史管理器
        /// </summary>
        private ChatHistoryManager _historyManager;

        /// <summary>
        /// 网络管理器
        /// </summary>
        private NetworkManager _networkManager;

        /// <summary>
        /// 消息转换器
        /// </summary>
        private MessageConverter _messageConverter;

        /// <summary>
        /// 消息路由器
        /// </summary>
        private MessageRouter _messageRouter;

        /// <summary>
        /// 网络状态监控器
        /// </summary>
        private NetworkStatusMonitor _networkStatusMonitor;

        /// <summary>
        /// 是否启用调试日志
        /// </summary>
        [SerializeField] private bool _enableDebugLog = true;

        #endregion

        #region 事件

        /// <summary>
        /// 聊天模式变化事件
        /// </summary>
        public event Action<ChatMode, ChatMode> OnChatModeChanged;

        /// <summary>
        /// 消息接收事件
        /// </summary>
        public event Action<ChatMessage> OnMessageReceived;

        /// <summary>
        /// 消息发送事件
        /// </summary>
        public event Action<ChatMessage> OnMessageSent;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event Action<bool> OnConnectionStatusChanged;

        /// <summary>
        /// 网络状态变化事件
        /// </summary>
        public event Action<NetworkStatus> OnNetworkStatusChanged;

        /// <summary>
        /// 聊天历史同步事件
        /// </summary>
        public event Action<List<ChatMessage>> OnHistorySynced;

        /// <summary>
        /// 聊天错误事件
        /// </summary>
        public event Action<string> OnChatError;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            LogInfo("聊天管理器实例已创建");
        }

        private void Start()
        {
            // 延迟初始化，确保其他系统已准备就绪
            Invoke(nameof(InitializeAsync), 0.1f);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                LogInfo("聊天管理器正在销毁");
                Shutdown();
                _instance = null;
            }
        }

        #endregion

        #region 初始化方法

        /// <summary>
        /// 异步初始化聊天管理器
        /// </summary>
        private async void InitializeAsync()
        {
            try
            {
                LogInfo("开始初始化聊天管理器...");

                // 初始化核心组件
                await InitializeCoreComponents();

                // 初始化本地聊天管理器
                InitializeLocalChatManager();

                // 初始化网络组件
                InitializeNetworkComponents();

                // 初始化消息处理组件
                InitializeMessageProcessing();

                // 设置默认为本地模式
                await SwitchToLocalMode();

                IsInitialized = true;
                LogInfo("聊天管理器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化聊天管理器时发生异常: {ex.Message}");
                OnChatError?.Invoke($"聊天系统初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化核心组件
        /// </summary>
        private async Task InitializeCoreComponents()
        {
            LogInfo("正在初始化核心组件...");

            // 初始化 Steam 用户服务
            _steamUserService = new SteamUserService();
            await _steamUserService.InitializeSteamAPI();

            // 获取当前用户信息
            CurrentUser = await _steamUserService.GetCurrentUserInfo();
            if (CurrentUser == null)
            {
                throw new Exception("无法获取当前用户信息");
            }

            // 初始化聊天历史管理器
            _historyManager = new ChatHistoryManager();

            LogInfo($"核心组件初始化完成，当前用户: {CurrentUser.GetDisplayName()}");
        }

        /// <summary>
        /// 初始化本地聊天管理器
        /// </summary>
        private void InitializeLocalChatManager()
        {
            LogInfo("正在初始化本地聊天管理器...");

            // 获取或创建本地聊天管理器实例
            _localChatManager = LocalChatManager.GetOrCreateInstance();

            // 订阅本地聊天事件
            _localChatManager.OnMessageSent += HandleLocalMessageSent;
            _localChatManager.OnMessageReceived += HandleLocalMessageReceived;
            _localChatManager.OnMessageValidationFailed += HandleMessageValidationFailed;

            LogInfo("本地聊天管理器初始化完成");
        }

        /// <summary>
        /// 初始化网络组件
        /// </summary>
        private void InitializeNetworkComponents()
        {
            LogInfo("正在初始化网络组件...");

            // 获取网络管理器实例
            _networkManager = NetworkManager.Instance;

            // 获取主机聊天服务实例
            _hostChatService = HostChatService.Instance;

            // 获取或创建客机聊天处理器实例
            _clientChatHandler = ClientChatHandler.GetOrCreateInstance();

            // 初始化网络状态监控器
            _networkStatusMonitor = new NetworkStatusMonitor(_networkManager);

            // 订阅网络事件
            SubscribeNetworkEvents();

            LogInfo("网络组件初始化完成");
        }

        /// <summary>
        /// 初始化消息处理组件
        /// </summary>
        private void InitializeMessageProcessing()
        {
            LogInfo("正在初始化消息处理组件...");

            // 初始化消息转换器
            _messageConverter = new MessageConverter();

            // 初始化消息路由器（将在网络模式下使用）
            var routerGO = new GameObject("MessageRouter");
            routerGO.transform.SetParent(transform);
            _messageRouter = routerGO.AddComponent<MessageRouter>();

            LogInfo("消息处理组件初始化完成");
        }

        #endregion

        #region 聊天模式切换

        /// <summary>
        /// 切换到本地模式
        /// </summary>
        /// <returns>切换是否成功</returns>
        public async Task<bool> SwitchToLocalMode()
        {
            try
            {
                LogInfo("切换到本地聊天模式...");

                var oldMode = CurrentMode;

                // 停止网络服务
                await StopNetworkServices();

                // 确保本地聊天管理器已初始化
                if (!_localChatManager.IsInitialized)
                {
                    _localChatManager.Initialize();
                    await Task.Delay(100); // 等待初始化完成
                }

                CurrentMode = ChatMode.Local;

                LogInfo("已切换到本地聊天模式");
                OnChatModeChanged?.Invoke(oldMode, CurrentMode);
                OnConnectionStatusChanged?.Invoke(IsConnected);

                return true;
            }
            catch (Exception ex)
            {
                LogError($"切换到本地模式时发生异常: {ex.Message}");
                OnChatError?.Invoke($"切换到本地模式失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切换到主机模式
        /// </summary>
        /// <param name="config">主机服务配置</param>
        /// <returns>切换是否成功</returns>
        public async Task<bool> SwitchToHostMode(HostServiceConfig config = null)
        {
            try
            {
                LogInfo("切换到主机聊天模式...");

                var oldMode = CurrentMode;

                // 停止其他服务
                await StopNetworkServices();

                // 启动主机聊天服务
                bool serviceStarted = await _hostChatService.StartService(config);
                if (!serviceStarted)
                {
                    throw new Exception("主机聊天服务启动失败");
                }

                // 初始化消息路由器
                _messageRouter.Initialize(_networkManager);

                // 订阅主机服务事件
                SubscribeHostServiceEvents();

                // 将本地历史消息同步到主机服务
                await SyncLocalHistoryToHost();

                CurrentMode = ChatMode.Host;

                LogInfo("已切换到主机聊天模式");
                OnChatModeChanged?.Invoke(oldMode, CurrentMode);
                OnConnectionStatusChanged?.Invoke(IsConnected);

                return true;
            }
            catch (Exception ex)
            {
                LogError($"切换到主机模式时发生异常: {ex.Message}");
                OnChatError?.Invoke($"切换到主机模式失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切换到客机模式
        /// </summary>
        /// <param name="hostEndpoint">主机端点</param>
        /// <param name="config">客机连接配置</param>
        /// <returns>切换是否成功</returns>
        public async Task<bool> SwitchToClientMode(string hostEndpoint, ClientConnectionConfig config = null)
        {
            try
            {
                LogInfo($"切换到客机聊天模式，连接主机: {hostEndpoint}");

                var oldMode = CurrentMode;

                // 停止其他服务
                await StopNetworkServices();

                // 连接到主机
                bool connected = await _clientChatHandler.ConnectToHost(hostEndpoint, config);
                if (!connected)
                {
                    throw new Exception($"连接主机失败: {hostEndpoint}");
                }

                // 订阅客机处理器事件
                SubscribeClientHandlerEvents();

                CurrentMode = ChatMode.Client;

                LogInfo($"已切换到客机聊天模式，已连接到: {hostEndpoint}");
                OnChatModeChanged?.Invoke(oldMode, CurrentMode);
                OnConnectionStatusChanged?.Invoke(IsConnected);

                return true;
            }
            catch (Exception ex)
            {
                LogError($"切换到客机模式时发生异常: {ex.Message}");
                OnChatError?.Invoke($"切换到客机模式失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 消息发送接口

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <returns>发送是否成功</returns>
        public new async Task<bool> SendMessage(string content)
        {
            if (!IsInitialized)
            {
                LogError("聊天管理器未初始化");
                return false;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                LogWarning("消息内容为空");
                return false;
            }

            try
            {
                LogDebug($"发送消息 ({CurrentMode}): {content}");

                switch (CurrentMode)
                {
                    case ChatMode.Local:
                        return await SendLocalMessage(content);

                    case ChatMode.Host:
                        return await SendHostMessage(content);

                    case ChatMode.Client:
                        return await SendClientMessage(content);

                    default:
                        LogError($"不支持的聊天模式: {CurrentMode}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                LogError($"发送消息时发生异常: {ex.Message}");
                OnChatError?.Invoke($"发送消息失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送本地消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendLocalMessage(string content)
        {
            return _localChatManager.SendChatMessage(content);
        }

        /// <summary>
        /// 发送主机消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendHostMessage(string content)
        {
            // 创建聊天消息
            var message = new ChatMessage
            {
                Content = content,
                Sender = CurrentUser,
                Type = MessageType.Normal,
                Timestamp = DateTime.UtcNow
            };

            // 通过消息路由器广播消息
            var routeId = _messageRouter.BroadcastMessage(message, MessagePriority.Normal, false);

            // 同时在本地显示
            _localChatManager.ReceiveMessage(message);

            return !string.IsNullOrEmpty(routeId);
        }

        /// <summary>
        /// 发送客机消息
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendClientMessage(string content)
        {
            // 创建聊天消息
            var message = new ChatMessage
            {
                Content = content,
                Sender = CurrentUser,
                Type = MessageType.Normal,
                Timestamp = DateTime.UtcNow
            };

            // TODO: 在后续任务中实现客机消息发送
            // 这里暂时只在本地显示
            _localChatManager.ReceiveMessage(message);

            LogDebug("客机消息发送功能将在后续任务中实现");
            return true;
        }

        #endregion

        #region 消息接收处理

        /// <summary>
        /// 接收网络消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void ReceiveNetworkMessage(ChatMessage message)
        {
            if (!IsInitialized || message == null)
                return;

            try
            {
                LogDebug($"接收网络消息: {message.GetDisplayText()}");

                // 转换网络消息为显示消息
                var displayMessage = _messageConverter.ConvertNetworkToDisplay(message);

                // 如果转换器返回 null（可能是重复消息），直接使用原消息
                if (displayMessage == null)
                {
                    LogDebug($"消息转换器返回 null，使用原消息: {message.Id}");
                    displayMessage = message;
                }

                // 添加到历史记录
                _historyManager.AddMessage(displayMessage);

                // 在本地显示
                _localChatManager.ReceiveMessage(displayMessage);

                // 触发消息接收事件
                OnMessageReceived?.Invoke(displayMessage);
            }
            catch (Exception ex)
            {
                LogError($"处理网络消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 批量接收消息（用于历史同步）
        /// </summary>
        /// <param name="messages">消息列表</param>
        public void ReceiveMessages(List<ChatMessage> messages)
        {
            if (!IsInitialized || messages == null || messages.Count == 0)
                return;

            try
            {
                LogInfo($"批量接收消息: {messages.Count} 条");

                var displayMessages = new List<ChatMessage>();

                foreach (var message in messages)
                {
                    if (message != null && message.IsValid())
                    {
                        var displayMessage = _messageConverter.ConvertNetworkToDisplay(message);
                        displayMessages.Add(displayMessage);
                        _historyManager.AddMessage(displayMessage);
                    }
                }

                // 批量显示消息
                _localChatManager.ReceiveMessages(displayMessages);

                // 触发历史同步事件
                OnHistorySynced?.Invoke(displayMessages);
            }
            catch (Exception ex)
            {
                LogError($"批量接收消息时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 聊天历史管理

        /// <summary>
        /// 获取聊天历史
        /// </summary>
        /// <param name="count">消息数量</param>
        /// <returns>聊天历史消息</returns>
        public List<ChatMessage> GetChatHistory(int count = 100)
        {
            return _historyManager?.GetRecentMessages(count) ?? new List<ChatMessage>();
        }

        /// <summary>
        /// 清空聊天历史
        /// </summary>
        public void ClearChatHistory()
        {
            _historyManager?.ClearHistory();
            _localChatManager?.ClearMessageHistory();
            LogInfo("聊天历史已清空");
        }

        /// <summary>
        /// 同步本地历史到主机服务
        /// </summary>
        private async Task SyncLocalHistoryToHost()
        {
            try
            {
                if (_localChatManager == null || _hostChatService == null)
                    return;

                var localHistory = _localChatManager.MessageHistory;
                if (localHistory.Count == 0)
                    return;

                LogInfo($"同步本地历史到主机服务: {localHistory.Count} 条消息");

                foreach (var message in localHistory)
                {
                    _historyManager.AddMessage(message);
                }

                LogInfo("本地历史同步完成");
            }
            catch (Exception ex)
            {
                LogError($"同步本地历史时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 网络服务管理

        /// <summary>
        /// 停止所有网络服务
        /// </summary>
        private async Task StopNetworkServices()
        {
            try
            {
                // 取消订阅事件
                UnsubscribeNetworkEvents();

                // 停止主机服务
                if (_hostChatService?.IsServiceRunning == true)
                {
                    _hostChatService.StopService();
                }

                // 断开客机连接
                if (_clientChatHandler?.IsConnectedToHost == true)
                {
                    _clientChatHandler.DisconnectFromHost();
                }

                // 等待服务完全停止
                await Task.Delay(500);

                LogDebug("网络服务已停止");
            }
            catch (Exception ex)
            {
                LogError($"停止网络服务时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 事件订阅管理

        /// <summary>
        /// 订阅网络事件
        /// </summary>
        private void SubscribeNetworkEvents()
        {
            if (_networkStatusMonitor != null)
            {
                _networkStatusMonitor.OnNetworkStatusChanged += HandleNetworkStatusChanged;
            }
        }

        /// <summary>
        /// 取消订阅网络事件
        /// </summary>
        private void UnsubscribeNetworkEvents()
        {
            UnsubscribeHostServiceEvents();
            UnsubscribeClientHandlerEvents();

            if (_networkStatusMonitor != null)
            {
                _networkStatusMonitor.OnNetworkStatusChanged -= HandleNetworkStatusChanged;
            }
        }

        /// <summary>
        /// 订阅主机服务事件
        /// </summary>
        private void SubscribeHostServiceEvents()
        {
            if (_hostChatService != null)
            {
                _hostChatService.OnServiceStarted += HandleHostServiceStarted;
                _hostChatService.OnServiceStopped += HandleHostServiceStopped;
                _hostChatService.OnClientConnected += HandleHostClientConnected;
                _hostChatService.OnClientDisconnected += HandleHostClientDisconnected;
                _hostChatService.OnServiceError += HandleHostServiceError;
            }
        }

        /// <summary>
        /// 取消订阅主机服务事件
        /// </summary>
        private void UnsubscribeHostServiceEvents()
        {
            if (_hostChatService != null)
            {
                _hostChatService.OnServiceStarted -= HandleHostServiceStarted;
                _hostChatService.OnServiceStopped -= HandleHostServiceStopped;
                _hostChatService.OnClientConnected -= HandleHostClientConnected;
                _hostChatService.OnClientDisconnected -= HandleHostClientDisconnected;
                _hostChatService.OnServiceError -= HandleHostServiceError;
            }
        }

        /// <summary>
        /// 订阅客机处理器事件
        /// </summary>
        private void SubscribeClientHandlerEvents()
        {
            if (_clientChatHandler != null)
            {
                _clientChatHandler.OnConnectedToHost += HandleClientConnectedToHost;
                _clientChatHandler.OnDisconnectedFromHost += HandleClientDisconnectedFromHost;
                _clientChatHandler.OnConnectionFailed += HandleClientConnectionFailed;
            }
        }

        /// <summary>
        /// 取消订阅客机处理器事件
        /// </summary>
        private void UnsubscribeClientHandlerEvents()
        {
            if (_clientChatHandler != null)
            {
                _clientChatHandler.OnConnectedToHost -= HandleClientConnectedToHost;
                _clientChatHandler.OnDisconnectedFromHost -= HandleClientDisconnectedFromHost;
                _clientChatHandler.OnConnectionFailed -= HandleClientConnectionFailed;
            }
        }

        #endregion

        #region 事件处理方法

        /// <summary>
        /// 处理本地消息发送
        /// </summary>
        private void HandleLocalMessageSent(ChatMessage message)
        {
            OnMessageSent?.Invoke(message);
        }

        /// <summary>
        /// 处理本地消息接收
        /// </summary>
        private void HandleLocalMessageReceived(ChatMessage message)
        {
            OnMessageReceived?.Invoke(message);
        }

        /// <summary>
        /// 处理消息验证失败
        /// </summary>
        private void HandleMessageValidationFailed(string errorMessage)
        {
            OnChatError?.Invoke(errorMessage);
        }

        /// <summary>
        /// 处理网络状态变化
        /// </summary>
        private void HandleNetworkStatusChanged(NetworkStatus status)
        {
            OnNetworkStatusChanged?.Invoke(status);
        }

        /// <summary>
        /// 处理主机服务启动
        /// </summary>
        private void HandleHostServiceStarted()
        {
            LogInfo("主机聊天服务已启动");
            OnConnectionStatusChanged?.Invoke(IsConnected);
        }

        /// <summary>
        /// 处理主机服务停止
        /// </summary>
        private void HandleHostServiceStopped()
        {
            LogInfo("主机聊天服务已停止");
            OnConnectionStatusChanged?.Invoke(IsConnected);
        }

        /// <summary>
        /// 处理主机客机连接
        /// </summary>
        private void HandleHostClientConnected(string clientId, UserInfo userInfo)
        {
            LogInfo($"客机已连接: {userInfo.GetDisplayName()} ({clientId})");
        }

        /// <summary>
        /// 处理主机客机断开
        /// </summary>
        private void HandleHostClientDisconnected(string clientId, UserInfo userInfo)
        {
            LogInfo($"客机已断开: {userInfo.GetDisplayName()} ({clientId})");
        }

        /// <summary>
        /// 处理主机服务错误
        /// </summary>
        private void HandleHostServiceError(string error)
        {
            LogError($"主机服务错误: {error}");
            OnChatError?.Invoke(error);
        }

        /// <summary>
        /// 处理客机连接成功
        /// </summary>
        private void HandleClientConnectedToHost(string hostEndpoint)
        {
            LogInfo($"已连接到主机: {hostEndpoint}");
            OnConnectionStatusChanged?.Invoke(IsConnected);
        }

        /// <summary>
        /// 处理客机断开连接
        /// </summary>
        private void HandleClientDisconnectedFromHost(string hostEndpoint)
        {
            LogInfo($"已断开主机连接: {hostEndpoint}");
            OnConnectionStatusChanged?.Invoke(IsConnected);
        }

        /// <summary>
        /// 处理客机连接失败
        /// </summary>
        private void HandleClientConnectionFailed(string hostEndpoint, string error)
        {
            LogError($"连接主机失败: {hostEndpoint}, 错误: {error}");
            OnChatError?.Invoke($"连接失败: {error}");
        }

        #endregion

        #region 公共接口方法

        /// <summary>
        /// 获取当前连接状态描述
        /// </summary>
        /// <returns>连接状态描述</returns>
        public string GetConnectionStatusDescription()
        {
            return CurrentMode switch
            {
                ChatMode.Local => "本地模式",
                ChatMode.Host => IsConnected ? "主机模式 - 服务运行中" : "主机模式 - 服务未启动",
                ChatMode.Client => IsConnected ? $"客机模式 - 已连接到 {_clientChatHandler?.HostEndpoint}" : "客机模式 - 未连接",
                _ => "未知模式"
            };
        }

        /// <summary>
        /// 获取连接的客机数量（主机模式下）
        /// </summary>
        /// <returns>客机数量</returns>
        public int GetConnectedClientCount()
        {
            return CurrentMode == ChatMode.Host ? _hostChatService?.GetConnectedClientCount() ?? 0 : 0;
        }

        /// <summary>
        /// 检查是否可以发送消息
        /// </summary>
        /// <returns>是否可以发送</returns>
        public bool CanSendMessage()
        {
            if (!IsInitialized)
                return false;

            return CurrentMode switch
            {
                ChatMode.Local => _localChatManager?.CanSendMessage() ?? false,
                ChatMode.Host => IsConnected,
                ChatMode.Client => IsConnected,
                _ => false
            };
        }

        /// <summary>
        /// 刷新当前用户信息
        /// </summary>
        public async Task RefreshCurrentUser()
        {
            try
            {
                if (_steamUserService != null)
                {
                    var updatedUser = await _steamUserService.GetCurrentUserInfo();
                    if (updatedUser != null)
                    {
                        CurrentUser = updatedUser;
                        _localChatManager?.RefreshCurrentUser();
                        LogInfo($"用户信息已更新: {CurrentUser.GetDisplayName()}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"刷新用户信息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭聊天管理器
        /// </summary>
        public void Shutdown()
        {
            try
            {
                LogInfo("正在关闭聊天管理器...");

                // 停止网络服务
                _ = StopNetworkServices();

                // 清理资源
                _steamUserService?.ShutdownSteamAPI();
                _networkStatusMonitor?.Dispose();

                IsInitialized = false;
                LogInfo("聊天管理器已关闭");
            }
            catch (Exception ex)
            {
                LogError($"关闭聊天管理器时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[ChatManager] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[ChatManager] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[ChatManager] {message}");
        }

        private void LogDebug(string message)
        {
            if (_enableDebugLog)
            {
                Debug.Log($"[ChatManager][DEBUG] {message}");
            }
        }

        #endregion

        #region 网络消息处理方法

        /// <summary>
        /// 处理来自网络的聊天消息
        /// </summary>
        /// <param name="messageJson">聊天消息JSON字符串</param>
        public void HandleNetworkMessage(string messageJson)
        {
            try
            {
                LogDebug($"处理网络聊天消息: {messageJson}");
                
                // 使用 Newtonsoft.Json 解析JSON消息（支持复杂对象）
                var message = Newtonsoft.Json.JsonConvert.DeserializeObject<ChatMessage>(messageJson);
                if (message == null)
                {
                    LogError("无法解析网络聊天消息JSON");
                    return;
                }

                // 调用现有的消息接收方法（如果已初始化）
                if (IsInitialized)
                {
                    ReceiveNetworkMessage(message);
                }
                else
                {
                    // 如果未初始化，直接显示到 UI
                    LogWarning("ChatManager 未初始化，直接显示消息到 UI");
                    
                    // 格式化消息并显示
                    string displayText = message.GetDisplayText();
                    
                    // 直接调用 ModUI 显示消息
                    var modUI = ModUI.Instance;
                    if (modUI != null)
                    {
                        modUI.AddChatMessage(displayText);
                        LogDebug($"消息已直接添加到 ModUI: {displayText}");
                    }
                    else
                    {
                        LogError("ModUI 实例未找到，无法显示消息");
                    }
                    
                    // 同时触发事件（如果有订阅者）
                    OnMessageReceived?.Invoke(message);
                }

                LogDebug($"网络聊天消息处理完成: {message.Sender?.UserName ?? "未知"}: {message.Content}");
            }
            catch (Exception ex)
            {
                LogError($"处理网络聊天消息时发生异常: {ex.Message}");
                LogError($"消息内容: {messageJson}");
            }
        }

        /// <summary>
        /// 处理聊天历史同步
        /// </summary>
        /// <param name="historyJson">聊天历史JSON字符串</param>
        public void HandleHistorySync(string historyJson)
        {
            try
            {
                LogDebug($"处理聊天历史同步: {historyJson.Length} 字符");
                
                // 解析JSON历史数据
                var history = JsonUtility.FromJson<ChatHistory>(historyJson);
                if (history == null)
                {
                    LogError("无法解析聊天历史JSON");
                    return;
                }

                // 触发历史同步事件
                OnHistorySynced?.Invoke(history.Messages?.ToList() ?? new List<ChatMessage>());
                
                LogInfo($"聊天历史同步完成: {history.Messages?.Count ?? 0} 条消息");
            }
            catch (Exception ex)
            {
                LogError($"处理聊天历史同步时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 静态方法

        /// <summary>
        /// 创建聊天管理器实例
        /// </summary>
        /// <returns>聊天管理器实例</returns>
        public static ChatManager CreateInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("ChatManager");
            var manager = go.AddComponent<ChatManager>();
            return manager;
        }

        /// <summary>
        /// 获取或创建实例
        /// </summary>
        /// <returns>聊天管理器实例</returns>
        public static ChatManager GetOrCreateInstance()
        {
            if (_instance == null)
            {
                return CreateInstance();
            }
            return _instance;
        }

        #endregion
    }
}