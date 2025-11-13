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
using EscapeFromDuckovCoopMod.Net;  // 引入智能发送扩展方法
using EscapeFromDuckovCoopMod.Utils;
using Random = UnityEngine.Random;

namespace EscapeFromDuckovCoopMod;

public class WeaponHandle
{
    private readonly Dictionary<int, float> _distCacheByWeaponType = new();
    private readonly Dictionary<int, float> _explDamageCacheByWeaponType = new();

    // 爆炸参数缓存（主机记住每种武器的爆炸半径/伤害）
    private readonly Dictionary<int, float> _explRangeCacheByWeaponType = new();

    public readonly HashSet<Projectile> _serverSpawnedFromClient = new();

    private readonly Dictionary<int, float> _speedCacheByWeaponType = new();
    public bool _hasPayloadHint;
    public ProjectileContext _payloadHint;
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


    // 主机：真正生成子弹（用 clientScatter 代替本地的 gun.CurrentScatter 参与随机散布）
    private bool Server_SpawnProjectile(ItemAgent_Gun gun, Vector3 muzzle, Vector3 baseDir, Vector3 firstCheckStart, out Vector3 finalDir, float clientScatter,
        float ads01)
    {
        finalDir = baseDir.sqrMagnitude < 1e-8f ? Vector3.forward : baseDir.normalized;

        // ====== 随机散布仍由主机来做，但幅度优先用客户端提示的散布 ======
        var isMain = gun.Holder && gun.Holder.IsMainCharacter;
        var extra = 0f;
        if (isMain)
            // 和原版一致：仅主角才叠加耐久衰减额外散布
            extra = Mathf.Max(1f, gun.CurrentScatter) * Mathf.Lerp(1.5f, 0f, Mathf.InverseLerp(0f, 0.5f, gun.durabilityPercent));

        // 核心：优先采用客户端当前帧的散布（它已把ADS影响折进 CurrentScatter 里）
        var usedScatter = clientScatter > 0f ? clientScatter : gun.CurrentScatter;

        // 计算偏航
        var yaw = Random.Range(-0.5f, 0.5f) * (usedScatter + extra);
        finalDir = (Quaternion.Euler(0f, yaw, 0f) * finalDir).normalized;

        // ====== 生成 Projectile ======
        var projectile = gun.GunItemSetting && gun.GunItemSetting.bulletPfb
            ? gun.GunItemSetting.bulletPfb
            : GameplayDataSettings.Prefabs.DefaultBullet;

        var projInst = LevelManager.Instance.BulletPool.GetABullet(projectile);
        projInst.transform.position = muzzle;
        if (finalDir.sqrMagnitude < 1e-8f) finalDir = Vector3.forward;
        projInst.transform.rotation = Quaternion.LookRotation(finalDir, Vector3.up);

        // ====== 依赖 Holder/子弹 的数值（保持你原来的兜底写法，不改动） ======
        var characterDamageMultiplier = gun.Holder != null ? gun.CharacterDamageMultiplier : 1f;
        var gunBulletSpeedMul = gun.Holder != null ? gun.Holder.GunBulletSpeedMultiplier : 1f;

        var hasBulletItem = gun.BulletItem != null;
        var bulletDamageMul = hasBulletItem ? gun.BulletDamageMultiplier : 1f;
        var bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
        var bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
        var bulletArmorPiercingGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
        var bulletArmorBreakGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
        var bulletExplosionRange = hasBulletItem ? gun.BulletExplosionRange : 0f;
        var bulletExplosionDamage = hasBulletItem ? gun.BulletExplosionDamage : 0f;
        var bulletBuffChanceMul = hasBulletItem ? gun.BulletBuffChanceMultiplier : 0f;
        var bulletBleedChance = hasBulletItem ? gun.BulletBleedChance : 0f;

        // === 若 BulletItem 缺失，用“客户端提示载荷/本地缓存”兜底爆炸参数（保持原样） ===
        try
        {
            if (bulletExplosionRange <= 0f)
            {
                if (_hasPayloadHint && _payloadHint.fromWeaponItemID == gun.Item.TypeID && _payloadHint.explosionRange > 0f)
                    bulletExplosionRange = _payloadHint.explosionRange;
                else if (_explRangeCacheByWeaponType.TryGetValue(gun.Item.TypeID, out var cachedR))
                    bulletExplosionRange = cachedR;
            }

            if (bulletExplosionDamage <= 0f)
            {
                if (_hasPayloadHint && _payloadHint.fromWeaponItemID == gun.Item.TypeID && _payloadHint.explosionDamage > 0f)
                    bulletExplosionDamage = _payloadHint.explosionDamage;
                else if (_explDamageCacheByWeaponType.TryGetValue(gun.Item.TypeID, out var cachedD))
                    bulletExplosionDamage = cachedD;
            }

            if (bulletExplosionRange > 0f) _explRangeCacheByWeaponType[gun.Item.TypeID] = bulletExplosionRange;
            if (bulletExplosionDamage > 0f) _explDamageCacheByWeaponType[gun.Item.TypeID] = bulletExplosionDamage;
        }
        catch
        {
        }

        var ctx = new ProjectileContext
        {
            firstFrameCheck = true,
            firstFrameCheckStartPoint = firstCheckStart,
            direction = finalDir,
            speed = gun.BulletSpeed * gunBulletSpeedMul,
            distance = gun.BulletDistance + 0.4f,
            halfDamageDistance = (gun.BulletDistance + 0.4f) * 0.5f,
            critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain),
            critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain),
            armorPiercing = gun.ArmorPiercing + bulletArmorPiercingGain,
            armorBreak = gun.ArmorBreak + bulletArmorBreakGain,
            explosionRange = bulletExplosionRange,
            explosionDamage = bulletExplosionDamage * gun.ExplosionDamageMultiplier,
            bleedChance = bulletBleedChance,
            fromWeaponItemID = gun.Item.TypeID
        };

        // 伤害（和你原来的除以 ShotCount 的逻辑一致）
        var perShotDiv = Mathf.Max(1, gun.ShotCount);
        ctx.damage = gun.Damage * bulletDamageMul * characterDamageMultiplier / perShotDiv;
        if (gun.Damage > 1f && ctx.damage < 1f) ctx.damage = 1f;

        // 元素
        switch (gun.GunItemSetting.element)
        {
            case ElementTypes.physics: ctx.element_Physics = 1f; break;
            case ElementTypes.fire: ctx.element_Fire = 1f; break;
            case ElementTypes.poison: ctx.element_Poison = 1f; break;
            case ElementTypes.electricity: ctx.element_Electricity = 1f; break;
            case ElementTypes.space: ctx.element_Space = 1f; break;
        }

        if (bulletBuffChanceMul > 0f) ctx.buffChance = bulletBuffChanceMul * gun.BuffChance;

        // fromCharacter / team 兜底，确保进入伤害系统
        if (gun.Holder)
        {
            ctx.fromCharacter = gun.Holder;
            ctx.team = gun.Holder.Team;
            if (gun.Holder.HasNearByHalfObsticle()) ctx.ignoreHalfObsticle = true;
        }
        else
        {
            var hostChar = LevelManager.Instance?.MainCharacter;
            if (hostChar != null)
            {
                ctx.team = hostChar.Team;
                ctx.fromCharacter = hostChar;
            }
        }

        if (ctx.critRate > 0.99f) ctx.ignoreHalfObsticle = true;

        projInst.Init(ctx);
        _serverSpawnedFromClient.Add(projInst);
        return true;
    }

    public void Host_OnMainCharacterShoot(ItemAgent_Gun gun)
    {
        if (!networkStarted || !IsServer) return;
        if (gun == null || gun.Holder == null || !gun.Holder.IsMainCharacter) return;

        var proj = Traverse.Create(gun).Field<Projectile>("projInst").Value;
        if (proj == null) return;

        var finalDir = proj.transform.forward;
        if (finalDir.sqrMagnitude < 1e-8f) finalDir = gun.muzzle ? gun.muzzle.forward : Vector3.forward;
        finalDir.Normalize();

        var muzzleWorld = proj.transform.position;
        var speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
        var distance = gun.BulletDistance + 0.4f;

        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.FIRE_EVENT);
        w.Put(localPlayerStatus.EndPoint);
        w.Put(gun.Item.TypeID);
        w.PutV3cm(muzzleWorld);
        w.PutDir(finalDir);
        w.Put(speed);
        w.Put(distance);
        w.Put(true); // remote客户端生成假子弹

        var payloadCtx = new ProjectileContext();

        var hasBulletItem = false;
        try
        {
            hasBulletItem = gun.BulletItem != null;
        }
        catch
        {
        }

        float charMul = 1f, bulletMul = 1f;
        var shots = 1;
        try
        {
            charMul = gun.CharacterDamageMultiplier;
            bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
            shots = Mathf.Max(1, gun.ShotCount);
        }
        catch
        {
        }

        try
        {
            payloadCtx.damage = gun.Damage * bulletMul * charMul / shots;
            if (gun.Damage > 1f && payloadCtx.damage < 1f) payloadCtx.damage = 1f;
        }
        catch
        {
            if (payloadCtx.damage <= 0f) payloadCtx.damage = 1f;
        }

        try
        {
            var bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
            var bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
            payloadCtx.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
            payloadCtx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);
        }
        catch
        {
        }

        try
        {
            var apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
            var abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
            payloadCtx.armorPiercing = gun.ArmorPiercing + apGain;
            payloadCtx.armorBreak = gun.ArmorBreak + abGain;
        }
        catch
        {
        }

        try
        {
            var setting = gun.GunItemSetting;
            if (setting != null)
                switch (setting.element)
                {
                    case ElementTypes.physics: payloadCtx.element_Physics = 1f; break;
                    case ElementTypes.fire: payloadCtx.element_Fire = 1f; break;
                    case ElementTypes.poison: payloadCtx.element_Poison = 1f; break;
                    case ElementTypes.electricity: payloadCtx.element_Electricity = 1f; break;
                    case ElementTypes.space: payloadCtx.element_Space = 1f; break;
                }

            payloadCtx.explosionRange = gun.BulletExplosionRange;
            payloadCtx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;

            if (hasBulletItem)
            {
                payloadCtx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                payloadCtx.bleedChance = gun.BulletBleedChance;
            }

            payloadCtx.penetrate = gun.Penetrate;
            payloadCtx.fromWeaponItemID = gun.Item.TypeID;
        }
        catch
        {
        }

        w.PutProjectilePayload(payloadCtx);
        // 使用 SendSmart 自动选择传输方式（FIRE_EVENT → Critical → ReliableOrdered）
        netManager.SendSmart(w, Op.FIRE_EVENT);

        FxManager.PlayMuzzleFxAndShell(localPlayerStatus.EndPoint, gun.Item.TypeID, muzzleWorld, finalDir);
    }

    public void HandleFireEvent(NetDataReader r)
    {
        // —— 主机广播的“射击视觉事件”的基础参数 —— 
        var shooterId = r.GetString();
        var weaponType = r.GetInt();
        var muzzle = r.GetV3cm();
        var dir = r.GetDir();
        var speed = r.GetFloat();
        var distance = r.GetFloat();

        var isFake = true;
        if (r.AvailableBytes > 0)
        {
            try
            {
                isFake = r.GetBool();
            }
            catch
            {
                isFake = true;
            }
        }

        if (NetService.Instance.IsSelfId(shooterId))
            return;

        // 尝试找到“开火者”的枪口（仅用于起点兜底/特效）
        CharacterMainControl shooterCMC = null;
        if (NetService.Instance.IsSelfId(shooterId)) shooterCMC = CharacterMainControl.Main;
        else if (clientRemoteCharacters.TryGetValue(shooterId, out var shooterGo) && shooterGo)
            shooterCMC = shooterGo.GetComponent<CharacterMainControl>();

        ItemAgent_Gun gun = null;
        Transform muzzleTf = null;
        if (shooterCMC && shooterCMC.characterModel)
        {
            gun = shooterCMC.GetGun();
            var model = shooterCMC.characterModel;
            if (!gun && model.RightHandSocket) gun = model.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
            if (!gun && model.LefthandSocket) gun = model.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
            if (!gun && model.MeleeWeaponSocket) gun = model.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);
            if (gun) muzzleTf = gun.muzzle;
        }

        // 生成起点（优先网络给的 muzzle，失败再用枪口/自身）
        var spawnPos = muzzleTf ? muzzleTf.position : muzzle;

        // —— 先用主机载荷初始化 ctx（关键：包含 explosionRange / explosionDamage）——
        var ctx = new ProjectileContext
        {
            direction = dir,
            speed = speed,
            distance = distance,
            halfDamageDistance = distance * 0.5f,
            firstFrameCheck = true,
            firstFrameCheckStartPoint = muzzle,
            team = shooterCMC && shooterCMC ? shooterCMC.Team :
                LevelManager.Instance?.MainCharacter ? LevelManager.Instance.MainCharacter.Team : Teams.player
        };

        var gotPayload = r.AvailableBytes > 0 && NetPackProjectile.TryGetProjectilePayload(r, ref ctx);

        // —— 只有在“旧包/无载荷”的情况下，才用本地枪械做兜底推导 —— 
        if (!gotPayload && gun != null)
        {
            var hasBulletItem = false;
            try
            {
                hasBulletItem = gun.BulletItem != null;
            }
            catch
            {
            }

            // 伤害
            try
            {
                var charMul = Mathf.Max(0.0001f, gun.CharacterDamageMultiplier);
                var bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                var shots = Mathf.Max(1, gun.ShotCount);
                ctx.damage = gun.Damage * bulletMul * charMul / shots;
                if (gun.Damage > 1f && ctx.damage < 1f) ctx.damage = 1f;
            }
            catch
            {
                if (ctx.damage <= 0f) ctx.damage = 1f;
            }

            // 暴击
            try
            {
                ctx.critDamageFactor = (gun.CritDamageFactor + gun.BulletCritDamageFactorGain) * (1f + gun.CharacterGunCritDamageGain);
                ctx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + gun.bulletCritRateGain);
            }
            catch
            {
            }

            // 破甲
            try
            {
                var apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
                var abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
                ctx.armorPiercing = gun.ArmorPiercing + apGain;
                ctx.armorBreak = gun.ArmorBreak + abGain;
            }
            catch
            {
            }

            // 元素
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
            }
            catch
            {
            }

            // 状态 / 爆炸 / 穿透（注意：只有“无载荷”才从本地枪写入爆炸参数）
            try
            {
                if (hasBulletItem)
                {
                    ctx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                    ctx.bleedChance = gun.BulletBleedChance;
                }

                ctx.explosionRange = gun.BulletExplosionRange; // 注意!!!!← RPG 的关键
                ctx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
                ctx.penetrate = gun.Penetrate;

                if (ctx.fromWeaponItemID == 0 && gun.Item != null)
                    ctx.fromWeaponItemID = gun.Item.TypeID;
            }
            catch
            {
                if (ctx.fromWeaponItemID == 0) ctx.fromWeaponItemID = weaponType;
            }

            if (ctx.halfDamageDistance <= 0f) ctx.halfDamageDistance = ctx.distance * 0.5f;

            try
            {
                if (gun.Holder && gun.Holder.HasNearByHalfObsticle()) ctx.ignoreHalfObsticle = true;
                if (ctx.critRate > 0.99f) ctx.ignoreHalfObsticle = true;
            }
            catch
            {
            }
        }

        if (gotPayload && ctx.explosionRange <= 0f && gun != null)
            try
            {
                ctx.explosionRange = gun.BulletExplosionRange;
                ctx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
            }
            catch
            {
            }

        if (isFake)
        {
            ctx.damage = 0f;
            ctx.explosionDamage = 0f;
            ctx.explosionRange = 0f;
            ctx.buffChance = 0f;
            ctx.bleedChance = 0f;
            ctx.penetrate = 0;
        }

        // 生成弹丸（客户端只做可视；爆炸逻辑由 Projectile 基于 ctx.explosionRange>0 触发）
        Projectile pfb = null;
        try
        {
            if (gun && gun.GunItemSetting && gun.GunItemSetting.bulletPfb) pfb = gun.GunItemSetting.bulletPfb;
        }
        catch
        {
        }

        if (!pfb) pfb = GameplayDataSettings.Prefabs.DefaultBullet;
        if (!pfb) return;

        var proj = LevelManager.Instance.BulletPool.GetABullet(pfb);
        proj.transform.position = spawnPos;
        proj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        proj.Init(ctx);

        if (isFake)
            FakeProjectileRegistry.Register(proj);

        FxManager.PlayMuzzleFxAndShell(shooterId, weaponType, spawnPos, dir);
        CoopTool.TryPlayShootAnim(shooterId);
    }

    public void HandleFireRequest(NetPeer peer, NetDataReader r)
    {
        var shooterId = r.GetString();
        var weaponType = r.GetInt();
        var muzzle = r.GetV3cm();
        var baseDir = r.GetDir();
        var firstCheckStart = r.GetV3cm();

        // 兼容旧包：读取并丢弃客户端散布/ADS 信息
        try
        {
            _ = r.GetFloat();
            _ = r.GetFloat();
        }
        catch
        {
            // ignore
        }

        _payloadHint = default;
        _hasPayloadHint = NetPackProjectile.TryGetProjectilePayload(r, ref _payloadHint);

        if (!remoteCharacters.TryGetValue(peer, out var who) || !who)
        {
            _hasPayloadHint = false;
            return;
        }

        var controller = who.GetComponent<CharacterMainControl>();
        var model = controller ? controller.characterModel : null;

        ItemAgent_Gun gun = null;
        if (controller)
            try
            {
                gun = controller.GetGun();
            }
            catch
            {
            }

        if (!gun && model)
            try
            {
                if (!gun && model.RightHandSocket)
                    gun = model.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                if (!gun && model.LefthandSocket)
                    gun = model.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                if (!gun && model.MeleeWeaponSocket)
                    gun = model.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);
            }
            catch
            {
            }

        if (muzzle == default || muzzle.sqrMagnitude < 1e-8f)
        {
            Transform mz = null;
            if (model)
            {
                if (!mz && model.RightHandSocket) mz = model.RightHandSocket.Find("Muzzle");
                if (!mz && model.LefthandSocket) mz = model.LefthandSocket.Find("Muzzle");
                if (!mz && model.MeleeWeaponSocket) mz = model.MeleeWeaponSocket.Find("Muzzle");
            }

            if (!mz) mz = who.transform.Find("Muzzle");
            if (mz) muzzle = mz.position;
        }

        var finalDir = baseDir.sqrMagnitude > 1e-8f ? baseDir.normalized : Vector3.forward;

        float speed;
        float distance;

        if (gun)
        {
            try
            {
                speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
            }
            catch
            {
                speed = 60f;
            }

            try
            {
                distance = gun.BulletDistance + 0.4f;
            }
            catch
            {
                distance = 50f;
            }

            _speedCacheByWeaponType[weaponType] = speed;
            _distCacheByWeaponType[weaponType] = distance;
        }
        else
        {
            speed = _speedCacheByWeaponType.TryGetValue(weaponType, out var sp) ? sp : 60f;
            distance = _distCacheByWeaponType.TryGetValue(weaponType, out var dist) ? dist : 50f;
        }

        var ctx = _hasPayloadHint ? _payloadHint : new ProjectileContext();
        ctx.direction = finalDir;
        ctx.speed = speed;
        ctx.distance = distance;
        ctx.halfDamageDistance = distance * 0.5f;
        ctx.firstFrameCheck = true;
        ctx.firstFrameCheckStartPoint = firstCheckStart;
        ctx.damage = 0f;
        ctx.buffChance = 0f;
        ctx.bleedChance = 0f;
        ctx.explosionDamage = 0f;
        ctx.explosionRange = 0f;
        ctx.penetrate = 0;

        if (gun && gun.Holder)
        {
            ctx.team = gun.Holder.Team;
            ctx.fromCharacter = gun.Holder;
            try
            {
                if (gun.Holder.HasNearByHalfObsticle()) ctx.ignoreHalfObsticle = true;
            }
            catch
            {
            }
        }
        else
        {
            var hostChar = LevelManager.Instance?.MainCharacter;
            if (hostChar)
                ctx.team = hostChar.Team;
            else
                ctx.team = Teams.player;
            ctx.fromCharacter = null;
            ctx.ignoreHalfObsticle = false;
        }

        Projectile pfb = null;
        try
        {
            if (gun && gun.GunItemSetting && gun.GunItemSetting.bulletPfb)
                pfb = gun.GunItemSetting.bulletPfb;
        }
        catch
        {
        }

        if (!pfb) pfb = GameplayDataSettings.Prefabs.DefaultBullet;

        if (pfb)
        {
            var proj = LevelManager.Instance.BulletPool.GetABullet(pfb);
            proj.transform.position = muzzle;
            proj.transform.rotation = Quaternion.LookRotation(finalDir, Vector3.up);
            proj.Init(ctx);
            FakeProjectileRegistry.Register(proj);
        }

        FxManager.PlayMuzzleFxAndShell(shooterId, weaponType, muzzle, finalDir);
        COOPManager.HostPlayer_Apply.PlayShootAnimOnServerPeer(peer);

        writer.Reset();
        writer.Put((byte)Op.FIRE_EVENT);
        writer.Put(shooterId);
        writer.Put(weaponType);
        writer.PutV3cm(muzzle);
        writer.PutDir(finalDir);
        writer.Put(speed);
        writer.Put(distance);
        writer.Put(true); // remote 客户端收到后生成假弹

        if (_hasPayloadHint)
            writer.PutProjectilePayload(_payloadHint);
        else
            writer.Put(false);

        // 使用 SendSmart 自动选择传输方式（FIRE_EVENT → Critical → ReliableOrdered）
        netManager.SendSmart(writer, Op.FIRE_EVENT);

        _hasPayloadHint = false;
    }

    // 主机：收到客户端“近战起手”，播动作 + 强制挥空 FX（避免动画事件缺失）
    public void HandleMeleeAttackRequest(NetPeer sender, NetDataReader reader)
    {
        var delay = reader.GetFloat();
        var pos = reader.GetV3cm();
        var dir = reader.GetDir();

        if (remoteCharacters.TryGetValue(sender, out var who) && who)
        {
            var anim = who.GetComponent<CharacterMainControl>().characterModel.GetComponent<CharacterAnimationControl_MagicBlend>();
            if (anim != null) anim.OnAttack();

            var model = who.GetComponent<CharacterMainControl>().characterModel;
            if (model) MeleeFx.SpawnSlashFx(model);
        }

        var pid = playerStatuses.TryGetValue(sender, out var st) && !string.IsNullOrEmpty(st.EndPoint)
            ? st.EndPoint
            : sender.EndPoint.ToString();
        foreach (var p in netManager.ConnectedPeerList)
        {
            if (p == sender) continue;
            var w = new NetDataWriter();
            w.Put((byte)Op.MELEE_ATTACK_SWING);
            w.Put(pid);
            w.Put(delay);
            // 使用 SendSmart 自动选择传输方式（MELEE_ATTACK_SWING → Critical → ReliableOrdered）
            p.SendSmart(w, Op.MELEE_ATTACK_SWING);
        }
    }

    public void HandleMeleeHitReport(NetPeer sender, NetDataReader reader)
    {
        Debug.Log($"[SERVER] HandleMeleeHitReport begin, from={sender?.EndPoint}, bytes={reader.AvailableBytes}");

        var attackerId = reader.GetString();

        var dmg = reader.GetFloat();
        var ap = reader.GetFloat();
        var cdf = reader.GetFloat();
        var cr = reader.GetFloat();
        var crit = reader.GetInt();

        var hitPoint = reader.GetV3cm();
        var normal = reader.GetDir();

        var wid = reader.GetInt();
        var bleed = reader.GetFloat();
        var boom = reader.GetBool();
        var range = reader.GetFloat();

        if (!remoteCharacters.TryGetValue(sender, out var attackerGo) || !attackerGo)
        {
            Debug.LogWarning("[SERVER] melee: attackerGo missing for sender");
            return;
        }

        // 拿攻击者控制器（尽量是远端玩家本体）
        CharacterMainControl attackerCtrl = null;
        var attackerModel = attackerGo.GetComponent<CharacterModel>() ?? attackerGo.GetComponentInChildren<CharacterModel>(true);
        if (attackerModel && attackerModel.characterMainControl) attackerCtrl = attackerModel.characterMainControl;
        if (!attackerCtrl) attackerCtrl = attackerGo.GetComponent<CharacterMainControl>() ?? attackerGo.GetComponentInChildren<CharacterMainControl>(true);
        if (!attackerCtrl)
        {
            Debug.LogWarning("[SERVER] melee: attackerCtrl null (实例结构异常)");
            return;
        }

        // —— 搜附近候选（包含 Trigger）——
        int mask = GameplayDataSettings.Layers.damageReceiverLayerMask;
        var radius = Mathf.Clamp(range * 0.6f, 0.4f, 1.2f);

        var buf = new Collider[12];
        var n = 0;
        try
        {
            n = Physics.OverlapSphereNonAlloc(hitPoint, radius, buf, mask, QueryTriggerInteraction.UseGlobal);
        }
        catch
        {
            var tmp = Physics.OverlapSphere(hitPoint, radius, mask, QueryTriggerInteraction.UseGlobal);
            n = Mathf.Min(tmp.Length, buf.Length);
            Array.Copy(tmp, buf, n);
        }

        DamageReceiver best = null;
        var bestD2 = float.MaxValue;

        for (var i = 0; i < n; i++)
        {
            var col = buf[i];
            if (!col) continue;
            var dr = col.GetComponent<DamageReceiver>();
            if (!dr) continue;

            if (CoopTool.IsSelfDR(dr, attackerCtrl)) continue; // 排自己
            if (CoopTool.IsCharacterDR(dr) && !Team.IsEnemy(dr.Team, attackerCtrl.Team)) continue; // 角色才做敌我判定

            var d2 = (dr.transform.position - hitPoint).sqrMagnitude;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = dr;
            }
        }

        // 兜底：沿攻击方向短球扫
        if (!best)
        {
            var dir = attackerCtrl.transform.forward;
            var start = hitPoint - dir * 0.5f;
            if (Physics.SphereCast(start, 0.3f, dir, out var hit, 1.5f, mask, QueryTriggerInteraction.UseGlobal))
            {
                var dr = hit.collider ? hit.collider.GetComponent<DamageReceiver>() : null;
                if (dr != null && !CoopTool.IsSelfDR(dr, attackerCtrl))
                    if (!CoopTool.IsCharacterDR(dr) || Team.IsEnemy(dr.Team, attackerCtrl.Team))
                        best = dr;
            }
        }

        if (!best)
        {
            Debug.Log($"[SERVER] melee hit miss @ {hitPoint} r={radius}");
            return;
        }

        // 目标类型区分（角色/环境）
        var victimIsChar = CoopTool.IsCharacterDR(best);

        // ★ 关键：环境/建筑用“空攻击者”避免二次缩放；角色保留攻击者
        var attackerForDI = victimIsChar || !ServerTuning.UseNullAttackerForEnv ? attackerCtrl : null;

        var di = new DamageInfo(attackerForDI)
        {
            damageValue = dmg,
            armorPiercing = ap,
            critDamageFactor = cdf,
            critRate = cr,
            crit = crit,
            damagePoint = hitPoint,
            damageNormal = normal,
            fromWeaponItemID = wid,
            bleedChance = bleed,
            isExplosion = boom
        };

        var scale = victimIsChar ? ServerTuning.RemoteMeleeCharScale : ServerTuning.RemoteMeleeEnvScale;
        if (Mathf.Abs(scale - 1f) > 1e-3f) di.damageValue = Mathf.Max(0f, di.damageValue * scale);

        Debug.Log($"[SERVER] melee hit -> target={best.name} raw={dmg} scaled={di.damageValue} env={!victimIsChar}");
        var victimCtrl = best.GetComponentInParent<CharacterMainControl>(true);
        var victimHealth = victimCtrl ? victimCtrl.Health : null;
        var victimWasDead = false;
        var victimIsAi = false;

        if (victimCtrl)
        {
            victimIsAi = ComponentCache.IsAI(victimCtrl);

            if (victimHealth)
                try
                {
                    victimWasDead = victimHealth.IsDead;
                }
                catch
                {
                }
        }

        best.Hurt(di);

        if (victimIsAi && victimCtrl && victimHealth)
        {
            var nowDead = false;
            try
            {
                nowDead = victimHealth.IsDead || victimHealth.CurrentHealth <= 0f;
            }
            catch
            {
            }

            if (!victimWasDead && nowDead)
            {
                var aiId = 0;
                var tag = ComponentCache.GetNetAiTag(victimCtrl);
                if (tag != null) aiId = tag.aiId;

                if (aiId == 0)
                {
                    foreach (var kv in AITool.aiById)
                        if (kv.Value == victimCtrl)
                        {
                            aiId = kv.Key;
                            break;
                        }
                }

                COOPManager.AIHealth.Server_HandleAuthoritativeAiDeath(victimCtrl, victimHealth, aiId, di, true);
            }
        }
    }
}
