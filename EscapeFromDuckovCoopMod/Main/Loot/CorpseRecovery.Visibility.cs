using ItemStatsSystem;
using UnityEngine.SceneManagement;

namespace EscapeFromDuckovCoopMod;

internal static partial class CorpseRecovery
{
    private static void UpdateVisibility(Inventory inventory, bool visible)
    {
        if (!CoopSyncDatabase.Loot.TryGetByInventory(inventory, out var entry) || !entry.Lootbox ||
            entry.Lootbox.gameObject.activeSelf == visible) return;
        entry.Lootbox.gameObject.SetActive(visible);
        var rpc = new DeadLootVisibilityRpc { SceneIndex = SceneManager.GetActiveScene().buildIndex, LootUid = entry.LootUid, Visible = visible };
        CoopTool.SendRpc(in rpc);
    }
    internal static void ApplyVisibility(RpcContext context, DeadLootVisibilityRpc rpc)
    {
        if (context.IsServer || SceneManager.GetActiveScene().buildIndex != rpc.SceneIndex ||
            !CoopSyncDatabase.Loot.TryGetByLootUid(rpc.LootUid, out var entry) || !entry.Lootbox) return;
        entry.Lootbox.gameObject.SetActive(rpc.Visible);
    }
}
