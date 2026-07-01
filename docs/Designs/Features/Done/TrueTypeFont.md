# TrueType Font Rasterizer and Public Domain Font Library

**Status:** Active -- Phases 1-5 complete, Phase 6 (curation) done
**Agent:** val
**Date:** 2026-06-20

## Goal

Ship a TrueType font rasterizer written in Codex, plus a curated
library of public domain (CC0) fonts covering monospace, serif, and
sans-serif families. The rasterizer runs on bare metal with no
external dependencies -- no FreeType, no HarfBuzz, no borrowed code.

## Font Sources (all CC0 / Public Domain)

| Source | License | What to Pull |
|--------|---------|-------------|
| Typodermic CC0 | CC0 1.0 | Professional-grade families: Blue Highway, Neuropol, and other high-quality serif/sans/mono from the 729-font collection |
| OpenGameArt CC0 | CC0 1.0 | Curated pixel and bitmap fonts for game UI, HUDs, terminals |
| Computer Modern | Public domain (Knuth) | Serif family -- the TeX standard, converted to TTF by Blue Sky Research / AMS |

Minimum coverage: one monospace, one serif, one sans-serif. Ship all
that meet a quality bar (clean outlines, full Latin coverage, bold +
italic variants preferred).

### Font Inventory (Downloaded)

14 TTF fonts validated, all parseable by our TTF parser:

| Font | Style | Glyphs | UPM | Source | License |
|------|-------|--------|-----|--------|---------|
| cmunrm.ttf | Serif Regular | 700 | 2048 | Computer Modern Unicode | Public Domain (Knuth) |
| cmunbx.ttf | Serif Bold | 685 | 2048 | Computer Modern Unicode | Public Domain |
| cmunti.ttf | Serif Italic | 682 | 2048 | Computer Modern Unicode | Public Domain |
| cmunss.ttf | Sans Regular | 684 | 2048 | Computer Modern Unicode | Public Domain |
| cmunsx.ttf | Sans Bold | 684 | 2048 | Computer Modern Unicode | Public Domain |
| cmuntt.ttf | Mono Regular | 684 | 2048 | Computer Modern Unicode | Public Domain |
| cmuntb.ttf | Mono Bold | 680 | 2048 | Computer Modern Unicode | Public Domain |
| HomeVideo-BLG6G.ttf | Display Regular | 804 | 1000 | FontSpace | CC0 |
| HomeVideoBold-R90Dv.ttf | Display Bold | 804 | 1000 | FontSpace | CC0 |
| LiberStructRegular-5yDOB.ttf | Sans Quirky | 945 | 2048 | FontSpace | CC0 |
| PublicPixel-rv0pA.ttf | Pixel | 1324 | 1024 | FontSpace | CC0 |
| SecolineRegular-aYmdx.ttf | Geometric | 126 | 2048 | FontSpace | CC0 |
| Unitblock-JpJma.ttf | Block | 437 | 8192 | FontSpace | CC0 |
| BrailleDisplayRegular-rvVB8.ttf | Braille | 345 | 2048 | FontSpace | CC0 |

Fonts stored in `fonts/cc0/`. Downloaded by `tools/validate-fonts.ps1`.

## Architecture

### New Modules

| Module | Quire | Purpose |
|--------|-------|---------|
| TrueType.codex | codex.foreword.encode | TTF binary parser: offset table, table directory, required tables |
| GlyphRasterizer.codex | codex.foreword.ui | Outline scaling, Bezier flattening, scanline fill, AA |
| FontAtlas.codex | codex.foreword.ui | Pre-rasterized glyph cache for efficient text rendering |

### Existing Modules Used

| Module | From | What It Provides |
|--------|------|-----------------|
| Bezier.codex | codex.foreword.math | `bezier2-eval` -- quadratic Bezier evaluation (TrueType uses quadratic splines) |
| Geometry.codex | codex.foreword.math | Vec2 operations, point-in-polygon, line segment intersection |
| Rasterizer.codex | codex.foreword.game | Framebuf, `fb-set`, `fb-hline` -- scanline fill target |
| Color.codex | codex.foreword.game | Rgb, alpha-blend for anti-aliased rendering |
| Font.codex | codex.foreword.ui | Glyph / BitmapFont types -- output target for rasterized glyphs |
| Bmp.codex | codex.foreword.encode | BMP encode/decode for testing and screenshot output |

### Data Flow

```
.ttf file bytes
    |
    v
TrueType.codex -- parse tables
    |  cmap: codepoint -> glyph index
    |  loca: glyph index -> offset in glyf
    |  glyf: outline contours (on/off-curve points)
    |  head: unitsPerEm
    |  hmtx: advance widths, left side bearings
    v
GlyphRasterizer.codex
    |  1. Scale FUnits to pixels: coord * ppem / unitsPerEm
    |  2. Expand implicit on-curve midpoints
    |  3. Flatten quadratic Beziers to line segments (adaptive subdivision)
    |  4. Scanline fill with non-zero winding rule
    |  5. Anti-alias via 4x4 sub-pixel sampling
    v
Glyph record (gl-code, gl-width, gl-height, gl-pixels)
    |
    v
FontAtlas.codex -- cache rasterized glyphs by (font, size, codepoint)
    |
    v
Font.codex / Render.codex -- text layout and rendering to Framebuf
```

## TTF Parser (TrueType.codex)

### File Structure

Offset table (12 bytes): sfnt version, numTables, search hints.
Table directory: numTables entries of (tag, checksum, offset, length).
All multi-byte values are big-endian.

### Required Tables (7)

| Table | Purpose | Parse Complexity |
|-------|---------|-----------------|
| `head` | unitsPerEm, indexToLocFormat (short/long loca) | Simple: fixed fields at known offsets |
| `maxp` | numGlyphs | Simple: one field |
| `cmap` | Unicode to glyph index | Medium: format 4 segment arrays |
| `loca` | Glyph offsets into glyf | Simple: array of short or long offsets |
| `glyf` | Glyph outlines | Complex: contour points, flags, compound refs |
| `hhea` | numberOfHMetrics | Simple: fixed fields |
| `hmtx` | Advance widths, LSBs | Simple: parallel arrays |

### Skipped for v1

`name`, `OS/2`, `post` (metadata), `cvt`, `fpgm`, `prep` (hinting),
`kern` (kerning), `GSUB`/`GPOS` (OpenType layout). Hinting is the
single largest complexity source in TrueType -- a stack-based VM with
~200 opcodes. We skip it entirely for v1 and rely on anti-aliasing
for quality at small sizes.

## Glyph Rasterizer (GlyphRasterizer.codex)

### Quadratic Bezier Flattening

TrueType outlines use quadratic (degree-2) Bezier splines. Two
adjacent off-curve points have an implicit on-curve midpoint between
them. The flattening algorithm:

1. For each contour, walk the point list and expand implicit midpoints
2. For each quadratic segment (P0, P1, P2), subdivide recursively
   until the control point is within a tolerance of the chord
   (flatness test: distance from P1 to midpoint(P0,P2) < 0.5 pixels)
3. Emit the resulting line segments

The foreword `bezier2-eval` can evaluate points along the curve. We
need a new `bezier2-flatten` that produces a list of line segments
from a quadratic curve given a pixel tolerance.

### Scanline Fill (Non-Zero Winding)

For each scanline y:
1. Find all intersections of the outline edges with the scanline
2. Sort intersections by x coordinate
3. Walk left to right, tracking winding number
4. Fill pixels where winding != 0

This integrates with `fb-hline` from Rasterizer.codex for the fill
spans.

### Anti-Aliasing

Coverage-based AA at 4x4 sub-pixel resolution:
- For each pixel, test 16 sample points against the glyph outline
- Alpha = count_inside / 16
- Blend glyph color with background using alpha

This gives 17 gray levels (0/16 through 16/16) per pixel, which is
sufficient for readable text down to ~10pt on a 96 dpi display.

### Coordinate Scaling

```
ppem = point_size * dpi / 72
pixel_x = funits_x * ppem / units_per_em
pixel_y = funits_y * ppem / units_per_em
```

Use integer arithmetic with sufficient precision (shift left by 6
for 26.6 fixed-point, matching FreeType's internal format).

## Font Atlas (FontAtlas.codex)

Lazy cache of rasterized glyphs. Key: (font-id, size-ppem, codepoint).
Value: Glyph record with pre-rasterized pixel data.

The atlas avoids re-rasterizing the same glyph. For bare-metal use,
pre-populate at boot for the ASCII range (32-126) at common sizes
(12, 14, 16, 20, 24 ppem). Rasterize on demand for anything else.

## Implementation Phases

### Phase 1: TTF Parser

Parse the 7 required tables. Test by loading a CC0 font and
extracting glyph count, advance widths, and the cmap for ASCII.
Validation: round-trip a known glyph index through cmap.

### Phase 2: Outline Extraction

Read glyph contours from the glyf table. Handle simple glyphs
(on/off-curve points) and compound glyphs (component references).
Expand implicit midpoints. Test by extracting the outline of 'A'
and verifying point coordinates against a reference tool.

### Phase 3: Rasterizer Core

Bezier flattening, scanline fill, pixel output. No AA yet --
binary black/white. Test by rendering 'A' at 48pt to a Framebuf
and writing a BMP via bmp-encode.

### Phase 4: Anti-Aliasing

Add 4x4 sub-pixel sampling. Test by rendering the full ASCII set
at 16pt and comparing visual quality against a reference rendering.

### Phase 5: Font Atlas and Integration

Wire the rasterizer into Font.codex's BitmapFont system. The atlas
pre-populates at boot, then any existing `font-draw-text` call
works with TrueType fonts. Ship the CC0 font files as data
alongside the foreword.

### Phase 6: Font Curation

Pull the CC0 collections, test each font through the pipeline, and
select the shipping set. Document which fonts are included and their
provenance.

## Memory and Time Complexity

**Parser:** O(numTables) for table directory scan, O(numGlyphs) for
loca. All table reads are offset-based, no full scans. Memory: one
TtfFont record with pointers into the raw byte buffer.

**Rasterizer:** O(P * log(1/tolerance)) per glyph for Bezier
flattening (P = number of points). O(E * H) for scanline fill
(E = edges, H = glyph height in pixels). At 16 ppem, a typical
glyph has ~30 points and ~20 pixel rows -- well under 1ms.

**Atlas:** O(1) lookup per cached glyph. Memory: ~200 bytes per
glyph at 16 ppem (16x20 pixels * 1 byte alpha). Full ASCII cache
at one size: ~19 KB.

**AA overhead:** 16x the scanline fill work (4x4 sampling). At
16 ppem this is still trivial. For large sizes (72pt+), consider
reducing to 2x2.

## Risks

1. **Quality without hinting.** At small sizes (< 10pt), unhinted
   TrueType looks soft. Mitigation: AA helps, and bare-metal displays
   are often higher DPI than 96. Phase 2 can add basic auto-hinting
   (stem detection + grid alignment) without the full TT VM.

2. **Compound glyph complexity.** Accented characters (e, a, o with
   diacritics) use compound glyphs with transform matrices. Must
   handle recursive component resolution. Codex foreword has
   LinearAlgebra for the matrix math.

3. **Font file sizes.** A full Unicode TTF can be 5+ MB. For
   bare-metal, subset to Latin + common symbols, or load from disk
   on demand.
