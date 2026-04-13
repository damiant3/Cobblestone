# Self-Host Parity Audit

## Why this exists

The self-hosted compiler (`Codex.Codex/`) is supposed to be modeled on the
reference compiler (`src/`). The reference is the source of truth for what
Codex *should* do. The self-host is supposed to match it, feature for feature,
so we can eventually cut the cord and not lose anything.

In practice, large chunks of the reference's foundational infrastructure
never made it into the self-host. We keep discovering these gaps one at a
time, painfully, often at the moment we most need the missing feature.

This document is the running inventory — green rows are in parity, yellow
rows are partially there, red rows are open gaps, and every row has an owner
or a note so future-Hex doesn't trip over the same rake twice.

## What this is not

Not a call to backport everything from the reference. Some reference
features are dumb and we want to replace them. That's fine — document the
decision in the "Notes" column.

## Parity matrix

Legend: ✅ at parity · 🟡 partial / different · ❌ missing · ⏭️ deliberately diverged

### Data structures

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| `List` | ✓ | ✓ | ✅ | Mutable-via-heap-top, geometric growth, `list-set-at` in-place (post-P1) |
| `TextSet` (sorted) | ✓ | ✓ `Core/Set.codex` | ✅ | O(log n) contains, O(n) insert. Used by NameResolver dup-check |
| `HamtMap` | ✓ | ✓ `Core/Hamt.codex` | ✅ | 8192-slot open addressing; text + integer keys; used at link-time func-offset lookup (P5) |
| `Maybe` / `Option` | ✓ stdlib | ❌ | ❌ | Exists in `foreword/Maybe.codex` but self-host compiler code does not use it. Callers work around via sentinel pairs. |
| `LinkedList` | ✓ | 🟡 | 🟡 | Type exists; `record-set` builtin + O(1) text-chunks mutation landed (`827ce6e`). Need audit of call sites. |
| `Queue` / `Stack` | ✓ stdlib | ❌ in self-host | ❌ | `foreword/Queue.codex` present but compiler does not consume it |
| `StringBuilder` | ✓ stdlib | ❌ in self-host | ❌ | Per-char `acc ++ char-to-text c` quadratic pattern (P10/P11) was fixed via chunked accumulation rather than introducing a StringBuilder abstraction |
| `Pair` | ✓ stdlib | 🟡 ad-hoc | 🟡 | `foreword/Pair.codex` exists; self-host uses per-purpose 2-field records |
| `TextSearch` (trie) | ✓ stdlib | ❌ in self-host | ❌ | `foreword/TextSearch.codex` present but unused |
| Flat buffer / mutable memory | ✓ | ✓ | ✅ | Bare-metal heap, bump allocator, `heap-save` / `heap-advance` (documented) |

### Primitives & runtime

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| `Integer` (64-bit) | ✓ | ✓ | ✅ | `IntegerTy`, wraps modulo 2⁶⁴ |
| `Number` / float | ✓ | ✓ | ✅ | Distinct `NumberTy` / `FloatTy` in TypeEnv, IRChapter, CodexType. Runtime conversion lives on .NET only (see BACKLOG item 5). |
| `Text` / `String` (CCE) | ✓ | ✓ | ✅ | CCE internal encoding; Unicode conversion only at I/O |
| `Character` | ✓ | ✓ | ✅ | |
| `Boolean` | ✓ | ✓ | ✅ | |
| Bitwise: `bit-and`, `bit-or`, `bit-xor`, `bit-shl`, `bit-shr`, `bit-not` | ✓ | ✓ | ✅ | Full pipeline: TypeEnv, NameResolver, CSharpEmitter, X86_64 codegen (landed around `211dea3`, `b4f9f4b`) |
| Arithmetic: `abs`, `min`, `max`, `mod`, `div` negative-semantics | ✓ | 🟡 | 🟡 | Builtins exist; negative-operand mod/div semantics have not been re-verified against reference |
| CCE-ordered comparison on `Text` | ✓ | ✓ | ✅ | |

### Diagnostics & error reporting

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| `Diagnostic` record (severity, code, message, span) | ✓ | ✓ `Core/Diagnostic.codex` | ✅ | |
| `DiagnosticBag` with max-errors + truncation | ✓ | ✓ `Core/DiagnosticBag.codex` | ✅ | 20-error cap, `CDX0001` sentinel on overflow (`2004592`) |
| `Severity` (error/warning/info/hint) | ✓ | ✓ `Core/Severity.codex` | ✅ | |
| `Phase` enum | ✓ | ✓ `Core/Phase.codex` | ✅ | |
| `CdxCodes` registry (integer codes) | ✓ | ✓ `Core/CdxCodes.codex` | ✅ | (`7b00b46`) |
| `SourceSpan` with file-id + provenance | ✓ | ✓ | ✅ | Span on every AST variant, threaded through Desugarer (`7601cd8`, `dacc14c`) |
| `FileTable` chapter | ✓ | ✓ | ✅ | (`dacc14c`) |
| Parser diagnostics captured into bag | ✓ | ✓ | ✅ | `st.bag` threaded through parser (`9552b0a`, `1484914`) |
| Type-checker diagnostics | ✓ | ✓ | ✅ | Bag threaded in Phase 2 |
| Name-resolver diagnostics | ✓ | ✓ | ✅ | Bag threaded in Phase 2 |
| X86_64 codegen diagnostics with real IR span | ✓ | ✓ | ✅ | (`8cda64d`, `f56e6ac`) |
| Staged compilation with error gates | ✓ | ✓ | ✅ | Phase 4 — halt at stage boundary on errors (`0d1239d`) |
| Diagnostic-display tests per severity | ✓ | ✓ | ✅ | (`792158c`) |
| Bare-metal BINARY-DIAG mode with per-stage PH markers | n/a | ✓ | ✅ | Emit-side (`02b71e9`, `8250b89`, `04b5c26`) |
| Parser error recovery (skip-to-next-def resync) | ✓ | ❌ | ❌ | Bag captures what it can; cascading failures still happen on malformed input. **Open gap.** |

### Debugging / crash behavior

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| .NET exception stack traces | ✓ | ✓ | ✅ | |
| Bare-metal ISR plumbing (timer, keyboard, serial) | ✓ | ✓ `X86_64Boot.codex` | ✅ | `emit-common-interrupt-handler` handles vectors 32/33/36 |
| CPU-exception ISRs (vectors 0-31: #DE, #UD, #GP, #PF, …) with register/fault dump to serial | 🟡 partial | ❌ | ❌ | Only device interrupts are wired. GPF / page fault produces silent QEMU hang. **Top open gap for bare-metal debugging.** |
| Source-location tracking through IR lowering | ✓ | ✓ | ✅ | IR-span surfaced to codegen errors |
| `--diagnostic` bare-metal allocation / function tracing | ✓ | ✓ | ✅ | (`f38e2d0`) |

### Parser features

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| Source span on every token / node | ✓ | ✓ | ✅ | |
| Indentation-sensitive grouping | ✓ | ✓ | ✅ | |
| Error recovery (resync to next top-level def) | ✓ | ❌ | ❌ | See diagnostics row above |
| `do`-block stop-set correctness | ✓ | ✓ | ✅ | `is-else-keyword`, `is-in-keyword` added (`058ac7c`) |
| Effect-annotation parsing | ✓ | ✓ | ✅ | No longer discarded (`2c8b520`) |

### Type system

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| Hindley-Milner-ish inference | ✓ | ✓ | ✅ | |
| Unifier with substitution map | ✓ | ✓ | ✅ | Fused `has-subst` + `subst-lookup` (P8) |
| Parameterized records | 🟡 C# emit chokes | 🟡 | 🟡 | Reference x86-64 path works; C# emitter path unverified |
| Union types with type variables | 🟡 | 🟡 | 🟡 | Needs dedicated pass |
| `EffectfulTy` / `ForAllTy` unwrapping | ✓ | ✓ | ✅ | T1/T2 fixes (`f71d8d7`, `8475298`, `785ff64`) |
| Polymorphism coverage audit | ? | ? | ❔ | Not yet done |

### Runtime behaviors

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| `list-snoc` amortized O(1) via heap-top in-place | ✓ | ✓ | ✅ | |
| `++` amortized O(1) (in-place path + geometric cap) | ✓ | ✓ | ✅ | `582562e` |
| `__list_cons` geometric capacity | ✓ | ✓ | ✅ | P4/P15, `dabddab` |
| `list-set-at` O(1) in-place builtin | ✓ | ✓ | ✅ | P1/P13, `466a08b` |
| `list-with-capacity` typed on C# host | ✓ | ✓ | ✅ | (`a87cce8`) |
| Text equality (CCE) | ✓ | ✓ | ✅ | |
| `heap-save` / `heap-restore` / `heap-advance` | ✓ | ✓ | ✅ | IrName dispatch fix landed (`f8a8fdd`) |
| Bump allocator, no GC | ✓ | ✓ | ✅ | Documented |
| Bare-metal heap size (~1 GB) | ✓ | ✓ | ✅ | `a725ac7` |

### Codegen / emission features

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|
| x86-64 bare-metal codegen | ✓ (frozen) | ✓ | ✅ | Primary MM4 target |
| ELF writer | ✓ | ✓ | ✅ | |
| CDX writer | ✓ | ✓ | ✅ | |
| C# emitter (bootstrap) | ✓ | ✓ | ✅ | |
| Codex text emitter (pingpong) | ✓ | ✓ | ✅ | |
| IL emitter | ✓ | ❌ | ⏭️ | Deliberate — .NET dependency being retired (BACKLOG) |
| x86-64 Linux user mode | ✓ | ❔ | ❔ | Not verified |

## What landed since this audit was first written

Rather than bury the diff, here's the headline: the **entire diagnostics
Phase 1-5 stack** (DiagnosticBag, Severity, Phase, TextFormat, CdxCodes,
SourceSpan with file-id, parser bag, staged error gates, BINARY-DIAG mode),
**all six bitwise ops**, **HAMT and TextSet in `Core/`**, and the **full
runtime fast-path set** (`++`, `::`, `list-set-at`, `list-snoc`) are all in
parity now. That's the bulk of the originally-identified gaps.

The P1-P17 n² hotspot hunt (see `docs/BACKLOG.md` Performance section)
closed 11 of 17 entries; the remaining six (P2, P3, P7, P9, P14, P13-tail)
are documented as lower-order wins.

## Top open gaps (priority order)

1. **CPU-exception ISRs with serial register dump** — #GP / #PF / #UD on
   bare metal currently hang silently. Adding vectors 0-31 with a minimal
   serial dump (vector, error code, RIP, R10/heap, RSP) would recover
   hours per week of debugging time. Self-contained, tractable.
2. **Parser error recovery (skip-to-next-def resync)** — bag captures
   what it can, but one malformed def still cascades. Classic technique:
   on parse error, skip tokens until column-1 `name :` pattern, resume.
3. **Self-host adoption of foreword `Maybe`** — the library exists; the
   compiler doesn't use it. Low urgency (sentinel pairs work) but would
   reduce API surprise across stdlib boundary.
4. **Parameterized records through C# emit path** — reference chokes;
   self-host behaviour unconfirmed. Needs a focused test.
5. **Arithmetic semantics on negative operands** — `mod` / `div` edge
   cases not re-verified after recent changes.

## Not in scope

- Actually implementing every row (each is follow-up work per ticket)
- Making self-host byte-identical to reference (it's allowed to diverge)
- Rewriting the reference (it's frozen)

## How to keep this current

When a gap is closed or a new one found:
- Flip the row's status and add the closing commit SHA or a short note.
- If a new data structure or runtime behaviour is introduced in either
  compiler, add a row.
- Don't let this doc rot. Every "why doesn't X work" session should start
  by checking here, and end by updating here.
