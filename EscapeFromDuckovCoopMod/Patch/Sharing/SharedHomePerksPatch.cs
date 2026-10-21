using System.Collections;
using Duckov.PerkTrees.Behaviours;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(ModifyCharacterStatsBase), "OnLocked")]
internal static class SharedHomePerkStatCleanupPatch
{
    private static void Postfix(ModifyCharacterStatsBase __instance)
    {
        if (!SharedHome.Applying && !SharedHome.Guest && !SharedHome.Reloading) return;
        // Native OnLocked subtracts these values but retains the records. A later
        // shared-save restore must not subtract the same stat bonuses again.
        ((IList)AccessTools.Field(typeof(ModifyCharacterStatsBase), "records").GetValue(__instance)).Clear();
    }
}
