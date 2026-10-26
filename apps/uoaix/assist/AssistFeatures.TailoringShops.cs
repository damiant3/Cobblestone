#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the thread overflow ----------
    // Damian, 2026-10-20: "there is a pile of threads in my house, there is a rune to my house, and the door is
    // unlocked ... it is my thread overflow storage"; "the house rune should only be in the bank box, and never taken
    // out, and so you must be at a bank to go to the house"; "the thread overflow happens when you literally are stuck
    // with too much thread and noone to sell shirts to"; "this morning i used the overflow during the vendor ramp up
    // stage". The rune and the pile's tile are set once by target: the rune in the open bank box, the pile on the house
    // floor.
    public int OverflowTake
    {
        get => Math.Max(0, Cfg.GetInt(Server + "overflow.take", 200));
        set => Cfg.Set(Server + "overflow.take", Math.Max(0, value).ToString());
    }
    // The thread a round of the order buys under which the pile feeds the circuit: 120, a fresh shard's 12 shops
    // (Damian, 2026-10-20: "like 120 thread is the beginning quantity if you do all 12").
    public int OverflowBelow
    {
        get => Math.Max(0, Cfg.GetInt(Server + "overflow.below", 120));
        set => Cfg.Set(Server + "overflow.below", Math.Max(0, value).ToString());
    }
    public uint OverflowRune
    {
        get
        {
            uint v;
            return uint.TryParse(Cfg.Get(Server + "overflow.rune", ""), NumberStyles.HexNumber, null, out v) ? v : 0;
        }
    }
    public bool OverflowPileSet => OverflowPile != null;
    int[]? OverflowPile
    {
        get
        {
            string[] t = Cfg.Get(Server + "overflow.pile", "").Split(',');
            int x, y, z;
            return t.Length == 3 && int.TryParse(t[0], out x) && int.TryParse(t[1], out y) && int.TryParse(t[2], out z)
                ? new[] { x, y, z } : null;
        }
    }
    public string OverflowText()
    {
        int[]? p = OverflowPile;
        return "house rune " + (OverflowRune == 0 ? "not set" : OverflowRune.ToString("X")) + ", pile "
            + (p == null ? "not set" : p[0] + "," + p[1] + "," + p[2]);
    }
    public volatile string OverflowStatus = "";
    const uint OverflowRuneCursor = 0x4F565252, OverflowPileCursor = 0x4F56504C;  // "OVRR" "OVPL"
    public string? OverflowTarget(bool pile)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = (byte)(pile ? 1 : 0);
        Put32(p, 2, pile ? OverflowPileCursor : OverflowRuneCursor);
        string? why = k.ToClient(p, "overflow cursor");
        OverflowStatus = why != null ? "No cursor: " + why
            : pile ? "Target the thread pile on your house floor." : "Target the house rune in your open bank box.";
        return why;
    }
    bool HoldOverflow(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 19)
            return false;
        uint id = U32(p, 2);
        if (id != OverflowRuneCursor && id != OverflowPileCursor)
            return false;
        uint s = U32(p, 7);
        if (id == OverflowRuneCursor)
        {
            AssistItem? it = s == 0 || p[1] != 0 ? null : Item(k, s);
            bool inPack = false;
            if (it != null)
                lock (k.State.Gate)
                    inPack = PackTree(k).Contains(s);
            if (it == null || it.Graphic < 0x1F14 || it.Graphic > 0x1F17 || inPack)
            {
                OverflowStatus = "Not set: target the house rune in the open bank box, not one in the pack.";
                return true;
            }
            Cfg.Set(Server + "overflow.rune", s.ToString("X"));
        }
        else
        {
            int x = (p[11] << 8) | p[12], y = (p[13] << 8) | p[14], z = (sbyte)p[16];
            if (p[1] == 0)
            {
                AssistItem? g = null;
                k.State.Ground.TryGet(s, out g);
                if (g == null)
                {
                    OverflowStatus = "Not set: target the pile on the floor, or the floor tile.";
                    return true;
                }
                x = g.X;
                y = g.Y;
                z = g.Z;
            }
            Cfg.Set(Server + "overflow.pile", x + "," + y + "," + z);
        }
        OverflowStatus = "Thread overflow: " + OverflowText();
        Note(OverflowStatus);
        return true;
    }
    // A bank stop a long walk from here goes by rune (Damian, 2026-10-20: "it should have recalled to a bank instead of
    // the long march in trinsic"): past BankWalkTiles to the town's nearest bank, the carried rune whose recorded
    // landing lies nearest a known bank is recalled first, then walked from; any town's bank serves.
    public const int BankWalkTiles = 20, BankRuneTiles = 15, RegsBankAgainMin = 30;
    // A recall to a bank must save RecallTiles of walking (Damian, 2026-10-26: "the choice was bad", Magincia to
    // Britain to save 48 of 83 tiles): its cast (about 4 s), 15 gp of reagents and 11 of 47 mana (about 55 s to regain)
    // against 100 tiles at 100 to 200 ms a step.
    public const int RecallTiles = 100;
    // walk: tiles to this town's nearest bank, int.MaxValue with none; runeWalk: the best rune's landing to its bank.
    internal static bool BankByRune(int walk, int runeWalk)
    {
        if (runeWalk == int.MaxValue)
            return false;
        return walk == int.MaxValue || walk - runeWalk > RecallTiles;
    }
    DateTime m_regsBankAt = DateTime.MinValue;
    // Recall reagents low and no refill tried within RegsBankAgainMin: a bank with none to give is not asked again each
    // top (the second 2026-10-24 audit, finding 5).
    bool RegsDue(GameLink k)
    {
        return RecallRegsLow(k) && Clock.UtcNow.ToLocalTime() >= m_regsBankAt.AddMinutes(RegsBankAgainMin);
    }
    void RecallNearBank(GameLink k)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        string? city = WalkTown(k);
        int walk = int.MaxValue;
        if (city != null)
            foreach (int[] b in CityBanks(city, px, py))
                walk = Math.Min(walk, Tiles(b[0], b[1], px, py));
        // From home or in the house's town, its bank is walked to, however far (Damian, 2026-10-25: "even to the bank
        // there").
        if (walk <= BankWalkTiles || walk != int.MaxValue && (AtHome(k) || city != null && city == HomeTown(k)))
            return;
        uint best = 0;
        int bestD = int.MaxValue;
        foreach (HeldRune r in PackRunes(k))
        {
            int[]? land = r.Landing;
            if (land == null || RuneBlocked(r.Serial))
                continue;
            string? c = UoRegions.City(land[0], land[1]);
            if (c == null || Banned(c))
                continue;
            foreach (int[] b in CityBanks(c, land[0], land[1]))
            {
                int d = Tiles(b[0], b[1], land[0], land[1]);
                if (d < bestD)
                {
                    bestD = d;
                    best = r.Serial;
                }
            }
        }
        // Any ordinary city rune landing in a town with a bank serves, the walk after (Damian, 2026-10-24: "any normal
        // city rune, not a roof, to a city with a bank, is sufficient for banking. just recall to city and then walk");
        // the roof runes are reserved (runes.reserved) and never offered here.
        if (best == 0 || !BankByRune(walk, bestD) || TripReady(k, false) != null)
            return;
        // Short of the mana for this recall and the one after it (the house rune's, from the bank), the town's own bank
        // is walked to while the mana comes back, however far (Damian, 2026-10-21: "we could have skipped this mana
        // wait"; "nothing wrong with a long run while waiting for mana rebuild": Ocllo, 45 s stood for mana against a
        // 72-tile run).
        int mana;
        lock (k.State.Gate)
            mana = k.State.Mana;
        if (mana < 2 * RecallMana && walk != int.MaxValue)
        {
            Note("Circuit: " + mana + " mana, under two recalls' " + 2 * RecallMana + "; walking " + walk
                + " tiles to the bank here");
            return;
        }
        CircuitStatus = "Recalling to a bank";
        string? w = Recall(best);
        if (w == "the landing is blocked")
            BlockRune(best);
        Note("Circuit: to a bank by the rune " + best.ToString("X") + " (" + bestD + " tiles from its bank, against "
            + (walk == int.MaxValue ? "no bank here" : walk + " to walk") + "): "
            + (w ?? "landed in " + (CityOf(k) ?? "no town")));
    }
    // The bank stop's own tasks for the status, or null when it has none (Damian, 2026-10-24: "the house rune is in
    // pack. not sure why the message says going to get house rune, when its refreshing reagents").
    internal static string? BankErrands(bool rune, bool goldOver, bool regsLow, bool purse)
    {
        List<string> l = new();
        if (rune)
            l.Add("the house rune");
        if (goldOver)
            l.Add("the gold over the bank-over setting");
        if (regsLow)
            l.Add("the recall reagents");
        if (purse)
            l.Add("the purse");
        return l.Count == 0 ? null : string.Join(", ", l);
    }
    // To the pile from a bank: the box opened, the rune recalled where it lies, the pile walked to.
    string? ToOverflow(GameLink k, bool topUp = false)
    {
        uint rune = OverflowRune;
        int[]? pile = OverflowPile;
        if (rune == 0 || pile == null)
            return "the thread overflow is not set (" + OverflowText() + ")";
        // A bank first only with a task there: the house rune in the box, gold over the bank-over setting, the recall
        // reagents low, or the purse to top up. A house rune carried in the pack recalls from where the player stands
        // (Damian, 2026-10-24: "these circuits must NOT recall somewhere without a task to perform"; 13:25:50, a recall
        // to Moonglow's bank and straight on home by the pack's house rune 4686C9F5).
        bool carried;
        lock (k.State.Gate)
            carried = PackTree(k).Contains(rune);
        int gold = PackCount(k, g => g == GoldArt);
        // From the house's town, home is walked to (Damian, 2026-10-26: "run home from brian, if brian is planned to be
        // last on the current outing, also saving mana reagents and idle time"), and needs no rune.
        string? homeTown = HomeTown(k);
        bool walkHome = homeTown != null && (AtHome(k) || CityOf(k) == homeTown);
        string? errands = BankErrands(!carried && !walkHome, gold > BankOver, RegsDue(k), topUp && gold < PurseKeep);
        string? w = null;
        if (errands != null)
        {
            RecallNearBank(k);
            string? city = WalkTown(k);
            if (city == null)
                return "not in a town, and the bank stop is for " + errands;
            w = ToCityBank(k, city, errands);
            if (w != null)
                return "the bank: " + w;
            // A bank stop like any other first (Damian, 2026-10-20: "we went to the bank to recall to the house, and we
            // recalled to the house with 5k gold"): the gold over the purse deposited, then the recall. The loadout is
            // refilled only when a recall reagent is low (ToBanker; Damian, 2026-10-21: "the reagent bag reloading
            // every time is excessive"). The gold over the purse goes in at any stop (Damian, 2026-10-26: "we don't
            // need to cart the gold around, the threshold purpose is prevent too much going to the bank").
            int put;
            if (PackCount(k, g => g == GoldArt) > PurseKeep)
            {
                w = BankDeposit(PurseKeep, out put);
                if (w != null)
                    return "the bank: " + w;
                TopOffRegs(k, city);
            }
            if (topUp)
                TopUpPurse(k);
        }
        // A failed walk home takes the rune.
        if (walkHome && (AtHome(k) || CityOf(k) == homeTown))
        {
            CircuitStatus = "Walking home to the thread pile";
            string? hw = WalkTo(pile[0], pile[1], 1);
            if (hw == null)
                return null;
            Note("Circuit: walking home from " + homeTown + ": " + hw + "; the house rune");
        }
        if (Item(k, rune) == null)
            return "the house rune " + rune.ToString("X") + " is not in the open bank box";
        CircuitStatus = "Recalling to the thread overflow";
        w = Recall(rune);
        if (w != null)
            return "the house rune: " + w;
        CircuitStatus = "Walking to the thread pile";
        w = WalkTo(pile[0], pile[1], 1);
        return w == null ? null : "walking to the pile: " + w;
    }
    // Every thread stack in the pack put on the pile; travel false: the player already stands by it.
    string? OverflowStore(GameLink k, out int stored, bool topUp = false, bool travel = true)
    {
        stored = 0;
        string? w = travel ? ToOverflow(k, topUp) : null;
        if (w != null)
            return w;
        int[]? pile = OverflowPile;
        List<AssistItem> stacks = new();
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && IsThreadArt(it.Graphic))
                    stacks.Add(it);
        }
        foreach (AssistItem t in stacks)
        {
            if (circuitStop)
                return "stopped";
            uint serial = t.Serial;
            int amount = Math.Max(1, t.Amount);
            // Onto the pile's own stack of the same art where one lies, so the floor keeps one stack.
            AssistItem? onto = null;
            lock (k.State.Gate)
                foreach (AssistItem g in k.State.Ground.Values)
                    if (g.Graphic == t.Graphic && g.X == pile![0] && g.Y == pile[1]
                        && (onto == null || g.Amount > onto.Amount))
                        onto = g;
            using (Act(k))
            {
                w = MoveStack(k, serial, amount, "thread", "Putting thread on the overflow pile",
                    () => (onto != null ? k.Drop(serial, onto.Serial)
                    : k.DropGround(serial, pile![0], pile[1], pile[2]))!);
            }
            if (w != null)
                return "putting thread on the pile: " + w;
            Leave(serial);
            stored += amount;
        }
        return null;
    }
    // Up to OverflowTake thread lifted from the pile, as far as the carry limit allows.
    string? OverflowTakeRun(GameLink k, out int taken)
    {
        taken = 0;
        string? w = ToOverflow(k);
        if (w != null)
            return w;
        int[]? pile = OverflowPile;
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        List<AssistItem> stacks = new();
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (IsThreadArt(g.Graphic) && g.X == pile![0] && g.Y == pile[1])
                    stacks.Add(g);
        if (stacks.Count == 0)
            return "no thread on the pile at " + pile![0] + "," + pile[1];
        foreach (AssistItem g in stacks)
        {
            if (circuitStop || taken >= OverflowTake)
                break;
            int amount = Math.Min(Math.Max(1, g.Amount),
                Math.Min(OverflowTake - taken, FitsIn(g.Graphic, CarryLimit(k) - SewWeightRoom - WeightNow(k))));
            if (amount <= 0)
                break;
            uint serial = g.Serial;
            using (Act(k))
            {
                w = MoveStack(k, serial, amount, "thread", "Taking thread from the overflow pile",
                    () => k.Drop(serial, pack)!);
            }
            if (w != null)
                return "taking thread from the pile: " + w;
            taken += amount;
        }
        return null;
    }

    // ---------- the tailoring circuit by shops ----------
    // Damian, 2026-10-20: "at the beginning of the cycle figure out where you are at, and what you have in your
    // backpack ... encumbered? dump thread first, bolts second, cloth third, if that doesn't work, stop the circuit.
    // next is gold over threshold->go to bank. anytime at bank? get reagent load out and gold. did you dump
    // encumberance items? go back and fetch. next->are you in tailor shop? go to tailor shop if not. then process
    // shirts, bolts, cloth, thread, buying sewing kits as needed, buying any thread that shows up for sale ... then
    // decide if you need to bank or move to another tailor shop." The shops in his fixed order ("there is never a need
    // to change order"); the bank waits for Jhelom North or Britain West, banks a walk from their shops, unless it is
    // critical (the pack was too heavy, or the purse is short with no kits and nothing to sew).
    static readonly ImmutableArray<string> s_tailorOrder = [
        "Scholars Cut in Moonglow @4456,1064", "Stitchin' Time in Magincia @3666,2234",
            "Adventurer's Clothing in Trinsic @1985,2837",
        "Shear Pleasure in Skara Brae @652,2184", "A Stitch in Time in Ocllo @3667,2587",
        "Tailor of the Isle in Nujel'm @3767,1263",
        "Adventurer's Needle in Jhelom @1456,4004", "Adventurer's Needle in Jhelom @1360,3784",
        "The Spinning Wheel in Vesper @2964,624", "The Spinning Wheel in Vesper @2840,884",
        "The Right Fit @1556,1656", "The Lord's Clothiers in Britain @1472,1689" ];
    static ImmutableArray<string> s_walkBankShops = [ "Adventurer's Needle in Jhelom @1360,3784",
        "The Lord's Clothiers in Britain @1472,1689" ];
    // Set by the world cases, whose towns are their own.
    internal static ImmutableArray<string> WalkBankShops
    {
        get => s_walkBankShops;
        set => s_walkBankShops = value;
    }
    // The homespun bag's sell order, tailors by first name (Damian, 2026-10-26: "sell down 1 vendor per region to start.
    // so brian first, hes the one we want to respawn the fastest. then slade ... then take your pick for trinsic ...
    // then for vesper ... then the tracy->secondary in brit, then trinsic region secondary, then vesper region
    // secondary, then brit tertiary, then on until you've sold shirts to all the vendors"): each bag goes to the first
    // that can take it. Empty: the shop order.
    public ImmutableArray<string> SellOrder
    {
        get => ImmutableArray.Create(Cfg.Get(Server + "circuit.sell.order", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        set => Cfg.Set(Server + "circuit.sell.order", string.Join(",", value));
    }
    // Under a sell order, a tailor sold down rests this many restocks before she is offered a bag again (Damian,
    // 2026-10-26: "if you sell them down hard enough they don't respawn with great demand"; "2 restocks might be the
    // correct answer to brian and a test circuit with that logic will confirm or deny the assertion").
    public int SellRest
    {
        get => Math.Max(1, Cfg.GetInt(Server + "circuit.sell.rest", 2));
        set => Cfg.Set(Server + "circuit.sell.rest", Math.Max(1, value).ToString());
    }
    // The sell order's tailors that can take a bag now, in its order, each with its shop's index in shops: paying the
    // floor, not passed over, and in this town or surely restocked (ShopIdle's test). why: each named tailor passed
    // and the reason.
    List<Tuple<AtlasNpc, int>> SellReady(GameLink k, List<UoRegions.Shop> shops, out string why)
    {
        List<Tuple<AtlasNpc, int>> l = new();
        List<string> passed = new();
        List<AtlasNpc> tailors = new();
        foreach (string c in TailorCities())
            tailors.AddRange(CityBuyers(c));
        string? here = WalkTown(k);
        foreach (string name in SellOrder)
        {
            List<AtlasNpc> named = tailors.FindAll(n =>
                string.Equals(n.Name.Split(' ')[0], name, StringComparison.OrdinalIgnoreCase));
            if (named.Count == 0)
                passed.Add(name + " not known");
            foreach (AtlasNpc n in named)
            {
                int s = shops.FindIndex(x => x.Holds(n.X, n.Y, 2));
                // PaysFloor first: it loads the rests.
                bool pays = PaysFloor(n);
                DateTime until;
                bool resting;
                lock (m_underFloorGate)
                    resting = m_underFloor.TryGet(n.Serial, out until) && Clock.UtcNow.ToLocalTime() < until;
                if (s < 0)
                    passed.Add(name + " in no shop of the order");
                else if (resting)
                    passed.Add(name + " resting to " + until.ToString("HH:mm"));
                else if (!pays)
                    passed.Add(name + " under the floor");
                else if (Skipped(n.Serial))
                    passed.Add(name + " passed over");
                else if (here != UoRegions.City(n.X, n.Y) && !TripDue(n))
                    passed.Add(name + " not surely restocked");
                else
                    l.Add(Tuple.Create(n, s));
            }
        }
        why = string.Join(", ", passed);
        return l;
    }
    // The shop of the bag's next tailor by the sell order, not among tried; fallback with no sell order, no shirts or no
    // tailor ready.
    int SellNextShop(GameLink k, List<UoRegions.Shop> shops, Set<int> tried, int fallback)
    {
        if (!Homespun || SellOrder.Length == 0 || PackCount(k, IsShirtArt) == 0)
            return fallback;
        List<Tuple<AtlasNpc, int>> ready = SellReady(k, shops, out string why);
        Tuple<AtlasNpc, int>? pick = ready.Find(t => !tried.Contains(t.Item2));
        if (pick == null)
        {
            Note("Circuit: sell order: no tailor ready (" + why + ")");
            return fallback;
        }
        Note("Circuit: sell order: " + pick.Item1.Name.Split(' ')[0] + " in " + UoRegions.Display(shops[pick.Item2])
            + (why.Length > 0 ? " (" + why + ")" : ""));
        return pick.Item2;
    }
    // The bag's first shop from home: the sell order's, else the house's town's (HomeTownShop); -1 for neither.
    int SellOrHomeShop(GameLink k, List<UoRegions.Shop> shops)
    {
        int s = SellNextShop(k, shops, Set<int>.Empty, -1);
        return s >= 0 ? s : HomeTownShop(k, shops);
    }
    // The recalls the plan makes before it reaches a bank: each stop's recalls (recalls[i]) summed up to and including
    // the first stop with a bank (bank[i]), whose bank refills them; int.MaxValue with no bank in the plan.
    internal static int RecallsToBank(IReadOnlyList<int> recalls, IReadOnlyList<bool> bank)
    {
        int n = 0;
        for (int i = 0; i < recalls.Count; i++)
        {
            n += recalls[i];
            if (bank[i])
                return n;
        }
        return int.MaxValue;
    }
    // The reagents planned ahead (Damian, 2026-10-26: "there is no need to detour for regs warning at 8 when in the next
    // 2 normal bounces we will be near a bank ... plan ahead the path the number of regs you have for a recall (min of
    // root, moss, pearl). if it fits normal cycle don't make special trip"): the recalls the sell order's ready tailors
    // take before a shop a walk from a bank (two a stop, out and home; none in the house's town), past skip, plus
    // RegsSpare for a fizzle; int.MaxValue with no sell order or no such shop in it.
    public const int RegsSpare = 1;
    int RegsPlanNeed(GameLink k, UoRegions.Shop? skip)
    {
        if (!Homespun || SellOrder.Length == 0)
            return int.MaxValue;
        List<UoRegions.Shop> shops = OrderShops();
        string? home = HomeTown(k);
        List<int> recalls = new();
        List<bool> bank = new();
        foreach (Tuple<AtlasNpc, int> t in SellReady(k, shops, out string _))
        {
            UoRegions.Shop s = shops[t.Item2];
            if (skip != null && s.Key == skip.Key)
                continue;
            recalls.Add(home != null && UoRegions.City(s.CX, s.CY) == home ? 0 : 2);
            bank.Add(s_walkBankShops.IndexOf(s.Key) >= 0);
        }
        int need = RecallsToBank(recalls, bank);
        return need == int.MaxValue ? need : need + RegsSpare;
    }
    int RegsHeld()
    {
        int[]? regs = RecallRegCounts();
        if (regs == null || regs.Length == 0)
            return int.MaxValue;
        int least = int.MaxValue;
        foreach (int r in regs)
            least = Math.Min(least, r);
        return least;
    }
    // The purse fetched when the pack holds no kits and nothing to sew (Damian, 2026-10-20: "kits are 3 each usually so
    // 3 + 30 or 60 is enough. if they have 108 thread, you need 5 kits and 350 gold or maybe 400. that is the most I
    // could imagine you would actually need"): 3 gold a thread and a kit, a kit for each 22 thread, never over
    // PurseMost.
    public const int ThreadGp = 3, KitGp = 3, ThreadPerKit = 22, PurseMost = 400, FreshThread = 20, ShopRounds = 6;
    int ShopCursor
    {
        get => Cfg.GetInt(Server + "circuit.shop.next", 0);
        set => Cfg.Set(Server + "circuit.shop.next", value.ToString());
    }
    sealed class Dumped
    {
        public uint Serial; public int Graphic, Amount, X, Y, Z;
    }
    readonly List<Dumped> m_dumped = new();
    // Homespun (Damian, 2026-10-21: "its a traveling tailor. this is a homespun tailor. we wont need to loom at the
    // vendor shops. we will just loom at night, make shirts and gather thread at day"; "no shirts at night, just
    // bolts"; "keep them npc tailors bought up and sold down all day long"): the day carries the house's bolts and sews
    // them at the shops, sells every shirt to the floor and buys every thread due, never weaves; the thread goes on the
    // house pile; home only when no shirt, bolt or cloth is left, and thread past the carry limit is passed by.
    public bool Homespun
    {
        get => Cfg.Get(Server + "circuit.homespun", "off") == "on";
        set => Cfg.Set(Server + "circuit.homespun", value ? "on" : "off");
    }
    // The tailoring steps in place of night and day (Damian, 2026-10-24: "remove night and day, instead replacing with
    // which steps of the tailor process we want to enable. buying thread, weaving, and making by either cutting bolts,
    // and/or just using the cut cloth on the ground"), each on unless unticked; a step unticked is never run. With all
    // on, the most finished first: the floor's cut cloth, then its bolts, then the loom ("if there is no more cloth,
    // then we should be looming, and if there is lots of cut cloth we shouldnt be looming it (but we should buy thread
    // and store it)").
    public bool StepBuyThread
    {
        get => TailorStep("buy");
        set => SetTailorStep("buy", value);
    }
    public bool StepWeave
    {
        get => TailorStep("weave");
        set => SetTailorStep("weave", value);
    }
    public bool StepBolts
    {
        get => TailorStep("bolts");
        set => SetTailorStep("bolts", value);
    }
    public bool StepCloth
    {
        get => TailorStep("cloth");
        set => SetTailorStep("cloth", value);
    }
    bool TailorStep(string s)
    {
        return Cfg.Get(Server + "circuit.step." + s, "on") == "on";
    }
    void SetTailorStep(string s, bool on)
    {
        Cfg.Set(Server + "circuit.step." + s, on ? "on" : "off");
    }
    // Where Go starts the shop order: after the last shop visited, or fresh at its first shop (Damian, 2026-10-21: "an
    // option to pickup from the previous run, or start fresh with the vendor list"; "meant as a first run in the
    // circuit").
    public bool ShopsResume
    {
        get => Cfg.Get(Server + "circuit.resume", "off") == "on";
        set => Cfg.Set(Server + "circuit.resume", value ? "on" : "off");
    }
    bool HasSewWork(GameLink k)
    {
        return PackCloth(k).Count > 0 || PackBolts(k).Count > 0 || !Homespun && PackCount(k, IsThreadArt) > 0;
    }
    // The day's home stop (Damian, 2026-10-21: "target the pile of bolts, not pick any up ever. then it makes shirts
    // until pack is full, it should then put down the pile of cloth ... until the bag is full of shirts, the pile of
    // cloth is on the ground, and there is only 10% roughly available room in backpack"): from away, the bank stop and
    // the house rune (ToOverflow, the door by HomeEnter); the thread on the pile; the shirts sewn from the floor's
    // bolts (HomeSew). Null, or why the circuit stops.
    string? HomespunHome(GameLink k, ref int shirts)
    {
        if (!Decide(k, "Home", !circuitStop))
            return null;
        int[]? pile = OverflowPile;
        if (OverflowRune == 0 || pile == null)
            return "homespun: the thread overflow is not set (" + OverflowText() + ")";
        Home? h = HomeAt(pile[0], pile[1]);
        if (h == null)
            return "homespun: no house holds the thread pile at " + pile[0] + "," + pile[1];
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        bool home = InHome(h, px, py);
        string? w;
        // Nothing to do at home with no kit to sew and no thread to store: on to the shops (Damian, 2026-10-21: "next
        // step should have been recall to moonglow tailor shop"; 12:29:18, a Go on the porch with no kit recalled to
        // Minoc's bank).
        if (!home && PackCount(k, g => g == KitArt) == 0 && PackCount(k, IsThreadArt) == 0
            && !(BoxInHome(k, h, s_tailorTools) && Tools.InBox(s_tailorTools).Length > 0))
        {
            return HomeDry + " (no kit and no thread to take home; " + CircuitPack() + ")";
        }
        // No thread to store and no room to sew: no task at home (Damian, 2026-10-24: "these circuits must NOT recall
        // somewhere without a task to perform"; a lap of every shop ending with shirts unsold went home all the same).
        if (!home && NoHomeTask(PackCount(k, IsThreadArt), PackCloth(k).Count, PackCount(k, IsShirtArt), PackNinety(k)))
        {
            Note("Circuit: homespun: the pack too full to sew and no thread to store; not home, on to the shops; "
                + CircuitPack());
            return null;
        }
        Note("Circuit: homespun: " + (home ? "at home" : "home") + "; " + CircuitPack());
        // A trip home that failed is not tried again for a restock: home counts as dry and the shops go on (the
        // 2026-10-24 audit: the top's ToOverflow failed, then HomespunHome cast the same bank stop and recall seconds
        // later, every top).
        if (!home && Clock.UtcNow < m_homeBlockedUntil)
            return HomeDry + " (home not reached; tried again at " + m_homeBlockedUntil.ToLocalTime().ToString("HH:mm")
                + ")";
        if (PackCount(k, IsThreadArt) > 0)
            m_homeDryKnown = false;
        if (!home && m_homeDryKnown)
            return HomeDry + HomeWasDryNote;
        // From near the door the walk in (HomeEnter by HomeCross), not the bank and the house rune.
        if (!home)
        {
            w = Tiles(px, py, h.DoorX, h.DoorY) <= HomeWalkTiles ? WalkTo(pile[0], pile[1], 1, false, h.Floor)
                : ToOverflow(k, true);
            if (w != null && KeepRules.Ends(w))
                return "homespun, home: " + w;
            if (w != null)
            {
                m_homeBlockedUntil = Clock.UtcNow + s_restockSpan;
                return HomeDry + " (home not reached: " + w + ")";
            }
        }
        if (PackCount(k, IsThreadArt) > 0)
        {
            CircuitStatus = "Homespun: to the thread pile";
            w = WalkTo(pile[0], pile[1], 1, false, h.Floor);
            if (w != null)
                return "homespun, to the thread pile: " + w;
            int put;
            w = OverflowStore(k, out put, false, false);
            if (w != null)
                return "homespun, the thread pile: " + w;
            Note("Circuit: homespun: " + put + " thread on the pile");
        }
        int made;
        w = HomeSew(k, h, out made);
        shirts += made;
        if (w != null)
            return "homespun, sewing: " + w;
        // Out of bolts and cloth: the loom work, then the day goes on (Damian, 2026-10-21: "day needs to now do the
        // loom work, then resume").
        // Whatever the pack carries (2026-10-21: "all the vendors are bought out of threads, we should have this start
        // up and loom this pile of thread"): no bolt or cloth on the floor is the loom's turn.
        // The loom only when nothing ticked is left to sew ("if there is no more cloth, then we should be looming"),
        // and only with Weave ticked.
        bool floorStuff;
        bool fc;
        lock (k.State.Gate)
            floorStuff = SewSource(k.State.Ground.Values, (x, y) => InHome(h, x, y), Set<uint>.Empty, StepCloth,
                StepBolts, IsCloth, IsBolt, out fc) != null;
        if (made == 0 && !floorStuff && StepWeave)
        {
            w = HomeWeave(k, h, pile);
            if (w != null && !w.StartsWith("no bolts woven", StringComparison.Ordinal))
                return "homespun, the loom: " + w;
            if (w == null)
            {
                w = HomeSew(k, h, out made);
                shirts += made;
                if (w != null)
                    return "homespun, sewing: " + w;
            }
        }
        if (PackCount(k, IsShirtArt) == 0)
            return HomeDry + (m_homeSewEnd == "no sewing kit" ? HomeNoKitNote : HomeEmptyNote + ")");
        // Shirts carried and nothing sewn or woven: dry all the same, or every round with a sale came home again (the
        // third 2026-10-24 audit, finding 4a).
        if (made == 0)
            return HomeDry + HomeFullNote + PackCount(k, IsShirtArt) + " shirts carried)";
        return null;
    }
    // Nothing sewn with shirts carried: the pack was too full, not home dry, so home is not known dry for it (2026-10-24
    // 21:35: the mark kept the circuit from home for an hour, a pile of about 200 thread there and room for 40 shirts).
    // NoHomeTask keeps a full pack from going home with nothing to store.
    public const string HomeFullNote = " (nothing sewn; ";
    public const string HomeEmptyNote = " (no bolts or thread in the house", HomeNoKitNote = " (no sewing kit)",
        HomeWasDryNote = " (home was dry and no thread is carried)";
    // Home is known dry only when found empty with a kit in hand: a stop with no kit, or home not reached, says
    // nothing of what lies there (the fourth 2026-10-24 audit: a stop with no kit kept the circuit from the floor's
    // cloth after kits were bought).
    internal static bool MarksHomeDry(string w)
    {
        return w != null && (w.StartsWith(HomeDry + HomeEmptyNote, StringComparison.Ordinal)
            || w == HomeDry + HomeWasDryNote);
    }
    // A home stop with nothing sewn or woven: not an end, the shops' thread is next (Damian, 2026-10-24: "every single
    // terminal condition should have a reset"; the round ended there whenever the pile ran out of thread).
    public const string HomeDry = "homespun: no shirts to sell and none sewn";
    DateTime m_homeBlockedUntil = DateTime.MinValue, m_brokeBankAt = DateTime.MinValue;
    // Each shop's last visit (reached by RunShops), for ShopIdle's unread-stock rule.
    ValueMap<string, DateTime> m_shopVisited = ValueMap<string, DateTime>.Empty;
    readonly Lock m_shopVisitedGate = new();
    bool VisitedLately(UoRegions.Shop shop)
    {
        DateTime at;
        return m_shopVisited.TryGet(shop.Key, out at) && Clock.UtcNow < at + s_restockSpan;
    }
    void Visited(UoRegions.Shop shop)
    {
        lock (m_shopVisitedGate)
            m_shopVisited = m_shopVisited.Set(shop.Key, Clock.UtcNow);
    }
    // Home found dry (nothing to sew or weave there), known until thread is carried: neither the top nor the home stop
    // goes home again for nothing (the homespun world, 2026-10-24: home, dry, the shop, nothing, the top, home again,
    // two recalls every two minutes, 175 in three hours).
    bool m_homeDryKnown;
    internal static bool IsHomeDry(string w)
    {
        return w != null && w.StartsWith(HomeDry, StringComparison.Ordinal);
    }
    // The home stop, never an end but a stop: null when home did its work; else why not, with the shops next. A failed
    // step at home (a sew answered by nothing, the loom, the pile) blocks home for a restock, so it is not repeated at
    // every top (the second 2026-10-24 audit's first finding).
    string? HomeOrShops(GameLink k, ref int shirts)
    {
        string? w = HomespunHome(k, ref shirts);
        if (w == null)
        {
            m_homeDryKnown = false;
            return w;
        }
        if (TopEnds(w))
            return w;
        if (MarksHomeDry(w))
            m_homeDryKnown = true;
        else if (!IsHomeDry(w))
            m_homeBlockedUntil = Clock.UtcNow + s_restockSpan;
        Note("Circuit: " + w + "; to the shops"
            + (IsHomeDry(w) ? " for thread"
            : ", home not tried again before " + m_homeBlockedUntil.ToLocalTime().ToString("HH:mm")));
        return w;
    }
    // A task at home for the top's trip there: thread to store, or no shirts and a kit to sew with.
    internal static bool HomeTask(int thread, int shirts, int kits)
    {
        return thread > 0 || shirts == 0 && kits > 0;
    }
    // The house's thread woven at its loom beside the pile (Work: the floor's thread in a safe house, the bolts put on
    // its floor), on the work thread while the circuit waits; a circuit stop stops it. Null once bolts lie on the
    // floor.
    string? HomeWeave(GameLink k, Home h, int[] pile)
    {
        CircuitStatus = "Homespun: to the loom";
        string? w = WalkTo(pile[0], pile[1], 1, false, h.Floor);
        if (w != null)
            return "to the thread pile: " + w;
        Note("Circuit: homespun: no bolts or cloth on the floor; weaving the thread pile");
        if (WorkRunning)
            return "Mill, spin and weave is already running";
        m_weaveRunner.Start(new WorkTask(this));
        while (WorkRunning)
        {
            if (circuitStop || k.Closed)
                m_weaveRunner.Stop();
            CircuitStatus = "Homespun: weaving (" + WorkState + ")";
            Clock.Sleep(500);
        }
        if (circuitStop)
            return "stopped";
        bool bolts = false;
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (IsBolt(g.Graphic) && InHome(h, g.X, g.Y))
                    bolts = true;
        return bolts || PackBolts(k).Count > 0 ? null : "no bolts woven: " + WorkEnd;
    }
    // Nothing to do at home with this pack: no thread to put on the pile, no cut cloth to put down, and shirts filling
    // it past nine tenths, so no room to sew.
    public static bool NoHomeTask(int thread, int clothPiles, int shirts, bool ninety)
    {
        return thread == 0 && clothPiles == 0 && shirts > 0 && ninety;
    }
    // Room for about a tenth more: HomeRoomTenths of the 125 items and of the carry limit. HomeWalkTiles: the door
    // within this is walked to, not recalled to.
    public const int HomeRoomTenths = 9, HomeWalkTiles = 20, ShirtCloth = 8;
    bool PackNinety(GameLink k)
    {
        int held = Inv(k).ItemCount();
        return held >= PackItemsMax * HomeRoomTenths / 10
            || CarryLimit(k) > 0 && WeightNow(k) >= CarryLimit(k) * HomeRoomTenths / 10;
    }
    // Shirts sewn from the house floor's largest cut cloth pile, then its largest bolt stack (StepCloth, StepBolts),
    // targeted where it lies, never lifted; the left-over cloth the
    // server puts in the pack goes down on the floor's cloth pile (ClothDown) whenever the pack is nine tenths full,
    // and once more at the end. Null when the pack is full of shirts or no bolt or kit is left; else why the sewing
    // stopped. uo1998, relay log 2026-10-21 12:15:24 to 12:15:34: the kit aimed at the floor stack (468B997B, 304
    // bolts, target z 7) cut one bolt (304 to 303), and the fancy shirt and 4 left-over cloth went into the pack; the
    // kit then wore out.
    Set<uint> m_floorSpent = Set<uint>.Empty;
    string? HomeSew(GameLink k, Home h, out int made)
    {
        made = 0;
        int failed = 0;
        m_floorSpent = m_floorSpent.Clear();
        m_refusedInARow = 0;
        ManualStart();
        if (!m_loops.Running)
            idleStop = false;
        OpenPackBags(k);
        LoopKind l = TailorKind;
        bool failedLast = false;
        string? end = null;
        // Where a round's time goes, summed per phase (2026-10-24: 1111 ms a shirt at a busy:sew of 1000, 1050 and 1020
        // alike, so the busy is not what holds it): result to the next round, the checks, the wait, the gate past the
        // last use, the wait's end to the use, the use to the picks sent, the picks to the result.
        double[] phases = new double[7];
        int timed = 0;
        DateTime resAt = DateTime.MinValue;
        while (end == null && !circuitStop && !k.Closed)
        {
            DateTime topAt = Clock.UtcNow;
            if (PackNinety(k))
            {
                if (PackCloth(k).Count == 0)
                {
                    end = "full";
                    break;
                }
                string? cw = ClothDown(k, h);
                if (cw != null)
                    return cw;
                if (PackNinety(k))
                {
                    end = "full";
                    break;
                }
            }
            // A kit from the pack, or straight from the tailoring toolbox within reach; with none, the toolbox in this
            // house is walked to and the toolbag topped up from it (AssistTools.md, "Direct use" and "Road counts").
            AssistItem? kit = Item(k, Tools.Spare(s_tailorTools));
            if (kit == null)
            {
                string? bw = HomeToolbox(k, h, s_tailorTools);
                if (bw != null)
                    Note("Circuit: homespun: " + bw);
                kit = Item(k, Tools.Spare(s_tailorTools));
            }
            if (kit == null)
            {
                end = "no sewing kit";
                break;
            }
            // The floor's cut cloth first, the most finished, the largest pile first, then its bolts (Damian,
            // 2026-10-24: "use the most finished bits first... e.g. cut cloth, then bolts"), each while its step is
            // ticked; a pile that gives no result is not aimed at again (L-FUTILITY). A pile under a shirt's cloth is
            // spoiled: uo1998 does not join fresh cloth to it (Damian, 2026-10-21), so it is neither sewn from nor
            // added to (ClothDown).
            AssistItem? bolt;
            bool fromCloth;
            lock (k.State.Gate)
                bolt = SewSource(k.State.Ground.Values, (x, y) => InHome(h, x, y), m_floorSpent, StepCloth, StepBolts,
                    IsCloth, IsBolt, out fromCloth);
            if (bolt == null)
            {
                end = !StepCloth && !StepBolts ? "sewing from cloth and from bolts both unticked"
                    : "no " + (StepCloth && StepBolts ? "cloth or bolts" : StepCloth ? "cloth" : "bolts")
                    + " on the house floor";
                break;
            }
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            if (Tiles(px, py, bolt.X, bolt.Y) > 1)
            {
                CircuitStatus = "Homespun: to the bolts";
                string? ww = WalkTo(bolt.X, bolt.Y, 1, false, h.Floor);
                if (ww != null)
                    return "to the bolts: " + ww;
            }
            // The busy time after the last click as well (the door shut, the key lifted): a sew 0.7 s after the door
            // was answered "You must wait to perform another action." (2026-10-21 13:23:08).
            DateTime preWaitAt = Clock.UtcNow;
            DateTime prevUse = m_lastUse;
            DateTime due = SewDue(m_lastUse, Delay(l), Ms("busy:sew"), failedLast, m_sewPickedAt, clickedAt);
            Wait.Till(Clock, due, () => circuitStop, stepMs: 10);
            DateTime waitedAt = Clock.UtcNow;
            CircuitStatus = "Homespun: sewing from the " + (fromCloth ? "floor cloth" : "bolts");
            Heard mark;
            string? w;
            using (Act(k))
            {
                string[] menu = ProductMenu.Split('|');
                mark = new(k.State);
                m_lastSewn = menu[menu.Length - 1];
                m_lastSewnAt = Clock.UtcNow;
                w = Sew(k, kit.Serial, bolt.Serial, menu.Length, (i, m) => MenuPick(m, menu[i])!);
            }
            DateTime pickedAt = Clock.UtcNow;
            string? res = w == null ? TailorResult(k, l, mark, TailorMs)
                : w == "no cursor" || w == "no menu" ? (SmeltWait(k, l, mark) ?? w) : null;
            if (w == null && resAt != DateTime.MinValue && prevUse != DateTime.MinValue)
            {
                DateTime gotAt = Clock.UtcNow;
                phases[0] += (topAt - resAt).TotalMilliseconds;
                phases[1] += (preWaitAt - topAt).TotalMilliseconds;
                phases[2] += (waitedAt - preWaitAt).TotalMilliseconds;
                phases[3] += (due - prevUse).TotalMilliseconds;
                phases[4] += (m_lastUse - waitedAt).TotalMilliseconds;
                phases[5] += (pickedAt - m_lastUse).TotalMilliseconds;
                phases[6] += (gotAt - pickedAt).TotalMilliseconds;
                timed++;
            }
            resAt = Clock.UtcNow;
            if (mark.Has("cannot hold more items"))
                end = "full";
            if (end != null)
                break;
            if (w != null && res == null)
                return w;
            failedLast = res == "failed";
            if (res == "again")
                continue;
            if (res == "made" || res == "exceptional")
            {
                made++;
                m_refusedInARow = 0;
                continue;
            }
            if (res == "failed")
            {
                failed++;
                m_refusedInARow = 0;
                continue;
            }
            if (res == "stopped")
                return "stopped";
            // The kit's use went unanswered: the server's silence, not the pile's, so the sewing stops there and no pile
            // is set aside (L-FUTILITY; 2026-10-25 20:39:57, a 6 s server stall set the cloth aside and used the kit
            // again on the bolts).
            if (res == "no cursor")
                return "the sewing kit's use gave no cursor";
            if (fromCloth)
            {
                m_floorSpent = m_floorSpent.Add(bolt.Serial);
                Note("Circuit: homespun: the floor cloth " + bolt.Serial.ToString("X") + " (" + bolt.Amount + ") gave "
                    + res + "; set aside");
                continue;
            }
            return "the bolts gave " + res;
        }
        if (end == null)
            return circuitStop ? "stopped" : "the connection closed";
        if (PackCloth(k).Count > 0)
        {
            string? cw = ClothDown(k, h);
            if (cw != null)
                return cw;
        }
        if (timed > 0)
        {
            string[] m = Array.ConvertAll(phases, p => ((int)(p / timed)).ToString());
            Note("Circuit: homespun: sew timing, mean ms over " + timed + ": result to the next round " + m[0]
                + ", checks " + m[1] + ", wait " + m[2] + " (gate " + m[3] + " past the last use), to the use " + m[4]
                + ", use to the picks sent " + m[5] + ", picks to the result " + m[6]);
        }
        m_homeSewEnd = end;
        Note("Circuit: homespun: " + made + " shirts sewn, " + failed + " failed; " + end + "; " + CircuitPack());
        return null;
    }
    // Why the last home sewing ended ("no sewing kit", "full", "no cloth or bolts on the house floor").
    string m_homeSewEnd = "";
    // When the next sew goes: the pace and the server's busy from the kit's last use; after a failure the busy from the
    // last menu pick too; the busy from the last other click (the door, the key).
    public static DateTime SewDue(DateTime lastUse, int paceMs, int busyMs, bool failedLast, DateTime pickedAt,
        DateTime clickedAt)
    {
        DateTime due = lastUse.AddMilliseconds(Math.Max(paceMs, busyMs));
        if (failedLast && pickedAt.AddMilliseconds(busyMs) > due)
            due = pickedAt.AddMilliseconds(busyMs);
        if (clickedAt.AddMilliseconds(busyMs) > due)
            due = clickedAt.AddMilliseconds(busyMs);
        return due;
    }
    // What the home stop sews from next: the floor's largest cut cloth pile of a shirt's cloth or more not set aside,
    // while cloth is ticked, else its largest bolt stack, while bolts are ticked; null for none, fromCloth saying
    // which.
    public static AssistItem? SewSource(IEnumerable<AssistItem> floor, Func<int, int, bool> inHome,
        Set<uint> spent, bool cloth, bool bolts, Func<int, bool> isCloth, Func<int, bool> isBolt,
        out bool fromCloth)
    {
        AssistItem? best = null;
        if (cloth)
            foreach (AssistItem g in floor)
                if (isCloth(g.Graphic) && inHome(g.X, g.Y) && g.Amount >= ShirtCloth && !spent.Contains(g.Serial)
                    && (best == null || g.Amount > best.Amount))
                    best = g;
        fromCloth = best != null;
        if (best == null && bolts)
            foreach (AssistItem g in floor)
                if (isBolt(g.Graphic) && inHome(g.X, g.Y) && (best == null || g.Amount > best.Amount))
                    best = g;
        return best;
    }
    // The profession's toolbox when it lies in this house: walked to, and the bag topped up from it, or the road count
    // taken into the pack when no bag is carried. Null when done or when no toolbox lies here, else why not.
    bool BoxInHome(GameLink k, Home h, Profession p)
    {
        uint box = Tools.Box(p);
        int[]? at = box == 0 ? null : GroundSpot(k, box);
        return at != null && InHome(h, at[0], at[1]);
    }
    string? HomeToolbox(GameLink k, Home h, Profession p)
    {
        if (!BoxInHome(k, h, p))
            return null;
        int[]? at = GroundSpot(k, Tools.Box(p));
        if (!Tools.BoxInReach(p))
        {
            CircuitStatus = "Homespun: to the " + p.Name.ToLowerInvariant() + " toolbox";
            string? w = WalkTo(at![0], at[1], 1, false, h.Floor);
            if (w != null)
                return "to the " + p.Name.ToLowerInvariant() + " toolbox: " + w;
        }
        return Tools.BagCarried(p) ? RefillBag(k, p) : ToolsFromBox(k, p, Math.Max(1, Tools.Road(p)));
    }
    // Where a container lies on the ground, itself or the container holding it; null when it is not on the ground.
    static int[]? GroundSpot(GameLink k, uint s)
    {
        lock (k.State.Gate)
        {
            AssistItem? it;
            for (int i = 0; i < Inventory.ChainDepth && k.State.Items.TryGet(s, out it); i++)
                s = it.Container;
            AssistItem? g;
            return k.State.Ground.TryGet(s, out g) ? new[] { g.X, g.Y, g.Z } : null;
        }
    }
    // The pack's cloth put down beside the player in the house, onto the cloth of its art already on the floor within
    // reach, else on a house floor tile beside the player (south, west, east, north, an empty one first; never the
    // player's own tile, which uo1998 answers by putting the cloth back in the pack, 2026-10-21 12:18:27, nor the
    // door's); a stack that comes back is not put down again (L-FUTILITY).
    string? ClothDown(GameLink k, Home h)
    {
        int x, y, z;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
            z = k.State.Z;
        }
        if (!InHome(h, x, y))
            return "the cloth is put down only inside the house (at " + x + "," + y + ")";
        int[]? tile = null;
        ValueMap<long, int> seen = ValueMap<long, int>.Empty;
        Set<long> taken = Set<long>.Empty;
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                taken = taken.Add(((long)g.X << 16) | (uint)g.Y);
        for (int pass = 0; pass < 2 && tile == null; pass++)
        {
            for (int i = 0; i < s_scrapTiles.GetLength(0) && tile == null; i++)
            {
                int tx = x + s_scrapTiles[i, 0], ty = y + s_scrapTiles[i, 1];
                if (taken.Contains(((long)tx << 16) | (uint)ty) != (pass == 1) || !InHome(h, tx, ty)
                    || tx == h.DoorX && ty == h.DoorY)
                    continue;
                int oz = Open(k, tx, ty, h.Floor, ref seen);
                if (oz != int.MinValue)
                    tile = new[] { tx, ty, oz };
            }
        }

        List<AssistItem> stacks = new();
        lock (k.State.Gate)
        {
            Set<uint> inside = PackTree(k);
            foreach (AssistItem it in k.State.Items.Values)
                if (inside.Contains(it.Container) && IsCloth(it.Graphic))
                    stacks.Add(it);
        }
        foreach (AssistItem c in stacks)
        {
            if (circuitStop)
                return "stopped";
            AssistItem? onto = null;
            lock (k.State.Gate)
                foreach (AssistItem g in k.State.Ground.Values)
                    if (g.Graphic == c.Graphic && InHome(h, g.X, g.Y) && g.Amount >= ShirtCloth
                        && Tiles(g.X, g.Y, x, y) <= VendorReach && (onto == null || g.Amount > onto.Amount))
                        onto = g;
            if (onto == null && tile == null)
                return "no open house floor beside " + x + "," + y + " to put the cloth on";
            if (onto == null)
            {
                x = tile![0];
                y = tile[1];
                z = tile[2];
            }
            uint serial = c.Serial;
            int amount = Math.Max(1, c.Amount);
            string? w;
            using (Act(k))
            {
                w = MoveStack(k, serial, amount, "cloth", "Putting the cloth down",
                    () => (onto != null ? k.Drop(serial, onto.Serial) : k.DropGround(serial, x, y, z))!);
            }
            if (w != null)
                return "putting the cloth down: " + w;
            if (PackCloth(k).ContainsKey(serial))
                return "the cloth " + serial.ToString("X") + " came back into the pack";
            Note("Circuit: homespun: " + amount + " cloth put down"
                + (onto != null ? " on the pile " + onto.Serial.ToString("X") : " at " + x + "," + y));
        }
        return null;
    }
    static int CarryLimit(GameLink k)
    {
        return Inv(k).CarryLimit;
    }
    static int WeightNow(GameLink k)
    {
        return Inv(k).Weight;
    }
    // How many of a stack fit in stones of room (OUO CItem_GetWeight_VT): the tile's weight each, or a stone for each
    // 10 of a tile with none.
    static int FitsIn(int graphic, int room)
    {
        List<UoTiles.Tile> tiles = UoTiles.Items();
        int w = graphic >= 0 && graphic < tiles.Count && tiles[graphic].Weight < 255 ? tiles[graphic].Weight : 0;
        return room <= 0 ? 0 : w > 0 ? room / w : room * 10;
    }
    string? RunShops(GameLink k, ref int bought, ref int shirts, ref int sold, ref int goldIn)
    {
        List<UoRegions.Shop> order = OrderShops();
        foreach (string key in s_tailorOrder)
            if (!order.Exists(s => s.Key == key))
                Note("Circuit: the tailor shop " + key + " has no rune in the tailor runes bag; not visited");
        if (order.Count == 0)
            return "no tailor shop has a rune in the tailor runes bag";
        m_dumped.Clear();
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        // Where the player is: in a shop of the order, that shop; in a town of the order, its first shop; else the next
        // one.
        int at = order.FindIndex(s => s.Holds(px, py, 2));
        if (at < 0)
        {
            string? here = CityOf(k);
            at = order.FindIndex(s => UoRegions.City(s.CX, s.CY) == here);
        }
        if (at < 0)
            at = Math.Max(0, ShopCursor) % order.Count;
        // The round's shops by their place in the order, each counted once (Damian, 2026-10-24: "the jhelom vendors
        // weren't visited ... it was a spin on a lie, there was no visit"): Vesper and the two after it, lapped three
        // times on short mana, read as "every shop visited after 12 shops" with shops 1 to 8 never tried.
        int lapBought = 0, phaseIdle = 0, skipNode = -1;
        bool restockReady = false;
        Set<int> idle = Set<int>.Empty;
        Set<int> lap = Set<int>.Empty;
        // Homespun's day (Damian, 2026-10-21): the shirts sewn at home first, then the order from its first shop,
        // Moonglow's, by rune; home again when the shirts are gone or every shop has been visited.
        // With shirts to sell, the shops first from where the player stands, never home (Damian, 2026-10-24: "it
        // recalled to the bank immediatly with a pack full of shirts and a vendor to sell to. wrong to even leave the
        // tailor shop").
        if (Homespun && PackCount(k, IsShirtArt) > 0)
            Note("Circuit: homespun: " + PackCount(k, IsShirtArt) + " shirts to sell first; the shops from "
                + UoRegions.Display(order[at]));
        bool homeDry = false;
        // Where the order goes on after a detour to the house's town (HomeTownShop), or -1.
        int detourBack = -1;
        if (Homespun && PackCount(k, IsShirtArt) == 0)
        {
            string? hw = HomeOrShops(k, ref shirts);
            if (TopEnds(hw!))
                return hw;
            homeDry = hw != null;
            at = ShopsResume ? Math.Max(0, ShopCursor) % order.Count : 0;
            int hs = SellOrHomeShop(k, order);
            if (hs >= 0 && hs != at)
            {
                detourBack = at;
                at = hs;
            }
            ShopCursor = at;
        }
        else if (Homespun)
        {
            int hs = SellOrHomeShop(k, order);
            if (hs >= 0 && hs != at)
            {
                detourBack = at;
                at = hs;
                ShopCursor = at;
            }
        }
        // The phases (Damian, 2026-10-20: "march through the tailors one at a time trying to sell the pile of shirts
        // ... we find the sale, empty our pouch of that first, then we should have gold. then we dump that except what
        // we need on the return to buy this thread we skip, dump the thread we have, and go back to the node where we
        // first had to skip thread and buy it. then we buy thread from each vendor in the circuit in order again, until
        // full of thread. then either we caught up with a selling tailor and we should make shirts, or we have more
        // thread to buy and we should go store the thread at the house, grab more thread gold, and loop"): sell,
        // restock, make.
        string phase = !Homespun && PackCount(k, IsShirtArt) >= SellBatch && PackFull(k) != null ? "sell" : "make";
        if (phase == "sell")
            Note("Circuit: Sell phase: the pack is full of shirts");
        string? lastPass = null;
        int stalled = 0;
        Set<string> stalledAt = Set<string>.Empty;
        while (!circuitStop && !k.Closed)
        {
            m_circuitPass++;
            UoRegions.Shop shop = order[at];
            string? city = UoRegions.City(shop.CX, shop.CY);
            {
                int sx, sy;
                lock (k.State.Gate)
                {
                    sx = k.State.X;
                    sy = k.State.Y;
                }
                string pass = sx + "," + sy + " " + sold + " " + shirts + " " + bought + " " + PackCount(k, IsShirtArt)
                    + " " + PackCount(k, IsThreadArt);
                stalled = StallCount(lastPass!, pass, stalled);
                lastPass = pass;
                if (stalled == 0)
                    stalledAt = stalledAt.Clear();
                stalledAt = stalledAt.Add(UoRegions.Display(shop));
                if (stalled >= StallPasses(order.Count))
                    return "no progress in " + stalled + " passes ("
                        + string.Join(", ", stalledAt.Ordered(StringComparer.Ordinal)) + ")";
            }
            CircuitNext = phase + ": to " + UoRegions.Display(shop) + ", then " + order[(at + 1) % order.Count].Text;
            Note("Circuit: " + phase + ", shop " + (at + 1) + " of " + order.Count + ", " + UoRegions.Display(shop)
                + "; " + CircuitPack());
            string? w = Unburden(k);
            if (w != null)
                return w;
            w = BankIfDue(k, shop, false);
            if (w != null)
                return w;
            if (m_dumped.Count > 0)
                Fetch(k);
            if (!Homespun && phase == "make" && PackCount(k, IsShirtArt) >= SellBatch && PackFull(k) != null)
            {
                phase = "sell";
                skipNode = -1;
                phaseIdle = 0;
                Note("Circuit: Sell phase: the pack is full of shirts");
            }
            // Sell: every pause cleared, every tailor in order asked, the shirts to the first that offers; no thread
            // bought, the first shop with thread for sale kept for the restock.
            if (phase == "sell")
            {
                if (PackCount(k, IsShirtArt) == 0)
                {
                    phase = "restock";
                    restockReady = false;
                    phaseIdle = 0;
                    Note("Circuit: Restock phase: the shirts are sold");
                    continue;
                }
                if (phaseIdle == 0)
                    ResetCooldowns(false);
                w = ReachShop(k, shop, out bool reachedSell);
                if (w != null)
                    return w;
                if (!reachedSell)
                {
                    phaseIdle++;
                    if (phaseIdle > order.Count)
                        return Stuck(k, order.Count);
                    at = (at + 1) % order.Count;
                    ShopCursor = at;
                    continue;
                }
                int soldBefore = sold;
                w = SellShop(k, shop, city!, ref sold, ref goldIn);
                if (w != null)
                    return w;
                if (skipNode < 0 && ThreadCandidates(city!).Exists(n => shop.Holds(n.X, n.Y, 2) && ThreadDue(n)))
                {
                    skipNode = at;
                    Note("Circuit: thread for sale in " + UoRegions.Display(shop)
                        + " skipped; the restock starts here");
                }
                phaseIdle = sold > soldBefore ? 1 : phaseIdle + 1;
                if (phaseIdle > order.Count)
                    return Stuck(k, order.Count);
                at = (at + 1) % order.Count;
                ShopCursor = at;
                continue;
            }
            // Restock: the gold banked but the purse, the thread held stored at the house, then thread bought in order
            // from the skipped shop until the pack is full of it; a full pack stored and the buying goes on; a tailor
            // that buys shirts reached with thread in the pack, make.
            if (phase == "restock")
            {
                if (!restockReady)
                {
                    restockReady = true;
                    string? here = CityOf(k);
                    if (PackCount(k, IsThreadArt) > 0 && OverflowRune != 0 && OverflowPile != null)
                    {
                        int put;
                        string? ow = OverflowStore(k, out put, true);
                        Note("Circuit: the overflow pile: " + (ow ?? put + " thread stored"));
                    }
                    else if (here != null)
                    {
                        string? bw = BankHere(k, here);
                        if (bw != null)
                            Note("Circuit: banking in " + here + ": " + bw);
                        else
                            TopUpPurse(k);
                    }
                    if (skipNode >= 0)
                        at = skipNode;
                    continue;
                }
                w = ReachShop(k, shop, out bool reachedRestock);
                if (w != null)
                    return w;
                if (!reachedRestock)
                {
                    phaseIdle++;
                    if (phaseIdle >= order.Count)
                    {
                        phase = "make";
                        idle = idle.Clear();
                        Note("Circuit: Make phase: no thread left to buy");
                    }
                    at = (at + 1) % order.Count;
                    ShopCursor = at;
                    continue;
                }
                int boughtBefore0 = bought;
                bool did0;
                w = BuyShopThread(k, shop, city!, ref bought, out did0);
                if (w != null)
                    return w;
                List<AtlasNpc> buyers = CityBuyers(city!).FindAll(n => shop.Holds(n.X, n.Y, 2) && PaysFloor(n));
                if (PackCount(k, IsThreadArt) > 0 && buyers.Count > 0)
                {
                    phase = "make";
                    idle = idle.Clear();
                    Note("Circuit: Make phase: " + buyers[0].Name + " in " + UoRegions.Display(shop) + " buys shirts");
                    continue;
                }
                if (PackFull(k) != null || FitsIn(0x0FA0, CarryLimit(k) - SewWeightRoom - WeightNow(k)) <= 0)
                {
                    if (OverflowRune != 0 && OverflowPile != null)
                    {
                        int put;
                        string? ow = OverflowStore(k, out put, true);
                        Note("Circuit: the overflow pile: " + (ow ?? put + " thread stored"));
                    }
                    else
                    {
                        phase = "make";
                        continue;
                    }
                }
                phaseIdle = bought > boughtBefore0 ? 0 : phaseIdle + 1;
                if (phaseIdle >= order.Count)
                {
                    phase = "make";
                    idle = idle.Clear();
                    Note("Circuit: Make phase: no thread left to buy");
                }
                at = (at + 1) % order.Count;
                ShopCursor = at;
                continue;
            }
            // A shop with nothing to do is passed over without the recall (Damian, 2026-10-20: Magincia, Trinsic and
            // Skara Brae recalled to within 7 s, every tailor there passed over).
            string? idleWhy = ShopIdle(k, shop, city!);
            int boughtBefore = bought;
            if (idleWhy != null)
            {
                Note("Circuit: " + UoRegions.Display(shop) + " passed over: " + idleWhy);
                idle = idle.Add(at);
            }
            else
            {
                w = ReachShop(k, shop, out bool reachedMake);
                if (w != null)
                    return w;
                if (!reachedMake)
                {
                    idle = idle.Add(at);
                }
                else
                {
                    Visited(shop);
                    int before = sold + shirts + bought;
                    w = ShopWork(k, shop, city!, order[(at + 1) % order.Count].Text, ref bought, ref shirts, ref sold,
                        ref goldIn);
                    if (w != null)
                        return w;
                    if (sold + shirts + bought > before)
                        idle = idle.Clear();
                    else
                        idle = idle.Add(at);
                    w = BankIfDue(k, shop, true);
                    if (w != null)
                        return w;
                }
            }
            // The round's thread (Damian, 2026-10-20: "120 thread is the beginning quantity if you do all 12 ... after
            // checking all 12 and not getting much, you should just sew what you got, then go fetch thread from the
            // storage to keep working while the restock timer runs"): a round of the order, passed-over shops counted,
            // that bought under OverflowBelow with nothing left to sew fetches from the pile, before a round with
            // nothing done is the rest.
            lap = lap.Add(at);
            lapBought += bought - boughtBefore;
            bool homeOpen = Clock.UtcNow >= m_homeBlockedUntil;
            if (PackCount(k, IsThreadArt) > 0 && homeOpen)
                homeDry = false;
            // The town finished before home: a next shop in the same town with work is walked to first (Damian,
            // 2026-10-24: "jhelom and brit you should never recall between the shops"; the three-town world went Ella,
            // home, Kimmie, home, recalling back to the shop forty tiles from the last one after every restock).
            // Whatever the next shop's work: one with none is passed over where the player stands, so taking it costs
            // nothing, and asking ShopIdle here made the choice depend on when a restock landed (57 recalls under
            // coverage).
            int nextShop = (at + 1) % order.Count;
            bool townLeft = nextShop != at && UoRegions.City(order[nextShop].CX, order[nextShop].CY) == city
                && !lap.Contains(nextShop);
            // No wait for a vendor's restock here (Damian, 2026-10-24: "no reason to wait for others ... the whole
            // circuit should only wait if all vendors refuse to buy anymore, sell anymore, and there is no work to be
            // done at the Astronomical Society"; "selling shirts is always more important than waiting for anything,
            // especially thread"): the round goes on, and the rest is the circuit's only wait.
            if (Homespun && !homeDry && homeOpen && !townLeft
                && HomespunGoesHome(PackCount(k, IsShirtArt), lap.Count, idle.Count, order.Count))
            {
                Note("Circuit: homespun: "
                    + (PackCount(k, IsShirtArt) == 0 ? "the shirts are gone" : "every shop visited") + " after "
                    + lap.Count + " shops");
                // Each bag of shirts goes on from the shop after this one (at + 1), so every shop is reached over the
                // cycles; the Resume setting decides only where Go starts (Damian, 2026-10-21: "meant as a first run in
                // the circuit ... not every bag of shirts").
                int nextAt = (at + 1) % order.Count;
                w = HomeOrShops(k, ref shirts);
                if (TopEnds(w!))
                    return w;
                homeDry = w != null;
                int hs = SellOrHomeShop(k, order);
                detourBack = hs >= 0 && hs != nextAt ? nextAt : -1;
                at = hs >= 0 ? hs : TwoTailorIfShort(k, order, nextAt, Set<int>.Empty);
                ShopCursor = at;
                idle = idle.Clear();
                lap = lap.Clear();
                lapBought = 0;
                continue;
            }
            if (lap.Count >= order.Count)
            {
                if (!Homespun && lapBought < OverflowBelow && !HasSewWork(k) && OverflowRune != 0
                    && OverflowPile != null && PackFull(k) == null && Decide(k, "OverflowTake", !circuitStop))
                {
                    Note("Circuit: a round of " + order.Count + " shops bought " + lapBought + " thread, under "
                        + OverflowBelow + "; the overflow pile");
                    int got;
                    string? ow = OverflowTakeRun(k, out got);
                    bought += got;
                    Note("Circuit: the overflow pile: " + (ow ?? got + " thread taken"));
                    if (got > 0)
                        idle = idle.Clear();
                }
                lap = lap.Clear();
                lapBought = 0;
            }
            // A whole round of the order with nothing sold, sewn or bought: nothing is left to do anywhere. Thread held
            // then is the overflow: called out, and put on the overflow pile.
            if (idle.Count >= order.Count)
                return Stuck(k, order.Count);
            int after = detourBack >= 0 ? detourBack : (at + 1) % order.Count;
            detourBack = -1;
            after = SellNextShop(k, order, idle, after);
            at = Homespun ? TwoTailorIfShort(k, order, after, idle) : after;
            ShopCursor = at;
        }
        return m_circuitAlarm ?? (k.Closed ? "the connection closed" : "stopped");
    }
    // Homespun goes home when the shirts are gone, or after a round of every shop in which something was done; a round
    // with nothing sold, sewn or bought is the rest (Stuck), never home: home with a pack of shirts has no task, and
    // going on from it looped the order at 40 ms a lap with 14 mana (Damian, 2026-10-24: "the circuit is infinite
    // looping", 16:09). The tailoring circuit never ends of itself (Damian, 2026-10-24: "this circuit should be able to
    // run continuously, forever, with no stops. every single terminal condition should have a reset back tot the top of
    // the circuit"; "you are literally just stringing them together in the circuit"; "a simple state machine that has
    // no ambiguity and no problem that 2 minute wait and recall home wouldnt solve"): whatever ends the shop loop is
    // noted, then TopWaitMs and mana for three recalls, then home (homespun: the house by its rune, ToOverflow; a
    // failure there is noted, never an end), then the top. The avoidances stay, each running out on its own span, so a
    // vendor that gave no answer is not asked again every two minutes (L-FUTILITY). Only a stop ends it: the player's
    // stop or takeover, a closed link, the fail-safe, death (KeepRules.Ends), or an alarm (attacked, a GM near).
    public const int TopWaitMs = 120000;
    // The rest ends when the soonest held tailor comes due, if sooner than TopWaitMs (Damian, 2026-10-25: "better than
    // sitting in the hut waiting for nothing"; Lynn held to 16:33:32 while the circuit rested): never under
    // RestFloorSeconds from now. soonestDue in UTC, DateTime.MaxValue for none.
    public const int RestFloorSeconds = 10;
    internal static DateTime RestEnd(DateTime now, int waitMs, DateTime soonestDue)
    {
        DateTime end = now.AddMilliseconds(waitMs), floor = now.AddSeconds(RestFloorSeconds);
        if (soonestDue >= end)
            return end;
        return soonestDue > floor ? soonestDue : floor;
    }
    // When a trip to a tailor is worth it (local time): once she has surely restocked since she was last full or under
    // the floor (her last offer under it, or a hold set at it), the homespun circuit's; MinValue with no such answer.
    // A check where the player already is goes at her hold's end (ShopIdle).
    DateTime TripDueAt(AtlasNpc n)
    {
        if (!Homespun)
            return DateTime.MinValue;
        LoadOffers();
        Tuple<int, DateTime>? o;
        if (!m_sellOffers.TryGet(n.Serial, out o))
            return DateTime.MinValue;
        SellItem? shirt = SellList().Find(i => Array.Exists(i.Arts, IsShirtArt));
        bool full = shirt != null && o.Item1 < VendorFloor(n.Serial, shirt.Arts[0]);
        DateTime u;
        lock (m_underFloorGate)
            if (m_underFloor.TryGet(n.Serial, out u) && u >= o.Item2)
                full = true;
        return full ? SureAfter(n.Serial, o.Item2) : DateTime.MinValue;
    }
    bool TripDue(AtlasNpc n)
    {
        return Clock.UtcNow.ToLocalTime() >= TripDueAt(n);
    }
    // When the soonest held tailor of the tailor towns comes due for a trip: her under-floor hold, her refusal's
    // freshness running out, or her sure restock, the latest (local time); DateTime.MaxValue with none held.
    DateTime SoonestTailorDue()
    {
        DateTime now = Clock.UtcNow.ToLocalTime(), best = DateTime.MaxValue;
        SellItem? shirt = SellList().Find(i => Array.Exists(i.Arts, IsShirtArt));
        LoadOffers();
        foreach (string c in TailorCities())
            foreach (AtlasNpc n in CityBuyers(c))
            {
                if (PaysFloor(n) && TripDue(n))
                    continue;
                DateTime due = TripDueAt(n), u;
                lock (m_underFloorGate)
                    if (m_underFloor.TryGet(n.Serial, out u))
                        due = u;
                Tuple<int, DateTime>? o;
                if (shirt != null && m_sellOffers.TryGet(n.Serial, out o)
                    && o.Item1 < VendorFloor(n.Serial, shirt.Arts[0]))
                {
                    DateTime f = Homespun ? RestockAfter(n.Serial, o.Item2) : o.Item2.AddMinutes(SurveyFreshMinutes);
                    if (f > due)
                        due = f;
                }
                if (due > now && due < best)
                    best = due;
            }
        return best;
    }
    internal static bool TopEnds(string end)
    {
        return KeepRules.Ends(end);
    }
    // Null at the top, or why the circuit ends after all.
    string? CircuitTop(GameLink k, string end)
    {
        DateTime due = SoonestTailorDue();
        DateTime until = RestEnd(Clock.UtcNow, TopWaitMs, due == DateTime.MaxValue ? due : due.ToUniversalTime());
        Note("Circuit: " + end + "; back to the top of the circuit in " + (int)(until - Clock.UtcNow).TotalSeconds
            + " s, by way of home");
        while (true)
        {
            if (circuitStop || k.Closed)
                return m_circuitAlarm ?? (k.Closed ? "the connection closed" : "stopped");
            int mana;
            lock (k.State.Gate)
                mana = k.State.Mana;
            // Mana for three recalls, or ten minutes more at most (a character whose most is under it waits no longer).
            if (Clock.UtcNow >= until && (mana >= 3 * RecallMana || Clock.UtcNow >= until.AddMinutes(10)))
                break;
            CircuitStatus = "Back to the top of the circuit at " + until.ToLocalTime().ToString("HH:mm:ss") + " ("
                + mana + " mana)";
            Clock.Sleep(1000);
        }
        int[]? pile = OverflowPile;
        Home? house = pile == null ? null : HomeAt(pile[0], pile[1]);
        int hx, hy;
        lock (k.State.Gate)
        {
            hx = k.State.X;
            hy = k.State.Y;
        }
        if (house != null && InHome(house, hx, hy))
        {
            Note("Circuit: the top of the circuit: at home");
        }
        // Home only with a task there (HomeTask: thread to store, or no shirts and a kit to sew); shirts to sell and
        // nothing to store go straight to the shops ("these circuits must NOT recall somewhere without a task to
        // perform"). Thread carried home is put on the pile here: the top's RunShops with shirts aboard sells first and
        // never stores it.
        else if (Homespun && OverflowRune != 0 && pile != null && Clock.UtcNow >= m_homeBlockedUntil
            && HomeTask(PackCount(k, IsThreadArt), PackCount(k, IsShirtArt),
            m_homeDryKnown ? 0 : PackCount(k, g => g == KitArt)))
        {
            CircuitStatus = "Back to the top: home";
            string? hw = ToOverflow(k);
            if (TopEnds(hw!) || circuitStop || k.Closed)
                return m_circuitAlarm ?? hw ?? "stopped";
            if (hw == null && PackCount(k, IsThreadArt) > 0)
            {
                int put;
                hw = OverflowStore(k, out put, false, false);
                if (hw == null)
                    Note("Circuit: homespun: " + put + " thread on the pile");
            }
            if (TopEnds(hw!) || circuitStop || k.Closed)
                return m_circuitAlarm ?? hw ?? "stopped";
            if (hw != null)
                m_homeBlockedUntil = Clock.UtcNow + s_restockSpan;
            Note("Circuit: the top of the circuit: home"
                + (hw == null ? ""
                : " not reached (" + hw + "); not tried again before "
                + m_homeBlockedUntil.ToLocalTime().ToString("HH:mm")));
        }
        else
        {
            Note("Circuit: the top of the circuit");
        }

        return null;
    }
    // The shop loop's guard against a lap with no progress, whatever its cause (Damian, 2026-10-24: "its fucking
    // broke", after loops at 16:09 and 18:38 that each fix's own case passed): a pass that starts with the player on
    // the same tile and the same sold, sewn, bought, shirt and thread counts as the pass before is a stalled pass; two
    // rounds' worth in a row ends the circuit with an alarm. A real round of passed-over shops is one round's worth and
    // ends in Stuck first.
    internal static int StallCount(string last, string now, int count)
    {
        return last != null && last == now ? count + 1 : 0;
    }
    internal static int StallPasses(int shops)
    {
        return 2 * shops;
    }
    internal static bool HomespunGoesHome(int shirts, int lapShops, int idle, int shops)
    {
        return shirts == 0 || lapShops >= shops && idle < shops;
    }
    // Short of the mana for three recalls (to the shop, to a bank, home), the next shop is the first, from at on, of a
    // town with two shops in the order, where the walk between them and to the bank rebuilds the mana (Damian,
    // 2026-10-21: "plan ahead to put yourself in a town with 2 tailors"), Jhelom among them (Damian, 2026-10-24: "same
    // with jhelom"). Only a town whose shops are walked (s_walkShopTowns): Vesper is recalled to in the order like every
    // other town (Damian, 2026-10-25: "a character should recall to vesper every round yes, just like all the rest. it
    // isn't special, except that the two shops in the town are far apart").
    int TwoTailorIfShort(GameLink k, List<UoRegions.Shop> order, int at, Set<int> tried)
    {
        int mana;
        lock (k.State.Gate)
            mana = k.State.Mana;
        if (mana >= 3 * RecallMana)
            return at;
        string?[] towns = order.ConvertAll(s => UoRegions.City(s.CX, s.CY)).ToArray();
        int j = TwoTailorPick(towns, at, RoundWork(tried, i => ShopIdle(k, order[i], towns[i]!) == null));
        if (j != at)
            Note("Circuit: " + mana + " mana, under three recalls' " + 3 * RecallMana + "; on to " + order[j].Text
                + ", a town of two tailors");
        return j;
    }
    // The first shop, from at on, of a town with two shops in the order (towns: each shop's town) and work in one
    // of them; at when none. A town whose every shop has nothing to do rebuilds no mana: no walk happens there (Vesper,
    // 16:08 to 16:09, its two shops passed over in 10 ms while shops 1 to 8 were never tried).
    // Work for the jump: a shop the round has not already tried with nothing done (tried, the round's idle shops) whose
    // own check finds work. The Lord's Clothiers' check found a tailor paying while its visit sold nothing, and the
    // jump sent the order back to The Right Fit, shops 11 and 12 lapped every 0.4 s (18:38:00 to 18:38:11).
    internal static Func<int, bool> RoundWork(Set<int> tried, Func<int, bool> shopWork)
    {
        return i => !tried.Contains(i) && shopWork(i);
    }
    internal static int TwoTailorPick(IReadOnlyList<string?> towns, int at, Func<int, bool> work)
    {
        for (int i = 0; i < towns.Count; i++)
        {
            int j = (at + i) % towns.Count;
            string? c = towns[j];
            if (c == null || s_walkShopTowns.IndexOf(c) < 0 || towns.Count(t => t == c) < 2)
                continue;
            if (j > 0 && towns[j - 1] == c && i > 0)
                continue;
            bool any = false;
            for (int s = 0; s < towns.Count; s++)
                if (towns[s] == c && work(s))
                {
                    any = true;
                    break;
                }
            if (!any)
                continue;
            return j;
        }
        return at;
    }
    // A round of every shop with nothing sold, sewn or bought: thread held then is stored, and called the overflow only
    // with shirts carried, the one case where nobody buying shirts is shown (2026-10-25 20:43: 70 thread, no shirts,
    // home blocked by a failed sew, called an overflow).
    // Then the rest (Damian, 2026-10-20: "rest means we have time to do other things, like get your reagent bag loaded
    // out again, deposit the gold, prep for respawn"): the thread stored at the house by way of a bank stop, else a
    // bank stop here; either way the gold deposited, the loadout refilled and the purse topped up.
    string Stuck(GameLink k, int shops)
    {
        int held = PackCount(k, IsThreadArt);
        if (held > 0 && PackCount(k, IsShirtArt) > 0)
        {
            string call = "THREAD OVERFLOW: " + held + " thread and nobody in the " + shops + " shops buys shirts";
            Note("Circuit: " + call);
            if (!k.Closed)
                k.Tell(call, DropHue);
        }
        if (held > 0 && OverflowRune != 0 && OverflowPile != null && Clock.UtcNow >= m_homeBlockedUntil
            && Decide(k, "OverflowStore", !circuitStop))
        {
            int put;
            string? ow = OverflowStore(k, out put, true);
            if (ow != null && !TopEnds(ow))
                m_homeBlockedUntil = Clock.UtcNow + s_restockSpan;
            Note("Circuit: the overflow pile: " + (ow ?? put + " thread stored"));
        }
        // A bank stop only with a task there (Damian, 2026-10-24: "these circuits must NOT recall somewhere without a
        // task to perform"): the round then goes back to the top every two minutes, and a bank stop each time drained
        // the reagents.
        else if (Decide(k, "Rest",
            !circuitStop && (PackCount(k, g => g == GoldArt) > BankOver || RegsDue(k))
            && !Broken("banking in " + CityOf(k))))
        {
            RecallNearBank(k);
            string? city = WalkTown(k);
            if (city == null)
            {
                Note("Circuit: rest: not in a town with a bank");
            }
            else
            {
                string? bw = BankHere(k, city);
                if (bw != null)
                {
                    Note("Circuit: rest: banking in " + city + ": " + bw);
                    FailSpan("banking in " + city, bw);
                }
                else
                {
                    FailOk("banking in " + city);
                    TopUpPurse(k);
                }
            }
        }
        return "resting: a round of all " + shops + " shops had nothing to sell, sew or buy";
    }
    // Why the shop has nothing to do, or null: shirts and a tailor known there that pays the floor, thread due there
    // with room for it, thread to weave or cloth to sew with room, or no tailor of it known yet.
    string? ShopIdle(GameLink k, UoRegions.Shop shop, string city)
    {
        List<AtlasNpc> known = CityBuyers(city).FindAll(n => shop.Holds(n.X, n.Y, 2));
        // No shirt buyer known: work only while no keeper's stock there has been read yet (a first visit learns it); a
        // shop whose keepers are read and buy no shirts is judged by its thread like any other (the homespun world,
        // 2026-10-24: The Stitch recalled to every two minutes for nothing).
        // A shop whose stock reads as unread (never read, or read longer ago than a restock) is work once a restock: a
        // visit that asked nothing left it unread, and every top recalled to it again (the three-town world, 151
        // recalls).
        if (known.Count == 0
            && (Atlas == null || !Atlas.Shops().Exists(n => shop.Holds(n.X, n.Y, 2) && n.Items.Count > 0))
            && !VisitedLately(shop))
            return null;
        bool room = PackFull(k) == null;
        // Away from the tailor's town a trip waits for her sure restock (TripDue); in it, her hold's end will do.
        bool here = WalkTown(k) == city;
        if (PackCount(k, IsShirtArt) > 0
            && known.Exists(n => PaysFloor(n) && !Skipped(n.Serial) && (here || TripDue(n))))
            return null;
        // Thread alone is no reason to go to a shop while homespun carries shirts: buying thread comes after selling
        // them (Damian, 2026-10-25: "buying thread is secondary to selling shirts in pack, secondary to making shirts
        // with cloth, secondary to making cloth from already purchased thread"); a shop visited to sell still buys it.
        if (room && StepBuyThread && !(Homespun && PackCount(k, IsShirtArt) > 0)
            && ThreadCandidates(city).Exists(n =>
            shop.Holds(n.X, n.Y, 2) && ThreadDue(n) && !ShownWithout(n.Serial, IsThreadArt) && !ThreadOut(n.Serial)
            && !PurseShortNow(k, n.Serial)))
            return null;
        // Homespun sews only at home: cloth or bolts carried are no work at a shop (the fourth 2026-10-24 audit).
        if (room && !Homespun && HasSewWork(k))
            return null;
        return PackCount(k, IsShirtArt) > 0 ? "its tailors are passed over this cycle and no thread is due"
            : "no thread is due and nothing is left to sew";
    }
    // Too heavy to walk or recall (past strength * 4 + 30): the pack's thread, then its bolts, then its cloth, the
    // largest pile first, put down under the player's feet until it is under; gold is never put down (Damian,
    // 2026-10-20: "you should also stop the circuit if you are going to drop gold").
    string? Unburden(GameLink k)
    {
        int limit = CarryLimit(k);
        if (limit == 0 || WeightNow(k) <= limit)
            return null;
        if (!Decide(k, "Unburden", true))
            return null;
        List<AssistItem> piles = new();
        foreach (Func<int, bool> kind in new Func<int, bool>[] { IsThreadArt, IsBolt, IsCloth })
        {
            List<AssistItem> one = new();
            lock (k.State.Gate)
            {
                Set<uint> inside = PackTree(k);
                foreach (AssistItem it in k.State.Items.Values)
                    if (inside.Contains(it.Container) && kind(it.Graphic))
                        one.Add(it);
            }
            one.Sort((a, b) => b.Amount.CompareTo(a.Amount));
            piles.AddRange(one);
        }
        foreach (AssistItem p in piles)
        {
            if (circuitStop || WeightNow(k) <= limit)
                break;
            int z;
            lock (k.State.Gate)
                z = k.State.Z;
            int amount = Math.Max(1, p.Amount);
            uint serial = p.Serial;
            int graphic = p.Graphic;
            // A tile beside, never the player's own, which uo1998 answers by putting it back (the third 2026-10-24
            // audit).
            int[]? at;
            string? w = PutDownBeside(k, serial, amount, UoTiles.Name(graphic), out at);
            if (w != null)
                return "too heavy to walk (" + WeightNow(k) + " of " + limit + " stones) and the "
                    + UoTiles.Name(graphic) + " did not go down: " + w;
            int x = at![0], y = at[1];
            Leave(serial);
            lock (m_dumped)
                m_dumped.Add(new Dumped { Serial = serial, Graphic = graphic, Amount = amount, X = x, Y = y, Z = z });
            Note("Circuit: " + amount + " " + UoTiles.Name(graphic) + " put down at " + x + "," + y + " to walk");
        }
        if (WeightNow(k) > limit)
            return "too heavy to walk (" + WeightNow(k) + " of " + limit
                + " stones) with no thread, bolts or cloth left to put down; gold is never put down";
        return null;
    }
    // The bank (Damian, 2026-10-20: "If i ever need to bank and can wait, i do, but if its critical you can walk to the
    // bank in whatever town you are in"): the gold over BankOver at a walk-bank shop's town; anywhere when critical. At
    // every bank stop the reagent loadout, and the purse only when the pack holds no kits and nothing to sew. leaving:
    // the shop's work is done and the circuit moves on.
    string? BankIfDue(GameLink k, UoRegions.Shop shop, bool leaving)
    {
        string? city = CityOf(k);
        if (city == null)
            return null;
        int gold = PackCount(k, g => g == GoldArt);
        // Shirts carried are the purse (Damian, 2026-10-21: "next step should have been recall to moonglow tailor shop
        // and sell shirts, buy kits and threads"; "a pack fancy shirts are always fungible even not homespun"): no bank
        // for gold while shirts are carried.
        // A broke bank stop is not repeated within a restock (the 2026-10-24 audit: a box whose top-level gold could
        // not fill the purse sent every pass, passed-over shops included, to the bank again; L-FUTILITY).
        bool broke = PackCount(k, g => g == KitArt) == 0 && !HasSewWork(k) && gold < PurseWant(city)
            && PackCount(k, IsShirtArt) == 0 && Clock.UtcNow >= m_brokeBankAt + s_restockSpan;
        // The reagent warning on (a recall reagent under RecallLow) makes this town's bank due, where ToBanker refills
        // the loadout (Damian, 2026-10-21: "watching the warning, and only getting regs then"; 13:46:59, 3 recalls left
        // four shops from the bank stop on the way home).
        // A refill that left the reagents low (the bank holds none) is not tried again for RegsBankAgainMin
        // (L-FUTILITY).
        int[]? regs = RecallRegCounts();
        // Under a sell order the warning makes no trip while the reagents reach the plan's next shop by a bank, and that
        // shop, left, refills them when they would not reach the one after it.
        bool atWalkBank = s_walkBankShops.IndexOf(shop.Key) >= 0 && UoRegions.City(shop.CX, shop.CY) == city;
        int held = RegsHeld();
        bool regsLow = RecallRegsLow(k) && Clock.UtcNow.ToLocalTime() >= m_regsBankAt.AddMinutes(RegsBankAgainMin)
            && held < RegsPlanNeed(k, null);
        bool regsHere = atWalkBank && leaving && SellOrder.Length > 0 && held < RegsPlanNeed(k, shop);
        bool critical = m_dumped.Count > 0 && gold > BankOver || broke || regsLow;
        if (!Decide(k, "Bank",
            !circuitStop && (critical || atWalkBank && leaving && (gold > BankOver || regsHere))
            && !Broken("banking in " + city)))
            return null;
        if (regsHere && !regsLow)
        {
            int need = RegsPlanNeed(k, shop);
            Note("Circuit: recall reagents " + held
                + (need == int.MaxValue ? ", and no shop by a bank ahead in the plan"
                    : ", short of the plan's " + need + " to its next bank") + "; refilled here");
        }
        if (broke)
            m_brokeBankAt = Clock.UtcNow;
        if (regsLow)
        {
            m_regsBankAt = Clock.UtcNow.ToLocalTime();
            Note("Circuit: recall reagents low (" + string.Join(", ", regs!) + "); banking in " + city
                + " for the loadout");
        }
        if (critical)
        {
            RecallNearBank(k);
            city = WalkTown(k);
            if (city == null)
                return null;
        }
        // At the bank for anything, the gold over the purse goes in (Damian, 2026-10-26: "do all your business pending at
        // the bank... we don't need to cart the gold around, the threshold purpose is prevent too much going to the
        // bank"): bank-over decides only whether the gold alone makes a trip.
        CircuitStatus = "Banking in " + city;
        string? bw = BankHere(k, city);
        if (bw != null)
        {
            Note("Circuit: banking in " + city + ": " + bw + "; the gold carried");
            FailSpan("banking in " + city, bw);
            return null;
        }
        FailOk("banking in " + city);
        if (broke)
        {
            string taken;
            string? gw = BankTake(new List<int> { GoldArt }, PurseWant(city), out taken);
            Note("Circuit: the purse from the bank's top-level gold: " + (gw ?? taken));
        }
        return null;
    }
    // What the town's due thread vendors are expected to sell (ThreadScore), FreshThread with none known, with its
    // kits.
    int PurseWant(string city)
    {
        int thread = ThreadScore(city);
        if (thread <= 0)
            thread = FreshThread;
        return Math.Min(PurseMost, thread * ThreadGp + ((thread + ThreadPerKit - 1) / ThreadPerKit) * KitGp);
    }
    // What was put down, lifted back as far as the carry limit allows; a pile too heavy whole is split.
    public const int FetchTiles = 30;
    void Fetch(GameLink k)
    {
        if (!Decide(k, "Fetch", !circuitStop))
            return;
        uint pack;
        lock (k.State.Gate)
            pack = k.State.Backpack;
        foreach (Dumped d in new List<Dumped>(m_dumped))
        {
            if (circuitStop)
                return;
            // Walked back to only from within FetchTiles and with room to lift some, and a failed walk drops it (the
            // second 2026-10-24 audit: every pass walked, or recalled, back to a pile it had no room for; L-FUTILITY).
            int fx, fy;
            lock (k.State.Gate)
            {
                fx = k.State.X;
                fy = k.State.Y;
            }
            if (Tiles(d.X, d.Y, fx, fy) > FetchTiles
                || FitsIn(d.Graphic, CarryLimit(k) - SewWeightRoom - WeightNow(k)) <= 0)
                continue;
            CircuitStatus = "Walking back for the " + UoTiles.Name(d.Graphic);
            string? w = WalkTo(d.X, d.Y, 1);
            if (w != null)
            {
                Note("Circuit: walking back to " + d.X + "," + d.Y + " for the " + UoTiles.Name(d.Graphic) + ": " + w
                    + "; left there");
                lock (m_dumped)
                    m_dumped.Remove(d);
                continue;
            }
            AssistItem? g = DumpedPile(k, d);
            if (g == null)
            {
                Note("Circuit: the " + d.Amount + " " + UoTiles.Name(d.Graphic) + " at " + d.X + "," + d.Y
                    + " are gone");
                lock (m_dumped)
                    m_dumped.Remove(d);
                continue;
            }
            int amount = Math.Max(1, g.Amount), fits = Math.Min(amount,
                FitsIn(d.Graphic, CarryLimit(k) - SewWeightRoom - WeightNow(k)));
            if (fits <= 0)
            {
                Note("Circuit: no room to lift the " + UoTiles.Name(d.Graphic) + " at " + d.X + "," + d.Y
                    + "; left there");
                continue;
            }
            uint serial = g.Serial;
            using (Act(k))
            {
                w = MoveStack(k, serial, fits, UoTiles.Name(d.Graphic),
                    "Lifting the " + UoTiles.Name(d.Graphic) + " back", () => k.Drop(serial, pack)!);
            }
            if (w != null)
            {
                Note("Circuit: lifting the " + UoTiles.Name(d.Graphic) + " back: " + w);
                continue;
            }
            if (fits >= amount)
                lock (m_dumped)
                    m_dumped.Remove(d);
            else
                Note("Circuit: " + fits + " of " + amount + " " + UoTiles.Name(d.Graphic)
                    + " lifted back; the rest stays at " + d.X + "," + d.Y);
        }
    }
    // A put-down pile where it lies: uo1998 sends a stack dropped on the ground back under a new serial (2026-10-25
    // 10:39:58, 476C218C lifted, 476C45A0 at 1362,3779 with its 82), so the pile is its art on its tile.
    static AssistItem? DumpedPile(GameLink k, Dumped d)
    {
        lock (k.State.Gate)
        {
            AssistItem? g;
            if (k.State.Ground.TryGet(d.Serial, out g) && g != null)
                return g;
            foreach (AssistItem i in k.State.Ground.Values)
                if (i.Graphic == d.Graphic && i.X == d.X && i.Y == d.Y)
                    return i;
        }
        return null;
    }
    // To the shop, or why the circuit ends. A shop not reached (a rune landing blocked, a walk with no way) is passed
    // over for this pass, paused FailPauseMinutes after a second failure in a row and BROKEN after a third (Damian,
    // 2026-10-20: "the first time ... make a alternate plan ... a second time, add the timer"); only a reason that ends
    // the circuit (KeepRules.Ends) ends it (2026-10-24 14:02:43: "Something is blocking the location." at The Lord's
    // Clothiers ended the circuit). reached: false when the shop is passed over.
    ValueMap<string, DateTime> m_shopPaused = ValueMap<string, DateTime>.Empty; readonly Lock m_shopPausedGate = new();
    static string ReachKey(UoRegions.Shop shop)
    {
        return "reach " + UoRegions.Display(shop);
    }
    internal bool ShopPaused(UoRegions.Shop shop)
    {
        string key = ReachKey(shop);
        if (Broken(key))
            return true;
        DateTime until;
        return m_shopPaused.TryGet(key, out until) && Clock.UtcNow < until;
    }
    string? ReachShop(GameLink k, UoRegions.Shop shop, out bool reached)
    {
        reached = false;
        if (ShopPaused(shop))
        {
            Note("Circuit: " + UoRegions.Display(shop) + " passed over: not reached on its last tries");
            return null;
        }
        if (!Decide(k, "ToShop", !circuitStop))
        {
            reached = true;
            return null;
        }
        string? w = ToShop(k, shop);
        if (w == null)
        {
            FailOk(ReachKey(shop));
            reached = true;
            return null;
        }
        return Unreached(shop, w);
    }
    // The shop not reached for why: null when it is passed over, else why the circuit ends.
    internal string? Unreached(UoRegions.Shop shop, string why)
    {
        // Out of recall reagents is the circuit's, not the shop's: back to the top, never counted toward the shop's
        // BROKEN.
        if (KeepRules.Ends(why) || why.StartsWith(ReagentsOut, StringComparison.Ordinal))
            return "to " + UoRegions.Display(shop) + ": " + why;
        TimeSpan span = FailSpan(ReachKey(shop), why);
        lock (m_shopPausedGate)
            m_shopPaused = m_shopPaused.Set(ReachKey(shop), Clock.UtcNow + span);
        Note("Circuit: " + UoRegions.Display(shop) + " passed over: not reached (" + why + ")");
        return null;
    }
    // A blocked shop landing reported for a better mark (Damian, 2026-10-24: "report that the rune was blocked so I can
    // resolve by picking a better landing for that vendor"): in red in the client, in the notes, and a line in
    // blocked-runes-<server>.txt (time, rune, its landing, the shop).
    internal void ReportBlockedRune(GameLink k, uint rune, UoRegions.Shop shop)
    {
        LoadRuneLandings();
        int[]? at;
        m_runeLandings.TryGet(rune, out at);
        string where = at != null ? at[0] + "," + at[1] : "landing not recorded";
        string call = "RUNE BLOCKED: the rune " + rune.ToString("X") + " for " + UoRegions.Display(shop) + " (" + where
            + "); mark a better landing";
        Note("Circuit: " + call);
        if (!k.Closed)
            k.Tell(call, DropHue);
        try
        {
            File.AppendAllLines(ServerFile("blocked-runes"),
                new[] { Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\t" + rune.ToString("X") + "\t"
                + where + "\t" + UoRegions.Display(shop) });
        }
        catch (IOException) { }
    }
    // The house's town (Damian, 2026-10-25: "brian can be reached by foot from the house in about the same time it
    // takes to wait for that mana ... you could save mana and let it regenerate while you run"; the pad at 4663,1134
    // beside the Moonglow house lands in the town's hedge maze): the CITY_ region holding the house, else the city a
    // known teleporter within HomeTeleporterTiles of the pile lands in, the nearest such teleporter's; null for none.
    public const int HomeTeleporterTiles = 50;
    internal string? HomeTown(GameLink k)
    {
        int[]? pile = OverflowPile;
        if (pile == null)
            return null;
        string? town = UoRegions.CityRegion(pile[0], pile[1]);
        if (town != null)
            return town;
        int bestD = HomeTeleporterTiles + 1;
        foreach (KeyValuePair<long, List<int[]>> t in Teleporters(k).Ordered(Comparer<long>.Default))
        {
            int d = Tiles((int)(t.Key >> 16), (int)(t.Key & 0xFFFF), pile[0], pile[1]);
            if (d >= bestD)
                continue;
            foreach (int[] e in t.Value)
            {
                string? c = UoRegions.City(e[1], e[2]);
                if (c != null)
                {
                    town = c;
                    bestD = d;
                    break;
                }
            }
        }
        return town;
    }
    // x, y within HomeWalkTiles of the thread pile: home, as a walk's target.
    bool NearPile(int x, int y)
    {
        int[]? pile = OverflowPile;
        return pile != null && Tiles(x, y, pile[0], pile[1]) <= HomeWalkTiles;
    }
    // In the house or within HomeWalkTiles of its door.
    bool AtHome(GameLink k)
    {
        int[]? pile = OverflowPile;
        Home? h = pile == null ? null : HomeAt(pile[0], pile[1]);
        if (h == null)
            return false;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        return InHome(h, px, py) || Tiles(px, py, h.DoorX, h.DoorY) <= HomeWalkTiles;
    }
    // The town a walk from here reaches: the town the player stands in, else from home the house's town, whose shops
    // and bank are walked to and a rune taken only when the walk fails.
    string? WalkTown(GameLink k)
    {
        return AtHome(k) && HomeTown(k) is string ht ? ht : CityOf(k);
    }
    // From home, the house's town's shop with work, or -1: walked to first, the mana coming back on the run, so the
    // two-tailor jump never sends a recall past it.
    int HomeTownShop(GameLink k, List<UoRegions.Shop> order)
    {
        if (!Homespun || !AtHome(k))
            return -1;
        string? town = HomeTown(k);
        if (town == null)
            return -1;
        for (int i = 0; i < order.Count; i++)
        {
            string? c = UoRegions.City(order[i].CX, order[i].CY);
            if (c == town && ShopIdle(k, order[i], c) == null)
                return i;
        }
        return -1;
    }
    string? ToShop(GameLink k, UoRegions.Shop shop)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (shop.Holds(px, py, 0))
            return null;
        string? city = UoRegions.City(shop.CX, shop.CY), w;
        // A walk inside the town, or from home into the house's town; Jhelom's two islands answer it with a failed
        // walk, and the rune goes instead.
        if (WalkTown(k) == city)
        {
            CircuitStatus = "Walking to " + UoRegions.Display(shop);
            w = WalkTo(shop.CX, shop.CY, 2);
            if (w == null)
                return null;
            if (KeepRules.Ends(w))
                return w;
            Note("Circuit: walking to " + UoRegions.Display(shop) + ": " + w + "; by rune");
        }
        w = TripReady(k, false);
        if (w != null)
            return w;
        uint rune = CarriedShopRune(k, city!, shop);
        // A failed recall ends the trip: the fallbacks after it (the carried rune, the bank's) cast again on the same
        // cause (2026-10-22 06:41:56, 06:42:01, 06:42:11: three casts, one open cursor). L-FUTILITY.
        if (rune != 0)
        {
            CircuitStatus = "Recalling to " + UoRegions.Display(shop);
            w = Recall(rune);
            if (w != null)
            {
                Note("Circuit: the rune for " + UoRegions.Display(shop) + ": " + w);
                // A blocked shop landing is most often the tailor standing on it, who moves only while a player is
                // there to wake it (Damian, 2026-10-24: "often there is noone there, and the problem will not resolve
                // on its own for a long time ... the circuit should use the bank runes in the bank to recall to the
                // city and run to the npc, not skip them"): the city's own rune from the bank box, a different landing,
                // then the walk in.
                if (w != "the landing is blocked")
                    return w;
                ReportBlockedRune(k, rune, shop);
                BlockRune(rune);
                // Away from a bank (the house door; 2026-10-24 14:13:32, "bank" said at the Moonglow house, no banker),
                // a bank by the carried rune first.
                RecallNearBank(k);
                CircuitStatus = "Recalling to " + city + " by the city's rune in the bank box";
                w = BankRecall(k, city!, false);
                if (w != null)
                {
                    Note("Circuit: the city's rune for " + city + ": " + w);
                    return w;
                }
            }
        }
        if (CityOf(k) != city)
        {
            w = Travel(k, city!);
            if (w != null)
                return w;
        }
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        if (shop.Holds(px, py, 0))
            return null;
        CircuitStatus = "Walking into " + UoRegions.Display(shop);
        return WalkTo(shop.CX, shop.CY, 2);
    }
    // Any thread for sale in the shop (Damian, 2026-10-20: "buying any thread that shows up for sale"), all of it, as
    // far as the carry limit (Damian, 2026-10-20: "there was plenty of thread to buy", "now see my encumberance
    // problem": 183 thread and 4889 gold, 394 of 378 stones). Short of room for ThreadWant with gold over the purse,
    // the gold is banked in the town first, the heavy thing gone and the cheap thing bought. Null, or why the circuit
    // stops.
    ValueMap<uint, int> m_purseShortAt = ValueMap<uint, int>.Empty; readonly Lock m_purseShortAtGate = new();
    // A vendor whose whole stock the purse fell short of, the purse no bigger since: no work at its shop (the second
    // 2026-10-24 audit: ShopIdle counted it, the shop was recalled to, and BuyShopThread then passed it over).
    // Homespun's thread vendors bought out or answered "has none", until a restock on: not asked for thread, and no
    // thread work in their shop (ShopIdle).
    ValueMap<uint, DateTime> m_threadOut = ValueMap<uint, DateTime>.Empty; readonly Lock m_threadOutGate = new();
    bool ThreadOut(uint serial)
    {
        DateTime until;
        return m_threadOut.TryGet(serial, out until) && Clock.UtcNow < until;
    }
    bool PurseShortNow(GameLink k, uint serial)
    {
        int s;
        if (!m_purseShortAt.TryGet(serial, out s))
            return false;
        return PackCount(k, g => g == GoldArt) <= s;
    }
    string? BuyShopThread(GameLink k, UoRegions.Shop shop, string city, ref int bought, out bool did)
    {
        did = false;
        // Homespun asks every thread vendor of a shop it stands in, due or not (Damian, 2026-10-26: "thread should be
        // bought always at every stop for sure, or checked for to at least confirm the zero when there, its basically
        // free sanity check"); "has none" is an answer and passes her over for a restock.
        List<AtlasNpc> sellers = ThreadCandidates(city).FindAll(n =>
            shop.Holds(n.X, n.Y, 2) && (Homespun || ThreadDue(n)));
        if (!Decide(k, "BuyThreadHere", sellers.Count > 0 && (Homespun || PackFull(k) == null)))
            return null;
        // The bank for room once a visit, not once a seller (the third 2026-10-24 audit, finding 10).
        bool bankedForRoom = false;
        foreach (AtlasNpc v in NearestFirst(k, sellers))
        {
            if (circuitStop || !Homespun && PackFull(k) != null)
                break;
            // A window that just showed no thread is the answer (2026-10-24 14:14:08 and 14:14:13, Brian asked twice in
            // the shop's two rounds, "the vendor has none" both).
            if (ShownWithout(v.Serial, IsThreadArt))
                continue;
            if (ThreadOut(v.Serial))
                continue;
            // A vendor whose whole stock the purse fell short of is asked again only once the purse has grown.
            int shortGold;
            if (m_purseShortAt.TryGet(v.Serial, out shortGold) && PackCount(k, g => g == GoldArt) <= shortGold)
                continue;
            int room = FitsIn(0x0FA0, CarryLimit(k) - SewWeightRoom - WeightNow(k));
            // Only over the bank-over setting (Damian, 2026-10-21: "not respecting the bank gold over setting"; Skara
            // Brae, 1704 gold banked for room with 82 shirts aboard, and Lynn had no thread): else the thread that fits
            // is bought.
            if (room < ThreadWant && PackCount(k, g => g == GoldArt) > BankOver && !bankedForRoom
                && !Broken("banking in " + city))
            {
                bankedForRoom = true;
                CircuitStatus = "Banking the gold in " + city + " to carry the thread";
                string? bw = BankHere(k, city);
                if (bw != null)
                {
                    Note("Circuit: banking in " + city + " for room: " + bw);
                    FailSpan("banking in " + city, bw);
                }
                else
                {
                    FailOk("banking in " + city);
                }

                room = FitsIn(0x0FA0, CarryLimit(k) - SewWeightRoom - WeightNow(k));
            }
            // Homespun buys every vendor out whatever the room (Damian, 2026-10-21: "critical. even putting it on the
            // floor of the tailor shop is better than leaving it for sale"): a vendor not bought out restocks no more.
            // The stock is asked with the room as the most first; a stock that does not fit gets room made for it
            // (MakeRoom), then is bought whole, the thread still past the carry limit put on the shop's floor.
            if (room <= 0 && !Homespun)
            {
                Note("Circuit: no room to carry thread from " + v.Name.Split(' ')[0] + " (" + WeightNow(k) + " of "
                    + CarryLimit(k) + " stones)");
                break;
            }
            string name = v.Name.Split(' ')[0];
            CircuitStatus = "Buying thread from " + name + " in " + UoRegions.Display(shop);
            int got, left;
            m_lastBuyShown = 0;
            m_lastBuyWhole = 0;
            string? w = VendorBuy(IsThreadArt, Homespun ? Math.Max(0, room) : Math.Min(ThreadWant * 10, room), out got,
                out left, name, int.MaxValue, true);
            int purse = PackCount(k, g => g == GoldArt);
            lock (k.State.Gate)
                purse = Math.Max(purse, k.State.Gold);
            // Room only for a stock the purse covers whole (2026-10-25 10:40:00, Jhelom: 82 thread put down for Ella's
            // 100, then 276 gold of the 300 and none bought).
            if (Homespun && w != null && w.StartsWith(PurseShort + "room for ") && m_lastBuyShown > 0
                && m_lastBuyWhole <= purse)
            {
                int shown = m_lastBuyShown;
                string? mw = MakeRoom(k, shop, city, shown);
                Note("Circuit: room for " + name + "'s " + shown + " thread: "
                    + (mw ?? "made (" + WeightNow(k) + " of " + CarryLimit(k) + " stones)"));
                w = VendorBuy(IsThreadArt, ThreadWant * 10, out got, out left, name, int.MaxValue, true);
                if (w == null)
                {
                    bool stuck;
                    string? dw = ThreadOverLimit(k, out stuck);
                    if (stuck)
                        return dw;
                    if (dw != null)
                        Note("Circuit: " + dw);
                }
            }
            if (w == null)
            {
                bought += got;
                if (got > 0)
                    did = true;
                FailOk("buy vendor " + v.Serial.ToString("X"));
                // Bought out, homespun's next round of the visit would ask her again, due or not (11:24:11, Ella
                // asked a second after her 37 for "the vendor has none"; L-FUTILITY): not asked for thread for a
                // restock. Her shirts are another matter, so not SkipFor, which the sell order reads too.
                if (Homespun && left == 0)
                    lock (m_threadOutGate)
                        m_threadOut = m_threadOut.Set(v.Serial, Clock.UtcNow + s_restockSpan);
                continue;
            }
            Note("Circuit: " + name + ": " + w);
            // Short of the whole stock: asked again after this shop's shirts are sold, never passed over for it.
            if (w.StartsWith(PurseShort))
            {
                lock (m_purseShortAtGate)
                    m_purseShortAt = m_purseShortAt.Set(v.Serial, PackCount(k, g => g == GoldArt));
                continue;
            }
            lock (m_purseShortAtGate)
                m_purseShortAt = m_purseShortAt.Remove(v.Serial);
            // A vendor that gave no answer is passed over for a restock, never asked again before then (L-FUTILITY).
            // "Has none" and "away" are answers, not failures: skipped for a restock and never counted toward BROKEN
            // (the third audit, finding 6; a vendor sold out was passed over for good after three visits).
            // "Has none" is about her thread only: SkipFor would pass her over for shirts too, and the sell order passed
            // Ella and Lynn over with shirts to sell (2026-10-26 11:29:05).
            if (Homespun && w.Contains("has none"))
                lock (m_threadOutGate)
                    m_threadOut = m_threadOut.Set(v.Serial, Clock.UtcNow + s_restockSpan);
            else if (Silent(w) || w.Contains("has none") || w.StartsWith("away:"))
                SkipFor(v.Serial, s_restockSpan);
            else
                SkipVendor(v.Serial, w);
        }
        return null;
    }
    // The thread past the carry limit (less SewWeightRoom) put on the floor beside the player, the largest stack's
    // excess (a tile beside, never the player's own, which uo1998 answers by putting it back). A tile the server
    // refuses (it answers the drop and puts the stack back in the pack: Moonglow, 4461,1066, 2026-10-21) shows as an
    // unchanged weight, and the next of the eight is tried. Null, or what happened; stuck when every tile refused and
    // the player is still over the limit, which leaves the player unable to recall or walk.
    static readonly int[,] s_beside = { { 0, 1 }, { 1, 0 }, { 0, -1 }, { -1, 0 }, { 1, 1 }, { -1, 1 }, { 1, -1 },
        { -1, -1 } };
    string? ThreadOverLimit(GameLink k, out bool stuck)
    {
        stuck = false;
        int over = WeightNow(k) - (CarryLimit(k) - SewWeightRoom);
        if (CarryLimit(k) == 0 || over <= 0)
            return null;
        AssistItem? st = LargestCarried(k, IsThreadArt);
        if (st == null)
            return null;
        int n = Math.Min(Math.Max(1, st.Amount), CountFor(st.Graphic, over));
        int[]? at;
        string? w = PutDownBeside(k, st.Serial, n, "thread", out at);
        if (w == null)
        {
            Leave(st.Serial);
            return n + " thread over the carry limit put down at " + at![0] + "," + at[1] + " (" + WeightNow(k) + " of "
                + CarryLimit(k) + " stones)";
        }
        stuck = WeightNow(k) > CarryLimit(k);
        return "the thread over the carry limit did not go down: " + w + " (" + WeightNow(k) + " of " + CarryLimit(k)
            + " stones)";
    }
    static AssistItem? LargestCarried(GameLink k, Func<int, bool> kind)
    {
        return Inv(k).Largest(kind);
    }
    // A stack's n put down on the first of the eight tiles beside the player whose drop lowers the weight. Null and the
    // tile, or why no tile took it.
    string? PutDownBeside(GameLink k, uint serial, int n, string what, out int[]? at)
    {
        at = null;
        string last = "every tile beside refused it";
        for (int t = 0; t < s_beside.GetLength(0) && !circuitStop; t++)
        {
            int x, y, z;
            lock (k.State.Gate)
            {
                x = k.State.X + s_beside[t, 0];
                y = k.State.Y + s_beside[t, 1];
                z = k.State.Z;
            }
            string? w;
            int before = WeightNow(k);
            using (Act(k))
            {
                w = MoveStack(k, serial, n, what, "Putting the " + what + " down",
                    () => k.DropGround(serial, x, y, z)!);
            }
            if (w != null)
            {
                last = w;
                continue;
            }
            if (WeightNow(k) >= before)
            {
                last = "the server put it back at " + x + "," + y;
                continue;
            }
            at = new[] { x, y, z };
            return null;
        }
        return last;
    }
    // Stones of n of a tile's art (OUO CItem_GetWeight_VT: the tile's weight each, a stone for each 10 with none), and
    // the count of it that weighs at least stones.
    static int StonesOf(int graphic, int n)
    {
        List<UoTiles.Tile> tiles = UoTiles.Items();
        int w = graphic >= 0 && graphic < tiles.Count && tiles[graphic].Weight < 255 ? tiles[graphic].Weight : 0;
        return w > 0 ? w * n : (n + 9) / 10;
    }
    static int CountFor(int graphic, int stones)
    {
        List<UoTiles.Tile> tiles = UoTiles.Items();
        int w = graphic >= 0 && graphic < tiles.Count && tiles[graphic].Weight < 255 ? tiles[graphic].Weight : 0;
        return Math.Max(1, w > 0 ? (stones + w - 1) / w : stones * 10);
    }
    // Room made before a vendor's whole thread stock is bought (Damian, 2026-10-21: "before you buy, make room"; "empty
    // the bag of dead weight: any gold in excess the purchase of that item. and thread already on board"): in his order,
    // until the stock fits, the gold over the stock's price banked in the town and the shop walked back to, then the
    // thread carried put down beside the player (Dumped, lifted back by Fetch as room allows). The shirts are never put
    // down: the stock is bought whole all the same and the thread past the carry limit goes on the floor, one item to
    // lift again where a pack of shirts is one lift each (Damian, 2026-10-25: "buy the thread, become encumbered, but
    // move the thread to the floor instead of the product. it is just 1 item and easier to pick back up later"). Null,
    // or why the room was not made.
    string? MakeRoom(GameLink k, UoRegions.Shop shop, string city, int shown)
    {
        Func<int> need = () => StonesOf(0x0FA0, shown) - (CarryLimit(k) - SewWeightRoom - WeightNow(k));
        if (CarryLimit(k) == 0 || need() <= 0)
            return null;
        int cost = shown * (ThreadGp + 1);
        if (PackCount(k, g => g == GoldArt) > cost && city != null && !Broken("banking in " + city))
        {
            CircuitStatus = "Banking the gold over " + cost + " in " + city + " for room";
            string? bw = BankHere(k, city, cost);
            if (bw != null)
                Note("Circuit: banking in " + city + " for room: " + bw);
            else
                FailOk("banking in " + city);
            string? tw = ToShop(k, shop);
            if (tw != null)
                return "back to " + UoRegions.Display(shop) + ": " + tw;
            if (need() <= 0)
                return null;
        }
        while (need() > 0 && !circuitStop)
        {
            AssistItem? st = LargestCarried(k, IsThreadArt);
            if (st == null)
                break;
            int n = Math.Min(Math.Max(1, st.Amount), CountFor(st.Graphic, need()));
            int[]? at;
            string what = UoTiles.Name(st.Graphic);
            string? pw = PutDownBeside(k, st.Serial, n, what, out at);
            if (pw != null)
                return "the " + what + " did not go down: " + pw;
            Leave(st.Serial);
            lock (m_dumped)
                m_dumped.Add(new Dumped { Serial = st.Serial, Graphic = st.Graphic, Amount = n, X = at![0],
                    Y = at[1], Z = at[2] });
            Note("Circuit: " + n + " " + what + " put down at " + at[0] + "," + at[1] + " for room");
        }
        return need() > 0 ? need() + " stones short with no gold over the price or thread left to shed; the stock "
            + "bought whole, the thread over the limit put down after" : null;
    }
    // The sell phase's sale: every tailor of the shop known, or its shopkeeper by name, offered the shirts at the
    // floor, whatever it offered before (Damian, 2026-10-20: "in the sell phase we reset all that stuff and sell to the
    // first offer everything we can").
    string? SellShop(GameLink k, UoRegions.Shop shop, string city, ref int sold, ref int goldIn)
    {
        MobileLabels(VendorHear * 4);
        List<AtlasNpc> tailors = CityBuyers(city).FindAll(n => shop.Holds(n.X, n.Y, 2));
        if (tailors.Count == 0)
        {
            AtlasNpc? keeper = ShopTailors(shop).Find(n => true);
            if (keeper != null)
                tailors.Add(keeper);
        }
        if (!Decide(k, "SellHere", PackCount(k, IsShirtArt) > 0 && tailors.Count > 0))
            return null;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        BestPayingFirst(tailors, px, py);
        foreach (AtlasNpc t in tailors)
        {
            if (circuitStop || PackCount(k, IsShirtArt) == 0)
                break;
            if (!PaysFloor(t))
                continue;
            string name = t.Name.Split(' ')[0];
            CircuitStatus = "Selling shirts to " + name + " in " + UoRegions.Display(shop);
            int n2, g2, u2;
            uint ts = t.Serial;
            string? w = VendorSell(IsShirtArt, g => VendorFloor(ts, g), out n2, out g2, out u2, name);
            sold += n2;
            goldIn += g2;
            if (w != null)
                Note("Circuit: " + UoRegions.Display(shop) + ", " + name + ": " + w);
            // A vendor that gave no answer is passed over for its span and the round goes on (L-FUTILITY: never asked
            // again before then): returning ended the round, and the top a minute later asked the same vendor again.
            if (Silent(w!))
            {
                AvoidTailor(ts, s_restockSpan);
                Note("Circuit: " + name + " passed over for a restock: " + w);
            }
        }
        return null;
    }
    // The gold for buying thread, from the box's top-level piles, up to PurseKeep (Damian, 2026-10-20: "grab more
    // thread gold"; "a 400 gold is enough to cover the thread of 1 vendor we are likely to see").
    void TopUpPurse(GameLink k)
    {
        string taken;
        string? gw = PurseTo(PurseKeep, out taken);
        if (gw != null || taken.Length > 0)
            Note("Circuit: the purse from the bank's top-level gold: " + (gw ?? taken));
    }
    // Everything the shop offers, round after round while one does something: the shirts sold to its tailors that pay
    // the floor, its thread bought, kits bought when none is left, the thread woven at its loom, the bolts and cloth
    // sewn.
    string? ShopWork(GameLink k, UoRegions.Shop shop, string city, string nextShop, ref int bought, ref int shirts,
        ref int sold, ref int goldIn)
    {
        for (int round = 0; round < ShopRounds && !circuitStop && !k.Closed; round++)
        {
            CircuitNext = "sell, buy thread, weave and sew in " + UoRegions.Display(shop) + " (round " + (round + 1)
                + "), then " + nextShop;
            bool did = false;
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            MobileLabels(VendorHear * 4);
            List<AtlasNpc> tailors = CityBuyers(city).FindAll(n => shop.Holds(n.X, n.Y, 2));
            // A shop whose tailors' stock is not read yet: its shopkeeper known by name, asked by name, as SellTrip
            // does.
            if (tailors.Count == 0)
            {
                AtlasNpc? keeper = ShopTailors(shop).Find(n => true);
                if (keeper != null)
                    tailors.Add(keeper);
            }
            if (Decide(k, "SellHere", PackCount(k, IsShirtArt) > 0 && tailors.Count > 0))
            {
                List<AtlasNpc> payers = tailors.FindAll(PaysFloor);
                BestPayingFirst(payers, px, py);
                bool under = false;
                foreach (AtlasNpc t in payers)
                {
                    if (circuitStop || PackCount(k, IsShirtArt) == 0)
                        break;
                    string name = t.Name.Split(' ')[0];
                    CircuitStatus = "Selling shirts to " + name + " in " + UoRegions.Display(shop);
                    int n2, g2, u2;
                    uint ts = t.Serial;
                    string? w = VendorSell(IsShirtArt, g => VendorFloor(ts, g), out n2, out g2, out u2, name);
                    sold += n2;
                    goldIn += g2;
                    if (n2 > 0)
                        did = true;
                    if (w != null)
                        Note("Circuit: " + UoRegions.Display(shop) + ", " + name + ": " + w);
                    // Away from her shop is no sale either: Michelle "stayed away from its shop" and was rested two
                    // restocks as sold down (2026-10-26 11:40:43).
                    if (Silent(w!) || n2 == 0 && w != null && w.StartsWith("away: "))
                    {
                        AvoidTailor(ts, s_restockSpan);
                        Note("Circuit: " + name + " passed over for a restock: " + w);
                        continue;
                    }
                    if (u2 > 0 || PackCount(k, IsShirtArt) > 0)
                    {
                        UnderFloorOffered(t.Serial);
                        under = true;
                    }
                }
                if (under)
                    ShopUnderFloor(shop);
            }
            CircuitNext = "buy thread, weave and sew here, then " + nextShop;
            if (StepBuyThread)
            {
                bool boughtHere;
                string? bt = BuyShopThread(k, shop, city, ref bought, out boughtHere);
                if (bt != null)
                    return bt;
                if (boughtHere)
                    did = true;
            }
            // Homespun carries no sewing work to a shop; its kits are for the next home stop, topped up to KitsWant (a
            // kit sews about 50 shirts and a home stop up to about 85: 12:38:09, the last kit wore out 40 shirts in).
            int kitsHeld = PackCount(k, g => g == KitArt);
            if (HasSewWork(k) && kitsHeld == 0 || Homespun && kitsHeld < KitsWant)
            {
                string? kw = BuyKits(k, px, py, KitsWant - kitsHeld);
                if (kw != null)
                    Note("Circuit: sewing kits in " + UoRegions.Display(shop) + ": " + kw);
            }
            int held = PackCount(k, IsThreadArt);
            CircuitNext = (held > 0 ? "weave, then " : "") + "sew, then sell here again or on to " + nextShop;
            if (Decide(k, "Weave", held > 0 && !Homespun && StepWeave))
            {
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                }
                AssistItem? loom = NearLoom(k, px, py);
                if (loom == null || !shop.Holds(loom.X, loom.Y, 4))
                {
                    Note("Circuit: no loom seen in " + UoRegions.Display(shop));
                }
                else
                {
                    CircuitStatus = "Walking to the loom";
                    string? w = WalkTo(loom.X, loom.Y, 1, true);
                    if (w != null)
                    {
                        Note("Circuit: walking to the loom: " + w);
                    }
                    else
                    {
                        CircuitStatus = "Weaving " + held + " thread";
                        WorkToggle();
                        while (WorkRunning && !circuitStop)
                            Clock.Sleep(500);
                        if (PackCount(k, IsThreadArt) < held)
                            did = true;
                        else
                            Note("Circuit: the loom took no thread: " + WorkEnd);
                    }
                }
            }
            CircuitNext = "sell the shirts here, or on to " + nextShop;
            if (Decide(k, "Sew",
                !Homespun && (PackCloth(k).Count > 0 || PackBolts(k).Count > 0) && PackCount(k, g => g == KitArt) > 0))
            {
                CircuitStatus = "Sewing fancy shirts";
                int made, failed;
                string? w = SewAll(out made, out failed);
                shirts += made;
                if (made + failed > 0)
                    did = true;
                if (w != null && w != "stopped")
                    Note("Circuit: sewing: " + w);
            }
            if (!did)
                return null;
        }
        return null;
    }
    void RunCircuit(GameLink k)
    {
        string runName = m_reagentMode ? "Reagents"
            : m_featherMode ? "Feathers"
            : m_lumberMode ? "Lumberjacking"
            : m_markMode ? "Mark runes"
            : m_spellbookMode ? "Spellbooks"
            : m_advertMode ? "Advertise"
            : m_smithMode ? "Smithing"
            : m_mineMode ? "Cave mining"
            : m_tourMode ? "Tour"
            : m_buyMode ? (m_buyKind == "ingots" ? "Buy ingots" : "Buy reagents")
            : m_surveyMode ? "Survey town" : m_refreshMode ? "Refresh" : m_sellOnly ? "Sell only" : "Tailoring";
        CircuitName = m_keepMode ? "Tailoring: Keep thread"
            : m_threadOnly ? "Tailoring: Buy thread"
            : m_sellOnly ? "Tailoring: Sell only" : m_tourMode ? "Tailoring: Tour" : runName;
        string? end = "stopped";
        int bought = 0, shirts = 0, sold = 0, goldIn = 0;
        s_busyTask = m_mineMode ? "mine"
            : m_lumberMode ? "lumber"
            : m_featherMode ? "feathers" : m_reagentMode ? "reagents" : m_spellbookMode ? "inscribe" : "circuit";
        try
        {
            // After a login the pack's contents are unknown until it is opened, and the circuit counted no shirts.
            uint pack;
            lock (k.State.Gate)
                pack = k.State.Backpack;
            bool known;
            lock (m_contentsGate)
                known = pack != 0 && m_contents.ContainsKey(pack);
            if (pack != 0 && !known)
            {
                Peek(pack);
                BusyWait(clickedAt);
            }
            OpenPackBags(k);
            if (m_threadOnly)
            {
                end = RunThreadOnly(k);
                return;
            }
            if (m_keepMode)
            {
                end = RunKeepThread(k);
                return;
            }
            if (m_smithMode)
            {
                end = RunSmithing(k);
                return;
            }
            if (m_advertMode)
            {
                end = RunAdvert(k);
                return;
            }
            if (m_spellbookMode)
            {
                end = RunSpellbooks(k);
                return;
            }
            if (m_markMode)
            {
                end = RunMark(k);
                return;
            }
            if (m_mineMode)
            {
                end = RunMine(k);
                return;
            }
            if (m_lumberMode)
            {
                end = RunLumber(k);
                return;
            }
            if (m_featherMode)
            {
                end = RunFeathers(k);
                return;
            }
            if (m_reagentMode)
            {
                end = RunReagents(k);
                return;
            }
            if (m_tourMode)
            {
                end = RunTour(k, ref bought, ref sold, ref goldIn);
                return;
            }
            if (m_buyMode)
            {
                end = RunBuy(k);
                return;
            }
            if (m_surveyMode)
            {
                end = RunSurvey(k);
                return;
            }
            if (m_refreshMode)
            {
                end = RunRefresh(k);
                return;
            }
            if (m_sellOnly)
            {
                string? cw = CarryShopRunes(k);
                if (cw != null)
                {
                    end = cw;
                    return;
                }
            }
            if (!m_sellOnly)
            {
                while (true)
                {
                    // The shop runes carried first each time round: a failure there is an end like any other, back to
                    // the top.
                    try
                    {
                        end = CarryShopRunes(k) ?? RunShops(k, ref bought, ref shirts, ref sold, ref goldIn);
                    }
                    catch (Exception ex) { end = "an error in the shop loop: " + ex.GetType().Name + ": "
                        + ex.Message; }
                    if (TopEnds(end!) || circuitStop || k.Closed)
                        return;
                    string? tw;
                    try
                    {
                        tw = CircuitTop(k, end!);
                    }
                    catch (Exception ex) { Note("Circuit: an error at the top: " + ex.GetType().Name + ": "
                        + ex.Message); tw = circuitStop || k.Closed ? "stopped" : null; }
                    if (tw != null)
                    {
                        end = tw;
                        return;
                    }
                }
            }
            // Sell only (circuit sell): the shirts sold and the gold banked.
            DateTime bankAgainAt = DateTime.MinValue;
            while (!circuitStop && !k.Closed)
            {
                m_circuitPass++;
                bool did = false;
                int px, py;
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                }
                // The best-offer window over: a new survey cycle at the top price, every tailor passed over for price
                // asked again.
                if (m_surveyedAt != DateTime.MinValue && !Surveyed)
                {
                    m_surveyedAt = DateTime.MinValue;
                    EndCycle();
                    Note("Circuit: the best-offer window ended; a new survey cycle at the top price");
                }
                // 0. The order of a pass (Damian, 2026-10-19: "first priority is offloading large gold, second is
                // selling large quantity of shirts in pack, then making the shirts, then spinning large amounts of
                // thread, then at the end, if all that is done, we buy thread"; "the primary thing is minimizing
                // movement when there is work that can be done here and now", so thread is still bought from a tailor
                // just sold to): the gold over BankOver banked in this town first; a bank that fails is tried again
                // after its FailSpan, the gold carried.
                if (Decide(k, "Bank",
                    !circuitStop && PackCount(k, g => g == GoldArt) > BankOver
                    && Clock.UtcNow.ToLocalTime() >= bankAgainAt && !Broken("banking in " + CityOf(k))))
                {
                    string? city0 = CityOf(k);
                    if (city0 != null)
                    {
                        CircuitStatus = "Banking the gold in " + city0;
                        string? bw = BankHere(k, city0);
                        if (bw != null)
                        {
                            Note("Circuit: banking in " + city0 + ": " + bw + "; the gold carried");
                            bankAgainAt = Clock.UtcNow.ToLocalTime() + FailSpan("banking in " + city0, bw);
                        }
                        else
                        {
                            FailOk("banking in " + city0);
                        }
                    }
                }
                // The town's thread vendors: those bought from before and those whose buy list named thread, near here.
                List<AtlasNpc> vendors = new();
                Set<uint> seen = Set<uint>.Empty;
                foreach (CircuitVendor v in CircuitVendors())
                {
                    AtlasNpc? n = VendorNow(v.Serial);
                    if (n != null && Tiles(n.X, n.Y, px, py) <= CircuitReach && !seen.Contains(n.Serial))
                    {
                        seen = seen.Add(n.Serial);
                        vendors.Add(n);
                    }
                }
                if (Atlas != null)
                    foreach (AtlasNpc n in Atlas.Shops())
                        if (Tiles(n.X, n.Y, px, py) <= CircuitReach && TailorVendor(n)
                            && n.Items.Exists(s => s.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0)
                            && !seen.Contains(n.Serial))
                        {
                            seen = seen.Add(n.Serial);
                            vendors.Add(n);
                        }
                vendors.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
                // The order: the work where the player stands first, sell the shirts, buy thread, weave and sew; the
                // gold is banked when the bank is the next place anyway (a recall goes from it, Travel) or before a
                // wait (Damian, 2026-10-17: "am I in the place i will come back to after the very next step, and maybe
                // i could just kill 2 birds with one stone, and one less round trip"; banking first walked from the
                // tailor to the bank and back). 1b. Sell the shirts, nearest tailor to where the player stands first,
                // on to the next when one will not pay the floor or is full.
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                }
                if (Decide(k, "SellHere", !circuitStop && PackCount(k, IsShirtArt) > 0))
                {
                    TopClosedCheck();
                    // Only a vendor that deals in something on the sell list: the circuit's thread record also holds a
                    // vendor asked for thread after a failed sale (Moonglow's armorer, 2026-10-17), which then drew
                    // every pass back.
                    List<AtlasNpc> sellers = vendors.FindAll(DealsIn);
                    if (Atlas != null)
                        foreach (AtlasNpc n in Atlas.Shops())
                            if (Tiles(n.X, n.Y, px, py) <= CircuitReach && DealsIn(n) && TailorVendor(n)
                                && !sellers.Exists(x => x.Serial == n.Serial))
                                sellers.Add(n);
                    sellers = ByShopRoute(sellers, px, py);
                    foreach (AtlasNpc v in sellers)
                    {
                        if (circuitStop || PackCount(k, IsShirtArt) == 0)
                            break;
                        // A tailor that offered under the floor is not walked to again this survey cycle (it walked
                        // Britain end to end asking Slade, then Tracy, each having just offered 24).
                        if (!PaysFloor(v) || Skipped(v.Serial))
                            continue;
                        string name = v.Name.Split(' ')[0];
                        AtlasNpc cur = VendorNow(v.Serial) ?? v;
                        // A tailor the walk could not reach is not walked to again every pass.
                        CircuitStatus = "Walking to " + name + " to sell";
                        string? w = WalkToSpeakingRange(cur.X, cur.Y);
                        if (w != null)
                        {
                            Note("Circuit: walking to " + name + ": " + w);
                            SkipVendor(v.Serial, w, "walk");
                            continue;
                        }
                        FailOk("walk vendor " + v.Serial.ToString("X"));
                        CircuitStatus = "Selling shirts to " + name;
                        int n2, g2, under;
                        uint vs = v.Serial;
                        w = VendorSell(IsShirtArt, g => VendorFloor(vs, g), out n2, out g2, out under, name);
                        sold += n2;
                        goldIn += g2;
                        if (n2 > 0)
                            did = true;
                        if (w != null)
                            Note("Circuit: " + name + ": " + w);
                        if (Silent(w!))
                        {
                            end = "selling to " + name + ": " + w;
                            return;
                        }
                        if (under > 0 || PackCount(k, IsShirtArt) > 0)
                            UnderFloorOffered(v.Serial);
                        // Thread from the tailor just asked, sale or not (Damian, 2026-10-17: "every single time you go
                        // to sell shirts, you should also look to buy threads afterwards"; every tailor sells thread).
                        // Only once this tailor answered the shirts, by buying or by an offer under the floor (Damian,
                        // 2026-10-18: "anytime we can sell we should sell the shirts ... to buy thread during the main
                        // go loop was crazy"; Lynn sold thread after a sale that never reached her, 02:17:45).
                        bool answered = w == null || under > 0 || PackCount(k, IsShirtArt) == 0;
                        if (!circuitStop && answered && ThreadDue(v) && PackCount(k, IsThreadArt) < ThreadWant)
                        {
                            int got, left;
                            string? tw = VendorBuy(IsThreadArt, ThreadWant * 10, out got, out left, name);
                            if (tw == null)
                            {
                                bought += got;
                                did = true;
                                FailOk("buy vendor " + v.Serial.ToString("X"));
                            }
                            else
                            {
                                Note("Circuit: " + name + ": " + tw);
                                if (Silent(tw))
                                {
                                    end = "buying thread from " + name + ": " + tw;
                                    return;
                                }
                                SkipVendor(v.Serial, tw);
                            }
                        }
                    }
                }
                // 1c. Shirts no tailor here takes at the floor: sold in another city, thread bought there or in the
                // next, the gold banked where it was made (Damian, 2026-10-17: "find a place to sell them. if you leave
                // brit to do that, check for thread where you sell em. then dump gold. if you didn't find thread yet,
                // then go find more thead").
                if (Decide(k, "SellTrip", !circuitStop && PackCount(k, IsShirtArt) >= SellBatch))
                {
                    int n2, g2, t2;
                    string? w = SellTrip(k, out n2, out g2, out t2);
                    sold += n2;
                    goldIn += g2;
                    bought += t2;
                    // Each hop must pay for itself (Damian, 2026-10-20: "5 hops without reason seems excessive ... its
                    // very context dependant"): the account each threshold is set from.
                    Note("Circuit: sell trip account: " + m_tripHops + " recalls (" + m_tripHops * RecallGp
                        + " gp of reagents), " + n2 + " sold for " + g2 + " gold, " + t2 + " thread bought");
                    if (w != null)
                        Note("Circuit: trip: " + w);
                    if (n2 + t2 > 0)
                        did = true;
                    // No city left with a tailor not passed over: the survey is done, and the passed tailors are asked
                    // again.
                    if (w != null && w.StartsWith(NoSellCity) && !Surveyed && PackCount(k, IsShirtArt) >= SellBatch)
                    {
                        SurveyDone();
                        continue;
                    }
                    if (m_sellOnly && n2 == 0)
                    {
                        end = "no tailor reached would buy at " + FloorText() + (w != null ? ": " + w : "");
                        return;
                    }
                    // A trip that failed is not taken again next pass: the circuit ends and says why. No city to go to
                    // is only a reading of the record, and the circuit goes on making shirts.
                    if (w != null && !w.StartsWith(NoSellCity))
                    {
                        end = "sell trip: " + w;
                        return;
                    }
                }
                // Sell-only (circuit sell): the shirts sold and the gold banked, and nothing else (Damian, 2026-10-17:
                // "not buy anything, and not do anything that is not critical path to selling the shirts in its
                // backpack").
                if (PackCount(k, IsShirtArt) == 0)
                {
                    string? city = CityOf(k);
                    if (city != null && PackCount(k, g => g == GoldArt) > PurseKeep)
                    {
                        string? bw = BankHere(k, city);
                        if (bw != null)
                            Note("Circuit: banking in " + city + ": " + bw);
                    }
                    end = "every shirt sold";
                    return;
                }
                if (PackCount(k, IsShirtArt) < SellBatch && !did)
                {
                    end = PackCount(k, IsShirtArt) + " shirts left, under a batch";
                    return;
                }
            }
            end = m_circuitAlarm ?? (k.Closed ? "the connection closed" : "stopped");
        }
        catch (Exception e) { end = "error: " + e.Message; }
        finally
        {
            // The thread, shirts and sales are the tailoring circuits' alone (Damian, 2026-10-18: "the circuit totals
            // still lists tailoring details for the cave and other circuits"); the mining circuit tells its ingots.
            bool tailoring = (runName == "Tailoring" || runName == "Sell only" || runName == "Tour") && !m_keepMode;
            if ((runName == "Tailoring" || runName == "Sell only") && !m_threadOnly && !m_keepMode)
            {
                Critique(k);
                DecisionRow(k, "Ended", true, true, "expert");
            }
            CircuitStatus = "Circuit ended (" + end + ")"
                + (tailoring
                ? ": bought " + bought + " thread, sewed " + shirts + " shirts, sold " + sold + " for " + goldIn
                + " gold."
                : runName == "Cave mining"
                ? ": " + m_mineIngotsMade + " ingots smelted, " + m_mineIngotsStored + " stored." : ".");
            Note("Circuit: " + CircuitStatus);
            lock (m_circuitEndsGate)
                CircuitEnds = CircuitEnds.Set(runName, Clock.UtcNow.ToLocalTime().ToString("HH:mm") + " " + CircuitStatus);
        }
    }
}
