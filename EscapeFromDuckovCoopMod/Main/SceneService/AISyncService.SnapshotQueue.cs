namespace EscapeFromDuckovCoopMod;

public sealed partial class AISyncService
{
    private readonly Dictionary<NetPeer, Queue<(int[] Ids, bool Reset)>> _serverSnapshotChunks = new();

    private IEnumerable<AISyncEntry> Server_SnapshotEntries() => CoopSyncDatabase.AI.Entries
        .OrderByDescending(entry => entry != null && HasNativeArea(entry.CharacterPresetKey));

    private void SendSnapshotChunk(NetPeer peer, IReadOnlyList<AISnapshotEntry> entries, bool reset)
    {
        if (!IsServer || peer == null || entries == null || entries.Count == 0) return;
        if (!_serverSnapshotChunks.TryGetValue(peer, out var chunks))
            _serverSnapshotChunks[peer] = chunks = new();
        chunks.Enqueue((entries.Select(e => e.Id).ToArray(), reset));
    }

    private void Server_FlushSnapshotChunks()
    {
        foreach (var peer in _serverSnapshotChunks.Keys.ToArray())
        {
            if (peer.ConnectionState != ConnectionState.Connected)
            {
                _serverSnapshotChunks.Remove(peer);
                continue;
            }
            // Leave capacity for interactions while a large AI snapshot is still being delivered.
            var queued = Service.netManager.GetReliableQueueLength(peer);
            if (queued >= 16) continue;
            var chunks = _serverSnapshotChunks[peer];
            if (chunks.Count == 0)
            {
                if (queued <= 0) _serverSnapshotChunks.Remove(peer);
                continue;
            }
            var chunk = chunks.Dequeue();
            var entries = new List<AISnapshotEntry>(chunk.Ids.Length);
            foreach (var id in chunk.Ids)
                if (CoopSyncDatabase.AI.TryGet(id, out var entry)) entries.Add(BuildSnapshotEntry(entry));
            // Health, buffs and pose may have changed while this peer was congested.
            Server_SendSnapshotChunk(peer, entries, chunk.Reset);
        }
    }
}
