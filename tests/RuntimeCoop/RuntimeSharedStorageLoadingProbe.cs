using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Newtonsoft.Json.Linq;

namespace CoopRuntimeTests;

internal static class RuntimeSharedStorageLoadingProbe
{
    private static bool _patched, _hideCharacter;
    private static int _sent;
    private static bool MainPrefix(ref CharacterMainControl __result)
    {
        if (!_hideCharacter) return true;
        __result = null;
        return false;
    }
    private static void Sending() => _sent++;

    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        var storage = mod.GetType("EscapeFromDuckovCoopMod.SharedHomeStorage");
        var home = mod.GetType("EscapeFromDuckovCoopMod.SharedHome");
        if (!_patched)
        {
            var harmony = new Harmony("local.coop.runtime-tests.storage-loading");
            harmony.Patch(AccessTools.PropertyGetter(typeof(CharacterMainControl), "Main"),
                prefix: new HarmonyMethod(typeof(RuntimeSharedStorageLoadingProbe), nameof(MainPrefix)));
            harmony.Patch(AccessTools.Method(storage, "SendBuffer"),
                prefix: new HarmonyMethod(typeof(RuntimeSharedStorageLoadingProbe), nameof(Sending)));
            _patched = true;
        }
        var instance = PlayerStorage.Instance;
        var inventory = PlayerStorage.Inventory;
        var before = InventoryData.FromInventory(inventory);
        var sent = _sent;
        string error = null;
        bool stuck, inventoryStuck;
        try
        {
            if ((bool?)command["inventoryOnly"] == true)
            {
                inventory.Loading = true;
                inventory.DestroyAllContent();
                await InventoryData.LoadIntoInventory(before, inventory);
                inventory.Loading = false;
            }
            else
            {
                AccessTools.Method(home, "Apply").Invoke(null, new object[] { (Action)(() =>
                    AccessTools.Method(typeof(PlayerStorage), "Save").Invoke(instance, null)) });
                _hideCharacter = (bool?)command["missingCharacter"] == true;
                await (UniTask)AccessTools.Method(typeof(PlayerStorage), "Load").Invoke(instance, null);
            }
        }
        catch (Exception e) { error = e.ToString(); }
        finally
        {
            stuck = PlayerStorage.Loading;
            inventoryStuck = inventory.Loading;
            _hideCharacter = false;
            // Only recover failed baseline fixtures, after recording the native loading flags.
            if (error != null)
            {
                AccessTools.PropertySetter(typeof(PlayerStorage), "Loading").Invoke(null, new object[] { false });
                inventory.Loading = false;
            }
        }
        return new
        {
            error, loadingStuck = stuck, inventoryLoadingStuck = inventoryStuck, sent = _sent - sent,
            before = before.entries.Select(e => new { slot = e.inventoryPosition, type = e.itemTreeData.RootTypeID, count = e.itemTreeData.RootData.StackCount }).ToArray(),
            after = Enumerable.Range(0, inventory.Capacity).Select(i => new { slot = i, item = inventory.GetItemAt(i) })
                .Where(e => e.item).Select(e => new { e.slot, type = e.item.TypeID, count = e.item.StackCount }).ToArray()
        };
    }
}
