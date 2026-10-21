using System.Collections;
using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal static class DisconnectNotice
{
    private static GameObject _panel;
    private static Coroutine _animation;
    private static MModUI _owner;
    internal static void Show(MModUI ui, Transform canvas, bool returning)
    {
        if (_animation != null && _owner) _owner.StopCoroutine(_animation);
        if (_panel) UnityEngine.Object.Destroy(_panel);
        _owner = ui;
        var autoReconnect = ConnectionRecovery.Pending;
        _panel = ui.CreateModernPanel("DisconnectNotice", canvas, new Vector2(540, 114), Vector2.zero, TextAnchor.UpperCenter);
        var panel = _panel;
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0, 148);
        var group = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();
        group.alpha = 0;
        group.blocksRaycasts = true;
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 16, 16);
        layout.spacing = 8;
        layout.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        CoopLocalizedText.Bind(ui.CreateText("Title", panel.transform, CoopLocalization.Get("net.connectionLost"), 22,
            MModUI.ModernColors.Error, TextAlignmentOptions.Left, FontStyles.Bold), "net.connectionLost");
        var details = ui.CreateText("Details", panel.transform, CoopLocalization.Get(returning ? "net.disconnect.returning" : "net.disconnect.reconnect"),
            16, MModUI.ModernColors.TextSecondary);
        var cancel = ui.CreateModernButton("CancelReconnect", panel.transform, CoopLocalization.Get("net.reconnect.cancel"),
            ConnectionRecovery.CancelAndStop, height: 36);
        CoopLocalizedText.Bind(cancel.GetComponentInChildren<TMP_Text>(), "net.reconnect.cancel");
        cancel.gameObject.SetActive(ConnectionRecovery.Pending);
        if (ConnectionRecovery.Pending) rect.sizeDelta = new Vector2(540, 164);
        panel.transform.SetAsLastSibling();
        _animation = ui.StartCoroutine(ShowAndHide());

        IEnumerator ShowAndHide()
        {
            yield return ui.AnimateCard(panel, true, new Vector2(0, -28), new Vector2(0, 148));
            while (panel && ConnectionRecovery.Pending)
            {
                var seconds = Mathf.Max(0, Mathf.CeilToInt(ConnectionRecovery.NextAttemptAt - Time.unscaledTime));
                details.text = CoopLocalization.Get(seconds > 0 ? "net.reconnect.countdown" : "net.reconnect.connecting",
                    seconds > 0 ? seconds : ConnectionRecovery.Attempt);
                panel.transform.SetAsLastSibling();
                yield return null;
            }
            if (!panel) yield break;
            cancel.gameObject.SetActive(false);
            if (autoReconnect) details.text = CoopLocalization.Get(NetService.Instance?.connectedPeer?.ConnectionState == ConnectionState.Connected
                ? "net.reconnect.connected" : "net.reconnect.stopped");
            yield return new WaitForSecondsRealtime(3.2f);
            if (panel) yield return ui.AnimateCard(panel, false, new Vector2(0, -28), new Vector2(0, 148));
            if (panel) UnityEngine.Object.Destroy(panel);
        }
    }
}
