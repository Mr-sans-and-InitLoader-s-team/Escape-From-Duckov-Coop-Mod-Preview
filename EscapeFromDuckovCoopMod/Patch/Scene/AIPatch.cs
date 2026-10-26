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

using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using Duckov.MiniMaps;
using Duckov.Scenes;
using ECM2;
using HarmonyLib;
using System;
using static Unity.Burst.Intrinsics.X86.Avx;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CharacterSpawnerRoot), "StartSpawn")]
public static class Patch_CharacterSpawnerRoot_StartSpawn
{
    public static bool Prefix(CharacterSpawnerRoot __instance)
    {
        var svc = NetService.Instance;

        if (svc == null || !svc.networkStarted)
            return true;

        if (MultiSceneCore.Instance?.SceneInfo?.ID == "Base")
            return true;

        if (!svc.IsServer)
        {
            AISpawnerTriggerNet.Client_RequestStart(__instance);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(CharacterSpawnerGroup), nameof(CharacterSpawnerGroup.StartSpawn))]
public static class Patch_CharacterSpawnerGroup_StartSpawn
{
    public static bool Prefix(CharacterSpawnerGroup __instance)
    {
        var svc = NetService.Instance;
        if (svc == null || !svc.networkStarted)
            return true;

        if (MultiSceneCore.Instance?.SceneInfo?.ID == "Base")
            return true;

        if (!svc.IsServer)
        {
            AISpawnerTriggerNet.Client_RequestStart(__instance);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(CharacterSpawnerGroupSelector), nameof(CharacterSpawnerGroupSelector.StartSpawn))]
public static class Patch_CharacterSpawnerGroupSelector_StartSpawn
{
    public static bool Prefix(CharacterSpawnerGroupSelector __instance)
    {
        var svc = NetService.Instance;
        if (svc == null || !svc.networkStarted)
            return true;

        if (MultiSceneCore.Instance?.SceneInfo?.ID == "Base")
            return true;

        if (!svc.IsServer)
        {
            AISpawnerTriggerNet.Client_RequestStart(__instance);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(WaveCharacterSpawner), nameof(WaveCharacterSpawner.StartSpawn))]
public static class Patch_WaveCharacterSpawner_StartSpawn
{
    public static bool Prefix(WaveCharacterSpawner __instance)
    {
        var svc = NetService.Instance;
        if (svc == null || !svc.networkStarted)
            return true;

        if (MultiSceneCore.Instance?.SceneInfo?.ID == "Base")
            return true;

        if (!svc.IsServer)
        {
            AISpawnerTriggerNet.Client_RequestStart(__instance);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(AICharacterController), nameof(AICharacterController.Init))]
public static class Patch_AICharacterController_Init
{
    public static void Postfix(AICharacterController __instance)
    {
        if (__instance == null) return;
        if (!NetService.Instance.IsServer) { return; }
        DelayedRegister(__instance).Forget();
    }

    private static async UniTaskVoid DelayedRegister(AICharacterController ai)
    {
        if (ai == null) return;

     

        // 等待 1 秒钟，确保 AI 完全初始化然后就可以注册到 COOPManager 里通过数据库同步了~~
        var token = ai.GetCancellationTokenOnDestroy();
        try
        {
            await UniTask.Delay(800, cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        var modelRoot = ai.CharacterMainControl.characterModel;
        if (modelRoot != null && modelRoot.name.Contains("0_CharacterModel_Custom_Enemy_Invisable"))
            ai.CharacterMainControl.Health.showHealthBar = false;
        //if (MultiSceneCore.Instance.SceneInfo.ID == "Base" && !ModBehaviourF.Instance.IsServer && ai.hideIfFoundEnemy == null)
        //{
        //    GameObject.Destroy(ai.CharacterMainControl.gameObject);
        //    return;
        //}

        var svc = NetService.Instance;
        if (svc == null || !svc.IsServer) return;
        if (ai == null) return;

        DifficultyManager.ApplyToAI(ai);

        COOPManager.AI?.Server_RegisterCharacter(ai);

        try
        {
            var cmc = ai.CharacterMainControl;
            if (cmc)
            {
                var id = 0;
                if (CoopSyncDatabase.AI.TryGet(ai, out var entry) && entry != null)
                    id = entry.Id;
                ModApiEvents.RaiseAiSpawned(id, cmc);
            }
        }
        catch
        {
        }
    }
}

//[HarmonyPatch(typeof(CharacterSoundMaker), "Update")]
//static class Patch_CharacterSoundMaker_Update
//{
//    static Dictionary<CharacterSoundMaker, Vector3> lastPos = new();

//    static void Prefix(CharacterSoundMaker __instance)
//    {
//        var cmc = __instance.characterMainControl;

//        if (cmc == null)
//            return;

//        // 只在主机上对【远端玩家】做替代逻辑，
//        // 本地玩家保持原版，避免影响单机平衡
//        if (!ModBehaviourF.Instance.IsServer || cmc == LevelManager.Instance.MainCharacter)
//            return;

//        if (!lastPos.TryGetValue(__instance, out var lp))
//            lp = __instance.transform.position;

//        var pos = __instance.transform.position;
//        float dist = Vector3.Distance(pos, lp);
//        lastPos[__instance] = pos;

//        float speed = dist / Mathf.Max(Time.deltaTime, 0.0001f);

//        // 如果原始 movementControl.Velocity 是 0，但我们算出来速度>阈值，
//        // 就把 movementControl.Velocity 写成我们的虚拟速度，骗一下原版逻辑
//        if (cmc.movementControl.Velocity.magnitude < 0.5f && speed > 0.5f)
//        {
//            var mv = Traverse.Create(cmc.movementControl).Field<CharacterMovement>("characterMovement").Value;

//            if(mv != null)
//            {
//                var cmv = Traverse.Create(mv).Field<Vector3>("_velocity").Value = (pos - lp) / Mathf.Max(Time.deltaTime, 0.0001f);
//            }
//        }
//    }
//}

[HarmonyPatch(typeof(CharacterSoundMaker), "Update")]
static class Patch_CharacterSoundMaker_Update
{
    static bool Prefix(CharacterSoundMaker __instance) => !FootstepSync.IsReplica(__instance.characterMainControl);
}
