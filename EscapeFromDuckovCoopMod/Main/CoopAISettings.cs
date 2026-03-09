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
/// 联机 AI 参数调整的集中入口，提供默认值以及运行时修改入口。
/// </summary>
public sealed class CoopAISettings : MonoBehaviour
{
    public static CoopAISettings Instance;

    public AISyncTuningSettings Settings { get; private set; } = AISyncTuningSettings.Default();
    public CoopGeneralSettings GeneralSettings { get; private set; } = CoopGeneralSettings.Default();

    public void Init()
    {
        Instance = this;
        AISyncSettingsPersistence.LoadAndApply(this, CoopLootSettings.Instance);
    }

    public static AISyncTuningSettings Active => Instance?.Settings ?? AISyncTuningSettings.Default();
    public static CoopGeneralSettings ActiveGeneral => Instance?.GeneralSettings ?? CoopGeneralSettings.Default();

    public void Apply(AISyncTuningSettings next)
    {
        Settings = next.CloneWithBounds();
        COOPManager.AI?.OnSettingsChanged();
    }

    public void ApplyGeneral(CoopGeneralSettings next)
    {
        GeneralSettings = next.CloneWithBounds();

        var net = NetService.Instance;
        if (net != null)
        {
            net.broadcastInterval = GeneralSettings.BroadcastInterval;
            net.syncInterval = GeneralSettings.SyncInterval;
        }

        COOPManager.FriendlyFire?.OnGeneralSettingsApplied(GeneralSettings);
    }
}

[Serializable]
public sealed class CoopGeneralSettings
{
    public float BroadcastInterval = 5f;
    public float SyncInterval = 0.015f;
    public float ProjectileSyncMaxDistance = 120f;
    public bool TeleporterSpawnTogether = false;
    public bool FriendlyFirePlayers = false;

    public CoopGeneralSettings Clone() => (CoopGeneralSettings)MemberwiseClone();

    public CoopGeneralSettings CloneWithBounds()
    {
        var clone = Clone();
        clone.BroadcastInterval = Mathf.Clamp(clone.BroadcastInterval, 1f, 30f);
        clone.SyncInterval = Mathf.Clamp(clone.SyncInterval, 0.01f, 0.1f);
        clone.ProjectileSyncMaxDistance = Mathf.Clamp(
            clone.ProjectileSyncMaxDistance <= 0f ? 120f : clone.ProjectileSyncMaxDistance,
            0f,
            500f);
        clone.TeleporterSpawnTogether = TeleporterSpawnTogether;
        clone.FriendlyFirePlayers = FriendlyFirePlayers;
        return clone;
    }

    public static CoopGeneralSettings Default() => new();
}

[Serializable]
public sealed class AISyncTuningSettings
{
    public float ActivationRadius = 260f;
    public float DeactivationRadius = 280f;
    public float ActivationRetryInterval = 0.75f;
    public float StateBroadcastInterval = 0.1f;
    public float IdleStateRecordInterval = 0.5f;
    public float HealthBroadcastInterval = 0.075f;
    public float MinPositionDelta = 0.25f;
    public float MinRotationDelta = 2f;
    public float VelocityLerp = 12f;
    public float SnapshotRefreshInterval = 15f;
    public float SnapshotRequestTimeout = 3f;
    public float SnapshotRecoveryCooldown = 2.5f;
    public int SnapshotChunkSize = 48;
    public int MaxStoredBuffs = 32;
    public int MaxSnapshotAppliesPerFrame = 12;
    public int MaxStateUpdatesPerFrame = 24;
    public int MaxClientEntryChecksPerFrame = 128;
    public int MaxPendingSnapshotQueue = 2048;
    public int MaxPendingStateQueue = 1024;
    public int SnapshotDropResyncThreshold = 48;
    public int StateDropResyncThreshold = 96;
    public float ServerControllerRescanInterval = 10f;
    public float ServerSnapshotBroadcastInterval = 12f;
    public float ServerSnapshotRetryInterval = 3f;

    public AISyncTuningSettings Clone() => (AISyncTuningSettings)MemberwiseClone();

    public AISyncTuningSettings CloneWithBounds()
    {
        var clone = Clone();
        clone.ActivationRadius = Mathf.Clamp(clone.ActivationRadius, 10f, 400f);
        clone.DeactivationRadius = Mathf.Clamp(clone.DeactivationRadius, clone.ActivationRadius + 1f, 450f);
        clone.ActivationRetryInterval = Mathf.Clamp(clone.ActivationRetryInterval, 0.1f, 5f);
        clone.StateBroadcastInterval = Mathf.Clamp(clone.StateBroadcastInterval, 0.02f, 1f);
        clone.IdleStateRecordInterval = Mathf.Clamp(clone.IdleStateRecordInterval, 0.05f, 2f);
        clone.HealthBroadcastInterval = Mathf.Clamp(clone.HealthBroadcastInterval, 0.02f, 1f);
        clone.MinPositionDelta = Mathf.Clamp(clone.MinPositionDelta, 0.05f, 5f);
        clone.MinRotationDelta = Mathf.Clamp(clone.MinRotationDelta, 0.5f, 30f);
        clone.VelocityLerp = Mathf.Clamp(clone.VelocityLerp, 1f, 30f);
        clone.SnapshotRefreshInterval = Mathf.Clamp(clone.SnapshotRefreshInterval, 1f, 60f);
        clone.SnapshotRequestTimeout = Mathf.Clamp(clone.SnapshotRequestTimeout, 0.5f, 10f);
        clone.SnapshotRecoveryCooldown = Mathf.Clamp(clone.SnapshotRecoveryCooldown, 0.25f, 10f);
        clone.SnapshotChunkSize = Mathf.Clamp(clone.SnapshotChunkSize, 12, 256);
        clone.MaxStoredBuffs = Mathf.Clamp(clone.MaxStoredBuffs, 8, 256);
        clone.MaxSnapshotAppliesPerFrame = Mathf.Clamp(clone.MaxSnapshotAppliesPerFrame, 1, 128);
        clone.MaxStateUpdatesPerFrame = Mathf.Clamp(clone.MaxStateUpdatesPerFrame, 1, 256);
        clone.MaxClientEntryChecksPerFrame = Mathf.Clamp(clone.MaxClientEntryChecksPerFrame, 16, 1024);
        clone.MaxPendingSnapshotQueue = Mathf.Clamp(clone.MaxPendingSnapshotQueue, 64, 2048);
        clone.MaxPendingStateQueue = Mathf.Clamp(clone.MaxPendingStateQueue, 128, 4096);
        clone.SnapshotDropResyncThreshold = Mathf.Clamp(clone.SnapshotDropResyncThreshold, 8, 256);
        clone.StateDropResyncThreshold = Mathf.Clamp(clone.StateDropResyncThreshold, 16, 512);
        clone.ServerControllerRescanInterval = Mathf.Clamp(clone.ServerControllerRescanInterval, 1f, 60f);
        clone.ServerSnapshotBroadcastInterval = Mathf.Clamp(clone.ServerSnapshotBroadcastInterval, 1f, 60f);
        clone.ServerSnapshotRetryInterval = Mathf.Clamp(clone.ServerSnapshotRetryInterval, 0.5f, 30f);
        return clone;
    }

    public static AISyncTuningSettings Default() => new();
}
