using System.Diagnostics;
using EscapeFromDuckovCoopMod;
using Steamworks;

static class Program
{
    private const ESteamNetworkingConnectionState NativeConnected = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
    private const ESteamNetworkingConnectionState NativeConnecting = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting;
    private static int _tests;

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Test(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
        _tests++;
    }
    static byte[] Frame(SteamPacketKind kind, DeliveryMethod delivery, uint sequence, byte[] payload)
    {
        var buffer = new byte[SteamPacketCodec.HeaderSize + payload.Length];
        SteamPacketCodec.WriteHeader(buffer, kind, delivery, sequence);
        payload.CopyTo(buffer, SteamPacketCodec.HeaderSize);
        return buffer;
    }
    static byte[] Handshake(string version, SteamPacketKind kind = SteamPacketKind.Accept)
    {
        var writer = new NetDataWriter();
        writer.Put(version);
        return Frame(kind, DeliveryMethod.ReliableOrdered, 1, writer.CopyData());
    }
    static (SteamSocketsTransport Transport, FakeSteamApi Api, TestListener Listener) Client(bool ready = true, Func<double> clock = null)
    {
        var api = new FakeSteamApi();
        var listener = new TestListener();
        var transport = new SteamSocketsTransport(listener, false, "test-version", _ => false, api, clock ?? (() => 0));
        Check(transport.Start(), "client start");
        transport.Connect(123);
        api.Change(1, NativeConnected);
        transport.PollEvents();
        if (ready)
        {
            api.Incoming.Enqueue((1, Handshake("test-version")));
            transport.PollEvents();
            Check(listener.Connected.Count == 1, "client ready");
        }
        return (transport, api, listener);
    }
    static void Main()
    {
        Test("reader respects payload offset and length", () =>
        {
            var reader = new CoopPacketReader(new byte[] { 1, 2, 3, 4, 5 }, 2, 2);
            Check(reader.AvailableBytes == 2 && reader.GetByte() == 3 && reader.GetByte() == 4 && reader.AvailableBytes == 0, "reader bounds");
        });
        Test("handshake gates connected callback and protocol rejects invalid version", () =>
        {
            var (transport, api, listener) = Client(false);
            Check(listener.Connected.Count == 0 && api.Sent.Count == 1, "premature connection");
            api.Incoming.Enqueue((1, Handshake("old-version")));
            transport.PollEvents();
            Check(listener.Connected.Count == 0 && listener.Disconnected.Count == 1 && api.Closed.Contains(1), "version not rejected");
            transport.Stop();
        });
        Test("native callbacks dispatch on poll thread", () =>
        {
            var api = new FakeSteamApi();
            var listener = new TestListener();
            var transport = new SteamSocketsTransport(listener, false, "test-version", _ => false, api, () => 0);
            transport.Start(); transport.Connect(123);
            Task.Run(() => api.Change(1, NativeConnected)).GetAwaiter().GetResult();
            Check(api.Sent.Count == 0, "callback sent from wrong thread");
            api.Incoming.Enqueue((1, Handshake("test-version")));
            transport.PollEvents();
            Check(listener.CallbackThread == Environment.CurrentManagedThreadId, "game callback thread");
            transport.Stop();
        });
        Test("host accepts only its lobby and listen socket", () =>
        {
            var api = new FakeSteamApi(); var listener = new TestListener();
            var transport = new SteamSocketsTransport(listener, true, "test-version", id => id == 123, api, () => 0);
            transport.Start();
            api.Change(1, NativeConnecting, 123, 88); // other application socket
            api.Change(2, NativeConnecting, 456, 99); // not a lobby member
            api.Change(3, NativeConnecting, 123, 99);
            api.Change(3, NativeConnected, 123, 99);
            api.Incoming.Enqueue((3, Handshake("test-version", SteamPacketKind.Hello)));
            transport.PollEvents();
            Check(!api.Closed.Contains(1) && api.Closed.Contains(2), "socket/lobby filtering");
            Check(listener.Connected.Single().SteamId == 123 && listener.Connected[0].EndPoint.ToString() == "Steam:123", "Steam identity");
            Check(api.Sent.Single().Data[2] == (byte)SteamPacketKind.Accept, "missing handshake acceptance");
            transport.Stop();
        });
        Test("delivery flags are explicit; payloads are not merged or changed", () =>
        {
            var (transport, api, listener) = Client();
            var peer = listener.Connected.Single(); api.Sent.Clear();
            foreach (DeliveryMethod delivery in Enum.GetValues<DeliveryMethod>())
                peer.Send(new byte[] { 99, 10, 20, 88 }, 1, 2, delivery);
            Check(api.Sent.Count == 5, "unexpected merge");
            foreach (var sent in api.Sent)
            {
                Check(SteamPacketCodec.TryRead(sent.Data, sent.Data.Length, out _, out var delivery, out _), "bad header");
                Check(sent.Data.Skip(8).SequenceEqual(new byte[] { 10, 20 }), "payload altered");
                var expected = delivery is DeliveryMethod.Unreliable or DeliveryMethod.Sequenced ? 5 : 9;
                Check(sent.Flags == expected, "wrong Steam reliability flag");
            }
            transport.Stop();
        });
        Test("sequenced messages reject late and duplicate data with wraparound", () =>
        {
            var (transport, api, listener) = Client();
            foreach (var seq in new uint[] { uint.MaxValue, 1, 0, 1, 2 })
                api.Incoming.Enqueue((1, Frame(SteamPacketKind.Data, DeliveryMethod.Sequenced, seq, new byte[] { 7 })));
            transport.PollEvents();
            Check(listener.Received.Count == 3 && api.Released == 6, "sequence or buffer release");
            transport.Stop();
        });
        Test("receive budget preserves pending reliable packets", () =>
        {
            var (transport, api, listener) = Client();
            for (var i = 0; i < 600; i++) api.Incoming.Enqueue((1, Frame(SteamPacketKind.Data, DeliveryMethod.ReliableOrdered, 0, BitConverter.GetBytes(i))));
            transport.PollEvents();
            Check(listener.Received.Count == 512 && api.Incoming.Count == 88, "receive budget");
            transport.PollEvents();
            Check(listener.Received.Count == 600 && BitConverter.ToInt32(listener.Received.Last()) == 599, "reliable packet loss/order");
            transport.Stop();
        });
        Test("real-time congestion drops; reliable congestion fails explicitly", () =>
        {
            var (transport, api, listener) = Client(); var peer = listener.Connected.Single();
            api.SendResult = EResult.k_EResultIgnored;
            peer.Send(new byte[] { 1 }, DeliveryMethod.Unreliable);
            transport.PollEvents();
            Check(listener.Disconnected.Count == 0, "unreliable congestion disconnected");
            api.SendResult = EResult.k_EResultLimitExceeded;
            peer.Send(new byte[] { 2 }, DeliveryMethod.ReliableOrdered);
            transport.PollEvents();
            Check(listener.Disconnected.Count == 1 && api.Closed.Count == 1 && transport.ConnectedPeersCount == 0, "reliable loss was silent");
            transport.Stop();
        });
        Test("handshake timeout, stop and new connection release state", () =>
        {
            double now = 0;
            var (transport, api, listener) = Client(false, () => now);
            now = 21; transport.PollEvents();
            Check(listener.Disconnected.Count == 1, "handshake timeout");
            transport.Connect(123); api.Change(2, NativeConnected);
            api.Incoming.Enqueue((2, Handshake("test-version"))); transport.PollEvents();
            Check(listener.Connected.Count == 1, "reconnect failed");
            transport.Stop();
            Check(api.Disposed && transport.ConnectedPeersCount == 0 && api.Closed.Contains(2), "stop leaks");
        });
        Test("handler exceptions and malformed frames release messages", () =>
        {
            var (transport, api, listener) = Client();
            listener.ThrowOnReceive = true;
            api.Incoming.Enqueue((1, Frame(SteamPacketKind.Data, DeliveryMethod.ReliableOrdered, 1, new byte[] { 3 })));
            transport.PollEvents();
            Check(api.Released == 2 && listener.Disconnected.Count == 0 && api.Closed.Count == 0, "application error disconnected peer");
            listener.ThrowOnReceive = false;
            api.Incoming.Enqueue((1, Frame(SteamPacketKind.Data, DeliveryMethod.ReliableOrdered, 2, new byte[] { 4 })));
            transport.PollEvents();
            Check(api.Released == 3 && listener.Received.Single().SequenceEqual(new byte[] { 4 }) && transport.ConnectedPeersCount == 1, "receive did not recover after application error");
            transport.Stop();
            (transport, api, listener) = Client();
            api.Incoming.Enqueue((1, new byte[] { 0 })); transport.PollEvents();
            Check(api.Released == 2 && listener.Disconnected.Count == 1, "malformed packet leak");
            transport.Stop();
        });
        Test("native attach failure reports failed connect", () =>
        {
            var api = new FakeSteamApi { AttachResult = false }; var listener = new TestListener();
            var transport = new SteamSocketsTransport(listener, false, "v", _ => false, api, () => 0);
            transport.Start(); transport.Connect(123); transport.PollEvents();
            Check(listener.Disconnected.Count == 1 && api.Closed.Count == 1, "attach failure stuck connecting");
            transport.Stop();
        });
        Test("real LiteNetLib loopback supports reliable fragmented payload and disconnect", DirectLoopback);
        Console.WriteLine($"{_tests} network transport tests passed.");
    }

    static void DirectLoopback()
    {
        var hostListener = new TestListener(); var clientListener = new TestListener();
        var host = new LiteNetTransport(hostListener); var client = new LiteNetTransport(clientListener);
        Check(host.Start() && client.Start(), "UDP bind");
        try
        {
            void Pump(Func<bool> done)
            {
                var timer = Stopwatch.StartNew();
                while (!done() && timer.ElapsedMilliseconds < 5000)
                {
                    host.PollEvents(); client.PollEvents(); Thread.Sleep(1);
                }
                Check(done(), "UDP loopback timeout");
            }
            var connect = new NetDataWriter(); connect.Put("test");
            client.Connect("127.0.0.1", host.LocalPort, connect);
            Pump(() => hostListener.Connected.Count == 1 && clientListener.Connected.Count == 1);
            var payload = Enumerable.Range(0, 20000).Select(i => (byte)i).ToArray();
            clientListener.Connected[0].Send(payload, DeliveryMethod.ReliableOrdered);
            Pump(() => hostListener.Received.Count == 1);
            Check(hostListener.Received[0].SequenceEqual(payload), "fragmented payload mismatch");
            hostListener.Connected[0].Send(new byte[] { 4, 5, 6 }, DeliveryMethod.ReliableOrdered);
            Pump(() => clientListener.Received.Count == 1);
            Check(clientListener.Received[0].SequenceEqual(new byte[] { 4, 5, 6 }), "return payload mismatch");
            clientListener.Connected[0].Disconnect();
            Pump(() => hostListener.Disconnected.Count == 1 && clientListener.Disconnected.Count == 1);
        }
        finally { client.Stop(); host.Stop(); }
    }
}
