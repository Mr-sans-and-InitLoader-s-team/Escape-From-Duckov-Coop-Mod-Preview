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

using static NodeCanvas.Tasks.Actions.CurveTransformTween;
using System.Linq;

namespace EscapeFromDuckovCoopMod;

public class ModUI : MonoBehaviour
{
    public static ModUI Instance;

    public bool showUI = true;
    public bool showPlayerStatusWindow;
    public KeyCode toggleWindowKey = KeyCode.P;

    private readonly List<string> _hostList = new();
    private readonly HashSet<string> _hostSet = new();
    public readonly KeyCode readyKey = KeyCode.J;
    private string _manualIP = "192.168.123.1";
    private string _manualPort = "9050";
    private int _port = 9050;
    private string _status = "";
    private Rect mainWindowRect = new(10, 10, 500, 600);
    private Vector2 playerStatusScrollPos = Vector2.zero;
    private Rect playerStatusWindowRect = new(420, 10, 300, 400);
    private readonly List<SteamLobbyManager.LobbyInfo> _steamLobbyInfos = new();
    private string _steamLobbyName = string.Empty;
    private string _steamLobbyPassword = string.Empty;
    private bool _steamLobbyFriendsOnly;
    private int _steamLobbyMaxPlayers = 4;
    private string _steamJoinPassword = string.Empty;
    private bool _steamShowLobbyBrowser;
    private string _roomSearchText = string.Empty;
    
    // 界面状态管理
    private enum UIState
    {
        MainLobby,
        InRoom
    }
    
    private UIState _currentUIState = UIState.MainLobby;
    
    // 聊天消息存储
    private readonly List<string> _chatMessages = new List<string>();
    private readonly int _maxChatMessages = 5;
    
    // 状态切换方法
    private void ChangeUIState(UIState newState)
    {
        if (_currentUIState == newState)
            return;
            
        var previousState = _currentUIState;
        _currentUIState = newState;
        
        // 状态变化时的数据更新逻辑
        OnUIStateChanged(previousState, newState);
    }
    
    // 状态变化时的处理逻辑
    private void OnUIStateChanged(UIState previousState, UIState newState)
    {
        switch (newState)
        {
            case UIState.MainLobby:
                // 切换到主大厅时的处理
                OnEnterMainLobby(previousState);
                break;
                
            case UIState.InRoom:
                // 切换到房间界面时的处理
                OnEnterRoom(previousState);
                break;
        }
    }
    
    // 进入主大厅状态的处理
    private void OnEnterMainLobby(UIState previousState)
    {
        // 如果从房间返回，清理房间相关数据
        if (previousState == UIState.InRoom)
        {
            // 清理房间搜索文本
            _roomSearchText = string.Empty;
            
            // 刷新房间列表
            RefreshRoomList();
        }
        
        // 更新状态显示
        if (string.IsNullOrEmpty(status) || status.Contains("房间"))
        {
            status = "已返回主大厅";
        }
    }
    
    // 进入房间状态的处理
    private void OnEnterRoom(UIState previousState)
    {
        // 进入房间时更新相关数据
        if (previousState == UIState.MainLobby)
        {
            // 更新房间信息
            UpdateRoomData();
            
            // 初始化Steam大厅成员计数
            InitializeSteamLobbyMemberTracking();
        }
        
        // 更新状态显示
        var roomName = GetCurrentRoomName();
        status = $"已进入房间: {roomName}";
    }
    
    // 初始化Steam大厅成员跟踪
    private void InitializeSteamLobbyMemberTracking()
    {
        var manager = LobbyManager;
        if (manager != null && manager.IsInLobby && SteamManager.Initialized)
        {
            try
            {
                _lastSteamLobbyMemberCount = Steamworks.SteamMatchmaking.GetNumLobbyMembers(manager.CurrentLobbyId);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ModUI] 初始化Steam大厅成员跟踪时发生错误: {ex.Message}");
                _lastSteamLobbyMemberCount = 0;
            }
        }
        else
        {
            _lastSteamLobbyMemberCount = 0;
        }
    }
    
    // 更新房间数据
    private void UpdateRoomData()
    {
        var manager = LobbyManager;
        if (manager != null && manager.IsInLobby)
        {
            // 从Steam大厅获取最新信息
            if (manager.TryGetLobbyInfo(manager.CurrentLobbyId, out var lobbyInfo))
            {
                _steamLobbyName = lobbyInfo.LobbyName;
                _steamLobbyMaxPlayers = lobbyInfo.MaxMembers;
            }
        }
    }
    private NetService Service => NetService.Instance;
    private bool IsServer => Service != null && Service.IsServer;
    private NetManager netManager => Service?.netManager;
    private NetDataWriter writer => Service?.writer;
    private NetPeer connectedPeer => Service?.connectedPeer;
    private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private bool networkStarted => Service != null && Service.networkStarted;

    private string manualIP
    {
        get => Service?.manualIP ?? _manualIP;
        set
        {
            _manualIP = value;
            if (Service != null) Service.manualIP = value;
        }
    }

    private string manualPort
    {
        get => Service?.manualPort ?? _manualPort;
        set
        {
            _manualPort = value;
            if (Service != null) Service.manualPort = value;
        }
    }

    private string status
    {
        get => Service?.status ?? _status;
        set
        {
            _status = value;
            if (Service != null) Service.status = value;
        }
    }

    private int port => Service?.port ?? _port;

    private List<string> hostList => Service?.hostList ?? _hostList;
    private HashSet<string> hostSet => Service?.hostSet ?? _hostSet;

    private Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
    private Dictionary<string, PlayerStatus> clientPlayerStatuses => Service?.clientPlayerStatuses;

    private SteamLobbyManager LobbyManager => SteamLobbyManager.Instance;
    private NetworkTransportMode TransportMode => Service?.TransportMode ?? NetworkTransportMode.Direct;


    void Update()
    {
        // 언어 변경 감지 및 자동 리로드
        CoopLocalization.CheckLanguageChange();
        
        // 监听连接状态变化
        MonitorConnectionStatus();
        
        // 处理聊天输入
        HandleChatInput();
    }
    
    /// <summary>
    /// 处理聊天输入
    /// </summary>
    private void HandleChatInput()
    {
        // 只在房间界面中处理聊天输入
        if (_currentUIState != UIState.InRoom)
            return;
            
        try
        {
            // 检查T键输入（打开聊天）
            if (Input.GetKeyDown(KeyCode.T))
            {
                ShowChatInputDialog();
            }
            
            // 检查Enter键输入
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                ShowChatInputDialog();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 处理聊天输入时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 显示聊天输入对话框
    /// </summary>
    private void ShowChatInputDialog()
    {
        try
        {
            // 使用ChatInputDialog显示输入框
            string userInput = EscapeFromDuckovCoopMod.Chat.UI.ChatInputDialog.ShowInputDialog(
                "聊天输入", 
                "请输入聊天消息:", 
                ""
            );
            
            if (!string.IsNullOrEmpty(userInput))
            {
                HandleChatMessageSent(userInput);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 显示聊天输入对话框失败: {ex.Message}");
        }
    }
    
    // 连接状态监听
    private void MonitorConnectionStatus()
    {
        // 检查是否成功进入房间
        CheckRoomEntryStatus();
        
        // 检查连接失败状态
        CheckConnectionFailureStatus();
        
        // 监听Steam大厅成员变化
        MonitorSteamLobbyMembers();
    }
    
    // 上次检查的Steam大厅成员数量
    private int _lastSteamLobbyMemberCount = 0;
    
    // 监听Steam大厅成员变化
    private void MonitorSteamLobbyMembers()
    {
        var manager = LobbyManager;
        if (manager == null || !manager.IsInLobby || !SteamManager.Initialized || _currentUIState != UIState.InRoom)
        {
            return;
        }
        
        try
        {
            var currentMemberCount = Steamworks.SteamMatchmaking.GetNumLobbyMembers(manager.CurrentLobbyId);
            
            // 如果成员数量发生变化，更新数据
            if (currentMemberCount != _lastSteamLobbyMemberCount)
            {
                _lastSteamLobbyMemberCount = currentMemberCount;
                
                // 更新房间数据
                UpdateRoomData();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 监听Steam大厅成员变化时发生错误: {ex.Message}");
        }
    }
    
    // 检查房间进入状态
    private void CheckRoomEntryStatus()
    {
        var manager = LobbyManager;
        
        // 检查Steam房间状态
        if (manager != null && manager.IsInLobby && _currentUIState == UIState.MainLobby)
        {
            // 成功进入Steam房间，切换到房间界面
            ChangeUIState(UIState.InRoom);
            return;
        }
        
        // 检查直连状态
        if (Service != null && Service.networkStarted && connectedPeer != null && _currentUIState == UIState.MainLobby)
        {
            // 成功建立直连，切换到房间界面
            ChangeUIState(UIState.InRoom);
            return;
        }
    }
    
    // 检查连接失败状态
    private void CheckConnectionFailureStatus()
    {
        // 如果正在连接中，不检查连接失败（避免误判）
        if (Service != null && Service.isConnecting)
        {
            return;
        }
        
        // 检查Steam连接失败
        if (LobbyManager != null && !LobbyManager.IsInLobby && _currentUIState == UIState.InRoom)
        {
            // Steam房间连接丢失，返回主大厅
            if (Service == null || !Service.networkStarted || connectedPeer == null)
            {
                HandleConnectionLoss("Steam房间连接已断开");
            }
        }
        
        // 检查直连失败
        if (Service != null && (!Service.networkStarted || connectedPeer == null) && _currentUIState == UIState.InRoom)
        {
            // 直连丢失，返回主大厅
            if (LobbyManager == null || !LobbyManager.IsInLobby)
            {
                HandleConnectionLoss("网络连接已断开");
            }
        }
    }
    
    // 处理连接丢失
    private void HandleConnectionLoss(string reason)
    {
        Debug.LogWarning($"[ModUI] 检测到连接丢失: {reason}");
        
        // 执行清理逻辑
        ClearRoomData();
        
        // 切换到主大厅状态
        ChangeUIState(UIState.MainLobby);
        
        // 显示错误信息
        ShowConnectionError(reason);
    }
    
    // 显示连接错误
    private void ShowConnectionError(string errorMessage)
    {
        status = errorMessage;
        Debug.LogWarning($"[ModUI] 连接错误: {errorMessage}");
    }

    private void OnGUI()
    {
        if (showUI)
        {
            mainWindowRect = GUI.Window(94120, mainWindowRect, DrawMainWindow, CoopLocalization.Get("ui.window.title"));

            if (showPlayerStatusWindow)
            {
                playerStatusWindowRect = GUI.Window(94121, playerStatusWindowRect, DrawPlayerStatusWindow, CoopLocalization.Get("ui.window.playerStatus"));
            }
        }

        if (SceneNet.Instance.sceneVoteActive)
        {
            var h = 220f;
            var area = new Rect(10, Screen.height * 0.5f - h * 0.5f, 320, h);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label(CoopLocalization.Get("ui.vote.mapVote", SceneInfoCollection.GetSceneInfo(SceneNet.Instance.sceneTargetId).DisplayName));
            var readyStatus = SceneNet.Instance.localReady ? CoopLocalization.Get("ui.vote.ready") : CoopLocalization.Get("ui.vote.notReady");
            GUILayout.Label(CoopLocalization.Get("ui.vote.pressKey", readyKey, readyStatus));

            GUILayout.Space(8);
            GUILayout.Label(CoopLocalization.Get("ui.vote.playerReadyStatus"));
            foreach (var pid in SceneNet.Instance.sceneParticipantIds)
            {
                var r = false;
                SceneNet.Instance.sceneReady.TryGetValue(pid, out r);
                GUILayout.Label($"• {pid}  —— {(r ? CoopLocalization.Get("ui.vote.readyIcon") : CoopLocalization.Get("ui.vote.notReadyIcon"))}");
            }

            GUILayout.EndArea();
        }

        if (Spectator.Instance._spectatorActive)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontSize = 18
            };
            style.normal.textColor = Color.white;

            //string who = "";
            try
            {
                // var cmc = (_spectateIdx >= 0 && _spectateIdx < _spectateList.Count) ? _spectateList[_spectateIdx] : null;
                // who = cmc ? (cmc.name ?? "队友") : "队友";
            }
            catch
            {
            }

            GUI.Label(new Rect(0, Screen.height - 40, Screen.width, 30),
                CoopLocalization.Get("ui.spectator.mode"), style);
        }
    }


    public void Init()
    {
        Instance = this;
        
        // 初始化时确保界面状态正确
        InitializeUIState();
        
        // 初始化聊天系统
        InitializeChatSystem();
        
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

            var options = svc.LobbyOptions;
            _steamLobbyName = options.LobbyName;
            _steamLobbyPassword = options.Password;
            _steamLobbyFriendsOnly = options.Visibility == SteamLobbyVisibility.FriendsOnly;
            _steamLobbyMaxPlayers = Mathf.Clamp(options.MaxPlayers, 2, 16);
        }

        if (LobbyManager != null)
        {
            LobbyManager.LobbyListUpdated -= OnLobbyListUpdated;
            LobbyManager.LobbyListUpdated += OnLobbyListUpdated;
            _steamLobbyInfos.Clear();
            _steamLobbyInfos.AddRange(LobbyManager.AvailableLobbies);
        }
    }
    
    /// <summary>
    /// 初始化聊天系统
    /// </summary>
    private void InitializeChatSystem()
    {
        try
        {
            // 获取或创建聊天UI管理器
            var chatUIManager = EscapeFromDuckovCoopMod.Chat.UI.ChatUIManager.GetOrCreateInstance();
            
            // 订阅消息发送事件
            chatUIManager.OnMessageSent += HandleChatMessageSent;
            
            // 初始化聊天管理器
            var localChatManager = EscapeFromDuckovCoopMod.Chat.Managers.LocalChatManager.Instance;
            if (localChatManager != null)
            {
                localChatManager.Initialize();
            }
            
            // 获取全局输入管理器实例（会自动初始化）
            var globalInputManager = EscapeFromDuckovCoopMod.Chat.Input.GlobalInputManager.Instance;
            
            Debug.Log("[ModUI] 聊天系统初始化完成");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 聊天系统初始化失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 处理聊天消息发送
    /// </summary>
    /// <param name="messageContent">消息内容</param>
    private void HandleChatMessageSent(string messageContent)
    {
        try
        {
            Debug.Log($"[ModUI] 收到聊天消息: {messageContent}");
            
            // 获取当前用户信息
            var userName = "本地玩家";
            if (SteamManager.Initialized)
            {
                userName = Steamworks.SteamFriends.GetPersonaName();
            }
            
            // 创建聊天消息
            var chatMessage = new EscapeFromDuckovCoopMod.Chat.Models.ChatMessage
            {
                Content = messageContent,
                Sender = new EscapeFromDuckovCoopMod.Chat.Models.UserInfo
                {
                    UserName = userName,
                    DisplayName = userName
                },
                Type = EscapeFromDuckovCoopMod.Chat.Models.MessageType.Normal
            };
            
            // 通过网络发送消息
            if (Service != null && Service.networkStarted)
            {
                Debug.Log($"[ModUI] 准备通过网络发送聊天消息: {messageContent}");
                SendChatMessageToNetwork(chatMessage);
                
                // 网络模式下，不在本地立即显示，等收到广播后再显示
                // 这样可以确保消息确实发送成功，并且避免重复显示
                Debug.Log($"[ModUI] 网络模式：等待消息广播后显示");
            }
            else
            {
                Debug.LogWarning($"[ModUI] 网络未启动，消息仅在本地显示");
                
                // 只有在网络未启动时才在本地显示
                // 添加到本地聊天历史
                var localChatManager = EscapeFromDuckovCoopMod.Chat.Managers.LocalChatManager.Instance;
                if (localChatManager != null)
                {
                    localChatManager.SendMessage(messageContent);
                }
                
                // 添加消息到本地显示存储
                var displayMessage = $"{userName}: {messageContent}";
                _chatMessages.Add(displayMessage);
                
                // 限制消息数量
                if (_chatMessages.Count > _maxChatMessages)
                {
                    _chatMessages.RemoveAt(0);
                }
                
                // 也添加到ChatUIManager（如果需要）
                var chatUIManager = EscapeFromDuckovCoopMod.Chat.UI.ChatUIManager.Instance;
                if (chatUIManager != null && chatUIManager.IsInitialized)
                {
                    chatUIManager.AddMessage(chatMessage);
                    Debug.Log($"[ModUI] 消息已添加到UI显示");
                }
            }
            
            // 更新状态显示
            status = $"发送消息: {messageContent}";
            
            Debug.Log($"[ModUI] 聊天消息处理完成: {userName}: {messageContent}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 处理聊天消息时发生错误: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 通过网络发送聊天消息
    /// 完全不依赖 LiteNetLib，使用统一传输层（支持 Steam P2P 和直连 UDP）
    /// </summary>
    /// <param name="message">聊天消息</param>
    private void SendChatMessageToNetwork(EscapeFromDuckovCoopMod.Chat.Models.ChatMessage message)
    {
        try
        {
            Debug.Log($"[ModUI] 开始发送网络聊天消息: {message.Content}");
            
            // 序列化消息为JSON
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(message);
            Debug.Log($"[ModUI] 消息已序列化: {json}");
            
            // 使用统一传输层发送消息（完全不依赖 LiteNetLib）
            bool success = EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.SendChatMessage(json);
            
            if (success)
            {
                Debug.Log($"[ModUI] ✓ 聊天消息已通过统一传输层发送");
                Debug.Log($"[ModUI] 传输状态: {EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.GetTransportStatus()}");
                
                // 如果是主机，本地也显示这条消息
                if (IsServer)
                {
                    var displayMessage = $"{message.Sender?.UserName ?? "未知"}: {message.Content}";
                    AddChatMessage(displayMessage);
                    Debug.Log($"[ModUI] 主机本地显示自己发送的消息: {displayMessage}");
                }
                else
                {
                    // 客机发送成功后，等待主机广播回来再显示
                    Debug.Log($"[ModUI] 客机消息已发送，等待主机广播");
                }
            }
            else
            {
                Debug.LogError($"[ModUI] ✗ 聊天消息发送失败");
                Debug.LogError($"[ModUI] 传输状态: {EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.GetTransportStatus()}");
                
                // 显示错误提示
                status = "聊天消息发送失败，请检查网络连接";
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 发送网络聊天消息时发生错误: {ex.Message}\n{ex.StackTrace}");
            status = $"聊天消息发送异常: {ex.Message}";
        }
    }
    
    /// <summary>
    /// 添加聊天消息到显示列表（供网络接收使用）
    /// </summary>
    /// <param name="displayMessage">格式化的显示消息</param>
    public void AddChatMessage(string displayMessage)
    {
        try
        {
            Debug.Log($"[ModUI] 添加网络聊天消息到显示: {displayMessage}");
            
            _chatMessages.Add(displayMessage);
            
            // 限制消息数量
            if (_chatMessages.Count > _maxChatMessages)
            {
                _chatMessages.RemoveAt(0);
            }
            
            Debug.Log($"[ModUI] 聊天消息已添加，当前消息数: {_chatMessages.Count}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 添加聊天消息时发生错误: {ex.Message}");
        }
    }
    
    // 初始化界面状态
    private void InitializeUIState()
    {
        // 检查当前连接状态，决定初始界面状态
        var manager = LobbyManager;
        var service = Service;
        
        if ((manager != null && manager.IsInLobby) || 
            (service != null && service.networkStarted && connectedPeer != null))
        {
            // 如果已经在房间中，设置为房间状态
            _currentUIState = UIState.InRoom;
            Debug.Log("[ModUI] 初始化时检测到已在房间中，设置为房间状态");
        }
        else
        {
            // 否则设置为主大厅状态
            _currentUIState = UIState.MainLobby;
            Debug.Log("[ModUI] 初始化时设置为主大厅状态");
        }
    }

    private void OnDestroy()
    {
        if (LobbyManager != null)
        {
            LobbyManager.LobbyListUpdated -= OnLobbyListUpdated;
        }
        
        // 清理聊天系统事件
        var chatUIManager = EscapeFromDuckovCoopMod.Chat.UI.ChatUIManager.Instance;
        if (chatUIManager != null)
        {
            chatUIManager.OnMessageSent -= HandleChatMessageSent;
        }
    }

    private void OnLobbyListUpdated(IReadOnlyList<SteamLobbyManager.LobbyInfo> lobbies)
    {
        _steamLobbyInfos.Clear();
        _steamLobbyInfos.AddRange(lobbies);
    }

    private void UpdateLobbyOptionsFromUI()
    {
        if (Service == null)
            return;

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

    private void DrawTransportModeSelector()
    {
        if (Service == null)
            return;

        GUILayout.Label(CoopLocalization.Get("ui.transport.label"));
        var modeLabels = new[]
        {
            CoopLocalization.Get("ui.transport.mode.direct"),
            CoopLocalization.Get("ui.transport.mode.steam")
        };

        var currentIndex = TransportMode == NetworkTransportMode.Direct ? 0 : 1;
        var selectedIndex = GUILayout.Toolbar(currentIndex, modeLabels);

        if (selectedIndex != currentIndex)
        {
            var newMode = selectedIndex == 0 ? NetworkTransportMode.Direct : NetworkTransportMode.SteamP2P;
            Service.SetTransportMode(newMode);

            if (newMode == NetworkTransportMode.SteamP2P && LobbyManager != null)
            {
                LobbyManager.RequestLobbyList();
            }
        }
    }

    private void DrawDirectClientSection()
    {
        GUILayout.Label(CoopLocalization.Get("ui.hostList.title"));

        if (hostList.Count == 0)
        {
            GUILayout.Label(CoopLocalization.Get("ui.hostList.empty"));
        }
        else
        {
            foreach (var host in hostList)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(CoopLocalization.Get("ui.hostList.connect"), GUILayout.Width(80)))
                {
                    var parts = host.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[1], out var parsedPort))
                    {
                        if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
                        {
                            NetService.Instance.StartNetwork(false);
                        }

                        NetService.Instance.ConnectToHost(parts[0], parsedPort);
                    }
                }

                GUILayout.Label(host);
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(20);
        GUILayout.Label(CoopLocalization.Get("ui.manualConnect.title"));

        GUILayout.BeginHorizontal();
        GUILayout.Label(CoopLocalization.Get("ui.manualConnect.ip"), GUILayout.Width(40));
        manualIP = GUILayout.TextField(manualIP, GUILayout.Width(150));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label(CoopLocalization.Get("ui.manualConnect.port"), GUILayout.Width(40));
        manualPort = GUILayout.TextField(manualPort, GUILayout.Width(150));
        GUILayout.EndHorizontal();

        if (GUILayout.Button(CoopLocalization.Get("ui.manualConnect.button")))
        {
            if (int.TryParse(manualPort, out var parsedPort))
            {
                if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
                {
                    NetService.Instance.StartNetwork(false);
                }

                NetService.Instance.ConnectToHost(manualIP, parsedPort);
            }
            else
            {
                status = CoopLocalization.Get("ui.manualConnect.portError");
            }
        }
    }

    private void DrawDirectServerSection()
    {
        GUILayout.Label($"{CoopLocalization.Get("ui.server.listenPort")} {port}");
        GUILayout.Label($"{CoopLocalization.Get("ui.server.connections")} {netManager?.ConnectedPeerList.Count ?? 0}");
    }

    private void DrawSteamClientSection()
    {
        var manager = LobbyManager;
        if (manager == null || !SteamManager.Initialized)
        {
            GUILayout.Label(CoopLocalization.Get("ui.steam.notInitialized"));
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(CoopLocalization.Get("ui.steam.refresh"), GUILayout.Width(120)))
        {
            manager.RequestLobbyList();
        }
        GUILayout.EndHorizontal();

        if (_steamLobbyInfos.Count == 0)
        {
            GUILayout.Label(CoopLocalization.Get("ui.steam.lobbiesEmpty"));
        }
        else
        {
            foreach (var lobby in _steamLobbyInfos)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(CoopLocalization.Get("ui.steam.joinButton"), GUILayout.Width(80)))
                {
                    AttemptSteamLobbyJoin(lobby);
                }

                var lobbyLabel = $"{lobby.LobbyName} ({lobby.MemberCount}/{lobby.MaxMembers})";
                if (lobby.RequiresPassword)
                {
                    lobbyLabel += " 🔒";
                }

                GUILayout.Label(lobbyLabel);
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(10);
        GUILayout.Label(CoopLocalization.Get("ui.steam.joinPassword"));
        var newJoinPassword = GUILayout.PasswordField(_steamJoinPassword, '*');
        if (newJoinPassword != _steamJoinPassword)
        {
            _steamJoinPassword = newJoinPassword;
        }

        if (manager.IsInLobby && !manager.IsHost)
        {
            GUILayout.Space(10);
            if (GUILayout.Button(CoopLocalization.Get("ui.steam.leaveLobby"), GUILayout.Height(32)))
            {
                NetService.Instance?.StopNetwork();
            }
        }
    }

    private void DrawSteamServerSection()
    {
        GUILayout.Label(CoopLocalization.Get("ui.steam.lobbySettings"));

        GUILayout.Label(CoopLocalization.Get("ui.steam.lobbyName"));
        var newLobbyName = GUILayout.TextField(_steamLobbyName);
        if (newLobbyName != _steamLobbyName)
        {
            _steamLobbyName = newLobbyName;
            UpdateLobbyOptionsFromUI();
        }

        GUILayout.Label(CoopLocalization.Get("ui.steam.lobbyPassword"));
        var newLobbyPassword = GUILayout.PasswordField(_steamLobbyPassword, '*');
        if (newLobbyPassword != _steamLobbyPassword)
        {
            _steamLobbyPassword = newLobbyPassword;
            UpdateLobbyOptionsFromUI();
        }

        var visibilityLabels = new[]
        {
            CoopLocalization.Get("ui.steam.visibility.public"),
            CoopLocalization.Get("ui.steam.visibility.friends")
        };

        var visibilityIndex = _steamLobbyFriendsOnly ? 1 : 0;
        var newVisibilityIndex = GUILayout.Toolbar(visibilityIndex, visibilityLabels);
        if (newVisibilityIndex != visibilityIndex)
        {
            _steamLobbyFriendsOnly = newVisibilityIndex == 1;
            UpdateLobbyOptionsFromUI();
        }

        GUILayout.Label(CoopLocalization.Get("ui.steam.maxPlayers", _steamLobbyMaxPlayers));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-", GUILayout.Width(30)))
        {
            var updated = Mathf.Max(2, _steamLobbyMaxPlayers - 1);
            if (updated != _steamLobbyMaxPlayers)
            {
                _steamLobbyMaxPlayers = updated;
                UpdateLobbyOptionsFromUI();
            }
        }

        GUILayout.Label(_steamLobbyMaxPlayers.ToString(), GUILayout.Width(40));

        if (GUILayout.Button("+", GUILayout.Width(30)))
        {
            var updated = Mathf.Min(16, _steamLobbyMaxPlayers + 1);
            if (updated != _steamLobbyMaxPlayers)
            {
                _steamLobbyMaxPlayers = updated;
                UpdateLobbyOptionsFromUI();
            }
        }
        GUILayout.EndHorizontal();

        var manager = LobbyManager;
        if (manager != null && manager.IsInLobby)
        {
            if (manager.TryGetLobbyInfo(manager.CurrentLobbyId, out var lobbyInfo))
            {
                GUILayout.Label(CoopLocalization.Get("ui.steam.currentLobby", lobbyInfo.LobbyName, lobbyInfo.MemberCount, lobbyInfo.MaxMembers));
            }
            else
            {
                GUILayout.Label(CoopLocalization.Get("ui.steam.currentLobby", _steamLobbyName, netManager?.ConnectedPeerList.Count ?? 1, Service?.LobbyOptions.MaxPlayers ?? 2));
            }
            GUILayout.Space(10);
            if (manager.IsHost)
            {
                if (GUILayout.Button(CoopLocalization.Get("ui.steam.leaveLobby"), GUILayout.Height(32)))
                {
                    NetService.Instance?.StopNetwork();
                }
            }
        }
        else
        {
            GUILayout.Label(CoopLocalization.Get("ui.steam.server.waiting"));
            if (!IsServer)
            {
                if (GUILayout.Button(CoopLocalization.Get("ui.steam.createHost"), GUILayout.Height(40)))
                {
                    UpdateLobbyOptionsFromUI();
                    NetService.Instance?.StartNetwork(true);
                }
            }
            else if (GUILayout.Button(CoopLocalization.Get("ui.steam.leaveLobby"), GUILayout.Height(32)))
            {
                NetService.Instance?.StopNetwork();
            }
        }
    }

    private void DrawSteamMode()
    {
        var manager = LobbyManager;
        var roleLabel = CoopLocalization.Get("ui.mode.client");

        if (manager != null && manager.IsInLobby)
        {
            roleLabel = manager.IsHost ? CoopLocalization.Get("ui.mode.server") : CoopLocalization.Get("ui.mode.client");
        }
        else if (IsServer)
        {
            roleLabel = CoopLocalization.Get("ui.mode.server");
        }

        GUILayout.Label($"{CoopLocalization.Get("ui.mode.current")}: {roleLabel}");

        GUILayout.BeginHorizontal();
        var createSelected = GUILayout.Toggle(!_steamShowLobbyBrowser, CoopLocalization.Get("ui.steam.tab.create"), GUI.skin.button);
        if (createSelected && _steamShowLobbyBrowser)
        {
            _steamShowLobbyBrowser = false;
        }

        var browseSelected = GUILayout.Toggle(_steamShowLobbyBrowser, CoopLocalization.Get("ui.steam.tab.browse"), GUI.skin.button);
        if (browseSelected && !_steamShowLobbyBrowser)
        {
            _steamShowLobbyBrowser = true;
            manager?.RequestLobbyList();
        }
        else if (!createSelected && !browseSelected)
        {
            _steamShowLobbyBrowser = false;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        if (_steamShowLobbyBrowser)
        {
            DrawSteamClientSection();
        }
        else
        {
            DrawSteamServerSection();
        }
    }

    private void AttemptSteamLobbyJoin(SteamLobbyManager.LobbyInfo lobby)
    {
        var manager = LobbyManager;
        if (manager == null)
        {
            ShowConnectionError("Steam未初始化，无法加入房间");
            return;
        }

        var password = lobby.RequiresPassword ? _steamJoinPassword : string.Empty;
        if (manager.TryJoinLobbyWithPassword(lobby.LobbyId, password, out var error))
        {
            status = "正在连接到房间...";
            // 连接成功后会通过MonitorConnectionStatus自动切换界面
            return;
        }

        // 处理连接失败
        switch (error)
        {
            case SteamLobbyManager.LobbyJoinError.SteamNotInitialized:
                ShowConnectionError("Steam未初始化");
                break;
            case SteamLobbyManager.LobbyJoinError.LobbyMetadataUnavailable:
                ShowConnectionError("房间信息不可用");
                break;
            case SteamLobbyManager.LobbyJoinError.IncorrectPassword:
                ShowConnectionError("房间密码错误");
                break;
            default:
                ShowConnectionError("加入房间失败");
                break;
        }
    }

    private void DrawMainWindow(int windowID)
    {
        GUILayout.BeginVertical();
        
        // 标题栏区域 - 右上角关闭按钮
        if (GUI.Button(new Rect(mainWindowRect.width - 25, 5, 20, 20), "✕"))
        {
            showUI = false;
        }
        
        // 根据当前状态显示不同界面
        switch (_currentUIState)
        {
            case UIState.MainLobby:
                DrawMainLobbyInterface();
                break;
            case UIState.InRoom:
                DrawRoomInterface();
                break;
        }

        GUILayout.EndVertical();
        GUI.DragWindow();
    }
    
    private void DrawMainLobbyInterface()
    {
        // 增加上边距，避免与关闭按钮太近
        GUILayout.Space(25);
        
        // 创建房间区域
        DrawRoomCreationSection();
        
        GUILayout.Space(15);
        
        // 房间列表区域
        DrawRoomListSection();
        
        GUILayout.Space(15);
        
        // 直连区域
        DrawDirectConnectSection();
    }
    
    private void DrawRoomInterface()
    {
        // 房间标题栏
        DrawRoomHeaderSection();
        
        GUILayout.Space(10);
        
        // 房间设置区域
        DrawRoomSettingsSection();
        
        GUILayout.Space(15);
        
        // 玩家列表区域
        DrawPlayerListSection();
        
        GUILayout.Space(15);
        
        // 邀请好友按钮
        DrawInviteFriendsSection();
    }


    private void DrawRoomCreationSection()
    {
        // 创建房间区域
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("创建房间", GUI.skin.label);
        GUILayout.Space(5);
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("房间名称:", GUILayout.Width(80));
        _steamLobbyName = GUILayout.TextField(_steamLobbyName, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("创建房间", GUILayout.Height(30)))
        {
            CreateRoom();
        }
        
        GUILayout.Space(5);
        GUILayout.Label("注: Steam房间创建需要时间，请耐心等待...", GUI.skin.label);
        
        GUILayout.EndVertical();
    }
    
    private void DrawRoomListSection()
    {
        // 房间列表区域
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("房间列表", GUI.skin.label);
        GUILayout.Space(5);
        
        // 搜索房间
        GUILayout.BeginHorizontal();
        GUILayout.Label("搜索房间:", GUILayout.Width(80));
        _roomSearchText = GUILayout.TextField(_roomSearchText, GUILayout.ExpandWidth(true));
        var buttonText = string.IsNullOrWhiteSpace(_roomSearchText) ? "刷新" : "搜索";
        if (GUILayout.Button(buttonText, GUILayout.Width(60)))
        {
            if (string.IsNullOrWhiteSpace(_roomSearchText))
            {
                RefreshRoomList();
            }
            // 搜索功能是实时的，不需要额外操作
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        
        // 房间列表显示
        var filteredLobbies = GetFilteredLobbies();
        if (filteredLobbies.Count == 0)
        {
            if (_steamLobbyInfos.Count == 0)
            {
                GUILayout.Label("暂无可用房间");
            }
            else
            {
                GUILayout.Label("没有匹配的房间");
            }
        }
        else
        {
            foreach (var lobby in filteredLobbies)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                var lobbyDisplayName = lobby.LobbyName;
                if (lobby.RequiresPassword)
                {
                    lobbyDisplayName += " 🔒";
                }
                GUILayout.Label(lobbyDisplayName, GUILayout.ExpandWidth(true));
                GUILayout.Label($"[{lobby.MemberCount}/{lobby.MaxMembers}人]", GUILayout.Width(80));
                if (GUILayout.Button("加入房间", GUILayout.Width(80)))
                {
                    AttemptSteamLobbyJoin(lobby);
                }
                GUILayout.EndHorizontal();
            }
        }
        
        GUILayout.EndVertical();
    }
    
    private void DrawDirectConnectSection()
    {
        // 直连区域
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("直连", GUI.skin.label);
        GUILayout.Space(5);
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("IP地址:", GUILayout.Width(60));
        manualIP = GUILayout.TextField(manualIP, GUILayout.Width(120));
        GUILayout.Label("端口:", GUILayout.Width(40));
        manualPort = GUILayout.TextField(manualPort, GUILayout.Width(60));
        GUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("直接连接", GUILayout.Height(30)))
        {
            ConnectDirect();
        }
        
        GUILayout.EndVertical();
    }
    
    private void CreateRoom()
    {
        if (string.IsNullOrWhiteSpace(_steamLobbyName))
        {
            _steamLobbyName = "我的游戏房间";
        }
        
        // 检查Steam是否可用
        if (!SteamManager.Initialized)
        {
            status = "Steam未初始化，无法创建房间";
            return;
        }
        
        // 更新大厅选项
        UpdateLobbyOptionsFromUI();
        
        // 设置传输模式为Steam P2P
        if (Service != null)
        {
            Service.SetTransportMode(NetworkTransportMode.SteamP2P);
            
            // 启动服务器模式，这会自动创建Steam房间
            Service.StartNetwork(true);
            
            status = "正在创建Steam房间，请耐心等待...";
            
            // 房间创建成功后会通过连接状态监听自动切换到房间界面
        }
        else
        {
            status = "网络服务未初始化";
        }
    }
    
    private void RefreshRoomList()
    {
        var manager = LobbyManager;
        if (manager != null && SteamManager.Initialized)
        {
            manager.RequestLobbyList();
            status = "正在刷新房间列表...";
        }
        else
        {
            status = "Steam未初始化，无法获取房间列表";
        }
    }
    
    private void ConnectDirect()
    {
        if (string.IsNullOrWhiteSpace(manualIP))
        {
            ShowConnectionError("IP地址不能为空");
            return;
        }
        
        if (!int.TryParse(manualPort, out var parsedPort) || parsedPort <= 0 || parsedPort > 65535)
        {
            ShowConnectionError("端口格式无效，请输入1-65535之间的数字");
            return;
        }
        
        // 设置传输模式为直连
        if (Service != null)
        {
            Service.SetTransportMode(NetworkTransportMode.Direct);
            
            // 启动客户端网络
            if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
            {
                Service.StartNetwork(false);
            }

            Service.ConnectToHost(manualIP, parsedPort);
            status = $"正在直连到 {manualIP}:{parsedPort}...";
            // 连接成功后会通过MonitorConnectionStatus自动切换界面
        }
        else
        {
            ShowConnectionError("网络服务未初始化");
        }
    }
    
    private List<SteamLobbyManager.LobbyInfo> GetFilteredLobbies()
    {
        if (string.IsNullOrWhiteSpace(_roomSearchText))
        {
            return _steamLobbyInfos;
        }
        
        var searchTerm = _roomSearchText.ToLower();
        return _steamLobbyInfos.Where(lobby => 
            lobby.LobbyName.ToLower().Contains(searchTerm) ||
            lobby.HostName.ToLower().Contains(searchTerm)
        ).ToList();
    }
    
    private void DrawRoomHeaderSection()
    {
        GUILayout.BeginHorizontal();
        
        // 返回箭头按钮
        if (GUILayout.Button("← 返回大厅", GUILayout.Width(100)))
        {
            LeaveRoom();
        }
        
        GUILayout.FlexibleSpace();
        
        // 房间名称显示
        var roomName = GetCurrentRoomName();
        GUILayout.Label($"房间: {roomName}", GUI.skin.label);
        
        GUILayout.EndHorizontal();
    }
    
    private string GetCurrentRoomName()
    {
        var manager = LobbyManager;
        if (manager != null && manager.IsInLobby)
        {
            if (manager.TryGetLobbyInfo(manager.CurrentLobbyId, out var lobbyInfo))
            {
                return lobbyInfo.LobbyName;
            }
        }
        
        return string.IsNullOrEmpty(_steamLobbyName) ? "未知房间" : _steamLobbyName;
    }
    
    private void LeaveRoom()
    {
        // 执行离开房间的清理逻辑
        PerformRoomCleanup();
        
        // 使用状态管理系统切换回主大厅界面
        ChangeUIState(UIState.MainLobby);
    }
    
    // 执行房间清理逻辑
    private void PerformRoomCleanup()
    {
        try
        {
            // 离开Steam房间
            var manager = LobbyManager;
            if (manager != null && manager.IsInLobby)
            {
                Debug.Log("[ModUI] 正在离开Steam房间...");
                manager.LeaveLobby();
            }
            
            // 停止网络服务
            if (Service != null)
            {
                Debug.Log("[ModUI] 正在停止网络服务...");
                Service.StopNetwork();
            }
            
            // 清理房间相关数据
            ClearRoomData();
            
            Debug.Log("[ModUI] 房间清理完成");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 房间清理时发生错误: {ex.Message}");
            // 即使清理失败，也要确保状态正确重置
            ClearRoomData();
        }
    }
    
    // 清理房间数据
    private void ClearRoomData()
    {
        // 重置房间相关的UI状态
        _steamJoinPassword = string.Empty;
        
        // 清理连接状态
        if (Service != null)
        {
            Service.isConnecting = false;
        }
        
        // 重置Steam大厅成员跟踪
        _lastSteamLobbyMemberCount = 0;
        
        // 重置状态显示
        status = "已离开房间";
    }
    
    // 确保界面状态正确重置的方法
    private void EnsureStateReset()
    {
        // 如果由于某种原因状态没有正确切换，强制重置
        if (_currentUIState != UIState.MainLobby)
        {
            Debug.LogWarning("[ModUI] 强制重置界面状态到主大厅");
            _currentUIState = UIState.MainLobby;
            OnEnterMainLobby(UIState.InRoom);
        }
    }
    
    private void DrawRoomSettingsSection()
    {
        var manager = LobbyManager;
        var isHost = manager != null && manager.IsHost;
        
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("房间设置", GUI.skin.label);
        
        // 可见性设置
        GUILayout.BeginHorizontal();
        GUILayout.Label("房间可见性:", GUILayout.Width(80));
        
        GUI.enabled = isHost; // 只有房主可以修改
        
        var visibilityLabels = new[] { "公开", "好友", "邀请" };
        var currentVisibility = _steamLobbyFriendsOnly ? 1 : 0; // 简化处理，只区分公开和好友
        var newVisibility = GUILayout.Toolbar(currentVisibility, visibilityLabels);
        
        if (newVisibility != currentVisibility && isHost)
        {
            _steamLobbyFriendsOnly = newVisibility == 1;
            UpdateLobbyOptionsFromUI();
        }
        
        GUILayout.EndHorizontal();
        
        GUILayout.Space(5);
        
        // 密码设置
        GUILayout.BeginHorizontal();
        GUILayout.Label("房间密码:", GUILayout.Width(80));
        
        var newPassword = GUILayout.PasswordField(_steamLobbyPassword, '*', GUILayout.ExpandWidth(true));
        if (newPassword != _steamLobbyPassword && isHost)
        {
            _steamLobbyPassword = newPassword;
            UpdateLobbyOptionsFromUI();
        }
        
        GUILayout.EndHorizontal();
        
        if (!isHost)
        {
            GUILayout.Space(5);
            GUILayout.Label("(只有房主可以修改设置)", GUI.skin.label);
        }
        
        GUI.enabled = true; // 恢复GUI状态
        
        GUILayout.EndVertical();
    }
    
    private void DrawPlayerListSection()
    {
        var manager = LobbyManager;
        var isHost = manager != null && manager.IsHost;
        
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("玩家列表", GUI.skin.label);
        GUILayout.Space(5);
        
        // 获取Steam大厅成员列表
        var steamLobbyMembers = GetSteamLobbyMembers();
        
        if (steamLobbyMembers.Count > 0)
        {
            // 显示Steam大厅成员
            foreach (var member in steamLobbyMembers)
            {
                var isCurrentPlayer = member.SteamId == Steamworks.SteamUser.GetSteamID();
                var isMemberHost = manager != null && manager.IsInLobby && 
                                 Steamworks.SteamMatchmaking.GetLobbyOwner(manager.CurrentLobbyId) == member.SteamId;
                
                // 尝试从网络状态获取延迟信息
                var latency = GetPlayerLatency(member.PlayerName, isCurrentPlayer);
                
                DrawPlayerEntry(member.PlayerName, latency, isMemberHost, !isCurrentPlayer, isHost);
            }
        }
        else
        {
            // 如果没有Steam大厅信息，使用原有的网络连接信息
            // 显示当前玩家
            if (localPlayerStatus != null)
            {
                DrawPlayerEntry(localPlayerStatus.PlayerName, localPlayerStatus.Latency, true, false, isHost);
            }
            else
            {
                // 如果没有本地玩家状态，显示Steam用户名
                var playerName = SteamManager.Initialized ? Steamworks.SteamFriends.GetPersonaName() : "本地玩家";
                DrawPlayerEntry(playerName, 0, true, false, isHost);
            }
            
            // 显示其他玩家
            if (IsServer && playerStatuses != null)
            {
                foreach (var kvp in playerStatuses)
                {
                    var playerStatus = kvp.Value;
                    DrawPlayerEntry(playerStatus.PlayerName, playerStatus.Latency, false, true, isHost);
                }
            }
            else if (!IsServer && clientPlayerStatuses != null)
            {
                foreach (var kvp in clientPlayerStatuses)
                {
                    var playerStatus = kvp.Value;
                    var isThisHost = kvp.Key == "host" || kvp.Key.Contains("host");
                    DrawPlayerEntry(playerStatus.PlayerName, playerStatus.Latency, isThisHost, true, false);
                }
            }
        }
        
        // 显示空位
        var currentPlayerCount = steamLobbyMembers.Count > 0 ? steamLobbyMembers.Count : GetCurrentPlayerCount();
        var maxPlayers = _steamLobbyMaxPlayers;
        
        for (int i = currentPlayerCount; i < maxPlayers; i++)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("等待玩家加入...", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }
        
        GUILayout.EndVertical();
    }
    
    private void DrawPlayerEntry(string playerName, int latency, bool isHost, bool canKick, bool showKickButton)
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        
        // 玩家名称，房主显示皇冠
        var displayName = isHost ? $"👑 {playerName} (房主)" : $"   {playerName}";
        GUILayout.Label(displayName, GUILayout.ExpandWidth(true));
        
        // 延迟显示
        string latencyText = GetLatencyDisplayText(latency);
        GUILayout.Label(latencyText, GUILayout.Width(80));
        
        // 踢出按钮（只有房主可以踢出其他玩家）
        if (canKick && showKickButton && !isHost)
        {
            if (GUILayout.Button("踢出", GUILayout.Width(50)))
            {
                // TODO: 实现踢出功能
                Debug.Log($"踢出玩家: {playerName}");
            }
        }
        else
        {
            GUILayout.Space(50); // 占位空间
        }
        
        GUILayout.EndHorizontal();
    }
    
    // 获取延迟显示文本
    private string GetLatencyDisplayText(int latency)
    {
        switch (latency)
        {
            case -1:
                return "未连接";
            case -2:
                return "已连接";
            case -3:
                return "连接中...";
            case 0:
                return "延迟: 0ms";
            default:
                return $"延迟: {latency}ms";
        }
    }
    
    private int GetCurrentPlayerCount()
    {
        int count = 1; // 本地玩家
        
        if (IsServer && playerStatuses != null)
        {
            count += playerStatuses.Count;
        }
        else if (!IsServer && clientPlayerStatuses != null)
        {
            count += clientPlayerStatuses.Count;
        }
        
        return count;
    }
    
    // Steam大厅成员信息结构
    private struct SteamLobbyMember
    {
        public Steamworks.CSteamID SteamId;
        public string PlayerName;
        
        public SteamLobbyMember(Steamworks.CSteamID steamId, string playerName)
        {
            SteamId = steamId;
            PlayerName = playerName;
        }
    }
    
    // 获取Steam大厅成员列表
    private List<SteamLobbyMember> GetSteamLobbyMembers()
    {
        var members = new List<SteamLobbyMember>();
        
        var manager = LobbyManager;
        if (manager == null || !manager.IsInLobby || !SteamManager.Initialized)
        {
            return members;
        }
        
        try
        {
            var lobbyId = manager.CurrentLobbyId;
            var memberCount = Steamworks.SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            
            for (int i = 0; i < memberCount; i++)
            {
                var memberId = Steamworks.SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                var memberName = Steamworks.SteamFriends.GetFriendPersonaName(memberId);
                
                if (string.IsNullOrEmpty(memberName))
                {
                    memberName = $"玩家_{memberId}";
                }
                
                members.Add(new SteamLobbyMember(memberId, memberName));
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 获取Steam大厅成员时发生错误: {ex.Message}");
        }
        
        return members;
    }
    
    // 获取玩家延迟信息
    private int GetPlayerLatency(string playerName, bool isCurrentPlayer)
    {
        if (isCurrentPlayer)
        {
            return localPlayerStatus?.Latency ?? 0;
        }
        
        // 首先尝试从网络状态中查找匹配的玩家（已建立连接的玩家）
        if (IsServer && playerStatuses != null)
        {
            foreach (var kvp in playerStatuses)
            {
                if (kvp.Value.PlayerName == playerName)
                {
                    return kvp.Value.Latency;
                }
            }
        }
        else if (!IsServer && clientPlayerStatuses != null)
        {
            foreach (var kvp in clientPlayerStatuses)
            {
                if (kvp.Value.PlayerName == playerName)
                {
                    return kvp.Value.Latency;
                }
            }
        }
        
        // 如果网络状态中没有找到，尝试从Steam P2P会话获取延迟
        var steamLatency = GetSteamP2PLatency(playerName);
        if (steamLatency > 0)
        {
            return steamLatency;
        }
        
        // 如果都没有找到，返回默认值
        return 0;
    }
    
    // 从Steam P2P会话获取连接状态信息
    private int GetSteamP2PLatency(string playerName)
    {
        if (!SteamManager.Initialized || LobbyManager == null || !LobbyManager.IsInLobby)
        {
            return -1; // 返回-1表示无法获取信息
        }
        
        try
        {
            var lobbyId = LobbyManager.CurrentLobbyId;
            var memberCount = Steamworks.SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            
            // 查找匹配的Steam用户
            for (int i = 0; i < memberCount; i++)
            {
                var memberId = Steamworks.SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                var memberName = Steamworks.SteamFriends.GetFriendPersonaName(memberId);
                
                if (string.IsNullOrEmpty(memberName))
                {
                    memberName = $"玩家_{memberId}";
                }
                
                if (memberName == playerName)
                {
                    // 获取P2P会话状态
                    if (Steamworks.SteamNetworking.GetP2PSessionState(memberId, out Steamworks.P2PSessionState_t sessionState))
                    {
                        // 检查连接是否活跃
                        if (sessionState.m_bConnectionActive == 1)
                        {
                            // Steam P2P没有直接的ping值，返回特殊值表示已连接
                            return -2; // 返回-2表示Steam P2P已连接但无具体延迟
                        }
                        else if (sessionState.m_bConnecting == 1)
                        {
                            return -3; // 返回-3表示正在连接中
                        }
                    }
                    break;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ModUI] 获取Steam P2P状态时发生错误: {ex.Message}");
        }
        
        return -1; // 无法获取状态
    }
    
    private void DrawInviteFriendsSection()
    {
        if (GUILayout.Button("邀请好友", GUILayout.Height(35)))
        {
            InviteFriends();
        }
        
        GUILayout.Space(15);
        
        // 聊天记录区域
        DrawChatSection();
    }
    
    private void DrawChatSection()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("聊天记录", GUI.skin.label);
        GUILayout.Space(5);
        
        // 聊天消息显示区域
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(120));
        
        // 显示真实的聊天消息
        if (_chatMessages.Count > 0)
        {
            foreach (var message in _chatMessages)
            {
                GUILayout.Label(message);
            }
        }
        else
        {
            GUILayout.Label("暂无聊天消息");
        }
        
        GUILayout.EndVertical();
        
        GUILayout.Space(5);
        
        // 聊天输入提示
        GUILayout.Label("按 Enter 键开始聊天...", GUI.skin.label);
        
        GUILayout.EndVertical();
    }
    
    private void InviteFriends()
    {
        var manager = LobbyManager;
        if (manager != null && manager.IsInLobby)
        {
            manager.InviteFriend();
            status = "已打开Steam邀请界面";
        }
        else
        {
            status = "当前不在房间中，无法邀请好友";
        }
    }

    private void DrawPlayerStatusWindow(int windowID)
    {
        if (GUI.Button(new Rect(playerStatusWindowRect.width - 25, 5, 20, 20), "×")) showPlayerStatusWindow = false;

        playerStatusScrollPos = GUILayout.BeginScrollView(playerStatusScrollPos, GUILayout.ExpandWidth(true));

        if (localPlayerStatus != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.id")} {localPlayerStatus.EndPoint}", GUILayout.Width(180));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.name")} {localPlayerStatus.PlayerName}", GUILayout.Width(180));
            GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.latency")} {localPlayerStatus.Latency}ms", GUILayout.Width(100));
            GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.inGame")} {(localPlayerStatus.IsInGame ? CoopLocalization.Get("ui.playerStatus.yes") : CoopLocalization.Get("ui.playerStatus.no"))}");
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        if (IsServer)
            foreach (var kvp in playerStatuses)
            {
                var st = kvp.Value;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.id")} {st.EndPoint}", GUILayout.Width(180));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.name")} {st.PlayerName}", GUILayout.Width(180));
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.latency")} {st.Latency}ms", GUILayout.Width(100));
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.inGame")} {(st.IsInGame ? CoopLocalization.Get("ui.playerStatus.yes") : CoopLocalization.Get("ui.playerStatus.no"))}");
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
        else
            foreach (var kvp in clientPlayerStatuses)
            {
                var st = kvp.Value;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.id")} {st.EndPoint}", GUILayout.Width(180));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.name")} {st.PlayerName}", GUILayout.Width(180));
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.latency")} {st.Latency}ms", GUILayout.Width(100));
                GUILayout.Label($"{CoopLocalization.Get("ui.playerStatus.inGame")} {(st.IsInGame ? CoopLocalization.Get("ui.playerStatus.yes") : CoopLocalization.Get("ui.playerStatus.no"))}");
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }

        GUILayout.EndScrollView();
        GUI.DragWindow();
    }

}