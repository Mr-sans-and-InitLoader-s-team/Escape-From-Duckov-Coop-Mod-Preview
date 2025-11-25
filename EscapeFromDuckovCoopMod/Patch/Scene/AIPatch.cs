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
using Duckov.Scenes;
using ECM2;
using HarmonyLib;
using System;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CharacterSpawnerRoot), "StartSpawn")]
public static class Patch_CharacterSpawnerRoot_StartSpawn
{
    public static bool Prefix(CharacterSpawnerRoot __instance)
    {
        var svc = NetService.Instance;

        if(MultiSceneCore.Instance.SceneInfo.ID == "Base" && svc.networkStarted)
            return true;

        if (svc != null && !svc.IsServer)
            return false;   
        return true;
    }
}

[HarmonyPatch(typeof(CharacterSpawnerGroup), nameof(CharacterSpawnerGroup.StartSpawn))]
public static class Patch_CharacterSpawnerGroup_StartSpawn
{
    public static bool Prefix()
    {
        var svc = NetService.Instance;
        if (MultiSceneCore.Instance.SceneInfo.ID == "Base" && svc.networkStarted)
            return true;
        if (svc != null && !svc.IsServer)
            return false;
        return true;
    }
}

[HarmonyPatch(typeof(CharacterSpawnerGroupSelector), nameof(CharacterSpawnerGroupSelector.StartSpawn))]
public static class Patch_CharacterSpawnerGroupSelector_StartSpawn
{
    public static bool Prefix()
    {
        var svc = NetService.Instance;
        if (MultiSceneCore.Instance.SceneInfo.ID == "Base" && svc.networkStarted)
            return true;
        if (svc != null && !svc.IsServer)
            return false;
        return true;
    }
}

[HarmonyPatch(typeof(AICharacterController), nameof(AICharacterController.Init))]
public static class Patch_AICharacterController_Init
{
    public static void Postfix(AICharacterController __instance)
    {
        if (__instance == null) return;

        DelayedRegister(__instance).Forget();
    }

    private static async UniTaskVoid DelayedRegister(AICharacterController ai)
    {
        if (ai == null) return;

     

        // 等待 1 秒钟，确保 AI 完全初始化然后就可以注册到 COOPManager 里通过数据库同步了~~
        var token = ai.GetCancellationTokenOnDestroy();
        try
        {
            await UniTask.Delay(1000, cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

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
    // 记录上一帧位置和定时器
    static readonly Dictionary<CharacterSoundMaker, Vector3> lastPos = new();
    static readonly Dictionary<CharacterSoundMaker, float> timers = new();

    static bool Prefix(CharacterSoundMaker __instance)
    {
        var cmc = __instance.characterMainControl;
        if (cmc == null)
            return true; // 没角色就让原版随便跑

        // 只改：主机 + 远端玩家
        if (!ModBehaviourF.Instance.IsServer || cmc == LevelManager.Instance.MainCharacter || !cmc.GetComponentInParent<RemoteReplicaTag>())
            return true; // 本地玩家、客户端直接走原版

        var pos = __instance.transform.position;
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return false; // 不需要原版

        if (!lastPos.TryGetValue(__instance, out var lp))
            lp = pos;

        float dist = Vector3.Distance(pos, lp);
        lastPos[__instance] = pos;

        // 优先用网络同步的速度，兜底用位移算速度
        float speed = GetSyncedSpeed(cmc, pos, lp, dt);

        // 速度太小，当没动
        const float minSpeed = 0.5f;
        if (speed < minSpeed)
        {
            timers[__instance] = 0f;
            return false; // 不跑原版
        }

        // 计时器
        float timer = 0f;
        timers.TryGetValue(__instance, out timer);
        timer += dt;

        // ===== 核心：用速度判断是走还是跑 =====
        var runSpeedThreshold = ResolveRunSpeedThreshold(cmc);
        bool running = speed > runSpeedThreshold;

        float interval = 1f / (running ? __instance.runSoundFrequence
                                       : __instance.walkSoundFrequence);

        if (timer < interval)
        {
            timers[__instance] = timer;
            return false;
        }

        timers[__instance] = 0f;

        // 是否重步（跟原版逻辑一致：负重>=75%）
        bool heavy = false;
        if (cmc.CharacterItem && cmc.MaxWeight > 0.1f)
        {
            heavy = (cmc.CharacterItem.TotalWeight / cmc.MaxWeight) >= 0.55f;
        }

        // 组 AISound
        AISound sound = default;
        sound.pos = pos;
        sound.fromTeam = cmc.Team;
        sound.soundType = SoundTypes.unknowNoise;
        sound.fromObject = cmc.gameObject;
        sound.fromCharacter = cmc;

        if (running && __instance.runSoundDistance > 0f)
        {
            sound.radius = __instance.runSoundDistance * (heavy ? 1.5f : 1f);

            CharacterSoundMaker.OnFootStepSound?.Invoke(
                pos,
                heavy ? CharacterSoundMaker.FootStepTypes.runHeavy
                      : CharacterSoundMaker.FootStepTypes.runLight,
                cmc);
        }
        else if (__instance.walkSoundDistance > 0f)
        {
            sound.radius = __instance.walkSoundDistance * (heavy ? 1.5f : 1f);

            CharacterSoundMaker.OnFootStepSound?.Invoke(
                pos,
                heavy ? CharacterSoundMaker.FootStepTypes.walkHeavy
                      : CharacterSoundMaker.FootStepTypes.walkLight,
                cmc);
        }

        if (sound.radius > 0f)
        {
            AIMainBrain.MakeSound(sound);
        }

        return false;
    }

    private static float GetSyncedSpeed(CharacterMainControl cmc, Vector3 pos, Vector3 lastPos, float dt)
    {
        var svc = NetService.Instance;
        if (svc != null)
        {
            var peer = CoopTool.TryGetPeerForCharacter(cmc);
            if (peer != null && svc.playerStatuses.TryGetValue(peer, out var st))
            {
                var netSpeed = st.Velocity.magnitude;
                if (netSpeed > 0.01f)
                    return netSpeed;
            }
        }

        return Vector3.Distance(pos, lastPos) / Mathf.Max(dt, 0.0001f);
    }

    private static float ResolveRunSpeedThreshold(CharacterMainControl cmc)
    {
        try
        {
            var mv = cmc.movementControl;
            if (mv != null)
            {
                var walk = Mathf.Max(0f, mv.walkSpeed);
                var run = Mathf.Max(walk, mv.runSpeed);
                var threshold = Mathf.Lerp(walk, run, 0.65f);
                return Mathf.Max(1f, threshold);
            }
        }
        catch
        {
            // ignored
        }

        return 4.5f;
    }
}