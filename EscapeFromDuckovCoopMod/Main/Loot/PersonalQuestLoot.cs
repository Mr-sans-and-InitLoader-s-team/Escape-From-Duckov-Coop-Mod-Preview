using System.Runtime.CompilerServices;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class PersonalQuestLoot
{
    private static readonly ConditionalWeakTable<Inventory, InteractableLootbox> Boxes = new();

    internal static void Observe(GameObject spawned)
    {
        foreach (var box in spawned.GetComponentsInChildren<InteractableLootbox>(true))
        {
            var inventory = box.Inventory;
            if (inventory) Boxes.GetValue(inventory, _ => box);
        }
    }

    internal static bool IsPersonal(Inventory inventory) => NetService.Instance?.networkStarted == true && !SharedHome.Active &&
        inventory && Boxes.TryGetValue(inventory, out var box) && box;
}
