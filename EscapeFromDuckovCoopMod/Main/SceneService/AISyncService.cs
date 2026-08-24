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

using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.Utilities;
using ECM2;
using HarmonyLib;
using ItemStatsSystem;
using LiteNetLib;
using Sirenix.Utilities;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using static Duckov.MiniGames.Examples.FPS.FPSGun;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public sealed class AISyncService
{
    private const float VehicleTransformSyncCullDistance = 150f;
    private const float SlowAiSpeedThreshold = 1.2f;
    private const float SlowAiStateIntervalScale = 0.5f;
    private const float SlowAiMinPositionDeltaScale = 0.25f;
    private const float MovingAiStateInterval = 0.05f;
    private const float SlowAiMinPositionDeltaFloor = 0.01f;
    private const float VehicleStateIntervalFloor = 0.2f;
    private const float VehicleMinPositionDeltaFloor = 0.4f;
    private const float VehicleReplicaKeepAliveDistance = 140f;
    private const float VehicleSnapshotRefreshInterval = 6f;
    private const float VehicleStateSuppressAfterTransformSeconds = 1f;
    private const float MinSnapshotRefreshIntervalRuntime = 30f;
    private const float MinServerSnapshotBroadcastIntervalRuntime = 30f;
    private const float PerfLogInterval = 5f;
    private const int MaxReplicaSpawnsPerFrame = 1;
    private const int ClientEntryCheckBudgetCap = 64;
    private const int MaxSnapshotChunkEntriesRuntime = 16;

    private AISyncTuningSettings Settings => CoopAISettings.Active;

    private float ActivationRadius => Settings.ActivationRadius;
    private float DeactivationRadius => Settings.DeactivationRadius;
    private float ActivationRetryInterval => Settings.ActivationRetryInterval;
    private float StateBroadcastInterval => Settings.StateBroadcastInterval;
    private float IdleStateRecordInterval => Settings.IdleStateRecordInterval;
    private float HealthBroadcastInterval => Settings.HealthBroadcastInterval;
    private float MinPositionDelta => Settings.MinPositionDelta;
    private float MinRotationDelta => Settings.MinRotationDelta;
    private float VelocityLerp => Settings.VelocityLerp;
    private float SnapshotRefreshInterval => Mathf.Max(Settings.SnapshotRefreshInterval, MinSnapshotRefreshIntervalRuntime);
    private float SnapshotRequestTimeout => Settings.SnapshotRequestTimeout;
    private float SnapshotRecoveryCooldown => Settings.SnapshotRecoveryCooldown;
    private int SnapshotChunkSize => Mathf.Min(Settings.SnapshotChunkSize, MaxSnapshotChunkEntriesRuntime);
    private int MaxStoredBuffs => Settings.MaxStoredBuffs;
    private int MaxSnapshotAppliesPerFrame => Settings.MaxSnapshotAppliesPerFrame;
    private int MaxStateUpdatesPerFrame => Settings.MaxStateUpdatesPerFrame;
    private int MaxClientEntryChecksPerFrame => Settings.MaxClientEntryChecksPerFrame;
    private int MaxPendingSnapshotQueue => Settings.MaxPendingSnapshotQueue;
    private int MaxPendingStateQueue => Settings.MaxPendingStateQueue;
    private int SnapshotDropResyncThreshold => Settings.SnapshotDropResyncThreshold;
    private int StateDropResyncThreshold => Settings.StateDropResyncThreshold;
    private float ServerControllerRescanInterval => Settings.ServerControllerRescanInterval;
    private float ServerSnapshotBroadcastInterval => Mathf.Max(Settings.ServerSnapshotBroadcastInterval, MinServerSnapshotBroadcastIntervalRuntime);
    private float ServerSnapshotRetryInterval => Settings.ServerSnapshotRetryInterval;

    public bool IsHostHurt { get; set; }

    private readonly Dictionary<int, HashSet<NetPeer>> _serverWatchers = new();
    private readonly Dictionary<NetPeer, Dictionary<int, DamageForwardPayload>> _serverLastDamageByPeer = new();
    private readonly Dictionary<int, NetPeer> _serverLastHitPeer = new();
    private readonly Dictionary<int, Queue<(int weaponTypeId, int buffId)>> _serverPendingBuffs = new();
    private readonly Dictionary<int, RemoteAIReplica> _clientReplicas = new();
    private readonly HashSet<int> _clientQuestKillCreditRaised = new();
    private readonly Dictionary<int, float> _clientRequested = new();
    private readonly Queue<int> _clientReplicaSpawnQueue = new();
    private readonly HashSet<int> _clientReplicaSpawnQueued = new();
    private readonly HashSet<int> _clientReplicaSpawning = new();
    private readonly Dictionary<int, List<(int weaponTypeId, int buffId)>> _clientPendingBuffs = new();
    private readonly Dictionary<int, float> _lastHealthBroadcastTime = new();
    private readonly Dictionary<int, PendingHealthBroadcast> _pendingHealthBroadcasts = new();
    private readonly Dictionary<int, int> _lastSnapshotSignatures = new();
    private readonly Dictionary<int, int> _lastStateSignatures = new();
    private readonly Dictionary<int, int> _lastHealthSignatures = new();
    private readonly Dictionary<int, float> _clientNextMissingModelWarning = new();
    private readonly Dictionary<int, float> _serverNextWatcherRefresh = new();
    private readonly Dictionary<string, GameObject> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterRandomPreset> _presetCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameObject> _hideIfFoundEnemyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AISnapshotEntry> _snapshotBuffer = new();
    private readonly List<AISyncEntry> _clientEntryBuffer = new();
    private readonly Queue<AISnapshotEntry> _pendingSnapshotQueue = new();
    private readonly Queue<AIStateUpdateRpc> _pendingStateUpdates = new();
    private readonly Dictionary<int, AIStateUpdateRpc> _latestPendingStateById = new();
    private readonly List<int> _pendingHealthFlush = new();
    private readonly List<int> _serverBuffQueueKeys = new();
    private readonly HashSet<int> _drainingBuffIds = new();
    private readonly Dictionary<int, float> _clientReplicaRecoveryTimes = new();
    public readonly Dictionary<int, DamageInfo> _clientLastDamage = new();
    private readonly List<NetPeer> _serverWatcherPruneBuffer = new();
    private readonly HashSet<NetPeer> _activationRecipients = new();
    private readonly HashSet<NetPeer> _serverNewWatcherBuffer = new();
    private readonly Dictionary<int, Animator> _serverAnimatorCache = new();
    private readonly Dictionary<int, string> _activeWeaponSlotById = new();
    private readonly Dictionary<int, int> _activeWeaponTypeById = new();
    private float _clientFrameBudgetScale = 1f;
    private bool _processingServerBuffQueue;
    private static readonly AccessTools.FieldRef<CharacterRandomPreset, CharacterModel>
        FR_CharacterModel = AccessTools.FieldRefAccess<CharacterRandomPreset, CharacterModel>("characterModel");
    private static readonly int AnimMoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int AnimMoveDirXHash = Animator.StringToHash("MoveDirX");
    private static readonly int AnimMoveDirYHash = Animator.StringToHash("MoveDirY");
    private static readonly int AnimDashingHash = Animator.StringToHash("Dashing");
    private static readonly int AnimAttackHash = Animator.StringToHash("Attack");
    private static readonly int AnimHandStateHash = Animator.StringToHash("HandState");
    private static readonly int AnimGunReadyHash = Animator.StringToHash("GunReady");
    private static readonly int AnimVehicleTypeHash = Animator.StringToHash("VehicleType");
    private static readonly FieldInfo HealthOnDeadEventField = AccessTools.Field(typeof(Health), "OnDead");

    private readonly struct PendingHealthBroadcast
    {
        public readonly float MaxHealth;
        public readonly float CurrentHealth;
        public readonly float BodyArmor;
        public readonly float HeadArmor;
        public readonly bool IsDead;
        public readonly bool HasDamage;
        public readonly DamageForwardPayload? Damage;
        public readonly float NextSendTime;

        public PendingHealthBroadcast(float maxHealth, float currentHealth, float bodyArmor, float headArmor, bool isDead, bool hasDamage, DamageForwardPayload? damage, float nextSendTime)
        {
            MaxHealth = maxHealth;
            CurrentHealth = currentHealth;
            BodyArmor = bodyArmor;
            HeadArmor = headArmor;
            IsDead = isDead;
            HasDamage = hasDamage;
            Damage = damage;
            NextSendTime = nextSendTime;
        }
    }

    private string _lastSnapshotSceneId;
    private bool _snapshotRequested;
    private float _nextSnapshotRefreshTime;
    private float _nextVehicleSnapshotRefreshTime;
    private float _lastSnapshotReceiveTime;
    private float _lastSnapshotRequestTime;
    private float _lastSnapshotRecoveryTime;
    private bool _pendingSnapshotReset;
    private int _droppedSnapshotCount;
    private int _droppedStateCount;
    private float _nextServerRescanTime;
    private float _nextServerSnapshotBroadcastTime;
    private bool _serverPendingSnapshotReset;
    private bool _syncBlocked;
    private int _clientEntryCursor;
    private int _clientEntryBufferVersion = -1;
    private int _clientAnchorFrame = -1;
    private bool _clientAnchorValid;
    private Vector3 _clientAnchor;
    private int _localAuthoritativeVehicleFrame = -1;
    private int _localAuthoritativeVehicleId;
    private float _nextPerfLogTime;
    private int _perfStatePackets;
    private int _perfSnapshotPackets;
    private int _perfHealthPackets;

    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;

    private bool ShouldBlockAiSync()
    {
        var core = MultiSceneCore.Instance;
        if (core == null) return false;

        var sceneInfo = core.SceneInfo;
        return sceneInfo != null && sceneInfo.ID == "Base";
    }

    private bool CheckAndHandleSyncBlock()
    {
        var blocked = ShouldBlockAiSync();
        if (blocked)
        {
            if (!_syncBlocked)
            {
                Reset();
                _syncBlocked = true;
            }
        }
        else if (_syncBlocked)
        {
            _syncBlocked = false;
        }

        return blocked;
    }

    public CharacterMainControl TryGetCharacter(int id)
    {
        if (id == 0) return null;

        if (IsServer)
        {
            if (CoopSyncDatabase.AI.TryGet(id, out var entry) && entry != null)
            {
                var controller = entry.Controller;
                if (controller)
                    return controller.CharacterMainControl;
            }
            return null;
        }

        if (_clientReplicas.TryGetValue(id, out var replica) && replica != null)
            return replica.Character;

        return null;
    }

    public void Reset()
    {
        if (IsServer)
        {
            foreach (var watcher in _serverWatchers.Values)
                watcher.Clear();
            _serverWatchers.Clear();
            _serverLastDamageByPeer.Clear();
            _serverLastHitPeer.Clear();
            _serverPendingBuffs.Clear();
            _serverBuffQueueKeys.Clear();
            _drainingBuffIds.Clear();
            _lastHealthBroadcastTime.Clear();
            _pendingHealthBroadcasts.Clear();
            _lastSnapshotSignatures.Clear();
            _lastStateSignatures.Clear();
            _lastHealthSignatures.Clear();
            _serverNextWatcherRefresh.Clear();
            _pendingHealthFlush.Clear();
        }
        else
        {
            foreach (var replica in _clientReplicas.Values)
                DestroyReplica(replica);
            _clientReplicas.Clear();
            _clientQuestKillCreditRaised.Clear();
            _clientRequested.Clear();
            _clientReplicaSpawnQueue.Clear();
            _clientReplicaSpawnQueued.Clear();
            _clientReplicaSpawning.Clear();
            _lastSnapshotSceneId = null;
            _snapshotRequested = false;
            _nextSnapshotRefreshTime = 0f;
            _nextVehicleSnapshotRefreshTime = 0f;
            _lastSnapshotReceiveTime = 0f;
            _lastSnapshotRequestTime = 0f;
            _lastSnapshotRecoveryTime = 0f;
            _pendingSnapshotQueue.Clear();
            _pendingStateUpdates.Clear();
            _pendingSnapshotReset = false;
            _droppedSnapshotCount = 0;
            _droppedStateCount = 0;
            _clientEntryCursor = 0;
            _clientEntryBuffer.Clear();
            _clientReplicaRecoveryTimes.Clear();
            _clientLastDamage.Clear();
            _clientEntryBufferVersion = -1;
            _clientAnchorFrame = -1;
            _localAuthoritativeVehicleFrame = -1;
        }

        _modelCache.Clear();
        _presetCache.Clear();
        _serverAnimatorCache.Clear();
        _activeWeaponSlotById.Clear();
        _activeWeaponTypeById.Clear();
        _clientPendingBuffs.Clear();
        foreach (var cached in _hideIfFoundEnemyCache.Values)
        {
            if (cached)
                Object.Destroy(cached);
        }
        _hideIfFoundEnemyCache.Clear();

        _nextServerRescanTime = 0f;
        _nextServerSnapshotBroadcastTime = 0f;
        _serverPendingSnapshotReset = IsServer;
        _nextPerfLogTime = 0f;
        _perfStatePackets = 0;
        _perfSnapshotPackets = 0;
        _perfHealthPackets = 0;
    }

    internal void OnSettingsChanged()
    {
        var now = Time.time;
        _nextSnapshotRefreshTime = now + SnapshotRefreshInterval;
        _nextServerRescanTime = now + ServerControllerRescanInterval;
        _nextServerSnapshotBroadcastTime = now + ServerSnapshotBroadcastInterval;
    }

    public void Server_RegisterCharacter(AICharacterController controller)
    {
        if (!IsServer || !controller) return;
        if (CheckAndHandleSyncBlock()) return;

        var entry = CoopSyncDatabase.AI.RegisterController(controller);
        if (entry == null) return;

        PopulateEntryMetadata(entry, controller);
        AttachTracker(controller, entry);
        entry.Status = AIStatus.Active;
        entry.LastKnownPosition = entry.SpawnPosition;
        entry.LastKnownRotation = entry.SpawnRotation;
        entry.LastKnownVelocity = Vector3.zero;
        ModApiEvents.RaiseServerRegisterAI(controller, entry);
    }

    public void Server_HandleTrackerDestroyed(AISyncEntry entry, AICharacterController controller)
    {
        if (!IsServer || entry == null) return;
        if (CheckAndHandleSyncBlock()) return;

        CoopSyncDatabase.AI.RemoveController(controller);
        entry.Controller = null;
        if (entry.Status != AIStatus.Dead)
            entry.Status = AIStatus.Despawned;
        entry.LastKnownVelocity = Vector3.zero;
        BroadcastDespawn(entry);
    }

    public void Server_HandleHealthChanged(AISyncEntry entry, float max, float current, float bodyArmor, float headArmor)
    {
        if (!IsServer || entry == null) return;
        if (CheckAndHandleSyncBlock()) return;

        if (max > 0f) entry.MaxHealth = max;
        entry.CurrentHealth = Mathf.Clamp(current, 0f, entry.MaxHealth <= 0f ? float.MaxValue : entry.MaxHealth);
        entry.BodyArmor = bodyArmor;
        entry.HeadArmor = headArmor;
        RefreshEntryPose(entry);
        BroadcastState(entry);
        QueueHealthBroadcast(entry, null, false);
    }

    public void Server_HandleDeath(AISyncEntry entry, float max, float current, float bodyArmor, float headArmor, DamageInfo? damage = null)
    {
        if (!IsServer || entry == null) return;
        if (CheckAndHandleSyncBlock()) return;

        var damagePayload = damage.HasValue ? DamageForwardPayload.FromDamageInfo(damage) : (DamageForwardPayload?)null;
        entry.Status = AIStatus.Dead;
        if (max > 0f) entry.MaxHealth = max;
        entry.CurrentHealth = Mathf.Clamp(current, 0f, entry.MaxHealth <= 0f ? float.MaxValue : entry.MaxHealth);
        entry.BodyArmor = bodyArmor;
        entry.HeadArmor = headArmor;
        RefreshEntryPose(entry);
        BroadcastState(entry);
        QueueHealthBroadcast(entry, damagePayload, true);
        BroadcastDespawn(entry);
    }

    public void Server_HandleHealthReport(RpcContext context, AIHealthReportRpc message)
    {
        if (!IsServer || context.Sender == null) return;
        if (CheckAndHandleSyncBlock()) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null) return;

        var damage = message.HasDamage ? message.Damage : (DamageForwardPayload?)null;

        var controller = entry.Controller;
        var cmc = controller ? controller.CharacterMainControl : null;
        var health = cmc ? cmc.Health : null;
        var (serverMax, serverCur, serverBody, serverHead) = ReadHealthSafe(health);

        if (serverMax > 0f)
            entry.MaxHealth = serverMax;

        var reportedMax = message.MaxHealth > 0f ? message.MaxHealth : entry.MaxHealth;
        var reportedCurrent = Mathf.Clamp(
            message.CurrentHealth,
            0f,
            reportedMax <= 0f ? float.MaxValue : reportedMax);

        if (!message.HasDamage)
        {
            if (message.MaxHealth > 0f && entry.MaxHealth <= 0f)
                entry.MaxHealth = message.MaxHealth;

            entry.CurrentHealth = health ? Mathf.Min(serverCur, reportedCurrent) : reportedCurrent;
            entry.BodyArmor = health ? Mathf.Min(serverBody, message.BodyArmor) : message.BodyArmor;
            entry.HeadArmor = health ? Mathf.Min(serverHead, message.HeadArmor) : message.HeadArmor;
        }
        else
        {
            if (message.MaxHealth > 0f && entry.MaxHealth <= 0f)
                entry.MaxHealth = message.MaxHealth;

            entry.CurrentHealth = health ? serverCur : reportedCurrent;
            entry.BodyArmor = health ? serverBody : message.BodyArmor;
            entry.HeadArmor = health ? serverHead : message.HeadArmor;
        }

        if (context.Sender != null && damage.HasValue)
        {
            if (!_serverLastDamageByPeer.TryGetValue(context.Sender, out var lastDamageForPeer) || lastDamageForPeer == null)
            {
                lastDamageForPeer = new Dictionary<int, DamageForwardPayload>();
                _serverLastDamageByPeer[context.Sender] = lastDamageForPeer;
            }

            lastDamageForPeer[message.Id] = damage.Value;
            _serverLastHitPeer[message.Id] = context.Sender;
        }

        var clampedCurrent = entry.CurrentHealth;
        var attacker = ResolveAttacker(context);
        var wasDead = entry.Status == AIStatus.Dead;
        var appliedDamage = ApplyDamageToController(entry, damage, attacker);
        if (appliedDamage)
        {
            controller = entry.Controller;
            cmc = controller ? controller.CharacterMainControl : null;
            health = cmc ? cmc.Health : null;
            if (health)
            {
                var (max, cur, body, head) = ReadHealthSafe(health);
                if (max > 0f)
                    entry.MaxHealth = max;
                entry.CurrentHealth = cur;
                entry.BodyArmor = body;
                entry.HeadArmor = head;
            }
            else
            {
                entry.CurrentHealth = clampedCurrent;
            }
        }
        else
        {
            ApplyHealthToController(entry);

            controller = entry.Controller;
            cmc = controller ? controller.CharacterMainControl : null;
            health = cmc ? cmc.Health : null;
            if (health)
            {
                var (max, cur, body, head) = ReadHealthSafe(health);
                if (max > 0f)
                    entry.MaxHealth = max;
                entry.CurrentHealth = cur;
                entry.BodyArmor = body;
                entry.HeadArmor = head;
            }
        }

        var isDead = message.IsDead || entry.CurrentHealth <= 0.0001f;
        if (isDead)
        {
            var forced = appliedDamage || EnsureServerControllerDeath(entry, damage, attacker);
            if (forced)
                entry.ServerDeathHandled = true;

            if (!forced)
            {
                ForceDisableServerController(entry);
            }

            entry.Status = AIStatus.Dead;
            if (!entry.ServerDeathHandled)
            {
                entry.ServerDeathHandled = true;
                TryForceServerOnDead(entry, damage, attacker);
            }
            if (!wasDead)
                NotifyKillerOfServerDeath(entry);
            QueueHealthBroadcast(entry, damage, true);
            if (!wasDead)
                BroadcastDespawn(entry);

            return;
        }

        QueueHealthBroadcast(entry, damage, false);
    }

    private void TryForceServerOnDead(AISyncEntry entry, DamageForwardPayload? damage, CharacterMainControl attacker)
    {
        if (entry == null) return;

        var controller = entry.Controller;
        var cmc = controller ? controller.CharacterMainControl : null;
        var health = cmc ? cmc.Health : null;
        if (!cmc || !health) return;

        var receiver = cmc ? cmc.mainDamageReceiver : null;
        if (!receiver && cmc)
            receiver = cmc.GetComponentInChildren<DamageReceiver>(true);

        var info = damage?.ToDamageInfo(attacker, receiver) ?? new DamageInfo(attacker)
        {
            damageValue = Mathf.Max(1f, entry.CurrentHealth <= 0f ? 1f : entry.CurrentHealth),
            finalDamage = Mathf.Max(1f, entry.CurrentHealth <= 0f ? 1f : entry.CurrentHealth),
            damagePoint = cmc.transform.position,
            damageNormal = Vector3.up
        };

        if (info.toDamageReceiver == null)
            info.toDamageReceiver = receiver;
        if (info.fromCharacter == null)
            info.fromCharacter = attacker;

        var bak = DeadLootSpawnContext.InOnDead;
       // DeadLootSpawnContext.InOnDead = cmc;
        try
        {
            IsHostHurt = true;
            cmc.Health.Hurt(info);
        }
        catch
        {
        }
        finally
        {
          //  DeadLootSpawnContext.InOnDead = bak;
        }
    }

    public void Server_HandleBuffReport(RpcContext context, AIBuffReportRpc message)
    {
        if (!IsServer || context.Sender == null || message.BuffId == 0) return;
        if (CheckAndHandleSyncBlock()) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null) return;

        var cmc = TryGetCharacter(entry.Id);
        if (cmc)
            ApplyBuffProxy(cmc, message.WeaponTypeId, message.BuffId);

        BroadcastAIBuff(entry, message.WeaponTypeId, message.BuffId);
    }

    public void Server_HandleBuffApplied(CharacterMainControl target, int weaponTypeId, int buffId)
    {
        if (!IsServer || target == null || buffId == 0) return;
        if (CheckAndHandleSyncBlock()) return;

        var controller = ResolveAiController(target);
        if (!controller) return;

        if (!CoopSyncDatabase.AI.TryGet(controller, out var entry) || entry == null) return;

        BroadcastAIBuff(entry, weaponTypeId, buffId);
    }

    public void Server_Update(float deltaTime)
    {
        if (!IsServer || Service == null || !Service.networkStarted) return;
        if (CheckAndHandleSyncBlock()) return;

        if (_serverPendingBuffs.Count > 0)
            ProcessServerBuffQueue();

        var now = Time.unscaledTime;
        var safeDeltaTime = Mathf.Max(deltaTime, 0.0001f);
        foreach (var entry in CoopSyncDatabase.AI.Entries)
        {
            if (entry == null) continue;
            var controller = entry.Controller;
            if (!controller)
            {
                if (entry.Status == AIStatus.Active)
                {
                    entry.Status = AIStatus.Despawned;
                    BroadcastDespawn(entry);
                }
                continue;
            }

            var cmc = controller.CharacterMainControl;
            var wasActivated = entry.Activated;
            var activatedNow = IsControllerActivated(controller, cmc);
            if (cmc != null && cmc.isVehicle)
                activatedNow = true;
            if (wasActivated != activatedNow)
            {
                entry.Activated = activatedNow;
                BroadcastActivationState(entry);
                if (!activatedNow)
                    _serverWatchers.Remove(entry.Id);
            }
            var watchers = entry.Activated ? GetServerWatchers(entry.Id) : null;
            if (watchers != null)
            {
                watchers = GetServerWatchersInRange(entry, DeactivationRadius);
            }
            else if (entry.Activated && ShouldRefreshServerWatchers(entry.Id, now))
            {
                watchers = EnsureServerWatchers(entry);
            }
            var modelTransform = cmc && cmc.characterModel ? cmc.characterModel.transform : null;
            var position = controller.transform.position;
            var rotation = modelTransform ? modelTransform.rotation : controller.transform.rotation;
            var positionDelta = position - entry.LastKnownPosition;
            var velocity = positionDelta / safeDeltaTime;
            var activeWeaponChanged = RefreshActiveWeapon(entry, cmc);

            if (watchers == null || watchers.Count == 0)
            {
                if (now - entry.LastStateSentTime < IdleStateRecordInterval)
                    continue;

                entry.LastKnownPosition = position;
                entry.LastKnownRotation = rotation;
                entry.LastKnownVelocity = Vector3.zero;
                entry.LastStateSentTime = now;
                continue;
            }

            var posDeltaSqr = positionDelta.sqrMagnitude;
            var rotDelta = Quaternion.Angle(rotation, entry.LastKnownRotation);

            var speedSqr = velocity.sqrMagnitude;
            var isSlowMoving = speedSqr > 0.0001f && speedSqr <= SlowAiSpeedThreshold * SlowAiSpeedThreshold;
            var isMoving = speedSqr > 0.0001f;
            var isVehicleEntry = entry.IsVehicle || (cmc != null && cmc.isVehicle);

            var stateInterval = StateBroadcastInterval;
            if (isSlowMoving)
                stateInterval *= SlowAiStateIntervalScale;
            if (isMoving)
                stateInterval = Mathf.Min(stateInterval, MovingAiStateInterval);
            if (isVehicleEntry)
                stateInterval = Mathf.Max(stateInterval, VehicleStateIntervalFloor);

            if (float.IsPositiveInfinity(stateInterval))
            {
                entry.LastKnownPosition = position;
                entry.LastKnownRotation = rotation;
                entry.LastKnownVelocity = Vector3.zero;
                continue;
            }

            var positionDeltaThreshold = isSlowMoving
                ? Mathf.Max(SlowAiMinPositionDeltaFloor, MinPositionDelta * SlowAiMinPositionDeltaScale)
                : MinPositionDelta;

            if (isVehicleEntry)
                positionDeltaThreshold = Mathf.Max(positionDeltaThreshold, VehicleMinPositionDeltaFloor);

            if (isMoving)
            {
                if (!activeWeaponChanged && now - entry.LastStateSentTime < stateInterval && rotDelta < MinRotationDelta)
                    continue;
            }
            else
            {
                if (!activeWeaponChanged && now - entry.LastStateSentTime < stateInterval &&
                    posDeltaSqr < positionDeltaThreshold * positionDeltaThreshold && rotDelta < MinRotationDelta)
                    continue;
            }

            entry.LastKnownPosition = position;
            entry.LastKnownRotation = rotation;
            entry.LastKnownVelocity = Vector3.Lerp(entry.LastKnownVelocity, velocity, deltaTime * VelocityLerp);
            entry.LastStateSentTime = now;
            entry.LastAnimSample = CaptureAnimSample(entry, controller);

            BroadcastState(entry, watchers, true);
        }

        Server_FlushPendingHealth(now);

        if (now >= _nextServerRescanTime)
        {
            Server_RescanActiveControllers();
            _nextServerRescanTime = now + ServerControllerRescanInterval;
        }

        if (now >= _nextServerSnapshotBroadcastTime)
        {
            var sent = Server_BroadcastSnapshotToAll(_serverPendingSnapshotReset);
            _serverPendingSnapshotReset &= !sent;
            _nextServerSnapshotBroadcastTime = now + (sent ? ServerSnapshotBroadcastInterval : ServerSnapshotRetryInterval);
        }

        TryWriteAiPerfLog(now, deltaTime);
    }

    public void Server_HandleSnapshotRequest(RpcContext context, AISnapshotRequestRpc message)
    {
        if (!IsServer || context.Sender == null) return;
        if (CheckAndHandleSyncBlock()) return;

        try
        {
            _snapshotBuffer.Clear();
            var hasRadius = message.HasRadius;
            var center = message.Center;
            var radiusSqr = message.Radius * message.Radius;
            var reset = message.ForceFull;

            foreach (var entry in CoopSyncDatabase.AI.Entries)
            {
                if (entry == null) continue;
                if (message.VehiclesOnly && !entry.IsVehicle) continue;

                if (hasRadius)
                {
                    var distanceSqr = (GetEntryAnchor(entry) - center).sqrMagnitude;
                    if (distanceSqr > radiusSqr) continue;
                }

                var snapshot = BuildSnapshotEntry(entry);
                var duplicate = !hasRadius && !snapshot.IsVehicle && !message.VehiclesOnly && HasDuplicateSnapshot(entry.Id, in snapshot);
                if (!reset && duplicate)
                    continue;

                _snapshotBuffer.Add(snapshot);

                if (_snapshotBuffer.Count >= SnapshotChunkSize)
                {
                    SendSnapshotChunk(context.Sender, _snapshotBuffer, reset);
                    reset = false;
                    _snapshotBuffer.Clear();
                }
            }

            if (_snapshotBuffer.Count > 0)
                SendSnapshotChunk(context.Sender, _snapshotBuffer, reset);
        }
        finally
        {
            _snapshotBuffer.Clear();
        }
    }

    public void Server_HandleActivationRequest(RpcContext context, AIActivationRequestRpc message)
    {
        if (!IsServer || context.Sender == null) return;
        if (CheckAndHandleSyncBlock()) return;

        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
        {
            Client_TryScheduleSnapshotRecovery(true);
            return;
        }

        var forceSpawn = entry.IsVehicle || IsForceSpawnModel(entry.ModelName);

        if (!entry.Activated && !forceSpawn)
            return;

        if (!forceSpawn)
        {
            var anchor = GetEntryAnchor(entry);
            if (!IsPeerWithinRange(context.Sender, anchor, ActivationRadius))
                return;
        }

        if (!_serverWatchers.TryGetValue(message.Id, out var watchers) || watchers == null)
        {
            watchers = new HashSet<NetPeer>();
            _serverWatchers[message.Id] = watchers;
        }

        if (watchers.Add(context.Sender))
            BroadcastSpawn(entry, watchers);
        else if (message.Force)
            BroadcastSpawn(entry, watchers);
    }

    public void Server_OnPeerDisconnected(NetPeer peer)
    {
        if (!IsServer || peer == null) return;

        foreach (var watchers in _serverWatchers.Values)
            watchers?.Remove(peer);
    }

    public void Client_RequestSnapshotIfNeeded()
    {
        if (IsServer || Service == null || !Service.networkStarted) return;
        if (CheckAndHandleSyncBlock()) return;

        var sceneId = SceneNet.Instance?._sceneReadySidSent;
        if (string.IsNullOrEmpty(sceneId))
        {
            _lastSnapshotSceneId = null;
            _snapshotRequested = false;
            return;
        }

        if (!string.Equals(_lastSnapshotSceneId, sceneId, StringComparison.Ordinal))
        {
            _lastSnapshotSceneId = sceneId;
            _snapshotRequested = false;
        }

        if (_snapshotRequested)
            return;

        _snapshotRequested = true;
        Client_SendSnapshotRequest(true);
        _nextSnapshotRefreshTime = Time.unscaledTime + SnapshotRefreshInterval;
        _nextVehicleSnapshotRefreshTime = Time.unscaledTime + 1f;
    }

    private void Client_SendSnapshotRequest(bool forceFull, bool vehiclesOnly = false)
    {
        if (IsServer || Service == null || !Service.networkStarted) return;
        if (CheckAndHandleSyncBlock()) return;

        var hasAnchor = TryGetClientAnchor(out var center);
        var radius = vehiclesOnly
            ? VehicleReplicaKeepAliveDistance * 1.2f
            : Mathf.Max(ActivationRadius, VehicleReplicaKeepAliveDistance) * 1.2f;
        var request = new AISnapshotRequestRpc
        {
            HasRadius = hasAnchor,
            Radius = radius,
            Center = hasAnchor ? center : Vector3.zero,
            ForceFull = forceFull,
            VehiclesOnly = vehiclesOnly
        };
        CoopTool.SendRpc(in request);
        _lastSnapshotRequestTime = Time.unscaledTime;
    }

    public void Client_HandleSnapshotChunk(AISnapshotChunkRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        _lastSnapshotReceiveTime = Time.unscaledTime;
        if (message.Reset)
        {
            _pendingSnapshotReset = true;
            _pendingSnapshotQueue.Clear();
        }

        if (message.Entries == null) return;

        foreach (var snap in message.Entries)
        {
            if (!IsClientEntryInRange(in snap, GetClientSnapshotRange(snap.IsVehicle)))
                continue;

            if (_pendingSnapshotQueue.Count >= MaxPendingSnapshotQueue)
            {
                _pendingSnapshotQueue.Dequeue();
                _droppedSnapshotCount++;
            }
            _pendingSnapshotQueue.Enqueue(snap);
        }

        if (_droppedSnapshotCount >= SnapshotDropResyncThreshold)
        {
            _droppedSnapshotCount = 0;
            Client_TryScheduleSnapshotRecovery(true);
        }
    }

    public void Client_HandleActivationState(AIActivationStateRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        var entry = CoopSyncDatabase.AI.GetOrCreate(message.Id);

        if (!IsClientEntryInRange(entry, GetClientSnapshotRange(entry.IsVehicle)))
        {
            Client_DestroyReplica(message.Id, AIStatus.Despawned, false);
            entry.Activated = false;
            return;
        }

        entry.Activated = message.Activated;

        if (entry.Activated)
        {
            Client_EnsureReplica(entry);
        }
        else
        {
            Client_DestroyReplica(message.Id, AIStatus.Despawned, false);
        }
    }

    public void Client_HandleSpawn(AISpawnRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        if (!IsClientEntryInRange(in message.Entry, GetClientSnapshotRange(message.Entry.IsVehicle)))
            return;

        var entry = CoopSyncDatabase.AI.GetOrCreate(message.Entry.Id);
        ApplySnapshot(entry, in message.Entry);

        // Avoid applying any pre-existing buff state when initially generating the AI replica.
        // Buff broadcasts that arrive after spawn will still be processed normally.
        entry.Buffs.Clear();
        _clientPendingBuffs.Remove(entry.Id);

        _clientRequested.Remove(entry.Id);

        Client_EnsureReplica(entry);
    }

    public void Client_HandleDespawn(AIDespawnRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
            return;

        Client_DestroyReplica(message.Id, message.Status, false);
    }

    public void Client_HandleState(AIStateUpdateRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
            return;

        var anchor = message.Position != Vector3.zero ? message.Position : GetEntryAnchor(entry);
        var stateRange = entry.IsVehicle ? VehicleReplicaKeepAliveDistance : DeactivationRadius;
        if (!IsClientWithinRange(anchor, stateRange))
        {
            Client_DestroyReplica(message.Id, AIStatus.Despawned, false);
            return;
        }
        if (_pendingStateUpdates.Count >= MaxPendingStateQueue)
        {
            _pendingStateUpdates.Dequeue();
            _droppedStateCount++;
        }
        _pendingStateUpdates.Enqueue(message);

        if (_droppedStateCount >= StateDropResyncThreshold)
        {
            _droppedStateCount = 0;
            Client_TryScheduleSnapshotRecovery(false);
        }
    }

    public void Client_Update(float deltaTime)
    {
        if (IsServer || Service == null || !Service.networkStarted) return;
        if (CheckAndHandleSyncBlock()) return;

        Client_RequestSnapshotIfNeeded();
        Client_ProcessPendingSnapshots();
        Client_ProcessPendingStateUpdates();
        Client_ProcessReplicaSpawnQueue();

        var now = Time.unscaledTime;
        if (_snapshotRequested && _nextSnapshotRefreshTime > 0f && now >= _nextSnapshotRefreshTime)
        {
            Client_SendSnapshotRequest(false);
            _nextSnapshotRefreshTime = now + SnapshotRefreshInterval;
        }

        if (_snapshotRequested && _nextVehicleSnapshotRefreshTime > 0f && now >= _nextVehicleSnapshotRefreshTime)
        {
            Client_SendSnapshotRequest(false, true);
            _nextVehicleSnapshotRefreshTime = now + VehicleSnapshotRefreshInterval;
        }

        if (_snapshotRequested && now - _lastSnapshotRequestTime >= SnapshotRequestTimeout && now - _lastSnapshotReceiveTime >= SnapshotRequestTimeout)
        {
            var forceFull = _lastSnapshotReceiveTime <= 0f;
            Client_TryScheduleSnapshotRecovery(forceFull);
        }

        foreach (var replica in _clientReplicas.Values)
            replica.Update(deltaTime);

        RefreshClientEntryBufferIfNeeded();
        var totalEntries = _clientEntryBuffer.Count;
        if (totalEntries == 0)
        {
            TryWriteAiPerfLog(now, deltaTime);
            return;
        }

        UpdateClientFrameBudgetScale(deltaTime);

        var entryBudget = GetDynamicProcessBudget(Mathf.Min(MaxClientEntryChecksPerFrame, ClientEntryCheckBudgetCap), totalEntries);
        entryBudget = ScaleBudgetForDelta(entryBudget);
        _clientEntryCursor = Mathf.Clamp(_clientEntryCursor, 0, Mathf.Max(0, totalEntries - 1));

        now = Time.unscaledTime;
        for (var processed = 0; processed < entryBudget && totalEntries > 0; processed++)
        {
            var entry = _clientEntryBuffer[_clientEntryCursor];
            _clientEntryCursor = (_clientEntryCursor + 1) % totalEntries;

            if (entry == null) continue;

            var forceSpawn = ShouldForceSpawnReplica(entry);
            var shouldKeepAlive = ShouldKeepReplicaAlive(entry) || forceSpawn;

            if (!entry.Activated && !forceSpawn)
            {
                Client_DestroyReplica(entry.Id, AIStatus.Despawned, !shouldKeepAlive);
                continue;
            }

            if (!ShouldHaveClientReplica(entry))
            {
                Client_DestroyReplica(entry.Id, AIStatus.Despawned, !shouldKeepAlive);
                continue;
            }

            Client_EnsureReplica(entry);
        }

        TryWriteAiPerfLog(now, deltaTime);
    }

    private void RefreshClientEntryBufferIfNeeded()
    {
        var registry = CoopSyncDatabase.AI;
        if (_clientEntryBufferVersion == registry.Version && _clientEntryBuffer.Count == registry.Count)
            return;

        _clientEntryBuffer.Clear();
        registry.CopyEntriesTo(_clientEntryBuffer);
        _clientEntryBufferVersion = registry.Version;

        if (_clientEntryCursor >= _clientEntryBuffer.Count)
            _clientEntryCursor = 0;
    }

    private bool ShouldForceSpawnReplica(AISyncEntry entry)
    {
        if (entry == null)
            return false;

        if (IsForceSpawnModel(entry.ModelName))
            return true;

        if (!entry.IsVehicle)
            return false;

        return IsLocalAuthoritativeVehicle(entry) || IsClientEntryInStrictRange(entry, VehicleReplicaKeepAliveDistance);
    }

    private float GetClientSnapshotRange(bool isVehicle)
    {
        return isVehicle ? VehicleReplicaKeepAliveDistance : ActivationRadius;
    }

    private bool ShouldHaveClientReplica(AISyncEntry entry)
    {
        if (entry == null || entry.Status == AIStatus.Dead || entry.Status == AIStatus.Despawned)
            return false;

        var forceSpawn = ShouldForceSpawnReplica(entry);
        if (entry.IsVehicle)
            return forceSpawn;

        if (!entry.Activated && !forceSpawn)
            return false;

        return forceSpawn || IsClientEntryInRange(entry, DeactivationRadius);
    }

    private static bool HasReplicaSpawnMetadata(AISyncEntry entry)
    {
        return entry != null &&
               (!string.IsNullOrEmpty(entry.ModelName) ||
                !string.IsNullOrEmpty(entry.CharacterPresetKey));
    }

    private void LogMissingModelPrefab(AISyncEntry entry)
    {
        if (entry == null)
            return;

        if (!HasReplicaSpawnMetadata(entry))
            return;

        var now = Time.unscaledTime;
        if (_clientNextMissingModelWarning.TryGetValue(entry.Id, out var next) && now < next)
            return;

        _clientNextMissingModelWarning[entry.Id] = now + 10f;
        Debug.LogWarning(
            $"[AI][CLIENT] Missing model prefab id={entry.Id} model='{entry.ModelName}' preset='{entry.CharacterPresetKey}', defer replica spawn.");
    }

    private void QueueReplicaSpawn(AISyncEntry entry)
    {
        if (entry == null)
            return;

        if (!HasReplicaSpawnMetadata(entry))
        {
            Client_DeferReplicaSpawn(entry, true);
            return;
        }

        if (_clientReplicaRecoveryTimes.TryGetValue(entry.Id, out var retryAt) && Time.unscaledTime < retryAt)
            return;

        if (_clientReplicas.ContainsKey(entry.Id) || _clientReplicaSpawning.Contains(entry.Id))
            return;

        if (_clientReplicaSpawnQueued.Add(entry.Id))
            _clientReplicaSpawnQueue.Enqueue(entry.Id);
    }

    private void Client_ProcessReplicaSpawnQueue()
    {
        var spawned = 0;
        while (spawned < MaxReplicaSpawnsPerFrame && _clientReplicaSpawnQueue.Count > 0)
        {
            var id = _clientReplicaSpawnQueue.Dequeue();
            _clientReplicaSpawnQueued.Remove(id);

            if (_clientReplicas.ContainsKey(id) || _clientReplicaSpawning.Contains(id))
                continue;

            if (!CoopSyncDatabase.AI.TryGet(id, out var entry) || entry == null || !ShouldHaveClientReplica(entry))
                continue;

            _clientReplicaSpawning.Add(id);
            SpawnReplicaAsync(entry).Forget();
            spawned++;
        }
    }

    private void Client_EnsureReplica(AISyncEntry entry)
    {
        if (entry == null) return;

        if (entry.Status == AIStatus.Dead)
        {
            Client_DestroyReplica(entry.Id, AIStatus.Dead, false);
            return;
        }

        if (entry.Status == AIStatus.Despawned)
        {
            Client_DestroyReplica(entry.Id, AIStatus.Despawned, false);
            return;
        }

        if (!ShouldHaveClientReplica(entry))
        {
            Client_DestroyReplica(entry.Id, AIStatus.Despawned, false);
            return;
        }

        if (_clientReplicaRecoveryTimes.TryGetValue(entry.Id, out var retryAt) && Time.unscaledTime < retryAt)
            return;
        _clientReplicaRecoveryTimes.Remove(entry.Id);
        _clientPendingBuffs.Remove(entry.Id);

        if (_clientReplicas.TryGetValue(entry.Id, out var replica))
        {
            replica.ApplyState(entry);
            return;
        }

        QueueReplicaSpawn(entry);
    }

    private void Client_DestroyReplica(int id, AIStatus status, bool removeEntry)
    {
        if (!CoopSyncDatabase.AI.TryGet(id, out var entry) || entry == null)
            return;

        entry.Activated = false;
        entry.Status = status;
        entry.LastAnimSample = default;

        if (_clientReplicas.TryGetValue(id, out var replica))
        {
            DestroyReplica(replica);
            _clientReplicas.Remove(id);
        }

        _clientRequested.Remove(id);
        _clientReplicaSpawnQueued.Remove(id);
        _clientPendingBuffs.Remove(id);
        _clientReplicaRecoveryTimes.Remove(id);
        _clientNextMissingModelWarning.Remove(id);

        if (removeEntry)
            CoopSyncDatabase.AI.RemoveEntry(id);
    }

    private void Client_ProcessPendingSnapshots()
    {
        if (_pendingSnapshotReset)
        {
            _clientRequested.Clear();
            _clientReplicaSpawnQueue.Clear();
            _clientReplicaSpawnQueued.Clear();
            _clientPendingBuffs.Clear();

            _pendingSnapshotReset = false;
        }

        var processed = 0;
        var budget = GetDynamicProcessBudget(MaxSnapshotAppliesPerFrame, _pendingSnapshotQueue.Count);
        budget = ScaleBudgetForDelta(budget);
        while (processed < budget && _pendingSnapshotQueue.Count > 0)
        {
            var snap = _pendingSnapshotQueue.Dequeue();
            var entry = CoopSyncDatabase.AI.GetOrCreate(snap.Id);
            ApplySnapshot(entry, in snap);
            if (entry.Status == AIStatus.Dead)
            {
                Client_DestroyReplica(entry.Id, AIStatus.Dead, false);
                processed++;
                continue;
            }
            processed++;
        }

        if (_pendingSnapshotQueue.Count == 0)
            _droppedSnapshotCount = 0;
    }

    private void Client_ProcessPendingStateUpdates()
    {
        if (_pendingStateUpdates.Count == 0)
        {
            _latestPendingStateById.Clear();
            _droppedStateCount = 0;
            return;
        }

        _latestPendingStateById.Clear();
        var budget = GetDynamicProcessBudget(MaxStateUpdatesPerFrame, _pendingStateUpdates.Count);
        budget = ScaleBudgetForDelta(budget);
        var drained = 0;
        while (drained < budget && _pendingStateUpdates.Count > 0)
        {
            var message = _pendingStateUpdates.Dequeue();
            _latestPendingStateById[message.Id] = message;
            drained++;
        }

        foreach (var message in _latestPendingStateById.Values)
            Client_ApplyStateUpdate(in message);

        _latestPendingStateById.Clear();

        if (_pendingStateUpdates.Count == 0)
            _droppedStateCount = 0;
    }

    private void Client_ApplyStateUpdate(in AIStateUpdateRpc message)
    {
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
        {
            Client_TryScheduleSnapshotRecovery(true);
            return;
        }

        var localAuthVehicle = IsLocalAuthoritativeVehicle(entry);
        var suppressVehicleState = entry.IsVehicle &&
                                   !localAuthVehicle &&
                                   RPCVehicle.HasRecentVehicleTransform(message.Id, VehicleStateSuppressAfterTransformSeconds);

        if (!localAuthVehicle && !suppressVehicleState)
        {
            entry.LastKnownPosition = message.Position;
            entry.LastKnownRotation = message.Rotation;
            entry.LastKnownVelocity = message.Velocity;
            entry.LastKnownRemoteTime = message.Timestamp;
        }
        entry.CurrentHealth = message.CurrentHealth;
        SetActiveWeapon(message.Id, GetActiveWeaponSlot(message.Id), message.ActiveWeaponTypeId);
        entry.LastStateReceivedTime = Time.unscaledTime;
        if (message.StatusOverride != AIStatus.Dormant)
            entry.Status = message.StatusOverride;

        if (!localAuthVehicle && !suppressVehicleState)
        {
            entry.LastAnimSample = new AnimSample
            {
                t = Time.unscaledTimeAsDouble,
                speed = message.MoveSpeed,
                dirX = message.MoveDirX,
                dirY = message.MoveDirY,
                dashing = message.IsDashing,
                attack = message.IsAttacking,
                hand = message.HandState,
                gunReady = message.GunReady,
                vehicleType = message.VehicleType,
                stateHash = message.StateHash,
                normTime = message.NormTime
            };
        }

        if (!suppressVehicleState && _clientReplicas.TryGetValue(message.Id, out var replica))
            replica.ApplyState(entry);
    }

    public void Client_HandleHealthBroadcast(AIHealthBroadcastRpc message)
    {
        if (IsServer) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
            return;

        if (!IsClientEntryInRange(entry, DeactivationRadius))
        {
            Client_DestroyReplica(message.Id, AIStatus.Despawned, false);
            return;
        }

        if (message.MaxHealth > 0f)
            entry.MaxHealth = message.MaxHealth;
        entry.CurrentHealth = message.CurrentHealth;
        entry.BodyArmor = message.BodyArmor;
        entry.HeadArmor = message.HeadArmor;
        if (message.IsDead)
        {
            entry.Status = AIStatus.Dead;
            if (message.KillerCredit)
                Client_RaiseQuestKillCredit(message.Id, in message);
            Client_DestroyReplica(message.Id, AIStatus.Dead, false);
            return;
        }

        if (_clientReplicas.TryGetValue(message.Id, out var replica))
            replica.ApplyHealth(entry.MaxHealth, entry.CurrentHealth, entry.BodyArmor, entry.HeadArmor);
    }

    public void Client_HandleBuffBroadcast(AIBuffBroadcastRpc message)
    {
        if (IsServer || message.BuffId == 0) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
        {
            CacheClientPendingBuff(message.Id, message.WeaponTypeId, message.BuffId);
            Client_TryScheduleSnapshotRecovery(true);
            return;
        }

        if (!IsClientEntryInRange(entry, DeactivationRadius))
        {
            Client_DestroyReplica(message.Id, AIStatus.Despawned, false);
            return;
        }

        AppendBuffState(entry.Buffs, message.WeaponTypeId, message.BuffId);

        if (_clientReplicas.TryGetValue(message.Id, out var replica) && replica != null)
            replica.ApplyBuff(message.WeaponTypeId, message.BuffId);
        else
        {
            CacheClientPendingBuff(message.Id, message.WeaponTypeId, message.BuffId);

            if (entry != null)
            {
                var now = Time.unscaledTime;
                _clientRequested.TryGetValue(message.Id, out var nextRetry);
                if (now >= nextRetry)
                {
                    var request = new AIActivationRequestRpc
                    {
                        Id = message.Id,
                        Force = true
                    };
                    CoopTool.SendRpc(in request);
                    _clientRequested[message.Id] = now + ActivationRetryInterval;
                }

                if (entry.Activated || entry.Status == AIStatus.Active)
                    Client_TryScheduleSnapshotRecovery(false);
            }
        }
    }

    public void Client_HandleVehicleTransform(VehicleTransformSyncRpc message)
    {
        if (IsServer) return;

        if (!IsClientWithinRange(message.Position, VehicleTransformSyncCullDistance))
            return;

        if (!CoopSyncDatabase.AI.TryGet(message.VehicleId, out var entry) || entry == null)
        {
            Client_TryScheduleSnapshotRecovery(true);
            return;
        }

        if (IsLocalAuthoritativeVehicle(entry))
            return;

        entry.LastKnownPosition = message.Position;
        entry.LastKnownRotation = message.Rotation;
        entry.LastKnownVelocity = message.Velocity;
        entry.LastKnownRemoteTime = message.Timestamp;
        entry.LastStateReceivedTime = Time.unscaledTime;

        if (_clientReplicas.TryGetValue(message.VehicleId, out var replica))
            replica.ApplyState(entry);
    }

    private void Server_FlushPendingHealth(float now)
    {
        if (_pendingHealthBroadcasts.Count == 0)
            return;

        _pendingHealthFlush.Clear();

        foreach (var kvp in _pendingHealthBroadcasts)
        {
            if (now >= kvp.Value.NextSendTime)
                _pendingHealthFlush.Add(kvp.Key);
        }

        foreach (var id in _pendingHealthFlush)
        {
            if (!_pendingHealthBroadcasts.TryGetValue(id, out var pending))
                continue;

            if (!CoopSyncDatabase.AI.TryGet(id, out var entry) || entry == null)
            {
                _pendingHealthBroadcasts.Remove(id);
                _lastHealthBroadcastTime.Remove(id);
                continue;
            }

            entry.MaxHealth = pending.MaxHealth;
            entry.CurrentHealth = pending.CurrentHealth;
            entry.BodyArmor = pending.BodyArmor;
            entry.HeadArmor = pending.HeadArmor;
            _lastHealthBroadcastTime[id] = now;
            _pendingHealthBroadcasts.Remove(id);
            var damage = pending.HasDamage ? pending.Damage : (DamageForwardPayload?)null;
            BroadcastAIHealth(entry, damage);
        }

        _pendingHealthFlush.Clear();
    }

    private void PopulateEntryMetadata(AISyncEntry entry, AICharacterController controller)
    {
        try
        {
            var cmc = controller.CharacterMainControl;
            if (!cmc) return;

          //  Debug.Log($"[AI][SERVER] PopulateEntryMetadata id={entry.Id} name={controller.name} model={(cmc.characterModel ? cmc.characterModel.name : "null")} preset={(cmc.characterPreset ? cmc.characterPreset.name : "null")} presetKey={(cmc.characterPreset ? cmc.characterPreset.nameKey : "null")} isVehicle={cmc.isVehicle} vehicleAnim={cmc.vehicleAnimationType}");

            entry.Activated = IsControllerActivated(controller, cmc);
            entry.SpawnPosition = controller.transform.position;
            entry.SpawnRotation = cmc.characterModel ? cmc.characterModel.transform.rotation : controller.transform.rotation;
            var scene = controller.gameObject.scene;
            entry.SceneBuildIndex = scene.buildIndex;
            entry.ScenePath = scene.path;
            entry.PositionKey = AISyncRegistry.ComputePositionKey(entry.SpawnPosition, entry.SceneBuildIndex, entry.ScenePath);
            entry.ModelName = cmc.characterModel ? NormalizePrefabName(cmc.characterModel.name) : cmc.name;
            entry.CustomFaceJson = TryCaptureFaceJson(cmc.characterModel);
            if (cmc.characterPreset)
            {
                entry.CharacterPresetKey = string.IsNullOrEmpty(cmc.characterPreset.nameKey)
                    ? cmc.characterPreset.name
                    : cmc.characterPreset.nameKey;
            }
            entry.Team = cmc.Team;
            entry.HideIfFoundEnemyName = NormalizePrefabName(controller.hideIfFoundEnemy ? controller.hideIfFoundEnemy.name : entry.HideIfFoundEnemyName);
            entry.IsVehicle = cmc.isVehicle;
            entry.VehicleAnimationType = cmc.vehicleAnimationType;
            if (entry.IsVehicle)
            {
                var mv = cmc.movementControl;
                if (mv != null)
                {
                    entry.VehicleWalkSpeed = Mathf.Max(0f, mv.walkSpeed);
                    entry.VehicleRunSpeed = Mathf.Max(entry.VehicleWalkSpeed, mv.runSpeed);
                }
            }
            RefreshActiveWeapon(entry, cmc);

            var health = cmc.Health;
            if (health)
            {
                try { entry.MaxHealth = health.MaxHealth; }
                catch { entry.MaxHealth = Mathf.Max(entry.MaxHealth, 1f); }
                try { entry.CurrentHealth = Mathf.Clamp(health.CurrentHealth, 0f, entry.MaxHealth > 0f ? entry.MaxHealth : float.MaxValue); }
                catch { entry.CurrentHealth = entry.MaxHealth; }
                try { entry.BodyArmor = health.BodyArmor; }
                catch { entry.BodyArmor = entry.BodyArmor; }
                try { entry.HeadArmor = health.HeadArmor; }
                catch { entry.HeadArmor = entry.HeadArmor; }
            }
            if (cmc.characterPreset != null)
            {
                entry.ShowHealthBar = cmc.characterPreset.showHealthBar;
            }
            else if (health != null)
            {
                entry.ShowHealthBar = health.showHealthBar;
            }

            entry.Equipment.Clear();
            entry.Weapons.Clear();
            entry.WeaponSnapshots.Clear();

            try
            {
                var slots = cmc.CharacterItem?.Slots;
                if (slots != null)
                {
                    foreach (var slot in slots)
                    {
                        var item = slot?.Content;
                        if (item == null) continue;

                        var key = slot.Key ?? string.Empty;
                        entry.Equipment[key] = item.TypeID;
                        if (IsWeaponSlot(key))
                        {
                            entry.Weapons[key] = item.TypeID;
                            entry.WeaponSnapshots[key] = ItemTool.MakeSnapshot(item);
                        }
                    }
                }
            }
            catch
            {
            }
        }
        catch
        {
        }

        controller.CharacterMainControl.GetBuffManager().Buffs.ForEach(buff =>
        {
            AppendBuffState(entry.Buffs, buff.fromWeaponID, buff.ID);
        });


    }

    private void RefreshEntryPose(AISyncEntry entry)
    {
        if (entry == null) return;
        var controller = entry.Controller;
        if (!controller) return;

        var cmc = controller.CharacterMainControl;
        var modelTransform = cmc && cmc.characterModel ? cmc.characterModel.transform : controller.transform;
        var position = controller.transform.position;
        var rotation = modelTransform ? modelTransform.rotation : controller.transform.rotation;
        var delta = Mathf.Max(Time.deltaTime, 0.0001f);
        var velocity = (position - entry.LastKnownPosition) / delta;

        entry.LastKnownPosition = position;
        entry.LastKnownRotation = rotation;
        entry.LastKnownVelocity = velocity;
        entry.LastAnimSample = CaptureAnimSample(entry, controller);
    }

    private static string TryCaptureFaceJson(CharacterModel model)
    {
        if (!model) return string.Empty;
        try
        {
            var cf = model.CustomFace;
            if (cf == null) return string.Empty;
            var data = cf.ConvertToSaveData();
            var json = JsonUtility.ToJson(data);
            return string.IsNullOrEmpty(json) ? string.Empty : json;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void AttachTracker(AICharacterController controller, AISyncEntry entry)
    {
        if (!controller) return;
        var tracker = controller.GetComponent<AISyncTracker>();
        if (!tracker)
            tracker = controller.gameObject.AddComponent<AISyncTracker>();
        tracker.Initialize(this, controller, entry);
    }

    private int GetDynamicProcessBudget(int baseBudget, int pendingCount)
    {
        if (pendingCount <= baseBudget)
            return baseBudget;

        var overloadRatio = Mathf.Clamp(pendingCount / (float)baseBudget, 1f, 3f);
        return Mathf.Clamp(Mathf.CeilToInt(baseBudget * overloadRatio), baseBudget, baseBudget * 3);
    }

    private void UpdateClientFrameBudgetScale(float deltaTime)
    {
        var unscaledDelta = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : deltaTime;
        var frameDelta = Mathf.Max(unscaledDelta, 0.0001f);
        var targetDelta = 1f / 60f;
        _clientFrameBudgetScale = Mathf.Clamp(frameDelta / targetDelta, 1f, 6f);
    }

    private int ScaleBudgetForDelta(int budget)
    {
        if (_clientFrameBudgetScale <= 1f)
            return budget;

        var divisor = Mathf.Clamp(_clientFrameBudgetScale, 1f, 4f);
        var scaled = Mathf.FloorToInt(budget / divisor);
        return Mathf.Clamp(scaled, 1, budget);
    }

    private void TryWriteAiPerfLog(float now, float deltaTime)
    {
        if (now < _nextPerfLogTime)
            return;

        _nextPerfLogTime = now + PerfLogInterval;

        var totalEntries = CoopSyncDatabase.AI.Count;
        var activeEntries = 0;
        foreach (var entry in CoopSyncDatabase.AI.Entries)
        {
            if (entry != null && entry.Status == AIStatus.Active && entry.Activated)
                activeEntries++;
        }

        var watcherLinks = 0;
        if (IsServer)
        {
            foreach (var watchers in _serverWatchers.Values)
                watcherLinks += watchers?.Count ?? 0;
        }

        CoopPerfLog.AppendAiSample(
            IsServer,
            totalEntries,
            activeEntries,
            _clientReplicas.Count,
            watcherLinks,
            _pendingSnapshotQueue.Count,
            _pendingStateUpdates.Count,
            _perfStatePackets,
            _perfSnapshotPackets,
            _perfHealthPackets,
            deltaTime,
            _clientFrameBudgetScale);

        if (!IsServer)
        {
            CoopPerfLog.AppendEvent(
                "ai-client",
                $"spawnQueue={_clientReplicaSpawnQueue.Count} spawning={_clientReplicaSpawning.Count} vehicleReplicas={CountClientVehicleReplicas()}");
        }

        _perfStatePackets = 0;
        _perfSnapshotPackets = 0;
        _perfHealthPackets = 0;
    }

    private int CountClientVehicleReplicas()
    {
        var count = 0;
        foreach (var id in _clientReplicas.Keys)
        {
            if (CoopSyncDatabase.AI.TryGet(id, out var entry) && entry != null && entry.IsVehicle)
                count++;
        }

        return count;
    }

    private void Client_TryScheduleSnapshotRecovery(bool forceFull)
    {
        if (IsServer || Service == null || !Service.networkStarted)
            return;

        var now = Time.unscaledTime;
        if (now - _lastSnapshotRecoveryTime < SnapshotRecoveryCooldown)
            return;

        _lastSnapshotRecoveryTime = now;
        _snapshotRequested = true;
        Client_SendSnapshotRequest(forceFull);
        _nextSnapshotRefreshTime = now + SnapshotRefreshInterval;
    }

    private void Client_DeferReplicaSpawn(AISyncEntry entry, bool forceFull)
    {
        if (entry == null)
            return;

        _clientReplicaRecoveryTimes[entry.Id] = Time.unscaledTime + Mathf.Max(0.25f, ActivationRetryInterval);
        Client_TryScheduleSnapshotRecovery(forceFull);
    }

    private void BroadcastSpawn(AISyncEntry entry, HashSet<NetPeer> targets)
    {
        if (!IsServer || entry == null) return;

        var descriptor = RpcRegistry.GetDescriptor<AISpawnRpc>();
        var message = new AISpawnRpc { Entry = BuildSnapshotEntry(entry) };
        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);

            if (targets != null && targets.Count > 0)
            {
                foreach (var peer in targets)
                    peer?.Send(writer, descriptor.Delivery);
            }
            else
            {
                Service?.netManager?.SendToAll(writer, descriptor.Delivery);
            }
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private void BroadcastDespawn(AISyncEntry entry)
    {
        if (!IsServer || entry == null) return;

        var descriptor = RpcRegistry.GetDescriptor<AIDespawnRpc>();
        var message = new AIDespawnRpc
        {
            Id = entry.Id,
            Status = entry.Status
        };

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            Service?.netManager?.SendToAll(writer, descriptor.Delivery);
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }

        _serverWatchers.Remove(entry.Id);
        _serverNextWatcherRefresh.Remove(entry.Id);
        _serverAnimatorCache.Remove(entry.Id);
        _serverLastHitPeer.Remove(entry.Id);
        foreach (var kv in _serverLastDamageByPeer.Values)
            kv?.Remove(entry.Id);
    }

    private void BroadcastActivationState(AISyncEntry entry)
    {
        if (!IsServer || entry == null) return;

        var descriptor = RpcRegistry.GetDescriptor<AIActivationStateRpc>();
        var message = new AIActivationStateRpc
        {
            Id = entry.Id,
            Activated = entry.Activated
        };

        var anchor = GetEntryAnchor(entry);
        var watchers = GetServerWatchersInRange(entry, ActivationRadius) ?? EnsureServerWatchers(entry);
        var recipients = _activationRecipients;
        recipients.Clear();
        try
        {
            if (watchers != null)
            {
                foreach (var watcher in watchers)
                {
                    if (watcher != null && watcher.ConnectionState == ConnectionState.Connected &&
                        IsPeerWithinRange(watcher, anchor, ActivationRadius))
                        recipients.Add(watcher);
                }
            }

            var statuses = Service?.playerStatuses;
            if (entry.Activated && statuses != null)
            {
                foreach (var kvp in statuses)
                {
                    var peer = kvp.Key;
                    var status = kvp.Value;
                    if (peer == null || status == null || peer.ConnectionState != ConnectionState.Connected)
                        continue;

                    if (IsPeerWithinRange(peer, anchor, ActivationRadius))
                        recipients.Add(peer);
                }
            }

            if (recipients.Count == 0)
                return;

            var writer = RpcWriterPool.Rent();
            try
            {
                writer.Put((byte)descriptor.Op);
                message.Serialize(writer);
                foreach (var peer in recipients)
                    peer?.Send(writer, descriptor.Delivery);
            }
            finally
            {
                RpcWriterPool.Return(writer);
            }
        }
        finally
        {
            recipients.Clear();
        }
    }

    private HashSet<NetPeer> EnsureServerWatchers(AISyncEntry entry)
    {
        if (!IsServer || entry == null) return null;

        if (!_serverWatchers.TryGetValue(entry.Id, out var watchers) || watchers == null)
        {
            watchers = new HashSet<NetPeer>();
            _serverWatchers[entry.Id] = watchers;
        }

        var statuses = Service?.playerStatuses;
        if (statuses == null || statuses.Count == 0)
            return watchers.Count > 0 ? watchers : null;

        var anchor = GetEntryAnchor(entry);
        _serverNewWatcherBuffer.Clear();

        foreach (var kvp in statuses)
        {
            var peer = kvp.Key;
            var status = kvp.Value;
            if (peer == null || status == null || peer.ConnectionState != ConnectionState.Connected)
                continue;

            if (!IsPeerWithinRange(peer, anchor, ActivationRadius))
                continue;

            if (watchers.Add(peer))
            {
                _serverNewWatcherBuffer.Add(peer);
            }
        }

        if (_serverNewWatcherBuffer.Count > 0)
            BroadcastSpawn(entry, _serverNewWatcherBuffer);
        _serverNewWatcherBuffer.Clear();

        if (watchers.Count == 0)
        {
            _serverWatchers.Remove(entry.Id);
            return null;
        }

        return watchers;
    }

    private bool ShouldRefreshServerWatchers(int id, float now)
    {
        if (id == 0)
            return false;

        if (_serverNextWatcherRefresh.TryGetValue(id, out var next) && now < next)
            return false;

        _serverNextWatcherRefresh[id] = now + ActivationRetryInterval;
        return true;
    }

    private void SendActivationState(AISyncEntry entry, NetPeer peer, bool activated)
    {
        if (!IsServer || entry == null || peer == null || peer.ConnectionState != ConnectionState.Connected)
            return;

        var descriptor = RpcRegistry.GetDescriptor<AIActivationStateRpc>();
        var message = new AIActivationStateRpc
        {
            Id = entry.Id,
            Activated = activated
        };

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            peer.Send(writer, descriptor.Delivery);
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private static Vector3 GetEntryAnchor(AISyncEntry entry)
    {
        return entry?.LastKnownPosition != Vector3.zero ? entry.LastKnownPosition : entry?.SpawnPosition ?? Vector3.zero;
    }

    private static Vector3 GetSnapshotAnchor(in AISnapshotEntry snapshot)
    {
        return snapshot.LastKnownPosition != Vector3.zero ? snapshot.LastKnownPosition : snapshot.SpawnPosition;
    }

    private bool TryGetClientAnchor(out Vector3 anchor)
    {
        var frame = Time.frameCount;
        if (_clientAnchorFrame == frame)
        {
            anchor = _clientAnchor;
            return _clientAnchorValid;
        }

        _clientAnchorFrame = frame;
        _clientAnchor = Vector3.zero;
        _clientAnchorValid = false;
        anchor = Vector3.zero;
        var service = Service;
        var localStatus = service?.localPlayerStatus;
        if (localStatus == null || !localStatus.IsInGame)
            return false;

        _clientAnchor = localStatus.Position;
        _clientAnchorValid = true;
        anchor = _clientAnchor;
        return true;
    }

    private bool IsClientWithinRange(Vector3 target, float range)
    {
        if (!TryGetClientAnchor(out var anchor))
            return true;

        var distSqr = (anchor - target).sqrMagnitude;
        return distSqr <= range * range;
    }

    private bool IsClientWithinStrictRange(Vector3 target, float range)
    {
        if (!TryGetClientAnchor(out var anchor))
            return false;

        var distSqr = (anchor - target).sqrMagnitude;
        return distSqr <= range * range;
    }

    private bool IsClientEntryInRange(AISyncEntry entry, float range)
    {
        return entry == null || IsClientWithinRange(GetEntryAnchor(entry), range);
    }

    private bool IsClientEntryInStrictRange(AISyncEntry entry, float range)
    {
        return entry != null && IsClientWithinStrictRange(GetEntryAnchor(entry), range);
    }

    private bool IsClientEntryInRange(in AISnapshotEntry snapshot, float range)
    {
        return IsClientWithinRange(GetSnapshotAnchor(in snapshot), range);
    }

    private bool IsLocalAuthoritativeVehicle(AISyncEntry entry)
    {
        if (IsServer || entry == null || !entry.IsVehicle)
            return false;

        var frame = Time.frameCount;
        if (_localAuthoritativeVehicleFrame != frame)
        {
            _localAuthoritativeVehicleFrame = frame;
            _localAuthoritativeVehicleId = ResolveLocalAuthoritativeVehicleId();
        }

        return _localAuthoritativeVehicleId != 0 && _localAuthoritativeVehicleId == entry.Id;
    }

    private int ResolveLocalAuthoritativeVehicleId()
    {
        if (IsServer)
            return 0;

        var level = LevelManager.Instance;
        var controlling = level != null ? level.ControllingCharacter : null;
        if (controlling != null && controlling.isVehicle)
        {
            var controllingId = ResolveVehicleId(controlling);
            if (controllingId != 0 &&
                (SendLocalVehicleStatus.Instance == null ||
                 SendLocalVehicleStatus.Instance.IsLocalAuthorityForVehicle(controllingId)))
            {
                return controllingId;
            }
        }

        var service = Service;
        var localStatus = service?.localPlayerStatus;
        if (localStatus == null || !localStatus.IsInGame)
            return 0;

        var mainControl = CharacterMainControl.Main;
        if (mainControl == null)
            return 0;

        var model = mainControl.modelRoot
            ? mainControl.modelRoot.Find("0_CharacterModel_Custom_Template(Clone)")
            : null;
        if (model == null)
            return 0;

        var animCtrl = model.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (animCtrl == null || animCtrl.animator == null)
            return 0;

        if (animCtrl.animator.GetInteger("VehicleType") <= 0)
            return 0;

        var nearestId = FindNearestVehicleId(localStatus.Position, 6f);
        if (nearestId == 0)
            return 0;

        return SendLocalVehicleStatus.Instance == null ||
               SendLocalVehicleStatus.Instance.IsLocalAuthorityForVehicle(nearestId)
            ? nearestId
            : 0;
    }

    private static int ResolveVehicleId(CharacterMainControl vehicle)
    {
        if (vehicle == null)
            return 0;

        foreach (var entry in CoopSyncDatabase.AI.Entries)
        {
            if (entry == null || !entry.IsVehicle || entry.Status == AIStatus.Dead)
                continue;

            var cmc = COOPManager.AI?.TryGetCharacter(entry.Id);
            if (cmc == vehicle)
                return entry.Id;
        }

        return 0;
    }

    private static int FindNearestVehicleId(Vector3 position, float maxDistance)
    {
        var maxSqr = maxDistance * maxDistance;
        var bestId = 0;
        var bestSqr = float.MaxValue;

        foreach (var entry in CoopSyncDatabase.AI.Entries)
        {
            if (entry == null || !entry.IsVehicle || entry.Status == AIStatus.Dead)
                continue;

            var anchor = GetEntryAnchor(entry);
            var distSqr = (anchor - position).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            bestSqr = distSqr;
            bestId = entry.Id;
        }

        return bestId;
    }

    private bool IsPeerWithinRange(NetPeer peer, Vector3 anchor, float range)
    {
        var service = Service;
        var statuses = service?.playerStatuses;
        if (statuses == null || peer == null)
            return false;

        if (!statuses.TryGetValue(peer, out var status) || status == null)
            return false;

        var distSqr = (status.Position - anchor).sqrMagnitude;
        return distSqr <= range * range;
    }

    private HashSet<NetPeer> GetServerWatchers(int id)
    {
        if (!IsServer) return null;
        return _serverWatchers.TryGetValue(id, out var watchers) && watchers != null && watchers.Count > 0
            ? watchers
            : null;
    }

    private HashSet<NetPeer> GetServerWatchersInRange(AISyncEntry entry, float range)
    {
        var watchers = GetServerWatchers(entry.Id);
        if (watchers == null || watchers.Count == 0)
            return null;

        var anchor = GetEntryAnchor(entry);
        _serverWatcherPruneBuffer.Clear();

        foreach (var peer in watchers)
        {
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
            {
                _serverWatcherPruneBuffer.Add(peer);
                continue;
            }

            if (!IsPeerWithinRange(peer, anchor, range))
            {
                SendActivationState(entry, peer, false);
                _serverWatcherPruneBuffer.Add(peer);
            }
        }

        for (var i = 0; i < _serverWatcherPruneBuffer.Count; i++)
            watchers.Remove(_serverWatcherPruneBuffer[i]);

        _serverWatcherPruneBuffer.Clear();

        if (watchers.Count == 0)
        {
            _serverWatchers.Remove(entry.Id);
            return null;
        }

        return watchers;
    }

    private void BroadcastState(AISyncEntry entry, HashSet<NetPeer> watchers = null, bool watchersAlreadyFiltered = false)
    {
        if (!IsServer || entry == null) return;

        if (!watchersAlreadyFiltered)
            watchers = GetServerWatchersInRange(entry, DeactivationRadius) ?? watchers;
        if (watchers == null || watchers.Count == 0)
            return;

        var anchor = GetEntryAnchor(entry);
        var descriptor = RpcRegistry.GetDescriptor<AIStateUpdateRpc>();
        var sample = entry.LastAnimSample;
        var message = new AIStateUpdateRpc
        {
            Id = entry.Id,
            Position = entry.LastKnownPosition,
            Rotation = entry.LastKnownRotation,
            Velocity = entry.LastKnownVelocity,
            CurrentHealth = entry.CurrentHealth,
            StatusOverride = entry.Status,
            MoveSpeed = sample.speed,
            MoveDirX = sample.dirX,
            MoveDirY = sample.dirY,
            IsDashing = sample.dashing,
            IsAttacking = sample.attack,
            HandState = sample.hand,
            GunReady = sample.gunReady,
            ActiveWeaponTypeId = GetActiveWeaponType(entry.Id),
            VehicleType = sample.vehicleType,
            StateHash = sample.stateHash,
            NormTime = sample.normTime,
            Timestamp = Time.unscaledTimeAsDouble
        };

        var signature = ComputeStateSignature(in message);
        if (_lastStateSignatures.TryGetValue(entry.Id, out var lastState) && lastState == signature)
            return;
        _lastStateSignatures[entry.Id] = signature;

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            var sent = 0;
            foreach (var peer in watchers)
            {
                if (peer != null && peer.ConnectionState == ConnectionState.Connected &&
                    (watchersAlreadyFiltered || IsPeerWithinRange(peer, anchor, DeactivationRadius)))
                {
                    peer.Send(writer, descriptor.Delivery);
                    sent++;
                }
            }
            _perfStatePackets += sent;
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private void QueueHealthBroadcast(AISyncEntry entry, DamageForwardPayload? damage, bool force)
    {
        if (!IsServer || entry == null) return;

        var now = Time.unscaledTime;
        _lastHealthBroadcastTime.TryGetValue(entry.Id, out var lastSent);
        var sendAfter = lastSent + HealthBroadcastInterval;

        if (force || now >= sendAfter)
        {
            _pendingHealthBroadcasts.Remove(entry.Id);
            _lastHealthBroadcastTime[entry.Id] = now;
            BroadcastAIHealth(entry, damage);
            return;
        }

        var pending = new PendingHealthBroadcast(
            entry.MaxHealth,
            entry.CurrentHealth,
            entry.BodyArmor,
            entry.HeadArmor,
            entry.Status == AIStatus.Dead,
            damage.HasValue,
            damage,
            sendAfter);

        _pendingHealthBroadcasts[entry.Id] = pending;
    }

    private void BroadcastAIHealth(AISyncEntry entry, DamageForwardPayload? damage = null)
    {
        if (!IsServer || entry == null) return;

        var watchers = GetServerWatchersInRange(entry, DeactivationRadius) ?? EnsureServerWatchers(entry);
        if (watchers == null || watchers.Count == 0)
            return;

        var descriptor = RpcRegistry.GetDescriptor<AIHealthBroadcastRpc>();
        var message = new AIHealthBroadcastRpc
        {
            Id = entry.Id,
            MaxHealth = entry.MaxHealth,
            CurrentHealth = entry.CurrentHealth,
            BodyArmor = entry.BodyArmor,
            HeadArmor = entry.HeadArmor,
            IsDead = entry.Status == AIStatus.Dead,
            HasDamage = damage.HasValue,
            Damage = damage ?? default,
            KillerCredit = false
        };

        var signature = ComputeHealthSignature(in message);
        if (_lastHealthSignatures.TryGetValue(entry.Id, out var lastHealth) && lastHealth == signature)
            return;
        _lastHealthSignatures[entry.Id] = signature;

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            var sent = 0;
            foreach (var watcher in watchers)
            {
                if (watcher == null || watcher.ConnectionState != ConnectionState.Connected)
                    continue;
                watcher.Send(writer, descriptor.Delivery);
                sent++;
            }
            _perfHealthPackets += sent;
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private void NotifyKillerOfServerDeath(AISyncEntry entry)
    {
        if (!IsServer || entry == null) return;

        if (!_serverLastHitPeer.TryGetValue(entry.Id, out var peer) || peer == null)
            return;

        if (!_serverLastDamageByPeer.TryGetValue(peer, out var lastDamage) || lastDamage == null ||
            !lastDamage.TryGetValue(entry.Id, out var payload))
            return;

        var descriptor = RpcRegistry.GetDescriptor<AIHealthBroadcastRpc>();
        var message = new AIHealthBroadcastRpc
        {
            Id = entry.Id,
            MaxHealth = entry.MaxHealth,
            CurrentHealth = entry.CurrentHealth,
            BodyArmor = entry.BodyArmor,
            HeadArmor = entry.HeadArmor,
            IsDead = true,
            HasDamage = true,
            Damage = payload,
            KillerCredit = true
        };

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            peer.Send(writer, descriptor.Delivery);
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private void BroadcastAIBuff(AISyncEntry entry, int weaponTypeId, int buffId)
    {
        if (!IsServer || entry == null || buffId == 0) return;

        AppendBuffState(entry.Buffs, weaponTypeId, buffId);

        var descriptor = RpcRegistry.GetDescriptor<AIBuffBroadcastRpc>();
        var message = new AIBuffBroadcastRpc
        {
            Id = entry.Id,
            WeaponTypeId = weaponTypeId,
            BuffId = buffId
        };

        var watchers = GetServerWatchersInRange(entry, DeactivationRadius);
        if (watchers == null || watchers.Count == 0)
            return;

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            foreach (var watcher in watchers)
                watcher?.Send(writer, descriptor.Delivery);
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private void ApplyHealthToController(AISyncEntry entry)
    {
        if (entry == null) return;
        var controller = entry.Controller;
        var cmc = controller ? controller.CharacterMainControl : null;
        var health = cmc ? cmc.Health : null;
        if (health)
            HealthM.Instance.ForceSetHealth(
                health,
                entry.MaxHealth > 0f ? entry.MaxHealth : Mathf.Max(1f, entry.CurrentHealth),
                entry.CurrentHealth,
                true,
                entry.BodyArmor,
                entry.HeadArmor);
    }

    private bool ApplyDamageToController(AISyncEntry entry, DamageForwardPayload? damage, CharacterMainControl attacker)
    {
        if (entry == null || !damage.HasValue)
            return false;

        var controller = entry.Controller;
        var cmc = controller ? controller.CharacterMainControl : null;
        var health = cmc ? cmc.Health : null;
        if (!health)
            return false;

        var receiver = cmc ? cmc.mainDamageReceiver : null;
        if (!receiver && cmc)
            receiver = cmc.GetComponentInChildren<DamageReceiver>(true);

        // 确保传给 AI 的伤害信息里有攻击者和命中点，避免行为树里因空引用而报错
        var info = damage.Value.ToDamageInfo(attacker ?? cmc, receiver);
        if (info.toDamageReceiver == null)
            info.toDamageReceiver = receiver;
        if (info.fromCharacter == null)
            info.fromCharacter = attacker ?? cmc;

        try
        {
            IsHostHurt = true;
            health.Hurt(info);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private CharacterMainControl ResolveAttacker(RpcContext context)
    {
        if ( context.Sender == null)
            return null;

        try
        {
            var cmc = CoopTool.TryGetRemoteCharacterForPeer(context.Sender);
            if (cmc)
                return cmc;
        }
        catch
        {
        }

        try
        {
            var service = Service;
            if (service == null) return null;

            if (service.playerStatuses != null && service.playerStatuses.TryGetValue(context.Sender, out var st) && st != null)
            {
                if (service.remoteCharacters != null && service.remoteCharacters.TryGetValue(context.Sender, out var go) && go)
                {
                    var remoteCmc = go.GetComponent<CharacterMainControl>() ?? go.GetComponentInChildren<CharacterMainControl>(true);
                    if (remoteCmc)
                        return remoteCmc;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private bool EnsureServerControllerDeath(AISyncEntry entry, DamageForwardPayload? damage, CharacterMainControl attacker)
    {
        if (entry == null) return false;
        var controller = entry.Controller;
        var cmc = controller ? controller.CharacterMainControl : null;
        var health = cmc ? cmc.Health : null;
        if (!health) return false;

        float current;
        try
        {
            current = health.CurrentHealth;
        }
        catch
        {
            current = 0f;
        }

        if (current <= 0.0001f)
            return false;

        DamageInfo info;
        if (damage.HasValue)
        {
            var receiver = cmc ? cmc.mainDamageReceiver : null;
            if (!receiver && cmc)
                receiver = cmc.GetComponentInChildren<DamageReceiver>(true);

            info = damage.Value.ToDamageInfo(attacker, receiver);
            if (info.toDamageReceiver == null)
                info.toDamageReceiver = receiver;
            if (info.fromCharacter == null)
                info.fromCharacter = attacker;
        }
        else
        {
            info = new DamageInfo(attacker)
            {
                damageValue = Mathf.Max(1f, current),
                finalDamage = Mathf.Max(1f, current),
                damagePoint = cmc ? cmc.transform.position : Vector3.zero,
                damageNormal = Vector3.up
            };
        }

        try
        {
            health.OnDeadEvent?.Invoke(info);
            RaiseStaticHealthOnDead(health, info);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ForceDisableServerController(AISyncEntry entry)
    {
        if (entry == null) return;
        var controller = entry.Controller;
        if (!controller) return;
        try
        {
            controller.enabled = false;
        }
        catch
        {
        }

        var cmc = controller.CharacterMainControl;
        if (!cmc) return;
        try
        {
            cmc.gameObject.SetActive(false);
        }
        catch
        {
        }
    }

    private void SendSnapshotChunk(NetPeer peer, List<AISnapshotEntry> buffer, bool reset)
    {
        if (!IsServer || peer == null || buffer == null || buffer.Count == 0) return;

        var descriptor = RpcRegistry.GetDescriptor<AISnapshotChunkRpc>();
        var message = new AISnapshotChunkRpc
        {
            Reset = reset,
            EntryList = buffer,
            EntryCount = buffer.Count
        };

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            peer.Send(writer, descriptor.Delivery);
            _perfSnapshotPackets++;
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private void SendSnapshotChunkToPeers(List<AISnapshotEntry> buffer, bool reset)
    {
        if (!IsServer || buffer == null || buffer.Count == 0) return;
        var service = Service;
        var statuses = service?.playerStatuses;
        if (statuses == null || statuses.Count == 0) return;

        foreach (var peer in statuses.Keys)
        {
            if (peer == null || peer.ConnectionState != ConnectionState.Connected) continue;
            SendSnapshotChunk(peer, buffer, reset);
        }
    }

    private bool Server_BroadcastSnapshotToAll(bool reset)
    {
        if (!IsServer) return false;
        var service = Service;
        var statuses = service?.playerStatuses;
        if (statuses == null || statuses.Count == 0) return false;

        var sent = false;
        try
        {
            _snapshotBuffer.Clear();
            foreach (var entry in CoopSyncDatabase.AI.Entries)
            {
                if (entry == null) continue;

                var snapshot = BuildSnapshotEntry(entry);
                var duplicate = HasDuplicateSnapshot(entry.Id, in snapshot);
                if (!reset && duplicate)
                    continue;

                _snapshotBuffer.Add(snapshot);
                if (_snapshotBuffer.Count >= SnapshotChunkSize)
                {
                    SendSnapshotChunkToPeers(_snapshotBuffer, reset);
                    reset = false;
                    _snapshotBuffer.Clear();
                    sent = true;
                }
            }

            if (_snapshotBuffer.Count > 0)
            {
                SendSnapshotChunkToPeers(_snapshotBuffer, reset);
                sent = true;
            }
        }
        finally
        {
            _snapshotBuffer.Clear();
        }

        return sent;
    }

    private void Server_RescanActiveControllers()
    {
        if (!IsServer) return;

        AICharacterController[] controllers;
        try
        {
            controllers = Object.FindObjectsOfType<AICharacterController>(true);
        }
        catch
        {
            return;
        }

        if (controllers == null || controllers.Length == 0) return;

        for (var i = 0; i < controllers.Length; i++)
        {
            var controller = controllers[i];
            if (!controller) continue;

            if (!CoopSyncDatabase.AI.TryGet(controller, out var entry) || entry == null)
            {
                entry = CoopSyncDatabase.AI.RegisterController(controller);
                if (entry == null) continue;
            }
            else if (!entry.Controller)
            {
                entry.Controller = controller;
            }

            PopulateEntryMetadata(entry, controller);
        }
    }

    private AISnapshotEntry BuildSnapshotEntry(AISyncEntry entry)
    {
        if (entry != null && string.IsNullOrEmpty(entry.ModelName) && entry.Controller)
            PopulateEntryMetadata(entry, entry.Controller);

        var snapshot = new AISnapshotEntry
        {
            Id = entry.Id,
            SceneBuildIndex = entry.SceneBuildIndex,
            ScenePath = entry.ScenePath ?? string.Empty,
            SpawnPosition = entry.SpawnPosition,
            SpawnRotation = entry.SpawnRotation,
            ModelName = entry.ModelName ?? string.Empty,
            CustomFaceJson = entry.CustomFaceJson ?? string.Empty,
            CharacterPresetKey = entry.CharacterPresetKey ?? string.Empty,
            HideIfFoundEnemyName = entry.HideIfFoundEnemyName ?? string.Empty,
            Status = entry.Status,
            Activated = entry.Activated,
            Team = entry.Team,
            MaxHealth = entry.MaxHealth,
            CurrentHealth = entry.CurrentHealth,
            BodyArmor = entry.BodyArmor,
            HeadArmor = entry.HeadArmor,
            ShowHealthBar = entry.ShowHealthBar,
            IsVehicle = entry.IsVehicle,
            VehicleAnimationType = entry.VehicleAnimationType,
            VehicleWalkSpeed = entry.VehicleWalkSpeed,
            VehicleRunSpeed = entry.VehicleRunSpeed,
            ActiveWeaponSlot = GetActiveWeaponSlot(entry.Id),
            ActiveWeaponTypeId = GetActiveWeaponType(entry.Id),
            LastKnownPosition = entry.LastKnownPosition != Vector3.zero ? entry.LastKnownPosition : entry.SpawnPosition,
            LastKnownRotation = entry.LastKnownRotation != Quaternion.identity ? entry.LastKnownRotation : entry.SpawnRotation,
            LastKnownVelocity = entry.LastKnownVelocity,
            LastKnownRemoteTime = entry.LastKnownRemoteTime > 0d ? entry.LastKnownRemoteTime : Time.unscaledTimeAsDouble
        };

        if (entry.Equipment.Count > 0)
        {
            snapshot.EquipmentSlots = new string[entry.Equipment.Count];
            snapshot.EquipmentItemTypeIds = new int[entry.Equipment.Count];
            var index = 0;
            foreach (var kv in entry.Equipment)
            {
                snapshot.EquipmentSlots[index] = kv.Key;
                snapshot.EquipmentItemTypeIds[index] = kv.Value;
                index++;
            }
        }
        else
        {
            snapshot.EquipmentSlots = Array.Empty<string>();
            snapshot.EquipmentItemTypeIds = Array.Empty<int>();
        }

        if (entry.Weapons.Count > 0)
        {
            snapshot.WeaponSlots = new string[entry.Weapons.Count];
            snapshot.WeaponItemTypeIds = new int[entry.Weapons.Count];
            snapshot.WeaponItemSnapshots = new ItemSnapshot[entry.Weapons.Count];
            var index = 0;
            foreach (var kv in entry.Weapons)
            {
                snapshot.WeaponSlots[index] = kv.Key;
                snapshot.WeaponItemTypeIds[index] = kv.Value;
                if (entry.WeaponSnapshots.TryGetValue(kv.Key, out var snap))
                    snapshot.WeaponItemSnapshots[index] = snap;
                index++;
            }
        }
        else
        {
            snapshot.WeaponSlots = Array.Empty<string>();
            snapshot.WeaponItemTypeIds = Array.Empty<int>();
            snapshot.WeaponItemSnapshots = Array.Empty<ItemSnapshot>();
        }

        if (entry.Buffs.Count > 0)
        {
            snapshot.BuffWeaponTypeIds = new int[entry.Buffs.Count];
            snapshot.BuffIds = new int[entry.Buffs.Count];
            for (var i = 0; i < entry.Buffs.Count; i++)
            {
                snapshot.BuffWeaponTypeIds[i] = entry.Buffs[i].WeaponTypeId;
                snapshot.BuffIds[i] = entry.Buffs[i].BuffId;
            }
        }
        else
        {
            snapshot.BuffWeaponTypeIds = Array.Empty<int>();
            snapshot.BuffIds = Array.Empty<int>();
        }

        return snapshot;
    }

    private bool HasDuplicateSnapshot(int id, in AISnapshotEntry snapshot)
    {
        var signature = ComputeSnapshotSignature(in snapshot);
        if (_lastSnapshotSignatures.TryGetValue(id, out var last) && last == signature)
            return true;

        _lastSnapshotSignatures[id] = signature;
        return false;
    }

    private static int ComputeSnapshotSignature(in AISnapshotEntry snapshot)
    {
        var hash = new HashCode();
        hash.Add(snapshot.Id);
        hash.Add(snapshot.SceneBuildIndex);
        hash.Add(snapshot.ScenePath);
        hash.Add(snapshot.SpawnPosition);
        hash.Add(snapshot.SpawnRotation);
        hash.Add(snapshot.ModelName);
        hash.Add(snapshot.CustomFaceJson);
        hash.Add(snapshot.CharacterPresetKey);
        hash.Add(snapshot.HideIfFoundEnemyName);
        hash.Add(snapshot.Status);
        hash.Add(snapshot.Activated);
        hash.Add(snapshot.Team);
        hash.Add(snapshot.MaxHealth);
        hash.Add(snapshot.CurrentHealth);
        hash.Add(snapshot.BodyArmor);
        hash.Add(snapshot.HeadArmor);
        hash.Add(snapshot.ShowHealthBar);
        hash.Add(snapshot.IsVehicle);
        hash.Add(snapshot.VehicleAnimationType);
        hash.Add(snapshot.VehicleWalkSpeed);
        hash.Add(snapshot.VehicleRunSpeed);
        hash.Add(snapshot.ActiveWeaponSlot);
        hash.Add(snapshot.ActiveWeaponTypeId);
        hash.Add(snapshot.LastKnownPosition);
        hash.Add(snapshot.LastKnownRotation);
        hash.Add(snapshot.LastKnownVelocity);

        if (snapshot.EquipmentSlots != null && snapshot.EquipmentItemTypeIds != null)
        {
            var length = Math.Min(snapshot.EquipmentSlots.Length, snapshot.EquipmentItemTypeIds.Length);
            for (var i = 0; i < length; i++)
            {
                hash.Add(snapshot.EquipmentSlots[i]);
                hash.Add(snapshot.EquipmentItemTypeIds[i]);
            }
        }

        if (snapshot.WeaponSlots != null && snapshot.WeaponItemTypeIds != null)
        {
            var length = Math.Min(snapshot.WeaponSlots.Length, snapshot.WeaponItemTypeIds.Length);
            for (var i = 0; i < length; i++)
            {
                hash.Add(snapshot.WeaponSlots[i]);
                hash.Add(snapshot.WeaponItemTypeIds[i]);
            }
        }

        if (snapshot.WeaponItemSnapshots != null && snapshot.WeaponItemSnapshots.Length > 0)
        {
            foreach (var snap in snapshot.WeaponItemSnapshots)
            {
                hash.Add(ItemTool.ComputeSnapshotHash(snap));
            }
        }

        if (snapshot.BuffWeaponTypeIds != null && snapshot.BuffIds != null)
        {
            var length = Math.Min(snapshot.BuffWeaponTypeIds.Length, snapshot.BuffIds.Length);
            for (var i = 0; i < length; i++)
            {
                hash.Add(snapshot.BuffWeaponTypeIds[i]);
                hash.Add(snapshot.BuffIds[i]);
            }
        }

        return hash.ToHashCode();
    }

    private static int ComputeStateSignature(in AIStateUpdateRpc message)
    {
        var hash = new HashCode();
        hash.Add(message.Id);
        hash.Add(message.Position);
        hash.Add(message.Rotation);
        hash.Add(message.Velocity);
        hash.Add(message.CurrentHealth);
        hash.Add(message.StatusOverride);
        hash.Add(message.MoveSpeed);
        hash.Add(message.MoveDirX);
        hash.Add(message.MoveDirY);
        hash.Add(message.IsDashing);
        hash.Add(message.IsAttacking);
        hash.Add(message.HandState);
        hash.Add(message.GunReady);
        hash.Add(message.ActiveWeaponTypeId);
        hash.Add(message.VehicleType);
        hash.Add(message.StateHash);
        hash.Add(message.NormTime);
        return hash.ToHashCode();
    }

    private static int ComputeHealthSignature(in AIHealthBroadcastRpc message)
    {
        var hash = new HashCode();
        hash.Add(message.Id);
        hash.Add(message.MaxHealth);
        hash.Add(message.CurrentHealth);
        hash.Add(message.BodyArmor);
        hash.Add(message.HeadArmor);
        hash.Add(message.IsDead);
        hash.Add(message.HasDamage);
        hash.Add(message.KillerCredit);
        if (message.HasDamage)
        {
            var damage = message.Damage;
            hash.Add(damage.DamageValue);
            hash.Add(damage.ArmorPiercing);
            hash.Add(damage.CritDamageFactor);
            hash.Add(damage.CritRate);
            hash.Add(damage.Crit);
            hash.Add(damage.HitPoint);
            hash.Add(damage.HitNormal);
            hash.Add(damage.WeaponItemId);
            hash.Add(damage.BleedChance);
            hash.Add(damage.IsExplosion);
        }

        return hash.ToHashCode();
    }

    private void ApplySnapshot(AISyncEntry entry, in AISnapshotEntry snapshot)
    {
        entry.Id = snapshot.Id;
        entry.SpawnPosition = snapshot.SpawnPosition;
        entry.SpawnRotation = snapshot.SpawnRotation;
        if (!string.IsNullOrEmpty(snapshot.ModelName))
            entry.ModelName = NormalizePrefabName(snapshot.ModelName);
        entry.CustomFaceJson = snapshot.CustomFaceJson;
        if (!string.IsNullOrEmpty(snapshot.CharacterPresetKey))
            entry.CharacterPresetKey = snapshot.CharacterPresetKey;
        entry.HideIfFoundEnemyName = snapshot.HideIfFoundEnemyName;
        entry.SceneBuildIndex = snapshot.SceneBuildIndex;
        entry.ScenePath = snapshot.ScenePath;
        entry.Team = snapshot.Team;
        var forceSpawn = IsForceSpawnModel(snapshot.ModelName) || snapshot.IsVehicle;
        entry.Status = forceSpawn && snapshot.Status != AIStatus.Dead
            ? AIStatus.Active
            : snapshot.Status;
        entry.Activated = forceSpawn || snapshot.Activated;
        if (entry.Status == AIStatus.Dead)
            entry.Activated = false;
        entry.MaxHealth = snapshot.MaxHealth;
        entry.CurrentHealth = snapshot.CurrentHealth;
        entry.BodyArmor = snapshot.BodyArmor;
        entry.HeadArmor = snapshot.HeadArmor;
        entry.ShowHealthBar = snapshot.ShowHealthBar;
        entry.IsVehicle = snapshot.IsVehicle;
        entry.VehicleAnimationType = snapshot.VehicleAnimationType;
        entry.VehicleWalkSpeed = snapshot.VehicleWalkSpeed;
        entry.VehicleRunSpeed = snapshot.VehicleRunSpeed;
        SetActiveWeapon(entry.Id, snapshot.ActiveWeaponSlot, snapshot.ActiveWeaponTypeId);
        entry.LastKnownPosition = snapshot.LastKnownPosition != Vector3.zero ? snapshot.LastKnownPosition : snapshot.SpawnPosition;
        entry.LastKnownRotation = snapshot.LastKnownRotation != Quaternion.identity ? snapshot.LastKnownRotation : snapshot.SpawnRotation;
        entry.LastKnownVelocity = snapshot.LastKnownVelocity;
        entry.LastKnownRemoteTime = snapshot.LastKnownRemoteTime > 0d ? snapshot.LastKnownRemoteTime : Time.unscaledTimeAsDouble;
        entry.LastAnimSample = default;
        entry.PositionKey = AISyncRegistry.ComputePositionKey(entry.SpawnPosition, entry.SceneBuildIndex, entry.ScenePath);

        entry.Equipment.Clear();
        if (snapshot.EquipmentSlots != null && snapshot.EquipmentItemTypeIds != null)
        {
            var count = Math.Min(snapshot.EquipmentSlots.Length, snapshot.EquipmentItemTypeIds.Length);
            for (var i = 0; i < count; i++)
            {
                var slot = snapshot.EquipmentSlots[i];
                if (string.IsNullOrEmpty(slot)) continue;
                entry.Equipment[slot] = snapshot.EquipmentItemTypeIds[i];
            }
        }

        entry.Weapons.Clear();
        entry.WeaponSnapshots.Clear();
        if (snapshot.WeaponSlots != null && snapshot.WeaponItemTypeIds != null)
        {
            var count = Math.Min(snapshot.WeaponSlots.Length, snapshot.WeaponItemTypeIds.Length);
            for (var i = 0; i < count; i++)
            {
                var slot = snapshot.WeaponSlots[i];
                if (string.IsNullOrEmpty(slot)) continue;
                entry.Weapons[slot] = snapshot.WeaponItemTypeIds[i];
            }
        }

        if (snapshot.WeaponSlots != null && snapshot.WeaponItemSnapshots != null)
        {
            var count = Math.Min(snapshot.WeaponSlots.Length, snapshot.WeaponItemSnapshots.Length);
            for (var i = 0; i < count; i++)
            {
                var slot = snapshot.WeaponSlots[i];
                if (string.IsNullOrEmpty(slot)) continue;
                var snap = snapshot.WeaponItemSnapshots[i];
                if (snap.TypeId != 0)
                    entry.WeaponSnapshots[slot] = snap;
            }
        }

        entry.Buffs.Clear();
        if (snapshot.BuffWeaponTypeIds != null && snapshot.BuffIds != null)
        {
            var count = Math.Min(snapshot.BuffWeaponTypeIds.Length, snapshot.BuffIds.Length);
            for (var i = 0; i < count; i++)
            {
                var buffId = snapshot.BuffIds[i];
                if (buffId == 0) continue;
                AppendBuffState(entry.Buffs, snapshot.BuffWeaponTypeIds[i], buffId);
            }
        }
        if (entry.Buffs.Count > MaxStoredBuffs)
            entry.Buffs.RemoveRange(0, entry.Buffs.Count - MaxStoredBuffs);
    }

    void MultiplyCharacterStat(Item characterItemInstance, string statName, float multiplier)
    {
        Stat stat = characterItemInstance.GetStat(statName.GetHashCode());
        if (stat != null)
        {
            stat.BaseValue *= multiplier;
        }
    }

    private async UniTaskVoid SpawnReplicaAsync(AISyncEntry entry)
    {
        var spawnId = entry?.Id ?? 0;
        try
        {
            if (entry == null) return;
            if (!ShouldHaveClientReplica(entry)) return;

            DestroyReplica(entry.Id);

            //Debug.Log($"[AI][CLIENT] SpawnReplica id={entry.Id} model={entry.ModelName} preset={entry.CharacterPresetKey} isVehicle={entry.IsVehicle} vehicleAnim={entry.VehicleAnimationType}");

            if (MultiSceneCore.Instance.SceneInfo.ID == "Base" && !string.IsNullOrEmpty(entry.HideIfFoundEnemyName))
            {
                return;
            }

            var prefab = ResolveModelPrefab(entry.ModelName);
            if (!prefab)
            {
                prefab = ResolveModelFromPresetKey(entry.CharacterPresetKey);
            }

            if (!prefab)
            {
                LogMissingModelPrefab(entry);
                Client_DeferReplicaSpawn(entry, true);
                return;
            }

            var host = CharacterMainControl.Main;
            if (!prefab || !host) return;

            var characterItemInstance = await Coopbase.LoadOrCreateCharacterItemInstance();
            if (!ShouldHaveClientReplica(entry)) return;
           // var modelInstance = Object.Instantiate(prefab);
            //var instance = Object.Instantiate(host.gameObject, entry.SpawnPosition, entry.SpawnRotation);
            var characterMainControl = await LevelManager.Instance.CharacterCreator.CreateCharacter(characterItemInstance, prefab, entry.SpawnPosition, entry.SpawnRotation);
            var instance = characterMainControl ? characterMainControl.gameObject : null;
            if (!instance) return;
            instance.SetActive(false);
            if (!ShouldHaveClientReplica(entry))
            {
                if (instance)
                    Object.Destroy(instance);
                return;
            }
            instance.name = $"RemoteAI_{entry.Id}_{prefab.name}";

            var aiTag = instance.GetComponent<RemoteAIReplicaTag>();
            if (!aiTag)
                aiTag = instance.AddComponent<RemoteAIReplicaTag>();
            aiTag.Id = entry.Id;

            var cmc = instance.GetComponent<CharacterMainControl>();
            if (!cmc)
            {
                Object.Destroy(instance);
                return;
            }

          //  cmc.SetCharacterModel(modelInstance);
            if (cmc.characterModel)
                cmc.characterModel.characterMainControl = cmc;

            if (characterItemInstance)
                cmc.SetItem(characterItemInstance);

            ApplyCharacterPreset(cmc, entry.CharacterPresetKey);
            TryApplyVehicleSpeedStats(characterItemInstance, entry);
           // MultiplyCharacterStat(characterItemInstance,"WalkSpeed",cmc.characterPreset.moveSpeedFactor);
           // MultiplyCharacterStat(characterItemInstance,"RunSpeed", cmc.characterPreset.moveSpeedFactor);
            
            ApplyVehicleState(cmc, entry);
            RPCVehicle.TryApplyCachedVehicleItemState(entry.Id, cmc);
            ApplySpecialAttachments(cmc,entry);
            cmc.SetTeam(entry.Team);
            MakeReplicaPassive(cmc);
            TryAttachHideIfFoundEnemyReplica(entry, instance, cmc);

            var modelRoot = cmc.characterModel;
            if (modelRoot != null && modelRoot.name.Contains("0_CharacterModel_Custom_Enemy_Invisable"))
            {
                modelRoot.gameObject.SetActive(false);
                entry.ShowHealthBar = false;
            }
            
            ApplyCustomFace(cmc.characterModel, entry.CustomFaceJson);
            await ApplyEquipmentAsync(cmc.characterModel, entry.Equipment, entry.Weapons, entry.WeaponSnapshots, GetActiveWeaponSlot(entry.Id), GetActiveWeaponType(entry.Id));
            TryApplyVehicleSpeedStats(characterItemInstance, entry);

           // MultiplyCharacterStat(characterItemInstance, "WalkSpeed", cmc.characterPreset.moveSpeedFactor);
           // MultiplyCharacterStat(characterItemInstance, "RunSpeed", cmc.characterPreset.moveSpeedFactor);

            var health = cmc.Health;
            if (health)
            {
                health.autoInit = false;
                health.showHealthBar = entry.ShowHealthBar;
                HealthTool.BindHealthToCharacter(health, cmc);
                var maxHealth = entry.MaxHealth <= 0f ? Mathf.Max(entry.MaxHealth, entry.CurrentHealth) : entry.MaxHealth;

                if (characterItemInstance != null)
                {
                    try
                    {
                        var stat = characterItemInstance.GetStat("MaxHealth".GetHashCode());
                        if (stat != null)
                        {
                            var rule = LevelManager.Rule;
                            var factor = rule != null ? rule.EnemyHealthFactor : 1f;
                            stat.BaseValue = maxHealth;
                        }
                        characterItemInstance.SetInt("Exp", cmc.characterPreset.exp);
                    }
                    catch
                    {
                    }
                }
                HealthM.Instance.ForceSetHealth(health, maxHealth, entry.CurrentHealth, true, entry.BodyArmor, entry.HeadArmor);
            }

            if (entry.ShowHealthBar)
            {
                if (!instance.GetComponent<AutoRequestHealthBar>())
                    instance.AddComponent<AutoRequestHealthBar>();
            }
            else
            {
                var autoRequest = instance.GetComponent<AutoRequestHealthBar>();
                if (autoRequest)
                    Object.Destroy(autoRequest);
            }

            if(entry.IsVehicle)
            {
                cmc.Health.showHealthBar = false;
            }
            
            var replica = new RemoteAIReplica(entry.Id, instance, cmc, entry, this);
            _clientReplicas[entry.Id] = replica;
            replica.ApplyState(entry);
            // Buffs are intentionally not applied at spawn; they will be received via live broadcasts.
            cmc.gameObject.SetActive(false);
            cmc.gameObject.SetActive(true);
            ModApiEvents.RaiseAiSpawned(entry.Id, cmc);
            if (entry.IsVehicle)
                LevelDataBoolNet.TryApplyCachedVehicleRequiredItems(entry.Id, cmc);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AI][CLIENT] Spawn replica failed: {ex}");
        }
        finally
        {
            if (spawnId != 0)
                _clientReplicaSpawning.Remove(spawnId);
        }
    }

    private void TryApplyVehicleSpeedStats(Item characterItemInstance, AISyncEntry entry)
    {
        if (characterItemInstance == null || entry == null || !entry.IsVehicle)
            return;

        var walk = Mathf.Max(0f, entry.VehicleWalkSpeed);
        var run = Mathf.Max(walk, entry.VehicleRunSpeed);
        if (walk <= 0f && run <= 0f)
            return;

        try
        {
            var walkStat = characterItemInstance.GetStat("WalkSpeed".GetHashCode());
            if (walkStat != null && walk > 0f && walkStat.BaseValue > 0f)
            {
                var walkMultiplier = walk / walkStat.BaseValue;
                MultiplyCharacterStat(characterItemInstance, "WalkSpeed", walkMultiplier);
            }

            var runStat = characterItemInstance.GetStat("RunSpeed".GetHashCode());
            if (runStat != null && run > 0f && runStat.BaseValue > 0f)
            {
                var runMultiplier = run / runStat.BaseValue;
                MultiplyCharacterStat(characterItemInstance, "RunSpeed", runMultiplier);
            }
        }
        catch
        {
        }
    }

    private void DestroyReplica(int id)
    {
        if (_clientReplicas.TryGetValue(id, out var replica))
        {
            DestroyReplica(replica);
            _clientReplicas.Remove(id);
        }
    }

    private void DestroyReplica(RemoteAIReplica replica)
    {
        if (replica == null) return;

        var hasDeathInfo = false;
        if (CoopSyncDatabase.AI.TryGet(replica.Id, out var entry) && entry != null)
            hasDeathInfo = entry.Status == AIStatus.Dead;

        if (hasDeathInfo && _clientLastDamage.TryGetValue(replica.Id, out var lastDamage))
        {
           // Debug.Log($"[AI][CLIENT] Forcing death event on replica {replica.Id}");
            var cmc = replica.Character;
            var health = cmc ? cmc.Health : null;
            if (health)
            {
                var receiver = cmc ? cmc.mainDamageReceiver : null;
                if (!receiver && cmc)
                    receiver = cmc.GetComponentInChildren<DamageReceiver>(true);
              //  Debug.Log($"[AI][CLIENT] Replica {replica.Id} has receiver: {(receiver != null)}");
                var info = lastDamage;
               // Debug.Log($"[AI][CLIENT] DamageInfo for replica {replica.Id}: DamageValue={info.damageValue}, FinalDamage={info.finalDamage} crit：{info.crit}  fromCharacter:{info.fromCharacter != null} fromWeaponItemID:{info.fromWeaponItemID}");
                info.damageValue = 99999f;
                info.finalDamage = 99999f;
                if (info.toDamageReceiver == null)
                    info.toDamageReceiver = receiver;
                if (info.fromCharacter == null && CharacterMainControl.Main)
                    info.fromCharacter = CharacterMainControl.Main;
                Client_RaiseQuestKillCredit(replica.Id, health, info);
               // Debug.Log($"[AI][CLIENT] Invoking OnDeadEvent for replica {replica.Id}");
                try
                {
                    health.OnDeadEvent?.Invoke(info);
                }
                catch
                {
                }
            }

            _clientLastDamage.Remove(replica.Id);
        }

        replica.Dispose();
        if (replica.Instance)
            Object.Destroy(replica.Instance);
    }

    private void Client_RaiseQuestKillCredit(int id, in AIHealthBroadcastRpc message)
    {
        if (!message.HasDamage)
            return;
        if (!_clientReplicas.TryGetValue(id, out var replica) || replica == null)
            return;

        var cmc = replica.Character;
        var health = cmc ? cmc.Health : null;
        if (!health)
            return;

        var receiver = cmc ? cmc.mainDamageReceiver : null;
        if (!receiver && cmc)
            receiver = cmc.GetComponentInChildren<DamageReceiver>(true);

        var attacker = CharacterMainControl.Main;
        if (!attacker)
            return;

        var info = message.Damage.ToDamageInfo(attacker, receiver);
        if (info.toDamageReceiver == null)
            info.toDamageReceiver = receiver;
        if (info.fromCharacter == null)
            info.fromCharacter = attacker;

        _clientLastDamage[id] = info;
        Client_RaiseQuestKillCredit(id, health, info);
    }

    private bool Client_RaiseQuestKillCredit(int id, Health health, DamageInfo info)
    {
        if (IsServer || !health || !CharacterMainControl.Main)
            return false;

        if (info.fromCharacter == null)
            info.fromCharacter = CharacterMainControl.Main;
        if (info.fromCharacter != CharacterMainControl.Main)
            return false;
        if (!_clientQuestKillCreditRaised.Add(id))
            return false;

        RaiseStaticHealthOnDead(health, info);
        return true;
    }

    private static void RaiseStaticHealthOnDead(Health health, DamageInfo info)
    {
        if (!health)
            return;

        try
        {
            if (HealthOnDeadEventField?.GetValue(null) is Action<Health, DamageInfo> handler)
                handler.Invoke(health, info);
        }
        catch
        {
        }
    }

    private static bool IsControllerActivated(AICharacterController controller, CharacterMainControl cmc)
    {
        if (!controller || !cmc) return false;
        return controller.isActiveAndEnabled
               && controller.gameObject.activeInHierarchy
               && cmc.isActiveAndEnabled
               && cmc.gameObject.activeInHierarchy;
    }

    public static bool IsForceSpawnModel(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return false;

        modelName = NormalizePrefabName(modelName);
        return string.Equals(modelName, "_Boss_Roadblock", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizePrefabName(string n)
    {
        if (string.IsNullOrEmpty(n)) return n;
        n = n.Trim();
        const string clone = "(Clone)";
        if (n.EndsWith(clone, StringComparison.Ordinal))
            n = n.Substring(0, n.Length - clone.Length).Trim();
        return n;
    }

    private static bool ShouldKeepReplicaAlive(AISyncEntry entry)
    {
        return entry != null && !string.IsNullOrEmpty(entry.HideIfFoundEnemyName);
    }

    private static (float max, float cur, float bodyArmor, float headArmor) ReadHealthSafe(Health health)
    {
        float max = 0f, cur = 0f, body = 0f, head = 0f;
        try { max = health.MaxHealth; }
        catch { }

        try { cur = health.CurrentHealth; }
        catch { }

        try { body = health.BodyArmor; }
        catch { }

        try { head = health.HeadArmor; }
        catch { }

        return (max, cur, body, head);
    }

    private static bool ContainsBuff(List<AIBuffState> list, int weaponTypeId, int buffId)
    {
        if (list == null || buffId == 0) return false;
        for (var i = 0; i < list.Count; i++)
        {
            var buff = list[i];
            if (buff.BuffId == buffId && buff.WeaponTypeId == weaponTypeId)
                return true;
        }

        return false;
    }

    private void AppendBuffState(List<AIBuffState> list, int weaponTypeId, int buffId)
    {
        if (list == null || buffId == 0) return;
        if (ContainsBuff(list, weaponTypeId, buffId)) return;
        list.Add(new AIBuffState { WeaponTypeId = weaponTypeId, BuffId = buffId });
        if (list.Count > MaxStoredBuffs)
            list.RemoveRange(0, list.Count - MaxStoredBuffs);
    }

    private void CacheClientPendingBuff(int id, int weaponTypeId, int buffId)
    {
        if (id == 0 || buffId == 0) return;
        if (!_clientPendingBuffs.TryGetValue(id, out var list) || list == null)
        {
            list = new List<(int weaponTypeId, int buffId)>();
            _clientPendingBuffs[id] = list;
        }
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item.weaponTypeId == weaponTypeId && item.buffId == buffId)
                return;
        }
        list.Add((weaponTypeId, buffId));
        if (list.Count > MaxStoredBuffs)
            list.RemoveRange(0, list.Count - MaxStoredBuffs);
    }

    private void FlushClientPendingBuffs(int id, RemoteAIReplica replica)
    {
        if (replica == null) return;
        if (!_clientPendingBuffs.TryGetValue(id, out var list) || list == null || list.Count == 0)
            return;

        foreach (var (weaponTypeId, buffId) in list)
        {
            if (CoopSyncDatabase.AI.TryGet(id, out var entry) && entry != null)
            {
                AppendBuffState(entry.Buffs, weaponTypeId, buffId);
            }
            replica.ApplyBuff(weaponTypeId, buffId);
        }

        _clientPendingBuffs.Remove(id);
    }

    private void ApplyBuffProxy(CharacterMainControl cmc, int weaponTypeId, int buffId)
    {
        ApplyBuffProxyAsync(cmc, weaponTypeId, buffId).Forget();
    }

    private async UniTask ApplyBuffProxyAsync(CharacterMainControl cmc, int weaponTypeId, int buffId)
    {
        if (!cmc || buffId == 0) return;

        var buff = await COOPManager.ResolveBuffAsync(weaponTypeId, buffId);
        if (buff == null || !cmc) return;

        RemoteAIReplicaTag tag = null;
        try
        {
            tag = cmc.GetComponent<RemoteAIReplicaTag>();
            if (tag != null)
                tag.SuppressBuffForward = true;

            await UniTask.SwitchToMainThread();
            if (cmc)
                cmc.AddBuff(buff, null, weaponTypeId);
        }
        finally
        {
            if (tag != null)
                tag.SuppressBuffForward = false;
        }
    }

    private void EnqueueServerBuff(int id, int weaponTypeId, int buffId)
    {
        if (!IsServer || id == 0 || buffId == 0) return;

        if (!_serverPendingBuffs.TryGetValue(id, out var queue) || queue == null)
        {
            queue = new Queue<(int weaponTypeId, int buffId)>();
            _serverPendingBuffs[id] = queue;
        }

        queue.Enqueue((weaponTypeId, buffId));
        ProcessServerBuffQueue(id);
    }

    private void ProcessServerBuffQueue(int? targetId = null)
    {
        if (_processingServerBuffQueue) return;
        _processingServerBuffQueue = true;

        try
        {
            if (targetId.HasValue)
            {
                if (!_drainingBuffIds.Contains(targetId.Value))
                    DrainBuffQueue(targetId.Value);
            }
            else
            {
                _serverBuffQueueKeys.Clear();
                foreach (var kv in _serverPendingBuffs)
                    _serverBuffQueueKeys.Add(kv.Key);

                for (var i = 0; i < _serverBuffQueueKeys.Count; i++)
                {
                    var id = _serverBuffQueueKeys[i];
                    if (_drainingBuffIds.Contains(id))
                        continue;

                    DrainBuffQueue(id);
                }
            }
        }
        finally
        {
            _processingServerBuffQueue = false;
        }
    }

    private void DrainBuffQueue(int id)
    {
        DrainBuffQueueAsync(id).Forget();
    }

    private async UniTaskVoid DrainBuffQueueAsync(int id)
    {
        if (!_serverPendingBuffs.TryGetValue(id, out var queue) || queue == null || queue.Count == 0)
        {
            _serverPendingBuffs.Remove(id);
            return;
        }

        if (_drainingBuffIds.Contains(id))
            return;

        _drainingBuffIds.Add(id);

        if (!CoopSyncDatabase.AI.TryGet(id, out var entry) || entry == null)
        {
            _serverPendingBuffs.Remove(id);
            _drainingBuffIds.Remove(id);
            return;
        }

        var cmc = TryGetCharacter(entry.Id);
        if (!cmc)
        {
            _drainingBuffIds.Remove(id);
            return;
        }

        while (queue.Count > 0)
        {
            var (weaponTypeId, buffId) = queue.Dequeue();
            await ApplyBuffProxyAsync(cmc, weaponTypeId, buffId);
            BroadcastAIBuff(entry, weaponTypeId, buffId);
        }

        if (queue.Count == 0)
            _serverPendingBuffs.Remove(id);

        _drainingBuffIds.Remove(id);
    }

    private static AICharacterController ResolveAiController(CharacterMainControl target)
    {
        if (!target) return null;
        var controller = target.GetComponent<AICharacterController>();
        if (controller) return controller;
        controller = target.GetComponentInChildren<AICharacterController>(true);
        if (controller) return controller;
        return target.GetComponentInParent<AICharacterController>(true);
    }

    public void Client_ReportAiBuff(int id, int weaponTypeId, int buffId)
    {
        if (IsServer || Service == null || !Service.networkStarted) return;
        if (id == 0 || buffId == 0) return;

        var rpc = new AIBuffReportRpc
        {
            Id = id,
            WeaponTypeId = weaponTypeId,
            BuffId = buffId
        };

        CoopTool.SendRpc(in rpc);
    }

    public void Client_ReportAiHealth(int id, Health health, DamageInfo? damage)
    {
        if (IsServer || Service == null || !Service.networkStarted || health == null) return;

        var (max, cur, bodyArmor, headArmor) = ReadHealthSafe(health);
        if (max <= 0f)
            max = Mathf.Max(cur, 1f);


        Client_ApplyLocalHealthReport(id, max, cur, bodyArmor, headArmor);

        var rpc = new AIHealthReportRpc
        {
            Id = id,
            MaxHealth = max,
            CurrentHealth = cur,
            BodyArmor = bodyArmor,
            HeadArmor = headArmor,
            IsDead = cur <= 0f,
            HasDamage = damage.HasValue,
            Damage = DamageForwardPayload.FromDamageInfo(damage)
        };

        CoopTool.SendRpc(in rpc);
    }

    private void Client_ApplyLocalHealthReport(int id, float max, float current, float bodyArmor, float headArmor)
    {
        if (id == 0) return;
        var entry = CoopSyncDatabase.AI.GetOrCreate(id);
        entry.MaxHealth = max;
        entry.CurrentHealth = Mathf.Clamp(current, 0f, max > 0f ? max : Mathf.Max(1f, current));
        entry.BodyArmor = bodyArmor;
        entry.HeadArmor = headArmor;
        if (entry.CurrentHealth <= 0.0001f)
            entry.Status = AIStatus.Dead;
        else if (entry.Status != AIStatus.Active)
            entry.Status = AIStatus.Active;

        if (_clientReplicas.TryGetValue(id, out var replica))
        {
            replica.ApplyHealth(entry.MaxHealth, entry.CurrentHealth, entry.BodyArmor, entry.HeadArmor);
            if (entry.Status == AIStatus.Dead)
            {
                DestroyReplica(replica);
                _clientReplicas.Remove(id);
                _clientRequested.Remove(id);
                _clientPendingBuffs.Remove(id);
            }
        }
    }

    private CharacterModel ResolveModelPrefab(string modelName)
    {
        modelName = NormalizePrefabName(modelName);
        if (string.IsNullOrEmpty(modelName))
            return null;

        if (_modelCache.TryGetValue(modelName, out var cached) && cached)
        {
            var cachedModel = cached.GetComponent<CharacterModel>();
            if (cachedModel) return cachedModel;
            _modelCache.Remove(modelName);
        }

        try
        {
            var main = CharacterMainControl.Main;
            if (main && main.characterModel && string.Equals(NormalizePrefabName(main.characterModel.name), modelName, StringComparison.OrdinalIgnoreCase))
            {
                _modelCache[modelName] = main.characterModel.gameObject;
                return main.characterModel;
            }
        }
        catch
        {
        }

        try
        {
            var loaded = Resources.Load<CharacterModel>(modelName);
            if (loaded)
            {
                _modelCache[modelName] = loaded.gameObject;
                return loaded;
            }
        }
        catch
        {
        }

        foreach (var model in Resources.FindObjectsOfTypeAll<CharacterModel>())
        {
            if (!model) continue;
            if (string.Equals(NormalizePrefabName(model.name), modelName, StringComparison.OrdinalIgnoreCase))
            {
                _modelCache[modelName] = model.gameObject;
                return model;
            }
        }

        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!go || go.scene.IsValid()) continue;
            if (!go.TryGetComponent(out CharacterModel model)) continue;
            if (string.Equals(NormalizePrefabName(go.name), modelName, StringComparison.OrdinalIgnoreCase))
            {
                _modelCache[modelName] = go;
                return model;
            }
        }

        return null;
    }

    private CharacterModel ResolveModelFromPresetKey(string presetKey)
    {
        if (string.IsNullOrEmpty(presetKey)) return null;
        var preset = ResolveCharacterPreset(presetKey);
        if (!preset) return null;

        try
        {
            return FR_CharacterModel(preset);
        }
        catch
        {
            return null;
        }
    }

    private CharacterRandomPreset ResolveCharacterPreset(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_presetCache.TryGetValue(key, out var cached) && cached)
            return cached;

        foreach (var preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
        {
            if (!preset) continue;
            CachePreset(preset);
            if (IsPresetMatch(preset, key))
                return preset;
        }

        try
        {
            var loaded = Resources.Load<CharacterRandomPreset>(key);
            if (loaded)
            {
                CachePreset(loaded);
                return loaded;
            }
        }
        catch
        {
        }

        return null;
    }

    private void CachePreset(CharacterRandomPreset preset)
    {
        if (!preset) return;
        if (!string.IsNullOrEmpty(preset.nameKey))
            _presetCache[preset.nameKey] = preset;
        if (!string.IsNullOrEmpty(preset.name))
            _presetCache[preset.name] = preset;
    }

    private static bool IsPresetMatch(CharacterRandomPreset preset, string key)
    {
        if (!preset || string.IsNullOrEmpty(key)) return false;
        return string.Equals(preset.nameKey, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(preset.name, key, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyCharacterPreset(CharacterMainControl cmc, string presetKey)
    {
        if (!cmc) return;
        if (string.IsNullOrEmpty(presetKey))
        {
            var fallback = CharacterMainControl.Main?.characterPreset;
            if (fallback)
                cmc.characterPreset = fallback;
            return;
        }

        var preset = ResolveCharacterPreset(presetKey);
        if (preset)
            cmc.characterPreset = preset;
        else if (!cmc.characterPreset)
        {
            var fallback = CharacterMainControl.Main?.characterPreset;
            if (fallback)
                cmc.characterPreset = fallback;
        }
    }

    private static void ApplyVehicleState(CharacterMainControl cmc, AISyncEntry entry)
    {
        if (!cmc || entry == null) return;

        var isVehicle = entry.IsVehicle;
        var vehicleAnimationType = entry.VehicleAnimationType;

        if (cmc.characterPreset)
        {
            if (!isVehicle)
                isVehicle = cmc.characterPreset.isVehicle;
            if (vehicleAnimationType == 0)
                vehicleAnimationType = cmc.characterPreset.vehicleAnimationType;
        }
        if(isVehicle)
        {
           // cmc.FootStepMaterialType = Duckov.AudioManager.FootStepMaterialType.horse;
            cmc.Health.showHealthBar = false;
        }
        cmc.isVehicle = isVehicle;
        cmc.vehicleAnimationType = vehicleAnimationType;
    }

    private static void ApplySpecialAttachments(CharacterMainControl cmc,AISyncEntry aISyncEntry)
    {
        if (!cmc || !cmc.characterPreset) return;
        if(aISyncEntry.Team != Teams.player) return;
        var bases = cmc.characterPreset.specialAttachmentBases;
        if (bases == null || bases.Count == 0) return;

        if (cmc.isVehicle && !cmc.aiCharacterController)
        {
            var aiBase = Traverse.Create(cmc.characterPreset).Field<AICharacterController>("aiController").Value;
            if (aiBase)
            {
                var ai = (cmc.aiCharacterController = UnityEngine.Object.Instantiate(aiBase));
                ai.Init(cmc, cmc.transform.position);
            }
        }

        var ready = AISpecialAttachmentLateBinderUtil.Ensure(cmc);
        if (ready) return;

       // var binder = cmc.GetComponent<AISpecialAttachmentLateBinder>() ?? cmc.gameObject.AddComponent<AISpecialAttachmentLateBinder>();
       // binder.Init(cmc);
    }

    private static void MakeReplicaPassive(CharacterMainControl cmc)
    {
        if (!cmc) return;

        try
        {
            if (!cmc.gameObject.GetComponent<RemoteReplicaTag>())
                cmc.gameObject.AddComponent<RemoteReplicaTag>();
        }
        catch
        {
        }

        try
        {
            var nma = cmc.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
            if (nma) nma.enabled = false;
        }
        catch
        {
        }

        try
        {
            var controller = cmc.GetComponent<AICharacterController>();
            if (controller) controller.enabled = false;
        }
        catch
        {
        }
    }

    private static void ApplyCustomFace(CharacterModel model, string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            CustomFace.ApplyFaceJsonToModel(model, json);
        }
        catch
        {
        }
    }

    private static async UniTask ApplyEquipmentAsync(
        CharacterModel model,
        Dictionary<string, int> equipment,
        Dictionary<string, int> weapons,
        Dictionary<string, ItemSnapshot> weaponSnapshots = null,
        string activeWeaponSlot = null,
        int activeWeaponTypeId = 0)
    {
        if (!model) return;

        var charItem = model.characterMainControl ? model.characterMainControl.CharacterItem : null;

        if (equipment != null)
        {
            foreach (var kv in equipment)
            {
                if (IsWeaponSlot(kv.Key)) continue;
                var typeId = kv.Value;
                if (typeId <= 0) continue;
                try
                {
                    var item = await ItemAssetsCollection.InstantiateAsync(typeId);
                    if (charItem != null)
                        charItem.TryPlug(item);
                }
                catch
                {
                }
            }
        }

        if (weapons != null)
        {
            Item activeItem = null;
            Item firstGunItem = null;
            Item firstWeaponItem = null;
            foreach (var kv in weapons)
            {
                var typeId = kv.Value;
                if (typeId <= 0) continue;
                try
                {
                    Item item = null;
                    if (weaponSnapshots != null && weaponSnapshots.TryGetValue(kv.Key, out var snap) && snap.TypeId != 0)
                    {
                        item = ItemTool.BuildItemFromSnapshot(snap);
                    }

                    if (item == null)
                        item = await ItemAssetsCollection.InstantiateAsync(typeId);

                    var slot = kv.Key;
                    if (charItem != null && item != null)
                        charItem.TryPlug(item);

                    if (item != null)
                    {
                        if (firstWeaponItem == null)
                            firstWeaponItem = item;
                        if (firstGunItem == null && item.GetComponent<ItemSetting_Gun>() != null)
                            firstGunItem = item;
                        if ((activeWeaponTypeId > 0 && item.TypeID == activeWeaponTypeId) ||
                            (!string.IsNullOrEmpty(activeWeaponSlot) && string.Equals(slot, activeWeaponSlot, StringComparison.Ordinal)))
                            activeItem = item;
                    }
                }
                catch
                {
                }
            }

            var itemToHold = activeItem ?? firstGunItem ?? firstWeaponItem;
            if (itemToHold != null && model.characterMainControl)
                model.characterMainControl.ChangeHoldItem(itemToHold);
        }
    }

    private void TryAttachHideIfFoundEnemyReplica(AISyncEntry entry, GameObject instance, CharacterMainControl cmc)
    {
        if (entry == null || cmc == null) return;
        var name = NormalizePrefabName(entry.HideIfFoundEnemyName);
        if (string.IsNullOrEmpty(name)) return;

        var template = ResolveHideIfFoundEnemyTemplate(name);
        if (!template) return;

        GameObject clone = null;
        try
        {
            clone = Object.Instantiate(template, cmc.transform, false);
            clone.name = template.name;
            clone.transform.localPosition = template.transform.localPosition;
            clone.transform.localRotation = template.transform.localRotation;
            clone.transform.localScale = template.transform.localScale;
            clone.SetActive(true);
        }
        catch
        {
            if (clone)
                Object.Destroy(clone);
            return;
        }

        var controller = instance ? instance.GetComponentInChildren<AICharacterController>() : null;
        if (controller)
            controller.hideIfFoundEnemy = clone;
    }

    private GameObject ResolveHideIfFoundEnemyTemplate(string rawName)
    {
        var name = NormalizePrefabName(rawName);
        if (string.IsNullOrEmpty(name)) return null;

        if (_hideIfFoundEnemyCache.TryGetValue(name, out var cached) && cached)
            return cached;

        var source = FindHideIfFoundEnemySource(name);
        if (!source) return null;

        var clone = CloneObject(source, name + "_COOP_HIDE_TEMPLATE");
        if (!clone) return null;

        _hideIfFoundEnemyCache[name] = clone;
        return clone;
    }

    private static GameObject FindHideIfFoundEnemySource(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName)) return null;

        try
        {
            var loaded = Resources.Load<GameObject>(normalizedName);
            if (loaded)
                return loaded;
        }
        catch
        {
        }

        try
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!obj) continue;
                if (string.Equals(NormalizePrefabName(obj.name), normalizedName, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }
        }
        catch
        {
        }

        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                var roots = scene.GetRootGameObjects();
                for (var i = 0; i < roots.Length; i++)
                {
                    var child = FindChildRecursive(roots[i].transform, normalizedName);
                    if (child)
                        return child.gameObject;
                }
            }
        }
        catch
        {
        }

        try
        {
            var loaded = Resources.Load<GameObject>(normalizedName);
            if (loaded)
            {
                return loaded;
            }
        }
        catch
        {
        }

        return null;
    }

    private static Transform FindChildRecursive(Transform root, string normalizedName)
    {
        if (!root) return null;
        if (string.Equals(NormalizePrefabName(root.name), normalizedName, StringComparison.OrdinalIgnoreCase))
            return root;

        for (var i = 0; i < root.childCount; i++)
        {
            var child = FindChildRecursive(root.GetChild(i), normalizedName);
            if (child)
                return child;
        }

        return null;
    }

    private static GameObject CloneObject(GameObject obj, string name)
    {
        if (!obj) return null;

        try
        {
            var clone = Object.Instantiate(obj);
            clone.name = name;
            clone.transform.SetParent(null, true);
            clone.SetActive(false);
            Object.DontDestroyOnLoad(clone);
            return clone;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWeaponSlot(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        key = key.ToLowerInvariant();
        return key.Contains("hand") || key.Contains("weapon");
    }

    private bool RefreshActiveWeapon(AISyncEntry entry, CharacterMainControl cmc)
    {
        if (entry == null || !cmc)
            return false;

        var oldSlot = GetActiveWeaponSlot(entry.Id);
        var oldType = GetActiveWeaponType(entry.Id);
        var activeSlot = string.Empty;
        var activeType = 0;

        try
        {
            var hold = cmc.CurrentHoldItemAgent;
            var item = hold ? hold.Item : null;
            if (item)
            {
                activeType = item.TypeID;
                activeSlot = ResolveActiveWeaponSlot(cmc, item, hold.handheldSocket);
            }
        }
        catch
        {
        }

        if (activeType == 0)
        {
            try
            {
                var gun = cmc.GetGun();
                if (gun && gun.Item)
                {
                    activeType = gun.Item.TypeID;
                    activeSlot = ResolveActiveWeaponSlot(cmc, gun.Item, HandheldSocketTypes.normalHandheld);
                }
            }
            catch
            {
            }
        }

        if (activeType == 0)
        {
            try
            {
                var melee = cmc.GetMeleeWeapon();
                if (melee && melee.Item)
                {
                    activeType = melee.Item.TypeID;
                    activeSlot = ResolveActiveWeaponSlot(cmc, melee.Item, HandheldSocketTypes.meleeWeapon);
                }
            }
            catch
            {
            }
        }

        SetActiveWeapon(entry.Id, activeSlot, activeType);
        return oldType != activeType || !string.Equals(oldSlot, activeSlot, StringComparison.Ordinal);
    }

    private static string ResolveActiveWeaponSlot(CharacterMainControl cmc, Item item, HandheldSocketTypes fallbackSocket)
    {
        if (cmc && item)
        {
            try
            {
                var slots = cmc.CharacterItem?.Slots;
                if (slots != null)
                {
                    foreach (var slot in slots)
                    {
                        if (slot?.Content == item)
                            return slot.Key ?? string.Empty;
                    }
                }
            }
            catch
            {
            }
        }

        return fallbackSocket.ToString();
    }

    private string GetActiveWeaponSlot(int id)
    {
        return _activeWeaponSlotById.TryGetValue(id, out var slot) ? slot ?? string.Empty : string.Empty;
    }

    private int GetActiveWeaponType(int id)
    {
        return _activeWeaponTypeById.TryGetValue(id, out var typeId) ? typeId : 0;
    }

    private void SetActiveWeapon(int id, string slot, int typeId)
    {
        if (id == 0)
            return;

        if (typeId <= 0 && string.IsNullOrEmpty(slot))
        {
            _activeWeaponSlotById.Remove(id);
            _activeWeaponTypeById.Remove(id);
            return;
        }

        _activeWeaponSlotById[id] = slot ?? string.Empty;
        _activeWeaponTypeById[id] = Mathf.Max(0, typeId);
    }

    private AnimSample CaptureAnimSample(AISyncEntry entry, AICharacterController controller)
    {
        var sample = new AnimSample
        {
            t = Time.unscaledTimeAsDouble,
            stateHash = -1
        };

        try
        {
            var animator = ResolveCachedAiAnimator(entry, controller);
            if (!animator) return sample;

            sample.speed = animator.GetFloat(AnimMoveSpeedHash);
            sample.dirX = animator.GetFloat(AnimMoveDirXHash);
            sample.dirY = animator.GetFloat(AnimMoveDirYHash);
            sample.dashing = animator.GetBool(AnimDashingHash);
            sample.attack = animator.GetBool(AnimAttackHash);
            sample.hand = animator.GetInteger(AnimHandStateHash);
            sample.gunReady = animator.GetBool(AnimGunReadyHash);
            sample.vehicleType = animator.GetInteger(AnimVehicleTypeHash);

            var state = animator.GetCurrentAnimatorStateInfo(0);
            sample.stateHash = state.shortNameHash;
            sample.normTime = state.normalizedTime;
        }
        catch
        {
        }

        return sample;
    }

    private Animator ResolveCachedAiAnimator(AISyncEntry entry, AICharacterController controller)
    {
        if (entry != null && _serverAnimatorCache.TryGetValue(entry.Id, out var cached) && cached)
            return cached;

        var cmc = controller ? controller.CharacterMainControl : null;
        var model = cmc ? cmc.characterModel : null;
        if (!model) return null;

        Animator animator = null;
        try
        {
            var magic = model.GetComponent<CharacterAnimationControl_MagicBlend>();
            if (magic != null && magic.animator != null)
                animator = magic.animator;
        }
        catch
        {
        }

        if (!animator)
        {
            try
            {
                var basic = model.GetComponent<CharacterAnimationControl>();
                if (basic != null && basic.animator != null)
                    animator = basic.animator;
            }
            catch
            {
            }
        }

        if (animator && entry != null)
            _serverAnimatorCache[entry.Id] = animator;

        return animator;
    }

    private sealed class RemoteAIReplica
    {
        public readonly int Id;
        public readonly GameObject Instance;
        public readonly CharacterMainControl Character;
        private readonly AnimParamInterpolator _animInterp;
        private readonly Health _health;
        private readonly AISyncService _service;
        private readonly RemoteAIReplicaTag _tag;
        private readonly NetInterpolator _netInterp;
        private readonly bool _isVehicle;
        private bool _deathNotified;
        private bool _suppressHealthReport;
        private int _lastEntryApplySignature = int.MinValue;
        private int _lastActiveWeaponTypeId;
        private int _pendingActiveWeaponTypeId;
        private float _lastPushedStateReceivedTime = -1f;
        private bool _hasLastPushedTransform;
        private Vector3 _lastPushedPosition;
        private Quaternion _lastPushedRotation;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private Animator _localVehicleAnimator;
        private bool _hasLastLocalPosition;
        private Vector3 _lastLocalPosition;
        private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
        private static readonly int MoveDirXHash = Animator.StringToHash("MoveDirX");
        private static readonly int MoveDirYHash = Animator.StringToHash("MoveDirY");
        private static readonly int VehicleTypeHash = Animator.StringToHash("VehicleType");

        public RemoteAIReplica(int id, GameObject instance, CharacterMainControl character, AISyncEntry entry, AISyncService service)
        {
            Id = id;
            Instance = instance;
            Character = character;
            _service = service;
            _animInterp = AnimInterpUtil.Attach(instance);
            _netInterp = NetInterpUtil.Attach(instance);
            _health = character ? character.Health : null;
            _tag = character ? character.GetComponent<RemoteAIReplicaTag>() : null;
            _isVehicle = entry != null && entry.IsVehicle;
            if (_isVehicle && _netInterp != null)
            {
                _netInterp.driveModelPosition = true;
                _netInterp.interpolationBackTime = 0.1f;
                _netInterp.maxExtrapolate = 0.18f;
                _netInterp.hardSnapDistance = 12f;
                _netInterp.sendInterval = 0.05f;
            }
            if (_tag)
                _tag.Id = id;
            if (_health)
            {
                _health.OnHurtEvent.AddListener(OnHurt);
            }
            ApplyTeam(entry.Team);
            ApplyState(entry);
        }

        public void ApplyState(AISyncEntry entry)
        {
            if (entry == null || !Instance || !Character) return;

            var activeWeaponTypeId = _service != null ? _service.GetActiveWeaponType(entry.Id) : 0;
            var applySignature = ComputeEntryApplySignature(entry, activeWeaponTypeId);
            if (applySignature == _lastEntryApplySignature)
                return;
            _lastEntryApplySignature = applySignature;

            ApplyTeam(entry.Team);
            ApplyVehicleState(Character, entry);
            var stateReceivedTime = entry.LastStateReceivedTime;
            var hasNewState = stateReceivedTime > _lastPushedStateReceivedTime + 1e-4f;
            var changedTransform = !_hasLastPushedTransform ||
                                   (entry.LastKnownPosition - _lastPushedPosition).sqrMagnitude > 1e-6f ||
                                   Quaternion.Angle(entry.LastKnownRotation, _lastPushedRotation) > 0.01f;

            if (hasNewState || changedTransform)
            {
                _lastPushedStateReceivedTime = stateReceivedTime;
                _lastPushedPosition = entry.LastKnownPosition;
                _lastPushedRotation = entry.LastKnownRotation;
                _hasLastPushedTransform = true;
            }

            var shouldPushNetState = hasNewState || changedTransform;
            var receiveTime = Time.unscaledTimeAsDouble;
            var localAuthVehicle = _service != null && _service.IsLocalAuthoritativeVehicle(entry);
            if (_netInterp != null)
            {
                if (localAuthVehicle)
                {
                    _netInterp.enabled = false;
                }
                else
                {
                    if (!_netInterp.enabled)
                        _netInterp.enabled = true;
                    if (shouldPushNetState)
                        _netInterp.Push(entry.LastKnownPosition, entry.LastKnownRotation, receiveTime, entry.LastKnownVelocity);
                }
            }

            if (_netInterp == null && !localAuthVehicle)
            {
                _targetPosition = entry.LastKnownPosition;
                _targetRotation = entry.LastKnownRotation;
            }

            if (_health)
            {
                var max = entry.MaxHealth;
                if (max <= 0f) max = Mathf.Max(1f, entry.CurrentHealth);
                ApplyHealth(max, entry.CurrentHealth, entry.BodyArmor, entry.HeadArmor);
            }

            if (_animInterp != null)
            {
                if (localAuthVehicle)
                {
                    if (_animInterp.enabled)
                        _animInterp.enabled = false;
                }
                else
                {
                    if (!_animInterp.enabled)
                        _animInterp.enabled = true;

                    var sample = entry.LastAnimSample;
                    if (sample.t <= 0d)
                        sample.t = Time.unscaledTimeAsDouble;
                    if (hasNewState)
                        _animInterp.Push(sample);
                }
            }

            if (activeWeaponTypeId != _lastActiveWeaponTypeId &&
                activeWeaponTypeId != _pendingActiveWeaponTypeId)
                EnsureActiveWeaponAsync(entry, activeWeaponTypeId).Forget();
        }

        private static int ComputeEntryApplySignature(AISyncEntry entry, int activeWeaponTypeId)
        {
            var hash = new HashCode();
            hash.Add(entry.Team);
            hash.Add(entry.IsVehicle);
            hash.Add(entry.VehicleAnimationType);
            hash.Add(entry.LastKnownPosition);
            hash.Add(entry.LastKnownRotation);
            hash.Add(entry.LastKnownVelocity);
            hash.Add(entry.LastStateReceivedTime);
            hash.Add(entry.MaxHealth);
            hash.Add(entry.CurrentHealth);
            hash.Add(entry.BodyArmor);
            hash.Add(entry.HeadArmor);
            hash.Add(activeWeaponTypeId);

            var sample = entry.LastAnimSample;
            hash.Add(sample.t);
            hash.Add(sample.speed);
            hash.Add(sample.dirX);
            hash.Add(sample.dirY);
            hash.Add(sample.dashing);
            hash.Add(sample.attack);
            hash.Add(sample.hand);
            hash.Add(sample.gunReady);
            hash.Add(sample.vehicleType);
            hash.Add(sample.stateHash);
            hash.Add(sample.normTime);

            return hash.ToHashCode();
        }

        private async UniTaskVoid EnsureActiveWeaponAsync(AISyncEntry entry, int typeId)
        {
            if (entry == null || !Character)
                return;

            _pendingActiveWeaponTypeId = typeId;

            try
            {
                if (typeId <= 0)
                {
                    _lastActiveWeaponTypeId = 0;
                    return;
                }

                var item = FindCharacterItemByType(Character, typeId);
                if (item == null)
                {
                    if (TryGetWeaponSnapshot(entry, typeId, out var snap) && snap.TypeId != 0)
                        item = ItemTool.BuildItemFromSnapshot(snap);

                    if (item == null)
                        item = await ItemAssetsCollection.InstantiateAsync(typeId);

                    if (item != null && Character && Character.CharacterItem != null)
                    {
                        try
                        {
                            Character.CharacterItem.TryPlug(item);
                        }
                        catch
                        {
                        }
                    }
                }

                if (item != null && Character)
                {
                    Character.ChangeHoldItem(item);
                    _lastActiveWeaponTypeId = typeId;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AI][CLIENT] Apply active weapon failed id={Id} type={typeId}: {ex.Message}");
            }
            finally
            {
                if (_pendingActiveWeaponTypeId == typeId)
                    _pendingActiveWeaponTypeId = 0;
            }
        }

        private static bool TryGetWeaponSnapshot(AISyncEntry entry, int typeId, out ItemSnapshot snapshot)
        {
            if (entry?.WeaponSnapshots != null)
            {
                foreach (var kv in entry.WeaponSnapshots)
                {
                    if (kv.Value.TypeId == typeId)
                    {
                        snapshot = kv.Value;
                        return true;
                    }
                }
            }

            snapshot = default;
            return false;
        }

        private static Item FindCharacterItemByType(CharacterMainControl character, int typeId)
        {
            if (!character || typeId <= 0)
                return null;

            try
            {
                var slots = character.CharacterItem?.Slots;
                if (slots != null)
                {
                    foreach (var slot in slots)
                    {
                        var item = slot?.Content;
                        if (item != null && item.TypeID == typeId)
                            return item;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public void ApplyBuffList(List<AIBuffState> buffs)
        {
            if (buffs == null) return;
            for (var i = 0; i < buffs.Count; i++)
                ApplyBuff(buffs[i].WeaponTypeId, buffs[i].BuffId);
        }

        public void ApplyBuff(int weaponTypeId, int buffId)
        {
            if (_service == null || buffId == 0) return;
            _service.ApplyBuffProxy(Character, weaponTypeId, buffId);
        }

        private void ApplyTeam(Teams team)
        {
            if (!Character) return;
            try
            {
                Character.SetTeam(team);
            }
            catch
            {
            }
        }

        public void Update(float deltaTime)
        {
            if (!Instance || !Character) return;

            var localAuthVehicle = false;
            if (_isVehicle && _service != null && CoopSyncDatabase.AI.TryGet(Id, out var entry) && entry != null)
                localAuthVehicle = _service.IsLocalAuthoritativeVehicle(entry);

            if (localAuthVehicle)
                ApplyLocalVehicleAnimation(deltaTime);
            else
                _hasLastLocalPosition = false;

            if (_netInterp != null)
                return;

            var nextPosition = Vector3.Lerp(Character.transform.position, _targetPosition, deltaTime * 8f);
            var nextRotation = Quaternion.Slerp(Character.transform.rotation, _targetRotation, deltaTime * 8f);
            Character.transform.SetPositionAndRotation(nextPosition, nextRotation);

            if (Character.characterModel)
                Character.characterModel.transform.rotation = nextRotation;
        }

        private void ApplyLocalVehicleAnimation(float deltaTime)
        {
            var animator = ResolveLocalVehicleAnimator();
            if (!animator)
                return;

            var position = Character.transform.position;
            if (!_hasLastLocalPosition)
            {
                _lastLocalPosition = position;
                _hasLastLocalPosition = true;
                TrySetFloat(animator, MoveSpeedHash, 0f);
                TrySetFloat(animator, MoveDirXHash, 0f);
                TrySetFloat(animator, MoveDirYHash, 0f);
                return;
            }

            var dt = Mathf.Max(0.0001f, deltaTime);
            var speed = (position - _lastLocalPosition).magnitude / dt;
            _lastLocalPosition = position;

            var moving = speed > 0.08f;
            TrySetFloat(animator, MoveSpeedHash, moving ? Mathf.Clamp(speed, 0f, 6f) : 0f);
            TrySetFloat(animator, MoveDirXHash, 0f);
            TrySetFloat(animator, MoveDirYHash, moving ? 1f : 0f);

            var vehicleType = Character.ridingVehicleType > 0 ? Character.ridingVehicleType : Character.vehicleAnimationType;
            if (vehicleType > 0)
                TrySetInt(animator, VehicleTypeHash, vehicleType);
        }

        private Animator ResolveLocalVehicleAnimator()
        {
            if (_localVehicleAnimator)
                return _localVehicleAnimator;

            var model = Character ? Character.characterModel : null;
            if (!model)
                return null;

            var basic = model.GetComponent<CharacterAnimationControl>();
            if (basic != null && basic.animator != null)
                _localVehicleAnimator = basic.animator;

            if (!_localVehicleAnimator)
            {
                var magic = model.GetComponent<CharacterAnimationControl_MagicBlend>();
                if (magic != null && magic.animator != null)
                    _localVehicleAnimator = magic.animator;
            }

            if (!_localVehicleAnimator)
                _localVehicleAnimator = model.GetComponentInChildren<Animator>(true);

            return _localVehicleAnimator;
        }

        private static void TrySetFloat(Animator animator, int hash, float value)
        {
            if (!animator) return;
            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == hash && parameters[i].type == AnimatorControllerParameterType.Float)
                {
                    animator.SetFloat(hash, value);
                    return;
                }
            }
        }

        private static void TrySetInt(Animator animator, int hash, int value)
        {
            if (!animator) return;
            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == hash && parameters[i].type == AnimatorControllerParameterType.Int)
                {
                    animator.SetInteger(hash, value);
                    return;
                }
            }
        }

        public void ApplyHealth(float max, float current, float bodyArmor, float headArmor)
        {
            if (!_health) return;
            _suppressHealthReport = true;
            try
            {
                HealthM.Instance.ForceSetHealth(_health, max > 0f ? max : Mathf.Max(1f, current), current, true, bodyArmor, headArmor);
            }
            finally
            {
                _suppressHealthReport = false;
            }
        }

       
        private void OnHurt(DamageInfo info)
        {
            //if (_suppressHealthReport || _service == null || _health == null) return;
            //_service.Client_ReportAiHealth(Id, _health, info);
        }


        public void Dispose()
        {
            if (_health)
            {
                _health.OnHurtEvent.RemoveListener(OnHurt);
            }
            if (_tag)
                _tag.SuppressBuffForward = false;
        }
    }
}
