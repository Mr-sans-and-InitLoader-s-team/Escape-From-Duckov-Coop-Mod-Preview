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
using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public enum AIStatus : byte
{
    Dormant = 0,
    Spawning = 1,
    Active = 2,
    Despawned = 3,
    Dead = 4
}

public struct AIBuffState
{
    public int WeaponTypeId;
    public int BuffId;
}

public struct AnimSample
{
    public double t;
    public float speed, dirX, dirY;
    public int hand;
    public int vehicleType;
    public bool gunReady, dashing;
    public bool attack;
    public int stateHash;
    public float normTime;
}

public sealed class AISyncEntry
{
    public int Id { get; set; }
    public int SpawnerGuid { get; set; }
    public int PositionKey { get; set; }
    public Vector3 SpawnPosition { get; set; }
    public Quaternion SpawnRotation { get; set; } = Quaternion.identity;
    public string ModelName { get; set; }
    public string CustomFaceJson { get; set; }
    public string CharacterPresetKey { get; set; }
    public string HideIfFoundEnemyName { get; set; }
    public bool Activated { get; set; } = true;
    public string ScenePath { get; set; }
    public int SceneBuildIndex { get; set; }
    public Teams Team { get; set; } = Teams.scav;
    public AIStatus Status { get; set; }
    public float MaxHealth { get; set; }
    public float CurrentHealth { get; set; }
    public float BodyArmor { get; set; }
    public float HeadArmor { get; set; }
    public bool ShowHealthBar { get; set; } = true;
    public Vector3 LastKnownPosition { get; set; }
    public Quaternion LastKnownRotation { get; set; } = Quaternion.identity;
    public Vector3 LastKnownVelocity { get; set; }
    public double LastKnownRemoteTime { get; set; }
    public float LastStateSentTime { get; set; }
    public float LastStateReceivedTime { get; set; }
    public bool IsVehicle { get; set; }
    public int VehicleAnimationType { get; set; }
    public float VehicleWalkSpeed { get; set; }
    public float VehicleRunSpeed { get; set; }
    public AICharacterController Controller { get; set; }
    public readonly Dictionary<string, int> Equipment = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> Weapons = new(StringComparer.Ordinal);
    public readonly Dictionary<string, ItemSnapshot> WeaponSnapshots = new(StringComparer.Ordinal);
    public readonly List<AIBuffState> Buffs = new();
    public AnimSample LastAnimSample;

    // 记录服务端是否已执行过一次死亡流程（OnDead → 掉落广播），避免重复触发或遗漏
    public bool ServerDeathHandled { get; set; }
}
