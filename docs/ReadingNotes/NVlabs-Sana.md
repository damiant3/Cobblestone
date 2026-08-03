# NVlabs/Sana -- Linear Diffusion Transformers

**Date**: 2026-05-23
**Source**: https://github.com/NVlabs/Sana (Apache 2.0)
**Paper**: https://arxiv.org/abs/2410.10629
**What it is**: NVIDIA's text-to-image generator. 0.6B params, generates
1024x1024 images in <1s on a laptop GPU. Claims 20x smaller and 100x
faster than FLUX-12B with competitive quality.

## Three Ideas Worth Keeping

### 1. Deep front-end compression dominates all other optimizations

Sana trains an autoencoder that compresses images 32x (vs. the standard
8x). This reduces the token count fed to the transformer by 16x.
Because attention is at least O(n) and usually O(n^2), compressing
the input by 4x in each spatial dimension makes everything downstream
cheaper -- not by a constant factor, but quadratically.

**The general principle**: if your pipeline has a stage whose cost
grows super-linearly with input size, the highest-leverage optimization
is a front-end compressor that shrinks the input while preserving the
semantics the downstream stages actually need.

**Where this applies in Codex**: the compiler already does this (source
text → tokens → AST → IR → machine code, each stage discards
information the next stage doesn't need). But the principle also
applies to:

- **Source indexing in the dev console.** SOURCE.SRC is a flat
  concatenation of all Codex source. Right now we parse it at boot
  to build a SourceIndex (chapter names + definition names). If the
  source corpus grows large, a pre-compressed index (a table of
  contents with byte offsets) on the boot disk would eliminate the
  parse-at-boot cost entirely. Build the index at image-build time,
  store it alongside SOURCE.SRC, and the dev console reads the index
  instead of scanning the full text.

- **Future agent inference.** If Codex ever runs a language model on
  bare metal (the Agent Manager menu exists), compressing the input
  context before it hits attention is the single most impactful
  optimization for bounded-memory hardware with no GC.

### 2. Linear attention -- O(n) instead of O(n^2)

Standard transformer self-attention materializes an n x n matrix
(queries x keys). Sana replaces this with linear attention: instead
of computing softmax(QK^T)V, it computes Q(K^T V) -- changing the
associativity so the inner product is keys x values (d x d, fixed
size) rather than queries x keys (n x n, grows with input).

The tradeoff: linear attention is less expressive (it can't represent
arbitrary pairwise interactions). In practice, for the tasks Sana
targets, the loss is negligible.

**Where this applies in Codex**:

- **Agent inference on bare metal.** Standard attention requires
  O(n^2) memory -- on a 2 GB machine with no GC, that's a hard wall.
  Linear attention keeps memory O(n), which is the difference between
  "works" and "impossible" for long-context inference on the Codex
  memory model.

- **Search / pattern matching.** Any algorithm that compares all pairs
  of items is a candidate for the same associativity trick: can we
  restructure the computation so the inner dimension is fixed-size
  (feature dimension) rather than input-size? This is relevant to
  the TextSearch library if it ever needs to do fuzzy matching over
  large corpora.

### 3. Decoder-only architecture for uniform compute patterns

Sana replaced the standard T5 text encoder (encoder-decoder, different
compute shapes for encoder vs. decoder vs. cross-attention) with a
small decoder-only LLM. The result: one type of layer, one memory
access pattern, one KV-cache strategy. Simpler to implement, simpler
to optimize, simpler to reason about resource consumption.

**The general principle**: when two architectures produce comparable
results, prefer the one with fewer distinct computational patterns.
Uniformity is a resource -- it reduces the number of code paths,
simplifies scheduling, and makes worst-case analysis tractable.

**Where this applies in Codex**:

- **Compiler pipeline philosophy.** Codex already follows this -- every
  phase is "read IR, produce IR, allocate from bivy, persist to deck."
  The uniformity is load-bearing: phase-compact works because every
  phase follows the same allocation discipline. Sana validates this
  instinct from a completely different domain.

- **Hardware-aware design.** On bare metal with a fixed memory layout
  (code at 0x100000, heap at 0x600000, stack at 0x80000000), uniform
  compute patterns mean predictable memory usage. A single layer type
  means one set of buffer sizes to get right, not N.

## What We Don't Take

- **Diffusion / flow matching / consistency distillation.** These are
  specific to generative image models. No current Codex application.

- **RL-based post-training.** Interesting but requires a reward model
  and exploration budget. Not relevant until Codex has inference.

- **NVFP4 / mixed-precision training.** Requires GPU compute. The
  `GpuCompute.md` design doc might revisit this when GPU kernels land.

## One-Line Summary

Compress hard at the front, keep the core computation linear and
uniform, and the whole system fits on small hardware. Codex already
does this for compilation -- Sana shows the same pattern works for
neural inference.
