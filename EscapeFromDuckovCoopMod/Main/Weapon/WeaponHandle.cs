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

using Duckov.Utilities;

namespace EscapeFromDuckovCoopMod;

public class WeaponHandle
{
    private readonly Dictionary<int, float> _distCacheByWeaponType = new();
    private readonly Dictionary<int, float> _explDamageCacheByWeaponType = new();

    // 爆炸参数缓存（主机记住每种武器的爆炸半径/伤害）
    private readonly Dictionary<int, float> _explRangeCacheByWeaponType = new();

    private readonly Dictionary<int, Projectile> _projectilePrefabCache = new();

    private readonly Dictionary<int, float> _speedCacheByWeaponType = new();
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    private CharacterMainControl TryResolveShooter(string shooterId, int aiId, out ItemAgent_Gun resolvedGun)
    {
        resolvedGun = null;

        if (!string.IsNullOrEmpty(shooterId))
        {
            if (NetService.Instance.IsSelfId(shooterId))
            {
                var main = CharacterMainControl.Main;
                if (main)
                {
                    resolvedGun = main.GetGun();
                    return main;
                }
            }
            else if (clientRemoteCharacters.TryGetValue(shooterId, out var remote) && remote)
            {
                var cmc = remote.GetComponent<CharacterMainControl>();
                if (cmc)
                {
                    resolvedGun = cmc.GetGun();
                    return cmc;
                }
            }
            else if (IsServer && remoteCharacters != null)
            {
                var srv = Service;
                foreach (var kv in remoteCharacters)
                {
                    if (kv.Value && srv != null && srv.GetPlayerId(kv.Key) == shooterId)
                    {
                        var cmc = kv.Value.GetComponent<CharacterMainControl>();
                        if (cmc)
                        {
                            resolvedGun = cmc.GetGun();
                            return cmc;
                        }
                    }
                }
            }
        }

        if (aiId != 0 && COOPManager.AI != null)
        {
            var cmc = COOPManager.AI.TryGetCharacter(aiId);
            if (cmc)
            {
                resolvedGun = cmc.GetGun();
                return cmc;
            }
        }

        return null;
    }

    private void EmitGunshotSound(CharacterMainControl shooter, ItemAgent_Gun gun, Vector3 muzzlePos, Teams team)
    {
        if (!IsServer)
            return;

        var radius = gun ? gun.SoundRange : 0f;
        if (radius <= 0f)
            radius = 22f; // 兜底值，避免静默

        if (team == 0 && shooter)
            team = shooter.Team;

        var sound = new AISound
        {
            pos = muzzlePos,
            fromTeam = team,
            soundType = SoundTypes.combatSound,
            fromObject = gun ? gun.gameObject : shooter ? shooter.gameObject : null,
            fromCharacter = shooter,
            radius = radius
        };

        AIMainBrain.MakeSound(sound);
    }

    private (ProjectileContext ctx, bool hasPayload) BuildPayloadFromGun(ItemAgent_Gun gun)
    {
        var ctx = new ProjectileContext();
        var hasPayload = false;
        if (!gun) return (ctx, false);

        var hasBulletItem = gun.BulletItem != null;
        try
        {
            var charMul = gun.CharacterDamageMultiplier;
            var bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
            var shots = Mathf.Max(1, gun.ShotCount);
            ctx.damage = gun.Damage * bulletMul * charMul / shots;
            if (gun.Damage > 1f && ctx.damage < 1f) ctx.damage = 1f;
            hasPayload = hasPayload || ctx.damage > 0f;
        }
        catch { }

        try
        {
            var bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
            var bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
            ctx.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
            ctx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);
            hasPayload = hasPayload || ctx.critDamageFactor > 0f || ctx.critRate > 0f;
        }
        catch { }

        try
        {
            var apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
            var abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
            ctx.armorPiercing = gun.ArmorPiercing + apGain;
            ctx.armorBreak = gun.ArmorBreak + abGain;
            hasPayload = hasPayload || ctx.armorPiercing > 0f || ctx.armorBreak > 0f;
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
            hasPayload = hasPayload || ctx.explosionRange > 0f || ctx.explosionDamage > 0f || ctx.penetrate != 0;
        }
        catch { }

        if (ctx.explosionRange > 0f) _explRangeCacheByWeaponType[gun.Item.TypeID] = ctx.explosionRange;
        if (ctx.explosionDamage > 0f) _explDamageCacheByWeaponType[gun.Item.TypeID] = ctx.explosionDamage;

        return (ctx, hasPayload);
    }

    private ProjectileContext FillPayloadFallbacks(int weaponTypeId, ProjectileContext ctx)
    {
        if (ctx.explosionRange <= 0f && _explRangeCacheByWeaponType.TryGetValue(weaponTypeId, out var er))
            ctx.explosionRange = er;
        if (ctx.explosionDamage <= 0f && _explDamageCacheByWeaponType.TryGetValue(weaponTypeId, out var ed))
            ctx.explosionDamage = ed;
        return ctx;
    }

    private void SpawnVisualProjectile(string shooterId, int weaponType, Vector3 spawnPos, Vector3 dir, float speed,
     float distance, bool isFake, ProjectileContext ctx, CharacterMainControl shooterCMC, ItemAgent_Gun overrideGun = null,
     int aiId = 0)
    {
       
        if (!shooterCMC)
        {  
            shooterCMC = TryResolveShooter(shooterId, aiId, out overrideGun);
        }

        if (ctx.team == 0)
        {
            if (shooterCMC)
            {
                ctx.team = shooterCMC.Team;
            }
            else if (aiId != 0 && CoopSyncDatabase.AI.TryGet(aiId, out var aiEntry) && aiEntry != null)
            {
                ctx.team = aiEntry.Team;
            }
            else if (LevelManager.Instance?.MainCharacter)
            {
                ctx.team = LevelManager.Instance.MainCharacter.Team;
            }
           
        }


        if (ctx.firstFrameCheckStartPoint == default)
            ctx.firstFrameCheckStartPoint = spawnPos;

        ctx.direction = dir;
        ctx.speed = speed;
        ctx.distance = distance;
        if (ctx.halfDamageDistance <= 0f)
            ctx.halfDamageDistance = distance * 0.5f;
        ctx.firstFrameCheck = true;

        if (isFake)
        {
            ctx.damage = 0f;
            ctx.buffChance = 0f;
        }

        ItemAgent_Gun gun = overrideGun;
        Transform muzzleTf = null;
        if (!gun && shooterCMC && shooterCMC.characterModel)
        {
            gun = shooterCMC.GetGun();
            var model = shooterCMC.characterModel;
            if (!gun && model.RightHandSocket)
                gun = model.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
            if (!gun && model.LefthandSocket)
                gun = model.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
            if (!gun && model.MeleeWeaponSocket)
                gun = model.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);

            if (gun)
                muzzleTf = gun.muzzle;
        }


        var spawn = muzzleTf ? muzzleTf.position : spawnPos;

        Projectile pfb = null;
        try
        {
            if (gun && gun.GunItemSetting && gun.GunItemSetting.bulletPfb)
            {
                pfb = gun.GunItemSetting.bulletPfb;

            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoopBullet] Exception when accessing gun.GunItemSetting.bulletPfb: {ex}");
        }

        if (!pfb && _projectilePrefabCache.TryGetValue(weaponType, out var cachedPfb) && cachedPfb)
        {
            pfb = cachedPfb;
          
        }

        if (!pfb)
        {
            pfb = GameplayDataSettings.Prefabs.DefaultBullet;
           
        }
     
        if (weaponType != 0 && pfb)
        {
            _projectilePrefabCache[weaponType] = pfb;
        }

        var proj = LevelManager.Instance.BulletPool.GetABullet(pfb);
        proj.transform.position = spawn;
        proj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        proj.Init(ctx);


        if (isFake)
        {
            FakeProjectileRegistry.Register(proj);
        }

        FxManager.PlayMuzzleFxAndShell(shooterId, weaponType, spawn, dir, gun, muzzleTf, aiId);
        CoopTool.TryPlayShootAnim(shooterId);

    }


    public void Client_HandleFireEvent(in WeaponFireEventRpc message)
    {
        if (!networkStarted) return;
        if (NetService.Instance.IsSelfId(message.ShooterId)) return;

        var ctx = message.HasPayload ? message.Payload : new ProjectileContext();
        ctx = FillPayloadFallbacks(message.WeaponTypeId, ctx);
        if (ctx.fromWeaponItemID == 0) ctx.fromWeaponItemID = message.WeaponTypeId;
        ctx.team = (Teams)message.Team;
        if (ctx.firstFrameCheckStartPoint == default)
            ctx.firstFrameCheckStartPoint = message.MuzzlePosition;

        SpawnVisualProjectile(message.ShooterId, message.WeaponTypeId, message.MuzzlePosition, message.Direction.normalized,
            message.Speed, message.Distance, message.IsFake, ctx, null, null, message.AiId);
    }

    public void Server_HandleFireRequest(NetPeer sender, in WeaponFireRequestRpc message)
    {
        if (!IsServer || !networkStarted) return;

        if (!playerStatuses.TryGetValue(sender, out var st) || string.IsNullOrEmpty(message.ShooterId))
            st = playerStatuses.GetValueOrDefault(sender);

        var shooterId = !string.IsNullOrEmpty(message.ShooterId) ? message.ShooterId : st?.EndPoint;
        if (string.IsNullOrEmpty(shooterId)) return;

        var ctx = message.HasPayload ? message.Payload : new ProjectileContext();
        ctx = FillPayloadFallbacks(message.WeaponTypeId, ctx);

        CharacterMainControl controller = null;
        ItemAgent_Gun gun = null;
        if (remoteCharacters.TryGetValue(sender, out var who) && who)
        {
            controller = who.GetComponent<CharacterMainControl>();
            gun = controller ? controller.GetGun() : null;
        }

        if (controller)
            ctx.team = controller.Team;
        else if (message.Team != 0)
            ctx.team = (Teams)message.Team;

        SpawnVisualProjectile(shooterId, message.WeaponTypeId, message.MuzzlePosition, message.Direction.normalized,
            message.Speed, message.Distance, true, ctx, controller, gun, message.AiId);

        EmitGunshotSound(controller, gun, message.MuzzlePosition, ctx.team);

        var evt = message.ToEvent(true, ctx, (int)ctx.team);
        CoopTool.SendRpc(in evt, sender);
    }

    public void Host_OnMainCharacterShoot(ItemAgent_Gun gun)
    {
        if (!networkStarted || !IsServer) return;
        if (gun == null || gun.Holder == null || !gun.Holder.IsMainCharacter) return;

        var proj = Traverse.Create(gun).Field<Projectile>("projInst").Value;
        var dir = proj ? proj.transform.forward : gun.muzzle ? gun.muzzle.forward : gun.transform.forward;
        if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
        dir.Normalize();

        var muzzleWorld = proj ? proj.transform.position : gun.muzzle ? gun.muzzle.position : gun.transform.position;
        var speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
        var distance = gun.BulletDistance + 0.4f;

        var (payload, hasPayload) = BuildPayloadFromGun(gun);
        payload.team = gun.Holder.Team;
        payload.fromCharacter = gun.Holder;
        payload.firstFrameCheckStartPoint = muzzleWorld;

        var evt = new WeaponFireEventRpc
        {
            ShooterId = localPlayerStatus.EndPoint,
            WeaponTypeId = gun.Item.TypeID,
            MuzzlePosition = muzzleWorld,
            Direction = dir,
            Speed = speed,
            Distance = distance,
            IsFake = true,
            PlayFx = true,
            Team = (int)gun.Holder.Team,
            AiId = 0,
            HasPayload = hasPayload,
            Payload = payload
        };

        CoopTool.SendRpc(in evt);
    }

    public void Server_BroadcastProjectileSpawn(ItemAgent_Gun gun, ProjectileContext ctx, Vector3 muzzle, Vector3 dir, int aiId,
        string shooterId)
    {
        if (!IsServer || !networkStarted) return;

        var evt = new WeaponFireEventRpc
        {
            ShooterId = shooterId,
            WeaponTypeId = gun != null && gun.Item != null ? gun.Item.TypeID : 0,
            MuzzlePosition = muzzle,
            Direction = dir.sqrMagnitude < 1e-8f ? Vector3.forward : dir.normalized,
            Speed = ctx.speed,
            Distance = ctx.distance,
            IsFake = true,
            PlayFx = true,
            Team = (int)ctx.team,
            AiId = aiId,
            HasPayload = true,
            Payload = ctx
        };

        CoopTool.SendRpc(in evt);
    }

    public void Server_HandleMeleeSwingRequest(NetPeer sender, in MeleeSwingRequestRpc message)
    {
        if (!IsServer || !networkStarted) return;

        var pid = string.IsNullOrEmpty(message.PlayerId)
            ? playerStatuses.TryGetValue(sender, out var st) && !string.IsNullOrEmpty(st.EndPoint)
                ? st.EndPoint
                : sender.EndPoint.ToString()
            : message.PlayerId;

        var broadcast = new MeleeSwingBroadcastRpc
        {
            PlayerId = pid,
            AiId = 0,
            DealDelay = message.DealDelay,
            SnapshotPosition = message.SnapshotPosition,
            SnapshotDirection = message.SnapshotDirection
        };
      
        Client_HandleMeleeSwing(in broadcast);
        CoopTool.SendRpc(in broadcast, sender);
    }

    public void Client_HandleMeleeSwing(in MeleeSwingBroadcastRpc message)
    {
        if (!networkStarted) return;
        if (NetService.Instance.IsSelfId(message.PlayerId)) return;

        CharacterMainControl cmc = null;
        if (clientRemoteCharacters.TryGetValue(message.PlayerId, out var who))
            cmc = who.GetComponent<CharacterMainControl>();
        else if (message.AiId != 0)
            cmc = COOPManager.AI?.TryGetCharacter(message.AiId);
        //兜底我擦老
        if (!cmc)
        {
            var closestDist = float.MaxValue;

            foreach (var kvp in clientRemoteCharacters)
            {
                var go = kvp.Value;
                if (!go) continue;

                var candidate = go.GetComponent<CharacterMainControl>();
                if (!candidate) continue;

                var dist = (candidate.transform.position - message.SnapshotPosition).sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    cmc = candidate;
                }
            }

            if (!cmc)
            {
                foreach (var candidate in GameObject.FindObjectsByType<CharacterMainControl>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!candidate || candidate.IsMainCharacter()) continue;

                    var dist = (candidate.transform.position - message.SnapshotPosition).sqrMagnitude;
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        cmc = candidate;
                    }
                }
            }
        }

        if (!cmc) return;

        var anim = cmc.characterModel.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (anim != null) anim.OnAttack();

        var anim2 = cmc.characterModel.GetComponent<CharacterAnimationControl>();
        if (anim2) anim2.OnAttack();

        var model = cmc.characterModel;
        if (model) MeleeFx.SpawnSlashFx(model);
    }
}
