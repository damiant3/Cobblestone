# CVMM — Codex Virtual Machine Manager

## Vision

CVMM is not a VM manager. CVMM is the operating system's graphical
shell — the desktop environment, the system manager, the fleet
controller. It happens to boot inside a VM today because codex-vm is
our test bed. Tomorrow it runs on phones, on PCs with four monitors
and discrete GPUs, on headless servers in racks. The abstractions must
survive that transition without rewrites.

The browser is the first display target because TCP/HTML/WebGPU reaches
every device with a screen. But the same widget trees, the same state
models, the same manager modules will render natively through the
foreword Render pipeline when the GPU driver lands.

## Core Principle: Everything Is a Managed Resource

A file, a disk, a USB device, a network port, a running service, a
deployed VM, a GPU — they are all resources with a lifecycle. CVMM
presents them through a unified resource model:

- **Discover** — enumerate what exists
- **Inspect** — show properties and status
- **Act** — mount, unmount, start, stop, deploy, attach, detach
- **Monitor** — watch for changes, alert on conditions

Each manager module follows this pattern. The shell composes them into
a coherent workspace.

## Port Assignment

**Port 2682** — the CVMM management dashboard.

IANA removed this port assignment on 2002-04-30. It remains unassigned
(not "Reserved" like de-assigned ports, which RFC 6335 discourages
reusing). IANA has precedent for re-assigning removed ports: 2426
became VeloCloud (2014), 3001 became OrigoDB (2013), 3121 became
Pacemaker (2013). Port 2682 is in the registered range (1024-49151),
easy to remember, and has no active protocol behind it.

The TCP bridge between the CDX server and the PowerShell bridge uses
port 9100 (standard for all codex.OS apps). Port 2682 is the
user-facing HTTP port only.

## Architecture

### Three Layers (Today)

```
┌─────────────────────────────────────────────────┐
│  Browser (HTML/JS/CSS/WASM/WebGPU)              │
│  ┌────────────┐  ┌───────────────────────────┐  │
│  │ Shell UI   │  │ Display Canvas             │  │
│  │ (managers) │  │ (WebGPU / Canvas 2D)       │  │
│  └────────────┘  └───────────────────────────┘  │
├─────────────────────────────────────────────────┤
│  PowerShell Bridge (server.ps1)                  │
│  HTTP + WebSocket ↔ Framed TCP                   │
├─────────────────────────────────────────────────┤
│  codex.OS (bare metal in codex-vm)               │
│  ┌───────────────────────────────────────────┐  │
│  │ CVMM Server + Managers + Display Server   │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

### Two Layers (Tomorrow)

```
┌─────────────────────────────────────────────────┐
│  codex.OS (bare metal on real hardware)          │
│  ┌───────────────────────────────────────────┐  │
│  │ CVMM Shell + Managers → GPU Compositor    │  │
│  └───────────────────────────────────────────┘  │
│  Direct: framebuffer, USB HID, NVMe, GPU         │
└─────────────────────────────────────────────────┘
```

Same Codex code. Different render target.

## Module Map

### Core
| File | Purpose |
|------|---------|
| `CvmmTypes.codex` | Shared types: DeviceId, Endpoint, ResourceStatus, etc. |
| `CvmmState.codex` | Global application state, navigation, active views |
| `CvmmShell.codex` | Desktop shell: workspace, panels, app launcher, taskbar, system tray, notifications |
| `CvmmTheme.codex` | OS theme: dark/light, accent colors, responsive breakpoints |
| `CvmmDashboard.codex` | Home dashboard: system summary, quick actions, activity feed |

### System Managers
| File | Purpose |
|------|---------|
| `FileExplorer.codex` | File browser: tree nav, breadcrumb, icon/list/detail views, file ops |
| `DriveManager.codex` | Volumes, partitions, mount points, disk health, format/resize |
| `UsbManager.codex` | USB device tree, attach/detach, class/vendor info, power |
| `ProcessManager.codex` | Task manager: process list, CPU/memory per process, kill/priority |
| `ServiceManager.codex` | System services: start/stop/restart, dependencies, logs |
| `PortMonitor.codex` | Network ports: listeners, connections, firewall rules |
| `NetworkManager.codex` | NICs, IPs, DNS, routing tables, traffic stats |
| `DisplayManager.codex` | Monitors, resolution, DPI, GPU info, multi-monitor layout |

### Server and Fleet
| File | Purpose |
|------|---------|
| `ServerManager.codex` | Managed servers: web, proxy, DNS, DHCP — deploy/configure/monitor |
| `FleetManager.codex` | Device/VM fleet: discover, group, deploy to, health dashboard |
| `DeployManager.codex` | Deployment pipeline: build, package, distribute, rollback |

### Infrastructure
| File | Purpose |
|------|---------|
| `CvmmServer.codex` | Entry point: boots WebServer, registers routes, starts managers |
| `CvmmRoutes.codex` | API route dispatch: /api/fs/*, /api/drive/*, /api/usb/*, etc. |
| `CvmmDisplay.codex` | Display protocol: frame encoding, dirty-rect diffing, input relay |

### Tests
| File | Purpose |
|------|---------|
| `tests/TestTypes.codex` | Resource status transitions, endpoint formatting |
| `tests/TestFileExplorer.codex` | Path operations, breadcrumb, sort, filter |
| `tests/TestDriveManager.codex` | Volume model, capacity math, mount state |
| `tests/TestProcessManager.codex` | Process list operations, sorting, filtering |
| `tests/TestNetworkManager.codex` | IP formatting, subnet math, route selection |
| `tests/TestFleetManager.codex` | Device discovery, grouping, health rollup |
| `tests/TestShell.codex` | Navigation state, panel layout, notification queue |

## Display Protocol

Binary framed (4-byte length + tag + body), same framing as WebServer.

### Frame Updates (Guest → Host)
| Tag | Name | Body |
|-----|------|------|
| 0x10 | FullFrame | width(u16) height(u16) pixels(RGBA) |
| 0x11 | DirtyRect | x(u16) y(u16) w(u16) h(u16) pixels(RGBA) |
| 0x12 | Cursor | x(u16) y(u16) shape(u8) |
| 0x13 | Bell | (empty) |
| 0x14 | Resize | width(u16) height(u16) |

### Input Events (Host → Guest)
| Tag | Name | Body |
|-----|------|------|
| 0x20 | KeyDown | scancode(u16) modifiers(u8) |
| 0x21 | KeyUp | scancode(u16) modifiers(u8) |
| 0x22 | MouseMove | x(u16) y(u16) |
| 0x23 | MouseDown | x(u16) y(u16) button(u8) |
| 0x24 | MouseUp | x(u16) y(u16) button(u8) |
| 0x25 | MouseScroll | dx(i16) dy(i16) |
| 0x26 | Clipboard | text(utf8) |

## Control API

All JSON over WebServer route dispatch.

| Prefix | Manager | Endpoints |
|--------|---------|-----------|
| `/api/system` | Dashboard | status, uptime, summary |
| `/api/fs` | FileExplorer | list, read, write, mkdir, delete, move, copy, stat |
| `/api/drive` | DriveManager | list, mount, unmount, format, health |
| `/api/usb` | UsbManager | list, attach, detach, info |
| `/api/process` | ProcessManager | list, kill, priority, stats |
| `/api/service` | ServiceManager | list, start, stop, restart, logs |
| `/api/port` | PortMonitor | list, listen, close, firewall |
| `/api/net` | NetworkManager | nics, ips, dns, routes, stats |
| `/api/display` | DisplayManager | monitors, resolution, gpu |
| `/api/server` | ServerManager | list, deploy, configure, start, stop, logs |
| `/api/fleet` | FleetManager | discover, list, group, deploy, health |
| `/api/deploy` | DeployManager | build, package, distribute, rollback, status |

## Phases

### Phase 1 — Skeleton + Core Managers
All data models, all widget builders, all route stubs. File explorer,
drive manager, USB manager, process manager, service manager working
with mock data. Dashboard with system summary. Shell with navigation.

### Phase 2 — Live Data + Display
Wire managers to real OS syscalls. Display protocol streaming. Input
relay. Port and network monitors with real data.

### Phase 3 — Fleet + Deploy
Multi-device discovery. Fleet dashboard. Deployment pipeline.
Server management (proxy, web, DNS).

### Phase 4 — Multi-Monitor + GPU
Native render path (bypass HTML, direct to GPU compositor). Multi-monitor
layout. Phone/tablet responsive layouts. GPU acceleration.
