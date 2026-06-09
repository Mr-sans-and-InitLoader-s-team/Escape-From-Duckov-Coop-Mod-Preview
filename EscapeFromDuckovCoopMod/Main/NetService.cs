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

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Steamworks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public enum NetworkTransportMode
{
    Direct,
    SteamP2P
}

public class NetService : MonoBehaviour, INetEventListener, IModNetworkService
{
    public static NetService Instance;
    public int port = 9050;
    public List<string> hostList = new();
    public bool isConnecting;
    public string status = "";
    public string manualIP = "127.0.0.1";
    public string manualPort = "9050";
    public bool networkStarted;
    private string _selfNetworkId;
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
    public bool NetworkStarted => networkStarted;
    public bool IsActuallyRunning => networkStarted && netManager != null && netManager.IsRunning;
    public NetworkTransportMode TransportMode { get; private set; } = NetworkTransportMode.Direct;
    public SteamLobbyOptions LobbyOptions { get; private set; } = SteamLobbyOptions.CreateDefault();
    private readonly Dictionary<string, float> _playerInvincibleUntil = new(StringComparer.OrdinalIgnoreCase);

    public void OnEnable()
    {
        Instance = this;
        ModNetworkApi.SetBackend(new NetServiceModNetworkBackend(this));
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

    public string ResolveLocalPlayerName()
    {
        if (SteamManager.Initialized)
        {
            try
            {
                var persona = SteamFriends.GetPersonaName();
                if (!string.IsNullOrEmpty(persona))
                {
                    return persona;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetService] Failed to query Steam persona name: {ex}");
            }
        }

        return IsServer ? "Host" : "Client";
    }

    public string ResolvePeerDisplayName(NetPeer peer, string fallback)
    {
        if (peer != null && SteamManager.Initialized && SteamEndPointMapper.Instance != null)
        {
            if (SteamEndPointMapper.Instance.TryGetSteamID(peer.EndPoint, out CSteamID steamId))
            {
                try
                {
                    var persona = SteamFriends.GetFriendPersonaName(steamId);
                    if (!string.IsNullOrEmpty(persona))
                    {
                        return persona;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NetService] Failed to resolve peer Steam persona: {ex}");
                }
            }
        }

        return fallback;
    }

    public void OnPeerConnected(NetPeer peer)
    {
        Debug.Log(CoopLocalization.Get("net.connectionSuccess", peer.EndPoint.ToString()));
        Debug.Log($"[NET_STATE] peer connected: {peer.EndPoint}, server={IsServer}, transport={TransportMode}");
        connectedPeer = peer;

        if (!IsServer)
        {
            status = CoopLocalization.Get("net.connectedTo", peer.EndPoint.ToString());
            isConnecting = false;
            Send_ClientStatus.Instance.SendClientStatusUpdate();
        }

        if (!playerStatuses.ContainsKey(peer))
        {
            playerStatuses[peer] = new PlayerStatus
            {
                EndPoint = peer.EndPoint.ToString(),
                PlayerName = ResolvePeerDisplayName(peer, IsServer ? $"Player_{peer.Id}" : "Host"),
                Latency = peer.Ping,
                IsInGame = false,
                LastIsInGame = false,
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                CustomFaceJson = null
            };
        }

        if (IsServer) SendLocalPlayerStatus.Instance.SendPlayerStatusUpdate();

        if (IsServer)
        {
            HealthM.Instance?.Server_SendAllSnapshotsTo(peer);
            COOPManager.FriendlyFire?.OnPeerConnected(peer);
            COOPManager.AI?.Server_SendSnapshotTo(peer, true);
        }

        ModNetworkApi.NotifyPeerConnected(peer);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Debug.Log(CoopLocalization.Get("net.disconnected", peer.EndPoint.ToString(), disconnectInfo.Reason.ToString()));
        Debug.Log($"[NET_STATE] peer disconnected: {peer.EndPoint}, reason={disconnectInfo.Reason}, server={IsServer}");
        if (!IsServer)
        {
            status = CoopLocalization.Get("net.connectionLost");
            isConnecting = false;

            foreach (var kvp in clientRemoteCharacters)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);
            }
            clientRemoteCharacters.Clear();
            clientPlayerStatuses.Clear();
            CoopTool._cliPendingRemoteHp.Clear();
            CustomFace._cliPendingFace.Clear();
            SceneNet.Instance?._cliLastSceneIdByPlayer.Clear();
        }

        if (connectedPeer == peer) connectedPeer = null;

        if (playerStatuses.ContainsKey(peer))
        {
            var _st = playerStatuses[peer];
            if (_st != null && !string.IsNullOrEmpty(_st.EndPoint))
            {
                _playerInvincibleUntil.Remove(_st.EndPoint);
                SceneNet.Instance?._cliLastSceneIdByPlayer.Remove(_st.EndPoint);
          
            }
            playerStatuses.Remove(peer);
        }

        if (remoteCharacters.ContainsKey(peer) && remoteCharacters[peer] != null)
        {
            Destroy(remoteCharacters[peer]);
            remoteCharacters.Remove(peer);
        }

        COOPManager.LootNet?.Server_RemoveViewer(peer);
        COOPManager.AI?.Server_OnPeerDisconnected(peer);
        SceneM._srvPeerScene.Remove(peer);
        ModNetworkApi.NotifyPeerDisconnected(peer);

        if (SteamP2PLoader.Instance == null || !SteamP2PLoader.Instance.UseSteamP2P || SteamP2PManager.Instance == null)
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
        if (!IsActuallyRunning || reader == null)
            return;

        var msg = reader.GetString();

        if (IsServer && msg == "DISCOVER_REQUEST")
        {
            if (writer == null || netManager == null)
                return;

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
            string clientVersion = null;
            bool hasValidKey = false;

            if (request.Data != null)
            {
                try
                {
                    var key = request.Data.GetString();
                    hasValidKey = key == "gameKey";

                    if (request.Data.AvailableBytes > 0)
                    {
                        clientVersion = request.Data.GetString();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NetService] 解析连接请求数据时出错: {ex}");
                }
            }

            if (!hasValidKey)
            {
                request.Reject();
                return;
            }

            if (string.IsNullOrEmpty(clientVersion))
            {
                status = CoopLocalization.Get("net.clientVersionUnknown");
                Debug.LogWarning(status);
                MModUI.ShowTip(status);
                request.Reject();
                return;
            }

            if (!string.Equals(clientVersion, BuildInfo.ModVersion, StringComparison.Ordinal))
            {
                status = CoopLocalization.Get("net.clientVersionMismatch", clientVersion, BuildInfo.ModVersion);
                Debug.LogWarning(status);
                MModUI.ShowTip(status);
                request.Reject();
                return;
            }

            request.Accept();
        }
        else
        {
            request.Reject();
        }
    }

    public void StartNetwork(bool isServer, bool keepSteamLobby = false)
    {
        TryStartNetwork(isServer, keepSteamLobby);
    }

    public bool TryStartNetwork(bool isServer, bool keepSteamLobby = false)
    {
        StopNetwork(!keepSteamLobby);
        IsServer = isServer;
        CoopTool.HideAllTargetObjects(isServer);
        NetDiagnostics.Instance.Reset();
        PerformanceDiagnostics.Instance.Reset();
        writer = new NetDataWriter();
        netManager = new NetManager(this)
        {
            BroadcastReceiveEnabled = true
        };

        var wantsP2P = TransportMode == NetworkTransportMode.SteamP2P;
        var p2pAvailable =
            wantsP2P &&
            SteamP2PLoader.Instance != null &&
            SteamManager.Initialized &&
            SteamP2PManager.Instance != null &&
            SteamP2PLoader.Instance.UseSteamP2P;

        if (wantsP2P && !p2pAvailable)
        {
            status = !SteamManager.Initialized
                ? "[STEAM_JOIN] Steam is not initialized"
                : "[STEAM_JOIN] Steam P2P is not available";
            Debug.LogError($"[NET_STATE] {status}. loader={SteamP2PLoader.Instance != null}, p2pManager={SteamP2PManager.Instance != null}, useP2P={SteamP2PLoader.Instance?.UseSteamP2P}");
            CleanupStoppedNetworkState();
            return false;
        }

        if (p2pAvailable)
        {
            if (SteamEndPointMapper.Instance == null)
                DontDestroyOnLoad(new GameObject("SteamEndPointMapper").AddComponent<SteamEndPointMapper>());
            if (SteamLobbyManager.Instance == null)
                DontDestroyOnLoad(new GameObject("SteamLobbyManager").AddComponent<SteamLobbyManager>());

            netManager.UseNativeSockets = false;
            netManager.UpdateTime = 1;
            Debug.Log("[NET_STATE] configured Steam P2P transport before NetManager.Start");
        }
        else
        {
            netManager.UseNativeSockets = true;
        }

        bool started;
        if (IsServer)
        {
            started = netManager.Start(port);
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
            started = netManager.Start();
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

        if (!started)
        {
            status = IsServer ? CoopLocalization.Get("net.serverStartFailed") : CoopLocalization.Get("net.clientStartFailed");
            Debug.LogError($"[NET_STATE] NetManager.Start failed. server={IsServer}, port={port}, transport={TransportMode}, p2pAvailable={p2pAvailable}");
            CleanupStoppedNetworkState();
            return false;
        }

        _selfNetworkId = ComputeSelfNetworkId();
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
        _playerInvincibleUntil.Clear();
        CoopSyncDatabase.AI.Clear();
        COOPManager.AI?.Reset();
        COOPManager.FriendlyFire?.OnNetworkStarted(IsServer);

        LocalPlayerManager.Instance.InitializeLocalPlayer();
        var main = CharacterMainControl.Main;
        if (main) ModApiEvents.RaisePlayerSpawned(main, GetSelfNetworkId(), true);
        if (IsServer)
        {
            ItemAgent_Gun.OnMainCharacterShootEvent -= COOPManager.WeaponHandle.Host_OnMainCharacterShoot;
            ItemAgent_Gun.OnMainCharacterShootEvent += COOPManager.WeaponHandle.Host_OnMainCharacterShoot;
        }


        Debug.Log($"[StartNetwork] WantsP2P={wantsP2P}, P2P可用={p2pAvailable}, UseSteamP2P={SteamP2PLoader.Instance?.UseSteamP2P}, " +
                  $"SteamInit={SteamManager.Initialized}, IsServer={IsServer}, NetRunning={netManager?.IsRunning}");

        if (p2pAvailable)
        {
            Debug.Log("[StartNetwork] 联机Mod已启动，初始化Steam P2P组件"); // ← 现在会正常打印

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



        Debug.Log($"[NET_STATE] network started: server={IsServer}, port={port}, localPort={netManager.LocalPort}, transport={TransportMode}");
        return true;
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
        isConnecting = false;
        connectedPeer = null;
        status = CoopLocalization.Get("net.networkStopped");

        if (leaveSteamLobby && SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.CancelPendingJoin("[STEAM_JOIN] network stopped");
            if (TransportMode == NetworkTransportMode.SteamP2P && SteamLobbyManager.Instance.IsInLobby)
                SteamLobbyManager.Instance.LeaveLobby();
        }

        if (leaveSteamLobby && SteamEndPointMapper.Instance != null)
        {
            SteamEndPointMapper.Instance.ClearAll();
        }

        playerStatuses.Clear();
        clientPlayerStatuses.Clear();
        hostList.Clear();
        hostSet.Clear();

        localPlayerStatus = null;
        _selfNetworkId = null;

        foreach (var kvp in remoteCharacters)
            if (kvp.Value != null)
                Destroy(kvp.Value);
        remoteCharacters.Clear();

        foreach (var kvp in clientRemoteCharacters)
            if (kvp.Value != null)
                Destroy(kvp.Value);
        clientRemoteCharacters.Clear();

        NetDiagnostics.Instance.Reset();
        PerformanceDiagnostics.Instance.Reset();

        ItemAgent_Gun.OnMainCharacterShootEvent -= COOPManager.WeaponHandle.Host_OnMainCharacterShoot;
        Debug.Log($"[NET_STATE] network stopped, leaveSteamLobby={leaveSteamLobby}");
    }

    private void CleanupStoppedNetworkState()
    {
        if (netManager != null && netManager.IsRunning)
            netManager.Stop();

        IsServer = false;
        networkStarted = false;
        isConnecting = false;
        connectedPeer = null;
        localPlayerStatus = null;
        _selfNetworkId = null;
        playerStatuses.Clear();
        remoteCharacters.Clear();
        clientPlayerStatuses.Clear();
        clientRemoteCharacters.Clear();
        _playerInvincibleUntil.Clear();
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
            status = CoopLocalization.Get("net.serverModeCannotConnect");
            isConnecting = false;
            return;
        }

        if (isConnecting)
        {
            Debug.LogWarning(CoopLocalization.Get("net.alreadyConnecting"));
            return;
        }

        //如未启动或仍在主机模式，则切到"客户端网络"
        if (!IsActuallyRunning || IsServer)
            try
            {
                if (!TryStartNetwork(false))
                {
                    if (string.IsNullOrEmpty(status) || status == CoopLocalization.Get("net.networkStopped"))
                        status = CoopLocalization.Get("net.clientNetworkStartFailedStatus");
                    isConnecting = false;
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(CoopLocalization.Get("net.clientNetworkStartFailed", e));
                status = CoopLocalization.Get("net.clientNetworkStartFailedStatus");
                isConnecting = false;
                return;
            }

        // 二次确认
        if (!IsActuallyRunning)
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
            writer.Put(BuildInfo.ModVersion);
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


    public string GetSelfNetworkId()
    {
        if (string.IsNullOrEmpty(_selfNetworkId))
            _selfNetworkId = ComputeSelfNetworkId();

        return _selfNetworkId;
    }

    public bool IsSelfId(string id)
    {
        var mine = localPlayerStatus?.EndPoint;
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(mine) && id == mine)
            return true;

        var networkId = GetSelfNetworkId();
        return !string.IsNullOrEmpty(id) && id == networkId;
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

    public void GrantPlayerInvincibility(string playerId, float durationSeconds)
    {
        if (string.IsNullOrEmpty(playerId))
            return;

        var until = Time.time + Mathf.Max(0f, durationSeconds);
        _playerInvincibleUntil[playerId] = until;
    }

    public bool IsPlayerInvincible(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return false;

        if (_playerInvincibleUntil.TryGetValue(playerId, out var until))
        {
            if (Time.time < until)
                return true;

            _playerInvincibleUntil.Remove(playerId);
        }

        return false;
    }

    public bool TryGetPeerByPlayerId(string playerId, out NetPeer peer)
    {
        peer = null;
        if (string.IsNullOrEmpty(playerId) || playerStatuses == null)
            return false;

        foreach (var kvp in playerStatuses)
        {
            var status = kvp.Value;
            if (status != null && !string.IsNullOrEmpty(status.EndPoint) &&
                string.Equals(status.EndPoint, playerId, StringComparison.OrdinalIgnoreCase))
            {
                peer = kvp.Key;
                return true;
            }
        }

        return false;
    }

    public bool TryGetPlayerId(CharacterMainControl cmc, out string playerId)
    {
        playerId = null;
        if (cmc == null) return false;

        if (IsServer)
        {
            foreach (var kvp in remoteCharacters)
            {
                var go = kvp.Value;
                if (go != null && cmc.transform.IsChildOf(go.transform))
                {
                    playerId = GetPlayerId(kvp.Key);
                    return !string.IsNullOrEmpty(playerId);
                }
            }
        }
        else
        {
            foreach (var kvp in clientRemoteCharacters)
            {
                var go = kvp.Value;
                if (go != null && cmc.transform.IsChildOf(go.transform))
                {
                    playerId = kvp.Key;
                    return true;
                }
            }
        }

        return false;
    }

    public bool KickPlayer(string playerId)
    {
        if (!IsServer || netManager == null || !netManager.IsRunning)
            return false;

        if (TryGetPeerByPlayerId(playerId, out var peer) && peer != null)
        {
            peer.Disconnect();
            return true;
        }

        return false;
    }

    private string ComputeSelfNetworkId()
    {
        if (IsServer)
            return $"Host:{port}";

        try
        {
            if (netManager != null && netManager.LocalPort > 0)
            {
                var ip = NetUtils.GetLocalIp(LocalAddrType.IPv4);
                if (ip != null)
                    return $"{ip}:{netManager.LocalPort}";
            }
        }
        catch
        {
        }

        return localPlayerStatus != null && !string.IsNullOrEmpty(localPlayerStatus.EndPoint)
            ? localPlayerStatus.EndPoint
            : $"Client:{Guid.NewGuid().ToString().Substring(0, 8)}";
    }
}
