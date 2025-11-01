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

using System.Reflection;
using Duckov.UI;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public class AIHealth
{
    // 反射字段（Health 反编译字段）研究了20年研究出来的
    private static readonly FieldInfo FI_defaultMax =
        typeof(Health).GetField("defaultMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_lastMax =
        typeof(Health).GetField("lastMaxHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI__current =
        typeof(Health).GetField("_currentHealth", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_characterCached =
        typeof(Health).GetField("characterCached", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FI_hasCharacter =
        typeof(Health).GetField("hasCharacter", BindingFlags.NonPublic | BindingFlags.Instance);

    private readonly Dictionary<int, float> _cliLastAiHp = new();
    private readonly Dictionary<int, float> _cliLastReportedHp = new();
    private readonly Dictionary<int, float> _cliNextReportAt = new();
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    /// <summary>
    ///     /////////////AI血量同步//////////////AI血量同步//////////////AI血量同步//////////////AI血量同步//////////////AI血量同步////////
    /// </summary>
    public void Server_BroadcastAiHealth(int aiId, float maxHealth, float currentHealth)
    {
        if (!networkStarted || !IsServer) return;
        var w = new NetDataWriter();
        w.Put((byte)Op.AI_HEALTH_SYNC);
        w.Put(aiId);
        w.Put(maxHealth);
        w.Put(currentHealth);
        netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }


    public void Client_ReportAiHealth(int aiId, float max, float cur)
    {
        if (!networkStarted || IsServer || connectedPeer == null || aiId == 0) return;

        var now = Time.time;
        if (_cliNextReportAt.TryGetValue(aiId, out var next) && now < next)
        {
            if (_cliLastReportedHp.TryGetValue(aiId, out var last) && Mathf.Abs(last - cur) < 0.01f)
                return;
        }

        var w = new NetDataWriter();
        w.Put((byte)Op.AI_HEALTH_REPORT);
        w.Put(aiId);
        w.Put(max);
        w.Put(cur);
        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);

        _cliNextReportAt[aiId] = now + 0.05f;
        _cliLastReportedHp[aiId] = cur;

        if (ModBehaviourF.LogAiHpDebug)
            Debug.Log($"[AI-HP][CLIENT] report aiId={aiId} max={max} cur={cur}");
    }

    public void HandleAiHealthReport(NetPeer sender, NetPacketReader r)
    {
        if (!networkStarted || !IsServer) return;

        if (r.AvailableBytes < 12) return;

        var aiId = r.GetInt();
        var max = r.GetFloat();
        var cur = r.GetFloat();

        if (!AITool.aiById.TryGetValue(aiId, out var cmc) || !cmc)
        {
            if (ModBehaviourF.LogAiHpDebug)
                Debug.LogWarning($"[AI-HP][SERVER] report missing AI aiId={aiId} from={sender?.EndPoint}");
            return;
        }

        var h = cmc.Health;
        if (!h)
        {
            if (ModBehaviourF.LogAiHpDebug)
                Debug.LogWarning($"[AI-HP][SERVER] report aiId={aiId} has no Health");
            return;
        }

        var applyMax = max > 0f ? max : h.MaxHealth;
        var maxForClamp = applyMax > 0f ? applyMax : h.MaxHealth;
        var clampedCur = maxForClamp > 0f ? Mathf.Clamp(cur, 0f, maxForClamp) : Mathf.Max(0f, cur);

        var wasDead = false;
        try
        {
            wasDead = h.IsDead;
        }
        catch
        {
        }

        HealthM.Instance.ForceSetHealth(h, applyMax, clampedCur, false);

        if (ModBehaviourF.LogAiHpDebug)
            Debug.Log($"[AI-HP][SERVER] apply report aiId={aiId} max={applyMax} cur={clampedCur} from={sender?.EndPoint}");

        Server_BroadcastAiHealth(aiId, applyMax, clampedCur);

        if (clampedCur <= 0f && !wasDead)
        {
            var di = new DamageInfo();

            try
            {
                di.damageValue = Mathf.Max(1f, applyMax > 0f ? applyMax : 1f);
            }
            catch
            {
            }

            try
            {
                di.finalDamage = di.damageValue;
            }
            catch
            {
            }

            try
            {
                di.damagePoint = cmc.transform.position;
            }
            catch
            {
            }

            try
            {
                di.damageNormal = Vector3.up;
            }
            catch
            {
            }

            try
            {
                di.toDamageReceiver = cmc.mainDamageReceiver;
            }
            catch
            {
            }

            try
            {
                if (playerStatuses != null && sender != null && playerStatuses.TryGetValue(sender, out var st) && st != null)
                    di.fromCharacter = CharacterMainControl.Main;
            }
            catch
            {
            }

            try
            {
                cmc.Health.OnDeadEvent?.Invoke(di);
            }
            catch
            {
            }

            try
            {
                AITool.TryFireOnDead(cmc.Health, di);
            }
            catch
            {
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
            return;
        }

        var h = cmc.Health;
        if (!h) return;

        try
        {
            var prev = 0f;
            _cliLastAiHp.TryGetValue(aiId, out prev);
            _cliLastAiHp[aiId] = cur;

            var delta = prev - cur; // 掉血为正
            if (delta > 0.01f)
            {
                var pos = cmc.transform.position + Vector3.up * 1.1f;
                var di = new DamageInfo();
                di.damagePoint = pos;
                di.damageNormal = Vector3.up;
                di.damageValue = delta;
                // 如果运行库里有 finalDamage 字段就能显示更准的数值（A 节已经做了优先显示）
                try
                {
                    di.finalDamage = delta;
                }
                catch
                {
                }

                LocalHitKillFx.PopDamageText(pos, di);
            }
        }
        catch
        {
        }

        // 写入/更新 Max 覆盖（只在给到有效 max 时）
        if (max > 0f)
        {
            COOPManager.AIHandle._cliAiMaxOverride[h] = max;
            // 顺便把 defaultMaxHealth 调大，触发一次 OnMaxHealthChange（即使有 item stat，我也同步一下，保险）
            try
            {
                FI_defaultMax?.SetValue(h, Mathf.RoundToInt(max));
            }
            catch
            {
            }

            try
            {
                FI_lastMax?.SetValue(h, -12345f);
            }
            catch
            {
            }

            try
            {
                h.OnMaxHealthChange?.Invoke(h);
            }
            catch
            {
            }
        }

        // 读一下当前 client 视角的 Max（注意：此时 get_MaxHealth 已有 Harmony 覆盖，能拿到“权威 max”）
        var nowMax = 0f;
        try
        {
            nowMax = h.MaxHealth;
        }
        catch
        {
        }

        // ——避免被 SetHealth() 按“旧 Max”夹住：当 cur>nowMax 时，直接反射写 _currentHealth —— 
        if (nowMax > 0f && cur > nowMax + 0.0001f)
        {
            try
            {
                FI__current?.SetValue(h, cur);
            }
            catch
            {
            }

            try
            {
                h.OnHealthChange?.Invoke(h);
            }
            catch
            {
            }
        }
        else
        {
            // 常规路径
            try
            {
                h.SetHealth(Mathf.Max(0f, cur));
            }
            catch
            {
                try
                {
                    FI__current?.SetValue(h, Mathf.Max(0f, cur));
                }
                catch
                {
                }
            }

            try
            {
                h.OnHealthChange?.Invoke(h);
            }
            catch
            {
            }
        }

        // 起血条兜底
        try
        {
            h.showHealthBar = true;
        }
        catch
        {
        }

        try
        {
            h.RequestHealthBar();
        }
        catch
        {
        }

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
                    var miGet = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(Health) });
                    var hb = miGet?.Invoke(HealthBarManager.Instance, new object[] { h }) as HealthBar;
                    if (hb != null)
                    {
                        var miRel = AccessTools.DeclaredMethod(typeof(HealthBar), "Release", Type.EmptyTypes);
                        if (miRel != null) miRel.Invoke(hb, null);
                        else hb.gameObject.SetActive(false);
                    }
                }
                catch
                {
                }

                cmc.gameObject.SetActive(false);
            }
            catch
            {
            }


            if (AITool._cliAiDeathFxOnce.Add(aiId))
                FxManager.Client_PlayAiDeathFxAndSfx(cmc);
        }
    }
}