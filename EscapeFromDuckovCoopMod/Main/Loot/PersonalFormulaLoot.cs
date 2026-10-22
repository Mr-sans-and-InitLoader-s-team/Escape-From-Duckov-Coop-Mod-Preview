using System.Runtime.CompilerServices;
using Duckov.Quests.Conditions;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class PersonalFormulaLoot
{
    private static readonly ConditionalWeakTable<Inventory, InteractableLootbox> Boxes = new();

    internal static void Observe(InteractableLootbox box, Inventory inventory)
    {
        if (box && inventory && box.GetComponent<RequireFormulaUnlocked>())
            Boxes.GetValue(inventory, _ => box);
    }

    internal static bool IsFormulaBox(Inventory inventory) => inventory &&
        Boxes.TryGetValue(inventory, out var box) && box;

    internal static bool IsPersonal(Inventory inventory) => NetService.Instance?.networkStarted == true && !SharedHome.Active &&
        IsFormulaBox(inventory);
}
