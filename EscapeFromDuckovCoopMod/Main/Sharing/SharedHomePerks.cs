using System.Collections;
using Duckov.PerkTrees;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomePerks
{
    private static readonly Type LevelBuffType = typeof(Perk).Assembly.GetType("Duckov.PerkTrees.Behaviours.AddBuffOnLevelInitialized");
    internal static void Reload(PerkTree tree)
    {
        if (!ES3File.cachedFiles.TryGetValue(SavesSystem.CurrentFilePath, out var cache)) return;
        var key = "PerkTree_" + tree.ID;
        if (!cache.KeyExists(key)) return;
        var data = cache.Load(key);
        var entries = ((IEnumerable)AccessTools.Field(data.GetType(), "entries").GetValue(data)).Cast<object>()
            .ToDictionary(e => (string)AccessTools.Field(e.GetType(), "perkName").GetValue(e));
        foreach (var perk in tree.Perks.Where(p => p))
        {
            if (!entries.TryGetValue(perk.name, out var entry)) continue;
            var type = entry.GetType();
            var unlocked = (bool)AccessTools.Field(type, "unlocked").GetValue(entry);
            var unlocking = (bool)AccessTools.Field(type, "unlocking").GetValue(entry);
            var begin = (long)AccessTools.Field(type, "unlockingBeginTime").GetValue(entry);
            var beginField = AccessTools.Field(typeof(Perk), "unlockingBeginTimeRaw");
            var changed = perk.Unlocked != unlocked || perk.Unlocking != unlocking || (long)beginField.GetValue(perk) != begin;
            var unlockChanged = perk.Unlocked != unlocked;
            AccessTools.Field(typeof(Perk), "unlocking").SetValue(perk, unlocking);
            beginField.SetValue(perk, begin);
            if (unlockChanged || changed && !unlocked)
            {
                if (!unlocked && perk.EnabledInCurrentLevel && LevelBuffType != null)
                    foreach (var effect in perk.GetComponents(LevelBuffType))
                        if (AccessTools.Field(LevelBuffType, "prefab").GetValue(effect) is Duckov.Buffs.Buff prefab && prefab)
                            CharacterMainControl.Main?.RemoveBuff(prefab.ID, true);
                AccessTools.PropertySetter(typeof(Perk), nameof(Perk.Unlocked)).Invoke(perk, new object[] { unlocked });
            }
            if (changed) AccessTools.Method(typeof(PerkTree), "NotifyChildStateChanged").Invoke(tree, new object[] { perk });
        }
    }
}
