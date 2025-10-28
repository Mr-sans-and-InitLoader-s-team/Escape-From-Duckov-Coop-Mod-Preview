﻿// Escape-From-Duckov-Coop-Mod-Preview
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace 鸭科夫联机Mod
{
    public class NetInterpolator : MonoBehaviour
    {
        struct Snap { public double t; public Vector3 pos; public Quaternion rot; }

        [Tooltip("渲染回看时间；越大越稳，越小越跟手")]
        public float interpolationBackTime = 0.12f;  
        [Tooltip("缺帧时最多允许预测多久")]
        public float maxExtrapolate = 0.05f;        
        [Tooltip("误差过大时直接硬对齐距离")]
        public float hardSnapDistance = 6f;          // 6 米 Sans看不懂就给设置个Tooltip
        [Tooltip("位置平滑插值的瞬时权重")]
        public float posLerpFactor = 0.9f;
        [Tooltip("朝向平滑插值的瞬时权重")]
        public float rotLerpFactor = 0.9f;

        [Header("跑步反超护栏")]
        public bool extrapolateWhenRunning = false; // 跑步默认禁用预测
        public float runSpeedThreshold = 3.0f;      // 认为 >3 m/s 为跑步

        Transform root;      // 驱动位置
        Transform modelRoot; // 驱动朝向

        readonly List<Snap> _buf = new List<Snap>(64);
        Vector3 _lastVel = Vector3.zero;

        public void Init(Transform rootT, Transform modelRootT)
        {
            root = rootT; modelRoot = modelRootT ? modelRootT : rootT;
        }

        // 喂一帧快照；when<0 则取到达时刻
        public void Push(Vector3 pos, Quaternion rot, double when = -1)
        {
            if (when < 0) when = Time.unscaledTimeAsDouble;
            if (_buf.Count > 0)
            {
                var prev = _buf[_buf.Count - 1];
                double dt = when - prev.t;
                if (dt > 1e-6) _lastVel = (pos - prev.pos) / (float)dt;

                // 若跨度太离谱，清空缓冲直接从新轨迹开始，避免长距离抖动
                if ((pos - prev.pos).sqrMagnitude > hardSnapDistance * hardSnapDistance)
                    _buf.Clear();
            }

            _buf.Add(new Snap { t = when, pos = pos, rot = rot });
            if (_buf.Count > 64) _buf.RemoveAt(0);
        }

        void LateUpdate()
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
                else root = transform;
            }
            if (!modelRoot) modelRoot = root;
            if (_buf.Count == 0) return;

            double renderT = Time.unscaledTimeAsDouble - interpolationBackTime;

            // 找到 [i-1, i] 包围 renderT 的两个样本
            int i = 0;
            while (i < _buf.Count && _buf[i].t < renderT) i++;

            if (i == 0)
            {
                // 数据太新：直接用第一帧（刚开始的 100ms 内）
                Apply(_buf[0].pos, _buf[0].rot, hardSnap: true);
                return;
            }

            if (i < _buf.Count)
            {
                // 插值
                var a = _buf[i - 1]; var b = _buf[i];
                float t = (float)((renderT - a.t) / System.Math.Max(1e-6, b.t - a.t));
                var pos = Vector3.LerpUnclamped(a.pos, b.pos, t);
                var rot = Quaternion.Slerp(a.rot, b.rot, t);
                Apply(pos, rot);

                // 适度回收旧帧（保留一帧冗余，遇到回退也能抗一下）
                if (i > 1) _buf.RemoveRange(0, i - 1);
            }
            else
            {
                var last = _buf[_buf.Count - 1];
                double dt = renderT - last.t;

                // 是否允许本帧预测
                bool allow = (dt <= maxExtrapolate);
                if (!extrapolateWhenRunning)
                {
                    float speed = _lastVel.magnitude;
                    if (speed > runSpeedThreshold) allow = false; // 跑步：禁用预测，避免超前后拉
                }

                if (allow)
                    Apply(last.pos + _lastVel * (float)dt, last.rot);
                else
                    Apply(last.pos, last.rot);

                if (_buf.Count > 2) _buf.RemoveRange(0, _buf.Count - 2);
            }
        }

        void Apply(Vector3 pos, Quaternion rot, bool hardSnap = false)
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

}
