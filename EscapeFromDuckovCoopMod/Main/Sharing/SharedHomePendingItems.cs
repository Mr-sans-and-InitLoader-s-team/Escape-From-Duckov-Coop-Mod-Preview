using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal sealed class SharedHomePendingItems
{
    private readonly Item _character;
    private readonly Inventory _safe;
    private readonly HashSet<Item> _known;
    private readonly Dictionary<Item, int> _stacks;
    private readonly Dictionary<int, int> _counts;
    private Dictionary<int, int> _gains;

    internal SharedHomePendingItems(Item character, Inventory safe)
    {
        _character = character;
        _safe = safe;
        _known = new HashSet<Item>(character.GetAllChildren()) { character };
        if (safe)
            foreach (var item in safe.Where(i => i))
            {
                _known.Add(item);
                _known.UnionWith(item.GetAllChildren());
            }
        _stacks = _known.Where(i => i && i.Stackable).ToDictionary(i => i, i => i.StackCount);
        _counts = _stacks.GroupBy(e => e.Key.TypeID).ToDictionary(g => g.Key, g => g.Sum(e => e.Value));
    }

    internal List<Item> DetachAdded(Item character) => ReferenceEquals(character, _character)
        ? Detach(character.Inventory.Concat(character.Slots.Select(s => s.Content))) : new();

    internal List<Item> DetachAdded(Inventory inventory) => ReferenceEquals(inventory, _safe) ? Detach(inventory) : new();

    private List<Item> Detach(IEnumerable<Item> roots)
    {
        if (_gains == null)
        {
            var owned = _character.GetAllChildren().Concat(_safe ? _safe.Where(i => i).SelectMany(i => i.GetAllChildren().Prepend(i)) : Enumerable.Empty<Item>()).Distinct();
            var counts = owned.Where(i => i && _stacks.ContainsKey(i)).GroupBy(i => i.TypeID).ToDictionary(g => g.Key, g => g.Sum(i => i.StackCount));
            _gains = _counts.ToDictionary(e => e.Key, e => Math.Max(0, counts.GetValueOrDefault(e.Key) - e.Value));
        }
        var added = new List<Item>();
        var moved = new List<Item>();
        Item[] Children(Item item) => (item.Inventory ? item.Inventory : Enumerable.Empty<Item>())
            .Concat(item.Slots?.Select(s => s?.Content) ?? Enumerable.Empty<Item>()).Where(i => i).ToArray();
        void RemoveKnown(Item item)
        {
            foreach (var child in Children(item))
                if (_known.Contains(child)) { Visit(child); moved.Add(child); }
                else RemoveKnown(child);
        }
        void Visit(Item item)
        {
            if (!item) return;
            if (!_known.Contains(item))
            {
                RemoveKnown(item);
                added.Add(item);
                return;
            }
            if (_stacks.TryGetValue(item, out var before) && item.StackCount > before && _gains[item.TypeID] > 0)
            {
                var snapshot = ItemTool.MakeSnapshot(item);
                snapshot.Stack = Math.Min(item.StackCount - before, _gains[item.TypeID]);
                var extra = ItemTool.BuildItemFromSnapshot(snapshot);
                if (!extra) throw new InvalidOperationException("Unable to preserve received stack items");
                extra.StackCount = snapshot.Stack;
                _gains[item.TypeID] -= snapshot.Stack;
                added.Add(extra);
            }
            foreach (var child in Children(item)) Visit(child);
        }
        foreach (var item in roots.Where(i => i).ToArray()) Visit(item);
        foreach (var item in added) item.Detach();
        foreach (var item in moved) { item.Detach(); item.DestroyTree(); }
        return added;
    }

    internal static void Restore(IEnumerable<Item> items, Inventory preferred)
    {
        foreach (var item in items)
        {
            if (!item || ItemUtilities.AddAndMerge(preferred, item)) continue;
            if (!item || ItemUtilities.SendToPlayerCharacterInventory(item)) continue;
            item.Drop(CharacterMainControl.Main, true);
        }
    }
}
