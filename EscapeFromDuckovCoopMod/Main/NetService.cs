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
using System.Net;
using System.Net.Sockets;

namespace EscapeFromDuckovCoopMod;

public enum NetworkTransportMode
{
    Direct,
    SteamP2P
}
public class NetService : MonoBehaviour, INetEventListener
{
    public static NetService Instance;
    public int port = 9050;
    public List<string> hostList = new();
    public bool isConnecting;
    public string status = "";
    public string manualIP = "192.168.123.1";
    public string manualPort = "9050"; // GTX 5090 我也想要
    public bool networkStarted;
    public float broadcastTimer;
    public float broadcastInterval = 5f;
    public float syncTimer;
    public float syncInterval = 0.015f; // =========== Mod开发者注意现在是TI版本也就是满血版无同步延迟，0.03 ~33ms ===================

    public readonly HashSet<int> _dedupeShotFrame = new(); // 本帧已发过的标记

    // ===== 场景切换重连功能 =====
    // 缓存成功连接的IP和端口，用于场景切换后自动重连
    public string cachedConnectedIP = "";
    public int cachedConnectedPort = 0;
    public bool hasSuccessfulConnection = false;
    
    // 重连防抖机制 - 防止重连触发过于频繁
    private float lastReconnectTime = 0f;
    private const float RECONNECT_COOLDOWN = 10f; // 10秒冷却时间
    
    // 连接类型标记 - 区分手动连接和自动重连
    private bool isManualConnection = false; // true: 手动连接(UI点击), false: 自动重连

    // 客户端：按 endPoint(玩家ID) 管理
    public readonly Dictionary<string, PlayerStatus> clientPlayerStatuses = new();
    public readonly Dictionary<string, GameObject> clientRemoteCharacters = new();

    //服务器主机玩家管理
    public readonly Dictionary<NetPeer, PlayerStatus> playerStatuses = new();
    public readonly Dictionary<NetPeer, GameObject> remoteCharacters = new();
    public NetPeer connectedPeer;
    public HashSet<string> hostSet = new();

    //本地玩家状态
    public PlayerStatus localPlayerStatus;

    public NetManager netManager;
    public NetDataWriter writer;
    public bool IsServer { get; private set; }

    public NetworkTransportMode TransportMode { get; private set; } = NetworkTransportMode.Direct;
    public SteamLobbyOptions LobbyOptions { get; private set; } = SteamLobbyOptions.CreateDefault();

    public void OnEnable()
    {
        Instance = this;
    }

    public void OnPeerConnected(NetPeer peer)
    {
        Debug.Log(CoopLocalization.Get("net.connectionSuccess", peer.EndPoint.ToString()));
        connectedPeer = peer;

        if (!IsServer)
        {
            status = CoopLocalization.Get("net.connectedTo", peer.EndPoint.ToString());
            isConnecting = false;
            
            // 只有手动连接成功才更新缓存
            if (isManualConnection)
            {
                cachedConnectedIP = peer.EndPoint.Address.ToString();
                cachedConnectedPort = peer.EndPoint.Port;
                hasSuccessfulConnection = true;
                Debug.Log($"[COOP] 手动连接成功，缓存连接信息: {cachedConnectedIP}:{cachedConnectedPort}");
                isManualConnection = false; // 重置标记
            }
            else
            {
                Debug.Log($"[COOP] 自动重连成功，不更新缓存: {peer.EndPoint.Address}:{peer.EndPoint.Port}");
            }
            
            Send_ClientStatus.Instance.SendClientStatusUpdate();
            
            // 注册主机的 Steam ID 映射（如果使用 Steam P2P）
            RegisterHostSteamId(peer);
            
            // 【调试】客机连接成功后自动发送测试消息
            SendDebugChatMessage();
        }

        // 【调试】如果是主机，当客机连接时发送欢迎消息
        if (IsServer)
        {
            SendHostWelcomeMessage(peer);
        }

        if (!playerStatuses.ContainsKey(peer))
            playerStatuses[peer] = new PlayerStatus
            {
                EndPoint = peer.EndPoint.ToString(),
                PlayerName = IsServer ? $"Player_{peer.Id}" : "Host",
                Latency = peer.Ping,
                IsInGame = false,
                LastIsInGame = false,
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                CustomFaceJson = null
            };

        if (IsServer) SendLocalPlayerStatus.Instance.SendPlayerStatusUpdate();

        if (IsServer)
        {
            // 1) 主机自己
            var hostMain = CharacterMainControl.Main;
            var hostH = hostMain ? hostMain.GetComponentInChildren<Health>(true) : null;
            if (hostH)
            {
                var w = new NetDataWriter();
                w.Put((byte)Op.AUTH_HEALTH_REMOTE);
                w.Put(GetPlayerId(null)); // Host 的 playerId
                try
                {
                    w.Put(hostH.MaxHealth);
                }
                catch
                {
                    w.Put(0f);
                }

                try
                {
                    w.Put(hostH.CurrentHealth);
                }
                catch
                {
                    w.Put(0f);
                }

                peer.Send(w, DeliveryMethod.ReliableOrdered);
            }

            if (remoteCharacters != null)
                foreach (var kv in remoteCharacters)
                {
                    var owner = kv.Key;
                    var go = kv.Value;

                    if (owner == null || go == null) continue;

                    var h = go.GetComponentInChildren<Health>(true);
                    if (!h) continue;

                    var w = new NetDataWriter();
                    w.Put((byte)Op.AUTH_HEALTH_REMOTE);
                    w.Put(GetPlayerId(owner)); // 原主的 playerId
                    try
                    {
                        w.Put(h.MaxHealth);
                    }
                    catch
                    {
                        w.Put(0f);
                    }

                    try
                    {
                        w.Put(h.CurrentHealth);
                    }
                    catch
                    {
                        w.Put(0f);
                    }

                    peer.Send(w, DeliveryMethod.ReliableOrdered);
                }
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Debug.Log(CoopLocalization.Get("net.disconnected", peer.EndPoint.ToString(), disconnectInfo.Reason.ToString()));
        if (!IsServer)
        {
            status = CoopLocalization.Get("net.connectionLost");
            isConnecting = false;
        }

        if (connectedPeer == peer) connectedPeer = null;

        if (playerStatuses.ContainsKey(peer))
        {
            var _st = playerStatuses[peer];
            if (_st != null && !string.IsNullOrEmpty(_st.EndPoint))
                SceneNet.Instance._cliLastSceneIdByPlayer.Remove(_st.EndPoint);
            playerStatuses.Remove(peer);
        }

        if (remoteCharacters.ContainsKey(peer) && remoteCharacters[peer] != null)
        {
            Destroy(remoteCharacters[peer]);
            remoteCharacters.Remove(peer);
        }

        if (!SteamP2PLoader.Instance.UseSteamP2P || SteamP2PManager.Instance == null)
            return;
        try
        {
            Debug.Log($"[Patch_OnPeerDisconnected] LiteNetLib断开: {peer.EndPoint}, 原因: {disconnectInfo.Reason}");
            if (SteamEndPointMapper.Instance != null &&
                SteamEndPointMapper.Instance.TryGetSteamID(peer.EndPoint, out CSteamID remoteSteamID))
            {
                Debug.Log($"[Patch_OnPeerDisconnected] 关闭Steam P2P会话: {remoteSteamID}");
                if (SteamNetworking.CloseP2PSessionWithUser(remoteSteamID))
                {
                    Debug.Log($"[Patch_OnPeerDisconnected] ✓ 成功关闭P2P会话");
                }
                SteamEndPointMapper.Instance.UnregisterSteamID(remoteSteamID);
                Debug.Log($"[Patch_OnPeerDisconnected] ✓ 已清理映射");
                if (SteamP2PManager.Instance != null)
                {
                    SteamP2PManager.Instance.ClearAcceptedSession(remoteSteamID);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Patch_OnPeerDisconnected] 异常: {ex}");
        }



    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        Debug.LogError(CoopLocalization.Get("net.networkError", socketError, endPoint.ToString()));
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        ModBehaviourF.Instance.OnNetworkReceive(peer, reader, channelNumber, deliveryMethod);
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
        UnconnectedMessageType messageType)
    {
        Debug.Log($"[UDP-DEBUG] OnNetworkReceiveUnconnected被调用: 来源={remoteEndPoint}, 类型={messageType}");
        
        string msg = null;
        try
        {
            msg = reader.GetString();
            Debug.Log($"[UDP-DEBUG] 成功读取字符串: 长度={msg?.Length}, 内容前50字符='{(msg?.Length > 50 ? msg.Substring(0, 50) : msg)}'");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UDP-DEBUG] 读取字符串失败: {ex.Message}");
            return;
        }
        
        // 记录所有接收到的UDP包
        Debug.Log($"[UDP] 收到UDP包: 来源={remoteEndPoint}, 消息='{msg}', 类型={messageType}, 长度={msg.Length}");

        Debug.Log($"[UDP-DEBUG] 开始处理消息: IsServer={IsServer}");
        
        if (IsServer && msg == "DISCOVER_REQUEST")
        {
            Debug.Log($"[UDP-DEBUG] 匹配到DISCOVER_REQUEST，准备发送响应");
            writer.Reset();
            writer.Put("DISCOVER_RESPONSE");
            netManager.SendUnconnectedMessage(writer, remoteEndPoint);
            Debug.Log($"[UDP-DEBUG] DISCOVER_RESPONSE已发送");
        }
        else if (!IsServer && msg == "DISCOVER_RESPONSE")
        {
            Debug.Log($"[UDP-DEBUG] 匹配到DISCOVER_RESPONSE");
            var hostInfo = remoteEndPoint.Address + ":" + port;
            if (!hostSet.Contains(hostInfo))
            {
                hostSet.Add(hostInfo);
                hostList.Add(hostInfo);
                Debug.Log(CoopLocalization.Get("net.hostDiscovered", hostInfo));
            }
        }
        else if (IsServer && msg.StartsWith("CHAT_MESSAGE:"))
        {
            Debug.Log($"[UDP-DEBUG] 匹配到CHAT_MESSAGE前缀");
            // 处理UDP聊天消息
            var chatJson = msg.Substring("CHAT_MESSAGE:".Length);
            Debug.Log($"[CHAT] 收到UDP聊天消息: {remoteEndPoint} -> JSON长度={chatJson.Length}");
            Debug.Log($"[CHAT] JSON内容: {chatJson}");
            Debug.Log($"[CHAT] IsServer={IsServer}, ModBehaviourF.Instance={ModBehaviourF.Instance != null}");
            
            // 通过ModBehaviourF处理聊天消息
            if (ModBehaviourF.Instance != null)
            {
                Debug.Log($"[CHAT] 调用HandleUDPChatMessage");
                ModBehaviourF.Instance.HandleUDPChatMessage(chatJson, remoteEndPoint.ToString());
            }
            else
            {
                Debug.LogError($"[CHAT] ModBehaviourF.Instance为null，无法处理聊天消息");
            }
        }
        else
        {
            Debug.Log($"[UDP-DEBUG] 未匹配任何已知消息类型");
            Debug.Log($"[UDP] 未处理的UDP消息: IsServer={IsServer}, 消息前100字符='{(msg.Length > 100 ? msg.Substring(0, 100) : msg)}'");
        }
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        if (playerStatuses.ContainsKey(peer))
            playerStatuses[peer].Latency = latency;
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        if (IsServer)
        {
            if (request.Data != null && request.Data.GetString() == "gameKey") request.Accept();
            else request.Reject();
        }
        else
        {
            request.Reject();
        }
    }

    public void StartNetwork(bool isServer, bool keepSteamLobby = false)
    {
        StopNetwork(!keepSteamLobby);
        COOPManager.AIHandle.freezeAI = !isServer;
        IsServer = isServer;
        writer = new NetDataWriter();
        netManager = new NetManager(this)
        {
            BroadcastReceiveEnabled = true
        };


        if (IsServer)
        {
            var started = netManager.Start(port);
            if (started)
            {
                Debug.Log(CoopLocalization.Get("net.serverStarted", port));
            }
            else
            {
                Debug.LogError(CoopLocalization.Get("net.serverStartFailed"));
            }
        }
        else
        {
            var started = netManager.Start();
            if (started)
            {
                Debug.Log(CoopLocalization.Get("net.clientStarted"));
                if (TransportMode == NetworkTransportMode.Direct)
                {
                    CoopTool.SendBroadcastDiscovery();
                }
            }
            else
            {
                Debug.LogError(CoopLocalization.Get("net.clientStartFailed"));
            }
        }

        networkStarted = true;
        status = CoopLocalization.Get("net.networkStarted");
        hostList.Clear();
        hostSet.Clear();
        isConnecting = false;
        connectedPeer = null;

        playerStatuses.Clear();
        remoteCharacters.Clear();
        clientPlayerStatuses.Clear();
        clientRemoteCharacters.Clear();

        LocalPlayerManager.Instance.InitializeLocalPlayer();
        if (IsServer)
        {
            ItemAgent_Gun.OnMainCharacterShootEvent -= COOPManager.WeaponHandle.Host_OnMainCharacterShoot;
            ItemAgent_Gun.OnMainCharacterShootEvent += COOPManager.WeaponHandle.Host_OnMainCharacterShoot;
        }


        // ===== 正确的 Steam P2P 初始化路径：在 P2P 可用时执行 =====
        bool wantsP2P = TransportMode == NetworkTransportMode.SteamP2P;
        bool p2pAvailable =
            wantsP2P &&
            SteamP2PLoader.Instance != null &&
            SteamManager.Initialized &&
            SteamP2PManager.Instance != null &&   // Loader.Init 正常时会挂上
            SteamP2PLoader.Instance.UseSteamP2P;

        Debug.Log($"[StartNetwork] WantsP2P={wantsP2P}, P2P可用={p2pAvailable}, UseSteamP2P={SteamP2PLoader.Instance?.UseSteamP2P}, " +
                  $"SteamInit={SteamManager.Initialized}, IsServer={IsServer}, NetRunning={netManager?.IsRunning}");

        if (p2pAvailable)
        {
            Debug.Log("[StartNetwork] 联机Mod已启动，初始化Steam P2P组件");

            if (netManager != null)
            {
                // Steam P2P 模式：客户端和主机都启用原生 Socket
                // 通过 Harmony 补丁将所有 Socket 操作重定向到 Steam P2P
                netManager.UseNativeSockets = true;
                
                if (IsServer)
                {
                    Debug.Log("[StartNetwork] ✓ UseNativeSockets=true（主机：Steam P2P + UDP 9050 双模）");
                }
                else
                {
                    Debug.Log("[StartNetwork] ✓ UseNativeSockets=true（客机：通过补丁重定向到 Steam P2P）");
                }
            }

            // 保险：确保必要组件存在（Loader.Init 一般已创建）
            if (SteamEndPointMapper.Instance == null)
                DontDestroyOnLoad(new GameObject("SteamEndPointMapper").AddComponent<SteamEndPointMapper>());
            if (SteamLobbyManager.Instance == null)
                DontDestroyOnLoad(new GameObject("SteamLobbyManager").AddComponent<SteamLobbyManager>());

            // 【可选】是否在这里创建 Lobby：建议不要，这会与 OnLobbyCreated 的二次 Start 冲突（见下文）
            if (!keepSteamLobby && IsServer && SteamLobbyManager.Instance != null && !SteamLobbyManager.Instance.IsInLobby)
            {
                SteamLobbyManager.Instance.CreateLobby(LobbyOptions);
            }
        }
        else
        {
            // 直连模式：使用原生 UDP
            if (netManager != null)
            {
                netManager.UseNativeSockets = true;
                if (wantsP2P)
                {
                    Debug.LogWarning("[StartNetwork] Steam P2P 不可用，回退 UDP（UseNativeSockets=true）");
                }
                else
                {
                    Debug.Log("[StartNetwork] 使用直连模式（UseNativeSockets=true）");
                }
            }
        }

        // 初始化统一聊天传输层
        EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.InitializeTransport(IsServer, p2pAvailable);



    }

    public void StopNetwork(bool leaveSteamLobby = true)
    {
        if (netManager != null && netManager.IsRunning)
        {
            netManager.Stop();
            Debug.Log(CoopLocalization.Get("net.networkStopped"));
        }

        IsServer = false;
        networkStarted = false;
        connectedPeer = null;

        if (leaveSteamLobby && TransportMode == NetworkTransportMode.SteamP2P && SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby)
        {
            SteamLobbyManager.Instance.LeaveLobby();
        }

        playerStatuses.Clear();
        clientPlayerStatuses.Clear();

        localPlayerStatus = null;

        foreach (var kvp in remoteCharacters)
            if (kvp.Value != null)
                Destroy(kvp.Value);
        remoteCharacters.Clear();

        foreach (var kvp in clientRemoteCharacters)
            if (kvp.Value != null)
                Destroy(kvp.Value);
        clientRemoteCharacters.Clear();

        ItemAgent_Gun.OnMainCharacterShootEvent -= COOPManager.WeaponHandle.Host_OnMainCharacterShoot;
    }

    public void ConnectToHost(string ip, int port)
    {
        // 标记为手动连接（从UI调用）
        isManualConnection = true;
        
        // 基础校验
        if (string.IsNullOrWhiteSpace(ip))
        {
            status = CoopLocalization.Get("net.ipEmpty");
            isConnecting = false;
            return;
        }

        if (port <= 0 || port > 65535)
        {
            status = CoopLocalization.Get("net.invalidPort");
            isConnecting = false;
            return;
        }

        if (IsServer)
        {
            Debug.LogWarning(CoopLocalization.Get("net.serverModeCannotConnect"));
            return;
        }

        if (isConnecting)
        {
            Debug.LogWarning(CoopLocalization.Get("net.alreadyConnecting"));
            return;
        }

        //如未启动或仍在主机模式，则切到"客户端网络"
        if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
            try
            {
                // 启动客户端网络，保持 Steam Lobby 连接（如果存在）
                StartNetwork(isServer: false, keepSteamLobby: true);
            }
            catch (Exception e)
            {
                Debug.LogError(CoopLocalization.Get("net.clientNetworkStartFailed", e));
                status = CoopLocalization.Get("net.clientNetworkStartFailedStatus");
                isConnecting = false;
                isManualConnection = false; // 重置手动连接标记
                return;
            }

        // 二次确认
        if (netManager == null || !netManager.IsRunning)
        {
            status = CoopLocalization.Get("net.clientNotStarted");
            isConnecting = false;
            isManualConnection = false; // 重置手动连接标记
            return;
        }

        try
        {
            status = CoopLocalization.Get("net.connectingTo", ip, port);
            isConnecting = true;

            // 若已有连接，先断开（以免残留状态）
            try
            {
                connectedPeer?.Disconnect();
            }
            catch
            {
            }

            connectedPeer = null;

            if (writer == null) writer = new NetDataWriter();

            writer.Reset();
            writer.Put("gameKey");
            netManager.Connect(ip, port, writer);
        }
        catch (Exception ex)
        {
            Debug.LogError(CoopLocalization.Get("net.connectionFailedLog", ex));
            status = CoopLocalization.Get("net.connectionFailed");
            isConnecting = false;
            connectedPeer = null;
            isManualConnection = false; // 重置手动连接标记
        }
    }


    public bool IsSelfId(string id)
    {
        var mine = localPlayerStatus?.EndPoint;
        return !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(mine) && id == mine;
    }

    public string GetPlayerId(NetPeer peer)
    {
        if (peer == null)
        {
            if (localPlayerStatus != null && !string.IsNullOrEmpty(localPlayerStatus.EndPoint))
                return localPlayerStatus.EndPoint; // 例如 "Host:9050"
            return $"Host:{port}";
        }

        if (playerStatuses != null && playerStatuses.TryGetValue(peer, out var st) &&
            !string.IsNullOrEmpty(st.EndPoint))
            return st.EndPoint;
        return peer.EndPoint.ToString();
    }

    /// <summary>
    /// 清除缓存的连接信息
    /// </summary>
    public void ClearConnectionCache()
    {
        hasSuccessfulConnection = false;
        cachedConnectedIP = "";
        cachedConnectedPort = 0;
        Debug.Log("[COOP] 手动清除缓存的连接信息");
    }

    /// <summary>
    /// 自动重连方法，不会更新缓存的连接信息
    /// </summary>
    private void AutoReconnectToHost(string ip, int port)
    {
        // 不标记为手动连接，这样连接成功后不会更新缓存
        isManualConnection = false;
        
        // 基础校验
        if (string.IsNullOrWhiteSpace(ip))
        {
            Debug.LogWarning("[COOP] 自动重连失败：IP为空");
            return;
        }

        if (port <= 0 || port > 65535)
        {
            Debug.LogWarning("[COOP] 自动重连失败：端口无效");
            return;
        }

        if (IsServer)
        {
            Debug.LogWarning("[COOP] 服务器模式无法自动重连");
            return;
        }

        if (isConnecting)
        {
            Debug.LogWarning("[COOP] 正在连接中，跳过自动重连");
            return;
        }

        //如未启动或仍在主机模式，则切到"客户端网络"
        if (netManager == null || !netManager.IsRunning || IsServer || !networkStarted)
            try
            {
                // 启动客户端网络，保持 Steam Lobby 连接（如果存在）
                StartNetwork(isServer: false, keepSteamLobby: true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[COOP] 自动重连启动客户端网络失败: {e}");
                return;
            }

        // 二次确认
        if (netManager == null || !netManager.IsRunning)
        {
            Debug.LogWarning("[COOP] 自动重连失败：客户端网络未启动");
            return;
        }

        try
        {
            Debug.Log($"[COOP] 开始自动重连到: {ip}:{port}");
            isConnecting = true;

            // 若已有连接，先断开（以免残留状态）
            try
            {
                connectedPeer?.Disconnect();
            }
            catch
            {
            }

            connectedPeer = null;

            if (writer == null) writer = new NetDataWriter();

            writer.Reset();
            writer.Put("gameKey");
            netManager.Connect(ip, port, writer);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[COOP] 自动重连失败: {ex}");
            isConnecting = false;
            connectedPeer = null;
        }
    }

    /// <summary>
    /// 场景加载完成后重新连接到缓存的主机，用于解决切换场景后看不到其他玩家的问题
    /// </summary>
    public async UniTask ReconnectAfterSceneLoad()
    {
        Debug.Log($"[COOP] ReconnectAfterSceneLoad 被调用 - IsServer: {IsServer}, hasSuccessfulConnection: {hasSuccessfulConnection}");
        
        // 只有客户端且有缓存的连接信息才执行重连
        if (IsServer)
        {
            Debug.Log("[COOP] 服务器模式，跳过重连");
            return;
        }

        if (!hasSuccessfulConnection)
        {
            Debug.Log("[COOP] 没有成功连接的缓存，跳过重连");
            return;
        }

        if (string.IsNullOrEmpty(cachedConnectedIP) || cachedConnectedPort <= 0)
        {
            Debug.Log($"[COOP] 缓存的连接信息无效 - IP: '{cachedConnectedIP}', Port: {cachedConnectedPort}");
            return;
        }

        // 防抖机制：检查是否在冷却时间内
        float currentTime = Time.realtimeSinceStartup;
        if (currentTime - lastReconnectTime < RECONNECT_COOLDOWN)
        {
            float remainingTime = RECONNECT_COOLDOWN - (currentTime - lastReconnectTime);
            Debug.Log($"[COOP] 重连冷却中，剩余 {remainingTime:F1} 秒");
            return;
        }

        lastReconnectTime = currentTime;

        Debug.Log($"[COOP] 检查当前连接状态 - connectedPeer: {connectedPeer != null}");

        // 强制重连，不跳过任何情况，确保场景切换后的完全同步
        if (connectedPeer != null && 
            connectedPeer.EndPoint.Address.ToString() == cachedConnectedIP && 
            connectedPeer.EndPoint.Port == cachedConnectedPort)
        {
            Debug.Log($"[COOP] 检测到已连接到目标主机 {cachedConnectedIP}:{cachedConnectedPort}，但仍然执行重连以确保同步");
            
            // 先断开当前连接
            try
            {
                Debug.Log("[COOP] 断开当前连接以准备重连");
                connectedPeer.Disconnect();
                connectedPeer = null;
                await UniTask.Delay(500); // 等待断开完成
            }
            catch (Exception ex)
            {
                Debug.LogError($"[COOP] 断开连接异常: {ex}");
            }
        }

        Debug.Log($"[COOP] 场景加载完成，开始重连到缓存的主机: {cachedConnectedIP}:{cachedConnectedPort}");

        // 等待一小段时间确保场景完全加载
        await UniTask.Delay(1000);

        try
        {
            // 执行自动重连（不会更新缓存）
            Debug.Log($"[COOP] 调用 AutoReconnectToHost({cachedConnectedIP}, {cachedConnectedPort})");
            AutoReconnectToHost(cachedConnectedIP, cachedConnectedPort);
            
            // 等待连接结果
            var timeout = Time.realtimeSinceStartup + 15f; // 15秒超时
            var startTime = Time.realtimeSinceStartup;
            
            while (isConnecting && Time.realtimeSinceStartup < timeout)
            {
                await UniTask.Delay(100);
                
                // 每秒输出一次等待状态
                if ((int)(Time.realtimeSinceStartup - startTime) % 1 == 0)
                {
                    Debug.Log($"[COOP] 等待连接中... 已等待 {(int)(Time.realtimeSinceStartup - startTime)} 秒");
                }
            }

            if (connectedPeer != null)
            {
                Debug.Log($"[COOP] 场景切换后重连成功: {cachedConnectedIP}:{cachedConnectedPort}");
                
                // 重连成功后，发送当前状态进行完全同步
                await UniTask.Delay(1000); // 等待连接稳定
                
                try
                {
                    if (Send_ClientStatus.Instance != null)
                    {
                        Debug.Log("[COOP] 重连成功，发送客户端状态更新");
                        Send_ClientStatus.Instance.SendClientStatusUpdate();
                    }
                    
                    // 额外发送场景就绪信息
                    if (SceneNet.Instance != null)
                    {
                        Debug.Log("[COOP] 重连成功，发送场景就绪信息");
                        SceneNet.Instance.TrySendSceneReadyOnce();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[COOP] 重连后发送状态更新异常: {ex}");
                }
            }
            else
            {
                Debug.LogWarning($"[COOP] 场景切换后重连失败: {cachedConnectedIP}:{cachedConnectedPort}");
                Debug.LogWarning($"[COOP] isConnecting: {isConnecting}, 超时: {Time.realtimeSinceStartup >= timeout}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[COOP] 场景切换后重连异常: {ex}");
        }
    }

    public void SetTransportMode(NetworkTransportMode mode)
    {
        Debug.Log($"[NetService] SetTransportMode 被调用: 当前={TransportMode}, 新模式={mode}, networkStarted={networkStarted}");
        
        if (TransportMode == mode)
        {
            Debug.Log($"[NetService] 传输模式未改变，跳过");
            return;
        }

        TransportMode = mode;
        Debug.Log($"[NetService] ✓ TransportMode 已设置为: {TransportMode}");

        if (SteamP2PLoader.Instance != null)
        {
            SteamP2PLoader.Instance.UseSteamP2P = mode == NetworkTransportMode.SteamP2P;
            Debug.Log($"[NetService] ✓ UseSteamP2P 已设置为: {SteamP2PLoader.Instance.UseSteamP2P}");
        }

        if (mode != NetworkTransportMode.SteamP2P && SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.LeaveLobby();
            Debug.Log($"[NetService] ✓ 已离开 Steam Lobby（切换到直连模式）");
        }

        if (networkStarted)
        {
            Debug.Log($"[NetService] 网络已启动，停止网络以应用新模式");
            // 保持 Steam Lobby 连接，只停止网络服务
            StopNetwork(leaveSteamLobby: false);
        }
        
        Debug.Log($"[NetService] SetTransportMode 完成: TransportMode={TransportMode}");
    }

    public void ConfigureLobbyOptions(SteamLobbyOptions? options)
    {
        LobbyOptions = options ?? SteamLobbyOptions.CreateDefault();

        if (SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.UpdateLobbySettings(LobbyOptions);
        }
    }

    /// <summary>
    /// 【调试】主机发送欢迎消息
    /// </summary>
    /// <param name="clientPeer">客机的 NetPeer</param>
    private void SendHostWelcomeMessage(NetPeer clientPeer)
    {
        try
        {
            Debug.Log($"[CHAT-DEBUG] 主机准备发送欢迎消息给客机: {clientPeer.EndPoint}");

            // 延迟2秒发送，确保客机已完全连接
            StartCoroutine(SendHostWelcomeMessageDelayed(clientPeer));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CHAT-DEBUG] 主机发送欢迎消息时发生异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 【调试】延迟发送主机欢迎消息
    /// </summary>
    private System.Collections.IEnumerator SendHostWelcomeMessageDelayed(NetPeer clientPeer)
    {
        yield return new WaitForSeconds(2f);

        Debug.Log("[CHAT-DEBUG] 主机开始发送欢迎消息");

        // 获取当前用户信息
        var steamUserService = new EscapeFromDuckovCoopMod.Chat.Services.SteamUserService();
        var userInfoTask = steamUserService.GetCurrentUserInfo();
        
        // 等待用户信息获取完成
        while (!userInfoTask.IsCompleted)
        {
            yield return null;
        }

        var userInfo = userInfoTask.Result;
        if (userInfo == null)
        {
            Debug.LogWarning("[CHAT-DEBUG] 主机无法获取用户信息，使用默认信息");
            userInfo = new EscapeFromDuckovCoopMod.Chat.Models.UserInfo
            {
                UserName = "主机",
                DisplayName = "主机"
            };
        }

        // 创建欢迎消息
        var welcomeMessage = new EscapeFromDuckovCoopMod.Chat.Models.ChatMessage
        {
            Content = $"【P2P测试】欢迎 {clientPeer.EndPoint} 加入游戏！聊天系统已就绪。",
            Sender = userInfo,
            Type = EscapeFromDuckovCoopMod.Chat.Models.MessageType.System,
            Timestamp = System.DateTime.UtcNow
        };

        // 序列化消息
        string messageJson = Newtonsoft.Json.JsonConvert.SerializeObject(welcomeMessage);
        Debug.Log($"[CHAT-DEBUG] 主机欢迎消息 JSON: {messageJson}");

        // 通过桥接器广播消息（支持 Steam P2P 和直连 UDP）
        bool success = EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.SendChatMessage(messageJson);
        Debug.Log($"[CHAT-DEBUG] 主机欢迎消息发送结果: {success}");
        Debug.Log($"[CHAT-DEBUG] 传输状态: {EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.GetTransportStatus()}");
        
        if (!success)
        {
            Debug.LogWarning("[CHAT-DEBUG] 主机欢迎消息发送失败，请检查网络连接");
        }
    }

    /// <summary>
    /// 注册主机的 Steam ID 映射
    /// </summary>
    /// <param name="peer">主机的 NetPeer</param>
    private void RegisterHostSteamId(NetPeer peer)
    {
        try
        {
            if (peer == null)
            {
                Debug.LogWarning("[CHAT-DEBUG] 无法注册主机 SteamID：peer 为 null");
                return;
            }

            Debug.Log($"[CHAT-DEBUG] 尝试注册主机 SteamID，端点: {peer.EndPoint}");

            // 尝试从 SteamEndPointMapper 获取主机的 SteamID
            if (SteamEndPointMapper.Instance != null)
            {
                var hostEndpoint = peer.EndPoint;
                Steamworks.CSteamID hostSteamId;
                
                if (SteamEndPointMapper.Instance.TryGetSteamID(hostEndpoint, out hostSteamId))
                {
                    Debug.Log($"[CHAT-DEBUG] 找到主机 SteamID: {hostSteamId}，注册到聊天传输层");
                    EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.RegisterClientSteamId(hostEndpoint.ToString(), hostSteamId);
                }
                else
                {
                    Debug.LogWarning($"[CHAT-DEBUG] 无法获取主机的 SteamID，端点: {hostEndpoint}");
                }
            }
            else
            {
                Debug.LogWarning("[CHAT-DEBUG] SteamEndPointMapper 未初始化");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CHAT-DEBUG] 注册主机 SteamID 时发生异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 【调试】发送测试聊天消息
    /// </summary>
    private void SendDebugChatMessage()
    {
        try
        {
            Debug.Log("[CHAT-DEBUG] 客机连接成功，准备发送测试消息");

            // 延迟1秒发送，确保网络已完全建立
            StartCoroutine(SendDebugChatMessageDelayed());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CHAT-DEBUG] 发送测试消息时发生异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 【调试】延迟发送测试聊天消息
    /// </summary>
    private System.Collections.IEnumerator SendDebugChatMessageDelayed()
    {
        yield return new WaitForSeconds(1f);

        Debug.Log("[CHAT-DEBUG] 开始发送测试消息");

        // 获取当前用户信息
        var steamUserService = new EscapeFromDuckovCoopMod.Chat.Services.SteamUserService();
        var userInfoTask = steamUserService.GetCurrentUserInfo();
        
        // 等待用户信息获取完成
        while (!userInfoTask.IsCompleted)
        {
            yield return null;
        }

        var userInfo = userInfoTask.Result;
        if (userInfo == null)
        {
            Debug.LogWarning("[CHAT-DEBUG] 无法获取用户信息，使用默认信息");
            userInfo = new EscapeFromDuckovCoopMod.Chat.Models.UserInfo
            {
                UserName = "测试客机",
                DisplayName = "测试客机"
            };
        }

        // 创建测试消息
        var testMessage = new EscapeFromDuckovCoopMod.Chat.Models.ChatMessage
        {
            Content = "【P2P测试】客机已连接，聊天系统测试中...",
            Sender = userInfo,
            Type = EscapeFromDuckovCoopMod.Chat.Models.MessageType.Normal,
            Timestamp = System.DateTime.UtcNow
        };

        // 序列化消息
        string messageJson = Newtonsoft.Json.JsonConvert.SerializeObject(testMessage);
        Debug.Log($"[CHAT-DEBUG] 测试消息 JSON: {messageJson}");

        // 优先通过统一传输层发送（支持 Steam P2P 和直连 UDP）
        bool transportSuccess = EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.SendChatMessage(messageJson);
        Debug.Log($"[CHAT-DEBUG] 统一传输层发送结果: {transportSuccess}");
        Debug.Log($"[CHAT-DEBUG] 传输状态: {EscapeFromDuckovCoopMod.Chat.Network.ChatTransportBridge.GetTransportStatus()}");

        // 如果统一传输层失败，回退到直接 UDP 发送
        if (!transportSuccess && connectedPeer != null && connectedPeer.ConnectionState == ConnectionState.Connected)
        {
            Debug.LogWarning("[CHAT-DEBUG] 统一传输层失败，回退到直接 UDP 发送");
            var writer = new NetDataWriter();
            writer.Put((byte)Op.CHAT_MESSAGE_SEND);
            writer.Put(messageJson);

            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            Debug.Log($"[CHAT-DEBUG] 测试消息已通过直接 UDP 发送到主机: {connectedPeer.EndPoint}");
        }
        else if (!transportSuccess)
        {
            Debug.LogWarning("[CHAT-DEBUG] 无法发送测试消息：未连接到主机且统一传输层失败");
        }
    }
}