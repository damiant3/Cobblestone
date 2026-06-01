# TCO Heap Reset Diagnostic Instrumentation

**Date:** 2026-04-01
**Status:** Design — not yet implemented
**Goal:** Identify why TCO heap reset crashes the self-compile when list
parameters are present, so the `hasListArg` bandaid can be replaced with
a proper fix.

## Finding (2026-04-01)

**The bandaid fires first in `tokenize-loop`.** Diagnostic instrumentation
(print function name + continue on bandaid) shows `@BANDAID:tokenize-loop`
on every iteration — thousands of times for a 362 KB source file. The
tokenizer is a TCO loop that accumulates tokens into a `List Token` via
`list-snoc`. This is THE function that makes the bandaid necessary: removing
the `hasListArg` check lets the TCO reset reclaim the just-snoced tokens,
corrupting the token list.

This means the fix is NOT about escape copy at all — it's about making
`tokenize-loop` (and similar accumulator patterns) safe for TCO heap reset.
Options:
1. **Copy-on-write list-snoc**: never mutate in-place, always allocate new.
   Reset becomes safe because the new list pointer is above the mark (caught
   by pointer check). Costs O(N) per snoc instead of amortized O(1).
2. **Accumulator-aware reset**: skip reset only for the specific param that
   is being snoced, not all params. Requires detecting which param changes.
3. **Return the list from result space**: after snoc, escape-copy the list
   to result space so the pointer is above the mark. Heavy but correct.
4. **Accept the bandaid**: `tokenize-loop` NEEDS the bandaid. The fix is
   to make OTHER functions (that don't snoc) benefit from reset despite
   having list params they only READ.

Option 4 is the original "last-element check" approach but applied
selectively. The crash wasn't from `tokenize-loop` (the bandaid protected
it) — it was from removing the bandaid for ALL functions simultaneously.

## The Problem

The TCO heap reset (X86_64CodeGen.cs lines 233-398) reclaims per-iteration
working-space allocations at each tail-call loop boundary. It works
correctly for functions without list parameters. But when the `hasListArg`
bail-out is removed (allowing reset for functions WITH list params), the
bare-metal self-compile produces zero output and hangs until timeout.

The bail-out was added to make TCO work at all. It masks a real bug.
Small programs work fine — the crash only manifests at self-compile scale,
making it impossible to diagnose without instrumentation.

## What We Know

1. The TCO reset mechanism is correct for non-list params (self-compile
   passes pingpong with it)
2. Removing `hasListArg` and adding last-element safety checks (correct
   on paper) causes the self-compile to produce zero serial output
3. Zero output means the crash happens early — before the first
   `print-line` in `emit-defs-streaming`
4. Small bare-metal programs with escape copy work fine (greeting, person,
   mini-bootstrap all produce correct output)

## Possible Root Causes

### 1. In-place list-snoc creates above-mark interior pointers

`__list_snoc` (line 3050) has three paths:
- **Path 1**: count < capacity → in-place append, return same pointer
- **Path 2**: at heap top → grow capacity in-place, return same pointer
- **Path 3**: not at top → allocate new list, copy, return new pointer

Path 1 is the dangerous case: the list pointer stays below the mark,
but the new element is stored at an interior position. If that element
is a heap pointer allocated this iteration (above mark), resetting
HeapReg reclaims the element's target while the list retains a dangling
pointer to it.

The last-element check should catch this — but it might miss cases where
the element type doesn't trigger `TypeNeedsHeapEscape` but IS a pointer,
or where structural sharing causes an element to be checked against the
wrong mark.

### 2. String concat in-place mutation

`__str_concat` (line 2464) has a similar in-place fast path: if the
first string is at the heap top, it appends in-place. A below-mark string
could contain content that references above-mark data (though strings
are flat byte arrays, not pointer-containing — so this is unlikely).

### 3. Mark saved at wrong point

The mark is saved at `m_tcoLoopTop` (line 478-488). If any allocation
happens between mark save and the first user-visible code, those
allocations would be reclaimed at reset. This could include IRRegion
mark saves, scalar region temporaries, or function call frame data.

### 4. Register clobber during reset

The reset code (lines 308-398) uses `AllocTemp()` to get scratch
registers. In the `anyChecks` path, it allocates `markReg` and possibly
additional temps for list element checks. If these temps conflict with
registers holding live values (function args being copied to temp locals),
data corruption results.

### 5. TCO reset fires in a function other than emit-defs-streaming

The `hasListArg` removal affects ALL TCO functions with list params.
Some of these might genuinely snoc lists during iteration, and the
last-element check might not catch all cases. Candidates in the
self-compiler:

- `register-def-headers` — tail-recursive, modifies state per iteration
- `check-defs-streaming` — uses `list-snoc acc entry` (the exact case
  the last-element check was designed for, but needs verification)
- `collect-ctor-bindings` — likely uses list-snoc for accumulation
- Any TCO function in Lexer, Parser, TypeChecker, Lowering, or Emitter

### 6. List capacity layout crossing the mark boundary

A list's capacity word is at `ptr - 8`. If a list is allocated just
above the mark, its capacity word is AT or below the mark. Resetting
HeapReg wouldn't reclaim the capacity word but would reclaim the rest.
Subsequent access to the list would read stale capacity data.

## Instrumentation Design

### Build Configuration

Add a `--diagnostic` flag to the CLI and a corresponding field on
X86_64CodeGen:

```
CLI:     dotnet run --project tools/Codex.Cli -- build <file> --target x86-64-bare --diagnostic
Codegen: bool m_diagnostic  (passed through from X86_64Emitter)
```

When `m_diagnostic` is true, the codegen emits serial print instrumentation
at key points. When false (default), zero overhead — identical ELF to
today.

### Halt-on-Bandaid (Priority Zero)

Before implementing full instrumentation, add a single trap: in
diagnostic mode, when the `hasListArg` bail-out would fire, print the
function name to serial and HLT. This tells us exactly which TCO
function first triggers the bail-out during the self-compile. Everything
before that point worked — the bug happened before the halt.

```
@BANDAID:<function-name>\n
HLT
```

This alone cuts the search space dramatically. If the halt fires in
`emit-defs-streaming`, the bug is in the setup phase. If it fires in
`register-def-headers`, the bug is in header registration. The function
name is the clue.

### Instrumentation Points

Each instrument emits a tagged serial line: `@TAG:value\n`. Tags are
short fixed strings. Values are hex addresses or decimal counts. The
serial output is interleaved with normal program output and can be
parsed by a diagnostic script.

#### A. Allocation tracking

Every `AddRI(HeapReg, N)` site in the codegen (there are ~20). Emit
BEFORE the bump:

```
@A:<hex HeapReg>:<hex size>\n
```

This shows every heap allocation: where it starts and how big it is.
Guarded by `if (m_diagnostic)` at each call site.

Implementation: helper method `EmitDiagAlloc(int size)` that:
1. Pushes RAX, RDX (preserve live regs)
2. Prints "@A:" via serial wait-and-send
3. Prints HeapReg as hex via a small inline hex printer
4. Prints ":" and size as hex
5. Prints "\n"
6. Pops RAX, RDX

#### B. TCO mark save

At `m_tcoLoopTop` (line 486), emit after saving the mark:

```
@TM:<hex mark>:<function-name>\n
```

Shows the mark value and which function is entering its TCO loop.

#### C. TCO reset decision

At each tail-call's heap reset check (inside EmitTailCall), emit the
decision and relevant values:

```
@TR:<hex mark>:<hex HeapReg>:RESET\n     (reset fired)
@TS:<hex mark>:<hex HeapReg>:SKIP:<reason>\n  (reset skipped)
```

Reasons: `PTR` (pointer above mark), `LAST` (last element above mark),
`EMPTY` (empty list, safe).

#### D. List-snoc tracking

In `EmitListSnocHelper` (line 3050), at each of the three paths, emit:

```
@LS:<hex list_ptr>:<hex element>:<path>\n
```

Where path is `IP` (in-place), `GROW` (grow at top), `COPY` (full copy).
This reveals whether snoc is mutating below-mark lists.

#### E. Region mark/restore

In `EmitRegion`, at scalar mark-save and restore:

```
@RM:<hex mark>\n   (mark saved)
@RR:<hex mark>:<hex HeapReg>\n   (mark restored, showing reclaim amount)
```

#### F. Function entry/exit

At function prologue and epilogue:

```
@FE:<function-name>\n  (entry)
@FX:<function-name>\n  (exit)
```

This creates a call trace that can be correlated with allocations and
resets to identify exactly where the crash happens.

### Helper Methods

```csharp
// Emit a diagnostic tag + hex value via serial. Only emits code when
// m_diagnostic is true. Preserves all registers via push/pop.
void EmitDiagTag(string tag)           // prints @TAG:
void EmitDiagHex(byte reg)            // prints hex value of register
void EmitDiagNewline()                // prints \n
void EmitDiagSeparator()              // prints :
void EmitDiagString(string s)         // prints literal string

// Composite helpers
void EmitDiagAlloc(int size)          // @A:<HeapReg>:<size>
void EmitDiagTcoMark(string func)     // @TM:<mark>:<func>
void EmitDiagTcoReset(bool fired)     // @TR/@TS:<mark>:<HeapReg>:RESET/SKIP
void EmitDiagFuncEntry(string name)   // @FE:<name>
void EmitDiagFuncExit(string name)    // @FX:<name>
```

All guarded by `if (!m_diagnostic) return;` at the top.

### Hex Printer

Need a small inline hex printer that converts a 64-bit register to hex
and sends via serial. This is ~60 bytes of generated code per call site.
Since diagnostic mode doesn't care about ELF size, this is fine.

Alternative: emit a single `__diag_hex` helper function and call it.
Saves code size in diagnostic builds.

```asm
__diag_hex(RDI = value):
    ; print 16 hex digits of RDI to serial
    mov RCX, 60           ; shift amount (start from high nibble)
.loop:
    mov RAX, RDI
    shr RAX, CL           ; shift to get nibble
    and RAX, 0xF          ; mask nibble
    add RAX, '0'          ; convert to ASCII
    cmp RAX, '9'
    jle .digit
    add RAX, 7            ; 'A' - '0' - 10
.digit:
    ; serial wait and send RAX
    ...
    sub RCX, 4
    jge .loop
    ret
```

## Test Suite

### Progressive test levels

Each level exercises more of the compiler and reveals different
allocation patterns. All targets `x86-64-bare` with `--diagnostic`.

#### Level 0: Minimal (no heap)
```codex
main : Integer
main = 42
```
Expected: no `@A` lines, no `@TR` lines.

#### Level 1: Text allocation
```codex
main : Text
main = "hello"
```
Expected: one `@A` for string allocation.

#### Level 2: Record allocation
```codex
P = record { x : Integer, y : Integer }
main : Integer
main = let p = P { x = 1, y = 2 } in p.x + p.y
```
Expected: one `@A` for record, one `@RM`/`@RR` for let region.

#### Level 3: List allocation + cons
```codex
main : Integer
main = let xs = 1 :: 2 :: 3 :: [] in list-length xs
```
Expected: multiple `@A` for list/cons operations.

#### Level 4: TCO with scalars
```codex
sum-to : Integer -> Integer -> Integer
sum-to (n) (acc) = if n <= 0 then acc else sum-to (n - 1) (acc + n)
main : Integer
main = sum-to 1000 0
```
Expected: `@TM` once, `@TR` with RESET on every iteration.

#### Level 5: TCO with list accumulator (the critical case)
```codex
build : Integer -> List Integer -> List Integer
build (n) (acc) = if n <= 0 then acc else build (n - 1) (list-snoc acc n)
main : Integer
main = list-length (build 100 [])
```
Expected: `@TM` once. `@LS` with IP/GROW/COPY per iteration. `@TR`
should show SKIP (last element above mark) on every iteration because
`n` (scalar) is snoced — wait, n is scalar, not heap. So last-element
check doesn't fire. Pointer check: after first snoc, list might be
reallocated above mark → SKIP. Or might be in-place → pointer below
mark, reset fires, CRASH.

**This is the level that should reproduce the bug with a simple program.**

#### Level 6: TCO with heap-element list accumulator
```codex
Pair = record { a : Integer, b : Integer }
build-pairs : Integer -> List Pair -> List Pair
build-pairs (n) (acc) =
  if n <= 0 then acc
  else build-pairs (n - 1) (list-snoc acc (Pair { a = n, b = n * 2 }))
main : Integer
main = list-length (build-pairs 50 [])
```
Expected: `@LS` with in-place snoc, `@TR` with SKIP (last element =
Pair record above mark). If last-element check works, reset skips. If
it doesn't, crash.

#### Level 7: Read-only list TCO (the emit-defs-streaming pattern)
```codex
count-items : List Text -> Integer -> Integer -> Integer
count-items (items) (i) (n) =
  if i >= n then 0
  else let item = list-at items i in text-length item + count-items items (i + 1) n
main : Integer
main = let xs = "alpha" :: "beta" :: "gamma" :: [] in count-items xs 0 3
```
Expected: `@TR` with RESET (list is read-only, pointer below mark,
elements below mark). Should work. If it doesn't, the bug is NOT
about snoc — it's about the reset mechanism itself.

#### Level 8: Mixed — read-only lists + heap let-bindings
```codex
process : List Text -> Integer -> Integer -> Text
process (items) (i) (n) =
  if i >= n then ""
  else
    let item = list-at items i
    in let result = item ++ "!"
    in result ++ process items (i + 1) n
main : Text
main = let xs = "a" :: "b" :: "c" :: [] in process xs 0 3
```
Expected: `@TR` RESET should fire, reclaiming per-iteration let-binding
allocations. Tests that heap let-bindings within a TCO body are properly
reclaimed.

#### Level 9: Self-compile subset (first 3 files)
Feed just the first 3 .codex files through the self-compiler.
Expected: reveals which function crashes and at what allocation.

#### Level 10: Full self-compile
Full source.codex through the self-compiler.
Expected: identifies exact crash location via `@FE`/`@FX` trace.

### Test Runner Script

`tools/diagnostic-tests.sh`:

```bash
#!/bin/bash
# Run diagnostic test suite for TCO heap reset investigation.
# Each test builds with --diagnostic, runs in QEMU, captures serial
# output, and checks for crashes (zero output after READY).

TESTS=(
    "samples/diag/level0-scalar.codex"
    "samples/diag/level1-text.codex"
    "samples/diag/level2-record.codex"
    "samples/diag/level3-list.codex"
    "samples/diag/level4-tco-scalar.codex"
    "samples/diag/level5-tco-list-snoc.codex"
    "samples/diag/level6-tco-heap-snoc.codex"
    "samples/diag/level7-tco-readonly-list.codex"
    "samples/diag/level8-tco-mixed.codex"
)

for test in "${TESTS[@]}"; do
    echo "=== $test ==="
    name=$(basename "$test" .codex)
    dotnet run --project tools/Codex.Cli -- build "$test" \
        --target x86-64-bare --diagnostic
    # Run in QEMU, capture serial, timeout 30s
    # Parse @-tagged lines, report allocation count, reset count,
    # and whether output was produced
    # ...
done
```

For levels 9-10, the script feeds source via the serial pipe (same as
pingpong) and captures the diagnostic trace.

The script should produce a summary per test:

```
Level 0: PASS  allocs=0  resets=0  output="42"
Level 1: PASS  allocs=1  resets=0  output="hello"
Level 4: PASS  allocs=0  resets=1000  output="500500"
Level 5: CRASH allocs=47 resets=3  last_func="build" last_alloc=@A:0x401230:16
```

The crash level identifies where the bug lives.

## Implementation Scope

### Files to modify

| File | Changes |
|---|---|
| `X86_64CodeGen.cs` | Add `m_diagnostic` field, 6 helper methods, ~20 instrumentation points at existing allocation sites, TCO mark/reset sites, function entry/exit |
| `X86_64Emitter.cs` | Pass `diagnostic` flag through to CodeGen constructor |
| `Program.Build.cs` | Add `--diagnostic` CLI flag |
| New: `samples/diag/level*.codex` | 10 test programs (level 0-8 are tiny, 5-15 lines each) |
| New: `tools/diagnostic-tests.sh` | Test runner script |

### Estimated ELF size impact

- Normal build (`--diagnostic` off): zero overhead, identical ELF
- Diagnostic build: ~2-5 KB additional code per instrumentation point,
  plus `__diag_hex` helper (~100 bytes). Total: ~50-100 KB overhead.
  Doesn't matter — diagnostic builds aren't for production.

### Estimated effort

- Diagnostic helpers + instrumentation points: ~200 lines of C#
- CLI flag plumbing: ~10 lines
- Test programs: ~100 lines of Codex total
- Test runner script: ~80 lines of bash
- Total: ~400 lines, ~2 hours of implementation

## Expected Outcome

Running the diagnostic suite will reveal one of:

1. **Level 5 crashes** → bug is in list-snoc interaction with TCO reset.
   The snoc returns the same pointer (in-place), but the element lives
   above the mark. The last-element check is either not firing or checking
   the wrong thing.

2. **Level 5 passes, Level 7 crashes** → bug is in the reset mechanism
   itself, not snoc. Read-only list params should be safe. Something
   about the mark save/restore is corrupting state.

3. **Levels 5-8 all pass, Level 9 crashes** → bug is in a specific
   compiler function, not the mechanism. The `@FE`/`@FX` trace identifies
   which function. Likely a TCO function with unusual parameter patterns.

4. **All levels pass** → the bug only manifests at scale (hash collisions,
   memory layout dependent). Needs the full self-compile trace to identify.

Each outcome narrows the investigation and dictates the fix strategy.
