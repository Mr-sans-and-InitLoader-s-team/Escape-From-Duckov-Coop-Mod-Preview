using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public class SteamNetworkTransport : MonoBehaviour
    {
        public static SteamNetworkTransport Instance { get; private set; }

        private SteamNetworkingSocketsManager steamNetSockets;
        private Dictionary<CSteamID, SteamPeerProxy> peerProxies = new Dictionary<CSteamID, SteamPeerProxy>();
        private SteamPeerProxy serverProxy;

        public event Action<SteamPeerProxy> OnPeerConnectedEvent;
        public event Action<SteamPeerProxy> OnPeerDisconnectedEvent;
        public event Action<SteamPeerProxy, byte[], int> OnNetworkReceiveEvent;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void Initialize()
        {
            if (steamNetSockets == null)
            {
                steamNetSockets = SteamNetworkingSocketsManager.Instance;
                if (steamNetSockets == null)
                {
                    Debug.LogError("[SteamNetTransport] 未找到SteamNetworkingSocketsManager实例");
                    return;
                }
            }

            steamNetSockets.OnPeerConnected += HandlePeerConnected;
            steamNetSockets.OnPeerDisconnected += HandlePeerDisconnected;
            steamNetSockets.OnDataReceived += HandleDataReceived;
            steamNetSockets.OnLobbyCreated += HandleLobbyCreated;
            steamNetSockets.OnLobbyJoined += HandleLobbyJoined;

            Debug.Log("[SteamNetTransport] 初始化完成");
        }

        public void StartAsServer(int maxPlayers = 4)
        {
            Debug.Log("[SteamNetTransport] 启动服务器，最大玩家: " + maxPlayers);
            steamNetSockets.StartServer();
        }

        public void ConnectToServer(CSteamID lobbyId)
        {
            Debug.Log("[SteamNetTransport] 连接到服务器，大厅ID: " + lobbyId);
        }

        public void Disconnect()
        {
            Debug.Log("[SteamNetTransport] 断开连接");
            steamNetSockets.LeaveLobby();
            peerProxies.Clear();
            serverProxy = null;
        }

        public void SendToServer(byte[] data, int length, DeliveryMethod method)
        {
            if (serverProxy == null)
            {
                Debug.LogError("[SteamNetTransport] SendToServer失败: serverProxy为null！");
                Debug.LogError("[SteamNetTransport] 客户端可能未正确设置serverProxy");
                return;
            }
            
            Debug.Log("[SteamNetTransport] 客户端发送数据到主机");
            Debug.Log("[SteamNetTransport] 主机SteamID: " + serverProxy.SteamId);
            Debug.Log("[SteamNetTransport] 数据大小: " + length + " bytes");
            Debug.Log("[SteamNetTransport] 传输方式: " + method);
            
            int sendFlags = ConvertDeliveryMethod(method);
            bool success = steamNetSockets.SendPacket(serverProxy.SteamId, data, length, sendFlags);
            
            if (!success)
            {
                Debug.LogError("[SteamNetTransport]  SendToServer失败: SendPacket返回false");
            }
            else
            {
                Debug.Log("[SteamNetTransport]  SendToServer成功");
            }
        }

        public void SendToPeer(SteamPeerProxy peer, byte[] data, int length, DeliveryMethod method)
        {
            if (peer != null)
            {
                int sendFlags = ConvertDeliveryMethod(method);
                steamNetSockets.SendPacket(peer.SteamId, data, length, sendFlags);
            }
        }

        public void BroadcastToAll(byte[] data, int length, DeliveryMethod method)
        {
            int sendFlags = ConvertDeliveryMethod(method);
            steamNetSockets.BroadcastPacket(data, length, sendFlags);
        }

        private void HandlePeerConnected(CSteamID steamId)
        {
            if (!peerProxies.ContainsKey(steamId))
            {
                var proxy = new SteamPeerProxy(steamId);
                peerProxies[steamId] = proxy;
                OnPeerConnectedEvent?.Invoke(proxy);
                Debug.Log("[SteamNetTransport] 对等连接建立: " + steamId);
            }
        }

        private void HandlePeerDisconnected(CSteamID steamId)
        {
            if (peerProxies.TryGetValue(steamId, out var proxy))
            {
                peerProxies.Remove(steamId);
                OnPeerDisconnectedEvent?.Invoke(proxy);
                Debug.Log("[SteamNetTransport] 对等连接断开: " + steamId);
            }
        }

        private void HandleDataReceived(CSteamID senderId, byte[] data, int length)
        {
            if (peerProxies.TryGetValue(senderId, out var proxy))
            {
                OnNetworkReceiveEvent?.Invoke(proxy, data, length);
            }
            else if (serverProxy != null && serverProxy.SteamId == senderId)
            {
                OnNetworkReceiveEvent?.Invoke(serverProxy, data, length);
            }
        }

        private void HandleLobbyCreated(CSteamID lobbyId)
        {
            Debug.Log("[SteamNetTransport] 作为主机创建大厅: " + lobbyId);
        }

        private void HandleLobbyJoined(CSteamID lobbyId)
        {
            CSteamID ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
            CSteamID mySteamId = SteamUser.GetSteamID();
            
            Debug.Log("[SteamNetTransport] ==================== 大厅加入处理 ====================");
            Debug.Log("[SteamNetTransport] 大厅ID: " + lobbyId);
            Debug.Log("[SteamNetTransport] 大厅所有者: " + ownerId);
            Debug.Log("[SteamNetTransport] 我的SteamID: " + mySteamId);
            
            if (ownerId != mySteamId)
            {
                serverProxy = new SteamPeerProxy(ownerId);
                Debug.Log("[SteamNetTransport]  设置serverProxy: " + serverProxy.EndPoint);
                Debug.Log("[SteamNetTransport] 作为客户端，准备连接到主机");
            }
            else
            {
                Debug.Log("[SteamNetTransport] 我是主机，不需要设置serverProxy");
            }
        }

        private int ConvertDeliveryMethod(DeliveryMethod method)
        {
            switch (method)
            {
                case DeliveryMethod.ReliableOrdered:
                case DeliveryMethod.ReliableUnordered:
                case DeliveryMethod.ReliableSequenced:
                    return Constants.k_nSteamNetworkingSend_Reliable;
                
                case DeliveryMethod.Sequenced:
                    return Constants.k_nSteamNetworkingSend_Reliable | Constants.k_nSteamNetworkingSend_NoNagle;
                
                case DeliveryMethod.Unreliable:
                default:
                    return Constants.k_nSteamNetworkingSend_Unreliable;
            }
        }

        public bool IsServer => steamNetSockets != null && steamNetSockets.IsServer;
        public bool IsConnected => steamNetSockets != null && steamNetSockets.LobbyId.IsValid();
        public IReadOnlyDictionary<CSteamID, SteamPeerProxy> Peers => peerProxies;
        public SteamPeerProxy ServerPeer => serverProxy;

        private void OnDestroy()
        {
            if (steamNetSockets != null)
            {
                steamNetSockets.OnPeerConnected -= HandlePeerConnected;
                steamNetSockets.OnPeerDisconnected -= HandlePeerDisconnected;
                steamNetSockets.OnDataReceived -= HandleDataReceived;
                steamNetSockets.OnLobbyCreated -= HandleLobbyCreated;
                steamNetSockets.OnLobbyJoined -= HandleLobbyJoined;
            }
        }
    }

    public class SteamPeerProxy
    {
        public CSteamID SteamId { get; private set; }
        public string EndPoint { get; private set; }
        
        public SteamPeerProxy(CSteamID steamId)
        {
            SteamId = steamId;
            EndPoint = steamId.ToString();
        }

        public override string ToString()
        {
            return EndPoint;
        }

        public override int GetHashCode()
        {
            return SteamId.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is SteamPeerProxy other)
            {
                return SteamId == other.SteamId;
            }
            return false;
        }
    }
}
