using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SodaCraft.Localizations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeLocalizationProbe
{
    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        Type Type(string name) => mod.GetType("EscapeFromDuckovCoopMod." + name, true);
        var localization = Type("CoopLocalization");
        string Get(string key, params object[] args) => (string)AccessTools.Method(localization, "Get").Invoke(null, new object[] { key, args });
        LocalizationManager.SetLanguage(Enum.Parse<SystemLanguage>((string)command["language"]));
        AccessTools.Method(localization, "CheckLanguageChange").Invoke(null, null);
        await UniTask.Delay(300, ignoreTimeScale: true);
        var code = (string)AccessTools.Method(localization, "GetCurrentLanguage").Invoke(null, null);
        var file = Path.Combine(Path.GetDirectoryName(mod.Location), "Localization", code + ".json");
        var expected = (JArray)JObject.Parse(File.ReadAllText(file))["translations"];
        var mismatch = expected.Where(e => Get((string)e["key"]) != (string)e["value"]).Select(e => (string)e["key"]).ToArray();
        var formatMismatch = expected.Where(e => ((string)e["key"]).StartsWith("net.sharedHome.") &&
            Get((string)e["key"], "Example Mod", "Host Version", "Client Version") !=
            string.Format((string)e["value"], "Example Mod", "Host Version", "Client Version")).Select(e => (string)e["key"]).ToArray();
        var translations = AccessTools.Field(localization, "currentTranslations");
        var saved = translations.GetValue(null);
        var fixture = "Quoted \"mod\", slash \\, newline\n日本語 {unbalanced";
        string parsed;
        string[] fallbackMismatch;
        try
        {
            translations.SetValue(null, Activator.CreateInstance(saved.GetType()));
            AccessTools.Method(localization, "ParseJsonTranslations").Invoke(null, new object[] {
                new JObject { ["translations"] = new JArray(new JObject { ["key"] = "test.escape", ["value"] = fixture }) }.ToString() });
            parsed = Get("test.escape");
            AccessTools.Method(localization, "LoadFallbackTranslations").Invoke(null, null);
            var english = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(mod.Location), "Localization", "en-US.json")))["translations"];
            fallbackMismatch = english.Where(e => new[] { "ui.coopMode.", "ui.steam.coopMode.", "ui.sharedHome", "net.sharedHome.", "net.disconnect.", "net.reconnect." }
                .Any(prefix => ((string)e["key"]).StartsWith(prefix)) && Get((string)e["key"]) != (string)e["value"]).Select(e => (string)e["key"]).ToArray();
        }
        finally { translations.SetValue(null, saved); }

        var ui = AccessTools.Field(Type("MModUI"), "Instance").GetValue(null);
        var canvas = (Canvas)AccessTools.Field(Type("MModUI"), "_canvas").GetValue(ui);
        var components = AccessTools.Field(Type("MModUI"), "_components").GetValue(ui);
        var switchButton = (Button)AccessTools.Field(components.GetType(), "CoopModeSelectButton").GetValue(components);
        var menu = canvas.GetComponentsInChildren<RectTransform>().Single(t => t.name == "CoopModeSelection");
        Canvas.ForceUpdateCanvases();
        object[] Labels(Component parent) => parent.GetComponentsInChildren<TMP_Text>(true).Select(t =>
        {
            t.ForceMeshUpdate(true);
            var binding = t.GetComponent(Type("CoopLocalizedText"));
            var key = binding ? (string)AccessTools.Field(binding.GetType(), "_key").GetValue(binding) : null;
            return (object)new { t.name, t.text, t.fontSize, overflow = t.isTextOverflowing,
                key, matches = key == null || t.text == Get(key),
                missingGlyphs = t.text.Where(c => c > 127 && !t.font.HasCharacter(c, true, true)).Distinct().Select(c => c.ToString()).ToArray() };
        }).ToArray();
        var modeLabels = Labels(menu);
        if (command["screenshot"] != null) RuntimeModeSelectionProbe.Capture(canvas, (string)command["screenshot"]);
        var badges = new List<object>();
        foreach (bool? shared in new bool?[] { false, true, null })
        {
            var parent = new GameObject("LocalizationBadgeFixture", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            parent.transform.SetParent(canvas.transform, false);
            ((RectTransform)parent.transform).sizeDelta = new Vector2(600, 30);
            var badge = (Component)AccessTools.Method(Type("CoopLobbyModeBadge"), "Create").Invoke(null, new object[] { parent.transform, shared });
            await UniTask.NextFrame();
            Canvas.ForceUpdateCanvases();
            badges.Add(new { shared, labels = Labels(badge), width = badge.GetComponent<LayoutElement>().preferredWidth });
            Object.Destroy(parent);
        }
        var title = Get("net.sharedHome.rejectedHost", "Test Player");
        var hash = Get("net.sharedHome.hash", "Example Mod", new string('A', 64), new string('B', 64));
        AccessTools.Method(Type("ConnectionErrorDialog"), "Show").Invoke(null, new object[] { ui, canvas.transform, title, hash });
        await UniTask.Delay(350, ignoreTimeScale: true);
        Canvas.ForceUpdateCanvases();
        var error = (GameObject)AccessTools.Field(Type("ConnectionErrorDialog"), "_panel").GetValue(null);
        var errorLabels = Labels(error.transform);
        error.GetComponentsInChildren<Button>().Single(b => b.name == "CloseError").onClick.Invoke();
        AccessTools.Method(Type("DisconnectNotice"), "Show").Invoke(null, new object[] { ui, canvas.transform, true });
        await UniTask.Delay(350, ignoreTimeScale: true);
        Canvas.ForceUpdateCanvases();
        var notice = (GameObject)AccessTools.Field(Type("DisconnectNotice"), "_panel").GetValue(null);
        var mainPanel = (GameObject)AccessTools.Field(components.GetType(), "MainPanel").GetValue(components);
        var panelActive = mainPanel.activeSelf;
        mainPanel.SetActive(true);
        await UniTask.NextFrame();
        var switchLabel = switchButton.GetComponentInChildren<TMP_Text>().text;
        mainPanel.SetActive(panelActive);
        var result = new { code, nativeLanguage = LocalizationManager.CurrentLanguage.ToString(), count = expected.Count, mismatch, formatMismatch,
            escapedTextRoundTrip = parsed == fixture, fallbackMismatch, modeLabels, badges, errorLabels, noticeLabels = Labels(notice.transform),
            switchLabel };
        ((MonoBehaviour)ui).StopCoroutine((Coroutine)AccessTools.Field(Type("DisconnectNotice"), "_animation").GetValue(null));
        AccessTools.Field(Type("DisconnectNotice"), "_animation").SetValue(null, null);
        Object.Destroy(notice);
        return result;
    }
}
