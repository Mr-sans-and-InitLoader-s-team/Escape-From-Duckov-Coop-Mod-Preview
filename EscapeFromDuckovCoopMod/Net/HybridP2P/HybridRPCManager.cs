using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Net.HybridP2P
{
    public enum RPCTarget : byte
    {
        Server = 0,
        AllClients = 1,
        TargetClient = 2,
        AllClientsExceptSender = 3
    }

    public delegate void RPCHandler(long senderConnectionId, NetDataReader reader);

    public enum TransportMode
    {
        Auto,    // 自动选择（优先Steam）
        Steam,   // 强制使用Steam
        LAN      // 强制使用LAN
    }

    public class HybridRPCManager : MonoBehaviour
    {
        public const byte RPC_MESSAGE_TYPE = 255;

        private readonly Dictionary<ushort, RPCHandler> _rpcHandlers = new Dictionary<ushort, RPCHandler>();
        private readonly Dictionary<string, ushort> _rpcNameToId = new Dictionary<string, ushort>();
        private readonly Dictionary<ushort, string> _rpcIdToName = new Dictionary<ushort, string>();
        private Dictionary<long, NetPeer> _connectionIdToPeer = new Dictionary<long, NetPeer>();
        
        private SteamNetworkingTransport _steamTransport;
        private ushort _nextRpcId = 1;
        private ReliabilityManager _reliabilityManager;
        private Core.INetworkTransport _coreTransport;

        public static HybridRPCManager Instance { get; private set; }
        public TransportMode Mode { get; set; } = TransportMode.Auto;
        
        public void SetCoreTransport(Core.INetworkTransport transport)
        {
            _coreTransport = transport;
            
            if (_coreTransport != null)
            {
                Debug.Log($"[HybridRPCManager] Core transport set: {_coreTransport.Type}");
            }
        }
        
        public bool IsServer
        {
            get
            {
                if (UseSteamTransport)
                    return _steamTransport != null && _steamTransport.IsServerStarted;
                else
                    return NetService.Instance != null && NetService.Instance.IsServer;
            }
        }
        
        public bool IsClient
        {
            get
            {
                if (UseSteamTransport)
                    return _steamTransport != null && _steamTransport.IsClientStarted;
                else
                    return NetService.Instance != null && !NetService.Instance.IsServer;
            }
        }
        
        private bool UseSteamTransport
        {
            get
            {
                if (Mode == TransportMode.Steam) return true;
                if (Mode == TransportMode.LAN) return false;
                
                // Auto模式：如果Steam传输层可用且已启动，使用Steam
                return _steamTransport != null && 
                       (_steamTransport.IsServerStarted || _steamTransport.IsClientStarted);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            _reliabilityManager = new ReliabilityManager();
            RegisterRPC("__MessageAck", OnMessageAck);
            
            Debug.Log("[HybridRPCManager] 初始化完成");
        }

        private void Update()
        {
            _reliabilityManager?.Update();
            ProcessMessages();
        }

        private void OnMessageAck(long senderConnectionId, NetDataReader reader)
        {
            uint messageId = reader.GetUInt();
            _reliabilityManager?.OnMessageAck(messageId, senderConnectionId);
        }

        public void Initialize(SteamNetworkingTransport transport)
        {
            _steamTransport = transport;
            Debug.Log("[HybridRPCManager] 已绑定Steam传输层");
        }

        public NetPeer GetPeerByConnectionId(long connectionId)
        {
            return _connectionIdToPeer.TryGetValue(connectionId, out var peer) ? peer : null;
        }
        
        public long GetConnectionIdByPeer(NetPeer peer)
        {
            if (peer == null) return 0;
            foreach (var kvp in _connectionIdToPeer)
            {
                if (kvp.Value == peer)
                    return kvp.Key;
            }
            return 0;
        }
        
        public void UnregisterPeer(long connectionId)
        {
            _connectionIdToPeer.Remove(connectionId);
        }
        
        public void UnregisterPeerByReference(NetPeer peer)
        {
            if (peer == null) return;
            var toRemove = new List<long>();
            foreach (var kvp in _connectionIdToPeer)
            {
                if (kvp.Value == peer)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
            {
                _connectionIdToPeer.Remove(id);
            }
        }
        
        public ushort RegisterRPC(string rpcName, RPCHandler handler)
        {
            if (_rpcNameToId.TryGetValue(rpcName, out ushort existingId))
            {
                Debug.LogWarning($"[HybridRPCManager] RPC '{rpcName}' already registered with ID {existingId}");
                return existingId;
            }

            ushort rpcId = _nextRpcId++;
            _rpcHandlers[rpcId] = handler;
            _rpcNameToId[rpcName] = rpcId;
            _rpcIdToName[rpcId] = rpcName;
            
            Debug.Log($"[HybridRPCManager] Registered RPC '{rpcName}' with ID {rpcId}");
            return rpcId;
        }

        public void UnregisterRPC(string rpcName)
        {
            if (_rpcNameToId.TryGetValue(rpcName, out ushort rpcId))
            {
                _rpcHandlers.Remove(rpcId);
                _rpcNameToId.Remove(rpcName);
                _rpcIdToName.Remove(rpcId);
                Debug.Log($"[HybridRPCManager] Unregistered RPC '{rpcName}'");
            }
        }

        public void CallRPC(string rpcName, RPCTarget target, long targetConnectionId, Action<NetDataWriter> writeData, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
        {
            CallRPCInternal(rpcName, target, targetConnectionId, writeData, deliveryMethod, false, 0);
        }

        public uint CallReliableRPC(string rpcName, RPCTarget target, long targetConnectionId, Action<NetDataWriter> writeData, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
        {
            uint messageId = _reliabilityManager.SendReliableMessage(rpcName, targetConnectionId, writeData);
            Debug.Log($"[HybridRPCManager] CallReliableRPC: {rpcName}, target={target}, msgId={messageId}, useSteam={UseSteamTransport}");
            CallRPCInternal(rpcName, target, targetConnectionId, writeData, deliveryMethod, true, messageId);
            return messageId;
        }

        private void CallRPCInternal(string rpcName, RPCTarget target, long targetConnectionId, Action<NetDataWriter> writeData, DeliveryMethod deliveryMethod, bool withMessageId, uint messageId)
        {
            if (!_rpcNameToId.TryGetValue(rpcName, out ushort rpcId))
            {
                Debug.LogError($"[HybridRPCManager] RPC '{rpcName}' not registered");
                return;
            }

            NetDataWriter writer = new NetDataWriter();
            writer.Put(RPC_MESSAGE_TYPE);
            writer.Put(rpcId);
            
            byte flags = (byte)target;
            if (withMessageId)
            {
                flags |= 0x80; // 设置最高位表示包含messageId
            }
            writer.Put(flags);
            
            if (target == RPCTarget.TargetClient)
            {
                writer.Put(targetConnectionId);
            }

            if (withMessageId)
            {
                writer.Put(messageId);
            }

            writeData?.Invoke(writer);

            if (UseSteamTransport)
            {
                SendViaSteam(target, targetConnectionId, writer.CopyData(), deliveryMethod);
            }
            else
            {
                SendViaLAN(target, targetConnectionId, writer, deliveryMethod);
            }
        }
        
        private void SendViaSteam(RPCTarget target, long targetConnectionId, byte[] data, DeliveryMethod deliveryMethod)
        {
            if (_steamTransport == null)
            {
                Debug.LogError("[HybridRPCManager] Steam transport not initialized");
                return;
            }

            if (IsServer)
            {
                SendSteamRPCAsServer(target, targetConnectionId, data, deliveryMethod);
            }
            else if (IsClient)
            {
                if (target != RPCTarget.Server)
                {
                    Debug.LogWarning($"[HybridRPCManager] Client can only call RPCs with target 'Server', got: {target}");
                    return;
                }
                _steamTransport.ClientSend(data, deliveryMethod);
            }
        }
        
        private void SendViaLAN(RPCTarget target, long targetConnectionId, NetDataWriter writer, DeliveryMethod deliveryMethod)
        {
            var netService = NetService.Instance;
            if (netService == null || netService.netManager == null || !netService.networkStarted)
            {
                return;
            }

            if (IsServer)
            {
                SendLANRPCAsServer(target, targetConnectionId, writer, deliveryMethod);
            }
            else if (IsClient)
            {
                if (target != RPCTarget.Server)
                {
                    Debug.LogWarning($"[HybridRPCManager] Client can only call RPCs with target 'Server', got: {target}");
                    return;
                }
                
                if (netService.connectedPeer != null)
                {
                    netService.connectedPeer.Send(writer, deliveryMethod);
                    // Debug.Log($"[HybridRPCManager] Sent LAN RPC to server");
                }
            }
        }

        private void SendSteamRPCAsServer(RPCTarget target, long targetConnectionId, byte[] data, DeliveryMethod deliveryMethod)
        {
            switch (target)
            {
                case RPCTarget.Server:
                    Debug.LogWarning("[HybridRPCManager] Server cannot call RPC to itself");
                    break;

                case RPCTarget.AllClients:
                    foreach (var kvp in GetAllServerConnections())
                    {
                        _steamTransport.ServerSend(kvp.Key, data, deliveryMethod);
                    }
                    Debug.Log($"[HybridRPCManager] Sent Steam RPC to all {GetAllServerConnections().Count} clients");
                    break;

                case RPCTarget.TargetClient:
                    if (_steamTransport.ServerSend(targetConnectionId, data, deliveryMethod))
                    {
                        Debug.Log($"[HybridRPCManager] Sent Steam RPC to client {targetConnectionId}");
                    }
                    else
                    {
                        Debug.LogWarning($"[HybridRPCManager] Failed to send Steam RPC to client {targetConnectionId}");
                    }
                    break;

                case RPCTarget.AllClientsExceptSender:
                    int sentCount = 0;
                    foreach (var kvp in GetAllServerConnections())
                    {
                        if (kvp.Key != targetConnectionId)
                        {
                            _steamTransport.ServerSend(kvp.Key, data, deliveryMethod);
                            sentCount++;
                        }
                    }
                    Debug.Log($"[HybridRPCManager] Sent Steam RPC to {sentCount} clients (excluding sender {targetConnectionId})");
                    break;
            }
        }
        
        private void SendLANRPCAsServer(RPCTarget target, long targetConnectionId, NetDataWriter writer, DeliveryMethod deliveryMethod)
        {
            var netService = NetService.Instance;
            if (netService == null || netService.netManager == null)
                return;

            switch (target)
            {
                case RPCTarget.Server:
                    Debug.LogWarning("[HybridRPCManager] Server cannot call RPC to itself");
                    break;

                case RPCTarget.AllClients:
                    int peerCount = netService.netManager.ConnectedPeersCount;
                    netService.netManager.SendToAll(writer, deliveryMethod);
                    break;

                case RPCTarget.TargetClient:
                    // 在LAN模式下，targetConnectionId实际上需要是NetPeer
                    // 这需要从playerStatuses中查找对应的peer
                    bool found = false;
                    foreach (var kvp in netService.playerStatuses)
                    {
                        // 暂时使用端口作为ID匹配（需要改进）
                        var peer = kvp.Key;
                        if (peer != null)
                        {
                            netService.netManager.SendToAll(writer, deliveryMethod); // 临时方案
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        Debug.LogWarning($"[HybridRPCManager] Failed to find LAN client {targetConnectionId}");
                    }
                    break;

                case RPCTarget.AllClientsExceptSender:
                    netService.netManager.SendToAll(writer, deliveryMethod); // 简化版，暂不排除发送者
                    // Debug.Log($"[HybridRPCManager] Sent LAN RPC to all clients (simplified)");
                    break;
            }
        }

        public void ProcessMessages()
        {
            // 只处理Steam模式的消息
            // LAN模式的消息在Mod.OnNetworkReceive中处理
            if (!UseSteamTransport)
                return;

            if (_steamTransport == null)
                return;

            if (IsServer)
            {
                ProcessServerMessages();
            }

            if (IsClient)
            {
                ProcessClientMessages();
            }
        }
        
        // 供Mod.OnNetworkReceive调用，处理LAN模式的RPC消息
        public void ProcessLANMessage(long senderConnectionId, NetPacketReader reader, NetPeer senderPeer = null)
        {
            if (reader == null || reader.AvailableBytes < 3)
                return;

            if (senderPeer != null)
            {
                _connectionIdToPeer[senderConnectionId] = senderPeer;
            }

            byte messageType = reader.GetByte();
            if (messageType != RPC_MESSAGE_TYPE)
                return;

            ushort rpcId = reader.GetUShort();
            byte flags = reader.GetByte();
            RPCTarget target = (RPCTarget)(flags & 0x7F);
            bool hasMessageId = (flags & 0x80) != 0;
            
            string rpcName = _rpcIdToName.TryGetValue(rpcId, out string name) ? name : $"RPC_{rpcId}";
            // Debug.Log($"[HybridRPCManager-LAN] ProcessLANMessage: rpc={rpcName}, sender={senderConnectionId}, target={target}, hasMsg={hasMessageId}");

            uint messageId = 0;
            if (hasMessageId)
            {
                messageId = reader.GetUInt();
                
                if (!_reliabilityManager.ShouldProcessMessage(messageId, senderConnectionId))
                {
                    Debug.Log($"[HybridRPCManager-LAN] Duplicate message {messageId} from {senderConnectionId}, skipping");
                    return;
                }
                
                _reliabilityManager.SendAck(messageId, senderConnectionId);
            }

            if (IsServer && target != RPCTarget.Server)
            {
                NetDataReader dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
                ForwardLANMessage(target, senderConnectionId, rpcId, flags, messageId, dataReader);
                return;
            }

            if (_rpcHandlers.TryGetValue(rpcId, out RPCHandler handler))
            {
                // Debug.Log($"[HybridRPCManager-LAN] Invoking handler for '{rpcName}' from {senderConnectionId}");
                handler?.Invoke(senderConnectionId, reader);
            }
            else
            {
                Debug.LogWarning($"[HybridRPCManager-LAN] No handler for RPC '{rpcName}' (ID {rpcId})");
            }
        }
        
        private void ForwardLANMessage(RPCTarget target, long senderConnectionId, ushort rpcId, byte flags, uint messageId, NetDataReader dataReader)
        {
            var netService = NetService.Instance;
            if (netService == null || netService.netManager == null)
                return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(RPC_MESSAGE_TYPE);
            writer.Put(rpcId);
            writer.Put(flags);
            
            bool hasMessageId = (flags & 0x80) != 0;
            if (hasMessageId)
            {
                writer.Put(messageId);
            }
            
            int remainingBytes = dataReader.AvailableBytes;
            if (remainingBytes > 0)
            {
                byte[] data = new byte[remainingBytes];
                dataReader.GetBytes(data, remainingBytes);
                writer.Put(data);
            }

            netService.netManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            Debug.Log($"[HybridRPCManager-LAN] Forwarded RPC {rpcId} from {senderConnectionId} to all clients");
        }

        private void ProcessServerMessages()
        {
            while (_steamTransport.ServerReceive(out NetworkEventData eventData))
            {
                switch (eventData.Type)
                {
                    case NetworkEventType.Data:
                        HandleRPCMessage(eventData.ConnectionId, eventData.Data);
                        break;

                    case NetworkEventType.Connect:
                        Debug.Log($"[HybridRPCManager] Steam client connected: {eventData.ConnectionId}");
                        break;

                    case NetworkEventType.Disconnect:
                        Debug.Log($"[HybridRPCManager] Steam client disconnected: {eventData.ConnectionId}, reason: {eventData.DisconnectReason}");
                        break;

                    case NetworkEventType.Error:
                        Debug.LogError($"[HybridRPCManager] Steam server error: {eventData.ErrorMessage}");
                        break;
                }
            }
        }

        private void ProcessClientMessages()
        {
            while (_steamTransport.ClientReceive(out NetworkEventData eventData))
            {
                switch (eventData.Type)
                {
                    case NetworkEventType.Data:
                        HandleRPCMessage(eventData.ConnectionId, eventData.Data);
                        break;

                    case NetworkEventType.Connect:
                        Debug.Log($"[HybridRPCManager] Connected to Steam server: {eventData.ConnectionId}");
                        break;

                    case NetworkEventType.Disconnect:
                        Debug.Log($"[HybridRPCManager] Disconnected from Steam server: {eventData.ConnectionId}, reason: {eventData.DisconnectReason}");
                        break;

                    case NetworkEventType.Error:
                        Debug.LogError($"[HybridRPCManager] Steam client error: {eventData.ErrorMessage}");
                        break;
                }
            }
        }

        private void HandleRPCMessage(long senderConnectionId, byte[] data)
        {
            if (data == null || data.Length < 3)
            {
                Debug.LogWarning("[HybridRPCManager] Received invalid RPC message");
                return;
            }

            try
            {
                NetDataReader reader = new NetDataReader(data);
                byte messageType = reader.GetByte();

                if (messageType != RPC_MESSAGE_TYPE)
                {
                    Debug.LogWarning($"[HybridRPCManager] Invalid message type: {messageType}");
                    return;
                }

                ushort rpcId = reader.GetUShort();
                byte flags = reader.GetByte();
                RPCTarget target = (RPCTarget)(flags & 0x7F); // 低7位是target
                bool hasMessageId = (flags & 0x80) != 0; // 最高位是messageId标志

                if (IsServer && target != RPCTarget.Server)
                {
                    long targetConnectionId = -1;
                    if (target == RPCTarget.TargetClient || target == RPCTarget.AllClientsExceptSender)
                    {
                        targetConnectionId = reader.GetLong();
                    }

                    NetDataWriter writer = new NetDataWriter();
                    writer.Put(RPC_MESSAGE_TYPE);
                    writer.Put(rpcId);
                    writer.Put(flags);
                    
                    int remainingBytes = reader.AvailableBytes;
                    if (remainingBytes > 0)
                    {
                        byte[] remainingData = new byte[remainingBytes];
                        reader.GetBytes(remainingData, remainingBytes);
                        writer.Put(remainingData);
                    }

                    byte[] forwardData = writer.CopyData();
                    SendSteamRPCAsServer(target, senderConnectionId, forwardData, DeliveryMethod.ReliableOrdered);
                    return;
                }

                uint messageId = 0;
                if (hasMessageId)
                {
                    messageId = reader.GetUInt();
                    
                    if (!_reliabilityManager.ShouldProcessMessage(messageId, senderConnectionId))
                    {
                        return; // 重复消息，跳过
                    }
                    
                    _reliabilityManager.SendAck(messageId, senderConnectionId);
                }

                if (_rpcHandlers.TryGetValue(rpcId, out RPCHandler handler))
                {
                    string rpcName = _rpcIdToName.TryGetValue(rpcId, out string name) ? name : $"RPC_{rpcId}";
                    // Debug.Log($"[HybridRPCManager] Invoking RPC '{rpcName}' from {senderConnectionId}" + (hasMessageId ? $" (msgId={messageId})" : ""));
                    handler?.Invoke(senderConnectionId, reader);
                }
                else
                {
                    Debug.LogWarning($"[HybridRPCManager] No handler for RPC ID {rpcId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HybridRPCManager] Error handling RPC message: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private Dictionary<long, object> GetAllServerConnections()
        {
            var connections = new Dictionary<long, object>();
            
            if (_steamTransport == null || !_steamTransport.IsServerStarted)
                return connections;

            for (int i = 0; i < _steamTransport.ServerPeersCount; i++)
            {
                connections.Add(i, null);
            }

            return connections;
        }

        private void OnDestroy()
        {
            _rpcHandlers.Clear();
            _rpcNameToId.Clear();
            _rpcIdToName.Clear();

            if (Instance == this)
            {
                Instance = null;
            }

            Debug.Log("[HybridRPCManager] Destroyed");
        }
    }
}

