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
    // ---------- walking (assist-plan.md stage 6) ----------
    // Toward a tile until within reach, a step every StepMs at walking pace: the direction straight at it first, then
    // the ones beside it. A refused step marks its tile blocked; a door within a tile of a refused step is opened
    // (Damian, 2026-10-17: "you have to learn to open those") and the step tried again. A tile another character stands
    // on is stepped around: a step through one is blocked without full stamina and costs stamina with it. The client is
    // shown the player after every step it did not take itself.
    // A walk runs (direction | 0x80, as the client sends a run) at RunMs a step, the on-foot run pace (Damian,
    // 2026-10-17: "walking very slowly" at 594 ms a step). The route is followed, not planned again every step: a
    // refusal, a character on the next tile, or ReplanSteps steps plan it anew.
    // Mounted, a run is RunMountMs a step (ServUO's mounted run pace; Damian: "you have a horse, you can run too"); a
    // mount is the item on layer MountLayer.
    // ReplanSteps: a route is planned again after this many steps as well as on a refusal or a character on the next
    // tile.
    int StepMs => Ms("walk-step");
    int RunMs => Ms("walk-run"); int RunMountMs => Ms("run-mount"); int WalkMountMs => Ms("walk-mount");
    public const int WalkStallsMax = 3;
    public const int MountLayer = 0x19, RunFlag = 0x80, ReplanSteps = 64, StepAnswerMs = 1500, WalkStepsMax = 400,
        WalkRefusalsMax = 40;
    // FatigueRestMs: a mount regains a point every 200 / 18 ticks of 250 ms, about 2.8 s (the estimate below), so 10 s
    // buys a few steps of running.
    // A refusal for fatigue rests FatigueRestMs, doubled for each one more in the same walk, at most FatigueRestMaxMs.
    public const int StraightCost = 100, DiagonalCost = 101, StepsAhead = 2, FatigueRestMaxMs = 60000;
    int StepSettleMs => Ms("step-settle"); int FatigueRestMs => Ms("fatigue-rest"); int WarnRestMs => Ms("warn-rest");
    // Pace (Damian, 2026-10-17: "when the horse is in a known rested state, run it if it helps. when it is in a known
    // tired state, rest it a little to catch its breath, but before its fully recouped, you can start walking again to
    // the next natural resting job. and if walking is still a problem, rest a bit longer"): "auto" runs a mount the
    // estimate holds rested (MountRested) and walks one it holds tired, deciding again every step, so the walk turns to
    // a run once the mount has recovered; "walk" and "run" fix it (the control port's `pace auto|walk|run`). On foot
    // "auto" runs.
    public volatile string Pace = "auto";
    bool Mounted(GameLink k)
    {
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Layer == MountLayer && it.Mobile == k.State.Player)
                    return true;
        return false;
    }
    bool MountRested()
    {
        MountSettle();
        lock (m_mountGate)
            return m_mountOwed < 0;
    }
    bool RunNow(GameLink k)
    {
        string p = Pace;
        return p == "run" || (p == "auto" && (!Mounted(k) || MountRested()));
    }
    int StepPace(GameLink k)
    {
        lock (k.State.Gate)
            foreach (AssistItem it in k.State.Items.Values)
                if (it.Layer == MountLayer && it.Mobile == k.State.Player)
                    return RunMountMs;
        return RunMs;
    }
    static readonly ImmutableArray<int> s_dX = [ 0, 1, 1, 1, 0, -1, -1, -1 ], s_dY = [ -1, -1, 0, 1, 1, 1, 0, -1 ];
    static int DirTo(int dx, int dy)
    {
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        for (int d = 0; d < 8; d++)
            if (s_dX[d] == sx && s_dY[d] == sy)
                return d;
        return 0;
    }
    // Any mobile in view on the tile, animals as well as people (two dogs in a row stopped a walk, 2026-10-17): the
    // server's own tiles (AssistState.Mobiles), not the atlas, which keeps people.
    bool Occupied(GameLink k, int x, int y)
    {
        lock (k.State.Gate)
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                if (kv.Key != k.State.Player && kv.Value[0] == x && kv.Value[1] == y)
                    return true;
        return false;
    }
    // One step and its answer: 1 moved or turned, 0 refused, -1 no answer. The client is told to take it (0x97) and
    // walks it as its own; a client that takes no step for it leaves the walk to steps the assist sends itself
    // (clientWalks off). The 1.25 client takes no step for 0x97 (uo1998 2026-10-17 07:44:23, 9707 then nothing), so the
    // assist sends them.
    bool m_clientWalks = false;
    int StepOnce(GameLink k, int dir)
    {
        if (m_clientWalks)
        {
            int ca = k.ClientStepAcks, cr = k.ClientStepRefusals;
            if (k.MoveClient(dir) != null)
                return -1;
            DateTime by = Clock.UtcNow.AddMilliseconds(StepAnswerMs);
            while (Clock.UtcNow < by && !k.Closed)
            {
                if (k.ClientStepRefusals != cr)
                    return 0;
                if (k.ClientStepAcks != ca)
                    return 1;
                Clock.Sleep(10);
            }
            m_clientWalks = false;
            Note("Walk: the client took no step when told to; the assist sends the steps itself");
        }
        int acks = k.StepAcks, refusals = k.StepRefusals;
        if (k.Step(dir) != null)
            return -1;
        DateTime until = Clock.UtcNow.AddMilliseconds(StepAnswerMs);
        while (Clock.UtcNow < until && !k.Closed)
        {
            if (k.StepRefusals != refusals)
                return 0;
            if (k.StepAcks != acks)
                return 1;
            Clock.Sleep(10);
        }
        return -1;
    }
    // A tile is blocked at the player's height by a wall or impassable static or ground item that overlaps a person's
    // height above the floor, however low (1.25 TILEDATA flags 0x10 wall, 0x40 impassable; a door, 0x20000000, is
    // opened instead). Players' piles block this way (Damian, 2026-10-17: "look for blocking items too ... like big ore
    // piles, boxes and stuff other players can put down"): the big iron ore pile 0x19B9 (1 high), crates, wooden boxes
    // and chests are impassable in the tile data; a bag, a small ore pile or a shirt is not.
    const uint TileWall = 0x10, TileImpassable = 0x40, TileDoor = 0x20000000;
    public const int PersonHeight = 16, PlanMargin = 24, TurnCost = 100, DoorClear = 2, DoorTries = 4,
        DoorBackoffMs = 500, DoorOpenMs = 1000;
    int HeldDoorWaitMs => Ms("door-held");
    // A door or a gate, opened by use (Damian, 2026-10-17, of a Moonglow gate: "opens like a door"): an art the 1.25
    // TILEDATA flags as a door, the 302 arts of DoorSets, gates among them (0x0824-0x0875; a scan of 2026-10-23), as
    // the stash spots read a door (Stash.cs).
    public static bool IsDoor(int graphic)
    {
        List<UoTiles.Tile> l = UoTiles.Items();
        return graphic >= 0 && graphic < l.Count && (l[graphic].Flags & TileDoor) != 0;
    }
    static bool Blocks(int graphic, int z, int pz)
    {
        List<UoTiles.Tile> l = UoTiles.Items();
        if (graphic < 0 || graphic >= l.Count)
            return false;
        UoTiles.Tile t = l[graphic];
        if ((t.Flags & (TileWall | TileImpassable)) == 0 || (t.Flags & TileDoor) != 0)
            return false;
        int top = z + Math.Max(1, t.Height);
        return z < pz + PersonHeight && top > pz;
    }
    // The height a walker from fromZ stands at on the tile, or int.MinValue when the tile is closed to it. The map's
    // floor (UoMap.StandZ) decides the height; walls and impassable statics and ground items are tested at that height.
    // An unreadable map falls back to the walker's own height and passable land.
    // A tile stood on near fromZ with no step to it (a Teleport's landing): the map's floor.
    int Open(GameLink k, int x, int y, int fromZ, ref ValueMap<long, int> seen, bool shove = false)
    {
        return OpenAt(k, x, y, fromZ, ref seen, shove, State(x, y, fromZ) ^ (1L << 63), UoMap.StandZ(x, y, fromZ));
    }
    // The step from (fx, fy) at fromZ, by the server's own rule (UoMap.StepZ): the stood-on surface's top decides the
    // climb, so a stage edge two above is a wall and a stair is a step (the walk ran at the music hall's stage wall
    // until it found the stairs), and a drop off a floor needs its whole column clear (Jhelom's fighting pit balcony,
    // 2026-10-18 21:26). tops: the StartTop of each tile stepped from, kept by a plan, which asks it for all eight
    // steps off a tile.
    int Open(GameLink k, int fx, int fy, int x, int y, int fromZ, ref ValueMap<long, int> seen, bool shove,
        ref ValueMap<long, int> tops)
    {
        int startTop;
        long from = State(fx, fy, fromZ);
        if (!tops.TryGet(from, out startTop))
        {
            startTop = UoMap.StartTop(fx, fy, fromZ);
            tops = tops.Set(from, startTop);
        }
        long key = State(x, y, fromZ) ^ ((long)(byte)(startTop - fromZ + 128) << 48);
        int z;
        if (seen.TryGet(key, out z))
            return z;
        return OpenAt(k, x, y, fromZ, ref seen, shove, key, UoMap.StepZ(fromZ, x, y, startTop));
    }
    // The ground items by tile (art and z) and the tiles people stand on, taken under the gate at most every
    // BlockersMs: a plan visits thousands of tiles, and a scan of every ground item and mobile under the gate for each
    // one starved the relay's packet thread, both directions silent 9.5 s (2026-10-18 22:59:02 and 23:00:53, a smithing
    // walk's replan).
    public const int BlockersMs = 250;
    sealed class TileBlockers
    {
        public GameLink Link = null!; public DateTime At;
        public Map<long, List<int[]>> Ground = Map<long, List<int[]>>.Empty;
        public Set<long> People = Set<long>.Empty;
    }
    volatile TileBlockers m_blockers = null!;
    TileBlockers BlockersNow(GameLink k)
    {
        TileBlockers b = m_blockers;
        if (b != null && b.Link == k && (Clock.UtcNow - b.At).TotalMilliseconds < BlockersMs)
            return b;
        b = new TileBlockers { Link = k, At = Clock.UtcNow };
        lock (k.State.Gate)
        {
            foreach (AssistItem g in k.State.Ground.Values)
            {
                long t = ((long)g.X << 16) | (uint)g.Y;
                List<int[]>? l;
                if (!b.Ground.TryGet(t, out l))
                    b.Ground = b.Ground.Set(t, l = new List<int[]>());
                l.Add(new[] { g.Graphic, g.Z });
            }
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                if (kv.Key != k.State.Player)
                    b.People = b.People.Add(((long)kv.Value[0] << 16) | (uint)kv.Value[1]);
        }
        // The decoration kept from earlier views, where the client holds nothing now (DecorSweep).
        lock (m_decorGate)
        {
            foreach (Decor d in DecorLoad().Values)
            {
                long t = ((long)d.X << 16) | (uint)d.Y;
                if (d.Graphic >= MultiArt || b.Ground.ContainsKey(t))
                    continue;
                b.Ground = b.Ground.Set(t, new List<int[]> { new[] { d.Graphic, d.Z } });
            }
        }

        m_blockers = b;
        return b;
    }
    // The server's world decoration (Damian, 2026-10-23: "we should also fill in doors and "tile fixing / tile filling"
    // dynamics. some of the art in the static file is undone, like a roof shingle here or there, a hole in the ground
    // ... they plug those holes with map addition when you enter the area. also things like streetlamps that block
    // movement are helpful to catalogue"; "do not catalog the interior of player houses though, that is rude"; "you can
    // put the door on their house though"; "and any part of the multi- like the forges and looms and mills"): every
    // item lying on the ground that no one can lift (TILEDATA weight 255), every door, and every multi (a house, its
    // forges, looms and mills being parts of it), kept by serial in decor-<server>.txt (serial, graphic, x, y, z a
    // line), DecorKeep at most; inside a house only its doors. The walk planner reads the items where the client holds
    // nothing, and a kept multi is the map's (UoMap.PutMulti) from the load on. An item kept within SeenView of the
    // player and not in view for SeenGoneMs leaves it.
    public const int DecorKeep = 100000, DecorSaveMs = 30000, MultiArt = 0x4000;
    sealed class Decor
    {
        public int Graphic, X, Y, Z;
    }
    readonly Lock m_decorGate = new();
    Map<uint, Decor> m_decorNow = null!; bool m_decorDirty; DateTime m_decorSaved;
    // Moves whenever the catalogue gains, moves or loses an item, so a game view drawn before it is drawn again.
    public volatile int DecorSeq;
    ValueMap<uint, DateTime> m_decorMissing = ValueMap<uint, DateTime>.Empty;
    public string DecorPath = "";
    string DecorFile => DecorPath.Length > 0 ? DecorPath
        : Path.Combine(Path.GetDirectoryName(AssistRelay.SettingsPath)!, "decor-" + ServerSafe + ".txt");
    Map<uint, Decor> DecorLoad()
    {
        if (m_decorNow != null)
            return m_decorNow;
        Map<uint, Decor> d = Map<uint, Decor>.Empty;
        try
        {
            if (File.Exists(DecorFile))
            {
                foreach (string line in File.ReadAllLines(DecorFile))
                {
                    string[] f = line.Split(',');
                    uint s;
                    int g, x, y, z;
                    if (f.Length == 5 && uint.TryParse(f[0], NumberStyles.HexNumber, null, out s)
                        && int.TryParse(f[1], NumberStyles.HexNumber, null, out g) && int.TryParse(f[2], out x)
                        && int.TryParse(f[3], out y) && int.TryParse(f[4], out z))
                        d = d.Set(s, new Decor { Graphic = g, X = x, Y = y, Z = z });
                }
            }
        }
        catch (Exception e) { Note("Decor: the catalogue not read: " + e.Message); }
        foreach (KeyValuePair<uint, Decor> kv in d)
            if (kv.Value.Graphic >= MultiArt)
                UoMap.PutMulti(kv.Key, kv.Value.Graphic - MultiArt, kv.Value.X, kv.Value.Y, kv.Value.Z);
        return m_decorNow = d;
    }
    static bool Fixed(int graphic)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        return graphic >= MultiArt || IsDoor(graphic)
            || graphic >= 0 && graphic < items.Count && items[graphic].Weight == 255;
    }
    public void DecorSweep(GameLink k)
    {
        if (k == null || k.Closed)
            return;
        Map<uint, Decor> here = Map<uint, Decor>.Empty;
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.Container == 0 && Fixed(g.Graphic))
                    here = here.Set(g.Serial, new Decor { Graphic = g.Graphic, X = g.X, Y = g.Y, Z = g.Z });
        }
        if ((px | py) == 0)
            return;
        DateTime now = Clock.UtcNow;
        lock (m_decorGate)
        {
            Map<uint, Decor> d = DecorLoad();
            foreach (KeyValuePair<uint, Decor> kv in here)
            {
                m_decorMissing = m_decorMissing.Remove(kv.Key);
                if (kv.Value.Graphic < MultiArt && !IsDoor(kv.Value.Graphic) && LumberHoused(kv.Value.X, kv.Value.Y))
                {
                    if (d.ContainsKey(kv.Key))
                    {
                        d = d.Remove(kv.Key);
                        m_decorDirty = true;
                        DecorSeq++;
                    }
                    continue;
                }
                Decor? was;
                if (d.TryGet(kv.Key, out was) && was.Graphic == kv.Value.Graphic && was.X == kv.Value.X
                    && was.Y == kv.Value.Y && was.Z == kv.Value.Z)
                    continue;
                if (was == null && d.Count >= DecorKeep)
                    continue;
                d = d.Set(kv.Key, kv.Value);
                m_decorDirty = true;
                DecorSeq++;
            }
            foreach (KeyValuePair<uint, Decor> kv in d)
            {
                uint s = kv.Key;
                if (here.ContainsKey(s))
                    continue;
                Decor it = kv.Value;
                if (Tiles(px, py, it.X, it.Y) > SeenView)
                {
                    m_decorMissing = m_decorMissing.Remove(s);
                    continue;
                }
                DateTime since;
                if (!m_decorMissing.TryGet(s, out since))
                {
                    m_decorMissing = m_decorMissing.Set(s, now);
                    continue;
                }
                if ((now - since).TotalMilliseconds >= SeenGoneMs)
                {
                    d = d.Remove(s);
                    m_decorMissing = m_decorMissing.Remove(s);
                    m_decorDirty = true;
                    DecorSeq++;
                }
            }
            m_decorNow = d;
            if (m_decorDirty && (now - m_decorSaved).TotalMilliseconds >= DecorSaveMs)
                DecorSave();
        }
    }
    // The caller holds decorGate.
    void DecorSave()
    {
        List<string> l = new();
        foreach (KeyValuePair<uint, Decor> kv in DecorLoad())
            l.Add(kv.Key.ToString("X") + "," + kv.Value.Graphic.ToString("X") + "," + kv.Value.X + "," + kv.Value.Y
                + "," + kv.Value.Z);
        try
        {
            File.WriteAllLines(DecorFile, l);
            m_decorDirty = false;
            m_decorSaved = Clock.UtcNow;
        }
        catch (Exception e) { Note("Decor: the catalogue not written: " + e.Message); }
    }
    // The catalogue's items: serial, graphic, x, y, z.
    public List<int[]> DecorItems()
    {
        List<int[]> l = new();
        lock (m_decorGate)
            foreach (KeyValuePair<uint, Decor> kv in DecorLoad())
                l.Add(new[] { (int)kv.Key, kv.Value.Graphic, kv.Value.X, kv.Value.Y, kv.Value.Z });
        return l;
    }
    // The catalogue's items on the tiles of the box, multis left out (the map draws those as UoMap.MultiAt's parts):
    // graphic, x, y, z each. The game view fills the static file's holes with them (Damian, 2026-10-23: "they plug
    // those holes with map addition when you enter the area").
    public List<int[]> DecorIn(int x0, int y0, int x1, int y1)
    {
        List<int[]> l = new();
        lock (m_decorGate)
            foreach (Decor d in DecorLoad().Values)
                if (d.Graphic < MultiArt && d.X >= x0 && d.X <= x1 && d.Y >= y0 && d.Y <= y1)
                    l.Add(new[] { d.Graphic, d.X, d.Y, d.Z });
        return l;
    }
    int OpenAt(GameLink k, int x, int y, int fromZ, ref ValueMap<long, int> seen, bool shove, long key, int z)
    {
        int cached;
        if (seen.TryGet(key, out cached))
            return cached;
        z = OpenCompute(k, x, y, fromZ, shove, z);
        seen = seen.Set(key, z);
        return z;
    }
    int OpenCompute(GameLink k, int x, int y, int fromZ, bool shove, int z)
    {
        if (z == int.MinValue && UoMap.LandZ(x, y) == int.MinValue && !UoMap.LandBlocks(x, y))
            z = fromZ;
        bool open = z != int.MinValue;
        if (open)
            foreach (int[] s in UoMap.StaticsAt(x, y))
                if (Blocks(s[0], s[1], z))
                {
                    open = false;
                    break;
                }
        TileBlockers b = BlockersNow(k);
        long tile = ((long)x << 16) | (uint)y;
        List<int[]>? here;
        if (open && b.Ground.TryGet(tile, out here))
            foreach (int[] g in here)
                if (Blocks(g[0], g[1], z))
                {
                    open = false;
                    break;
                }
        if (open && !shove && b.People.Contains(tile))
            open = false;
        return open ? z : int.MinValue;
    }
    // The shortest walk (8 directions, a diagonal only past two open sides) from a tile to within reach of another,
    // over the box around both widened by PlanMargin; the directions in order, or null when there is none.
    // Whether the line from a tile at height z to another crosses nothing a person cannot see through at eye height (z
    // + PersonHeight - 1): a wall or impassable static or ground item that spans it, or a door (Damian, 2026-10-17:
    // "you could be 1 tile away but not able to talk"). A counter below the eyes does not block. The tiles strictly
    // between the two, by Bresenham.
    static bool SightBlocks(int graphic, int z, int pz)
    {
        List<UoTiles.Tile> l = UoTiles.Items();
        if (graphic < 0 || graphic >= l.Count)
            return false;
        UoTiles.Tile t = l[graphic];
        if ((t.Flags & (TileWall | TileImpassable)) == 0)
            return false;
        int eye = pz + PersonHeight - 1;
        return z <= eye && z + Math.Max(1, t.Height) > eye;
    }
    bool Sight(GameLink k, int x0, int y0, int z, int x1, int y1)
    {
        if (x0 == x1 && y0 == y1)
            return true;
        TileBlockers b = BlockersNow(k);
        List<int[]>? here;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx
            + dy, x = x0, y = y0;
        while (true)
        {
            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y += sy;
            }
            if (x == x1 && y == y1)
                return true;
            foreach (int[] s in UoMap.StaticsAt(x, y))
                if (SightBlocks(s[0], s[1], z))
                    return false;
            if (b.Ground.TryGet(((long)x << 16) | (uint)y, out here))
                foreach (int[] g in here)
                    if (SightBlocks(g[0], g[1], z) || IsDoor(g[0]))
                        return false;
        }
    }
    // tz: the floor the walk ends on, within FloorApart of it; AnyFloor for any.
    public const int AnyFloor = int.MinValue;
    static bool OnFloor(int z, int tz)
    {
        return tz == AnyFloor || Math.Abs(z - tz) < FloorApart;
    }
    // A walk state: a tile and the height stood at there, so one tile holds a state per floor (the Magincia mage shop's
    // floors at z 20, 40 and 60 over the same tiles).
    internal static long State(int x, int y, int z)
    {
        return ((((long)x << 16) | (uint)(y & 0xFFFF)) << 8) | (byte)(z + 128);
    }
    static int StateX(long s)
    {
        return (int)(s >> 24);
    }
    static int StateY(long s)
    {
        return (int)((s >> 8) & 0xFFFF);
    }
    static int StateZ(long s)
    {
        return (int)(s & 0xFF) - 128;
    }
    // One plan's tables (Walks), as arrays over a dense index of the states it meets: each tile of the box chains its
    // states (one per floor), a state outside the box (a start beside the shop a plan keeps to) is found in a list.
    // Per state: its cheapest arrival (Cost, and From: the previous state times 8 plus the direction, -2 less the state
    // a teleporter link left, -1 at a start), in the order arrivals came (Arrivals), its StartTop, and the height each
    // of its 8 steps stands at. Per search node (the state times Headings plus the heading it was reached by, 8 at a
    // start or off a teleporter): its cost, the node before it (-2 less it across a link, -1 at a start), done.
    internal sealed class PlanGrid
    {
        public const int Headings = 9, Unknown = int.MaxValue;
        readonly int m_x0, m_y0, m_w, m_h;
        readonly int[] m_first;
        readonly bool[] m_refused;
        readonly List<int> m_outside = new();
        int[] m_x = new int[Start], m_y = new int[Start], m_z = new int[Start], m_next = new int[Start];
        const int Start = 1024;
        public int Count;
        public int[] Cost = new int[Start], From = new int[Start], Top = new int[Start], Step = new int[Start * 8];
        public bool[] Arrived = new bool[Start];
        public readonly List<int> Arrivals = new();
        public int[] NodeCost = new int[Start * Headings], NodeFrom = new int[Start * Headings];
        public bool[] Done = new bool[Start * Headings];

        public PlanGrid(int x0, int y0, int w, int h, Set<long> refused)
        {
            m_x0 = x0;
            m_y0 = y0;
            m_w = Math.Max(0, w);
            m_h = Math.Max(0, h);
            m_first = new int[m_w * m_h];
            new Span<int>(m_first).Fill(-1);
            m_refused = new bool[m_w * m_h];
            foreach (long r in refused)
            {
                int t = Tile((int)(r >> 16), (int)(r & 0xFFFF));
                if (t >= 0)
                    m_refused[t] = true;
            }
        }

        int Tile(int x, int y)
        {
            return x >= m_x0 && y >= m_y0 && x < m_x0 + m_w && y < m_y0 + m_h ? (x - m_x0) * m_h + (y - m_y0) : -1;
        }

        // A tile of the box no refusal closes.
        public bool Walkable(int x, int y)
        {
            int t = Tile(x, y);
            return t >= 0 && !m_refused[t];
        }

        public bool InBox(int x, int y) => Tile(x, y) >= 0;

        public int X(int s) => m_x[s];

        public int Y(int s) => m_y[s];

        public int Z(int s) => m_z[s];

        public long State(int s) => AssistFeatures.State(m_x[s], m_y[s], m_z[s]);

        // The state's index, -1 when the plan has not met it.
        public int Find(int x, int y, int z)
        {
            int t = Tile(x, y);
            if (t < 0)
            {
                foreach (int i in m_outside)
                    if (m_x[i] == x && m_y[i] == y && m_z[i] == z)
                        return i;
                return -1;
            }
            for (int i = m_first[t]; i >= 0; i = m_next[i])
                if (m_z[i] == z)
                    return i;
            return -1;
        }

        public int Find(long state) => Find(StateX(state), StateY(state), StateZ(state));

        // The state's index, added when new: no arrival, its StartTop, steps and nodes unknown.
        public int Index(int x, int y, int z)
        {
            int i = Find(x, y, z);
            if (i >= 0)
                return i;
            i = Count++;
            if (i == m_x.Length)
                Grow(2 * i);
            m_x[i] = x;
            m_y[i] = y;
            m_z[i] = z;
            int t = Tile(x, y);
            if (t < 0)
            {
                m_next[i] = -1;
                m_outside.Add(i);
            }
            else
            {
                m_next[i] = m_first[t];
                m_first[t] = i;
            }
            Top[i] = Unknown;
            new Span<int>(Step, i * 8, 8).Fill(Unknown);
            new Span<int>(NodeCost, i * Headings, Headings).Fill(Unknown);
            return i;
        }

        void Grow(int n)
        {
            Array.Resize(ref m_x, n);
            Array.Resize(ref m_y, n);
            Array.Resize(ref m_z, n);
            Array.Resize(ref m_next, n);
            Array.Resize(ref Cost, n);
            Array.Resize(ref From, n);
            Array.Resize(ref Top, n);
            Array.Resize(ref Arrived, n);
            Array.Resize(ref Step, n * 8);
            Array.Resize(ref NodeCost, n * Headings);
            Array.Resize(ref NodeFrom, n * Headings);
            Array.Resize(ref Done, n * Headings);
        }

        public void Arrive(int s, int cost, int from)
        {
            Cost[s] = cost;
            From[s] = from;
            if (Arrived[s])
                return;
            Arrived[s] = true;
            Arrivals.Add(s);
        }

        public bool Reached(long state)
        {
            int s = Find(state);
            return s >= 0 && Arrived[s];
        }

        // Every state arrived at, in the order of the first arrivals.
        public List<long> States()
        {
            List<long> l = new(Arrivals.Count);
            foreach (int s in Arrivals)
                l.Add(State(s));
            return l;
        }

        // Every state arrived at with its cost, in the order of the first arrivals.
        public List<KeyValuePair<long, int>> Costs()
        {
            List<KeyValuePair<long, int>> l = new(Arrivals.Count);
            foreach (int s in Arrivals)
                l.Add(new KeyValuePair<long, int>(State(s), Cost[s]));
            return l;
        }

        public int CostOf(long state) => Cost[Find(state)];

        // The directions from a start to the state. A teleporter on the way ends them on its tile: the server moves the
        // player from there, and the walk plans again from the landing.
        public List<int> Route(long end)
        {
            List<int> dirs = new();
            for (int c = Find(end); From[c] != -1;)
            {
                int f = From[c];
                if (f <= -2)
                {
                    dirs.Clear();
                    c = -2 - f;
                    continue;
                }
                dirs.Add(f & 7);
                c = f >> 3;
            }
            dirs.Reverse();
            return dirs;
        }
    }
    // The cheapest walks from the starts over the box x0, y0, w, h: every state reached, its cost and the step that
    // reached it (from: the previous state times 8 plus the direction, -1 at a start); stops at the first state goal
    // accepts and returns it, else -1 with the whole box walked. Cheapest first, a straight step StraightCost and a
    // diagonal DiagonalCost: every step takes the same time, so the route has the fewest steps, and of those the fewest
    // diagonals, a straight line where one will do (Damian, 2026-10-17: "the pathfinding is choosing diagonals here
    // when straight line would be preferred").
    // back: the walks INTO the starts, each step taken only where its reverse is open too, as a step down off a roof is
    // open and the climb back is not.
    // aimX, aimY, aimReach: the target the goal lies within reach of, when there is one: the search then goes cheapest
    // first by its cost so far plus a bound on the rest (A*), the tiles still to go past reach or to the nearest
    // teleporter in the box, a straight step each. No step costs less and a step moves either by at most one, so the
    // first goal reached is still the cheapest; a plan across a forest searched the whole circle round the start
    // (139947 ms from 1261,1745 to 2502,505, 2026-10-22).
    internal long Walks(GameLink k, IEnumerable<long> starts, int x0, int y0, int w, int h, Set<long> refused, bool shove,
        Func<int, int, int, bool> goal, out PlanGrid walked, bool back = false,
        int aimX = int.MinValue, int aimY = 0, int aimReach = 0)
    {
        const int H = PlanGrid.Headings;
        PlanGrid g = walked = new PlanGrid(x0, y0, w, h, refused);
        // The standing height state s's step in direction d stands at, int.MinValue when closed (Open's step rule),
        // worked out once per state and direction.
        Func<int, int, int> step = (s, d) =>
        {
            int z = g.Step[s * 8 + d];
            if (z != PlanGrid.Unknown)
                return z;
            int fx = g.X(s), fy = g.Y(s), fromZ = g.Z(s), x = fx + s_dX[d], y = fy + s_dY[d];
            if (!g.Walkable(x, y))
                z = int.MinValue;
            else
            {
                int top = g.Top[s];
                if (top == PlanGrid.Unknown)
                    g.Top[s] = top = UoMap.StartTop(fx, fy, fromZ);
                z = OpenCompute(k, x, y, fromZ, shove, UoMap.StepZ(fromZ, x, y, top));
            }
            g.Step[s * 8 + d] = z;
            return z;
        };
        // Each search node is a state and the heading it was reached by (h = (state << 4) | direction, 8 at a start or
        // off a teleporter), and a change of heading costs TurnCost: a turn is a move of its own and tires the mount,
        // and the shortest walk hugging Vesper's river bank turned at nearly every step (Damian, 2026-10-18: "use
        // backtracking for minimizing turns, bearing in mind ... a diagonal move actually moves more real distance than
        // the 4 cardinals"). cost and from hold, per plain state, its first (cheapest) arrival; the goal's route is
        // written into them along the node chain it was reached by.
        PriorityQueue<int, int> queue = new();
        foreach (long s in starts)
        {
            int hs = g.Index(StateX(s), StateY(s), StateZ(s)) * H + 8;
            g.NodeCost[hs] = 0;
            g.NodeFrom[hs] = -1;
            queue.Enqueue(hs, 0);
        }
        // The known teleporters as one-step links (from: -2 minus the state the link leaves), reversed for back.
        Map<long, List<int[]>> tele = Teleporters(k);
        Map<long, List<long>> into = Map<long, List<long>>.Empty;
        foreach (KeyValuePair<long, List<int[]>> kv in tele)
        {
            foreach (int[] e in kv.Value)
            {
                long a = State((int)(kv.Key >> 16), (int)(kv.Key & 0xFFFF), e[0]), b = State(e[1], e[2], e[3]);
                long src = back ? b : a, dst = back ? a : b;
                List<long>? l;
                if (!into.TryGet(src, out l))
                    into = into.Set(src, l = new List<long>());
                l.Add(dst);
            }
        }

        bool aimed = aimX != int.MinValue && !back;
        List<int[]> teleIn = new();
        if (aimed)
            foreach (long src in into.Keys)
            {
                int sx = StateX(src), sy = StateY(src);
                if (sx >= x0 && sy >= y0 && sx < x0 + w && sy < y0 + h)
                    teleIn.Add(new[] { sx, sy });
            }
        Func<int, int, int> rest = (x, y) =>
        {
            if (!aimed)
                return 0;
            int r = Math.Max(0, Tiles(x, y, aimX, aimY) - aimReach);
            foreach (int[] t in teleIn)
            {
                int d = Tiles(x, y, t[0], t[1]);
                if (d < r)
                    r = d;
            }
            return r * StraightCost;
        };
        // A stop asked of the circuit, a loop or the map's walk ends a plan on their thread at once (Damian,
        // 2026-10-22: four stops went unheard through one 140 s plan); planHalted tells the caller it was stopped, not
        // walled in.
        Thread me = Thread.CurrentThread;
        Func<bool> stopTest = PlanStopped;
        bool canHalt = stopTest != null || me == m_circuitThread || m_loops.Runs(me) || me == m_mapWalk
            || me == m_hopThread;
        int polls = 0;
        while (queue.Count > 0)
        {
            if ((++polls & 255) == 0 && s_planBy != DateTime.MinValue && Clock.UtcNow > s_planBy)
            {
                s_planOverBudget = true;
                s_planHalted = true;
                return -1;
            }
            if (canHalt && (polls & 255) == 0 && (stopTest != null ? stopTest() : circuitStop))
            {
                s_planHalted = true;
                return -1;
            }
            int hc = queue.Dequeue();
            if (g.Done[hc])
                continue;
            g.Done[hc] = true;
            s_planStates++;
            int c = hc / H, head = hc % H;
            int cx = g.X(c), cy = g.Y(c), cz = g.Z(c);
            long cs = g.State(c);
            int hp = g.NodeFrom[hc], hcc = g.NodeCost[hc];
            if (!g.Arrived[c])
                g.Arrive(c, hcc, hp == -1 ? -1 : hp <= -2 ? -2 - (-2 - hp) / H : hp / H * 8 + head);
            // A teleporter's tile sends whoever arrives on it away (Damian, 2026-10-22: "avoid the return teleporter
            // tiles, as they will bounce you to town"): walking forward, no walk ends on one and none steps on past
            // one; its link is the only way off it. A start on one is walked off.
            bool teleTile = !back && into.ContainsKey(cs) && hp != -1;
            if (goal != null && goal(cx, cy, cz) && !teleTile)
            {
                // The route the goal was reached by, written over each state's first arrival.
                for (int n = hc; g.NodeFrom[n] != -1;)
                {
                    int p = g.NodeFrom[n], pn = p <= -2 ? -2 - p : p;
                    g.Arrive(n / H, g.NodeCost[n], p <= -2 ? -2 - pn / H : pn / H * 8 + n % H);
                    n = pn;
                }
                return cs;
            }
            List<long>? links;
            if (into.TryGet(cs, out links))
            {
                foreach (long n in links)
                {
                    int nx = StateX(n), ny = StateY(n);
                    if (!g.InBox(nx, ny))
                        continue;
                    int hn = g.Index(nx, ny, StateZ(n)) * H + 8;
                    int nc = hcc + StraightCost;
                    if (g.Done[hn] || nc >= g.NodeCost[hn])
                        continue;
                    g.NodeCost[hn] = nc;
                    g.NodeFrom[hn] = -2 - hc;
                    queue.Enqueue(hn, nc + rest(nx, ny));
                }
            }

            if (teleTile)
                continue;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + s_dX[d], ny = cy + s_dY[d];
                int nz = step(c, d);
                if (nz == int.MinValue)
                    continue;
                int ns = -1;
                // The step back is the opposite direction, (d + 4) & 7, from the state stepped to.
                if (back && step(ns = g.Index(nx, ny, nz), (d + 4) & 7) != cz)
                    continue;
                bool diag = s_dX[d] != 0 && s_dY[d] != 0;
                // A diagonal's two sides: east (2) or west (6), and north (0) or south (4).
                if (diag && (step(c, s_dX[d] > 0 ? 2 : 6) == int.MinValue
                    || step(c, s_dY[d] > 0 ? 4 : 0) == int.MinValue))
                    continue;
                int nc = hcc + (diag ? DiagonalCost : StraightCost) + (head != 8 && head != d ? TurnCost : 0);
                int hn = (ns >= 0 ? ns : g.Index(nx, ny, nz)) * H + d;
                if (g.Done[hn] || nc >= g.NodeCost[hn])
                    continue;
                g.NodeCost[hn] = nc;
                g.NodeFrom[hn] = hc;
                queue.Enqueue(hn, nc + rest(nx, ny));
            }
        }
        return -1;
    }
    // The server's hidden teleporters (Jhelom's islands are joined by them, Damian 2026-10-17; the walk to Kimmie on
    // the south island had "no way"): learned from AssistState.Jumps, one way each, tile stepped onto to landing, kept
    // in teleporters-<server>.txt ("x,y,z>x,y,z").
    Map<long, List<int[]>> m_teleporters = Map<long, List<int[]>>.Empty; bool m_teleportersLoaded;
    readonly Lock m_teleportersGate = new();
    string TeleportersPath => ServerFile("teleporters");
    static long TileKey(int x, int y)
    {
        return ((long)x << 16) | (uint)y;
    }
    void AddTeleporter(int fx, int fy, int fz, int tx, int ty, int tz)
    {
        List<int[]>? l;
        long key = TileKey(fx, fy);
        if (!m_teleporters.TryGet(key, out l))
            m_teleporters = m_teleporters.Set(key, l = new List<int[]>());
        if (!l.Exists(e => OnFloor(e[0], fz) && e[1] == tx && e[2] == ty))
            l.Add(new[] { fz, tx, ty, tz });
        // The tile's lower floors too: a teleporter fires for a walker within TeleporterReach below it (ServUO's
        // OnMoveOver window), and the planner's states hold one floor each (Damian, 2026-10-24: "should have been using
        // the teleporters right outside the building here to get back to city"; Moonglow 4540,898, learned at z 32 on
        // the pad, reached from the road at z 27 on the step, never planned through).
        foreach (int f in TeleporterFloors(fz, from => UoMap.StandZ(fx, fy, from)))
            if (!l.Exists(e => e[0] == f && e[1] == tx && e[2] == ty))
                l.Add(new[] { f, tx, ty, tz });
    }
    public const int TeleporterReach = 16;
    // The standing heights below fz within TeleporterReach that standZ (the tile's floor stood on from a height) gives.
    public static List<int> TeleporterFloors(int fz, Func<int, int> standZ)
    {
        List<int> l = new();
        for (int from = fz - TeleporterReach; from < fz; from++)
        {
            int s = standZ(from);
            if (s != int.MinValue && s < fz && s >= fz - TeleporterReach && !l.Contains(s))
                l.Add(s);
        }
        return l;
    }
    // Teleporters known before any is seen (Damian, 2026-10-18: "teleporters you can guess from public data, and ones
    // that are found by in game discovery"; Jhelom's islands: "need to take the teleporter in the middle of the middle
    // island"): teleporters-public.txt beside the sources, ServUO's Data/teleporters.csv Felucca rows; the 1998 scripts
    // hold none. A teleporter that does not move the player is forgotten (dead-teleporters-<server>.txt) once its 8
    // neighbours were tried (WalkTo).
    static IEnumerable<string> PublicTeleporters()
    {
        string p = Path.Combine(AssistRelay.SourceDir ?? "", "teleporters-public.txt");
        try
        {
            return File.Exists(p) ? File.ReadAllLines(p) : new string[0];
        }
        catch { return new string[0]; }
    }
    string DeadTeleportersPath => ServerFile("dead-teleporters");
    // The tiles whose teleporter came from the public file rather than from a jump seen in game.
    Set<long> m_guessedTeleporters = Set<long>.Empty;
    void ForgetTeleporter(int x, int y)
    {
        lock (m_teleportersGate)
            m_teleporters = m_teleporters.Remove(TileKey(x, y));
        try
        {
            File.AppendAllText(DeadTeleportersPath, x + "," + y + Environment.NewLine);
        }
        catch { }
        // Out of the learned file too: the dead list is read for the public teleporters only, and a learned one came
        // back at every restart (1418,3831, 2026-10-18).
        lock (m_teleportersGate)
        {
            try
            {
                if (File.Exists(TeleportersPath))
                {
                    string head = x + "," + y + ",";
                    string[] keep = Array.FindAll(File.ReadAllLines(TeleportersPath), l => !l.StartsWith(head));
                    File.WriteAllLines(TeleportersPath, keep);
                }
            }
            catch { }
        }

        Note("Walk: the teleporter at " + x + "," + y + " did not move the player; forgotten");
    }
    // The teleporters known, the new jumps the session saw added first (and written to the file).
    Map<long, List<int[]>> Teleporters(GameLink k)
    {
        lock (m_teleportersGate)
        {
            if (!m_teleportersLoaded)
            {
                m_teleportersLoaded = true;
                Set<long> dead = Set<long>.Empty;
                try
                {
                    if (File.Exists(DeadTeleportersPath))
                    {
                        foreach (string line in File.ReadAllLines(DeadTeleportersPath))
                        {
                            string[] a = line.Split(',');
                            int dx, dy;
                            if (a.Length == 2 && int.TryParse(a[0], out dx) && int.TryParse(a[1], out dy))
                                dead = dead.Add(TileKey(dx, dy));
                        }
                    }
                }
                catch { }
                // A guessed teleporter stands on the floor the client's map gives nearest its listed height, and lands
                // on the same: the planner's states carry the standing height exactly, and ServUO's z need not be
                // uo1998's floor (a house floor above the ground, a stair).
                Action<string, bool> take = (line, guessed) =>
                {
                    string[] ends = line.Split('>');
                    if (ends.Length != 2)
                        return;
                    string[] a = ends[0].Split(',');
                    string[] b = ends[1].Split(',');
                    int fx, fy, fz, tx, ty, tz;
                    if (a.Length != 3 || b.Length != 3 || !int.TryParse(a[0], out fx) || !int.TryParse(a[1], out fy)
                        || !int.TryParse(a[2], out fz) || !int.TryParse(b[0], out tx) || !int.TryParse(b[1], out ty)
                        || !int.TryParse(b[2], out tz))
                        return;
                    if (guessed)
                    {
                        if (dead.Contains(TileKey(fx, fy)))
                            return;
                        int sz = UoMap.StandZ(fx, fy, fz);
                        if (sz == int.MinValue)
                            return;
                        fz = sz;
                        sz = UoMap.StandZ(tx, ty, tz);
                        if (sz != int.MinValue)
                            tz = sz;
                        m_guessedTeleporters = m_guessedTeleporters.Add(TileKey(fx, fy));
                    }
                    else
                    {
                        m_guessedTeleporters = m_guessedTeleporters.Remove(TileKey(fx, fy));
                    }

                    AddTeleporter(fx, fy, fz, tx, ty, tz);
                };
                int publicCount = 0;
                foreach (string line in PublicTeleporters())
                {
                    take(line, true);
                    publicCount++;
                }
                try
                {
                    if (File.Exists(TeleportersPath))
                        foreach (string line in File.ReadAllLines(TeleportersPath))
                            take(line, false);
                }
                catch { }
                if (publicCount == 0)
                    Note("Walk: no teleporters-public.txt beside the sources (" + (AssistRelay.SourceDir ?? "")
                        + "); only teleporters seen in game are known");
            }
            List<int[]> fresh = new();
            if (k != null)
                lock (k.State.Gate)
                {
                    fresh.AddRange(k.State.Jumps);
                    k.State.Jumps.Clear();
                }
            foreach (int[] j in fresh)
            {
                // A jump already known is not written again: every pass over the Moonglow pads appended its line, 59
                // lines of two pads by 2026-10-26 (rule 8, the files are bounded).
                List<int[]>? had;
                if (m_teleporters.TryGet(TileKey(j[0], j[1]), out had)
                    && had.Exists(e => OnFloor(e[0], j[2]) && e[1] == j[3] && e[2] == j[4]))
                    continue;
                AddTeleporter(j[0], j[1], j[2], j[3], j[4], j[5]);
                m_guessedTeleporters = m_guessedTeleporters.Remove(TileKey(j[0], j[1]));
                try
                {
                    File.AppendAllText(TeleportersPath,
                        j[0] + "," + j[1] + "," + j[2] + ">" + j[3] + "," + j[4] + "," + j[5] + Environment.NewLine);
                }
                catch { }
                Note("Walk: learned a teleporter, " + j[0] + "," + j[1] + " z " + j[2] + " to " + j[3] + "," + j[4]
                    + " z " + j[5]);
            }
            // The lists copied too: AddTeleporter and a forgotten teleporter change them under the lock while a plan
            // walks them.
            Map<long, List<int[]>> copy = Map<long, List<int[]>>.Empty;
            foreach (KeyValuePair<long, List<int[]>> kv in m_teleporters)
                copy = copy.Set(kv.Key, new List<int[]>(kv.Value));
            return copy;
        }
    }
    // A box with no way is widened, PlanMargin doubled up to PlanWiden times: Vesper's canal crossing from the east
    // provisioner (2979,640) to the bank (2896,677) runs north to y 606, ten tiles past the first box (Damian's walk,
    // 2026-10-18).
    public const int PlanWiden = 2;
    internal List<int>? PlanPath(GameLink k, int sx, int sy, int pz, int tx, int ty, int reach, Set<long> refused,
        bool sight = false, bool shove = false, int tz = AnyFloor)
    {
        DateTime began = Clock.UtcNow;
        s_planHalted = false;
        s_planStates = 0;
        List<int>? route = PlanPathIn(k, sx, sy, pz, tx, ty, reach, refused, sight, shove, tz);
        int ms = (int)(Clock.UtcNow - began).TotalMilliseconds;
        if (s_planHalted)
            Note("Walk: planning " + sx + "," + sy + " to " + tx + "," + ty + " stopped after " + ms + " ms");
        else if (ms > 1000)
            Note("Walk: planning " + sx + "," + sy + " to " + tx + "," + ty + " took " + ms + " ms, " + s_planStates
                + " states searched" + (route == null ? ", no route" : ""));
        return route;
    }
    // The search states the plans since PlanPath began expanded, on this thread (Walks).
    [ThreadStatic] static long s_planStates;
    // Whether the last plan on this thread ended on a stop (Walks).
    [ThreadStatic] static bool s_planHalted;
    // A time budget for the plans on this thread, MinValue for none (the bunny hop's whole route, Damian 2026-10-23:
    // "max distance should be a thing we figure out based on the pathfinder algorithm run. if its too big/slow we
    // should disallow it"); planOverBudget: a plan ended on it.
    [ThreadStatic] static DateTime s_planBy;
    [ThreadStatic] static bool s_planOverBudget;
    // A test's own stop for every plan, on any thread; null for the circuit's, a loop's or the map walk's stop.
    internal Func<bool> PlanStopped
    {
        get; set;
    } = null!;
    // A rectangle (x, y, w, h) the planner keeps to on this thread, or null: the shop a vendor and the player stand in
    // (NearVendor).
    [ThreadStatic] static int[]? s_walkWithin;
    List<int>? PlanPathIn(GameLink k, int sx, int sy, int pz, int tx, int ty, int reach, Set<long> refused,
        bool sight, bool shove, int tz)
    {
        int[]? lim = s_walkWithin;
        for (int widen = 0, m = PlanMargin; widen <= PlanWiden; widen++, m *= 2)
        {
            int x0 = Math.Min(sx, tx) - m, y0 = Math.Min(sy, ty) - m, w = Math.Abs(sx - tx) + 2 * m
                + 1, h = Math.Abs(sy - ty) + 2 * m + 1;
            if (lim != null)
            {
                int x1 = Math.Min(x0 + w, lim[0] + lim[2]), y1 = Math.Min(y0 + h, lim[1] + lim[3]);
                x0 = Math.Max(x0, lim[0]);
                y0 = Math.Max(y0, lim[1]);
                w = x1 - x0;
                h = y1 - y0;
                if (w <= 0 || h <= 0)
                    return null;
            }
            long goal = Walks(k, new[] { State(sx, sy, pz) }, x0, y0, w, h, refused, shove,
                (cx, cy, cz) => Tiles(cx, cy, tx, ty) <= reach && OnFloor(cz, tz)
                && (!sight || Sight(k, cx, cy, cz, tx, ty)), out PlanGrid walked, false, tx, ty, reach);
            if (goal >= 0)
                return walked.Route(goal);
            if (s_planHalted)
                return null;
        }
        return null;
    }
    // Teleport (spell 22, circle 3) where no walk reaches (Damian, 2026-10-17: "this is the whole point of the teleport
    // spell ... to access otherwise unwalkable places"; the Magincia mage shop's upper floors have no stairs). The 1998
    // rules (teleportbase.m): within 11 tiles ("That location is too far away"), seen ("Target cannot be seen."), a
    // standing spot from the targeted z to z + 8, no one there, no house or boat part within 5. TeleportMana is UO's
    // circle 3 cost (Recall's 11 for circle 4 was measured); TeleportCost weighs a cast as 30 steps against walking.
    public const int TeleporterWaitMs = 2000, ShoveWaitMs = 90000;
    public const int TeleportSpell = 22, TeleportMana = 9, TeleportRange = 11, TeleportTries = 3, TeleportHops = 3,
        TeleportCost = 30 * StraightCost, TeleportMoveMs = 4000;
    static readonly ImmutableArray<int> s_teleportReagents = [ 0xF7B, 0xF86 ];  // bloodmoss, mandrake root
    static readonly ImmutableArray<string> s_teleportRefusals = [ "too far away", "cannot be seen",
        "Cannot teleport to that spot",
        "Someone is standing there", "can't teleport there", "too encumbered" ];
    // Every cast the server refused, cast tile to landing ("x,y,z>x,y,z"), kept in teleport-refused-<server>.txt: a
    // refused pair is never cast again (L-FUTILITY); another pair is new circumstances.
    Set<string> m_teleportRefused = Set<string>.Empty; bool m_teleportRefusedLoaded;
    readonly Lock m_teleportRefusedGate = new();
    string TeleportRefusedPath => ServerFile("teleport-refused");
    static string Pair(long r, long l)
    {
        return StateX(r) + "," + StateY(r) + "," + StateZ(r) + ">" + StateX(l) + "," + StateY(l) + "," + StateZ(l);
    }
    bool TeleportRefused(string pair)
    {
        lock (m_teleportRefusedGate)
        {
            LoadOnce(ref m_teleportRefusedLoaded, TeleportRefusedPath,
                l => m_teleportRefused = m_teleportRefused.Add(l.Trim()));
            return m_teleportRefused.Contains(pair);
        }
    }
    void RefuseTeleport(string pair)
    {
        TeleportRefused(pair);
        lock (m_teleportRefusedGate)
        {
            if (m_teleportRefused.Contains(pair))
                return;
            m_teleportRefused = m_teleportRefused.Add(pair);
            try
            {
                File.AppendAllText(TeleportRefusedPath, pair + Environment.NewLine);
            }
            catch { }
        }
    }
    // The teleport-only spots near the player, for the map (Actions, Movement; Damian, 2026-10-17: "flag unwalkable
    // teleport spots, those spots like the ones on the roof there of the magincia mage shop", "unwalkable from where
    // you stand, at least by some reasonable heuristic"): every standing spot within TeleportRange, on land or on a
    // surface, that a Teleport can aim at and no walk from the player reaches inside the plan box (PlanMargin past the
    // range). Worked out off the window's thread when the player has moved, and kept until the next move.
    List<int[]> m_teleportOnly = new(); long m_teleportOnlyAt = -1; volatile bool m_teleportOnlyBusy;
    public List<int[]> TeleportOnlySpots()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return new List<int[]>();
        int px, py, pz;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
            pz = k.State.Z;
        }
        long at = State(px, py, pz);
        if (at != m_teleportOnlyAt && !m_teleportOnlyBusy)
        {
            m_teleportOnlyBusy = true;
            ThreadPool.QueueUserWorkItem(_ => { try { List<long> walked; List<int[]> l = TeleportOnlyAt(k, px, py, pz,
                out walked); m_teleportWalked = walked; m_teleportOnly = l; m_teleportOnlyAt = at; } catch { } finally { m_teleportOnlyBusy = false; } });
        }
        return m_teleportOnly;
    }
    // Where a teleport-only spot can be cast from (Damian, 2026-10-19: "the map needs to show "from where" a spot is
    // teleportable to when it is hovered over"): every tile the player's walks reach within TeleportRange of it with
    // the spot in sight (Sight, at the caster's eye), one per tile.
    List<long> m_teleportWalked = new();
    public List<int[]> TeleportFrom(int x, int y)
    {
        List<int[]> l = new();
        GameLink k = m_link;
        if (k == null || k.Closed)
            return l;
        Set<long> tiles = Set<long>.Empty;
        foreach (long s in m_teleportWalked)
        {
            int sx = StateX(s), sy = StateY(s), sz = StateZ(s);
            if (Tiles(sx, sy, x, y) > TeleportRange || (sx == x && sy == y) || tiles.Contains(TileKey(sx, sy))
                || !Sight(k, sx, sy, sz, x, y))
                continue;
            tiles = tiles.Add(TileKey(sx, sy));
            l.Add(new[] { sx, sy, sz });
        }
        return l;
    }
    List<int[]> TeleportOnlyAt(GameLink k, int px, int py, int pz, out List<long> walkedStates)
    {
        int m = PlanMargin + TeleportRange, x0 = px - m, y0 = py - m, w = 2 * m + 1;
        Walks(k, new[] { State(px, py, pz) }, x0, y0, w, w, Set<long>.Empty, false, null!, out PlanGrid plan);
        walkedStates = plan.States();
        List<int[]> spots = new();
        ValueMap<long, int> seen = ValueMap<long, int>.Empty;
        List<UoTiles.Tile> items = UoTiles.Items();
        // The heights the walks reached on each tile: a spot is walked to when any of them is on its floor, as a dip in
        // the land is reached a few z off its corner average (Damian, 2026-10-17: "a weird little divet in the map ...
        // i can walk there").
        Map<long, List<int>> reached = Map<long, List<int>>.Empty;
        foreach (long s in walkedStates)
        {
            long t = TileKey(StateX(s), StateY(s));
            List<int>? l;
            if (!reached.TryGet(t, out l))
                reached = reached.Set(t, l = new List<int>());
            l.Add(StateZ(s));
        }
        for (int x = px - TeleportRange; x <= px + TeleportRange; x++)
        {
            for (int y = py - TeleportRange; y <= py + TeleportRange; y++)
            {
                // Each floor on the tile: the land, and the top of every surface; a floor counts where a walker stands
                // on it.
                Set<int> floors = Set<int>.Empty;
                if (!UoMap.LandBlocks(x, y))
                    floors = floors.Add(UoMap.LandStandZ(x, y));
                foreach (int[] s in UoMap.StaticsAt(x, y))
                {
                    if (s[0] < 0 || s[0] >= items.Count)
                        continue;
                    UoTiles.Tile t = items[s[0]];
                    if ((t.Flags & 0x200) != 0 && (t.Flags & 0x40) == 0)
                        floors = floors.Add(s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height));
                }
                foreach (int f in floors)
                {
                    int z = Open(k, x, y, f, ref seen);
                    if (z != f || TeleportAim(x, y, z) == null)
                        continue;
                    // Walked to when this tile or one beside it was reached on this floor: a hollow entered only
                    // diagonally past two blocked sides, which the planner does not step, is walked to on uo1998
                    // (2659,459 at z 0 beside 2660,460 at 15, Damian, 2026-10-17: "another here").
                    bool walked = false;
                    for (int dx = -1; dx <= 1 && !walked; dx++)
                        for (int dy = -1; dy <= 1 && !walked; dy++)
                        {
                            List<int>? got;
                            walked = reached.TryGet(TileKey(x + dx, y + dy), out got)
                                && got.Exists(r => OnFloor(r, z));
                        }
                    if (walked)
                        continue;
                    spots.Add(new[] { x, y, z });
                }
            }
        }

        return spots;
    }
    // Where a cast at a landing state aims: a surface static whose top is the landing height (its graphic and z, as the
    // client targets the roof tile, 0x0569 at z 60), else the land there (graphic 0); null when neither stands there.
    static int[]? TeleportAim(int x, int y, int z)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        foreach (int[] s in UoMap.StaticsAt(x, y))
        {
            if (s[0] < 0 || s[0] >= items.Count)
                continue;
            UoTiles.Tile t = items[s[0]];
            if ((t.Flags & 0x200) == 0 || (t.Flags & 0x40) != 0)
                continue;
            if (s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height) == z)
                return new[] { s[0], s[1] };
        }
        return UoMap.LandStandZ(x, y) == z && !UoMap.LandBlocks(x, y) ? new[] { 0, z } : null;
    }
    // One cast where no walk reaches the target: the cast state among those walked to from the start, the landing among
    // those the target is walked to from, at most TeleportRange apart, the cheapest walk to the cast plus the walk on
    // from the landing, no pair refused before; on one floor the landing must be in sight. Null when none.
    sealed class Hop
    {
        public long Cast, Land; public int Graphic, AimZ; public List<int> Walk = null!;
    }
    // Whether a step leaves x, y at z for any of its 8 neighbours (UoMap.StepZ, the server's step rule).
    static bool HasExit(int x, int y, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if ((dx != 0 || dy != 0) && UoMap.StepZ(x, y, z, x + dx, y + dy) != int.MinValue)
                    return true;
        return false;
    }
    Hop? TeleportHop(GameLink k, int sx, int sy, int sz, int tx, int ty, int tz, int reach, bool sight,
        Set<long> refused)
    {
        if (tz == AnyFloor)
            return null;
        int m = PlanMargin + TeleportRange, x0 = Math.Min(sx, tx) - m, y0 = Math.Min(sy, ty)
            - m, w = Math.Abs(sx - tx) + 2 * m + 1, h = Math.Abs(sy - ty) + 2 * m + 1;
        // A walk cut short by a stop is no map of where the player reaches: no cast is chosen from it.
        s_planHalted = false;
        Walks(k, new[] { State(sx, sy, sz) }, x0, y0, w, h, refused, false, null!, out PlanGrid walkedR);
        if (s_planHalted)
            return null;
        // The target's floor: every tile within reach where a walker on that floor stands in sight of it.
        List<long> goals = new();
        ValueMap<long, int> seen = ValueMap<long, int>.Empty;
        for (int x = tx - reach; x <= tx + reach; x++)
        {
            for (int y = ty - reach; y <= ty + reach; y++)
            {
                int z = Open(k, x, y, tz, ref seen);
                if (z == int.MinValue || !OnFloor(z, tz) || (sight && !Sight(k, x, y, z, tx, ty)))
                    continue;
                goals.Add(State(x, y, z));
            }
        }

        if (goals.Count == 0)
            return null;
        Walks(k, goals, x0, y0, w, h, refused, false, null!, out PlanGrid walkedC, true);
        if (s_planHalted)
            return null;
        Map<long, List<long>> byTile = Map<long, List<long>>.Empty;
        foreach (long r in walkedR.States())
        {
            long t = ((long)StateX(r) << 16) | (uint)StateY(r);
            List<long>? l;
            if (!byTile.TryGet(t, out l))
                byTile = byTile.Set(t, l = new List<long>());
            l.Add(r);
        }
        Hop? best = null;
        int bestCost = int.MaxValue;
        // Landings cheapest first, so the scan stops once no landing can beat the best pair found.
        List<KeyValuePair<long, int>> lands = walkedC.Costs();
        lands.Sort((a, b) => a.Value.CompareTo(b.Value));
        foreach (KeyValuePair<long, int> kv in lands)
        {
            long land = kv.Key;
            int lx = StateX(land), ly = StateY(land), lz = StateZ(land);
            if (kv.Value + TeleportCost >= bestCost)
                break;
            if (walkedR.Reached(land) || Occupied(k, lx, ly))
                continue;
            // Never onto a door ("Cannot teleport to that spot.", Radcliffe's door at 2534,577, 2026-10-18 23:44:41).
            List<int[]>? onLand;
            if (BlockersNow(k).Ground.TryGet(((long)lx << 16) | (uint)ly, out onLand)
                && onLand.Exists(g => IsDoor(g[0])))
                continue;
            int[]? aim = TeleportAim(lx, ly, lz);
            if (aim == null)
                continue;
            // Never onto an island no step leaves (Damian, 2026-10-19: "the character appears stuck in a wrong spot
            // there, on the shoe case"): The Right Fit's display case at 1564,1650 carries a half apron the 1.25
            // TILEDATA flags a surface, so it stands at z 30 inside a ring of impassable cases, and a landing there
            // left no walk out.
            if (!HasExit(lx, ly, lz))
                continue;
            for (int x = lx - TeleportRange; x <= lx + TeleportRange; x++)
            {
                for (int y = ly - TeleportRange; y <= ly + TeleportRange; y++)
                {
                    List<long>? casts;
                    if (!byTile.TryGet(((long)x << 16) | (uint)y, out casts))
                        continue;
                    foreach (long r in casts)
                    {
                        int c = walkedR.CostOf(r) + kv.Value + TeleportCost;
                        if (c >= bestCost)
                            continue;
                        if (OnFloor(StateZ(r), lz) && !Sight(k, x, y, StateZ(r), lx, ly))
                            continue;
                        if (TeleportRefused(Pair(r, land)))
                            continue;
                        best = new Hop { Cast = r, Land = land, Graphic = aim[0], AimZ = aim[1] };
                        bestCost = c;
                    }
                }
            }
        }
        if (best != null)
            best.Walk = walkedR.Route(best.Cast);
        return best;
    }
    // Casts Teleport at the hop's landing: the reagents and mana first, again only after a fizzle; null once the server
    // has put the player on the landing, else the server's refusal (the pair is then never cast again).
    string? Teleport(GameLink k, Hop hop)
    {
        int lx = StateX(hop.Land), ly = StateY(hop.Land);
        string pair = Pair(hop.Cast, hop.Land);
        string? fh = FreeHands(k);
        if (fh != null)
            return "the hands before the Teleport: " + fh;
        for (int attempt = 1; attempt <= TeleportTries; attempt++)
        {
            if (circuitStop)
                return "stopped";
            OpenPackBags(k);
            foreach (int r in s_teleportReagents)
                if (PackCount(k, g => g == r) == 0)
                    return "no " + UoTiles.Name(r) + " in the pack for Teleport";
            DateTime manaBy = Clock.UtcNow.AddMilliseconds(ManaWaitMs);
            while (true)
            {
                int mana;
                lock (k.State.Gate)
                    mana = k.State.Mana;
                if (mana >= TeleportMana)
                    break;
                if (Clock.UtcNow > manaBy || k.Closed)
                    return "mana stayed under " + TeleportMana;
                Clock.Sleep(500);
            }
            Heard mark;
            using (Act(k))
            {
                mark = new(k.State);
                string? why = k.Cast(TeleportSpell, true);
                if (why != null)
                    return why;
                DateTime until = Clock.UtcNow.AddMilliseconds(CastCursorMs);
                AwaitCursor(k, until, mark);
                if (k.MacroCursor)
                {
                    why = k.Target(true, 0, lx, ly, hop.AimZ, hop.Graphic);
                    if (why != null)
                        return "aiming the Teleport: " + why;
                }
            }
            // Landed only once moved off the cast tile: a landing beside the cast tile read the unmoved player as
            // landed, and the same cast went three times (Minoc, 2450,430 to 2450,431, 2026-10-18 22:15:02).
            DateTime moveBy = Clock.UtcNow.AddMilliseconds(TeleportMoveMs);
            int cx0 = StateX(hop.Cast), cy0 = StateY(hop.Cast);
            while (Clock.UtcNow < moveBy && !k.Closed)
            {
                int x, y, z;
                lock (k.State.Gate)
                {
                    x = k.State.X;
                    y = k.State.Y;
                    z = k.State.Z;
                }
                if ((x != cx0 || y != cy0) && Tiles(x, y, lx, ly) <= 1 && OnFloor(z, StateZ(hop.Land)))
                {
                    Note("Walk: teleported to " + x + "," + y + " z " + z);
                    return null;
                }
                List<string> heard0 = mark.Answers();
                if (heard0.Exists(j => j.IndexOf("fizzle", StringComparison.OrdinalIgnoreCase) >= 0)
                    || heard0.Exists(j =>
                    s_teleportRefusals.Exists(e => j.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)))
                    break;
                Clock.Sleep(100);
            }
            List<string> heard = mark.Answers();
            string? refusal = heard.Find(j =>
                s_teleportRefusals.Exists(e => j.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0));
            if (refusal != null)
            {
                RefuseTeleport(pair);
                return "the server refused the Teleport " + pair + ": \"" + refusal + "\"";
            }
            // L-FUTILITY: cast again only after a fizzle or "You must wait"; any other answer, or none, ends it.
            if (!heard.Exists(j => j.IndexOf("fizzle", StringComparison.OrdinalIgnoreCase) >= 0
                || j.IndexOf("You must wait", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                RefuseTeleport(pair);
                string? said = null;
                foreach (string j in heard)
                    said = j;
                return "the Teleport " + pair + " did not move you" + (said != null ? " (\"" + said + "\")" : "");
            }
            Clock.Sleep(Ms("busy:recall"));
        }
        return "the Teleport fizzled " + TeleportTries + " times";
    }
    // To where someone at x, y hears the player speak: within VendorReach (a vendor hears its name from 3 tiles) and in
    // sight, so not on the far side of a wall (Damian, 2026-10-17: "walkto should probably be walktospeakingrange").
    // A ServUO vendor hears by range alone (BaseVendor.cs:2497), so a tile in sight is preferred, not required: with
    // none reachable (Jannelle at 3761,1267 behind her shop's walls, 2026-10-17 09:07:04) any tile within reach does.
    // In sight is required: Slade, 3 tiles off behind his shop's closed door, never answered "Slade buy" (2026-10-17).
    public string? WalkToSpeakingRange(int x, int y, int z = AnyFloor)
    {
        return WalkTo(x, y, VendorReach, true, z);
    }
    // sight: arrived only where the target is in sight (Sight), for a vendor or a loom; a banker hears through walls.
    // teleport: a Teleport where no walk reaches (TeleportHop); off for the walk to a cast tile.
    public string? WalkTo(int tx, int ty, int reach, bool sight = false, int tz = AnyFloor, bool teleport = true)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        string? hw = HomeCross(k, tx, ty);
        if (hw != null)
            return "the house door: " + hw;
        return WalkToIn(tx, ty, reach, sight, tz, teleport);
    }
    // A tile the server refused a step onto with no door to open is a wall to every walk for WallMemoryMs (L-FUTILITY):
    // a mining circuit walked into the same cave walls every round, 13 rounds in a row (2026-10-20 18:52 to 19:03).
    public const int WallMemoryMs = 30 * 60 * 1000;
    ValueMap<long, DateTime> m_walls = ValueMap<long, DateTime>.Empty; readonly Lock m_wallsGate = new();
    string? WalkToIn(int tx, int ty, int reach, bool sight, int tz, bool teleport)
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        Set<long> blocked = Set<long>.Empty;
        Set<uint> opened = Set<uint>.Empty;
        Set<uint> ahead = Set<uint>.Empty;
        int[]? clearOf = null;
        // A leaf this walk opened (it swung) that has swung shut again on its own is opened again, ReopenMax times a
        // walk (Damian, 2026-10-24: "the door being closed (automatically) after pathing back to the vendor stunnded
        // the circuit into believing the path was blocked by a person. it was just the door"; Britain, Incantations &
        // Enchantments' double door, 14:57:22 to 14:57:52, pushes and a stamina rest). A leaf that never swung stays
        // unused (L-FUTILITY). Shut again means moved by the server since the walk's own last use (art or tile), not
        // merely read shut: a misread art, or the walk's own use shutting it, is no swing back.
        Map<uint, int[]> swungOpen = Map<uint, int[]>.Empty;
        Func<AssistItem, bool> reshut = d =>
            DoorShut(d.Graphic) != false && swungOpen.TryGet(d.Serial, out int[]? s) && s[0] < ReopenMax
            && (s[1] != d.Graphic || s[2] != d.X || s[3] != d.Y);
        foreach (KeyValuePair<long, DateTime> kv in m_walls)
            if ((Clock.UtcNow - kv.Value).TotalMilliseconds < WallMemoryMs)
                blocked = blocked.Add(kv.Key);
        // Doors left alone because someone stood beside them, each with its tile; waited for once when no other way
        // exists.
        List<Tuple<AssistItem, long>> heldDoors = new();
        Set<uint> heldWaited = Set<uint>.Empty;
        int steps = 0, refusals = 0, hops = 0, stalls = 0;
        List<int>? path = null;
        int followed = 0, pace = StepPace(k);
        // Steps go out as the client sends its own run: StepsAhead in flight before their answers, one every pace (the
        // client's mounted run, sniffed 2026-10-17: 25 steps in 2.8 s, a median of 94 ms apart, 2 outstanding at most;
        // waiting for each answer ran at 140 ms). The route is planned from where the steps in flight will put the
        // player; a refusal names the first unanswered step, and the rest are let settle before the walk goes on.
        Queue<int[]> flight = new();
        int acksSeen = k.StepAcks, refusalsSeen = k.StepRefusals;
        // The player's own step ends the walk (Damian, 2026-10-17: "you were controlling movement and the guy was
        // coming on and i couldn't get us outta harms way").
        int playerAcks = k.ClientStepAcks, playerRefusals = k.ClientStepRefusals;
        // Run or walk is decided again before every step (RunNow); runPace is the mount's or the foot's run.
        int runPace = pace, tiredRests = 0;
        int x = 0, y = 0, z = 0, f = 0, run = 0;
        DateTime nextAt = Clock.UtcNow;
        Heard journalMark = new(k.State), warnMark = new(k.State);
        // A recall first where it beats the walk (RecallNearer); a failed one leaves the walk as it was.
        if (teleport)
        {
            int rx, ry;
            lock (k.State.Gate)
            {
                rx = k.State.X;
                ry = k.State.Y;
            }
            uint rr = RecallNearer(k, rx, ry, tx, ty);
            if (rr != 0)
            {
                int[]? land = m_recallLand;
                Note("Walk: to " + tx + "," + ty + ", recalling by the rune " + rr.ToString("X") + " to " + land![0]
                    + "," + land[1] + ", " + Tiles(land[0], land[1], tx, ty) + " from it: " + m_lastRecallReason);
                string? rw = Recall(rr);
                if (rw == "the landing is blocked")
                    BlockRune(rr);
                if (rw != null)
                    Note("Walk: the recall: " + rw + "; walking");
            }
        }
        // A target farther than WalkStepsMax steps is no walk: a plan from Minoc to Britain's bank took a minute each
        // and three ran before the player stopped it (2026-10-18 00:26:08 to 00:28:18).
        {
            int fx, fy;
            lock (k.State.Gate)
            {
                fx = k.State.X;
                fy = k.State.Y;
            }
            if (Tiles(fx, fy, tx, ty) > WalkStepsMax)
                return tx + "," + ty + " is " + Tiles(fx, fy, tx, ty) + " tiles away, past a walk of " + WalkStepsMax
                    + " steps";
        }
        try
        {
            while (!circuitStop && !k.Closed)
            {
                // The player's own step stops the circuit as well as the walk: a trip that read it as a failed walk
                // recalled by a pack rune after Damian took over (2026-10-17 23:13 and 01:12).
                if (!m_clientWalks && (k.ClientStepAcks != playerAcks || k.ClientStepRefusals != playerRefusals))
                {
                    CircuitHalt(TookOver);
                    return TookOver;
                }
                for (int a = k.StepAcks; acksSeen < a; acksSeen++)
                    if (flight.Count > 0)
                        flight.Dequeue();
                if (k.StepRefusals != refusalsSeen)
                {
                    int[]? bad = flight.Count > 0 ? flight.Peek() : null;
                    flight.Clear();
                    Clock.Sleep(StepSettleMs);
                    refusalsSeen = k.StepRefusals;
                    acksSeen = k.StepAcks;
                    path = null;
                    // A tired mount or player is refused every step, not a wall (ServUO WeightOverloading: "Your mount
                    // is too fatigued to move." at 0 stamina): rest and go on, the tile not marked blocked.
                    bool tired = journalMark.Has("too fatigued");
                    journalMark = new(k.State);
                    // Then walked until the estimate says rested (Damian, 2026-10-17: "when the horse is exhausted,
                    // rest a bit, then walk"); refused again for fatigue, the rest is longer ("if walking is still a
                    // problem, rest a bit longer").
                    if (tired)
                    {
                        int rest = Math.Min(FatigueRestMaxMs, FatigueRestMs << Math.Min(4, tiredRests++));
                        CircuitStatus = "Resting: too fatigued to move";
                        Note("Walk: too fatigued to move; resting " + rest / 1000 + " s, then walking");
                        Clock.Sleep(rest);
                        continue;
                    }
                    if (bad != null && bad[3] == 0)
                    {
                        int bx = bad[0] + s_dX[bad[2]], by = bad[1] + s_dY[bad[2]];
                        if (++refusals > WalkRefusalsMax)
                            return "refused " + refusals + " steps on the way";
                        // A door beside the refused step: opened, and the step tried again. The door on the refused
                        // tile first, then the nearest the player: Vesper's double door at 2923-2924,671 had its far
                        // leaf used ("I can't reach that.") and the near one 0.8 s later ("You must wait to perform
                        // another action."), and neither opened (2026-10-18 12:45:20). A use answered "You must wait"
                        // is used again once after the busy time, the answer asking for it.
                        long tile = ((long)bx << 16) | (uint)by;
                        // An open leaf the walk did not open, standing on the refused tile, is a door's own swing, and
                        // a use would only shut it: the tile is closed to this walk alone.
                        if (DoorOn(k, bx, by,
                            d => DoorShut(d.Graphic) == false && !ahead.Contains(d.Serial)
                            && !opened.Contains(d.Serial)) != null)
                        {
                            Note("Walk: the open door at " + bx + "," + by + " stands in the way; going round it");
                            blocked = blocked.Add(tile);
                            continue;
                        }
                        AssistItem? door = null;
                        lock (k.State.Gate)
                            foreach (AssistItem g in k.State.Ground.Values)
                                if ((!opened.Contains(g.Serial) || reshut(g)) && Tiles(g.X, g.Y, bx, by) <= 1
                                    && IsDoor(g.Graphic) && OnWalkerFloor(k, g)
                                    && (door == null || Tiles(g.X, g.Y, bx, by) < Tiles(door.X, door.Y, bx, by)
                                    || (Tiles(g.X, g.Y, bx, by) == Tiles(door.X, door.Y, bx, by)
                                    && Tiles(g.X, g.Y, k.State.X, k.State.Y)
                                    < Tiles(door.X, door.Y, k.State.X, k.State.Y))))
                                    door = g;
                        if (door != null)
                        {
                            opened = opened.Add(door.Serial);
                            if (UseDoor(k, door, tile, ref blocked, heldDoors) == DoorUse.Opened)
                                Swung(k, ref swungOpen, door.Serial);
                        }
                        // A tile holding a door is no wall to the next walk: the leaf swings.
                        else
                        {
                            blocked = blocked.Add(tile);
                            if (DoorOn(k, bx, by, d => true) == null)
                                lock (m_wallsGate)
                                {
                                    if (m_walls.Count > 4096)
                                        m_walls = m_walls.Clear();
                                    m_walls = m_walls.Set(tile, Clock.UtcNow);
                                }
                        }
                    }
                }
                // The 10% warning stops the running at once (Damian, 2026-10-17: "when we get the 10% horse exhaustion
                // warning you stop the running immediately, rest for 5 seconds, then start walking until the horse is
                // rested ... usually due to a work step where you just sit there").
                if (run != 0 && warnMark.Has("very fatigued"))
                {
                    warnMark = new(k.State);
                    Wait.Until(Clock, () => k.StepAcks - acksSeen >= flight.Count || k.StepRefusals != refusalsSeen,
                        StepAnswerMs, "the steps' answers", stepMs: 10);
                    for (int a = k.StepAcks; acksSeen < a; acksSeen++)
                        if (flight.Count > 0)
                            flight.Dequeue();
                    CircuitStatus = "Resting: the horse is very fatigued";
                    Note("Walk: the horse is very fatigued; resting " + WarnRestMs / 1000
                        + " s, then walking until it is rested");
                    Clock.Sleep(WarnRestMs);
                    path = null;
                    continue;
                }
                if (flight.Count == 0)
                    lock (k.State.Gate)
                    {
                        x = k.State.X;
                        y = k.State.Y;
                        z = k.State.Z;
                        f = k.State.Facing;
                    }
                bool there = Tiles(x, y, tx, ty) <= reach && OnFloor(z, tz) && (!sight || Sight(k, x, y, z, tx, ty));
                if (there && flight.Count == 0)
                    return null;
                if (flight.Count > 0)
                {
                    // An unanswered step is resynced, not the end of the walk (D3 of the 2026-10-19 travel review: a
                    // step sent between a recall's two position updates went unanswered, and Ella, 8 tiles off, was
                    // dropped for 15 minutes, 21:11:35): the steps in flight forgotten, the position read again after
                    // the settle, the route planned anew; WalkStallsMax of them in one walk end it.
                    int[] oldest = flight.Peek();
                    if ((Clock.UtcNow
                        - new DateTime(((long)oldest[4] << 32) | (uint)oldest[5], DateTimeKind.Utc)).TotalMilliseconds
                        > StepAnswerMs)
                    {
                        if (++stalls > WalkStallsMax)
                            return "the server did not answer a step " + stalls + " times";
                        Note("Walk: no answer to a step in " + StepAnswerMs + " ms; reading the position again");
                        Clock.Sleep(StepSettleMs);
                        flight.Clear();
                        acksSeen = k.StepAcks;
                        refusalsSeen = k.StepRefusals;
                        lock (k.State.Gate)
                        {
                            x = k.State.X;
                            y = k.State.Y;
                            z = k.State.Z;
                            f = k.State.Facing;
                        }
                        path = null;
                        continue;
                    }
                }
                if (there || flight.Count >= StepsAhead || Clock.UtcNow < nextAt)
                {
                    Clock.Sleep(5);
                    continue;
                }
                if (steps >= WalkStepsMax)
                    return "still " + Tiles(x, y, tx, ty) + " tiles away after " + steps + " steps";
                // Through a doorway the walk keeps to its route until DoorClear tiles past the door, then plans afresh
                // (Damian, 2026-10-18: "building egress through a doorway clears the door by 2 tiles before finding
                // path to target").
                if (clearOf == null)
                    lock (k.State.Gate)
                        foreach (AssistItem g in k.State.Ground.Values)
                            if (IsDoor(g.Graphic) && Tiles(g.X, g.Y, x, y) <= 1)
                            {
                                clearOf = new[] { g.X, g.Y };
                                break;
                            }
                bool nearDoor = clearOf != null && Tiles(x, y, clearOf[0], clearOf[1]) < DoorClear;
                if (clearOf != null && !nearDoor)
                {
                    clearOf = null;
                    path = null;
                }
                if (path != null && path.Count > 0
                    && ((followed >= ReplanSteps && !nearDoor) || Occupied(k, x + s_dX[path[0]], y + s_dY[path[0]])))
                    path = null;
                if (path == null || path.Count == 0)
                {
                    if (flight.Count > 0)
                    {
                        Clock.Sleep(5);
                        continue;
                    }
                    path = PlanPath(k, x, y, z, tx, ty, reach, blocked, sight, false, tz);
                    followed = 0;
                    if (s_planHalted)
                        return "stopped";
                    // An empty route short of the target: the player stands on a teleporter the server has yet to act
                    // on.
                    if (path != null && path.Count == 0)
                    {
                        int mx = x, my = y;
                        Wait.Until(Clock,
                            () => { lock (k.State.Gate) { mx = k.State.X; my = k.State.Y; } return mx != x
                            || my != y; }, TeleporterWaitMs, "the teleporter", closed: () => k.Closed, stepMs: 50);
                        // A teleporter that does not move the player: a guessed one has each of its 8 neighbours
                        // stepped on (Damian, 2026-10-18: "add 1 tile in all directions search for any searched
                        // teleporter"), the jump there learned as any is; then the tile is forgotten, so no walk plans
                        // through it again.
                        if (mx == x && my == y)
                        {
                            // A learned one too: a jump learned one step short sends every walk to the tile before the
                            // teleporter (1418,3831 for 1419,3832, Damian 2026-10-18: "the bot did not walk onto it
                            // properly").
                            bool jumped = false;
                            for (int d = 0; d < 8 && !jumped && !circuitStop && !k.Closed; d++)
                            {
                                // On the teleporter's own floor: a tile can hold a floor above the ground (a house's),
                                // and a step onto the other floor fires nothing (Damian, 2026-10-18: "be careful of the
                                // z too").
                                int nx = x + s_dX[d], ny = y + s_dY[d], nz = UoMap.StandZ(nx, ny, z);
                                string? nw = WalkTo(nx, ny, 0, false, nz == int.MinValue ? AnyFloor : nz);
                                // The walk to the neighbour can itself be the jump, a known teleporter standing there.
                                lock (k.State.Gate)
                                {
                                    mx = k.State.X;
                                    my = k.State.Y;
                                }
                                if (Tiles(mx, my, nx, ny) > 2 && Tiles(mx, my, x, y) > 2)
                                {
                                    jumped = true;
                                    Note("Walk: the teleporter at " + x + "," + y + " stands at " + nx + "," + ny);
                                    break;
                                }
                                if (nw != null)
                                    continue;
                                jumped = Wait.Until(Clock,
                                    () => { lock (k.State.Gate) { mx = k.State.X; my = k.State.Y; } return Tiles(mx,
                                    my, nx, ny) > 2; }, TeleporterWaitMs, "the teleporter", closed: () => k.Closed,
                                    stepMs: 50).Done;
                                if (jumped)
                                    Note("Walk: the teleporter at " + x + "," + y + " stands at " + nx + "," + ny);
                            }
                            ForgetTeleporter(x, y);
                            // Forgotten, the walk plans again: another teleporter or a walk can still reach the target,
                            // and each pass forgets one, so the passes end.
                            lock (k.State.Gate)
                            {
                                x = k.State.X;
                                y = k.State.Y;
                                z = k.State.Z;
                            }
                            acksSeen = k.StepAcks;
                            refusalsSeen = k.StepRefusals;
                        }
                        path = null;
                        continue;
                    }
                    // No way round the people in it: at full stamina a step through one is taken, as a player shoves
                    // through a pinch point (Damian, 2026-10-17: "it can push through if it has full stamina ...
                    // sometimes just walking through is best"; ServUO refuses the shove below full stamina).
                    if (path == null || path.Count == 0)
                    {
                        int stam, maxStam;
                        lock (k.State.Gate)
                        {
                            stam = k.State.Stam;
                            maxStam = k.State.MaxStam;
                        }
                        if (maxStam > 0 && stam >= maxStam)
                        {
                            path = PlanPath(k, x, y, z, tx, ty, reach, blocked, sight, true, tz);
                            if (path != null && path.Count > 0)
                                Note("Walk: no way round the people here; pushing through at full stamina");
                        }
                        // Short of full stamina with a way through them: rest until full, ShoveWaitMs at most, then
                        // push (Damian, 2026-10-18: "push past, wait for stamina"; a buy ended at a mage shop's blocked
                        // door).
                        else if (maxStam > 0)
                        {
                            List<int>? through = PlanPath(k, x, y, z, tx, ty, reach, blocked, sight, true, tz);
                            if (through != null && through.Count > 0)
                            {
                                CircuitStatus = "Walk: resting to push past the people in the way (" + stam + " of "
                                    + maxStam + " stamina)";
                                Wait.Until(Clock,
                                    () => { lock (k.State.Gate) { stam = k.State.Stam; maxStam = k.State.MaxStam; } return stam
                                    >= maxStam; },
                                    ShoveWaitMs, "the rest", () => circuitStop, () => k.Closed, 500);
                                // The way may have cleared while resting: planned again, around people first.
                                path = PlanPath(k, x, y, z, tx, ty, reach, blocked, sight, false, tz);
                                if ((path == null || path.Count == 0) && stam >= maxStam)
                                {
                                    path = PlanPath(k, x, y, z, tx, ty, reach, blocked, sight, true, tz);
                                    if (path != null && path.Count > 0)
                                        Note("Walk: rested to full stamina; pushing through the people in the way");
                                }
                            }
                        }
                    }
                    // No walk: one Teleport from a tile walked to, onto a tile the target is walked to from
                    // (TeleportHop), at most TeleportHops a walk. A refused cast ends the walk (L-FUTILITY): the next
                    // pair was the same ask from the tile beside it, refused "Target cannot be seen." three times
                    // running (Vesper, 2026-10-18 12:45:24-30).
                    // No way round a door someone stood beside: the walk waits once, HeldDoorWaitMs at most, for nobody
                    // to stand beside it, then takes the door again as any door (Damian, 2026-10-18: "they trying to
                    // teleport through doors"; a Teleport onto Radcliffe's door at 2534,577, "Cannot teleport to that
                    // spot.").
                    Tuple<AssistItem, long>? heldDoor = (path == null || path.Count == 0)
                        ? heldDoors.Find(d => !heldWaited.Contains(d.Item1.Serial)) : null;
                    if (heldDoor != null)
                    {
                        if (flight.Count > 0)
                        {
                            Clock.Sleep(5);
                            continue;
                        }
                        AssistItem hd = heldDoor.Item1;
                        heldWaited = heldWaited.Add(hd.Serial);
                        CircuitStatus = "Waiting for the way through the " + UoTiles.Name(hd.Graphic) + " at " + hd.X
                            + "," + hd.Y;
                        DateTime until = Clock.UtcNow.AddMilliseconds(HeldDoorWaitMs);
                        bool held = true;
                        while (held && Clock.UtcNow < until && !circuitStop && !k.Closed)
                        {
                            held = false;
                            lock (k.State.Gate)
                                foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                                    if (kv.Key != k.State.Player && !OwnVendor(kv.Key)
                                        && Tiles(kv.Value[0], kv.Value[1], hd.X, hd.Y) <= 1)
                                    {
                                        held = true;
                                        break;
                                    }
                            if (held)
                                Clock.Sleep(250);
                        }
                        string dn = "the " + UoTiles.Name(hd.Graphic) + " at " + hd.X + "," + hd.Y;
                        if (!held)
                        {
                            Note("Walk: no way but " + dn + "; clear, taking it");
                            blocked = blocked.Remove(heldDoor.Item2);
                            opened = opened.Remove(hd.Serial);
                            path = null;
                            continue;
                        }
                        // Still crowded (Damian, 2026-10-18: "recall if stuck, give up that local target if you cannot
                        // make ingress"): from inside a shop the target is not in, a recall out by a pack rune for the
                        // town; from outside, the target is given up.
                        string? town = CityOf(k);
                        bool egress = false;
                        if (town != null)
                            foreach (UoRegions.Shop s in UoRegions.AllShopsIn(town))
                                if (s.Holds(x, y, 0) && !s.Holds(tx, ty, 0))
                                {
                                    egress = true;
                                    break;
                                }
                        if (!egress)
                            return "the way in through " + dn + " is crowded after " + HeldDoorWaitMs / 1000 + " s; "
                                + tx + "," + ty + " given up";
                        // A rune for this town only: the bank rune fallback was Minoc's, and the walk to Britain's bank
                        // then planned from Minoc, a minute a plan (2026-10-18 00:25:05).
                        uint rune = PackTownRune(k, town!);
                        if (rune == 0)
                            return "stuck behind " + dn + ", crowded after " + HeldDoorWaitMs / 1000
                                + " s, and no rune for " + town + " in the pack";
                        Note("Walk: stuck behind " + dn + ", crowded after " + HeldDoorWaitMs / 1000
                            + " s; recalling out");
                        string? rw = Recall(rune);
                        if (rw != null)
                            return "stuck behind " + dn + "; recalling out: " + rw;
                        if (CityOf(k) != town)
                            return "stuck behind " + dn + "; the rune for " + town + " landed in "
                                + (CityOf(k) ?? "no town");
                        lock (k.State.Gate)
                        {
                            x = k.State.X;
                            y = k.State.Y;
                            z = k.State.Z;
                        }
                        blocked = blocked.Clear();
                        acksSeen = k.StepAcks;
                        refusalsSeen = k.StepRefusals;
                        path = null;
                        continue;
                    }
                    if ((path == null || path.Count == 0) && teleport && hops < TeleportHops)
                    {
                        if (flight.Count > 0)
                        {
                            Clock.Sleep(5);
                            continue;
                        }
                        Hop? hop = TeleportHop(k, x, y, z, tx, ty, tz, reach, sight, blocked);
                        if (hop != null)
                        {
                            hops++;
                            Note("Walk: no walk reaches " + tx + "," + ty + " z " + tz + "; teleporting "
                                + Pair(hop.Cast, hop.Land));
                            string? cw = hop.Walk.Count == 0 ? null
                                : WalkTo(StateX(hop.Cast), StateY(hop.Cast), 0, false, StateZ(hop.Cast), false);
                            string? tw = cw ?? Teleport(k, hop);
                            if (tw != null)
                                Note("Walk: " + (cw != null ? "walking to the cast tile: " : "") + tw);
                            if (cw == null && tw != null)
                                return "no way from " + x + "," + y + " z " + z + " to " + tx + "," + ty + ": " + tw;
                            acksSeen = k.StepAcks;
                            refusalsSeen = k.StepRefusals;
                            path = null;
                            continue;
                        }
                    }
                    if ((path == null || path.Count == 0) && (s_planHalted || circuitStop))
                        return "stopped";
                    if (path == null || path.Count == 0)
                        return "no way from " + x + "," + y + " z " + z + " to within " + reach + " of " + tx + ","
                            + ty + (tz != AnyFloor ? " z " + tz : "");
                }
                // A shut door on the next tile is opened before the step onto it, from where the player stands, every
                // step in flight answered first; a door the walk failed to open closes the tile, and the route is
                // planned again. A door opened ahead is still open to the refusal's own use, should its art have been
                // misread.
                AssistItem? shut = DoorOn(k, x + s_dX[path[0]], y + s_dY[path[0]],
                    d => (!opened.Contains(d.Serial) && !ahead.Contains(d.Serial) || reshut(d))
                    && DoorShut(d.Graphic) == true);
                if (shut != null)
                {
                    if (flight.Count > 0)
                    {
                        Clock.Sleep(5);
                        continue;
                    }
                    ahead = ahead.Add(shut.Serial);
                    if (UseDoor(k, shut, ((long)(x + s_dX[path[0]]) << 16) | (uint)(y + s_dY[path[0]]), ref blocked,
                        heldDoors) != DoorUse.Opened)
                    {
                        path = null;
                        continue;
                    }
                    Swung(k, ref swungOpen, shut.Serial);
                }
                int pick = path[0];
                bool turn = f != pick;
                long sentAt = Clock.UtcNow.Ticks;
                bool running = RunNow(k);
                run = running ? RunFlag : 0;
                pace = running ? runPace : runPace == RunMountMs ? WalkMountMs : StepMs;
                string? why;
                using (Act(k))
                    why = k.Step(pick | run);
                if (why != null)
                    return "stepping: " + why;
                // The assist's own steps tire the mount as the player's do; the estimate sees only the client's
                // otherwise.
                MountStep(k, new byte[] { 0x02, (byte)(pick | run), 0 });
                flight.Enqueue(new[] { x, y, pick, turn ? 1 : 0, (int)(sentAt >> 32), (int)sentAt });
                nextAt = Clock.UtcNow.AddMilliseconds(pace);
                // A step in a new direction only turns the player; a move puts it on the next tile at that tile's
                // height.
                if (turn)
                {
                    f = pick;
                }
                else
                {
                    int nz = UoMap.StepZ(x, y, z, x + s_dX[pick], y + s_dY[pick]);
                    x += s_dX[pick];
                    y += s_dY[pick];
                    if (nz != int.MinValue)
                        z = nz;
                    path.RemoveAt(0);
                    followed++;
                    steps++;
                    if ((steps & 7) == 0)
                        NoteLooms(k);
                }
            }
            return circuitStop ? "stopped" : "the connection closed";
        }
        finally { if (!m_clientWalks) k.ShowPlayer(true); }
    }
    // A door's state by its art: TILEDATA flags a shut leaf and an open one alike (the wooden 0x06A5/0x06A6, metal
    // 0x0675/0x0676 and barred 0x0685/0x0686 pairs, the 1.25 client, 2026-10-23), but the client's door arts run in
    // sets of DoorSetSize from these bases (0x190E a pair), shut at an even step past the base and open one past it
    // (uo1998's door 40001F3C: 0x06A5 shut, 0x06A6 open, 2026-10-23 17:50; AssistTesting.md T3).
    static readonly ImmutableArray<int> s_doorSets = [ 0x00E8, 0x0314, 0x0324, 0x0334, 0x0344, 0x0354, 0x0675, 0x0685,
        0x0695,
        0x06A5, 0x06B5, 0x06C5, 0x06D5, 0x06E5, 0x0824, 0x0839, 0x084C, 0x0866, 0x1FED ];
    public const int DoorSetSize = 16, DoorPair = 0x190E;
    // True shut, false open, null for an art in no set.
    public static bool? DoorShut(int graphic)
    {
        if (graphic == DoorPair || graphic == DoorPair + 1)
            return graphic == DoorPair;
        foreach (int b in s_doorSets)
            if (graphic >= b && graphic < b + DoorSetSize)
                return ((graphic - b) & 1) == 0;
        return null;
    }
    // Only a door on the walker's own floor (Damian, 2026-10-18: "z comparison when trying to use a door or other
    // object and you get "can't reach that" problems"): the ground-floor door at 1378,3751 was used from the balcony
    // above, 20 up, and could not open (21:26:15). The caller holds the state's gate.
    static bool OnWalkerFloor(GameLink k, AssistItem door)
    {
        return Math.Abs(door.Z - k.State.Z) < UoMap.PersonHeight;
    }
    // A door on the tile x, y on the walker's floor that which accepts; null when none.
    AssistItem? DoorOn(GameLink k, int x, int y, Func<AssistItem, bool> which)
    {
        lock (k.State.Gate)
            foreach (AssistItem g in k.State.Ground.Values)
                if (g.X == x && g.Y == y && IsDoor(g.Graphic) && OnWalkerFloor(k, g) && which(g))
                    return g;
        return null;
    }
    enum DoorUse
    {
        Opened, NotOpened, Held, Locked
    }
    public const int ReopenMax = 3;
    // The walk's use of the door counted, and the door as it stands after it: its art and tile.
    static void Swung(GameLink k, ref Map<uint, int[]> swungOpen, uint door)
    {
        swungOpen.TryGet(door, out int[]? was);
        AssistItem? now;
        k.State.Ground.TryGet(door, out now);
        swungOpen = swungOpen.Set(door, new[] { (was == null ? 0 : was[0]) + 1, now == null ? -1 : now.Graphic,
            now == null ? 0 : now.X, now == null ? 0 : now.Y });
    }
    // The door a walk needs, used before the step onto its tile or after a step at tile was refused. Every answer but
    // Opened closes tile to the walk.
    DoorUse UseDoor(GameLink k, AssistItem door, long tile, ref Set<long> blocked,
        List<Tuple<AssistItem, long>> heldDoors)
    {
        // The locked house's door is opened only by HomeEnter and HomeLeave, never by a walk passing it (2026-10-20
        // 20:43:47: a walk to the house floor opened it from inside and went out with the key).
        if (LockedHouseDoor(k, door.X, door.Y))
        {
            Note("Walk: the house door opens only by the door routine; going round");
            blocked = blocked.Add(tile);
            return DoorUse.Locked;
        }
        // A person beside the door holds it as it stands (open, most likely), and a use only tries to close it: the
        // door is left alone and the walk goes round (Damian, 2026-10-18: "an npc is blocking a door from closing",
        // "should just ignore the doorclose and repath around it"; four uses in 19 s at Serpent's Hold, 3011,3464). The
        // player's own house vendor never steps away and holds no door (Damian, 2026-10-20: "rex is my vendor"; a 5 s
        // wait at his house door, then the forge given up).
        // A shut door opens whoever stands beside it: only one read open is held (Damian, 2026-10-24: Tracy teleported
        // home behind the shut door of The Right Fit's other room, "the circuit stops at the door", "the circuit should
        // instead open the door").
        bool held = false;
        if (DoorShut(door.Graphic) != true)
            lock (k.State.Gate)
                foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
                    if (kv.Key != k.State.Player && !OwnVendor(kv.Key)
                        && Tiles(kv.Value[0], kv.Value[1], door.X, door.Y) <= 1)
                    {
                        held = true;
                        break;
                    }
        if (held)
        {
            Note("Walk: someone stands at the " + UoTiles.Name(door.Graphic) + " at " + door.X + "," + door.Y
                + "; going round it");
            blocked = blocked.Add(tile);
            heldDoors.Add(Tuple.Create(door, tile));
            return DoorUse.Held;
        }
        // Opened, and seen open: the door's art or tile changes when it swings. Used again only when the server
        // answered "You must wait", after DoorBackoffMs, doubling, DoorTries in all (Damian, 2026-10-18: "pathfinding
        // through a closed doorway should open it, check its open, if not open repeat with backoff delay iteration"); a
        // use with no swing and no such answer is not repeated (L-FUTILITY).
        bool swung = false;
        int g0 = door.Graphic, x0d = door.X, y0d = door.Y;
        for (int dt = 0; dt < DoorTries && !swung && !circuitStop; dt++)
        {
            if (dt > 0)
                Clock.Sleep(DoorBackoffMs << (dt - 1));
            BusyWait(clickedAt);
            Heard dmark;
            using (Act(k))
            {
                dmark = new(k.State);
                k.Use(door.Serial, false);
                clickedAt = Clock.UtcNow;
            }
            swung = Wait.Until(Clock,
                () => { AssistItem? now; k.State.Ground.TryGet(door.Serial,
                out now); return now != null && (now.Graphic != g0 || now.X != x0d || now.Y != y0d); }, DoorOpenMs,
                "the door's swing", stepMs: 50).Done;
            if (!swung
                && !dmark.Answers().Exists(j =>
                s_waitLines.Exists(wl => j.StartsWith(wl, StringComparison.OrdinalIgnoreCase))))
                break;
        }
        Note("Walk: " + (swung ? "opened" : "could not open") + " the " + UoTiles.Name(door.Graphic) + " at " + door.X
            + "," + door.Y + (swung ? "" : "; going round it"));
        if (!swung)
        {
            blocked = blocked.Add(tile);
            return DoorUse.NotOpened;
        }
        Clock.Sleep(StepMs);
        return DoorUse.Opened;
    }

    // Away from every threat in view, running, chosen again on every step (Damian, 2026-10-17: "pick a
    // direction away or at least right angle, and run", "zig zag your way out", "paying attention, every second"): of
    // the open tiles straight away, 45 and 90 degrees off it (all eight when those are closed), the one farthest from
    // the nearest threat, until none is within FleeClear, FleeSteps are run, or the player steps. Orc 0x07 0x11 0x8A,
    // lizardman 0x21 0x23 0x24.
    static readonly ImmutableArray<int> s_threatBodies = [ 0x07, 0x11, 0x8A, 0x21, 0x23, 0x24 ];
    public static ImmutableArray<int> ThreatBodies => s_threatBodies;
    public const int FleeClear = 14, FleeSteps = 150;
    // A threat by its kind, not its region (Damian, 2026-10-23: "skeletons, liches, shades, etc. also scorpions, giant
    // spiders, and the whole lot of npcs that attack you"): a ThreatBodies body, or on uo1998 a notoriety of ThreatNoto
    // or below (OUO NotoValueToLevel's level -5). Every 1998 template that attacks carries notoriety -125 (rundir
    // bank/templatestable.dat: liche, skeleton, ghost, giant spider, giant scorpion, ettin, troll, harpy, evil mage)
    // and none that does not (rabbit, horse, cougar, grey wolf); the live orc read 0x81, -127. Never
    // the player's own pet. The caller holds the state's gate.
    public const int ThreatNoto = -120;
    bool Threat(GameLink k, uint s, ICollection<uint> mine)
    {
        if (s == k.State.Player || mine.Contains(s))
            return false;
        int b;
        if (k.State.Bodies.TryGet(s, out b) && s_threatBodies.IndexOf(b) >= 0)
            return true;
        int n;
        return RawNotoriety && k.State.Notorieties.TryGet(s, out n) && (sbyte)n <= ThreatNoto;
    }
    List<uint> PetSerials()
    {
        lock (m_pets)
        {
            PetOf(0);
            return m_pets.ConvertAll(p => p.Serial);
        }
    }
    // Every threat in view: x, y, distance.
    List<int[]> Threats(GameLink k)
    {
        List<int[]> all = new();
        List<uint> mine = PetSerials();
        lock (k.State.Gate)
        {
            foreach (KeyValuePair<uint, int[]> kv in k.State.Mobiles)
            {
                if (!Threat(k, kv.Key, mine))
                    continue;
                all.Add(new[] { kv.Value[0], kv.Value[1], Tiles(kv.Value[0], kv.Value[1], k.State.X, k.State.Y) });
            }
        }

        return all;
    }
    static int Nearest(List<int[]> all)
    {
        int d = int.MaxValue;
        foreach (int[] t in all)
            d = Math.Min(d, t[2]);
        return d;
    }
    public string? Flee()
    {
        GameLink k = m_link;
        if (k == null || k.Closed)
            return "no game connection";
        ManualStart();
        int playerAcks = k.ClientStepAcks, playerRefusals = k.ClientStepRefusals, pace = StepPace(k);
        Set<long> refused = Set<long>.Empty;
        try
        {
            for (int step = 0; step < FleeSteps; step++)
            {
                if (circuitStop)
                    return "stopped";
                if (k.Closed)
                    return "the connection closed";
                if (k.ClientStepAcks != playerAcks || k.ClientStepRefusals != playerRefusals)
                {
                    CircuitHalt(TookOver);
                    return TookOver;
                }
                List<int[]> all = Threats(k);
                if (all.Count == 0 || Nearest(all) > FleeClear)
                {
                    Note("Flee: clear after " + step + " steps");
                    return null;
                }
                int px, py, pz, f;
                lock (k.State.Gate)
                {
                    px = k.State.X;
                    py = k.State.Y;
                    pz = k.State.Z;
                    f = k.State.Facing;
                }
                // Away from all of them, the nearer weighing more (Damian: "because you just might run into another").
                double vx = 0, vy = 0;
                foreach (int[] t in all)
                {
                    double dd = Math.Max(1, t[2]);
                    vx += (px - t[0]) / (dd * dd);
                    vy += (py - t[1]) / (dd * dd);
                }
                int away = DirTo((int)Math.Round(vx * 1000), (int)Math.Round(vy * 1000));
                ValueMap<long, int> seen = ValueMap<long, int>.Empty, tops = ValueMap<long, int>.Empty;
                int best = -1, bestScore = int.MinValue;
                // Boxed in, past them (Damian, 2026-10-17: "boxed in requires run past them"): 135 and 180 degrees only
                // when no tile from away to 90 degrees off is open.
                foreach (int turn in new[] { 0, 1, 7, 2, 6, -1, 3, 5, 4 })
                {
                    if (turn < 0)
                    {
                        if (best >= 0)
                            break;
                        continue;
                    }
                    int d = (away + turn) & 7, nx = px + s_dX[d], ny = py + s_dY[d];
                    if (refused.Contains(((long)nx << 16) | (uint)ny)
                        || Open(k, px, py, nx, ny, pz, ref seen, false, ref tops) == int.MinValue)
                        continue;
                    if (s_dX[d] != 0 && s_dY[d] != 0
                        && (Open(k, px, py, px + s_dX[d], py, pz, ref seen, false, ref tops) == int.MinValue
                        || Open(k, px, py, px, py + s_dY[d], pz, ref seen, false, ref tops) == int.MinValue))
                        continue;
                    int m = int.MaxValue;
                    foreach (int[] t in all)
                        m = Math.Min(m, Tiles(nx, ny, t[0], t[1]));
                    // Farther from the nearest threat first; of equals, no turn (a turn is a step that does not move).
                    int score = m * 4 - (d != f ? 1 : 0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = d;
                    }
                }
                // Never a stand in the corner (Damian, 2026-10-17: "sitting in the corner waiting to die is no
                // solution"): the refusals forgotten, the tiles read again.
                if (best < 0)
                {
                    refused = refused.Clear();
                    Clock.Sleep(200);
                    continue;
                }
                int r;
                using (Act(k))
                    r = StepOnce(k, best | RunFlag);
                if (r == 0)
                {
                    refused = refused.Add(((long)(px + s_dX[best]) << 16) | (uint)(py + s_dY[best]));
                    Clock.Sleep(StepSettleMs);
                    continue;
                }
                if (r < 0)
                    return "the server did not answer a step";
                Clock.Sleep(pace);
            }
            List<int[]> left = Threats(k);
            return left.Count == 0 || Nearest(left) > FleeClear ? null
                : "still within " + Nearest(left) + " tiles after " + FleeSteps + " steps";
        }
        finally { if (!m_clientWalks) k.ShowPlayer(true); }
    }
}
