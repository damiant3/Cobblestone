# IoT Threat Model: Adversarial Analysis

**Created**: 2026-06-12 (reek)
**Status**: Design — not yet started
**Upstream**: All six IoT design docs, `docs/PM/IoT/Compliance/`,
`docs/Designs/OS/Active/TrustAndRuntime.md`,
`docs/Designs/OS/Active/Identity.md`

## Purpose

The other six IoT design docs describe what Codex builds. This
document describes what the adversary does and maps each attack
class to the specific mechanism that defeats it — or honestly names
the residual exposure where one exists. A compliance evidence
package (`ComplianceEvidence.md`) that claims "memory safety by
construction" without identifying what memory safety *prevents* is
marketing, not evidence. This is the adversary's half of the
argument.

## Scope

The threat model covers a Codex IoT device from silicon power-on
through decommissioning. It does not model threats to the cloud
backend, the manufacturer's build infrastructure, or the supply
chain of silicon itself — those are real but outside the toolchain's
enforcement boundary. Where the boundary matters, it is stated.

## Adversary Classes (IEC 62443 security levels)

| SL | Adversary | Resources | Relevant targets |
|---|---|---|---|
| 1 | Unintentional misuse | None (configuration error, operator mistake) | All consumer devices |
| 2 | Intentional, low-skill | Public tools, scripts, known CVEs, default credentials | Consumer IoT, small industrial |
| 3 | Intentional, moderate | Custom tooling, protocol fuzzers, logic analyzers, months of effort | Industrial IoT, medical, critical infra |
| 4 | State-level | Custom silicon, supply-chain implants, zero-days, years of effort | Critical infrastructure, defense-adjacent |

The designs target **SL 2 by construction and SL 3 with
deployment-level hardening**. SL 4 is stated as aspirational; the
specific gaps are named below. Claiming SL 4 without naming the
gaps would be dishonest and counterproductive with certification
bodies.

---

## Attack Surface 1: Firmware Binary

### 1.1 Memory corruption (buffer overflow, heap overflow, use-after-free, double-free)

**The attack**: Malformed input (sensor data, network packet,
configuration blob) triggers a write past an allocated buffer. The
attacker controls the overwritten bytes and redirects execution to
injected payload or a ROP chain built from existing code gadgets.
This is the dominant IoT vulnerability class — ~70% of critical
firmware CVEs (Forescout 2024, Microsoft MSRC historical data).

**Defense — BY CONSTRUCTION**:
- **Linear types** (CDX2061/CDX2063): every resource handle is used
  exactly once on every code path. Use-after-free is a static type
  error. Double-free is a static type error. Resource leak (never
  freed) is a static type error. The checker enforces these at
  compile time; no runtime cost.
- **Bounded integers** (`Integer between L and H`): array index
  types carry their valid range. The static prover (CDX4010)
  eliminates bounds checks where it can prove safety; where it
  cannot, `__narrow` inserts a trap. There is no unchecked index
  operation in the language.
- **No pointer arithmetic**: Codex has no raw pointers exposed to
  user code. Record field access is type-checked offsets. List
  access is bounds-checked. There is no `unsafe` escape hatch.
- **Region-based allocation**: the bump allocator (R10) with
  phase-scoped `heap-save`/`heap-restore` means there is no
  free-list to corrupt. Heap metadata corruption — the classic
  escalation from heap overflow to arbitrary write — is
  structurally impossible because there is no heap metadata.
  Allocation is `mov rax, r10; add r10, rdi; ret` (calloc
  variant: plus `rep stosb`). No linked list, no bins, no
  coalescing.
- **Stack collision check**: every function prologue compares RSP
  against R10 (`cmp rsp, r10; jb __out_of_memory`). Stack overflow
  does not silently corrupt the heap — it traps.

**Residual exposure**: the compiler itself runs on bare metal with
the same allocator. A bug in the compiler's own code (not the
compiled program's) could theoretically corrupt compiler-internal
structures during compilation. The poison-alloc gate (105/105 pass,
Operator's Manual) and the fixed-point requirement (the compiler
must reproduce itself byte-identically) are the mitigations. A
compiler bug that corrupts memory would almost certainly break the
fixed point.

### 1.2 Code injection / arbitrary code execution

**The attack**: attacker gets the device to execute instructions
they control — either by overwriting code in memory, by loading a
malicious firmware image, or by abusing a code-loading mechanism
(dynamic linking, JIT, eval).

**Defense — BY CONSTRUCTION**:
- **No dynamic linking**: CDX binaries are statically linked. There
  is no GOT, no PLT, no ld.so, no dlopen. The attack surface of
  dynamic linker exploitation does not exist.
- **No JIT, no eval, no interpreters**: there is no mechanism in
  the runtime to turn data into executable code. The binary that
  the verifier approved is the only code that runs.
- **W^X enforcement at the memory map level**: code lives at
  0x100000 (load address), data/heap starts at 0x600000 (x86).
  The identity-mapped page tables can enforce execute-never on
  heap/stack regions (NX bit in PTE). On Cortex-M with MPU, the
  code region (flash) is read-execute; SRAM (heap/stack) is
  read-write-no-execute. On Cortex-A, the MMU page tables enforce
  the same split. The emitter must set these permissions in the
  boot chapter for each target (BackendArchitecture.md, boot
  infrastructure section).

**Residual**: the x86-64 bare-metal kernel currently identity-maps
with 2 MB pages and does not set NX bits on data pages. This is a
known gap in the x86 boot infrastructure (the page tables in
`X86_64Boot.codex` are minimal). Adding NX to data/stack pages is
straightforward (bit 63 of the PTE) but is not yet implemented.
For the IoT targets this must be a Phase B1/B3 deliverable, not a
someday item — the Cortex-M MPU and the Cortex-A MMU both support
it and it is table stakes for CRA compliance.

### 1.3 Return-oriented programming (ROP) / code reuse attacks

**The attack**: even without code injection, an attacker who
controls the stack (via a buffer overflow) can chain existing code
fragments ("gadgets") that end in `ret` to perform arbitrary
computation. This bypasses W^X because no new code is introduced.

**Defense — BY CONSTRUCTION (mostly)**:
- The prerequisite for ROP is a stack buffer overflow, which
  requires either an unchecked write to a stack buffer or control
  of an index into a stack-allocated array. Codex has neither:
  local variables are register-allocated or spilled to fixed stack
  slots by the compiler; there are no user-controlled stack buffers.
  The compiler's own stack layout is entirely compiler-determined —
  the programmer cannot declare a local array on the stack.
- The stack collision check prevents stack pivot (redirecting RSP
  into the heap) because the first function call after the pivot
  would see RSP < R10 (or the heap pointer equivalent) and trap.

**Residual**: the bare-metal model with identity-mapped memory
means an attacker with an arbitrary-write primitive could
overwrite return addresses on the stack. The defense relies on
not having the arbitrary-write primitive in the first place —
which the type system ensures for compiled Codex code. If a bug
exists in the compiler's own runtime helpers (the 17+ inline
assembly-level functions like `__str_concat`, `__alloc`,
`emit-read-line-cce-helper`), those are outside the type system's
reach. The poison build and fixed-point gates are the backstop
for that code.

### 1.4 Integer overflow / truncation

**The attack**: arithmetic on integers wraps silently, producing
a value the program did not intend. Classic exploitation: a length
calculation wraps to a small value, a small buffer is allocated,
then a large copy overflows it.

**Defense — BY CONSTRUCTION**:
- `Integer` is 64-bit with no implicit wrapping. Overflow on
  unbounded `Integer` is not possible short of exhausting the
  64-bit range.
- `Integer between L and H` with explicit overflow mode: `error`
  (default, compile-time check on literals, runtime trap on
  dynamic values), `wrapping` (explicit modular arithmetic),
  `clamping` (saturation). The programmer must choose. Silent
  wrapping is impossible unless explicitly requested via the
  `wrapping` keyword — and even then, the type signature
  documents it.
- `__narrow` traps on out-of-range. CDX2071 rejects literal
  values that exceed the type's range. The static prover
  (CDX4010) propagates ranges through arithmetic and elides
  checks where possible.
- On 32-bit targets (decided 2026-06-12): values without a proven
  32-bit range get a refusal diagnostic. The prover's interval
  is printed so the programmer can add the appropriate `between`
  bounds. Silent truncation from 64-bit to 32-bit is a compile
  error, not a runtime surprise.

### 1.5 Malicious firmware image

**The attack**: an attacker provides a firmware image (via OTA,
physical access, or compromised update server) that contains
malicious code.

**Defense — MECHANISM** (see `OTAFirmwareUpdate.md`):
- Gate A (streaming, during download): SHA-256 content hash
  verified, Ed25519 signature verified against the author public
  key embedded in the header. Image with wrong hash or invalid
  signature is rejected before any byte is written to the
  primary flash bank.
- Gate B (pre-activation): full 5-phase verification —
  integrity, author trust (must meet trust-lattice threshold),
  capability policy (requested capabilities must be granted by
  device policy), effect coverage (every declared effect must
  have a matching capability), optional proof verification.
  Firmware requesting capabilities the device doesn't grant (e.g.,
  network access on an air-gapped sensor) is rejected.
- Anti-rollback: monotonic sequence number signed in the release
  fact. A replayed older firmware is rejected even if its
  signature is valid.
- Boot selector re-verification: Gate A is re-run on the
  candidate bank at boot time, closing the TOCTOU window between
  Gate B and execution.
- A/B rollback: if the new firmware fails health checks, the
  device automatically reverts to the old bank. The old firmware
  never stopped being available.

**Residual**: the trust decision depends on the trust lattice
being correctly provisioned. A device with a trust lattice that
trusts the attacker's key will accept the attacker's firmware.
This is a deployment concern (the Identity design's first-boot
ceremony and the provisioning flow own this). The toolchain cannot
prevent a manufacturer from provisioning a bad trust lattice — it
can only ensure the lattice is consulted and the decision is
recorded as a fact.

---

## Attack Surface 2: Network Protocols

### 2.1 Unauthenticated connection / default credentials

**The attack**: attacker connects to the device using default or
no credentials. The Mirai botnet family exploits exactly this —
SSH/Telnet with factory-default username/password across millions
of devices.

**Defense — BY CONSTRUCTION + MECHANISM**:
- There are no passwords in the Codex identity model. Identity is
  an Ed25519 keypair generated at first boot. There is no
  username, no default password, no password database, no
  password recovery mechanism. The concept does not exist in the
  system. (ETSI 5.1-1 satisfied by absence.)
- Network connections are authenticated via the trust handshake:
  proof-of-work challenge → nonce → Ed25519 signature → trust
  score check. An unauthenticated peer cannot send any
  application-layer message.
- SSH and Telnet do not exist. There is no remote shell, no debug
  console accessible over the network. The diagnostic shell
  (`codex.os.kernel/DiagnosticShell`) is serial-only (physical
  access).

### 2.2 Protocol downgrade attacks

**The attack**: a man-in-the-middle forces the device to negotiate
a weaker protocol version or cipher suite. Classic: TLS 1.3
downgrade to TLS 1.0, then exploit known weaknesses.

**Defense — MECHANISM** (see `ProtocolStack.md`):
- MQTT v5.0 only — no 3.1.1 compatibility mode. There is no older
  version to downgrade to.
- TLS 1.3 only (when implemented). The design does not include
  TLS 1.2 or earlier. The cipher suite is fixed: X25519 key
  exchange, AES-256-GCM or ChaCha20-Poly1305 AEAD. There is no
  negotiation that includes weak ciphers because weak ciphers are
  not implemented.
- DTLS: the design recommends 1.3-style handshake with 1.2
  compatibility only if interop requires it. If 1.2 is needed, the
  cipher suite is still restricted to AEAD ciphers — no CBC, no
  RC4, no MD5.
- The CoAP token and message ID are not security mechanisms and
  are not treated as such; security is at the DTLS layer.

**Residual**: if a deployment requires DTLS 1.2 for LwM2M server
interop (Eclipse Leshan), the 1.2 handshake has known weaknesses
(no encrypted SNI, weaker key schedule). The evidence package must
classify this deployment choice honestly — the device is as strong
as its weakest negotiated session.

### 2.3 Replay attacks

**The attack**: attacker records a legitimate message (e.g., an
"unlock door" command) and retransmits it later to re-execute the
action.

**Defense — MECHANISM**:
- Agent protocol messages carry sequence numbers; the trust node
  tracks the last-seen sequence per peer and rejects duplicates
  (TrustNode.codex, replay dedup).
- DTLS/TLS record layer includes implicit sequence numbers in the
  AEAD nonce; replayed records fail authentication.
- CoAP message IDs are checked for deduplication (RFC 7252
  §4.5) within the exchange lifetime (247 seconds by default).
- LwM2M registration lifetime + update means stale registrations
  are garbage-collected by the server; a replayed registration
  would carry a stale lifetime and would not match the server's
  expected sequence.
- OTA anti-rollback: even a valid older firmware with a valid
  signature is rejected if its sequence number is ≤ the committed
  sequence (OTAFirmwareUpdate.md).

**Residual**: CoAP NON (non-confirmable) messages have no built-in
replay protection beyond the message ID cache, which is bounded in
time and space. For security-critical commands, only CON messages
over DTLS should be used. The protocol stack design should enforce
this via effect typing — a command that mutates device state
requires `[Network, Authenticated]`, not just `[Network]`.

### 2.4 Denial of service / resource exhaustion

**The attack**: flood the device with connection requests, large
payloads, or malformed packets to exhaust memory, CPU, or network
buffers and make the device unresponsive.

**Defense — MECHANISM**:
- **Connection-level**: the trust handshake requires proof-of-work
  before the device allocates any session state. A SYN-flood
  equivalent costs the attacker computation per attempt
  (Handshake.codex, proof-of-work challenge).
- **Memory**: bounded allocation everywhere — CoAP retransmit
  queue bounded by NSTART (1), MQTT packet-identifier table
  bounded by 65535 entries (fixed-capacity pre-allocated), all
  accumulator lists pre-allocated via `__list-with-capacity`.
  There is no dynamic allocation in the hot path that an attacker
  can trigger unboundedly.
- **Stack**: collision check in every prologue. A deeply nested
  parsing path that would overflow the stack traps cleanly
  instead.
- **Network buffers**: NE2K receive buffer is fixed (1536 bytes,
  kernel metadata). Packets larger than the buffer are truncated
  at the NIC layer. There is no reassembly buffer an attacker can
  exhaust (CoAP Block transfer reassembles to flash, not RAM —
  see OTA design).

**Residual**: the proof-of-work difficulty is not dynamically
adjustable per the current trust-handshake design. Under sustained
attack, the device cannot increase the cost of first contact. This
is a named gap in the trust layer. Additionally, the NE2K is the
only NIC — it has no hardware filtering, no RSS, no interrupt
coalescing. On real hardware with a real NIC (ESP32-C6 WiFi,
Pi gigabit Ethernet), the hardware capabilities differ and the
driver must expose them.

### 2.5 Man-in-the-middle on first contact

**The attack**: during initial device provisioning (first-boot
ceremony), an attacker intercepts the communication between the
device and the provisioning system and substitutes their own
identity or trust configuration.

**Defense — MECHANISM + DEPLOYMENT**:
- The first-boot ceremony (Identity design) generates the keypair
  locally on the device. The private key never traverses a
  network.
- Trust bootstrapping uses the LwM2M Bootstrap interface or a
  direct physical connection (USB DiskFacts). The bootstrap server
  is itself authenticated via a pre-provisioned trust anchor (a
  factory root key burned into the trust lattice at manufacturing).
- Where physical provisioning (USB stick with trust store) is used,
  the attack requires physical access to the provisioning station.

**Residual**: if provisioning happens over a network without a
pre-shared trust anchor, the bootstrapping is vulnerable to MitM.
The design assumes a factory root key exists — its provisioning
is a manufacturing-line concern outside the toolchain's scope.
The compliance evidence classifies this as DEPLOYMENT.

### 2.6 Amplification attacks (CoAP-specific)

**The attack**: attacker sends a small CoAP request with a spoofed
source IP to a Codex device. The device sends a large response to
the victim. CoAP's typical amplification factor is 10-30x for
resource discovery (/.well-known/core).

**Defense — MECHANISM**:
- CoAP DTLS (coaps://) authenticates the peer before responding,
  eliminating spoofed-source amplification entirely.
- For cleartext CoAP (bring-up mode only): rate-limit responses to
  unknown peers; the trust layer's proof-of-work applies to first
  contact; resource discovery responses should be bounded in size.
- The compliance evidence design gates cleartext CoAP out of any
  compliance-evidence build — if the firmware ships cleartext CoAP,
  the evidence package's ETSI 5.5 claim degrades from MECHANISM to
  DEPLOYMENT(conditional).

---

## Attack Surface 3: Hardware and Physical Access

### 3.1 Debug interface exploitation (JTAG/SWD)

**The attack**: attacker connects to the MCU's JTAG or SWD debug
port (often exposed as test pads on the PCB) and reads/writes
arbitrary memory, dumps firmware, modifies execution state, or
extracts cryptographic keys from SRAM.

**Defense — DEPLOYMENT + MECHANISM (where hardware supports it)**:
- STM32: read-out protection levels (RDP). Level 1 prevents
  flash readout via debug but allows debug otherwise. Level 2
  permanently disables JTAG/SWD (irreversible fuse). The board
  chapter should set RDP Level 2 for production builds.
- ESP32-C6: eFuse-based JTAG disable + flash encryption. When
  JTAG is disabled via eFuse, the debug interface is physically
  disconnected. Flash encryption (AES-256-XTS) means even
  physical flash readout yields ciphertext.
- Raspberry Pi: no hardware debug disable mechanism for the
  ARM cores. The Pi is a gateway target, not an endpoint in
  hostile-physical-access scenarios. If physical security is
  required, the Pi is the wrong board.

**Residual**: this is entirely hardware-dependent and the toolchain
cannot enforce it. The compliance evidence classifies debug-port
lockdown as DEPLOYMENT with specific per-board guidance in the
residual checklist. The evidence plug should emit a warning if the
target board is known to have no debug-disable mechanism.

### 3.2 Side-channel attacks (power analysis, EM emanation)

**The attack**: attacker measures the device's power consumption
or electromagnetic emissions during cryptographic operations and
uses statistical analysis (DPA/DEMA) to recover secret keys.

**Defense — BY CONSTRUCTION (partial)**:
- All cryptographic primitives are constant-time: no branching on
  secret data, no data-dependent memory access, no early exit
  (documented in `docs/Designs/OS/Done/CryptoPrimitives.md`).
  This eliminates simple power analysis (SPA) where the power
  trace directly reveals the operation being performed.
- Ed25519 uses Montgomery ladder (constant sequence of
  double-and-add) or fixed-window with constant-time conditional
  swap. AES-GCM uses T-table-free bitsliced implementation to
  avoid cache-timing leakage.
- SHA-256 compression is data-independent in control flow (the
  round function is the same regardless of input).

**Residual — significant at SL 3+**:
- **Differential power analysis (DPA)**: constant-time code
  defeats SPA but not DPA. DPA uses statistical correlation across
  many traces to recover key bits even when the control flow is
  constant. Countermeasures: randomized blinding (add random mask
  to intermediate values, remove after), shuffling (randomize the
  order of independent operations), dummy operations. None of
  these are implemented. On MCUs without hardware crypto
  acceleration, software DPA countermeasures add 2-10x overhead
  to cryptographic operations.
- **EM emanation**: similar to power analysis but measured remotely
  (up to several meters with sensitive equipment). Same
  countermeasures apply.
- **Fault injection** (voltage glitching, clock glitching, laser):
  attacker induces a computational error at a precise moment to
  skip a security check or corrupt a comparison. For example,
  glitching during Ed25519 signature verification to make
  `verify` return `True` for an invalid signature. Software
  countermeasures: double-check critical comparisons, verify
  before and after branching, use redundant computations.
  **None of these are implemented.** The boot selector's Gate A
  re-check (OTAFirmwareUpdate.md) provides one layer of
  redundancy for the firmware-update path specifically, but the
  general verifier is single-pass.
- **Hardware countermeasures**: the ESP32-C6 has a hardware crypto
  accelerator that implements its own side-channel protections.
  When available, the HAL should prefer the hardware path over
  software crypto for operations that handle long-term keys.
  This is future work — the current crypto chapters are
  software-only.

**Honest assessment**: constant-time software crypto is necessary
but not sufficient for SL 3 physical-access scenarios. The
compliance evidence for IEC 62443 SL 3+ should state this
explicitly: "software cryptographic operations are constant-time
but do not include DPA countermeasures. For SL 3 deployments,
hardware crypto acceleration or external secure elements are
recommended." This is not a failure of the design — it is a
correct scoping of what a compiler can provide.

### 3.3 Cold boot / SRAM remanence

**The attack**: attacker power-cycles the device and reads SRAM
contents before they decay. Cryptographic keys in SRAM are
recoverable for seconds to minutes depending on temperature.

**Defense — MECHANISM (partial)**:
- The Identity design specifies that kernel-pinned keys are zeroed
  on timeout/lock (`memset` equivalent with volatile qualifier to
  prevent elision). On power loss, zeroing does not happen.
- ESP32-C6 flash encryption means the key-encryption-key is in
  eFuse (not SRAM); the runtime key is derived and could be
  recovered from SRAM, but the eFuse key itself is protected.
- STM32 tamper detection (if wired) can trigger an SRAM wipe on
  case-open or voltage anomaly.

**Residual**: on devices without hardware tamper detection, SRAM
remanence is an irreducible physical-access risk. The toolchain
can minimize exposure time (zeroize after use, don't keep keys in
SRAM longer than needed) but cannot prevent a determined attacker
with physical access and a freezer.

### 3.4 Flash readout / firmware extraction

**The attack**: attacker desolders the flash chip or uses debug
interfaces to extract the firmware binary for reverse engineering
or cloning.

**Defense — DEPLOYMENT**:
- ESP32-C6: AES-256-XTS flash encryption at the hardware level.
  The firmware is encrypted at rest; readout yields ciphertext.
- STM32: RDP Level 2 prevents debug readout. Flash encryption
  is available on some STM32 variants (STM32H7 with OTFDEC).
- CDX binaries are signed but not encrypted — the code is
  readable if extracted. The signing prevents *modification* but
  not *inspection*.

**Residual**: firmware reverse engineering is possible on any
platform where the code is not encrypted at rest. For IP
protection, hardware flash encryption is required. The toolchain
does not provide code obfuscation (it would conflict with the
literate-source philosophy). The compliance evidence classifies
this as DEPLOYMENT.

---

## Attack Surface 4: Supply Chain and Lifecycle

### 4.1 Compromised firmware distribution (supply-chain attack)

**The attack**: attacker compromises the build server, CI pipeline,
or distribution channel and injects malicious firmware that is
signed with a legitimate key (because the attacker has access to
the signing infrastructure).

**Defense — MECHANISM (strong)**:
- The fixed-point property: the compiler reproduces itself
  byte-identically. A compromised build that modifies the compiler
  would break the fixed point. Any build environment can verify
  this independently.
- Content-addressed SBOM: every dependency is identified by its
  SHA-256 hash, not by a mutable name/version coordinate. A
  substituted dependency has a different hash and would produce a
  different binary (which would have a different content hash in
  the CDX header, which would invalidate the signature).
- The evidence package includes the source manifest with per-chapter
  hashes. A reviewer (or automated system) can diff the manifest
  against a known-good build to detect additions, removals, or
  modifications.

**Residual**: if the attacker compromises the signing key itself,
the signatures are valid for attacker-produced binaries. This
reduces to the key-compromise scenario (OTAFirmwareUpdate.md §Key
compromise and revocation). The trust-lattice key rotation
mechanism is the response — but the current FactStore lacks a
supersession/revocation primitive, which is a named gap.

### 4.2 Counterfeit device (cloning)

**The attack**: attacker manufactures a device that appears
identical to a legitimate device but runs modified firmware or
exfiltrates data.

**Defense — MECHANISM**:
- Device identity is a locally-generated Ed25519 keypair. The
  first-boot ceremony creates a unique identity that is
  cryptographically bound to that physical device's trust-store
  provisioning. A clone would have a different keypair and would
  not be recognized by the fleet server's trust lattice.
- Where the SoC has a hardware unique ID (STM32 96-bit UID at
  0x1FFF7A10, ESP32-C6 eFuse MAC), the identity ceremony can
  bind the Ed25519 key to the hardware ID as an additional fact.
  This makes the identity non-transferable — the key only works
  on the device it was generated on.

**Residual**: hardware ID binding is optional and depends on the
SoC. Without it, a cloned device with a copied trust store
(including the private key) is indistinguishable from the
original. The compliance evidence classifies hardware ID binding
as DEPLOYMENT.

### 4.3 End-of-life / decommissioning

**The attack**: a decommissioned device retains credentials,
personal data, or fleet access. An attacker acquires the device
(e-waste, resale) and uses the retained credentials to access
the fleet or extract data.

**Defense — MECHANISM + DEPLOYMENT**:
- Capability leases have explicit expiry. A device that has not
  renewed its leases (because it is powered off / decommissioned)
  loses all granted capabilities. The fleet server observes the
  lapsed registration (LwM2M lifetime expiry) and can revoke the
  device's trust-lattice entry.
- Secure erase of identity material: a decommissioning procedure
  (product-level, not toolchain) zeros the identity keypair and
  trust store from persistent storage. On ESP32-C6, eFuse-based
  keys cannot be erased (by design — they are one-time
  programmable); the device should be physically destroyed.
- ETSI 5.11 (easy personal-data deletion) is classified as
  DEPLOYMENT in the compliance evidence; the toolchain provides
  the erase primitives but the product determines what "personal
  data" means.

---

## Attack Surface 5: Cryptographic Attacks

### 5.1 Key exhaustion / nonce reuse

**The attack**: AES-GCM with a repeated (key, nonce) pair
catastrophically leaks the authentication key. With 96-bit random
nonces, the birthday bound is ~2^32 messages per key before
collision probability becomes dangerous.

**Defense — MECHANISM**:
- The HKDF-based key derivation (foreword Hkdf) supports key
  rotation: derive per-session or per-epoch keys from a master
  key, limiting the number of messages encrypted under any single
  key.
- DTLS/TLS record layers use implicit sequence numbers as part of
  the nonce construction, making nonce reuse impossible within a
  session (the sequence is monotonic and the connection is torn
  down before it wraps at 2^64).
- For application-layer encryption (data at rest), the nonce
  management is the caller's responsibility. The evidence
  package should flag any use of `aesgcm-encrypt` outside a
  TLS/DTLS context as requiring a nonce-management audit.

**Residual**: ChaCha20-Poly1305 has the same nonce-reuse
catastrophe. The XChaCha20 variant (192-bit nonce, birthday-safe
at any practical message count) is not currently implemented.
Adding it would be a foreword chapter change.

### 5.2 Timing attacks on non-crypto code

**The attack**: the cryptographic primitives are constant-time, but
application code that makes security decisions based on secret data
may not be. Example: a custom authentication check that returns
early on the first mismatched byte of a token.

**Defense — MECHANISM (partial)**:
- The constant-time comparison function (`ct-compare` or
  equivalent) should be a foreword builtin, not left to the
  application programmer.
- Effect typing ensures that security-critical comparisons are
  only performed in contexts that have the appropriate capability,
  but it does not enforce constant-time execution.

**Residual**: the compiler does not analyze or enforce constant-time
properties of user code. A user who writes `if token == expected`
gets short-circuit string comparison, which is timing-variable.
This is a documentation/API-design concern: the security-sensitive
comparison functions must be clearly marked, and the compliance
evidence should note that application-level timing safety is the
developer's responsibility.

**Research lead (IRISA, 2026-06-23):** The EPICURE team (IRISA D4)
has built static analysis tools that prove constant-time execution
survives compilation — they verify that CompCert-style codegen
preserves information flow properties to machine code. Their work
on RIOT OS (an IoT kernel) directly parallels our bare-metal
kernel. Applying their approach to our emitter could close R8 by
construction: the compiler would reject or flag user code whose
emitted x86-64 contains data-dependent branches on secret-typed
values. See `docs/Reference/IRISA_Research_Harvest.md` item 3.

The GnuZero tool (SPICY team, IRISA D1, DSN'25 Best Paper) detects
when compilers optimize away security-critical memory clearing. Since
we own the emitter, we can make zeroization a semantic guarantee
immune to dead-store elimination — a `secure-erase` annotation or
effect that the emitter enforces unconditionally.

---

## Cross-Reference: Defense Mechanism by Design Document

| Design Document | Attack classes it addresses |
|---|---|
| BackendArchitecture | 1.2 (W^X per target), 1.3 (stack layout), 3.1 (debug lockdown per board) |
| HardwareAbstractionLayer | 1.1 (linear handles prevent peripheral misuse), 3.1 (JTAG disable in board chapters), 3.2 (hardware crypto preference) |
| ProtocolStack | 2.1-2.6 (all network attacks), 5.1 (nonce management via TLS/DTLS) |
| ComplianceEvidence | Maps every attack class to a regulatory requirement and evidence artifact |
| OTAFirmwareUpdate | 1.5 (malicious firmware), 2.3 (replay/anti-rollback), 4.1 (supply chain via SBOM) |
| CrossArchitectureTestStrategy | 1.5 (OTA rejection tests in battery), all (encoder golden vectors catch codegen bugs that could open holes) |

---

## Consolidated Residual Risk Register

These are the honest gaps — things the toolchain cannot fully
address and that the compliance evidence must classify honestly:

| ID | Gap | SL impact | Mitigation path |
|---|---|---|---|
| R1 | No DPA/fault-injection countermeasures in software crypto | SL 3+ | Hardware crypto acceleration via HAL; software blinding is future work |
| R2 | NX bit not set on x86 data pages (current boot infra) | SL 2 | Straightforward PTE change; must be Phase B1 deliverable for IoT targets |
| R3 | FactStore lacks supersession/revocation | SL 2+ | Required for key rotation and OTA trust management; trust-layer backlog item |
| R4 | Forensic chain not durably persisted | SL 2+ | Audit-trail claims capped at MECHANISM(partial) until implemented |
| R5 | Proof-of-work difficulty not dynamically adjustable | SL 2+ | DoS resilience limited under sustained attack; trust-layer enhancement |
| R6 | No hardware ID binding by default | SL 2+ | Per-SoC; DEPLOYMENT classification; ESP32-C6 eFuse MAC available |
| R7 | Debug interfaces (JTAG/SWD) are a board-level concern | SL 3+ | Board chapters document lockdown; compliance evidence prints per-board checklist |
| R8 | No constant-time enforcement for user code | all | API design + documentation; `ct-compare` builtin in foreword; EPICURE-style verified compilation (see 5.2 research lead) |
| R9 | Cold boot / SRAM remanence | SL 3+ | Zeroization on timeout; hardware tamper detection is DEPLOYMENT |
| R10 | Cleartext CoAP/MQTT in bring-up mode | SL 2+ | Gated out of compliance-evidence builds; TLS/DTLS mandatory for production |

## Memory and Time-Complexity Risk

This document is analysis, not code. No memory or time-complexity
assessment required.

## Open Questions

1. **NX enforcement priority.** Should NX bits on data/stack
   pages be added to the x86 boot infrastructure now (before IoT
   work begins), or is it sufficient to implement it per-target
   in the IoT boot chapters? Recommendation: now, because it
   strengthens the x86 security story for the Monday demo and
   the compliance evidence.
2. **Hardware crypto HAL surface.** The ESP32-C6's RSA/AES/SHA
   accelerators and the STM32H7's crypto coprocessor have
   different register interfaces. Should the foreword crypto
   chapters gain a dispatch mechanism (software vs hardware
   implementation selected at compile time based on target), or
   should board chapters provide their own crypto effect handlers?
   Recommendation: foreword defines the API; board chapters
   provide the implementation via effect handlers; the capability
   manifest indicates whether hardware crypto is in use.
3. **Fault-injection countermeasures scope.** Double-verification
   of the Ed25519 signature in the boot selector (already
   designed in OTA) is one countermeasure. Should the verifier
   itself gain redundant checks (verify, then re-verify with a
   different code path)? This adds code size and latency.
   Recommendation: yes, for the boot selector only (it is small
   and on every boot path); not for the general verifier (it
   runs behind Gate B, which is already behind Gate A).
4. **Secure element support.** External secure elements (ATECC608,
   Infineon OPTIGA) offload key storage and signing to tamper-
   resistant hardware. Should the Identity design support them?
   This is an I2C peripheral from the HAL's perspective but adds
   a new trust anchor model. Defer to design-partner requirements.
