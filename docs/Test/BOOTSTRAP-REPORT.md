# Bootstrap Verification Report

**Date:** 2026-04-07
**Compiler version:** Codex self-hosted compiler (33 source files, 1292 definitions)
**Result:** All three proofs green — C# bootstrap, bare-metal pingpong, semantic equivalence

---

## What Is Bootstrapping?

A self-hosting compiler is one that can compile its own source code. Bootstrapping
is the process of proving this works — that the compiler, when compiled by itself,
produces an identical copy of itself.

The test is called a **fixed-point proof**: if you compile the source with compiler A
to get compiler B, then compile the same source with compiler B to get compiler C,
and B equals C byte-for-byte, the compiler has reached a fixed point. It is
self-consistent. No external authority is needed to verify it — the proof is in
the output.

Codex proves this fixed point three independent ways:

1. **C# Bootstrap** — the self-hosted compiler emits C# code, which is compiled
   by .NET and run again. Three stages, comparing stage 1 and stage 3.

2. **Bare-Metal Pingpong** — the self-hosted compiler is compiled to a bare-metal
   x86-64 ELF (no OS, no runtime, no libc), run under QEMU, and its output is
   fed back through the same binary. Two stages, byte-identical comparison.

3. **Semantic Equivalence** — the source (stage 0) is compared against the
   bare-metal output (stage 1) definition by definition, proving the compiler
   reproduces its own source semantically. 1292/1292 body match, no normalizations
   hiding differences.

---

## Files in This Directory

| File | Size | Description |
|------|------|-------------|
| `source.codex` | 462 KB | Combined Codex source — input to both bootstraps |
| `bootstrap1-stage0.cs` | 833 KB | Reference compiler (hand-written C#) compiles source → C# |
| `bootstrap1-stage1.cs` | 681 KB | Self-hosted compiler (from stage 0) compiles source → C# |
| `bootstrap1-stage3.cs` | 681 KB | Self-hosted compiler (from stage 1) compiles source → C# |
| `bootstrap2-stage1.codex` | 418 KB | Bare-metal ELF compiles source → Codex |
| `bootstrap2-stage2.codex` | 418 KB | Same ELF compiles stage 1 output → Codex |

**Fixed-point results:**
- `bootstrap1-stage1.cs` and `bootstrap1-stage3.cs` are identical (681,004 bytes)
- `bootstrap2-stage1.codex` and `bootstrap2-stage2.codex` are identical (418,032 bytes,
  excluding STACK/HEAP diagnostic lines which report runtime memory usage)
- `source.codex` and `bootstrap2-stage1.codex` match semantically (1292/1292 definitions)

---

## Semantic Equivalence (stage0==stage1)

The `codex sem-equiv` tool compares source definitions against bare-metal output
honestly — no hidden normalizations. The tool documents what it does:

```
Stage0 structure (recognized, not compared):
  33 chapters, 239 sections, 259 prose lines, 4 cites

Comparison method:
  Applied: name demangling (stage1), whitespace collapse (both), type-var alpha-norm (sigs)
  Not applied: brace escapes, parenthesization — reported as-is

Bodies: 1292 match, 0 differ
Verdict: PASS
```

Chapters, sections, and prose are structural metadata that the emitter does not
yet produce. They are recognized and counted but not compared. Name demangling
reverses the compiler's cross-chapter name mangling. Whitespace collapse is
applied at comparison time (not stored). Everything else — brace escapes,
parenthesization — is compared as-is.

---

## How to Reproduce

### Prerequisites

- .NET 8 SDK
- WSL with QEMU (`/usr/bin/qemu-system-x86_64`) for bare-metal tests
- Git clone of this repository

### Method 1: Automated (recommended)

**C# bootstrap only:**

```bash
dotnet build tools/Codex.Cli/Codex.Cli.csproj -c Release
dotnet run --project tools/Codex.Cli -c Release -- bootstrap Codex.Codex
```

Expected output:
```
✅ FIXED POINT PROVEN: Stage 1 = Stage 3 (681,004 chars identical)
```

**Full verification (C# bootstrap + bare-metal pingpong):**

```bash
wsl bash tools/pingpong.sh
```

Expected output:
```
✅ FIXED POINT PROVEN: Stage 1 = Stage 3 (681,004 chars identical)
PASS: stage1 === stage2 (byte-identical)
```

**Semantic equivalence:**

```bash
dotnet run --project tools/Codex.Cli -- sem-equiv docs/Test/source.codex docs/Test/bootstrap2-stage1.codex
```

Expected output: `Verdict: PASS` with 0 body mismatches.

---

## What the Results Mean

### Stage 0 vs Stage 1 (833 KB → 681 KB)

Stage 0 is produced by the **reference compiler** — hand-written C# code in `src/`.
Stage 1 is produced by the **self-hosted compiler** — Codex code in `Codex.Codex/`,
compiled to C# by the reference compiler, then executed.

The size difference (833 KB vs 681 KB) is because the two compilers are different
implementations. The reference compiler is hand-written C#; the self-hosted
compiler is generated from Codex source. They produce semantically equivalent
but textually different C# output.

### Stage 1 = Stage 3 (identical)

Stage 1 and stage 3 are both produced by the self-hosted compiler, but compiled
by different versions of itself. Stage 1 was compiled by the reference compiler.
Stage 3 was compiled by stage 1's output. The fact that they are identical proves
the self-hosted compiler is **self-consistent** — compiling itself produces the
same compiler, which produces the same output.

This is the fixed point. It means the self-hosted compiler does not depend on
which compiler compiled it. Its behavior is determined entirely by its source code.

### Bare-metal stage1 === stage2 (byte-identical)

The bare-metal test goes further. The compiler is compiled to a raw x86-64 ELF
binary with no operating system — no libc, no malloc, no syscalls. It runs on
QEMU bare metal, reading source from a serial pipe and writing output to the
same pipe.

Stage 1 feeds the source through this binary. Stage 2 feeds stage 1's output
through the same binary. Byte-identical output proves the compiler works
correctly on bare hardware with its own memory allocator, its own string
handling, and its own I/O — no borrowed substrate.

### Source == Stage 1 (semantic equivalence)

The semantic equivalence proof closes the loop. It shows that the bare-metal
compiler's output reproduces its own source — every definition, every body,
every type signature (modulo cross-chapter name mangling which is reversed
during comparison). No normalizations hide differences.

### The Trust Chain

1. **You read the reference compiler** (`src/`) — it is hand-written, auditable C#
2. **The reference compiler compiles the Codex source** → stage 0
3. **Stage 0 compiles the same source** → stage 1
4. **Stage 1 compiles the same source** → stage 3
5. **Stage 1 = Stage 3** — the self-hosted compiler is self-consistent
6. **The bare-metal binary proves the same** — no OS in the loop
7. **Source == stage 1 semantically** — the compiler reproduces its source

The only trust assumption is step 1: that the reference compiler does what it
claims. After that, the math takes over. The fixed point is the proof.

---

## Performance Summary (bare-metal)

| Stage | Output | Time | Stack HWM | Heap HWM |
|-------|--------|------|-----------|----------|
| Stage 1 | 418,061 bytes | 175s | 2,137,600 B | 155,241,400 B |
| Stage 2 | 418,061 bytes | 174s | 1,836,784 B | 152,077,272 B |

Both stages run on QEMU x86-64 bare metal with KVM. The ELF binary is
750,936 bytes. Source input is 461,935 bytes (33 files, 1292 definitions).
