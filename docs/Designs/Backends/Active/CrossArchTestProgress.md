# Cross-Architecture Test Progress

Status of ARM64 and RISC-V backends against the x86-64 test battery,
run via `build/test-cross.ps1` on Renode virtual boards.

## Current Results (2026-06-21, after CLs 5282-5347)

| Metric | ARM64 | RISC-V | x86-64 |
|---|---|---|---|
| Tests total | 152 | ~152 | ~152 |
| Compile success | ~137 | ~137 | ~152 |
| Output match | 62 (41%) | ~30 (est) | ~140 |
| Output mismatch | 75 | ~90 (est) | -- |
| Compile fail | 0 | 0 | 0 |
| Runtime fail | 0 | 0 | -- |
| Skip | 15 | 15 | -- |

ARM64 verified via Renode full run 2026-06-21 (CLs through 5347).
RISC-V estimated — ConstructedTy dispatch fix (CL 5282) should
improve RISC-V significantly but not yet re-run.

### Baseline (before fixes)

| Metric | ARM64 | RISC-V |
|---|---|---|
| Output match | 26 (20%) | 9 (7%) |

### Fixes applied (blu session + fester session)

| CL | Fix | Impact |
|---|---|---|
| 5083 | RISC-V register clobber in IrAppendText/IrConsList/IrAppendList | RISC-V: 9 -> 12 passes |
| 5100 | Boolean show dispatch (both), RISC-V spill scratch rotation | RISC-V: 12 -> ~14, ARM64: 26 -> ~28 |
| 5171 | 7 runtime functions (list-set-at, list-insert-at, substring, text-contains, text-starts-with, text-to-integer, text-compare, list-snoc) + lambda prologue/epilogue (both) | Lambda closures now save/restore callee-saved regs; ~30+ tests unblocked |
| 5174 | Bounded integer clamping in record construction (both) | Fixes Pct { p = 150 } yielding 150 instead of 100 |
| 5175 | ConstructedTy field resolution with type-defs fallback (both) | RISC-V gains full type-defs field lookup |
| 5178 | show CharTy, is-digit/is-letter/is-whitespace (RISC-V), inline bit-shl, char-code/code-to-char identity (both) | Completes character classification + shift builtins |
| 5282 | Bulk builtin coverage: ConstructedTy dispatch (RISC-V), state monad, 50+ builtin stubs, linked-list/text-concat-list runtime (both) | Unblocks many tests that hit unknown-builtin crashes |
| 5294 | ARM64 CCE-to-Unicode in print-line-uni + CCE digit codes in itoa/real_to_text (both) | Text strings now print correctly |
| 5304 | text-replace and text-split runtime functions (both) | |
| 5313 | ARM64 char-at fix: return raw CCE byte, drop stale ASCII-to-CCE lookup | Fixes char comparison for all punctuation |
| 5323 | Sum type structural equality: tag + up to 2 payload fields (both) | Circle 5 == Circle 7 now correctly False |
| 5326 | ARM64 text-split runtime | lang-smoke passes completely |
| 5330 | force builtin: lazy thunk evaluation via closure call (both) | |
| 5335 | ARM64 itoa minus CCE 71, RISC-V text-to-integer minus fix | parse-test passes, negative numbers display correctly |
| 5337 | freeze builtin: identity at runtime for linear types (both) | |
| 5341 | Print return value when opening returns Text (both) | prose-smoke, linear-smoke now produce output |
| 5344 | show fallback for ListTy/RecordTy (both) | Lists/records no longer print raw pointers |
| 5347 | Let-binding variable shadowing: lookup-local searches most recent binding (both) | linear-smoke passes (42 21 56) |

Compile failures (both architectures): arm64-web-server,
nrf52840-drivers / rp2040-drivers, ui-orchestrator-test,
ui-font-test / ui-icon-test. All are large tests that exhaust the
codegen plug's 3 GB heap.

## Tests Passing on Both Architectures

bounds-prover, bs3-smoke, implicit-convert, mini-bootstrap,
ota-gate-real, punctual-smoke, typeclass-poly, unit-family,
with-timeout-test.

These tests share a common trait: they do not call `show` on integers
or use text concatenation with function-call results in the
right-hand position.

## Tests Passing on ARM64 Only

approx-eq, board-types, color-test, eventbus-test, mqtt-packet,
ota-update, prose-consistency, punctual-iot, stringbuilder-test,
tuple-syntax, ui-dialog-test, ui-scroll-test, ui-theme-test,
unit-family-mixed, units-foreword, usb-test, xhci-enum-test.

## Known Codegen Bugs

### Both Backends

1. **Lambda absorption** — (FIXED, CL 5171) Inline closures now
   emit prologue/epilogue with callee-saved register save/restore.
   Def-level lambda unwrap was already working.

2. **Bounded integer clamping** — (FIXED, CL 5174) Record field
   construction now checks for IntegerTy + OvClamping and emits
   clamp (CSEL on ARM64, branch-and-move on RISC-V).

3. **Boolean show** — (PARTIALLY FIXED, CL 5100) Direct
   `show bool-expr` works. Booleans returned from functions where
   IR type resolves to Integer still print as 0/1 (compiler IR
   issue, not plug issue).

4. **Literal pattern matching (IrLitPat)** — Already implemented
   in both backends with parse-int-text from common plug chain.
   Verified working.

### RISC-V Only

5. **Register clobber in binary ops** — (FIXED, CL 5083) When
   `IrAppendText`, `IrConsList`, or `IrAppendList` had `right-reg`
   in a0, `mv a0 left-reg` clobbered a0 before `mv a1 right-reg`
   could read it. Both arguments became the left operand. ARM64
   already had this fix.

6. **Spill reload uses single scratch register** — (FIXED, CL 5100)
   `rv-load-local` always reloaded spilled values into t0. Changed
   to use `rv-alloc-temp` which rotates through t3/t4/t5/t6.

7. **Seven-digit integer show** — `show 1000000` returns empty
   string on RISC-V. Cause under investigation; five- and six-digit
   integers convert correctly.

8. **Boolean show partial coverage** — (PARTIALLY FIXED, CL 5100)
   Direct `show bool-expr` calls now emit True/False. But Booleans
   returned from functions (where IR type resolves to Integer rather
   than BooleanTy) still print as 0/1.

## Infrastructure

- **Test harness**: `build/test-cross.ps1 -Arch arm64|riscv64|all`
  with optional `-Filter` glob. Submitted CL 5076 (Mountain),
  copied to main CL 5085.

- **Renode boards**: `tools/renode/codex/codex-arm64.repl` (Cortex-A53
  + GICv3 + PL011), `codex-riscv64.repl` (RV64GC + PLIC/CLINT +
  NS16550). 256 MB RAM each.

- **Compile pipeline**: source -> IR (via x86 compiler on codex-vm) ->
  codegen plug (ARM64 or RISC-V, also on codex-vm) -> ELF binary ->
  Renode boot -> UART capture -> compare against `.expected`.

- **Output handling**: backends loop after `opening` returns (no halt
  instruction). The harness truncates UART output to the expected
  line count. Diagnostic lines (HEAP:/WD:/STACK:) are filtered.

- **Runtime**: ~35 seconds per test (25s compile + 5-8s Renode with
  3s timeout). Full battery takes ~75-90 minutes per architecture.

## Path to Full Backend Parity

### Completed (CLs 5083-5178)

1. ~~RISC-V spill scratch rotation~~ (CL 5100)
2. ~~Boolean show~~ (CL 5100, partially)
3. ~~Bounded integer clamping~~ (CL 5174)
4. ~~Lambda absorption / inline closures~~ (CL 5171)
5. ~~Literal pattern matching~~ (already implemented)
6. ~~7 missing runtime functions~~ (CL 5171)
7. ~~ConstructedTy field resolution~~ (CL 5175)
8. ~~Character classification builtins~~ (CL 5178)

### Remaining (priority order)

1. **Real/float operations** — blu is implementing ARM64/RISC-V
   FP support. Affects real-approx, geometry, vector tests.

2. **Atomic operations** — ARM64 LDXR/STXR, RISC-V LR/SC.
   Affects atomic-smoke test.

3. **State monad** — run-state/set-state/get-state. Affects
   UI layout tests and some app tests.

4. **Halt after opening** — emit WFI (ARM64) / ECALL (RISC-V)
   after opening returns. Eliminates line-count truncation
   workaround in test harness.

5. **Boolean type in IR** — functions returning Boolean sometimes
   have Integer in the IR type, causing show to print 0/1 instead
   of True/False. Compiler-side fix needed.

### Renode-Verified Results (2026-06-21)

ARM64 pass=36, fail=78, skip=14, compile-only=2 (total 130 tested).
Improvement: 28 -> 36 passes (+8 new, +29% improvement).

New passes: arithmetic, audio-diffusion-test, circbuf-test, factorial,
iterate-test, iterate-zip-test, pipe-unique-test, queue-test,
real-approx, real-saturating, real-trapping, sprite-test,
stringbuilder-test, stringutils-test, suggested-width, synth-test,
truetype-test, typeclass-smoke, ui-focus-test.

### Remaining Failure Categories

1. **ConstructedTy field access** (~20 tests) — user-defined records
   returned from functions have ConstructedTy in IR type, and
   type-defs list may not include the record definition. Field
   access defaults to index 0 (wrong). Root cause: compiler IR
   type-defs pipeline needs investigation.

2. **Char classification** (CL 5241, not in this run) — is-letter,
   is-digit, is-whitespace were using CCE ranges instead of ASCII.
   Fixed but not yet verified on Renode.

3. **Tests that crash/hang** (~5 tests) — linear-smoke produces
   empty output. May be stack overflow or linear type issue.

4. **Complex closure patterns** (~10 tests) — closures with
   multiple captures or nested closures may have register issues.

5. **Effect handlers / try / fork** (~7 tests) — stubbed to
   just emit body, ignoring handler semantics.
