// A scripted uo1998 for a real GameLink (docs/Designs/Active/Apps/AssistTesting.md, instruments): the client side and
// the server side of one game connection on in-memory pipes, speaking the 1.25.32 wire (the client keystream up, the
// server Huffman code down). Answer is asked for the server's reply to each packet the server receives.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;

namespace Uoaix.Assist.Tests;

public sealed class MemoryLog : IRelayLog
{
    readonly Lock m_gate = new();
    readonly List<string> m_lines = new();

    public ImmutableArray<string> Lines
    {
        get
        {
            lock (m_gate)
                return m_lines.ToImmutableArray();
        }
    }

    public void Line(int id, string dir, string text)
    {
        lock (m_gate)
            m_lines.Add("c" + id + " " + dir + " " + text);
    }

    public string? Crash(string why)
    {
        Line(0, "--", "CRASH " + why);
        return null;
    }
}

// The link's clock, ahead of the virtual clock by Skew: a watcher that adds to Skew is a relay pass that long, which
// the virtual clock cannot express (its time waits while a pipe's reader is busy).
public sealed class SkewClock : IClock
{
    readonly VirtualClock m_clock;
    volatile int m_skewMs;

    public SkewClock(VirtualClock clock)
    {
        m_clock = clock;
    }

    public void Skew(int ms)
    {
        m_skewMs += ms;
    }

    public DateTime UtcNow => m_clock.UtcNow.AddMilliseconds(m_skewMs);

    public void Sleep(int ms) => m_clock.Sleep(ms);

    public bool Wait(ManualResetEventSlim signal, int ms) => m_clock.Wait(signal, ms);
}

// The piles a test's shard keeps on the ground (serial to x, y, amount and, in some suites, art), shared by the
// shard's answers and the case.
public sealed class Ground
{
    public Map<uint, int[]> Piles = Map<uint, int[]>.Empty;
}

public sealed class FakeShard
{
    public const uint Seed = 0x7F000001;

    static readonly Lazy<WireTables> s_wire = new(() => WireTables.Load(
        Environment.GetEnvironmentVariable("UOAIX_DIR")
            ?? throw new InvalidOperationException("UOAIX_DIR is not set")));

    readonly Lock m_gate = new();
    readonly BytePipe m_toLinkFromServer;
    readonly BytePipe m_toLinkFromClient;
    readonly UoKeystream m_serverDecrypt = new(Seed);
    readonly UoKeystream m_clientEncrypt = new(Seed);
    readonly UoFramer m_serverFramer;
    readonly UoHuffman.Decoder m_clientDecoder;
    readonly UoHuffman m_huff;
    readonly List<byte[]> m_serverGot = new();
    readonly List<DateTime> m_serverGotAt = new();
    readonly List<byte[]> m_clientGot = new();
    Thread? m_linkThread;

    public FakeShard(VirtualClock clock, IClock? linkClock = null)
    {
        Clock = clock;
        m_huff = new UoHuffman(Copy(Wire.Book));
        m_serverFramer = new UoFramer(Copy(Wire.Lengths));
        m_clientDecoder = m_huff.NewDecoder();
        m_toLinkFromServer = new BytePipe(clock);
        m_toLinkFromClient = new BytePipe(clock);
        Log = new MemoryLog();
        Stream clientSide = new PipeStream(m_toLinkFromClient, OnClientBytes, OnClose);
        Stream serverSide = new PipeStream(m_toLinkFromServer, OnServerBytes, OnClose);
        Link = new GameLink(1, clientSide, serverSide, Seed, Copy(Wire.Lengths), m_huff, Log, linkClock ?? clock,
            () => Disabled!);
    }

    // What the link's fail-safe answers: why the assist is disabled, or null.
    public string? Disabled
    {
        get; set;
    }

    // A close of either side throws, as a socket already torn down can.
    public bool CloseThrows
    {
        get; set;
    }

    static int[] Copy(ImmutableArray<int> values)
    {
        int[] copy = new int[values.Length];
        values.CopyTo(copy);
        return copy;
    }

    public static WireTables Wire => s_wire.Value;

    public VirtualClock Clock
    {
        get;
    }

    public GameLink Link
    {
        get;
    }

    public MemoryLog Log
    {
        get;
    }

    // The server's replies to a packet it received; none when null.
    public Func<byte[], ImmutableArray<byte[]>>? Answer
    {
        get; set;
    }

    public ImmutableArray<byte[]> ServerGot
    {
        get
        {
            lock (m_gate)
                return m_serverGot.ToImmutableArray();
        }
    }

    // The virtual time each packet in ServerGot reached the server.
    public ImmutableArray<DateTime> ServerGotAt
    {
        get
        {
            lock (m_gate)
                return m_serverGotAt.ToImmutableArray();
        }
    }

    // Runs act on a second actor once afterMs of virtual time have passed; the returned thread is joined by Joined.
    public Thread Also(int afterMs, Action act)
    {
        Clock.Join();
        Thread actor = new(() =>
        {
            try
            {
                Clock.Sleep(afterMs);
                act();
            }
            finally
            {
                Clock.Leave();
            }
        });
        actor.IsBackground = true;
        actor.Start();
        return actor;
    }

    public void Joined(Thread actor)
    {
        Clock.Outside(() => actor.Join());
    }

    public ImmutableArray<byte[]> ClientGot
    {
        get
        {
            lock (m_gate)
                return m_clientGot.ToImmutableArray();
        }
    }

    public void Start()
    {
        m_linkThread = new Thread(Link.Run);
        m_linkThread.IsBackground = true;
        m_linkThread.Start();
    }

    // Returns when the link's Run has returned, letting virtual time move meanwhile (called by an actor).
    public void WaitEnded()
    {
        Clock.Outside(() => m_linkThread!.Join());
    }

    // Both directions closed; returns when the link has ended.
    public void Stop()
    {
        m_toLinkFromClient.Close();
        m_toLinkFromServer.Close();
        m_linkThread?.Join();
    }

    public void ServerSends(byte[] packet)
    {
        byte[] coded = m_huff.Encode(packet);
        ServerSendsRaw(coded);
    }

    // Bytes on the server's wire as they are, coded or not.
    public void ServerSendsRaw(byte[] bytes)
    {
        m_toLinkFromServer.Write(bytes, 0, bytes.Length);
    }

    // The client's socket reset by the far side: the link's next client read throws.
    public void ResetClient(string message)
    {
        m_toLinkFromClient.Fail(message);
    }

    // The server's socket gone: every write the link makes to it throws.
    public bool ServerRefusesWrites
    {
        get; set;
    }

    // The client's socket gone: every write the link makes to it throws.
    public bool ClientRefusesWrites
    {
        get; set;
    }

    // The server's socket reset by the far side: the link's next server read throws.
    public void ResetServer(string message)
    {
        m_toLinkFromServer.Fail(message);
    }

    // The bytes for count symbols of 0x00 with no end symbol: a unit that never ends.
    public byte[] Unended(int count)
    {
        int bits = Wire.Book[0];
        int code = Wire.Book[1];
        List<byte> bytes = new();
        int acc = 0;
        int n = 0;
        for (int s = 0; s < count; s++)
        {
            for (int b = bits - 1; b >= 0; b--)
            {
                acc = (acc << 1) | ((code >> b) & 1);
                if (++n == 8)
                {
                    bytes.Add((byte)acc);
                    acc = 0;
                    n = 0;
                }
            }
        }
        return bytes.ToArray();
    }

    // The player's own client sending a packet, encrypted as CLIENT.EXE encrypts it.
    public void ClientSends(byte[] packet)
    {
        ClientSends(packet, 0, packet.Length);
    }

    // Part of a packet: the client's stream carries no boundaries.
    public void ClientSends(byte[] packet, int offset, int count)
    {
        byte[] coded = new byte[count];
        lock (m_gate)
        {
            for (int i = 0; i < count; i++)
                coded[i] = m_clientEncrypt.Apply(packet[offset + i]);
        }
        m_toLinkFromClient.Write(coded, 0, coded.Length);
    }

    public void CloseClient()
    {
        m_toLinkFromClient.Close();
    }

    public void CloseServer()
    {
        m_toLinkFromServer.Close();
    }

    // Every byte the server and the client got, framed or not.
    public int ServerBytes
    {
        get; private set;
    }

    public int ClientBytes
    {
        get; private set;
    }

    void OnServerBytes(byte[] buffer, int offset, int count)
    {
        if (ServerRefusesWrites)
            throw new IOException("the server's socket is gone");
        byte[] plain = new byte[count];
        List<byte[]> packets = new();
        lock (m_gate)
        {
            ServerBytes += count;
            for (int i = 0; i < count; i++)
                plain[i] = m_serverDecrypt.Apply(buffer[offset + i]);
            m_serverFramer.Feed(plain, 0, count, p => packets.Add(p));
            m_serverGot.AddRange(packets);
            foreach (byte[] p in packets)
                m_serverGotAt.Add(Clock.UtcNow);
        }
        foreach (byte[] p in packets)
        {
            if (Answer is null)
                continue;
            foreach (byte[] reply in Answer(p))
                ServerSends(reply);
        }
    }

    void OnClose()
    {
        if (CloseThrows)
            throw new IOException("the socket is already torn down");
    }

    void OnClientBytes(byte[] buffer, int offset, int count)
    {
        if (ClientRefusesWrites)
            throw new IOException("the client's socket is gone");
        lock (m_gate)
        {
            ClientBytes += count;
            m_clientDecoder.Feed(buffer, offset, count, (unit, end) => m_clientGot.Add(unit));
        }
    }
}
