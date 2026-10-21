using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal sealed class CoopModeCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private Image _border, _selection;
    private RawImage _portrait;
    private CoopModePreview _preview;
    private CanvasGroup _details, _summary;
    private RectTransform _detailRect;
    private RectTransform _scrim;
    private Color _accent;
    private float _amount;
    private bool _hovered, _focused;

    internal static void Create(Transform parent, bool shared, Vector2 position, Action<bool> select, CoopModePortrait settings)
    {
        var rect = Rect(shared ? "SharedCoopCard" : "IndependentCoopCard", parent, new Vector2(500, 760), position);
        var card = rect.gameObject.AddComponent<CoopModeCard>();
        card._accent = shared ? MModUI.ModernColors.Success : MModUI.ModernColors.Primary;
        card._border = rect.gameObject.AddComponent<Image>();
        MModUI.StyleCardImage(card._border, MModUI.ModernColors.InputBorder);
        var shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = MModUI.ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -8);
        var background = Rect("Background", rect, new Vector2(496, 756), Vector2.zero);
        var cardColor = MModUI.GlassTheme.CardBg;
        MModUI.StyleCardImage(background.gameObject.AddComponent<Image>(), new Color(cardColor.r, cardColor.g, cardColor.b, .97f));
        background.GetComponent<Image>().raycastTarget = false;
        background.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = card._border;
        button.onClick.AddListener(() => select(shared));

        var atmosphere = Rect("PortraitBackdrop", background, new Vector2(496, 756), Vector2.zero);
        atmosphere.gameObject.AddComponent<CoopModeBackdrop>().Initialize(settings.backdrop, cardColor);
        var portrait = Rect("DuckPair", background, new Vector2(492, 500), new Vector2(0, 48));
        card._portrait = portrait.gameObject.AddComponent<RawImage>();
        card._portrait.raycastTarget = false;
        card._preview = portrait.gameObject.AddComponent<CoopModePreview>();
        card._preview.Initialize(card._portrait, shared, settings);
        card._scrim = Rect("BottomScrim", background, new Vector2(496, 240), Vector2.zero);
        card._scrim.anchorMin = card._scrim.anchorMax = new Vector2(.5f, 0);
        card._scrim.pivot = new Vector2(.5f, 0);
        card._scrim.anchoredPosition = Vector2.zero;
        var scrim = card._scrim.gameObject.AddComponent<CoopModeCardScrim>();
        scrim.color = new Color(cardColor.r, cardColor.g, cardColor.b, 1);
        scrim.raycastTarget = false;
        var key = shared ? "shared" : "independent";
        Text("Number", background, shared ? "02" : "01", 18, card._accent, new Vector2(40, 44), new Vector2(-204, 332), TextAlignmentOptions.MidlineLeft);
        var title = LocalizedText("ModeTitle", background, $"ui.coopMode.{key}.title", 28, MModUI.ModernColors.TextPrimary,
            new Vector2(370, 44), new Vector2(15, 332), TextAlignmentOptions.MidlineLeft);
        title.enableAutoSizing = true;
        title.fontSizeMin = 18;
        title.fontSizeMax = 28;
        title.enableWordWrapping = false;
        LocalizedText("Tag", background, $"ui.coopMode.{key}.tag", 14, MModUI.ModernColors.TextTertiary,
            new Vector2(410, 26), new Vector2(0, 289));
        Text("Info", background, "i", 20, card._accent, new Vector2(30, 44), new Vector2(218, 332), TextAlignmentOptions.Midline);
        var line = Rect("Rule", background, new Vector2(440, 1), new Vector2(0, 264));
        line.gameObject.AddComponent<Image>().color = new Color(card._accent.r, card._accent.g, card._accent.b, .22f);
        line.GetComponent<Image>().raycastTarget = false;

        var summary = Rect("Summary", background, new Vector2(428, 155), new Vector2(0, -208));
        card._summary = summary.gameObject.AddComponent<CanvasGroup>();
        LocalizedText("SummaryText", summary, $"ui.coopMode.{key}.summary", 18,
            MModUI.ModernColors.TextSecondary, new Vector2(428, 155), Vector2.zero);
        var details = Rect("Details", background, new Vector2(496, 340), new Vector2(0, -160));
        card._detailRect = details;
        card._details = details.gameObject.AddComponent<CanvasGroup>();
        card._details.alpha = 0;
        card._details.blocksRaycasts = false;
        LocalizedText("DetailsHeading", details, $"ui.coopMode.{key}.tag", 19, card._accent,
            new Vector2(428, 34), new Vector2(0, 124));
        LocalizedText("Description", details, $"ui.coopMode.{key}.detail", 17,
            MModUI.ModernColors.TextSecondary, new Vector2(428, 230), new Vector2(0, -12)).lineSpacing = 3;
        var selection = Rect("Select", background, new Vector2(428, 44), new Vector2(0, -324));
        card._selection = selection.gameObject.AddComponent<Image>();
        MModUI.StyleControlImage(card._selection, MModUI.ModernColors.Primary);
        card._selection.raycastTarget = false;
        LocalizedText("SelectLabel", selection, "ui.coopMode.select", 20,
            MModUI.ModernColors.PrimaryText, new Vector2(420, 44), Vector2.zero, TextAlignmentOptions.Center);
        rect.gameObject.AddComponent<CanvasGroup>().alpha = 1;
    }

    private void Update()
    {
        _amount = Mathf.MoveTowards(_amount, _hovered || _focused ? 1 : 0, Time.unscaledDeltaTime * 5);
        _border.color = Color.Lerp(MModUI.ModernColors.InputBorder, _accent, _amount);
        _selection.color = Color.Lerp(MModUI.ModernColors.Primary, MModUI.ModernColors.PrimaryHover, _amount);
        _portrait.color = Color.Lerp(Color.white, new Color(.94f, .94f, .94f, 1), _amount);
        _details.alpha = _amount;
        _summary.alpha = 1 - _amount;
        _detailRect.anchoredPosition = new Vector2(0, Mathf.Lerp(-195, -150, _amount));
        _scrim.sizeDelta = new Vector2(496, Mathf.Lerp(240, 450, _amount));
        _preview.SetHovered(_hovered || _focused);
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
    public void OnPointerExit(PointerEventData eventData) => _hovered = false;
    public void OnSelect(BaseEventData eventData) => _focused = true;
    public void OnDeselect(BaseEventData eventData) => _focused = false;

    internal static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    internal static TMP_Text Text(string name, Transform parent, string content, int size, Color color,
        Vector2 area, Vector2 position, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var label = Rect(name, parent, area, position).gameObject.AddComponent<TextMeshProUGUI>();
        label.text = content;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.enableWordWrapping = true;
        label.lineSpacing = 8;
        return label;
    }

    internal static TMP_Text LocalizedText(string name, Transform parent, string key, int size, Color color,
        Vector2 area, Vector2 position, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var label = CoopLocalizedText.Bind(Text(name, parent, string.Empty, size, color, area, position, alignment), key);
        label.enableAutoSizing = true;
        label.fontSizeMin = size * .8f;
        label.fontSizeMax = size;
        return label;
    }
}
