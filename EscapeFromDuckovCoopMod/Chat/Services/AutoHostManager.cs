using System;
using System.Threading.Tasks;
using UnityEngine;
using EscapeFromDuckovCoopMod.Chat.Managers;
using EscapeFromDuckovCoopMod.Chat.Network;

namespace EscapeFromDuckovCoopMod.Chat.Services
{
    /// <summary>
    /// 自动主机管理器
    /// 负责在适当的条件下自动启动主机聊天服务
    /// </summary>
    public class AutoHostManager : MonoBehaviour
    {
        #region 字段和属性

        /// <summary>
        /// 聊天管理器引用
        /// </summary>
        private ChatManager _chatManager;

        /// <summary>
        /// 网络状态监控器引用
        /// </summary>
        private NetworkStatusMonitor _networkStatusMonitor;

        /// <summary>
        /// 自动启动配置
        /// </summary>
        private AutoHostConfig _config;

        /// <summary>
        /// 是否启用自动启动
        /// </summary>
        public bool AutoStartEnabled { get; set; } = true;

        /// <summary>
        /// 是否正在尝试启动
        /// </summary>
        public bool IsAttemptingStart { get; private set; }

        /// <summary>
        /// 最后一次启动尝试时间
        /// </summary>
        private DateTime _lastStartAttempt = DateTime.MinValue;

        /// <summary>
        /// 启动重试计数
        /// </summary>
        private int _startRetryCount = 0;

        /// <summary>
        /// 网络状态检查定时器
        /// </summary>
        private float _statusCheckTimer = 0f;

        /// <summary>
        /// 启动条件检查定时器
        /// </summary>
        private float _conditionCheckTimer = 0f;

        /// <summary>
        /// 自动启动统计信息
        /// </summary>
        private AutoStartStatistics _statistics;

        #endregion

        #region 事件

        /// <summary>
        /// 自动启动成功事件
        /// </summary>
        public event Action OnAutoStartSucceeded;

        /// <summary>
        /// 自动启动失败事件
        /// </summary>
        public event Action<string> OnAutoStartFailed;

        /// <summary>
        /// 启动条件检查事件
        /// </summary>
        public event Action<bool> OnStartConditionChanged;

        /// <summary>
        /// 配置变化事件
        /// </summary>
        public event Action<AutoHostConfig> OnConfigChanged;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化自动主机管理器
        /// </summary>
        /// <param name="chatManager">聊天管理器</param>
        public void Initialize(ChatManager chatManager)
        {
            try
            {
                _chatManager = chatManager ?? throw new ArgumentNullException(nameof(chatManager));

                // 初始化配置
                _config = new AutoHostConfig();

                // 初始化统计信息
                _statistics = new AutoStartStatistics();

                // 初始化网络状态监控器
                InitializeNetworkMonitor();

                // 订阅聊天管理器事件
                SubscribeChatManagerEvents();

                LogInfo("自动主机管理器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化自动主机管理器时发生异常: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 初始化网络监控器
        /// </summary>
        private void InitializeNetworkMonitor()
        {
            try
            {
                var networkManager = NetworkManager.Instance;
                if (networkManager != null)
                {
                    _networkStatusMonitor = new NetworkStatusMonitor(networkManager);
                    SubscribeNetworkEvents();
                }
            }
            catch (Exception ex)
            {
                LogError($"初始化网络监控器时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region Unity生命周期

        private void Update()
        {
            if (!AutoStartEnabled)
                return;

            // 更新网络状态监控器
            _networkStatusMonitor?.Update();

            // 定期检查启动条件
            UpdateConditionCheck();

            // 定期检查网络状态
            UpdateNetworkStatusCheck();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region 条件检查

        /// <summary>
        /// 更新启动条件检查
        /// </summary>
        private void UpdateConditionCheck()
        {
            _conditionCheckTimer += Time.deltaTime;

            if (_conditionCheckTimer >= _config.ConditionCheckIntervalSeconds)
            {
                _conditionCheckTimer = 0f;
                CheckAutoStartConditions();
            }
        }

        /// <summary>
        /// 更新网络状态检查
        /// </summary>
        private void UpdateNetworkStatusCheck()
        {
            _statusCheckTimer += Time.deltaTime;

            if (_statusCheckTimer >= _config.NetworkStatusCheckIntervalSeconds)
            {
                _statusCheckTimer = 0f;
                CheckNetworkStatus();
            }
        }

        /// <summary>
        /// 检查自动启动条件
        /// </summary>
        private void CheckAutoStartConditions()
        {
            try
            {
                if (!AutoStartEnabled || IsAttemptingStart)
                    return;

                // 检查是否已经是主机模式
                if (_chatManager.CurrentMode == ChatManager.ChatMode.Host && _chatManager.IsConnected)
                {
                    LogDebug("已处于主机模式，无需自动启动");
                    return;
                }

                // 检查重试限制
                if (_startRetryCount >= _config.MaxRetryAttempts)
                {
                    LogWarning($"已达到最大重试次数 ({_config.MaxRetryAttempts})，停止自动启动");
                    return;
                }

                // 检查重试间隔
                var timeSinceLastAttempt = DateTime.UtcNow - _lastStartAttempt;
                if (timeSinceLastAttempt.TotalSeconds < _config.RetryIntervalSeconds)
                {
                    return;
                }

                // 检查启动条件
                var shouldStart = ShouldAutoStartHost();
                OnStartConditionChanged?.Invoke(shouldStart);

                if (shouldStart)
                {
                    LogInfo("满足自动启动条件，开始启动主机服务");
                    _ = AttemptAutoStart();
                }
            }
            catch (Exception ex)
            {
                LogError($"检查自动启动条件时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否应该自动启动主机
        /// </summary>
        /// <returns>是否应该启动</returns>
        private bool ShouldAutoStartHost()
        {
            // 检查基本条件
            if (!_config.EnableAutoStart)
            {
                LogDebug("自动启动已禁用");
                return false;
            }

            if (_chatManager.CurrentMode != ChatManager.ChatMode.Local)
            {
                LogDebug($"当前模式不是本地模式: {_chatManager.CurrentMode}");
                return false;
            }

            // 检查网络条件
            if (_config.RequireNetworkAvailable && !IsNetworkAvailable())
            {
                LogDebug("网络不可用，不满足启动条件");
                return false;
            }

            // 检查Steam条件
            if (_config.RequireSteamOnline && !IsSteamOnline())
            {
                LogDebug("Steam不在线，不满足启动条件");
                return false;
            }

            // 检查游戏状态条件
            if (_config.RequireInRoom && !IsInGameRoom())
            {
                LogDebug("不在游戏房间中，不满足启动条件");
                return false;
            }

            // 检查时间条件
            if (_config.EnableTimeRestriction && !IsWithinAllowedTime())
            {
                LogDebug("不在允许的时间范围内");
                return false;
            }

            LogDebug("满足所有自动启动条件");
            return true;
        }

        /// <summary>
        /// 检查网络是否可用
        /// </summary>
        /// <returns>网络是否可用</returns>
        private bool IsNetworkAvailable()
        {
            try
            {
                var networkManager = NetworkManager.Instance;
                if (networkManager == null)
                    return false;

                var availableNetworks = networkManager.GetAvailableNetworkTypes();
                return availableNetworks.Count > 0;
            }
            catch (Exception ex)
            {
                LogError($"检查网络可用性时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查Steam是否在线
        /// </summary>
        /// <returns>Steam是否在线</returns>
        private bool IsSteamOnline()
        {
            try
            {
                // 这里需要检查Steam API的状态
                // 由于架构限制，我们简单返回true
                return true;
            }
            catch (Exception ex)
            {
                LogError($"检查Steam状态时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否在游戏房间中
        /// </summary>
        /// <returns>是否在游戏房间中</returns>
        private bool IsInGameRoom()
        {
            try
            {
                // 这里需要检查游戏状态
                // 由于架构限制，我们简单返回true
                return true;
            }
            catch (Exception ex)
            {
                LogError($"检查游戏房间状态时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否在允许的时间范围内
        /// </summary>
        /// <returns>是否在允许时间内</returns>
        private bool IsWithinAllowedTime()
        {
            if (!_config.EnableTimeRestriction)
                return true;

            var now = DateTime.Now.TimeOfDay;
            return now >= _config.AllowedStartTime && now <= _config.AllowedEndTime;
        }

        #endregion

        #region 自动启动逻辑

        /// <summary>
        /// 尝试自动启动
        /// </summary>
        /// <returns>启动任务</returns>
        private async Task AttemptAutoStart()
        {
            if (IsAttemptingStart)
            {
                LogWarning("已在尝试启动中，跳过重复启动");
                return;
            }

            try
            {
                IsAttemptingStart = true;
                _lastStartAttempt = DateTime.UtcNow;
                _startRetryCount++;
                _statistics.TotalStartAttempts++;

                LogInfo($"开始自动启动主机服务 (尝试 {_startRetryCount}/{_config.MaxRetryAttempts})");

                // 创建主机配置
                var hostConfig = CreateHostConfig();

                // 尝试启动主机模式
                var success = await _chatManager.SwitchToHostMode(hostConfig);

                if (success)
                {
                    LogInfo("自动启动主机服务成功");
                    _statistics.TotalStartSuccesses++;
                    _startRetryCount = 0; // 重置重试计数
                    OnAutoStartSucceeded?.Invoke();
                }
                else
                {
                    var errorMessage = "自动启动主机服务失败";
                    LogError(errorMessage);
                    _statistics.TotalStartFailures++;
                    OnAutoStartFailed?.Invoke(errorMessage);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"自动启动时发生异常: {ex.Message}";
                LogError(errorMessage);
                _statistics.TotalStartFailures++;
                OnAutoStartFailed?.Invoke(errorMessage);
            }
            finally
            {
                IsAttemptingStart = false;
            }
        }

        /// <summary>
        /// 创建主机配置
        /// </summary>
        /// <returns>主机配置</returns>
        private HostServiceConfig CreateHostConfig()
        {
            return new HostServiceConfig
            {
                PreferredNetworkType = _config.PreferredNetworkType,
                Port = _config.DefaultPort,
                MaxClients = _config.MaxClients,
                MaxHistoryMessages = _config.MaxHistoryMessages,
                AutoCleanupEnabled = true,
                AutoBackupEnabled = _config.EnableAutoBackup
            };
        }

        #endregion

        #region 网络状态检查

        /// <summary>
        /// 检查网络状态
        /// </summary>
        private void CheckNetworkStatus()
        {
            try
            {
                if (_networkStatusMonitor == null)
                    return;

                var status = _networkStatusMonitor.CurrentStatus;
                if (status == null)
                    return;

                // 检查网络质量
                if (status.NetworkQuality != null)
                {
                    var quality = status.NetworkQuality;
                    
                    // 如果网络质量太差，可能需要重启服务
                    if (_chatManager.CurrentMode == ChatManager.ChatMode.Host && 
                        quality.Score < _config.MinNetworkQualityScore)
                    {
                        LogWarning($"网络质量过低 ({quality.Score})，考虑重启服务");
                        
                        if (_config.EnableQualityBasedRestart)
                        {
                            _ = RestartHostService("网络质量过低");
                        }
                    }
                }

                // 检查连接状态
                if (!status.IsConnected && _chatManager.CurrentMode == ChatManager.ChatMode.Host)
                {
                    LogWarning("主机模式下网络连接丢失");
                    
                    if (_config.EnableConnectionLossRestart)
                    {
                        _ = RestartHostService("网络连接丢失");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"检查网络状态时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 重启主机服务
        /// </summary>
        /// <param name="reason">重启原因</param>
        /// <returns>重启任务</returns>
        private async Task RestartHostService(string reason)
        {
            try
            {
                LogInfo($"重启主机服务: {reason}");
                _statistics.TotalServiceRestarts++;

                // 先切换到本地模式
                await _chatManager.SwitchToLocalMode();

                // 等待一段时间
                await Task.Delay(2000);

                // 重新启动主机模式
                var hostConfig = CreateHostConfig();
                await _chatManager.SwitchToHostMode(hostConfig);

                LogInfo("主机服务重启完成");
            }
            catch (Exception ex)
            {
                LogError($"重启主机服务时发生异常: {ex.Message}");
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
                _chatManager.OnConnectionStatusChanged += HandleConnectionStatusChanged;
                _chatManager.OnChatError += HandleChatError;
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
                _chatManager.OnConnectionStatusChanged -= HandleConnectionStatusChanged;
                _chatManager.OnChatError -= HandleChatError;
            }
        }

        /// <summary>
        /// 订阅网络事件
        /// </summary>
        private void SubscribeNetworkEvents()
        {
            if (_networkStatusMonitor != null)
            {
                _networkStatusMonitor.OnNetworkStatusChanged += HandleNetworkStatusChanged;
                _networkStatusMonitor.OnNetworkException += HandleNetworkException;
            }
        }

        /// <summary>
        /// 取消订阅网络事件
        /// </summary>
        private void UnsubscribeNetworkEvents()
        {
            if (_networkStatusMonitor != null)
            {
                _networkStatusMonitor.OnNetworkStatusChanged -= HandleNetworkStatusChanged;
                _networkStatusMonitor.OnNetworkException -= HandleNetworkException;
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 处理聊天模式变化
        /// </summary>
        private void HandleChatModeChanged(ChatManager.ChatMode oldMode, ChatManager.ChatMode newMode)
        {
            LogInfo($"聊天模式变化: {oldMode} -> {newMode}");

            // 如果从主机模式切换到其他模式，重置重试计数
            if (oldMode == ChatManager.ChatMode.Host && newMode != ChatManager.ChatMode.Host)
            {
                _startRetryCount = 0;
            }
        }

        /// <summary>
        /// 处理连接状态变化
        /// </summary>
        private void HandleConnectionStatusChanged(bool isConnected)
        {
            LogDebug($"连接状态变化: {isConnected}");

            if (!isConnected && _chatManager.CurrentMode == ChatManager.ChatMode.Host)
            {
                LogWarning("主机模式下连接丢失");
            }
        }

        /// <summary>
        /// 处理聊天错误
        /// </summary>
        private void HandleChatError(string error)
        {
            LogError($"聊天错误: {error}");
            _statistics.TotalChatErrors++;
        }

        /// <summary>
        /// 处理网络状态变化
        /// </summary>
        private void HandleNetworkStatusChanged(NetworkStatus status)
        {
            LogDebug($"网络状态变化: {status}");
        }

        /// <summary>
        /// 处理网络异常
        /// </summary>
        private void HandleNetworkException(string exception)
        {
            LogError($"网络异常: {exception}");
            _statistics.TotalNetworkExceptions++;
        }

        #endregion

        #region 配置管理

        /// <summary>
        /// 设置自动启动配置
        /// </summary>
        /// <param name="config">配置</param>
        public void SetConfig(AutoHostConfig config)
        {
            _config = config ?? new AutoHostConfig();
            OnConfigChanged?.Invoke(_config);
            LogInfo("自动主机配置已更新");
        }

        /// <summary>
        /// 获取自动启动配置
        /// </summary>
        /// <returns>配置</returns>
        public AutoHostConfig GetConfig()
        {
            return _config;
        }

        #endregion

        #region 统计信息

        /// <summary>
        /// 获取自动启动统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        public AutoStartStatistics GetStatistics()
        {
            return _statistics.Clone();
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            _statistics.Reset();
            LogInfo("自动启动统计信息已重置");
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 手动触发启动检查
        /// </summary>
        public void TriggerStartCheck()
        {
            LogInfo("手动触发启动检查");
            CheckAutoStartConditions();
        }

        /// <summary>
        /// 重置重试计数
        /// </summary>
        public void ResetRetryCount()
        {
            _startRetryCount = 0;
            LogInfo("重试计数已重置");
        }

        /// <summary>
        /// 获取状态摘要
        /// </summary>
        /// <returns>状态摘要</returns>
        public string GetStatusSummary()
        {
            return $"自动启动: {(AutoStartEnabled ? "启用" : "禁用")}, " +
                   $"重试: {_startRetryCount}/{_config.MaxRetryAttempts}, " +
                   $"正在启动: {IsAttemptingStart}";
        }

        #endregion

        #region 清理

        /// <summary>
        /// 清理资源
        /// </summary>
        private void Cleanup()
        {
            try
            {
                UnsubscribeChatManagerEvents();
                UnsubscribeNetworkEvents();
                _networkStatusMonitor?.Dispose();

                LogDebug("自动主机管理器已清理");
            }
            catch (Exception ex)
            {
                LogError($"清理自动主机管理器时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[AutoHostManager] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AutoHostManager] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[AutoHostManager] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[AutoHostManager][DEBUG] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 自动主机配置
    /// </summary>
    public class AutoHostConfig
    {
        /// <summary>
        /// 是否启用自动启动
        /// </summary>
        public bool EnableAutoStart { get; set; } = true;

        /// <summary>
        /// 首选网络类型
        /// </summary>
        public NetworkType PreferredNetworkType { get; set; } = NetworkType.SteamP2P;

        /// <summary>
        /// 默认端口
        /// </summary>
        public int DefaultPort { get; set; } = 7777;

        /// <summary>
        /// 最大客机数
        /// </summary>
        public int MaxClients { get; set; } = 8;

        /// <summary>
        /// 最大历史消息数
        /// </summary>
        public int MaxHistoryMessages { get; set; } = 100;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// 重试间隔（秒）
        /// </summary>
        public int RetryIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// 条件检查间隔（秒）
        /// </summary>
        public float ConditionCheckIntervalSeconds { get; set; } = 10f;

        /// <summary>
        /// 网络状态检查间隔（秒）
        /// </summary>
        public float NetworkStatusCheckIntervalSeconds { get; set; } = 5f;

        /// <summary>
        /// 是否需要网络可用
        /// </summary>
        public bool RequireNetworkAvailable { get; set; } = true;

        /// <summary>
        /// 是否需要Steam在线
        /// </summary>
        public bool RequireSteamOnline { get; set; } = true;

        /// <summary>
        /// 是否需要在游戏房间中
        /// </summary>
        public bool RequireInRoom { get; set; } = false;

        /// <summary>
        /// 是否启用时间限制
        /// </summary>
        public bool EnableTimeRestriction { get; set; } = false;

        /// <summary>
        /// 允许启动的开始时间
        /// </summary>
        public TimeSpan AllowedStartTime { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// 允许启动的结束时间
        /// </summary>
        public TimeSpan AllowedEndTime { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// 是否启用自动备份
        /// </summary>
        public bool EnableAutoBackup { get; set; } = true;

        /// <summary>
        /// 最小网络质量分数
        /// </summary>
        public int MinNetworkQualityScore { get; set; } = 30;

        /// <summary>
        /// 是否启用基于质量的重启
        /// </summary>
        public bool EnableQualityBasedRestart { get; set; } = true;

        /// <summary>
        /// 是否启用连接丢失重启
        /// </summary>
        public bool EnableConnectionLossRestart { get; set; } = true;
    }

    /// <summary>
    /// 自动启动统计信息
    /// </summary>
    public class AutoStartStatistics
    {
        /// <summary>
        /// 总启动尝试次数
        /// </summary>
        public long TotalStartAttempts { get; set; }

        /// <summary>
        /// 总启动成功次数
        /// </summary>
        public long TotalStartSuccesses { get; set; }

        /// <summary>
        /// 总启动失败次数
        /// </summary>
        public long TotalStartFailures { get; set; }

        /// <summary>
        /// 总服务重启次数
        /// </summary>
        public long TotalServiceRestarts { get; set; }

        /// <summary>
        /// 总聊天错误次数
        /// </summary>
        public long TotalChatErrors { get; set; }

        /// <summary>
        /// 总网络异常次数
        /// </summary>
        public long TotalNetworkExceptions { get; set; }

        /// <summary>
        /// 启动成功率
        /// </summary>
        public double SuccessRate => TotalStartAttempts > 0 ? 
            (double)TotalStartSuccesses / TotalStartAttempts * 100 : 0;

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            TotalStartAttempts = 0;
            TotalStartSuccesses = 0;
            TotalStartFailures = 0;
            TotalServiceRestarts = 0;
            TotalChatErrors = 0;
            TotalNetworkExceptions = 0;
        }

        /// <summary>
        /// 克隆统计信息
        /// </summary>
        /// <returns>统计信息副本</returns>
        public AutoStartStatistics Clone()
        {
            return new AutoStartStatistics
            {
                TotalStartAttempts = TotalStartAttempts,
                TotalStartSuccesses = TotalStartSuccesses,
                TotalStartFailures = TotalStartFailures,
                TotalServiceRestarts = TotalServiceRestarts,
                TotalChatErrors = TotalChatErrors,
                TotalNetworkExceptions = TotalNetworkExceptions
            };
        }
    }
}