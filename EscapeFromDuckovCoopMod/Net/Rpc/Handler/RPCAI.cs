namespace EscapeFromDuckovCoopMod;

public static class RPCAI
{
    public static void HandleSnapshotRequest(RpcContext context, AISnapshotRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.AI?.Server_HandleSnapshotRequest(context, message);
    }

    public static void HandleSnapshotChunk(RpcContext context, AISnapshotChunkRpc message)
    {
        if (context.IsServer) return;
        COOPManager.AI?.Client_HandleSnapshotChunk(message);
    }

    public static void HandleActivationRequest(RpcContext context, AIActivationRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.AI?.Server_HandleActivationRequest(context, message);
    }

    public static void HandleActivationState(RpcContext context, AIActivationStateRpc message)
    {
        if (context.IsServer) return;
        COOPManager.AI?.Client_HandleActivationState(message);
    }

    public static void HandleSpawn(RpcContext context, AISpawnRpc message)
    {
        if (context.IsServer) return;
        COOPManager.AI?.Client_HandleSpawn(message);
    }

    public static void HandleDespawn(RpcContext context, AIDespawnRpc message)
    {
        if (context.IsServer) return;
        COOPManager.AI?.Client_HandleDespawn(message);
    }

    public static void HandleStateUpdate(RpcContext context, AIStateUpdateRpc message)
    {
        if (context.IsServer) return;
        COOPManager.AI?.Client_HandleState(message);
    }

    public static void HandleHealthReport(RpcContext context, AIHealthReportRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.AI?.Server_HandleHealthReport(context, message);
    }

    public static void HandleHealthBroadcast(RpcContext context, AIHealthBroadcastRpc message)
    {
        if (context.IsServer) return;
        COOPManager.AI?.Client_HandleHealthBroadcast(message);
    }

    public static void HandleBuffReport(RpcContext context, AIBuffReportRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.AI?.Server_HandleBuffReport(context, message);
    }

    public static void HandleBuffBroadcast(RpcContext context, AIBuffBroadcastRpc message)
    {
        if (context.IsServer) return;
        COOPManager.AI?.Client_HandleBuffBroadcast(message);
    }
}
