#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

// One game connection through the relay, and what the assist may put into it (assist-plan.md stages 2 and 3).

namespace Uoaix.Assist;

public sealed class GameLink
{
    public readonly int Id; public readonly AssistState State = new();
    readonly IRelayLog m_log; readonly int[] m_lengths; readonly UoHuffman m_huff; readonly uint m_seed;
    readonly Stream m_client, m_server; readonly IClock m_clock;
    // Why the fail-safe has disabled the assist, or null (AssistGuard).
    readonly Func<string> m_disabled;
    readonly object m_upGate = new(), m_downGate = new();
    readonly UoKeystream m_enc; readonly UoFramer m_upFramer;
    readonly Queue<byte[]> m_toServer = new();
    bool m_bytewise, m_raw, m_claimCursor;
    // Why injection stopped (rule 5); null while it may run.
    public volatile string Halted = null!;
    // Rule 3: one injected action in flight, cleared by the server's answer.
    public volatile string? InFlight = null; public DateTime InFlightAt; volatile uint m_inFlightUse;
    public const int AnswerMs = 3000;
    // The server's busy rule (OUO packet_handler.c 9446 and 3225, player.c 1246 and 4113): a double-click (0x06) or a
    // lift (0x07), from either side, makes the player busy for 4 ticks of 250 ms, 750 to 1000 ms by the tick's phase,
    // and refuses the next double-click or lift until then. ActionGapMs is measured over the uo1998 relay logs of
    // 2026-10-21 and 22: of 3122 such actions, the 15 refused all came under 1050 ms after the one before (the
    // highest 1000 to 1049, the network's jitter), and none of the 3086 at 1100 ms or more.
    public const int ActionGapMs = 1100, ActionPollMs = 10;
    DateTime m_lastActionAt = DateTime.MinValue;
    static bool IsAction(byte[] p)
    {
        return p[0] == 0x06 || p[0] == 0x07;
    }
    // When an action may go now: ActionGapMs after the last one either side sent, and never into a client packet
    // half sent (it would go out right behind it). Under upGate.
    DateTime ActionDue()
    {
        return m_upFramer.Buffered > 0 ? m_clock.UtcNow.AddMilliseconds(ActionPollMs)
            : m_lastActionAt.AddMilliseconds(ActionGapMs);
    }
    // A cursor opened by an injected use belongs to the assist: it is not shown to the client (rule 4).
    public volatile bool MacroCursor; public int CursorFlags;
    public volatile bool Closed;
    // Features watch whole packets here (after the state has read them), and may hold a client or server packet back;
    // a held server packet still reaches the watchers.
    public event Action<byte[]> ServerPacket = null!, ClientPacket = null!;
    public Func<byte[], bool> HoldClient = null!, HoldServer = null!;
    // The last lift went out from the assist, not the client: its refusal (0x27) is the assist's to read, and the 1.25
    // client, refused a lift it never made, crashed (access violation, 2026-10-17 05:17:09, a pickup lift refused out
    // of range; the Vesper buy circuit's crash at 01:37:55 was during bank lifts).
    public volatile bool AssistLift;
    // Rule 2: what the assist may send the server, each at the client table's length. Attack, use, lift, drop,
    // text command (skill and spell use), equip, target.
    // 0x34 is the status request the client sends when its status bar or skill gump opens.
    // 0x09 is the client's single click, which the server answers with the clicked mobile's name label.
    // 0x7D is the pick in an item menu (0x7C), the smithing loop's answer to the menu its hammer use opened.
    // 0x03 typed speech, 0x3B the buy window's answer and 0x9F the sell window's: the tailoring circuit's "vendor buy",
    // "vendor sell" and "bank" (assist-plan.md stage 6). 0x9A answers a text prompt: a rune's description.
    // 0x93 and 0x66 write a book's title and pages (Advertise).
    static readonly ImmutableArray<int> s_injectable = [ 0x02, 0x03, 0x05, 0x06, 0x07, 0x08, 0x09, 0x12, 0x13, 0x34,
        0x3B, 0x66,
        0x6C, 0x7D, 0x93, 0x9A, 0x9F ];
    // A server packet that answers an injected action (0x11 and 0x3A answer 0x34, 0x7C a tool's use).
    static readonly ImmutableArray<int> s_answers = [ 0x6C, 0x1C, 0x24, 0x25, 0x27, 0x2E, 0x1D, 0x3C, 0x11, 0x3A,
        0x7C ];
    // While set, an item menu (0x7C) goes to the assist's watchers and not to the client's screen.
    public volatile bool ClaimMenu;

    public GameLink(int id, Stream client, Stream server, uint seed, int[] lengths, UoHuffman huff, IRelayLog log,
        IClock clock, Func<string> disabled)
    {
        Id = id;
        m_client = client;
        m_server = server;
        m_seed = seed;
        m_lengths = lengths;
        m_huff = huff;
        m_log = log;
        m_clock = clock;
        m_disabled = disabled;
        State.Clock = clock;
        m_enc = new UoKeystream(seed);
        m_upFramer = new UoFramer(lengths);
    }

    public void Run()
    {
        Thread up = new(Up);
        up.IsBackground = true;
        up.Start();
        Down();
        up.Join(2000);
        Closed = true;
    }

    void Halt(string why)
    {
        if (Halted == null)
        {
            Halted = why;
            m_log.Line(Id, "--", "HALT " + why);
        }
    }

    // The 30-byte password fields: 0x80 and 0x91 the account's, 0x5D the character's, 0x83 (delete a character) the
    // account's.
    static byte[] Masked(byte[] p)
    {
        int from = p[0] == 0x91 ? 35 : p[0] == 0x80 ? 31 : p[0] == 0x5D ? 35 : p[0] == 0x83 ? 1 : -1;
        if (from < 0)
            return p;
        byte[] m = (byte[])p.Clone();
        int end = Math.Min(from + 30, m.Length);
        for (int i = from; i < end; i++)
            m[i] = 0;
        return m;
    }
    void ToServer(byte[] plain, int off, int n)
    {
        byte[] o = new byte[n];
        for (int i = 0; i < n; i++)
            o[i] = m_enc.Apply(plain[off + i]);
        m_server.Write(o, 0, n);
    }

    // Client to server: decrypted, framed, re-encrypted; queued injections go out between whole client packets.
    // Which side ended the connection first, logged once as "ENDED BY CLIENT" or "ENDED BY SERVER": a read that ends is
    // that side's close, a write that fails is the other side's. Closing one side ends the other loop with "aborted by
    // the software in your host machine", so the first END line alone does not say who left (a logout reads as a drop).
    int m_ended;
    void End(string by)
    {
        if (Interlocked.Exchange(ref m_ended, 1) == 0)
            m_log.Line(Id, "--", "ENDED BY " + by);
    }
    // Each subscriber runs on the relay thread: one that throws is logged and the rest run, never dropping the
    // connection.
    void Notify(Action<byte[]> ev, byte[] p, string dir, Pass pass)
    {
        if (ev == null)
            return;
        Delegate[] list = ev.GetInvocationList();
        for (int i = 0; i < list.Length; i++)
        {
            Action<byte[]> h = (Action<byte[]>)list[i];
            DateTime at = m_clock.UtcNow;
            try
            {
                h(p);
            }
            catch (Exception e) { m_log.Line(Id, "--",
                dir + " SUBSCRIBER FAILED on " + p[0].ToString("X2") + ": " + e.GetType().Name + " " + e.Message); }
            double ms = pass.Since(at);
            pass.Watch += ms;
            if (ms > pass.SlowestMs)
            {
                pass.SlowestMs = ms;
                pass.SlowestOp = p[0];
                pass.Slowest = (h.Method.DeclaringType == null ? "" : h.Method.DeclaringType.Name + ".")
                    + h.Method.Name;
            }
        }
    }
    // One read's packets through the relay, from the read's return to the last byte written on: where the time went, so
    // a pile of packets let through at once names what held it (Damian, 2026-10-23: "sometimes, it seems like a whole
    // pile of messages is getting hung up at the relay"). A pass of SlowMs or more is logged as RELAY SLOW.
    public const int SlowMs = 300;
    sealed class Pass
    {
        readonly IClock m_clock; public DateTime From; public int Packets;
        public double State, Watch, Hold, Gate, Write, SlowestMs; public string? Slowest = null; public int SlowestOp;
        public Pass(IClock clock)
        {
            m_clock = clock;
        }
        public double Since(DateTime at)
        {
            return (m_clock.UtcNow - at).TotalMilliseconds;
        }
        public void Start()
        {
            From = m_clock.UtcNow;
            Packets = 0;
            State = Watch = Hold = Gate = Write = SlowestMs = 0;
            Slowest = null;
            SlowestOp = 0;
        }
    }
    void Slow(string dir, Pass pass)
    {
        double total = pass.Since(pass.From);
        if (total < SlowMs)
            return;
        m_log.Line(Id, "--",
            "RELAY SLOW " + dir + " " + (int)total + " ms for " + pass.Packets + " packets: state lock "
            + (int)pass.State + " ms, watchers " + (int)pass.Watch + " ms"
            + (pass.Slowest == null ? ""
            : " (slowest " + pass.Slowest + " on " + pass.SlowestOp.ToString("X2") + ", " + (int)pass.SlowestMs
            + " ms)")
            + ", holds " + (int)pass.Hold + " ms, waiting on the " + (dir == "S>C" ? "client" : "server")
            + " side's lock " + (int)pass.Gate + " ms, socket write " + (int)pass.Write
            + " ms, the rest (the log, decoding) "
            + (int)(total - pass.State - pass.Watch - pass.Hold - pass.Gate - pass.Write) + " ms");
    }
    // The thread relaying the client's packets: a send it makes is a hold handler acting in the client's place
    // (HoldLoginSelfUse), sent at once as the client's own would have been; waiting there would hold upGate, and the
    // packet in hand still counts as buffered, so the wait would never end.
    volatile Thread m_upThread = null!;
    void Up()
    {
        m_upThread = Thread.CurrentThread;
        byte[] buf = new byte[8192];
        UoKeystream dec = new(m_seed);
        bool reading = false;
        Pass pass = new(m_clock);
        try
        {
            while (true)
            {
                reading = true;
                int n = m_client.Read(buf, 0, buf.Length);
                reading = false;
                if (n <= 0)
                {
                    End("CLIENT");
                    break;
                }
                pass.Start();
                for (int i = 0; i < n; i++)
                    buf[i] = dec.Apply(buf[i]);
                DateTime gateAt = m_clock.UtcNow;
                lock (m_upGate)
                {
                    pass.Gate = pass.Since(gateAt);
                    if (m_bytewise)
                    {
                        ToServer(buf, 0, n);
                        continue;
                    }
                    m_upFramer.Feed(buf, 0, n, p =>
                    {
                        pass.Packets++;
                        Func<byte[], bool> hold = HoldClient;
                        DateTime at = m_clock.UtcNow;
                        bool held = hold != null && hold(p);
                        pass.Hold += pass.Since(at);
                        if (held)
                        {
                            m_log.Line(Id, "C>S", UoHex.Of(Masked(p)) + " (held by the assist)");
                            return;
                        }
                        // The framer's table fixes 0x02 at 3 bytes, 0x21 at 8, 0x22 at 3 and 0x20 at 19 (WireTests).
                        if (p[0] == 0x02)
                            Renumber(p, false);
                        if (p[0] == 0x07)
                            AssistLift = false;
                        if (IsAction(p))
                            m_lastActionAt = m_clock.UtcNow;
                        at = m_clock.UtcNow;
                        State.Client(p);
                        pass.State += pass.Since(at);
                        m_log.Line(Id, "C>S", UoHex.Of(Masked(p)));
                        at = m_clock.UtcNow;
                        ToServer(p, 0, p.Length);
                        pass.Write += pass.Since(at);
                        Notify(ClientPacket, p, "C>S", pass);
                    });
                    if (m_upFramer.Desync != null)
                    {
                        m_bytewise = true;
                        byte[] rest = m_upFramer.TakeBuffered();
                        ToServer(rest, 0, rest.Length);
                        Halt("client packet unframed: " + m_upFramer.Desync + "; client bytes now pass re-encrypted");
                    }
                    DateTime drainAt = m_clock.UtcNow;
                    Drain();
                    pass.Write += pass.Since(drainAt);
                }
                Slow("C>S", pass);
            }
        }
        catch (Exception e)
        {
            End(reading ? "CLIENT" : "SERVER");
            m_log.Line(Id, "--", "C>S END " + e.Message);
            // The client's side reset rather than closed: CLIENT.EXE died (or was closed by hand). Its last packets are
            // kept.
            if (reading && e.Message.IndexOf("forcibly closed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string? p = m_log.Crash("the client's game connection was reset (" + State.Summary() + ")");
                m_log.Line(Id, "--", "CRASH FILE " + (p ?? "not written"));
            }
        }
        Closed = true;
        try
        {
            m_server.Close();
        }
        catch { }
    }
    void Drain()
    {
        while (m_toServer.Count > 0 && m_upFramer.Buffered == 0 && !m_bytewise)
        {
            byte[] p = m_toServer.Dequeue();
            if (p[0] == 0x02)
                Renumber(p, true);
            if (IsAction(p))
                m_lastActionAt = m_clock.UtcNow;
            State.Client(p);
            m_log.Line(Id, "INJ C>S", UoHex.Of(p));
            ToServer(p, 0, p.Length);
        }
    }

    // Server to client: Huffman units decoded, framed, re-encoded one packet a unit, so an injected unit always falls
    // between whole server packets. On a desync the rest passes raw.
    void Down()
    {
        byte[] buf = new byte[8192];
        UoHuffman.Decoder dec = m_huff.NewDecoder();
        UoFramer framer = new(m_lengths);
        List<byte> pending = new();
        MemoryStream outq = new();
        bool reading = false;
        Pass pass = new(m_clock);
        try
        {
            while (true)
            {
                reading = true;
                int n = m_server.Read(buf, 0, buf.Length);
                reading = false;
                if (n <= 0)
                {
                    End("SERVER");
                    break;
                }
                if (m_raw)
                {
                    lock (m_downGate)
                        m_client.Write(buf, 0, n);
                    continue;
                }
                pass.Start();
                int start = pending.Count, good = 0;
                for (int i = 0; i < n; i++)
                    pending.Add(buf[i]);
                dec.Feed(buf, 0, n, (u, end) =>
                {
                    if (framer.Desync != null)
                        return;
                    framer.Feed(u, 0, u.Length, p =>
                    {
                        pass.Packets++;
                        DateTime at = m_clock.UtcNow;
                        State.Server(p);
                        pass.State += pass.Since(at);
                        if (WalkAnswer(p))
                        {
                            m_log.Line(Id, "S>C", UoHex.Of(p) + " (assist step)");
                            // The player redrawn in the ack's place in the stream: an item the server sends as the step
                            // brings it into range reaches a client that has the player there, not a tile behind, where
                            // an item at the edge of view is dropped (the east tailor's door and sign, 2026-10-17).
                            byte[]? draw = ShowMode != 0 ? PlayerDraw(false) : null;
                            if (draw != null)
                            {
                                byte[] de = m_huff.Encode(draw);
                                outq.Write(de, 0, de.Length);
                                m_log.Line(Id, "INJ S>C", UoHex.Of(draw));
                            }
                            Notify(ServerPacket, p, "S>C", pass);
                            return;
                        }
                        // A door's use is answered by the door's own 0x1A (its new graphic), which Answers does not
                        // list: the walk to Rod stalled "busy: use 40001A90 is unanswered" after the door had opened
                        // (2026-10-19 22:53:05).
                        if (InFlight != null
                            && (IsAnswer(p)
                            || m_inFlightUse != 0 && p[0] == 0x1A && p.Length >= 7
                            && ((((uint)p[3] << 24) | ((uint)p[4] << 16) | ((uint)p[5] << 8) | p[6]) & 0x7FFFFFFF)
                            == m_inFlightUse))
                        {
                            m_log.Line(Id, "--", "ANSWERED " + InFlight + " by " + p[0].ToString("X2"));
                            InFlight = null;
                        }
                        // A claim lasts until its cursor comes or ClaimMs passes: an earlier line (a refusal of
                        // something else, the prompt) must not hand the cursor to the client.
                        if (m_claimCursor && m_clock.UtcNow > m_claimUntil)
                            m_claimCursor = false;
                        if (p[0] == 0x6C && m_claimCursor)
                        {
                            m_claimCursor = false;
                            MacroCursor = true;
                            CursorFlags = p[6];
                            m_log.Line(Id, "S>C", UoHex.Of(p) + " (assist cursor, not shown)");
                            Notify(ServerPacket, p, "S>C", pass);
                            return;
                        }
                        if (p[0] == 0x7C && ClaimMenu)
                        {
                            m_log.Line(Id, "S>C", UoHex.Of(p) + " (assist menu, not shown)");
                            Notify(ServerPacket, p, "S>C", pass);
                            return;
                        }
                        Func<byte[], bool> hold = HoldServer;
                        at = m_clock.UtcNow;
                        bool held = hold != null && hold(p);
                        pass.Hold += pass.Since(at);
                        if (held)
                        {
                            m_log.Line(Id, "S>C", UoHex.Of(p) + " (held by the assist)");
                            Notify(ServerPacket, p, "S>C", pass);
                            return;
                        }
                        m_log.Line(Id, "S>C", UoHex.Of(p));
                        byte[] e = m_huff.Encode(p);
                        outq.Write(e, 0, e.Length);
                        Notify(ServerPacket, p, "S>C", pass);
                    });
                    if (framer.Desync == null)
                        good = start + end;
                });
                // A packet is at most 65535 bytes, at most 11 code bits each: more undecoded bytes than that is no
                // packet.
                if (framer.Desync == null && pending.Count - good > MaxUnitBytes)
                    framer.Desync = "a server unit past " + MaxUnitBytes + " bytes undecoded";
                DateTime gateAt = m_clock.UtcNow;
                lock (m_downGate)
                {
                    pass.Gate = pass.Since(gateAt);
                    DateTime writeAt = m_clock.UtcNow;
                    if (outq.Length > 0)
                    {
                        m_client.Write(outq.GetBuffer(), 0, (int)outq.Length);
                        outq.SetLength(0);
                    }
                    pass.Write = pass.Since(writeAt);
                    if (framer.Desync != null)
                    {
                        m_raw = true;
                        byte[] rest = pending.ToArray();
                        m_client.Write(rest, good, rest.Length - good);
                        pending.Clear();
                        Halt("server packet unframed: " + framer.Desync + "; server bytes now pass raw");
                    }
                    else
                    {
                        pending.RemoveRange(0, good);
                    }
                }
                Slow("S>C", pass);
            }
        }
        catch (Exception e) { End(reading ? "SERVER" : "CLIENT"); m_log.Line(Id, "--", "S>C END " + e.Message); }
        Closed = true;
        try
        {
            m_client.Close();
        }
        catch { }
    }

    const int MaxUnitBytes = 65536 * 11 / 8 + 2;

    // Speech answers only when it is the system's or the player's own line, not a passer-by's.
    bool IsAnswer(byte[] p)
    {
        if (s_answers.IndexOf((int)p[0]) < 0)
            return false;
        if (p[0] != 0x1C || p.Length < 7)
            return true;
        uint who = ((uint)p[3] << 24) | ((uint)p[4] << 16) | ((uint)p[5] << 8) | p[6];
        uint me;
        lock (State.Gate)
            me = State.Player;
        return who == 0xFFFFFFFF || who == me;
    }
    static void Put32(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }
    static void Put16(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 8);
        b[at + 1] = (byte)v;
    }

    // Null when the packet went out (or was queued behind a partial client packet); otherwise why not.
    // A single click on a mobile, untracked: its answer is the mobile's own label, never one IsAnswer counts.
    public string? Look(uint serial)
    {
        byte[] p = new byte[5];
        p[0] = 0x09;
        Put32(p, 1, serial);
        return Inject(p, "look " + serial.ToString("X"), false);
    }
    public string? Inject(byte[] p, string action, bool track = true)
    {
        return Send(p, action, track, false);
    }
    // The client's own packet the assist held: sent whatever the player's state, as the client would have sent it.
    public string? Release(byte[] p, string action, bool track = true)
    {
        return Send(p, action, track, true);
    }
    // A double-click or a lift waits out ActionGapMs here (every refusal checked again after each wait); the rest go at
    // once.
    string? Send(byte[] p, string action, bool track, bool clients)
    {
        if (s_injectable.IndexOf((int)p[0]) < 0)
            return "opcode " + p[0].ToString("X2") + " is not one the assist sends";
        // Every Injectable opcode is inside the client's table (180 entries, 00 to B3).
        int len = m_lengths[p[0]];
        if (len == 0 ? (p.Length < 3 || ((p[1] << 8) | p[2]) != p.Length) : p.Length != len)
            return "length " + p.Length + " is not the client's for " + p[0].ToString("X2");
        while (true)
        {
            if (Closed)
                return "the connection is closed";
            // Nothing the assist starts goes out while the player is dead (Damian, 2026-10-15).
            if (!clients && State.Dead)
                return "the player is dead";
            string off = m_disabled();
            if (off != null)
                return "assist disabled by the fail-safe: " + off;
            int waitMs = 0;
            // An action unanswered after AnswerMs is let go for an untracked send as for a tracked one: a door's use,
            // which no packet in Answers answers, left every later step "busy" and ended the circuit (Moonglow,
            // 2026-10-17 09:53:06).
            lock (m_upGate)
            {
                // Read under the lock that a desync takes to set it, so a send racing the desync cannot slip past.
                if (Halted != null)
                    return "halted: " + Halted;
                string? busy = InFlight;
                if (busy != null)
                {
                    if ((m_clock.UtcNow - InFlightAt).TotalMilliseconds < AnswerMs)
                        return "busy: " + busy + " is unanswered";
                    m_log.Line(Id, "--", "UNANSWERED " + busy + " after " + AnswerMs + " ms");
                    InFlight = null;
                }
                if (IsAction(p) && Thread.CurrentThread != m_upThread)
                {
                    DateTime now = m_clock.UtcNow;
                    DateTime due = ActionDue();
                    if (now < due)
                        waitMs = (int)Math.Ceiling((due - now).TotalMilliseconds);
                }
                if (waitMs == 0)
                {
                    if (p[0] == 0x07)
                        AssistLift = !clients;
                    if (track)
                    {
                        InFlight = action;
                        InFlightAt = m_clock.UtcNow;
                        m_inFlightUse = p[0] == 0x06
                            ? (((uint)p[1] << 24) | ((uint)p[2] << 16) | ((uint)p[3] << 8) | p[4]) & 0x7FFFFFFF : 0;
                    }
                    m_toServer.Enqueue(p);
                    Drain();
                    return null;
                }
            }
            m_clock.Sleep(waitMs);
        }
    }

    // claim false leaves the cursor the use opens to the client: the first iteration of a loop, where the player picks.
    public string? Use(uint serial, bool claim = true)
    {
        byte[] p = new byte[5];
        p[0] = 0x06;
        Put32(p, 1, serial);
        return Claimed(p, "use " + serial.ToString("X"), claim);
    }
    // 0x05: an attack on a mobile, as the client sends a double-click on it in war mode. uo1998 answers with the
    // combatant (0xAA) and needs no war mode (OUO HandlePacket_ATTACK); 0xAA is no answer the link waits on, so
    // untracked.
    public string? Attack(uint serial)
    {
        byte[] p = new byte[5];
        p[0] = 0x05;
        Put32(p, 1, serial);
        return Inject(p, "attack " + serial.ToString("X"), false);
    }
    // 0x12 type 0x24 "<skill> 0": the client's skill-gump use (soak-bots.ps1 Skill-Use).
    public string? Skill(int skill, bool claim = true)
    {
        byte[] t = Encoding.ASCII.GetBytes(skill + " 0");
        byte[] p = new byte[5 + t.Length];
        p[0] = 0x12;
        Put16(p, 1, p.Length);
        p[3] = 0x24;
        t.CopyTo(p, 4);
        return Claimed(p, "skill " + skill, claim);
    }
    // 0x12 type 0x56 "<spell>" and a zero: the client's spellbook cast (uo1998 relay log 02:31:12, Poison "20"; OUO
    // packet_handler.c CastSpellByID).
    // A spell's cursor comes after its cast time, not a use's quick answer: Recall's came after the 3 s claim had
    // lapsed and went to the client (uo1998 2026-10-17 00:21:41), so a cast claims its cursor CastClaimMs.
    public const int CastClaimMs = 8000;
    public string? Cast(int spell, bool claim = true)
    {
        byte[] t = Encoding.ASCII.GetBytes(spell.ToString());
        byte[] p = new byte[5 + t.Length];
        p[0] = 0x12;
        Put16(p, 1, p.Length);
        p[3] = 0x56;
        t.CopyTo(p, 4);
        return Claimed(p, "cast " + spell, claim, CastClaimMs);
    }
    public const int ClaimMs = 3000; DateTime m_claimUntil;
    string? Claimed(byte[] p, string action, bool claim, int ms = ClaimMs)
    {
        m_claimUntil = m_clock.UtcNow.AddMilliseconds(ms);
        m_claimCursor = claim;
        MacroCursor = false;
        string? why = Inject(p, action);
        if (why != null)
            m_claimCursor = false;
        return why;
    }
    // 0x34 EDEDEDED, type (4 status, 5 skills), serial: byte for byte what the client sends (uo1998 log 15:52:55).
    // A typed line as the client sends it: type 0, the player's hue and font, the text and its NUL (uo1998 04:41:55,
    // 03 0014 00 005A 0003 "vendor sell"). The hue and font follow the player's own last line. Untracked: the answer is
    // the vendor's speech or a window, neither of which IsAnswer counts.
    public volatile int SpeechHue = 0x005A, SpeechFont = 3;
    // L-FUTILITY (Damian, 2026-10-17: "if it didn't get a proper response the first time, DO NOT EVER REPEAT IT"): a
    // line equal to the last one said, which got no proper answer (Heard), is refused within SayRepeatMs, whatever loop
    // sent it, and the caller gets why. A line that was answered may go again: a sale is "Brian sell" once per 5
    // shirts. A move of SayMovedTiles or more is new circumstances (Damian, 2026-10-17: "why is there a 120s delay if
    // you made a decision to move? circumstances changed yeah").
    public const int SayRepeatMs = 120000, SayMovedTiles = 3; readonly object m_sayGate = new();
    string? m_lastSaid = null;
    bool m_lastHeard; DateTime m_lastSaidAt = DateTime.MinValue; int m_lastSaidX, m_lastSaidY;
    public string? Say(string text)
    {
        int x, y;
        lock (State.Gate)
        {
            x = State.X;
            y = State.Y;
        }
        lock (m_sayGate)
        {
            bool moved = Math.Max(Math.Abs(x - m_lastSaidX), Math.Abs(y - m_lastSaidY)) >= SayMovedTiles;
            if (string.Equals(text, m_lastSaid, StringComparison.OrdinalIgnoreCase) && !m_lastHeard && !moved
                && (m_clock.UtcNow - m_lastSaidAt).TotalMilliseconds < SayRepeatMs)
                return "said \"" + text + "\" " + (int)(m_clock.UtcNow - m_lastSaidAt).TotalSeconds
                    + " s ago from here, and it got no answer";
        }
        byte[] t = Encoding.ASCII.GetBytes(text);
        byte[] p = new byte[9 + t.Length];
        p[0] = 0x03;
        Put16(p, 1, p.Length);
        p[3] = 0;
        Put16(p, 4, SpeechHue);
        Put16(p, 6, SpeechFont);
        t.CopyTo(p, 8);
        // Counted as said only once it went out: a line Send refused (busy) was never heard by anyone.
        string? why = Inject(p, "say " + text, false);
        if (why == null)
            lock (m_sayGate)
            {
                m_lastSaid = text;
                m_lastHeard = false;
                m_lastSaidAt = m_clock.UtcNow;
                m_lastSaidX = x;
                m_lastSaidY = y;
            }
        return why;
    }
    // The line got its proper answer (a window, or a reply that asks for one more ask): it may be said again.
    // The repeat guard forgotten: the Reinitialize button's (AssistFeatures.Reinitialize).
    public void ForgetSaid()
    {
        lock (m_sayGate)
        {
            m_lastSaid = null;
            m_lastHeard = false;
        }
    }
    public void Heard(string text)
    {
        lock (m_sayGate)
            if (string.Equals(text, m_lastSaid, StringComparison.OrdinalIgnoreCase))
                m_lastHeard = true;
    }
    // The buy window's answer: vendor, flag 2, then per item the stock layer (0x1A), its serial and the amount (uo1998
    // 04:23:33, 3B 000F 000764E0 02 1A 40076518 000A).
    public string? Buy(uint vendor, List<uint[]> items)
    {
        byte[] p = new byte[8 + 7 * items.Count];
        p[0] = 0x3B;
        Put16(p, 1, p.Length);
        Put32(p, 3, vendor);
        p[7] = 2;
        for (int i = 0; i < items.Count; i++)
        {
            p[8 + i * 7] = 0x1A;
            Put32(p, 9 + i * 7, items[i][0]);
            Put16(p, 13 + i * 7, (int)items[i][1]);
        }
        return Inject(p, "buy " + items.Count + " from " + vendor.ToString("X"), false);
    }
    // The sell window's answer: vendor, a count, then per item its serial and the amount (the client's 0x9F).
    public string? Sell(uint vendor, List<uint[]> items)
    {
        byte[] p = new byte[9 + 6 * items.Count];
        p[0] = 0x9F;
        Put16(p, 1, p.Length);
        Put32(p, 3, vendor);
        Put16(p, 7, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            Put32(p, 9 + i * 6, items[i][0]);
            Put16(p, 13 + i * 6, (int)items[i][1]);
        }
        return Inject(p, "sell " + items.Count + " to " + vendor.ToString("X"), false);
    }
    public string? Status(int type, uint serial)
    {
        byte[] p = new byte[10];
        p[0] = 0x34;
        Put32(p, 1, 0xEDEDEDED);
        p[5] = (byte)type;
        Put32(p, 6, serial);
        return Inject(p, (type == 4 ? "status" : "skills") + " request");
    }
    public string? Lift(uint serial, int amount)
    {
        byte[] p = new byte[7];
        p[0] = 0x07;
        Put32(p, 1, serial);
        Put16(p, 5, amount);
        return Inject(p, "lift " + serial.ToString("X"));
    }
    // x, y FFFF puts the item where the container chooses, as a drop on the paperdoll's backpack does.
    public string? Drop(uint serial, uint container)
    {
        byte[] p = new byte[14];
        p[0] = 0x08;
        Put32(p, 1, serial);
        Put16(p, 5, 0xFFFF);
        Put16(p, 7, 0xFFFF);
        Put32(p, 10, container);
        return Inject(p, "drop " + serial.ToString("X") + " into " + container.ToString("X"));
    }
    // A drop at x, y in a container's own gump coordinates, as the client sends an item placed in an open bag.
    public string? DropAt(uint serial, uint container, int x, int y)
    {
        byte[] p = new byte[14];
        p[0] = 0x08;
        Put32(p, 1, serial);
        Put16(p, 5, x);
        Put16(p, 7, y);
        Put32(p, 10, container);
        return Inject(p, "drop " + serial.ToString("X") + " into " + container.ToString("X") + " at " + x + "," + y);
    }
    // A drop on the ground: container FFFFFFFF and the tile, as the client sends a drag onto the world.
    public string? DropGround(uint serial, int x, int y, int z)
    {
        byte[] p = new byte[14];
        p[0] = 0x08;
        Put32(p, 1, serial);
        Put16(p, 5, x);
        Put16(p, 7, y);
        p[9] = (byte)(sbyte)z;
        Put32(p, 10, 0xFFFFFFFF);
        return Inject(p, "drop " + serial.ToString("X") + " at " + x + "," + y + "," + z);
    }
    public string? Equip(uint serial, int layer, uint mobile)
    {
        byte[] p = new byte[10];
        p[0] = 0x13;
        Put32(p, 1, serial);
        p[5] = (byte)layer;
        Put32(p, 6, mobile);
        return Inject(p, "equip " + serial.ToString("X") + " on layer " + layer);
    }
    // The assist judged the action answered (a lift the server accepts sends nothing back).
    public void Settle()
    {
        InFlight = null;
    }

    // Rule 4: only into the cursor the server opened for an injected use, echoing its id and type.
    public string? Target(bool ground, uint serial, int x, int y, int z, int graphic)
    {
        bool open;
        uint cursor;
        lock (State.Gate)
        {
            open = State.CursorOpen;
            cursor = State.Cursor;
        }
        if (!MacroCursor || !open)
            return "no assist cursor is open";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = (byte)(ground ? 1 : 0);
        Put32(p, 2, cursor);
        p[6] = (byte)CursorFlags;
        if (!ground)
            Put32(p, 7, serial);
        Put16(p, 11, x);
        Put16(p, 13, y);
        p[16] = (byte)z;
        Put16(p, 17, graphic);
        string? why = Inject(p,
            "target " + (ground ? "ground " : "object " + serial.ToString("X") + " ") + x + "," + y + "," + z);
        if (why == null)
            MacroCursor = false;
        return why;
    }

    // A packet for the client only (never the server), Huffman-encoded between whole server packets.
    public string? ToClient(byte[] p, string what)
    {
        if (Closed)
            return "the connection is closed";
        lock (m_downGate)
        {
            if (m_raw)
                return "server stream unframed";
            byte[] e = m_huff.Encode(p);
            m_client.Write(e, 0, e.Length);
        }
        m_log.Line(Id, "INJ S>C", what + " " + UoHex.Of(p));
        return null;
    }
    // ---------- walking (assist-plan.md stage 6) ----------
    // A step is 0x02 direction, number (uo1998 07:30:51: 020300 answered 220001); the server takes them in turn, 0 then
    // 1 up to 255 and on from 1, and starts over at 0 after a refusal (0x21) or the player drawn anew (0x20). The
    // assist's steps share that count with the client's, so the relay numbers every step itself: a client step is
    // renumbered on its way up and its answer numbered back on the way down; the answer to an assist step is kept from
    // the client, which is shown the player where the server has it (ShowPlayer) when the assist has walked. A refusal
    // goes to the client, which puts the player back where the server says.
    readonly object m_walkGate = new();
    int m_nextSeq; readonly int[] m_seqOwner = new int[256];  // 0 none, 1 + the client's number, -1 an assist step
    public volatile int StepAcks, StepRefusals; byte[] m_lastDraw = null!;
    void Renumber(byte[] p, bool assist)
    {
        lock (m_walkGate)
        {
            int s = m_nextSeq;
            m_seqOwner[s] = assist ? -1 : 1 + p[2];
            p[2] = (byte)s;
            m_nextSeq = s == 255 ? 1 : s + 1;
        }
    }
    // What the server's walk answers do on the way down; true when the packet is the assist's and not the client's.
    bool WalkAnswer(byte[] p)
    {
        if (p[0] == 0x22)
        {
            int owner;
            lock (m_walkGate)
            {
                owner = m_seqOwner[p[1]];
                m_seqOwner[p[1]] = 0;
            }
            if (owner < 0)
            {
                StepAcks++;
                return true;
            }
            if (owner > 0)
            {
                p[1] = (byte)(owner - 1);
                ClientStepAcks++;
            }
        }
        else if (p[0] == 0x21)
        {
            int owner;
            lock (m_walkGate)
            {
                owner = m_seqOwner[p[1]];
                Array.Clear(m_seqOwner, 0, 256);
                m_nextSeq = 0;
            }
            // A refusal of no known step (the server's second 0x21 for one step, after the first cleared the count) is
            // not the player's: counted the player's, it ended an assist walk as a takeover (2026-10-17 12:06:08).
            if (owner > 0)
                ClientStepRefusals++;
            else
                StepRefusals++;
            p[1] = (byte)(owner > 0 ? owner - 1 : 0);
        }
        else if (p[0] == 0x20)
        {
            uint who = ((uint)p[1] << 24) | ((uint)p[2] << 16) | ((uint)p[3] << 8) | p[4];
            uint me;
            lock (State.Gate)
                me = State.Player;
            if (who == me)
            {
                lock (m_walkGate)
                {
                    Array.Clear(m_seqOwner, 0, 256);
                    m_nextSeq = 0;
                }
                m_lastDraw = (byte[])p.Clone();
            }
        }
        return false;
    }
    // The client told to take a step (0x97, direction): it walks it as its own, animated, numbered and answered as any
    // step of the player's (Damian, 2026-10-17, of the injected steps: "it moved, but it didn't look like walking").
    public volatile int ClientStepAcks, ClientStepRefusals;
    public string? MoveClient(int dir)
    {
        if (Closed)
            return "the connection is closed";
        byte[] p = new byte[] { 0x97, (byte)dir };
        lock (m_downGate)
        {
            if (m_raw)
                return "server stream unframed";
            byte[] e = m_huff.Encode(p);
            m_client.Write(e, 0, e.Length);
        }
        m_log.Line(Id, "INJ S>C", UoHex.Of(p));
        return null;
    }
    // One step: direction 0 north to 7 north-west, 0x80 running. A step in a new direction only turns the player.
    // A step reveals a hidden player, so the redraw of the player (lastDraw, flags byte 10, 0x80 hidden) drops hidden
    // (Damian, 2026-10-17: "you need to pass through the unhide if you start moving hidden to the client").
    public string? Step(int dir)
    {
        byte[] d = m_lastDraw;
        if (d != null)
            d[10] &= 0x7F;
        byte[] p = new byte[3];
        p[0] = 0x02;
        p[1] = (byte)dir;
        return Inject(p, "step " + dir, false);
    }
    // The player drawn where the server has it (0x20, the last one the server sent with its place, facing and height
    // replaced), after assist steps the client did not see.
    // ShowMode: 0x20 draws the player anew (a jump, "it didn't look like walking"); 0x77 moves it as the client moves
    // any mobile it sees walk; 0 shows nothing until the walk ends (the walker then draws it with 0x20 once).
    // The 1.25 client ignores 0x77 for its own player (2026-10-17: "i only saw it move once at the end"), and the
    // animation is not needed ("we don't need to make it animate if the server is responding the correct way").
    public volatile int ShowMode = 0x20;
    public string? ShowPlayer(bool final = false)
    {
        if (m_lastDraw == null || Closed)
            return "the player has not been drawn yet";
        if (ShowMode == 0 && !final)
            return null;
        byte[]? p = PlayerDraw(final);
        lock (m_downGate)
        {
            if (m_raw)
                return "server stream unframed";
            byte[] e = m_huff.Encode(p!);
            m_client.Write(e, 0, e.Length);
        }
        m_log.Line(Id, "INJ S>C", UoHex.Of(p!));
        return null;
    }
    // The packet that shows the player where the assist has it: 0x20, or 0x77 in ShowMode 0x77; null before the server
    // has drawn the player once.
    byte[]? PlayerDraw(bool final)
    {
        byte[] d = m_lastDraw;
        if (d == null)
            return null;
        byte[] p = (byte[])d.Clone();
        int x, y, z, f;
        lock (State.Gate)
        {
            x = State.X;
            y = State.Y;
            z = State.Z;
            f = State.Facing;
        }
        Put16(p, 11, x);
        Put16(p, 13, y);
        p[17] = (byte)((p[17] & 0xF8) | (f & 7));
        p[18] = (byte)(sbyte)z;
        if (ShowMode == 0x77 && !final)
        {
            // 0x77: serial, body, x, y, z, direction, hue, flags, notoriety (17 bytes); the 0x20's serial, body, hue
            // and flags, notoriety 1 (innocent).
            byte[] m = new byte[17];
            m[0] = 0x77;
            Array.Copy(d, 1, m, 1, 4);
            m[5] = d[5];
            m[6] = d[6];
            Put16(m, 7, x);
            Put16(m, 9, y);
            m[11] = (byte)(sbyte)z;
            m[12] = (byte)(f & 7);
            m[13] = d[8];
            m[14] = d[9];
            m[15] = d[10];
            m[16] = 1;
            p = m;
        }
        return p;
    }

    // A system line in the client's journal (0x1C from serial FFFFFFFF), Huffman-encoded between whole server packets.
    public string? Tell(string text, int hue = 0x3B2)
    {
        if (Closed)
            return "the connection is closed";
        byte[] t = Encoding.ASCII.GetBytes(text.Length > 200 ? text.Substring(0, 200) : text);
        byte[] p = new byte[45 + t.Length];
        p[0] = 0x1C;
        Put16(p, 1, p.Length);
        Put32(p, 3, 0xFFFFFFFF);
        Put16(p, 7, 0xFFFF);
        p[9] = 0;
        Put16(p, 10, hue);
        Put16(p, 12, 3);
        Encoding.ASCII.GetBytes("Assist").CopyTo(p, 14);
        t.CopyTo(p, 44);
        lock (m_downGate)
        {
            if (m_raw)
                return "server stream unframed";
            byte[] e = m_huff.Encode(p);
            m_client.Write(e, 0, e.Length);
        }
        m_log.Line(Id, "INJ S>C", UoHex.Of(p));
        return null;
    }
}
