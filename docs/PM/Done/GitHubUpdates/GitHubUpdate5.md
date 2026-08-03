# GitHub Update 5 -- CL 707 to CL 739 (2026-05-03)

Previous update: CL 706 (GitHubUpdate4).
This update: CL 739.

## CDX Is the Seed

The canonical bootstrap artifact is now `seed/Codex.cdx` (1,845,488
bytes), a CDX binary bootable directly by QEMU via the multiboot AOUT
kludge. The ELF is a derived artifact. All three gates proven on both
paths at the crossover point:

| Gate | CDX path | ELF path |
|------|----------|----------|
| BS2 (text) | 946,094 bytes, stage 1 === stage 2 | 946,094 bytes, stage 1 === stage 2 |
| BS3 (binary) | 1,845,488 bytes CDX fixed point | 1,933,272 bytes ELF fixed point |
| Sweep | 154/0/15 | 154/0/15 |

Text output is byte-identical across CDX and ELF paths (946,094 bytes),
proving semantic equivalence of the two binary formats.

## Preemptive Scheduler -- Complete

Full preemptive process scheduler shipped (CLs 668-735):

- Cooperative yield + spawn (CL 668)
- Timer-driven preemptive context switch via PIT 18 Hz + iretq (CL 711)
- 4 priority levels with per-priority slice table (CL 712)
- CPU quota enforcement: max-ticks per process, zombie on expiry (CL 714)
- process-wait: blocked state, blocked-on tracking, exit wakeup scan (CL 715)
- process-spawn-priority: priority + quota at spawn time (CL 714)
- Capability inheritance: child copies parent's cap bitmask (CL 719)
- ProcessCreate capability check on spawn (CL 718)
- process-restrict-cap: remove capability bit from child, CapabilityAdmin gated (CL 721)
- process-kill: forcible termination with wakeup scan (CL 732)
- process-get-cap: query capability bitmask (CL 735)
- process-get-pid: query current process ID (CL 720)
- Starvation prevention: rescan for priority-2/3 every 100 ticks (CL 728)

Process table: 256-byte entries, 16 slots, full GPR save/restore
via proc table + iretq. Shared `__process_resume` helper.

## IPC Channels -- Kernel Syscalls

4 kernel-level IPC builtins (CL 725): `chan-kern-create`,
`chan-kern-send`, `chan-kern-recv`, `chan-kern-close`. 16-slot channel
table with 8-message ring buffers, gated on cap-ipc (bit 11).

## Fork Pool Reclaim

Completed fork/await tasks now return their descriptor + stack to a
free list (CL 723). The next fork checks the free list before
bump-allocating. Proven with 100-iteration loop test.

## Ed25519 Signing + CDX Verifier

Full Ed25519 sign/verify (CL 722): deterministic RFC 8032 signing,
scalar reduction modulo L, point decompression. Three-phase CDX
verification: integrity (magic + hash + signature), author (trust
lattice score), capabilities (policy evaluation).

## Boot Sequence Progress

- `emit-revoke-capability`: kernel-level capability dropping (CL 729)
- Boot stage 2 probe: reads sector 0 at boot, checks CODEXFS1 magic,
  stores result at fixed address (CL 738)
- `boot-stage-test`: end-to-end capability-dropping proof of concept (CL 729)
- Bootable CDX: multiboot AOUT kludge for direct CDX boot (CL 731)

## Integration Tests

- `supervisor-pattern`: init spawns 3 workers, workers send squares via
  IPC channel, init collects and sums (CL 737)
- `supervisor-kill-restart`: spawn stuck worker, kill, respawn, collect
  result via IPC (CL 737)
- `scheduler-integration`: 3 children write to shared memory, parent
  waits and sums (CL 720)

## Numbers

| Metric | Value |
|--------|-------|
| Sweep | 154 pass / 0 fail / 15 skip / 169 total |
| CDX seed | 1,845,488 bytes |
| ELF (derived) | 1,933,272 bytes |
| Source | ~1,000,054 bytes across 52 codex/ files |
| CLs this push | 707-739 (33 changelists) |
