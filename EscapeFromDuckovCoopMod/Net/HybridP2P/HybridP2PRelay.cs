using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P;

public class HybridP2PRelay : MonoBehaviour
{
    public static HybridP2PRelay Instance { get; private set; }
    
    private class RelayConnection
    {
        public string EndPoint;
        public CSteamID SteamID;
        public NATType NATType;
        public bool UseRelay;
        public float LastActivityTime;
        public int FailedPackets;
        public bool P2PHealthy = true;
    }
    
    private readonly Dictionary<string, RelayConnection> _connections = new();
    private HybridP2PValidator _validator;
    private NATDetector _natDetector;
    private LatencyCalculator _latencyCalculator;
    private LatencyCompensator _latencyCompensator;
    private SteamNetworkingTransport _steamTransport;
    private HybridRPCManager _rpcManager;
    
    public SteamNetworkingTransport SteamTransport => _steamTransport;
    public HybridRPCManager RPCManager => _rpcManager;
    public bool UseSteamNetworkingSockets { get; set; } = false;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        _validator = new HybridP2PValidator();
        _natDetector = new NATDetector();
        _latencyCalculator = new LatencyCalculator();
        _latencyCompensator = new LatencyCompensator();
        
        InitializeSteamNetworking();
        
        Debug.Log("[HybridP2PRelay] Initialized");
    }
    
    private void InitializeSteamNetworking()
    {
        var transportGO = new GameObject("SteamNetworkingTransport");
        transportGO.transform.SetParent(transform);
        _steamTransport = transportGO.AddComponent<SteamNetworkingTransport>();
        
        var rpcGO = new GameObject("HybridRPCManager");
        rpcGO.transform.SetParent(transform);
        _rpcManager = rpcGO.AddComponent<HybridRPCManager>();
        _rpcManager.Initialize(_steamTransport);
        
        Debug.Log("[HybridP2PRelay] Steam Networking initialized");
    }
    
    private void Start()
    {
        InitializeNATDetection();
    }
    
    private void Update()
    {
        _latencyCalculator?.Update();
        CleanupInactiveConnections();
        CheckP2PHealth();
    }
    
    private async void InitializeNATDetection()
    {
        try
        {
            var natType = await _natDetector.DetectNATType();
            Debug.Log($"[HybridP2PRelay] Local NAT type: {NATDetector.GetNATTypeDisplayName(natType)}");
            
            var netService = EscapeFromDuckovCoopMod.NetService.Instance;
            if (netService != null && netService.localPlayerStatus != null)
            {
                netService.localPlayerStatus.NATType = natType;
                netService.localPlayerStatus.UseRelay = !_natDetector.CanDirectConnect(natType);
                Debug.Log($"[HybridP2PRelay] 更新本地玩家NAT状态: {NATDetector.GetNATTypeDisplayName(natType)}, UseRelay={netService.localPlayerStatus.UseRelay}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[HybridP2PRelay] Error detecting NAT: {e.Message}");
        }
    }
    
    public void RegisterConnection(string endPoint, CSteamID steamID, NATType natType)
    {
        if (!_connections.ContainsKey(endPoint))
        {
            bool useRelay = !_natDetector.CanDirectConnect(natType);
            
            _connections[endPoint] = new RelayConnection
            {
                EndPoint = endPoint,
                SteamID = steamID,
                NATType = natType,
                UseRelay = useRelay,
                LastActivityTime = Time.realtimeSinceStartup,
                P2PHealthy = true
            };
            
            _latencyCalculator.RegisterClient(endPoint);
            
            var service = EscapeFromDuckovCoopMod.NetService.Instance;
            var isServer = service?.IsServer ?? false;
            Debug.Log($"[HybridP2PRelay] 注册连接 endPoint={endPoint}, NAT={NATDetector.GetNATTypeDisplayName(natType)}, Relay={useRelay}, IsServer={isServer}");
        }
    }
    
    public void UnregisterConnection(string endPoint)
    {
        if (_connections.Remove(endPoint))
        {
            _latencyCalculator.UnregisterClient(endPoint);
            Debug.Log($"[HybridP2PRelay] Unregistered connection {endPoint}");
        }
    }
    
    public bool ValidateAndRelayPacket(string endPoint, NetDataReader reader, byte packetType)
    {
        if (!_connections.TryGetValue(endPoint, out var connection))
        {
            if (packetType == 19 || packetType == 20 || packetType == 21 || packetType == 23)
            {
                Debug.Log($"[HybridP2PRelay] 投票相关消息通过 (未注册连接): endPoint={endPoint}, op={packetType}");
            }
            return true;
        }
        
        connection.LastActivityTime = Time.realtimeSinceStartup;
        
        if (connection.UseRelay)
        {
            Debug.Log($"[HybridP2PRelay] 使用Relay转发: endPoint={endPoint}, op={packetType}");
            return RelayPacketViaSteam(connection, reader, packetType);
        }
        
        if (packetType == 19 || packetType == 20 || packetType == 21 || packetType == 23)
        {
            Debug.Log($"[HybridP2PRelay] 投票相关消息通过: endPoint={endPoint}, op={packetType}");
        }
        return true;
    }
    
    private bool ValidatePacket(string endPoint, NetDataReader reader, byte packetType)
    {
        try
        {
            switch (packetType)
            {
                case 1:
                    var position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    var velocity = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
                    return _validator.ValidatePositionUpdate(endPoint, position, velocity);
                
                case 10:
                    return _validator.ValidateFireEvent(endPoint);
                
                case 11:
                    float damage = reader.GetFloat();
                    return _validator.ValidateDamageValue(endPoint, damage);
                
                case 200:
                    _latencyCalculator.HandlePingPacket(endPoint, reader);
                    return true;
                
                case 201:
                    _latencyCalculator.HandlePongPacket(endPoint, reader);
                    return true;
                
                default:
                    return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[HybridP2PRelay] Error validating packet: {e.Message}");
            return false;
        }
    }
    
    private bool RelayPacketViaSteam(RelayConnection connection, NetDataReader reader, byte packetType)
    {
        try
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogWarning("[HybridP2PRelay] Steam not initialized, cannot relay");
                return false;
            }
            
            byte[] data = new byte[reader.AvailableBytes];
            reader.GetBytes(data, reader.AvailableBytes);
            
            bool sent = SteamNetworking.SendP2PPacket(connection.SteamID, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, 0);
            
            if (!sent)
            {
                Debug.LogWarning($"[HybridP2PRelay] Failed to relay packet to {connection.EndPoint}");
            }
            
            return sent;
        }
        catch (Exception e)
        {
            Debug.LogError($"[HybridP2PRelay] Error relaying packet: {e.Message}");
            return false;
        }
    }
    
    private void CheckP2PHealth()
    {
        foreach (var kvp in _connections)
        {
            var connection = kvp.Value;
            
            if (!connection.P2PHealthy && Time.realtimeSinceStartup - connection.LastActivityTime > 5f)
            {
                connection.P2PHealthy = true;
                connection.UseRelay = !_natDetector.CanDirectConnect(connection.NATType);
                connection.FailedPackets = 0;
                Debug.Log($"[HybridP2PRelay] P2P recovered for {connection.EndPoint}");
            }
        }
    }
    
    private void CleanupInactiveConnections()
    {
        float currentTime = Time.realtimeSinceStartup;
        List<string> toRemove = new List<string>();
        
        foreach (var kvp in _connections)
        {
            if (currentTime - kvp.Value.LastActivityTime > 30f)
            {
                toRemove.Add(kvp.Key);
            }
        }
        
        foreach (var endPoint in toRemove)
        {
            UnregisterConnection(endPoint);
        }
    }
    
    public NATType GetConnectionNATType(string endPoint)
    {
        if (_connections.TryGetValue(endPoint, out var connection))
        {
            return connection.NATType;
        }
        return NATType.Unknown;
    }
    
    public bool IsUsingRelay(string endPoint)
    {
        if (_connections.TryGetValue(endPoint, out var connection))
        {
            return connection.UseRelay;
        }
        return false;
    }
    
    public bool IsP2PHealthy(string endPoint)
    {
        if (_connections.TryGetValue(endPoint, out var connection))
        {
            return connection.P2PHealthy;
        }
        return true;
    }
    
    public float GetLatency(string endPoint)
    {
        return _latencyCalculator.GetLatency(endPoint);
    }
    
    public NATType GetLocalNATType()
    {
        return _natDetector.LocalNATType;
    }
    
    public bool ShouldUseHybridForVote()
    {
        int unhealthyCount = 0;
        int totalCount = 0;
        
        foreach (var kvp in _connections)
        {
            totalCount++;
            if (!kvp.Value.P2PHealthy)
            {
                unhealthyCount++;
            }
        }
        
        if (totalCount == 0) return false;
        
        float unhealthyRatio = (float)unhealthyCount / totalCount;
        return unhealthyRatio > 0.3f;
    }
    
    public void RecordPosition(string endPoint, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        _latencyCompensator?.RecordPosition(endPoint, position, rotation, velocity);
    }
    
    public Vector3 CompensatePosition(string endPoint, Vector3 receivedPosition)
    {
        float latency = GetLatency(endPoint);
        return _latencyCompensator?.CompensatePosition(endPoint, receivedPosition, latency) ?? receivedPosition;
    }
}

