using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

public sealed class SharedHomeTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private GameObject _popup;
    private void OnDisable() => Hide();
    public void OnPointerExit(PointerEventData eventData) => Hide();
    private void Hide()
    {
        if (_popup) Destroy(_popup);
        _popup = null;
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        Hide();
        var canvas = GetComponentInParent<Canvas>();
        if (!canvas) return;
        _popup = new GameObject("SharedHomeExplanation", typeof(RectTransform), typeof(Image));
        _popup.transform.SetParent(canvas.transform, false);
        _popup.GetComponent<Image>().color = MModUI.GlassTheme.PanelBg;
        _popup.GetComponent<Image>().raycastTarget = false;
        var rect = _popup.GetComponent<RectTransform>();
        rect.pivot = new Vector2(1, 0);
        var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(_popup.transform, false);
        var text = label.GetComponent<TextMeshProUGUI>();
        text.fontSize = 15;
        var source = GetComponentInChildren<TextMeshProUGUI>();
        if (source) text.font = source.font;
        text.color = MModUI.ModernColors.TextPrimary;
        text.raycastTarget = false;
        CoopLocalizedText.Bind(text, "ui.sharedHome.desc");
        text.enableWordWrapping = true;
        var height = Mathf.Max(180, text.GetPreferredValues(text.text, 376, 0).y + 20);
        rect.sizeDelta = new Vector2(400, height);
        rect.position = new Vector3(Mathf.Clamp(eventData.position.x, 410, Screen.width - 10),
            Mathf.Clamp(eventData.position.y + 15, 10, Screen.height - height - 10), 0);
        var textRect = label.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12, 10);
        textRect.offsetMax = new Vector2(-12, -10);
    }
}
