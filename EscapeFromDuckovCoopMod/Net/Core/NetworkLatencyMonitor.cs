using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using LiteNetLib;
using Steamworks;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    public class LatencyMeasurement
    {
        public float CurrentLatency;
        public float AverageLatency;
        public float MinLatency = float.MaxValue;
        public float MaxLatency;
        public int PacketsSent;
        public int PacketsReceived;
        public float PacketLoss;
        public long LastUpdateTime;
    }
    
    public class NetworkLatencyMonitor : MonoBehaviour
    {
        public static NetworkLatencyMonitor Instance { get; private set; }
        
        private readonly Dictionary<string, LatencyMeasurement> _endpointLatency = new();
        private readonly Dictionary<ulong, LatencyMeasurement> _steamIdLatency = new();
        private readonly Dictionary<string, Queue<float>> _latencyHistory = new();
        private readonly Dictionary<string, Stopwatch> _pingTimers = new();
        
        private const int HISTORY_SIZE = 30;
        private const float PING_INTERVAL = 2f;
        private float _nextPingTime;
        
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
    
    private void Start()
    {
        RegisterLatencyRPCs();
    }
        
        private void Update()
        {
            if (Time.time >= _nextPingTime)
            {
                _nextPingTime = Time.time + PING_INTERVAL;
                SendPingToAll();
            }
            
            UpdateLANLatency();
            UpdateSteamLatency();
        }
        
        private void SendPingToAll()
        {
            var mod = ModBehaviourF.Instance;
            if (mod == null || !mod.networkStarted) return;
            
            var rpcManager = HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null) return;
            
            long timestamp = Stopwatch.GetTimestamp();
            
            if (mod.IsServer)
            {
                rpcManager.CallRPC("LatencyPing", HybridP2P.RPCTarget.AllClients, 0, (writer) =>
                {
                    writer.Put(timestamp);
                }, LiteNetLib.DeliveryMethod.Unreliable);
            }
            else
            {
                rpcManager.CallRPC("LatencyPing", HybridP2P.RPCTarget.Server, 0, (writer) =>
                {
                    writer.Put(timestamp);
                }, LiteNetLib.DeliveryMethod.Unreliable);
            }
        }
        
        public void RegisterLatencyRPCs()
        {
            var rpcManager = HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null) return;
            
            rpcManager.RegisterRPC("LatencyPing", OnRPC_LatencyPing);
            rpcManager.RegisterRPC("LatencyPong", OnRPC_LatencyPong);
        }
        
        private void OnRPC_LatencyPing(long senderConnectionId, LiteNetLib.Utils.NetDataReader reader)
        {
            long timestamp = reader.GetLong();
            
            var rpcManager = HybridP2P.HybridRPCManager.Instance;
            if (rpcManager == null) return;
            
            var mod = ModBehaviourF.Instance;
            if (mod == null) return;
            
            if (mod.IsServer)
            {
                rpcManager.CallRPC("LatencyPong", HybridP2P.RPCTarget.AllClients, senderConnectionId, (writer) =>
                {
                    writer.Put(timestamp);
                }, LiteNetLib.DeliveryMethod.Unreliable);
            }
            else
            {
                rpcManager.CallRPC("LatencyPong", HybridP2P.RPCTarget.Server, 0, (writer) =>
                {
                    writer.Put(timestamp);
                }, LiteNetLib.DeliveryMethod.Unreliable);
            }
        }
        
        private void OnRPC_LatencyPong(long senderConnectionId, LiteNetLib.Utils.NetDataReader reader)
        {
            long sentTimestamp = reader.GetLong();
            long nowTimestamp = Stopwatch.GetTimestamp();
            
            float latencyMs = (float)(nowTimestamp - sentTimestamp) * 1000f / Stopwatch.Frequency;
            
            string identifier = $"conn_{senderConnectionId}";
            RecordLatency(identifier, latencyMs);
        }
        
        private void UpdateLANLatency()
        {
            var service = NetService.Instance;
            if (service == null || service.netManager == null) return;
            
            var mod = ModBehaviourF.Instance;
            if (mod == null) return;
            
            if (mod.IsServer)
            {
                foreach (var peer in service.netManager.ConnectedPeerList)
                {
                    if (peer == null) continue;
                    
                    float latencyMs = peer.Ping;
                    
                    string endpoint = peer.EndPoint?.ToString();
                    if (!string.IsNullOrEmpty(endpoint))
                    {
                        RecordLatency(endpoint, latencyMs);
                        
                        if (service.playerStatuses.TryGetValue(peer, out var status))
                        {
                            status.Latency = (int)GetAverageLatency(endpoint);
                        }
                    }
                }
            }
            else if (service.connectedPeer != null)
            {
                float latencyMs = service.connectedPeer.Ping;
                
                string endpoint = service.connectedPeer.EndPoint?.ToString();
                if (!string.IsNullOrEmpty(endpoint))
                {
                    RecordLatency(endpoint, latencyMs);
                }
            }
        }
        
        private void UpdateSteamLatency()
        {
            if (!SteamManager.Initialized) return;
            
            var mapper = SteamEndPointMapper.Instance;
            if (mapper == null) return;
            
            var service = NetService.Instance;
            if (service == null) return;
            
            var mod = ModBehaviourF.Instance;
            if (mod == null) return;
            
            if (mod.IsServer)
            {
                foreach (var kv in service.playerStatuses)
                {
                    var peer = kv.Key;
                    if (peer == null || peer.EndPoint == null) continue;
                    
                    if (mapper.TryGetSteamID((System.Net.IPEndPoint)peer.EndPoint, out var steamId))
                    {
                        float latencyMs = peer.Ping;
                        RecordLatencyForSteamID(steamId.m_SteamID, latencyMs);
                            
                        kv.Value.Latency = (int)GetAverageLatencyForSteamID(steamId.m_SteamID);
                    }
                }
            }
        }
        
        private void RecordLatency(string identifier, float latencyMs)
        {
            if (!_latencyHistory.ContainsKey(identifier))
            {
                _latencyHistory[identifier] = new Queue<float>();
                _endpointLatency[identifier] = new LatencyMeasurement();
            }
            
            var history = _latencyHistory[identifier];
            var measurement = _endpointLatency[identifier];
            
            history.Enqueue(latencyMs);
            if (history.Count > HISTORY_SIZE)
                history.Dequeue();
            
            measurement.CurrentLatency = latencyMs;
            measurement.MinLatency = Mathf.Min(measurement.MinLatency, latencyMs);
            measurement.MaxLatency = Mathf.Max(measurement.MaxLatency, latencyMs);
            measurement.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            float sum = 0;
            foreach (var sample in history)
                sum += sample;
            measurement.AverageLatency = sum / history.Count;
        }
        
        private void RecordLatencyForSteamID(ulong steamId, float latencyMs)
        {
            string key = $"steam_{steamId}";
            RecordLatency(key, latencyMs);
            
            if (!_steamIdLatency.ContainsKey(steamId))
            {
                _steamIdLatency[steamId] = new LatencyMeasurement();
            }
            
            _steamIdLatency[steamId] = _endpointLatency[key];
        }
        
        public float GetCurrentLatency(string identifier)
        {
            return _endpointLatency.TryGetValue(identifier, out var m) ? m.CurrentLatency : 0f;
        }
        
        public float GetAverageLatency(string identifier)
        {
            return _endpointLatency.TryGetValue(identifier, out var m) ? m.AverageLatency : 0f;
        }
        
        public float GetAverageLatencyForSteamID(ulong steamId)
        {
            return _steamIdLatency.TryGetValue(steamId, out var m) ? m.AverageLatency : 0f;
        }
        
        public LatencyMeasurement GetMeasurement(string identifier)
        {
            return _endpointLatency.TryGetValue(identifier, out var m) ? m : null;
        }
    }
}

