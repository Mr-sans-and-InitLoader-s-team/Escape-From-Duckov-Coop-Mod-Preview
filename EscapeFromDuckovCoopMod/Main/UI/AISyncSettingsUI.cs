// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

/// <summary>
/// 联机 AI 同步参数的可视化调节界面（延续 MModUI 的玻璃拟态风格）。
/// </summary>
public sealed class AISyncSettingsUI : MonoBehaviour
{
    public static AISyncSettingsUI Instance;

    private Canvas _canvas;
    private GameObject _panel;
    private AISyncTuningSettings _workingSettings;
    private CoopGeneralSettings _workingGeneral;
    private LootTuningSettings _workingLootSettings;
    private AISyncTuningSettings _defaultSettings;
    private CoopGeneralSettings _defaultGeneral;
    private LootTuningSettings _defaultLootSettings;
    private bool _visible = false;

    private readonly Dictionary<string, GameObject> _pageRoots = new();
    private readonly Dictionary<string, LayoutElement> _pageRootLayouts = new();
    private readonly Dictionary<string, LayoutElement> _pageScrollLayouts = new();
    private readonly Dictionary<string, ScrollRect> _pageScrollRects = new();
    private readonly Dictionary<string, Transform> _pageContents = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private ScrollRect _pagesScroll;
    private LayoutElement _pagesWrapperLayout;
    private RectTransform _pagesContentRect;
    private string _activePageKey;

    private bool _initialized;

    private GameObject _tooltip;
    private RectTransform _tooltipRect;
    private TextMeshProUGUI _tooltipLabel;

    private TMP_InputField _searchInput;
    private readonly List<SearchEntry> _searchEntries = new();

    private DifficultyLevel _workingDifficultySelection = DifficultyLevel.Normal;
    private DifficultyCustomSettings _workingCustomDifficulty;
    private DifficultyCustomSettings _defaultCustomDifficulty;
    private readonly List<DifficultyFieldBinding> _difficultyFields = new();
    private readonly List<DifficultyBoolBinding> _difficultyBoolFields = new();
    private readonly Dictionary<DifficultyLevel, Button> _difficultyButtons = new();
    private readonly List<Toggle> _hostOnlyToggles = new();
    private readonly Dictionary<UIThemeMode, Button> _themeButtons = new();
    private bool _lastHostState;

    private void EnsureWorkingCopies()
    {
        _workingSettings ??= CoopAISettings.Active.Clone();
        _workingGeneral ??= CoopAISettings.ActiveGeneral.Clone();
        _workingLootSettings ??= CoopLootSettings.Active.Clone();
        _workingCustomDifficulty ??= DifficultyManager.GetCustomSettings();
        NormalizeGeneralTheme(_workingGeneral);
    }

    private static void NormalizeGeneralTheme(CoopGeneralSettings settings)
    {
        if (settings != null)
            settings.UiThemeMode = MModUITheme.NormalizeMode(settings.UiThemeMode);
    }

    private static bool IsHostActive() => ModBehaviourF.Instance != null && ModBehaviourF.Instance.IsServer;

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static Color SettingsPanelColor()
    {
        if (MModUITheme.UseLunarNewYearTheme)
            return new Color(0.74f, 0.10f, 0.15f, 0.97f);
        if (MModUITheme.IsSummerTheme)
            return new Color(0.66f, 0.89f, 1f, 0.96f);
        if (MModUITheme.IsDarkTheme)
            return new Color(0.07f, 0.09f, 0.12f, 0.96f);
        return new Color(0.90f, 0.93f, 0.96f, 0.94f);
    }

    private static Color SettingsSurfaceColor(float alpha = 0.90f)
    {
        if (MModUITheme.UseLunarNewYearTheme)
            return new Color(0.86f, 0.14f, 0.18f, alpha);
        if (MModUITheme.IsSummerTheme)
            return new Color(1f, 1f, 1f, Mathf.Min(alpha + 0.02f, 0.94f));
        if (MModUITheme.IsDarkTheme)
            return new Color(0.11f, 0.14f, 0.18f, Mathf.Min(alpha, 0.94f));
        return new Color(0.98f, 0.99f, 1f, alpha);
    }

    private static Color SettingsRowColor()
    {
        if (MModUITheme.UseLunarNewYearTheme)
            return new Color(0.66f, 0.08f, 0.12f, 0.18f);
        if (MModUITheme.IsSummerTheme)
            return new Color(0.05f, 0.58f, 0.90f, 0.12f);
        if (MModUITheme.IsDarkTheme)
            return new Color(0.18f, 0.22f, 0.28f, 0.22f);
        return new Color(0.88f, 0.92f, 0.97f, 0.18f);
    }

    private static Color SettingsAdjustColor(Color color, float factor)
    {
        color.r = Mathf.Clamp01(color.r * factor);
        color.g = Mathf.Clamp01(color.g * factor);
        color.b = Mathf.Clamp01(color.b * factor);
        return color;
    }

    private static Color SettingsBlendColor(Color from, Color to, float amount)
    {
        amount = Mathf.Clamp01(amount);
        return new Color(
            Mathf.Lerp(from.r, to.r, amount),
            Mathf.Lerp(from.g, to.g, amount),
            Mathf.Lerp(from.b, to.b, amount),
            Mathf.Lerp(from.a, to.a, amount));
    }

    private static void ApplySettingsSelectableFeedback(Selectable selectable, Color baseColor, bool animate = true)
    {
        if (selectable == null)
            return;

        var highlighted = SettingsBlendColor(SettingsAdjustColor(baseColor, 1.08f), MModUI.ModernColors.PrimaryHover, 0.18f);
        var pressed = SettingsBlendColor(SettingsAdjustColor(baseColor, 0.72f), MModUI.ModernColors.PrimaryActive, 0.30f);
        var colors = selectable.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        colors.selectedColor = highlighted;
        colors.disabledColor = WithAlpha(SettingsAdjustColor(baseColor, 0.82f), Mathf.Min(baseColor.a, 0.34f));
        colors.colorMultiplier = 1.06f;
        colors.fadeDuration = 0.06f;
        selectable.transition = Selectable.Transition.ColorTint;
        selectable.colors = colors;

        if (animate && selectable.gameObject.GetComponent<ButtonHoverAnimator>() == null)
        {
            selectable.gameObject.AddComponent<ButtonHoverAnimator>();
        }
    }

    public void Init()
    {
        // Leave the UI unbuilt until the player explicitly opens it to avoid
        // flashing the panel on load.
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void Update()
    {
        if (_tooltip != null && _tooltip.activeSelf)
        {
            UpdateTooltipPosition();
        }

        if (_initialized)
        {
            RefreshHostOnlyControls();
        }
    }

    public void Toggle()
    {
        EnsureBuilt();
        _visible = !_visible;
        SyncPanelVisibility();
    }

    public void Show()
    {
        EnsureBuilt();
        _visible = true;
        SyncPanelVisibility();
    }

    public void Hide()
    {
        _visible = false;
        SyncPanelVisibility();
    }

    private void ClearBuiltUiReferences()
    {
        _panel = null;
        _canvas = null;
        _tooltip = null;
        _tooltipRect = null;
        _tooltipLabel = null;
        _searchInput = null;
        _pagesScroll = null;
        _pagesWrapperLayout = null;
        _pagesContentRect = null;
        _activePageKey = null;

        _pageRoots.Clear();
        _pageRootLayouts.Clear();
        _pageScrollLayouts.Clear();
        _pageScrollRects.Clear();
        _pageContents.Clear();
        _navButtons.Clear();
        _searchEntries.Clear();
        _difficultyFields.Clear();
        _difficultyBoolFields.Clear();
        _difficultyButtons.Clear();
        _hostOnlyToggles.Clear();
        _themeButtons.Clear();
    }

    private void BuildUI(bool reloadSettings = true)
    {
        if (_initialized)
            return;

        ClearBuiltUiReferences();

        DontDestroyOnLoad(gameObject);
        Instance = this;
        var loaded = reloadSettings
            ? AISyncSettingsPersistence.LoadAndApply(CoopAISettings.Instance, CoopLootSettings.Instance)
            : null;

        _workingSettings = reloadSettings || _workingSettings == null
            ? (loaded?.AI ?? CoopAISettings.Active).Clone()
            : _workingSettings.CloneWithBounds();
        _workingGeneral = reloadSettings || _workingGeneral == null
            ? (loaded?.General ?? CoopAISettings.ActiveGeneral).Clone()
            : _workingGeneral.CloneWithBounds();
        _workingLootSettings = reloadSettings || _workingLootSettings == null
            ? (loaded?.Loot ?? CoopLootSettings.Active).Clone()
            : _workingLootSettings.CloneWithBounds();
        _defaultSettings = AISyncTuningSettings.Default();
        _defaultGeneral = CoopGeneralSettings.Default();
        _defaultLootSettings = LootTuningSettings.Default();
        NormalizeGeneralTheme(_workingGeneral);
        NormalizeGeneralTheme(_defaultGeneral);
        if (reloadSettings || _workingCustomDifficulty == null)
        {
            _workingDifficultySelection = loaded?.Difficulty?.Selected ?? DifficultyManager.Selected;
            _workingCustomDifficulty = (loaded?.Difficulty?.Custom ?? DifficultyManager.GetCustomSettings()).CloneAndClamp();
        }
        else
        {
            _workingCustomDifficulty = _workingCustomDifficulty.CloneAndClamp();
        }

        _defaultCustomDifficulty = DifficultyManager.GetCustomSettings().CloneAndClamp();

        _canvas = new GameObject("AISyncSettingsCanvas").AddComponent<Canvas>();
        _canvas.transform.SetParent(transform, false);
        _canvas.gameObject.layer = LayerMask.NameToLayer("UI");
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32500;
        var scaler = _canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        _canvas.gameObject.AddComponent<GraphicRaycaster>();

        var background = new GameObject("Background");
        background.transform.SetParent(_canvas.transform, false);
        var bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, MModUITheme.IsDarkTheme ? 0.62f : 0.50f);
        var bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        _panel = CreatePanel("AISyncSettingsPanel", _canvas.transform);
        var layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 18, 22);
        layout.spacing = 14;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var panelSizer = _panel.AddComponent<LayoutElement>();
        panelSizer.minWidth = 1500f;
        panelSizer.minHeight = 820f;

        CreateHeader(_panel.transform);

        CreateTooltipLayer();

        var body = new GameObject("Body");
        body.transform.SetParent(_panel.transform, false);
        var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.padding = new RectOffset(8, 8, 8, 8);
        bodyLayout.spacing = 18f;
        bodyLayout.childAlignment = TextAnchor.UpperLeft;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;
        var bodyImage = body.AddComponent<Image>();
        MModUI.StyleControlImage(bodyImage, SettingsSurfaceColor(MModUITheme.IsDarkTheme ? 0.72f : 0.76f));
        MModUI.AddControlChrome(body, WithAlpha(MModUI.ModernColors.InputBorder, 0.26f), WithAlpha(MModUI.ModernColors.Shadow, 0.10f), new Vector2(0f, -4f));

        var nav = CreateNavColumn(body.transform);
        var pagesWrapper = new GameObject("PagesWrapper");
        pagesWrapper.transform.SetParent(body.transform, false);
        _pagesWrapperLayout = pagesWrapper.AddComponent<LayoutElement>();
        _pagesWrapperLayout.flexibleWidth = 1;
        _pagesWrapperLayout.flexibleHeight = 1;
        _pagesWrapperLayout.minHeight = 720f;
        _pagesWrapperLayout.minWidth = 1180f;

        var pagesImage = pagesWrapper.AddComponent<Image>();
        MModUI.StyleControlImage(pagesImage, SettingsSurfaceColor(MModUITheme.IsDarkTheme ? 0.58f : 0.72f));
        MModUI.AddControlChrome(pagesWrapper, WithAlpha(MModUI.ModernColors.InputBorder, 0.18f), WithAlpha(MModUI.ModernColors.Shadow, 0.06f), new Vector2(0f, -2f));

        _pagesScroll = pagesWrapper.AddComponent<ScrollRect>();
        _pagesScroll.horizontal = false;
        _pagesScroll.vertical = true;
        _pagesScroll.movementType = ScrollRect.MovementType.Clamped;

        var pagesViewport = new GameObject("Viewport");
        pagesViewport.transform.SetParent(pagesWrapper.transform, false);
        var viewportRect = pagesViewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        pagesViewport.AddComponent<RectMask2D>();

        var pagesContainer = new GameObject("Pages");
        pagesContainer.transform.SetParent(pagesViewport.transform, false);
        var pagesRect = pagesContainer.AddComponent<RectTransform>();
        pagesRect.anchorMin = new Vector2(0, 1);
        pagesRect.anchorMax = new Vector2(1, 1);
        pagesRect.pivot = new Vector2(0.5f, 1f);
        pagesRect.offsetMin = new Vector2(0, 0);
        pagesRect.offsetMax = new Vector2(0, 0);

        _pagesContentRect = pagesRect;

        var pagesLayout = pagesContainer.AddComponent<VerticalLayoutGroup>();
        pagesLayout.padding = new RectOffset(0, 0, 0, 0);
        pagesLayout.childControlWidth = true;
        pagesLayout.childControlHeight = true;
        pagesLayout.childForceExpandWidth = true;
        pagesLayout.childForceExpandHeight = true;
        pagesLayout.spacing = 16f;

        var pagesFitter = pagesContainer.AddComponent<ContentSizeFitter>();
        pagesFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _pagesScroll.viewport = viewportRect;
        _pagesScroll.content = pagesRect;

        var networkContent = CreatePage("network", pagesContainer.transform);
        CreateNavButton(nav, "network", CoopLocalization.Get("ui.settings.nav.network"));

        var aiSyncContent = CreatePage("aiSync", pagesContainer.transform);
        CreateNavButton(nav, "aiSync", CoopLocalization.Get("ui.settings.nav.aiSync"));

        var aiDifficultyContent = CreatePage("aiDifficulty", pagesContainer.transform);
        CreateNavButton(nav, "aiDifficulty", CoopLocalization.Get("ui.settings.nav.aiDifficulty"));

        var lootContent = CreatePage("loot", pagesContainer.transform);
        CreateNavButton(nav, "loot", CoopLocalization.Get("ui.settings.nav.loot"));

        var network = CreateSection(networkContent,
            CoopLocalization.Get("ui.settings.section.network.title"),
            CoopLocalization.Get("ui.settings.section.network.subtitle"),
            true,
            "network");
        CreateFloatField(network, CoopLocalization.Get("ui.settings.broadcastInterval"), () => _workingGeneral.BroadcastInterval, v => _workingGeneral.BroadcastInterval = v, 1f, 30f, true, _defaultGeneral.BroadcastInterval, CoopLocalization.Get("ui.settings.broadcastInterval.desc"));
        CreateFloatField(network, CoopLocalization.Get("ui.settings.syncInterval"), () => _workingGeneral.SyncInterval, v => _workingGeneral.SyncInterval = v, 0.01f, 0.1f, true, _defaultGeneral.SyncInterval, CoopLocalization.Get("ui.settings.syncInterval.desc"));
        CreateFloatField(network, CoopLocalization.Get("ui.settings.projectileSyncMaxDistance"), () => _workingGeneral.ProjectileSyncMaxDistance, v => _workingGeneral.ProjectileSyncMaxDistance = v, 0f, 500f, true, _defaultGeneral.ProjectileSyncMaxDistance, CoopLocalization.Get("ui.settings.projectileSyncMaxDistance.desc"));
        CreateBoolField(network, CoopLocalization.Get("ui.settings.teleporterSpawnTogether"), () => _workingGeneral.TeleporterSpawnTogether, v => _workingGeneral.TeleporterSpawnTogether = v, true, _defaultGeneral.TeleporterSpawnTogether, CoopLocalization.Get("ui.settings.teleporterSpawnTogether.desc"));
        CreateBoolField(network, CoopLocalization.Get("ui.settings.friendlyFirePlayers"), () => _workingGeneral.FriendlyFirePlayers, v => _workingGeneral.FriendlyFirePlayers = v, true, _defaultGeneral.FriendlyFirePlayers, CoopLocalization.Get("ui.settings.friendlyFirePlayers.desc"));
        CreateThemeModeField(network, CoopLocalization.Get("ui.settings.themeMode"), () => _workingGeneral.UiThemeMode, v => _workingGeneral.UiThemeMode = v, _defaultGeneral.UiThemeMode, CoopLocalization.Get("ui.settings.themeMode.desc"));

        var distances = CreateSection(aiSyncContent,
            CoopLocalization.Get("ui.aiSettings.section.distance.title"),
            CoopLocalization.Get("ui.aiSettings.section.distance.subtitle"),
            false,
            "aiSync");
        CreateFloatField(distances, CoopLocalization.Get("ui.aiSettings.activationRadius"), () => _workingSettings.ActivationRadius, v => _workingSettings.ActivationRadius = v, 10, 400, false, _defaultSettings.ActivationRadius, CoopLocalization.Get("ui.aiSettings.activationRadius.desc"));
        CreateFloatField(distances, CoopLocalization.Get("ui.aiSettings.deactivationRadius"), () => _workingSettings.DeactivationRadius, v => _workingSettings.DeactivationRadius = v, 15, 450, false, _defaultSettings.DeactivationRadius, CoopLocalization.Get("ui.aiSettings.deactivationRadius.desc"));

        var pacing = CreateSection(aiSyncContent,
            CoopLocalization.Get("ui.aiSettings.section.pacing.title"),
            CoopLocalization.Get("ui.aiSettings.section.pacing.subtitle"),
            false,
            "aiSync");
        CreateFloatField(pacing, CoopLocalization.Get("ui.aiSettings.activationRetryInterval"), () => _workingSettings.ActivationRetryInterval, v => _workingSettings.ActivationRetryInterval = v, 0.1f, 5f, false, _defaultSettings.ActivationRetryInterval, CoopLocalization.Get("ui.aiSettings.activationRetryInterval.desc"));
        CreateFloatField(pacing, CoopLocalization.Get("ui.aiSettings.stateBroadcastInterval"), () => _workingSettings.StateBroadcastInterval, v => _workingSettings.StateBroadcastInterval = v, 0.02f, 1f, false, _defaultSettings.StateBroadcastInterval, CoopLocalization.Get("ui.aiSettings.stateBroadcastInterval.desc"));
        CreateFloatField(pacing, CoopLocalization.Get("ui.aiSettings.idleStateRecordInterval"), () => _workingSettings.IdleStateRecordInterval, v => _workingSettings.IdleStateRecordInterval = v, 0.05f, 2f, false, _defaultSettings.IdleStateRecordInterval, CoopLocalization.Get("ui.aiSettings.idleStateRecordInterval.desc"));
        CreateFloatField(pacing, CoopLocalization.Get("ui.aiSettings.healthBroadcastInterval"), () => _workingSettings.HealthBroadcastInterval, v => _workingSettings.HealthBroadcastInterval = v, 0.02f, 1f, false, _defaultSettings.HealthBroadcastInterval, CoopLocalization.Get("ui.aiSettings.healthBroadcastInterval.desc"));

        var precision = CreateSection(aiSyncContent,
            CoopLocalization.Get("ui.aiSettings.section.precision.title"),
            CoopLocalization.Get("ui.aiSettings.section.precision.subtitle"),
            false,
            "aiSync");
        CreateFloatField(precision, CoopLocalization.Get("ui.aiSettings.minPositionDelta"), () => _workingSettings.MinPositionDelta, v => _workingSettings.MinPositionDelta = v, 0.05f, 5f, false, _defaultSettings.MinPositionDelta, CoopLocalization.Get("ui.aiSettings.minPositionDelta.desc"));
        CreateFloatField(precision, CoopLocalization.Get("ui.aiSettings.minRotationDelta"), () => _workingSettings.MinRotationDelta, v => _workingSettings.MinRotationDelta = v, 0.5f, 30f, false, _defaultSettings.MinRotationDelta, CoopLocalization.Get("ui.aiSettings.minRotationDelta.desc"));
        CreateFloatField(precision, CoopLocalization.Get("ui.aiSettings.velocityLerp"), () => _workingSettings.VelocityLerp, v => _workingSettings.VelocityLerp = v, 1f, 30f, false, _defaultSettings.VelocityLerp, CoopLocalization.Get("ui.aiSettings.velocityLerp.desc"));

        var snapshot = CreateSection(aiSyncContent,
            CoopLocalization.Get("ui.aiSettings.section.snapshot.title"),
            CoopLocalization.Get("ui.aiSettings.section.snapshot.subtitle"),
            true,
            "aiSync");
        CreateFloatField(snapshot, CoopLocalization.Get("ui.aiSettings.snapshotRefreshInterval"), () => _workingSettings.SnapshotRefreshInterval, v => _workingSettings.SnapshotRefreshInterval = v, 1f, 60f, true, _defaultSettings.SnapshotRefreshInterval, CoopLocalization.Get("ui.aiSettings.snapshotRefreshInterval.desc"));
        CreateFloatField(snapshot, CoopLocalization.Get("ui.aiSettings.snapshotRequestTimeout"), () => _workingSettings.SnapshotRequestTimeout, v => _workingSettings.SnapshotRequestTimeout = v, 0.5f, 10f, true, _defaultSettings.SnapshotRequestTimeout, CoopLocalization.Get("ui.aiSettings.snapshotRequestTimeout.desc"));
        CreateFloatField(snapshot, CoopLocalization.Get("ui.aiSettings.snapshotRecoveryCooldown"), () => _workingSettings.SnapshotRecoveryCooldown, v => _workingSettings.SnapshotRecoveryCooldown = v, 0.25f, 10f, true, _defaultSettings.SnapshotRecoveryCooldown, CoopLocalization.Get("ui.aiSettings.snapshotRecoveryCooldown.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.snapshotChunkSize"), () => _workingSettings.SnapshotChunkSize, v => _workingSettings.SnapshotChunkSize = v, 12, 256, true, _defaultSettings.SnapshotChunkSize, CoopLocalization.Get("ui.aiSettings.snapshotChunkSize.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.maxStoredBuffs"), () => _workingSettings.MaxStoredBuffs, v => _workingSettings.MaxStoredBuffs = v, 8, 256, true, _defaultSettings.MaxStoredBuffs, CoopLocalization.Get("ui.aiSettings.maxStoredBuffs.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.maxSnapshotAppliesPerFrame"), () => _workingSettings.MaxSnapshotAppliesPerFrame, v => _workingSettings.MaxSnapshotAppliesPerFrame = v, 1, 128, true, _defaultSettings.MaxSnapshotAppliesPerFrame, CoopLocalization.Get("ui.aiSettings.maxSnapshotAppliesPerFrame.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.maxStateUpdatesPerFrame"), () => _workingSettings.MaxStateUpdatesPerFrame, v => _workingSettings.MaxStateUpdatesPerFrame = v, 1, 256, true, _defaultSettings.MaxStateUpdatesPerFrame, CoopLocalization.Get("ui.aiSettings.maxStateUpdatesPerFrame.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.maxClientEntryChecksPerFrame"), () => _workingSettings.MaxClientEntryChecksPerFrame, v => _workingSettings.MaxClientEntryChecksPerFrame = v, 16, 1024, true, _defaultSettings.MaxClientEntryChecksPerFrame, CoopLocalization.Get("ui.aiSettings.maxClientEntryChecksPerFrame.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.maxPendingSnapshotQueue"), () => _workingSettings.MaxPendingSnapshotQueue, v => _workingSettings.MaxPendingSnapshotQueue = v, 64, 2048, true, _defaultSettings.MaxPendingSnapshotQueue, CoopLocalization.Get("ui.aiSettings.maxPendingSnapshotQueue.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.maxPendingStateQueue"), () => _workingSettings.MaxPendingStateQueue, v => _workingSettings.MaxPendingStateQueue = v, 128, 4096, true, _defaultSettings.MaxPendingStateQueue, CoopLocalization.Get("ui.aiSettings.maxPendingStateQueue.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.snapshotDropResyncThreshold"), () => _workingSettings.SnapshotDropResyncThreshold, v => _workingSettings.SnapshotDropResyncThreshold = v, 8, 256, true, _defaultSettings.SnapshotDropResyncThreshold, CoopLocalization.Get("ui.aiSettings.snapshotDropResyncThreshold.desc"));
        CreateIntField(snapshot, CoopLocalization.Get("ui.aiSettings.stateDropResyncThreshold"), () => _workingSettings.StateDropResyncThreshold, v => _workingSettings.StateDropResyncThreshold = v, 16, 512, true, _defaultSettings.StateDropResyncThreshold, CoopLocalization.Get("ui.aiSettings.stateDropResyncThreshold.desc"));

        var serverOnly = CreateSection(aiSyncContent,
            CoopLocalization.Get("ui.aiSettings.section.hostOnly.title"),
            CoopLocalization.Get("ui.aiSettings.section.hostOnly.subtitle"),
            true,
            "aiSync");
        CreateFloatField(serverOnly, CoopLocalization.Get("ui.aiSettings.serverControllerRescanInterval"), () => _workingSettings.ServerControllerRescanInterval, v => _workingSettings.ServerControllerRescanInterval = v, 1f, 60f, true, _defaultSettings.ServerControllerRescanInterval, CoopLocalization.Get("ui.aiSettings.serverControllerRescanInterval.desc"));
        CreateFloatField(serverOnly, CoopLocalization.Get("ui.aiSettings.serverSnapshotBroadcastInterval"), () => _workingSettings.ServerSnapshotBroadcastInterval, v => _workingSettings.ServerSnapshotBroadcastInterval = v, 1f, 60f, true, _defaultSettings.ServerSnapshotBroadcastInterval, CoopLocalization.Get("ui.aiSettings.serverSnapshotBroadcastInterval.desc"));
        CreateFloatField(serverOnly, CoopLocalization.Get("ui.aiSettings.serverSnapshotRetryInterval"), () => _workingSettings.ServerSnapshotRetryInterval, v => _workingSettings.ServerSnapshotRetryInterval = v, 0.5f, 30f, true, _defaultSettings.ServerSnapshotRetryInterval, CoopLocalization.Get("ui.aiSettings.serverSnapshotRetryInterval.desc"));

        var loot = CreateSection(lootContent,
            CoopLocalization.Get("ui.lootSettings.title"),
            CoopLocalization.Get("ui.lootSettings.hostOnly"),
            true,
            "loot");
        CreateFloatField(loot, CoopLocalization.Get("ui.lootSettings.spawnChanceMultiplier"), () => _workingLootSettings.SpawnChanceMultiplier, v => _workingLootSettings.SpawnChanceMultiplier = v, 0f, 5f, true, _defaultLootSettings.SpawnChanceMultiplier);
        CreateFloatField(loot, CoopLocalization.Get("ui.lootSettings.itemCountMultiplier"), () => _workingLootSettings.ItemCountMultiplier, v => _workingLootSettings.ItemCountMultiplier = v, 0.1f, 50f, true, _defaultLootSettings.ItemCountMultiplier);
        CreateFloatField(loot, CoopLocalization.Get("ui.lootSettings.globalWeight"), () => _workingLootSettings.GlobalWeightMultiplier, v => _workingLootSettings.GlobalWeightMultiplier = v, 0f, 50f, true, _defaultLootSettings.GlobalWeightMultiplier);
        CreateFloatField(loot, CoopLocalization.Get("ui.lootSettings.qualityBias"), () => _workingLootSettings.QualityBias, v => _workingLootSettings.QualityBias = v, -1f, 50f, true, _defaultLootSettings.QualityBias);

        BuildDifficultyPage(aiDifficultyContent);

        ShowPageInternal("network");
    }

    private void SyncPanelVisibility()
    {
        if (_panel != null)
            _panel.SetActive(_visible);
        if (_canvas != null)
            _canvas.enabled = _visible;

        if (!_visible)
        {
            HideTooltip();
        }
    }

    private void ApplyChanges()
    {
        EnsureWorkingCopies();
        var previousTheme = MModUITheme.CurrentMode;

        _workingSettings = _workingSettings.CloneWithBounds();
        CoopAISettings.Instance?.Apply(_workingSettings);
        _workingGeneral = _workingGeneral.CloneWithBounds();
        NormalizeGeneralTheme(_workingGeneral);
        if (CoopAISettings.Instance != null)
        {
            CoopAISettings.Instance.ApplyGeneral(_workingGeneral);
        }
        else
        {
            MModUITheme.SetThemeMode(_workingGeneral.UiThemeMode);
        }

        _workingLootSettings = (_workingLootSettings ?? LootTuningSettings.Default()).CloneWithBounds();
        CoopLootSettings.Instance?.Apply(_workingLootSettings);

        var customDifficulty = (_workingCustomDifficulty ?? DifficultyManager.GetCustomSettings()).CloneAndClamp();
        DifficultyManager.SetCustomSettings(customDifficulty);
        DifficultyManager.SetDifficulty(_workingDifficultySelection);

        if (_initialized && previousTheme != MModUITheme.CurrentMode)
        {
            RebuildForCurrentTheme();
        }
    }

    private void RebuildForCurrentTheme()
    {
        var wasVisible = _visible;
        var activePage = string.IsNullOrEmpty(_activePageKey) ? "network" : _activePageKey;
        var searchText = _searchInput != null ? _searchInput.text : string.Empty;

        HideTooltip();

        if (_canvas != null)
        {
            var oldCanvas = _canvas.gameObject;
            oldCanvas.SetActive(false);
            Destroy(oldCanvas);
        }

        _initialized = false;
        BuildUI(false);
        _initialized = true;
        _visible = wasVisible;
        SyncPanelVisibility();

        if (!string.IsNullOrEmpty(activePage) && _pageRoots.ContainsKey(activePage))
        {
            ShowPageInternal(activePage);
        }

        if (_searchInput != null && !string.IsNullOrEmpty(searchText))
        {
            _searchInput.SetTextWithoutNotify(searchText);
            ApplySearchFilter(searchText);
        }
    }

    private void ApplyChangesFromUI()
    {
        ApplyChanges();
    }

    private void SaveSettingsToDisk()
    {
        ApplyChanges();

        AISyncSettingsPersistence.Save(
            _workingSettings,
            _workingGeneral,
            _workingLootSettings,
            _workingDifficultySelection,
            _workingCustomDifficulty ?? DifficultyManager.GetCustomSettings());
    }

    private void CreateHeader(Transform parent)
    {
        var header = new GameObject("Header");
        header.transform.SetParent(parent, false);
        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 14, 10, 10);
        layout.spacing = 12;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var headerSizer = header.AddComponent<LayoutElement>();
        headerSizer.minHeight = 66f;
        headerSizer.preferredHeight = 66f;

        var title = CreateText("Title", header.transform, CoopLocalization.Get("ui.settings.title"), 26, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        var titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.preferredWidth = 300f;
        titleLayout.flexibleWidth = 0f;
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(header.transform, false);
        var spacerEl = spacer.AddComponent<LayoutElement>();
        spacerEl.flexibleWidth = 1;

        _searchInput = CreateSearchInput(header.transform);

        CreateApplyButton(header.transform);
        CreateSaveButton(header.transform);
        CreateCloseButton(header.transform);

        var image = header.AddComponent<Image>();
        MModUI.StyleControlImage(image, SettingsSurfaceColor(MModUITheme.IsDarkTheme ? 0.82f : 0.84f));
        MModUI.AddControlChrome(header, WithAlpha(MModUI.ModernColors.InputBorder, 0.28f), WithAlpha(MModUI.ModernColors.Shadow, 0.10f), new Vector2(0f, -3f));
    }

    private void CreateCloseButton(Transform parent)
    {
        var button = new GameObject("CloseButton");
        button.transform.SetParent(parent, false);
        var layout = button.AddComponent<LayoutElement>();
        layout.minHeight = 38f;
        layout.preferredHeight = 38f;
        layout.preferredWidth = 42f;

        var image = button.AddComponent<Image>();
        MModUI.AddControlChrome(button, WithAlpha(MModUI.ModernColors.Error, 0.32f), WithAlpha(MModUI.ModernColors.Shadow, 0.12f), new Vector2(0f, -2f));

        var btn = button.AddComponent<Button>();
        MModUI.ConfigureCloseButton(btn, image, button.transform, 38f, MModUI.ModernColors.Error);
        btn.onClick.AddListener(Hide);
    }

    private void CreateSaveButton(Transform parent)
    {
        var button = new GameObject("SaveButton");
        button.transform.SetParent(parent, false);
        var layout = button.AddComponent<LayoutElement>();
        layout.minHeight = 38f;
        layout.preferredHeight = 38f;
        layout.preferredWidth = 170f;

        var image = button.AddComponent<Image>();
        MModUI.StyleControlImage(image, WithAlpha(MModUI.ModernColors.Success, MModUITheme.IsDarkTheme ? 0.82f : 0.74f));
        MModUI.AddControlChrome(button, WithAlpha(MModUI.ModernColors.Success, 0.42f), MModUI.ModernColors.Shadow, new Vector2(0f, -3f));

        var text = CreateText("Label", button.transform, CoopLocalization.Get("ui.settings.saveGlobal"), 15, MModUI.ModernColors.PrimaryText, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8, 6);
        rect.offsetMax = new Vector2(-8, -6);

        var btn = button.AddComponent<Button>();
        btn.targetGraphic = image;
        ApplySettingsSelectableFeedback(btn, WithAlpha(MModUI.ModernColors.Success, MModUITheme.IsDarkTheme ? 0.82f : 0.74f));
        btn.onClick.AddListener(SaveSettingsToDisk);
    }

    private void CreateApplyButton(Transform parent)
    {
        var button = new GameObject("ApplyButton");
        button.transform.SetParent(parent, false);
        var layout = button.AddComponent<LayoutElement>();
        layout.minHeight = 38f;
        layout.preferredHeight = 38f;
        layout.preferredWidth = 150f;

        var image = button.AddComponent<Image>();
        MModUI.StyleControlImage(image, MModUI.ModernColors.Primary);
        MModUI.AddControlChrome(button, WithAlpha(MModUI.ModernColors.PrimaryHover, 0.48f), MModUI.ModernColors.Shadow, new Vector2(0f, -3f));

        var text = CreateText("Label", button.transform, CoopLocalization.Get("ui.settings.applyChanges"), 15, MModUI.ModernColors.PrimaryText, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8, 6);
        rect.offsetMax = new Vector2(-8, -6);

        var btn = button.AddComponent<Button>();
        btn.targetGraphic = image;
        ApplySettingsSelectableFeedback(btn, MModUI.ModernColors.Primary);
        btn.onClick.AddListener(ApplyChangesFromUI);
    }

    private Transform CreateScrollContent(Transform parent, string pageKey)
    {
        var scrollObj = new GameObject("Scroll");
        scrollObj.transform.SetParent(parent, false);
        var scrollRect = scrollObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        var scrollImage = scrollObj.AddComponent<Image>();
        scrollImage.color = new Color(1f, 1f, 1f, 0.02f);
        var scrollLayout = scrollObj.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1;
        scrollLayout.minHeight = 720f;
        _pageScrollLayouts[pageKey] = scrollLayout;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollObj.transform, false);
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewport.AddComponent<RectMask2D>();
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.offsetMin = new Vector2(0, 0);
        contentRect.offsetMax = new Vector2(0, 0);

        var vLayout = content.AddComponent<VerticalLayoutGroup>();
        vLayout.padding = new RectOffset(0, 0, 0, 0);
        vLayout.spacing = 12;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandHeight = false;

        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        _pageScrollRects[pageKey] = scrollRect;

        return content.transform;
    }

    private Transform CreateNavColumn(Transform parent)
    {
        var nav = new GameObject("Nav");
        nav.transform.SetParent(parent, false);
        var layout = nav.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var background = nav.AddComponent<Image>();
        MModUI.StyleControlImage(background, SettingsSurfaceColor(MModUITheme.IsDarkTheme ? 0.70f : 0.82f));
        MModUI.AddControlChrome(nav, WithAlpha(MModUI.ModernColors.InputBorder, 0.22f), WithAlpha(MModUI.ModernColors.Shadow, 0.08f), new Vector2(0f, -3f));

        var navSizer = nav.AddComponent<LayoutElement>();
        navSizer.preferredWidth = 200f;
        navSizer.minHeight = 720f;
        navSizer.flexibleHeight = 1f;

        var title = CreateText("NavTitle", nav.transform, CoopLocalization.Get("ui.settings.title"), 17, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;

        return nav.transform;
    }

    private Transform CreatePage(string key, Transform parent)
    {
        var pageRoot = new GameObject(key + "Page");
        pageRoot.transform.SetParent(parent, false);
        var pageLayout = pageRoot.AddComponent<VerticalLayoutGroup>();
        pageLayout.padding = new RectOffset(0, 0, 0, 0);
        pageLayout.childControlWidth = true;
        pageLayout.childControlHeight = true;
        pageLayout.childForceExpandWidth = true;
        pageLayout.childForceExpandHeight = true;
        var size = pageRoot.AddComponent<LayoutElement>();
        size.minHeight = 720f;
        size.flexibleHeight = 1;
        size.flexibleWidth = 1;

        var content = CreateScrollContent(pageRoot.transform, key);

        _pageRoots[key] = pageRoot;
        _pageRootLayouts[key] = size;
        _pageContents[key] = content;
        pageRoot.SetActive(false);

        return content;
    }

    private GameObject CreatePanel(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1500f, 860f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        var image = go.AddComponent<Image>();
        MModUI.StyleControlImage(image, SettingsPanelColor());

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = MModUI.ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -6f);

        var outline = go.AddComponent<Outline>();
        outline.effectColor = WithAlpha(MModUI.ModernColors.InputBorder, 0.34f);
        outline.effectDistance = new Vector2(2f, -2f);

        var canvasGroup = go.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;

        return go;
    }

    private Transform CreateSection(Transform parent, string title, string subtitle, bool hostOnly = false, string pageKey = null)
    {
        var card = new GameObject(title + "Card");
        card.transform.SetParent(parent, false);
        var image = card.AddComponent<Image>();
        MModUI.StyleControlImage(image, SettingsSurfaceColor(MModUITheme.IsDarkTheme ? 0.78f : 0.88f));
        MModUI.AddControlChrome(card, WithAlpha(MModUI.ModernColors.InputBorder, 0.20f), WithAlpha(MModUI.ModernColors.Shadow, 0.08f), new Vector2(0f, -3f));
        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 16);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var size = card.AddComponent<LayoutElement>();
        size.minHeight = 140f;

        var meta = card.AddComponent<SectionMeta>();
        meta.PageKey = pageKey;

        var header = new GameObject("Header");
        header.transform.SetParent(card.transform, false);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 10;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandWidth = false;

        var titleText = CreateText("Title", header.transform, title, 18, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        var titleLayout = titleText.gameObject.AddComponent<LayoutElement>();
        titleLayout.preferredWidth = 260f;
        titleLayout.flexibleWidth = 0f;
        if (hostOnly)
        {
            CreateBadge(header.transform, CoopLocalization.Get("ui.aiSettings.badge.hostOnly"));
        }

        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(header.transform, false);
        var spacerEl = spacer.AddComponent<LayoutElement>();
        spacerEl.flexibleWidth = 1;

        CreateText("Subtitle", card.transform, subtitle, 13, MModUI.ModernColors.TextSecondary, FontStyles.Italic);

        return card.transform;
    }

    internal void ShowPageExternal(string key)
    {
        EnsureBuilt();
        _visible = true;
        SyncPanelVisibility();
        ShowPageInternal(key);
    }

    private void ShowPageInternal(string key)
    {
        _activePageKey = key;
        foreach (var kvp in _pageRoots)
        {
            kvp.Value.SetActive(kvp.Key == key);
        }

        foreach (var kvp in _navButtons)
        {
            if (kvp.Value == null) continue;
            var selected = kvp.Key == key;
            var img = kvp.Value.GetComponent<Image>();
            if (img != null)
            {
                MModUI.StyleControlImage(img, selected ? MModUI.ModernColors.Primary : SettingsSurfaceColor(MModUITheme.IsDarkTheme ? 0.54f : 0.62f));
            }

            var label = kvp.Value.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = selected ? MModUI.ModernColors.PrimaryText : MModUI.ModernColors.TextPrimary;
            }

            ApplySettingsSelectableFeedback(kvp.Value, selected ? MModUI.ModernColors.Primary : SettingsSurfaceColor(MModUITheme.IsDarkTheme ? 0.54f : 0.62f));
        }

        ApplySearchFilter(_searchInput != null ? _searchInput.text : string.Empty);
    }

    private void CreateNavButton(Transform parent, string key, string label)
    {
        var go = new GameObject(key + "NavButton");
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 44f;
        layout.preferredHeight = 48f;
        layout.minWidth = 180f;

        var image = go.AddComponent<Image>();
        MModUI.StyleControlImage(image, MModUI.GlassTheme.InputBg);
        MModUI.AddControlChrome(go, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));

        var text = CreateText("Label", go.transform, label, 15, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6, 6);
        textRect.offsetMax = new Vector2(-6, -6);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ApplySettingsSelectableFeedback(button, MModUI.GlassTheme.InputBg);
        button.onClick.AddListener(() => ShowPageInternal(key));

        _navButtons[key] = button;
    }

    private void CreateBadge(Transform parent, string text)
    {
        var badge = new GameObject("Badge");
        badge.transform.SetParent(parent, false);
        var layout = badge.AddComponent<LayoutElement>();
        layout.minWidth = text.Length <= 2 ? 42 : 54;
        layout.preferredWidth = text.Length <= 2 ? 42 : 54;
        layout.preferredHeight = 24;
        layout.flexibleWidth = 0;
        var image = badge.AddComponent<Image>();
        MModUI.StyleToggleBoxImage(image, WithAlpha(MModUI.ModernColors.Error, MModUITheme.IsDarkTheme ? 0.32f : 0.18f));
        image.raycastTarget = false;

        var badgeTextColor = MModUITheme.IsDarkTheme || MModUITheme.UseLunarNewYearTheme
            ? Color.white
            : MModUI.ModernColors.Error;
        var t = CreateText("BadgeText", badge.transform, text, 11, badgeTextColor, FontStyles.Bold);
        t.alignment = TextAlignmentOptions.Center;
        var textRect = t.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var textLayout = t.gameObject.AddComponent<LayoutElement>();
        textLayout.ignoreLayout = true;
    }

    private void CreateFloatField(Transform parent, string label, System.Func<float> getter, System.Action<float> setter, float min, float max, bool hostOnly = false, float? defaultValue = null, string tooltip = null)
    {
        var row = CreateFieldRow(parent, label, hostOnly, tooltip);
        var input = CreateInput(row, getter().ToString("0.###"));
        input.onEndEdit.AddListener(value =>
        {
            if (!float.TryParse(value, out var parsed))
            {
                input.text = getter().ToString("0.###");
                return;
            }

            parsed = Mathf.Clamp(parsed, min, max);
            setter(parsed);
            ApplyChanges();
            input.text = getter().ToString("0.###");
        });

        CreateResetButton(row, () =>
        {
            var resetVal = defaultValue ?? getter();
            setter(resetVal);
            input.text = resetVal.ToString("0.###");
            ApplyChanges();
        });
    }

    private void CreateBoolField(Transform parent, string label, System.Func<bool> getter, System.Action<bool> setter, bool hostOnly = false, bool? defaultValue = null, string tooltip = null)
    {
        var row = CreateFieldRow(parent, label, hostOnly, tooltip);

        var toggleObj = new GameObject(label + "Toggle");
        toggleObj.transform.SetParent(row, false);
        var toggleLayout = toggleObj.AddComponent<LayoutElement>();
        toggleLayout.minWidth = 24f;
        toggleLayout.preferredWidth = 24f;
        toggleLayout.flexibleWidth = 0f;
        toggleLayout.minHeight = 24f;
        toggleLayout.preferredHeight = 24f;
        toggleLayout.flexibleHeight = 0f;

        var toggleRect = toggleObj.GetComponent<RectTransform>();
        toggleRect.sizeDelta = new Vector2(24f, 24f);

        var backgroundObj = new GameObject("Background");
        backgroundObj.transform.SetParent(toggleObj.transform, false);
        var toggleBg = backgroundObj.AddComponent<Image>();
        MModUI.StyleToggleBoxImage(toggleBg, MModUI.GlassTheme.InputBg);
        toggleBg.raycastTarget = true;
        MModUI.AddControlChrome(backgroundObj, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));
        var bgRect = backgroundObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        var checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(backgroundObj.transform, false);
        var checkRect = checkmark.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.25f, 0.25f);
        checkRect.anchorMax = new Vector2(0.75f, 0.75f);
        checkRect.sizeDelta = Vector2.zero;
        var checkImage = checkmark.AddComponent<Image>();
        MModUI.StyleToggleBoxImage(checkImage, MModUI.ModernColors.Primary);
        checkImage.type = Image.Type.Simple;
        checkImage.preserveAspect = false;
        checkImage.color = MModUI.ModernColors.Primary;

        var toggle = toggleObj.AddComponent<Toggle>();
        toggle.targetGraphic = toggleBg;
        toggle.graphic = checkImage;

        var toggleColors = toggle.colors;
        toggleColors.normalColor = MModUI.GlassTheme.ButtonBg;
        toggleColors.highlightedColor = MModUI.GlassTheme.ButtonHover;
        toggleColors.pressedColor = MModUI.GlassTheme.ButtonActive;
        toggleColors.selectedColor = MModUI.GlassTheme.ButtonBg;
        toggleColors.disabledColor = new Color(1f, 1f, 1f, 0.08f);
        toggle.colors = toggleColors;
        ApplySettingsSelectableFeedback(toggle, MModUI.GlassTheme.InputBg);

        toggle.SetIsOnWithoutNotify(getter());
        toggle.interactable = !hostOnly || IsHostActive();

        if (hostOnly)
        {
            _hostOnlyToggles.Add(toggle);
        }

        toggle.onValueChanged.AddListener(v =>
        {
            if (hostOnly && (ModBehaviourF.Instance == null || !ModBehaviourF.Instance.IsServer))
            {
                toggle.SetIsOnWithoutNotify(getter());
                return;
            }

            setter(v);
            ApplyChanges();
        });

        CreateResetButton(row, () =>
        {
            var resetVal = defaultValue ?? getter();
            setter(resetVal);
            toggle.SetIsOnWithoutNotify(resetVal);
            ApplyChanges();
        });
    }

    private void CreateIntField(Transform parent, string label, System.Func<int> getter, System.Action<int> setter, int min, int max, bool hostOnly = false, int? defaultValue = null, string tooltip = null)
    {
        var row = CreateFieldRow(parent, label, hostOnly, tooltip);
        var input = CreateInput(row, getter().ToString());
        input.onEndEdit.AddListener(value =>
        {
            if (!int.TryParse(value, out var parsed))
            {
                input.text = getter().ToString();
                return;
            }

            parsed = Mathf.Clamp(parsed, min, max);
            setter(parsed);
            ApplyChanges();
            input.text = getter().ToString();
        });

        CreateResetButton(row, () =>
        {
            var resetVal = defaultValue ?? getter();
            setter(resetVal);
            input.text = resetVal.ToString();
            ApplyChanges();
        });
    }

    private void CreateDifficultySlider(
        Transform parent,
        string label,
        System.Func<DifficultySettings, float> getter,
        System.Action<float> setter,
        float min,
        float max,
        float defaultValue,
        string tooltip = null,
        string format = "0.##")
    {
        var row = CreateFieldRow(parent, label, false, tooltip);

        var sliderObj = new GameObject(label + "Slider");
        sliderObj.transform.SetParent(row, false);
        var sliderLayout = sliderObj.AddComponent<LayoutElement>();
        sliderLayout.preferredWidth = 170f;
        sliderLayout.minWidth = 140f;
        sliderLayout.flexibleWidth = 0;
        sliderLayout.minHeight = 24f;
        sliderLayout.preferredHeight = 24f;
        sliderLayout.flexibleHeight = 0f;

        var sliderBg = sliderObj.AddComponent<Image>();
        MModUI.StyleControlImage(sliderBg, WithAlpha(MModUI.GlassTheme.InputBg, MModUITheme.IsDarkTheme ? 0.45f : 0.50f));

        var sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.sizeDelta = new Vector2(170, 24);

        var slider = sliderObj.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;

        var fillArea = new GameObject("FillArea");
        fillArea.transform.SetParent(sliderObj.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1, 0.5f);
        fillAreaRect.offsetMin = new Vector2(8, -3);
        fillAreaRect.offsetMax = new Vector2(-8, 3);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = MModUI.ModernColors.Primary;
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(1, 1);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        slider.fillRect = fillRect;

        var handleArea = new GameObject("HandleArea");
        handleArea.transform.SetParent(sliderObj.transform, false);
        var handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0, 0);
        handleAreaRect.anchorMax = new Vector2(1, 1);
        handleAreaRect.offsetMin = new Vector2(8, 0);
        handleAreaRect.offsetMax = new Vector2(-8, 0);

        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var handleImage = handle.AddComponent<Image>();
        handleImage.color = Color.white;
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(12, 12);
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        ApplySettingsSelectableFeedback(slider, Color.white, false);

        var input = CreateInput(row, getter(GetSelectedDifficultySettings()).ToString(format));
        input.contentType = TMP_InputField.ContentType.DecimalNumber;
        var inputLayout = input.GetComponent<LayoutElement>();
        if (inputLayout != null)
        {
            inputLayout.preferredWidth = 110f;
        }

        var valueText = CreateText("Value", row, getter(GetSelectedDifficultySettings()).ToString(format), 14, MModUI.ModernColors.TextSecondary);
        var valueLayout = valueText.GetComponent<RectTransform>().gameObject.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 70f;
        valueText.alignment = TextAlignmentOptions.Right;

        var resetButton = CreateResetButton(row, () =>
        {
            setter(defaultValue);
            RefreshDifficultyFields();
            ApplyChanges();
        });

        slider.onValueChanged.AddListener(v =>
        {
            if (_workingDifficultySelection != DifficultyLevel.Custom)
            {
                RefreshDifficultyFields();
                return;
            }

            var clamped = Mathf.Clamp(v, min, max);
            setter(clamped);
            input.SetTextWithoutNotify(clamped.ToString(format));
            RefreshDifficultyFields();
            ApplyChanges();
        });

        input.onEndEdit.AddListener(value =>
        {
            if (_workingDifficultySelection != DifficultyLevel.Custom)
            {
                RefreshDifficultyFields();
                return;
            }

            if (!float.TryParse(value, out var parsed))
            {
                RefreshDifficultyFields();
                return;
            }

            var clamped = Mathf.Clamp(parsed, min, max);
            setter(clamped);
            RefreshDifficultyFields();
            ApplyChanges();
        });

        _difficultyFields.Add(new DifficultyFieldBinding
        {
            Slider = slider,
            ValueText = valueText,
            Input = input,
            Getter = getter,
            Setter = setter,
            Min = min,
            Max = max,
            Format = format,
            ResetButton = resetButton,
            DefaultValue = defaultValue
        });
    }

    private void CreateDifficultyToggle(
        Transform parent,
        string label,
        System.Func<DifficultySettings, bool> getter,
        System.Action<bool> setter,
        string tooltip = null)
    {
        var row = CreateFieldRow(parent, label, false, tooltip);

        var toggleObj = new GameObject(label + "Toggle");
        toggleObj.transform.SetParent(row, false);
        var toggleLayout = toggleObj.AddComponent<LayoutElement>();
        toggleLayout.minWidth = 24f;
        toggleLayout.preferredWidth = 24f;
        toggleLayout.flexibleWidth = 0f;
        toggleLayout.minHeight = 24f;
        toggleLayout.preferredHeight = 24f;
        toggleLayout.flexibleHeight = 0f;

        var toggleRect = toggleObj.GetComponent<RectTransform>();
        toggleRect.sizeDelta = new Vector2(24f, 24f);

        var backgroundObj = new GameObject("Background");
        backgroundObj.transform.SetParent(toggleObj.transform, false);
        var toggleBg = backgroundObj.AddComponent<Image>();
        MModUI.StyleToggleBoxImage(toggleBg, MModUI.GlassTheme.InputBg);
        toggleBg.raycastTarget = true;
        MModUI.AddControlChrome(backgroundObj, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));
        var bgRect = backgroundObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        var checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(backgroundObj.transform, false);
        var checkRect = checkmark.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.25f, 0.25f);
        checkRect.anchorMax = new Vector2(0.75f, 0.75f);
        checkRect.sizeDelta = Vector2.zero;
        var checkImage = checkmark.AddComponent<Image>();
        MModUI.StyleToggleBoxImage(checkImage, MModUI.ModernColors.Primary);
        checkImage.type = Image.Type.Simple;
        checkImage.preserveAspect = false;
        checkImage.color = MModUI.ModernColors.Primary;

        var toggle = toggleObj.AddComponent<Toggle>();
        toggle.targetGraphic = toggleBg;
        toggle.graphic = checkImage;

        var toggleColors = toggle.colors;
        toggleColors.normalColor = MModUI.GlassTheme.ButtonBg;
        toggleColors.highlightedColor = MModUI.GlassTheme.ButtonHover;
        toggleColors.pressedColor = MModUI.GlassTheme.ButtonActive;
        toggleColors.selectedColor = MModUI.GlassTheme.ButtonBg;
        toggleColors.disabledColor = new Color(1f, 1f, 1f, 0.08f);
        toggle.colors = toggleColors;
        ApplySettingsSelectableFeedback(toggle, MModUI.GlassTheme.InputBg);

        var valueText = CreateText("Value", row, getter(GetSelectedDifficultySettings()) ? CoopLocalization.Get("ui.difficulty.value.on") : CoopLocalization.Get("ui.difficulty.value.off"), 14, MModUI.ModernColors.TextSecondary);
        var valueLayout = valueText.GetComponent<RectTransform>().gameObject.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 80f;
        valueText.alignment = TextAlignmentOptions.Right;

        var resetButton = CreateResetButton(row, () =>
        {
            setter(_defaultCustomDifficulty.CanDash);
            RefreshDifficultyFields();
            ApplyChanges();
        });

        toggle.onValueChanged.AddListener(v =>
        {
            if (_workingDifficultySelection != DifficultyLevel.Custom)
            {
                RefreshDifficultyFields();
                return;
            }

            setter(v);
            RefreshDifficultyFields();
            ApplyChanges();
        });

        _difficultyBoolFields.Add(new DifficultyBoolBinding
        {
            Toggle = toggle,
            ValueText = valueText,
            Getter = getter,
            Setter = setter,
            ResetButton = resetButton
        });
    }

    private void CreateThemeModeField(
        Transform parent,
        string label,
        System.Func<UIThemeMode> getter,
        System.Action<UIThemeMode> setter,
        UIThemeMode defaultValue,
        string tooltip = null)
    {
        var row = CreateFieldRow(parent, label, false, tooltip);

        var buttonGroup = new GameObject("ThemeButtons");
        buttonGroup.transform.SetParent(row, false);
        var groupLayout = buttonGroup.AddComponent<HorizontalLayoutGroup>();
        groupLayout.spacing = 8f;
        groupLayout.childAlignment = TextAnchor.MiddleLeft;
        groupLayout.childControlHeight = true;
        groupLayout.childControlWidth = true;
        groupLayout.childForceExpandHeight = false;
        groupLayout.childForceExpandWidth = false;

        var groupSize = buttonGroup.AddComponent<LayoutElement>();
        groupSize.preferredWidth = 360f;
        groupSize.preferredHeight = 38f;

        CreateThemeButton(buttonGroup.transform, UIThemeMode.Black, getter, setter);
        CreateThemeButton(buttonGroup.transform, UIThemeMode.Spring, getter, setter);
        CreateThemeButton(buttonGroup.transform, UIThemeMode.Summer, getter, setter);
        RefreshThemeButtons();

        CreateResetButton(row, () =>
        {
            setter(MModUITheme.NormalizeMode(defaultValue));
            ApplyChanges();
            RefreshThemeButtons();
        });
    }

    private void CreateThemeButton(
        Transform parent,
        UIThemeMode mode,
        System.Func<UIThemeMode> getter,
        System.Action<UIThemeMode> setter)
    {
        var buttonObj = new GameObject($"Theme_{mode}");
        buttonObj.transform.SetParent(parent, false);
        var layout = buttonObj.AddComponent<LayoutElement>();
        layout.preferredWidth = 110f;
        layout.preferredHeight = 36f;

        var image = buttonObj.AddComponent<Image>();
        MModUI.StylePillImage(image, MModUI.GlassTheme.InputBg);
        MModUI.AddControlChrome(buttonObj, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));

        var text = CreateText("Label", buttonObj.transform, GetThemeModeName(mode), 14, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8, 5);
        rect.offsetMax = new Vector2(-8, -5);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = image;
        ApplySettingsSelectableFeedback(button, MModUI.GlassTheme.InputBg);
        button.onClick.AddListener(() =>
        {
            var normalizedMode = MModUITheme.NormalizeMode(mode);
            if (MModUITheme.NormalizeMode(getter()) == normalizedMode)
                return;

            setter(normalizedMode);
            ApplyChanges();
            RefreshThemeButtons();
        });

        _themeButtons[mode] = button;
    }

    private void RefreshThemeButtons()
    {
        foreach (var pair in _themeButtons)
        {
            var button = pair.Value;
            if (button == null)
                continue;

            var selected = pair.Key == MModUITheme.NormalizeMode(_workingGeneral.UiThemeMode);
            if (button.TryGetComponent<Image>(out var image))
            {
                MModUI.StylePillImage(image, selected ? MModUI.ModernColors.Primary : MModUI.GlassTheme.InputBg);
            }
            ApplySettingsSelectableFeedback(button, selected ? MModUI.ModernColors.Primary : MModUI.GlassTheme.InputBg);

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = GetThemeModeName(pair.Key);
                label.color = selected ? MModUI.ModernColors.PrimaryText : MModUI.ModernColors.TextPrimary;
            }
        }
    }

    private static string GetThemeModeName(UIThemeMode mode)
    {
        return mode switch
        {
            UIThemeMode.Black => CoopLocalization.Get("ui.settings.theme.black"),
            UIThemeMode.Spring => CoopLocalization.Get("ui.settings.theme.spring"),
            UIThemeMode.Summer => CoopLocalization.Get("ui.settings.theme.summer"),
            _ => CoopLocalization.Get("ui.settings.theme.white")
        };
    }

    private void CreateEnumField(Transform parent, string label, System.Func<NetworkTransportMode> getter, System.Action<NetworkTransportMode> setter, NetworkTransportMode? defaultValue = null, bool hostOnly = false, string tooltip = null)
    {
        var row = CreateFieldRow(parent, label, hostOnly, tooltip);

        var dropdown = CreateDropdown(row, getter());
        dropdown.onValueChanged.AddListener(idx =>
        {
            var selected = (NetworkTransportMode)idx;
            setter(selected);
            ApplyChanges();
        });

        CreateResetButton(row, () =>
        {
            var resetVal = defaultValue ?? getter();
            dropdown.value = (int)resetVal;
            dropdown.RefreshShownValue();
            setter(resetVal);
            ApplyChanges();
        });
    }

    private Button CreateResetButton(Transform parent, System.Action onClick)
    {
        var button = new GameObject("ResetButton");
        button.transform.SetParent(parent, false);
        var layout = button.AddComponent<LayoutElement>();
        layout.preferredWidth = 86f;
        layout.preferredHeight = 34f;

        var image = button.AddComponent<Image>();
        MModUI.StyleControlImage(image, MModUI.GlassTheme.InputBg);
        MModUI.AddControlChrome(button, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));

        var text = CreateText("ResetLabel", button.transform, CoopLocalization.Get("ui.settings.reset"), 14, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6, 4);
        textRect.offsetMax = new Vector2(-6, -4);

        var btn = button.AddComponent<Button>();
        btn.targetGraphic = image;
        ApplySettingsSelectableFeedback(btn, MModUI.GlassTheme.InputBg);
        btn.onClick.AddListener(() => onClick());

        return btn;
    }

    private Transform CreateFieldRow(Transform parent, string label, bool hostOnly, string tooltip)
    {
        var row = new GameObject(label);
        row.transform.SetParent(parent, false);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        var rowSize = row.AddComponent<LayoutElement>();
        rowSize.minHeight = 42f;

        var rowImage = row.AddComponent<Image>();
        MModUI.StyleControlImage(rowImage, SettingsRowColor());

        var labelText = CreateText("Label", row.transform, label, 15, MModUI.ModernColors.TextPrimary);
        var labelLayout = labelText.GetComponent<RectTransform>().gameObject.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 190;

        if (hostOnly)
        {
            CreateBadge(row.transform, CoopLocalization.Get("ui.aiSettings.badge.hostOnlyShort"));
        }

        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(row.transform, false);
        var spacerEl = spacer.AddComponent<LayoutElement>();
        spacerEl.flexibleWidth = 1;

        AddTooltipHandlers(row, tooltip);
        RegisterSearchRow(row, label, tooltip, parent);

        return row.transform;
    }

    private void RefreshHostOnlyControls()
    {
        var isHost = IsHostActive();
        if (isHost == _lastHostState)
        {
            return;
        }

        _lastHostState = isHost;

        for (var i = _hostOnlyToggles.Count - 1; i >= 0; i--)
        {
            var toggle = _hostOnlyToggles[i];
            if (toggle == null)
            {
                _hostOnlyToggles.RemoveAt(i);
                continue;
            }

            toggle.interactable = isHost;
        }
    }

    private TMP_Dropdown CreateDropdown(Transform parent, NetworkTransportMode value)
    {
        var go = new GameObject("Dropdown");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        MModUI.StyleControlImage(image, MModUI.GlassTheme.InputBg);
        MModUI.AddControlChrome(go, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(240, 36);

        var dropdown = go.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.template = CreateDropdownTemplate(go.transform);
        var captionKey = value == NetworkTransportMode.Direct
            ? "ui.settings.transport.direct"
            : "ui.settings.transport.steam";
        dropdown.captionText = CreateDropdownLabel(go.transform, CoopLocalization.Get(captionKey));
        dropdown.itemText = dropdown.template.GetComponentInChildren<TextMeshProUGUI>();
        dropdown.options.Clear();
        dropdown.options.Add(new TMP_Dropdown.OptionData(CoopLocalization.Get("ui.settings.transport.direct")));
        dropdown.options.Add(new TMP_Dropdown.OptionData(CoopLocalization.Get("ui.settings.transport.steam")));
        dropdown.value = (int)value;
        dropdown.RefreshShownValue();
        ApplySettingsSelectableFeedback(dropdown, MModUI.GlassTheme.InputBg);

        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 260;

        return dropdown;
    }

    private DifficultySettings GetSelectedDifficultySettings()
    {
        var custom = (_workingCustomDifficulty ?? DifficultyManager.GetCustomSettings()).CloneAndClamp();

        return _workingDifficultySelection == DifficultyLevel.Custom
            ? custom.ToSettings()
            : DifficultyManager.Get(_workingDifficultySelection);
    }

    private void RefreshDifficultyButtons()
    {
        foreach (var kvp in _difficultyButtons)
        {
            var button = kvp.Value;
            if (button == null) continue;

            var image = button.GetComponent<Image>();
            var selected = kvp.Key == _workingDifficultySelection;
            if (image != null)
            {
                MModUI.StyleControlImage(image, selected ? MModUI.ModernColors.Primary : MModUI.GlassTheme.InputBg);
            }

            ApplySettingsSelectableFeedback(button, selected ? MModUI.ModernColors.Primary : MModUI.GlassTheme.InputBg);

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = DifficultyManager.GetLocalizedName(kvp.Key);
                label.color = selected ? MModUI.ModernColors.PrimaryText : MModUI.ModernColors.TextPrimary;
            }
        }
    }

    private void RefreshDifficultyFields()
    {
        var settings = GetSelectedDifficultySettings();
        var isCustom = _workingDifficultySelection == DifficultyLevel.Custom;

        foreach (var field in _difficultyFields)
        {
            if (field.Slider == null || field.ValueText == null)
                continue;

            var value = field.Getter(settings);
            field.Slider.SetValueWithoutNotify(Mathf.Clamp(value, field.Min, field.Max));
            field.Slider.interactable = isCustom;
            field.ValueText.text = value.ToString(field.Format);

            if (field.Input != null)
            {
                field.Input.SetTextWithoutNotify(value.ToString(field.Format));
                field.Input.interactable = isCustom;
            }

            if (field.ResetButton != null)
            {
                field.ResetButton.interactable = isCustom;
            }
        }

        foreach (var field in _difficultyBoolFields)
        {
            if (field.Toggle == null || field.ValueText == null)
                continue;

            var value = field.Getter(settings);
            field.Toggle.SetIsOnWithoutNotify(value);
            field.Toggle.interactable = isCustom;
            field.ValueText.text = value
                ? CoopLocalization.Get("ui.difficulty.value.on")
                : CoopLocalization.Get("ui.difficulty.value.off");

            if (field.ResetButton != null)
            {
                field.ResetButton.interactable = isCustom;
            }
        }

        RefreshDifficultyButtons();
    }

    private void OnDifficultySelected(DifficultyLevel level)
    {
        _workingDifficultySelection = level;
        RefreshDifficultyFields();
        ApplyChanges();
    }

    private void CreateDifficultyButton(Transform parent, DifficultyLevel level)
    {
        var btnObj = new GameObject($"Difficulty_{level}");
        btnObj.transform.SetParent(parent, false);
        var layout = btnObj.AddComponent<LayoutElement>();
        layout.preferredWidth = 140f;
        layout.preferredHeight = 46f;

        var image = btnObj.AddComponent<Image>();
        MModUI.StyleControlImage(image, MModUI.GlassTheme.InputBg);
        MModUI.AddControlChrome(btnObj, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));

        var text = CreateText("Label", btnObj.transform, DifficultyManager.GetLocalizedName(level), 14, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6, 6);
        rect.offsetMax = new Vector2(-6, -6);

        var btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = image;
        ApplySettingsSelectableFeedback(btn, MModUI.GlassTheme.InputBg);
        btn.onClick.AddListener(() => OnDifficultySelected(level));

        _difficultyButtons[level] = btn;
    }

    private void BuildDifficultyPage(Transform parent)
    {
        EnsureWorkingCopies();
        _workingCustomDifficulty ??= DifficultyManager.GetCustomSettings().CloneAndClamp();
        _defaultCustomDifficulty ??= DifficultyManager.GetCustomSettings().CloneAndClamp();

        var section = CreateSection(parent,
            CoopLocalization.Get("ui.difficulty.section.title"),
            CoopLocalization.Get("ui.difficulty.section.subtitle"),
            false,
            "aiDifficulty");

        var presetRow = new GameObject("DifficultyPresets");
        presetRow.transform.SetParent(section, false);
        var presetLayout = presetRow.AddComponent<HorizontalLayoutGroup>();
        presetLayout.spacing = 10f;
        presetLayout.childAlignment = TextAnchor.UpperLeft;

        foreach (var level in new[]
                 {
                     DifficultyLevel.Easy,
                     DifficultyLevel.Normal,
                     DifficultyLevel.Hard,
                     DifficultyLevel.VeryHard,
                     DifficultyLevel.Impossible,
                     DifficultyLevel.Custom
                 })
        {
            CreateDifficultyButton(presetRow.transform, level);
        }

        var hint = CreateText("DifficultyHint", section, CoopLocalization.Get("ui.difficulty.section.hint"), 13, MModUI.ModernColors.TextSecondary);
        hint.alignment = TextAlignmentOptions.Left;

        var fields = new GameObject("DifficultyFields");
        fields.transform.SetParent(section, false);
        var fieldsLayout = fields.AddComponent<VerticalLayoutGroup>();
        fieldsLayout.spacing = 8f;
        fieldsLayout.childControlWidth = true;
        fieldsLayout.childControlHeight = true;

        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.patrolTurnSpeed"), s => s.PatrolTurnSpeed, v => _workingCustomDifficulty.PatrolTurnSpeed = v, 100f, 600f, _defaultCustomDifficulty.PatrolTurnSpeed);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.combatTurnSpeed"), s => s.CombatTurnSpeed, v => _workingCustomDifficulty.CombatTurnSpeed = v, 800f, 3500f, _defaultCustomDifficulty.CombatTurnSpeed);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.baseReactionTime"), s => s.BaseReactionTime, v => _workingCustomDifficulty.BaseReactionTime = v, 0.01f, 0.35f, _defaultCustomDifficulty.BaseReactionTime, format: "0.###");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.scatterRunning"), s => s.ScatterMultiIfTargetRunning, v => _workingCustomDifficulty.ScatterMultiIfTargetRunning = v, 0f, 5f, _defaultCustomDifficulty.ScatterMultiIfTargetRunning);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.scatterOffScreen"), s => s.ScatterMultiIfOffScreen, v => _workingCustomDifficulty.ScatterMultiIfOffScreen = v, 0f, 5f, _defaultCustomDifficulty.ScatterMultiIfOffScreen);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.nightReaction"), s => s.NightReactionTimeFactor, v => _workingCustomDifficulty.NightReactionTimeFactor = v, 0.5f, 4f, _defaultCustomDifficulty.NightReactionTimeFactor);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.hearing"), s => s.HearingAbility, v => _workingCustomDifficulty.HearingAbility = v, 0.5f, 4f, _defaultCustomDifficulty.HearingAbility);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.traceTarget"), s => s.TraceTargetChance, v => _workingCustomDifficulty.TraceTargetChance = v, 0f, 4f, _defaultCustomDifficulty.TraceTargetChance);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.shootDelay"), s => s.ShootDelayMultiplier, v => _workingCustomDifficulty.ShootDelayMultiplier = v, -0.3f, 0.6f, _defaultCustomDifficulty.ShootDelayMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.shootTime"), s => s.ShootTimeMultiplier, v => _workingCustomDifficulty.ShootTimeMultiplier = v, -0.3f, 0.8f, _defaultCustomDifficulty.ShootTimeMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.shootInterval"), s => s.ShootIntervalMultiplier, v => _workingCustomDifficulty.ShootIntervalMultiplier = v, -0.5f, 0.6f, _defaultCustomDifficulty.ShootIntervalMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.combatMoveTime"), s => s.CombatMoveTimeMultiplier, v => _workingCustomDifficulty.CombatMoveTimeMultiplier = v, -0.3f, 0.8f, _defaultCustomDifficulty.CombatMoveTimeMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.sightAngle"), s => s.SightAngleMultiplier, v => _workingCustomDifficulty.SightAngleMultiplier = v, -0.25f, 0.7f, _defaultCustomDifficulty.SightAngleMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.sightDistance"), s => s.SightDistanceMultiplier, v => _workingCustomDifficulty.SightDistanceMultiplier = v, -0.25f, 0.8f, _defaultCustomDifficulty.SightDistanceMultiplier, format: "0.##");
        CreateDifficultyToggle(fields.transform, CoopLocalization.Get("ui.difficulty.canDash"), s => s.CanDash, v => _workingCustomDifficulty.CanDash = v);
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.dashCooldown"), s => s.DashCoolTimeMultiplier, v => _workingCustomDifficulty.DashCoolTimeMultiplier = v, -0.6f, 0.9f, _defaultCustomDifficulty.DashCoolTimeMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.moveSpeed"), s => s.MoveSpeedFactor, v => _workingCustomDifficulty.MoveSpeedFactor = v, -0.5f, 0.8f, _defaultCustomDifficulty.MoveSpeedFactor, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.bulletSpeed"), s => s.BulletSpeedMultiplier, v => _workingCustomDifficulty.BulletSpeedMultiplier = v, -0.5f, 1.2f, _defaultCustomDifficulty.BulletSpeedMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.gunDistance"), s => s.GunDistanceMultiplier, v => _workingCustomDifficulty.GunDistanceMultiplier = v, -0.5f, 1.2f, _defaultCustomDifficulty.GunDistanceMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.damage"), s => s.DamageMultiplier, v => _workingCustomDifficulty.DamageMultiplier = v, -0.5f, 1.2f, _defaultCustomDifficulty.DamageMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.healthMultiplier"), s => s.HealthMultiplier, v => _workingCustomDifficulty.HealthMultiplier = v, 0.1f, DifficultyManager.MaxHealthMultiplier, _defaultCustomDifficulty.HealthMultiplier, format: "0.##");
        CreateDifficultySlider(fields.transform, CoopLocalization.Get("ui.difficulty.spawnBonus"), s => s.EnemySpawnBonusMultiplier, v => _workingCustomDifficulty.EnemySpawnBonusMultiplier = v, 0f, 6f, _defaultCustomDifficulty.EnemySpawnBonusMultiplier, format: "0.##", tooltip: CoopLocalization.Get("ui.difficulty.spawnBonus.desc"));
        CreateDifficultyToggle(fields.transform, CoopLocalization.Get("ui.difficulty.forceBoss"), s => s.ForceBossSpawn, v => _workingCustomDifficulty.ForceBossSpawn = v, CoopLocalization.Get("ui.difficulty.forceBoss.desc"));

        RefreshDifficultyFields();
    }

    private RectTransform CreateDropdownTemplate(Transform parent)
    {
        var templateGO = new GameObject("Template");
        templateGO.transform.SetParent(parent, false);
        templateGO.SetActive(false);
        var templateRect = templateGO.AddComponent<RectTransform>();
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.sizeDelta = new Vector2(0, 150);

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(templateGO.transform, false);
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.pivot = new Vector2(0, 1);
        viewportRect.anchorMin = new Vector2(0, 0);
        viewportRect.anchorMax = new Vector2(1, 1);
        viewportRect.offsetMin = new Vector2(0, 0);
        viewportRect.offsetMax = new Vector2(0, 0);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.35f);

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.offsetMin = new Vector2(0, 0);
        contentRect.offsetMax = new Vector2(0, 0);

        var layoutGroup = content.AddComponent<VerticalLayoutGroup>();
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childControlHeight = true;
        layoutGroup.childControlWidth = true;

        var item = new GameObject("Item");
        item.transform.SetParent(content.transform, false);
        var itemLayout = item.AddComponent<Toggle>();
        var itemBg = item.AddComponent<Image>();
        itemBg.color = new Color(1f, 1f, 1f, 0.08f);
        itemLayout.targetGraphic = itemBg;
        ApplySettingsSelectableFeedback(itemLayout, MModUI.GlassTheme.InputBg, false);

        var checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(item.transform, false);
        var checkmarkImage = checkmark.AddComponent<Image>();
        checkmarkImage.color = MModUI.ModernColors.Primary;
        var checkmarkRect = checkmark.GetComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0, 0.5f);
        checkmarkRect.sizeDelta = new Vector2(18, 18);
        checkmarkRect.anchoredPosition = new Vector2(12, 0);

        var label = CreateDropdownItemLabel(item.transform);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.offsetMin = new Vector2(36, 0);
        labelRect.offsetMax = new Vector2(-10, 0);

        itemLayout.graphic = checkmarkImage;
        itemLayout.interactable = true;
        var itemRect = item.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 32);

        var scrollbar = new GameObject("Scrollbar");
        scrollbar.transform.SetParent(templateGO.transform, false);
        var scrollbarRect = scrollbar.AddComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1, 0);
        scrollbarRect.anchorMax = new Vector2(1, 1);
        scrollbarRect.pivot = new Vector2(1, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(8, 0);

        var scrollRect = templateGO.AddComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.verticalScrollbar = MModUI.ConfigureVerticalScrollbar(
            scrollbar,
            new Color(1f, 1f, 1f, MModUITheme.IsDarkTheme ? 0.06f : 0.18f),
            new Color(MModUI.ModernColors.Primary.r, MModUI.ModernColors.Primary.g, MModUI.ModernColors.Primary.b, 0.74f));
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scrollRect.verticalScrollbarSpacing = -3f;

        return templateRect;
    }

    private void RegisterSearchRow(GameObject row, string label, string tooltip, Transform parent)
    {
        var section = parent.GetComponentInParent<SectionMeta>();
        string pageKey = null;

        if (section != null && !string.IsNullOrEmpty(section.PageKey))
        {
            pageKey = section.PageKey;
        }

        if (string.IsNullOrEmpty(pageKey))
        {
            foreach (var kvp in _pageRoots)
            {
                if (parent.IsChildOf(kvp.Value.transform))
                {
                    pageKey = kvp.Key;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(pageKey))
        {
            pageKey = _activePageKey;
        }

        if (string.IsNullOrEmpty(pageKey))
            return;
        var text = (label + " " + (tooltip ?? string.Empty)).ToLowerInvariant();
        _searchEntries.Add(new SearchEntry
        {
            Row = row,
            Section = section != null ? section.transform : parent,
            PageKey = pageKey,
            Text = text
        });
    }

    private void EnsureBuilt()
    {
        if (_initialized)
            return;

        BuildUI();
        _initialized = true;
        SyncPanelVisibility();
    }

    private void CreateTooltipLayer()
    {
        _tooltip = new GameObject("Tooltip");
        _tooltip.transform.SetParent(_canvas.transform, false);
        _tooltipRect = _tooltip.AddComponent<RectTransform>();
        _tooltipRect.pivot = new Vector2(0f, 1f);

        var bg = _tooltip.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.86f);
        var outline = _tooltip.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0f, 0f, 0.35f);
        outline.effectDistance = new Vector2(1f, -1f);

        var layout = _tooltip.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var fitter = _tooltip.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(_tooltip.transform, false);
        _tooltipLabel = textGO.AddComponent<TextMeshProUGUI>();
        _tooltipLabel.fontSize = 15;
        _tooltipLabel.color = MModUI.ModernColors.TextPrimary;
        _tooltipLabel.enableWordWrapping = true;
        _tooltipLabel.text = string.Empty;
        var textRect = _tooltipLabel.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        _tooltip.SetActive(false);
    }

    private void ApplySearchFilter(string term)
    {
        term = (term ?? string.Empty).ToLowerInvariant();
        var hasTerm = !string.IsNullOrEmpty(term);
        var sectionVisibility = new Dictionary<Transform, bool>();
        var pageVisibility = new Dictionary<string, bool>();

        foreach (var entry in _searchEntries)
        {
            if (entry.Row == null || string.IsNullOrEmpty(entry.PageKey))
                continue;

            var match = !hasTerm || entry.Text.Contains(term);
            var isPageActive = !hasTerm && _activePageKey == entry.PageKey;
            var shouldShow = match && (hasTerm || isPageActive);
            entry.Row.SetActive(shouldShow);

            if (!sectionVisibility.ContainsKey(entry.Section))
            {
                sectionVisibility[entry.Section] = false;
            }

            if (!pageVisibility.ContainsKey(entry.PageKey))
            {
                pageVisibility[entry.PageKey] = false;
            }

            if (shouldShow)
            {
                sectionVisibility[entry.Section] = true;
                pageVisibility[entry.PageKey] = true;
            }
        }

        foreach (var kvp in sectionVisibility)
        {
            if (kvp.Key != null)
            {
                kvp.Key.gameObject.SetActive(kvp.Value || !hasTerm);
            }
        }

        foreach (var page in _pageRoots)
        {
            var hasMatch = hasTerm && pageVisibility.ContainsKey(page.Key) && pageVisibility[page.Key];
            page.Value.SetActive(hasTerm ? hasMatch : page.Key == _activePageKey);
        }

        foreach (var kvp in _pageScrollLayouts)
        {
            if (kvp.Value == null)
                continue;

            var key = kvp.Key;
            if (_pageContents.TryGetValue(key, out var content))
            {
                var rect = content as RectTransform;
                if (rect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    var preferred = LayoutUtility.GetPreferredHeight(rect);
                    if (hasTerm)
                    {
                        kvp.Value.preferredHeight = preferred;
                        kvp.Value.flexibleHeight = 0f;
                        kvp.Value.minHeight = 0f;
                    }
                    else
                    {
                        kvp.Value.preferredHeight = -1f;
                        kvp.Value.flexibleHeight = 1f;
                        kvp.Value.minHeight = 720f;
                    }

                    if (_pageRootLayouts.TryGetValue(key, out var rootLayout) && rootLayout != null)
                    {
                        if (hasTerm)
                        {
                            rootLayout.preferredHeight = preferred;
                            rootLayout.flexibleHeight = 0f;
                            rootLayout.minHeight = 0f;
                        }
                        else
                        {
                            rootLayout.preferredHeight = -1f;
                            rootLayout.flexibleHeight = 1f;
                            rootLayout.minHeight = 720f;
                        }
                    }
                }
            }

            if (_pageScrollRects.TryGetValue(key, out var scroll) && scroll != null)
            {
                scroll.enabled = !hasTerm;
                if (!hasTerm)
                {
                    scroll.verticalNormalizedPosition = 1f;
                }
            }
        }

        if (_pageRootLayouts.TryGetValue(_activePageKey, out var activeLayout) && activeLayout != null && !hasTerm)
        {
            activeLayout.preferredHeight = -1f;
            activeLayout.flexibleHeight = 1f;
        }

        if (_pageScrollLayouts.TryGetValue(_activePageKey, out var activeScrollLayout) && activeScrollLayout != null && !hasTerm)
        {
            activeScrollLayout.preferredHeight = -1f;
            activeScrollLayout.flexibleHeight = 1f;
        }

        foreach (var root in _pageRoots.Values)
        {
            var rect = root != null ? root.transform as RectTransform : null;
            if (rect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
        }

        if (_pagesContentRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_pagesContentRect);
        }

        if (_pagesScroll != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_pagesScroll.viewport);
            _pagesScroll.verticalNormalizedPosition = 1f;
        }
    }

    private TMP_InputField CreateSearchInput(Transform parent)
    {
        var go = new GameObject("Search");
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 260f;
        layout.minHeight = 38f;
        layout.preferredHeight = 38f;

        var bg = go.AddComponent<Image>();
        MModUI.StyleControlImage(bg, MModUI.GlassTheme.InputBg);
        MModUI.AddControlChrome(go, WithAlpha(MModUI.ModernColors.InputBorder, 0.36f), WithAlpha(MModUI.ModernColors.Shadow, 0.10f), new Vector2(0f, -2f));

        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = CreateSearchViewport(go.transform);
        input.textComponent = CreateSearchText(input.textViewport.transform);
        input.placeholder = CreateSearchPlaceholder(input.textViewport.transform);
        input.pointSize = 16;
        input.characterLimit = 64;
        input.onValueChanged.AddListener(ApplySearchFilter);
        input.text = string.Empty;
        ApplySettingsSelectableFeedback(input, MModUI.GlassTheme.InputBg);

        return input;
    }

    private RectTransform CreateSearchViewport(Transform parent)
    {
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(parent, false);
        var rect = viewport.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.offsetMin = new Vector2(8, 6);
        rect.offsetMax = new Vector2(-8, -6);
        viewport.AddComponent<RectMask2D>();
        return rect;
    }

    private Graphic CreateSearchPlaceholder(Transform parent)
    {
        var go = new GameObject("Placeholder");
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = 15;
        text.color = MModUI.ModernColors.TextTertiary;
        text.text = CoopLocalization.Get("ui.settings.search.placeholder");
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return text;
    }

    private TextMeshProUGUI CreateSearchText(Transform parent)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = 16;
        text.color = MModUI.ModernColors.TextPrimary;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return text;
    }

    private sealed class SectionMeta : MonoBehaviour
    {
        public string PageKey;
    }

    private sealed class DifficultyFieldBinding
    {
        public Slider Slider;
        public TMP_Text ValueText;
        public TMP_InputField Input;
        public Button ResetButton;
        public System.Func<DifficultySettings, float> Getter;
        public System.Action<float> Setter;
        public float Min;
        public float Max;
        public string Format;
        public float DefaultValue;
    }

    private sealed class DifficultyBoolBinding
    {
        public Toggle Toggle;
        public TMP_Text ValueText;
        public Button ResetButton;
        public System.Func<DifficultySettings, bool> Getter;
        public System.Action<bool> Setter;
    }

    private sealed class SearchEntry
    {
        public GameObject Row;
        public Transform Section;
        public string PageKey;
        public string Text;
    }

    private void AddTooltipHandlers(GameObject target, string tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
            return;

        var trigger = target.AddComponent<EventTrigger>();
        trigger.triggers = new List<EventTrigger.Entry>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowTooltip(tooltip));
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideTooltip());
        trigger.triggers.Add(exit);
    }

    private void ShowTooltip(string text)
    {
        if (_tooltip == null || string.IsNullOrWhiteSpace(text))
            return;

        if (!_tooltip.activeSelf)
            _tooltip.SetActive(true);

        _tooltipLabel.text = text;
        UpdateTooltipPosition();
    }

    private void HideTooltip()
    {
        if (_tooltip != null && _tooltip.activeSelf)
        {
            _tooltip.SetActive(false);
        }
    }

    private void UpdateTooltipPosition()
    {
        if (_tooltipRect == null)
            return;

        var offset = new Vector2(18f, -18f);
        _tooltipRect.position = Input.mousePosition + (Vector3)offset;
    }

    private TextMeshProUGUI CreateDropdownItemLabel(Transform parent)
    {
        var label = new GameObject("Item Label");
        label.transform.SetParent(parent, false);
        var text = label.AddComponent<TextMeshProUGUI>();
        text.text = "";
        text.fontSize = 16;
        text.color = MModUI.ModernColors.TextPrimary;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.offsetMin = new Vector2(12, 0);
        rect.offsetMax = new Vector2(-32, 0);
        return text;
    }

    private TextMeshProUGUI CreateDropdownLabel(Transform parent, string value)
    {
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(parent, false);
        var text = labelGO.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = 16;
        text.color = MModUI.ModernColors.TextPrimary;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.offsetMin = new Vector2(12, 0);
        rect.offsetMax = new Vector2(-32, 0);
        return text;
    }

    private TMP_InputField CreateInput(Transform parent, string value)
    {
        var go = new GameObject("Input");
        go.transform.SetParent(parent, false);
        var background = go.AddComponent<Image>();
        MModUI.StyleControlImage(background, MModUI.GlassTheme.InputBg);
        MModUI.AddControlChrome(go, MModUI.ModernColors.InputBorder, MModUI.ModernColors.Shadow, new Vector2(0f, -2f));
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 140;
        layout.preferredHeight = 38;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(go.transform, false);
        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 15;
        text.color = MModUI.ModernColors.TextPrimary;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 8);
        textRect.offsetMax = new Vector2(-10, -8);

        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textComponent = text;
        input.text = value;
        input.contentType = TMP_InputField.ContentType.Standard;
        ApplySettingsSelectableFeedback(input, MModUI.GlassTheme.InputBg);

        return input;
    }

    private TMP_Text CreateText(string name, Transform parent, string text, int fontSize, Color color, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.enableWordWrapping = false;
        return tmp;
    }
}
