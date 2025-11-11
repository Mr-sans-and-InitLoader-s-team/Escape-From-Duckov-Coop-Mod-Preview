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

using LiteNetLib;
using UnityEngine;
using EscapeFromDuckovCoopMod.Net;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// JSON消息路由器 - 根据消息类型分发到不同的处理器
/// </summary>
public static class JsonMessageRouter
{
    /// <summary>
    /// 基础JSON消息结构（用于识别type字段）
    /// </summary>
    [System.Serializable]
    private class BaseJsonMessage
    {
        public string type;
    }

    /// <summary>
    /// 处理接收到的JSON消息（Op.JSON）
    /// 根据type字段路由到对应的处理器
    /// </summary>
    /// <param name="reader">网络数据读取器</param>
    /// <param name="fromPeer">发送消息的对等端（仅主机端有效）</param>
    public static void HandleJsonMessage(NetPacketReader reader, NetPeer fromPeer = null)
    {
        if (reader == null)
        {
            Debug.LogWarning("[JsonRouter] reader为空");
            return;
        }

        var json = reader.GetString();
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[JsonRouter] 收到空JSON消息");
            return;
        }

        try
        {
            // 先解析基础结构获取type字段
            var baseMsg = JsonUtility.FromJson<BaseJsonMessage>(json);
            if (baseMsg == null || string.IsNullOrEmpty(baseMsg.type))
            {
                Debug.LogWarning($"[JsonRouter] JSON消息缺少type字段: {json}");
                return;
            }

            Debug.Log($"[JsonRouter] 收到JSON消息，type={baseMsg.type}");

            // 根据type路由到对应的处理器
            switch (baseMsg.type)
            {
                case "setId":
                    HandleSetIdMessage(json);
                    break;

                case "lootFullSync":
                    // 战利品箱全量同步
                    LootFullSyncMessage.Client_OnLootFullSync(json);
                    break;

                case "sceneVote":
                    // 场景投票状态广播
                    SceneVoteMessage.Client_HandleVoteState(json);
                    break;

                case "sceneVoteRequest":
                    // 客户端请求发起投票
                    SceneVoteMessage.Host_HandleVoteRequest(json);
                    break;

                case "sceneVoteReady":
                    // 客户端切换准备状态
                    SceneVoteMessage.Host_HandleReadyToggle(json);
                    break;

                case "forceSceneLoad":
                    // 强制场景切换（投票成功后）
                    SceneVoteMessage.Client_HandleForceSceneLoad(json);
                    break;

                case "updateClientStatus":
                    // 客户端状态上报
                    HandleClientStatusMessage(json, fromPeer);
                    break;

                case "kick":
                    // 踢人消息
                    KickMessage.Client_HandleKickMessage(json);
                    break;

                case "test":
                    // 测试消息（向后兼容）
                    HandleTestMessage(json);
                    break;

                default:
                    Debug.LogWarning($"[JsonRouter] 未知的消息类型: {baseMsg.type}");
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JsonRouter] 处理JSON消息失败: {ex.Message}\nJSON: {json}");
        }
    }

    /// <summary>
    /// 处理SetId消息
    /// </summary>
    private static void HandleSetIdMessage(string json)
    {
        var service = NetService.Instance;
        if (service == null)
        {
            Debug.LogWarning("[JsonRouter] NetService未初始化");
            return;
        }

        if (service.IsServer)
        {
            Debug.LogWarning("[JsonRouter] 主机不应该接收SetId消息");
            return;
        }

        try
        {
            var data = JsonUtility.FromJson<SetIdMessage.SetIdData>(json);
            if (data == null)
            {
                Debug.LogError("[JsonRouter] SetId消息解析失败");
                return;
            }

            var oldId = service.localPlayerStatus?.EndPoint;
            var newId = data.networkId;

            Debug.Log($"[SetId] 收到主机告知的网络ID: {newId}");
            Debug.Log($"[SetId] 旧ID: {oldId}");

            // 更新本地玩家状态的EndPoint
            if (service.localPlayerStatus != null)
            {
                service.localPlayerStatus.EndPoint = newId;
                Debug.Log($"[SetId] ✓ 已更新 localPlayerStatus.EndPoint: {oldId} → {newId}");
            }
            else
            {
                Debug.LogWarning("[SetId] localPlayerStatus为空，无法更新");
            }

            // 检查是否有自己的远程副本需要清理
            CleanupSelfDuplicate(oldId, newId);

            // 🆕 SetId 处理完成后，发送客户端状态（包含 SteamID 和 EndPoint）
            // 此时 EndPoint 已经被更新为主机分配的真实网络ID
            ClientStatusMessage.Client_SendStatusUpdate();
            Debug.Log("[SetId] ✓ 已发送客户端状态更新（包含 SteamID）");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JsonRouter] 处理SetId消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理客户端为自己创建的远程副本
    /// </summary>
    private static void CleanupSelfDuplicate(string oldId, string newId)
    {
        var service = NetService.Instance;
        if (service == null || service.clientRemoteCharacters == null)
            return;

        var toRemove = new System.Collections.Generic.List<string>();

        foreach (var kv in service.clientRemoteCharacters)
        {
            var playerId = kv.Key;
            var go = kv.Value;

            // 检查是否是自己的副本（使用旧ID或新ID）
            if (playerId == oldId || playerId == newId)
            {
                Debug.LogWarning($"[SetId] 发现自己的远程副本，准备删除: {playerId}");
                toRemove.Add(playerId);
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                    Debug.Log($"[SetId] ✓ 已删除远程副本GameObject: {playerId}");
                }
            }
        }

        foreach (var id in toRemove)
        {
            service.clientRemoteCharacters.Remove(id);
            Debug.Log($"[SetId] ✓ 已从clientRemoteCharacters移除: {id}");
        }

        if (toRemove.Count > 0)
        {
            Debug.Log($"[SetId] ✓ 清理完成，共删除 {toRemove.Count} 个自己的远程副本");
        }
    }

    /// <summary>
    /// 处理客户端状态上报消息
    /// </summary>
    private static void HandleClientStatusMessage(string json, NetPeer fromPeer)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
        {
            Debug.LogWarning("[JsonRouter] 只有主机可以接收客户端状态消息");
            return;
        }

        if (fromPeer == null)
        {
            Debug.LogWarning("[JsonRouter] fromPeer为空，无法处理客户端状态消息");
            return;
        }

        try
        {
            ClientStatusMessage.Host_HandleClientStatus(fromPeer, json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JsonRouter] 处理客户端状态消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理测试消息（向后兼容）
    /// </summary>
    private static void HandleTestMessage(string json)
    {
        try
        {
            var data = JsonUtility.FromJson<JsonMessage.TestJsonData>(json);
            Debug.Log($"[JsonRouter] 测试消息: {data.message} (时间: {data.timestamp}, 随机值: {data.randomValue})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JsonRouter] 处理测试消息失败: {ex.Message}");
        }
    }
}
