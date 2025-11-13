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

using Duckov.UI;
using EscapeFromDuckovCoopMod.Net;  // 引入智能发送扩展方法
using EscapeFromDuckovCoopMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

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

    // 【优化】缓存反射方法，避免每次死亡都调用 AccessTools.DeclaredMethod
    private static readonly MethodInfo MI_GetActiveHealthBar;
    private static readonly MethodInfo MI_ReleaseHealthBar;

    // 【优化】静态构造函数，初始化时缓存反射方法
    static AIHealth()
    {
        try
        {
            MI_GetActiveHealthBar = AccessTools.DeclaredMethod(typeof(HealthBarManager), "GetActiveHealthBar", new[] { typeof(Health) });
        }
        catch
        {
            MI_GetActiveHealthBar = null;
        }

        try
        {
            MI_ReleaseHealthBar = AccessTools.DeclaredMethod(typeof(HealthBar), "Release", Type.EmptyTypes);
        }
        catch
        {
            MI_ReleaseHealthBar = null;
        }
    }

    private readonly Dictionary<int, float> _cliLastAiHp = new();
    private readonly Dictionary<int, float> _cliLastReportedHp = new();
    private readonly Dictionary<int, float> _cliNextReportAt = new();
    private readonly HashSet<int> _srvDeathHandled = new();

    // 🛡️ 日志频率限制
    private static int _pendingAiWarningCount = 0;
    private const int PENDING_AI_WARNING_INTERVAL = 200;  // 每200次只警告1次

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
        // 使用 SendSmart 自动选择传输方式（AI_HEALTH_SYNC → Critical → ReliableOrdered）
        netManager.SendSmart(w, Op.AI_HEALTH_SYNC);
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

    public void HandleAiHealthReport(NetPeer sender, NetDataReader r)
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

        DamageInfo deathInfo = new DamageInfo();
        if (clampedCur <= 0f && !wasDead)
        {
            if (ModBehaviourF.LogAiHpDebug)
                Debug.Log($"[AI-HP][SERVER] AI死亡触发 aiId={aiId}, 准备生成战利品盒子");

            deathInfo = new DamageInfo();

            try
            {
                deathInfo.damageValue = Mathf.Max(1f, applyMax > 0f ? applyMax : 1f);
            }
            catch
            {
            }

            try
            {
                deathInfo.finalDamage = deathInfo.damageValue;
            }
            catch
            {
            }

            try
            {
                deathInfo.damagePoint = cmc.transform.position;
            }
            catch
            {
            }

            try
            {
                deathInfo.damageNormal = Vector3.up;
            }
            catch
            {
            }

            try
            {
                deathInfo.toDamageReceiver = cmc.mainDamageReceiver;
            }
            catch
            {
            }

            try
            {
                if (playerStatuses != null && sender != null && playerStatuses.TryGetValue(sender, out var st) && st != null)
                    deathInfo.fromCharacter = CharacterMainControl.Main;
            }
            catch
            {
            }
        }

        if (clampedCur <= 0f)
        {
            Server_HandleAuthoritativeAiDeath(cmc, h, aiId, deathInfo, !wasDead);
        }
    }

    private int Server_GetDeathHandleKey(int aiId, CharacterMainControl cmc)
    {
        if (aiId != 0) return aiId;

        if (cmc != null)
        {
            try
            {
                var instId = cmc.GetInstanceID();
                if (instId != 0) return -Mathf.Abs(instId);
            }
            catch
            {
            }
        }

        return int.MinValue;
    }

    private bool Server_TryMarkDeathHandled(int aiId, CharacterMainControl cmc)
    {
        var key = Server_GetDeathHandleKey(aiId, cmc);
        if (key == int.MinValue) return true; // 缺少唯一 key 时直接处理但不去重

        return _srvDeathHandled.Add(key);
    }

    private void Server_EnsureAiFullyDead(CharacterMainControl cmc, Health h, int aiId)
    {
        if (cmc == null || h == null) return;
        if (!Server_TryMarkDeathHandled(aiId, cmc)) return;

        Server_DisableAiAfterDeath(cmc, h);
    }

    private void Server_DisableAiAfterDeath(CharacterMainControl cmc, Health h)
    {
        if (cmc == null || h == null) return;

        try
        {
            var ai = cmc.GetComponent<AICharacterController>();
            if (ai) ai.enabled = false;
        }
        catch
        {
        }

        try
        {
            cmc.enabled = false;
        }
        catch
        {
        }

        UniTask.Void(async () =>
        {
            try
            {
                await UniTask.Delay(50);

                try
                {
                    var hb = MI_GetActiveHealthBar?.Invoke(HealthBarManager.Instance, new object[] { h }) as HealthBar;
                    if (hb != null)
                    {
                        if (MI_ReleaseHealthBar != null)
                            MI_ReleaseHealthBar.Invoke(hb, null);
                        else
                            hb.gameObject.SetActive(false);
                    }
                }
                catch
                {
                }

                try
                {
                    if (cmc != null)
                        cmc.gameObject.SetActive(false);
                }
                catch
                {
                }
            }
            catch
            {
            }
        });
    }


    public void Server_HandleAuthoritativeAiDeath(CharacterMainControl cmc, Health h, int aiId, DamageInfo di, bool triggerEvents)
    {
        if (!IsServer || cmc == null || h == null) return;

        if (aiId == 0)
        {
            var tag = ComponentCache.GetNetAiTag(cmc);
            if (tag != null) aiId = tag.aiId;

            if (aiId == 0)
            {
                foreach (var kv in AITool.aiById)
                    if (kv.Value == cmc)
                    {
                        aiId = kv.Key;
                        break;
                    }
            }
        }

        var firstHandle = Server_TryMarkDeathHandled(aiId, cmc);

        if (firstHandle && networkStarted)
        {
            float broadcastMax = 0f;
            float broadcastCur = 0f;

            try
            {
                broadcastMax = h.MaxHealth;
            }
            catch
            {
            }

            try
            {
                broadcastCur = Mathf.Max(0f, h.CurrentHealth);
            }
            catch
            {
            }

            if (broadcastCur <= 0f)
            {
                if (aiId == 0)
                {
                    var tag = ComponentCache.GetNetAiTag(cmc);
                    if (tag != null) aiId = tag.aiId;

                    if (aiId == 0)
                    {
                        foreach (var kv in AITool.aiById)
                            if (kv.Value == cmc)
                            {
                                aiId = kv.Key;
                                break;
                            }
                    }
                }

                if (aiId != 0)
                    Server_BroadcastAiHealth(aiId, broadcastMax, broadcastCur);
            }
        }

        if (triggerEvents && firstHandle)
        {
            var oldContext = DeadLootSpawnContext.InOnDead;
            DeadLootSpawnContext.InOnDead = cmc;

            try
            {
                if (ModBehaviourF.LogAiHpDebug)
                    Debug.Log($"[AI-HP][SERVER] 触发 OnDeadEvent for aiId={aiId} (authoritative)");

                h.OnDeadEvent?.Invoke(di);

                if (ModBehaviourF.LogAiHpDebug)
                    Debug.Log($"[AI-HP][SERVER] OnDeadEvent 触发完成 for aiId={aiId} (authoritative)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI-HP][SERVER] OnDeadEvent.Invoke failed for aiId={aiId}: {e}");
            }
            finally
            {
                DeadLootSpawnContext.InOnDead = oldContext;
            }

            try
            {
                AITool.TryFireOnDead(h, di);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI-HP][SERVER] TryFireOnDead failed for aiId={aiId}: {e}");
            }
        }

        if (firstHandle)
            Server_DisableAiAfterDeath(cmc, h);
    }


    public void Client_ApplyAiHealth(int aiId, float max, float cur)
    {
        if (IsServer) return;

        // AI 尚未注册：缓存 max/cur，等 RegisterAi 时一起冲
        if (!AITool.aiById.TryGetValue(aiId, out var cmc) || !cmc)
        {
            COOPManager.AIHandle._cliPendingAiHealth[aiId] = cur;
            if (max > 0f) COOPManager.AIHandle._cliPendingAiMax[aiId] = max;

            // 🛡️ 限制日志频率：每200次只输出1次，避免刷屏
            _pendingAiWarningCount++;
            // 注释掉刷屏日志，避免干扰 Debug 输出
            // if (_pendingAiWarningCount == 1 || _pendingAiWarningCount % PENDING_AI_WARNING_INTERVAL == 0)
            // {
            //     Debug.Log($"[AI-HP][CLIENT] pending aiId={aiId} max={max} cur={cur} (已发生 {_pendingAiWarningCount} 次)");
            // }
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

        // 死亡则本地立即隐藏，防"幽灵AI"
        if (cur <= 0f)
        {
            // 【优化】先禁用 AI 控制器（立即生效），其他清理操作延迟执行
            try
            {
                var ai = cmc.GetComponent<AICharacterController>();
                if (ai) ai.enabled = false;
            }
            catch
            {
            }

            // 【优化】延迟清理操作，避免死亡瞬间卡顿
            UniTask.Void(async () =>
            {
                try
                {
                    await UniTask.Delay(50); // 延迟 50ms

                    // 释放/隐藏血条（使用缓存的反射方法）
                    try
                    {
                        var hb = MI_GetActiveHealthBar?.Invoke(HealthBarManager.Instance, new object[] { h }) as HealthBar;
                        if (hb != null)
                        {
                            if (MI_ReleaseHealthBar != null)
                                MI_ReleaseHealthBar.Invoke(hb, null);
                            else
                                hb.gameObject.SetActive(false);
                        }
                    }
                    catch
                    {
                    }

                    // 禁用 GameObject
                    try
                    {
                        if (cmc != null)
                            cmc.gameObject.SetActive(false);
                    }
                    catch
                    {
                    }
                }
                catch
                {
                    // 延迟操作失败不影响游戏
                }
            });

            // 播放死亡特效（保持原有逻辑）
            if (AITool._cliAiDeathFxOnce.Add(aiId))
                FxManager.Client_PlayAiDeathFxAndSfx(cmc);
        }
    }
}
