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

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(ItemExtensions), nameof(ItemExtensions.Drop), typeof(Item), typeof(Vector3), typeof(bool), typeof(Vector3), typeof(float))]
public static class Patch_Item_Drop
{
    private static bool Prefix(Item item, Vector3 pos, bool createRigidbody, Vector3 dropDirection, float randomAngle)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;


        if (NetSilenceGuards.InPickupItem || NetSilenceGuards.InCapacityShrinkCleanup)
        {
            Debug.Log("[ITEM] 静音丢弃：拾取失败/容量清理导致的自动 Drop，不上报主机");
            return true;
        }

        if (mod.IsServer)
            // 服务器：正常执行，由 Postfix 负责广播（除非来自客户端请求）
            return true;

        // 客户端：若是"来自主机同步"的生成，放行且不再发请求
        if (COOPManager.ItemHandle._clientSpawnByServerItems.Remove(item))
            return true;


        // 客户端本地丢：先发送请求，再允许本地正常丢（这样本地背包立即变化）
        var token = ++ItemTool.nextLocalDropToken;
        COOPManager.ItemHandle.pendingLocalDropTokens.Add(token);
        COOPManager.ItemHandle.pendingTokenItems[token] = item;
        COOPManager.ItemRequest.SendItemDropRequest(token, item, pos, createRigidbody, dropDirection, randomAngle);
        return true;
    }

    private static void Postfix(Item item, DuckovItemAgent __result, Vector3 pos, bool createRigidbody, Vector3 dropDirection, float randomAngle)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;

        // 若这是“处理客户端请求”时生成的，别再次广播
        if (COOPManager.ItemHandle._serverSpawnedFromClientItems.Remove(item))
            return;

        // 仅在服务器分支
        if (NetSilenceGuards.InPickupItem)
        {
            Debug.Log("[SVR] 自动Drop（拾取失败回滚）——不广播SPAWN，避免复制");
            return;
        }

        try
        {
            // 主机自身（或 AI）本地丢弃：广播给客户端
            var w = mod.writer;
            w.Reset();
            w.Put((byte)Op.ITEM_SPAWN);
            w.Put((uint)0); // token=0，表示主机自发
            var id = ItemTool.AllocateDropId();
            COOPManager.ItemHandle.serverDroppedItems[id] = item;
            w.Put(id);
            w.PutV3cm(pos);
            w.PutDir(dropDirection);
            w.Put(randomAngle);
            w.Put(createRigidbody);
            ItemTool.WriteItemSnapshot(w, item);
            CoopTool.BroadcastReliable(w);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ITEM] 主机广播失败: {e}");
        }
    }
}

[HarmonyPatch]
public static class Patch_ItemExtensions_Drop_AddNetDropTag
{
    // 明确锁定到扩展方法 Drop(Item, Vector3, bool, Vector3, float)
    [HarmonyPatch(typeof(ItemExtensions), "Drop", typeof(Item), typeof(Vector3), typeof(bool), typeof(Vector3), typeof(float))]
    [HarmonyPostfix]
    private static void Postfix(
        // 扩展方法的第一个参数（this Item）
        Item item,
        Vector3 pos,
        bool createRigidbody,
        Vector3 dropDirection,
        float randomAngle,
        // 返回值必须用 ref 才能拿到
        ref DuckovItemAgent __result)
    {
        try
        {
            var agent = __result;
            if (agent == null) return;

            var go = agent.gameObject;
            if (go == null) return;

            // 已有就不重复加
            var tag = go.GetComponent<NetDropTag>();
            if (tag == null)
                tag = go.AddComponent<NetDropTag>();

            // 如果你需要在这里写入标识信息，可在此处补充
            // 例如：tag.itemTypeId = item?.TypeID ?? 0;
            // 或者 tag.ownerNetId = ModBehaviour.Instance?.LocalPlayerId ?? 0;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Harmony][Drop.Postfix] Add NetDropTag failed: {e}");
        }
    }
}