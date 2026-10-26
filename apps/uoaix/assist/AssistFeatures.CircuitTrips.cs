#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the circuit's trips (tailoring-circuit.md) ----------
    // Travel is by Recall from the bank box's runes (BankRecall). A city is the region file's (UoRegions.City); a
    // tailor that offered under the floor is passed over until the survey cycle ends (UnderFloorOffered).
    public const int TripReagents = 4, ReagentRefill = 30;
    // Before a trip: TripReagents of each recall reagent, else ReagentRefill from the bank of the city here;
    // null, or why the trip cannot go (2026-10-17: out of mandrake root, the trip tried 8 cities in a row).
    // The purse from the bank box now open to what it keeps. Null, or with must why the circuit stops: the pack still
    // holding no gold (Damian, 2026-10-24: "the circuit should have reported it can't find the gold and stop"; the buy
    // circuit asked Vesper's three reagent keepers nine times with 0 gold, 05:45:17). A short bank is noted, not a
    // reason.
    string? PurseFromBank(bool must)
    {
        string gt;
        string? gw = PurseTo(PurseKeep, out gt);
        if (gw != null || gt.Length > 0)
            Note("Circuit: the purse from the bank: " + (gw ?? gt));
        GameLink k = m_link;
        return must && k != null && PackCount(k, g => g == GoldArt) == 0
            ? (gw != null ? "no gold in the pack, and the bank: " + gw : BankGoldNone) : null;
    }
    public const string BankGoldNone = "no gold in the bank box's top-level piles (gold in its bags is savings)";
    // purse false: the tailoring circuit by shops, whose purse comes from the bank only with no kits and nothing to sew
    // (BankIfDue; Damian, 2026-10-20: "then why we walking to the bank", 29 gold fetched in Moonglow at 371 of 400).
    public const string ReagentsOut = "out of recall reagents";
    DateTime m_tripRegsAt = DateTime.MinValue;
    string? TripReady(GameLink k, bool purse = true)
    {
        OpenPackBags(k);
        List<int> shortOf = RecallRegsUnder(k, TripReagents);
        // A purse under what it keeps is a reason to stop at the bank by itself: with the reagents full this returned
        // first, and the circuit toured Britain, Magincia and Jhelom with 1 gold buying no thread (2026-10-19 21:47).
        bool purseShort = purse && PackCount(k, g => g == GoldArt) < PurseKeep;
        // The reagent fetch at most once a RegsBankAgainMin (the third 2026-10-24 audit: with the bank out of them
        // every trip asked it again, and the failures marked every shop BROKEN): between fetches a trip goes on with
        // two of each, and short of that the circuit goes back to the top (ReagentsOut), never a shop's failure.
        if (shortOf.Count > 0 && Clock.UtcNow < m_tripRegsAt.AddMinutes(RegsBankAgainMin))
        {
            foreach (int r in s_recallReagents)
                if (PackCount(k, g => g == r) < 2)
                    return ReagentsOut + ": " + UoTiles.Name(r) + " under two; the bank tried at "
                        + m_tripRegsAt.ToLocalTime().ToString("HH:mm");
            shortOf.Clear();
        }
        if (shortOf.Count == 0 && !purseShort)
            return null;
        if (shortOf.Count > 0)
            m_tripRegsAt = Clock.UtcNow;
        string? city = CityOf(k);
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        List<int[]> cb = city == null ? new List<int[]>() : CityBanks(city, px, py);
        int[]? bank = cb.Count > 0 ? cb[0] : null;
        if (bank == null)
            return shortOf.Count > 0 ? ReagentsOut + ": short of " + UoTiles.Name(shortOf[0]) + " and no bank here"
                : null;
        CircuitStatus = (shortOf.Count > 0 ? "Fetching reagents" : "Fetching gold") + " from the bank in " + city;
        string? w = ToBanker(k, bank);
        if (w != null)
            return shortOf.Count > 0 ? "walking to the bank for reagents: " + w : null;
        string taken;
        if (shortOf.Count > 0)
        {
            w = BankTake(new List<int>(s_recallReagents), ReagentRefill, out taken);
            Note("Circuit: reagents from the bank: " + (w ?? taken));
        }
        if (shortOf.Count == 0)
            return PurseFromBank(purse);
        // The purse brought to what it keeps at the same stop: the tailoring circuit landed in Jhelom with 0 gold and
        // bought no thread (2026-10-18 21:00:20).
        w = PurseFromBank(purse);
        if (w != null)
            return w;
        // What the bank lacked, from the city's mage shop (the bank ran out of mandrake root, 2026-10-17).
        // The nearest seller the atlas knows has it, by name (Britain's are in The Sorcerer's Delight; the nearest mage
        // shop, Ethereal Goods, has none known); a mage shop asked as "vendor buy" only when the atlas knows no seller
        // here.
        foreach (int r in RecallRegsUnder(k, TripReagents))
        {
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            int qx = px, qy = py, want = r;
            string label = UoTiles.Name(r);
            string? mw;
            List<AtlasNpc> sellers = Atlas == null ? new List<AtlasNpc>()
                : Atlas.Shops().FindAll(n => UoRegions.City(n.X, n.Y) == city
                && n.Items.Exists(s => s.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase)));
            sellers.Sort((a, b) => Tiles(a.X, a.Y, qx, qy).CompareTo(Tiles(b.X, b.Y, qx, qy)));
            // Every seller known by name, nearest first, until the pack holds ReagentRefill (Damian, 2026-10-18: "there
            // was more bloodmoss on other vendors here"; Brenda's 10 bought her out and no one else was asked).
            mw = sellers.Count == 0 ? "no " + label + " seller in " + city + " known by name" : null;
            for (int si = 0; si < sellers.Count; si++)
            {
                AtlasNpc v = sellers[si];
                int short0 = ReagentRefill - PackCount(k, g => g == want);
                if (short0 <= 0 || circuitStop)
                    break;
                CircuitStatus = "Buying " + label + " from " + v.Name;
                int got, left;
                mw = VendorBuy(g => g == want, short0, out got, out left, v.Name.Split(' ')[0]);
                if (mw != null)
                    Note("Circuit: buying " + label + " from " + v.Name + ": " + mw);
                if (Silent(mw!))
                    return "buying " + label + ": " + mw;
                // The rest of the seller's shop read while there, so its other keepers' stock is known.
                SurveyHere(k);
                List<AtlasNpc> more = Atlas!.Shops().FindAll(n =>
                    UoRegions.City(n.X, n.Y) == city && !sellers.Exists(s => s.Serial == n.Serial)
                    && n.Items.Exists(s => s.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase)));
                if (more.Count > 0)
                {
                    more.Sort((a, b) => Tiles(a.X, a.Y, qx, qy).CompareTo(Tiles(b.X, b.Y, qx, qy)));
                    sellers.AddRange(more);
                }
            }
            if (sellers.Count == 0)
                Note("Circuit: buying " + label + ": " + mw);
            // Still short: every mage shop of the city walked into, nearest first, its keepers named by a click and
            // asked by name (Jhelom's known seller stood on the south island, no way there, 2026-10-18 21:03:22).
            if (PackCount(k, g => g == r) < ReagentRefill)
            {
                List<BuyItem> list = new()
                { new BuyItem { Name = label, Arts = new[] { r }, Count = ReagentRefill - PackCount(k, g => g == r),
                    MaxPrice = LoadoutPrice(r) } };
                ValueMap<string, int> left2 = ValueMap<string, int>.Of((label, list[0].Count));
                int bought = 0;
                List<UoRegions.Shop> shops = UoRegions.ShopsIn(city!, s_mageShopKinds);
                shops.Sort((a, b) => Tiles(a.CX, a.CY, qx, qy).CompareTo(Tiles(b.CX, b.CY, qx, qy)));
                foreach (UoRegions.Shop s in shops)
                {
                    if (circuitStop || left2.Get(label, 0) <= 0)
                        break;
                    CircuitStatus = "Buying " + label + " in " + s.Text;
                    string? bw = BuyInShop(k, s, list, ref left2, ref bought);
                    if (bw != null)
                    {
                        Note("Circuit: buying " + label + ": " + bw);
                        break;
                    }
                }
            }
        }
        // Two of each, one recall there and one back: Kwik recalled to Jhelom on his last bloodmoss, where no seller
        // could be reached, and both the tailoring and the buy circuit stopped there (2026-10-18 21:00).
        foreach (int r in s_recallReagents)
        {
            int n = PackCount(k, g => g == r);
            if (n < 2)
                return ReagentsOut + ": " + (n == 0 ? "no " : "only 1 ") + UoTiles.Name(r)
                    + " in the pack and none in the bank or the mage shops of " + city
                    + " (a recall needs one there and one back)";
        }
        return null;
    }
    // Every travel failure ends the trip but a city with no rune in the bank: the next city's recall goes from the same
    // bank and fails the same way, and the trip said "bank" every 1.5 s, city after city, short of Vesper's banker
    // (Damian, 2026-10-17: "you must stop all spamming").
    static bool TripStops(string why)
    {
        return why != null && !why.StartsWith("no rune for ");
    }
    ValueMap<uint, DateTime> m_underFloor = ValueMap<uint, DateTime>.Empty;
    string? CityOf(GameLink k)
    {
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        return UoRegions.City(x, y);
    }
    // Travel from the bank box (Damian, 2026-10-17: "now we don't need the public library and dont have to 'go back to
    // brit' ever. all the destinations we need are right there in any bank"): to the nearest bank of the city here (or
    // "bank" where the player stands when the region file knows none), the box and its bags opened, each rune labelled
    // where it lies (cached by serial in bank-runes-<server>.txt), and the recall cast at the rune for the city. The
    // shop runes bags alone leave the bank, into the pack at the first recall from it (Damian, 2026-10-18: "take the
    // whole bag of vendor runes out of the bank and use it directly rather than go back and forth to bank"); every
    // other rune stays.
    Map<uint, string> m_bankRunes = Map<uint, string>.Empty; readonly Lock m_bankRunesGate = new();
    bool m_bankRunesLoaded, m_lastRecallShop;
    string BankRunesPath => ServerFile("bank-runes");
    // A rune's place from its label: "a recall rune for north mines" is "north mines", as the bank's runes are kept;
    // the whole label found no bank rune (2026-10-18 19:53:33, the cave rune picked by its cursor).
    static string RunePlace(string label)
    {
        if (label == null)
            return "";
        int at = label.IndexOf("rune for ", StringComparison.OrdinalIgnoreCase);
        return (at >= 0 ? label.Substring(at + 9) : label).Replace('\t', ' ').Replace('\r', ' ').Replace('\n',
            ' ').Trim();
    }
    static string PlaceKey(string s)
    {
        return s == null ? "" : s.Replace("'", "").Replace(" ", "").ToLowerInvariant();
    }
    // A LabelsOf line, "<serial hex> <label> at x,y,z": its serial and label; false for any other shape.
    static bool LabelLine(string line, out uint serial, out string? label)
    {
        serial = 0;
        label = null;
        int sp = line.IndexOf(' '), at = line.LastIndexOf(" at ");
        if (sp <= 0 || at <= sp)
            return false;
        serial = Convert.ToUInt32(line.Substring(0, sp), 16);
        label = line.Substring(sp + 1, at - sp - 1);
        return true;
    }
    // The runes whose labels are not kept yet, each clicked once, their places kept and saved. Every answered label is
    // kept, an unmarked rune's too: kept only for marked ones, every unmarked rune was clicked again at every bank, a
    // flood of "unmarked recall rune" in Damian's chat (2026-10-17).
    void ReadRuneLabels(GameLink k, List<AssistItem> runes)
    {
        List<AssistItem> unknown = runes.FindAll(r => !m_bankRunes.ContainsKey(r.Serial));
        if (unknown.Count == 0)
            return;
        foreach (string line in LabelsOf(k, unknown))
        {
            uint s;
            string? label;
            if (!LabelLine(line, out s, out label) || label == "no label")
                continue;
            lock (m_bankRunesGate)
                m_bankRunes = m_bankRunes.Set(s, RunePlace(label!));
        }
        lock (m_bankRunesGate)
        {
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
    // A rune's kept label follows its renames (Damian, 2026-10-19: "i fixed those runes to be explicit"; a label is
    // kept by serial and a renamed rune keeps its serial): any label the server sends for a rune in view, the player's
    // own click included, and a rename the client answers (0x9A, type 1).
    void RuneRelabel(GameLink k, uint serial, string text)
    {
        bool rune;
        lock (k.State.Gate)
        {
            AssistItem? it;
            rune = (k.State.Items.TryGet(serial, out it) || k.State.Ground.TryGet(serial, out it))
                && it.Graphic >= 0x1F14 && it.Graphic <= 0x1F17;
        }
        if (!rune || text.Length == 0)
            return;
        string place = RunePlace(text);
        LoadBankRunes();
        lock (m_bankRunesGate)
        {
            string? had;
            if (m_bankRunes.TryGet(serial, out had) && had == place)
                return;
            m_bankRunes = m_bankRunes.Set(serial, place);
            List<string> lines = new();
            foreach (KeyValuePair<uint, string> kv in m_bankRunes)
                lines.Add(kv.Key.ToString("X") + "\t" + kv.Value);
            try
            {
                File.WriteAllLines(BankRunesPath, lines);
            }
            catch { }
        }
        lock (m_packRunesGate)
        {
            foreach (KeyValuePair<string, uint> kv in m_packRunes)
                if (kv.Value == serial)
                    m_packRunes = m_packRunes.Remove(kv.Key);
        }
    }
    // A rune in the pack: its serial, the bag it lies in, the place its kept label names (null while unread) and its
    // landing (null before its first recall).
    sealed class HeldRune
    {
        public uint Serial, Container; public string? Place = null; public int[]? Landing = null;
    }
    // Every rune in the pack and its bags, each bag opened once, each label read once and kept (ReadRuneLabels); Wind's
    // runes left out (NeverGo). A rune kept as unmarked and never recalled by is read again once a session, as it may
    // have been marked since (Jhelom's second tailor rune and The Right Fit's were read blank and kept so, 2026-10-19).
    // The one way every picker of a pack rune sees the pack (AssistTesting.md T3, one implementation each).
    List<HeldRune> PackRunes(GameLink k)
    {
        OpenPackBags(k);
        LoadBankRunes();
        LoadRuneLandings();
        List<AssistItem> runes = new();
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && IsRuneArt(it.Graphic))
                    runes.Add(it);
        }
        foreach (AssistItem it in runes)
        {
            if (m_runeLandings.ContainsKey(it.Serial))
                continue;
            lock (m_bankRunesGate)
            {
                string? kept;
                if (m_bankRunes.TryGet(it.Serial, out kept) && IsBlankLabel(kept) && !m_relabeled.Contains(it.Serial))
                {
                    m_relabeled = m_relabeled.Add(it.Serial);
                    m_bankRunes = m_bankRunes.Remove(it.Serial);
                }
            }
        }
        ReadRuneLabels(k, runes);
        List<HeldRune> held = new();
        foreach (AssistItem r in runes)
        {
            string? place;
            m_bankRunes.TryGet(r.Serial, out place);
            if (Banned(place!) || Reserved(r.Serial))
                continue;
            int[]? land;
            if (!m_runeLandings.TryGet(r.Serial, out land))
                land = null;
            held.Add(new HeldRune { Serial = r.Serial, Container = r.Container, Place = place, Landing = land });
        }
        return held;
    }
    // Runes kept for the player's own use, never picked by a circuit (Damian, 2026-10-24: "moonglow bank roof has no
    // walk to anything, its for me to use when i have key in hand on the road"): <server>runes.reserved, serials in
    // hex, by commas.
    internal bool Reserved(uint serial)
    {
        foreach (string t in Cfg.Get(Server + "runes.reserved", "").Split(','))
        {
            uint v;
            if (uint.TryParse(t.Trim(), NumberStyles.HexNumber, null, out v) && v == serial)
                return true;
        }
        return false;
    }
    void LoadBankRunes()
    {
        lock (m_bankRunesGate)
            LoadOnce(ref m_bankRunesLoaded, BankRunesPath,
                line => { string[] t = line.Split('\t'); if (t.Length
                == 2) m_bankRunes = m_bankRunes.Set(Convert.ToUInt32(t[0], 16), t[1]); });
    }
    // Whether the bank box holds a rune for the city, by the labels read so far; true while none has been read.
    bool BankHasRune(string city)
    {
        LoadBankRunes();
        Map<uint, string> runes = m_bankRunes;
        if (runes.Count == 0)
            return true;
        foreach (string v in runes.Values)
            if (PlaceKey(v) == PlaceKey(city))
                return true;
        return false;
    }
    public string? BankRecall(GameLink k, string city)
    {
        return BankRecall(k, city, true);
    }
    // landAtShop: the recall may land at one of the city's shops, by a rune marked there, before the city's own rune.
    string? BankRecall(GameLink k, string city, bool landAtShop)
    {
        if (Banned(city))
            return "never going to " + city;
        LoadBankRunes();
        string? here = ToBankTown(k, CityOf(k)!);
        if (here == null)
            return NoBankTown;
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        List<int[]> banks = here == null ? new List<int[]>() : CityBanks(here, x, y);
        // The city's banks, one that answered before first (CityBanks); a bank the walk cannot reach is passed over for
        // its FailSpan and the next walked to, nothing having been said; "bank" said and unanswered ends the trip
        // (L-FUTILITY). With none left the walk-to-bank fallback in Travel takes a pack rune.
        int[]? bank = null;
        string? why = null;
        foreach (int[] b in banks)
        {
            if (circuitStop)
                return "stopped";
            if (BankSkipped(b))
                continue;
            // ToBanker says "bank" only at the bank, its edge first (Damian, 2026-10-17: "kwik called for the bank
            // NOWHERE NEAR THE BANK"), and returns with the box open.
            CircuitStatus = "Walking to the bank in " + here + " for the " + city + " rune";
            string? w = ToBanker(k, b);
            if (w != null)
            {
                SkipBank(b);
                why = "the bank: " + w;
                continue;
            }
            bank = b;
            FailOk("bank " + b[0] + "," + b[1]);
            break;
        }
        if (banks.Count > 0 && bank == null)
            return why ?? "walking to the bank: every bank in " + here + " failed";
        byte[]? open;
        using (Act(k))
        {
            open = SayBank(k);
        }
        if (open == null)
        {
            if (bank != null)
                SkipBank(bank);
            return NoBanker();
        }
        if (bank != null)
            BankHeard(bank);
        uint box = BankBoxDeposit(k, open);
        Set<uint> tree = BankTree(k, box, false);
        List<AssistItem> runes = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (tree.Contains(it.Container) && it.Graphic >= 0x1F14 && it.Graphic <= 0x1F17 && !Reserved(it.Serial))
                    runes.Add(it);
        ReadRuneLabels(k, runes);
        // A shop rune first (Damian, 2026-10-17: "the circuit didn't use the moonglow tailor rune"): the bank's bag of
        // runes marked at the tailor shops, each labelled for its shop ("Stitchin' Time in Magincia", "Moonglow
        // tailor"), lands at the shop; the city's own rune, which lands at the city's recall point, when the city has
        // none. The picked rune bags first (BankBags): a city bag's rune naming the city is the city's, a shop bag's
        // rune naming it a shop's; then every rune as before.
        // By where each lands (RuneChoice; AssistIdentity.md stage 1): a shop rune lands in one of the city's shops,
        // the city's rune anywhere in the city (docks, a bank, an island area), the city runes bag's and the one
        // labelled the city's name before any other landing there. A rune never yet recalled by is tried by its label
        // and its bag.
        List<uint> cityBag = BankBags("cityrunes");
        List<uint> shopBags = BankBags("shoprunes");
        string want = PlaceKey(city);
        LoadRuneLandings();
        ImmutableArray<RuneSeen>.Builder asShop = ImmutableArray.CreateBuilder<RuneSeen>();
        ImmutableArray<RuneSeen>.Builder asCity = ImmutableArray.CreateBuilder<RuneSeen>();
        foreach (AssistItem r in runes)
        {
            if (RuneBlocked(r.Serial))
                continue;
            string? label;
            m_bankRunes.TryGet(r.Serial, out label);
            string key = PlaceKey(label!);
            int[]? land;
            if (!m_runeLandings.TryGet(r.Serial, out land))
                land = null;
            bool names = key.Length > 0 && key.Contains(want);
            asShop.Add(new RuneSeen(r.Serial, land,
                !names || key == want || cityBag.Contains(r.Container) ? 0 : shopBags.Contains(r.Container) ? 2 : 1));
            asCity.Add(new RuneSeen(r.Serial, land,
                !names ? 0 : cityBag.Contains(r.Container) ? 3 : key == want ? 2 : 0));
        }
        uint shopRune = landAtShop
            ? RuneChoice.Pick(asShop.ToImmutable(), (x, y) => UoRegions.City(x, y) == city && InShop(x, y)) : 0;
        uint cityRune = RuneChoice.Pick(asCity.ToImmutable(), (x, y) => UoRegions.City(x, y) == city);
        uint rune = shopRune != 0 ? shopRune : cityRune;
        m_lastRecallShop = shopRune != 0;
        if (rune == 0)
            return "no rune for " + city + " in the bank";
        // The shop runes bags into the pack while the box is open, carried from now on (CarriedShopRune), by the
        // tailoring circuits only: the mining circuit's way back to its cave took the tailor runes bag along (Damian,
        // 2026-10-22: "it also picked up the bag of tailor runes").
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        List<uint> take = new();
        if (CircuitProfessions().Contains(s_tailorTools))
            lock (k.State.Gate)
                foreach (uint b in shopBags)
                    if (tree.Contains(b) && !PackTree(k).Contains(b))
                        take.Add(b);
        if (take.Count > 0 && pack != 0)
        {
            string? mw = MoveInto(take, pack);
            Note("Circuit: the shop runes bag" + (take.Count > 1 ? "s" : "") + " "
                + (mw == null ? "taken into the pack" : "not taken: " + mw));
        }
        CircuitStatus = "Recalling to " + city + " from the bank";
        string? rw = Recall(rune);
        if (rw == "the landing is blocked")
            BlockRune(rune);
        return rw;
    }
    // The tailor shop runes bag into the pack at the tailoring circuit's start (Damian, 2026-10-18: "when the tailor
    // circuit comes online, look for a bag full of tailor shop runes in the backpack or bank, and put it in backpack if
    // it fits"; "if it wont fit stop"): carried already, nothing; else at the town's bank, the picked shop runes bag,
    // or the box's bag holding the most runes labelled for a tailor shop (a rune label naming a TAILOR shop of any
    // town, or "tailor"), lifted into the pack when the pack keeps under PackItemsMax items and the carry limit. Null
    // to go on (carried, or none found and noted); else why the circuit stops.
    // A bag that does not fit is left in the box and the circuit goes on by the bank's runes, the bank not walked to
    // for it again until the pack has room for its items and weight (the fourth 2026-10-24 audit: the bag's refusal
    // skipped the shops at every top; Damian, 2026-10-24: "this circuit should be able to run continuously, forever,
    // with no stops").
    int m_shopRunesItems, m_shopRunesWeight;
    string? CarryShopRunes(GameLink k)
    {
        List<uint> picked = BankBags("shoprunes");
        int heldNow, weightNow;
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (uint b in picked)
                if (inside.Contains(b))
                    return null;
            heldNow = Inv(k).ItemCount();
            weightNow = k.State.Weight;
        }
        if (m_shopRunesItems > 0 && (heldNow + m_shopRunesItems >= PackItemsMax - 1
            || weightNow > 0 && weightNow + m_shopRunesWeight > MineLimit(k)))
        {
            Note("Circuit: the shop runes bag still does not fit (" + m_shopRunesItems + " items, " + m_shopRunesWeight
                + " stones; the pack " + heldNow + " items, " + weightNow + " stones); travelling by the bank's runes");
            return null;
        }
        string? city = CityOf(k);
        if (city == null)
        {
            Note("Circuit: no shop runes bag in the pack, and no town bank here to look in");
            return null;
        }
        string? why = ToCityBank(k, city, "the shop runes bag");
        if (why != null)
        {
            Note("Circuit: the bank for the shop runes bag: " + why);
            return null;
        }
        byte[]? open;
        using (Act(k))
        {
            open = SayBank(k);
        }
        if (open == null)
        {
            Note("Circuit: the shop runes bag: " + NoBanker());
            return null;
        }
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        uint box = BankBoxDeposit(k, open);
        List<uint> bags = BoxBags(k, box);
        uint bag = 0;
        foreach (uint b in picked)
            if (bags.Contains(b))
            {
                bag = b;
                break;
            }
        if (bag == 0)
        {
            // By the runes' labels: every TAILOR shop's name in every town, and "tailor".
            LoadBankRunes();
            List<string> shopNames = new()
            { "tailor" };
            foreach (string c in UoRegions.Cities())
                foreach (UoRegions.Shop s in UoRegions.ShopsIn(c, new[] { "TAILOR" }))
                    shopNames.Add(PlaceKey(s.Text));
            List<AssistItem> runes = new();
            lock (k.State.Gate)
                foreach (AssistItem it in k.State.Items.Values)
                    if (bags.Contains(it.Container) && it.Graphic >= 0x1F14 && it.Graphic <= 0x1F17)
                        runes.Add(it);
            ReadRuneLabels(k, runes);
            ValueMap<uint, int> score = ValueMap<uint, int>.Empty;
            foreach (AssistItem r in runes)
            {
                string? label;
                m_bankRunes.TryGet(r.Serial, out label);
                string key = PlaceKey(label!);
                if (key.Length > 0 && shopNames.Exists(n => n.Length > 0 && key.Contains(n)))
                    score = score.Set(r.Container, score.Get(r.Container, 0) + 1);
            }
            int best = 0;
            foreach (KeyValuePair<uint, int> kv in score)
                if (kv.Value > best)
                {
                    best = kv.Value;
                    bag = kv.Key;
                }
            if (bag == 0)
            {
                Note("Circuit: no bag of tailor shop runes in the bank box; travelling by the bank's runes");
                return null;
            }
        }
        // Whether it fits: the bag and everything in it against the pack's item limit, its tile weights against the
        // carry limit.
        int items = 1, weight = 0, held = 0, w8;
        List<UoTiles.Tile> tiles = UoTiles.Items();
        lock (k.State.Gate)
        {
            Set<uint> inBag = Set<uint>.Of(bag);
            for (int pass = 0; pass < 4; pass++)
                foreach (AssistItem it in k.State.Items.Values)
                    if (inBag.Contains(it.Container))
                        inBag = inBag.Add(it.Serial);
            foreach (uint s in inBag)
            {
                AssistItem? it;
                if (!k.State.Items.TryGet(s, out it))
                    continue;
                if (s != bag)
                    items++;
                if (it.Graphic >= 0 && it.Graphic < tiles.Count && tiles[it.Graphic].Weight < 255)
                    weight += tiles[it.Graphic].Weight * Math.Max(1, it.Amount);
            }
            held = Inv(k).ItemCount();
            w8 = k.State.Weight;
        }
        if (held + items >= PackItemsMax - 1 || w8 > 0 && w8 + weight > MineLimit(k))
        {
            m_shopRunesItems = items;
            m_shopRunesWeight = weight;
            Note("Circuit: the shop runes bag " + bag.ToString("X") + " does not fit (" + items + " items, " + weight
                + " stones; the pack " + held + " items of " + (PackItemsMax - 1) + ", " + w8 + " stones of "
                + MineLimit(k) + "); left in the box, travelling by the bank's runes");
            return null;
        }
        m_shopRunesItems = m_shopRunesWeight = 0;
        string? mw = MoveInto(new List<uint> { bag }, pack);
        if (mw != null)
            return "the shop runes bag " + bag.ToString("X") + " into the pack: " + mw;
        if (!picked.Contains(bag))
        {
            picked.Add(bag);
            Cfg.Set(Server + BankBagKey("shoprunes"), string.Join(",", picked.ConvertAll(b => b.ToString("X"))));
        }
        Note("Circuit: the shop runes bag " + bag.ToString("X") + " (" + (items - 1) + " items) taken into the pack");
        return null;
    }
    // A rune for the city carried anywhere in the pack, every bag opened (Damian, 2026-10-19: "i got the full bag of
    // runes in my backpack. how about we have this circuit do a full bag scan"), by the label kept by serial (an unread
    // one clicked once): one naming the shop first, then one naming the city alone, then a shop runes bag's rune naming
    // the city, then any naming it; 0 when none.
    // The pack's rune for the shop, else for the city, by where each lands (RuneChoice; AssistIdentity.md stage 1): one
    // landing in the shop's rectangle, else one landing in the city; a rune never yet recalled by is tried by its label
    // (the shop's title, the city's name, a shop runes bag's), and its first recall records its landing.
    internal uint CarriedShopRune(GameLink k, string city, UoRegions.Shop? shop = null)
    {
        if (Banned(city))
            return 0;
        List<uint> bags = BankBags("shoprunes");
        // A rune whose landing was blocked is out of every choice below, NearestLanding's too (the homespun world: it
        // handed the blocked shop rune back two and nine minutes after the block, every restock).
        List<HeldRune> runes = PackRunes(k);
        runes.RemoveAll(r => RuneBlocked(r.Serial));
        if (runes.Count == 0)
            return 0;
        string want = PlaceKey(city), shopKey = shop == null ? "" : PlaceKey(shop.Text);
        ImmutableArray<RuneSeen>.Builder forShop = ImmutableArray.CreateBuilder<RuneSeen>();
        ImmutableArray<RuneSeen>.Builder forCity = ImmutableArray.CreateBuilder<RuneSeen>();
        foreach (HeldRune r in runes)
        {
            string key = PlaceKey(r.Place!);
            bool named = key.Length >= 3;
            forShop.Add(new RuneSeen(r.Serial, r.Landing,
                named && shopKey.Length > 0 && (shopKey.Contains(key) || key.Contains(shopKey)) ? 1 : 0));
            forCity.Add(new RuneSeen(r.Serial, r.Landing,
                !named || !key.Contains(want) ? 0 : key == want ? 3 : bags.Contains(r.Container) ? 2 : 1));
        }
        uint s = shop == null ? 0 : RuneChoice.Pick(forShop.ToImmutable(), (x, y) => shop.Holds(x, y, 8));
        if (s != 0)
            return s;
        s = RuneChoice.Pick(forCity.ToImmutable(), (x, y) => UoRegions.City(x, y) == city);
        // A city rune landing far from the shop loses to the carried rune landing nearest it (Damian, 2026-10-24:
        // "before "recalling" decide if it is neccessary here... a bank is a bank"; 14:33:29, Moonglow's bank rune
        // landed 302 tiles from Encyclopedia Magicka, then the pack's 41E9C908, labelled "Moonglow Encyclopedia
        // Magicka", recalled to it).
        return shop == null ? s : NearestLanding(runes, city, shop, s);
    }
    // Of the carried runes with a recorded landing in the city, the one landing nearest the shop, when it lands nearer
    // than the rune chosen (whose landing, unrecorded, counts as far).
    static uint NearestLanding(List<HeldRune> runes, string city, UoRegions.Shop shop, uint chosen)
    {
        uint best = chosen;
        int bestD = int.MaxValue;
        foreach (HeldRune r in runes)
            if (r.Serial == chosen && r.Landing != null)
                bestD = Tiles(r.Landing[0], r.Landing[1], shop.CX, shop.CY);
        foreach (HeldRune r in runes)
        {
            if (r.Landing == null || UoRegions.City(r.Landing[0], r.Landing[1]) != city)
                continue;
            int d = Tiles(r.Landing[0], r.Landing[1], shop.CX, shop.CY);
            if (d < bestD)
            {
                bestD = d;
                best = r.Serial;
            }
        }
        return best;
    }
    // Places a trip never goes (Damian, 2026-10-17: "put a rule in, do not go to wind"), by destination or rune label.
    static readonly ImmutableArray<string> s_neverGo = [ "Wind" ];
    static bool Banned(string place)
    {
        if (place == null)
            return false;
        foreach (string b in s_neverGo)
            if (place.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
    // A recall that moved the player is not one that arrived: the trip goes on only in the city it named.
    string? Travel(GameLink k, string city)
    {
        if (Banned(city))
            return "never going to " + city;
        if (CityOf(k) == city)
            return null;
        // A carried shop rune for the city is recalled from where the player stands, no bank walk; the gold over
        // BankOver is banked here first, as the town is left.
        uint carried = CarriedShopRune(k, city);
        if (carried != 0)
        {
            string? here0 = CityOf(k);
            if (here0 != null && PackCount(k, g => g == GoldArt) > BankOver)
            {
                string? bw = BankHere(k, here0);
                if (bw != null)
                    Note("Circuit: banking before the recall: " + bw);
            }
            CircuitStatus = "Recalling to " + city + " by the carried shop rune";
            string? rw = Recall(carried);
            if (rw == "the landing is blocked")
                BlockRune(carried);
            if (rw == null && CityOf(k) == city)
            {
                m_lastRecallShop = true;
                return null;
            }
            Note("Circuit: the carried shop rune for " + city + ": " + (rw ?? "landed in " + (CityOf(k) ?? "no city"))
                + "; by the bank");
            if (CityOf(k) == city)
                return null;
        }
        // The recall goes from the bank, so the gold over the purse is banked there first: one walk, not two (Damian
        // banked whatever he carried at each recall's bank, 970 in Britain, 2026-10-17).
        // Keep thread carries its purse up to BankOver (KeepPurse), so it banks only past that.
        string? from = CityOf(k);
        int keep = KeepRules.PackKeeps(PurseKeep, m_keepPurseNeed, m_keepMode);
        if (from != null && PackCount(k, g => g == GoldArt) > keep)
        {
            string? bw = BankHere(k, from, keep);
            if (bw != null)
                Note("Circuit: banking before the recall: " + bw);
        }
        string? w = BankRecall(k, city);
        // A shop rune's landing taken by someone ("Something is blocking the location."; Skara Brae's tailor,
        // 2026-10-17): the city's own rune once, a different landing, from the box still open.
        if (w == "the landing is blocked" && m_lastRecallShop)
        {
            Note("Circuit: the shop rune for " + city + " is blocked; by the city's rune");
            w = BankRecall(k, city, false);
        }
        // A bank the walk cannot reach (Jhelom's south island has none; Damian, 2026-10-17: "also you had runes you
        // could have recalled"): by the pack's rune for the city when it carries one, else by another pack rune to a
        // city with a bank, and the bank's rune from there.
        if (w != null && !circuitStop && (w.StartsWith("walking to the bank") || w.StartsWith("the walk to the bank")))
        {
            Note("Circuit: " + w + "; travelling by a pack rune");
            uint direct = 0, other = 0;
            string? otherCity = null, here = CityOf(k);
            foreach (string line in PackRuneLabels(k))
            {
                uint s;
                string? label;
                if (!LabelLine(line, out s, out label)
                    || label!.IndexOf("rune for ", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string place = RunePlace(label);
                if (PlaceKey(place) == PlaceKey(city))
                {
                    direct = s;
                }
                else if (other == 0 && PlaceKey(place) != PlaceKey(here!) && !Banned(place))
                {
                    other = s;
                    otherCity = place;
                }
            }
            if (direct == 0 && other == 0)
                return w + "; no pack rune to travel by";
            string? rw = Recall(direct != 0 ? direct : other);
            if (rw != null)
                return "recalling by the pack rune for " + (direct != 0 ? city : otherCity) + ": " + rw;
            w = CityOf(k) == city ? null : BankRecall(k, city);
        }
        if (w != null)
            return w;
        return CityOf(k) == city ? null : "the rune for " + city + " landed in " + (CityOf(k) ?? "no city");
    }
    // The shops of a city: tailors (fancy shirts) or thread vendors, from the atlas.
    List<AtlasNpc> CityShops(string city, string sells)
    {
        List<AtlasNpc> l = new();
        if (Atlas == null)
            return l;
        foreach (AtlasNpc n in Atlas.Shops())
            if (UoRegions.City(n.X, n.Y) == city && TailorVendor(n)
                && n.Items.Exists(s => s.IndexOf(sells, StringComparison.OrdinalIgnoreCase) >= 0))
                l.Add(n);
        return l;
    }
    // A thread vendor by its stock list, or any tailor not yet seen to have none: a tailor's buy list may never have
    // been read (Damian, 2026-10-17: "we go find another vendor that is not recently checked. magincia is great for
    // that"; the atlas knew no Magincia thread vendor).
    // Every named shopkeeper standing in one of the city's tailor shops is one too, unless its title names another
    // trade (TailorStock) or its stock list is read and holds no thread (Damian, 2026-10-17: "its always a tailor that
    // is thread selling, and they all do"; started in Jhelom's north Needle, the circuit walked for Kimmie on the south
    // island and asked nobody there).
    List<AtlasNpc> ThreadCandidates(string city)
    {
        List<AtlasNpc> l = CityShops(city, "thread");
        foreach (AtlasNpc n in CityBuyers(city))
            if (!l.Exists(x => x.Serial == n.Serial))
                l.Add(n);
        if (Atlas != null)
        {
            List<UoRegions.Shop> shops = TailorShopsIn(city);
            foreach (AtlasNpc n in Atlas.Shops())
            {
                if (n.Name.Length == 0 || l.Exists(x => x.Serial == n.Serial)
                    || !shops.Exists(s => s.Holds(n.X, n.Y, 2)))
                    continue;
                if (n.Items.Count > 0
                    && !n.Items.Exists(s => s.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;
                l.Add(n);
            }
        }
        Set<uint> off = ThreadOff();
        l.RemoveAll(n => !TailorStock(n) || off.Contains(n.Serial));
        return l;
    }
    // A thread vendor is a tailor (Damian, 2026-10-21: "we need to remove the circuit vendors that are not tailors
    // (weavers) and disable tate"): one whose stock, once read, names a sewing kit, a shirt or thread. A weaver's stock
    // is yarn, bolts, dyes, dye tubs, folded cloth and scissors (Bertram, Aneida, Erica, Alexander, Neale), and the
    // shirt buyers among them came in as CityBuyers; a stock not yet read counts as a tailor's. A keeper whose name
    // carries a title is a tailor by that title alone (Damian, 2026-10-24: "only tailors have thread. weavers never do.
    // only tailors buy shirts, weavers never do."; William the blacksmith, wandered to the door of Ocllo's tailor shop,
    // was asked for thread at 15:49:50).
    static readonly ImmutableArray<string> s_tailorGoods = [ "sewing kit", "shirt", "thread" ];
    // The named keepers seen in the shop in the last PeopleShownMs who can be tailors (TailorStock): the shop's keeper
    // asked when no tailor of it is known (Vesper south, 17:12:08: Tate out of the shop, "Judd sell" to the weaver;
    // Damian, 2026-10-24: "judd is a weaver, tate is the tailor there").
    internal List<AtlasNpc> ShopTailors(UoRegions.Shop shop)
    {
        if (Atlas == null)
            return new List<AtlasNpc>();
        return Atlas.Shops().FindAll(n =>
            n.Name.Length > 0 && shop.Holds(n.X, n.Y, 2)
            && (Clock.UtcNow.ToLocalTime() - n.Last).TotalMilliseconds < AssistAtlas.PeopleShownMs && TailorStock(n));
    }
    internal static bool TailorStock(AtlasNpc n)
    {
        int at = n.Name.LastIndexOf(" the ", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
            return n.Name.Substring(at + 5).IndexOf("tailor", StringComparison.OrdinalIgnoreCase) >= 0;
        if (n.Items.Count == 0)
            return true;
        foreach (string s in n.Items)
            foreach (string g in s_tailorGoods)
                if (s.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
        return false;
    }
    // The thread vendors taken out of the round by the player (<server>thread.off, serials), never asked for thread.
    Set<uint> ThreadOff()
    {
        Set<uint> s = Set<uint>.Empty;
        foreach (string t in Cfg.Get(Server + "thread.off", "").Split(new[] { ';', ',' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            uint v;
            if (uint.TryParse(t.Trim(), NumberStyles.HexNumber, null, out v))
                s = s.Add(v);
        }
        return s;
    }
    // thread-off / thread-on <name or serial>: the tailor of that serial (8 hex digits), else every tailor of that
    // first name in the approved shops, taken out of the round or put back (AssistIdentity.md stage 3: a name may be
    // two vendors). What changed, or why nothing did.
    public string ThreadOffSet(string name, bool off)
    {
        if (Atlas == null)
            return "ERROR no atlas";
        uint serial = 0;
        bool bySerial = name.Length == 8 && uint.TryParse(name, NumberStyles.HexNumber, null, out serial);
        Set<uint> set = ThreadOff();
        List<string> hit = new();
        foreach (AtlasNpc n in Atlas.Shops())
        {
            if (bySerial ? n.Serial != serial : !n.Name.Split(' ')[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TailorVendor(n) || !TailorStock(n))
                continue;
            set = off ? set.Add(n.Serial) : set.Remove(n.Serial);
            hit.Add(n.Name + " " + n.Serial.ToString("X") + " at " + n.X + "," + n.Y);
        }
        if (hit.Count == 0)
            return "ERROR no tailor named " + name + " in the approved shops";
        SaveThreadOff(set);
        Note("Thread: " + (off ? "out of the round: " : "back in the round: ") + string.Join(", ", hit));
        return "OK " + (off ? "off: " : "on: ") + string.Join(", ", hit);
    }
    void SaveThreadOff(Set<uint> set)
    {
        List<string> l = new();
        foreach (uint v in set.Ordered(Comparer<uint>.Default))
            l.Add(v.ToString("X"));
        Cfg.Set(Server + "thread.off", string.Join(";", l));
    }
    public bool IsThreadOff(uint serial)
    {
        return ThreadOff().Contains(serial);
    }
    // One vendor out of the thread round or back, by serial: the circuit vendor list's menu (Damian, 2026-10-22: "tate
    // remains a circuit vendor and i have no way of turning him off here").
    public void ThreadOffSerial(uint serial, string name, bool off)
    {
        Set<uint> set = ThreadOff();
        SaveThreadOff(off ? set.Add(serial) : set.Remove(serial));
        Note("Thread: " + (off ? "out of the round: " : "back in the round: ") + name + " " + serial.ToString("X"));
    }
    // The candidates nearest the player first.
    List<AtlasNpc> NearestFirst(GameLink k, List<AtlasNpc> l)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        l.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
        return l;
    }
    bool ThreadDue(AtlasNpc n)
    {
        if (Skipped(n.Serial) || IsThreadOff(n.Serial))
            return false;
        return Clock.UtcNow.ToLocalTime() >= ThreadDueAt(n.Serial);
    }
    // A thread vendor's next stock, from its thread buys in the trades history: uo1998 grows a tailor's thread by
    // ThreadStep at each restock after it was bought out (10, 19, 28 ... 109: Slade and Tate, 2026-10-19), and one not
    // bought out for longer than ThreadKeepMinutes is back at ThreadBase (Brian 73 to 10 after 101 minutes, Jannelle 19
    // to 10 after 100; gains seen after up to 62). A stock seen and left unbought is what it holds.
    public const int ThreadBase = 10, ThreadStep = 9, ThreadKeepMinutes = 62;
    int ThreadExpected(AtlasNpc n)
    {
        DateTime now = Clock.UtcNow.ToLocalTime();
        CircuitVendor? rec = CircuitVendors().Find(c => c.Serial == n.Serial);
        if (rec != null && rec.Stock > 0 && (now - rec.StockAt).TotalMinutes < ThreadKeepMinutes)
            return rec.Stock;
        if (Atlas == null)
            return ThreadBase;
        List<AtlasTrade> buys = Atlas.Trades(n.Serial).FindAll(t => t.Kind == "buy"
            && t.Item.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0);
        DateTime last = DateTime.MinValue;
        foreach (AtlasTrade t in buys)
            if (t.At > last)
                last = t.At;
        if (last == DateTime.MinValue || (now - last).TotalMinutes > ThreadKeepMinutes)
            return ThreadBase;
        // A buy-out can take two purchases (Jannelle's 10 at 3 gp, then her last 9 at 4).
        int got = 0;
        foreach (AtlasTrade t in buys)
            if ((last - t.At).TotalMinutes < 2)
                got += t.Amount;
        return got + ThreadStep;
    }
    // A city's thread for a trip: the expected stock of every vendor there whose restock is due, so the big restockers
    // are visited while they grow and a city of 10s waits (tailoring-circuit.md Next 0).
    int ThreadScore(string city)
    {
        int s = 0;
        foreach (AtlasNpc n in ThreadCandidates(city))
            if (ThreadDue(n))
                s += ThreadExpected(n);
        return s;
    }
    // A trip's preference by city: the quiet ones first (Damian, 2026-10-17: "magincia is great for solo, nujelm and
    // jhelom too"). Britain is one city among the rest, its shops used and no centre (Damian, 2026-10-17: "yes it
    // should visit brit, but it should also not consider it the primary central location"; he sold at both its tailors
    // at 24).
    static readonly ImmutableArray<string> s_quietCities = [ "Magincia", "Nujel'm", "Jhelom" ];
    static int CityWeight(string city)
    {
        return s_quietCities.IndexOf(city) >= 0 ? 300 : 100;
    }
    // The under-floor offers, kept in underfloor-<server>.txt (serial, tab, ticks) so a relay restart does not ask
    // every tailor again (2026-10-17: after a restart the circuit walked Britain end to end re-asking Slade, Tracy and
    // Michelle).
    bool m_underFloorLoaded;
    string UnderFloorPath => ServerFile("underfloor");
    // underFloor holds when a tailor may be asked again (Damian, 2026-10-17: "do not fucking return to a vendor you
    // have seen and bought out and sold out for 15 mintues. remember the history of these runs").
    // A tailor or shop offering under the floor, or buying no more, is passed over until the survey cycle ends, never
    // by a timer (Damian, 2026-10-19: "a tailor offering less should be passed over until the cycle is complete, not a
    // timer"; 2026-10-20: "there always must be a reset condition for all pauses. like after you've gone through all
    // the other vendors"): the survey's end (SurveyDone) or the end of its SurveyFreshMinutes (EndCycle); a day stands
    // for "until then".
    static readonly TimeSpan s_cycleSpan = TimeSpan.FromDays(1);
    Set<uint> m_passedTailors = Set<uint>.Empty;
    Set<string> m_passedShops = Set<string>.Empty;
    readonly Lock m_passedTailorsGate = new(), m_passedShopsGate = new(), m_underFloorGate = new();
    // Homespun's marks last a restock, not a survey cycle (nothing in homespun ends a cycle): a tailor that left shirts
    // unbought at 16:08:42 was passed over for a day, and with every tailor so marked each two-minute top lapped twelve
    // passed-over shops with a full pack (the 2026-10-24 audit's first finding).
    internal TimeSpan PassSpan => Homespun ? s_restockSpan : s_cycleSpan;
    static readonly TimeSpan s_restockSpan = TimeSpan.FromMinutes(RestockMinutes);
    internal static TimeSpan RestockSpan => s_restockSpan;
    // In homespun, until her next restock (RestockAfter): a tailor that has bought her fill buys again then, and the
    // fixed restock span recalled to three tailors full since 20:16 (Nana, Simon and Lynn, 2026-10-24 20:35).
    void UnderFloorOffered(uint serial)
    {
        if (Homespun)
        {
            // Under a sell order, SellRest restocks surely past: the first by SureAfter, then whole periods. The
            // estimate's median (RestockAfter) can fall before her real first restock, and two by it were one in the
            // sell order's world case.
            DateTime now = Clock.UtcNow.ToLocalTime();
            DateTime until = SellOrder.Length > 0
                ? SureAfter(serial, now).AddSeconds((SellRest - 1) * KeepRules.RestockPeriod(RestocksOf(serial)))
                : RestockAfter(serial, now);
            AvoidTailorUntil(serial, until);
            if (SellOrder.Length > 0)
                Note("Circuit: sell order: " + (VendorNow(serial)?.Name ?? serial.ToString("X")) + " sold down; rests "
                    + SellRest + " restocks, to " + until.ToString("HH:mm:ss"));
        }
        else
            AvoidTailor(serial, PassSpan);
        lock (m_passedTailorsGate)
            m_passedTailors = m_passedTailors.Add(serial);
    }
    void ShopUnderFloor(UoRegions.Shop s)
    {
        AvoidShop(s, PassSpan);
        lock (m_passedShopsGate)
            m_passedShops = m_passedShops.Add(s.Key);
    }
    // A vendor that gave no answer, passed over for a restock: SkipVendor's first span is none (FailSpan), so the next
    // round asked it again (L-FUTILITY).
    void SkipFor(uint serial, TimeSpan span)
    {
        lock (m_skippedGate)
            m_skipped = m_skipped.Set(serial, Clock.UtcNow.ToLocalTime() + span);
    }
    // A rune whose landing answered "Something is blocking the location." is not cast again for a restock, at a bank
    // stop or a shop (15:47:04, 15:54:16 and 15:59:27: the bank rune 45C8C04E cast into the same block three times).
    ValueMap<uint, DateTime> m_blockedRunes = ValueMap<uint, DateTime>.Empty; readonly Lock m_blockedRunesGate = new();
    internal void BlockRune(uint rune)
    {
        lock (m_blockedRunesGate)
            m_blockedRunes = m_blockedRunes.Set(rune, Clock.UtcNow + s_restockSpan);
    }
    internal bool RuneBlocked(uint rune)
    {
        DateTime until;
        return m_blockedRunes.TryGet(rune, out until) && Clock.UtcNow < until;
    }
    void EndCycle()
    {
        lock (m_passedTailorsGate)
            lock (m_underFloorGate)
            {
                foreach (uint s in m_passedTailors)
                    m_underFloor = m_underFloor.Remove(s);
                m_passedTailors = m_passedTailors.Clear();
                List<string> lines = new();
                foreach (KeyValuePair<uint, DateTime> kv in m_underFloor)
                    lines.Add(kv.Key.ToString("X") + "\t" + kv.Value.Ticks);
                try
                {
                    File.WriteAllLines(UnderFloorPath, lines);
                }
                catch { }
            }
        lock (m_passedShopsGate)
            lock (m_shopAvoidGate)
            {
                foreach (string s in m_passedShops)
                    m_shopAvoid = m_shopAvoid.Remove(s);
                m_passedShops = m_passedShops.Clear();
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
    void AvoidTailor(uint serial, TimeSpan span) => AvoidTailorUntil(serial, Clock.UtcNow.ToLocalTime() + span);
    void AvoidTailorUntil(uint serial, DateTime until)
    {
        Interlocked.Increment(ref m_vendorSkips);
        lock (m_underFloorGate)
        {
            DateTime had;
            if (m_underFloor.TryGet(serial, out had) && had > until)
                until = had;
            m_underFloor = m_underFloor.Set(serial, until);
            List<string> lines = new();
            foreach (KeyValuePair<uint, DateTime> kv in m_underFloor)
                lines.Add(kv.Key.ToString("X") + "\t" + kv.Value.Ticks);
            try
            {
                File.WriteAllLines(UnderFloorPath, lines);
            }
            catch { }
        }
    }
    bool PaysFloor(AtlasNpc n)
    {
        lock (m_underFloorGate)
            LoadOnce(ref m_underFloorLoaded, UnderFloorPath,
                line => { string[] t = line.Split('\t'); if (t.Length
                == 2) m_underFloor = m_underFloor.Set(Convert.ToUInt32(t[0], 16), new DateTime(long.Parse(t[1]))); });
        DateTime until;
        if (m_underFloor.TryGet(n.Serial, out until) && Clock.UtcNow.ToLocalTime() < until)
            return false;
        // A fresh offer under the shirts' market floor is an answer already (RecordOffer), whatever was cleared since.
        int fresh = FreshOffer(n.Serial);
        if (fresh < 0)
            return true;
        SellItem? shirt = SellList().Find(i => Array.Exists(i.Arts, IsShirtArt));
        return shirt == null || fresh >= VendorFloor(n.Serial, shirt.Arts[0]);
    }
    // Hours since the circuit last sold to this tailor, from the trades history, at most RestCapHours (never: the cap).
    public const int RestCapHours = 12;
    double RestHours(AtlasNpc n)
    {
        if (Atlas == null)
            return RestCapHours;
        DateTime last = DateTime.MinValue;
        foreach (AtlasTrade t in Atlas.Trades(n.Serial))
            if (t.Kind == "sell" && t.At > last)
                last = t.At;
        return last == DateTime.MinValue ? RestCapHours
            : Math.Min(RestCapHours, (Clock.UtcNow.ToLocalTime() - last).TotalHours);
    }
    // What a tailor's latest sale within RestCapHours paid over its item's floor; int.MinValue for none. A vendor's
    // offer rises with its own stock of the item against its region's (OUO CShopkeeper_VendorAdjustment,
    // tailoring-circuit.md Next 2), so the tailor that paid most last pays most again, and is asked first (Damian,
    // 2026-10-19).
    int PaidOver(AtlasNpc n)
    {
        if (Atlas == null)
            return int.MinValue;
        AtlasTrade? last = null;
        foreach (AtlasTrade t in Atlas.Trades(n.Serial))
            if (t.Kind == "sell" && (last == null || t.At > last.At)
                && (Clock.UtcNow.ToLocalTime() - t.At).TotalHours < RestCapHours)
                last = t;
        if (last == null)
            return int.MinValue;
        SellItem? item = SellList().Find(i => string.Equals(i.Name, last.Item, StringComparison.OrdinalIgnoreCase));
        return item == null ? int.MinValue : last.Price - item.Floor;
    }
    // A sell trip's shops a few seconds' run apart count as equally near (SellTrip's order).
    public const int ShopBand = 30;
    // Best paying first, then nearest to x,y.
    void BestPayingFirst(List<AtlasNpc> l, int x, int y)
    {
        // A fresh offer (FreshOffer, SurveyFreshMinutes) ranks before a latest sale.
        ValueMap<uint, int> paid = ValueMap<uint, int>.Empty;
        foreach (AtlasNpc n in l)
        {
            int o = FreshOffer(n.Serial);
            paid = paid.Set(n.Serial, o >= 0 ? 1000 + o : PaidOver(n));
        }
        l.Sort((a, b) => { int c = paid.Get(b.Serial, 0).CompareTo(paid.Get(a.Serial, 0)); return c != 0 ? c
            : Tiles(a.X, a.Y, x, y).CompareTo(Tiles(b.X, b.Y, x, y)); });
    }
    // The tailors in town in walking order: shop by shop, the nearest shop next from the last, every tailor of a shop
    // asked while there, the best paying first inside it (Damian, 2026-10-19: "check both vendors at a store, not just
    // one, while you are there"; "the vendor check planning is not location sensitive": Tracy, Slade, then back to
    // Michelle beside Tracy). A vendor in no shop region is a stop of its own.
    List<AtlasNpc> ByShopRoute(List<AtlasNpc> l, int x, int y)
    {
        Map<string, List<AtlasNpc>> groups = Map<string, List<AtlasNpc>>.Empty;
        List<string>? order = new();
        foreach (AtlasNpc n in l)
        {
            UoRegions.Shop? s = ShopAt(n.X, n.Y);
            string key = s != null ? s.Key : "@" + n.Serial.ToString("X");
            List<AtlasNpc>? g;
            if (!groups.TryGet(key, out g))
            {
                groups = groups.Set(key, g = new List<AtlasNpc>());
                order.Add(key);
            }
            g.Add(n);
        }
        // The shortest order over ShopOrderExact shops or fewer, every order tried (720 at 6); past that the nearest
        // next.
        if (order.Count > 1 && order.Count <= ShopOrderExact)
            order = ShortestShopOrder(order, groups, x, y);
        List<AtlasNpc> route = new();
        int cx = x, cy = y;
        while (order!.Count > 0)
        {
            string next = order[0];
            int best = int.MaxValue;
            if (order.Count > ShopOrderExact)
                foreach (string key in order)
                {
                    int d = NearestIn(groups[key]!, cx, cy);
                    if (d < best)
                    {
                        best = d;
                        next = key;
                    }
                }
            order.Remove(next);
            List<AtlasNpc> grp = groups[next]!;
            BestPayingFirst(grp, cx, cy);
            route.AddRange(grp);
            cx = grp[0].X;
            cy = grp[0].Y;
        }
        return route;
    }
    public const int ShopOrderExact = 6;
    static int NearestIn(List<AtlasNpc> g, int x, int y)
    {
        int d = int.MaxValue;
        foreach (AtlasNpc n in g)
            d = Math.Min(d, Tiles(n.X, n.Y, x, y));
        return d;
    }
    // A shop is entered at its member nearest the last stop and left from there.
    static List<string>? ShortestShopOrder(List<string> keys, Map<string, List<AtlasNpc>> groups, int x, int y)
    {
        List<string>? best = null;
        int bestCost = int.MaxValue;
        List<string> perm = new();
        bool[] used = new bool[keys.Count];
        Action<int, int, int>? walk = null;
        walk = (cx, cy, cost) =>
        {
            if (cost >= bestCost)
                return;
            if (perm.Count == keys.Count)
            {
                bestCost = cost;
                best = new List<string>(perm);
                return;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                if (used[i])
                    continue;
                AtlasNpc? at = null;
                int d = int.MaxValue;
                foreach (AtlasNpc n in groups[keys[i]]!)
                {
                    int t = Tiles(n.X, n.Y, cx, cy);
                    if (t < d)
                    {
                        d = t;
                        at = n;
                    }
                }
                used[i] = true;
                perm.Add(keys[i]);
                walk!(at!.X, at.Y, cost + d);
                perm.RemoveAt(perm.Count - 1);
                used[i] = false;
            }
        };
        walk(x, y, 0);
        return best;
    }
    // The cities a trip can go to, best first by score.
    List<string> TripCities(GameLink k, Func<string, int> score)
    {
        List<string> cities = new();
        string? here = CityOf(k);
        if (Atlas != null)
            foreach (AtlasNpc n in Atlas.Shops())
            {
                string? c = UoRegions.City(n.X, n.Y);
                if (c != null && c != here && !Banned(c) && !cities.Contains(c) && score(c) > 0)
                    cities.Add(c);
            }
        cities.Sort((a, b) => score(b).CompareTo(score(a)));
        return cities;
    }
    // Null, or why the buying stopped: a vendor that gave no proper answer ends it (assist-plan.md rule 1); one that
    // could not be reached or sold none is passed over for its FailSpan.
    // Nearest first, chosen again from where each ask left the player (Vesper, 2026-10-17: the first candidate in the
    // atlas's order was Tate across town, and the circuit walked past Morena's shop where the recall landed).
    string? BuyCityThread(GameLink k, string city, out int got)
    {
        got = 0;
        NameUnnamed(VendorHear * 4);
        List<AtlasNpc> left0 = ThreadCandidates(city).FindAll(ThreadDue);
        while (left0.Count > 0)
        {
            AtlasNpc v = NearestFirst(k, left0)[0];
            left0.Remove(v);
            if (circuitStop || PackCount(k, IsThreadArt) >= ThreadWant)
                break;
            if (!ThreadDue(v))
                continue;
            string name = v.Name.Split(' ')[0];
            CircuitStatus = "Buying thread from " + name + " in " + city;
            int g, left;
            string? w = VendorBuy(IsThreadArt, ThreadWant * 10, out g, out left, name);
            if (w == null)
            {
                FailOk("buy vendor " + v.Serial.ToString("X"));
                got += g;
                continue;
            }
            Note("Circuit: " + name + ": " + w);
            if (Silent(w))
                return "buying thread from " + name + ": " + w;
            SkipVendor(v.Serial, w);
        }
        return null;
    }
    // A failure's pause (Damian, 2026-10-20: "the first time we get 1-5, do not make a timer, make a alternate plan.
    // then try again on a normal cycle. then if it fails the same task on the same target a second time, add the
    // timer"; 5 minutes): a first failure passes the target over for this pass only, the next vendor, shop or bank
    // being the alternate plan; a second of the same task on it pauses it FailPauseMinutes and starts the count again.
    // A third, after the timer, marks the task on the target broken (Damian, 2026-10-20: "then after that timer expires
    // it tries a third time, then mark that task/target broken until intelligence checks it out"): kept in
    // broken-<server>.txt (key, why, ticks), refused by Skipped, BankSkipped and ShopOpen until "broken clear" on the
    // control port, and noted "BROKEN:" for whoever watches the log.
    public const int FailPauseMinutes = 5;
    ValueMap<string, int> m_failCount = ValueMap<string, int>.Empty; readonly Lock m_failCountGate = new();
    Map<string, string> m_broken = Map<string, string>.Empty; bool m_brokenLoaded;
    readonly Lock m_brokenGate = new();
    string BrokenPath => ServerFile("broken");
    void LoadBroken()
    {
        LoadOnce(ref m_brokenLoaded, BrokenPath,
            line => { string[] t = line.Split('\t'); if (t.Length >= 2) m_broken = m_broken.Set(t[0], t[1]); });
    }
    void SaveBroken()
    {
        List<string> lines = new();
        foreach (KeyValuePair<string, string> kv in m_broken)
            lines.Add(kv.Key + "\t" + kv.Value + "\t" + Clock.UtcNow.ToLocalTime().Ticks);
        try
        {
            File.WriteAllLines(BrokenPath, lines);
        }
        catch { }
    }
    // A BROKEN mark lasts a restock, its failure count with it (Damian, 2026-10-24: "every single terminal condition
    // should have a reset back tot the top of the circuit"; the third audit: three passing failures, a reagent shortage
    // or a vendor sold out, took a shop or a vendor out for good, and the circuit rested every two minutes forever).
    ValueMap<string, DateTime> m_brokenAt = ValueMap<string, DateTime>.Empty;
    bool Broken(string key)
    {
        lock (m_brokenGate)
        {
            LoadBroken();
            if (!m_broken.ContainsKey(key))
                return false;
            DateTime at;
            if (!m_brokenAt.TryGet(key, out at))
                m_brokenAt = m_brokenAt.Set(key, at = Clock.UtcNow);
            if (Clock.UtcNow < at + s_restockSpan)
                return true;
            m_broken = m_broken.Remove(key);
            m_brokenAt = m_brokenAt.Remove(key);
            SaveBroken();
        }
        lock (m_failCountGate)
            m_failCount = m_failCount.Remove(key);
        Note("BROKEN: " + key + " tried again after a restock");
        return false;
    }
    public string BrokenList()
    {
        lock (m_brokenGate)
        {
            LoadBroken();
            List<string> l = new();
            foreach (KeyValuePair<string, string> kv in m_broken.Ordered(StringComparer.Ordinal))
                l.Add(kv.Key + ": " + kv.Value);
            return l.Count == 0 ? "nothing broken" : string.Join(" ; ", l);
        }
    }
    public string BrokenClear(string key)
    {
        lock (m_brokenGate)
        {
            LoadBroken();
            int n = m_broken.Count;
            m_broken = key == "all" ? m_broken.Clear() : m_broken.Remove(key);
            SaveBroken();
            lock (m_failCountGate)
                m_failCount = key == "all" ? m_failCount.Clear() : m_failCount.Remove(key);
            return "cleared " + (n - m_broken.Count) + "; " + BrokenList();
        }
    }
    // A success starts the count again: the timer and the mark are for failures in a row.
    void FailOk(string key)
    {
        lock (m_failCountGate)
            m_failCount = m_failCount.Remove(key);
    }
    TimeSpan FailSpan(string key, string? why = null)
    {
        int n;
        lock (m_failCountGate)
        {
            n = m_failCount.Get(key, 0) + 1;
            m_failCount = m_failCount.Set(key, n);
        }
        if (n == 1)
            return TimeSpan.Zero;
        if (n == 2)
            return TimeSpan.FromMinutes(FailPauseMinutes);
        lock (m_brokenGate)
        {
            LoadBroken();
            m_broken = m_broken.Set(key, (why ?? "failed").Replace('\t', ' ').Replace('\n', ' '));
            m_brokenAt = m_brokenAt.Set(key, Clock.UtcNow);
            SaveBroken();
        }
        Note("BROKEN: " + key + " failed a third time (" + (why ?? "no answer")
            + "); not tried again for a restock (control port: broken clear " + key + ")");
        return TimeSpan.Zero;
    }
    // Vendors not walked to or asked again (FailSpan): the walk to them failed, or the buy failed with an answer.
    ValueMap<uint, DateTime> m_skipped = ValueMap<uint, DateTime>.Empty; readonly Lock m_skippedGate = new();
    // task: the timer counts the same task's failures on the same vendor (Damian, 2026-10-20: "if it fails the same
    // task on the same target a second time, add the timer").
    void SkipVendor(uint serial, string? why = null, string task = "buy")
    {
        Interlocked.Increment(ref m_vendorSkips);
        lock (m_skippedGate)
            m_skipped = m_skipped.Set(serial,
                Clock.UtcNow.ToLocalTime() + FailSpan(task + " vendor " + serial.ToString("X"), why));
        // No route at all to where the vendor stands is kept across circuits (noroute-<server>.txt: serial, x, y) until
        // the vendor is seen elsewhere (D5 of the 2026-10-19 travel review: Garth at 1386,1602, inside the castle's The
        // Lord's Clothes, cost a recall and two 3 s plans every circuit).
        AtlasNpc? v = VendorNow(serial);
        if (why == null || v == null || why.IndexOf("no way from", StringComparison.OrdinalIgnoreCase) < 0)
            return;
        LoadNoRoute();
        lock (m_noRouteGate)
        {
            m_noRoute = m_noRoute.Set(serial, new[] { v.X, v.Y });
            SaveNoRoute();
        }
        Note("Circuit: no route reaches " + v.Name + " at " + v.X + "," + v.Y + "; not walked to again while there");
    }
    bool Skipped(uint serial)
    {
        DateTime until;
        if (m_skipped.TryGet(serial, out until) && Clock.UtcNow.ToLocalTime() < until)
            return true;
        if (Broken("walk vendor " + serial.ToString("X")) || Broken("buy vendor " + serial.ToString("X")))
            return true;
        LoadNoRoute();
        int[]? at;
        if (!m_noRoute.TryGet(serial, out at))
            return false;
        AtlasNpc? v = VendorNow(serial);
        if (v == null && Atlas != null)
            foreach (AtlasNpc n in Atlas.Shops())
                if (n.Serial == serial)
                    v = n;
        if (v == null || (v.X == at[0] && v.Y == at[1]))
            return true;
        lock (m_noRouteGate)
        {
            m_noRoute = m_noRoute.Remove(serial);
            SaveNoRoute();
        }
        return false;
    }
    Map<uint, int[]> m_noRoute = Map<uint, int[]>.Empty; bool m_noRouteLoaded; readonly Lock m_noRouteGate = new();
    void LoadNoRoute()
    {
        lock (m_noRouteGate)
            LoadOnce(ref m_noRouteLoaded, ServerFile("noroute"),
                line => { string[] t = line.Split('\t'); if (t.Length == 3) m_noRoute = m_noRoute.Set(
                Convert.ToUInt32(t[0], 16), new[] { int.Parse(t[1]), int.Parse(t[2]) }); });
    }
    void SaveNoRoute()
    {
        List<string> l = new();
        foreach (KeyValuePair<uint, int[]> kv in m_noRoute)
            l.Add(kv.Key.ToString("X") + "\t" + kv.Value[0] + "\t" + kv.Value[1]);
        try
        {
            File.WriteAllLines(ServerFile("noroute"), l);
        }
        catch { }
    }
}
