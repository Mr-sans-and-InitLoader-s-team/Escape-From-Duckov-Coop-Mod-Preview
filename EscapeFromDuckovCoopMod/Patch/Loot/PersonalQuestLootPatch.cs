using System.Collections.Generic;
using System.Reflection;
using Duckov.Quests;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

[HarmonyPatch(typeof(SpawnPrefabForTask), "Spawn")]
internal static class PersonalQuestLootPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(UnityEngine.Object) &&
                method.Name == nameof(UnityEngine.Object.Instantiate) && method.ReturnType == typeof(GameObject))
                instruction.operand = AccessTools.Method(typeof(PersonalQuestLootPatch), nameof(Instantiate));
            yield return instruction;
        }
    }

    private static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        var spawned = UnityEngine.Object.Instantiate(prefab, position, rotation);
        // A task prefab can contain ordinary weapons or consumables as well as Quest-tagged items.
        PersonalQuestLoot.Observe(spawned);
        return spawned;
    }
}
