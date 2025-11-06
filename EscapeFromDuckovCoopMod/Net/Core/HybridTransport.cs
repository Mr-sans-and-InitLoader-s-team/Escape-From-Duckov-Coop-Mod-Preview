using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    public enum TransportProtocol
    {
        UDP,
        RESTful
    }
    
    public class HybridTransport : MonoBehaviour
    {
        public static HybridTransport Instance { get; private set; }
        
        private INetworkTransport _udpTransport;
        private SimpleRESTfulTransport _restfulTransport;
        
        public INetworkTransport UDPTransport => _udpTransport;
        public SimpleRESTfulTransport RESTfulTransport => _restfulTransport;
        
        public bool IsInitialized => (_udpTransport?.IsInitialized ?? false) || (_restfulTransport?.IsInitialized ?? false);
        public bool IsServer => _udpTransport?.IsServer ?? false;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        
        public void Initialize(TransportType udpType, int port, bool enableRESTful = true)
        {
            _udpTransport = NetworkTransportFactory.Create(udpType);
            
            Debug.Log($"[HybridTransport-Init] enableRESTful={enableRESTful}, existing Instance={(SimpleRESTfulTransport.Instance != null ? "OK" : "NULL")}");
            
            if (enableRESTful)
            {
                if (SimpleRESTfulTransport.Instance != null)
                {
                    _restfulTransport = SimpleRESTfulTransport.Instance;
                    Debug.Log("[HybridTransport-Init] Reusing existing RESTful transport instance");
                }
                else
                {
                    var restGO = new GameObject("SimpleRESTfulTransport");
                    restGO.transform.SetParent(transform);
                    _restfulTransport = restGO.AddComponent<SimpleRESTfulTransport>();
                    Debug.Log("[HybridTransport-Init] Created new RESTful transport instance");
                }
            }
            
            Debug.Log($"[HybridTransport] Initialized with UDP:{udpType}, RESTful:{enableRESTful}, _restfulTransport={(_restfulTransport != null ? "OK" : "NULL")}");
        }
        
        public bool StartServer(int port, int restPort = 0)
        {
            bool udpStarted = _udpTransport?.StartServer(port) ?? false;
            
            if (_restfulTransport != null && !_restfulTransport.IsInitialized)
            {
                int actualRestPort = restPort > 0 ? restPort : port + 1000;
                _restfulTransport.InitializeServer(actualRestPort);
                Debug.Log($"[HybridTransport] RESTful server initialized on port {actualRestPort}");
            }
            else if (_restfulTransport != null)
            {
                Debug.Log("[HybridTransport] RESTful server already initialized, skipping");
            }
            
            Debug.Log($"[HybridTransport] Server started - UDP:{udpStarted}, RESTful:{_restfulTransport?.IsInitialized}");
            return udpStarted;
        }
        
        public bool StartClient(string serverAddress, int port, int restPort = 0)
        {
            bool udpStarted = false;
            
            if (_udpTransport != null)
            {
                udpStarted = _udpTransport.StartClient();
                _udpTransport.Connect(serverAddress, port);
            }
            
            if (_restfulTransport != null)
            {
                int actualRestPort = restPort > 0 ? restPort : port + 1000;
                _restfulTransport.InitializeClient(serverAddress, actualRestPort);
            }
            
            Debug.Log($"[HybridTransport] Client started - UDP:{udpStarted}, RESTful:{_restfulTransport?.IsInitialized}");
            return udpStarted;
        }
        
        public void Send(NetDataWriter writer, DeliveryMethod method, TransportProtocol protocol = TransportProtocol.UDP)
        {
            if (protocol == TransportProtocol.UDP)
            {
                _udpTransport?.Send(writer, method);
            }
        }
        
        public void SendToAll(NetDataWriter writer, DeliveryMethod method, TransportProtocol protocol = TransportProtocol.UDP)
        {
            if (protocol == TransportProtocol.UDP)
            {
                _udpTransport?.SendToAll(writer, method);
            }
        }
        
        public void SendToPeer(long connectionId, NetDataWriter writer, DeliveryMethod method, TransportProtocol protocol = TransportProtocol.UDP)
        {
            if (protocol == TransportProtocol.UDP)
            {
                _udpTransport?.SendToPeer(connectionId, writer, method);
            }
        }
        
        public void SendRESTfulRequest(string endpoint, string method, object data, Action<RESTfulResponse> callback)
        {
            if (_restfulTransport == null || !_restfulTransport.IsInitialized)
            {
                Debug.LogError("[HybridTransport] RESTful transport not available");
                callback?.Invoke(new RESTfulResponse { Success = false, Error = "RESTful not initialized" });
                return;
            }
            
            _restfulTransport.SendRequest(endpoint, method, data, callback);
        }
        
        public void PollEvents()
        {
            _udpTransport?.PollEvents();
        }
        
        public void Stop()
        {
            _udpTransport?.Stop();
            _restfulTransport?.Shutdown();
            Debug.Log("[HybridTransport] Stopped");
        }
        
        public IEnumerable<long> GetConnectedPeers()
        {
            return _udpTransport?.GetConnectedPeers() ?? Array.Empty<long>();
        }
        
        public float GetPing(long connectionId)
        {
            return _udpTransport?.GetPing(connectionId) ?? 0f;
        }
    }
}

