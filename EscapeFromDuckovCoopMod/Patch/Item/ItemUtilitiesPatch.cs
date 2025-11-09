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

using System.Reflection;
using Duckov.UI;
using ItemStatsSystem;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class Patch_ItemUtilities_SendToPlayerCharacterInventory_FromLoot
{
    private static MethodBase TargetMethod()
    {
        var t = typeof(ItemUtilities);
        var m2 = AccessTools.Method(
            t,
            "SendToPlayerCharacterInventory",
            new[] { typeof(Item), typeof(bool) }
        );
        if (m2 != null)
            return m2;

        // 兼容可能存在的 5 参重载
        return AccessTools.Method(
            t,
            "SendToPlayerCharacterInventory",
            new[] { typeof(Item), typeof(bool), typeof(bool), typeof(Inventory), typeof(int) }
        );
    }

    // 只写 (Item item, bool dontMerge, ref bool __result)，别再写不存在的参数

    private static bool Prefix(Item item, bool dontMerge, ref bool __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer)
            return true;

        // 在 Loot.AddAt 的保护期内不兜底，避免复制
        if (LootUiGuards.InLootAddAt)
        {
            __result = false;
            return false;
        }

        // A) 物品本身就在公共容器的格子里：走 TAKE
        var inv = item ? item.InInventory : null;
        if (
            inv
            && LootboxDetectUtil.IsLootboxInventory(inv)
            && !LootboxDetectUtil.IsPrivateInventory(inv)
        )
        {
            var srcPos = inv.GetIndex(item);
            if (srcPos >= 0)
            {
                COOPManager.LootNet.Client_SendLootTakeRequest(inv, srcPos); // 目的地不指定，TAKE_OK 再落背包
                __result = true;
                return false;
            }
        }

        // B) 物品还插在“公共容器里的武器槽位”里：走 UNPLUG + takeToBackpack
        var slot = item ? item.PluggedIntoSlot : null;
        if (slot != null)
        {
            var master = slot.Master;
            while (master && master.PluggedIntoSlot != null)
                master = master.PluggedIntoSlot.Master;

            var srcLoot = master ? master.InInventory : null;
            if (!srcLoot)
                try
                {
                    var lv = LootView.Instance;
                    if (lv)
                        srcLoot = lv.TargetInventory;
                }
                catch { }

            if (
                srcLoot
                && LootboxDetectUtil.IsLootboxInventory(srcLoot)
                && !LootboxDetectUtil.IsPrivateInventory(srcLoot)
            )
            {
                Debug.Log(
                    "[Coop] SendToPlayerCharInv (slot->backpack) -> send UNPLUG(takeToBackpack=true)"
                );
                // 不指定落位；TAKE_OK 时走默认背包吸收（你在 Client_OnLootTakeOk 里已有逻辑）
                COOPManager.LootNet.Client_RequestLootSlotUnplug(
                    srcLoot,
                    master,
                    slot.Key,
                    true,
                    0
                );
                __result = true;
                return false;
            }
        }

        // 其它情况走原生
        return true;
    }
}

[HarmonyPatch(typeof(ItemUtilities), "AddAndMerge")]
internal static class Patch_ItemUtilities_AddAndMerge_LootPut
{
    private static bool Prefix(
        Inventory inventory,
        Item item,
        int preferedFirstPosition,
        ref bool __result
    )
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted)
            return true;

        //  同样仅限“战利品容器初始化”时屏蔽
        if (!m.IsServer && m.ClientLootSetupActive)
        {
            var isLootInv =
                LootboxDetectUtil.IsLootboxInventory(inventory)
                && !LootboxDetectUtil.IsPrivateInventory(inventory);
            if (isLootInv)
            {
                try
                {
                    if (item)
                    {
                        item.Detach();
                        Object.Destroy(item.gameObject);
                    }
                }
                catch { }

                __result = true;
                return false;
            }
        }

        if (!m.IsServer && !COOPManager.LootNet._applyingLootState)
        {
            var isLootInv =
                LootboxDetectUtil.IsLootboxInventory(inventory)
                && !LootboxDetectUtil.IsPrivateInventory(inventory);
            if (isLootInv)
            {
                COOPManager.LootNet.Client_SendLootPutRequest(
                    inventory,
                    item,
                    preferedFirstPosition
                );
                __result = false;
                return false;
            }
        }

        return true;
    }

    private static void Postfix(
        Inventory inventory,
        Item item,
        int preferedFirstPosition,
        bool __result
    )
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || !m.IsServer)
            return;
        if (!__result || COOPManager.LootNet._serverApplyingLoot)
            return;

        // ✅ 修复：场景切换时 LevelManager 可能正在初始化，跳过同步避免崩溃
        try
        {
            if (LevelManager.Instance == null || LevelManager.LootBoxInventories == null)
            {
                return; // 场景初始化中，跳过
            }
        }
        catch
        {
            return; // 访问 LootBoxInventories 失败，说明场景正在切换
        }

        // ✅ 优化：延迟到帧结束时执行，减少场景加载时的性能压力
        DeferedRunner.EndOfFrame(() =>
        {
            var isLootInv =
                LootboxDetectUtil.IsLootboxInventory(inventory)
                && !LootboxDetectUtil.IsPrivateInventory(inventory);
            if (isLootInv)
                COOPManager.LootNet.Server_SendLootboxState(null, inventory);
        });
    }
}

// 2) 优先拦截 AddAndMerge：若是“容器内拆分的新堆”，改发 SPLIT
[HarmonyPatch(typeof(ItemUtilities), "AddAndMerge")]
[HarmonyPriority(Priority.First)] // 一定要先于你现有的 AddAndMerge 拦截执行
internal static class Patch_AddAndMerge_SplitFirst
{
    private static bool Prefix(
        Inventory inventory,
        Item item,
        int preferedFirstPosition,
        ref bool __result
    )
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer)
            return true; // 主机 / 未联网：放行

        if (inventory == null || item == null)
            return true;
        if (
            !LootboxDetectUtil.IsLootboxInventory(inventory)
            || LootboxDetectUtil.IsPrivateInventory(inventory)
        )
            return true;

        // 是不是刚刚“容器内拆分”出来的那个新堆？
        if (!ModBehaviourF.map.TryGetValue(item.GetInstanceID(), out var p))
            return true;
        if (!ReferenceEquals(p.inv, inventory))
            return true; // 必须是同一个容器内拆分

        // 发“拆分”请求：由主机把 srcPos 减 count，并尽量放在 preferedFirstPosition（或就近空格）
        COOPManager.LootNet.Client_SendLootSplitRequest(
            inventory,
            p.srcPos,
            p.count,
            preferedFirstPosition
        );

        // 清理本地临时 newItem，避免和主机广播的正式实体重复
        try
        {
            if (item)
            {
                item.Detach();
                Object.Destroy(item.gameObject);
            }
        }
        catch { }

        ModBehaviourF.map.Remove(item.GetInstanceID());

        __result = true; // 告诉上层“处理完成”
        return false; // 不要执行原方法（否则又会 PUT 一遍）
    }
}

// 修正：拦截 AddAndMerge 时的参数名必须与原方法一致
[HarmonyPatch(typeof(ItemUtilities), nameof(ItemUtilities.AddAndMerge))]
internal static class Patch_ItemUtilities_AddAndMerge_InterceptSlotToBackpack
{
    // 原方法签名：static bool AddAndMerge(Inventory inventory, Item item, int preferedFirstPosition)
    private static bool Prefix(
        Inventory inventory,
        Item item,
        int preferedFirstPosition,
        ref bool __result
    )
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer)
            return true;
        if (COOPManager.LootNet._applyingLootState)
            return true;

        // 目标必须是私有库存（背包/身上/宠物包）
        if (!inventory || !LootboxDetectUtil.IsPrivateInventory(inventory))
            return true;

        var slot = item ? item.PluggedIntoSlot : null;
        if (slot == null)
            return true;

        // 提升到最外层主件 + 源容器兜底（LootView）
        var master = slot.Master;
        while (master && master.PluggedIntoSlot != null)
            master = master.PluggedIntoSlot.Master;

        var srcLoot = master ? master.InInventory : null;
        if (!srcLoot)
            try
            {
                var lv = LootView.Instance;
                if (lv)
                    srcLoot = lv.TargetInventory;
            }
            catch { }

        if (
            srcLoot
            && LootboxDetectUtil.IsLootboxInventory(srcLoot)
            && !LootboxDetectUtil.IsPrivateInventory(srcLoot)
        )
        {
            Debug.Log(
                $"[Coop] AddAndMerge(slot->backpack) -> UNPLUG(takeToBackpack), prefer={preferedFirstPosition}"
            );
            // 直接携带目标格（preferedFirstPosition）做精确落位
            COOPManager.LootNet.Client_RequestSlotUnplugToBackpack(
                srcLoot,
                master,
                slot.Key,
                inventory,
                preferedFirstPosition
            );
            __result = true;
            return false; // 阻止原生 AddAndMerge
        }

        // 源容器不明：阻止原方法以免触发“仍有父物体”的报错
        __result = false;
        return false;
    }
}
