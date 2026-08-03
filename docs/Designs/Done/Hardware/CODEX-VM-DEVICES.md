# codex-vm Device Emulation Roadmap

**Created**: 2026-05-23
**Status**: Active -- analysis complete, implementation not started

## Current State

codex-vm emulates 12 device classes in ~2900 lines of C. The Codex OS
kernel has drivers for several devices that codex-vm does not yet
emulate (xHCI, USB Audio, Bochs VBE graphics, USB Mass Storage). This
doc plans the additions needed to close those gaps and bring up new
device classes: audio, GPU acceleration, USB, cameras, and microphones.

### What codex-vm emulates today

| Device | I/O Range | Lines | Notes |
|--------|-----------|-------|-------|
| VGA text mode | 0xB8000 MMIO, 0x3C0-0x3D5 | ~80 | 80x25 character buffer |
| GOP framebuffer | 0x40000000 MMIO | ~120 | 640/800/1024, 32-bit XRGB |
| PS/2 keyboard | 0x60, 0x64 | ~100 | Scancode Set 1, 64-entry queue |
| PS/2 mouse | 0x60, 0x64 | ~40 | 3-byte packets, button tracking |
| IDE PIO | 0x1F0-0x1F7, 0x3F6 | ~80 | LBA read from disk image |
| NE2K NIC | 0x300-0x31F | ~400 | Full NE2000, user-mode NAT |
| Dual 8259 PIC | 0x20-0x21, 0xA0-0xA1 | ~80 | ICW/OCW, masking, EOI |
| PIT 8253 | 0x40-0x43 | ~10 | Host-driven timer, no register state |
| CMOS RTC | 0x70-0x71 | ~30 | BCD time from host clock |
| Serial COM1/COM2 | 0x3F8, 0x2F8 | ~120 | 16550 UART over TCP sockets |
| UEFI firmware | 0xF0000-0xF1FFF | ~500 | Trap-page dispatch, 35 functions |
| Debug exit | 0xF4 | ~5 | Exit code to host |

### Kernel drivers that exist but have no codex-vm backend

| Driver | File | What it does |
|--------|------|-------------|
| xHCI | Xhci.codex | PCI discovery, capability/operational regs, TRB rings |
| USB Audio | UsbAudio.codex | Audio class descriptors, stream setup |
| USB Mass Storage | UsbMassStorage.codex | BBB protocol, SCSI READ/WRITE_10 |
| Bochs VBE | VgaGraphics.codex | VBE registers 0x01CE/0x01CF, framebuffer at 0xFD000000 |
| PCI | Pci.codex | CAM via 0xCF8/0xCFC |
| GPU Bridge | GpuBridge.codex | COM3 serial to CUDA proxy |

## Roadmap

### Tier 1: Wire up existing kernel drivers (~600 lines C)

These drivers already exist in the kernel. The only missing piece is
the codex-vm hardware emulation behind them.

#### 1A. PCI Configuration Space (~150 lines)

The kernel's Pci.codex uses CAM (ports 0xCF8/0xCFC) to enumerate
devices. codex-vm doesn't handle these ports -- all PCI reads return
0xFF. Adding a minimal PCI config space with a device table unlocks
every PCI-based device below.

**Implementation**: static device table (bus 0, up to 8 devices).
Each entry: vendor/device ID, class/subclass, BARs, interrupt line.
Port 0xCF8 write latches address. Port 0xCFC read returns the
corresponding config register from the table.

Populate with: NE2K (already at ISA), Bochs VGA (slot 1), xHCI
(slot 2), Intel HDA (slot 3, future).

#### 1B. Bochs VBE Display (~100 lines)

The kernel's VgaGraphics.codex programs VBE registers at ports
0x01CE (index) and 0x01CF (data) to set resolution and enable a
linear framebuffer. codex-vm already has a GOP framebuffer at
0x40000000 with host-side rendering.

**Implementation**: handle VBE index/data ports. On mode enable,
resize the host window and switch rendering from VGA text to the
framebuffer. Guest writes pixels to the VBE framebuffer address
(configure via PCI BAR, default 0xFD000000 or reuse 0x40000000).

This replaces the UEFI-only GOP path with a bare-metal graphics
path that works without UEFI mode.

#### 1C. xHCI Controller Stub (~350 lines)

The kernel's Xhci.codex discovers the controller via PCI, reads
capability registers, and sets up command/event/transfer rings.
Emulating enough xHCI to enumerate one USB device unlocks USB
keyboard, USB mass storage, and USB audio.

**Implementation**:
- PCI device at slot 2 (class 0x0C, subclass 0x03, progif 0x30)
- Capability registers: CAPLENGTH, HCIVERSION, HCSPARAMS1
  (MaxSlots, MaxPorts), HCSPARAMS2, HCCPARAMS1, DBOFF, RTSOFF
- Operational registers: USBCMD (run/stop, HCRST), USBSTS (HCH),
  PAGESIZE, DNCTRL, CRCR, DCBAAP, CONFIG
- Port registers: PORTSC (CCS, PED, speed)
- One root hub port with a permanently-attached device
- Command ring: process ENABLE_SLOT, ADDRESS_DEVICE,
  CONFIGURE_ENDPOINT, EVALUATE_CONTEXT TRBs
- Event ring: post completion events
- Transfer ring: process NORMAL/SETUP/DATA/STATUS TRBs

This is the single largest addition but unlocks all USB devices.

### Tier 2: Audio (~400 lines C)

#### 2A. PC Speaker (~30 lines)

Trivial. Port 0x61 bits 0-1 control PIT channel 2 gate and speaker
data. PIT channel 2 (port 0x42) sets frequency. Host side: call
`Beep()` Win32 API or generate a square wave to the Windows audio
API. No guest driver needed.

#### 2B. Intel HDA Controller (~370 lines)

The kernel's UsbAudio.codex already handles USB Audio Class, but a
native audio controller is more practical for codex-vm. Intel HDA
is the modern standard.

**Implementation**:
- PCI device at slot 3 (class 0x04, subclass 0x03)
- MMIO BAR at a fixed address (e.g., 0xFE000000)
- Registers: GCAP, VMIN, VMAJ, GCTL (CRST), WAKEEN, STATESTS,
  INTCTL, INTSTS, CORBLBASE/CORBUBASE/CORBWP/CORBRP (command ring),
  RIRBLBASE/RIRBUBASE/RIRBWP (response ring)
- One codec (address 0) with a minimal widget tree:
  - Root node (vendor/device ID)
  - Audio Function Group
  - DAC widget (output, PCM format)
  - Pin widget (speaker, connected)
- Stream descriptors: SDCTL, SDSTS, SDLPIB, SDCBL, SDLVI, SDFMT,
  SDBDPL/SDBDPU (buffer descriptor list)
- Host side: read BDL entries, mix PCM samples, output via
  Windows WASAPI or waveOut

CORB/RIRB is simpler than xHCI rings. The codec tree is static.
The main complexity is DMA: reading buffer descriptors and streaming
PCM data to the host audio API.

**Microphone**: Same HDA controller, second stream descriptor
configured for input. The codec widget tree adds an ADC widget and
a microphone pin. Host side: capture via WASAPI loopback or a
physical input device.

### Tier 3: Camera (~500 lines C, requires Tier 1C)

#### 3A. USB Video Class (UVC) Device

Emulate a UVC-compliant USB camera on the xHCI bus. The guest sees
a standard USB video device with no special driver needed (UVC is
class-compliant).

**Implementation**:
- xHCI device on port 1 (or a second port)
- USB descriptors: device, config, interface association,
  video control interface, video streaming interface
- Control endpoint: UVC-specific requests (GET_CUR, SET_CUR for
  brightness, contrast, etc.)
- Isochronous endpoint: streams MJPEG or uncompressed YUV frames
- Host side: capture from Windows Media Foundation
  (IMFSourceReader) or a test pattern generator

**Dependencies**: requires Tier 1C (xHCI) and isochronous transfer
support in the xHCI emulation.

**Practical note**: camera is a late-stage feature. A test pattern
generator (colored bars, timestamp overlay) is sufficient for
initial development without requiring a physical camera.

### Tier 4: Additional Devices (easy wins, independent)

| Device | Lines | PCI? | Notes |
|--------|-------|------|-------|
| HPET (high-precision timer) | ~60 | No | MMIO at 0xFED00000, replaces PIT for precision timing |
| IOAPIC | ~100 | No | MMIO at 0xFEC00000, needed for MSI and multi-vector interrupts |
| virtio-rng | ~150 | Yes | Single virtqueue, reads host RNG |
| ACPI tables (RSDP/RSDT/FADT/MADT) | ~200 | No | Static tables in guest memory, needed for SMP discovery |
| TPM 2.0 CRB | ~200 | No | MMIO at 0xFED40000, command/response buffer interface |
| SMBIOS/DMI tables | ~100 | No | Static tables, system information (board, BIOS version) |

## Priority Order

1. **PCI config space** (1A) -- prerequisite for everything PCI-based
2. **Bochs VBE** (1B) -- bare-metal graphics without UEFI
3. **PC speaker** (2A) -- trivial, instant gratification
4. **xHCI stub** (1C) -- unlocks all USB devices
5. **Intel HDA** (2B) -- real audio output + microphone input
6. **HPET + IOAPIC** (Tier 4) -- precision timing and modern interrupts
7. **UVC camera** (3A) -- requires xHCI

## Estimated Size

| Phase | Lines C | Cumulative |
|-------|---------|------------|
| PCI config space | ~150 | 3,080 |
| Bochs VBE | ~100 | 3,180 |
| PC speaker | ~30 | 3,210 |
| xHCI stub | ~350 | 3,560 |
| Intel HDA | ~370 | 3,930 |
| UVC camera | ~500 | 4,430 |
| Tier 4 easy wins | ~810 | 5,240 |
| **Total** | **~2,310** | **~5,240** |

codex-vm grows from ~2,930 lines to ~5,240 lines -- still a single
file, still a small C program.

## What We Don't Build

- **Full xHCI with hot-plug and hub support.** One root port, one
  device per port, no hubs.
- **3D GPU acceleration.** Bochs VBE gives us a linear framebuffer.
  The GPU bridge (COM3 to CUDA proxy) handles compute. No OpenGL/
  Vulkan passthrough.
- **WiFi/Bluetooth.** The NE2K NIC with user-mode NAT already
  provides networking. WiFi adds enormous complexity (802.11 state
  machine, WPA) for no benefit in a VM.
- **AHCI/NVMe.** IDE PIO is sufficient for disk I/O at VM speeds.
  The kernel's DriveManager already abstracts over the block layer.
- **Full ACPI.** Static tables for device discovery only. No AML
  interpreter, no power management, no sleep states.

## References

- `tools/codex-vm.c` -- current VM implementation (~2,930 lines)
- `codex/os/kernel/Xhci.codex` -- xHCI driver (discovery + rings)
- `codex/os/kernel/UsbAudio.codex` -- USB Audio Class driver
- `codex/os/kernel/VgaGraphics.codex` -- Bochs VBE driver
- `codex/os/kernel/Pci.codex` -- PCI enumeration driver
- `codex/os/kernel/GpuBridge.codex` -- GPU compute bridge
- Intel HDA spec: https://www.intel.com/content/dam/www/public/us/en/documents/product-specifications/high-definition-audio-specification.pdf
- xHCI spec: https://www.intel.com/content/dam/www/public/us/en/documents/technical-specifications/extensible-host-controler-interface-usb-xhci.pdf
- USB Video Class spec: https://www.usb.org/document-library/video-class-v15-document-set
- OSDev PC Speaker: https://wiki.osdev.org/PC_Speaker
- OSDev Bochs VBE: https://wiki.osdev.org/Bochs_VBE_Extensions
