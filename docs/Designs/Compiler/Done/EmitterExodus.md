# The Emitter Exodus

*In which the container format writers leave the compiler and find
their own homes — but the compiler keeps its soul.*

## The Shire

The compiler today is a single monolith: parser, type checker, IR
lowering, lambda lifting, x86-64 codegen, and *every output format
writer* all live under one roof in `codex/compiler/`. The ELF/DWARF
writer, the PE writer, the GPT/FAT disk image writer — they all share
the compiler's heap, its phase allocator, its build cycle. A change
to how DWARF line tables are encoded requires a full seed rebuild.

This was necessary during bootstrap. Everything had to be one binary
because there was nothing else to run. But the cord is cut now. The
compiler is a fixed point of itself. The format writers don't need
to live in the Shire anymore.

## The Fixed-Point Principle

The compiler's defining property: install the artifact, give it its
source, it produces a byte-identical copy of itself. One binary, one
input, one output. Self-sustaining.

This constrains what can leave. The x86-64 codegen and CDX writer are
not packaging — they ARE the compiler's output. Extracting them would
mean the compiler alone can no longer reproduce itself. You would need
the compiler plus the x86 plug plus the CDX plug, and the fixed-point
property would require all three to be coordinated. The simplicity and
trust of "one artifact, one source, one copy" would be lost.

The container format writers (ELF, PE, GPT, FAT) are different. They
are post-processors that convert CDX into other formats. The compiler
produces CDX. CDX is the fixed point. Everything else is derived.

## What Stays (Forever)

The compiler core: parser, name resolver, type checker, type inference,
unifier, IR lowering, lambda lifting, x86-64 codegen, CDX writer, text
round-trip emitter, and IR text emitter. These are the compiler. They
produce CDX, which is the self-sustaining artifact.

The `.codex` text emitter also stays — it is part of the fixed-point
proof (text round-trip: source → TEXT → TEXT must be identical).

## What Left

The container format writers are pure format converters. They receive
machine code bytes (extracted from CDX) and produce their respective
output formats. They don't need the compiler's type system, IR, or
phase allocator. They just need bytes and metadata.

| Writer | Plug | Input | Output | Status |
|---|---|---|---|---|
| ELF + DWARF | `codex/plugs/elf/` | x86 code+data+funcs | `.elf` binary | Done — verified byte-identical, selfhost compiles |
| PE | `codex/plugs/pe/` | CDX bytes | PE32+ UEFI binary | Done — verified zero code diffs |
| GPT + FAT32 + FAT16 | `codex/plugs/img/` | PE + CDX bytes | GPT disk image | Done — compiles, runtime test pending |

## The Architecture

```
Compiler → CDX (self-sustaining, fixed point, the root of trust)
            |
     CDX bytes extracted from header
            |
     ┌──────┼──────┐
     ↓      ↓      ↓
  ELF plug  PE plug  IMG plug
     ↓      ↓      ↓
   .elf    .efi   .img
```

The compiler produces CDX. Container plugs convert CDX to other
formats. The x86 machine code never leaves the compiler — plugs
receive it as bytes extracted from the CDX artifact, not from a
separate codegen step.

This means:
- The compiler is self-sustaining without any plugs
- Plugs can be updated, rebuilt, or replaced without touching the seed
- Format bugs don't require seed rebuilds
- The fixed-point property is preserved unconditionally

## Wire Protocols

### Machine-Code Wire Protocol (tag=2, ELF plug)

Binary format for x86 code + metadata extracted from CDX:

```
[4 bytes] code-len   (LE)
[4 bytes] data-len   (LE)
[4 bytes] func-count (LE)
[code-len bytes] code
[data-len bytes] data
For each function:
  [2 bytes] name-len (LE)
  [name-len bytes] name (CCE)
  [4 bytes] offset  (LE)
```

### CDX-to-PE Protocol (tag=4, PE plug)

```
[1 byte]  mode (0=kernel, 1=app)
[4 bytes] heap-pages (LE, app mode only)
[rest]    CDX bytes
```

### IMG Protocol (tag=5, IMG plug)

```
[1 byte]  fs-type (0=FAT32, 1=FAT16)
[4 bytes] total-sectors (LE)
[4 bytes] PE size (LE)
[4 bytes] CDX size (LE)
[4 bytes] source size (LE, 0 if none)
[rest]    PE bytes, CDX bytes, source bytes
```

## Shared Infrastructure

- `codex/plugs/common/ByteHelpers.codex` — LE byte encoding/decoding
- `codex/plugs/common/PlugChain.codex` — wire protocol types and parsers
- `codex/plugs/common/plug-build-lib.ps1` — shared build script (foreword
  resolution, VM launch, serial compile protocol)
- `build/run-plug-chain.ps1` — host-side chain orchestrator

## Foreword Rules

Emitter plugs use Foreword libraries, never compiler-internal Core
chapters. The compiler Core depends on Phase Allocator which is not
available in plug CDXs.

## History

### CL 2029 (2026-05-23): ELF/DWARF plug + chaining infrastructure

First container writer extracted. Plug produces byte-identical ELF
with 2614 DWARF function entries. Selfhost ELF compiles the full
compiler source to a CDX that is byte-identical to the CDX seed's
output (SHA-256 match).

Also fixed: NE2000 word-DMA bug (odd-frame last byte truncation),
TCP transport O(n²) list concat, codex-vm ELF loading support,
ip-payload IP total-length fix.

### CL 2042 (2026-05-23): PE plug + IMG plug

PE and IMG container writers extracted. PE plug verified zero code
diffs against compiler's internal PE output. IMG plug bundles GPT,
FAT32, and FAT16 writers. Common build library extracted to reduce
duplication. codex-vm e_phoff type corrected (unsigned short →
unsigned int for ELF32).
