using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal sealed class LootPutReceipts
{
    private readonly Dictionary<Inventory, Dictionary<int, int>> _known = new();

    internal void Clear() => _known.Clear();

    internal void Remember(Inventory inventory, int slot, int typeId)
    {
        if (!inventory || slot < 0 || typeId <= 0) return;
        if (!_known.TryGetValue(inventory, out var slots))
            _known[inventory] = slots = new();
        slots[slot] = typeId;
    }

    internal void Apply(Inventory inventory, int? changedSlot)
    {
        if (!inventory || !_known.TryGetValue(inventory, out var slots)) return;
        foreach (var slot in changedSlot.HasValue ? new[] { changedSlot.Value } : slots.Keys.ToArray())
        {
            if (!slots.TryGetValue(slot, out var typeId)) continue;
            var item = inventory.GetItemAt(slot);
            if (!item || item.TypeID != typeId) { slots.Remove(slot); continue; }
            item.Inspected = true;
            item.Inspecting = false;
        }
        if (slots.Count == 0) _known.Remove(inventory);
    }
}
