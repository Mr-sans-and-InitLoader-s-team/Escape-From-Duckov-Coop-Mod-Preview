using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public class SteamNetworkingSocketsManager : MonoBehaviour
    {
        private static SteamNetworkingSocketsManager instance;
        public static SteamNetworkingSocketsManager Instance => instance;

        // 回调
        public event Action<CSteamID, byte[], int> OnDataReceived;
        public event Action<CSteamID> OnPeerConnected;
        public event Action<CSteamID> OnPeerDisconnected;
        
        // Steam大厅相关
        public event Action<CSteamID> OnLobbyCreated;
        public event Action<CSteamID> OnLobbyJoined;
        public event Action OnLobbyLeft;
        public event Action<List<LobbyInfo>> OnLobbyListReceived;
        
        // 网络状态
        private bool isInitialized = false;
        private bool isServer = false;
        
        // 大厅信息
        private CSteamID currentLobbyId = CSteamID.Nil;
        private string currentLobbyPassword = "";
        private string currentLobbyName = "";
        
        // 连接管理
        private HSteamListenSocket listenSocket = HSteamListenSocket.Invalid;
        public Dictionary<CSteamID, HSteamNetConnection> peerConnections = new Dictionary<CSteamID, HSteamNetConnection>();
        private Dictionary<HSteamNetConnection, CSteamID> connectionToPeer = new Dictionary<HSteamNetConnection, CSteamID>();
        private Dictionary<CSteamID, PingInfo> peerPings = new Dictionary<CSteamID, PingInfo>();
        
        private class PingInfo
        {
            public int CurrentPing = 0;
            public long LastPingSentTime = 0;
            public long LastPingReceivedTime = 0;
        }
        
        // Steam Callbacks
        private Callback<SteamNetConnectionStatusChangedCallback_t> connectionStatusCallback;
        private Callback<LobbyCreated_t> lobbyCreatedCallback;
        private Callback<LobbyEnter_t> lobbyEnterCallback;
        private Callback<LobbyChatUpdate_t> lobbyChatUpdateCallback;
        private Callback<LobbyMatchList_t> lobbyMatchListCallback;
        private Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequestedCallback;
        
        // 常量
        private const string LOBBY_PASSWORD_KEY = "password";
        private const string LOBBY_MOD_ID_KEY = "mod_id";
        private const string LOBBY_NAME_KEY = "name";
        private const string MOD_IDENTIFIER = "EscapeFromDuckovCoopMod_v1.0";
        private const int MAX_MESSAGE_SIZE = 512 * 1024; // 512KB
        private const int VIRTUAL_PORT = 27015;
        
        // 消息缓冲区
        private IntPtr[] messageBuffers = new IntPtr[256];
        
        public bool IsInitialized => isInitialized;
        public bool IsServer => isServer;
        public CSteamID LobbyId => currentLobbyId;
        public int ConnectedPeerCount => peerConnections.Count;
        
        public int GetPeerPing(CSteamID peerId)
        {
            if (peerPings.TryGetValue(peerId, out PingInfo pingInfo))
            {
                return pingInfo.CurrentPing;
            }
            return -1;
        }

        public bool IsP2PConnected(CSteamID peerId)
        {
            return peerConnections.ContainsKey(peerId);
        }

        public string GetConnectionStatus(CSteamID peerId)
        {
            if (!currentLobbyId.IsValid())
            {
                return "未在大厅";
            }

            if (peerId == SteamUser.GetSteamID())
            {
                return "本地玩家";
            }

            if (peerConnections.ContainsKey(peerId))
            {
                if (peerPings.TryGetValue(peerId, out PingInfo pingInfo))
                {
                    return "已连接 (" + pingInfo.CurrentPing + "ms)";
                }
                return "已连接";
            }

            return "未建立P2P";
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private float pingUpdateTimer = 0f;
        private const float PING_UPDATE_INTERVAL = 1.0f;

        private void SendPingPackets()
        {
            long currentTime = System.DateTime.UtcNow.Ticks / System.TimeSpan.TicksPerMillisecond;
            
            foreach (var kvp in peerConnections)
            {
                CSteamID peerId = kvp.Key;
                HSteamNetConnection conn = kvp.Value;
                
                if (!peerPings.ContainsKey(peerId))
                {
                    peerPings[peerId] = new PingInfo();
                }
                
                byte[] pingData = System.BitConverter.GetBytes(currentTime);
                byte[] packet = new byte[pingData.Length + 1];
                packet[0] = 255;
                System.Array.Copy(pingData, 0, packet, 1, pingData.Length);
                
                System.IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(packet.Length);
                System.Runtime.InteropServices.Marshal.Copy(packet, 0, dataPtr, packet.Length);
                
                EResult result = SteamNetworkingSockets.SendMessageToConnection(
                    conn, 
                    dataPtr, 
                    (uint)packet.Length, 
                    Constants.k_nSteamNetworkingSend_Unreliable, 
                    out long messageNum
                );
                
                System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
                
                if (result == EResult.k_EResultOK)
                {
                    peerPings[peerId].LastPingSentTime = currentTime;
                }
            }
        }

        private void ProcessPingPacket(CSteamID senderId, byte[] data)
        {
            if (data.Length < 9 || data[0] != 255)
                return;
            
            long sentTime = System.BitConverter.ToInt64(data, 1);
            long currentTime = System.DateTime.UtcNow.Ticks / System.TimeSpan.TicksPerMillisecond;
            int rtt = (int)(currentTime - sentTime);
            
            if (!peerPings.ContainsKey(senderId))
            {
                peerPings[senderId] = new PingInfo();
            }
            
            peerPings[senderId].CurrentPing = rtt;
            peerPings[senderId].LastPingReceivedTime = currentTime;
            
            byte[] pongData = new byte[data.Length];
            data.CopyTo(pongData, 0);
            pongData[0] = 254;
            
            if (peerConnections.TryGetValue(senderId, out HSteamNetConnection conn))
            {
                System.IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(pongData.Length);
                System.Runtime.InteropServices.Marshal.Copy(pongData, 0, dataPtr, pongData.Length);
                
                SteamNetworkingSockets.SendMessageToConnection(
                    conn, 
                    dataPtr, 
                    (uint)pongData.Length, 
                    Constants.k_nSteamNetworkingSend_Unreliable, 
                    out long messageNum
                );
                
                System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
            }
        }

        private void ProcessPongPacket(CSteamID senderId, byte[] data)
        {
            if (data.Length < 9 || data[0] != 254)
                return;
            
            long sentTime = System.BitConverter.ToInt64(data, 1);
            long currentTime = System.DateTime.UtcNow.Ticks / System.TimeSpan.TicksPerMillisecond;
            int rtt = (int)(currentTime - sentTime);
            
            if (!peerPings.ContainsKey(senderId))
            {
                peerPings[senderId] = new PingInfo();
            }
            
            peerPings[senderId].CurrentPing = rtt;
            peerPings[senderId].LastPingReceivedTime = currentTime;
        }

        public void Initialize()
        {
            if (isInitialized)
            {
                Debug.Log("[SteamNetSockets] 已经初始化");
                return;
            }

            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamNetSockets] Steam未初始化");
                return;
            }

            Debug.Log("[SteamNetSockets] ==================== 初始化 ====================");
            
            // 初始化回调
            connectionStatusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
            lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreatedCallback);
            lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnterCallback);
            lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdateCallback);
            lobbyMatchListCallback = Callback<LobbyMatchList_t>.Create(OnLobbyMatchListCallback);
            gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequestedCallback);

            isInitialized = true;
            Debug.Log("[SteamNetSockets] 初始化完成，使用 ISteamNetworkingSockets API");
        }

        private void Update()
        {
            if (!isInitialized) return;
            
            // 接收所有连接的消息
            ReceiveMessages();
            
            // 定期发送ping包
            if (peerConnections.Count > 0)
            {
                pingUpdateTimer += Time.deltaTime;
                if (pingUpdateTimer >= PING_UPDATE_INTERVAL)
                {
                    pingUpdateTimer = 0f;
                    SendPingPackets();
                }
            }
        }

        #region 大厅管理

        public void CreateLobby(int maxPlayers, ELobbyType lobbyType, string lobbyName, string password = "")
        {
            if (!isInitialized)
            {
                Debug.LogError("[SteamNetSockets] 未初始化");
                return;
            }
            
            Debug.Log("[SteamNetSockets] ==================== 创建大厅 ====================");
            Debug.Log("[SteamNetSockets] 类型: " + lobbyType + ", 最大玩家数: " + maxPlayers);
            Debug.Log("[SteamNetSockets] 名称: " + lobbyName);
            Debug.Log("[SteamNetSockets] 密码: " + (string.IsNullOrEmpty(password) ? "无" : "有"));
            
            currentLobbyName = lobbyName;
            currentLobbyPassword = password;
            SteamAPICall_t apiCall = SteamMatchmaking.CreateLobby(lobbyType, maxPlayers);
            Debug.Log("[SteamNetSockets] Steam API调用ID: " + apiCall.m_SteamAPICall);
        }

        public void JoinLobby(CSteamID lobbyId, string password = "")
        {
            if (!isInitialized)
            {
                Debug.LogError("[SteamNetSockets] 未初始化");
                return;
            }

            Debug.Log("[SteamNetSockets] ==================== 加入大厅 ====================");
            Debug.Log("[SteamNetSockets] 大厅ID: " + lobbyId);
            Debug.Log("[SteamNetSockets] 密码: " + (string.IsNullOrEmpty(password) ? "无" : "有"));
            
            currentLobbyPassword = password;
            SteamMatchmaking.JoinLobby(lobbyId);
        }

        public void LeaveLobby()
        {
            if (!currentLobbyId.IsValid())
            {
                Debug.LogWarning("[SteamNetSockets] 不在大厅中");
                return;
            }

            Debug.Log("[SteamNetSockets] ==================== 离开大厅 ====================");
            Debug.Log("[SteamNetSockets] 大厅ID: " + currentLobbyId);
            
            // 关闭所有连接
            CloseAllConnections();
            
            // 离开Steam大厅
            CSteamID oldLobby = currentLobbyId;
            SteamMatchmaking.LeaveLobby(currentLobbyId);
            
            currentLobbyId = CSteamID.Nil;
            currentLobbyPassword = "";
            currentLobbyName = "";
            isServer = false;
            
            Debug.Log("[SteamNetSockets] 已离开大厅: " + oldLobby);
            OnLobbyLeft?.Invoke();
        }

        public void RequestLobbyList()
        {
            if (!isInitialized)
            {
                Debug.LogError("[SteamNetSockets] 未初始化");
                return;
            }

            Debug.Log("[SteamNetSockets] 请求大厅列表");
            
            // 添加过滤器
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            SteamMatchmaking.AddRequestLobbyListStringFilter(LOBBY_MOD_ID_KEY, MOD_IDENTIFIER, ELobbyComparison.k_ELobbyComparisonEqual);
            
            SteamMatchmaking.RequestLobbyList();
        }

        public void InviteUserToLobby(CSteamID friendId)
        {
            if (!isInitialized || !currentLobbyId.IsValid())
            {
                Debug.LogError("[SteamNetSockets] 未在大厅中");
                return;
            }

            Debug.Log("[SteamNetSockets] 邀请好友: " + friendId + " (" + SteamFriends.GetFriendPersonaName(friendId) + ")");
            
            bool success = SteamMatchmaking.InviteUserToLobby(currentLobbyId, friendId);
            if (success)
            {
                Debug.Log("[SteamNetSockets] 邀请已发送");
            }
            else
            {
                Debug.LogError("[SteamNetSockets] 邀请发送失败");
            }
        }

        public string GetLobbyData(string key)
        {
            if (!currentLobbyId.IsValid()) return "";
            return SteamMatchmaking.GetLobbyData(currentLobbyId, key);
        }

        public void SetLobbyData(string key, string value)
        {
            if (!currentLobbyId.IsValid() || !isServer) return;
            
            Debug.Log("[SteamNetSockets] 设置大厅数据: " + key + " = " + value);
            SteamMatchmaking.SetLobbyData(currentLobbyId, key, value);
        }

        #endregion

        #region 连接管理

        public void StartServer()
        {
            if (listenSocket != HSteamListenSocket.Invalid)
            {
                Debug.LogWarning("[SteamNetSockets] 服务器已启动");
                return;
            }

            Debug.Log("[SteamNetSockets] ==================== 启动服务器 ====================");
            
            isServer = true;
            
            Debug.Log("[SteamNetSockets] 创建P2P监听套接字，虚拟端口: " + VIRTUAL_PORT);
            listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(VIRTUAL_PORT, 0, null);
            
            if (listenSocket == HSteamListenSocket.Invalid)
            {
                Debug.LogError("[SteamNetSockets] 创建P2P监听套接字失败!");
                return;
            }
            
            Debug.Log("[SteamNetSockets] P2P监听套接字创建成功: " + listenSocket.m_HSteamListenSocket);
            Debug.Log("[SteamNetSockets] P2P服务器模式，等待客户端连接");
        }

        public void ConnectToHost(CSteamID hostSteamId)
        {
            if (peerConnections.ContainsKey(hostSteamId))
            {
                Debug.LogWarning("[SteamNetSockets] 已经连接到主机: " + hostSteamId);
                Debug.LogWarning("[SteamNetSockets] 连接句柄: " + peerConnections[hostSteamId].m_HSteamNetConnection);
                return;
            }

            Debug.Log("[SteamNetSockets] ==================== 连接到主机 ====================");
            Debug.Log("[SteamNetSockets] 主机SteamID: " + hostSteamId);
            Debug.Log("[SteamNetSockets] 我的SteamID: " + SteamUser.GetSteamID());
            Debug.Log("[SteamNetSockets] 虚拟端口: " + VIRTUAL_PORT);
            Debug.Log("[SteamNetSockets] 当前已有连接数: " + peerConnections.Count);
            
            SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
            identity.SetSteamID(hostSteamId);
            
            HSteamNetConnection conn = SteamNetworkingSockets.ConnectP2P(ref identity, VIRTUAL_PORT, 0, null);
            
            if (conn == HSteamNetConnection.Invalid)
            {
                Debug.LogError("[SteamNetSockets]  ConnectP2P返回无效句柄！");
                return;
            }
            
            peerConnections[hostSteamId] = conn;
            connectionToPeer[conn] = hostSteamId;
            
            Debug.Log("[SteamNetSockets]  P2P连接已初始化");
            Debug.Log("[SteamNetSockets] 连接句柄: " + conn.m_HSteamNetConnection);
            Debug.Log("[SteamNetSockets] 目标SteamID: " + hostSteamId);
            Debug.Log("[SteamNetSockets] 已存储到peerConnections，当前总数: " + peerConnections.Count);
        }

        private void CloseAllConnections()
        {
            Debug.Log("[SteamNetSockets] 关闭所有连接，当前连接数: " + peerConnections.Count);
            
            foreach (var conn in peerConnections.Values)
            {
                SteamNetworkingSockets.CloseConnection(conn, 0, "Leaving lobby", false);
            }
            
            peerConnections.Clear();
            connectionToPeer.Clear();
            
            if (listenSocket != HSteamListenSocket.Invalid)
            {
                SteamNetworkingSockets.CloseListenSocket(listenSocket);
                listenSocket = HSteamListenSocket.Invalid;
            }
            
            Debug.Log("[SteamNetSockets] 所有连接已关闭");
        }

        #endregion

        #region 数据传输

        public bool SendPacket(CSteamID target, byte[] data, int length, int sendFlags = Constants.k_nSteamNetworkingSend_Reliable)
        {
            if (!peerConnections.TryGetValue(target, out HSteamNetConnection conn))
            {
                Debug.LogError("[P2P发包] 未找到到目标的连接！");
                Debug.LogError("[P2P发包] 目标SteamID: " + target);
                Debug.LogError("[P2P发包] IsServer: " + isServer);
                Debug.LogError("[P2P发包] 当前连接数: " + peerConnections.Count);
                
                // 打印所有已连接的peers
                int idx = 0;
                foreach (var kvp in peerConnections)
                {
                    Debug.LogError("[P2P发包] [" + idx + "] 已连接SteamID: " + kvp.Key + " → 连接句柄: " + kvp.Value.m_HSteamNetConnection);
                    idx++;
                }
                
                return false;
            }

            if (length > MAX_MESSAGE_SIZE)
            {
                Debug.LogError("[P2P发包] 数据包过大: " + length + " bytes");
                return false;
            }

            // Debug.Log("[P2P发包] 发送数据包 -> 目标: " + target + ", 大小: " + length + " bytes");
            
            System.IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(length);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, dataPtr, length);
            
            EResult result = SteamNetworkingSockets.SendMessageToConnection(conn, dataPtr, (uint)length, sendFlags, out long messageNumber);
            
            System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
            
            if (result != EResult.k_EResultOK)
            {
                Debug.LogError("[P2P发包] 发送失败: " + result);
                return false;
            }
            
            // Debug.Log("[P2P发包] 发送成功，消息编号: " + messageNumber);
            return true;
        }

        public void BroadcastPacket(byte[] data, int length, int sendFlags = Constants.k_nSteamNetworkingSend_Reliable)
        {
            // Debug.Log("[P2P广播] 广播数据包，目标数量: " + peerConnections.Count + ", 大小: " + length + " bytes");
            int successCount = 0;
            
            foreach (var peer in peerConnections.Keys)
            {
                if (SendPacket(peer, data, length, sendFlags))
                {
                    successCount++;
                }
            }
            
            // Debug.Log("[P2P广播] 广播完成，成功: " + successCount + "/" + peerConnections.Count);
        }

        private void ReceiveMessages()
        {
            int totalReceived = 0;
            
            foreach (var kvp in peerConnections)
            {
                HSteamNetConnection conn = kvp.Value;
                CSteamID peer = kvp.Key;
                
                int numMessages = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, messageBuffers, messageBuffers.Length);
                
                if (numMessages > 0)
                {
                    // Debug.Log("[P2P收包] 从 " + peer + " 收到 " + numMessages + " 个消息");
                    
                    for (int i = 0; i < numMessages; i++)
                    {
                        IntPtr msgPtr = messageBuffers[i];
                        SteamNetworkingMessage_t msg = SteamNetworkingMessage_t.FromIntPtr(msgPtr);
                        
                        int size = msg.m_cbSize;
                        byte[] data = new byte[size];
                        System.Runtime.InteropServices.Marshal.Copy(msg.m_pData, data, 0, size);
                        
                        
                        if (size > 0 && (data[0] == 255 || data[0] == 254 || data[0] == 253))
                        {
                            if (data[0] == 255)
                            {
                                ProcessPingPacket(peer, data);
                            }
                            else if (data[0] == 254)
                            {
                                ProcessPongPacket(peer, data);
                            }
                            else if (data[0] == 253)
                            {
                                Debug.Log("[P2P收包] ==================== 收到欢迎消息 ====================");
                                Debug.Log("[P2P收包] 来自: " + peer);
                                Debug.Log("[P2P收包] 连接完全建立！");
                            }
                        }
                        else
                        {
                            // Debug.Log("[P2P收包] 消息 #" + (i + 1) + ", 大小: " + size + " bytes");
                            OnDataReceived?.Invoke(peer, data, size);
                        }
                        
                        // 释放消息
                        SteamNetworkingMessage_t.Release(msgPtr);
                    }
                    
                    totalReceived += numMessages;
                }
            }
            
            // if (totalReceived > 0)
            // {
            //     Debug.Log("[P2P收包] 本帧共收到 " + totalReceived + " 个数据包");
            // }
        }

        #endregion

        #region Steam回调处理

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            Debug.Log("[SteamNetSockets] ==================== 连接状态变更 ====================");
            Debug.Log("[SteamNetSockets] 连接: " + callback.m_hConn.m_HSteamNetConnection);
            Debug.Log("[SteamNetSockets] 旧状态: " + callback.m_eOldState);
            Debug.Log("[SteamNetSockets] 新状态: " + callback.m_info.m_eState);
            
            HSteamNetConnection conn = callback.m_hConn;
            SteamNetConnectionInfo_t info = callback.m_info;
            
            switch (info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    Debug.Log("[SteamNetSockets] 连接中...");
                    Debug.Log("[SteamNetSockets] 当前isServer=" + isServer);
                    if (isServer)
                    {
                        CSteamID remoteSteamId = info.m_identityRemote.GetSteamID();
                        Debug.Log("[SteamNetSockets] 服务器接受来自 " + remoteSteamId + " 的连接");
                        
                        EResult result = SteamNetworkingSockets.AcceptConnection(conn);
                        Debug.Log("[SteamNetSockets] AcceptConnection结果: " + result);
                        
                        if (result == EResult.k_EResultOK)
                        {
                            peerConnections[remoteSteamId] = conn;
                            connectionToPeer[conn] = remoteSteamId;
                            Debug.Log("[SteamNetSockets] 连接已接受并记录: " + remoteSteamId);
                        }
                        else
                        {
                            Debug.LogError("[SteamNetSockets] 接受连接失败: " + result);
                        }
                    }
                    else
                    {
                        Debug.Log("[SteamNetSockets] 客户端等待连接建立...");
                    }
                    break;
                    
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    Debug.Log("[SteamNetSockets] 连接已建立");
                    CSteamID connectedPeer = info.m_identityRemote.GetSteamID();
                    Debug.Log("[SteamNetSockets] 对方SteamID: " + connectedPeer);
                    
                    if (isServer)
                    {
                        Debug.Log("[SteamNetSockets] 服务器向客户端 " + connectedPeer + " 发送欢迎消息");
                        byte[] welcomeData = new byte[1];
                        welcomeData[0] = 253;
                        SendPacket(connectedPeer, welcomeData, 1, Constants.k_nSteamNetworkingSend_Reliable);
                    }
                    else
                    {
                        Debug.Log("[SteamNetSockets] 客户端已成功连接到服务器");
                    }
                    
                    OnPeerConnected?.Invoke(connectedPeer);
                    break;
                    
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Debug.Log("[SteamNetSockets] 连接关闭");
                    Debug.Log("[SteamNetSockets] 原因: " + info.m_eEndReason + " - " + info.m_szEndDebug);
                    
                    if (connectionToPeer.TryGetValue(conn, out CSteamID disconnectedPeer))
                    {
                        peerConnections.Remove(disconnectedPeer);
                        connectionToPeer.Remove(conn);
                        Debug.Log("[SteamNetSockets] 移除连接: " + disconnectedPeer);
                        OnPeerDisconnected?.Invoke(disconnectedPeer);
                    }
                    
                    SteamNetworkingSockets.CloseConnection(conn, 0, "", false);
                    break;
            }
        }

        private void OnLobbyCreatedCallback(LobbyCreated_t callback)
        {
            if (SteamP2PLoader.Instance != null && SteamP2PLoader.Instance.UseSteamP2P)
            {
                Debug.Log("[SteamNetSockets] 虚拟网络P2P模式已启用，跳过混合模式大厅创建处理");
                return;
            }
            
            Debug.Log("[SteamNetSockets] ==================== 大厅创建回调 ====================");
            Debug.Log("[SteamNetSockets] 结果: " + callback.m_eResult);
            
            if (callback.m_eResult == EResult.k_EResultOK)
            {
                currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
                isServer = true;
                
                Debug.Log("[SteamNetSockets] 大厅创建成功，ID: " + currentLobbyId);
                Debug.Log("[SteamNetSockets] 已设置为主机模式 isServer = true");
                
                SetLobbyData(LOBBY_MOD_ID_KEY, MOD_IDENTIFIER);
                if (!string.IsNullOrEmpty(currentLobbyName))
                {
                    SetLobbyData(LOBBY_NAME_KEY, currentLobbyName);
                }
                if (!string.IsNullOrEmpty(currentLobbyPassword))
                {
                    SetLobbyData(LOBBY_PASSWORD_KEY, currentLobbyPassword);
                }
                
                if (listenSocket == HSteamListenSocket.Invalid)
                {
                    Debug.Log("[SteamNetSockets] 大厅创建后启动服务器模式");
                    StartServer();
                }
                
                OnLobbyCreated?.Invoke(currentLobbyId);
            }
            else
            {
                Debug.LogError("[SteamNetSockets] 大厅创建失败: " + callback.m_eResult);
            }
        }

        private void OnLobbyEnterCallback(LobbyEnter_t callback)
        {
            if (SteamP2PLoader.Instance != null && SteamP2PLoader.Instance.UseSteamP2P)
            {
                Debug.Log("[SteamNetSockets] 虚拟网络P2P模式已启用，跳过混合模式大厅加入处理");
                return;
            }
            
            Debug.Log("[SteamNetSockets] ==================== 进入大厅回调 ====================");
            Debug.Log("[SteamNetSockets] 大厅ID: " + callback.m_ulSteamIDLobby);
            Debug.Log("[SteamNetSockets] 响应: " + callback.m_EChatRoomEnterResponse);
            
            if (callback.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
                
                // 检查密码
                string lobbyPassword = GetLobbyData(LOBBY_PASSWORD_KEY);
                if (!string.IsNullOrEmpty(lobbyPassword) && lobbyPassword != currentLobbyPassword)
                {
                    Debug.LogError("[SteamNetSockets] 密码错误");
                    LeaveLobby();
                    return;
                }
                
                Debug.Log("[SteamNetSockets] 成功进入大厅");
                
                // 如果不是服务器，连接到主机
                if (!isServer)
                {
                    CSteamID hostId = SteamMatchmaking.GetLobbyOwner(currentLobbyId);
                    Debug.Log("[SteamNetSockets] 主机SteamID: " + hostId);
                    ConnectToHost(hostId);
                }
                
                OnLobbyJoined?.Invoke(currentLobbyId);
            }
            else
            {
                Debug.LogError("[SteamNetSockets] 进入大厅失败: " + callback.m_EChatRoomEnterResponse);
            }
        }

        private void OnLobbyChatUpdateCallback(LobbyChatUpdate_t callback)
        {
            Debug.Log("[SteamNetSockets] 大厅成员变更: " + callback.m_ulSteamIDUserChanged);
            Debug.Log("[SteamNetSockets] 变更类型: " + callback.m_rgfChatMemberStateChange);
            
            // 可以在这里处理玩家加入/离开事件
        }

        private void OnLobbyMatchListCallback(LobbyMatchList_t callback)
        {
            Debug.Log("[SteamNetSockets] 收到大厅列表，数量: " + callback.m_nLobbiesMatching);
            
            List<LobbyInfo> lobbies = new List<LobbyInfo>();
            
            for (int i = 0; i < callback.m_nLobbiesMatching; i++)
            {
                CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                string modId = SteamMatchmaking.GetLobbyData(lobbyId, LOBBY_MOD_ID_KEY);
                
                // 只显示匹配的mod
                if (modId == MOD_IDENTIFIER)
                {
                    string ownerName = SteamFriends.GetFriendPersonaName(SteamMatchmaking.GetLobbyOwner(lobbyId));
                    string lobbyName = SteamMatchmaking.GetLobbyData(lobbyId, LOBBY_NAME_KEY);
                    if (string.IsNullOrEmpty(lobbyName))
                    {
                        lobbyName = ownerName + "的房间";
                    }
                    
                    LobbyInfo info = new LobbyInfo
                    {
                        LobbyId = lobbyId,
                        LobbyName = lobbyName,
                        OwnerName = ownerName,
                        CurrentPlayers = SteamMatchmaking.GetNumLobbyMembers(lobbyId),
                        MaxPlayers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId),
                        HasPassword = !string.IsNullOrEmpty(SteamMatchmaking.GetLobbyData(lobbyId, LOBBY_PASSWORD_KEY))
                    };
                    
                    lobbies.Add(info);
                }
            }
            
            Debug.Log("[SteamNetSockets] 匹配的大厅数: " + lobbies.Count);
            OnLobbyListReceived?.Invoke(lobbies);
        }

        private void OnGameLobbyJoinRequestedCallback(GameLobbyJoinRequested_t callback)
        {
            Debug.Log("[SteamNetSockets] 收到加入大厅请求: " + callback.m_steamIDLobby);
            JoinLobby(callback.m_steamIDLobby);
        }

        #endregion

        private void OnDestroy()
        {
            CloseAllConnections();
        }
    }

    public class LobbyInfo
    {
        public CSteamID LobbyId;
        public string LobbyName;
        public string OwnerName;
        public int CurrentPlayers;
        public int MaxPlayers;
        public bool HasPassword;
        public bool IsCompatibleMod = true;
    }
}

