using System.Reflection;
using Duckov.Economy;
using Duckov.Endowment;
using Duckov.NoteIndexs;
using Duckov.Scenes;
using Duckov.MasterKeys;
using Duckov.Rules.UI;
using Duckov.Quests.Conditions;
using Duckov.Utilities;
using Duckvo.Beacons;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeUnlocks
{
    internal static readonly Dictionary<string, MethodInfo> Methods = new()
    {
        ["visited"] = AccessTools.Method(typeof(MultiSceneCore), nameof(MultiSceneCore.SetVisited)),
        ["rage"] = AccessTools.Method(typeof(DifficultySelection), nameof(DifficultySelection.UnlockRage)),
        ["fished"] = AccessTools.Method(typeof(RequireHasFished), nameof(RequireHasFished.SetHasFished)),
        ["key"] = AccessTools.Method(typeof(MasterKeysManager), "Activate"),
        ["formula"] = AccessTools.Method(typeof(CraftingManager), nameof(CraftingManager.UnlockFormula)),
        ["beacon"] = AccessTools.Method(typeof(BeaconManager), nameof(BeaconManager.UnlockBeacon)),
        ["shop"] = AccessTools.Method(typeof(EconomyManager), nameof(EconomyManager.Unlock)),
        ["shopConfirm"] = AccessTools.Method(typeof(EconomyManager), nameof(EconomyManager.ConfirmUnlock)),
        ["note"] = AccessTools.Method(typeof(NoteIndex), nameof(NoteIndex.SetNoteUnlocked)),
        ["noteRead"] = AccessTools.Method(typeof(NoteIndex), nameof(NoteIndex.SetNoteRead)),
        ["endowment"] = AccessTools.Method(typeof(EndowmentManager), nameof(EndowmentManager.UnlockEndowment)),
        ["worldBool"] = AccessTools.Method(typeof(CommonVariables), nameof(CommonVariables.SetBool)),
        ["worldInt"] = AccessTools.Method(typeof(CommonVariables), nameof(CommonVariables.SetInt)),
        ["worldFloat"] = AccessTools.Method(typeof(CommonVariables), nameof(CommonVariables.SetFloat)),
        ["worldString"] = AccessTools.Method(typeof(CommonVariables), nameof(CommonVariables.SetString))
    };
    internal static bool Forward(MethodBase method, object[] args)
    {
        if (method == Methods["shop"] && (SharedHome.Applying || SharedHome.Guest && !SharedHomeLoadouts.Ready)) args[2] = false;
        if (!SharedHome.Guest || SharedHome.Applying) return true;
        var entry = Methods.First(p => p.Value == method);
        SharedHome.Request("unlock", new { kind = entry.Key, values = args });
        return false;
    }
    internal static void Execute(JObject args)
    {
        if (!Methods.TryGetValue((string)args["kind"], out var method)) return;
        var values = (JArray)args["values"];
        var parameters = method.GetParameters();
        if (values == null || values.Count != parameters.Length) return;
        var converted = parameters.Select((p, i) => values[i].ToObject(p.ParameterType)).ToArray();
        method.Invoke(null, converted);
        SharedHome.MarkDirty();
    }
}
