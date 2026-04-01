# Language Updates — Bitwise Operations for Crypto Primitives

**Date**: 2026-03-31
**Status**: Design
**Depends on**: MM4 (self-compilation fixed point)
**Unblocks**: CryptoPrimitives.md (SHA-256, Ed25519)
**Prior art**: `docs/Codex.OS/CryptoPrimitives.md`, `Codex.Codex/Emit/X86_64Encoder.codex`

---

## The Problem

Codex has no bitwise operations. The language, IR, and all backends
support only boolean `and` / `or` (truth values), not integer-level
bit manipulation.

The self-hosted encoder (`X86_64Encoder.codex`) documents this explicitly
(lines 8-13):

```
Codex has no bitwise operators. All bit manipulation uses arithmetic:
  reg & 7       ->  int-mod reg 8
  mod << 6      ->  m * 64
  (reg & 7) << 3 -> (int-mod reg 8) * 8
  a | b (disjoint) -> a + b
  value >> 8    ->  floor-div value 256
```

These arithmetic workarounds are sufficient for the encoder because it
only manipulates known-disjoint bit fields (register indices, opcode
fields). They do NOT generalize to crypto:

| Operation | Arithmetic workaround | Works for crypto? |
|-----------|-----------------------|-------------------|
| `a AND b` | `int-mod a (b+1)` | Only if b is a mask (2^n - 1). Not general. |
| `a OR b`  | `a + b` | Only if bits are disjoint. SHA-256 OR is not disjoint. |
| `a XOR b` | None | No arithmetic equivalent for arbitrary values. |
| `NOT a`   | None | Bitwise complement has no arithmetic equivalent. |
| `a >> n`  | `floor-div a (2^n)` | Wrong for negative values. Crypto needs unsigned shift. |
| `a <<< n` (rotate) | None | No arithmetic equivalent. |

SHA-256 requires AND, XOR, NOT, and right-rotate on 32-bit words for
its core operations:

```
Ch(x,y,z)  = (x AND y) XOR (NOT x AND z)        -- 3 bitwise ops
Maj(x,y,z) = (x AND y) XOR (x AND z) XOR (y AND z)  -- 5 bitwise ops
Sigma0(x)  = ROTR(x,2) XOR ROTR(x,13) XOR ROTR(x,22)  -- 3 rotates + 2 XOR
Sigma1(x)  = ROTR(x,6) XOR ROTR(x,11) XOR ROTR(x,25)  -- 3 rotates + 2 XOR
```

That's ~10 bitwise operations per round x 64 rounds = ~640 bitwise ops
per SHA-256 hash. XOR on arbitrary values is irreducible — there is no
arithmetic shortcut. This is a hard blocker.

---

## What Exists Today

| Layer | Bitwise support | Notes |
|-------|----------------|-------|
| **Lexer/Parser** | None | No tokens for `.&.`, `.^.`, etc. |
| **AST** | None | `BinaryOp` enum has no bitwise variants |
| **IR** | None | `IRBinaryOp` has `And`/`Or` (boolean only) |
| **Type checker** | None | Nothing to check |
| **C# x86-64 emitter** | Emits AND/XOR/SHL/SHR machine instructions | Used internally for alignment, register zeroing. Not exposed to the language. |
| **Self-hosted encoder** | Encodes AND/XOR/SHL/SHR opcodes | Same — used internally, not reachable from Codex source. |
| **Other backends** (C#, JS, Python, etc.) | N/A | Irrelevant post-MM4 |

The machine instructions exist in the encoder. The pipeline above them
does not connect.

---

## Design: Builtins, Not Operators

### Why builtins

Post-MM4, the compiler is self-hosted Codex. Adding new syntax (operators
like `.&.`) requires modifying the lexer, parser, and precedence tables —
all of which are .codex source at that point. This is high-risk for the
first post-MM4 change.

Builtins require:
1. New entries in the name resolver (builtin function table)
2. New IR opcodes (6 new values in the `IRBinaryOp` equivalent)
3. New cases in the x86-64 emitter (mapping IR opcodes to the machine
   instructions that already exist in the encoder)

No parser changes. No precedence changes. No new syntax. The risk
surface is small and testable.

### The six builtins

```
bit-and   : Integer -> Integer -> Integer    -- bitwise AND
bit-or    : Integer -> Integer -> Integer    -- bitwise OR
bit-xor   : Integer -> Integer -> Integer    -- bitwise XOR
bit-not   : Integer -> Integer               -- bitwise complement (one's complement)
bit-shl   : Integer -> Integer -> Integer    -- logical shift left by n bits
bit-shr   : Integer -> Integer -> Integer    -- logical shift right by n bits (unsigned)
```

All operate on the full 64-bit `Integer` type. Codex has one integer
type (64-bit signed). For crypto, we treat these as unsigned 64-bit
values — the bitwise operations are the same regardless of signedness.
The caller is responsible for masking to the relevant width (e.g.,
`bit-and x 0xFFFFFFFF` for 32-bit SHA-256 words).

### Why not `bit-rotr`?

Right-rotate could be a seventh builtin:

```
bit-rotr : Integer -> Integer -> Integer -> Integer
           -- bit-rotr width value n = rotate value right by n within width bits
```

However, rotate is composable from the other six:

```
rotr32 x n = bit-or (bit-shr (bit-and x 0xFFFFFFFF) n)
                     (bit-shl (bit-and x 0xFFFFFFFF) (32 - n))
```

Three instructions instead of one. The encoder DOES need a `ROR`
instruction for performance (see `EncoderUpdates.md`), but the language
doesn't need a rotate builtin — the emitter can pattern-match
`bit-or (bit-shr x n) (bit-shl x (w-n))` and emit `ROR` as a
peephole optimization. This keeps the language surface minimal and
pushes the optimization to where it belongs (the backend).

Alternatively, if peephole pattern matching is too complex for the
initial post-MM4 emitter, `bit-rotr` can be added as a seventh builtin
later. The three-instruction version is correct; it's just slower.

---

## IR Changes

Add six new opcodes to the IR binary/unary operation set:

```
-- New IR binary operations
BitAnd   : Integer x Integer -> Integer
BitOr    : Integer x Integer -> Integer
BitXor   : Integer x Integer -> Integer
BitShl   : Integer x Integer -> Integer
BitShr   : Integer x Integer -> Integer

-- New IR unary operation
BitNot   : Integer -> Integer
```

These lower directly to x86-64 instructions:

| IR opcode | x86-64 instruction | Encoder function | Status |
|-----------|-------------------|-----------------|--------|
| `BitAnd` | `AND r64, r64` | `and-rr` | Exists in encoder |
| `BitOr` | `OR r64, r64` | `or-rr` | **Missing — see EncoderUpdates.md** |
| `BitXor` | `XOR r64, r64` | `xor-rr` | Exists in encoder (currently 32-bit, needs 64-bit variant) |
| `BitShl` | `SHL r64, CL` | `shl-ri` exists, `shl-rcl` **missing** |  Immediate form exists; register form needed for variable shifts |
| `BitShr` | `SHR r64, CL` | `shr-ri` exists, `shr-rcl` **missing** | Same |
| `BitNot` | `NOT r64` | `not-r` | **Missing — see EncoderUpdates.md** |

Note: SHA-256 uses only constant shift/rotate amounts (the sigma
functions have fixed rotation counts), so the immediate forms (`shl-ri`,
`shr-ri`) are sufficient for SHA-256. Ed25519 field arithmetic does not
use shifts. Variable shifts (`SHL r64, CL`) are needed only if a general
`bit-shl` builtin is provided — which we do for completeness, but crypto
code won't exercise the variable path.

---

## Emitter Changes

The x86-64 emitter (the codegen layer above the encoder) needs new
cases for each IR opcode. This is mechanical: load operands into
registers, emit the instruction, store the result. The pattern is
identical to the existing `AddInt`/`SubInt`/`MulInt` cases.

```
-- Pseudocode for the emitter case
emit (BitAnd rd rs) =
  load rd into register-a
  load rs into register-b
  and-rr register-a register-b
  store register-a into rd

emit (BitNot rd) =
  load rd into register-a
  not-r register-a
  store register-a into rd
```

### XOR special case

The encoder's current `xor-rr` emits a 32-bit XOR (opcode `0x31`,
no REX.W prefix). This is used for register zeroing (`xor eax, eax`).
For crypto, we need a 64-bit XOR (REX.W prefix = `0x48`). The encoder
needs a `xor-rr64` variant or the existing `xor-rr` needs to emit
the REX.W prefix. See `EncoderUpdates.md` for details.

---

## Type Checking

Bitwise builtins operate on `Integer -> Integer -> Integer` (binary) or
`Integer -> Integer` (unary). The type checker already handles builtins
with these signatures — no new type logic needed. The builtins are pure
(no effects).

One consideration: should bitwise operations on `Number` (floating-point)
be an error? Yes. The builtins are typed `Integer -> ...`, so applying
them to `Number` is already a type error. No special handling needed.

---

## Testing

1. **Unit tests for each builtin**: `bit-and 0xFF 0x0F` = `0x0F`, etc.
   Test identity laws: `bit-and x x = x`, `bit-or x 0 = x`,
   `bit-xor x x = 0`, `bit-not (bit-not x) = x`.

2. **Shift edge cases**: `bit-shl 1 63` = `0x8000000000000000` (MSB set),
   `bit-shr 0x8000000000000000 63` = `1` (unsigned shift, not arithmetic).

3. **SHA-256 Ch and Maj**: Implement `Ch` and `Maj` using builtins, test
   against known values from a reference implementation.

4. **Round-trip with encoder**: The encoder currently uses arithmetic
   workarounds for bit manipulation. After builtins are available,
   rewrite the encoder's bit manipulation to use them. The encoder's
   output (byte sequences) must be identical before and after — this
   is a golden-file regression test.

---

## Implementation Order

| Step | What | Effort | Risk |
|------|------|--------|------|
| 1 | Add 6 IR opcodes to the self-hosted compiler | Small | Low — additive |
| 2 | Add 6 builtins to the name resolver | Small | Low — additive |
| 3 | Add encoder instructions (OR, NOT, XOR64, SHL/SHR CL — see EncoderUpdates.md) | Small | Low — additive |
| 4 | Add emitter cases for 6 IR opcodes | Small | Low — mechanical |
| 5 | Test builtins with unit tests | Small | Low |
| 6 | Verify pingpong (self-compilation fixed point holds) | Required | Medium — any bug here is a showstopper |

Total effort: ~1-2 sessions. All changes are additive (new opcodes,
new builtins, new encoder functions). Nothing existing is modified
except to add new cases to existing dispatch tables.

**Step 6 is critical.** After adding bitwise builtins, the self-hosted
compiler must still produce a fixed-point. The builtins don't change
existing codegen — they only add new paths. But the pingpong test must
pass to confirm nothing was broken.

---

## Syntax Sugar (Deferred)

If operator syntax is desired later:

```
-- Possible operators (Haskell-style dotted to avoid ambiguity)
x .&. y   = bit-and x y
x .|. y   = bit-or x y
x .^. y   = bit-xor x y
~x        = bit-not x
x .<<. n  = bit-shl x n
x .>>. n  = bit-shr x n
```

This requires lexer + parser changes and is separate from the crypto
prerequisite. The builtins are sufficient for crypto. Operators are
ergonomic sugar for later.
