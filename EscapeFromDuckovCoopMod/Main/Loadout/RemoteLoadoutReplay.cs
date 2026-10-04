namespace EscapeFromDuckovCoopMod;

internal static class RemoteLoadoutReplay
{
    public static bool IsCurrent(CoopPeer peer, GameObject remote, CharacterModel model)
    {
        var service = NetService.Instance;
        return service != null && service.networkStarted && service.IsServer && remote != null && model != null &&
            service.remoteCharacters.TryGetValue(peer, out var current) && current == remote &&
            remote.GetComponent<CharacterMainControl>()?.characterModel == model;
    }

    public static bool IsCurrent(string playerId, GameObject remote, CharacterModel model)
    {
        var service = NetService.Instance;
        return service != null && service.networkStarted && !service.IsServer && remote != null && model != null &&
            service.clientRemoteCharacters.TryGetValue(playerId, out var current) && current == remote &&
            remote.GetComponent<CharacterMainControl>()?.characterModel == model;
    }

    public static void ForHost(CoopPeer peer, GameObject created)
    {
        UniTask.Void(async () =>
        {
            await UniTask.NextFrame();
            var service = NetService.Instance;
            if (service == null || !service.networkStarted || !service.IsServer || created == null ||
                !service.remoteCharacters.TryGetValue(peer, out var current) || current != created ||
                !service.playerStatuses.TryGetValue(peer, out var status)) return;
            // Read the latest cache after creation, including updates received while
            // the asynchronous character factory was still running.
            foreach (var equipment in status.EquipmentList)
                COOPManager.HostPlayer_Apply.ApplyEquipmentUpdate(peer, equipment.SlotHash, equipment.ItemId).Forget();
            foreach (var weapon in status.WeaponList)
                COOPManager.HostPlayer_Apply.ApplyWeaponUpdate(peer, weapon.SlotHash, weapon.ItemId, weapon.Snapshot).Forget();
        });
    }

    public static void ForClient(string playerId, GameObject created)
    {
        UniTask.Void(async () =>
        {
            await UniTask.NextFrame();
            var service = NetService.Instance;
            if (service == null || !service.networkStarted || service.IsServer || created == null ||
                !service.clientRemoteCharacters.TryGetValue(playerId, out var current) || current != created ||
                !service.clientPlayerStatuses.TryGetValue(playerId, out var status)) return;
            foreach (var equipment in status.EquipmentList)
                COOPManager.ClientPlayer_Apply.ApplyEquipmentUpdate_Client(playerId, equipment.SlotHash, equipment.ItemId).Forget();
            foreach (var weapon in status.WeaponList)
                COOPManager.ClientPlayer_Apply.ApplyWeaponUpdate_Client(playerId, weapon.SlotHash, weapon.ItemId, weapon.Snapshot).Forget();
        });
    }
}
