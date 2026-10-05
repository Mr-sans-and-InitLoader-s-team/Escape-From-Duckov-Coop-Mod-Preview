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

using Duckov.UI;
using Duckov.Scenes;
using UnityEngine.EventSystems;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LevelManager), "StartInit")]
internal static class Patch_Level_StartInit_Gate
{
    private static bool Prefix(LevelManager __instance, SceneLoadingContext context)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null) return true;
        if (mod.IsServer) return true;
        var sid = SceneInfoCollection.GetSceneID(__instance.gameObject.scene.buildIndex);
        if (string.Equals(sid, SceneInfoCollection.BaseSceneID, StringComparison.OrdinalIgnoreCase))
        {
            WaitingSynchronizationUI.Instance?.Hide();
            CoopLogSystem.WriteNetworkDiagnostic("[SCENE] Returning to base: original level initialization, no host gate");
            return true;
        }

        var needGate = SceneNet.Instance.sceneVoteActive || (mod.networkStarted && !mod.IsServer);
        if (!needGate) return true;

        RunAsync(__instance, context, sid).Forget();
        return false;
    }

    private static async UniTaskVoid RunAsync(LevelManager self, SceneLoadingContext ctx, string sceneId)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null) return;

        try { await SceneNet.Instance.Client_SceneGateAsync(sceneId); }
        catch (Exception e) { Debug.LogError($"[SCENE] Gate failed; continuing original initialization: {e}"); }

        try
        {
            if (!self || !self.gameObject.scene.isLoaded) return;
            var m = AccessTools.Method(typeof(LevelManager), "InitLevel", new[] { typeof(SceneLoadingContext) });
            if (m == null) throw new MissingMethodException(typeof(LevelManager).FullName, "InitLevel");
            CoopLogSystem.WriteNetworkDiagnostic($"[SCENE] Gate finished: initializing '{sceneId}'");
            m.Invoke(self, new object[] { ctx });
        }
        catch (Exception e)
        {
            Debug.LogError("[SCENE] StartInit gate -> InitLevel failed: " + e);
        }
    }
}

[HarmonyPatch(typeof(MapSelectionEntry), "OnPointerClick")]
internal static class Patch_Mapen_OnPointerClick
{
    private static bool Prefix(MapSelectionEntry __instance, PointerEventData eventData)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        TeleporterTravel.Request(__instance.SceneID, __instance.BeaconIndex);
        return false;
    }
}

[HarmonyPatch(typeof(MapSelectionView), "NotifyEntryClicked")]
internal static class Patch_MapSelectionView_NotifyEntryClicked_Authority
{
    private static bool Prefix(MapSelectionEntry mapSelectionEntry, PointerEventData eventData)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (mapSelectionEntry == null) return true;
        TeleporterTravel.Request(mapSelectionEntry.SceneID, mapSelectionEntry.BeaconIndex);
        return false;
    }
}
