# App Persistence Layer

**Author**: Red
**CL**: 3496
**Date**: 2026-06-08

## Overview

Persistent storage for all Codex apps using DiskFacts. Each app
serializes its state to text and writes it as a fact with an
app-specific kind integer. On startup, each app scans the DiskFacts
log for its latest save and restores state.

## Architecture

```
App opening.codex
  │  calls disk-load at startup
  │  scans for saved state via app-scan-last
  │  restores into app state
  │  runs event loop with DiskFactStore threaded alongside app state
  │  detects state changes and calls app-write-and-checkpoint
  ▼
AppPersist.codex (codex.os.kernel) — shared helpers
  │  app-write-fact: multi-sector fact writer (no 434-byte limit)
  │  app-scan-last: find most recent fact of a kind
  │  app-scan-all: find all facts of a kind
  ▼
DiskFacts.codex (codex.os.kernel) — log-structured storage
  │  block-read-sector / block-write-sector (builtins)
  │  dual-superblock crash recovery
  ▼
IDE disk (codex-vm)
```

## Kind Allocation

| Kind | App | Data |
|------|-----|------|
| 0–4 | Core | DefinitionFact..CapabilityFact |
| 10–14 | SystemDb | Identity, boot config, drive registry |
| 20 | Browser | Browsing history (all entries) |
| 21 | Browser | Trust store decisions |
| 22 | Diagram | Full diagram JSON |
| 23 | Secrets | Vault index (all vaults + entries) |
| 24 | Secrets | Audit log count |
| 25 | FileShare | Transfer list (manifests + metadata) |
| 26 | Services | Revocation records |
| 27 | Services | Account/parental settings (reserved) |
| 28 | CVMM | Notes |
| 29 | CVMM | Recipes (with ingredients + steps) |
| 30 | CVMM | Contacts |
| 31 | CVMM | Calendar events |
| 32 | CVMM | Budget (categories + transactions) |
| 33 | CVMM | Car maintenance (vehicles + records + fuel) |
| 34 | CVMM | Fitness (workouts + body logs) |
| 35 | CVMM | Garden (plants + action logs) |
| 36 | CVMM | Home projects (with materials) |
| 37 | CVMM | Reading list (books) |
| 38 | CVMM | Todo list |
| 39 | Mathbook | Notebook cells + title |
| 40 | NetTool | Scan results + discovered hosts |
| 41 | AppLog | Shared application log entries |

## Multi-Sector Support

DiskFacts' built-in `disk-write-fact` limits content to 434 bytes
(one sector minus the 78-byte header). `AppPersist.app-write-fact`
lifts this limit by allocating `ceil((78 + len) / 512)` sectors and
writing the full entry. The fact format is identical — the same
78-byte header (SHA-256 hash, kind u16, padding, timestamp u64,
content-length u32) followed by raw text content.

`app-scan-last` correctly handles multi-sector facts during scanning
by computing sector count from the content-length field and skipping
the right number of sectors.

## Serialization Formats

**Diagram**: JSON via existing `diagram-to-json` / `diagram-from-json`.
Round-trips through the JSON foreword module. No size limit.

**Browser History**: Newline-separated, tab-delimited fields:
`address\ttitle\tvisit_tick\tvisit_count`. Deserialized with
`text-split` and `parse-decimal-full`.

**Browser Trust**: Newline-separated, tab-delimited fields:
`pub_key_hex\ttier_int\tgranted_tick\texpires`. Publisher key as
64-char hex. Tier as integer (0=Static..4=System). Expires -1 for
never.

**Secrets Vault**: Block-delimited format. Header line:
`VAULTS:active_id\tvault_count`. Each vault separated by `\n---\n`.
Vault header: `id\tname\ttype\tnext_id\tmodified\tfolders_csv`.
Entry lines: `id\ttype_int\tname\tusername\turl\tfolder\tcreated\t
modified\taccessed\taccess_count\tfavorite\tarchived`.

**FileShare**: Block-delimited transfers. Each transfer header
has tab fields for id, name, direction, bytes, started. Manifest
is inline JSON after the 6th tab.

**Revocation**: Newline-separated records. Each line:
`type_byte\tscope_text\tauthority_hex\ttimestamp\teffective\t
reason\tsignature_hex`.

## Persistence Triggers

Each app detects state changes by comparing before/after state in
the event loop:

| App | Trigger | Comparison |
|-----|---------|-----------|
| Diagram | Ctrl+S | Explicit save key |
| Browser | Navigation | History entry count changed |
| Browser | Trust grant | Trust decision count changed |
| Secrets | Vault modify | Total entry count across vaults |
| FileShare | Transfer add/remove | Transfer list length |
| Services | (no entry point) | Caller-driven |

## Files

### New modules (CL 3506: 6, CL 3519: 3)
- `codex/os/kernel/AppPersist.codex` — shared multi-sector helpers
- `apps/diagram/DiagramPersist.codex` — diagram save/load
- `apps/browser/BrowserPersist.codex` — history + trust save/load
- `apps/secrets/SecretsPersist.codex` — vault + audit save/load
- `apps/fileshare/FileSharePersist.codex` — transfer save/load
- `apps/services/ServicesPersist.codex` — revocation save/load
- `apps/cvmm/CvmmPersist.codex` — 11 mini-utility domains (JSON)
- `apps/mathbook/MathbookPersist.codex` — notebook cells save/load
- `apps/nettool/NetToolPersist.codex` — scan results + hosts save/load

### Modified modules (9 in CL 3506, 3 in CL 3519)
- `apps/diagram/opening.codex` — persistent event loop
- `apps/diagram/Diagram.codex` — diagram-save simplified
- `apps/browser/opening.codex` — persistent event loop
- `apps/browser/Browser.codex` — bs-history field + history-visit wiring
- `apps/secrets/opening.codex` — persistent event loop
- `apps/fileshare/opening.codex` — persistent event loop
- `apps/*/codex.project.json` (5 files) — added codex.os.kernel dependency

## Memory Assessment

**Heap**: Each persist operation allocates a sector-sized buffer per
sector written. For a 2KB diagram JSON, that's 5 × 512 = 2,560 bytes.
These buffers are on bivy and reclaimed when the function returns.

**Time**: One sector write takes ~1ms on the virtual IDE. A full
diagram save (5 sectors + 1 superblock) takes ~6ms. History/trust
saves are smaller (1-2 sectors typically). All saves are user-triggered
(not per-tick), so latency is imperceptible.

**Disk**: DiskFacts is append-only. Repeated saves accumulate stale
entries. `disk-compact` (AppPersist.codex) reclaims space by scanning
the log, keeping only the latest entry per kind, and rewriting the
log from sector 2. Called at app startup or on demand. Example: the
browser re-saves all history on every navigation — after 100
navigations with 50-entry history (~2.5KB each), the log holds 500
sectors (250KB) of duplicate data. After compaction: 5 sectors.

Compaction rewrites the log in-place and updates the superblock
atomically (dual-superblock). The window between rewrite and
superblock update is a crash risk — mitigated by only compacting at
safe points (startup, explicit save).
