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

﻿using Duckov.Buffs;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod
{
    [HarmonyPatch(typeof(Buff), "Setup")]
    static class Patch_Buff_Setup_Safe
    {
        // 反射缓存
        static readonly FieldInfo FI_master = AccessTools.Field(typeof(Buff), "master");
        static readonly FieldInfo FI_timeWhenStarted = AccessTools.Field(typeof(Buff), "timeWhenStarted");
        static readonly FieldInfo FI_buffFxPfb = AccessTools.Field(typeof(Buff), "buffFxPfb");
        static readonly FieldInfo FI_buffFxInstance = AccessTools.Field(typeof(Buff), "buffFxInstance");
        static readonly FieldInfo FI_OnSetupEvent = AccessTools.Field(typeof(Buff), "OnSetupEvent");
        static readonly FieldInfo FI_effects = AccessTools.Field(typeof(Buff), "effects");
        static readonly MethodInfo MI_OnSetup = AccessTools.Method(typeof(Buff), "OnSetup");

        static bool Prefix(Buff __instance, CharacterBuffManager manager)
        {
            // 有 CharacterItem：让原方法照常执行
            var masterCMC = manager ? manager.Master : null;
            var item = (masterCMC != null) ? masterCMC.CharacterItem : null;
            if (item != null && item.transform != null) return true;

            // —— 无 CharacterItem 的“兜底初始化” —— //
            // 写 master / timeWhenStarted
            FI_master?.SetValue(__instance, manager);
            FI_timeWhenStarted?.SetValue(__instance, Time.time);

            // 先把 Buff 掛到角色 Transform 上（不要去访问 CharacterItem.transform）
            var parent = masterCMC ? masterCMC.transform : __instance.transform.parent;
            if (parent) __instance.transform.SetParent(parent, false);

            // 刷新 FX：销毁旧的，按角色的 ArmorSocket/根节点生成新的
            var oldFx = FI_buffFxInstance?.GetValue(__instance) as GameObject;
            if (oldFx) UnityEngine.Object.Destroy(oldFx);

            var pfb = FI_buffFxPfb?.GetValue(__instance) as GameObject;
            if (pfb && masterCMC && masterCMC.characterModel)
            {
                var fx = Object.Instantiate(pfb);
                var t = masterCMC.characterModel.ArmorSocket ? masterCMC.characterModel.ArmorSocket : masterCMC.transform;
                fx.transform.SetParent(t);
                fx.transform.position = t.position;
                fx.transform.localRotation = Quaternion.identity;
                FI_buffFxInstance?.SetValue(__instance, fx);
            }

            // 跳过 effects.SetItem（当前没 Item 可设），但先把 OnSetup / OnSetupEvent 触发掉
            MI_OnSetup?.Invoke(__instance, null);
            var onSetupEvent = FI_OnSetupEvent?.GetValue(__instance) as UnityEvent;
            onSetupEvent?.Invoke();

            // 挂一个一次性补丁组件，等 CharacterItem 可用后把 SetItem/SetParent 补上
            if (!__instance.gameObject.GetComponent<_BuffLateBinder>())
            {
                var binder = __instance.gameObject.AddComponent<_BuffLateBinder>();
                binder.Init(__instance, FI_effects);
            }

            //sans的主义
            return false;
        }


        [HarmonyPatch(typeof(CharacterBuffManager), nameof(CharacterBuffManager.AddBuff))]
        static class Patch_BroadcastBuffToOwner
        {
            static void Postfix(CharacterBuffManager __instance, Buff buffPrefab, CharacterMainControl fromWho, int overrideWeaponID)
            {
                var mod = ModBehaviourF.Instance;
                if (mod == null || !mod.networkStarted || !mod.IsServer) return;
                if (buffPrefab == null) return;

                var target = __instance.Master;                // 被加 Buff 的角色
                if (target == null) return;

                // 只给"这名远端玩家本人"发：在服务器的 remoteCharacters: string -> GameObject 中查找
                string endPoint = null;
                foreach (var kv in mod.remoteCharacters)
                {
                    if (kv.Value == null) continue;
                    if (kv.Value == target.gameObject) { endPoint = kv.Key; break; }
                }
                if (string.IsNullOrEmpty(endPoint)) return; // 非玩家，或者就是主机本地角色

                // 发一条"自加 Buff"消息（只给这名玩家）
                var w = new NetDataWriter();
                w.Put((byte)Op.PLAYER_BUFF_SELF_APPLY); // 新 opcode（见 Mod.cs）
                w.Put(overrideWeaponID);   // weaponTypeId：客户端可用它解析出正确的 buff prefab
                w.Put(buffPrefab.ID);      // 兜底：buffId（若武器没法解析，就用 id 回退）
                CoopTool.SendToEndPoint(endPoint, w.Data, w.Length, DeliveryMethod.ReliableOrdered);
            }
        }


        [HarmonyPatch(typeof(CharacterBuffManager), nameof(CharacterBuffManager.AddBuff))]
        static class Patch_BroadcastBuffApply
        {
            static void Postfix(CharacterBuffManager __instance, Buff buffPrefab, CharacterMainControl fromWho, int overrideWeaponID)
            {
                var mod = ModBehaviourF.Instance;
                if (mod == null || !mod.networkStarted || !mod.IsServer) return;
                if (buffPrefab == null) return;

                var target = __instance.Master; // 被加 Buff 的角色
                if (target == null) return;

                // ① 原有：只通知"被命中的那位本人客户端"做自加（保证本地玩法效果）
                string ownerEndPoint = null;
                foreach (var kv in mod.remoteCharacters)
                {
                    if (kv.Value == null) continue;
                    if (kv.Value == target.gameObject) { ownerEndPoint = kv.Key; break; }
                }
                if (!string.IsNullOrEmpty(ownerEndPoint))
                {
                    var w = new NetDataWriter();
                    w.Put((byte)Op.PLAYER_BUFF_SELF_APPLY);
                    w.Put(overrideWeaponID);
                    w.Put(buffPrefab.ID);
                    CoopTool.SendToEndPoint(ownerEndPoint, w.Data, w.Length, DeliveryMethod.ReliableOrdered);
                }

                // ② 如果“被命中者是主机本体”，就广播给所有客户端，让他们在“主机的代理对象”上也加 Buff（用于可见 FX）
                if (target.IsMainCharacter)
                {
                    var w2 = new NetDataWriter();
                    w2.Put((byte)Op.HOST_BUFF_PROXY_APPLY);
                    // 用你们现有的玩家标识：Host 的 endPoint 已在 InitializeLocalPlayer 里设为 "Host:端口"
                    w2.Put(mod.localPlayerStatus?.EndPoint ?? ("Host:" + mod.port));
                    w2.Put(overrideWeaponID);
                    w2.Put(buffPrefab.ID);
                    mod.netManager.SendToAll(w2, DeliveryMethod.ReliableOrdered);
                }
            }
        }
    }



}
