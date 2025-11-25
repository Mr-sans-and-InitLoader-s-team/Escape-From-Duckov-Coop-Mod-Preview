namespace EscapeFromDuckovCoopMod;

public static class RPCAudio
{
    public static void HandleAudioEvent(RpcContext context, AudioEventRpc message)
    {
        CoopAudioSync.HandleIncoming(message.Payload);

        if (context.IsServer)
        {
            CoopTool.SendRpc(in message, context.Sender);
        }
    }
}
