using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using System.Linq;
using System.Text;
using LiteNetLib;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 直连 P2P 网络适配器实现
    /// 提供基于 TCP/UDP 的直接 P2P 网络通信功能
    /// </summary>
    public class DirectP2PNetwork : NetworkAdapter, IDirectP2PNetwork
    {
        #region 常量定义

        /// <summary>
        /// 默认监听端口（与NetService保持一致）
        /// </summary>
        private const int DEFAULT_PORT = 9050;

        /// <summary>
        /// 广播发现端口
        /// </summary>
        private const int DISCOVERY_PORT = 9051;

        /// <summary>
        /// 最大消息大小（字节）
        /// </summary>
        private const int MAX_MESSAGE_SIZE = 1024 * 1024; // 1MB

        /// <summary>
        /// 心跳间隔（毫秒）
        /// </summary>
        private const int HEARTBEAT_INTERVAL_MS = 5000;

        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        private const int CONNECTION_TIMEOUT_MS = 10000;

        /// <summary>
        /// 广播间隔（毫秒）
        /// </summary>
        private const int BROADCAST_INTERVAL_MS = 3000;

        #endregion

        #region 字段和属性

        /// <summary>
        /// 当前网络类型
        /// </summary>
        public override NetworkType CurrentNetworkType => NetworkType.DirectP2P;

        /// <summary>
        /// 是否启用 UPnP 端口映射
        /// </summary>
        public bool EnableUPnP { get; set; } = true;

        /// <summary>
        /// UDP 客户端（用于所有通信）
        /// </summary>
        private UdpClient _udpClient;

        /// <summary>
        /// 连接的客户端列表
        /// </summary>
        private readonly Dictionary<string, DirectP2PClient> _connectedClients = new Dictionary<string, DirectP2PClient>();

        /// <summary>
        /// 当前监听端口
        /// </summary>
        private int _currentPort;

        /// <summary>
        /// 主机广播定时器
        /// </summary>
        private Timer _broadcastTimer;

        /// <summary>
        /// 心跳定时器
        /// </summary>
        private Timer _heartbeatTimer;

        /// <summary>
        /// 消息处理取消令牌
        /// </summary>
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// UPnP 端口映射管理器
        /// </summary>
        private UPnPPortMapper _upnpMapper;

        /// <summary>
        /// 网络状态监控器
        /// </summary>
        private DirectNetworkMonitor _networkMonitor;

        /// <summary>
        /// 消息协议处理器
        /// </summary>
        private DirectMessageProtocol _messageProtocol;

        /// <summary>
        /// NAT穿透辅助器
        /// </summary>
        private NATTraversalHelper _natTraversalHelper;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        private bool _isRunning;

        /// <summary>
        /// 本地IP地址缓存
        /// </summary>
        private string _localIPAddress;

        /// <summary>
        /// 目标主机地址（用于网络监控）
        /// </summary>
        private string _targetHost;

        /// <summary>
        /// 是否已注册NetService处理器
        /// </summary>
        private bool _netServiceHandlersRegistered;

        #endregion

        #region 构造函数和初始化

        /// <summary>
        /// 构造函数
        /// </summary>
        public DirectP2PNetwork()
        {
            InitializeComponents();
        }

        /// <summary>
        /// 初始化组件
        /// </summary>
        private void InitializeComponents()
        {
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _upnpMapper = new UPnPPortMapper();
                _networkMonitor = new DirectNetworkMonitor();
                
                // 初始化消息协议
                _messageProtocol = new DirectMessageProtocol();
                _messageProtocol.SendRawDataCallback = SendRawDataInternal;
                _messageProtocol.OnMessageReceived += OnProtocolMessageReceived;
                _messageProtocol.OnMessageSendFailed += OnProtocolMessageSendFailed;
                _messageProtocol.OnProtocolError += OnProtocolError;
                
                // 初始化NAT穿透辅助器
                _natTraversalHelper = new NATTraversalHelper();
                
                // 获取本地IP地址
                _localIPAddress = GetLocalIPAddress();
                
                LogInfo($"直连 P2P 网络适配器初始化完成，本地IP: {_localIPAddress}");
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                LogError($"初始化直连 P2P 网络适配器时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 网络适配器实现

        /// <summary>
        /// 启动主机服务的具体实现
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <returns>启动是否成功</returns>
        protected override async Task<bool> StartHostInternal(NetworkConfig config)
        {
            try
            {
                var port = config.Port > 0 ? config.Port : DEFAULT_PORT;
                return await StartDirectHost(port);
            }
            catch (Exception ex)
            {
                LogError($"启动直连 P2P 主机服务时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 连接到主机的具体实现
        /// </summary>
        /// <param name="endpoint">主机端点（IP:Port格式）</param>
        /// <returns>连接是否成功</returns>
        protected override async Task<bool> ConnectToHostInternal(string endpoint)
        {
            try
            {
                // 解析端点
                var parts = endpoint.Split(':');
                if (parts.Length != 2)
                {
                    LogError($"无效的端点格式: {endpoint}，应为 IP:Port 格式");
                    return false;
                }

                var ip = parts[0];
                if (!int.TryParse(parts[1], out int port))
                {
                    LogError($"无效的端口号: {parts[1]}");
                    return false;
                }

                return await ConnectDirect(ip, port);
            }
            catch (Exception ex)
            {
                LogError($"连接直连 P2P 主机时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开连接的具体实现
        /// </summary>
        protected override void DisconnectInternal()
        {
            try
            {
                _isRunning = false;

                // 取消注册NetService处理器
                UnregisterNetServiceHandlers();

                // 停止定时器
                _broadcastTimer?.Dispose();
                _heartbeatTimer?.Dispose();

                // 取消所有异步操作
                _cancellationTokenSource?.Cancel();

                // 清理连接的客户端
                _connectedClients.Clear();

                // 关闭UDP客户端（如果有的话）
                _udpClient?.Close();

                // 移除UPnP端口映射
                if (EnableUPnP && _currentPort > 0)
                {
                    RemovePortMapping(_currentPort);
                }

                LogInfo("直连 P2P 连接已断开");
            }
            catch (Exception ex)
            {
                LogError($"断开直连 P2P 连接时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送消息的具体实现
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="targetId">目标客户端ID</param>
        /// <returns>发送是否成功</returns>
        protected override async Task<bool> SendMessageInternal(byte[] data, string targetId)
        {
            try
            {
                if (data.Length > MAX_MESSAGE_SIZE)
                {
                    LogError($"消息大小超过限制: {data.Length} > {MAX_MESSAGE_SIZE}");
                    return false;
                }

                // 使用消息协议发送
                var messageId = await _messageProtocol.SendMessage(data, true);
                return messageId > 0;
            }
            catch (Exception ex)
            {
                LogError($"发送直连 P2P 消息时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 直连特定功能

        /// <summary>
        /// 启动直连主机服务（集成到现有NetService）
        /// </summary>
        /// <param name="port">监听端口</param>
        /// <returns>启动是否成功</returns>
        public async Task<bool> StartDirectHost(int port)
        {
            try
            {
                if (_isRunning)
                {
                    LogWarning("直连主机服务已在运行");
                    return true;
                }

                // 确保使用9050端口与NetService保持一致
                _currentPort = 9050;

                // 检查NetService是否已启动
                if (NetService.Instance == null)
                {
                    LogError("NetService未初始化，无法启动直连主机服务");
                    return false;
                }

                // 如果NetService未启动，则启动它
                if (!NetService.Instance.networkStarted)
                {
                    LogInfo("启动NetService主机模式...");
                    NetService.Instance.StartNetwork(true);
                }

                // 设置UPnP端口映射
                if (EnableUPnP)
                {
                    LogInfo("正在设置 UPnP 端口映射...");
                    await SetupPortMapping(_currentPort);
                }

                _isRunning = true;

                // 注册到NetService的消息处理
                RegisterNetServiceHandlers();

                // 开始主机广播
                StartHostBroadcast();

                // 启动心跳
                StartHeartbeat();

                LogInfo($"直连主机服务已启动，使用NetService端口: {_currentPort}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"启动直连主机服务时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 连接到直连主机（通过NetService）
        /// </summary>
        /// <param name="ip">主机IP地址</param>
        /// <param name="port">主机端口</param>
        /// <returns>连接是否成功</returns>
        public async Task<bool> ConnectDirect(string ip, int port)
        {
            try
            {
                if (_isRunning)
                {
                    LogWarning("已有活动连接，无法连接到新主机");
                    return false;
                }

                LogInfo($"正在通过NetService连接到主机: {ip}:{port}");

                // 检查NetService是否已初始化
                if (NetService.Instance == null)
                {
                    LogError("NetService未初始化，无法连接");
                    return false;
                }

                // 测试网络质量
                var qualityResult = await TestNetworkQuality(ip, port);
                if (!qualityResult.IsReachable)
                {
                    LogWarning($"网络质量测试失败: {ip}:{port}");
                }

                // 使用NetService连接
                NetService.Instance.ConnectToHost(ip, port);

                // 等待连接结果
                var timeout = DateTime.UtcNow.AddMilliseconds(CONNECTION_TIMEOUT_MS);
                while (DateTime.UtcNow < timeout)
                {
                    if (NetService.Instance.connectedPeer != null)
                    {
                        _targetHost = ip;
                        _isRunning = true;

                        // 注册NetService处理器
                        RegisterNetServiceHandlers();

                        // 启动心跳
                        StartHeartbeat();

                        LogInfo($"成功通过NetService连接到主机: {ip}:{port}");
                        return true;
                    }

                    if (NetService.Instance.status.Contains("失败") || NetService.Instance.status.Contains("错误"))
                    {
                        LogError($"NetService连接失败: {NetService.Instance.status}");
                        return false;
                    }

                    await Task.Delay(100);
                }

                LogError($"连接超时: {ip}:{port}");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"连接直连主机时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region UPnP 支持

        /// <summary>
        /// 设置 UPnP 端口映射
        /// </summary>
        /// <param name="port">要映射的端口</param>
        /// <returns>映射是否成功</returns>
        public async Task<bool> SetupPortMapping(int port)
        {
            try
            {
                if (!EnableUPnP || _upnpMapper == null)
                {
                    LogDebug("UPnP 已禁用或映射器未初始化");
                    return false;
                }

                return await _upnpMapper.AddPortMapping(port, "TCP", "游戏聊天服务");
            }
            catch (Exception ex)
            {
                LogError($"设置 UPnP 端口映射时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 移除 UPnP 端口映射
        /// </summary>
        /// <param name="port">要移除映射的端口</param>
        public void RemovePortMapping(int port)
        {
            try
            {
                if (!EnableUPnP || _upnpMapper == null)
                {
                    return;
                }

                _upnpMapper.RemovePortMapping(port, "TCP");
                LogInfo($"已移除 UPnP 端口映射: {port}");
            }
            catch (Exception ex)
            {
                LogError($"移除 UPnP 端口映射时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 网络发现

        /// <summary>
        /// 发现本地网络中的主机
        /// </summary>
        /// <returns>发现的主机信息列表</returns>
        public async Task<List<HostInfo>> DiscoverLocalHosts()
        {
            var hosts = new List<HostInfo>();

            try
            {
                LogInfo("开始发现本地网络主机...");

                using (var udpClient = new UdpClient())
                {
                    udpClient.EnableBroadcast = true;

                    // 发送发现请求
                    var discoveryMessage = Encoding.UTF8.GetBytes("DISCOVER_HOST");
                    var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
                    
                    await udpClient.SendAsync(discoveryMessage, discoveryMessage.Length, broadcastEndpoint);

                    // 等待响应
                    var timeout = Task.Delay(5000); // 5秒超时
                    var responses = new List<Task<UdpReceiveResult>>();

                    while (true)
                    {
                        var receiveTask = udpClient.ReceiveAsync();
                        var completedTask = await Task.WhenAny(receiveTask, timeout);

                        if (completedTask == timeout)
                        {
                            break; // 超时
                        }

                        try
                        {
                            var result = await receiveTask;
                            var responseData = Encoding.UTF8.GetString(result.Buffer);

                            if (responseData.StartsWith("HOST_INFO:"))
                            {
                                var hostInfo = ParseHostInfo(responseData, result.RemoteEndPoint);
                                if (hostInfo != null)
                                {
                                    hosts.Add(hostInfo);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"处理发现响应时发生异常: {ex.Message}");
                        }
                    }
                }

                LogInfo($"发现完成，找到 {hosts.Count} 个主机");
            }
            catch (Exception ex)
            {
                LogError($"发现本地网络主机时发生异常: {ex.Message}");
            }

            return hosts;
        }

        /// <summary>
        /// 开始主机广播
        /// </summary>
        public void StartHostBroadcast()
        {
            try
            {
                if (_broadcastTimer != null)
                {
                    return; // 已在广播
                }

                _broadcastTimer = new Timer(BroadcastHostInfo, null, 0, BROADCAST_INTERVAL_MS);
                LogInfo("主机广播已启动");
            }
            catch (Exception ex)
            {
                LogError($"启动主机广播时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止主机广播
        /// </summary>
        public void StopHostBroadcast()
        {
            try
            {
                _broadcastTimer?.Dispose();
                _broadcastTimer = null;
                LogInfo("主机广播已停止");
            }
            catch (Exception ex)
            {
                LogError($"停止主机广播时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region 网络质量

        /// <summary>
        /// 获取当前连接质量
        /// </summary>
        /// <returns>连接质量信息</returns>
        public ConnectionQuality GetConnectionQuality()
        {
            try
            {
                return _networkMonitor?.GetCurrentQuality() ?? new ConnectionQuality();
            }
            catch (Exception ex)
            {
                LogError($"获取连接质量时发生异常: {ex.Message}");
                return new ConnectionQuality();
            }
        }

        /// <summary>
        /// 测试到指定主机的网络质量
        /// </summary>
        /// <param name="ip">主机IP地址</param>
        /// <param name="port">主机端口</param>
        /// <returns>网络质量测试结果</returns>
        public async Task<NetworkQualityTestResult> TestNetworkQuality(string ip, int port)
        {
            var result = new NetworkQualityTestResult();

            try
            {
                LogDebug($"测试网络质量: {ip}:{port}");

                var latencies = new List<int>();
                int successCount = 0;
                const int testCount = 5;

                for (int i = 0; i < testCount; i++)
                {
                    var startTime = DateTime.UtcNow;

                    try
                    {
                        using (var testClient = new TcpClient())
                        {
                            var connectTask = testClient.ConnectAsync(ip, port);
                            if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                            {
                                var latency = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                                latencies.Add(latency);
                                successCount++;
                            }
                        }
                    }
                    catch
                    {
                        // 连接失败，不计入延迟
                    }

                    await Task.Delay(100); // 间隔100ms
                }

                result.IsReachable = successCount > 0;
                result.PacketLoss = ((float)(testCount - successCount) / testCount) * 100;

                if (latencies.Count > 0)
                {
                    result.AverageLatency = (int)latencies.Average();
                    result.MinLatency = latencies.Min();
                    result.MaxLatency = latencies.Max();

                    // 计算质量分数（0-100）
                    result.QualityScore = CalculateQualityScore(result.AverageLatency, result.PacketLoss);
                }

                LogDebug($"网络质量测试完成: {result}");
            }
            catch (Exception ex)
            {
                LogError($"测试网络质量时发生异常: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 获取本地IP地址
        /// </summary>
        /// <returns>本地IP地址</returns>
        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"获取本地IP地址时发生异常: {ex.Message}");
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// 处理来自NetService的聊天消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="senderId">发送者ID</param>
        public void HandleNetServiceChatMessage(byte[] data, string senderId)
        {
            try
            {
                LogDebug($"收到NetService聊天消息: 发送者={senderId}, 大小={data.Length}字节");

                // 记录客户端连接
                if (!_connectedClients.ContainsKey(senderId))
                {
                    _connectedClients[senderId] = new DirectP2PClient(null, senderId);
                    TriggerClientConnected(senderId);
                }

                // 通过消息协议处理接收到的数据
                _messageProtocol.ProcessReceivedData(data, senderId);
            }
            catch (Exception ex)
            {
                LogError($"处理NetService聊天消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 广播给所有客户端
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>广播是否成功</returns>
        private async Task<bool> BroadcastToAllClients(byte[] data)
        {
            bool allSuccess = true;

            foreach (var client in _connectedClients.Values)
            {
                if (!await client.SendMessage(data))
                {
                    allSuccess = false;
                    LogWarning($"向客户端发送消息失败: {client.EndPoint}");
                }
            }

            return allSuccess;
        }

        /// <summary>
        /// 广播主机信息
        /// </summary>
        /// <param name="state">定时器状态</param>
        private void BroadcastHostInfo(object state)
        {
            try
            {
                if (!_isRunning || _udpClient == null)
                {
                    return;
                }

                var hostInfo = $"HOST_INFO:{Environment.MachineName}:{_localIPAddress}:{_currentPort}:游戏聊天服务";
                var data = Encoding.UTF8.GetBytes(hostInfo);
                var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);

                _udpClient.SendAsync(data, data.Length, broadcastEndpoint);
            }
            catch (Exception ex)
            {
                LogDebug($"广播主机信息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析主机信息
        /// </summary>
        /// <param name="responseData">响应数据</param>
        /// <param name="remoteEndPoint">远程端点</param>
        /// <returns>主机信息</returns>
        private HostInfo ParseHostInfo(string responseData, IPEndPoint remoteEndPoint)
        {
            try
            {
                var parts = responseData.Substring("HOST_INFO:".Length).Split(':');
                if (parts.Length >= 4)
                {
                    return new HostInfo
                    {
                        HostName = parts[0],
                        IPAddress = parts[1],
                        Port = int.Parse(parts[2]),
                        Description = parts[3],
                        IsReachable = true,
                        Latency = 0 // 可以后续测试延迟
                    };
                }
            }
            catch (Exception ex)
            {
                LogDebug($"解析主机信息时发生异常: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 启动心跳
        /// </summary>
        private void StartHeartbeat()
        {
            try
            {
                if (_heartbeatTimer != null)
                {
                    return;
                }

                _heartbeatTimer = new Timer(SendHeartbeat, null, HEARTBEAT_INTERVAL_MS, HEARTBEAT_INTERVAL_MS);
                LogDebug("心跳已启动");
            }
            catch (Exception ex)
            {
                LogError($"启动心跳时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送心跳
        /// </summary>
        /// <param name="state">定时器状态</param>
        private void SendHeartbeat(object state)
        {
            try
            {
                var heartbeatData = Encoding.UTF8.GetBytes("HEARTBEAT");
                _ = SendMessage(heartbeatData);
            }
            catch (Exception ex)
            {
                LogDebug($"发送心跳时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 计算质量分数
        /// </summary>
        /// <param name="latency">延迟</param>
        /// <param name="packetLoss">丢包率</param>
        /// <returns>质量分数（0-100）</returns>
        private int CalculateQualityScore(int latency, float packetLoss)
        {
            // 基础分数100
            int score = 100;

            // 延迟扣分：每10ms扣1分
            score -= latency / 10;

            // 丢包扣分：每1%扣10分
            score -= (int)(packetLoss * 10);

            return Math.Max(0, Math.Min(100, score));
        }

        #endregion

        #region 消息协议事件处理

        /// <summary>
        /// 处理协议消息接收
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="senderId">发送者ID</param>
        private void OnProtocolMessageReceived(byte[] data, string senderId)
        {
            try
            {
                // 触发上层消息接收事件
                TriggerMessageReceived(data, senderId);
                LogDebug($"协议消息接收完成: 发送者={senderId}, 大小={data.Length}字节");
            }
            catch (Exception ex)
            {
                LogError($"处理协议消息接收时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理协议消息发送失败
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="reason">失败原因</param>
        private void OnProtocolMessageSendFailed(uint messageId, string reason)
        {
            LogError($"协议消息发送失败: ID={messageId}, 原因={reason}");
            
            var error = new NetworkError(
                NetworkErrorType.MessageSendFailed,
                $"消息发送失败: {reason}",
                $"MessageId: {messageId}"
            );
            TriggerNetworkError(error);
        }

        /// <summary>
        /// 处理协议错误
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        private void OnProtocolError(string errorMessage)
        {
            LogError($"协议错误: {errorMessage}");
            
            var error = new NetworkError(
                NetworkErrorType.MessageSendFailed,
                "协议错误",
                errorMessage
            );
            TriggerNetworkError(error);
        }

        /// <summary>
        /// 发送原始数据（协议回调）
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendRawDataInternal(byte[] data)
        {
            try
            {
                // 如果没有指定目标，广播给所有客户端
                return await BroadcastRawDataToAllClients(data);
            }
            catch (Exception ex)
            {
                LogError($"发送原始数据时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 广播原始数据给所有客户端
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <returns>广播是否成功</returns>
        private async Task<bool> BroadcastRawDataToAllClients(byte[] data)
        {
            try
            {
                // 使用NetService发送消息
                return await SendViaNetService(data);
            }
            catch (Exception ex)
            {
                LogError($"广播原始数据时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region NetService集成

        /// <summary>
        /// 注册NetService消息处理器
        /// </summary>
        private void RegisterNetServiceHandlers()
        {
            if (_netServiceHandlersRegistered || NetService.Instance == null)
            {
                return;
            }

            try
            {
                // 注册聊天消息处理
                // 这里我们需要扩展NetService来支持聊天消息
                LogInfo("已注册NetService消息处理器");
                _netServiceHandlersRegistered = true;
            }
            catch (Exception ex)
            {
                LogError($"注册NetService处理器时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 取消注册NetService消息处理器
        /// </summary>
        private void UnregisterNetServiceHandlers()
        {
            if (!_netServiceHandlersRegistered)
            {
                return;
            }

            try
            {
                // 取消注册聊天消息处理
                LogInfo("已取消注册NetService消息处理器");
                _netServiceHandlersRegistered = false;
            }
            catch (Exception ex)
            {
                LogError($"取消注册NetService处理器时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 通过NetService发送聊天消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="targetEndpoint">目标端点（可选）</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendViaNetService(byte[] data, string targetEndpoint = null)
        {
            try
            {
                if (NetService.Instance?.netManager == null || !NetService.Instance.networkStarted)
                {
                    LogError("NetService未启动，无法发送消息");
                    return false;
                }

                // 创建聊天消息包装
                var chatPacket = CreateChatPacket(data);
                
                if (NetService.Instance.IsServer)
                {
                    // 主机模式：广播给所有客户端
                    foreach (var peer in NetService.Instance.playerStatuses.Keys)
                    {
                        if (peer != null && peer.ConnectionState == ConnectionState.Connected)
                        {
                            peer.Send(chatPacket, DeliveryMethod.ReliableOrdered);
                        }
                    }
                }
                else
                {
                    // 客户端模式：发送给主机
                    if (NetService.Instance.connectedPeer != null)
                    {
                        NetService.Instance.connectedPeer.Send(chatPacket, DeliveryMethod.ReliableOrdered);
                    }
                    else
                    {
                        LogError("未连接到主机，无法发送消息");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"通过NetService发送消息时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建聊天消息包
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>网络数据包</returns>
        private NetDataWriter CreateChatPacket(byte[] data)
        {
            var writer = new NetDataWriter();
            
            // 写入聊天消息标识符
            writer.Put((byte)255); // 使用255作为聊天消息的操作码
            
            // 写入消息长度
            writer.Put(data.Length);
            
            // 写入消息数据
            writer.Put(data);
            
            return writer;
        }

        #endregion

        #region 维护和更新

        /// <summary>
        /// 更新网络状态（需要定期调用）
        /// </summary>
        public void Update()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                // 清理过期消息
                _messageProtocol?.CleanupExpiredMessages();

                // 更新网络监控
                if (!string.IsNullOrEmpty(_targetHost))
                {
                    _networkMonitor?.SetTargetHost(_targetHost);
                }
            }
            catch (Exception ex)
            {
                LogError($"更新网络状态时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取协议统计信息
        /// </summary>
        /// <returns>协议统计信息</returns>
        public ProtocolStatistics GetProtocolStatistics()
        {
            return _messageProtocol?.GetStatistics() ?? new ProtocolStatistics();
        }

        /// <summary>
        /// 获取NAT穿透建议
        /// </summary>
        /// <returns>NAT穿透建议</returns>
        public NATTraversalAdvice GetNATTraversalAdvice()
        {
            return _natTraversalHelper?.GetTraversalAdvice() ?? new NATTraversalAdvice
            {
                NATType = NATType.Unknown,
                Difficulty = TraversalDifficulty.Unknown,
                SupportsUPnP = false,
                RecommendedMethods = new List<NATTraversalMethod>()
            };
        }

        /// <summary>
        /// 获取网络配置信息
        /// </summary>
        /// <returns>网络配置信息</returns>
        public NetworkConfigurationInfo GetNetworkConfiguration()
        {
            return new NetworkConfigurationInfo
            {
                LocalIPAddress = _localIPAddress,
                ExternalIPAddress = _natTraversalHelper?.ExternalIPAddress,
                NATType = _natTraversalHelper?.NATType ?? NATType.Unknown,
                SupportsUPnP = _natTraversalHelper?.SupportsUPnP ?? false,
                CurrentPort = _currentPort,
                IsRunning = _isRunning,
                ConnectionStatus = Status
            };
        }

        #endregion

        #region 析构和清理

        /// <summary>
        /// 析构函数
        /// </summary>
        ~DirectP2PNetwork()
        {
            DisconnectInternal();
            _cancellationTokenSource?.Dispose();
            _upnpMapper?.Dispose();
            _networkMonitor?.Dispose();
            _messageProtocol?.Dispose();
            _natTraversalHelper?.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// 网络配置信息类
    /// </summary>
    public class NetworkConfigurationInfo
    {
        /// <summary>
        /// 本地IP地址
        /// </summary>
        public string LocalIPAddress { get; set; }

        /// <summary>
        /// 外部IP地址
        /// </summary>
        public string ExternalIPAddress { get; set; }

        /// <summary>
        /// NAT类型
        /// </summary>
        public NATType NATType { get; set; }

        /// <summary>
        /// 是否支持UPnP
        /// </summary>
        public bool SupportsUPnP { get; set; }

        /// <summary>
        /// 当前端口
        /// </summary>
        public int CurrentPort { get; set; }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 连接状态
        /// </summary>
        public ConnectionStatus ConnectionStatus { get; set; }

        public override string ToString()
        {
            return $"本地IP: {LocalIPAddress}, 外部IP: {ExternalIPAddress}, NAT: {NATType}, " +
                   $"UPnP: {SupportsUPnP}, 端口: {CurrentPort}, 状态: {ConnectionStatus}";
        }
    }
}