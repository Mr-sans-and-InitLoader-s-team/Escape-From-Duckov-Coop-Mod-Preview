using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeGunLifetimeProbe
{
    internal static async UniTask<object> Run()
    {
        var transient = new GameObject("TransientReplicaGun");
        try
        {
            var gun = transient.AddComponent<ItemAgent_Gun>();
            var update = AccessTools.Method(typeof(ItemAgent_Gun), "Update");
            for (var i = 0; i < 100; i++) update.Invoke(gun, null);
            await UniTask.Delay(150, ignoreTimeScale: true);
            var held = CharacterMainControl.Main?.GetGun();
            if (held && held.Item) update.Invoke(held, null);
            return new { emptyAgentSurvived = gun && !gun.Item, heldItem = held?.Item?.TypeID,
                heldScatter = held && held.Item ? (float?)held.MaxScatter : null };
        }
        finally { UnityEngine.Object.Destroy(transient); }
    }
}
