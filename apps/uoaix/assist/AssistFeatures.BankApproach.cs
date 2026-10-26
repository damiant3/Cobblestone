#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // BankerHearsFrom: ServUO's banker hears "bank" from 12 tiles (Banker.cs:299).
    public const int BankWalkReach = 3, BankerFind = 10, BankerNear = 3, BankSpotTries = 3, BankOpenFreshMs = 10000,
        BankerHearsFrom = 12;
    // The bank approached as a player does (Damian, 2026-10-17: "first goal is usually to find a spot in range of the
    // banker but not go inside. to stay outside so you can't be boxed in, plus its faster. a human usually runs up
    // right the edge of the building where their travel lands and they say bank. if bank opens great. if not, run
    // toward the bank door the shortest route hugging the wall, unless there is players there then ride around them.
    // keep spacing of at least 1 between you and other players when you land and say bank again. look to find the
    // banker if the second blind attempt fails. then you consider going through the doors"). "bank" is said once from
    // each new place, in order: the nearest free tile outside the building, the free outside tile nearest its door, a
    // free outside tile within BankHear of the banker, and beside the banker inside. Null once the box has opened
    // (SayBank keeps its answer), else why every place failed. say false: the walk to the building's edge only, nothing
    // said (the survey's end). At any circuit's bank with the title bar's reagent warning on (Damian, 2026-10-20: "we
    // also need to build in the run reg bag loadout when at the bank and the warnings are going off"): a recall reagent
    // under RecallLow refills the loadout once the box has opened, before the circuit's own business there.
    // Whether a banker in view stands within BankerHearsFrom of the player.
    bool BankerInHearing(GameLink k)
    {
        if (Atlas == null)
            return false;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        foreach (AtlasNpc n in Atlas.Visible())
            if (n.Type == "Banker" && Tiles(n.X, n.Y, px, py) <= BankerHearsFrom)
                return true;
        return false;
    }
    // The town whose bank a bank stop uses: the town here when it has a bank or a banker is in hearing; else a recall
    // by a carried city rune to a town with a bank (RecallNearBank), that town; null when none is reached. "bank" is
    // never said where no banker can hear it (L-FUTILITY; Damian, 2026-10-24: "first think the agent did was call for
    // the bank", "no bank here": 14:48:27, "bank" said in the house at 4700,1112, the region "telescope in Moonglow"
    // holding no bank).
    string? ToBankTown(GameLink k, string city)
    {
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        if (city != null && CityBanks(city, x, y).Count > 0 || BankerInHearing(k))
            return city ?? "";
        RecallNearBank(k);
        string? there = WalkTown(k);
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        return there != null && (CityBanks(there, x, y).Count > 0 || BankerInHearing(k)) ? there : null;
    }
    public const string NoBankTown = "no bank here, and no carried rune lands in a town with one";
    string? ToBanker(GameLink k, int[] bank, bool say = true)
    {
        string? w = ToBankerOnce(k, bank, say);
        if (w != null || !say || !CircuitRunning || circuitStop)
            return w;
        string? city = CityOf(k);
        if (city != null && RecallRegsLow(k))
        {
            Note("Circuit: recall reagents low (" + string.Join(", ", RecallRegCounts()!) + "); refilling the loadout");
            string? lw = RefreshLoadout(k, city);
            if (lw != null)
                Note("Circuit: " + lw);
            // Still low, the bank holds none: no bank stop for reagents for RegsBankAgainMin (BankIfDue; 2026-10-24
            // 14:20:26 short at a home stop's bank, then 14:22:37 a recall from Nujel'm to Moonglow's bank for
            // nothing).
            if (RecallRegsLow(k))
                m_regsBankAt = Clock.UtcNow.ToLocalTime();
            // At the bank for reagents, the gold over the purse banked too (Damian, 2026-10-25: "when at the bank for
            // reagents, you should bank any gold in excess of the 200 pocket change").
            DepositWhileOpen(k);
        }
        return null;
    }
    // At the bank for the gold, the recall reagents topped off to the loadout (Damian, 2026-10-25: "if you are at the
    // bank to deposit gold, you should top off your recall reagents"); not again within RegsBankAgainMin of a bank
    // stop that left them short (the bank holds none).
    void TopOffRegs(GameLink k, string city)
    {
        if (!CircuitRunning || circuitStop || RecallRegCounts() == null
            || Clock.UtcNow.ToLocalTime() < m_regsBankAt.AddMinutes(RegsBankAgainMin))
            return;
        bool shortOf = false;
        foreach (int r in s_recallReagents)
            if (PackCount(k, g => g == r) < LoadoutWant(r))
                shortOf = true;
        if (!shortOf)
            return;
        Note("Circuit: at the bank for the gold; topping off the recall reagents ("
            + string.Join(", ", RecallRegCounts()!) + ")");
        string? lw = RefreshLoadout(k, city);
        if (lw != null)
            Note("Circuit: " + lw);
        foreach (int r in s_recallReagents)
            if (PackCount(k, g => g == r) < LoadoutWant(r))
                m_regsBankAt = Clock.UtcNow.ToLocalTime();
    }
    string? ToBankerOnce(GameLink k, int[] bank, bool say)
    {
        List<int[]> rects = UoRegions.BankRects(bank[0], bank[1]);
        if (rects.Count == 0)
            rects.Add(new[] { bank[0], bank[1], 1, 1 });
        List<int[]> said = new();
        string? why = null;
        // 0. The tile a banker answered from last time (<server>bankspot.<bank>; Damian, 2026-10-18: "can't seem to
        // remember where the vendors are"; every visit of a smithing run said "bank" unanswered at the edge and toward
        // the door before the inside answered).
        string spotKey = Server + "bankspot." + bank[0] + "," + bank[1];
        Func<string, bool> tryHere = where =>
        {
            if (!BankFrom(k, said, where))
                return false;
            int hx, hy;
            lock (k.State.Gate)
            {
                hx = k.State.X;
                hy = k.State.Y;
            }
            Cfg.Set(spotKey, hx + "," + hy);
            return true;
        };
        // 0a. Where the player stands, when a banker in view is within BankerHearsFrom, on any floor (Damian,
        // 2026-10-19: "bank would have opened from roof too"; the recall landed on a roof at 1431,1686 z 20 with no
        // walk down and the circuit recalled again). Not kept as the bank's spot: no walk may reach it. Said too when
        // the tile a banker answered from is within hearing, the banker not yet drawn after a recall (Damian,
        // 2026-10-20: "ran inside bank but didn't even try to bank outside"; Minoc 23:18:07, landed 2506,542, 5 tiles
        // from the spot 2502,547 inside).
        string[] known = Cfg.Get(spotKey, "").Split(',');
        int kx = 0, ky = 0;
        bool hasKnown = known.Length == 2 && int.TryParse(known[0], out kx) & int.TryParse(known[1], out ky);
        if (say)
        {
            int lx, ly;
            lock (k.State.Gate)
            {
                lx = k.State.X;
                ly = k.State.Y;
            }
            int[]? near = BankerAt(k, rects, bank);
            bool hears = near != null ? Tiles(lx, ly, near[0], near[1]) <= BankerHearsFrom
                : hasKnown && Tiles(lx, ly, kx, ky) <= BankerHearsFrom;
            if (hears && BankFrom(k, said, "where the player stands"))
                return null;
        }
        if (say && hasKnown)
        {
            string? kw = WalkTo(kx, ky, 0);
            if (kw == null && tryHere("where a banker answered before"))
                return null;
            if (kw != null)
                Note("Bank: the tile a banker answered from (" + kx + "," + ky + ") was not reached: " + kw);
        }
        // 1. The edge nearest where the travel landed; with no walk there, a recall by a pack rune labelled for the
        // bank's town, the reagents held (Damian, 2026-10-18: "since we had bloodmoss due to successful purchase, we
        // could recall to bank in magic bag in backpack"; the walk from Jhelom's south-island mage shop had no way).
        string? w = ToBankSpot(k, rects, null!, 0);
        if (w != null)
        {
            string? rw = RecallToBank(k, bank);
            if (rw != null)
                return "walking to the bank's edge: " + w + "; " + rw;
            w = ToBankSpot(k, rects, null!, 0);
            if (w != null)
                return "walking to the bank's edge after the recall: " + w;
        }
        if (!say)
            return null;
        if (tryHere("at the bank's edge"))
            return null;
        // 1b. The banker by name, approached as a vendor is (Damian, 2026-10-19: "find the banker npc near the bank,
        // and approach the banker like its a vendor"; no banker answered three blind tries at 3691,2512 twice): walked
        // to within hearing and in sight (NearVendor), then asked "<name> bank" (AskNear: going home, silence and
        // moves).
        string? bankerName = BankerName(k, rects, bank);
        if (bankerName != null)
        {
            CircuitStatus = "To the banker " + bankerName;
            string? nw = NearVendor(k, bankerName);
            if (nw != null)
            {
                Note("Bank: walking to the banker " + bankerName + ": " + nw);
            }
            else
            {
                byte[]? open = AskNear(k, bankerName, "bank", 0x24);
                if (open != null)
                {
                    BankOpened(k, open);
                    int hx, hy;
                    lock (k.State.Gate)
                    {
                        hx = k.State.X;
                        hy = k.State.Y;
                    }
                    Cfg.Set(spotKey, hx + "," + hy);
                    return null;
                }
                Note("Bank: " + bankerName + " did not open the box"
                    + (LastReply != null ? " (\"" + LastReply + "\")" : ""));
            }
        }
        // 2. Along the wall toward its door, BankRetryMin to BankRetryMax tiles on from the first try (Damian,
        // 2026-10-17: "when the first attempt doesn't land, move about 5-8 tiles before you try again").
        int[]? door = BankDoor(k, rects), first = said[said.Count - 1];
        w = ToBankSpot(k, rects, door!, int.MaxValue, first, BankRetryMin, BankRetryMax);
        if (w != null)
            why = "walking along the bank toward its door: " + w;
        else if (tryHere(door != null ? "toward the bank's door" : "further along the bank"))
            return null;
        // 3. Outside, within the banker's hearing, BankRetryMin on from every try; 4. beside the banker, through the
        //    doors.
        int[]? banker = BankerAt(k, rects, bank);
        if (banker == null)
            return why ?? "no banker answered \"bank\" from two places outside the bank, and no banker is in view";
        w = ToBankSpot(k, rects, banker, BankHear, said[said.Count - 1], BankRetryMin, int.MaxValue, said);
        if (w == null && tryHere("outside, within the banker's hearing"))
            return null;
        w = WalkTo(banker[0], banker[1], BankerNear);
        if (w != null)
            return "walking to the banker at " + banker[0] + "," + banker[1] + ": " + w;
        if (tryHere("beside the banker"))
            return null;
        return "no banker answered \"bank\" at the bank at " + bank[0] + "," + bank[1] + " (" + said.Count + " places)";
    }
    // A recall to the bank by the pack's rune for its town; null once landed within RecallBankNear of it, else why not.
    public const int RecallBankNear = 40;
    string? RecallToBank(GameLink k, int[] bank)
    {
        string? city = UoRegions.City(bank[0], bank[1]);
        if (city == null)
            return "no walk to the bank, and it is in no town";
        foreach (int r in s_recallReagents)
            if (PackCount(k, g => g == r) < 1)
                return "no walk to the bank, and no " + UoTiles.Name(r) + " to recall with";
        uint rune = PackTownRune(k, city);
        if (rune == 0)
            return "no walk to the bank, and no rune for " + city + " in the pack";
        CircuitStatus = "Recalling to the bank in " + city + " by the pack's rune";
        Note("Bank: no walk to the bank in " + city + "; recalling by the pack's rune " + rune.ToString("X"));
        string? w = Recall(rune);
        if (w != null)
            return "recalling to the bank: " + w;
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        return Tiles(x, y, bank[0], bank[1]) <= RecallBankNear ? null
            : "the pack's rune for " + city + " landed " + Tiles(x, y, bank[0], bank[1]) + " tiles from its bank";
    }
    static bool InRects(List<int[]> rects, int x, int y)
    {
        foreach (int[] r in rects)
            if (x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3])
                return true;
        return false;
    }
    // To a free tile just outside the building (beside a tile of it, standable, no one within a tile): within reach of
    // near when given, nearest near first, else nearest the player; from minFrom to maxFrom tiles from `from` and at
    // least minFrom from every place in `apart`; BankSpotTries tried. Null once there.
    public const int BankRetryMin = 5, BankRetryMax = 8;
    string? ToBankSpot(GameLink k, List<int[]> rects, int[] near, int reach, int[]? from = null, int minFrom = 0,
        int maxFrom = int.MaxValue, List<int[]>? apart = null)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        List<int[]> spots = new();
        Set<long> seen = Set<long>.Empty;
        foreach (int[] r in rects)
        {
            for (int x = r[0] - 1; x <= r[0] + r[2]; x++)
            {
                for (int y = r[1] - 1; y <= r[1] + r[3]; y++)
                {
                    if (InRects(rects, x, y) || seen.Contains(TileKey(x, y)))
                        continue;
                    seen = seen.Add(TileKey(x, y));
                    if (near != null && Tiles(x, y, near[0], near[1]) > reach)
                        continue;
                    if (from != null
                        && (Tiles(x, y, from[0], from[1]) < minFrom || Tiles(x, y, from[0], from[1]) > maxFrom))
                        continue;
                    if (apart != null && apart.Exists(a => Tiles(x, y, a[0], a[1]) < minFrom))
                        continue;
                    if (UoMap.StandZ(x, y, UoMap.LandStandZ(x, y)) == int.MinValue || Crowded(k, x, y))
                        continue;
                    spots.Add(new[] { x, y });
                }
            }
        }

        if (spots.Count == 0)
            return "no free tile outside the bank"
                + (near != null && reach < int.MaxValue ? " within " + reach + " of " + near[0] + "," + near[1] : "")
                + (from != null
                ? " " + minFrom + (maxFrom < int.MaxValue ? " to " + maxFrom : " or more") + " tiles from " + from[0]
                + "," + from[1] : "");
        // Already on one: nothing to walk.
        if (spots.Exists(s => s[0] == px && s[1] == py))
            return null;
        spots.Sort((a, b) => near != null && Tiles(a[0], a[1], near[0], near[1]) != Tiles(b[0], b[1], near[0], near[1])
            ? Tiles(a[0], a[1], near[0], near[1]).CompareTo(Tiles(b[0], b[1], near[0], near[1]))
            : Tiles(a[0], a[1], px, py).CompareTo(Tiles(b[0], b[1], px, py)));
        string? w = null;
        foreach (int[] s in spots.GetRange(0, Math.Min(BankSpotTries, spots.Count)))
        {
            w = WalkTo(s[0], s[1], 0);
            if (w == null)
                return null;
        }
        return w;
    }
    // Anyone other than the player on the tile or beside it (Damian: "keep spacing of at least 1 between you and other
    // players").
    bool Crowded(GameLink k, int x, int y)
    {
        lock (k.State.Gate)
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                if (kv.Key != k.State.Player && Tiles(kv.Value[0], kv.Value[1], x, y) <= 1)
                    return true;
        return false;
    }
    // The bank's door nearest the player: a door or gate item in view on the building's edge.
    int[]? BankDoor(GameLink k, List<int[]> rects)
    {
        int px, py, best = int.MaxValue;
        int[]? door = null;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            foreach (AssistItem g in k.State.Ground.Values)
            {
                if (!IsDoor(g.Graphic))
                    continue;
                bool edge = false;
                for (int dx = -1; dx <= 1 && !edge; dx++)
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                        edge = InRects(rects, g.X + dx, g.Y + dy);
                if (edge && Tiles(g.X, g.Y, px, py) < best)
                {
                    best = Tiles(g.X, g.Y, px, py);
                    door = new[] { g.X, g.Y };
                }
            }
        }
        return door;
    }
    // The banker: the person in view inside the building nearest its centre (uo1998's bankers carry no title).
    int[]? BankerAt(GameLink k, List<int[]> rects, int[] bank)
    {
        int[]? at = null;
        int best = int.MaxValue;
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
            {
                int body;
                if (kv.Key == k.State.Player || !k.State.Bodies.TryGet(kv.Key, out body) || body < 0x190
                    || body > 0x191 || !InRects(rects, kv.Value[0], kv.Value[1]))
                    continue;
                int d = Tiles(kv.Value[0], kv.Value[1], bank[0], bank[1]);
                if (d < best)
                {
                    best = d;
                    at = new[] { kv.Value[0], kv.Value[1] };
                }
            }
        }

        return at;
    }
    // The banker's first name: the people in view named first, then the atlas's banker, or failing that the person
    // inside the building nearest the bank's centre (uo1998's bankers carry no title); null when none is known.
    string? BankerName(GameLink k, List<int[]> rects, int[] bank)
    {
        if (Atlas == null)
            return null;
        NameUnnamed(VendorHear * 4);
        AtlasNpc? best = null;
        bool bestBanker = false;
        foreach (AtlasNpc n in Atlas.Visible())
        {
            if (n.Name.Length == 0)
                continue;
            bool banker = n.Type == "Banker";
            // An untitled person is classed "Person" (AssistAtlas.Classify), as uo1998's bankers are.
            if (!banker && !((n.Type == "NPC" || n.Type == "Person") && InRects(rects, n.X, n.Y)))
                continue;
            if (!InRects(rects, n.X, n.Y) && Tiles(n.X, n.Y, bank[0], bank[1]) > BankerFind)
                continue;
            if (best == null || banker && !bestBanker
                || banker == bestBanker && Tiles(n.X, n.Y, bank[0], bank[1]) < Tiles(best.X, best.Y, bank[0], bank[1]))
            {
                best = n;
                bestBanker = banker;
            }
        }
        return best == null ? null : best.Name.Split(' ')[0];
    }
    // A box opened by a banker asked by name, kept as SayBank keeps "bank"'s, so the deposit, take or put that follows
    // uses it.
    void BankOpened(GameLink k, byte[] open)
    {
        m_lastBankOpen = open;
        m_lastBankOpenAt = Clock.UtcNow;
        lock (k.State.Gate)
        {
            m_lastBankX = k.State.X;
            m_lastBankY = k.State.Y;
        }
    }
    // "bank" from where the player stands, unless said from beside here already; true when the box opened. Each is a
    // new place, so the line may go again although the last went unanswered (k.Heard lifts GameLink.Say's repeat
    // guard).
    bool BankFrom(GameLink k, List<int[]> said, string where)
    {
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        if (said.Exists(s => Tiles(s[0], s[1], x, y) <= 1))
            return false;
        if (said.Count > 0)
            k.Heard("bank");
        said.Add(new[] { x, y });
        byte[]? open;
        using (Act(k))
        {
            open = SayBank(k);
        }
        if (open == null)
            Note("Bank: no answer to \"bank\" " + where + " (" + x + "," + y + ")");
        return open != null;
    }
    // "bank", or the box it opened from here in the last BankOpenFreshMs: the approach opens the box, and the deposit,
    // the take or the put that follows uses it without saying "bank" again.
    byte[] m_lastBankOpen = null!; DateTime m_lastBankOpenAt; int m_lastBankX, m_lastBankY;
    bool BankFresh(GameLink k)
    {
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        return m_lastBankOpen != null && (Clock.UtcNow - m_lastBankOpenAt).TotalMilliseconds < BankOpenFreshMs
            && Tiles(x, y, m_lastBankX, m_lastBankY) <= 1;
    }
    // The box "bank" opened, once its contents came (or AnswerMs passed).
    uint BankBox(byte[] open)
    {
        uint box = U32(open, 1);
        Wait.Until(Clock, () => { lock (m_contentsGate) return m_contents.ContainsKey(box); }, AnswerMs,
            "the box's contents", stepMs: 50);
        return box;
    }
    // The box opened for anything other than gold, with the gold over the purse put in (DepositWhileOpen).
    uint BankBoxDeposit(GameLink k, byte[] open)
    {
        uint box = BankBox(open);
        DepositWhileOpen(k);
        return box;
    }
    // The box and its containers two levels down (a wooden box of reagents in the bank box, Damian 2026-10-17), each
    // opened for its contents: a container not yet seen, or every one when fresh (a take or put counts what lies there
    // now). Each open waits out the last action ("You must wait to perform another action." twice at 08:42:57).
    // A container opened for its contents only when none have been seen.
    void PeekUnseen(uint bag)
    {
        bool known;
        lock (m_contentsGate)
            known = m_contents.ContainsKey(bag);
        if (!known)
        {
            BusyWait(clickedAt);
            Peek(bag);
        }
    }
    // The containers lying in the box itself, spellbooks apart, each opened once for its contents.
    List<uint> BoxBags(GameLink k, uint box)
    {
        List<uint> bags = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == box && IsContainer(it.Graphic) && !IsKey(it.Graphic) && it.Graphic != SpellbookArt)
                    bags.Add(it.Serial);
        foreach (uint b in bags)
            PeekUnseen(b);
        return bags;
    }
    // A key ring is a container by its tile and is never opened: its double-click asks "What do you want to unlock?"
    // with a cursor (2026-10-22 17:28:42, the ring 468656EB, art 1769, in the bank box at Minoc).
    Set<uint> BankTree(GameLink k, uint box, bool fresh)
    {
        Set<uint> tree = Set<uint>.Of(box);
        for (int depth = 0; depth < 2; depth++)
        {
            List<uint> bags = new();
            lock (k.State.Gate)
                foreach (AssistItem it in k.State.Items.Values)
                    if (tree.Contains(it.Container) && !tree.Contains(it.Serial) && IsContainer(it.Graphic)
                        && !IsKey(it.Graphic))
                        bags.Add(it.Serial);
            foreach (uint b in bags)
            {
                if (fresh)
                {
                    BusyWait(clickedAt);
                    string? w = Peek(b);
                    if (w != null)
                        Note("Bank: opening " + b.ToString("X") + ": " + w);
                }
                else
                {
                    PeekUnseen(b);
                }

                tree = tree.Add(b);
            }
        }
        return tree;
    }
    byte[]? SayBank(GameLink k)
    {
        if (BankFresh(k))
            return m_lastBankOpen;
        byte[]? open = SayFor(k, "bank", 0x24, AnswerMs);
        if (open != null)
        {
            m_lastBankOpen = open;
            m_lastBankOpenAt = Clock.UtcNow;
            lock (k.State.Gate)
            {
                m_lastBankX = k.State.X;
                m_lastBankY = k.State.Y;
            }
        }
        return open;
    }
    // To the nearest bank of the city here that answers, a bank whose walk failed passed over. Null, or why not.
    string? ToCityBank(GameLink k, string city, string why)
    {
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        string? w = "no bank known in " + city;
        foreach (int[] b in CityBanks(city, x, y))
        {
            if (BankSkipped(b))
                continue;
            CircuitStatus = "To the bank for " + why;
            w = ToBanker(k, b);
            if (w == null)
                return null;
            SkipBank(b);
        }
        return w;
    }
    // Tinker's tools bought in the town's tinker shops (region kind TINKER), nearest first, each walked into and its
    // keepers named and asked by name (Damian, 2026-10-18: "or tinker with tinker shop search like with smith vendor").
    // The arts are every 1.25 tile whose name says tinker. Null once the pack holds one, else why not.
    string? BuyTinkerTools(GameLink k)
    {
        return BuyTool(k, s_tinkerTools, new[] { "TINKER" });
    }
    // One tool of a profession bought in the town's shops of the region kinds given, nearest first, each walked into
    // and its keepers asked by name. Null once the pack holds one, else why not.
    string? BuyTool(GameLink k, Profession p, IReadOnlyList<string> kinds)
    {
        Func<int, bool> tool = p.IsTool;
        string what = p.ToolsName;
        string? city = CityOf(k);
        if (city == null)
            return "no " + what + ", and no town to buy them in";
        List<int> arts = new();
        foreach (UoTiles.Tile t in UoTiles.Items())
            if (tool(t.Graphic))
                arts.Add(t.Graphic);
        if (arts.Count == 0)
            return "no " + what + " art in the tile data";
        List<BuyItem> list = new()
        { new BuyItem { Name = what, Arts = arts.ToArray(), Count = 1, MaxPrice = int.MaxValue } };
        ValueMap<string, int> left = ValueMap<string, int>.Of((what, 1));
        int bought = 0;
        List<UoRegions.Shop> shops = UoRegions.ShopsIn(city, kinds);
        string? end = shops.Count == 0 ? "no " + string.Join(" or ", kinds).ToLowerInvariant() + " shop in " + city
            : null;
        while (shops.Count > 0 && left.Get(what, 0) > 0 && !circuitStop)
        {
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            shops.Sort((a, b) => Tiles(a.CX, a.CY, px, py).CompareTo(Tiles(b.CX, b.CY, px, py)));
            UoRegions.Shop s = shops[0];
            shops.RemoveAt(0);
            end = BuyInShop(k, s, list, ref left, ref bought);
            if (end != null)
                break;
        }
        if (PackCount(k, tool) > 0)
        {
            ToolsToBag(k, p);
            return null;
        }
        return "no " + what + " bought in " + city + (end != null ? ": " + end : "");
    }
    // KitsWant sewing kits from the nearest tailor in reach that sells them (a kit wears out; the circuit stalled with
    // bolts and no kit, 2026-10-17 10:08:36); 4 (Damian, 2026-10-19: "increase the sewing kit buys to 4 kits").
    public const int KitsWant = 4;
    DateTime m_spareKitAfter;
    string? BuyKits(GameLink k, int px, int py, int want = KitsWant)
    {
        if (Atlas == null)
            return "no atlas";
        List<AtlasNpc> sellers = Atlas.Shops().FindAll(n =>
            Tiles(n.X, n.Y, px, py) <= CircuitReach && TailorVendor(n) && !Skipped(n.Serial)
            && n.Items.Exists(s => s.IndexOf("sewing kit", StringComparison.OrdinalIgnoreCase) >= 0));
        sellers.Sort((a, b) => Tiles(a.X, a.Y, px, py).CompareTo(Tiles(b.X, b.Y, px, py)));
        string? w = "no tailor in reach sells sewing kits";
        foreach (AtlasNpc v in sellers)
        {
            if (circuitStop)
                break;
            if (ShownWithout(v.Serial, g => g == KitArt))
            {
                w = v.Name.Split(' ')[0] + ": the vendor has none";
                continue;
            }
            string name = v.Name.Split(' ')[0];
            CircuitStatus = "Buying sewing kits from " + name;
            int got, left;
            w = VendorBuy(g => g == KitArt, want, out got, out left, name);
            if (w == null && got > 0)
            {
                SurveyHere(k);
                ToolsToBag(k, s_tailorTools);
                return null;
            }
            if (w != null)
                Note("Circuit: " + name + ": " + w);
            // A tailor with no kit is an answer, and the next is asked ("in case for some reason that particular vendor
            // doesn't have one"); no proper answer stops the asking, and that tailor is not asked again for a restock
            // (the second 2026-10-24 audit: a silent kit seller was asked on every visit).
            if (Silent(w!))
            {
                SkipFor(v.Serial, s_restockSpan);
                return name + ": " + w;
            }
        }
        return w;
    }
    // A bank whose walk or "bank" failed, not tried again for its FailSpan (L-FUTILITY), by its tile.
    ValueMap<string, DateTime> m_bankSkip = ValueMap<string, DateTime>.Empty; readonly Lock m_bankSkipGate = new();
    // A lost connection is no fault of the bank: the client dropping mid-walk (2026-10-17 01:37:58) marked every Vesper
    // bank failed and the next circuit ended on them.
    void SkipBank(int[] bank)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return;
        lock (m_bankSkipGate)
            m_bankSkip = m_bankSkip.Set(bank[0] + "," + bank[1], Clock.UtcNow.ToLocalTime()
                + FailSpan("bank " + bank[0] + "," + bank[1]));
    }
    bool BankSkipped(int[] bank)
    {
        if (Broken("bank " + bank[0] + "," + bank[1]))
            return true;
        DateTime until;
        return m_bankSkip.TryGet(bank[0] + "," + bank[1], out until) && Clock.UtcNow.ToLocalTime() < until;
    }
    string? BankHere(GameLink k, string city, int keep = -1)
    {
        if (keep < 0)
            keep = PurseKeep;
        int x, y;
        lock (k.State.Gate)
        {
            x = k.State.X;
            y = k.State.Y;
        }
        List<int[]> banks = CityBanks(city, x, y);
        if (banks.Count == 0)
            return "no bank known in " + city;
        string? w = "every bank in " + city + " failed";
        foreach (int[] bank in banks)
        {
            if (circuitStop)
                return "stopped";
            if (BankSkipped(bank))
                continue;
            CircuitStatus = "Walking to the bank at " + bank[0] + "," + bank[1] + " in " + city;
            w = ToBanker(k, bank);
            if (w != null)
            {
                Note("Circuit: walking to the bank at " + bank[0] + "," + bank[1] + ": " + w);
                SkipBank(bank);
                continue;
            }
            int put;
            w = BankDeposit(keep, out put);
            if (w != null)
                SkipBank(bank);
            else
            {
                BankHeard(bank);
                TopOffRegs(k, city);
            }
            return w == null ? null : "the bank at " + bank[0] + "," + bank[1] + ": " + w;
        }
        return w;
    }
    static readonly ImmutableArray<string> s_tailorShopKinds = [ "CLOTHIER", "WEAVER" ],
        s_sellFirst = [ "Magincia", "Moonglow" ];
    public static ImmutableArray<string> TailorShopKinds => s_tailorShopKinds;
    static readonly ImmutableArray<string> s_mageShopKinds = [ "MAGE", "ALCHEMIST", "SCRIBE" ];
    public static ImmutableArray<string> MageShopKinds => s_mageShopKinds;
}
