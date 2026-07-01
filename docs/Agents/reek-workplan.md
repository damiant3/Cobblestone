# Agent Reek Work Plan

**Date**: 2026-06-22
**Stream**: //Codex/MutableRecords
**Seed**: E625476A (CL 5704)
**Last CL**: 5856 (on reek), 5823 (on main)

---

## Priority 1: Fix TrueTypeWriter Runtime Crash

The TrueTypeWriter (`codex/foreword/encode/TrueTypeWriter.codex`)
generates TTF byte sequences that compile and build successfully but
crash `fb-parse-ttf` (in `apps/guios/FontLoad.codex`) at runtime.

### What Works

- `ttw-build-ttf` produces a `List Integer` of TTF bytes — runs fine
  (standalone test: 244 VM exits for 1 glyph, 1210 exits for full
  95-glyph block font)
- `fl-list-to-buf` copies bytes to an `alloc-bytes` buffer
- All table offsets, searchRange, entrySelector, rangeShift are now
  correct (after CL 5822 fixes)

### What Crashes

- `fb-parse-ttf buf size` crashes the VM (code=-1, HLT with IF=0)
- Crash at 2125 exits (without disk fonts) or 363K exits (with)
- Deterministic — same exit count every time

### Fixed So Far (CL 5816, 5822, 5846)

1. `ttw-build-head` (CL 5816): swapped checksumAdjustment/magicNumber
   fields, missing flags field before unitsPerEm.
2. `ttw-pow2-le` (CL 5822): returned `p * 2` instead of `p` —
   searchRange was 2x too large.
3. `indexToLocFormat` (CL 5816): was 1 (long) but loca uses short.
4. **`ttw-coord-flag` (CL 5846, ROOT CAUSE):** When `abs_delta == 0`,
   set both SHORT and SAME bits (`bit-or short-bit same-bit`), telling
   the parser "read a 1-byte positive value." But `ttw-write-x/y-coords`
   emits NO bytes for delta=0. The parser then consumed subsequent data
   as coordinate bytes, misaligning everything downstream. Every
   axis-aligned rectangle (the entire block font) had multiple delta=0
   occurrences, causing total corruption of all glyph coordinates.
   Fix: return `same-bit` only (TTF spec: !SHORT + SAME = same as
   previous, no data bytes).
5. **cmap length (CL 5846):** `table-len = 14 + seg_count * 8` didn't
   count the 2-byte reservedPad that IS written. Fixed to `16 + ...`.
   Cosmetic — rangeOffset=0 path doesn't use the length for lookup.

### Status: RESOLVED

The TrueTypeWriter runtime crash is fixed. Verified by running guios
with `fl-load-block-font` → `fl-load-generated` → `fb-parse-ttf`
end-to-end (exit code 0, no crash). Both with-disk and without-disk
paths work.

### Files

| File | Role |
|---|---|
| `codex/foreword/encode/TrueTypeWriter.codex` | TTF byte emitter |
| `codex/foreword/encode/FontGen.codex` | Block font glyph definitions |
| `apps/guios/FontLoad.codex` | `fl-load-generated` + `fb-parse-ttf` |
| `apps/guios/GuiShell.codex` | Boot-time font loading (call reverted) |
| `docs/Designs/Tools/Active/FontCreator.md` | Full design doc |

---

## Priority 2: GuiOS Polish — RESOLVED (CL 5856)

All three items addressed:

- **peek-32 0x7C4 GOP width (FIXED):** The VM writes gop_width to
  GPA 0x7C4 before boot. The original "returns 640" was from testing
  with `-gop` alone (default 640x480). With `-gop-width 1024`, the
  value is correct. GuiShell now reads dimensions dynamically via
  `peek-32 0 1988`/`peek-32 0 1992` with 1024x768 fallback.
- **GopRender TrueType support (DONE):** Threaded MutWheel through
  `gop-render-tree` and all render functions. Labels use
  `gop-buf-put-text-role` with `font-role-sans`, inputs use
  `font-role-mono`. Falls back to bitmap font when fonts not loaded.
  Updated 31 call sites across 9 app files.
- **TrackerApp Framebuf→GopBuf (ALREADY CLEAN):** Inspected — no
  Framebuf references remain. TrackerApp uses WidgetNode/Overlay/GopBuf
  throughout. The `ov-widget` field is WidgetNode, not Framebuf.

---

## Session 2 Summary (2026-06-22)

### CLs Submitted

| CL | Stream | Description |
|---|---|---|
| 5734 | reek | cvmm build fix: 14→0 type errors, 5 missing chapters |
| 5735 | main | copy-up |
| 5745 | reek | strengthened CVMM-TYPE-CHECKER-BUG.md |
| 5747 | reek | merge-down: ARM/RISC-V plug updates |
| 5748 | main | copy-up: bug doc |
| 5753 | main | Consolas removal: 18 files + font-disk rebuilt (13 CC0 fonts) |
| 5763 | reek | merge-down: Consolas fix |
| 5771 | reek | guios 1024x768 + build.ps1 + tracker fixes |
| 5773 | main | copy-up: guios desktop |
| 5778 | reek | GopRender bridge (widget toolkit → GOP framebuffer) |
| 5780 | main | copy-up: GopRender |
| 5785 | reek | 7 app modules → widget toolkit |
| 5787 | main | copy-up: app modules |
| 5788 | reek | 7 shell views → widget toolkit |
| 5789 | main | copy-up: shell views |
| 5793 | main | FontCreator design doc |
| 5795 | reek | TrueTypeWriter (Phase 1) |
| 5798 | main | copy-up: TrueTypeWriter |
| 5804 | reek | FontGen block font (Phase 2) |
| 5806 | main | copy-up: FontGen |
| 5808 | reek | FontLoad generated-font API |
| 5809 | main | copy-up: FontLoad API |
| 5816 | reek | TrueTypeWriter head table fix |
| 5818 | main | copy-up: head table fix |
| 5822 | reek | TrueTypeWriter pow2-le fix |
| 5823 | main | copy-up: pow2-le fix |
| 5846 | reek | TrueTypeWriter coord-flag + cmap length fix |
| 5847 | reek | GuiShell block font fallback + safe disk detection |
| 5848 | reek | docs: workplan update (TTF crash resolved) |
| 5856 | reek | dynamic GOP dimensions + GopRender TrueType threading |

### New Modules

| Module | Location |
|---|---|
| GopRender | `apps/guios/GopRender.codex` |
| TrueTypeWriter | `codex/foreword/encode/TrueTypeWriter.codex` |
| FontGen | `codex/foreword/encode/FontGen.codex` |
| guios build.ps1 | `apps/guios/build.ps1` |

### Known Bugs (documented)

- `integer-to-text` INT64_MIN: garbled CDX2051 warnings. See
  `docs/Test/KNOWN-CONDITIONS.md`. Display only, no codegen impact.
- TrueTypeWriter runtime crash: generated TTF crashes `fb-parse-ttf`.
  See Priority 1 above.
