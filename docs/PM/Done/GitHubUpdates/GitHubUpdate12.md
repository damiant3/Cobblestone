# GitHub Update 12 — CL 1170 to CL 1300 (2026-05-09)

Previous update: CL 1168 (GitHubUpdate11).
This update: CL 1300.

Two days, ~130 changes, three agents (Cam, Nib, Pip) plus Damian.
The headline is that the seed compiler stopped silently corrupting
itself when an emit-time diagnostic outlived its bivy. The
load-bearing pieces are Nib's de-deck campaign across the frontend
phases, Cam's resilient `trying / falling back to / on failure`
language feature, Pip's plug architecture (emitters as standalone
CDX programs), and a final dangler fix in `make-diagnostic` that
unblocked the plug bundles.

## Pip joins (CL 1192)

Third Claude Code agent joins Cam + Nib. Workspace
`BigWhite_Codex_pip` at `D:\Projects\NewRepository-pip`. Agent file
`docs/Agents/Pip.txt`.

## Memory work — Nib's de-deck campaign + diagnostic dangler fix

The frontend phases used to allocate scratch on the deck (long-lived)
when bivy (per-phase scratch) would do. Nib walked the pipeline
phase by phase, separating durable allocations from scratch and
moving the latter to bivy. The cumulative effect is a smaller
deck-end at the boundary of each phase and tighter heap surveys.

### Phase de-decking (Nib)

- **CL 1179** — Lex phase: separate scratch from durable allocations.
- **CL 1188** — Parse phase: move scratch to bivy, deck-record only
  prose-text escape.
- **CL 1220** — Desugar phase: LinkedList accumulation for defs /
  type-defs / effect-defs / citations; fix desugar-annotations bivy
  escape.
- **CL 1228** — Scope phase: LinkedList accumulation; fix rename-alet
  + rename-lookup escapes.
- **CL 1236** — Resolver `ctor-names`: SkipListText to List Text at
  boundary, drop outer `deck-record`.

### Survey tightening (Nib)

- **CL 1202** — Fix cite-title bivy escape; tighten parse survey
  30x → 15x; fix lower survey 48x → 300x (was undercounting).
- **CL 1209** — Lex 11x+1M → 10x+64K; parse 15x+1M → 5x+64K.
- **CL 1210** — Lex 11x → 10x; parse 30x → 5x (keep +1M for
  small-source overhead).

### Compact disabling + scan-deck-dangling (Nib)

- **CL 1289** — Disable frontend phase compacts; add
  `scan-deck-dangling` diagnostic; seed rebuild (1,998,008 bytes).
  Frontend phases used to call `phase-compact` after each phase, but
  compaction was rewriting pointers in deck records that were still
  live across the phase boundary.
- **CL 1294** — Re-enable lex / desugar / scope / lower compacts
  (parse / check stay off); enhanced `scan-phase-pre-compact`
  diagnostic; seed rebuild (1,998,032 bytes).

### Defensive deck-record (Nib)

- **CL 1239** — Fix CRLF in `.expected` files + defensive
  `deck-record` in `Unifier` and `TypeCheckerInference`.

### Diagnostic dangler (Pip, CL 1300)

After `compile-frontend` succeeds, the emitter calls
`__heap-restore` per function to reclaim bivy. Any
`st-add-error` during emit creates a `Diagnostic` whose `message`
is a bivy-allocated `Text` (built with `++`). The `Diagnostic`
record itself is `deck-record`-tagged, but the message field
holds a bivy pointer. After `__heap-restore` the message
dangles. `bag-merge-all` later iterates the diagnostics, reads
dead bytes, and crashes with the BIOS-area sentinel pattern
(`0xf000ff53...`).

Fix in `codex/Core/Diagnostic.codex`: pass `msg` through
`substring msg 0 (text-length msg)` inside the surrounding
`deck-record` so the bytes get freshly allocated on the deck.
Companion fix in `codex/Core/DiagnosticBag.codex`:
`deck-record`-wrap `bag-add` and `bag-add-error` so the new
`DiagnosticBag` record and the cons cell that prepends each
diagnostic land on the deck too.

For well-typed programs this never fired — emit produces no
diagnostics. Cam's reduced plug bundles tripped it because they
trigger emit-time `unresolved-type` errors against `FuncOffset`
(which used to leak in via OffsetTable). Result: clean error
message instead of a register dump.

## Trying / falling back to / on failure (Cam, CL 1298)

New language construct for resilient effectful operations.

```codex
trying 3 times
  result <- net-fetch
  when result.ok
    is True -> result
    is False -> fail "retry"
falling back to
  default-payload
on failure
  print-line "permanently failed"
end
```

- New `TryingKeyword` + `is-trying-keyword` parser hook.
- `TryExpr` / `ATryExpr` / `IrTry` through Token → SyntaxNodes →
  Desugarer → AstNodes → Lowering → IRChapter → ChapterScoper →
  LambdaLifting → X86_64Chapter → X86_64 → CodexEmitter.
- New `fail` builtin: `Text -> a`. Sets `try-fail-flag-addr` (boot
  page slot 30064), returns 0.
- Backend emits a retry loop with `__heap-save` at entry,
  `__heap-restore` between attempts so each iteration's bivy
  allocations are reclaimed.
- Codex text emitter round-trips the syntax.
- Pingpong: text + CDX fixed point both byte-identical.

Design doc: `docs/Designs/Features/RESILIENT-ACT-BLOCKS.md` (CL 1292).

## Plug architecture (Pip, CL 1300)

Emitters move out of the compiler core and become standalone CDX
programs that consume IR text on stdin and emit target source on
stdout.

### Compiler-side IR text emit mode

- `codex/Emit/IRTextEmitter.codex` — new chapter, S-expression
  serializer for IRChapter + ATypeDef. Includes `safe-int-text` and
  `ir-emit-int-bounds` to short-circuit the i64-min `__itoa` overflow.
- `codex/opening.codex` — cite + `emit-ir` + `IR` mode dispatch.
- `codex.build/sample-compile-selfhost.ps1 -Ir` — captures IR text
  between `IR-BEGIN` / `IR-END` sentinels and writes to `$Out`.

### Plug-side scaffold (template for further emitters)

- `plugs/common/IRTextParser.codex` — shared `Text → ParsedIR`
  parser (S-expressions → `IRChapter` + `List ATypeDef`).
- `plugs/csharp/CSharpEmitter.codex`, `CSharpEmitterExpressions.codex`,
  `CSharpPlug.codex` — recovered C# emitter, ported into the plug.
- `plugs/csharp/build.ps1` — bundles full `codex/` tree + plug code,
  compiles via the seed in CDX mode → 2.36 MB plug binary.
- `plugs/csharp/run.ps1` — pipeline: Codex source → IR (via
  selfhost-compile) → plug stdin → C# stdout.
- `codex.test/apps/ir-probe.codex` — probe sample exercising
  literals, binary ops, records, variants, `when` / `match`, `let`,
  `if`, `act`. Expected: `hello Codex sum=31`.

### OffsetTable cleanup

`codex/Core/OffsetTable.codex` had a dead `build-offset-table : List
FuncOffset` that pulled `FuncOffset` (defined in
`codex/Emit/X86_64State.codex`) into Core's surface. Any plug-style
bundle that included Core but not Emit emitted unresolved-type
errors against it. Removed; the only live builder is
`build-offset-table-parallel` in `X86_64Chapter.codex`.

## UEFI dev console (Cam, CLs 1171-1233)

Interactive colored menus when you hold Escape / F12 at UEFI boot.
Replaces the earlier "boot to compiler" flow with a menu that lets
you browse the source tree, edit, view system info, etc.

- **CL 1171** — IMG mode `uefi` flag selects
  `build-uefi-app-pe-from-cdx`.
- **CL 1172** — README flash instructions (`tools\write-usb.ps1`).
- **CL 1177** — Boot path: ConOut routing.
- **CL 1181** — Source browsing + FAT16 disk read.
- **CL 1186** — Seed rebuild: dedupe finalize, `heap-pages` param,
  `uefi-read-key-ex` builtin.
- **CL 1187** — Source browser, viewer, ctrl-alt-del reboot.
- **CL 1189** — RTC clock; revert wait-key to basic
  `uefi-read-key`.
- **CL 1197** — Heap collision: `AllocateAnyPages` for heap, save
  R10 across UEFI calls.
- **CL 1203** — Seed rebuild merging Nib parser fix + Cam UEFI heap
  fix.
- **CL 1223** — `WaitForEvent` keyboard, remove `ClearScreen` heap
  corruption, TCO `wait-key` spin.
- **CL 1232** — Single-write header bar (no flicker).
- **CL 1233** — Live clock (BS→Stall polling), poll-key loop.
- **CL 1262** — Pip stability sweep: `&` operator bombs (Codex has
  no `&`), wait-key redraw, alloc pressure.

## Library gap sweep (Cam, CLs 1249-1271)

A push to fill in the foreword library where samples were exercising
patterns the libraries didn't cover.

- **CL 1249** — Path, Format, Hkdf, Deflate, Gzip, NumberTheory,
  Filter, Probability.
- **CL 1250** — AesGcm, X25519, Locale, Selection, TextField,
  Clipboard, Constraint, SpatialHash.
- **CL 1251** — LinearAlgebra, Toml, Cbor, Resample, Vector,
  Attention, Embedding, Loss.
- **CL 1252** — Transformer, KvCache, Sampling, Optimizer, Numeric,
  Drag, RichText, Lz4.
- **CL 1253** — Decimal, Argon2, Zstd, Yaml, MessagePack, Touch,
  Charts, Wavelet.
- **CL 1254** — Probes for Path, Format, Hkdf, NumberTheory,
  Deflate, Gzip, Cbor, Decimal, AesGcm.
- **CL 1257** — Bridges (Tls, Render, Animation, FileSystem) +
  Accessibility, Window, Pitch, Kinematics, Camera, Inventory.
- **CL 1258** — SaveSlot, Netcode, Optimize, Brotli, Unicode +
  Widget / Layout / Dialog deepening.
- **CL 1259** — Review fixes: Window shadow, Camera rename,
  Kinematics atan2, Brotli prose, Netcode input queue.
- **CL 1271** — Library compile fixes: Tls duplicate
  `tls-text-to-bytes`, Render `fb-set-pixel` → `fb-set`, Dialog
  TypedDialog mismatch.

## Annotations (Pip, CLs 1199-1243)

First-class fact-publication surface for AI agents and tools to
annotate code without modifying it. Annotations are facts about
code, distinct from edits.

- **CL 1199-1201, 1204** — Design + addendum (rename
  `annotations/` → `codex.annotations/` to match the top-level
  Codex tree convention; ground addendum in
  `docs/Stories/Vision/NewRepository.txt` +
  `IntelligenceLayer.txt`).
- **CL 1221** — H1-H12 integration: full surface (AnnotationFact,
  RepoState, EditorNotifications, EditorVerdicts, Keypair,
  TrustLattice, threshold). Each phase exercised by a probe in
  `codex.test/apps/`.
- **CL 1224** — Cleanup follow-ups (drop dead RepoFactKind,
  consolidate constructors).
- **CL 1226** — `AnnotationDriver` coordination chapter.
- **CL 1229** — JSON parser (closed the chapter intro promise of
  "emitter and parser"; prior to this, only the emitter existed).
- **CL 1230** — `AnnotationsSidecar` reader (loads on-disk
  annotation sidecars from `codex.annotations/<chapter>.json`).
- **CL 1231** — Sidecar reader handles `discussion` entries;
  routes through `AnnotationStore.discuss()` instead of dropping.
- **CL 1243** — `MsgAnnotate` + `MsgVerdict` on `AgentMessage` +
  Works pump. Annotations as first-class transport, distinct from
  edits.

## text-slice substring sweep (Cam, CLs 1264-1266)

Replace char-by-char `text-slice-loop` accumulation with the
`substring` builtin across forewords, OS chapters, and Works
chapters. Significant heap and time wins on text-heavy code paths.

## Docs (Pip, Nib)

- **CL 1192** — Register Pip agent.
- **CL 1193** — Refresh stale module / sample / LOC counts;
  retired-tooling references.
- **CL 1194** — Rewrite `00-OVERVIEW.md` for the post-MM4 era.
- **CL 1195** — Refresh `10-PRINCIPLES.md`.
- **CL 1196** — Gate-failure response codified: shelve + notify +
  re-evaluate; not "back out".
- **CL 1198-1200** — `GpuKernels` design (CUDA PTX backend);
  `LibraryGapAnalysis` survey.
- **CL 1204** — Annotations addendum, founding-vision-grounded.
- **CL 1222** — Parser bug doc: `[list] ++ when ... ++ [list]`
  chains mis-parse to CDX1023.
- **CL 1268** — Rewrite `CurrentPlan` as forward-only "Closing the
  Toolbox" gap list; collapse `CurrentSubPlan` to a redirect.
- **CL 1269** — `KNOWN-CONDITIONS` audit: drop CS2001 (post-cord-cut),
  add ClearScreen condition, refresh parser-bug section.
- **CL 1275** — Docs cleanup: merge quick refs into
  `DevelopersHandbook`; delete copilot/codex-agent artifacts;
  merge `Design` into `Designs`.

## Seed

- **`seed/Codex.cdx`** — 2,089,224 bytes (Pip, CL 1300).
- **`seed/Codex.img`** — 8,388,608 bytes (8 MB FAT16 GPT, signed
  UEFI dev console).
- Compiler: 57 files, ~25,150 lines.
- Modules: 352 across 19 quires (excluding 4 plug files).
- Test samples: 401 (144 core + 210 apps + 29 errors + 18 lib).
- Pingpong: text round-trip + CDX fixed-point both byte-identical
  on the new seed.
