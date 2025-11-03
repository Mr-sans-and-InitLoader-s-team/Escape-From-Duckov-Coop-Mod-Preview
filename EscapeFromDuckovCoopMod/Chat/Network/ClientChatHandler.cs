using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Models;
using EscapeFromDuckovCoopMod.Chat.Services;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 客机聊天处理器核心类
    /// 负责客机端的聊天通信、连接管理和状态监控
    /// </summary>
    public class ClientChatHandler : MonoBehaviour
    {
        #region 常量定义
        
        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        private const int CONNECTION_TIMEOUT_MS = 10000;
        
        /// <summary>
        /// 心跳检测间隔（毫秒）
        /// </summary>
        private const int HEARTBEAT_INTERVAL_MS = 5000;
        
        /// <summary>
        /// 连接状态检查间隔（毫秒）
        /// </summary>
        private const int STATUS_CHECK_INTERVAL_MS = 1000;
        
        /// <summary>
        /// 最大重连次数
        /// </summary>
        private const int MAX_RETRY_COUNT = 3;
        
        #endregion
        
        #region 字段和属性
        
        /// <summary>
        /// 单例实例
        /// </summary>
        public static ClientChatHandler Instance { get; private set; }
        
        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized { get; private set; }
        
        /// <summary>
        /// 当前连接状态
        /// </summary>
        public ClientConnectionStatus ConnectionStatus { get; private set; } = ClientConnectionStatus.Disconnected;
        
        /// <summary>
        /// 是否已连接到主机
        /// </summary>
        public bool IsConnectedToHost => ConnectionStatus == ClientConnectionStatus.Connected;
        
        /// <summary>
        /// 主机端点信息
        /// </summary>
        public string HostEndpoint { get; private set; }
        
        /// <summary>
        /// 当前用户信息
        /// </summary>
        public UserInfo CurrentUser { get; private set; }
        
        /// <summary>
        /// 网络管理器
        /// </summary>
        private NetworkManager _networkManager;
        
        /// <summary>
        /// Steam 用户服务
        /// </summary>
        private ISteamUserService _steamUserService;
        
        /// <summary>
        /// 连接参数配置
        /// </summary>
        private ClientConnectionConfig _connectionConfig;
        
        /// <summary>
        /// 连接质量监控数据
        /// </summary>
        private ConnectionQualityMonitor _qualityMonitor;
        
        /// <summary>
        /// 连接重试计数器
        /// </summary>
        private int _retryCount;
        
        /// <summary>
        /// 最后一次心跳时间
        /// </summary>
        private DateTime _lastHeartbeat;
        
        /// <summary>
        /// 最后一次状态检查时间
        /// </summary>
        private DateTime _lastStatusCheck;
        
        /// <summary>
        /// 连接开始时间
        /// </summary>
        private DateTime _connectionStartTime;
        
        /// <summary>
        /// 是否正在连接中
        /// </summary>
        private bool _isConnecting;
        
        /// <summary>
        /// 是否启用调试日志
        /// </summary>
        [SerializeField] private bool _enableDebugLog = true;
        
        #endregion
        
        #region 事件
        
        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event Action<ClientConnectionStatus, ClientConnectionStatus> OnConnectionStatusChanged;
        
        /// <summary>
        /// 连接成功事件
        /// </summary>
        public event Action<string> OnConnectedToHost;
        
        /// <summary>
        /// 连接断开事件
        /// </summary>
        public event Action<string> OnDisconnectedFromHost;
        
        /// <summary>
        /// 连接失败事件
        /// </summary>
        public event Action<string, string> OnConnectionFailed;
        
        /// <summary>
        /// 连接质量变化事件
        /// </summary>
        public event Action<ConnectionQuality> OnConnectionQualityChanged;
        
        /// <summary>
        /// 客机服务初始化完成事件
        /// </summary>
        public event Action<bool> OnClientServiceInitialized;
        
        #endregion
        
        #region Unity 生命周期
        
        private void Awake()
        {
            // 实现单例模式
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LogInfo("客机聊天处理器实例已创建");
            }
            else if (Instance != this)
            {
                LogWarning("检测到重复的客机聊天处理器实例，销毁当前实例");
                Destroy(gameObject);
                return;
            }
            
            // 初始化组件
            InitializeComponents();
        }
        
        private void Start()
        {
            // 延迟初始化，确保其他系统已准备就绪
            Invoke(nameof(DelayedInitialize), 0.1f);
        }
        
        private void Update()
        {
            if (!IsInitialized)
                return;
                
            // 更新连接状态监控
            UpdateConnectionMonitoring();
            
            // 更新网络管理器
            if (_networkManager != null)
            {
                // NetworkManager 有自己的 Update 方法
            }
        }
        
        private void OnDestroy()
        {
            if (Instance == this)
            {
                LogInfo("客机聊天处理器正在销毁");
                
                // 断开连接
                DisconnectFromHost();
                
                // 清理事件订阅
                UnsubscribeFromNetworkEvents();
                
                // 清理资源
                CleanupResources();
                
                Instance = null;
            }
        }
        
        #endregion
        
        #region 初始化方法
        
        /// <summary>
        /// 初始化组件
        /// </summary>
        private void InitializeComponents()
        {
            try
            {
                LogInfo("开始初始化客机聊天处理器组件...");
                
                // 初始化连接配置
                _connectionConfig = new ClientConnectionConfig();
                
                // 初始化连接质量监控器
                _qualityMonitor = new ConnectionQualityMonitor();
                
                // 重置状态
                ResetConnectionState();
                
                LogInfo("客机聊天处理器组件初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化客机聊天处理器组件时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 延迟初始化
        /// </summary>
        private async void DelayedInitialize()
        {
            try
            {
                LogInfo("开始延迟初始化客机聊天处理器...");
                
                // 初始化 Steam 用户服务
                await InitializeSteamUserService();
                
                // 初始化网络管理器
                InitializeNetworkManager();
                
                // 标记为已初始化
                IsInitialized = true;
                
                LogInfo("客机聊天处理器初始化完成");
                OnClientServiceInitialized?.Invoke(true);
            }
            catch (Exception ex)
            {
                LogError($"延迟初始化客机聊天处理器时发生异常: {ex.Message}");
                IsInitialized = false;
                OnClientServiceInitialized?.Invoke(false);
            }
        }
        
        /// <summary>
        /// 初始化 Steam 用户服务
        /// </summary>
        private async Task InitializeSteamUserService()
        {
            try
            {
                LogInfo("正在初始化 Steam 用户服务...");
                
                _steamUserService = new SteamUserService();
                await _steamUserService.InitializeSteamAPI();
                
                // 获取当前用户信息
                CurrentUser = await _steamUserService.GetCurrentUserInfo();
                if (CurrentUser == null)
                {
                    throw new Exception("无法获取当前用户信息");
                }
                
                LogInfo($"Steam 用户服务初始化完成，当前用户: {CurrentUser.GetDisplayName()}");
            }
            catch (Exception ex)
            {
                LogError($"初始化 Steam 用户服务时发生异常: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 初始化网络管理器
        /// </summary>
        private void InitializeNetworkManager()
        {
            try
            {
                LogInfo("正在初始化网络管理器...");
                
                _networkManager = NetworkManager.Instance;
                if (_networkManager == null)
                {
                    throw new Exception("无法获取网络管理器实例");
                }
                
                // 订阅网络事件
                SubscribeToNetworkEvents();
                
                LogInfo("网络管理器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化网络管理器时发生异常: {ex.Message}");
                throw;
            }
        }
        
        #endregion
        
        #region 连接管理方法
        
        /// <summary>
        /// 连接到主机
        /// </summary>
        /// <param name="hostEndpoint">主机端点</param>
        /// <param name="config">连接配置（可选）</param>
        /// <returns>连接是否成功</returns>
        public async Task<bool> ConnectToHost(string hostEndpoint, ClientConnectionConfig config = null)
        {
            if (!IsInitialized)
            {
                LogError("客机聊天处理器未初始化，无法连接主机");
                return false;
            }
            
            if (_isConnecting)
            {
                LogWarning("正在连接中，请勿重复连接");
                return false;
            }
            
            if (IsConnectedToHost)
            {
                LogWarning("已连接到主机，无需重复连接");
                return true;
            }
            
            try
            {
                LogInfo($"开始连接到主机: {hostEndpoint}");
                
                // 设置连接状态
                _isConnecting = true;
                _connectionStartTime = DateTime.UtcNow;
                _retryCount = 0;
                HostEndpoint = hostEndpoint;
                
                // 使用提供的配置或默认配置
                if (config != null)
                {
                    _connectionConfig = config;
                }
                
                // 验证连接参数
                if (!ValidateConnectionParameters(hostEndpoint))
                {
                    throw new ArgumentException("连接参数无效");
                }
                
                // 更新连接状态
                SetConnectionStatus(ClientConnectionStatus.Connecting);
                
                // 执行连接逻辑
                bool result = await PerformConnection(hostEndpoint);
                
                if (result)
                {
                    LogInfo($"成功连接到主机: {hostEndpoint}");
                    SetConnectionStatus(ClientConnectionStatus.Connected);
                    OnConnectedToHost?.Invoke(hostEndpoint);
                    
                    // 开始连接质量监控
                    StartConnectionQualityMonitoring();
                }
                else
                {
                    LogError($"连接主机失败: {hostEndpoint}");
                    SetConnectionStatus(ClientConnectionStatus.Failed);
                    OnConnectionFailed?.Invoke(hostEndpoint, "连接失败");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                LogError($"连接主机时发生异常: {ex.Message}");
                SetConnectionStatus(ClientConnectionStatus.Failed);
                OnConnectionFailed?.Invoke(hostEndpoint, ex.Message);
                return false;
            }
            finally
            {
                _isConnecting = false;
            }
        }
        
        /// <summary>
        /// 断开与主机的连接
        /// </summary>
        public void DisconnectFromHost()
        {
            try
            {
                if (ConnectionStatus == ClientConnectionStatus.Disconnected)
                {
                    LogInfo("已处于断开状态，无需重复断开");
                    return;
                }
                
                LogInfo($"正在断开与主机的连接: {HostEndpoint}");
                
                // 停止连接质量监控
                StopConnectionQualityMonitoring();
                
                // 断开网络连接
                if (_networkManager != null)
                {
                    _networkManager.Disconnect();
                }
                
                // 重置连接状态
                var oldEndpoint = HostEndpoint;
                ResetConnectionState();
                SetConnectionStatus(ClientConnectionStatus.Disconnected);
                
                LogInfo("已断开与主机的连接");
                OnDisconnectedFromHost?.Invoke(oldEndpoint);
            }
            catch (Exception ex)
            {
                LogError($"断开连接时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 重新连接到主机
        /// </summary>
        /// <returns>重连是否成功</returns>
        public async Task<bool> ReconnectToHost()
        {
            if (string.IsNullOrEmpty(HostEndpoint))
            {
                LogError("没有可重连的主机端点");
                return false;
            }
            
            LogInfo($"尝试重新连接到主机: {HostEndpoint}");
            
            // 先断开当前连接
            DisconnectFromHost();
            
            // 等待一段时间后重连
            await Task.Delay(1000);
            
            // 重新连接
            return await ConnectToHost(HostEndpoint, _connectionConfig);
        }
        
        #endregion
        
        #region 连接状态监控
        
        /// <summary>
        /// 更新连接监控
        /// </summary>
        private void UpdateConnectionMonitoring()
        {
            var now = DateTime.UtcNow;
            
            // 检查连接超时
            if (_isConnecting && (now - _connectionStartTime).TotalMilliseconds > CONNECTION_TIMEOUT_MS)
            {
                LogError("连接超时");
                HandleConnectionTimeout();
                return;
            }
            
            // 定期状态检查
            if ((now - _lastStatusCheck).TotalMilliseconds >= STATUS_CHECK_INTERVAL_MS)
            {
                _lastStatusCheck = now;
                PerformStatusCheck();
            }
            
            // 心跳检测
            if (IsConnectedToHost && (now - _lastHeartbeat).TotalMilliseconds >= HEARTBEAT_INTERVAL_MS)
            {
                _lastHeartbeat = now;
                PerformHeartbeatCheck();
            }
            
            // 更新连接质量监控
            if (IsConnectedToHost)
            {
                UpdateConnectionQuality();
            }
        }
        
        /// <summary>
        /// 执行状态检查
        /// </summary>
        private void PerformStatusCheck()
        {
            try
            {
                if (_networkManager == null)
                    return;
                
                // 检查网络管理器状态
                var networkStatus = _networkManager.Status;
                var isNetworkConnected = _networkManager.IsConnected;
                
                // 根据网络状态更新客机连接状态
                if (!isNetworkConnected && IsConnectedToHost)
                {
                    LogWarning("检测到网络连接丢失");
                    HandleConnectionLost();
                }
                else if (isNetworkConnected && ConnectionStatus == ClientConnectionStatus.Connecting)
                {
                    // 连接可能已建立，等待确认
                    LogDebug("网络连接已建立，等待连接确认");
                }
            }
            catch (Exception ex)
            {
                LogError($"执行状态检查时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 执行心跳检查
        /// </summary>
        private void PerformHeartbeatCheck()
        {
            try
            {
                // 这里可以发送心跳消息或检查连接活跃度
                // 具体实现将在后续的消息发送任务中完成
                LogDebug("执行心跳检查");
                
                // 更新连接质量数据
                _qualityMonitor?.UpdateHeartbeat();
            }
            catch (Exception ex)
            {
                LogError($"执行心跳检查时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新连接质量
        /// </summary>
        private void UpdateConnectionQuality()
        {
            try
            {
                if (_qualityMonitor == null)
                    return;
                
                // 获取网络质量信息
                var networkQuality = _networkManager?.GetNetworkQuality(_networkManager.CurrentNetworkType);
                if (networkQuality != null)
                {
                    var quality = _qualityMonitor.CalculateConnectionQuality(networkQuality);
                    OnConnectionQualityChanged?.Invoke(quality);
                }
            }
            catch (Exception ex)
            {
                LogError($"更新连接质量时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 连接参数验证
        
        /// <summary>
        /// 验证连接参数
        /// </summary>
        /// <param name="hostEndpoint">主机端点</param>
        /// <returns>参数是否有效</returns>
        private bool ValidateConnectionParameters(string hostEndpoint)
        {
            try
            {
                if (string.IsNullOrEmpty(hostEndpoint))
                {
                    LogError("主机端点不能为空");
                    return false;
                }
                
                if (_connectionConfig == null)
                {
                    LogError("连接配置不能为空");
                    return false;
                }
                
                if (_connectionConfig.ConnectionTimeoutMs <= 0)
                {
                    LogError("连接超时时间必须大于0");
                    return false;
                }
                
                if (_connectionConfig.MaxRetryCount < 0)
                {
                    LogError("最大重试次数不能小于0");
                    return false;
                }
                
                LogDebug("连接参数验证通过");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"验证连接参数时发生异常: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region 网络连接实现
        
        /// <summary>
        /// 执行实际的连接操作
        /// </summary>
        /// <param name="hostEndpoint">主机端点</param>
        /// <returns>连接是否成功</returns>
        private async Task<bool> PerformConnection(string hostEndpoint)
        {
            try
            {
                if (_networkManager == null)
                {
                    LogError("网络管理器未初始化");
                    return false;
                }
                
                LogInfo($"正在通过网络管理器连接到主机: {hostEndpoint}");
                
                // 使用网络管理器连接到主机
                bool result = await _networkManager.ConnectToHost(hostEndpoint);
                
                if (result)
                {
                    LogInfo("网络连接建立成功");
                }
                else
                {
                    LogError("网络连接建立失败");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                LogError($"执行连接操作时发生异常: {ex.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region 连接异常处理
        
        /// <summary>
        /// 处理连接超时
        /// </summary>
        private void HandleConnectionTimeout()
        {
            try
            {
                LogError($"连接超时: {HostEndpoint}");
                
                _isConnecting = false;
                SetConnectionStatus(ClientConnectionStatus.Failed);
                
                // 尝试重连
                if (_retryCount < _connectionConfig.MaxRetryCount)
                {
                    _retryCount++;
                    LogInfo($"尝试重连 ({_retryCount}/{_connectionConfig.MaxRetryCount})");
                    
                    // 延迟重连
                    Invoke(nameof(RetryConnection), _connectionConfig.RetryDelayMs / 1000f);
                }
                else
                {
                    LogError("达到最大重连次数，连接失败");
                    OnConnectionFailed?.Invoke(HostEndpoint, "连接超时");
                }
            }
            catch (Exception ex)
            {
                LogError($"处理连接超时时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理连接丢失
        /// </summary>
        private void HandleConnectionLost()
        {
            try
            {
                LogWarning($"连接丢失: {HostEndpoint}");
                
                SetConnectionStatus(ClientConnectionStatus.Disconnected);
                OnDisconnectedFromHost?.Invoke(HostEndpoint);
                
                // 根据配置决定是否自动重连
                if (_connectionConfig.AutoReconnect)
                {
                    LogInfo("启用自动重连，尝试重新连接");
                    _ = ReconnectToHost();
                }
            }
            catch (Exception ex)
            {
                LogError($"处理连接丢失时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 重试连接
        /// </summary>
        private async void RetryConnection()
        {
            try
            {
                if (!string.IsNullOrEmpty(HostEndpoint))
                {
                    await ConnectToHost(HostEndpoint, _connectionConfig);
                }
            }
            catch (Exception ex)
            {
                LogError($"重试连接时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 连接质量监控
        
        /// <summary>
        /// 开始连接质量监控
        /// </summary>
        private void StartConnectionQualityMonitoring()
        {
            try
            {
                LogInfo("开始连接质量监控");
                _qualityMonitor?.StartMonitoring();
            }
            catch (Exception ex)
            {
                LogError($"开始连接质量监控时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 停止连接质量监控
        /// </summary>
        private void StopConnectionQualityMonitoring()
        {
            try
            {
                LogInfo("停止连接质量监控");
                _qualityMonitor?.StopMonitoring();
            }
            catch (Exception ex)
            {
                LogError($"停止连接质量监控时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 网络事件处理
        
        /// <summary>
        /// 订阅网络事件
        /// </summary>
        private void SubscribeToNetworkEvents()
        {
            if (_networkManager == null)
                return;
            
            try
            {
                _networkManager.OnConnectionStatusChanged += HandleNetworkStatusChanged;
                _networkManager.OnNetworkError += HandleNetworkError;
                _networkManager.OnClientConnected += HandleClientConnected;
                _networkManager.OnClientDisconnected += HandleClientDisconnected;
                
                LogDebug("已订阅网络事件");
            }
            catch (Exception ex)
            {
                LogError($"订阅网络事件时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 取消订阅网络事件
        /// </summary>
        private void UnsubscribeFromNetworkEvents()
        {
            if (_networkManager == null)
                return;
            
            try
            {
                _networkManager.OnConnectionStatusChanged -= HandleNetworkStatusChanged;
                _networkManager.OnNetworkError -= HandleNetworkError;
                _networkManager.OnClientConnected -= HandleClientConnected;
                _networkManager.OnClientDisconnected -= HandleClientDisconnected;
                
                LogDebug("已取消订阅网络事件");
            }
            catch (Exception ex)
            {
                LogError($"取消订阅网络事件时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理网络状态变化
        /// </summary>
        /// <param name="status">网络连接状态</param>
        private void HandleNetworkStatusChanged(Network.ConnectionStatus status)
        {
            try
            {
                LogDebug($"网络状态变化: {status}");
                
                // 根据网络状态更新客机连接状态
                switch (status)
                {
                    case Network.ConnectionStatus.Connected:
                        if (this.ConnectionStatus == ClientConnectionStatus.Connecting)
                        {
                            SetConnectionStatus(ClientConnectionStatus.Connected);
                        }
                        break;
                        
                    case Network.ConnectionStatus.Disconnected:
                        if (IsConnectedToHost)
                        {
                            HandleConnectionLost();
                        }
                        break;
                        
                    case Network.ConnectionStatus.Failed:
                        if (this.ConnectionStatus == ClientConnectionStatus.Connecting)
                        {
                            SetConnectionStatus(ClientConnectionStatus.Failed);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                LogError($"处理网络状态变化时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理网络错误
        /// </summary>
        /// <param name="error">网络错误</param>
        private void HandleNetworkError(NetworkError error)
        {
            try
            {
                LogError($"网络错误: {error.Type} - {error.Message}");
                
                // 根据错误类型进行相应处理
                switch (error.Type)
                {
                    case NetworkErrorType.ConnectionLost:
                        HandleConnectionLost();
                        break;
                        
                    case NetworkErrorType.ConnectionFailed:
                        if (ConnectionStatus == ClientConnectionStatus.Connecting)
                        {
                            SetConnectionStatus(ClientConnectionStatus.Failed);
                            OnConnectionFailed?.Invoke(HostEndpoint, error.Message);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                LogError($"处理网络错误时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理客户端连接（主机视角）
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        private void HandleClientConnected(string clientId)
        {
            try
            {
                LogDebug($"客户端连接: {clientId}");
                // 客机端通常不需要处理其他客户端的连接事件
            }
            catch (Exception ex)
            {
                LogError($"处理客户端连接时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 处理客户端断开（主机视角）
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        private void HandleClientDisconnected(string clientId)
        {
            try
            {
                LogDebug($"客户端断开: {clientId}");
                // 客机端通常不需要处理其他客户端的断开事件
            }
            catch (Exception ex)
            {
                LogError($"处理客户端断开时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 辅助方法
        
        /// <summary>
        /// 设置连接状态
        /// </summary>
        /// <param name="newStatus">新的连接状态</param>
        private void SetConnectionStatus(ClientConnectionStatus newStatus)
        {
            if (ConnectionStatus != newStatus)
            {
                var oldStatus = ConnectionStatus;
                ConnectionStatus = newStatus;
                
                LogInfo($"客机连接状态变化: {oldStatus} -> {newStatus}");
                OnConnectionStatusChanged?.Invoke(oldStatus, newStatus);
            }
        }
        
        /// <summary>
        /// 重置连接状态
        /// </summary>
        private void ResetConnectionState()
        {
            HostEndpoint = null;
            _retryCount = 0;
            _isConnecting = false;
            _lastHeartbeat = DateTime.MinValue;
            _lastStatusCheck = DateTime.MinValue;
            _connectionStartTime = DateTime.MinValue;
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {
            try
            {
                _qualityMonitor?.Dispose();
                _steamUserService?.ShutdownSteamAPI();
                
                LogDebug("资源清理完成");
            }
            catch (Exception ex)
            {
                LogError($"清理资源时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 日志方法
        
        /// <summary>
        /// 记录信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogInfo(string message)
        {
            Debug.Log($"[ClientChatHandler] {message}");
        }
        
        /// <summary>
        /// 记录警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogWarning(string message)
        {
            Debug.LogWarning($"[ClientChatHandler] {message}");
        }
        
        /// <summary>
        /// 记录错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogError(string message)
        {
            Debug.LogError($"[ClientChatHandler] {message}");
        }
        
        /// <summary>
        /// 记录调试日志
        /// </summary>
        /// <param name="message">日志消息</param>
        private void LogDebug(string message)
        {
            if (_enableDebugLog)
            {
                Debug.Log($"[ClientChatHandler][DEBUG] {message}");
            }
        }
        
        #endregion
        
        #region 静态方法
        
        /// <summary>
        /// 创建客机聊天处理器实例
        /// </summary>
        /// <returns>客机聊天处理器实例</returns>
        public static ClientChatHandler CreateInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }
            
            var go = new GameObject("ClientChatHandler");
            var handler = go.AddComponent<ClientChatHandler>();
            return handler;
        }
        
        /// <summary>
        /// 获取或创建实例
        /// </summary>
        /// <returns>客机聊天处理器实例</returns>
        public static ClientChatHandler GetOrCreateInstance()
        {
            if (Instance == null)
            {
                return CreateInstance();
            }
            return Instance;
        }
        
        #endregion
    }
}