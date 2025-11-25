namespace EscapeFromDuckovCoopMod;

public static class RPCItem
{
    public static void HandleDropRequest(RpcContext context, ItemDropRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.ItemNet?.Server_HandleDropRequest(context, message);
    }

    public static void HandleSpawn(RpcContext context, ItemSpawnRpc message)
    {
        if (context.IsServer) return;
        COOPManager.ItemNet?.Client_HandleSpawn(message);
    }

    public static void HandlePickupRequest(RpcContext context, ItemPickupRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.ItemNet?.Server_HandlePickupRequest(context, message);
    }

    public static void HandleDespawn(RpcContext context, ItemDespawnRpc message)
    {
        if (context.IsServer) return;
        COOPManager.ItemNet?.Client_HandleDespawn(message);
    }

    public static void HandleDropSnapshotRequest(RpcContext context, ItemDropSnapshotRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.ItemNet?.Server_HandleDropSnapshotRequest(context, message);
    }

    public static void HandleDropSnapshotChunk(RpcContext context, ItemDropSnapshotChunkRpc message)
    {
        if (context.IsServer) return;
        COOPManager.ItemNet?.Client_HandleDropSnapshotChunk(message);
    }
}
