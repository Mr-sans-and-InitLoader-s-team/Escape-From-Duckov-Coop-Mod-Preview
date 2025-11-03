using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Chat.Network
{
    /// <summary>
    /// 直连网络监控器
    /// 监控网络连接质量和状态
    /// </summary>
    public class DirectNetworkMonitor : IDisposable
    {
        #region 常量定义

        /// <summary>
        /// 监控间隔（毫秒）
        /// </summary>
        private const int MONITOR_INTERVAL_MS = 10000; // 10秒

        /// <summary>
        /// 质量历史记录数量
        /// </summary>
        private const int QUALITY_HISTORY_SIZE = 10;

        /// <summary>
        /// Ping 超时时间（毫秒）
        /// </summary>
        private const int PING_TIMEOUT_MS = 3000;

        #endregion

        #region 字段和属性

        /// <summary>
        /// 当前连接质量
        /// </summary>
        public ConnectionQuality CurrentQuality { get; private set; }

        /// <summary>
        /// 是否正在监控
        /// </summary>
        public bool IsMonitoring { get; private set; }

        /// <summary>
        /// 目标主机地址
        /// </summary>
        private string _targetHost;

        /// <summary>
        /// 监控定时器
        /// </summary>
        private Timer _monitorTimer;

        /// <summary>
        /// 质量历史记录
        /// </summary>
        private readonly Queue<ConnectionQuality> _qualityHistory = new Queue<ConnectionQuality>();



        /// <summary>
        /// 是否已释放
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 事件

        /// <summary>
        /// 连接质量变化事件
        /// </summary>
        public event Action<ConnectionQuality> OnQualityChanged;

        /// <summary>
        /// 网络状态变化事件
        /// </summary>
        public event Action<DirectNetworkStatus> OnNetworkStatusChanged;

        #endregion

        #region 构造函数和初始化

        /// <summary>
        /// 构造函数
        /// </summary>
        public DirectNetworkMonitor()
        {
            CurrentQuality = new ConnectionQuality();
        }

        #endregion

        #region 监控控制

        /// <summary>
        /// 开始监控
        /// </summary>
        /// <param name="targetHost">目标主机地址（可选）</param>
        public void StartMonitoring(string targetHost = null)
        {
            if (IsMonitoring)
            {
                LogWarning("网络监控已在运行");
                return;
            }

            try
            {
                _targetHost = targetHost;
                IsMonitoring = true;

                // 启动监控定时器
                _monitorTimer = new Timer(MonitorNetwork, null, 0, MONITOR_INTERVAL_MS);

                LogInfo($"网络监控已启动，目标主机: {_targetHost ?? "无"}");
            }
            catch (Exception ex)
            {
                LogError($"启动网络监控时发生异常: {ex.Message}");
                IsMonitoring = false;
            }
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

            try
            {
                IsMonitoring = false;
                _monitorTimer?.Dispose();
                _monitorTimer = null;

                LogInfo("网络监控已停止");
            }
            catch (Exception ex)
            {
                LogError($"停止网络监控时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置目标主机
        /// </summary>
        /// <param name="targetHost">目标主机地址</param>
        public void SetTargetHost(string targetHost)
        {
            _targetHost = targetHost;
            LogInfo($"目标主机已更新: {_targetHost}");
        }

        #endregion

        #region 质量监控

        /// <summary>
        /// 监控网络（定时器回调）
        /// </summary>
        /// <param name="state">定时器状态</param>
        private void MonitorNetwork(object state)
        {
            if (!IsMonitoring || _disposed)
            {
                return;
            }

            try
            {
                _ = Task.Run(async () =>
                {
                    var quality = await MeasureConnectionQuality();
                    UpdateQuality(quality);
                });
            }
            catch (Exception ex)
            {
                LogError($"监控网络时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 测量连接质量
        /// </summary>
        /// <returns>连接质量</returns>
        private async Task<ConnectionQuality> MeasureConnectionQuality()
        {
            var quality = new ConnectionQuality
            {
                Timestamp = DateTime.UtcNow
            };

            try
            {
                // 如果有目标主机，测量延迟
                if (!string.IsNullOrEmpty(_targetHost))
                {
                    await MeasureLatency(quality, _targetHost);
                }

                // 计算总体质量分数和等级
                quality.OverallScore = CalculateQualityScore(quality);
                quality.QualityLevel = DetermineQualityLevel(quality.OverallScore);

                LogDebug($"连接质量测量完成: {quality}");
            }
            catch (Exception ex)
            {
                LogError($"测量连接质量时发生异常: {ex.Message}");
                quality.OverallScore = 0;
                quality.QualityLevel = ConnectionQualityLevel.VeryPoor;
            }

            return quality;
        }



        /// <summary>
        /// 测量延迟
        /// </summary>
        /// <param name="quality">连接质量对象</param>
        /// <param name="targetHost">目标主机</param>
        private async Task MeasureLatency(ConnectionQuality quality, string targetHost)
        {
            try
            {
                using (var ping = new System.Net.NetworkInformation.Ping())
                {
                    var latencies = new List<long>();
                    int successCount = 0;
                    const int pingCount = 3;

                    for (int i = 0; i < pingCount; i++)
                    {
                        try
                        {
                            var reply = await ping.SendPingAsync(targetHost, PING_TIMEOUT_MS);
                            
                            if (reply.Status == IPStatus.Success)
                            {
                                latencies.Add(reply.RoundtripTime);
                                successCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Ping失败: {ex.Message}");
                        }

                        if (i < pingCount - 1)
                        {
                            await Task.Delay(100); // 间隔100ms
                        }
                    }

                    if (latencies.Count > 0)
                    {
                        quality.Latency = (int)latencies.Average();
                        quality.AverageLatency = quality.Latency;
                    }
                    else
                    {
                        quality.Latency = -1;
                        quality.AverageLatency = -1;
                    }

                    quality.PacketLoss = ((float)(pingCount - successCount) / pingCount) * 100;
                }
            }
            catch (Exception ex)
            {
                LogError($"测量延迟时发生异常: {ex.Message}");
                quality.Latency = -1;
                quality.AverageLatency = -1;
                quality.PacketLoss = 100;
            }
        }

        /// <summary>
        /// 更新质量信息
        /// </summary>
        /// <param name="newQuality">新的质量信息</param>
        private void UpdateQuality(ConnectionQuality newQuality)
        {
            try
            {
                var oldQuality = CurrentQuality;
                CurrentQuality = newQuality;

                // 添加到历史记录
                _qualityHistory.Enqueue(newQuality);
                if (_qualityHistory.Count > QUALITY_HISTORY_SIZE)
                {
                    _qualityHistory.Dequeue();
                }

                // 检查是否有显著变化
                if (HasSignificantChange(oldQuality, newQuality))
                {
                    OnQualityChanged?.Invoke(newQuality);
                }

                // 检查网络状态变化
                var networkStatus = DetermineNetworkStatus(newQuality);
                OnNetworkStatusChanged?.Invoke(networkStatus);
            }
            catch (Exception ex)
            {
                LogError($"更新质量信息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否有显著变化
        /// </summary>
        /// <param name="oldQuality">旧质量</param>
        /// <param name="newQuality">新质量</param>
        /// <returns>是否有显著变化</returns>
        private bool HasSignificantChange(ConnectionQuality oldQuality, ConnectionQuality newQuality)
        {
            if (oldQuality == null)
            {
                return true;
            }

            // 质量等级变化
            if (oldQuality.QualityLevel != newQuality.QualityLevel)
            {
                return true;
            }

            // 质量分数变化超过10分
            if (Math.Abs(oldQuality.OverallScore - newQuality.OverallScore) >= 10)
            {
                return true;
            }

            // 延迟变化超过50ms
            if (Math.Abs(oldQuality.Latency - newQuality.Latency) >= 50)
            {
                return true;
            }

            // 丢包率变化超过5%
            if (Math.Abs(oldQuality.PacketLoss - newQuality.PacketLoss) >= 5)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 确定网络状态
        /// </summary>
        /// <param name="quality">连接质量</param>
        /// <returns>网络状态</returns>
        private DirectNetworkStatus DetermineNetworkStatus(ConnectionQuality quality)
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return DirectNetworkStatus.Unavailable;
            }

            if (quality.Latency < 0)
            {
                return DirectNetworkStatus.Disconnected;
            }

            switch (quality.QualityLevel)
            {
                case ConnectionQualityLevel.Excellent:
                    return DirectNetworkStatus.Excellent;
                case ConnectionQualityLevel.Good:
                    return DirectNetworkStatus.Good;
                case ConnectionQualityLevel.Fair:
                    return DirectNetworkStatus.Fair;
                case ConnectionQualityLevel.Poor:
                    return DirectNetworkStatus.Poor;
                case ConnectionQualityLevel.VeryPoor:
                default:
                    return DirectNetworkStatus.Poor;
            }
        }

        /// <summary>
        /// 确定质量等级
        /// </summary>
        /// <param name="score">质量分数</param>
        /// <returns>质量等级</returns>
        private ConnectionQualityLevel DetermineQualityLevel(int score)
        {
            if (score >= 90)
            {
                return ConnectionQualityLevel.Excellent;
            }
            else if (score >= 70)
            {
                return ConnectionQualityLevel.Good;
            }
            else if (score >= 50)
            {
                return ConnectionQualityLevel.Fair;
            }
            else if (score >= 30)
            {
                return ConnectionQualityLevel.Poor;
            }
            else
            {
                return ConnectionQualityLevel.VeryPoor;
            }
        }

        /// <summary>
        /// 计算质量分数
        /// </summary>
        /// <param name="quality">连接质量</param>
        /// <returns>质量分数（0-100）</returns>
        private int CalculateQualityScore(ConnectionQuality quality)
        {
            if (!NetworkInterface.GetIsNetworkAvailable() || quality.Latency < 0)
            {
                return 0;
            }

            int score = 100;

            // 延迟扣分
            if (quality.Latency > 0)
            {
                if (quality.Latency > 200)
                {
                    score -= 40; // 高延迟严重扣分
                }
                else if (quality.Latency > 100)
                {
                    score -= 20; // 中等延迟中等扣分
                }
                else if (quality.Latency > 50)
                {
                    score -= 10; // 低延迟轻微扣分
                }
            }

            // 丢包扣分
            score -= (int)(quality.PacketLoss * 2); // 每1%丢包扣2分

            // 计算稳定性（基于历史数据）
            quality.Stability = CalculateStability();

            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>
        /// 计算连接稳定性
        /// </summary>
        /// <returns>稳定性分数（0-100）</returns>
        private int CalculateStability()
        {
            if (_qualityHistory.Count < 3)
            {
                return 100; // 数据不足，假设稳定
            }

            var recentQualities = _qualityHistory.TakeLast(5).ToList();
            var latencyVariance = CalculateVariance(recentQualities.Select(q => (double)q.Latency));
            var packetLossVariance = CalculateVariance(recentQualities.Select(q => (double)q.PacketLoss));

            // 基于方差计算稳定性
            int stability = 100;
            stability -= (int)(latencyVariance / 10); // 延迟方差影响
            stability -= (int)(packetLossVariance * 5); // 丢包方差影响

            return Math.Max(0, Math.Min(100, stability));
        }

        /// <summary>
        /// 计算方差
        /// </summary>
        /// <param name="values">数值序列</param>
        /// <returns>方差</returns>
        private double CalculateVariance(IEnumerable<double> values)
        {
            var valueList = values.ToList();
            if (valueList.Count < 2)
            {
                return 0;
            }

            var mean = valueList.Average();
            var variance = valueList.Sum(v => Math.Pow(v - mean, 2)) / valueList.Count;
            return variance;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取当前连接质量
        /// </summary>
        /// <returns>连接质量</returns>
        public ConnectionQuality GetCurrentQuality()
        {
            return CurrentQuality ?? new ConnectionQuality();
        }

        /// <summary>
        /// 获取质量历史记录
        /// </summary>
        /// <returns>质量历史记录</returns>
        public List<ConnectionQuality> GetQualityHistory()
        {
            return new List<ConnectionQuality>(_qualityHistory);
        }

        /// <summary>
        /// 获取平均质量分数
        /// </summary>
        /// <returns>平均质量分数</returns>
        public int GetAverageQualityScore()
        {
            if (_qualityHistory.Count == 0)
            {
                return 0;
            }

            return (int)_qualityHistory.Average(q => q.OverallScore);
        }

        #endregion

        #region 日志方法

        private void LogInfo(string message)
        {
            Debug.Log($"[DirectNetworkMonitor] {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[DirectNetworkMonitor] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[DirectNetworkMonitor] {message}");
        }

        private void LogDebug(string message)
        {
            Debug.Log($"[DirectNetworkMonitor][DEBUG] {message}");
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
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~DirectNetworkMonitor()
        {
            Dispose(false);
        }

        #endregion
    }



    /// <summary>
    /// 直连网络状态枚举
    /// </summary>
    public enum DirectNetworkStatus
    {
        /// <summary>
        /// 网络不可用
        /// </summary>
        Unavailable,

        /// <summary>
        /// 已断开连接
        /// </summary>
        Disconnected,

        /// <summary>
        /// 连接质量差
        /// </summary>
        Poor,

        /// <summary>
        /// 连接质量一般
        /// </summary>
        Fair,

        /// <summary>
        /// 连接质量良好
        /// </summary>
        Good,

        /// <summary>
        /// 连接质量优秀
        /// </summary>
        Excellent
    }
}