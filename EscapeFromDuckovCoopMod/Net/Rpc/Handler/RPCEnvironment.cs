namespace EscapeFromDuckovCoopMod;

public static class RPCEnvironment
{
    public static void HandleSnapshotRequest(RpcContext context, EnvSnapshotRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.Weather?.Server_HandleSnapshotRequest(context);
    }

    public static void HandleClockState(RpcContext context, EnvClockStateRpc message)
    {
        if (context.IsServer) return;
        COOPManager.Weather?.Client_HandleClockState(message);
    }

    public static void HandleWeatherState(RpcContext context, EnvWeatherStateRpc message)
    {
        if (context.IsServer) return;
        COOPManager.Weather?.Client_HandleWeatherState(message);
    }

    public static void HandleLootChunk(RpcContext context, EnvLootChunkRpc message)
    {
        if (context.IsServer) return;
        COOPManager.Weather?.Client_HandleLootChunk(message);
    }

    public static void HandleDoorChunk(RpcContext context, EnvDoorChunkRpc message)
    {
        if (context.IsServer) return;
        COOPManager.Weather?.Client_HandleDoorChunk(message);
    }

    public static void HandleDestructibleState(RpcContext context, EnvDestructibleStateRpc message)
    {
        if (context.IsServer) return;
        COOPManager.Weather?.Client_HandleDestructibleState(message);
    }

    public static void HandleExplosiveOilBarrelState(RpcContext context, EnvExplosiveOilBarrelStateRpc message)
    {
        if (context.IsServer) return;
        COOPManager.ExplosiveBarrels?.Client_ApplySnapshot(message);
    }
}
