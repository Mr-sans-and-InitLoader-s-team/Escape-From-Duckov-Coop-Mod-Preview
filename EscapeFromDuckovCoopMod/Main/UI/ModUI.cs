﻿﻿using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using Steamworks;
using static Animancer.Easing;

namespace EscapeFromDuckovCoopMod
{
    /// <summary>
    /// 联机模组主UI类
    /// 支持三种网络模式：LAN局域网、Steam P2P混合模式、Steam P2P虚拟网络模式
    /// </summary>
    public partial class ModUI:MonoBehaviour
    {
        public static ModUI Instance;

        // 核心服务引用
        private NetService Service => NetService.Instance;
        private EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService HybridService => EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
        
        /// <summary>
        /// 判断当前是否为服务器/主机
        /// 根据不同网络模式返回相应的判断结果
        /// </summary>
        private bool IsServer
        {
            get
            {
                if (currentNetworkMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    return HybridService != null && HybridService.IsServer;
                }
                return Service != null && Service.IsServer;
            }
        }
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        
        // 网络模式配置
        private EscapeFromDuckovCoopMod.Net.Steam.NetworkMode currentNetworkMode = EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.LAN;
        private bool useVirtualNetworkP2P = false; // 是否使用虚拟网络P2P（Socket劫持模式）
        
        // Steam大厅相关
        private string steamLobbyId = "";
        private string lobbyPassword = "";
        private string lobbyName = "";
        private Steamworks.ELobbyType lobbyType = Steamworks.ELobbyType.k_ELobbyTypeFriendsOnly;
        
        // UI滚动位置
        private Vector2 mainWindowScrollPos = Vector2.zero;
        private Vector2 lobbyListScrollPos = Vector2.zero;
        private Vector2 friendListScrollPos = Vector2.zero;
        private Vector2 playerListScrollPos = Vector2.zero;
        
        // 大厅浏览
        private List<EscapeFromDuckovCoopMod.Net.Steam.LobbyInfo> availableLobbies = new List<EscapeFromDuckovCoopMod.Net.Steam.LobbyInfo>();
        private bool showLobbyBrowser = false; // 是否显示大厅浏览器（false=创建模式，true=浏览模式）
        
        // UI显示开关
        private bool showFriendList = false;
        private bool showSteamPlayerList = true;
        
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

        private readonly List<string> _hostList = new List<string>();
        private readonly HashSet<string> _hostSet = new HashSet<string>();
        private string _manualIP = "127.0.0.1";
        private string _manualPort = "9050";
        private string _status = "未连接";
        private int _port = 9050;

        private Dictionary<string, GameObject> remoteCharacters => Service?.remoteCharacters;
        private Dictionary<string, PlayerStatus> playerStatuses => Service?.playerStatuses;
        private Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;
        private Dictionary<string, PlayerStatus> clientPlayerStatuses => Service?.clientPlayerStatuses;

        public bool showUI = true;
        private Rect mainWindowRect = new Rect(10, 10, 600, 700);
        private Rect playerStatusWindowRect = new Rect(420, 10, 300, 400);
        public bool showPlayerStatusWindow = false;
        private Vector2 playerStatusScrollPos = Vector2.zero;
        public KeyCode toggleWindowKey = KeyCode.P;
        public readonly KeyCode readyKey = KeyCode.J;


        public void Init()
        {
            Instance = this;
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
            }
            
            if (EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance != null)
            {
                var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                steamNet.OnLobbyListReceived += HandleLobbyListReceived;
                steamNet.OnLobbyCreated += OnLobbyCreatedSuccess;
                steamNet.OnLobbyJoined += OnLobbyJoinedSuccess;
                steamNet.OnLobbyLeft += OnLobbyLeftHandler;
            }
            
            if (EscapeFromDuckovCoopMod.Net.Steam.SteamLobbyManager.Instance != null)
            {
                var lobbyMgr = EscapeFromDuckovCoopMod.Net.Steam.SteamLobbyManager.Instance;
                lobbyMgr.OnLobbyCreatedEvent += OnVirtualNetworkLobbyCreated;
                lobbyMgr.OnLobbyJoinedEvent += OnVirtualNetworkLobbyJoined;
            }
        }
        
        public bool IsUsingVirtualNetworkP2P()
        {
            return useVirtualNetworkP2P;
        }
        
        private void OnVirtualNetworkLobbyCreated(Steamworks.CSteamID lobbyId)
        {
            steamLobbyId = lobbyId.m_SteamID.ToString();
            GUIUtility.systemCopyBuffer = steamLobbyId;
            status = "大厅创建成功（虚拟网络）！ID: " + steamLobbyId + " (已复制)";
            Debug.Log("[UI] 虚拟网络大厅创建成功，已跳转到房间页面");
            showLobbyBrowser = false;
        }
        
        private void OnVirtualNetworkLobbyJoined(Steamworks.CSteamID lobbyId)
        {
            steamLobbyId = lobbyId.m_SteamID.ToString();
            showLobbyBrowser = false;
            lobbyPassword = "";
            
            bool amIHost = EscapeFromDuckovCoopMod.Net.Steam.SteamLobbyManager.Instance.GetLobbyOwner() == Steamworks.SteamUser.GetSteamID();
            
            if (!amIHost)
            {
                Debug.Log("[UI] 成功加入虚拟网络大厅（客户端）");
                status = "已连接到大厅，等待同步...";
            }
            else
            {
                Debug.Log("[UI] 虚拟网络大厅加入事件触发（主机）");
                status = "大厅创建成功，等待玩家加入...";
            }
        }
        
        private void HandleLobbyListReceived(List<EscapeFromDuckovCoopMod.Net.Steam.LobbyInfo> lobbies)
        {
            availableLobbies = lobbies;
            Debug.Log("收到 " + lobbies.Count + " 个大厅");
        }
        
        private void HandleLobbyInviteReceived(Steamworks.CSteamID lobbyId)
        {
            steamLobbyId = lobbyId.m_SteamID.ToString();
            status = "收到大厅邀请: " + lobbyId;
        }
        
        private void OnLobbyCreatedSuccess(Steamworks.CSteamID lobbyId)
        {
            steamLobbyId = lobbyId.m_SteamID.ToString();
            GUIUtility.systemCopyBuffer = steamLobbyId;
            status = "大厅创建成功！ID: " + steamLobbyId + " (已复制)";
            Debug.Log("大厅创建成功（主机），已跳转到房间页面");
            showLobbyBrowser = false;
        }
        
        private void OnLobbyJoinedSuccess(Steamworks.CSteamID lobbyId)
        {
            var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
            bool amIHost = steamNet != null && steamNet.IsServer;
            
            steamLobbyId = lobbyId.m_SteamID.ToString();
            showLobbyBrowser = false;
            lobbyPassword = "";
            
            if (!amIHost)
            {
                Debug.Log("成功加入大厅（客户端），正在启动客户端网络...");
                if (HybridService != null)
                {
                    Debug.Log("[UI] 初始化HybridService为Steam P2P模式");
                    HybridService.Initialize(EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P);
                    
                    Debug.Log("[UI] 启动Steam P2P客户端");
                    HybridService.StartClient();
                    status = "已连接到大厅，等待同步...";
                }
            }
            else
            {
                Debug.Log("大厅加入事件触发（主机）");
                status = "大厅创建成功，等待玩家加入...";
            }
        }
        
        private void OnLobbyLeftHandler()
        {
            Debug.Log("收到离开大厅事件，重置UI状态");
            steamLobbyId = "";
            lobbyPassword = "";
            lobbyName = "";
            showFriendList = false;
            showLobbyBrowser = false;
            status = "已离开大厅";
        }

        void OnGUI()
        {
            if (showUI)
            {
                mainWindowRect = GUI.Window(94120, mainWindowRect, DrawMainWindow, "联机Mod控制面板");

                if (showPlayerStatusWindow)
                {
                    playerStatusWindowRect = GUI.Window(94121, playerStatusWindowRect, DrawPlayerStatusWindow, "玩家状态");
                }
            }

            if (SceneNet.Instance.sceneVoteActive)
            {
                float h = 220f;
                var area = new Rect(10, Screen.height * 0.5f - h * 0.5f, 320, h);
                GUILayout.BeginArea(area, GUI.skin.box);
                GUILayout.Label("地图投票 / 准备  [" + SceneInfoCollection.GetSceneInfo(SceneNet.Instance.sceneTargetId).DisplayName + "]");
                GUILayout.Label("按 " + readyKey + " 切换准备（当前：" + (SceneNet.Instance.localReady ? "已准备" : "未准备") + "）");

                GUILayout.Space(8);
                GUILayout.Label("玩家准备状态：");
                foreach (var pid in SceneNet.Instance.sceneParticipantIds)
                {
                    bool r = false; SceneNet.Instance.sceneReady.TryGetValue(pid, out r);
                    string displayName = GetPlayerDisplayName(pid);
                    GUILayout.Label("• " + displayName + "  —— " + (r ? "✅ 就绪" : "⌛ 未就绪"));
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

                try
                {
                }
                catch { }

                GUI.Label(new Rect(0, Screen.height - 40, Screen.width, 30),
                    "观战模式：左键 ▶ 下一个 | 右键 ◀ 上一个  | 正在观战", style);
            }
        }

        private void DrawMainWindow(int windowID)
        {
            mainWindowScrollPos = GUILayout.BeginScrollView(mainWindowScrollPos);
            GUILayout.BeginVertical();
            
            if (!networkStarted)
            {
                GUILayout.Label("=== 网络模式选择 ===", GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(currentNetworkMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.LAN, "LAN局域网"))
                {
                    currentNetworkMode = EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.LAN;
                    useVirtualNetworkP2P = false;
                }
                if (GUILayout.Toggle(currentNetworkMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P && !useVirtualNetworkP2P, "Steam P2P(混合)"))
                {
                    currentNetworkMode = EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P;
                    useVirtualNetworkP2P = false;
                }
                if (GUILayout.Toggle(currentNetworkMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P && useVirtualNetworkP2P, "Steam P2P(虚拟网络)"))
                {
                    currentNetworkMode = EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P;
                    useVirtualNetworkP2P = true;
                    if (EscapeFromDuckovCoopMod.Net.Steam.SteamP2PLoader.Instance != null)
                    {
                        EscapeFromDuckovCoopMod.Net.Steam.SteamP2PLoader.Instance.UseSteamP2P = true;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            else
            {
                GUILayout.Label("=== 当前状态 ===", GUI.skin.box);
                string modeStr = "LAN";
                if (currentNetworkMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
                {
                    modeStr = useVirtualNetworkP2P ? "Steam P2P (虚拟网络)" : "Steam P2P (混合)";
                }
                GUILayout.Label("模式: " + modeStr);
                GUILayout.Label("角色: " + (IsServer ? "服务器(主机)" : "客户端"));
                GUILayout.Space(10);
            }

            if (currentNetworkMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.SteamP2P)
            {
                DrawSteamP2PUI();
            }
            else
            {
                DrawLANUI();
            }

            GUILayout.Space(10);
            
            if (currentNetworkMode == EscapeFromDuckovCoopMod.Net.Steam.NetworkMode.LAN)
            {
                showPlayerStatusWindow = GUILayout.Toggle(showPlayerStatusWindow, "显示玩家状态窗口 (切换键: " + toggleWindowKey + ")");
            }

            if (GUILayout.Button("[Debug] 打印出该地图的所有lootbox"))
            {
                foreach (var i in LevelManager.LootBoxInventories)
                {
                    try
                    {
                        Debug.Log("Name " + i.Value.name + " DisplayNameKey " + i.Value.DisplayNameKey + " Key " + i.Key);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }
        
        private void DrawSteamP2PUI()
        {
            var steamNetSockets = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
            var steamLobbyMgr = EscapeFromDuckovCoopMod.Net.Steam.SteamLobbyManager.Instance;
            
            bool inLobby = false;
            if (useVirtualNetworkP2P)
            {
                inLobby = steamLobbyMgr != null && steamLobbyMgr.IsInLobby;
            }
            else
            {
                inLobby = steamNetSockets != null && steamNetSockets.LobbyId.IsValid();
            }
            
            DrawSteamUserInfo();
            
            GUILayout.Space(5);
            
            // 未加入大厅：显示创建/浏览房间界面
            if (!inLobby)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("=== 公众大厅 ===", GUI.skin.box);
                
                // 创建/浏览切换按钮
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("创建房间", showLobbyBrowser ? GUILayout.Height(35) : GUILayout.Height(35)))
                {
                    showLobbyBrowser = false;
                }
                if (GUILayout.Button("浏览房间", showLobbyBrowser ? GUILayout.Height(35) : GUILayout.Height(35)))
                {
                    showLobbyBrowser = true;
                    if (useVirtualNetworkP2P)
                    {
                        if (steamLobbyMgr != null)
                        {
                            Debug.Log("[UI] 请求大厅列表（虚拟网络）");
                            EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance?.RequestLobbyList();
                        }
                    }
                    else if (EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance != null)
                    {
                        Debug.Log("[UI] 请求大厅列表（混合模式）");
                        EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance.RequestLobbyList();
                    }
                }
                GUILayout.EndHorizontal();
                
                GUILayout.Space(10);
                
                if (!showLobbyBrowser)
                {
                    GUILayout.Label("创建新房间", GUI.skin.box);
                    
                    GUILayout.Label("大厅类型:");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Toggle(lobbyType == Steamworks.ELobbyType.k_ELobbyTypePublic, "公开"))
                        lobbyType = Steamworks.ELobbyType.k_ELobbyTypePublic;
                    if (GUILayout.Toggle(lobbyType == Steamworks.ELobbyType.k_ELobbyTypeFriendsOnly, "仅好友"))
                        lobbyType = Steamworks.ELobbyType.k_ELobbyTypeFriendsOnly;
                    if (GUILayout.Toggle(lobbyType == Steamworks.ELobbyType.k_ELobbyTypePrivate, "私有"))
                        lobbyType = Steamworks.ELobbyType.k_ELobbyTypePrivate;
                    GUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                GUILayout.Label("房间名称(必填):");
                lobbyName = GUILayout.TextField(lobbyName, GUILayout.Width(500));
                
                GUILayout.Space(5);
                GUILayout.Label("房间密码(可选):");
                lobbyPassword = GUILayout.TextField(lobbyPassword, GUILayout.Width(500));
                
                GUILayout.Space(5);
                if (GUILayout.Button("创建Steam大厅", GUILayout.Height(40)))
                {
                    Debug.Log("[UI] ==================== 用户点击创建大厅 ====================");
                    if (string.IsNullOrEmpty(lobbyName))
                    {
                        status = "请输入房间名称！";
                        Debug.LogWarning("[UI] 房间名称为空");
                    }
                    else
                    {
                        if (useVirtualNetworkP2P)
                        {
                            Debug.Log("[UI] 使用虚拟网络P2P模式创建大厅");
                            if (steamLobbyMgr != null)
                            {
                                Debug.Log("[UI] 调用SteamLobbyManager.CreateLobby，类型: " + lobbyType + ", 名称: " + lobbyName + ", 密码: " + (string.IsNullOrEmpty(lobbyPassword) ? "无" : "有"));
                                steamLobbyMgr.CreateLobby(lobbyName, lobbyPassword, 4, lobbyType);
                                status = "正在创建Steam大厅（虚拟网络）...";
                                Debug.Log("[UI] 服务器将在大厅创建成功后自动启动");
                            }
                        }
                        else if (HybridService != null)
                        {
                            Debug.Log("[UI] 使用混合P2P模式创建大厅");
                            HybridService.Initialize(currentNetworkMode);
                            var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                            if (steamNet != null)
                            {
                                Debug.Log("[UI] 调用CreateLobby，类型: " + lobbyType + ", 名称: " + lobbyName + ", 密码: " + (string.IsNullOrEmpty(lobbyPassword) ? "无" : "有"));
                                steamNet.CreateLobby(4, lobbyType, lobbyName, lobbyPassword);
                                status = "正在创建Steam大厅（混合）...";
                            }
                            HybridService.StartServer(4);
                        }
                    }
                }
                }
                else
                {
                    GUILayout.Label("浏览可用房间", GUI.skin.box);
                    
                    DrawLobbyBrowserEnhanced();
                    
                    GUILayout.Space(10);
                    GUILayout.Label("或手动输入大厅ID:");
                    steamLobbyId = GUILayout.TextField(steamLobbyId, GUILayout.Width(500));
                    
                    GUILayout.Label("房间密码(如有):");
                    lobbyPassword = GUILayout.TextField(lobbyPassword, GUILayout.Width(500));
                    
                    GUILayout.Space(5);
                    if (GUILayout.Button("加入Steam大厅", GUILayout.Height(40)))
                    {
                        if (!string.IsNullOrEmpty(steamLobbyId) && ulong.TryParse(steamLobbyId, out ulong lobbyIdNum))
                        {
                            Debug.Log("[UI] 用户请求加入大厅: " + steamLobbyId);
                            JoinSteamLobby(new Steamworks.CSteamID(lobbyIdNum), lobbyPassword);
                        }
                        else
                        {
                            status = "请输入有效的Steam大厅ID";
                            Debug.LogWarning("[UI] 无效的大厅ID: " + steamLobbyId);
                        }
                    }
                }
                
                GUILayout.EndVertical();
                
                GUILayout.Space(10);
                GUILayout.Label("状态: " + status);
            }
            else
            {
                // 判断当前玩家是否为主机
                // 虚拟网络模式：通过大厅所有权判断（因为NetService可能未启动）
                // 混合模式：直接使用SteamNetworkingSocketsManager的IsServer标志
                bool actuallyIsServer;
                if (useVirtualNetworkP2P)
                {
                    bool isServerMode = NetService.Instance != null && NetService.Instance.IsServer;
                    bool isLobbyOwner = steamLobbyMgr != null && steamLobbyMgr.GetLobbyOwner() == Steamworks.SteamUser.GetSteamID();
                    actuallyIsServer = isServerMode || isLobbyOwner;
                }
                else
                {
                    actuallyIsServer = steamNetSockets.IsServer;
                }
                
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("=== Steam大厅房间 ===", GUI.skin.box);
                
                GUILayout.BeginHorizontal();
                GUILayout.Label("大厅ID:", GUILayout.Width(80));
                GUILayout.TextField(steamLobbyId, GUILayout.Width(300));
                if (GUILayout.Button("复制", GUILayout.Width(80), GUILayout.Height(25)))
                {
                    GUIUtility.systemCopyBuffer = steamLobbyId;
                    status = "已复制大厅ID到剪贴板";
                    Debug.Log("[UI] 复制大厅ID: " + steamLobbyId);
                }
                GUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label("角色:", GUILayout.Width(80));
                GUILayout.Label(actuallyIsServer ? "主机（房主）" : "客户端（玩家）");
                GUILayout.EndHorizontal();
                
                GUILayout.Label("状态: " + status);
                
                GUILayout.EndVertical();
                
                GUILayout.Space(10);
                
                if (useVirtualNetworkP2P)
                {
                    DrawVirtualNetworkPlayerList();
                }
                else
                {
                    DrawSteamPlayerList();
                }
                
                GUILayout.Space(10);
                if (actuallyIsServer)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label("=== 房主控制 ===", GUI.skin.box);
                    
                    if (GUILayout.Button("邀请好友", GUILayout.Height(35)))
                    {
                        Debug.Log("[UI] 打开Steam邀请窗口");
                        if (Steamworks.SteamUtils.IsOverlayEnabled())
                        {
                            Steamworks.CSteamID lobbyIdToInvite;
                            if (useVirtualNetworkP2P && steamLobbyMgr != null)
                            {
                                lobbyIdToInvite = new Steamworks.CSteamID(ulong.Parse(steamLobbyId));
                            }
                            else
                            {
                                lobbyIdToInvite = steamNetSockets.LobbyId;
                            }
                            Steamworks.SteamFriends.ActivateGameOverlayInviteDialog(lobbyIdToInvite);
                        }
                        else
                        {
                            status = "Steam覆盖层未启用";
                            Debug.LogWarning("[UI] Steam覆盖层未启用");
                        }
                    }
                    
                    GUILayout.EndVertical();
                }
                else
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    Color oldColor = GUI.color;
                    GUI.color = new Color(0.8f, 0.8f, 0.8f);
                    GUILayout.Label("=== 玩家身份 ===", GUI.skin.box);
                    GUILayout.Label("您是房间成员，只有房主可以邀请好友");
                    GUI.color = oldColor;
                    GUILayout.EndVertical();
                }
                
                GUILayout.Space(10);
                if (GUILayout.Button("离开大厅", GUILayout.Height(40)))
                {
                    Debug.Log("[UI] ==================== 用户点击离开大厅 ====================");
                    Debug.Log("[UI] 当前IsServer: " + IsServer + ", LobbyID: " + steamLobbyId);
                    Debug.Log("[UI] 使用虚拟网络P2P: " + useVirtualNetworkP2P);
                    
                    if (useVirtualNetworkP2P)
                    {
                        Debug.Log("[UI] 虚拟网络模式：停止网络服务");
                        if (NetService.Instance != null && NetService.Instance.networkStarted)
                        {
                            NetService.Instance.StopNetwork();
                        }
                        
                        if (steamLobbyMgr != null)
                        {
                            Debug.Log("[UI] 调用SteamLobbyManager.LeaveLobby");
                            steamLobbyMgr.LeaveLobby();
                        }
                    }
                    else
                    {
                        Debug.Log("[UI] 混合模式：断开HybridService");
                        if (HybridService != null)
                        {
                            Debug.Log("[UI] 断开HybridService网络连接");
                            HybridService.Disconnect();
                        }
                        
                        var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                        if (steamNet != null)
                        {
                            Debug.Log("[UI] 调用SteamNetworkingSocketsManager.LeaveLobby");
                            steamNet.LeaveLobby();
                        }
                    }
                    
                    steamLobbyId = "";
                    lobbyPassword = "";
                    lobbyName = "";
                    showLobbyBrowser = false;
                    status = "已离开大厅";
                    
                    Debug.Log("[UI] ==================== UI状态已重置 ====================");
                }
            }
        }
        
        private void DrawLobbyBrowser()
        {
            GUILayout.Label("可用大厅 (" + availableLobbies.Count + "): ");
            
            if (GUILayout.Button("刷新列表"))
            {
                if (EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance != null)
                {
                    EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance.RequestLobbyList();
                }
            }
            
            lobbyListScrollPos = GUILayout.BeginScrollView(lobbyListScrollPos, GUILayout.Height(200));
            
            foreach (var lobby in availableLobbies)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();
                GUILayout.Label(lobby.LobbyName + " " + (lobby.HasPassword ? "🔒" : ""));
                GUILayout.Label("主机: " + lobby.OwnerName + " | 玩家: " + lobby.CurrentPlayers + "/" + lobby.MaxPlayers);
                GUILayout.EndVertical();
                
                if (GUILayout.Button("加入", GUILayout.Width(60)))
                {
                    steamLobbyId = lobby.LobbyId.m_SteamID.ToString();
                    if (lobby.HasPassword)
                    {
                        status = "此大厅需要密码，请输入密码后加入";
                    }
                    else
                    {
                        var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                        if (steamNet != null)
                        {
                            steamNet.JoinLobby(lobby.LobbyId);
                        }
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
            
            GUILayout.EndScrollView();
        }
        
        private void DrawFriendList()
        {
            GUILayout.Label("Steam好友列表:");
            
            friendListScrollPos = GUILayout.BeginScrollView(friendListScrollPos, GUILayout.Height(150));
            
            try
            {
                int friendCount = Steamworks.SteamFriends.GetFriendCount(Steamworks.EFriendFlags.k_EFriendFlagImmediate);
                
                for (int i = 0; i < friendCount; i++)
                {
                    Steamworks.CSteamID friendId = Steamworks.SteamFriends.GetFriendByIndex(i, Steamworks.EFriendFlags.k_EFriendFlagImmediate);
                    string friendName = Steamworks.SteamFriends.GetFriendPersonaName(friendId);
                    Steamworks.EPersonaState state = Steamworks.SteamFriends.GetFriendPersonaState(friendId);
                    
                    string stateStr = state == Steamworks.EPersonaState.k_EPersonaStateOnline ? "在线" : "离线";
                    
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(friendName + " (" + stateStr + ")");
                    if (GUILayout.Button("邀请", GUILayout.Width(60)))
                    {
                        var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                        if (steamNet != null)
                        {
                            steamNet.InviteUserToLobby(friendId);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            catch
            {
                GUILayout.Label("Steam未初始化");
            }
            
            GUILayout.EndScrollView();
        }
        
        private void DrawLANUI()
        {
            if (!IsServer)
            {
                GUILayout.Label("局域网主机列表");

                if (hostList.Count == 0)
                {
                    GUILayout.Label("等待广播回应，暂无主机");
                }
                else
                {
                    foreach (var host in hostList)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("连接", GUILayout.Width(60)))
                        {
                            var parts = host.Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int p))
                            {
                                if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
                                {
                                    NetService.Instance.StartNetwork(false);
                                }

                                NetService.Instance.ConnectToHost(parts[0], p);
                            }
                        }
                        GUILayout.Label(host);
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(20);
                GUILayout.Label("手动输入 IP 和端口连接:");
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(40));
                manualIP = GUILayout.TextField(manualIP, GUILayout.Width(150));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("端口:", GUILayout.Width(40));
                manualPort = GUILayout.TextField(manualPort, GUILayout.Width(150));
                GUILayout.EndHorizontal();
                if (GUILayout.Button("手动连接"))
                {
                    if (int.TryParse(manualPort, out int p))
                    {
                        if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
                        {
                            NetService.Instance.StartNetwork(false);
                        }

                        NetService.Instance.ConnectToHost(manualIP, p);
                    }
                    else
                    {
                        status = "端口格式错误";
                    }
                }

                GUILayout.Space(20);
                GUILayout.Label("状态: " + status);
            }
            else
            {
                GUILayout.Label("服务器监听端口: " + port);
                GUILayout.Label("当前连接数: " + (netManager != null ? netManager.ConnectedPeerList.Count : 0));
            }
        }

        private void DrawPlayerStatusWindow(int windowID)
        {
            if (GUI.Button(new Rect(playerStatusWindowRect.width - 25, 5, 20, 20), "×"))
            {
                showPlayerStatusWindow = false;
            }

            playerStatusScrollPos = GUILayout.BeginScrollView(playerStatusScrollPos, GUILayout.ExpandWidth(true));

            if (localPlayerStatus != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("ID: " + localPlayerStatus.EndPoint, GUILayout.Width(180));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("名称: " + localPlayerStatus.PlayerName, GUILayout.Width(180));
                GUILayout.Label("延迟: " + localPlayerStatus.Latency + "ms", GUILayout.Width(100));
                GUILayout.Label("游戏中: " + (localPlayerStatus.IsInGame ? "是" : "否"));
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }

            if (IsServer)
            {
                foreach (var kvp in playerStatuses)
                {
                    var st = kvp.Value;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("ID: " + st.EndPoint, GUILayout.Width(180));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("名称: " + st.PlayerName, GUILayout.Width(180));
                    GUILayout.Label("延迟: " + st.Latency + "ms", GUILayout.Width(100));
                    GUILayout.Label("游戏中: " + (st.IsInGame ? "是" : "否"));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(10);
                }
            }
            else
            {
                foreach (var kvp in clientPlayerStatuses)
                {
                    var st = kvp.Value;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("ID: " + st.EndPoint, GUILayout.Width(180));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("名称: " + st.PlayerName, GUILayout.Width(180));
                    GUILayout.Label("延迟: " + st.Latency + "ms", GUILayout.Width(100));
                    GUILayout.Label("游戏中: " + (st.IsInGame ? "是" : "否"));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(10);
                }
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private string GetPlayerDisplayName(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return "Unknown";

            if (pid.StartsWith("Steam:"))
            {
                try
                {
                    if (pid.Length > 6)
                    {
                        string steamIdStr = pid.Substring(6);
                        if (ulong.TryParse(steamIdStr, out ulong steamIdValue))
                        {
                            CSteamID steamId = new CSteamID(steamIdValue);
                            string steamName = SteamFriends.GetFriendPersonaName(steamId);
                            if (!string.IsNullOrEmpty(steamName))
                            {
                                return steamName;
                            }
                        }
                    }
                }
                catch
                {
                }
                return "Steam玩家";
            }

            if (pid.StartsWith("Host:"))
            {
                if (SteamManager.Initialized)
                {
                    return SteamFriends.GetPersonaName() + " (主机)";
                }
                return "主机";
            }

            if (pid.StartsWith("Client:"))
            {
                return "玩家";
            }

            return pid;
        }
    }
}
