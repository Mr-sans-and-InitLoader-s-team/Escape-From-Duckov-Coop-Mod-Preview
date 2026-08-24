using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class LotteryBoxNet
{
    public const byte PhaseBegin = 0;
    public const byte PhaseResult = 1;
    public const byte PhaseEnd = 2;
    public const byte PhaseRollDisplay = 3;
    public const byte PhaseRollLight = 4;

    private const float FindDistance = 5f;

    [ThreadStatic] private static bool _applyingRemote;

    private static readonly MethodInfo InteractableGetKeyMethod =
        AccessTools.Method(typeof(InteractableBase), "GetKey");

    private static readonly FieldInfo InteractableRequireItemUsedField =
        AccessTools.Field(typeof(InteractableBase), "requireItemUsed");

    private static readonly FieldInfo CostTakerField =
        AccessTools.Field(typeof(LotteryBox), "costTaker");

    private static readonly FieldInfo InProgressField =
        AccessTools.Field(typeof(LotteryBox), "inProgress");

    private static readonly FieldInfo InteractableField =
        AccessTools.Field(typeof(LotteryBox), "interactable");

    private static readonly FieldInfo ResultField =
        AccessTools.Field(typeof(LotteryBox), "result");

    private static readonly FieldInfo OpenSfxField =
        AccessTools.Field(typeof(LotteryBox), "openSFX");

    private static readonly FieldInfo CloseSfxField =
        AccessTools.Field(typeof(LotteryBox), "closeSFX");

    private static readonly MethodInfo DisplayMethod =
        AccessTools.Method(typeof(LotteryBox), "Display");

    private static readonly MethodInfo SetColorMethod =
        AccessTools.Method(typeof(LotteryBox), "SetColor");

    private static readonly MethodInfo HideAllCachedGraphicsMethod =
        AccessTools.Method(typeof(LotteryBox), "HideAllCachedGraphics");

    private static readonly HashSet<int> SentResultForRun = new();
    private static readonly Dictionary<string, float> RecentOutbound = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, float> RecentInbound = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, PaymentGateState> PaymentGateStates = new();

    private readonly struct PaymentGateState
    {
        public PaymentGateState(CostTaker gate, Cost cost, bool requireItem, bool requireOnce, bool requireItemUsed)
        {
            Gate = gate;
            Cost = cost;
            RequireItem = requireItem;
            RequireOnce = requireOnce;
            RequireItemUsed = requireItemUsed;
        }

        public CostTaker Gate { get; }
        public Cost Cost { get; }
        public bool RequireItem { get; }
        public bool RequireOnce { get; }
        public bool RequireItemUsed { get; }
    }

    public static bool ApplyingRemote => _applyingRemote;

    public static bool TryPrepareLocalBegin(LotteryBox box)
    {
        if (!ShouldSyncLocal(box))
            return false;

        if (GetBool(InProgressField, box))
            return false;

        RememberKnownPaymentGateState(GetField<CostTaker>(CostTakerField, box));
        SentResultForRun.Remove(box.GetInstanceID());
        SetField(ResultField, box, null);
        return true;
    }

    public static void Local_BroadcastBegin(LotteryBox box)
    {
        if (!ShouldSyncLocal(box))
            return;

        SendLocal(box, PhaseBegin, 0);
    }

    public static void Local_TryBroadcastDisplay(LotteryBox box, int displayedTypeId)
    {
        if (!ShouldSyncLocal(box))
            return;

        var result = GetField<Item>(ResultField, box);
        if (result && result.TypeID == displayedTypeId)
        {
            var instanceId = box.GetInstanceID();
            if (!SentResultForRun.Add(instanceId))
                return;

            SendLocal(box, PhaseResult, displayedTypeId);
            return;
        }

        if (!GetBool(InProgressField, box) || result || displayedTypeId <= 0)
            return;

        SendLocal(box, PhaseRollDisplay, displayedTypeId);
    }

    public static void Local_TryBroadcastColor(LotteryBox box, Color color)
    {
        if (!ShouldSyncLocal(box))
            return;

        if (!GetBool(InProgressField, box))
            return;

        var result = GetField<Item>(ResultField, box);
        if (!result && IsRollLightColor(color))
        {
            SendLocal(box, PhaseRollLight, 0);
            return;
        }

        if (!IsNearlyBlack(color))
            return;

        if (!result)
            return;

        RestorePaymentGate(GetField<CostTaker>(CostTakerField, box));
        SendLocal(box, PhaseEnd, result.TypeID);
        SentResultForRun.Remove(box.GetInstanceID());
    }

    public static void HandleState(RpcContext context, LotteryBoxStateRpc message)
    {
        var service = context.Service;
        if (service == null || !service.networkStarted)
            return;

        if (ShouldSuppressDuplicate(RecentInbound, BuildRecentKey(message)))
            return;

        var box = FindBox(message);
        if (box)
            ApplyRemote(box, message);
        else
            Debug.LogWarning($"[LotteryBox] missing box phase={message.Phase} key={message.KeyHash} scene={message.SceneId} name={message.Name} pos={message.Position}");

        if (context.IsServer)
            CoopTool.SendRpc(in message, context.Sender);
    }

    private static void SendLocal(LotteryBox box, byte phase, int resultTypeId)
    {
        var service = NetService.Instance;
        if (service == null || !service.networkStarted || ApplyingRemote)
            return;

        var rpc = new LotteryBoxStateRpc
        {
            Phase = phase,
            KeyHash = ComputeInteractableKey(box),
            Position = box.transform.position,
            SceneId = GetCurrentSceneId(),
            Name = box.name,
            ResultTypeId = resultTypeId
        };

        if (ShouldSuppressDuplicate(RecentOutbound, BuildRecentKey(rpc)))
            return;

        Debug.Log($"[LotteryBox] send phase={phase} type={resultTypeId} key={rpc.KeyHash} scene={rpc.SceneId} name={rpc.Name} pos={rpc.Position}");
        CoopTool.SendRpc(in rpc);
    }

    private static void ApplyRemote(LotteryBox box, in LotteryBoxStateRpc message)
    {
        try
        {
            _applyingRemote = true;

            switch (message.Phase)
            {
                case PhaseBegin:
                    ApplyBegin(box);
                    break;
                case PhaseRollDisplay:
                    ApplyRollDisplay(box, message.ResultTypeId);
                    break;
                case PhaseRollLight:
                    ApplyRollLight(box);
                    break;
                case PhaseResult:
                    ApplyResult(box, message.ResultTypeId);
                    break;
                case PhaseEnd:
                    ApplyEnd(box);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LotteryBox] apply failed phase={message.Phase} type={message.ResultTypeId}: {ex.Message}");
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    private static void ApplyBegin(LotteryBox box)
    {
        var wasInProgress = GetBool(InProgressField, box);
        SetField(InProgressField, box, true);
        SetField(InteractableField, box, false);
        SetCostActive(box, false);
        if (!wasInProgress)
        {
            box.onBegin?.Invoke();
            PostSfx(box, OpenSfxField);
        }
    }

    private static void ApplyRollDisplay(LotteryBox box, int typeId)
    {
        if (typeId <= 0)
            return;

        ApplyBegin(box);
        DisplayMethod?.Invoke(box, new object[] { typeId });
        box.onRollStep?.Invoke();
    }

    private static void ApplyRollLight(LotteryBox box)
    {
        ApplyBegin(box);
        ApplyColor(box, Color.white * 0.6f);
        box.onRollBegin?.Invoke();
    }

    private static void ApplyResult(LotteryBox box, int resultTypeId)
    {
        if (resultTypeId <= 0)
            return;

        ApplyBegin(box);

        box.overrideInteractName = true;
        var meta = ItemAssetsCollection.GetMetaData(resultTypeId);
        if (!string.IsNullOrEmpty(meta.DisplayNameKey))
            box._overrideInteractNameKey = meta.DisplayNameKey;

        DisplayMethod?.Invoke(box, new object[] { resultTypeId });
        box.onShowResult?.Invoke();
        ApplyQualityColor(box, resultTypeId);
    }

    private static void ApplyEnd(LotteryBox box)
    {
        ApplyColor(box, Color.black);
        HideAllCachedGraphicsMethod?.Invoke(box, null);
        SetField(InteractableField, box, false);
        SetField(InProgressField, box, false);
        SetField(ResultField, box, null);
        SetCostActive(box, true);
        PostSfx(box, CloseSfxField);
        box.onEnd?.Invoke();
    }

    private static void ApplyQualityColor(LotteryBox box, int resultTypeId)
    {
        var quality = 0;
        try
        {
            quality = ItemAssetsCollection.GetMetaData(resultTypeId).quality;
        }
        catch
        {
        }

        quality = Mathf.Clamp(quality, 0, 6);
        var look = GameplayDataSettings.UIStyle.GetDisplayQualityLook((DisplayQuality)quality);
        ApplyColor(box, look.shadowColor);
    }

    private static void ApplyColor(LotteryBox box, Color color)
    {
        SetColorMethod?.Invoke(box, new object[] { color });
    }

    private static void SetCostActive(LotteryBox box, bool active)
    {
        var costTaker = GetField<CostTaker>(CostTakerField, box);
        if (!costTaker)
            return;

        if (!active)
        {
            RememberKnownPaymentGateState(costTaker);
        }
        else
        {
            RestorePaymentGate(costTaker);
        }

        if (costTaker.gameObject.activeSelf != active)
            costTaker.gameObject.SetActive(active);

        if (active)
            RestorePaymentGate(costTaker);
    }

    public static void RememberPaymentGateState(CostTaker costTaker)
    {
        if (!IsPaymentGate(costTaker))
            return;

        RememberKnownPaymentGateState(costTaker);
    }

    private static void RememberKnownPaymentGateState(CostTaker costTaker)
    {
        if (!costTaker)
            return;

        var id = costTaker.GetInstanceID();
        if (!PaymentGateStates.TryGetValue(id, out var state) || state.Gate != costTaker)
        {
            PaymentGateStates[id] = new PaymentGateState(
                costTaker,
                costTaker.Cost,
                costTaker.requireItem,
                costTaker.requireOnce,
                GetRequireItemUsed(costTaker));
        }
    }

    private static void RestorePaymentGate(CostTaker costTaker)
    {
        if (!costTaker)
            return;

        try
        {
            var id = costTaker.GetInstanceID();
            if (PaymentGateStates.TryGetValue(id, out var state) && state.Gate == costTaker)
            {
                costTaker.SetCost(state.Cost);
                costTaker.requireOnce = state.RequireOnce;
                costTaker.requireItem = state.RequireItem;
                InteractableRequireItemUsedField?.SetValue(
                    costTaker,
                    state.RequireItem ? false : state.RequireItemUsed);

                if (state.RequireItem)
                    ClearRequirementLevelData(costTaker);
            }
            else
            {
                PaymentGateStates.Remove(id);
                if (costTaker.requireItemId > 0)
                {
                    costTaker.requireItem = true;
                    InteractableRequireItemUsedField?.SetValue(costTaker, false);
                    ClearRequirementLevelData(costTaker);
                }
            }

            if (costTaker.interactCollider)
                costTaker.interactCollider.enabled = true;

            costTaker.enabled = true;
        }
        catch
        {
        }
    }

    public static bool IsPaymentGate(CostTaker costTaker)
    {
        if (!costTaker)
            return false;

        try
        {
            if (PaymentGateStates.TryGetValue(costTaker.GetInstanceID(), out var state) &&
                state.Gate == costTaker)
                return true;

            var paymentEvent = costTaker.onPayedUnityEvent;
            if (paymentEvent != null)
            {
                for (var i = 0; i < paymentEvent.GetPersistentEventCount(); i++)
                {
                    var target = paymentEvent.GetPersistentTarget(i);
                    if (target is LotteryBox)
                        return true;

                    if (target is Component component && component.GetComponent<LotteryBox>())
                        return true;

                    if (target is GameObject gameObject && gameObject.GetComponent<LotteryBox>())
                        return true;
                }
            }

            if (costTaker.GetComponentInParent<LotteryBox>(true))
                return true;

            var boxes = UnityEngine.Object.FindObjectsByType<LotteryBox>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var box in boxes)
            {
                if (box && GetField<CostTaker>(CostTakerField, box) == costTaker)
                    return true;
            }
        }
        catch
        {
        }

        return false;
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

    private static void ClearRequirementLevelData(InteractableBase interactable)
    {
        try
        {
            var core = MultiSceneCore.Instance;
            var key = ComputeInteractableKey(interactable);
            if (core != null && key != 0)
                core.inLevelData[key] = false;
        }
        catch
        {
        }
    }

    private static void PostSfx(LotteryBox box, FieldInfo field)
    {
        try
        {
            var sfx = field?.GetValue(box) as string;
            if (!string.IsNullOrEmpty(sfx))
                AudioManager.Post(sfx);
        }
        catch
        {
        }
    }

    private static LotteryBox FindBox(in LotteryBoxStateRpc message)
    {
        var boxes = UnityEngine.Object.FindObjectsOfType<LotteryBox>(true);
        if (boxes == null || boxes.Length == 0)
            return null;

        LotteryBox best = null;
        var bestSqr = float.MaxValue;
        var maxSqr = FindDistance * FindDistance;

        foreach (var box in boxes)
        {
            if (!box)
                continue;

            var key = ComputeInteractableKey(box);
            if (message.KeyHash != 0 && key != message.KeyHash)
                continue;

            var distSqr = (box.transform.position - message.Position).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            best = box;
            bestSqr = distSqr;
        }

        if (best)
            return best;

        foreach (var box in boxes)
        {
            if (!box)
                continue;

            if (!string.Equals(box.name, message.Name, StringComparison.Ordinal))
                continue;

            var distSqr = (box.transform.position - message.Position).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            best = box;
            bestSqr = distSqr;
        }

        return best;
    }

    private static bool ShouldSyncLocal(LotteryBox box)
    {
        var service = NetService.Instance;
        return box && service != null && service.networkStarted && !ApplyingRemote;
    }

    private static int ComputeInteractableKey(InteractableBase interactable)
    {
        if (!interactable)
            return 0;

        try
        {
            if (InteractableGetKeyMethod != null)
                return (int)InteractableGetKeyMethod.Invoke(interactable, null);
        }
        catch
        {
        }

        var p = interactable.transform.position * 10f;
        var key = new Vector3Int(
            Mathf.RoundToInt(p.x),
            Mathf.RoundToInt(p.y),
            Mathf.RoundToInt(p.z));
        return $"Intact_{key}".GetHashCode();
    }

    private static string GetCurrentSceneId()
    {
        try
        {
            var core = MultiSceneCore.Instance;
            if (core != null && core.SceneInfo != null && !string.IsNullOrEmpty(core.SceneInfo.ID))
                return core.SceneInfo.ID;
        }
        catch
        {
        }

        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty;
    }

    private static T GetField<T>(FieldInfo field, object instance)
        where T : class
    {
        try
        {
            return field?.GetValue(instance) as T;
        }
        catch
        {
            return null;
        }
    }

    private static bool GetBool(FieldInfo field, object instance)
    {
        try
        {
            return field != null && (bool)field.GetValue(instance);
        }
        catch
        {
            return false;
        }
    }

    private static void SetField(FieldInfo field, object instance, object value)
    {
        try
        {
            field?.SetValue(instance, value);
        }
        catch
        {
        }
    }

    private static bool IsNearlyBlack(Color color)
    {
        return color.r <= 0.001f && color.g <= 0.001f && color.b <= 0.001f;
    }

    private static bool IsRollLightColor(Color color)
    {
        return color.r > 0.45f && color.g > 0.45f && color.b > 0.45f &&
               Mathf.Abs(color.r - color.g) < 0.08f &&
               Mathf.Abs(color.r - color.b) < 0.08f;
    }

    private static string BuildRecentKey(in LotteryBoxStateRpc message)
    {
        return $"{message.SceneId}|{message.KeyHash}|{message.Name}|{message.Phase}|{message.ResultTypeId}|{Mathf.RoundToInt(message.Position.x * 10f)}|{Mathf.RoundToInt(message.Position.y * 10f)}|{Mathf.RoundToInt(message.Position.z * 10f)}";
    }

    private static bool ShouldSuppressDuplicate(Dictionary<string, float> recent, string key)
    {
        var now = Time.unscaledTime;
        if (recent.TryGetValue(key, out var last) && now - last < 0.25f)
            return true;

        recent[key] = now;
        if (recent.Count > 128)
            PruneRecent(recent, now);

        return false;
    }

    private static void PruneRecent(Dictionary<string, float> recent, float now)
    {
        foreach (var key in new List<string>(recent.Keys))
        {
            if (now - recent[key] > 3f)
                recent.Remove(key);
        }
    }
}
