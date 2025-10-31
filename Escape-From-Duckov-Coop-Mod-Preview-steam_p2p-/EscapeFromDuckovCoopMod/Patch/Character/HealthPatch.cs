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
using Duckov.Utilities;
using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(HealthSimpleBase), "Awake")]
public static class Patch_HSB_Awake_TagRegister
{
    private static void Postfix(HealthSimpleBase __instance)
    {
        if (!__instance) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) return; // 你已标注在墙/油桶上了，这里不再 AddComponent

        // —— BreakableWall：用墙根节点来计算稳定ID，避免主客机层级差导致错位 —— //
        var wallRoot = FindBreakableWallRoot(__instance.transform);
        if (wallRoot != null)
            try
            {
                var computed = NetDestructibleTag.ComputeStableId(wallRoot.gameObject);
                if (tag.id != computed) tag.id = computed;
            }
            catch
            {
            }

        // —— 幂等注册 —— //
        var mod = ModBehaviourF.Instance;
        if (mod != null) COOPManager.destructible.RegisterDestructible(tag.id, __instance);
    }

    // 向上找名字含“BreakableWall”的祖先（不区分大小写）
    private static Transform FindBreakableWallRoot(Transform t)
    {
        var p = t;
        while (p != null)
        {
            var nm = p.name;
            if (!string.IsNullOrEmpty(nm) &&
                nm.IndexOf("BreakableWall", StringComparison.OrdinalIgnoreCase) >= 0)
                return p;
            p = p.parent;
        }

        return null;
    }
}

// 客户端：阻断本地扣血，改为请求主机结算；
// 主机：照常结算（原方法运行），并在 Postfix 广播受击
[HarmonyPatch(typeof(HealthSimpleBase), "OnHurt")]
public static class Patch_HSB_OnHurt_RedirectNet
{
    private static bool Prefix(HealthSimpleBase __instance, DamageInfo dmgInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        if (!mod.IsServer)
        {
            // 本地 UI：子弹/爆炸统一点亮 Hit；若你能在此处判断“必死”，可传 true 亮 Kill
            LocalHitKillFx.ClientPlayForDestructible(__instance, dmgInfo, false);

            var tag = __instance.GetComponent<NetDestructibleTag>();
            if (!tag) tag = __instance.gameObject.AddComponent<NetDestructibleTag>();
            COOPManager.HurtM.Client_RequestDestructibleHurt(tag.id, dmgInfo);
            return false;
        }

        return true;
    }

    private static void Postfix(HealthSimpleBase __instance, DamageInfo dmgInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) return;
        COOPManager.destructible.Server_BroadcastDestructibleHurt(tag.id, __instance.HealthValue, dmgInfo);
    }
}

// 主机在死亡后广播；客户端收到“死亡广播”时只做视觉切换
[HarmonyPatch(typeof(HealthSimpleBase), "Dead")]
public static class Patch_HSB_Dead_Broadcast
{
    private static void Postfix(HealthSimpleBase __instance, DamageInfo dmgInfo)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var tag = __instance.GetComponent<NetDestructibleTag>();
        if (!tag) return;
        COOPManager.destructible.Server_BroadcastDestructibleDead(tag.id, dmgInfo);
    }
}

// 回血/设血：主机也要广播（治疗、药效、脚本设血等）
[HarmonyPatch(typeof(Health), "SetHealth")]
internal static class Patch_AIHealth_SetHealth_Broadcast
{
    private static void Postfix(Health __instance, float healthValue)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var cmc = __instance.TryGetCharacter();
        if (!cmc)
            try
            {
                cmc = __instance.GetComponentInParent<CharacterMainControl>();
            }
            catch
            {
            }

        if (!cmc || !cmc.GetComponent<NetAiTag>()) return;

        var tag = cmc.GetComponent<NetAiTag>();
        if (!tag) return;

        if (ModBehaviourF.LogAiHpDebug) Debug.Log($"[AI-HP][SERVER] SetHealth => broadcast aiId={tag.aiId} cur={__instance.CurrentHealth}");
        COOPManager.AIHealth.Server_BroadcastAiHealth(tag.aiId, __instance.MaxHealth, __instance.CurrentHealth);
    }
}

[HarmonyPatch(typeof(Health), "AddHealth")]
internal static class Patch_AIHealth_AddHealth_Broadcast
{
    private static void Postfix(Health __instance, float healthValue)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var cmc = __instance.TryGetCharacter();
        if (!cmc)
            try
            {
                cmc = __instance.GetComponentInParent<CharacterMainControl>();
            }
            catch
            {
            }

        if (!cmc || !cmc.GetComponent<NetAiTag>()) return;

        var tag = cmc.GetComponent<NetAiTag>();
        if (!tag) return;

        if (ModBehaviourF.LogAiHpDebug) Debug.Log($"[AI-HP][SERVER] AddHealth => broadcast aiId={tag.aiId} cur={__instance.CurrentHealth}");
        COOPManager.AIHealth.Server_BroadcastAiHealth(tag.aiId, __instance.MaxHealth, __instance.CurrentHealth);
    }
}

[HarmonyPatch(typeof(HealthBar), "RefreshCharacterIcon")]
internal static class Patch_HealthBar_RefreshCharacterIcon_Override
{
    private static void Postfix(HealthBar __instance)
    {
        try
        {
            var h = __instance.target;
            if (!h) return;

            var cmc = h.TryGetCharacter();
            if (!cmc) return;

            var tag = cmc.GetComponent<NetAiTag>();
            if (!tag) return;

            // 若没有任何覆写数据，就不动原版结果
            var hasIcon = tag.iconTypeOverride.HasValue;
            var hasShow = tag.showNameOverride.HasValue;
            var hasName = !string.IsNullOrEmpty(tag.nameOverride);
            if (!hasIcon && !hasShow && !hasName) return;

            // 取到 UI 私有字段
            var tr = Traverse.Create(__instance);
            var levelIcon = tr.Field<Image>("levelIcon").Value;
            var nameText = tr.Field<TextMeshProUGUI>("nameText").Value;

            // 1) 图标覆写（有就用，没有就保留原版）
            if (levelIcon && hasIcon)
            {
                var sp = ResolveIconSpriteCompat(tag.iconTypeOverride.Value);
                if (sp)
                {
                    levelIcon.sprite = sp;
                    levelIcon.gameObject.SetActive(true);
                }
                else
                {
                    levelIcon.gameObject.SetActive(false);
                }
            }

            // 2) 名字显隐与文本（主机裁决优先；boss/elete 兜底强制显示）
            var show = hasShow ? tag.showNameOverride.Value : cmc.characterPreset ? cmc.characterPreset.showName : false;
            if (tag.iconTypeOverride.HasValue)
            {
                var t = (CharacterIconTypes)tag.iconTypeOverride.Value;
                if (!show && (t == CharacterIconTypes.boss || t == CharacterIconTypes.elete))
                    show = true;
            }

            if (nameText)
            {
                if (show)
                {
                    if (hasName) nameText.text = tag.nameOverride;
                    nameText.gameObject.SetActive(true);
                }
                else
                {
                    nameText.gameObject.SetActive(false);
                }
            }
        }
        catch
        {
            /* 防守式：别让UI崩 */
        }
    }

    // 拷一份兼容的解析函数（避免跨文件访问）
    private static Sprite ResolveIconSpriteCompat(int iconType)
    {
        switch ((CharacterIconTypes)iconType)
        {
            case CharacterIconTypes.elete: return GameplayDataSettings.UIStyle.EleteCharacterIcon;
            case CharacterIconTypes.pmc: return GameplayDataSettings.UIStyle.PmcCharacterIcon;
            case CharacterIconTypes.boss: return GameplayDataSettings.UIStyle.BossCharacterIcon;
            case CharacterIconTypes.merchant: return GameplayDataSettings.UIStyle.MerchantCharacterIcon;
            case CharacterIconTypes.pet: return GameplayDataSettings.UIStyle.PetCharacterIcon;
            default: return null;
        }
    }
}

[HarmonyPatch(typeof(Health), "get_MaxHealth")]
internal static class Patch_Health_get_MaxHealth_ClientOverride
{
    private static void Postfix(Health __instance, ref float __result)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || mod.IsServer) return;

        // 只给 AI 覆盖（避免动到玩家自身的本地 UI）
        var cmc = __instance.TryGetCharacter();
        var isAI = cmc && (cmc.GetComponent<AICharacterController>() != null || cmc.GetComponent<NetAiTag>() != null);
        if (!isAI) return;

        if (HealthM.Instance.TryGetClientMaxOverride(__instance, out var v) && v > 0f)
            if (__result <= 0f || v > __result)
                __result = v;
    }
}

[HarmonyPatch(typeof(HealthSimpleBase), "OnHurt")]
internal static class Patch_HealthSimpleBase_OnHurt_RedirectNet
{
    private static bool Prefix(HealthSimpleBase __instance, ref DamageInfo __0)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        // 必须是本机玩家的命中才拦截；防止 AI 打障碍物也触发 UI
        var from = __0.fromCharacter;
        var fromLocalMain = from == CharacterMainControl.Main;

        if (!mod.IsServer && fromLocalMain)
        {
            // 预测是否致死（简单用 HealthValue 判断，足够做“演出预判”）
            var predictedDead = false;
            try
            {
                var cur = __instance.HealthValue;
                predictedDead = cur > 0f && __0.damageValue >= cur - 0.001f;
            }
            catch
            {
            }

            LocalHitKillFx.ClientPlayForDestructible(__instance, __0, predictedDead);

            // 继续你的原有逻辑：把命中发给主机权威结算
            return false;
        }

        return true;
    }
}

// 统一给所有可破坏体（HealthSimpleBase）打上 NetDestructibleTag 并注册进索引
[HarmonyPatch(typeof(HealthSimpleBase), "Awake")]
internal static class Patch_HSB_Awake_AddTagAndRegister
{
    private static void Postfix(HealthSimpleBase __instance)
    {
        try
        {
            var mod = ModBehaviourF.Instance;
            if (mod == null) return;

            // 没有就补一个
            var tag = __instance.GetComponent<NetDestructibleTag>();
            if (!tag) tag = __instance.gameObject.AddComponent<NetDestructibleTag>();

            // 尽量用“墙体根”等稳定根节点算稳定ID；失败则退回到自身
            uint id = 0;
            try
            {
                // 你已有的稳定ID算法在 Mod.cs 里；这里直接复用 NetDestructibleTag 的稳定计算兜底
                id = NetDestructibleTag.ComputeStableId(__instance.gameObject);
            }
            catch
            {
                /* 忽略差异 */
            }

            tag.id = id;
            COOPManager.destructible.RegisterDestructible(id, __instance);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Coop][HSB.Awake] Tag/Register failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(Health), "DestroyOnDelay")]
internal static class Patch_Health_DestroyOnDelay_SkipForAI_Server
{
    private static bool Prefix(Health __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return true;

        CharacterMainControl cmc = null;
        try
        {
            cmc = __instance.TryGetCharacter();
        }
        catch
        {
        }

        if (!cmc)
            try
            {
                cmc = __instance.GetComponentInParent<CharacterMainControl>();
            }
            catch
            {
            }

        var isAI = cmc &&
                   (cmc.GetComponent<AICharacterController>() != null ||
                    cmc.GetComponent<NetAiTag>() != null);
        if (!isAI) return true;

        // 对 AI：主机不再走原 DestroyOnDelay，避免已销毁对象的后续访问导致 NRE
        return false;
    }
}

// 兜底：即使有第三方路径仍触发 DestroyOnDelay，吞掉异常防止打断主循环（可选）
[HarmonyPatch(typeof(Health), "DestroyOnDelay")]
internal static class Patch_Health_DestroyOnDelay_Finalizer
{
    private static Exception Finalizer(Exception __exception)
    {
        // 返回 null 表示吞掉异常
        if (__exception != null)
            Debug.LogWarning("[COOP] Swallow DestroyOnDelay exception: " + __exception.Message);
        return null;
    }
}