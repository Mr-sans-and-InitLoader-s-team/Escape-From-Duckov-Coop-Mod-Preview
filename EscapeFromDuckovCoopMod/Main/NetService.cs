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

using System.IO;
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
    public string manualIP = "r.pansx.net";
    public string manualPort = "9050"; // GTX 5090 我也想要
    public bool networkStarted;
    public float broadcastTimer;
    public float broadcastInterval = 5f;
    public float syncTimer;
    public float syncInterval = 0.015f; // =========== Mod开发者注意现在是TI版本也就是满血版无同步延迟，0.03 ~33ms ===================

    public readonly HashSet<int> _dedupeShotFrame = new(); // 本帧已发过的标记

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

    // 连接缓存相关字段
    private bool isManualConnection = false;
    private string cachedConnectedIP = "";
    private int cachedConnectedPort = 0;
    private bool hasSuccessfulConnection = false;
    private float lastReconnectTime = 0f;
    private const float RECONNECT_COOLDOWN = 5f;

    public void OnEnable()
    {
        Instance = this;
        if (SteamP2PLoader.Instance != null)
        {
            SteamP2PLoader.Instance.UseSteamP2P = TransportMode == NetworkTransportMode.SteamP2P;
        }
    }

    public void SetTransportMode(NetworkTransportMode mode)
    {
        if (TransportMode == mode)
            return;

        TransportMode = mode;

        if (SteamP2PLoader.Instance != null)
        {
            SteamP2PLoader.Instance.UseSteamP2P = mode == NetworkTransportMode.SteamP2P;
        }

        if (mode != NetworkTransportMode.SteamP2P && SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.LeaveLobby();
        }

        if (networkStarted)
        {
            StopNetwork();
        }
    }

    public void ConfigureLobbyOptions(SteamLobbyOptions? options)
    {
        LobbyOptions = options ?? SteamLobbyOptions.CreateDefault();

        if (SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.UpdateLobbySettings(LobbyOptions);
        }
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
            
            // 客户端连接成功时清除战利品缓存，确保完全同步
            ClearClientLootCache();
            Send_ClientStatus.Instance.SendClientStatusUpdate();
            
            // 延迟一点时间后强制重新同步所有战利品箱
            UniTask.Void(async () =>
            {
                await UniTask.Delay(2000); // 等待连接完全稳定
                if (LootManager.Instance != null && connectedPeer != null)
                {
                    Debug.Log("[COOP] 连接成功，开始强制重新同步所有战利品箱");
                    LootManager.Instance.Client_ForceResyncAllLootboxes();
                }
            });
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
            
            // 同步当前场景的所有墓碑给新连接的客户端
            SyncTombstonesToNewClient(peer);
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

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        ModBehaviourF.Instance.OnNetworkReceive(peer, reader, channelNumber, deliveryMethod);
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        var msg = reader.GetString();

        if (IsServer && msg == "DISCOVER_REQUEST")
        {
            writer.Reset();
            writer.Put("DISCOVER_RESPONSE");
            netManager.SendUnconnectedMessage(writer, remoteEndPoint);
        }
        else if (!IsServer && msg == "DISCOVER_RESPONSE")
        {
            var hostInfo = remoteEndPoint.Address + ":" + port;
            if (!hostSet.Contains(hostInfo))
            {
                hostSet.Add(hostInfo);
                hostList.Add(hostInfo);
                Debug.Log(CoopLocalization.Get("net.hostDiscovered", hostInfo));
            }
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

        LoaclPlayerManager.Instance.InitializeLocalPlayer();
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
            Debug.Log("[StartNetwork] 联机Mod已启动，初始化Steam P2P组件"); // ← 现在会正常打印

            if (netManager != null)
            {
                // 使用 Steam P2P 时让 LiteNetLib 不去占 UDP socket
                netManager.UseNativeSockets = false;
                Debug.Log("[StartNetwork] ✓ UseNativeSockets=false（P2P 模式）");
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
            // 回退到纯 UDP
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
                StartNetwork(false); // 启动/切换到客户端模式
            }
            catch (Exception e)
            {
                Debug.LogError(CoopLocalization.Get("net.clientNetworkStartFailed", e));
                status = CoopLocalization.Get("net.clientNetworkStartFailedStatus");
                isConnecting = false;
                return;
            }

        // 二次确认
        if (netManager == null || !netManager.IsRunning)
        {
            status = CoopLocalization.Get("net.clientNotStarted");
            isConnecting = false;
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

        if (playerStatuses != null && playerStatuses.TryGetValue(peer, out var st) && !string.IsNullOrEmpty(st.EndPoint))
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
    /// 清除客户端战利品箱缓存，强制重新同步所有战利品箱
    /// </summary>
    private void ClearClientLootCache()
    {
        if (IsServer)
        {
            Debug.Log("[COOP] 服务器模式，跳过清除战利品缓存");
            return;
        }

        try
        {
            if (LootManager.Instance != null)
            {
                var clearedCount = LootManager.Instance._cliLootByUid.Count;
                LootManager.Instance._cliLootByUid.Clear();
                LootManager.Instance._pendingLootStatesByUid.Clear();
                Debug.Log($"[COOP] 已清除客户端战利品缓存，共清除 {clearedCount} 个战利品箱");
            }

            if (COOPManager.LootNet != null)
            {
                var pendingCount = COOPManager.LootNet._cliPendingPut.Count;
                COOPManager.LootNet._cliPendingPut.Clear();
                COOPManager.LootNet._cliSwapByVictim.Clear();
                Debug.Log($"[COOP] 已清除客户端待处理的战利品操作，共清除 {pendingCount} 个待处理操作");
            }

            if (LootManager.Instance != null)
            {
                var takeCount = LootManager.Instance._cliPendingTake.Count;
                var reorderCount = LootManager.Instance._cliPendingReorder.Count;
                LootManager.Instance._cliPendingTake.Clear();
                LootManager.Instance._cliPendingReorder.Clear();
                Debug.Log($"[COOP] 已清除客户端待处理的拾取和重排操作，拾取: {takeCount}, 重排: {reorderCount}");
            }

            Debug.Log("[COOP] 客户端战利品缓存清除完成，所有战利品箱将重新从服务端同步");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[COOP] 清除客户端战利品缓存时发生异常: {ex}");
        }
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
                StartNetwork(false); // 启动/切换到客户端模式
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
                
                // 重连成功后，清除客户端战利品箱缓存，强制重新同步
                ClearClientLootCache();
                
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
                    
                    // 强制重新同步所有战利品箱
                    await UniTask.Delay(500); // 再等待一点时间确保场景就绪
                    if (LootManager.Instance != null)
                    {
                        Debug.Log("[COOP] 重连成功，开始强制重新同步所有战利品箱");
                        LootManager.Instance.Client_ForceResyncAllLootboxes();
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

    /// <summary>
    /// 同步当前场景的所有墓碑给新连接的客户端
    /// </summary>
    private void SyncTombstonesToNewClient(NetPeer peer)
    {
        if (!IsServer || peer == null || LootManager.Instance == null)
        {
            return;
        }

        try
        {
            Debug.Log($"[TOMBSTONE] 开始同步墓碑给新客户端: {peer.EndPoint}");
            
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            var syncCount = 0;
            
            // 遍历所有服务端的墓碑，发送给新客户端
            foreach (var kv in LootManager.Instance._srvLootByUid)
            {
                var lootUid = kv.Key;
                var inventory = kv.Value;
                
                if (inventory == null)
                {
                    continue;
                }
                
                // 尝试获取墓碑的位置信息
                Vector3 position = Vector3.zero;
                Quaternion rotation = Quaternion.identity;
                
                if (LootManager.Instance.TryGetLootboxWorldPos(inventory, out position))
                {
                    // 从墓碑持久化系统获取更准确的位置和旋转信息
                    if (TombstonePersistence.Instance != null)
                    {
                        // 尝试从所有用户的墓碑数据中找到匹配的墓碑
                        var tombstoneFound = false;
                        var tombstoneDir = Path.Combine(Application.streamingAssetsPath, "TombstoneData");
                        
                        if (Directory.Exists(tombstoneDir))
                        {
                            var tombstoneFiles = Directory.GetFiles(tombstoneDir, "*_tombstones.json");
                            foreach (var filePath in tombstoneFiles)
                            {
                                try
                                {
                                    var fileName = Path.GetFileName(filePath);
                                    var userId = TombstonePersistence.Instance.DecodeUserIdFromFileName(fileName);
                                    var tombstone = TombstonePersistence.Instance.GetTombstone(userId, lootUid);
                                    
                                    if (tombstone != null)
                                    {
                                        position = tombstone.position;
                                        rotation = tombstone.rotation;
                                        tombstoneFound = true;
                                        break;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Debug.LogWarning($"[TOMBSTONE] 获取墓碑数据失败: {e.Message}");
                                }
                            }
                        }
                        
                        if (!tombstoneFound)
                        {
                            Debug.LogWarning($"[TOMBSTONE] 未找到lootUid={lootUid}的墓碑数据，使用默认旋转");
                        }
                    }
                    
                    // 发送DEAD_LOOT_SPAWN消息给新客户端
                    var writer = new NetDataWriter();
                    writer.Put((byte)Op.DEAD_LOOT_SPAWN);
                    writer.Put(currentScene);
                    writer.Put(0); // aiId，对于恢复的墓碑设为0
                    writer.Put(lootUid);
                    writer.PutV3cm(position);
                    writer.PutQuaternion(rotation);
                    
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);
                    syncCount++;
                    
                    Debug.Log($"[TOMBSTONE] 已同步墓碑给客户端: lootUid={lootUid}, pos={position}");
                }
                else
                {
                    Debug.LogWarning($"[TOMBSTONE] 无法获取lootUid={lootUid}的位置信息，跳过同步");
                }
            }
            
            Debug.Log($"[TOMBSTONE] 完成墓碑同步，共同步 {syncCount} 个墓碑给客户端: {peer.EndPoint}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] 同步墓碑给新客户端失败: {e}");
        }
    }

    /// <summary>
    /// 客户端死亡时发送剩余物品信息给服务端（用于从墓碑中减去）
    /// </summary>
    public void SendPlayerDeathEquipment(string userId, int lootUid)
    {
        if (IsServer || connectedPeer == null)
        {
            return;
        }

        try
        {
            var remainingItemTypeIds = GetPlayerEquipmentTypeIds();
            
            Debug.Log($"[TOMBSTONE] Sending player remaining items: userId={userId}, lootUid={lootUid}, remaining items count={remainingItemTypeIds.Count}");
            
            writer.Reset();
            writer.Put((byte)Op.PLAYER_DEATH_EQUIPMENT);
            writer.Put(userId);
            writer.Put(lootUid);
            writer.Put(remainingItemTypeIds.Count);
            
            foreach (var typeId in remainingItemTypeIds)
            {
                writer.Put(typeId);
                Debug.Log($"[TOMBSTONE] Reporting remaining item: TypeID={typeId}");
            }
            
            connectedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            Debug.Log($"[TOMBSTONE] Sent player remaining items report to server");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Failed to send player remaining items: {e}");
        }
    }

    /// <summary>
    /// 获取玩家身上所有剩余物品的TypeID列表（用于从墓碑中减去）
    /// </summary>
    private List<int> GetPlayerEquipmentTypeIds()
    {
        var remainingItemTypeIds = new List<int>();
        
        try
        {
            var mainControl = CharacterMainControl.Main;
            if (mainControl == null)
            {
                Debug.LogWarning("[TOMBSTONE] Main character control not found");
                return remainingItemTypeIds;
            }

            Debug.Log("[TOMBSTONE] Collecting all remaining items on player...");

            // 获取远程武器
            var rangedWeapon = mainControl.GetGun();
            if (rangedWeapon != null && rangedWeapon.Item != null)
            {
                remainingItemTypeIds.Add(rangedWeapon.Item.TypeID);
                Debug.Log($"[TOMBSTONE] Found ranged weapon: TypeID={rangedWeapon.Item.TypeID}");
            }

            // 获取近战武器
            var meleeWeapon = mainControl.GetMeleeWeapon();
            if (meleeWeapon != null && meleeWeapon.Item != null)
            {
                remainingItemTypeIds.Add(meleeWeapon.Item.TypeID);
                Debug.Log($"[TOMBSTONE] Found melee weapon: TypeID={meleeWeapon.Item.TypeID}");
            }

            // 获取角色身上的所有装备槽位（包括图腾等）
            var characterItem = mainControl.CharacterItem;
            if (characterItem != null && characterItem.Slots != null)
            {
                Debug.Log($"[TOMBSTONE] Checking character equipment slots");
                
                foreach (var slot in characterItem.Slots)
                {
                    if (slot != null && slot.Content != null)
                    {
                        remainingItemTypeIds.Add(slot.Content.TypeID);
                        Debug.Log($"[TOMBSTONE] Found equipped item in slot '{slot.Key}': TypeID={slot.Content.TypeID}, Name={slot.Content.name}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[TOMBSTONE] Character equipment slots not found");
            }

            // 获取背包中的所有物品
            var playerInventory = PlayerStorage.Inventory;
            if (playerInventory != null)
            {
                Debug.Log($"[TOMBSTONE] Checking player inventory with {playerInventory.Content.Count} slots");
                
                for (int i = 0; i < playerInventory.Content.Count; i++)
                {
                    var item = playerInventory.GetItemAt(i);
                    if (item != null)
                    {
                        remainingItemTypeIds.Add(item.TypeID);
                        Debug.Log($"[TOMBSTONE] Found inventory item {i}: TypeID={item.TypeID}, Name={item.name}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[TOMBSTONE] Player inventory not found");
            }

            // 获取宠物背包中的物品 - 但不包含在剩余物品中，因为宠物背包物品不会掉落
            // 注意：宠物背包物品不添加到remainingItemTypeIds，因为它们不会掉落也不应该从墓碑中减去

            Debug.Log($"[TOMBSTONE] Total remaining items found: {remainingItemTypeIds.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TOMBSTONE] Error getting player remaining items: {e}");
        }

        return remainingItemTypeIds;
    }
}