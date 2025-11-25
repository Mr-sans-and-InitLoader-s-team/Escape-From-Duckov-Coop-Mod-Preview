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
using ItemStatsSystem;
using LiteNetLib;
using Sirenix.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Duckov.MiniGames.Examples.FPS.FPSGun;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public sealed class AISyncService
{
    private const float ActivationRadius = 80f;
    private const float DeactivationRadius = 100f;
    private const float ActivationRetryInterval = 0.75f;
    private const float StateBroadcastInterval = 0.1f;
    private const float IdleStateRecordInterval = 0.5f;
    private const float HealthBroadcastInterval = 0.075f;
    private const float MinPositionDelta = 0.25f;
    private const float MinRotationDelta = 2.0f;
    private const float VelocityLerp = 12f;
    private const float SnapshotRefreshInterval = 15f;
    private const int SnapshotChunkSize = 48;
    private const int MaxStoredBuffs = 32;
    private const int MaxSnapshotAppliesPerFrame = 12;
    private const int MaxStateUpdatesPerFrame = 24;
    private const int MaxPendingSnapshotQueue = 512;
    private const int MaxPendingStateQueue = 1024;
    private const float ServerControllerRescanInterval = 10f;
    private const float ServerSnapshotBroadcastInterval = 12f;
    private const float ServerSnapshotRetryInterval = 3f;

    private readonly Dictionary<int, HashSet<NetPeer>> _serverWatchers = new();
    private readonly Dictionary<int, RemoteAIReplica> _clientReplicas = new();
    private readonly Dictionary<int, float> _clientRequested = new();
    private readonly Dictionary<int, List<(int weaponTypeId, int buffId)>> _clientPendingBuffs = new();
    private readonly Dictionary<int, float> _lastHealthBroadcastTime = new();
    private readonly Dictionary<int, PendingHealthBroadcast> _pendingHealthBroadcasts = new();
    private readonly Dictionary<string, GameObject> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterRandomPreset> _presetCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameObject> _hideIfFoundEnemyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AISnapshotEntry> _snapshotBuffer = new();
    private readonly Queue<AISnapshotEntry> _pendingSnapshotQueue = new();
    private readonly Queue<AIStateUpdateRpc> _pendingStateUpdates = new();
    private readonly List<int> _pendingHealthFlush = new();
    private readonly HashSet<int> _loadedSceneIndexBuffer = new();
    private readonly HashSet<string> _loadedScenePathBuffer = new(StringComparer.OrdinalIgnoreCase);

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
    private bool _pendingSnapshotReset;
    private float _nextServerRescanTime;
    private float _nextServerSnapshotBroadcastTime;
    private bool _serverPendingSnapshotReset;
    private bool _syncBlocked;

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
            _lastHealthBroadcastTime.Clear();
            _pendingHealthBroadcasts.Clear();
            _pendingHealthFlush.Clear();
        }
        else
        {
            foreach (var replica in _clientReplicas.Values)
                DestroyReplica(replica);
            _clientReplicas.Clear();
            _clientRequested.Clear();
            _lastSnapshotSceneId = null;
            _snapshotRequested = false;
            _nextSnapshotRefreshTime = 0f;
            _pendingSnapshotQueue.Clear();
            _pendingStateUpdates.Clear();
            _pendingSnapshotReset = false;
        }

        _modelCache.Clear();
        _presetCache.Clear();
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
        BroadcastState(entry);
        QueueHealthBroadcast(entry, null, false);
    }

    public void Server_HandleDeath(AISyncEntry entry, float max, float current, float bodyArmor, float headArmor)
    {
        if (!IsServer || entry == null) return;
        if (CheckAndHandleSyncBlock()) return;

        entry.Status = AIStatus.Dead;
        if (max > 0f) entry.MaxHealth = max;
        entry.CurrentHealth = Mathf.Clamp(current, 0f, entry.MaxHealth <= 0f ? float.MaxValue : entry.MaxHealth);
        entry.BodyArmor = bodyArmor;
        entry.HeadArmor = headArmor;
        BroadcastState(entry);
        QueueHealthBroadcast(entry, null, true);
        BroadcastDespawn(entry);
    }

    public void Server_HandleHealthReport(RpcContext context, AIHealthReportRpc message)
    {
        if (!IsServer || context.Sender == null) return;
        if (CheckAndHandleSyncBlock()) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null) return;

        if (message.MaxHealth > 0f)
            entry.MaxHealth = message.MaxHealth;
        entry.CurrentHealth = Mathf.Clamp(message.CurrentHealth, 0f, entry.MaxHealth <= 0f ? float.MaxValue : entry.MaxHealth);
        entry.BodyArmor = message.BodyArmor;
        entry.HeadArmor = message.HeadArmor;
        var damage = message.HasDamage ? message.Damage : (DamageForwardPayload?)null;

        var clampedCurrent = entry.CurrentHealth;
        var appliedDamage = ApplyDamageToController(entry, damage, ResolveAttacker(context));
        if (appliedDamage)
            entry.CurrentHealth = clampedCurrent;
        else
            ApplyHealthToController(entry);

        var isDead = message.IsDead || entry.CurrentHealth <= 0.0001f;
        if (isDead)
        {
            var forced = appliedDamage || EnsureServerControllerDeath(entry, damage);
            if (forced)
                entry.ServerDeathHandled = true;

            if (!forced)
            {
                ForceDisableServerController(entry);
            }

            var wasDead = entry.Status == AIStatus.Dead;
            entry.Status = AIStatus.Dead;
            if (!entry.ServerDeathHandled)
            {
                entry.ServerDeathHandled = true;
                TryForceServerOnDead(entry, damage);
            }
            QueueHealthBroadcast(entry, damage, true);
            if (!wasDead)
                BroadcastDespawn(entry);

            return;
        }

        QueueHealthBroadcast(entry, damage, false);
    }

    private void TryForceServerOnDead(AISyncEntry entry, DamageForwardPayload? damage)
    {
        if (entry == null) return;

        var controller = entry.Controller;
        var cmc = controller ? controller.CharacterMainControl : null;
        var health = cmc ? cmc.Health : null;
        if (!cmc || !health) return;

        var receiver = cmc ? cmc.mainDamageReceiver : null;
        if (!receiver && cmc)
            receiver = cmc.GetComponentInChildren<DamageReceiver>(true);

        var info = damage?.ToDamageInfo(null, receiver) ?? new DamageInfo
        {
            damageValue = Mathf.Max(1f, entry.CurrentHealth <= 0f ? 1f : entry.CurrentHealth),
            finalDamage = Mathf.Max(1f, entry.CurrentHealth <= 0f ? 1f : entry.CurrentHealth),
            damagePoint = cmc.transform.position,
            damageNormal = Vector3.up
        };

        if (info.toDamageReceiver == null)
            info.toDamageReceiver = receiver;

        var bak = DeadLootSpawnContext.InOnDead;
        DeadLootSpawnContext.InOnDead = cmc;
        try
        {
            cmc.Health.Hurt(info);
        }
        catch
        {
        }
        finally
        {
            DeadLootSpawnContext.InOnDead = bak;
        }
    }

    public void Server_HandleBuffReport(RpcContext context, AIBuffReportRpc message)
    {
        if (!IsServer || context.Sender == null || message.BuffId == 0) return;
        if (CheckAndHandleSyncBlock()) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null) return;

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

        var now = Time.unscaledTime;
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
            if (wasActivated != activatedNow)
            {
                entry.Activated = activatedNow;
                BroadcastActivationState(entry);
                if (!activatedNow)
                    _serverWatchers.Remove(entry.Id);
            }
            var watchers = GetServerWatchers(entry.Id);
            var modelTransform = cmc && cmc.characterModel ? cmc.characterModel.transform : null;
            var position = controller.transform.position;
            var rotation = modelTransform ? modelTransform.rotation : controller.transform.rotation;
            var velocity = (position - entry.LastKnownPosition) / Mathf.Max(Time.deltaTime, 0.0001f);

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

            var posDelta = Vector3.Distance(position, entry.LastKnownPosition);
            var rotDelta = Quaternion.Angle(rotation, entry.LastKnownRotation);

            var nearestWatcher = GetNearestWatcherDistance(entry, watchers);
            var stateInterval = ResolveStateInterval(nearestWatcher);

            if (float.IsPositiveInfinity(stateInterval))
            {
                entry.LastKnownPosition = position;
                entry.LastKnownRotation = rotation;
                entry.LastKnownVelocity = Vector3.zero;
                continue;
            }

            if (now - entry.LastStateSentTime < stateInterval &&
                posDelta < MinPositionDelta && rotDelta < MinRotationDelta)
                continue;

            entry.LastKnownPosition = position;
            entry.LastKnownRotation = rotation;
            entry.LastKnownVelocity = Vector3.Lerp(entry.LastKnownVelocity, velocity, deltaTime * VelocityLerp);
            entry.LastStateSentTime = now;
            entry.LastAnimSample = CaptureAnimSample(controller);

            BroadcastState(entry, watchers);
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

                if (hasRadius)
                {
                    var distanceSqr = (entry.SpawnPosition - center).sqrMagnitude;
                    if (distanceSqr > radiusSqr) continue;
                }

                _snapshotBuffer.Add(BuildSnapshotEntry(entry));

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
            return;

        if (!entry.Activated)
            return;

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
    }

    private void Client_SendSnapshotRequest(bool forceFull)
    {
        if (IsServer || Service == null || !Service.networkStarted) return;
        if (CheckAndHandleSyncBlock()) return;

        var request = new AISnapshotRequestRpc
        {
            HasRadius = false,
            Radius = ActivationRadius * 1.2f,
            Center = Vector3.zero,
            ForceFull = forceFull
        };
        CoopTool.SendRpc(in request);
    }

    public void Client_HandleSnapshotChunk(AISnapshotChunkRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        if (message.Reset)
        {
            _pendingSnapshotReset = true;
            _pendingSnapshotQueue.Clear();
        }

        if (message.Entries == null) return;

        foreach (var snap in message.Entries)
        {
            if (_pendingSnapshotQueue.Count >= MaxPendingSnapshotQueue)
                _pendingSnapshotQueue.Dequeue();
            _pendingSnapshotQueue.Enqueue(snap);
        }
    }

    public void Client_HandleActivationState(AIActivationStateRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        var entry = CoopSyncDatabase.AI.GetOrCreate(message.Id);
        entry.Activated = message.Activated;

        if (!message.Activated)
        {
            if (_clientReplicas.TryGetValue(message.Id, out var replica))
            {
                DestroyReplica(replica);
                _clientReplicas.Remove(message.Id);
            }

            _clientRequested.Remove(message.Id);
            return;
        }

        _clientRequested.Remove(message.Id);
    }

    public void Client_HandleSpawn(AISpawnRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        var entry = CoopSyncDatabase.AI.GetOrCreate(message.Entry.Id);
        ApplySnapshot(entry, in message.Entry);

        CollectLoadedScenes();
        if (!IsEntrySceneLoaded(entry))
        {
            entry.Status = AIStatus.Dormant;
            return;
        }

        _clientRequested.Remove(entry.Id);
        if (!entry.Activated)
        {
            entry.Status = AIStatus.Dormant;
            if (_clientReplicas.TryGetValue(entry.Id, out var replica))
            {
                DestroyReplica(replica);
                _clientReplicas.Remove(entry.Id);
            }
            return;
        }

        if (entry.Status == AIStatus.Dead || entry.Status == AIStatus.Despawned)
            return;

        SpawnReplicaAsync(entry).Forget();
    }

    public void Client_HandleDespawn(AIDespawnRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;

        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
            return;

        entry.Status = message.Status;
        entry.LastAnimSample = default;
        if (_clientReplicas.TryGetValue(message.Id, out var replica))
        {
            DestroyReplica(replica);
            _clientReplicas.Remove(message.Id);
        }
        _clientRequested.Remove(message.Id);
        _clientPendingBuffs.Remove(message.Id);
    }

    public void Client_HandleState(AIStateUpdateRpc message)
    {
        if (IsServer) return;
        if (CheckAndHandleSyncBlock()) return;
        if (_pendingStateUpdates.Count >= MaxPendingStateQueue)
            _pendingStateUpdates.Dequeue();
        _pendingStateUpdates.Enqueue(message);
    }

    public void Client_Update(float deltaTime)
    {
        if (IsServer || Service == null || !Service.networkStarted) return;
        if (CheckAndHandleSyncBlock()) return;

        CollectLoadedScenes();
        Client_RequestSnapshotIfNeeded();
        Client_ProcessPendingSnapshots();
        Client_ProcessPendingStateUpdates();

        var now = Time.unscaledTime;
        if (_snapshotRequested && _nextSnapshotRefreshTime > 0f && now >= _nextSnapshotRefreshTime)
        {
            Client_SendSnapshotRequest(false);
            _nextSnapshotRefreshTime = now + SnapshotRefreshInterval;
        }

        var main = CharacterMainControl.Main;
        var playerPos = main ? main.transform.position : Vector3.zero;
        var hasPlayer = main != null;

        foreach (var replica in _clientReplicas.Values)
            replica.Update(deltaTime);

        if (!hasPlayer) return;

        now = Time.unscaledTime;
        foreach (var entry in CoopSyncDatabase.AI.Entries)
        {
            if (entry == null) continue;
            if (!IsEntrySceneLoaded(entry))
            {
                if (_clientReplicas.TryGetValue(entry.Id, out var replica))
                {
                    DestroyReplica(replica);
                    _clientReplicas.Remove(entry.Id);
                }
                _clientRequested.Remove(entry.Id);
                continue;
            }
            var persistent = ShouldKeepReplicaAlive(entry);
            if (entry.Status == AIStatus.Dead || entry.Status == AIStatus.Despawned)
            {
                if (!persistent && _clientReplicas.TryGetValue(entry.Id, out var replica))
                {
                    DestroyReplica(replica);
                    _clientReplicas.Remove(entry.Id);
                }
                _clientRequested.Remove(entry.Id);
                continue;
            }

            if (!entry.Activated)
            {
                if (_clientReplicas.TryGetValue(entry.Id, out var replica))
                {
                    DestroyReplica(replica);
                    _clientReplicas.Remove(entry.Id);
                }
                _clientRequested.Remove(entry.Id);
                continue;
            }

            var anchor = entry.LastKnownPosition != Vector3.zero ? entry.LastKnownPosition : entry.SpawnPosition;
            var distance = Vector3.Distance(playerPos, anchor);

            if (distance <= ActivationRadius)
            {
                if (_clientReplicas.ContainsKey(entry.Id))
                {
                    _clientRequested.Remove(entry.Id);
                    continue;
                }

                _clientRequested.TryGetValue(entry.Id, out var nextRetry);
                if (now >= nextRetry)
                {
                    var request = new AIActivationRequestRpc
                    {
                        Id = entry.Id,
                        Force = true
                    };
                    CoopTool.SendRpc(in request);
                    _clientRequested[entry.Id] = now + ActivationRetryInterval;
                }
            }
            else if (distance >= DeactivationRadius && !persistent)
            {
                if (_clientReplicas.TryGetValue(entry.Id, out var replica))
                {
                    DestroyReplica(replica);
                    _clientReplicas.Remove(entry.Id);
                }
                _clientRequested.Remove(entry.Id);
            }
        }
    }

    private void Client_ProcessPendingSnapshots()
    {
        if (_pendingSnapshotReset)
        {
            foreach (var replica in _clientReplicas.Values)
                DestroyReplica(replica);
            _clientReplicas.Clear();
            _clientRequested.Clear();
            _clientPendingBuffs.Clear();

            foreach (var entry in CoopSyncDatabase.AI.Entries)
            {
                if (entry == null) continue;
                entry.Status = AIStatus.Dormant;
            }

            _pendingSnapshotReset = false;
        }

        var processed = 0;
        while (processed < MaxSnapshotAppliesPerFrame && _pendingSnapshotQueue.Count > 0)
        {
            var snap = _pendingSnapshotQueue.Dequeue();
            var entry = CoopSyncDatabase.AI.GetOrCreate(snap.Id);
            ApplySnapshot(entry, in snap);
            processed++;
        }
    }

    private void Client_ProcessPendingStateUpdates()
    {
        var processed = 0;
        while (processed < MaxStateUpdatesPerFrame && _pendingStateUpdates.Count > 0)
        {
            var message = _pendingStateUpdates.Dequeue();
            Client_ApplyStateUpdate(in message);
            processed++;
        }
    }

    private void CollectLoadedScenes()
    {
        _loadedSceneIndexBuffer.Clear();
        _loadedScenePathBuffer.Clear();

        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid())
                continue;

            _loadedSceneIndexBuffer.Add(scene.buildIndex);
            if (!string.IsNullOrEmpty(scene.path))
                _loadedScenePathBuffer.Add(scene.path);
            if (!string.IsNullOrEmpty(scene.name))
                _loadedScenePathBuffer.Add(scene.name);
        }
    }

    private bool IsEntrySceneLoaded(AISyncEntry entry)
    {
        if (entry == null)
            return false;

        if (_loadedSceneIndexBuffer.Count == 0 && _loadedScenePathBuffer.Count == 0)
            return true;

        if (entry.SceneBuildIndex != 0 && _loadedSceneIndexBuffer.Contains(entry.SceneBuildIndex))
            return true;

        if (!string.IsNullOrEmpty(entry.ScenePath) && _loadedScenePathBuffer.Contains(entry.ScenePath))
            return true;

        return false;
    }

    private void Client_ApplyStateUpdate(in AIStateUpdateRpc message)
    {
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
            return;

        if (!IsEntrySceneLoaded(entry))
            return;

        entry.LastKnownPosition = message.Position;
        entry.LastKnownRotation = message.Rotation;
        entry.LastKnownVelocity = message.Velocity;
        entry.LastKnownRemoteTime = message.Timestamp;
        entry.CurrentHealth = message.CurrentHealth;
        entry.LastStateReceivedTime = Time.unscaledTime;
        if (message.StatusOverride != AIStatus.Dormant)
            entry.Status = message.StatusOverride;

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
            stateHash = message.StateHash,
            normTime = message.NormTime
        };

        if (_clientReplicas.TryGetValue(message.Id, out var replica))
            replica.ApplyState(entry);
    }

    public void Client_HandleHealthBroadcast(AIHealthBroadcastRpc message)
    {
        if (IsServer) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
            return;

        if (message.MaxHealth > 0f)
            entry.MaxHealth = message.MaxHealth;
        entry.CurrentHealth = message.CurrentHealth;
        entry.BodyArmor = message.BodyArmor;
        entry.HeadArmor = message.HeadArmor;
        if (message.IsDead)
            entry.Status = AIStatus.Dead;

        if (_clientReplicas.TryGetValue(message.Id, out var replica))
            replica.ApplyHealth(entry.MaxHealth, entry.CurrentHealth, entry.BodyArmor, entry.HeadArmor);
    }

    public void Client_HandleBuffBroadcast(AIBuffBroadcastRpc message)
    {
        if (IsServer || message.BuffId == 0) return;
        if (!CoopSyncDatabase.AI.TryGet(message.Id, out var entry) || entry == null)
            return;

        AppendBuffState(entry.Buffs, message.WeaponTypeId, message.BuffId);

        if (_clientReplicas.TryGetValue(message.Id, out var replica) && replica != null)
            replica.ApplyBuff(message.WeaponTypeId, message.BuffId);
        else
            CacheClientPendingBuff(message.Id, message.WeaponTypeId, message.BuffId);
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

            entry.Activated = IsControllerActivated(controller, cmc);
            entry.SpawnPosition = controller.transform.position;
            entry.SpawnRotation = cmc.characterModel ? cmc.characterModel.transform.rotation : controller.transform.rotation;
            var scene = controller.gameObject.scene;
            entry.SceneBuildIndex = scene.buildIndex;
            entry.ScenePath = scene.path;
            entry.PositionKey = AISyncRegistry.ComputePositionKey(entry.SpawnPosition, entry.SceneBuildIndex, entry.ScenePath);
            entry.ModelName = cmc.characterModel ? NormalizePrefabName(cmc.characterModel.name) : cmc.name;
            entry.CustomFaceJson = TryCaptureFaceJson(cmc.characterModel);
            entry.CharacterPresetKey = cmc.characterPreset ? cmc.characterPreset.nameKey : entry.CharacterPresetKey;
            entry.Team = cmc.Team;
            entry.HideIfFoundEnemyName = NormalizePrefabName(controller.hideIfFoundEnemy ? controller.hideIfFoundEnemy.name : entry.HideIfFoundEnemyName);

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

            entry.Equipment.Clear();
            entry.Weapons.Clear();

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
                            entry.Weapons[key] = item.TypeID;
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
    }

    private HashSet<NetPeer> GetServerWatchers(int id)
    {
        if (!IsServer) return null;
        return _serverWatchers.TryGetValue(id, out var watchers) && watchers != null && watchers.Count > 0
            ? watchers
            : null;
    }

    private float GetNearestWatcherDistance(AISyncEntry entry, HashSet<NetPeer> watchers)
    {
        if (entry == null || watchers == null || watchers.Count == 0) return float.PositiveInfinity;

        var statuses = Service?.playerStatuses;
        if (statuses == null || statuses.Count == 0) return float.PositiveInfinity;

        var anchor = entry.LastKnownPosition != Vector3.zero ? entry.LastKnownPosition : entry.SpawnPosition;
        var best = float.PositiveInfinity;

        foreach (var peer in watchers)
        {
            if (peer == null || !statuses.TryGetValue(peer, out var st) || st == null) continue;
            var d = Vector3.Distance(anchor, st.Position);
            if (d < best) best = d;
        }

        return best;
    }

    private static float ResolveStateInterval(float nearestWatcherDistance)
    {
        if (!float.IsFinite(nearestWatcherDistance))
            return StateBroadcastInterval;

        if (nearestWatcherDistance <= 55f)
            return StateBroadcastInterval;

        if (nearestWatcherDistance <= 65f)
            return 0.3f;

        return float.PositiveInfinity;
    }

    private void BroadcastState(AISyncEntry entry, HashSet<NetPeer> watchers = null)
    {
        if (!IsServer || entry == null) return;

        watchers ??= GetServerWatchers(entry.Id);
        if (watchers == null || watchers.Count == 0)
            return;

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
            StateHash = sample.stateHash,
            NormTime = sample.normTime,
            Timestamp = Time.unscaledTimeAsDouble
        };

        var writer = RpcWriterPool.Rent();
        try
        {
            writer.Put((byte)descriptor.Op);
            message.Serialize(writer);
            foreach (var peer in watchers)
                peer?.Send(writer, descriptor.Delivery);
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

        var watchers = GetServerWatchers(entry.Id);
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
            Damage = damage ?? default
        };

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

    private bool EnsureServerControllerDeath(AISyncEntry entry, DamageForwardPayload? damage)
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

            info = damage.Value.ToDamageInfo(null, receiver);
            if (info.toDamageReceiver == null)
                info.toDamageReceiver = receiver;
        }
        else
        {
            info = new DamageInfo
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
            Entries = buffer.ToArray()
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

                _snapshotBuffer.Add(BuildSnapshotEntry(entry));
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
            HeadArmor = entry.HeadArmor
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
            var index = 0;
            foreach (var kv in entry.Weapons)
            {
                snapshot.WeaponSlots[index] = kv.Key;
                snapshot.WeaponItemTypeIds[index] = kv.Value;
                index++;
            }
        }
        else
        {
            snapshot.WeaponSlots = Array.Empty<string>();
            snapshot.WeaponItemTypeIds = Array.Empty<int>();
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

    private void ApplySnapshot(AISyncEntry entry, in AISnapshotEntry snapshot)
    {
        entry.Id = snapshot.Id;
        entry.SpawnPosition = snapshot.SpawnPosition;
        entry.SpawnRotation = snapshot.SpawnRotation;
        entry.ModelName = snapshot.ModelName;
        entry.CustomFaceJson = snapshot.CustomFaceJson;
        entry.CharacterPresetKey = snapshot.CharacterPresetKey;
        entry.HideIfFoundEnemyName = snapshot.HideIfFoundEnemyName;
        entry.SceneBuildIndex = snapshot.SceneBuildIndex;
        entry.ScenePath = snapshot.ScenePath;
        entry.Team = snapshot.Team;
        entry.Status = snapshot.Status;
        entry.Activated = snapshot.Activated;
        entry.MaxHealth = snapshot.MaxHealth;
        entry.CurrentHealth = snapshot.CurrentHealth;
        entry.BodyArmor = snapshot.BodyArmor;
        entry.HeadArmor = snapshot.HeadArmor;
        entry.LastKnownPosition = snapshot.SpawnPosition;
        entry.LastKnownRotation = snapshot.SpawnRotation;
        entry.LastKnownVelocity = Vector3.zero;
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

        entry.Buffs.Clear();
        if (snapshot.BuffWeaponTypeIds != null && snapshot.BuffIds != null)
        {
            var count = Math.Min(snapshot.BuffWeaponTypeIds.Length, snapshot.BuffIds.Length);
            for (var i = 0; i < count; i++)
            {
                var buffId = snapshot.BuffIds[i];
                if (buffId == 0) continue;
                entry.Buffs.Add(new AIBuffState
                {
                    WeaponTypeId = snapshot.BuffWeaponTypeIds[i],
                    BuffId = buffId
                });
            }
        }
        if (entry.Buffs.Count > MaxStoredBuffs)
            entry.Buffs.RemoveRange(0, entry.Buffs.Count - MaxStoredBuffs);
    }

    private async UniTaskVoid SpawnReplicaAsync(AISyncEntry entry)
    {
        try
        {
            if (entry == null) return;

            DestroyReplica(entry.Id);

            if (MultiSceneCore.Instance.SceneInfo.ID == "Base" && !string.IsNullOrEmpty(entry.HideIfFoundEnemyName))
            {
                return;
            }

            var prefab = ResolveModelPrefab(entry.ModelName);
            if (!prefab)
            {
                Debug.LogWarning($"[AI][CLIENT] Missing model prefab for {entry.ModelName}, using main character fallback");
                var mainFallback = CharacterMainControl.Main?.characterModel;
                if (mainFallback)
                    prefab = mainFallback;
            }

            var host = CharacterMainControl.Main;
            if (!prefab || !host) return;

            var characterItemInstance = await Coopbase.LoadOrCreateCharacterItemInstance();
           // var modelInstance = Object.Instantiate(prefab);
            //var instance = Object.Instantiate(host.gameObject, entry.SpawnPosition, entry.SpawnRotation);
            var characterMainControl = await LevelManager.Instance.CharacterCreator.CreateCharacter(characterItemInstance, prefab, entry.SpawnPosition, entry.SpawnRotation);
            var instance = characterMainControl ? characterMainControl.gameObject : null;
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
            cmc.SetTeam(entry.Team);
            MakeReplicaPassive(cmc);
            TryAttachHideIfFoundEnemyReplica(entry, instance, cmc);

            ApplyCustomFace(cmc.characterModel, entry.CustomFaceJson);
            await ApplyEquipmentAsync(cmc.characterModel, entry.Equipment, entry.Weapons);

            var health = cmc.Health;
            if (health)
            {
                health.autoInit = false;
                health.showHealthBar = true;
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

            if (!instance.GetComponent<AutoRequestHealthBar>())
                instance.AddComponent<AutoRequestHealthBar>();

            if(entry.Buffs.Count > 0)
            {
                foreach (var i in entry.Buffs)
                {
                    Debug.Log("buff "+i.BuffId);
                }
            }
           

            var replica = new RemoteAIReplica(entry.Id, instance, cmc, entry, this);
            _clientReplicas[entry.Id] = replica;
            replica.ApplyState(entry);
            replica.ApplyBuffList(entry.Buffs);
            FlushClientPendingBuffs(entry.Id, replica);
            cmc.gameObject.SetActive(false);
            cmc.gameObject.SetActive(true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AI][CLIENT] Spawn replica failed: {ex}");
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

    private static void DestroyReplica(RemoteAIReplica replica)
    {
        if (replica == null) return;
        replica.Dispose();
        if (replica.Instance)
            Object.Destroy(replica.Instance);
    }

    private static bool IsControllerActivated(AICharacterController controller, CharacterMainControl cmc)
    {
        if (!controller || !cmc) return false;
        return controller.isActiveAndEnabled
               && controller.gameObject.activeInHierarchy
               && cmc.isActiveAndEnabled
               && cmc.gameObject.activeInHierarchy;
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

    private static void AppendBuffState(List<AIBuffState> list, int weaponTypeId, int buffId)
    {
        if (list == null || buffId == 0) return;
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
            replica.ApplyBuff(weaponTypeId, buffId);

        _clientPendingBuffs.Remove(id);
    }

    private void ApplyBuffProxy(CharacterMainControl cmc, int weaponTypeId, int buffId)
    {
        if (!cmc || buffId == 0) return;

        COOPManager.ResolveBuffAsync(weaponTypeId, buffId)
            .ContinueWith(buff =>
            {
                if (buff == null || !cmc) return;

                RemoteAIReplicaTag tag = null;
                try
                {
                    tag = cmc.GetComponent<RemoteAIReplicaTag>();
                    if (tag != null)
                        tag.SuppressBuffForward = true;
                    cmc.AddBuff(buff, null, weaponTypeId);
                }
                finally
                {
                    if (tag != null)
                        tag.SuppressBuffForward = false;
                }
            })
            .Forget();
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
            }
        }
    }

    private CharacterModel ResolveModelPrefab(string modelName)
    {
        modelName = NormalizePrefabName(modelName);
        if (string.IsNullOrEmpty(modelName))
        {
            var main = CharacterMainControl.Main;
            return main ? main.characterModel : null;
        }

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

    private static async UniTask ApplyEquipmentAsync(CharacterModel model, Dictionary<string, int> equipment, Dictionary<string, int> weapons)
    {
        if (!model) return;

        var charItem = model.characterMainControl ? model.characterMainControl.CharacterItem : null;

        if (equipment != null)
        {
            foreach (var kv in equipment)
            {
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
            foreach (var kv in weapons)
            {
                var typeId = kv.Value;
                if (typeId <= 0) continue;
                try
                {
                    var item = await ItemAssetsCollection.InstantiateAsync(typeId);
                    var slot = kv.Key;
                    if (charItem != null)
                        charItem.TryPlug(item);

                    model.characterMainControl.ChangeHoldItem(item);
                }
                catch
                {
                }
            }
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

    private static AnimSample CaptureAnimSample(AICharacterController controller)
    {
        var sample = new AnimSample
        {
            t = Time.unscaledTimeAsDouble,
            stateHash = -1
        };

        try
        {
            var cmc = controller?.CharacterMainControl;
            var model = cmc ? cmc.characterModel : null;
            if (!model) return sample;

            CharacterAnimationControl_MagicBlend magic = null;
            CharacterAnimationControl basic = null;

            try { magic = model.GetComponent<CharacterAnimationControl_MagicBlend>(); }
            catch { }
            if (!magic)
            {
                try { basic = model.GetComponent<CharacterAnimationControl>(); }
                catch { }
            }

            var animator = magic ? magic.animator : basic ? basic.animator : null;
            if (!animator) return sample;

            sample.speed = animator.GetFloat("MoveSpeed");
            sample.dirX = animator.GetFloat("MoveDirX");
            sample.dirY = animator.GetFloat("MoveDirY");
            sample.dashing = animator.GetBool("Dashing");
            sample.attack = animator.GetBool("Attack");
            sample.hand = animator.GetInteger("HandState");
            sample.gunReady = animator.GetBool("GunReady");

            var state = animator.GetCurrentAnimatorStateInfo(0);
            sample.stateHash = state.shortNameHash;
            sample.normTime = state.normalizedTime;
        }
        catch
        {
        }

        return sample;
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
        private bool _suppressHealthReport;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

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
            ApplyTeam(entry.Team);
            var remoteTime = entry.LastKnownRemoteTime > 0d ? entry.LastKnownRemoteTime : Time.unscaledTimeAsDouble;
            _netInterp?.Push(entry.LastKnownPosition, entry.LastKnownRotation, remoteTime, entry.LastKnownVelocity);

            if (_netInterp == null)
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
                var sample = entry.LastAnimSample;
                if (sample.t <= 0d)
                    sample.t = Time.unscaledTimeAsDouble;
                _animInterp.Push(sample);
            }
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

            if (_netInterp != null)
                return;

            var nextPosition = Vector3.Lerp(Character.transform.position, _targetPosition, deltaTime * 8f);
            var nextRotation = Quaternion.Slerp(Character.transform.rotation, _targetRotation, deltaTime * 8f);
            Character.transform.SetPositionAndRotation(nextPosition, nextRotation);

            if (Character.characterModel)
                Character.characterModel.transform.rotation = nextRotation;
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
            if (_suppressHealthReport || _service == null || _health == null) return;
            _service.Client_ReportAiHealth(Id, _health, info);
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
