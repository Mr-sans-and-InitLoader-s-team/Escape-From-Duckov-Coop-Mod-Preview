using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeJoinFeedbackProbe
{
    private static readonly List<string> Notifications = new();
    private static bool _monitoring;

    private static void OnNotification(string text)
    {
        if (Notifications.Count < 500) Notifications.Add(text);
    }

    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        if ((bool?)command["monitor"] == true)
        {
            if (!_monitoring)
            {
                new Harmony("local.coop.runtime-tests.join-feedback").Patch(
                    AccessTools.Method(typeof(NotificationText), nameof(NotificationText.Push)),
                    prefix: new HarmonyMethod(typeof(RuntimeJoinFeedbackProbe), nameof(OnNotification)));
                _monitoring = true;
            }
            Notifications.Clear();
        }
        if ((bool?)command["notifications"] == true) return VerifyNotifications(mod);
        if ((bool?)command["preview"] == true)
        {
            var paused = (bool?)command["paused"] == true && !GameManager.Paused;
            if (paused) PauseMenu.Show();
            try { return await VerifyPreview(mod); }
            finally { if (paused) PauseMenu.Hide(); }
        }
        return new { notifications = Notifications.ToArray() };
    }

    private static object VerifyNotifications(Assembly mod)
    {
        var manager = EconomyManager.Instance;
        var original = (EconomyManager.SaveData)manager.GenerateSaveData();
        var queue = (Queue<string>)AccessTools.Field(typeof(NotificationText), "pendingTexts").GetValue(null);
        var pending = queue.ToArray();
        var home = mod.GetType("EscapeFromDuckovCoopMod.SharedHome", true);
        var apply = AccessTools.Method(home, "Apply");
        void Apply(Action action) => apply.Invoke(null, new object[] { action });
        var events = 0;
        void Unlocked(int _) => events++;
        EconomyManager.OnItemUnlockStateChanged += Unlocked;
        try
        {
            object Check(bool replay, bool showUI)
            {
                manager.SetupSaveData(new EconomyManager.SaveData
                {
                    money = original.money,
                    unlockedItems = original.unlockedItems.Where(id => id != 140).ToArray(),
                    unlockesWaitingForConfirm = original.unlockesWaitingForConfirm.Where(id => id != 140).ToArray()
                });
                queue.Clear();
                var before = events;
                void Unlock() => EconomyManager.Unlock(140, needConfirm: false, showUI: showUI);
                if (replay) Apply(Unlock); else Unlock();
                return new { replay, requestedUI = showUI, unlocked = EconomyManager.IsUnlocked(140),
                    stateEvents = events - before, notifications = queue.ToArray() };
            }
            var replay = Check(true, true);
            var normal = Check(false, true);
            var explicitlySilent = Check(false, false);
            try { Apply(() => Apply(() => throw new InvalidOperationException("Replay scope fixture"))); }
            catch (TargetInvocationException) { }
            var restored = !(bool)AccessTools.Property(home, "Applying").GetValue(null);
            var afterException = Check(false, true);
            return new { replay, normal, explicitlySilent, scopeRestoredAfterException = restored, afterException };
        }
        finally
        {
            EconomyManager.OnItemUnlockStateChanged -= Unlocked;
            manager.SetupSaveData(original);
            queue.Clear();
            foreach (var text in pending) queue.Enqueue(text);
        }
    }

    private static async UniTask<object> VerifyPreview(Assembly mod)
    {
        var selectionType = mod.GetType("EscapeFromDuckovCoopMod.CoopModeSelectionUI", true);
        var previewType = mod.GetType("EscapeFromDuckovCoopMod.CoopModePreview", true);
        var animationType = mod.GetType("EscapeFromDuckovCoopMod.CoopModeDuckAnimation", true);
        var selection = Object.FindObjectsByType(selectionType, FindObjectsSortMode.None).Single();
        AccessTools.Field(selectionType, "_chosen").SetValue(selection, true);
        AccessTools.Method(selectionType, "Close").Invoke(selection, null);
        await UniTask.NextFrame();
        AccessTools.Method(selectionType, "Show").Invoke(selection, null);
        var frames = new List<object>();
        var end = Time.unscaledTime + 2f;
        while (Time.unscaledTime < end)
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            foreach (Component preview in Object.FindObjectsByType(previewType, FindObjectsSortMode.None))
            {
                var ducks = ((System.Collections.IEnumerable)AccessTools.Field(previewType, "_ducks").GetValue(preview))
                    .Cast<Component>().ToArray();
                frames.Add(new { frame = Time.frameCount, timeScale = Time.timeScale, card = preview.transform.parent.parent.name,
                    visible = preview.GetComponent<RawImage>().enabled,
                    ready = (bool)AccessTools.Property(previewType, "Ready").GetValue(preview),
                    ducks = ducks.Select(d => new {
                        settled = AccessTools.Property(animationType, "Settled")?.GetValue(d),
                        speed = d.GetComponentInChildren<Animator>(true).GetFloat("MoveSpeed"),
                        transitioning = Enumerable.Range(0, d.GetComponentInChildren<Animator>(true).layerCount)
                            .Any(layer => d.GetComponentInChildren<Animator>(true).IsInTransition(layer))
                    }).ToArray() });
            }
        }
        return frames;
    }
}
