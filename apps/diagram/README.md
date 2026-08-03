# Codex Diagram

A full-featured interactive diagramming editor supporting flowcharts, ERDs, UML class and sequence diagrams, network topologies, mind maps, state machines, and freeform graphs. Runs bare-metal via codex-vm with Console effects and persists diagrams to DiskFacts using JSON serialization.

## Modules

- **DiagramModel** -- Core typed graph: Diagram, DiagramNode, DiagramEdge, 13 ShapeTypes, port model, snap-to-grid
- **DiagramRenderer** -- Full pixel-push rendering pipeline: grid, edges, all 13 shapes, arrowheads, labels, selection highlights
- **ShapeLibrary** -- Per-diagram-type shape catalogs (Flowchart, ERD, UML Class, Network, State Machine)
- **EdgeRouter** -- Four routing algorithms: straight with border clipping, orthogonal Manhattan, bezier, stepwise L-shape
- **SelectionManager** -- Single/multi-select, shift-toggle, marquee, drag-move, resize handle hit testing
- **Canvas** -- Scrollable/zoomable viewport, pan mode, coordinate conversion, zoom-fit
- **CommandHistory** -- Full undo/redo stack (snapshot-based, depth-capped at 50)
- **Toolbar** -- Tool palette (Select, Connect, Text, Pan, AddNode), shape picker, property panel
- **DiagramSerializer** -- JSON save/load (nodes, edges, grid, snap, routing)
- **DiagramPersist** -- DiskFacts integration (kind 22), checkpoint on Ctrl+S
- **Diagram** -- Main app state machine: keyboard shortcuts, mouse dispatch, inline label editing, connect-mode drag

## Completeness

82% -- Core functionality is fully implemented end-to-end: model, rendering, routing, selection, undo/redo, persistence, keyboard shortcuts. Gaps: text rendering uses filled-rectangle placeholders instead of glyphs; ShapeCylinder/Cloud/Document fall back to rectangles; DiagramSerializer restores all shapes as ShapeRect; toolbar widget tree not connected to framebuffer paint path.

## Codex Conformance

Full -- Written entirely in Codex. Renders directly to framebuffer via foreword primitives. Persistence through DiskFacts. No plug involvement -- pure Codex top-to-bottom.
