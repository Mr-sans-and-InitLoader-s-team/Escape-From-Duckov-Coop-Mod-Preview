namespace EscapeFromDuckovCoopMod;

public static class RPCModApi
{
    public static void HandleModApiMessage(RpcContext context, ModApiMessageRpc rpc)
    {
        ModNetworkApi.Receive(context.Service, context.Sender, rpc.Channel, rpc.Payload);
    }

    public static void HandleModApiReplayRequest(RpcContext context, ModApiReplayRequestRpc rpc)
    {
        ModNetworkApi.HandleReplayRequest(context.Sender, rpc.Channel);
    }
}
