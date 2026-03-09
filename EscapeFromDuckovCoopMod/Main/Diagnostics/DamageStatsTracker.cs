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
using LiteNetLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public class DamageStatsTracker : MonoBehaviour
{
    private const float BROADCAST_INTERVAL = 0.4f;

    public static DamageStatsTracker Instance;

    private readonly Dictionary<string, float> _damageByPlayer = new(System.StringComparer.OrdinalIgnoreCase);
    private bool _pendingBroadcast;
    private float _nextBroadcastTime;

    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private bool NetworkStarted => Service != null && Service.networkStarted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (!IsServer || !NetworkStarted || !_pendingBroadcast)
            return;

        if (Time.time < _nextBroadcastTime)
            return;

        BroadcastStats();
    }

    public IReadOnlyDictionary<string, float> GetStats() => _damageByPlayer;

    public List<KeyValuePair<string, float>> GetSortedStats()
    {
        var list = new List<KeyValuePair<string, float>>(_damageByPlayer);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        return list;
    }

    public void RecordLocalDamage(float amount)
    {
        if (amount >= 99999f)
        {
            return;
        }

        if(COOPManager.AI.IsHostHurt)
        {
            COOPManager.AI.IsHostHurt = false;
            return;
        }

        if (!NetworkStarted || amount <= 0f)
            return;

        if (IsServer)
        {
            var service = Service;
            var playerId = service != null ? service.GetPlayerId(null) : string.Empty;
            AddDamage(playerId, amount);
            return;
        }

        CoopTool.SendRpc(new DamageStatsReportRpc
        {
            Damage = amount
        });
    }

    public void Server_RecordReportedDamage(NetPeer peer, float amount)
    {
        if (!IsServer || peer == null || amount <= 0f)
            return;

        var service = Service;
        var playerId = service != null ? service.GetPlayerId(peer) : string.Empty;
        AddDamage(playerId, amount);
    }

    public void Client_ApplyStats(DamageStatsSyncRpc message)
    {
        _damageByPlayer.Clear();
        if (message.Entries == null)
            return;

        foreach (var entry in message.Entries)
        {
            if (string.IsNullOrEmpty(entry.PlayerId) || entry.TotalDamage <= 0f)
                continue;
            _damageByPlayer[entry.PlayerId] = entry.TotalDamage;
        }
    }

    private void BroadcastStats()
    {
        var rpc = new DamageStatsSyncRpc
        {
            Entries = new List<DamageStatsEntry>()
        };

        foreach (var kv in _damageByPlayer)
        {
            rpc.Entries.Add(new DamageStatsEntry
            {
                PlayerId = kv.Key,
                TotalDamage = kv.Value
            });
        }

        CoopTool.SendRpc(in rpc);
        _pendingBroadcast = false;
    }

    private void AddDamage(string playerId, float amount)
    {
        if (!IsServer || string.IsNullOrEmpty(playerId) || amount <= 0f)
            return;

        if (_damageByPlayer.ContainsKey(playerId))
            _damageByPlayer[playerId] += amount;
        else
            _damageByPlayer[playerId] = amount;

        _pendingBroadcast = true;
        _nextBroadcastTime = Time.time + BROADCAST_INTERVAL;
    }
}
