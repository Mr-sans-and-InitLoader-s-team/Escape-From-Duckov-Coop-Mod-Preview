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

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(global::ExitCreator), nameof(global::ExitCreator.Spawn))]
internal static class Patch_ExitCreator_Spawn
{
    private static bool Prefix(global::ExitCreator __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        COOPManager.ExitSync?.RegisterCreator(__instance);

        if (!mod.IsServer)
        {
            COOPManager.ExitSync?.Client_OnSpawnBlocked(__instance);
            return false;
        }

        COOPManager.ExitSync?.Server_OnSpawnStarting(__instance);
        return true;
    }

    private static void Postfix(global::ExitCreator __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        COOPManager.ExitSync?.Server_OnSpawnFinished(__instance);
    }
}

[HarmonyPatch(typeof(global::ExitCreator), "CreateExit")]
internal static class Patch_ExitCreator_CreateExit_ServerCapture
{
    private static void Postfix(global::ExitCreator __instance, Vector3 position, int sceneBuildIndex, int debugIndex)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        COOPManager.ExitSync?.Server_RecordExit(__instance, position, sceneBuildIndex);
    }
}
