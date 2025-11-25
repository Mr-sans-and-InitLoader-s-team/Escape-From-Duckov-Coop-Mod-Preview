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

namespace EscapeFromDuckovCoopMod;

public class NetInterpolator : MonoBehaviour
{
    public const string MirrorVersionUsed = "73.3.0";

    [Tooltip("渲染回看时间；越大越稳，越小越跟手")] public float interpolationBackTime = 0.12f;

    [Tooltip("缺帧时最多允许预测多久")] public float maxExtrapolate = 0.05f;

    [Tooltip("误差过大时直接硬对齐距离")] public float hardSnapDistance = 6f; // 6 米 Sans看不懂就给设置个Tooltip

    [Tooltip("位置平滑插值的瞬时权重")] public float posLerpFactor = 0.9f;

    [Tooltip("朝向平滑插值的瞬时权重")] public float rotLerpFactor = 0.9f;

    [Header("跑步反超护栏")] public bool extrapolateWhenRunning; // 跑步默认禁用预测

    public float runSpeedThreshold = 3.0f; // 认为 >3 m/s 为跑步

    [Header("Mirror Snapshot Interpolation")]
    [Tooltip("Mirror SnapshotInterpolation 默认发送间隔（秒）")] public float sendInterval = 0.05f;

    [Tooltip("Mirror 缓冲倍数，用于计算回看窗口")]
    public float bufferTimeMultiplier = 2f;

    [Tooltip("Mirror 异步纠偏速率（越大越快贴合远端时间线）")]
    public float catchupSpeed = 0.05f;

    [Tooltip("Mirror 异步纠偏下限（秒）")] public float catchupNegativeThreshold = -0.1f;

    [Tooltip("Mirror 异步纠偏上限（秒）")] public float catchupPositiveThreshold = 0.1f;

    private readonly List<Snap> _buf = new(64);
    private Vector3 _lastVel = Vector3.zero;
    private double _timeOffset;
    private bool _offsetInitialized;
    private Transform modelRoot; // 驱动朝向

    private Transform root; // 驱动位置

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
        if (_buf.Count == 0) return;

        var localTime = Time.unscaledTimeAsDouble;
        var backTime = Math.Max(interpolationBackTime, sendInterval * bufferTimeMultiplier);

        // Mirror SnapshotInterpolation: 根据 drift 轻微调整 timeOffset，使本地时间线贴合远端
        if (_offsetInitialized)
        {
            var drift = localTime - (_buf[_buf.Count - 1].t + _timeOffset);
            if (drift < catchupNegativeThreshold || drift > catchupPositiveThreshold)
                _timeOffset += drift * catchupSpeed * Time.unscaledDeltaTime;
        }

        var renderT = localTime - _timeOffset - backTime;

        // 找到 [i-1, i] 包围 renderT 的两个样本
        var i = 0;
        while (i < _buf.Count && _buf[i].t < renderT) i++;

        if (i == 0)
        {
            // 数据太新：直接用第一帧（刚开始的 100ms 内）
            Apply(_buf[0].pos, _buf[0].rot, true);
            return;
        }

        if (i < _buf.Count)
        {
            // 插值
            var a = _buf[i - 1];
            var b = _buf[i];
            var t = (float)((renderT - a.t) / Math.Max(1e-6, b.t - a.t));
            var pos = Vector3.LerpUnclamped(a.pos, b.pos, t);
            var rot = Quaternion.Slerp(a.rot, b.rot, t);
            _lastVel = Vector3.LerpUnclamped(a.vel, b.vel, t);
            Apply(pos, rot);

            // 适度回收旧帧（保留一帧冗余，遇到回退也能抗一下）
            if (i > 1) _buf.RemoveRange(0, i - 1);
        }
        else
        {
            var last = _buf[_buf.Count - 1];
            var dt = renderT - last.t;

            // 是否允许本帧预测
            var allow = dt <= maxExtrapolate;
            if (!extrapolateWhenRunning)
            {
                var speed = _lastVel.magnitude;
                if (speed > runSpeedThreshold) allow = false; // 跑步：禁用预测，避免超前后拉
            }

            if (allow)
            {
                var vel = last.vel.sqrMagnitude > 0.0001f ? last.vel : _lastVel;
                Apply(last.pos + vel * (float)dt, last.rot);
                _lastVel = vel;
            }
            else
            {
                Apply(last.pos, last.rot);
                _lastVel = last.vel;
            }

            if (_buf.Count > 2) _buf.RemoveRange(0, _buf.Count - 2);
        }
    }

    public void Init(Transform rootT, Transform modelRootT)
    {
        root = rootT;
        modelRoot = modelRootT ? modelRootT : rootT;
    }

    // 喂一帧快照；when<0 则取到达时刻；velocity 可选用于预测
    public void Push(Vector3 pos, Quaternion rot, double when = -1, Vector3? velocity = null)
    {
        if (when < 0) when = Time.unscaledTimeAsDouble;
        if (!_offsetInitialized)
        {
            _timeOffset = Time.unscaledTimeAsDouble - when;
            _offsetInitialized = true;
        }
        else
        {
            var predictedArrival = when + _timeOffset;
            var drift = Time.unscaledTimeAsDouble - predictedArrival;
            if (drift < catchupNegativeThreshold || drift > catchupPositiveThreshold)
                _timeOffset += drift * catchupSpeed;
        }

        if (_buf.Count > 0)
        {
            var prev = _buf[_buf.Count - 1];
            var dt = when - prev.t;
            if (velocity.HasValue)
                _lastVel = velocity.Value;
            else if (dt > 1e-6)
                _lastVel = (pos - prev.pos) / (float)dt;

            // 若跨度太离谱，清空缓冲直接从新轨迹开始，避免长距离抖动
            if ((pos - prev.pos).sqrMagnitude > hardSnapDistance * hardSnapDistance)
                _buf.Clear();
        }
        else if (velocity.HasValue)
        {
            _lastVel = velocity.Value;
        }

        _buf.Add(new Snap { t = when, pos = pos, rot = rot, vel = _lastVel });
        if (_buf.Count > 64) _buf.RemoveAt(0);
    }

    private void Apply(Vector3 pos, Quaternion rot, bool hardSnap = false)
    {
        if (!root) return;

        // 误差特别大直接硬对齐，避免“橡皮筋”Sans说的回弹
        if (hardSnap || (root.position - pos).sqrMagnitude > hardSnapDistance * hardSnapDistance)
        {
            root.SetPositionAndRotation(pos, rot);
            if (modelRoot && modelRoot != root) modelRoot.rotation = rot;
            return;
        }

        // 正常平滑
        root.position = Vector3.Lerp(root.position, pos, posLerpFactor);
        if (modelRoot)
            modelRoot.rotation = Quaternion.Slerp(modelRoot.rotation, rot, rotLerpFactor);
    }

    private struct Snap
    {
        public double t;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 vel;
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