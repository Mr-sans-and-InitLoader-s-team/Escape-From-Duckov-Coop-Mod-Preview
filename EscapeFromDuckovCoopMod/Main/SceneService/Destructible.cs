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

﻿using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod
{
    public class Destructible
    {
        private NetService Service => NetService.Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        // Destructible registry: id -> HealthSimpleBase
        private readonly Dictionary<uint, HealthSimpleBase> _serverDestructibles = new Dictionary<uint, HealthSimpleBase>();
        private readonly Dictionary<uint, HealthSimpleBase> _clientDestructibles = new Dictionary<uint, HealthSimpleBase>();

        public readonly HashSet<uint> _deadDestructibleIds = new HashSet<uint>();


        // 用来避免 dangerFx 重复播放
        private readonly HashSet<uint> _dangerDestructibleIds = new HashSet<uint>();


        public void RegisterDestructible(uint id, HealthSimpleBase hs)
        {
            if (id == 0 || hs == null) return;
            if (IsServer) _serverDestructibles[id] = hs;
            else _clientDestructibles[id] = hs;
        }

        // 容错：找不到就全局扫一遍（场景切换后第一次命中时也能兜底）
        public HealthSimpleBase FindDestructible(uint id)
        {
            HealthSimpleBase hs = null;
            if (IsServer) _serverDestructibles.TryGetValue(id, out hs);
            else _clientDestructibles.TryGetValue(id, out hs);
            if (hs) return hs;

            var all = Object.FindObjectsOfType<HealthSimpleBase>(true);
            foreach (var e in all)
            {
                var tag = e.GetComponent<NetDestructibleTag>() ?? e.gameObject.AddComponent<NetDestructibleTag>();
                RegisterDestructible(tag.id, e);
                if (tag.id == id) hs = e;
            }
            return hs;
        }




        // 客户端：用于 ENV 快照应用，静默切换到“已破坏”外观（不放爆炸特效）
        public void Client_ApplyDestructibleDead_Snapshot(uint id)
        {
            if (_deadDestructibleIds.Contains(id)) return;
            var hs = FindDestructible(id);
            if (!hs) return;

            // Breakable：关正常/危险外观，开破坏外观，关主碰撞体
            var br = hs.GetComponent<Breakable>();
            if (br)
            {
                try
                {
                    if (br.normalVisual) br.normalVisual.SetActive(false);
                    if (br.dangerVisual) br.dangerVisual.SetActive(false);
                    if (br.breakedVisual) br.breakedVisual.SetActive(true);
                    if (br.mainCollider) br.mainCollider.SetActive(false);
                }
                catch { }
            }

            // HalfObsticle：走它自带的 Dead 一下，避免残留交互
            var half = hs.GetComponent<HalfObsticle>();
            if (half) { try { half.Dead(new DamageInfo()); } catch { } }

            // 彻底关掉所有 Collider
            try
            {
                foreach (var c in hs.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
            catch { }

            _deadDestructibleIds.Add(id);
        }

        private static Transform FindBreakableWallRoot(Transform t)
        {
            var p = t;
            while (p != null)
            {
                string nm = p.name;
                if (!string.IsNullOrEmpty(nm) &&
                    nm.IndexOf("BreakableWall", StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
                p = p.parent;
            }
            return null;
        }

        private static uint ComputeStableIdForDestructible(HealthSimpleBase hs)
        {
            if (!hs) return 0u;
            Transform root = FindBreakableWallRoot(hs.transform);
            if (root == null) root = hs.transform;
            try { return NetDestructibleTag.ComputeStableId(root.gameObject); }
            catch { return 0u; }
        }
        private void ScanAndMarkInitiallyDeadDestructibles()
        {
            if (_deadDestructibleIds == null) return;
            if (_serverDestructibles == null || _serverDestructibles.Count == 0) return;

            foreach (var kv in _serverDestructibles)
            {
                uint id = kv.Key;
                var hs = kv.Value;
                if (!hs) continue;
                if (_deadDestructibleIds.Contains(id)) continue;

                bool isDead = false;

                // 1) HP 兜底（部分 HSB 有 HealthValue）
                try { if (hs.HealthValue <= 0f) isDead = true; } catch { }

                // 2) Breakable：breaked 外观/主碰撞体关闭 => 视为“已破坏”
                if (!isDead)
                {
                    try
                    {
                        var br = hs.GetComponent<Breakable>();
                        if (br)
                        {
                            bool brokenView = (br.breakedVisual && br.breakedVisual.activeInHierarchy);
                            bool mainOff = (br.mainCollider && !br.mainCollider.activeSelf);
                            if (brokenView || mainOff) isDead = true;
                        }
                    }
                    catch { }
                }

                // 3) HalfObsticle：如果存在 isDead 字段，读一下（没有就忽略）
                if (!isDead)
                {
                    try
                    {
                        var half = hs.GetComponent("HalfObsticle"); // 避免编译期硬引用
                        if (half != null)
                        {
                            var t = half.GetType();
                            var fi = HarmonyLib.AccessTools.Field(t, "isDead");
                            if (fi != null)
                            {
                                object v = fi.GetValue(half);
                                if (v is bool && (bool)v) isDead = true;
                            }
                        }
                    }
                    catch { }
                }

                if (isDead) _deadDestructibleIds.Add(id);
            }
        }

        // 客户端：死亡复现（实际干活的内部函数）
        // 客户端：死亡复现（Breakable/半障碍/受击FX/碰撞体）
        private void Client_ApplyDestructibleDead_Inner(uint id, Vector3 point, Vector3 normal)
        {
            if (_deadDestructibleIds.Contains(id)) return;
            _deadDestructibleIds.Add(id);

            var hs = FindDestructible(id);
            if (!hs) return;

            // ★★ Breakable：复现 OnDead 里的可视化与爆炸（不做真正的扣血计算）
            var br = hs.GetComponent<Breakable>();
            if (br)
            {
                try
                {
                    // 视觉：normal/danger -> breaked
                    if (br.normalVisual) br.normalVisual.SetActive(false);
                    if (br.dangerVisual) br.dangerVisual.SetActive(false);
                    if (br.breakedVisual) br.breakedVisual.SetActive(true);

                    // 关闭主碰撞体
                    if (br.mainCollider) br.mainCollider.SetActive(false);

                    // 爆炸（与源码一致：LevelManager.ExplosionManager.CreateExplosion(...)）:contentReference[oaicite:9]{index=9}
                    if (br.createExplosion)
                    {
                        // fromCharacter 在客户端可为空，不影响范围伤害的演出
                        var di = br.explosionDamageInfo;
                        di.fromCharacter = null;
                        LevelManager.Instance.ExplosionManager.CreateExplosion(
                            hs.transform.position, br.explosionRadius, di, ExplosionFxTypes.normal, 1f
                        );
                    }
                }
                catch { /* 忽略反编译差异引发的异常 */ }
            }

            // HalfObsticle：走它自带的 Dead（工程里已有）  
            var half = hs.GetComponent<HalfObsticle>();
            if (half) { try { half.Dead(new DamageInfo { damagePoint = point, damageNormal = normal }); } catch { } }

            // 死亡特效（HurtVisual.DeadFx），项目里已有
            var hv = hs.GetComponent<HurtVisual>();
            if (hv && hv.DeadFx) Object.Instantiate(hv.DeadFx, hs.transform.position, hs.transform.rotation);

            // 关掉所有 Collider，防止残留可交互
            foreach (var c in hs.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        // 原来的 ENV_DEAD_EVENT 入口里，改为调用内部函数并记死
        public void Client_ApplyDestructibleDead(NetPacketReader r)
        {
            uint id = r.GetUInt();
            Vector3 point = r.GetV3cm();
            Vector3 normal = r.GetDir();
            Client_ApplyDestructibleDead_Inner(id, point, normal);
        }


        // 主机：把受击事件广播给所有客户端：包括当前位置供播放 HitFx，以及当前血量（可用于客户端UI/调试）
        public void Server_BroadcastDestructibleHurt(uint id, float newHealth, DamageInfo dmg)
        {
            if (!networkStarted || !IsServer) return;
            var w = new NetDataWriter();
            w.Put((byte)Op.ENV_HURT_EVENT);
            w.Put(id);
            w.Put(newHealth);
            // Hit视觉信息足够：点+法线
            w.PutV3cm(dmg.damagePoint);
            w.PutDir(dmg.damageNormal.sqrMagnitude < 1e-6f ? Vector3.forward : dmg.damageNormal.normalized);
            
            if (netManager != null)
            {
                netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.BroadcastData(w.Data, w.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        public void Server_BroadcastDestructibleDead(uint id, DamageInfo dmg)
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.ENV_DEAD_EVENT);
            w.Put(id);
            w.PutV3cm(dmg.damagePoint);
            w.PutDir(dmg.damageNormal.sqrMagnitude < 1e-6f ? Vector3.up : dmg.damageNormal.normalized);
            
            if (netManager != null)
            {
                netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.BroadcastData(w.Data, w.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        // 客户端：复现受击视觉（不改血量，不触发本地 OnHurt）
        // 客户端：复现受击视觉 + Breakable 的“危险态”显隐
        public void Client_ApplyDestructibleHurt(NetPacketReader r)
        {
            uint id = r.GetUInt();
            float curHealth = r.GetFloat();
            Vector3 point = r.GetV3cm();
            Vector3 normal = r.GetDir();

            // 已死亡就不播受击
            if (_deadDestructibleIds.Contains(id)) return;

            // 如果主机侧已经 <= 0，直接走死亡复现兜底
            if (curHealth <= 0f)
            {
                Client_ApplyDestructibleDead_Inner(id, point, normal);
                return;
            }

            var hs = FindDestructible(id);
            if (!hs) return;

            // 播放受击火花（项目里已有的 HurtVisual）
            var hv = hs.GetComponent<HurtVisual>();
            if (hv && hv.HitFx) Object.Instantiate(hv.HitFx, point, Quaternion.LookRotation(normal));

            // Breakable 的“危险态”切换（不改血，只做可视化）
            var br = hs.GetComponent<Breakable>();
            if (br)
            {
                // 危险阈值：源码里是 simpleHealth.HealthValue <= dangerHealth 时切到 danger。:contentReference[oaicite:7]{index=7}
                try
                {
                    // 当服务器汇报的血量低于危险阈值，且本地还没进危险态时，切显示 & 播一次 fx
                    if (curHealth <= br.dangerHealth && !_dangerDestructibleIds.Contains(id))
                    {
                        // normal -> danger
                        if (br.normalVisual) br.normalVisual.SetActive(false);
                        if (br.dangerVisual) br.dangerVisual.SetActive(true);
                        if (br.dangerFx) Object.Instantiate(br.dangerFx, br.transform.position, br.transform.rotation);
                        _dangerDestructibleIds.Add(id);
                    }
                }
                catch { /* 防御式：反编译字段为 null 时静默 */ }
            }
        }

        public void BuildDestructibleIndex()
        {
            // —— 兜底清空，防止跨图脏状态 —— //
            if (_deadDestructibleIds != null) _deadDestructibleIds.Clear();
            if (_dangerDestructibleIds != null) _dangerDestructibleIds.Clear();

            if (_serverDestructibles != null) _serverDestructibles.Clear();
            if (_clientDestructibles != null) _clientDestructibles.Clear();

            // 遍历所有 HSB（包含未激活物体，避免漏 index）
            var all = UnityEngine.Object.FindObjectsOfType<HealthSimpleBase>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var hs = all[i];
                if (!hs) continue;

                var tag = hs.GetComponent<NetDestructibleTag>();
                if (!tag) continue; // 我们只索引带有 NetDestructibleTag 的目标（墙/油桶等）

                // —— 统一计算稳定ID —— //
                uint id = ComputeStableIdForDestructible(hs);
                if (id == 0u)
                {
                    // 兜底：偶发异常时用自身 gameObject 算一次
                    try { id = NetDestructibleTag.ComputeStableId(hs.gameObject); } catch { }
                }
                tag.id = id;

                // —— 注册到现有索引（与你项目里的一致） —— //
                RegisterDestructible(tag.id, hs);
            }

            // —— 仅主机：扫描一遍“初始即已破坏”的目标，写进 _deadDestructibleIds —— //
            if (IsServer) // ⇦ 这里用你项目中判断“是否为主机”的字段/属性；若无则换成你原有判断
            {
                ScanAndMarkInitiallyDeadDestructibles();
            }
        }




    }
}
