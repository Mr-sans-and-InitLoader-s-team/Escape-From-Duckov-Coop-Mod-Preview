namespace EscapeFromDuckovCoopMod;

public static class RPCAudio
{
    public static void HandleAudioEvent(RpcContext context, AudioEventRpc message)
    {
        CoopAudioSync.HandleIncoming(message.Payload);

        if (context.IsServer)
        {
            if (CoopCombatAudio.IsTransientGunshot(message.Payload))
                CoopTool.SendUnreliableRpc(in message, context.Sender);
            else
                CoopTool.SendRpc(in message, context.Sender);
        }
    }
}
