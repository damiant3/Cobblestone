#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

// The 1998 server's regions (OUO/UoDemo regions.txt: id, x, y, width, height, z low, z high, flags, NAME, numbers,
// description), read from regions-1998.txt beside the assist's settings when present. The smallest region holding the
// player names the zone; a CITY_ or JUSTICE_ region is guarded (OUO RegionManager isInCityRegion, inJusticeRegion).
public static class UoRegions
{
    sealed class R
    {
        public int X, Y, W, H, Z0, Z1; public string Name = null!, Text = null!;
    }
    static List<R>? s_regions; static string? s_loadedFrom;
    public static void Load(string path)
    {
        if (s_loadedFrom == path)
            return;
        s_loadedFrom = path;
        s_regions = null;
        s_signs = null;
        lock (s_displaysGate)
            s_displays = s_displays.Clear();
        if (!File.Exists(path))
            return;
        List<R> list = new();
        foreach (string line in File.ReadAllLines(path))
        {
            string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 9)
                continue;
            int[] n = new int[7];
            bool ok = true;
            for (int i = 0; i < 7 && ok; i++)
                ok = int.TryParse(t[i + 1], out n[i]);
            if (!ok)
                continue;
            list.Add(new R { X = n[0], Y = n[1], W = n[2], H = n[3], Z0 = n[4], Z1 = n[5], Name = t[8],
                Text = t.Length > 14 ? string.Join(" ", t, 14, t.Length - 14) : "" });
        }
        s_regions = list;
    }
    public static bool Loaded => s_regions != null;
    // The house deed's area rule (rundir housedeed.m: isHousingOkay(place, 1); OUO region.c:1302
    // RegionManager_isHousingOkay): a region at the spot named housing_yes... allows, else one named dungeon, dungn,
    // city, town, housing_no or justice... denies (each a case-blind name prefix, RegionGrid_SearchTag); none allows.
    // Null when allowed, else the denying name.
    static readonly ImmutableArray<string> s_housingDeny = [ "dungeon", "dungn", "city", "town", "housing_no",
        "justice" ];
    public static string? HousingDenied(int x, int y, int z)
    {
        if (s_regions == null)
            return null;
        string? deny = null;
        foreach (R r in s_regions)
        {
            if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || z < r.Z0 || z > r.Z1)
                continue;
            if (r.Name.StartsWith("housing_yes", StringComparison.OrdinalIgnoreCase))
                return null;
            foreach (string d in s_housingDeny)
                if (deny == null && r.Name.StartsWith(d, StringComparison.OrdinalIgnoreCase))
                    deny = r.Name;
        }
        return deny;
    }
    // The regions overlapping a box, less the whole-map ones, for the map's region layer.
    public sealed class Box
    {
        public int X, Y, W, H; public string Label = null!;
    }
    public static List<Box> InView(int x0, int y0, int x1, int y1)
    {
        List<Box> list = new();
        if (s_regions == null)
            return list;
        foreach (R r in s_regions)
        {
            if (r.Name.StartsWith("BRITANNIA") || r.Name.StartsWith("OCEAN") || r.X >= x1 || r.Y >= y1
                || r.X + r.W <= x0 || r.Y + r.H <= y0)
                continue;
            list.Add(new Box { X = r.X, Y = r.Y, W = r.W, H = r.H, Label = r.Text.Length > 0 ? r.Text : r.Name });
        }
        return list;
    }
    // "City, shop" for a tile (Damian, 2026-10-15: "for the where, put the name of the city and name of shop too"): the
    // city is the smallest CITY_ or JUSTICE_ region's text, the shop the smallest other described region's ("The Hammer
    // and Anvil in Britain", its " in <city>" dropped); null with no region file or neither found.
    static R? CityAndShop(int x, int y, out R? city)
    {
        R? shop = null;
        city = null;
        long ca = long.MaxValue, sa = long.MaxValue;
        foreach (R r in s_regions!)
        {
            if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || r.Text.Length == 0)
                continue;
            long a = (long)r.W * r.H;
            if (r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE"))
            {
                if (a < ca)
                {
                    ca = a;
                    city = r;
                }
            }
            else if (!r.Name.StartsWith("BRITANNIA") && !r.Name.StartsWith("OCEAN") && a < sa)
            {
                sa = a;
                shop = r;
            }
        }
        return shop;
    }
    // The shop's trade from its region name (BLACKSMITH_BRITAIN_1 is "blacksmith"); null outside every shop region.
    public static string? ShopKind(int x, int y)
    {
        if (s_regions == null)
            return null;
        R? city;
        R? shop = CityAndShop(x, y, out city);
        return shop == null ? null : shop.Name.Split('_')[0].ToLowerInvariant();
    }
    // The smallest CITY_ region holding x, y, its text; null for none. A JUSTICE_ zone is no town of its own here: the
    // Moonglow house stands in JUSTICE_MOONGLOW_3, "telescope in Moonglow", and City named that its town.
    public static string? CityRegion(int x, int y)
    {
        if (s_regions == null)
            return null;
        R? best = null;
        foreach (R r in s_regions)
        {
            if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || r.Text.Length == 0
                || !r.Name.StartsWith("CITY"))
                continue;
            if (best == null || (long)r.W * r.H < (long)best.W * best.H)
                best = r;
        }
        return best?.Text;
    }
    public static string? City(int x, int y)
    {
        if (s_regions == null)
            return null;
        R? city;
        CityAndShop(x, y, out city);
        return city == null ? null : city.Text;
    }
    // The centre of the city's bank nearest x, y (a BANK_ region inside it), or null: where a trip banks its gold. The
    // nearest, not the smallest: Britain's east bank region is the smaller, and a deposit by the First Bank walked
    // east. The shops of a kind in a city (regions whose name starts with one of the prefixes, joined by their
    // description, "Adventurer's Clothing in Trinsic" being a WEAVER_ and a CLOTHIER_ region): description, box,
    // centre.
    public sealed class Shop
    {
        public string Text = null!;
        public int X, Y, W, H;
        public int CX => X + W / 2;
        public int CY => Y + H / 2;
        public string Key => Text + " @" + CX + "," + CY; public bool Holds(int x, int y, int pad)
        {
            return x >= X - pad && y >= Y - pad && x < X + W + pad && y < Y + H + pad;
        }
    }
    const int ShopJoin = 16;
    public static List<Shop> ShopsIn(string city, IReadOnlyList<string> prefixes)
    {
        List<Shop> l = new();
        if (s_regions == null || city == null)
            return l;
        foreach (R r in s_regions)
        {
            bool kind = false;
            foreach (string p in prefixes)
                if (r.Name.StartsWith(p))
                    kind = true;
            if (!kind || City(r.X + r.W / 2, r.Y + r.H / 2) != city)
                continue;
            // The same description far apart is two shops (Jhelom's and Vesper's Adventurer's Needle and Spinning
            // Wheel).
            string text = r.Text.Length > 0 ? r.Text : r.Name;
            Shop? s = l.Find(t =>
                t.Text == text && r.X < t.X + t.W + ShopJoin && t.X < r.X + r.W + ShopJoin
                && r.Y < t.Y + t.H + ShopJoin && t.Y < r.Y + r.H + ShopJoin);
            if (s == null)
            {
                l.Add(new Shop { Text = text, X = r.X, Y = r.Y, W = r.W, H = r.H });
            }
            else
            {
                int x1 = Math.Max(s.X + s.W, r.X + r.W), y1 = Math.Max(s.Y + s.H, r.Y + r.H);
                s.X = Math.Min(s.X, r.X);
                s.Y = Math.Min(s.Y, r.Y);
                s.W = x1 - s.X;
                s.H = y1 - s.Y;
            }
        }
        return l;
    }
    // The shop's name for display: its title, and when another shop of its city has the same title, the city and where
    // it lies among them, north or south when they lie more apart that way, else east or west (Damian, 2026-10-22:
    // "both shops are the spinning wheel in vesper. spinning wheel vesper north/south works for me"). Never an
    // identity: that is the shop's rectangle (Key, Holds; AssistIdentity.md).
    // Cached per shop: AllShopsIn walks every region against every region, and status lines name a shop at every step.
    static Map<string, string> s_displays = Map<string, string>.Empty; static readonly Lock s_displaysGate = new();
    public static string Display(Shop s)
    {
        string? d;
        if (s_displays.TryGet(s.Key, out d))
            return d;
        d = DisplayOf(s);
        lock (s_displaysGate)
            s_displays = s_displays.Set(s.Key, d);
        return d;
    }
    static string DisplayOf(Shop s)
    {
        string? city = City(s.CX, s.CY);
        if (city == null)
            return s.Text;
        List<Shop> same = AllShopsIn(city).FindAll(t => t.Text == s.Text);
        if (same.Count < 2)
            return s.Text;
        double mx = 0, my = 0;
        foreach (Shop t in same)
        {
            mx += t.CX;
            my += t.CY;
        }
        mx /= same.Count;
        my /= same.Count;
        int spreadX = 0, spreadY = 0;
        foreach (Shop t in same)
        {
            spreadX = Math.Max(spreadX, Math.Abs(t.CX - same[0].CX));
            spreadY = Math.Max(spreadY, Math.Abs(t.CY - same[0].CY));
        }
        string dir = spreadY >= spreadX ? (s.CY < my ? "north" : "south") : (s.CX < mx ? "west" : "east");
        return s.Text + ", " + (s.Text.Contains(city) ? "" : city + " ") + dir;
    }
    // Every shop in the city, of any trade: the named regions inside it that are not the city, a bank, the open land or
    // the sea.
    public static List<Shop> AllShopsIn(string city)
    {
        List<string> kinds = new();
        if (s_regions == null || city == null)
            return new List<Shop>();
        foreach (R r in s_regions)
        {
            if (r.Text.Length == 0 || r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE")
                || r.Name.StartsWith("BRITANNIA") || r.Name.StartsWith("OCEAN") || r.Name.StartsWith("BANK")
                || r.Name.StartsWith("HOUSING"))
                continue;
            if (City(r.X + r.W / 2, r.Y + r.H / 2) != city)
                continue;
            string kind = r.Name.Split('_')[0] + "_";
            if (!kinds.Contains(kind))
                kinds.Add(kind);
        }
        return ShopsIn(city, kinds.ToArray());
    }
    // Every city the region file names.
    // Not a moongate, a named spot ("docks in Moonglow", lower-case), or Wind, where no trip goes.
    public static List<string> Cities()
    {
        List<string> l = new();
        if (s_regions == null)
            return l;
        foreach (R r in s_regions)
            if ((r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE")) && r.Text.Length > 0
                && char.IsUpper(r.Text[0]) && !r.Text.Contains("Moongate") && r.Text != "Wind" && !l.Contains(r.Text))
                l.Add(r.Text);
        l.Sort(StringComparer.Ordinal);
        return l;
    }
    // Every city holding a shop of the kind.
    public static List<string> CitiesWith(IReadOnlyList<string> prefixes)
    {
        List<string> l = new();
        if (s_regions == null)
            return l;
        foreach (R r in s_regions)
        {
            bool kind = false;
            foreach (string p in prefixes)
                if (r.Name.StartsWith(p))
                    kind = true;
            if (!kind)
                continue;
            string? c = City(r.X + r.W / 2, r.Y + r.H / 2);
            if (c != null && !l.Contains(c))
                l.Add(c);
        }
        return l;
    }
    public static int[]? BankIn(string city, int x, int y)
    {
        List<int[]> l = BanksIn(city, x, y);
        return l.Count > 0 ? l[0] : null;
    }
    // Every bank region's centre in the city, nearest x, y first: a bank that does not answer is followed by the next
    // (Damian, 2026-10-17: "when the banking at moonglow failed, it should have tried to find another bank").
    public static List<int[]> BanksIn(string city, int x, int y)
    {
        List<int[]> l = new();
        if (s_regions == null || city == null)
            return l;
        foreach (R r in s_regions)
        {
            if (!r.Name.StartsWith("BANK"))
                continue;
            int cx = r.X + r.W / 2, cy = r.Y + r.H / 2;
            if (City(cx, cy) == city)
                l.Add(new[] { cx, cy });
        }
        l.Sort((a, b) =>
            Math.Max(Math.Abs(a[0] - x),
            Math.Abs(a[1] - y)).CompareTo(Math.Max(Math.Abs(b[0] - x), Math.Abs(b[1] - y))));
        return l;
    }
    public static string? Where(int x, int y)
    {
        if (s_regions == null)
            return null;
        R? city;
        R? shop = CityAndShop(x, y, out city);
        string? s = shop == null ? null
            : city != null && shop.Text.EndsWith(" in " + city.Text)
            ? shop.Text.Substring(0, shop.Text.Length - 4 - city.Text.Length) : shop.Text;
        if (city != null && s != null)
            return city.Text + ", " + s;
        return city != null ? city.Text : s;
    }
    // The rectangles (x, y, w, h) of the bank whose region is centred at cx, cy: every region of its name and text, as
    // Britain's First Bank is two.
    public static List<int[]> BankRects(int cx, int cy)
    {
        List<int[]> l = new();
        if (s_regions == null)
            return l;
        R? hit = s_regions.Find(r => r.Name.StartsWith("BANK") && r.X + r.W / 2 == cx && r.Y + r.H / 2 == cy);
        if (hit == null)
            return l;
        foreach (R r in s_regions)
            if (r.Name == hit.Name && r.Text == hit.Text)
                l.Add(new[] { r.X, r.Y, r.W, r.H });
        return l;
    }
    // A building's sign for the map (Damian, 2026-10-19: "add the buiding sign icon to the center of the building,
    // scaled with the zoom, but the bank is 2x bigger than all the others"). The shop signs are server items, not map
    // statics (0 of STATICS0.MUL's 2738839 are 0x0BA3-0x0C0E; Damian: "its dynamic data"), so each is read from
    // signs-1998.txt beside the sources; a building region with no sign there takes the 1.25 TILEDATA sign for its
    // trade.
    static readonly ValueMap<string, int> s_signArt = ValueMap<string, int>.Of(
        ("BANK", 0x0C0B), ("INN", 0x0BB3), ("TAVERN", 0x0BC3), ("MAGE", 0x0BAD), ("MAGEGUILD", 0x0BE7),
            ("PROVISIONER", 0x0BCB),
        ("CLOTHIER", 0x0BA5), ("WEAVER", 0x0BA5), ("HEALER", 0x0BAB), ("BLACKSMITH", 0x0BC7),
        ("BUTCHER", 0x0BA9), ("BAKER", 0x0BA3),
        ("FIGHTERGUILD", 0x0C07), ("ARMORER", 0x0BBF), ("JEWELER", 0x0BC1), ("STABLES", 0x0BB7),
        ("WEAPONRY", 0x0BD9), ("SHIPWRIGHT", 0x0BB5),
        ("TINKERGUILD", 0x0C01), ("TINKER", 0x0BA7), ("BOWYER", 0x0BCD), ("ALCHEMIST", 0x0BC5),
        ("HERBALIST", 0x0BC5), ("BARDGUILD", 0x0BDB),
        ("BARD", 0x0BBB), ("CARPENTER", 0x0BAF), ("WOODWORKER", 0x0BAF), ("THEATER", 0x0C0D),
        ("MINERSGUILD", 0x0BED), ("FISHERGUILD", 0x0BF3),
        ("CUSTOMS", 0x0BB1), ("RANGERGUILD", 0x0BEF), ("MERCHANTGUILD", 0x0C09), ("THIEFGUILD", 0x0BFB),
        ("THIEVESGUILD", 0x0BFB), ("LIBRARY", 0x0B95));
    public sealed class Sign
    {
        public int X, Y, Art; public string Name = ""; public bool Bank => Art == 0x0C0B || Art == 0x0C0C;
    }
    static List<Sign>? s_signs;
    // A sign within SignPad tiles of a building's box belongs to it (it hangs outside the wall: Vesper's at 2896,673).
    const int SignPad = 3;
    sealed class Building
    {
        public string Key = null!; public int X, Y, W, H; public Sign Sign = null!;
    }
    // One sign a building, at the centre of the building: the regions of one trade and description within ShopJoin of
    // each other are one building, as Britain's First Bank is two regions. A sign from the file takes the building
    // whose box holds it, else the smallest shop region holding it, else it stands where it hangs.
    public static List<Sign> SignsInView(int x0, int y0, int x1, int y1)
    {
        List<Sign> l = new();
        if (s_regions == null)
            return l;
        if (s_signs == null)
        {
            List<Building> found = new();
            Func<R, Building> join = r =>
            {
                string key = r.Name.Split('_')[0] + "|" + (r.Text.Length > 0 ? r.Text : r.Name);
                Building? b = found.Find(t =>
                    t.Key == key && r.X < t.X + t.W + ShopJoin && t.X < r.X + r.W + ShopJoin
                    && r.Y < t.Y + t.H + ShopJoin && t.Y < r.Y + r.H + ShopJoin);
                if (b == null)
                {
                    b = new Building { Key = key, X = r.X, Y = r.Y, W = r.W, H = r.H };
                    found.Add(b);
                    return b;
                }
                int ex = Math.Max(b.X + b.W, r.X + r.W), ey = Math.Max(b.Y + b.H, r.Y + r.H);
                b.X = Math.Min(b.X, r.X);
                b.Y = Math.Min(b.Y, r.Y);
                b.W = ex - b.X;
                b.H = ey - b.Y;
                return b;
            };
            foreach (R r in s_regions)
            {
                int art;
                if (s_signArt.TryGet(r.Name.Split('_')[0], out art))
                {
                    Building b = join(r);
                    if (b.Sign == null)
                        b.Sign = new Sign { Art = art };
                }
            }
            List<Sign> all = new();
            foreach (Sign s in ReadSigns())
            {
                Building? b = found.Find(t =>
                    s.X >= t.X - SignPad && s.Y >= t.Y - SignPad && s.X < t.X + t.W + SignPad
                    && s.Y < t.Y + t.H + SignPad && !(t.Sign != null && t.Sign.Name.Length > 0));
                // Farther out, the nearest building of the same sign (either facing: art pairs 0BA3/0BA4 to 0C0D/0C0E)
                // within ShopJoin whose sign is still its trade's: Ocllo's bank sign hangs 8 tiles off its region.
                if (b == null)
                {
                    int nd = int.MaxValue;
                    foreach (Building t in found)
                    {
                        if (t.Sign == null || t.Sign.Name.Length > 0 || (t.Sign.Art + 1) >> 1 != (s.Art + 1) >> 1)
                            continue;
                        int d = Math.Max(Math.Max(t.X - s.X, s.X - (t.X + t.W - 1)),
                            Math.Max(t.Y - s.Y, s.Y - (t.Y + t.H - 1)));
                        if (d <= ShopJoin && d < nd)
                        {
                            nd = d;
                            b = t;
                        }
                    }
                }
                if (b == null)
                {
                    R? best = null;
                    long area = long.MaxValue;
                    foreach (R r in s_regions)
                    {
                        if (r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE") || r.Name.StartsWith("BRITANNIA")
                            || r.Name.StartsWith("OCEAN") || r.Name.StartsWith("NONE") || r.Name.StartsWith("HOUSING")
                            || r.Name.StartsWith("GUARD"))
                            continue;
                        if (s.X < r.X - SignPad || s.Y < r.Y - SignPad || s.X >= r.X + r.W + SignPad
                            || s.Y >= r.Y + r.H + SignPad || (long)r.W * r.H > 4096)
                            continue;
                        long a = (long)r.W * r.H;
                        if (a < area)
                        {
                            area = a;
                            best = r;
                        }
                    }
                    if (best != null)
                        b = join(best);
                }
                if (b == null)
                {
                    all.Add(s);
                    continue;
                }
                b.Sign = s;
            }
            foreach (Building b in found)
                if (b.Sign != null)
                {
                    b.Sign.X = b.X + b.W / 2;
                    b.Sign.Y = b.Y + b.H / 2;
                    all.Add(b.Sign);
                }
            s_signs = all;
        }
        foreach (Sign s in s_signs)
            if (s.X >= x0 && s.X <= x1 && s.Y >= y0 && s.Y <= y1)
                l.Add(s);
        return l;
    }
    static List<Sign> ReadSigns()
    {
        List<Sign> l = new();
        try
        {
            string p = Path.Combine(AssistRelay.SourceDir ?? "", "signs-1998.txt");
            if (!File.Exists(p))
                return l;
            foreach (string line in File.ReadAllLines(p))
            {
                if (line.StartsWith("#"))
                    continue;
                string[] t = line.Split(new[] { ',' }, 4);
                int art, x, y;
                if (t.Length < 3 || !int.TryParse(t[0], NumberStyles.HexNumber, null, out art)
                    || !int.TryParse(t[1], out x) || !int.TryParse(t[2], out y))
                    continue;
                l.Add(new Sign { Art = art, X = x, Y = y, Name = t.Length > 3 ? t[3] : "" });
            }
        }
        catch { }
        return l;
    }
    // The 1998 server's home test (human.m: getSmallestArea at the vendor's location, z included, its name's first word
    // against the vendor's myHomeShop): the smallest region holding x, y, z, as that first word ("MAGEGUILD"); null
    // with no region file or none holding it.
    public static string? AreaKey(int x, int y, int z)
    {
        if (s_regions == null)
            return null;
        R? best = null;
        long area = long.MaxValue;
        foreach (R r in s_regions)
        {
            if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || z < r.Z0 || z > r.Z1)
                continue;
            long a = (long)r.W * r.H;
            if (a < area)
            {
                area = a;
                best = r;
            }
        }
        return best == null ? null : best.Name.Split('_')[0];
    }
    // The nearest region whose name's first word is key: its centre and z range, or null.
    public static int[]? AreaNearest(string key, int x, int y)
    {
        if (s_regions == null || string.IsNullOrEmpty(key))
            return null;
        int[]? best = null;
        int bd = int.MaxValue;
        foreach (R r in s_regions)
        {
            if (r.Name.Split('_')[0] != key)
                continue;
            int cx = r.X + r.W / 2, cy = r.Y + r.H / 2, d = Math.Max(Math.Abs(cx - x), Math.Abs(cy - y));
            if (d < bd)
            {
                bd = d;
                best = new[] { cx, cy, r.Z0, r.Z1 };
            }
        }
        return best;
    }
    // The zone text and whether the map calls it guarded; null with no region file.
    public static string? Zone(int x, int y, int z, out bool guarded)
    {
        guarded = false;
        if (s_regions == null)
            return null;
        R? best = null;
        long area = long.MaxValue;
        foreach (R r in s_regions)
        {
            if (x < r.X || y < r.Y || x >= r.X + r.W || y >= r.Y + r.H || z < r.Z0 || z > r.Z1)
                continue;
            if (r.Name.StartsWith("CITY") || r.Name.StartsWith("JUSTICE"))
                guarded = true;
            long a = (long)r.W * r.H;
            if (r.Name.StartsWith("BRITANNIA"))
                a = long.MaxValue - 1;
            if (a < area)
            {
                area = a;
                best = r;
            }
        }
        if (best == null)
            return "outside every region";
        return best.Text.Length > 0 ? best.Text + " (" + best.Name + ")" : best.Name;
    }
}
