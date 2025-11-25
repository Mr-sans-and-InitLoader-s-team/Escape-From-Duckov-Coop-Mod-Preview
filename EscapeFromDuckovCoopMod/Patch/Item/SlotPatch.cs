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

using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Slot), nameof(Slot.Plug))]
public static class Patch_Slot_Plug_PickupCleanup
{
    // 原签名：bool Plug(Item otherItem, out Item unpluggedItem, bool dontForce = false, Slot[] acceptableSlot = null, int acceptableSlotMask = 0)
    private static void Postfix(Slot __instance, Item otherItem, Item unpluggedItem, bool __result)
    {
        if (!__result || otherItem == null) return;

        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        var net = COOPManager.ItemNet;
        if (net == null) return;

        net.HandleSlotItemEquipped(otherItem);
    }
}

[HarmonyPatch(typeof(Slot), "Plug")]
internal static class Patch_Slot_Plug_BlockEquipFromLoot
{
    private static bool Prefix(Slot __instance, Item otherItem, ref Item unpluggedItem)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;
        if (COOPManager.LootNet._applyingLootState) return true;

        var inv = otherItem ? otherItem.InInventory : null;
        // ★ 排除私有库存
        if (LootboxDetectUtil.IsLootboxInventory(inv) && !LootboxDetectUtil.IsPrivateInventory(inv))
        {
            var srcPos = inv?.GetIndex(otherItem) ?? -1;
            COOPManager.LootNet.Client_SendLootTakeRequest(inv, srcPos, null, -1, __instance);
            unpluggedItem = null;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(SplitDialogue), "DoSplit")]
internal static class Patch_SplitDialogue_DoSplit_NetOnly
{
    private static bool Prefix(SplitDialogue __instance, int value, ref UniTask __result)
    {
        var m = ModBehaviourF.Instance;
        // 未联网 / 主机执行 / 没有 Mod 行为时，走原版
        if (m == null || !m.networkStarted || m.IsServer)
            return true;

        // 读取 SplitDialogue 的私有字段
        var tr = Traverse.Create(__instance);
        var target = tr.Field<Item>("target").Value;
        var destInv = tr.Field<Inventory>("destination").Value;
        var destIndex = tr.Field<int>("destinationIndex").Value;

        var inv = target ? target.InInventory : null;
        // 非容器（或私域容器）拆分，保留原版逻辑
        if (inv == null || !LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv))
            return true;

        // 源格（按当前客户端视图计算）
        var srcPos = inv.GetIndex(target);
        if (srcPos < 0)
        {
            __result = UniTask.CompletedTask;
            return false;
        }

        // 计算“优先落位”：如果用户是从容器拖到容器且目标格子为空，就强制落在那个格子
        var prefer = -1;
        if (destInv == inv && destIndex >= 0 && destIndex < inv.Capacity && inv.GetItemAt(destIndex) == null)
        {
            prefer = destIndex;
        }
        else
        {
            // 否则找就近空位；找不到就交给主机决定（-1）
            prefer = inv.GetFirstEmptyPosition(srcPos + 1);
            if (prefer < 0) prefer = inv.GetFirstEmptyPosition();
            if (prefer < 0) prefer = -1;
        }

        // 发请求给主机：仅网络，不在本地造新堆
        COOPManager.LootNet.Client_SendLootSplitRequest(inv, srcPos, value, prefer);


        // 友好点：切成 Busy→Complete→收起对话框（避免 UI 挂在“忙碌中”）
        try
        {
            tr.Method("Hide").GetValue();
        }
        catch
        {
        }

        __result = UniTask.CompletedTask;
        return false; // 阻止原方法，避免触发 <DoSplit>g__Send|24_0
    }
}

[HarmonyPatch(typeof(Slot), nameof(Slot.Plug))]
internal static class Patch_Slot_Plug_ClientRedirect
{
    private static bool Prefix(Slot __instance, Item otherItem, ref bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer || m.ClientLootSetupActive || COOPManager.LootNet._applyingLootState)
            return true; // 主机/初始化/套快照时放行原逻辑

        var master = __instance?.Master;
        var inv = master ? master.InInventory : null;
        if (!inv) return true;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv))
            return true; // 只拦“公共战利品容器”里的槽位

        if (!otherItem) return true;

        // 走网络：客户端 -> 主机
        COOPManager.LootNet.Client_RequestLootSlotPlug(inv, master, __instance.Key, otherItem);

        __result = true; // 让 UI 认为已处理，实际等主机广播来驱动可视变化
        return false; // 阻止本地真正 Plug
    }
}

// HarmonyFix.cs
[HarmonyPatch(typeof(Slot), nameof(Slot.Unplug))]
[HarmonyPriority(Priority.First)]
internal static class Patch_Slot_Unplug_ClientRedirect
{
    private static bool Prefix(Slot __instance, ref Item __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;
        if (COOPManager.LootNet.ApplyingLootState) return true;

        // 关键：用 Master.InInventory 判断该槽位属于哪个容器
        var inv = __instance?.Master ? __instance.Master.InInventory : null;
        if (inv == null) return true;
        // 仅在“公共战利品容器且非私有”时拦截
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv))
            return true;

        // 统一做法：本地完全不执行 Unplug，等待我们在 AddAt/​AddAndMerge/​SendToInventory 的前缀里走网络
        Debug.Log("[Coop] Slot.Unplug@Loot -> ignore (network-handled)");
        __result = null; // 别生成本地分离物
        return false; // 阻断原始 Unplug
    }
}

// Slot.Plug 主机在“容器里的武器”上装配件（目标 master 所在 Inventory 是容器）
[HarmonyPatch(typeof(Slot), nameof(Slot.Plug))]
internal static class Patch_ServerBroadcast_OnSlotPlug
{
    private static void Postfix(Slot __instance, Item otherItem, Item unpluggedItem, bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;
        if (!__result || COOPManager.LootNet._serverApplyingLoot) return;

        var master = __instance?.Master;
        var inv = master ? master.InInventory : null;
        if (!inv) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;

        if (LootManager.Instance.Server_IsLootMuted(inv)) return; // ★ 新增
        COOPManager.LootNet.Server_SendLootboxState(null, inv);
    }
}

// Slot.Unplug 主机在“容器里的武器”上拆配件
[HarmonyPatch(typeof(Slot), nameof(Slot.Unplug))]
internal static class Patch_ServerBroadcast_OnSlotUnplug
{
    private static void Postfix(Slot __instance, Item __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer) return;
        if (COOPManager.LootNet._serverApplyingLoot) return;

        var master = __instance?.Master;
        var inv = master ? master.InInventory : null;
        if (!inv) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return;

        COOPManager.LootNet.Server_SendLootboxState(null, inv);
    }
}