using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace EscapeFromDuckovCoopMod
{
    public static class PacketSignature
    {
        // 🛡️ 修复：存储 DeliveryMethod 和通道号
        private struct PacketInfo
        {
            public DeliveryMethod Method;
            public byte Channel;
        }

        private static readonly ConcurrentDictionary<ulong, PacketInfo> _signatures =
            new ConcurrentDictionary<ulong, PacketInfo>();
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

        // 🛡️ 修复：注册时同时记录通道号
        public static void Register(byte[] data, int start, int length, DeliveryMethod deliveryMethod, byte channel)
        {
            if (data == null || length == 0)
                return;
            ulong signature = CalculateSignature(data, start, length);
            _signatures[signature] = new PacketInfo { Method = deliveryMethod, Channel = channel };
            _cleanupCounter++;
            if (_cleanupCounter >= CLEANUP_THRESHOLD)
            {
                Cleanup();
                _cleanupCounter = 0;
            }
        }

        // 兼容旧 API
        public static void Register(byte[] data, int start, int length, DeliveryMethod deliveryMethod)
        {
            Register(data, start, length, deliveryMethod, 0);
        }

        // 🛡️ 修复：返回通道号
        public static bool TryGetPacketInfo(byte[] data, int start, int length, out DeliveryMethod method, out byte channel)
        {
            method = DeliveryMethod.ReliableOrdered;
            channel = 0;

            if (data == null || length == 0)
                return false;

            ulong signature = CalculateSignature(data, start, length);
            if (_signatures.TryGetValue(signature, out PacketInfo info))
            {
                _signatures.TryRemove(signature, out _);
                method = info.Method;
                channel = info.Channel;
                return true;
            }
            return false;
        }

        // 兼容旧 API
        public static DeliveryMethod? TryGetDeliveryMethod(byte[] data, int start, int length)
        {
            if (TryGetPacketInfo(data, start, length, out DeliveryMethod method, out _))
                return method;
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
