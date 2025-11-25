using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EscapeFromDuckovCoopMod.LootNet;

namespace EscapeFromDuckovCoopMod;

public static class RPCLoot
{

    public static void HandleDeadLootSpawn(RpcContext context, DeadLootSpawnRpc message)
    {
        if (context.Service == null || context.Service.IsServer)
            return;

        if (SceneManager.GetActiveScene().buildIndex != message.SceneIndex)
            return;

        DeadLootBox.Instance?.SpawnDeadLootboxAt(message.LootUid, message.Position, message.Rotation, message.UseTombPrefab);

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
