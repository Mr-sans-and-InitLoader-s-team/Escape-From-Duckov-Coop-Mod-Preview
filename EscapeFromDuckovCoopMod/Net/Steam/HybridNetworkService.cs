using Steamworks;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public enum NetworkMode
    {
        LAN,
        SteamP2P
    }

    public class HybridNetworkService : MonoBehaviour
    {
        public static HybridNetworkService Instance { get; private set; }

        private NetworkMode currentMode = NetworkMode.LAN;
        private SteamNetworkTransport steamTransport;
        private NetService lanService;

        private Dictionary<string, object> peerMap = new Dictionary<string, object>();
        private Dictionary<string, NetPeer> fakePeerCache = new Dictionary<string, NetPeer>();

        public NetworkMode CurrentMode => currentMode;
        public bool IsServer { get; private set; }
        public bool IsConnected { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void Initialize(NetworkMode mode)
        {
            currentMode = mode;
            lanService = NetService.Instance;

            if (mode == NetworkMode.SteamP2P)
            {
                if (steamTransport == null)
                {
                    steamTransport = SteamNetworkTransport.Instance;
                    if (steamTransport == null)
                    {
                        Debug.LogError("[HybridNetwork] 未找到SteamNetworkTransport实例");
                        return;
                    }
                }
                
                steamTransport.OnPeerConnectedEvent += HandleSteamPeerConnected;
                steamTransport.OnPeerDisconnectedEvent += HandleSteamPeerDisconnected;
                steamTransport.OnNetworkReceiveEvent += HandleSteamDataReceived;

                Debug.Log("[HybridNetwork] 混合网络服务初始化为Steam P2P模式");
            }
            else
            {
                Debug.Log("[HybridNetwork] 混合网络服务初始化为LAN模式");
            }
        }

        public void StartServer(int maxPlayers = 4)
        {
            Debug.Log("==================== 开始启动服务器 ====================");
            Debug.Log("网络模式: " + currentMode);
            Debug.Log("最大玩家数: " + maxPlayers);
            
            IsServer = true;
            
            if (currentMode == NetworkMode.SteamP2P)
            {
                Debug.Log("[Steam P2P] 正在启动Steam P2P服务器...");
                steamTransport.StartAsServer(maxPlayers);
                Debug.Log("[Steam P2P] Steam P2P传输层已启动");
                
                Debug.Log("[Steam P2P] 初始化游戏服务器逻辑...");
                lanService.InitializeGameLogic(true);
                Debug.Log("[Steam P2P] 游戏服务器逻辑初始化完成");
                
                Debug.Log("[Steam P2P] 大厅ID: " + GetLobbyId());
            }
            else
            {
                Debug.Log("[LAN] 正在启动LAN服务器...");
                lanService.StartNetwork(true);
                Debug.Log("[LAN] LAN服务器已启动，端口: " + lanService.port);
            }

            IsConnected = true;
            Debug.Log("服务器启动完成！IsServer=" + IsServer + ", IsConnected=" + IsConnected);
            Debug.Log("==================== 服务器启动完成 ====================");
        }
        
        public void StartClient()
        {
            Debug.Log("==================== 开始启动客户端 ====================");
            Debug.Log("网络模式: " + currentMode);
            
            IsServer = false;
            
            if (currentMode == NetworkMode.SteamP2P)
            {
                Debug.Log("[Steam P2P] 正在启动Steam P2P客户端...");
                Debug.Log("[Steam P2P] 初始化游戏客户端逻辑...");
                
                if (lanService != null)
                {
                    lanService.InitializeGameLogic(false);
                    Debug.Log("[Steam P2P] 游戏客户端逻辑初始化完成");
                }
                else
                {
                    Debug.LogWarning("[Steam P2P] NetService实例为null，跳过游戏逻辑初始化");
                }
                
                IsConnected = true;
                Debug.Log("[Steam P2P] Steam P2P客户端已启动，等待与主机建立连接");
                Debug.Log("[Steam P2P] 当前大厅ID: " + GetLobbyId());
            }
            else
            {
                Debug.Log("[LAN] 正在启动LAN客户端...");
                if (lanService != null)
                {
                    lanService.StartNetwork(false);
                    Debug.Log("[LAN] LAN客户端已启动");
                }
                else
                {
                    Debug.LogError("[LAN] NetService实例为null，无法启动LAN客户端");
                }
            }
            
            Debug.Log("客户端启动完成！IsServer=" + IsServer + ", IsConnected=" + IsConnected);
            Debug.Log("==================== 客户端启动完成 ====================");
        }

        public void ConnectToServer(string address)
        {
            IsServer = false;

            if (currentMode == NetworkMode.SteamP2P)
            {
                if (ulong.TryParse(address, out ulong lobbyIdNum))
                {
                    CSteamID lobbyId = new CSteamID(lobbyIdNum);
                    steamTransport.ConnectToServer(lobbyId);
                    IsConnected = true;
                }
                else
                {
                    Debug.LogError("无效的Steam大厅ID: " + address);
                }
            }
            else
            {
                lanService.StartNetwork(false);
                if (int.TryParse(lanService.manualPort, out int port))
                {
                    lanService.netManager.Connect(address, port, "gameKey");
                }
            }
        }

        public void Disconnect()
        {
            Debug.Log("==================== 开始断开网络 ====================");
            Debug.Log("当前模式: " + currentMode);
            Debug.Log("IsServer: " + IsServer + ", IsConnected: " + IsConnected);
            
            if (currentMode == NetworkMode.SteamP2P)
            {
                Debug.Log("[Steam P2P] 正在断开Steam P2P连接...");
                steamTransport.Disconnect();
                Debug.Log("[Steam P2P] Steam P2P连接已断开");
            }
            else
            {
                Debug.Log("[LAN] 正在停止LAN网络...");
                lanService.StopNetwork();
                Debug.Log("[LAN] LAN网络已停止");
            }

            IsConnected = false;
            IsServer = false;
            peerMap.Clear();
            
            Debug.Log("网络断开完成！IsServer=" + IsServer + ", IsConnected=" + IsConnected);
            Debug.Log("==================== 网络断开完成 ====================");
        }

        public void SendData(byte[] data, int length, DeliveryMethod method)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[HybridNetwork] SendData失败: 未连接");
                return;
            }

            if (currentMode == NetworkMode.SteamP2P)
            {
                if (IsServer)
                {
                    Debug.LogWarning("[HybridNetwork] SendData: 服务器不应该调用SendData，应该使用BroadcastData");
                    steamTransport.BroadcastToAll(data, length, method);
                }
                else
                {
                    // 客户端发送给主机
                    steamTransport.SendToServer(data, length, method);
                }
            }
            else
            {
                if (lanService.connectedPeer != null)
                {
                    lanService.connectedPeer.Send(data, method);
                }
                else if (IsServer)
                {
                    foreach (var peer in lanService.netManager.ConnectedPeerList)
                    {
                        peer.Send(data, method);
                    }
                }
            }
        }

        public void BroadcastData(byte[] data, int length, DeliveryMethod method)
        {
            if (!IsConnected || !IsServer) return;

            if (currentMode == NetworkMode.SteamP2P)
            {
                steamTransport.BroadcastToAll(data, length, method);
            }
            else
            {
                foreach (var peer in lanService.netManager.ConnectedPeerList)
                {
                    peer.Send(data, method);
                }
            }
        }

        public string GetLobbyId()
        {
            if (currentMode == NetworkMode.SteamP2P && steamTransport != null)
            {
                var lobbyId = SteamNetworkingSocketsManager.Instance.LobbyId;
                if (lobbyId.IsValid())
                {
                    return lobbyId.m_SteamID.ToString();
                }
            }
            return string.Empty;
        }

        private void HandleSteamPeerConnected(SteamPeerProxy peer)
        {
            Debug.Log("Steam对等端连接: " + peer.EndPoint);
            peerMap[peer.EndPoint] = peer;

            if (lanService != null)
            {
                bool isServer = lanService.IsServer;
                
                // 服务器端：添加到 playerStatuses
                if (isServer)
                {
                    if (!lanService.playerStatuses.ContainsKey(peer.EndPoint))
                    {
                        lanService.playerStatuses[peer.EndPoint] = new PlayerStatus
                        {
                            EndPoint = peer.EndPoint,
                            PlayerName = "SteamPlayer_" + peer.SteamId,
                            IsInGame = false,
                            Position = Vector3.zero,
                            Rotation = Quaternion.identity
                        };
                        Debug.Log("[SERVER] 添加P2P客户端到playerStatuses: " + peer.EndPoint);
                    }
                }
                // 客户端：添加到 clientPlayerStatuses
                else
                {
                    if (!lanService.clientPlayerStatuses.ContainsKey(peer.EndPoint))
                    {
                        lanService.clientPlayerStatuses[peer.EndPoint] = new PlayerStatus
                        {
                            EndPoint = peer.EndPoint,
                            PlayerName = "SteamPlayer_" + peer.SteamId,
                            IsInGame = false,
                            Position = Vector3.zero,
                            Rotation = Quaternion.identity
                        };
                        Debug.Log("[CLIENT] 添加P2P玩家到clientPlayerStatuses: " + peer.EndPoint);
                    }
                }
            }
        }

        private void HandleSteamPeerDisconnected(SteamPeerProxy peer)
        {
            Debug.Log("Steam对等端断开: " + peer.EndPoint);
            peerMap.Remove(peer.EndPoint);

            if (lanService != null)
            {
                bool isServer = lanService.IsServer;
                
                if (isServer)
                {
                    // 服务器端：从 playerStatuses 和 remoteCharacters 移除
                    lanService.playerStatuses.Remove(peer.EndPoint);
                    if (lanService.remoteCharacters.TryGetValue(peer.EndPoint, out var character))
                    {
                        if (character != null)
                        {
                            Destroy(character);
                        }
                        lanService.remoteCharacters.Remove(peer.EndPoint);
                    }
                    Debug.Log("[SERVER] 移除P2P客户端: " + peer.EndPoint);
                }
                else
                {
                    // 客户端：从 clientPlayerStatuses 和 clientRemoteCharacters 移除
                    lanService.clientPlayerStatuses.Remove(peer.EndPoint);
                    if (lanService.clientRemoteCharacters.TryGetValue(peer.EndPoint, out var character))
                    {
                        if (character != null)
                        {
                            Destroy(character);
                        }
                        lanService.clientRemoteCharacters.Remove(peer.EndPoint);
                    }
                    Debug.Log("[CLIENT] 移除P2P玩家: " + peer.EndPoint);
                }
            }
        }

        private void HandleSteamDataReceived(SteamPeerProxy peer, byte[] data, int length)
        {
            if (lanService == null)
            {
                Debug.LogWarning("[HybridNetwork] lanService为null，跳过数据处理");
                return;
            }
            
            if (ModBehaviourF.Instance == null)
            {
                return;
            }
            
            try
            {
                var readerType = typeof(NetPacketReader);
                var reader = (NetPacketReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(readerType);
                
                var dataReaderType = typeof(NetDataReader);
                var dataField = readerType.GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic);
                if (dataField == null)
                {
                    var baseFields = dataReaderType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (var field in baseFields)
                    {
                        if (field.Name.Contains("data") || field.Name.Contains("Data"))
                        {
                            dataField = field;
                            break;
                        }
                    }
                }
                
                var positionField = readerType.GetField("_position", BindingFlags.Instance | BindingFlags.NonPublic);
                if (positionField == null)
                {
                    positionField = dataReaderType.GetField("_position", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                
                var dataLengthField = readerType.GetField("_dataSize", BindingFlags.Instance | BindingFlags.NonPublic);
                if (dataLengthField == null)
                {
                    dataLengthField = dataReaderType.GetField("_dataSize", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                
                if (dataField != null)
                {
                    dataField.SetValue(reader, data);
                }
                
                if (positionField != null)
                {
                    positionField.SetValue(reader, 0);
                }
                
                if (dataLengthField != null)
                {
                    dataLengthField.SetValue(reader, length);
                }
                
                NetPeer fakePeer = CreateFakeNetPeer(peer.EndPoint);
                if (fakePeer == null || fakePeer.EndPoint == null)
                {
                    Debug.LogError("[HybridNetwork] 无法为EndPoint创建fakePeer: " + peer.EndPoint);
                    return;
                }
                ModBehaviourF.Instance.OnNetworkReceive(fakePeer, reader, 0, DeliveryMethod.ReliableOrdered);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[HybridNetwork] Steam数据处理错误: " + ex.Message);
                Debug.LogError("[HybridNetwork] 堆栈: " + ex.StackTrace);
            }
        }
        
        private NetPeer CreateFakeNetPeer(string endPoint)
        {
            if (fakePeerCache.TryGetValue(endPoint, out var cached))
            {
                return cached;
            }
            
            var netPeerType = typeof(NetPeer);
            var fakePeer = (NetPeer)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(netPeerType);
            
            // 尝试多个可能的字段名
            var endPointField = netPeerType.GetField("_remoteEndPoint", BindingFlags.Instance | BindingFlags.NonPublic);
            if (endPointField == null)
            {
                endPointField = netPeerType.GetField("_endPoint", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (endPointField == null)
            {
                endPointField = netPeerType.GetField("EndPoint", BindingFlags.Instance | BindingFlags.Public);
            }
            
            if (endPointField == null)
            {
                // 列出所有字段用于调试
                var allFields = netPeerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Debug.LogError("[HybridNetwork] 无法找到NetPeer的EndPoint字段。所有字段: " + string.Join(", ", System.Linq.Enumerable.Select(allFields, f => f.Name)));
            }
            
            if (endPointField != null)
            {
                try
                {
                    int port = endPoint.GetHashCode() & 0xFFFF;
                    if (port < 1024) port += 10000;
                    var ipEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), port);
                    endPointField.SetValue(fakePeer, ipEndPoint);
                }
                catch
                {
                    var ipEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 9050);
                    endPointField.SetValue(fakePeer, ipEndPoint);
                }
            }
            
            var remoteIdField = netPeerType.GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);
            if (remoteIdField != null)
            {
                remoteIdField.SetValue(fakePeer, endPoint.GetHashCode());
            }
            
            // 验证EndPoint是否设置成功
            if (fakePeer.EndPoint == null)
            {
                Debug.LogError("[HybridNetwork] CreateFakeNetPeer失败：无法设置EndPoint字段，反射可能失败");
                return null;
            }
            
            fakePeerCache[endPoint] = fakePeer;
            return fakePeer;
        }

        private void OnDestroy()
        {
            if (steamTransport != null)
            {
                steamTransport.OnPeerConnectedEvent -= HandleSteamPeerConnected;
                steamTransport.OnPeerDisconnectedEvent -= HandleSteamPeerDisconnected;
                steamTransport.OnNetworkReceiveEvent -= HandleSteamDataReceived;
            }
        }
    }
}

