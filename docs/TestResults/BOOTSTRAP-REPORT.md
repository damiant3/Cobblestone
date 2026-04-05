# Bootstrap Verification Report

**Date:** 2026-04-04
**Compiler version:** Codex self-hosted compiler (33 source files, 1161 definitions)
**Result:** Both fixed points proven — C# bootstrap and bare-metal pingpong

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

Codex proves this fixed point two independent ways:

1. **C# Bootstrap** — the self-hosted compiler emits C# code, which is compiled
   by .NET and run again. Three stages, comparing stage 1 and stage 3.

2. **Bare-Metal Pingpong** — the self-hosted compiler is compiled to a bare-metal
   x86-64 ELF (no OS, no runtime, no libc), run under QEMU, and its output is
   fed back through the same binary. Two stages, byte-identical comparison.

---

## Files in This Directory

| File | Size | Description |
|------|------|-------------|
| `source.codex` | 463 KB | Combined Codex source — input to both bootstraps |
| `bootstrap1-stage0.cs` | 824 KB | Reference compiler (hand-written C#) compiles source → C# |
| `bootstrap1-stage1.cs` | 673 KB | Self-hosted compiler (from stage 0) compiles source → C# |
| `bootstrap1-stage3.cs` | 673 KB | Self-hosted compiler (from stage 1) compiles source → C# |
| `bootstrap2-stage1.codex` | 418 KB | Bare-metal ELF compiles source → Codex |
| `bootstrap2-stage2.codex` | 418 KB | Same ELF compiles stage 1 output → Codex |

**Fixed-point results:**
- `bootstrap1-stage1.cs` and `bootstrap1-stage3.cs` are identical (673,114 bytes)
- `bootstrap2-stage1.codex` and `bootstrap2-stage2.codex` are identical (417,738 bytes,
  excluding STACK/HEAP diagnostic lines which report runtime memory usage)

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
✅ FIXED POINT PROVEN: Stage 1 = Stage 3 (673,114 chars identical)
```

**Full verification (C# bootstrap + bare-metal pingpong):**

```bash
wsl bash tools/pingpong.sh
```

Expected output:
```
✅ FIXED POINT PROVEN: Stage 1 = Stage 3 (673,114 chars identical)
PASS: stage1 === stage2 (byte-identical)
```

### Method 2: Manual step-by-step

This method lets you inspect every intermediate artifact.

**Step 1: Build the reference compiler**

```bash
dotnet build Codex.sln -c Release
```

**Step 2: Compile Codex source to C# (stage 0)**

```bash
dotnet run --project tools/Codex.Cli -c Release -- \
    build Codex.Codex --target cs --output-dir build-output
```

This produces `build-output/Codex.Codex.cs` — the reference compiler's C#
output. This is `bootstrap1-stage0.cs`.

**Step 3: Prepare the self-hosted compiler**

```bash
cp build-output/Codex.Codex.cs tools/Codex.Bootstrap/CodexLib.g.cs
sed -i '/^Codex_Codex_Codex\.main();$/d' tools/Codex.Bootstrap/CodexLib.g.cs
dotnet build tools/Codex.Bootstrap/Codex.Bootstrap.csproj -c Release
```

**Step 4: Run stage 1 (self-hosted compiler compiles source)**

```bash
dotnet run --project tools/Codex.Bootstrap -c Release -- \
    Codex.Codex build-output/stage1-output.cs
```

This produces `bootstrap1-stage1.cs`.

**Step 5: Run stage 3 (compile again from stage 1 output)**

```bash
cp build-output/stage1-output.cs tools/Codex.Bootstrap/CodexLib.g.cs
sed -i '/^Codex_Codex_Codex\.main();$/d' tools/Codex.Bootstrap/CodexLib.g.cs
dotnet build tools/Codex.Bootstrap/Codex.Bootstrap.csproj -c Release
dotnet run --project tools/Codex.Bootstrap -c Release -- \
    Codex.Codex build-output/stage3-output.cs
```

This produces `bootstrap1-stage3.cs`.

**Step 6: Verify fixed point**

```bash
diff build-output/stage1-output.cs build-output/stage3-output.cs
```

If the diff is empty, the fixed point is proven.

**Step 7: Bare-metal verification (optional, requires WSL + QEMU)**

```bash
# Build the bare-metal ELF
dotnet run --project tools/Codex.Cli -c Release -- \
    build Codex.Codex --target x86-64-bare --output-dir build-output

# Dump the combined source
dotnet run --project tools/Codex.Bootstrap -c Release -- --dump-source

# Stage 1: feed source through QEMU
# Stage 2: feed stage 1 output through same ELF
# (see tools/pingpong.sh for the full QEMU invocation)
wsl bash tools/pingpong.sh
```

---

## What the Results Mean

### Stage 0 vs Stage 1 (824 KB → 673 KB)

Stage 0 is produced by the **reference compiler** — hand-written C# code in `src/`.
Stage 1 is produced by the **self-hosted compiler** — Codex code in `Codex.Codex/`,
compiled to C# by the reference compiler, then executed.

The size difference (824 KB vs 673 KB) is because the two compilers are different
implementations. The reference compiler is hand-written C#; the self-hosted
compiler is generated from Codex source. They produce semantically equivalent
but textually different C# output.

### Stage 1 vs Stage 3 (identical)

Stage 1 and stage 3 are both produced by the self-hosted compiler, but compiled
by different versions of itself. Stage 1 was compiled by the reference compiler.
Stage 3 was compiled by stage 1's output. The fact that they are identical proves
the self-hosted compiler is **self-consistent** — compiling itself produces the
same compiler, which produces the same output.

This is the fixed point. It means the self-hosted compiler does not depend on
which compiler compiled it. Its behavior is determined entirely by its source code.

### Bare-metal stage 1 vs stage 2 (identical)

The bare-metal test goes further. The compiler is compiled to a raw x86-64 ELF
binary with no operating system — no libc, no malloc, no syscalls. It runs on
QEMU bare metal, reading source from a serial pipe and writing output to the
same pipe.

Stage 1 feeds the source through this binary. Stage 2 feeds stage 1's output
through the same binary. Byte-identical output proves the compiler works
correctly on bare hardware with its own memory allocator, its own string
handling, and its own I/O — no borrowed substrate.

### The Trust Chain

1. **You read the reference compiler** (`src/`) — it is hand-written, auditable C#
2. **The reference compiler compiles the Codex source** → stage 0
3. **Stage 0 compiles the same source** → stage 1
4. **Stage 1 compiles the same source** → stage 3
5. **Stage 1 === stage 3** — the self-hosted compiler is self-consistent
6. **The bare-metal binary proves the same** — no OS in the loop

The only trust assumption is step 1: that the reference compiler does what it
claims. After that, the math takes over. The fixed point is the proof.

---

## Performance Summary (bare-metal)

| Stage | Output | Time | Stack HWM | Heap HWM |
|-------|--------|------|-----------|----------|
| Stage 1 | 417,738 bytes | 29s | 2,068,976 B | 159,347,592 B |
| Stage 2 | 417,738 bytes | 26s | 2,097,152 B | 156,720,672 B |

Both stages run on QEMU x86-64 bare metal (TCG, no KVM). The ELF binary is
701,600 bytes. Source input is 462,728 bytes.
