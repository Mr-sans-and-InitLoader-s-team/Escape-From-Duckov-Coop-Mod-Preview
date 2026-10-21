using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeEquipment
{
    internal static async UniTask LoadInto(ItemTreeData data, Item target, Func<bool> current)
    {
        var root = data?.RootData;
        if (root == null || !target) throw new InvalidOperationException("Shared character equipment is unavailable");
        var entries = data.entries.ToDictionary(e => e.instanceID);
        var incoming = new List<(Item Item, string Slot, int Position)>();
        var attached = false;
        ItemTreeData Subtree(int id)
        {
            var tree = new ItemTreeData { rootInstanceID = id };
            var visited = new HashSet<int>();
            void Add(int next)
            {
                if (!visited.Add(next)) throw new InvalidOperationException("Shared item tree contains a cycle or duplicate");
                if (!entries.TryGetValue(next, out var entry)) throw new InvalidOperationException("Shared item tree is incomplete");
                tree.entries.Add(entry);
                foreach (var slot in entry.slotContents) Add(slot.instanceID);
                foreach (var item in entry.inventory) Add(item.instanceID);
            }
            Add(id);
            return tree;
        }
        try
        {
            foreach (var slot in root.slotContents)
            {
                var item = await ItemTreeData.InstantiateAsync(Subtree(slot.instanceID));
                if (!item) throw new InvalidOperationException("Unable to restore shared equipment");
                incoming.Add((item, slot.slot, -1));
            }
            foreach (var entry in root.inventory)
            {
                if (entry.position < 0 || entry.position > 16383) throw new InvalidOperationException("Invalid shared inventory position");
                var item = await ItemTreeData.InstantiateAsync(Subtree(entry.instanceID));
                if (!item) throw new InvalidOperationException("Unable to restore shared inventory item");
                incoming.Add((item, null, entry.position));
            }
            if (!target || !current()) return;
            foreach (var entry in incoming.Where(e => e.Slot != null))
            {
                var slot = target.Slots?.GetSlot(entry.Slot);
                if (slot == null || !slot.CanPlug(entry.Item)) throw new InvalidOperationException("Shared equipment slot is incompatible: " + entry.Slot);
            }
            SharedHome.Apply(() =>
            {
                var inv = target.Inventory;
                inv.Loading = true;
                try
                {
                    CharacterMainControl.Main?.ChangeHoldItem(null);
                    // Keep the live character root and its native stat providers.
                    // Only the owned equipment and inventory are replaced.
                    foreach (var slot in target.Slots)
                    {
                        var old = slot.Unplug();
                        if (old) old.DestroyTree();
                    }
                    inv.DestroyAllContent();
                    var required = incoming.Where(e => e.Slot == null).Select(e => e.Position + 1).DefaultIfEmpty(0).Max();
                    inv.SetCapacity(Math.Max(inv.Capacity, required));
                    foreach (var entry in incoming)
                    {
                        var success = entry.Slot == null ? inv.AddAt(entry.Item, entry.Position) : target.Slots.GetSlot(entry.Slot).Plug(entry.Item, out _);
                        if (!success) throw new InvalidOperationException("Failed to attach shared equipment");
                    }
                    inv.lockedIndexes.Clear();
                    if (root.inventorySortLocks != null) inv.lockedIndexes.AddRange(root.inventorySortLocks);
                    attached = true;
                }
                finally { inv.Loading = false; }
            });
        }
        finally
        {
            if (!attached)
                foreach (var entry in incoming)
                    if (entry.Item) entry.Item.DestroyTree();
        }
    }
}
