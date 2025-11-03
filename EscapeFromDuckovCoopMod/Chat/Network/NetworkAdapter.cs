using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 网络适配器抽象基类，提供通用的连接状态管理和事件发布机制
    /// </summary>
    public abstract class NetworkAdapter : INetworkAdapter
    {
        #region 属性
        
        /// <summary>
        /// 当前网络类型
        /// </summary>
        public abstract NetworkType CurrentNetworkType { get; }
        
        /// <summary>
        /// 连接状态
        /// </summary>
        public ConnectionStatus Status { get; protected set; } = ConnectionStatus.Disconnected;
        
        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => Status == ConnectionStatus.Connected || Status == ConnectionStatus.Hosting;
        
        /// <summary>
        /// 当前网络配置
        /// </summary>
        protected NetworkConfig CurrentConfig { get; set; }
        
        /// <summary>
        /// 是否已初始化
        /// </summary>
        protected bool IsInitialized { get; set; }
        
        #endregion
        
        #region 事件
        
        /// <summary>
        /// 客户端连接事件
        /// </summary>
        public event Action<string> OnClientConnected;
        
        /// <summary>
        /// 客户端断开连接事件
        /// </summary>
        public event Action<string> OnClientDisconnected;
        
        /// <summary>
        /// 消息接收事件
        /// </summary>
        public event Action<byte[], string> OnMessageReceived;
        
        /// <summary>
        /// 网络错误事件
        /// </summary>
        public event Action<NetworkError> OnNetworkError;
        
        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event Action<ConnectionStatus> OnConnectionStatusChanged;
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 切换网络类型
        /// </summary>
        /// <param name="type">目标网络类型</param>
        /// <returns>切换是否成功</returns>
        public virtual bool SwitchNetworkType(NetworkType type)
        {
            // 基类默认不支持网络类型切换，由具体实现决定
            LogWarning($"网络类型切换不被支持: {CurrentNetworkType} -> {type}");
            return false;
        }
        
        /// <summary>
        /// 获取可用的网络类型列表
        /// </summary>
        /// <returns>可用网络类型列表</returns>
        public virtual List<NetworkType> GetAvailableNetworks()
        {
            // 基类默认只返回当前网络类型
            return new List<NetworkType> { CurrentNetworkType };
        }
        
        /// <summary>
        /// 启动主机服务
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <returns>启动是否成功</returns>
        public async Task<bool> StartHost(NetworkConfig config)
        {
            try
            {
                // 验证配置
                if (!ValidateConfig(config))
                {
                    var error = new NetworkError(NetworkErrorType.InvalidConfiguration, "网络配置无效");
                    OnNetworkError?.Invoke(error);
                    return false;
                }
                
                // 检查当前状态
                if (IsConnected)
                {
                    LogWarning("网络适配器已连接，无法启动主机服务");
                    return false;
                }
                
                SetConnectionStatus(ConnectionStatus.Connecting);
                CurrentConfig = config;
                
                // 调用具体实现的启动逻辑
                bool result = await StartHostInternal(config);
                
                if (result)
                {
                    SetConnectionStatus(ConnectionStatus.Hosting);
                    LogInfo($"主机服务启动成功: {CurrentNetworkType}");
                }
                else
                {
                    SetConnectionStatus(ConnectionStatus.Failed);
                    var error = new NetworkError(NetworkErrorType.ServiceStartFailed, "主机服务启动失败");
                    OnNetworkError?.Invoke(error);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                SetConnectionStatus(ConnectionStatus.Failed);
                var error = new NetworkError(NetworkErrorType.ServiceStartFailed, "主机服务启动异常", ex.Message);
                OnNetworkError?.Invoke(error);
                LogError($"启动主机服务时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 连接到主机
        /// </summary>
        /// <param name="endpoint">主机端点</param>
        /// <returns>连接是否成功</returns>
        public async Task<bool> ConnectToHost(string endpoint)
        {
            try
            {
                // 检查当前状态
                if (IsConnected)
                {
                    LogWarning("网络适配器已连接，无法连接到主机");
                    return false;
                }
                
                if (string.IsNullOrEmpty(endpoint))
                {
                    var error = new NetworkError(NetworkErrorType.InvalidConfiguration, "主机端点不能为空");
                    OnNetworkError?.Invoke(error);
                    return false;
                }
                
                SetConnectionStatus(ConnectionStatus.Connecting);
                
                // 调用具体实现的连接逻辑
                bool result = await ConnectToHostInternal(endpoint);
                
                if (result)
                {
                    SetConnectionStatus(ConnectionStatus.Connected);
                    LogInfo($"成功连接到主机: {endpoint}");
                }
                else
                {
                    SetConnectionStatus(ConnectionStatus.Failed);
                    var error = new NetworkError(NetworkErrorType.ConnectionFailed, $"连接主机失败: {endpoint}");
                    OnNetworkError?.Invoke(error);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                SetConnectionStatus(ConnectionStatus.Failed);
                var error = new NetworkError(NetworkErrorType.ConnectionFailed, "连接主机时发生异常", ex.Message);
                OnNetworkError?.Invoke(error);
                LogError($"连接主机时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 断开连接
        /// </summary>
        public virtual void Disconnect()
        {
            try
            {
                if (!IsConnected)
                {
                    LogWarning("网络适配器未连接，无需断开");
                    return;
                }
                
                // 调用具体实现的断开逻辑
                DisconnectInternal();
                
                SetConnectionStatus(ConnectionStatus.Disconnected);
                CurrentConfig = null;
                
                LogInfo("网络连接已断开");
            }
            catch (Exception ex)
            {
                LogError($"断开连接时发生异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 发送消息到指定目标
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="targetId">目标ID，null表示发送给所有客户端</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendMessage(byte[] data, string targetId = null)
        {
            try
            {
                if (!IsConnected)
                {
                    var error = new NetworkError(NetworkErrorType.MessageSendFailed, "网络未连接，无法发送消息");
                    OnNetworkError?.Invoke(error);
                    return false;
                }
                
                if (data == null || data.Length == 0)
                {
                    LogWarning("消息数据为空，跳过发送");
                    return false;
                }
                
                // 调用具体实现的发送逻辑
                return await SendMessageInternal(data, targetId);
            }
            catch (Exception ex)
            {
                var error = new NetworkError(NetworkErrorType.MessageSendFailed, "发送消息时发生异常", ex.Message);
                OnNetworkError?.Invoke(error);
                LogError($"发送消息时发生异常: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 广播消息给所有连接的客户端
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <returns>广播是否成功</returns>
        public async Task<bool> BroadcastMessage(byte[] data)
        {
            return await SendMessage(data, null);
        }
        
        #endregion
        
        #region 抽象方法 - 由子类实现
        
        /// <summary>
        /// 启动主机服务的具体实现
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <returns>启动是否成功</returns>
        protected abstract Task<bool> StartHostInternal(NetworkConfig config);
        
        /// <summary>
        /// 连接到主机的具体实现
        /// </summary>
        /// <param name="endpoint">主机端点</param>
        /// <returns>连接是否成功</returns>
        protected abstract Task<bool> ConnectToHostInternal(string endpoint);
        
        /// <summary>
        /// 断开连接的具体实现
        /// </summary>
        protected abstract void DisconnectInternal();
        
        /// <summary>
        /// 发送消息的具体实现
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="targetId">目标ID</param>
        /// <returns>发送是否成功</returns>
        protected abstract Task<bool> SendMessageInternal(byte[] data, string targetId);
        
        #endregion
        
        #region 受保护的方法
        
        /// <summary>
        /// 验证网络配置
        /// </summary>
        /// <param name="config">网络配置</param>
        /// <returns>配置是否有效</returns>
        protected virtual bool ValidateConfig(NetworkConfig config)
        {
            if (config == null)
            {
                LogError("网络配置不能为空");
                return false;
            }
            
            if (config.Type != CurrentNetworkType)
            {
                LogError($"网络类型不匹配: 期望 {CurrentNetworkType}, 实际 {config.Type}");
                return false;
            }
            
            return config.IsValid();
        }
        
        /// <summary>
        /// 设置连接状态并触发事件
        /// </summary>
        /// <param name="status">新的连接状态</param>
        protected void SetConnectionStatus(ConnectionStatus status)
        {
            if (Status != status)
            {
                var oldStatus = Status;
                Status = status;
                
                LogInfo($"连接状态变化: {oldStatus} -> {status}");
                OnConnectionStatusChanged?.Invoke(status);
            }
        }
        
        /// <summary>
        /// 触发客户端连接事件
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        protected void TriggerClientConnected(string clientId)
        {
            LogInfo($"客户端已连接: {clientId}");
            OnClientConnected?.Invoke(clientId);
        }
        
        /// <summary>
        /// 触发客户端断开连接事件
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        protected void TriggerClientDisconnected(string clientId)
        {
            LogInfo($"客户端已断开: {clientId}");
            OnClientDisconnected?.Invoke(clientId);
        }
        
        /// <summary>
        /// 触发消息接收事件
        /// </summary>
        /// <param name="data">消息数据</param>
        /// <param name="senderId">发送者ID</param>
        protected void TriggerMessageReceived(byte[] data, string senderId)
        {
            LogDebug($"收到消息: 发送者={senderId}, 大小={data?.Length ?? 0}字节");
            OnMessageReceived?.Invoke(data, senderId);
        }
        
        /// <summary>
        /// 触发网络错误事件
        /// </summary>
        /// <param name="error">网络错误</param>
        protected void TriggerNetworkError(NetworkError error)
        {
            LogError($"网络错误: {error}");
            OnNetworkError?.Invoke(error);
        }
        
        #endregion
        
        #region 日志方法
        
        /// <summary>
        /// 记录信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        protected virtual void LogInfo(string message)
        {
            Debug.Log($"[{CurrentNetworkType}] {message}");
        }
        
        /// <summary>
        /// 记录警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        protected virtual void LogWarning(string message)
        {
            Debug.LogWarning($"[{CurrentNetworkType}] {message}");
        }
        
        /// <summary>
        /// 记录错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        protected virtual void LogError(string message)
        {
            Debug.LogError($"[{CurrentNetworkType}] {message}");
        }
        
        /// <summary>
        /// 记录调试日志
        /// </summary>
        /// <param name="message">日志消息</param>
        protected virtual void LogDebug(string message)
        {
            Debug.Log($"[{CurrentNetworkType}][DEBUG] {message}");
        }
        
        #endregion
        
        #region 析构和清理
        
        /// <summary>
        /// 析构函数，确保资源清理
        /// </summary>
        ~NetworkAdapter()
        {
            Disconnect();
        }
        
        #endregion
    }
}