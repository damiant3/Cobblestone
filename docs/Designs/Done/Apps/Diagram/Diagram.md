# Codex Diagram

A diagramming app for flowcharts, ERDs, UML, network topologies,
sequence diagrams, and freeform graphs. Runs inside the Codex
Browser as a page with `[Display, Network, Storage]` effects, or
standalone inside codex-vm.

---

## 1. Vision

Every diagramming tool is a web app now -- Miro, Lucidchart, draw.io,
Excalidraw. They all depend on the browser's DOM, SVG, and Canvas
APIs. We have none of those. What we have is better: a typed widget
tree, a rasterizer with lines/circles/rects/polygons/beziers, a
drag-and-drop system, and a graph data structure. That is everything
a diagram tool needs.

A diagram is a graph. Nodes have shapes, positions, labels. Edges
have endpoints, routing, labels. The data model is a typed Codex
record. The renderer walks the model and draws to a framebuffer.
The interaction model is select-drag-connect. No DOM, no SVG path
strings, no CSS transforms. Just data and pixels.

---

## 2. Diagram Types

| Type | Node Shapes | Edge Styles | Use Case |
|------|-------------|-------------|----------|
| Flowchart | Rectangle, diamond, oval, parallelogram, hexagon | Directed arrows, labeled | Process flows, algorithms |
| ERD | Entity box, attribute oval, relationship diamond | Lines with cardinality (1, N, M) | Database design |
| UML Class | Class box (name/attrs/methods compartments) | Solid (association), dashed (dependency), hollow arrow (inheritance), filled diamond (composition) | Object-oriented design |
| UML Sequence | Lifeline (actor/object), activation bar | Horizontal arrows (sync), dashed arrows (return) | Interaction flow |
| Network | Server, router, switch, firewall, cloud, client | Lines with bandwidth/protocol labels | Infrastructure |
| Mind Map | Central node, branch nodes | Curved branches, no arrows | Brainstorming |
| State Machine | Rounded rectangle, initial dot, final bullseye | Labeled transitions | FSM design |
| Freeform | Any shape, custom | Any connection style | General purpose |

---

## 3. Data Model

```
  DiagramNode = record {
    dn-id       : Integer,
    dn-shape    : ShapeType,
    dn-x        : Integer,
    dn-y        : Integer,
    dn-width    : Integer,
    dn-height   : Integer,
    dn-label    : Text,
    dn-sublabel : Text,
    dn-style    : NodeStyle,
    dn-ports    : List Port,
    dn-props    : List NodeProp
  }

  DiagramEdge = record {
    de-id       : Integer,
    de-source   : Integer,
    de-target   : Integer,
    de-src-port : Integer,
    de-tgt-port : Integer,
    de-label    : Text,
    de-style    : EdgeStyle,
    de-routing  : EdgeRouting,
    de-points   : List Vec2
  }

  Diagram = record {
    dg-nodes    : List DiagramNode,
    dg-edges    : List DiagramEdge,
    dg-name     : Text,
    dg-type     : DiagramType,
    dg-next-id  : Integer,
    dg-grid     : Integer,
    dg-snap     : Boolean
  }
```

---

## 4. Architecture

```
  ┌─────────────────────────────────────────────────┐
  │  Toolbar (shape palette, tools, properties)     │
  ├──────────┬──────────────────────────────────────┤
  │ Shape    │                                      │
  │ Palette  │          Canvas                      │
  │          │    (diagram viewport)                │
  │ ──────── │                                      │
  │ Props    │   Grid + Nodes + Edges + Selection   │
  │ Panel    │                                      │
  ├──────────┴──────────────────────────────────────┤
  │  Status bar (zoom, grid, cursor position)       │
  └─────────────────────────────────────────────────┘
```

---

## 5. App Structure

```
apps/diagram/
  codex.project.json
  opening.codex          -- entry point
  Diagram.codex          -- main app state, event loop
  DiagramModel.codex     -- data model: nodes, edges, diagrams
  DiagramRenderer.codex  -- render nodes/edges/grid to framebuf
  ShapeLibrary.codex     -- shape definitions per diagram type
  EdgeRouter.codex       -- edge routing: straight, orthogonal, bezier
  SelectionManager.codex -- select, multi-select, drag, resize
  Toolbar.codex          -- tool palette, shape picker, property panel
  DiagramSerializer.codex -- save/load in JSON format
  DiagramTheme.codex     -- colors, stroke widths, fonts
  GridSnap.codex         -- grid rendering, snap-to-grid, align guides
  Canvas.codex           -- scrollable viewport, zoom, pan
  CommandHistory.codex   -- undo/redo stack
```

---

## 6. Interaction Model

| Tool | Behavior |
|------|----------|
| Select | Click node/edge to select. Drag to move. Shift-click for multi-select. |
| Connect | Click source port, drag to target port. Creates edge. |
| Add Node | Click shape in palette, click canvas to place. |
| Text | Double-click node to edit label inline. |
| Pan | Middle-click drag or Space+drag. |
| Zoom | Scroll wheel or +/- keys. |
| Delete | Select + Delete key. |
| Undo/Redo | Ctrl+Z / Ctrl+Y. |

---

## 7. Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+A | Select all |
| Ctrl+C | Copy selection |
| Ctrl+V | Paste |
| Ctrl+S | Save |
| Ctrl+N | New diagram |
| Delete | Delete selection |
| Escape | Cancel / deselect |
| G | Toggle grid |
| S | Toggle snap |
| +/- | Zoom in/out |
| Arrow keys | Nudge selected nodes by 1px (or grid unit) |
