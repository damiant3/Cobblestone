# GitHub Update 30 -- 2026-07-01

Covers main CLs 6302-6479 (since Update 29 at CL 6300, 2026-06-28).
Three days, five agent streams (val, fester, reek, blu, restructure).

## Machine-Checked Proofs by Induction

The headline: **reverse (reverse xs) === xs** is now machine-checked
by the Codex compiler via structural induction. This is the flagship
proof that validates the entire dependent-type and proof infrastructure.

Val shipped the proof system in six stages across 20 copy-ups:

**Stage 1-2** (CLs 6411, 6440): Propositional equality soundness fix.
`Integer === Text` proved by `Refl` was previously accepted vacuously.
Fix: the unifier's PropEqTy arm now recurses with `unify-at` (resolves
through the substitution). `cong` upgraded from degenerate to real
congruence: `forall a b f. (a === b) -> (f a === f b)`. Value-level
`===` tests added.

**Stage 3** (CL 6449): Definitional equality normalizer. Delta
(unfold definitions), iota (reduce pattern matches), and beta
(apply lambdas) reduction. The normalizer runs during unification
so `reverse (reverse []) === []` simplifies to `[] === []` and
proves automatically.

**Stage 4** (CLs 6457, 6461): Induction syntax and verification.
`forall`/`induction` parse as first-class AST nodes. The compiler
generates per-constructor subgoals, verifies each against the
inductive hypothesis, and rejects unsound proofs (CDX2001).
`add-zero` proven over user-defined `Nat`.

**Stage 5** (CLs 6463, 6472, 6479): N-ary constructor congruence,
`app-cong` (function-position congruence), applicable lemmas (claim
instantiation), capture-avoiding normalizer fix. Lemma chain:
`append-nil` -> `append-assoc` -> `reverse-append` -> **`reverse-reverse`**.
Parametric-type induction (sumty-of resolves applied ConstructedTy).

**Stage 6** (CL 6438): `assume` axiom warning (CDX4021) so
unverified axioms don't hide silently.

All proofs erase at emit time (CDX4020) -- zero runtime cost.

## Claim/Proof/QED Soundness Fix

Val found and fixed a separate bug (CL 6425): the `claim`/`proof`/`qed`
sugar did not thread the claim's type to the proof body, so false
equalities were accepted vacuously. Root cause: a dangling-else in
`parse-top-level` orphaned the simple-claim branch.

## x86-64 Codegen: Unsound JMP Elision Fix

Val fixed a codegen regression (CL 6430): `emit-if-to-local` elided
the if's terminating jump based on `last-was-jmp`, which is unsound
when the then-branch is a join construct (nested if/match). In a
tail-recursive if-chain this silently skipped the whole arm. Fix adds
`tail-is-join` so the jmp-end is only elided for genuine terminals.

## CCE Output Boundary -- Full Unicode UTF-8

Fester shipped the output half of the I/O boundary Unicode work
(CL 6475). The compiler now emits proper UTF-8 for all CCE tiers:

- **Tier 0** (accented Latin, Cyrillic): 2-byte UTF-8 via a new
  hi-byte rodata table. Previously sent raw bytes >= 128 which
  aren't valid UTF-8.
- **Tier 1** (CJK 19968-20479): 3-byte UTF-8. The tier1 encoder
  maps these to 2-byte CCE; the output path detects unicode >= 2048
  and emits 3-byte UTF-8 instead of the broken 2-byte form.
- **Tier 2** (full CJK, Hangul, Hiragana, Katakana, Georgian,
  General Punctuation, Emoji): 3-byte or 4-byte UTF-8 via a 60-byte
  precomputed rodata table (10 block entries, no emit-time computation).
- **Rodata offset fix**: the unicode-to-cce reverse table was 256
  bytes but should have been 128. This misaligned the tier1 block
  rodata for the entire lifetime of the project -- tier1 output
  decode was reading from the wrong table. Fixed.

Input boundary (UTF-8 -> CCE) was shipped in Update 29's timeframe.
The compiler now round-trips CJK string literals: `"你好世界"`
compiles and prints correctly.

## Circuits EDA -- Phases 3-6

Reek continued the Circuits app with seven copy-ups (CLs 6310-6336):

- Widget system upgrade (InputSource, Canvas, GpuRender, AppRunner)
- Demo circuit polish: values, power symbols, crystal, reset
- Rendering: pin numbers, title block, better symbols, color palette
- Full crosshair, zone markers, hover highlight, status bar info
- Tab switching, net labels
- PCB board view, marquee select, undo counter
- 3D board viewer, SPICE waveform viewer, 9-entry PLACE menu
- Description panel, properties panel with hover inspect

## Syntax Cleanup (val)

Four CLs removing deprecated syntax and adding diagnostics:

- **Remove `++` operator** (CL 6390): all remaining users migrated
  to `&`. PlusPlus token, precedence rule, desugar path, and CDX5001
  all removed.
- **Remove vestigial tokens** (CL 6395): DashGreater, DotDot,
  Turnstile, LinearProduct, ForAllSymbol, ExistsSymbol.
- **Remove chained-arrow syntax** (CL 6397): CDX1072 rejects
  `a -> b -> c` in type signatures; 74 signatures migrated to
  comma form.
- **New diagnostics** (CL 6386): CDX2086 (Nothing-as-value with
  "use None" hint), CDX1071 (leading-dot rejection), CDX1070
  (literal continuations).

## codex-vm Improvements

- **Fault diagnostics** (CL 6420): ExceptionExitBitmap widened to
  all hardware fault vectors; watchdog ring dump with symbol
  resolution on panic.
- **Symbol map anchors** (CL 6416): interrupt handler, ISR stubs,
  and syscall handler now named in the MAP; refreshed seed/Codex.map.

## Design Doc Grooming

34 completed designs moved from `Active/` to `Done/` (CL 6475,
originally fester CL 6413). Design docs badly lagged the code --
a survey agent reading them literally reported shipped features as
"not started". Moved set includes the full CodexMagic engine suite
(15 docs), Browser, Diagram, FileShare, Secrets, Services, IoT
Addendum, CCE-TIER1, TrueTypeFont, ProofReading, REPL, and others.
14 stale status headers fixed in still-active docs.

## RISC-V Plug Local Recycling (blu)

Per-expression local recycling in the RISC-V plug (CL 6409): callee-saved
register reuse via peak-local tracking. Register stress benchmark:
100 insns / 0 spills with recycling vs 114 insns / 23 spills without.
Plus a FUNCMAP Unicode name manifest for human-readable cross-compile
bench maps.

## By the numbers

| Metric | Update 29 | Update 30 | Delta |
|--------|----------:|----------:|------:|
| Foreword modules | 367 | 371 | +4 |
| OS/Kernel modules | 137 | 137 | -- |
| Seed size | 2.31 MB | 1.88 MB | -19% |
| Seed digest | `E625476A` | `3D7787D0` | changed |
| Battery pass rate | 181/181 | 207/207 | +26 |
| Copy-ups | 35 | 46 | +11 |
| Days | 2 | 3 | +1 |
| Agent streams | 4 | 5 | +1 |
| Proofs machine-checked | 0 | 5 | +5 |

## What's next

Tier 2 output multi-char string debugging (fester). Val continuing
induction over richer types. Copy-up fester's `raw-bytes-to-text`
builtin to main. TEXT mode rebuild for the merged seed.
