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

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.DAMAGE_STATS_REPORT, DeliveryMethod.Sequenced, RpcDirection.ClientToServer)]
public struct DamageStatsReportRpc : IRpcMessage
{
    public float Damage;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Damage);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Damage = reader.GetFloat();
    }
}
