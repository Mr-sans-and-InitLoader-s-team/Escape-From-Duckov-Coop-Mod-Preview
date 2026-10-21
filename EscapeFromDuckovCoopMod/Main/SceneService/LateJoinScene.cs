using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

internal static class LateJoinScene
{
    private static int _generation;
    private static bool _joining;
    private static SceneJoinRpc? _pending;

    internal static void Reset() { _generation++; _joining = false; _pending = null; }

    internal static async UniTaskVoid SendDestination(NetPeer peer, byte kind = 0)
    {
        var generation = _generation;
        await UniTask.WaitUntil(() => generation != _generation || peer.ConnectionState != ConnectionState.Connected ||
            !SceneLoader.IsSceneLoading && LevelManager.LevelInited && CharacterMainControl.Main);
        if (generation != _generation || peer.ConnectionState != ConnectionState.Connected ||
            NetService.Instance?.IsServer != true || !SharedHomeCompatibility.Allows(peer)) return;
        var main = MultiSceneCore.MainSceneID;
        if (kind == 0 && main == SceneInfoCollection.BaseSceneID) return;
        var character = CharacterMainControl.Main;
        var message = new SceneJoinRpc { Kind = kind, MainSceneId = main, SceneId = MultiSceneCore.ActiveSubSceneID,
            Position = character.transform.position, Rotation = character.modelRoot.rotation };
        CoopTool.SendRpcTo(peer, in message);
    }

    internal static void Handle(RpcContext context, SceneJoinRpc message)
    {
        if (context.IsServer)
        {
            if (message.Kind == 1 && context.Sender != null && SharedHomeCompatibility.Allows(context.Sender))
                SendDestination(context.Sender, 2).Forget();
            return;
        }
        if (message.Kind != 0 && message.Kind != 2 || string.IsNullOrEmpty(message.MainSceneId)) return;
        _pending = message;
        if (!_joining) Join(_generation).Forget();
    }

    private static async UniTaskVoid Join(int generation)
    {
        _joining = true;
        var service = NetService.Instance;
        bool Current() => generation == _generation && service && service.networkStarted &&
            !service.IsServer && service.connectedPeer?.ConnectionState == ConnectionState.Connected;
        try
        {
            while (Current() && _pending.HasValue)
            {
                var destination = _pending.Value;
                _pending = null;
                await SharedHomeEntry.WaitForTitle();
                await UniTask.WaitUntil(() => !Current() || !SceneLoader.IsSceneLoading &&
                    (!SharedHome.Guest || SharedHomeLoadouts.Ready && !SharedHomeCharacterCreation.Creating));
                if (!Current()) return;
                // A same-map join can still contain enemies and loot generated while offline.
                var moved = !MultiSceneCore.MainScene.HasValue || MultiSceneCore.MainSceneID != destination.MainSceneId ||
                    destination.Kind == 0 && destination.MainSceneId != SceneInfoCollection.BaseSceneID;
                if (moved)
                {
                    SceneNet.Instance.AuthorizeLocalSceneLoad(destination.MainSceneId, throughSettlement: true);
                    await SceneLoader.Instance.LoadScene(destination.MainSceneId, clickToConinue: false,
                        notifyEvacuation: false, saveToFile: false);
                }
                if (!Current()) return;
                if (!string.IsNullOrEmpty(destination.SceneId) && MultiSceneCore.ActiveSubSceneID != destination.SceneId &&
                    destination.SceneId != destination.MainSceneId)
                {
                    SceneNet.Instance.AuthorizeLocalSceneLoad(destination.SceneId);
                    if (!await MultiSceneCore.Instance.LoadAndTeleport(destination.SceneId, destination.Position))
                        throw new InvalidOperationException("Unable to enter host sub-scene " + destination.SceneId);
                    moved = true;
                }
                if (!Current()) return;
                if (moved || destination.Kind == 0)
                {
                    var request = new SceneJoinRpc { Kind = 1 };
                    CoopTool.SendRpc(in request);
                    continue;
                }
                CharacterMainControl.Main.SetPosition(destination.Position);
                CharacterMainControl.Main.modelRoot.rotation = destination.Rotation;
                SceneNet.Instance._sceneReadySidSent = null;
                Send_ClientStatus.Instance.SendClientStatusUpdate();
                SceneNet.Instance.TrySendSceneReadyOnce();
                LocalPlayerManager.Instance.RequestFullLoadoutSync();
                COOPManager.Weather.Client_RequestSnapshot();
                COOPManager.ItemNet.Client_RequestDropSnapshot();
                COOPManager.ExitSync.Client_RequestSnapshot(true);
                COOPManager.AI.Client_RefreshSnapshot();
                SharedHomeCharacterCreation.FinishJoining();
                Debug.Log($"[LateJoin] Joined host at {destination.MainSceneId}/{destination.SceneId} {destination.Position}");
            }
        }
        catch (Exception e)
        {
            if (!Current()) return;
            Debug.LogError($"[LateJoin] Join host scene failed: {e}");
            service.StopNetwork();
            MModUI.Instance?.ShowConnectionError(CoopLocalization.Get("net.connectionFailed"), e.Message);
        }
        finally { if (generation == _generation) _joining = false; }
    }
}
