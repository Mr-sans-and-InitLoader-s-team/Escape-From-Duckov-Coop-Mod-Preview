using Duckov;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeStorageStoreAll
{
    internal static bool Pending { get; private set; }

    internal static async UniTaskVoid Request()
    {
        if (Pending || SharedHomeInventoryTransaction.Busy || !SharedHomeStorage.CanTransfer ||
            !SharedHomeLoadouts.Ready || !SharedHomeSafeInventory.Ready) return;
        Pending = true;
        try
        {
            var first = CharacterMainControl.Main.CharacterItem.Inventory.GetItemAt(0);
            var locked = CharacterMainControl.Main.CharacterItem.Inventory.IsIndexLocked(0);
            var storage = PlayerStorage.Inventory;
            var sound = first && !locked && (storage.GetFirstEmptyPosition() >= 0 || first.Stackable &&
                storage.Where(i => i && i.Stackable && i.TypeID == first.TypeID)
                    .Sum(i => Math.Max(0, i.MaxStackCount - i.StackCount)) >= first.StackCount);
            var request = SharedHomeInventoryTransaction.Request("storageStoreAll", new { });
            if (sound) AudioManager.PlayPutItemSFX(first);
            await request;
        }
        finally { Pending = false; }
    }

    internal static bool Execute(Item character)
    {
        var storage = PlayerStorage.Inventory;
        if (!character || !storage || PlayerStorage.Loading || storage.Loading) return false;
        var source = character.Inventory;
        var last = source.GetLastItemPosition();
        for (var i = 0; i <= last; i++)
        {
            if (source.IsIndexLocked(i)) continue;
            var item = source.GetItemAt(i);
            if (item && !ItemUtilities.AddAndMerge(storage, item)) break;
        }
        return true;
    }
}
