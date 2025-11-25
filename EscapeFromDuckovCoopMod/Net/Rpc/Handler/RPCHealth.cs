namespace EscapeFromDuckovCoopMod;

public static class RPCHealth
{
    public static void HandlePlayerHealthReport(RpcContext context, PlayerHealthReportRpc message)
    {
        if (!context.IsServer) return;
        HealthM.Instance?.Server_HandlePlayerHealthReport(context.Sender, message);
    }

    public static void HandlePlayerHealthBroadcast(RpcContext context, PlayerHealthBroadcastRpc message)
    {
        if (context.IsServer) return;
        HealthM.Instance?.Client_HandlePlayerHealthBroadcast(message);
    }

    public static void HandlePlayerDamageForward(RpcContext context, PlayerDamageForwardRpc message)
    {
        if (context.IsServer) return;
        HealthM.Instance?.Client_HandlePlayerDamageForward(message);
    }

    public static void HandlePlayerBuffReport(RpcContext context, PlayerBuffReportRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.Buff?.Server_HandleBuffReport(context.Sender, message);
    }

    public static void HandlePlayerBuffBroadcast(RpcContext context, PlayerBuffBroadcastRpc message)
    {
        if (context.IsServer) return;
        COOPManager.Buff?.Client_HandleBuffBroadcast(message);
    }
}
