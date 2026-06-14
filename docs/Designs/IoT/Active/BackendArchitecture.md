# Backend Architecture: ARM and RISC-V Targets

**Created**: 2026-06-12 (reek)
**Status**: Design — not yet started
**Upstream**: `docs/PM/Stories/Vision/CodexIoTPlan.md` Phase 1,
`docs/PM/IoT/AGENT-PROMPT.md` deliverable 1

## The Problem

The self-hosted compiler emits x86-64 only. The IoT targets are ARM
Cortex-M (STM32F4/H7, Thumb-2), ARM Cortex-A (Raspberry Pi 4/5,
AArch64), and RISC-V (ESP32-C6, RV32IMC). The compiler must learn to
emit these architectures without disturbing the x86-64 fixed point.

This is a port, not greenfield: the C# reference compiler shipped a
proven RISC-V backend (`old/src/Codex.Emit.RiscV/`, ~4,100 lines —
the project's first backend) and a proven ARM64 backend
(`old/src/Codex.Emit.Arm64/`, ~2,713 lines). Instruction encoding,
register conventions, closure trampolines, TCO, and ELF emission are
all solved there. The `old/` tree is frozen and read-only; we read it,
we never build it.

## One Honest Correction to the Prospectus

The prospectus says "the architecture decisions are already made."
That is true for **two** of the three targets and **false** for the
third class:

- The old RISC-V backend is **RV64IM** (64-bit, QEMU virt at
  0x80000000, NS16550A UART). The ESP32-C6 is **RV32IMC** (32-bit).
- The old ARM64 backend is **AArch64** (64-bit, Linux ELF at
  0x400000, SVC syscalls). The Raspberry Pi is AArch64 — direct
  match. The STM32 is **Thumb-2** (32-bit) — no prior implementation
  exists anywhere in the project.

Codex `Integer` is a 64-bit value; every record field, list element,
and text length is 8 bytes; the whole runtime model (heap pointer,
collision check, CCE text) assumes 64-bit words. The 32-bit targets
therefore require a word-size decision that no prior backend made.
The phasing below quarantines that decision so the proven 64-bit
ports are not blocked by it.

## Constraints

1. The x86-64 fixed point is untouchable. New backends are additive:
   new `CompileTarget` cases, new Emit chapters. Shared IR changes
   must pass the full existing gates.
2. Cross-compile first. The x86-64 self-host emits ARM/RISC-V
   binaries. Self-hosting on target comes later (Phase 4 of the
   prospectus).
3. No external toolchain. No GNU as, no lld. Codex encodes
   instructions and writes containers itself, as it does today.
4. Safety guarantees are never silently lost. Where a 32-bit target
   cannot represent a 64-bit value, the emitter inserts a runtime
   check or refuses with a diagnostic — never truncates.
5. Container formats stay in plugs (EmitterExodus). The compiler
   emits CDX; ELF/IMG/flash-image wrapping happens in
   `codex/plugs/`.

## What Exists, Precisely

### The pattern to follow: the x86-64 emitter

17 files in `codex/compiler/Emit/`, ~18,500 lines. The structure a
new backend replicates:

| x86-64 file | Role a new backend must fill |
|---|---|
| X86_64State.codex (583) | CodegenState record, register alloc, patch tables |
| X86_64Encoder.codex (453) | Low-level instruction encoding |
| X86_64Helpers.codex (3079) | Operand building, condition codes |
| X86_64.codex (1915) | Expression dispatch, locals, control flow |
| X86_64Compound.codex (1617) | Closures, records, matches, lists |
| X86_64Builtins.codex (1177) | Runtime builtins |
| X86_64TextHelpers.codex (1347) | Text/CCE helpers |
| X86_64ListHelpers.codex (805) | List runtime helpers |
| X86_64Boot.codex (1793) | Memory map, interrupts, process table |
| X86_64Chapter.codex (931) | Start routine, finalize, CDX assembly |
| X86_64IO.codex (397) | Serial/port I/O |

Backend dispatch is a single point: `opening.codex` line ~715
switches on `CompileTarget` (`CtCodexText | CtX86_64Bare | CtCdx`)
and calls `x86-64-emit-cdx-with-exit-mode(ir, sorted-type-defs, ...)`.
Everything upstream of that call — lex, parse, desugar, scope, check,
lower, resolve, lift — is architecture-neutral and shared.

The patch architecture is four parallel accumulator-list pairs in
CodegenState, applied in finalize: call patches (rel32), func-addr
fixups (64-bit absolute), rodata fixups (data vaddr), and
stack-overflow-check redirects. All three old backends used the same
two-pass emit-then-patch shape, so this carries over directly.

### The proven sources in `old/`

**RiscV** (RV64IM): pure-static encoders for all six instruction
formats (R/I/S/B/U/J); `Li` materialization (addi / lui+addiw /
recursive 64-bit split, up to 8 instructions); S1 = heap pointer;
T3–T6 temp rotation; S2–S9 locals then stack spill at Sp+80+;
A0–A7 args then caller stack; closure trampolines via T2; TCO loop
rewrite; 17 inline runtime helpers (str/list/itoa/io); two targets —
Linux ELF (brk heap probe) and bare-metal flat binary at 0x80000000
with stack 0x81000000, heap 0x82000000, UART at 0x10000000.

**Arm64** (AArch64): fixed 32-bit words; MOVZ/MOVK/MOVN `Li` (≤4
instructions); X28 = heap pointer; X12–X15 temp rotation; X19–X27
locals then spill at SP+96+; STP/LDP pair prologue (96-byte base
frame); BL rel26 call patches; same three patch classes; ELF writer
(144 lines) at 0x400000, text RX + rodata R segments, minimal
section table.

### Conventions side by side

| Role | x86-64 (self-host) | RV64 (old) | AArch64 (old) | Thumb-2 (proposed) |
|---|---|---|---|---|
| Heap/bump ptr | R10 | S1 (x9) | X28 | R9 |
| Temps (rotating) | RAX,RCX,RDX,RSI,RDI,R11 | T3–T6 | X12–X15 | R0–R3 pool |
| Locals (callee-saved) | RBX,R12,R13,R14 | S2–S9 | X19–X27 | R4–R8 |
| Closure env | R15 | T2 | X11 | R10 |
| Args | RDI first | A0–A7 | X0–X7 | R0–R3 then stack |
| Frame | RBP, locals at RBP-ofs | S0/Fp, spills Sp+80+ | X29, spills SP+96+ | R7 or SP-relative |
| Stack/heap collision | cmp rsp,r10 in prologue | not in old backend | not in old backend | required (new) |

The old backends predate the collision check, the deck/bivy phase
allocator, and the kernel metadata cells. Cross-compiled IoT
firmware needs: `__alloc` with calloc semantics, the prologue
collision check (mandatory on MCU-sized RAM), `__heap-save` /
`__heap-restore`, and the serial/diagnostic conventions. It does
NOT need the full phase allocator or process table — those are
compiler-self-host machinery and come with Phase 4 self-hosting.

## The Design

### Target taxonomy

```
CompileTarget = | CtCodexText | CtX86_64Bare | CtCdx
                | CtArm64    -- AArch64 bare-metal CDX (Pi 4/5, QEMU virt)
                | CtRiscV64  -- RV64IM bare-metal CDX (QEMU virt)
                | CtRiscV32  -- RV32IMC bare-metal CDX (ESP32-C6)
                | CtThumb2   -- ARMv7-M CDX (STM32F4/H7)
```

Each target gets its own chapter set mirroring the X86_64 file
inventory, prefixed `Arm64`, `RiscV`, `Thumb2`. RV64 and RV32 share
`RiscVEncoder.codex` (the format encoders are width-agnostic; only
word-sized loads/stores and the `Li` strategy differ) behind a
width field in the codegen state.

### Phasing — 64-bit ports first, the 32-bit decision quarantined

**Phase B1 — AArch64 (CtArm64).** Closest to the existing model:
64-bit words, fixed-width encoding simpler than x86's, proven
encoder and ELF writer to transliterate, QEMU `-M virt` and
Raspberry Pi as hardware. Port order inside the phase: Encoder →
State (reuse the four patch-table classes verbatim) → expression
emission → compound/builtins/text/list helpers (transliterate the
17 old helpers, then reconcile against the 30+ helpers the x86-64
runtime has since grown — the x86-64 helper list is the
authoritative inventory of what the foreword needs) → boot chapter
(vector table at 0x80000 Pi convention, GIC init, identity-map MMU
tables — section-level template is X86_64Boot) → finalize/CDX.

**Phase B2 — RV64IM (CtRiscV64).** Direct transliteration of the
first backend. QEMU virt only; no hardware target this phase. Its
purpose is to prove the RISC-V encoder chapters and runtime helpers
on the easy word size before RV32 complicates them.

**Phase B3 — the 32-bit word model (CtRiscV32, CtThumb2).** The
decision (see Open Questions) plus the C-extension encoder for
code density and the Thumb-2 encoder (16/32-bit mixed — the only
fully new encoder in the program). NVIC vector table, CLINT
interrupt model, flash XIP layout (code executes from flash, data
in SRAM — a split the 64-bit targets don't have).

Each phase ends with a demo per Virtue 1: B1 boots on QEMU virt and
a Pi and prints over UART; B2 boots on QEMU virt; B3 blinks the LED
on the physical boards via the board layer (`HardwareAbstractionLayer.md`,
`codex.os.board` quire).

### The 32-bit value model (decided 2026-06-12, Damian)

Bounded integers are range-refinement subtypes of `Integer`, and the
static bounds prover's interval arithmetic is the subtype check. The
32-bit backends consume what the prover already computes — no new
type-system machinery:

- On CtRiscV32/CtThumb2 the machine word is 32 bits; record fields,
  list elements, and text headers use 4-byte slots.
- A value whose proven range fits a 32-bit word compiles to one
  word. Subsumption is free in the narrowing-into-wider direction,
  exactly as today.
- A value with no proven 32-bit-fitting range in a word-sized
  position gets the existing rule from the vision applied at a new
  boundary: the programmer inserts `__narrow` (runtime-checked,
  traps out-of-range), or the emitter refuses with a new diagnostic
  (CDX2xxx, allocated at implementation) stating the inferred range
  and the obligation. Never silent truncation.
- No register-pair 64-bit arithmetic in B3. The rare genuinely
  64-bit quantities in firmware (timestamps, hash words) use an
  explicit pair record or a foreword chapter; register-pair support
  is justified only by real firmware demonstrating the need.
- `#` hash literals denote raw bit patterns; on 32-bit targets a
  pattern with more than 8 significant hex digits gets the same
  refusal (the CDX2071 precedent — the compiler never silently
  truncates a literal).

The consequence worth stating plainly: on MCU targets, `between`
bounds shift from good practice to load-bearing, which is the most
Codex-shaped outcome available — the types are the specification,
and here they are also the port.

### Containers

The compiler emits CDX for every target; the CDX header gains a
`machine` field (one byte, 0 = x86-64 currently implicit — see Open
Questions on header versioning). Plugs wrap CDX per deployment:
the existing ELF plug grows `e_machine` parameterization (183 =
AArch64, 243 = RISC-V — both already proven in the old writers);
a new flash-image plug produces the raw layouts MCU boot ROMs
expect (STM32: vector table at 0x08000000; ESP32-C6: Espressif
image format with the RSA-3072 secure-boot header — its bootloader
chain-verifies, then the Codex verifier governs everything above).

### Verifier and signing

CDX signing and the 5-phase verifier (`codex/os/verify/`) are
architecture-neutral — they hash and verify bytes, capabilities,
and proofs without decoding instructions. Nothing to port; the
`machine` field must simply be inside the signed content range.

## Memory and Time-Complexity Risk

Emit-phase cost model is unchanged: per-function `__heap-save` /
`__heap-restore` scratch, accumulator lists pre-allocated at
capacity, output buffers sized by `defs × 64 KB + 16 MB` survey.
New backends must adopt the same survey discipline — the old
backends' unbounded `List<uint>` instruction accumulation maps to
the existing capacity-checked accumulator pattern, not to ad-hoc
list growth. Fixed 32-bit encodings make ARM/RISC-V code-size
estimation *more* predictable than x86's variable length. Target
RAM budgets (192 KB STM32F4 SRAM, 512 KB ESP32-C6) constrain the
*emitted program's* heap, not the cross-compiling host — the HAL
and runtime designs own that budget; the emitter's obligation is
the collision check in every prologue and honest CDX heap-size
headers.

## Open Questions

1. **32-bit refusal diagnostic ergonomics.** The word-size model
   itself is decided (see "The 32-bit value model" above). What
   remains is making the refusal mechanical to fix: the diagnostic
   should print the prover's inferred interval so the suggested
   `between` bounds (or the `__narrow` site) can be copied straight
   from the message. Settle the wording during B3 with real
   firmware examples.
2. **CDX header `machine` field.** Where in the 224-byte header,
   and does format_version bump? Must coordinate with
   `docs/Designs/OS/Done/CodexBinary.md` owners; affects verifier
   and every existing seed if mishandled. Proposal: claim a byte
   from the existing flags region, default 0 = x86-64, no version
   bump.
3. **Pi boot firmware.** The Pi's GPU loads `kernel8.img` from a
   FAT partition — Codex must produce that via the IMG plug.
   Acceptable, or does the no-borrowed-substrate principle demand
   more? (Recommendation: acceptable — it is unavoidable ROM, same
   status as the x86 UEFI firmware we already tolerate.)
4. **Thumb-2 frame convention.** R7 vs SP-relative frames, and
   whether the 4-arg AAPCS register budget forces more aggressive
   spilling in the emitter's first cut. Decide during B3 design
   review, informed by B1 experience.
5. **Cross-emit of plugs.** Plugs run on the x86 host VM over TCP
   today. Do ARM-target builds run plugs on the host (yes, for
   cross-compile phases) — and does anything in the plug protocol
   assume the IR's pointer width (audit during B1)?
