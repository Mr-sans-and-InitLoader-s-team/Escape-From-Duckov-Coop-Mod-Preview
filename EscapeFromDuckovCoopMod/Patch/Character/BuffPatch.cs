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

using System.Reflection;
using Duckov.Buffs;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Buff), "Setup")]
internal static class Patch_Buff_Setup_Safe
{
    // 反射缓存
    private static readonly FieldInfo FI_master = AccessTools.Field(typeof(Buff), "master");
    private static readonly FieldInfo FI_timeWhenStarted = AccessTools.Field(typeof(Buff), "timeWhenStarted");
    private static readonly FieldInfo FI_buffFxPfb = AccessTools.Field(typeof(Buff), "buffFxPfb");
    private static readonly FieldInfo FI_buffFxInstance = AccessTools.Field(typeof(Buff), "buffFxInstance");
    private static readonly FieldInfo FI_OnSetupEvent = AccessTools.Field(typeof(Buff), "OnSetupEvent");
    private static readonly FieldInfo FI_effects = AccessTools.Field(typeof(Buff), "effects");
    private static readonly MethodInfo MI_OnSetup = AccessTools.Method(typeof(Buff), "OnSetup");

    private static bool Prefix(Buff __instance, CharacterBuffManager manager)
    {
        // 有 CharacterItem：让原方法照常执行
        var masterCMC = manager ? manager.Master : null;
        var item = masterCMC != null ? masterCMC.CharacterItem : null;
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
        if (oldFx) Object.Destroy(oldFx);

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
        if (!__instance.gameObject.GetComponent<BuffLateBinder>())
        {
            var binder = __instance.gameObject.AddComponent<BuffLateBinder>();
            binder.Init(__instance, FI_effects);
        }

        //sans的主义
        return false;
    }


    [HarmonyPatch(typeof(CharacterBuffManager), nameof(CharacterBuffManager.AddBuff))]
    private static class Patch_BroadcastBuff
    {
        private static void Postfix(CharacterBuffManager __instance, Buff buffPrefab, CharacterMainControl fromWho, int overrideWeaponID)
        {
            var mod = ModBehaviourF.Instance;
            if (mod == null || !mod.networkStarted) return;
            if (buffPrefab == null) return;

            var target = __instance.Master;
            if (target == null) return;

            if (target.IsMainCharacter)
            {
                // 主机：直接广播；客户端：上报给主机
                if (mod.IsServer)
                {
                    COOPManager.Buff.Server_BroadcastHostBuff(overrideWeaponID, buffPrefab.ID);
                }
                else
                {
                    var rpc = new PlayerBuffReportRpc
                    {
                        WeaponTypeId = overrideWeaponID,
                        BuffId = buffPrefab.ID
                    };
                    CoopTool.SendRpc(in rpc);
                }
                return;
            }

            if (mod.IsServer)
            {
                COOPManager.AI?.Server_HandleBuffApplied(target, overrideWeaponID, buffPrefab.ID);
                return;
            }

            var aiTag = target.GetComponent<RemoteAIReplicaTag>();
            if (aiTag != null)
            {
                if (aiTag.SuppressBuffForward) return;
                COOPManager.AI?.Client_ReportAiBuff(aiTag.Id, overrideWeaponID, buffPrefab.ID);
            }
        }
    }
}