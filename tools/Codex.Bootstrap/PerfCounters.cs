using System;
using System.Collections.Generic;

// Host-side profile counters for the x86 emit-expr dispatch. Dormant unless
// the emit-expr wrapper that calls Enter/Finish is installed (see
// tools/profile-x86-emit.sh). Reset/Report are always safe to call.
public static class PerfCounters
{
    public static long ExprCalls;
    public static long ExprMaxDepth;
    static int s_depth;
    static long[] s_childTicks = new long[256];

    public static Dictionary<string, long> VariantCalls = new();
    public static Dictionary<string, long> VariantSumBytes = new();
    public static Dictionary<string, long> VariantMaxBytes = new();
    public static Dictionary<string, long> VariantSumTicks = new();

    public static void Reset()
    {
        ExprCalls = 0; ExprMaxDepth = 0; s_depth = 0;
        Array.Clear(s_childTicks, 0, s_childTicks.Length);
        VariantCalls.Clear(); VariantSumBytes.Clear();
        VariantMaxBytes.Clear(); VariantSumTicks.Clear();
    }

    // Called at the top of the wrapped emit-expr. Returns this call's depth
    // so Finish can read back its child-ticks accumulator slot.
    public static int Enter()
    {
        s_depth++;
        if (s_depth > ExprMaxDepth) ExprMaxDepth = s_depth;
        if (s_depth < s_childTicks.Length) s_childTicks[s_depth] = 0;
        return s_depth;
    }

    // Called after the wrapped emit-expr impl returns. Records this call's
    // exclusive time (totalTicks minus accumulated child ticks), credits the
    // parent's child-ticks with this call's total, then pops.
    public static void Finish(object expr, long bytesDelta, long totalTicks, int myDepth)
    {
        long childTicks = (myDepth >= 0 && myDepth < s_childTicks.Length) ? s_childTicks[myDepth] : 0;
        long excl = totalTicks - childTicks;
        string v = expr.GetType().Name;

        ExprCalls++;
        VariantCalls[v] = VariantCalls.TryGetValue(v, out var c) ? c + 1 : 1;
        VariantSumBytes[v] = VariantSumBytes.TryGetValue(v, out var s) ? s + bytesDelta : bytesDelta;
        if (!VariantMaxBytes.TryGetValue(v, out var mx) || bytesDelta > mx) VariantMaxBytes[v] = bytesDelta;
        VariantSumTicks[v] = VariantSumTicks.TryGetValue(v, out var t) ? t + excl : excl;

        s_depth--;
        if (s_depth >= 1 && s_depth < s_childTicks.Length) s_childTicks[s_depth] += totalTicks;
    }

    public static void Report()
    {
        if (ExprCalls == 0) { return; }
        Console.WriteLine();
        Console.WriteLine("=== x86 emit-expr profile (exclusive time) ===");
        Console.WriteLine($"Total calls: {ExprCalls:N0}   max depth: {ExprMaxDepth}");
        Console.WriteLine();
        Console.WriteLine($"  {"variant",-20} {"calls",10} {"sum-bytes",13} {"max-bytes",10} {"avg-bytes",10} {"sum-ms",10} {"avg-us",8}");
        Console.WriteLine($"  {new string('-', 20)} {new string('-', 10)} {new string('-', 13)} {new string('-', 10)} {new string('-', 10)} {new string('-', 10)} {new string('-', 8)}");
        var rows = new List<(string v, long calls, long sb, long mb, long st)>();
        foreach (var kv in VariantCalls)
        {
            rows.Add((kv.Key, kv.Value, VariantSumBytes[kv.Key], VariantMaxBytes[kv.Key], VariantSumTicks[kv.Key]));
        }
        rows.Sort((a, b) => b.st.CompareTo(a.st));
        double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
        double ticksPerUs = System.Diagnostics.Stopwatch.Frequency / 1_000_000.0;
        long totalExclTicks = 0;
        foreach (var r in rows) totalExclTicks += r.st;
        Console.WriteLine($"  (total exclusive time: {totalExclTicks / ticksPerMs:F1} ms)");
        foreach (var r in rows)
        {
            long avgB = r.calls == 0 ? 0 : r.sb / r.calls;
            double ms = r.st / ticksPerMs;
            double avgUs = r.calls == 0 ? 0 : (r.st / ticksPerUs) / r.calls;
            Console.WriteLine($"  {r.v,-20} {r.calls,10:N0} {r.sb,13:N0} {r.mb,10:N0} {avgB,10:N0} {ms,10:F1} {avgUs,8:F1}");
        }
    }
}
