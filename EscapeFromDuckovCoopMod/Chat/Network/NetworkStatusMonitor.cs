using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 网络状态监控器
    /// 监控网络连接状态、质量和异常情况
    /// </summary>
    public class NetworkStatusMonitor : IDisposable
    {
        #region 字段和属性

        /// <summary>
        /// 网络管理器引用
        /// </summary>
        private readonly NetworkManager _networkManager;

        /// <summary>
        /// 当前网络状态
        /// </summary>
        public NetworkStatus CurrentStatus { get; private set; }

        /// <summary>
        /// 是否正在监控
        /// </summary>
        public bool IsMonitoring { get; private set; }

        /// <summary>
        /// 监控间隔（毫秒）
        /// </summary>
        public int MonitorIntervalMs { get; set; } = 5000; // 5秒

        /// <summary>
        /// 最后一次状态检查时间
        /// </summary>
        private DateTime _lastStatusCheck = DateTime.MinValue;

        /// <summary>
        /// 是否已释放资源
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 事件

        /// <summary>
        /// 网络状态变化事件
        /// </summary>
        public event Action<NetworkStatus> OnNetworkStatusChanged;

        /// <summary>
        /// 网络质量变化事件
        /// </summary>
        public event Action<NetworkQuality> OnNetworkQualityChanged;

        /// <summary>
        /// 网络异常事件
        /// </summary>
        public event Action<string> OnNetworkException;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化网络状态监控器
        /// </summary>
        /// <param name="networkManager">网络管理器</param>
        public NetworkStatusMonitor(NetworkManager networkManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            
            // 初始化状态
            CurrentStatus = new NetworkStatus();
            
            // 订阅网络管理器事件
            SubscribeNetworkEvents();
            
            // 开始监控
            StartMonitoring();
            
            LogDebug("网络状态监控器已初始化");
        }

        #endregion

        #region 监控控制

        /// <summary>
        /// 开始监控
        /// </summary>
        public void StartMonitoring()
        {
            if (IsMonitoring)
            {
                LogDebug("网络状态监控已在运行中");
                return;
            }

            IsMonitoring = true;
            _lastStatusCheck = DateTime.UtcNow;
            
            LogInfo("网络状态监控已启动");
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void StopMonitoring()
        {
            if (!IsMonitoring)
            {
                LogDebug("网络状态监控未在运行");
                return;
            }

            IsMonitoring = false;
            LogInfo("网络状态监控已停止");
        }

        /// <summary>
        /// 更新监控状态（需要在主线程中定期调用）
        /// </summary>
        public void Update()
        {
            if (!IsMonitoring || _disposed)
                return;

            var now = DateTime.UtcNow;
            if ((now - _lastStatusCheck).TotalMilliseconds >= MonitorIntervalMs)
            {
                _lastStatusCheck = now;
                CheckNetworkStatus();
            }
        }

        #endregion

        #region 状态检查

        /// <summary>
        /// 检查网络状态
        /// </summary>
        private void CheckNetworkStatus()
        {
            try
            {
                var newStatus = CollectNetworkStatus();
                
                // 检查状态是否发生变化
                if (HasStatusChanged(CurrentStatus, newStatus))
                {
                    var oldStatus = CurrentStatus;
                    CurrentStatus = newStatus;
                    
                    LogDebug($"网络状态变化: {oldStatus.ConnectionState} -> {newStatus.ConnectionState}");
                    OnNetworkStatusChanged?.Invoke(newStatus);
                }
            }
            catch (Exception ex)
            {
                LogError($"检查网络状态时发生异常: {ex.Message}");
                OnNetworkException?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// 收集当前网络状态信息
        /// </summary>
        /// <returns>网络状态</returns>
        private NetworkStatus CollectNetworkStatus()
        {
            var status = new NetworkStatus
            {
                Timestamp = DateTime.UtcNow,
                IsConnected = _networkManager.IsConnected,
                ConnectionState = _networkManager.Status,
                CurrentNetworkType = _networkManager.CurrentNetworkType
            };

            // 获取网络质量信息
            var quality = _networkManager.GetNetworkQuality(_networkManager.CurrentNetworkType);
            status.NetworkQuality = quality;

            // 获取连接详细信息
            if (_networkManager.CurrentAdapter != null)
            {
                status.AdapterInfo = new NetworkAdapterInfo
                {
                    Type = _networkManager.CurrentAdapter.CurrentNetworkType,
                    IsConnected = _networkManager.CurrentAdapter.IsConnected,
                    AvailableNetworks = _networkManager.CurrentAdapter.GetAvailableNetworks()
                };
            }

            return status;
        }

        /// <summary>
        /// 检查状态是否发生变化
        /// </summary>
        /// <param name="oldStatus">旧状态</param>
        /// <param name="newStatus">新状态</param>
        /// <returns>是否发生变化</returns>
        private bool HasStatusChanged(NetworkStatus oldStatus, NetworkStatus newStatus)
        {
            if (oldStatus == null || newStatus == null)
                return true;

            // 检查关键状态字段
            return oldStatus.IsConnected != newStatus.IsConnected ||
                   oldStatus.ConnectionState != newStatus.ConnectionState ||
                   oldStatus.CurrentNetworkType != newStatus.CurrentNetworkType ||
                   HasQualityChanged(oldStatus.NetworkQuality, newStatus.NetworkQuality);
        }

        /// <summary>
        /// 检查网络质量是否发生显著变化
        /// </summary>
        /// <param name="oldQuality">旧质量</param>
        /// <param name="newQuality">新质量</param>
        /// <returns>是否发生显著变化</returns>
        private bool HasQualityChanged(NetworkQuality oldQuality, NetworkQuality newQuality)
        {
            if (oldQuality == null || newQuality == null)
                return true;

            // 检查质量分数变化是否超过阈值
            const int qualityChangeThreshold = 10;
            return Math.Abs(oldQuality.Score - newQuality.Score) >= qualityChangeThreshold ||
                   oldQuality.IsAvailable != newQuality.IsAvailable;
        }

        #endregion

        #region 网络事件处理

        /// <summary>
        /// 订阅网络管理器事件
        /// </summary>
        private void SubscribeNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.OnConnectionStatusChanged += HandleConnectionStatusChanged;
                _networkManager.OnNetworkError += HandleNetworkError;
                _networkManager.OnNetworkTypeSwitched += HandleNetworkTypeSwitched;
                _networkManager.OnNetworkQualityChanged += HandleNetworkQualityChanged;
            }
        }

        /// <summary>
        /// 取消订阅网络管理器事件
        /// </summary>
        private void UnsubscribeNetworkEvents()
        {
            if (_networkManager != null)
            {
                _networkManager.OnConnectionStatusChanged -= HandleConnectionStatusChanged;
                _networkManager.OnNetworkError -= HandleNetworkError;
                _networkManager.OnNetworkTypeSwitched -= HandleNetworkTypeSwitched;
                _networkManager.OnNetworkQualityChanged -= HandleNetworkQualityChanged;
            }
        }

        /// <summary>
        /// 处理连接状态变化
        /// </summary>
        /// <param name="status">连接状态</param>
        private void HandleConnectionStatusChanged(ConnectionStatus status)
        {
            LogDebug($"连接状态变化: {status}");
            
            // 立即更新状态
            CheckNetworkStatus();
        }

        /// <summary>
        /// 处理网络错误
        /// </summary>
        /// <param name="error">网络错误</param>
        private void HandleNetworkError(NetworkError error)
        {
            LogError($"网络错误: {error.Type} - {error.Message}");
            OnNetworkException?.Invoke($"{error.Type}: {error.Message}");
            
            // 立即更新状态
            CheckNetworkStatus();
        }

        /// <summary>
        /// 处理网络类型切换
        /// </summary>
        /// <param name="oldType">旧网络类型</param>
        /// <param name="newType">新网络类型</param>
        private void HandleNetworkTypeSwitched(NetworkType oldType, NetworkType newType)
        {
            LogInfo($"网络类型切换: {oldType} -> {newType}");
            
            // 立即更新状态
            CheckNetworkStatus();
        }

        /// <summary>
        /// 处理网络质量变化
        /// </summary>
        /// <param name="networkType">网络类型</param>
        /// <param name="quality">网络质量</param>
        private void HandleNetworkQualityChanged(NetworkType networkType, NetworkQuality quality)
        {
            LogDebug($"网络质量变化 ({networkType}): {quality}");
            OnNetworkQualityChanged?.Invoke(quality);
        }

        #endregion

        #region 公共接口方法

        /// <summary>
        /// 获取网络状态摘要
        /// </summary>
        /// <returns>网络状态摘要</returns>
        public string GetStatusSummary()
        {
            if (CurrentStatus == null)
                return "状态未知";

            var summary = $"连接: {(CurrentStatus.IsConnected ? "已连接" : "未连接")}, " +
                         $"状态: {CurrentStatus.ConnectionState}, " +
                         $"网络: {CurrentStatus.CurrentNetworkType}";

            if (CurrentStatus.NetworkQuality != null)
            {
                summary += $", 质量: {CurrentStatus.NetworkQuality.Score}";
            }

            return summary;
        }

        /// <summary>
        /// 强制刷新网络状态
        /// </summary>
        public void RefreshStatus()
        {
            CheckNetworkStatus();
        }

        /// <summary>
        /// 获取网络诊断信息
        /// </summary>
        /// <returns>诊断信息</returns>
        public NetworkDiagnostics GetDiagnostics()
        {
            return new NetworkDiagnostics
            {
                CurrentStatus = CurrentStatus,
                IsMonitoring = IsMonitoring,
                MonitorIntervalMs = MonitorIntervalMs,
                LastStatusCheck = _lastStatusCheck,
                AvailableNetworkTypes = _networkManager?.GetAvailableNetworkTypes()
            };
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的具体实现
        /// </summary>
        /// <param name="disposing">是否正在释放</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 停止监控
                    StopMonitoring();
                    
                    // 取消事件订阅
                    UnsubscribeNetworkEvents();
                    
                    LogDebug("网络状态监控器资源已释放");
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~NetworkStatusMonitor()
        {
            Dispose(false);
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[NetworkStatusMonitor] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[NetworkStatusMonitor][DEBUG] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[NetworkStatusMonitor] {message}");
        }

        #endregion
    }

    /// <summary>
    /// 网络状态信息
    /// </summary>
    public class NetworkStatus
    {
        /// <summary>
        /// 状态时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 连接状态
        /// </summary>
        public ConnectionStatus ConnectionState { get; set; }

        /// <summary>
        /// 当前网络类型
        /// </summary>
        public NetworkType CurrentNetworkType { get; set; }

        /// <summary>
        /// 网络质量
        /// </summary>
        public NetworkQuality NetworkQuality { get; set; }

        /// <summary>
        /// 适配器信息
        /// </summary>
        public NetworkAdapterInfo AdapterInfo { get; set; }

        public NetworkStatus()
        {
            Timestamp = DateTime.UtcNow;
            ConnectionState = ConnectionStatus.Disconnected;
            CurrentNetworkType = NetworkType.SteamP2P;
        }

        public override string ToString()
        {
            return $"连接: {IsConnected}, 状态: {ConnectionState}, 网络: {CurrentNetworkType}";
        }
    }

    /// <summary>
    /// 网络适配器信息
    /// </summary>
    public class NetworkAdapterInfo
    {
        /// <summary>
        /// 适配器类型
        /// </summary>
        public NetworkType Type { get; set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 可用网络列表
        /// </summary>
        public System.Collections.Generic.List<NetworkType> AvailableNetworks { get; set; }

        public NetworkAdapterInfo()
        {
            AvailableNetworks = new System.Collections.Generic.List<NetworkType>();
        }
    }

    /// <summary>
    /// 网络诊断信息
    /// </summary>
    public class NetworkDiagnostics
    {
        /// <summary>
        /// 当前状态
        /// </summary>
        public NetworkStatus CurrentStatus { get; set; }

        /// <summary>
        /// 是否正在监控
        /// </summary>
        public bool IsMonitoring { get; set; }

        /// <summary>
        /// 监控间隔
        /// </summary>
        public int MonitorIntervalMs { get; set; }

        /// <summary>
        /// 最后状态检查时间
        /// </summary>
        public DateTime LastStatusCheck { get; set; }

        /// <summary>
        /// 可用网络类型
        /// </summary>
        public System.Collections.Generic.List<NetworkType> AvailableNetworkTypes { get; set; }

        public NetworkDiagnostics()
        {
            AvailableNetworkTypes = new System.Collections.Generic.List<NetworkType>();
        }
    }
}