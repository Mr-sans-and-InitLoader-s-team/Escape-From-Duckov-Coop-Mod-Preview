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

﻿using HarmonyLib;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{

    [HarmonyPatch(typeof(ItemAgent_Gun), "ShootOneBullet")]
    static class Patch_BlockClientAiShoot
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(ItemAgent_Gun __instance, Vector3 _muzzlePoint, Vector3 _shootDirection, Vector3 firstFrameCheckStartPoint)
        {
            var mod = ModBehaviourF.Instance;
            if (mod == null || !mod.networkStarted) return true;

            // 主机照常；客户端才需要拦
            if (mod.IsServer) return true;

            var holder = __instance ? __instance.Holder : null;

            // 非本地主角 &&（AI 有 AICharacterController 或 NetAiTag 任一）=> 拦截
            if (holder && holder != CharacterMainControl.Main)
            {
                bool isAI = holder.GetComponent<AICharacterController>() != null
                         || holder.GetComponent<NetAiTag>() != null;

                if (isAI)
                {
                    if (ModBehaviourF.LogAiHpDebug)
                        Debug.Log("[CLIENT] Block local AI ShootOneBullet holder='" + holder.name + "'");
                    return false; // 不让客户端本地造弹
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ItemAgent_MeleeWeapon), "CheckCollidersInRange")]
    static class Patch_Melee_FlagLocalDeal
    {
        static void Prefix(ItemAgent_MeleeWeapon __instance, bool dealDamage)
        {
            var mod = EscapeFromDuckovCoopMod.ModBehaviourF.Instance;
            bool isClient = (mod != null && mod.networkStarted && !mod.IsServer);
            bool fromLocalMain = (__instance && __instance.Holder == CharacterMainControl.Main);
            EscapeFromDuckovCoopMod.MeleeLocalGuard.LocalMeleeTryingToHurt = (isClient && fromLocalMain && dealDamage);
        }
        static void Postfix()
        {
            EscapeFromDuckovCoopMod.MeleeLocalGuard.LocalMeleeTryingToHurt = false;
        }
    }

    // 客户端：拦截本地主角的开火，改为发 FIRE_REQUEST，不在本地生成弹丸
    [HarmonyPatch(typeof(ItemAgent_Gun), "ShootOneBullet")]
    public static class Patch_ShootOneBullet_Client
    {
        static bool Prefix(ItemAgent_Gun __instance, Vector3 _muzzlePoint, Vector3 _shootDirection, Vector3 firstFrameCheckStartPoint)
        {
            var mod = ModBehaviourF.Instance;
            if (mod == null || !mod.networkStarted) return true;

            bool isClient = !mod.IsServer;
            if (!isClient) return true;

            var holder = __instance.Holder;
            bool isLocalMain = (holder == CharacterMainControl.Main);
            bool isAI = holder && holder.GetComponent<NetAiTag>() != null;

            if (isLocalMain)
            {
                COOPManager.WeaponRequest.Net_OnClientShoot(__instance, _muzzlePoint, _shootDirection, firstFrameCheckStartPoint);
                return false; // 客户端不生成，交主机
            }

            if (isAI) return false;     // 客户端看到的AI，等主机的 FIRE_EVENT
            if (!isLocalMain) return false;
            return true;
        }
    }

    // 服务端：在 Projectile.Init 后，把“服务端算好的弹丸参数”一并广播给所有客户端
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Init), new[] { typeof(ProjectileContext) })]
    static class Patch_ProjectileInit_Broadcast
    {
        static void Postfix(Projectile __instance, ref ProjectileContext _context)
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
                if (string.IsNullOrEmpty(shooterId))
                {
                    var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                    if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                    {
                        shooterId = Steamworks.SteamUser.GetSteamID().ToString();
                    }
                }
            }
            else
            {
                var tag = fromC.GetComponent<NetAiTag>();
                if (tag == null || tag.aiId == 0) return;
                shooterId = "AI:" + tag.aiId;
            }
            
            if (string.IsNullOrEmpty(shooterId)) return;

            int weaponType = 0;
            try { var gun = fromC.GetGun(); if (gun != null && gun.Item != null) weaponType = gun.Item.TypeID; } catch { }

            var w = new LiteNetLib.Utils.NetDataWriter();
            w.Put((byte)Op.FIRE_EVENT);
            w.Put(shooterId);
            w.Put(weaponType);
            w.PutV3cm(__instance.transform.position);
            w.PutDir(_context.direction);
            w.Put(_context.speed);
            w.Put(_context.distance);

            w.PutProjectilePayload(_context);

            if (mod.netManager != null)
            {
                mod.netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.CurrentMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    byte[] data = w.CopyData();
                    hybrid.BroadcastData(data, data.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }
    }






}
