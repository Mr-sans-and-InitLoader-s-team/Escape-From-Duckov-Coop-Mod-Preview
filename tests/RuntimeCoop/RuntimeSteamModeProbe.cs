using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeSteamModeProbe
{
    private static bool _tracing;
    private static readonly HashSet<int> Traced = new();
    private static void TraceText(TextMeshProUGUI __instance, Exception __exception)
    {
        if (__exception == null || !Traced.Add(__instance.GetInstanceID())) return;
        var path = new List<string>();
        for (var t = __instance.transform; t; t = t.parent) path.Add(t.name);
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "mode-selection"));
        File.AppendAllText(Path.Combine(folder, "tmp-failures.jsonl"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            path = string.Join("/", path), __instance.text, materialCount = __instance.textInfo.materialCount,
            meshes = __instance.textInfo.meshInfo.Select(m => new { mesh = m.mesh?.name, uv = m.uvs2?.Length }).ToArray(),
            submeshes = ((TMP_SubMeshUI[])AccessTools.Field(typeof(TextMeshProUGUI), "m_subTextObjects").GetValue(__instance)).Select(m => m ? m.name : "null").ToArray()
        }) + "\n");
    }

    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        if (!_tracing)
        {
            new Harmony("local.coop.runtime-tests.tmp-trace").Patch(AccessTools.Method(typeof(TextMeshProUGUI), "UpdateSDFScale"),
                finalizer: new HarmonyMethod(typeof(RuntimeSteamModeProbe), nameof(TraceText)));
            _tracing = true;
        }
        Type Type(string name) => mod.GetType("EscapeFromDuckovCoopMod." + name, true);
        var managerType = Type("SteamLobbyManager");
        var manager = AccessTools.Property(managerType, "Instance").GetValue(null);
        var uiType = Type("MModUI");
        var ui = AccessTools.Field(uiType, "Instance").GetValue(null);
        if (command["mode"] != null)
        {
            var service = AccessTools.Field(Type("NetService"), "Instance").GetValue(null);
            AccessTools.Method(service.GetType(), "StopNetwork").Invoke(service, new object[] { true });
            AccessTools.Method(Type("SharedHome"), "SetEnabled").Invoke(null, new object[] { (bool)command["mode"] });
            AccessTools.Method(uiType, "OnTransportModeChanged").Invoke(ui, new[] { Enum.Parse(Type("NetworkTransportMode"), "SteamP2P") });
            ((IDictionary)AccessTools.Field(managerType, "_lobbyMetadata").GetValue(manager)).Clear();
            var optionsType = Type("SteamLobbyOptions");
            var options = Activator.CreateInstance(optionsType);
            AccessTools.Property(optionsType, "LobbyName").SetValue(options, "模式标签验证");
            AccessTools.Property(optionsType, "Visibility").SetValue(options, Enum.Parse(Type("SteamLobbyVisibility"), "FriendsOnly"));
            AccessTools.Method(managerType, "CreateLobby").Invoke(manager, new[] { options });
            await UniTask.WaitUntil(() => (bool)AccessTools.Property(managerType, "IsInLobby").GetValue(manager)).Timeout(TimeSpan.FromSeconds(25));
            await UniTask.Delay(500, ignoreTimeScale: true);
        }
        var id = (CSteamID)AccessTools.Property(managerType, "CurrentLobbyId").GetValue(manager);
        SteamMatchmaking.SetLobbyJoinable(id, false);
        if ((bool?)command["missing"] == true) SteamMatchmaking.DeleteLobbyData(id, "coop_mode");
        AccessTools.Method(managerType, "CacheLobbySnapshot").Invoke(manager, new object[] { id });
        if ((bool?)command["memberUpdate"] == true)
            AccessTools.Method(managerType, "OnLobbyChatUpdate").Invoke(manager, new object[] { new LobbyChatUpdate_t {
                m_ulSteamIDLobby = id.m_SteamID, m_ulSteamIDUserChanged = SteamUser.GetSteamID().m_SteamID,
                m_rgfChatMemberStateChange = (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered } });
        var args = new object[] { id, null };
        AccessTools.Method(managerType, "TryGetLobbyInfo").Invoke(manager, args);
        AccessTools.Method(uiType, "UpdateSteamLobbyList").Invoke(ui, null);
        AccessTools.Method(uiType, "UpdateModeDisplay").Invoke(ui, null);
        var components = AccessTools.Field(uiType, "_components").GetValue(ui);
        var panel = (GameObject)AccessTools.Field(components.GetType(), "MainPanel").GetValue(components);
        AccessTools.Field(uiType, "showUI").SetValue(ui, true);
        panel.SetActive(true);
        foreach (var group in panel.GetComponents<CanvasGroup>()) group.alpha = 1;
        await UniTask.Delay(300, ignoreTimeScale: true);
        Canvas.ForceUpdateCanvases();
        if (command["screenshot"] != null) RuntimeModeSelectionProbe.Capture(panel.GetComponentInParent<Canvas>(), (string)command["screenshot"]);
        return new
        {
            lobby = id.m_SteamID,
            metadata = SteamMatchmaking.GetLobbyData(id, "coop_mode"),
            shared = AccessTools.Property(args[1].GetType(), "SharedMode").GetValue(args[1]),
            badges = Object.FindObjectsByType(Type("CoopLobbyModeBadge"), FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Cast<Component>().Select(c => new { parent = c.transform.parent.name,
                    text = c.GetComponentInChildren<TMP_Text>().text, overflow = c.GetComponentInChildren<TMP_Text>().isTextOverflowing,
                    raycast = c.GetComponent<Image>().raycastTarget }).ToArray()
        };
    }
}
