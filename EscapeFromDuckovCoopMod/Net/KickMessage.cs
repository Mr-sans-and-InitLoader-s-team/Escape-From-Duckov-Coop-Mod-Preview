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

using Steamworks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 踢人消息数据结构
/// </summary>
[System.Serializable]
public class KickMessageData
{
    public string type = "kick";
    public ulong targetSteamId;  // 被踢玩家的 Steam ID
    public string reason;  // 踢人原因（可选）
}

/// <summary>
/// 踢人功能 - 基于 Steam ID 的踢人系统
/// </summary>
public static class KickMessage
{
    /// <summary>
    /// 主机：踢出指定 Steam ID 的玩家
    /// </summary>
    /// <param name="targetSteamId">目标玩家的 Steam ID</param>
    /// <param name="reason">踢人原因</param>
    public static void Server_KickPlayer(ulong targetSteamId, string reason = "被主机踢出")
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
        {
            Debug.LogWarning("[KickMessage] 只有主机可以踢人");
            return;
        }

        var kickData = new KickMessageData
        {
            type = "kick",
            targetSteamId = targetSteamId,
            reason = reason
        };

        var json = JsonUtility.ToJson(kickData);
        Debug.Log($"[KickMessage] 主机踢出玩家: SteamID={targetSteamId}, 原因={reason}");

        // 广播踢人消息给所有客户端
        JsonMessage.BroadcastToAllClients(json, LiteNetLib.DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// 客户端：处理接收到的踢人消息
    /// </summary>
    /// <param name="json">JSON 消息</param>
    public static void Client_HandleKickMessage(string json)
    {
        try
        {
            var kickData = JsonUtility.FromJson<KickMessageData>(json);

            if (kickData == null || kickData.type != "kick")
            {
                return;  // 不是踢人消息，忽略
            }

            // 检查是否是踢自己
            if (SteamManager.Initialized)
            {
                var mySteamId = SteamUser.GetSteamID().m_SteamID;

                if (mySteamId == kickData.targetSteamId)
                {
                    Debug.LogWarning($"[KickMessage] 收到踢人消息: {kickData.reason}");

                    // 断开所有连接
                    var service = NetService.Instance;
                    if (service != null)
                    {
                        // 更新状态显示
                        service.status = $"已被踢出: {kickData.reason}";

                        // 断开连接
                        if (service.connectedPeer != null)
                        {
                            service.connectedPeer.Disconnect();
                        }

                        // 停止网络
                        service.StopNetwork();

                        // 如果在 Steam Lobby 中，也离开 Lobby
                        if (SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby)
                        {
                            SteamLobbyManager.Instance.LeaveLobby();
                        }
                    }

                    // 显示提示消息
                    if (MModUI.Instance != null)
                    {
                        // UI 会自动更新状态显示
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[KickMessage] 处理踢人消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查 JSON 消息是否是踢人消息
    /// </summary>
    public static bool IsKickMessage(string json)
    {
        try
        {
            // 简单检查是否包含 "kick" 类型
            return json.Contains("\"type\":\"kick\"") || json.Contains("\"type\": \"kick\"");
        }
        catch
        {
            return false;
        }
    }
}
