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

public class ItemHandle
{
    public readonly HashSet<Item> _clientSpawnByServerItems = new(); // 客户端：标记“来自主机的生成”，防止 Prefix 误发请求
    public readonly HashSet<Item> _serverSpawnedFromClientItems = new(); // 主机：标记“来自客户端请求的生成”，防止 Postfix 二次广播
    public readonly Dictionary<uint, Item> clientDroppedItems = new(); // 客户端记录（可用于拾取等后续）
    public readonly HashSet<uint> pendingLocalDropTokens = new();
    public readonly Dictionary<uint, Item> pendingTokenItems = new(); // 客户端：本地丢物时记录 token -> item
    public readonly Dictionary<uint, Item> serverDroppedItems = new(); // 主机记录
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;

    public void HandleItemDropRequest(NetPeer peer, NetPacketReader r)
    {
        if (!IsServer) return;
        var token = r.GetUInt();
        var pos = r.GetV3cm();
        var dir = r.GetDir();
        var angle = r.GetFloat();
        var create = r.GetBool();
        var snap = ItemTool.ReadItemSnapshot(r);

        // 在主机生成物体（并阻止 Postfix 再广播）
        var item = ItemTool.BuildItemFromSnapshot(snap);
        if (item == null) return;
        _serverSpawnedFromClientItems.Add(item);
        var agent = item.Drop(pos, create, dir, angle);

        // 分配唯一 id，入表
        var id = ItemTool.AllocateDropId();
        ItemTool.serverDroppedItems[id] = item;


        if (agent && agent.gameObject) ItemTool.AddNetDropTag(agent.gameObject, id);

        // 广播 SPAWN（包含 token，发回给所有客户端）
        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.ITEM_SPAWN);
        w.Put(token); // 回显客户端 token（发起者据此忽略）
        w.Put(id);
        w.PutV3cm(pos);
        w.PutDir(dir);
        w.Put(angle);
        w.Put(create);
        ItemTool.WriteItemSnapshot(w, item); // 用实际生成后的状态
        CoopTool.BroadcastReliable(w);
    }

    public void HandleItemSpawn(NetPacketReader r)
    {
        if (IsServer) return;
        var token = r.GetUInt();
        var id = r.GetUInt();
        var pos = r.GetV3cm();
        var dir = r.GetDir();
        var angle = r.GetFloat();
        var create = r.GetBool();
        var snap = ItemTool.ReadItemSnapshot(r);

        if (pendingLocalDropTokens.Remove(token))
        {
            if (pendingTokenItems.TryGetValue(token, out var localItem) && localItem != null)
            {
                clientDroppedItems[id] = localItem; // 主机id -> 本地item
                pendingTokenItems.Remove(token);

                ItemTool.AddNetDropTag(localItem, id);
            }
            else
            {
                // 回退重建一份
                var item2 = ItemTool.BuildItemFromSnapshot(snap);
                if (item2 != null)
                {
                    _clientSpawnByServerItems.Add(item2);
                    var agent2 = item2.Drop(pos, create, dir, angle);
                    clientDroppedItems[id] = item2;

                    if (agent2 && agent2.gameObject) ItemTool.AddNetDropTag(agent2.gameObject, id);
                }
            }

            return;
        }

        // 正常路径：主机发来的新掉落
        var item = ItemTool.BuildItemFromSnapshot(snap);
        if (item == null) return;

        _clientSpawnByServerItems.Add(item);
        var agent = item.Drop(pos, create, dir, angle);
        clientDroppedItems[id] = item;

        if (agent && agent.gameObject) ItemTool.AddNetDropTag(agent.gameObject, id);
    }

    public void HandleItemPickupRequest(NetPeer peer, NetPacketReader r)
    {
        if (!IsServer) return;
        var id = r.GetUInt();
        if (!serverDroppedItems.TryGetValue(id, out var item) || item == null)
            return; // 可能已经被别人拿走

        // 从映射表移除，并销毁场景 agent（若仍存在）
        serverDroppedItems.Remove(id);
        try
        {
            var agent = item.ActiveAgent;
            if (agent != null && agent.gameObject != null)
                Object.Destroy(agent.gameObject);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ITEM] 服务器销毁 agent 异常: {e.Message}");
        }

        // 广播 DESPAWN
        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.ITEM_DESPAWN);
        w.Put(id);
        CoopTool.BroadcastReliable(w);
    }

    public void HandleItemDespawn(NetPacketReader r)
    {
        if (IsServer) return;
        var id = r.GetUInt();
        if (ItemTool.clientDroppedItems.TryGetValue(id, out var item))
        {
            ItemTool.clientDroppedItems.Remove(id);
            try
            {
                var agent = item?.ActiveAgent;
                if (agent != null && agent.gameObject != null)
                    Object.Destroy(agent.gameObject);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ITEM] 客户端销毁 agent 异常: {e.Message}");
            }
        }
    }
}