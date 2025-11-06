using System;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    public class DirectConnectionTransport : INetworkTransport
    {
        private NetManager _netManager;
        private readonly Dictionary<NetPeer, long> _peerToId = new();
        private readonly Dictionary<long, NetPeer> _idToPeer = new();
        private long _nextConnectionId = 1;
        
        public TransportType Type => TransportType.Direct;
        public bool IsInitialized => _netManager != null && _netManager.IsRunning;
        public bool IsServer { get; private set; }
        public bool IsClient => !IsServer && IsInitialized;
        public bool IsConnected => _netManager != null && _netManager.ConnectedPeersCount > 0;
        
        public event Action<long> OnPeerConnected;
        public event Action<long> OnPeerDisconnected;
        public event Action<long, NetDataReader> OnDataReceived;
        
        private EventBasedNetListener _listener;
        
        public DirectConnectionTransport()
        {
            _listener = new EventBasedNetListener();
            _listener.PeerConnectedEvent += HandlePeerConnected;
            _listener.PeerDisconnectedEvent += HandlePeerDisconnected;
            _listener.NetworkReceiveEvent += HandleNetworkReceive;
            
            _netManager = new NetManager(_listener)
            {
                AutoRecycle = true,
                IPv6Enabled = false,
                DisconnectTimeout = 10000,
                UpdateTime = 15,
                PingInterval = 1000,
                ReconnectDelay = 500,
                MaxConnectAttempts = 10,
                BroadcastReceiveEnabled = true
            };
        }
        
        public bool StartServer(int port)
        {
            if (IsInitialized)
            {
                Debug.LogWarning("[DirectConnectionTransport] Already initialized");
                return false;
            }
            
            IsServer = true;
            var started = _netManager.Start(port);
            
            if (started)
            {
                Debug.Log($"[DirectConnectionTransport] Server started on port {port}");
            }
            else
            {
                Debug.LogError("[DirectConnectionTransport] Failed to start server");
            }
            
            return started;
        }
        
        public bool StartClient()
        {
            if (IsInitialized)
            {
                Debug.LogWarning("[DirectConnectionTransport] Already initialized");
                return false;
            }
            
            IsServer = false;
            var started = _netManager.Start();
            
            if (started)
            {
                Debug.Log("[DirectConnectionTransport] Client started");
            }
            else
            {
                Debug.LogError("[DirectConnectionTransport] Failed to start client");
            }
            
            return started;
        }
        
        public bool Connect(string address, int port)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[DirectConnectionTransport] Not initialized");
                return false;
            }
            
            var peer = _netManager.Connect(address, port, string.Empty);
            if (peer != null)
            {
                Debug.Log($"[DirectConnectionTransport] Connecting to {address}:{port}");
                return true;
            }
            
            Debug.LogError($"[DirectConnectionTransport] Failed to connect to {address}:{port}");
            return false;
        }
        
        public void Disconnect()
        {
            if (_netManager != null)
            {
                _netManager.DisconnectAll();
            }
        }
        
        public void Stop()
        {
            if (_netManager != null && _netManager.IsRunning)
            {
                _netManager.Stop();
                _peerToId.Clear();
                _idToPeer.Clear();
                Debug.Log("[DirectConnectionTransport] Stopped");
            }
            
            IsServer = false;
        }
        
        public void Send(NetDataWriter writer, DeliveryMethod method)
        {
            if (_netManager == null) return;
            
            if (IsServer)
            {
                SendToAll(writer, method);
            }
            else
            {
                var peer = _netManager.FirstPeer;
                if (peer != null)
                {
                    peer.Send(writer, method);
                }
            }
        }
        
        public void SendToAll(NetDataWriter writer, DeliveryMethod method)
        {
            _netManager?.SendToAll(writer, method);
        }
        
        public void SendToPeer(long connectionId, NetDataWriter writer, DeliveryMethod method)
        {
            if (_idToPeer.TryGetValue(connectionId, out var peer))
            {
                peer.Send(writer, method);
            }
        }
        
        public void PollEvents()
        {
            _netManager?.PollEvents();
        }
        
        public IEnumerable<long> GetConnectedPeers()
        {
            return _peerToId.Values;
        }
        
        public float GetPing(long connectionId)
        {
            if (_idToPeer.TryGetValue(connectionId, out var peer))
            {
                return peer.Ping;
            }
            return 0f;
        }
        
        public string GetPeerAddress(long connectionId)
        {
            if (_idToPeer.TryGetValue(connectionId, out var peer))
            {
                return peer.EndPoint?.ToString();
            }
            return null;
        }
        
        private void HandlePeerConnected(NetPeer peer)
        {
            long connectionId = _nextConnectionId++;
            _peerToId[peer] = connectionId;
            _idToPeer[connectionId] = peer;
            
            Debug.Log($"[DirectConnectionTransport] Peer connected: {peer.EndPoint} (ID: {connectionId})");
            OnPeerConnected?.Invoke(connectionId);
        }
        
        private void HandlePeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (_peerToId.TryGetValue(peer, out var connectionId))
            {
                _peerToId.Remove(peer);
                _idToPeer.Remove(connectionId);
                
                Debug.Log($"[DirectConnectionTransport] Peer disconnected: {peer.EndPoint} (ID: {connectionId})");
                OnPeerDisconnected?.Invoke(connectionId);
            }
        }
        
        private void HandleNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (_peerToId.TryGetValue(peer, out var connectionId))
            {
                var dataReader = new NetDataReader();
                dataReader.SetSource(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
                OnDataReceived?.Invoke(connectionId, dataReader);
            }
        }
    }
}

