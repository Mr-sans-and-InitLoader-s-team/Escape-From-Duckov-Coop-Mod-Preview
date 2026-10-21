using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CoopRuntimeTests;

internal static class RuntimeSessionUIProbe
{
    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        var uiType = mod.GetType("EscapeFromDuckovCoopMod.MModUI");
        var ui = AccessTools.Field(uiType, "Instance").GetValue(null);
        var components = AccessTools.Field(uiType, "_components").GetValue(ui);
        var button = (Button)AccessTools.Field(components.GetType(), "CoopModeSelectButton").GetValue(components);
        var panel = (GameObject)AccessTools.Field(components.GetType(), "MainPanel").GetValue(components);
        var toggle = (Toggle)AccessTools.Field(components.GetType(), "StreamerModeToggle").GetValue(components);
        var serviceType = mod.GetType("EscapeFromDuckovCoopMod.NetService");
        var service = AccessTools.Field(serviceType, "Instance").GetValue(null);
        var lobbyType = mod.GetType("EscapeFromDuckovCoopMod.SteamLobbyManager");
        var lobby = AccessTools.Property(lobbyType, "Instance").GetValue(null);
        if ((bool?)command["replayWorld"] == true)
        {
            var manager = mod.GetType("EscapeFromDuckovCoopMod.COOPManager");
            var weather = AccessTools.Field(manager, "Weather").GetValue(null);
            AccessTools.Method(weather.GetType(), "Client_RequestSnapshot").Invoke(weather, null);
        }
        if ((bool?)command["cancelSteamCreate"] == true)
        {
            var optionsType = mod.GetType("EscapeFromDuckovCoopMod.SteamLobbyOptions");
            var options = Activator.CreateInstance(optionsType);
            AccessTools.Property(optionsType, "Visibility").SetValue(options, Enum.Parse(mod.GetType("EscapeFromDuckovCoopMod.SteamLobbyVisibility"), "FriendsOnly"));
            AccessTools.Method(lobbyType, "CreateLobby").Invoke(lobby, new[] { options });
            AccessTools.Method(lobbyType, "LeaveLobby").Invoke(lobby, null);
            await UniTask.Delay(1500, ignoreTimeScale: true);
        }
        if ((bool?)command["transientBeforeLevel"] == true)
        {
            if (LevelManager.LevelInited) throw new InvalidOperationException("Probe requires no initialized level");
            var readerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("EscapeFromDuckovCoopMod.CoopPacketReader")).First(t => t != null);
            var receive = AccessTools.Method(serviceType, "OnNetworkReceive");
            foreach (var op in new byte[] { 8, 10, 11 })
            {
                var reader = Activator.CreateInstance(readerType, new object[] { new[] { op }, 0, 1 });
                receive.Invoke(service, new object[] {
                    null, reader, (byte)0, Enum.Parse(receive.GetParameters()[3].ParameterType, "ReliableOrdered") });
            }
        }
        if ((bool?)command["cancelReconnect"] == true)
            panel.GetComponentInParent<Canvas>().GetComponentsInChildren<Button>().Single(b => b.name == "CancelReconnect").onClick.Invoke();
        if (command["connecting"] != null) AccessTools.Field(serviceType, "isConnecting").SetValue(service, (bool)command["connecting"]);
        if (command["steamPending"] != null) AccessTools.Property(lobbyType, "IsLobbyRequestPending").SetValue(lobby, (bool)command["steamPending"]);
        if ((bool?)command["show"] == true) AccessTools.Method(uiType, "ShowConnectionPanel").Invoke(ui, null);
        AccessTools.Method(uiType, "UpdateModeDisplay").Invoke(ui, null);
        if ((bool?)command["click"] == true) button.onClick.Invoke();
        await UniTask.Delay(350, ignoreTimeScale: true);
        Canvas.ForceUpdateCanvases();
        var modeCard = button.transform.parent.GetComponent<RectTransform>();
        var connectCard = toggle.transform.parent.parent.GetComponent<RectTransform>();
        if (command["screenshot"] != null) RuntimeModeSelectionProbe.Capture(panel.GetComponentInParent<Canvas>(), (string)command["screenshot"]);
        return new
        {
            enabled = button.interactable,
            panelVisible = panel.activeInHierarchy,
            modeHeight = modeCard.rect.height,
            connectHeight = connectCard.rect.height,
            streamerParent = connectCard.name,
            streamerCount = panel.GetComponentsInChildren<Toggle>(true).Count(t => t.name == "StreamerModeToggle"),
            reconnecting = AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.ConnectionRecovery"), "Pending").GetValue(null),
            attempt = AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.ConnectionRecovery"), "Attempt").GetValue(null),
            inLobby = AccessTools.Property(lobbyType, "IsInLobby").GetValue(lobby),
            steamRequestPending = AccessTools.Property(lobbyType, "IsLobbyRequestPending").GetValue(lobby),
            modeChildren = modeCard.Cast<Transform>().Select(t => new { t.name, height = ((RectTransform)t).rect.height }).ToArray(),
            notice = panel.GetComponentInParent<Canvas>().GetComponentsInChildren<RectTransform>().Where(t => t.name == "DisconnectNotice")
                .Select(t => new { position = t.anchoredPosition, scale = t.localScale, alpha = t.GetComponent<CanvasGroup>().alpha,
                    text = t.GetComponentsInChildren<TMPro.TMP_Text>().Select(v => v.text).ToArray() }).ToArray(),
            open = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Any(t => t.name == "CoopModeSelection")
        };
    }
}
