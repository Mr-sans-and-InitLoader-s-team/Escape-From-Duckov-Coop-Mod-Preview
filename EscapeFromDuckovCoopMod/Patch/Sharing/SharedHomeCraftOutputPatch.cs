using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class SharedHomeCraftOutputPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(CraftingManager), "Craft", new[] { typeof(CraftingFormula) })
        .GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType.GetMethod("MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(SharedHomeCraftOutput.NativeReturn))
            {
                var replacement = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SharedHomeCraftOutput), "Return"));
                replacement.MoveLabelsFrom(instruction);
                replacement.MoveBlocksFrom(instruction);
                yield return replacement;
            }
            else yield return instruction;
        }
    }
}
