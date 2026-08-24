using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Scenes;
using UnityEngine.Events;

namespace EscapeFromDuckovCoopMod;

public static class InteractableEventNet
{
    private const float FindDistance = 4f;
    private const float DuplicateSuppressSeconds = 1.5f;
    private const byte PhaseFinish = 0;
    private const byte PhaseTimeout = 1;
    private const string SurvivalChallengeSceneId = "Level_SurivalChallenge_Main";
    private const int SurvivalChallengeArenaInteractKey = -886089368;

    [ThreadStatic] private static bool _serverApplyingFinish;
    private static readonly Dictionary<string, float> ClientRecentRequests = new();
    private static readonly Dictionary<string, float> ServerRecentRequests = new();

    private static readonly MethodInfo InteractableGetKeyMethod =
        AccessTools.Method(typeof(InteractableBase), "GetKey");

    private static readonly FieldInfo InteractableTimerField =
        AccessTools.Field(typeof(InteractableBase), "interactTimer");

    private static readonly MethodInfo InteractableOnTimeOutMethod =
        AccessTools.Method(typeof(InteractableBase), "OnTimeOut");

    private static readonly MethodInfo InteractableUseRequiredItemMethod =
        AccessTools.Method(typeof(InteractableBase), "UseRequiredItem");

    private static readonly FieldInfo InteractableTimeOutField =
        AccessTools.Field(typeof(InteractableBase), "timeOut");

    public static bool Client_TryRequestHostTimeout(InteractableBase interactable, CharacterMainControl character, float deltaTime)
    {
        if (!ShouldClientForward(interactable, character))
            return false;

        if (!HasAuthoritativeTargets(interactable.OnInteractTimeoutEvent))
            return false;

        if (!WillTimeout(interactable, deltaTime))
            return false;

        return Client_RequestHostRun(interactable, character, PhaseTimeout);
    }

    public static bool Client_TryRequestHostFinish(InteractableBase interactable, CharacterMainControl character)
    {
        if (!ShouldClientForward(interactable, character))
            return false;

        if (!ShouldForwardFinish(interactable))
            return false;

        return Client_RequestHostRun(interactable, character, PhaseFinish);
    }

    private static bool Client_RequestHostRun(
        InteractableBase interactable,
        CharacterMainControl character,
        byte phase)
    {
        var keyHash = ComputeInteractableKey(interactable);
        var rpc = new InteractableFinishRequestRpc
        {
            KeyHash = keyHash,
            Position = interactable.transform.position,
            SceneId = GetCurrentSceneId(),
            Name = interactable.name,
            Phase = phase
        };

        if (ShouldSuppressDuplicate(ClientRecentRequests, BuildRequestKey(rpc)))
            return true;

        if (!TryUseRequesterRequiredItem(interactable, character, phase, out var requireItemId))
        {
            if (interactable.Interacting)
                interactable.StopInteract();
            return true;
        }

        rpc.RequesterUsedRequiredItem = requireItemId != 0;
        rpc.RequireItemId = requireItemId;

        Debug.Log($"[InteractableEvent] client request phase={rpc.Phase} key={rpc.KeyHash} scene={rpc.SceneId} name={rpc.Name} pos={rpc.Position} usedItem={rpc.RequesterUsedRequiredItem} item={rpc.RequireItemId}");
        CoopTool.SendRpc(in rpc);
        var destroyLocalInteractable = ShouldDestroyLocalArenaInteractable(interactable, rpc);

        try
        {
            if (interactable.Interacting)
                interactable.StopInteract();
        }
        catch
        {
        }

        if (destroyLocalInteractable)
            DestroyLocalInteractable(interactable);

        return true;
    }

    private static bool TryUseRequesterRequiredItem(
        InteractableBase interactable,
        CharacterMainControl character,
        byte phase,
        out int requireItemId)
    {
        requireItemId = 0;
        if (!interactable || !interactable.requireItem)
            return true;

        var shouldUse = phase == PhaseFinish &&
                        interactable.whenToUseRequireItem ==
                        InteractableBase.WhenToUseRequireItemTypes.OnFinshed;
        shouldUse |= phase == PhaseTimeout &&
                     interactable.whenToUseRequireItem ==
                     InteractableBase.WhenToUseRequireItemTypes.OnTimeOut;
        if (!shouldUse)
            return true;

        if (!character || InteractableUseRequiredItemMethod == null)
            return false;

        var itemId = interactable.requireItemId;
        try
        {
            if (InteractableUseRequiredItemMethod.Invoke(interactable, new object[] { character }) is not true)
                return false;

            requireItemId = itemId;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[InteractableEvent] client required item use failed name={interactable.name} item={itemId}: {ex.Message}");
            return false;
        }
    }

    private static bool ShouldDestroyLocalArenaInteractable(InteractableBase interactable, in InteractableFinishRequestRpc rpc)
    {
        if (!interactable)
            return false;

        if (rpc.Phase != PhaseFinish)
            return false;

        if (!string.Equals(rpc.SceneId, SurvivalChallengeSceneId, StringComparison.Ordinal))
            return false;

        return rpc.KeyHash == SurvivalChallengeArenaInteractKey;
    }

    private static void DestroyLocalInteractable(InteractableBase interactable)
    {
        if (!interactable)
            return;

        try
        {
            if (interactable.Interacting)
                interactable.StopInteract();

            interactable.MarkerActive = false;
            if (interactable.interactCollider)
                interactable.interactCollider.enabled = false;

            var go = interactable.gameObject;
            interactable.enabled = false;

            Debug.Log($"[InteractableEvent] destroyed local arena interactable key={SurvivalChallengeArenaInteractKey} name={go.name}");
            UnityEngine.Object.Destroy(go);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[InteractableEvent] destroy local arena interactable failed: {ex.Message}");
        }
    }

    public static void Server_HandleFinishRequest(RpcContext context, in InteractableFinishRequestRpc message)
    {
        if (!context.IsServer)
            return;

        if (!SceneMatches(message.SceneId, GetCurrentSceneId()))
            return;

        if (ShouldSuppressDuplicate(ServerRecentRequests, BuildRequestKey(message)))
            return;

        var interactable = FindInteractable(message);
        if (!interactable)
        {
            Debug.LogWarning($"[InteractableEvent] server missing interactable key={message.KeyHash} scene={message.SceneId} name={message.Name} pos={message.Position}");
            return;
        }

        var restoreReusableRequirement = false;
        try
        {
            _serverApplyingFinish = true;
            Debug.Log($"[InteractableEvent] server run phase={message.Phase} key={message.KeyHash} scene={message.SceneId} name={interactable.name} usedItem={message.RequesterUsedRequiredItem} item={message.RequireItemId}");

            if (message.RequesterUsedRequiredItem)
            {
                if (message.RequireItemId <= 0 || interactable.requireItemId != message.RequireItemId)
                {
                    Debug.LogWarning($"[InteractableEvent] server rejected required item key={message.KeyHash} expected={interactable.requireItemId} actual={message.RequireItemId}");
                    return;
                }

                if (interactable.requireOnce)
                {
                    LevelDataBoolNet.ApplyRemoteRequiredItemUse(interactable);
                }
                else
                {
                    interactable.requireItem = false;
                    restoreReusableRequirement = true;
                }
            }

            if (message.Phase == PhaseTimeout)
                RunTimeoutThenFinish(interactable);
            else
                interactable.FinishInteract(CharacterMainControl.Main);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[InteractableEvent] server finish failed key={message.KeyHash} name={message.Name}: {ex.Message}");
        }
        finally
        {
            if (restoreReusableRequirement && interactable)
                interactable.requireItem = true;

            _serverApplyingFinish = false;
        }
    }

    private static bool ShouldClientForward(InteractableBase interactable, CharacterMainControl character)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer || !interactable)
            return false;

        if (character && CharacterMainControl.Main && character != CharacterMainControl.Main)
            return false;

        return true;
    }

    private static bool ShouldForwardFinish(InteractableBase interactable)
    {
        if (!interactable || _serverApplyingFinish)
            return false;

        return HasAuthoritativeTargets(interactable.OnInteractFinishedEvent) ||
               HasAuthoritativeTargets(interactable.OnInteractTimeoutEvent) ||
               HasAuthoritativeTargets(interactable.OnRequiredItemUsedEvent);
    }

    private static bool WillTimeout(InteractableBase interactable, float deltaTime)
    {
        if (!interactable || !interactable.Interacting)
            return false;

        try
        {
            var timer = InteractableTimerField != null && InteractableTimerField.GetValue(interactable) is float value
                ? value
                : 0f;

            return timer + Mathf.Max(0f, deltaTime) >= interactable.InteractTime;
        }
        catch
        {
            return false;
        }
    }

    private static void RunTimeoutThenFinish(InteractableBase interactable)
    {
        var character = CharacterMainControl.Main;

        try
        {
            InteractableTimeOutField?.SetValue(interactable, true);
            InteractableOnTimeOutMethod?.Invoke(interactable, null);
            interactable.OnInteractTimeoutEvent?.Invoke(character, interactable);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[InteractableEvent] server timeout event failed name={interactable.name}: {ex.Message}");
        }

        if (interactable.finishWhenTimeOut)
            interactable.FinishInteract(character);
        else if (interactable.Interacting)
            interactable.StopInteract();
    }

    private static bool HasAuthoritativeTargets(UnityEventBase unityEvent)
    {
        if (unityEvent == null)
            return false;

        var count = unityEvent.GetPersistentEventCount();
        for (var i = 0; i < count; i++)
        {
            if (IsAuthoritativeTarget(unityEvent.GetPersistentTarget(i)))
                return true;
        }

        return false;
    }

    private static bool IsAuthoritativeTarget(UnityEngine.Object target)
    {
        if (!target)
            return false;

        switch (target)
        {
            case global::Door:
            case CharacterSpawnerRoot:
            case CharacterSpawnerGroup:
            case CharacterSpawnerGroupSelector:
            case RandomCharacterSpawner:
            case WaveCharacterSpawner:
            case SetInLevelDataBoolProxy:
                return true;
        }

        var go = target switch
        {
            GameObject gameObject => gameObject,
            Component component => component.gameObject,
            _ => null
        };

        if (!go)
            return false;

        return go.GetComponent<global::Door>() ||
               go.GetComponent<CharacterSpawnerRoot>() ||
               go.GetComponent<CharacterSpawnerGroup>() ||
               go.GetComponent<CharacterSpawnerGroupSelector>() ||
               go.GetComponent<RandomCharacterSpawner>() ||
               go.GetComponent<WaveCharacterSpawner>() ||
               go.GetComponent<SetInLevelDataBoolProxy>();
    }

    private static InteractableBase FindInteractable(in InteractableFinishRequestRpc message)
    {
        var interactables = CollectInteractables();
        if (interactables == null || interactables.Count == 0)
            return null;

        InteractableBase best = null;
        var bestSqr = float.MaxValue;
        var maxSqr = FindDistance * FindDistance;

        foreach (var interactable in interactables)
        {
            if (!interactable)
                continue;

            var key = ComputeInteractableKey(interactable);
            if (message.KeyHash != 0 && key != message.KeyHash)
                continue;

            var distSqr = (interactable.transform.position - message.Position).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            best = interactable;
            bestSqr = distSqr;
        }

        if (best || message.KeyHash != 0)
            return best;

        foreach (var interactable in interactables)
        {
            if (!interactable)
                continue;

            if (!string.IsNullOrEmpty(message.Name) &&
                !string.Equals(interactable.name, message.Name, StringComparison.Ordinal))
                continue;

            var distSqr = (interactable.transform.position - message.Position).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            best = interactable;
            bestSqr = distSqr;
        }

        return best;
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

    private static string BuildRequestKey(in InteractableFinishRequestRpc message)
    {
        return $"{message.SceneId}|{message.KeyHash}|{message.Name}|{message.Phase}|{Mathf.RoundToInt(message.Position.x * 10f)}|{Mathf.RoundToInt(message.Position.y * 10f)}|{Mathf.RoundToInt(message.Position.z * 10f)}";
    }

    private static bool ShouldSuppressDuplicate(Dictionary<string, float> recentRequests, string key)
    {
        var now = Time.unscaledTime;
        if (recentRequests.TryGetValue(key, out var last) && now - last < DuplicateSuppressSeconds)
            return true;

        recentRequests[key] = now;
        if (recentRequests.Count > 256)
            PruneRecentRequests(recentRequests, now);

        return false;
    }

    private static void PruneRecentRequests(Dictionary<string, float> recentRequests, float now)
    {
        var expired = new List<string>();
        foreach (var entry in recentRequests)
        {
            if (now - entry.Value > DuplicateSuppressSeconds)
                expired.Add(entry.Key);
        }

        foreach (var key in expired)
            recentRequests.Remove(key);
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

    private static bool SceneMatches(string requestSceneId, string currentSceneId)
    {
        if (string.IsNullOrEmpty(requestSceneId) || string.IsNullOrEmpty(currentSceneId))
            return true;

        return string.Equals(requestSceneId, currentSceneId, StringComparison.Ordinal);
    }
}
