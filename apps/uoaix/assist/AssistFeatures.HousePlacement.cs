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
    // ---------- house placement pretest (docs/Designs/Active/Apps/AssistHousePlacement.md, stage 2) ----------
    // "[house <type>" said in the client (a deed's type number or a word of its name) hands the client the deed's multi
    // cursor (0x99, OUO PacketManager_MakePacket_TARGET_MULTI: allow ground, cursor id, 12 zero bytes, multi id, x, y
    // and z offset); the click's answer is kept from the server, judged by UoMap.HouseCheck, told and pinned on the
    // map.
    const uint HouseCursor = 0x484F5553;  // "HOUS"
    volatile int m_houseType = -1;
    bool HoldHouse(GameLink k, byte[] p)
    {
        if (p.Length >= 9 && p[0] == 0x03)
        {
            int end = Array.IndexOf(p, (byte)0, 8);
            string text = Encoding.ASCII.GetString(p, 8, (end < 0 ? p.Length : end) - 8).Trim();
            if (!text.StartsWith("[house", StringComparison.OrdinalIgnoreCase))
                return false;
            string arg = text.Substring(6).Trim().ToLowerInvariant();
            int pick = -1, n;
            if (int.TryParse(arg, out n))
            {
                pick = UoMap.HouseTypes.IndexOf(n);
            }
            else if (arg.Length > 0)
            {
                for (int i = 0; i < UoMap.HouseNames.Length && pick < 0; i++)
                    if (UoMap.HouseNames[i] == arg)
                        pick = i;
                for (int i = 0; i < UoMap.HouseNames.Length && pick < 0; i++)
                    if (UoMap.HouseNames[i].Contains(arg))
                        pick = i;
            }
            if (pick < 0)
            {
                k.Tell("[house <type>, a number or a word of its name:");
                StringBuilder line = new();
                for (int i = 0; i < UoMap.HouseTypes.Length; i++)
                {
                    string e = UoMap.HouseTypes[i] + " " + UoMap.HouseNames[i];
                    if (line.Length + e.Length > 180)
                    {
                        k.Tell(line.ToString());
                        line.Length = 0;
                    }
                    line.Append(line.Length > 0 ? ", " : "").Append(e);
                }
                if (line.Length > 0)
                    k.Tell(line.ToString());
                return true;
            }
            int multi = UoMap.HouseMulti(UoMap.HouseTypes[pick]);
            byte[] c = new byte[26];
            c[0] = 0x99;
            c[1] = 1;
            Put32(c, 2, HouseCursor);
            Put16(c, 18, multi);
            Put16(c, 20, 0);
            Put16(c, 22, UoMap.HouseFront(multi));
            Put16(c, 24, 0);
            string? why = k.ToClient(c, "house test cursor " + multi.ToString("X"));
            if (why == null)
            {
                m_houseType = pick;
                k.Tell("Where would the " + UoMap.HouseNames[pick] + " stand? Target its front row.");
            }
            else
            {
                k.Tell("No house cursor: " + why);
            }

            return true;
        }
        if (p[0] != 0x6C || p.Length < 19 || U32(p, 2) != HouseCursor)
            return false;
        int type = m_houseType;
        m_houseType = -1;
        int x = (p[11] << 8) | p[12], y = (p[13] << 8) | p[14], z = (sbyte)p[16];
        if (type < 0 || x == 0xFFFF)
        {
            k.Tell("House test cancelled.");
            return true;
        }
        int cy;
        UoMap.HouseFit fit = HouseTest(UoMap.HouseMulti(UoMap.HouseTypes[type]), x, y, z, out cy);
        string head = "House test, " + UoMap.HouseNames[type] + " at " + x + "," + cy + "," + z
            + ": ", verdict = HouseVerdict(fit);
        k.Tell(head + verdict, fit.Fits ? 0x44 : 0x22);
        for (int i = 0; i < fit.Why.Count && i < 3; i++)
            k.Tell(fit.Why[i], 0x22);
        Note(head + verdict + (fit.Why.Count > 0 ? "; " + string.Join("; ", fit.Why) : ""));
        List<string> pins = new();
        foreach (string e in Cfg.Get("pins", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = e.Split(new[] { ',' }, 4);
            if (f.Length < 3 || f[2] != "House test")
                pins.Add(e);
        }
        pins.Add(x + "," + cy + ",House test," + UoMap.HouseNames[type] + (fit.Fits ? ": fits" : ": does not fit"));
        Cfg.Set("pins", string.Join("|", pins));
        return true;
    }
    // One judgement for the client's cursor, the map's click and a real deed's placement: multi m with its front row at
    // x, frontY; cy is the house's centre y. areMobilesInMultiArea (housestuff.m Q4X5) refuses before the terrain: any
    // mobile in view on a tile of the house, the player included.
    public UoMap.HouseFit HouseTest(int m, int x, int frontY, int z, out int cy)
    {
        cy = frontY - UoMap.HouseFront(m);
        UoMap.HouseFit fit = UoMap.HouseCheck(m, x, cy, z);
        GameLink k = m_link;
        if (k == null || k.Closed)
            return fit;
        List<int[]> mobs = new();
        lock (k.State.Gate)
        {
            mobs.Add(new[] { k.State.X, k.State.Y });
            foreach (int[] v in k.State.Mobiles.Values)
                mobs.Add(v);
        }
        foreach (int[] t in fit.Tiles)
            foreach (int[] o in mobs)
                if (o[0] == t[0] && o[1] == t[1])
                {
                    fit.Fits = false;
                    t[2] = 0;
                    fit.Why.Insert(0,
                        t[0] + "," + t[1] + ": " + (o == mobs[0] ? "you stand there" : "a mobile stands there"));
                }
        return fit;
    }
    public static string HouseVerdict(UoMap.HouseFit fit)
    {
        int bad = 0;
        foreach (int[] t in fit.Tiles)
            if (t[2] == 0)
                bad++;
        return fit.Fits ? "fits by the client's map and the houses the Assist has seen"
            : fit.Tiles.Count == 0 ? "not judged, " + string.Join("; ", fit.Why)
            : "does not fit, " + bad + " of " + fit.Tiles.Count + " tiles fail";
    }
    // The placement log (stage 4): a real deed's cursor (0x99 from the server, the multi id at 18) and the client's
    // answer with the same cursor id are a placement try. The check's prediction is logged at the click; the server's
    // answer is the new multi at the centre (0x1A with 0x4000) or a line within HousePlaceMs naming a house key
    // (placed) or a house, housing or terrain (refused). Each try is a case line in house-cases-<server>.txt
    // (time|multi|x|front y|z|mobiles on the tiles|outcome) for HouseGrade, which judges every case again with the
    // check as it stands now.
    const int HousePlaceMs = 5000;
    sealed class PlaceTry
    {
        public int Multi, X, Y, Z, Cy; public bool Fits, Mobiles; public DateTime At;
    }
    volatile uint m_placeCursor; volatile int m_placeMulti; volatile PlaceTry? m_placeTry = null;
    void HouseWatchServer(byte[] p)
    {
        if (p[0] == 0x99 && p.Length >= 26)
        {
            m_placeCursor = U32(p, 2);
            m_placeMulti = (p[18] << 8) | p[19];
            return;
        }
        PlaceTry? t = m_placeTry;
        if (t == null)
            return;
        if ((Clock.UtcNow - t.At).TotalMilliseconds > HousePlaceMs)
        {
            m_placeTry = null;
            HouseCase(t, "no answer");
            return;
        }
        if (p[0] == 0x1A && p.Length >= 13)
        {
            uint s = U32(p, 3);
            int at = 7;
            if ((s & 0x80000000) != 0)
                at += 2;
            int g = (p[at] << 8) | p[at + 1];
            at += 2;
            if ((g & 0x8000) != 0)
                at++;
            g &= 0x7FFF;
            if (at + 4 > p.Length || (g & 0x4000) == 0 || (g & 0x3FFF) != t.Multi)
                return;
            int x = ((p[at] << 8) | p[at + 1]) & 0x7FFF, y = ((p[at + 2] << 8) | p[at + 3]) & 0x3FFF;
            m_placeTry = null;
            HouseCase(t, "placed" + (x == t.X && y == t.Cy ? "" : " at " + x + "," + y));
        }
        else if (p[0] == 0x1C && p.Length > 44)
        {
            string line = Encoding.ASCII.GetString(p, 44,
                p.Length - 44).TrimEnd('\0').Trim(), low = line.ToLowerInvariant();
            if (low.Contains("house key"))
            {
                m_placeTry = null;
                HouseCase(t, "placed");
            }
            else if (low.Contains("house") || low.Contains("housing") || low.Contains("terrain"))
            {
                m_placeTry = null;
                HouseCase(t, "refused: " + line.Replace("|", "/"));
            }
        }
    }
    void HouseWatchClient(byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 19 || m_placeCursor == 0 || U32(p, 2) != m_placeCursor)
            return;
        m_placeCursor = 0;
        int x = (p[11] << 8) | p[12], y = (p[13] << 8) | p[14], z = (sbyte)p[16];
        if (x == 0xFFFF)
            return;
        int m = m_placeMulti, cy;
        UoMap.HouseFit fit = HouseTest(m, x, y, z, out cy);
        bool mobiles = fit.Why.Exists(w => w.EndsWith("stands there"));
        m_placeTry = new PlaceTry { Multi = m, X = x, Y = y, Z = z, Cy = cy, Fits = fit.Fits, Mobiles = mobiles,
            At = Clock.UtcNow };
        Note("House placement: " + UoMap.HouseName(m) + " at " + x + "," + cy + "," + z + ", predicted: "
            + HouseVerdict(fit)
            + (fit.Why.Count > 0 ? "; " + string.Join("; ", fit.Why.GetRange(0, Math.Min(3, fit.Why.Count))) : ""));
    }
    void HouseCase(PlaceTry t, string outcome)
    {
        bool placed = outcome.StartsWith("placed"), agree = outcome != "no answer" && placed == t.Fits;
        Note("House placement answer: " + UoMap.HouseName(t.Multi) + " at " + t.X + "," + t.Cy + ": " + outcome
            + "; the check " + (outcome == "no answer" ? "is not graded" : agree ? "agrees" : "DISAGREES"));
        try
        {
            File.AppendAllText(ServerFile("house-cases"),
                Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "|" + t.Multi.ToString("X") + "|" + t.X
                + "|" + t.Y + "|" + t.Z + "|" + (t.Mobiles ? 1 : 0) + "|" + outcome + Environment.NewLine);
        }
        catch { }
    }
    // Every logged case judged again by the check as it stands (terrain only: the mobiles are those recorded at the
    // try).
    public string HouseGrade()
    {
        string path = ServerFile("house-cases");
        if (!File.Exists(path))
            return "no house cases yet";
        int n = 0, agree = 0;
        List<string> wrong = new();
        foreach (string line in File.ReadAllLines(path))
        {
            string[] c = line.Split('|');
            int m, x, y, z;
            if (c.Length < 7 || c[6] == "no answer" || !int.TryParse(c[1], NumberStyles.HexNumber, null, out m)
                || !int.TryParse(c[2], out x) || !int.TryParse(c[3], out y) || !int.TryParse(c[4], out z))
                continue;
            int cy = y - UoMap.HouseFront(m);
            UoMap.HouseFit fit = UoMap.HouseCheck(m, x, cy, z);
            bool fits = fit.Fits && c[5] != "1", placed = c[6].StartsWith("placed");
            n++;
            if (fits == placed)
                agree++;
            else if (wrong.Count < 10)
                wrong.Add(UoMap.HouseName(m) + " at " + x + "," + cy + "," + z + " " + c[6] + ", the check says "
                    + (fits ? "fits" : string.Join("; ", fit.Why.GetRange(0, Math.Min(2, fit.Why.Count)))));
        }
        return n + " house cases, " + agree + " agree"
            + (wrong.Count > 0 ? "; disagree: " + string.Join(" | ", wrong) : "");
    }
    // A click, double-click or lift of a marker never reaches the server; a lift is refused to the client (0x27).
    bool HoldMarker(GameLink k, byte[] p)
    {
        if (p.Length < 5 || (p[0] != 0x06 && p[0] != 0x09 && p[0] != 0x07))
            return false;
        uint s = U32(p, 1);
        if (!IsMarker(s))
            return false;
        if (p[0] == 0x07)
            k.ToClient(new byte[] { 0x27, 0 }, "marker lift refused");
        // A single click names the bank (Damian, 2026-10-15: "make them click name for the map location and which
        // corner its in"): the rune stands on the bank's north-west tile, its first.
        if (p[0] == 0x09)
        {
            int bx = (int)((s >> 10) & 0x3FF), by = (int)(s & 0x3FF), x = bx * 8, y = by * 8;
            Label(k, s,
                "8x8 bank " + bx + "," + by + ": north-west corner " + x + "," + y + " of tiles " + x + "-" + (x + 7)
                + ", " + y + "-" + (y + 7));
        }
        return true;
    }
    // A label over an item (0x1C type 6, the item's serial and graphic), for the client only.
    void Label(GameLink k, uint serial, string text)
    {
        byte[] t = Encoding.ASCII.GetBytes(text.Length > 200 ? text.Substring(0, 200) : text);
        byte[] p = new byte[45 + t.Length];
        p[0] = 0x1C;
        Put16(p, 1, p.Length);
        Put32(p, 3, serial);
        Put16(p, 7, MarkerArt);
        p[9] = 6;
        Put16(p, 10, MarkerHue);
        Put16(p, 12, 3);
        t.CopyTo(p, 44);
        k.ToClient(p, "marker label");
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

    // The bag pricer (Damian, 2026-10-15: "you target a bag, then it counts everything in it, and gives them all a
    // default price to sell at on player vendor"): a target cursor of the assist's own, shown to the client only; the
    // client's answer is held from the server. The picked bag and every container inside it is opened (double-clicked)
    // once, so the server sends their contents; PricerSeq moves when the count is ready.
    public const uint PricerCursor = 0x50524943;  // "PRIC": no server cursor id this client has seen is near it
    public const int PricerBagsMax = 24;
    int PricerOpenMs => Ms("pricer-open");
    public volatile uint PricerBag;
    public long PricerSeq; public volatile string PricerStatus = "Target a bag to price its contents.";
    public string? PricerTarget()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, PricerCursor);
        string? why = k.ToClient(p, "pricer cursor");
        PricerStatus = why == null ? "Pick the bag to price." : "No cursor: " + why;
        return why;
    }
    bool HoldPricer(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != PricerCursor)
            return false;
        uint s = U32(p, 7);
        if (s == 0 || p[1] != 0)
        {
            PricerStatus = "Pricing cancelled: pick a bag, not the ground.";
            return true;
        }
        PricerBag = s;
        PricerStatus = "Opening the bag...";
        Spawn("the bag pricer", () => PricerOpen(k, s));
        return true;
    }
    public static bool IsContainer(int graphic)
    {
        List<UoTiles.Tile> l = UoTiles.Items();
        return graphic >= 0 && graphic < l.Count && (l[graphic].Flags & 0x00200000) != 0;
    }
    // The containers named for books in the 1.25 TILEDATA (the bookcases 0x0A97-0x0A9C and the spellbook 0x0EFA, a scan
    // of 2026-10-23): a book's pages and a spellbook's scrolls came in as items, so none is a bag to open.
    static readonly ImmutableArray<int> s_bookContainers = [ 0x0A97, 0x0A98, 0x0A99, 0x0A9A, 0x0A9B, 0x0A9C, 0x0EFA ];
    // A bag of the pack's to open: a container, never a book nor a key ring (IsKey).
    public static bool PackBag(int graphic)
    {
        return IsContainer(graphic) && !IsKey(graphic) && s_bookContainers.IndexOf(graphic) < 0;
    }
    void PricerOpen(GameLink k, uint bag)
    {
        Set<uint> opened = Set<uint>.Empty;
        Queue<uint> queue = new();
        queue.Enqueue(bag);
        while (queue.Count > 0 && opened.Count < PricerBagsMax && !k.Closed)
        {
            uint b = queue.Dequeue();
            if (opened.Contains(b))
                continue;
            opened = opened.Add(b);
            string? why = "busy";
            for (int tries = 0; tries < 10 && why != null; tries++)
            {
                using (Act(k))
                    why = k.Use(b, false);
                if (why != null)
                    Clock.Sleep(300);
            }
            if (why != null)
            {
                PricerStatus = "Could not open " + b.ToString("X") + ": " + why;
                break;
            }
            Clock.Sleep(PricerOpenMs);
            lock (k.State.Gate)
                foreach (KeyValuePair<uint, AssistItem> kv in k.State.Items)
                    if (kv.Value.Container == b && IsContainer(kv.Value.Graphic) && !IsKey(kv.Value.Graphic)
                        && !opened.Contains(kv.Key))
                        queue.Enqueue(kv.Key);
            PricerStatus = "Opened " + opened.Count + " bag" + (opened.Count == 1 ? "" : "s") + "...";
            Interlocked.Increment(ref PricerSeq);
        }
        PricerStatus = "Counted " + bag.ToString("X") + " (" + opened.Count + " bag" + (opened.Count == 1 ? "" : "s")
            + " opened).";
        Interlocked.Increment(ref PricerSeq);
    }
}
