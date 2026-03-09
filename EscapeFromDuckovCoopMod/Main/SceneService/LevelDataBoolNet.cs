using System;
using System.Collections.Generic;
using Duckov.Scenes;
using LiteNetLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public static class LevelDataBoolNet
{
    public static void OnLocalSet(SetInLevelDataBoolProxy proxy)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || proxy == null || !mod.networkStarted) return;

        var key = proxy.keyString;
        var value = proxy.targetValue;
        if (string.IsNullOrEmpty(key)) return;

        if (mod.IsServer)
        {
            ApplyToAllProxies(key, value);
            Broadcast(key, value);
        }
        else
        {
            SendRequest(key, value);
        }
    }

    public static void HandleRpc(RpcContext context, in EnvLevelDataBoolRpc rpc)
    {
        var key = rpc.KeyString;
        if (string.IsNullOrEmpty(key)) return;

        ApplyToAllProxies(key, rpc.Value);

        if (context.IsServer)
            Broadcast(key, rpc.Value);
    }

    private static void SendRequest(string key, bool value)
    {
        var rpc = new EnvLevelDataBoolRpc
        {
            KeyString = key,
            Value = value
        };

        CoopTool.SendRpc(in rpc);
    }

    private static void Broadcast(string key, bool value, NetPeer exclude = null)
    {
        var rpc = new EnvLevelDataBoolRpc
        {
            KeyString = key,
            Value = value
        };

        CoopTool.SendRpc(in rpc, exclude);
    }

    private static void ApplyToAllProxies(string key, bool value)
    {
        try
        {
            UpdateLevelData(key, value);

            var proxies = CollectProxies();
            if (proxies == null || proxies.Count == 0) return;

            foreach (var proxy in proxies)
            {
                if (proxy == null) continue;
                if (!string.Equals(proxy.keyString, key, StringComparison.Ordinal)) continue;

                proxy.SetTo(value);
            }
        }
        catch
        {
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

    private static void UpdateLevelData(string key, bool value)
    {
        try
        {
            var core = MultiSceneCore.Instance;
            if (core == null) return;

            var hash = key.GetHashCode();
            core.inLevelData[hash] = value;
        }
        catch
        {
        }
    }
}
