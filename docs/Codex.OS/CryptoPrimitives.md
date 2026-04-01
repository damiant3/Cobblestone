# Crypto Primitives — Ed25519 and SHA-256 in Codex on Bare Metal

**Date**: 2026-03-31
**Status**: Design
**Depends on**: Second Bootstrap (Phase 1+), bare-metal x86-64 backend
**Unblocks**: CDX binary verification, Trust Network, Agent Protocol, Trust Lattice identity

---

## The Problem

Four designs depend on two cryptographic operations that do not yet exist
in Codex:

| System | Needs SHA-256 | Needs Ed25519 |
|--------|--------------|---------------|
| **CDX binary** (CodexBinary.md) | Content hash (header 0x08), proof hashes | Author key (0x28), signature (0x48) |
| **Trust Network** (TrustNetwork.md) | FactHash on every message, replay dedup | Signature on every message, identity = public key |
| **Agent Protocol** (RuntimeTrust.txt) | FactHash for all 7 message types | Signature field on Propose/Grant/Deny/Explain/Narrate/Interrupt/Handoff |
| **Capability Refinement** | Proof-of-work (partial hash collision) | Trust lattice vouch signatures |

These are not optional. Without SHA-256, there is no content addressing.
Without Ed25519, there is no identity, no authentication, no trust lattice.
Every message on the Trust Network, every CDX binary header, and every
capability grant requires both primitives.

The primitives must run on bare metal (x86-64, Ring 3, no OS, no libc).
They must be implemented in Codex. They must be constant-time. There are
no external dependencies to call — this is the bottom of the stack.

---

## Design Principles

1. **Constant-time execution.** No data-dependent branches, no
   data-dependent memory access patterns. Timing side channels are
   the primary threat model for crypto on bare metal. Every comparison,
   every conditional, every table lookup must take the same number of
   cycles regardless of the key material or message content.

2. **No external dependencies.** The implementation is self-contained
   Codex code compiled by the Codex compiler to bare-metal x86-64.
   No libc, no OpenSSL, no system calls. The only runtime services
   available are the bare-metal kernel's memory allocator and serial I/O.

3. **Auditable size.** Follow TweetNaCl's philosophy: small enough to
   audit by hand. TweetNaCl implements the full NaCl API in 100 tweets
   (~770 lines of C). Our target is comparable — small enough that a
   single reviewer can read every line and verify correctness.

4. **Test against published vectors.** SHA-256 tests against NIST
   FIPS 180-4 examples. Ed25519 tests against RFC 8032 Section 7.1
   test vectors. No "it looks right" — either the output matches the
   published vector byte-for-byte, or it's wrong.

5. **Separation of concerns.** SHA-256 and Ed25519 are independent
   modules with no shared state. Ed25519 uses SHA-512 internally
   (part of the Ed25519 spec), so SHA-512 is also required. SHA-256
   and SHA-512 share the Merkle-Damgard structure and can share a
   compression function pattern.

---

## SHA-256

### What It Is

SHA-256 (FIPS 180-4) takes an arbitrary-length message and produces a
256-bit (32-byte) hash. It is the content-addressing primitive for the
entire system: CDX content hashes, FactHashes, proof hashes, dependency
hashes.

### Algorithm Outline

```
1. Pad message to a multiple of 512 bits (64 bytes):
   - Append bit '1'
   - Append zeros until length = 448 mod 512
   - Append original message length as 64-bit big-endian

2. Initialize 8 state words (H0..H7) from the FIPS constants

3. For each 512-bit block:
   a. Prepare message schedule W[0..63]:
      - W[0..15] = block words (big-endian)
      - W[16..63] = sigma1(W[t-2]) + W[t-7] + sigma0(W[t-15]) + W[t-16]
   b. Run 64 rounds of the compression function:
      - T1 = h + Sigma1(e) + Ch(e,f,g) + K[t] + W[t]
      - T2 = Sigma0(a) + Maj(a,b,c)
      - Rotate working variables, add T1 and T2
   c. Add compressed values to state

4. Output H0..H7 concatenated (32 bytes, big-endian)
```

### Constant-Time Considerations

SHA-256 is naturally constant-time for a fixed-length input: the message
schedule and compression rounds are all arithmetic (add, rotate, shift,
and/or/xor) with no data-dependent branches. The only variable is message
length (which determines how many blocks are processed), and message
length is not secret.

The operations Ch(x,y,z) = (x AND y) XOR (NOT x AND z) and
Maj(x,y,z) = (x AND y) XOR (x AND z) XOR (y AND z) must use bitwise
operations, not conditional expressions. In Codex:

```
ch x y z = (x .&. y) .^. (complement x .&. z)
maj x y z = (x .&. y) .^. (x .&. z) .^. (y .&. z)
```

No `if`, no pattern match on bit values.

### SHA-512

Ed25519 requires SHA-512 internally (RFC 8032, Section 5.1). SHA-512
uses the same Merkle-Damgard structure as SHA-256 but with:

- 1024-bit blocks (128 bytes) instead of 512-bit
- 80 rounds instead of 64
- 64-bit words instead of 32-bit
- Different constants and initial hash values

The compression function shape is identical. Both can be expressed as:

```
sha-compress : ShaParams -> State -> Block -> State
```

where `ShaParams` carries the word size, round count, constants, and
rotation amounts. This is code reuse, not premature abstraction — both
algorithms genuinely share the structure.

---

## Ed25519

### What It Is

Ed25519 (RFC 8032, using the Edwards curve Ed25519 over the prime field
GF(2^255 - 19)) provides:

- **Key generation**: Private key (32 bytes random) -> public key (32 bytes)
- **Sign**: (private key, message) -> signature (64 bytes)
- **Verify**: (public key, message, signature) -> Boolean

It is the identity and authentication primitive. An agent's identity IS
its Ed25519 public key. Every message signature, every CDX binary
signature, every trust lattice vouch is an Ed25519 signature.

### Algorithm Outline (Sign)

```
1. Hash the 32-byte private key with SHA-512 -> 64 bytes (h)
2. Clamp h[0..31]:
   - h[0] &= 248       (clear low 3 bits)
   - h[31] &= 127      (clear high bit)
   - h[31] |= 64       (set second-highest bit)
   This is the scalar 'a'

3. r = SHA-512(h[32..63] || message) mod L
   (L = group order = 2^252 + 27742317777372353535851937790883648493)
4. R = r * B  (B is the base point; this is a scalar multiplication on the curve)
5. S = (r + SHA-512(R || public_key || message) * a) mod L
6. Signature = R (32 bytes, compressed point) || S (32 bytes, little-endian scalar)
```

### Algorithm Outline (Verify)

```
1. Decode R from signature[0..31] (compressed Edwards point)
2. Decode public key A from 32 bytes (compressed Edwards point)
3. S = signature[32..63] as little-endian scalar
4. h = SHA-512(R || A || message) mod L
5. Check: 8*S*B == 8*R + 8*h*A  (cofactor-cleared comparison)
   Equivalently: [8][S]B - [8]R - [8][h]A == neutral point
```

### Building Blocks

Ed25519 requires these sub-components, listed bottom-up:

| Component | What it does | Approx. size |
|-----------|-------------|-------------|
| **Field arithmetic (GF(2^255-19))** | Add, subtract, multiply, square, invert, reduce mod p | ~200 lines |
| **Scalar arithmetic (mod L)** | Add, multiply, reduce mod group order L | ~80 lines |
| **Point operations (Extended Coordinates)** | Point add, double, scalar multiply on twisted Edwards curve | ~150 lines |
| **Point encoding/decoding** | Compress/decompress Edwards points (y-coordinate + sign bit) | ~60 lines |
| **SHA-512** | Hashing for key derivation, nonce generation, challenge | ~120 lines |
| **Ed25519 sign/verify** | Top-level operations combining the above | ~80 lines |
| **Constant-time utilities** | ct-select, ct-eq, ct-compare | ~30 lines |

**Total estimate: ~720 lines of Codex** (comparable to TweetNaCl's ~770
lines of C).

### The TweetNaCl Approach

TweetNaCl (Bernstein, van Gastel, Janssen, Lange, Schwabe, Smetsers, 2014)
implements the full NaCl API in minimal C. Its Ed25519 implementation is
the reference for our approach because:

1. **It fits in your head.** The entire curve25519/ed25519 implementation
   is ~300 lines of C. No abstraction layers, no plugin architectures.
   Straight-line arithmetic.

2. **It's verified.** TweetNaCl has been formally verified (TweetNaCl-
   Crypto, Protzenko et al.) and extensively audited.

3. **It uses the same field representation.** 16 limbs of 16-bit values
   (with carry propagation), which maps well to Codex's integer arithmetic.
   We can use the same representation or adapt to 5 limbs of 51-bit values
   (the "radix-2^51" representation used in ref10) depending on what the
   x86-64 backend handles more naturally.

**Limb representation decision**: The x86-64 backend has 64-bit registers
and efficient 64-bit multiply. The 5x51-bit representation (5 limbs, each
fitting in 51 bits of a 64-bit word, with ~13 bits of headroom for carry
accumulation) is more natural:

```
-- A field element in GF(2^255 - 19)
-- Represented as 5 limbs: f0 + f1*2^51 + f2*2^102 + f3*2^153 + f4*2^204
-- Each limb fits in 64 bits with headroom
type FieldElement = { f0 : Int64, f1 : Int64, f2 : Int64, f3 : Int64, f4 : Int64 }
```

This gives us:
- Field multiply: 25 multiply-and-accumulate operations (5x5 schoolbook)
- Field square: 15 operations (exploiting symmetry)
- Reduction: carry propagation with multiply by 19 for overflow from f4

### Constant-Time Discipline

Ed25519's security depends entirely on constant-time execution. The
threat: an attacker measures how long sign or verify takes and extracts
the private key from timing variations. On bare metal with no OS noise,
timing signals are *cleaner* than on hosted systems — making this MORE
critical, not less.

**Rules:**

1. **No branching on secret data.** The scalar multiplication
   `[s]P` must not branch on the bits of `s`. Use the Montgomery ladder
   or a constant-time fixed-window method. Every iteration executes
   the same instructions regardless of the current bit.

   ```
   -- WRONG: branches on secret bit
   if bit-set s i then point-add acc base else acc

   -- RIGHT: constant-time conditional swap
   ct-swap (bit-set s i) acc temp
   point-add ...
   ct-swap (bit-set s i) acc temp
   ```

2. **No data-dependent memory access.** Table lookups indexed by secret
   values must scan the entire table and select with a constant-time mask.

   ```
   -- WRONG: direct index (cache timing leak)
   table.[secret-index]

   -- RIGHT: scan all entries, mask-select
   ct-table-lookup table secret-index =
     fold (\ acc (i, entry) -> ct-select (ct-eq i secret-index) entry acc)
          zero-point
          (enumerate table)
   ```

3. **No early exit.** Comparison functions must always examine all bytes.
   `ct-eq a b` computes `(a XOR b) OR ...` across all bytes and checks
   the accumulator, never short-circuiting.

4. **No variable-time arithmetic.** Division and modular inversion use
   Fermat's little theorem (exponentiation by p-2) via a fixed addition
   chain, not the extended Euclidean algorithm (which is variable-time).

   ```
   -- Inversion in GF(2^255 - 19): a^(p-2) mod p
   -- Fixed sequence of 255 squarings and ~12 multiplications
   -- Identical operation count for all inputs
   field-invert : FieldElement -> FieldElement
   ```

5. **Compiler cooperation.** The Codex compiler must not optimize away
   constant-time patterns. Specifically:
   - Do not eliminate "dead" branches in ct-select (both sides must execute)
   - Do not reorder loads based on branch prediction hints
   - Do not strength-reduce multiplications in ways that create data-dependent timing

   This is a constraint on the x86-64 backend. Today the backend does minimal
   optimization, which is actually a feature here — less optimization means
   fewer opportunities to break constant-time discipline. As the optimizer
   matures, it needs a `[ConstantTime]` annotation or effect that suppresses
   timing-sensitive optimizations.

---

## Testing Strategy

### SHA-256 Test Vectors

From NIST FIPS 180-4, Section B (the canonical source):

| Test | Input | Expected SHA-256 |
|------|-------|-----------------|
| Empty string | `""` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| "abc" | `"abc"` | `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad` |
| 448-bit | `"abcdbcde..."` (56 bytes) | `248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1` |
| Million 'a' | `"aaa..."` (1,000,000 bytes) | `cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0` |

### SHA-512 Test Vectors

From FIPS 180-4:

| Test | Input | Expected SHA-512 (first 32 bytes shown) |
|------|-------|-----------------------------------------|
| Empty string | `""` | `cf83e1357eefb8bd...` |
| "abc" | `"abc"` | `ddaf35a193617aba...` |

### Ed25519 Test Vectors

From RFC 8032, Section 7.1:

**Test 1 — zero key:**
```
Private key: 9d61b19deffd5a60ba844af492ec2cc4
              4449c5697b326919703bac031cae7f60
Public key:  d75a980182b10ab7d54bfed3c964073a
              0ee172f3daa3f4a18446b0b8d183f8e3
Message:     (empty)
Signature:   e5564300c360ac729086e2cc806e828a
              84877f1eb8e5d974d873e06522490155
              5fb8821590a33bacc61e39701cf9b46b
              d25bf5f0595bbe24655141438e7a100b
```

**Test 2:**
```
Private key: 4ccd089b28ff96da9db6c346ec114e0f
              5b8a319f35aba624da8cf6ed4fb8a6fb
Public key:  3d4017c3e843895a92b70aa74d1b7ebc
              9c982ccf2ec4968cc0cd55f12af4660c
Message:     72
Signature:   92a009a9f0d4cab8720e820b5f642540
              a2b27b5416503f8fb3762223ebdb69da
              085ac1e43e159c7e94b2ba7f6c0c3548
              31b3a2d9b34ff2c45c6e09cc3b67c6a4
              ...
```

All 5 RFC 8032 Section 7.1 vectors must pass. Additionally, test with
the "1023-byte message" vector and the "SHA(abc)" vector from the same
section.

### Structural Tests

Beyond vector tests:

1. **Round-trip**: Generate keypair, sign message, verify — must succeed.
2. **Wrong key**: Sign with key A, verify with key B — must fail.
3. **Tampered message**: Sign message M, verify with M' (one bit flipped) — must fail.
4. **Tampered signature**: Flip one bit in signature, verify — must fail.
5. **SHA-256 incremental**: Hash in one call vs. multiple update calls — must match.
6. **SHA-256 block boundary**: Messages of length 55, 56, 63, 64, 119, 120, 127, 128 bytes (padding edge cases).
7. **Field arithmetic**: Verify that multiply and reduce produce correct results for edge cases (0, 1, p-1, p, 2p-1).
8. **Scalar arithmetic**: Verify reduction mod L for values near L, 2L, and 2^256.

### Timing Tests

On bare metal, measure cycle count (via `RDTSC`) for:
- SHA-256 of 64 bytes with all-zero input vs. all-one input
- Ed25519 sign with different private keys
- Ed25519 verify with different public keys

The cycle counts must be identical (within measurement noise, typically
< 0.1% variance for fixed-length inputs). Any systematic difference
indicates a timing leak.

```
-- Bare-metal timing harness (pseudocode)
test-constant-time op input1 input2 =
  let cycles1 = rdtsc () in
  let _ = op input1 in
  let cycles2 = rdtsc () in
  let _ = op input2 in
  let cycles3 = rdtsc () in
  assert (abs ((cycles2 - cycles1) - (cycles3 - cycles2)) < threshold)
```

---

## Implementation Plan

### Phase A: SHA-256 and SHA-512

**Effort**: Small (1-2 sessions)
**Depends on**: Bare-metal integer arithmetic (already working)
**Files**: `Codex.Codex/sha256.codex`, `Codex.Codex/sha512.codex`

1. Implement SHA-256 compression function (the inner loop)
2. Implement padding and message schedule
3. Implement streaming interface (init / update / finalize)
4. Verify against FIPS 180-4 vectors
5. Implement SHA-512 (same structure, different constants and word size)
6. Verify against FIPS 180-4 vectors

SHA-256 and SHA-512 are pure functions with no effects — they are ideal
early targets for bare-metal Codex because they exercise integer arithmetic,
arrays, and loops, nothing else.

### Phase B: Field and Scalar Arithmetic

**Effort**: Medium (2-3 sessions)
**Depends on**: Phase A (SHA-512 needed for Ed25519 internals)
**Files**: `Codex.Codex/field25519.codex`, `Codex.Codex/scalar25519.codex`

1. Implement GF(2^255-19) field element type (5x51-bit representation)
2. Implement field add, subtract, multiply, square
3. Implement carry propagation and reduction
4. Implement field inversion via Fermat (a^(p-2))
5. Implement field square root (for point decompression)
6. Implement scalar mod L (Barrett reduction)
7. Test: field multiply associativity, commutativity, identity, inverse
8. Test: known field element products from reference implementation

### Phase C: Curve Operations

**Effort**: Medium (2-3 sessions)
**Depends on**: Phase B
**Files**: `Codex.Codex/ed25519.codex`

1. Define Extended Twisted Edwards coordinates: (X, Y, Z, T) where
   x = X/Z, y = Y/Z, T = X*Y/Z
2. Implement point addition (unified formula, no special cases)
3. Implement point doubling
4. Implement constant-time scalar multiplication (double-and-add with
   ct-swap, or fixed-window with ct-table-lookup)
5. Implement point encoding (compress to 32 bytes: y-coordinate + x sign bit)
6. Implement point decoding (decompress: recover x from y via sqrt)
7. Test: base point * L == neutral point
8. Test: base point * 1 == base point
9. Test: known multiples of the base point

### Phase D: Ed25519 Sign and Verify

**Effort**: Small (1 session)
**Depends on**: Phase C + Phase A (SHA-512)
**Files**: `Codex.Codex/ed25519.codex` (continued)

1. Implement key generation (clamp, scalar multiply base point)
2. Implement sign (RFC 8032 Section 5.1.6)
3. Implement verify (RFC 8032 Section 5.1.7)
4. Pass all RFC 8032 Section 7.1 test vectors
5. Run structural tests (round-trip, wrong key, tampered message/signature)

### Phase E: Integration and Timing Verification

**Effort**: Small (1 session)
**Depends on**: Phase D
**Files**: Test harness additions

1. Wire SHA-256 into a bare-metal test that hashes a known string and
   emits the result over serial
2. Wire Ed25519 into a bare-metal test that signs and verifies over serial
3. Run timing tests (RDTSC-based) to verify constant-time properties
4. Document cycle counts for SHA-256 (per block) and Ed25519 (sign, verify)
   as baseline performance numbers

---

## Performance Expectations

Based on TweetNaCl and ref10 benchmarks on x86-64, scaled for Codex's
current backend (no SIMD, no inline assembly, straightforward register
allocation):

| Operation | TweetNaCl (C, -O2) | Expected (Codex, bare metal) | Notes |
|-----------|--------------------|------------------------------|-------|
| SHA-256 (64B) | ~1,000 cycles | ~3,000-5,000 cycles | No SIMD; 64 rounds of integer arithmetic |
| SHA-512 (128B) | ~1,500 cycles | ~4,000-7,000 cycles | 80 rounds, 64-bit words |
| Ed25519 sign | ~90,000 cycles | ~300,000-500,000 cycles | Dominated by scalar multiply (~255 point doubles + ~128 point adds) |
| Ed25519 verify | ~250,000 cycles | ~800,000-1,200,000 cycles | Two scalar multiplies (double-base) |

At 1 GHz, Ed25519 verify at 1.2M cycles = 1.2ms. At 3 GHz = 0.4ms. For
the Trust Network's per-message signature verification, this is well within
the target: even at 1 GHz, an agent can verify ~800 messages per second.

These are conservative estimates. The Codex backend currently does not
optimize aggressively, which is acceptable — correctness and constant-time
behavior are more important than speed. As the backend matures (register
allocation improvements, instruction selection), performance will improve
without changing the Codex source.

---

## API Surface

The public API is deliberately minimal:

```
-- SHA-256
sha256 : Bytes -> Bytes                           -- one-shot: message -> 32-byte hash
sha256-init : Sha256State                         -- streaming: initialize
sha256-update : Sha256State -> Bytes -> Sha256State  -- streaming: feed data
sha256-finalize : Sha256State -> Bytes            -- streaming: produce hash

-- SHA-512 (same shape)
sha512 : Bytes -> Bytes
sha512-init : Sha512State
sha512-update : Sha512State -> Bytes -> Sha512State
sha512-finalize : Sha512State -> Bytes

-- Ed25519
ed25519-keypair : Bytes -> (Bytes, Bytes)         -- 32-byte seed -> (public key, secret key)
ed25519-sign : Bytes -> Bytes -> Bytes -> Bytes   -- secret key -> public key -> message -> 64-byte signature
ed25519-verify : Bytes -> Bytes -> Bytes -> Bool  -- public key -> message -> signature -> valid?
```

No key formats, no ASN.1, no PEM, no X.509. Raw 32-byte keys and 64-byte
signatures. The CDX binary header (CodexBinary.md) stores them at fixed
offsets — no parsing needed.

The streaming SHA interface exists because CDX content hashing must hash
everything from `capabilities_offset` to EOF, which may be large. The
streaming interface avoids buffering the entire binary in memory.

---

## What This Does NOT Cover

- **Key storage and protection.** Where private keys live (secure enclave,
  encrypted file, memory-only) is a separate design. This document covers
  the math, not the key management.

- **Key rotation and revocation.** How to replace a compromised key in the
  trust lattice is a trust network protocol problem, not a crypto primitive
  problem.

- **Encryption (X25519/ChaCha20-Poly1305).** The Trust Network design notes
  that encryption is optional (integrity is guaranteed by signatures;
  confidentiality is a separate concern). If needed later, X25519 key
  exchange shares the same field arithmetic and can be added with ~100
  additional lines.

- **Random number generation.** Ed25519 key generation needs 32 bytes of
  entropy. On bare metal, the entropy source (RDRAND, timer jitter, serial
  input timing) is a separate design. The crypto primitives take a seed;
  they don't generate one.

- **Certificate or attestation formats.** How to bundle a public key with
  metadata (agent name, capabilities, expiry) is a trust lattice design
  issue. The primitives just sign and verify bytes.

---

## Connection to Existing Systems

| System | How it uses crypto primitives |
|--------|------------------------------|
| **CDX binary** | `sha256` for content_hash; `ed25519-sign` at compile time; `ed25519-verify` at load time |
| **Trust Network** | `sha256` for FactHash; `ed25519-verify` on every inbound message; `ed25519-sign` on every outbound message |
| **Agent Protocol** | Every message type's `signature` field is `ed25519-sign(sender_key, sha256(message_body))` |
| **Trust Lattice** | Identity = ed25519 public key; vouches are signed facts |
| **Proof-of-work** | `sha256` iterated with nonce until partial collision (difficulty-adjusted) |
| **Fact store** | `sha256` for content addressing; deduplication by hash |
| **Second Bootstrap** | Crypto modules are new .codex files in `Codex.Codex/`, compiled by the self-hosted compiler |

---

## Open Questions

1. **Limb representation.** 5x51-bit (natural for 64-bit multiply) vs.
   TweetNaCl's 16x16-bit (simpler carry propagation, more portable)?
   Recommendation: 5x51 for x86-64 bare metal — we have 64-bit registers
   and that's the only target. If Codex targets other architectures later,
   the 16x16 representation can be a separate backend.

2. **Batch verification.** Ed25519 supports batch verification (verify
   N signatures faster than N individual verifications via a random linear
   combination). This matters for the CDX loader verifying multiple proof
   hashes. Defer to a follow-up — single verification first.

3. **`[ConstantTime]` effect or annotation.** Should the compiler track
   constant-time requirements in the type system? This would let the type
   checker reject `if secret-bit then ... else ...` patterns in crypto
   code. Appealing but complex — defer until the optimizer is sophisticated
   enough to actually break constant-time patterns.

4. **Wycheproof vectors.** Google's Project Wycheproof provides thousands
   of edge-case test vectors for Ed25519 (small-order points, non-canonical
   encodings, etc.). Should we test against the full Wycheproof suite?
   Recommendation: yes, after RFC 8032 vectors pass. Wycheproof catches
   implementation bugs that spec vectors don't.

5. **128-bit multiply.** Field multiplication in 5x51 representation
   produces intermediate products that exceed 64 bits (51+51 = 102 bits).
   The x86-64 `MUL` instruction produces a 128-bit result in RDX:RAX.
   Does the Codex backend expose this? If not, we need to decompose into
   32-bit half-multiplies, which is slower. This should be resolved during
   Second Bootstrap Phase 1 (instruction encoder) — ensure `MUL r/m64` is
   in the encoder's instruction set.

---

## Implementation Order Summary

```
Phase A: SHA-256 + SHA-512                    [pure functions, FIPS vectors]
Phase B: Field GF(2^255-19) + Scalar mod L    [arithmetic, property tests]
Phase C: Curve point operations               [ct-scalar-mul, encode/decode]
Phase D: Ed25519 sign + verify                [RFC 8032 vectors]
Phase E: Integration + timing verification    [bare-metal harness, RDTSC]
```

Phases A through D are pure computation — no effects, no I/O, no OS
interaction. They can be tested in the C# hosted environment (via the
existing test infrastructure) before running on bare metal. Phase E is
the bare-metal integration test.

Total estimated size: ~720 lines of Codex across 4-5 source files.
Total estimated effort: 7-10 sessions, mostly arithmetic and testing.

This unblocks: CDX binary signing and verification, Trust Network
message authentication, Agent Protocol signature fields, trust lattice
identity, and proof-of-work for first contact.
