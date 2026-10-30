using Duckov.PerkTrees;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomePerkUpgrade
{
    internal static UniTask<bool> Request(Perk perk) => SharedHomeInventoryTransaction.Request("perkBegin",
        new { tree = perk.Master.ID, perk = perk.name });

    internal static UniTask<bool> Execute(JObject args, Item character, Inventory safe, Func<bool> current)
    {
        if (!current()) return UniTask.FromResult(false);
        var tree = PerkTreeManager.GetPerkTree((string)args["tree"]);
        var perk = tree?.Perks.FirstOrDefault(p => p && p.name == (string)args["perk"]);
        if (!perk || perk.Unlocked || perk.Unlocking) return UniTask.FromResult(false);
        return UniTask.FromResult(SharedHomeInventoryTransaction.WithInventories(character, safe,
            () => perk.SubmitItemsAndBeginUnlocking()));
    }
}
