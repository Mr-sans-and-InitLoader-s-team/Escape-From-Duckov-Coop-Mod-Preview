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

[HarmonyPatch(typeof(CharacterAnimationControl_MagicBlend), "OnAttack")]
internal static class Patch_Melee_OnAttack_SendNetAndFx
{
    private static void Postfix(CharacterAnimationControl_MagicBlend __instance)
    {
        var mod = ModBehaviourF.Instance;
        var ctrl = __instance?.characterMainControl;
        if (mod == null || !mod.networkStarted || ctrl == null) return;
        if (ctrl != CharacterMainControl.Main) return; // 只处理本地玩家

        // 一帧一次闸门（解决重复注入/重复回调）
        var model = ctrl.characterModel;
        if (model)
        {
            var gate = model.GetComponent<LocalMeleeOncePerFrame>() ?? model.gameObject.AddComponent<LocalMeleeOncePerFrame>();
            if (gate.lastFrame == Time.frameCount) return;
            gate.lastFrame = Time.frameCount;
        }

        var melee = ctrl.CurrentHoldItemAgent as ItemAgent_MeleeWeapon;
        if (!melee) return;

        var dealDelay = 0.1f;
        try
        {
            dealDelay = Mathf.Max(0f, melee.DealDamageTime);
        }
        catch
        {
        }

        var snapPos = ctrl.modelRoot ? ctrl.modelRoot.position : ctrl.transform.position;
        var snapDir = ctrl.CurrentAimDirection.sqrMagnitude > 1e-6f ? ctrl.CurrentAimDirection : ctrl.transform.forward;

        if (mod.IsServer)
        {
            COOPManager.WeaponRequest.BroadcastMeleeSwing(mod.localPlayerStatus.EndPoint, dealDelay);
        }
        else
        {
            // 客户端：本地FX + 告诉主机
            MeleeFx.SpawnSlashFx(ctrl.characterModel);
            COOPManager.WeaponRequest.Net_OnClientMeleeAttack(dealDelay, snapPos, snapDir);
        }
    }
}

[HarmonyPatch(typeof(CharacterAnimationControl_MagicBlend), "OnAttack")]
internal static class Patch_AI_OnAttack_Broadcast
{
    private static void Postfix(CharacterAnimationControl_MagicBlend __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.IsServer) return;

        var cmc = __instance.characterMainControl;
        if (!cmc) return;

        // 排除玩家本体，只给 AI 发
        // 判断条件：有 AI 组件/或 NetAiTag（你现成的 AI id 标签）
        var aiCtrl = cmc.GetComponent<AICharacterController>();
        var aiTag = cmc.GetComponent<NetAiTag>();
        if (!aiCtrl && aiTag == null) return;

        var aiId = aiTag != null ? aiTag.aiId : 0;
        if (aiId == 0) return;

        mod.writer.Reset();
        mod.writer.Put((byte)Op.AI_ATTACK_SWING);
        mod.writer.Put(aiId);
        mod.netManager.SendToAll(mod.writer, DeliveryMethod.ReliableUnordered);
    }
}

[HarmonyPatch(typeof(CharacterAnimationControl_MagicBlend), "OnAttack")]
internal static class Patch_AI_OnAttack_BroadcastAll
{
    private static void Postfix(CharacterAnimationControl_MagicBlend __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var cmc = __instance ? __instance.characterMainControl : null;
        if (!cmc) return;

        if (cmc.IsMainCharacter) return;

        // 通过 NetAiTag 拿 aiId
        var tag = cmc.GetComponent<NetAiTag>();
        if (tag == null || tag.aiId == 0) return;

        // 持枪的 AI：逐弹丸广播由 Projectile.Init/Postfix 完成；这里不要再额外发送 FIRE_EVENT
        var gun = cmc.GetGun();
        if (gun != null) return;

        // 近战：复用玩家的 MELEE_ATTACK_SWING
        var w = new NetDataWriter();
        w.Put((byte)Op.MELEE_ATTACK_SWING);
        w.Put($"AI:{tag.aiId}");
        w.Put(__instance.attackTime); // 有就写；没有也无妨
        mod.netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }
}

[HarmonyPatch(typeof(CharacterAnimationControl_MagicBlend), "OnAttack")]
internal static class Patch_AI_OnAttack_MeleeOnly
{
    private static void Postfix(CharacterAnimationControl_MagicBlend __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        var cmc = __instance ? __instance.characterMainControl : null;
        if (!cmc || cmc.IsMainCharacter) return;

        var tag = cmc.GetComponent<NetAiTag>();
        if (tag == null || tag.aiId == 0) return;

        // 仅近战：复用玩家的 MELEE_ATTACK_SWING 协议
        var gun = cmc.GetGun();
        if (gun != null) return; // 手里是枪：真正的弹丸广播由 Projectile.Init 完成

        var w = new NetDataWriter();
        w.Put((byte)Op.MELEE_ATTACK_SWING);
        w.Put($"AI:{tag.aiId}");
        mod.netManager.SendToAll(w, DeliveryMethod.ReliableUnordered);
    }
}

[HarmonyPatch(typeof(CharacterAnimationControl_MagicBlend), "Update")]
internal static class Patch_MagicBlend_Update_SkipOnRemoteAI
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CharacterAnimationControl_MagicBlend __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (mod.IsServer) return true;

        CharacterMainControl cmc = null;
        try
        {
            var cm = __instance.characterModel;
            cmc = cm ? cm.characterMainControl : __instance.GetComponentInParent<CharacterMainControl>();
        }
        catch
        {
        }

        if (!cmc) return true;

        // 只拦“客户端上的 AI 复制体”
        var isAI =
            cmc.GetComponent<AICharacterController>() != null ||
            cmc.GetComponent<NetAiTag>() != null;

        var isRemoteReplica =
            cmc.GetComponent<NetAiFollower>() != null ||
            cmc.GetComponent<RemoteReplicaTag>() != null;

        if (isAI && isRemoteReplica)
            return false; // 跳过 Update，不要覆盖网络来的参数

        return true;
    }
}

[HarmonyPatch(typeof(CharacterAnimationControl), "Update")]
internal static class Patch_CharAnimCtrl_Update_SkipOnRemoteAI
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CharacterAnimationControl __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (mod.IsServer) return true; // 主机侧仍由本地AI驱动

        CharacterMainControl cmc = null;
        try
        {
            var cm = __instance.characterModel;
            cmc = cm ? cm.characterMainControl : __instance.GetComponentInParent<CharacterMainControl>();
        }
        catch
        {
        }

        if (!cmc) return true;

        var isAI =
            cmc.GetComponent<AICharacterController>() != null ||
            cmc.GetComponent<NetAiTag>() != null;

        var isRemoteReplica =
            cmc.GetComponent<NetAiFollower>() != null ||
            cmc.GetComponent<RemoteReplicaTag>() != null;

        // 客户端的AI复制体：拦掉本地Update
        if (isAI && isRemoteReplica)
            return false;

        return true;
    }
}