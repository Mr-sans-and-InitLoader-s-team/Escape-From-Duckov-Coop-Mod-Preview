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
using EscapeFromDuckovCoopMod.Net;

namespace EscapeFromDuckovCoopMod;

public static class AudioEventMessage
{
    public static void ClientSend(in CoopAudioEventPayload payload)
    {
        var service = NetService.Instance;
        if (service == null || service.IsServer || !service.networkStarted)
            return;

        var peer = service.connectedPeer;
        if (peer == null)
            return;

        var writer = new NetDataWriter();
        writer.Put((byte)Op.AUDIO_EVENT);
        payload.Write(writer);

        peer.SendSmart(writer, Op.AUDIO_EVENT);
    }

    public static void ServerBroadcast(in CoopAudioEventPayload payload)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer || !service.networkStarted)
            return;

        var manager = service.netManager;
        if (manager == null)
            return;

        var writer = new NetDataWriter();
        writer.Put((byte)Op.AUDIO_EVENT);
        payload.Write(writer);

        manager.SendSmart(writer, Op.AUDIO_EVENT);
    }

    public static void ServerBroadcastExcept(in CoopAudioEventPayload payload, NetPeer excludedPeer)
    {
        var service = NetService.Instance;
        if (service == null || !service.IsServer || !service.networkStarted)
            return;

        var manager = service.netManager;
        if (manager == null)
            return;

        var writer = new NetDataWriter();
        writer.Put((byte)Op.AUDIO_EVENT);
        payload.Write(writer);

        manager.SendSmartExcept(writer, Op.AUDIO_EVENT, excludedPeer);
    }
}
