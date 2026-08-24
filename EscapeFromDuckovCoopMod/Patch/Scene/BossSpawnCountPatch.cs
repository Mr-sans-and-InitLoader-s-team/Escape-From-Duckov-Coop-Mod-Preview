using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

internal static class BossSpawnCountPolicy
{
    public static void RestoreBaseCount(
        string spawnerName,
        IReadOnlyList<CharacterRandomPresetInfo> presets,
        Vector2Int originalRange,
        ref Vector2Int currentRange)
    {
        var svc = NetService.Instance;
        if (svc == null || !svc.networkStarted || !svc.IsServer)
            return;

        if (!ContainsBoss(presets) || !DifficultyManager.TryGetBaseEnemySpawnFactor(out var baseFactor))
            return;

        var baseRange = new Vector2Int(
            Mathf.RoundToInt(originalRange.x * baseFactor),
            Mathf.RoundToInt(originalRange.y * baseFactor));

        if (currentRange == baseRange)
            return;

        Debug.Log($"[DifficultyManager] Preserved boss spawn count for {spawnerName}: {currentRange} -> {baseRange}");
        currentRange = baseRange;
    }

    private static bool ContainsBoss(IReadOnlyList<CharacterRandomPresetInfo> presets)
    {
        if (presets == null)
            return false;

        for (var i = 0; i < presets.Count; i++)
        {
            var preset = presets[i].randomPreset;
            if (preset != null && preset.isBoss)
                return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(RandomCharacterSpawner), nameof(RandomCharacterSpawner.Init))]
internal static class RandomCharacterSpawnerBossCountPatch
{
    private static void Prefix(RandomCharacterSpawner __instance, out Vector2Int __state)
    {
        __state = __instance.spawnCountRange;
    }

    private static void Postfix(RandomCharacterSpawner __instance, Vector2Int __state)
    {
        BossSpawnCountPolicy.RestoreBaseCount(
            __instance.name,
            __instance.randomPresetInfos,
            __state,
            ref __instance.spawnCountRange);
    }
}

[HarmonyPatch(typeof(WaveCharacterSpawner), nameof(WaveCharacterSpawner.Init))]
internal static class WaveCharacterSpawnerBossCountPatch
{
    private static void Prefix(WaveCharacterSpawner __instance, out Vector2Int __state)
    {
        __state = __instance.spawnCountRange;
    }

    private static void Postfix(WaveCharacterSpawner __instance, Vector2Int __state)
    {
        BossSpawnCountPolicy.RestoreBaseCount(
            __instance.name,
            __instance.randomPresetInfos,
            __state,
            ref __instance.spawnCountRange);
    }
}
