using Steamworks;

namespace EscapeFromDuckovCoopMod;

// Keeps native message ownership and Steam callbacks out of the game/RPC layer.
internal interface ISteamSocketsApi : IDisposable
{
    bool Initialize(Action<SteamConnectionChange> callback);
    uint Listen();
    uint Connect(ulong steamId);
    EResult Accept(uint connection);
    bool Attach(uint connection);
    void Close(uint connection, string reason);
    EResult Send(uint connection, byte[] data, int length, int flags);
    bool Receive(Action<uint, byte[], int> dispatch);
    bool GetStatus(uint connection, out SteamNetConnectionRealTimeStatus_t status);
}

internal readonly struct SteamConnectionChange
{
    public SteamConnectionChange(uint handle, uint listenSocket, ulong steamId, ESteamNetworkingConnectionState state, string reason)
    {
        Handle = handle;
        ListenSocket = listenSocket;
        SteamId = steamId;
        State = state;
        Reason = reason;
    }
    public uint Handle { get; }
    public uint ListenSocket { get; }
    public ulong SteamId { get; }
    public ESteamNetworkingConnectionState State { get; }
    public string Reason { get; }
}
