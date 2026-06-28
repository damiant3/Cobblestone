# Codex Circuits — Full UI Workplan

**Agent:** reek  
**Stream:** //Codex/MutableRecords  
**Started:** CL 6240 (2026-06-27) — 59 chapters, GPU-rendered demo  
**Goal:** Make Codex Circuits a fully functional, interactive EDA tool

---

## Current State (CL 6240)

- 59 .codex source files across 8 subsystem directories
- Full data model: netlists, DRC rules, stack-ups, components, SPICE
- GPU triangle rasterizer renders static demo scene (77 triangles)
- No interactivity — app renders one frame and loops on `uefi-read-key`
- No file I/O, no component placement, no wire drawing, no property editing
- VM fixes committed: RAM cap for GPU region, serial exit guard, GPU region commit

---

## Phase 1: Interactive Canvas Foundation

**Goal:** Mouse + keyboard input, pan/zoom, grid, coordinate display

### 1.1 Input System
- Read `uefi-read-key` for keyboard (already works)
- Read mouse from GPA 28684 (kernel metadata cell `key-buffer-addr + 4`)
  - 3-byte packet: buttons, dx, dy (same as globe app)
- Build `InputState` record: mouse-x, mouse-y, buttons, last-key, modifiers
- Event loop: poll input → update state → re-render → repeat

### 1.2 Camera/Viewport
- Pan with middle-mouse-drag or Space+drag
- Zoom with scroll wheel (scale factor around cursor)
- Zoom-to-fit with Home key
- Track viewport origin + zoom level in `ViewState` record
- World-to-screen and screen-to-world coordinate transforms

### 1.3 Grid System
- Configurable grid spacing (default 50 mil = 1.27mm for schematic)
- Snap-to-grid for all placement operations
- Grid dots rendered as single-pixel points (already have this)
- Grid spacing display in status bar

### 1.4 Status Bar
- Cursor position in world coordinates (X, Y)
- Zoom level
- Active tool name
- Grid spacing
- Selected object count

### 1.5 Coordinate Crosshair
- Full-screen crosshair following mouse cursor
- Shows current snap position

**Deliverable:** Pan/zoom around the schematic canvas with grid and status bar updating live.

---

## Phase 2: Tool System + Selection

**Goal:** Click to select, move, delete objects. Tool palette.

### 2.1 Tool State Machine
- Active tool: Select, Wire, Place Symbol, Place Power, Label, No-Connect, Pan, Zoom, Measure
- Esc cancels current tool, returns to Select
- Tool indicator in toolbar (highlight active button)
- Keyboard shortcuts: W=Wire, P=Place, L=Label, X=No-Connect, Esc=Select

### 2.2 Hit Testing
- Point-in-rectangle for component bodies
- Distance-to-line for wires
- Point-in-circle for junction dots
- Spatial index (flat scan first, quadtree later) for large schematics

### 2.3 Selection
- Click to select one object
- Shift+click to toggle selection
- Drag rectangle for marquee selection
- Selected objects highlighted in blue/white
- Selection info in status bar

### 2.4 Move
- Click-and-drag selected objects
- Snap to grid during move
- Wires rubber-band when connected component moves

### 2.5 Delete
- Delete key removes selected objects
- Connected wires cleaned up on component delete

### 2.6 Undo/Redo
- Snapshot-based undo stack (already have UndoStack type)
- Ctrl+Z undo, Ctrl+Y redo
- Status bar shows undo depth

**Deliverable:** Click, select, move, and delete schematic objects interactively.

---

## Phase 3: Schematic Editing

**Goal:** Place components, draw wires, add labels — the core schematic workflow.

### 3.1 Component Placement
- P key or toolbar button opens component chooser
- For now: hardcoded palette of common parts (resistor, capacitor, LED, MCU, connector, crystal, voltage regulator, diode, transistor, op-amp)
- Click to place at cursor position, snap to grid
- R to rotate before placing (0/90/180/270)
- Component body rendered with outline, pins, reference designator, value

### 3.2 Wire Drawing
- W key activates wire tool
- Click to start wire, click to place corners, double-click or Esc to end
- Wires snap to grid and to pin endpoints
- Auto-junction when wire crosses another wire
- 90-degree routing by default (horizontal-then-vertical)

### 3.3 Labels
- L key: place net label at wire
- Power symbols: VCC, GND, 3.3V, 5V from palette
- No-connect flag (X) on unconnected pins

### 3.4 Properties Panel
- Click component → right sidebar shows:
  - Reference designator (editable)
  - Value (editable)
  - Footprint assignment
  - Pin list
- Click wire → shows net name
- Click label → shows label text (editable)
- Double-click to edit in-place

### 3.5 Reference Designator Auto-Annotation
- Auto-assign R1, R2, C1, C2 etc. on placement
- Re-annotate command (uses SchematicAnnotate module)
- By-position or by-sheet ordering

**Deliverable:** Draw a complete schematic from scratch — place parts, wire them, label nets.

---

## Phase 4: File I/O + Project Management

**Goal:** Save and load designs, manage project files.

### 4.1 Project File
- Save to DiskFacts (fact kind 33, already defined in SchematicPersist)
- Serialize entire design state to JSON via CircuitSerializer
- Ctrl+S to save, status bar shows "Saved"
- Auto-save at configurable interval

### 4.2 New/Open/Close
- Ctrl+N: new blank schematic
- Ctrl+O: load from DiskFacts (show list of saved designs)
- Recent files list

### 4.3 Multi-Sheet Schematics
- Sheet hierarchy (parent/child sheets)
- Hierarchical labels for inter-sheet connections
- Sheet navigator panel in sidebar

### 4.4 Export
- Netlist export (text format for SPICE simulation)
- BOM export (CSV via BomGenerator module)
- PDF/image export (screenshot capture via VM `-screenshot`)

**Deliverable:** Full save/load cycle. Design persists across sessions.

---

## Phase 5: PCB Layout View

**Goal:** Switch between schematic and PCB views, place footprints, route traces.

### 5.1 View Switching
- Tab bar: Schematic | PCB | 3D Viewer
- Keyboard: F1=Schematic, F2=PCB, F3=3D
- Each view has its own viewport state

### 5.2 Board Outline
- Draw board edge with click-and-drag
- Rectangular and polygon outlines

### 5.3 Footprint Placement
- Import netlist from schematic (forward annotation)
- Footprints from FootprintModel (IPC-7351 generators)
- Click to place, R to rotate, F to flip front/back
- Ratsnest lines showing unrouted connections

### 5.4 Interactive Routing
- X key activates route tool
- Click pad to start, click to place corners, click destination pad to end
- Push-and-shove obstacle avoidance (InteractiveRouter module)
- Layer switching: + and - keys, or V for via
- Track width from net class or manual override

### 5.5 Copper Pour
- Draw zone outline
- Assign to net (usually GND)
- Fill with thermal relief at pad connections

### 5.6 DRC
- Run DRC (Inspect menu or Ctrl+Shift+D)
- Violations shown as markers on board
- Violation list in bottom panel
- Click violation to zoom to location

### 5.7 Layer Manager
- Show/hide layers in sidebar
- Active layer selection
- Layer colors (use PcbRenderer color scheme)

**Deliverable:** Route a PCB from a schematic netlist with DRC checking.

---

## Phase 6: Component Library

**Goal:** Browse, search, and manage component library.

### 6.1 Library Browser Panel
- Sidebar panel showing library categories
- Filter by: resistor, capacitor, IC, connector, etc.
- Search by keyword, MPN, value
- Preview: symbol + footprint + 3D model

### 6.2 Built-in Library
- Passive: R (0402-2512), C (0402-1206), L (0402-1210)
- Discrete: diode (SOD-123), LED (0603/0805), BJT (SOT-23), MOSFET (SOT-23)
- IC: SOIC-8, TQFP-32/48/64, QFP-100, QFN-16/24/32, BGA grid
- Connectors: pin header (1x2 through 2x20), USB-C, JST
- Power: LDO (SOT-223, SOT-23-5), buck (SOIC-8)
- Each with symbol + footprint + SPICE model

### 6.3 KiCad Import
- Import .kicad_sym symbol libraries (KicadImporter module)
- Import .kicad_mod footprint libraries
- Conversion to Codex ComponentDef format

**Deliverable:** Browse and place components from a searchable library.

---

## Phase 7: 3D Board Viewer

**Goal:** Interactive 3D view of the PCB with components.

### 7.1 Board Mesh
- Extrude board outline into 3D slab (BoardMesh module)
- Layer coloring: green solder mask, copper traces, white silk

### 7.2 Component Models
- Parametric 3D for standard packages (box for ICs, cylinder for caps)
- Placed at footprint coordinates with rotation

### 7.3 Camera Controls
- Mouse orbit (drag to rotate)
- Scroll to zoom
- Middle-click to pan
- Home key: reset to top-down
- Preset views: top, bottom, front, back, isometric

### 7.4 GPU Rendering
- All 3D via gpu-tri through the triangle rasterizer
- Perspective projection with depth buffer
- Directional lighting (port-out-32 0x403-0x405)

**Deliverable:** Orbit a 3D rendered PCB board with placed components.

---

## Phase 8: Simulation

**Goal:** Run SPICE simulations from the schematic.

### 8.1 Simulation Setup
- Select components for simulation
- Add voltage/current sources
- Configure analysis type (DC, AC, Transient)
- Set parameters (start, stop, step, frequency range)

### 8.2 Run Simulation
- Generate SPICE netlist from schematic (SpiceNetlist module)
- Run DC operating point (DcAnalysis module)
- Run AC sweep (AcAnalysis module)
- Run transient (TransientAnalysis module)

### 8.3 Waveform Viewer
- Plot results in a bottom panel
- Time/frequency on X axis, voltage/current on Y
- Multiple traces with different colors
- Cursors for measurement (WaveformViewer module)
- Zoom and pan on waveform plot

**Deliverable:** Simulate a simple circuit and view waveforms.

---

## Phase 9: Manufacturing Output

**Goal:** Generate production-ready files.

### 9.1 Gerber Generation
- Per-layer Gerber RS-274X (GerberWriter module)
- Drill files (DrillWriter module)
- Preview in a Gerber viewer panel

### 9.2 BOM Generation
- Grouped by value/MPN (BomGenerator module)
- CSV and JSON output
- DNP marking from design variants

### 9.3 Pick-and-Place
- Centroid file for SMT assembly (PickAndPlace module)
- Front/back side separation

### 9.4 IPC-2581
- Single-file manufacturing package (Ipc2581Writer module)
- Includes BOM, placement, layers

### 9.5 Panelization
- Step-and-repeat array (Panelizer module)
- V-score and tab-route options

### 9.6 Signed Packages
- Trust-lattice signed output (SignedPackage module)
- DRC/ERC gate results embedded

**Deliverable:** Generate a complete manufacturing package from a finished design.

---

## Phase 10: Polish + Advanced Features

### 10.1 Design Variants
- Multiple BOM configurations from one schematic
- DNP marking per variant
- Variant switcher in toolbar

### 10.2 Cross-Probing
- Click component in schematic → highlight in PCB
- Click footprint in PCB → highlight in schematic
- Uses CrossProbe module

### 10.3 Diff Pair Routing
- Dedicated tool for USB, HDMI, PCIe differential pairs
- Length matching with serpentine tuning
- Impedance calculator in properties panel

### 10.4 Dark/Light Theme
- Theme switcher in View menu
- CircuitsTheme module already has color constants
- Add light theme variant

### 10.5 Keyboard Shortcut Reference
- Ctrl+F1 shows hotkey overlay
- Customizable shortcuts

---

## UI Layout Reference (KiCad 10 / Altium hybrid)

```
┌─────────────────────────────────────────────────────────────┐
│ Menu: File Edit View Place Inspect Tools Simulate Help      │
├─────────────────────────────────────────────────────────────┤
│ Toolbar: [New][Open][Save] | [Undo][Redo] | [Zoom+][-][Fit]│
│          | [Layer▼] [Width▼] | [DRC] [ERC] [Annotate]      │
├────┬────────────────────────────────────────────────┬───────┤
│    │                                                │ Props │
│ T  │                                                │ Panel │
│ o  │                                                │       │
│ o  │           Main Canvas                          │ Ref:  │
│ l  │           (Schematic or PCB)                   │ Val:  │
│    │                                                │ FP:   │
│ B  │                                                │ Pins: │
│ a  │                                                │       │
│ r  │                                                │ Net:  │
│    │                                                │ Class:│
├────┴────────────────────────────────────────────────┴───────┤
│ Tab: [Schematic] [PCB Layout] [3D Viewer] [Simulator]      │
├─────────────────────────────────────────────────────────────┤
│ Status: X:125.50mm Y:87.20mm | Grid:1.27mm | Tool:Wire    │
│         | DRC:0 ERC:0 | Unrouted:5 | zoom:100%             │
└─────────────────────────────────────────────────────────────┘
```

### Left Tool Bar (Schematic)
Select, Wire, Bus, Place Symbol, Place Power, Net Label, Global Label, No-Connect, Text, Measure

### Left Tool Bar (PCB)
Select, Route Track, Route Diff Pair, Place Via, Draw Zone, Place Footprint, Draw Line/Rect/Circle, Text, Measure, Dimension

### Right Properties Panel
Context-sensitive: shows properties of selected object.
- Component: ref, value, footprint, datasheet, fields
- Wire: net name, net class
- Track: width, layer, net, length
- Via: diameter, drill, layers
- Zone: net, priority, fill type, clearance
- Footprint: ref, value, position, rotation, side

---

## VM Requirements

- `-mem 3072` minimum (GPU cmd buffer at 0xBE000000)
- `-gop-width 1024 -gop-height 768` for full UI (or 640x480 for compact)
- RAM cap fix in codex-vm.c (CL 6240) prevents stack/GPU overlap
- Serial exit guard (CL 6240) keeps app alive after boot
- GPU region commit (CL 6240) backs the command buffer memory

---

## Technical Debt to Address

1. `& (show x)` pattern — every `show` after `&` needs parens (feedback memory saved)
2. `None` not `Nothing` — the Maybe empty constructor (feedback memory saved)
3. `list-length`/`list-at` loops, not `Cons`/`Nil` patterns (feedback memory saved)
4. No `\"` in strings — use single quotes
5. No multi-line `&` chains — keep on one line or use let bindings
6. `real-from-int`/`real-to-int` for numeric conversion
7. `poke-32` results must be chained (`s1 + poke-32 ...`) to prevent dead-code elimination
8. `port-out-32` results likewise must be chained

---

## Priority Order

Phases 1-3 are the critical path — they make the tool interactive and usable for schematic capture. Phase 4 (file I/O) makes work persistent. Phase 5 (PCB) is the second major feature. Phases 6-10 are progressive enhancement.

**Estimated scope:** ~15,000 additional lines of Codex across ~20 new chapters + modifications to existing chapters.

---

## Sources

- [KiCad 10 Schematic Editor](https://docs.kicad.org/10.0/en/eeschema/eeschema.html)
- [KiCad 10 PCB Editor](https://docs.kicad.org/10.0/en/pcbnew/pcbnew.html)
- [Altium Designer Properties Panel](https://www.altium.com/documentation/altium-designer/properties-panel)
- [KiCad 10.0 Release](https://www.kicad.org/blog/2026/03/Version-10.0.0-Released/)
