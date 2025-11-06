using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod.Net.Core
{
    public enum TransportType
    {
        Direct,
        SteamP2P
    }
    
    public interface INetworkTransport
    {
        TransportType Type { get; }
        bool IsInitialized { get; }
        bool IsServer { get; }
        bool IsClient { get; }
        bool IsConnected { get; }
        
        bool StartServer(int port);
        bool StartClient();
        bool Connect(string address, int port);
        void Disconnect();
        void Stop();
        
        void Send(NetDataWriter writer, DeliveryMethod method);
        void SendToAll(NetDataWriter writer, DeliveryMethod method);
        void SendToPeer(long connectionId, NetDataWriter writer, DeliveryMethod method);
        
        void PollEvents();
        
        event Action<long> OnPeerConnected;
        event Action<long> OnPeerDisconnected;
        event Action<long, NetDataReader> OnDataReceived;
        
        IEnumerable<long> GetConnectedPeers();
        float GetPing(long connectionId);
        string GetPeerAddress(long connectionId);
    }
    
    public static class NetworkTransportFactory
    {
        public static INetworkTransport Create(TransportType type)
        {
            switch (type)
            {
                case TransportType.SteamP2P:
                    return new SteamP2PTransport();
                case TransportType.Direct:
                default:
                    return new DirectConnectionTransport();
            }
        }
        
        public static INetworkTransport CreateAuto()
        {
            if (SteamManager.Initialized)
            {
                return new SteamP2PTransport();
            }
            return new DirectConnectionTransport();
        }
    }
}

