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

using Duckov.Scenes;
using Duckov.Utilities;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LootBoxLoader), "Setup")]
internal static class Patch_LootBoxLoader_Setup_GuardClientInit
{
    private static void Prefix()
    {
        var m = ModBehaviourF.Instance;
        if (m != null && m.networkStarted && !m.IsServer)
            m._clientLootSetupDepth++;
    }

    // 用 Finalizer 确保异常时也能退出“初始化阶段”
    private static void Finalizer(Exception __exception)
    {
        var m = ModBehaviourF.Instance;
        if (m != null && m.networkStarted && !m.IsServer && m._clientLootSetupDepth > 0)
            m._clientLootSetupDepth--;
    }
}

[HarmonyPatch(typeof(LootBoxLoader), "Setup")]
internal static class Patch_LootBoxLoader_Setup_BroadcastOnServer
{
    private static async void Postfix(LootBoxLoader __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;
        await UniTask.Yield(); // 等一帧，确保物品都进箱子
        var box = __instance ? __instance.GetComponent<InteractableLootbox>() : null;
        var inv = box ? box.Inventory : null;
        if (inv != null) COOPManager.LootNet.Server_SendLootboxState(null, inv);
    }
}

[HarmonyPatch(typeof(LootBoxLoader), "RandomActive")]
internal static class Patch_LootBoxLoader_RandomActive_NetAuthority
{
    private static bool Prefix(LootBoxLoader __instance)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;

        try
        {
            var core = MultiSceneCore.Instance;
            if (core == null) return true; // 没 core 就让它走原逻辑，避免极端时序问题

            // 计算与游戏一致的 key（复制 GetKey 的算法）
            var key = ModBehaviour_ComputeLootKeyCompat(__instance.transform);


            if (core.inLevelData != null && core.inLevelData.TryGetValue(key, out var obj) && obj is bool on)
                __instance.gameObject.SetActive(on);
            else
                __instance.gameObject.SetActive(false); // 未拿到就先关

            return false; // 阻止原始随机
        }
        catch
        {
            return true; // 防守式：异常时走原逻辑
        }
    }

    private static int ModBehaviour_ComputeLootKeyCompat(Transform t)
    {
        if (t == null) return 0;
        var v = t.position * 10f;
        var x = Mathf.RoundToInt(v.x);
        var y = Mathf.RoundToInt(v.y);
        var z = Mathf.RoundToInt(v.z);
        var v3i = new Vector3Int(x, y, z);
        return v3i.GetHashCode();
    }
}