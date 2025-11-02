using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using UnityEngine;
using System.Collections.Generic;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// UPnP 端口映射管理器
    /// 提供自动端口映射功能以支持NAT穿透
    /// </summary>
    public class UPnPPortMapper : IDisposable
    {
        #region 常量定义

        /// <summary>
        /// UPnP 多播地址
        /// </summary>
        private const string UPNP_MULTICAST_ADDRESS = "239.255.255.250";

        /// <summary>
        /// UPnP 多播端口
        /// </summary>
        private const int UPNP_MULTICAST_PORT = 1900;

        /// <summary>
        /// 发现超时时间（毫秒）
        /// </summary>
        private const int DISCOVERY_TIMEOUT_MS = 5000;

        /// <summary>
        /// HTTP 请求超时时间（毫秒）
        /// </summary>
        private const int HTTP_TIMEOUT_MS = 10000;

        #endregion

        #region 字段和属性

        /// <summary>
        /// 是否已发现UPnP设备
        /// </summary>
        public bool IsUPnPAvailable { get; private set; }

        /// <summary>
        /// UPnP 设备控制URL
        /// </summary>
        private string _controlUrl;

        /// <summary>
        /// UPnP 服务类型
        /// </summary>
        private string _serviceType;

        /// <summary>
        /// 本地IP地址
        /// </summary>
        private string _localIPAddress;

        /// <summary>
        /// 已映射的端口列表
        /// </summary>
        private readonly Dictionary<int, PortMapping> _mappedPorts = new Dictionary<int, PortMapping>();

        /// <summary>
        /// 是否已释放
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 构造函数和初始化

        /// <summary>
        /// 构造函数
        /// </summary>
        public UPnPPortMapper()
        {
            _localIPAddress = GetLocalIPAddress();
            _ = Task.Run(DiscoverUPnPDevice);
        }

        #endregion

        #region UPnP 发现

        /// <summary>
        /// 发现UPnP设备
        /// </summary>
        private async Task DiscoverUPnPDevice()
        {
            try
            {
                LogInfo("开始发现 UPnP 设备...");

                using (var udpClient = new UdpClient())
                {
                    // 构造SSDP发现消息
                    var searchMessage = 
                        "M-SEARCH * HTTP/1.1\r\n" +
                        "HOST: 239.255.255.250:1900\r\n" +
                        "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
                        "MAN: \"ssdp:discover\"\r\n" +
                        "MX: 3\r\n\r\n";

                    var searchData = Encoding.UTF8.GetBytes(searchMessage);
                    var multicastEndpoint = new IPEndPoint(IPAddress.Parse(UPNP_MULTICAST_ADDRESS), UPNP_MULTICAST_PORT);

                    // 发送发现请求
                    await udpClient.SendAsync(searchData, searchData.Length, multicastEndpoint);

                    // 等待响应
                    var timeout = Task.Delay(DISCOVERY_TIMEOUT_MS);
                    
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
                            var response = Encoding.UTF8.GetString(result.Buffer);

                            if (await ProcessDiscoveryResponse(response))
                            {
                                IsUPnPAvailable = true;
                                LogInfo($"UPnP 设备发现成功: {_controlUrl}");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"处理UPnP发现响应时发生异常: {ex.Message}");
                        }
                    }
                }

                LogWarning("未发现可用的 UPnP 设备");
            }
            catch (Exception ex)
            {
                LogError($"发现 UPnP 设备时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理发现响应
        /// </summary>
        /// <param name="response">响应内容</param>
        /// <returns>是否成功处理</returns>
        private async Task<bool> ProcessDiscoveryResponse(string response)
        {
            try
            {
                // 查找LOCATION头
                var lines = response.Split('\n');
                string location = null;

                foreach (var line in lines)
                {
                    if (line.ToUpper().StartsWith("LOCATION:"))
                    {
                        location = line.Substring(9).Trim();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(location))
                {
                    return false;
                }

                // 获取设备描述
                var deviceDescription = await GetDeviceDescription(location);
                if (string.IsNullOrEmpty(deviceDescription))
                {
                    return false;
                }

                // 解析控制URL
                return ParseControlUrl(deviceDescription, location);
            }
            catch (Exception ex)
            {
                LogDebug($"处理UPnP发现响应时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取设备描述
        /// </summary>
        /// <param name="location">设备位置URL</param>
        /// <returns>设备描述XML</returns>
        private async Task<string> GetDeviceDescription(string location)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "UPnP/1.0 GameChat/1.0");
                    
                    var downloadTask = client.DownloadStringTaskAsync(location);
                    var timeoutTask = Task.Delay(HTTP_TIMEOUT_MS);
                    
                    var completedTask = await Task.WhenAny(downloadTask, timeoutTask);
                    
                    if (completedTask == downloadTask)
                    {
                        return await downloadTask;
                    }
                    else
                    {
                        LogWarning($"获取设备描述超时: {location}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"获取设备描述时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析控制URL
        /// </summary>
        /// <param name="deviceDescription">设备描述XML</param>
        /// <param name="baseUrl">基础URL</param>
        /// <returns>是否成功解析</returns>
        private bool ParseControlUrl(string deviceDescription, string baseUrl)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(deviceDescription);

                // 查找WANIPConnection服务
                var serviceNodes = doc.GetElementsByTagName("service");
                
                foreach (XmlNode serviceNode in serviceNodes)
                {
                    var serviceTypeNode = serviceNode.SelectSingleNode("serviceType");
                    if (serviceTypeNode?.InnerText.Contains("WANIPConnection") == true)
                    {
                        var controlUrlNode = serviceNode.SelectSingleNode("controlURL");
                        if (controlUrlNode != null)
                        {
                            _serviceType = serviceTypeNode.InnerText;
                            _controlUrl = CombineUrls(baseUrl, controlUrlNode.InnerText);
                            return true;
                        }
                    }
                }

                // 如果没找到WANIPConnection，尝试WANPPPConnection
                foreach (XmlNode serviceNode in serviceNodes)
                {
                    var serviceTypeNode = serviceNode.SelectSingleNode("serviceType");
                    if (serviceTypeNode?.InnerText.Contains("WANPPPConnection") == true)
                    {
                        var controlUrlNode = serviceNode.SelectSingleNode("controlURL");
                        if (controlUrlNode != null)
                        {
                            _serviceType = serviceTypeNode.InnerText;
                            _controlUrl = CombineUrls(baseUrl, controlUrlNode.InnerText);
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogError($"解析控制URL时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 端口映射

        /// <summary>
        /// 添加端口映射
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="protocol">协议（TCP/UDP）</param>
        /// <param name="description">描述</param>
        /// <returns>映射是否成功</returns>
        public async Task<bool> AddPortMapping(int port, string protocol, string description)
        {
            if (!IsUPnPAvailable)
            {
                LogWarning("UPnP 不可用，无法添加端口映射");
                return false;
            }

            try
            {
                LogInfo($"正在添加端口映射: {port}/{protocol}");

                // 构造SOAP请求
                var soapRequest = CreateAddPortMappingRequest(port, protocol, description);
                
                // 发送SOAP请求
                var success = await SendSoapRequest("AddPortMapping", soapRequest);
                
                if (success)
                {
                    _mappedPorts[port] = new PortMapping
                    {
                        Port = port,
                        Protocol = protocol,
                        Description = description,
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    LogInfo($"端口映射添加成功: {port}/{protocol}");
                }
                else
                {
                    LogError($"端口映射添加失败: {port}/{protocol}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"添加端口映射时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 移除端口映射
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="protocol">协议（TCP/UDP）</param>
        public void RemovePortMapping(int port, string protocol)
        {
            if (!IsUPnPAvailable)
            {
                return;
            }

            try
            {
                LogInfo($"正在移除端口映射: {port}/{protocol}");

                // 构造SOAP请求
                var soapRequest = CreateRemovePortMappingRequest(port, protocol);
                
                // 异步发送请求，不等待结果
                _ = Task.Run(async () =>
                {
                    var success = await SendSoapRequest("DeletePortMapping", soapRequest);
                    if (success)
                    {
                        _mappedPorts.Remove(port);
                        LogInfo($"端口映射移除成功: {port}/{protocol}");
                    }
                    else
                    {
                        LogError($"端口映射移除失败: {port}/{protocol}");
                    }
                });
            }
            catch (Exception ex)
            {
                LogError($"移除端口映射时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取已映射的端口列表
        /// </summary>
        /// <returns>端口映射列表</returns>
        public List<PortMapping> GetMappedPorts()
        {
            return new List<PortMapping>(_mappedPorts.Values);
        }

        #endregion

        #region SOAP 请求

        /// <summary>
        /// 创建添加端口映射的SOAP请求
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="protocol">协议</param>
        /// <param name="description">描述</param>
        /// <returns>SOAP请求内容</returns>
        private string CreateAddPortMappingRequest(int port, string protocol, string description)
        {
            return $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
<s:Body>
<u:AddPortMapping xmlns:u=""{_serviceType}"">
<NewRemoteHost></NewRemoteHost>
<NewExternalPort>{port}</NewExternalPort>
<NewProtocol>{protocol}</NewProtocol>
<NewInternalPort>{port}</NewInternalPort>
<NewInternalClient>{_localIPAddress}</NewInternalClient>
<NewEnabled>1</NewEnabled>
<NewPortMappingDescription>{description}</NewPortMappingDescription>
<NewLeaseDuration>0</NewLeaseDuration>
</u:AddPortMapping>
</s:Body>
</s:Envelope>";
        }

        /// <summary>
        /// 创建移除端口映射的SOAP请求
        /// </summary>
        /// <param name="port">端口号</param>
        /// <param name="protocol">协议</param>
        /// <returns>SOAP请求内容</returns>
        private string CreateRemovePortMappingRequest(int port, string protocol)
        {
            return $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
<s:Body>
<u:DeletePortMapping xmlns:u=""{_serviceType}"">
<NewRemoteHost></NewRemoteHost>
<NewExternalPort>{port}</NewExternalPort>
<NewProtocol>{protocol}</NewProtocol>
</u:DeletePortMapping>
</s:Body>
</s:Envelope>";
        }

        /// <summary>
        /// 发送SOAP请求
        /// </summary>
        /// <param name="action">SOAP动作</param>
        /// <param name="soapRequest">SOAP请求内容</param>
        /// <returns>请求是否成功</returns>
        private async Task<bool> SendSoapRequest(string action, string soapRequest)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("Content-Type", "text/xml; charset=\"utf-8\"");
                    client.Headers.Add("SOAPAction", $"\"{_serviceType}#{action}\"");
                    client.Headers.Add("User-Agent", "UPnP/1.0 GameChat/1.0");

                    var requestData = Encoding.UTF8.GetBytes(soapRequest);
                    
                    var uploadTask = client.UploadDataTaskAsync(_controlUrl, "POST", requestData);
                    var timeoutTask = Task.Delay(HTTP_TIMEOUT_MS);
                    
                    var completedTask = await Task.WhenAny(uploadTask, timeoutTask);
                    
                    if (completedTask == uploadTask)
                    {
                        var response = await uploadTask;
                        var responseText = Encoding.UTF8.GetString(response);
                        
                        // 检查响应是否包含错误
                        return !responseText.Contains("soap:Fault") && !responseText.Contains("s:Fault");
                    }
                    else
                    {
                        LogWarning($"SOAP请求超时: {action}");
                        return false;
                    }
                }
            }
            catch (WebException ex)
            {
                // HTTP错误可能仍然表示成功（某些路由器返回500但实际成功）
                if (ex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.InternalServerError)
                {
                    LogDebug($"SOAP请求返回500，可能仍然成功: {action}");
                    return true;
                }
                
                LogError($"发送SOAP请求时发生网络异常: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"发送SOAP请求时发生异常: {ex.Message}");
                return false;
            }
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
        /// 组合URL
        /// </summary>
        /// <param name="baseUrl">基础URL</param>
        /// <param name="relativePath">相对路径</param>
        /// <returns>完整URL</returns>
        private string CombineUrls(string baseUrl, string relativePath)
        {
            if (relativePath.StartsWith("http://") || relativePath.StartsWith("https://"))
            {
                return relativePath;
            }

            var uri = new Uri(baseUrl);
            var baseUri = new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}");
            
            return new Uri(baseUri, relativePath).ToString();
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[UPnPPortMapper] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[UPnPPortMapper] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[UPnPPortMapper] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[UPnPPortMapper][DEBUG] {message}");
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
                    // 移除所有端口映射
                    foreach (var mapping in _mappedPorts.Values)
                    {
                        RemovePortMapping(mapping.Port, mapping.Protocol);
                    }
                    _mappedPorts.Clear();
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~UPnPPortMapper()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// 端口映射信息类
    /// </summary>
    public class PortMapping
    {
        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 协议（TCP/UDP）
        /// </summary>
        public string Protocol { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        public override string ToString()
        {
            return $"{Port}/{Protocol} - {Description} (创建于: {CreatedAt:yyyy-MM-dd HH:mm:ss})";
        }
    }
}