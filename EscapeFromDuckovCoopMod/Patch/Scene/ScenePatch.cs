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

using Duckov.Scenes;
using Duckov.UI;
using Eflatun.SceneReference;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(SceneLoaderProxy), "LoadScene")]
public static class Patch_SceneLoaderProxy_Authority
{
    private static bool Prefix(SceneLoaderProxy __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (SceneNet.Instance.allowLocalSceneLoad) return true;


        var proxySceneId = Traverse.Create(__instance).Field<string>("sceneID").Value;
        var useLoc = Traverse.Create(__instance).Field<bool>("useLocation").Value;
        var loc = Traverse.Create(__instance).Field<MultiSceneLocation>("location").Value;
        var curtain = Traverse.Create(__instance).Field<SceneReference>("overrideCurtainScene").Value;
        var notifyEvac = Traverse.Create(__instance).Field<bool>("notifyEvacuation").Value;
        var save = Traverse.Create(__instance).Field<bool>("saveToFile").Value;

        var targetId = proxySceneId;
        var locationName = useLoc ? loc.LocationName : null;
        var curtainGuid = curtain != null ? curtain.Guid : null;

        if (mod.IsServer)
        {
            SceneNet.Instance.Host_BeginSceneVote_Simple(targetId, curtainGuid, notifyEvac, save, useLoc, locationName);
            return false;
        }

        if(__instance.name == "CustomFace" || __instance.name == "SelectDifficulty")
        {
            MModUI.ShowTip("客户端请在单机进行设置! Please configure the client on a standalone machine!");
            return false;
        }
        SceneNet.Instance.Client_RequestBeginSceneVote(targetId, curtainGuid, notifyEvac, save, useLoc, locationName);
        //string mySceneId = null;
        //try { mySceneId = mod.localPlayerStatus != null ? mod.localPlayerStatus.SceneId : null; } catch { } 

        //ModBehaviour.PlayerStatus host = null;
        //if (mod.clientPlayerStatuses != null)
        //{
        //    foreach (var kv in mod.clientPlayerStatuses)
        //    {
        //        var st = kv.Value;
        //        if (st == null) continue;
        //        bool isHostName = false;
        //        try { isHostName = (st.PlayerName == "Host"); } catch { }
        //        bool isHostId = false;
        //        try { isHostId = (!string.IsNullOrEmpty(st.EndPoint) && st.EndPoint.StartsWith("Host:")); } catch { }

        //        if (isHostName || isHostId) { host = st; break; }
        //    }
        //}

        //bool hostMissing = (host == null);

        //bool hostNotInGame = false;
        //try { hostNotInGame = (host != null && !host.IsInGame); } catch { } 

        //bool hostSceneDiff = false;
        //try
        //{
        //    string hostSid = (host != null) ? host.SceneId : null;
        //    hostSceneDiff = (!string.IsNullOrEmpty(hostSid) && !string.IsNullOrEmpty(mySceneId) && !string.Equals(hostSid, mySceneId, StringComparison.Ordinal));
        //}
        //catch { }

        //bool hostDead = false;
        //try
        //{
        //    // Host 的 EndPoint 在初始化时就是 "Host:{port}"（见d1 Mod.cs.InitializeLocalPlayer）
        //    string hostKey = $"Host:{mod.port}";

        //    if (mod.clientRemoteCharacters != null &&
        //        mod.clientRemoteCharacters.TryGetValue(hostKey, out var hostProxy) &&
        //        hostProxy)
        //    {
        //        var h = hostProxy.GetComponentInChildren<Health>(true);
        //        hostDead = (h == null) || h.CurrentHealth <= 0.001f;
        //    }
        //    else
        //    {
        //        // 如果“主机状态存在且与我同图”，但连主机代理都不存在，多半也是死亡后进入观战
        //        if (!hostMissing && !hostSceneDiff) hostDead = true;
        //    }
        //}
        //catch { }

        //// 原来的 allow 条件基础上，把 hostDead 并进去
        //bool allowClientVote = hostMissing || hostNotInGame || hostSceneDiff || hostDead;

        //if (allowClientVote)
        //{
        //    Debug.Log($"[SCENE] 客户端放行切图（允许投票）：target={targetId}, hostMissing={hostMissing}, hostNotInGame={hostNotInGame}, hostSceneDiff={hostSceneDiff}");
        //    mod.Client_RequestBeginSceneVote(targetId, curtainGuid, notifyEvac, save, useLoc, locationName);
        //    return false;
        //}
        Debug.Log($"[SCENE] 客户端放行切图（允许投票）：target={targetId}");
        return false;
    }

}

//[HarmonyPatch(typeof(MultiSceneTeleporter), "DoTeleport")]
//internal static class Patch_Mapen_DoTeleport
//{
//    private static void Prefix()
//    {
//        var mod = ModBehaviourF.Instance;
//        if (mod == null || !mod.networkStarted)
//            return;

//        COOPManager.TeleportAiClear();
      
//        CoopSyncDatabase.AI.Clear();
       
//        COOPManager.AI?.Reset();

//        Debug.Log("[SCENE] MultiSceneTeleporter 触发，已清理 AI 缓存与数据库，等待主机重新同步。");
//    }
//}


//[HarmonyPatch(typeof(TimeOfDayController), "Update")]
//internal static class Patch_Mapen_TimeOfDayController
//{
//    private static bool Prefix()
//    {
//        var mod = ModBehaviourF.Instance;
//        if (mod == null || !mod.networkStarted || mod.IsServer)
//            return true;

//        return false;
//    }
//}

[HarmonyPatch(typeof(MultiSceneTeleporter), "DoTeleport")]
internal static class Patch_Mapen_DoTeleport
{
    private static bool Prefix(MultiSceneTeleporter __instance)
    {
   
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (SceneNet.Instance.allowLocalSceneLoad) return true;
        if (!mod.IsServer) return false;
    
        SceneNet.Instance.IsDoteleportMap = true;
       // SceneNet.Instance.Host_BeginSceneVote_Simple(__instance.Target.SceneID, "", false, false, false, "DoTeleport");
        if (mod.IsServer)
        {
            SceneNet.Instance.Host_BeginSceneVote_Simple(__instance.Target.SceneID, "", false, false, false, "DoTeleport");
            return false;
        }

        SceneNet.Instance.Client_RequestBeginSceneVote(__instance.Target.SceneID, "", false, false, false, "DoTeleport");
        return false;
    }
}