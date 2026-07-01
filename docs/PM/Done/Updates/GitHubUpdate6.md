# GitHub Update 6 — CL 745 to CL 783 (2026-05-03)

Previous update: CL 739 (GitHubUpdate5).
This update: CL 783.

## Signed CDX

Ed25519 CDX signing integrated into the pingpong build pipeline
(CL 751). The build script generates an inline Codex sample with the
signing key and CDX content hash as byte-array literals, compiles it,
runs it, and patches the CDX header at offsets 40-71 (pubkey) and
72-135 (signature). Signing completes in ~1.4s on bare metal.

## Blocking IPC Channels

Kernel-level blocking send/recv for typed channels (CL 747).
`chan-kern-send-block` and `chan-kern-recv-block` save full GPR state
to the process table, set blocked + blocked-on-chan flags, and the
waker completes the blocked operation on behalf of the blocked process
before waking it. Producer-consumer integration test (CL 748) proves
the blocking semantics end-to-end.

## Process Management

- `process-status` + `process-count` builtins (CL 745)
- Mini-shell sample: interactive process management (CL 746)
- `process-spawn-with-heap`: spawn a child with a user-specified heap
  size (CL 770) — child gets `[heap_size bytes][stack]` carved from
  the parent's heap frontier

## Identity Infrastructure

Full per-process identity system (CLs 755-768):

- 32-byte identity slots in a dedicated identity table (16 entries)
- `identity-set-proc(pid, identity-bytes)` gated by CapabilityAdmin
- `identity-get-proc(pid)` for reading any process's identity
- First-boot ceremony: boot process generates a keypair via RDRAND,
  sets its own identity (CL 757)
- Identity inheritance: child processes inherit parent's identity at
  spawn
- Boot stage markers on COM2 for harness observability (CL 764)
- Integration tests: capability gating, mutual authentication,
  set-proc without admin cap (CLs 767-768, 781)

## Verifier Phase 4 — Effect Metadata

CDX binary format extended with effect metadata section (CL 775).
The previously-reserved header fields at offsets 152-167 are now
populated with effect-offset and effect-size. `verify-effects` checks
that every declared effect has a matching capability in the capability
table. Verifier pipeline chains: integrity → trust → capabilities →
effects.

Additional verifier tests: spawned-process verification, service
pattern, fact-store integration, tampered-binary rejection (CLs 769,
777, 779, 781).

## Fuel Foreword

New `codex.foreword/Fuel.codex` (CL 778): fuel-limited computation
pattern for bounded resource consumption.

## In-Place Crypto

Ed25519 and SHA field element operations rewritten from `list-snoc`
accumulator pattern to `list-set-at` in-place mutation with `fe-copy`
for aliasing safety (CLs 770-772). Eliminates thousands of
intermediate list allocations during signing.

## Sort Foreword + Lower Phase Memory Optimization

New `codex.foreword/Sort.codex` (CL 783): generic in-place quicksort
with median-of-three pivot selection. `sort-by` takes a list and a
comparator function, sorts in place via `list-set-at`, allocates
nothing beyond the input array. O(n log n) average time.

Replaced merge-sort + list-snoc across 8 call sites in the compiler:
`sort-bindings`, `sort-expr-types`, `sort-x86-emitters`,
`sort-x86-arities`, `sort-type-bindings`, `sort-free-vars`,
`sort-raw-builtins`, `sort-emitters`. The old merge sorts allocated
O(n² log n) intermediate lists via list-snoc copies; the new quicksort
allocates O(1).

Also removed dead `ProvLowered` provenance variant and the
`lowered-span` function from the lowering phase — set on every IR node
but never matched anywhere.

Lower phase deck usage: **103.6 MB → 78.9 MB** (24% reduction).
Compile speed unchanged (47s stage 1, 46s stage 2).

## Gates

All three gates green at CL 783:

| Gate | Result |
|------|--------|
| BS2 (pingpong) | 982,827 bytes, stage 1 === stage 2 |
| BS3 (fixed point) | 1,918,472 bytes CDX, byte-identical |
| Sweep | 174 pass / 0 fail / 16 skip / 192 total |

Seed: `seed/Codex.cdx` — 1,918,472 bytes, signed.

## CL Summary

| CL | Author | What |
|----|--------|------|
| 745 | Cam | process-status + process-count builtins |
| 746 | Cam | mini-shell sample |
| 747 | Nib | blocking IPC channels |
| 748 | Cam | blocking producer-consumer test |
| 751 | Nib | signed CDX — Ed25519 in build pipeline |
| 752 | Nib | docs refresh |
| 754 | Cam | bootable disk image + PIT timer |
| 755 | Nib | identity infrastructure |
| 756 | Nib | seed rebuild for identity |
| 757 | Nib | first-boot ceremony |
| 760 | — | doc: record and replay |
| 761 | Nib | identity-set-proc builtin |
| 762 | Nib | identity-get-proc builtin |
| 763 | Nib | trust lattice + identity tests |
| 764 | Nib | boot stage markers on COM2 |
| 765 | Nib | seed rebuild for CLs 757-764 |
| 766 | Nib | milestone history update |
| 767 | Nib | identity integration — cap gate test |
| 768 | Nib | verifier + identity integration |
| 769 | Nib | verifier service test |
| 770 | Nib | process-spawn-with-heap + in-place Ed25519 |
| 771 | Nib | in-place crypto forewords (Sha256, Sha512) |
| 772 | Nib | ge-from-bytes copy fix |
| 773 | Nib | verifier-identity-test expected fix |
| 774 | Nib | seed rebuild for CLs 766-773 |
| 775 | Nib | verifier Phase 4 — effect metadata |
| 776 | Nib | cdx-binary-test expected fix |
| 777 | Nib | verifier Phase 4 test |
| 778 | Nib | Fuel foreword |
| 779 | Nib | fact store + verifier integration tests |
| 780 | Nib | README refresh |
| 781 | Nib | adversarial/negative case tests |
| 782 | Nib | seed rebuild checklist update |
| 783 | Cam | Sort foreword + lower phase memory optimization |
