using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EscapeFromDuckovCoopMod.LootNet;

namespace EscapeFromDuckovCoopMod;

public static class RPCLoot
{

    public static void HandleLootOpenRequest(RpcContext context, LootOpenRequestRpc message)
    {
        COOPManager.LootNet?.Server_HandleLootOpenRequest(context, message);
    }

    public static void HandleLootState(RpcContext context, LootStateRpc message)
    {
        if (context.IsServer) return;
        COOPManager.LootNet?.Client_ApplyLootboxState(message);
    }

    public static void HandleLootPutRequest(RpcContext context, LootPutRequestRpc message)
    {
        COOPManager.LootNet?.Server_HandleLootPutRequest(context, message);
    }

    public static void HandleLootTakeRequest(RpcContext context, LootTakeRequestRpc message)
    {
        COOPManager.LootNet?.Server_HandleLootTakeRequest(context, message);
    }

    public static void HandleLootSplitRequest(RpcContext context, LootStackRequestRpc message)
    {
        COOPManager.LootNet?.Server_HandleLootSplitRequest(context, message);
    }

    public static void HandleLootSlotPlugRequest(RpcContext context, LootSlotPlugRequestRpc message)
    {
        COOPManager.LootNet?.Server_HandleLootSlotPlugRequest(context, message);
    }

    public static void HandleLootSlotUnplugRequest(RpcContext context, LootSlotUnplugRequestRpc message)
    {
        COOPManager.LootNet?.Server_HandleLootSlotUnplugRequest(context, message);
    }

    public static void HandleLootSlotSnapshot(RpcContext context, LootSlotSnapshotRpc message)
    {
        COOPManager.LootNet?.Server_HandleLootSlotSnapshot(context, message);
    }

    public static void HandleLootPutOk(RpcContext context, LootPutOkRpc message)
    {
        if (context.IsServer) return;
        COOPManager.LootNet?.Client_OnLootPutOk(message);
    }

    public static void HandleLootTakeOk(RpcContext context, LootTakeOkRpc message)
    {
        if (context.IsServer) return;
        COOPManager.LootNet?.Client_OnLootTakeOk(message);
    }

    public static void HandleLootDeny(RpcContext context, LootDenyRpc message)
    {
        if (context.IsServer) return;
        Debug.LogWarning($"[LOOT] request denied: {message.Reason}");
    }

    public static void HandleDeadLootSpawn(RpcContext context, DeadLootSpawnRpc message)
    {
        if (context.Service == null || context.Service.IsServer)
            return;

        if (SceneManager.GetActiveScene().buildIndex != message.SceneIndex)
            return;

        DeadLootBox.Instance?.SpawnDeadLootboxAt(message.LootUid, message.Position, message.Rotation, message.UseTombPrefab, message.PrefabName);

        if (!string.IsNullOrEmpty(message.PlayerId))
            Client_ForceRemoteDeath(context.Service, message.PlayerId);
    }

    private static void Client_ForceRemoteDeath(NetService service, string playerId)
    {
        if (service == null || service.IsServer) return;

        if (service.clientRemoteCharacters == null || !service.clientRemoteCharacters.TryGetValue(playerId, out var go) || !go)
            return;

        var cmc = go.GetComponent<CharacterMainControl>();
        var h = cmc ? cmc.Health : null;
        if (cmc == null || h == null) return;

        var max = h.MaxHealth > 0f ? h.MaxHealth : 1f;
        HealthM.Instance?.ApplyHealthAndEnsureBar(go, max, 0f);
        HealthM.Instance?.ForceRemoteOnDead(cmc);
    }
}
