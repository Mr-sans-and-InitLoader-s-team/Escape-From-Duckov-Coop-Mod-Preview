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

public class PublicHandleUpdate
{
    private NetService Service => NetService.Instance;

    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;
    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public void HandleEquipmentUpdate(NetPeer sender, NetPacketReader reader)
    {
        var endPoint = reader.GetString();
        var slotHash = reader.GetInt();
        var itemId = reader.GetString();

        COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(sender, slotHash, itemId).Forget();

        foreach (var p in netManager.ConnectedPeerList)
        {
            if (p == sender) continue;
            var w = new NetDataWriter();
            w.Put((byte)Op.EQUIPMENT_UPDATE);
            w.Put(endPoint);
            w.Put(slotHash);
            w.Put(itemId);
            p.Send(w, DeliveryMethod.ReliableOrdered);
        }
    }


    public void HandleWeaponUpdate(NetPeer sender, NetPacketReader reader)
    {
        var endPoint = reader.GetString();
        var slotHash = reader.GetInt();
        var itemId = reader.GetString();

        COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(sender, slotHash, itemId).Forget();

        foreach (var p in netManager.ConnectedPeerList)
        {
            if (p == sender) continue;
            var w = new NetDataWriter();
            w.Put((byte)Op.PLAYERWEAPON_UPDATE);
            w.Put(endPoint);
            w.Put(slotHash);
            w.Put(itemId);
            p.Send(w, DeliveryMethod.ReliableOrdered);
        }
    }

}
