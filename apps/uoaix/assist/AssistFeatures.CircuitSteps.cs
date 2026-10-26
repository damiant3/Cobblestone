#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the tailoring circuit's steps (assist-plan.md stage 6, step 1 and 2) ----------
    // "vendor buy" and "vendor sell" open the shop windows (0x24 gump 0x30 with the stock 0x3C and the buy list 0x74;
    // the sell list 0x9E); while the assist asks (ShopClaimMs from its line) those windows go to the assist and not to
    // the client's screen, and the assist answers them as the client would.
    public const int ShopClaimMs = 4000, GoldArt = 0x0EED, BuyGump = 0x30, BuySetMs = 1000;
    DateTime m_shopClaimUntil = DateTime.MinValue;
    bool HoldShop(byte[] p)
    {
        // A bag the assist opened to read (Peek) shows no window on the client (Damian, 2026-10-17: "lets just inject
        // the right messages instead of user emulation of clicks unless absolutely necessary").
        if (p[0] == 0x24 && p.Length >= 7)
        {
            uint s = U32(p, 1);
            lock (m_peekingGate)
                if (m_peeking.Contains(s))
                {
                    m_peeking = m_peeking.Remove(s);
                    return true;
                }
        }
        if (Clock.UtcNow > m_shopClaimUntil)
            return false;
        return p[0] == 0x74 || p[0] == 0x9E || (p[0] == 0x24 && p.Length >= 7 && ((p[5] << 8) | p[6]) == BuyGump);
    }
    // Opens a container for its contents (0x3C) without a window on the client. Null, or why not.
    Set<uint> m_peeking = Set<uint>.Empty; readonly Lock m_peekingGate = new();
    public string? Peek(uint bag)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        using (Act(k))
        {
            ManualResetEventSlim got = new(false);
            Action<byte[]> h = p => { if (p[0] == 0x3C && p.Length >= 5 && ((p[3] << 8) | p[4]) > 0
                && U32(p, 5 + 13)
                == bag) got.Set(); else if (p[0] == 0x24 && p.Length >= 7 && U32(p, 1) == bag) got.Set(); };
            lock (m_peekingGate)
                m_peeking = m_peeking.Add(bag);
            k.ServerPacket += h;
            // The open is a double-click: it leaves the player busy, and the next action waits it out (a loom's thread
            // used 1.2 s after a peek was refused, "You must wait to perform another action.", 2026-10-17 00:34:15).
            bool opened;
            try
            {
                string? w = k.Use(bag, false);
                if (w != null)
                    return w;
                m_harvestActAt = clickedAt = Clock.UtcNow;
                opened = Clock.Wait(got, AnswerMs);
                Clock.Sleep(200);
            }
            finally { k.ServerPacket -= h; lock (m_peekingGate) m_peeking = m_peeking.Remove(bag); }
            return opened ? null : "the bag " + bag.ToString("X") + " sent no contents in " + AnswerMs + " ms";
        }
    }
    // A marked rune renamed (Damian, 2026-10-17: "you can 'use' a rune to rename it"): its double-click asks for a
    // description by a text prompt (0x9A: serial, prompt id, type, text), kept off the client, and answered as the
    // client answers one (0x9A: the serial and prompt id echoed, type 1 for ok, the text and its NUL). Null, or why
    // not.
    DateTime m_promptClaimUntil = DateTime.MinValue;
    bool HoldPrompt(byte[] p)
    {
        return p[0] == 0x9A && Clock.UtcNow <= m_promptClaimUntil;
    }
    public string? Rename(uint rune, string text)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        using (Act(k))
        {
            BusyWait(clickedAt);
            byte[]? prompt = null;
            ManualResetEventSlim got = new(false);
            Action<byte[]> h = p => { if (p[0] == 0x9A && p.Length >= 15
                && U32(p, 3) == rune) { prompt = p; got.Set(); } };
            k.ServerPacket += h;
            m_promptClaimUntil = Clock.UtcNow.AddMilliseconds(AnswerMs);
            try
            {
                string? w = k.Use(rune, false);
                if (w != null)
                    return w;
                clickedAt = Clock.UtcNow;
                Clock.Wait(got, AnswerMs);
            }
            finally { k.ServerPacket -= h; m_promptClaimUntil = DateTime.MinValue; }
            if (prompt == null)
                return "the rune " + rune.ToString("X") + " asked for no description in " + AnswerMs + " ms";
            k.Settle();
            byte[] t = Encoding.ASCII.GetBytes(text);
            byte[] r = new byte[16 + t.Length];
            r[0] = 0x9A;
            r[1] = (byte)(r.Length >> 8);
            r[2] = (byte)r.Length;
            Array.Copy(prompt, 3, r, 3, 8);
            r[14] = 1;
            t.CopyTo(r, 15);
            string? why = k.Inject(r, "rename " + rune.ToString("X"), false);
            if (why != null)
                return why;
            // The bank's label cache reads a rune once by serial, so a renamed rune is relabelled there too.
            LoadBankRunes();
            lock (m_bankRunesGate)
            {
                if (m_bankRunes.ContainsKey(rune))
                {
                    m_bankRunes = m_bankRunes.Set(rune, text);
                    List<string> lines = new();
                    foreach (KeyValuePair<uint, string> kv in m_bankRunes)
                        lines.Add(kv.Key.ToString("X") + "\t" + kv.Value);
                    try
                    {
                        File.WriteAllLines(BankRunesPath, lines);
                    }
                    catch { }
                }
            }
            return null;
        }
    }
    // Every bag in the pack whose contents are not known opened once, so the pack's item count is the server's; a book
    // is not a bag (the spellbook's scrolls came in as items), and the next action waits out the last open ("You must
    // wait to perform another action." at 09:10:22, a kit used 375 ms after it).
    void OpenPackBags(GameLink k)
    {
        List<uint> bags = new();
        // Nor a keyring: its tile is a container, and its double-click asks "What do you want to unlock?" with a cursor
        // that went to the client (2026-10-20 20:42:45).
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && PackBag(it.Graphic))
                    bags.Add(it.Serial);
        }
        bool opened = false;
        foreach (uint b in bags)
        {
            bool known;
            lock (m_contentsGate)
                known = m_contents.ContainsKey(b);
            if (known)
                continue;
            BusyWait(clickedAt);
            string? w = Peek(b);
            opened = true;
            if (w != null)
                Note("Pack: opening " + b.ToString("X") + ": " + w);
        }
        if (opened)
            BusyWait(clickedAt);
    }
    // Whether the pack's counts are incomplete: the pack, or a bag in it, not opened on this connection (Damian,
    // 2026-10-18: "there should be a 'count' button there in the header if the data is unintialized or stale").
    public bool PackStale()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return false;
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        if (pack == 0)
            return true;
        lock (m_contentsGate)
            if (!m_contents.ContainsKey(pack))
                return true;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && PackBag(it.Graphic))
                    lock (m_contentsGate)
                        if (!m_contents.ContainsKey(it.Serial))
                            return true;
        }
        return false;
    }
    // The pack, then every bag in it not yet known, opened once each. Null, or why not.
    public string? CountPack()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        if (pack == 0)
            return "the pack is not known: double-click yourself once";
        bool known;
        lock (m_contentsGate)
            known = m_contents.ContainsKey(pack);
        if (!known)
        {
            BusyWait(clickedAt);
            string? w = Peek(pack);
            if (w != null)
                return "opening the pack: " + w;
        }
        OpenPackBags(k);
        return null;
    }
    static int PackCount(GameLink k, Func<int, bool> graphic)
    {
        return Inv(k).Count(graphic);
    }
    // A vendor trades only inside its shop, a small range; engaged elsewhere it teleports home, or says something and
    // walks home slowly (Damian, 2026-10-17: "you have to wait till they get there. stay out of their way until they
    // land home"). Asked by name and unanswered, the asking waits until that vendor has stood still VendorStillMs (at
    // most VendorHomeMs), then asks again, ShopAsks times in all. The circuit never walks, so it keeps out of the way.
    // Asked once where it stands (Damian, 2026-10-17: "it shouldn't repeated ask to buy over and over again ... or any
    // command"); AskNear's one more ask goes out only at the vendor's home.
    // Home is told by timing, as nothing announces it (Damian, 2026-10-17: "about 1 second ticks on walks, then a 5
    // second stop is usually an indication they are home"): the vendor's tile from the server's mobile updates, home
    // once it has not moved VendorStillMs; a jump of more than VendorJump tiles is a teleport home ("sometimes they
    // teleport when you ask, and immediate land on home"), home after VendorLandMs still.
    // Where the vendor's region is known (VendorAtHome), home is that region and nothing else: the wait ends when the
    // vendor stands in it. False when the wait ran out with the vendor away.
    public const int ShopAsks = 1, VendorJump = 3;
    int VendorHomeMs => Ms("vendor-home");
    int VendorStillMs => Ms("vendor-still"); int VendorLandMs => Ms("vendor-land");
    // sentHome: the vendor has just answered "Let us go back to my shop", which overrules the shop region: it is home
    // only once it has moved and then stood still, and one that never moves in VendorHomeMs stayed away (Allison in The
    // Sorcerer's Delight, inside her region, was asked three times in 5 s and ended the buy circuit, 2026-10-18
    // 12:46:45).
    bool WaitVendorHome(GameLink k, string who, bool sentHome = false)
    {
        AtlasNpc? v = VendorNamed(k, who);
        if (v == null)
        {
            Clock.Sleep(VendorStillMs);
            return true;
        }
        DateTime until = Clock.UtcNow.AddMilliseconds(VendorHomeMs);
        int lx = int.MinValue, ly = 0, need = VendorStillMs, moves = 0;
        DateTime still = Clock.UtcNow;
        while (Clock.UtcNow < until && !k.Closed && !circuitStop)
        {
            bool? home = VendorAtHome(k, v.Serial);
            if (home == true && !sentHome)
                return true;
            int[]? at;
            bool seen;
            seen = k.State.Mobiles.TryGet(v.Serial, out at);
            if (seen && (at![0] != lx || at[1] != ly))
            {
                need = lx != int.MinValue && Tiles(at[0], at[1], lx, ly) > VendorJump ? VendorLandMs : VendorStillMs;
                if (lx != int.MinValue)
                    moves++;
                lx = at[0];
                ly = at[1];
                still = Clock.UtcNow;
            }
            if ((sentHome ? moves > 0 : home == null) && (Clock.UtcNow - still).TotalMilliseconds >= need)
                return true;
            Clock.Sleep(100);
        }
        return !sentHome && VendorAtHome(k, v.Serial) != false;
    }
    // The shopkeeper by first name, the one in view nearest the player.
    AtlasNpc? VendorNamed(GameLink k, string who)
    {
        if (Atlas == null || who == "vendor")
            return null;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        AtlasNpc? best = null;
        foreach (AtlasNpc n in Atlas.Visible())
            if (string.Equals(n.Name.Split(' ')[0], who, StringComparison.OrdinalIgnoreCase)
                && (best == null || Tiles(n.X, n.Y, px, py) < Tiles(best.X, best.Y, px, py)))
                best = n;
        return best;
    }
    // Each vendor's home as the server judges it (human.m: the smallest region holding the vendor, z included, named as
    // its myHomeShop): Cybil's spot on the Magincia mage shop roof, z 60, lies above MAGEGUILD_MAGINCIA's z 20..59, and
    // she answered every ask there "Let us go back to my shop" (2026-10-17 02:56). Home is the region key
    // (UoRegions.AreaKey) it answered in, "*" once it answered in two (a vendor with no region trades anywhere); Away
    // the keys it sent the player home from. Kept in vendor-homes-<server>.txt (serial, home, away keys by commas).
    sealed class VendorHome
    {
        public string Home = ""; public Set<string> Away = Set<string>.Empty;
    }
    Map<uint, VendorHome> m_vendorHomes = Map<uint, VendorHome>.Empty; bool m_vendorHomesLoaded;
    readonly Lock m_vendorHomesGate = new();
    string VendorHomesPath => ServerFile("vendor-homes");
    void LoadVendorHomes()
    {
        lock (m_vendorHomesGate)
            LoadOnce(ref m_vendorHomesLoaded, VendorHomesPath,
                line => { string[] t = line.Split('\t'); if (t.Length < 2
                || t.Length > 3) return; VendorHome h = new() { Home = t[1] }; if (t.Length
                > 2) foreach (string a in t[2].Split(new[] { ',' },
                StringSplitOptions.RemoveEmptyEntries)) h.Away = h.Away.Add(a);
                m_vendorHomes = m_vendorHomes.Set(Convert.ToUInt32(t[0], 16), h); });
    }
    void SaveVendorHomes()
    {
        List<string> lines = new();
        foreach (KeyValuePair<uint, VendorHome> kv in m_vendorHomes)
            lines.Add(kv.Key.ToString("X") + "\t" + kv.Value.Home + "\t"
                + string.Join(",", kv.Value.Away.Ordered(StringComparer.Ordinal)));
        try
        {
            File.WriteAllLines(VendorHomesPath, lines);
        }
        catch { }
    }
    // The region key where the vendor stands now, with where for a report; null when out of view or no region holds it.
    string? VendorArea(GameLink k, uint serial, out string where)
    {
        int[]? at;
        where = "out of view";
        if (!k.State.Mobiles.TryGet(serial, out at))
            return null;
        string? key = UoRegions.AreaKey(at[0], at[1], at[2]);
        where = (key ?? "no region") + " at " + at[0] + "," + at[1] + " z " + at[2];
        return key;
    }
    // True at home, false only where it sent the player home from ("Let us go back to my shop"), null otherwise:
    // outside its known home is not away (Damian, 2026-10-19, of Moonglow's Brian, waited on at 4455,1072 where he
    // trades and which no CLOTHIER region holds: "he never said that").
    bool? VendorAtHome(GameLink k, uint serial)
    {
        string? where, key = VendorArea(k, serial, out where);
        if (key == null)
            return null;
        LoadVendorHomes();
        VendorHome? h;
        lock (m_vendorHomesGate)
        {
            if (!m_vendorHomes.TryGet(serial, out h))
                return null;
            if (h.Away.Contains(key))
                return false;
            if (h.Home == "*" || h.Home == key)
                return true;
            return null;
        }
    }
    void NoteVendorHome(GameLink k, string who)
    {
        AtlasNpc? v = VendorNamed(k, who);
        if (v == null)
            return;
        string? where, key = VendorArea(k, v.Serial, out where);
        if (key == null)
            return;
        LoadVendorHomes();
        lock (m_vendorHomesGate)
        {
            VendorHome? h;
            if (!m_vendorHomes.TryGet(v.Serial, out h))
                m_vendorHomes = m_vendorHomes.Set(v.Serial, h = new VendorHome());
            string home = h.Home.Length == 0 || h.Home == key ? key : "*";
            if (home == h.Home && !h.Away.Contains(key))
                return;
            h.Home = home;
            h.Away = h.Away.Remove(key);
            SaveVendorHomes();
        }
    }
    void NoteVendorAway(GameLink k, string who)
    {
        AtlasNpc? v = VendorNamed(k, who);
        if (v == null)
            return;
        string? where, key = VendorArea(k, v.Serial, out where);
        if (key == null)
            return;
        LoadVendorHomes();
        lock (m_vendorHomesGate)
        {
            VendorHome? h;
            if (!m_vendorHomes.TryGet(v.Serial, out h))
                m_vendorHomes = m_vendorHomes.Set(v.Serial, h = new VendorHome());
            if (h.Home == key || h.Home == "*" || h.Away.Contains(key))
                return;
            h.Away = h.Away.Add(key);
            SaveVendorHomes();
        }
        Note("Circuit: " + who + " is away from its shop in " + where);
    }
    // The vendor's home region nearest it: centre and z range, or null when its home is not known.
    int[]? VendorHomeRegion(GameLink k, AtlasNpc v)
    {
        LoadVendorHomes();
        VendorHome? h;
        lock (m_vendorHomesGate)
            if (!m_vendorHomes.TryGet(v.Serial, out h) || h.Home.Length == 0 || h.Home == "*")
                return null;
        return UoRegions.AreaNearest(h.Home, v.X, v.Y);
    }

    // The floor a walk to a vendor's home region aims at: the region's low z, unless that is the region file's -127,
    // no bound (the 1998 regions-1998.txt), when any floor: aimed at z -127 the walk found no tile ("no way from
    // 652,2179 z 0 to within 2 of 652,2184 z -127", Lynn's shop in Skara Brae, 2026-10-24).
    public const int RegionNoFloor = -127;

    internal static int HomeFloor(int[] home) => home[2] <= RegionNoFloor ? AnyFloor : home[2];
    // A shop ask goes out SellGapMs after the last shop window: "Slade buy" said the instant Bertram's window came was
    // answered by Bertram and never by Slade, which ended the survey (2026-10-17 02:12:40).
    DateTime m_lastShopWindow = DateTime.MinValue;
    byte[]? AskShop(GameLink k, string who, string what, int opcode)
    {
        for (int ask = 1; ask <= ShopAsks; ask++)
        {
            TimeSpan wait = m_lastShopWindow.AddMilliseconds(SellGapMs) - Clock.UtcNow;
            if (wait > TimeSpan.Zero)
                Clock.Sleep((int)Math.Ceiling(wait.TotalMilliseconds));
            byte[]? got = SayFor(k, who + " " + what, opcode, AnswerMs, who);
            if (got != null)
                m_lastShopWindow = Clock.UtcNow;
            if (got == null && LastReply != null)
                return null;
            if (got != null)
                return got;
            if (ask < ShopAsks)
            {
                Note("Circuit: " + who + " did not answer \"" + what + "\"; waiting for them to be home");
                WaitVendorHome(k, who);
            }
        }
        return null;
    }
    // A spoken line answered by a window (opcode) or by speech, and both are watched (Damian, 2026-10-17: "a command
    // that might open a window or return a string in chat needs to be not one or the other checked, but both"): the
    // window wins; speech from the one asked (from, a name; null for anyone) with no window within ReplyGraceMs is the
    // answer, kept in LastReply ("I don't understand", "You have nothing I would be interested in.", "You can only sell
    // 5 items at a time!"). A greeting before the window ("Greetings.  Have a look around.") waits out the grace for
    // it.
    int ReplyGraceMs => Ms("reply-grace");
    // SayRefused: why the line was not said (GameLink.Say refused it), null when it went out; the answer is then null
    // with no LastReply, and the caller reports the refusal, not a silent NPC.
    public volatile string? LastReply = null, SayRefused = null;
    // OtherWindow: the asked window came during the wait, but from another vendor than the one asked.
    public volatile bool OtherWindow;
    // NotHome: why AskNear passed the vendor over unasked, away from its shop (VendorAtHome); null when it asked.
    public volatile string? NotHome = null;
    // A spoken answer can take longer than an action's: Simon's came 1.55 s after the ask, past an auto-tuned 1500 ms
    // answer wait, and was read as silence (Trinsic, 2026-10-17). The wait is SpeechWaitMs at least; a window ends it
    // at once.
    int SpeechWaitMs => Ms("speech-wait");
    byte[]? SayFor(GameLink k, string line, int opcode, int ms, string? from = null)
    {
        ms = Math.Max(ms, SpeechWaitMs);
        LastReply = null;
        SayRefused = null;
        OtherWindow = false;
        byte[]? got = null;
        string? reply = null;
        DateTime replyAt = DateTime.MaxValue;
        ManualResetEventSlim done = new(false);
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        // A window answers only from the vendor asked, where its name is known: Judd's buy list arrived as the answer
        // to "Tate buy" (Vesper, 2026-10-17), and Tate's thread was recorded as none. The sell list (0x9E) names its
        // vendor; the buy list (0x74) names the stock box, worn by its vendor.
        Func<byte[], bool> fromAsked = p =>
        {
            if (from == null || from == "vendor" || p.Length < 7)
                return true;
            uint v = U32(p, 3);
            if (p[0] == 0x74)
            {
                uint wv;
                if (!m_wornBy.TryGet(v, out wv))
                    return true;
                v = wv;
            }
            string? n;
            if (!k.State.Names.TryGet(v, out n))
                return true;
            return string.Equals(n.Split(' ')[0], from, StringComparison.OrdinalIgnoreCase);
        };
        // A container window from the pack is the player's own open, never the answer: Kwik's double-click on his pack
        // opened it 80 ms after "bank" was said, the circuit took the pack for the bank box, read its spellbook as the
        // toolbag and banked the gold into the pack (2026-10-18 20:56:31).
        Func<byte[], bool> packWindow = p => { if (p[0] != 0x24
            || p.Length < 5) return false; uint s = U32(p, 1); lock (k.State.Gate) return s == k.State.Backpack
            || PackTree(k).Contains(s); };
        bool gumpSeen = false;
        Action<byte[]> h = p =>
        {
            if (p[0] == 0x24 && p.Length >= 7 && ((p[5] << 8) | p[6]) == BuyGump)
                gumpSeen = true;
            if (p[0] == opcode && got == null)
            {
                if (packWindow(p))
                    return;
                if (!fromAsked(p))
                {
                    OtherWindow = true;
                    return;
                }
                got = p;
                done.Set();
                return;
            }
            // 0x1C: serial, body, type (9), hue, font, name (14, 30), text (44); a label (6) is not an answer.
            if (p[0] == 0x1C && p.Length > 44 && reply == null)
            {
                uint s = U32(p, 3);
                if (s == me || s == 0xFFFFFFFF || p[9] == 6)
                    return;
                string name = Encoding.ASCII.GetString(p, 14, 30).TrimEnd('\0');
                if (from != null && from != "vendor" && !string.Equals(name, from, StringComparison.OrdinalIgnoreCase))
                    return;
                reply = name + ": " + Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0');
                replyAt = Clock.UtcNow;
            }
        };
        k.ServerPacket += h;
        m_shopClaimUntil = Clock.UtcNow.AddMilliseconds(ShopClaimMs);
        try
        {
            string? refused = k.Say(line);
            if (refused != null)
            {
                SayRefused = refused;
                return null;
            }
            DateTime by = Clock.UtcNow.AddMilliseconds(ms);
            while (Clock.UtcNow < by && !k.Closed)
            {
                if (Clock.Wait(done, 50))
                    break;
                // Anyone's speech (from null, the bank's "bank") is kept but never ends the wait: chatter nearby is not
                // an answer.
                if (reply != null && from != null && (Clock.UtcNow - replyAt).TotalMilliseconds >= ReplyGraceMs)
                    break;
            }
            // A buy ask's set ends with its window (0x24 gump 0x30), after a 0x74 for each of the vendor's boxes: the
            // claim ended on the first 0x74, and the second and the window reached the client, which showed a buy
            // window of "(null)" at 0 gp (Damian, 2026-10-20: "increasing numbers of shop inventory with (null) at
            // 0gp"; Brian's set, 15:33:40.279 to .281).
            if (got != null && opcode == 0x74)
            {
                Wait.Until(Clock, () => gumpSeen, BuySetMs, "the buy gump", closed: () => k.Closed, stepMs: 10);
            }
            // The claim ends with the ask: kept its whole ShopClaimMs, it held the player's own "vendor buy" said 0.3 s
            // inside it after Watch me sell's last ask, and no window opened (2026-10-20 11:46:46). The answer (0x9E,
            // or 0x24 after its 0x74 lists) is the last packet of its set.
        }
        finally { k.ServerPacket -= h; m_shopClaimUntil = DateTime.MinValue; }
        LastReply = got == null ? reply : null;
        if (got != null)
            k.Heard(line);
        return got;
    }
    // A line said from the control port, answered when it opens a window (0x24) within AnswerMs. Null, or why not said.
    public string? SayHeard(string text)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        using (Act(k))
        {
            SayFor(k, text, 0x24, AnswerMs);
            return SayRefused;
        }
    }
    // Why "bank" opened no box: the line refused before it went out, or no banker answered it.
    string NoBanker()
    {
        string? r = SayRefused;
        return r != null ? "\"bank\" not said: " + r : "no banker answered \"bank\"";
    }
    // What an NPC's spoken answer means, from the lines uo1998 gave on 2026-10-17; a line matching none is Unknown, and
    // goes up to whoever drives the assist (Damian, 2026-10-17: "these unknown text responses that the bot gets need to
    // be kicked up to you monitoring so you can interpret any new text we haven't seen before").
    public enum Reply
    {
        None, Vendor, NotTrader, NoInterest, GoingHome, AskAgain, SellLimit, Unknown
    }
    // The specific kinds first: "I do not have my wares here with me" also holds a greeting's "my wares". The wording
    // can change any day (Damian, 2026-10-17: "tomorrow the devs could change the reponse slightly so we need agent to
    // pay attention here"), and a changed line falls to Unknown and is kicked up rather than guessed at.
    static readonly string[][] s_replyLines = {
        // "... thou must give it to me and I'll decide if I need it in my stock" (Simon, Trinsic, 2026-10-17, asked
        // away from his post after buying 99): the vendor not at its shop spot (Damian: "so that vendor walked back to
        // its shop space"); asked once more there.
        new[] { "GoingHome", "do not have my wares here", "go back to my shop", "thou must give it to me" },
        new[] { "NotTrader", "do not understand", "fail to follow", "do not quite follow", "meaning escapes",
        "fail to grasp" },
        // "... hand it over and I shall determine its value." (Jannelle, Nujel'm, after buying 5) and "Hand it to me
        // and I shall decide its worth." (Sophie, Moonglow): misleading, not a refusal (Damian, 2026-10-17: "the hand
        // it to me line is misleading"; he asked again and sold); one more ask after SellGapMs.
        // The rest of the 1998 shopkeeper conversation's answers to "sell" for a player of good name (rundir/convo/
        // shopkeep.frg, high, medium and low sophistication), one picked at random: Michelle's "I may be interested in
        // what thou have-let me see it." after buying 10 (2026-10-17 03:30) ended the circuit as unknown.
        new[] { "AskAgain", "hand it over and i shall determine", "hand it to me and i shall decide", "let me see it",
        "give it to me and i shall value",
            "hand them to me for appraisal", "let me see what thou art offering", "give it here",
            "hand it over and i'll see", "give 'em to me for a once-over",
            "give me what thou'rt wantin' to sell" },
        // "I am a tailor. I could purchase those things that I supply." (Nana, Magincia, 2026-10-17, asked 0.7 s after
        // a sale of 5, see SellGapMs): no window this time; the circuit moves on from her rather than stopping.
        // "I can't afford any more of that right now.  You might try other adventurers." (Kimmie, Jhelom, 2026-10-19,
        // after buying 20): the vendor's gold is spent; the circuit moves on to the next tailor.
        new[] { "NoInterest", "nothing i would be interested in", "purchase those things that i supply",
        "can't afford any more", "cannot afford any more" },
        new[] { "SellLimit", "only sell 5 items" },
        new[] { "Vendor", "have a look around", "thou seekest", "sell to thee", "buy from me", "my wares",
        "purchase supplies", "my shop sells", "greetings" },
    };
    public static Reply KindOf(string reply)
    {
        if (reply == null)
            return Reply.None;
        string t = reply.Substring(reply.IndexOf(": ") + 2).ToLowerInvariant();
        foreach (string[] row in s_replyLines)
            for (int i = 1; i < row.Length; i++)
                if (t.Contains(row[i]))
                    return (Reply)Enum.Parse(typeof(Reply), row[0]);
        return Reply.Unknown;
    }
    public readonly List<string> UnknownReplies = new();
    void KickUp(string asked, string reply)
    {
        string row = Clock.UtcNow.ToLocalTime().ToString("HH:mm:ss") + "\t" + Clean(asked) + "\t" + Clean(reply);
        lock (UnknownReplies)
            UnknownReplies.Add(row);
        Note("UNKNOWN REPLY to \"" + asked + "\": " + reply);
        try
        {
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "unknown-replies.txt"),
                row + Environment.NewLine);
        }
        catch { }
    }
    // An ask that got no proper answer (not said, nobody answered, not a trader, an unknown line, or a greeting with no
    // window): the code that asked stops there (assist-plan.md rule 1) rather than asking the next vendor. "Nothing I
    // would be interested in" and an offer under the floor are answers, and the circuit moves on from them.
    static bool Silent(string why)
    {
        if (why == null)
            return false;
        foreach (string s in new[] { "no vendor answered", "not said", "UNKNOWN REPLY", "NotTrader", "GoingHome",
            "Vendor " })
            if (why.StartsWith(s))
                return true;
        return false;
    }
    // Why an ask failed, in the words given: the reply and its kind, or that nobody answered.
    string NoAnswer(string who, string word)
    {
        if (NotHome != null)
            return "away: " + NotHome;
        string? r = LastReply;
        if (r == null)
            return SayRefused != null ? "not said: " + SayRefused : "no vendor answered \"" + who + " " + word + "\"";
        Reply kind = KindOf(r);
        return (kind == Reply.Unknown ? "UNKNOWN REPLY " : kind + " ") + who + " answered \"" + word + "\" with \""
            + r.Substring(r.IndexOf(": ") + 2) + "\"";
    }
    // Each vendor's last thread buy and last sale (Damian, 2026-10-20: "circuit vendors needs a "last seen inventory"
    // and "last attempt at sold shirts" and success or fail to buy thread or sell shirt"): the time and the result,
    // kept in attempts-<server>.txt (serial, kind, ticks, text; a later line for the same vendor and kind wins).
    Map<string, Tuple<DateTime, string>> m_attempts = Map<string, Tuple<DateTime, string>>.Empty;
    bool m_attemptsLoaded; readonly Lock m_attemptsGate = new();
    void LoadAttempts()
    {
        lock (m_attemptsGate)
            LoadOnce(ref m_attemptsLoaded, ServerFile("attempts"),
                line => { string[] t = line.Split('\t'); if (t.Length == 4) m_attempts = m_attempts.Set(t[0] + t[1],
                Tuple.Create(new DateTime(long.Parse(t[2])), t[3])); });
    }
    void Attempt(uint vendor, string kind, string text)
    {
        if (vendor == 0)
            return;
        LoadAttempts();
        DateTime now = Clock.UtcNow.ToLocalTime();
        text = (text ?? "").Replace('\t', ' ').Replace('\n', ' ');
        lock (m_attemptsGate)
        {
            m_attempts = m_attempts.Set(vendor.ToString("X") + kind, Tuple.Create(now, text));
            try
            {
                File.AppendAllText(ServerFile("attempts"),
                    vendor.ToString("X") + "\t" + kind + "\t" + now.Ticks + "\t" + text + Environment.NewLine);
            }
            catch { }
        }
    }
    public string LastAttempt(uint vendor, string kind)
    {
        LoadAttempts();
        Tuple<DateTime, string>? a;
        if (m_attempts.TryGet(vendor.ToString("X") + kind, out a))
            return a.Item1.ToString("HH:mm") + " " + a.Item2;
        return "";
    }
    public bool HasAttempts(uint vendor)
    {
        LoadAttempts();
        Map<string, Tuple<DateTime, string>> attempts = m_attempts;
        return attempts.ContainsKey(vendor.ToString("X") + "buy") || attempts.ContainsKey(vendor.ToString("X") + "sell");
    }
    public List<uint> AttemptVendors()
    {
        LoadAttempts();
        List<uint> l = new();
        foreach (KeyValuePair<string, Tuple<DateTime, string>> kv in m_attempts.Ordered(StringComparer.Ordinal))
        {
            string key = kv.Key;
            uint v;
            if (uint.TryParse(key.Substring(0, key.Length - (key.EndsWith("sell") ? 4 : 3)),
                NumberStyles.HexNumber, null, out v) && !l.Contains(v))
                l.Add(v);
        }
        return l;
    }
    // Buys up to want of the stock whose art is one of graphics, as far as the pack's gold goes: every such stack
    // bought out ("each time if they have sold out of thread, the next respawn gets more"). Null, or why not; bought
    // and the stock left come back.
    // who: "vendor" asks every vendor in earshot; a vendor's name ("Morena buy") asks that one (Damian, 2026-10-17:
    // "you can use the name of the vendor").
    // maxPrice: a stock priced above it is not bought (Damian, 2026-10-17: "250 of them @5gp each").
    // allOrNone: the vendor's whole stock of it or none (Damian, 2026-10-20: "it should only buy all the thread or no
    // thread ... it could have attempted to make shirts and sell them to build the capital to buy the thread. now the
    // thread has a chance to cost more per"; 11 of Simon's 73 bought with 35 gold); short of it, PurseShort.
    public const string PurseShort = "all or nothing: ";
    // The stock of the asked arts the last buy list showed (MakeRoom's measure of a stock that did not fit), and its
    // whole price (Keep thread's purse when the prediction fell short).
    volatile int m_lastBuyShown, m_lastBuyWhole;
    // Each vendor's last buy window: when, and the arts its stock held. A vendor whose window within StockSeenMs showed
    // none of an art is not asked for it again (L-FUTILITY; 2026-10-24 14:00:42, Michelle asked "buy" for thread, then
    // for kits 0.9 s later, "the vendor has none" both times).
    sealed class StockSeen
    {
        public DateTime At; public Set<int> Arts = null!;
    }
    Map<uint, StockSeen> m_stockSeen = Map<uint, StockSeen>.Empty; readonly Lock m_stockSeenGate = new();
    public const int StockSeenMs = 60000;
    public bool ShownWithout(uint vendor, Func<int, bool> graphic)
    {
        StockSeen? s;
        if (!m_stockSeen.TryGet(vendor, out s))
            return false;
        if ((Clock.UtcNow - s.At).TotalMilliseconds > StockSeenMs)
            return false;
        foreach (int a in s.Arts)
            if (graphic(a))
                return false;
        return true;
    }
    public string? VendorBuy(Func<int, bool> graphic, int want, out int bought, out int left, string who = "vendor",
        int maxPrice = int.MaxValue, bool allOrNone = false)
    {
        bought = 0;
        left = 0;
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        string? near = NearVendor(k, who);
        if (near != null)
            return near;
        byte[]? list = AskNear(k, who, "buy", 0x74);
        using (Act(k))
        {
            if (list == null || list.Length < 8)
            {
                string na = NoAnswer(who, "buy");
                if (graphic(0x0FA0) || graphic(0x0FA1))
                {
                    AtlasNpc? vn = VendorNamed(k, who);
                    if (vn != null)
                        Attempt(vn.Serial, "buy", "failed: " + na);
                }
                return na;
            }
            uint box = U32(list, 3);
            List<uint>? order;
            List<int>? stock;
            lock (m_contentsGate)
            {
                m_contents.TryGet(box, out order);
                m_contentAmounts.TryGet(box, out stock);
            }
            if (order == null)
                return "the vendor's stock was not sent";
            List<int> prices = new();
            int at = 8;
            List<int> listed = new();
            for (int i = 0; i < list[7] && at + 5 <= list.Length; i++)
            {
                listed.Add((int)U32(list, at));
                int len = list[at + 4];
                at += 5 + len;
            }
            // Into stock order (StockIndex): prices[i] is the price of order[i].
            for (int i = 0; i < listed.Count; i++)
                prices.Add(0);
            for (int i = 0; i < listed.Count; i++)
            {
                int si = StockIndex(i, listed.Count, order.Count);
                if (si >= 0)
                    prices[si] = listed[i];
            }
            // The vendor wearing the stock, as the atlas finds it; else the nearest shopkeeper in sight.
            uint vendor = 0;
            m_wornBy.TryGet(box, out vendor);
            if (vendor == 0)
            {
                AssistItem? boxItem = Item(k, box);
                if (boxItem != null)
                    vendor = boxItem.Mobile != 0 ? boxItem.Mobile : boxItem.Container;
            }
            if (vendor == 0 && Atlas != null)
            {
                int px, py, best = int.MaxValue;
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                }
                foreach (AtlasNpc n in Atlas.Visible())
                    if (n.Type == "Shopkeeper")
                    {
                        int d = Tiles(n.X, n.Y, px, py);
                        if (d < best)
                        {
                            best = d;
                            vendor = n.Serial;
                        }
                    }
            }
            if (vendor == 0)
                return "the vendor wearing the stock " + box.ToString("X") + " is not known";
            {
                Set<int> arts = Set<int>.Empty;
                for (int i = 0; i < order.Count && i < stock!.Count; i++)
                {
                    AssistItem? it = Item(k, order[i]);
                    if (it != null && stock[i] > 0)
                        arts = arts.Add(it.Graphic);
                }
                lock (m_stockSeenGate)
                    m_stockSeen = m_stockSeen.Set(vendor, new StockSeen { At = Clock.UtcNow, Arts = arts });
            }
            // The status's gold (0x11) counts the pack before its contents are known (after login, unopened).
            int gold = PackCount(k, g => g == GoldArt);
            lock (k.State.Gate)
                gold = Math.Max(gold, k.State.Gold);
            List<uint[]> picks = new();
            int cost = 0, shown = 0, overPrice = 0;
            for (int i = 0; i < order.Count && i < prices.Count; i++)
            {
                AssistItem? it = Item(k, order[i]);
                if (it != null && graphic(it.Graphic) && stock![i] > 0)
                    shown += stock[i];
            }
            m_lastBuyShown = shown;
            if (allOrNone && shown > 0)
            {
                int whole = 0;
                for (int i = 0; i < order.Count && i < prices.Count; i++)
                {
                    AssistItem? it = Item(k, order[i]);
                    if (it != null && graphic(it.Graphic) && stock![i] > 0)
                        whole += stock[i] * Math.Max(1, prices[i]);
                }
                m_lastBuyWhole = whole;
                if (shown > want || whole > gold)
                {
                    k.Settle();
                    return PurseShort
                        + (shown > want ? "room for " + want + " of the " + shown
                        : "the pack's " + gold + " gold of the " + whole + " for all " + shown) + "; none bought";
                }
            }
            for (int i = 0; i < order.Count && i < prices.Count && bought < want; i++)
            {
                AssistItem? it = Item(k, order[i]);
                if (it == null || !graphic(it.Graphic))
                    continue;
                int have = Math.Max(1, stock![i]), price = Math.Max(1, prices[i]);
                if (price > maxPrice)
                {
                    left += have;
                    overPrice = Math.Max(overPrice, price);
                    continue;
                }
                int take = Math.Min(have, Math.Min(want - bought, (gold - cost) / price));
                left += have - Math.Max(0, take);
                if (take <= 0)
                    continue;
                picks.Add(new uint[] { order[i], (uint)take });
                bought += take;
                cost += take * price;
            }
            // Only a thread buy goes in the circuit's record: the circuit takes every vendor there for a thread seller
            // and a shirt buyer, and offered shirts to the mages and the stable a reagent and a horse buy had recorded
            // (Damian, 2026-10-17: "the bot is trying to sell to mage").
            bool threadBuy = graphic(0x0FA0) || graphic(0x0FA1);
            if (picks.Count == 0)
            {
                k.Settle();
                if (threadBuy)
                    CircuitStock(vendor, who == "vendor" ? "" : who, shown, shown);
                string none = overPrice > 0 ? "the vendor asks " + overPrice + ", over " + maxPrice
                    : left > 0 ? "the pack's " + gold + " gold buys none of it" : "the vendor has none";
                if (threadBuy)
                    Attempt(vendor, "buy", "none: " + none);
                return none;
            }
            Heard mark = new(k.State);
            string? why = k.Buy(vendor, picks);
            if (why != null)
                return why;
            // The vendor's own total ("The total of thy purchase is 36 gold."): Jannelle's last 9 thread cost 4 each
            // against the listed 3 (2026-10-17 00:19:39), so the record takes what was charged.
            DateTime totalBy = Clock.UtcNow.AddMilliseconds(AnswerMs);
            int charged = 0;
            while (charged == 0 && Clock.UtcNow < totalBy && !k.Closed)
            {
                foreach (string j in mark.Answers())
                {
                    int t = j.IndexOf("total of thy purchase is ", StringComparison.OrdinalIgnoreCase);
                    if (t >= 0)
                    {
                        string[] w = j.Substring(t + 25).Split(' ');
                        int n;
                        if (w.Length > 0 && int.TryParse(w[0], out n))
                            charged = n;
                    }
                }
                if (charged == 0)
                    Clock.Sleep(50);
            }
            if (charged > 0 && cost > 0 && charged != cost)
            {
                for (int i = 0; i < prices.Count; i++)
                    prices[i] = prices[i] * charged / cost;
                cost = charged;
            }
            byte[] sent = new byte[8 + 7 * picks.Count];
            sent[0] = 0x3B;
            sent[3] = (byte)(vendor >> 24);
            sent[4] = (byte)(vendor >> 16);
            sent[5] = (byte)(vendor >> 8);
            sent[6] = (byte)vendor;
            sent[7] = 2;
            for (int i = 0; i < picks.Count; i++)
            {
                uint s = picks[i][0];
                sent[8 + i * 7] = 0x1A;
                sent[9 + i * 7] = (byte)(s >> 24);
                sent[10 + i * 7] = (byte)(s >> 16);
                sent[11 + i * 7] = (byte)(s >> 8);
                sent[12 + i * 7] = (byte)s;
                sent[13 + i * 7] = (byte)(picks[i][1] >> 8);
                sent[14 + i * 7] = (byte)picks[i][1];
            }
            lock (m_offersGate)
            {
                m_offers = m_offers.Clear();
                for (int i = 0; i < order.Count && i < prices.Count; i++)
                {
                    AssistItem? it = Item(k, order[i]);
                    m_offers = m_offers.Set(order[i], new object[] { it != null ? UoTiles.Name(it.Graphic) : "item",
                        prices[i], vendor });
                }
            }
            Traded(k, sent);
            Clock.Sleep(DropSettleMs);
            if (threadBuy)
                CircuitStock(vendor, who == "vendor" ? "" : who, shown, left);
            // The item named: "bought 3 ... 7 left" read as a thread buy when it was sewing kits (2026-10-20 00:33:59).
            AssistItem? first = Item(k, picks[0][0]);
            string bName = first != null ? UoTiles.Name(first.Graphic) : "items";
            Note("Circuit: bought " + bought + " " + bName + " for " + cost + " gold from "
                + (who == "vendor" ? vendor.ToString("X") : who) + "; " + left + " left in stock");
            if (threadBuy)
                Attempt(vendor, "buy", "bought " + bought + " for " + cost + ", " + left + " left");
            return null;
        }
    }
    // Sells every item of these arts the vendor offers at least floor for. Null, or why not; sold, the gold, and the
    // best price offered under the floor come back.
    // most: the items sold at most (the tour sells one to each tailor to read its price).
    public string? VendorSell(Func<int, bool> graphic, int floor, out int sold, out int gold, out int under,
        string who = "vendor", int most = int.MaxValue)
    {
        return VendorSell(graphic, g => floor, out sold, out gold, out under, who, most);
    }
    // floorOf: the lowest price for each art (the sell list's, FloorOf).
    // serialOk: only these pieces (an order's spares, OrderPick).
    public string? VendorSell(Func<int, bool> graphic, Func<int, int> floorOf, out int sold, out int gold,
        out int under, string who = "vendor", int most = int.MaxValue, Func<uint, bool>? serialOk = null)
    {
        sold = 0;
        gold = 0;
        under = 0;
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        string? near = NearVendor(k, who);
        if (near != null)
            return near;
        // A sale takes at most SellBatch items (Damian, 2026-10-17: "you can only sell 5 items at a time"; his own
        // sales went 5 at a time, trades 2026-10-16), so the vendor is asked again after each until it offers none at
        // the floor.
        uint vendor = 0;
        string? end = null;
        // Asked again only while the pack holds some of it (Damian, 2026-10-19: "watch me sell tries to sell one too
        // many times"; Stanley was asked once more after the last 5 shirts, 02:30:41).
        while (!circuitStop && !k.Closed && sold < most && (sold == 0 || PackCount(k, graphic) > 0))
        {
            using (Act(k))
            {
                byte[]? list = AskNear(k, who, "sell", 0x9E);
                // A later batch's ask that got no proper answer is reported, the shirts already sold with it.
                if (list == null || list.Length < 9)
                {
                    string na = NoAnswer(who, "sell");
                    end = sold > 0 && !Silent(na) ? null : na;
                    // A vendor that will buy no more ("I can't afford any more of that") offers nothing for now.
                    if (KindOf(LastReply!) == Reply.NoInterest)
                    {
                        AtlasNpc? vn = VendorNamed(k, who);
                        if (vn != null)
                            RecordOffer(vn.Serial, 0);
                    }
                    break;
                }
                vendor = U32(list, 3);
                int n = (list[7] << 8) | list[8], at = 9, batchGold = 0, batchItems = 0, bestHere = 0;
                List<uint[]> picks = new();
                // One floor a graphic a window: worked out for each of a window's 95 shirts, the floor (sell list, top
                // price, pack count) held the answer 5.2 s of each 7 s sale (Kimmie, 2026-10-20 13:59:34).
                ValueMap<int, int> floors = ValueMap<int, int>.Empty;
                for (int i = 0; i < n && at + 14 <= list.Length; i++)
                {
                    uint s = U32(list, at);
                    int g = (list[at + 4] << 8) | list[at + 5], amount = (list[at + 8] << 8)
                        | list[at + 9], price = (list[at + 10] << 8) | list[at + 11], len = (list[at + 12] << 8)
                        | list[at + 13];
                    at += 14 + len;
                    if (!graphic(g))
                        continue;
                    bestHere = Math.Max(bestHere, price);
                    if (serialOk != null && !serialOk(s))
                        continue;
                    int floor;
                    if (!floors.TryGet(g, out floor))
                        floors = floors.Set(g, floor = floorOf(g));
                    if (price < floor)
                    {
                        under = Math.Max(under, price);
                        continue;
                    }
                    // The 5 counts a stack's amount, not its stacks (uo1998 2026-10-17: a stack of 129 Spider's Silk
                    // answered "You can only sell 5 items at a time!"): a stack goes 5 at a time.
                    int room = Math.Min(SellBatch, most - sold) - batchItems;
                    if (room <= 0)
                        continue;
                    int a = Math.Min(Math.Max(1, amount), room);
                    picks.Add(new uint[] { s, (uint)a, (uint)Math.Max(1, amount) });
                    batchGold += a * price;
                    batchItems += a;
                }
                RecordOffer(vendor, bestHere);
                if (picks.Count == 0)
                {
                    if (sold == 0)
                        end = under > 0 ? "the vendor pays " + under + ", under the lowest price"
                            : "the vendor wants none of them";
                    break;
                }
                string? why = k.Sell(vendor, picks);
                if (why != null)
                {
                    end = why;
                    break;
                }
                byte[] sent = new byte[9 + 6 * picks.Count];
                sent[0] = 0x9F;
                sent[3] = (byte)(vendor >> 24);
                sent[4] = (byte)(vendor >> 16);
                sent[5] = (byte)(vendor >> 8);
                sent[6] = (byte)vendor;
                for (int i = 0; i < picks.Count; i++)
                {
                    uint s = picks[i][0];
                    sent[9 + i * 6] = (byte)(s >> 24);
                    sent[10 + i * 6] = (byte)(s >> 16);
                    sent[11 + i * 6] = (byte)(s >> 8);
                    sent[12 + i * 6] = (byte)s;
                    sent[13 + i * 6] = (byte)(picks[i][1] >> 8);
                    sent[14 + i * 6] = (byte)picks[i][1];
                }
                Traded(k, sent);
                Clock.Sleep(DropSettleMs);
                // Counted only when the server took them: an item still in the pack was not sold.
                // A stack sold in part stays in the pack with less: the drop in its amount is what sold.
                int took = 0, offeredCount = 0;
                foreach (uint[] pk in picks)
                {
                    offeredCount += (int)pk[1];
                    AssistItem? left = Item(k, pk[0]);
                    took += left == null ? (int)pk[1] : Math.Max(0, (int)pk[2] - Math.Max(1, left.Amount));
                }
                sold += took;
                gold += batchGold * took / Math.Max(1, offeredCount);
                if (took == 0)
                {
                    RecordOffer(vendor, 0);
                    end = sold > 0 ? null : "the vendor took none of the " + picks.Count + " offered";
                    break;
                }
                // The next batch's ask waits SellGapMs: asked 0.7 s after a sale, Nana answered "I am a tailor. I could
                // purchase those things that I supply." with no window, and bought at a person's pace (Magincia,
                // 2026-10-17).
                if (sold < most)
                    Clock.Sleep(SellGapMs);
            }
        }
        // Watch me sell reports its own sales ("Watch: sold ..."), and a circuit's are the circuit's.
        if (sold > 0 && !(m_watchSelling && !CircuitRunning))
            Note("Circuit: sold " + sold + " for " + gold + " gold to " + who
                + (under > 0 ? "; it offered " + under + " for others" : ""));
        {
            uint av = vendor;
            if (av == 0)
            {
                AtlasNpc? vn = VendorNamed(k, who);
                if (vn != null)
                    av = vn.Serial;
            }
            Attempt(av, "sell",
                sold > 0 ? "sold " + sold + " for " + gold + (end != null ? "; " + end : "")
                : "failed: " + (end ?? "none taken"));
        }
        return end;
    }
    public const int SellBatch = 5;
    int SellGapMs => Ms("sell-gap");
    // Each sell ask records the tailor's best offer for the sell list's items (0 once it takes no more or cannot afford
    // more). The survey is done when a sell trip finds no city left with a tailor not passed over (NoSellCity); for
    // SurveyFreshMinutes after it a sell trip goes first to the city of the best fresh offer.
    public const int SurveyFreshMinutes = 30, TopDays = 7;
    Map<uint, Tuple<int, DateTime>> m_sellOffers = Map<uint, Tuple<int, DateTime>>.Empty;
    readonly Lock m_sellOffersGate = new();
    DateTime m_surveyedAt = DateTime.MinValue;
    // Kept in offers-<server>.txt (serial, price, ticks), so a relay restart or a new circuit does not ask again a
    // tailor whose offer is still fresh (Magincia's Nana re-asked 9 minutes after her 23, 2026-10-19 16:22).
    bool m_sellOffersLoaded;
    void LoadOffers()
    {
        lock (m_sellOffersGate)
            LoadOnce(ref m_sellOffersLoaded, ServerFile("offers"),
                line => { string[] t = line.Split('\t'); if (t.Length == 3) m_sellOffers = m_sellOffers.Set(
                Convert.ToUInt32(t[0], 16), Tuple.Create(int.Parse(t[1]), new DateTime(long.Parse(t[2])))); });
    }
    void RecordOffer(uint vendor, int price)
    {
        if (vendor == 0)
            return;
        LoadOffers();
        DateTime now = Clock.UtcNow.ToLocalTime();
        lock (m_sellOffersGate)
        {
            m_sellOffers = m_sellOffers.Set(vendor, Tuple.Create(price, now));
            try
            {
                File.AppendAllText(ServerFile("offers"),
                    vendor.ToString("X") + "\t" + price + "\t" + now.Ticks + Environment.NewLine);
            }
            catch { }
        }
    }
    bool Surveyed => (Clock.UtcNow.ToLocalTime() - m_surveyedAt).TotalMinutes < SurveyFreshMinutes;
    // A tailor's offer within SurveyFreshMinutes, or -1 for none.
    // Homespun's offers are fresh until the tailor's next restock after the offer (RestockAfter): a tailor that has
    // bought her fill buys again at her restock, and the survey's half hour left the homespun world's tailor unasked
    // for thirteen minutes more.
    int FreshOffer(uint vendor)
    {
        LoadOffers();
        Tuple<int, DateTime>? o;
        if (!m_sellOffers.TryGet(vendor, out o))
            return -1;
        DateTime now = Clock.UtcNow.ToLocalTime();
        bool fresh = Homespun ? now < RestockAfter(vendor, o.Item2) : (now - o.Item2).TotalMinutes < SurveyFreshMinutes;
        return fresh ? o.Item1 : -1;
    }
    int BestFreshOffer()
    {
        LoadOffers();
        int best = 0;
        DateTime now = Clock.UtcNow.ToLocalTime();
        foreach (Tuple<int, DateTime> o in m_sellOffers.Values)
            if ((now - o.Item2).TotalMinutes < SurveyFreshMinutes)
                best = Math.Max(best, o.Item1);
        return best;
    }
    // The top first, then the sell list's floor (Damian, 2026-10-20: "once brian @27 was sold down to refusals, that's
    // when i decided 23 is good price"; "my original stipulations and designs were not quite aligned with complicated
    // economy here"). A tailor holds the top when our last sale to it fetched the item's top price: it opened there
    // again the next visit 10 times in 14, while one left under the top opened at 23 or 24 81 times in 93 and climbed
    // only within the visit, and our sales to other tailors did not move it (Britain's three kept 27 through each
    // other's sales; trades to 2026-10-20). While a tailor holding the top that the bank's runes reach has not refused
    // (a fresh offer of 0), every other tailor's floor is the top; once each has refused, every floor is the sell
    // list's. One floor for every tailor, the week's best, passed over the tailors at 23: 102 thread bought, 88 shirts
    // sewn, none sold (2026-10-20 03:16). The top is the highest price paid in TopSales sales of the last TopDays
    // (Slade's single 46 is not).
    public const int TopSales = 20;
    ValueMap<string, int> m_priceMemo = ValueMap<string, int>.Empty; DateTime m_priceMemoAt = DateTime.MinValue;
    readonly Lock m_priceMemoGate = new();
    int Memo(string key, Func<int> f)
    {
        lock (m_priceMemoGate)
        {
            if ((Clock.UtcNow.ToLocalTime() - m_priceMemoAt).TotalSeconds > 60)
            {
                m_priceMemo = m_priceMemo.Clear();
                m_priceMemoAt = Clock.UtcNow.ToLocalTime();
            }
            int v;
            if (!m_priceMemo.TryGet(key, out v))
            {
                v = f();
                m_priceMemo = m_priceMemo.Set(key, v);
            }
            return v;
        }
    }
    int TopPrice(string item)
    {
        return Memo("top|" + item, () =>
        {
            if (Atlas == null)
                return 0;
            ValueMap<int, int> count = ValueMap<int, int>.Empty;
            foreach (AtlasTrade t in Atlas.Trades(0))
                if (t.Kind == "sell" && (Clock.UtcNow.ToLocalTime() - t.At).TotalDays < TopDays
                    && string.Equals(t.Item, item, StringComparison.OrdinalIgnoreCase))
                    count = count.Set(t.Price, count.Get(t.Price, 0) + 1);
            int top = 0;
            foreach (KeyValuePair<int, int> kv in count)
                if (kv.Value >= TopSales)
                    top = Math.Max(top, kv.Key);
            return top;
        });
    }
    // The price of our latest sale of the item to this tailor, -1 for none or one older than TopHoldMinutes. A tailor
    // left at the top opened there again 9 times in 9 within 1.3 hours, 1 in 5 at 1.4 to 2 hours, and 3 in 15 past that
    // (Lynn, left at 27, opened at 23 seven hours later, 2026-10-20 03:28).
    public const int TopHoldMinutes = 80;
    int LastSalePrice(uint serial, string item)
    {
        return Memo(serial.ToString("X") + "|" + item, () =>
        {
            if (Atlas == null)
                return -1;
            AtlasTrade? last = null;
            foreach (AtlasTrade t in Atlas.Trades(serial))
                if (t.Kind == "sell" && string.Equals(t.Item, item, StringComparison.OrdinalIgnoreCase)
                    && (last == null || t.At > last.At))
                    last = t;
            return last == null || (Clock.UtcNow.ToLocalTime() - last.At).TotalMinutes > TopHoldMinutes ? -1
                : last.Price;
        });
    }
    // A tailor whose every sale of the last TopDays fetched the top, TopSales or more, holds it whatever the time since
    // (Brian in Moonglow: 1182 of 1182 at 27 to 2026-10-20).
    bool AlwaysTop(uint serial, SellItem item, int top)
    {
        return Memo("always|" + serial.ToString("X") + "|" + item.Name, () =>
        {
            if (Atlas == null)
                return 0;
            int n = 0;
            foreach (AtlasTrade t in Atlas.Trades(serial))
                if (t.Kind == "sell" && (Clock.UtcNow.ToLocalTime() - t.At).TotalDays < TopDays
                    && string.Equals(t.Item, item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (t.Price < top)
                        return 0;
                    n++;
                }
            return n >= TopSales ? 1 : 0;
        }) == 1;
    }
    bool HoldsTop(uint serial, SellItem item)
    {
        int top = TopPrice(item.Name);
        return top > item.Floor && (LastSalePrice(serial, item.Name) >= top || AlwaysTop(serial, item, top));
    }
    // A tailor holding the top that the bank's runes reach and that is not resting. A refused tailor's purse comes back
    // slowly (Damian, 2026-10-20: "he wont take anymore. one or two sales is all": Brian took exactly 10 shirts 4 times
    // in 6 within 86 minutes of a refusal), so it rests until every other tailor the runes reach has been asked since,
    // never on a timer (Damian, 2026-10-20: "no 2 hour pauses! there always must be a reset condition for all pauses.
    // like after you've gone through all the other vendors").
    bool Resting(uint serial)
    {
        LoadOffers();
        Tuple<int, DateTime>? o;
        if (!m_sellOffers.TryGet(serial, out o) || o.Item1 != 0)
            return false;
        foreach (string c in TailorCities())
        {
            if (Banned(c) || !BankHasRune(c))
                continue;
            foreach (AtlasNpc n in CityBuyers(c))
            {
                if (n.Serial == serial)
                    continue;
                Tuple<int, DateTime>? a;
                bool asked = m_sellOffers.TryGet(n.Serial, out a) && a.Item2 >= o.Item2;
                if (!asked)
                    return true;
            }
        }
        return false;
    }
    bool TopOpen(SellItem item)
    {
        foreach (string c in TailorCities())
        {
            if (Banned(c) || !BankHasRune(c))
                continue;
            foreach (AtlasNpc n in CityBuyers(c))
                if (HoldsTop(n.Serial, item) && !Resting(n.Serial))
                    return true;
        }
        return false;
    }
    // The shopkeepers list's forecasts (Damian, 2026-10-20: "a column of expected restock times for thread, and
    // expected refresh of expected purchase price and depth"): thread, the next stock and when (ThreadDueAt,
    // ThreadExpected the amount); price, the shirt offer expected and until when (the top
    // TopHoldMinutes after the last sale at it, a refusal until the other tailors are asked, Resting); depth, the
    // median and the best shirts a visit sold in the last TopDays.
    // Empty where the vendor neither sells thread nor buys the sell list's shirts.
    public string[] VendorForecast(AtlasNpc n)
    {
        string thread = "", price = "", depth = "";
        DateTime now = Clock.UtcNow.ToLocalTime();
        CircuitVendor? rec = CircuitVendors().Find(c => c.Serial == n.Serial);
        bool sellsThread = rec != null
            || n.Items.Exists(i => i.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0);
        if (sellsThread)
        {
            DateTime at = ThreadDueAt(n.Serial);
            thread = "~" + ThreadExpected(n) + (at > now ? " at " + at.ToString("HH:mm") : " now");
        }
        SellItem? shirt = SellList().Find(i => Array.Exists(i.Arts, IsShirtArt));
        if (shirt != null && TailorVendor(n) && DealsIn(n) && Atlas != null)
        {
            int top = TopPrice(shirt.Name);
            LoadOffers();
            Tuple<int, DateTime>? o;
            m_sellOffers.TryGet(n.Serial, out o);
            List<AtlasTrade> sales = Atlas.Trades(n.Serial).FindAll(t => t.Kind == "sell"
                && string.Equals(t.Item, shirt.Name, StringComparison.OrdinalIgnoreCase));
            sales.Sort((a, b) => a.At.CompareTo(b.At));
            if (Resting(n.Serial))
                price = "refused " + o!.Item2.ToString("HH:mm") + ", rests until the others are asked";
            else if (AlwaysTop(n.Serial, shirt, top))
                price = top + " always";
            else if (HoldsTop(n.Serial, shirt) && sales.Count > 0)
                price = top + " until " + sales[sales.Count - 1].At.AddMinutes(TopHoldMinutes).ToString("HH:mm");
            else
                price = shirt.Floor + " expected";
            List<int> visits = new();
            DateTime prev = DateTime.MinValue;
            foreach (AtlasTrade t in sales)
            {
                if ((now - t.At).TotalDays >= TopDays)
                    continue;
                if ((t.At - prev).TotalMinutes > VisitGapMinutes)
                    visits.Add(0);
                visits[visits.Count - 1] += Math.Max(1, t.Amount);
                prev = t.At;
            }
            // The median is pulled down by one-batch survey asks and visits that ran out of shirts (Brian: median 15,
            // his full visits 60 to 109), so the best visit stands beside it as the capacity.
            if (visits.Count > 0)
            {
                visits.Sort();
                depth = "~" + visits[visits.Count / 2] + ", best " + visits[visits.Count - 1] + " (" + visits.Count
                    + ")";
            }
        }
        return new[] { thread, price, depth };
    }
    public const int VisitGapMinutes = 10;
    // The tailors passed over for offering under the top are asked again once the top has closed (EndCycle).
    bool m_topWasOpen;
    void TopClosedCheck()
    {
        SellItem? shirt = SellList().Find(i => Array.Exists(i.Arts, IsShirtArt));
        if (shirt == null)
            return;
        bool open = TopOpen(shirt);
        if (m_topWasOpen && !open)
        {
            EndCycle();
            Note("Circuit: every tailor holding " + TopPrice(shirt.Name) + " has refused; the floor is " + FloorText());
        }
        m_topWasOpen = open;
    }
    // A recall's reagents (Damian, 2026-10-20: "a recall costs 15gp per cast"). Holding out for the top here costs the
    // trip to a tailor holding it and back, RecallGp each way, so the pack's items must gain more than that at the top.
    public const int RecallGp = 15;
    int m_tripHops;
    int VendorFloor(uint serial, int g)
    {
        SellItem? item = SellList().Find(i => Array.IndexOf(i.Arts, g) >= 0);
        if (item == null)
            return -1;
        // Homespun sells to the floor, no holding out for the top (Damian, 2026-10-21: "gotta keep those vendors bought
        // down"; five tailors at 23 and 24 refused in a row against a top of 27, 82 shirts carried).
        if (SellOff || Homespun || HoldsTop(serial, item) || !TopOpen(item))
            return item.Floor;
        int top = TopPrice(item.Name), held = 0;
        GameLink k = m_link;
        if (k != null && !k.Closed)
            held = PackCount(k, x => Array.IndexOf(item.Arts, x) >= 0);
        return held * (top - item.Floor) > 2 * RecallGp ? Math.Max(item.Floor, top) : item.Floor;
    }
    // The top for a city with a tailor holding it that has not refused, else the sell list's floor.
    int CityPrice(string city)
    {
        SellItem? shirt = SellList().Find(i => Array.Exists(i.Arts, IsShirtArt));
        if (shirt == null)
            return 0;
        foreach (AtlasNpc n in CityBuyers(city))
            if (PaysFloor(n) && HoldsTop(n.Serial, shirt) && !Resting(n.Serial))
                return Math.Max(shirt.Floor, TopPrice(shirt.Name));
        return shirt.Floor;
    }
    // Sell off (Damian, 2026-10-19: "we got a shit ton of thread too, and more coming so we need to be selling off and
    // not looking for a great price right now"): while on, a circuit takes any offer at the sell list's own floor, not
    // the trades' top price nor the survey's. Per server: circuit.selloff.
    public bool SellOff
    {
        get => Cfg.Get(Server + "circuit.selloff", "off") == "on";
        set => Cfg.Set(Server + "circuit.selloff", value ? "on" : "off");
    }
    // Watch me sell (Damian, 2026-10-19: "we need a watch me sell loop that lets you fiddle with the price allowables.
    // so it sees me sell at 27, and there is a +/- config there either amount or %. min increments 1gp"; "i could walk
    // up with a pack of, say wheat sheafs, and sell them all after you watch"): while on and no circuit runs, a sale
    // made by hand starts the loop: the rest of that item in the pack is sold to the same vendor, SellBatch at a time,
    // while it pays at least the hand sale's price less the allowance. A 1998 vendor pays less as its stock of the item
    // grows (OUO CShopkeeper_GetBuyPrice: a surplus marks the price down), so the loop ends at the first batch under
    // that. The allowance is gold, or a percent of the price rounded to whole gold and at least 1. Per server:
    // watchsell, watchsell.tol, watchsell.pct.
    public bool WatchSell
    {
        get => Cfg.Get(Server + "watchsell", "off") == "on"; set
        {
            Cfg.Set(Server + "watchsell", value ? "on" : "off");
            if (!value && m_watchSelling && !CircuitRunning)
                circuitStop = true;
        }
    }
    public int WatchTol
    {
        get => Math.Max(0, Cfg.GetInt(Server + "watchsell.tol", 2));
        set => Cfg.Set(Server + "watchsell.tol", Math.Max(0, value).ToString());
    }
    public bool WatchPct
    {
        get => Cfg.Get(Server + "watchsell.pct", "0") == "1";
        set => Cfg.Set(Server + "watchsell.pct", value ? "1" : "0");
    }
    public int WatchTolGp(int price)
    {
        int t = WatchTol;
        return t == 0 ? 0 : WatchPct ? Math.Max(1, (int)Math.Round(price * t / 100.0)) : t;
    }
    // The loop's state for the window: WatchSelling only while it sells (Damian, 2026-10-19: "it should stop
    // automatically, and neither manual stoppage, nor startage of the tailor should change that config").
    public volatile string WatchStatus = "";
    volatile bool m_watchSelling;
    public bool WatchSelling => m_watchSelling;
    // Stops the selling run under way; the setting stays as it is.
    public void StopWatchSelling()
    {
        if (m_watchSelling && !CircuitRunning)
        {
            circuitStop = true;
            Note("Watch: selling stopped");
        }
    }
    // The serial and art of each item sold by hand, until the server takes it (SaleDeleted).
    ValueMap<uint, int> m_handSales = ValueMap<uint, int>.Empty; readonly Lock m_handSalesGate = new();
    // The vendor of a hand sale as an ask names it: its first name (NearVendor matches first names), "vendor" for none.
    // The trade record names it whole, and "Stanley the tailor" was "not known here" (Damian, 2026-10-24: "watch me
    // sell isn't working", 17:28:33).
    // A vendor's name as the Circuit activity shows it, its title cut (Damian, 2026-10-24: "it shows stanely the
    // tailor, whereas all the other tailor npc vendors are just name, no profession").
    internal static string Untitled(string name)
    {
        int at = name.LastIndexOf(" the ", StringComparison.OrdinalIgnoreCase);
        return at > 0 ? name.Substring(0, at) : name;
    }
    internal static string WatchVendor(string vendor)
    {
        return vendor.Length > 0 ? vendor.Split(' ')[0] : "vendor";
    }
    // Every way out says why in the log: two sales right after a login ended silently (2026-10-20 12:09:42, 12:14:04).
    void WatchSaw(AtlasTrade t, int art)
    {
        if (!WatchSell)
            return;
        if (t.Price <= 0 || t.Item.StartsWith("item ") || art < 0)
        {
            Note("Watch: your sale of " + t.Item + " not followed: "
                + (art < 0 ? "its art is unknown" : "its price is unknown"));
            return;
        }
        if (CircuitRunning)
        {
            WatchStatus = "a circuit is running; not selling the rest of " + t.Item;
            Note("Watch: " + WatchStatus);
            return;
        }
        if (m_watchSelling)
            return;
        m_watchSelling = true;
        NewThread(() =>
        {
            int floor = Math.Max(1, t.Price - WatchTolGp(t.Price));
            string who = WatchVendor(t.Vendor);
            try
            {
                // The vendor's next ask waits as a circuit's does after a sale (sell-gap).
                Clock.Sleep(SellGapMs);
                GameLink k = m_link;
                if (k == null || k.Closed)
                {
                    WatchStatus = "no game connection";
                    Note("Watch: " + WatchStatus);
                    return;
                }
                // A pack not opened since the login counts as empty: it is read first, with no window on the client.
                if (PackCount(k, g => g == art) == 0 && PackStale())
                {
                    uint pack;
                    lock (k.State.Gate)
                        pack = k.State.Backpack;
                    if (pack != 0)
                    {
                        string? pw = Peek(pack);
                        if (pw != null)
                            Note("Watch: reading the pack: " + pw);
                    }
                }
                if (PackCount(k, g => g == art) == 0)
                {
                    WatchStatus = "you sold " + t.Item + " at " + t.Price + "; none left in the pack";
                    Note("Watch: " + WatchStatus);
                    return;
                }
                WatchStatus = "selling the rest of " + t.Item + " to " + who + " down to " + floor;
                Note("Watch: you sold " + t.Item + " at " + t.Price + "; " + WatchStatus);
                // The player's own sale is the vendor trading where it stands: that spot is its home, an away mark
                // there cleared (2026-10-20 11:44: Simon behind his counter, sold to by hand, refused as "away from its
                // shop").
                if (who != "vendor")
                    NoteVendorHome(k, who);
                int sold, gold, under;
                string? w = VendorSell(g => g == art, floor, out sold, out gold, out under, who);
                WatchStatus = "sold " + sold + " more " + t.Item + " to " + who + " for " + gold + " gold"
                    + (under > 0 ? "; it now pays " + under + ", under " + floor : "")
                    + (w != null && (sold == 0 || under == 0) ? ": " + w : "")
                    + (PackCount(k, g => g == art) > 0 ? "; " + PackCount(k, g => g == art) + " left" : "");
                Note("Watch: " + WatchStatus);
            }
            finally { m_watchSelling = false; }
        }).Start();
    }
    // The survey done: the floor falls to the best offer since, and the tailors passed over for offering under the top
    // price may be asked again.
    void SurveyDone()
    {
        m_surveyedAt = Clock.UtcNow.ToLocalTime();
        EndCycle();
        Note("Circuit: every tailor in reach asked; the best offer since, " + BestFreshOffer()
            + ", leads the next sell trip");
    }
    // The runes on the ground within reach, each single-clicked (0x09) for its label (0x1C from the rune: "a recall
    // rune for <place>"), nearest first: a rune library (Damian, 2026-10-17: "there is a rune library in the moat next
    // to brit bank. the names so far have been reliable"). Serial, label and tile each; a rune that answered nothing in
    // LabelWaitMs reads "no label".
    int LabelGapMs => Ms("label-gap");
    public List<string> RuneLabels(int reach)
    {
        GameLink k = m_link;
        List<string> list = new();
        if (k == null || k.Closed)
            return list;
        List<AssistItem> runes = new();
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            foreach (AssistItem g in k.State.Ground.Values)
                if (Tiles(g.X, g.Y, px, py) <= reach && IsRuneArt(g.Graphic))
                    runes.Add(g);
        }
        runes.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
        return LabelsOf(k, runes);
    }
    // The pack's runes and their labels, the same way: the home rune and the spare one (Damian, 2026-10-17: "i put a
    // second rune in the backpack bag to skara brea in case the brit one is blocked by a character standing in the
    // way").
    public List<string> PackRuneLabels(GameLink k)
    {
        OpenPackBags(k);
        List<AssistItem> runes = new();
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && it.Graphic >= 0x1F14 && it.Graphic <= 0x1F17)
                    runes.Add(it);
        }
        return LabelsOf(k, runes);
    }
    // Every item in one container labelled where it lies, a rune in the bank box read without moving it (Damian,
    // 2026-10-17: "learn how to get their names while they are still in the bank box").
    public List<string> ContainerLabels(uint box)
    {
        GameLink k = m_link;
        List<AssistItem> items = new();
        if (k == null || k.Closed)
            return new List<string>();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == box)
                    items.Add(it);
        return LabelsOf(k, items);
    }
    // Every mobile in view within reach single-clicked for its name (Damian, 2026-10-17: the vendors of Serpent's Hold
    // showed "?"); the label answers name them in AssistState.Names as well.
    public List<string> MobileLabels(int reach)
    {
        GameLink k = m_link;
        List<AssistItem> list = new();
        if (k == null || k.Closed)
            return new List<string>();
        lock (k.State.Gate)
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                if (Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= reach)
                    list.Add(new AssistItem { Serial = kv.Key, X = kv.Value[0], Y = kv.Value[1] });
        return LabelsOf(k, list);
    }
    // Only the mobiles in reach with no name known yet, so a circuit pass clicks nobody twice.
    public void NameUnnamed(int reach)
    {
        GameLink k = m_link;
        List<AssistItem> list = new();
        if (k == null || k.Closed)
            return;
        lock (k.State.Gate)
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                if (!k.State.Names.ContainsKey(kv.Key)
                    && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= reach)
                    list.Add(new AssistItem { Serial = kv.Key, X = kv.Value[0], Y = kv.Value[1] });
        if (list.Count > 0)
            LabelsOf(k, list);
    }
    List<string> LabelsOf(GameLink k, List<AssistItem> runes)
    {
        List<string> list = new();
        Map<uint, string> labels = Map<uint, string>.Empty;
        Lock labelsGate = new();
        Set<uint> wanted = Set<uint>.Empty;
        foreach (AssistItem r in runes)
            wanted = wanted.Add(r.Serial);
        Action<byte[]> h = p => { if (p[0] == 0x1C
            && p.Length
            > 44) { uint s = U32(p,
            3); if (wanted.Contains(s)) lock (labelsGate) labels = labels.Set(s, Encoding.ASCII.GetString(p, 44,
            p.Length - 44).TrimEnd('\0')); } };
        k.ServerPacket += h;
        DateTime hideUntil = Clock.UtcNow.AddMilliseconds(LabelWaitMs + LabelGapMs * (runes.Count + 1));
        lock (m_lookingGate)
            foreach (AssistItem r in runes)
                m_looking = m_looking.Set(r.Serial, hideUntil);
        try
        {
            foreach (AssistItem r in runes)
            {
                using (Act(k))
                    k.Look(r.Serial);
                Clock.Sleep(LabelGapMs);
            }
            Wait.Until(Clock, () => labels.Count == runes.Count, LabelWaitMs, "the runes' labels", stepMs: 50);
        }
        finally { k.ServerPacket -= h; }
        foreach (AssistItem r in runes)
        {
            string? t;
            labels.TryGet(r.Serial, out t);
            list.Add(r.Serial.ToString("X") + " " + (t ?? "no label") + " at " + r.X + "," + r.Y + "," + r.Z);
        }
        return list;
    }
    // Each rune's landing, learned at every recall that moved the player (rune-landings-<server>.txt: serial, x, y).
    Map<uint, int[]> m_runeLandings = Map<uint, int[]>.Empty; bool m_runeLandingsLoaded;
    readonly Lock m_runeLandingsGate = new();
    string RuneLandingsPath => ServerFile("rune-landings");
    void LoadRuneLandings()
    {
        lock (m_runeLandingsGate)
            LoadOnce(ref m_runeLandingsLoaded, RuneLandingsPath,
                line => { string[] t = line.Split('\t'); if (t.Length == 3) m_runeLandings = m_runeLandings.Set(
                Convert.ToUInt32(t[0], 16), new[] { int.Parse(t[1]), int.Parse(t[2]) }); });
    }
    void RuneLanded(uint rune, int x, int y)
    {
        LoadRuneLandings();
        lock (m_runeLandingsGate)
        {
            m_runeLandings = m_runeLandings.Set(rune, new[] { x, y });
            List<string> l = new();
            foreach (KeyValuePair<uint, int[]> kv in m_runeLandings)
                l.Add(kv.Key.ToString("X") + "\t" + kv.Value[0] + "\t" + kv.Value[1]);
            try
            {
                File.WriteAllLines(RuneLandingsPath, l);
            }
            catch { }
        }
    }
    // A recall replaces a walk only when the walk runs longer than the recall-walk timing (Damian, 2026-10-19: "if you
    // have a run and the distance betweeen the shops is more than 15 seconds of running away ... in vesper there is the
    // 2 reagent shops near the bank. its silly to recall between them. but between the two big ones in brit, yeah it
    // makes sense"): the straight line from here at the walk's step pace, and only to a rune landing nearer the target
    // than here. RecallLand: the chosen rune's landing, known or placed by its label.
    int[]? m_recallLand = null;
    // A recall's cast to landing: about 2 s on uo1998 (recalls of 2026-10-17 to 19), with margin.
    public const int RecallCastCostMs = 2500;
    Set<uint> m_relabeled = Set<uint>.Empty;
    static bool IsBlankLabel(string label)
    {
        string l = (label ?? "").ToLowerInvariant();
        return l.Contains("unmarked") || l.Contains("blank");
    }
    uint RecallNearer(GameLink k, int px, int py, int tx, int ty)
    {
        m_recallLand = null;
        // Keep thread runs between the shops of a walking town, to save mana (RunKeepThread).
        if (m_keepMode && CircuitRunning)
        {
            string? here = UoRegions.City(px, py);
            if (here != null && here == UoRegions.City(tx, ty) && s_keepWalkCities.IndexOf(here) >= 0)
                return 0;
        }
        // A gathering circuit's walks, Lumberjacking's and the feathers' (Damian, 2026-10-23: "no recall was necessary
        // to start chopping and no recall necessary to run 30 tiles"; a Resume recalled 108 tiles to the sentinel at
        // 22:33:41 and put the axe in the pack).
        if ((m_lumberMode || m_featherMode || m_reagentMode) && CircuitRunning)
            return 0;
        // In the house's town, from home or inside it, the walk (Damian, 2026-10-25: "save mana and let it regenerate
        // while you run"; 2026-10-26: "the walk to the bank and tailor shop in moonglow is always worth it either to let
        // mana regen or to prevent spending it"). The Moonglow house stands in "telescope in Moonglow", so a test for
        // a house in no town recalled 244 tiles to Brian (11:22:18).
        // Home itself the same, from the house's town ("run home from brian").
        if (CircuitRunning && HomeTown(k) is string ht && (UoRegions.City(tx, ty) == ht || NearPile(tx, ty))
            && (AtHome(k) || UoRegions.City(px, py) == ht))
            return 0;
        if (CircuitRunning)
        {
            string? town = UoRegions.City(px, py);
            int mana;
            lock (k.State.Gate)
                mana = k.State.Mana;
            if (WalkInTown(town!,
                town != null && town == UoRegions.City(tx, ty) && TailorShopsIn(town).Exists(s => s.Holds(tx, ty, 2)),
                mana, RecallMana))
                return 0;
        }
        uint best = 0;
        int bestLeft = int.MaxValue;
        // A rune with no landing yet is placed by its label (GuessLanding).
        foreach (HeldRune r in PackRunes(k))
        {
            if (RuneBlocked(r.Serial))
                continue;
            int[]? land = r.Landing ?? GuessLanding(r.Place!);
            if (land == null)
                continue;
            int left = Tiles(land[0], land[1], tx, ty);
            if (left < bestLeft)
            {
                bestLeft = left;
                best = r.Serial;
                m_recallLand = land;
            }
        }
        int far = Tiles(px, py, tx, ty);
        if (best == 0 || bestLeft >= far)
            return 0;
        // The walk saved must also run over the limit: a rune landing beside the player (Jhelom's first tailor rune, 8
        // tiles nearer Kimmie than Kwik stood, 2026-10-19 21:47:43) was cast, did not move him, and he walked on.
        int pace = StepPace(k), walkMs = far * pace, savedMs = (far - bestLeft) * pace, over = Ms("recall-walk");
        m_lastRecallReason = "walk " + far + " tiles, about " + (walkMs / 1000.0).ToString("0.0") + " s at " + pace
            + " ms a step, the rune saving " + (savedMs / 1000.0).ToString("0.0") + " s, over "
            + (over / 1000.0).ToString("0.0") + " s";
        // The saving must also pay for the cast itself (RecallCastCostMs; D6 of the travel review).
        return walkMs > over && savedMs > over + RecallCastCostMs ? best : 0;
    }
    string m_lastRecallReason = "";
    // To a tailor shop of the town the player stands in: in Jhelom and Britain always the walk (Damian, 2026-10-24:
    // "jhelom and brit you should never recall between the shops, they are close enough"); elsewhere (Vesper) a recall
    // only with the mana for two, the one there and the one out to the next town, short of it the run, which rebuilds
    // the mana ("the walk shouldn't happen in vesper if you have mana to make 2 recalls ... if you have not the mana,
    // then you make the run").
    static readonly ImmutableArray<string> s_walkShopTowns = [ "Jhelom", "Britain" ];
    internal static bool WalkInTown(string town, bool townShop, int mana, int recallMana)
    {
        return townShop && (s_walkShopTowns.IndexOf(town) >= 0 || mana < 2 * recallMana);
    }
    // A rune never recalled by (no landing yet) lands, until its first recall, at the centre of the shop its label
    // names (Damian, 2026-10-19: "yes if the rune exists"; the shop runes "Britain The Right Fit", "Jhelom Adventurers
    // Needle", "Vesper Spinning Wheel (North)"): a shop of the town the label names whose words, less "the" and its "
    // in <town>", all stand in the label, apostrophes and brackets aside. Of shops sharing a name (Jhelom's two
    // Adventurer's Needles) the one farthest from every rune's known landing, as a second rune is marked for the other.
    // Null when none matches.
    int[]? GuessLanding(string label)
    {
        if (string.IsNullOrEmpty(label))
            return null;
        Func<string, Set<string>> words = t =>
            Set<string>.Of(t.ToLowerInvariant().Replace("'", "").Split(new[] { ' ', '(', ')', ',', ':', '-' },
            StringSplitOptions.RemoveEmptyEntries));
        Set<string> have = words(label);
        string? city = null;
        foreach (string c in UoRegions.Cities())
            if (have.IsSupersetOf(words(c)) && (city == null || c.Length > city.Length))
                city = c;
        if (city == null)
            return null;
        List<int[]> known = new(m_runeLandings.Values);
        // The town's own words match nothing (a region named only "Britain" matched every Britain rune), and the shop
        // that matches the most of the label's words wins.
        int[]? best = null;
        int bestFar = -1, bestWords = 0;
        Set<string> town = words(city);
        foreach (UoRegions.Shop shop in UoRegions.AllShopsIn(city))
        {
            string text = shop.Text;
            if (text.EndsWith(" in " + city))
                text = text.Substring(0, text.Length - 4 - city.Length);
            Set<string> need = words(text).Remove("the").Except(town);
            if (need.Count == 0 || !have.IsSupersetOf(need))
                continue;
            int far = int.MaxValue;
            foreach (int[] l in known)
                far = Math.Min(far, Tiles(l[0], l[1], shop.CX, shop.CY));
            if (need.Count > bestWords || (need.Count == bestWords && far > bestFar))
            {
                bestWords = need.Count;
                bestFar = far;
                best = new[] { shop.CX, shop.CY };
            }
        }
        return best;
    }
    // Recall (spell 32, circle 4) at a rune in the pack or in reach: the reagents and mana first, retried on a fizzle
    // ("it sometimes fizzles but it works. be persistent"), done when the player has moved.
    public const int RecallSpell = 32, RecallMana = 11, RecallTries = 5, CastCursorMs = 6000, RecallMoveMs = 5000,
        RecallMoved = 10, RecallSettleMs = 400, RecallSettleCapMs = 2000;
    // The assist's claimed cursor awaited until until, the connection's close, (from castMark) a line ending the cast,
    // or ended; whether it came.
    bool AwaitCursor(GameLink k, DateTime until, Heard? castMark = null, Func<bool>? ended = null)
    {
        while (!k.MacroCursor && Clock.UtcNow < until && !k.Closed && (castMark == null || !CastEnded(castMark))
            && (ended == null || !ended()))
            Clock.Sleep(20);
        return k.MacroCursor;
    }
    // A line of the server's own (never a label or anyone's speech) that ends the cast. Once the player's words came
    // the server took the cast, and a "You must wait" answers something else: the player's own click, 2026-10-22
    // 06:41:45.566, 116 ms after the words, ended the wait and the cursor at 06:41:46.942 went unanswered.
    bool CastEnded(Heard mark)
    {
        bool began = mark.PlayerSpoke();
        return mark.Server().Exists(j =>
            s_castEnds.Exists(e => (!began || e != "You must wait")
            && j.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0));
    }
    static readonly ImmutableArray<string> s_castEnds = [ "fizzle", "already casting", "insufficient", "not enough",
        "reagents",
        "You must wait", "cannot cast", "hands must be free" ];
    public string? Recall(uint rune)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        // A circuit stopped while on its way to the recall does not cast it (Damian, 2026-10-19: "i hit the stop
        // button, but something caused the character to recall"; the trip's bank walk and rune search ran on, 32 s
        // after the stop).
        if (circuitStop && CircuitRunning)
            return "stopped";
        string? kw = HomeKeyBeforeTravel(k);
        if (kw != null)
            return "the house key before the recall: " + kw;
        string? fh = FreeHands(k);
        if (fh != null)
            return "the hands before the recall: " + fh;
        for (int attempt = 1; attempt <= RecallTries; attempt++)
        {
            // A stop ends the tries (the circuit cast three more after "circuit stop", 2026-10-17).
            if (circuitStop && m_circuitThread != null && m_circuitThread.IsAlive)
                return "stopped";
            // The rune before the cast: a cast at a rune out of sight spent mana and left its cursor open (2026-10-17).
            bool seen;
            lock (k.State.Gate)
                seen = k.State.Items.ContainsKey(rune) || k.State.Ground.ContainsKey(rune);
            if (!seen)
                return "the rune " + rune.ToString("X") + " is not in sight";
            // The pack's bags read first: after a login a bag's reagents are unknown until it is opened.
            OpenPackBags(k);
            foreach (int r in s_recallReagents)
                if (PackCount(k, g => g == r) == 0)
                    return "no " + UoTiles.Name(r) + " in the pack";
            DateTime manaBy = Clock.UtcNow.AddMilliseconds(ManaWaitMs);
            // A stop ends the mana wait as well (2026-10-21 14:08:47, two stops at Minoc's bank went unanswered during
            // it).
            while (true)
            {
                int mana;
                lock (k.State.Gate)
                    mana = k.State.Mana;
                if (mana >= RecallMana)
                    break;
                if (circuitStop && CircuitRunning)
                    return "stopped";
                if (Clock.UtcNow > manaBy || k.Closed)
                    return "mana stayed under " + RecallMana;
                Clock.Sleep(500);
            }
            int x0, y0;
            lock (k.State.Gate)
            {
                x0 = k.State.X;
                y0 = k.State.Y;
            }
            Heard mark;
            using (Act(k))
            {
                mark = new(k.State);
                string? why = k.Cast(RecallSpell, true);
                if (why != null)
                    return why;
                // The player's own spell words ("Kal Ort Por") come before the cursor; only a line that ends the cast
                // ends the wait. Anyone else's speech does not: Jannelle's "Excuse me, but what art thou saying?" came
                // 121 ms before the cursor (2026-10-17 08:54:25), the cursor went unanswered and every retry was "You
                // are already casting a spell."
                DateTime until = Clock.UtcNow.AddMilliseconds(CastCursorMs);
                AwaitCursor(k, until, mark);
                if (k.MacroCursor)
                {
                    AssistItem? it;
                    if (!k.State.Items.TryGet(rune, out it))
                        k.State.Ground.TryGet(rune, out it);
                    if (it == null)
                    {
                        k.Settle();
                        return "the rune " + rune.ToString("X") + " is not in sight";
                    }
                    why = k.Target(false, rune, it.X, it.Y, it.Z, it.Graphic);
                    if (why != null)
                        return "aiming at the rune: " + why;
                }
            }
            DateTime moveBy = Clock.UtcNow.AddMilliseconds(RecallMoveMs);
            while (Clock.UtcNow < moveBy && !k.Closed)
            {
                int x, y;
                lock (k.State.Gate)
                {
                    x = k.State.X;
                    y = k.State.Y;
                }
                // A recall moves the player far; a step of a tile is not one ("the rune for Jhelom landed in Britain"
                // at 1414,1699 from 1414,1700).
                if (Tiles(x, y, x0, y0) > RecallMoved)
                {
                    // The landing settled before the first step: uo1998 draws the player (0x20) twice after a recall
                    // (2026-10-19 21:47:16.214), and a 0x20 after a step restarts the step numbers under it.
                    Wait.Until(Clock,
                        () => { DateTime d; lock (k.State.Gate) { d = k.State.DrawAt; x = k.State.X; y = k.State.Y; } return (Clock.UtcNow
                        - d).TotalMilliseconds
                        >= RecallSettleMs; },
                        RecallSettleCapMs, "the landing's settle", closed: () => k.Closed, stepMs: 50);
                    Note("Circuit: recalled to " + x + "," + y + (attempt > 1 ? " (try " + attempt + ")" : ""));
                    RuneLanded(rune, x, y);
                    return null;
                }
                if (CastEnded(mark))
                    break;
                Clock.Sleep(100);
            }
            // The server's own lines only: the labels of the player's clicks ("30 Blood Moss", 2026-10-22 06:41:56) are
            // not the cast's answer.
            List<string> heard = mark.Server();
            string? said = heard.Count > 0 ? heard[heard.Count - 1] : null;
            Note("Circuit: recall try " + attempt + " did not move you" + (said != null ? " (\"" + said + "\")" : ""));
            Func<string, bool> has = s => heard.Exists(j => j.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            // A blocked landing is the server's answer, not bad luck: tried once (a recall to Skara Brae from its own
            // landing tile answered this twice, 2026-10-17).
            if (has("blocking the location"))
                return "the landing is blocked";
            if (has("reagents") || has("insufficient"))
                return "the server says the cast is short: \""
                    + heard.Find(j => j.IndexOf("reagents", StringComparison.OrdinalIgnoreCase) >= 0
                    || j.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0) + "\"";
            // L-FUTILITY: cast again only when the server's answer asks for it, a fizzle or a "You must wait" to the
            // cast itself (no words came); any other answer, or none, ends it.
            if (!has("fizzle") && !(has("You must wait") && !mark.PlayerSpoke()))
                return "the recall did not move you" + (said != null ? " (\"" + said + "\")" : "");
            Clock.Sleep(Ms("busy:recall"));
        }
        return "the recall failed " + RecallTries + " times";
    }
}
