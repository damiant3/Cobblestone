# The x86-64 Decoder in codex-vm

**Capability:** decode every instruction the Codex x86-64 compiler emits to an
exact length and a readable mnemonic, inside `codex-vm`, both live (crash
reports, the debugger) and offline over a CDX and its map (codegen censuses).
**Status:** proposal, scoped 2026-10-02 (red, at Damian's direction). Unowned.

## Why

Three consumers need the same decode, and none has a correct one.

1. **Crash reports and the debugger.** `dbg_disasm_at` (`tools/codex-vm.c:9711`)
   prints "Code at RIP" and serves the `d` / `disasm` debugger command through
   `disasm_one` (`:9494`). A wrong length desynchronises every line after it.
2. **Codegen censuses over any CDX.** The compiler's own census (the
   `callcensus` mode flag, CDX6014, `.census` pins) counts only what that
   compiler emits, while compiling, through `x86-insn-len`. A census of a
   binary already built, the seed included, has no instrument: `build/ablate.ps1`
   (`Make-CoffObj` and `Disasm-Obj`, from `bench/disasm-cdx.ps1`) shells out
   to Visual Studio's `dumpbin`, a dependency outside Windows + codex-vm.
3. **MMIO sizing.** `mmio_decode` (`:4496`) already walks prefixes, REX, ModRM,
   SIB and displacement correctly, but only for the MOV forms a device access
   takes. It is hardware-proven and stays the reference for that walk.

## Prior art: the compiler's own length walk

`codex/compiler/Emit/X86_64InsnCount.codex` is a refusing length decoder in
Codex, used by `emit-wcet-check` (`X86_64.codex`, CDX6012) to count the
instructions of punctual functions. It handles prefixes 66 / F0 / F2 / F3, one
REX, ModRM with SIB, no-base SIB disp32 and RIP-relative (`x86-modrm-tail`), the
F7 /0 immediate (`x86-len-f7-group`), and answers -1 rather than guess. Its
table covers what punctual code and the call census meet, not the whole
vocabulary: it has no VEX, no 67 or segment prefixes, and refuses C2, A4, 6E,
0F A2, 0F 20 / 21 / 22 / 23 and the other boot forms below.

It is the second implementation the graders use (G5), and the starting table
for the C one. Two implementations written from the same reading of the SDM
can share a mistake (L-BOTHARMS), which is why G1 exists.

## What exists in codex-vm, and what is wrong with it

`disasm_one` decodes REX (as the first byte only); push and pop; `ret`, `nop`,
`int3`; `call` and `jmp` rel; `jcc` rel8 and rel32; `mov` 89 / 8B / C7 / B8+r;
`add`, `sub`, `cmp`, `test`, `xor` in their register forms; `lea`; groups 81 /
83 / F7 / FF (`inc`, `dec`, `call`, `jmp`, `push`); and from the 0F map
`setcc`, `imul`, `movzx` / `movsx` byte. Defects, each read from the source:

| id | defect | consequence |
|---|---|---|
| D1 | No legacy prefixes: 66, F0, F2, F3 fall to the raw fallback | every SSE op, `rep` string op, `lock` op and 16-bit store is missized |
| D2 | No SIB (`rm == 4`), no no-base SIB disp32, no RIP-relative (`mod 0, rm 5`) | missized: `emit-mem-operand` puts a SIB byte on every RSP or R12 base; `cmp-r-abs`, `cmp-abs-imm8` and `call-abs-ind` (every PE import call) use no-base SIB disp32; `lea-rr` uses SIB with an index; `mov-load-rip-rel` is RIP-relative |
| D3 | The displacement is skipped only for 89 / 8B / 8D | 01 03 29 2B 39 3B 85 31 81 83 C7 F7 FF and 0F AF / B6 / BE with `mod` 1 or 2 come out 1 or 4 bytes short |
| D4 | An unknown opcode returns the bytes consumed so far and the listing goes on; the 0F path prints `0F xx ...` and goes on | every line after the first unknown is garbage printed as instructions (L-BAILVALUE) |
| D5 | Branch targets are printed as `imm + pos`, relative to the instruction, and the annotation resolves `addr + target` with `addr` the window start; the `target > 0x100000` gate is applied to that relative value; the `c` / `j` first-letter test reads any `0x` immediate as a target | every annotated call or jump after the first line names the wrong symbol, a forward branch shorter than 1 MB is never annotated, and the printed targets are not addresses |
| D6 | Opcodes the compiler emits are absent (below) | missized |
| D7 | F7 reads no immediate | `test-ri` (F7 /0 imm32) comes out 4 bytes short |

**The vocabulary (D6)** is everything the x86-64 emitters write, which is two
sources: the emitters in `codex/compiler/Emit/X86_64Encoder.codex`, and the
raw byte lists in `Emit/X86_64Boot.codex` and the other `Emit/X86_64*.codex`
chapters (246 literal lists of three or more numbers, 2026-10-02). Absent from
`disasm_one` today:

- Encoder, one-byte map: 88 (byte store), 63 (`movsxd`), 99 (`cqo`), C1 / D3
  (shifts), 09 (`or`), 21 (`and`), 69 (`imul` imm32), 68 (`push` imm32), 87
  (`xchg`), C2 (`ret imm16`), EC / EE and 66 ED / 66 EF (port I/O), F3 66 6D /
  F3 66 6F / F3 6E (`rep ins` / `outs`), F3 AA / F3 A4 (`rep stos` / `movs`),
  FC / FA / FB / F4 (`cld`, `cli`, `sti`, `hlt`), 48 CF (`iretq`), F3 90
  (`pause`).
- Encoder, 0F map: B7 / BF (word extends), F0 0F B1 / F0 0F C1 (`lock cmpxchg`,
  `lock xadd`), A2 (`cpuid`), 05 (`syscall`), 01 (`lidt`, `xsetbv`, `swapgs`),
  20 / 22 (CR4 moves), AE (`fxsave`, `fxrstor`, `xsave`, `xrstor`, `mfence`).
- Encoder, SSE: none, F2, F3 or 66 before 0F 10 11 14 2A 2C 2E 50 54 55 56 57
  58 59 5A 5C 5E 6E 7E C2 C6 D4 FB, with REX between the prefix and 0F.
- Encoder, VEX (C4 / C5, map 0F) for the AVX `-y` forms: 10 11 50 54 55 56 58
  59 5C 5E C2, and 77 (`vzeroupper`).
- Boot sequences (`X86_64Boot.codex`, for example): A8 ib (`test al`, :1204),
  0F 21 / 0F 23 (debug registers, :1415, :1425), 0F 1F (multi-byte `nop`,
  :2058), 0F 00 (`ltr`, :2227), 0F 30 / 0F 32 (`wrmsr`, `rdmsr`, :2251-2253),
  48 CB (`retfq`, :2456), 66 B8 iw (:2457), 8E (`mov sreg`, :2459). The full
  set is whatever G3 reports undecoded on its first run.

## Scope, in order

1. **One length walk.** First, legacy prefixes (66 67 F0 F2 F3 and the segment
   overrides); then REX; then VEX (C4 / C5); then the opcode maps (one byte and
   0F; the compiler emits nothing from 0F 38 or 0F 3A); then ModRM, SIB and
   displacement, no-base and RIP-relative included; then the immediate, sized
   by an opcode table seeded from `X86_64InsnCount.codex`. `mmio_decode` keeps
   its own walk until this one passes every grader below, and only then calls
   it, because the MMIO path is hardware-proven (L-FALLBACK).
2. **Mnemonics for the vocabulary,** in the existing Intel-order style, branch
   targets printed as absolute addresses. Anything outside the vocabulary prints
   `(undecoded XX ...)`.
3. **Refuse instead of guessing (D4).** An undecoded byte ends the live
   listing at that line; the offline census counts it and names the function.
4. **Fix D5:** compute the absolute target from the instruction's own address,
   and annotate from the decoded operand kind (a rel32 or rel8 branch), never
   from the mnemonic text.
5. **Offline mode:** `codex-vm -disasm <cdx> [-map <file>] [-func <name>]`
   prints a listing without booting; `-disasm-census` prints one line per
   function: instruction count, push, pop, stores through RSP or RBP,
   undecoded bytes. Both go in the argument table, which refuses an
   unrecognised flag (L-ACCEPTED).

Not in scope: x87, AVX-512, 16- and 32-bit modes, AT&T syntax, the full ISA,
and sharing code between the C decoder and the Codex one (they stay two
implementations, because G5 needs two). The arm64 and riscv plugs carry their
own disassemblers (`Arm64Disasm.codex`, `RiscVDisasm.codex`).

## Graders

- **G1, the SDM table (independent oracle).** One row per form, hand-assembled
  from the Intel SDM rather than from our encoder: bytes, length, mnemonic. A
  grader built only on our encoder would agree with the encoder's mistakes
  (L-ORACLE).
- **G2, the encoder sweep (completeness).** A Codex chapter that calls every
  emitter in `X86_64Encoder.codex` with operands covering every register class,
  every `mod`, the RSP / R12 / RBP / R13 bases, the no-base form and the disp8 /
  disp32 boundaries (-128, 127, 128), and prints the bytes with their
  boundaries. The decoder must reproduce every boundary.
- **G3, the seed corpus.** Decode all of `seed/Codex.cdx` through its map. On
  2026-10-02 the map's 6380 functions tile the text section from 0x100114 to
  0x42E84B with zero gaps, so the bar is exact: every function decodes to
  exactly its map size with 0 undecoded bytes, and the run prints N of the
  function count (L-DENOM). Data embedded in a function body, if the compiler
  emits any, shows up here as undecoded bytes and is named by function.
- **G4, the annotation arm (D5).** A crash whose window holds a `call` to a
  known symbol after the first line, and a forward branch shorter than 1 MB;
  both annotations must name their symbols.
- **G5, the second implementation.** Over the G3 corpus, every instruction
  where `x86-insn-len` answers a length, the C decoder answers the same length;
  the run prints how many instructions both decoded and how many only one did.
- **Sabotage control.** Shorten one displacement size in the table: G1, G3 and
  G5 must all go red (L-FALSIF).

## Cost and landing

R-COST: decoding is linear in bytes with fixed-size line buffers; the corpus
census is one pass over 3.3 MB of text and allocates nothing per instruction.
`codex-vm.c` is not seed-affecting, so the work takes no build token, but
`tools/codex-vm.exe` is shared by every lane: rebuild it with
`tools/build-vm.ps1` only with no other lane's guest running, which is the
commander's to arbitrate.

**What it unblocks:** a census of any built CDX, and `ablate.ps1` without
`dumpbin`.
