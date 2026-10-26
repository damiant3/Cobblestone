#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // The advertiser, "Advertise" (circuit advert; Damian, 2026-10-18: "an advertiser circuit that buys empty books,
    // writes an ad, recalls to major banks and drops a book on the ground there so people see it. make it configurable
    // for which shops/banks/other localles, how many books per bank, and the message"): blank books (BookArts) bought
    // at the town's provisioners and scribes (BookShopKinds, nearest first, advert.price gp at most) for every place
    // short; each written by the packets the client sends for a book (0x93 title and author, 0x66 pages; ServUO
    // BaseBook.OldHeaderChange and ContentChange take them for any writable book within a tile, the pack's included, no
    // skill used) and its title read back by a click; then each place in turn (advert.places: bank:<city>, shop:<shop
    // key>, spot:<x>,<y>), reached by the bank's rune for its city and a walk, its advert.per books put down on free
    // tiles beside the player. A book this session put down that still lies there counts toward them. ServUO decays a
    // floor item in 60 minutes unless the shard sets otherwise (Item.DefaultDecayTime). ServUO's Inscription copies a
    // written book for free (Inscribe.cs: no materials, a check of 0 to 50.0, a failure loses nothing); the packets
    // make it unneeded. The books the vendors sell (ServUO SBProvisioner and SBScribe: brown 0FEF, tan 0FF0, red 0FF1,
    // blue 0FF2), every one read for the ad's title in the pack; the ones bought are picked on the Circuits tab
    // (advert.books; Damian, 2026-10-18: "i need a picker for book type with graphics from the game files that match
    // books sold at the vendors"), red and blue unset: 1998 refuses to write into brown and tan ("Cannot write into
    // that book.", scripts.wombat inscribe.m).
    static readonly ImmutableArray<int> s_bookArts = [ 0x0FEF, 0x0FF0, 0x0FF1, 0x0FF2 ];
    public static ImmutableArray<int> BookArts => s_bookArts;
    static readonly ImmutableArray<string> s_bookNames = [ "brown book", "tan book", "red book", "blue book" ];
    public static ImmutableArray<string> BookNames => s_bookNames;
    public static bool BookWritable1998(int art)
    {
        return art == 0x0FF1 || art == 0x0FF2;
    }
    public int[] AdBookArts
    {
        get
        {
            List<int> l = new();
            foreach (string s in Cfg.Get(Server + "advert.books", "0FF1,0FF2").Split(','))
            {
                int v;
                if (int.TryParse(s.Trim(), NumberStyles.HexNumber, null, out v) && s_bookArts.IndexOf(v) >= 0)
                    l.Add(v);
            }
            return l.Count > 0 ? l.ToArray() : new[] { 0x0FF1, 0x0FF2 };
        }

        set => Cfg.Set(Server + "advert.books",
            string.Join(",", Array.ConvertAll(value ?? new int[0], a => a.ToString("X4"))));
    }
    static readonly ImmutableArray<string> s_bookShopKinds = [ "PROVISIONER", "SCRIBE" ];
    // A page of the 1.25 book gump holds 8 lines (ServUO refuses more than 8 a page, and a line of 80 characters or
    // more); vendor books hold 20 pages (TanBook, RedBook; BlueBook 40); the old header carries a title of 60 bytes and
    // an author of 30, NUL included.
    public const int AdLineWidth = 24, AdPageLines = 8, AdPagesMax = 20, AdTitleMax = 59, AdAuthorMax = 29;
    public const string AdPlacesDefault = "bank:Britain;bank:Vesper;bank:Minoc;bank:Trinsic;bank:Moonglow";
    public string AdTitle
    {
        get => Cfg.Get(Server + "advert.title", "For sale");
        set => Cfg.Set(Server + "advert.title", (value ?? "").Trim());
    }
    // The message; "|" starts a new line.
    public string AdText
    {
        get => Cfg.Get(Server + "advert.text", "");
        set => Cfg.Set(Server + "advert.text", value ?? "");
    }
    // Empty is the player's name.
    public string AdAuthor
    {
        get => Cfg.Get(Server + "advert.author", "");
        set => Cfg.Set(Server + "advert.author", (value ?? "").Trim());
    }
    public int AdPer
    {
        get => Math.Max(1, Cfg.GetInt(Server + "advert.per", 1));
        set => Cfg.Set(Server + "advert.per", Math.Max(1, value).ToString());
    }
    // Twice ServUO's 15 at SBProvisioner and SBScribe.
    public int AdPrice
    {
        get => Math.Max(1, Cfg.GetInt(Server + "advert.price", 30));
        set => Cfg.Set(Server + "advert.price", Math.Max(1, value).ToString());
    }
    public List<string> AdPlaces()
    {
        List<string> l = new();
        foreach (string e in Cfg.Get(Server + "advert.places", AdPlacesDefault).Split(new[] { ';' },
            StringSplitOptions.RemoveEmptyEntries))
            if (e.Trim().Length > 0)
                l.Add(e.Trim());
        return l;
    }
    public void SetAdPlaces(List<string> l)
    {
        Cfg.Set(Server + "advert.places", string.Join(";", l));
    }
    // A place as its town and the tile walked to (a bank: its bank nearest x, y); null, or why it is not known.
    string? AdPlace(string place, int x0, int y0, out string? city, out int x, out int y)
    {
        city = null;
        x = 0;
        y = 0;
        int c = place.IndexOf(':');
        if (c < 0)
            return "a place is bank:<city>, shop:<shop key> or spot:<x>,<y>";
        string kind = place.Substring(0, c).Trim().ToLowerInvariant(), v = place.Substring(c + 1).Trim();
        if (kind == "bank")
        {
            List<int[]> b = UoRegions.BanksIn(v, x0, y0);
            if (b.Count == 0)
                return "no bank known in " + v;
            city = v;
            x = b[0][0];
            y = b[0][1];
            return null;
        }
        if (kind == "shop")
        {
            UoRegions.Shop? s = null;
            foreach (string cc in UoRegions.Cities())
            {
                s = UoRegions.AllShopsIn(cc).Find(t => t.Key == v);
                if (s != null)
                    break;
            }
            if (s == null)
                return "no shop " + v;
            city = UoRegions.City(s.CX, s.CY);
            x = s.CX;
            y = s.CY;
            return city == null ? "the shop " + v + " is in no town" : null;
        }
        if (kind == "spot")
        {
            string[] p = v.Split(',');
            if (p.Length < 2 || !int.TryParse(p[0].Trim(), out x) || !int.TryParse(p[1].Trim(), out y))
                return "a spot is spot:<x>,<y>";
            city = UoRegions.City(x, y);
            return city == null ? "the spot " + x + "," + y + " is in no town, and travel goes by a bank's rune" : null;
        }
        return "a place is bank:<city>, shop:<shop key> or spot:<x>,<y>";
    }
    // Where the player stands, added to the places: the bank within 12 tiles, else the shop stood in, else the spot.
    public string AdAddHere()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        string? city = UoRegions.City(x, y);
        if (city == null)
            return "not in a town: travel goes by a bank's rune";
        string? place = null;
        foreach (int[] b in UoRegions.BanksIn(city, x, y))
            if (Tiles(b[0], b[1], x, y) <= 12)
            {
                place = "bank:" + city;
                break;
            }
        if (place == null)
        {
            UoRegions.Shop? s = UoRegions.AllShopsIn(city).Find(t => t.Holds(x, y, 0));
            if (s != null)
                place = "shop:" + s.Key;
        }
        if (place == null)
            place = "spot:" + x + "," + y;
        List<string> l = AdPlaces();
        if (!l.Contains(place))
            l.Add(place);
        SetAdPlaces(l);
        return "added " + place;
    }
    // The message as book lines, each at most AdLineWidth, broken at spaces; "|" starts a line.
    static List<string> AdLines(string text)
    {
        List<string> lines = new();
        foreach (string para in (text ?? "").Split('|'))
        {
            string line = "";
            foreach (string w0 in para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = w0;
                while (w.Length > AdLineWidth)
                {
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                        line = "";
                    }
                    lines.Add(w.Substring(0, AdLineWidth));
                    w = w.Substring(AdLineWidth);
                }
                if (line.Length == 0)
                {
                    line = w;
                }
                else if (line.Length + 1 + w.Length <= AdLineWidth)
                {
                    line += " " + w;
                }
                else
                {
                    lines.Add(line);
                    line = w;
                }
            }
            lines.Add(line);
        }
        while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }
    static byte[] AdAscii(string s, int max)
    {
        byte[] b = new byte[Math.Min(max, s.Length)];
        for (int i = 0; i < b.Length; i++)
        {
            char c = s[i];
            b[i] = c >= 32 && c < 127 ? (byte)c : (byte)'?';
        }
        return b;
    }
    // The book's title, author and pages sent as the client sends them after writing.
    string? WriteAd(GameLink k, uint book, string title, string author, List<string> lines)
    {
        int pages = Math.Min(AdPagesMax, (lines.Count + AdPageLines - 1) / AdPageLines);
        if (pages == 0)
            return "the ad has no text";
        // The 1.25.32 client's 0x93 is 98 bytes: serial, one writable flag, the page count, a 60-byte title, a 30-byte
        // author.
        byte[] h = new byte[98];
        h[0] = 0x93;
        Put32(h, 1, book);
        h[5] = 1;
        h[6] = (byte)(pages >> 8);
        h[7] = (byte)pages;
        byte[] tb = AdAscii(title, AdTitleMax);
        Array.Copy(tb, 0, h, 8, tb.Length);
        byte[] ab = AdAscii(author, AdAuthorMax);
        Array.Copy(ab, 0, h, 68, ab.Length);
        List<byte> body = new()
        { 0x66, 0, 0, (byte)(book >> 24), (byte)(book >> 16), (byte)(book >> 8), (byte)book, (byte)(pages >> 8),
            (byte)pages };
        for (int p = 0; p < pages; p++)
        {
            int from = p * AdPageLines, n = Math.Min(AdPageLines, lines.Count - from);
            body.Add((byte)((p + 1) >> 8));
            body.Add((byte)(p + 1));
            body.Add(0);
            body.Add((byte)n);
            for (int i = 0; i < n; i++)
            {
                body.AddRange(AdAscii(lines[from + i], 79));
                body.Add(0);
            }
        }
        byte[] b = body.ToArray();
        b[1] = (byte)(b.Length >> 8);
        b[2] = (byte)b.Length;
        using (Act(k))
        {
            // No server packet answers a book write, so neither send is tracked; the title read back by a click checks
            // both.
            string? w = k.Inject(h, "book title " + book.ToString("X"), false);
            if (w != null)
                return w;
            return k.Inject(b, "book pages " + book.ToString("X"), false);
        }
    }
    // Every label a click on the item brings (a book answers "<title> by <author>", then "[N pages]").
    string AllLabels(GameLink k, uint serial)
    {
        List<string> said = new();
        Action<byte[]> h = p => { if (p[0] == 0x1C && p.Length > 44
            && U32(p, 3)
            == serial) lock (said) said.Add(Encoding.ASCII.GetString(p, 44, p.Length - 44).TrimEnd('\0')); };
        k.ServerPacket += h;
        lock (m_lookingGate)
            m_looking = m_looking.Set(serial, Clock.UtcNow.AddMilliseconds(LabelWaitMs + LabelGapMs));
        try
        {
            using (Act(k))
                k.Look(serial);
            Wait.For(Clock, LabelWaitMs);
        }
        finally { k.ServerPacket -= h; }
        lock (said)
            return string.Join(" | ", said);
    }
    List<uint> PackBooks(GameLink k)
    {
        List<uint> l = new();
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && s_bookArts.IndexOf(it.Graphic) >= 0)
                    l.Add(it.Serial);
        }
        return l;
    }
    // The book put down on the first tile beside the player it stays on, free tiles first. Null, or why not.
    string? AdDrop(GameLink k, uint book)
    {
        int px, py, pz;
        List<int[]> tiles = new();
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
            Set<long> taken = Set<long>.Empty;
            foreach (AssistItem g in k.State.Ground.Values)
                taken = taken.Add(((long)g.X << 16) | (uint)g.Y);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < s_scrapTiles.GetLength(0); i++)
                {
                    int tx = px + s_scrapTiles[i, 0], ty = py + s_scrapTiles[i, 1];
                    if (taken.Contains(((long)tx << 16) | (uint)ty) == (pass == 1))
                        tiles.Add(new[] { tx, ty });
                }
        }
        foreach (int[] t in tiles)
        {
            if (circuitStop)
                return "stopped";
            using (Act(k))
            {
                string? w = MoveStack(k, book, 1, "the book", "Putting the ad down",
                    () => k.DropGround(book, t[0], t[1], pz)!);
                if (w != null)
                    return w;
            }
            Leave(book);
            AssistItem? it = Item(k, book);
            bool inPack;
            lock (k.State.Gate)
                inPack = it != null && PackTree(k).Contains(it.Container);
            if (!inPack)
                return null;
        }
        return "the book came back into the pack from every tile beside the player";
    }
    // A bank's roof for the ad (Damian, 2026-10-19: "lets use the bank roof here. we should look for any accessible
    // bank roof. prevents people from messin with it"): the tiles of the bank's rectangles whose highest standable
    // surface (a TILEDATA surface 0x200, not impassable 0x40, stood on at its z plus its height, half for a bridge
    // 0x400) has no static above it and stands AdRoofRise or more over the land, as the First Bank of Britain's stone
    // pavers at z 20 do. The one with the most roof neighbours at its height, then nearest the bank's centre, is walked
    // to; one walk tries a roof (L-FUTILITY), a reached one is kept per server (advert.roof.<bank>) and an unreached
    // one skipped until the next circuit start (roofNone, cleared with the avoidances).
    public const int AdRoofRise = 15;
    Set<string> m_roofNone = Set<string>.Empty; readonly Lock m_roofNoneGate = new();
    static int[]? RoofTile(int[] bank)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        ValueMap<long, int> roof = ValueMap<long, int>.Empty;
        foreach (int[] r in UoRegions.BankRects(bank[0], bank[1]))
        {
            for (int x = r[0]; x < r[0] + r[2]; x++)
            {
                for (int y = r[1]; y < r[1] + r[3]; y++)
                {
                    int stand = int.MinValue, above = int.MinValue;
                    List<int[]> st = UoMap.StaticsAt(x, y);
                    foreach (int[] s in st)
                    {
                        if (s[0] < 0 || s[0] >= items.Count)
                            continue;
                        UoTiles.Tile t = items[s[0]];
                        above = Math.Max(above, s[1]);
                        if ((t.Flags & 0x200) == 0 || (t.Flags & 0x40) != 0)
                            continue;
                        stand = Math.Max(stand, s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height));
                    }
                    int land = UoMap.LandStandZ(x, y);
                    if (stand == int.MinValue || land == int.MinValue || above > stand || stand < land + AdRoofRise)
                        continue;
                    roof = roof.Set(((long)x << 16) | (uint)y, stand);
                }
            }
        }

        int[]? best = null;
        int bestN = -1, bestD = int.MaxValue;
        foreach (KeyValuePair<long, int> kv in roof)
        {
            int x = (int)(kv.Key >> 16), y = (int)(kv.Key & 0xFFFF), n = 0, z;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if ((dx != 0 || dy != 0) && roof.TryGet(((long)(x + dx) << 16) | (uint)(y + dy), out z)
                        && z == kv.Value)
                        n++;
            int d = Tiles(x, y, bank[0], bank[1]);
            if (n > bestN || (n == bestN && d < bestD))
            {
                best = new[] { x, y, kv.Value };
                bestN = n;
                bestD = d;
            }
        }
        return best;
    }
    // The player on a roof of one of the city's banks, nearest first; null, or why none was reached.
    string? ToBankRoof(GameLink k, string city, out int[]? at)
    {
        at = null;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        List<string> why = new();
        foreach (int[] bank in CityBanks(city, px, py))
        {
            if (circuitStop)
                return "stopped";
            string key = Server + "advert.roof." + bank[0] + "," + bank[1], name = "the bank at " + bank[0] + ","
                + bank[1];
            if (m_roofNone.Contains(key))
                continue;
            int[]? tile = null;
            string[] kept = Cfg.Get(key, "").Split(',');
            int kx, ky, kz;
            if (kept.Length == 3 && int.TryParse(kept[0], out kx) && int.TryParse(kept[1], out ky)
                && int.TryParse(kept[2], out kz))
                tile = new[] { kx, ky, kz };
            if (tile == null)
                tile = RoofTile(bank);
            if (tile == null)
            {
                lock (m_roofNoneGate)
                    m_roofNone = m_roofNone.Add(key);
                why.Add(name + " has no roof to stand on");
                continue;
            }
            CircuitStatus = "Advertise: walking onto the roof of " + name;
            string? w = WalkTo(tile[0], tile[1], 0, false, tile[2]);
            int z;
            lock (k.State.Gate)
                z = k.State.Z;
            if (w == null && z >= tile[2] - UoMap.StepUp)
            {
                Cfg.Set(key, tile[0] + "," + tile[1] + "," + tile[2]);
                at = tile;
                Note("Advertise: on the roof of " + name + " at " + tile[0] + "," + tile[1] + " z " + z);
                return null;
            }
            lock (m_roofNoneGate)
                m_roofNone = m_roofNone.Add(key);
            Cfg.Set(key, "");
            why.Add(name + "'s roof at " + tile[0] + "," + tile[1] + " z " + tile[2] + ": "
                + (w ?? "the walk ended at z " + z));
        }
        return why.Count > 0 ? string.Join("; ", why) : "no bank roof left to try in " + city;
    }
    volatile bool m_advertMode;
    public volatile string AdStatus = "";
    Set<uint> m_adDropped = Set<uint>.Empty;
    public string? CircuitAdvert()
    {
        return StartCircuit(() => m_advertMode = true);
    }
    string RunAdvert(GameLink k)
    {
        int placed = 0, written = 0, bought = 0;
        string? end = null;
        try
        {
            string title = AdTitle;
            List<string> lines = AdLines(AdText);
            List<string> places = AdPlaces();
            if (title.Length == 0)
                return end = "the ad has no title";
            if (lines.Count == 0)
                return end = "the ad has no text";
            if (places.Count == 0)
                return end = "no place is picked";
            foreach (string pl in places)
            {
                string? c0;
                int x0, y0;
                string? pw = AdPlace(pl, 0, 0, out c0, out x0, out y0);
                if (pw != null)
                    return end = pl + ": " + pw;
            }
            int per = AdPer, total = per * places.Count;
            string author = AdAuthor;
            if (author.Length == 0)
            {
                uint me;
                lock (k.State.Gate)
                    me = k.State.Player;
                author = MobileName(k, me);
            }
            // The ad's books already in the pack, by the title a click reads; a book of the player's own is never
            // written.
            List<uint> ready = new();
            foreach (uint s in PackBooks(k))
                if (AllLabels(k, s).IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                    ready.Add(s);
            if (ready.Count < total)
            {
                string? city = CityOf(k);
                if (city == null)
                    return end = "start in a town: blank books are bought at its provisioners and scribes";
                // Gold for the books short at the price cap, from the city's bank when the pack holds less (Damian,
                // 2026-10-19: "be sure it has 'get gold if needed'").
                int cost = (total - ready.Count) * AdPrice;
                if (PackCount(k, g => g == GoldArt) < cost)
                {
                    int gx, gy;
                    lock (k.State.Gate)
                    {
                        gx = k.State.X;
                        gy = k.State.Y;
                    }
                    List<int[]> cb = CityBanks(city, gx, gy);
                    if (cb.Count == 0)
                        return end = "no bank in " + city + " for the books' " + cost + " gold";
                    CircuitStatus = "Advertise: gold from the bank";
                    string taken;
                    string? gw = ToBanker(k, cb[0]);
                    if (gw == null)
                        gw = PurseTo(Math.Max(cost, PurseKeep), out taken);
                    if (gw != null)
                        return end = "gold from the bank: " + gw;
                    Note("Advertise: " + PackCount(k, g => g == GoldArt) + " gold in the pack for "
                        + (total - ready.Count) + " books");
                }
                Set<uint> before = Set<uint>.Of(PackBooks(k).ToArray());
                List<BuyItem> list = new()
                { new BuyItem { Name = "book", Arts = AdBookArts, Count = total - ready.Count, MaxPrice = AdPrice } };
                ValueMap<string, int> left = ValueMap<string, int>.Of(("book", total - ready.Count));
                List<UoRegions.Shop> shops = UoRegions.ShopsIn(city, s_bookShopKinds);
                int qx, qy;
                lock (k.State.Gate)
                {
                    qx = k.State.X;
                    qy = k.State.Y;
                }
                shops.Sort((a, b) => Tiles(a.CX, a.CY, qx, qy).CompareTo(Tiles(b.CX, b.CY, qx, qy)));
                if (shops.Count == 0)
                    Note("Advertise: no provisioner or scribe in " + city);
                foreach (UoRegions.Shop s in shops)
                {
                    if (circuitStop || left.Get("book", 0) <= 0)
                        break;
                    end = BuyInShop(k, s, list, ref left, ref bought);
                    if (end != null)
                        return end;
                }
                // Each new book written and its title read back; a book the server did not write ends it (L-FUTILITY).
                foreach (uint s in PackBooks(k))
                {
                    if (before.Contains(s) || circuitStop)
                        continue;
                    CircuitStatus = "Advertise: writing the ad in " + s.ToString("X");
                    string? w = WriteAd(k, s, title, author, lines);
                    if (w != null)
                        return end = "writing the ad: " + w;
                    Clock.Sleep(DropSettleMs);
                    string label = AllLabels(k, s);
                    if (label.IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0)
                        return end = "the book " + s.ToString("X") + " did not take the ad (its label: "
                            + (label.Length > 0 ? label : "none") + ")";
                    ready.Add(s);
                    written++;
                }
                if (ready.Count == 0)
                    return end = "no book bought, and no ad book in the pack";
            }
            foreach (string pl in places)
            {
                if (circuitStop || ready.Count == 0)
                    break;
                int px, py;
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                }
                string? city;
                int tx, ty;
                AdPlace(pl, px, py, out city, out tx, out ty);
                if (CityOf(k) != city)
                {
                    string? tr = TripReady(k);
                    if (tr != null)
                    {
                        end = tr;
                        break;
                    }
                    CircuitStatus = "Advertise: travelling to " + city;
                    string? tw = BankRecall(k, city!, false);
                    if (tw != null)
                    {
                        Note("Advertise: " + pl + ": " + tw);
                        if (TripStops(tw))
                        {
                            end = "travelling to " + city + ": " + tw;
                            break;
                        }
                        continue;
                    }
                    if (CityOf(k) != city)
                    {
                        end = "the rune for " + city + " landed in " + (CityOf(k) ?? "no city");
                        break;
                    }
                    lock (k.State.Gate)
                    {
                        px = k.State.X;
                        py = k.State.Y;
                    }
                    AdPlace(pl, px, py, out city, out tx, out ty);
                }
                // A bank place puts its books on a roof of the city's banks when one is reached, else beside the bank.
                int[]? roof = null;
                if (pl.StartsWith("bank:", StringComparison.OrdinalIgnoreCase))
                {
                    string? rw = ToBankRoof(k, city!, out roof);
                    if (rw != null)
                        Note("Advertise: " + pl + ": no roof, the ground instead: " + rw);
                }
                if (roof != null)
                {
                    tx = roof[0];
                    ty = roof[1];
                }
                else
                {
                    CircuitStatus = "Advertise: walking to " + pl;
                    string? ww = WalkTo(tx, ty, 2);
                    if (ww != null)
                    {
                        Note("Advertise: " + pl + ": not reached: " + ww);
                        continue;
                    }
                }
                int lying = 0;
                lock (k.State.Gate)
                    foreach (AssistItem g in k.State.Ground.Values)
                        if (m_adDropped.Contains(g.Serial) && Tiles(g.X, g.Y, tx, ty) <= 4)
                            lying++;
                int put = 0;
                for (int n = lying; n < per && ready.Count > 0 && !circuitStop; n++)
                {
                    uint book = ready[0];
                    ready.RemoveAt(0);
                    CircuitStatus = "Advertise: putting the ad down at " + pl;
                    string? dw = AdDrop(k, book);
                    if (dw != null)
                    {
                        Note("Advertise: " + pl + ": " + dw);
                        break;
                    }
                    m_adDropped = m_adDropped.Add(book);
                    put++;
                    placed++;
                }
                Note("Advertise: " + pl + ": " + put + " put down"
                    + (lying > 0 ? ", " + lying + " of this session's still lying there" : ""));
            }
            if (end == null)
                end = circuitStop ? "stopped" : placed + " books put down";
            return end;
        }
        finally
        {
            m_advertMode = false;
            AdStatus = bought + " books bought, " + written + " written, " + placed + " put down"
                + (end != null ? "; " + end : "");
            Note("Advertise: " + AdStatus);
        }
    }
}
