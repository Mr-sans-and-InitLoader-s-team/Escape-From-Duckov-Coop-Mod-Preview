using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal static class ConnectionErrorDialog
{
    private static GameObject _panel;
    private static readonly Queue<string> Errors = new();
    internal static string LastDetails { get; private set; }
    internal static void Show(MModUI ui, Transform canvas, string title, string details)
    {
        Errors.Enqueue(title + "\n" + details);
        while (Errors.Count > 8) Errors.Dequeue();
        LastDetails = string.Join("\n\n────────\n\n", Errors);
        if (_panel) UnityEngine.Object.Destroy(_panel);
        _panel = ui.CreateModernPanel("ConnectionErrorDialog", canvas, new Vector2(760, 510), Vector2.zero, TextAnchor.MiddleCenter);
        var rect = _panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        var layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 20);
        layout.spacing = 14;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        var heading = ui.CreateText("Title", _panel.transform, CoopLocalization.Get("net.sharedHome.title"), 22,
            MModUI.GlassTheme.Text, TextAlignmentOptions.Left, FontStyles.Bold);
        CoopLocalizedText.Bind(heading, "net.sharedHome.title");
        heading.gameObject.GetComponent<LayoutElement>().preferredHeight = 34;
        var scroll = ui.CreateModernScrollView("ErrorDetails", _panel.transform, 348);
        var content = scroll.GetComponent<ScrollRect>().content;
        var text = ui.CreateText("Details", content, LastDetails, 17, MModUI.GlassTheme.Text);
        text.richText = false;
        var close = ui.CreateModernButton("CloseError", _panel.transform, CoopLocalization.Get("net.sharedHome.close"), () =>
        {
            Errors.Clear();
            LastDetails = null;
            if (_panel) _panel.SetActive(false);
        }, height: 42);
        CoopLocalizedText.Bind(close.GetComponentInChildren<TMP_Text>(), "net.sharedHome.close");
        _panel.transform.SetAsLastSibling();
        ui.StartCoroutine(ui.AnimatePanel(_panel, true));
    }
}
