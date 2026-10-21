using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCharacterCreation
{
    private static Interact_CustomFace _editor;
    private static int _generation;
    internal static bool Creating { get; private set; }
    internal static bool Joining { get; private set; }
    internal static void FinishJoining() => Joining = false;

    internal static async UniTask<ItemTreeData> NewEquipment()
    {
        var item = await ItemAssetsCollection.InstantiateAsync(CharacterMainControl.Main.CharacterItem.TypeID);
        try { return ItemTreeData.FromItem(item); }
        finally { if (item) item.DestroyTree(); }
    }

    internal static void ApplyFace(CustomFaceSettingData face)
    {
        SharedHome.Apply(() => LevelManager.Instance.CustomFaceManager.SaveSettingToMainCharacter(face));
        LevelManager.Instance.RefreshMainCharacterFace();
    }

    internal static async UniTask Create(Func<bool> current)
    {
        var generation = _generation;
        Creating = true;
        var closed = new UniTaskCompletionSource();
        Interact_CustomFace editor = null;
        try
        {
            var proxy = FindEntry();
            if (!proxy)
            {
                await SceneLoader.Instance.LoadBaseScene();
                if (generation != _generation || !current()) return;
                proxy = FindEntry();
            }
            if (!proxy) throw new InvalidOperationException("Native character customization entry is unavailable");
            var scene = (string)AccessTools.Field(typeof(SceneLoaderProxy), "sceneID").GetValue(proxy);
            var location = (MultiSceneLocation)AccessTools.Field(typeof(SceneLoaderProxy), "location").GetValue(proxy);
            var useLocation = (bool)AccessTools.Field(typeof(SceneLoaderProxy), "useLocation").GetValue(proxy);
            SceneNet.Instance.AuthorizeLocalSceneLoad(scene, throughSettlement: true);
            await SceneLoader.Instance.LoadScene(scene, clickToConinue: true, notifyEvacuation: false,
                useLocation: useLocation, location: location, saveToFile: false);
            if (generation != _generation || !current()) return;
            editor = UnityEngine.Object.FindObjectsByType<Interact_CustomFace>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (!editor) throw new InvalidOperationException("Native character customization is unavailable in this scene");
            _editor = editor;
            editor.OnCustomFaceUiClosedEvent.AddListener(Closed);
            if (!editor.Interacting && !editor.StartInteract(CharacterMainControl.Main))
                throw new InvalidOperationException("Unable to open native character customization");
            await UniTask.WaitUntil(() => closed.Task.Status.IsCompleted() || generation != _generation || !current());
        }
        finally
        {
            if (editor) editor.OnCustomFaceUiClosedEvent.RemoveListener(Closed);
            if (_editor == editor) _editor = null;
            if (generation == _generation)
            {
                Creating = false;
                Joining = closed.Task.Status.IsCompleted();
            }
        }
        void Closed() => closed.TrySetResult();
    }

    private static SceneLoaderProxy FindEntry() => UnityEngine.Object.FindObjectsByType<SceneLoaderProxy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .FirstOrDefault(p => p.name == "CustomFace");

    internal static async UniTaskVoid SendDestination(NetPeer peer)
    {
        var generation = _generation;
        await UniTask.WaitUntil(() => generation != _generation || !SceneLoader.IsSceneLoading && LevelManager.LevelInited);
        if (generation != _generation || !SharedHome.Active || peer.ConnectionState != ConnectionState.Connected) return;
        if (MultiSceneCore.MainSceneID != SceneInfoCollection.BaseSceneID && MultiSceneCore.GetVisited(SceneInfoCollection.BaseSceneID))
        {
            LateJoinScene.SendDestination(peer).Forget();
            return;
        }
        SharedHome.SendPrivate(11, HostDestination(), peer);
    }

    private static byte[] HostDestination()
    {
        var data = new ES3File(false);
        data.Save("tutorialComplete", MultiSceneCore.GetVisited(SceneInfoCollection.BaseSceneID));
        data.Save("scene", MultiSceneCore.MainSceneID);
        data.Save("position", CharacterMainControl.Main.transform.position);
        return data.LoadRawBytes();
    }

    internal static async UniTaskVoid JoinHost(byte[] bytes)
    {
        var generation = _generation;
        Joining = true;
        try
        {
            var data = new ES3File(bytes);
            var scene = data.Load<string>("scene");
            if (string.IsNullOrEmpty(scene)) throw new InvalidOperationException("Shared host scene is unavailable");
            if (MultiSceneCore.MainSceneID != scene)
            {
                SceneNet.Instance.AuthorizeLocalSceneLoad(scene, throughSettlement: true);
                await SceneLoader.Instance.LoadScene(scene, clickToConinue: true, notifyEvacuation: false, saveToFile: false);
            }
            if (generation != _generation || !SharedHome.Guest || !CharacterMainControl.Main) return;
            if (!data.Load<bool>("tutorialComplete"))
                CharacterMainControl.Main.transform.position = data.Load<Vector3>("position");
        }
        catch (Exception e)
        {
            if (generation != _generation) return;
            Debug.LogError($"[SharedHome] Join host after character creation failed: {e}");
            NetService.Instance.StopNetwork();
            MModUI.ShowTip(CoopLocalization.Get("net.sharedHome.entryFailed"));
        }
        finally { if (generation == _generation) Joining = false; }
    }

    internal static void Reset()
    {
        _generation++;
        Creating = false;
        Joining = false;
        if (_editor && _editor.Interacting) _editor.StopInteract();
    }
}
