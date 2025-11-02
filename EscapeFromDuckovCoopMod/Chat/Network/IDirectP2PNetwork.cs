using System.Collections.Generic;
using System.Threading.Tasks;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 直连 P2P 网络接口
    /// 提供基于 TCP/UDP 的直接 P2P 连接功能
    /// </summary>
    public interface IDirectP2PNetwork : INetworkAdapter
    {
        #region 直连特定功能

        /// <summary>
        /// 启动直连主机服务
        /// </summary>
        /// <param name="port">监听端口</param>
        /// <returns>启动是否成功</returns>
        Task<bool> StartDirectHost(int port);

        /// <summary>
        /// 连接到直连主机
        /// </summary>
        /// <param name="ip">主机IP地址</param>
        /// <param name="port">主机端口</param>
        /// <returns>连接是否成功</returns>
        Task<bool> ConnectDirect(string ip, int port);

        #endregion

        #region UPnP 支持

        /// <summary>
        /// 是否启用 UPnP 端口映射
        /// </summary>
        bool EnableUPnP { get; set; }

        /// <summary>
        /// 设置 UPnP 端口映射
        /// </summary>
        /// <param name="port">要映射的端口</param>
        /// <returns>映射是否成功</returns>
        Task<bool> SetupPortMapping(int port);

        /// <summary>
        /// 移除 UPnP 端口映射
        /// </summary>
        /// <param name="port">要移除映射的端口</param>
        void RemovePortMapping(int port);

        #endregion

        #region 网络发现

        /// <summary>
        /// 发现本地网络中的主机
        /// </summary>
        /// <returns>发现的主机信息列表</returns>
        Task<List<HostInfo>> DiscoverLocalHosts();

        /// <summary>
        /// 开始主机广播
        /// </summary>
        void StartHostBroadcast();

        /// <summary>
        /// 停止主机广播
        /// </summary>
        void StopHostBroadcast();

        #endregion

        #region 网络质量

        /// <summary>
        /// 获取当前连接质量
        /// </summary>
        /// <returns>连接质量信息</returns>
        ConnectionQuality GetConnectionQuality();

        /// <summary>
        /// 测试到指定主机的网络质量
        /// </summary>
        /// <param name="ip">主机IP地址</param>
        /// <param name="port">主机端口</param>
        /// <returns>网络质量测试结果</returns>
        Task<NetworkQualityTestResult> TestNetworkQuality(string ip, int port);

        #endregion
    }

    /// <summary>
    /// 主机信息类
    /// </summary>
    public class HostInfo
    {
        /// <summary>
        /// 主机名称
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IPAddress { get; set; }

        /// <summary>
        /// 端口
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 主机描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 发现时间
        /// </summary>
        public System.DateTime DiscoveredAt { get; set; }

        /// <summary>
        /// 是否可连接
        /// </summary>
        public bool IsReachable { get; set; }

        /// <summary>
        /// 延迟（毫秒）
        /// </summary>
        public int Latency { get; set; }

        public HostInfo()
        {
            DiscoveredAt = System.DateTime.UtcNow;
        }

        public override string ToString()
        {
            return $"{HostName} ({IPAddress}:{Port}) - 延迟: {Latency}ms";
        }
    }

    /// <summary>
    /// 网络质量测试结果
    /// </summary>
    public class NetworkQualityTestResult
    {
        /// <summary>
        /// 是否可达
        /// </summary>
        public bool IsReachable { get; set; }

        /// <summary>
        /// 平均延迟（毫秒）
        /// </summary>
        public int AverageLatency { get; set; }

        /// <summary>
        /// 最小延迟（毫秒）
        /// </summary>
        public int MinLatency { get; set; }

        /// <summary>
        /// 最大延迟（毫秒）
        /// </summary>
        public int MaxLatency { get; set; }

        /// <summary>
        /// 丢包率（百分比）
        /// </summary>
        public float PacketLoss { get; set; }

        /// <summary>
        /// 质量分数（0-100）
        /// </summary>
        public int QualityScore { get; set; }

        /// <summary>
        /// 测试时间
        /// </summary>
        public System.DateTime TestTime { get; set; }

        public NetworkQualityTestResult()
        {
            TestTime = System.DateTime.UtcNow;
        }

        public override string ToString()
        {
            return $"延迟: {AverageLatency}ms, 丢包: {PacketLoss:F1}%, 分数: {QualityScore}";
        }
    }
}