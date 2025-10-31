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

using Duckov.Economy;
using EscapeFromDuckovCoopMod.Main.Map;

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
    private string _manualIP = "127.0.0.1";
    private string _manualPort = "9050";
    private int _port = 9050;
    private string _status = "";
    private Rect mainWindowRect = new(10, 10, 400, 1000);
    private Vector2 playerStatusScrollPos = Vector2.zero;
    private Rect playerStatusWindowRect = new(420, 10, 300, 400);

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

    private List<MapInfo> mapList;

    private string mapIndex = "1";

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
        }
    }

    private void DrawMainWindow(int windowID)
    {
        GUILayout.BeginVertical();
        GUILayout.Label($"{CoopLocalization.Get("ui.mode.current")}: {(IsServer ? CoopLocalization.Get("ui.mode.server") : CoopLocalization.Get("ui.mode.client"))}");

        if (GUILayout.Button(CoopLocalization.Get("ui.mode.switchTo", IsServer ? CoopLocalization.Get("ui.mode.client") : CoopLocalization.Get("ui.mode.server"))))
        {
            var target = !IsServer;
            NetService.Instance.StartNetwork(target);
        }

        GUILayout.Space(10);

        if (!IsServer)
        {
            GUILayout.Label(CoopLocalization.Get("ui.hostList.title"));

            if (hostList.Count == 0)
                GUILayout.Label(CoopLocalization.Get("ui.hostList.empty"));
            else
                foreach (var host in hostList)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(CoopLocalization.Get("ui.hostList.connect"), GUILayout.Width(60)))
                    {
                        var parts = host.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out var p))
                        {
                            if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted) NetService.Instance.StartNetwork(false);

                            NetService.Instance.ConnectToHost(parts[0], p);
                        }
                    }

                    GUILayout.Label(host);
                    GUILayout.EndHorizontal();
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
                if (int.TryParse(manualPort, out var p))
                {
                    if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted) NetService.Instance.StartNetwork(false);

                    NetService.Instance.ConnectToHost(manualIP, p);
                }
                else
                {
                    status = CoopLocalization.Get("ui.manualConnect.portError");
                }
            }

            GUILayout.Space(20);
            var displayStatus = string.IsNullOrEmpty(status) ? CoopLocalization.Get("ui.status.notConnected") : status;
            GUILayout.Label($"{CoopLocalization.Get("ui.status.label")} {displayStatus}");
        }
        else
        {
            GUILayout.Label($"{CoopLocalization.Get("ui.server.listenPort")} {port}");
            GUILayout.Label($"{CoopLocalization.Get("ui.server.connections")} {netManager?.ConnectedPeerList.Count ?? 0}");
        }

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

        if (GUILayout.Button("所有maplist")) {
            MapManager mgr = MapManager.Instance;
            mapList = mgr.GetMapList();
        }

        if (mapList != null)
        {
            foreach (var map in mapList)
            {
                GUILayout.Label(map.Name);
            }
        }


        GUILayout.Label("地图序号:", GUILayout.Width(40));
        mapIndex = GUILayout.TextField(mapIndex, GUILayout.Width(150));

        if (GUILayout.Button("设置地图"))
        {
            if (int.TryParse(mapIndex, out var i))
            {
                MapManager mgr = MapManager.Instance;
                mgr.SetDefaultMap(mapList[i].Reference);
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