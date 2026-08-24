using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
using LiteNetLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class LevelDataBoolNet
{
    [ThreadStatic] private static bool _applyingRemoteProxy;
    [ThreadStatic] private static bool _applyingRemoteInteractable;
    [ThreadStatic] private static bool _applyingRemoteCostTaker;
    [ThreadStatic] private static CostTaker _activeCostTakerPayment;
    [ThreadStatic] private static bool _activeCostTakerPaySucceeded;
    private const float VehicleRequiredItemApplyDistance = 12f;
    private static readonly Dictionary<int, HashSet<int>> VehicleRequiredItemStates = new();
    private static readonly Dictionary<int, PaidCostTakerState> PaidCostTakerStates = new();

    private readonly struct PaidCostTakerState
    {
        public PaidCostTakerState(string sceneId, long costMoney)
        {
            SceneId = sceneId ?? string.Empty;
            CostMoney = costMoney;
        }

        public string SceneId { get; }
        public long CostMoney { get; }
    }

    private static readonly FieldInfo InteractableRequireItemUsedField =
        AccessTools.Field(typeof(InteractableBase), "requireItemUsed");

    private static readonly FieldInfo InteractableRequireItemDataKeyField =
        AccessTools.Field(typeof(InteractableBase), "requireItemDataKeyCached");

    private static readonly MethodInfo InteractableGetKeyMethod =
        AccessTools.Method(typeof(InteractableBase), "GetKey");

    public static bool IsApplyingRemoteProxy => _applyingRemoteProxy;

    public static void OnLocalSet(SetInLevelDataBoolProxy proxy)
    {
        if (proxy == null)
            return;

        OnLocalSet(proxy, proxy.targetValue);
    }

    public static void OnLocalSet(SetInLevelDataBoolProxy proxy, bool value)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || proxy == null || !mod.networkStarted) return;

        var key = proxy.keyString;
        if (string.IsNullOrEmpty(key)) return;

        if (mod.IsServer)
        {
            ApplyToAll(key, value, key.GetHashCode());
            Broadcast(key, value, key.GetHashCode());
        }
        else
        {
            SendRequest(key, value, key.GetHashCode());
        }
    }

    public static void OnInteractableRequiredItemUsed(InteractableBase interactable)
    {
        var mod = ModBehaviourF.Instance;
        if (_applyingRemoteInteractable || mod == null || interactable == null || !mod.networkStarted)
            return;

        if (IsLotteryBoxInteractable(interactable))
            return;

        if (!IsInteractableRequiredItemSatisfied(interactable))
            return;

        var hasKey = TryGetInteractableKey(interactable, out var keyHash);
        var key = BuildInteractableKeyString(interactable);
        ResolveVehicleRequiredItem(interactable, out var vehicleId, out var requireItemId);
        if (!hasKey && (vehicleId == 0 || requireItemId == 0))
            return;

        Debug.Log($"[LevelDataBool] required item used keyHash={keyHash} vehicle={vehicleId} item={requireItemId} server={mod.IsServer}");

        if (mod.IsServer)
        {
            ApplyToAll(key, true, keyHash, vehicleId, requireItemId);
            Broadcast(key, true, keyHash, vehicleId, requireItemId);
        }
        else
        {
            SendRequest(key, true, keyHash, vehicleId, requireItemId);
        }
    }

    public static void HandleRpc(RpcContext context, in EnvLevelDataBoolRpc rpc)
    {
        var key = rpc.KeyString;
        var keyHash = rpc.KeyHash != 0
            ? rpc.KeyHash
            : !string.IsNullOrEmpty(key)
                ? key.GetHashCode()
                : 0;
        if (string.IsNullOrEmpty(key) && keyHash == 0 && (rpc.VehicleId == 0 || rpc.RequireItemId == 0) && !rpc.CostTakerPaid)
            return;

        if (rpc.CostTakerPaid)
        {
            ApplyCostTakerPaidState(key, keyHash, rpc.CostMoney, rpc.SceneId);
            if (context.IsServer)
                BroadcastCostTakerPaid(key, keyHash, rpc.CostMoney, rpc.SceneId);
            return;
        }

        ApplyToAll(key, rpc.Value, keyHash, rpc.VehicleId, rpc.RequireItemId);

        if (context.IsServer)
            Broadcast(key, rpc.Value, keyHash, rpc.VehicleId, rpc.RequireItemId);
    }

    public static void Server_SendCostTakerSnapshot(NetPeer target)
    {
        if (target == null || PaidCostTakerStates.Count == 0)
            return;

        var sceneId = GetCurrentSceneId();
        foreach (var entry in PaidCostTakerStates)
        {
            if (!SceneMatches(entry.Value.SceneId, sceneId))
                continue;

            var rpc = new EnvLevelDataBoolRpc
            {
                KeyHash = entry.Key,
                Value = true,
                CostTakerPaid = true,
                CostMoney = entry.Value.CostMoney,
                SceneId = entry.Value.SceneId
            };

            CoopTool.SendRpcTo(target, in rpc);
        }
    }

    public static void BeginCostTakerPayment(CostTaker costTaker)
    {
        if (HasPerPlayerItemCost(costTaker) || IsLotteryBoxCostTaker(costTaker))
        {
            _activeCostTakerPayment = null;
            _activeCostTakerPaySucceeded = false;
            return;
        }

        _activeCostTakerPayment = costTaker;
        _activeCostTakerPaySucceeded = false;
    }

    public static void RecordCostTakerPayment()
    {
        if (_activeCostTakerPayment)
            _activeCostTakerPaySucceeded = true;
    }

    public static void EndCostTakerPayment(CostTaker costTaker)
    {
        try
        {
            if (_activeCostTakerPayment == costTaker && _activeCostTakerPaySucceeded)
                OnCostTakerPaid(costTaker);
        }
        finally
        {
            if (_activeCostTakerPayment == costTaker)
            {
                _activeCostTakerPayment = null;
                _activeCostTakerPaySucceeded = false;
            }
        }
    }

    public static void TryApplyCachedCostTakerPaid(CostTaker costTaker)
    {
        if (!costTaker)
            return;

        if (HasPerPlayerItemCost(costTaker) || IsLotteryBoxCostTaker(costTaker))
            return;

        if (!TryGetInteractableKey(costTaker, out var keyHash) || keyHash == 0)
            return;

        if (!PaidCostTakerStates.TryGetValue(keyHash, out var state))
            return;

        if (!SceneMatches(state.SceneId, GetCurrentSceneId()) || !CostMoneyMatches(costTaker, state.CostMoney))
            return;

        ApplyCostTakerPaid(costTaker, keyHash);
    }

    public static void OnCostTakerPaid(CostTaker costTaker)
    {
        var mod = ModBehaviourF.Instance;
        if (_applyingRemoteCostTaker || mod == null || costTaker == null || !mod.networkStarted)
            return;

        if (HasPerPlayerItemCost(costTaker) || IsLotteryBoxCostTaker(costTaker))
            return;

        if (costTaker.Cost.IsFree)
            return;

        var hasKey = TryGetInteractableKey(costTaker, out var keyHash);
        var key = BuildInteractableKeyString(costTaker);
        if (!hasKey && string.IsNullOrEmpty(key))
            return;

        if (keyHash == 0 && !string.IsNullOrEmpty(key))
            keyHash = key.GetHashCode();

        var costMoney = costTaker.Cost.money;
        var sceneId = GetCurrentSceneId();
        Debug.Log($"[LevelDataBool] cost taker paid keyHash={keyHash} money={costMoney} scene={sceneId} server={mod.IsServer}");

        RememberCostTakerPaid(keyHash, costMoney, sceneId);

        DeferedRunner.EndOfFrame(() => ApplyCostTakerPaidState(key, keyHash, costMoney, sceneId));

        if (mod.IsServer)
            BroadcastCostTakerPaid(key, keyHash, costMoney, sceneId);
        else
            SendCostTakerPaid(key, keyHash, costMoney, sceneId);
    }

    private static void SendRequest(string key, bool value, int keyHash, int vehicleId = 0, int requireItemId = 0)
    {
        var rpc = new EnvLevelDataBoolRpc
        {
            KeyString = key,
            Value = value,
            KeyHash = keyHash,
            VehicleId = vehicleId,
            RequireItemId = requireItemId
        };

        CoopTool.SendRpc(in rpc);
    }

    private static void SendCostTakerPaid(string key, int keyHash, long costMoney, string sceneId)
    {
        var rpc = new EnvLevelDataBoolRpc
        {
            KeyString = key,
            Value = true,
            KeyHash = keyHash,
            CostTakerPaid = true,
            CostMoney = costMoney,
            SceneId = sceneId
        };

        CoopTool.SendRpc(in rpc);
    }

    private static void Broadcast(string key, bool value, int keyHash, int vehicleId = 0, int requireItemId = 0, NetPeer exclude = null)
    {
        var rpc = new EnvLevelDataBoolRpc
        {
            KeyString = key,
            Value = value,
            KeyHash = keyHash,
            VehicleId = vehicleId,
            RequireItemId = requireItemId
        };

        CoopTool.SendRpc(in rpc, exclude);
    }

    private static void BroadcastCostTakerPaid(string key, int keyHash, long costMoney, string sceneId, NetPeer exclude = null)
    {
        var rpc = new EnvLevelDataBoolRpc
        {
            KeyString = key,
            Value = true,
            KeyHash = keyHash,
            CostTakerPaid = true,
            CostMoney = costMoney,
            SceneId = sceneId
        };

        CoopTool.SendRpc(in rpc, exclude);
    }

    private static void ApplyToAll(string key, bool value, int keyHash, int vehicleId = 0, int requireItemId = 0)
    {
        try
        {
            UpdateLevelData(key, value, keyHash);
            ApplyToAllProxies(key, value);
            ApplyToRequiredInteractables(keyHash, value);
            ApplyVehicleRequiredItemState(vehicleId, requireItemId, value);
        }
        catch
        {
        }
    }

    public static void TryApplyCachedVehicleRequiredItems(int vehicleId, CharacterMainControl vehicle)
    {
        if (vehicleId == 0 || !vehicle)
            return;

        if (!VehicleRequiredItemStates.TryGetValue(vehicleId, out var itemIds) || itemIds == null || itemIds.Count == 0)
            return;

        foreach (var itemId in itemIds)
            ApplyVehicleRequiredItemState(vehicleId, itemId, true, vehicle);
    }

    private static void ApplyToAllProxies(string key, bool value)
    {
        if (string.IsNullOrEmpty(key))
            return;

        var proxies = CollectProxies();
        if (proxies == null || proxies.Count == 0) return;

        try
        {
            _applyingRemoteProxy = true;

            foreach (var proxy in proxies)
            {
                if (proxy == null) continue;
                if (!string.Equals(proxy.keyString, key, StringComparison.Ordinal)) continue;

                proxy.SetTo(value);
            }
        }
        finally
        {
            _applyingRemoteProxy = false;
        }
    }

    private static List<SetInLevelDataBoolProxy> CollectProxies()
    {
        try
        {
            return new List<SetInLevelDataBoolProxy>(
                UnityEngine.Object.FindObjectsByType<SetInLevelDataBoolProxy>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));
        }
        catch
        {
            return null;
        }
    }

    private static List<InteractableBase> CollectInteractables()
    {
        try
        {
            return new List<InteractableBase>(
                UnityEngine.Object.FindObjectsByType<InteractableBase>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));
        }
        catch
        {
            return null;
        }
    }

    private static List<CostTaker> CollectCostTakers()
    {
        try
        {
            return new List<CostTaker>(
                UnityEngine.Object.FindObjectsByType<CostTaker>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));
        }
        catch
        {
            return null;
        }
    }

    private static void UpdateLevelData(string key, bool value, int keyHash)
    {
        try
        {
            var core = MultiSceneCore.Instance;
            if (core == null) return;

            var hash = keyHash != 0
                ? keyHash
                : !string.IsNullOrEmpty(key)
                    ? key.GetHashCode()
                    : 0;
            if (hash != 0)
                core.inLevelData[hash] = value;
        }
        catch
        {
        }
    }

    private static void RememberCostTakerPaid(int keyHash, long costMoney, string sceneId)
    {
        if (keyHash != 0)
            PaidCostTakerStates[keyHash] = new PaidCostTakerState(sceneId, costMoney);
    }

    private static void ApplyCostTakerPaidState(string key, int keyHash, long costMoney, string sceneId)
    {
        if (keyHash == 0 && !string.IsNullOrEmpty(key))
            keyHash = key.GetHashCode();

        if (keyHash == 0)
            return;

        RememberCostTakerPaid(keyHash, costMoney, sceneId);

        if (!SceneMatches(sceneId, GetCurrentSceneId()))
            return;

        var costTakers = CollectCostTakers();
        if (costTakers == null || costTakers.Count == 0)
            return;

        var applied = 0;
        foreach (var costTaker in costTakers)
        {
            if (!costTaker)
                continue;

            if (HasPerPlayerItemCost(costTaker) || IsLotteryBoxCostTaker(costTaker))
                continue;

            if (!CostMoneyMatches(costTaker, costMoney))
                continue;

            if (!TryGetInteractableKey(costTaker, out var costTakerKey) || costTakerKey != keyHash)
                continue;

            ApplyCostTakerPaid(costTaker, keyHash);
            applied++;
        }

        if (applied > 0)
            Debug.Log($"[LevelDataBool] applied cost taker paid keyHash={keyHash} money={costMoney} scene={sceneId} count={applied}");
    }

    private static string GetCurrentSceneId()
    {
        try
        {
            return MultiSceneCore.Instance?.SceneInfo?.ID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool SceneMatches(string stateSceneId, string currentSceneId)
    {
        if (string.IsNullOrEmpty(stateSceneId) || string.IsNullOrEmpty(currentSceneId))
            return true;

        return string.Equals(stateSceneId, currentSceneId, StringComparison.Ordinal);
    }

    private static bool CostMoneyMatches(CostTaker costTaker, long costMoney)
    {
        if (!costTaker || costMoney <= 0)
            return true;

        try
        {
            return costTaker.Cost.money == costMoney;
        }
        catch
        {
            return true;
        }
    }

    private static void ApplyCostTakerPaid(CostTaker costTaker, int keyHash)
    {
        if (!costTaker)
            return;

        if (HasPerPlayerItemCost(costTaker) || IsLotteryBoxCostTaker(costTaker))
            return;

        try
        {
            _applyingRemoteCostTaker = true;

            if (costTaker.Interacting)
                costTaker.StopInteract();

            costTaker.SetCost(new Cost(0L));
            if (costTaker.interactCollider)
                costTaker.interactCollider.enabled = true;
            costTaker.enabled = true;
        }
        catch
        {
        }
        finally
        {
            _applyingRemoteCostTaker = false;
        }
    }

    public static bool IsInteractableRequiredItemSatisfied(InteractableBase interactable)
    {
        if (!interactable)
            return false;

        if (!interactable.requireItem)
            return true;

        try
        {
            return InteractableRequireItemUsedField != null &&
                   InteractableRequireItemUsedField.GetValue(interactable) is bool used &&
                   used;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyRemoteRequiredItemUse(InteractableBase interactable)
    {
        if (!interactable)
            return;

        var keyHash = TryGetInteractableKey(interactable, out var key) ? key : 0;
        ApplyRequiredItemUsed(interactable, keyHash);
    }

    private static bool ResolveVehicleRequiredItem(InteractableBase interactable, out int vehicleId, out int requireItemId)
    {
        vehicleId = 0;
        requireItemId = 0;
        if (!interactable)
            return false;

        requireItemId = interactable.requireItemId;
        if (requireItemId == 0)
            return false;

        try
        {
            var parentVehicle = interactable.GetComponentInParent<CharacterMainControl>();
            if (parentVehicle && parentVehicle.isVehicle)
                vehicleId = SendLocalVehicleStatus.ResolveVehicleId(parentVehicle);
        }
        catch
        {
            vehicleId = 0;
        }

        if (vehicleId == 0)
            vehicleId = SendLocalVehicleStatus.FindNearestVehicleId(interactable.transform.position, VehicleRequiredItemApplyDistance);

        return vehicleId != 0;
    }

    private static string BuildInteractableKeyString(InteractableBase interactable)
    {
        if (!interactable)
            return null;

        if (interactable.overrideItemUsedKey)
            return interactable.overrideItemUsedSaveKey ?? string.Empty;

        var p = interactable.transform.position * 10f;
        var key = new Vector3Int(
            Mathf.RoundToInt(p.x),
            Mathf.RoundToInt(p.y),
            Mathf.RoundToInt(p.z));

        return $"Intact_{key}";
    }

    private static void ApplyToRequiredInteractables(int keyHash, bool value)
    {
        if (!value || keyHash == 0)
            return;

        var interactables = CollectInteractables();
        if (interactables == null || interactables.Count == 0)
            return;

        foreach (var interactable in interactables)
        {
            if (!interactable)
                continue;

            if (IsLotteryBoxInteractable(interactable))
                continue;

            if (!TryGetInteractableKey(interactable, out var interactableKey) || interactableKey != keyHash)
                continue;

            ApplyRequiredItemUsed(interactable, keyHash);
        }
    }

    private static void ApplyVehicleRequiredItemState(
        int vehicleId,
        int requireItemId,
        bool value,
        CharacterMainControl knownVehicle = null)
    {
        if (!value || vehicleId == 0 || requireItemId == 0)
            return;

        if (!VehicleRequiredItemStates.TryGetValue(vehicleId, out var itemIds))
        {
            itemIds = new HashSet<int>();
            VehicleRequiredItemStates[vehicleId] = itemIds;
        }

        itemIds.Add(requireItemId);

        var vehicle = knownVehicle ? knownVehicle : COOPManager.AI?.TryGetCharacter(vehicleId);
        var applied = 0;

        var interactables = CollectInteractables();
        if (interactables == null || interactables.Count == 0)
            return;

        var vehiclePos = vehicle
            ? vehicle.transform.position
            : CoopSyncDatabase.AI.TryGet(vehicleId, out var entry) && entry != null
                ? entry.LastKnownPosition != Vector3.zero ? entry.LastKnownPosition : entry.SpawnPosition
                : Vector3.zero;

        foreach (var interactable in interactables)
        {
            if (!interactable || interactable.requireItemId != requireItemId)
                continue;

            var belongsToVehicle = false;
            if (vehicle)
            {
                try
                {
                    belongsToVehicle = interactable.transform.IsChildOf(vehicle.transform);
                }
                catch
                {
                    belongsToVehicle = false;
                }
            }

            if (!belongsToVehicle && vehiclePos != Vector3.zero)
            {
                var distSqr = (interactable.transform.position - vehiclePos).sqrMagnitude;
                belongsToVehicle = distSqr <= VehicleRequiredItemApplyDistance * VehicleRequiredItemApplyDistance;
            }

            if (!belongsToVehicle)
                continue;

            var keyHash = TryGetInteractableKey(interactable, out var interactableKey) ? interactableKey : 0;
            ApplyRequiredItemUsed(interactable, keyHash);
            applied++;
        }

        if (applied > 0)
            Debug.Log($"[LevelDataBool] applied vehicle required item vehicle={vehicleId} item={requireItemId} count={applied}");
    }

    private static bool TryGetInteractableKey(InteractableBase interactable, out int key)
    {
        key = 0;
        if (!interactable)
            return false;

        try
        {
            if (InteractableRequireItemDataKeyField != null &&
                InteractableRequireItemDataKeyField.GetValue(interactable) is int cached &&
                cached != 0)
            {
                key = cached;
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (InteractableGetKeyMethod != null)
            {
                key = (int)InteractableGetKeyMethod.Invoke(interactable, null);
                if (key != 0)
                    return true;
            }
        }
        catch
        {
        }

        var keyString = BuildInteractableKeyString(interactable);
        if (string.IsNullOrEmpty(keyString))
            return false;

        key = keyString.GetHashCode();
        return true;
    }

    private static bool GetRequireItemUsed(InteractableBase interactable)
    {
        try
        {
            return InteractableRequireItemUsedField != null &&
                   InteractableRequireItemUsedField.GetValue(interactable) is bool used &&
                   used;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyRequiredItemUsed(InteractableBase interactable, int keyHash)
    {
        if (!interactable)
            return;

        if (IsLotteryBoxInteractable(interactable))
            return;

        var hadRequirement = interactable.requireItem || GetRequireItemUsed(interactable);
        if (!hadRequirement)
            return;

        var wasSatisfied = IsInteractableRequiredItemSatisfied(interactable);

        try
        {
            _applyingRemoteInteractable = true;

            interactable.requireItem = false;
            InteractableRequireItemUsedField?.SetValue(interactable, true);
            InteractableRequireItemDataKeyField?.SetValue(interactable, keyHash);

            var core = MultiSceneCore.Instance;
            if (core != null && keyHash != 0)
                core.inLevelData[keyHash] = true;

            if (!wasSatisfied)
                interactable.OnRequiredItemUsedEvent?.Invoke();
        }
        catch
        {
        }
        finally
        {
            _applyingRemoteInteractable = false;
        }
    }

    private static bool IsLotteryBoxCostTaker(CostTaker costTaker)
    {
        return costTaker && LotteryBoxNet.IsPaymentGate(costTaker);
    }

    private static bool HasPerPlayerItemCost(CostTaker costTaker)
    {
        if (!costTaker)
            return false;

        try
        {
            var items = costTaker.Cost.items;
            return items != null && items.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLotteryBoxInteractable(InteractableBase interactable)
    {
        if (!interactable)
            return false;

        try
        {
            if (interactable is CostTaker costTaker && LotteryBoxNet.IsPaymentGate(costTaker))
                return true;

            return interactable.GetComponentInParent<LotteryBox>(true);
        }
        catch
        {
            return false;
        }
    }
}
