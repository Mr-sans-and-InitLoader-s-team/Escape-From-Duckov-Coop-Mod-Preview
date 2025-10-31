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

using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
public static class Patch_Grenade_Sync
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Skill_Grenade), nameof(Skill_Grenade.OnRelease))]
    private static bool Skill_Grenade_OnRelease_Prefix(Skill_Grenade __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        // —— 无论主机/客户端，都先缓存一次 ——
        try
        {
            var prefab = __instance.grenadePfb; // public 字段
            var typeId = 0;
            try
            {
                typeId = __instance.fromItem != null ? __instance.fromItem.TypeID : __instance.damageInfo.fromWeaponItemID;
            }
            catch
            {
            }

            if (prefab) CoopTool.CacheGrenadePrefab(typeId, prefab);
        }
        catch
        {
        }


        if (mod.IsServer)
        {
            // 服务器本地丢：确保 damageInfo.fromWeaponItemID 被写入，供 Grenade.Launch Postfix 读取
            try
            {
                var tid = 0;
                try
                {
                    if (__instance.fromItem != null) tid = __instance.fromItem.TypeID;
                }
                catch
                {
                }

                if (tid == 0)
                    try
                    {
                        tid = __instance.damageInfo.fromWeaponItemID;
                    }
                    catch
                    {
                    }

                if (tid != 0)
                    try
                    {
                        __instance.damageInfo.fromWeaponItemID = tid;
                    }
                    catch
                    {
                    }
            }
            catch
            {
            }

            // 放行原流程，由 Launch 的 Postfix 广播
            return true;
        }


        // 未连接客户端：不拦截
        if (mod.connectedPeer == null || mod.connectedPeer.ConnectionState != ConnectionState.Connected) return true;

        // 只拦“本地主角”
        CharacterMainControl fromChar = null;
        try
        {
            var f_from = AccessTools.Field(typeof(SkillBase), "fromCharacter");
            fromChar = f_from?.GetValue(__instance) as CharacterMainControl;
        }
        catch
        {
        }

        if (fromChar != CharacterMainControl.Main) return true;

        try
        {
            var position = fromChar ? fromChar.CurrentUsingAimSocket.position : Vector3.zero;

            var releasePoint = Vector3.zero;
            var relCtx = AccessTools.Field(typeof(SkillBase), "skillReleaseContext")?.GetValue(__instance);
            if (relCtx != null)
            {
                var f_rp = AccessTools.Field(relCtx.GetType(), "releasePoint");
                if (f_rp != null) releasePoint = (Vector3)f_rp.GetValue(relCtx);
            }

            var y = releasePoint.y;
            var point = releasePoint - (fromChar ? fromChar.transform.position : Vector3.zero);
            point.y = 0f;
            var dist = point.magnitude;
            var ctxObj = AccessTools.Field(typeof(SkillBase), "skillContext")?.GetValue(__instance);
            if (!__instance.canControlCastDistance && ctxObj != null)
            {
                var f_castRange = AccessTools.Field(ctxObj.GetType(), "castRange");
                if (f_castRange != null) dist = (float)f_castRange.GetValue(ctxObj);
            }

            point.Normalize();
            var target = position + point * dist;
            target.y = y;

            float vert = 8f, effectRange = 3f;
            if (ctxObj != null)
            {
                var f_vert = AccessTools.Field(ctxObj.GetType(), "grenageVerticleSpeed");
                var f_eff = AccessTools.Field(ctxObj.GetType(), "effectRange");
                if (f_vert != null) vert = (float)f_vert.GetValue(ctxObj);
                if (f_eff != null) effectRange = (float)f_eff.GetValue(ctxObj);
            }

            var velocity = __instance.CalculateVelocity(position, target, vert);

            var prefabType = __instance.grenadePfb ? __instance.grenadePfb.GetType().FullName : string.Empty;
            var prefabName = __instance.grenadePfb ? __instance.grenadePfb.name : string.Empty;
            var typeId2 = 0;
            try
            {
                typeId2 = __instance.fromItem != null ? __instance.fromItem.TypeID : __instance.damageInfo.fromWeaponItemID;
            }
            catch
            {
            }

            var createExplosion = __instance.createExplosion;
            var shake = __instance.explosionShakeStrength;
            var damageRange = effectRange;
            var delayFromCollide = __instance.delayFromCollide;
            var delayTime = __instance.delay;
            var isLandmine = __instance.isLandmine;
            var landmineRange = __instance.landmineTriggerRange;

            // 只发请求，不本地生成
            COOPManager.GrenadeM.Net_OnClientThrow(__instance, typeId2, prefabType, prefabName, position, velocity,
                createExplosion, shake, damageRange, delayFromCollide, delayTime, isLandmine, landmineRange);
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GRENADE Prefix] exception -> pass through: " + e);
            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Grenade), nameof(Grenade.Launch))]
    private static void Grenade_Launch_Postfix(Grenade __instance, Vector3 startPoint, Vector3 velocity, CharacterMainControl fromCharacter)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var typeId = 0;
        try
        {
            typeId = __instance.damageInfo.fromWeaponItemID;
        }
        catch
        {
        }

        if (typeId == 0)
            try
            {
                typeId = Traverse.Create(__instance).Field<ItemAgent>("bindedAgent").Value.Item.TypeID;
            }
            catch
            {
            }

        COOPManager.GrenadeM.Server_OnGrenadeLaunched(__instance, startPoint, velocity, typeId);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Grenade), "Explode")]
    private static void Grenade_Explode_Prefix(Grenade __instance, ref bool __state)
    {
        __state = __instance.createExplosion;
        var mod = ModBehaviourF.Instance;
        if (mod != null && mod.networkStarted && !mod.IsServer)
        {
            var isNetworkGrenade = __instance && __instance.GetComponent<NetGrenadeTag>() != null;
            if (!isNetworkGrenade)
                __instance.createExplosion = false;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Grenade), "Explode")]
    private static void Grenade_Explode_Postfix(Grenade __instance, bool __state)
    {
        var mod = ModBehaviourF.Instance;
        if (mod != null && mod.networkStarted && !mod.IsServer) __instance.createExplosion = __state;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Grenade), "Explode")]
    private static void Grenade_Explode_ServerBroadcast(Grenade __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;
        COOPManager.GrenadeM.Server_OnGrenadeExploded(__instance);
    }
}

[HarmonyPatch(typeof(Breakable), "Awake")]
internal static class Patch_Breakable_Awake_ForceVisibleInCoop
{
    private static void Postfix(Breakable __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        // 确保可破坏体带 NetDestructibleTag 并注册
        try
        {
            var hs = __instance.simpleHealth;
            if (hs)
            {
                var tag = hs.GetComponent<NetDestructibleTag>() ?? hs.gameObject.AddComponent<NetDestructibleTag>();
                tag.id = NetDestructibleTag.ComputeStableId(hs.gameObject);
                COOPManager.destructible.RegisterDestructible(tag.id, hs);
            }
        }
        catch
        {
        }

        // 仅客户端：把 Awake 里因本地 Save 关掉的外观/碰撞体全部拉回“未破坏”
        if (!mod.IsServer)
            try
            {
                if (__instance.normalVisual) __instance.normalVisual.SetActive(true);
                if (__instance.dangerVisual) __instance.dangerVisual.SetActive(false);
                if (__instance.breakedVisual) __instance.breakedVisual.SetActive(false);
                if (__instance.mainCollider) __instance.mainCollider.SetActive(true);

                var hs = __instance.simpleHealth;
                if (hs && hs.dmgReceiver) hs.dmgReceiver.gameObject.SetActive(true);
            }
            catch
            {
            }
    }
}