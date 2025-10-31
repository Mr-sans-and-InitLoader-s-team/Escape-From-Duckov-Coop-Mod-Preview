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
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Item), "NotifyAddedToInventory")]
public static class Patch_Item_Pickup_NotifyAdded
{
    private static void Postfix(Item __instance, Inventory __0 /* inv */)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        // 只关注“主角相关”的拾取；例如 inv 属于玩家身上的背包/角色栏等
        // 如果游戏里还有其它 NPC/容器也会触发，可再加更精确的判断（比如 inv.AttachedToItem 是否是玩家背包）
        // —— 客户端逻辑：拾到自己客户端映射的掉落，就立刻销毁地面体并发拾取请求
        if (!mod.IsServer)
        {
            if (TryFindId(COOPManager.ItemHandle.clientDroppedItems, __instance, out var cid))
            {
                // 本地立刻把地面拾取体干掉（如果还在）
                try
                {
                    var ag = __instance.ActiveAgent;
                    if (ag && ag.gameObject) Object.Destroy(ag.gameObject);
                }
                catch
                {
                }

                // 发送拾取请求给主机（等主机广播 DESPAWN，让所有客户端一致删除）
                var w = mod.writer;
                w.Reset();
                w.Put((byte)Op.ITEM_PICKUP_REQUEST);
                w.Put(cid);
                mod.connectedPeer?.Send(w, DeliveryMethod.ReliableOrdered);
            }

            return;
        }

        // —— 主机逻辑：主机自己捡起主机表里的掉落，则直接移除并广播 DESPAWN
        if (mod.IsServer && TryFindId(COOPManager.ItemHandle.serverDroppedItems, __instance, out var sid))
        {
            COOPManager.ItemHandle.serverDroppedItems.Remove(sid);

            try
            {
                var ag = __instance.ActiveAgent;
                if (ag && ag.gameObject) Object.Destroy(ag.gameObject);
            }
            catch
            {
            }

            var w = mod.writer;
            w.Reset();
            w.Put((byte)Op.ITEM_DESPAWN);
            w.Put(sid);
            mod.netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
        }
    }

    // 小工具：ReferenceEquals 扫描映射
    private static bool TryFindId(Dictionary<uint, Item> dict, Item item, out uint id)
    {
        foreach (var kv in dict)
            if (ReferenceEquals(kv.Value, item))
            {
                id = kv.Key;
                return true;
            }

        id = 0;
        return false;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.Split), typeof(int))]
internal static class Patch_Item_Split_RecordForLoot
{
    private static void Postfix(Item __instance, int count, ref UniTask<Item> __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return;

        var srcInv = __instance ? __instance.InInventory : null;
        if (srcInv == null) return;
        if (!LootboxDetectUtil.IsLootboxInventory(srcInv) || LootboxDetectUtil.IsPrivateInventory(srcInv)) return;

        var srcPos = srcInv.GetIndex(__instance);
        if (srcPos < 0) return;

        __result = __result.ContinueWith(newItem =>
        {
            if (newItem != null)
                ModBehaviourF.map[newItem.GetInstanceID()] = new ModBehaviourF.Pending
                {
                    inv = srcInv,
                    srcPos = srcPos,
                    count = count
                };
            return newItem;
        });
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.Split), typeof(int))]
internal static class Patch_Item_Split_InterceptLoot_Prefix
{
    private static bool Prefix(Item __instance, int count, ref UniTask<Item> __result)
    {
        var m = ModBehaviourF.Instance;
        if (m == null || !m.networkStarted || m.IsServer) return true;

        var inv = __instance ? __instance.InInventory : null;
        if (inv == null) return true;
        if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv)) return true;

        // 源格基于当前客户端视图索引；若后续容器变化导致索引不匹配，主机会据此拒绝
        var srcPos = inv.GetIndex(__instance);
        if (srcPos < 0) return true;

        // 选择一个优先落位（尽量不合并）：先找 srcPos 后面的空格，再全表，最后交给主机 -1
        var prefer = inv.GetFirstEmptyPosition(srcPos + 1);
        if (prefer < 0) prefer = inv.GetFirstEmptyPosition();
        if (prefer < 0) prefer = -1;

        // 只发请求，不做本地拆分
        COOPManager.LootNet.Client_SendLootSplitRequest(inv, srcPos, count, prefer);

        // 立刻返回“没有本地新堆”，避免任何后续本地 Add/Merge 流程
        __result = UniTask.FromResult<Item>(null);
        return false;
    }
}