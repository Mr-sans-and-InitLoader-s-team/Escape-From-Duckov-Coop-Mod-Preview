using System;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 连接质量数据类
    /// </summary>
    public class ConnectionQuality
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 当前延迟（毫秒）
        /// </summary>
        public int Latency { get; set; }
        
        /// <summary>
        /// 平均延迟（毫秒）
        /// </summary>
        public int AverageLatency { get; set; }
        
        /// <summary>
        /// 丢包率（百分比）
        /// </summary>
        public float PacketLoss { get; set; }
        
        /// <summary>
        /// 连接稳定性（0-100）
        /// </summary>
        public int Stability { get; set; }
        
        /// <summary>
        /// 总体质量分数（0-100）
        /// </summary>
        public int OverallScore { get; set; }
        
        /// <summary>
        /// 质量等级
        /// </summary>
        public ConnectionQualityLevel QualityLevel { get; set; }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public ConnectionQuality()
        {
            Timestamp = DateTime.UtcNow;
            QualityLevel = ConnectionQualityLevel.Unknown;
        }
        
        /// <summary>
        /// 获取质量描述
        /// </summary>
        /// <returns>质量描述字符串</returns>
        public string GetQualityDescription()
        {
            switch (QualityLevel)
            {
                case ConnectionQualityLevel.Excellent:
                    return "优秀";
                case ConnectionQualityLevel.Good:
                    return "良好";
                case ConnectionQualityLevel.Fair:
                    return "一般";
                case ConnectionQualityLevel.Poor:
                    return "较差";
                case ConnectionQualityLevel.VeryPoor:
                    return "很差";
                default:
                    return "未知";
            }
        }
        
        /// <summary>
        /// 获取详细的质量报告
        /// </summary>
        /// <returns>详细质量报告</returns>
        public string GetDetailedReport()
        {
            return $"连接质量: {GetQualityDescription()} (分数: {OverallScore})\n" +
                   $"延迟: {Latency}ms (平均: {AverageLatency}ms)\n" +
                   $"丢包率: {PacketLoss:F2}%\n" +
                   $"稳定性: {Stability}%";
        }
        
        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>字符串表示</returns>
        public override string ToString()
        {
            return $"Quality[{QualityLevel}:{OverallScore}, Latency:{Latency}ms, Loss:{PacketLoss:F1}%, Stability:{Stability}%]";
        }
    }
    
    /// <summary>
    /// 连接质量等级枚举
    /// </summary>
    public enum ConnectionQualityLevel
    {
        /// <summary>
        /// 未知
        /// </summary>
        Unknown,
        
        /// <summary>
        /// 很差
        /// </summary>
        VeryPoor,
        
        /// <summary>
        /// 较差
        /// </summary>
        Poor,
        
        /// <summary>
        /// 一般
        /// </summary>
        Fair,
        
        /// <summary>
        /// 良好
        /// </summary>
        Good,
        
        /// <summary>
        /// 优秀
        /// </summary>
        Excellent
    }
    
    /// <summary>
    /// 连接质量警告类
    /// </summary>
    public class ConnectionQualityWarning
    {
        /// <summary>
        /// 警告类型
        /// </summary>
        public QualityWarningType Type { get; set; }
        
        /// <summary>
        /// 警告消息
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// 警告严重程度
        /// </summary>
        public WarningSeverity Severity { get; set; }
        
        /// <summary>
        /// 警告时间
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public ConnectionQualityWarning()
        {
            Timestamp = DateTime.UtcNow;
        }
        
        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>字符串表示</returns>
        public override string ToString()
        {
            return $"[{Severity}] {Type}: {Message}";
        }
    }
    
    /// <summary>
    /// 质量警告类型枚举
    /// </summary>
    public enum QualityWarningType
    {
        /// <summary>
        /// 高延迟
        /// </summary>
        HighLatency,
        
        /// <summary>
        /// 高丢包率
        /// </summary>
        HighPacketLoss,
        
        /// <summary>
        /// 低稳定性
        /// </summary>
        LowStability,
        
        /// <summary>
        /// 连接不稳定
        /// </summary>
        UnstableConnection,
        
        /// <summary>
        /// 网络拥塞
        /// </summary>
        NetworkCongestion
    }
    
    /// <summary>
    /// 警告严重程度枚举
    /// </summary>
    public enum WarningSeverity
    {
        /// <summary>
        /// 信息
        /// </summary>
        Info,
        
        /// <summary>
        /// 警告
        /// </summary>
        Warning,
        
        /// <summary>
        /// 严重
        /// </summary>
        Critical
    }
    
    /// <summary>
    /// 连接监控统计信息类
    /// </summary>
    public class ConnectionMonitoringStats
    {
        /// <summary>
        /// 监控持续时间
        /// </summary>
        public TimeSpan MonitoringDuration { get; set; }
        
        /// <summary>
        /// 总心跳次数
        /// </summary>
        public int TotalHeartbeats { get; set; }
        
        /// <summary>
        /// 丢失的心跳次数
        /// </summary>
        public int MissedHeartbeats { get; set; }
        
        /// <summary>
        /// 总发送消息数
        /// </summary>
        public int TotalMessagesSent { get; set; }
        
        /// <summary>
        /// 总接收消息数
        /// </summary>
        public int TotalMessagesReceived { get; set; }
        
        /// <summary>
        /// 发送失败消息数
        /// </summary>
        public int FailedMessagesSent { get; set; }
        
        /// <summary>
        /// 最后一次心跳时间
        /// </summary>
        public DateTime LastHeartbeatTime { get; set; }
        
        /// <summary>
        /// 心跳成功率
        /// </summary>
        public float HeartbeatSuccessRate
        {
            get
            {
                if (TotalHeartbeats == 0)
                    return 100f;
                
                return (float)(TotalHeartbeats - MissedHeartbeats) / TotalHeartbeats * 100f;
            }
        }
        
        /// <summary>
        /// 消息发送成功率
        /// </summary>
        public float MessageSendSuccessRate
        {
            get
            {
                if (TotalMessagesSent == 0)
                    return 100f;
                
                return (float)(TotalMessagesSent - FailedMessagesSent) / TotalMessagesSent * 100f;
            }
        }
        
        /// <summary>
        /// 获取统计报告
        /// </summary>
        /// <returns>统计报告字符串</returns>
        public string GetStatsReport()
        {
            return $"监控统计报告:\n" +
                   $"监控时长: {MonitoringDuration.TotalMinutes:F1} 分钟\n" +
                   $"心跳统计: {TotalHeartbeats - MissedHeartbeats}/{TotalHeartbeats} ({HeartbeatSuccessRate:F1}%)\n" +
                   $"消息统计: 发送 {TotalMessagesSent} ({MessageSendSuccessRate:F1}% 成功), 接收 {TotalMessagesReceived}\n" +
                   $"最后心跳: {(LastHeartbeatTime == DateTime.MinValue ? "无" : LastHeartbeatTime.ToString("HH:mm:ss"))}";
        }
        
        /// <summary>
        /// 重写ToString方法
        /// </summary>
        /// <returns>字符串表示</returns>
        public override string ToString()
        {
            return $"Stats[Duration:{MonitoringDuration.TotalMinutes:F1}min, Heartbeat:{HeartbeatSuccessRate:F1}%, Messages:{MessageSendSuccessRate:F1}%]";
        }
    }
}