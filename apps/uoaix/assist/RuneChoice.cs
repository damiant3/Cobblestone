// A rune is where it lands, never its label (docs/Designs/Active/Apps/AssistIdentity.md, stage 1; Damian, 2026-10-22:
// "make all identification of ingame objects either tied to map/region knowns or by targeting in the client").
#nullable enable
using System;
using System.Collections.Immutable;

namespace Uoaix.Assist;

// A rune seen: its serial, where it was seen to land (null before its first recall), and how well its label names the
// place wanted (0 for not at all, higher for better).
public sealed record RuneSeen(uint Serial, int[]? Landing, int LabelRank);

public static class RuneChoice
{
    // The rune for a place: one whose landing lies in it, the best label first, then the lower serial; else, of the
    // runes never yet recalled by, the best label above 0. A rune whose landing lies elsewhere is never taken, whatever
    // its label says. 0 for none.
    public static uint Pick(ImmutableArray<RuneSeen> runes, Func<int, int, bool> inPlace)
    {
        RuneSeen? landed = null, guessed = null;
        foreach (RuneSeen r in runes)
        {
            if (r.Landing is int[] at)
            {
                if (inPlace(at[0], at[1]) && Better(r, landed))
                    landed = r;
            }
            else if (r.LabelRank > 0 && Better(r, guessed))
            {
                guessed = r;
            }
        }
        return landed?.Serial ?? guessed?.Serial ?? 0;
    }

    static bool Better(RuneSeen r, RuneSeen? best) =>
        best is null || r.LabelRank > best.LabelRank || r.LabelRank == best.LabelRank && r.Serial < best.Serial;
}
