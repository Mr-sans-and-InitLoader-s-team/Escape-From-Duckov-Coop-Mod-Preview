using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeSafeInventory
{
    internal const string ProfileKey = "safeInventory";
    private const string SaveKey = "Inventory/Inventory_Safe";
    private static Inventory _sharedInventory;
    internal static bool Ready => !LevelConfig.SavePet || PetProxy.PetInventory && !PetProxy.PetInventory.Loading;

    internal static void ClearPersonal(ES3File overlay)
    {
        _sharedInventory = PetProxy.PetInventory;
        overlay.DeleteKey(SaveKey);
    }

    internal static void Track(PetProxy proxy)
    {
        if (SharedHomeSave.OverlayActive) _sharedInventory = proxy.Inventory;
    }

    internal static bool CanSave(PetProxy proxy)
    {
        if (!SharedHomeSave.OverlayActive) return !ReferenceEquals(proxy.Inventory, _sharedInventory);
        Track(proxy);
        return SharedHomeLoadouts.Ready && !proxy.Inventory.Loading;
    }

    internal static void Capture(ES3File profile)
    {
        if (LevelConfig.SavePet && PetProxy.PetInventory)
            profile.Save(ProfileKey, InventoryData.FromInventory(PetProxy.PetInventory));
    }

    internal static async UniTask Restore(ES3File profile, Func<bool> current)
    {
        if (!LevelConfig.SavePet) return;
        await UniTask.WaitUntil(() => !current() || Ready);
        if (!current()) return;
        var inventory = PetProxy.PetInventory;
        var data = profile.KeyExists(ProfileKey) ? profile.Load<InventoryData>(ProfileKey) : new InventoryData();
        var incoming = new List<(Item Item, int Position)>();
        var attached = false;
        try
        {
            foreach (var entry in data.entries)
            {
                if (entry.inventoryPosition < 0 || entry.inventoryPosition > 16383)
                    throw new InvalidOperationException("Invalid shared safe inventory position");
                var item = await ItemTreeData.InstantiateAsync(entry.itemTreeData);
                if (!item) throw new InvalidOperationException("Unable to restore shared safe item");
                incoming.Add((item, entry.inventoryPosition));
            }
            if (!current() || !inventory) return;
            inventory.Loading = true;
            try
            {
                inventory.DestroyAllContent();
                foreach (var entry in incoming)
                    if (!inventory.AddAt(entry.Item, entry.Position))
                        throw new InvalidOperationException("Failed to attach shared safe item");
                inventory.lockedIndexes.Clear();
                if (data.lockedIndexes != null) inventory.lockedIndexes.AddRange(data.lockedIndexes);
                attached = true;
                inventory.Save("Inventory_Safe");
                CoopSessionLog.Write("SharedHome", $"Safe restored: capacity={inventory.Capacity}, items={string.Join(",", incoming.Select(e => $"{e.Position}:{e.Item.TypeID}"))}");
            }
            finally { inventory.Loading = false; }
        }
        finally
        {
            if (!attached)
                foreach (var entry in incoming)
                    if (entry.Item && entry.Item.InInventory != inventory) entry.Item.DestroyTree();
        }
    }

}
