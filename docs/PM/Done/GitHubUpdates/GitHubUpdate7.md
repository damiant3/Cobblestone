# GitHub Update 7 -- CL 784 to CL 803+ (2026-05-04)

Previous update: CL 783 (GitHubUpdate6).
This update: CL 803+.

## Verifier Phase 5 -- Proof Verification

The CDX verifier now has all 5 phases (CLs 786-790):

1. **Integrity** -- SHA-256 content hash + Ed25519 signature
2. **Author** -- trust lattice score check
3. **Capabilities** -- policy evaluation per capability entry
4. **Effects** -- effect/capability consistency
5. **Proofs** -- fuel-bounded fact store lookup for proof hashes

`verify-cdx-full` is the 5-phase paranoid path. `verify-cdx` (4-phase
fast path) is unchanged. CDX binaries carry proof hashes in a section
after rodata; the `cdx-flag-has-proofs` flag signals their presence.

Supporting infrastructure:

- **VerifyCache** (CL 787): HAMT-backed verification result cache keyed
  by content hash. O(1) re-verification for unchanged binaries.
- **VerifyReport** (CL 788): diagnostic verifier that runs all 5 phases
  without short-circuiting. Returns per-phase pass/fail with reasons.
- **VerifiedLoader** (CL 789): maps CDX capability entries (cap-id +
  direction) to kernel capability bitmask bits. `evaluate-load` runs
  verification, computes the grant.
- **Verified spawn** (CL 790): end-to-end integration test composing
  `evaluate-load` + `process-spawn` + `restrict-to-grant`. Proves
  capability isolation after verified install.

## Program Registry + Shell Commands

- **ProgramRegistry** (CL 791): verified program install with name,
  content hash, author key, and kernel capability bitmask. Lookup,
  remove, duplicate detection.
- **ShellCommand** (CL 794): typed command dispatch for the OS shell.
  8 commands: `CmdInstall`, `CmdRun`, `CmdRevoke`, `CmdStatus`,
  `CmdListPrograms`, `CmdTrust`, `CmdUntrust`, `CmdAddRule`.
  Immutable state threading with `ShellState` (registry + lattice +
  policy + fact store + fuel).

## Clarifier -- The Semantic Mirror

OS stack item #14 (CL 800). The Clarifier parses an utterance into
solid nodes (understood) and unresolved nodes (ambiguous), sorts
unresolved by priority, and reflects questions in the user's register.

Priority order: Referent > Domain > Cardinality > Threshold > Temporal.
Referents first -- "he", "it", "that thing" must bind before anything
else resolves. Mirrors CPL Rule NP-1.

Four output registers:
- **Child**: "Who do you mean by 'he'?"
- **Casual**: "When you say 'management', who specifically?"
- **Technical**: "Unresolved referent: 'management' (which authority level)"
- **Formal**: "NP-1: referent of 'management' is unresolved."

ShellClarifier bridges the Clarifier to shell command failures --
install denials, missing programs, and policy violations get
register-appropriate diagnostic questions instead of raw errors.

## NE2000 NIC Driver + Full Networking Stack

Layer 1 (CL 795): bare-metal NE2000/DP8390 NIC driver.
- Hardware reset, ISR polling, phantom detection
- DCR/RCR/TCR/ring buffer configuration
- MAC address read from word-doubled NIC PROM
- `net-status` (0 or 1) and `net-get-hwaddr(index)` builtins
- QEMU config: `ne2k_isa` at iobase 0x300, IRQ 9

Layer 2 (CL 801): Ethernet frame build/parse, ARP request/reply,
IPv4 packet framing with one's complement checksum.

Layer 3-4 (CL 802): TCP segment build/parse, 9-state machine
(CLOSED through TIME_WAIT), 3-way handshake, data transfer, teardown.

Layer 5 (CL 803): NetworkStack session API composing Ethernet + IP +
TCP with ARP cache and outbound frame queue. TcpTransport adds
length-prefixed message framing over TCP sessions -- bridges
MessageFraming wire format to the network layer.

## Networking Quick Start

QEMU is configured with a NE2000 ISA NIC via `codex.build/qemu-config.ps1`:

```
-netdev user,id=net0
-device ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56
```

The NIC initializes at boot (before READY). Programs can check
`net-status` (returns 1 if NIC present) and read MAC bytes via
`net-get-hwaddr 0` through `net-get-hwaddr 5`.

Networking forewords (all pure -- no I/O, just byte construction):
- `Ethernet.codex` -- frame encode/decode, ARP, IPv4
- `Tcp.codex` -- segment encode/decode, state machine
- `NetworkStack.codex` -- session API, ARP cache, frame dispatch
- `TcpTransport.codex` -- framed messages over TCP

Kernel-level `net-send-raw` / `net-recv` builtins are in progress
(next CL from Nib). Once landed, the foreword stack drives real
packet I/O.

## Numbers

- **Sample battery**: 190+ pass / 0 fail / 16 skipped / 206+ total
- **Foreword count**: 30+ library chapters
- **Compiler**: ~21,000 lines of Codex across 52 files
- **Seed**: 1,927,568 bytes (CDX), hard fixed point proven
