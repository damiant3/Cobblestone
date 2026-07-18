# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) and any other AI agents when working with code in this repository.

## Overview

This is an optimizing compiler for Anthropic's original performance engineering take-home challenge. The task is to optimize a kernel running on a simulated VLIW SIMD virtual machine. The goal is to minimize cycle count for a tree traversal + hash computation workload.

The upstream challenge code lives in `original_performance_takehome/` (an unmodified copy of [anthropics/original_performance_takehome](https://github.com/anthropics/original_performance_takehome)). This project provides an optimizing compiler that targets that VM.

## ⚠️ Essential Reading: VLIW ISA Documentation

**Consult `docs/VLIW_ISA.md`** to understand the target architecture. This document is critical for:

- Understanding the VLIW execution model and instruction bundling constraints
- Knowing available instructions, their latencies, and engine slot assignments
- Identifying optimization opportunities (e.g., instruction parallelism, vector operations)
- Understanding memory access patterns and scratch register usage
- Making informed decisions about performance trade-offs

**Before implementing any optimization, always re-read the relevant sections of `docs/VLIW_ISA.md`** to ensure your approach aligns with the hardware capabilities and constraints.

## Commands

```bash
# Run the tree hash program (compile + execute)
python programs/tree_hash.py

# Run with trace output
python programs/tree_hash.py --trace

# Run with compiler diagnostics
python programs/tree_hash.py --print-after-all
python programs/tree_hash.py --print-metrics

# Run submission tests (validates correctness and shows cycle count)
python tests/submission_tests.py

# Run all compiler tests
python3 -m pytest compiler/tests/ -v

# View trace in Perfetto (run after generating trace)
python original_performance_takehome/watch_trace.py
# Then open http://localhost:8000 and click "Open Perfetto"
```

## Architecture

### Project Structure & Module Organization
- `original_performance_takehome/`: Unmodified upstream challenge code (VM simulator, reference kernel, tests). **Do not modify any files in this directory.**
- `vm/`: Thin wrapper re-exporting `original_performance_takehome.problem` for convenient imports.
- `compiler/`: IR definitions, passes, lowering, and codegen for the VLIW SIMD target. Exports `compile()` and `execute()` APIs.
- `programs/`: Program implementations using the IR compiler (e.g., `programs/tree_hash.py`).
- `tests/`: Submission correctness and regression tests (pytest-based).
- `compiler/tests/`: Compiler-specific tests.
- `docs/`: Architecture reference (`docs/VLIW_ISA.md` is essential).

### Virtual Machine (original_performance_takehome/problem.py)

A VLIW SIMD simulator with:
- **Engines**: Execute multiple slots per cycle in parallel
  - `alu` (12 slots): Scalar arithmetic (+, -, *, //, %, ^, &, |, <<, >>, <, ==)
  - `valu` (6 slots): Vector operations on VLEN=8 lanes (vbroadcast, multiply_add, vector ops)
  - `load` (2 slots): Memory reads (load, vload, const, load_offset)
  - `store` (2 slots): Memory writes (store, vstore)
  - `flow` (1 slot): Control flow, select, vselect
  - `debug` (64 slots): Debugging only, doesn't count toward cycles

- **Memory model**: All slots in a bundle read pre-bundle state; writes commit at bundle end
- **Scratch**: Per-core register file (1536 words), used like registers/cache
- **Memory**: Shared flat array of 32-bit words

### Compiler API (compiler/)

The compiler package exports two main APIs:
- `compile(hir, **kwargs)`: Compile HIR program to VLIW instructions
- `execute(instrs, mem, **kwargs)`: Execute VLIW instructions on the VM

### Program (programs/tree_hash.py)

`KernelBuilder.build_kernel()` generates optimized instructions for the tree hash algorithm:
1. Load batch of indices and values from memory
2. For each round and batch element:
   - Look up node value at current index
   - Compute `val = myhash(val ^ node_val)` (6-stage hash)
   - Compute next index: `2*idx + (1 if val%2==0 else 2)`
   - Wrap index if out of bounds
   - Store updated values back

The baseline implementation is fully unrolled and scalar (~147k cycles). Optimization strategies include VLIW packing, vectorization (VLEN=8), loop transformations, and exploiting instruction-level parallelism.

### Memory Layout (mem image)

```
Header (words 0-6): rounds, n_nodes, batch_size, forest_height, forest_values_p, inp_indices_p, inp_values_p
Arrays: forest_values[], inp_indices[], inp_values[]
```

## Testing

### Test Commands

```bash
# Run all compiler tests
python3 -m pytest compiler/tests/ -v

# Run submission tests (validates correctness and shows cycle count)
python3 tests/submission_tests.py

# Run specific test file
python3 -m pytest compiler/tests/test_regressions.py -v

# Run specific test class
python3 -m pytest compiler/tests/test_regressions.py::TestCompilerRegressions -v

# Run with CLI flags
python3 programs/tree_hash.py --trace
```

### Test Organization (compiler/tests/)

Tests are organized by functionality:

- **test_kernel.py**: End-to-end kernel correctness tests (small/medium/full sizes)
- **test_simple_programs.py**: Unit tests for individual IR features (arithmetic, loops, if/else, select, bitwise ops)
- **test_regressions.py**: Regression tests for specific compiler bugs
- **test_loop_unroll.py**: Pass manager and loop unroll tests (including pragma unroll)
- **test_cse.py**: Common subexpression elimination pass tests
- **test_simplify.py**: Simplify pass tests (constant folding, identity ops, strength reduction)
- **test_dce.py**: Dead code elimination pass tests
- **test_simplify_cfg.py**: Control flow graph simplification pass tests
- **test_codegen.py**: Code generation optimization tests

Shared fixtures and imports are in `conftest.py`.

### Adding Tests

When fixing compiler bugs, always add a regression test that:
1. Demonstrates the bug (would fail before the fix)
2. Verifies the fix works correctly
3. Prevents future regressions

## Validation

Always validate submissions with:
```bash
python3 tests/submission_tests.py  # Correctness test must pass; speed tests are informational only
```

**Note:** The submission tests include both correctness and speed tests. All correctness tests must pass, but the speed/performance tests (cycle count thresholds) are informational benchmarks and do not need to pass.
