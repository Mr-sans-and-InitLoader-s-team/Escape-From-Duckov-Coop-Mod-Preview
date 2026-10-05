using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

/// <summary>The transport owns the buffer; readers are valid only during message dispatch.</summary>
public sealed class CoopPacketReader : NetDataReader
{
    public CoopPacketReader(byte[] data, int offset, int length) : base(data, offset, offset + length) { }

    // Existing RPC handlers call Recycle. Buffer ownership stays with the transport,
    // which releases it in finally even when a handler fails or returns early.
    public void Recycle() { }
}
