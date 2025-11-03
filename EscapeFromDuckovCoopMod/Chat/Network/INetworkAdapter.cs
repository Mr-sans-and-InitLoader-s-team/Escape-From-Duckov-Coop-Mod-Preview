using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 网络适配器接口
    /// </summary>
    public interface INetworkAdapter
    {
        #region 属性

        /// <summary>
        /// 当前网络类型
        /// </summary>
        NetworkType CurrentNetworkType { get; }

        /// <summary>
        /// 连接状态
        /// </summary>
        ConnectionStatus Status { get; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        #endregion

        #region 事件

        /// <summary>
        /// 客户端连接事件
        /// </summary>
        event Action<string> OnClientConnected;

        /// <summary>
        /// 客户端断开连接事件
        /// </summary>
        event Action<string> OnClientDisconnected;

        /// <summary>
        /// 消息接收事件
        /// </summary>
        event Action<byte[], string> OnMessageReceived;

        /// <summary>
        /// 网络错误事件
        /// </summary>
        event Action<NetworkError> OnNetworkError;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        event Action<ConnectionStatus> OnConnectionStatusChanged;

        #endregion

        #region 方法

        /// <summary>
        /// 切换网络类型
        /// </summary>
        /// <param name="type">目标网络类型</param>
        /// <returns>切换是否成功</returns>
        bool SwitchNetworkType(NetworkType type);

        /// <summary>
        /// 获取可用的网络类型列表
        /// </summary>
        /// <returns>可用网络类型列表</returns>
        List<NetworkType> GetAvailableNetworks();

        /// <summary>
        /// 启动主机服务
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <returns>启动是否成功</returns>
        Task<bool> StartHost(NetworkConfig config);

        /// <summary>
        /// 连接到主机
        /// </summary>
        /// <param name="endpoint">主机端点</param>
        /// <returns>连接是否成功</returns>
        Task<bool> ConnectToHost(string endpoint);

        /// <summary>
        /// 断开连接
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="targetId">目标ID，null表示广播</param>
        /// <returns>发送是否成功</returns>
        Task<bool> SendMessage(byte[] data, string targetId = null);

        /// <summary>
        /// 广播消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>广播是否成功</returns>
        Task<bool> BroadcastMessage(byte[] data);

        #endregion
    }
}