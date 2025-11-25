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

[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.Attack))]
public static class Patch_Melee_Attack_Request
{
    private static void Postfix(CharacterMainControl __instance, bool __result)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !__result)
            return;

        var holder = __instance ? __instance.GetMeleeWeapon() : null;
        if (!holder) return;

        if (mod.IsServer)
        {
            var aiId = 0;
            var pid = string.Empty;

            if (__instance.IsMainCharacter())
            {
                pid = mod.localPlayerStatus?.EndPoint;
            }
            else
            {
                var ai = __instance.GetComponentInChildren<AICharacterController>();
                if (ai && CoopSyncDatabase.AI.TryGet(ai, out var entry) && entry != null)
                    aiId = entry.Id;
            }

            var broadcast = new MeleeSwingBroadcastRpc
            {
                PlayerId = pid,
                AiId = aiId,
                DealDelay = 0f,
                SnapshotPosition = holder.transform.position,
                SnapshotDirection = holder.transform.forward
            };

            COOPManager.WeaponHandle.Client_HandleMeleeSwing(in broadcast);
            CoopTool.SendRpc(in broadcast);
            return;
        }

        if (holder.Holder != CharacterMainControl.Main)
            return;

        var pos = holder.transform.position;
        var dir = holder.transform.forward;

        var delay = 0f;
        try
        {
            delay = Traverse.Create(__instance).Field<float>("dealDamageDelay").Value;
        }
        catch
        {
            try
            {
                delay = Traverse.Create(__instance).Field<float>("dealDelay").Value;
            }
            catch
            {
            }
        }

        COOPManager.WeaponRequest.Net_OnClientMeleeAttack(delay, pos, dir);
    }
}

// 客户端：在本地生成真实弹丸的同时，追加 FIRE_REQUEST 提示给主机
[HarmonyPatch(typeof(ItemAgent_Gun), "ShootOneBullet")]
public static class Patch_ShootOneBullet_Client
{
    private struct FireState
    {
        public bool ShouldNotify;
        public Vector3 MuzzlePoint;
        public Vector3 FirstFrameCheckStart;
    }

    private static void Prefix(ItemAgent_Gun __instance, Vector3 _muzzlePoint, Vector3 _shootDirection, Vector3 firstFrameCheckStartPoint, ref FireState __state)
    {
        var mod = ModBehaviourF.Instance;
        var shouldSend = mod != null && mod.networkStarted && !mod.IsServer && __instance.Holder == CharacterMainControl.Main;

        __state = new FireState
        {
            ShouldNotify = shouldSend,
            MuzzlePoint = _muzzlePoint,
            FirstFrameCheckStart = firstFrameCheckStartPoint
        };
    }

    private static void Postfix(ItemAgent_Gun __instance, FireState __state)
    {
        if (!__state.ShouldNotify)
            return;

        Projectile proj = null;
        try
        {
            proj = Traverse.Create(__instance).Field<Projectile>("projInst").Value;
        }
        catch
        {
        }

        var muzzle = proj ? proj.transform.position : __state.MuzzlePoint;
        var dir = proj ? proj.transform.forward : (__instance.muzzle ? __instance.muzzle.forward : __instance.transform.forward);
        if (dir.sqrMagnitude < 1e-8f)
            dir = Vector3.forward;

        COOPManager.WeaponRequest.Net_OnClientShoot(__instance, muzzle, dir.normalized, __state.FirstFrameCheckStart);
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

        var fromC = _context.fromCharacter;
        if (!fromC) return;

        string shooterId = null;
        var aiId = 0;
        if (fromC.IsMainCharacter())
        {
            shooterId = mod.localPlayerStatus?.EndPoint;
        }
        else
        {
            var ai = fromC.GetComponentInChildren<AICharacterController>();
            if (ai && CoopSyncDatabase.AI.TryGet(ai, out var entry) && entry != null)
                aiId = entry.Id;
        }

        var weaponType = 0;
        ItemAgent_Gun gun = null;
        try
        {
            gun = fromC.GetGun();
            if (gun != null && gun.Item != null) weaponType = gun.Item.TypeID;
        }
        catch
        {
        }

        _context.firstFrameCheckStartPoint = _context.firstFrameCheckStartPoint == default
            ? __instance.transform.position
            : _context.firstFrameCheckStartPoint;
        _context.direction = _context.direction.sqrMagnitude < 1e-8f
            ? __instance.transform.forward
            : _context.direction;
        _context.distance = _context.distance <= 0f
            ? (gun ? gun.BulletDistance + 0.4f : 50f)
            : _context.distance;
        _context.speed = _context.speed <= 0f
            ? (gun ? gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f) : 60f)
            : _context.speed;
        _context.team = _context.team == 0 && fromC != null ? fromC.Team : _context.team;

        COOPManager.WeaponHandle.Server_BroadcastProjectileSpawn(gun, _context, __instance.transform.position,
            _context.direction, aiId, shooterId);
    }
}