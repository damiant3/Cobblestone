#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- setup ----------
    public string MineSetup(string[] a)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "ERROR no game connection";
        string what = a.Length > 1 ? a[1] : "";
        int px, py, pz;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
        }
        if (what == "forge")
        {
            // The forge must be in view: an item, else a static within 12 tiles, the nearest.
            int[]? best = null;
            int bd = int.MaxValue;
            lock (k.State.Gate)
                foreach (AssistItem g in k.State.Ground.Values)
                    if (IsForge(g.Graphic) && Tiles(g.X, g.Y, px, py) < bd)
                    {
                        bd = Tiles(g.X, g.Y, px, py);
                        best = new[] { 0, (int)g.Serial, g.X, g.Y, g.Z, g.Graphic };
                    }
            for (int x = px - 12; x <= px + 12; x++)
                for (int y = py - 12; y <= py + 12; y++)
                    if (Tiles(x, y, px, py) < bd)
                        foreach (int[] s in UoMap.StaticsAt(x, y))
                            if (IsForge(s[0]))
                            {
                                bd = Tiles(x, y, px, py);
                                best = new[] { 1, 0, x, y, s[1], s[0] };
                            }
            if (best == null)
                return "REFUSED no forge in view within 12 tiles: stand where you can see it";
            Cfg.Set(Server + "mine.forge", string.Join("|", best));
            return "OK forge " + UoTiles.Name(best[5]) + " " + best[5].ToString("X4") + " at " + best[2] + ","
                + best[3] + "," + best[4]
                + (best[0] == 0 ? " (item " + ((uint)best[1]).ToString("X") + ")" : " (static)");
        }
        if (what == "mode" && a.Length > 2)
        {
            MineMode = a[2] == "1" ? 1 : 2;
            return "OK mode " + MineMode + " shovel" + (MineMode == 1 ? "" : "s");
        }
        if (what == "toolbag")
        {
            string? w = ToolTarget(ToolPlaces.BagKey(s_miningTools));
            return w == null ? "OK target the mining toolbag in your pack" : "ERROR " + w;
        }
        if (what == "storage" && a.Length > 2)
        {
            if (a[2] == "backpack")
            {
                Cfg.Set(Server + "mine.store", "backpack");
                return "OK storage: the backpack; the circuit ends when the pack is full of ingots";
            }
            if (a[2] == "stash")
            {
                Cfg.Set(Server + "mine.store", "stash");
                return "OK storage: the ingot stash, " + StashText()
                    + (StashInBank && MineBankRune.Length == 0
                    ? "; set the bank rune (mine-setup bank-rune <label>), the circuit refuses to start without it"
                    : "");
            }
            if (a[2] == "target")
            {
                Cfg.Set(Server + "mine.store", "target");
                return "OK storage: the targeted place, " + MineStoreToText()
                    + (MineStoreTo.Length == 0 ? "; target it (mine-setup store-to)" : "");
            }
            if (a[2] == "vendor")
            {
                Cfg.Set(Server + "mine.store", "vendor");
                return "OK storage: the house vendor, for sale at "
                    + (MineVendorPrice > 0 ? MineVendorPrice + " gp an ingot"
                    : "NO PRICE: set one (mine-setup vendor-price <gp>)")
                    + (HouseVendorSerial == 0 ? "; NO HOUSE VENDOR: pick its house on the Houses tab (House vendor)"
                    : "");
            }
        }
        if (what == "store-to")
        {
            string? w = MineStoreToTarget();
            return w == null ? "OK target where the ingots go" : "ERROR " + w;
        }
        if (what == "ore-to")
        {
            string? w = MineOreToTarget();
            return w == null ? "OK target where the ore piles are collected" : "ERROR " + w;
        }
        if (what == "feet")
        {
            if (a.Length < 3 || (a[2] != "on" && a[2] != "off"))
                return "ERROR mine-setup feet on|off";
            MineAtFeet = a[2] == "on";
            return "OK " + MineOreText();
        }
        if (what == "collect-at" && a.Length > 2)
        {
            int n;
            if (!int.TryParse(a[2], out n))
                return "ERROR collect-at <ore>";
            MineCollectAt = n;
            return "OK collect-at " + MineCollectAt;
        }
        if (what == "house-ignore")
        {
            HomeIgnoreText = a.Length > 2 ? string.Join(" ", a, 2, a.Length - 2) : "";
            return "OK house-ignore " + (HomeIgnoreText.Length > 0 ? HomeIgnoreText : "none");
        }
        if (what == "vendor-price" && a.Length > 2)
        {
            int n;
            if (!int.TryParse(a[2], out n))
                return "ERROR vendor-price <gp an ingot>";
            MineVendorPrice = n;
            return "OK vendor-price " + MineVendorPrice;
        }
        if (what == "bank-rune" && a.Length > 2)
        {
            string label = string.Join(" ", a, 2, a.Length - 2);
            MineBankRune = label == "none" ? "" : label;
            return "OK bank-rune " + (MineBankRune.Length > 0 ? MineBankRune : "none");
        }
        if (what == "shovels" && a.Length > 2)
        {
            int n;
            if (!int.TryParse(a[2], out n))
                return "ERROR shovels <n>";
            MineCarry = n;
            return "OK shovels " + MineCarry;
        }
        if (what == "store-at" && a.Length > 2)
        {
            Cfg.Set(Server + "mine.store-at", a[2]);
            return "OK store-at " + a[2];
        }
        if (what == "cave" && a.Length > 2)
        {
            string rest = a.Length > 3 ? string.Join(" ", a, 3, a.Length - 3) : "";
            if (a[2] == "mark")
            {
                int r = 0;
                string[] parts = rest.Split(' ');
                if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out r))
                    rest = string.Join(" ", parts, 0, parts.Length - 1);
                return MarkCave(rest, r);
            }
            if (a[2] == "pick")
            {
                if (rest.Length > 0 && CaveNamed(rest) == null)
                    return "REFUSED no cave " + rest;
                PickedCave = rest;
                return "OK cave " + (rest.Length > 0 ? rest : "none: where the circuit starts");
            }
            if (a[2] == "rune")
            {
                int sp = rest.IndexOf(" | ");
                if (sp < 0)
                    return "ERROR cave rune <name> | <label>";
                return SetCaveRune(rest.Substring(0, sp).Trim(), rest.Substring(sp + 3));
            }
            if (a[2] == "forget")
                return ForgetCave(rest);
            if (a[2] == "show")
                return CaveText((rest.Length > 0 ? rest : PickedCave)!).Replace("\n", " ");
        }
        if (what == "caves")
            return string.Join(" | ", CaveNames());
        string store = MineStorage;
        return "forge " + MineCfg("forge", "none") + " ; mode " + MineMode + " ; toolbag "
            + (MineToolbag != 0 ? MineToolbag.ToString("X") : "none") + " ; storage " + store
            + (store == "stash"
            ? " (" + StashText() + (StashInBank ? ", by " + (MineBankRune.Length > 0 ? MineBankRune : "NO RUNE") : "")
            + ")" : store == "target" ? " (" + MineStoreToText() + ")" : "")
            + " ; shovels " + MineCarry + " ; store-at " + MineCfgInt("store-at", 500) + " ; cave "
            + (PickedCave!.Length > 0 ? PickedCave : "none") + " ; " + MineOreText()
            + (MineAtFeet ? " ; at feet " + MineDropped() + " ore in " + MinePileCount()
            + " piles, the collection at " + MineCollectAt : "");
    }
    // At my feet (MineGather): a full pack's ore goes on a pile beside the miner and the digging goes on; the piles are
    // carried to Ore to at Collect at and when every chunk is empty. Off, a full pack is smelted and stored.
    readonly MineGather m_mineGather;
    public const uint MineOreCursor = 0x4D4F5245;  // "MORE"
    public string MineOreTo
    {
        get => m_mineGather.PlaceSpec;
        set => m_mineGather.PlaceSpec = value;
    }
    public bool MineAtFeet
    {
        get => m_mineGather.AtFeet;
        set => m_mineGather.AtFeet = value;
    }
    public int MineCollectAt
    {
        get => m_mineGather.CollectAt;
        set => m_mineGather.CollectAt = value;
    }
    public int MineDropped()
    {
        return m_mineGather.Dropped();
    }
    public int MinePileCount()
    {
        return m_mineGather.Piles().Length;
    }
    public void MinePilesClear()
    {
        m_mineGather.ClearPiles();
    }
    public void MinePileAdd(int x, int y, int ore)
    {
        m_mineGather.AddPile(x, y, ore);
    }
    public string MineOreText()
    {
        return PlaceText(MineOreTo, "Ore to") + (MineAtFeet ? ", at my feet first" : "");
    }
    public string? MineOreToTarget()
    {
        return LumberCursor(MineOreCursor, "Target where the ore piles are collected: a tile, a bag or a pile.");
    }
    // A full pack at feet: the ore put down beside the miner, then the piles collected when due. Null, or why the
    // circuit ends.
    string? MineDropOre(GameLink k, MineCave c)
    {
        int x0, y0;
        lock (k.State.Gate)
        {
            x0 = k.State.X;
            y0 = k.State.Y;
        }
        string? sw = GatherStore(k, m_mineGather);
        if (sw != null)
            return "putting the ore down: " + sw;
        if (MineFull(k, 0))
        {
            int w8;
            lock (k.State.Gate)
                w8 = k.State.Weight;
            return "the pack is still full after putting the ore down: " + w8 + " of " + MineLimit(k) + " stones";
        }
        if (!m_mineGather.CollectDue)
            return null;
        return MineCollect(k, c, x0, y0);
    }
    // Collect now (Damian, 2026-10-25: "cave mining needs collect now button"), the logger's: during cave mining the
    // next step collects the piles at feet, Collect at or not, and the digging goes on; with no circuit running, the
    // collection alone.
    volatile bool m_mineCollectNow, m_mineCollectOnly;
    public string? MineCollectNow()
    {
        if (MinePileCount() == 0)
            return "no ore piles dropped to collect";
        if (CircuitRunning)
        {
            if (!m_mineMode)
                return "the running circuit is not cave mining";
            m_mineCollectNow = true;
            Note("Mine: collect now asked");
            return null;
        }
        return StartCircuit(() => { m_mineMode = true; m_mineCollectOnly = true; });
    }
    // Every ore pile carried to Ore to, then back to the dig. Null, or why the circuit ends.
    string? MineCollect(GameLink k, MineCave c, int x0, int y0)
    {
        CircuitStatus = "Mine: collecting " + MineDropped() + " ore in " + MinePileCount() + " piles";
        string? cw = GatherCollect(k, m_mineGather);
        if (cw != null)
            return "the collection: " + cw;
        return MineBack(k, c, x0, y0);
    }

    // ---------- the circuit ----------
    // To the cave first, then off the mount: mounted, the miner leaves the mount at the back of the cave, where the
    // circuit starts, told to stop, and mounts it again when the circuit ends (Damian, 2026-10-18: "if the player is
    // mounted, the character should dismount at the back of the cave and tell the mount to stop. when its time to
    // finalize the circuit, then mount the horse again").
    string RunMine(GameLink k)
    {
        if (m_mineCollectOnly)
        {
            m_mineCollectOnly = false;
            m_mineCollectNow = false;
            m_mineMode = false;
            CircuitStatus = "Mine: collecting " + MineDropped() + " ore in " + MinePileCount() + " piles";
            string? ow = GatherCollect(k, m_mineGather);
            return ow == null ? "collected" : "the collection: " + ow;
        }
        MineCave? cave = PickedCave!.Length > 0 ? CaveNamed(PickedCave) : null;
        if (PickedCave.Length > 0 && cave == null)
        {
            m_mineMode = false;
            return "the picked cave " + PickedCave + " is not in the cave list";
        }
        if (MineCfg("forge").Split('|').Length < 6)
        {
            m_mineMode = false;
            return "no forge set up (stand where you can see it and press Forge)";
        }
        if (MineStorage == "stash" && StashInBank)
        {
            if (MineBankRune.Length == 0)
            {
                m_mineMode = false;
                return "an ingot stash in the bank box needs a bank rune in the pack: set its label (no running to "
                    + "the bank)";
            }
            if (PackBankRune(k) == 0)
            {
                m_mineMode = false;
                return "no rune labelled \"" + MineBankRune + "\" in the pack (no running to the bank)";
            }
        }
        if (MineStorage == "vendor" && (HouseVendorSerial == 0 || MineVendorPrice <= 0))
        {
            m_mineMode = false;
            return "vendor storage needs the house vendor set (Houses tab, House vendor) and a price per ingot";
        }
        if (MineStorage == "target" && MineStoreTo.Split('|').Length < 2)
        {
            m_mineMode = false;
            return "targeted storage needs its place: target it with Store to";
        }
        // The mining toolbox's spot noted while it is in view at the start (MineBoxSpot).
        MineBoxSpot(k);
        uint bag = MineToolbag;
        bool lying;
        lock (k.State.Gate)
            lying = bag != 0 && k.State.Ground.ContainsKey(bag);
        if (bag != 0 && !lying)
        {
            bool inPack;
            lock (k.State.Gate)
                inPack = PackTree(k).Contains(bag);
            if (!inPack)
            {
                m_mineMode = false;
                return "the tool bag " + bag.ToString("X") + " is not in the pack: pick it again";
            }
        }
        if (cave != null)
        {
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            if (Tiles(px, py, cave.X, cave.Y) > cave.Radius)
            {
                if (cave.Rune.Length == 0 && !CaveWalkable(cave, px, py))
                {
                    m_mineMode = false;
                    return "you are " + Tiles(px, py, cave.X, cave.Y) + " tiles from the cave " + cave.Name
                        + ", and it has no rune: walk in, or set its rune";
                }
                string? cw = CaveBack(k, cave);
                if (cw != null)
                {
                    m_mineMode = false;
                    return "to the cave " + cave.Name + ": " + cw;
                }
            }
        }
        uint horse = 0;
        if (Riding(k))
        {
            string? dw = Dismount(k, out horse);
            if (dw != null)
            {
                m_mineMode = false;
                return "dismounting: " + dw;
            }
        }
        try
        {
            return RunMineOn(k, cave!);
        }
        finally
        {
            // A circuit stopped (by Stop, the player's own step, or an alarm) walks the player nowhere: the mount is
            // left.
            if (horse != 0 && !k.Closed)
            {
                if (circuitStop)
                {
                    int[]? at;
                    k.State.Mobiles.TryGet(horse, out at);
                    Note("Mine: the circuit was stopped; the mount waits"
                        + (at != null ? " at " + at[0] + "," + at[1] : " where it was left"));
                }
                else
                {
                    string? mw = Remount(k, horse);
                    Note("Mine: " + (mw ?? "mounted again"));
                }
            }
        }
    }
    // Off the mount, its serial out, told to stop by its name ("all stop" when it has none). Null, or why not.
    string? Dismount(GameLink k, out uint horse)
    {
        horse = MountSerial();
        uint me;
        lock (k.State.Gate)
            me = k.State.Player;
        Map<uint, int[]> before = k.State.Mobiles;
        BusyWait(clickedAt);
        using (Act(k))
        {
            string? why = k.Use(me, false);
            clickedAt = Clock.UtcNow;
            if (why != null)
                return why;
        }
        Wait.Until(Clock, () => !Riding(k), MountWaitMs, "the dismount", stepMs: 50);
        if (Riding(k))
            return "the dismount did not happen";
        DateTime seenBy = Clock.UtcNow.AddMilliseconds(MountWaitMs);
        bool inView = false;
        uint h = horse;
        while (!inView && Clock.UtcNow < seenBy)
        {
            lock (k.State.Gate)
            {
                if (h != 0 && k.State.Mobiles.ContainsKey(h))
                    inView = true;
                else
                    foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                        if (!before.ContainsKey(kv.Key) && Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) <= 2)
                        {
                            h = kv.Key;
                            inView = true;
                            break;
                        }
            }
            if (!inView)
                Clock.Sleep(50);
        }
        horse = h;
        if (!inView)
            return "the mount is not in view after the dismount";
        string name = "";
        lock (m_pets)
        {
            Pet? p = PetOf(h);
            if (p != null)
                name = p.Name;
        }
        k.Say((name.Length > 0 ? name : "all") + " stop");
        Note("Mine: dismounted; " + (name.Length > 0 ? name : "the mount") + " told to stop");
        return null;
    }
    // Back to the mount where it stands and onto it. Null, or why not.
    string? Remount(GameLink k, uint horse)
    {
        int[]? at;
        if (!k.State.Mobiles.TryGet(horse, out at))
            at = null;
        else
            at = new[] { at[0], at[1] };
        if (at == null)
            return "the mount " + horse.ToString("X") + " is not in view; it waits where it was left";
        CircuitStatus = "Mine: back to the mount";
        string? w = WalkTo(at[0], at[1], 1);
        if (w != null)
            return "walking to the mount: " + w;
        BusyWait(clickedAt);
        using (Act(k))
        {
            w = k.Use(horse, false);
            clickedAt = Clock.UtcNow;
            if (w != null)
                return "mounting: " + w;
        }
        Wait.Until(Clock, () => Riding(k), MountWaitMs, "the mount", stepMs: 50);
        return Riding(k) ? null : "mounting did not happen; the mount waits beside you";
    }
    // A dig sent and not yet answered: its shovel, chunk and tile.
    sealed class DigFlight
    {
        public uint Shovel; public long Zone; public int X, Y; public DateTime At;
    }
    // Inside the cave: a floor static stood on under the mountain, the land rock or void (Damian, 2026-10-20: "a cave
    // is characterized by a cave opening, a narrow doorway into and under this mountain ... the idea of staying in the
    // cave is it can be safer"). A cave with such floor in its radius is mined only from it: the circuit had dug the
    // mountainside from the grass outside (2456,81, 19:03).
    static bool InCave(int x, int y)
    {
        if (!UoMap.LandBlocks(x, y) && !UoMap.LandVoid(x, y))
            return false;
        List<UoTiles.Tile> items = UoTiles.Items();
        foreach (int[] s in UoMap.StaticsAt(x, y))
            if (s[0] >= 0 && s[0] < items.Count && (items[s[0]].Flags & 0x200) != 0
                && (items[s[0]].Flags & TileImpassable) == 0)
                return true;
        return false;
    }
    volatile bool m_mineInside;
    string RunMineOn(GameLink k, MineCave cave)
    {
        try
        {
            string[] forge = MineCfg("forge").Split('|');
            string store = m_mineStoreFor ?? MineStorage;
            int mode = MineMode;
            MineCave here = cave ?? new MineCave();
            int cx, cy;
            if (cave != null)
            {
                cx = cave.X;
                cy = cave.Y;
            }
            else
            {
                lock (k.State.Gate)
                {
                    cx = k.State.X;
                    cy = k.State.Y;
                }
                here.X = cx;
                here.Y = cy;
            }
            Map<long, int[]> grid = MineGrid(cx, cy, here.Radius + MineReach);
            if (grid.Count == 0)
                return "no ground a shovel can dig within " + (here.Radius + MineReach) + " tiles of " + cx + "," + cy;
            m_mineInside = false;
            for (int x = cx - here.Radius; x <= cx + here.Radius && !m_mineInside; x++)
                for (int y = cy - here.Radius; y <= cy + here.Radius; y++)
                    if (InCave(x, y))
                    {
                        m_mineInside = true;
                        break;
                    }
            List<DigFlight> flights = new();
            Set<long> bad = Set<long>.Empty;
            bool boxTakeFailed = false, lowTripFailed = false;
            // A circuit's start trusts no rest: every chunk is probed, the sentinel first.
            if (cave != null)
            {
                lock (m_caves)
                {
                    here.Sentinel = DoorZone(grid, here, bad);
                    here.RestCleared = Clock.UtcNow.Ticks;
                }
                m_sentinelArmed = here.Sentinel == long.MinValue;
            }
            ValueMap<long, long> lastTile = ValueMap<long, long>.Empty;
            ValueMap<long, int>? order = null;
            long lastZone = long.MinValue;
            DateTime planAt = DateTime.MinValue;
            bool planDue = false;
            Heard jmark = new(k.State);
            m_mineIngotsMade = 0;
            m_mineIngotsStored = 0;
            m_mineVendorFull = false;
            m_mineStashFull = false;
            int ores = 0, unanswered = 0, oresAtCollect = -1;
            LoopKind? digKind = Find(mode == 2 ? "Mining (2 tools)" : "Mining (1 tool)");
            DateTime lookAt = DateTime.MinValue;
            Note("Mine: " + mode + " shovel" + (mode == 1 ? "" : "s") + ", "
                + (cave != null ? "the cave " + cave.Name : "around " + cx + "," + cy) + ", " + grid.Count
                + " diggable tiles, " + (MineAtFeet ? MineOreText() : "storage " + store));
            // At feet, the collection's place is checked before the first dig, and a collection already due (a resume)
            // runs first, as the lumberjack's.
            if (MineAtFeet)
            {
                int ox, oy, oz;
                uint oo;
                string? pw = CollectPlace(k, m_mineGather, out ox, out oy, out oz, out oo);
                if (pw != null)
                    return pw;
                m_mineGather.Stored = 0;
                if (m_mineGather.CollectDue)
                {
                    string? cw = MineCollect(k, here, cx, cy);
                    if (cw != null)
                        return cw;
                }
            }
            while (!circuitStop && !k.Closed)
            {
                string? end = MineResults(k, here, flights, ref bad, ref jmark, ref ores, ref unanswered);
                if (end != null)
                    return end;
                if (unanswered >= MineUnansweredMax)
                    return MineUnansweredMax + " digs in a row had no answer from the server";
                // Collect now asked: once every dig in flight is answered.
                if (m_mineCollectNow && flights.Count == 0)
                {
                    m_mineCollectNow = false;
                    int nx, ny;
                    lock (k.State.Gate)
                    {
                        nx = k.State.X;
                        ny = k.State.Y;
                    }
                    string? nw = MineCollect(k, here, nx, ny);
                    if (nw != null)
                        return nw;
                    continue;
                }
                if (Clock.UtcNow > lookAt)
                {
                    CaveLook(k, cave!);
                    if (cave != null)
                        SaveCaves();
                    lookAt = Clock.UtcNow.AddSeconds(CaveLookSeconds);
                }
                MineStatus = ores + " ore digs this run, " + m_mineIngotsMade + " ingots smelted, "
                    + m_mineIngotsStored + " stored (" + store + "); " + flights.Count + " in flight";
                // Every dig answered before anything leaves the spot: a dig still out lands its ore after a move.
                List<uint> shovels = Shovels(k);
                // Down to 1 shovel, the mining toolbox lying in the cave tops the bag back up (Damian, 2026-10-21: "as
                // soon as you drop to 1 shovel you should go fetch 2"), once the dig in flight is answered; a fetch
                // that brings none is not tried again this run (L-FUTILITY).
                if (cave != null && !boxTakeFailed && shovels.Count <= 1 && BoxInCave(k, here))
                {
                    if (flights.Count > 0)
                    {
                        Clock.Sleep(50);
                        continue;
                    }
                    string? bw = CaveToolbox(k, here);
                    if (Shovels(k).Count > shovels.Count)
                        continue;
                    Note("Mine: shovels from the mining toolbox: " + (bw ?? "none came"));
                    boxTakeFailed = true;
                    continue;
                }
                // Two shovels with fewer than 2 in the pack, the bag and the box together (the box fetched from above,
                // or none in the cave) make the shovel trip at once (Damian, 2026-10-22: "we should trigger tinker
                // store run in 2 shovel mode when pack, bag and box is sum less than 2"); a trip that brings none is
                // not made again this run, and the one shovel digs on (L-FUTILITY).
                bool lowShovels = mode == 2 && shovels.Count == 1 && !lowTripFailed
                    && (cave == null || boxTakeFailed || !BoxInCave(k, here));
                if (shovels.Count == 0 || lowShovels)
                {
                    if (flights.Count > 0)
                    {
                        Clock.Sleep(50);
                        continue;
                    }
                    int had = shovels.Count;
                    string? sw = MineResupply(k, here, forge, store);
                    if (sw != null)
                        return sw;
                    // The trip can have filled the box (ShovelsToBox): it is fetched from again (2026-10-22 17:45:39, a
                    // second trip with 12 in the box, the box marked empty since 17:36).
                    boxTakeFailed = false;
                    if (had > 0 && Shovels(k).Count <= had)
                    {
                        lowTripFailed = true;
                        Note("Mine: the shovel trip brought none; one shovel digs on this run");
                    }
                    continue;
                }
                if (MineFull(k, flights.Count))
                {
                    if (flights.Count > 0)
                    {
                        Clock.Sleep(50);
                        continue;
                    }
                    if (MineAtFeet)
                    {
                        string? fw = MineDropOre(k, here);
                        if (fw != null)
                            return fw;
                        continue;
                    }
                    string? uw = MineUnload(k, here, forge, store);
                    if (uw != null)
                        return uw;
                    if (MineFull(k, 0))
                    {
                        int w8;
                        lock (k.State.Gate)
                            w8 = k.State.Weight;
                        return "the pack is still full after unloading: " + w8 + " of " + MineLimit(k) + " stones";
                    }
                    continue;
                }
                uint shovel = 0;
                foreach (uint s in shovels)
                    if (!flights.Exists(f => f.Shovel == s))
                    {
                        shovel = s;
                        break;
                    }
                if (shovel == 0 || flights.Count >= mode)
                {
                    Clock.Sleep(50);
                    continue;
                }
                int px, py;
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                }
                // The sentinel emptied before any other chunk: until it answers "no metal", every dig is its chunk's.
                long only = long.MinValue;
                if (cave != null && !m_sentinelArmed)
                {
                    MineZone? sz;
                    bool done;
                    lock (m_caves)
                        done = here.Zones.TryGet(here.Sentinel, out sz) && Resting(here, sz);
                    if (done)
                    {
                        m_sentinelArmed = true;
                        Note("Mine: the doorway chunk " + ZoneText(here.Sentinel) + " is empty; the sentinel is set");
                    }
                    else
                    {
                        only = here.Sentinel;
                    }
                }
                if (only != long.MinValue && flights.Count == 0
                    && MineTarget(grid, here, flights, bad, lastTile, px, py, only) == null)
                {
                    string? sw = SentinelDig(k, grid, here, ref bad, flights, ref jmark, ref ores, ref unanswered);
                    if (sw != null)
                        return "emptying the doorway chunk: " + sw;
                    continue;
                }
                if (m_mineInside && flights.Count == 0 && !InCave(px, py))
                {
                    CircuitStatus = "Mine: into the cave";
                    string? iw = MineWalk(k, grid, here, cx, cy, bad, mode);
                    if (iw != null)
                        return "into the cave: " + iw;
                    continue;
                }
                // Two shovels in two chunks (MinePlanner; Damian, 2026-10-22: "digging with one shovel should only
                // happen when only 1 shovel is available or there is no available adjacent spawn 8x8 chunks"): the free
                // shovel takes a chunk of its own in reach by the depletion order; with none, it waits for the dig in
                // flight and the miner walks to the best stand ranked, carrying the chunk still being dug. Only with no
                // such stand does it dig a second spot of the other's chunk (Damian, 2026-10-18: "if only 1 viable
                // region is left, dig in 2 spots"). A walk that fails waits a minute. The stands are ranked at most
                // every PlanEveryMs while waiting.
                int[]? t = null;
                if (only == long.MinValue && mode == 2 && shovels.Count >= 2)
                {
                    if (order == null)
                        order = MinePlanner.Order(CaveChunks(grid), ZoneKey(px, py));
                    // A move decided while a dig was out is made the moment the digs drain, before a new dig is chosen:
                    // the drained shovel's chunk is free again then, and choosing first kept one shovel on it until it
                    // emptied (2026-10-22 17:39:33 to 17:40:12, tile 2471,63 from 2472,64, the other shovel idle).
                    if (planDue && flights.Count == 0 && Clock.UtcNow > m_betterAfter)
                    {
                        planDue = false;
                        long? held = lastZone != long.MinValue
                            && ZonesInReach(grid, here, bad, px, py).ContainsKey(lastZone) ? lastZone : (long?)null;
                        ImmutableArray<MineStand> ahead = PlanStands(k, grid, here, cx, cy, bad, held, order, px, py);
                        planAt = Clock.UtcNow.AddMilliseconds(PlanEveryMs);
                        if (ahead.Length > 0)
                        {
                            string? pw = PlanWalk(ahead);
                            if (pw == null)
                                continue;
                            Note("Mine: no planned stand was reached: " + pw);
                            m_betterAfter = Clock.UtcNow.AddMinutes(1);
                        }
                    }
                    t = PlanTarget(grid, here, flights, bad, lastTile, px, py, order);
                    if (t == null && Clock.UtcNow > m_betterAfter)
                    {
                        long? carry = flights.Count > 0 ? flights[0].Zone
                            : lastZone != long.MinValue && ZonesInReach(grid, here, bad, px, py).ContainsKey(lastZone)
                            ? lastZone : (long?)null;
                        if (flights.Count > 0)
                        {
                            if (Clock.UtcNow >= planAt)
                            {
                                planDue = PlanStands(k, grid, here, cx, cy, bad, carry, order, px, py).Length > 0;
                                planAt = Clock.UtcNow.AddMilliseconds(PlanEveryMs);
                            }
                            if (planDue)
                            {
                                Clock.Sleep(50);
                                continue;
                            }
                        }
                        else
                        {
                            ImmutableArray<MineStand> stands = PlanStands(k, grid, here, cx, cy, bad, carry, order, px,
                                py);
                            planDue = false;
                            planAt = Clock.UtcNow.AddMilliseconds(PlanEveryMs);
                            if (stands.Length > 0)
                            {
                                string? pw = PlanWalk(stands);
                                if (pw == null)
                                    continue;
                                Note("Mine: no planned stand was reached: " + pw);
                                m_betterAfter = Clock.UtcNow.AddMinutes(1);
                            }
                        }
                    }
                }
                if (t == null)
                    t = MineTarget(grid, here, flights, bad, lastTile, px, py, only);
                if (t == null && only != long.MinValue)
                {
                    Clock.Sleep(50);
                    continue;
                }
                if (t == null)
                {
                    if (flights.Count > 0)
                    {
                        Clock.Sleep(50);
                        continue;
                    }
                    string? ww = MineWalk(k, grid, here, cx, cy, bad, mode);
                    // Every chunk empty: the pack's ore put down and every pile collected before the wait or the end,
                    // Collect at or not (the lumberjack's "instead of just stopping, it should collect first").
                    // Once until the next ore: a collection that left piles is not asked again (L-FUTILITY).
                    if (ww != null && ww.StartsWith(NoMetalInReach) && MineAtFeet && ores != oresAtCollect
                        && (PackHolds(k, m_mineGather.Gathered) || MinePileCount() > 0))
                    {
                        oresAtCollect = ores;
                        int ex, ey;
                        lock (k.State.Gate)
                        {
                            ex = k.State.X;
                            ey = k.State.Y;
                        }
                        if (PackHolds(k, m_mineGather.Gathered))
                        {
                            string? sw = GatherStore(k, m_mineGather);
                            if (sw != null)
                                return ww + "; putting the ore down: " + sw;
                        }
                        Note("Mine: " + ww + "; collecting the piles first");
                        string? cw = MineCollect(k, here, ex, ey);
                        if (cw != null)
                            return ww + "; " + cw;
                    }
                    if (ww != null && cave != null && ww.StartsWith(NoMetalInReach))
                    {
                        string? sw = MineSentinel(k, grid, here, ref bad, flights, ref jmark, ref ores, ref unanswered);
                        if (sw != null)
                            return sw + "; " + ww;
                        continue;
                    }
                    if (ww != null)
                        return ww;
                    continue;
                }
                DateTime due = m_lastUse.AddMilliseconds(Delay(digKind!));
                Wait.Till(Clock, due, () => circuitStop);
                if (circuitStop)
                    break;
                string? dw = MineDig(k, shovel, t, mode, flights);
                if (dw == "again")
                    continue;
                if (dw == "no cursor")
                {
                    unanswered++;
                    continue;
                }
                if (dw != null)
                    return dw;
                lastTile = lastTile.Set(ZoneKey(t[0], t[1]), TileKey(t[0], t[1]));
                lastZone = ZoneKey(t[0], t[1]);
            }
            return circuitStop ? "stopped" : "the connection closed";
        }
        finally { m_mineMode = false; if (cave != null) SaveCaves(); }
    }
    // The sentinel (Damian, 2026-10-21: "you should always strike and empty the sentinel first. if it is first strike
    // of circuit, don't trust it. an empty sentinel means nothing. you still must probe every chunk at that point. but
    // once an empty sentinel strikes ore, the whole cave is reset fresh and you can rely on that, not time estimates";
    // "the sentinel is generally defined as the first chunk you encounter running in the cave door"): the chunk nearest
    // the cave's mark, emptied before any other at a circuit's start and after every strike (sentinelArmed). mining.m's
    // "no metal" is the chunk egg's metal under 2 (getResource "metal", 3, 2).
    volatile bool m_sentinelWait, m_sentinelArmed;
    // The sentinel's chunk: the diggable tile nearest the cave's mark; long.MinValue when none.
    static long DoorZone(Map<long, int[]> grid, MineCave c, Set<long> bad)
    {
        int[]? door = null;
        foreach (int[] g in grid.Values)
            if (!bad.Contains(TileKey(g[0], g[1]))
                && (door == null || Tiles(g[0], g[1], c.X, c.Y) < Tiles(door[0], door[1], c.X, c.Y)))
                door = g;
        return door == null ? long.MinValue : ZoneKey(door[0], door[1]);
    }
    // A chunk emptied since the last strike that gives ore again is the whole cave's respawn: every chunk is fresh, the
    // sentinel is emptied again first, and the time since the last strike measures the cycle (its shortest is kept).
    public const int StrikeGapMinMinutes = 2;
    public void Strike(MineCave c, long now, string why)
    {
        long gap = c.LastStrike > 0 ? now - c.LastStrike : 0;
        // A gap across an absence is many cycles (Damian, 2026-10-22: "after i went to sleep and came back to the cave,
        // it interpreted that as a continuation"): only a gap under one and a half server cycles is measured.
        if (gap > TimeSpan.FromMinutes(StrikeGapMinMinutes).Ticks && gap < ServerCycleTicks * 3 / 2
            && (c.Cycle == 0 || gap < c.Cycle))
            c.Cycle = gap;
        c.StrikeFrom = c.LastEmpty > c.LastStrike && c.LastEmpty < now ? c.LastEmpty : 0;
        c.LastStrike = now;
        c.RestCleared = now;
        m_sentinelArmed = false;
        Note("Mine: " + why + ": the cave respawned, every chunk is fresh"
            + (c.Cycle > 0 ? " (respawn every " + c.Cycle / TimeSpan.TicksPerMinute + " min at most)" : ""));
    }
    // The 1998 server's regrow pass starts every 0x1000 ticks of 250 ms (OUO resbank.c, RespawnTimerCheck), 1024 s: the
    // cycle until one is measured here.
    public const long ServerCycleTicks = 1024 * TimeSpan.TicksPerSecond;
    // A gap between two strikes seen spans whole server cycles, so a measured cycle longer than the server's is several
    // of them (HomeCave's record held 2537 min, 2026-10-22, and every chunk read "back in 2516 min"): the cycle in use
    // is the measured one only when it is shorter.
    long CycleOf(MineCave c)
    {
        return c.Cycle > 0 && c.Cycle < ServerCycleTicks ? c.Cycle : ServerCycleTicks;
    }
    // The earliest the next respawn can come: from StrikeFrom (else the strike) on by CycleOf; 0 before any strike.
    public long NextStrike(MineCave c)
    {
        if (c.LastStrike <= 0)
            return 0;
        long cycle = CycleOf(c), from = c.StrikeFrom > 0 ? c.StrikeFrom : c.LastStrike;
        long n = from + cycle, now = Clock.UtcNow.Ticks;
        while (n + cycle < now)
            n += cycle;
        return n;
    }
    // Every chunk empty: the sentinel dug every SentinelPollFarS, every SentinelPollNearS within SentinelNearMinutes of
    // the respawn the cycle predicts (and with none measured), SentinelMaxMinutes at most, until a strike
    // (MineResults). The player's own digs count meanwhile (sentinelWait). Null to mine on.
    public const int SentinelPollFarS = 60, SentinelPollNearS = 30, SentinelNearMinutes = 3, SentinelMaxMinutes = 120;
    string? MineSentinel(GameLink k, Map<long, int[]> grid, MineCave c, ref Set<long> bad,
        List<DigFlight> flights, ref Heard jmark, ref int ores, ref int unanswered)
    {
        if (c.Sentinel == long.MinValue)
            return "no diggable chunk for a sentinel";
        string at = ZoneText(c.Sentinel);
        long strike0 = c.LastStrike;
        DateTime until = Clock.UtcNow.AddMinutes(SentinelMaxMinutes);
        long due = 0;
        long next0 = NextStrike(c);
        Note("Mine: every chunk is empty; polling the doorway chunk " + at
            + (next0 > 0
            ? ", the respawn expected in " + Math.Max(0, (next0 - Clock.UtcNow.Ticks) / TimeSpan.TicksPerMinute)
            + " min" : ""));
        m_sentinelWait = true;
        try
        {
            while (!circuitStop && !k.Closed)
            {
                if (c.LastStrike != strike0)
                    return null;
                if (Clock.UtcNow > until)
                    return "no respawn in " + SentinelMaxMinutes + " min of polling the doorway chunk " + at;
                if (Clock.UtcNow.Ticks < due)
                {
                    CircuitStatus = "Mine: every chunk empty; the doorway chunk " + at + " dug in "
                        + ((due - Clock.UtcNow.Ticks) / TimeSpan.TicksPerSecond + 1) + " s";
                    Clock.Sleep(1000);
                    continue;
                }
                m_sentinelWait = false;
                string? w = SentinelDig(k, grid, c, ref bad, flights, ref jmark, ref ores, ref unanswered);
                m_sentinelWait = true;
                if (w != null)
                    return w;
                if (c.LastStrike != strike0)
                    return null;
                long next = NextStrike(c), now = Clock.UtcNow.Ticks;
                bool near = next == 0 || next - now < TimeSpan.FromMinutes(SentinelNearMinutes).Ticks;
                due = now + TimeSpan.FromSeconds(near ? SentinelPollNearS : SentinelPollFarS).Ticks;
            }
        }
        finally { m_sentinelWait = false; }
        return circuitStop ? "stopped" : "the connection closed";
    }
    // One dig of the sentinel's chunk, walked to when out of reach, its answer awaited. Null, or why the circuit ends.
    string? SentinelDig(GameLink k, Map<long, int[]> grid, MineCave c, ref Set<long> bad,
        List<DigFlight> flights, ref Heard jmark, ref int ores, ref int unanswered)
    {
        string at = ZoneText(c.Sentinel);
        List<int[]> tiles = new();
        foreach (int[] g in grid.Values)
            if (ZoneKey(g[0], g[1]) == c.Sentinel && !bad.Contains(TileKey(g[0], g[1])))
                tiles.Add(g);
        if (tiles.Count == 0)
            return "no diggable tile left in the doorway chunk " + at;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        tiles.Sort((a, b) => Tiles(a[0], a[1], px, py).CompareTo(Tiles(b[0], b[1], px, py)));
        int[] t = tiles[0];
        if (Tiles(t[0], t[1], px, py) > MineReach)
        {
            CircuitStatus = "Mine: to the doorway chunk " + at;
            string? ww = WalkTo(t[0], t[1], 1);
            if (ww != null)
                return "to the doorway chunk " + at + ": " + ww;
        }
        List<uint> shovels = Shovels(k);
        if (shovels.Count == 0)
            return "no shovel to dig the doorway chunk";
        Wait.Till(Clock, m_lastUse.AddMilliseconds(Delay(Find("Mining (1 tool)")!)), () => circuitStop);
        string? dw = MineDig(k, shovels[0], t, 1, flights);
        if (dw == "again")
        {
            Clock.Sleep(BusyMs);
            return null;
        }
        if (dw == "no cursor")
            return ++unanswered >= MineUnansweredMax
                ? MineUnansweredMax + " digs in a row had no answer from the server" : null;
        if (dw != null)
            return dw;
        DateTime by = Clock.UtcNow.AddMilliseconds(PendingDigMs + 500);
        while (flights.Count > 0 && Clock.UtcNow < by)
        {
            string? e = MineResults(k, c, flights, ref bad, ref jmark, ref ores, ref unanswered);
            if (e != null)
                return e;
            Clock.Sleep(100);
        }
        return null;
    }
    // Every tile a shovel can dig within r of the centre: x, y, z, and the graphic to target (0 for land, mining.m
    // reading the land itself); a mineable static first, as the client targets the static over the land.
    static Map<long, int[]> MineGrid(int cx, int cy, int r)
    {
        Map<long, int[]> grid = Map<long, int[]>.Empty;
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                int[]? hit = null;
                foreach (int[] s in UoMap.StaticsAt(x, y))
                    if (InRanges(s_mineStatic, s[0]))
                    {
                        hit = new[] { x, y, s[1], s[0] };
                        break;
                    }
                if (hit == null)
                {
                    int land = UoMap.LandIdAt(x, y);
                    if (land >= 0 && InRanges(s_mineLand, land))
                        hit = new[] { x, y, UoMap.LandZ(x, y), 0 };
                }
                if (hit != null)
                    grid = grid.Set(TileKey(x, y), hit);
            }
        }

        return grid;
    }
    // A chunk rests from its "no metal" until the next strike; no chunk respawns on its own clock.
    static bool Resting(MineCave c, MineZone z)
    {
        return z.EmptyAt > c.RestCleared && z.LastOreAt < z.EmptyAt;
    }
    // When a resting chunk has metal again: the next respawn the cycle predicts (0 unknown, and 0 for a chunk not
    // resting).
    long BackAt(MineCave c, MineZone z)
    {
        return Resting(c, z) ? NextStrike(c) : 0;
    }
    string Ago(long ticks)
    {
        if (ticks == 0)
            return "";
        long m = (Clock.UtcNow.Ticks - ticks) / TimeSpan.TicksPerMinute;
        return m < 1 ? "just now" : m < 120 ? m + " min ago" : (m / 60) + " h ago";
    }
    string BackText(MineCave c, MineZone z)
    {
        if (!Resting(c, z))
            return "has metal";
        long at = NextStrike(c);
        if (at == 0)
            return "at the next respawn";
        long m = (at - Clock.UtcNow.Ticks) / TimeSpan.TicksPerMinute;
        return m <= 0 ? "at the respawn, due now" : "at the respawn, in " + m + " min";
    }
    static bool ZoneResting(MineCave c, long zone)
    {
        MineZone? z;
        return c.Zones.TryGet(zone, out z) && Resting(c, z);
    }
    string CycleText(MineCave c)
    {
        return (CycleOf(c) < ServerCycleTicks
            ? "respawn every " + c.Cycle / TimeSpan.TicksPerSecond + " s at most, measured"
            : "respawn by the server's pass every " + ServerCycleTicks / TimeSpan.TicksPerSecond + " s")
            + (c.LastStrike > 0 ? ", the last " + Ago(c.LastStrike) : "");
    }
    // The cave's chunks for the window's grid: chunk, ore digs, emptied, last dig, last "no metal", likely back,
    // evidence; the chunks resting last.
    public List<string[]> CaveZoneRows(string name)
    {
        List<string[]> rows = new();
        MineCave? c = CaveNamed(name);
        if (c == null)
            return rows;
        lock (m_caves)
        {
            string est = CycleText(c);
            List<long> keys = new(c.Zones.Keys);
            keys.Sort((a, b) => BackAt(c, c.Zones[a]!).CompareTo(BackAt(c, c.Zones[b]!)));
            foreach (long key in keys)
            {
                MineZone z = c.Zones[key]!;
                string ev = (z.RegrowMin > 0 ? "back within " + (z.RegrowMin / TimeSpan.TicksPerMinute) + " min" : "")
                    + (z.StillEmptyMax > 0
                    ? (z.RegrowMin > 0 ? "; " : "") + "still empty at " + (z.StillEmptyMax / TimeSpan.TicksPerMinute)
                    + " min" : "");
                rows.Add(new[] { ZoneText(key), z.Ore.ToString(), z.Emptied.ToString(), Ago(z.LastDig), BackText(c, z),
                    z.EmptyAt > 0 ? Ago(z.EmptyAt) + (z.EmptyLine.Length > 0 ? ": " + z.EmptyLine : "") : "",
                    ev.Length > 0 ? ev : est });
            }
        }
        return rows;
    }
    // The chunks with a diggable tile within reach of x, y, each with its tiles.
    Map<long, List<int[]>> ZonesInReach(Map<long, int[]> grid, MineCave c, Set<long> bad, int x, int y)
    {
        Map<long, List<int[]>> by = Map<long, List<int[]>>.Empty;
        lock (m_caves)
        {
            for (int tx = x - MineReach; tx <= x + MineReach; tx++)
            {
                for (int ty = y - MineReach; ty <= y + MineReach; ty++)
                {
                    int[]? m;
                    long key = TileKey(tx, ty);
                    if (!grid.TryGet(key, out m) || bad.Contains(key))
                        continue;
                    long z = ZoneKey(tx, ty);
                    if (ZoneResting(c, z))
                        continue;
                    List<int[]>? l;
                    if (!by.TryGet(z, out l))
                        by = by.Set(z, l = new List<int[]>());
                    l.Add(m);
                }
            }
        }

        return by;
    }
    // The next dig's tile: a chunk no dig in flight is in (two shovels dig two chunks), else, with one chunk left,
    // another tile of the chunk in flight (Damian, 2026-10-18: "when in 2 shovel mode, digging from 2 seperate regions
    // or if only 1 viable region is left, dig in 2 spots"). A chunk keeps the tile it was last dug at. Null when
    // nothing is in reach. only: that chunk's tiles alone (the sentinel being emptied).
    int[]? MineTarget(Map<long, int[]> grid, MineCave c, List<DigFlight> flights, Set<long> bad,
        ValueMap<long, long> lastTile, int px, int py, long only = long.MinValue)
    {
        Map<long, List<int[]>> by = ZonesInReach(grid, c, bad, px, py);
        if (only != long.MinValue)
        {
            List<int[]>? l;
            by = by.TryGet(only, out l) ? Map<long, List<int[]>>.Of((only, l)) : Map<long, List<int[]>>.Empty;
        }
        if (by.Count == 0)
            return null;
        List<long> keys = new(by.Keys);
        keys.Sort();
        long pick = keys.Find(z => !flights.Exists(f => f.Zone == z));
        if (!by.ContainsKey(pick) || flights.Exists(f => f.Zone == pick))
            pick = keys[0];
        List<int[]> tiles = by[pick]!.FindAll(t => !flights.Exists(f => f.X == t[0] && f.Y == t[1]));
        if (tiles.Count == 0)
            return null;
        long was;
        if (lastTile.TryGet(pick, out was))
        {
            int[]? same = tiles.Find(t => TileKey(t[0], t[1]) == was);
            if (same != null)
                return same;
        }
        tiles.Sort((a, b) => Tiles(a[0], a[1], px, py).CompareTo(Tiles(b[0], b[1], px, py)));
        return tiles[0];
    }
    // To the standing tile within the cave that reaches the most chunks with metal (up to the shovels digging), the
    // nearest of those. A walk that fails passes that tile over; MineWalkTries failures end the circuit.
    // better: only a tile reaching more chunks than this; NoBetter when there is none.
    public const string NoBetter = "no better tile", NoMetalInReach = "no metal in reach";
    DateTime m_betterAfter = DateTime.MinValue;
    string? MineWalk(GameLink k, Map<long, int[]> grid, MineCave c, int cx, int cy, Set<long> bad, int mode,
        int better = 0)
    {
        Set<long> refused = Set<long>.Empty;
        string? last = null;
        for (int attempt = 0; attempt < MineWalkTries && !circuitStop; attempt++)
        {
            int px, py;
            lock (k.State.Gate)
            {
                px = k.State.X;
                py = k.State.Y;
            }
            int[]? best = null;
            int bestScore = 0, bestDist = int.MaxValue;
            for (int x = cx - c.Radius; x <= cx + c.Radius; x++)
            {
                for (int y = cy - c.Radius; y <= cy + c.Radius; y++)
                {
                    if (refused.Contains(TileKey(x, y)) || (m_mineInside && !InCave(x, y)))
                        continue;
                    int score = Math.Min(mode, ZonesInReach(grid, c, bad, x, y).Count);
                    if (score == 0 || score <= better || score < bestScore)
                        continue;
                    int d = Tiles(x, y, px, py);
                    if (score == bestScore && d >= bestDist)
                        continue;
                    if (!StandOk(k, x, y))
                        continue;
                    best = new[] { x, y };
                    bestScore = score;
                    bestDist = d;
                }
            }

            if (best == null && better > 0)
                return attempt == 0 ? NoBetter
                    : "no tile reaching more than " + better + " chunk" + (better == 1 ? "" : "s")
                    + " reached; the last walk: " + last;
            if (best == null)
            {
                int resting = 0;
                lock (m_caves)
                    foreach (long z in c.Zones.Keys)
                        if (ZoneResting(c, z))
                            resting++;
                return NoMetalInReach + " of any standing tile within " + c.Radius + " tiles of " + cx + "," + cy
                    + " (" + resting + " chunks answered no metal since the last respawn, " + bad.Count
                    + " tiles refused)" + (last != null ? "; the last walk: " + last : "");
            }
            CircuitStatus = "Mine: walking to " + best[0] + "," + best[1] + " (" + bestScore + " chunk"
                + (bestScore == 1 ? "" : "s") + " in reach)";
            string? w = WalkTo(best[0], best[1], 0);
            if (w == null)
                return null;
            refused = refused.Add(TileKey(best[0], best[1]));
            last = w;
            Note("Mine: walking to " + best[0] + "," + best[1] + ": " + w);
        }
        return "no standing tile reached in " + MineWalkTries + " walks; the last: " + last;
    }
    // A tile the miner can stand on: the walk planner's own test, the floor, house parts included, and walls,
    // impassable statics and ground items at its height (Damian, 2026-10-18: "blockers on the ground like ore or other
    // things"), and no one standing there.
    bool StandOk(GameLink k, int x, int y)
    {
        int sz = UoMap.StandZ(x, y, UoMap.LandStandZ(x, y));
        if (sz == int.MinValue || Crowded(k, x, y))
            return false;
        foreach (int[] s in UoMap.StaticsAt(x, y))
            if (Blocks(s[0], s[1], sz))
                return false;
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.X == x && g.Y == y && Blocks(g.Graphic, g.Z, sz))
                    return false;
        return true;
    }
    // Every chunk with a diggable tile in the grid, for the depletion order.
    static ImmutableArray<long> CaveChunks(Map<long, int[]> grid)
    {
        List<long> seen = new();
        foreach (int[] t in grid.Values)
        {
            long z = ZoneKey(t[0], t[1]);
            if (!seen.Contains(z))
                seen.Add(z);
        }
        return ImmutableArray.CreateRange(seen);
    }
    // The free shovel's tile by the plan (MinePlanner.Pick): its chunk's last tile, else its nearest not in flight.
    // Null when no chunk of its own is in reach.
    int[]? PlanTarget(Map<long, int[]> grid, MineCave c, List<DigFlight> flights, Set<long> bad,
        ValueMap<long, long> lastTile, int px, int py, ValueMap<long, int> order)
    {
        Map<long, List<int[]>> by = ZonesInReach(grid, c, bad, px, py);
        if (by.Count == 0)
            return null;
        Set<long> inFlight = Set<long>.Empty;
        foreach (DigFlight f in flights)
            inFlight = inFlight.Add(f.Zone);
        Set<long> started = Set<long>.Empty;
        foreach (long z in lastTile.Keys)
            started = started.Add(z);
        long? pick = MinePlanner.Pick(ImmutableArray.CreateRange(by.Keys), inFlight, started, order);
        if (pick == null)
            return null;
        List<int[]> tiles = by[pick.Value]!.FindAll(t => !flights.Exists(f => f.X == t[0] && f.Y == t[1]));
        if (tiles.Count == 0)
            return null;
        long was;
        if (lastTile.TryGet(pick.Value, out was))
        {
            int[]? same = tiles.Find(t => TileKey(t[0], t[1]) == was);
            if (same != null)
                return same;
        }
        tiles.Sort((a, b) => Tiles(a[0], a[1], px, py).CompareTo(Tiles(b[0], b[1], px, py)));
        return tiles[0];
    }
    // The standing tiles the plan would walk to (MinePlanner.Rank), best first: within the cave's radius, inside the
    // cave when it has floor, reaching at least two chunks with metal.
    ImmutableArray<MineStand> PlanStands(GameLink k, Map<long, int[]> grid, MineCave c, int cx, int cy,
        Set<long> bad, long? carry, ValueMap<long, int> order, int px, int py)
    {
        ImmutableArray<MineStand>.Builder stands = ImmutableArray.CreateBuilder<MineStand>();
        for (int x = cx - c.Radius; x <= cx + c.Radius; x++)
        {
            for (int y = cy - c.Radius; y <= cy + c.Radius; y++)
            {
                if ((x == px && y == py) || (m_mineInside && !InCave(x, y)))
                    continue;
                Map<long, List<int[]>> by = ZonesInReach(grid, c, bad, x, y);
                if (by.Count < 2 || !StandOk(k, x, y))
                    continue;
                stands.Add(new MineStand(x, y, ImmutableArray.CreateRange(by.Keys)));
            }
        }

        return MinePlanner.Rank(stands.ToImmutable(), carry, order, px, py);
    }
    // To the first planned stand reached, MineWalkTries at most. Null once there, else the last walk's failure.
    string? PlanWalk(ImmutableArray<MineStand> stands)
    {
        string? last = "no stand to walk to";
        for (int i = 0; i < stands.Length && i < MineWalkTries && !circuitStop; i++)
        {
            MineStand s = stands[i];
            CircuitStatus = "Mine: walking to " + s.X + "," + s.Y + " (" + s.Chunks.Length + " chunks in reach)";
            last = WalkTo(s.X, s.Y, 0);
            if (last == null)
            {
                Note("Mine: to " + s.X + "," + s.Y + " for two chunks at once (" + s.Chunks.Length + " in reach)");
                return null;
            }
            Note("Mine: walking to " + s.X + "," + s.Y + ": " + last);
        }
        return last;
    }
    // One dig: the shovel used and its cursor answered at the tile. Null once sent, "again" when the server asked to
    // wait, "no cursor" when the shovel opened none, else why the circuit ends.
    string? MineDig(GameLink k, uint shovel, int[] t, int mode, List<DigFlight> flights)
    {
        using (Act(k))
        {
            Heard mark = new(k.State);
            string? w = k.Use(shovel, true);
            if (w != null)
                return "using the shovel: " + w;
            m_lastUse = Clock.UtcNow;
            DateTime until = Clock.UtcNow.AddMilliseconds(MineCursorMs);
            AwaitCursor(k, until);
            if (!k.MacroCursor)
            {
                k.Settle();
                foreach (string j in mark.Answers())
                    foreach (string wl in s_waitLines)
                        if (j.StartsWith(wl, StringComparison.OrdinalIgnoreCase))
                            return "again";
                return "no cursor";
            }
            w = k.Target(true, 0, t[0], t[1], t[2], t[3]);
            if (w != null)
                return "aiming: " + w;
            // The next use is paced from this target (Damian, 2026-10-20: "the time between the first shovel sending
            // its dirt coords and the second shovel being started, the delay between the two is 1250"); paced use to
            // use, the target to the next use ran about 1060 ms.
            m_lastUse = Clock.UtcNow;
            flights.Add(new DigFlight { Shovel = shovel, Zone = ZoneKey(t[0], t[1]), X = t[0], Y = t[1],
                At = Clock.UtcNow });
            // Two shovels overlap: a target's answer is its own dig's result, about 2.6 s later on uo1998, and waiting
            // for it serializes the shovels (the 2-tool loop's finding, 2026-10-16).
            if (mode > 1)
                k.Settle();
            else
                Answered(k, AnswerMs);
        }
        return null;
    }
    // The server's dig answers since jmark, each taken by the oldest dig in flight, kept in the cave's chunk record; a
    // dig with no answer in PendingDigMs is taken as unanswered. Null, or why the circuit ends.
    string? MineResults(GameLink k, MineCave c, List<DigFlight> flights, ref Set<long> bad, ref Heard jmark,
        ref int ores, ref int unanswered)
    {
        List<string> lines;
        lock (k.State.Gate)
        {
            lines = jmark.Answers();
            jmark = new(k.State);
        }
        bool changed = false;
        foreach (string j in lines)
        {
            if (Says(j, s_digEnds))
                return "the server says \"" + j + "\"";
            bool ore = Says(j, s_digOre), loose = Says(j, s_digLoosened), empty = Says(j,
                s_digEmpty), refused = Says(j, s_digRefused), broken = Says(j, s_digBroken);
            if (!(ore || loose || empty || refused || broken) || flights.Count == 0)
                continue;
            DigFlight f = flights[0];
            flights.RemoveAt(0);
            unanswered = 0;
            changed = true;
            lock (m_caves)
            {
                MineZone? z;
                if (!c.Zones.TryGet(f.Zone, out z))
                    c.Zones = c.Zones.Set(f.Zone, z = new MineZone());
                long now = Clock.UtcNow.Ticks;
                z.LastDig = now;
                bool wasEmpty = z.EmptyAt > 0 && z.LastOreAt < z.EmptyAt, resting = Resting(c, z);
                if (ore)
                {
                    z.Ore++;
                    ores++;
                    if (wasEmpty && (z.RegrowMin == 0 || now - z.EmptyAt < z.RegrowMin))
                        z.RegrowMin = now - z.EmptyAt;
                    z.LastOreAt = now;
                }
                if (loose)
                    z.Loosened++;
                // Metal (ore, or rocks loosened over it) in a chunk emptied since the last strike: the cave respawned.
                if ((ore || loose) && resting)
                    Strike(c, now,
                        "the chunk at " + ZoneText(f.Zone) + " emptied " + Ago(z.EmptyAt) + " has metal again");
                if (empty)
                {
                    z.Emptied++;
                    z.EmptyLine = j;
                    c.LastEmpty = now;
                    if (resting)
                    {
                        z.StillEmptyMax = Math.Max(z.StillEmptyMax, now - z.EmptyAt);
                    }
                    else
                    {
                        z.EmptyAt = now;
                        Note("Mine: the chunk at " + ZoneText(f.Zone) + " has no metal after " + z.Ore
                            + " ore digs; back " + BackText(c, z));
                    }
                }
            }
            if (refused)
            {
                bad = bad.Add(TileKey(f.X, f.Y));
                Note("Mine: " + f.X + "," + f.Y + " refused: \"" + j + "\"");
            }
        }
        while (flights.Count > 0 && (Clock.UtcNow - flights[0].At).TotalMilliseconds > PendingDigMs)
        {
            DigFlight f = flights[0];
            flights.RemoveAt(0);
            unanswered++;
            bad = bad.Add(TileKey(f.X, f.Y));
            Note("Mine: the dig at " + f.X + "," + f.Y + " had no answer in " + PendingDigMs
                + " ms; that tile is not dug again");
        }
        if (changed && c.Name.Length > 0)
            SaveCaves();
        return null;
    }
    // The player's own digs in the picked cave kept in its chunk record like the circuit's (Damian, 2026-10-20: "the
    // chunk list should be updated when i manually dig chunks in the selected cave too"): a shovel or pickaxe the
    // client used, then its cursor answered on a tile within the cave, is a dig in flight, and the server's answers are
    // taken by MineResults, while no mining circuit runs. ClientPacket carries the client's own packets only, never the
    // assist's.
    readonly List<DigFlight> m_handDigs = new();
    Set<long> m_handDigBad = Set<long>.Empty;
    uint m_handDigTool; Heard m_handDigMark = null!;
    void HandDigClient(GameLink k, byte[] p)
    {
        if (p[0] == 0x06 && p.Length >= 5)
        {
            AssistItem? it = Item(k, U32(p, 1) & 0x7FFFFFFF);
            m_handDigTool = it != null && DigTool(it.Graphic) ? it.Serial : 0;
            return;
        }
        if (p[0] != 0x6C || p.Length < 19 || m_handDigTool == 0)
            return;
        uint tool = m_handDigTool;
        m_handDigTool = 0;
        if (m_mineMode && !m_sentinelWait)
            return;
        int x = (p[11] << 8) | p[12], y = (p[13] << 8) | p[14];
        MineCave? c = PickedCave!.Length > 0 ? CaveNamed(PickedCave) : null;
        if (c == null || Tiles(x, y, c.X, c.Y) > c.Radius)
            return;
        lock (m_handDigs)
        {
            if (m_handDigs.Count == 0)
                m_handDigMark = new(k.State);
            m_handDigs.Add(new DigFlight { Shovel = tool, Zone = ZoneKey(x, y), X = x, Y = y, At = Clock.UtcNow });
        }
    }
    void HandDigServer(GameLink k)
    {
        lock (m_handDigs)
        {
            if (m_handDigs.Count == 0)
                return;
            MineCave? c = (m_mineMode && !m_sentinelWait) || PickedCave!.Length == 0 ? null : CaveNamed(PickedCave);
            if (c == null)
            {
                m_handDigs.Clear();
                return;
            }
            int ores = 0, unanswered = 0;
            MineResults(k, c, m_handDigs, ref m_handDigBad, ref m_handDigMark, ref ores, ref unanswered);
        }
    }
    // The pack's carry limit: the server's (strength * 4 + 30, OUO mobile.c CMobile_GetMaxWeight) or the one set, the
    // lower, with room left for every dig in flight and the next.
    int MineLimit(GameLink k)
    {
        int str;
        lock (k.State.Gate)
            str = k.State.Str;
        int set = PackLimitFor;
        return str > 0 ? Math.Min(set, str * 4 + 30) : set;
    }
    bool MineFull(GameLink k, int inFlight)
    {
        int w;
        lock (k.State.Gate)
            w = k.State.Weight;
        return w > 0 && w + DigGain * (inFlight + 1) > MineLimit(k);
    }
    // A full pack: the ore smelted at the forge, the ingots stored by the storage set up, and back to the spot. The
    // backpack keeps them, so a pack still full after smelting ends the circuit (Damian, 2026-10-18: "you can also pick
    // backpack and just not stash it. it needs to be pack weight limit aware").
    string? MineUnload(GameLink k, MineCave c, string[] forge, string store)
    {
        int x0, y0;
        lock (k.State.Gate)
        {
            x0 = k.State.X;
            y0 = k.State.Y;
        }
        string? sw = MineSmelt(k, forge);
        if (sw != null)
            return "smelting: " + sw;
        bool full = MineFull(k, 0);
        int ingots = SmeltIngots(k), w8;
        lock (k.State.Gate)
            w8 = k.State.Weight;
        if (store == "backpack")
        {
            if (full)
                return "the pack is full after smelting: " + w8 + " of " + MineLimit(k) + " stones, " + ingots
                    + " ingots kept";
        }
        else if (full || ingots >= MineCfgInt("store-at", 500))
        {
            if (store == "stash" && StashInBank)
                return MineTrip(k, c, true);
            int put;
            string? fw = MineStoreHere(k, store, out put);
            m_mineIngotsStored += put;
            if (fw != null)
                return "storing: " + fw;
            if (MineFull(k, 0))
                return "the pack is still full after storing " + put + " ingots";
        }
        return MineBack(k, c, x0, y0);
    }
    // The ingots onto the storage that is walked to: the house vendor, the targeted place, or the stash in the house.
    public string? MineStoreHere(GameLink k, string store, out int put)
    {
        if (store == "vendor")
            return MineStoreVendor(k, out put);
        if (store == "target")
        {
            string? w = PutStacks(k, MineStoreTo, g => g == IngotArt, "ingots", "Store to", out put);
            if (w == null)
                Note("Mine: " + put + " ingots put down (" + MineStoreToText() + ")");
            return w;
        }
        return MineStoreHomeFloor(k, out put);
    }
    // No shovel left: the house vendor's, else a trip to the bank town by its rune to buy them (Damian, 2026-10-20:
    // "lets just drop that complexity now and go with simple: get 140 gold, buy 10 shovels, immediatly return to the
    // cave and go back to work").
    // First the ore smelted and the ingots stored, so the pack reaches town with room for the shovels (Damian,
    // 2026-10-22: "on a shovel run, smelt and store/stash any ingots so you don't get to town with a bag full of ore
    // and no room for the shovels"); backpack storage keeps its ingots, and a stash in the bank box takes them in town.
    string? MineResupply(GameLink k, MineCave c, string[] forge, string store)
    {
        int x0, y0;
        lock (k.State.Gate)
        {
            x0 = k.State.X;
            y0 = k.State.Y;
        }
        if (PackCount(k, IsOre) > 0)
        {
            string? sw = MineSmelt(k, forge);
            if (sw != null)
                Note("Mine: smelting before the shovel trip: " + sw);
        }
        if (SmeltIngots(k) > 0 && (store == "vendor" || store == "target" || store == "stash" && !StashInBank))
        {
            int put;
            string? fw = MineStoreHere(k, store, out put);
            m_mineIngotsStored += put;
            if (fw != null)
                Note("Mine: storing before the shovel trip: " + fw);
        }
        // The mining toolbox at its last known spot first, when the trip stands within CircuitReach of it (Damian,
        // 2026-10-25: "record the last known position of the box, and check there"; the trip at the house, the box
        // drawn there, went to the vendor and then the bank rune, 16:24:53).
        int[]? box = MineBoxSpot(k);
        if (box != null)
        {
            int bx, by;
            lock (k.State.Gate)
            {
                bx = k.State.X;
                by = k.State.Y;
            }
            if (Tiles(bx, by, box[0], box[1]) <= CircuitReach)
            {
                int had = Shovels(k).Count;
                string? bw = BoxFetch(k);
                if (Shovels(k).Count > had)
                {
                    Note("Mine: " + (Shovels(k).Count - had) + " shovels from the mining toolbox");
                    return MineBack(k, c, x0, y0);
                }
                Note("Mine: shovels from the mining toolbox: " + (bw ?? "none came"));
            }
        }
        if (HouseVendorSerial != 0)
        {
            int got;
            string? vw = ShovelsFromVendor(k, out got);
            if (got > 0)
            {
                Note("Mine: " + got + " shovels taken from the vendor");
                ToolsToBag(k, s_miningTools);
                return MineBack(k, c, x0, y0);
            }
            Note("Mine: shovels from the vendor: " + vw);
        }
        if (MineBankRune.Length == 0)
            return "no shovel left, and no bank rune to buy shovels by";
        return MineTrip(k, c, store == "stash" && StashInBank);
    }
    // To the bank town by the pack rune and back by the cave's: the gold for the shovels short of MineCarry
    // (ShovelPrice each) from the bank box, the shovels bought at the town's tinkers, and the ingots put on the stash
    // when it is in the bank box.
    // A trip that comes away with no shovel ends the circuit in town.
    string? MineTrip(GameLink k, MineCave c, bool bankIngots)
    {
        uint rune = PackBankRune(k);
        if (rune == 0)
            return "no rune labelled \"" + MineBankRune + "\" in the pack that lands by a bank";
        CircuitStatus = "Mine: recalling to the bank";
        string? w = Recall(rune);
        if (w != null)
            return "recalling by the bank rune: " + w;
        string? city = CityOf(k);
        if (city == null)
            return "the bank rune " + MineBankRune + " landed outside a town";
        int carry = MineCarry;
        string? bankFull = null;
        bool toBox = c != null && BoxInCave(k, c);
        if (toBox)
            carry = Math.Max(carry, Tools.Road(s_miningTools) + BoxShovels);
        int gold = (carry - Shovels(k).Count) * ShovelPrice;
        if (gold > 0 && PackCount(k, g => g == GoldArt) < gold)
        {
            string? gw = ToCityBank(k, city, "the shovels' gold");
            if (gw == null)
            {
                string taken;
                gw = BankTake(new List<int> { GoldArt }, gold, out taken);
            }
            if (gw != null)
                Note("Mine: gold for the shovels: " + gw);
        }
        if (Shovels(k).Count < carry)
        {
            string? bw = BuyShovels(k, city, carry);
            if (bw != null)
                Note("Mine: " + bw);
        }
        if (bankIngots && SmeltIngots(k) > 0)
        {
            string? why = ToCityBank(k, city, "the ingots");
            if (why != null)
                return "the bank: " + why;
            int before = SmeltIngots(k), put;
            w = MineStoreStash(k, null!, StashSerial, out put);
            if (w != null)
            {
                bankFull = w;
                Note("Mine: the ingot stash in the bank box: " + w + "; the rest go to the house floor");
            }
            m_mineIngotsStored += before - SmeltIngots(k);
            Note("Mine: " + (before - SmeltIngots(k)) + " ingots put on the stash in the bank box in " + city);
        }
        if (Shovels(k).Count == 0)
            return "no shovel bought in " + city;
        ToolsToBag(k, s_miningTools);
        string? back = CaveBack(k, c!);
        if (back == null && toBox)
        {
            string? sb = ShovelsToBox(k, c!);
            if (sb != null)
                Note("Mine: shovels into the mining toolbox: " + sb);
        }
        if (back != null || bankFull == null)
            return back;
        int fp;
        string? fw = MineStoreHomeFloor(k, out fp);
        m_mineIngotsStored += fp;
        return fw == null ? null
            : "the stash in the bank box took no more (" + bankFull + "), and the house floor: " + fw;
    }
    // Shovels bought at the town's tinker shops, nearest first, as many as the pack's weight takes up to want; never a
    // pickaxe (Damian, 2026-10-22: "pickaxes are bad, waste of money, heavy. should never use them": 37 gp and 11
    // stones against a shovel's 14 gp and 5 stones at Magdalena in Minoc).
    string? BuyShovels(GameLink k, string city, int want)
    {
        int w8;
        lock (k.State.Gate)
            w8 = k.State.Weight;
        int room = MineLimit(k) - w8, n = -1, bought = 0;
        string? end = null;
        foreach (string kind in new[] { "shovel" })
        {
            List<int> arts = new();
            int each = 1;
            foreach (UoTiles.Tile t in UoTiles.Items())
                if (UoTiles.Name(t.Graphic).ToLowerInvariant() == kind)
                {
                    arts.Add(t.Graphic);
                    each = Math.Max(each, t.Weight);
                }
            if (arts.Count == 0)
                continue;
            if (n < 0)
            {
                n = Math.Min(want - Shovels(k).Count, room / each);
                if (n <= 0)
                    return "no room in the pack for a shovel (" + w8 + " of " + MineLimit(k) + " stones)";
            }
            int take = Math.Min(n - bought, room / each);
            if (take <= 0 || circuitStop)
                break;
            List<BuyItem> list = new()
            { new BuyItem { Name = kind, Arts = arts.ToArray(), Count = take, MaxPrice = int.MaxValue } };
            ValueMap<string, int> left = ValueMap<string, int>.Of((kind, take));
            int got = 0;
            List<UoRegions.Shop> shops = UoRegions.ShopsIn(city, new[] { "TINKER" });
            if (shops.Count == 0)
                return "no tinker shop in " + city;
            while (shops.Count > 0 && left.Get(kind, 0) > 0 && !circuitStop)
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
                end = BuyInShop(k, s, list, ref left, ref got);
                if (end != null)
                    break;
            }
            bought += got;
            room -= got * each;
            if (end != null || bought >= n)
                break;
        }
        if (n < 0)
            return "no shovel art in the tile data";
        return bought >= n ? null
            : bought + " of " + n + " shovels bought in " + city + (end != null ? ": " + end : "");
    }
    // The bank's bags picked by a target cursor, for every circuit (Damian, 2026-10-18: "we should do a general setting
    // like tool bag and bank tool bag, and bank store, for the bags of recall runes to the cities and shop types"): the
    // store the buy circuits and the spellbook maker put their goods in (<server>bank.store, else the container already
    // holding the most of them), the bag of runes to the cities (<server>runes.city) and the bags of runes marked at
    // shops (<server>runes.shop, a list), each searched first for its kind of rune. A rune bag is picked in the bank
    // box or the pack; the bank store only in the bank box.
    // The Bags tab (Damian, 2026-10-19: "consolidate all the "bag" settings on the bags tab ... with auto refresh when
    // backpack or bank opened, or it is opened if on floor. also put in house vendor as a bag"): each setting's key,
    // label and tip; where each bag is and what it holds, read from what the server has sent. Each profession's toolbox
    // and toolbag first.
    static readonly string[][] s_bagSettings = ToolBagSettings(new[] {
        new[] { "loadout.bag", "Reagent bag",
            "The reagent loadout's bag; Set asks for it, then for the storage bag it is refilled from." },
        new[] { "loadout.storage", "Reagent storage",
        "Where the reagent loadout is refilled from; set with the reagent bag." },
        new[] { "bankstore", "Bank store",
        ("Where the buy circuits and the spellbook maker put what they bank. Unset, the container already holding the "
        + "most of it. Open your bank box to set it.") },
        new[] { "cityrunes", "City runes",
        "The bag of runes to the cities: searched first for a city's rune. Set it in your open bank box or your pack." },
        new[] { "shoprunes", "Shop runes",
        ("The bags of runes marked at shops: searched first for a shop's rune; Set on a bag already listed removes it. "
        + "Set them in your open bank box or your pack.") },    });
    public static string[][] BagSettings => s_bagSettings;
    static string[][] ToolBagSettings(string[][] rest)
    {
        List<string[]> rows = new();
        foreach (Profession p in s_professions)
        {
            rows.Add(new[] { ToolPlaces.BoxKey(p), p.Name + " toolbox",
                "The big store of " + p.ToolsName
                + (", any container wherever it lies (a chest in the house, a bag in another city). Used straight from "
                + "the box when it is within reach, and the ") + p.Name.ToLowerInvariant()
                + " toolbag is topped up from it there."
                + (p == s_miningTools
                ? " Put it down in the cave and the cave circuit walks to it for shovels when down to 1." : "")
                + " The box stays where you put it: no circuit moves it." });
            rows.Add(new[] { ToolPlaces.BagKey(p), p.Name + " toolbag",
                "The bag in your pack carrying the " + p.ToolsName
                + (" for the road. Tools taken from the toolbox, bought or tinkered go into it. It stays in your pack: "
                + "no circuit puts it down. Unset, the one tool bag set before.") });
        }
        rows.AddRange(rest);
        return rows.ToArray();
    }
    public List<uint> BagsOf(string key)
    {
        Profession? tp = ToolPickProfession(key);
        if (tp != null)
        {
            List<uint> l = new();
            uint s = key.EndsWith(".box") ? Tools.Box(tp) : Tools.Bag(tp);
            if (s != 0)
                l.Add(s);
            return l;
        }
        if (key.StartsWith("loadout.") || key == "housevendor")
        {
            List<uint> l = new();
            uint s;
            if (uint.TryParse(Cfg.Get(key == "housevendor" ? Server + "house.vendor" : key, ""),
                NumberStyles.HexNumber, null, out s) && s != 0)
                l.Add(s);
            return l;
        }
        return BankBags(key);
    }
    public string? BagTarget(string key)
    {
        return ToolPickProfession(key) != null ? ToolTarget(key)
            : key.StartsWith("loadout.") ? LoadoutTarget()
            : key == "housevendor" ? HouseVendorTarget() : BankBagTarget(key);
    }
    public void BagClear(string key)
    {
        Profession? tp = ToolPickProfession(key);
        if (tp != null)
        {
            if (key.EndsWith(".box"))
                Tools.SetBox(tp, 0);
            else
                Tools.SetBag(tp, 0);
        }
        else if (key.StartsWith("loadout."))
        {
            Cfg.Set(key, "");
        }
        else if (key == "housevendor")
        {
            Cfg.Set(Server + "house.vendor", "");
        }
        else
        {
            Cfg.Set(Server + BankBagKey(key), "");
        }
    }
    // "<name> <serial>, <where>, <n> items" for the tab: in the pack, in the bank, on the floor, or not in view.
    public string BagWhere(uint s)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return s.ToString("X");
        lock (k.State.Gate)
        {
            AssistItem? it;
            bool ground = false;
            int[]? mob;
            if (!k.State.Items.TryGet(s, out it))
                ground = k.State.Ground.TryGet(s, out it);
            if (it == null)
                return k.State.Mobiles.TryGet(s, out mob)
                    ? "vendor " + s.ToString("X") + " at " + mob[0] + "," + mob[1] : s.ToString("X") + ": not in view";
            uint box = 0;
            foreach (AssistItem b in k.State.Items.Values)
                if (b.Layer == 0x1D && b.Mobile == k.State.Player)
                    box = b.Serial;
            string where = PackTree(k).Contains(s) ? "in the pack"
                : ground ? "on the floor at " + it.X + "," + it.Y
                : box != 0 && Within(k, s, box) ? "in the bank" : "in a container";
            bool known;
            lock (m_contentsGate)
                known = m_contents.ContainsKey(s);
            int n = 0;
            foreach (AssistItem c in k.State.Items.Values)
                if (c.Container == s)
                    n++;
            return UoTiles.Name(it.Graphic) + " " + s.ToString("X") + ", " + where + ", "
                + (known ? n + " items" : "not opened yet");
        }
    }
    static bool Within(GameLink k, uint s, uint root)
    {
        return Inv(k).Within(s, root);
    }
    // A set bag lying on the floor within reach is opened once, to read it ("or it is opened if on floor"); a bag in
    // the pack or the bank is read when they open.
    Set<uint> m_floorBagsRead = Set<uint>.Empty; volatile bool m_floorBagsBusy;
    readonly Lock m_floorBagsGate = new();
    public void RefreshFloorBags()
    {
        GameLink k = m_link;
        if (k == null || k.Closed || m_floorBagsBusy || CircuitRunning)
            return;
        List<uint> open = new();
        foreach (string[] set in s_bagSettings)
        {
            foreach (uint s in BagsOf(set[0]))
            {
                bool known;
                lock (m_contentsGate)
                    known = m_contents.ContainsKey(s);
                if (known)
                    continue;
                lock (k.State.Gate)
                {
                    AssistItem? it;
                    if (k.State.Ground.TryGet(s, out it) && Tiles(it.X, it.Y, k.State.X, k.State.Y) <= 2)
                        lock (m_floorBagsGate)
                            if (!m_floorBagsRead.Contains(s))
                            {
                                m_floorBagsRead = m_floorBagsRead.Add(s);
                                open.Add(s);
                            }
                }
            }
        }

        if (open.Count == 0)
            return;
        m_floorBagsBusy = true;
        ThreadPool.QueueUserWorkItem(_ => { try { foreach (uint s in open) { BusyWait(clickedAt); string? w = Peek(s); if (w
            != null) Note("Bags: opening " + s.ToString("X") + ": " + w); } } finally { m_floorBagsBusy = false; } });
    }
    const uint HouseVendorCursor = 0x48564E44;  // "HVND"
    public volatile string HouseVendorStatus = "";
    public string? HouseVendorTarget()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, HouseVendorCursor);
        string? why = k.ToClient(p, "house vendor cursor");
        HouseVendorStatus = why == null ? "Target your house vendor." : "No cursor: " + why;
        return why;
    }
    bool HoldHouseVendor(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != HouseVendorCursor)
            return false;
        uint s = U32(p, 7);
        bool mobile;
        lock (k.State.Gate)
            mobile = s != 0 && p[1] == 0 && k.State.Mobiles.ContainsKey(s);
        if (!mobile)
        {
            HouseVendorStatus = "House vendor not set: target the vendor itself.";
            return true;
        }
        Cfg.Set(Server + "house.vendor", s.ToString("X"));
        HouseVendorStatus = "House vendor " + s.ToString("X") + ".";
        Note("Bags: " + HouseVendorStatus);
        return true;
    }
    // The layout helper (assist-plan.md stage 8; Damian, 2026-10-19: "like target bag, reorg contents into a shape"): a
    // bag's items moved into a shape inside it, one lift and drop at a time at the busy pace. The shape fills the box
    // the items already span when that is wider than LayoutMinSpan both ways, else LayoutBox (the 1.25 backpack gump's
    // item area); x, y are the bag's own gump coordinates.
    static readonly ImmutableArray<int> s_layoutBox = [ 44, 65, 142, 140 ];
    public const int LayoutMinSpan = 30;
    static readonly ImmutableArray<string> s_layoutShapes = [ "square", "circle", "pentagram", "ascii" ];
    public static ImmutableArray<string> LayoutShapes => s_layoutShapes;
    public volatile string LayoutStatus = ""; volatile string m_layoutShape = "square", m_layoutArt = "";
    const uint LayoutCursor = 0x4C415954;  // "LAYT"
    public string? LayoutTarget(string shape, string art)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "not connected";
        m_layoutShape = shape;
        m_layoutArt = art ?? "";
        byte[] p = new byte[19];
        p[0] = 0x6C;
        p[1] = 0;
        Put32(p, 2, LayoutCursor);
        string? why = k.ToClient(p, "layout cursor");
        LayoutStatus = why == null ? "Target the bag to lay out." : "No cursor: " + why;
        return why;
    }
    bool HoldLayout(GameLink k, byte[] p)
    {
        if (p[0] != 0x6C || p.Length < 11 || U32(p, 2) != LayoutCursor)
            return false;
        uint s = U32(p, 7);
        AssistItem? it = s == 0 || p[1] != 0 ? null : Item(k, s);
        if (it == null || !IsContainer(it.Graphic))
        {
            LayoutStatus = "Layout cancelled: pick a bag.";
            return true;
        }
        string shape = m_layoutShape, art = m_layoutArt;
        Spawn("the layout", () => LayoutBag(k, s, shape, art));
        return true;
    }
    // A shape's slots in 0..1 both ways, n of them (ascii: one for each character that is not a space, in reading
    // order).
    public static List<double[]> ShapePoints(string shape, int n, string art)
    {
        List<double[]> l = new();
        if (n <= 0)
            return l;
        if (shape == "circle")
        {
            for (int i = 0; i < n; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                l.Add(new[] { 0.5 + 0.45 * Math.Cos(a), 0.5 + 0.45 * Math.Sin(a) });
            }
            return l;
        }
        if (shape == "pentagram")
        {
            // The star drawn in one stroke, outer points 0, 2, 4, 1, 3, the items spaced evenly along it.
            double[][] v = new double[5][];
            for (int i = 0; i < 5; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / 5;
                v[i] = new[] { 0.5 + 0.45 * Math.Cos(a), 0.5 + 0.45 * Math.Sin(a) };
            }
            int[] order = { 0, 2, 4, 1, 3, 0 };
            double seg = Math.Sqrt(Math.Pow(v[2][0] - v[0][0], 2) + Math.Pow(v[2][1] - v[0][1], 2));
            for (int i = 0; i < n; i++)
            {
                double d = 5 * seg * i / n;
                int s = Math.Min(4, (int)(d / seg));
                double f = (d - s * seg) / seg;
                double[] a0 = v[order[s]];
                double[] a1 = v[order[s + 1]];
                l.Add(new[] { a0[0] + (a1[0] - a0[0]) * f, a0[1] + (a1[1] - a0[1]) * f });
            }
            return l;
        }
        if (shape == "ascii")
        {
            string[] lines = (art ?? "").Replace("\r", "").Split('\n');
            int cols = 1;
            foreach (string ln in lines)
                cols = Math.Max(cols, ln.Length);
            for (int r = 0; r < lines.Length; r++)
                for (int c = 0; c < lines[r].Length; c++)
                    if (lines[r][c] != ' ')
                        l.Add(new[] { (c + 0.5) / cols, (r + 0.5) / lines.Length });
            return l;
        }
        int cs = (int)Math.Ceiling(Math.Sqrt(n)), rs = (n + cs - 1) / cs;
        for (int i = 0; i < n; i++)
            l.Add(new[] { (i % cs + 0.5) / cs, (i / cs + 0.5) / rs });
        return l;
    }
    void LayoutBag(GameLink k, uint bag, string shape, string art)
    {
        bool known;
        lock (m_contentsGate)
            known = m_contents.ContainsKey(bag);
        if (!known)
        {
            BusyWait(clickedAt);
            string? pw = Peek(bag);
            if (pw != null)
            {
                LayoutStatus = "Layout: opening the bag: " + pw;
                return;
            }
        }
        List<AssistItem> items = new();
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Container == bag)
                    items.Add(it);
        if (items.Count == 0)
        {
            LayoutStatus = "Layout: the bag is empty.";
            return;
        }
        items.Sort((a, b) => a.Serial.CompareTo(b.Serial));
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (AssistItem it in items)
        {
            x0 = Math.Min(x0, it.X);
            y0 = Math.Min(y0, it.Y);
            x1 = Math.Max(x1, it.X);
            y1 = Math.Max(y1, it.Y);
        }
        if (x1 - x0 < LayoutMinSpan || y1 - y0 < LayoutMinSpan)
        {
            x0 = s_layoutBox[0];
            y0 = s_layoutBox[1];
            x1 = s_layoutBox[2];
            y1 = s_layoutBox[3];
        }
        List<double[]> pts = ShapePoints(shape, items.Count, art);
        if (pts.Count == 0)
        {
            LayoutStatus = "Layout: the shape has no slots.";
            return;
        }
        int moved = 0, n = Math.Min(items.Count, pts.Count);
        for (int i = 0; i < n && !k.Closed; i++)
        {
            AssistItem it = items[i];
            int tx = x0 + (int)Math.Round(pts[i][0] * (x1 - x0)), ty = y0 + (int)Math.Round(pts[i][1] * (y1 - y0));
            if (Math.Abs(it.X - tx) <= 1 && Math.Abs(it.Y - ty) <= 1)
                continue;
            LayoutStatus = "Layout: " + (i + 1) + " of " + n + " (" + shape + ")";
            string? w;
            using (Act(k))
                w = MoveStack(k, it.Serial, Math.Max(1, it.Amount), UoTiles.Name(it.Graphic), "the layout",
                    () => k.DropAt(it.Serial, bag, tx, ty)!);
            if (w != null)
            {
                LayoutStatus = "Layout stopped at " + (i + 1) + " of " + n + ": " + w;
                Note(LayoutStatus);
                return;
            }
            moved++;
        }
        LayoutStatus = "Layout done: " + moved + " moved into a " + shape
            + (pts.Count < items.Count
            ? "; " + (items.Count - pts.Count) + " left where they were (the picture has " + pts.Count + " slots)"
            : "") + ".";
        Note(LayoutStatus);
    }
}
