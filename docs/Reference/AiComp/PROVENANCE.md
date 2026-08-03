# ai-comp -- Provenance and Terms of Use

## What this is

`upstream/` is a verbatim archive of the design documentation and pass
configuration from **fiigii/ai-comp**, taken 2026-07-14.

| | |
|---|---|
| Source | `https://github.com/fiigii/ai-comp` |
| Author | Fei Peng (`fiigii`) -- GPU and compiler engineer, NVIDIA |
| Created | 2026-02-05 |
| Snapshot taken | 2026-07-14, from `main` (upstream last pushed 2026-07-12) |
| Upstream size | ~875 KB, Python, 95 commits, 77 stars, 11 forks |
| **License** | **NONE -- no LICENSE file, no license metadata** |

It is an optimizing compiler written for Anthropic's published
[performance take-home](https://github.com/anthropics/original_performance_takehome):
optimize a kernel (tree traversal + hash) on a simulated VLIW SIMD virtual
machine, minimizing cycle count. Rather than hand-tune the kernel, the author
wrote a compiler -- `HIR -> LIR -> MIR -> VLIW`, sixteen passes over three IRs --
and had it generate the kernel.

We archived it because it is a complete, well-documented, *working* middle-end,
and a middle-end is the one part of a compiler Codex does not have.

## Terms: read, do not lift

**The repository carries no license. That means all rights reserved.** We have
no grant to copy, adapt, or redistribute its code, and this archive confers
none.

What that permits and forbids, concretely:

- **Permitted, and the entire point:** reading the design documents and
  reimplementing the *algorithms* from scratch in Codex. Every algorithm
  described in `upstream/` is published, decades-old compiler literature --
  list scheduling, linear-scan allocation, SLP (Larsen & Amarasinghe, PLDI
  2000), SROA, straight-line strength reduction, interval-domain range
  analysis with widening. None of it is his invention and none of it is
  encumbered. The documents are a *good explanation* of public algorithms,
  which is exactly what makes them worth keeping.
- **Forbidden:** copying his Python into our tree, transliterating a function
  of his line-by-line into Codex, or vendoring any part of `upstream/` into a
  shipped artifact. `upstream/` is reference material and stays under
  `docs/`.

The two source files we archived (`compiler-pass_manager.py`,
`compiler-pass_config.json`) are here because the *pipeline-as-data* idea is
best understood by reading the actual config. They are illustrations, not a
starting point. Write ours from the design, not from his file.

If we ever want more than clean-room reimplementation, the answer is to ask
the author for a license, not to quietly copy.

## Why an archive at all

The repo is unlicensed, single-author, and tied to a specific interview
challenge. It can go private, get taken down, or be rewritten without notice.
The design documents are the durable value and they exist nowhere else. This
follows the standing convention in `docs/Reference/README.md`: store the
artifact, do not summarize-and-discard, because a citation without a local
copy is a hyperlink waiting to break.

## Contents of `upstream/`

| File | What it is |
|---|---|
| `VLIW_ISA.md` | The target machine: engines, slot limits, bundle semantics, scratch file |
| `instruction_scheduling_design.md` | Delay-aware list scheduler, LIR→MIR, critical-path priority |
| `slp_vectorization_design.md` | Superword-level parallelism: seeds, pack extension, legality, cost model |
| `range_analysis.md` | Sound u32 interval analysis: widening ladder, narrowing, branch refinement |
| `slsr.md` | Straight-line strength reduction over affine chain recurrences |
| `sroa.md` | Scalar replacement of aggregates; local-memory contract; read-only windows |
| `mad_optimization.md` | Multiply-add synthesis, and the cross-pass shaping that feeds it |
| `hir_load_elimination_design.md` | Store-to-load forwarding with base+offset alias analysis |
| `load_store_optimizations.md` | **Superseded by `sroa.md`** -- the author marks it historical. Kept for the reasoning, not the conclusions |
| `compiler-pass_config.json` | The pipeline as data: 27 ordered pass names + per-pass options |
| `compiler-pass_manager.py` | The pass manager that consumes it |
| `aicomp-Readme.md` | Upstream README |
| `aicomp-CLAUDE.md` | Upstream agent instructions -- a peer artifact of our own `CLAUDE.md` |

## Our reading of it

`OPPORTUNITIES.md`, alongside this file, is our analysis: what it does that we
do not, what is worth taking, what is not, and in what order. That document is
ours and carries no license question.
