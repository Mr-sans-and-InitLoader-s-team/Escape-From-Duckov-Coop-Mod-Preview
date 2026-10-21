using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeNativeSceneryProbe
{
    internal static async UniTask<object> Run(JObject command)
    {
        if (command["capture"] != null)
        {
            var roots = ((JArray)command["roots"]).Select(id => Resources.FindObjectsOfTypeAll<Transform>().Single(t => t.GetInstanceID() == (int)id)).ToArray();
            var meshes = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>()).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>()).Distinct().ToArray();
            var bounds = meshes[0].GetComponent<MeshRenderer>().bounds;
            foreach (var mesh in meshes) bounds.Encapsulate(mesh.GetComponent<MeshRenderer>().bounds);
            var stage = new GameObject("NativeBuildingCapture");
            var texture = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1024, 768, TextureFormat.RGBA32, false);
            var active = RenderTexture.active;
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(l => (Light: l, Mask: l.cullingMask)).ToArray();
            try
            {
                stage.transform.position = new Vector3(20000, -10000, 20000);
                var scale = 4f / Mathf.Max(bounds.size.x, bounds.size.z);
                foreach (var mesh in meshes)
                {
                    var visual = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                    visual.transform.SetParent(stage.transform, false);
                    visual.transform.localPosition = (mesh.transform.position - bounds.center) * scale;
                    visual.transform.localRotation = mesh.transform.rotation;
                    visual.transform.localScale = mesh.transform.lossyScale * scale;
                    visual.GetComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                    visual.GetComponent<MeshRenderer>().sharedMaterials = mesh.GetComponent<MeshRenderer>().sharedMaterials;
                    visual.layer = 29;
                }
                foreach (var source in lights) source.Light.cullingMask &= ~(1 << 29);
                var light = new GameObject("NativeBuildingCaptureLight").AddComponent<Light>();
                light.transform.SetParent(stage.transform, false);
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(40, -135, 0);
                light.intensity = .9f;
                light.cullingMask = 1 << 29;
                var camera = new GameObject("NativeBuildingCaptureCamera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = (float?)command["size"] ?? 2;
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 50;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << 29;
                camera.transform.localPosition = command["camera"]?.ToObject<Vector3>() ?? new Vector3(3, 1.5f, 5);
                camera.transform.LookAt(stage.transform.position);
                camera.targetTexture = texture;
                await UniTask.NextFrame();
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = texture };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                else camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "mode-selection"));
                File.WriteAllBytes(Path.Combine(folder, Path.GetFileName((string)command["capture"])), pixels.EncodeToPNG());
            }
            finally
            {
                foreach (var light in lights) if (light.Light) light.Light.cullingMask = light.Mask;
                RenderTexture.active = active;
                texture.Release();
                Object.Destroy(texture); Object.Destroy(pixels); Object.Destroy(stage);
            }
            return new { meshes = meshes.Length, bounds.center, bounds.size };
        }
        var candidates = Resources.FindObjectsOfTypeAll<MeshFilter>().Where(f => f && f.sharedMesh && f.gameObject.scene.IsValid() &&
            f.gameObject.scene.isLoaded && f.GetComponent<MeshRenderer>() && !f.GetComponentInParent<CharacterMainControl>() &&
            f.GetComponent<MeshRenderer>().bounds.size.y > 3).ToArray();
        return new
        {
            buildings = candidates.Select(f => new { f.name, mesh = f.sharedMesh.name,
                ancestors = Ancestors(f.transform).Take(6).Select(t => new { t.name, id = t.GetInstanceID() }).ToArray(),
                center = f.GetComponent<MeshRenderer>().bounds.center, size = f.GetComponent<MeshRenderer>().bounds.size }).ToArray(),
            smoke = Resources.FindObjectsOfTypeAll<ParticleSystem>().Where(p => p.name.ToLowerInvariant().Contains("smoke"))
                .Select(p => new { p.name, path = string.Join("/", Ancestors(p.transform).Reverse().Select(t => t.name)),
                    materials = p.GetComponent<ParticleSystemRenderer>().sharedMaterials.Select(m => m ? m.name : "null").ToArray(),
                    p.main.loop, p.main.duration, p.main.maxParticles, prefab = !p.gameObject.scene.IsValid() }).ToArray()
        };
    }

    private static IEnumerable<Transform> Ancestors(Transform current)
    {
        while (current) { yield return current; current = current.parent; }
    }
}
