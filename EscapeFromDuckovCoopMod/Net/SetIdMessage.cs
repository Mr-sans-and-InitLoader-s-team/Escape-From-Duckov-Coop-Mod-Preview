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

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// SetId消息 - 主机告知客户端其真实网络ID
/// 用于解决多网卡环境下客户端ID不匹配的问题
/// </summary>
public static class SetIdMessage
{
    /// <summary>
    /// SetId消息数据结构
    /// </summary>
    [System.Serializable]
    public class SetIdData
    {
        public string type = "setId";  // 消息类型标识
        public string networkId;        // 主机看到的客户端网络ID（peer.EndPoint）
        public string timestamp;        // 时间戳（用于调试）
    }

    /// <summary>
    /// 主机：发送SetId消息给指定客户端
    /// </summary>
    /// <param name="peer">目标客户端的Peer</param>
    public static void SendSetIdToPeer(NetPeer peer)
    {
        if (peer == null)
        {
            Debug.LogWarning("[SetId] SendSetIdToPeer: peer为空");
            return;
        }

        var service = NetService.Instance;
        if (service == null || !service.IsServer)
        {
            Debug.LogWarning("[SetId] SendSetIdToPeer 只能在服务器端调用");
            return;
        }

        var networkId = peer.EndPoint.ToString();
        var data = new SetIdData
        {
            networkId = networkId,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };

        JsonMessage.SendToPeer(peer, data, DeliveryMethod.ReliableOrdered);
        Debug.Log($"[SetId] 发送SetId给客户端: {networkId}");
    }

    /// <summary>
    /// 客户端：处理接收到的SetId消息
    /// </summary>
    /// <param name="reader">网络数据读取器</param>
    public static void HandleSetIdMessage(NetPacketReader reader)
    {
        var service = NetService.Instance;
        if (service == null)
        {
            Debug.LogWarning("[SetId] NetService未初始化");
            return;
        }

        if (service.IsServer)
        {
            Debug.LogWarning("[SetId] 主机不应该接收SetId消息");
            return;
        }

        JsonMessage.HandleReceivedJson<SetIdData>(reader, data =>
        {
            if (data.type != "setId")
            {
                Debug.LogWarning($"[SetId] 消息类型不匹配: {data.type}");
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
        });
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
    /// 检查JSON消息是否是SetId类型
    /// </summary>
    public static bool IsSetIdMessage(string json)
    {
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            // 简单检查是否包含 "setId" 类型标识
            return json.Contains("\"type\"") && json.Contains("\"setId\"");
        }
        catch
        {
            return false;
        }
    }
}
