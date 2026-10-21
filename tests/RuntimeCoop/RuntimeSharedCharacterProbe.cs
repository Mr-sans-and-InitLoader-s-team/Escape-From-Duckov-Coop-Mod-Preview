using System.Reflection;
using Duckov.Scenes;
using HarmonyLib;
using ItemStatsSystem.Data;
using Newtonsoft.Json.Linq;
using Saves;
using UnityEngine;
using UnityEngine.UI;

namespace CoopRuntimeTests;

internal static class RuntimeSharedCharacterProbe
{
    internal static object Run(Assembly mod, JObject command)
    {
        RuntimeSharedHomeProbe.PrepareIdentity(mod);
        var loadouts = mod.GetType("EscapeFromDuckovCoopMod.SharedHomeLoadouts");
        var identity = (string)command["identity"] ?? "private-test-publicclient";
        var key = (string)AccessTools.Method(loadouts, "Key").Invoke(null, new object[] { identity });
        SavesSystem.KeyExisits("Created");
        var cache = ES3File.cachedFiles[SavesSystem.CurrentFilePath];
        if ((bool?)command["forget"] == true)
            foreach (var name in cache.GetKeys().Where(k => k == key || k.StartsWith(key + "/"))) cache.DeleteKey(name);
        if (command["tutorialComplete"] != null) cache.Save("MultiSceneCore_Visited_" + SceneInfoCollection.BaseSceneID, (bool)command["tutorialComplete"]);
        if (command["slot"] != null) SavesSystem.SetFile((int)command["slot"]);
        if ((bool?)command["tryExit"] == true)
        {
            var exit = UnityEngine.Object.FindObjectsByType<SceneLoaderProxy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(p => (string)AccessTools.Field(typeof(SceneLoaderProxy), "sceneID").GetValue(p) == "Level_Guide_Main");
            exit.LoadScene();
        }
        if ((bool?)command["confirm"] == true)
        {
            var ui = CustomFaceUI.ActiveView;
            if (!ui || !ui.canControl) throw new InvalidOperationException("Native face editor is not open");
            ui.skinColorPicker.SetColor(new Color((float?)command["r"] ?? .2f, (float?)command["g"] ?? .6f, (float?)command["b"] ?? .8f, 1f));
            for (var i = 0; i < ((int?)command["hairSteps"] ?? 0); i++) ui.hairSwitch.rightButton.onClick.Invoke();
            ui.RefreshInfos();
            var confirm = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Single(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == "SaveToMainCharacter"));
            confirm.onClick.Invoke();
        }
        var face = LevelManager.Instance?.CustomFaceManager.LoadMainCharacterSetting() ?? default;
        var record = SavesSystem.Load<byte[]>(key);
        return new
        {
            scene = MultiSceneCore.MainScene.HasValue ? MultiSceneCore.MainSceneID : null,
            tutorialComplete = MultiSceneCore.GetVisited(SceneInfoCollection.BaseSceneID),
            creating = AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.SharedHomeCharacterCreation"), "Creating").GetValue(null),
            ready = AccessTools.Property(loadouts, "Ready").GetValue(null),
            vote = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.SceneNet"), "sceneVoteActive").GetValue(
                AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.SceneNet"), "Instance").GetValue(null)),
            editor = CustomFaceUI.ActiveView && CustomFaceUI.ActiveView.canControl,
            face = face.DataToJson(),
            recorded = record != null || SavesSystem.Load<bool>(key + "/Created"),
            storedFace = SavesSystem.KeyExisits(key + "/Face") ? SavesSystem.Load<CustomFaceSettingData>(key + "/Face").DataToJson() : null,
            inventory = CharacterMainControl.Main ? ItemTreeData.FromItem(CharacterMainControl.Main.CharacterItem).entries.Select(e => new { type = e.typeID, count = e.StackCount }).ToArray() : null,
            mirrors = UnityEngine.Object.FindObjectsByType<Interact_CustomFace>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(e => new { e.name, active = e.gameObject.activeInHierarchy, e.Interacting }).ToArray(),
            nativeEditors = Resources.FindObjectsOfTypeAll<CustomFaceUI>().Select(e => new { e.name, scene = e.gameObject.scene.name, active = e.gameObject.activeInHierarchy, parent = e.transform.parent?.name }).ToArray(),
            nativeMirrors = Resources.FindObjectsOfTypeAll<Interact_CustomFace>().Select(e => new { e.name, scene = e.gameObject.scene.name, parent = e.transform.parent?.name }).ToArray(),
            faceObjects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(e => e.name == "CustomFace").Select(e => new { e.name, scene = e.gameObject.scene.name, components = e.GetComponents<Component>().Select(c => c.GetType().FullName).ToArray() }).ToArray()
        };
    }
}
