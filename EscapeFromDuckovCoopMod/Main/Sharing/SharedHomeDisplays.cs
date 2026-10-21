using Duckov.Buildings;
using ItemStatsSystem.Data;
using ItemStatsSystem;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeDisplays
{
    internal static readonly HashSet<Showcase> Displays = new();
    private static readonly Dictionary<Showcase, int> Versions = new();
    private static readonly HashSet<Showcase> Pending = new();
    internal static bool Loading => Pending.Any(d => d);
    internal static void Reload(HashSet<string> changed)
    {
        Displays.RemoveWhere(d => !d);
        Pending.RemoveWhere(d => !d);
        foreach (var display in Versions.Keys.Where(d => !d).ToArray()) Versions.Remove(display);
        foreach (var display in Displays)
        {
            var key = "Item/Showcase_" + display.ID;
            if (changed != null && !changed.Contains(key)) continue;
            var version = Versions.TryGetValue(display, out var old) ? old + 1 : 1;
            Versions[display] = version;
            Pending.Add(display);
            Refresh(display, key, version).Forget();
        }
    }
    private static async UniTaskVoid Refresh(Showcase display, string key, int version)
    {
        Item item = null;
        try
        {
            await UniTask.WaitUntil(() => !display || (bool)AccessTools.Field(typeof(Showcase), "loaded").GetValue(display));
            if (!display || Versions[display] != version) return;
            var tree = SavesSystem.Load<ItemTreeData>(key);
            item = await ItemTreeData.InstantiateAsync(tree);
            if (!display || Versions[display] != version) return;
            var snapshot = item ? ItemTool.MakeSnapshot(item) : new ItemSnapshot
                { TypeId = display.Item.TypeID, Stack = 1, InventoryCapacity = display.Inventory ? display.Inventory.Capacity : 0 };
            SharedHome.Apply(() => ItemTool.ApplySnapshot(display.Item, snapshot));
        }
        finally
        {
            if (item) item.DestroyTree();
            if (!display || Versions[display] == version) Pending.Remove(display);
        }
    }
}
