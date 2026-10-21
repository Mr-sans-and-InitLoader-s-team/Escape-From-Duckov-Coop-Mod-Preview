using System.Buffers;
using System.Runtime.InteropServices;
using Steamworks;

namespace EscapeFromDuckovCoopMod;

internal sealed class NativeSteamSocketsApi : ISteamSocketsApi
{
    private const int VirtualPort = 9050;
    private HSteamNetPollGroup _group;
    private HSteamListenSocket _listen;
    private Callback<SteamNetConnectionStatusChangedCallback_t> _callback;
    private readonly IntPtr[] _messages = new IntPtr[1];

    public bool Initialize(Action<SteamConnectionChange> callback)
    {
        _callback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(change => callback(new SteamConnectionChange(
            change.m_hConn.m_HSteamNetConnection, change.m_info.m_hListenSocket.m_HSteamListenSocket,
            change.m_info.m_identityRemote.GetSteamID64(), change.m_info.m_eState,
            $"{change.m_info.m_eEndReason}: {change.m_info.m_szEndDebug}")));
        SteamNetworkingUtils.InitRelayNetworkAccess();
        _group = SteamNetworkingSockets.CreatePollGroup();
        return _group.m_HSteamNetPollGroup != 0;
    }

    public uint Listen()
    {
        _listen = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, null);
        return _listen.m_HSteamListenSocket;
    }
    public uint Connect(ulong steamId)
    {
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(steamId);
        return SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, 0, null).m_HSteamNetConnection;
    }
    public EResult Accept(uint connection) => SteamNetworkingSockets.AcceptConnection(new HSteamNetConnection(connection));
    public bool Attach(uint connection) => SteamNetworkingSockets.SetConnectionPollGroup(new HSteamNetConnection(connection), _group);
    public void Close(uint connection, string reason) => SteamNetworkingSockets.CloseConnection(new HSteamNetConnection(connection), 1000, reason, false);

    public EResult Send(uint connection, byte[] data, int length, int flags)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            return SteamNetworkingSockets.SendMessageToConnection(new HSteamNetConnection(connection), pinned.AddrOfPinnedObject(), (uint)length, flags, out _);
        }
        finally { pinned.Free(); }
    }

    public bool Receive(Action<uint, byte[], int> dispatch)
    {
        if (SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_group, _messages, 1) <= 0) return false;
        var pointer = _messages[0];
        byte[] buffer = null;
        try
        {
            var message = SteamNetworkingMessage_t.FromIntPtr(pointer);
            if (message.m_cbSize < SteamPacketCodec.HeaderSize || message.m_cbSize > SteamPacketCodec.MaxMessageSize)
            {
                dispatch(message.m_conn.m_HSteamNetConnection, Array.Empty<byte>(), 0);
                return true;
            }
            buffer = ArrayPool<byte>.Shared.Rent(message.m_cbSize);
            Marshal.Copy(message.m_pData, buffer, 0, message.m_cbSize);
            dispatch(message.m_conn.m_HSteamNetConnection, buffer, message.m_cbSize);
            return true;
        }
        finally
        {
            SteamNetworkingMessage_t.Release(pointer);
            if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public bool GetStatus(uint connection, out SteamNetConnectionRealTimeStatus_t status)
    {
        status = default;
        var lane = default(SteamNetConnectionRealTimeLaneStatus_t);
        return SteamNetworkingSockets.GetConnectionRealTimeStatus(new HSteamNetConnection(connection), ref status, 0, ref lane) == EResult.k_EResultOK;
    }
    public void Dispose()
    {
        _callback?.Dispose();
        _callback = null;
        if (_listen.m_HSteamListenSocket != 0) SteamNetworkingSockets.CloseListenSocket(_listen);
        if (_group.m_HSteamNetPollGroup != 0) SteamNetworkingSockets.DestroyPollGroup(_group);
        _listen = default;
        _group = default;
    }
}
