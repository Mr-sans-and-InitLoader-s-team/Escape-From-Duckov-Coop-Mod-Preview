// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 统一管理世界掉落相关的可调参数，仅限主机生效。
/// </summary>
public sealed class CoopLootSettings : MonoBehaviour
{
    public static CoopLootSettings Instance;

    public LootTuningSettings Settings { get; private set; } = LootTuningSettings.Default();

    public static LootTuningSettings Active => Instance?.Settings ?? LootTuningSettings.Default();

    public static bool IsHost => ModBehaviourF.Instance != null && ModBehaviourF.Instance.IsServer;

    public void Init()
    {
        Instance = this;
    }

    public void Apply(LootTuningSettings next)
    {
        Settings = (next ?? LootTuningSettings.Default()).CloneWithBounds();
    }
}

[Serializable]
public sealed class LootTuningSettings
{
    public float SpawnChanceMultiplier = 1f;
    public float ItemCountMultiplier = 1f;
    public float GlobalWeightMultiplier = 1f;
    public float QualityBias = 0f;

    public bool IsDefault =>
        Mathf.Approximately(SpawnChanceMultiplier, 1f) &&
        Mathf.Approximately(ItemCountMultiplier, 1f) &&
        Mathf.Approximately(GlobalWeightMultiplier, 1f) &&
        Mathf.Approximately(QualityBias, 0f);

    public LootTuningSettings Clone() => (LootTuningSettings)MemberwiseClone();

    public LootTuningSettings CloneWithBounds()
    {
        var clone = Clone();
        clone.SpawnChanceMultiplier = Mathf.Clamp(clone.SpawnChanceMultiplier, 0f, 5f);
        clone.ItemCountMultiplier = Mathf.Clamp(clone.ItemCountMultiplier, 0.1f, 50f);
        clone.GlobalWeightMultiplier = Mathf.Clamp(clone.GlobalWeightMultiplier, 0f, 50f);
        clone.QualityBias = Mathf.Clamp(clone.QualityBias, -1f, 50f);
        return clone;
    }

    public static LootTuningSettings Default() => new();
}

internal static class LootSettingsRuntime
{
    internal static bool ShouldApply => CoopLootSettings.IsHost;
    internal static bool ShouldApplyTuning => ShouldApply && !CoopLootSettings.Active.IsDefault;

    internal static float AdjustSpawnChance(float original)
    {
        if (!ShouldApplyTuning) return original;
        var tuned = original * CoopLootSettings.Active.SpawnChanceMultiplier;
        return Mathf.Clamp01(tuned);
    }

    internal static Vector2Int AdjustRandomCount(Vector2Int original)
    {
        if (!ShouldApplyTuning) return original;

        var mult = CoopLootSettings.Active.ItemCountMultiplier;
        var min = Mathf.Max(1, Mathf.CeilToInt(original.x * mult));
        var max = Mathf.Max(min, Mathf.CeilToInt(original.y * mult));
        return new Vector2Int(min, max);
    }

    internal static float AdjustWeight<T>(T value, float weight)
    {
        if (!ShouldApplyTuning) return weight;

        var tuned = weight * CoopLootSettings.Active.GlobalWeightMultiplier;

        if (value is int quality)
        {
            var bias = 1f + quality * CoopLootSettings.Active.QualityBias;
            tuned *= Mathf.Max(0f, bias);
        }

        return tuned;
    }
}
