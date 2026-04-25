#!/bin/bash
# Per-variant exclusive-time profile of the C# emit-expr dispatch.
#
# Mirrors profile-x86-emit.sh but targets the C#-emission path (used for
# Bootstrap 1/1.1 and the pre-binary-mode --bench workload). Injects a
# wrapper around emit__csharp_emitter_emit_expr that records per-variant
# exclusive time via PerfCounters, then runs the default single-shot
# compile and restores the file.
#
# Usage: tools/profile-csharp-emit.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
WINREPO="$(wslpath -m "$REPO" 2>/dev/null || echo "$REPO")"
DOTNET="${DOTNET:-/mnt/c/Program Files/dotnet/dotnet.exe}"
if [ ! -x "$DOTNET" ]; then DOTNET="dotnet"; fi

GEN="$REPO/tools/Codex.Bootstrap/CodexLib.g.cs"
PROG="$REPO/tools/Codex.Bootstrap/Program.cs"

echo "  [1/5] fresh build..."
"$DOTNET" build "$WINREPO/tools/Codex.Bootstrap/Codex.Bootstrap.csproj" -c Release > /tmp/profile-csharp-emit-build1.log 2>&1 \
    || { echo "FAIL: initial build"; cat /tmp/profile-csharp-emit-build1.log; exit 1; }

cp "$GEN" "$GEN.profile.bak"
cp "$PROG" "$PROG.profile.bak"
trap 'mv "$GEN.profile.bak" "$GEN" 2>/dev/null || true; mv "$PROG.profile.bak" "$PROG" 2>/dev/null || true' EXIT

echo "  [2/5] inject emit-expr wrapper..."
SIG='    public static string emit__csharp_emitter_emit_expr(IRExpr e, List<ArityEntry> arities)'
IMPL_SIG='    public static string emit__csharp_emitter_emit_expr_impl(IRExpr e, List<ArityEntry> arities)'
if grep -qF "$IMPL_SIG" "$GEN"; then
    echo "FAIL: wrapper already present; aborting."
    exit 1
fi
if ! grep -qF "$SIG" "$GEN"; then
    echo "FAIL: emit-expr signature not found in $GEN"
    exit 1
fi

sed -i "s|${SIG}|${IMPL_SIG}|" "$GEN"
sed -i "/${IMPL_SIG}/i\\
${SIG}\\
    {\\
        int _myDepth = PerfCounters.Enter();\\
        long _t0 = System.Diagnostics.Stopwatch.GetTimestamp();\\
        string _r = emit__csharp_emitter_emit_expr_impl(e, arities);\\
        long _t1 = System.Diagnostics.Stopwatch.GetTimestamp();\\
        PerfCounters.Finish(e, (long)_r.Length, _t1 - _t0, _myDepth);\\
        return _r;\\
    }\\
" "$GEN"

echo "  [3/5] add Reset/Report to default compile path..."
python3 -c "print('no python')" 2>/dev/null || true
# Insert PerfCounters.Reset() before the first [1/11] tokenize line and
# PerfCounters.Report() after the Total line.
sed -i 's|Console.WriteLine("  \[1/11\] tokenize\.\.\.");|PerfCounters.Reset(); &|' "$PROG"
sed -i 's|Console.WriteLine(\$"Total: {sw.ElapsedMilliseconds}ms");|& PerfCounters.Report();|' "$PROG"

echo "  [4/5] build with wrapper (SkipCodexRegenerate)..."
"$DOTNET" build "$WINREPO/tools/Codex.Bootstrap/Codex.Bootstrap.csproj" -c Release -p:SkipCodexRegenerate=true > /tmp/profile-csharp-emit-build2.log 2>&1 \
    || { echo "FAIL: instrumented build"; cat /tmp/profile-csharp-emit-build2.log; exit 1; }

echo "  [5/5] run default compile and report:"
echo ""
"$DOTNET" run --project "$WINREPO/tools/Codex.Bootstrap" -c Release --no-build \
    || true
