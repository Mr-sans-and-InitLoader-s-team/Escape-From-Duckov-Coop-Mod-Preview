using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal sealed class CoopModeSelectionUI : MonoBehaviour
{
    private Canvas _canvas;
    private MModUI _ui;
    private GameObject _root;
    private int _menu = -1;
    private bool _chosen;
    private bool _manual;
    private int _manualScene;
    private readonly List<(Light Light, int Mask)> _sceneLights = new();

    internal void Bind(Canvas canvas, MModUI ui) { _canvas = canvas; _ui = ui; }

    internal static bool CanChoose => NetService.Instance?.networkStarted != true &&
        NetService.Instance?.isConnecting != true && SteamLobbyManager.Instance?.IsInLobby != true &&
        SteamLobbyManager.Instance?.IsLobbyRequestPending != true && !SceneLoader.IsSceneLoading && !ConnectionRecovery.Pending;

    internal void Show()
    {
        if (!CanChoose || _root || !_canvas) return;
        _manual = true;
        _manualScene = SceneManager.GetActiveScene().handle;
        Open();
    }

    private void Update()
    {
        var menu = SharedHomeEntry.AtMainMenu;
        if (!CanChoose || _manual && _manualScene != SceneManager.GetActiveScene().handle || !menu && !_manual)
        {
            Close();
            _manual = false;
            if (!menu) _menu = -1;
            return;
        }
        if (_manual) return;
        var handle = SceneManager.GetActiveScene().handle;
        if (_menu != handle) { _menu = handle; _chosen = false; }
        if (!_chosen && !_root && _canvas) Open();
    }

    private void Open()
    {
        _ui.HideConnectionPanel();
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if ((light.cullingMask & CoopModePreview.LayerMask) == 0) continue;
            _sceneLights.Add((light, light.cullingMask));
            light.cullingMask &= ~CoopModePreview.LayerMask;
        }
        _root = CoopModeCard.Rect("CoopModeSelection", _canvas.transform, Vector2.zero, Vector2.zero).gameObject;
        var rect = (RectTransform)_root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        var backdrop = MModUI.ModernColors.BgDark;
        _root.AddComponent<Image>().color = new Color(backdrop.r, backdrop.g, backdrop.b, .96f);
        var presentation = CoopModePresentation.Load();
        var atmosphere = CoopModeCard.Rect("MenuBackdrop", rect, Vector2.zero, Vector2.zero);
        atmosphere.anchorMin = Vector2.zero;
        atmosphere.anchorMax = Vector2.one;
        atmosphere.gameObject.AddComponent<CoopModeBackdrop>().Initialize(presentation.menuBackdrop, backdrop);
        var content = CoopModeCard.Rect("Content", rect, new Vector2(1160, 980), Vector2.zero);
        var fit = content.gameObject.AddComponent<CoopModeSelectionFit>();
        fit.Init(rect);
        CoopModeCard.LocalizedText("Eyebrow", content, "ui.coopMode.eyebrow", 13,
            MModUI.ModernColors.TextTertiary, new Vector2(1100, 24), new Vector2(0, 450), TMPro.TextAlignmentOptions.Center).characterSpacing = 5;
        CoopModeCard.LocalizedText("Title", content, "ui.coopMode.title", 36,
            MModUI.ModernColors.TextPrimary, new Vector2(1100, 60), new Vector2(0, 400), TMPro.TextAlignmentOptions.Center);
        CoopModeCard.Create(content, false, new Vector2(-270, -20), Choose, presentation.independent);
        CoopModeCard.Create(content, true, new Vector2(270, -20), Choose, presentation.shared);
        CoopModeCard.LocalizedText("Hint", content, "ui.coopMode.hint", 16,
            MModUI.ModernColors.TextSecondary, new Vector2(1120, 48), new Vector2(0, -447), TMPro.TextAlignmentOptions.Center);
    }

    private void Choose(bool shared)
    {
        if (!CanChoose) return;
        SharedHome.SetEnabled(shared);
        _chosen = true;
        Close();
        _ui.ShowConnectionPanel();
        _manual = false;
    }

    private void Close()
    {
        if (_root) Destroy(_root);
        foreach (var source in _sceneLights) if (source.Light) source.Light.cullingMask = source.Mask;
        _sceneLights.Clear();
    }

    private void OnDestroy() => Close();
}

internal sealed class CoopModeSelectionFit : MonoBehaviour
{
    private RectTransform _parent;
    internal void Init(RectTransform parent) => _parent = parent;
    private void LateUpdate()
    {
        var size = _parent.rect.size;
        transform.localScale = Vector3.one * Mathf.Min(1, size.x / 1200, size.y / 1000);
    }
}
