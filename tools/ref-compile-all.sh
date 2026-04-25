#!/bin/bash
set -u
cd "$(dirname "$0")/.."
DOTNET="/c/Program Files/dotnet/dotnet.exe"
CLI="tools/Codex.Cli/bin/Debug/net8.0/Codex.Cli.dll"
OUTROOT=build-output/probes/samples-ref
mkdir -p "$OUTROOT"
PASS=0
FAIL=0
FAILED=()
for f in samples/*.codex; do
  name=$(basename "$f" .codex)
  out="$OUTROOT/$name"
  mkdir -p "$out"
  if "$DOTNET" "$CLI" build "$f" --target x86-64-bare --output-dir "$out" > "$out/build.log" 2>&1; then
    PASS=$((PASS+1))
  else
    FAIL=$((FAIL+1))
    FAILED+=("$name")
  fi
done
echo "PASS=$PASS  FAIL=$FAIL"
for n in "${FAILED[@]}"; do echo "  FAIL: $n"; done
