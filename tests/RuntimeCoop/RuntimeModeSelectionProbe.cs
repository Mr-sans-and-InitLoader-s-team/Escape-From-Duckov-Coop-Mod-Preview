using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeModeSelectionProbe
{
    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        var root = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(r => r.name == "CoopModeSelection");
        bool? lightsRestored = null;
        if (command["width"] != null)
        {
            Screen.SetResolution((int)command["width"], (int)command["height"], false);
            await UniTask.Delay(500, ignoreTimeScale: true);
        }
        if (command["turn"] != null)
            foreach (var model in Object.FindObjectsByType<CharacterModel>(FindObjectsSortMode.None).Where(m => m.name == "PortraitDuck"))
                model.transform.localRotation *= Quaternion.Euler(0, (float)command["turn"], 0);
        if (command["sample"] != null)
            foreach (var model in Object.FindObjectsByType<CharacterModel>(FindObjectsSortMode.None).Where(m => m.name == "PortraitDuck"))
            {
                var animator = model.GetComponentInChildren<Animator>(true);
                var state = animator.GetBehaviours<StateMachineBehaviour>().First(s => AccessTools.Field(s.GetType(), "magicBlendAsset")?.GetValue(s) is ScriptableObject asset && asset.name == (string)command["sample"]);
                var asset = AccessTools.Field(state.GetType(), "magicBlendAsset").GetValue(state);
                ((AnimationClip)AccessTools.Field(asset.GetType(), "basePose").GetValue(asset)).SampleAnimation(animator.gameObject, 0);
                ((AnimationClip)AccessTools.Field(asset.GetType(), "overlayPose").GetValue(asset)).SampleAnimation(animator.gameObject, 0);
                model.CustomFace.RefreshAll();
                AccessTools.Method(typeof(CustomFaceInstance), "UpdateHands").Invoke(model.CustomFace, null);
                foreach (var gun in model.GetComponentsInChildren<ItemGraphicInfo_Gun>(true))
                {
                    gun.transform.SetParent(model.RightHandSocket, false);
                    gun.transform.localPosition = Vector3.zero;
                    gun.transform.localRotation = Quaternion.identity;
                }
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            }
        if (command["hover"] != null)
        {
            foreach (var name in new[] { "IndependentCoopCard", "SharedCoopCard" })
            {
                var card = root.GetComponentsInChildren<RectTransform>().Single(t => t.name == name);
                var data = new PointerEventData(EventSystem.current);
                if ((string)command["hover"] == name) ExecuteEvents.Execute(card.gameObject, data, ExecuteEvents.pointerEnterHandler);
                else ExecuteEvents.Execute(card.gameObject, data, ExecuteEvents.pointerExitHandler);
            }
            await UniTask.Delay(350, ignoreTimeScale: true);
        }
        if (command["select"] != null)
        {
            var selection = Object.FindObjectsByType(mod.GetType("EscapeFromDuckovCoopMod.CoopModeSelectionUI"), FindObjectsSortMode.None).Single();
            var lights = ((System.Collections.IEnumerable)AccessTools.Field(selection.GetType(), "_sceneLights").GetValue(selection)).Cast<object>()
                .Select(v => (Light: (Light)v.GetType().GetField("Item1").GetValue(v), Mask: (int)v.GetType().GetField("Item2").GetValue(v))).ToArray();
            var card = root.GetComponentsInChildren<Button>().Single(b => b.name == (string)command["select"]);
            card.onClick.Invoke();
            await UniTask.NextFrame();
            lightsRestored = lights.All(v => !v.Light || v.Light.cullingMask == v.Mask);
        }
        if (command["screenshot"] != null) Capture(root.GetComponentInParent<Canvas>(), (string)command["screenshot"]);
        return new
        {
            faceParts = new[] { Duckov.Utilities.GameplayDataSettings.CustomFaceData.Mouths, Duckov.Utilities.GameplayDataSettings.CustomFaceData.Eyes }
                .Select(collection => ((System.Collections.IEnumerable)AccessTools.Field(collection.GetType(), "parts").GetValue(collection)).Cast<object>()
                    .Take(5).Select(entry => new { id = AccessTools.Field(entry.GetType(), "id").GetValue(entry),
                        part = ((CustomFacePart)AccessTools.Field(entry.GetType(), "part").GetValue(entry)).name,
                        mesh = ((CustomFacePart)AccessTools.Field(entry.GetType(), "part").GetValue(entry)).GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh?.vertexCount).ToArray() }).ToArray()).ToArray(),
            open = root && root.gameObject.activeInHierarchy,
            lightsRestored,
            width = Screen.width, height = Screen.height,
            shared = mod.GetType("EscapeFromDuckovCoopMod.SharedHome").GetProperty("Enabled").GetValue(null),
            characters = Object.FindObjectsByType<CharacterMainControl>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
            cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.name == "PortraitCamera").Select(c => new { c.name, c.enabled, texture = c.targetTexture?.name, c.cullingMask, c.fieldOfView, position = c.transform.localPosition }).ToArray(),
            lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.name == "PortraitLight")
                .Select(l => new { l.intensity, l.cullingMask, rotation = l.transform.localEulerAngles }).ToArray(),
            cards = !root ? null : root.GetComponentsInChildren<Button>().Select(b => new {
                b.name,
                roundedMask = b.GetComponentInChildren<Mask>().enabled,
                headers = b.GetComponentsInChildren<TMP_Text>().Where(t => t.name == "Number" || t.name == "ModeTitle" || t.name == "Info")
                    .Select(t => new { t.name, y = t.rectTransform.anchoredPosition.y }).ToArray(),
                scrim = b.GetComponentsInChildren<RectTransform>().Where(t => t.name == "BottomScrim")
                    .Select(t => new { bottom = t.anchoredPosition.y, height = t.sizeDelta.y }).Single()
            }).ToArray(),
            previews = Object.FindObjectsByType(mod.GetType("EscapeFromDuckovCoopMod.CoopModePreview"), FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Select(p => AccessTools.Property(p.GetType(), "Ready").GetValue(p)).ToArray(),
            ducks = Object.FindObjectsByType<CharacterModel>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(m => m.name == "PortraitDuck").Select(m => new { m.name, m.enabled,
                    renderers = m.GetComponentsInChildren<Renderer>().Length, scripts = m.GetComponentsInChildren<Behaviour>().Count(b => b.enabled),
                    position = m.transform.localPosition, rotation = m.transform.localEulerAngles,
                    mouth = m.CustomFace.mouthPart.GetCurrentPartID(), eye = m.CustomFace.eyePart.GetCurrentPartID(),
                    mouthSocket = m.CustomFace.mouthPart.socket.localPosition,
                    mouthVisible = m.CustomFace.mouthPart.PartInstance && m.CustomFace.mouthPart.PartInstance.gameObject.activeInHierarchy,
                    face = JsonUtility.ToJson(m.CustomFace.ConvertToSaveData()),
                    clips = m.GetComponentsInChildren<Animator>(true).Where(a => a.runtimeAnimatorController)
                        .SelectMany(a => a.runtimeAnimatorController.animationClips).Select(a => a.name).Distinct().ToArray(),
                    poses = m.GetComponentsInChildren<Animator>(true).SelectMany(a => a.GetBehaviours<StateMachineBehaviour>())
                        .Select(s => AccessTools.Field(s.GetType(), "magicBlendAsset")?.GetValue(s)).OfType<ScriptableObject>().Select(a => a.name).Distinct().ToArray(),
                    activePose = m.GetComponentsInChildren<Component>().Where(c => c && c.GetType().FullName == "KINEMATION.MagicBlend.Runtime.MagicBlending")
                        .Select(c => ((Object)AccessTools.Property(c.GetType(), "BlendAsset").GetValue(c))?.name).ToArray(),
                    speed = m.GetComponentInChildren<Animator>(true).GetFloat("MoveSpeed"),
                    motion = m.GetComponentInChildren<Animator>(true).GetCurrentAnimatorClipInfo(0).Select(c => new { clip = c.clip.name, c.weight }).ToArray(),
                    guns = m.GetComponentsInChildren<ItemGraphicInfo_Gun>(true).Select(g => new {
                        attached = g.transform.parent == m.RightHandSocket, position = g.transform.localPosition,
                        rotation = g.transform.localEulerAngles }).ToArray(),
                    enabledScripts = m.GetComponentsInChildren<Behaviour>().Where(b => b.enabled).Select(b => b.GetType().FullName).ToArray(),
                    faceComponents = m.CustomFace.mouthPart.PartInstance.GetComponentsInChildren<Component>().Where(c => c).Select(c => c.GetType().FullName).Distinct().ToArray(),
                    mouthRenderers = m.CustomFace.mouthPart.PartInstance.renderers.Select(r => new { r.enabled,
                        center = m.transform.InverseTransformPoint(r.bounds.center), size = r.bounds.size, type = r.GetType().Name,
                        mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh?.vertexCount : r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount,
                        scale = r.transform.lossyScale }).ToArray() }).ToArray(),
            labels = !root ? null : root.GetComponentsInChildren<TMP_Text>().Select(t => new { t.name, t.text, overflow = t.isTextOverflowing }).ToArray(),
            oldToggle = Object.FindObjectsByType<Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.name == "SharedHomeToggle")
        };
    }

    internal static void Capture(Canvas canvas, string screenshot)
    {
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "mode-selection"));
        var path = Path.Combine(folder, Path.GetFileName(screenshot));
        foreach (var portrait in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.name == "PortraitCamera" && c.targetTexture))
            Render(portrait, portrait.targetTexture);
        var previousCamera = canvas.worldCamera;
        var mode = canvas.renderMode;
        var distance = canvas.planeDistance;
        var layers = canvas.GetComponentsInChildren<Transform>(true).Select(t => (Transform: t, Layer: t.gameObject.layer)).ToArray();
        var camera = new GameObject("ModeSelectionCaptureCamera").AddComponent<Camera>();
        var texture = new RenderTexture(Screen.width, Screen.height, 24);
        var pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        var active = RenderTexture.active;
        try
        {
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            foreach (var layer in layers) layer.Transform.gameObject.layer = 28;
            camera.cullingMask = 1 << 28;
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            Canvas.ForceUpdateCanvases();
            Render(camera, texture);
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = mode;
            canvas.worldCamera = previousCamera;
            canvas.planeDistance = distance;
            foreach (var layer in layers) if (layer.Transform) layer.Transform.gameObject.layer = layer.Layer;
            RenderTexture.active = active;
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(pixels);
            Object.Destroy(camera.gameObject);
        }
    }

    private static void Render(Camera camera, RenderTexture target)
    {
        var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target };
        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request))
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
        else camera.Render();
    }
}
