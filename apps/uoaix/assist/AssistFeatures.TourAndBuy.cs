#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // The tour (Damian, 2026-10-17: "we have to visit every tailor shop before making that call, all recently ... go to
    // every tailor shop, buy all the thread they have and sell 1 shirt to each (that will buy them)"): every city with
    // a tailor shop and a rune in the bank box, in the region file's order; in each, every shop walked into, its people
    // named by a click, and each shopkeeper in it asked once to buy one shirt at any price, then for all its thread.
    // The gold over twice the purse is banked before the next recall. Each shop's answer is a line of the report, kept
    // in reports\tour-<server>-<time>.txt; a keeper that gives no proper answer ends the tour (assist-plan.md rule 1).
    public volatile string TourReport = "";
    // TourBankOver: a tour banks only above this, keeping TourGold, so its deposit and its top-up never undo each
    // other.
    public const int TourGoldLow = 500, TourGold = 1000, TourBankOver = 2000;
    // The tour is a cycle (Damian, 2026-10-17: "the point is to keep following the cycle", "not starting from the top
    // every time"): the cities reached in the current lap are kept in tour-lap-<server>.txt, one a line, and a tour
    // goes on with the cities not yet reached; a lap with every city reached starts the next lap.
    string TourLapPath => ServerFile("tour-lap");
    string RunTour(GameLink k, ref int bought, ref int sold, ref int gold)
    {
        List<string> report = new();
        Set<uint> asked = Set<uint>.Empty;
        List<string> cycle = new();
        foreach (string c in TailorCities())
            if (!Banned(c) && BankHasRune(c))
                cycle.Add(c);
        List<string> lap = new();
        try
        {
            if (File.Exists(TourLapPath))
                lap.AddRange(File.ReadAllLines(TourLapPath));
        }
        catch { }
        List<string> cities = cycle.FindAll(c => !lap.Contains(c));
        if (cities.Count == 0)
        {
            lap.Clear();
            cities = cycle;
            Note("Tour: every city reached; a new lap");
        }
        else if (lap.Count > 0)
        {
            Note("Tour: this lap reached " + string.Join(", ", lap) + "; on to " + string.Join(", ", cities));
        }

        string? end = null;
        try
        {
            foreach (string city in cities)
            {
                if (circuitStop)
                {
                    end = "stopped";
                    break;
                }
                if (CityOf(k) != city)
                {
                    string? ready = TripReady(k);
                    if (ready != null)
                    {
                        end = ready;
                        break;
                    }
                    // Gold for every vendor's thread, from the bank box (Damian, 2026-10-17: "there is plenty of gold
                    // in the bank if you need it").
                    if (PackCount(k, g => g == GoldArt) < TourGoldLow)
                    {
                        int gx, gy;
                        lock (k.State.Gate)
                        {
                            gx = k.State.X;
                            gy = k.State.Y;
                        }
                        List<int[]> cb = CityOf(k) == null ? new List<int[]>() : CityBanks(CityOf(k)!, gx, gy);
                        if (cb.Count > 0)
                        {
                            string? gw = ToBanker(k, cb[0]);
                            string taken;
                            if (gw == null)
                                gw = BankTake(new List<int> { GoldArt }, TourGold, out taken);
                            if (gw != null)
                            {
                                end = "gold from the bank: " + gw;
                                break;
                            }
                        }
                    }
                    CircuitStatus = "Tour: travelling to " + city;
                    string? tw = Travel(k, city);
                    if (TripStops(tw!))
                    {
                        end = tw;
                        break;
                    }
                    if (tw != null)
                    {
                        report.Add(city + ": " + tw);
                        lap.Add(city);
                        try
                        {
                            File.WriteAllLines(TourLapPath, lap);
                        }
                        catch { }
                        continue;
                    }
                }
                lap.Add(city);
                try
                {
                    File.WriteAllLines(TourLapPath, lap);
                }
                catch { }
                int x0, y0;
                lock (k.State.Gate)
                {
                    x0 = k.State.X;
                    y0 = k.State.Y;
                }
                List<UoRegions.Shop> shops = TailorShopsIn(city);
                shops.Sort((a, b) => Tiles(a.CX, a.CY, x0, y0).CompareTo(Tiles(b.CX, b.CY, x0, y0)));
                foreach (UoRegions.Shop shop in shops)
                {
                    if (circuitStop)
                        break;
                    CircuitStatus = "Tour: " + UoRegions.Display(shop);
                    int px, py;
                    lock (k.State.Gate)
                    {
                        px = k.State.X;
                        py = k.State.Y;
                    }
                    string? w = shop.Holds(px, py, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
                    if (w != null)
                    {
                        report.Add(UoRegions.Display(shop) + ": not reached: " + w);
                        continue;
                    }
                    MobileLabels(VendorHear * 4);
                    List<AtlasNpc> keepers = ShopTailors(shop);
                    if (keepers.Count == 0)
                    {
                        report.Add(UoRegions.Display(shop) + ": no shopkeeper known by name");
                        continue;
                    }
                    foreach (AtlasNpc v in keepers)
                    {
                        if (circuitStop || asked.Contains(v.Serial))
                            continue;
                        asked = asked.Add(v.Serial);
                        string name = v.Name.Split(' ')[0];
                        int n2 = 0, g2 = 0, under, got = 0, left;
                        string? sw = "no shirt left";
                        if (PackCount(k, IsShirtArt) > 0)
                        {
                            CircuitStatus = "Tour: one shirt to " + name;
                            sw = VendorSell(IsShirtArt, 0, out n2, out g2, out under, name, 1);
                            sold += n2;
                            gold += g2;
                        }
                        if (Silent(sw!))
                        {
                            report.Add(UoRegions.Display(shop) + ", " + name + ": " + sw);
                            end = "asking " + name + ": " + sw;
                            break;
                        }
                        CircuitStatus = "Tour: thread from " + name;
                        string? bw = VendorBuy(IsThreadArt, 10000, out got, out left, name);
                        bought += got;
                        report.Add(UoRegions.Display(shop) + ", " + name + ": "
                            + (n2 > 0 ? "bought a shirt for " + g2 : "shirt: " + (sw ?? "not sold")) + "; thread: "
                            + (bw ?? "sold us " + got));
                        Note("Tour: " + report[report.Count - 1]);
                        if (Silent(bw!))
                        {
                            end = "asking " + name + ": " + bw;
                            break;
                        }
                    }
                    if (end != null)
                        break;
                }
                if (end != null)
                    break;
                if (PackCount(k, g => g == GoldArt) > TourBankOver)
                {
                    string? bw = BankHere(k, city, TourGold);
                    if (bw != null)
                    {
                        end = "banking in " + city + ": " + bw;
                        break;
                    }
                }
            }
        }
        finally
        {
            TourReport = string.Join("\r\n", report);
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "reports");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,
                    "tour-" + ServerSafe + "-" + Clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmm") + ".txt"),
                    TourReport);
            }
            catch { }
            m_tourMode = false;
        }
        return end ?? (circuitStop ? "stopped" : "tour done: " + report.Count + " shops and keepers");
    }
    // The buy circuit (Damian, 2026-10-17: "a new circuit called "Buy reagents" which takes a shopping list of mage
    // supply stuff (reagents, runes, scrolls) and goes to stores buying them. like a simpler version of the big tailor
    // circuit, this one just buys and stores in the pack in the bank"): the picked shops in their order, lap after lap
    // while a lap buys anything; in each shop every keeper named by a click is asked by name for each item still wanted
    // that its stock list (when read) names, at most the item's highest price; at the bank of every city left, and
    // where the circuit ends, the goods go into the bank (BuyBank). A keeper that gives no proper answer ends it
    // (assist-plan.md rule 1). The restock (Damian, 2026-10-17: "someday the reagent buy circuit will need to follow
    // the same "get more next respawn" logic"): a shop whose keeper was bought out of an item still wanted is not
    // visited again for RestockMinutes; when a lap buys nothing and a bought-out shop's restock is still to come, the
    // goods go into the bank here and the circuit waits for the soonest restock, then laps again; a lap after a wait
    // that buys nothing ends it.
    string RunBuy(GameLink k)
    {
        string kind = m_buyKind;
        List<BuyItem> list = BuyListFor(kind);
        ValueMap<string, int> left = ValueMap<string, int>.Empty;
        foreach (BuyItem i in list)
            left = left.Set(i.Name!, i.Count);
        List<UoRegions.Shop> shops = CircuitShops(kind);
        // No smith shop picked for the ingots: the town's here, nearest first.
        if (kind == "ingots" && shops.Count == 0)
        {
            string? c = CityOf(k);
            int qx, qy;
            lock (k.State.Gate)
            {
                qx = k.State.X;
                qy = k.State.Y;
            }
            if (c != null)
            {
                shops = UoRegions.ShopsIn(c, s_smithyShopKinds);
                shops.Sort((a, b) => Tiles(a.CX, a.CY, qx, qy).CompareTo(Tiles(b.CX, b.CY, qx, qy)));
            }
        }
        if (list.Count == 0)
        {
            m_buyMode = false;
            return "the shopping list is empty";
        }
        if (shops.Count == 0)
        {
            m_buyMode = false;
            return kind == "ingots" ? "no smith shop is picked, and none is in the town here"
                : "no shop is picked for the buy circuit";
        }
        string? end = null;
        int bought = 0;
        bool waited = false;
        ValueMap<string, DateTime> soldOut = ValueMap<string, DateTime>.Empty;
        try
        {
            // The purse first, from the bank of the city here.
            string? here0 = CityOf(k);
            if (here0 != null)
            {
                string? bw = BuyBank(k, here0, false, list);
                if (bw != null)
                    return "the bank in " + here0 + ": " + bw;
            }
            // The shop runes bags carried, as the tailoring circuit carries them (the only runes that leave the bank,
            // Damian 2026-10-18): their landings serve the walks between shops (2026-10-24 15:00:47, a 228-tile walk to
            // Moonglow's Finest Alchemy with "Moonglow Scholars Cut" landing 15 tiles from it left in the box).
            {
                string? cw = CarryShopRunes(k);
                if (cw != null)
                    return cw;
            }
            for (int lap = 0; end == null && !circuitStop; lap++)
            {
                int lapStart = bought;
                foreach (UoRegions.Shop shop in shops)
                {
                    if (circuitStop)
                    {
                        end = "stopped";
                        break;
                    }
                    if (!list.Exists(i => left.Get(i.Name!, 0) > 0))
                        break;
                    DateTime gone;
                    if (soldOut.TryGet(shop.Key, out gone)
                        && Clock.UtcNow.ToLocalTime() < gone.AddMinutes(RestockMinutes))
                        continue;
                    string? city = UoRegions.City(shop.CX, shop.CY);
                    if (!WorthTrip(shop, list, left))
                    {
                        Note("Buy: " + UoRegions.Display(shop)
                            + " passed over: no keeper known there sells what is still wanted");
                        continue;
                    }
                    if (city != null && CityOf(k) != city)
                    {
                        string? ready = TripReady(k);
                        if (ready != null)
                        {
                            end = ready;
                            break;
                        }
                        string? here = CityOf(k);
                        if (here != null
                            && BankBeforeTrip(PackCount(k, g => g == GoldArt), WantedCost(list, left), BuyPurse,
                            WeightNow(k), CarryLimit(k)))
                        {
                            string? bw = BuyBank(k, here, false, list);
                            if (bw != null)
                            {
                                end = "the bank in " + here + ": " + bw;
                                break;
                            }
                        }
                        CircuitStatus = "Buy: travelling to " + city;
                        string? tw = null;
                        // A pack rune for the shop or the city first, from where the player stands; the bank's after.
                        uint carried = CarriedShopRune(k, city, shop);
                        if (carried != 0)
                        {
                            tw = Recall(carried);
                            if (tw != null || CityOf(k) != city)
                                Note("Buy: the pack's rune for " + city + ": "
                                    + (tw ?? "landed in " + (CityOf(k) ?? "no city")) + "; by the bank");
                        }
                        if (carried == 0 || tw != null || CityOf(k) != city)
                            tw = BankRecall(k, city, false);
                        if (tw != null)
                        {
                            end = "travelling to " + city + ": " + tw;
                            break;
                        }
                        if (CityOf(k) != city)
                        {
                            end = "the rune for " + city + " landed in " + (CityOf(k) ?? "no city");
                            break;
                        }
                    }
                    bool emptied;
                    end = BuyInShop(k, shop, list, ref left, ref bought, out emptied);
                    if (emptied)
                        soldOut = soldOut.Set(shop.Key, Clock.UtcNow.ToLocalTime());
                    if (end != null)
                        break;
                }
                if (end != null || circuitStop)
                    break;
                if (!list.Exists(i => left.Get(i.Name!, 0) > 0))
                {
                    end = "every item bought";
                    break;
                }
                if (bought > lapStart)
                {
                    waited = false;
                    continue;
                }
                DateTime next = DateTime.MaxValue;
                string? at = null;
                foreach (KeyValuePair<string, DateTime> kv in soldOut)
                {
                    DateTime due = kv.Value.AddMinutes(RestockMinutes);
                    if (due > Clock.UtcNow.ToLocalTime() && due < next)
                    {
                        next = due;
                        at = kv.Key;
                    }
                }
                if (waited)
                {
                    end = "the restock brought nothing more";
                    break;
                }
                if (at == null)
                {
                    end = "a lap of every picked shop bought nothing more";
                    break;
                }
                string? hereW = CityOf(k);
                if (hereW != null)
                {
                    string? bw = BuyBank(k, hereW, false, list);
                    if (bw != null)
                    {
                        end = "the bank in " + hereW + " before the restock: " + bw;
                        break;
                    }
                }
                UoRegions.Shop? waitShop = shops.Find(s => s.Key == at);
                CircuitStatus = "Buy: waiting for the restock at " + (waitShop != null ? waitShop.Text : at) + " ("
                    + next.ToString("HH:mm") + ")";
                Note(CircuitStatus);
                while (Clock.UtcNow.ToLocalTime() < next && !circuitStop && !k.Closed)
                    Clock.Sleep(RestockPollMs);
                waited = true;
            }
            // The goods into the bank where the circuit ends.
            string? last = CityOf(k);
            if (last != null && !circuitStop)
            {
                string? bw = BuyBank(k, last, true, list);
                if (bw != null)
                    end += "; the bank in " + last + ": " + bw;
            }
        }
        finally
        {
            m_buyMode = false;
            List<string> still = new();
            foreach (BuyItem i in list)
                if (left.Get(i.Name!, 0) > 0)
                    still.Add(left.Get(i.Name!, 0) + " " + i.Name);
            Note("Buy: bought " + bought + (still.Count > 0 ? "; still wanted " + string.Join(", ", still) : ""));
        }
        return end ?? (circuitStop ? "stopped" : "done");
    }
    // One shop of a buy: walked into, its people named by a click, and each keeper asked by name for each item still
    // wanted that its stock list (when read) names, at most the item's highest price. Null, or why the buying ends (a
    // keeper that gives no proper answer, assist-plan.md rule 1).
    string? BuyInShop(GameLink k, UoRegions.Shop shop, List<BuyItem> list, ref ValueMap<string, int> left,
        ref int bought)
    {
        bool emptied;
        return BuyInShop(k, shop, list, ref left, ref bought, out emptied);
    }
    // Whether the keeper's stock list, when read, names the item: an unread list names everything, and so does one read
    // longer ago than a restock period, which may have restocked since. The one rule both the trip (WorthTrip) and the
    // asking (BuyInShop) go by (Damian, 2026-10-24: "why did we travel to vesper and not query the vendors"; 15:21:54,
    // the trip judged Vesper's lists stale and went, the shop trusted the same lists and asked nobody).
    bool ListNames(AtlasNpc v, BuyItem item)
    {
        if (v.Items.Count == 0 || v.ItemsAt < Clock.UtcNow.ToLocalTime().AddSeconds(-KeepRules.RestockPeriodSeconds))
            return true;
        string label = UoTiles.Name(item.Arts[0]);
        return v.Items.Exists(s => s.StartsWith(item.Name!, StringComparison.OrdinalIgnoreCase)
            || s.StartsWith(label, StringComparison.OrdinalIgnoreCase));
    }
    // Whether a bank stop is owed before a trip: the purse short of what is still wanted at its highest prices (up to
    // the buy purse), or the pack past nine tenths of the carry limit. Else none (Damian, 2026-10-24: "marching to the
    // bank, where we just were in a different town"; "figure out why you are going somewhere before you go").
    public static bool BankBeforeTrip(int gold, int wantedCost, int buyPurse, int weight, int carryLimit)
    {
        if (gold < Math.Min(wantedCost, buyPurse))
            return true;
        return carryLimit > 0 && weight >= carryLimit * 9 / 10;
    }
    int WantedCost(List<BuyItem> list, ValueMap<string, int> left)
    {
        long c = 0;
        foreach (BuyItem i in list)
            if (left.Get(i.Name!, 0) > 0)
                c += (long)left.Get(i.Name!, 0) * Math.Max(1, i.MaxPrice);
        return (int)Math.Min(int.MaxValue, c);
    }
    // Whether the shop is worth the trip: a keeper the atlas knows there whose list names an item still wanted, or no
    // keeper known there at all. The same rule BuyInShop asks by, applied before the recall (Damian, 2026-10-24:
    // "before "recalling" decide if it is neccessary here"; 14:55:33, a recall to Vesper's The Circles of Magic whose
    // three keepers' lists named none of the reagents, nobody asked).
    internal bool WorthTrip(UoRegions.Shop shop, List<BuyItem> list, ValueMap<string, int> left)
    {
        if (Atlas == null)
            return true;
        List<AtlasNpc> keepers = Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2));
        if (keepers.Count == 0)
            return true;
        foreach (AtlasNpc v in keepers)
            foreach (BuyItem item in list)
                if (left.Get(item.Name!, 0) > 0 && ListNames(v, item))
                    return true;
        return false;
    }
    // emptied: a keeper here was bought out of an item still wanted.
    string? BuyInShop(GameLink k, UoRegions.Shop shop, List<BuyItem> list, ref ValueMap<string, int> left,
        ref int bought, out bool emptied)
    {
        emptied = false;
        CircuitStatus = "Buy: " + UoRegions.Display(shop);
        // No keeper asked with an empty pack: the answer is known (L-FUTILITY), and the circuit stops saying so.
        if (PackCount(k, g => g == GoldArt) == 0)
            return "no gold in the pack to buy with at " + UoRegions.Display(shop);
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        string? w = shop.Holds(px, py, 0) ? null : WalkTo(shop.CX, shop.CY, 2);
        if (w != null)
        {
            Note("Buy: " + UoRegions.Display(shop) + ": not reached: " + w);
            return null;
        }
        MobileLabels(VendorHear * 4);
        List<AtlasNpc> keepers = Atlas == null ? new List<AtlasNpc>()
            : Atlas.Shops().FindAll(n => n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2)
            && (Clock.UtcNow.ToLocalTime() - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs);
        if (keepers.Count == 0)
        {
            Note("Buy: no shopkeeper in " + UoRegions.Display(shop) + " is known by name; not asked");
            return null;
        }
        // A keeper the walk did not reach is asked once more after the shop's others, the way having changed meanwhile:
        // Abel, the provisioner with the books, was behind a door someone held, and the walk to the next keeper opened
        // it (Minoc, 2026-10-18 22:15:12); the walk's failure was no answer of his.
        List<AtlasNpc> queue = new(keepers);
        Set<uint> again = Set<uint>.Empty;
        for (int qi = 0; qi < queue.Count; qi++)
        {
            AtlasNpc v = queue[qi];
            string name = v.Name.Split(' ')[0];
            bool unreached = false;
            foreach (BuyItem item in list)
            {
                int want = left.Get(item.Name!, 0);
                if (want <= 0 || circuitStop)
                    continue;
                // A keeper whose stock list was read and names none of it is not asked.
                if (!ListNames(v, item))
                    continue;
                int[] arts = item.Arts;
                int got, stock;
                // The keeper's window just read holds none of it (L-FUTILITY; 2026-10-24 14:32:17, Jarrod asked "buy"
                // for black pearl, bloodmoss and mandrake root in turn, "the vendor has none" twice).
                if (ShownWithout(v.Serial, g => Array.IndexOf(arts, g) >= 0))
                {
                    Note("Buy: " + UoRegions.Display(shop) + ", " + name + ", " + item.Name
                        + ": none in the window just read");
                    continue;
                }
                CircuitStatus = "Buy: " + item.Name + " from " + name;
                string? bw = VendorBuy(g => Array.IndexOf(arts, g) >= 0, want, out got, out stock, name, item.MaxPrice);
                left = left.Set(item.Name!, want - got);
                bought += got;
                if (bw == null && got > 0 && stock == 0 && want - got > 0)
                    emptied = true;
                Note("Buy: " + UoRegions.Display(shop) + ", " + name + ", " + item.Name + ": "
                    + (bw ?? "bought " + got + ", " + (want - got) + " still wanted"));
                if (Silent(bw!))
                    return "asking " + name + ": " + bw;
                if (bw != null && bw.StartsWith("walking to"))
                {
                    unreached = true;
                    break;
                }
            }
            if (unreached && !again.Contains(v.Serial))
            {
                again = again.Add(v.Serial);
                queue.Add(v);
                Note("Buy: " + name + " not reached; asked again after the others in " + UoRegions.Display(shop));
            }
        }
        // Every keeper here the list named nothing of, read before leaving (the shop surveyed).
        if (!circuitStop)
            SurveyHere(k);
        return null;
    }
}
