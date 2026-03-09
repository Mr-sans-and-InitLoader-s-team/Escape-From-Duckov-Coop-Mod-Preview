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
    private bool _lastHostState;

    private void EnsureWorkingCopies()
    {
        _workingSettings ??= CoopAISettings.Active.Clone();
        _workingGeneral ??= CoopAISettings.ActiveGeneral.Clone();
        _workingLootSettings ??= CoopLootSettings.Active.Clone();
        _workingCustomDifficulty ??= DifficultyManager.GetCustomSettings();
    }

    private static bool IsHostActive() => ModBehaviourF.Instance != null && ModBehaviourF.Instance.IsServer;

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

    private void BuildUI()
    {
        if (_initialized)
            return;

        _searchEntries.Clear();

        DontDestroyOnLoad(gameObject);
        Instance = this;
        var loaded = AISyncSettingsPersistence.LoadAndApply(CoopAISettings.Instance, CoopLootSettings.Instance);

        _workingSettings = (loaded?.AI ?? CoopAISettings.Active).Clone();
        _workingGeneral = (loaded?.General ?? CoopAISettings.ActiveGeneral).Clone();
        _workingLootSettings = (loaded?.Loot ?? CoopLootSettings.Active).Clone();
        _defaultSettings = AISyncTuningSettings.Default();
        _defaultGeneral = CoopGeneralSettings.Default();
        _defaultLootSettings = LootTuningSettings.Default();
        _workingDifficultySelection = loaded?.Difficulty?.Selected ?? DifficultyManager.Selected;
        _workingCustomDifficulty = (loaded?.Difficulty?.Custom ?? DifficultyManager.GetCustomSettings()).CloneAndClamp();
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
        bgImage.color = new Color(0f, 0f, 0f, 0.35f);
        var bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        _panel = CreatePanel("AISyncSettingsPanel", _canvas.transform);
        var layout = _panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 20);
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
        bodyLayout.spacing = 16f;
        bodyLayout.childAlignment = TextAnchor.UpperLeft;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;

        var nav = CreateNavColumn(body.transform);
        var pagesWrapper = new GameObject("PagesWrapper");
        pagesWrapper.transform.SetParent(body.transform, false);
        _pagesWrapperLayout = pagesWrapper.AddComponent<LayoutElement>();
        _pagesWrapperLayout.flexibleWidth = 1;
        _pagesWrapperLayout.flexibleHeight = 1;
        _pagesWrapperLayout.minHeight = 720f;
        _pagesWrapperLayout.minWidth = 1180f;

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

        _workingSettings = _workingSettings.CloneWithBounds();
        CoopAISettings.Instance?.Apply(_workingSettings);
        _workingGeneral = _workingGeneral.CloneWithBounds();
        CoopAISettings.Instance?.ApplyGeneral(_workingGeneral);

        _workingLootSettings = (_workingLootSettings ?? LootTuningSettings.Default()).CloneWithBounds();
        CoopLootSettings.Instance?.Apply(_workingLootSettings);

        var customDifficulty = (_workingCustomDifficulty ?? DifficultyManager.GetCustomSettings()).CloneAndClamp();
        DifficultyManager.SetCustomSettings(customDifficulty);
        DifficultyManager.SetDifficulty(_workingDifficultySelection);
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
        layout.padding = new RectOffset(4, 4, 0, 0);
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var headerSizer = header.AddComponent<LayoutElement>();
        headerSizer.minHeight = 46f;

        var title = CreateText("Title", header.transform, CoopLocalization.Get("ui.settings.title"), 26, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(header.transform, false);
        var spacerEl = spacer.AddComponent<LayoutElement>();
        spacerEl.flexibleWidth = 1;

        _searchInput = CreateSearchInput(header.transform);

        CreateCloseButton(_panel.transform);
        CreateApplyButton(header.transform);
        CreateSaveButton(header.transform);
    }

    private void CreateCloseButton(Transform parent)
    {
        var button = new GameObject("CloseButton");
        button.transform.SetParent(parent, false);
        var layout = button.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        layout.minHeight = 32f;
        layout.preferredHeight = 32f;
        layout.preferredWidth = 44f;

        var rect = button.GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = button.AddComponent<RectTransform>();
        }
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(44f, 32f);
        rect.anchoredPosition = new Vector2(-14f, -14f);

        var image = button.AddComponent<Image>();
        image.color = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        var outline = button.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.35f);
        outline.effectDistance = new Vector2(1f, -1f);

        var label = CreateText("Label", button.transform, "×", 18, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Center;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var btn = button.AddComponent<Button>();
        btn.targetGraphic = image;
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
        image.color = new Color(0.35f, 0.7f, 0.45f, 0.2f);
        var outline = button.AddComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.7f, 0.45f, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);

        var text = CreateText("Label", button.transform, CoopLocalization.Get("ui.settings.saveGlobal"), 15, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8, 6);
        rect.offsetMax = new Vector2(-8, -6);

        var btn = button.AddComponent<Button>();
        btn.targetGraphic = image;
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
        image.color = new Color(0.32f, 0.58f, 0.95f, 0.2f);
        var outline = button.AddComponent<Outline>();
        outline.effectColor = new Color(0.32f, 0.58f, 0.95f, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);

        var text = CreateText("Label", button.transform, CoopLocalization.Get("ui.settings.applyChanges"), 15, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8, 6);
        rect.offsetMax = new Vector2(-8, -6);

        var btn = button.AddComponent<Button>();
        btn.targetGraphic = image;
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
        background.color = new Color(1f, 1f, 1f, 0.03f);

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
        image.color = MModUI.GlassTheme.PanelBg;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = MModUI.ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -6f);

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        outline.effectDistance = new Vector2(2f, -2f);

        var canvasGroup = go.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0.98f;

        return go;
    }

    private Transform CreateSection(Transform parent, string title, string subtitle, bool hostOnly = false, string pageKey = null)
    {
        var card = new GameObject(title + "Card");
        card.transform.SetParent(parent, false);
        var image = card.AddComponent<Image>();
        image.color = MModUI.GlassTheme.CardBg;
        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 14);
        layout.spacing = 8;
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

        CreateText("Title", header.transform, title, 18, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
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
            var img = kvp.Value.GetComponent<Image>();
            if (img != null)
            {
                img.color = kvp.Key == key ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.04f);
            }
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
        image.color = new Color(1f, 1f, 1f, 0.04f);

        var text = CreateText("Label", go.transform, label, 15, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6, 6);
        textRect.offsetMax = new Vector2(-6, -6);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => ShowPageInternal(key));

        _navButtons[key] = button;
    }

    private void CreateBadge(Transform parent, string text)
    {
        var badge = new GameObject("Badge");
        badge.transform.SetParent(parent, false);
        var layout = badge.AddComponent<LayoutElement>();
        layout.preferredWidth = 64;
        layout.preferredHeight = 22;
        var image = badge.AddComponent<Image>();
        image.color = new Color(0.85f, 0.35f, 0.35f, 0.75f);
        image.raycastTarget = false;

        var t = CreateText("BadgeText", badge.transform, text, 12, Color.white, FontStyles.Bold);
        t.alignment = TextAlignmentOptions.Center;
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
        toggleLayout.preferredWidth = 44f;
        toggleLayout.preferredHeight = 22f;

        var toggleRect = toggleObj.GetComponent<RectTransform>();
        toggleRect.sizeDelta = new Vector2(40f, 20f);

        var toggle = toggleObj.AddComponent<Toggle>();
        var toggleBg = toggleObj.AddComponent<Image>();
        toggleBg.color = MModUI.GlassTheme.InputBg;
        toggleBg.raycastTarget = true;
        var toggleOutline = toggleObj.AddComponent<Outline>();
        toggleOutline.effectColor = MModUI.ModernColors.InputBorder;
        toggleOutline.effectDistance = new Vector2(1f, -1f);
        toggle.targetGraphic = toggleBg;
        var checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(toggleObj.transform, false);
        var checkRect = checkmark.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkRect.sizeDelta = new Vector2(16f, 10f);
        var checkImage = checkmark.AddComponent<Image>();
        checkImage.color = MModUI.ModernColors.Primary;
        toggle.graphic = checkImage;

        var toggleColors = toggle.colors;
        toggleColors.normalColor = new Color(1f, 1f, 1f, 0.14f);
        toggleColors.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
        toggleColors.pressedColor = new Color(1f, 1f, 1f, 0.22f);
        toggleColors.selectedColor = new Color(1f, 1f, 1f, 0.18f);
        toggleColors.disabledColor = new Color(1f, 1f, 1f, 0.08f);
        toggle.colors = toggleColors;

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

        var sliderBg = sliderObj.AddComponent<Image>();
        sliderBg.color = new Color(1f, 1f, 1f, 0.06f);

        var sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.sizeDelta = new Vector2(170, 14);

        var slider = sliderObj.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;

        var fillArea = new GameObject("FillArea");
        fillArea.transform.SetParent(sliderObj.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.35f);
        fillAreaRect.anchorMax = new Vector2(1, 0.65f);
        fillAreaRect.offsetMin = new Vector2(8, 0);
        fillAreaRect.offsetMax = new Vector2(-8, 0);

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
        toggleLayout.preferredWidth = 44f;
        toggleLayout.preferredHeight = 22f;

        var toggleRect = toggleObj.GetComponent<RectTransform>();
        toggleRect.sizeDelta = new Vector2(40f, 20f);

        var toggle = toggleObj.AddComponent<Toggle>();
        var toggleBg = toggleObj.AddComponent<Image>();
        toggleBg.color = MModUI.GlassTheme.InputBg;
        toggleBg.raycastTarget = true;
        var toggleOutline = toggleObj.AddComponent<Outline>();
        toggleOutline.effectColor = MModUI.ModernColors.InputBorder;
        toggleOutline.effectDistance = new Vector2(1f, -1f);
        toggle.targetGraphic = toggleBg;
        var checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(toggleObj.transform, false);
        var checkRect = checkmark.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkRect.sizeDelta = new Vector2(16f, 10f);
        var checkImage = checkmark.AddComponent<Image>();
        checkImage.color = MModUI.ModernColors.Primary;
        toggle.graphic = checkImage;

        var toggleColors = toggle.colors;
        toggleColors.normalColor = new Color(1f, 1f, 1f, 0.14f);
        toggleColors.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
        toggleColors.pressedColor = new Color(1f, 1f, 1f, 0.22f);
        toggleColors.selectedColor = new Color(1f, 1f, 1f, 0.18f);
        toggleColors.disabledColor = new Color(1f, 1f, 1f, 0.08f);
        toggle.colors = toggleColors;

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
        image.color = MModUI.GlassTheme.InputBg;
        var outline = button.AddComponent<Outline>();
        outline.effectColor = MModUI.ModernColors.InputBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var text = CreateText("ResetLabel", button.transform, CoopLocalization.Get("ui.settings.reset"), 14, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6, 4);
        textRect.offsetMax = new Vector2(-6, -4);

        var btn = button.AddComponent<Button>();
        btn.targetGraphic = image;
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

        var rowSize = row.AddComponent<LayoutElement>();
        rowSize.minHeight = 42f;

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
        image.color = new Color(1f, 1f, 1f, 0.05f);
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(240, 36);

        var dropdown = go.AddComponent<TMP_Dropdown>();
        dropdown.template = CreateDropdownTemplate(go.transform);
        dropdown.captionText = CreateDropdownLabel(go.transform, CoopLocalization.Get(value == NetworkTransportMode.Direct ? "ui.settings.transport.direct" : "ui.settings.transport.steam"));
        dropdown.itemText = dropdown.template.GetComponentInChildren<TextMeshProUGUI>();
        dropdown.options.Clear();
        dropdown.options.Add(new TMP_Dropdown.OptionData(CoopLocalization.Get("ui.settings.transport.direct")));
        dropdown.options.Add(new TMP_Dropdown.OptionData(CoopLocalization.Get("ui.settings.transport.steam")));
        dropdown.value = (int)value;
        dropdown.RefreshShownValue();

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
                image.color = selected ? MModUI.ModernColors.Primary : new Color(1f, 1f, 1f, 0.04f);
            }

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = DifficultyManager.GetLocalizedName(kvp.Key);
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
        image.color = new Color(1f, 1f, 1f, 0.04f);

        var text = CreateText("Label", btnObj.transform, DifficultyManager.GetLocalizedName(level), 14, MModUI.ModernColors.TextPrimary, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6, 6);
        rect.offsetMax = new Vector2(-6, -6);

        var btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = image;
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
        var scrollRect = templateGO.AddComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.verticalScrollbar = scrollbar.AddComponent<Scrollbar>();
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
        layout.preferredWidth = 200f;
        layout.minHeight = 32f;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.08f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = MModUI.ModernColors.InputBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var input = go.AddComponent<TMP_InputField>();
        input.textViewport = CreateSearchViewport(go.transform);
        input.textComponent = CreateSearchText(input.textViewport.transform);
        input.placeholder = CreateSearchPlaceholder(input.textViewport.transform);
        input.pointSize = 16;
        input.characterLimit = 64;
        input.onValueChanged.AddListener(ApplySearchFilter);
        input.text = string.Empty;

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
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
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
        text.enableWordWrapping = false;
        return text;
    }

    private TextMeshProUGUI CreateSearchText(Transform parent)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = 16;
        text.color = MModUI.ModernColors.TextPrimary;
        text.enableWordWrapping = false;
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
        background.color = MModUI.GlassTheme.InputBg;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = MModUI.ModernColors.InputBorder;
        outline.effectDistance = new Vector2(1f, -1f);
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
        input.textComponent = text;
        input.text = value;
        input.contentType = TMP_InputField.ContentType.Standard;

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
