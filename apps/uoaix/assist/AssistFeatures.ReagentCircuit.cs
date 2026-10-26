#nullable enable
using System;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed partial class AssistFeatures
{
    // ---------- the reagent harvest ----------
    // Damian, 2026-10-24: "a new circuit which is reagent harvestor, which just runs between the reagent finds and
    // makes sure the autopicker grabs them and they avoid monsters". The finds are the reagent piles the seen record
    // keeps (res.seen kind 4, ResourceSpots); the walk-past pickup (PickupLoop) lifts each one in its reach, and the
    // lift takes the sighting (SeenLifted). A pile the pickup leaves where it lies, or no walk reaches, is left for the
    // run (L-FUTILITY).
    volatile bool m_reagentMode;
    public const int ReagentRangeDefault = 60, ReagentPickupWaitMs = 6000, ReagentKind = 4;
    public int ReagentRange
    {
        get => Math.Max(1, Cfg.GetInt(Server + "reagents.range", ReagentRangeDefault));
        set => Cfg.Set(Server + "reagents.range", Math.Max(1, value).ToString());
    }
    public string? CircuitReagents()
    {
        return StartCircuit(() => m_reagentMode = true);
    }
    Set<long> m_reagentLeft = Set<long>.Empty;
    bool ReagentLies(int x, int y)
    {
        foreach (int[] r in ResourceSpots())
            if (r[2] == ReagentKind && r[0] == x && r[1] == y)
                return true;
        return false;
    }
    // The nearest reagent pile known within the range and not left this run: x, y, amount; null when none.
    int[]? ReagentNext(GameLink k)
    {
        int px, py;
        lock (k.State.Gate)
        {
            px = k.State.X;
            py = k.State.Y;
        }
        int[]? best = null;
        int bd = int.MaxValue, range = ReagentRange;
        foreach (int[] r in ResourceSpots())
        {
            if (r[2] != ReagentKind || m_reagentLeft.Contains(TileKey(r[0], r[1])))
                continue;
            int d = Tiles(px, py, r[0], r[1]);
            if (d > range || d >= bd)
                continue;
            best = new[] { r[0], r[1], r[3] };
            bd = d;
        }
        return best;
    }
    string RunReagents(GameLink k)
    {
        if (!Pickup)
        {
            Pickup = true;
            Note("Reagents: Item pickup turned on for the harvest");
        }
        m_reagentLeft = m_reagentLeft.Clear();
        int visited = 0, taken = 0;
        while (!circuitStop && !k.Closed)
        {
            ReagentStatus = visited + " piles visited, " + taken + " picked up";
            List<int[]> th = Threats(k);
            if (th.Count > 0 && Nearest(th) <= FleeClear)
            {
                CircuitStatus = "Reagents: away from a threat";
                Note("Reagents: a threat within " + Nearest(th) + " tiles; running");
                string? fw = Flee();
                if (fw != null)
                    return "running from a threat: " + fw;
                continue;
            }
            int held = Inv(k).ItemCount();
            if (held >= Inventory.ItemsMax - 1)
                return "the pack holds " + held + " items, " + Inventory.ItemsMax + " at most";
            int str, weight;
            lock (k.State.Gate)
            {
                str = k.State.Str;
                weight = k.State.Weight;
            }
            if (str > 0 && weight + SewWeightRoom > str * 4 + 30)
                return "the pack weighs " + weight + " of " + (str * 4 + 30) + " stones";
            int[]? next = ReagentNext(k);
            if (next == null)
                return visited == 0 ? "no reagent pile known within " + ReagentRange + " tiles"
                    : "every reagent pile known within " + ReagentRange + " tiles visited";
            int x = next[0], y = next[1];
            long key = TileKey(x, y);
            CircuitStatus = "Reagents: to " + next[2] + " at " + x + "," + y;
            string? w = WalkTo(x, y, 1);
            if (w != null)
            {
                m_reagentLeft = m_reagentLeft.Add(key);
                if (w == "stopped" || circuitStop)
                    return "stopped";
                Note("Reagents: the pile at " + x + "," + y + " was not reached: " + w + "; left");
                continue;
            }
            visited++;
            CircuitStatus = "Reagents: the pickup at " + x + "," + y;
            if (Wait.Until(Clock, () => !ReagentLies(x, y) || circuitStop || k.Closed, ReagentPickupWaitMs,
                "the pickup", stepMs: 100).Done && !ReagentLies(x, y))
            {
                taken++;
                continue;
            }
            if (circuitStop || k.Closed)
                return "stopped";
            m_reagentLeft = m_reagentLeft.Add(key);
            Note("Reagents: the pile at " + x + "," + y + " was not picked up in " + ReagentPickupWaitMs / 1000
                + " s (not on the pickup's list?); left");
        }
        return "stopped";
    }
    public volatile string ReagentStatus = "";
}
