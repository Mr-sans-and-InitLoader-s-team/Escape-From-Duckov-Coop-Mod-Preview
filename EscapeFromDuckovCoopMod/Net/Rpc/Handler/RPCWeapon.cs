namespace EscapeFromDuckovCoopMod;

public static class RPCWeapon
{
    public static void HandleFireRequest(RpcContext context, WeaponFireRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.WeaponHandle?.Server_HandleFireRequest(context.Sender, in message);
    }

    public static void HandleFireEvent(RpcContext context, WeaponFireEventRpc message)
    {
        if (context.IsServer) return;
        COOPManager.WeaponHandle?.Client_HandleFireEvent(in message);
    }

    public static void HandleMeleeSwingRequest(RpcContext context, MeleeSwingRequestRpc message)
    {
        if (!context.IsServer) return;
        COOPManager.WeaponHandle?.Server_HandleMeleeSwingRequest(context.Sender, in message);
    }

    public static void HandleMeleeSwingBroadcast(RpcContext context, MeleeSwingBroadcastRpc message)
    {
        if (context.IsServer) return;
        COOPManager.WeaponHandle?.Client_HandleMeleeSwing(in message);
    }
}
