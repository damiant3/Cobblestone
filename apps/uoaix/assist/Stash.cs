// Thread put down in a shop past the carry limit, kept for a return trip (Damian, 2026-10-21: "add explicit offloading
// when encumbered of thread just purchased, mark it for return trip later. it takes hours to decay, and there will be
// moments of opportunity to clean up"), and the spot it goes on (StashSpots).
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

public sealed record Stash(uint Serial, int Graphic, int Amount, int X, int Y, int Z, string City, DateTime At)
{
    public string Line()
    {
        return Serial.ToString("X") + "\t" + Graphic.ToString("X") + "\t" + Amount + "\t" + X + "\t" + Y + "\t" + Z
            + "\t" + City + "\t" + At.Ticks;
    }

    public static Stash? Parse(string line)
    {
        string[] t = line.Split('\t');
        if (t.Length != 8)
            return null;
        bool ok = uint.TryParse(t[0], NumberStyles.HexNumber, null, out uint serial)
            & int.TryParse(t[1], NumberStyles.HexNumber, null, out int graphic)
            & int.TryParse(t[2], out int amount) & int.TryParse(t[3], out int x) & int.TryParse(t[4], out int y)
            & int.TryParse(t[5], out int z) & long.TryParse(t[7], out long ticks);
        return ok ? new Stash(serial, graphic, amount, x, y, z, t[6], new DateTime(ticks)) : null;
    }
}

// The stashes, one line each in stash-<server>.txt, kept across relay restarts.
public sealed class Stashes
{
    readonly Func<string> m_path;
    readonly Lock m_gate = new();
    ImmutableList<Stash> m_all = [];
    bool m_loaded;

    public Stashes(Func<string> path)
    {
        m_path = path;
    }

    public ImmutableList<Stash> All
    {
        get
        {
            lock (m_gate)
            {
                Load();
                return m_all;
            }
        }
    }

    public void Put(Stash s)
    {
        lock (m_gate)
        {
            Load();
            m_all = m_all.RemoveAll(x => x.Serial == s.Serial).Add(s);
            Save();
        }
    }

    public void Remove(uint serial)
    {
        lock (m_gate)
        {
            Load();
            m_all = m_all.RemoveAll(x => x.Serial == serial);
            Save();
        }
    }

    void Load()
    {
        if (m_loaded)
            return;
        m_loaded = true;
        try
        {
            string path = m_path();
            if (!File.Exists(path))
                return;
            foreach (string line in File.ReadAllLines(path))
            {
                Stash? s = Stash.Parse(line);
                if (s is not null)
                    m_all = m_all.Add(s);
            }
        }
        catch
        {
        }
    }

    void Save()
    {
        List<string> lines = new();
        foreach (Stash s in m_all)
            lines.Add(s.Line());
        try
        {
            File.WriteAllLines(m_path(), lines);
        }
        catch
        {
        }
    }
}

// A floor tile the client draws behind something (Damian, 2026-10-21: "create a spot finder that uses the isomorphic
// projection to hide things behind the walls and such, although tread in a tailor shop looks like decorations if done
// right"). The 1.25 client draws a tile's art at screen (x - y) * 22, (x + y) * 22 - z * 4, a tile 44 pixels wide and a
// z step 4 pixels, and draws nearer tiles (greater x + y) over farther ones. A tall static on the tile south-east of a
// spot (x + 1, y + 1, 44 pixels lower on screen, the same column) covers what lies on the spot once it rises CoverRise
// above it; one on the tile east (x + 1, y) or south (x, y + 1), 22 pixels lower and half a tile aside, covers half of
// it once it rises HalfRise. Failing a hidden spot, one beside the shop's furniture, where thread passes for
// decoration.
// What the spot finder reads of the client's map: a tile's statics (art, z each), the tile data, and the height a step
// from one tile onto another stands at (int.MinValue for none). Client is the client's own; a test gives its own.
public sealed record StashMap(Func<int, int, List<int[]>> Statics, Func<List<UoTiles.Tile>> Tiles,
    Func<int, int, int, int, int, int> StepZ)
{
    static readonly StashMap s_client = new(UoMap.StaticsAt, UoTiles.Items, UoMap.StepZ);

    public static StashMap Client => s_client;
}

public static class StashSpots
{
    // A thread pile's art stands about 16 pixels (4 z): hidden behind a static 44 pixels lower once 44 + 16 = 60 pixels
    // (15 z) of it rise above the spot; half hidden behind one 22 pixels lower once 22 + 16 = 38 pixels (10 z) rise.
    public const int CoverRise = 15, HalfRise = 10;
    // Tile data flags: wall, impassable, surface, roof, door.
    const uint FlagWall = 0x10, FlagImpassable = 0x40, FlagSurface = 0x200;
    const uint FlagRoof = 0x10000000, FlagDoor = 0x20000000;
    // A spot's floor within this of the player's height is on the player's floor.
    public const int FloorRise = 10;

    public sealed record Spot(int X, int Y, int Z, int Cover, bool Furnished);

    // How much of a thing on (x, y) at z the client hides, in quarters: 4 hidden, 2 half, 0 in plain view.
    public static int Cover(int x, int y, int z, StashMap? map = null)
    {
        StashMap m = map ?? StashMap.Client;
        int cover = Rise(m, x + 1, y + 1, z) >= CoverRise ? 4 : 0;
        if (Rise(m, x + 1, y, z) >= HalfRise)
            cover += 2;
        if (Rise(m, x, y + 1, z) >= HalfRise)
            cover += 2;
        return Math.Min(4, cover);
    }

    // The highest top over z of a static standing up from the floor on (x, y): not the floor's own surface tiles, not a
    // roof (the client leaves the roof undrawn while the player is under it), nothing that starts above FloorRise.
    static int Rise(StashMap m, int x, int y, int z)
    {
        List<UoTiles.Tile> tiles = m.Tiles();
        int best = 0;
        foreach (int[] s in m.Statics(x, y))
        {
            if (s[0] < 0 || s[0] >= tiles.Count)
                continue;
            UoTiles.Tile t = tiles[s[0]];
            if ((t.Flags & FlagRoof) != 0 || s[1] > z + FloorRise)
                continue;
            if ((t.Flags & FlagSurface) != 0 && (t.Flags & FlagImpassable) == 0 && s[1] + t.Height <= z)
                continue;
            best = Math.Max(best, s[1] + t.Height - z);
        }
        return best;
    }

    // A tile beside furniture: a static standing on a neighbouring tile that is no wall or door (a table, a loom, a
    // shelf, a bolt display).
    static bool Furnished(StashMap m, int x, int y, int z)
    {
        List<UoTiles.Tile> tiles = m.Tiles();
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                foreach (int[] s in m.Statics(x + dx, y + dy))
                {
                    if (s[0] < 0 || s[0] >= tiles.Count || Math.Abs(s[1] - z) > FloorRise)
                        continue;
                    uint f = tiles[s[0]].Flags;
                    if ((f & (FlagWall | FlagDoor)) == 0 && tiles[s[0]].Height > 0)
                        return true;
                }
            }
        }
        return false;
    }

    static bool HasDoor(StashMap m, int x, int y)
    {
        List<UoTiles.Tile> tiles = m.Tiles();
        foreach (int[] s in m.Statics(x, y))
        {
            if (s[0] >= 0 && s[0] < tiles.Count && (tiles[s[0]].Flags & FlagDoor) != 0)
                return true;
        }
        return false;
    }

    // The spots of the rectangle on the player's floor, best first: the most hidden, then furnished, then the nearest
    // the player. refused: tiles not to use (taken, or refused before).
    public static ImmutableArray<Spot> Find(int left, int top, int width, int height, int px, int py, int pz,
        Func<int, int, bool> refused, StashMap? map = null)
    {
        StashMap m = map ?? StashMap.Client;
        List<Spot> spots = new();
        for (int x = left; x < left + width; x++)
        {
            for (int y = top; y < top + height; y++)
            {
                if (refused(x, y) || HasDoor(m, x, y) || x == px && y == py)
                    continue;
                int z = m.StepZ(px, py, pz, x, y);
                if (z == int.MinValue || Math.Abs(z - pz) > FloorRise)
                    continue;
                spots.Add(new Spot(x, y, z, Cover(x, y, z, m), Furnished(m, x, y, z)));
            }
        }
        spots.Sort((a, b) =>
        {
            if (a.Cover != b.Cover)
                return b.Cover.CompareTo(a.Cover);
            if (a.Furnished != b.Furnished)
                return b.Furnished.CompareTo(a.Furnished);
            int da = Math.Max(Math.Abs(a.X - px), Math.Abs(a.Y - py));
            int db = Math.Max(Math.Abs(b.X - px), Math.Abs(b.Y - py));
            return da.CompareTo(db);
        });
        return [.. spots];
    }
}
