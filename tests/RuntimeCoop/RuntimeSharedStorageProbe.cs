using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CoopRuntimeTests;

internal static class RuntimeSharedStorageProbe
{
    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        var inventory = PlayerStorage.Inventory;
        var backpack = CharacterMainControl.Main.CharacterItem.Inventory;
        if ((bool?)command["clearBackpack"] == true) backpack.DestroyAllContent();
        if ((bool?)command["fillBackpack"] == true)
        {
            backpack.DestroyAllContent();
            var ammo = await ItemAssetsCollection.InstantiateAsync(140);
            ammo.StackCount = ammo.MaxStackCount - 1;
            backpack.AddItem(ammo);
            while (backpack.GetFirstEmptyPosition() >= 0)
                backpack.AddItem(await ItemAssetsCollection.InstantiateAsync(2));
        }
        if ((bool?)command["clear"] == true) inventory.DestroyAllContent();
        if (command["put"] != null)
        {
            var item = CharacterMainControl.Main.CharacterItem.Inventory.First(i => i && i.TypeID == (int)command["put"]);
            item.Detach();
            ItemUtilities.AddAndMerge(inventory, item);
        }
        if ((bool?)command["sort"] == true)
        {
            var display = UnityEngine.Object.FindObjectsByType<InventoryDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(d => d.Target == inventory);
            AccessTools.Method(typeof(InventoryDisplay), "OnSortButtonClicked").Invoke(display, null);
        }
        if (command["lock"] != null) inventory.LockIndex((int)command["lock"]);
        if (command["drop"] != null)
        {
            var item = inventory.GetItemAt((int)command["drop"]);
            for (var i = 0; i < ((int?)command["repeat"] ?? 1); i++)
                if (item) item.Drop(CharacterMainControl.Main, true);
        }
        if (command["take"] != null) ItemUtilities.SendToPlayerCharacterInventory(inventory.GetItemAt((int)command["take"]));
        if (command["move"] != null)
        {
            var item = inventory.GetItemAt((int)command["move"]);
            item.Detach();
            inventory.AddAt(item, (int)command["to"]);
        }
        if (command["combine"] != null)
            inventory.GetItemAt((int)command["to"]).Combine(inventory.GetItemAt((int)command["combine"]));
        if (command["mergeFromBag"] != null)
            inventory.GetItemAt((int)command["to"]).Combine(CharacterMainControl.Main.CharacterItem.Inventory.First(i => i && i.TypeID == (int)command["mergeFromBag"]));
        if (command["mergeToBag"] != null)
            CharacterMainControl.Main.CharacterItem.Inventory.First(i => i && i.TypeID == (int)command["type"]).Combine(inventory.GetItemAt((int)command["mergeToBag"]));
        if (command["split"] != null)
        {
            var item = await inventory.GetItemAt((int)command["split"]).Split((int)command["count"]);
            if (item) ItemUtilities.SendToPlayerCharacterInventory(item, true);
        }
        if (command["unload"] != null) inventory.GetItemAt((int)command["unload"]).GetComponent<ItemSetting_Gun>().TakeOutAllBullets();
        if (command["use"] != null) CharacterMainControl.Main.UseItem(inventory.GetItemAt((int)command["use"]));
        if (command["uiMove"] != null)
        {
            var entries = UnityEngine.Object.FindObjectsByType<InventoryEntry>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var source = entries.Single(e => e.Master && e.Master.Target == inventory && e.Index == (int)command["uiMove"]);
            var targetInventory = (bool?)command["toBag"] == true ? CharacterMainControl.Main.CharacterItem.Inventory : inventory;
            var target = entries.Single(e => e.Master && e.Master.Target == targetInventory && e.Index == (int)command["to"]);
            target.OnDrop(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerDrag = source.gameObject });
        }
        if (command["gunAmmo"] != null)
        {
            var gun = inventory.GetItemAt((int)command["gunAmmo"]);
            var ammo = await ItemAssetsCollection.InstantiateAsync(140);
            ammo.StackCount = (int)command["count"];
            gun.Inventory.AddItem(ammo);
        }
        var storage = mod.GetType("EscapeFromDuckovCoopMod.SharedHomeStorage");
        var drops = mod.GetType("EscapeFromDuckovCoopMod.CoopSyncDatabase").GetProperty("Drops").GetValue(null);
        return new
        {
            loading = inventory.Loading,
            bagEmpty = backpack.GetFirstEmptyPosition(),
            useRunning = ((CharacterActionBase)AccessTools.Field(typeof(CharacterMainControl), "useItemAction").GetValue(CharacterMainControl.Main)).Running,
            pending = ((IDictionary)AccessTools.Field(storage, "Pending").GetValue(null)).Count,
            escrow = ((IDictionary)AccessTools.Field(storage, "Escrow").GetValue(null)).Count,
            drops = ((IEnumerable)AccessTools.Property(drops.GetType(), "Entries").GetValue(drops)).Cast<object>()
                .Select(e => new { id = AccessTools.Property(e.GetType(), "DropId").GetValue(e), item = (Item)AccessTools.Property(e.GetType(), "Item").GetValue(e) })
                .Where(e => e.item).Select(e => new { e.id, type = e.item.TypeID, count = e.item.StackCount }).ToArray(),
            items = Enumerable.Range(0, inventory.Capacity).Select(i => new { slot = i, item = inventory.GetItemAt(i) })
                .Where(e => e.item).Select(e => new { e.slot, type = e.item.TypeID, count = e.item.StackCount, locked = inventory.IsIndexLocked(e.slot),
                    children = ItemStatsSystem.Data.ItemTreeData.FromItem(e.item).entries.Where(c => c.instanceID != e.item.GetInstanceID()).Select(c => new { type = c.typeID, count = c.StackCount }).ToArray(),
                    nested = e.item.Inventory?.Where(i => i).Select(i => new { type = i.TypeID, count = i.StackCount }).ToArray() }).ToArray(),
            backpack = ItemStatsSystem.Data.ItemTreeData.FromItem(CharacterMainControl.Main.CharacterItem).entries
                .Select(e => new { type = e.typeID, count = e.StackCount }).ToArray()
        };
    }
}
