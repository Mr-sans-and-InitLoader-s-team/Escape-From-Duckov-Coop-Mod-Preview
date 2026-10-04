using System.Buffers.Binary;
using Steamworks;

namespace EscapeFromDuckovCoopMod;

internal enum SteamPacketKind : byte { Hello = 1, Accept = 2, Data = 3 }

internal static class SteamPacketCodec
{
    public const byte ProtocolVersion = 2;
    public const int HeaderSize = 8;
    public const int MaxMessageSize = Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend;
    public const string LobbyProtocol = "steam-sockets-v2";

    public static void WriteHeader(byte[] buffer, SteamPacketKind kind, DeliveryMethod delivery, uint sequence)
    {
        buffer[0] = 0xDC;
        buffer[1] = ProtocolVersion;
        buffer[2] = (byte)kind;
        buffer[3] = (byte)delivery;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), sequence);
    }

    public static bool TryRead(byte[] buffer, int length, out SteamPacketKind kind, out DeliveryMethod delivery, out uint sequence)
    {
        kind = default;
        delivery = default;
        sequence = 0;
        if (length < HeaderSize || length > MaxMessageSize || length > buffer.Length || buffer[0] != 0xDC || buffer[1] != ProtocolVersion)
            return false;
        kind = (SteamPacketKind)buffer[2];
        delivery = (DeliveryMethod)buffer[3];
        if (kind < SteamPacketKind.Hello || kind > SteamPacketKind.Data || !Enum.IsDefined(typeof(DeliveryMethod), delivery)) return false;
        sequence = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4));
        return true;
    }

    public static bool IsSequenced(DeliveryMethod delivery) => delivery == DeliveryMethod.Sequenced || delivery == DeliveryMethod.ReliableSequenced;
    public static bool IsReliable(DeliveryMethod delivery) => delivery == DeliveryMethod.ReliableOrdered || delivery == DeliveryMethod.ReliableUnordered || delivery == DeliveryMethod.ReliableSequenced;
    public static int SendFlags(DeliveryMethod delivery) => IsReliable(delivery)
        ? Constants.k_nSteamNetworkingSend_ReliableNoNagle
        : Constants.k_nSteamNetworkingSend_UnreliableNoDelay;
    public static bool IsNewer(uint sequence, uint previous) => unchecked((int)(sequence - previous)) > 0;
}
