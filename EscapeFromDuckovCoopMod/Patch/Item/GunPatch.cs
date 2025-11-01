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

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(ItemAgent_Gun), "ShootOneBullet")]
internal static class Patch_BlockClientAiShoot
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ItemAgent_Gun __instance, Vector3 _muzzlePoint, Vector3 _shootDirection, Vector3 firstFrameCheckStartPoint)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        // 主机照常；客户端才需要拦
        if (mod.IsServer) return true;

        var holder = __instance ? __instance.Holder : null;

        // 非本地主角 &&（AI 有 AICharacterController 或 NetAiTag 任一）=> 拦截
        if (holder && holder != CharacterMainControl.Main)
        {
            var isAI = holder.GetComponent<AICharacterController>() != null
                       || holder.GetComponent<NetAiTag>() != null;

            if (isAI)
            {
                if (ModBehaviourF.LogAiHpDebug)
                    Debug.Log($"[CLIENT] Block local AI ShootOneBullet holder='{holder.name}'");
                return false; // 不让客户端本地造弹
            }
        }

        return true;
    }
}

[HarmonyPatch(typeof(ItemAgent_MeleeWeapon), "CheckCollidersInRange")]
internal static class Patch_Melee_FlagLocalDeal
{
    private static void Prefix(ItemAgent_MeleeWeapon __instance, bool dealDamage)
    {
        var mod = ModBehaviourF.Instance;
        var isClient = mod != null && mod.networkStarted && !mod.IsServer;
        var fromLocalMain = __instance && __instance.Holder == CharacterMainControl.Main;
        MeleeLocalGuard.LocalMeleeTryingToHurt = isClient && fromLocalMain && dealDamage;
    }

    private static void Postfix()
    {
        MeleeLocalGuard.LocalMeleeTryingToHurt = false;
    }
}

// 客户端：在本地生成真实弹丸的同时，追加 FIRE_REQUEST 提示给主机
[HarmonyPatch(typeof(ItemAgent_Gun), "ShootOneBullet")]
public static class Patch_ShootOneBullet_Client
{
    private static bool Prefix(ItemAgent_Gun __instance, Vector3 _muzzlePoint, Vector3 _shootDirection, Vector3 firstFrameCheckStartPoint)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        var isClient = !mod.IsServer;
        if (!isClient) return true;

        var holder = __instance.Holder;
        var isLocalMain = holder == CharacterMainControl.Main;
        var isAI = holder && holder.GetComponent<NetAiTag>() != null;

        if (isLocalMain)
        {
            COOPManager.WeaponRequest.Net_OnClientShoot(__instance, _muzzlePoint, _shootDirection, firstFrameCheckStartPoint);
            return true; // 允许本地生成真实子弹
        }

        if (isAI) return false; // 客户端看到的AI，等主机的 FIRE_EVENT
        if (!isLocalMain) return false;
        return true;
    }
}

// 服务端：在 Projectile.Init 后，把“服务端算好的弹丸参数”一并广播给所有客户端
[HarmonyPatch(typeof(Projectile), nameof(Projectile.Init), typeof(ProjectileContext))]
internal static class Patch_ProjectileInit_Broadcast
{
    private static void Postfix(Projectile __instance, ref ProjectileContext _context)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.IsServer || __instance == null) return;

        if (COOPManager.WeaponHandle._serverSpawnedFromClient != null && COOPManager.WeaponHandle._serverSpawnedFromClient.Contains(__instance)) return;

        var fromC = _context.fromCharacter;
        if (!fromC) return;

        string shooterId = null;
        if (fromC.IsMainCharacter)
        {
            shooterId = mod.localPlayerStatus?.EndPoint;
        }
        else
        {
            var tag = fromC.GetComponent<NetAiTag>();
            if (tag == null || tag.aiId == 0) return;
            shooterId = $"AI:{tag.aiId}";
        }

        var weaponType = 0;
        try
        {
            var gun = fromC.GetGun();
            if (gun != null && gun.Item != null) weaponType = gun.Item.TypeID;
        }
        catch
        {
        }

        var w = new NetDataWriter();
        w.Put((byte)Op.FIRE_EVENT);
        w.Put(shooterId);
        w.Put(weaponType);
        w.PutV3cm(__instance.transform.position); // 近似 muzzle
        w.PutDir(_context.direction);
        w.Put(_context.speed);
        w.Put(_context.distance);
        w.Put(true); // 客户端收到后仅生成假子弹

        // 把服务端算好的弹丸参数一并带上（含 explosionRange / explosionDamage 等）
        w.PutProjectilePayload(_context);

        mod.netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }
}