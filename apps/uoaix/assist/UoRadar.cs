// The client's radar colours (RADARCOL.MUL: 0x8000 u16 colours, 5-5-5, land ids first, static ids from 0x4000) over
// MAP0.MUL and the statics: one colour per tile, the highest static's where a static stands, else the land's.
// MAP0: per 8 by 8 block (numbered x-block * 512 + y-block) a 4-byte header and 64 cells of (land id u16, z s8), row by
// row. STAIDX0: per block (offset, length, extra); STATICS0: 7-byte entries (graphic u16, x u8, y u8, z s8, hue).
// Blocks are cached, 128 bytes of colours each; the cache is dropped past BlockKeep blocks (5 MiB).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public static class UoRadar
{
    const int BlocksTall = 512;
    const int BlockKeep = 20000;
    const int BlockBytes = 196;

    // Per cell of a block, x fastest: the radar colour of the top surface, its height, the land id under it, and
    // whether a static covers the land.
    public sealed class Cells
    {
        public readonly ushort[] Color = new ushort[64];
        public readonly ushort[] Land = new ushort[64];
        public readonly ushort[] Static = new ushort[64];
        public readonly sbyte[] Z = new sbyte[64];
        public readonly sbyte[] LandZ = new sbyte[64];
        public readonly bool[] Covered = new bool[64];
        // Every static per cell, lowest first: graphic | (z & 0xFF) << 16; null where a cell has none.
        public readonly List<int>?[] ByCell = new List<int>?[64];
    }

    sealed record Files(
        string Folder,
        ushort[] Colors,
        FileStream MapFile,
        FileStream IndexFile,
        FileStream StaticsFile,
        int BlocksWide);

    static readonly Lock s_lock = new();
    static Files? s_files;
    static Map<int, Cells> s_blocks = Map<int, Cells>.Empty;
    // The two blocks last answered: a map render asks for the same block for every pixel it covers, and for the block
    // northwest of it where hill shading crosses a block edge; a Map lookup per pixel doubled the radar zooms' render
    // time (2026-10-21, 480 x 500 in Britain: 20 to 25 ms became 37 to 45).
    static int s_lastKey = -1;
    static Cells? s_last;
    static int s_prevKey = -1;
    static Cells? s_prev;

    static Files? Open()
    {
        string? folder = UoArt.Folder;
        if (folder is null)
            return null;
        if (s_files is not null && s_files.Folder == folder)
            return s_files;
        try
        {
            byte[] c = File.ReadAllBytes(Path.Combine(folder, "RADARCOL.MUL"));
            ushort[] colors = new ushort[c.Length / 2];
            Buffer.BlockCopy(c, 0, colors, 0, colors.Length * 2);
            FileStream map = File.OpenRead(Path.Combine(folder, "MAP0.MUL"));
            FileStream sidx = File.OpenRead(Path.Combine(folder, "STAIDX0.MUL"));
            FileStream stat = File.OpenRead(Path.Combine(folder, "STATICS0.MUL"));
            s_files = new Files(folder, colors, map, sidx, stat, (int)(map.Length / BlockBytes / BlocksTall));
            s_blocks = Map<int, Cells>.Empty;
            s_lastKey = s_prevKey = -1;
            s_last = s_prev = null;
            return s_files;
        }
        catch
        {
            s_files = null;
            return null;
        }
    }

    static byte[]? Read(FileStream fs, long at, int len)
    {
        if (at < 0 || len <= 0 || at + len > fs.Length)
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

    // The cells of block (bx, by); null off the map or with no files.
    public static Cells? Block(int bx, int by)
    {
        lock (s_lock)
        {
            Files? f = Open();
            if (f is null || bx < 0 || by < 0 || bx >= f.BlocksWide || by >= BlocksTall)
                return null;
            int key = bx * BlocksTall + by;
            if (key == s_lastKey && s_last is not null)
                return s_last;
            if (key == s_prevKey && s_prev is not null)
                return Remember(key, s_prev);
            if (s_blocks.TryGet(key, out Cells? got))
                return Remember(key, got);
            if (s_blocks.Count > BlockKeep)
                s_blocks = Map<int, Cells>.Empty;
            byte[]? cells = Read(f.MapFile, (long)key * BlockBytes + 4, 192);
            if (cells is null)
                return null;
            ushort[] colors = f.Colors;
            Cells c = new();
            for (int i = 0; i < 64; i++)
            {
                int id = BitConverter.ToUInt16(cells, i * 3);
                c.Land[i] = (ushort)id;
                c.Color[i] = id < colors.Length ? colors[id] : (ushort)0;
                c.Z[i] = c.LandZ[i] = (sbyte)cells[i * 3 + 2];
            }
            byte[]? idx = Read(f.IndexFile, (long)key * 12, 12);
            if (idx is not null)
            {
                int off = BitConverter.ToInt32(idx, 0);
                int len = BitConverter.ToInt32(idx, 4);
                byte[]? st = off >= 0 && len > 0 && len < 1 << 20 ? Read(f.StaticsFile, off, len) : null;
                if (st is not null)
                    AddStatics(c, st, colors);
            }
            s_blocks = s_blocks.Set(key, c);
            return Remember(key, c);
        }
    }

    static Cells Remember(int key, Cells c)
    {
        s_prevKey = s_lastKey;
        s_prev = s_last;
        s_lastKey = key;
        s_last = c;
        return c;
    }

    static void AddStatics(Cells c, byte[] st, ushort[] colors)
    {
        for (int i = 0; i + 7 <= st.Length; i += 7)
        {
            int id = BitConverter.ToUInt16(st, i);
            int cell = st[i + 3] * 8 + st[i + 2];
            int z = (sbyte)st[i + 4];
            if (cell >= 64)
                continue;
            List<int> at = c.ByCell[cell] ??= new List<int>();
            int v = (id & 0xFFFF) | (z & 0xFF) << 16;
            int pri = Priority(id, z);
            int j = at.Count;
            while (j > 0 && Priority(at[j - 1] & 0xFFFF, (sbyte)(at[j - 1] >> 16)) > pri)
                j--;
            at.Insert(j, v);
            if (z >= c.Z[cell] && 0x4000 + id < colors.Length)
            {
                c.Z[cell] = (sbyte)z;
                c.Color[cell] = colors[0x4000 + id];
                c.Covered[cell] = true;
                c.Static[cell] = (ushort)id;
            }
        }
    }

    public static int ToArgb(ushort c)
    {
        int r = (c >> 10) & 31;
        int g = (c >> 5) & 31;
        int b = c & 31;
        return unchecked((int)0xFF000000) | (r * 255 / 31) << 16 | (g * 255 / 31) << 8 | (b * 255 / 31);
    }

    // A static's drawing order on its tile, as the client's (ClassicUO GameObject PriorityZ): its z, one less for a
    // background piece (TILEDATA flag 0x1), one more for one with height, so a tree's leaves draw over its trunk.
    public static int Priority(int id, int z)
    {
        List<UoTiles.Tile> items = UoTiles.Items();
        if (id < 0 || id >= items.Count)
            return z;
        UoTiles.Tile t = items[id];
        return z - ((t.Flags & 0x1) != 0 ? 1 : 0) + (t.Height > 0 ? 1 : 0);
    }

    public static int LandZAt(int x, int y, int fallback)
    {
        if (x < 0 || y < 0)
            return fallback;
        Cells? b = Block(x >> 3, y >> 3);
        return b is null ? fallback : b.LandZ[(y & 7) * 8 + (x & 7)];
    }

    // The top surface's height at (x, y), or fallback off the map.
    public static int TopZ(int x, int y, int fallback)
    {
        if (x < 0 || y < 0)
            return fallback;
        Cells? b = Block(x >> 3, y >> 3);
        return b is null ? fallback : b.Z[(y & 7) * 8 + (x & 7)];
    }

    // Hill shading, light from the northwest: a surface rising toward the southeast faces the light.
    public static int Shade(int argb, int z, int zNorthWest)
    {
        return Scale(argb, Math.Max(55, Math.Min(145, 100 + (z - zNorthWest) * 5)));
    }

    // The colour at f percent brightness.
    public static int Scale(int argb, int f)
    {
        int r = Math.Min(255, ((argb >> 16) & 255) * f / 100);
        int g = Math.Min(255, ((argb >> 8) & 255) * f / 100);
        int b = Math.Min(255, (argb & 255) * f / 100);
        return unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
    }
}
