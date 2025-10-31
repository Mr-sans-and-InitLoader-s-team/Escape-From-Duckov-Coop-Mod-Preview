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

﻿using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class WeaponRequest
    {
        private NetService Service => NetService.Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        public void BroadcastMeleeSwing(string playerId, float dealDelay)
        {
            if (!networkStarted || !IsServer) return;

            if (writer == null)
            {
                Debug.LogWarning("[WeaponRequest] writer is null");
                return;
            }

            writer.Reset();
            writer.Put((byte)Op.MELEE_ATTACK_SWING);
            writer.Put(playerId);
            writer.Put(dealDelay);

            if (netManager != null)
            {
                netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.BroadcastData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        // 客户端：拦截本地生成后，向主机发开火请求（带上 clientScatter / ads01）
        public void Net_OnClientShoot(ItemAgent_Gun gun, Vector3 muzzle, Vector3 baseDir, Vector3 firstCheckStart)
        {
            if (IsServer || connectedPeer == null) return;

            if (baseDir.sqrMagnitude < 1e-8f)
            {
                var fallback = (gun != null && gun.muzzle != null) ? gun.muzzle.forward : Vector3.forward;
                baseDir = fallback.sqrMagnitude < 1e-8f ? Vector3.forward : fallback.normalized;
            }

            if (gun && gun.muzzle)
            {
                int weaponType = (gun.Item != null) ? gun.Item.TypeID : 0;
               FxManager.Client_PlayLocalShotFx(gun, gun.muzzle, weaponType);
            }

            writer.Reset();
            writer.Put((byte)Op.FIRE_REQUEST);        // opcode
            writer.Put(localPlayerStatus.EndPoint);   // shooterId
            writer.Put(gun.Item.TypeID);              // weaponType
            writer.PutV3cm(muzzle);
            writer.PutDir(baseDir);
            writer.PutV3cm(firstCheckStart);

            // === 新增：把当前这一枪的散布与ADS状态作为提示发给主机 ===
            float clientScatter = 0f;
            float ads01 = 0f;
            try
            {
                clientScatter = Mathf.Max(0f, gun.CurrentScatter); // 客户端这帧真实散布（已包含ADS影响）
                ads01 = (gun.IsInAds ? 1f : 0f);
            }
            catch { }
            writer.Put(clientScatter);
            writer.Put(ads01);

            // 仍旧带原有的“提示载荷”，用于爆炸等参数兜底
            var hint = new ProjectileContext();
            try
            {
                bool hasBulletItem = (gun.BulletItem != null);

                // 伤害
                float charMul = gun.CharacterDamageMultiplier;
                float bulletMul = hasBulletItem ? Mathf.Max(0.0001f, gun.BulletDamageMultiplier) : 1f;
                int shots = Mathf.Max(1, gun.ShotCount);
                hint.damage = gun.Damage * bulletMul * charMul / shots;
                if (gun.Damage > 1f && hint.damage < 1f) hint.damage = 1f;

                // 暴击
                float bulletCritRateGain = hasBulletItem ? gun.bulletCritRateGain : 0f;
                float bulletCritDmgGain = hasBulletItem ? gun.BulletCritDamageFactorGain : 0f;
                hint.critDamageFactor = (gun.CritDamageFactor + bulletCritDmgGain) * (1f + gun.CharacterGunCritDamageGain);
                hint.critRate = gun.CritRate * (1f + gun.CharacterGunCritRateGain + bulletCritRateGain);

                // 元素/破甲/爆炸/流血等（保持你原有写法）
                switch (gun.GunItemSetting.element)
                {
                    case ElementTypes.physics: hint.element_Physics = 1f; break;
                    case ElementTypes.fire: hint.element_Fire = 1f; break;
                    case ElementTypes.poison: hint.element_Poison = 1f; break;
                    case ElementTypes.electricity: hint.element_Electricity = 1f; break;
                    case ElementTypes.space: hint.element_Space = 1f; break;
                }

                hint.armorPiercing = gun.ArmorPiercing + (hasBulletItem ? gun.BulletArmorPiercingGain : 0f);
                hint.armorBreak = gun.ArmorBreak + (hasBulletItem ? gun.BulletArmorBreakGain : 0f);
                hint.explosionRange = gun.BulletExplosionRange;
                hint.explosionDamage = gun.BulletExplosionDamage * gun.ExplosionDamageMultiplier;
                if (hasBulletItem)
                {
                    hint.buffChance = gun.BulletBuffChanceMultiplier * gun.BuffChance;
                    hint.bleedChance = gun.BulletBleedChance;
                }
                hint.penetrate = gun.Penetrate;
                hint.fromWeaponItemID = (gun.Item != null ? gun.Item.TypeID : 0);
            }
            catch { /* 忽略 */ }

            writer.PutProjectilePayload(hint);  // 带着提示载荷发给主机
            
            if (connectedPeer != null)
            {
                connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.SendData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        // 客户端：近战起手用于远端看得见
        public void Net_OnClientMeleeAttack(float dealDelay, Vector3 snapPos, Vector3 snapDir)
        {
            if (!networkStarted || IsServer) return;

            if (writer == null)
            {
                Debug.LogWarning("[WeaponRequest] writer is null in Net_OnClientMeleeAttack");
                return;
            }

            writer.Reset();
            writer.Put((byte)Op.MELEE_ATTACK_REQUEST);
            writer.Put(dealDelay);
            writer.PutV3cm(snapPos);
            writer.PutDir(snapDir);

            if (connectedPeer != null)
            {
                connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.SendData(writer.Data, writer.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }


    }
}
