#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // Each circuit's shops, picked by the shop's name and place (Damian, 2026-10-17: "for each, let me pick the list of
    // shops to visit in case we need to adjust on the fly", "not vendor names, but shops"): circuit.shops.<circuit>,
    // shop keys (UoRegions.Shop.Key) separated by ";", in visiting order. Unset or empty for the tailor circuit is
    // every tailor shop; the buy circuit's default is Vesper's mage shop by the bank and Britain's big one in the north
    // (Damian: "the best mage shops are the ones in vesper by the bank and the big one in brit up north").
    public static ImmutableArray<string> ShopKindsOf(string circuit)
    {
        return circuit == "buy" ? s_mageShopKinds
            : circuit == "refresh" ? s_foodShopKinds : circuit == "ingots" ? s_smithyShopKinds : s_tailorShopKinds;
    }
    // The refresh's food shops, in Damian's order (2026-10-17: "use provisioner for apples or pears or a farmers
    // market, or a stable if necessary").
    static readonly ImmutableArray<string> s_foodShopKinds = [ "PROVISIONER", "FARM", "MARKET", "STABLES" ];
    public static ImmutableArray<string> FoodShopKinds => s_foodShopKinds;
    static readonly ImmutableArray<string> s_buyShopDefault = [ "The Circles of Magic in Vesper",
        "The Sorcerer's Delight in Britain" ];
    public static List<UoRegions.Shop> AllShops(string circuit)
    {
        ImmutableArray<string> kinds = ShopKindsOf(circuit);
        List<UoRegions.Shop> l = new();
        foreach (string c in UoRegions.CitiesWith(kinds))
            if (!Banned(c))
                l.AddRange(UoRegions.ShopsIn(c, kinds));
        return l;
    }
    public List<string> CircuitShopKeys(string circuit)
    {
        string t = Cfg.Get("circuit.shops." + circuit, null!);
        List<string> l = new();
        // A default names a title, so every shop of that title is in it: a shop is its rectangle, and two of one title
        // are two shops (AssistIdentity.md stage 2).
        if (t == null && circuit == "buy")
        {
            foreach (string name in s_buyShopDefault)
                foreach (UoRegions.Shop s in AllShops("buy").FindAll(x => x.Text == name))
                    l.Add(s.Key);
            return l;
        }
        if (t != null)
            foreach (string e in t.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                l.Add(e.Trim());
        return l;
    }
    public void SetCircuitShopKeys(string circuit, List<string> keys)
    {
        Cfg.Set("circuit.shops." + circuit, string.Join(";", keys));
    }
    // The circuit's shops in visiting order; for the tailor circuit with none picked, every tailor shop.
    public List<UoRegions.Shop> CircuitShops(string circuit)
    {
        List<UoRegions.Shop> all = AllShops(circuit);
        List<string> keys = CircuitShopKeys(circuit);
        if (keys.Count == 0)
            return circuit == "buy" || circuit == "ingots" ? new List<UoRegions.Shop>() : all;
        List<UoRegions.Shop> l = new();
        foreach (string key in keys)
        {
            UoRegions.Shop? s = all.Find(x => x.Key == key);
            if (s != null)
                l.Add(s);
        }
        return l;
    }
    List<UoRegions.Shop> TailorShopsIn(string city)
    {
        return ApprovedShops().FindAll(s => UoRegions.City(s.CX, s.CY) == city);
    }
    List<string> TailorCities()
    {
        return UoRegions.CitiesWith(s_tailorShopKinds).FindAll(c => TailorShopsIn(c).Count > 0);
    }
    // A vendor the tailor circuit may deal with: any, with no shops picked; else one inside a picked shop.
    bool TailorVendor(AtlasNpc n)
    {
        foreach (UoRegions.Shop s in ApprovedShops())
            if (s.Holds(n.X, n.Y, 2))
                return true;
        return false;
    }
    // The player's approved tailor shops (Damian, 2026-10-20: "lets just use the runes in the bag as the list of player
    // approved shops"): every tailor shop holding a rune of the tailor runes bag, by the rune's recorded landing, else
    // by its label naming the shop; rebuilt at most every ApprovedMs (TailorVendor asks for each vendor). With none
    // found (no bag, or its runes not yet seen), every tailor shop.
    public const int ApprovedMs = 30000;
    List<UoRegions.Shop> m_approved = null!; DateTime m_approvedAt; volatile bool m_approvedByBag;
    List<UoRegions.Shop> ApprovedShops(bool fresh = false)
    {
        List<UoRegions.Shop> l = m_approved;
        if (!fresh && l != null && (Clock.UtcNow - m_approvedAt).TotalMilliseconds < ApprovedMs)
            return l;
        List<UoRegions.Shop> all = AllShops("tailor");
        GameLink k = m_link;
        List<uint> bags = BankBags("shoprunes");
        l = new List<UoRegions.Shop>();
        if (k != null)
        {
            LoadRuneLandings();
            LoadBankRunes();
            List<uint> runes = new();
            lock (k.State.Gate)
                foreach (AssistItem it in k.State.Items.Values)
                    if (bags.Contains(it.Container) && it.Graphic >= 0x1F14 && it.Graphic <= 0x1F17)
                        runes.Add(it.Serial);
            foreach (uint r in runes)
            {
                int[]? land;
                if (!m_runeLandings.TryGet(r, out land))
                    land = null;
                UoRegions.Shop? s = land == null ? null : all.Find(x => x.Holds(land[0], land[1], 8));
                if (s == null)
                {
                    string? label;
                    m_bankRunes.TryGet(r, out label);
                    string key = PlaceKey(label!);
                    if (key.Length >= 3)
                        s = all.Find(x => { string sk = PlaceKey(x.Text); return key.Contains(sk)
                            || sk.Contains(key); });
                }
                if (s != null && !l.Contains(s))
                    l.Add(s);
            }
        }
        m_approvedByBag = l.Count > 0;
        if (l.Count == 0)
            l = all;
        m_approved = l;
        m_approvedAt = Clock.UtcNow;
        return l;
    }
    // The approved shops in TailorOrder, then any the bag holds a rune for that the order does not name; with no bag
    // runes seen, TailorOrder alone.
    List<UoRegions.Shop> OrderShops()
    {
        List<UoRegions.Shop> ok = ApprovedShops(true);
        List<UoRegions.Shop> l = new();
        foreach (string key in s_tailorOrder)
        {
            UoRegions.Shop? s = ok.Find(x => x.Key == key);
            if (s != null)
                l.Add(s);
        }
        if (m_approvedByBag)
            foreach (UoRegions.Shop s in ok)
                if (!l.Contains(s))
                    l.Add(s);
        return l;
    }
    // A shop's avoidance, by its description, kept in shops-<server>.txt (description, tab, ticks it may be asked
    // again).
    ValueMap<string, DateTime> m_shopAvoid = ValueMap<string, DateTime>.Empty; bool m_shopAvoidLoaded;
    readonly Lock m_shopAvoidGate = new();
    string ShopAvoidPath => ServerFile("shops");
    bool ShopOpen(UoRegions.Shop s)
    {
        if (Broken("shop " + s.Key))
            return false;
        lock (m_shopAvoidGate)
        {
            LoadOnce(ref m_shopAvoidLoaded, ShopAvoidPath,
                line => { string[] t = line.Split('\t'); if (t.Length
                == 2) m_shopAvoid = m_shopAvoid.Set(t[0], new DateTime(long.Parse(t[1]))); });
            DateTime until;
            return !m_shopAvoid.TryGet(s.Key, out until) || Clock.UtcNow.ToLocalTime() >= until;
        }
    }
    void AvoidShop(UoRegions.Shop s, TimeSpan span)
    {
        ShopOpen(s);
        lock (m_shopAvoidGate)
        {
            DateTime until = Clock.UtcNow.ToLocalTime() + span;
            DateTime had;
            if (m_shopAvoid.TryGet(s.Key, out had) && had > until)
                until = had;
            m_shopAvoid = m_shopAvoid.Set(s.Key, until);
            List<string> lines = new();
            foreach (KeyValuePair<string, DateTime> kv in m_shopAvoid)
                lines.Add(kv.Key + "\t" + kv.Value.Ticks);
            try
            {
                File.WriteAllLines(ShopAvoidPath, lines);
            }
            catch { }
        }
    }
    // A city's sell score: its best open shop's hours since a sale there (a shop never sold to, the cap), by its
    // weight.
    int ShopScore(string city)
    {
        double rest = 0;
        List<AtlasNpc> tailors = CityBuyers(city);
        foreach (UoRegions.Shop s in TailorShopsIn(city))
        {
            if (!ShopOpen(s))
                continue;
            List<AtlasNpc> known = tailors.FindAll(n => s.Holds(n.X, n.Y, 2));
            if (known.Count == 0)
            {
                rest = Math.Max(rest, RestCapHours);
                continue;
            }
            foreach (AtlasNpc n in known)
                if (PaysFloor(n))
                    rest = Math.Max(rest, RestHours(n));
        }
        return rest <= 0 ? 0 : (int)((1 + rest * 10) * CityWeight(city));
    }
    string? SellTrip(GameLink k, out int sold, out int gold, out int thread)
    {
        sold = 0;
        gold = 0;
        thread = 0;
        m_tripHops = 0;
        TopClosedCheck();
        // Best first: the tailor longest since a sale (Damian, 2026-10-17: "other vendors we haven't seen in hours that
        // likely have available buy power"), by the city's weight, thread there a plus.
        // By the region file's tailor shops, not only the tailors the atlas has met: a shop never entered counts as
        // rested RestCapHours (Damian, 2026-10-17: "there are still half a dozen other tailor shops"; Trinsic's,
        // Vesper's second and Jhelom's second were never visited).
        List<string> cities = new();
        string? here = CityOf(k);
        // Only cities the bank box has a rune for (travel is from the bank's runes): Delucia and Papua have tailor
        // shops and no rune.
        foreach (string c in TailorCities())
            if (c != here && !Banned(c) && BankHasRune(c) && CityWeight(c) > 0 && ShopScore(c) > 0)
                cities.Add(c);
        // Magincia first, then Moonglow (Damian, 2026-10-17: "next time, try magincia first"; Brian in Moonglow took 90
        // at 27), then by score.
        // The best expected price first (CityPrice; recall travel takes seconds, so the price outranks the distance).
        ValueMap<string, int> cityPrice = ValueMap<string, int>.Empty;
        foreach (string c in cities)
            cityPrice = cityPrice.Set(c, CityPrice(c));
        cities.Sort((a, b) => { int p = cityPrice.Get(b, 0).CompareTo(cityPrice.Get(a, 0)); if (p
            != 0) return p; int fa = s_sellFirst.IndexOf(a), fb = s_sellFirst.IndexOf(b); if (fa
            < 0) fa = s_sellFirst.Length; if (fb < 0) fb = s_sellFirst.Length; return fa != fb ? fa.CompareTo(fb)
            : ShopScore(b).CompareTo(ShopScore(a)); });
        // After a survey, straight to the city of the best fresh offer (SurveyDone): the shirts go to the best price
        // seen, not to whichever city the survey ended in.
        if (Surveyed)
        {
            ValueMap<string, int> cityOffer = ValueMap<string, int>.Empty;
            foreach (string c in cities)
            {
                int best = -1;
                foreach (AtlasNpc n in CityBuyers(c))
                    best = Math.Max(best, FreshOffer(n.Serial));
                cityOffer = cityOffer.Set(c, best);
            }
            List<string> byOffer = new(cities);
            byOffer.Sort((a, b) => { int c = cityOffer.Get(b, 0).CompareTo(cityOffer.Get(a, 0)); return c != 0 ? c
                : cities.IndexOf(a).CompareTo(cities.IndexOf(b)); });
            cities = byOffer;
        }
        // A city whose every buyer offered under the floor within SurveyFreshMinutes is not travelled to (D7 of the
        // 2026-10-19 travel review: Tate, Nana and Kimmie, sold to by hand 10 to 100 minutes before, were each visited
        // for 23).
        SellItem? shirtItem = SellList().Find(i => Array.Exists(i.Arts, IsShirtArt));
        cities.RemoveAll(c => { List<AtlasNpc> buyers = CityBuyers(c); if (buyers.Count == 0
            || shirtItem
            == null) return false; foreach (AtlasNpc n in buyers) { int o = FreshOffer(n.Serial); if (o < 0
            || o >= VendorFloor(n.Serial, shirtItem.Arts[0])) return false; } return true; });
        if (cities.Count == 0)
            return NoSellCity + " that has not offered under " + FloorText();
        string? ready = TripReady(k);
        if (ready != null)
            return ready;
        foreach (string city in cities)
        {
            if (circuitStop || PackCount(k, IsShirtArt) < SellBatch)
                break;
            string? w = Travel(k, city);
            if (TripStops(w!))
                return w;
            if (w != null)
            {
                Note("Circuit: " + w);
                continue;
            }
            m_tripHops++;
            int x0, y0;
            lock (k.State.Gate)
            {
                x0 = k.State.X;
                y0 = k.State.Y;
            }
            // The shop of the best paying tailor first (PaidOver), then nearest.
            List<UoRegions.Shop> shops = TailorShopsIn(city);
            List<AtlasNpc> buyers = CityBuyers(city);
            ValueMap<string, int> shopBest = ValueMap<string, int>.Empty;
            foreach (UoRegions.Shop s in shops)
            {
                int best = int.MinValue;
                foreach (AtlasNpc n in buyers)
                    if (s.Holds(n.X, n.Y, 2))
                        best = Math.Max(best, PaidOver(n));
                shopBest = shopBest.Set(s.Key, best);
            }
            // Distance first, in ShopBand-tile bands, the best price only between shops of one band (D4 of the
            // 2026-10-19 travel review: price first walked Kwik 22 s to Kimmie and recalled back for Ella beside the
            // landing).
            shops.Sort((a, b) => { int c = (Tiles(a.CX, a.CY, x0, y0) / ShopBand).CompareTo(Tiles(b.CX, b.CY, x0, y0)
                / ShopBand); if (c != 0) return c; c = shopBest.Get(b.Key, 0).CompareTo(shopBest.Get(a.Key, 0));
                return c != 0 ? c
                : Tiles(a.CX, a.CY, x0, y0).CompareTo(Tiles(b.CX, b.CY, x0, y0)); });
            string? silent = null;
            foreach (UoRegions.Shop shop in shops)
            {
                if (circuitStop || PackCount(k, IsShirtArt) == 0 || !ShopOpen(shop))
                    continue;
                // A tailor the atlas knows in the shop is asked by name; a shop with none known is walked into, the
                // people in view are named by a click, and its shopkeeper (a worn shop pack) is asked by name. Never
                // "vendor sell": every vendor in earshot answers it (Damian, 2026-10-17: "use the vendors name
                // proper").
                List<AtlasNpc> known = CityBuyers(city).FindAll(n => shop.Holds(n.X, n.Y, 2));
                if (known.Count > 0 && !known.Exists(PaysFloor))
                    continue;
                // Every tailor in the shop that pays the floor, each by name (Damian sold to every vendor in Morena's
                // shop and in Tate's, 2026-10-17; asking the first alone left the rest of the shop's buying unused).
                List<AtlasNpc> tailors = known.FindAll(PaysFloor);
                BestPayingFirst(tailors, x0, y0);
                if (tailors.Count == 0)
                {
                    CircuitStatus = "Walking into " + UoRegions.Display(shop);
                    w = WalkTo(shop.CX, shop.CY, 2);
                    if (w != null)
                    {
                        Note("Circuit: walking into " + UoRegions.Display(shop) + ": " + w);
                        AvoidShop(shop, FailSpan("shop " + shop.Key, "walking in: " + w));
                        continue;
                    }
                    FailOk("shop " + shop.Key);
                    MobileLabels(VendorHear * 4);
                    AtlasNpc? keeper = ShopTailors(shop).Find(n => true);
                    if (keeper == null)
                    {
                        Note("Circuit: no shopkeeper in " + UoRegions.Display(shop) + " is known by name; not asked");
                        AvoidShop(shop, FailSpan("shop " + shop.Key, "no shopkeeper known by name"));
                        continue;
                    }
                    tailors.Add(keeper);
                }
                int shopUnder = 0;
                foreach (AtlasNpc t in tailors)
                {
                    if (circuitStop || PackCount(k, IsShirtArt) == 0)
                        break;
                    string name = t.Name.Split(' ')[0];
                    CircuitStatus = "Selling shirts to " + name + " in " + UoRegions.Display(shop);
                    int n2, g2, under;
                    uint ts = t.Serial;
                    w = VendorSell(IsShirtArt, g => VendorFloor(ts, g), out n2, out g2, out under, name);
                    if (w != null)
                        Note("Circuit: " + UoRegions.Display(shop) + ", " + name + ": " + w);
                    sold += n2;
                    gold += g2;
                    if (Silent(w!))
                    {
                        silent = "selling in " + UoRegions.Display(shop) + ": " + w;
                        break;
                    }
                    shopUnder = Math.Max(shopUnder, under);
                    // A full tailor is passed over until the cycle ends, as one under the floor is: no pause without a
                    // reset condition (Damian, 2026-10-20).
                    if (under > 0 || PackCount(k, IsShirtArt) > 0)
                        UnderFloorOffered(t.Serial);
                    // Thread from this tailor while here, as the sale in town does (2026-10-19: Slade's 55 thread were
                    // bought on a walk back to his shop a minute after his offer).
                    bool answered = w == null || under > 0 || PackCount(k, IsShirtArt) == 0;
                    if (!circuitStop && answered && ThreadDue(t) && PackCount(k, IsThreadArt) < ThreadWant)
                    {
                        int got, left;
                        string? tw = VendorBuy(IsThreadArt, ThreadWant * 10, out got, out left, name);
                        if (tw == null)
                        {
                            thread += got;
                            FailOk("buy vendor " + t.Serial.ToString("X"));
                        }
                        else
                        {
                            Note("Circuit: " + name + ": " + tw);
                            SkipVendor(t.Serial, tw);
                        }
                    }
                }
                if (silent != null)
                    break;
                if (shopUnder > 0 || PackCount(k, IsShirtArt) > 0)
                    ShopUnderFloor(shop);
            }
            if (silent != null)
                return silent;
            // Thread wherever shirts were offered, sold or not (Damian, 2026-10-17: "every single time you go to sell
            // shirts, you should also look to buy threads afterwards"; "flying to magincia and not even looking"),
            // bought while still in the shop, before the walk to the bank ("it is mindblowingly stupid to run back to
            // the bank to deposit gold, only to run back to tailorshop to buy 10 thread when you started in the tailor
            // shop"), sell-only too ("we were doing a lot of running around during sell down where we could also have
            // just bought the thread", 2026-10-18).
            if (!circuitStop && CityOf(k) == city)
            {
                int got;
                string? bw = BuyCityThread(k, city, out got);
                thread += got;
                if (bw != null)
                    return bw;
            }
            // Then the gold banked before the city is left (Damian, 2026-10-17, of 1863 gold carried home unbanked from
            // Moonglow: "that was critical at that moment"): every bank of the city.
            // Only above BankOver, and a bank that fails is passed over, the gold carried: banking after every sale
            // ended 3 of 7 circuits on 2026-10-17, while Damian banked once at 2042 gold and kept selling.
            if (sold > 0 && PackCount(k, g => g == GoldArt) > BankOver)
            {
                w = BankHere(k, city);
                if (w != null)
                    Note("Circuit: banking in " + city + ": " + w + "; the gold carried");
            }
            // On to the next city while shirts are left: in sell-only, until none are; else once some sold here.
            if (m_sellOnly ? PackCount(k, IsShirtArt) == 0 : sold > 0)
                break;
        }
        return null;
    }
    public const string NoSellCity = "no other city has a tailor shop";
}
