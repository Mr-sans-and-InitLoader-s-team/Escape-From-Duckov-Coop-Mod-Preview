using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal sealed class TravelNotice : MonoBehaviour
{
    private static TravelNotice _current;
    private RectTransform _card;
    private CanvasGroup _group;
    private float _started;

    public static void Show(string message)
    {
        if (_current != null) Destroy(_current.gameObject);
        var root = new GameObject("TravelNotice", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _current = root.AddComponent<TravelNotice>();
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        card.transform.SetParent(root.transform, false);
        _current._card = card.GetComponent<RectTransform>();
        _current._card.anchorMin = _current._card.anchorMax = new Vector2(1, 1);
        _current._card.pivot = new Vector2(1, 1);
        _current._card.sizeDelta = new Vector2(420, 118);
        card.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.97f);
        _current._group = card.GetComponent<CanvasGroup>();
        _current._group.blocksRaycasts = false;
        var textObject = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(card.transform, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(20, 12); rect.offsetMax = new Vector2(-20, -12);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 21;
        text.color = new Color(1f, 0.85f, 0.68f);
        text.richText = false;
        text.enableWordWrapping = true;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.text = message;
        var height = Mathf.Clamp(text.GetPreferredValues(message, 380f, 0f).y + 24f, 118f, 260f);
        _current._card.sizeDelta = new Vector2(420, height);
        _current._started = Time.unscaledTime;
        _current.Update();
    }
    private void Update()
    {
        var age = Time.unscaledTime - _started;
        var enter = Mathf.Clamp01(age / 0.35f);
        var exit = Mathf.Clamp01((age - 5f) / 0.3f);
        var visible = (1f - Mathf.Pow(1f - enter, 3f)) * (1f - exit * exit);
        _card.anchoredPosition = new Vector2(Mathf.Lerp(444, -24, visible), -72);
        _group.alpha = visible;
        if (age >= 5.3f) Destroy(gameObject);
    }
}
