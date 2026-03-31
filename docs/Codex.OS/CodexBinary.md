# Codex Binary Format — Trust at the Binary Boundary

**Date**: 2026-03-31
**Status**: Design
**Depends on**: Second Bootstrap, Capability System, Crypto Primitives (TBD)
**Prior art**: ELF (System V, 1988), `docs/Compiler/SECOND-BOOTSTRAP.md`

---

## The Problem With ELF

ELF is a loading instruction sheet from 1988. It tells the OS where to put
bytes in memory and where to start executing. That's all it does. It does
not tell you:

- What the binary will do when it runs
- Who wrote it
- Whether it's been tampered with
- What resources it needs
- Whether it's safe

Everything the OS knows about an ELF binary is metadata bolted on after
the fact: file permissions, SELinux labels, AppArmor profiles, code signing
certificates. The binary itself carries no trust information.

### Specific Flaws

| Flaw | Consequence |
|------|-------------|
| **No type information** | The loader has no idea what capabilities the program needs. It loads untyped bytes and hopes. |
| **No signing** | Anyone can produce an ELF. No author identity. `chmod +x` and it runs. |
| **No capability manifest** | The binary can do whatever the process permissions allow. No compile-time verification of what it claims to need. |
| **Dynamic linking (GOT/PLT)** | Global Offset Table and Procedure Linkage Table are writable function pointers. GOT overwrite is a classic exploitation technique. |
| **RWX pages allowed** | Self-modifying code, JIT regions, `mprotect` to flip permissions at runtime. The format permits executing data and writing code. |
| **Optional section headers** | Can be stripped, losing all metadata. The binary still runs. |
| **No dependency verification** | Shared libraries are resolved by name at runtime. `LD_PRELOAD` injects arbitrary code. The format has no concept of verified dependencies. |
| **Mitigations are bolt-ons** | RELRO, stack canaries, ASLR, NX, PIE — all patching a format that doesn't protect itself. Each one is optional and can be disabled. |

These flaws are not bugs. ELF was designed for a world where the OS
enforces security through process isolation and file permissions. The binary
is assumed to be trusted once it's loaded. Codex.OS inverts this: **the
binary proves it is safe before it loads.**

---

## Design Principles

1. **The binary carries its own trust record.** Author signature, capability
   manifest, proof hashes, trust threshold — all embedded in the binary.
   The verifier reads them before loading a single byte of code.

2. **No dynamic linking.** All dependencies are resolved at compile time,
   by content hash. There is no GOT, no PLT, no `LD_PRELOAD`, no runtime
   symbol resolution. The binary is complete and self-contained.

3. **No self-modifying code.** Code pages are read-execute (RX). Data pages
   are read-write (RW). There is no RWX. The format does not support
   `mprotect`-style permission changes. JIT compilation is an effect that
   requires the `[CodeGen]` capability — and most programs don't have it.

4. **Verification before loading.** The Codex.OS loader verifies the
   content hash, checks the signature, evaluates the capability manifest
   against the current policy, and optionally verifies carried proofs —
   all before mapping any code into memory. A binary that fails any check
   is never loaded.

5. **Content-addressed.** The binary's identity is its SHA-256 hash. Two
   identical compilations produce the same hash. A different hash means
   different code. There is no versioning — there are different binaries,
   identified by their content.

---

## The Format

### Header

```
Offset  Size  Field              Description
------  ----  -----              -----------
0x00    4     magic              "CDX1" (0x43 0x44 0x58 0x31)
0x04    2     format_version     Format version (currently 1)
0x06    2     flags              Bit flags (see below)
0x08    32    content_hash       SHA-256 of everything from capabilities_offset
                                 to end of file (excludes header through signature)
0x28    32    author_key         Ed25519 public key of the author
0x48    64    signature          Ed25519 signature over content_hash
0x88    8     capabilities_offset  File offset to capability table
0x90    8     capabilities_size    Size of capability table in bytes
0x98    8     proofs_offset      File offset to proof hash table
0xA0    8     proofs_size        Size of proof hash table in bytes
0xA8    8     text_offset        File offset to code section
0xB0    8     text_size          Size of code section in bytes
0xB8    8     rodata_offset      File offset to read-only data section
0xC0    8     rodata_size        Size of read-only data section in bytes
0xC8    8     entry_point        Offset into text section (byte offset from text_offset)
0xD0    4     stack_size         Required stack size in bytes
0xD4    4     heap_size          Required heap size in bytes
0xD8    2     trust_threshold    Minimum trust score (fixed-point, 0-10000 = 0.0-1.0)
0xDA    2     reserved           Must be zero
0xDC    4     fact_hash_count    Number of dependency fact hashes
0xE0    varies  fact_hashes      Array of SHA-256 hashes (32 bytes each) — content-
                                 addressed dependencies this binary was compiled against
```

**Total fixed header**: 0xE0 (224 bytes) + fact_hashes.

**Flags**:

| Bit | Name | Meaning |
|-----|------|---------|
| 0 | BARE_METAL | Binary targets bare metal (no OS syscalls) |
| 1 | NEEDS_HEAP | Binary requires heap allocation |
| 2 | NEEDS_STACK_GUARD | Binary requires stack overflow detection |
| 3 | HAS_PROOFS | Proof hash table is present and non-empty |
| 4-15 | Reserved | Must be zero |

### Capability Table

The capability table lists every effect the binary requires. This is the
manifest that the verifier checks before loading.

```
Entry format:
  2 bytes   capability_id     Numeric ID (from the capability registry)
  2 bytes   direction         0=Read, 1=Write, 2=ReadWrite
  4 bytes   scope_length      Length of scope string (0 = no scope restriction)
  N bytes   scope             Scope string (path prefix, host, etc.) — CCE encoded
  8 bytes   max_duration      Maximum lease duration in ticks (0 = unlimited)
```

Example capability table for a program that reads config files and writes
to the console:

```
[FileSystem.Read, Read, 8, "/config/", 0]
[Console, Write, 0, (none), 0]
```

The verifier reads this table and checks it against the effective policy
for the installing user. If the policy denies any requested capability,
the binary is rejected before loading. The user sees:

```
This program requests:
  - Read files under /config/
  - Write to console

Your policy allows:
  - Read files under /config/  ✓
  - Write to console           ✓

Load? [the agent decides based on policy, or asks the user]
```

### Proof Hash Table

Optional. Lists SHA-256 hashes of proofs that the binary carries. The
proofs themselves are facts in the repository — the binary carries only
the hashes. The verifier can:

1. **Skip verification**: Trust the author's signature (fast path).
2. **Check proofs**: Retrieve the proof facts by hash from the local
   fact store or a trusted peer, and verify them (thorough path).
3. **Require proofs**: Refuse to load unless all listed proofs are
   verified (paranoid path — appropriate for safety-critical binaries).

```
Entry format:
  32 bytes  proof_hash        SHA-256 of a proof fact
  2 bytes   proof_kind        0=Termination, 1=MemorySafety, 2=CapabilityCompliance,
                              3=Custom
  2 bytes   reserved          Must be zero
```

### Code Section (text)

Machine code. Read-execute only. No relocations — all addresses are
resolved at compile time relative to the load address. The load address
is determined by the loader, but the code is position-independent (all
internal references are RIP-relative or use a base register).

On bare metal, the load address is fixed (0x100000 for Codex.OS kernel-
loaded binaries, or as specified by the kernel's process loader). The
code section is mapped RX. Any attempt to write to code pages is a fault.

### Read-Only Data Section (rodata)

String literals, lookup tables, constant data. Read-only. Mapped with
no execute permission. Attempts to execute rodata or write to rodata
are faults.

### What's NOT in the Binary

- **No writable data section.** Codex programs are functional — mutable
  state lives in the heap, managed by the runtime's region allocator.
  There is no `.data` or `.bss` section with pre-initialized mutable
  globals.

- **No dynamic linking tables.** No GOT, no PLT, no `.dynamic`, no
  `.interp`. The binary is fully linked at compile time. Dependencies
  are resolved by content hash, not by symbol name.

- **No debug information (in the binary).** Debug information is a
  separate fact in the repository, linked by content hash. The binary
  is a deployment artifact — debug info is a development artifact.
  Keeping them separate means the binary hash doesn't change when you
  update debug info.

- **No section headers.** ELF section headers are metadata for the
  linker and debugger. They're optional and strippable. CDX has no
  linker (no dynamic linking) and puts debug info in the repository.
  Section headers serve no purpose.

---

## The Loading Sequence

When Codex.OS loads a binary:

```
1. Read header
   - Check magic ("CDX1")
   - Check format_version (must be supported)
   - Check flags (must not have unknown bits set)

2. Verify integrity
   - Compute SHA-256 of everything from capabilities_offset to EOF
   - Compare with content_hash in header
   - If mismatch: reject (corrupted or tampered)

3. Verify author
   - Look up author_key in the trust lattice
   - Verify signature over content_hash using author_key
   - If signature invalid: reject (forged or corrupted)
   - If author's trust score < trust_threshold: reject (untrusted)

4. Evaluate capabilities
   - Read capability table
   - Check each requested capability against the effective policy
     for the installing user/agent
   - If any capability is denied by policy: reject (insufficient permission)

5. Verify proofs (optional, based on policy)
   - Read proof hash table
   - Retrieve proof facts from local store or trusted peers
   - Verify each proof
   - If any proof fails: reject (unverified claims)

6. Allocate resources
   - Allocate stack (stack_size from header)
   - Allocate heap (heap_size from header)
   - Map text section RX at assigned load address
   - Map rodata section R at assigned address

7. Grant capabilities
   - Set capability bits in process table based on capability table
   - Start lease timers based on max_duration fields
   - Record grant facts in forensic chain

8. Execute
   - Jump to entry_point
   - Process runs with exactly the capabilities listed in the table
   - No more, no less
```

Steps 1-5 execute before any code is mapped into memory. A malicious
binary is rejected at step 2, 3, 4, or 5 — it never gets to execute
a single instruction.

---

## ELF Compatibility

ELF is not going away. We need it for:

- **QEMU development**: QEMU loads ELF binaries. During development, we
  wrap CDX binaries in a minimal ELF container (the current 113-line
  ELF writer).
- **Linux user-mode**: Running Codex programs on Linux requires ELF.
  The Linux target produces an ELF binary with a CDX manifest embedded
  as a custom ELF note section (type `NT_CODEX_MANIFEST`). The Codex
  metadata travels with the ELF but doesn't affect Linux's loader.

The compatibility path:

| Target | Binary format | Trust verification |
|--------|---------------|-------------------|
| Codex.OS bare metal | CDX native | Full (steps 1-8) |
| Codex.OS process | CDX native | Full (steps 1-8) |
| Linux user-mode | ELF with CDX note | CDX note verified if present |
| QEMU development | Minimal ELF wrapper | None (development only) |

On Codex.OS, only CDX binaries load. On Linux, ELF with optional CDX
metadata. The CDX format is the primary; ELF is the compatibility shim.

---

## Comparison

| Property | ELF | CDX |
|----------|-----|-----|
| Year | 1988 | 2026 |
| Trust model | None (OS enforces via process isolation) | Built-in (signature, capabilities, proofs) |
| Identity | None | Author public key |
| Integrity | None (optional codesign outside format) | SHA-256 content hash |
| Signing | Not in format | Ed25519 signature in header |
| Capabilities | Not in format (file permissions, SELinux) | Typed capability table |
| Proofs | Not in format | Proof hash table |
| Dynamic linking | GOT/PLT, `LD_PRELOAD` | None. Fully resolved at compile time |
| Self-modifying code | Allowed (RWX pages) | Forbidden (no RWX, no mprotect) |
| Section headers | Optional, strippable | No sections. Fixed layout |
| Dependencies | Shared library names, resolved at runtime | Content hashes, resolved at compile time |
| Verification | After loading (sandbox, scan) | Before loading (reject or accept) |

---

## Connection to the Second Bootstrap

The Second Bootstrap (`docs/Compiler/SECOND-BOOTSTRAP.md`) Phase 2 calls
for porting the ELF writer to Codex. This should produce TWO writers:

1. **CDX writer**: The native Codex binary format. This is what the
   compiler produces for Codex.OS targets. ~200 lines of Codex.
2. **ELF shim writer**: Minimal ELF wrapper for QEMU compatibility.
   ~120 lines of Codex. Used only during development.

The CDX writer replaces the ELF writer as the primary output. The self-
compilation fixed point (MM4) should be achieved using CDX binaries, with
ELF used only as a QEMU boot shim that wraps the CDX binary for loading.

---

## Implementation Order

| Step | What | Effort | Depends on |
|------|------|--------|------------|
| 1 | Define the CDX header format (this document) | Done | — |
| 2 | CDX writer in Codex (Second Bootstrap Phase 2) | Small | Phase 1 of Second Bootstrap |
| 3 | CDX loader in bare-metal kernel | Medium | Step 2 + kernel process loader |
| 4 | Capability table evaluation in loader | Medium | Step 3 + Policy Contract |
| 5 | Signature verification in loader | Medium | Step 3 + Crypto primitives |
| 6 | Proof verification in loader (optional path) | Medium | Step 5 + Fact store on disk |
| 7 | ELF-with-CDX-note writer for Linux target | Small | Step 2 |

Steps 2-3 are part of the Second Bootstrap and should be built during
that effort. Steps 4-6 are part of the Codex.OS verifier — the component
that THE-LAST-PEAK calls "the hardest sub-problem." Step 7 is a convenience
for running Codex programs on Linux with metadata.

---

## Open Questions

1. **Load address**: Fixed or position-independent? Fixed is simpler
   (no relocation) but limits the number of concurrent processes to
   the number of address slots. Position-independent requires a base
   register or RIP-relative addressing for all data references.

2. **Compression**: Should the text and rodata sections be compressed
   in the binary and decompressed at load time? Reduces storage (good
   for floppy disk target) but adds decompression to the load path.

3. **Incremental verification**: Can the loader verify the binary
   incrementally (header first, then capabilities, then code) to fail
   fast? Or must it read the entire binary to compute the content hash?
   Incremental verification requires a Merkle tree structure instead of
   a flat SHA-256.

4. **Multi-architecture**: Should a single CDX binary carry code for
   multiple architectures (fat binary)? Or should each architecture
   produce a separate CDX binary with the same content hash for the
   capability/proof sections? Separate binaries are simpler and follow
   the "one purpose per artifact" principle.
