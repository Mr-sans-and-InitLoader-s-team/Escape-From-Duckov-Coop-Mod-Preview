using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeAppearanceInteractionProbe
{
    private static object Events(UnityEventBase value) => Enumerable.Range(0, value.GetPersistentEventCount())
        .Select(i => new { target = value.GetPersistentTarget(i)?.name, type = value.GetPersistentTarget(i)?.GetType().FullName,
            method = value.GetPersistentMethodName(i) }).ToArray();

    private static SceneLoaderProxy Entry() => Object.FindObjectsByType<SceneLoaderProxy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Single(p => p.name == "CustomFace");

    internal static object Snapshot()
    {
        var character = CharacterMainControl.Main;
        var manager = LevelManager.Instance?.InputManager;
        var action = character ? AccessTools.Field(typeof(CharacterMainControl), "currentAction").GetValue(character) as CharacterActionBase : null;
        return new { scene = MultiSceneCore.MainScene.HasValue ? MultiSceneCore.MainSceneID : null,
            inputActive = InputManager.InputActived, canMove = character && character.CanMove(),
            walkSpeed = character ? character.CharacterWalkSpeed : 0, action = action?.GetType().Name,
            actionRunning = action && action.Running,
            blocks = manager ? ((HashSet<GameObject>)AccessTools.Field(typeof(InputManager), "blockInputSources").GetValue(manager))
                .Select(g => new { name = g ? g.name : null, active = g && g.activeInHierarchy }).ToArray() : null,
            editor = CustomFaceUI.ActiveView && CustomFaceUI.ActiveView.canControl,
            position = character ? character.transform.position : default };
    }

    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        if ((bool?)command["catalog"] == true)
            return Object.FindObjectsByType<SceneLoaderProxy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(p => new { p.name, scene = (string)AccessTools.Field(typeof(SceneLoaderProxy), "sceneID").GetValue(p),
                    location = AccessTools.Field(typeof(SceneLoaderProxy), "location").GetValue(p),
                    components = p.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name).ToArray(),
                    interactions = p.GetComponents<InteractableBase>().Select(i => new { i.name, i.InteractTime, i.finishWhenTimeOut,
                        start = Events(i.OnInteractStartEvent), timeout = Events(i.OnInteractTimeoutEvent), finish = Events(i.OnInteractFinishedEvent) }).ToArray()
                }).ToArray();
        if ((bool?)command["interact"] == true)
        {
            var entry = Entry();
            if ((bool?)command["forceVisible"] == true) entry.gameObject.SetActive(true);
            if ((bool?)command["permit"] == true)
            {
                var sceneNet = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.SceneNet"), "Instance").GetValue(null);
                AccessTools.Method(sceneNet.GetType(), "AuthorizeLocalSceneLoad").Invoke(sceneNet,
                    new object[] { (string)AccessTools.Field(typeof(SceneLoaderProxy), "sceneID").GetValue(entry), 10f, true });
            }
            var interaction = entry.GetComponent<InteractableBase>();
            if (!interaction) throw new InvalidOperationException("Native appearance portal interaction missing");
            CharacterMainControl.Main.Interact(interaction);
        }
        if ((bool?)command["previewColor"] == true)
            CustomFaceUI.ActiveView.skinColorPicker.SetColor(new Color(.9f, .1f, .1f, 1f));
        if ((bool?)command["cancel"] == true)
        {
            var editor = Object.FindObjectsByType<Interact_CustomFace>(FindObjectsSortMode.None).Single(e => e.Interacting);
            editor.StopInteract();
        }
        if ((bool?)command["openEditor"] == true)
            CharacterMainControl.Main.Interact(Object.FindObjectsByType<Interact_CustomFace>(FindObjectsSortMode.None).Single());
        if ((bool?)command["returnBase"] == true)
            CharacterMainControl.Main.Interact(Object.FindObjectsByType<SceneLoaderProxy>(FindObjectsSortMode.None)
                .Single(p => p.name == "BackToBunker").GetComponent<InteractableBase>());
        if ((bool?)command["stopAction"] == true) CharacterMainControl.Main.interactAction.StopAction();
        if ((bool?)command["drive"] == true)
        {
            var before = Snapshot();
            var character = CharacterMainControl.Main;
            var manager = LevelManager.Instance.InputManager;
            // The isolated harness blocks InputManager.Update, including its normal camera cache setup.
            AccessTools.Field(typeof(InputManager), "mainCam").SetValue(manager, LevelManager.Instance.GameCamera.renderCamera);
            var start = character.transform.position;
            var end = Time.unscaledTime + .6f;
            var tick = AccessTools.Method(typeof(InputManager), "UpdateInputActived");
            try
            {
                while (Time.unscaledTime < end)
                {
                    tick.Invoke(manager, null);
                    manager.SetMoveInput(Vector2.up);
                    await UniTask.NextFrame();
                }
                return new { before, after = Snapshot(), distance = Vector3.Distance(start, character.transform.position) };
            }
            finally { if (manager) manager.SetMoveInput(Vector2.zero); }
        }
        return Snapshot();
    }
}
