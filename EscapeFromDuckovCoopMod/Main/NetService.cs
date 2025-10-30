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

﻿using ItemStatsSystem;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod
{
    public class NetService : MonoBehaviour, INetEventListener
    {
        public static NetService Instance;
        public bool IsServer { get; private set; } = false;

        public NetManager netManager;
        public NetDataWriter writer;
        public int port = 9050;
        public List<string> hostList = new List<string>();
        public HashSet<string> hostSet = new HashSet<string>();
        public bool isConnecting = false;
        public string status = "";
        public string manualIP = "127.0.0.1";
        public string manualPort = "9050"; // GTX 5090 我也想要
        public NetPeer connectedPeer;
        public bool networkStarted = false;
        public float broadcastTimer = 0f;
        public float broadcastInterval = 5f;
        public float syncTimer = 0f;
        public float syncInterval = 0.015f; // =========== Mod开发者注意现在是TI版本也就是满血版无同步延迟，0.03 ~33ms ===================

        public readonly HashSet<int> _dedupeShotFrame = new HashSet<int>(); // 本帧已发过的标记

        //本地玩家状态
        public PlayerStatus localPlayerStatus;

        //服务器主机玩家管理
        public readonly Dictionary<NetPeer, PlayerStatus> playerStatuses = new Dictionary<NetPeer, PlayerStatus>();
        public readonly Dictionary<NetPeer, GameObject> remoteCharacters = new Dictionary<NetPeer, GameObject>();

        // 客户端：按 endPoint(玩家ID) 管理
        public readonly Dictionary<string, PlayerStatus> clientPlayerStatuses = new Dictionary<string, PlayerStatus>();
        public readonly Dictionary<string, GameObject> clientRemoteCharacters = new Dictionary<string, GameObject>();

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
                Send_ClientStatus.Instance.SendClientStatusUpdate();
            }

            if (!playerStatuses.ContainsKey(peer))
            {
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
            }

            if (IsServer) Send_LoaclPlayerStatus.Instance.SendPlayerStatusUpdate();

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
                    try { w.Put(hostH.MaxHealth); } catch { w.Put(0f); }
                    try { w.Put(hostH.CurrentHealth); } catch { w.Put(0f); }
                    peer.Send(w, DeliveryMethod.ReliableOrdered);
                }

                if (remoteCharacters != null)
                {
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
                        try { w.Put(h.MaxHealth); } catch { w.Put(0f); }
                        try { w.Put(h.CurrentHealth); } catch { w.Put(0f); }
                        peer.Send(w, DeliveryMethod.ReliableOrdered);
                    }
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
        }

        public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
            Debug.LogError(CoopLocalization.Get("net.networkError", socketError, endPoint.ToString()));
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            ModBehaviourF.Instance.OnNetworkReceive(peer,reader,channelNumber,deliveryMethod);
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            string msg = reader.GetString();

            if (IsServer && msg == "DISCOVER_REQUEST")
            {
                writer.Reset();
                writer.Put("DISCOVER_RESPONSE");
                netManager.SendUnconnectedMessage(writer, remoteEndPoint);
            }
            else if (!IsServer && msg == "DISCOVER_RESPONSE")
            {
                string hostInfo = remoteEndPoint.Address + ":" + port;
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
            else request.Reject();
        }

        public void StartNetwork(bool isServer)
        {
            StopNetwork();
            COOPManager.AIHandle.freezeAI = !isServer;
            IsServer = isServer;
            writer = new NetDataWriter();
            netManager = new NetManager(this)
            {
                BroadcastReceiveEnabled = true
            };

            if (IsServer)
            {
                bool started = netManager.Start(port);
                if (started) Debug.Log(CoopLocalization.Get("net.serverStarted", port));
                else Debug.LogError(CoopLocalization.Get("net.serverStartFailed"));
            }
            else
            {
                bool started = netManager.Start();
                if (started)
                {
                    Debug.Log(CoopLocalization.Get("net.clientStarted"));
                   CoopTool.SendBroadcastDiscovery();
                }
                else Debug.LogError(CoopLocalization.Get("net.clientStartFailed"));
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
        }

        public void StopNetwork()
        {
            if (netManager != null && netManager.IsRunning)
            {
                netManager.Stop();
                Debug.Log(CoopLocalization.Get("net.networkStopped"));
            }
            networkStarted = false;
            connectedPeer = null;

            playerStatuses.Clear();
            clientPlayerStatuses.Clear();

            localPlayerStatus = null;

            foreach (var kvp in remoteCharacters)
                if (kvp.Value != null) Destroy(kvp.Value);
            remoteCharacters.Clear();

            foreach (var kvp in clientRemoteCharacters)
                if (kvp.Value != null) Destroy(kvp.Value);
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
            {
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
                try { connectedPeer?.Disconnect(); } catch { }
                connectedPeer = null;

                if (writer == null) writer = new LiteNetLib.Utils.NetDataWriter();

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
                    return localPlayerStatus.EndPoint;   // 例如 "Host:9050"
                return $"Host:{port}";
            }
            if (playerStatuses != null && playerStatuses.TryGetValue(peer, out var st) && !string.IsNullOrEmpty(st.EndPoint))
                return st.EndPoint;
            return peer.EndPoint.ToString();
        }









    }
}
