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

using Steamworks;
using System.Collections.Generic;
using UnityEngine;

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
    private string _manualIP = "r.pansx.net";
    private string _manualPort = "9050";
    private int _port = 9050;
    private string _status = "";
    private Rect mainWindowRect = new(10, 10, 400, 700);
    private Vector2 playerStatusScrollPos = Vector2.zero;
    private Rect playerStatusWindowRect = new(420, 10, 300, 400);
    private readonly List<SteamLobbyManager.LobbyInfo> _steamLobbyInfos = new();
    private string _steamLobbyName = string.Empty;
    private string _steamLobbyPassword = string.Empty;
    private bool _steamLobbyFriendsOnly;
    private int _steamLobbyMaxPlayers = 2;
    private string _steamJoinPassword = string.Empty;
    private bool _steamShowLobbyBrowser;

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
            status = CoopLocalization.Get("ui.steam.error.notInitialized");
            return;
        }

        var password = lobby.RequiresPassword ? _steamJoinPassword : string.Empty;
        if (manager.TryJoinLobbyWithPassword(lobby.LobbyId, password, out var error))
        {
            status = CoopLocalization.Get("ui.status.connecting");
            return;
        }

        switch (error)
        {
            case SteamLobbyManager.LobbyJoinError.SteamNotInitialized:
                status = CoopLocalization.Get("ui.steam.error.notInitialized");
                break;
            case SteamLobbyManager.LobbyJoinError.LobbyMetadataUnavailable:
                status = CoopLocalization.Get("ui.steam.error.metadata");
                break;
            case SteamLobbyManager.LobbyJoinError.IncorrectPassword:
                status = CoopLocalization.Get("ui.steam.error.password");
                break;
            default:
                status = CoopLocalization.Get("ui.steam.error.generic");
                break;
        }
    }

    private void DrawMainWindow(int windowID)
    {
        GUILayout.BeginVertical();
        DrawTransportModeSelector();

        GUILayout.Space(10);
        if (TransportMode == NetworkTransportMode.SteamP2P)
        {
            DrawSteamMode();
        }
        else
        {
            GUILayout.Label($"{CoopLocalization.Get("ui.mode.current")}: {(IsServer ? CoopLocalization.Get("ui.mode.server") : CoopLocalization.Get("ui.mode.client"))}");

            if (GUILayout.Button(CoopLocalization.Get("ui.mode.switchTo", IsServer ? CoopLocalization.Get("ui.mode.client") : CoopLocalization.Get("ui.mode.server"))))
            {
                var target = !IsServer;
                NetService.Instance.StartNetwork(target);
            }

            GUILayout.Space(10);

            if (IsServer)
                DrawDirectServerSection();
            else
                DrawDirectClientSection();
        }

        GUILayout.Space(20);
        var displayStatus = string.IsNullOrEmpty(status) ? CoopLocalization.Get("ui.status.notConnected") : status;
        GUILayout.Label($"{CoopLocalization.Get("ui.status.label")} {displayStatus}");

        GUILayout.Space(10);
        showPlayerStatusWindow = GUILayout.Toggle(showPlayerStatusWindow, CoopLocalization.Get("ui.playerStatus.toggle", toggleWindowKey));

        if (GUILayout.Button(CoopLocalization.Get("ui.debug.printLootBoxes")))
        {
            foreach (var i in LevelManager.LootBoxInventories)
            {
                try
                {
                    Debug.Log($"Name {i.Value.name}" + $" DisplayNameKey {i.Value.DisplayNameKey}" + $" Key {i.Key}");
                }
                catch
                {
                }
            }
        }

        GUILayout.EndVertical();
        GUI.DragWindow();
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