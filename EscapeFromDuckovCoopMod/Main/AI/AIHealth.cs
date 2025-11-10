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
using Duckov.UI;

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
    public void Server_BroadcastAiHealth(int aiId, float maxHealth, float currentHealth, DamageInfo dmg)
    {
        if (!networkStarted || !IsServer) return;
        var w = new NetDataWriter();
        w.Put((byte)Op.AI_HEALTH_SYNC);
        w.Put(aiId);
        w.Put(maxHealth);
        w.Put(currentHealth);
        w.PutDamagePayload(
            dmg.damageValue, dmg.armorPiercing, dmg.critDamageFactor, dmg.critRate, dmg.crit,
            dmg.damagePoint, dmg.damageNormal, dmg.fromWeaponItemID, dmg.bleedChance, dmg.isExplosion,
            0f
        );
        netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }


    public void Client_ApplyAiHealth(int aiId, float max, float cur, DamageInfo dmg)
    {
        if (IsServer) return;

        // AI 尚未注册：缓存 max/cur，等 RegisterAi 时一起冲
        if (!AITool.aiById.TryGetValue(aiId, out var cmc) || !cmc)
        {
            COOPManager.AIHandle._cliPendingAiHealth[aiId] = cur;
            if (max > 0f) COOPManager.AIHandle._cliPendingAiMax[aiId] = max;
            Debug.Log($"[AI-HP][CLIENT] pending aiId={aiId} max={max} cur={cur}");
            return;
        }

        var h = cmc.Health;
        if (!h) return;

        try
        {
            var prev = 0f;
            _cliLastAiHp.TryGetValue(aiId, out prev);
            _cliLastAiHp[aiId] = cur;

            if (dmg.damageValue > 0f)
            {
                var pos = cmc.transform.position + Vector3.up * 1.1f;
                var headArmor = h.HeadArmor;
                var bodyArmor = h.BodyArmor;
                var isAimingHead = LevelManager.Instance.InputManager.AimingEnemyHead;
                var armor = isAimingHead ? headArmor : bodyArmor;
                dmg.damagePoint = pos;
                dmg.damageNormal = Vector3.up;
                if (dmg.crit > 0) dmg.damageValue *= dmg.critDamageFactor;
                dmg.damageValue *= 2f / (Mathf.Clamp(armor - dmg.armorPiercing, 0f, 999f) + 2f);
                LocalHitKillFx.PopDamageText(pos, dmg);
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