# Encoder Updates — Missing x86-64 Instructions for Crypto

**Date**: 2026-03-31
**Status**: Design
**Depends on**: MM4 (self-compilation fixed point)
**Unblocks**: CryptoPrimitives.md (SHA-256, Ed25519)
**Companion**: `docs/Codex.OS/LanguageUpdates.md` (language-level bitwise builtins)

---

## The Problem

The x86-64 instruction encoder (both C# and self-hosted Codex) is missing
several instructions required by the crypto primitives. The encoder was
built for compiler codegen — it has what the compiler needs today. Crypto
needs more.

### Current encoder instruction set

The self-hosted encoder (`Codex.Codex/Emit/X86_64Encoder.codex`) supports:

```
MOV    (rr, ri32, ri64, load, store, rip-rel, byte)
ADD    (rr, ri)           SUB    (rr, ri)
IMUL   (rr — 2-operand, signed, truncated to 64 bits)
NEG    (r)                CQO
IDIV   (r)
AND    (rr, ri)           XOR    (rr — 32-bit only, used for zeroing)
SHL    (ri)               SHR    (ri)               SAR    (ri)
CMP    (rr, ri)           TEST   (rr)
SETcc  (cc, r)            MOVZX  (byte)
Jcc    (cc, rel32)        JMP    (rel32)
CALL   (rel32)            RET                        NOP
PUSH   (r)                POP    (r)
LEA    (r, [r+off])       LI     (r, imm — shortest encoding)
SYSCALL, OUT, IN, HLT, PAUSE, CLI, STI, IRETQ, LIDT, SWAPGS
```

### What's missing for crypto

| Instruction | Needed by | Impact |
|------------|-----------|--------|
| **OR r64, r64** | SHA-256 (Ch, Maj, rotate composition), Ed25519 (ct-select) | Hard blocker. No bitwise OR at all. |
| **NOT r64** | SHA-256 (Ch: `NOT x AND z`), Ed25519 (ct-select complement) | Hard blocker. No complement. |
| **XOR r64, r64** (64-bit) | SHA-256 (Ch, Maj, Sigma), Ed25519 (field arithmetic, ct-eq) | Hard blocker. Current XOR is 32-bit only. |
| **MUL r/m64** (unsigned, RDX:RAX) | Ed25519 field multiply (5x51 produces 102-bit products) | Hard blocker for Ed25519 performance. |
| **ROR r64, imm8** | SHA-256 Sigma functions (384 rotations per hash) | Performance blocker. 3x overhead without it. |
| **RDTSC** | Phase E timing tests (constant-time verification) | Testing blocker. Not needed for correctness. |

---

## Instruction Encodings

Each instruction below shows the exact byte encoding, following the
encoder's existing patterns. The `rex-w`, `modrm`, and helper functions
are already defined. The C# encoder (`X86_64Encoder.cs`) should get
matching implementations, but since crypto is post-MM4, only the
self-hosted Codex encoder is strictly required.

### 1. OR r64, r64

Bitwise OR. Same encoding pattern as AND (0x21) but opcode 0x09.

```
or-rr : Integer -> Integer -> List Integer
or-rr (rd) (rs) =
  [rex-w rs rd, 9, modrm 3 rs rd]
  -- 9 = 0x09 OR r/m64, r64
```

For completeness, register-immediate form (ALU extension /1):

```
or-ri : Integer -> Integer -> List Integer
or-ri (rd) (imm) = alu-ri 1 rd imm
-- Uses existing alu-ri helper with ext=1 (ADD=0, OR=1, AND=4, SUB=5, XOR=6, CMP=7)
```

Note: the existing `alu-ri` helper already supports arbitrary extension
codes. It currently documents `ext: 0=ADD, 4=AND, 5=SUB, 7=CMP`. The
OR encoding simply uses ext=1. No changes to `alu-ri` needed.

### 2. NOT r64

One's complement. Uses opcode group 0xF7 /2 (same group as NEG /3 and
IDIV /7, all already in the encoder).

```
not-r : Integer -> List Integer
not-r (rd) =
  [rex-w 0 rd, 247, modrm 3 2 rd]
  -- 247 = 0xF7 /2 NOT r/m64
```

Compare with the existing `neg-r`:

```
neg-r (rd) = [rex-w 0 rd, 247, modrm 3 3 rd]   -- 0xF7 /3
```

Identical except extension field: 2 for NOT, 3 for NEG.

### 3. XOR r64, r64 (64-bit)

The encoder already has `xor-rr` but it emits a 32-bit XOR (no REX.W):

```
-- Current (line 359-364): 32-bit XOR, zero-extends upper 32 bits
xor-rr (rd) (rs) =
  let rex-byte = rex False (rs >= 8) False (rd >= 8)
  in ...  [49, modrm 3 rs rd]
  -- 49 = 0x31 XOR r/m32, r32
```

Crypto needs 64-bit XOR. Two options:

**Option A: Add a separate `xor-rr64` function.**

```
xor-rr64 : Integer -> Integer -> List Integer
xor-rr64 (rd) (rs) =
  [rex-w rs rd, 49, modrm 3 rs rd]
  -- 49 = 0x31 with REX.W: XOR r/m64, r64
```

Same opcode (0x31), just adds REX.W prefix. The existing `xor-rr`
(32-bit) is still needed for the `li` zero-idiom (`xor eax, eax` is
shorter than `xor rax, rax` and both zero the full 64-bit register).

**Option B: Rename current to `xor-rr32`, add `xor-rr` as 64-bit.**

This would break existing code that calls `xor-rr`. Option A is safer —
additive, no renaming.

**Decision: Option A.** Add `xor-rr64` alongside the existing `xor-rr`.

For register-immediate, use `alu-ri` with ext=6:

```
xor-ri : Integer -> Integer -> List Integer
xor-ri (rd) (imm) = alu-ri 6 rd imm
-- ext=6 for XOR in the ALU group
```

### 4. MUL r/m64 (unsigned, 128-bit result)

Unsigned multiply. One-operand form: RDX:RAX = RAX * operand. This is
the critical instruction for Ed25519 field multiplication — the 5x51
representation produces 102-bit intermediate products.

```
mul-r : Integer -> List Integer
mul-r (rs) =
  [rex-w 0 rs, 247, modrm 3 4 rs]
  -- 247 = 0xF7 /4 MUL r/m64 (unsigned)
  -- Result: RDX:RAX = RAX * rs
```

Compare with existing instructions in the 0xF7 group:

```
neg-r  (rd) = [rex-w 0 rd, 247, modrm 3 3 rd]   -- /3 NEG
not-r  (rd) = [rex-w 0 rd, 247, modrm 3 2 rd]   -- /2 NOT (new, above)
idiv-r (rs) = [rex-w 0 rs, 247, modrm 3 7 rs]   -- /7 IDIV
mul-r  (rs) = [rex-w 0 rs, 247, modrm 3 4 rs]   -- /4 MUL (new)
```

Same opcode group, different extension. Mechanical to add.

**Usage in field multiply**: Before calling `mul-r`, the emitter must:
1. Load one operand into RAX (register 0)
2. Call `mul-r` with the other operand register
3. Read the 128-bit result from RDX:RAX (RDX = high 64 bits, RAX = low 64)
4. The emitter is responsible for the RAX/RDX register discipline

This is an emitter-level concern, not an encoder concern. The encoder
just encodes the instruction.

### 5. ROR r64, imm8

Rotate right by immediate count. Opcode group 0xC1 /1 (same group as
SHL /4 and SHR /5, already in the encoder).

```
ror-ri : Integer -> Integer -> List Integer
ror-ri (rd) (imm) =
  [rex-w 0 rd, 193, modrm 3 1 rd, imm]
  -- 193 = 0xC1 /1 ROR r/m64, imm8
```

Compare with existing shift instructions:

```
shl-ri (rd) (imm) = [rex-w 0 rd, 193, modrm 3 4 rd, imm]   -- /4 SHL
shr-ri (rd) (imm) = [rex-w 0 rd, 193, modrm 3 5 rd, imm]   -- /5 SHR
sar-ri (rd) (imm) = [rex-w 0 rd, 193, modrm 3 7 rd, imm]   -- /7 SAR
ror-ri (rd) (imm) = [rex-w 0 rd, 193, modrm 3 1 rd, imm]   -- /1 ROR (new)
```

Same opcode, same encoding pattern. Different extension field.

**SHA-256 impact**: Without ROR, each 32-bit rotation is synthesized as:

```
-- Without ROR: 3 instructions per rotation
rotr32 x n = or-rr (shr-ri x n) (shl-ri x (32 - n))
```

SHA-256 has 6 rotations per round x 64 rounds = 384 rotations per hash.
Without ROR: 384 x 3 = 1,152 instructions. With ROR: 384 instructions.
That's 768 extra instructions, roughly 2-3x overhead on the inner loop.

Note: SHA-256 rotates 32-bit words, not 64-bit. `ROR r64, imm8` rotates
the full 64-bit register. For 32-bit rotation, the emitter must mask
the value to 32 bits first (`and-ri rd 0xFFFFFFFF`) or use the 32-bit
operand-size override (omit REX.W). The 32-bit form:

```
ror-ri32 : Integer -> Integer -> List Integer
ror-ri32 (rd) (imm) =
  let rex-byte = rex False False False (rd >= 8)
  in let pfx = if rex-byte /= 64 then [rex-byte] else []
  in pfx ++ [193, modrm 3 1 rd, imm]
  -- 0xC1 /1 without REX.W: ROR r/m32, imm8 (32-bit rotate)
```

The 32-bit form is what SHA-256 actually needs. The 64-bit form is
useful for SHA-512 (which uses 64-bit words).

### 6. RDTSC

Read Time Stamp Counter. Two-byte opcode. Result in EDX:EAX (high 32
bits in EDX, low 32 bits in EAX).

```
rdtsc : List Integer
rdtsc = [15, 49]
-- 15 49 = 0x0F 0x31 RDTSC
-- Result: EDX:EAX = 64-bit timestamp counter
```

Two bytes, no operands. Trivial to add. Used only for Phase E timing
tests (constant-time verification), not for crypto operations themselves.

---

## Additional Encoder Functions for Completeness

These are not strictly required for crypto but round out the ALU group
and would be needed by the emitter for general bitwise support:

### SHL r64, CL and SHR r64, CL (variable shift)

The encoder has immediate-count shifts (`shl-ri`, `shr-ri`) but not
variable-count shifts where the count is in the CL register. The
`bit-shl` and `bit-shr` language builtins (see `LanguageUpdates.md`)
need variable shifts for the general case.

```
shl-rcl : Integer -> List Integer
shl-rcl (rd) =
  [rex-w 0 rd, 211, modrm 3 4 rd]
  -- 211 = 0xD3 /4 SHL r/m64, CL

shr-rcl : Integer -> List Integer
shr-rcl (rd) =
  [rex-w 0 rd, 211, modrm 3 5 rd]
  -- 211 = 0xD3 /5 SHR r/m64, CL
```

The emitter must move the shift count into RCX (register 1) before
calling these. SHA-256 and Ed25519 use only constant shift amounts, so
the immediate forms suffice for crypto. Variable shifts are needed for
the general-purpose builtins.

---

## Summary: All New Encoder Functions

| Function | Opcode | Extension | Pattern matches |
|----------|--------|-----------|-----------------|
| `or-rr` | 0x09 | — | `and-rr` (0x21) |
| `or-ri` | via `alu-ri` | ext=1 | `and-ri` (ext=4) |
| `not-r` | 0xF7 | /2 | `neg-r` (/3) |
| `xor-rr64` | 0x31 + REX.W | — | `xor-rr` (0x31, no REX.W) |
| `xor-ri` | via `alu-ri` | ext=6 | `and-ri` (ext=4) |
| `mul-r` | 0xF7 | /4 | `idiv-r` (/7) |
| `ror-ri` | 0xC1 | /1 | `shl-ri` (/4) |
| `ror-ri32` | 0xC1 (no REX.W) | /1 | — |
| `rdtsc` | 0x0F 0x31 | — | `syscall` (0x0F 0x05) |
| `shl-rcl` | 0xD3 | /4 | `shl-ri` (0xC1 /4) |
| `shr-rcl` | 0xD3 | /5 | `shr-ri` (0xC1 /5) |

Every new instruction follows an existing encoding pattern in the
encoder. None require new encoding helpers or new addressing modes.
The `rex-w`, `modrm`, `alu-ri`, and `write-bytes` helpers are sufficient.

---

## Testing

Each new instruction must be tested byte-for-byte against a reference
assembler (NASM or the C# encoder). The test format matches the existing
golden tests in `tests/Codex.Emit.Tests/X86_64EncoderGoldenTests.cs`:

```
-- Example: or-rr RAX, RCX should produce [0x48, 0x09, 0xC8]
-- 0x48 = REX.W, 0x09 = OR, 0xC8 = ModRM(11, RCX=1, RAX=0) = 3*64 + 1*8 + 0
assert (or-rr 0 1) == [72, 9, 200]

-- Example: not-r RDI should produce [0x48, 0xF7, 0xD7]
-- 0xD7 = ModRM(11, /2, RDI=7) = 3*64 + 2*8 + 7
assert (not-r 7) == [72, 247, 215]

-- Example: mul-r RBX should produce [0x48, 0xF7, 0xE3]
-- 0xE3 = ModRM(11, /4, RBX=3) = 3*64 + 4*8 + 3
assert (mul-r 3) == [72, 247, 227]

-- Example: ror-ri RAX, 7 should produce [0x48, 0xC1, 0xC8, 0x07]
-- 0xC8 = ModRM(11, /1, RAX=0) = 3*64 + 1*8 + 0
assert (ror-ri 0 7) == [72, 193, 200, 7]

-- Example: rdtsc should produce [0x0F, 0x31]
assert rdtsc == [15, 49]
```

Extended register tests (R8-R15) must also be included to verify REX
prefix handling.

---

## Implementation Order

| Step | What | Effort | Notes |
|------|------|--------|-------|
| 1 | Add `or-rr`, `or-ri`, `not-r` | Trivial | Same pattern as existing AND/NEG |
| 2 | Add `xor-rr64`, `xor-ri` | Trivial | REX.W variant of existing XOR |
| 3 | Add `mul-r` | Trivial | Same 0xF7 group as NEG/IDIV |
| 4 | Add `ror-ri`, `ror-ri32` | Trivial | Same 0xC1 group as SHL/SHR |
| 5 | Add `rdtsc` | Trivial | Two-byte fixed encoding |
| 6 | Add `shl-rcl`, `shr-rcl` | Trivial | Same 0xD3 group pattern |
| 7 | Golden tests for all new instructions | Small | Byte-for-byte against NASM |
| 8 | Update `alu-ri` comment to document all ext codes | Trivial | Currently documents 0,4,5,7 — add 1,6 |

Total effort: ~1 session. Every instruction is a mechanical variation of
an existing encoder function. The hardest part is the golden tests.

---

## Relationship to LanguageUpdates.md

The encoder is the bottom layer. `LanguageUpdates.md` defines the language
builtins and IR opcodes that lower to these encoder functions:

```
Language builtin     IR opcode     Emitter          Encoder function
─────────────────    ──────────    ──────────       ─────────────────
bit-and x y      →   BitAnd    →   emit BitAnd  →   and-rr (exists)
bit-or x y       →   BitOr     →   emit BitOr   →   or-rr (NEW)
bit-xor x y      →   BitXor    →   emit BitXor  →   xor-rr64 (NEW)
bit-not x        →   BitNot    →   emit BitNot  →   not-r (NEW)
bit-shl x n      →   BitShl    →   emit BitShl  →   shl-ri / shl-rcl
bit-shr x n      →   BitShr    →   emit BitShr  →   shr-ri / shr-rcl
```

`MUL r/m64` and `ROR r64, imm8` are not directly exposed as language
builtins. They are used by the emitter as optimizations:

- `MUL`: The emitter generates this for Ed25519 field multiply, where
  it knows two 51-bit limbs are being multiplied and needs the 128-bit
  result. This is a codegen pattern, not a language operation.

- `ROR`: Either generated as a peephole optimization for the
  `bit-or (bit-shr x n) (bit-shl x (w-n))` pattern, or exposed via
  a `bit-rotr` builtin if peephole is too complex initially.

- `RDTSC`: Exposed as a builtin or intrinsic for the timing test
  harness only. Not part of the general-purpose language surface.
