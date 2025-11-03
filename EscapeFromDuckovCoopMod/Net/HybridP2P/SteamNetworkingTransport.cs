using LiteNetLib;
using LiteNetLib.Utils;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P
{
    public enum NetworkEventType : byte
    {
        Data = 0,
        Connect = 1,
        Disconnect = 2,
        Error = 3
    }

    public struct NetworkEventData
    {
        public NetworkEventType Type;
        public long ConnectionId;
        public byte[] Data;
        public DisconnectReason DisconnectReason;
        public string ErrorMessage;
    }

    public class SteamNetworkingTransport : MonoBehaviour
    {
        private class SteamConnection
        {
            public CSteamID SteamID;
            public HSteamNetConnection Connection;
            public long ConnectionId;
            public bool IsConnected;
            public float LastMessageTime;

            public SteamConnection(CSteamID steamId, HSteamNetConnection connection, long connectionId)
            {
                SteamID = steamId;
                Connection = connection;
                ConnectionId = connectionId;
                IsConnected = false;
                LastMessageTime = Time.realtimeSinceStartup;
            }
        }

        private const int MAX_MESSAGES = 256;
        private const int MAX_MESSAGE_SIZE = 1024 * 512;

        public bool IsServerStarted { get; private set; }
        public bool IsClientStarted => _clientConnection != null;
        public int ServerPeersCount => _serverConnections.Count;
        public int ServerMaxConnections { get; private set; }

        private HSteamListenSocket _serverSocket;
        private SteamConnection _clientConnection;
        private readonly Dictionary<long, SteamConnection> _serverConnections = new Dictionary<long, SteamConnection>();
        private readonly Queue<NetworkEventData> _clientEventQueue = new Queue<NetworkEventData>();
        private readonly Queue<NetworkEventData> _serverEventQueue = new Queue<NetworkEventData>();
        
        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionCallback;
        private SteamNetworkingConfigValue_t[] _connectionConfig;

        public static SteamNetworkingTransport Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            InitializeConfig();
            Debug.Log("[SteamNetworkingTransport] 初始化完成");
        }

        private void InitializeConfig()
        {
            _connectionConfig = new SteamNetworkingConfigValue_t[]
            {
                new SteamNetworkingConfigValue_t
                {
                    m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected,
                    m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                    m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = 10000 }
                }
            };
        }

        public bool StartServer(int port, int maxConnections)
        {
            if (IsServerStarted)
            {
                Debug.LogWarning("[SteamNetworkingTransport] Server already started");
                return false;
            }

            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamNetworkingTransport] Steam not initialized");
                return false;
            }

            try
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                
                if (_connectionCallback == null)
                {
                    _connectionCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
                }

                _serverSocket = SteamNetworkingSockets.CreateListenSocketP2P(port, _connectionConfig.Length, _connectionConfig);
                IsServerStarted = true;
                ServerMaxConnections = maxConnections;
                
                Debug.Log($"[SteamNetworkingTransport] Server started on port {port}, max connections: {maxConnections}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamNetworkingTransport] Failed to start server: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public bool StartClient(string address, int port)
        {
            if (IsClientStarted)
            {
                Debug.LogWarning("[SteamNetworkingTransport] Client already started");
                return false;
            }

            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamNetworkingTransport] Steam not initialized");
                return false;
            }

            try
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                
                if (_connectionCallback == null)
                {
                    _connectionCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
                }

                if (!ulong.TryParse(address, out ulong steamId))
                {
                    Debug.LogError($"[SteamNetworkingTransport] Invalid Steam ID: {address}");
                    return false;
                }

                CSteamID targetSteamId = new CSteamID(steamId);
                SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
                identity.SetSteamID(targetSteamId);

                HSteamNetConnection connection = SteamNetworkingSockets.ConnectP2P(ref identity, port, _connectionConfig.Length, _connectionConfig);
                _clientConnection = new SteamConnection(targetSteamId, connection, (long)steamId);
                
                Debug.Log($"[SteamNetworkingTransport] Client connecting to {steamId} on port {port}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamNetworkingTransport] Failed to start client: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public void StopServer()
        {
            if (!IsServerStarted)
                return;

            foreach (var conn in _serverConnections.Values)
            {
                SteamNetworkingSockets.CloseConnection(conn.Connection, 0, "Server stopped", false);
            }

            _serverConnections.Clear();
            SteamNetworkingSockets.CloseListenSocket(_serverSocket);
            IsServerStarted = false;

            if (!IsClientStarted && _connectionCallback != null)
            {
                _connectionCallback.Dispose();
                _connectionCallback = null;
            }

            Debug.Log("[SteamNetworkingTransport] Server stopped");
        }

        public void StopClient()
        {
            if (!IsClientStarted)
                return;

            if (_clientConnection != null)
            {
                SteamNetworkingSockets.CloseConnection(_clientConnection.Connection, 0, "Client disconnected", false);
                _clientConnection = null;
            }

            if (!IsServerStarted && _connectionCallback != null)
            {
                _connectionCallback.Dispose();
                _connectionCallback = null;
            }

            Debug.Log("[SteamNetworkingTransport] Client stopped");
        }

        public bool ServerSend(long connectionId, byte[] data, DeliveryMethod deliveryMethod)
        {
            if (!IsServerStarted)
                return false;

            if (!_serverConnections.TryGetValue(connectionId, out var conn))
            {
                Debug.LogWarning($"[SteamNetworkingTransport] Connection not found: {connectionId}");
                return false;
            }

            return SendMessage(conn.Connection, data, deliveryMethod);
        }

        public bool ClientSend(byte[] data, DeliveryMethod deliveryMethod)
        {
            if (!IsClientStarted || _clientConnection == null)
                return false;

            return SendMessage(_clientConnection.Connection, data, deliveryMethod);
        }

        public bool ServerReceive(out NetworkEventData eventData)
        {
            eventData = default;

            if (!IsServerStarted)
                return false;

            foreach (var conn in _serverConnections.Values)
            {
                if (conn.IsConnected)
                {
                    ReceiveMessages(conn, _serverEventQueue);
                }
            }

            if (_serverEventQueue.Count == 0)
                return false;

            eventData = _serverEventQueue.Dequeue();
            return true;
        }

        public bool ClientReceive(out NetworkEventData eventData)
        {
            eventData = default;

            if (!IsClientStarted || _clientConnection == null)
                return false;

            if (_clientConnection.IsConnected)
            {
                ReceiveMessages(_clientConnection, _clientEventQueue);
            }

            if (_clientEventQueue.Count == 0)
                return false;

            eventData = _clientEventQueue.Dequeue();
            return true;
        }

        public bool ServerDisconnect(long connectionId, string reason = "Disconnected")
        {
            if (!IsServerStarted)
                return false;

            if (!_serverConnections.TryGetValue(connectionId, out var conn))
                return false;

            SteamNetworkingSockets.CloseConnection(conn.Connection, 0, reason, false);
            _serverConnections.Remove(connectionId);
            
            Debug.Log($"[SteamNetworkingTransport] Disconnected: {connectionId}, reason: {reason}");
            return true;
        }

        public long GetClientRtt()
        {
            if (!IsClientStarted || _clientConnection == null)
                return 0;

            P2PSessionState_t sessionState;
            if (SteamNetworking.GetP2PSessionState(_clientConnection.SteamID, out sessionState))
            {
                return sessionState.m_nRemoteIP;
            }

            return 0;
        }

        public long GetServerRtt(long connectionId)
        {
            if (!IsServerStarted)
                return 0;

            if (!_serverConnections.TryGetValue(connectionId, out var conn))
                return 0;

            P2PSessionState_t sessionState;
            if (SteamNetworking.GetP2PSessionState(conn.SteamID, out sessionState))
            {
                return sessionState.m_nRemoteIP;
            }

            return 0;
        }

        private bool SendMessage(HSteamNetConnection connection, byte[] data, DeliveryMethod deliveryMethod)
        {
            if (data == null || data.Length == 0)
                return false;

            if (data.Length > MAX_MESSAGE_SIZE)
            {
                Debug.LogError($"[SteamNetworkingTransport] Message too large: {data.Length} > {MAX_MESSAGE_SIZE}");
                return false;
            }

            try
            {
                GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
                IntPtr pData = pinnedArray.AddrOfPinnedObject();

                int sendFlags = GetSendFlags(deliveryMethod);
                EResult result = SteamNetworkingSockets.SendMessageToConnection(connection, pData, (uint)data.Length, sendFlags, out long _);

                pinnedArray.Free();

                if (result == EResult.k_EResultOK)
                {
                    return true;
                }
                else if (result == EResult.k_EResultNoConnection || result == EResult.k_EResultInvalidParam)
                {
                    Debug.LogWarning($"[SteamNetworkingTransport] Connection lost");
                    SteamNetworkingSockets.CloseConnection(connection, 0, "Connection lost", false);
                    return false;
                }
                else
                {
                    Debug.LogError($"[SteamNetworkingTransport] Send failed: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamNetworkingTransport] Send exception: {ex.Message}");
                return false;
            }
        }

        private void ReceiveMessages(SteamConnection connection, Queue<NetworkEventData> eventQueue)
        {
            try
            {
                IntPtr[] ptrs = new IntPtr[MAX_MESSAGES];
                int messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection.Connection, ptrs, MAX_MESSAGES);

                if (messageCount > 0)
                {
                    connection.LastMessageTime = Time.realtimeSinceStartup;

                    for (int i = 0; i < messageCount; i++)
                    {
                        SteamNetworkingMessage_t message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs[i]);

                        if (message.m_cbSize > 0)
                        {
                            byte[] buffer = new byte[message.m_cbSize];
                            Marshal.Copy(message.m_pData, buffer, 0, message.m_cbSize);

                            eventQueue.Enqueue(new NetworkEventData
                            {
                                Type = NetworkEventType.Data,
                                ConnectionId = connection.ConnectionId,
                                Data = buffer
                            });
                        }

                        SteamNetworkingMessage_t.Release(ptrs[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamNetworkingTransport] Receive exception: {ex.Message}");
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            CSteamID remoteSteamId = callback.m_info.m_identityRemote.GetSteamID();
            long connectionId = (long)remoteSteamId.m_SteamID;

            Debug.Log($"[SteamNetworkingTransport] Connection status changed: {remoteSteamId} -> {callback.m_info.m_eState}");

            switch (callback.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (IsServerStarted)
                    {
                        EResult acceptResult = SteamNetworkingSockets.AcceptConnection(callback.m_hConn);
                        if (acceptResult == EResult.k_EResultOK)
                        {
                            var conn = new SteamConnection(remoteSteamId, callback.m_hConn, connectionId);
                            _serverConnections[connectionId] = conn;
                            Debug.Log($"[SteamNetworkingTransport] Server accepted connection: {remoteSteamId}");
                        }
                        else
                        {
                            Debug.LogWarning($"[SteamNetworkingTransport] Failed to accept connection: {remoteSteamId}, result: {acceptResult}");
                        }
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (IsServerStarted && _serverConnections.TryGetValue(connectionId, out var serverConn))
                    {
                        serverConn.IsConnected = true;
                        _serverEventQueue.Enqueue(new NetworkEventData
                        {
                            Type = NetworkEventType.Connect,
                            ConnectionId = connectionId
                        });
                        Debug.Log($"[SteamNetworkingTransport] Server connection established: {remoteSteamId}");
                    }
                    else if (IsClientStarted && _clientConnection != null && _clientConnection.SteamID == remoteSteamId)
                    {
                        _clientConnection.IsConnected = true;
                        _clientEventQueue.Enqueue(new NetworkEventData
                        {
                            Type = NetworkEventType.Connect,
                            ConnectionId = connectionId
                        });
                        Debug.Log($"[SteamNetworkingTransport] Client connection established: {remoteSteamId}");
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    DisconnectReason reason = callback.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                        ? DisconnectReason.DisconnectPeerCalled
                        : DisconnectReason.ConnectionFailed;

                    if (IsServerStarted && _serverConnections.TryGetValue(connectionId, out var disconnServerConn))
                    {
                        _serverEventQueue.Enqueue(new NetworkEventData
                        {
                            Type = NetworkEventType.Disconnect,
                            ConnectionId = connectionId,
                            DisconnectReason = reason
                        });
                        _serverConnections.Remove(connectionId);
                        SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Disconnected", false);
                        Debug.Log($"[SteamNetworkingTransport] Server connection closed: {remoteSteamId}, reason: {reason}");
                    }
                    else if (IsClientStarted && _clientConnection != null && _clientConnection.SteamID == remoteSteamId)
                    {
                        _clientEventQueue.Enqueue(new NetworkEventData
                        {
                            Type = NetworkEventType.Disconnect,
                            ConnectionId = connectionId,
                            DisconnectReason = reason
                        });
                        _clientConnection = null;
                        Debug.Log($"[SteamNetworkingTransport] Client connection closed: {remoteSteamId}, reason: {reason}");
                    }
                    break;
            }
        }

        private int GetSendFlags(DeliveryMethod deliveryMethod)
        {
            switch (deliveryMethod)
            {
                case DeliveryMethod.ReliableOrdered:
                case DeliveryMethod.ReliableUnordered:
                case DeliveryMethod.ReliableSequenced:
                    return Constants.k_nSteamNetworkingSend_Reliable;
                
                case DeliveryMethod.Sequenced:
                    return Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
                
                case DeliveryMethod.Unreliable:
                default:
                    return Constants.k_nSteamNetworkingSend_Unreliable;
            }
        }

        private void OnDestroy()
        {
            StopClient();
            StopServer();

            if (_connectionCallback != null)
            {
                _connectionCallback.Dispose();
                _connectionCallback = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }

            Debug.Log("[SteamNetworkingTransport] Destroyed");
        }

        private void Update()
        {
            if (IsServerStarted)
            {
                var now = Time.realtimeSinceStartup;
                var toRemove = new List<long>();

                foreach (var kvp in _serverConnections)
                {
                    if (now - kvp.Value.LastMessageTime > 30f)
                    {
                        Debug.LogWarning($"[SteamNetworkingTransport] Connection timeout: {kvp.Key}");
                        toRemove.Add(kvp.Key);
                    }
                }

                foreach (var connId in toRemove)
                {
                    ServerDisconnect(connId, "Timeout");
                }
            }
        }
    }
}

