#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Uoaix.Assist.Tests;

// A client map of the test's own, 256 tiles square, written as the 1.25 client's files (MAP0.MUL, STAIDX0.MUL,
// STATICS0.MUL, TILEDATA.MUL) and installed as UoArt.Folder for one body: grass at z 0 everywhere, water (land 0xA8,
// impassable) and wall statics (0x80, 20 high) where the test puts them; a door (0x81, flagged a door, in no door set)
// and a bag (0x82, a container) for items, placed by none.
public sealed class TestMap
{
    public const int Size = 256;
    public const int Grass = 0x3;
    public const int Water = 0xA8;
    public const int Wall = 0x80;
    public const int Door = 0x81;
    public const int Bag = 0x82;
    // The first door set of the 1.25 client (base 0x00E8): shut at the base, open one past it, both flagged door and
    // impassable as the client's are.
    public const int DoorShut = 0xE8;
    public const int DoorOpen = 0xE9;
    // A bookcase and the spellbook, containers as in the client, and a plain container one past the bookcase.
    public const int Bookcase = 0x0A97;
    public const int Spellbook = 0x0EFA;
    public const int BoxPastBooks = 0x0A9D;
    const int BlocksTall = 512;
    const int LandBlock = 4 + 32 * 26;
    const int LandBytes = 512 * LandBlock;
    const int ItemBlock = 4 + 32 * 37;
    const int Items = 0x0F00;

    Set<long> m_water = Set<long>.Empty;
    Set<long> m_walls = Set<long>.Empty;
    Set<long> m_rock = Set<long>.Empty;
    Map<int, List<int[]>> m_multis = Map<int, List<int[]>>.Empty;

    // A multi of id id whose static parts are wall pieces at the offsets given (dx, dy each), as MULTI.MUL holds them:
    // placed in the world by UoMap.PutMulti, as the server's 0x1A does.
    public TestMap MultiWalls(int id, params int[][] parts)
    {
        m_multis = m_multis.Set(id, new List<int[]>(parts));
        return this;
    }

    // A house in multi id: wall pieces round the square of half-side r about its center, a shut door at the middle of
    // its south wall (0, r), and floor (Floor, a walkable surface) on every tile inside.
    public TestMap MultiHouse(int id, int r)
    {
        if (!m_multis.TryGet(id, out List<int[]>? parts))
            m_multis = m_multis.Set(id, parts = new List<int[]>());
        for (int d = -r; d <= r; d++)
        {
            parts.Add(new[] { d, -r });
            parts.Add(new[] { -r, d });
            parts.Add(new[] { r, d });
            if (d != 0)
                parts.Add(new[] { d, r });
        }
        for (int dx = -r + 1; dx < r; dx++)
        {
            for (int dy = -r + 1; dy < r; dy++)
                parts.Add(new[] { dx, dy, Floor, 1 });
        }
        parts.Add(new[] { 0, r, DoorShut });
        return this;
    }

    // A walkable floor: a surface (TILEDATA flag 0x200), as a house's floor is.
    public const int Floor = 0x83;

    // Cut cloth, a stack named so (the Assist knows cloth by its name, IsCloth).
    public const int Cloth = 0x84;

    // A shut door (DoorShut) in multi id at dx, dy, flags 0: a part the server places as its own item, as a house's
    // door.
    public TestMap MultiDoor(int id, int dx, int dy)
    {
        if (!m_multis.TryGet(id, out List<int[]>? parts))
            m_multis = m_multis.Set(id, parts = new List<int[]>());
        parts.Add(new[] { dx, dy, DoorShut });
        return this;
    }

    static long Key(int x, int y) => ((long)x << 16) | (uint)y;

    public TestMap WaterAt(int x, int y)
    {
        m_water = m_water.Add(Key(x, y));
        return this;
    }

    // Mountain land (0xDC, the first of mining.m's Q4ZJ land ids): ground a shovel digs, walkable here.
    public const int Rock = 0xDC;

    public TestMap RockAt(int x, int y)
    {
        m_rock = m_rock.Add(Key(x, y));
        return this;
    }

    public TestMap WallAt(int x, int y)
    {
        m_walls = m_walls.Add(Key(x, y));
        return this;
    }

    public TestMap WallLine(int x0, int y0, int x1, int y1)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                m_walls = m_walls.Add(Key(x, y));
        }
        return this;
    }

    public bool IsWall(int x, int y) => m_walls.Contains(Key(x, y));

    public bool IsWater(int x, int y) => m_water.Contains(Key(x, y));

    static int s_runs;

    // The files written, the folder installed for body, and the client's caches emptied before and after. Each run has
    // a folder of its own: a loop of the last case's features still reading TILEDATA held the shared one's file open
    // when the next case wrote it (IOException, 1 suite run in 3, 2026-10-23).
    public void Run(Action body)
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "assist-tests-map-" + Environment.ProcessId + "-" + Interlocked.Increment(ref s_runs));
        Directory.CreateDirectory(dir);
        Write(dir);
        string? was = UoArt.Folder;
        UoArt.Folder = dir;
        UoMap.Reset();
        UoTiles.Reset();
        try
        {
            body();
        }
        finally
        {
            UoArt.Folder = was;
            UoMap.Reset();
            UoTiles.Reset();
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    void Write(string dir)
    {
        int blocks = (Size / 8 - 1) * BlocksTall + Size / 8;
        byte[] map = new byte[blocks * 196];
        byte[] index = new byte[blocks * 12];
        List<byte> statics = new();
        for (int bx = 0; bx < Size / 8; bx++)
        {
            for (int by = 0; by < Size / 8; by++)
            {
                int block = bx * BlocksTall + by;
                int start = statics.Count;
                for (int cy = 0; cy < 8; cy++)
                {
                    for (int cx = 0; cx < 8; cx++)
                    {
                        int x = bx * 8 + cx;
                        int y = by * 8 + cy;
                        int cell = block * 196 + 4 + (cy * 8 + cx) * 3;
                        int land = IsWater(x, y) ? Water : m_rock.Contains(Key(x, y)) ? Rock : Grass;
                        map[cell] = (byte)land;
                        map[cell + 1] = (byte)(land >> 8);
                        map[cell + 2] = 0;
                        if (IsWall(x, y))
                            statics.AddRange(new byte[] { Wall & 0xFF, Wall >> 8, (byte)cx, (byte)cy, 0, 0, 0 });
                    }
                }
                int len = statics.Count - start;
                BitConverter.GetBytes(len > 0 ? start : -1).CopyTo(index, block * 12);
                BitConverter.GetBytes(len).CopyTo(index, block * 12 + 4);
            }
        }
        File.WriteAllBytes(Path.Combine(dir, "MAP0.MUL"), map);
        File.WriteAllBytes(Path.Combine(dir, "STAIDX0.MUL"), index);
        File.WriteAllBytes(Path.Combine(dir, "STATICS0.MUL"), statics.ToArray());
        byte[] tiles = new byte[LandBytes + Items / 32 * ItemBlock];
        BitConverter.GetBytes(0x40u).CopyTo(tiles, (Water / 32) * LandBlock + 4 + (Water % 32) * 26);
        int wall = LandBytes + (Wall / 32) * ItemBlock + 4 + (Wall % 32) * 37;
        BitConverter.GetBytes(0x10u).CopyTo(tiles, wall);
        tiles[wall + 4] = 255;
        tiles[wall + 16] = 20;
        Encoding.ASCII.GetBytes("wall").CopyTo(tiles, wall + 17);
        int door = LandBytes + (Door / 32) * ItemBlock + 4 + (Door % 32) * 37;
        BitConverter.GetBytes(0x20000000u).CopyTo(tiles, door);
        tiles[door + 4] = 255;
        Encoding.ASCII.GetBytes("door").CopyTo(tiles, door + 17);
        foreach (int leaf in new[] { DoorShut, DoorOpen })
        {
            int at = LandBytes + (leaf / 32) * ItemBlock + 4 + (leaf % 32) * 37;
            BitConverter.GetBytes(0x20000040u).CopyTo(tiles, at);
            tiles[at + 4] = 255;
            tiles[at + 16] = 20;
            Encoding.ASCII.GetBytes("wooden door").CopyTo(tiles, at + 17);
        }
        foreach (int box in new[] { Bookcase, Spellbook, BoxPastBooks })
        {
            int at = LandBytes + (box / 32) * ItemBlock + 4 + (box % 32) * 37;
            BitConverter.GetBytes(0x00200000u).CopyTo(tiles, at);
            Encoding.ASCII.GetBytes(box == Spellbook ? "spellbook"
                : box == Bookcase ? "bookcase" : "wooden box").CopyTo(tiles, at + 17);
        }
        // Gold coin (0x0EED) stacks, as the 1.25 client's does (flag 0x800): an amount, not a count of items.
        int gold = LandBytes + (0x0EED / 32) * ItemBlock + 4 + (0x0EED % 32) * 37;
        BitConverter.GetBytes(0x00000800u).CopyTo(tiles, gold);
        Encoding.ASCII.GetBytes("gold coin").CopyTo(tiles, gold + 17);
        int cloth = LandBytes + (Cloth / 32) * ItemBlock + 4 + (Cloth % 32) * 37;
        BitConverter.GetBytes(0x00000800u).CopyTo(tiles, cloth);
        Encoding.ASCII.GetBytes("cut cloth").CopyTo(tiles, cloth + 17);
        int floor = LandBytes + (Floor / 32) * ItemBlock + 4 + (Floor % 32) * 37;
        BitConverter.GetBytes(0x00000200u).CopyTo(tiles, floor);
        Encoding.ASCII.GetBytes("floor").CopyTo(tiles, floor + 17);
        int bag = LandBytes + (Bag / 32) * ItemBlock + 4 + (Bag % 32) * 37;
        BitConverter.GetBytes(0x00200000u).CopyTo(tiles, bag);
        Encoding.ASCII.GetBytes("bag").CopyTo(tiles, bag + 17);
        File.WriteAllBytes(Path.Combine(dir, "TILEDATA.MUL"), tiles);
        // MULTI.IDX: offset, length, extra per id; MULTI.MUL: item u16, x s16, y s16, z s16, flags u32 per part (flags
        // 0 is a part the server places as its own item, so these carry 1).
        int ids = 0;
        foreach (int id in m_multis.Keys)
            ids = Math.Max(ids, id + 1);
        byte[] midx = new byte[ids * 12];
        for (int i = 0; i < ids; i++)
            BitConverter.GetBytes(-1).CopyTo(midx, i * 12);
        List<byte> mmul = new();
        foreach (KeyValuePair<int, List<int[]>> m in m_multis)
        {
            BitConverter.GetBytes(mmul.Count).CopyTo(midx, m.Key * 12);
            BitConverter.GetBytes(m.Value.Count * 12).CopyTo(midx, m.Key * 12 + 4);
            foreach (int[] p in m.Value)
            {
                // A part of three is placed by the server (a door, flags 0); of four, a static of its own art (flags
                // 1).
                bool placed = p.Length == 3;
                mmul.AddRange(BitConverter.GetBytes((ushort)(p.Length > 2 ? p[2] : Wall)));
                mmul.AddRange(BitConverter.GetBytes((short)p[0]));
                mmul.AddRange(BitConverter.GetBytes((short)p[1]));
                mmul.AddRange(BitConverter.GetBytes((short)0));
                mmul.AddRange(BitConverter.GetBytes(placed ? 0u : 1u));
            }
        }
        File.WriteAllBytes(Path.Combine(dir, "MULTI.IDX"), midx);
        File.WriteAllBytes(Path.Combine(dir, "MULTI.MUL"), mmul.ToArray());
        File.WriteAllBytes(Path.Combine(dir, "VERDATA.MUL"), new byte[4]);
    }
}
