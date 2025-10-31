using System.Collections.Generic;
using UnityEngine;
using Steamworks;

namespace EscapeFromDuckovCoopMod
{
    public partial class ModUI
    {
        private void DrawSteamUserInfo()
        {
            if (SteamManager.Initialized)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Steam 用户信息", GUI.skin.box);
                string steamName = SteamFriends.GetPersonaName();
                CSteamID steamId = SteamUser.GetSteamID();
                GUILayout.Label("用户名: " + steamName);
                GUILayout.Label("Steam ID: " + steamId.m_SteamID);
                GUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }
        
        private void DrawSteamPlayerList()
        {
            var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
            if (steamNet == null || !steamNet.LobbyId.IsValid()) return;
            
            GUILayout.BeginVertical(GUI.skin.box);
            
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(steamNet.LobbyId);
            int connectedCount = steamNet.ConnectedPeerCount;
            
            GUILayout.Label("在线玩家列表", GUI.skin.box);
            GUILayout.Label("大厅成员: " + memberCount + " | P2P已连接: " + connectedCount);
            GUILayout.Space(3);
            
            playerListScrollPos = GUILayout.BeginScrollView(playerListScrollPos, GUILayout.Height(200));
            
            for (int i = 0; i < memberCount; i++)
            {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(steamNet.LobbyId, i);
                string memberName = SteamFriends.GetFriendPersonaName(memberId);
                bool isOwner = memberId == SteamMatchmaking.GetLobbyOwner(steamNet.LobbyId);
                CSteamID localSteamId = SteamUser.GetSteamID();
                bool isLocalPlayer = memberId == localSteamId;
                
                bool isP2PConnected = steamNet.IsP2PConnected(memberId);
                string connectionStatus = steamNet.GetConnectionStatus(memberId);
                
                Steamworks.EPersonaState playerState = SteamFriends.GetFriendPersonaState(memberId);
                bool isInGame = playerState == Steamworks.EPersonaState.k_EPersonaStateOnline || 
                                playerState == Steamworks.EPersonaState.k_EPersonaStateBusy || 
                                playerState == Steamworks.EPersonaState.k_EPersonaStateLookingToPlay || 
                                playerState == Steamworks.EPersonaState.k_EPersonaStateLookingToTrade;
                
                GUILayout.BeginVertical(GUI.skin.box);
                
                Color oldColor = GUI.color;
                if (isOwner)
                {
                    GUI.color = Color.yellow;
                }
                else if (isLocalPlayer)
                {
                    GUI.color = Color.cyan;
                }
                else if (isP2PConnected)
                {
                    GUI.color = Color.green;
                }
                else
                {
                    GUI.color = new Color(1f, 0.5f, 0f);
                }
                
                GUILayout.BeginHorizontal();
                string playerLabel = "玩家: " + memberName;
                if (isOwner) playerLabel += " [主机]";
                if (isLocalPlayer) playerLabel += " [你]";
                
                GUILayout.Label(playerLabel, GUILayout.Width(280));
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                if (!isLocalPlayer)
                {
                    GUILayout.Label("连接状态: " + connectionStatus, GUILayout.Width(200));
                }
                else
                {
                    GUILayout.Label("连接状态: 本地玩家", GUILayout.Width(200));
                }
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                string steamIdStr = memberId.m_SteamID.ToString();
                string displayId = steamIdStr.Length > 16 ? steamIdStr.Substring(0, 16) + "..." : steamIdStr;
                GUILayout.Label("Steam ID: " + displayId, GUILayout.Width(280));
                GUILayout.EndHorizontal();
                
                GUI.color = oldColor;
                
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
            
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
        
        private void DrawLobbyBrowserEnhanced()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("可用大厅 (" + availableLobbies.Count + ")", GUI.skin.box);
            
            if (GUILayout.Button("刷新列表", GUILayout.Height(25)))
            {
                if (EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance != null)
                {
                    EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance.RequestLobbyList();
                }
            }
            
            lobbyListScrollPos = GUILayout.BeginScrollView(lobbyListScrollPos, GUILayout.Height(250));
            
            foreach (var lobby in availableLobbies)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                
                Color oldColor = GUI.color;
                if (!lobby.IsCompatibleMod)
                {
                    GUI.color = Color.red;
                }
                else
                {
                    GUI.color = Color.green;
                }
                
                GUILayout.BeginHorizontal();
                
                GUILayout.BeginVertical();
                GUILayout.Label(lobby.LobbyName + " " + (lobby.HasPassword ? "🔒" : ""));
                GUILayout.Label("主机: " + lobby.OwnerName);
                GUILayout.Label("玩家: " + lobby.CurrentPlayers + "/" + lobby.MaxPlayers + 
                               " | " + (lobby.IsCompatibleMod ? "[兼容]" : "[不兼容]"));
                GUILayout.EndVertical();
                
                GUI.color = oldColor;
                
                if (lobby.IsCompatibleMod && GUILayout.Button("加入", GUILayout.Width(80), GUILayout.Height(60)))
                {
                    steamLobbyId = lobby.LobbyId.m_SteamID.ToString();
                    if (lobby.HasPassword && string.IsNullOrEmpty(lobbyPassword))
                    {
                        status = "此大厅需要密码，请在下方输入密码后再加入";
                    }
                    else
                    {
                        JoinSteamLobby(lobby.LobbyId, lobby.HasPassword ? lobbyPassword : "");
                        showLobbyBrowser = false;
                    }
                }
                
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(3);
            }
            
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
        
        private void JoinSteamLobby(CSteamID lobbyId, string password)
        {
            Debug.Log("[UI] ==================== JoinSteamLobby ====================");
            Debug.Log("[UI] 大厅ID: " + lobbyId);
            Debug.Log("[UI] 密码: " + (string.IsNullOrEmpty(password) ? "无" : "有"));
            Debug.Log("[UI] 使用虚拟网络P2P: " + ((ModUI)this).IsUsingVirtualNetworkP2P());
            
            if (((ModUI)this).IsUsingVirtualNetworkP2P())
            {
                Debug.Log("[UI] 使用虚拟网络P2P模式加入大厅");
                var steamLobbyMgr = EscapeFromDuckovCoopMod.Net.Steam.SteamLobbyManager.Instance;
                if (steamLobbyMgr != null)
                {
                    Debug.Log("[UI] 调用SteamLobbyManager.JoinLobby");
                    steamLobbyMgr.JoinLobby(lobbyId, password);
                    status = "正在加入Steam大厅（虚拟网络）...";
                }
                else
                {
                    Debug.LogError("[UI] SteamLobbyManager未初始化");
                    status = "Steam Lobby服务未初始化";
                }
            }
            else if (HybridService != null)
            {
                Debug.Log("[UI] 使用混合P2P模式加入大厅");
                HybridService.Initialize(currentNetworkMode);
                var steamNet = EscapeFromDuckovCoopMod.Net.Steam.SteamNetworkingSocketsManager.Instance;
                if (steamNet != null)
                {
                    Debug.Log("[UI] 调用SteamNetworkingSocketsManager.JoinLobby");
                    steamNet.JoinLobby(lobbyId, password);
                    status = "正在连接到Steam大厅（混合）...";
                }
                else
                {
                    Debug.LogError("[UI] SteamNetworkingSocketsManager未初始化");
                    status = "Steam网络服务未初始化";
                }
            }
            else
            {
                Debug.LogError("[UI] 网络服务未初始化");
                status = "网络服务未初始化";
            }
        }
        
        private void DrawVirtualNetworkPlayerList()
        {
            var steamLobbyMgr = EscapeFromDuckovCoopMod.Net.Steam.SteamLobbyManager.Instance;
            if (steamLobbyMgr == null || !steamLobbyMgr.IsInLobby) return;
            
            GUILayout.BeginVertical(GUI.skin.box);
            
            Steamworks.CSteamID currentLobby = steamLobbyMgr.GetCurrentLobbyId();
            int memberCount = Steamworks.SteamMatchmaking.GetNumLobbyMembers(currentLobby);
            
            int connectedCount = 0;
            if (NetService.Instance != null && NetService.Instance.IsServer)
            {
                connectedCount = NetService.Instance.playerStatuses.Count;
            }
            else if (NetService.Instance != null && !NetService.Instance.IsServer && NetService.Instance.connectedPeer != null)
            {
                connectedCount = 1;
            }
            
            GUILayout.Label("在线玩家列表", GUI.skin.box);
            GUILayout.Label("大厅成员: " + memberCount + " | 网络已连接: " + connectedCount);
            GUILayout.Space(3);
            
            playerListScrollPos = GUILayout.BeginScrollView(playerListScrollPos, GUILayout.Height(200));
            
            for (int i = 0; i < memberCount; i++)
            {
                Steamworks.CSteamID memberId = Steamworks.SteamMatchmaking.GetLobbyMemberByIndex(currentLobby, i);
                string memberName = Steamworks.SteamFriends.GetFriendPersonaName(memberId);
                bool isOwner = memberId == Steamworks.SteamMatchmaking.GetLobbyOwner(currentLobby);
                Steamworks.CSteamID localSteamId = Steamworks.SteamUser.GetSteamID();
                bool isLocalPlayer = memberId == localSteamId;
                
                string connectionStatus = "等待连接";
                if (isLocalPlayer)
                {
                    connectionStatus = "本地玩家";
                }
                else if (NetService.Instance != null && NetService.Instance.networkStarted)
                {
                    connectionStatus = "已连接";
                }
                
                GUILayout.BeginVertical(GUI.skin.box);
                
                Color oldColor = GUI.color;
                if (isOwner)
                {
                    GUI.color = Color.yellow;
                }
                else if (isLocalPlayer)
                {
                    GUI.color = Color.cyan;
                }
                else
                {
                    GUI.color = Color.green;
                }
                
                GUILayout.BeginHorizontal();
                string playerLabel = memberName;
                if (isOwner) playerLabel += " [房主]";
                if (isLocalPlayer) playerLabel += " [我]";
                GUILayout.Label(playerLabel, GUILayout.Width(250));
                GUILayout.FlexibleSpace();
                GUILayout.Label(connectionStatus, GUILayout.Width(100));
                GUILayout.EndHorizontal();
                
                GUI.color = oldColor;
                GUILayout.EndVertical();
                GUILayout.Space(3);
            }
            
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
        
        private GUIStyle CreateBoxStyle(Color color)
        {
            GUIStyle style = new GUIStyle(GUI.skin.box);
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            style.normal.background = tex;
            return style;
        }
    }
}

