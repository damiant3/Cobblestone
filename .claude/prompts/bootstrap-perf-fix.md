Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md. Sync with origin master — previous commits on proofreading-5 have been merged.

## Problem

The self-hosted compile function in main.codex now includes AST-level chapter scoping
(scope-achapter) before type checking. This produces correct output — zero C# compile
errors in stage2. But the compile function hangs or takes 10+ minutes (was 2 seconds)
due to O(n²) or worse performance in the scoping pass.

Branch: proofreading-5 (commit dcfb0cc)
Files: Codex.Codex/Semantics/ChapterScoper.codex, Codex.Codex/main.codex

## The compile pipeline (main.codex line 12)

    tokenize → scan-document → build-all-assignments → find-colliding-names →
    parse-document → desugar-document → scope-achapter → check-chapter →
    lower-chapter → emit-full-chapter

## Where the perf problem is

scope-achapter (ChapterScoper.codex ~line 434) calls scope-adefs which:
1. For each of 1161 defs, builds a per-chapter rename map via build-chapter-rename-map
   (iterates 1220 assignments each time — cached when chapter doesn't change)
2. For each def, calls apply-cite-overrides which iterates 3 citations, each calling
   apply-cite-selected which iterates selected names and calls chapter-defines-name
   which iterates 1220 assignments
3. For each def, calls rename-aexpr which walks the entire AST expression tree

The caching on #1 (cur-slug == d.chapter-slug → reuse cur-rn) helps — only rebuilds
when chapter changes (33 times). But #2 runs per-def (1161 times) and #3 also per-def.

## Avenues to investigate

### A: Cache cite overrides per chapter
The apply-cite-overrides result only changes when d.chapter-slug changes (same as the
rename map). Move the cite override computation into the chapter-change branch so it's
computed 33 times, not 1161 times.

### B: Precompute chapter-defines-name
chapter-defines-name iterates all 1220 assignments for every selected name for every
def. Build a lookup set (chapter-slug → Set of def-names) once at the start.

### C: Check rename-aexpr tree walker
rename-aexpr walks every AExpr node in every def body. For 1161 defs with complex
bodies, this could be millions of nodes. Check if the walker creates excessive
intermediate lists (list-snoc patterns creating O(n²) allocation).

### D: Profile list operations
The rename-lookup, remove-rename, and list-snoc operations use linear list scans.
With 42 colliding names and 1161 defs, each rename-lookup does up to 42 comparisons.
This should be fast but verify.

## How to test

1. Build: dotnet build tools/Codex.Cli/Codex.Cli.csproj
2. Compile project: dotnet run --project tools/Codex.Cli -- build Codex.Codex --target cs --output-dir build-output
3. Copy to Bootstrap: cp build-output/Codex.Codex.cs tools/Codex.Bootstrap/CodexLib.g.cs && sed -i '/^Codex_Codex_Codex\.main();$/d' tools/Codex.Bootstrap/CodexLib.g.cs
4. Run bootstrap: dotnet run --project tools/Codex.Cli -- bootstrap Codex.Codex
   - Stage 1 should complete in <5 seconds (currently hangs)
5. Stage 2 should build with 0 errors
6. Stage 3 should complete (fixed-point test)

## Key context

- 33 unique chapter slugs, 1161 defs, 1220 scan assignments, 42 colliding names
- 3 selective cites in main.codex (CSharpEmitter, CodexEmitter, TypeChecker)
- The cite override maps cited names to the cited chapter's mangled version
- chapter-defines-name prevents overrides for names the current chapter defines
- The streaming v2 path (compile-streaming-v2) uses per-def scoping during emit
  and completes in ~2 seconds — so the scoping logic itself isn't inherently slow
- The non-streaming compile path does scope-achapter which walks ALL defs upfront

## What NOT to do

- Do not strip prose or use ExtractCodeBlocks — the compiler handles full prose
- Do not use IR-level scoping (was tried, causes type mismatches)
- Do not use the streaming v2 path for C# output (it uses the Codex identity emitter)
- Do not add heuristics — use deterministic approaches only
