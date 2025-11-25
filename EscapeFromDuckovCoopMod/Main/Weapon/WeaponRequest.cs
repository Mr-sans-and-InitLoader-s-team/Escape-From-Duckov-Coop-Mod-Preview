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

public class WeaponRequest
{
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;

    private static ProjectileContext BuildPayload(ItemAgent_Gun gun, out bool hasPayload)
    {
        var ctx = new ProjectileContext();
        hasPayload = false;
        if (!gun) return ctx;

        var hasBulletItem = gun.BulletItem != null;
        try
        {
            var charMul = gun.CharacterDamageMultiplier;
            var bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
            var shots = Mathf.Max(1, gun.ShotCount);
            ctx.damage = gun.Damage * bulletMul * charMul / shots;
            if (gun.Damage > 1f && ctx.damage < 1f) ctx.damage = 1f;
            hasPayload |= ctx.damage > 0f;
        }
        catch { }

        try
        {
            var bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
            var bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
            ctx.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
            ctx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);
            hasPayload |= ctx.critDamageFactor > 0f || ctx.critRate > 0f;
        }
        catch { }

        try
        {
            var apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
            var abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
            ctx.armorPiercing = gun.ArmorPiercing + apGain;
            ctx.armorBreak = gun.ArmorBreak + abGain;
            hasPayload |= ctx.armorPiercing > 0f || ctx.armorBreak > 0f;
        }
        catch { }

        try
        {
            var setting = gun.GunItemSetting;
            if (setting != null)
                switch (setting.element)
                {
                    case ElementTypes.physics: ctx.element_Physics = 1f; break;
                    case ElementTypes.fire: ctx.element_Fire = 1f; break;
                    case ElementTypes.poison: ctx.element_Poison = 1f; break;
                    case ElementTypes.electricity: ctx.element_Electricity = 1f; break;
                    case ElementTypes.space: ctx.element_Space = 1f; break;
                }

            ctx.explosionRange = gun.BulletExplosionRange;
            ctx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
            if (hasBulletItem)
            {
                ctx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                ctx.bleedChance = gun.BulletBleedChance;
            }

            ctx.penetrate = gun.Penetrate;
            ctx.fromWeaponItemID = gun.Item != null ? gun.Item.TypeID : 0;
            hasPayload |= ctx.explosionRange > 0f || ctx.explosionDamage > 0f || ctx.penetrate != 0;
        }
        catch { }

        return ctx;
    }

    // 客户端：拦截本地生成后，向主机发开火请求
    public void Net_OnClientShoot(ItemAgent_Gun gun, Vector3 muzzle, Vector3 baseDir, Vector3 firstCheckStart)
    {
        if (IsServer || !networkStarted || localPlayerStatus == null) return;

        if (baseDir.sqrMagnitude < 1e-8f)
        {
            var fallback = gun != null && gun.muzzle != null ? gun.muzzle.forward : Vector3.forward;
            baseDir = fallback.sqrMagnitude < 1e-8f ? Vector3.forward : fallback.normalized;
        }

        if (gun && gun.muzzle)
        {
            var weaponType = gun.Item != null ? gun.Item.TypeID : 0;
            FxManager.Client_PlayLocalShotFx(gun, gun.muzzle, weaponType);
        }

        var speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
        var distance = gun.BulletDistance + 0.4f;

        var payload = BuildPayload(gun, out var hasPayload);
        payload.firstFrameCheckStartPoint = firstCheckStart;
        payload.team = gun.Holder ? gun.Holder.Team : (int)Teams.player;

        var rpc = new WeaponFireRequestRpc
        {
            ShooterId = localPlayerStatus.EndPoint,
            WeaponTypeId = gun.Item.TypeID,
            MuzzlePosition = muzzle,
            Direction = baseDir,
            Speed = speed,
            Distance = distance,
            PlayFx = true,
            Team = (int)payload.team,
            AiId = 0,
            HasPayload = hasPayload,
            Payload = payload
        };

        CoopTool.SendRpc(in rpc);
    }

    // 客户端：近战起手用于远端看得见
    public void Net_OnClientMeleeAttack(float dealDelay, Vector3 snapPos, Vector3 snapDir)
    {
        if (!networkStarted || IsServer) return;
        var pid = localPlayerStatus?.EndPoint;
        if (string.IsNullOrEmpty(pid))
            pid = NetService.Instance?.GetSelfNetworkId();

        var rpc = new MeleeSwingRequestRpc
        {
            PlayerId = pid ?? string.Empty,
            DealDelay = dealDelay,
            SnapshotPosition = snapPos,
            SnapshotDirection = snapDir
        };
        Debug.Log("Net_OnClientMeleeAttack "+ rpc.PlayerId);
        CoopTool.SendRpc(in rpc);
    }
}
