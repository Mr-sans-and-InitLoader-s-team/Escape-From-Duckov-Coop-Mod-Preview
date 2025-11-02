using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using System.Text;
using System.Linq;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// NAT 穿透辅助类
    /// 提供各种 NAT 穿透技术的实现
    /// </summary>
    public class NATTraversalHelper : IDisposable
    {
        #region 常量定义

        /// <summary>
        /// STUN 服务器列表
        /// </summary>
        private static readonly string[] STUN_SERVERS = {
            "stun.l.google.com:19302",
            "stun1.l.google.com:19302",
            "stun2.l.google.com:19302",
            "stun3.l.google.com:19302",
            "stun4.l.google.com:19302"
        };

        /// <summary>
        /// 打洞尝试超时时间（毫秒）
        /// </summary>
        private const int HOLE_PUNCHING_TIMEOUT_MS = 10000;

        /// <summary>
        /// 端口范围开始
        /// </summary>
        private const int PORT_RANGE_START = 49152;

        /// <summary>
        /// 端口范围结束
        /// </summary>
        private const int PORT_RANGE_END = 65535;

        #endregion

        #region 字段和属性

        /// <summary>
        /// 本地IP地址
        /// </summary>
        public string LocalIPAddress { get; private set; }

        /// <summary>
        /// 外部IP地址
        /// </summary>
        public string ExternalIPAddress { get; private set; }

        /// <summary>
        /// NAT类型
        /// </summary>
        public NATType NATType { get; private set; }

        /// <summary>
        /// 是否支持UPnP
        /// </summary>
        public bool SupportsUPnP { get; private set; }

        /// <summary>
        /// UPnP端口映射器
        /// </summary>
        private UPnPPortMapper _upnpMapper;

        /// <summary>
        /// 是否已释放
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 构造函数和初始化

        /// <summary>
        /// 构造函数
        /// </summary>
        public NATTraversalHelper()
        {
            InitializeHelper();
        }

        /// <summary>
        /// 初始化辅助器
        /// </summary>
        private void InitializeHelper()
        {
            try
            {
                LocalIPAddress = GetLocalIPAddress();
                _upnpMapper = new UPnPPortMapper();
                
                LogInfo($"NAT穿透辅助器初始化完成，本地IP: {LocalIPAddress}");
                
                // 异步检测NAT类型和外部IP
                _ = Task.Run(DetectNATConfiguration);
            }
            catch (Exception ex)
            {
                LogError($"初始化NAT穿透辅助器时发生异常: {ex.Message}");
            }
        }

        #endregion

        #region NAT 检测

        /// <summary>
        /// 检测NAT配置
        /// </summary>
        private async Task DetectNATConfiguration()
        {
            try
            {
                LogInfo("开始检测NAT配置...");

                // 检测外部IP地址
                ExternalIPAddress = await DetectExternalIP();
                
                // 检测NAT类型
                NATType = await DetectNATType();
                
                // 检测UPnP支持
                SupportsUPnP = _upnpMapper?.IsUPnPAvailable ?? false;

                LogInfo($"NAT配置检测完成: 外部IP={ExternalIPAddress}, NAT类型={NATType}, UPnP支持={SupportsUPnP}");
            }
            catch (Exception ex)
            {
                LogError($"检测NAT配置时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检测外部IP地址
        /// </summary>
        /// <returns>外部IP地址</returns>
        private async Task<string> DetectExternalIP()
        {
            try
            {
                // 尝试通过STUN服务器获取外部IP
                foreach (var stunServer in STUN_SERVERS)
                {
                    try
                    {
                        var externalIP = await GetExternalIPFromSTUN(stunServer);
                        if (!string.IsNullOrEmpty(externalIP))
                        {
                            LogDebug($"通过STUN服务器获取外部IP: {stunServer} -> {externalIP}");
                            return externalIP;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"STUN服务器 {stunServer} 失败: {ex.Message}");
                    }
                }

                // 如果STUN失败，尝试通过HTTP服务获取
                return await GetExternalIPFromHTTP();
            }
            catch (Exception ex)
            {
                LogError($"检测外部IP时发生异常: {ex.Message}");
                return LocalIPAddress; // 降级到本地IP
            }
        }

        /// <summary>
        /// 通过STUN服务器获取外部IP
        /// </summary>
        /// <param name="stunServer">STUN服务器地址</param>
        /// <returns>外部IP地址</returns>
        private async Task<string> GetExternalIPFromSTUN(string stunServer)
        {
            try
            {
                var parts = stunServer.Split(':');
                var host = parts[0];
                var port = int.Parse(parts[1]);

                using (var udpClient = new UdpClient())
                {
                    // 构造STUN绑定请求
                    var stunRequest = CreateSTUNBindingRequest();
                    
                    // 发送请求
                    await udpClient.SendAsync(stunRequest, stunRequest.Length, host, port);
                    
                    // 接收响应
                    var receiveTask = udpClient.ReceiveAsync();
                    var timeoutTask = Task.Delay(5000);
                    
                    var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                    if (completedTask == receiveTask)
                    {
                        var result = await receiveTask;
                        return ParseSTUNResponse(result.Buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"STUN请求失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 通过HTTP服务获取外部IP
        /// </summary>
        /// <returns>外部IP地址</returns>
        private async Task<string> GetExternalIPFromHTTP()
        {
            try
            {
                var httpServices = new[]
                {
                    "https://api.ipify.org",
                    "https://icanhazip.com",
                    "https://ipecho.net/plain"
                };

                foreach (var service in httpServices)
                {
                    try
                    {
                        using (var client = new WebClient())
                        {
                            var downloadTask = client.DownloadStringTaskAsync(service);
                            var timeoutTask = Task.Delay(5000);
                            
                            var completedTask = await Task.WhenAny(downloadTask, timeoutTask);
                            if (completedTask == downloadTask)
                            {
                                var result = await downloadTask;
                                var ip = result.Trim();
                                
                                if (IPAddress.TryParse(ip, out _))
                                {
                                    LogDebug($"通过HTTP服务获取外部IP: {service} -> {ip}");
                                    return ip;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"HTTP服务 {service} 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"通过HTTP获取外部IP时发生异常: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 检测NAT类型
        /// </summary>
        /// <returns>NAT类型</returns>
        private async Task<NATType> DetectNATType()
        {
            try
            {
                // 简化的NAT类型检测
                if (LocalIPAddress == ExternalIPAddress)
                {
                    return NATType.None; // 没有NAT
                }

                if (SupportsUPnP)
                {
                    return NATType.FullCone; // 支持UPnP通常表示较宽松的NAT
                }

                // 尝试端口预测
                var predictable = await TestPortPredictability();
                if (predictable)
                {
                    return NATType.RestrictedCone;
                }
                else
                {
                    return NATType.Symmetric; // 最严格的NAT类型
                }
            }
            catch (Exception ex)
            {
                LogError($"检测NAT类型时发生异常: {ex.Message}");
                return NATType.Unknown;
            }
        }

        /// <summary>
        /// 测试端口可预测性
        /// </summary>
        /// <returns>端口是否可预测</returns>
        private async Task<bool> TestPortPredictability()
        {
            try
            {
                var ports = new List<int>();
                
                // 创建多个UDP套接字并记录分配的端口
                for (int i = 0; i < 3; i++)
                {
                    using (var udpClient = new UdpClient(0))
                    {
                        var localEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;
                        ports.Add(localEndPoint.Port);
                    }
                    
                    await Task.Delay(100);
                }

                // 检查端口是否连续或有规律
                if (ports.Count >= 2)
                {
                    var differences = new List<int>();
                    for (int i = 1; i < ports.Count; i++)
                    {
                        differences.Add(Math.Abs(ports[i] - ports[i - 1]));
                    }

                    // 如果端口差异较小且规律，认为可预测
                    var avgDifference = differences.Average();
                    return avgDifference < 100;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogDebug($"测试端口可预测性时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region NAT 穿透

        /// <summary>
        /// 尝试NAT穿透
        /// </summary>
        /// <param name="targetIP">目标IP地址</param>
        /// <param name="targetPort">目标端口</param>
        /// <param name="localPort">本地端口</param>
        /// <returns>穿透结果</returns>
        public async Task<NATTraversalResult> AttemptNATTraversal(string targetIP, int targetPort, int localPort)
        {
            var result = new NATTraversalResult
            {
                Success = false,
                Method = NATTraversalMethod.None,
                LocalPort = localPort,
                TargetIP = targetIP,
                TargetPort = targetPort
            };

            try
            {
                LogInfo($"开始NAT穿透尝试: 目标={targetIP}:{targetPort}, 本地端口={localPort}");

                // 方法1: UPnP端口映射
                if (SupportsUPnP)
                {
                    LogInfo("尝试UPnP端口映射...");
                    if (await AttemptUPnPTraversal(localPort))
                    {
                        result.Success = true;
                        result.Method = NATTraversalMethod.UPnP;
                        result.MappedPort = localPort;
                        LogInfo("UPnP穿透成功");
                        return result;
                    }
                }

                // 方法2: UDP打洞
                LogInfo("尝试UDP打洞...");
                var holePunchingResult = await AttemptUDPHolePunching(targetIP, targetPort, localPort);
                if (holePunchingResult.Success)
                {
                    result.Success = true;
                    result.Method = NATTraversalMethod.UDPHolePunching;
                    result.MappedPort = holePunchingResult.LocalPort;
                    LogInfo("UDP打洞成功");
                    return result;
                }

                // 方法3: 端口预测
                if (NATType == NATType.RestrictedCone || NATType == NATType.FullCone)
                {
                    LogInfo("尝试端口预测...");
                    var portPredictionResult = await AttemptPortPrediction(targetIP, targetPort, localPort);
                    if (portPredictionResult.Success)
                    {
                        result.Success = true;
                        result.Method = NATTraversalMethod.PortPrediction;
                        result.MappedPort = portPredictionResult.LocalPort;
                        LogInfo("端口预测成功");
                        return result;
                    }
                }

                LogWarning("所有NAT穿透方法都失败了");
            }
            catch (Exception ex)
            {
                LogError($"NAT穿透尝试时发生异常: {ex.Message}");
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 尝试UPnP穿透
        /// </summary>
        /// <param name="port">端口</param>
        /// <returns>是否成功</returns>
        private async Task<bool> AttemptUPnPTraversal(int port)
        {
            try
            {
                if (_upnpMapper == null || !SupportsUPnP)
                {
                    return false;
                }

                return await _upnpMapper.AddPortMapping(port, "TCP", "游戏聊天NAT穿透");
            }
            catch (Exception ex)
            {
                LogError($"UPnP穿透失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 尝试UDP打洞
        /// </summary>
        /// <param name="targetIP">目标IP</param>
        /// <param name="targetPort">目标端口</param>
        /// <param name="localPort">本地端口</param>
        /// <returns>打洞结果</returns>
        private async Task<NATTraversalResult> AttemptUDPHolePunching(string targetIP, int targetPort, int localPort)
        {
            var result = new NATTraversalResult { Success = false };

            try
            {
                using (var udpClient = new UdpClient(localPort))
                {
                    var targetEndPoint = new IPEndPoint(IPAddress.Parse(targetIP), targetPort);
                    var punchData = Encoding.UTF8.GetBytes("PUNCH");

                    // 发送多个打洞包
                    for (int i = 0; i < 10; i++)
                    {
                        await udpClient.SendAsync(punchData, punchData.Length, targetEndPoint);
                        await Task.Delay(100);
                    }

                    // 尝试接收响应
                    var receiveTask = udpClient.ReceiveAsync();
                    var timeoutTask = Task.Delay(HOLE_PUNCHING_TIMEOUT_MS);

                    var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                    if (completedTask == receiveTask)
                    {
                        var response = await receiveTask;
                        var responseText = Encoding.UTF8.GetString(response.Buffer);
                        
                        if (responseText == "PUNCH_ACK")
                        {
                            result.Success = true;
                            result.LocalPort = localPort;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"UDP打洞失败: {ex.Message}");
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 尝试端口预测
        /// </summary>
        /// <param name="targetIP">目标IP</param>
        /// <param name="targetPort">目标端口</param>
        /// <param name="basePort">基础端口</param>
        /// <returns>预测结果</returns>
        private async Task<NATTraversalResult> AttemptPortPrediction(string targetIP, int targetPort, int basePort)
        {
            var result = new NATTraversalResult { Success = false };

            try
            {
                // 尝试预测可能的端口范围
                var portRange = GeneratePortPredictions(basePort);

                foreach (var predictedPort in portRange)
                {
                    try
                    {
                        using (var udpClient = new UdpClient(predictedPort))
                        {
                            var targetEndPoint = new IPEndPoint(IPAddress.Parse(targetIP), targetPort);
                            var testData = Encoding.UTF8.GetBytes("PORT_TEST");

                            await udpClient.SendAsync(testData, testData.Length, targetEndPoint);

                            // 短暂等待响应
                            var receiveTask = udpClient.ReceiveAsync();
                            var timeoutTask = Task.Delay(1000);

                            var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                            if (completedTask == receiveTask)
                            {
                                result.Success = true;
                                result.LocalPort = predictedPort;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // 端口可能被占用，继续尝试下一个
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"端口预测失败: {ex.Message}");
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 生成端口预测列表
        /// </summary>
        /// <param name="basePort">基础端口</param>
        /// <returns>预测端口列表</returns>
        private List<int> GeneratePortPredictions(int basePort)
        {
            var predictions = new List<int>();

            // 添加基础端口
            predictions.Add(basePort);

            // 添加连续端口
            for (int i = 1; i <= 10; i++)
            {
                if (basePort + i <= PORT_RANGE_END)
                    predictions.Add(basePort + i);
                if (basePort - i >= PORT_RANGE_START)
                    predictions.Add(basePort - i);
            }

            // 添加一些常见的端口偏移
            var commonOffsets = new[] { 100, 200, 500, 1000 };
            foreach (var offset in commonOffsets)
            {
                if (basePort + offset <= PORT_RANGE_END)
                    predictions.Add(basePort + offset);
                if (basePort - offset >= PORT_RANGE_START)
                    predictions.Add(basePort - offset);
            }

            return predictions.Distinct().Where(p => p >= PORT_RANGE_START && p <= PORT_RANGE_END).ToList();
        }

        #endregion

        #region STUN 协议

        /// <summary>
        /// 创建STUN绑定请求
        /// </summary>
        /// <returns>STUN请求数据</returns>
        private byte[] CreateSTUNBindingRequest()
        {
            var request = new byte[20];
            
            // STUN消息类型: Binding Request (0x0001)
            request[0] = 0x00;
            request[1] = 0x01;
            
            // 消息长度: 0 (没有属性)
            request[2] = 0x00;
            request[3] = 0x00;
            
            // Magic Cookie: 0x2112A442
            request[4] = 0x21;
            request[5] = 0x12;
            request[6] = 0xA4;
            request[7] = 0x42;
            
            // Transaction ID: 12字节随机数
            var random = new System.Random();
            for (int i = 8; i < 20; i++)
            {
                request[i] = (byte)random.Next(256);
            }
            
            return request;
        }

        /// <summary>
        /// 解析STUN响应
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <returns>外部IP地址</returns>
        private string ParseSTUNResponse(byte[] response)
        {
            try
            {
                if (response.Length < 20)
                    return null;

                // 检查是否为STUN Binding Success Response (0x0101)
                if (response[0] != 0x01 || response[1] != 0x01)
                    return null;

                // 解析属性
                int offset = 20;
                while (offset < response.Length)
                {
                    if (offset + 4 > response.Length)
                        break;

                    var attrType = (response[offset] << 8) | response[offset + 1];
                    var attrLength = (response[offset + 2] << 8) | response[offset + 3];
                    
                    offset += 4;

                    if (offset + attrLength > response.Length)
                        break;

                    // MAPPED-ADDRESS (0x0001) 或 XOR-MAPPED-ADDRESS (0x0020)
                    if (attrType == 0x0001 || attrType == 0x0020)
                    {
                        if (attrLength >= 8)
                        {
                            var family = response[offset + 1];
                            if (family == 0x01) // IPv4
                            {
                                var port = (response[offset + 2] << 8) | response[offset + 3];
                                var ip = new IPAddress(new byte[] 
                                { 
                                    response[offset + 4], 
                                    response[offset + 5], 
                                    response[offset + 6], 
                                    response[offset + 7] 
                                });

                                // 如果是XOR-MAPPED-ADDRESS，需要进行XOR操作
                                if (attrType == 0x0020)
                                {
                                    var magicCookie = new byte[] { 0x21, 0x12, 0xA4, 0x42 };
                                    var ipBytes = ip.GetAddressBytes();
                                    for (int i = 0; i < 4; i++)
                                    {
                                        ipBytes[i] ^= magicCookie[i];
                                    }
                                    ip = new IPAddress(ipBytes);
                                }

                                return ip.ToString();
                            }
                        }
                    }

                    offset += attrLength;
                    
                    // 属性长度需要4字节对齐
                    if (attrLength % 4 != 0)
                    {
                        offset += 4 - (attrLength % 4);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"解析STUN响应时发生异常: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region 辅助方法

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
        /// 获取NAT穿透建议
        /// </summary>
        /// <returns>建议信息</returns>
        public NATTraversalAdvice GetTraversalAdvice()
        {
            var advice = new NATTraversalAdvice
            {
                NATType = NATType,
                SupportsUPnP = SupportsUPnP,
                RecommendedMethods = new List<NATTraversalMethod>()
            };

            switch (NATType)
            {
                case NATType.None:
                    advice.Difficulty = TraversalDifficulty.Easy;
                    advice.RecommendedMethods.Add(NATTraversalMethod.DirectConnection);
                    break;

                case NATType.FullCone:
                    advice.Difficulty = TraversalDifficulty.Easy;
                    if (SupportsUPnP)
                        advice.RecommendedMethods.Add(NATTraversalMethod.UPnP);
                    advice.RecommendedMethods.Add(NATTraversalMethod.UDPHolePunching);
                    break;

                case NATType.RestrictedCone:
                    advice.Difficulty = TraversalDifficulty.Medium;
                    if (SupportsUPnP)
                        advice.RecommendedMethods.Add(NATTraversalMethod.UPnP);
                    advice.RecommendedMethods.Add(NATTraversalMethod.UDPHolePunching);
                    advice.RecommendedMethods.Add(NATTraversalMethod.PortPrediction);
                    break;

                case NATType.Symmetric:
                    advice.Difficulty = TraversalDifficulty.Hard;
                    if (SupportsUPnP)
                        advice.RecommendedMethods.Add(NATTraversalMethod.UPnP);
                    advice.RecommendedMethods.Add(NATTraversalMethod.Relay);
                    break;

                default:
                    advice.Difficulty = TraversalDifficulty.Unknown;
                    advice.RecommendedMethods.Add(NATTraversalMethod.UPnP);
                    advice.RecommendedMethods.Add(NATTraversalMethod.UDPHolePunching);
                    break;
            }

            return advice;
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[NATTraversalHelper] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[NATTraversalHelper] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[NATTraversalHelper] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[NATTraversalHelper][DEBUG] {message}");
        }

        #endregion

        #region IDisposable 实现

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
                    _upnpMapper?.Dispose();
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~NATTraversalHelper()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// NAT类型枚举
    /// </summary>
    public enum NATType
    {
        /// <summary>
        /// 未知
        /// </summary>
        Unknown,

        /// <summary>
        /// 无NAT
        /// </summary>
        None,

        /// <summary>
        /// 完全锥形NAT
        /// </summary>
        FullCone,

        /// <summary>
        /// 受限锥形NAT
        /// </summary>
        RestrictedCone,

        /// <summary>
        /// 端口受限锥形NAT
        /// </summary>
        PortRestrictedCone,

        /// <summary>
        /// 对称NAT
        /// </summary>
        Symmetric
    }

    /// <summary>
    /// NAT穿透方法枚举
    /// </summary>
    public enum NATTraversalMethod
    {
        /// <summary>
        /// 无方法
        /// </summary>
        None,

        /// <summary>
        /// 直接连接
        /// </summary>
        DirectConnection,

        /// <summary>
        /// UPnP端口映射
        /// </summary>
        UPnP,

        /// <summary>
        /// UDP打洞
        /// </summary>
        UDPHolePunching,

        /// <summary>
        /// 端口预测
        /// </summary>
        PortPrediction,

        /// <summary>
        /// 中继服务器
        /// </summary>
        Relay
    }

    /// <summary>
    /// 穿透难度枚举
    /// </summary>
    public enum TraversalDifficulty
    {
        /// <summary>
        /// 未知
        /// </summary>
        Unknown,

        /// <summary>
        /// 简单
        /// </summary>
        Easy,

        /// <summary>
        /// 中等
        /// </summary>
        Medium,

        /// <summary>
        /// 困难
        /// </summary>
        Hard,

        /// <summary>
        /// 不可能
        /// </summary>
        Impossible
    }

    /// <summary>
    /// NAT穿透结果类
    /// </summary>
    public class NATTraversalResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 使用的方法
        /// </summary>
        public NATTraversalMethod Method { get; set; }

        /// <summary>
        /// 本地端口
        /// </summary>
        public int LocalPort { get; set; }

        /// <summary>
        /// 映射的端口
        /// </summary>
        public int MappedPort { get; set; }

        /// <summary>
        /// 目标IP
        /// </summary>
        public string TargetIP { get; set; }

        /// <summary>
        /// 目标端口
        /// </summary>
        public int TargetPort { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        public override string ToString()
        {
            if (Success)
            {
                return $"成功 - 方法: {Method}, 本地端口: {LocalPort}, 映射端口: {MappedPort}";
            }
            else
            {
                return $"失败 - 错误: {ErrorMessage}";
            }
        }
    }

    /// <summary>
    /// NAT穿透建议类
    /// </summary>
    public class NATTraversalAdvice
    {
        /// <summary>
        /// NAT类型
        /// </summary>
        public NATType NATType { get; set; }

        /// <summary>
        /// 是否支持UPnP
        /// </summary>
        public bool SupportsUPnP { get; set; }

        /// <summary>
        /// 穿透难度
        /// </summary>
        public TraversalDifficulty Difficulty { get; set; }

        /// <summary>
        /// 推荐的方法
        /// </summary>
        public List<NATTraversalMethod> RecommendedMethods { get; set; }

        public override string ToString()
        {
            var methods = string.Join(", ", RecommendedMethods);
            return $"NAT类型: {NATType}, 难度: {Difficulty}, UPnP: {SupportsUPnP}, 推荐方法: {methods}";
        }
    }
}