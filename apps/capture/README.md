# Codex Capture

A screenshot and image annotation tool with a left-side tool palette, central canvas workspace, and a right-side properties and export panel.

## Features

- Five capture-mode buttons (Select, Region, Window, Full Screen) and annotation tools (Arrow, Rectangle, Circle, Text, Blur)
- Canvas area toggling between empty-state and captured image placeholder
- Properties panel: Crop dimensions, Aspect Ratio selector, Export Format (PNG/JPEG/BMP/QOI), Quality display
- Recent Captures history list
- Undo/Redo buttons

## Completeness

35% -- Shell and layout are well-defined. Only the Capture button toggles the canvas between empty-state and placeholder. All annotation tools, zoom, save, copy, share, and format selection are structurally present but return 0.

## Codex Conformance

Full -- Entirely Codex. Pixel/OS capture operations would be backend plug responsibilities.
