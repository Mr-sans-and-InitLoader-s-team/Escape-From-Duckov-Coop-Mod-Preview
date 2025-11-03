using System;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 客机连接配置类
    /// 包含连接参数的配置和验证逻辑
    /// </summary>
    [Serializable]
    public class ClientConnectionConfig
    {
        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; }
        
        /// <summary>
        /// 心跳间隔时间（毫秒）
        /// </summary>
        public int HeartbeatIntervalMs { get; set; }
        
        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetryCount { get; set; }
        
        /// <summary>
        /// 重试延迟时间（毫秒）
        /// </summary>
        public int RetryDelayMs { get; set; }
        
        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool AutoReconnect { get; set; }
        
        /// <summary>
        /// 连接质量检查间隔（毫秒）
        /// </summary>
        public int QualityCheckIntervalMs { get; set; }
        
        /// <summary>
        /// 消息发送超时时间（毫秒）
        /// </summary>
        public int MessageSendTimeoutMs { get; set; }
        
        /// <summary>
        /// 消息接收缓冲区大小
        /// </summary>
        public int ReceiveBufferSize { get; set; }
        
        /// <summary>
        /// 是否启用消息压缩
        /// </summary>
        public bool EnableMessageCompression { get; set; }
        
        /// <summary>
        /// 网络类型偏好
        /// </summary>
        public NetworkType PreferredNetworkType { get; set; }
        
        /// <summary>
        /// 构造函数，设置默认值
        /// </summary>
        public ClientConnectionConfig()
        {
            // 连接相关默认值
            ConnectionTimeoutMs = 10000;        // 10秒连接超时
            HeartbeatIntervalMs = 5000;         // 5秒心跳间隔
            MaxRetryCount = 3;                  // 最大重试3次
            RetryDelayMs = 2000;                // 重试延迟2秒
            AutoReconnect = true;               // 默认启用自动重连
            
            // 质量监控默认值
            QualityCheckIntervalMs = 1000;      // 1秒质量检查间隔
            
            // 消息相关默认值
            MessageSendTimeoutMs = 5000;        // 5秒消息发送超时
            ReceiveBufferSize = 1024 * 64;      // 64KB接收缓冲区
            EnableMessageCompression = false;    // 默认不启用压缩
            
            // 网络类型偏好
            PreferredNetworkType = NetworkType.SteamP2P;  // 默认偏好Steam P2P
        }
        
        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        /// <returns>配置是否有效</returns>
        public bool IsValid()
        {
            // 检查超时时间
            if (ConnectionTimeoutMs <= 0)
            {
                return false;
            }
            
            // 检查心跳间隔
            if (HeartbeatIntervalMs <= 0)
            {
                return false;
            }
            
            // 检查重试次数
            if (MaxRetryCount < 0)
            {
                return false;
            }
            
            // 检查重试延迟
            if (RetryDelayMs < 0)
            {
                return false;
            }
            
            // 检查质量检查间隔
            if (QualityCheckIntervalMs <= 0)
            {
                return false;
            }
            
            // 检查消息发送超时
            if (MessageSendTimeoutMs <= 0)
            {
                return false;
            }
            
            // 检查接收缓冲区大小
            if (ReceiveBufferSize <= 0)
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// 创建快速连接配置（较短的超时时间）
        /// </summary>
        /// <returns>快速连接配置</returns>
        public static ClientConnectionConfig CreateFastConfig()
        {
            return new ClientConnectionConfig
            {
                ConnectionTimeoutMs = 5000,     // 5秒连接超时
                HeartbeatIntervalMs = 3000,     // 3秒心跳间隔
                MaxRetryCount = 5,              // 最大重试5次
                RetryDelayMs = 1000,            // 重试延迟1秒
                AutoReconnect = true,
                QualityCheckIntervalMs = 500,   // 0.5秒质量检查间隔
                MessageSendTimeoutMs = 3000,    // 3秒消息发送超时
                ReceiveBufferSize = 1024 * 32,  // 32KB接收缓冲区
                EnableMessageCompression = false,
                PreferredNetworkType = NetworkType.SteamP2P
            };
        }
        
        /// <summary>
        /// 创建稳定连接配置（较长的超时时间）
        /// </summary>
        /// <returns>稳定连接配置</returns>
        public static ClientConnectionConfig CreateStableConfig()
        {
            return new ClientConnectionConfig
            {
                ConnectionTimeoutMs = 20000,    // 20秒连接超时
                HeartbeatIntervalMs = 10000,    // 10秒心跳间隔
                MaxRetryCount = 2,              // 最大重试2次
                RetryDelayMs = 5000,            // 重试延迟5秒
                AutoReconnect = true,
                QualityCheckIntervalMs = 2000,  // 2秒质量检查间隔
                MessageSendTimeoutMs = 10000,   // 10秒消息发送超时
                ReceiveBufferSize = 1024 * 128, // 128KB接收缓冲区
                EnableMessageCompression = true, // 启用压缩以提高稳定性
                PreferredNetworkType = NetworkType.SteamP2P
            };
        }
        
        /// <summary>
        /// 创建低延迟配置（针对实时性要求高的场景）
        /// </summary>
        /// <returns>低延迟配置</returns>
        public static ClientConnectionConfig CreateLowLatencyConfig()
        {
            return new ClientConnectionConfig
            {
                ConnectionTimeoutMs = 8000,     // 8秒连接超时
                HeartbeatIntervalMs = 2000,     // 2秒心跳间隔
                MaxRetryCount = 3,              // 最大重试3次
                RetryDelayMs = 500,             // 重试延迟0.5秒
                AutoReconnect = true,
                QualityCheckIntervalMs = 250,   // 0.25秒质量检查间隔
                MessageSendTimeoutMs = 2000,    // 2秒消息发送超时
                ReceiveBufferSize = 1024 * 16,  // 16KB接收缓冲区
                EnableMessageCompression = false, // 不启用压缩以减少延迟
                PreferredNetworkType = NetworkType.SteamP2P
            };
        }
        
        /// <summary>
        /// 克隆配置
        /// </summary>
        /// <returns>配置副本</returns>
        public ClientConnectionConfig Clone()
        {
            return new ClientConnectionConfig
            {
                ConnectionTimeoutMs = this.ConnectionTimeoutMs,
                HeartbeatIntervalMs = this.HeartbeatIntervalMs,
                MaxRetryCount = this.MaxRetryCount,
                RetryDelayMs = this.RetryDelayMs,
                AutoReconnect = this.AutoReconnect,
                QualityCheckIntervalMs = this.QualityCheckIntervalMs,
                MessageSendTimeoutMs = this.MessageSendTimeoutMs,
                ReceiveBufferSize = this.ReceiveBufferSize,
                EnableMessageCompression = this.EnableMessageCompression,
                PreferredNetworkType = this.PreferredNetworkType
            };
        }
        
        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>配置的字符串表示</returns>
        public override string ToString()
        {
            return $"ClientConnectionConfig[" +
                   $"Timeout={ConnectionTimeoutMs}ms, " +
                   $"Heartbeat={HeartbeatIntervalMs}ms, " +
                   $"MaxRetry={MaxRetryCount}, " +
                   $"AutoReconnect={AutoReconnect}, " +
                   $"NetworkType={PreferredNetworkType}]";
        }
    }
}