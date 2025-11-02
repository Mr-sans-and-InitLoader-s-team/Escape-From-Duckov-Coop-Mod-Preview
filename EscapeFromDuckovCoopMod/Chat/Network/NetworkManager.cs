using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 网络管理器，统一管理不同类型的网络适配器
    /// 提供网络类型自动检测、切换和降级功能
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        #region 单例模式
        
        private static NetworkManager _instance;
        
        /// <summary>
        /// 网络管理器单例实例
        /// </summary>
        public static NetworkManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("NetworkManager");
                    _instance = go.AddComponent<NetworkManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region 字段和属性
        
        /// <summary>
        /// 当前活动的网络适配器
        /// </summary>
        public INetworkAdapter CurrentAdapter { get; private set; }
        
        /// <summary>
        /// 当前网络类型
        /// </summary>
        public NetworkType CurrentNetworkType => CurrentAdapter?.CurrentNetworkType ?? NetworkType.SteamP2P;
        
        /// <summary>
        /// 连接状态
        /// </summary>
        public ConnectionStatus Status => CurrentAdapter?.Status ?? ConnectionStatus.Disconnected;
        
        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => CurrentAdapter?.IsConnected ?? false;
        
        /// <summary>
        /// 网络适配器注册表
        /// </summary>
        private readonly Dictionary<NetworkType, INetworkAdapter> _adapters = new Dictionary<NetworkType, INetworkAdapter>();
        
        /// <summary>
        /// 网络类型优先级列表（按优先级从高到低排序）
        /// </summary>
        private readonly List<NetworkType> _networkPriority = new List<NetworkType>
        {
            NetworkType.SteamP2P,    // Steam P2P 优先级最高
            NetworkType.DirectP2P    // 直连 P2P 作为备选
        };
        
        /// <summary>
        /// 网络质量监控数据
        /// </summary>
        private readonly Dictionary<NetworkType, NetworkQuality> _networkQuality = new Dictionary<NetworkType, NetworkQuality>();
        
        /// <summary>
        /// 是否启用自动网络切换
        /// </summary>
        public bool AutoSwitchEnabled { get; set; } = true;
        
        /// <summary>
        /// 网络质量检测间隔（毫秒）
        /// </summary>
        public int QualityCheckIntervalMs { get; set; } = 30000; // 30秒
        
        /// <summary>
        /// 最后一次质量检测时间
        /// </summary>
        private DateTime _lastQualityCheck = DateTime.MinValue;
        
        #endregion
        
        #region 事件
        
        /// <summary>
        /// 网络类型切换事件
        /// </summary>
        public event Action<NetworkType, NetworkType> OnNetworkTypeSwitched;
        
        /// <summary>
        /// 网络质量变化事件
        /// </summary>
        public event Action<NetworkType, NetworkQuality> OnNetworkQualityChanged;
        
        /// <summary>
        /// 网络自动降级事件
        /// </summary>
        public event Action<NetworkType, string> OnNetworkDegraded;
        
        /// <summary>
        /// 转发当前适配器的事件
        /// </summary>
        public event Action<string> OnClientConnected;
        public event Action<string> OnClientDisconnected;
        public event Action<byte[], string> OnMessageReceived;
        public event Action<NetworkError> OnNetworkError;
        public event Action<ConnectionStatus> OnConnectionStatusChanged;
        
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
            
            InitializeNetworkQuality();
        }
        
        private void Start()
        {
            // 延迟初始化，确保其他系统已准备就绪
            Invoke(nameof(InitializeAdapters), 0.1f);
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
            {
                DisconnectAll();
                _instance = null;
            }
        }
        
        /// <summary>
        /// 初始化网络质量监控数据
        /// </summary>
        private void InitializeNetworkQuality()
        {
            foreach (NetworkType type in Enum.GetValues(typeof(NetworkType)))
            {
                _networkQuality[type] = new NetworkQuality();
            }
        }
        
        /// <summary>
        /// 初始化网络适配器
        /// </summary>
        private void InitializeAdapters()
        {
            try
            {
                LogInfo("开始初始化网络适配器...");
                
                // 注册 Steam P2P 网络适配器
                RegisterAdapter(new SteamP2PNetwork());
                
                // 注册直连 P2P 网络适配器
                RegisterAdapter(new DirectP2PNetwork());
                
                LogInfo("网络适配器初始化完成");
            }
            catch (Exception ex)
            {
                LogError($"初始化网络适配器时发生异常: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 适配器管理
        
        /// <summary>
        /// 注册网络适配器
        /// </summary>
        /// <param name="adapter">网络适配器实例</param>
        public void RegisterAdapter(INetworkAdapter adapter)
        {
            if (adapter == null)
            {
                LogError("无法注册空的网络适配器");
                return;
            }
            
            var type = adapter.CurrentNetworkType;
            
            if (_adapters.ContainsKey(type))
            {
                LogWarning($"网络适配器已存在，将替换: {type}");
                UnsubscribeAdapterEvents(_adapters[type]);
            }
            
            _adapters[type] = adapter;
            SubscribeAdapterEvents(adapter);
            
            LogInfo($"网络适配器已注册: {type}");
            
            // 如果当前没有活动适配器，设置为默认适配器
            if (CurrentAdapter == null)
            {
                SetCurrentAdapter(type);
            }
        }
        
        /// <summary>
        /// 取消注册网络适配器
        /// </summary>
        /// <param name="type">网络类型</param>
        public void UnregisterAdapter(NetworkType type)
        {
            if (!_adapters.ContainsKey(type))
            {
                LogWarning($"网络适配器不存在: {type}");
                return;
            }
            
            var adapter = _adapters[type];
            UnsubscribeAdapterEvents(adapter);
            
            // 如果是当前活动适配器，需要切换到其他适配器
            if (CurrentAdapter == adapter)
            {
                adapter.Disconnect();
                CurrentAdapter = null;
                
                // 尝试切换到其他可用适配器
                var availableTypes = GetAvailableNetworkTypes();
                if (availableTypes.Count > 0)
                {
                    SetCurrentAdapter(availableTypes[0]);
                }
            }
            
            _adapters.Remove(type);
            LogInfo($"网络适配器已取消注册: {type}");
        }
        
        /// <summary>
        /// 设置当前活动的网络适配器
        /// </summary>
        /// <param name="type">网络类型</param>
        /// <returns>设置是否成功</returns>
        public bool SetCurrentAdapter(NetworkType type)
        {
            if (!_adapters.ContainsKey(type))
            {
                LogError($"网络适配器不存在: {type}");
                return false;
            }
            
            var oldType = CurrentAdapter?.CurrentNetworkType;
            var newAdapter = _adapters[type];
            
            if (CurrentAdapter == newAdapter)
            {
                LogDebug($"网络适配器已是当前活动适配器: {type}");
                return true;
            }
            
            // 断开当前适配器
            if (CurrentAdapter != null && CurrentAdapter.IsConnected)
            {
                CurrentAdapter.Disconnect();
            }
            
            CurrentAdapter = newAdapter;
            
            LogInfo($"网络适配器已切换: {oldType} -> {type}");
            
            if (oldType.HasValue)
            {
                OnNetworkTypeSwitched?.Invoke(oldType.Value, type);
            }
            
            return true;
        }
        
        #endregion
        
        #region 网络类型检测和切换
        
        /// <summary>
        /// 获取可用的网络类型列表
        /// </summary>
        /// <returns>可用网络类型列表</returns>
        public List<NetworkType> GetAvailableNetworkTypes()
        {
            return _adapters.Keys.ToList();
        }
        
        /// <summary>
        /// 检测最佳网络类型
        /// </summary>
        /// <returns>最佳网络类型</returns>
        public async Task<NetworkType?> DetectBestNetworkType()
        {
            LogInfo("开始检测最佳网络类型...");
            
            var availableTypes = GetAvailableNetworkTypes();
            if (availableTypes.Count == 0)
            {
                LogWarning("没有可用的网络适配器");
                return null;
            }
            
            // 按优先级排序
            var sortedTypes = availableTypes
                .OrderBy(type => _networkPriority.IndexOf(type))
                .ToList();
            
            // 检测每种网络类型的可用性和质量
            foreach (var type in sortedTypes)
            {
                if (await TestNetworkType(type))
                {
                    LogInfo($"检测到最佳网络类型: {type}");
                    return type;
                }
            }
            
            LogWarning("未找到可用的网络类型");
            return null;
        }
        
        /// <summary>
        /// 测试网络类型的可用性
        /// </summary>
        /// <param name="type">网络类型</param>
        /// <returns>是否可用</returns>
        private async Task<bool> TestNetworkType(NetworkType type)
        {
            if (!_adapters.ContainsKey(type))
            {
                return false;
            }
            
            try
            {
                var adapter = _adapters[type];
                
                // 检查适配器的基本可用性
                var availableNetworks = adapter.GetAvailableNetworks();
                if (!availableNetworks.Contains(type))
                {
                    LogDebug($"网络类型不可用: {type}");
                    return false;
                }
                
                // TODO: 添加更详细的网络质量测试
                // 例如：延迟测试、带宽测试、连接稳定性测试等
                
                UpdateNetworkQuality(type, true, 0, 100);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"测试网络类型时发生异常 {type}: {ex.Message}");
                UpdateNetworkQuality(type, false, 0, 0);
                return false;
            }
        }
        
        /// <summary>
        /// 自动切换到最佳网络类型
        /// </summary>
        /// <returns>切换是否成功</returns>
        public async Task<bool> AutoSwitchToBestNetwork()
        {
            if (!AutoSwitchEnabled)
            {
                LogDebug("自动网络切换已禁用");
                return false;
            }
            
            var bestType = await DetectBestNetworkType();
            if (!bestType.HasValue)
            {
                LogWarning("未找到可用的网络类型进行切换");
                return false;
            }
            
            if (CurrentAdapter?.CurrentNetworkType == bestType.Value)
            {
                LogDebug($"当前网络类型已是最佳选择: {bestType.Value}");
                return true;
            }
            
            return SetCurrentAdapter(bestType.Value);
        }
        
        /// <summary>
        /// 网络降级处理
        /// </summary>
        /// <param name="reason">降级原因</param>
        /// <returns>降级是否成功</returns>
        public async Task<bool> DegradeNetwork(string reason)
        {
            LogWarning($"网络降级触发: {reason}");
            
            var currentType = CurrentAdapter?.CurrentNetworkType;
            if (!currentType.HasValue)
            {
                LogError("没有当前网络类型可以降级");
                return false;
            }
            
            // 查找下一个可用的网络类型
            var availableTypes = GetAvailableNetworkTypes();
            var currentIndex = _networkPriority.IndexOf(currentType.Value);
            
            for (int i = currentIndex + 1; i < _networkPriority.Count; i++)
            {
                var fallbackType = _networkPriority[i];
                if (availableTypes.Contains(fallbackType))
                {
                    LogInfo($"降级到网络类型: {currentType.Value} -> {fallbackType}");
                    
                    if (SetCurrentAdapter(fallbackType))
                    {
                        OnNetworkDegraded?.Invoke(fallbackType, reason);
                        return true;
                    }
                }
            }
            
            LogError("没有可用的网络类型进行降级");
            return false;
        }
        
        #endregion
        
        #region 网络质量监控
        
        private void Update()
        {
            // 更新当前网络适配器
            if (CurrentAdapter is SteamP2PNetwork steamAdapter)
            {
                steamAdapter.Update();
            }
            else if (CurrentAdapter is DirectP2PNetwork directAdapter)
            {
                directAdapter.Update();
            }
            
            // 定期检查网络质量
            if (AutoSwitchEnabled && 
                (DateTime.Now - _lastQualityCheck).TotalMilliseconds >= QualityCheckIntervalMs)
            {
                _lastQualityCheck = DateTime.Now;
                _ = CheckNetworkQuality();
            }
        }
        
        /// <summary>
        /// 检查网络质量
        /// </summary>
        private async Task CheckNetworkQuality()
        {
            try
            {
                foreach (var type in GetAvailableNetworkTypes())
                {
                    await TestNetworkType(type);
                }
                
                // 如果当前网络质量较差，尝试切换到更好的网络
                var currentType = CurrentAdapter?.CurrentNetworkType;
                if (currentType.HasValue && _networkQuality.ContainsKey(currentType.Value))
                {
                    var currentQuality = _networkQuality[currentType.Value];
                    if (currentQuality.Score < 50) // 质量分数低于50时考虑切换
                    {
                        await AutoSwitchToBestNetwork();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"检查网络质量时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新网络质量数据
        /// </summary>
        /// <param name="type">网络类型</param>
        /// <param name="isAvailable">是否可用</param>
        /// <param name="latency">延迟（毫秒）</param>
        /// <param name="score">质量分数（0-100）</param>
        private void UpdateNetworkQuality(NetworkType type, bool isAvailable, int latency, int score)
        {
            if (!_networkQuality.ContainsKey(type))
            {
                _networkQuality[type] = new NetworkQuality();
            }
            
            var quality = _networkQuality[type];
            var oldScore = quality.Score;
            
            quality.IsAvailable = isAvailable;
            quality.Latency = latency;
            quality.Score = score;
            quality.LastUpdated = DateTime.Now;
            
            if (Math.Abs(oldScore - score) >= 10) // 分数变化超过10时触发事件
            {
                OnNetworkQualityChanged?.Invoke(type, quality);
            }
        }
        
        /// <summary>
        /// 获取网络质量信息
        /// </summary>
        /// <param name="type">网络类型</param>
        /// <returns>网络质量信息</returns>
        public NetworkQuality GetNetworkQuality(NetworkType type)
        {
            return _networkQuality.ContainsKey(type) ? _networkQuality[type] : new NetworkQuality();
        }
        
        #endregion
        
        #region 网络操作代理
        
        /// <summary>
        /// 启动主机服务
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <returns>启动是否成功</returns>
        public async Task<bool> StartHost(NetworkConfig config)
        {
            if (CurrentAdapter == null)
            {
                // 尝试自动选择最佳网络类型
                var bestType = await DetectBestNetworkType();
                if (bestType.HasValue)
                {
                    SetCurrentAdapter(bestType.Value);
                }
            }
            
            if (CurrentAdapter == null)
            {
                LogError("没有可用的网络适配器启动主机服务");
                return false;
            }
            
            // 如果配置的网络类型与当前适配器不匹配，尝试切换
            if (config.Type != CurrentAdapter.CurrentNetworkType)
            {
                if (!SetCurrentAdapter(config.Type))
                {
                    LogError($"无法切换到指定的网络类型: {config.Type}");
                    return false;
                }
            }
            
            return await CurrentAdapter.StartHost(config);
        }
        
        /// <summary>
        /// 连接到主机
        /// </summary>
        /// <param name="endpoint">主机端点</param>
        /// <returns>连接是否成功</returns>
        public async Task<bool> ConnectToHost(string endpoint)
        {
            if (CurrentAdapter == null)
            {
                // 尝试自动选择最佳网络类型
                var bestType = await DetectBestNetworkType();
                if (bestType.HasValue)
                {
                    SetCurrentAdapter(bestType.Value);
                }
            }
            
            if (CurrentAdapter == null)
            {
                LogError("没有可用的网络适配器连接主机");
                return false;
            }
            
            var result = await CurrentAdapter.ConnectToHost(endpoint);
            
            // 如果连接失败，尝试降级到其他网络类型
            if (!result && AutoSwitchEnabled)
            {
                LogWarning("连接失败，尝试网络降级...");
                if (await DegradeNetwork("连接失败"))
                {
                    result = await CurrentAdapter.ConnectToHost(endpoint);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            CurrentAdapter?.Disconnect();
        }
        
        /// <summary>
        /// 断开所有网络适配器
        /// </summary>
        public void DisconnectAll()
        {
            foreach (var adapter in _adapters.Values)
            {
                try
                {
                    adapter.Disconnect();
                }
                catch (Exception ex)
                {
                    LogError($"断开网络适配器时发生异常: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="targetId">目标ID</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendMessage(byte[] data, string targetId = null)
        {
            if (CurrentAdapter == null)
            {
                LogError("没有可用的网络适配器发送消息");
                return false;
            }
            
            var result = await CurrentAdapter.SendMessage(data, targetId);
            
            // 如果发送失败，可能是网络问题，记录质量下降
            if (!result)
            {
                var currentType = CurrentAdapter.CurrentNetworkType;
                var quality = GetNetworkQuality(currentType);
                UpdateNetworkQuality(currentType, quality.IsAvailable, quality.Latency, Math.Max(0, quality.Score - 10));
            }
            
            return result;
        }
        
        /// <summary>
        /// 广播消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>广播是否成功</returns>
        public async Task<bool> BroadcastMessage(byte[] data)
        {
            return await SendMessage(data, null);
        }
        
        #endregion
        
        #region 事件处理
        
        /// <summary>
        /// 订阅适配器事件
        /// </summary>
        /// <param name="adapter">网络适配器</param>
        private void SubscribeAdapterEvents(INetworkAdapter adapter)
        {
            adapter.OnClientConnected += HandleClientConnected;
            adapter.OnClientDisconnected += HandleClientDisconnected;
            adapter.OnMessageReceived += HandleMessageReceived;
            adapter.OnNetworkError += HandleNetworkError;
            adapter.OnConnectionStatusChanged += HandleConnectionStatusChanged;
        }
        
        /// <summary>
        /// 取消订阅适配器事件
        /// </summary>
        /// <param name="adapter">网络适配器</param>
        private void UnsubscribeAdapterEvents(INetworkAdapter adapter)
        {
            adapter.OnClientConnected -= HandleClientConnected;
            adapter.OnClientDisconnected -= HandleClientDisconnected;
            adapter.OnMessageReceived -= HandleMessageReceived;
            adapter.OnNetworkError -= HandleNetworkError;
            adapter.OnConnectionStatusChanged -= HandleConnectionStatusChanged;
        }
        
        private void HandleClientConnected(string clientId)
        {
            OnClientConnected?.Invoke(clientId);
        }
        
        private void HandleClientDisconnected(string clientId)
        {
            OnClientDisconnected?.Invoke(clientId);
        }
        
        private void HandleMessageReceived(byte[] data, string senderId)
        {
            OnMessageReceived?.Invoke(data, senderId);
        }
        
        private void HandleNetworkError(NetworkError error)
        {
            LogError($"网络错误: {error}");
            OnNetworkError?.Invoke(error);
            
            // 根据错误类型决定是否需要网络降级
            if (error.Type == NetworkErrorType.ConnectionLost || 
                error.Type == NetworkErrorType.ConnectionFailed)
            {
                _ = DegradeNetwork(error.Message);
            }
        }
        
        private void HandleConnectionStatusChanged(ConnectionStatus status)
        {
            OnConnectionStatusChanged?.Invoke(status);
        }
        
        #endregion
        
        #region 日志方法
        
        private void LogInfo(string message)
        {
            Debug.Log($"[NetworkManager] {message}");
        }
        
        private void LogWarning(string message)
        {
            Debug.LogWarning($"[NetworkManager] {message}");
        }
        
        private void LogError(string message)
        {
            Debug.LogError($"[NetworkManager] {message}");
        }
        
        private void LogDebug(string message)
        {
            Debug.Log($"[NetworkManager][DEBUG] {message}");
        }
        
        #endregion
    }
    
    /// <summary>
    /// 网络质量信息类
    /// </summary>
    public class NetworkQuality
    {
        /// <summary>
        /// 是否可用
        /// </summary>
        public bool IsAvailable { get; set; }
        
        /// <summary>
        /// 延迟（毫秒）
        /// </summary>
        public int Latency { get; set; }
        
        /// <summary>
        /// 质量分数（0-100）
        /// </summary>
        public int Score { get; set; }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; }
        
        public NetworkQuality()
        {
            IsAvailable = false;
            Latency = 0;
            Score = 0;
            LastUpdated = DateTime.MinValue;
        }
        
        public override string ToString()
        {
            return $"可用: {IsAvailable}, 延迟: {Latency}ms, 分数: {Score}";
        }
    }
}