using Duckov;
using ItemStatsSystem;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

[HarmonyPatch(typeof(DeadBodyManager), "ShouldSpawnDeadBody")]
internal static class CorpseRecoveryEligibilityPatch
{
    private static void Prefix(DeadBodyManager.DeathInfo info, out bool __state)
    {
        __state = info?.touched == true;
        if (NetService.Instance?.networkStarted == true && CorpseRecovery.HasRemaining(info)) info.touched = false;
    }
    private static void Finalizer(DeadBodyManager.DeathInfo info, bool __state)
    {
        if (info != null) info.touched = __state;
    }
}

[HarmonyPatch(typeof(DeadBodyManager), "SpawnDeadBody")]
internal static class CorpseRecoverySpawnPatch
{
    private static bool Prefix(DeadBodyManager.DeathInfo info, ref UniTask __result)
    {
        if (NetService.Instance?.networkStarted != true || !CorpseRecovery.TryRestore(info, out var task)) return true;
        __result = task;
        return false;
    }
}

[HarmonyPatch]
internal static class CorpseRecoveryNativeSpawnPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(DeadBodyManager), "SpawnDeadBody")
        .GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType.GetMethod("MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var info = __originalMethod.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(DeadBodyManager.DeathInfo));
        var register = AccessTools.Method(typeof(CorpseRecovery), nameof(CorpseRecovery.RegisterNativeBody));
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(InteractableLootbox) &&
                method.Name == nameof(InteractableLootbox.CreateFromItem))
            {
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, info);
                yield return new CodeInstruction(OpCodes.Call, register);
            }
        }
    }
}

[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.NotifySaveBeforeLoadScene))]
internal static class CorpseRecoveryFlushPatch
{
    private static void Prefix() => CorpseRecovery.Flush();
}

[HarmonyPatch(typeof(Item), "NotifyItemTreeChanged")]
internal static class CorpseRecoveryItemTreePatch
{
    private static void Postfix(Item __instance)
    {
        CorpseRecovery.QueueItemTreeSnapshot(__instance);
    }
}
