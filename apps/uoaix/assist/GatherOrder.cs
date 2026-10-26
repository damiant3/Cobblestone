// The order a gathering takes its spots in (AssistHarvest.md stage 1): every spot of the oval of two foci (the
// collection's place and where the gathering starts) and a range, taken in bands of Band tiles outward, the width of an
// 8x8 chunk; the spot whose walk plus Band a band is least first. Range: the outermost oval's half-width across the
// middle of the line, so with one focus a circle of Range.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Uoaix.Assist;

// A spot of the oval: its tile and height, the art there, and its band outward.
public sealed record GatherSpot(int X, int Y, int Z, int Graphic, int Band);

public static class GatherOrder
{
    public const int Band = 8;

    public static double Apart(int x, int y, int x2, int y2)
    {
        double dx = x - x2;
        double dy = y - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static int Tiles(int x, int y, int x2, int y2) => Math.Max(Math.Abs(x - x2), Math.Abs(y - y2));

    // Every tile of the oval's box once, in the order of the map's columns; spotAt gives a tile's spot (graphic, z) or
    // null where it holds none.
    public static ImmutableArray<GatherSpot> Oval(int ax, int ay, int bx, int by, int radius,
        Func<int, int, int[]?> spotAt)
    {
        ImmutableArray<GatherSpot>.Builder l = ImmutableArray.CreateBuilder<GatherSpot>();
        double ab = Apart(ax, ay, bx, by);
        // The outermost oval's semi-major axis; its level is twice that less the foci apart.
        double major = Math.Sqrt(ab * ab / 4 + (double)radius * radius);
        double limit = 2 * major - ab;
        int mx = (ax + bx) / 2;
        int my = (ay + by) / 2;
        int half = (int)Math.Ceiling(major) + 1;
        for (int x = mx - half; x <= mx + half; x++)
        {
            for (int y = my - half; y <= my + half; y++)
            {
                double level = Apart(x, y, ax, ay) + Apart(x, y, bx, by) - ab;
                if (level > limit)
                    continue;
                int[]? s = spotAt(x, y);
                if (s is not null)
                    l.Add(new GatherSpot(x, y, s[1], s[0], (int)(level / Band)));
            }
        }
        return l.ToImmutable();
    }

    // A band outward costs its own width in walking (Damian, 2026-10-23: the U of Yew's grove, "at the top of the U the
    // circuit will run back and forth between the two sides of the U arms. instead it should, at the top of one arm,
    // turn around and spiral backwards"): the next band near the player beats this band across the grove. Over Yew's
    // 188 trees in 88 chunks at range 60 (the client statics, every pick emptying its chunk), strict band order walked
    // 1630 tiles with 28 crossings over 20 tiles; this weight 967 and 13. The least walk plus Band a band from (px,
    // py), the lower band on a tie, the first of those in order; skip leaves a spot out; null when none is left.
    public static GatherSpot? Pick(IEnumerable<GatherSpot> spots, int px, int py, Func<GatherSpot, bool> skip)
    {
        GatherSpot? best = null;
        int bestScore = int.MaxValue;
        foreach (GatherSpot t in spots)
        {
            if (skip(t))
                continue;
            int score = Tiles(px, py, t.X, t.Y) + Band * t.Band;
            if (best is null || score < bestScore || (score == bestScore && t.Band < best.Band))
            {
                best = t;
                bestScore = score;
            }
        }
        return best;
    }
}
