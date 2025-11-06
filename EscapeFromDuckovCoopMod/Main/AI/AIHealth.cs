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

using System.Reflection;
﻿using Duckov.UI;
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
        
        bool isDead = currentHealth <= 0f;
        var connectedCount = netManager?.ConnectedPeerList?.Count ?? 0;
        
        if (ModBehaviourF.LogAiHpDebug || isDead)
        {
            Debug.Log($"[AI-HP][SERVER-BROADCAST] aiId={aiId} hp={currentHealth:F1}/{maxHealth:F1} dead={isDead} 广播给{connectedCount}个客户端");
            if (netManager?.ConnectedPeerList != null)
            {
                foreach (var peer in netManager.ConnectedPeerList)
                {
                    Debug.Log($"[AI-HP][SERVER-BROADCAST]   -> {peer.EndPoint}");
                }
            }
        }
        
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

        bool isDead = cur <= 0f;
        if (ModBehaviourF.LogAiHpDebug || isDead)
            Debug.Log($"[AI-HP][CLIENT-REPORT] aiId={aiId} max={max:F1} cur={cur:F1} isDead={isDead}");
    }

    public void HandleAiHealthReport(NetPeer sender, NetDataReader r)
    {
        if (!networkStarted || !IsServer) return;

        if (r.AvailableBytes < 12) return;

        var aiId = r.GetInt();
        var max = r.GetFloat();
        var cur = r.GetFloat();
        
        var senderEndpoint = sender?.EndPoint?.ToString() ?? "unknown";

        if (!AITool.aiById.TryGetValue(aiId, out var cmc) || !cmc)
        {
            Debug.LogWarning($"[AI-HP][SERVER-RECV]  AI不存在: aiId={aiId} from={senderEndpoint}");
            return;
        }

        var h = cmc.Health;
        if (!h)
        {
            Debug.LogWarning($"[AI-HP][SERVER-RECV]  AI无Health组件: aiId={aiId} from={senderEndpoint}");
            return;
        }

        var serverCurrentHp = h.CurrentHealth;
        var serverMaxHp = h.MaxHealth;
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

        bool clientReportDead = cur <= 0f;
        bool serverCurrentlyDead = serverCurrentHp <= 0f;
        
        Debug.Log($"[AI-HP][SERVER-RECV] aiId={aiId} from={senderEndpoint} | 客户端报告: hp={cur:F1}/{max:F1} dead={clientReportDead} | 服务器当前: hp={serverCurrentHp:F1}/{serverMaxHp:F1} dead={serverCurrentlyDead}");

        HealthM.Instance.ForceSetHealth(h, applyMax, clampedCur, false);

        Debug.Log($"[AI-HP][SERVER-APPLY] aiId={aiId} 应用血量: {clampedCur:F1}/{applyMax:F1} wasDead={wasDead}");

        Server_BroadcastAiHealth(aiId, applyMax, clampedCur);

        if (clampedCur <= 0f && !wasDead)
        {
            Debug.Log($"[AI-HP][SERVER-DEATH]  AI死亡触发 aiId={aiId} from={senderEndpoint}");
            
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
                Debug.Log($"[AI-HP][SERVER-DEATH] OnDeadEvent已触发 aiId={aiId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI-HP][SERVER-DEATH] OnDeadEvent异常: {e.Message}");
            }

            try
            {
                AITool.TryFireOnDead(cmc.Health, di);
                Debug.Log($"[AI-HP][SERVER-DEATH] TryFireOnDead已触发 aiId={aiId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI-HP][SERVER-DEATH] TryFireOnDead异常: {e.Message}");
            }
        }
        else if (clampedCur <= 0f && wasDead)
        {
            Debug.Log($"[AI-HP][SERVER-DEATH] AI已死亡(重复) aiId={aiId} from={senderEndpoint}");
        }
    }


    public void Client_ApplyAiHealth(int aiId, float max, float cur)
    {
        if (IsServer) return;

        bool isDead = cur <= 0f;
        
        // AI 尚未注册：缓存 max/cur，等 RegisterAi 时一起冲
        if (!AITool.aiById.TryGetValue(aiId, out var cmc) || !cmc)
        {
            COOPManager.AIHandle._cliPendingAiHealth[aiId] = cur;
            if (max > 0f) COOPManager.AIHandle._cliPendingAiMax[aiId] = max;
            // 注释掉日志避免刷屏
            // Debug.Log($"[AI-HP][CLIENT-RECV]  AI未注册，缓存血量: aiId={aiId} max={max:F1} cur={cur:F1} dead={isDead}");
            return;
        }

        var h = cmc.Health;
        if (!h)
        {
            // Debug.LogWarning($"[AI-HP][CLIENT-RECV]  AI无Health组件: aiId={aiId}");
            return;
        }
        
        var clientCurrentHp = h.CurrentHealth;
        // 注释掉日志避免刷屏，仅在显著变化时记录
        // Debug.Log($"[AI-HP][CLIENT-RECV] aiId={aiId} | 服务器同步: hp={cur:F1}/{max:F1} dead={isDead} | 客户端当前: hp={clientCurrentHp:F1}");

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

        // 死亡则本地立即隐藏，防"幽灵AI"
        if (cur <= 0f)
        {
            Debug.Log($"[AI-HP][CLIENT-DEATH]  客户端处理AI死亡 aiId={aiId}");
            
            try
            {
                var ai = cmc.GetComponent<AICharacterController>();
                if (ai)
                {
                    ai.enabled = false;
                    Debug.Log($"[AI-HP][CLIENT-DEATH] 禁用AICharacterController aiId={aiId}");
                }

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
                        Debug.Log($"[AI-HP][CLIENT-DEATH] 隐藏血条 aiId={aiId}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AI-HP][CLIENT-DEATH] 隐藏血条失败: {e.Message}");
                }

                cmc.gameObject.SetActive(false);
                Debug.Log($"[AI-HP][CLIENT-DEATH] 隐藏GameObject aiId={aiId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI-HP][CLIENT-DEATH] 处理死亡异常: {e.Message}");
            }


            if (AITool._cliAiDeathFxOnce.Add(aiId))
            {
                FxManager.Client_PlayAiDeathFxAndSfx(cmc);
                Debug.Log($"[AI-HP][CLIENT-DEATH] 播放死亡特效 aiId={aiId}");
            }
            else
            {
                Debug.Log($"[AI-HP][CLIENT-DEATH] 跳过重复的死亡特效 aiId={aiId}");
            }
        }
    }
}