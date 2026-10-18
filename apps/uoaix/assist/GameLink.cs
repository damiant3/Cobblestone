// One game connection through the relay, and what the assist may put into it (assist-plan.md stages 2 and 3).
// No using directives: assist.ps1 compiles this after Assist.cs as one unit.
public sealed class GameLink {
    public readonly int Id; public readonly AssistState State = new AssistState();
    readonly AssistLog log; readonly int[] lengths; readonly UoHuffman huff; readonly uint seed;
    readonly System.Net.Sockets.NetworkStream client, server;
    readonly object upGate = new object(), downGate = new object();
    readonly UoKeystream enc; readonly UoFramer upFramer;
    readonly System.Collections.Generic.Queue<byte[]> toServer = new System.Collections.Generic.Queue<byte[]>();
    bool bytewise, raw, claimCursor;
    // Why injection stopped (rule 5); null while it may run.
    public volatile string Halted;
    // Rule 3: one injected action in flight, cleared by the server's answer.
    public volatile string InFlight; public System.DateTime InFlightAt;
    public const int AnswerMs = 3000;
    // A cursor opened by an injected use belongs to the assist: it is not shown to the client (rule 4).
    public volatile bool MacroCursor; public int CursorFlags;
    public volatile bool Closed;
    // Features watch whole packets here (after the state has read them), and may hold a client or server packet back;
    // a held server packet still reaches the watchers.
    public event System.Action<byte[]> ServerPacket, ClientPacket;
    public System.Func<byte[], bool> HoldClient, HoldServer;
    // The last lift went out from the assist, not the client: its refusal (0x27) is the assist's to read, and the 1.25
    // client, refused a lift it never made, crashed (access violation, 2026-10-17 05:17:09, a pickup lift refused out of
    // range; the Vesper buy circuit's crash at 01:37:55 was during bank lifts).
    public volatile bool AssistLift;
    // Rule 2: what the assist may send the server, each at the client table's length. Attack, use, lift, drop,
    // text command (skill and spell use), equip, target.
    // 0x34 is the status request the client sends when its status bar or skill gump opens.
    // 0x09 is the client's single click, which the server answers with the clicked mobile's name label.
    // 0x7D is the pick in an item menu (0x7C), the smithing loop's answer to the menu its hammer use opened.
    // 0x03 typed speech, 0x3B the buy window's answer and 0x9F the sell window's: the tailoring circuit's "vendor buy",
    // "vendor sell" and "bank" (assist-plan.md stage 6). 0x9A answers a text prompt: a rune's description.
    static readonly int[] Injectable = { 0x02, 0x03, 0x05, 0x06, 0x07, 0x08, 0x09, 0x12, 0x13, 0x34, 0x3B, 0x6C, 0x7D, 0x9A, 0x9F };
    // A server packet that answers an injected action (0x11 and 0x3A answer 0x34, 0x7C a tool's use).
    static readonly int[] Answers = { 0x6C, 0x1C, 0x24, 0x25, 0x27, 0x2E, 0x1D, 0x3C, 0x11, 0x3A, 0x7C };
    // While set, an item menu (0x7C) goes to the assist's watchers and not to the client's screen.
    public volatile bool ClaimMenu;

    public GameLink(int id, System.Net.Sockets.NetworkStream client, System.Net.Sockets.NetworkStream server, uint seed, int[] lengths, UoHuffman huff, AssistLog log) {
        Id = id; this.client = client; this.server = server; this.seed = seed; this.lengths = lengths; this.huff = huff; this.log = log;
        enc = new UoKeystream(seed); upFramer = new UoFramer(lengths);
    }

    public void Run() {
        var up = new System.Threading.Thread(Up); up.IsBackground = true; up.Start();
        Down(); up.Join(2000); Closed = true;
    }

    void Halt(string why) { if (Halted == null) { Halted = why; log.Line(Id, "--", "HALT " + why); } }

    static byte[] Masked(byte[] p) {
        var m = (byte[])p.Clone(); int from = p[0] == 0x91 ? 35 : p[0] == 0x80 ? 31 : -1;
        if (from >= 0) for (int i = from; i < from + 30 && i < m.Length; i++) m[i] = 0;
        return m;
    }
    void ToServer(byte[] plain, int off, int n) { var o = new byte[n]; for (int i = 0; i < n; i++) o[i] = enc.Apply(plain[off + i]); server.Write(o, 0, n); }

    // Client to server: decrypted, framed, re-encrypted; queued injections go out between whole client packets.
    // Which side ended the connection first, logged once as "ENDED BY CLIENT" or "ENDED BY SERVER": a read that ends is
    // that side's close, a write that fails is the other side's. Closing one side ends the other loop with "aborted by
    // the software in your host machine", so the first END line alone does not say who left (a logout reads as a drop).
    int ended;
    void End(string by) { if (System.Threading.Interlocked.Exchange(ref ended, 1) == 0) log.Line(Id, "--", "ENDED BY " + by); }
    void Up() {
        var buf = new byte[8192]; var dec = new UoKeystream(seed); bool reading = false;
        try {
            while (true) {
                reading = true; int n = client.Read(buf, 0, buf.Length); reading = false; if (n <= 0) { End("CLIENT"); break; }
                for (int i = 0; i < n; i++) buf[i] = dec.Apply(buf[i]);
                lock (upGate) {
                    if (bytewise) { ToServer(buf, 0, n); continue; }
                    upFramer.Feed(buf, 0, n, p => {
                        var hold = HoldClient;
                        if (hold != null && hold(p)) { log.Line(Id, "C>S", UoHex.Of(p) + " (held by the assist)"); return; }
                        if (p[0] == 0x02 && p.Length >= 3) Renumber(p, false);
                        if (p[0] == 0x07) AssistLift = false;
                        State.Client(p);
                        log.Line(Id, "C>S", UoHex.Of(p[0] == 0x91 || p[0] == 0x80 ? Masked(p) : p));
                        ToServer(p, 0, p.Length);
                        var watch = ClientPacket; if (watch != null) watch(p);
                    });
                    if (upFramer.Desync != null) {
                        bytewise = true; var rest = upFramer.TakeBuffered(); ToServer(rest, 0, rest.Length);
                        Halt("client packet unframed: " + upFramer.Desync + "; client bytes now pass re-encrypted");
                    }
                    Drain();
                }
            }
        } catch (System.Exception e) { End(reading ? "CLIENT" : "SERVER"); log.Line(Id, "--", "C>S END " + e.Message); }
        Closed = true; try { server.Close(); } catch { }
    }
    void Drain() {
        while (toServer.Count > 0 && upFramer.Buffered == 0 && !bytewise) {
            var p = toServer.Dequeue(); if (p[0] == 0x02 && p.Length >= 3) Renumber(p, true); State.Client(p); log.Line(Id, "INJ C>S", UoHex.Of(p)); ToServer(p, 0, p.Length);
        }
    }

    // Server to client: Huffman units decoded, framed, re-encoded one packet a unit, so an injected unit always falls
    // between whole server packets. On a desync the rest passes raw.
    void Down() {
        var buf = new byte[8192]; var dec = huff.NewDecoder(); var framer = new UoFramer(lengths);
        var pending = new System.Collections.Generic.List<byte>(); var outq = new System.IO.MemoryStream(); bool reading = false;
        try {
            while (true) {
                reading = true; int n = server.Read(buf, 0, buf.Length); reading = false; if (n <= 0) { End("SERVER"); break; }
                if (raw) { lock (downGate) client.Write(buf, 0, n); continue; }
                int start = pending.Count, good = 0;
                for (int i = 0; i < n; i++) pending.Add(buf[i]);
                dec.Feed(buf, 0, n, (u, end) => {
                    if (framer.Desync != null) return;
                    framer.Feed(u, 0, u.Length, p => {
                        State.Server(p);
                        if (WalkAnswer(p)) {
                            log.Line(Id, "S>C", UoHex.Of(p) + " (assist step)");
                            // The player redrawn in the ack's place in the stream: an item the server sends as the step
                            // brings it into range reaches a client that has the player there, not a tile behind, where
                            // an item at the edge of view is dropped (the east tailor's door and sign, 2026-10-17).
                            var draw = ShowMode != 0 ? PlayerDraw(false) : null;
                            if (draw != null) { var de = huff.Encode(draw); outq.Write(de, 0, de.Length); log.Line(Id, "INJ S>C", UoHex.Of(draw)); }
                            var walked = ServerPacket; if (walked != null) walked(p); return;
                        }
                        if (InFlight != null && IsAnswer(p)) { log.Line(Id, "--", "ANSWERED " + InFlight + " by " + p[0].ToString("X2")); InFlight = null; }
                        // A claim lasts until its cursor comes or ClaimMs passes: an earlier line (a refusal of something
                        // else, the prompt) must not hand the cursor to the client.
                        if (claimCursor && System.DateTime.UtcNow > claimUntil) claimCursor = false;
                        if (p[0] == 0x6C && claimCursor) {
                            claimCursor = false; MacroCursor = true; CursorFlags = p[6];
                            log.Line(Id, "S>C", UoHex.Of(p) + " (assist cursor, not shown)");
                            var seen = ServerPacket; if (seen != null) seen(p); return;
                        }
                        if (p[0] == 0x7C && ClaimMenu) {
                            log.Line(Id, "S>C", UoHex.Of(p) + " (assist menu, not shown)");
                            var menu = ServerPacket; if (menu != null) menu(p); return;
                        }
                        var hold = HoldServer;
                        if (hold != null && hold(p)) {
                            log.Line(Id, "S>C", UoHex.Of(p) + " (held by the assist)");
                            var held = ServerPacket; if (held != null) held(p); return;
                        }
                        log.Line(Id, "S>C", UoHex.Of(p));
                        var e = huff.Encode(p); outq.Write(e, 0, e.Length);
                        var watch = ServerPacket; if (watch != null) watch(p);
                    });
                    if (framer.Desync == null) good = start + end;
                });
                lock (downGate) {
                    if (outq.Length > 0) { client.Write(outq.GetBuffer(), 0, (int)outq.Length); outq.SetLength(0); }
                    if (framer.Desync != null) {
                        raw = true; var rest = pending.ToArray(); client.Write(rest, good, rest.Length - good); pending.Clear();
                        Halt("server packet unframed: " + framer.Desync + "; server bytes now pass raw");
                    } else pending.RemoveRange(0, good);
                }
            }
        } catch (System.Exception e) { End(reading ? "SERVER" : "CLIENT"); log.Line(Id, "--", "S>C END " + e.Message); }
        Closed = true; try { client.Close(); } catch { }
    }

    // Speech answers only when it is the system's or the player's own line, not a passer-by's.
    bool IsAnswer(byte[] p) {
        if (System.Array.IndexOf(Answers, (int)p[0]) < 0) return false;
        if (p[0] != 0x1C || p.Length < 7) return true;
        uint who = ((uint)p[3] << 24) | ((uint)p[4] << 16) | ((uint)p[5] << 8) | p[6]; uint me; lock (State.Gate) me = State.Player;
        return who == 0xFFFFFFFF || who == me;
    }
    static void Put32(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
    static void Put16(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }

    // Null when the packet went out (or was queued behind a partial client packet); otherwise why not.
    // A single click on a mobile, untracked: its answer is the mobile's own label, never one IsAnswer counts.
    public string Look(uint serial) { var p = new byte[5]; p[0] = 0x09; Put32(p, 1, serial); return Inject(p, "look " + serial.ToString("X"), false); }
    public string Inject(byte[] p, string action, bool track = true) { return Send(p, action, track, false); }
    // The client's own packet the assist held: sent whatever the player's state, as the client would have sent it.
    public string Release(byte[] p, string action, bool track = true) { return Send(p, action, track, true); }
    string Send(byte[] p, string action, bool track, bool clients) {
        if (Closed) return "the connection is closed";
        // Nothing the assist starts goes out while the player is dead (Damian, 2026-10-15).
        if (!clients && State.Dead) return "the player is dead";
        if (Halted != null) return "halted: " + Halted;
        var guard = AssistGuard.Current; if (guard != null && guard.Disabled != null) return "assist disabled by the fail-safe: " + guard.Disabled;
        if (System.Array.IndexOf(Injectable, (int)p[0]) < 0) return "opcode " + p[0].ToString("X2") + " is not one the assist sends";
        int len = p[0] < lengths.Length ? lengths[p[0]] : -1;
        if (len == 0 ? (p.Length < 3 || ((p[1] << 8) | p[2]) != p.Length) : p.Length != len) return "length " + p.Length + " is not the client's for " + p[0].ToString("X2");
        // An action unanswered after AnswerMs is let go for an untracked send as for a tracked one: a door's use, which no
        // packet in Answers answers, left every later step "busy" and ended the circuit (Moonglow, 2026-10-17 09:53:06).
        if (InFlight != null) {
            if ((System.DateTime.UtcNow - InFlightAt).TotalMilliseconds < AnswerMs) return "busy: " + InFlight + " is unanswered";
            log.Line(Id, "--", "UNANSWERED " + InFlight + " after " + AnswerMs + " ms"); InFlight = null;
        }
        lock (upGate) {
            if (bytewise) return "client stream unframed";
            if (p[0] == 0x07) AssistLift = !clients;
            if (track) { InFlight = action; InFlightAt = System.DateTime.UtcNow; }
            toServer.Enqueue(p); Drain();
        }
        return null;
    }

    // claim false leaves the cursor the use opens to the client: the first iteration of a loop, where the player picks.
    public string Use(uint serial, bool claim = true) {
        var p = new byte[5]; p[0] = 0x06; Put32(p, 1, serial);
        return Claimed(p, "use " + serial.ToString("X"), claim);
    }
    // 0x12 type 0x24 "<skill> 0": the client's skill-gump use (soak-bots.ps1 Skill-Use).
    public string Skill(int skill, bool claim = true) {
        var t = System.Text.Encoding.ASCII.GetBytes(skill + " 0"); var p = new byte[5 + t.Length];
        p[0] = 0x12; Put16(p, 1, p.Length); p[3] = 0x24; t.CopyTo(p, 4);
        return Claimed(p, "skill " + skill, claim);
    }
    // 0x12 type 0x56 "<spell>" and a zero: the client's spellbook cast (uo1998 relay log 02:31:12, Poison "20"; OUO
    // packet_handler.c CastSpellByID).
    // A spell's cursor comes after its cast time, not a use's quick answer: Recall's came after the 3 s claim had lapsed
    // and went to the client (uo1998 2026-10-17 00:21:41), so a cast claims its cursor CastClaimMs.
    public const int CastClaimMs = 8000;
    public string Cast(int spell, bool claim = true) {
        var t = System.Text.Encoding.ASCII.GetBytes(spell.ToString()); var p = new byte[5 + t.Length];
        p[0] = 0x12; Put16(p, 1, p.Length); p[3] = 0x56; t.CopyTo(p, 4);
        return Claimed(p, "cast " + spell, claim, CastClaimMs);
    }
    public const int ClaimMs = 3000; System.DateTime claimUntil;
    string Claimed(byte[] p, string action, bool claim, int ms = ClaimMs) {
        claimUntil = System.DateTime.UtcNow.AddMilliseconds(ms); claimCursor = claim; MacroCursor = false;
        string why = Inject(p, action); if (why != null) claimCursor = false;
        return why;
    }
    // 0x34 EDEDEDED, type (4 status, 5 skills), serial: byte for byte what the client sends (uo1998 log 15:52:55).
    // A typed line as the client sends it: type 0, the player's hue and font, the text and its NUL (uo1998 04:41:55,
    // 03 0014 00 005A 0003 "vendor sell"). The hue and font follow the player's own last line. Untracked: the answer is
    // the vendor's speech or a window, neither of which IsAnswer counts.
    public volatile int SpeechHue = 0x005A, SpeechFont = 3;
    // L-FUTILITY (Damian, 2026-10-17: "if it didn't get a proper response the first time, DO NOT EVER REPEAT IT"): a line
    // equal to the last one said, which got no proper answer (Heard), is refused within SayRepeatMs, whatever loop sent it,
    // and the caller gets why. A line that was answered may go again: a sale is "Brian sell" once per 5 shirts. A move of
    // SayMovedTiles or more is new circumstances (Damian, 2026-10-17: "why is there a 120s delay if you made a decision to
    // move? circumstances changed yeah").
    public const int SayRepeatMs = 120000, SayMovedTiles = 3; readonly object sayGate = new object();
    string lastSaid; bool lastHeard; System.DateTime lastSaidAt = System.DateTime.MinValue; int lastSaidX, lastSaidY;
    public string Say(string text) {
        int x, y; lock (State.Gate) { x = State.X; y = State.Y; }
        lock (sayGate) {
            bool moved = System.Math.Max(System.Math.Abs(x - lastSaidX), System.Math.Abs(y - lastSaidY)) >= SayMovedTiles;
            if (string.Equals(text, lastSaid, System.StringComparison.OrdinalIgnoreCase) && !lastHeard && !moved && (System.DateTime.UtcNow - lastSaidAt).TotalMilliseconds < SayRepeatMs)
                return "said \"" + text + "\" " + (int)(System.DateTime.UtcNow - lastSaidAt).TotalSeconds + " s ago from here, and it got no answer";
        }
        var t = System.Text.Encoding.ASCII.GetBytes(text); var p = new byte[9 + t.Length];
        p[0] = 0x03; Put16(p, 1, p.Length); p[3] = 0; Put16(p, 4, SpeechHue); Put16(p, 6, SpeechFont); t.CopyTo(p, 8);
        // Counted as said only once it went out: a line Send refused (busy) was never heard by anyone.
        string why = Inject(p, "say " + text, false);
        if (why == null) lock (sayGate) { lastSaid = text; lastHeard = false; lastSaidAt = System.DateTime.UtcNow; lastSaidX = x; lastSaidY = y; }
        return why;
    }
    // The line got its proper answer (a window, or a reply that asks for one more ask): it may be said again.
    public void Heard(string text) { lock (sayGate) if (string.Equals(text, lastSaid, System.StringComparison.OrdinalIgnoreCase)) lastHeard = true; }
    // The buy window's answer: vendor, flag 2, then per item the stock layer (0x1A), its serial and the amount (uo1998
    // 04:23:33, 3B 000F 000764E0 02 1A 40076518 000A).
    public string Buy(uint vendor, System.Collections.Generic.List<uint[]> items) {
        var p = new byte[8 + 7 * items.Count]; p[0] = 0x3B; Put16(p, 1, p.Length); Put32(p, 3, vendor); p[7] = 2;
        for (int i = 0; i < items.Count; i++) { p[8 + i * 7] = 0x1A; Put32(p, 9 + i * 7, items[i][0]); Put16(p, 13 + i * 7, (int)items[i][1]); }
        return Inject(p, "buy " + items.Count + " from " + vendor.ToString("X"), false);
    }
    // The sell window's answer: vendor, a count, then per item its serial and the amount (the client's 0x9F).
    public string Sell(uint vendor, System.Collections.Generic.List<uint[]> items) {
        var p = new byte[9 + 6 * items.Count]; p[0] = 0x9F; Put16(p, 1, p.Length); Put32(p, 3, vendor); Put16(p, 7, items.Count);
        for (int i = 0; i < items.Count; i++) { Put32(p, 9 + i * 6, items[i][0]); Put16(p, 13 + i * 6, (int)items[i][1]); }
        return Inject(p, "sell " + items.Count + " to " + vendor.ToString("X"), false);
    }
    public string Status(int type, uint serial) { var p = new byte[10]; p[0] = 0x34; Put32(p, 1, 0xEDEDEDED); p[5] = (byte)type; Put32(p, 6, serial); return Inject(p, (type == 4 ? "status" : "skills") + " request"); }
    public string Lift(uint serial, int amount) { var p = new byte[7]; p[0] = 0x07; Put32(p, 1, serial); Put16(p, 5, amount); return Inject(p, "lift " + serial.ToString("X")); }
    // x, y FFFF puts the item where the container chooses, as a drop on the paperdoll's backpack does.
    public string Drop(uint serial, uint container) { var p = new byte[14]; p[0] = 0x08; Put32(p, 1, serial); Put16(p, 5, 0xFFFF); Put16(p, 7, 0xFFFF); Put32(p, 10, container); return Inject(p, "drop " + serial.ToString("X") + " into " + container.ToString("X")); }
    // A drop on the ground: container FFFFFFFF and the tile, as the client sends a drag onto the world.
    public string DropGround(uint serial, int x, int y, int z) { var p = new byte[14]; p[0] = 0x08; Put32(p, 1, serial); Put16(p, 5, x); Put16(p, 7, y); p[9] = (byte)(sbyte)z; Put32(p, 10, 0xFFFFFFFF); return Inject(p, "drop " + serial.ToString("X") + " at " + x + "," + y + "," + z); }
    public string Equip(uint serial, int layer, uint mobile) { var p = new byte[10]; p[0] = 0x13; Put32(p, 1, serial); p[5] = (byte)layer; Put32(p, 6, mobile); return Inject(p, "equip " + serial.ToString("X") + " on layer " + layer); }
    // The assist judged the action answered (a lift the server accepts sends nothing back).
    public void Settle() { InFlight = null; }

    // Rule 4: only into the cursor the server opened for an injected use, echoing its id and type.
    public string Target(bool ground, uint serial, int x, int y, int z, int graphic) {
        bool open; uint cursor; lock (State.Gate) { open = State.CursorOpen; cursor = State.Cursor; }
        if (!MacroCursor || !open) return "no assist cursor is open";
        var p = new byte[19]; p[0] = 0x6C; p[1] = (byte)(ground ? 1 : 0); Put32(p, 2, cursor); p[6] = (byte)CursorFlags;
        if (!ground) Put32(p, 7, serial);
        Put16(p, 11, x); Put16(p, 13, y); p[16] = (byte)z; Put16(p, 17, graphic);
        string why = Inject(p, "target " + (ground ? "ground " : "object " + serial.ToString("X") + " ") + x + "," + y + "," + z);
        if (why == null) MacroCursor = false;
        return why;
    }

    // A packet for the client only (never the server), Huffman-encoded between whole server packets.
    public string ToClient(byte[] p, string what) {
        if (Closed) return "the connection is closed";
        lock (downGate) {
            if (raw) return "server stream unframed";
            var e = huff.Encode(p); client.Write(e, 0, e.Length);
        }
        log.Line(Id, "INJ S>C", what + " " + UoHex.Of(p));
        return null;
    }
    // ---------- walking (assist-plan.md stage 6) ----------
    // A step is 0x02 direction, number (uo1998 07:30:51: 020300 answered 220001); the server takes them in turn, 0 then
    // 1 up to 255 and on from 1, and starts over at 0 after a refusal (0x21) or the player drawn anew (0x20). The assist's
    // steps share that count with the client's, so the relay numbers every step itself: a client step is renumbered on its
    // way up and its answer numbered back on the way down; the answer to an assist step is kept from the client, which is
    // shown the player where the server has it (ShowPlayer) when the assist has walked. A refusal goes to the client, which
    // puts the player back where the server says.
    readonly object walkGate = new object();
    int nextSeq; readonly int[] seqOwner = new int[256];  // 0 none, 1 + the client's number, -1 an assist step
    public volatile int StepAcks, StepRefusals; byte[] lastDraw;
    void Renumber(byte[] p, bool assist) {
        lock (walkGate) {
            int s = nextSeq; seqOwner[s] = assist ? -1 : 1 + p[2]; p[2] = (byte)s;
            nextSeq = s == 255 ? 1 : s + 1;
        }
    }
    // What the server's walk answers do on the way down; true when the packet is the assist's and not the client's.
    bool WalkAnswer(byte[] p) {
        if (p[0] == 0x22 && p.Length >= 3) {
            int owner; lock (walkGate) { owner = seqOwner[p[1]]; seqOwner[p[1]] = 0; }
            if (owner < 0) { StepAcks++; return true; }
            if (owner > 0) { p[1] = (byte)(owner - 1); ClientStepAcks++; }
        } else if (p[0] == 0x21 && p.Length >= 8) {
            int owner; lock (walkGate) { owner = seqOwner[p[1]]; System.Array.Clear(seqOwner, 0, 256); nextSeq = 0; }
            // A refusal of no known step (the server's second 0x21 for one step, after the first cleared the count) is
            // not the player's: counted the player's, it ended an assist walk as a takeover (2026-10-17 12:06:08).
            if (owner > 0) ClientStepRefusals++; else StepRefusals++;
            p[1] = (byte)(owner > 0 ? owner - 1 : 0);
        } else if (p[0] == 0x20 && p.Length >= 19) {
            uint who = ((uint)p[1] << 24) | ((uint)p[2] << 16) | ((uint)p[3] << 8) | p[4]; uint me; lock (State.Gate) me = State.Player;
            if (who == me) { lock (walkGate) { System.Array.Clear(seqOwner, 0, 256); nextSeq = 0; } lastDraw = (byte[])p.Clone(); }
        }
        return false;
    }
    // The client told to take a step (0x97, direction): it walks it as its own, animated, numbered and answered as any
    // step of the player's (Damian, 2026-10-17, of the injected steps: "it moved, but it didn't look like walking").
    public volatile int ClientStepAcks, ClientStepRefusals;
    public string MoveClient(int dir) {
        if (Closed) return "the connection is closed";
        var p = new byte[] { 0x97, (byte)dir };
        lock (downGate) { if (raw) return "server stream unframed"; var e = huff.Encode(p); client.Write(e, 0, e.Length); }
        log.Line(Id, "INJ S>C", UoHex.Of(p));
        return null;
    }
    // One step: direction 0 north to 7 north-west, 0x80 running. A step in a new direction only turns the player.
    // A step reveals a hidden player, so the redraw of the player (lastDraw, flags byte 10, 0x80 hidden) drops hidden
    // (Damian, 2026-10-17: "you need to pass through the unhide if you start moving hidden to the client").
    public string Step(int dir) { var d = lastDraw; if (d != null) d[10] &= 0x7F; var p = new byte[3]; p[0] = 0x02; p[1] = (byte)dir; return Inject(p, "step " + dir, false); }
    // The player drawn where the server has it (0x20, the last one the server sent with its place, facing and height
    // replaced), after assist steps the client did not see.
    // ShowMode: 0x20 draws the player anew (a jump, "it didn't look like walking"); 0x77 moves it as the client moves any
    // mobile it sees walk; 0 shows nothing until the walk ends (the walker then draws it with 0x20 once).
    // The 1.25 client ignores 0x77 for its own player (2026-10-17: "i only saw it move once at the end"), and the
    // animation is not needed ("we don't need to make it animate if the server is responding the correct way").
    public volatile int ShowMode = 0x20;
    public string ShowPlayer(bool final = false) {
        if (lastDraw == null || Closed) return "the player has not been drawn yet";
        if (ShowMode == 0 && !final) return null;
        var p = PlayerDraw(final);
        lock (downGate) { if (raw) return "server stream unframed"; var e = huff.Encode(p); client.Write(e, 0, e.Length); }
        log.Line(Id, "INJ S>C", UoHex.Of(p));
        return null;
    }
    // The packet that shows the player where the assist has it: 0x20, or 0x77 in ShowMode 0x77; null before the server
    // has drawn the player once.
    byte[] PlayerDraw(bool final) {
        var d = lastDraw; if (d == null) return null;
        var p = (byte[])d.Clone(); int x, y, z, f; lock (State.Gate) { x = State.X; y = State.Y; z = State.Z; f = State.Facing; }
        Put16(p, 11, x); Put16(p, 13, y); p[17] = (byte)((p[17] & 0xF8) | (f & 7)); p[18] = (byte)(sbyte)z;
        if (ShowMode == 0x77 && !final) {
            // 0x77: serial, body, x, y, z, direction, hue, flags, notoriety (17 bytes); the 0x20's serial, body, hue and
            // flags, notoriety 1 (innocent).
            var m = new byte[17]; m[0] = 0x77; System.Array.Copy(d, 1, m, 1, 4); m[5] = d[5]; m[6] = d[6];
            Put16(m, 7, x); Put16(m, 9, y); m[11] = (byte)(sbyte)z; m[12] = (byte)(f & 7); m[13] = d[8]; m[14] = d[9]; m[15] = d[10]; m[16] = 1;
            p = m;
        }
        return p;
    }

    // A system line in the client's journal (0x1C from serial FFFFFFFF), Huffman-encoded between whole server packets.
    public string Tell(string text, int hue = 0x3B2) {
        if (Closed) return "the connection is closed";
        var t = System.Text.Encoding.ASCII.GetBytes(text.Length > 200 ? text.Substring(0, 200) : text);
        var p = new byte[45 + t.Length]; p[0] = 0x1C; Put16(p, 1, p.Length); Put32(p, 3, 0xFFFFFFFF); Put16(p, 7, 0xFFFF);
        p[9] = 0; Put16(p, 10, hue); Put16(p, 12, 3); System.Text.Encoding.ASCII.GetBytes("Assist").CopyTo(p, 14); t.CopyTo(p, 44);
        lock (downGate) {
            if (raw) return "server stream unframed";
            var e = huff.Encode(p); client.Write(e, 0, e.Length);
        }
        log.Line(Id, "INJ S>C", UoHex.Of(p));
        return null;
    }
}
