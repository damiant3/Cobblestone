# Plug Compiler Crash — Investigation Handoff (Session 2)

## The Bug

The seed compiler (CL 1557, `build-output/bare-metal/Codex.cdx`) crashes with a
page fault when compiling `plugs/csharp/build-output/plug-source.codex`. The crash
is in `text-compare` called from `bsearch-text-pos` during a type lookup in the
emitter. A TypeBinding record at address 0x1a6f7c5 has its `.name` field corrupted.

## How to Reproduce

```powershell
codex.build/test-compile.ps1 -Src "plugs/csharp/build-output/plug-source.codex" -Out "test-output/csharp-plug.cdx" -Log "test-output/csharp-plug.log"
# Exit code 4, log contains !EXC=0e
```

## Crash Signature (Stable Across Runs)

```
!EXC=0e RIP=00000000002748af CR2=00002eeXXX000000 R15=0000000001a6f7c5
```

- **Exception 0x0e** = page fault
- **CR2** = the corrupt pointer value being dereferenced (varies slightly: 0x2ee56a, 0x2ee7af, 0x2eeef7 — always a seed code-section address shifted left ~24 bits)
- **R15 = 0x1a6f7c5** = the TypeBinding record pointer (STABLE every run)
- **[R15+0]** = the `.name` field = the corrupt value = CR2
- **Faulting instruction** at RIP: `mov (%rax), %rcx` — dereferencing the corrupt text pointer loaded from the TypeBinding

## Key Facts

1. **Deterministic for a given build.** Same source, same seed → same R15, same crash.
2. **Does NOT reproduce under QEMU TCG.** Only crashes under QEMU WHPX. Under TCG the guest exits normally (compilation succeeds or at least doesn't page-fault).
3. **kernel-irqchip=off is set** but **`sti` is called during boot** (line 915 of X86_64Boot.codex). However, process table slot 0 has slice=0 (zeroed at boot), so the timer ISR always takes the no-pre1 fast path (just pop+iretq). The ISR does NOT corrupt registers on this path.
4. **The compiler self-compiles fine** (23-arm IRExpr match in emit-expr). The plug's `ir-expr-type` is also 23 arms. The difference is compilation context (heap layout, total source size, type environment contents).
5. **The corrupt value is a seed code address** (~0x2ee000, which is 0x100000 + ~0x1ee000 = near end of the 2.1MB seed). This is NOT a plug output address (plug output would be 0x100000-0x110000).

## What Has Been Ruled Out

1. **Timer interrupt register corruption.** The ISR takes the no-pre1 fast path (slice==0, process table zeroed). Callee-saved registers are never touched. A fix was submitted (CL 1557) to restore registers on the preemption path, but it doesn't help because preemption never triggers. The fix is harmless but irrelevant.

2. **Out-of-bounds code buffer writes.** Added bounds checks to `apply-func-addr-patches-direct`, `apply-call-patches-direct`, `apply-rodata-patches-direct` — none fired. Patch offsets are all within `text-buf-size` (4MB). The code buffer is at the heap base (~0x600000) and TypeBindings are at ~0x1a6f7c5 (much higher). You'd need offset ~20MB to reach them.

3. **List capacity overflow.** Global patch lists (`fa-offsets` etc.) are pre-allocated to 32768 via `__list-with-capacity`. The plug is small (~130KB source, ~64KB output). It doesn't approach 32768 patches. Lists never grow.

4. **The `list-push` heap-save/restore interaction.** I theorized that `list-push` growing a list past `__heap-save` would leave dangling entries after `__heap-restore`. WRONG — the lists don't grow (pre-allocated capacity), and the per-function `emit-all-defs` loop only does heap-save/restore around temporary per-function state, not the global lists.

5. **GDB watchpoints.** WHPX doesn't support hardware watchpoints through GDB. TCG supports them but doesn't reproduce the bug. Conditional breakpoints at the faulting instruction are impractical (text-compare is called thousands of times).

6. **codex-vm.exe.** Crashes on MMIO at 0x7ffffff0 during boot — not ready for this workload yet.

## The Corrupt Value Pattern

The 8-byte `.name` field at 0x1a6f7c5 contains (example): `0x00002eeef7000000`

In little-endian memory: `00 00 00 F7 EE 2E 00 00`

The 4-byte sequence `F7 EE 2E 00` (= LE for `0x002EEEF7`) appears at byte offset 3 within the 8-byte field. This is NOT a clean 4-byte-aligned write. It's as if 4 bytes were written at address `0x1a6f7c5 + 3 = 0x1a6f7c8`.

The value `0x002EEEF7` is `bare-metal-load-addr + func_offset` = `0x100000 + 0x1EEEF7`. This is a function address within the seed binary. The seed is 2,111,024 bytes, so offset 0x1EEEF7 (~2.03MB) is near the end of the code.

## What Writes Seed Function Addresses?

The ONLY things that write seed function addresses during compilation:
1. **`apply-func-addr-patches-direct`** — writes `text-base + resolved` to the code buffer. But bounds checks confirm all offsets are valid.
2. **Return addresses pushed by `call` instructions** — these go on the STACK (at 0x7FFFxxxx), not the heap.
3. **The `fa-offsets`/`fa-targets` lists** — these store the OFFSET positions (where to patch), not the actual addresses.

NONE of these should write to heap address 0x1a6f7c8.

## What To Do Next

**READ THE EMITTER CODE.** Specifically, read what happens during emission of a 23-arm match on a sum type with field-carrying constructors. The relevant code path is:

- `codex/Emit/X86_64Compound.codex`: `emit-match`, `emit-one-match-branch`, `emit-pattern` (IrCtorPat case), `bind-ctor-fields`
- `codex/Emit/X86_64.codex`: `emit-all-defs`, `emit-function`, `emit-expr`
- `codex/Emit/X86_64State.codex`: `codegen-carry-forward`, `st-append-code`, `alloc-local`, `load-local`

**The WHPX-vs-TCG difference is the key clue.** WHPX and TCG execute identical instructions on identical data (no interrupts reach the compiler). The ONLY difference should be:
- Memory layout of QEMU's virtual machine control structures
- Timing of VM exits for serial I/O port access
- Whether QEMU's internal serial buffer delivers bytes differently

If the serial I/O delivers source bytes differently (partial reads, different chunking), the compiler's source-reading loop could produce different heap allocation patterns. This changes WHERE TypeBindings land in memory, which changes whether the corruption hits a live record or harmless free space.

**The fundamental question:** What operation writes 4 bytes to address 0x1a6f7c8 during plug emission that doesn't happen during self-compilation? It's writing a value that looks like a seed function address. The only seed function addresses that exist at runtime are in the code section itself and as return addresses on the stack. Something is copying a return address (or code pointer) from the stack to the heap at the wrong location.

## Session 5 — 2026-05-17 (agent gollum)

### New Crash Signature (depot seed CL 1606)

```
!EXC=0e RIP=0000000000274b54 RBX=0000000001a80ce5 R12=0000000000c597d5
R13=0000000000000000 R14=0000000000000000 R10=0000000001a71cb0
RDI=0000000001a80ce5 RSI=0000000000c597d5 CallR=000000007fffe5c0
CR2=00002ef19c000000 R15=0000000001a6f7c5
```

- **RIP = 0x274B54**: Inside `skip-to-next-line` (parser function, 0x274A57+253 of 310 bytes)
- **R15 = 0x1A6F7C5**: Still the same misaligned pointer from sessions 2-4
- **Crash is IDENTICAL between codex-vm (with shadow register file) and QEMU WHPX** — same RIP, same CR2, same R15, same R10, same everything. This proves the bug is in GUEST CODE, not the VM.

### Machine Code at Crash Site

```
0x274B4D: mov r15, [rbp-0x70]     ; load list element from stack local
0x274B51: mov rax, [r15]          ; load .name from TypeBinding at R15
0x274B54: mov rcx, [rax]   ← FAULT; dereference .name (CR2 = corrupt text ptr)
```

Pattern: load element from list → load .name field → dereference. The list element at index R14=0 is the misaligned pointer 0x1A6F7C5.

### Critical Discovery: Corrupt Value Is a CODE Address

CDX header analysis:
- **Code section**: 0x100000 – 0x2FA408 (2,073,608 bytes)
- **Data section**: 0x2FA408 – 0x304690 (41,608 bytes)
- **0x2EF19C is in the CODE section** (trampoline/helper area)

The symbol map's last function (`__start`) ends at ~0x2C7D75. Between 0x2C7D75 and 0x2FA408 are 206 KB of unmapped runtime helpers (partial-application trampolines, ISR stubs, syscall handler).

The bytes at 0x2EF19C decode as a partial-application trampoline:
```
E9 1E 00 00 00   jmp +30       ; skip to actual code
4D 89 C9         mov r9, r9    ; arg shift NOPs
4D 89 C0         mov r8, r8
48 89 C9         mov rcx, rcx
...
48 B8 xx xx xx xx movabs rax, <target>  ; load function address
```

The corrupt 8-byte value at [R15+0] = 0x00002EF19C000000. In LE memory: `00 00 00 9C F1 2E 00 00`. Bytes 3-6 contain `9C F1 2E 00` = LE for 0x002EF19C — the trampoline address. This is the low 4 bytes of a movabs immediate or a function-address patch.

### Crash Phase: LOWER (confirmed)

- **R10 = 0x1A71CB0**: 21.4 MB past heap base. Consistent with the LOWER phase's deck allocation (after lex+parse+desugar+scope+check decks ≈ 7-8 MB actual, then lower deck allocations).
- **IR mode also crashes**: Same RIP, same CR2 (R15 shifts by 0x58 = 88 bytes due to different mode header size). IR mode runs through LOWER but NOT emit. Confirms the crash is in the LOWER phase or earlier.
- RIP is in `skip-to-next-line` (a parser function). This is the function that the compiler compiled at that address. The crash occurs because a list element dereference pattern at that code offset accesses corrupted heap data.

### Ruled Out This Session

1. **codex-vm shadow register file as fix**: Crash is identical between codex-vm and QEMU WHPX. The shadow register file (CL 1606) has no effect on this crash.
2. **sort-test bug**: The sort-test "failure" in the test battery is a CRLF line-ending mismatch, not a real sort bug. The sort output is correct.
3. **TCG as comparison baseline**: TCG doesn't boot (READY timeout within 30s). Cannot use TCG for sentinel-fill analysis (Probe A).
4. **Data section addresses**: Previous sessions theorized the corrupt value was a data-section address. It's actually a **code-section** address (trampoline area, 0x2EF19C).

### What Writes 4-Byte Code Addresses?

The 4-byte code address is written at the aligned address 0x1A6F7C8 (= R15+3). Candidates:
1. **Closure code-ptr field**: Closures are [code-ptr, env-ptr, captures...]. If a closure is allocated adjacent to a TypeBinding and the base address is off by 3 bytes, the code-ptr write overlaps.
2. **Trampoline movabs patch**: The `movabs rax, imm64` instruction in trampolines is patched with the target function address. If a patch writes to the wrong buffer location...
3. **Return address copy**: Something copies a return address from the stack to the heap at the wrong offset.

### Next Steps

1. **Trace closure allocation**: In the lower phase's `sort-bindings` call, the comparator `\a b -> text-compare (a.name) (b.name)` is a closure. Check how it's allocated and whether its code-ptr write could overflow into adjacent memory.
2. **Check `emit-load-known-func-offset`**: This function creates movabs patches at specific code offsets. If the offset is wrong, the patch writes to the wrong location.
3. **Heap layout analysis**: Determine what's allocated immediately before 0x1A6F7C8 and immediately after. If a closure record starts at 0x1A6F7C0 (8-aligned), its code-ptr at +0 would be at 0x1A6F7C0, overlapping bytes 0-7 — NOT 0x1A6F7C8. The 3-byte offset doesn't match a simple adjacency.

## Session 6 — 2026-05-20 (agent eek): RESOLVED

### Root Cause

Non-short-circuit `&` in `lookup-expr-type` (Unifier.codex, line 143):

```codex
in if pos < len & (list-at entries pos).key == k
```

`&` on Booleans compiles to `IrAnd`, which evaluates BOTH operands
unconditionally. When `pos >= len`, the right side executes
`list-at entries pos` — an out-of-bounds read past the end of the
`expr-types` list. This reads whatever is adjacent on the heap:
stale data, a partial record, or a code-section address.

The OOB value propagated through the type environment during the
CHECK phase. By the LOWER phase, the sorted `all-types` list
(`sort-bindings (types & env.bindings)`) contained a corrupt entry.
`bsearch-text-pos` hit this entry at the midpoint, dereferenced the
corrupt `.name` field (which contained bytes from a `builtin-type-env`
return address shifted by 3 bytes), and page-faulted.

### Why WHPX-Only

The bug fires under both WHPX and TCG. Serial I/O chunking differences
change heap layout during the LEX phase. Under TCG the OOB read
happened to land on zeros or harmless padding; under WHPX it landed
on a live code-section address, producing a visibly corrupt pointer.
The crash was latent under TCG, not absent.

### Probe B Result

All four drifting CR2 values (0x2EE56A, 0x2EE7AF, 0x2EEEF7, 0x2EF19C)
resolve to `builtin-type-env` (0x2E7888, 52079 bytes) in the current
seed's symbol map. These are return addresses from within that massive
function, which builds the initial type environment during CHECK. The
OOB read captured these stale return addresses from the stack or from
previously-freed bivy scratch adjacent to the list.

### Fix

CL 1845 (Damian, 2026-05-19) split the non-short-circuit guard into
nested `if` statements:

```codex
in if pos < len
   then if (list-at entries pos).key == k
        then (list-at entries pos).ty
        else ErrorTy
   else ErrorTy
```

### Verification

Plug build with post-CL-1845 seed succeeds cleanly:

```
[csharp-plug] bundled 2969 lines, 128486 bytes
[csharp-plug] OK: csharp-plug.cdx (360632 bytes)
```

No page fault. No `!EXC`. Status: **CLOSED**.

## Files Modified During Investigation

- `codex/Emit/X86_64Boot.codex` (CL 1552-1553, 1557): Added CR2 + R15 to exception dump. Added register restore on preemption no-switch path (irrelevant to bug but correct fix for a latent scheduler issue).
- `codex.build/gdb-watchpoint.ps1` (CL 1554, 1557): Reusable GDB watchpoint tool for QEMU TCG/WHPX debugging.

## Environment

- QEMU: `D:\Program Files\qemu\qemu-system-x86_64.exe` with WHPX
- Seed: `build-output/bare-metal/Codex.cdx` (CL 1557, 2,111,024 bytes)
- Plug source: `plugs/csharp/build-output/plug-source.codex` (130,898 bytes, 4393 lines)
- WSL has `/usr/bin/gdb` (GDB 15.1) and `/usr/bin/qemu-system-x86_64` (but no KVM)
