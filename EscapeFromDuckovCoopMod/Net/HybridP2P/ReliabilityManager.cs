using System;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P;

public class ReliabilityManager
{
    public static ReliabilityManager Instance { get; private set; }

    private class PendingMessage
    {
        public uint messageId;
        public string rpcName;
        public long targetConnectionId;
        public byte[] data;
        public float sendTime;
        public int retryCount;
        public Action<NetDataWriter> writeAction;
    }

    private class ReceivedMessage
    {
        public uint messageId;
        public float receivedTime;
    }

    private readonly Dictionary<uint, PendingMessage> _pendingMessages = new();
    private readonly Dictionary<string, ReceivedMessage> _receivedMessages = new(); // key: connectionId_messageId
    private readonly Dictionary<string, PacketLossStats> _packetLossStats = new();

    private uint _nextMessageId = 1;

    // 配置
    private const float RETRY_TIMEOUT = 1.0f; // 1秒后重发
    private const int MAX_RETRY_COUNT = 3; // 最多重试3次
    private const float MESSAGE_CLEANUP_TIME = 10f; // 10秒后清理已接收消息记录
    private const int PACKET_LOSS_WINDOW = 100; // 统计最近100个包

    public class PacketLossStats
    {
        public int totalSent;
        public int totalLost;
        public Queue<bool> recentPackets = new Queue<bool>(); // true=成功, false=丢失
        
        public float GetLossRate()
        {
            if (totalSent == 0) return 0f;
            return (float)totalLost / totalSent;
        }

        public float GetRecentLossRate()
        {
            if (recentPackets.Count == 0) return 0f;
            int lostCount = 0;
            foreach (var lost in recentPackets)
            {
                if (lost) lostCount++;
            }
            return (float)lostCount / recentPackets.Count;
        }
    }

    public ReliabilityManager()
    {
        Instance = this;
    }

    public uint SendReliableMessage(string rpcName, long targetConnectionId, Action<NetDataWriter> writeAction)
    {
        uint messageId = _nextMessageId++;

        var writer = new NetDataWriter();
        writer.Put(messageId); // 消息ID
        writeAction(writer);

        var pending = new PendingMessage
        {
            messageId = messageId,
            rpcName = rpcName,
            targetConnectionId = targetConnectionId,
            data = writer.CopyData(),
            sendTime = Time.realtimeSinceStartup,
            retryCount = 0,
            writeAction = writeAction
        };

        _pendingMessages[messageId] = pending;

        RecordPacketSent(targetConnectionId.ToString());

        Debug.Log($"[ReliabilityManager] Sent reliable message {messageId} for RPC {rpcName}");
        return messageId;
    }

    public void OnMessageAck(uint messageId, long senderConnectionId)
    {
        if (_pendingMessages.TryGetValue(messageId, out var pending))
        {
            _pendingMessages.Remove(messageId);
            RecordPacketReceived(senderConnectionId.ToString(), false);
            Debug.Log($"[ReliabilityManager] Message {messageId} acknowledged");
        }
    }

    public bool ShouldProcessMessage(uint messageId, long senderConnectionId)
    {
        string key = $"{senderConnectionId}_{messageId}";

        if (_receivedMessages.ContainsKey(key))
        {
            Debug.Log($"[ReliabilityManager] Duplicate message {messageId} from {senderConnectionId}, skipping");
            return false; // 重复消息，不处理
        }

        _receivedMessages[key] = new ReceivedMessage
        {
            messageId = messageId,
            receivedTime = Time.realtimeSinceStartup
        };

        return true;
    }

    public void SendAck(uint messageId, long targetConnectionId)
    {
        var rpcManager = HybridRPCManager.Instance;
        if (rpcManager == null) return;

        Debug.Log($"[ReliabilityManager] Sending ACK for message {messageId} to {targetConnectionId}");

        // 发送ACK
        rpcManager.CallRPC("__MessageAck", RPCTarget.TargetClient, targetConnectionId, writer =>
        {
            writer.Put(messageId);
        }, LiteNetLib.DeliveryMethod.Unreliable);
    }

    public void Update()
    {
        float currentTime = Time.realtimeSinceStartup;

        // 检查待重发的消息
        var toRetry = new List<uint>();
        foreach (var kv in _pendingMessages)
        {
            var pending = kv.Value;
            if (currentTime - pending.sendTime > RETRY_TIMEOUT)
            {
                toRetry.Add(pending.messageId);
            }
        }

        // 重发超时的消息
        foreach (var messageId in toRetry)
        {
            if (_pendingMessages.TryGetValue(messageId, out var pending))
            {
                pending.retryCount++;

                if (pending.retryCount >= MAX_RETRY_COUNT)
                {
                    Debug.LogWarning($"[ReliabilityManager] Message {messageId} failed after {MAX_RETRY_COUNT} retries");
                    _pendingMessages.Remove(messageId);
                    RecordPacketReceived(pending.targetConnectionId.ToString(), true); // 标记为丢失
                }
                else
                {
                    pending.sendTime = currentTime;
                    Debug.LogWarning($"[ReliabilityManager] Retrying message {messageId} (attempt {pending.retryCount}/{MAX_RETRY_COUNT})");

                    var rpcManager = HybridRPCManager.Instance;
                    if (rpcManager != null)
                    {
                        rpcManager.CallRPC(pending.rpcName, RPCTarget.TargetClient, pending.targetConnectionId, 
                            pending.writeAction, LiteNetLib.DeliveryMethod.ReliableOrdered);
                    }
                }
            }
        }

        // 清理过期的已接收消息记录
        var toRemove = new List<string>();
        foreach (var kv in _receivedMessages)
        {
            if (currentTime - kv.Value.receivedTime > MESSAGE_CLEANUP_TIME)
            {
                toRemove.Add(kv.Key);
            }
        }
        foreach (var key in toRemove)
        {
            _receivedMessages.Remove(key);
        }
    }

    private void RecordPacketSent(string connectionId)
    {
        if (!_packetLossStats.TryGetValue(connectionId, out var stats))
        {
            stats = new PacketLossStats();
            _packetLossStats[connectionId] = stats;
        }

        stats.totalSent++;
    }

    private void RecordPacketReceived(string connectionId, bool lost)
    {
        if (!_packetLossStats.TryGetValue(connectionId, out var stats))
        {
            stats = new PacketLossStats();
            _packetLossStats[connectionId] = stats;
        }

        if (lost)
        {
            stats.totalLost++;
        }

        stats.recentPackets.Enqueue(lost);
        if (stats.recentPackets.Count > PACKET_LOSS_WINDOW)
        {
            stats.recentPackets.Dequeue();
        }
    }

    public PacketLossStats GetPacketLossStats(string connectionId)
    {
        return _packetLossStats.TryGetValue(connectionId, out var stats) ? stats : new PacketLossStats();
    }

    public void Clear()
    {
        _pendingMessages.Clear();
        _receivedMessages.Clear();
    }

    public void ClearConnection(string connectionId)
    {
        var toRemove = new List<uint>();
        foreach (var kv in _pendingMessages)
        {
            if (kv.Value.targetConnectionId.ToString() == connectionId)
            {
                toRemove.Add(kv.Key);
            }
        }

        foreach (var id in toRemove)
        {
            _pendingMessages.Remove(id);
        }

        _packetLossStats.Remove(connectionId);
    }
}

