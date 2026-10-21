using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal sealed class CoopModePreview : MonoBehaviour
{
    internal const int LayerMask = (1 << 30) | (1 << 31);
    private GameObject _stage;
    private RenderTexture _texture;
    private readonly List<Item> _items = new();
    private readonly List<CoopModeDuckAnimation> _ducks = new();
    internal bool Ready { get; private set; }

    internal void Initialize(RawImage image, bool shared, CoopModePortrait settings) => Build(image, shared, settings).Forget();

    private async UniTaskVoid Build(RawImage image, bool shared, CoopModePortrait settings)
    {
        try
        {
            image.enabled = false;
            var layer = shared ? 30 : 31;
            _stage = new GameObject(shared ? "SharedCoopPortraitStage" : "IndependentCoopPortraitStage");
            _stage.transform.position = new Vector3(shared ? 10040 : 10000, -10000, 10000);
            _stage.SetActive(false);
            _texture = new RenderTexture(588, 600, 24, RenderTextureFormat.ARGB32) { name = "CoopModePortrait" };
            _texture.Create();
            image.texture = _texture;
            var camera = new GameObject("PortraitCamera").AddComponent<Camera>();
            camera.transform.SetParent(_stage.transform, false);
            camera.transform.localPosition = settings.cameraPosition;
            camera.transform.LookAt(_stage.transform.TransformPoint(settings.cameraTarget));
            camera.fieldOfView = settings.fieldOfView;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 20;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = settings.background;
            camera.cullingMask = 1 << layer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.targetTexture = _texture;
            foreach (var source in settings.lights)
            {
                var light = new GameObject("PortraitLight").AddComponent<Light>();
                light.transform.SetParent(_stage.transform, false);
                light.transform.localPosition = source.position;
                light.type = source.directional ? LightType.Directional : LightType.Point;
                light.transform.localRotation = Quaternion.Euler(source.rotation);
                light.color = source.color;
                light.intensity = source.intensity;
                light.range = source.range;
                light.cullingMask = 1 << layer;
                light.shadows = LightShadows.None;
            }
            foreach (var pose in settings.ducks)
            {
                var model = Instantiate(GameplayDataSettings.Prefabs.DefaultCharacterModel, _stage.transform, false);
                model.name = "PortraitDuck";
                DisableGameplay(model.gameObject);
                model.transform.localPosition = pose.position;
                model.transform.localRotation = Quaternion.Euler(pose.rotation);
                var face = GameplayDataSettings.CustomFaceData.DefaultPreset.settings;
                face.headSetting.mainColor = pose.color;
                face.eyeID = pose.eyes;
                face.mouthID = pose.mouth;
                model.CustomFace.LoadFromData(face);
                await Attach(pose.armor, model.ArmorSocket, false);
                if (!this || !_stage) return;
                await Attach(pose.helmet, model.HelmatSocket, false);
                if (!this || !_stage) return;
                if (model.CustomFace.hairSocket) model.CustomFace.hairSocket.gameObject.SetActive(false);
                await Attach(pose.backpack, model.BackpackSocket, false);
                if (!this || !_stage) return;
                var gun = await Attach(pose.gun, model.RightHandSocket, true);
                if (!this || !_stage) return;
                if (gun)
                {
                    gun.transform.localPosition = Vector3.zero;
                    gun.transform.localRotation = Quaternion.identity;
                }
                DisableGameplay(model.gameObject);
                var animation = model.gameObject.AddComponent<CoopModeDuckAnimation>();
                animation.Initialize(model, pose);
                _ducks.Add(animation);
            }
            foreach (var child in _stage.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            _stage.SetActive(true);
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            await UniTask.WaitUntil(() => !this || !_stage || _ducks.All(duck => duck && duck.Settled));
            if (!this || !_stage) return;
            foreach (var duck in _ducks) duck.Preparing = false;
            Ready = true;
            image.enabled = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[CoopModeSelection] Portrait failed: {e}");
            if (_stage) _stage.SetActive(false);
        }
    }

    private async UniTask<GameObject> Attach(int id, Transform socket, bool handheld)
    {
        var item = await ItemAssetsCollection.InstantiateAsync(id);
        if (!this || !_stage)
        {
            if (item) item.DestroyTree();
            return null;
        }
        if (!item) throw new InvalidOperationException("Portrait equipment is unavailable: " + id);
        _items.Add(item);
        item.transform.SetParent(_stage.transform, false);
        item.gameObject.SetActive(false);
        var equipment = item.AgentUtilities.GetPrefab(1900327206);
        var prefab = !handheld && equipment ? equipment.gameObject : item.ItemGraphic?.gameObject;
        if (!prefab) throw new InvalidOperationException("Portrait equipment model is unavailable: " + id);
        var visual = Instantiate(prefab, socket, false);
        DisableGameplay(visual);
        return visual;
    }

    private static void DisableGameplay(GameObject root)
    {
        foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
    }

    internal void SetHovered(bool hovered)
    {
        foreach (var duck in _ducks) if (duck) duck.SetHovered(hovered);
    }

    private void OnDestroy()
    {
        if (_stage) { _stage.SetActive(false); Destroy(_stage); }
        foreach (var item in _items) if (item) item.DestroyTree();
        if (_texture) { _texture.Release(); Destroy(_texture); }
    }
}
