using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class TeleporterSpawnEntry
{
    private static string _token, _map, _scene, _path;
    private static int _beacon, _generation;
    internal static int Beacon => _beacon;
    internal static bool Matches(string map) => _token != null && NetService.Instance?.networkStarted == true &&
        string.Equals(_map, map, StringComparison.OrdinalIgnoreCase);

    internal static void Begin(SceneBeginLoadRpc message)
    {
        Reset();
        if (!message.SyncSpawnEntry || string.IsNullOrEmpty(message.TeleporterToken)) return;
        _token = message.TeleporterToken;
        _map = message.SceneId;
        _beacon = message.BeaconIndex;
    }

    internal static void Reset()
    {
        _generation++;
        _token = _map = _scene = _path = null;
    }

    internal static bool TryResolve(out (string sceneID, SubSceneEntry.Location locationData) result)
    {
        if (_path == string.Empty) { result = default; return true; }
        var entry = MultiSceneCore.Instance?.SubScenes.FirstOrDefault(s => s.sceneID == _scene);
        var location = entry?.cachedLocations.FirstOrDefault(l => l.path == _path);
        result = (_scene, location);
        return location != null;
    }

    internal static void Publish((string sceneID, SubSceneEntry.Location locationData) result)
    {
        if (!NetService.Instance.IsServer || !Matches(MultiSceneCore.MainSceneID) || _path != null) return;
        _scene = result.sceneID ?? string.Empty;
        _path = result.locationData?.path ?? string.Empty;
        CoopTool.SendRpc(new SceneSpawnEntryRpc { Token = _token, Map = _map, Scene = _scene, Path = _path, Beacon = _beacon });
        CoopSessionLog.Write("Travel", $"Selected native entry: beacon={_beacon}, scene={_scene}, path={_path}");
    }

    internal static void Handle(RpcContext context, SceneSpawnEntryRpc message)
    {
        if (context.Service == null || context.IsServer || context.Sender != context.Service.connectedPeer || !Matches(message.Map) ||
            message.Token != _token || message.Beacon != _beacon || _path != null) return;
        _scene = message.Scene;
        _path = message.Path;
        CoopSessionLog.Write("Travel", $"Received native entry: beacon={_beacon}, scene={_scene}, path={_path}");
    }

    internal static async UniTask<bool> Wait(string map)
    {
        if (!Matches(map)) return true;
        var generation = _generation;
        var deadline = Time.realtimeSinceStartup + 60f;
        while (generation == _generation && Matches(map) && _path == null && Time.realtimeSinceStartup < deadline)
            await UniTask.Delay(100, ignoreTimeScale: true);
        if (generation != _generation || !Matches(map)) return false;
        if (TryResolve(out _)) return true;
        CoopSessionLog.Write("Travel", $"Native entry unavailable: beacon={_beacon}, scene={_scene}, path={_path}");
        var reason = _path == null ? "ui.teleporter.reason.timeout" : "ui.teleporter.reason.destinationMissing";
        var returnToMenu = SharedHome.Guest;
        NetService.Instance.StopNetwork();
        if (!returnToMenu) SceneLoader.LoadMainMenu();
        MModUI.ShowTip(CoopLocalization.Get(reason));
        return false;
    }
}
