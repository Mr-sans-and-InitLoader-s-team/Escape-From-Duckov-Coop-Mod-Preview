using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using Steamworks;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    public class SteamP2PTransport : INetworkTransport
    {
        private DirectConnectionTransport _underlyingTransport;
        private readonly Dictionary<long, CSteamID> _connectionIdToSteamId = new();
        private readonly Dictionary<ulong, long> _steamIdToConnectionId = new();
        private bool _isServer;
        private bool _isClient;
        
        public TransportType Type => TransportType.SteamP2P;
        public bool IsInitialized => _underlyingTransport != null && _underlyingTransport.IsInitialized && SteamManager.Initialized;
        public bool IsServer => _isServer;
        public bool IsClient => _isClient;
        public bool IsConnected => _underlyingTransport?.IsConnected ?? false;
        
        public event Action<long> OnPeerConnected;
        public event Action<long> OnPeerDisconnected;
        public event Action<long, NetDataReader> OnDataReceived;
        
        public SteamP2PTransport()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamP2PTransport] Steam not initialized, falling back to direct connection");
                _underlyingTransport = new DirectConnectionTransport();
                return;
            }
            
            _underlyingTransport = new DirectConnectionTransport();
            
            _underlyingTransport.OnPeerConnected += HandlePeerConnected;
            _underlyingTransport.OnPeerDisconnected += HandlePeerDisconnected;
            _underlyingTransport.OnDataReceived += HandleDataReceived;
            
            var p2pManager = SteamP2PManager.Instance;
            if (p2pManager != null)
            {
                Debug.Log("[SteamP2PTransport] Initialized with Steam P2P support");
            }
        }
        
    public bool StartServer(int port)
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamP2PTransport] Steam not initialized");
            return false;
        }
        
        _isServer = true;
        Debug.Log("[SteamP2PTransport] Server started (Steam P2P mode, no socket binding)");
        return true;
    }
        
        public bool StartClient()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamP2PTransport] Steam not initialized");
                return false;
            }
            
            _isClient = true;
            bool result = _underlyingTransport.StartClient();
            if (result)
            {
                Debug.Log("[SteamP2PTransport] Client started (Steam P2P mode)");
            }
            return result;
        }
        
        public bool Connect(string address, int port)
        {
            return _underlyingTransport.Connect(address, port);
        }
        
        public void Disconnect()
        {
            _underlyingTransport?.Disconnect();
            
            foreach (var steamId in _connectionIdToSteamId.Values)
            {
                SteamNetworking.CloseP2PSessionWithUser(steamId);
            }
            
            _connectionIdToSteamId.Clear();
            _steamIdToConnectionId.Clear();
        }
        
        public void Stop()
        {
            Disconnect();
            _underlyingTransport?.Stop();
        }
        
        public void Send(NetDataWriter writer, DeliveryMethod method)
        {
            _underlyingTransport?.Send(writer, method);
        }
        
        public void SendToAll(NetDataWriter writer, DeliveryMethod method)
        {
            _underlyingTransport?.SendToAll(writer, method);
        }
        
        public void SendToPeer(long connectionId, NetDataWriter writer, DeliveryMethod method)
        {
            _underlyingTransport?.SendToPeer(connectionId, writer, method);
        }
        
        public void PollEvents()
        {
            _underlyingTransport?.PollEvents();
        }
        
        public IEnumerable<long> GetConnectedPeers()
        {
            return _underlyingTransport?.GetConnectedPeers() ?? Array.Empty<long>();
        }
        
        public float GetPing(long connectionId)
        {
            return _underlyingTransport?.GetPing(connectionId) ?? 0f;
        }
        
        public string GetPeerAddress(long connectionId)
        {
            if (_connectionIdToSteamId.TryGetValue(connectionId, out var steamId))
            {
                return $"steam_{steamId.m_SteamID}";
            }
            
            return _underlyingTransport?.GetPeerAddress(connectionId);
        }
        
        private void HandlePeerConnected(long connectionId)
        {
            var address = _underlyingTransport.GetPeerAddress(connectionId);
            
            if (SteamEndPointMapper.Instance != null && !string.IsNullOrEmpty(address))
            {
                try
                {
                    var parts = address.Split(':');
                    if (parts.Length == 2)
                    {
                        var endpoint = new System.Net.IPEndPoint(
                            System.Net.IPAddress.Parse(parts[0]), 
                            int.Parse(parts[1])
                        );
                        
                        if (SteamEndPointMapper.Instance.TryGetSteamID(endpoint, out var steamId))
                        {
                            _connectionIdToSteamId[connectionId] = steamId;
                            _steamIdToConnectionId[steamId.m_SteamID] = connectionId;
                            Debug.Log($"[SteamP2PTransport] Mapped connection {connectionId} to Steam ID {steamId.m_SteamID}");
                        }
                    }
                }
                catch
                {
                }
            }
            
            OnPeerConnected?.Invoke(connectionId);
        }
        
        private void HandlePeerDisconnected(long connectionId)
        {
            if (_connectionIdToSteamId.TryGetValue(connectionId, out var steamId))
            {
                SteamNetworking.CloseP2PSessionWithUser(steamId);
                _connectionIdToSteamId.Remove(connectionId);
                _steamIdToConnectionId.Remove(steamId.m_SteamID);
            }
            
            OnPeerDisconnected?.Invoke(connectionId);
        }
        
        private void HandleDataReceived(long connectionId, NetDataReader reader)
        {
            OnDataReceived?.Invoke(connectionId, reader);
        }
        
        public CSteamID GetSteamID(long connectionId)
        {
            return _connectionIdToSteamId.TryGetValue(connectionId, out var steamId) ? steamId : CSteamID.Nil;
        }
        
        public long GetConnectionId(CSteamID steamId)
        {
            return _steamIdToConnectionId.TryGetValue(steamId.m_SteamID, out var connId) ? connId : 0;
        }
    }
}

