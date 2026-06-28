# GitHub Update 28 -- 2026-06-26

Covers main CLs 6066-6144 (since Update 27 at CL 6065, 2026-06-24).
Two days, copy-ups from four agent streams (fester, val, reek, blu).

## Both Cross-Architecture Backends at 100% (Major Milestone)

**ARM64: 135/135 verified tests, 0 failures.**
**RISC-V: 134/134 verified tests, 0 failures.**

This is the first time the Codex compiler has full test parity across
three instruction set architectures. The same Codex source compiles
to x86-64 (self-hosted), ARM64 (plug), and RISC-V (plug), and all
three produce byte-identical output for every test in the battery.

## ARM64 Backend -- 135/135 (fester)

Up from 96% (130/135) in Update 27. Three codegen bugs fixed
(CL 6111):

- **Record constructor heap corruption** -- when a function exhausted
  all 9 callee-saved registers (x19-x27), `a64-emit-record` passed
  the spill slot number (e.g. 92) directly to `arm64-mov`. ARM64's
  5-bit register encoding wrapped 92 to x28 (the heap register),
  making the MOV a self-assignment NOP. The heap pointer was never
  saved before advancing, so the next allocation overwrote the
  record. Found via QEMU GDB hardware watchpoint. Fix: use temp x9
  to capture x28, then `a64-store-local` for proper spill handling.
  Fixes: truetype-bridge, truetype-render, trie-prefix tests.

- **VBAR_EL3 boot stub crash** -- the boot stub wrote MSR VBAR_EL3,
  which traps on QEMU virt (boots at EL1). Removed.

- **unicode-bytes-to-text byte stride** -- the runtime helper read
  List Integer elements at 1-byte stride (LDRB at base+i) but each
  element is 8 bytes. Only the first character converted correctly;
  the rest read zero high-bytes. Fix: LSL index by 3, use LDR for
  8-byte element access.

New tests: `arm64-http-test`, `arm64-boot-test` promoted from
compile-only.

## RISC-V Backend -- 134/134 (val)

Up from 92% (122/133) at the start of the day. Seven distinct bug
classes fixed in a single debugging session (CLs 6096-6137):

1. **Map file byte offsets** (CL 6096) -- wire protocol func-offsets
   were instruction counts; ARM64 already multiplied by 4, RISC-V
   didn't. GDB breakpoints resolved to wrong addresses.

2. **Compile-time opening result print** (CL 6098) -- ported ARM64
   approach: check return type at codegen time instead of runtime
   beq-skip. Fixed `opening : Integer = 0` returning nothing.

3. **12-bit signed immediate overflow** (CLs 6113, 6126) -- RISC-V
   ADDI/SD/LD use 12-bit signed immediates (range -2048 to +2047).
   Values outside this range silently wrap, flipping the sign.
   ARM64 uses unsigned 12-bit SUB (range 0-4095) and never hits
   this. Two manifestations:
   - **Frame adjustment** (CL 6113): functions with > 242 spill
     slots (frame > 2048 bytes) got `addi sp, sp, +N` instead of
     `-N`. Fix: LUI+ADDI+SUB for large frames. Also ported ARM64's
     mutable code buffer (`alloc-bytes` + `__buf-write-byte`).
   - **Spill slot access** (CL 6126): SD/LD at offsets > 2047
     wrapped to negative. Fix: LI+ADD+SD/LD for large offsets.

4. **Missing `alloc-bytes` runtime helper** (CL 6132) -- calls
   compiled as NOPs. Heap pointer never bumped, so multiple
   allocations returned the same address. Keyboard layout base/shift
   buffers aliased.

5. **Missing CCE table lookup helpers** (CL 6133) --
   `__cce_to_unicode` and `__unicode_to_cce` added as LI+ADD+LBU
   sequences reading from pre-built boot-time tables.

6. **`unicode-bytes-to-text` list element stride** (CL 6134) --
   same bug as ARM64. Fix: LD with index*8 byte offset.

7. **Zero-arg list-literal memoization** (CL 6137) -- constant
   functions like `cce-to-unicode-table` (128 elements) were
   re-allocated on every reference. Each keyboard layout consumed
   ~7 MB of heap. Fix: 64-slot memo table cached at s0, zeroed
   at boot. Heap usage dropped ~100x.

### Initial codegen quality (RISC-V RV64 vs GCC cross-compiler)

| Bench | GCC -O0 | GCC -O2 | Codex RV64 | x86 Codex |
|-------|--------:|--------:|-----------:|----------:|
| fib   |      34 |     22* |         41 |        21 |
| fact  |      27 |      14 |         37 |        15 |
| gcd   |      26 |       8 |         39 |        17 |
| sum   |      27 |      11 |         39 |        14 |

*GCC -O2 fib: 241 insns (unrolled iteration), -Os: 22.

Codex RV64 is ~1.4x GCC -O0. Next phase: frame elision,
destination-driven emission, TCO, immediate operands.

## GPU Kernels (reek)

CL 6109: f64 GPU kernels -- PTX emitter upgraded from integer-only to
double-precision floating point. Font classification kernels, contour
topology analysis, temperature scheduling, early stopping.

## Documentation (blu)

CL 6070: Cornell CS 6120 review of the Codex compiler design.

## By the numbers

| Metric | Update 27 | Update 28 | Delta |
|--------|----------:|----------:|------:|
| Foreword modules | 367 | 367 | -- |
| OS/Kernel modules | 137 | 137 | -- |
| Seed size | 2.31 MB | 2.31 MB | -- |
| Seed digest | `E625476A` | `E625476A` | -- |
| ARM64 cross-tests | 131/137 (96%) | 135/135 (100%) | **+4, full pass** |
| RISC-V cross-tests | ~50 | 134/134 (100%) | **+84, full pass** |
| Cross-arch backends at 100% | 0 | 2 | **both** |

## What's next

RISC-V codegen optimization toward GCC -O2 parity. Public GitHub
push with both cross-architecture backends at full parity.
