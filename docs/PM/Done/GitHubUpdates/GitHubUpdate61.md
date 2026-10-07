# GitHub Update 61

**Release of 2026-09-17.**

Update 61 combines Steve Howell's app and fixture corrections, explicit deck
telemetry, the unconstrained-type repair, and nine crypto primitive repair sets.
The full gate, normal and poison batteries, IR fidelity and independent
C# compiler witness passed. The proof details and remaining app limits follow.

## Steve Howell's app and fixture corrections

Steve Howell contributed PRs [148](https://github.com/damiant3/Cobblestone/pull/148),
[149](https://github.com/damiant3/Cobblestone/pull/149),
[150](https://github.com/damiant3/Cobblestone/pull/150),
[151](https://github.com/damiant3/Cobblestone/pull/151), and
[152](https://github.com/damiant3/Cobblestone/pull/152), developed with Claude.
The fixes and the initial measurements are his contribution. Main CL 25746
integrates the five PRs; our additions are source-prose cleanup and local
acceptance checks.

- Both FAT copies agree in all eleven FAT16 disk fixtures. The cycle fixture
  retains the intended `2 -> 3 -> 2` cycle in both copies. The landed images
  match the PR's Git blobs exactly.
- Raytracer shading uses Real geometry with unit normals and material weights
  in thousandths. Camera field of view uses thousandths of image height at a
  forward distance of one. The nearest-object scan constructs the full hit
  only for the winner, preserving first-object ties and the distance cutoff.
- GuiOpening selects the navigation button under the mouse using widget bounds.
  Replacing an AppRunner root lays out the replacement before rendering.
- The bare-metal Fireworks app wraps both hash multiplications explicitly.

## Validation on 2026-09-17

One combined serial proof passed 21 subjects under Update60's depot seed:
19 compile-and-run subjects matched expected output exactly, and the complete
GuiOpening and Fireworks apps compiled. The runtime subjects comprise eleven
FAT tests, five tests selected by the cite graph, and three focused checks.
The focused checks cover eight ray-selection boundaries, navigation and child
bounds across root replacements, and six RNG values checked against independent
modulo-2^64 arithmetic.

The compiler source bundle is byte-identical before and after the changes:
`1ADF15FF8042D9B0430829761CEFF6D360F6B500B14CDC846CCB467B5C822C58`.
AppRunner and Raytracer are outside that bundle. The seed is unchanged.
The ray scan remains linear in object count with fewer full-hit constructions;
GUI navigation adds a tree hit test and root replacement adds one layout pass.
RNG time and space remain constant. Compiler heap/time behavior is unchanged.

The local proof log is `build/output/pr-intake-20260917/proof.log` in the root
workspace. No full release battery, graphical app run, or performance benchmark
was run for this intake. The combined release proof is recorded below.

## Explicit deck telemetry

Steve Howell's [PR 147](https://github.com/damiant3/Cobblestone/pull/147)
moves Zig deck reports from stdout to file descriptor 3. Main CL 25763 adds
our explicit opt-in: `CODEX_DECK_REPORT=1 ./program 3>deck.log` on Linux.
An unrelated inherited descriptor receives no telemetry by default.

The candidate plug and its emitted telemetry and arithmetic programs built
under the unchanged depot seed. Linux checks passed for absent, `0`, and `01`
settings with descriptor 3 open, enabled reporting with descriptor 3 closed,
and enabled reporting with descriptor 3 open. Only the final case reported.
Every case checked exit status and both standard output channels. Arithmetic
output also matched on Linux and Windows; the Windows telemetry program
remained silent with opt-in enabled. The prelude surface check passed over
both emitted programs. An independent reader checked the probe and assertions.

Opt-in handling allocates no heap, retains constant environment/fd state,
and scans the environment once at first deck arming. The runtime proof is
`build/output/pr147-optin/runtime-proof.log` in the root workspace.

## Unconstrained empty values in typed IR

Main CL 25770 fixes COMPILER-81 and Steve Howell's
[issue 146](https://github.com/damiant3/Cobblestone/issues/146). An empty list
or a nullary constructor can leave a use-site type argument unconstrained.
Final IR resolution now closes those internal arguments to ordinary Integer,
retaining variables carried by the definition's signature or an explicit
type binder. A generic helper remains generic; a concrete Text constraint
remains Text. The Zig plug still refuses unbound variables, and the checker
still rejects bodies that choose a concrete type for a declared generic.

Both reported failures reproduced under Update 60's seed and passed through
the new candidate and the unchanged Zig plug. Regression fixtures cover
Integer/Text uses, return-only polymorphism, nullary and payload constructors,
nested binder scope, and high 32-bit type-variable ids.
An independent reader checked the contract and the regression controls.

The seed proof passed exact whole-file equality of unsigned stages 2 and 3,
79 BVT compile checks, 64 BVT runtime checks, the two-member batch check,
the CDX2087 negative control, and the unchanged eight-warning self-compile
baseline. Signing and candidate self-verification passed. Installation
independently reconfirmed the fixed point and verified the installed seed.
Seed revision 797 and its matching map landed with the repair; the crypto
changes below subsequently advanced the seed. TechnicalDetails owns its digest.

The scope set uses fixed 32-bit radix paths, avoiding a scan through generic
parameters at every type occurrence. In one self-compile pair, the measured
R10 high-water offset from heap base was 827,013,930 bytes in both arms; elapsed time
was 10.56 seconds before and 11.48 seconds after. A single pair does not
establish a stable timing delta. Proof artifacts are under
`build/output/compiler81` in the root workspace.

## Crypto primitive repairs

Red's scoped primitive audit on 2026-09-17 landed nine repair sets.
The results below are bounded development proofs. Full release proof
remains separate, and the audit makes no whole-library constant-time claim.

- **Ed25519, main25777:** reject malformed key/signature lengths, non-octet
  values, scalars outside the group order and invalid point encodings. The
  old verifier accepted a valid signature with the group order added to S,
  and a 65-byte signature faulted with EXC06. Three RFC 8032 signatures and
  all 151 Wycheproof cases pass, together with focused curve/handshake checks.
  The signed seed has an exact unsigned stage 2=stage 3 fixed point, BVT 79
  compile and 64 runtime passes with 0 failures plus the batch control, and
  successful candidate and installed self-verification. TechnicalDetails
  records the current seed's digests. Valid verification added 8,336 retained bytes in
  the focused probe, with fixed-size work and no new message-length scan.
- **HMAC/HKDF, main25781:** preserve caller key bytes instead of padding a
  retained 20-byte key to 64 bytes in place; reject HKDF lengths outside 0..8160
  and PRKs shorter than 32 bytes. Output assembly now fills one reserved
  array instead of repeatedly copying the growing result. RFC vectors,
  maximum-length output checked by a calibrated independent oracle, and
  focused TLS/DTLS controls pass. Output assembly is linear; short-key
  copying is bounded by 63 bytes.
- **AES/GCM/CMAC, main25783:** reject malformed dimensions, partial CBC
  blocks and invalid PKCS7 padding; padding preserves the caller's bytes.
  AES substitution uses fixed-operation field arithmetic; GHASH and CMAC
  use masked selection. GCM reclaims per-block scratch and fills reserved
  output. All 256 forward/inverse S-box values, published vectors, AES-256 CBC
  bytes, and 130 independently generated GCM encryption/decryption/tamper
  cases pass. Focused TLS, DTLS and LoRaWAN consumers pass. For a 4,096-byte
  AES-128-GCM plaintext with empty AAD, post-call retained heap fell from
  99,192,856 to 84,528 bytes. That is not a high-water measurement or a CPU
  speedup claim. Native inspection covered eight arithmetic/subkey helpers;
  no whole-library or every-target constant-time claim follows.
- **RSA, main25790:** reject signature representatives at or above the
  modulus and malformed basic public-key ranges before modular arithmetic.
  A same-width PSS signature S+N and exponent 1 encoded-message forgery both
  verified before the repair and now fail. Independent .NET verification
  confirms the PSS control. Four exact-output RSA/PSS/TLS/certificate-chain
  fixtures pass. Validation adds linear public-input scans and constant
  auxiliary heap; key factorization and exponent coprimality are not proved.

- **PBKDF2, main25794:** replace the custom XOR-based password KDF with
  PBKDF2-HMAC-SHA256. The old function accepted password [1] for [0] at
  positive block counts 2 and 8, and invalid costs could produce an empty
  or password-independent tag. Invalid parameters and octets now refuse;
  the vault propagates refusal instead of hashing an empty derived key.
  Damian confirmed that no real stored data needs the old algorithm.
  Sixteen full-output .NET vectors and three focused fixtures pass,
  including actual 100000-iteration vault derivation. A 32-byte result
  retains 296 bytes at 1, 4096 and 100000 iterations; peak heap is unmeasured.
  Retained heap is O(output length); time is linear in iterations for fixed
  input/output lengths. The seed and existing signing material are unchanged.

- **ChaCha20/Poly1305, main25798:** reject malformed raw keys, nonces,
  counters and byte values; reject encryption crossing the 32-bit counter
  limit. AEAD propagates refusal and Poly1305 rejects empty tags. The final
  counter accepts 64 bytes and refuses 65. Four focused RFC/guard fixtures
  and 16 independent .NET AEAD cases pass, including foreign ciphertext
  decryption and modified-tag rejection. ChaCha20 output assembly is linear
  and reclaims per-block scratch. A 4096-byte probe retained 1520048 bytes
  before and 33200 after; neither figure measures peak heap. AEAD limits
  requiring hundreds of gigabytes were inspected, not executed.

- **X25519/TLS/DTLS, main25802:** preserve caller scalars, reject malformed
  widths/octet values, and reject all-zero shared secrets. Raw scalar
  multiplication retains RFC 7748 behavior. Both TLS/DTLS roles abort before
  deriving traffic secrets, emit fatal illegal_parameter alerts, and refuse
  later hello, receive and application-send operations. Seven focused
  fixtures and all 518 Wycheproof cases pass, including 31 zero-secret cases,
  noncanonical encodings, twist points and input preservation. The final
  fixture retries failed endpoints with encoded valid hellos. Added scans
  and one 32-element copy have constant cost at fixed X25519 widths. The
  allocation-heavy ladder is unchanged. Clearing state references does not
  establish secure heap erasure; no high-water or whole-target timing claim
  follows from these tests.

- **Ed25519 signing/callers, main25806:** reject invalid seed/public-key
  widths and non-octet inputs, and require the public key to match the seed.
  The old signer emitted signatures for mismatched keys with the same R;
  malformed messages could also sign and verify. KeyManager now converts
  SHA-256 words to 32 seed bytes, and both repository signing/verifying
  callers use digest bytes. Identity and signed-CDX wrappers propagate
  refusal. Eight focused fixtures pass. Seed799 has exact unsigned stages
  2=3, BVT79 compile/64 runtime/zero failures plus batch, candidate caller
  and TLS/DTLS passes, 151 Ed25519 Wycheproof passes, and candidate/installed
  self-verification. Promotion preserved the existing signing key. The
  installed signed seed differs from the independently hashed parent.
  Empty-message signing retains 4193720 bytes before and after; binding
  adds a fixed scalar multiplication whose scratch is reclaimed, plus
  linear message validation. Zero timer ticks do not establish speed.

- **CryptoBig/ECDSA, main25810:** reduce wide bases instead of truncating
  high bytes before modular exponentiation, and reject unsupported moduli
  or malformed octet operands. The old code returned 0 for 65536^3 mod 17,
  where the independent answer is 1. ECDSA now rejects non-byte public-key
  and signature elements that preserve the decoded integer; all 32 malformed
  cases were accepted before the repair and are refused after. Eleven
  focused primitive/RSA/ECDSA/certificate/TLS fixtures pass, alongside 28
  independent BigInteger results and 64 ECDSA assertions over 16 independent
  signatures. Wide-base reduction uses O(B*N) time for B base bytes and N
  modulus limbs, reusing one O(N) accumulator. Validation adds linear scans
  with constant auxiliary heap. The compiler source and seed are unchanged.

The SHA review matched 68 API results over 17 messages, including padding
boundaries and lengths through 4096 bytes: SHA256 List/buffer, SHA384 and
SHA512 against three .NET hash implementations. List inputs were preserved.
No algorithm repair was needed for those cases. Buffer offsets, arbitrary
malformed inputs, huge inputs and every-target timing were not graded.

A separate app defect remains in `apps/secrets/secrets-backlog.md`: vault
team sharing hashes private/public concatenation instead of deriving the
participants' shared key. Two valid X25519 participants agree through the
primitive but cannot decrypt the vault share. The current deterministic
recipient-derived nonce also needs an app-level repair. No claim that this
sharing workflow works follows from the primitive results.

The six intervening units and the final math unit leave the compiler dependency closure unchanged
and need no seed replacement. Independent readers checked the diagnostic
claims and strengthened their negative and byte-preservation controls.
Evidence is under red's `build-output/crypto-audit-20260917/`; changelist
descriptions carry the individual receipts. No full release battery was run
for these landings.

## Quotation fixture compatibility

The first release gate exposed stale static signatures in the quotation fixtures:
`quotes-untrusted` reported CDX3024 (invalid signature) before reaching its
intended CDX3025 trust-floor refusal. Main25815 refreshes the shared fixtures
for the digest-byte signing contract with an RFC8032 public test key.
OpenSSL independently verified the refreshed signature. Seven diagnostic
refusals, two exact runtime outputs, disk-store and peer quotation passed,
including rejection from an empty peer. The forged-signature control remains
invalid. Fixture bytes changed; compiler heap/time behavior and seed799 did not.

## Combined release proof

On 2026-09-17, the full gate at main25816 passed in 1,624.5 seconds. The signed
compiler reproduced depot seed799 byte for byte in one pass, and both text
stages were byte-identical. BVT passed 79 compile and 64 runtime checks plus
the batch control. All 211 declared refusal tests passed after the fixture repair.

The normal and poison batteries each passed 1,780 tests with zero failures;
49 declared exclusions were preserved. The poison kernel was used explicitly,
and the working compiler was restored and hash-checked afterwards.
IR fidelity graded its reader and verdict controls and reported nine cases
with zero unexpected results. The separate app sweep reported 292 clean
units, three known baseline failures and zero regressions across 295 units.

Roslyn built the freshly emitted C# compiler. Both DDC arms produced 3,399,989
bytes with zero differences from the release seed outside offsets40..135;
96 signature bytes differed. The symbol map matched all 5,742 embedded MAP1
rows by name, address and size. TechnicalDetails records the seed digest.

Both boot images were rebuilt. The boot image contains the exact release seed.
All 50 diagnostic rehearsal arms passed across Codex VM and QEMU/OVMF, and the
shipping check confirmed the default configuration. The diagnostic image SHA-256
is `A410476F7DFE01173C3B82650BAF65B038A9BDD05732CF7622AC83F234A83F79`.

The first image rehearsal failed four network-related arms at their time limits.
The same image and assertions passed with a 180-second minimum allowance; the
frozen-clock control took 156.6 seconds. Main25820 fixes the harness override:
`-Seconds` now applies to arms with explicit budgets too, retaining any larger
arm budget. Default allowances and assertions are unchanged. The complete
rehearsal was repeated after the repair; partial controls did not certify the image.

Five-second memory samples from the successful gate through image proof measured
a free-memory floor of 3.981 GiB during the full test-compilation phase. The sampled
guest peak was eight, with 3,350.86 MiB combined working set, averaging 418.86 MiB
per guest at that observation. Run-list supervisors were counted separately.
These are sampled host observations, not per-guest heap high-water guarantees.
