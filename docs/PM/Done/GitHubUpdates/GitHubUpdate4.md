# GitHub Update 4 -- CL 679 to CL 706 (2026-05-02 to 2026-05-03)

Previous update: CL 678 (GitHubUpdate3).
This update: CL 706.

## VM Profiles -- Codegen Knobs for Target Environments

New `codex/Core/VmProfile.codex` introduces a VM profile system that
controls target-specific code generation. Two profiles ship:

| Profile | Behavior |
|---------|----------|
| `spec` (default) | Full ATA bring-up at boot: soft reset, IDENTIFY DEVICE, sector-count extraction |
| `QEMU-11.0.0` | Skips ATA bring-up (works around WHPX IDE buffer initialization bug) |

Profiles are selected at compile time via the mode protocol:
`"BINARY"` → spec, `"BINARY QEMU-11.0.0"` → QEMU profile.
All harness scripts now send `QEMU-11.0.0` by default.

## ATA PIO Block Driver -- Spec-Compliant Bring-Up

The boot sequence gained proper PATA detection and IDENTIFY DEVICE
support under the `spec` profile:

- Soft reset with spec-compliant timing (100-read hold/clear delays).
- Bounded BSY/DRDY waits (1M iteration cap -- no infinite loops).
- Drive signature detection (LBA mid/high = 0/0 → PATA).
- IDENTIFY DEVICE with ERR/DRQ checking.
- `block-sector-count` syscall (RAX=12) returns LBA28 sector count.
- New `block-identify` sample (returns sector count; skipped in
  default sweep, runs via `codex.build/run-with-disk.ps1`).

## Workspace Reorganization (Cam, CLs 695–703)

| Before | After | CL |
|--------|-------|----|
| `Codex.Codex/` | `codex/` | 696 |
| `samples/` | `codex.test/` | 702 |
| `foreword/` | `codex.foreword/` | 702 |
| `tools/*.ps1` | `codex.build/*.ps1` | 703 |
| `seed/Codex.Codex.elf` | `seed/Codex.elf` | 706 |
| Build artifact `Codex.Codex.elf` | `Codex.elf` | 697 |

## Seed Refresh

The seed is rebuilt to a 1,828,760-byte hard fixed point (CL 704/706):

| Algorithm | Digest |
|---|---|
| MD5 | `648f4eee6a9085c7c1c5cf7838e01b14` |
| SHA-256 | `4ada40418df49f222c6f20b2400bd58781f5ad4407f597ec13a995db8ef35f98` |

BS3 proves byte-identity: stage 1 ELF compiled by seed === stage 2 ELF
compiled by stage 1. Sweep: 138 pass / 0 fail / 14 skipped.

## Other

- Right-sized record fields + groomed const definitions (CLs 675–676, 671).
- Verbose 3-line consts collapsed to single-line form (110 sites, CL 671).
- Process scheduler V0 yield helper (CL 668, cam).
- Deck-waste audit documented (CL 663 shelved diagnostic).
- Seed rebuild checklist formalized (`docs/SEED-REBUILD-CHECKLIST.md`, CL 705).
- 3 WHPX BSOD incidents documented in `docs/Bugs/`.
