# Codex Circuits

A Codex-native electronic design automation (EDA) suite for schematic
capture, circuit simulation, PCB layout, 3D board visualization,
manufacturing output, and component library management. Runs bare-metal
via codex-vm with GPU-accelerated routing and rendering, DiskFacts
persistence, trust-lattice-signed design packages, and compile-time
safety guarantees that no existing EDA tool can offer.

Inspired by KiCad 10, Altium Designer, and OrCAD -- but rebuilt from
first principles in Codex, leveraging dependent types for electrical
rule enforcement, linear types for resource-safe hardware interfaces,
effect types for simulation isolation, and the full Codex platform
(GPU compute, bare-metal OS, content-addressed repository, literate
source, IoT board support).

## Architecture

The app is organized into seven major subsystems, each a Codex quire
(chapter group), plus shared infrastructure:

```
apps/circuits/
  SchematicEditor/     Schematic capture and symbol placement
  SymbolEditor/        Symbol creation and library management
  Simulator/           SPICE-class circuit simulation engine
  PcbEditor/           PCB layout, routing, copper pour, DRC
  FootprintEditor/     Footprint creation and pad geometry
  BoardViewer/         3D board visualization and mechanical fit
  Manufacturing/       Gerber, ODB++, IPC-2581, BOM, pick-and-place
  Core/                Shared types, netlist, design rules, units
  opening.codex        App entry point and workspace manager
  codex.project.json   Project metadata
```

## Feature Summary

### Schematic Capture
- Hierarchical multi-sheet schematics with cross-references
- Symbol placement, wire routing, bus notation, power symbols
- Design variants (shared schematic, different component properties)
- Electrical Rules Check (ERC) with dependent-type net constraints
- Hop-over display for unconnected wire crossings
- Inline annotation editor with CPL prose blocks
- CSV import/export for pin tables
- Drag-and-drop image insertion

### Circuit Simulation
- Full SPICE-class analog simulator (DC operating point, AC sweep,
  transient, noise, Monte Carlo, parametric sweep)
- Mixed-signal simulation (digital + analog)
- Interactive waveform viewer with cursors, measurements, FFT
- SPICE model library with manufacturer device models
- Effect-typed simulation: `[Simulate]` effect isolates simulation
  state from design state -- impossible to accidentally mutate the
  schematic during a sim run
- GPU-accelerated matrix solve for large circuits via `[Device]` effect

### PCB Layout
- Up to 64 copper layers + 64 technical layers
- Interactive push-and-shove router with DRC-aware obstacle avoidance
- GPU-accelerated autorouter (topological + A* hybrid, `[Device]` kernels)
- Differential pair routing with length matching and skew constraints
- Impedance-controlled trace width calculation (microstrip, stripline,
  coplanar waveguide) with stack-up editor
- Copper pour with thermal relief, zone priority, hatched/solid fill
- Design Blocks (reusable board layout fragments)
- Pin and gate swap with forward/back annotation
- Time-domain tuning with per-layer tuning profiles
- Teardrops, rounded traces, native rounded rectangles
- Barcode generation (QR, Code128, DataMatrix)

### Design Rule Checking (DRC)
- Dependent-type net constraints: `NetClass "USB" { impedance = Ohm 90,
  differential = True, max-skew = Picosecond 5 }` -- violations are
  compile errors, not post-layout warnings
- Clearance, width, via size, annular ring, hole-to-hole, silk-to-pad
- Signal integrity: return path continuity, split plane crossing
- Thermal: copper balance per layer, thermal relief sizing
- Manufacturability: minimum feature size per fab capability profile
- Graphical rule editor (visual DRC rule construction)
- Suggested fixes with one-click apply

### Component Libraries
- Unified symbol + footprint + 3D model + SPICE model per component
- Content-addressed library (every component version is immutable,
  trust-lattice signed by the author)
- Parametric search: "capacitor, 100nF, 0402, X7R, 16V, in stock"
- Manufacturer part number cross-reference (Digi-Key, Mouser, LCSC)
- Automatic footprint generation from IPC-7351 land patterns
- KiCad library import (symbols, footprints, 3D models)

### 3D Board Visualization
- Real-time 3D rendering via GPU rasterizer (I/O ports 0x400-0x40F)
- Component placement with collision detection
- Mechanical enclosure import (step file mesh approximation)
- Cross-probing: click a component in 3D, highlight in schematic/PCB
- Raytraced render for presentation images
- 3D PDF and STEP export

### Manufacturing Output
- Gerber RS-274X (per-layer copper, mask, silk, paste, drill)
- Excellon drill files (plated, non-plated, via)
- ODB++ (single-archive intelligent format)
- IPC-2581 (open single-file manufacturing package)
- Bill of Materials with grouped/ungrouped modes, CSV/JSON/HTML
- Pick-and-place centroid file (component XY, rotation, side)
- Assembly drawings with reference designator callouts
- Panelization (step-and-repeat, V-score, tab-route)
- Design-for-manufacturing (DFM) audit against fab house profiles
- All output files are trust-lattice signed (CDX package with
  Ed25519 signature, SHA-256 content hash, capability manifest)

### Workspace and Collaboration
- Project manager with schematic, PCB, library, sim, output panes
- Content-addressed version control via Codex repository protocol
  (facts, proposals, verdicts -- not file-based diffs)
- Real-time multi-user editing via gossip protocol mesh
- Design review with inline annotations (CPL prose, not comments)
- Change proposals with ERC/DRC gate (proposal cannot be accepted
  if it introduces rule violations)

## What Codex Brings That No EDA Tool Has

### 1. DRC as Type Errors (Dependent Types)

Traditional EDA tools run DRC as a batch post-processing step. You
design, then check, then fix, then re-check. Violations are warnings
in a list that you can ignore.

In Codex Circuits, critical design rules are expressed as dependent
types. A `Net` carries its electrical constraints in its type. A
`Trace` that violates its net's impedance constraint cannot be
constructed -- it is a compile error, not a warning.

```
NetClass "DDR4-DQ" {
  impedance = Ohm 40,
  differential = False,
  max-length = Millimeter 75,
  max-skew = Picosecond 5,
  min-spacing = Mil 5
}
```

The type checker verifies that every trace segment assigned to this
net class satisfies these constraints. A trace that is too narrow for
40-ohm impedance given the stack-up cannot exist in the design.

### 2. Simulation Isolation (Effect Types)

A SPICE simulation reads the schematic but must never modify it. In
traditional tools, simulation and editing share mutable state -- a
category of bugs that has plagued EDA software for decades.

In Codex Circuits, simulation runs under the `[Simulate]` effect.
The simulator can read component values and netlist topology but
cannot write to schematic state. This is enforced by the type system.
A function that modifies the schematic has the `[SchematicEdit]`
effect; one cannot be called from within the other.

### 3. Resource-Safe Hardware Probing (Linear Types)

When Codex Circuits drives real measurement hardware (oscilloscopes,
logic analyzers, VNAs) via the IoT board support, the instrument
connection is a `linear InstrumentHandle`. The handle must be
acquired, used, and released exactly once. A test script that opens
a scope connection and forgets to close it is a compile error.

### 4. GPU-Accelerated Routing and Simulation

The autorouter and SPICE matrix solver use `[Device]`-effected GPU
kernels compiled through the PTX/SPIR-V plug pipeline. Same trust
chain, same effect-typed safety, same signed CDX. The router kernel
runs on the RTX 4060 Ti during development and on ARM Mali or
Qualcomm Adreno GPUs on edge deployment hardware.

### 5. Signed Manufacturing Packages

Every manufacturing output (Gerber archive, IPC-2581 package, BOM)
is wrapped in a signed CDX envelope with:
- Ed25519 signature (who generated this)
- SHA-256 content hash (tamper detection)
- Capability manifest (what this package authorizes)
- Trust lattice provenance (which design revision, which DRC run)

A fab house receiving a Codex manufacturing package can verify that
the files were generated from a design that passed all DRC rules,
was signed by an authorized engineer, and has not been modified
since generation. No other EDA tool provides this.

### 6. Content-Addressed Component Libraries

Components in the library are immutable, content-addressed facts in
the Codex repository. When you place a 100nF 0402 capacitor, you are
placing a specific, hash-identified version of that component. If
someone publishes a corrected footprint, it is a new fact that
supersedes the old one -- but your design still references the version
you placed, not the latest. Library updates are explicit proposals
with engineering review, not silent overwrites.

### 7. Literate Design Documentation

The schematic is not just a diagram. Each sheet can contain CPL prose
blocks that describe the design intent in load-bearing English:

```
We say:
  The USB Type-C controller receives differential pairs D+ and D-
  from the connector at impedance 90 ohms. The ESD protection
  diodes must be placed within 5 millimeters of the connector pads
  to minimize stub length.
```

This prose is parsed by the compiler. The constraints it expresses
(90 ohms, 5mm placement radius) are checked against the actual
layout. If the ESD diodes are 7mm from the connector, the compiler
reports a violation.

### 8. Compliance-Ready Designs

For IoT and safety-critical hardware, Codex Circuits integrates with
the compliance evidence pipeline. The design package includes:
- CRA compliance evidence (design-by-construction arguments)
- IEC 62443 evidence (if the firmware is also Codex)
- Full audit trail (every design change is a fact in the repository)
- SBOM generated from the component library (exact part numbers,
  versions, suppliers)

### 9. Board-to-Firmware Continuity

Codex Circuits and the Codex compiler share the same platform. A
board designed in Circuits with an STM32F4 MCU can have its firmware
written in Codex, compiled by the seed, and flashed via the OTA
update mechanism -- all within one signed trust chain. The pin
assignments in the schematic are available as typed constants in the
firmware source. Change a pin assignment in the schematic, and the
firmware sees a type error if it uses the old pin.

### 10. Real-Time Collaboration on Bare Metal

The gossip protocol and Raft consensus from the OS layer enable
multi-user editing without a cloud server. Two engineers on the same
mesh network can edit the same PCB layout simultaneously, with
conflict resolution handled by the repository protocol (proposals
and verdicts, not merge conflicts).

## Physical Units

All dimensional quantities use Codex unit types with compile-time
safety:

```
Millimeter = unit Integer
Mil = unit Integer
Ohm = unit Integer
Farad = unit Integer
Henry = unit Integer
Ampere = unit Integer
Volt = unit Integer
Watt = unit Integer
Hertz = unit Integer
Picosecond = unit Integer
Kelvin = unit Integer
```

Cross-unit arithmetic is a type error. `Millimeter 5 + Mil 10` does
not compile. You must convert explicitly:
`Millimeter 5 + mil-to-mm (Mil 10)`.

## Module Plan (50+ chapters)

### Core/ (8 chapters)
- **CircuitUnits** -- Physical unit types (mm, mil, ohm, farad, etc.)
- **NetlistModel** -- Net, pin, connection, netlist graph
- **DesignRules** -- DRC rule types, net class constraints, clearances
- **StackUp** -- PCB layer stack definition with material properties
- **ComponentModel** -- Unified component (symbol + footprint + model)
- **DesignVariants** -- Variant management (shared base, property deltas)
- **ProjectModel** -- Project, sheet hierarchy, cross-references
- **CircuitSerializer** -- JSON/binary serialization for all types

### SchematicEditor/ (7 chapters)
- **SchematicModel** -- Sheet, wire, bus, junction, label, power symbol
- **SchematicRenderer** -- Wire rendering, symbol painting, hop-overs
- **SchematicRouter** -- Auto-wire routing with junction insertion
- **SchematicErc** -- Electrical rules check (pin compatibility, floating nets)
- **SchematicAnnotate** -- Reference designator assignment and renumbering
- **SchematicVariants** -- Variant overlay on schematic properties
- **SchematicEditor** -- Editor state machine, tools, keyboard shortcuts

### SymbolEditor/ (3 chapters)
- **SymbolModel** -- Pin geometry, body graphics, alternate styles
- **SymbolRenderer** -- Pin rendering, body shapes, text labels
- **SymbolEditor** -- Editor state, pin table, CSV import/export

### Simulator/ (8 chapters)
- **SpiceNetlist** -- SPICE netlist generation from schematic
- **SpiceModels** -- Device model library (MOSFET, BJT, diode, etc.)
- **DcAnalysis** -- DC operating point and DC sweep solver
- **AcAnalysis** -- AC small-signal frequency response
- **TransientAnalysis** -- Time-domain transient solver (Gear/Trapezoidal)
- **MonteCarloSim** -- Statistical variation analysis
- **WaveformViewer** -- Interactive plot with cursors, FFT, measurements
- **SimKernels** -- GPU-accelerated sparse matrix solve (`[Device]` effect)

### PcbEditor/ (10 chapters)
- **PcbModel** -- Board outline, layers, tracks, vias, zones, footprints
- **PcbRenderer** -- Multi-layer rendering with alpha blending, ratsnest
- **InteractiveRouter** -- Push-and-shove router with DRC-aware shove
- **AutoRouter** -- GPU-accelerated topological + A* hybrid router
- **DifferentialPairs** -- Diff pair routing, length matching, skew tuning
- **CopperPour** -- Zone fill with thermal relief, priority, hatching
- **LengthTuning** -- Serpentine/trombone tuning with time-domain targets
- **PcbDrc** -- Full design rule check engine (clearance, width, impedance)
- **DesignBlocks** -- Reusable board layout fragments
- **PcbEditor** -- Editor state, tool palette, layer manager

### FootprintEditor/ (3 chapters)
- **FootprintModel** -- Pad geometry, courtyard, fab layer, 3D anchor
- **IpcLandPatterns** -- IPC-7351 automatic footprint generation
- **FootprintEditor** -- Editor state, pad table, courtyard calculator

### BoardViewer/ (4 chapters)
- **BoardMesh** -- PCB-to-3D mesh conversion (layer extrusion, vias)
- **ComponentMesh** -- 3D component models (parametric + imported)
- **BoardRenderer3D** -- GPU-accelerated 3D rendering with lighting
- **MechanicalFit** -- Enclosure overlay and interference detection

### Manufacturing/ (7 chapters)
- **GerberWriter** -- RS-274X Gerber generation per layer
- **DrillWriter** -- Excellon drill file generation
- **OdbWriter** -- ODB++ archive generation
- **Ipc2581Writer** -- IPC-2581 single-file package generation
- **BomGenerator** -- BOM with grouping, cross-reference, multi-format
- **PickAndPlace** -- Centroid file with rotation and side assignment
- **Panelizer** -- Step-and-repeat, V-score, tab-route panelization

## Dependencies

```json
{
  "dependencies": [
    "codex.foreword",
    "codex.foreword.ui",
    "codex.foreword.math",
    "codex.foreword.encode",
    "codex.foreword.gpu",
    "codex.foreword.game",
    "codex.foreword.signal",
    "codex.os.kernel"
  ]
}
```

Key foreword usage:
- **LinearAlgebra / Matrix4** -- coordinate transforms, 3D projection
- **Geometry / Bezier / Spline** -- curve rendering, trace smoothing
- **Quadtree / Octree** -- spatial indexing for component lookup
- **Rasterizer / Scene3D** -- 3D board rendering
- **Fft** -- waveform FFT in simulator viewer
- **GpuEffect / DeviceBuffer** -- GPU autorouter and SPICE solver
- **Json / Csv** -- import/export
- **Sha256 / Ed25519** -- signed manufacturing packages
- **FactStore** -- content-addressed component library

## Rendering Targets

| Target | Backend | Use Case |
|--------|---------|----------|
| Bare metal (codex-vm) | GOP framebuffer + GPU rasterizer | Primary dev environment |
| Web (HTML plug) | Canvas 2D / WebGL | Browser-based viewer/editor |
| WinForms (WinForms plug) | GDI+ / Direct2D | Windows desktop app |
| UEFI console | GOP framebuffer | On-device board review |

## Completeness

0% -- This is the design document. No code has been written yet.

## Codex Conformance

Full -- Will be written entirely in Codex. UI via Widget/Theme foreword.
Simulation engine native (no ngspice dependency). GPU compute via
`[Device]` effect. Persistence via DiskFacts. Manufacturing output
via native writers (no external libraries).
