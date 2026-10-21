using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeAIAgentLifetimeProbe
{
    internal static async UniTask<object> Run(Assembly mod)
    {
        var item = await ItemAssetsCollection.InstantiateAsync(240);
        var root = new GameObject("RemoteAIAgentLifetimeTest");
        root.AddComponent(mod.GetType("EscapeFromDuckovCoopMod.RemoteAIReplicaTag"));
        var callbacks = AccessTools.Field(typeof(Item), "onUnpluggedFromSlot");
        Delegate[] Listeners() => ((Action<Item>)callbacks.GetValue(item))?.GetInvocationList() ?? Array.Empty<Delegate>();
        try
        {
            var agent = item.CreateHandheldAgent();
            agent.transform.SetParent(root.transform);
            var stale = 0;
            for (var i = 0; i < 20; i++)
            {
                var previous = agent;
                item.AgentUtilities.ReleaseActiveAgent();
                agent = item.CreateHandheldAgent();
                agent.transform.SetParent(root.transform);
                stale += Listeners().Count(d => ReferenceEquals(d.Target, previous));
            }
            var activeListeners = Listeners().Count(d => ReferenceEquals(d.Target, agent));
            ((Action<Item>)callbacks.GetValue(item))?.Invoke(item);
            var released = item.AgentUtilities.ActiveAgent == null;
            await UniTask.NextFrame();
            return new { replacements = 20, staleCallbacks = stale, activeListeners, unplugReleasedActiveAgent = released };
        }
        finally
        {
            Object.Destroy(root);
            if (item) Object.Destroy(item.gameObject);
        }
    }
}
