using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class RemoteMeleePlayback
{
    public static void Play(NetService service, in MeleeSwingBroadcastRpc message)
    {
        if (service == null || !service.networkStarted || SceneLoader.IsSceneLoading ||
            MultiSceneCore.Instance == null || MultiSceneCore.Instance.IsLoading) return;
        if (service.IsSelfId(message.PlayerId)) return;

        CharacterMainControl character = null;
        if (message.AiId != 0)
        {
            character = COOPManager.AI?.TryGetCharacter(message.AiId);
        }
        else if (!string.IsNullOrEmpty(message.PlayerId))
        {
            GameObject remote = null;
            if (service.IsServer)
            {
                if (service.TryGetPeerByPlayerId(message.PlayerId, out var peer))
                    service.remoteCharacters.TryGetValue(peer, out remote);
            }
            else service.clientRemoteCharacters.TryGetValue(message.PlayerId, out remote);
            if (remote) character = remote.GetComponent<CharacterMainControl>();
        }

        // A late swing belongs to this actor only. Never redirect it to a nearby
        // player or AI while its original actor is being unloaded/recreated.
        if (!character || !character.gameObject.activeInHierarchy || !character.characterModel) return;
        var model = character.characterModel;
        var magic = model.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (magic) magic.OnAttack();
        var basic = model.GetComponent<CharacterAnimationControl>();
        if (basic) basic.OnAttack();
        MeleeFx.SpawnSlashFx(model);
    }
}
