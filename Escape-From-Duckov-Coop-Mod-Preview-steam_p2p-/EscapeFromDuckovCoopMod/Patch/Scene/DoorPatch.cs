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

// ========= 客户端：拦截 Door.Open -> 发送请求给主机 =========
[HarmonyPatch(typeof(global::Door), nameof(global::Door.Open))]
internal static class Patch_Door_Open_ClientToServer
{
    private static bool Prefix(global::Door __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted) return true;
        if (m.IsServer) return true; // 主机放行
        if (Door._applyingDoor) return true; // 正在应用网络下发，放行

        COOPManager.Door.Client_RequestDoorSetState(__instance, false);
        return false; // 客户端不直接开门
    }
}

// ========= 客户端：拦截 Door.Close -> 发送请求给主机 =========
[HarmonyPatch(typeof(global::Door), nameof(global::Door.Close))]
internal static class Patch_Door_Close_ClientToServer
{
    private static bool Prefix(global::Door __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted) return true;
        if (m.IsServer) return true;
        if (Door._applyingDoor) return true;

        COOPManager.Door.Client_RequestDoorSetState(__instance, true);
        return false;
    }
}

// ========= 客户端：拦截 Door.Switch -> 发送请求给主机 =========
[HarmonyPatch(typeof(global::Door), nameof(global::Door.Switch))]
internal static class Patch_Door_Switch_ClientToServer
{
    private static bool Prefix(global::Door __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted) return true;
        if (m.IsServer) return true;
        if (Door._applyingDoor) return true;

        var isOpen = false;
        try
        {
            isOpen = __instance.IsOpen;
        }
        catch
        {
        }

        COOPManager.Door.Client_RequestDoorSetState(__instance, isOpen /* open->关，close->开 */);
        return false;
    }
}

// ========= 主机：任何地方调用 SetClosed 都广播给所有客户端 =========
[HarmonyPatch(typeof(global::Door), "SetClosed")]
internal static class Patch_Door_SetClosed_BroadcastOnServer
{
    private static void Postfix(global::Door __instance, bool _closed)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;

        var key = 0;
        try
        {
            key = (int)AccessTools.Field(typeof(global::Door), "doorClosedDataKeyCached").GetValue(__instance);
        }
        catch
        {
        }

        if (key == 0) key = COOPManager.Door.ComputeDoorKey(__instance.transform);
        if (key == 0) return;

        COOPManager.Door.Server_BroadcastDoorState(key, _closed);
    }
}