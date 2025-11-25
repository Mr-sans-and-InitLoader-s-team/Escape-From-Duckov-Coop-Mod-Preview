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

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteNetLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public class Buff_
{
    private NetService Service => NetService.Instance;


    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;

    public void Server_HandleBuffReport(NetPeer sender, PlayerBuffReportRpc message)
    {
        if (sender == null || message.BuffId == 0) return;

        var service = Service;
        var playerId = service?.GetPlayerId(sender);
        if (string.IsNullOrEmpty(playerId)) return;

        ApplyBuffOnServerProxy(playerId, message.WeaponTypeId, message.BuffId);
        BroadcastBuff(playerId, message.WeaponTypeId, message.BuffId, sender);
    }

    public void Server_BroadcastHostBuff(int weaponTypeId, int buffId)
    {
        var playerId = Service?.GetPlayerId(null);
        if (string.IsNullOrEmpty(playerId)) return;

        BroadcastBuff(playerId, weaponTypeId, buffId, null);
    }

    private void BroadcastBuff(string playerId, int weaponTypeId, int buffId, NetPeer exclude)
    {
        var rpc = new PlayerBuffBroadcastRpc
        {
            PlayerId = playerId,
            WeaponTypeId = weaponTypeId,
            BuffId = buffId
        };

        CoopTool.SendRpc(in rpc, exclude);
    }

    private void ApplyBuffOnServerProxy(string playerId, int weaponTypeId, int buffId)
    {
        var service = Service;
        var remotes = service?.remoteCharacters;
        if (remotes == null) return;

        foreach (var kv in remotes)
        {
            var peer = kv.Key;
            if (peer == null) continue;
            if (service.GetPlayerId(peer) != playerId) continue;

            var cmc = kv.Value ? kv.Value.GetComponent<CharacterMainControl>() : null;
            if (!cmc) return;

            COOPManager.ResolveBuffAsync(weaponTypeId, buffId)
                .ContinueWith(buff =>
                {
                    if (buff != null && cmc) cmc.AddBuff(buff, null, weaponTypeId);
                })
                .Forget();
            return;
        }
    }

    public void Client_HandleBuffBroadcast(PlayerBuffBroadcastRpc message)
    {
        if (string.IsNullOrEmpty(message.PlayerId)) return;
        ApplyBuffProxy_Client(message.PlayerId, message.WeaponTypeId, message.BuffId).Forget();
    }

    public async UniTask ApplyBuffProxy_Client(string playerId, int weaponTypeId, int buffId)
    {
        if (NetService.Instance.IsSelfId(playerId)) return; // 不应该给本地自己用这个分支
        if (!clientRemoteCharacters.TryGetValue(playerId, out var go) || go == null)
        {
            // 远端主机克隆还没生成？先记下来，等 CreateRemoteCharacterForClient 时补发
            if (!CoopTool._cliPendingProxyBuffs.TryGetValue(playerId, out var list))
                list = CoopTool._cliPendingProxyBuffs[playerId] = new List<(int, int)>();
            list.Add((weaponTypeId, buffId));
            return;
        }

        var cmc = go.GetComponent<CharacterMainControl>();
        if (!cmc) return;

        var buff = await COOPManager.ResolveBuffAsync(weaponTypeId, buffId);
        if (buff != null) cmc.AddBuff(buff, null, weaponTypeId);
    }
}