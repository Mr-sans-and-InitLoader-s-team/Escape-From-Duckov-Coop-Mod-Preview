namespace EscapeFromDuckovCoopMod;

public static class RPCDiagnostics
{
    public static void HandleDamageStats(RpcContext context, DamageStatsSyncRpc message)
    {
        DamageStatsTracker.Instance?.Client_ApplyStats(message);
    }

    public static void HandleDamageReport(RpcContext context, DamageStatsReportRpc message)
    {
        DamageStatsTracker.Instance?.Server_RecordReportedDamage(context.Sender, message.Damage);
    }
}
