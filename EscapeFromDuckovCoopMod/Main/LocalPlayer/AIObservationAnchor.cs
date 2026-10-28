namespace EscapeFromDuckovCoopMod;

internal static class AIObservationAnchor
{
    private static CharacterMainControl SpectatedCharacter
    {
        get
        {
            if (Spectator.Instance?._spectatorActive != true) return null;
            var target = GameCamera.Instance ? GameCamera.Instance.target : null;
            return target && LocalPlayerManager.Instance.IsAlive(target) ? target : null;
        }
    }

    internal static string SpectatedPlayerId()
    {
        var target = SpectatedCharacter;
        return target && NetService.Instance.TryGetPlayerId(target, out var id) ? id : string.Empty;
    }

    internal static Vector3 LocalPosition(Vector3 fallback)
    {
        var target = SpectatedCharacter;
        if (target) return target.transform.position;
        var controlling = LevelManager.Instance ? LevelManager.Instance.ControllingCharacter : null;
        return controlling && controlling.isVehicle ? controlling.transform.position : fallback;
    }

    internal static Vector3 PeerPosition(NetPeer peer, PlayerStatus status)
    {
        var service = NetService.Instance;
        var targetId = status.SpectatingPlayerId;
        if (string.IsNullOrEmpty(targetId)) return status.Position;
        if (service.remoteCharacters.TryGetValue(peer, out var observer) && observer &&
            LocalPlayerManager.Instance.IsAlive(observer.GetComponent<CharacterMainControl>())) return status.Position;

        if (service.IsSelfId(targetId))
        {
            var main = CharacterMainControl.Main;
            if (main && LocalPlayerManager.Instance.IsAlive(main) &&
                Spectator.AreSameMap(status.SceneId, service.localPlayerStatus?.SceneId))
            {
                var controlling = LevelManager.Instance ? LevelManager.Instance.ControllingCharacter : null;
                return controlling && controlling.isVehicle ? controlling.transform.position : main.transform.position;
            }
        }
        else if (service.TryGetPeerByPlayerId(targetId, out var targetPeer) &&
                 service.playerStatuses.TryGetValue(targetPeer, out var targetStatus) && targetStatus.IsInGame &&
                 Spectator.AreSameMap(status.SceneId, targetStatus.SceneId) &&
                 service.remoteCharacters.TryGetValue(targetPeer, out var targetObject) && targetObject &&
                 LocalPlayerManager.Instance.IsAlive(targetObject.GetComponent<CharacterMainControl>()))
            return targetStatus.Position;

        return status.Position;
    }
}
