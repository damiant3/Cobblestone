using System.Diagnostics;
using Codex.Core;

namespace Codex.Cli;

// Per-phase wall-time accumulator gated on Program.s_emitPhaseTimings.
// Reset before a compile, Time(name, body) around each phase call, Flush
// after the last phase emits one "PHASE <name> <ms>" line per accumulator
// key to stderr. Per-phase totals sum across multiple calls in the same
// compile (e.g. desugar inside a per-file loop accumulates). Output order
// is pinned to s_phaseOrder so consumers that parse by position (e.g. the
// Bootstrap-Codex bench mode under construction) see stable line layout.
internal static class PhaseTimer
{
    static readonly string[] s_phaseOrder =
        ["lex", "parse", "desugar", "scope", "resolve", "check", "lower", "emit"];

    static ValueMap<string, double> s_totals = ValueMap<string, double>.s_empty;

    public static T Time<T>(string name, Func<T> body)
    {
        Stopwatch sw = Stopwatch.StartNew();
        T result = body();
        sw.Stop();
        Add(name, sw.Elapsed.TotalMilliseconds);
        return result;
    }

    public static void Time(string name, Action body)
    {
        Stopwatch sw = Stopwatch.StartNew();
        body();
        sw.Stop();
        Add(name, sw.Elapsed.TotalMilliseconds);
    }

    public static void Reset() => s_totals = ValueMap<string, double>.s_empty;

    public static void Flush()
    {
        if (!Program.s_emitPhaseTimings) return;
        foreach (string name in s_phaseOrder)
        {
            if (s_totals.TryGet(name, out double ms))
                Console.Error.WriteLine($"PHASE {name} {ms.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
        }
    }

    static void Add(string name, double ms)
    {
        double prev = s_totals.Get(name, 0.0);
        s_totals = s_totals.Set(name, prev + ms);
    }
}
