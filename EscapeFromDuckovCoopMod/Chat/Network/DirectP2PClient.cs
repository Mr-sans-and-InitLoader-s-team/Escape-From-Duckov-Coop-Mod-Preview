using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 直连 P2P 客户端类
    /// 管理单个客户端的TCP连接和消息传输
    /// </summary>
    public class DirectP2PClient : IDisposable
    {
        #region 字段和属性

        /// <summary>
        /// TCP客户端
        /// </summary>
        private readonly TcpClient _tcpClient;

        /// <summary>
        /// 网络流
        /// </summary>
        private readonly NetworkStream _stream;

        /// <summary>
        /// 客户端端点
        /// </summary>
        public string EndPoint { get; private set; }

        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime ConnectedTime { get; private set; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivity { get; private set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _tcpClient?.Connected ?? false;

        /// <summary>
        /// 是否活跃（30秒内有活动）
        /// </summary>
        public bool IsActive => (DateTime.UtcNow - LastActivity).TotalSeconds < 30;

        /// <summary>
        /// 发送的消息数量
        /// </summary>
        public int MessagesSent { get; private set; }

        /// <summary>
        /// 接收的消息数量
        /// </summary>
        public int MessagesReceived { get; private set; }

        /// <summary>
        /// 发送的字节数
        /// </summary>
        public long BytesSent { get; private set; }

        /// <summary>
        /// 接收的字节数
        /// </summary>
        public long BytesReceived { get; private set; }

        /// <summary>
        /// 是否已释放
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="tcpClient">TCP客户端</param>
        /// <param name="endPoint">客户端端点</param>
        public DirectP2PClient(TcpClient tcpClient, string endPoint)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
            
            _stream = _tcpClient.GetStream();
            ConnectedTime = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;

            LogInfo($"直连客户端已创建: {EndPoint}");
        }

        #endregion

        #region 消息传输

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendMessage(byte[] data)
        {
            if (_disposed || !IsConnected || data == null || data.Length == 0)
            {
                return false;
            }

            try
            {
                // 发送消息长度（4字节）
                var lengthBytes = BitConverter.GetBytes(data.Length);
                await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);

                // 发送消息数据
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();

                // 更新统计信息
                MessagesSent++;
                BytesSent += data.Length + 4; // 包含长度字节
                LastActivity = DateTime.UtcNow;

                LogDebug($"消息已发送到 {EndPoint}: {data.Length} 字节");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"发送消息到 {EndPoint} 时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        /// <returns>接收到的消息数据，如果失败返回null</returns>
        public async Task<byte[]> ReceiveMessage()
        {
            if (_disposed || !IsConnected)
            {
                return null;
            }

            try
            {
                // 读取消息长度（4字节）
                var lengthBytes = new byte[4];
                int bytesRead = 0;
                
                while (bytesRead < 4)
                {
                    int read = await _stream.ReadAsync(lengthBytes, bytesRead, 4 - bytesRead);
                    if (read == 0)
                    {
                        LogWarning($"连接已关闭: {EndPoint}");
                        return null;
                    }
                    bytesRead += read;
                }

                int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                
                // 验证消息长度
                if (messageLength <= 0 || messageLength > 1024 * 1024) // 最大1MB
                {
                    LogError($"无效的消息长度: {messageLength}");
                    return null;
                }

                // 读取消息数据
                var messageData = new byte[messageLength];
                bytesRead = 0;
                
                while (bytesRead < messageLength)
                {
                    int read = await _stream.ReadAsync(messageData, bytesRead, messageLength - bytesRead);
                    if (read == 0)
                    {
                        LogWarning($"连接已关闭: {EndPoint}");
                        return null;
                    }
                    bytesRead += read;
                }

                // 更新统计信息
                MessagesReceived++;
                BytesReceived += messageLength + 4; // 包含长度字节
                LastActivity = DateTime.UtcNow;

                LogDebug($"消息已从 {EndPoint} 接收: {messageLength} 字节");
                return messageData;
            }
            catch (Exception ex)
            {
                LogError($"从 {EndPoint} 接收消息时发生异常: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (!_disposed && IsConnected)
                {
                    _stream?.Close();
                    _tcpClient?.Close();
                    LogInfo($"客户端连接已断开: {EndPoint}");
                }
            }
            catch (Exception ex)
            {
                LogError($"断开客户端连接时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        /// <returns>连接统计信息</returns>
        public ClientStatistics GetStatistics()
        {
            return new ClientStatistics
            {
                EndPoint = EndPoint,
                ConnectedTime = ConnectedTime,
                LastActivity = LastActivity,
                IsConnected = IsConnected,
                IsActive = IsActive,
                MessagesSent = MessagesSent,
                MessagesReceived = MessagesReceived,
                BytesSent = BytesSent,
                BytesReceived = BytesReceived,
                ConnectionDuration = DateTime.UtcNow - ConnectedTime
            };
        }

        /// <summary>
        /// 测试连接是否活跃
        /// </summary>
        /// <returns>连接是否活跃</returns>
        public async Task<bool> TestConnection()
        {
            try
            {
                if (!IsConnected)
                {
                    return false;
                }

                // 发送ping消息测试连接
                var pingData = System.Text.Encoding.UTF8.GetBytes("PING");
                return await SendMessage(pingData);
            }
            catch (Exception ex)
            {
                LogError($"测试连接时发生异常: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[DirectP2PClient] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[DirectP2PClient] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[DirectP2PClient] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[DirectP2PClient][DEBUG] {message}");
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
                    Disconnect();
                    _stream?.Dispose();
                    _tcpClient?.Dispose();
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~DirectP2PClient()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// 客户端统计信息类
    /// </summary>
    public class ClientStatistics
    {
        /// <summary>
        /// 客户端端点
        /// </summary>
        public string EndPoint { get; set; }

        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime ConnectedTime { get; set; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivity { get; set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 是否活跃
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 发送的消息数量
        /// </summary>
        public int MessagesSent { get; set; }

        /// <summary>
        /// 接收的消息数量
        /// </summary>
        public int MessagesReceived { get; set; }

        /// <summary>
        /// 发送的字节数
        /// </summary>
        public long BytesSent { get; set; }

        /// <summary>
        /// 接收的字节数
        /// </summary>
        public long BytesReceived { get; set; }

        /// <summary>
        /// 连接持续时间
        /// </summary>
        public TimeSpan ConnectionDuration { get; set; }

        public override string ToString()
        {
            return $"{EndPoint} - 连接: {IsConnected}, 活跃: {IsActive}, " +
                   $"消息: {MessagesSent}↑/{MessagesReceived}↓, " +
                   $"字节: {BytesSent}↑/{BytesReceived}↓, " +
                   $"持续: {ConnectionDuration:hh\\:mm\\:ss}";
        }
    }
}