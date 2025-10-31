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
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Slot), nameof(Slot.Plug))]
public static class Patch_Slot_Plug_PickupCleanup
{
    private const float PICK_RADIUS = 2.5f; // 与库存补丁保持一致的半径
    private const QueryTriggerInteraction QTI = QueryTriggerInteraction.Collide;
    private const int LAYER_MASK = ~0;

    // 原签名：bool Plug(Item otherItem, out Item unpluggedItem, bool dontForce = false, Slot[] acceptableSlot = null, int acceptableSlotMask = 0)
    private static void Postfix(Slot __instance, Item otherItem, Item unpluggedItem, bool __result)
    {
        if (!__result || otherItem == null) return;

        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;

        // --- 客户端：发拾取请求 + 本地销毁地上Agent ---
        if (!mod.IsServer)
        {
            // A) 直接命中：字典里就是这个 item 引用（非合堆最常见）
            if (TryFindId(COOPManager.ItemHandle.clientDroppedItems, otherItem, out var cid))
            {
                LocalDestroyAgent(otherItem);
                SendPickupReq(mod, cid);
                return;
            }

            // B) 合堆/引用变化：用近场 NetDropTag 反查 ID
            if (TryFindNearestTaggedId(otherItem, out var nearId))
            {
                LocalDestroyAgentById(COOPManager.ItemHandle.clientDroppedItems, nearId);
                SendPickupReq(mod, nearId);
            }

            return;
        }

        // --- 主机：本地销毁并广播 DESPAWN ---
        if (TryFindId(COOPManager.ItemHandle.serverDroppedItems, otherItem, out var sid))
        {
            ServerDespawn(mod, sid);
            return;
        }

        if (TryFindNearestTaggedId(otherItem, out var nearSid)) ServerDespawn(mod, nearSid);
    }

    // ========= 工具函数（与库存补丁同等逻辑，自包含） =========
    private static void SendPickupReq(ModBehaviourF mod, uint id)
    {
        var w = mod.writer;
        w.Reset();
        w.Put((byte)Op.ITEM_PICKUP_REQUEST);
        w.Put(id);
        mod.connectedPeer?.Send(w, DeliveryMethod.ReliableOrdered);
    }

    private static void ServerDespawn(ModBehaviourF mod, uint id)
    {
        if (COOPManager.ItemHandle.serverDroppedItems.TryGetValue(id, out var it) && it != null)
            LocalDestroyAgent(it);
        COOPManager.ItemHandle.serverDroppedItems.Remove(id);

        var w = mod.writer;
        w.Reset();
        w.Put((byte)Op.ITEM_DESPAWN);
        w.Put(id);
        mod.netManager.SendToAll(w, DeliveryMethod.ReliableOrdered);
    }

    private static void LocalDestroyAgent(Item it)
    {
        try
        {
            var ag = it.ActiveAgent;
            if (ag && ag.gameObject) Object.Destroy(ag.gameObject);
        }
        catch
        {
        }
    }

    private static void LocalDestroyAgentById(Dictionary<uint, Item> dict, uint id)
    {
        if (dict.TryGetValue(id, out var it) && it != null) LocalDestroyAgent(it);
    }

    private static bool TryFindId(Dictionary<uint, Item> dict, Item it, out uint id)
    {
        foreach (var kv in dict)
            if (ReferenceEquals(kv.Value, it))
            {
                id = kv.Key;
                return true;
            }

        id = 0;
        return false;
    }

    // 近场反查：以“被装备的物品”的位置（或其 ActiveAgent 位置）为圆心搜 NetDropTag
    private static bool TryFindNearestTaggedId(Item item, out uint id)
    {
        id = 0;
        if (item == null) return false;

        Vector3 center;
        try
        {
            var ag = item.ActiveAgent;
            center = ag ? ag.transform.position : item.transform.position;
        }
        catch
        {
            center = item.transform.position;
        }

        var cols = Physics.OverlapSphere(center, PICK_RADIUS, LAYER_MASK, QTI);
        var best = float.MaxValue;
        uint bestId = 0;

        foreach (var c in cols)
        {
            var tag = c.GetComponentInParent<NetDropTag>();
            if (tag == null) continue;
            var d2 = (c.transform.position - center).sqrMagnitude;
            if (d2 < best)
            {
                best = d2;
                bestId = tag.id;
            }
        }

        if (bestId != 0)
        {
            id = bestId;
            return true;
        }

        return false;
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