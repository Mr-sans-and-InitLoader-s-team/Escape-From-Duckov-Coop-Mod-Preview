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

using UnityEngine;
using EscapeFromDuckovCoopMod.Net;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// SceneNet 投票辅助类 - 提供 JSON 投票系统的便捷方法
/// </summary>
public static class SceneNetVoteHelper
{
    /// <summary>
    /// 主机：使用 JSON 系统发起投票
    /// </summary>
    public static void Host_StartJsonVote(string targetSceneId, string curtainGuid,
        bool notifyEvac, bool saveToFile, bool useLocation, string locationName)
    {
        var sceneNet = SceneNet.Instance;
        if (sceneNet == null)
        {
            Debug.LogWarning("[SceneVote] SceneNet.Instance 为空");
            return;
        }

        // 更新 SceneNet 的状态
        sceneNet.sceneTargetId = targetSceneId ?? "";
        sceneNet.sceneCurtainGuid = string.IsNullOrEmpty(curtainGuid) ? null : curtainGuid;
        sceneNet.sceneNotifyEvac = notifyEvac;
        sceneNet.sceneSaveToFile = saveToFile;
        sceneNet.sceneUseLocation = useLocation;
        sceneNet.sceneLocationName = locationName ?? "";

        // 重置场景门控状态
        sceneNet._srvSceneGateOpen = false;
        sceneNet._srvGateReadyPids.Clear();
        Debug.Log("[GATE] 投票开始，重置场景门控状态");

        // 使用 JSON 投票系统
        SceneVoteMessage.Host_StartVote(targetSceneId, curtainGuid, notifyEvac, saveToFile, useLocation, locationName);
        
        Debug.Log($"[SCENE] 投票开始 (JSON): target='{targetSceneId}', loc='{locationName}'");
    }

    /// <summary>
    /// 客户端：使用 JSON 系统请求发起投票
    /// </summary>
    public static void Client_RequestJsonVote(string targetId, string curtainGuid,
        bool notifyEvac, bool saveToFile, bool useLocation, string locationName)
    {
        SceneVoteMessage.Client_RequestVote(targetId, curtainGuid, notifyEvac, saveToFile, useLocation, locationName);
    }
}
