# Font Creator — AI Model to TrueType Font Pipeline

**Date**: 2026-06-22
**Status**: Design
**Agent**: reek

## Vision

Generate custom TrueType fonts on bare metal from a style prompt or
a handful of reference glyphs. The user describes a font ("rounded
sans-serif for headings", "pixel-art monospace", "handwritten
journal") and the system produces a `.ttf` file ready for the GUI
desktop's TrueType rasterizer. No cloud, no PyTorch, no borrowed
trust.

## State of the Art (as of 2026-06)

Research verified via multi-source adversarial search (2024-2026):

### Vector-Native Models (output Bezier curves)

| Model | Venue | Output Format | Few-Shot | Open Source | Notes |
|-------|-------|---------------|----------|-------------|-------|
| **VecGlypher** | CVPR 2026 | SVG path tokens (autoregressive LLM) | Yes (text or image ref) | github.com/xk-huang/VecGlypher | Single-pass, no raster intermediate |
| **VecFusion** | CVPR 2024 (Adobe) | Cubic Bezier control points | 4 reference glyphs | Not released | Cascaded diffusion (raster UNet + BERT transformer). L1=0.069 vs DeepVecFont-v2=0.098 |
| **DiffVecFont** | CVM 2025 | **Quadratic Bezier** (TrueType-native) | Via raster conditioning | Paper only | Masked diffusion transformer, dual-modal denoising |
| **DualVector** | CVPR 2023 | Pos/neg Bezier path pairs → boolean → TrueType | Yes | github.com/thuliu-yt16/dualvector | Explicit TrueType conversion claimed |
| **DeepVecFont-v2** | CVPR 2023 | Sequential Bezier drawing commands | Yes | github.com/yizhiwang96/deepvecfont-v2 | Transformer replaces RNN from v1 |

### Raster-Only Models (output pixel images)

| Model | Venue | Method | Few-Shot | Open Source |
|-------|-------|--------|----------|-------------|
| **DA-Font** | ACM MM 2025 | VQ-VAE + dual-attention transformer | Yes | github.com/wrchen2001/DA-Font |
| **FontDiffuser** | AAAI 2024 | One-shot diffusion | 1 glyph | github.com/yeungchenwa/fontdiffuser |
| **Clova AI suite** | Various | FUNIT/DM-Font/LF-Font/MX-Font | <10 glyphs, no fine-tune | github.com/clovaai/fewshot-font-generation |

### Gap Analysis

- **No model exists in ONNX or GGUF format.** All are PyTorch
  research code.
- **No model reports parameter counts for edge assessment.** VecFusion
  trains on 8x A100 for 5 days; inference is ~10s/glyph on A100.
- **No model targets embedded/consumer hardware.** The field is
  entirely research-grade GPU.
- **Quadratic Bezier output is rare.** Most produce cubic (PostScript)
  curves. Only DiffVecFont explicitly targets quadratic (TrueType).
  Cubic→quadratic conversion is straightforward but adds a step.

**Our opportunity:** First system to run font generation on bare metal
with no external runtime. The existing Codex AI stack (GGUF loader,
tensor ops, inference) plus the TrueType infrastructure (parser,
rasterizer, glyph renderer) provide both ends of the pipeline. The
missing middle is the model itself and the TrueType writer.

## Architecture

```
 Style Prompt / Reference Glyphs
              |
     [ Font Generation Model ]
     (GGUF, ~20-50M params)
              |
     Bezier contour sequences
     (per-glyph: list of quadratic curves)
              |
     [ TrueType Font Writer ]
     (new codex module)
              |
     .ttf file in memory
              |
     [ FontLoad rasterizer ]
     (existing: fb-parse-ttf → fb-render-ascii)
              |
     GopBitmapFont → screen
```

### Phase 1: TrueType Font Writer

Build the inverse of the existing TrueType reader. Given a list of
glyph contours (quadratic Bezier control points), character mappings,
and metrics, emit a valid TrueType binary.

Required TrueType tables:
- `head` — font header (units-per-em, bounds, flags)
- `hhea` — horizontal header (ascent, descent, line gap)
- `maxp` — maximum profile (num glyphs, max points, max contours)
- `OS/2` — OS metrics (weight class, width class, panose)
- `name` — font name records (family, style, unique ID)
- `cmap` — character-to-glyph mapping (format 4 for BMP)
- `loca` — glyph offset table (short or long format)
- `glyf` — glyph outlines (contour endpoints, flags, x/y deltas)
- `hmtx` — horizontal metrics (advance width, left side bearing)
- `post` — PostScript name mapping

**Input:** `TtfBuildSpec` record with font name, units-per-em,
ascent/descent, and a list of `GlyphSpec` records (codepoint, advance,
lsb, list of contours where each contour is a list of `CurvePoint`
records with x, y, on-curve flag).

**Output:** `List Integer` (byte sequence) that can be written to disk
as a `.ttf` file or fed directly to `fb-parse-ttf`.

Estimated size: ~400-600 lines of Codex. No new dependencies — just
byte-level packing using `__buf-write-byte` / `__buf-write-bytes`.

### Phase 2: Glyph Generation from Templates

Before tackling a neural model, build a procedural glyph generator
that produces simple geometric fonts:

- **Block font**: each glyph is axis-aligned rectangles (like the
  existing Unitblock CC0 font, but generated programmatically)
- **Rounded font**: rectangles with quarter-circle corners
- **Pixel font**: grid-snapped outlines at low resolution

This validates the TrueType writer end-to-end and provides fallback
fonts that don't require AI inference.

Estimated size: ~200-300 lines per font template.

### Phase 3: Small Vector Font Model (GGUF)

Train or distill a small model (~20-50M params) for font generation:

**Architecture options (ranked by feasibility for Codex):**

1. **Autoregressive sequence model** (VecGlypher-style)
   - Tokenize Bezier control points as discrete tokens
   - Small transformer (6-8 layers, 256-512 dim)
   - Input: style embedding + character code
   - Output: sequence of (x, y, on-curve, end-contour) tokens
   - Estimated: ~20M params, ~80MB GGUF (Q4)
   - Pro: Natural fit for GGUF/autoregressive inference
   - Con: Sequential generation (one token at a time)

2. **Conditional VAE** (DeepVecFont-style)
   - Encoder: reference glyphs → style latent
   - Decoder: style latent + char code → Bezier sequence
   - Estimated: ~15M params, ~60MB GGUF (Q4)
   - Pro: Fast generation (single forward pass per glyph)
   - Con: VAE inference needs custom ops beyond basic transformer

3. **Diffusion on control points** (VecFusion-style)
   - Denoise Bezier control point coordinates
   - Estimated: ~30M params, ~120MB GGUF (Q4)
   - Pro: Highest quality in benchmarks
   - Con: Multi-step inference (20-50 denoising steps)

**Recommendation:** Option 1 (autoregressive). It maps directly to
the existing GGUF transformer inference in `foreword/ai/`. Each glyph
is a sequence of ~50-200 tokens. At 95 ASCII glyphs, generation takes
~5000-19000 tokens total — feasible in seconds on bare metal.

**Training data:** Google Fonts (5000+ open fonts, all with vector
outlines). Extract quadratic Bezier contours, normalize to
units-per-em grid, tokenize. Training on a single GPU (A100 or
4090) for 1-2 days.

**Style conditioning:** Encode style as a learned embedding vector.
Options:
- Text prompt → small text encoder (CLIP-style, ~5M params)
- Reference glyphs → CNN encoder → mean-pool style vector
- Attribute vector (weight, width, serif/sans, x-height ratio)

### Phase 4: Interactive Font Creator App

GuiOS app that provides:
- Style prompt text field
- Reference glyph upload (draw or load)
- Live preview of generated glyphs
- Slider controls: weight, width, x-height, contrast
- Export to .ttf on the font disk
- "Set as system font" to apply immediately

Uses the GopRender widget toolkit for the UI, the GGUF inference
stack for generation, the TrueType writer for export, and FontLoad
for preview.

## Dependencies

| Component | Status | Location |
|-----------|--------|----------|
| TrueType reader/parser | Done | `foreword/encode/TrueType.codex` |
| Glyph rasterizer | Done | `foreword/ui/GlyphRasterizer.codex` |
| Font loader (disk→screen) | Done | `apps/guios/FontLoad.codex` |
| GGUF model loader | Done | `foreword/ai/Gguf.codex` |
| Tensor operations | Done | `foreword/ai/Tensor.codex` |
| Transformer inference | Done | `foreword/ai/Transformer.codex` |
| GopRender (widget→GOP) | Done | `apps/guios/GopRender.codex` |
| **TrueType writer** | **Phase 1** | `foreword/encode/TrueTypeWriter.codex` |
| **Procedural glyph gen** | **Phase 2** | `apps/guios/FontGen.codex` |
| **Font generation model** | **Phase 3** | External training → GGUF |
| **Font creator app** | **Phase 4** | `apps/guios/FontCreatorApp.codex` |

## Sizing

| Phase | Est. Lines | Blocks | Risk |
|-------|-----------|--------|------|
| 1. TrueType Writer | 400-600 | 1 CL | Low — table format is well-documented |
| 2. Procedural Fonts | 600-900 | 1-2 CLs | Low — geometric shapes only |
| 3. Model Training | External | Offline | Medium — need quality training data + hyperparameter tuning |
| 3b. GGUF Integration | 100-200 | 1 CL | Low — existing inference stack |
| 4. Font Creator App | 300-500 | 1-2 CLs | Low — standard GuiOS app pattern |

## Open Questions

1. **Quadratic vs cubic Bezier?** TrueType uses quadratic, most
   research models output cubic. Cubic→quadratic conversion is
   lossless for simple curves but approximate for complex ones.
   Decision: target quadratic directly in the tokenizer to avoid
   conversion artifacts.

2. **How many glyphs per font?** ASCII 32-126 (95 glyphs) is the
   minimum for GuiOS. Extended Latin (Tier 1 CCE, ~200 glyphs) for
   EU language support. Full Unicode coverage is a non-goal.

3. **Model hosting?** The GGUF file needs to be on the font disk or
   a separate model disk. At ~80MB Q4, it fits alongside the 13 CC0
   fonts on the 8MB font disk — but only if we increase the disk
   size or use a separate model disk.

4. **Quality bar?** The procedural fonts (Phase 2) are immediately
   useful. The AI fonts (Phase 3) need to be at least as readable as
   Public Pixel at 16ppem. Decorative/display fonts have a lower bar
   than body text fonts.

5. **Training infrastructure?** The model is trained OUTSIDE Codex
   (PyTorch on a GPU workstation), then exported to GGUF. This is the
   same pattern as the existing AI inference modules. The goal is
   inference-on-bare-metal, not training-on-bare-metal.

## References

- VecGlypher (CVPR 2026): arxiv.org/abs/2602.21461, github.com/xk-huang/VecGlypher
- VecFusion (CVPR 2024): arxiv.org/pdf/2312.10540
- DiffVecFont (CVM 2025): Springer LNCS, quadratic Bezier reconstruction
- DualVector (CVPR 2023): arxiv.org/pdf/2305.10462, github.com/thuliu-yt16/dualvector
- DeepVecFont-v2 (CVPR 2023): arxiv.org/html/2303.14585, github.com/yizhiwang96/deepvecfont-v2
- FontDiffuser (AAAI 2024): github.com/yeungchenwa/fontdiffuser (raster only)
- DA-Font (ACM MM 2025): github.com/wrchen2001/DA-Font (raster only)
- Clova AI few-shot suite: github.com/clovaai/fewshot-font-generation (raster only)
