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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class HealthM : MonoBehaviour
    {
        public static HealthM Instance;

        private NetService Service => NetService.Instance;
        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        private static (float max, float cur) _cliLastSentHp = HealthTool._cliLastSentHp;
        private static float _cliNextSendHp = HealthTool._cliNextSendHp;

        private Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

        public bool _cliApplyingSelfSnap = false;
        public float _cliEchoMuteUntil = 0f;

        // 主机端：节流去抖
        private readonly Dictionary<Health, (float max, float cur)> _srvLastSent = new Dictionary<Health, (float max, float cur)>();
        private readonly Dictionary<Health, float> _srvNextSend = new Dictionary<Health, float>();
        private const float SRV_HP_SEND_COOLDOWN = 0.05f; // 20Hz
        private readonly Dictionary<Health, string> _srvHealthOwner = HealthTool._srvHealthOwner;

        public void Init()
        {
            Instance = this;
        }

        internal bool TryGetClientMaxOverride(Health h, out float v) => COOPManager.AIHandle._cliAiMaxOverride.TryGetValue(h, out v);


        // 发送自身血量（带 20Hz 节流 & 值未变不发）
        public void Client_SendSelfHealth(Health h, bool force)
        {
            if (_cliApplyingSelfSnap || Time.time < _cliEchoMuteUntil) return;

            if (!networkStarted || IsServer || h == null) return;
            
            var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
            if (connectedPeer == null && (hybrid == null || !hybrid.IsConnected)) return;

            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; } catch { }
            try { cur = h.CurrentHealth; } catch { }

            // 去抖：值相同直接跳过
            if (!force && Mathf.Approximately(max, _cliLastSentHp.max) && Mathf.Approximately(cur, _cliLastSentHp.cur))
                return;

            // 节流：20Hz
            if (!force && Time.time < _cliNextSendHp) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYER_HEALTH_REPORT);
            w.Put(max);
            w.Put(cur);
            
            if (connectedPeer != null)
            {
                connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
            }
            else if (hybrid != null && hybrid.IsConnected)
            {
                hybrid.SendData(w.Data, w.Length, DeliveryMethod.ReliableOrdered);
            }

            _cliLastSentHp = (max, cur);
            _cliNextSendHp = Time.time + 0.05f;
        }


        public void Server_ForceAuthSelf(Health h)
        {
            if (!networkStarted || !IsServer || h == null) return;
            if (!_srvHealthOwner.TryGetValue(h, out var ownerEndPoint) || string.IsNullOrEmpty(ownerEndPoint)) return;

            var w = writer;
            if (w == null) return;
            w.Reset();
            w.Put((byte)Op.AUTH_HEALTH_SELF);
            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; cur = h.CurrentHealth; } catch { }
            w.Put(max);
            w.Put(cur);
            CoopTool.SendToEndPoint(ownerEndPoint, w.Data, w.Length, LiteNetLib.DeliveryMethod.ReliableOrdered);
        }

        // 主机：把 DamageInfo（简化字段）发给拥有者客户端，让其本地执行 Hurt
        public void Server_ForwardHurtToOwner(string ownerEndPoint, global::DamageInfo di)
        {
            if (!IsServer || string.IsNullOrEmpty(ownerEndPoint)) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYER_HURT_EVENT);

            // 参照你现有近战上报字段进行对称序列化
            w.Put(di.damageValue);
            w.Put(di.armorPiercing);
            w.Put(di.critDamageFactor);
            w.Put(di.critRate);
            w.Put(di.crit);
            w.PutV3cm(di.damagePoint);
            w.PutDir(di.damageNormal.sqrMagnitude < 1e-6f ? Vector3.up : di.damageNormal.normalized);
            w.Put(di.fromWeaponItemID);
            w.Put(di.bleedChance);
            w.Put(di.isExplosion);

            CoopTool.SendToEndPoint(ownerEndPoint, w.Data, w.Length, DeliveryMethod.ReliableOrdered);
        }


        public void Client_ApplySelfHurtFromServer(NetPacketReader r)
        {
            try
            {
                // 反序列化与上面写入顺序保持一致
                float dmg = r.GetFloat();
                float ap = r.GetFloat();
                float cdf = r.GetFloat();
                float cr = r.GetFloat();
                int crit = r.GetInt();
                Vector3 hit = r.GetV3cm();
                Vector3 nrm = r.GetDir();
                int wid = r.GetInt();
                float bleed = r.GetFloat();
                bool boom = r.GetBool();

                var main = LevelManager.Instance ? LevelManager.Instance.MainCharacter : null;
                if (!main || main.Health == null) return;

                // 构造 DamageInfo（攻击者此处可不给/或给 main，自身并不影响结算核心）
                var di = new DamageInfo(main)
                {
                    damageValue = dmg,
                    armorPiercing = ap,
                    critDamageFactor = cdf,
                    critRate = cr,
                    crit = crit,
                    damagePoint = hit,
                    damageNormal = nrm,
                    fromWeaponItemID = wid,
                    bleedChance = bleed,
                    isExplosion = boom
                };

                // 记录“最近一次本地受击时间”，便于已有的 echo 抑制逻辑
                HealthTool._cliLastSelfHurtAt = Time.time;

                main.Health.Hurt(di);

                Client_ReportSelfHealth_IfReadyOnce();

            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CLIENT] apply self hurt from server failed: " + e);
            }
        }

        public void Client_ReportSelfHealth_IfReadyOnce()
        {
            if (_cliApplyingSelfSnap || Time.time < _cliEchoMuteUntil) return;
            if (IsServer || HealthTool._cliInitHpReported) return;

            var main = CharacterMainControl.Main;
            var h = main ? main.GetComponentInChildren<Health>(true) : null;
            if (!h) return;

            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; } catch { }
            try { cur = h.CurrentHealth; } catch { }

            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYER_HEALTH_REPORT);
            w.Put(max);
            w.Put(cur);

            if (connectedPeer != null && connectedPeer.ConnectionState == ConnectionState.Connected)
            {
                connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.SendData(w.Data, w.Length, DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    return;
                }
            }

            HealthTool._cliInitHpReported = true;
        }

        public void Server_OnHealthChanged(string ownerEndPoint, Health h)
        {
            if (!IsServer || !h) return;

            float max = 0f, cur = 0f;
            try { max = h.MaxHealth; } catch { }
            try { cur = h.CurrentHealth; } catch { }

            if (max <= 0f) return;
            // 去抖 + 限频（与你现有字段保持一致）
            if (_srvLastSent.TryGetValue(h, out var last))
                if (Mathf.Approximately(max, last.max) && Mathf.Approximately(cur, last.cur))
                    return;

            float now = Time.time;
            if (_srvNextSend.TryGetValue(h, out var tNext) && now < tNext)
                return;

            _srvLastSent[h] = (max, cur);
            _srvNextSend[h] = now + SRV_HP_SEND_COOLDOWN;

            // 计算 playerId（你已有的辅助方法）
            string pid = NetService.Instance.GetPlayerId(ownerEndPoint);

           
            if (!string.IsNullOrEmpty(ownerEndPoint))
            {
                var w1 = new NetDataWriter();
                w1.Put((byte)Op.AUTH_HEALTH_SELF);
                w1.Put(max);
                w1.Put(cur);
                CoopTool.SendToEndPoint(ownerEndPoint, w1.Data, w1.Length, DeliveryMethod.ReliableOrdered);
            }

            
            var w2 = new NetDataWriter();
            w2.Put((byte)Op.AUTH_HEALTH_REMOTE);
            w2.Put(pid);
            w2.Put(max);
            w2.Put(cur);

            if (netManager != null)
            {
                foreach (var p in netManager.ConnectedPeerList)
                {
                    if (p.EndPoint.ToString() == ownerEndPoint) continue;
                    p.Send(w2, DeliveryMethod.ReliableOrdered);
                }
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                    if (steamNet != null && steamNet.peerConnections != null)
                    {
                        foreach (var peer in steamNet.peerConnections.Keys)
                        {
                            if (peer.ToString() == ownerEndPoint) continue;
                            steamNet.SendPacket(peer, w2.Data, w2.Length);
                        }
                    }
                }
            }
        }

        // 服务器兜底：每帧确保所有权威对象都已挂监听（含主机自己）
        public void Server_EnsureAllHealthHooks()
        {
            if (!IsServer || !networkStarted) return;

            var hostMain = CharacterMainControl.Main;
            if (hostMain) HealthTool.Server_HookOneHealth(null, hostMain.gameObject);

            if (remoteCharacters != null)
            {
                foreach (var kv in remoteCharacters)
                {
                    var endPoint = kv.Key;
                    var go = kv.Value;
                    if (string.IsNullOrEmpty(endPoint) || !go) continue;
                    HealthTool.Server_HookOneHealth(endPoint, go);
                }
            }
        }






        // 起条兜底：多帧重复请求血条，避免 UI 初始化竞态
        private static IEnumerator EnsureBarRoutine(Health h, int attempts, float interval)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (h == null) yield break;
                try { h.showHealthBar = true; } catch { }
                try { h.RequestHealthBar(); } catch { }
                try { h.OnMaxHealthChange?.Invoke(h); } catch { }
                try { h.OnHealthChange?.Invoke(h); } catch { }
                yield return new WaitForSeconds(interval);
            }
        }

        // 把 (max,cur) 灌到 Health，并确保血条显示（修正 defaultMax=0）
        public void ForceSetHealth(Health h, float max, float cur, bool ensureBar = true)
        {
            if (!h) return;

            float nowMax = 0f; try { nowMax = h.MaxHealth; } catch { }
            int defMax = 0; try { defMax = (int)(HealthTool.FI_defaultMax?.GetValue(h) ?? 0); } catch { }

            // ★ 只要传入的 max 更大，就把 defaultMaxHealth 调到更大，并触发一次 Max 变更事件
            if (max > 0f && (nowMax <= 0f || max > nowMax + 0.0001f || defMax <= 0))
            {
                try
                {
                    HealthTool.FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max));
                    HealthTool.FI_lastMax?.SetValue(h, -12345f);
                    h.OnMaxHealthChange?.Invoke(h);
                }
                catch { }
            }

            // ★ 避免被 SetHealth() 按旧 Max 夹住
            float effMax = 0f; try { effMax = h.MaxHealth; } catch { }
            if (effMax > 0f && cur > effMax + 0.0001f)
            {
                try { HealthTool.FI__current?.SetValue(h, cur); } catch { }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }
            else
            {
                try { h.SetHealth(cur); } catch { try { HealthTool.FI__current?.SetValue(h, cur); } catch { } }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }

            if (ensureBar)
            {
                try { h.showHealthBar = true; } catch { }
                try { h.RequestHealthBar(); } catch { }
                StartCoroutine(EnsureBarRoutine(h, 30, 0.1f));
            }
        }

        // 统一应用到某个 GameObject 的 Health（含绑定）

        public void ApplyHealthAndEnsureBar(GameObject go, float max, float cur)
        {
            if (!go) return;

            var cmc = go.GetComponent<CharacterMainControl>();
            var h = go.GetComponentInChildren<Health>(true);
            if (!cmc || !h) return;

            try { h.autoInit = false; } catch { }

            // 绑定 Health ⇄ Character（否则 UI/Hidden 判断拿不到角色）
            HealthTool.BindHealthToCharacter(h, cmc);

            // 先把数值灌进去（内部会触发 OnMax/OnHealth）
            ForceSetHealth(h, max > 0 ? max : 40f, (cur > 0 ? cur : (max > 0 ? max : 40f)), ensureBar: false);

            // 立刻起条 + 多帧兜底（UI 还没起来时反复 Request）
            try { h.showHealthBar = true; } catch { }
            try { h.RequestHealthBar(); } catch { }

            // 触发一轮事件，部分 UI 需要
            try { h.OnMaxHealthChange?.Invoke(h); } catch { }
            try { h.OnHealthChange?.Invoke(h); } catch { }

            // 多帧重试：8 次、每 0.25s 一次（你已有 EnsureBarRoutine(h, attempts, interval)）
            StartCoroutine(EnsureBarRoutine(h, 8, 0.25f));
        }

    }
}
