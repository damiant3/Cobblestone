# AI-Comp (Anthropic Interview Compiler)

Anthropic published their [original performance take-home](https://github.com/anthropics/original_performance_takehome) interview challenge: optimize a kernel running on a simulated VLIW SIMD virtual machine to minimize cycle count for a tree traversal + hash computation workload.

Instead of hand-optimizing the kernel, I wrote an optimizing compiler that takes a high-level IR description of the kernel and compiles it down to efficient VLIW bundles.

## Project Structure

```
ai-comp/
├── compiler/           # Optimizing compiler (HIR → LIR → MIR → VLIW)
│   ├── passes/         # Optimization passes (DCE, CSE, SLP vectorization, etc.)
│   └── tests/          # Compiler unit and regression tests
├── programs/           # Kernel implementations using the compiler
├── vm/                 # Thin wrapper around the upstream VM simulator
├── original_performance_takehome/  # Unmodified upstream challenge code
├── tests/              # Submission correctness and speed tests
├── docs/               # Architecture and design documents
└── tools/              # Development utilities
```

## Usage

```bash
# Compile and run the tree hash kernel
python programs/tree_hash.py

# Run submission tests (correctness must pass; speed tests are informational)
python tests/submission_tests.py

# Run compiler unit tests
python -m pytest compiler/tests/ -v
```

### Kernel Parameters

```bash
python programs/tree_hash.py --forest-height 10 --rounds 16 --batch-size 256
```

| Flag | Default | Description |
|------|---------|-------------|
| `--forest-height` | 10 | Height of the forest tree |
| `--rounds` | 16 | Number of hash rounds |
| `--batch-size` | 256 | Elements per batch |

### Compiler Diagnostics

```bash
python programs/tree_hash.py --print-after-all     # Print IR after each pass
python programs/tree_hash.py --print-metrics        # Print pass metrics and diagnostics
python programs/tree_hash.py --print-ddg-after-all  # Print data dependency graphs
python programs/tree_hash.py --print-vliw           # Print final VLIW instructions
python programs/tree_hash.py --profile-reg-pressure # Write register pressure HTML chart
```

### Custom Pass Config

The compiler pipeline is defined in `compiler/pass_config.json`. To run with a different config (e.g. for A/B testing or parallel searches):

```bash
python programs/tree_hash.py --pass-config /path/to/my_config.json
```

This allows multiple compiler instances to run concurrently with different configurations. The config file has two sections:

- **`pipeline`** — ordered list of pass names to execute (passes can appear multiple times)
- **`passes`** — per-pass `enabled` flag and `options` dict

### Trace Viewer

```bash
python programs/tree_hash.py --trace
python original_performance_takehome/watch_trace.py
# Open http://localhost:8000 and click "Open Perfetto"
```
