using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 连接质量监控器
    /// 负责监控和评估网络连接质量
    /// </summary>
    public class ConnectionQualityMonitor : IDisposable
    {
        #region 常量定义
        
        /// <summary>
        /// 质量历史记录最大数量
        /// </summary>
        private const int MAX_QUALITY_HISTORY = 60;
        
        /// <summary>
        /// 延迟历史记录最大数量
        /// </summary>
        private const int MAX_LATENCY_HISTORY = 30;
        
        /// <summary>
        /// 丢包率历史记录最大数量
        /// </summary>
        private const int MAX_PACKET_LOSS_HISTORY = 20;
        
        #endregion
        
        #region 字段和属性
        
        /// <summary>
        /// 是否正在监控
        /// </summary>
        public bool IsMonitoring { get; private set; }
        
        /// <summary>
        /// 当前连接质量
        /// </summary>
        public ConnectionQuality CurrentQuality { get; private set; }
        
        /// <summary>
        /// 质量历史记录
        /// </summary>
        private readonly Queue<ConnectionQuality> _qualityHistory = new Queue<ConnectionQuality>();
        
        /// <summary>
        /// 延迟历史记录（毫秒）
        /// </summary>
        private readonly Queue<int> _latencyHistory = new Queue<int>();
        
        /// <summary>
        /// 丢包率历史记录（百分比）
        /// </summary>
        private readonly Queue<float> _packetLossHistory = new Queue<float>();
        
        /// <summary>
        /// 监控开始时间
        /// </summary>
        private DateTime _monitoringStartTime;
        
        /// <summary>
        /// 最后一次心跳时间
        /// </summary>
        private DateTime _lastHeartbeatTime;
        
        /// <summary>
        /// 心跳计数器
        /// </summary>
        private int _heartbeatCount;
        
        /// <summary>
        /// 丢失的心跳计数
        /// </summary>
        private int _missedHeartbeats;
        
        /// <summary>
        /// 总发送消息数
        /// </summary>
        private int _totalMessagesSent;
        
        /// <summary>
        /// 总接收消息数
        /// </summary>
        private int _totalMessagesReceived;
        
        /// <summary>
        /// 发送失败消息数
        /// </summary>
        private int _failedMessagesSent;
        
        /// <summary>
        /// 是否已释放资源
        /// </summary>
        private bool _disposed;
        
        #endregion
        
        #region 事件
        
        /// <summary>
        /// 连接质量变化事件
        /// </summary>
        public event Action<ConnectionQuality> OnQualityChanged;
        
        /// <summary>
        /// 连接质量警告事件
        /// </summary>
        public event Action<ConnectionQualityWarning> OnQualityWarning;
        
        #endregion
        
        #region 构造函数和初始化
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public ConnectionQualityMonitor()
        {
            CurrentQuality = new ConnectionQuality();
            ResetStatistics();
        }
        
        /// <summary>
        /// 重置统计数据
        /// </summary>
        private void ResetStatistics()
        {
            _heartbeatCount = 0;
            _missedHeartbeats = 0;
            _totalMessagesSent = 0;
            _totalMessagesReceived = 0;
            _failedMessagesSent = 0;
            _lastHeartbeatTime = DateTime.MinValue;
            
            _qualityHistory.Clear();
            _latencyHistory.Clear();
            _packetLossHistory.Clear();
        }
        
        #endregion
        
        #region 监控控制
        
        /// <summary>
        /// 开始监控
        /// </summary>
        public void StartMonitoring()
        {
            if (IsMonitoring)
            {
                Debug.LogWarning("[ConnectionQualityMonitor] 监控已在运行中");
                return;
            }
            
            IsMonitoring = true;
            _monitoringStartTime = DateTime.UtcNow;
            ResetStatistics();
            
            Debug.Log("[ConnectionQualityMonitor] 开始连接质量监控");
        }
        
        /// <summary>
        /// 停止监控
        /// </summary>
        public void StopMonitoring()
        {
            if (!IsMonitoring)
            {
                return;
            }
            
            IsMonitoring = false;
            
            Debug.Log("[ConnectionQualityMonitor] 停止连接质量监控");
        }
        
        #endregion
        
        #region 质量计算
        
        /// <summary>
        /// 计算连接质量
        /// </summary>
        /// <param name="networkQuality">网络质量数据</param>
        /// <returns>连接质量</returns>
        public ConnectionQuality CalculateConnectionQuality(NetworkQuality networkQuality)
        {
            if (!IsMonitoring)
            {
                return CurrentQuality;
            }
            
            try
            {
                // 更新延迟历史
                UpdateLatencyHistory(networkQuality.Latency);
                
                // 计算各项质量指标
                var quality = new ConnectionQuality
                {
                    Timestamp = DateTime.UtcNow,
                    Latency = networkQuality.Latency,
                    AverageLatency = CalculateAverageLatency(),
                    PacketLoss = CalculatePacketLoss(),
                    Stability = CalculateStability(),
                    OverallScore = 0 // 将在下面计算
                };
                
                // 计算总体质量分数
                quality.OverallScore = CalculateOverallScore(quality);
                
                // 确定质量等级
                quality.QualityLevel = DetermineQualityLevel(quality.OverallScore);
                
                // 更新当前质量
                var oldQuality = CurrentQuality;
                CurrentQuality = quality;
                
                // 添加到历史记录
                AddToQualityHistory(quality);
                
                // 检查质量变化
                if (HasSignificantQualityChange(oldQuality, quality))
                {
                    OnQualityChanged?.Invoke(quality);
                }
                
                // 检查质量警告
                CheckForQualityWarnings(quality);
                
                return quality;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionQualityMonitor] 计算连接质量时发生异常: {ex.Message}");
                return CurrentQuality;
            }
        }
        
        /// <summary>
        /// 计算平均延迟
        /// </summary>
        /// <returns>平均延迟（毫秒）</returns>
        private int CalculateAverageLatency()
        {
            if (_latencyHistory.Count == 0)
            {
                return 0;
            }
            
            return (int)_latencyHistory.Average();
        }
        
        /// <summary>
        /// 计算丢包率
        /// </summary>
        /// <returns>丢包率（百分比）</returns>
        private float CalculatePacketLoss()
        {
            if (_totalMessagesSent == 0)
            {
                return 0f;
            }
            
            var lossRate = (float)_failedMessagesSent / _totalMessagesSent * 100f;
            
            // 更新丢包率历史
            UpdatePacketLossHistory(lossRate);
            
            return lossRate;
        }
        
        /// <summary>
        /// 计算连接稳定性
        /// </summary>
        /// <returns>稳定性分数（0-100）</returns>
        private int CalculateStability()
        {
            if (_heartbeatCount == 0)
            {
                return 100; // 刚开始时认为是稳定的
            }
            
            // 基于心跳丢失率计算稳定性
            var heartbeatLossRate = (float)_missedHeartbeats / _heartbeatCount;
            var stability = (int)((1f - heartbeatLossRate) * 100f);
            
            // 基于延迟变化计算稳定性
            if (_latencyHistory.Count > 1)
            {
                var latencyVariance = CalculateLatencyVariance();
                var latencyStability = Math.Max(0, 100 - (int)(latencyVariance / 10)); // 延迟变化越大，稳定性越低
                stability = (stability + latencyStability) / 2;
            }
            
            return Math.Max(0, Math.Min(100, stability));
        }
        
        /// <summary>
        /// 计算延迟方差
        /// </summary>
        /// <returns>延迟方差</returns>
        private double CalculateLatencyVariance()
        {
            if (_latencyHistory.Count < 2)
            {
                return 0;
            }
            
            var average = _latencyHistory.Average();
            var variance = _latencyHistory.Select(x => Math.Pow(x - average, 2)).Average();
            
            return Math.Sqrt(variance);
        }
        
        /// <summary>
        /// 计算总体质量分数
        /// </summary>
        /// <param name="quality">质量数据</param>
        /// <returns>总体分数（0-100）</returns>
        private int CalculateOverallScore(ConnectionQuality quality)
        {
            // 延迟分数（权重40%）
            var latencyScore = CalculateLatencyScore(quality.Latency);
            
            // 丢包率分数（权重30%）
            var packetLossScore = CalculatePacketLossScore(quality.PacketLoss);
            
            // 稳定性分数（权重30%）
            var stabilityScore = quality.Stability;
            
            // 加权计算总分
            var overallScore = (int)(latencyScore * 0.4f + packetLossScore * 0.3f + stabilityScore * 0.3f);
            
            return Math.Max(0, Math.Min(100, overallScore));
        }
        
        /// <summary>
        /// 计算延迟分数
        /// </summary>
        /// <param name="latency">延迟（毫秒）</param>
        /// <returns>延迟分数（0-100）</returns>
        private int CalculateLatencyScore(int latency)
        {
            if (latency <= 50)
                return 100;
            else if (latency <= 100)
                return 90;
            else if (latency <= 150)
                return 75;
            else if (latency <= 200)
                return 60;
            else if (latency <= 300)
                return 40;
            else if (latency <= 500)
                return 20;
            else
                return 0;
        }
        
        /// <summary>
        /// 计算丢包率分数
        /// </summary>
        /// <param name="packetLoss">丢包率（百分比）</param>
        /// <returns>丢包率分数（0-100）</returns>
        private int CalculatePacketLossScore(float packetLoss)
        {
            if (packetLoss <= 0.1f)
                return 100;
            else if (packetLoss <= 0.5f)
                return 90;
            else if (packetLoss <= 1.0f)
                return 75;
            else if (packetLoss <= 2.0f)
                return 60;
            else if (packetLoss <= 5.0f)
                return 40;
            else if (packetLoss <= 10.0f)
                return 20;
            else
                return 0;
        }
        
        /// <summary>
        /// 确定质量等级
        /// </summary>
        /// <param name="score">质量分数</param>
        /// <returns>质量等级</returns>
        private ConnectionQualityLevel DetermineQualityLevel(int score)
        {
            if (score >= 90)
                return ConnectionQualityLevel.Excellent;
            else if (score >= 75)
                return ConnectionQualityLevel.Good;
            else if (score >= 60)
                return ConnectionQualityLevel.Fair;
            else if (score >= 40)
                return ConnectionQualityLevel.Poor;
            else
                return ConnectionQualityLevel.VeryPoor;
        }
        
        #endregion
        
        #region 历史记录管理
        
        /// <summary>
        /// 更新延迟历史
        /// </summary>
        /// <param name="latency">延迟值</param>
        private void UpdateLatencyHistory(int latency)
        {
            _latencyHistory.Enqueue(latency);
            
            while (_latencyHistory.Count > MAX_LATENCY_HISTORY)
            {
                _latencyHistory.Dequeue();
            }
        }
        
        /// <summary>
        /// 更新丢包率历史
        /// </summary>
        /// <param name="packetLoss">丢包率</param>
        private void UpdatePacketLossHistory(float packetLoss)
        {
            _packetLossHistory.Enqueue(packetLoss);
            
            while (_packetLossHistory.Count > MAX_PACKET_LOSS_HISTORY)
            {
                _packetLossHistory.Dequeue();
            }
        }
        
        /// <summary>
        /// 添加到质量历史
        /// </summary>
        /// <param name="quality">质量数据</param>
        private void AddToQualityHistory(ConnectionQuality quality)
        {
            _qualityHistory.Enqueue(quality);
            
            while (_qualityHistory.Count > MAX_QUALITY_HISTORY)
            {
                _qualityHistory.Dequeue();
            }
        }
        
        #endregion
        
        #region 统计更新方法
        
        /// <summary>
        /// 更新心跳统计
        /// </summary>
        public void UpdateHeartbeat()
        {
            if (!IsMonitoring)
                return;
            
            _heartbeatCount++;
            _lastHeartbeatTime = DateTime.UtcNow;
        }
        
        /// <summary>
        /// 记录心跳丢失
        /// </summary>
        public void RecordMissedHeartbeat()
        {
            if (!IsMonitoring)
                return;
            
            _missedHeartbeats++;
        }
        
        /// <summary>
        /// 记录消息发送
        /// </summary>
        /// <param name="success">是否发送成功</param>
        public void RecordMessageSent(bool success)
        {
            if (!IsMonitoring)
                return;
            
            _totalMessagesSent++;
            
            if (!success)
            {
                _failedMessagesSent++;
            }
        }
        
        /// <summary>
        /// 记录消息接收
        /// </summary>
        public void RecordMessageReceived()
        {
            if (!IsMonitoring)
                return;
            
            _totalMessagesReceived++;
        }
        
        #endregion
        
        #region 质量检查
        
        /// <summary>
        /// 检查是否有显著的质量变化
        /// </summary>
        /// <param name="oldQuality">旧质量数据</param>
        /// <param name="newQuality">新质量数据</param>
        /// <returns>是否有显著变化</returns>
        private bool HasSignificantQualityChange(ConnectionQuality oldQuality, ConnectionQuality newQuality)
        {
            if (oldQuality == null)
                return true;
            
            // 质量等级变化
            if (oldQuality.QualityLevel != newQuality.QualityLevel)
                return true;
            
            // 总体分数变化超过10分
            if (Math.Abs(oldQuality.OverallScore - newQuality.OverallScore) >= 10)
                return true;
            
            return false;
        }
        
        /// <summary>
        /// 检查质量警告
        /// </summary>
        /// <param name="quality">质量数据</param>
        private void CheckForQualityWarnings(ConnectionQuality quality)
        {
            // 高延迟警告
            if (quality.Latency > 300)
            {
                OnQualityWarning?.Invoke(new ConnectionQualityWarning
                {
                    Type = QualityWarningType.HighLatency,
                    Message = $"连接延迟过高: {quality.Latency}ms",
                    Severity = quality.Latency > 500 ? WarningSeverity.Critical : WarningSeverity.Warning
                });
            }
            
            // 高丢包率警告
            if (quality.PacketLoss > 2.0f)
            {
                OnQualityWarning?.Invoke(new ConnectionQualityWarning
                {
                    Type = QualityWarningType.HighPacketLoss,
                    Message = $"丢包率过高: {quality.PacketLoss:F1}%",
                    Severity = quality.PacketLoss > 5.0f ? WarningSeverity.Critical : WarningSeverity.Warning
                });
            }
            
            // 低稳定性警告
            if (quality.Stability < 60)
            {
                OnQualityWarning?.Invoke(new ConnectionQualityWarning
                {
                    Type = QualityWarningType.LowStability,
                    Message = $"连接稳定性较低: {quality.Stability}%",
                    Severity = quality.Stability < 40 ? WarningSeverity.Critical : WarningSeverity.Warning
                });
            }
        }
        
        #endregion
        
        #region 公共查询方法
        
        /// <summary>
        /// 获取质量历史记录
        /// </summary>
        /// <returns>质量历史记录</returns>
        public List<ConnectionQuality> GetQualityHistory()
        {
            return _qualityHistory.ToList();
        }
        
        /// <summary>
        /// 获取延迟历史记录
        /// </summary>
        /// <returns>延迟历史记录</returns>
        public List<int> GetLatencyHistory()
        {
            return _latencyHistory.ToList();
        }
        
        /// <summary>
        /// 获取监控统计信息
        /// </summary>
        /// <returns>监控统计信息</returns>
        public ConnectionMonitoringStats GetMonitoringStats()
        {
            return new ConnectionMonitoringStats
            {
                MonitoringDuration = IsMonitoring ? DateTime.UtcNow - _monitoringStartTime : TimeSpan.Zero,
                TotalHeartbeats = _heartbeatCount,
                MissedHeartbeats = _missedHeartbeats,
                TotalMessagesSent = _totalMessagesSent,
                TotalMessagesReceived = _totalMessagesReceived,
                FailedMessagesSent = _failedMessagesSent,
                LastHeartbeatTime = _lastHeartbeatTime
            };
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
                    StopMonitoring();
                    _qualityHistory.Clear();
                    _latencyHistory.Clear();
                    _packetLossHistory.Clear();
                }
                
                _disposed = true;
            }
        }
        
        /// <summary>
        /// 析构函数
        /// </summary>
        ~ConnectionQualityMonitor()
        {
            Dispose(false);
        }
        
        #endregion
    }
}