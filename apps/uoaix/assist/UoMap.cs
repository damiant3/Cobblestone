#nullable enable
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist;

// What is at a map tile, from the client's own MAP0, STAIDX0, STATICS0, TILEDATA, MULTI files (6144 by 4096 map,
// 8 by 8 blocks numbered x-block * 512 + y-block). MAP0: per block a 4-byte header and 64 cells of (land id u16, z s8),
// row by row. STAIDX0: per block (offset, length, extra); STATICS0: 7-byte entries (graphic u16, x u8, y u8, z s8,
// hue). TILEDATA land entries: 512 blocks of a 4-byte header and 32 of (flags u32, texture u16, name 20). MULTI.IDX per
// multi (offset, length, extra); MULTI.MUL 12-byte components (item u16, x s16, y s16, z s16, flags u32) around the
// center.
public static class UoMap
{
    const int BlocksTall = 512;
    static string F(string n)
    {
        return Path.Combine(UoArt.Folder!, n);
    }
    static byte[]? Read(string file, long at, int len)
    {
        using (FileStream fs = File.OpenRead(F(file)))
        {
            if (at < 0 || at + len > fs.Length)
                return null;
            byte[] b = new byte[len];
            fs.Position = at;
            int got = 0;
            while (got < len)
            {
                int n = fs.Read(b, got, len - got);
                if (n <= 0)
                    return null;
                got += n;
            }
            return b;
        }
    }
    static string LandName(int id)
    {
        try
        {
            byte[]? b = Read("TILEDATA.MUL", (id / 32) * (4 + 32 * 26) + 4 + (id % 32) * 26 + 6, 20);
            if (b == null)
                return id.ToString("X4");
            int n = 0;
            while (n < 20 && b[n] != 0)
                n++;
            string s = Encoding.ASCII.GetString(b, 0, n).Trim();
            return s.Length > 0 ? s : id.ToString("X4");
        }
        catch { return id.ToString("X4"); }
    }
    // "ground (z), static (z), ..." for the tile, or null when the files are not readable.
    public static string? Tile(int x, int y)
    {
        if (UoArt.Folder == null || x < 0 || y < 0)
            return null;
        try
        {
            long block = (long)(x >> 3) * BlocksTall + (y >> 3);
            byte[]? cell = Read("MAP0.MUL", block * 196 + 4 + ((y & 7) * 8 + (x & 7)) * 3, 3);
            if (cell == null)
                return null;
            List<string> parts = new()
            { "ground " + LandName(BitConverter.ToUInt16(cell, 0)) + " (z " + (sbyte)cell[2] + ")" };
            byte[]? idx = Read("STAIDX0.MUL", block * 12, 12);
            if (idx != null)
            {
                int off = BitConverter.ToInt32(idx, 0), len = BitConverter.ToInt32(idx, 4);
                if (off >= 0 && len > 0 && len < 1 << 20)
                {
                    byte[]? st = Read("STATICS0.MUL", off, len);
                    if (st != null)
                    {
                        for (int i = 0; i + 7 <= st.Length; i += 7)
                            if (st[i + 2] == (x & 7) && st[i + 3] == (y & 7))
                                parts.Add(UoTiles.Name(BitConverter.ToUInt16(st, i)) + " (z " + (sbyte)st[i + 4] + ")");
                    }
                }
            }
            return string.Join(", ", parts);
        }
        catch { return null; }
    }
    // Everything read from the client's files forgotten, for a folder changed under it (the suites' TestMap).
    public static void Reset()
    {
        lock (s_staticTilesGate)
            s_staticTiles = s_staticTiles.Clear();
        lock (s_mapBlocksGate)
            s_mapBlocks = s_mapBlocks.Clear();
        s_landFlags = null;
        s_verMulti = null;
        lock (s_multiPartsGate)
            s_multiParts = s_multiParts.Clear();
        lock (s_multiBoxGate)
            s_multiBox = s_multiBox.Clear();
        lock (s_multisGate)
            s_multiIndexSeq = -1;
    }
    // A tile's statics, graphic and height each, from blocks read once, split by tile and kept (at most BlocksKept,
    // then read again): a plan reads every tile of its box, and scanning a block's whole statics for each of its 64
    // tiles was a planner's largest cost (2026-10-22).
    const int BlocksKept = 32768;
    static Map<long, List<int[]>[]> s_staticTiles = Map<long, List<int[]>[]>.Empty;
    static readonly Lock s_staticTilesGate = new();
    static readonly List<int[]> s_noStatics = new();
    public static List<int[]> StaticsAt(int x, int y)
    {
        if (UoArt.Folder == null || x < 0 || y < 0)
            return new List<int[]>();
        long block = (long)(x >> 3) * BlocksTall + (y >> 3);
        List<int[]>[]? tiles;
        lock (s_staticTilesGate)
        {
            if (!s_staticTiles.TryGet(block, out tiles))
            {
                byte[] st = new byte[0];
                try
                {
                    byte[]? idx = Read("STAIDX0.MUL", block * 12, 12);
                    if (idx != null)
                    {
                        int off = BitConverter.ToInt32(idx, 0), len = BitConverter.ToInt32(idx, 4);
                        if (off >= 0 && len > 0 && len < 1 << 20)
                            st = Read("STATICS0.MUL", off, len) ?? new byte[0];
                    }
                }
                catch { }
                tiles = new List<int[]>[64];
                for (int i = 0; i + 7 <= st.Length; i += 7)
                {
                    int c = st[i + 3] * 8 + st[i + 2];
                    if (st[i + 2] > 7 || st[i + 3] > 7)
                        continue;
                    if (tiles[c] == null)
                        tiles[c] = new List<int[]>();
                    tiles[c].Add(new[] { (int)BitConverter.ToUInt16(st, i), (sbyte)st[i + 4] });
                }
                if (s_staticTiles.Count > BlocksKept)
                    s_staticTiles = s_staticTiles.Clear();
                s_staticTiles = s_staticTiles.Set(block, tiles);
            }
        }
        List<int[]> here = tiles[(y & 7) * 8 + (x & 7)] ?? s_noStatics;
        List<int[]> l = new(here.Count);
        l.AddRange(here);
        l.AddRange(MultiAt(x, y));
        return l;
    }
    // The multis the server sent by the map blocks their footprints cover, built again whenever MultiSeq moves: a tile
    // asks only the multis over its own block, not every multi in view (up to 2000) for every tile of a plan.
    static Map<long, List<int[]>> s_multiByBlock = Map<long, List<int[]>>.Empty; static int s_multiIndexSeq = -1;
    static void IndexMultis()
    {
        s_multiByBlock = s_multiByBlock.Clear();
        foreach (int[] m in s_multis.Values)
        {
            int[]? box = MultiBox(m[0]);
            if (box == null)
                continue;
            for (int bx = (m[1] + box[0]) >> 3; bx <= (m[1] + box[2]) >> 3; bx++)
            {
                for (int by = (m[2] + box[1]) >> 3; by <= (m[2] + box[3]) >> 3; by++)
                {
                    long key = ((long)bx << 32) | (uint)by;
                    List<int[]>? l;
                    if (!s_multiByBlock.TryGet(key, out l))
                        s_multiByBlock = s_multiByBlock.Set(key, l = new List<int[]>());
                    l.Add(m);
                }
            }
        }
        s_multiIndexSeq = s_multiSeq;
    }
    // The parts of every multi the server sent standing on a tile, graphic and height each.
    public static List<int[]> MultiAt(int x, int y)
    {
        List<int[]> l = new();
        if (UoArt.Folder == null)
            return l;
        lock (s_multisGate)
        {
            if (s_multiIndexSeq != s_multiSeq)
                IndexMultis();
            List<int[]>? near;
            if (!s_multiByBlock.TryGet(((long)(x >> 3) << 32) | (uint)(y >> 3), out near))
                return l;
            foreach (int[] m in near)
            {
                int[]? box = MultiBox(m[0]);
                if (box == null || x < m[1] + box[0] || x > m[1] + box[2] || y < m[2] + box[1] || y > m[2] + box[3])
                    continue;
                Map<long, List<int[]>>? parts = MultiParts(m[0]);
                List<int[]>? here;
                if (parts != null
                    && parts.TryGet(((long)(x - m[1]) << 16) | (uint)((y - m[2]) & 0xFFFF), out here))
                    foreach (int[] c in here)
                        l.Add(new[] { c[0], m[3] + c[1] });
            }
        }
        return l;
    }
    // The doors of the house holding the tile where the multi places them shut (MULTI.MUL parts of flags 0, which the
    // server makes items of, named a door): x, y, z and graphic each; empty when no house holds the tile.
    public static List<int[]> HouseDoors(int x, int y)
    {
        List<int[]> l = new();
        int[]? hm = null;
        lock (s_multisGate)
        {
            foreach (int[] m in s_multis.Values)
            {
                if (m[0] < 0x40)
                    continue;
                int[]? box = MultiBox(m[0]);
                if (box != null && x >= m[1] + box[0] && x <= m[1] + box[2] && y >= m[2] + box[1] && y <= m[2] + box[3])
                {
                    hm = m;
                    break;
                }
            }
        }

        if (hm == null)
            return l;
        try
        {
            byte[]? b = MultiBytes(hm[0]);
            if (b == null)
                return l;
            for (int i = 0; i + 12 <= b.Length; i += 12)
            {
                if (BitConverter.ToUInt32(b, i + 8) != 0)
                    continue;
                int g = BitConverter.ToUInt16(b, i);
                if (AssistFeatures.IsDoor(g))
                    l.Add(new[] { hm[1] + BitConverter.ToInt16(b, i + 2), hm[2] + BitConverter.ToInt16(b, i + 4),
                        hm[3] + BitConverter.ToInt16(b, i + 6), g });
            }
        }
        catch { }
        return l;
    }
    // The houses (multis of id 0x40 or more) whose footprint meets the box: min x, min y, max x, max y each. MultiSeq
    // moves whenever a multi comes or goes, so a map drawn before it is drawn again.
    static int s_multiSeq;
    public static int MultiSeq { get => s_multiSeq; set => s_multiSeq = value; }
    public static List<int[]> HousesIn(int x0, int y0, int x1, int y1)
    {
        List<int[]> l = new();
        lock (s_multisGate)
        {
            foreach (int[] m in s_multis.Values)
            {
                if (m[0] < 0x40)
                    continue;
                int[]? box = MultiBox(m[0]);
                if (box == null)
                    continue;
                int a = m[1] + box[0], b = m[2] + box[1], c = m[1] + box[2], d = m[2] + box[3];
                if (c >= x0 && a <= x1 && d >= y0 && b <= y1)
                    l.Add(new[] { a, b, c, d });
            }
        }

        return l;
    }
    // Houses (Damian, 2026-10-18: "it doesn't navigate the player house here ... it doesn't recognize the dynamically
    // placed walls"): a multi's components stand on its tiles as statics do, its walls, floors and stairs being in
    // MULTI.MUL and not in the map's statics. Each multi the server sends (0x1A, graphic 0x4000 | id) is kept by
    // serial, with its id, x, y and z, until it is deleted (0x1D).
    static Map<uint, int[]> s_multis = Map<uint, int[]>.Empty; static readonly Lock s_multisGate = new();
    public static void PutMulti(uint serial, int id, int x, int y, int z)
    {
        lock (s_multisGate)
        {
            if (s_multis.Count > 2000)
                s_multis = s_multis.Clear();
            int[]? was;
            if (!s_multis.TryGet(serial, out was) || was[0] != id || was[1] != x || was[2] != y || was[3] != z)
                s_multiSeq++;
            s_multis = s_multis.Set(serial, new[] { id, x, y, z });
        }
    }
    public static void DropMulti(uint serial)
    {
        lock (s_multisGate)
            if (s_multis.ContainsKey(serial))
            {
                s_multis = s_multis.Remove(serial);
                s_multiSeq++;
            }
    }
    public static bool HasMulti(uint serial)
    {
        return s_multis.ContainsKey(serial);
    }
    // The house (a multi of id 0x40 or more; below are boats) whose footprint holds the tile: min x, min y, max x, max
    // y and its z; null when none.
    public static int[]? HouseAround(int x, int y)
    {
        lock (s_multisGate)
        {
            foreach (int[] m in s_multis.Values)
            {
                if (m[0] < 0x40)
                    continue;
                int[]? box = MultiBox(m[0]);
                if (box != null && x >= m[1] + box[0] && x <= m[1] + box[2] && y >= m[2] + box[1] && y <= m[2] + box[3])
                    return new[] { m[1] + box[0], m[2] + box[1], m[1] + box[2], m[2] + box[3], m[3] };
            }
        }

        return null;
    }
    // A multi's components by offset from its center ((dx << 16) | dy): graphic and dz each (MULTI.MUL 12-byte entries:
    // item u16, x s16, y s16, z s16, flags u32); null when unreadable.
    static Map<int, Map<long, List<int[]>>> s_multiParts = Map<int, Map<long, List<int[]>>>.Empty;
    static readonly Lock s_multiPartsGate = new();
    static Map<long, List<int[]>>? MultiParts(int id)
    {
        // Unread, not unreadable, until the client's folder is known: a null kept from a read before it lost a house's
        // walls from every map and walk until the relay restarted (2026-10-24, multi 0x6C drawn as its door alone).
        if (UoArt.Folder == null)
            return null;
        Map<long, List<int[]>>? d;
        lock (s_multiPartsGate)
            if (s_multiParts.TryGet(id, out d))
                return d;
        d = null;
        try
        {
            byte[]? m = MultiBytes(id);
            if (m != null)
            {
                d = Map<long, List<int[]>>.Empty;
                for (int i = 0; i + 12 <= m.Length; i += 12)
                {
                    // Flags 0 is a part the server places as its own item (a house's door, sign and signpost: 3 of
                    // multi 0x6c's 147), which a door opens; as a static it closed the doorway to every walk.
                    if (BitConverter.ToUInt32(m, i + 8) == 0)
                        continue;
                    int g = BitConverter.ToUInt16(m, i), dx = BitConverter.ToInt16(m,
                        i + 2), dy = BitConverter.ToInt16(m, i + 4), dz = BitConverter.ToInt16(m, i + 6);
                    long key = ((long)dx << 16) | (uint)(dy & 0xFFFF);
                    List<int[]>? l;
                    if (!d.TryGet(key, out l))
                        d = d.Set(key, l = new List<int[]>());
                    l.Add(new[] { g, dz });
                }
            }
        }
        catch { d = null; }
        lock (s_multiPartsGate)
            s_multiParts = s_multiParts.Set(id, d!);
        return d;
    }
    // A multi's MULTI.MUL entries (12 bytes each), from VERDATA.MUL's patch when it holds one, else MULTI.IDX and
    // MULTI.MUL; null when unreadable. VERDATA.MUL: a count, then 20-byte entries of file, block, offset, length,
    // extra; file 14 is the multi data. uo1998's client patches the houses 0x8C and 0x8D there, and MULTI.IDX has no
    // entry for either.
    static Map<int, int[]>? s_verMulti;
    static byte[]? MultiBytes(int id)
    {
        if (UoArt.Folder == null)
            return null;
        Map<int, int[]>? vm = s_verMulti;
        if (vm == null)
        {
            vm = Map<int, int[]>.Empty;
            try
            {
                byte[]? head = Read("VERDATA.MUL", 0, 4);
                int n = head == null ? 0 : BitConverter.ToInt32(head, 0);
                byte[]? e = n > 0 && n < 1 << 16 ? Read("VERDATA.MUL", 4, n * 20) : null;
                if (e != null)
                    for (int i = 0; i < n; i++)
                        if (BitConverter.ToInt32(e, i * 20) == 14)
                            vm = vm.Set(BitConverter.ToInt32(e, i * 20 + 4), new[] { BitConverter.ToInt32(e, i * 20 + 8),
                                BitConverter.ToInt32(e, i * 20 + 12) });
            }
            catch { }
            s_verMulti = vm;
        }
        int[]? v;
        if (vm.TryGet(id, out v))
            return v[0] >= 0 && v[1] >= 12 && v[1] < 1 << 20 ? Read("VERDATA.MUL", v[0], v[1]) : null;
        byte[]? idx = Read("MULTI.IDX", (long)id * 12, 12);
        if (idx == null)
            return null;
        int off = BitConverter.ToInt32(idx, 0), len = BitConverter.ToInt32(idx, 4);
        return off >= 0 && len >= 12 && len < 1 << 20 ? Read("MULTI.MUL", off, len) : null;
    }
    // Whether a tile's land is impassable (water, rock: TILEDATA land flags 0x40), from MAP0's land id; false when
    // unread.
    static uint[]? s_landFlags;
    public static bool LandBlocks(int x, int y)
    {
        if (UoArt.Folder == null || x < 0 || y < 0)
            return false;
        try
        {
            if (s_landFlags == null)
            {
                byte[] td = File.ReadAllBytes(F("TILEDATA.MUL"));
                uint[] f = new uint[0x4000];
                for (int id = 0; id < 0x4000; id++)
                {
                    int at = (id / 32) * (4 + 32 * 26) + 4 + (id % 32) * 26;
                    if (at + 4 > td.Length)
                        break;
                    f[id] = BitConverter.ToUInt32(td, at);
                }
                s_landFlags = f;
            }
            long block = (long)(x >> 3) * BlocksTall + (y >> 3);
            byte[]? b = MapBlock(block);
            if (b == null)
                return false;
            int cell = 4 + ((y & 7) * 8 + (x & 7)) * 3;
            return (s_landFlags[BitConverter.ToUInt16(b, cell) & 0x3FFF] & 0x40) != 0;
        }
        catch { return false; }
    }
    // MAP0 blocks (a 4-byte header, then 64 cells of land id u16 and z s8), read once and kept (at most 4096).
    static Map<long, byte[]> s_mapBlocks = Map<long, byte[]>.Empty; static readonly Lock s_mapBlocksGate = new();
    static byte[]? MapBlock(long block)
    {
        byte[]? b;
        lock (s_mapBlocksGate)
        {
            if (!s_mapBlocks.TryGet(block, out b))
            {
                b = Read("MAP0.MUL", block * 196, 196);
                if (s_mapBlocks.Count > BlocksKept)
                    s_mapBlocks = s_mapBlocks.Clear();
                s_mapBlocks = s_mapBlocks.Set(block, b!);
            }
        }
        return b;
    }
    // The land height at a tile, int.MinValue when the map is not readable.
    // From the kept MAP0 blocks: a file read per call made each route plan take up to 1.3 s (the standing height reads
    // 4 corners for every tile of the plan's box), and the walk stalled that long every 8 steps (2026-10-17).
    public static int LandZ(int x, int y)
    {
        if (UoArt.Folder == null || x < 0 || y < 0)
            return int.MinValue;
        try
        {
            long block = (long)(x >> 3) * BlocksTall + (y >> 3);
            byte[]? b = MapBlock(block);
            return b == null ? int.MinValue : (sbyte)b[4 + ((y & 7) * 8 + (x & 7)) * 3 + 2];
        }
        catch { return int.MinValue; }
    }
    // The land id at a tile (MAP0's cell), -1 when the map is not readable.
    public static int LandIdAt(int x, int y)
    {
        if (UoArt.Folder == null || x < 0 || y < 0)
            return -1;
        try
        {
            long block = (long)(x >> 3) * BlocksTall + (y >> 3);
            byte[]? b = MapBlock(block);
            return b == null ? -1 : BitConverter.ToUInt16(b, 4 + ((y & 7) * 8 + (x & 7)) * 3) & 0x3FFF;
        }
        catch { return -1; }
    }
    // The height a walker at fromZ stands at on a tile: the highest floor (passable land, or a static with the 1.25
    // TILEDATA surface flag 0x200 that is not impassable 0x40, its top half height when a bridge 0x400, as stairs are)
    // no more than StepUp above fromZ with PersonHeight clear above it; int.MinValue when there is none or the map is
    // not readable. A step only updates x and y on the wire (0x22), so the walker's z comes from here.
    // Land is stood on at the average of its corners (RunUO's GetAverageZ: the mean of the flatter diagonal), and a
    // slope climbs up to LandStepUp a tile; uo1998's Britain rises 0 to 20 east of x 1488 with cells 10 apart.
    public const int StepUp = 5, LandStepUp = 10, PersonHeight = 16;
    public static int LandStandZ(int x, int y)
    {
        int top = LandZ(x, y), left = LandZ(x, y + 1), right = LandZ(x + 1, y), bottom = LandZ(x + 1, y + 1);
        if (top == int.MinValue || left == int.MinValue || right == int.MinValue || bottom == int.MinValue)
            return top;
        return Math.Abs(top - bottom) > Math.Abs(left - right) ? (int)Math.Floor((left + right) / 2.0)
            : (int)Math.Floor((top + bottom) / 2.0);
    }
    // Void land (OUO Terrain_IsVoidTile: ids 2, 0x1AE to 0x1B5, 0x1DB) is no surface at all to the 1998 server, so a
    // cave floor over it is stood on at the floor (the cave mouth at 2471..2473,67: drawn at z 10 and 12 on the void
    // land's corners, 2026-10-20).
    public static bool LandVoid(int x, int y)
    {
        int id = LandIdAt(x, y);
        return id == 2 || (id >= 0x1AE && id <= 0x1B5) || id == 0x1DB;
    }
    // Impassable land is a block from its lowest corner to its average (OUO Terrain_BuildSurfaceList), and a floor is
    // stood on only with PersonHeight clear between it and the next block above (CanWalk): a cave floor at z 0 under
    // rock whose lowest corner is 7 is a wall (2474,66), under rock whose lowest corner is 28 a floor (2474,65).
    static bool LandOver(int x, int y, int z)
    {
        if (!LandBlocks(x, y) || LandVoid(x, y))
            return false;
        int c0 = LandZ(x, y), c1 = LandZ(x, y + 1), c2 = LandZ(x + 1, y), c3 = LandZ(x + 1, y + 1);
        if (c0 == int.MinValue || c1 == int.MinValue || c2 == int.MinValue || c3 == int.MinValue)
            return false;
        return LandStandZ(x, y) > z && Math.Min(Math.Min(c0, c1), Math.Min(c2, c3)) < z + PersonHeight;
    }
    public static int StandZ(int x, int y, int fromZ)
    {
        int land = LandStandZ(x, y);
        if (land == int.MinValue)
            return int.MinValue;
        List<int[]> st = StaticsAt(x, y);
        List<UoTiles.Tile> items = UoTiles.Items();
        List<int> floors = new();
        if (!LandBlocks(x, y) && !LandVoid(x, y) && land <= fromZ + LandStepUp)
            floors.Add(land);
        foreach (int[] s in st)
        {
            if (s[0] < 0 || s[0] >= items.Count)
                continue;
            UoTiles.Tile t = items[s[0]];
            if ((t.Flags & 0x200) == 0 || (t.Flags & 0x40) != 0)
                continue;
            int f = s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height);
            if (f <= fromZ + StepUp && !LandOver(x, y, f))
                floors.Add(f);
        }
        int best = int.MinValue;
        foreach (int f in floors)
        {
            if (f <= best)
                continue;
            bool clear = true;
            foreach (int[] s in st)
            {
                if (s[0] < 0 || s[0] >= items.Count)
                    continue;
                UoTiles.Tile t = items[s[0]];
                bool solid = (t.Flags & (0x10 | 0x40)) != 0, surface = (t.Flags & 0x200) != 0;
                if (!solid && !surface)
                    continue;
                int bottom = s[1], top = s[1] + Math.Max(1, t.Height);
                if ((solid ? bottom >= f : bottom > f) && bottom < f + PersonHeight && top > f)
                {
                    clear = false;
                    break;
                }
            }
            if (clear)
                best = f;
        }
        return best;
    }
    // A step as the server takes it, ServUO's Movement.Check (Scripts/Services/Pathing/Movement.cs; Damian, 2026-10-18:
    // "pathfinding needs to be z-block aware enough to find the stairs leading up to the stage, and down to the door on
    // the first floor"): from a walker standing at fz on (fx, fy), whose stood-on surface's top is startTop
    // (GetStartZ), a step reaches a static surface (Surface, not Impassable) whose top, or base for a bridge (a stair),
    // is at most startTop + StepHeight, standing at its base plus CalcHeight (half the height for a bridge); or the
    // land, when its lowest corner is at most startTop + StepHeight, at its centre. A candidate stands only when the
    // column from it to the higher of the walker's old head and its new one (z + PersonHeight) crosses no impassable or
    // surface static (IsOk), so a drop off a floor needs the whole column clear; of those, the one nearest fz, the
    // lower on a tie. int.MinValue when none. The 1998 client's step answer (0x22) carries no height, so the walker's z
    // comes from here.
    public const int StepHeight = 2;
    const int FlagImpassable = 0x40, FlagSurface = 0x200, FlagBridge = 0x400;
    static int CalcHeight(UoTiles.Tile t)
    {
        return (t.Flags & FlagBridge) != 0 ? t.Height / 2 : t.Height;
    }
    // The top of what the walker at z stands on at (x, y): a static surface stood on at z (its full height), or the
    // land's highest corner when stood on the land; z when neither is found.
    public static int StartTop(int x, int y, int z)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        int top = z;
        bool found = false;
        foreach (int[] s in StaticsAt(x, y))
        {
            if (s[0] < 0 || s[0] >= items.Count)
                continue;
            UoTiles.Tile t = items[s[0]];
            if ((t.Flags & FlagSurface) == 0 || (t.Flags & FlagImpassable) != 0)
                continue;
            if (s[1] + CalcHeight(t) == z)
            {
                top = found ? Math.Max(top, s[1] + t.Height) : s[1] + t.Height;
                found = true;
            }
        }
        if (!found && !LandVoid(x, y) && LandStandZ(x, y) == z)
        {
            int c0 = LandZ(x, y), c1 = LandZ(x, y + 1), c2 = LandZ(x + 1, y), c3 = LandZ(x + 1, y + 1);
            if (c0 != int.MinValue && c1 != int.MinValue && c2 != int.MinValue && c3 != int.MinValue)
                top = Math.Max(Math.Max(c0, c1), Math.Max(c2, c3));
        }
        return top;
    }
    static bool ColumnClear(List<int[]> st, List<UoTiles.Tile> items, int ourZ, int testTop)
    {
        foreach (int[] s in st)
        {
            if (s[0] < 0 || s[0] >= items.Count)
                continue;
            UoTiles.Tile t = items[s[0]];
            if ((t.Flags & (FlagImpassable | FlagSurface)) == 0)
                continue;
            int checkZ = s[1], checkTop = checkZ + CalcHeight(t);
            if (checkTop > ourZ && testTop > checkZ)
                return false;
        }
        return true;
    }
    public static int StepZ(int fx, int fy, int fz, int x, int y)
    {
        return StepZ(fz, x, y, StartTop(fx, fy, fz));
    }
    // startTop: StartTop of the tile stepped from, read once for its eight steps by a plan.
    public static int StepZ(int fz, int x, int y, int startTop)
    {
        int c0 = LandZ(x, y), c1 = LandZ(x, y + 1), c2 = LandZ(x + 1, y), c3 = LandZ(x + 1, y + 1);
        if (c0 == int.MinValue)
            return int.MinValue;
        int landLow = c0, landCenter = LandStandZ(x, y);
        if (c1 != int.MinValue && c2 != int.MinValue && c3 != int.MinValue)
            landLow = Math.Min(Math.Min(c0, c1), Math.Min(c2, c3));
        int stepTop = startTop + StepHeight, checkTop = fz + PersonHeight;
        List<int[]> st = StaticsAt(x, y);
        List<UoTiles.Tile> items = UoTiles.Items();
        bool landOpen = !LandBlocks(x, y) && !LandVoid(x, y);
        int best = int.MinValue;
        bool ok = false;
        Func<int, bool> nearer = z => !ok || Math.Abs(z - fz) < Math.Abs(best - fz)
            || (Math.Abs(z - fz) == Math.Abs(best - fz) && z < best);
        foreach (int[] s in st)
        {
            if (s[0] < 0 || s[0] >= items.Count)
                continue;
            UoTiles.Tile t = items[s[0]];
            if ((t.Flags & FlagSurface) == 0 || (t.Flags & FlagImpassable) != 0)
                continue;
            int itemZ = s[1], ourZ = itemZ + CalcHeight(t), itemTop = (t.Flags & FlagBridge) != 0 ? itemZ
                : itemZ + t.Height;
            if (stepTop < itemTop || !nearer(ourZ))
                continue;
            int testTop = Math.Max(checkTop, ourZ + PersonHeight), landCheck = itemZ + Math.Min(t.Height, StepHeight);
            if (landOpen && landCheck < landCenter && landCenter > ourZ && testTop > landLow)
                continue;
            if (!ColumnClear(st, items, ourZ, testTop) || LandOver(x, y, ourZ))
                continue;
            best = ourZ;
            ok = true;
        }
        if (landOpen && stepTop >= landLow && nearer(landCenter)
            && ColumnClear(st, items, landCenter, Math.Max(checkTop, landCenter + PersonHeight)))
        {
            best = landCenter;
            ok = true;
        }
        return best;
    }
    static Map<int, int[]> s_multiBox = Map<int, int[]>.Empty; static readonly Lock s_multiBoxGate = new();
    // A multi's footprint around its center: min x, min y, max x, max y.
    public static int[]? MultiBox(int id)
    {
        if (UoArt.Folder == null)
            return null;
        int[]? box;
        if (s_multiBox.TryGet(id, out box))
            return box;
        box = null;
        try
        {
            byte[]? m = MultiBytes(id);
            if (m != null)
            {
                box = new[] { int.MaxValue, int.MaxValue, int.MinValue, int.MinValue };
                for (int i = 0; i + 12 <= m.Length; i += 12)
                {
                    int cx = BitConverter.ToInt16(m, i + 2), cy = BitConverter.ToInt16(m, i + 4);
                    box[0] = Math.Min(box[0], cx);
                    box[1] = Math.Min(box[1], cy);
                    box[2] = Math.Max(box[2], cx);
                    box[3] = Math.Max(box[3], cy);
                }
            }
        }
        catch { box = null; }
        lock (s_multiBoxGate)
            s_multiBox = s_multiBox.Set(id, box!);
        return box;
    }
    // A house's fit where it would stand, by the 1998 server's rule (OUO 814ea87, CMultiSlave_CanExistAt,
    // multi.c:1051): every component of the multi, at the spot plus its offset and for its height, meets no impassable
    // surface between its z and its top (CTerrainManager_CheckMoveBlocked, terrain.c:536) and has a surface in its
    // column. The surfaces (Terrain_BuildSurfaceList, terrain.c:441): the land from its lowest corner to its average,
    // impassable always and a surface when walkable and not road or furrow land (GetLandTileFlags, move type 8,
    // entity.c:119; void land is none); statics and the multis the server sent, by TILEDATA impassable 0x40, surface
    // 0x200 and bridge 0x400, a bridge's top at half its height (SurfaceInfo_GetTopZ, terrain.c:1691). Items on the
    // ground and the deed script's own conditions (regions) are not counted. Tiles: x, y and 1 where every part there
    // fits, else 0.
    public sealed class HouseFit
    {
        public bool Fits; public readonly List<string> Why = new(); public readonly List<int[]> Tiles = new();
    }
    // The 1998 house deeds (rundir bank/templatestable.dat, templates 1900 on, objvar myhousetype) and their multis,
    // 0x64 + 2 x myhousetype (housestuff.m Q4WW, state 0). The shop deeds (blacksmith's, clothier's, miller's,
    // pickpocket's den, weapon training hut) are type 4; type 20 is the large smithy, which the large bakery and tailor
    // shop share.
    static readonly ImmutableArray<int> s_houseTypes = [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 20 ];
    public static ImmutableArray<int> HouseTypes => s_houseTypes;
    static readonly ImmutableArray<string> s_houseNames = [ "stone-and-plaster house", "field stone house",
        "small brick house",
        "wooden house", "wood-and-plaster house", "thatched-roof cottage", "blue tent", "green tent", "brick house",
        "two-story wood-and-plaster house", "two-story stone-and-plaster house", "tower", "small stone keep", "castle",
        "large smithy" ];
    public static ImmutableArray<string> HouseNames => s_houseNames;
    public static int HouseMulti(int type)
    {
        return 0x64 + 2 * type;
    }
    public static string HouseName(int multiId)
    {
        for (int i = 0; i < s_houseTypes.Length; i++)
            if (HouseMulti(s_houseTypes[i]) == multiId)
                return s_houseNames[i];
        return "multi " + multiId.ToString("X");
    }
    // The deed's cursor carries the multi's largest y as its y offset, and the clicked spot moves north by it before
    // the check (housedeed.m use, housestuff.m Q4X1, getMultiExtents): the click is the house's front row.
    public static int HouseFront(int multiId)
    {
        int[]? b = MultiBox(multiId);
        return b == null ? 0 : b[3];
    }
    static readonly ImmutableArray<int> s_roadLand = [ 0x71, 0x78, 0x09, 0x15, 0x150, 0x15C ];
    public static HouseFit HouseCheck(int multiId, int x, int y, int z)
    {
        HouseFit fit = new();
        byte[]? m;
        try
        {
            m = MultiBytes(multiId);
        }
        catch { m = null; }
        if (m == null || m.Length < 12)
        {
            fit.Why.Add("no multi " + multiId.ToString("X") + " in MULTI.MUL");
            return fit;
        }
        List<UoTiles.Tile> items = UoTiles.Items();
        Map<long, int[]> tiles = Map<long, int[]>.Empty;
        fit.Fits = true;
        for (int i = 0; i + 12 <= m.Length; i += 12)
        {
            int g = BitConverter.ToUInt16(m, i), px = x + BitConverter.ToInt16(m, i + 2), py = y
                + BitConverter.ToInt16(m, i + 4), pz = z + BitConverter.ToInt16(m, i + 6);
            string? why = PartBlocked(px, py, pz, g >= 0 && g < items.Count ? items[g].Height : 0, items);
            long key = ((long)px << 32) | (uint)py;
            int[]? t;
            if (!tiles.TryGet(key, out t))
            {
                t = new[] { px, py, 1 };
                tiles = tiles.Set(key, t);
                fit.Tiles.Add(t);
            }
            if (why == null || t[2] == 0)
                continue;
            t[2] = 0;
            fit.Fits = false;
            if (fit.Why.Count < 20)
                fit.Why.Add(px + "," + py + ": " + why);
        }
        // The deed's area rule comes first in the server (housedeed.m:9, "Housing can not be created in this area.";
        // the placement log's first disagreement, 4532,882, 2026-10-21 03:19:49): a denied area fails every tile.
        if (!UoRegions.Loaded)
        {
            fit.Why.Insert(0, "the regions file is not loaded: the area rule is not checked");
        }
        else
        {
            string? area = UoRegions.HousingDenied(x, y, z);
            if (area != null)
            {
                fit.Fits = false;
                foreach (int[] t in fit.Tiles)
                    t[2] = 0;
                fit.Why.Insert(0, "housing can not be created in this area (region " + area + ")");
            }
        }
        return fit;
    }
    static string? PartBlocked(int x, int y, int z, int height, List<UoTiles.Tile> items)
    {
        if (x < 0 || y < 0 || x >= 6144 || y >= 4096)
            return "off the map";
        bool surface = false, road = false;
        int land = LandIdAt(x, y);
        if (land >= 0 && !LandVoid(x, y))
        {
            int c0 = LandZ(x, y), c1 = LandZ(x + 1, y), c2 = LandZ(x + 1, y + 1), c3 = LandZ(x, y + 1);
            if (c0 == int.MinValue || c1 == int.MinValue || c2 == int.MinValue || c3 == int.MinValue)
                return "map unreadable";
            int low = Math.Min(Math.Min(c0, c1), Math.Min(c2, c3)), avg = LandStandZ(x, y);
            if (z + height > low && avg > z)
                return "the ground rises to z " + avg;
            for (int r = 0; r < s_roadLand.Length; r += 2)
                if (land >= s_roadLand[r] && land <= s_roadLand[r + 1])
                    road = true;
            if (!road && !LandBlocks(x, y))
                surface = true;
        }
        foreach (int[] s in StaticsAt(x, y))
        {
            if (s[0] < 0 || s[0] >= items.Count)
                continue;
            UoTiles.Tile t = items[s[0]];
            int top = s[1] + ((t.Flags & 0x400) != 0 ? t.Height / 2 : t.Height);
            if ((t.Flags & 0x40) != 0 && z + height > s[1] && top > z)
                return UoTiles.Name(s[0]) + " at z " + s[1];
            if ((t.Flags & 0x200) != 0)
                surface = true;
        }
        return surface ? null : road ? "road or furrow" : "nothing to stand on";
    }
}
