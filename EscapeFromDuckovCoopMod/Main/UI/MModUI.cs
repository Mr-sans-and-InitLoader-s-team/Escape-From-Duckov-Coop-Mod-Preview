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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LeTai.Asset.TranslucentImage;
using Steamworks;
using static BakeryLightmapGroup;
using RenderMode = UnityEngine.RenderMode;

namespace EscapeFromDuckovCoopMod;

public class MModUI : MonoBehaviour
{
    public static MModUI Instance;

    // UI 组件引用 - 使用组件容器
    private Canvas _canvas;
    private MModUIComponents _components;
    private MModUILayoutBuilder _layoutBuilder;

    private GameObject _hostEntryPrefab;
    private GameObject _playerEntryPrefab;

    public bool showUI = true;
    public bool showPlayerStatusWindow;
    public KeyCode toggleUIKey = KeyCode.Equals;  // = 键
    public KeyCode togglePlayerStatusKey = KeyCode.P;
    public readonly KeyCode readyKey = KeyCode.J;

    private readonly List<(string sender, string content)> _chatHistory = new();
    private const int ChatHistoryLimit = 50;
    private const float ChatAutoHideDelay = 3f;
    private bool _chatVisible;
    private bool _chatAutoHideArmed;
    private float _chatAutoHideDeadline;
    private Coroutine _chatAutoHideRoutine;
    public KeyCode chatToggleKey = KeyCode.Return;

    private AISyncSettingsUI _aiSettingsUI;

    private readonly List<string> _hostList = new();
    private readonly HashSet<string> _hostSet = new();
    private string _manualIP = "127.0.0.1";
    private string _manualPort = "9050";
    private int _port = 9050;
    private string _status = "未连接";
    public bool _streamerMode;

    private readonly Dictionary<string, GameObject> _hostEntries = new();
    private readonly Dictionary<string, GameObject> _playerEntries = new();
    private readonly HashSet<string> _displayedPlayerIds = new();  // 缓存已显示的玩家ID

    // Steam相关字段
    private readonly List<SteamLobbyManager.LobbyInfo> _steamLobbyInfos = new();
    private readonly HashSet<ulong> _displayedSteamLobbies = new();  // 缓存已显示的房间ID
    private string _steamLobbySearchTerm = string.Empty;
    private string _lastAppliedSteamSearchTerm = string.Empty;
    private string _steamLobbyName = string.Empty;
    private string _steamLobbyPassword = string.Empty;
    private bool _steamLobbyFriendsOnly;
    private int _steamLobbyMaxPlayers = 2;
    private string _steamJoinPassword = string.Empty;

    // 投票面板状态缓存
    private bool _lastVoteActive = false;
    private string _lastVoteSceneId = "";
    private bool _lastLocalReady = false;
    private readonly HashSet<string> _lastVoteParticipants = new();
    private float _lastVoteUpdateTime = 0f;
    private Coroutine _votePanelAnimation;
    private bool _votePanelVisible;
    private const float VotePanelWidth = 380f;
    private const float VotePanelMinHeight = 252f;
    private const float VotePanelMaxHeight = 430f;
    private const float VotePlayerRowHeight = 30f;
    private const float VotePlayerRowSpacing = 8f;
    private static readonly Vector2 VotePanelShownPosition = new Vector2(0f, -24f);

    public static class ModernColors
    {
        public static Color Primary => MModUITheme.Primary;
        public static Color PrimaryHover => MModUITheme.PrimaryHover;
        public static Color PrimaryActive => MModUITheme.PrimaryActive;
        public static Color PrimaryText => MModUITheme.PrimaryText;
        public static Color BgDark => MModUITheme.BgDark;
        public static Color BgMedium => MModUITheme.BgMedium;
        public static Color BgLight => MModUITheme.BgLight;
        public static Color TextPrimary => MModUITheme.TextPrimary;
        public static Color TextSecondary => MModUITheme.TextSecondary;
        public static Color TextTertiary => MModUITheme.TextTertiary;
        public static Color Success => MModUITheme.Success;
        public static Color Warning => MModUITheme.Warning;
        public static Color Error => MModUITheme.Error;
        public static Color Info => MModUITheme.Info;
        public static Color InputBg => MModUITheme.InputBg;
        public static Color InputBorder => MModUITheme.InputBorder;
        public static Color InputFocus => MModUITheme.PrimaryHover;
        public static Color Divider => MModUITheme.Divider;
        public static Color GlassBg => MModUITheme.GlassBg;
        public static Color Shadow => MModUITheme.Shadow;
    }

    public static class GlassTheme
    {
        public static Color PanelBg => MModUITheme.PanelBg;
        public static Color CardBg => MModUITheme.CardBg;
        public static Color ButtonBg => MModUITheme.ButtonBg;
        public static Color ButtonHover => MModUITheme.ButtonHover;
        public static Color ButtonActive => MModUITheme.ButtonActive;
        public static Color ButtonText => MModUITheme.ButtonText;
        public static Color InputBg => MModUITheme.GlassInputBg;
        public static Color Accent => MModUITheme.Accent;
        public static Color Text => MModUITheme.GlassText;
        public static Color TextSecondary => MModUITheme.GlassTextSecondary;
        public static Color Divider => MModUITheme.GlassDivider;
        public static Color ViewportBg => MModUITheme.ViewportBg;
    }

    private static Sprite _roundedPanelSprite;
    private static Sprite _roundedCardSprite;
    private static Sprite _roundedControlSprite;
    private static Sprite _roundedPillSprite;
    private static Sprite _roundedScrollbarSprite;
    private static Sprite _roundedCloseGlyphSprite;
    private static Sprite _roundedToggleSprite;






    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;

    private List<string> hostList => Service?.hostList ?? _hostList;
    private HashSet<string> hostSet => Service?.hostSet ?? _hostSet;

    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
    private Dictionary<string, PlayerStatus> clientPlayerStatuses => Service?.clientPlayerStatuses;

    // Steam相关属性
    private SteamLobbyManager LobbyManager => SteamLobbyManager.Instance;
    internal NetworkTransportMode TransportMode => Service?.TransportMode ?? NetworkTransportMode.Direct;

    // 公开属性供布局构建器访问
    internal NetService Service => NetService.Instance;
    internal bool IsServer => Service != null && Service.IsServer;
    internal int port => Service?.port ?? _port;
    internal string status => Service?.status ?? _status;
    internal string manualIP
    {
        get => Service?.manualIP ?? _manualIP;
        set
        {
            _manualIP = value;
            if (Service != null) Service.manualIP = value;
        }
    }
    internal string manualPort
    {
        get => Service?.manualPort ?? _manualPort;
        set
        {
            _manualPort = value;
            if (Service != null) Service.manualPort = value;
        }
    }

    internal bool StreamerMode => _streamerMode;

    internal void SetStreamerMode(bool enabled)
    {
        if (_streamerMode == enabled)
            return;

        _streamerMode = enabled;
        UpdateStreamerModeVisuals();
    }

    private void Update()
    {
        //var serverLoading = NetService.Instance.IsServer && SceneNet.Instance.IsServerLoadInProgress();
        //if (serverLoading)
        //{
        //    return;
        //}
        // 语言变更检测及自动重载
        CoopLocalization.CheckLanguageChange();

        // 切换主界面显示
        if (Input.GetKeyDown(toggleUIKey))
        {
            showUI = !showUI;
            if (_components?.MainPanel != null)
            {
                StartCoroutine(AnimatePanel(_components.MainPanel, showUI));
            }
        }

        // 切换玩家状态窗口
        if (Input.GetKeyDown(togglePlayerStatusKey))
        {
            showPlayerStatusWindow = !showPlayerStatusWindow;
            if (_components?.PlayerStatusPanel != null)
            {
                StartCoroutine(AnimatePanel(_components.PlayerStatusPanel, showPlayerStatusWindow));
            }
        }

        // 切换聊天窗口（Enter）
        var enterPressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (enterPressed)
        {
            if (IsChatTyping())
                SubmitChatMessage();
            else
                ToggleChatPanel();
        }

        // 更新模式显示（服务器/客户端状态）
        UpdateModeDisplay();

        // 同步连接状态显示
        UpdateConnectionStatus();

        // 更新投票面板
        UpdateVotePanel();

        // 更新观战面板
        UpdateSpectatorPanel();

        // 定期更新主机列表和玩家列表
        UpdateHostList();
        UpdatePlayerList();

        // 更新Steam Lobby列表
        UpdateSteamLobbyList();

    }

    // 面板动画
    internal IEnumerator AnimatePanel(GameObject panel, bool show)
    {
        if (show)
        {
            panel.SetActive(true);
            var canvasGroup = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0;

            float time = 0;
            while (time < 0.2f)
            {
                time += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(0, 1, time / 0.2f);
                yield return null;
            }
            canvasGroup.alpha = 1;
        }
        else
        {
            var canvasGroup = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();

            float time = 0;
            while (time < 0.15f)
            {
                time += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1, 0, time / 0.15f);
                yield return null;
            }
            panel.SetActive(false);
        }
    }

    private void SetVotePanelVisible(bool visible)
    {
        if (_components?.VotePanel == null)
            return;

        if (_votePanelVisible == visible &&
            (_components.VotePanel.activeSelf || !visible))
        {
            return;
        }

        _votePanelVisible = visible;
        if (_votePanelAnimation != null)
        {
            StopCoroutine(_votePanelAnimation);
            _votePanelAnimation = null;
        }

        _votePanelAnimation = StartCoroutine(AnimateVotePanel(visible));
    }

    private IEnumerator AnimateVotePanel(bool show)
    {
        var panel = _components?.VotePanel;
        if (panel == null)
            yield break;

        var rect = panel.GetComponent<RectTransform>();
        var canvasGroup = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();
        if (rect == null)
            yield break;

        if (show)
            panel.SetActive(true);

        var startPos = rect.anchoredPosition;
        var targetPos = show ? VotePanelShownPosition : GetVotePanelHiddenPosition(rect.sizeDelta.y);
        var startAlpha = canvasGroup.alpha;
        var targetAlpha = show ? 1f : 0f;
        var startScale = rect.localScale;
        var targetScale = show ? Vector3.one : Vector3.one * 0.96f;
        var duration = show ? 0.42f : 0.26f;
        var time = 0f;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(time / duration);
            var eased = show ? VoteEaseOutBack(t) : VoteEaseInCubic(t);
            var alphaT = show ? VoteEaseOutCubic(t) : t;

            rect.anchoredPosition = Vector2.LerpUnclamped(startPos, targetPos, eased);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, alphaT);

            var bounce = show ? Mathf.Sin(t * Mathf.PI) * 0.018f : 0f;
            rect.localScale = Vector3.LerpUnclamped(startScale, targetScale, Mathf.Clamp01(eased)) * (1f + bounce);
            yield return null;
        }

        rect.anchoredPosition = targetPos;
        rect.localScale = targetScale;
        canvasGroup.alpha = targetAlpha;
        if (!show)
            panel.SetActive(false);

        _votePanelAnimation = null;
    }

    private static float VoteEaseOutCubic(float t)
    {
        t = 1f - Mathf.Clamp01(t);
        return 1f - t * t * t;
    }

    private static float VoteEaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    private static float VoteEaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.35f;
        var c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private static Vector2 GetVotePanelHiddenPosition(float panelHeight)
    {
        return new Vector2(0f, panelHeight + 34f);
    }

    private static float CalculateVotePlayerListHeight(int playerCount)
    {
        playerCount = Mathf.Max(1, playerCount);
        var fullHeight = 16f + playerCount * VotePlayerRowHeight + Mathf.Max(0, playerCount - 1) * VotePlayerRowSpacing;
        return Mathf.Clamp(fullHeight, 48f, 172f);
    }

    private static float CalculateVotePanelHeight(int playerCount)
    {
        return Mathf.Clamp(202f + CalculateVotePlayerListHeight(playerCount), VotePanelMinHeight, VotePanelMaxHeight);
    }

    public void Init()
    {
        Instance = this;
        _aiSettingsUI = FindObjectOfType<AISyncSettingsUI>();

        // 初始化组件容器和布局构建器
        _components = new MModUIComponents();
        _layoutBuilder = new MModUILayoutBuilder(this, _components);

        var svc = Service;
        if (svc != null)
        {
            _manualIP = svc.manualIP;
            _manualPort = svc.manualPort;
            _status = svc.status;
            _port = svc.port;
            _hostList.Clear();
            _hostSet.Clear();
            _hostList.AddRange(svc.hostList);
            foreach (var host in svc.hostSet) _hostSet.Add(host);

            // Steam相关初始化
            var options = svc.LobbyOptions;
            _steamLobbyName = options.LobbyName;
            _steamLobbyPassword = options.Password;
            _steamLobbyFriendsOnly = options.Visibility == SteamLobbyVisibility.FriendsOnly;
            _steamLobbyMaxPlayers = Mathf.Clamp(options.MaxPlayers, 2, 16);
        }

        // 注册Steam Lobby相关事件
        if (LobbyManager != null)
        {
            LobbyManager.LobbyListUpdated -= OnLobbyListUpdated;
            LobbyManager.LobbyListUpdated += OnLobbyListUpdated;
            LobbyManager.LobbyJoined -= OnLobbyJoined;
            LobbyManager.LobbyJoined += OnLobbyJoined;
            _steamLobbyInfos.Clear();
            _steamLobbyInfos.AddRange(LobbyManager.AvailableLobbies);
        }

        CreateUI();
    }

    internal void RefreshTheme()
    {
        if (_canvas == null)
            return;

        var mainVisible = showUI;
        var playerStatusVisible = showPlayerStatusWindow;
        var chatVisible = _chatVisible;

        if (_chatAutoHideRoutine != null)
        {
            StopCoroutine(_chatAutoHideRoutine);
            _chatAutoHideRoutine = null;
        }

        Destroy(_canvas.gameObject);
        _components = new MModUIComponents();
        _layoutBuilder = new MModUILayoutBuilder(this, _components);
        _hostEntries.Clear();
        _playerEntries.Clear();
        _displayedPlayerIds.Clear();
        _displayedSteamLobbies.Clear();

        CreateUI();

        if (_components.MainPanel != null)
            _components.MainPanel.SetActive(mainVisible);
        if (_components.PlayerStatusPanel != null)
            _components.PlayerStatusPanel.SetActive(playerStatusVisible);
        if (_components.ChatPanel != null)
            _components.ChatPanel.SetActive(chatVisible);

        foreach (var message in _chatHistory)
            AppendChatEntry(message.sender, message.content);

        UpdateModeDisplay();
        UpdateConnectionStatus();
        UpdateTransportModePanels();
        UpdateHostList();
        UpdatePlayerList();
        UpdateSteamLobbyList();
    }

    private void OnDestroy()
    {
        if (LobbyManager != null)
        {
            LobbyManager.LobbyListUpdated -= OnLobbyListUpdated;
        }
    }

    private void OnLobbyListUpdated(IReadOnlyList<SteamLobbyManager.LobbyInfo> lobbies)
    {
        _steamLobbyInfos.Clear();
        _steamLobbyInfos.AddRange(lobbies);
    }

    private void OnLobbyJoined()
    {
        Debug.Log("[MModUI] Lobby加入成功，强制刷新玩家列表");
        // 清空玩家列表缓存，强制刷新
        _displayedPlayerIds.Clear();
    }

    internal void ToggleAISyncSettings()
    {
        if (_aiSettingsUI == null)
        {
            _aiSettingsUI = FindObjectOfType<AISyncSettingsUI>();
        }

        if (_aiSettingsUI != null)
        {
            _aiSettingsUI.Toggle();
        }
        else
        {
            Debug.LogWarning("[MModUI] AISyncSettingsUI 未找到，无法切换");
        }
    }

    internal void OpenCoopSettingsPage(string pageKey)
    {
        if (_aiSettingsUI == null)
        {
            _aiSettingsUI = FindObjectOfType<AISyncSettingsUI>();
        }

        if (_aiSettingsUI != null)
        {
            _aiSettingsUI.ShowPageExternal(pageKey ?? "network");
        }
        else
        {
            Debug.LogWarning("[MModUI] AISyncSettingsUI 未找到，无法打开页面");
        }
    }

    private void CreateUI()
    {
        // 确保有EventSystem（按钮交互必需）
        if (EventSystem.current == null)
        {
            var eventSystemGO = new GameObject("EventSystem");
            DontDestroyOnLoad(eventSystemGO);
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<StandaloneInputModule>();
        }

        // 初始化主相机的模糊源
        InitializeBlurSource();

        // 创建 Canvas
        var canvasGO = new GameObject("CoopModCanvas");
        DontDestroyOnLoad(canvasGO);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9999;

        var canvasScaler = canvasGO.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920, 1080);
        canvasScaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // 创建主面板
        CreateMainPanel();

        // 创建玩家状态面板
        CreatePlayerStatusPanel();

        // 创建投票面板
        CreateVotePanel();

        // 创建观战面板
        CreateSpectatorPanel();

        // 创建聊天面板
        CreateChatPanel();
    }

    private static string _tipText;
    private static float _tipExpireTime;
    public static void ShowTip(string msg, float duration = 4f)
    {
        _tipText = msg;
        _tipExpireTime = Time.time + duration;
    }

    private void OnGUI()
    {
        // ========= 右上角自适应分辨率显示版本号 =========
        {
            // 当前屏幕宽高（就是当前游戏分辨率）
            float sw = Screen.width;
            float sh = Screen.height;

            // 参考分辨率，比如 1920x1080
            const float refW = 1920f;
            const float refH = 1080f;

            // 取较小的缩放，保证宽高比例变化时也比较合理
            float scale = Mathf.Min(sw / refW, sh / refH);

            // 基础字体和边距
            int baseFontSize = 14;
            float basePadding = 10f;

            // 实际使用的字体/边距
            int fontSize = Mathf.Max(10, Mathf.RoundToInt(baseFontSize * scale));
            float padding = basePadding * scale;

          

            // ========= 顶部红色提示文字（自适应缩放） =========
            if (!string.IsNullOrEmpty(_tipText) && Time.time < _tipExpireTime)
            {
                // 可以复用上面的 sw/sh/scale/padding 等
                int tipBaseFontSize = 22;            // 提示字稍微大一点
                int tipFontSize = Mathf.Max(12, Mathf.RoundToInt(tipBaseFontSize * scale));
                float tipPaddingTop = padding * 1.5f; // 比版本号稍微往下一点

                GUIStyle tipStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = tipFontSize,
                    fontStyle = FontStyle.Bold
                };
                tipStyle.normal.textColor = ModernColors.Error;

                Vector2 tipSize = tipStyle.CalcSize(new GUIContent(_tipText));

                var tipRect = new Rect(
                    (sw - tipSize.x) / 2f, // 居中
                    tipPaddingTop,
                    tipSize.x,
                    tipSize.y
                );

                GUI.Label(tipRect, _tipText, tipStyle);
            }
            else if (!string.IsNullOrEmpty(_tipText) && Time.time >= _tipExpireTime)
            {
                // 超时后清空文本
                _tipText = null;
            }
        }
    }

    #region UI 创建方法

    private void CreateSideDecorations()
    {
        if (_components?.MainPanel == null)
        {
            return;
        }

        if (_components.LeftDecorationImage != null)
        {
            Destroy(_components.LeftDecorationImage.gameObject);
            _components.LeftDecorationImage = null;
        }

        if (_components.RightDecorationImage != null)
        {
            Destroy(_components.RightDecorationImage.gameObject);
            _components.RightDecorationImage = null;
        }

        if (_components.TopDecorationImage != null)
        {
            Destroy(_components.TopDecorationImage.gameObject);
            _components.TopDecorationImage = null;
        }

        if (!MModUITheme.ShouldShowNewYearDecorations)
            return;

        var leftSprite = LoadDecorationSprite("Assets/NewYear1");
        var rightSprite = LoadDecorationSprite("Assets/NewYear2");
        var topSprite = LoadDecorationSprite("Assets/NewYear3");

        var panelRect = _components.MainPanel.transform as RectTransform;
        var panelHeight = panelRect != null && panelRect.rect.height > 1f ? panelRect.rect.height : 784f;
        const float sideEdgePadding = 3f;
        const float topBannerLift = 3f; // 向上抬升 3 像素（等效于底端与 UI 顶边距离 -3）
        var sideTopOffset = -panelHeight * 0.1f; // 侧边对联顶部下移十分之一

        if (leftSprite != null)
        {
            _components.LeftDecorationImage = CreateSideDecorationImage("LeftDecoration", _components.MainPanel.transform, leftSprite, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-sideEdgePadding, sideTopOffset));
        }

        if (rightSprite != null)
        {
            _components.RightDecorationImage = CreateSideDecorationImage("RightDecoration", _components.MainPanel.transform, rightSprite, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(sideEdgePadding, sideTopOffset));
        }

        if (topSprite != null)
        {
            _components.TopDecorationImage = CreateSideDecorationImage("TopDecoration", _components.MainPanel.transform, topSprite, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, topBannerLift), true);
        }
    }

    private Image CreateSideDecorationImage(string name, Transform parent, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, bool fitByWidth = false)
    {
        var decoration = new GameObject(name);
        decoration.transform.SetParent(parent, false);

        var rect = decoration.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;

        var aspect = sprite.rect.width / Mathf.Max(sprite.rect.height, 1f);
        var parentRect = parent as RectTransform;
        var parentHeight = parentRect != null && parentRect.rect.height > 1f ? parentRect.rect.height : 640f;
        var parentWidth = parentRect != null && parentRect.rect.width > 1f ? parentRect.rect.width : 960f;

        if (fitByWidth)
        {
            var targetWidth = parentWidth * 0.22f; // 横批目标宽度系数调整为 0.22
            rect.sizeDelta = new Vector2(targetWidth, targetWidth / Mathf.Max(aspect, 0.01f));
        }
        else
        {
            var targetHeight = parentHeight * 0.65f; // 在上一版基础上放大1.3倍，保留上下留白
            rect.sizeDelta = new Vector2(targetHeight * aspect, targetHeight);
        }

        var image = decoration.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        var layoutElement = decoration.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        return image;
    }

    private static Sprite LoadDecorationSprite(string relativePathWithoutExtension)
    {
        var candidates = new[] { ".png", ".jpg", ".jpeg", ".webp" };
        var embeddedSprite = LoadEmbeddedDecorationSprite(relativePathWithoutExtension, candidates);
        if (embeddedSprite != null)
            return embeddedSprite;

        // 以当前 DLL 所在目录为根目录（不是游戏根目录）
        var assemblyPath = typeof(MModUI).Assembly.Location;
        var dllDirectory = System.IO.Path.GetDirectoryName(assemblyPath) ?? string.Empty;

        // 保留工作目录作为兜底，便于开发调试
        var basePaths = new[] { dllDirectory, System.IO.Directory.GetCurrentDirectory() };

        foreach (var basePath in basePaths)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                continue;
            }

            foreach (var ext in candidates)
            {
                var fullPath = System.IO.Path.Combine(basePath, relativePathWithoutExtension + ext);
                if (!System.IO.File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    var bytes = System.IO.File.ReadAllBytes(fullPath);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, bytes))
                    {
                        continue;
                    }

                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MModUI] 加载装饰图失败 {fullPath}: {ex.Message}");
                }
            }
        }

        Debug.LogWarning($"[MModUI] 未找到装饰图(以DLL目录为根): {relativePathWithoutExtension}(.png/.jpg/.jpeg/.webp)");
        return null;
    }

    private static Sprite LoadEmbeddedDecorationSprite(string relativePathWithoutExtension, string[] candidates)
    {
        var assetName = relativePathWithoutExtension
            .Replace('\\', '/')
            .Split('/')
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(assetName))
            return null;

        var assembly = typeof(MModUI).Assembly;
        foreach (var ext in candidates)
        {
            var resourceName = $"EscapeFromDuckovCoopMod.Assets.{assetName}{ext}";
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    continue;

                using var memory = new System.IO.MemoryStream();
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                    continue;

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MModUI] 加载内置装饰图失败 {resourceName}: {ex.Message}");
            }
        }

        return null;
    }

    private void InitializeBlurSource()
    {
        // 在主相机上添加模糊源组件
        var mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("主相机未找到，模糊效果将不可用");
            return;
        }

        var source = mainCamera.GetComponent<TranslucentImageSource>();
        if (source == null)
        {
            source = mainCamera.gameObject.AddComponent<TranslucentImageSource>();
        }

        // 配置模糊参数
        var blurConfig = new ScalableBlurConfig
        {
            Strength = 12f,      // 模糊强度（半径）
            Iteration = 4        // 迭代次数（质量）
        };
        source.BlurConfig = blurConfig;
        source.Downsample = 1;  // 降采样等级（提升性能）
    }

    private void CreateMainPanel()
    {
        // 使用布局构建器创建主面板
        _layoutBuilder.BuildMainPanel(_canvas.transform);

        // Seasonal decorations are only created on Chinese New Year's Eve / New Year.
        CreateSideDecorations();

        // 根据当前传输模式显示对应面板
        UpdateTransportModePanels();
    }

    private void CreatePlayerStatusPanel()
    {
        _components.PlayerStatusPanel = CreateModernPanel("PlayerStatusPanel", _canvas.transform, new Vector2(420, 600), new Vector2(1680, 130));
        MakeDraggable(_components.PlayerStatusPanel);
        _components.PlayerStatusPanel.SetActive(false);

        var layout = _components.PlayerStatusPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 0;
        layout.childForceExpandHeight = false;

        // 标题栏
        var titleBar = CreateTitleBar(_components.PlayerStatusPanel.transform);
        CreateText("Title", titleBar.transform, CoopLocalization.Get("ui.window.playerStatus"), 22, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);

        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(titleBar.transform, false);
        var spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.flexibleWidth = 1;

        CreateIconButton("CloseBtn", titleBar.transform, "x", () =>
        {
            showPlayerStatusWindow = false;
            StartCoroutine(AnimatePanel(_components.PlayerStatusPanel, false));
        }, 36, ModernColors.Error);

        // 内容区域
        var contentArea = CreateContentArea(_components.PlayerStatusPanel.transform);

        // 玩家列表滚动视图
        var scrollView = CreateModernScrollView("PlayerListScroll", contentArea.transform, 450);
        _components.PlayerListContent = scrollView.transform.Find("Viewport/Content");
    }

    private void CreateVotePanel()
    {
        _components.VotePanel = CreateModernPanel("VotePanel", _canvas.transform, new Vector2(VotePanelWidth, VotePanelMinHeight), new Vector2(-1, 0), TextAnchor.UpperCenter);
        if (_components.VotePanel.TryGetComponent<RectTransform>(out var rect))
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = GetVotePanelHiddenPosition(VotePanelMinHeight);
            rect.localScale = Vector3.one * 0.96f;
        }

        var canvasGroup = _components.VotePanel.GetComponent<CanvasGroup>() ?? _components.VotePanel.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        _components.VotePanel.SetActive(false);

        var layout = _components.VotePanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 12);
        layout.spacing = 6;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
    }

    private void CreateSpectatorPanel()
    {
        _components.SpectatorPanel = CreateModernPanel("SpectatorPanel", _canvas.transform, new Vector2(430, 40), new Vector2(-1, -1), TextAnchor.LowerCenter);
        _components.SpectatorPanel.SetActive(false);

        var layout = _components.SpectatorPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(25, 25, 20, 20);

        _components.SpectatorHintText = CreateText("SpectatorHint", _components.SpectatorPanel.transform,
            CoopLocalization.Get("ui.spectator.mode"), 18, ModernColors.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
    }

    private void CreateChatPanel()
    {
        _components.ChatPanel = CreateModernPanel("ChatPanel", _canvas.transform, new Vector2(550, 360), new Vector2(-40, 20), TextAnchor.LowerRight);
        _components.ChatPanel.SetActive(false);

        if (_components.ChatPanel.TryGetComponent<RectTransform>(out var chatRect))
        {
            chatRect.anchorMin = new Vector2(1f, 0f);
            chatRect.anchorMax = new Vector2(1f, 0f);
            chatRect.pivot = new Vector2(1f, 0f);
            chatRect.anchoredPosition = new Vector2(-40f, 20f);
        }

        var layout = _components.ChatPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 6;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Header removed per request (no title/close button)

        var messageArea = new GameObject("MessageArea");
        messageArea.transform.SetParent(_components.ChatPanel.transform, false);
        var messageLayout = messageArea.AddComponent<LayoutElement>();
        messageLayout.preferredHeight = 260f;
        messageLayout.flexibleHeight = 1f;

        var scrollRect = messageArea.AddComponent<ScrollRect>();
        scrollRect.vertical = true;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 40f;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(messageArea.transform, false);
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0, 0);
        viewportRect.anchorMax = new Vector2(1, 1);
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        var mask = viewport.AddComponent<RectMask2D>();
        mask.padding = Vector4.zero;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(14, 10, 10, 12);
        contentLayout.spacing = 10;
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var contentFitter = content.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = content.GetComponent<RectTransform>();
        _components.ChatContent = content.transform;
        _components.ChatScroll = scrollRect;

        var inputRow = new GameObject("InputRow");
        inputRow.transform.SetParent(_components.ChatPanel.transform, false);
        var inputLayout = inputRow.AddComponent<HorizontalLayoutGroup>();
        inputLayout.spacing = 0f;
        inputLayout.childControlHeight = true;
        inputLayout.childControlWidth = true;
        inputLayout.childForceExpandHeight = false;
        inputLayout.childForceExpandWidth = true;
        inputLayout.padding = new RectOffset(0, 0, 0, 0);

        var inputRowLayout = inputRow.AddComponent<LayoutElement>();
        inputRowLayout.preferredHeight = 38f;
        inputRowLayout.minHeight = 38f;

        var inputField = CreateModernInputField("ChatInput", inputRow.transform, CoopLocalization.Get("ui.chat.inputPlaceholder"), string.Empty);
        var inputLayoutElement = inputField.GetComponent<LayoutElement>();
        if (inputLayoutElement == null) inputLayoutElement = inputField.gameObject.AddComponent<LayoutElement>();
        inputLayoutElement.flexibleWidth = 1f;
        inputLayoutElement.preferredHeight = 24f;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.pointSize = 13;
        inputField.onSubmit.AddListener(_ => SubmitChatMessage());
        if (inputField.TryGetComponent<Image>(out var chatInputBg))
            chatInputBg.color = new Color(chatInputBg.color.r, chatInputBg.color.g, chatInputBg.color.b, 1f);
        _components.ChatInput = inputField;

        // 微调聊天面板背景，让内容层看起来更自然
        if (_components.ChatPanel.TryGetComponent<TranslucentImage>(out var translucent))
        {
            translucent.color = new Color(0f, 0f, 0f, 0f);
        }
        else if (_components.ChatPanel.TryGetComponent<Image>(out var image))
        {
            image.color = new Color(image.color.r, image.color.g, image.color.b, 0f);
        }
    }

    #endregion

    #region UI 更新方法

    private void UpdateModeDisplay()
    {
        // 判断是否是活跃的服务器（服务器模式且网络已启动）
        bool isActiveServer = IsServer && networkStarted;
        bool isSteamMode = TransportMode == NetworkTransportMode.SteamP2P;
        bool isInSteamLobby = isSteamMode && LobbyManager != null && LobbyManager.IsInLobby;

        if (_components?.ModeText != null)
        {
            string modeText = CoopLocalization.Get("ui.status.notConnected");
            if (networkStarted)
            {
                if (isSteamMode)
                {
                    modeText = isInSteamLobby ? (LobbyManager.IsHost ? CoopLocalization.Get("ui.mode.server") : CoopLocalization.Get("ui.mode.client")) : CoopLocalization.Get("ui.transport.mode.steam");
                }
                else
                {
                    modeText = IsServer ? CoopLocalization.Get("ui.mode.server") : CoopLocalization.Get("ui.mode.client");
                }
            }
            else if (isSteamMode)
            {
                modeText = CoopLocalization.Get("ui.transport.mode.steam");
            }
            _components.ModeText.text = modeText;

            if (_components.ModeIndicator != null)
                _components.ModeIndicator.color = (isActiveServer || isInSteamLobby) ? ModernColors.Success : ModernColors.Info;
        }

        // 更新模式信息文本
        if (_components?.ModeInfoText != null)
        {
            if (isSteamMode)
            {
                if (isInSteamLobby)
                {
                    if (LobbyManager.IsHost)
                    {
                        var lobbyInfo = LobbyManager.TryGetLobbyInfo(LobbyManager.CurrentLobbyId, out var info) ? (SteamLobbyManager.LobbyInfo?)info : null;
                        if (lobbyInfo != null)
                        {
                            _components.ModeInfoText.text = CoopLocalization.Get("ui.steam.currentLobby", lobbyInfo.Value.LobbyName, lobbyInfo.Value.MemberCount, lobbyInfo.Value.MaxMembers);
                        }
                        else
                        {
                            _components.ModeInfoText.text = CoopLocalization.Get("ui.steam.server.waiting");
                        }
                    }
                    else
                    {
                        _components.ModeInfoText.text = CoopLocalization.Get("ui.steam.client.connected");
                    }
                    _components.ModeInfoText.color = ModernColors.Success;
                }
                else
                {
                    _components.ModeInfoText.text = CoopLocalization.Get("ui.steam.hint.createOrJoin");
                    _components.ModeInfoText.color = ModernColors.TextSecondary;
                }
            }
            else
            {
                if (isActiveServer)
                {
                    int currentPort = NetService.Instance?.port ?? 9050;
                    _components.ModeInfoText.text = CoopLocalization.Get("ui.server.listenPort") + " " + currentPort;
                    _components.ModeInfoText.color = ModernColors.TextSecondary;
                }
                else
                {
                    _components.ModeInfoText.text = CoopLocalization.Get("ui.server.hint.willUsePort", manualPort);
                    _components.ModeInfoText.color = ModernColors.TextSecondary;
                }
            }
        }

        // 更新创建/关闭主机按钮
        if (_components?.ModeToggleButton != null && _components?.ModeToggleButtonText != null)
        {
            if (isSteamMode)
            {
                // Steam模式下隐藏此按钮，因为有独立的控制按钮
                _components.ModeToggleButton.gameObject.SetActive(false);
            }
            else
            {
                _components.ModeToggleButton.gameObject.SetActive(true);
                _components.ModeToggleButtonText.text = isActiveServer ? CoopLocalization.Get("ui.server.close") : CoopLocalization.Get("ui.server.create");

                // 更新按钮颜色
                var image = _components.ModeToggleButton.GetComponent<Image>();
                if (image != null)
                {
                    var baseColor = isActiveServer ? ModernColors.Error : ModernColors.Success;
                    ApplyButtonColors(_components.ModeToggleButton, baseColor);
                }
            }
        }

        // 更新服务器端口显示
        if (_components?.ServerPortText != null)
        {
            if (isSteamMode)
            {
                _components.ServerPortText.text = "Steam P2P";
                _components.ServerPortText.color = isInSteamLobby ? ModernColors.Success : ModernColors.TextSecondary;
            }
            else if (isActiveServer)
            {
                int currentPort = NetService.Instance?.port ?? 9050;
                _components.ServerPortText.text = $"{currentPort}";
                _components.ServerPortText.color = ModernColors.Success;
            }
            else
            {
                _components.ServerPortText.text = manualPort;
                _components.ServerPortText.color = ModernColors.TextSecondary;
            }
        }

        if (_components?.ConnectionCountText != null)
        {
            if (isSteamMode && isInSteamLobby)
            {
                var lobbyInfo = LobbyManager.TryGetLobbyInfo(LobbyManager.CurrentLobbyId, out var info) ? (SteamLobbyManager.LobbyInfo?)info : null;
                var count = lobbyInfo != null ? lobbyInfo.Value.MemberCount - 1 : 0; // 减1因为包含自己
                _components.ConnectionCountText.text = $"{count}";
                _components.ConnectionCountText.color = count > 0 ? ModernColors.Success : ModernColors.TextSecondary;
            }
            else if (isActiveServer)
            {
                var count = netManager?.ConnectedPeerList.Count ?? 0;
                _components.ConnectionCountText.text = $"{count}";
                _components.ConnectionCountText.color = count > 0 ? ModernColors.Success : ModernColors.TextSecondary;
            }
            else
            {
                _components.ConnectionCountText.text = "0";
                _components.ConnectionCountText.color = ModernColors.TextSecondary;
            }
        }

        // 更新 Steam 创建/离开按钮
        if (isSteamMode && _components?.SteamCreateLeaveButton != null && _components?.SteamCreateLeaveButtonText != null)
        {
            bool lobbyActive = LobbyManager != null && LobbyManager.IsInLobby;

            // 更新按钮文本
            _components.SteamCreateLeaveButtonText.text = lobbyActive
                ? CoopLocalization.Get("ui.steam.leaveLobby")
                : CoopLocalization.Get("ui.steam.createHost");

            // 更新按钮颜色
            var buttonImage = _components.SteamCreateLeaveButton.GetComponent<Image>();
            if (buttonImage != null)
            {
                ApplyButtonColors(_components.SteamCreateLeaveButton, lobbyActive ? ModernColors.Error : ModernColors.Success);
            }
        }
    }

    /// <summary>
    /// 统一更新所有状态文本（Direct和Steam模式）
    /// </summary>
    private void SetStatusText(string text, Color color)
    {
        if (_components?.StatusText != null)
        {
            _components.StatusText.text = text;
            _components.StatusText.color = color;
        }
        if (_components?.SteamStatusText != null)
        {
            _components.SteamStatusText.text = text;
            _components.SteamStatusText.color = color;
        }
    }

    private void UpdateConnectionStatus()
    {
        if (Service == null) return;
        if (_components?.StatusText == null && _components?.SteamStatusText == null) return;

        var currentStatus = Service.status;
        var displayStatus = MaskStatusText(currentStatus);

        // 检查状态是否改变
        if (displayStatus != _status)
        {
            _status = displayStatus;

            // 根据状态内容设置颜色
            Color statusColor = ModernColors.TextSecondary;
            string statusIcon = "[*]";

            if (currentStatus.Contains("已连接"))
            {
                statusColor = ModernColors.Success;
                statusIcon = "[OK]";
            }
            else if (currentStatus.Contains("连接中") || currentStatus.Contains("正在连接"))
            {
                statusColor = ModernColors.Info;
                statusIcon = "[*]";
            }
            else if (currentStatus.Contains("断开") || currentStatus.Contains("失败") || currentStatus.Contains("错误"))
            {
                statusColor = ModernColors.Error;
                statusIcon = "[!]";
            }
            else if (currentStatus.Contains("启动"))
            {
                statusColor = ModernColors.Success;
                statusIcon = "[OK]";
            }

            string statusText = $"{statusIcon} {displayStatus}";
            SetStatusText(statusText, statusColor);
        }

        // 如果是客户端且已连接，检查服务端关卡状态
        if (!IsServer && connectedPeer != null && networkStarted)
        {
            CheckServerInGame();
        }
    }

    private float _serverCheckTimer = 0f;
    private const float SERVER_CHECK_INTERVAL = 2f; // 每2秒检查一次

    private void CheckServerInGame()
    {
        _serverCheckTimer += Time.deltaTime;
        if (_serverCheckTimer < SERVER_CHECK_INTERVAL)
            return;

        _serverCheckTimer = 0f;

        // 检查服务端玩家状态
        if (playerStatuses != null && playerStatuses.Count > 0)
        {
            // 获取主机玩家状态
            foreach (var kvp in playerStatuses)
            {
                var hostStatus = kvp.Value;
                if (hostStatus != null && hostStatus.EndPoint.Contains("Host"))
                {
                    // 检查主机是否在游戏中
                    if (!hostStatus.IsInGame)
                    {
                        Debug.LogWarning("服务端不在关卡内，断开连接");

                        SetStatusText("[!] " + CoopLocalization.Get("ui.error.serverNotInGame"), ModernColors.Warning);

                        // 断开连接
                        if (connectedPeer != null)
                        {
                            connectedPeer.Disconnect();
                        }
                        return;
                    }
                    break;
                }
            }
        }
    }

    private void UpdateHostList()
    {
        if (_components?.HostListContent == null || IsServer) return;

        // 清理不存在的主机
        var toRemove = _hostEntries.Keys.Where(h => !hostSet.Contains(h)).ToList();
        foreach (var h in toRemove)
        {
            Destroy(_hostEntries[h]);
            _hostEntries.Remove(h);
        }

        // 添加新主机
        foreach (var host in hostList)
        {
            if (!_hostEntries.ContainsKey(host))
            {
                var entry = CreateHostEntry(host);
                _hostEntries[host] = entry;
            }
        }

        // 显示空列表提示
        if (hostList.Count == 0 && _components.HostListContent.childCount == 0)
        {
            var emptyHint = CreateText("EmptyHint", _components.HostListContent, CoopLocalization.Get("ui.hostList.empty"), 14, ModernColors.TextTertiary, TextAlignmentOptions.Center);
        }
    }

    private GameObject CreateHostEntry(string host)
    {
        // 创建表格样式的服务器条目
        var entry = new GameObject($"Host_{host}");
        entry.transform.SetParent(_components.HostListContent, false);

        var entryLayout = entry.AddComponent<HorizontalLayoutGroup>();
        entryLayout.padding = new RectOffset(20, 20, 15, 15);  // 增加上下内边距：12 -> 15
        entryLayout.spacing = 15;
        entryLayout.childForceExpandWidth = false;
        entryLayout.childControlWidth = true;
        entryLayout.childAlignment = TextAnchor.MiddleLeft;  // 垂直居中对齐

        var bg = entry.AddComponent<Image>();
        StyleCardImage(bg, GlassTheme.CardBg);

        var entryLayoutElement = entry.AddComponent<LayoutElement>();
        entryLayoutElement.preferredHeight = 75;  // 增加高度：60 -> 75
        entryLayoutElement.minHeight = 75;
        entryLayoutElement.flexibleWidth = 1;

        // 悬停效果
        var button = entry.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = GlassTheme.CardBg;
        colors.highlightedColor = new Color(ModernColors.Primary.r, ModernColors.Primary.g, ModernColors.Primary.b, MModUITheme.UseLunarNewYearTheme ? 0.34f : 0.14f);
        colors.pressedColor = new Color(ModernColors.Primary.r, ModernColors.Primary.g, ModernColors.Primary.b, MModUITheme.UseLunarNewYearTheme ? 0.48f : 0.20f);
        button.colors = colors;
        button.targetGraphic = bg;

        var parts = host.Split(':');
        var ip = parts.Length > 0 ? parts[0] : host;
        var portStr = parts.Length > 1 ? parts[1] : "9050";
        var displayIp = GetMaskedHostIp(ip);

        // 服务器图标
        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(entry.transform, false);
        var iconLayout = iconObj.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 40;
        iconLayout.preferredHeight = 40;
        var iconImage = iconObj.AddComponent<Image>();
        iconImage.color = ModernColors.Primary;

        // 服务器信息（左侧）
        var infoArea = new GameObject("InfoArea");
        infoArea.transform.SetParent(entry.transform, false);
        var infoLayout = infoArea.AddComponent<VerticalLayoutGroup>();
        infoLayout.spacing = 6;  // 增加文字行间距：4 -> 6
        infoLayout.childForceExpandHeight = false;
        infoLayout.childControlHeight = false;
        infoLayout.childAlignment = TextAnchor.MiddleLeft;  // 垂直居中对齐
        var infoLayoutElement = infoArea.AddComponent<LayoutElement>();
        infoLayoutElement.preferredWidth = 500;

        CreateText("ServerName", infoArea.transform, CoopLocalization.Get("ui.hostList.lanServer", displayIp), 16, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
        CreateText("ServerDetails", infoArea.transform, CoopLocalization.Get("ui.hostList.serverDetails", portStr), 13, ModernColors.TextSecondary, TextAlignmentOptions.Left);

        // 中间空白
        var spacer = new GameObject("Spacer");
        spacer.transform.SetParent(entry.transform, false);
        var spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.flexibleWidth = 1;

        // 状态标签
        var statusBadge = CreateBadge(entry.transform, CoopLocalization.Get("ui.status.online"), ModernColors.Success);
        statusBadge.GetComponent<LayoutElement>().preferredWidth = 70;

        // 连接按钮
        CreateModernButton("ConnectBtn", entry.transform, CoopLocalization.Get("ui.hostList.connect"), () =>
        {
            // 检查是否在关卡内
            if (!CheckCanConnect())
                return;

            if (parts.Length == 2 && int.TryParse(parts[1], out var p))
            {
                if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
                    NetService.Instance.StartNetwork(false);
                NetService.Instance.ConnectToHost(parts[0], p);
            }
        }, 120, ModernColors.Primary, 45, 16);

        return entry;
    }

    private void UpdateStreamerModeVisuals()
    {
        ApplyIpInputMask();
        RefreshHostListForStreamerMode();
        RefreshPlayerListForStreamerMode();

        // 强制刷新状态文本以应用遮罩
        _status = null;

        if (_components?.StreamerModeToggle != null && _components.StreamerModeToggle.isOn != _streamerMode)
        {
            _components.StreamerModeToggle.SetIsOnWithoutNotify(_streamerMode);
        }
    }

    private void ApplyIpInputMask()
    {
        if (_components?.IpInputField == null)
            return;

        var targetType = _streamerMode ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        if (_components.IpInputField.contentType != targetType)
        {
            _components.IpInputField.contentType = targetType;
            _components.IpInputField.SetTextWithoutNotify(manualIP);
            _components.IpInputField.ForceLabelUpdate();
        }
    }

    private void RefreshHostListForStreamerMode()
    {
        if (_components?.HostListContent == null)
            return;

        foreach (Transform child in _components.HostListContent)
        {
            Destroy(child.gameObject);
        }

        _hostEntries.Clear();
        UpdateHostList();
    }

    private void RefreshPlayerListForStreamerMode()
    {
        if (_components?.PlayerListContent == null)
            return;

        foreach (Transform child in _components.PlayerListContent)
            Destroy(child.gameObject);

        _playerEntries.Clear();
        _displayedPlayerIds.Clear();
        UpdatePlayerList();
    }

    private string GetMaskedHostIp(string ip)
    {
        return _streamerMode ? "*****" : ip;
    }

    private string GetMaskedEndpoint(string endpoint)
    {
        if (!_streamerMode || string.IsNullOrEmpty(endpoint))
            return endpoint;

        if (endpoint.Contains("Host", StringComparison.OrdinalIgnoreCase))
            return "*****";

        // 简单判断是否为IP地址或端口组合
        if (endpoint.Any(char.IsDigit) && (endpoint.Contains('.') || endpoint.Contains(':')))
            return "*****";

        return endpoint;
    }

    private string MaskStatusText(string text)
    {
        if (!_streamerMode || string.IsNullOrEmpty(text))
            return text;

        // 避免在主播模式下泄露IP或端点信息
        string masked = Regex.Replace(text, @"(?:(?:\d{1,3}\.){3}\d{1,3})(?::\d+)?", "*****");
        masked = Regex.Replace(masked, @"\b(?:[\da-fA-F]{0,4}:){2,7}[\da-fA-F]{0,4}(?::\d+)?\b", "*****");

        return masked;
    }

    private void UpdatePlayerList()
    {
        if (_components?.PlayerListContent == null) return;

        var currentPlayerIds = new HashSet<string>();
        var playerStatusesToDisplay = new List<PlayerStatus>();

        if (localPlayerStatus != null)
        {
            currentPlayerIds.Add(localPlayerStatus.EndPoint);
            playerStatusesToDisplay.Add(localPlayerStatus);
        }

        IEnumerable<PlayerStatus> remoteStatuses = IsServer
            ? playerStatuses?.Values
            : clientPlayerStatuses?.Values;

        if (remoteStatuses != null)
        {
            foreach (var status in remoteStatuses)
            {
                if (status == null)
                    continue;

                if (!currentPlayerIds.Contains(status.EndPoint))
                {
                    currentPlayerIds.Add(status.EndPoint);
                    playerStatusesToDisplay.Add(status);
                }
            }
        }

        // 检查是否需要重建UI
        bool needsRebuild = false;

        if (!_displayedPlayerIds.SetEquals(currentPlayerIds))
        {
            needsRebuild = true;
            Debug.Log($"[MModUI] 玩家列表已更新，重建UI (当前: {currentPlayerIds.Count}, 之前: {_displayedPlayerIds.Count})");
        }

        if (!needsRebuild)
            return;

        // 清空现有列表
        foreach (Transform child in _components.PlayerListContent)
            Destroy(child.gameObject);
        _playerEntries.Clear();

        // 更新缓存
        _displayedPlayerIds.Clear();
        foreach (var id in currentPlayerIds)
            _displayedPlayerIds.Add(id);

        // 显示玩家列表
        foreach (var status in playerStatusesToDisplay)
        {
            bool isLocal = (status == localPlayerStatus);
            CreatePlayerEntry(status, isLocal);
        }
    }


    private void CreatePlayerEntry(PlayerStatus status, bool isLocal)
    {
        var entry = CreateModernCard(_components.PlayerListContent, $"Player_{status.EndPoint}");

        // 玩家卡片特殊样式
        var bg = entry.GetComponent<Image>();
        if (bg != null)
        {
            if (isLocal)
            {
                bg.color = new Color(0.24f, 0.52f, 0.98f, 0.15f); // 蓝色半透明
                var outline = entry.AddComponent<Outline>();
                outline.effectColor = ModernColors.Primary;
                outline.effectDistance = new Vector2(2, -2);
            }
        }

        var headerRow = CreateHorizontalGroup(entry.transform, "Header");

        // 状态指示器
        var statusDot = new GameObject("StatusDot");
        statusDot.transform.SetParent(headerRow.transform, false);
        var dotLayout = statusDot.AddComponent<LayoutElement>();
        dotLayout.preferredWidth = 10;
        dotLayout.preferredHeight = 10;
        var dotImage = statusDot.AddComponent<Image>();
        dotImage.color = status.IsInGame ? ModernColors.Success : ModernColors.Warning;

        string displayId = GetMaskedEndpoint(status.EndPoint);
        string displayName = !string.IsNullOrEmpty(status.PlayerName) ? status.PlayerName : displayId;

        var nameText = CreateText("Name", headerRow.transform, displayName, 16, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
        if (isLocal)
        {
            var localBadge = CreateBadge(headerRow.transform, CoopLocalization.Get("ui.playerStatus.local"), ModernColors.Primary);
        }

        CreateDivider(entry.transform);

        var infoRow = CreateHorizontalGroup(entry.transform, "Info");
        CreateText("ID", infoRow.transform, CoopLocalization.Get("ui.playerStatus.id") + ": " + displayId, 13, ModernColors.TextSecondary);

        var pingText = CreateText("Ping", infoRow.transform, $"{status.Latency}ms", 13,
            status.Latency < 50 ? ModernColors.Success :
            status.Latency < 100 ? ModernColors.Warning : ModernColors.Error);

        var stateText = CreateText("State", infoRow.transform, status.IsInGame ? CoopLocalization.Get("ui.playerStatus.inGameStatus") : CoopLocalization.Get("ui.playerStatus.idle"), 13,
            status.IsInGame ? ModernColors.Success : ModernColors.TextSecondary);

        if (IsServer && !isLocal)
        {
            var actionRow = CreateHorizontalGroup(entry.transform, "Actions");
            var layout = actionRow.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.childAlignment = TextAnchor.MiddleRight;
                layout.childForceExpandWidth = false;
            }

            CreateModernButton("KickBtn", actionRow.transform, CoopLocalization.Get("ui.playerlist.kick"), () =>
            {
                Service?.KickPlayer(status.EndPoint);
            }, 90, ModernColors.Error, 36, 14);
        }
    }

    private void UpdateVotePanel()
    {
        if (SceneNet.Instance == null)
        {
            SetVotePanelVisible(false);
            _lastVoteActive = false;
            return;
        }

        bool active = SceneNet.Instance.sceneVoteActive;
        SetVotePanelVisible(active);

        if (!active)
        {
            _lastVoteActive = false;
            return;
        }

        // 检查是否需要重建UI（只有状态改变时才重建）
        bool needsRebuild = false;
        string rebuildReason = "";
        var currentParticipants = new HashSet<string>(SceneNet.Instance.sceneParticipantIds);

        if (_lastVoteActive != active)
        {
            needsRebuild = true;
            rebuildReason = "vote active changed";
        }
        else if (_lastVoteSceneId != SceneNet.Instance.sceneTargetId)
        {
            needsRebuild = true;
            rebuildReason = "target scene changed";
        }
        else if (_lastLocalReady != SceneNet.Instance.localReady)
        {
            needsRebuild = true;
            rebuildReason = "local ready changed";
        }
        else
        {
            // 检查参与者列表是否改变
            if (!_lastVoteParticipants.SetEquals(currentParticipants))
            {
                needsRebuild = true;
                rebuildReason = $"participants changed ({_lastVoteParticipants.Count} -> {currentParticipants.Count})";
            }
        }

        if (!needsRebuild)
        {
            // 即使参与者没变，也检查准备状态（每秒最多更新一次）
            if (Time.time - _lastVoteUpdateTime > 1f)
            {
                needsRebuild = true;
                rebuildReason = "periodic update";
                _lastVoteUpdateTime = Time.time;
            }
        }

        if (!needsRebuild) return;

        //Debug.Log($"[MModUI] 重建投票面板: {rebuildReason}");

        RebuildVotePanelContent();

        // 更新缓存
        _lastVoteActive = active;
        _lastVoteSceneId = SceneNet.Instance.sceneTargetId;
        _lastLocalReady = SceneNet.Instance.localReady;
        _lastVoteParticipants.Clear();
        foreach (var pid in SceneNet.Instance.sceneParticipantIds)
            _lastVoteParticipants.Add(pid);
        _lastVoteUpdateTime = Time.time;
    }

    private void RebuildVotePanelContent()
    {
        if (_components?.VotePanel == null || SceneNet.Instance == null)
            return;

        // 清空并重建投票面板内容（删除所有子对象）
        var childCount = _components.VotePanel.transform.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(_components.VotePanel.transform.GetChild(i).gameObject);
        }

        var sceneName = SceneInfoCollection.GetSceneInfo(SceneNet.Instance.sceneTargetId).DisplayName;
        var readyCount = 0;
        foreach (var pid in SceneNet.Instance.sceneParticipantIds)
        {
            if (SceneNet.Instance.sceneReady.TryGetValue(pid, out var ready) && ready)
                readyCount++;
        }

        var totalCount = Mathf.Max(1, SceneNet.Instance.sceneParticipantIds.Count);
        var listHeight = CalculateVotePlayerListHeight(totalCount);
        var panelHeight = CalculateVotePanelHeight(totalCount);
        if (_components.VotePanel.TryGetComponent<RectTransform>(out var panelRect))
        {
            panelRect.sizeDelta = new Vector2(VotePanelWidth, panelHeight);
            if (!_votePanelVisible && !_components.VotePanel.activeSelf)
            {
                panelRect.anchoredPosition = GetVotePanelHiddenPosition(panelHeight);
            }
        }

        // 标题
        var header = CreateHorizontalGroup(_components.VotePanel.transform, "VoteHeader");
        var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 7;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        var headerSizer = header.GetComponent<LayoutElement>();
        headerSizer.preferredHeight = 26f;
        headerSizer.minHeight = 26f;
        headerSizer.flexibleHeight = 0f;

        CreateVoteIndicator(header.transform, SceneNet.Instance.localReady ? ModernColors.Success : ModernColors.Warning, 8f);

        var titleText = CreateText("VoteTitle", header.transform, CoopLocalization.Get("ui.vote.title"), 16, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
        titleText.enableWordWrapping = false;
        titleText.overflowMode = TextOverflowModes.Ellipsis;

        var countText = CreateText("VoteCount", header.transform, $"{readyCount}/{totalCount}", 13, SceneNet.Instance.localReady ? ModernColors.Success : ModernColors.Warning, TextAlignmentOptions.Right, FontStyles.Bold);
        countText.enableWordWrapping = false;
        var countLayout = countText.gameObject.GetComponent<LayoutElement>();
        countLayout.preferredWidth = 48f;
        countLayout.flexibleWidth = 0f;

        CreateDivider(_components.VotePanel.transform);

        var sceneCard = CreateModernCard(_components.VotePanel.transform, "VoteSceneCard");
        var sceneCardLayout = sceneCard.GetComponent<LayoutElement>();
        sceneCardLayout.preferredHeight = 50f;
        sceneCardLayout.minHeight = 50f;
        sceneCardLayout.flexibleHeight = 0f;
        var sceneCardGroup = sceneCard.GetComponent<VerticalLayoutGroup>();
        sceneCardGroup.padding = new RectOffset(12, 12, 8, 8);
        sceneCardGroup.spacing = 2f;

        var sceneLabel = CreateText("SceneLabel", sceneCard.transform, CoopLocalization.Get("ui.vote.mapVote", sceneName), 10, ModernColors.TextSecondary, TextAlignmentOptions.Left, FontStyles.Bold);
        sceneLabel.enableWordWrapping = false;
        sceneLabel.overflowMode = TextOverflowModes.Ellipsis;
        var sceneText = CreateText("SceneName", sceneCard.transform, sceneName, 15, ModernColors.Primary, TextAlignmentOptions.Left, FontStyles.Bold);
        sceneText.enableWordWrapping = false;
        sceneText.overflowMode = TextOverflowModes.Ellipsis;

        var middleRow = CreateHorizontalGroup(_components.VotePanel.transform, "VoteMiddleRow");
        var middleLayout = middleRow.GetComponent<HorizontalLayoutGroup>();
        middleLayout.spacing = 7;
        middleLayout.childAlignment = TextAnchor.UpperLeft;
        middleLayout.childForceExpandWidth = true;
        var middleSizer = middleRow.GetComponent<LayoutElement>();
        middleSizer.preferredHeight = 54f;
        middleSizer.minHeight = 54f;
        middleSizer.flexibleHeight = 0f;

        // 准备状态卡片
        var readySection = CreateModernCard(_components.VotePanel.transform, "ReadySection");
        readySection.transform.SetParent(middleRow.transform, false);
        var readyLayout = readySection.GetComponent<LayoutElement>();
        if (readyLayout != null)
        {
            readyLayout.preferredHeight = 54f;
            readyLayout.minHeight = 54f;
            readyLayout.flexibleWidth = 1;
            readyLayout.flexibleHeight = 0;
        }
        var readyGroup = readySection.GetComponent<VerticalLayoutGroup>();
        readyGroup.padding = new RectOffset(12, 12, 7, 7);
        readyGroup.spacing = 2f;

        var readyText = CreateText(
            "ReadyStatus",
            readySection.transform,
            SceneNet.Instance.localReady ? CoopLocalization.Get("ui.vote.ready") : CoopLocalization.Get("ui.vote.notReady"),
            14,
            SceneNet.Instance.localReady ? ModernColors.Success : ModernColors.Warning,
            TextAlignmentOptions.Left,
            FontStyles.Bold);
        readyText.enableWordWrapping = false;
        var hint = CreateText("ReadyHint", readySection.transform, CoopLocalization.Get("ui.vote.pressKey", readyKey, SceneNet.Instance.localReady ? CoopLocalization.Get("ui.vote.ready") : CoopLocalization.Get("ui.vote.notReady")), 10, ModernColors.TextTertiary);
        hint.enableWordWrapping = false;
        hint.overflowMode = TextOverflowModes.Ellipsis;

        // 取消投票按钮（只有房主才能看到）
        if (IsServer)
        {
            var cancelButton = CreateModernButton("CancelVote", middleRow.transform,
                CoopLocalization.Get("ui.vote.cancel", "取消投票"),
                OnCancelVote, 92, ModernColors.Error, 54, 11);
        }

        // 玩家列表标题
        var listTitle = CreateText("PlayerListTitle", _components.VotePanel.transform, CoopLocalization.Get("ui.vote.playerReadyStatus"), 11, ModernColors.TextSecondary);
        var listTitleLayout = listTitle.gameObject.GetComponent<LayoutElement>();
        listTitleLayout.flexibleWidth = 0;
        listTitleLayout.preferredWidth = -1;

        var scroll = CreateModernScrollView("VotePlayers", _components.VotePanel.transform, listHeight);
        var content = scroll.transform.Find("Viewport/Content") ?? scroll.transform;

        // 玩家列表
        foreach (var pid in SceneNet.Instance.sceneParticipantIds)
        {
            SceneNet.Instance.sceneReady.TryGetValue(pid, out var ready);
            var playerRow = CreateModernListItem(content, $"Player_{pid}");
            var rowLayout = playerRow.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6;
            rowLayout.padding = new RectOffset(8, 8, 5, 5);
            var rowSize = playerRow.GetComponent<LayoutElement>();
            rowSize.preferredHeight = 30f;
            rowSize.minHeight = 30f;

            CreateVoteIndicator(playerRow.transform, ready ? ModernColors.Success : ModernColors.TextTertiary, 8f);

            // 显示名称和ID
            var displayName = ResolveVotePlayerDisplayName(pid);
            var displayId = StreamerMode ? "*****" : pid;
            var nameText = CreateText("Name", playerRow.transform, displayName, 11, ModernColors.TextPrimary);
            var nameLayout = nameText.gameObject.GetComponent<LayoutElement>();
            nameLayout.flexibleWidth = 1;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;

            var idText = CreateText("ID", playerRow.transform, displayId, 9, ModernColors.TextSecondary, TextAlignmentOptions.Right);
            idText.enableWordWrapping = false;
            idText.overflowMode = TextOverflowModes.Ellipsis;
            var idLayout = idText.gameObject.GetComponent<LayoutElement>();
            idLayout.preferredWidth = 92f;
            idLayout.flexibleWidth = 0f;

            var readyLabel = CreateText("ReadyText", playerRow.transform, ready ? CoopLocalization.Get("ui.vote.ready") : CoopLocalization.Get("ui.vote.notReady"), 10, ready ? ModernColors.Success : ModernColors.TextTertiary, TextAlignmentOptions.Right, FontStyles.Bold);
            readyLabel.enableWordWrapping = false;
            readyLabel.overflowMode = TextOverflowModes.Ellipsis;
            var readyLabelLayout = readyLabel.gameObject.GetComponent<LayoutElement>();
            readyLabelLayout.preferredWidth = 48f;
            readyLabelLayout.flexibleWidth = 0f;
        }
    }

    private string ResolveVotePlayerDisplayName(string pid)
    {
        string displayName = GetMaskedEndpoint(pid);
        var service = NetService.Instance;
        if (service == null)
            return displayName;

        if (service.localPlayerStatus != null && service.localPlayerStatus.EndPoint == pid)
        {
            return string.IsNullOrEmpty(service.localPlayerStatus.PlayerName)
                ? pid
                : service.localPlayerStatus.PlayerName;
        }

        if (service.clientPlayerStatuses != null && service.clientPlayerStatuses.TryGetValue(pid, out var clientStatus))
        {
            return string.IsNullOrEmpty(clientStatus.PlayerName) ? pid : clientStatus.PlayerName;
        }

        if (service.playerStatuses != null)
        {
            foreach (var kv in service.playerStatuses)
            {
                var st = kv.Value;
                if (st != null && st.EndPoint == pid)
                    return string.IsNullOrEmpty(st.PlayerName) ? pid : st.PlayerName;
            }
        }

        return displayName;
    }

    private void CreateVoteIndicator(Transform parent, Color color, float size = 8f)
    {
        var dot = new GameObject("VoteIndicator");
        dot.transform.SetParent(parent, false);

        var layout = dot.AddComponent<LayoutElement>();
        layout.minWidth = size;
        layout.preferredWidth = size;
        layout.minHeight = size;
        layout.preferredHeight = size;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        var rect = dot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(size, size);

        var image = dot.AddComponent<Image>();
        StyleToggleBoxImage(image, color);
        AddControlChrome(dot, new Color(color.r, color.g, color.b, 0.22f), new Color(0f, 0f, 0f, 0.10f), new Vector2(0f, -1f));
    }

    /// <summary>
    /// 取消投票按钮回调
    /// </summary>
    private void OnCancelVote()
    {
        if (SceneNet.Instance == null)
        {
            SetStatusText("[!] 投票系统未初始化", ModernColors.Error);
            return;
        }

        // 只有房主才能取消投票
        if (!IsServer)
        {
            SetStatusText("[!] 只有房主可以取消投票", ModernColors.Error);
            return;
        }

        // 调用取消投票方法
        SceneNet.Instance.CancelVote();
        SetStatusText("[OK] 已取消投票", ModernColors.Success);
        Debug.Log("[MModUI] 房主取消了投票");
    }

    private void UpdateSpectatorPanel()
    {
        if (_components?.SpectatorPanel != null)
        {
            var shouldShow = Spectator.Instance?._spectatorActive ?? false;
            if (_components.SpectatorHintText != null)
                _components.SpectatorHintText.text = CoopLocalization.Get("ui.spectator.mode");

            if (_components.SpectatorPanel.activeSelf != shouldShow)
            {
                if (shouldShow)
                    StartCoroutine(AnimatePanel(_components.SpectatorPanel, true));
                else
                    StartCoroutine(AnimatePanel(_components.SpectatorPanel, false));
            }
        }
    }

    #endregion

    #region Chat

    private void ToggleChatPanel()
    {
        _chatVisible = !_chatVisible;
        _chatAutoHideArmed = false;
        if (_components?.ChatPanel == null)
            return;

        StartCoroutine(AnimatePanel(_components.ChatPanel, _chatVisible));

        if (_chatVisible && _components.ChatInput != null)
        {
            EventSystem.current?.SetSelectedGameObject(_components.ChatInput.gameObject);
            _components.ChatInput.ActivateInputField();
        }
    }

    internal void OnChatMessageReceived(string senderName, string content)
    {
        if (string.IsNullOrEmpty(content))
            return;

        ShowChatTemporarily();

        _chatHistory.Add((senderName ?? string.Empty, content));
        while (_chatHistory.Count > ChatHistoryLimit)
            _chatHistory.RemoveAt(0);

        AppendChatEntry(senderName, content);
    }

    private void AppendChatEntry(string senderName, string content)
    {
        if (_components?.ChatContent == null)
            return;

        var line = new GameObject("ChatLine");
        line.transform.SetParent(_components.ChatContent, false);
        var layout = line.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childAlignment = TextAnchor.UpperLeft;

        var nameText = CreateText("Sender", line.transform, string.IsNullOrEmpty(senderName) ? CoopLocalization.Get("ui.chat.unknown") : senderName, 16, ModernColors.Primary, TextAlignmentOptions.Left, FontStyles.Bold);
        var nameLayout = nameText.gameObject.AddComponent<LayoutElement>();
        nameLayout.preferredWidth = 132f;
        nameLayout.flexibleWidth = 0;

        var contentText = CreateText("Content", line.transform, content, 16, ModernColors.TextPrimary, TextAlignmentOptions.TopLeft);
        contentText.enableWordWrapping = true;
        contentText.richText = true;
        contentText.margin = new Vector4(0, 1, 0, 1);
        contentText.overflowMode = TextOverflowModes.Overflow;
        var contentLayout = contentText.gameObject.AddComponent<LayoutElement>();
        contentLayout.flexibleWidth = 1f;

        if (_components.ChatContent.childCount > ChatHistoryLimit)
        {
            var oldest = _components.ChatContent.GetChild(0);
            Destroy(oldest.gameObject);
        }

        StartCoroutine(LateScrollToBottom());
    }

    private IEnumerator LateScrollToBottom()
    {
        yield return null;
        if (_components?.ChatScroll != null)
            _components.ChatScroll.verticalNormalizedPosition = 0f;
    }

    internal bool IsChatTyping()
    {
        var input = _components?.ChatInput;
        if (input == null) return false;
        if (!input.isActiveAndEnabled) return false;
        if (!input.gameObject.activeInHierarchy) return false;

        try
        {
            return input.isFocused;
        }
        catch
        {
            return false;
        }
    }

    private void ShowChatTemporarily()
    {
        if (_components?.ChatPanel == null)
            return;

        if (!_chatVisible)
        {
            _chatVisible = true;
            StartCoroutine(AnimatePanel(_components.ChatPanel, true));
        }

        _chatAutoHideArmed = true;
        _chatAutoHideDeadline = Time.time + ChatAutoHideDelay;
        if (_chatAutoHideRoutine == null)
            _chatAutoHideRoutine = StartCoroutine(ChatAutoHideRoutine());
    }

    private IEnumerator ChatAutoHideRoutine()
    {
        while (true)
        {
            yield return null;

            if (!_chatAutoHideArmed || !_chatVisible)
            {
                _chatAutoHideRoutine = null;
                yield break;
            }

            var input = _components?.ChatInput;
            var isTyping = input != null && (input.isFocused || !string.IsNullOrEmpty(input.text));

            if (Time.time < _chatAutoHideDeadline)
                continue;

            if (isTyping)
            {
                _chatAutoHideDeadline = Time.time + ChatAutoHideDelay;
                continue;
            }

            _chatAutoHideArmed = false;
            _chatVisible = false;
            StartCoroutine(AnimatePanel(_components.ChatPanel, false));
            _chatAutoHideRoutine = null;
            yield break;
        }
    }

    private void SubmitChatMessage()
    {
        var service = Service;
        if (service == null || _components?.ChatInput == null)
            return;

        var content = _components.ChatInput.text?.Trim();
        if (string.IsNullOrEmpty(content))
            return;

        var senderName = service.ResolveLocalPlayerName();

        if (service.IsServer)
        {
            OnChatMessageReceived(senderName, content);
            CoopTool.SendRpc(new ChatMessageRpc
            {
                SenderId = service.GetSelfNetworkId(),
                SenderName = senderName,
                Content = content
            });
        }
        else
        {
            CoopTool.SendRpc(new ChatSendRequestRpc
            {
                SenderName = senderName,
                Content = content
            });
        }

        _components.ChatInput.text = string.Empty;
        _components.ChatInput.ActivateInputField();

        ShowChatTemporarily();
    }

    #endregion

    #region 现代化UI Helper方法

    internal GameObject CreateModernPanel(string name, Transform parent, Vector2 size, Vector2 anchorPos, TextAnchor pivot = TextAnchor.UpperLeft)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        var rect = panel.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        SetAnchor(rect, anchorPos, pivot);

        var image = panel.AddComponent<Image>();
        StylePanelImage(image, GlassTheme.PanelBg);

        var shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = MModUI.ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -10);

        var outline = panel.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // 用于淡入淡出动画
        panel.AddComponent<CanvasGroup>();

        return panel;
    }

    private IEnumerator InitializeTranslucentImageProperties(TranslucentImage translucentImage)
    {
        // 等待几帧，让 TranslucentImage 完成初始化
        yield return null;
        yield return null;

        if (translucentImage != null && translucentImage.material != null)
        {
            try
            {
                translucentImage.vibrancy = 0.3f;      // 降低色彩饱和度
                translucentImage.brightness = 0.9f;    // 略微变暗
                translucentImage.flatten = 0.5f;       // 扁平化，减少背景干扰
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"TranslucentImage 参数设置失败: {e.Message}");
            }
        }
    }
    private static Sprite CreateEmbeddedNoiseSprite()
    {
        int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

        var rand = new System.Random();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float g = 0.5f + (float)(rand.NextDouble() - 0.5) * 0.12f; // 灰度轻微扰动
                tex.SetPixel(x, y, new Color(g, g, g, 0.83f)); // 几乎不透明

            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite RoundedPanelSprite => _roundedPanelSprite ??= CreateRoundedRectSprite(96, 24, 24);
    private static Sprite RoundedCardSprite => _roundedCardSprite ??= CreateRoundedRectSprite(72, 18, 18);
    private static Sprite RoundedControlSprite => _roundedControlSprite ??= CreateRoundedRectSprite(56, 14, 14);
    private static Sprite RoundedPillSprite => _roundedPillSprite ??= CreateRoundedRectSprite(56, 28, 24);
    private static Sprite RoundedScrollbarSprite => _roundedScrollbarSprite ??= CreateVerticalCapsuleSprite(16, 64, 8, new Vector4(4, 8, 4, 8));
    private static Sprite RoundedCloseGlyphSprite => _roundedCloseGlyphSprite ??= CreateVerticalCapsuleSprite(32, 8, 4, new Vector4(4, 4, 4, 4));
    private static Sprite RoundedToggleSprite => _roundedToggleSprite ??= CreateRoundedRectSprite(44, 8, 8);

    private static void StylePanelImage(Image image, Color color)
    {
        StyleRoundedImage(image, color, RoundedPanelSprite);
    }

    private static void StyleCardImage(Image image, Color color)
    {
        StyleRoundedImage(image, color, RoundedCardSprite);
    }

    internal static void StyleControlImage(Image image, Color color)
    {
        StyleRoundedImage(image, color, RoundedControlSprite);
    }

    internal static void StyleToggleBoxImage(Image image, Color color)
    {
        StyleRoundedImage(image, color, RoundedToggleSprite);
    }

    internal static void StylePillImage(Image image, Color color)
    {
        StyleRoundedImage(image, color, RoundedPillSprite);
    }

    internal static void AddControlChrome(GameObject target, Color? borderColor = null, Color? shadowColor = null, Vector2? shadowDistance = null)
    {
        if (target == null)
            return;

        var outline = target.GetComponent<Outline>() ?? target.AddComponent<Outline>();
        outline.effectColor = borderColor ?? ModernColors.InputBorder;
        outline.effectDistance = new Vector2(1.1f, -1.1f);
        outline.useGraphicAlpha = true;

        Shadow shadow = null;
        foreach (var effect in target.GetComponents<Shadow>())
        {
            if (effect.GetType() == typeof(Shadow))
            {
                shadow = effect;
                break;
            }
        }

        shadow ??= target.AddComponent<Shadow>();
        shadow.effectColor = shadowColor ?? ModernColors.Shadow;
        shadow.effectDistance = shadowDistance ?? new Vector2(0f, -3f);
        shadow.useGraphicAlpha = true;
    }

    internal static void ConfigureCloseButton(Button button, Image background, Transform glyphParent, float size, Color? glyphColor = null)
    {
        var normal = WithAlpha(ModernColors.Error, MModUITheme.IsDarkTheme ? 0.20f : 0.12f);
        var highlighted = WithAlpha(ModernColors.Error, MModUITheme.IsDarkTheme ? 0.34f : 0.22f);
        var pressed = WithAlpha(ModernColors.Error, MModUITheme.IsDarkTheme ? 0.48f : 0.32f);
        var disabled = WithAlpha(ModernColors.Error, 0.08f);
        var resolvedGlyphColor = glyphColor ?? ModernColors.Error;
        var useThemedChrome = false;
        var borderColor = WithAlpha(ModernColors.Error, 0.24f);
        var shadowColor = WithAlpha(ModernColors.Shadow, 0.10f);

        if (MModUITheme.UseLunarNewYearTheme)
        {
            normal = new Color(1f, 0.82f, 0.20f, 0.90f);
            highlighted = new Color(1f, 0.91f, 0.36f, 1f);
            pressed = new Color(0.88f, 0.62f, 0.08f, 1f);
            disabled = new Color(0.72f, 0.46f, 0.08f, 0.32f);
            resolvedGlyphColor = new Color(0.72f, 0.06f, 0.10f, 0.98f);
            borderColor = new Color(1f, 0.96f, 0.58f, 0.58f);
            shadowColor = new Color(0.38f, 0.02f, 0.03f, 0.24f);
            useThemedChrome = true;
        }
        else if (MModUITheme.IsSummerTheme)
        {
            normal = new Color(1f, 1f, 1f, 0.78f);
            highlighted = new Color(1f, 0.93f, 0.36f, 0.92f);
            pressed = new Color(0.92f, 0.78f, 0.14f, 0.96f);
            disabled = new Color(1f, 1f, 1f, 0.30f);
            resolvedGlyphColor = new Color(0.02f, 0.48f, 0.78f, 0.98f);
            borderColor = new Color(1f, 0.84f, 0.22f, 0.52f);
            shadowColor = new Color(0.02f, 0.30f, 0.48f, 0.14f);
            useThemedChrome = true;
        }

        if (background != null)
        {
            StylePillImage(background, normal);
            background.raycastTarget = true;
        }

        if (button != null)
        {
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;

            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = highlighted;
            colors.disabledColor = disabled;
            colors.colorMultiplier = 1.08f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
        }

        if (useThemedChrome && glyphParent != null)
            AddControlChrome(glyphParent.gameObject, borderColor, shadowColor, new Vector2(0f, -2f));

        CreateCloseGlyph(glyphParent, resolvedGlyphColor, size);

        if (glyphParent != null && glyphParent.GetComponent<ButtonHoverAnimator>() == null)
            glyphParent.gameObject.AddComponent<ButtonHoverAnimator>();
    }

    private static bool IsCloseIconButton(string name, string icon)
    {
        return (!string.IsNullOrEmpty(name) && name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0)
               || string.Equals(icon, "x", StringComparison.OrdinalIgnoreCase);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static void CreateCloseGlyph(Transform parent, Color color, float buttonSize)
    {
        if (parent == null)
            return;

        var usesBrightTheme = MModUITheme.UseLunarNewYearTheme || MModUITheme.IsSummerTheme;
        var glyphSize = Mathf.Max(usesBrightTheme ? 13f : 12f, buttonSize * (usesBrightTheme ? 0.46f : 0.42f));
        var strokeHeight = Mathf.Max(usesBrightTheme ? 2.8f : 2.4f, buttonSize * (usesBrightTheme ? 0.09f : 0.075f));
        CreateCloseGlyphStroke(parent, "CloseStrokeA", color, glyphSize, strokeHeight, 45f);
        CreateCloseGlyphStroke(parent, "CloseStrokeB", color, glyphSize, strokeHeight, -45f);
    }

    private static void CreateCloseGlyphStroke(Transform parent, string name, Color color, float width, float height, float rotation)
    {
        var stroke = new GameObject(name);
        stroke.transform.SetParent(parent, false);

        var image = stroke.AddComponent<Image>();
        StyleRoundedImage(image, color, RoundedCloseGlyphSprite);
        image.raycastTarget = false;

        var rect = stroke.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    internal static Scrollbar ConfigureVerticalScrollbar(GameObject scrollbarObj, Color trackColor, Color handleColor)
    {
        if (scrollbarObj.GetComponent<RectTransform>() == null)
            scrollbarObj.AddComponent<RectTransform>();

        var trackImage = scrollbarObj.GetComponent<Image>();
        if (trackImage == null)
            trackImage = scrollbarObj.AddComponent<Image>();
        StyleRoundedImage(trackImage, trackColor, RoundedScrollbarSprite);
        trackImage.raycastTarget = true;

        var slidingArea = new GameObject("Sliding Area");
        slidingArea.transform.SetParent(scrollbarObj.transform, false);
        var slidingRect = slidingArea.AddComponent<RectTransform>();
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = new Vector2(1f, 3f);
        slidingRect.offsetMax = new Vector2(-1f, -3f);

        var handle = new GameObject("Handle");
        handle.transform.SetParent(slidingArea.transform, false);
        var handleRect = handle.AddComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;

        var handleImage = handle.AddComponent<Image>();
        StyleRoundedImage(handleImage, handleColor, RoundedScrollbarSprite);
        handleImage.raycastTarget = true;

        var scrollbar = scrollbarObj.GetComponent<Scrollbar>();
        if (scrollbar == null)
            scrollbar = scrollbarObj.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handleRect;

        var colors = scrollbar.colors;
        colors.normalColor = handleColor;
        colors.highlightedColor = BlendColor(AdjustColor(handleColor, 1.08f), ModernColors.PrimaryHover, 0.18f);
        colors.pressedColor = BlendColor(AdjustColor(handleColor, 0.78f), ModernColors.PrimaryActive, 0.22f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(handleColor.r, handleColor.g, handleColor.b, 0.25f);
        colors.fadeDuration = 0.06f;
        scrollbar.colors = colors;

        return scrollbar;
    }

    private static void StyleRoundedImage(Image image, Color color, Sprite sprite)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
    }

    private static Sprite CreateRoundedRectSprite(int size, int radius, int border)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var half = size * 0.5f;
        var inner = half - radius - 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var px = Mathf.Abs(x + 0.5f - half);
                var py = Mathf.Abs(y + 0.5f - half);
                var dx = Mathf.Max(px - inner, 0f);
                var dy = Mathf.Max(py - inner, 0f);
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01(radius + 0.5f - dist);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        return Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
    }

    private static Sprite CreateVerticalCapsuleSprite(int width, int height, int radius, Vector4 border)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        var innerX = halfWidth - radius - 0.5f;
        var innerY = halfHeight - radius - 0.5f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var px = Mathf.Abs(x + 0.5f - halfWidth);
                var py = Mathf.Abs(y + 0.5f - halfHeight);
                var dx = Mathf.Max(px - innerX, 0f);
                var dy = Mathf.Max(py - innerY, 0f);
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01(radius + 0.5f - dist);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        return Sprite.Create(
            tex,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);
    }

    internal GameObject CreateTitleBar(Transform parent)
    {
        var titleBar = CreateHorizontalGroup(parent, "TitleBar");
        var layout = titleBar.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(22, 18, 12, 12);
        layout.spacing = 14;

        var bg = titleBar.AddComponent<Image>();
        StyleCardImage(bg, new Color(GlassTheme.CardBg.r, GlassTheme.CardBg.g, GlassTheme.CardBg.b, MModUITheme.UseLunarNewYearTheme ? GlassTheme.CardBg.a : 0.42f));

        var outline = titleBar.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        var shadow = titleBar.AddComponent<Shadow>();
        shadow.effectColor = ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -5);

        var layoutElement = titleBar.GetComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;
        layoutElement.preferredHeight = 58;
        layoutElement.minHeight = 58;
        layoutElement.flexibleHeight = 0;  // 不占据额外垂直空间

        return titleBar;
    }


    private GameObject CreateContentArea(Transform parent)
    {
        var content = new GameObject("ContentArea");
        content.transform.SetParent(parent, false);

        var rect = content.AddComponent<RectTransform>();

        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 15, 20);
        layout.spacing = 15;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;

        var image = content.AddComponent<Image>();
        StyleCardImage(image, GlassTheme.CardBg);

        var outline = content.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var layoutElement = content.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;
        layoutElement.flexibleHeight = 1;

        return content;
    }


    internal GameObject CreateModernCard(Transform parent, string name)
    {
        var card = new GameObject(name);
        card.transform.SetParent(parent, false);

        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 10;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;

        var image = card.AddComponent<Image>();
        StyleCardImage(image, GlassTheme.CardBg);

        var outline = card.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = card.AddComponent<Shadow>();
        shadow.effectColor = ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -6);

        var layoutElement = card.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;

        return card;
    }


    private GameObject CreateModernListItem(Transform parent, string name)
    {
        var item = new GameObject(name);
        item.transform.SetParent(parent, false);

        var layout = item.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var image = item.AddComponent<Image>();
        StyleCardImage(image, GlassTheme.CardBg);

        var layoutElem = item.AddComponent<LayoutElement>();
        layoutElem.preferredHeight = 44;
        layoutElem.flexibleWidth = 1;

        return item;
    }


    internal void CreateSectionHeader(Transform parent, string text)
    {
        var header = CreateText("SectionHeader", parent, text, 15, GlassTheme.TextSecondary, TextAlignmentOptions.Left, FontStyles.Bold);
        var layoutElement = header.gameObject.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = 26;
        layoutElement.minHeight = 26;

        // 加一条轻微分割线（玻璃风格下的柔光线）
        var divider = new GameObject("Divider");
        divider.transform.SetParent(parent, false);
        var img = divider.AddComponent<Image>();
        img.color = GlassTheme.Divider;

        var rect = divider.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.sizeDelta = new Vector2(0, 1);
    }


    internal TMP_Text CreateInfoRow(Transform parent, string label, string value)
    {
        var row = CreateHorizontalGroup(parent, $"InfoRow_{label}");
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 10;

        var labelText = CreateText("Label", row.transform, label, 14, GlassTheme.TextSecondary);
        var labelLayout = labelText.gameObject.GetComponent<LayoutElement>();
        labelLayout.preferredWidth = 70;
        labelLayout.flexibleWidth = 0;

        var valueText = CreateText("Value", row.transform, value, 14, GlassTheme.Text, TextAlignmentOptions.Left, FontStyles.Bold);

        return valueText;
    }


    internal GameObject CreateStatusBar(Transform parent)
    {
        var bar = new GameObject("StatusBar");
        bar.transform.SetParent(parent, false);

        var layout = bar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(15, 15, 10, 10);
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var bg = bar.AddComponent<Image>();
        StyleCardImage(bg, GlassTheme.CardBg);

        var outline = bar.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = bar.AddComponent<Shadow>();
        shadow.effectColor = ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -5);

        var layoutElement = bar.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;
        layoutElement.preferredHeight = 42;
        layoutElement.minHeight = 42;
        layoutElement.flexibleHeight = 0;  // 不占据额外垂直空间

        return bar;
    }


    private GameObject CreateActionBar(Transform parent)
    {
        var bar = CreateHorizontalGroup(parent, "ActionBar");
        var layout = bar.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(15, 15, 8, 8);
        layout.spacing = 10;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        var bg = bar.AddComponent<Image>();
        StyleCardImage(bg, GlassTheme.CardBg);

        var outline = bar.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = bar.AddComponent<Shadow>();
        shadow.effectColor = ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -5);

        var layoutElement = bar.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = 48;
        layoutElement.minHeight = 48;

        return bar;
    }


    private GameObject CreateHintBar(Transform parent)
    {
        var bar = new GameObject("HintBar");
        bar.transform.SetParent(parent, false);

        var layout = bar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 12, 12);
        layout.childAlignment = TextAnchor.MiddleCenter;

        var bg = bar.AddComponent<Image>();
        StyleCardImage(bg, GlassTheme.CardBg);

        var outline = bar.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var layoutElement = bar.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;
        layoutElement.preferredHeight = 42;
        layoutElement.minHeight = 42;

        return bar;
    }


    private GameObject CreateBadge(Transform parent, string text, Color color)
    {
        var badge = new GameObject("Badge");
        badge.transform.SetParent(parent, false);

        var layoutElement = badge.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 20;
        layoutElement.preferredWidth = 50;

        var bg = badge.AddComponent<Image>();
        StylePillImage(bg, new Color(color.r, color.g, color.b, MModUITheme.UseLunarNewYearTheme ? 0.20f : 0.13f));

        var badgeText = CreateText("Text", badge.transform, text, 11, color, TextAlignmentOptions.Center, FontStyles.Bold);

        return badge;
    }

    private void CreateDivider(Transform parent)
    {
        var divider = new GameObject("Divider");
        divider.transform.SetParent(parent, false);

        var layoutElement = divider.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 1;
        layoutElement.flexibleWidth = 1;

        var image = divider.AddComponent<Image>();
        image.color = ModernColors.Divider;
    }

    private void SetAnchor(RectTransform rect, Vector2 anchorPos, TextAnchor pivot)
    {
        if (anchorPos.x < 0) // 居中模式
        {
            rect.anchorMin = new Vector2(0.5f, anchorPos.y < 0 ? 0 : 1);
            rect.anchorMax = new Vector2(0.5f, anchorPos.y < 0 ? 0 : 1);
            rect.pivot = new Vector2(0.5f, anchorPos.y < 0 ? 0 : 1);
            rect.anchoredPosition = new Vector2(0, anchorPos.y < 0 ? 50 : -50);
        }
        else // 左上角锚点模式
        {
            // 锚点设置为左上角
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            // anchorPos 是从左上角的偏移量，Y需要取反（Unity UI Y轴向下为正）
            rect.anchoredPosition = new Vector2(anchorPos.x, -anchorPos.y);
        }
    }

    private GameObject CreateSection(Transform parent, string name)
    {
        return CreateModernCard(parent, name);
    }

    internal GameObject CreateHorizontalGroup(Transform parent, string name)
    {
        var group = new GameObject(name);
        group.transform.SetParent(parent, false);

        var layout = group.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var layoutElement = group.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;

        return group;
    }

    internal TMP_Text CreateText(string name, Transform parent, string text, int fontSize = 16, Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
    {
        var textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        var tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.fontSize = fontSize;
        tmpText.color = color ?? ModernColors.TextPrimary;
        tmpText.alignment = alignment;
        tmpText.fontStyle = style;
        tmpText.enableWordWrapping = true;

        var fitter = textObj.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var layoutElement = textObj.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;

        return tmpText;
    }

    internal Button CreateModernButton(string name, Transform parent, string text, UnityEngine.Events.UnityAction onClick, float width = -1, Color? color = null, float height = 40, int fontSize = 15)
    {
        var btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        var layout = btnObj.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        if (width > 0) layout.preferredWidth = width;
        else layout.flexibleWidth = 1;

        var baseColor = color ?? GlassTheme.ButtonBg;
        var image = btnObj.AddComponent<Image>();
        StyleControlImage(image, baseColor);
        AddControlChrome(btnObj, GlassTheme.Divider, ModernColors.Shadow, new Vector2(0f, -3f));

        var button = btnObj.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        button.targetGraphic = image;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = GetReadableButtonTextColor(baseColor);
        tmp.fontStyle = FontStyles.Bold;

        var rect = tmp.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        ApplyButtonColors(button, baseColor);
        btnObj.AddComponent<ButtonHoverAnimator>();
        return button;
    }

    private static void ApplyButtonColors(Button button, Color baseColor)
    {
        if (button == null)
            return;

        var image = button.GetComponent<Image>();
        if (image != null)
            StyleControlImage(image, baseColor);

        var colors = button.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = MModUITheme.UseLunarNewYearTheme
            ? AdjustColor(baseColor, 1.18f)
            : BlendColor(AdjustColor(baseColor, 1.06f), ModernColors.PrimaryHover, 0.28f);
        colors.pressedColor = MModUITheme.UseLunarNewYearTheme
            ? AdjustColor(baseColor, 0.66f)
            : BlendColor(AdjustColor(baseColor, 0.72f), ModernColors.PrimaryActive, 0.44f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.18f, 0.20f, 0.24f, 0.45f);
        colors.colorMultiplier = 1.06f;
        colors.fadeDuration = 0.06f;
        button.colors = colors;

        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.color = GetReadableButtonTextColor(baseColor);
    }

    private static Color AdjustColor(Color color, float factor)
    {
        return new Color(
            Mathf.Clamp01(color.r * factor),
            Mathf.Clamp01(color.g * factor),
            Mathf.Clamp01(color.b * factor),
            color.a);
    }

    private static Color BlendColor(Color a, Color b, float t)
    {
        return new Color(
            Mathf.Lerp(a.r, b.r, t),
            Mathf.Lerp(a.g, b.g, t),
            Mathf.Lerp(a.b, b.b, t),
            Mathf.Max(a.a, b.a * 0.75f));
    }

    private static Color GetReadableButtonTextColor(Color background)
    {
        var luminance = 0.2126f * background.r + 0.7152f * background.g + 0.0722f * background.b;
        return luminance > 0.58f
            ? new Color(0.08f, 0.09f, 0.11f, 0.98f)
            : GlassTheme.ButtonText;
    }


    internal Button CreateIconButton(string name, Transform parent, string icon, UnityEngine.Events.UnityAction onClick, float size = 32, Color? color = null)
    {
        var btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        var layoutElement = btnObj.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = size;
        layoutElement.preferredHeight = size;

        var btnColor = color ?? GlassTheme.ButtonBg;
        var isCloseButton = IsCloseIconButton(name, icon);
        var image = btnObj.AddComponent<Image>();
        if (!isCloseButton)
            StylePillImage(image, new Color(1f, 1f, 1f, MModUITheme.UseLunarNewYearTheme ? 0f : 0.24f));

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        if (isCloseButton)
        {
            AddControlChrome(btnObj, WithAlpha(ModernColors.Error, 0.24f), WithAlpha(ModernColors.Shadow, 0.10f), new Vector2(0f, -2f));
            ConfigureCloseButton(button, image, btnObj.transform, size, btnColor);
            return button;
        }

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        var tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = icon;
        tmpText.fontSize = (int)(size * 0.6f);
        tmpText.color = btnColor;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.fontStyle = FontStyles.Bold;

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        var colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, 0);
        colors.highlightedColor = new Color(1, 1, 1, 0.1f);
        colors.pressedColor = new Color(1, 1, 1, 0.2f);
        colors.disabledColor = new Color(1, 1, 1, 0);
        button.colors = colors;

        return button;
    }

    internal Toggle CreateModernToggle(string name, Transform parent, bool defaultValue)
    {
        var toggleObj = new GameObject(name);
        toggleObj.transform.SetParent(parent, false);

        var layout = toggleObj.AddComponent<LayoutElement>();
        layout.preferredWidth = 24;
        layout.preferredHeight = 24;

        var backgroundObj = new GameObject("Background");
        backgroundObj.transform.SetParent(toggleObj.transform, false);
        var bgImage = backgroundObj.AddComponent<Image>();
        StyleToggleBoxImage(bgImage, GlassTheme.InputBg);
        AddControlChrome(backgroundObj, ModernColors.InputBorder, ModernColors.Shadow, new Vector2(0f, -2f));

        var checkmarkObj = new GameObject("Checkmark");
        checkmarkObj.transform.SetParent(backgroundObj.transform, false);
        var checkImage = checkmarkObj.AddComponent<Image>();
        StyleToggleBoxImage(checkImage, ModernColors.Primary);
        checkImage.color = ModernColors.Primary;

        var toggle = toggleObj.AddComponent<Toggle>();
        toggle.targetGraphic = bgImage;
        toggle.graphic = checkImage;
        toggle.isOn = defaultValue;

        var bgRect = backgroundObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        var checkRect = checkmarkObj.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.25f, 0.25f);
        checkRect.anchorMax = new Vector2(0.75f, 0.75f);
        checkRect.sizeDelta = Vector2.zero;

        var colors = toggle.colors;
        colors.normalColor = GlassTheme.ButtonBg;
        colors.highlightedColor = GlassTheme.ButtonHover;
        colors.pressedColor = GlassTheme.ButtonActive;
        colors.selectedColor = GlassTheme.ButtonBg;
        toggle.colors = colors;

        return toggle;
    }

    internal TMP_InputField CreateModernInputField(string name, Transform parent, string placeholder, string defaultValue)
    {
        var inputObj = new GameObject(name);
        inputObj.transform.SetParent(parent, false);

        var layout = inputObj.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1;
        layout.preferredHeight = 35;
        layout.minHeight = 35;

        var image = inputObj.AddComponent<Image>();
        StyleControlImage(image, GlassTheme.InputBg);
        AddControlChrome(inputObj, ModernColors.InputBorder, ModernColors.Shadow, new Vector2(0f, -2f));

        var input = inputObj.AddComponent<TMP_InputField>();

        var textArea = new GameObject("TextArea");
        textArea.transform.SetParent(inputObj.transform, false);
        var rect = textArea.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12, 5);
        rect.offsetMax = new Vector2(-12, -5);
        textArea.AddComponent<RectMask2D>();

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        var tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.color = GlassTheme.Text;
        tmpText.fontSize = 15;
        tmpText.alignment = TextAlignmentOptions.MidlineLeft;
        tmpText.enableWordWrapping = false;
        tmpText.overflowMode = TextOverflowModes.Ellipsis;
        var textRect = tmpText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        var placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.color = GlassTheme.TextSecondary;
        placeholderText.fontSize = 15;
        placeholderText.fontStyle = FontStyles.Italic;
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        placeholderText.enableWordWrapping = false;
        placeholderText.overflowMode = TextOverflowModes.Ellipsis;
        var placeholderRect = placeholderText.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        input.textViewport = rect;
        input.textComponent = tmpText;
        input.placeholder = placeholderText;
        input.text = defaultValue;
        input.lineType = TMP_InputField.LineType.SingleLine;

        return input;
    }


    internal GameObject CreateModernScrollView(string name, Transform parent, float height)
    {
        var scrollObj = new GameObject(name);
        scrollObj.transform.SetParent(parent, false);

        var scrollRect = scrollObj.AddComponent<RectTransform>();
        var layoutElement = scrollObj.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = height;
        layoutElement.minHeight = height;
        layoutElement.flexibleHeight = 0;

        // === 背景：伪毛玻璃 ===
        var scrollImage = scrollObj.AddComponent<Image>();
        StyleCardImage(scrollImage, GlassTheme.CardBg);

        // 柔光边缘
        var outline = scrollObj.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = scrollObj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.25f);
        shadow.effectDistance = new Vector2(0, -3);

        // === ScrollRect ===
        var scroll = scrollObj.AddComponent<ScrollRect>();

        // --- Viewport ---
        var viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(scrollObj.transform, false);
        var viewportRect = viewportObj.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportRect.offsetMin = new Vector2(5, 5);
        viewportRect.offsetMax = new Vector2(-5, -5);

        var viewportImage = viewportObj.AddComponent<Image>();
        StyleCardImage(viewportImage, GlassTheme.ViewportBg);

        var viewportMask = viewportObj.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        // --- Content ---
        var contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);
        var contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 0);

        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8;
        contentLayout.padding = new RectOffset(8, 8, 8, 8);
        contentLayout.childForceExpandHeight = false;

        var contentFitter = contentObj.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // --- Scroll config ---
        scroll.content = contentRect;
        scroll.viewport = viewportRect;
        scroll.horizontal = false;
        scroll.vertical = true;

        // 可选：增加滚动条
        var scrollbarObj = new GameObject("Scrollbar");
        scrollbarObj.transform.SetParent(scrollObj.transform, false);
        var scrollbarRect = scrollbarObj.AddComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1, 0);
        scrollbarRect.anchorMax = new Vector2(1, 1);
        scrollbarRect.pivot = new Vector2(1, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(10, 0);

        var scrollbar = ConfigureVerticalScrollbar(
            scrollbarObj,
            new Color(1f, 1f, 1f, MModUITheme.IsDarkTheme ? 0.06f : 0.18f),
            new Color(ModernColors.Primary.r, ModernColors.Primary.g, ModernColors.Primary.b, 0.74f));

        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        return scrollObj;
    }


    internal void MakeDraggable(GameObject panel)
    {
        var dragger = panel.AddComponent<UIDragger>();
    }

    // 创建Steam服务器列表UI（左侧）
    internal void CreateSteamServerListUI(Transform parent, MModUIComponents components)
    {
        // Steam房间列表标题栏
        var steamHeader = CreateModernCard(parent, "SteamHeader");
        var steamHeaderLayout = steamHeader.GetComponent<LayoutElement>();
        steamHeaderLayout.preferredHeight = 60;
        steamHeaderLayout.minHeight = 60;
        steamHeaderLayout.flexibleHeight = 0;

        var steamHeaderGroup = CreateHorizontalGroup(steamHeader.transform, "HeaderGroup");
        steamHeaderGroup.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 0, 0, 0);

        CreateText("ListTitle", steamHeaderGroup.transform, CoopLocalization.Get("ui.steam.lobbyList"), 20, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);

        var headerSpacer = new GameObject("Spacer");
        headerSpacer.transform.SetParent(steamHeaderGroup.transform, false);
        var headerSpacerLayout = headerSpacer.AddComponent<LayoutElement>();
        headerSpacerLayout.flexibleWidth = 1;

        var searchInput = CreateModernInputField("SteamLobbySearch", steamHeaderGroup.transform, CoopLocalization.Get("ui.steam.searchPlaceholder"), _steamLobbySearchTerm);
        var searchLayout = searchInput.GetComponent<LayoutElement>();
        if (searchLayout != null)
        {
            searchLayout.preferredWidth = 220;
            searchLayout.minWidth = 200;
        }
        searchInput.onValueChanged.AddListener(value =>
        {
            _steamLobbySearchTerm = value;
            UpdateSteamLobbyList();
        });

        // 刷新按钮
        CreateModernButton("RefreshBtn", steamHeaderGroup.transform, CoopLocalization.Get("ui.steam.refresh"), () =>
        {
            if (LobbyManager != null) LobbyManager.RequestLobbyList();
        }, 120, ModernColors.Primary, 38, 15);

        // Steam房间列表滚动视图
        var lobbyScroll = CreateModernScrollView("SteamLobbyScroll", parent, 400);
        var scrollLayout = lobbyScroll.GetComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1;
        components.SteamLobbyListContent = lobbyScroll.transform.Find("Viewport/Content");

        // 加入密码输入区域
        var passCard = CreateModernCard(parent, "JoinPassCard");
        var passCardLayout = passCard.GetComponent<LayoutElement>();
        passCardLayout.preferredHeight = 70;
        passCardLayout.minHeight = 70;

        var passRow = CreateHorizontalGroup(passCard.transform, "JoinPassRow");
        CreateText("JoinPassLabel", passRow.transform, CoopLocalization.Get("ui.steam.joinPassword"), 14, GlassTheme.TextSecondary).gameObject.GetComponent<LayoutElement>().preferredWidth = 80;
        var joinPassInput = CreateModernInputField("JoinPass", passRow.transform, CoopLocalization.Get("ui.steam.joinPasswordPlaceholder"), _steamJoinPassword);
        joinPassInput.contentType = TMP_InputField.ContentType.Password;
        joinPassInput.onValueChanged.AddListener(value => _steamJoinPassword = value);

        // Steam模式状态栏
        var steamStatusBar = CreateStatusBar(parent);
        _components.SteamStatusText = CreateText("SteamStatus", steamStatusBar.transform, $"[*] {status}", 14, ModernColors.TextSecondary);

        var steamStatusSpacer = new GameObject("Spacer");
        steamStatusSpacer.transform.SetParent(steamStatusBar.transform, false);
        var steamStatusSpacerLayout = steamStatusSpacer.AddComponent<LayoutElement>();
        steamStatusSpacerLayout.flexibleWidth = 1;

        CreateText("Hint", steamStatusBar.transform, CoopLocalization.Get("ui.hint.toggleUI", "="), 12, ModernColors.TextTertiary, TextAlignmentOptions.Right);
    }

    // 创建Steam控制面板（右侧）
    internal void CreateSteamControlPanel(Transform parent)
    {
        var controlCard = CreateModernCard(parent, "SteamControlCard");
        var controlLayout = controlCard.GetComponent<LayoutElement>();
        controlLayout.flexibleHeight = 1;

        CreateSectionHeader(controlCard.transform, CoopLocalization.Get("ui.steam.lobbySettings"));

        // 房间名称
        var nameRow = CreateHorizontalGroup(controlCard.transform, "NameRow");
        CreateText("NameLabel", nameRow.transform, CoopLocalization.Get("ui.steam.lobbyName"), 14, GlassTheme.TextSecondary).gameObject.GetComponent<LayoutElement>().preferredWidth = 70;
        var nameInput = CreateModernInputField("LobbyName", nameRow.transform, CoopLocalization.Get("ui.steam.lobbyNamePlaceholder"), _steamLobbyName);
        nameInput.onValueChanged.AddListener(value =>
        {
            _steamLobbyName = value;
            UpdateLobbyOptionsFromUI();
        });

        // 房间密码
        var passRow = CreateHorizontalGroup(controlCard.transform, "PassRow");
        CreateText("PassLabel", passRow.transform, CoopLocalization.Get("ui.steam.lobbyPassword"), 14, GlassTheme.TextSecondary).gameObject.GetComponent<LayoutElement>().preferredWidth = 70;
        var passInput = CreateModernInputField("LobbyPass", passRow.transform, CoopLocalization.Get("ui.steam.lobbyPasswordPlaceholder"), _steamLobbyPassword);
        passInput.contentType = TMP_InputField.ContentType.Password;
        passInput.onValueChanged.AddListener(value =>
        {
            _steamLobbyPassword = value;
            UpdateLobbyOptionsFromUI();
        });

        // 可见性
        var visRow = CreateHorizontalGroup(controlCard.transform, "VisRow");
        CreateText("VisLabel", visRow.transform, CoopLocalization.Get("ui.steam.visibility"), 14, GlassTheme.TextSecondary).gameObject.GetComponent<LayoutElement>().preferredWidth = 70;

        var visButtons = CreateHorizontalGroup(visRow.transform, "VisButtons");
        visButtons.GetComponent<HorizontalLayoutGroup>().spacing = 5;
        CreateModernButton("Public", visButtons.transform, CoopLocalization.Get("ui.steam.visibility.public"), () =>
        {
            _steamLobbyFriendsOnly = false;
            UpdateLobbyOptionsFromUI();
        }, 90, _steamLobbyFriendsOnly ? GlassTheme.ButtonBg : ModernColors.Primary, 35, 13);

        CreateModernButton("Friends", visButtons.transform, CoopLocalization.Get("ui.steam.visibility.friends"), () =>
        {
            _steamLobbyFriendsOnly = true;
            UpdateLobbyOptionsFromUI();
        }, 90, _steamLobbyFriendsOnly ? ModernColors.Primary : GlassTheme.ButtonBg, 35, 13);

        // 最大玩家数
        var maxRow = CreateHorizontalGroup(controlCard.transform, "MaxRow");
        CreateText("MaxLabel", maxRow.transform, CoopLocalization.Get("ui.steam.maxPlayers.label"), 14, GlassTheme.TextSecondary).gameObject.GetComponent<LayoutElement>().preferredWidth = 70;

        var maxButtons = CreateHorizontalGroup(maxRow.transform, "MaxButtons");
        maxButtons.GetComponent<HorizontalLayoutGroup>().spacing = 5;
        CreateModernButton("Minus", maxButtons.transform, "-", () =>
        {
            _steamLobbyMaxPlayers = Mathf.Max(2, _steamLobbyMaxPlayers - 1);
            if (_components.SteamMaxPlayersText != null)
                _components.SteamMaxPlayersText.text = _steamLobbyMaxPlayers.ToString();
            UpdateLobbyOptionsFromUI();
        }, 35, GlassTheme.ButtonBg, 35, 13);

        _components.SteamMaxPlayersText = CreateText("MaxValue", maxButtons.transform, _steamLobbyMaxPlayers.ToString(), 14, ModernColors.TextPrimary);
        _components.SteamMaxPlayersText.gameObject.GetComponent<LayoutElement>().preferredWidth = 40;

        CreateModernButton("Plus", maxButtons.transform, "+", () =>
        {
            _steamLobbyMaxPlayers = Mathf.Min(16, _steamLobbyMaxPlayers + 1);
            if (_components.SteamMaxPlayersText != null)
                _components.SteamMaxPlayersText.text = _steamLobbyMaxPlayers.ToString();
            UpdateLobbyOptionsFromUI();
        }, 35, GlassTheme.ButtonBg, 35, 13);

        CreateDivider(controlCard.transform);

        // 创建/离开按钮 - 保存引用以便动态更新
        var isInLobby = LobbyManager != null && LobbyManager.IsInLobby;
        _components.SteamCreateLeaveButton = CreateModernButton("CreateLobby", controlCard.transform,
            isInLobby ? CoopLocalization.Get("ui.steam.leaveLobby") : CoopLocalization.Get("ui.steam.createHost"),
            OnSteamCreateOrLeave, -1, isInLobby ? ModernColors.Error : ModernColors.Success, 45, 16);

        // 保存按钮文本引用
        _components.SteamCreateLeaveButtonText = _components.SteamCreateLeaveButton.GetComponentInChildren<TextMeshProUGUI>();
    }

    private void UpdateTransportModePanels()
    {
        // 更新右侧面板
        if (_components?.DirectModePanel != null && _components?.SteamModePanel != null)
        {
            _components.DirectModePanel.SetActive(TransportMode == NetworkTransportMode.Direct);
            _components.SteamModePanel.SetActive(TransportMode == NetworkTransportMode.SteamP2P);
        }

        // 更新左侧列表区域
        if (_components != null)
        {
            if (_components.DirectServerListArea != null)
                _components.DirectServerListArea.SetActive(TransportMode == NetworkTransportMode.Direct);
            if (_components.SteamServerListArea != null)
                _components.SteamServerListArea.SetActive(TransportMode == NetworkTransportMode.SteamP2P);
        }
    }


    private void UpdateLobbyOptionsFromUI()
    {
        if (Service == null) return;

        var maxPlayers = Mathf.Clamp(_steamLobbyMaxPlayers, 2, 16);
        _steamLobbyMaxPlayers = maxPlayers;

        var options = new SteamLobbyOptions
        {
            LobbyName = _steamLobbyName,
            Password = _steamLobbyPassword,
            Visibility = _steamLobbyFriendsOnly ? SteamLobbyVisibility.FriendsOnly : SteamLobbyVisibility.Public,
            MaxPlayers = maxPlayers
        };

        Service.ConfigureLobbyOptions(options);
    }

    #endregion

    #region 事件处理

    internal void OnToggleServerMode()
    {
        // 判断当前是否是活跃的服务器
        bool isActiveServer = IsServer && networkStarted;

        if (isActiveServer)
        {
            // 关闭主机 - 完全停止网络
            NetService.Instance.StopNetwork();

            SetStatusText("[OK] " + CoopLocalization.Get("ui.server.closed"), ModernColors.Info);

            Debug.Log("主机已关闭，网络已完全停止");
        }
        else
        {
            // 创建主机 - 使用下方连接区域的端口
            if (int.TryParse(manualPort, out int serverPort))
            {
                // 设置服务器端口
                NetService.Instance.port = serverPort;
                NetService.Instance.StartNetwork(true);

                SetStatusText("[OK] " + CoopLocalization.Get("ui.server.created", serverPort), ModernColors.Success);

                Debug.Log($"主机创建成功，使用端口: {serverPort}");
            }
            else
            {
                // 端口格式错误
                SetStatusText("[" + CoopLocalization.Get("ui.error") + "] " + CoopLocalization.Get("ui.manualConnect.portError"), ModernColors.Error);

                Debug.LogError($"端口格式错误: {manualPort}");
                return;
            }
        }

        // 延迟更新UI显示，确保状态已切换
        StartCoroutine(DelayedUpdateModeDisplay());
    }

    private IEnumerator DelayedUpdateModeDisplay()
    {
        // 等待一帧确保网络状态已更新
        yield return null;
        UpdateModeDisplay();
    }

    private bool CheckCanConnect()
    {
        // 检查客户端是否在关卡内
        if (LocalPlayerManager.Instance == null)
        {
            SetStatusText("[!] " + CoopLocalization.Get("ui.error.gameNotInitialized"), ModernColors.Error);
            return false;
        }

        var isInGame = LocalPlayerManager.Instance.ComputeIsInGame(out var sceneId);
        if (!isInGame)
        {
            SetStatusText("[!] " + CoopLocalization.Get("ui.error.mustInLevel"), ModernColors.Warning);
            Debug.LogWarning("无法连接：客户端未在游戏关卡中");
            return false;
        }

        Debug.Log($"客户端关卡检查通过，当前场景: {sceneId}");
        return true;
    }

    internal void OnManualConnect()
    {
        if (_components?.IpInputField != null) manualIP = _components.IpInputField.text;
        if (_components?.PortInputField != null) manualPort = _components.PortInputField.text;

        if (!int.TryParse(manualPort, out var p))
        {
            // 端口格式错误需要立即显示
            SetStatusText("[!] " + CoopLocalization.Get("ui.manualConnect.portError"), ModernColors.Error);
            return;
        }

        // 检查是否在关卡内
        if (!CheckCanConnect())
            return;

        if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
            NetService.Instance.StartNetwork(false);
        NetService.Instance.ConnectToHost(manualIP, p);
        // 状态会由 UpdateConnectionStatus() 自动同步
    }

    internal void DebugPrintLootBoxes()
    {
        if (LevelManager.LootBoxInventories == null)
        {
            Debug.LogWarning("LootBoxInventories is null. Make sure you are in a game level.");
            SetStatusText("[!] " + CoopLocalization.Get("ui.error.mustInLevel"), ModernColors.Warning);
            return;
        }

        var count = 0;
        foreach (var i in LevelManager.LootBoxInventories)
        {
            try
            {
                Debug.Log($"Name {i.Value.name} DisplayNameKey {i.Value.DisplayNameKey} Key {i.Key}");
                count++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error printing loot box: {ex.Message}");
            }
        }

        Debug.Log($"Total LootBoxes: {count}");
        SetStatusText($"[OK] " + CoopLocalization.Get("ui.debug.lootBoxCount", count), ModernColors.Success);
    }

    internal void OnTransportModeChanged(NetworkTransportMode newMode)
    {
        if (Service == null) return;

        Service.SetTransportMode(newMode);
        UpdateTransportModePanels();

        if (newMode == NetworkTransportMode.SteamP2P && LobbyManager != null)
        {
            // 清空缓存，强制刷新UI
            _displayedSteamLobbies.Clear();
            LobbyManager.RequestLobbyList();
        }
    }

    private void OnSteamCreateOrLeave()
    {
        var manager = LobbyManager;
        if (manager == null)
        {
            SetStatusText("[!] " + CoopLocalization.Get("ui.steam.error.notInitialized"), ModernColors.Error);
            return;
        }

        if (manager.IsInLobby)
        {
            // 离开房间 - 先停止网络再离开Lobby
            NetService.Instance?.StopNetwork();
            manager.LeaveLobby();  // 显式离开/销毁Steam房间

            SetStatusText("[OK] " + CoopLocalization.Get("ui.steam.lobby.left"), ModernColors.Info);
        }
        else
        {
            // 创建房间
            UpdateLobbyOptionsFromUI();
            NetService.Instance?.StartNetwork(true);
            SetStatusText("[*] " + CoopLocalization.Get("ui.steam.lobby.creating"), ModernColors.Info);
        }
    }

    private void UpdateSteamLobbyList()
    {
        if (_components?.SteamLobbyListContent == null || TransportMode != NetworkTransportMode.SteamP2P)
            return;

        var searchTerm = (_steamLobbySearchTerm ?? string.Empty).Trim();
        bool searchChanged = !string.Equals(searchTerm, _lastAppliedSteamSearchTerm, StringComparison.Ordinal);

        IEnumerable<SteamLobbyManager.LobbyInfo> filteredLobbies = _steamLobbyInfos;
        if (!string.IsNullOrEmpty(searchTerm))
        {
            filteredLobbies = _steamLobbyInfos.Where(lobby =>
                (!string.IsNullOrEmpty(lobby.LobbyName) && lobby.LobbyName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(lobby.HostName) && lobby.HostName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        var currentLobbies = new HashSet<ulong>(filteredLobbies.Select(l => l.LobbyId.m_SteamID));

        // 如果列表没有变化，直接返回（避免重复创建UI）
        if (!searchChanged && _displayedSteamLobbies.SetEquals(currentLobbies))
            return;

        _lastAppliedSteamSearchTerm = searchTerm;

        // 列表改变了，需要重建UI
        Debug.Log($"[MModUI] Steam房间列表已更新，重建UI (当前: {currentLobbies.Count}, 之前: {_displayedSteamLobbies.Count})");

        // 清空现有列表
        foreach (Transform child in _components.SteamLobbyListContent)
            Destroy(child.gameObject);

        // 更新缓存
        _displayedSteamLobbies.Clear();
        foreach (var id in currentLobbies)
            _displayedSteamLobbies.Add(id);

        if (!filteredLobbies.Any())
        {
            var emptyKey = string.IsNullOrEmpty(searchTerm) ? "ui.steam.lobbiesEmpty" : "ui.steam.lobbiesEmptyFiltered";
            CreateText("EmptyHint", _components.SteamLobbyListContent, CoopLocalization.Get(emptyKey), 14, ModernColors.TextTertiary, TextAlignmentOptions.Center);
            return;
        }

        // 创建房间条目
        foreach (var lobby in filteredLobbies)
        {
            CreateSteamLobbyEntry(lobby);
        }
    }

    private void CreateSteamLobbyEntry(SteamLobbyManager.LobbyInfo lobby)
    {
        var entry = CreateModernCard(_components.SteamLobbyListContent, $"Lobby_{lobby.LobbyId}");
        var entryLayout = entry.GetComponent<LayoutElement>();
        entryLayout.preferredHeight = 120;  // 增加高度：90 -> 120
        entryLayout.minHeight = 120;

        // 禁用卡片背景的射线检测，让点击事件能传递到按钮
        var entryImage = entry.GetComponent<Image>();
        if (entryImage != null)
        {
            entryImage.raycastTarget = false;
        }

        // 调整内部垂直布局的间距
        var cardLayout = entry.GetComponent<VerticalLayoutGroup>();
        if (cardLayout != null)
        {
            cardLayout.spacing = 10;  // 增加子元素间距
            cardLayout.padding = new RectOffset(15, 15, 15, 15);  // 增加内边距
        }

        // 房间名
        var nameRow = CreateHorizontalGroup(entry.transform, "NameRow");
        var nameRowLayout = nameRow.GetComponent<HorizontalLayoutGroup>();
        nameRowLayout.spacing = 12;  // 增加房间名和密码图标的间距

        var lobbyNameText = CreateText("LobbyName", nameRow.transform, lobby.LobbyName, 16, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
        lobbyNameText.raycastTarget = false;  // 禁用文本射线检测

        if (lobby.RequiresPassword)
        {
            CreateBadge(nameRow.transform, "🔒", ModernColors.Warning);
        }

        // 房间信息
        var infoRow = CreateHorizontalGroup(entry.transform, "InfoRow");
        var playerCountText = CreateText("PlayerCount", infoRow.transform, CoopLocalization.Get("ui.steam.playerCount", lobby.MemberCount, lobby.MaxMembers), 13, ModernColors.TextSecondary);
        playerCountText.raycastTarget = false;  // 禁用文本射线检测

        CreateDivider(entry.transform);

        // 加入按钮
        var joinButton = CreateModernButton("JoinBtn", entry.transform, CoopLocalization.Get("ui.steam.joinButton"), () =>
        {
            Debug.Log($"[MModUI] 加入按钮被点击！房间: {lobby.LobbyName}");
            AttemptSteamLobbyJoin(lobby);
        }, -1, ModernColors.Primary, 40, 15);

        // 确保按钮的 targetGraphic 正确设置
        var joinButtonImage = joinButton.GetComponent<Image>();
        if (joinButtonImage != null)
        {
            joinButtonImage.raycastTarget = true;  // 确保按钮背景可以接收射线
            Debug.Log($"[MModUI] 创建加入按钮: {lobby.LobbyName}, raycastTarget={joinButtonImage.raycastTarget}");
        }
    }

    private void AttemptSteamLobbyJoin(SteamLobbyManager.LobbyInfo lobby)
    {
        Debug.Log($"[MModUI] 尝试加入Steam房间: {lobby.LobbyName} (ID: {lobby.LobbyId})");

        var manager = LobbyManager;
        if (manager == null)
        {
            Debug.LogError("[MModUI] Steam Lobby Manager 未初始化");
            SetStatusText("[!] " + CoopLocalization.Get("ui.steam.error.notInitialized"), ModernColors.Error);
            return;
        }

        // 检查是否在关卡内 - 必须在游戏中才能加入
        if (!CheckCanConnect())
        {
            Debug.LogWarning("[MModUI] 关卡检查失败，无法加入房间");
            return;
        }

        Debug.Log("[MModUI] 关卡检查通过，准备加入房间");

        // 如果网络未启动，先启动客户端模式
        if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
        {
            Debug.Log("[MModUI] 启动客户端网络模式");
            NetService.Instance?.StartNetwork(false);
        }

        var password = lobby.RequiresPassword ? _steamJoinPassword : string.Empty;
        Debug.Log($"[MModUI] 调用 TryJoinLobbyWithPassword, 需要密码: {lobby.RequiresPassword}");

        if (manager.TryJoinLobbyWithPassword(lobby.LobbyId, password, out var error))
        {
            Debug.Log($"[MModUI] 加入请求已发送，等待Steam响应");
            SetStatusText("[*] " + CoopLocalization.Get("ui.status.connecting"), ModernColors.Info);
            return;
        }

        // 处理错误
        Debug.LogError($"[MModUI] 加入房间失败: {error}");
        string errorMsg = error switch
        {
            SteamLobbyManager.LobbyJoinError.SteamNotInitialized => "[!] " + CoopLocalization.Get("ui.steam.error.notInitialized"),
            SteamLobbyManager.LobbyJoinError.LobbyMetadataUnavailable => "[!] " + CoopLocalization.Get("ui.steam.error.metadata"),
            SteamLobbyManager.LobbyJoinError.IncorrectPassword => "[!] " + CoopLocalization.Get("ui.steam.error.password"),
            _ => "[!] " + CoopLocalization.Get("ui.steam.error.generic")
        };

        SetStatusText(errorMsg, ModernColors.Error);
    }

    #endregion
}

#region 辅助组件

// UI 拖拽组件
public class UIDragger : MonoBehaviour, IDragHandler, IBeginDragHandler
{
    private RectTransform _rectTransform;
    private Vector2 _dragOffset;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rectTransform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out var localPoint);
        _dragOffset = _rectTransform.anchoredPosition - localPoint;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform.parent as RectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out var localPoint))
            _rectTransform.anchoredPosition = localPoint + _dragOffset;
    }
}

// 按钮悬停动画组件 - 带弹性缓动效果
public class ButtonHoverAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private RectTransform _rectTransform;
    private Vector3 _originalScale;
    private Coroutine _scaleCoroutine;
    private bool _isPressed;
    private Shadow _shadow;
    private Vector2 _shadowDefault;
    private Vector2 _shadowHover;
    private Vector2 _shadowPressed;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _originalScale = _rectTransform.localScale;

        foreach (var effect in GetComponents<Shadow>())
        {
            if (effect.GetType() == typeof(Shadow))
            {
                _shadow = effect;
                break;
            }
        }

        if (_shadow != null)
        {
            _shadowDefault = _shadow.effectDistance;
            _shadowHover = _shadowDefault + new Vector2(0f, -1.5f);
            _shadowPressed = new Vector2(_shadowDefault.x * 0.35f, _shadowDefault.y * 0.35f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_isPressed)
        {
            SetShadow(_shadowHover);
            if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
            _scaleCoroutine = StartCoroutine(ScaleToWithEasing(_originalScale * 1.025f, 0.12f, EaseOutCubic));
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isPressed = false;
        SetShadow(_shadowDefault);
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(ScaleToWithEasing(_originalScale, 0.11f, EaseOutCubic));
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        SetShadow(_shadowPressed);
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(ScaleToWithEasing(_originalScale * 0.94f, 0.055f, EaseOutCubic));
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        SetShadow(_shadowHover);
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(ScaleToWithEasing(_originalScale * 1.025f, 0.10f, EaseOutBack));
    }

    private void SetShadow(Vector2 distance)
    {
        if (_shadow != null)
            _shadow.effectDistance = distance;
    }

    private IEnumerator ScaleToWithEasing(Vector3 targetScale, float duration, System.Func<float, float> easingFunction)
    {
        Vector3 startScale = _rectTransform.localScale;
        float time = 0;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            float easedT = easingFunction(t);
            _rectTransform.localScale = Vector3.Lerp(startScale, targetScale, easedT);
            yield return null;
        }

        _rectTransform.localScale = targetScale;
    }

    // 缓动函数 - EaseOutBack (带回弹效果)
    private float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // 缓动函数 - EaseOutCubic (平滑减速)
    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    // 生成嵌入式的噪声纹理（128x128）


}

// 输入框聚焦处理组件
public class InputFieldFocusHandler : MonoBehaviour
{
    public TMP_InputField inputField;
    public Outline outline;

    private void Start()
    {
        if (inputField != null)
        {
            inputField.onSelect.AddListener(OnSelect);
            inputField.onDeselect.AddListener(OnDeselect);
        }
    }

    private void OnSelect(string text)
    {
        if (outline != null)
            outline.effectColor = MModUI.ModernColors.InputFocus;
    }

    private void OnDeselect(string text)
    {
        if (outline != null)
            outline.effectColor = MModUI.ModernColors.InputBorder;
    }

    private void OnDestroy()
    {
        if (inputField != null)
        {
            inputField.onSelect.RemoveListener(OnSelect);
            inputField.onDeselect.RemoveListener(OnDeselect);
        }
    }
}



#endregion
