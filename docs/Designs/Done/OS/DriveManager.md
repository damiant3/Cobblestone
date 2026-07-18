# Drive Manager & Installer — Boot, Format, Partition, Deploy

**Date**: 2026-05-18
**Status**: Design
**Depends on**: ATA block driver (done, X86_64Boot.codex), FAT16 reader
(done, foreword/Fat16.codex), DiskFacts (done, os/kernel/DiskFacts.codex),
GPT writer (done, Emit/GptWriter.codex), FAT32 writer (done,
Emit/Fat32Writer.codex), Crypto primitives (done), Identity design,
FirstBoot.codex (partial)
**Unblocks**: USB installer, first-time experience, self-deploying OS,
dual-boot, headless fleet deployment

---

## The Vision

A Codex USB stick is the root of trust. You boot it on any x86-64
UEFI machine. If the stick is fresh (never used), the first-time
experience runs: generate your Ed25519 keypair, encrypt and store
it on the stick, establish your identity. From there, you land in
the UEFI console — a text-mode shell that is always available,
even on headless machines with no GPU.

From the console you can:
- Boot into the graphical UI (if a boot drive is configured)
- Stay in the console (headless / no-GUI deployments)
- Enter the Drive Manager to format, partition, and manage storage

The stick carries your identity. The target drive carries the OS.
The two are separate. You can plug the stick into any machine and
your identity travels with you.

---

## Boot Flow

```
Power on
  → UEFI firmware reads GPT on USB stick
  → Loads EFI/BOOT/BOOTX64.EFI from stick's ESP
  → PE32+ stub → __start → Codex kernel

Kernel init:
  → Stage 0: Hardware (multiboot/UEFI, long mode, PIC, IDT, serial)
  → Stage 1: Block driver (enumerate ATA channels, detect disks)
  → Stage 2: Read system DB from stick
      → System DB missing or empty?
          YES → First Boot Ceremony (see below)
          NO  → Load identity, load config
  → Stage 3: UEFI Console ready
      → Config says boot-drive exists?
          YES → Prompt: "Boot into [drive]? (Y/n, timeout 5s)"
                  → Y or timeout → chainload installed kernel from drive
                  → N → stay in console
          NO  → Stay in console
```

### First Boot Ceremony

When the system DB on the stick has no identity:

```
1. Welcome screen
     "Welcome to Codex. This stick has no identity."

2. Generate Ed25519 keypair from hardware entropy (RDRAND)
     Mix in user-provided entropy: prompt for a passphrase
     The passphrase protects the private key at rest

3. Derive encryption key via HKDF(SHA-256, passphrase, salt)
     salt = 16 bytes from RDRAND, stored in plaintext in system DB

4. Encrypt private key: AES-256-CBC(derived-key, private-key)
     Store in system DB as IdentityFact:
       { salt, encrypted-private-key, public-key, created-ticks }

5. Display fingerprint, confirm
     "Your identity: [fingerprint]. Keep this passphrase safe."

6. Write BootConfig to system DB:
     { boot-mode = BootDevConsole, boot-drive = None }

7. Enter UEFI console
```

On subsequent boots, the kernel reads the system DB, finds the
IdentityFact, prompts for the passphrase, decrypts the private key
into memory. The plaintext key is held in RAM only for the session
and wiped on shutdown.

---

## System DB

The system DB lives on the USB stick's FAT32 ESP in a reserved
sector range. It uses the existing DiskFacts format: dual-sector
superblock ("CODEXFS1" magic), append-only fact log, SHA-256
integrity per fact.

### Fact Kinds (System DB)

| Kind | Contents | Purpose |
|------|----------|---------|
| `IdentityFact` | salt + encrypted private key + public key + timestamp | Owner identity |
| `BootConfigFact` | boot-mode + boot-drive UUID + boot-partition | Persistent config |
| `TrustVouchFact` | vouch for another public key | Trust lattice root |
| `DriveRegistryFact` | drive UUID + label + last-seen timestamp | Known drives |
| `InstalledOsFact` | drive UUID + partition index + kernel hash | Installed OS records |

The system DB is small — a few KB at most. It rides on the FAT32
filesystem's reserved sectors (between the BPB and the data area),
or in a dedicated file `CODEX/SYSDB.BIN` on the ESP.

---

## Disk Enumeration

### The Problem

The existing ATA driver (X86_64Boot.codex) hardcodes the primary
IDE channel (ports 0x1F0-0x1F7, control 0x3F6). A machine may have
multiple ATA channels and multiple drives per channel. The installer
needs to see all of them.

### Design

Enumerate four standard ATA targets:

| Channel | I/O Base | Control | Drive Select |
|---------|----------|---------|-------------|
| Primary Master | 0x1F0 | 0x3F6 | 0xE0 |
| Primary Slave | 0x1F0 | 0x3F6 | 0xF0 |
| Secondary Master | 0x170 | 0x376 | 0xE0 |
| Secondary Slave | 0x170 | 0x376 | 0xF0 |

For each target:
1. Select drive (write drive-select byte to port base+6)
2. Send IDENTIFY (0xEC)
3. If status = 0x00 or 0xFF → no drive, skip
4. If DRQ set → read 256 words of identify data
5. Extract: model string (words 27-46), serial (words 10-19),
   sector count (words 60-61 for LBA28, words 100-103 for LBA48),
   sector size (word 106, or assume 512)

Store results in a `DriveInfo` list:

```
DriveInfo = record {
  channel     : Integer,    -- 0 = primary, 1 = secondary
  position    : Integer,    -- 0 = master, 1 = slave
  model       : Text,
  serial      : Text,
  sector-count: Integer,
  sector-size : Integer,
  size-mb     : Integer     -- computed: sector-count * sector-size / 1048576
}
```

### Syscall Extension

The existing block-read/block-write syscalls (RAX=10, 11) operate
on an implicit "current drive." Add a drive-select syscall:

| Syscall | RAX | RDI | Description |
|---------|-----|-----|-------------|
| `block-select` | 13 | drive-index (0-3) | Select active drive |
| `block-enumerate` | 14 | buffer-ptr | Write DriveInfo array to buffer |

`block-select` updates the global ATA channel/drive-select state.
Subsequent block-read/block-write target the selected drive.
`block-enumerate` runs IDENTIFY on all four targets and writes
results to the provided buffer.

Both require `BlockDevice` capability (bit 10).

---

## GPT Runtime Read/Write

### GPT Read

Parse a GPT disk to discover partitions. The GPT writer
(Emit/GptWriter.codex) already knows the struct layouts; this is
the inverse operation.

```
read-gpt : Integer -> Maybe GptDisk

GptDisk = record {
  disk-guid       : Guid,
  partition-count : Integer,
  partitions      : List GptPartition
}

GptPartition = record {
  type-guid    : Guid,
  unique-guid  : Guid,
  start-lba    : Integer,
  end-lba      : Integer,
  attributes   : Integer,
  name         : Text
}
```

Procedure:
1. Read sector 0 (protective MBR) — verify 0x55AA signature
2. Read sector 1 (GPT header) — verify "EFI PART" signature
3. Validate header CRC32
4. Read partition entry sectors (header says where and how many)
5. Validate partition entries CRC32
6. Parse each 128-byte entry: type GUID, unique GUID, start/end
   LBA, attributes, UTF-16LE name

### GPT Write

Create a fresh GPT on a drive. Reuse logic from GptWriter.codex
but target the ATA syscalls instead of a file buffer.

```
write-gpt : Integer, List GptPartitionSpec -> Boolean

GptPartitionSpec = record {
  type-guid  : Guid,        -- ESP, Codex data, etc.
  name       : Text,
  size-mb    : Integer       -- 0 = fill remaining space
}
```

Procedure:
1. Zero sector 0 (kill any existing MBR)
2. Write protective MBR
3. Compute partition layout from specs (align to 1 MiB boundaries)
4. Write GPT header (sector 1) with CRC32
5. Write partition entries (sectors 2-33)
6. Write backup GPT header (last sector)
7. Write backup partition entries (sectors before last)

---

## FAT32 Formatter

Create a FAT32 filesystem on a partition. The FAT32 writer
(Emit/Fat32Writer.codex) knows the format; this operates at
runtime via ATA syscalls.

```
format-fat32 : Integer, Integer, Text -> Boolean
  -- start-lba, sector-count, volume-label
```

Procedure:
1. Compute geometry: sectors-per-cluster (8 for typical sizes),
   reserved sectors (32), FAT count (2), FAT size
2. Write BPB/boot sector at partition start
3. Write FS Info sector (sector 1 within partition)
4. Write backup boot sector (sector 6)
5. Zero both FAT tables
6. Set FAT[0] = 0x0FFFFFF8, FAT[1] = 0x0FFFFFFF (reserved)
7. Allocate cluster 2 for root directory, zero it
8. Set FAT[2] = 0x0FFFFFFF (end of chain)
9. Write volume label directory entry in root

### FAT32 File Write

Extend the existing FAT16 foreword pattern to FAT32 write:

```
fat32-write-file : Fat32Context, Text, List Integer -> Boolean
  -- context, path, file-bytes
```

Procedure:
1. Parse path into directory components
2. Navigate to parent directory (create if needed)
3. Allocate clusters for file data (scan FAT for free clusters)
4. Write file data to allocated clusters
5. Update FAT chain entries
6. Write directory entry (name, size, start cluster, timestamps)

This enables copying the kernel, seed, and source tree onto a
freshly formatted drive.

---

## Encrypted Key Store

The private key must never exist on disk in plaintext.

### Encryption Scheme

```
passphrase → HKDF-SHA256(passphrase, salt, info="codex-identity") → 256-bit key
private-key → AES-256-CBC(key, iv, private-key) → encrypted-blob
```

- `salt`: 16 bytes from RDRAND, generated once at first boot
- `iv`: 16 bytes from RDRAND, generated fresh each time the key
  is re-encrypted (passphrase change)
- `info`: fixed string "codex-identity" for domain separation

Stored in system DB as:

```
IdentityFact = record {
  version        : Integer,       -- 1
  salt           : List Integer,  -- 16 bytes
  iv             : List Integer,  -- 16 bytes
  encrypted-key  : List Integer,  -- 48 bytes (32 key + 16 padding)
  public-key     : List Integer,  -- 32 bytes
  created-ticks  : Integer
}
```

### Unlock Flow

```
1. Read IdentityFact from system DB
2. Prompt: "Passphrase: " (echo disabled)
3. Derive key: HKDF-SHA256(input, salt, "codex-identity")
4. Decrypt: AES-256-CBC-decrypt(key, iv, encrypted-key)
5. Verify: derive public key from decrypted private key
   → matches stored public-key? Accept. Otherwise: "Wrong passphrase."
6. Hold plaintext private key in memory
7. On shutdown: zero the memory region
```

### What We Have

- AES-256: `foreword/core/Aes.codex` — AES-128 exists, extend to
  AES-256 (14 rounds, 240-byte round key)
- SHA-256: `foreword/core/Sha256.codex` — done
- Ed25519: `foreword/core/Ed25519.codex` — done
- HKDF: needs implementation (~40 lines, HMAC-SHA256 extract+expand)

### What's Missing

- **AES-256**: Current AES foreword is AES-128. AES-256 is the same
  structure with 14 rounds instead of 10 and a 256-bit key schedule.
  ~50 lines of changes.
- **HKDF-SHA256**: HMAC-SHA256 extract step + expand step. ~40 lines.
- **CBC mode**: The AES foreword has ECB and CBC for AES-128. Extend
  to AES-256. Minimal change.

---

## UEFI Console — Drive Manager

The UEFI console is a text-mode menu system (DevConsoleMenu.codex
pattern). The Drive Manager is a submenu accessible from the main
console.

### Console Main Menu

```
  Codex Console                    [identity fingerprint]
  ═══════════════════════════════════════════════════════

  [B] Boot installed OS            (drive: NVMe 1TB "CODEX-SYS")
  [D] Drive Manager
  [C] Compiler / Dev Console
  [S] Settings
  [R] Reboot
  [P] Power off
```

### Drive Manager Menu

```
  Drive Manager
  ═════════════

  Detected drives:
    0: ATA Primary Master    Samsung 870 EVO    500 GB
       GPT: 1 partition (EFI System, 500 GB, FAT32)
    1: ATA Primary Slave     WD Blue 1TB        1000 GB
       GPT: 2 partitions (EFI System 512MB, Codex Data 999 GB)
    2: ATA Secondary Master  (USB stick — this device)

  [I] Inspect drive
  [F] Format drive
  [P] Partition drive
  [C] Copy files
  [N] Install Codex to drive
  [W] Wipe drive
  [A] RAID configuration
  [B] Back
```

### Inspect Drive

Read and display GPT, partition table, filesystem info. For each
partition: type, size, filesystem type (detect FAT32 BPB magic,
DiskFacts superblock magic, or unknown), used/free space.

### Format Drive

```
  Format Drive 0 (Samsung 870 EVO, 500 GB)
  WARNING: This will destroy all data on the drive.

  Filesystem:
    [1] FAT32 (UEFI compatible, max 4GB files)
    [2] Codex FS (content-addressed fact store)

  Type number and press Enter, or [B] to go back:
```

For FAT32: calls `format-fat32` on the selected partition.
For Codex FS: initializes DiskFacts superblock on the partition.

### Partition Drive

```
  Partition Drive 0 (Samsung 870 EVO, 500 GB)
  WARNING: This will destroy the existing partition table.

  Scheme:
    [1] Single partition (entire drive)
    [2] EFI System + Codex Data (recommended for Codex install)
    [3] EFI System + Codex Data + Windows preserve
    [4] Custom (manual partition editor)

  Type number and press Enter:
```

Scheme 2 (recommended):
- Partition 1: EFI System Partition, 512 MB, FAT32
- Partition 2: Codex Data, remaining space, Codex FS

Scheme 3 (dual boot — preserves existing Windows ESP):
- Read existing GPT, find Windows ESP
- Shrink last partition or use unallocated space
- Add Codex partitions after Windows

### Install Codex to Drive

The core installer operation. Creates a bootable Codex installation
on a target drive.

```
  Install Codex to Drive 0
  ═════════════════════════

  This will:
    1. Create GPT with EFI System + Codex Data partitions
    2. Format EFI System as FAT32
    3. Copy kernel (BOOTX64.EFI) to EFI/BOOT/
    4. Copy seed (Codex.cdx) to seed/
    5. Copy compiler source to codex/
    6. Format Codex Data as Codex FS
    7. Register this drive as boot target

  Proceed? [Y/n]
```

Steps:
1. `write-gpt` with ESP (512 MB) + Data (rest)
2. `format-fat32` on ESP partition
3. Copy files from USB stick's ESP to target ESP:
   - `EFI/BOOT/BOOTX64.EFI` (kernel)
   - `seed/Codex.cdx`
   - `codex/**` (source tree)
4. Initialize DiskFacts on data partition
5. Write `InstalledOsFact` to USB stick's system DB
6. Update `BootConfigFact` with new boot drive UUID

After install, the machine can boot from the target drive
directly. The USB stick is needed only for identity (passphrase
unlock) and recovery.

### Copy Files

Basic file operations between drives/partitions:

```
  Copy Files
  ══════════

  Source: Drive 2 (USB stick), Partition 1 (ESP)
  Target: Drive 0, Partition 1 (ESP)

  [A] Copy all files
  [S] Select files to copy
  [B] Back
```

Uses FAT32 read from source + FAT32 write to target.

### Wipe Drive

```
  Wipe Drive 0 (Samsung 870 EVO, 500 GB)

  [Q] Quick wipe (zero GPT headers + first 1MB)
  [F] Full wipe (zero entire drive — SLOW)
  [S] Secure wipe (ATA SECURITY ERASE — if supported)
  [B] Back

  WARNING: This cannot be undone.
```

Quick wipe: zero sectors 0-2048 + last 33 sectors (GPT backup).
Full wipe: zero every sector (progress bar).
Secure wipe: ATA SECURITY ERASE UNIT command (0xF4) if the drive
supports it (check IDENTIFY word 82 bit 1).

### RAID Configuration

Software RAID managed by the kernel. Phase 1 supports mirror
(RAID-1) only — the simplest and most useful for data protection.

```
  RAID Configuration
  ══════════════════

  No RAID arrays configured.

  [C] Create mirror (RAID-1)
  [B] Back
```

Create mirror:
1. Select two drives of equal or similar size
2. Create matching GPT on both
3. Initialize DiskFacts on both with matching superblock
4. Mark array in system DB (RaidArrayFact)
5. All writes go to both drives; reads from either

RAID-1 implementation:
- Intercept block-write syscall: write to both drives
- Intercept block-read syscall: read from primary, fallback to
  secondary on error
- Rebuild: copy all sectors from good drive to replacement
- Status: compare superblock generation counters

Higher RAID levels (0, 5, 6) are deferred. Mirror is sufficient
for the use case of "don't lose my identity and work."

---

## Dual Boot

Chainloading Windows (or another OS) from the Codex console.

### Detection

When inspecting a drive's GPT, look for:
- Microsoft Basic Data partition (GUID `EBD0A0A2-B9E5-...`)
- EFI System Partition containing `EFI/Microsoft/Boot/bootmgfw.efi`

If found, display in the console:

```
  Drive 0 has a Windows installation.
  [W] Boot Windows
```

### Chainload

To boot Windows from the Codex UEFI console:
1. Read the Windows `bootmgfw.efi` from the ESP into memory
2. Set up PE32+ headers, relocate if needed
3. Jump to the Windows bootloader entry point

Alternatively, during install (scheme 3), register both boot
entries in the UEFI Boot Manager (via EFI runtime services if
available, or by writing NVRAM variables through the UEFI stub).

### Codex Boot Entry

For machines that boot from the installed drive (not USB stick):
- The Codex kernel is at `EFI/BOOT/BOOTX64.EFI` on the ESP
- UEFI firmware picks it up as the default boot entry
- On boot, check for USB stick presence:
  - Stick present → unlock identity from stick
  - Stick absent → boot with device-only identity (limited
    capabilities, no signing)

---

## Implementation Sequence

| Step | Component | Effort | Depends On | New Code |
|------|-----------|--------|------------|----------|
| 1 | Disk enumeration (4-target ATA scan) | Small | Existing ATA driver | ~150 lines |
| 2 | block-select syscall | Small | Step 1 | ~30 lines (boot codegen) |
| 3 | GPT read (parse headers + entries) | Medium | Step 2 | ~200 lines |
| 4 | GPT write (runtime, via ATA) | Medium | Step 3 | ~200 lines (adapt GptWriter) |
| 5 | FAT32 formatter | Medium | Step 2 | ~300 lines |
| 6 | FAT32 file write | Large | Step 5 | ~400 lines |
| 7 | AES-256 + HKDF | Small | Existing AES-128 + SHA-256 | ~90 lines |
| 8 | System DB (DiskFacts on stick) | Small | Existing DiskFacts | ~100 lines (new fact kinds) |
| 9 | First Boot Ceremony | Medium | Steps 7, 8 | ~200 lines (extend FirstBoot.codex) |
| 10 | UEFI Console shell | Medium | Step 8 | ~300 lines (extend DevConsoleMenu) |
| 11 | Drive Manager UI | Medium | Steps 3-6, 10 | ~400 lines |
| 12 | Install Codex to drive | Large | Steps 4-6, 11 | ~300 lines |
| 13 | Wipe + format commands | Small | Steps 4-5, 11 | ~100 lines |
| 14 | RAID-1 mirror | Large | Step 2 | ~500 lines |
| 15 | Dual boot / chainload | Medium | Step 12 | ~200 lines |

**Total estimate**: ~3,570 lines of new Codex code.

### Critical Path

```
Step 1-2 (disk enum + select)
  → Step 3-4 (GPT read/write)
    → Step 5-6 (FAT32 format + write)
      → Step 12 (install to drive)

Step 7 (AES-256 + HKDF)
  → Step 8 (system DB)
    → Step 9 (first boot ceremony)

Step 10 (console shell)
  → Step 11 (drive manager UI)
```

Steps 1-6 (storage infrastructure) and 7-9 (identity/crypto) are
independent tracks. Step 10-11 (UI) depends on both. Step 12
(installer) is the integration point.

---

## New Capability Bits

| Bit | Name | Used By |
|-----|------|---------|
| 10 | `BlockDevice` | Drive Manager (existing) |
| 16 | `DriveFormat` | Format, partition, wipe operations |
| 17 | `IdentityAdmin` | Key generation, passphrase change |

`DriveFormat` is separate from `BlockDevice` because reading a
drive is much less dangerous than formatting it. The UEFI console
holds both; user programs get `BlockDevice` read-only at most.

---

## What This Does NOT Cover

- **AHCI/SATA/NVMe drivers.** ATA PIO works for QEMU and older
  hardware. Modern drive interfaces are a separate design.
- **UEFI runtime services.** Chainloading and NVRAM access require
  UEFI protocol implementation (see CODEX-VM-UEFI.md). The
  installer works without it (direct sector writes to ESP).
- **Network install.** PXE boot, network image download. The
  network stack exists (os/net/) but integration is deferred.
- **Encryption at rest for the boot drive.** The USB stick's
  identity is encrypted. Full-disk encryption for the target drive
  is a separate design (requires boot-time decryption layer).
- **Filesystem journaling.** DiskFacts is crash-safe by design
  (append-only + COW HAMT). FAT32 writes are not journaled —
  acceptable for installer use, not for production filesystem.

---

## Open Questions

1. **System DB location on the stick.** Reserved sectors in FAT32
   (between BPB and data area, ~32 sectors available) vs. a regular
   file (`CODEX/SYSDB.BIN`). File is simpler (use FAT32 read/write);
   reserved sectors are more tamper-resistant (invisible to other
   OSes that mount the stick).

2. **Passphrase vs. hardware key.** The design uses a passphrase.
   Should we also support TPM 2.0 key sealing? TPM would enable
   "insert stick and boot, no passphrase needed" but ties the stick
   to one machine. Both could coexist (passphrase as fallback).

3. **UEFI Secure Boot.** Should the Codex kernel's PE32+ stub be
   signed with a key that UEFI firmware trusts? Without this,
   machines with Secure Boot enabled won't boot the stick. Options:
   enroll a custom Secure Boot key (requires setup), use shim
   (depends on Microsoft's third-party key), or require Secure Boot
   to be disabled.

4. **Drive identity.** How to uniquely identify a drive across
   reboots? ATA serial number from IDENTIFY is the simplest. For
   the RAID and boot-drive-registry use cases, we need stable IDs.

5. **File copy progress.** Copying gigabytes over ATA PIO is slow
   (~3-5 MB/s). The UI needs a progress indicator. How to estimate
   time remaining when sector-level writes have variable latency?

6. **LBA48.** The existing ATA driver uses LBA28 (max 128 GB).
   Drives larger than 128 GB need LBA48 addressing (48-bit sector
   numbers). The driver needs to detect LBA48 support from IDENTIFY
   and use the extended command set (0x24 read, 0x34 write).
