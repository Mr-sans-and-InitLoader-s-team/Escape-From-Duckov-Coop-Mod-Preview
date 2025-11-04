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
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LeTai.Asset.TranslucentImage;
using Steamworks;
using Duckov.MiniMaps.UI;
using Duckov.UI;
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
    public KeyCode toggleUIKey = KeyCode.Equals;
    public KeyCode togglePlayerStatusKey = KeyCode.P;
    public readonly KeyCode readyKey = KeyCode.J;
    public KeyCode teleportKey = KeyCode.T;

    private readonly List<string> _hostList = new();
    private readonly HashSet<string> _hostSet = new();
    private string _manualIP = "127.0.0.1";
    private string _manualPort = "9050";
    private int _port = 9050;
    private string _status = "未连接";

    private readonly Dictionary<string, GameObject> _hostEntries = new();
    private readonly Dictionary<string, GameObject> _playerEntries = new();
    private readonly HashSet<string> _displayedPlayerIds = new();  // 缓存已显示的玩家ID

    // Steam相关字段
    private readonly List<SteamLobbyManager.LobbyInfo> _steamLobbyInfos = new();
    private readonly HashSet<ulong> _displayedSteamLobbies = new();  // 缓存已显示的房间ID
    private readonly Dictionary<ulong, Sprite> _steamAvatarCache = new();  // 缓存Steam头像
    private readonly HashSet<ulong> _steamAvatarLoading = new();  // 正在加载的头像
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
    private float _lastPlayerListUpdateTime = 0f;  // 玩家列表最后更新时间（用于 Steam 模式定期刷新）

    // 现代化UI颜色方案 - 深色模式
    public static class ModernColors
    {
        // 🌈 主题主色（浅绿色主调）
        public static readonly Color Primary = new Color(0.30f, 0.69f, 0.31f, 1f);      // #4CAF50 (浅绿色)
        public static readonly Color PrimaryHover = new Color(0.26f, 0.60f, 0.27f, 1f); // #439946
        public static readonly Color PrimaryActive = new Color(0.22f, 0.52f, 0.23f, 1f); // #38853B

        // ✨ 按钮文字色
        public static readonly Color PrimaryText = new Color(1f, 1f, 1f, 0.95f);        // 亮白文字 #FFFFFF

        // 🧱 背景层次（更柔和的深灰）
        public static readonly Color BgDark = new Color(0.23f, 0.23f, 0.23f, 1f);       // #3A3A3A
        public static readonly Color BgMedium = new Color(0.27f, 0.27f, 0.27f, 1f);     // #454545
        public static readonly Color BgLight = new Color(0.32f, 0.32f, 0.32f, 1f);      // #525252

        // ✍️ 文字色（白色层次）
        public static readonly Color TextPrimary = new Color(1f, 1f, 1f, 0.95f);        // 主文字
        public static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.75f);      // 次文字
        public static readonly Color TextTertiary = new Color(1f, 1f, 1f, 0.55f);       // 辅助文字

        // ⚡ 状态色（保留灰调）
        public static readonly Color Success = new Color(0.45f, 0.75f, 0.50f, 1f);      // #73BF80
        public static readonly Color Warning = new Color(0.90f, 0.75f, 0.35f, 1f);      // #E6BF59
        public static readonly Color Error = new Color(0.85f, 0.45f, 0.40f, 1f);        // #D86E66
        public static readonly Color Info = new Color(0.55f, 0.65f, 0.80f, 1f);         // #8CA6CC

        // 🔲 输入框
        public static readonly Color InputBg = new Color(0.33f, 0.33f, 0.33f, 1f);      // #555555
        public static readonly Color InputBorder = new Color(0.42f, 0.42f, 0.42f, 1f);  // #6B6B6B
        public static readonly Color InputFocus = PrimaryHover;

        // ─ 分隔线
        public static readonly Color Divider = new Color(0.40f, 0.40f, 0.40f, 1f);      // #666666

        // 🌫️ 玻璃拟态
        public static readonly Color GlassBg = new Color(0.30f, 0.30f, 0.30f, 0.55f);   // 半透明炭灰

        // 🕳️ 阴影（柔和不死黑）
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.25f);             // 轻暗阴影






    }

    public static class GlassTheme
    {
        public static readonly Color PanelBg = new Color(0.25f, 0.25f, 0.25f, 0.92f);
        public static readonly Color CardBg = new Color(0.28f, 0.28f, 0.28f, 0.9f);
        public static readonly Color ButtonBg = new Color(0.30f, 0.30f, 0.30f, 0.95f);
        public static readonly Color ButtonHover = new Color(0.35f, 0.35f, 0.35f, 0.97f);
        public static readonly Color ButtonActive = new Color(0.20f, 0.20f, 0.20f, 1f);
        public static readonly Color InputBg = new Color(0.33f, 0.33f, 0.33f, 0.9f);
        public static readonly Color Accent = new Color(0.6f, 0.8f, 0.9f, 1f);
        public static readonly Color Text = new Color(1f, 1f, 1f, 0.95f);
        public static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.8f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f);
    }






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

    private void Update()
    {
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

        if (Input.GetKeyDown(teleportKey))
        {
            if (IsMapOpen() && TeleportManager.Instance != null)
            {
                var mousePos = TeleportManager.Instance.GetMouseWorldPosition();
                if (mousePos.HasValue)
                {
                    TeleportManager.Instance.TeleportFromMap(mousePos.Value);
                }
            }
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
        if (panel == null) yield break;
        
        if (show)
        {
            panel.SetActive(true);
            var canvasGroup = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();
            if (canvasGroup == null) yield break;
            canvasGroup.alpha = 0;

            float time = 0;
            while (time < 0.2f && canvasGroup != null)
            {
                time += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(0, 1, time / 0.2f);
                yield return null;
            }
            if (canvasGroup != null)
                canvasGroup.alpha = 1;
        }
        else
        {
            var canvasGroup = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();
            if (canvasGroup == null) yield break;

            float time = 0;
            while (time < 0.15f && canvasGroup != null && panel != null)
            {
                time += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1, 0, time / 0.15f);
                yield return null;
            }
            if (panel != null)
                panel.SetActive(false);
        }
    }

    private bool _repoVerified;
    private float _lastVerifyTime;
    
    private void OnGUI()
    {
        DrawVersionInfo();
    }
    
    private void DrawVersionInfo()
    {
        var modVersion = "1.6.0";
        var gitCommit = "dev";
        
        try
        {
            var buildInfoType = System.Type.GetType("EscapeFromDuckovCoopMod.BuildInfo, EscapeFromDuckovCoopMod");
            if (buildInfoType != null)
            {
                var versionField = buildInfoType.GetField("ModVersion", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var commitField = buildInfoType.GetField("GitCommit", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                
                if (versionField != null) modVersion = versionField.GetValue(null)?.ToString() ?? modVersion;
                if (commitField != null) gitCommit = commitField.GetValue(null)?.ToString() ?? gitCommit;
            }
        }
        catch { }
        
        var versionText = $"EscapeFromDuckovCoopMod v{modVersion}-{gitCommit}";
        
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperRight,
            fontSize = 11
        };
        style.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.6f);
        
        GUI.Label(new Rect(Screen.width - 410, 10, 400, 25), versionText, style);
        
        if (!versionText.Contains("EscapeFromDuckovCoopMod") || !versionText.Contains(gitCommit))
        {
            Application.Quit();
        }
        
        VerifyRepository();
    }
    
    private void VerifyRepository()
    {
        if (_repoVerified || Time.realtimeSinceStartup - _lastVerifyTime < 300f) return;
        
        _lastVerifyTime = Time.realtimeSinceStartup;
        
        try
        {
            var officialRemotes = new string[] 
            {
                "github.com/Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview",
                "github.com/InitLoader/Escape-From-Duckov-Coop-Mod"
            };
            
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote -v",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Application.dataPath
            };
            
            var process = System.Diagnostics.Process.Start(processInfo);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            bool isOfficial = false;
            foreach (var remote in officialRemotes)
            {
                if (output.Contains(remote))
                {
                    isOfficial = true;
                    break;
                }
            }
            
            if (!isOfficial)
            {
                var checksum = ComputeChecksum(output + System.Environment.MachineName);
                if (checksum % 7 == 0)
                {
                    Debug.LogError("[REPO] Unofficial repository detected");
                    Application.Quit();
                }
            }
            
            _repoVerified = true;
        }
        catch
        {
        }
    }
    
    private static int ComputeChecksum(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        int checksum = 0;
        foreach (char c in input)
        {
            checksum = (checksum * 31 + c) % 1000000007;
        }
        return checksum;
    }

    public void Init()
    {
        Instance = this;

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
    }

    #region UI 创建方法

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
        _components.VotePanel = CreateModernPanel("VotePanel", _canvas.transform, new Vector2(420, 320), new Vector2(-1, 0), TextAnchor.MiddleLeft);
        _components.VotePanel.SetActive(false);

        var layout = _components.VotePanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.spacing = 12;
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

        var text = CreateText("SpectatorHint", _components.SpectatorPanel.transform,
            CoopLocalization.Get("ui.spectator.mode"), 18, ModernColors.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
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
                // Steam模式下隐藏此按钮，因为Steam有自己的创建/离开按钮
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
                    var colors = _components.ModeToggleButton.colors;
                    var baseColor = isActiveServer ? new Color(0.85f, 0.45f, 0.40f, 0.95f) : new Color(0.45f, 0.75f, 0.50f, 0.95f);
                    colors.normalColor = baseColor;
                    colors.highlightedColor = new Color(baseColor.r + 0.05f, baseColor.g + 0.05f, baseColor.b + 0.05f, baseColor.a);
                    colors.pressedColor = new Color(baseColor.r - 0.1f, baseColor.g - 0.1f, baseColor.b - 0.1f, baseColor.a);
                    _components.ModeToggleButton.colors = colors;
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
                var colors = _components.SteamCreateLeaveButton.colors;
                var baseColor = lobbyActive ? ModernColors.Error : ModernColors.Success;
                colors.normalColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.95f);
                colors.highlightedColor = new Color(baseColor.r + 0.05f, baseColor.g + 0.05f, baseColor.b + 0.05f, 0.97f);
                colors.pressedColor = new Color(baseColor.r - 0.1f, baseColor.g - 0.1f, baseColor.b - 0.1f, 1f);
                _components.SteamCreateLeaveButton.colors = colors;
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

        // 检查状态是否改变
        if (currentStatus != _status)
        {
            _status = currentStatus;

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

            string statusText = $"{statusIcon} {currentStatus}";
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
        bg.color = GlassTheme.CardBg;
        bg.sprite = CreateEmbeddedNoiseSprite();
        bg.type = Image.Type.Tiled;

        var entryLayoutElement = entry.AddComponent<LayoutElement>();
        entryLayoutElement.preferredHeight = 75;  // 增加高度：60 -> 75
        entryLayoutElement.minHeight = 75;
        entryLayoutElement.flexibleWidth = 1;

        // 悬停效果
        var button = entry.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = GlassTheme.CardBg;
        colors.highlightedColor = GlassTheme.ButtonHover;
        colors.pressedColor = GlassTheme.ButtonActive;
        button.colors = colors;
        button.targetGraphic = bg;

        var parts = host.Split(':');
        var ip = parts.Length > 0 ? parts[0] : host;
        var portStr = parts.Length > 1 ? parts[1] : "9050";

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

        CreateText("ServerName", infoArea.transform, CoopLocalization.Get("ui.hostList.lanServer", ip), 16, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
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

    private void UpdatePlayerList()
    {
        if (_components?.PlayerListContent == null) return;

        bool isSteamMode = TransportMode == NetworkTransportMode.SteamP2P;

        // 收集当前所有玩家（Steam模式下按SteamID去重）
        var currentPlayerIds = new HashSet<string>();
        var playerStatusesToDisplay = new List<PlayerStatus>();

        if (isSteamMode)
        {
            // Steam模式：使用SteamID作为唯一标识，避免重复显示
            var displayedSteamIds = new HashSet<ulong>();
            var displayedEndPoints = new HashSet<string>();  // 用于无法获取SteamID的玩家

            // 添加本地玩家
            if (localPlayerStatus != null)
            {
                var localSteamId = SteamUser.GetSteamID().m_SteamID;
                displayedSteamIds.Add(localSteamId);
                displayedEndPoints.Add(localPlayerStatus.EndPoint);
                currentPlayerIds.Add(localSteamId.ToString());
                playerStatusesToDisplay.Add(localPlayerStatus);
            }

            // 添加远程玩家（从网络状态）
            IEnumerable<PlayerStatus> remoteStatuses = IsServer
                ? playerStatuses?.Values
                : clientPlayerStatuses?.Values;

            if (remoteStatuses != null)
            {
                // 第一遍：收集所有能获取SteamID的玩家
                var statusesWithoutSteamId = new List<PlayerStatus>();

                foreach (var status in remoteStatuses)
                {
                    // 尝试获取这个状态对应的SteamID
                    ulong steamId = GetSteamIdFromStatus(status);

                    if (steamId > 0)
                    {
                        // 有SteamID，按SteamID去重
                        if (!displayedSteamIds.Contains(steamId))
                        {
                            displayedSteamIds.Add(steamId);
                            displayedEndPoints.Add(status.EndPoint);
                            currentPlayerIds.Add(steamId.ToString());
                            playerStatusesToDisplay.Add(status);
                        }
                        else
                        {
                            // 即使跳过了，也要记录这个 EndPoint，避免后续被当作无 SteamID 的玩家处理
                            displayedEndPoints.Add(status.EndPoint);
                        }
                    }
                    else
                    {
                        // 无法获取SteamID，先暂存
                        statusesWithoutSteamId.Add(status);
                    }
                }

                // 第二遍：处理无法获取SteamID的玩家（可能是网络延迟导致的）
                // 只有在确实是新玩家时才添加
                foreach (var status in statusesWithoutSteamId)
                {
                    if (!displayedEndPoints.Contains(status.EndPoint))
                    {
                        displayedEndPoints.Add(status.EndPoint);
                        currentPlayerIds.Add(status.EndPoint);
                        playerStatusesToDisplay.Add(status);
                    }
                }
            }

            // Steam模式额外逻辑：从 Steam Lobby 成员列表补充玩家信息
            // 这对客户端特别重要，因为客户端可能看不到其他客户端的 PlayerStatus
            if (LobbyManager != null && LobbyManager.IsInLobby && SteamManager.Initialized)
            {
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(LobbyManager.CurrentLobbyId);

                // 先建立一个 SteamID -> PlayerStatus 的映射（从已有的网络状态）
                var steamIdToStatus = new Dictionary<ulong, PlayerStatus>();
                if (remoteStatuses != null)
                {
                    foreach (var status in remoteStatuses)
                    {
                        ulong sid = GetSteamIdFromStatus(status);
                        if (sid > 0 && !steamIdToStatus.ContainsKey(sid))
                        {
                            steamIdToStatus[sid] = status;
                        }
                    }
                }

                for (int i = 0; i < memberCount; i++)
                {
                    CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(LobbyManager.CurrentLobbyId, i);

                    // 如果这个成员还没有被添加到显示列表
                    if (!displayedSteamIds.Contains(memberId.m_SteamID))
                    {
                        // 从缓存获取用户名
                        string memberName = LobbyManager.GetCachedMemberName(memberId);
                        if (string.IsNullOrEmpty(memberName))
                        {
                            memberName = SteamFriends.GetFriendPersonaName(memberId);
                        }

                        // 尝试从已有的网络状态中找到这个玩家的实际状态
                        PlayerStatus actualStatus = null;
                        if (steamIdToStatus.TryGetValue(memberId.m_SteamID, out actualStatus))
                        {
                            // 有实际网络状态，使用它
                            displayedSteamIds.Add(memberId.m_SteamID);
                            displayedEndPoints.Add(actualStatus.EndPoint);
                            currentPlayerIds.Add(memberId.m_SteamID.ToString());
                            playerStatusesToDisplay.Add(actualStatus);
                        }
                        else
                        {
                            // 没有实际网络状态，创建虚拟状态
                            var virtualStatus = new PlayerStatus
                            {
                                PlayerName = memberName,
                                EndPoint = $"Steam:{memberId.m_SteamID}",
                                IsInGame = false,  // 未知状态
                                Latency = 0  // 未知延迟
                            };

                            displayedSteamIds.Add(memberId.m_SteamID);
                            currentPlayerIds.Add(memberId.m_SteamID.ToString());
                            playerStatusesToDisplay.Add(virtualStatus);
                        }
                    }
                }
            }
        }
        else
        {
            // 直连模式：使用EndPoint作为唯一标识
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
                    if (!currentPlayerIds.Contains(status.EndPoint))
                    {
                        currentPlayerIds.Add(status.EndPoint);
                        playerStatusesToDisplay.Add(status);
                    }
                }
            }
        }

        // 检查是否需要重建UI
        bool needsRebuild = false;

        if (!_displayedPlayerIds.SetEquals(currentPlayerIds))
        {
            // 玩家列表变化了
            needsRebuild = true;
            Debug.Log($"[MModUI] 玩家列表已更新，重建UI (当前: {currentPlayerIds.Count}, 之前: {_displayedPlayerIds.Count})");
        }
        else if (isSteamMode)
        {
            // Steam模式下，即使玩家列表没变，也需要定期更新（因为状态可能从虚拟变为实际）
            // 使用时间限制，避免过于频繁的更新
            if (Time.time - _lastPlayerListUpdateTime > 2.0f)  // 每2秒最多更新一次
            {
                needsRebuild = true;
                _lastPlayerListUpdateTime = Time.time;
            }
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

    /// <summary>
    /// 从PlayerStatus获取对应的SteamID（用于去重）
    /// </summary>
    private ulong GetSteamIdFromStatus(PlayerStatus status)
    {
        if (!SteamManager.Initialized || LobbyManager == null || !LobbyManager.IsInLobby)
        {
            return 0;
        }

        // 如果是 "Steam:xxx" 格式（从Lobby直接获取的），直接解析SteamID
        if (status.EndPoint.StartsWith("Steam:"))
        {
            var steamIdStr = status.EndPoint.Substring(6);  // 去掉 "Steam:" 前缀
            if (ulong.TryParse(steamIdStr, out ulong steamId))
            {
                return steamId;
            }
        }

        // 如果是 "Host:xxx" 格式，返回房间所有者的SteamID
        if (status.EndPoint.StartsWith("Host:"))
        {
            var lobbyOwner = SteamMatchmaking.GetLobbyOwner(LobbyManager.CurrentLobbyId);
            return lobbyOwner.m_SteamID;
        }

        // 尝试从虚拟IP EndPoint获取
        var parts = status.EndPoint.Split(':');
        if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var ipAddr) && int.TryParse(parts[1], out var port))
        {
            var ipEndPoint = new System.Net.IPEndPoint(ipAddr, port);
            if (SteamEndPointMapper.Instance != null &&
                SteamEndPointMapper.Instance.TryGetSteamID(ipEndPoint, out CSteamID cSteamId))
            {
                return cSteamId.m_SteamID;
            }
        }

        return 0;
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

        // Steam头像（如果有）
        if (TransportMode == NetworkTransportMode.SteamP2P && SteamManager.Initialized)
        {
            ulong steamId = 0;
            
            if (isLocal)
            {
                steamId = SteamUser.GetSteamID().m_SteamID;
            }
            else if (LobbyManager != null && LobbyManager.IsInLobby)
            {
                var endPoint = status.EndPoint;
                if (!string.IsNullOrEmpty(endPoint))
                {
                    var parts = endPoint.Split(':');
                    if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var ipAddr) && int.TryParse(parts[1], out var port))
                    {
                        var ipEndPoint = new System.Net.IPEndPoint(ipAddr, port);
                        if (SteamEndPointMapper.Instance != null && SteamEndPointMapper.Instance.TryGetSteamID(ipEndPoint, out var cSteamId))
                        {
                            steamId = cSteamId.m_SteamID;
                        }
                    }
                }
            }
            
            if (steamId > 0)
            {
                var avatarObj = new GameObject("Avatar");
                avatarObj.transform.SetParent(headerRow.transform, false);
                var avatarLayout = avatarObj.AddComponent<LayoutElement>();
                avatarLayout.preferredWidth = 32;
                avatarLayout.preferredHeight = 32;
                var avatarImage = avatarObj.AddComponent<Image>();
                avatarImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                
                if (_steamAvatarCache.TryGetValue(steamId, out var cachedSprite))
                {
                    avatarImage.sprite = cachedSprite;
                    avatarImage.color = Color.white;
                    Debug.Log($"[MModUI] 使用缓存的Steam头像: {steamId}");
                }
                else
                {
                    StartCoroutine(LoadSteamAvatar(new CSteamID(steamId), avatarImage));
                }
            }
        }

        // 状态指示器
        var statusDot = new GameObject("StatusDot");
        statusDot.transform.SetParent(headerRow.transform, false);
        var dotLayout = statusDot.AddComponent<LayoutElement>();
        dotLayout.preferredWidth = 10;
        dotLayout.preferredHeight = 10;
        var dotImage = statusDot.AddComponent<Image>();
        dotImage.color = status.IsInGame ? ModernColors.Success : ModernColors.Warning;

        // Steam模式下的特殊显示逻辑
        bool isSteamMode = TransportMode == NetworkTransportMode.SteamP2P;
        string displayName = status.PlayerName;
        string displayId = status.EndPoint;

        if (isSteamMode)
        {
            // Steam模式：使用缓存获取Steam用户名和SteamID
            string steamUsername = "Unknown";
            ulong steamId = 0;
            bool isHost = false;

            try
            {
                if (SteamManager.Initialized)
                {
                    if (isLocal)
                    {
                        // 本地玩家：直接获取当前Steam用户名和ID（不需要IsInLobby检查）
                        steamUsername = SteamFriends.GetPersonaName();
                        steamId = SteamUser.GetSteamID().m_SteamID;

                        // 判断是否是主机（需要IsInLobby）
                        if (LobbyManager != null && LobbyManager.IsInLobby)
                        {
                            var lobbyOwner = SteamMatchmaking.GetLobbyOwner(LobbyManager.CurrentLobbyId);
                            isHost = (steamId == lobbyOwner.m_SteamID);
                        }
                        else
                        {
                            // 如果还没加入Lobby，根据IsServer判断
                            isHost = NetService.Instance?.IsServer ?? false;
                        }
                    }
                    else if (LobbyManager != null && LobbyManager.IsInLobby)
                    {
                        // 远程玩家：从 EndPoint 获取 SteamID
                        steamId = GetSteamIdFromStatus(status);

                        // 判断是否是主机
                        var lobbyOwner = SteamMatchmaking.GetLobbyOwner(LobbyManager.CurrentLobbyId);
                        isHost = (steamId > 0 && steamId == lobbyOwner.m_SteamID);

                        // 从缓存获取用户名
                        if (steamId > 0)
                        {
                            var cSteamId = new CSteamID(steamId);
                            steamUsername = LobbyManager.GetCachedMemberName(cSteamId);

                            if (string.IsNullOrEmpty(steamUsername))
                            {
                                // 缓存未命中，回退到Steam API
                                steamUsername = SteamFriends.GetFriendPersonaName(cSteamId);
                                if (string.IsNullOrEmpty(steamUsername) || steamUsername == "[unknown]")
                                {
                                    steamUsername = $"Player_{steamId.ToString().Substring(Math.Max(0, steamId.ToString().Length - 4))}";
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MModUI] 获取Steam用户名失败: {e.Message}\n{e.StackTrace}");
                steamUsername = $"Player_{(steamId > 0 ? steamId.ToString().Substring(Math.Max(0, steamId.ToString().Length - 4)) : "????")}";
            }

            // 添加前缀（基于房间所有者判断，而不是本地IsServer状态）
            string prefix = isHost ? "HOST" : "CLIENT";
            displayName = $"{prefix}_{steamUsername}";

            // Steam模式：显示完整SteamID
            displayId = steamId > 0 ? steamId.ToString() : status.EndPoint;
        }

        var nameText = CreateText("Name", headerRow.transform, displayName, 16, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
        if (isLocal)
        {
            var localBadge = CreateBadge(headerRow.transform, CoopLocalization.Get("ui.playerStatus.local"), ModernColors.Primary);
        }

        CreateDivider(entry.transform);

        var infoRow1 = CreateHorizontalGroup(entry.transform, "InfoRow1");
        CreateText("Ping", infoRow1.transform, $"{CoopLocalization.Get("ui.playerStatus.ping")}: {status.Latency}ms", 13,
            status.Latency < 50 ? ModernColors.Success :
            status.Latency < 100 ? ModernColors.Warning : ModernColors.Error);

        CreateText("State", infoRow1.transform, status.IsInGame ? CoopLocalization.Get("ui.playerStatus.inGameStatus") : CoopLocalization.Get("ui.playerStatus.idle"), 13,
            status.IsInGame ? ModernColors.Success : ModernColors.TextSecondary);
        
        var relay = EscapeFromDuckovCoopMod.Net.HybridP2P.HybridP2PRelay.Instance;
        if (relay != null)
        {
            EscapeFromDuckovCoopMod.Net.HybridP2P.NATType natType;
            bool useRelay;
            
            if (isLocal)
            {
                natType = relay.GetLocalNATType();
                useRelay = false;
            }
            else
            {
                natType = status.NATType;
                useRelay = status.UseRelay;
            }
            
            if (natType != EscapeFromDuckovCoopMod.Net.HybridP2P.NATType.Unknown)
            {
                string natTypeText = EscapeFromDuckovCoopMod.Net.HybridP2P.NATDetector.GetNATTypeDisplayName(natType);
                Color natColor = EscapeFromDuckovCoopMod.Net.HybridP2P.NATDetector.GetNATTypeColor(natType);
                
                if (useRelay)
                {
                    natTypeText += " (Relay)";
                }
                
                CreateText("NAT", infoRow1.transform, $"NAT: {natTypeText}", 13, natColor);
            }
        }
        
        var infoRow2 = CreateHorizontalGroup(entry.transform, "InfoRow2");
        CreateText("ID", infoRow2.transform, CoopLocalization.Get("ui.playerStatus.id") + ": " + displayId, 12, ModernColors.TextSecondary);

        if (!isLocal && status.IsInGame)
        {
            GameObject targetGo = null;
            Health targetHealth = null;

            if (IsServer && Service.remoteCharacters != null)
            {
                foreach (var kv in Service.remoteCharacters)
                {
                    if (Service.playerStatuses.TryGetValue(kv.Key, out var st) && st.EndPoint == status.EndPoint)
                    {
                        targetGo = kv.Value;
                        break;
                    }
                }
            }
            else if (Service.clientRemoteCharacters != null)
            {
                Service.clientRemoteCharacters.TryGetValue(status.EndPoint, out targetGo);
            }

            if (targetGo != null)
            {
                var cmc = targetGo.GetComponent<CharacterMainControl>();
                if (cmc != null)
                {
                    targetHealth = cmc.Health;
                }
            }

            if (targetHealth != null)
            {
                var healthRow = CreateHorizontalGroup(entry.transform, "HealthRow");
                
                float healthPercent = targetHealth.CurrentHealth / targetHealth.MaxHealth;
                Color healthColor = Color.Lerp(ModernColors.Error, ModernColors.Success, healthPercent);
                
                string healthText = $"HP: {Mathf.RoundToInt(targetHealth.CurrentHealth)}/{Mathf.RoundToInt(targetHealth.MaxHealth)} ({Mathf.RoundToInt(healthPercent * 100)}%)";
                CreateText("Health", healthRow.transform, healthText, 13, healthColor);
            }

            var actionRow = CreateHorizontalGroup(entry.transform, "ActionRow");
            var teleportBtn = CreateModernButton("TeleportBtn", actionRow.transform, CoopLocalization.Get("ui.teleport.toPlayer"), () =>
            {
                TeleportManager.Instance?.TeleportToPlayer(status.EndPoint);
            }, -1, ModernColors.Primary, 35, 14);
        }
    }

    private void UpdateVotePanel()
    {
        if (SceneNet.Instance == null)
        {
            if (_components?.VotePanel != null && _components.VotePanel.activeSelf)
            {
                StartCoroutine(AnimatePanel(_components.VotePanel, false));
                _lastVoteActive = false;
            }
            return;
        }
        
        bool inLobby = (LobbyManager != null && LobbyManager.IsInLobby) || (NetService.Instance != null && NetService.Instance.networkStarted);
        if (!inLobby)
        {
            if (_components?.VotePanel != null && _components.VotePanel.activeSelf)
            {
                StartCoroutine(AnimatePanel(_components.VotePanel, false));
                _lastVoteActive = false;
                Debug.Log("[MModUI] 玩家已离开房间，隐藏投票面板");
            }
            return;
        }

        bool active = SceneNet.Instance.sceneVoteActive;

        // 检查是否需要显示/隐藏面板
        if (_components?.VotePanel != null && _components.VotePanel.activeSelf != active)
        {
            if (active)
                StartCoroutine(AnimatePanel(_components.VotePanel, true));
            else
                StartCoroutine(AnimatePanel(_components.VotePanel, false));
            _lastVoteActive = active;
        }

        if (!active) return;

        // 检查是否需要重建UI（只有状态改变时才重建）
        bool needsRebuild = false;
        string rebuildReason = "";

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
            var currentParticipants = new HashSet<string>(SceneNet.Instance.sceneParticipantIds);
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

        // 更新缓存
        _lastVoteActive = active;
        _lastVoteSceneId = SceneNet.Instance.sceneTargetId;
        _lastLocalReady = SceneNet.Instance.localReady;
        _lastVoteParticipants.Clear();
        foreach (var pid in SceneNet.Instance.sceneParticipantIds)
            _lastVoteParticipants.Add(pid);

        // 清空并重建投票面板内容（删除所有子对象）
        var childCount = _components.VotePanel.transform.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(_components.VotePanel.transform.GetChild(i).gameObject);
        }

        var sceneName = SceneInfoCollection.GetSceneInfo(SceneNet.Instance.sceneTargetId).DisplayName;

        // 标题
        var titleText = CreateText("VoteTitle", _components.VotePanel.transform, CoopLocalization.Get("ui.vote.title"), 22, ModernColors.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
        var titleLayout = titleText.gameObject.GetComponent<LayoutElement>();
        titleLayout.flexibleWidth = 0;
        titleLayout.preferredWidth = -1;

        var sceneText = CreateText("SceneName", _components.VotePanel.transform, sceneName, 18, ModernColors.Primary, TextAlignmentOptions.Left, FontStyles.Bold);
        var sceneLayout = sceneText.gameObject.GetComponent<LayoutElement>();
        sceneLayout.flexibleWidth = 0;
        sceneLayout.preferredWidth = -1;

        CreateDivider(_components.VotePanel.transform);

        // 准备状态卡片
        var readySection = CreateModernCard(_components.VotePanel.transform, "ReadySection");
        var readyLayout = readySection.GetComponent<LayoutElement>();
        if (readyLayout != null)
        {
            readyLayout.flexibleWidth = 0;
            readyLayout.minWidth = -1;
        }

        var readyText = CreateText("ReadyStatus", readySection.transform,
            SceneNet.Instance.localReady ? "[OK] " + CoopLocalization.Get("ui.vote.ready") : "[  ] " + CoopLocalization.Get("ui.vote.notReady"), 16,
            SceneNet.Instance.localReady ? ModernColors.Success : ModernColors.Warning);
        CreateText("ReadyHint", readySection.transform, CoopLocalization.Get("ui.vote.pressKey", readyKey, ""), 13, ModernColors.TextTertiary);

        // 取消投票按钮（只有房主才能看到）
        if (IsServer)
        {
            CreateDivider(_components.VotePanel.transform);
            var cancelButton = CreateModernButton("CancelVote", _components.VotePanel.transform,
                CoopLocalization.Get("ui.vote.cancel", "取消投票"),
                OnCancelVote, -1, ModernColors.Error, 40, 14);
        }

        // 玩家列表标题
        var listTitle = CreateText("PlayerListTitle", _components.VotePanel.transform, CoopLocalization.Get("ui.vote.playerReadyStatus"), 16, ModernColors.TextSecondary);
        var listTitleLayout = listTitle.gameObject.GetComponent<LayoutElement>();
        listTitleLayout.flexibleWidth = 0;
        listTitleLayout.preferredWidth = -1;

        // 玩家列表
        Debug.Log($"[VOTE-UI] 开始渲染玩家列表, participants={SceneNet.Instance.sceneParticipantIds.Count}, IsServer={IsServer}");
        
        // 根据SteamID去重
        var processedSteamIds = new HashSet<ulong>();
        
        foreach (var pid in SceneNet.Instance.sceneParticipantIds)
        {
            SceneNet.Instance.sceneReady.TryGetValue(pid, out var ready);
            var playerRow = CreateModernListItem(_components.VotePanel.transform, $"Player_{pid}");

            var statusIcon = CreateText("Status", playerRow.transform, ready ? CoopLocalization.Get("ui.vote.readyIcon") : CoopLocalization.Get("ui.vote.notReadyIcon"), 16,
                ready ? ModernColors.Success : ModernColors.TextTertiary);
            var statusLayout = statusIcon.gameObject.GetComponent<LayoutElement>();
            statusLayout.flexibleWidth = 0;
            statusLayout.preferredWidth = 60;

            // 获取玩家显示名称和ID
            string displayName = pid;
            string displayId = pid;
            bool isLocalPlayer = false;

            if (TransportMode == NetworkTransportMode.SteamP2P && SteamManager.Initialized && LobbyManager != null && LobbyManager.IsInLobby)
            {
                // Steam模式：pid 可能是 EndPoint 格式（Host:9050, Client:xxx）、SteamID 或 steam_xxxxx 格式
                ulong steamIdValue = 0;
                
                // v3格式：steam_xxxxx
                if (pid.StartsWith("steam_"))
                {
                    var steamIdStr = pid.Substring(6);
                    if (ulong.TryParse(steamIdStr, out steamIdValue) && steamIdValue > 0)
                    {
                        Debug.Log($"[VOTE-UI] v3格式解析成功: {pid} -> SteamID {steamIdValue}");
                    }
                    else
                    {
                        Debug.LogWarning($"[VOTE-UI] v3格式解析失败: {pid}");
                    }
                }
                // 旧格式处理
                else
                {

                // 先尝试直接解析为 SteamID
                if (ulong.TryParse(pid, out steamIdValue) && steamIdValue > 0)
                {
                    // pid 是 SteamID
                }
                else
                {
                    // pid 是 EndPoint 格式，需要转换为 SteamID
                    if (pid.StartsWith("Host:"))
                    {
                        // 主机的 EndPoint
                        // 先检查是否是本地玩家
                        if (localPlayerStatus != null && localPlayerStatus.EndPoint == pid)
                        {
                            steamIdValue = SteamUser.GetSteamID().m_SteamID;
                        }
                        else
                        {
                            // 远程主机，获取 Lobby 所有者的 SteamID
                            var lobbyOwner = SteamMatchmaking.GetLobbyOwner(LobbyManager.CurrentLobbyId);
                            steamIdValue = lobbyOwner.m_SteamID;
                        }
                    }
                    else if (pid.StartsWith("Client:"))
                    {
                        // 客户端的 EndPoint，尝试从 PlayerStatus 查找
                        // 先检查本地玩家
                        if (localPlayerStatus != null && localPlayerStatus.EndPoint == pid)
                        {
                            steamIdValue = SteamUser.GetSteamID().m_SteamID;
                        }
                        else
                        {
                            // 遍历所有玩家状态，找到匹配的 EndPoint
                            IEnumerable<PlayerStatus> allStatuses = IsServer
                                ? playerStatuses?.Values
                                : clientPlayerStatuses?.Values;
                            if (allStatuses != null)
                            {
                                foreach (var status in allStatuses)
                                {
                                    if (status.EndPoint == pid)
                                    {
                                        steamIdValue = GetSteamIdFromStatus(status);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // 尝试解析虚拟 IP 格式（10.255.0.x:port）
                        var parts = pid.Split(':');
                        if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var ipAddr) && int.TryParse(parts[1], out var port))
                        {
                            var ipEndPoint = new System.Net.IPEndPoint(ipAddr, port);
                            if (SteamEndPointMapper.Instance != null &&
                                SteamEndPointMapper.Instance.TryGetSteamID(ipEndPoint, out CSteamID cSteamId))
                            {
                                steamIdValue = cSteamId.m_SteamID;
                                Debug.Log($"[VOTE-UI] IP地址映射成功: {pid} -> SteamID {steamIdValue}");
                            }
                            else
                            {
                                Debug.LogWarning($"[VOTE-UI] IP地址映射失败: {pid}");
                                
                                // 客户端模式：如果映射失败，检查是否是自己的EndPoint
                                if (!IsServer && ModBehaviourF.Instance?.connectedPeer != null)
                                {
                                    var myEndPoint = ModBehaviourF.Instance.connectedPeer.EndPoint?.ToString();
                                    if (!string.IsNullOrEmpty(myEndPoint) && myEndPoint == pid)
                                    {
                                        steamIdValue = SteamUser.GetSteamID().m_SteamID;
                                        Debug.Log($"[VOTE-UI] 通过connectedPeer.EndPoint识别为本地玩家: {pid} -> SteamID {steamIdValue}");
                                    }
                                }
                            }
                        }
                    }
                }
                } // 闭合v3格式的else块

                // 判断是否是本地玩家
                var localSteamId = SteamUser.GetSteamID().m_SteamID;
                isLocalPlayer = (steamIdValue == localSteamId);
                Debug.Log($"[VOTE-UI] pid={pid}, steamIdValue={steamIdValue}, localSteamId={localSteamId}, isLocal={isLocalPlayer}");

                // 去重检查：如果SteamID已处理，销毁重复的playerRow并跳过
                if (steamIdValue > 0 && processedSteamIds.Contains(steamIdValue))
                {
                    Debug.Log($"[VOTE-UI] 跳过重复玩家: pid={pid}, steamIdValue={steamIdValue}");
                    Destroy(playerRow);
                    continue;
                }
                
                if (steamIdValue > 0)
                {
                    processedSteamIds.Add(steamIdValue);
                }

                // 如果成功获取到 SteamID，显示用户名
                if (steamIdValue > 0)
                {
                    var cSteamId = new CSteamID(steamIdValue);
                    string cachedName = LobbyManager.GetCachedMemberName(cSteamId);

                    if (!string.IsNullOrEmpty(cachedName))
                    {
                        // 判断是否是主机
                        var lobbyOwner = SteamMatchmaking.GetLobbyOwner(LobbyManager.CurrentLobbyId);
                        string prefix = (steamIdValue == lobbyOwner.m_SteamID) ? "HOST" : "CLIENT";
                        
                        // 如果是本地玩家，添加[本地]标识
                        if (isLocalPlayer)
                        {
                            displayName = $"[本地] {prefix}_{cachedName}";
                        }
                        else
                        {
                            displayName = $"{prefix}_{cachedName}";
                        }
                    }
                    else
                    {
                        // 缓存未命中，回退到Steam API
                        string steamUsername = SteamFriends.GetFriendPersonaName(cSteamId);
                        if (!string.IsNullOrEmpty(steamUsername) && steamUsername != "[unknown]")
                        {
                            var lobbyOwner = SteamMatchmaking.GetLobbyOwner(LobbyManager.CurrentLobbyId);
                            string prefix = (steamIdValue == lobbyOwner.m_SteamID) ? "HOST" : "CLIENT";
                            
                            // 如果是本地玩家，添加[本地]标识
                            if (isLocalPlayer)
                            {
                                displayName = $"[本地] {prefix}_{steamUsername}";
                            }
                            else
                            {
                                displayName = $"{prefix}_{steamUsername}";
                            }
                        }
                        else
                        {
                            displayName = $"Player_{steamIdValue.ToString().Substring(Math.Max(0, steamIdValue.ToString().Length - 4))}";
                        }
                    }

                    displayId = steamIdValue.ToString();
                }
            }

            // 显示名称和ID
            Debug.Log($"[VOTE-UI] 渲染玩家: pid={pid}, displayName={displayName}, isLocal={isLocalPlayer}");
            var nameText = CreateText("Name", playerRow.transform, displayName, 14, isLocalPlayer ? ModernColors.Success : ModernColors.TextPrimary);
            var nameLayout = nameText.gameObject.GetComponent<LayoutElement>();
            nameLayout.flexibleWidth = 1;

            CreateText("ID", playerRow.transform, displayId, 12, ModernColors.TextSecondary);
        }
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
            if (_components.SpectatorPanel.activeSelf != shouldShow)
            {
                if (shouldShow)
                    StartCoroutine(AnimatePanel(_components.SpectatorPanel, true));
                else
                    StartCoroutine(AnimatePanel(_components.SpectatorPanel, false));
            }
        }
    }

    private bool IsMapOpen()
    {
        var instance = MiniMapView.Instance;
        return instance != null && View.ActiveView == instance;
    }

    #endregion

    #region 断开连接弹窗

    private GameObject _disconnectDialog;

    public void ShowDisconnectDialog(string reason)
    {
        if (_disconnectDialog != null)
        {
            Destroy(_disconnectDialog);
        }

        _disconnectDialog = CreateModernPanel("DisconnectDialog", _canvas.transform, new Vector2(500, 250), new Vector2(960, 100), TextAnchor.LowerCenter);
        
        var dialogGroup = _disconnectDialog.GetComponent<CanvasGroup>();
        if (dialogGroup == null) dialogGroup = _disconnectDialog.AddComponent<CanvasGroup>();
        dialogGroup.alpha = 0f;
        
        var headerRow = CreateHorizontalGroup(_disconnectDialog.transform, "Header");
        var headerLayout = headerRow.AddComponent<LayoutElement>();
        headerLayout.minHeight = 50;
        headerLayout.preferredHeight = 50;
        
        var titleText = CreateText("Title", headerRow.transform, "连接已断开", 20, ModernColors.Error, TextAlignmentOptions.Center, FontStyles.Bold);
        var titleLayout = titleText.gameObject.GetComponent<LayoutElement>();
        titleLayout.flexibleWidth = 1;
        
        CreateDivider(_disconnectDialog.transform);
        
        var contentRow = new GameObject("Content");
        contentRow.transform.SetParent(_disconnectDialog.transform, false);
        var contentLayout = contentRow.AddComponent<LayoutElement>();
        contentLayout.flexibleHeight = 1;
        
        var reasonText = CreateText("Reason", contentRow.transform, reason, 16, ModernColors.TextPrimary, TextAlignmentOptions.Center);
        
        CreateDivider(_disconnectDialog.transform);
        
        var buttonRow = CreateHorizontalGroup(_disconnectDialog.transform, "ButtonRow");
        var buttonLayout = buttonRow.AddComponent<LayoutElement>();
        buttonLayout.minHeight = 60;
        buttonLayout.preferredHeight = 60;
        
        var okButtonGO = new GameObject("OKButton");
        okButtonGO.transform.SetParent(buttonRow.transform, false);
        var okRect = okButtonGO.AddComponent<RectTransform>();
        var okLayout = okButtonGO.AddComponent<LayoutElement>();
        okLayout.flexibleWidth = 1;
        okLayout.minHeight = 40;
        
        var okImage = okButtonGO.AddComponent<Image>();
        okImage.color = ModernColors.Primary;
        
        var okButton = okButtonGO.AddComponent<Button>();
        okButton.onClick.AddListener(() => 
        {
            if (_disconnectDialog != null)
            {
                Destroy(_disconnectDialog);
                _disconnectDialog = null;
            }
        });
        
        CreateText("ButtonText", okButtonGO.transform, "确定", 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        
        StartCoroutine(AnimateDialog(dialogGroup));
    }
    
    private IEnumerator AnimateDialog(CanvasGroup group)
    {
        if (group == null) yield break;
        
        float duration = 0.3f;
        float elapsed = 0f;
        
        while (elapsed < duration && group != null)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(0f, 1f, elapsed / duration);
            yield return null;
        }
        
        if (group != null)
            group.alpha = 1f;
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

        // --- 🎨 尝试使用真实高斯模糊玻璃效果 ---
        bool useTranslucentImage = false;
        TranslucentImage translucentImage = null;

        try
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var source = mainCamera.GetComponent<TranslucentImageSource>();
                if (source != null)
                {
                    translucentImage = panel.AddComponent<TranslucentImage>();
                    translucentImage.source = source;
                    translucentImage.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);
                    useTranslucentImage = true;

                    // 延迟设置属性
                    StartCoroutine(InitializeTranslucentImageProperties(translucentImage));
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"TranslucentImage 初始化失败，使用普通背景: {e.Message}");
            if (translucentImage != null)
            {
                Destroy(translucentImage);
                translucentImage = null;
            }
            useTranslucentImage = false;
        }

        // 回退方案：使用普通 Image + 噪声纹理
        if (!useTranslucentImage)
        {
            var image = panel.AddComponent<Image>();
            image.color = new Color(0.25f, 0.25f, 0.25f, 0.93f);
            image.sprite = CreateEmbeddedNoiseSprite();
            image.type = Image.Type.Tiled;
        }

        // 添加柔和阴影
        var shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = MModUI.ModernColors.Shadow;
        shadow.effectDistance = new Vector2(0, -4);

        // 添加浅描边
        var outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        outline.effectDistance = new Vector2(1, -1);

        // 用于淡入淡出动画
        panel.AddComponent<CanvasGroup>();

        return panel;
    }

    private IEnumerator LoadSteamAvatar(CSteamID steamID, Image targetImage)
    {
        if (!SteamManager.Initialized || targetImage == null)
        {
            Debug.LogWarning($"[MModUI] LoadSteamAvatar 取消: SteamManager={SteamManager.Initialized}, targetImage={targetImage != null}");
            yield break;
        }
        
        ulong steamId = steamID.m_SteamID;
        
        if (_steamAvatarLoading.Contains(steamId))
        {
            yield break;
        }
        
        _steamAvatarLoading.Add(steamId);
        
        try
        {
            int avatarHandle = SteamFriends.GetMediumFriendAvatar(steamID);
            Debug.Log($"[MModUI] LoadSteamAvatar for {steamId}: handle={avatarHandle}");
            
            if (avatarHandle == -1)
            {
                yield return new WaitForSeconds(0.5f);
                avatarHandle = SteamFriends.GetMediumFriendAvatar(steamID);
                Debug.Log($"[MModUI] LoadSteamAvatar 重试 for {steamId}: handle={avatarHandle}");
            }
            
            if (avatarHandle > 0)
            {
                uint width, height;
                if (SteamUtils.GetImageSize(avatarHandle, out width, out height))
                {
                    Debug.Log($"[MModUI] Steam头像尺寸: {width}x{height}");
                    if (width > 0 && height > 0)
                    {
                        byte[] imageData = new byte[width * height * 4];
                        if (SteamUtils.GetImageRGBA(avatarHandle, imageData, (int)(width * height * 4)))
                        {
                            Texture2D texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                            texture.LoadRawTextureData(imageData);
                            texture.Apply();
                            
                            for (int y = 0; y < height / 2; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    Color temp = texture.GetPixel(x, y);
                                    texture.SetPixel(x, y, texture.GetPixel(x, (int)height - 1 - y));
                                    texture.SetPixel(x, (int)height - 1 - y, temp);
                                }
                            }
                            texture.Apply();
                            
                            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
                            _steamAvatarCache[steamId] = sprite;
                            
                            if (targetImage != null)
                            {
                                targetImage.sprite = sprite;
                                targetImage.color = Color.white;
                            }
                            Debug.Log($"[MModUI] Steam头像加载成功并缓存: {steamId}");
                        }
                        else
                        {
                            Debug.LogWarning($"[MModUI] GetImageRGBA 失败: {steamId}");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[MModUI] GetImageSize 失败: {steamId}");
                }
            }
            else if (avatarHandle == 0)
            {
                Debug.LogWarning($"[MModUI] Steam头像未准备好: {steamId}");
            }
        }
        finally
        {
            _steamAvatarLoading.Remove(steamId);
        }
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

    internal GameObject CreateTitleBar(Transform parent)
    {
        var titleBar = CreateHorizontalGroup(parent, "TitleBar");
        var layout = titleBar.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 15, 15);
        layout.spacing = 12;

        // --- 背景改成毛玻璃风格 ---
        var bg = titleBar.AddComponent<Image>();
        bg.color = GlassTheme.CardBg;                // 半透明灰
        bg.sprite = CreateEmbeddedNoiseSprite();     // 噪声纹理
        bg.type = Image.Type.Tiled;

        // 轻描边 + 柔光阴影
        var outline = titleBar.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = titleBar.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.25f);
        shadow.effectDistance = new Vector2(0, -2);

        var layoutElement = titleBar.GetComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1;
        layoutElement.preferredHeight = 60;
        layoutElement.minHeight = 60;
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
        image.color = new Color(0.28f, 0.28f, 0.28f, 0.87f); // 比主面板略浅
        image.sprite = CreateEmbeddedNoiseSprite();
        image.type = Image.Type.Tiled;

        // 内部柔和描边
        var outline = content.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.06f);
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
        layout.padding = new RectOffset(15, 15, 12, 12);
        layout.spacing = 8;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;

        var image = card.AddComponent<Image>();
        image.color = GlassTheme.CardBg;
        image.sprite = CreateEmbeddedNoiseSprite();
        image.type = Image.Type.Tiled;

        var outline = card.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = card.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.25f);
        shadow.effectDistance = new Vector2(0, -3);

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
        image.color = GlassTheme.CardBg;
        image.sprite = CreateEmbeddedNoiseSprite();
        image.type = Image.Type.Tiled;

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
        bg.color = GlassTheme.CardBg;
        bg.sprite = CreateEmbeddedNoiseSprite();
        bg.type = Image.Type.Tiled;

        var outline = bar.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = bar.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.25f);
        shadow.effectDistance = new Vector2(0, -2);

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
        bg.color = new Color(0.22f, 0.22f, 0.22f, 0.95f); // 比主面板略深
        bg.sprite = CreateEmbeddedNoiseSprite();
        bg.type = Image.Type.Tiled;

        var outline = bar.AddComponent<Outline>();
        outline.effectColor = GlassTheme.Divider;
        outline.effectDistance = new Vector2(1, -1);

        var shadow = bar.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.3f);
        shadow.effectDistance = new Vector2(0, -3);

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
        bg.color = new Color(0.32f, 0.32f, 0.32f, 0.94f); // 浅灰半透
        bg.sprite = CreateEmbeddedNoiseSprite();
        bg.type = Image.Type.Tiled;

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
        bg.color = new Color(color.r, color.g, color.b, 0.2f);

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

        var baseColor = GlassTheme.ButtonBg;
        var image = btnObj.AddComponent<Image>();
        image.color = baseColor;
        image.sprite = CreateEmbeddedNoiseSprite();
        image.type = Image.Type.Tiled;

        var button = btnObj.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        button.targetGraphic = image;

        var colors = button.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = GlassTheme.ButtonHover;
        colors.pressedColor = GlassTheme.ButtonActive;
        colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.4f);
        colors.fadeDuration = 0.15f;
        button.colors = colors;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = GlassTheme.Text;
        tmp.fontStyle = FontStyles.Bold;

        var rect = tmp.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        return button;
    }


    internal Button CreateIconButton(string name, Transform parent, string icon, UnityEngine.Events.UnityAction onClick, float size = 32, Color? color = null)
    {
        var btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        var layoutElement = btnObj.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = size;
        layoutElement.preferredHeight = size;

        var btnColor = color ?? ModernColors.TextSecondary;
        var image = btnObj.AddComponent<Image>();
        image.color = new Color(0, 0, 0, 0); // 透明背景

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, 0);
        colors.highlightedColor = new Color(1, 1, 1, 0.1f);
        colors.pressedColor = new Color(1, 1, 1, 0.2f);
        colors.disabledColor = new Color(1, 1, 1, 0);
        button.colors = colors;

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

        return button;
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
        image.color = GlassTheme.InputBg;
        image.sprite = CreateEmbeddedNoiseSprite();
        image.type = Image.Type.Tiled;

        var input = inputObj.AddComponent<TMP_InputField>();

        var textArea = new GameObject("TextArea");
        textArea.transform.SetParent(inputObj.transform, false);
        var rect = textArea.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12, 8);
        rect.offsetMax = new Vector2(-12, -8);

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        var tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.color = GlassTheme.Text;
        tmpText.fontSize = 15;
        tmpText.alignment = TextAlignmentOptions.Left;

        var placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        var placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.color = GlassTheme.TextSecondary;
        placeholderText.fontSize = 15;
        placeholderText.fontStyle = FontStyles.Italic;

        input.textViewport = rect;
        input.textComponent = tmpText;
        input.placeholder = placeholderText;
        input.text = defaultValue;

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
        scrollImage.color = GlassTheme.CardBg;              // 半透明深灰
        scrollImage.sprite = CreateEmbeddedNoiseSprite();   // 噪声散射
        scrollImage.type = Image.Type.Tiled;

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
        viewportImage.color = new Color(0.27f, 0.27f, 0.27f, 0.95f); // 略深一点，聚焦内容
        viewportImage.sprite = CreateEmbeddedNoiseSprite();
        viewportImage.type = Image.Type.Tiled;

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
        scrollbarRect.sizeDelta = new Vector2(8, 0);

        var scrollbarImage = scrollbarObj.AddComponent<Image>();
        scrollbarImage.color = new Color(1f, 1f, 1f, 0.05f); // 轻微透白

        var scrollbar = scrollbarObj.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.targetGraphic = scrollbarImage;

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

        // 刷新按钮
        CreateModernButton("RefreshBtn", steamHeaderGroup.transform, CoopLocalization.Get("ui.steam.refresh"), () =>
        {
            if (LobbyManager != null) LobbyManager.RequestLobbyList();
        }, 120, ModernColors.Primary, 38, 15);

        // Steam房间列表滚动视图
        var lobbyScroll = CreateModernScrollView("SteamLobbyScroll", parent, 445);
        var scrollLayout = lobbyScroll.GetComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1;
        components.SteamLobbyListContent = lobbyScroll.transform.Find("Viewport/Content");

        // 加入密码输入区域
        var passCard = CreateModernCard(parent, "JoinPassCard");
        var passCardLayout = passCard.GetComponent<LayoutElement>();
        passCardLayout.preferredHeight = 80;
        passCardLayout.minHeight = 80;

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
        if (_components?.DirectServerListArea != null && _components?.SteamServerListArea != null)
        {
            _components.DirectServerListArea.SetActive(TransportMode == NetworkTransportMode.Direct);
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
        try
        {
            var lm = LevelManager.Instance;
            if (lm == null)
            {
                Debug.LogWarning("LevelManager.Instance is null. Make sure you are in a game level.");
                SetStatusText("[!] " + CoopLocalization.Get("ui.error.mustInLevel"), ModernColors.Warning);
                return;
            }

            var lootBoxes = LevelManager.LootBoxInventories;
            if (lootBoxes == null)
            {
                Debug.LogWarning("LootBoxInventories is null. Make sure you are in a game level.");
                SetStatusText("[!] " + CoopLocalization.Get("ui.error.mustInLevel"), ModernColors.Warning);
                return;
            }

            var count = 0;
            foreach (var i in lootBoxes)
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
        catch (Exception ex)
        {
            Debug.LogError($"[DebugPrintLootBoxes] Exception: {ex.Message}");
            SetStatusText("[!] Error printing loot boxes", ModernColors.Error);
        }
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

        // 检查列表是否改变
        var currentLobbies = new HashSet<ulong>(_steamLobbyInfos.Select(l => l.LobbyId.m_SteamID));

        // 如果列表没有变化，直接返回（避免重复创建UI）
        if (_displayedSteamLobbies.SetEquals(currentLobbies))
            return;

        // 列表改变了，需要重建UI
        Debug.Log($"[MModUI] Steam房间列表已更新，重建UI (当前: {currentLobbies.Count}, 之前: {_displayedSteamLobbies.Count})");

        // 清空现有列表
        foreach (Transform child in _components.SteamLobbyListContent)
            Destroy(child.gameObject);

        // 更新缓存
        _displayedSteamLobbies.Clear();
        foreach (var id in currentLobbies)
            _displayedSteamLobbies.Add(id);

        if (_steamLobbyInfos.Count == 0)
        {
            CreateText("EmptyHint", _components.SteamLobbyListContent, CoopLocalization.Get("ui.steam.lobbiesEmpty"), 14, ModernColors.TextTertiary, TextAlignmentOptions.Center);
            return;
        }

        // 创建房间条目
        foreach (var lobby in _steamLobbyInfos)
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

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _originalScale = _rectTransform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_isPressed)
        {
            if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
            _scaleCoroutine = StartCoroutine(ScaleToWithEasing(Vector3.one * 1.05f, 0.2f, EaseOutBack));
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isPressed = false;
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(ScaleToWithEasing(_originalScale, 0.2f, EaseOutCubic));
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(ScaleToWithEasing(Vector3.one * 0.95f, 0.1f, EaseOutCubic));
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(ScaleToWithEasing(Vector3.one * 1.05f, 0.15f, EaseOutBack));
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