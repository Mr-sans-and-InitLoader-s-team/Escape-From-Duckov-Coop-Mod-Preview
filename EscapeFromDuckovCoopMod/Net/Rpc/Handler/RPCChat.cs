namespace EscapeFromDuckovCoopMod
{
    public static class RPCChat
    {
        public static void HandleChatSend(RpcContext context, ChatSendRequestRpc message)
        {
            var service = context.Service;
            if (service == null || !context.IsServer)
                return;

            var content = message.Content?.Trim();
            if (string.IsNullOrEmpty(content))
                return;

            var senderId = service.GetPlayerId(context.Sender) ?? string.Empty;
            var senderName = service.ResolvePeerDisplayName(context.Sender, message.SenderName);
            if (string.IsNullOrWhiteSpace(senderName))
                senderName = senderId;

            var broadcast = new ChatMessageRpc
            {
                SenderId = senderId,
                SenderName = senderName,
                Content = content
            };

            MModUI.Instance?.OnChatMessageReceived(broadcast.SenderName, broadcast.Content);
            CoopTool.SendRpc(broadcast);
        }

        public static void HandleChatMessage(RpcContext context, ChatMessageRpc message)
        {
            var service = context.Service;
            if (service == null || context.IsServer)
                return;

            if (string.IsNullOrEmpty(message.Content))
                return;

            MModUI.Instance?.OnChatMessageReceived(message.SenderName, message.Content);
        }
    }
}
