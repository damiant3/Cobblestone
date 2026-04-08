#!/bin/bash
# linux-bootstrap.sh — Full bootstrap verification from scratch.
#
# Proves: Stage 2 output == Stage 3 output (fixed point).
#
# Stages:
#   Stage 0: Reference C# compiler (src/) compiles .codex → out/Codex.Codex.cs
#   Stage 1: Bootstrap binary (built from Stage 0 output) compiles .codex → stage1-output.cs
#   Stage 2: Bootstrap binary (rebuilt from Stage 1 output) compiles .codex → stage3-output.cs
#   Fixed point: stage1-output.cs == stage3-output.cs
#
# Usage:
#   bash tools/linux-bootstrap.sh              — full verification
#   bash tools/linux-bootstrap.sh --skip-build — skip initial solution build
#   bash tools/linux-bootstrap.sh --stage1-only — stop after Stage 1

set -e

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

CODEX_DIR="$REPO_ROOT/Codex.Codex"
BOOTSTRAP_DIR="$REPO_ROOT/tools/Codex.Bootstrap"
CLI_DLL="$REPO_ROOT/tools/Codex.Cli/bin/Debug/net8.0/Codex.Cli.dll"
BOOTSTRAP_DLL="$BOOTSTRAP_DIR/bin/Debug/net8.0/Codex.Bootstrap.dll"

S0_OUTPUT="$CODEX_DIR/out/Codex.Codex.cs"
S1_OUTPUT="$CODEX_DIR/stage1-output.cs"
S3_OUTPUT="$CODEX_DIR/stage3-output.cs"
CODEXLIB="$BOOTSTRAP_DIR/CodexLib.g.cs"

SKIP_BUILD=false
STAGE1_ONLY=false

for arg in "$@"; do
    case "$arg" in
        --skip-build)  SKIP_BUILD=true ;;
        --stage1-only) STAGE1_ONLY=true ;;
        -h|--help)
            echo "Usage: bash tools/linux-bootstrap.sh [--skip-build] [--stage1-only]"
            exit 0
            ;;
    esac
done

# ── Helpers ──

DIM='\033[90m'
GREEN='\033[92m'
YELLOW='\033[93m'
RED='\033[91m'
BOLD='\033[1m'
CYAN='\033[96m'
RESET='\033[0m'

step() { echo -e "\n${BOLD}${CYAN}▶ $1${RESET}"; }
ok()   { echo -e "  ${GREEN}✓ $1${RESET}"; }
warn() { echo -e "  ${YELLOW}⚠ $1${RESET}"; }
fail() { echo -e "  ${RED}✗ $1${RESET}"; }

elapsed() {
    local secs=$1
    if [ "$secs" -ge 60 ]; then
        echo "$((secs / 60))m $((secs % 60))s"
    else
        echo "${secs}s"
    fi
}

OVERALL_START=$SECONDS

# ═══════════════════════════════════════════════════════════════
# STEP 1: Build the solution
# ═══════════════════════════════════════════════════════════════

if ! $SKIP_BUILD; then
    step "Step 1: Building Codex.sln"
    START=$SECONDS
    if dotnet build Codex.sln -v q 2>&1 | tail -5; then
        ok "Solution built ($(elapsed $((SECONDS - START))))"
    else
        fail "Solution build failed"
        exit 1
    fi
else
    step "Step 1: Skipped (--skip-build)"
fi

# ═══════════════════════════════════════════════════════════════
# STEP 2: Stage 0 — Reference compiler compiles .codex
# ═══════════════════════════════════════════════════════════════

step "Step 2: Stage 0 — Reference compiler compiles .codex sources"
START=$SECONDS

if [ ! -f "$CLI_DLL" ]; then
    fail "Codex.Cli.dll not found at: $CLI_DLL"
    echo "  Run 'dotnet build Codex.sln' first."
    exit 1
fi

dotnet "$CLI_DLL" build "$CODEX_DIR" 2>&1

if [ ! -f "$S0_OUTPUT" ]; then
    fail "Stage 0 output not generated: $S0_OUTPUT"
    exit 1
fi

S0_CHARS=$(wc -c < "$S0_OUTPUT")
S0_LINES=$(wc -l < "$S0_OUTPUT")
ok "Stage 0 output: $S0_LINES lines, $S0_CHARS chars ($(elapsed $((SECONDS - START))))"

# ═══════════════════════════════════════════════════════════════
# STEP 3: Build Stage 1 binary
# ═══════════════════════════════════════════════════════════════

step "Step 3: Building Stage 1 binary (Codex.Bootstrap from Stage 0 output)"
START=$SECONDS

# The csproj StripEntryPoint target handles creating CodexLib.g.cs via sed on Linux
dotnet build "$BOOTSTRAP_DIR/Codex.Bootstrap.csproj" -v q 2>&1 | tail -3

if [ ! -f "$BOOTSTRAP_DLL" ]; then
    fail "Bootstrap DLL not found: $BOOTSTRAP_DLL"
    exit 1
fi

ok "Stage 1 binary built ($(elapsed $((SECONDS - START))))"

# ═══════════════════════════════════════════════════════════════
# STEP 4: Stage 1 — Self-hosted compiler compiles .codex
# ═══════════════════════════════════════════════════════════════

step "Step 4: Stage 1 — Self-hosted compiler compiles .codex sources"
START=$SECONDS

dotnet "$BOOTSTRAP_DLL" "$CODEX_DIR" 2>&1

if [ ! -f "$S1_OUTPUT" ]; then
    fail "Stage 1 output not generated: $S1_OUTPUT"
    exit 1
fi

S1_CHARS=$(wc -c < "$S1_OUTPUT")
S1_LINES=$(wc -l < "$S1_OUTPUT")
ok "Stage 1 output (= Stage 2): $S1_LINES lines, $S1_CHARS chars ($(elapsed $((SECONDS - START))))"

if $STAGE1_ONLY; then
    step "Stopping after Stage 1 (--stage1-only)"
    echo ""
    echo -e "  Stage 0: ${S0_CHARS} chars"
    echo -e "  Stage 2: ${S1_CHARS} chars"
    echo -e "  Total: $(elapsed $((SECONDS - OVERALL_START)))"
    exit 0
fi

# ═══════════════════════════════════════════════════════════════
# STEP 5: Build Stage 2 binary (from Stage 1 output)
# ═══════════════════════════════════════════════════════════════

step "Step 5: Building Stage 2 binary (Codex.Bootstrap from Stage 1 output)"
START=$SECONDS

# Back up the current CodexLib.g.cs
cp "$CODEXLIB" "$CODEXLIB.bak"

# Strip main() from Stage 1 output and use as the new CodexLib
sed 's/^Codex_Codex_Codex\.main();$//' "$S1_OUTPUT" > "$CODEXLIB"

# Rebuild Bootstrap with Stage 1 output
dotnet build "$BOOTSTRAP_DIR/Codex.Bootstrap.csproj" -v q --no-dependencies 2>&1 | tail -3

ok "Stage 2 binary built ($(elapsed $((SECONDS - START))))"

# ═══════════════════════════════════════════════════════════════
# STEP 6: Stage 2 — Stage 2 binary compiles .codex → Stage 3
# ═══════════════════════════════════════════════════════════════

step "Step 6: Stage 2 — Stage 2 binary compiles .codex sources"
START=$SECONDS

# Stage 2 output goes to a temp location, then we copy
S2_TEMP="$CODEX_DIR/stage1-output.cs"
S2_BACKUP="$S1_OUTPUT.s2bak"

# Back up the Stage 1 output (Stage 2 will overwrite stage1-output.cs)
cp "$S1_OUTPUT" "$S2_BACKUP"

dotnet "$BOOTSTRAP_DLL" "$CODEX_DIR" 2>&1

# The bootstrap writes to stage1-output.cs, so copy it as stage3
cp "$S2_TEMP" "$S3_OUTPUT"

# Restore the Stage 1 output
cp "$S2_BACKUP" "$S1_OUTPUT"
rm -f "$S2_BACKUP"

S3_CHARS=$(wc -c < "$S3_OUTPUT")
S3_LINES=$(wc -l < "$S3_OUTPUT")
ok "Stage 2 output (= Stage 3): $S3_LINES lines, $S3_CHARS chars ($(elapsed $((SECONDS - START))))"

# ═══════════════════════════════════════════════════════════════
# STEP 7: Restore CodexLib.g.cs and verify fixed point
# ═══════════════════════════════════════════════════════════════

step "Step 7: Restoring CodexLib.g.cs and verifying fixed point"

# Restore original CodexLib.g.cs (from Stage 0 output)
cp "$CODEXLIB.bak" "$CODEXLIB"
rm -f "$CODEXLIB.bak"

# Rebuild Bootstrap back to Stage 1 state
dotnet build "$BOOTSTRAP_DIR/Codex.Bootstrap.csproj" -v q --no-dependencies 2>&1 | tail -1

ok "CodexLib.g.cs restored to Stage 0 state"

# ═══════════════════════════════════════════════════════════════
# RESULTS
# ═══════════════════════════════════════════════════════════════

echo ""
echo -e "${BOLD}═══ BOOTSTRAP RESULTS ═══${RESET}"
echo ""
echo -e "  Stage 0 (ref compiler):    ${S0_CHARS} chars, ${S0_LINES} lines"
echo -e "  Stage 2 (self-hosted):     ${S1_CHARS} chars, ${S1_LINES} lines"
echo -e "  Stage 3 (self-compiling):  ${S3_CHARS} chars, ${S3_LINES} lines"
echo ""

if diff -q "$S1_OUTPUT" "$S3_OUTPUT" > /dev/null 2>&1; then
    echo -e "  ${GREEN}${BOLD}✓ FIXED POINT ACHIEVED${RESET}"
    echo -e "  ${GREEN}  Stage 2 == Stage 3 ($S1_CHARS chars, byte-for-byte)${RESET}"
else
    echo -e "  ${RED}${BOLD}✗ FIXED POINT BROKEN${RESET}"
    echo ""
    DELTA_CHARS=$((S1_CHARS - S3_CHARS))
    echo -e "  ${RED}  Char delta: ${DELTA_CHARS}${RESET}"
    echo ""
    echo "  First 20 differing lines:"
    diff --unified=0 "$S1_OUTPUT" "$S3_OUTPUT" | head -40
fi

# Type quality metrics
S0_OBJECTS=$(grep -o '\bobject\b' "$S0_OUTPUT" | wc -l)
S0_P0=$(grep -o '_p0_' "$S0_OUTPUT" | wc -l)
echo ""
echo -e "  Type quality (Stage 0 output):"
echo -e "    object refs:  $S0_OBJECTS"
echo -e "    _p0_ proxies: $S0_P0"

echo ""
echo -e "  ${DIM}Total time: $(elapsed $((SECONDS - OVERALL_START)))${RESET}"
echo ""
