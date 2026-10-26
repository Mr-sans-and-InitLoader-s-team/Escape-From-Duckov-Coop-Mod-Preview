using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class SpectatorTravel
{
    private static bool _travelling, _following;
    private static int _generation;
    internal static bool Travelling => _travelling;
    internal static void Reset() { _generation++; _travelling = _following = false; }

    internal static bool TryGetSurvivor(out PlayerStatus survivor, bool sameMap = true)
    {
        survivor = null;
        var service = NetService.Instance;
        if (service?.networkStarted != true) return false;
        LocalPlayerManager.Instance.ComputeIsInGame(out var scene);
        if (service.IsServer)
        {
            foreach (var pair in service.playerStatuses)
            {
                if (pair.Key?.ConnectionState != ConnectionState.Connected || pair.Value == null ||
                    sameMap && (pair.Value.IsInGame != true || !Spectator.AreSameMap(scene, pair.Value.SceneId))) continue;
                service.remoteCharacters.TryGetValue(pair.Key, out var remote);
                if (HealthM.Instance.IsKnownPlayerAlive(service.GetPlayerId(pair.Key), remote)) { survivor = pair.Value; return true; }
            }
        }
        else
            foreach (var pair in service.clientPlayerStatuses)
            {
                if (service.IsSelfId(pair.Key) || pair.Value == null ||
                    sameMap && (pair.Value.IsInGame != true || !Spectator.AreSameMap(scene, pair.Value.SceneId))) continue;
                service.clientRemoteCharacters.TryGetValue(pair.Key, out var remote);
                if (HealthM.Instance.IsKnownPlayerAlive(pair.Key, remote)) { survivor = pair.Value; return true; }
            }
        return false;
    }

    internal static bool WaitForTarget()
    {
        if (_travelling || _following || SceneLoader.IsSceneLoading || MultiSceneCore.Instance?.IsLoading == true) return true;
        var target = GameCamera.Instance ? GameCamera.Instance.target : null;
        if (target && target != CharacterMainControl.Main && target.gameObject.activeInHierarchy &&
            LocalPlayerManager.Instance.IsAlive(target) && Spectator.Instance.IsInSameScene(target))
            return false;
        if (!TryGetSurvivor(out var survivor)) return false;
        var core = MultiSceneCore.Instance;
        if (core && survivor.SceneId != MultiSceneCore.ActiveSubSceneID && core.SubScenes.Any(s => s.sceneID == survivor.SceneId))
        {
            Follow(survivor).Forget();
            return true;
        }
        return false;
    }

    private static async UniTaskVoid Follow(PlayerStatus survivor)
    {
        var generation = _generation;
        _following = true;
        try
        {
            SceneNet.Instance.AuthorizeLocalSceneLoad(survivor.SceneId);
            await MultiSceneCore.Instance.LoadAndTeleport(survivor.SceneId, survivor.Position);
            if (generation != _generation) return;
            Spectator.Instance.BuildSpectateList(CharacterMainControl.Main);
        }
        finally { if (generation == _generation) _following = false; }
    }

    internal static void Begin()
    {
        if (_travelling) return;
        _travelling = true;
        PreserveDeath(_generation).Forget();
    }

    private static async UniTaskVoid PreserveDeath(int generation)
    {
        var service = NetService.Instance;
        try
        {
            await UniTask.WaitUntil(() => generation != _generation || !service.networkStarted || SceneLoader.IsSceneLoading || MultiSceneCore.Instance?.IsLoading == true);
            await UniTask.WaitUntil(() => generation != _generation || !service.networkStarted ||
                !SceneLoader.IsSceneLoading && MultiSceneCore.Instance?.IsLoading != true && LevelManager.AfterInit);
            if (generation != _generation || !service.networkStarted) return;
            RestoreDeath();
            await UniTask.WaitUntil(() => generation != _generation || !service.networkStarted ||
                TryGetSurvivor(out _) || !TryGetSurvivor(out _, false));
            if (generation != _generation || !service.networkStarted) return;
            Spectator.Instance.BuildSpectateList(CharacterMainControl.Main);
        }
        finally { if (generation == _generation) _travelling = false; }
    }

    internal static void RestoreDeath()
    {
        if (!_travelling || Spectator.Instance?._spectatorActive != true) return;
        var main = CharacterMainControl.Main;
        if (!main || main.Health.IsDead) return;
        // Carry the existing death across travel without creating another tomb or death event.
        AccessTools.Field(typeof(Health), "isDead").SetValue(main.Health, true);
        main.Health.SetHealth(0);
        main.gameObject.SetActive(false);
    }
}
