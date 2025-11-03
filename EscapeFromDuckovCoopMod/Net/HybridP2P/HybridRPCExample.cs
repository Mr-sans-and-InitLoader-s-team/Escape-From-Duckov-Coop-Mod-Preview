using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P
{
    public class HybridRPCExample : MonoBehaviour
    {
        private void Start()
        {
            RegisterExampleRPCs();
        }

        private void RegisterExampleRPCs()
        {
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null)
            {
                Debug.LogWarning("[HybridRPCExample] RPC Manager not found");
                return;
            }

            rpcManager.RegisterRPC("TestRPC", HandleTestRPC);
            rpcManager.RegisterRPC("SyncVoteData", HandleSyncVoteData);
            rpcManager.RegisterRPC("RequestPlayerData", HandleRequestPlayerData);
            
            Debug.Log("[HybridRPCExample] Example RPCs registered");
        }

        private void HandleTestRPC(long senderConnectionId, NetDataReader reader)
        {
            string message = reader.GetString();
            int value = reader.GetInt();
            
            Debug.Log($"[HybridRPCExample] Received TestRPC from {senderConnectionId}: message='{message}', value={value}");
        }

        private void HandleSyncVoteData(long senderConnectionId, NetDataReader reader)
        {
            string voteId = reader.GetString();
            int voteCount = reader.GetInt();
            bool isReady = reader.GetBool();
            
            Debug.Log($"[HybridRPCExample] Received SyncVoteData from {senderConnectionId}: voteId='{voteId}', count={voteCount}, ready={isReady}");
        }

        private void HandleRequestPlayerData(long senderConnectionId, NetDataReader reader)
        {
            ulong playerId = reader.GetULong();
            
            Debug.Log($"[HybridRPCExample] Received RequestPlayerData from {senderConnectionId}: playerId={playerId}");
            
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager != null && rpcManager.IsServer)
            {
                rpcManager.CallRPC("SendPlayerDataResponse", RPCTarget.TargetClient, senderConnectionId, (writer) =>
                {
                    writer.Put(playerId);
                    writer.Put("PlayerName");
                    writer.Put(100);
                    writer.Put(75.5f);
                });
            }
        }

        public static void CallTestRPC(string message, int value)
        {
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null)
                return;

            rpcManager.CallRPC("TestRPC", RPCTarget.Server, 0, (writer) =>
            {
                writer.Put(message);
                writer.Put(value);
            });
        }

        public static void BroadcastVoteData(string voteId, int voteCount, bool isReady)
        {
            var rpcManager = HybridRPCManager.Instance;
            if (rpcManager == null || !rpcManager.IsServer)
                return;

            rpcManager.CallRPC("SyncVoteData", RPCTarget.AllClients, 0, (writer) =>
            {
                writer.Put(voteId);
                writer.Put(voteCount);
                writer.Put(isReady);
            });
        }
    }
}

