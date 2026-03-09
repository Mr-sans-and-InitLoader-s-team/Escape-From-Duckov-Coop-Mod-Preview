namespace EscapeFromDuckovCoopMod;

public static class RPCEnvironment
{
    public static void HandleSnapshotRequest(RpcContext context, EnvSnapshotRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.Weather?.Server_HandleSnapshotRequest(context);
    }

    public static void HandleExitSnapshotRequest(RpcContext context, EnvExitSnapshotRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.ExitSync?.Server_HandleSnapshotRequest(context, in message);
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

    public static void HandleLevelDataBool(RpcContext context, EnvLevelDataBoolRpc message)
    {
        LevelDataBoolNet.HandleRpc(context, in message);
    }

    public static void HandleExitSnapshot(RpcContext context, EnvExitSnapshotRpc message)
    {
        if (context.IsServer) return;
        COOPManager.ExitSync?.Client_ApplySnapshot(in message);
    }

    public static void HandleDestructibleHealthReport(RpcContext context, EnvDestructibleHealthReportRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.destructible?.Server_HandleHealthReport(context, in message);
    }
}
