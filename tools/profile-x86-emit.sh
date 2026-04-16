#!/bin/bash
# Per-variant exclusive-time profile of the x86 emit-expr dispatch.
#
# PerfCounters.cs (checked-in) is dormant unless emit-expr is wrapped to
# call PerfCounters.Enter()/Finish(). This script injects that wrapper
# into the regenerated CodexLib.g.cs, builds the Bootstrap with
# SkipCodexRegenerate=true (so the injection isn't wiped), runs --binary,
# and restores the original file on exit.
#
# Usage: tools/profile-x86-emit.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
WINREPO="$(wslpath -m "$REPO" 2>/dev/null || echo "$REPO")"
DOTNET="${DOTNET:-/mnt/c/Program Files/dotnet/dotnet.exe}"
if [ ! -x "$DOTNET" ]; then DOTNET="dotnet"; fi

GEN="$REPO/tools/Codex.Bootstrap/CodexLib.g.cs"

# Ensure the generated file is fresh (regen can only happen during a full build).
echo "  [1/4] fresh build..."
"$DOTNET" build "$WINREPO/tools/Codex.Bootstrap/Codex.Bootstrap.csproj" -c Release > /tmp/profile-x86-emit-build1.log 2>&1 \
    || { echo "FAIL: initial build"; cat /tmp/profile-x86-emit-build1.log; exit 1; }

# Back up and arrange to restore on any exit path.
cp "$GEN" "$GEN.profile.bak"
trap 'mv "$GEN.profile.bak" "$GEN" 2>/dev/null || true' EXIT

echo "  [2/4] inject emit-expr wrapper..."
SIG='    public static EmitResult emit__x86_64_code_generator_emit_expr(CodegenState st, IRExpr expr)'
IMPL_SIG='    public static EmitResult emit__x86_64_code_generator_emit_expr_impl(CodegenState st, IRExpr expr)'
if grep -qF "$IMPL_SIG" "$GEN"; then
    echo "FAIL: wrapper already present (CodexLib.g.cs has _impl); aborting."
    exit 1
fi
if ! grep -qF "$SIG" "$GEN"; then
    echo "FAIL: emit-expr signature not found in $GEN"
    exit 1
fi

# Rename the existing function to _impl, then insert the wrapper in front of it.
sed -i "s|${SIG}|${IMPL_SIG}|" "$GEN"
sed -i "/${IMPL_SIG}/i\\
${SIG}\\
    {\\
        int _myDepth = PerfCounters.Enter();\\
        long _bytesBefore = st.text_len;\\
        long _t0 = System.Diagnostics.Stopwatch.GetTimestamp();\\
        EmitResult _r = emit__x86_64_code_generator_emit_expr_impl(st, expr);\\
        long _t1 = System.Diagnostics.Stopwatch.GetTimestamp();\\
        PerfCounters.Finish(expr, _r.state.text_len - _bytesBefore, _t1 - _t0, _myDepth);\\
        return _r;\\
    }\\
" "$GEN"

echo "  [3/4] build with wrapper (SkipCodexRegenerate)..."
"$DOTNET" build "$WINREPO/tools/Codex.Bootstrap/Codex.Bootstrap.csproj" -c Release -p:SkipCodexRegenerate=true > /tmp/profile-x86-emit-build2.log 2>&1 \
    || { echo "FAIL: instrumented build"; cat /tmp/profile-x86-emit-build2.log; exit 1; }

echo "  [4/4] run --binary and report:"
echo ""
"$DOTNET" run --project "$WINREPO/tools/Codex.Bootstrap" -c Release --no-build -- --binary \
    | grep -v -E "^\s+\[[0-9]+\] (emit-field-access|Unresolved)" \
    || true
