using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Scenes;
using UnityEngine.Events;

namespace EscapeFromDuckovCoopMod;

public static class AISpawnerTriggerNet
{
    private const float FallbackFindDistance = 6f;
    private const float DuplicateSuppressSeconds = 1.5f;
    private const byte FlagFromPlayerInteract = 1;
    private const byte TargetRoot = 0;
    private const byte TargetGroup = 1;
    private const byte TargetSelector = 2;
    private const byte TargetRandomSpawner = 3;
    private const byte TargetWaveSpawner = 4;

    [ThreadStatic] private static int ClientInteractDepth;

    private static readonly Dictionary<string, float> ClientRecentRequests = new();
    private static readonly Dictionary<string, float> ServerRecentRequests = new();

    private static readonly MethodInfo StartSpawnMethod =
        AccessTools.Method(typeof(CharacterSpawnerRoot), "StartSpawn");

    public static bool BeginClientInteractContext(InteractableBase interactable, CharacterMainControl character)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer || !interactable)
            return false;

        if (character && CharacterMainControl.Main && character != CharacterMainControl.Main)
            return false;

        ClientInteractDepth++;
        return true;
    }

    public static void EndClientInteractContext()
    {
        if (ClientInteractDepth > 0)
            ClientInteractDepth--;
    }

    public static bool Client_RequestStart(CharacterSpawnerRoot root)
    {
        return Client_RequestStart(root, root, TargetRoot);
    }

    public static bool Client_RequestStart(CharacterSpawnerGroup group)
    {
        return Client_RequestStart(ResolveRoot(group?.spawnerRoot, group), group, TargetGroup);
    }

    public static bool Client_RequestStart(CharacterSpawnerGroupSelector selector)
    {
        return Client_RequestStart(ResolveRoot(selector?.spawnerRoot, selector), selector, TargetSelector);
    }

    public static bool Client_RequestStart(RandomCharacterSpawner spawner)
    {
        return Client_RequestStart(ResolveRoot(spawner?.spawnerRoot, spawner), spawner, TargetRandomSpawner);
    }

    public static bool Client_RequestStart(WaveCharacterSpawner spawner)
    {
        return Client_RequestStart(ResolveRoot(spawner?.spawnerRoot, spawner), spawner, TargetWaveSpawner);
    }

    public static void Client_RequestInteractableEventSpawners(InteractableBase interactable)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer || !interactable)
            return;

        Client_RequestEventSpawners(interactable.OnInteractStartEvent);
        Client_RequestEventSpawners(interactable.OnInteractTimeoutEvent);
        Client_RequestEventSpawners(interactable.OnInteractFinishedEvent);
        Client_RequestEventSpawners(interactable.OnRequiredItemUsedEvent);
    }

    private static bool Client_RequestStart(CharacterSpawnerRoot root, Component source, byte targetKind)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer || !root || !source)
            return false;

        if (ClientInteractDepth <= 0)
            return false;

        var rpc = new AISpawnerTriggerRequestRpc
        {
            SpawnerGuid = root.SpawnerGuid,
            Position = source.transform.position,
            SceneId = GetCurrentSceneId(),
            TargetKind = targetKind,
            ComponentPath = BuildComponentPath(root.transform, source.transform),
            Flags = FlagFromPlayerInteract
        };

        if (ShouldSuppressDuplicate(ClientRecentRequests, BuildTriggerKey(rpc)))
            return true;

        Debug.Log($"[AISpawnerTrigger] client request kind={rpc.TargetKind} guid={rpc.SpawnerGuid} scene={rpc.SceneId} pos={rpc.Position} path={rpc.ComponentPath}");
        CoopTool.SendRpc(in rpc);
        return true;
    }

    public static void Server_HandleTriggerRequest(RpcContext context, AISpawnerTriggerRequestRpc message)
    {
        if (!context.IsServer)
            return;

        if ((message.Flags & FlagFromPlayerInteract) == 0)
            return;

        if (ShouldSuppressDuplicate(ServerRecentRequests, BuildTriggerKey(message)))
            return;

        var root = FindSpawnerRoot(message.SpawnerGuid, message.Position);
        if (!root)
        {
            Debug.LogWarning($"[AISpawnerTrigger] server missing spawner guid={message.SpawnerGuid} scene={message.SceneId} pos={message.Position}");
            return;
        }

        try
        {
            Debug.Log($"[AISpawnerTrigger] server start kind={message.TargetKind} guid={message.SpawnerGuid} scene={message.SceneId} root={root.name} path={message.ComponentPath}");
            RelaxDistanceGateForRemoteInteract(root);
            StartTarget(root, message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AISpawnerTrigger] server start failed guid={message.SpawnerGuid}: {ex.Message}");
        }
    }

    private static string BuildTriggerKey(AISpawnerTriggerRequestRpc message)
    {
        return $"{message.SceneId}|{message.SpawnerGuid}|{message.TargetKind}|{message.ComponentPath}";
    }

    private static bool ShouldSuppressDuplicate(Dictionary<string, float> recentRequests, string key)
    {
        var now = Time.unscaledTime;
        if (recentRequests.TryGetValue(key, out var last) && now - last < DuplicateSuppressSeconds)
            return true;

        recentRequests[key] = now;
        if (recentRequests.Count > 512)
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

    private static void StartTarget(CharacterSpawnerRoot root, AISpawnerTriggerRequestRpc message)
    {
        switch (message.TargetKind)
        {
            case TargetGroup:
                if (TryFindComponentByPath<CharacterSpawnerGroup>(root, message.ComponentPath, out var group) ||
                    TryFindNearestComponent(root, message.Position, out group))
                {
                    group.StartSpawn();
                    return;
                }

                break;

            case TargetSelector:
                if (TryFindComponentByPath<CharacterSpawnerGroupSelector>(root, message.ComponentPath, out var selector) ||
                    TryFindNearestComponent(root, message.Position, out selector))
                {
                    selector.StartSpawn();
                    return;
                }

                break;

            case TargetRandomSpawner:
                if (TryFindComponentByPath<RandomCharacterSpawner>(root, message.ComponentPath, out var spawner) ||
                    TryFindNearestComponent(root, message.Position, out spawner))
                {
                    spawner.StartSpawn();
                    return;
                }

                break;

            case TargetWaveSpawner:
                if (TryFindComponentByPath<WaveCharacterSpawner>(root, message.ComponentPath, out var waveSpawner) ||
                    TryFindNearestComponent(root, message.Position, out waveSpawner))
                {
                    waveSpawner.StartSpawn();
                    return;
                }

                break;
        }

        StartSpawnMethod?.Invoke(root, null);
    }

    private static void RelaxDistanceGateForRemoteInteract(CharacterSpawnerRoot root)
    {
        if (!root)
            return;

        try
        {
            var oldMin = root.minDistanceToPlayer;
            var oldMax = root.maxDistanceToPlayer;
            root.minDistanceToPlayer = 0f;
            root.maxDistanceToPlayer = Mathf.Max(oldMax, 999999f);

            if (oldMin > 0f || oldMax < root.maxDistanceToPlayer)
                Debug.Log($"[AISpawnerTrigger] relaxed distance gate guid={root.SpawnerGuid} min {oldMin}->0 max {oldMax}->{root.maxDistanceToPlayer}");
        }
        catch
        {
        }
    }

    private static void Client_RequestEventSpawners(UnityEventBase unityEvent)
    {
        if (unityEvent == null)
            return;

        var count = unityEvent.GetPersistentEventCount();
        for (var i = 0; i < count; i++)
        {
            var target = unityEvent.GetPersistentTarget(i);
            Client_RequestSpawnerTarget(target);
        }
    }

    private static void Client_RequestSpawnerTarget(UnityEngine.Object target)
    {
        if (!target)
            return;

        switch (target)
        {
            case CharacterSpawnerRoot root:
                Client_RequestStart(root);
                return;
            case CharacterSpawnerGroup group:
                Client_RequestStart(group);
                return;
            case CharacterSpawnerGroupSelector selector:
                Client_RequestStart(selector);
                return;
            case RandomCharacterSpawner spawner:
                Client_RequestStart(spawner);
                return;
            case WaveCharacterSpawner spawner:
                Client_RequestStart(spawner);
                return;
        }

        if (target is GameObject go)
            Client_RequestSpawnerComponents(go);
        else if (target is Component component)
            Client_RequestSpawnerComponents(component.gameObject);
    }

    private static void Client_RequestSpawnerComponents(GameObject go)
    {
        if (!go)
            return;

        foreach (var root in go.GetComponents<CharacterSpawnerRoot>())
            Client_RequestStart(root);
        foreach (var group in go.GetComponents<CharacterSpawnerGroup>())
            Client_RequestStart(group);
        foreach (var selector in go.GetComponents<CharacterSpawnerGroupSelector>())
            Client_RequestStart(selector);
        foreach (var spawner in go.GetComponents<RandomCharacterSpawner>())
            Client_RequestStart(spawner);
        foreach (var spawner in go.GetComponents<WaveCharacterSpawner>())
            Client_RequestStart(spawner);
    }

    private static CharacterSpawnerRoot FindSpawnerRoot(int spawnerGuid, Vector3 position)
    {
        var roots = CollectSpawnerRoots();
        if (roots == null || roots.Count == 0)
            return null;

        if (spawnerGuid != 0)
        {
            foreach (var root in roots)
            {
                if (root && root.SpawnerGuid == spawnerGuid)
                    return root;
            }
        }

        var maxSqr = FallbackFindDistance * FallbackFindDistance;
        CharacterSpawnerRoot best = null;
        var bestSqr = float.MaxValue;
        foreach (var root in roots)
        {
            if (!root)
                continue;

            var distSqr = (root.transform.position - position).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            best = root;
            bestSqr = distSqr;
        }

        return best;
    }

    private static CharacterSpawnerRoot ResolveRoot(CharacterSpawnerRoot knownRoot, Component source)
    {
        if (knownRoot)
            return knownRoot;

        return source ? source.GetComponentInParent<CharacterSpawnerRoot>() : null;
    }

    private static string BuildComponentPath(Transform root, Transform target)
    {
        if (!root || !target || root == target)
            return string.Empty;

        var indices = new List<int>();
        var current = target;
        while (current && current != root)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }

        if (current != root)
            return string.Empty;

        indices.Reverse();
        return string.Join("/", indices);
    }

    private static bool TryFindComponentByPath<T>(CharacterSpawnerRoot root, string path, out T component)
        where T : Component
    {
        component = null;
        if (!root || string.IsNullOrEmpty(path))
            return false;

        var current = root.transform;
        var parts = path.Split('/');
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var index) || index < 0 || index >= current.childCount)
                return false;

            current = current.GetChild(index);
        }

        component = current ? current.GetComponent<T>() : null;
        return component;
    }

    private static bool TryFindNearestComponent<T>(CharacterSpawnerRoot root, Vector3 position, out T component)
        where T : Component
    {
        component = null;
        if (!root)
            return false;

        var components = root.GetComponentsInChildren<T>(true);
        var maxSqr = FallbackFindDistance * FallbackFindDistance;
        var bestSqr = float.MaxValue;

        foreach (var candidate in components)
        {
            if (!candidate)
                continue;

            var distSqr = (candidate.transform.position - position).sqrMagnitude;
            if (distSqr > maxSqr || distSqr >= bestSqr)
                continue;

            component = candidate;
            bestSqr = distSqr;
        }

        return component;
    }

    private static List<CharacterSpawnerRoot> CollectSpawnerRoots()
    {
        try
        {
            return new List<CharacterSpawnerRoot>(
                UnityEngine.Object.FindObjectsByType<CharacterSpawnerRoot>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None));
        }
        catch
        {
            return null;
        }
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
}
