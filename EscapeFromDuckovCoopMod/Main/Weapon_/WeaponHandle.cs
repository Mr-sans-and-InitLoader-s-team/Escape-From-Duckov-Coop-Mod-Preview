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

﻿using Duckov.Utilities;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class WeaponHandle
    {
        private NetService Service => NetService.Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        private Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
        public bool _hasPayloadHint;
        public ProjectileContext _payloadHint;

        // 爆炸参数缓存（主机记住每种武器的爆炸半径/伤害）
        private readonly Dictionary<int, float> _explRangeCacheByWeaponType = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _explDamageCacheByWeaponType = new Dictionary<int, float>();

        public readonly HashSet<Projectile> _serverSpawnedFromClient = new HashSet<Projectile>();

        private readonly Dictionary<int, float> _speedCacheByWeaponType = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _distCacheByWeaponType = new Dictionary<int, float>();


        // 主机：真正生成子弹（用 clientScatter 代替本地的 gun.CurrentScatter 参与随机散布）
        bool Server_SpawnProjectile(ItemAgent_Gun gun, Vector3 muzzle, Vector3 baseDir, Vector3 firstCheckStart, out Vector3 finalDir, float clientScatter, float ads01)
        {
            finalDir = baseDir.sqrMagnitude < 1e-8f ? Vector3.forward : baseDir.normalized;

            // ====== 随机散布仍由主机来做，但幅度优先用客户端提示的散布 ======
            bool isMain = (gun.Holder && gun.Holder.IsMainCharacter);
            float extra = 0f;
            if (isMain)
            {
                // 和原版一致：仅主角才叠加耐久衰减额外散布
                extra = Mathf.Max(1f, gun.CurrentScatter) * Mathf.Lerp(1.5f, 0f, Mathf.InverseLerp(0f, 0.5f, gun.durabilityPercent));
            }

            // 核心：优先采用客户端当前帧的散布（它已把ADS影响折进 CurrentScatter 里）
            float usedScatter = (clientScatter > 0f ? clientScatter : gun.CurrentScatter);

            // 计算偏航
            float yaw = UnityEngine.Random.Range(-0.5f, 0.5f) * (usedScatter + extra);
            finalDir = (Quaternion.Euler(0f, yaw, 0f) * finalDir).normalized;

            // ====== 生成 Projectile ======
            var projectile = (gun.GunItemSetting && gun.GunItemSetting.bulletPfb)
                ? gun.GunItemSetting.bulletPfb
                : Duckov.Utilities.GameplayDataSettings.Prefabs.DefaultBullet;

            var projInst = LevelManager.Instance.BulletPool.GetABullet(projectile);
            projInst.transform.position = muzzle;
            if (finalDir.sqrMagnitude < 1e-8f) finalDir = Vector3.forward;
            projInst.transform.rotation = Quaternion.LookRotation(finalDir, Vector3.up);

            // ====== 依赖 Holder/子弹 的数值（保持你原来的兜底写法，不改动） ======
            float characterDamageMultiplier = (gun.Holder != null) ? gun.CharacterDamageMultiplier : 1f;
            float gunBulletSpeedMul = (gun.Holder != null) ? gun.Holder.GunBulletSpeedMultiplier : 1f;

            bool hasBulletItem = (gun.BulletItem != null);
            float bulletDamageMul = hasBulletItem ? gun.BulletDamageMultiplier : 1f;
            float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
            float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
            float bulletArmorPiercingGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
            float bulletArmorBreakGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
            float bulletExplosionRange = hasBulletItem ? gun.BulletExplosionRange : 0f;
            float bulletExplosionDamage = hasBulletItem ? gun.BulletExplosionDamage : 0f;
            float bulletBuffChanceMul = hasBulletItem ? gun.BulletBuffChanceMultiplier : 0f;
            float bulletBleedChance = hasBulletItem ? gun.BulletBleedChance : 0f;

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
            catch { }

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
            int perShotDiv = Mathf.Max(1, gun.ShotCount);
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

            if (bulletBuffChanceMul > 0f)
            {
                ctx.buffChance = bulletBuffChanceMul * gun.BuffChance;
            }

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

            Vector3 finalDir = proj.transform.forward;
            if (finalDir.sqrMagnitude < 1e-8f) finalDir = (gun.muzzle ? gun.muzzle.forward : Vector3.forward);
            finalDir.Normalize();

            Vector3 muzzleWorld = proj.transform.position;
            float speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
            float distance = gun.BulletDistance + 0.4f;

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

            var payloadCtx = new ProjectileContext();

            bool hasBulletItem = false;
            try { hasBulletItem = (gun.BulletItem != null); } catch { }

            float charMul = 1f, bulletMul = 1f;
            int shots = 1;
            try
            {
                charMul = gun.CharacterDamageMultiplier;
                bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                shots = Mathf.Max(1, gun.ShotCount);
            }
            catch { }

            try
            {
                payloadCtx.damage = gun.Damage * bulletMul * charMul / shots;
                if (gun.Damage > 1f && payloadCtx.damage < 1f) payloadCtx.damage = 1f;
            }
            catch { if (payloadCtx.damage <= 0f) payloadCtx.damage = 1f; }

            try
            {
                float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
                float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
                payloadCtx.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
                payloadCtx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);
            }
            catch { }

            try
            {
                float apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
                float abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
                payloadCtx.armorPiercing = gun.ArmorPiercing + apGain;
                payloadCtx.armorBreak = gun.ArmorBreak + abGain;
            }
            catch { }

            try
            {
                var setting = gun.GunItemSetting;
                if (setting != null)
                {
                    switch (setting.element)
                    {
                        case ElementTypes.physics: payloadCtx.element_Physics = 1f; break;
                        case ElementTypes.fire: payloadCtx.element_Fire = 1f; break;
                        case ElementTypes.poison: payloadCtx.element_Poison = 1f; break;
                        case ElementTypes.electricity: payloadCtx.element_Electricity = 1f; break;
                        case ElementTypes.space: payloadCtx.element_Space = 1f; break;
                    }
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
            catch { }

            w.PutProjectilePayload(payloadCtx);
            
            if (netManager != null)
            {
                netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.BroadcastData(w.Data, w.Length, DeliveryMethod.ReliableOrdered);
                }
            }

           FxManager.PlayMuzzleFxAndShell(localPlayerStatus.EndPoint, gun.Item.TypeID, muzzleWorld, finalDir);
        }

        public void HandleFireEvent(NetPacketReader r)
        {
            // —— 主机广播的"射击视觉事件"的基础参数 —— 
            string shooterId = r.GetString();
            int weaponType = r.GetInt();
            Vector3 muzzle = r.GetV3cm();
            Vector3 dir = r.GetDir();
            float speed = r.GetFloat();
            float distance = r.GetFloat();

            // 尝试找到"开火者"的枪口（仅用于起点兜底/特效）
            CharacterMainControl shooterCMC = null;
            if (NetService.Instance != null && NetService.Instance.IsSelfId(shooterId)) 
                shooterCMC = CharacterMainControl.Main;
            else if (clientRemoteCharacters != null && clientRemoteCharacters.TryGetValue(shooterId, out var shooterGo) && shooterGo)
                shooterCMC = shooterGo.GetComponent<CharacterMainControl>();

            ItemAgent_Gun gun = null; Transform muzzleTf = null;
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
            Vector3 spawnPos = muzzleTf ? muzzleTf.position : muzzle;

            // —— 先用主机载荷初始化 ctx（关键：包含 explosionRange / explosionDamage）——
            var ctx = new ProjectileContext
            {
                direction = dir,
                speed = speed,
                distance = distance,
                halfDamageDistance = distance * 0.5f,
                firstFrameCheck = true,
                firstFrameCheckStartPoint = muzzle,
                team = (shooterCMC && shooterCMC) ? shooterCMC.Team :
                       (LevelManager.Instance?.MainCharacter ? LevelManager.Instance.MainCharacter.Team : Teams.player)
            };

            bool gotPayload = (r.AvailableBytes > 0) && NetPack_Projectile.TryGetProjectilePayload(r, ref ctx);

            // —— 只有在“旧包/无载荷”的情况下，才用本地枪械做兜底推导 —— 
            if (!gotPayload && gun != null)
            {
                bool hasBulletItem = false;
                try { hasBulletItem = (gun.BulletItem != null); } catch { }

                // 伤害
                try
                {
                    float charMul = Mathf.Max(0.0001f, gun.CharacterDamageMultiplier);
                    float bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                    int shots = Mathf.Max(1, gun.ShotCount);
                    ctx.damage = gun.Damage * bulletMul * charMul / shots;
                    if (gun.Damage > 1f && ctx.damage < 1f) ctx.damage = 1f;
                }
                catch { if (ctx.damage <= 0f) ctx.damage = 1f; }

                // 暴击
                try
                {
                    ctx.critDamageFactor = (gun.CritDamageFactor + gun.BulletCritDamageFactorGain) * (1f + gun.CharacterGunCritDamageGain);
                    ctx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + gun.bulletCritRateGain);
                }
                catch { }

                // 破甲
                try
                {
                    float apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
                    float abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
                    ctx.armorPiercing = gun.ArmorPiercing + apGain;
                    ctx.armorBreak = gun.ArmorBreak + abGain;
                }
                catch { }

                // 元素
                try
                {
                    var setting = gun.GunItemSetting;
                    if (setting != null)
                    {
                        switch (setting.element)
                        {
                            case ElementTypes.physics: ctx.element_Physics = 1f; break;
                            case ElementTypes.fire: ctx.element_Fire = 1f; break;
                            case ElementTypes.poison: ctx.element_Poison = 1f; break;
                            case ElementTypes.electricity: ctx.element_Electricity = 1f; break;
                            case ElementTypes.space: ctx.element_Space = 1f; break;
                        }
                    }
                }
                catch { }

                // 状态 / 爆炸 / 穿透（注意：只有“无载荷”才从本地枪写入爆炸参数）
                try
                {
                    if (hasBulletItem)
                    {
                        ctx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                        ctx.bleedChance = gun.BulletBleedChance;
                    }
                    ctx.explosionRange = gun.BulletExplosionRange;                                // 注意!!!!← RPG 的关键
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
                catch { }
            }

            if (gotPayload && ctx.explosionRange <= 0f && gun != null)
            {
                try
                {
                    ctx.explosionRange = gun.BulletExplosionRange;
                    ctx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
                }
                catch { }
            }

            // 生成弹丸（客户端只做可视；爆炸逻辑由 Projectile 基于 ctx.explosionRange>0 触发）
            Projectile pfb = null;
            try { if (gun && gun.GunItemSetting && gun.GunItemSetting.bulletPfb) pfb = gun.GunItemSetting.bulletPfb; } catch { }
            if (!pfb) pfb = Duckov.Utilities.GameplayDataSettings.Prefabs.DefaultBullet;
            if (!pfb) return;

            var proj = LevelManager.Instance.BulletPool.GetABullet(pfb);
            proj.transform.position = spawnPos;
            proj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            proj.Init(ctx);

            FxManager.PlayMuzzleFxAndShell(shooterId, weaponType, spawnPos, dir);
            CoopTool.TryPlayShootAnim(shooterId);
        }

        public void HandleFireRequest(string endPoint, NetPacketReader r)
        {
            string shooterId = r.GetString();
            int weaponType = r.GetInt();
            Vector3 muzzle = r.GetV3cm();
            Vector3 baseDir = r.GetDir();
            Vector3 firstCheckStart = r.GetV3cm();

            // === 新增：读取客户端这帧的散布 & ADS 提示 ===
            float clientScatter = 0f;
            float ads01 = 0f;
            try
            {
                clientScatter = r.GetFloat();
                ads01 = r.GetFloat();
            }
            catch
            {
                clientScatter = 0f; ads01 = 0f; // 兼容老包
            }

            // 读取客户端随包提示载荷（可能不存在，Try 不会抛异常）
            _payloadHint = default;
            _hasPayloadHint = NetPack_Projectile.TryGetProjectilePayload(r, ref _payloadHint);

            if (!remoteCharacters.TryGetValue(endPoint, out var who) || !who) { _hasPayloadHint = false; return; }

            var cm = who.GetComponent<CharacterMainControl>().characterModel;

            // —— 贪婪地查找远端玩家的枪 —— 
            ItemAgent_Gun gun = null;
            if (cm)
            {
                try
                {
                    gun = who.GetComponent<CharacterMainControl>()?.GetGun();
                    if (!gun && cm.RightHandSocket) gun = cm.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                    if (!gun && cm.LefthandSocket) gun = cm.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                    if (!gun && cm.MeleeWeaponSocket) gun = cm.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                }
                catch { }
            }

            // 找不到 muzzle 就从骨骼里兜底
            if (muzzle == default || muzzle.sqrMagnitude < 1e-8f)
            {
                Transform mz = null;
                if (cm)
                {
                    if (!mz && cm.RightHandSocket) mz = cm.RightHandSocket.Find("Muzzle");
                    if (!mz && cm.LefthandSocket) mz = cm.LefthandSocket.Find("Muzzle");
                    if (!mz && cm.MeleeWeaponSocket) mz = cm.MeleeWeaponSocket.Find("Muzzle");
                }
                if (!mz) mz = who.transform.Find("Muzzle");
                if (mz) muzzle = mz.position;
            }

            // —— 有 gun 走权威生成；无 gun 走可视兜底 —— 
            Vector3 finalDir;
            float speed, distance;

            if (gun) // 正常路径：主机生成真正的弹丸
            {
                if (!Server_SpawnProjectile(gun, muzzle, baseDir, firstCheckStart, out finalDir, clientScatter, ads01))
                { _hasPayloadHint = false; return; }

                speed = gun.BulletSpeed * (gun.Holder ? gun.Holder.GunBulletSpeedMultiplier : 1f);
                distance = gun.BulletDistance + 0.4f;
            }
            else
            {
                // 没 gun 的可视兜底
                finalDir = (baseDir.sqrMagnitude > 1e-8f ? baseDir.normalized : Vector3.forward);
                speed = _speedCacheByWeaponType.TryGetValue(weaponType, out var sp) ? sp : 60f;
                distance = _distCacheByWeaponType.TryGetValue(weaponType, out var dist) ? dist : 50f;
                // 可选：也可以在服务器生成一个“无 holder”的 Projectile（略）
            }

            // —— 广播 FIRE_EVENT（带主机权威 ctx）——
            writer.Reset();
            writer.Put((byte)Op.FIRE_EVENT);
            writer.Put(shooterId);
            writer.Put(weaponType);
            writer.PutV3cm(muzzle);
            writer.PutDir(finalDir);
            writer.Put(speed);
            writer.Put(distance);

            var payloadCtx = new ProjectileContext();
            if (gun != null)
            {
                bool hasBulletItem = false;
                try { hasBulletItem = (gun.BulletItem != null); } catch { }

                // …（保留你原有的 payload 构造，略）…
                try
                {
                    float charMul = gun.CharacterDamageMultiplier;
                    float bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                    int shots = Mathf.Max(1, gun.ShotCount);
                    payloadCtx.damage = gun.Damage * bulletMul * charMul / shots;
                    if (gun.Damage > 1f && payloadCtx.damage < 1f) payloadCtx.damage = 1f;
                }
                catch { if (payloadCtx.damage <= 0f) payloadCtx.damage = 1f; }

                try
                {
                    float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
                    float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
                    payloadCtx.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
                    payloadCtx.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);
                }
                catch { }

                try
                {
                    float apGain = hasBulletItem ? gun.BulletArmorPiercingGain : 0f;
                    float abGain = hasBulletItem ? gun.BulletArmorBreakGain : 0f;
                    payloadCtx.armorPiercing = gun.ArmorPiercing + apGain;
                    payloadCtx.armorBreak = gun.ArmorBreak + abGain;
                }
                catch { }

                try
                {
                    var setting = gun.GunItemSetting;
                    if (setting != null)
                    {
                        switch (setting.element)
                        {
                            case ElementTypes.physics: payloadCtx.element_Physics = 1f; break;
                            case ElementTypes.fire: payloadCtx.element_Fire = 1f; break;
                            case ElementTypes.poison: payloadCtx.element_Poison = 1f; break;
                            case ElementTypes.electricity: payloadCtx.element_Electricity = 1f; break;
                            case ElementTypes.space: payloadCtx.element_Space = 1f; break;
                        }
                    }

                    payloadCtx.explosionRange = gun.BulletExplosionRange;
                    payloadCtx.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;

                    if (hasBulletItem)
                    {
                        payloadCtx.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                        payloadCtx.bleedChance = gun.BulletBleedChance;
                    }

                    payloadCtx.penetrate = gun.Penetrate;
                    payloadCtx.fromWeaponItemID = (gun.Item != null ? gun.Item.TypeID : 0);
                }
                catch { }
            }

            writer.PutProjectilePayload(payloadCtx);
            netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);

            FxManager.PlayMuzzleFxAndShell(shooterId, weaponType, muzzle, finalDir);
            COOPManager.HostPlayer_Apply.PlayShootAnimOnServerPeer(endPoint);

            // 清理本次 hint 状态
            _hasPayloadHint = false;
        }

        // 主机：收到客户端"近战起手"，播动作 + 强制挥空 FX（避免动画事件缺失）
        public void HandleMeleeAttackRequest(string endPoint, NetPacketReader reader)
        {

            float delay = reader.GetFloat();
            Vector3 pos = reader.GetV3cm();
            Vector3 dir = reader.GetDir();

            if (remoteCharacters != null && remoteCharacters.TryGetValue(endPoint, out var who) && who)
            {
                var cmc = who.GetComponent<CharacterMainControl>();
                if (cmc != null && cmc.characterModel != null)
                {
                    var anim = cmc.characterModel.GetComponent<CharacterAnimationControl_MagicBlend>();
                    if (anim != null) anim.OnAttack();

                    EscapeFromDuckovCoopMod.MeleeFx.SpawnSlashFx(cmc.characterModel);
                }
            }

            string pid = (playerStatuses != null && playerStatuses.TryGetValue(endPoint, out var st) && !string.IsNullOrEmpty(st.EndPoint))
                          ? st.EndPoint : endPoint;
            
            var w = new NetDataWriter();
            w.Put((byte)Op.MELEE_ATTACK_SWING);
            w.Put(pid);
            w.Put(delay);

            if (netManager != null)
            {
                foreach (var p in netManager.ConnectedPeerList)
                {
                    if (p.EndPoint.ToString() == endPoint) continue;
                    p.Send(w, DeliveryMethod.ReliableOrdered);
                }
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                    if (steamNet != null && steamNet.peerConnections != null)
                    {
                        foreach (var peer in steamNet.peerConnections.Keys)
                        {
                            if (peer.ToString() == endPoint) continue;
                            steamNet.SendPacket(peer, w.Data, w.Length);
                        }
                    }
                }
            }
        }

        public void HandleMeleeHitReport(string endPoint, NetPacketReader reader)
        {
            Debug.Log("[SERVER] HandleMeleeHitReport begin, from=" + endPoint + ", bytes=" + reader.AvailableBytes);

            string attackerId = reader.GetString();

            float dmg = reader.GetFloat();
            float ap = reader.GetFloat();
            float cdf = reader.GetFloat();
            float cr = reader.GetFloat();
            int crit = reader.GetInt();

            Vector3 hitPoint = reader.GetV3cm();
            Vector3 normal = reader.GetDir();

            int wid = reader.GetInt();
            float bleed = reader.GetFloat();
            bool boom = reader.GetBool();
            float range = reader.GetFloat();

            if (!remoteCharacters.TryGetValue(endPoint, out var attackerGo) || !attackerGo)
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
            float radius = Mathf.Clamp(range * 0.6f, 0.4f, 1.2f);

            Collider[] buf = new Collider[12];
            int n = 0;
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
            float bestD2 = float.MaxValue;

            for (int i = 0; i < n; i++)
            {
                var col = buf[i]; if (!col) continue;
                var dr = col.GetComponent<DamageReceiver>(); if (!dr) continue;

                if (CoopTool.IsSelfDR(dr, attackerCtrl)) continue;                // 排自己
                if (CoopTool.IsCharacterDR(dr) && !Team.IsEnemy(dr.Team, attackerCtrl.Team)) continue; // 角色才做敌我判定

                float d2 = (dr.transform.position - hitPoint).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = dr; }
            }

            // 兜底：沿攻击方向短球扫
            if (!best)
            {
                Vector3 dir = attackerCtrl.transform.forward;
                Vector3 start = hitPoint - dir * 0.5f;
                if (Physics.SphereCast(start, 0.3f, dir, out var hit, 1.5f, mask, QueryTriggerInteraction.UseGlobal))
                {
                    var dr = hit.collider ? hit.collider.GetComponent<DamageReceiver>() : null;
                    if (dr != null && !CoopTool.IsSelfDR(dr, attackerCtrl))
                    {
                        if (!CoopTool.IsCharacterDR(dr) || Team.IsEnemy(dr.Team, attackerCtrl.Team))
                            best = dr;
                    }
                }
            }

            if (!best)
            {
                Debug.Log("[SERVER] melee hit miss @ " + hitPoint + " r=" + radius);
                return;
            }

            // 目标类型区分（角色/环境）
            bool victimIsChar = CoopTool.IsCharacterDR(best);

            // ★ 关键：环境/建筑用“空攻击者”避免二次缩放；角色保留攻击者
            var attackerForDI = (victimIsChar || !ServerTuning.UseNullAttackerForEnv) ? attackerCtrl : null;

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

            float scale = victimIsChar ? ServerTuning.RemoteMeleeCharScale : ServerTuning.RemoteMeleeEnvScale;
            if (Mathf.Abs(scale - 1f) > 1e-3f) di.damageValue = Mathf.Max(0f, di.damageValue * scale);

            Debug.Log("[SERVER] melee hit -> target=" + best.name + " raw=" + dmg + " scaled=" + di.damageValue + " env=" + !victimIsChar);
            best.Hurt(di);
        }

      








    }
}
