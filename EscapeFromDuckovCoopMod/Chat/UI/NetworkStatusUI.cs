using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using EscapeFromDuckovCoopMod.Chat.Network;
using EscapeFromDuckovCoopMod.Chat.Managers;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 网络状态UI组件
    /// 显示网络连接状态、质量和异常信息
    /// </summary>
    public class NetworkStatusUI : MonoBehaviour
    {
        #region UI组件引用

        [Header("状态显示组件")]
        [SerializeField] private Text _connectionStatusText;
        [SerializeField] private Text _networkTypeText;
        [SerializeField] private Text _qualityScoreText;
        [SerializeField] private Text _latencyText;
        [SerializeField] private Text _clientCountText;

        [Header("状态指示器")]
        [SerializeField] private Image _connectionIndicator;
        [SerializeField] private Image _qualityIndicator;
        [SerializeField] private Slider _qualitySlider;

        [Header("错误和警告")]
        [SerializeField] private GameObject _errorPanel;
        [SerializeField] private Text _errorMessageText;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _dismissErrorButton;

        [Header("详细信息面板")]
        [SerializeField] private GameObject _detailsPanel;
        [SerializeField] private Button _showDetailsButton;
        [SerializeField] private Button _hideDetailsButton;
        [SerializeField] private Text _detailsText;

        [Header("自动启动控制")]
        [SerializeField] private Toggle _autoStartHostToggle;
        [SerializeField] private Button _manualStartHostButton;
        [SerializeField] private Button _stopServiceButton;

        #endregion

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
        /// 当前网络状态
        /// </summary>
        private NetworkStatus _currentStatus;

        /// <summary>
        /// 状态颜色配置
        /// </summary>
        private readonly Dictionary<ConnectionStatus, Color> _statusColors = new Dictionary<ConnectionStatus, Color>
        {
            { ConnectionStatus.Connected, Color.green },
            { ConnectionStatus.Connecting, Color.yellow },
            { ConnectionStatus.Disconnected, Color.gray },
            { ConnectionStatus.Failed, Color.red }
        };

        /// <summary>
        /// 质量颜色配置
        /// </summary>
        private readonly Dictionary<QualityLevel, Color> _qualityColors = new Dictionary<QualityLevel, Color>
        {
            { QualityLevel.Excellent, Color.green },
            { QualityLevel.Good, Color.yellow },
            { QualityLevel.Poor, Color.red },
            { QualityLevel.Unknown, Color.gray }
        };

        /// <summary>
        /// 是否显示详细信息
        /// </summary>
        private bool _showingDetails = false;

        /// <summary>
        /// 错误消息队列
        /// </summary>
        private readonly Queue<string> _errorQueue = new Queue<string>();

        /// <summary>
        /// 是否正在显示错误
        /// </summary>
        private bool _showingError = false;

        /// <summary>
        /// 状态更新定时器
        /// </summary>
        private float _updateTimer = 0f;

        /// <summary>
        /// 状态更新间隔（秒）
        /// </summary>
        private const float UPDATE_INTERVAL = 1.0f;

        #endregion

        #region Unity生命周期

        private void Awake()
        {
            InitializeUI();
        }

        private void Start()
        {
            InitializeNetworkStatusUI();
        }

        private void Update()
        {
            UpdateStatusDisplay();
        }

        private void OnDestroy()
        {
            CleanupNetworkStatusUI();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeUI()
        {
            // 设置按钮事件
            if (_retryButton != null)
                _retryButton.onClick.AddListener(OnRetryButtonClicked);

            if (_dismissErrorButton != null)
                _dismissErrorButton.onClick.AddListener(OnDismissErrorButtonClicked);

            if (_showDetailsButton != null)
                _showDetailsButton.onClick.AddListener(OnShowDetailsButtonClicked);

            if (_hideDetailsButton != null)
                _hideDetailsButton.onClick.AddListener(OnHideDetailsButtonClicked);

            if (_manualStartHostButton != null)
                _manualStartHostButton.onClick.AddListener(OnManualStartHostButtonClicked);

            if (_stopServiceButton != null)
                _stopServiceButton.onClick.AddListener(OnStopServiceButtonClicked);

            if (_autoStartHostToggle != null)
                _autoStartHostToggle.onValueChanged.AddListener(OnAutoStartHostToggleChanged);

            // 初始化UI状态
            if (_errorPanel != null)
                _errorPanel.SetActive(false);

            if (_detailsPanel != null)
                _detailsPanel.SetActive(false);

            LogDebug("网络状态UI组件已初始化");
        }

        /// <summary>
        /// 初始化网络状态UI
        /// </summary>
        private void InitializeNetworkStatusUI()
        {
            try
            {
                // 获取聊天管理器
                _chatManager = ChatManager.Instance;
                if (_chatManager == null)
                {
                    LogError("无法获取聊天管理器实例");
                    return;
                }

                // 订阅聊天管理器事件
                SubscribeChatManagerEvents();

                // 获取网络状态监控器
                // 注意：这里需要等待聊天管理器完全初始化
                Invoke(nameof(DelayedInitializeNetworkMonitor), 0.5f);

                LogInfo("网络状态UI已初始化");
            }
            catch (Exception ex)
            {
                LogError($"初始化网络状态UI时发生异常: {ex.Message}");
                ShowError($"初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 延迟初始化网络监控器
        /// </summary>
        private void DelayedInitializeNetworkMonitor()
        {
            try
            {
                // 这里需要从聊天管理器获取网络状态监控器
                // 由于架构限制，我们直接创建一个监控器实例
                var networkManager = NetworkManager.Instance;
                if (networkManager != null)
                {
                    _networkStatusMonitor = new NetworkStatusMonitor(networkManager);
                    SubscribeNetworkStatusEvents();
                }
            }
            catch (Exception ex)
            {
                LogError($"延迟初始化网络监控器时发生异常: {ex.Message}");
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
                _chatManager.OnNetworkStatusChanged += HandleNetworkStatusChanged;
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
                _chatManager.OnNetworkStatusChanged -= HandleNetworkStatusChanged;
                _chatManager.OnChatError -= HandleChatError;
            }
        }

        /// <summary>
        /// 订阅网络状态事件
        /// </summary>
        private void SubscribeNetworkStatusEvents()
        {
            if (_networkStatusMonitor != null)
            {
                _networkStatusMonitor.OnNetworkStatusChanged += HandleDetailedNetworkStatusChanged;
                _networkStatusMonitor.OnNetworkQualityChanged += HandleNetworkQualityChanged;
                _networkStatusMonitor.OnNetworkException += HandleNetworkException;
            }
        }

        /// <summary>
        /// 取消订阅网络状态事件
        /// </summary>
        private void UnsubscribeNetworkStatusEvents()
        {
            if (_networkStatusMonitor != null)
            {
                _networkStatusMonitor.OnNetworkStatusChanged -= HandleDetailedNetworkStatusChanged;
                _networkStatusMonitor.OnNetworkQualityChanged -= HandleNetworkQualityChanged;
                _networkStatusMonitor.OnNetworkException -= HandleNetworkException;
            }
        }

        #endregion

        #region 状态显示更新

        /// <summary>
        /// 更新状态显示
        /// </summary>
        private void UpdateStatusDisplay()
        {
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= UPDATE_INTERVAL)
            {
                _updateTimer = 0f;
                RefreshStatusDisplay();
            }

            // 更新网络监控器
            _networkStatusMonitor?.Update();
        }

        /// <summary>
        /// 刷新状态显示
        /// </summary>
        private void RefreshStatusDisplay()
        {
            try
            {
                if (_chatManager == null)
                    return;

                // 更新连接状态
                UpdateConnectionStatus();

                // 更新网络类型
                UpdateNetworkType();

                // 更新客机数量（主机模式下）
                UpdateClientCount();

                // 更新质量信息
                UpdateQualityDisplay();

                // 更新控制按钮状态
                UpdateControlButtons();
            }
            catch (Exception ex)
            {
                LogError($"刷新状态显示时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新连接状态显示
        /// </summary>
        private void UpdateConnectionStatus()
        {
            var isConnected = _chatManager.IsConnected;
            var statusDescription = _chatManager.GetConnectionStatusDescription();

            if (_connectionStatusText != null)
            {
                _connectionStatusText.text = statusDescription;
            }

            if (_connectionIndicator != null)
            {
                var status = isConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
                _connectionIndicator.color = _statusColors.ContainsKey(status) ? 
                    _statusColors[status] : Color.gray;
            }
        }

        /// <summary>
        /// 更新网络类型显示
        /// </summary>
        private void UpdateNetworkType()
        {
            if (_networkTypeText != null)
            {
                var networkManager = NetworkManager.Instance;
                var networkType = networkManager?.CurrentNetworkType.ToString() ?? "未知";
                _networkTypeText.text = $"网络: {networkType}";
            }
        }

        /// <summary>
        /// 更新客机数量显示
        /// </summary>
        private void UpdateClientCount()
        {
            if (_clientCountText != null)
            {
                var clientCount = _chatManager.GetConnectedClientCount();
                _clientCountText.text = $"客机: {clientCount}";
            }
        }

        /// <summary>
        /// 更新质量显示
        /// </summary>
        private void UpdateQualityDisplay()
        {
            if (_currentStatus?.NetworkQuality != null)
            {
                var quality = _currentStatus.NetworkQuality;

                if (_qualityScoreText != null)
                {
                    _qualityScoreText.text = $"质量: {quality.Score}";
                }

                if (_latencyText != null)
                {
                    _latencyText.text = $"延迟: {quality.Latency}ms";
                }

                if (_qualitySlider != null)
                {
                    _qualitySlider.value = quality.Score / 100f;
                }

                if (_qualityIndicator != null)
                {
                    var qualityLevel = GetQualityLevel(quality.Score);
                    _qualityIndicator.color = _qualityColors.ContainsKey(qualityLevel) ? 
                        _qualityColors[qualityLevel] : Color.gray;
                }
            }
        }

        /// <summary>
        /// 更新控制按钮状态
        /// </summary>
        private void UpdateControlButtons()
        {
            var currentMode = _chatManager.CurrentMode;
            var isConnected = _chatManager.IsConnected;

            if (_manualStartHostButton != null)
            {
                _manualStartHostButton.interactable = currentMode != ChatManager.ChatMode.Host || !isConnected;
            }

            if (_stopServiceButton != null)
            {
                _stopServiceButton.interactable = currentMode != ChatManager.ChatMode.Local && isConnected;
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
            RefreshStatusDisplay();
        }

        /// <summary>
        /// 处理连接状态变化
        /// </summary>
        private void HandleConnectionStatusChanged(bool isConnected)
        {
            LogInfo($"连接状态变化: {isConnected}");
            RefreshStatusDisplay();
        }

        /// <summary>
        /// 处理网络状态变化
        /// </summary>
        private void HandleNetworkStatusChanged(NetworkStatus status)
        {
            LogDebug($"网络状态变化: {status}");
            _currentStatus = status;
            RefreshStatusDisplay();
        }

        /// <summary>
        /// 处理详细网络状态变化
        /// </summary>
        private void HandleDetailedNetworkStatusChanged(NetworkStatus status)
        {
            _currentStatus = status;
            RefreshStatusDisplay();
            UpdateDetailsPanel();
        }

        /// <summary>
        /// 处理网络质量变化
        /// </summary>
        private void HandleNetworkQualityChanged(NetworkQuality quality)
        {
            LogDebug($"网络质量变化: {quality}");
            RefreshStatusDisplay();
        }

        /// <summary>
        /// 处理网络异常
        /// </summary>
        private void HandleNetworkException(string exception)
        {
            LogError($"网络异常: {exception}");
            ShowError($"网络异常: {exception}");
        }

        /// <summary>
        /// 处理聊天错误
        /// </summary>
        private void HandleChatError(string error)
        {
            LogError($"聊天错误: {error}");
            ShowError(error);
        }

        #endregion

        #region 按钮事件处理

        /// <summary>
        /// 处理重试按钮点击
        /// </summary>
        private void OnRetryButtonClicked()
        {
            try
            {
                LogInfo("用户点击重试按钮");

                // 根据当前模式执行重试逻辑
                switch (_chatManager.CurrentMode)
                {
                    case ChatManager.ChatMode.Host:
                        _ = _chatManager.SwitchToHostMode();
                        break;

                    case ChatManager.ChatMode.Client:
                        // 这里需要获取之前的主机端点信息
                        ShowError("客机重连功能需要主机端点信息");
                        break;

                    default:
                        _ = _chatManager.SwitchToLocalMode();
                        break;
                }

                DismissError();
            }
            catch (Exception ex)
            {
                LogError($"重试操作时发生异常: {ex.Message}");
                ShowError($"重试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理关闭错误按钮点击
        /// </summary>
        private void OnDismissErrorButtonClicked()
        {
            DismissError();
        }

        /// <summary>
        /// 处理显示详细信息按钮点击
        /// </summary>
        private void OnShowDetailsButtonClicked()
        {
            ShowDetails();
        }

        /// <summary>
        /// 处理隐藏详细信息按钮点击
        /// </summary>
        private void OnHideDetailsButtonClicked()
        {
            HideDetails();
        }

        /// <summary>
        /// 处理手动启动主机按钮点击
        /// </summary>
        private void OnManualStartHostButtonClicked()
        {
            try
            {
                LogInfo("用户手动启动主机服务");
                _ = _chatManager.SwitchToHostMode();
            }
            catch (Exception ex)
            {
                LogError($"手动启动主机时发生异常: {ex.Message}");
                ShowError($"启动主机失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理停止服务按钮点击
        /// </summary>
        private void OnStopServiceButtonClicked()
        {
            try
            {
                LogInfo("用户停止聊天服务");
                _ = _chatManager.SwitchToLocalMode();
            }
            catch (Exception ex)
            {
                LogError($"停止服务时发生异常: {ex.Message}");
                ShowError($"停止服务失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理自动启动主机开关变化
        /// </summary>
        private void OnAutoStartHostToggleChanged(bool enabled)
        {
            LogInfo($"自动启动主机设置变化: {enabled}");
            
            if (enabled && _chatManager.CurrentMode == ChatManager.ChatMode.Local)
            {
                // 自动启动主机模式
                _ = _chatManager.SwitchToHostMode();
            }
        }

        #endregion

        #region 错误显示管理

        /// <summary>
        /// 显示错误消息
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        public void ShowError(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                return;

            _errorQueue.Enqueue(errorMessage);

            if (!_showingError)
            {
                DisplayNextError();
            }
        }

        /// <summary>
        /// 显示下一个错误
        /// </summary>
        private void DisplayNextError()
        {
            if (_errorQueue.Count == 0)
            {
                _showingError = false;
                if (_errorPanel != null)
                    _errorPanel.SetActive(false);
                return;
            }

            var errorMessage = _errorQueue.Dequeue();
            _showingError = true;

            if (_errorPanel != null)
                _errorPanel.SetActive(true);

            if (_errorMessageText != null)
                _errorMessageText.text = errorMessage;

            LogWarning($"显示错误消息: {errorMessage}");
        }

        /// <summary>
        /// 关闭当前错误显示
        /// </summary>
        private void DismissError()
        {
            DisplayNextError();
        }

        #endregion

        #region 详细信息面板

        /// <summary>
        /// 显示详细信息
        /// </summary>
        private void ShowDetails()
        {
            _showingDetails = true;
            if (_detailsPanel != null)
                _detailsPanel.SetActive(true);

            UpdateDetailsPanel();
        }

        /// <summary>
        /// 隐藏详细信息
        /// </summary>
        private void HideDetails()
        {
            _showingDetails = false;
            if (_detailsPanel != null)
                _detailsPanel.SetActive(false);
        }

        /// <summary>
        /// 更新详细信息面板
        /// </summary>
        private void UpdateDetailsPanel()
        {
            if (!_showingDetails || _detailsText == null)
                return;

            try
            {
                var details = GenerateDetailedStatusText();
                _detailsText.text = details;
            }
            catch (Exception ex)
            {
                LogError($"更新详细信息面板时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成详细状态文本
        /// </summary>
        /// <returns>详细状态文本</returns>
        private string GenerateDetailedStatusText()
        {
            var details = new System.Text.StringBuilder();

            // 基本信息
            details.AppendLine("=== 网络状态详情 ===");
            details.AppendLine($"聊天模式: {_chatManager?.CurrentMode}");
            details.AppendLine($"连接状态: {_chatManager?.IsConnected}");

            // 网络信息
            var networkManager = NetworkManager.Instance;
            if (networkManager != null)
            {
                details.AppendLine($"网络类型: {networkManager.CurrentNetworkType}");
                details.AppendLine($"网络状态: {networkManager.Status}");
                details.AppendLine($"可用网络: {string.Join(", ", networkManager.GetAvailableNetworkTypes())}");
            }

            // 质量信息
            if (_currentStatus?.NetworkQuality != null)
            {
                var quality = _currentStatus.NetworkQuality;
                details.AppendLine($"网络质量: {quality.Score}/100");
                details.AppendLine($"延迟: {quality.Latency}ms");
                details.AppendLine($"可用性: {quality.IsAvailable}");
                details.AppendLine($"最后更新: {quality.LastUpdated:HH:mm:ss}");
            }

            // 监控器信息
            if (_networkStatusMonitor != null)
            {
                var diagnostics = _networkStatusMonitor.GetDiagnostics();
                details.AppendLine($"监控状态: {diagnostics.IsMonitoring}");
                details.AppendLine($"监控间隔: {diagnostics.MonitorIntervalMs}ms");
                details.AppendLine($"最后检查: {diagnostics.LastStatusCheck:HH:mm:ss}");
            }

            // 客机信息（主机模式下）
            if (_chatManager?.CurrentMode == ChatManager.ChatMode.Host)
            {
                var clientCount = _chatManager.GetConnectedClientCount();
                details.AppendLine($"连接客机数: {clientCount}");
            }

            return details.ToString();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取质量等级
        /// </summary>
        /// <param name="score">质量分数</param>
        /// <returns>质量等级</returns>
        private QualityLevel GetQualityLevel(int score)
        {
            if (score >= 80)
                return QualityLevel.Excellent;
            else if (score >= 60)
                return QualityLevel.Good;
            else if (score >= 30)
                return QualityLevel.Poor;
            else
                return QualityLevel.Unknown;
        }

        #endregion

        #region 清理

        /// <summary>
        /// 清理网络状态UI
        /// </summary>
        private void CleanupNetworkStatusUI()
        {
            try
            {
                UnsubscribeChatManagerEvents();
                UnsubscribeNetworkStatusEvents();
                _networkStatusMonitor?.Dispose();

                LogDebug("网络状态UI已清理");
            }
            catch (Exception ex)
            {
                LogError($"清理网络状态UI时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 刷新显示
        /// </summary>
        public void RefreshDisplay()
        {
            RefreshStatusDisplay();
        }

        /// <summary>
        /// 显示网络异常提示
        /// </summary>
        /// <param name="exception">异常信息</param>
        public void ShowNetworkException(string exception)
        {
            ShowError($"网络异常: {exception}");
        }

        /// <summary>
        /// 获取当前显示的状态摘要
        /// </summary>
        /// <returns>状态摘要</returns>
        public string GetStatusSummary()
        {
            return _networkStatusMonitor?.GetStatusSummary() ?? "状态未知";
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[NetworkStatusUI] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[NetworkStatusUI] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[NetworkStatusUI] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[NetworkStatusUI][DEBUG] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 质量等级枚举
    /// </summary>
    public enum QualityLevel
    {
        /// <summary>
        /// 未知
        /// </summary>
        Unknown,

        /// <summary>
        /// 差
        /// </summary>
        Poor,

        /// <summary>
        /// 良好
        /// </summary>
        Good,

        /// <summary>
        /// 优秀
        /// </summary>
        Excellent
    }
}