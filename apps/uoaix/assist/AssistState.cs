#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

// What the server has told this client, kept from whole packets in both directions (assist-plan.md stage 2).
// Field offsets are the 1.25.32 layouts the UOAIX shard writes (GamePackets.codex gp-status, gp-speech;
// GameSession.codex gs-skills) and the soak bots read (proofs/soak-bots.ps1 Handle).

namespace Uoaix.Assist;

public sealed class AssistItem
{
    public uint Serial, Container, Mobile; public int Graphic, Amount = 1, X, Y, Z, Hue, Layer = -1;
}

// A vendor's restock seen as it happened (Restocks).
public sealed class VendorRestock
{
    public uint Vendor; public DateTime At;
}

public sealed class AssistState
{
    public readonly object Gate = new();
    public uint Player, Backpack; public int X, Y, Z, Facing;
    public string Name = "", Title = "";
    public int Hits, MaxHits, Str, Dex, Int, Stam, MaxStam, Mana, MaxMana, Gold, Armor, Weight;
    public ValueMap<int, int> Skills = ValueMap<int, int>.Empty;
    public ValueMap<int, int> SkillStart = ValueMap<int, int>.Empty;
    // Carried, contained and worn items; ground items apart, dropped beyond GroundReach of the player.
    public Map<uint, AssistItem> Items = Map<uint, AssistItem>.Empty;
    public Map<uint, AssistItem> Ground = Map<uint, AssistItem>.Empty;
    // ItemKeep bounds Items and Ground against a server naming serials without end; a 1998 pack or bank box holds about
    // 125.
    public const int GroundReach = 24, JournalKeep = 200, ItemKeep = 16384;
    public readonly List<string> Journal = new();
    public bool CursorOpen; public uint Cursor; public int CursorType;
    public int SkillEntry;  // 4 (id, value) or 6 (id, value, base) bytes a skill in the 0x3A full list
    public string Fault = null!;    // the first packet this state could not read
    readonly int[] m_walking = new int[256];
    // The sequence numbers of steps sent, oldest first; an acknowledged one is dropped from the front.
    readonly Queue<int> m_stepOrder = new();

    static int U16(byte[] p, int at)
    {
        return (p[at] << 8) | p[at + 1];
    }
    static uint U32(byte[] p, int at)
    {
        return ((uint)p[at] << 24) | ((uint)p[at + 1] << 16) | ((uint)p[at + 2] << 8) | p[at + 3];
    }
    static string Ascii(byte[] p, int at, int max)
    {
        StringBuilder sb = new();
        for (int i = at; i < at + max && i < p.Length && p[i] != 0; i++)
            sb.Append(p[i] >= 32 && p[i] < 127 ? (char)p[i] : '?');
        return sb.ToString();
    }
    public long JournalSeq;  // lines ever said; the journal holds the last JournalKeep of them
    public readonly List<string[]> Chat = new();  // time, speaker, text
    public long ChatSeq;
    // Session: when the game login confirmed; the player's notoriety (0x77/0x78, 1 innocent to 7 invulnerable, 0 not
    // seen); guarded from the server's own guard-zone lines (OUO player.c), null until the first one.
    public DateTime LoginAt; public int Notoriety; public bool? Guarded;
    // Dead from the death packet (0x2C) until the player's body is drawn human again (a ghost is 0x192 or 0x193).
    public bool Dead;
    // The player's poisoned bit: 0x04 of the flags byte in 0x20, 0x77 and 0x78 (OUO wombat_exec.c Script_setPoisoned
    // sets and clears mobile flag 4).
    public const int PoisonFlag = 0x04; public volatile bool Poisoned;
    void PlayerBody(int body)
    {
        Dead = body == 0x192 || body == 0x193;
    }
    // For the session summary: attributes at login, and what the server named or drew each mobile as (bounded: cleared
    // past MobileKeep, as mobiles come and go).
    public int StrStart = -1, DexStart, IntStart;
    public const int MobileKeep = 4096;
    public Map<uint, string> Names = Map<uint, string>.Empty;
    public ValueMap<uint, int> Bodies = ValueMap<uint, int>.Empty;
    // Each other mobile's notoriety byte from 0x77 and 0x78, as sent (uo1998's is signed: read it as sbyte).
    public ValueMap<uint, int> Notorieties = ValueMap<uint, int>.Empty;
    // The mobiles in view and their tiles, from the server's own packets: 0x78 in view, 0x77 moved, 0x1D out of view.
    public Map<uint, int[]> Mobiles = Map<uint, int[]>.Empty;
    // Teleporter jumps seen (from x, y, z, to x, y, z), taken by AssistFeatures.LearnJumps.
    public readonly List<int[]> Jumps = new();
    public DateTime StepAt, CursorAt, DrawAt;
    // The link's clock (GameLink), so a stamp here and the features' own reading are on one clock: the pickup's Moving
    // and the landing's settle subtract Clock.UtcNow and StepAt or DrawAt, two clocks under the suites' virtual one.
    public IClock Clock = SystemClock.Instance;
    public const int JumpTiles = 2, JumpStepMs = 1500, JumpCursorMs = 10000;
    // The mobiles whose single-click label has come, and those it called invulnerable: a vendor, a player's vendor
    // ("Rex (invulnerable)", 2026-10-18) or another NPC, never a player.
    public Set<uint> Labelled = Set<uint>.Empty, Invulnerable = Set<uint>.Empty;
    // The mobiles whose label says "(tame)": someone's pet (Damian, 2026-10-23: "there is typically a (Tame) in the
    // name for a currently owned pet").
    public Set<uint> Tame = Set<uint>.Empty;
    // Each vendor's stock box (the box it wears at layer 0x1A in 0x78) and the vendor; cleared past MobileKeep.
    public ValueMap<uint, uint> VendorBoxes = ValueMap<uint, uint>.Empty;
    public const int StockBoxLayer = 0x1A;
    // A vendor's restocks seen, oldest first, for the features to take; at most RestockKeep. A restock re-creates each
    // ware of the stock box: a destroy (0x1D) to every client within 18 tiles, then the ware back in the box (0x25),
    // one pair a unit restocked (OUO CItem_HideVT, CMobile_VendorStock_Restock; relay log 2026-10-24 05:52:31.931,
    // Atta's box 4096B168: one ware 21 to 26 a unit at a time, others set to 10). A purchase sends the same pair with
    // the amount down, so a ware back with more than it had is a restock.
    public readonly List<VendorRestock> Restocks = new();
    public const int RestockKeep = 256, RestockPairMs = 1000, RestockGapMs = 60000;
    // A vendor's ware destroyed: its amount and when, until it comes back.
    Map<uint, long[]> m_wareGone = Map<uint, long[]>.Empty;
    ValueMap<uint, DateTime> m_lastRestock = ValueMap<uint, DateTime>.Empty;
    // The last ground item a destroy (0x1D) took, for a server packet handler, which runs after the state has dropped
    // it.
    public AssistItem GroundGone = null!;
    void WareBack(uint s, int amount, uint box)
    {
        long[]? gone;
        if (!m_wareGone.TryGet(s, out gone))
            return;
        m_wareGone = m_wareGone.Remove(s);
        uint vendor;
        DateTime now = Clock.UtcNow;
        if (!VendorBoxes.TryGet(box, out vendor)
            || (now - new DateTime(gone[1])).TotalMilliseconds > RestockPairMs || Math.Max(1, amount) <= gone[0])
            return;
        DateTime last;
        if (m_lastRestock.TryGet(vendor, out last) && (now - last).TotalMilliseconds < RestockGapMs)
            return;
        if (m_lastRestock.Count > MobileKeep)
            m_lastRestock = m_lastRestock.Clear();
        m_lastRestock = m_lastRestock.Set(vendor, now);
        Restocks.Add(new VendorRestock { Vendor = vendor, At = now });
        if (Restocks.Count > RestockKeep)
            Restocks.RemoveAt(0);
    }
    void Named(uint s, string name)
    {
        if (name.Length == 0)
            return;
        if (Names.Count > MobileKeep)
            Names = Names.Clear();
        Names = Names.Set(s, name);
    }
    void Body(uint s, int body)
    {
        if (Bodies.Count > MobileKeep)
            Bodies = Bodies.Clear();
        Bodies = Bodies.Set(s, body);
    }
    void Noto(uint s, int noto)
    {
        if (Notorieties.Count > MobileKeep)
            Notorieties = Notorieties.Clear();
        Notorieties = Notorieties.Set(s, noto);
    }
    // Each journal line's speaker, 0xFFFFFFFF for the server's own line: a single-click label is a line too ("30 Blood
    // Moss" read as a recall's answer, 2026-10-22 06:41:56).
    readonly List<uint> m_journalWho = new();
    // Whether each journal line is a single-click label (0x1C type 6).
    readonly List<bool> m_journalLabel = new();
    public const int BarkHue = 0x22;
    void Say(string line, uint who, bool label)
    {
        Journal.Add(line);
        m_journalWho.Add(who);
        m_journalLabel.Add(label);
        JournalSeq++;
        if (Journal.Count > JournalKeep)
        {
            int cut = Journal.Count - JournalKeep;
            Journal.RemoveRange(0, cut);
            m_journalWho.RemoveRange(0, cut);
            m_journalLabel.RemoveRange(0, cut);
        }
    }
    int JournalFrom(long seq)
    {
        long first = JournalSeq - Journal.Count;
        return seq > JournalSeq ? 0 : (int)Math.Min(Journal.Count, Math.Max(0, seq - first));
    }
    // The index an end seq stops at: every line kept for an end at or past the newest.
    int JournalTo(long to)
    {
        long first = JournalSeq - Journal.Count;
        return to >= JournalSeq ? Journal.Count : (int)Math.Max(0, to - first);
    }
    public List<string> JournalSince(long seq)
    {
        return JournalBetween(seq, long.MaxValue);
    }
    // The lines said from seq up to (not including) to.
    public List<string> JournalBetween(long seq, long to)
    {
        lock (Gate)
        {
            int from = JournalFrom(seq), end = JournalTo(to);
            return Journal.GetRange(from, Math.Max(0, end - from));
        }
    }
    // The server's own lines since seq ("You must wait", "fizzle"), never a label or anyone's speech.
    public List<string> ServerSince(long seq)
    {
        return ServerBetween(seq, long.MaxValue);
    }
    public List<string> ServerBetween(long seq, long to)
    {
        List<string> lines = new();
        lock (Gate)
            for (int i = JournalFrom(seq), end = JournalTo(to); i < end; i++)
                if (m_journalWho[i] == 0xFFFFFFFF)
                    lines.Add(Journal[i]);
        return lines;
    }
    // What can answer an action from seq up to to: every line but a label and the player's own words (a bark over the
    // player is the server's line), so a line barked over an item or said by an NPC still answers. A label holding an
    // answer's word ("a bag of reagents", 2026-10-22 06:41:56) is no answer.
    public List<string> AnswersBetween(long seq, long to)
    {
        List<string> lines = new();
        lock (Gate)
            for (int i = JournalFrom(seq), end = JournalTo(to); i < end; i++)
                if (!m_journalLabel[i] && m_journalWho[i] != Player)
                    lines.Add(Journal[i]);
        return lines;
    }
    // Whether the player has spoken since seq: a spell's words mean the server took the cast.
    public bool PlayerSpokeSince(long seq)
    {
        return PlayerSpokeBetween(seq, long.MaxValue);
    }
    public bool PlayerSpokeBetween(long seq, long to)
    {
        lock (Gate)
            for (int i = JournalFrom(seq), end = JournalTo(to); i < end; i++)
                if (m_journalWho[i] == Player)
                    return true;
        return false;
    }

    public void Client(byte[] p)
    {
        lock (Gate)
        {
            switch (p[0])
            {
                case 0x02:
                    m_walking[p[2]] = p[1] + 1;
                    m_stepOrder.Enqueue(p[2]);
                    if (m_stepOrder.Count > 256)
                        m_stepOrder.Dequeue();
                    break;
                case 0x6C:
                    CursorOpen = false;
                    break;
            }
        }
    }

    public void Server(byte[] p)
    {
        lock (Gate)
        {
            try
            {
                Read(p);
            }
            catch (Exception e) { if (Fault
                == null) Fault = "opcode " + p[0].ToString("X2") + " length " + p.Length + ": " + e.Message; }
        }
    }

    void Read(byte[] p)
    {
        switch (p[0])
        {
            case 0x77:
                if (p.Length >= 7)
                {
                    Body(U32(p, 1), U16(p, 5));
                    if (U32(p, 1) == Player)
                        PlayerBody(U16(p, 5));
                }
                if (p.Length >= 17 && U32(p, 1) == Player)
                {
                    Notoriety = p[16];
                    Poisoned = (p[15] & PoisonFlag) != 0;
                }
                if (p.Length >= 12 && U32(p, 1) != Player)
                    Mobiles = Mobiles.Set(U32(p, 1), new[] { U16(p, 7), U16(p, 9), (sbyte)p[11] });
                if (p.Length >= 17 && U32(p, 1) != Player)
                    Noto(U32(p, 1), p[16]);
                break;
            case 0x1B:
                Mobiles = Mobiles.Clear();
                LoginAt = Clock.UtcNow.ToLocalTime();
                Player = U32(p, 1);
                PlayerBody(U16(p, 9));
                X = U16(p, 11);
                Y = U16(p, 13);
                Z = (sbyte)p[16];
                Facing = p[17] & 7;
                Prune();
                break;
            case 0x2C:
                Dead = true;
                break;
            case 0x20:
                if (U32(p, 1) == Player)
                {
                    if (p.Length > 10)
                        Poisoned = (p[10] & PoisonFlag) != 0;
                    int nx = U16(p, 11), ny = U16(p, 13), nz = (sbyte)p[18];
                    // A redraw far off just after a step, with no spell cursor (Recall, Teleport) in JumpCursorMs, is a
                    // teleporter the server keeps from the client: the tile stepped onto sends the player to the
                    // landing.
                    DateTime now = Clock.UtcNow;
                    if (Math.Max(Math.Abs(nx - X), Math.Abs(ny - Y)) > JumpTiles
                        && (now - StepAt).TotalMilliseconds < JumpStepMs
                        && (now - CursorAt).TotalMilliseconds > JumpCursorMs)
                    {
                        // The tile stepped onto: the oldest step not yet acknowledged, when the redraw came before its
                        // answer, else where the player stood (1418,3831 was learned one step short of the teleporter
                        // at 1419,3832, and a walk onto it went nowhere, 2026-10-18 01:13:20).
                        int fx = X, fy = Y, fz = Z;
                        while (m_stepOrder.Count > 0 && m_walking[m_stepOrder.Peek()] == 0)
                            m_stepOrder.Dequeue();
                        if (m_stepOrder.Count > 0)
                        {
                            int dir = (m_walking[m_stepOrder.Peek()] - 1) & 7;
                            if (dir == Facing)
                            {
                                fx = X + new[] { 0, 1, 1, 1, 0, -1, -1, -1 }[dir];
                                fy = Y + new[] { -1, -1, 0, 1, 1, 1, 0, -1 }[dir];
                                int sz = UoMap.StepZ(X, Y, Z, fx, fy);
                                if (sz == int.MinValue)
                                    sz = UoMap.StandZ(fx, fy, Z);
                                if (sz != int.MinValue)
                                    fz = sz;
                            }
                        }
                        Jumps.Add(new[] { fx, fy, fz, nx, ny, nz });
                        if (Jumps.Count > 64)
                            Jumps.RemoveAt(0);
                        // A step still unanswered is from before the jump: its late answer must not move the landing.
                        Array.Clear(m_walking, 0, 256);
                        m_stepOrder.Clear();
                    }
                    PlayerBody(U16(p, 5));
                    X = nx;
                    Y = ny;
                    Facing = p[17] & 7;
                    Z = nz;
                    DrawAt = now;
                    Prune();
                }
                break;
            case 0x21:
                X = U16(p, 2);
                Y = U16(p, 4);
                Facing = p[6] & 7;
                Z = (sbyte)p[7];
                Array.Clear(m_walking, 0, 256);
                m_stepOrder.Clear();
                Prune();
                break;
            case 0x22:
                {
                    int w = m_walking[p[1]];
                    m_walking[p[1]] = 0;
                    if (m_stepOrder.Contains(p[1]))
                        while (m_stepOrder.Dequeue() != p[1])
                        {
                        }
                    if (w == 0)
                        break;
                    int dir = (w - 1) & 7;
                    // A step in a new direction only turns the player.
                    if (dir != Facing)
                    {
                        Facing = dir;
                        break;
                    }
                    int ox = X, oy = Y;
                    X += new[] { 0, 1, 1, 1, 0, -1, -1, -1 }[dir];
                    Y += new[] { -1, -1, 0, 1, 1, 1, 0, -1 }[dir];
                    // The server's own step rule from the tile left (UoMap.StepZ), the map's floor when it finds none.
                    int z = UoMap.StepZ(ox, oy, Z, X, Y);
                    if (z == int.MinValue)
                        z = UoMap.StandZ(X, Y, Z);
                    if (z != int.MinValue)
                        Z = z;
                    StepAt = Clock.UtcNow;
                    Prune();
                    break;
                }
            // Hits, mana and stamina updates between full statuses: serial, maximum, current (uo1998 sends these, and
            // the full 0x11 only when the client asks with 0x34).
            case 0xA1:
                if (p.Length >= 9 && U32(p, 1) == Player)
                {
                    MaxHits = U16(p, 5);
                    Hits = U16(p, 7);
                }
                break;
            case 0xA2:
                if (p.Length >= 9 && U32(p, 1) == Player)
                {
                    MaxMana = U16(p, 5);
                    Mana = U16(p, 7);
                }
                break;
            case 0xA3:
                if (p.Length >= 9 && U32(p, 1) == Player)
                {
                    MaxStam = U16(p, 5);
                    Stam = U16(p, 7);
                }
                break;
            // The paperdoll's line, name and title ("Astroglide, Master Ranger"): serial, 60 bytes of text.
            case 0x88:
                if (p.Length >= 65 && U32(p, 1) == Player)
                    Title = Ascii(p, 5, 60);
                break;
            case 0x11:
                if (p.Length >= 37)
                    Named(U32(p, 3), Ascii(p, 7, 30));
                if (U32(p, 3) != Player || p.Length < 43)
                    break;
                Name = Ascii(p, 7, 30);
                Hits = U16(p, 37);
                MaxHits = U16(p, 39);
                if (p[42] >= 1 && p.Length >= 66)
                {
                    Str = U16(p, 44);
                    Dex = U16(p, 46);
                    Int = U16(p, 48);
                    Stam = U16(p, 50);
                    MaxStam = U16(p, 52);
                    if (StrStart < 0)
                    {
                        StrStart = Str;
                        DexStart = Dex;
                        IntStart = Int;
                    }
                    Mana = U16(p, 54);
                    MaxMana = U16(p, 56);
                    Gold = (int)U32(p, 58);
                    Armor = U16(p, 62);
                    Weight = U16(p, 64);
                }
                break;
            case 0x3A:
                Skills3A(p);
                break;
            case 0x1C:
                {
                    uint who = U32(p, 3);
                    string name = Ascii(p, 14, 30);
                    string text = Ascii(p, 44, p.Length - 44);
                    if (who != 0xFFFFFFFF && name.Length > 0)
                        Named(who, name);
                    if (who != 0xFFFFFFFF && p[9] == 6)
                    {
                        if (Labelled.Count > MobileKeep)
                        {
                            Labelled = Labelled.Clear();
                            Invulnerable = Invulnerable.Clear();
                            Tame = Tame.Clear();
                        }
                        Labelled = Labelled.Add(who);
                        if (text.TrimEnd().EndsWith("(invulnerable)"))
                            Invulnerable = Invulnerable.Add(who);
                        if (text.IndexOf("(tame)", StringComparison.OrdinalIgnoreCase) >= 0)
                            Tame = Tame.Add(who);
                    }
                    // uo1998's scripts bark a refusal over the player's own head in hue 0x22 (barkToHued(user, user,
                    // 0x22, ...), rundir spelskil.m: "Your hands must be free to cast spells.", 2026-10-22 21:54:31);
                    // the spell's words come in 0x5A. The bark is the server's line, not the player's words.
                    uint by = who == Player && p.Length >= 12 && U16(p, 10) == BarkHue ? 0xFFFFFFFF : who;
                    Say(who == 0xFFFFFFFF || name.Length == 0 ? text : name + ": " + text, by,
                        who != 0xFFFFFFFF && p[9] == 6);
                    if (text.Contains("under the protection of the town guards"))
                        Guarded = true;
                    else if (text.Contains("left the protection of the town guards"))
                        Guarded = false;
                    // 0x1C: serial, body (7), type (9). Player speech: a named mobile with a human or human-ghost body
                    // (0x190 to 0x193) speaking normally (0), emoting (2), whispering (8) or yelling (9); labels (6)
                    // and system lines are not. A human NPC passes this test too; the window hides speakers by name.
                    int body = U16(p, 7), type = p[9];
                    if (who != 0xFFFFFFFF && (who & 0x40000000) == 0 && name.Length > 0 && name != "System"
                        && body >= 0x190 && body <= 0x193
                        && (type == 0 || type == 2 || type == 8 || type == 9))
                    {
                        Chat.Add(new[] { Clock.UtcNow.ToLocalTime().ToString("HH:mm"), name, text });
                        ChatSeq++;
                        if (Chat.Count > JournalKeep)
                            Chat.RemoveRange(0, Chat.Count - JournalKeep);
                    }
                    break;
                }
            case 0x6C:
                CursorOpen = true;
                CursorType = p[1];
                Cursor = U32(p, 2);
                CursorAt = Clock.UtcNow;
                break;
            case 0x1D:
                {
                    uint s = U32(p, 1);
                    AssistItem? was;
                    if (Items.TryGet(s, out was) && VendorBoxes.ContainsKey(was.Container)
                        && UoTiles.Stacks(was.Graphic))
                    {
                        if (m_wareGone.Count > ItemKeep)
                            m_wareGone = m_wareGone.Clear();
                        m_wareGone = m_wareGone.Set(s, new[] { (long)Math.Max(1, was.Amount), Clock.UtcNow.Ticks });
                    }
                    Items = Items.Remove(s);
                    if (Ground.TryGet(s, out was))
                        GroundGone = was;
                    Ground = Ground.Remove(s);
                    Mobiles = Mobiles.Remove(s);
                    if (s == Backpack)
                        Backpack = 0;
                    break;
                }
            case 0x25:
                if (UoTiles.Stacks(U16(p, 5)))
                    WareBack(U32(p, 1), U16(p, 8), U32(p, 14));
                Put(U32(p, 1), U16(p, 5), U16(p, 8), U16(p, 10), U16(p, 12), U32(p, 14), U16(p, 18));
                break;
            case 0x3C:
                {
                    int count = U16(p, 3);
                    for (int i = 0; i < count; i++)
                    {
                        int at = 5 + i * 19;
                        if (at + 19 > p.Length)
                            break;
                        if (i == 0)
                        {
                            uint box = U32(p, at + 13);
                            List<uint> gone = new();
                            foreach (KeyValuePair<uint, AssistItem> kv in Items)
                                if (kv.Value.Container == box)
                                    gone.Add(kv.Key);
                            foreach (uint s in gone)
                                Items = Items.Remove(s);
                        }
                        Put(U32(p, at), U16(p, at + 4), U16(p, at + 7), U16(p, at + 9), U16(p, at + 11),
                            U32(p, at + 13), U16(p, at + 17));
                    }
                    break;
                }
            case 0x2E:
                Wear(U32(p, 1), U16(p, 5), p[8], U32(p, 9), U16(p, 13));
                break;
            case 0x78:
                {
                    uint m = U32(p, 3) & 0x7FFFFFFF;
                    if (p.Length >= 9)
                        Body(m, U16(p, 7));
                    if (m != Player && p.Length >= 14)
                        Mobiles = Mobiles.Set(m, new[] { U16(p, 9), U16(p, 11), (sbyte)p[13] });
                    if (m != Player && p.Length > 18)
                        Noto(m, p[18]);
                    if (m != Player)
                    {
                        VendorGear(p, m);
                        break;
                    }
                    if (p.Length >= 9)
                        PlayerBody(U16(p, 7));
                    if ((U32(p, 3) & 0x80000000) == 0 && p.Length > 18)
                    {
                        Notoriety = p[18];
                        Poisoned = (p[17] & PoisonFlag) != 0;
                    }
                    for (int at = 19; at + 7 <= p.Length;)
                    {
                        uint item = U32(p, at);
                        if (item == 0)
                            break;
                        int g = U16(p, at + 4);
                        int hue = 0;
                        if ((g & 0x8000) != 0 && at + 9 <= p.Length)
                            hue = U16(p, at + 7);
                        Wear(item, g & 0x7FFF, p[at + 6], m, hue);
                        at += 7 + ((g & 0x8000) != 0 ? 2 : 0);
                    }
                    break;
                }
            case 0x1A:
                {
                    uint s = U32(p, 3);
                    int g = U16(p, 7);
                    int at = 9, amount = 1;
                    if ((s & 0x80000000) != 0)
                    {
                        amount = U16(p, 9);
                        at = 11;
                    }
                    // A graphic's 0x8000: a byte after the amount is added to it. uo1998 opens a door so: 86A5 then 01,
                    // the open art 0x06A6 (door 40001F3C, 2026-10-23 17:50:22), which read as the shut art moved out a
                    // tile.
                    if ((g & 0x8000) != 0)
                    {
                        g = ((g & 0x7FFF) + (at < p.Length ? p[at] : 0)) & 0x7FFF;
                        at++;
                    }
                    int xs = U16(p, at), ys = U16(p, at + 2);
                    int x = xs & 0x7FFF, y = ys & 0x3FFF;
                    at += 4;
                    if ((xs & 0x8000) != 0)
                        at++;
                    // After z: a hue (u16) when y's 0x8000 is set, then flags when its 0x4000 is.
                    int hue = (ys & 0x8000) != 0 && at + 3 <= p.Length ? U16(p, at + 1) : 0;
                    s &= 0x7FFFFFFF;
                    Items = Items.Remove(s);
                    if (Ground.Count >= ItemKeep && !Ground.ContainsKey(s))
                        break;
                    Ground = Ground.Set(s, new AssistItem { Serial = s, Graphic = g & 0x7FFF,
                        Amount = Counted(g & 0x7FFF, amount), X = x, Y = y, Z = at < p.Length ? (sbyte)p[at] : 0,
                        Hue = hue });
                    break;
                }
        }
    }

    // Another mobile's gear in its 0x78: a box at StockBoxLayer is its stock box.
    void VendorGear(byte[] p, uint m)
    {
        for (int at = 19; at + 7 <= p.Length;)
        {
            uint item = U32(p, at);
            if (item == 0)
                break;
            int g = U16(p, at + 4);
            if (p[at + 6] == StockBoxLayer)
            {
                if (VendorBoxes.Count > MobileKeep)
                    VendorBoxes = VendorBoxes.Clear();
                VendorBoxes = VendorBoxes.Set(item, m);
            }
            at += 7 + ((g & 0x8000) != 0 ? 2 : 0);
        }
    }
    // An item that does not stack is one item whatever amount the server sends: uo1998 sent a ringmail suit as gloves
    // 124, sleeves 123, leggings 122, tunic 121, and the bag pricer counted 490 items in a bag of four (2026-10-18).
    static int Counted(int graphic, int amount)
    {
        return UoTiles.Stacks(graphic) ? amount : 1;
    }
    void Put(uint s, int graphic, int amount, int x, int y, uint box, int hue)
    {
        Ground = Ground.Remove(s);
        if (Items.Count >= ItemKeep && !Items.ContainsKey(s))
        {
            SweepItems();
            if (Items.Count >= ItemKeep)
                return;
        }
        Items = Items.Set(s, new AssistItem { Serial = s, Graphic = graphic, Amount = Counted(graphic, amount), X = x,
            Y = y, Container = box, Hue = hue });
    }
    void Wear(uint s, int graphic, int layer, uint mobile, int hue)
    {
        if (mobile != Player)
            return;
        Ground = Ground.Remove(s);
        Items = Items.Set(s,
            new AssistItem { Serial = s, Graphic = graphic, Layer = layer, Mobile = mobile, Hue = hue });
        if (layer == 0x15)
            Backpack = s;
    }
    // Ground items and mobiles beyond GroundReach: a recall or a teleport sends no 0x1D for what was left behind, and a
    // stale mobile on a tile reads as Occupied to the walker.
    void Prune()
    {
        List<uint> far = new();
        foreach (KeyValuePair<uint, AssistItem> kv in Ground)
            if (Math.Max(Math.Abs(kv.Value.X - X), Math.Abs(kv.Value.Y - Y)) > GroundReach)
                far.Add(kv.Key);
        foreach (uint s in far)
            Ground = Ground.Remove(s);
        far.Clear();
        foreach (KeyValuePair<uint, int[]> kv in Mobiles)
            if (Math.Max(Math.Abs(kv.Value[0] - X), Math.Abs(kv.Value[1] - Y)) > GroundReach)
                far.Add(kv.Key);
        foreach (uint s in far)
            Mobiles = Mobiles.Remove(s);
        if (Math.Max(Math.Abs(X - m_itemsSweptX), Math.Abs(Y - m_itemsSweptY)) > GroundReach)
        {
            m_itemsSweptX = X;
            m_itemsSweptY = Y;
            SweepItems();
        }
    }
    // Contained items whose outermost container is not worn, on the ground in reach or a mobile in view: a corpse, a
    // chest or a vendor's stock left behind sends no 0x1D for its contents. Swept once the player is GroundReach away.
    int m_itemsSweptX, m_itemsSweptY;
    void SweepItems()
    {
        List<uint> gone = new();
        foreach (KeyValuePair<uint, AssistItem> kv in Items)
        {
            uint c = kv.Value.Container;
            AssistItem? up;
            for (int depth = 0; depth < 16 && c != 0 && c != Player && Items.TryGet(c, out up); depth++)
                c = up.Container;
            if (c != 0 && c != Player && !Items.ContainsKey(c) && !Ground.ContainsKey(c) && !Mobiles.ContainsKey(c))
                gone.Add(kv.Key);
        }
        foreach (uint s in gone)
            Items = Items.Remove(s);
    }

    // Type 0 is the full list ending in id 0; 0xFF one skill. The entry is (id, value) on the UOAIX shard and can carry
    // a base value after; the size is the one whose ids run 1, 2, 3 ... to the zero id.
    void Skills3A(byte[] p)
    {
        if (p.Length < 4)
            return;
        // The single update numbers its skill from 0, the full list from 1 (RunUO SkillUpdate; uo1998 sent
        // 3A0008FF001901F9, Magery 50.5, which read 1-based landed on Lockpicking).
        if (p[3] == 0xFF)
        {
            if (p.Length >= 8)
                Skill(U16(p, 4) + 1, U16(p, 6));
            return;
        }
        if (p[3] != 0)
            return;
        for (int size = 4; size <= 6; size += 2)
        {
            bool ok = true;
            int at = 4, id = 1;
            for (; at + 2 <= p.Length; at += size, id++)
            {
                int got = U16(p, at);
                if (got == 0)
                    break;
                if (got != id || at + size > p.Length)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok || at + 2 > p.Length)
                continue;
            SkillEntry = size;
            for (at = 4; U16(p, at) != 0; at += size)
                Skill(U16(p, at), U16(p, at + 2));
            return;
        }
        throw new Exception("skill list fits neither 4- nor 6-byte entries");
    }
    void Skill(int id, int value)
    {
        Skills = Skills.Set(id, value);
        if (!SkillStart.ContainsKey(id))
            SkillStart = SkillStart.Set(id, value);
    }

    public int Count(uint box)
    {
        int n = 0;
        foreach (KeyValuePair<uint, AssistItem> kv in Items)
            if (kv.Value.Container == box)
                n++;
        return n;
    }

    public string Summary()
    {
        lock (Gate)
        {
            int learned = 0, gained = 0;
            foreach (KeyValuePair<int, int> kv in Skills)
            {
                if (kv.Value > 0)
                    learned++;
                if (kv.Value != SkillStart.Get(kv.Key, kv.Value))
                    gained++;
            }
            int worn = 0;
            foreach (KeyValuePair<uint, AssistItem> kv in Items)
                if (kv.Value.Layer >= 0)
                    worn++;
            return "player " + Player.ToString("X") + " '" + Name + "' at " + X + "," + Y + "," + Z + " hits " + Hits
                + "/" + MaxHits
                + " str " + Str + " dex " + Dex + " int " + Int + " gold " + Gold + " weight " + Weight
                + " skills " + Skills.Count + " (" + learned + " above 0, " + gained + " moved, entry " + SkillEntry
                + ")"
                + " worn " + worn + " pack "
                + (Backpack == 0 ? "?" : Backpack.ToString("X") + " holding " + Count(Backpack))
                + " items " + Items.Count + " ground " + Ground.Count + " journal " + Journal.Count
                + (CursorOpen ? " cursor " + Cursor.ToString("X") + " type " + CursorType : " no cursor")
                + (Fault != null ? " FAULT " + Fault : "");
        }
    }
}
