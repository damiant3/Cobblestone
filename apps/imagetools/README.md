# Codex ImageTools

A lightweight image editing utility with a workspace canvas and a right-panel containing crop, resize, rotate/flip, brightness/contrast/saturation adjustments, and multi-format export.

## Features

- Five tool sections: Crop (X/Y/W/H + aspect ratio), Resize (width/height/scale + lock), Rotate/Flip (CCW 90, CW 90, 180, flip-H, flip-V), Adjustments (Brightness, Contrast, Saturation), Export (PNG/JPEG/BMP/QOI/TIFF/WebP + Quality)
- Workspace toolbar: Open File, Paste, Undo/Redo, zoom
- Batch processing drop zone

## Completeness

35% -- All five operation categories have complete UI skeletons. Only the open/empty-state toggle is live. No crop, resize, rotate, or adjustment controls apply any transformation. Sliders are labels, not range inputs. Export emits nothing.

## Codex Conformance

Full -- Pure Codex. Image processing operations belong to a backend plug.
