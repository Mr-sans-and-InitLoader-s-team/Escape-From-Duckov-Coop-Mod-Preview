using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using EscapeFromDuckovCoopMod.Chat.Network;
using EscapeFromDuckovCoopMod.Chat.Data;

namespace EscapeFromDuckovCoopMod.Chat.Services
{
    /// <summary>
    /// 主机聊天服务核心类
    /// 负责管理聊天服务的启动、停止、客机连接管理和服务状态监控
    /// </summary>
    public class HostChatService : MonoBehaviour
    {
        #region 单例模式

        private static HostChatService _instance;

        /// <summary>
        /// 主机聊天服务单例实例
        /// </summary>
        public static HostChatService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("HostChatService");
                    _instance = go.AddComponent<HostChatService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        #endregion

        #region 字段和属性

        /// <summary>
        /// 服务是否正在运行
        /// </summary>
        public bool IsServiceRunning { get; private set; }

        /// <summary>
        /// 服务启动时间
        /// </summary>
        public DateTime ServiceStartTime { get; private set; }

        /// <summary>
        /// 连接的客机列表
        /// </summary>
        private readonly Dictionary<string, ConnectedClient> _connectedClients = new Dictionary<string, ConnectedClient>();

        /// <summary>
        /// 网络管理器
        /// </summary>
        private NetworkManager _networkManager;

        /// <summary>
        /// 聊天历史管理器
        /// </summary>
        private ChatHistoryManager _historyManager;

        /// <summary>
        /// 主机历史管理器
        /// </summary>
        private HostHistoryManager _hostHistoryManager;

        /// <summary>
        /// 消息路由器
        /// </summary>
        private MessageRouter _messageRouter;

        /// <summary>
        /// 服务配置
        /// </summary>
        private HostServiceConfig _serviceConfig;

        /// <summary>
        /// 服务状态监控定时器
        /// </summary>
        private float _statusMonitorTimer;

        /// <summary>
        /// 状态监控间隔（秒）
        /// </summary>
        private const float STATUS_MONITOR_INTERVAL = 5.0f;

        /// <summary>
        /// 客机连接超时时间（秒）
        /// </summary>
        private const float CLIENT_TIMEOUT_SECONDS = 30.0f;

        #endregion

        #region 事件

        /// <summary>
        /// 服务启动事件
        /// </summary>
        public event Action OnServiceStarted;

        /// <summary>
        /// 服务停止事件
        /// </summary>
        public event Action OnServiceStopped;

        /// <summary>
        /// 客机连接事件
        /// </summary>
        public event Action<string, UserInfo> OnClientConnected;

        /// <summary>
        /// 客机断开连接事件
        /// </summary>
        public event Action<string, UserInfo> OnClientDisconnected;

        /// <summary>
        /// 服务状态变化事件
        /// </summary>
        public event Action<ServiceStatus> OnServiceStatusChanged;

        /// <summary>
        /// 服务错误事件
        /// </summary>
        public event Action<string> OnServiceError;

        #endregion

        #region 初始化和清理

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeService();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                StopService();
                _instance = null;
            }
        }

        /// <summary>
        /// 初始化服务
        /// </summary>
        private void InitializeService()
        {
            try
            {
                LogInfo("正在初始化主机聊天服务...");

                // 获取网络管理器
                _networkManager = NetworkManager.Instance;
                if (_networkManager == null)
                {
                    LogError("无法获取网络管理器实例");
                    return;
                }

                // 初始化聊天历史管理器
                _historyManager = new ChatHistoryManager();

                // 初始化主机历史管理器
                var hostHistoryGO = new GameObject("HostHistoryManager");
                hostHistoryGO.transform.SetParent(transform);
                _hostHistoryManager = hostHistoryGO.AddComponent<HostHistoryManager>();

                // 初始化消息路由器
                var routerGO = new GameObject("MessageRouter");
                routerGO.transform.SetParent(transform);
                _messageRouter = routerGO.AddComponent<MessageRouter>();

                // 创建默认服务配置
                _serviceConfig = new HostServiceConfig();

                // 订阅网络事件
                SubscribeNetworkEvents();

                LogInfo("主机聊天服务初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化主机聊天服务时发生异常: {ex.Message}");
                OnServiceError?.Invoke($"服务初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅网络事件
        /// </summary>
        private void SubscribeNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.OnClientConnected += HandleClientConnected;
                _networkManager.OnClientDisconnected += HandleClientDisconnected;
                _networkManager.OnMessageReceived += HandleMessageReceived;
                _networkManager.OnNetworkError += HandleNetworkError;
                _networkManager.OnConnectionStatusChanged += HandleConnectionStatusChanged;
            }
        }

        /// <summary>
        /// 取消订阅网络事件
        /// </summary>
        private void UnsubscribeNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.OnClientConnected -= HandleClientConnected;
                _networkManager.OnClientDisconnected -= HandleClientDisconnected;
                _networkManager.OnMessageReceived -= HandleMessageReceived;
                _networkManager.OnNetworkError -= HandleNetworkError;
                _networkManager.OnConnectionStatusChanged -= HandleConnectionStatusChanged;
            }
        }

        #endregion

        #region 服务控制

        /// <summary>
        /// 启动聊天服务
        /// </summary>
        /// <param name="config">服务配置</param>
        /// <returns>启动是否成功</returns>
        public async Task<bool> StartService(HostServiceConfig config = null)
        {
            try
            {
                if (IsServiceRunning)
                {
                    LogWarning("聊天服务已在运行中");
                    return true;
                }

                LogInfo("正在启动主机聊天服务...");

                // 使用提供的配置或默认配置
                _serviceConfig = config ?? new HostServiceConfig();

                // 验证配置
                if (!_serviceConfig.IsValid())
                {
                    LogError("服务配置无效");
                    OnServiceError?.Invoke("服务配置无效");
                    return false;
                }

                // 启动网络服务
                var networkConfig = CreateNetworkConfig();
                bool networkStarted = await _networkManager.StartHost(networkConfig);

                if (!networkStarted)
                {
                    LogError("网络服务启动失败");
                    OnServiceError?.Invoke("网络服务启动失败");
                    return false;
                }

                // 初始化聊天历史
                _historyManager.SetMaxHistoryMessages(_serviceConfig.MaxHistoryMessages);

                // 初始化主机历史管理器
                var hostHistoryConfig = new HostHistoryConfig
                {
                    MaxHistoryMessages = _serviceConfig.MaxHistoryMessages,
                    HistoryRetentionDays = _serviceConfig.HistoryRetentionDays,
                    AutoCleanupEnabled = _serviceConfig.AutoCleanupEnabled,
                    AutoBackupEnabled = _serviceConfig.AutoBackupEnabled
                };
                _hostHistoryManager.Initialize(hostHistoryConfig);

                // 初始化消息路由器
                _messageRouter.Initialize(_networkManager);

                // 注册消息处理器
                RegisterMessageHandlers();

                // 设置服务状态
                IsServiceRunning = true;
                ServiceStartTime = DateTime.UtcNow;

                // 触发服务启动事件
                OnServiceStarted?.Invoke();
                OnServiceStatusChanged?.Invoke(new ServiceStatus { IsRunning = true });

                LogInfo($"主机聊天服务启动成功，网络类型: {_networkManager.CurrentNetworkType}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"启动聊天服务时发生异常: {ex.Message}");
                OnServiceError?.Invoke($"服务启动失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止聊天服务
        /// </summary>
        public void StopService()
        {
            try
            {
                if (!IsServiceRunning)
                {
                    LogWarning("聊天服务未在运行");
                    return;
                }

                LogInfo("正在停止主机聊天服务...");

                // 断开所有客机连接
                DisconnectAllClients();

                // 停止网络服务
                _networkManager?.Disconnect();

                // 清理资源
                _connectedClients.Clear();
                _historyManager?.ClearHistory();
                _hostHistoryManager?.Cleanup();
                _messageRouter?.Cleanup();

                // 设置服务状态
                IsServiceRunning = false;

                // 触发服务停止事件
                OnServiceStopped?.Invoke();
                OnServiceStatusChanged?.Invoke(new ServiceStatus { IsRunning = false });

                LogInfo("主机聊天服务已停止");
            }
            catch (Exception ex)
            {
                LogError($"停止聊天服务时发生异常: {ex.Message}");
                OnServiceError?.Invoke($"服务停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重启聊天服务
        /// </summary>
        /// <returns>重启是否成功</returns>
        public async Task<bool> RestartService()
        {
            LogInfo("正在重启主机聊天服务...");

            StopService();
            await Task.Delay(1000); // 等待1秒确保完全停止

            return await StartService(_serviceConfig);
        }

        #endregion

        #region 客机连接管理

        /// <summary>
        /// 获取连接的客机列表
        /// </summary>
        /// <returns>客机ID列表</returns>
        public List<string> GetConnectedClients()
        {
            return new List<string>(_connectedClients.Keys);
        }

        /// <summary>
        /// 获取连接的客机数量
        /// </summary>
        /// <returns>客机数量</returns>
        public int GetConnectedClientCount()
        {
            return _connectedClients.Count;
        }

        /// <summary>
        /// 获取客机信息
        /// </summary>
        /// <param name="clientId">客机ID</param>
        /// <returns>客机信息</returns>
        public ConnectedClient GetClientInfo(string clientId)
        {
            return _connectedClients.ContainsKey(clientId) ? _connectedClients[clientId] : null;
        }

        /// <summary>
        /// 检查客机是否已连接
        /// </summary>
        /// <param name="clientId">客机ID</param>
        /// <returns>是否已连接</returns>
        public bool IsClientConnected(string clientId)
        {
            return _connectedClients.ContainsKey(clientId);
        }

        /// <summary>
        /// 断开指定客机
        /// </summary>
        /// <param name="clientId">客机ID</param>
        /// <param name="reason">断开原因</param>
        public void DisconnectClient(string clientId, string reason = "主机断开连接")
        {
            try
            {
                if (!_connectedClients.ContainsKey(clientId))
                {
                    LogWarning($"客机不存在: {clientId}");
                    return;
                }

                var client = _connectedClients[clientId];
                LogInfo($"断开客机连接: {client.UserInfo.UserName} ({clientId}), 原因: {reason}");

                // 从连接列表中移除
                _connectedClients.Remove(clientId);

                // 触发客机断开连接事件
                OnClientDisconnected?.Invoke(clientId, client.UserInfo);

                // 广播用户离开消息
                var leaveMessage = new ChatMessage
                {
                    Type = MessageType.Leave,
                    Sender = client.UserInfo,
                    Content = $"{client.UserInfo.UserName} 离开了房间"
                };

                _ = BroadcastChatMessage(leaveMessage);
            }
            catch (Exception ex)
            {
                LogError($"断开客机连接时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开所有客机连接
        /// </summary>
        private void DisconnectAllClients()
        {
            var clientIds = new List<string>(_connectedClients.Keys);
            foreach (var clientId in clientIds)
            {
                DisconnectClient(clientId, "服务停止");
            }
        }

        #endregion

        #region 服务状态监控

        private void Update()
        {
            if (!IsServiceRunning)
                return;

            // 更新状态监控定时器
            _statusMonitorTimer += Time.deltaTime;

            if (_statusMonitorTimer >= STATUS_MONITOR_INTERVAL)
            {
                _statusMonitorTimer = 0f;
                MonitorServiceStatus();
            }
        }

        /// <summary>
        /// 监控服务状态
        /// </summary>
        private void MonitorServiceStatus()
        {
            try
            {
                // 检查网络连接状态
                if (_networkManager == null || !_networkManager.IsConnected)
                {
                    LogWarning("网络连接丢失，停止聊天服务");
                    StopService();
                    return;
                }

                // 检查客机连接超时
                CheckClientTimeouts();

                // 报告服务状态
                ReportServiceStatus();
            }
            catch (Exception ex)
            {
                LogError($"监控服务状态时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查客机连接超时
        /// </summary>
        private void CheckClientTimeouts()
        {
            var currentTime = DateTime.UtcNow;
            var timeoutClients = new List<string>();

            foreach (var kvp in _connectedClients)
            {
                var client = kvp.Value;
                var timeSinceLastActivity = (currentTime - client.LastActivity).TotalSeconds;

                if (timeSinceLastActivity > CLIENT_TIMEOUT_SECONDS)
                {
                    timeoutClients.Add(kvp.Key);
                }
            }

            // 断开超时的客机
            foreach (var clientId in timeoutClients)
            {
                DisconnectClient(clientId, "连接超时");
            }
        }

        /// <summary>
        /// 报告服务状态
        /// </summary>
        private void ReportServiceStatus()
        {
            var status = new ServiceStatus
            {
                IsRunning = IsServiceRunning,
                StartTime = ServiceStartTime,
                ConnectedClients = GetConnectedClientCount(),
                NetworkType = _networkManager?.CurrentNetworkType ?? NetworkType.SteamP2P,
                HistoryMessageCount = _historyManager?.History?.Count ?? 0
            };

            OnServiceStatusChanged?.Invoke(status);
        }

        #endregion

        #region 网络事件处理

        /// <summary>
        /// 处理客机连接
        /// </summary>
        /// <param name="clientId">客机ID</param>
        private void HandleClientConnected(string clientId)
        {
            try
            {
                LogInfo($"新客机连接: {clientId}");

                // 获取客机用户信息
                var userInfo = GetClientUserInfo(clientId);
                if (userInfo == null)
                {
                    LogWarning($"无法获取客机用户信息: {clientId}");
                    return;
                }

                // 创建连接客机记录
                var connectedClient = new ConnectedClient
                {
                    ClientId = clientId,
                    UserInfo = userInfo,
                    ConnectedTime = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow
                };

                _connectedClients[clientId] = connectedClient;

                // 触发客机连接事件
                OnClientConnected?.Invoke(clientId, userInfo);

                // 广播用户加入消息
                var joinMessage = new ChatMessage
                {
                    Type = MessageType.Join,
                    Sender = userInfo,
                    Content = $"{userInfo.UserName} 加入了房间"
                };

                _ = BroadcastChatMessage(joinMessage);

                LogInfo($"客机连接成功: {userInfo.UserName} ({clientId})");
            }
            catch (Exception ex)
            {
                LogError($"处理客机连接时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理客机断开连接
        /// </summary>
        /// <param name="clientId">客机ID</param>
        private void HandleClientDisconnected(string clientId)
        {
            try
            {
                if (_connectedClients.ContainsKey(clientId))
                {
                    DisconnectClient(clientId, "客机主动断开");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理客机断开连接时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="senderId">发送者ID</param>
        private void HandleMessageReceived(byte[] data, string senderId)
        {
            try
            {
                // 更新客机活动时间
                if (_connectedClients.ContainsKey(senderId))
                {
                    _connectedClients[senderId].LastActivity = DateTime.UtcNow;
                }

                // 这里将在消息路由系统中处理具体的消息内容
                LogDebug($"收到来自客机的消息: {senderId}, 大小: {data.Length}字节");
            }
            catch (Exception ex)
            {
                LogError($"处理接收消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理网络错误
        /// </summary>
        /// <param name="error">网络错误</param>
        private void HandleNetworkError(NetworkError error)
        {
            LogError($"网络错误: {error}");
            OnServiceError?.Invoke($"网络错误: {error.Message}");

            // 根据错误类型决定是否需要重启服务
            if (error.Type == NetworkErrorType.ConnectionLost || 
                error.Type == NetworkErrorType.ServiceStartFailed)
            {
                _ = RestartService();
            }
        }

        /// <summary>
        /// 处理连接状态变化
        /// </summary>
        /// <param name="status">连接状态</param>
        private void HandleConnectionStatusChanged(ConnectionStatus status)
        {
            LogInfo($"网络连接状态变化: {status}");

            if (status == ConnectionStatus.Disconnected || status == ConnectionStatus.Failed)
            {
                StopService();
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 创建网络配置
        /// </summary>
        /// <returns>网络配置</returns>
        private NetworkConfig CreateNetworkConfig()
        {
            return new NetworkConfig
            {
                Type = _serviceConfig.PreferredNetworkType,
                Port = _serviceConfig.Port,
                SteamLobbyId = _serviceConfig.SteamLobbyId
            };
        }

        /// <summary>
        /// 获取客机用户信息
        /// </summary>
        /// <param name="clientId">客机ID</param>
        /// <returns>用户信息</returns>
        private UserInfo GetClientUserInfo(string clientId)
        {
            try
            {
                // 尝试从Steam获取用户信息
                if (ulong.TryParse(clientId, out ulong steamId))
                {
                    var steamAdapter = _networkManager.CurrentAdapter as ISteamP2PNetwork;
                    if (steamAdapter != null)
                    {
                        return steamAdapter.GetSteamUserInfo(steamId);
                    }
                }

                // 如果无法获取Steam信息，创建基本用户信息
                return new UserInfo
                {
                    SteamId = ulong.TryParse(clientId, out ulong id) ? id : 0,
                    UserName = $"Player_{clientId}",
                    DisplayName = $"Player_{clientId}",
                    Status = UserStatus.Online
                };
            }
            catch (Exception ex)
            {
                LogError($"获取客机用户信息时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 广播聊天消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>广播是否成功</returns>
        private async Task<bool> BroadcastChatMessage(ChatMessage message)
        {
            try
            {
                // 添加到历史记录
                _historyManager.AddMessage(message);
                _hostHistoryManager.AddMessage(message);

                // 通过消息路由器广播消息
                var routeId = _messageRouter.BroadcastMessage(message, MessagePriority.Normal, false);
                
                return !string.IsNullOrEmpty(routeId);
            }
            catch (Exception ex)
            {
                LogError($"广播聊天消息时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送消息给指定客机
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <param name="targetId">目标客机ID</param>
        /// <param name="priority">消息优先级</param>
        /// <param name="requireAck">是否需要确认</param>
        /// <returns>发送是否成功</returns>
        public string SendMessageToClient(ChatMessage message, string targetId, MessagePriority priority = MessagePriority.Normal, bool requireAck = false)
        {
            try
            {
                if (!IsClientConnected(targetId))
                {
                    LogWarning($"目标客机未连接: {targetId}");
                    return null;
                }

                // 通过消息路由器发送消息
                return _messageRouter.SendUnicastMessage(message, targetId, priority, requireAck);
            }
            catch (Exception ex)
            {
                LogError($"发送消息给客机时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        private void RegisterMessageHandlers()
        {
            try
            {
                // 注册聊天消息处理器
                _messageRouter.RegisterMessageHandler(MessageType.Normal, new ChatMessageHandler(this));
                _messageRouter.RegisterMessageHandler(MessageType.System, new SystemMessageHandler(this));
                _messageRouter.RegisterMessageHandler(MessageType.Join, new JoinMessageHandler(this));
                _messageRouter.RegisterMessageHandler(MessageType.Leave, new LeaveMessageHandler(this));

                LogInfo("消息处理器注册完成");
            }
            catch (Exception ex)
            {
                LogError($"注册消息处理器时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 历史同步功能

        /// <summary>
        /// 为客机准备历史同步
        /// </summary>
        /// <param name="clientId">客机ID</param>
        /// <param name="maxMessages">最大消息数量</param>
        /// <returns>历史同步数据包</returns>
        public async Task<HistorySyncPacket> PrepareHistorySyncForClient(string clientId, int maxMessages = 100)
        {
            try
            {
                if (!IsClientConnected(clientId))
                {
                    LogWarning($"客机未连接，无法准备历史同步: {clientId}");
                    return new HistorySyncPacket
                    {
                        ClientId = clientId,
                        Success = false,
                        ErrorMessage = "客机未连接"
                    };
                }

                LogInfo($"为客机 {clientId} 准备历史同步，最大消息数: {maxMessages}");

                return await _hostHistoryManager.PrepareHistorySync(clientId, maxMessages);
            }
            catch (Exception ex)
            {
                LogError($"为客机准备历史同步时发生异常: {ex.Message}");
                return new HistorySyncPacket
                {
                    ClientId = clientId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 发送历史同步数据给客机
        /// </summary>
        /// <param name="syncPacket">同步数据包</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendHistorySyncToClient(HistorySyncPacket syncPacket)
        {
            try
            {
                if (syncPacket == null || !syncPacket.Success)
                {
                    LogWarning("无效的历史同步数据包");
                    return false;
                }

                // 创建历史同步消息
                var syncMessage = new ChatMessage
                {
                    Type = MessageType.System,
                    Content = "HISTORY_SYNC",
                    Metadata = new Dictionary<string, object>
                    {
                        ["SyncPacket"] = syncPacket,
                        ["MessageCount"] = syncPacket.MessageCount,
                        ["CompressedSize"] = syncPacket.CompressedSize
                    }
                };

                // 发送给指定客机
                var routeId = SendMessageToClient(syncMessage, syncPacket.ClientId, MessagePriority.High, true);

                LogInfo($"历史同步数据已发送给客机 {syncPacket.ClientId}: {syncPacket.MessageCount} 条消息");

                return !string.IsNullOrEmpty(routeId);
            }
            catch (Exception ex)
            {
                LogError($"发送历史同步数据时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取历史统计信息
        /// </summary>
        /// <returns>历史统计信息</returns>
        public ChatHistoryStats GetHistoryStatistics()
        {
            try
            {
                var fullHistory = _hostHistoryManager.GetFullHistory();
                return fullHistory?.GetStats() ?? new ChatHistoryStats();
            }
            catch (Exception ex)
            {
                LogError($"获取历史统计信息时发生异常: {ex.Message}");
                return new ChatHistoryStats();
            }
        }

        /// <summary>
        /// 手动创建历史备份
        /// </summary>
        /// <returns>备份是否成功</returns>
        public bool CreateHistoryBackup()
        {
            try
            {
                return _hostHistoryManager.CreateBackup();
            }
            catch (Exception ex)
            {
                LogError($"创建历史备份时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 手动清理历史记录
        /// </summary>
        /// <param name="daysToKeep">保留天数</param>
        /// <returns>清理的消息数量</returns>
        public int CleanupHistory(int daysToKeep = -1)
        {
            try
            {
                return _hostHistoryManager.CleanupOldMessages(daysToKeep);
            }
            catch (Exception ex)
            {
                LogError($"清理历史记录时发生异常: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[HostChatService] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[HostChatService] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[HostChatService] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[HostChatService][DEBUG] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 主机服务配置类
    /// </summary>
    public class HostServiceConfig
    {
        /// <summary>
        /// 首选网络类型
        /// </summary>
        public NetworkType PreferredNetworkType { get; set; } = NetworkType.SteamP2P;

        /// <summary>
        /// 端口号（用于直连P2P）
        /// </summary>
        public int Port { get; set; } = 7777;

        /// <summary>
        /// Steam大厅ID（用于Steam P2P）
        /// </summary>
        public ulong SteamLobbyId { get; set; }

        /// <summary>
        /// 最大历史消息数量
        /// </summary>
        public int MaxHistoryMessages { get; set; } = 100;

        /// <summary>
        /// 最大客机连接数
        /// </summary>
        public int MaxClients { get; set; } = 8;

        /// <summary>
        /// 历史保留天数
        /// </summary>
        public int HistoryRetentionDays { get; set; } = 30;

        /// <summary>
        /// 是否启用自动清理
        /// </summary>
        public bool AutoCleanupEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用自动备份
        /// </summary>
        public bool AutoBackupEnabled { get; set; } = true;

        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        /// <returns>是否有效</returns>
        public bool IsValid()
        {
            return MaxHistoryMessages > 0 && 
                   MaxClients > 0 && 
                   Port > 0 && Port <= 65535;
        }
    }

    /// <summary>
    /// 连接的客机信息类
    /// </summary>
    public class ConnectedClient
    {
        /// <summary>
        /// 客机ID
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// 用户信息
        /// </summary>
        public UserInfo UserInfo { get; set; }

        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime ConnectedTime { get; set; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivity { get; set; }

        /// <summary>
        /// 是否活跃
        /// </summary>
        public bool IsActive => (DateTime.UtcNow - LastActivity).TotalSeconds < 30;
    }

    /// <summary>
    /// 服务状态信息类
    /// </summary>
    public class ServiceStatus
    {
        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 启动时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 连接的客机数量
        /// </summary>
        public int ConnectedClients { get; set; }

        /// <summary>
        /// 网络类型
        /// </summary>
        public NetworkType NetworkType { get; set; }

        /// <summary>
        /// 历史消息数量
        /// </summary>
        public int HistoryMessageCount { get; set; }
    }
}