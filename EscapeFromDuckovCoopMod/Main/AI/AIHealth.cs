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

﻿using Duckov.UI;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class AIHealth
    {
        private NetService Service => NetService.Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        private Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
        private readonly Dictionary<int, float> _cliLastAiHp = new Dictionary<int, float>();

        /// <summary>
        /// /////////////AI血量同步//////////////AI血量同步//////////////AI血量同步//////////////AI血量同步//////////////AI血量同步////////
        /// </summary>

        public void Server_BroadcastAiHealth(int aiId, float maxHealth, float currentHealth)
        {
            if (!networkStarted || !IsServer) return;
            
            if (writer == null)
            {
                Debug.LogWarning("[AIHealth] writer is null");
                return;
            }
            
            writer.Reset();
            writer.Put((byte)Op.AI_HEALTH_SYNC);
            writer.Put(aiId);
            writer.Put(maxHealth);
            writer.Put(currentHealth);
            
            if (netManager != null)
            {
                netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.BroadcastData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }



        public void Client_ApplyAiHealth(int aiId, float max, float cur)
        {
            if (IsServer) return;

            // AI 尚未注册：缓存 max/cur，等 RegisterAi 时一起冲
            if (!AITool.aiById.TryGetValue(aiId, out var cmc) || !cmc)
            {
                COOPManager.AIHandle._cliPendingAiHealth[aiId] = cur;
                if (max > 0f) COOPManager.AIHandle._cliPendingAiMax[aiId] = max;
                Debug.Log("[AI-HP][CLIENT] pending aiId=" + aiId + " max=" + max + " cur=" + cur);
                return;
            }

            var h = cmc.Health;
            if (!h) return;

            try
            {
                float prev = 0f;
                _cliLastAiHp.TryGetValue(aiId, out prev);
                _cliLastAiHp[aiId] = cur;

                float delta = prev - cur;                     // 掉血为正
                if (delta > 0.01f)
                {
                    var pos = cmc.transform.position + Vector3.up * 1.1f;
                    var di = new global::DamageInfo();
                    di.damagePoint = pos;
                    di.damageNormal = Vector3.up;
                    di.damageValue = delta;
                    // 如果运行库里有 finalDamage 字段就能显示更准的数值（A 节已经做了优先显示）
                    try { di.finalDamage = delta; } catch { }
                    LocalHitKillFx.PopDamageText(pos, di);
                }
            }
            catch { }

            // 写入/更新 Max 覆盖（只在给到有效 max 时）
            if (max > 0f)
            {
                COOPManager.AIHandle._cliAiMaxOverride[h] = max;
                // 顺便把 defaultMaxHealth 调大，触发一次 OnMaxHealthChange（即使有 item stat，我也同步一下，保险）
                try { FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max)); } catch { }
                try { FI_lastMax?.SetValue(h, -12345f); } catch { }
                try { h.OnMaxHealthChange?.Invoke(h); } catch { }
            }

            // 读一下当前 client 视角的 Max（注意：此时 get_MaxHealth 已有 Harmony 覆盖，能拿到“权威 max”）
            float nowMax = 0f; try { nowMax = h.MaxHealth; } catch { }

            // ——避免被 SetHealth() 按“旧 Max”夹住：当 cur>nowMax 时，直接反射写 _currentHealth —— 
            if (nowMax > 0f && cur > nowMax + 0.0001f)
            {
                try { FI__current?.SetValue(h, cur); } catch { }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }
            else
            {
                // 常规路径
                try { h.SetHealth(Mathf.Max(0f, cur)); } catch { try { FI__current?.SetValue(h, Mathf.Max(0f, cur)); } catch { } }
                try { h.OnHealthChange?.Invoke(h); } catch { }
            }

            // 起血条兜底
            try { h.showHealthBar = true; } catch { }
            try { h.RequestHealthBar(); } catch { }

            // 死亡则本地立即隐藏，防“幽灵AI”
            if (cur <= 0f)
            {
                try
                {
                    var ai = cmc.GetComponent<AICharacterController>();
                    if (ai) ai.enabled = false;

                    // 释放/隐藏血条
                    try
                    {
                        var miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(global::Health) });
                        var hb = miGet?.Invoke(HealthBarManager.Instance, new object[] { h }) as Duckov.UI.HealthBar;
                        if (hb != null)
                        {
                            var miRel = AccessTools.DeclaredMethod(typeof(global::Duckov.UI.HealthBar), "Release", Type.EmptyTypes);
                            if (miRel != null) miRel.Invoke(hb, null);
                            else hb.gameObject.SetActive(false);
                        }
                    }
                    catch { }

                    cmc.gameObject.SetActive(false);
                }
                catch { }


                if (AITool._cliAiDeathFxOnce.Add(aiId))
                    FxManager.Client_PlayAiDeathFxAndSfx(cmc);
            }
        }


        // 反射字段（Health 反编译字段）研究了20年研究出来的
        static readonly System.Reflection.FieldInfo FI_defaultMax =
            typeof(Health).GetField("defaultMaxHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI_lastMax =
            typeof(Health).GetField("lastMaxHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI__current =
            typeof(Health).GetField("_currentHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI_characterCached =
            typeof(Health).GetField("characterCached", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo FI_hasCharacter =
            typeof(Health).GetField("hasCharacter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    }
}
