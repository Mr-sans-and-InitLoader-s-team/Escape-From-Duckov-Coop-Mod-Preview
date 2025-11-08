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
/// JSON消息工具类 - 提供通用的JSON消息发送和接收功能
/// </summary>
public static class JsonMessage
{
    /// <summary>
    /// 广播JSON消息给所有客户端（仅服务器可调用）
    /// </summary>
    /// <param name="jsonData">要发送的JSON字符串</param>
    /// <param name="deliveryMethod">传输方式，默认为可靠有序</param>
    public static void BroadcastToAllClients(
        string jsonData,
        DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered
    )
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer)
        {
            Debug.LogWarning("[JsonMessage] BroadcastToAllClients 只能在服务器端调用");
            return;
        }

        var netManager = service.netManager;
        if (netManager == null || netManager.ConnectedPeerList.Count == 0)
        {
            Debug.LogWarning("[JsonMessage] 没有连接的客户端");
            return;
        }

        var writer = service.writer;
        writer.Reset();
        writer.Put((byte)Op.JSON);
        writer.Put(jsonData);

        int sentCount = 0;
        foreach (var peer in netManager.ConnectedPeerList)
        {
            peer.Send(writer, deliveryMethod);
            sentCount++;
        }

        Debug.Log($"[JSON] 暴力发包 {sentCount} 个客户端");
    }

    /// <summary>
    /// 广播JSON对象给所有客户端（仅服务器可调用）
    /// </summary>
    /// <typeparam name="T">可序列化的对象类型</typeparam>
    /// <param name="data">要发送的对象</param>
    /// <param name="deliveryMethod">传输方式，默认为可靠有序</param>
    public static void BroadcastToAllClients<T>(
        T data,
        DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered
    )
    {
        var json = JsonUtility.ToJson(data, true);
        BroadcastToAllClients(json, deliveryMethod);
    }

    /// <summary>
    /// 发送JSON消息给指定的Peer
    /// </summary>
    /// <param name="peer">目标Peer</param>
    /// <param name="jsonData">要发送的JSON字符串</param>
    /// <param name="deliveryMethod">传输方式，默认为可靠有序</param>
    public static void SendToPeer(
        NetPeer peer,
        string jsonData,
        DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered
    )
    {
        if (peer == null)
        {
            Debug.LogWarning("[JsonMessage] SendToPeer: peer为空");
            return;
        }

        var service = NetService.Instance;
        if (service == null)
        {
            Debug.LogWarning("[JsonMessage] NetService未初始化");
            return;
        }

        var writer = service.writer;
        writer.Reset();
        writer.Put((byte)Op.JSON);
        writer.Put(jsonData);

        peer.Send(writer, deliveryMethod);

        Debug.Log($"[JSON] 发送到 {peer.EndPoint}");
    }

    /// <summary>
    /// 发送JSON对象给指定的Peer
    /// </summary>
    /// <typeparam name="T">可序列化的对象类型</typeparam>
    /// <param name="peer">目标Peer</param>
    /// <param name="data">要发送的对象</param>
    /// <param name="deliveryMethod">传输方式，默认为可靠有序</param>
    public static void SendToPeer<T>(
        NetPeer peer,
        T data,
        DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered
    )
    {
        var json = JsonUtility.ToJson(data, true);
        SendToPeer(peer, json, deliveryMethod);
    }

    /// <summary>
    /// 发送JSON消息给主机（仅客户端可调用）
    /// </summary>
    /// <param name="jsonData">要发送的JSON字符串</param>
    /// <param name="deliveryMethod">传输方式，默认为可靠有序</param>
    public static void SendToHost(
        string jsonData,
        DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered
    )
    {
        var service = NetService.Instance;
        if (service == null)
        {
            Debug.LogWarning("[JsonMessage] NetService未初始化");
            return;
        }

        if (service.IsServer)
        {
            Debug.LogWarning("[JsonMessage] SendToHost 只能在客户端调用");
            return;
        }

        var connectedPeer = service.connectedPeer;
        if (connectedPeer == null)
        {
            Debug.LogWarning("[JsonMessage] 未连接到主机");
            return;
        }

        SendToPeer(connectedPeer, jsonData, deliveryMethod);
    }

    /// <summary>
    /// 发送JSON对象给主机（仅客户端可调用）
    /// </summary>
    /// <typeparam name="T">可序列化的对象类型</typeparam>
    /// <param name="data">要发送的对象</param>
    /// <param name="deliveryMethod">传输方式，默认为可靠有序</param>
    public static void SendToHost<T>(
        T data,
        DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered
    )
    {
        var json = JsonUtility.ToJson(data, true);
        SendToHost(json, deliveryMethod);
    }

    /// <summary>
    /// 处理接收到的JSON消息
    /// </summary>
    /// <param name="reader">网络数据读取器</param>
    /// <param name="onReceived">接收回调，参数为JSON字符串</param>
    public static void HandleReceivedJson(
        NetPacketReader reader,
        System.Action<string> onReceived = null
    )
    {
        var json = reader.GetString();
        Debug.Log($"[JSON] 收到消息");
        onReceived?.Invoke(json);
    }

    /// <summary>
    /// 处理接收到的JSON消息并解析为指定类型
    /// </summary>
    /// <typeparam name="T">要解析的对象类型</typeparam>
    /// <param name="reader">网络数据读取器</param>
    /// <param name="onReceived">接收回调，参数为解析后的对象</param>
    public static void HandleReceivedJson<T>(
        NetPacketReader reader,
        System.Action<T> onReceived = null
    )
    {
        var json = reader.GetString();

        try
        {
            var data = JsonUtility.FromJson<T>(json);
            Debug.Log($"[JSON] 收到 {typeof(T).Name}");
            onReceived?.Invoke(data);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[JSON] 解析失败: {ex.Message}");
        }
    }

    #region 测试方法（保留向后兼容）

    /// <summary>
    /// 发送测试JSON消息（向后兼容）
    /// </summary>
    public static void SendTestJson(NetPeer peer, NetDataWriter writer)
    {
        var isServer = NetService.Instance?.IsServer ?? false;
        var testData = new TestJsonData
        {
            message = "Hello from " + (isServer ? "Server" : "Client"),
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            randomValue = UnityEngine.Random.Range(1, 1000),
        };

        SendToPeer(peer, testData);
    }

    /// <summary>
    /// 处理接收到的测试JSON消息（向后兼容）
    /// </summary>
    public static void HandleReceivedJson(NetPacketReader reader)
    {
        HandleReceivedJson<TestJsonData>(reader);
    }

    /// <summary>
    /// 测试用的JSON数据结构
    /// </summary>
    [System.Serializable]
    public class TestJsonData
    {
        public string message;
        public string timestamp;
        public int randomValue;
    }

    #endregion
}
