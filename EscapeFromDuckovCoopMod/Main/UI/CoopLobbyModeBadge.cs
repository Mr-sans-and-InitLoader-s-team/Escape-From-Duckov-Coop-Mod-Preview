using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal sealed class CoopLobbyModeBadge : MonoBehaviour
{
    private TMP_Text _text;
    private Image _background;
    private LayoutElement _layout;
    private bool? _mode;
    private bool _initialized;
    private bool _measure;
    private string _language;

    internal static CoopLobbyModeBadge Create(Transform parent, bool? shared)
    {
        var root = new GameObject("CoopModeBadge", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var badge = root.AddComponent<CoopLobbyModeBadge>();
        badge._layout = root.AddComponent<LayoutElement>();
        badge._layout.preferredHeight = badge._layout.minHeight = 22;
        badge._layout.minWidth = badge._layout.preferredWidth = 76;
        badge._background = root.AddComponent<Image>();
        badge._background.raycastTarget = false;
        badge._text = CoopModeCard.Text("ModeLabel", root.transform, string.Empty, 12, Color.white, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        badge._text.fontStyle = FontStyles.Bold;
        badge._text.enableWordWrapping = false;
        badge._text.raycastTarget = false;
        badge._text.rectTransform.anchorMin = Vector2.zero;
        badge._text.rectTransform.anchorMax = Vector2.one;
        badge._text.rectTransform.sizeDelta = Vector2.zero;
        badge.SetMode(shared);
        return badge;
    }

    internal void SetMode(bool? shared)
    {
        if (_initialized && _mode == shared && _language == CoopLocalization.GetCurrentLanguage()) return;
        _mode = shared;
        _initialized = true;
        _text.text = CoopLocalization.Get(shared == true ? "ui.steam.coopMode.shared" : shared == false ? "ui.steam.coopMode.independent" : "ui.steam.coopMode.unknown");
        _language = CoopLocalization.GetCurrentLanguage();
        var tint = shared == true ? MModUI.ModernColors.Success : shared == false ? MModUI.ModernColors.Primary : MModUI.ModernColors.TextTertiary;
        _text.color = tint;
        MModUI.StylePillImage(_background, new Color(tint.r, tint.g, tint.b, .13f));
        _measure = true;
    }

    private void LateUpdate()
    {
        if (_language != CoopLocalization.GetCurrentLanguage()) SetMode(_mode);
        if (!_measure) return;
        // Hidden room rows have not run TMP Awake yet; measure only after they become active.
        _measure = false;
        _layout.minWidth = _layout.preferredWidth = Mathf.Max(76, _text.GetPreferredValues().x + 20);
        _text.havePropertiesChanged = true;
    }
}
