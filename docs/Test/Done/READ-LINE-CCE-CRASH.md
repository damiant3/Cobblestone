# read-line-cce Offset Table Lookup Failure

**Status:** RESOLVED 2026-05-29 by val (Opus 4.8). Two distinct bugs,
both now fixed.

## Resolution Summary

The originally-reported symptom (call to `read-line-cce` resolving to
`__unresolved_trap`, `!EXC=06`) **no longer reproduces** after the
RESTRUCTURE merge-down (CL 2649). On the merged seed, binary inspection
of a compiled `read-line-cce` program (MAP1 parse + E8 call scan) shows
exactly one call to `__read_line_cce` at its correct code offset and
zero calls to `__unresolved_trap`. The offset-table lookup now finds
the helper. (Whatever resolution fix landed in RESTRUCTURE closed the
original hash-table miss; it was never root-caused here because the
merge fixed it.)

A **second, independent bug** was then found and fixed:
`__read_line_cce` silently dropped every `'e'`. The helper was cloned
from the ASCII `__read_line` helper, which skips carriage returns with
`cmp rax, 13` (ASCII CR = 13). But the cce helper reads CCE bytes
natively, and **CCE byte 13 is `'e'`** (the CCE table is
frequency-ordered: e is the most common letter, lowest letter code).
So feeding CCE `"hello world\x01"` returned `"hllo world"`. CCE has no
carriage return at all, so the CR-skip is meaningless — the fix removes
it from `emit-read-line-cce-helper` (X86_64Helpers.codex). The newline
terminator check correctly uses CCE newline (`cmp rdx, 1`).

Verified: with the fix, CCE input `"hello world" + 0x01` returns the
full `"hello world"`.

---

## Original Report (superseded — kept for history)

**Status:** Active. Discovered 2026-05-29 by val.

## Summary

`read-line-cce` (a new builtin added in CL 2618) compiles
correctly and its runtime helper `__read_line_cce` is emitted
at the correct code offset. However, when a user program calls
`read-line-cce`, the call patch resolves to `__unresolved_trap`
instead of `__read_line_cce`. The function is in the MAP (built
from fo-names) but not found by `offset-table-lookup` in the
hash table (built from the same fo-names).

## Reproduction

```codex
Chapter: TestReadLineCce

Section: Body

  opening : [Console] Nothing = act
    result <- read-line-cce
    when result
      is Just (line) -> print-line line
      is None -> print-line "none"
  end

Page 1
```

```powershell
build/compile.ps1 -Src test.codex -Out test.cdx
# Produces test.cdx that crashes with !EXC=06 at __unresolved_trap
```

## Evidence

- MAP shows `__read_line_cce` at 0x101403, size 378 bytes
- Binary diff shows exactly ONE code difference between a
  `read-line` program and a `read-line-cce` program: the call
  offset at 0x109438
- `read-line` resolves to `__read_line` at 0x101263 (correct)
- `read-line-cce` resolves to 0x1089B6 = `__unresolved_trap + 1`
- Renaming `__read_line_cce` to `__rlcce` produces the same crash
  at the same address — it's not a name-specific issue
- Hash slots are non-colliding: CCE hash of `__read_line_cce`
  → slot 5808, `__read_line` → slot 8372 (table size 16384)
- The seed compiles itself correctly (all gates green, fixed
  point) with `__read_line_cce` in the runtime helpers

## Root Cause Hypothesis

The offset table (`OffsetTable` hash table built by
`build-offset-table-parallel`) silently fails to insert or
retrieve `__read_line_cce`. Possible causes:

1. **Hash table capacity overflow.** The table has 16384 slots
   and 122 entries. Load factor is 0.7% — should be fine. But
   if the insertion loop has an off-by-one in the linear probing,
   entries near the end of a probe chain might not be found.

2. **Insert ordering.** `record-func-offset` appends to fo-names
   in emission order. `build-offset-table-parallel` iterates
   from 0 to len. If inserting entry N evicts entry M (probe
   chain collision), and M is looked up later, it won't be found.
   But open-addressing with linear probing doesn't evict — it
   probes to the next empty slot.

3. **Text comparison bug in CCE.** `offset-table-get` uses
   `text-compare` or `==` on CCE strings. If there's a
   comparison issue with underscore-heavy names, the probe
   chain would walk past the correct entry.

## Investigation Path

This needs a GDB watchpoint on the hash table entry for
`__read_line_cce`:

1. Set breakpoint at `offset-table-set` and capture the slot
   computed for `__read_line_cce`
2. Verify the entry is written to the keys/values lists
3. Set breakpoint at `offset-table-get` when called with
   `__read_line_cce` and trace the probe chain
4. Check if the entry is present in the table but the probe
   walks past it

## Deep Investigation (val, 2026-05-29)

### Proven

1. **Algorithm is correct.** PowerShell simulation of djb2-hash +
   linear-probing insert + lookup for all 120 test CDX function
   names succeeds. `__read_line_cce` hashes to slot 5808, no
   collisions, lookup finds it at the correct offset 0x1403.

2. **Function is emitted.** The test CDX MAP shows `__read_line_cce`
   at 0x101403 (378 bytes). Binary diff between `read-line` and
   `read-line-cce` programs shows exactly ONE code difference:
   the call target at 0x109437.

3. **Self-compile doesn't exercise this.** The seed never calls
   `__read_line_cce` internally — it's emitted as a helper but
   no compiler code references it. The fixed-point test doesn't
   catch lookup failures for unused functions.

4. **Renaming doesn't help.** `__rlcce` (6 chars, different hash)
   produces the same crash at the same address. Any new runtime
   helper added to the chain fails the same way.

5. **`-Break` re-added to compile.ps1** (CL pending) but MAP
   addresses may be unreliable — `apply-call-patches-direct` at
   0x1D6949 points to `00 00` (middle of a jump displacement,
   not a valid function entry). Breakpoint patching at MAP
   addresses corrupts instructions.

### Hypothesis

The bare-metal `build-offset-table-parallel` or `offset-table-set`
silently fails to insert new entries that the hash algorithm
should correctly place. Possible causes:

- `list-set-at` on the hash table's backing list has a bounds
  or aliasing issue for slots > some threshold
- The `djb2-hash` runtime produces a different value than the
  PowerShell simulation (CCE encoding mismatch?)
- The `==` comparison in `offset-table-lookup-probe` on CCE
  strings with underscores behaves differently than expected

### Next Step

Add a diagnostic `print-line` inside `check-call-patch-targets`
(X86_64State.codex:524) to print each unresolved call target.
This won't affect the fixed point (no unresolved calls in
self-compile). When compiling a user program with `read-line-cce`,
it will print the exact target name that failed lookup. Compare
against what `build-offset-table-parallel` inserted.

## Files

- `codex/compiler/Emit/X86_64Helpers.codex` — `emit-read-line-cce-helper`
- `codex/compiler/Emit/X86_64Builtins.codex` — `emit-read-line-cce-builtin`
- `codex/compiler/Core/OffsetTable.codex` — hash table implementation
- `codex/compiler/Emit/X86_64Chapter.codex:707` — `build-offset-table-parallel`
- `codex/compiler/Emit/X86_64State.codex:524` — `check-call-patch-targets`
- `build/compile.ps1` — `-Break` parameter re-added
