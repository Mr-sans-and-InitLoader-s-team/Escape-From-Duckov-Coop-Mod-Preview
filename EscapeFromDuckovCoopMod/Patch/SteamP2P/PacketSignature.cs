// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using LiteNetLib;
using System;
using System.Collections.Concurrent;

namespace EscapeFromDuckovCoopMod.Patch.SteamP2P
{
    public static class PacketSignature
    {
        private static readonly ConcurrentDictionary<ulong, DeliveryMethod> _signatures =
            new ConcurrentDictionary<ulong, DeliveryMethod>();
        private static int _cleanupCounter = 0;
        private const int CLEANUP_THRESHOLD = 10000;
        
        public static ulong CalculateSignature(byte[] data, int start, int length)
        {
            if (data == null || length == 0)
                return 0;
                
            ulong hash = (ulong)length;
            int bytesToHash = Math.Min(8, length);
            
            for (int i = 0; i < bytesToHash; i++)
            {
                int index = start + i;
                if (index < data.Length)
                {
                    hash = hash * 31 + data[index];
                }
            }
            
            return hash;
        }
        
        public static void Register(byte[] data, int start, int length, DeliveryMethod deliveryMethod)
        {
            if (data == null || length == 0)
                return;
                
            ulong signature = CalculateSignature(data, start, length);
            _signatures[signature] = deliveryMethod;
            
            _cleanupCounter++;
            if (_cleanupCounter >= CLEANUP_THRESHOLD)
            {
                Cleanup();
                _cleanupCounter = 0;
            }
        }
        
        public static DeliveryMethod? TryGetDeliveryMethod(byte[] data, int start, int length)
        {
            if (data == null || length == 0)
                return null;
                
            ulong signature = CalculateSignature(data, start, length);
            if (_signatures.TryGetValue(signature, out DeliveryMethod method))
            {
                _signatures.TryRemove(signature, out _);
                return method;
            }
            
            return null;
        }
        
        private static void Cleanup()
        {
            if (_signatures.Count > 1000)
            {
                _signatures.Clear();
            }
        }
        
        public static int GetSignatureCount()
        {
            return _signatures.Count;
        }
    }
}

