// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using EscapeFromDuckovCoopMod.Utils;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// SortedList 扩展方法（参考 Fika）
/// </summary>
public static class SortedListExtensions
{
    /// <summary>
    /// 从开头移除指定数量的元素
    /// </summary>
    public static void RemoveRange<T, U>(this SortedList<T, U> list, int amount)
    {
        for (int i = 0; i < amount && i < list.Count; ++i)
        {
            list.RemoveAt(0);
        }
    }
}

public class NetInterpolator : MonoBehaviour
{
    [Tooltip("渲染回看时间；越大越稳，越小越跟手")] public float interpolationBackTime = 0.12f;

    [Tooltip("缺帧时最多允许预测多久")] public float maxExtrapolate = 0.05f;

    [Tooltip("误差过大时直接硬对齐距离")] public float hardSnapDistance = 6f; // 6 米 Sans看不懂就给设置个Tooltip

    [Tooltip("位置平滑插值的瞬时权重")] public float posLerpFactor = 0.9f;

    [Tooltip("朝向平滑插值的瞬时权重")] public float rotLerpFactor = 0.9f;

    [Header("跑步反超护栏")] public bool extrapolateWhenRunning; // 跑步默认禁用预测

    public float runSpeedThreshold = 3.0f; // 认为 >3 m/s 为跑步

    [Header("EMA 平滑配置 (高级)")]
    [Tooltip("启用 EMA 平滑网络抖动")]
    public bool enableEmaSmoothing = true;

    [Tooltip("动态调整延迟（根据网络质量自动优化）")]
    public bool enableDynamicDelay = true;

    [Tooltip("最小延迟时间（秒）")]
    public float minInterpolationDelay = 0.05f;

    [Tooltip("最大延迟时间（秒）")]
    public float maxInterpolationDelay = 0.25f;

    [Tooltip("延迟安全倍数（增加此值可提高稳定性但增加延迟）")]
    public float delaySafetyMultiplier = 2.0f;

    [Header("网络配置")]
    [Tooltip("服务器发送频率 (Hz)，用于计算追赶/减速阈值")]
    public int sendRate = 60;

    [Header("追赶/减速配置 (高级)")]
    [Tooltip("启用自动追赶/减速机制（推荐）")]
    public bool enableCatchupSlowdown = true;

    [Tooltip("追赶加速比例（0-1，推荐0.05 = 5%加速）")]
    [Range(0f, 0.5f)]
    public float catchupSpeed = 0.05f;

    [Tooltip("减速比例（0-1，推荐0.04 = 4%减速）")]
    [Range(0f, 0.5f)]
    public float slowdownSpeed = 0.04f;

    [Tooltip("追赶阈值（帧数倍数，推荐1.0，即落后1帧时开始追赶）")]
    public float catchupPositiveThreshold = 1.0f;

    [Tooltip("减速阈值（帧数倍数，推荐-1.0，即超前1帧时开始减速）")]
    public float catchupNegativeThreshold = -1.0f;

    [Header("调试信息")]
    [Tooltip("显示插值调试信息")]
    public bool showDebugInfo = false;

    [Tooltip("启用二分查找优化（推荐）")]
    public bool enableBinarySearch = true;

    private readonly SortedList<double, Snap> _buf = new(64); // 改用 SortedList 自动排序
    private readonly object _bufferLock = new();               // 线程锁（保护缓冲区访问）
    private Vector3 _lastVel = Vector3.zero;
    private Transform modelRoot; // 驱动朝向
    private Transform root; // 驱动位置

    // EMA 追踪器
    private ExponentialMovingAverage _delayEma = new(60);           // 追踪包到达延迟（1秒窗口）
    private ExponentialMovingAverage _intervalEma = new(60);        // 追踪包间隔时间（1秒窗口）
    private ExponentialMovingAverage _driftEma = new(60);           // 追踪时间漂移（用于追赶/减速）
    private ExponentialMovingAverage _deliveryTimeEma = new(120);   // 追踪包到达时间差（2秒窗口，用于动态调整）
    private double _lastPushTime = -1;                              // 上次 Push 的时间
    private double _baseInterpolationDelay;                         // 基础延迟（初始化时设置）

    // 时间轴系统
    private double _localTimeline = 0;                           // 本地播放时间轴
    private double _localTimeScale = 1.0;                        // 时间缩放（1.0=正常，>1追赶，<1减速）
    private bool _timelineInitialized = false;                   // 时间轴是否已初始化

    // 属性
    private float SendInterval => 1f / Mathf.Max(1, sendRate);   // 发送间隔（秒）

    private void LateUpdate()
    {
        // 懒初始化（有些对象刚克隆完组件还没取到）
        if (!root)
        {
            var cmc = GetComponentInChildren<CharacterMainControl>();
            if (cmc)
            {
                root = cmc.transform;
                modelRoot = cmc.modelRoot ? cmc.modelRoot.transform : cmc.transform;
            }
            else
            {
                root = transform;
            }
        }

        if (!modelRoot) modelRoot = root;

        // 线程安全：锁定缓冲区读取
        lock (_bufferLock)
        {
            if (_buf.Count == 0) return;

            // === 时间轴系统：前进本地时间轴 ===
            if (enableCatchupSlowdown && _timelineInitialized)
            {
                // 使用动态时间缩放前进时间轴
                _localTimeline += Time.unscaledDeltaTime * _localTimeScale;
            }
            else
            {
                // 传统模式：使用固定回退时间
                _localTimeline = Time.unscaledTimeAsDouble - interpolationBackTime;
            }

            double renderT = _localTimeline;

            // 找到 [i-1, i] 包围 renderT 的两个样本
            int i;
            if (enableBinarySearch && _buf.Count > 10)
            {
                // 二分查找（O(log n)）- 适合大缓冲区
                i = BinarySearchSnapshot(renderT);
            }
            else
            {
                // 线性查找（O(n)）- 适合小缓冲区
                i = 0;
                var keys = _buf.Keys;
                while (i < _buf.Count && keys[i] < renderT) i++;
            }

            if (i == 0)
            {
                // 数据太新：直接用第一帧（刚开始的 100ms 内）
                var first = _buf.Values[0];
                Apply(first.pos, first.rot, true);
                return;
            }

            if (i < _buf.Count)
            {
                // 插值
                var a = _buf.Values[i - 1];
                var b = _buf.Values[i];
                var t = (float)((renderT - a.t) / Math.Max(1e-6, b.t - a.t));
                var pos = Vector3.LerpUnclamped(a.pos, b.pos, t);
                var rot = Quaternion.Slerp(a.rot, b.rot, t);
                Apply(pos, rot);

                // 调试信息
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[NetInterp] 时间轴={_localTimeline:F3}s, " +
                        $"时间缩放={_localTimeScale:F3}x, " +
                        $"插值进度={t:F2}, " +
                        $"缓冲区={_buf.Count}, " +
                        $"查找方式={(enableBinarySearch && _buf.Count > 10 ? "二分" : "线性")}");
                }

                // 适度回收旧帧（保留一帧冗余，遇到回退也能抗一下）
                if (i > 1)
                {
                    _buf.RemoveRange(i - 1);
                }
            }
            else
            {
                // 数据不足：使用预测或停留在最后一帧
                var last = _buf.Values[_buf.Count - 1];
                var dt = renderT - last.t;

                // 是否允许本帧预测
                var allow = dt <= maxExtrapolate;
                if (!extrapolateWhenRunning)
                {
                    var speed = _lastVel.magnitude;
                    if (speed > runSpeedThreshold) allow = false; // 跑步：禁用预测，避免超前后拉
                }

                if (allow)
                    Apply(last.pos + _lastVel * (float)dt, last.rot);
                else
                    Apply(last.pos, last.rot);

                // 清理旧快照（保留最后2帧）
                if (_buf.Count > 2)
                {
                    _buf.RemoveRange(_buf.Count - 2);
                }
            }
        } // lock (_bufferLock)
    }

    /// <summary>
    /// 二分查找：找到第一个时间戳 >= targetTime 的快照索引
    /// </summary>
    private int BinarySearchSnapshot(double targetTime)
    {
        var keys = _buf.Keys;
        int left = 0;
        int right = _buf.Count - 1;
        int result = _buf.Count; // 默认返回末尾

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (keys[mid] < targetTime)
            {
                left = mid + 1;
            }
            else
            {
                result = mid;
                right = mid - 1;
            }
        }

        return result;
    }

    public void Init(Transform rootT, Transform modelRootT)
    {
        root = rootT;
        modelRoot = modelRootT ? modelRootT : rootT;
    }

    // 喂一帧快照；when<0 则取到达时刻
    /// <summary>
    /// 推送新的位置/旋转快照到插值缓冲区
    /// 完全按照 Fika 的 InsertAndAdjust 逻辑实现
    /// </summary>
    public void Push(Vector3 pos, Quaternion rot, double when = -1)
    {
        double now = Time.unscaledTimeAsDouble;
        if (when < 0) when = now;

        lock (_bufferLock) // 线程安全：保护缓冲区操作
        {
            // === 异常跳变检测 ===
            if (_buf.Count > 0)
            {
                var prev = _buf.Values[_buf.Count - 1];
                var dt = when - prev.t;
                if (dt > 1e-6) _lastVel = (pos - prev.pos) / (float)dt;

                // 异常跳变：清空缓冲
                float distSqr = (pos - prev.pos).sqrMagnitude;
                if (distSqr > hardSnapDistance * hardSnapDistance)
                {
                    if (showDebugInfo)
                    {
                        Debug.LogWarning($"[NetInterp] 异常跳变: {Mathf.Sqrt(distSqr):F2}m, 清空缓冲");
                    }
                    _buf.Clear();
                    _delayEma.Reset();
                    _intervalEma.Reset();
                    _driftEma.Reset();
                    _deliveryTimeEma.Reset();
                    _timelineInitialized = false;
                    _localTimeScale = 1.0;
                }
            }

            // === Fika 的 InsertAndAdjust 完整逻辑 ===

            // 1. 首次初始化时间轴
            if (_buf.Count == 0)
            {
                _localTimeline = when - interpolationBackTime;
                _timelineInitialized = true;
            }

            // 2. 插入快照（避免重复）
            int beforeCount = _buf.Count;
            var newSnap = new Snap { t = when, localTime = now, pos = pos, rot = rot };

            // 限制缓冲区大小
            if (_buf.Count >= 64)
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning($"[NetInterp] 缓冲区已满(64)，清空");
                }
                _buf.Clear();
                _localTimeline = when - interpolationBackTime;
            }

            _buf[when] = newSnap;
            bool wasInserted = _buf.Count > beforeCount;

            // 3. 如果成功插入，进行调整
            if (wasInserted && _buf.Count >= 2)
            {
                // === 计算 deliveryTime（包到达时间差）===
                var secondLast = _buf.Values[_buf.Count - 2];
                var latest = _buf.Values[_buf.Count - 1];

                double localDeliveryTime = latest.localTime - secondLast.localTime;
                _deliveryTimeEma.Add(localDeliveryTime);

                // === 动态调整缓冲倍数（基于 jitter）===
                if (enableDynamicDelay && _deliveryTimeEma.IsInitialized)
                {
                    // Fika 的动态调整公式：
                    // bufferTimeMultiplier = (sendInterval + jitterStdDev) / sendInterval + tolerance
                    double jitterStdDev = _deliveryTimeEma.StandardDeviation;
                    double dynamicMultiplier = ((SendInterval + jitterStdDev) / SendInterval) +
                                              delaySafetyMultiplier; // tolerance

                    // 限制在合理范围（0-5倍）
                    dynamicMultiplier = Math.Clamp(dynamicMultiplier, 0, 5);

                    // 更新插值延迟
                    float oldDelay = interpolationBackTime;
                    interpolationBackTime = (float)(SendInterval * dynamicMultiplier);
                    interpolationBackTime = Math.Clamp(interpolationBackTime,
                        minInterpolationDelay, maxInterpolationDelay);

                    if (showDebugInfo && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"[NetInterp] 动态调整: jitter={jitterStdDev:F4}s, " +
                            $"倍数={dynamicMultiplier:F2}x, " +
                            $"延迟: {oldDelay:F3}s→{interpolationBackTime:F3}s");
                    }
                }

                // === TimelineClamp: 防止时间轴偏离过远 ===
                double latestRemoteTime = when;
                double bufferTime = interpolationBackTime;
                double targetTime = latestRemoteTime - bufferTime;
                double lowerBound = targetTime - bufferTime;
                double upperBound = targetTime + bufferTime;
                _localTimeline = Math.Clamp(_localTimeline, lowerBound, upperBound);

                // === 计算 drift 并调整时间缩放（Fika 方式）===
                double timeDiff = latestRemoteTime - _localTimeline;
                _driftEma.Add(timeDiff);

                // Fika 的 drift 计算：EMA(timeDiff) - bufferTime
                double drift = _driftEma.Value - bufferTime;

                // 计算绝对阈值（以发送间隔为单位）
                double absoluteNegativeThreshold = SendInterval * catchupNegativeThreshold;
                double absolutePositiveThreshold = SendInterval * catchupPositiveThreshold;

                // Fika 的 Timescale 逻辑
                if (drift > absolutePositiveThreshold)
                {
                    _localTimeScale = 1.0 + catchupSpeed; // 追赶
                }
                else if (drift < absoluteNegativeThreshold)
                {
                    _localTimeScale = 1.0 - slowdownSpeed; // 减速
                }
                else
                {
                    _localTimeScale = 1.0; // 正常
                }

                // 调试输出
                if (showDebugInfo && Time.frameCount % 60 == 0)
                {
                    string mode = _localTimeScale > 1.0 ? "追赶" :
                                 _localTimeScale < 1.0 ? "减速" : "正常";
                    Debug.Log($"[NetInterp] {mode}: drift={drift:F4}s, " +
                        $"timeDiff={timeDiff:F4}s, " +
                        $"缩放={_localTimeScale:F3}x, " +
                        $"缓冲={_buf.Count}");
                }
            }
        } // lock (_bufferLock)
    }

    /// <summary>
    /// 获取网络质量统计信息（用于调试和监控）
    /// </summary>
    public NetworkQualityStats GetNetworkStats()
    {
        return new NetworkQualityStats
        {
            AverageDelay = _delayEma.Value,
            DelayStandardDeviation = _delayEma.StandardDeviation,
            AveragePacketInterval = _intervalEma.Value,
            PacketIntervalJitter = _intervalEma.StandardDeviation,
            CurrentInterpolationDelay = interpolationBackTime,
            BufferSize = _buf.Count,
            IsEmaInitialized = _delayEma.IsInitialized && _intervalEma.IsInitialized,
            // 时间轴统计
            LocalTimeline = _localTimeline,
            LocalTimeScale = _localTimeScale,
            Drift = _driftEma.Value,
            DriftStandardDeviation = _driftEma.StandardDeviation,
            IsTimelineInitialized = _timelineInitialized,
            // Fika 新增统计
            AverageDeliveryTime = _deliveryTimeEma.Value,
            DeliveryTimeJitter = _deliveryTimeEma.StandardDeviation
        };
    }

    /// <summary>
    /// 网络质量统计数据
    /// </summary>
    public struct NetworkQualityStats
    {
        public double AverageDelay;              // 平均延迟（秒）
        public double DelayStandardDeviation;    // 延迟标准差
        public double AveragePacketInterval;     // 平均包间隔（秒）
        public double PacketIntervalJitter;      // 包间隔抖动（秒）
        public float CurrentInterpolationDelay;  // 当前插值延迟（秒）
        public int BufferSize;                   // 缓冲区大小
        public bool IsEmaInitialized;            // EMA 是否已初始化

        // 时间轴统计
        public double LocalTimeline;             // 本地时间轴位置
        public double LocalTimeScale;            // 当前时间缩放
        public double Drift;                     // 时间漂移（秒）
        public double DriftStandardDeviation;    // 漂移标准差
        public bool IsTimelineInitialized;       // 时间轴是否已初始化

        // Fika 新增统计
        public double AverageDeliveryTime;       // 平均包到达时间间隔（用于动态调整）
        public double DeliveryTimeJitter;        // 包到达时间抖动（标准差）

        /// <summary>
        /// 获取网络质量评分（0-100，越高越好）
        /// </summary>
        public readonly int GetQualityScore()
        {
            if (!IsEmaInitialized) return 0;

            // 基于延迟和抖动计算质量分数
            double latencyScore = Math.Max(0, 100 - AverageDelay * 1000); // 延迟越低越好
            double jitterScore = Math.Max(0, 100 - PacketIntervalJitter * 2000); // 抖动越低越好
            double driftScore = Math.Max(0, 100 - Math.Abs(Drift) * 500); // 漂移越小越好

            return (int)((latencyScore + jitterScore + driftScore) / 3);
        }

        /// <summary>
        /// 获取网络状态描述
        /// </summary>
        public readonly string GetStatusDescription()
        {
            int score = GetQualityScore();
            if (score >= 80) return "优秀";
            if (score >= 60) return "良好";
            if (score >= 40) return "一般";
            if (score >= 20) return "较差";
            return "很差";
        }
    }

    private void Apply(Vector3 pos, Quaternion rot, bool hardSnap = false)
    {
        if (!root) return;

        // 误差特别大直接硬对齐，避免"橡皮筋"Sans说的回弹
        if (hardSnap || (root.position - pos).sqrMagnitude > hardSnapDistance * hardSnapDistance)
        {
            root.SetPositionAndRotation(pos, rot);
            if (modelRoot && modelRoot != root) modelRoot.rotation = rot;
            return;
        }

        // === 优化：直接应用插值结果，避免双重平滑 ===
        // 在新的时间轴系统中，我们已经在 LateUpdate 中做了精确的插值
        // 不需要再次 Lerp，否则会增加额外延迟
        if (enableCatchupSlowdown && _timelineInitialized)
        {
            // 时间轴模式：直接应用（已经在 LateUpdate 中插值过了）
            root.position = pos;
            if (modelRoot)
                modelRoot.rotation = rot;
            else
                root.rotation = rot;
        }
        else
        {
            // 传统模式：保留原有的平滑逻辑（兼容旧行为）
            root.position = Vector3.Lerp(root.position, pos, posLerpFactor);
            if (modelRoot)
                modelRoot.rotation = Quaternion.Slerp(modelRoot.rotation, rot, rotLerpFactor);
        }
    }

    private struct Snap
    {
        public double t;           // RemoteTime - 服务器发送时的时间戳
        public double localTime;   // LocalTime - 本地收到的时间戳（用于计算延迟）
        public Vector3 pos;
        public Quaternion rot;
    }
}

// 便捷：确保挂载并初始化
public static class NetInterpUtil
{
    public static NetInterpolator Attach(GameObject go)
    {
        if (!go) return null;
        var ni = go.GetComponent<NetInterpolator>();
        if (!ni) ni = go.AddComponent<NetInterpolator>();
        var cmc = go.GetComponent<CharacterMainControl>();
        if (cmc) ni.Init(cmc.transform, cmc.modelRoot ? cmc.modelRoot.transform : cmc.transform);
        return ni;
    }
}