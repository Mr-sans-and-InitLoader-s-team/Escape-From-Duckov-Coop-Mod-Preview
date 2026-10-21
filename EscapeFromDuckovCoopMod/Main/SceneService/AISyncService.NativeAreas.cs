using Duckov.MiniMaps;
using Duckov.Scenes;
using UnityEngine;
using UnityEngine.Rendering;

namespace EscapeFromDuckovCoopMod;

public sealed partial class AISyncService
{
    private readonly Dictionary<string, AISpecialAttachmentBase[]> _nativeAreaPrefabs = new();
    private readonly Dictionary<int, GameObject> _clientNativeAreas = new();

    private AISpecialAttachmentBase[] NativeAreaPrefabs(string presetKey)
    {
        if (string.IsNullOrEmpty(presetKey)) return Array.Empty<AISpecialAttachmentBase>();
        if (_nativeAreaPrefabs.TryGetValue(presetKey, out var cached)) return cached;
        var preset = ResolveCharacterPreset(presetKey);
        if (!preset) return Array.Empty<AISpecialAttachmentBase>();
        var areas = new List<AISpecialAttachmentBase>();
        if (preset.specialAttachmentBases != null)
        {
            foreach (var attachment in preset.specialAttachmentBases)
            {
                if (!attachment || attachment.GetType() != typeof(AISpecialAttachmentBase)) continue;
                if (!attachment.GetComponentsInChildren<SimplePointOfInterest>(true).Any(p => p.IsArea)) continue;
                // Only native presentation components; never run another AI, damage zone or reward action.
                if (attachment.GetComponentsInChildren<Component>(true).Any(c => !c ||
                    c is not Transform && c is not Collider && c is not Volume &&
                    c is not SimplePointOfInterest && c.GetType() != typeof(AISpecialAttachmentBase))) continue;
                areas.Add(attachment);
            }
        }
        return _nativeAreaPrefabs[presetKey] = areas.ToArray();
    }

    private bool HasNativeArea(string presetKey) => NativeAreaPrefabs(presetKey).Length > 0;

    private void ApplyNativeAreaScene(AISyncEntry entry, CharacterMainControl character)
    {
        if (!HasNativeArea(entry.CharacterPresetKey)) return;
        var index = Traverse.Create(character).Field<int>("relatedScene").Value;
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByBuildIndex(index);
        if (!scene.IsValid()) return;
        entry.SceneBuildIndex = index;
        entry.ScenePath = scene.path;
    }

    private void Client_UpdateNativeArea(AISyncEntry entry)
    {
        if (entry.Status == AIStatus.Dead || entry.Status == AIStatus.Despawned)
        {
            Client_RemoveNativeArea(entry.Id);
            return;
        }
        if (!_clientNativeAreas.TryGetValue(entry.Id, out var root) || !root)
        {
            var prefabs = NativeAreaPrefabs(entry.CharacterPresetKey);
            if (prefabs.Length == 0 || !MultiSceneCore.MainScene.HasValue) return;
            root = new GameObject($"RemoteAI_Area_{entry.Id}");
            root.SetActive(false);
            MultiSceneCore.MoveToActiveWithScene(root, entry.SceneBuildIndex);
            root.transform.SetPositionAndRotation(GetEntryAnchor(entry), entry.LastKnownRotation);
            foreach (var prefab in prefabs)
            {
                var attachment = UnityEngine.Object.Instantiate(prefab, root.transform);
                attachment.transform.localPosition = Vector3.zero;
                attachment.transform.localRotation = Quaternion.identity;
            }
            foreach (var point in root.GetComponentsInChildren<SimplePointOfInterest>(true))
            {
                Traverse.Create(point).Field<bool>("followActiveScene").Value = false;
                Traverse.Create(point).Field<string>("overrideSceneID").Value = SceneInfoCollection.GetSceneID(entry.SceneBuildIndex);
            }
            _clientNativeAreas[entry.Id] = root;
        }
        var character = TryGetCharacter(entry.Id);
        root.transform.SetPositionAndRotation(character ? character.transform.position : GetEntryAnchor(entry), entry.LastKnownRotation);
        var localScene = MultiSceneCore.ActiveSubScene ?? MultiSceneCore.MainScene;
        root.SetActive(localScene.HasValue && localScene.Value.buildIndex == entry.SceneBuildIndex);
    }

    private void Client_RemoveNativeArea(int id)
    {
        if (!_clientNativeAreas.TryGetValue(id, out var root)) return;
        if (root) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
        _clientNativeAreas.Remove(id);
    }

    private void ResetNativeAreas()
    {
        foreach (var id in _clientNativeAreas.Keys.ToArray()) Client_RemoveNativeArea(id);
        _nativeAreaPrefabs.Clear();
    }
}
