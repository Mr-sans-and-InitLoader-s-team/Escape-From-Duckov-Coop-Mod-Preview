namespace EscapeFromDuckovCoopMod;

internal static class FootstepSync
{
    private static bool _replaying;

    internal static bool IsReplica(CharacterMainControl character) => NetService.Instance?.networkStarted == true &&
        character && character.GetComponentInParent<RemoteReplicaTag>();

    internal static void Capture(CharacterSoundMaker.FootStepTypes type, CharacterMainControl character)
    {
        var service = NetService.Instance;
        if (_replaying || service?.networkStarted != true || !character || IsReplica(character)) return;
        var message = new FootstepRpc { Type = type, Material = character.FootStepMaterialType, Position = character.transform.position };
        if (character == CharacterMainControl.Main) message.PlayerId = service.GetSelfNetworkId();
        else
        {
            if (!service.IsServer || !character.aiCharacterController ||
                !CoopSyncDatabase.AI.TryGet(character.aiCharacterController, out var entry)) return;
            message.AiId = entry.Id;
        }
        LocalPlayerManager.Instance.ComputeIsInGame(out message.SceneId);
        var heavy = character.CharacterItem && character.CharacterItem.TotalWeight / character.MaxWeight >= .75f;
        message.HearingRadius = (character.Running ? character.RunSoundRange : character.WalkSoundRange) * (heavy ? 1.5f : 1f);
        CoopTool.SendUnreliableRpc(in message);
    }

    internal static void Handle(RpcContext context, FootstepRpc message)
    {
        var service = context.Service;
        if (service?.networkStarted != true || context.Sender == null || (byte)message.Type > (byte)CharacterSoundMaker.FootStepTypes.swim) return;
        CharacterMainControl character;
        if (context.IsServer)
        {
            if (message.AiId != 0 || !service.remoteCharacters.TryGetValue(context.Sender, out var remote) || !remote) return;
            character = remote.GetComponent<CharacterMainControl>();
            message.PlayerId = service.GetPlayerId(context.Sender);
        }
        else
        {
            if (context.Sender != service.connectedPeer || message.AiId == 0 && service.IsSelfId(message.PlayerId)) return;
            character = message.AiId != 0 ? COOPManager.AI?.TryGetCharacter(message.AiId) :
                service.clientRemoteCharacters.TryGetValue(message.PlayerId ?? string.Empty, out var remote) && remote
                    ? remote.GetComponent<CharacterMainControl>() : null;
        }
        if (context.IsServer) CoopTool.SendUnreliableRpc(in message, context.Sender);
        if (!character || !LocalPlayerManager.Instance.IsAlive(character)) return;
        LocalPlayerManager.Instance.ComputeIsInGame(out var scene);
        if (!Spectator.AreSameMap(scene, message.SceneId)) return;
        _replaying = true;
        var material = character.FootStepMaterialType;
        try
        {
            character.FootStepMaterialType = message.Material;
            CharacterSoundMaker.OnFootStepSound?.Invoke(character.transform.position, message.Type, character);
            if (context.IsServer && message.HearingRadius > 0 && !float.IsInfinity(message.HearingRadius))
                AIMainBrain.MakeSound(new AISound
                {
                    pos = message.Position, radius = message.HearingRadius, fromTeam = character.Team,
                    soundType = SoundTypes.unknowNoise, fromObject = character.gameObject, fromCharacter = character
                });
        }
        finally { character.FootStepMaterialType = material; _replaying = false; }
    }
}
