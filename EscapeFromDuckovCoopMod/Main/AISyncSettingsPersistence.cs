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
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// Centralizes persistence logic for AI sync tuning and general settings.
/// </summary>
public static class AISyncSettingsPersistence
{
    private const string ConfigFolderName = "Config";
    private const string ConfigFileName = "AISyncSettings.json";

    public static AISyncSavedSettings Load()
    {
        try
        {
            var path = GetConfigFilePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            var payload = JsonConvert.DeserializeObject<AISyncSavedSettings>(json);
            return payload;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AISyncSettingsPersistence] Failed to load saved settings: {ex.Message}");
            return null;
        }
    }

    public static AISyncSavedSettings LoadAndApply(CoopAISettings aiTarget, CoopLootSettings lootTarget = null)
    {
        var payload = Load();
        ApplySavedSettings(aiTarget, lootTarget, payload);
        return payload;
    }

    public static void Save(
        AISyncTuningSettings tuning,
        CoopGeneralSettings general,
        LootTuningSettings loot,
        DifficultyLevel selectedDifficulty,
        DifficultyCustomSettings customDifficulty)
    {
        try
        {
            var payload = AISyncSavedSettings.WithDefaults(
                tuning?.CloneWithBounds(),
                general?.CloneWithBounds(),
                loot?.CloneWithBounds(),
                selectedDifficulty,
                customDifficulty);

            var path = EnsureConfigFilePath();
            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            File.WriteAllText(path, json);

            Debug.Log($"[AISyncSettingsPersistence] Saved global settings to: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AISyncSettingsPersistence] Failed to save global settings: {ex.Message}");
        }
    }

    public static void ApplySavedSettings(CoopAISettings aiTarget, CoopLootSettings lootTarget, AISyncSavedSettings payload)
    {
        if (payload == null)
        {
            return;
        }

        if (payload.AI != null && aiTarget != null)
        {
            aiTarget.Apply(payload.AI.CloneWithBounds());
        }

        if (payload.General != null && aiTarget != null)
        {
            aiTarget.ApplyGeneral(payload.General.CloneWithBounds());
        }

        if (payload.Loot != null && lootTarget != null)
        {
            lootTarget.Apply(payload.Loot.CloneWithBounds());
        }

        if (payload.Difficulty != null)
        {
            if (payload.Difficulty.Custom != null)
            {
                DifficultyManager.SetCustomSettings(payload.Difficulty.Custom.CloneAndClamp());
            }

            DifficultyManager.SetDifficulty(payload.Difficulty.Selected);
        }
    }

    private static string EnsureConfigFilePath()
    {
        var directory = GetConfigDirectory();
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return Path.Combine(directory, ConfigFileName);
    }

    private static string GetConfigFilePath()
    {
        var directory = GetConfigDirectory();
        return Path.Combine(directory, ConfigFileName);
    }

    private static string GetConfigDirectory()
    {
        var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        return Path.Combine(modDir, ConfigFolderName);
    }
}

[Serializable]
public sealed class AISyncSavedSettings
{
    [SerializeField] public AISyncTuningSettings AI;
    [SerializeField] public CoopGeneralSettings General;
    [SerializeField] public LootTuningSettings Loot;
    [SerializeField] public DifficultySavedSettings Difficulty;

    public static AISyncSavedSettings WithDefaults(
        AISyncTuningSettings tuning,
        CoopGeneralSettings general,
        LootTuningSettings loot,
        DifficultyLevel selectedDifficulty,
        DifficultyCustomSettings customDifficulty)
    {
        return new AISyncSavedSettings
        {
            AI = tuning ?? AISyncTuningSettings.Default(),
            General = general ?? CoopGeneralSettings.Default(),
            Loot = loot ?? LootTuningSettings.Default(),
            Difficulty = DifficultySavedSettings.WithDefaults(selectedDifficulty, customDifficulty)
        };
    }
}

[Serializable]
public sealed class DifficultySavedSettings
{
    [SerializeField] public DifficultyLevel Selected;
    [SerializeField] public DifficultyCustomSettings Custom;

    public static DifficultySavedSettings WithDefaults(DifficultyLevel selected, DifficultyCustomSettings custom)
    {
        return new DifficultySavedSettings
        {
            Selected = selected,
            Custom = (custom ?? DifficultyManager.GetCustomSettings()).CloneAndClamp()
        };
    }
}
