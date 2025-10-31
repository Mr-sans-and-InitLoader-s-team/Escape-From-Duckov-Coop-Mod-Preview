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

public class ItemRequest
{
    private NetService Service => NetService.Instance;


    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;

    public void SendItemDropRequest(uint token, Item item, Vector3 pos, bool createRb, Vector3 dir, float angle)
    {
        if (netManager == null || IsServer) return;
        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.ITEM_DROP_REQUEST);
        w.Put(token);
        w.PutV3cm(pos);
        w.PutDir(dir);
        w.Put(angle);
        w.Put(createRb);
        ItemTool.WriteItemSnapshot(w, item);
        CoopTool.SendReliable(w);
    }

    public void SendItemPickupRequest(uint dropId)
    {
        if (IsServer || !networkStarted) return;
        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.ITEM_PICKUP_REQUEST);
        w.Put(dropId);
        CoopTool.SendReliable(w);
    }
}