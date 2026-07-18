# Commodore 64 Emulator — Design and Plan

A cycle-approximate Commodore 64 emulator written entirely in Codex,
compiled to CDX, and running on bare metal inside codex-vm. No
Windows, no C, no borrowed substrate. The VIC-II renders to the GOP
framebuffer, the SID synthesizes to HDA audio, and the PS/2 keyboard
maps to the C64 keyboard matrix.

A Commodore 64 running inside a self-sustaining compiler's own OS.

---

## Architecture

```
codex-vm (host)
  │
  ├─ GOP framebuffer (0xBF000000)  ← VIC-II writes pixels here
  ├─ HDA audio (48 kHz stereo)    ← SID writes PCM samples here
  ├─ PS/2 keyboard (port 0x60)    ← scancodes → C64 keyboard matrix
  ├─ IDE disk (port 0x1F0)        ← .prg / .d64 file loading
  └─ GPU rasterizer (0xBE000000)  ← optional: hardware-accelerated sprites
```

The emulator is a Codex application in `apps/c64/`, compiled as a
standard CDX binary, booted with `codex-vm -kernel c64.cdx -gop
-mem 3072 -disk game.d64`.

---

## Phase 1 — CPU and Memory (the core loop)

### MOS 6502 CPU

The 6502 has 56 unique instructions across 13 addressing modes,
producing ~151 valid opcodes. It is one of the simplest CPUs ever
made: 8-bit data bus, 16-bit address bus, 3 registers (A, X, Y),
an 8-bit stack pointer, a status register (N, V, -, B, D, I, Z, C),
and a 256-byte hardware stack at $0100-$01FF.

Implementation: a `when opcode` dispatch in a tight loop. Each
opcode handler computes the effective address (per addressing mode),
performs the operation, updates flags, and advances the cycle counter.

Addressing modes:
- Immediate (#$nn)
- Zero page ($nn)
- Zero page,X / Zero page,Y ($nn,X / $nn,Y)
- Absolute ($nnnn)
- Absolute,X / Absolute,Y ($nnnn,X / $nnnn,Y)
- Indirect ($nnnn) — JMP only
- Indexed indirect (($nn,X))
- Indirect indexed (($nn),Y)
- Relative (branches, signed 8-bit offset)
- Implied / Accumulator

Instructions by category:
- **Load/Store**: LDA, LDX, LDY, STA, STX, STY
- **Transfer**: TAX, TAY, TXA, TYA, TSX, TXS
- **Arithmetic**: ADC, SBC (with decimal mode)
- **Logic**: AND, ORA, EOR
- **Shift/Rotate**: ASL, LSR, ROL, ROR
- **Compare**: CMP, CPX, CPY
- **Branch**: BCC, BCS, BEQ, BNE, BMI, BPL, BVC, BVS
- **Jump**: JMP, JSR, RTS, RTI
- **Stack**: PHA, PLA, PHP, PLP
- **Flags**: CLC, SEC, CLI, SEI, CLD, SED, CLV
- **Other**: BRK, NOP, BIT, INC, DEC, INX, INY, DEX, DEY

Cycle counting: each instruction takes a known number of cycles
(2-7). Page-crossing penalties (+1 cycle) on indexed modes. Branch
taken penalties (+1, +2 if page cross). The emulator counts cycles
per instruction; the main loop runs the CPU until cycles reach the
next VIC-II event (raster line, sprite fetch, etc.).

### Memory Map (64 KB)

```
$0000-$00FF   Zero page (256 bytes, fast access)
$0100-$01FF   Stack (256 bytes, hardware stack pointer)
$0200-$03FF   OS workspace
$0400-$07FF   Screen RAM (40×25 = 1000 bytes + spare)
$0800-$9FFF   BASIC program area (~38 KB free)
$A000-$BFFF   BASIC ROM (8 KB) / RAM (banked)
$C000-$CFFF   Upper RAM (4 KB)
$D000-$D3FF   VIC-II registers (banked over char ROM)
$D400-$D7FF   SID registers
$D800-$DBFF   Color RAM (1000 nybbles)
$DC00-$DCFF   CIA 1 (keyboard, joystick, timers)
$DD00-$DDFF   CIA 2 (serial bus, user port, VIC bank)
$E000-$FFFF   KERNAL ROM (8 KB) / RAM (banked)
```

Bank switching via $0001 (CPU I/O port):
- Bits 0-2 select ROM/RAM configuration
- BASIC ROM, KERNAL ROM, char ROM, and I/O can each be
  independently mapped in or out

ROM images: BASIC ($A000, 8 KB), KERNAL ($E000, 8 KB), and
character generator ($D000, 4 KB) are loaded from the IDE disk
or embedded in the CDX as static data. These are well-documented
and freely available.

### .PRG Loader

A .prg file is the simplest C64 binary format: 2-byte little-endian
load address, then raw bytes. Load: read the address from bytes 0-1,
copy the rest to that address in C64 RAM, set the PC.

### Deliverable

Phase 1 is done when: a .prg containing hand-assembled 6502 that
writes characters to screen RAM ($0400) and color RAM ($D800)
produces visible colored text on the GOP framebuffer. No sprites,
no scrolling, no audio — just the CPU, memory, bank switching,
and a minimal VIC-II that reads screen RAM and renders characters
using the character ROM font.

---

## Phase 2 — VIC-II Display

The VIC-II (MOS 6569 PAL / 6567 NTSC) generates the C64's video
output. It operates in lockstep with the CPU — the VIC-II steals
cycles from the CPU during badlines (character pointer fetches) and
sprite fetches.

### Display Modes

| Mode | Resolution | Colors | Bits/cell |
|------|-----------|--------|-----------|
| Standard character | 320×200 | 16 fg + 1 bg per char | 1 |
| Multicolor character | 160×200 | 4 per char (shared bg) | 2 |
| Standard bitmap | 320×200 | 2 per 8×8 cell | 1 |
| Multicolor bitmap | 160×200 | 4 per 4×8 cell | 2 |
| Extended background | 320×200 | 4 backgrounds | 1 |

Phase 2 implements standard character mode (the default) and
multicolor character mode (used by most games). Bitmap modes
are Phase 4.

### Raster Timing

The VIC-II draws 312 lines (PAL) or 263 lines (NTSC) per frame.
Visible area is lines 51-250 (200 lines). Each line takes 63
CPU cycles (PAL). The CPU and VIC-II share the bus — on "badlines"
(every 8th line in the visible area), the VIC-II stalls the CPU
for ~40 cycles to fetch character pointers.

Raster interrupts: the VIC-II fires an IRQ when the raster counter
($D012) matches a programmed value. Games use this for split-screen
effects, color bars, and multiplexed sprites.

### Sprites

8 hardware sprites, 24×21 pixels each (or 12×21 multicolor).
Priority: sprites can appear in front of or behind characters.
Collision detection: sprite-sprite and sprite-background collisions
set bits in $D01E/$D01F and optionally trigger IRQ.

Sprite rendering: for each raster line, check which sprites are
active on that line, fetch 3 bytes of sprite data, shift out pixels
with priority/multicolor handling.

### Color Palette

The C64 has 16 fixed colors. We map these to 32-bit XRGB for the
GOP framebuffer:

```
 0 Black       $000000    8 Orange      $6C5EB5
 1 White       $FFFFFF    9 Brown       $444429
 2 Red         $68372B   10 Light red   $A9736E
 3 Cyan        $70A4B2   11 Dark grey   $444444
 4 Purple      $6F3D86   12 Grey        $6C6C6C
 5 Green       $588D43   13 Light green $9AD284
 6 Blue        $352879   14 Light blue  $6C5EB5
 7 Yellow      $B8C76F   15 Light grey  $959595
```

(Pepto's palette — the standard-bearer for accurate C64 colors.)

### Rendering to GOP

The C64 display is 320×200 (or 384×272 with borders). The GOP
framebuffer is 640×480 minimum. Options:
- 2× integer scale: 640×400, centered in 640×480 (40 pixels black
  top/bottom)
- 2× with borders: 768×544, needs 800×600 GOP mode
- Pixel-perfect: write to a 320×200 region, let the player resize

Phase 2 uses 2× integer scaling: each C64 pixel becomes a 2×2 block
in the GOP framebuffer. The border color fills the surround.

### Deliverable

Phase 2 is done when: a C64 BASIC program that PRINTs text and uses
POKE to set colors renders correctly. Character mode games that don't
use sprites or custom characters should be playable (text adventures,
simple BASIC programs).

---

## Phase 3 — Keyboard and Joystick

### CIA 1 Keyboard Matrix

The C64 keyboard is an 8×8 matrix scanned via CIA 1 ($DC00/$DC01).
The program writes a column mask to $DC00 (port A, output) and reads
the row result from $DC01 (port B, input). Each bit corresponds to
a key.

PS/2 scancode → C64 matrix mapping: codex-vm delivers PS/2 scan
codes at port 0x60. The emulator maintains a 64-bit key state
(8 bytes, one per column). On key down/up, set/clear the
corresponding bit. When the program reads $DC01, AND the active
columns and return the result.

### Joystick

CIA 1 port A ($DC00) bits 0-4 also read joystick 2. CIA 1 port B
($DC01) bits 0-4 read joystick 1. Arrow keys → joystick directions,
right-Ctrl → fire button. This is enough for most games.

### Deliverable

Phase 3 is done when: you can type in BASIC (LOAD, RUN, PRINT),
and joystick-controlled games respond to arrow keys + fire.

---

## Phase 4 — SID Audio

The MOS 6581/8580 SID is the soul of the C64. Three independent
voice channels, each with:

- **Oscillators**: sawtooth, triangle, pulse (variable width), noise
- **ADSR envelope**: 4-bit attack, 4-bit decay, 4-bit sustain level,
  4-bit release, each with nonlinear lookup tables
- **Ring modulation**: voice N modulated by voice N-1
- **Hard sync**: voice N reset by voice N-1's cycle
- **Resonant filter**: 12 dB/octave multimode (lowpass, bandpass,
  highpass). 11-bit cutoff, 4-bit resonance. Voices can be
  individually routed through or around the filter.

Output: mix the three voices (with filter), clamp, and write 16-bit
signed PCM samples to the HDA DMA buffer at 48 kHz. codex-vm's
Intel HDA emulation handles the host-side playback via waveOut.

The SID's analog filter is notoriously hard to emulate perfectly
(component-level quirks differ between 6581 and 8580 revisions).
A reasonable digital approximation uses a state-variable filter
(SVF) with resonance feedback. This gets 90% of the character.

### Deliverable

Phase 4 is done when: `POKE 54296,15: POKE 54277,9: POKE 54273,28:
POKE 54272,49: POKE 54276,17` produces an audible tone through the
speakers (the classic SID test sequence). Music players (SID files)
should sound recognizable.

---

## Phase 5 — Full Compatibility

Everything needed for real games:

- **Bitmap modes**: standard and multicolor bitmap rendering
- **Smooth scrolling**: $D011/$D016 fine scroll (0-7 pixels)
- **Screen blanking**: bit 4 of $D011
- **Sprite multiplexing**: games use raster interrupts to reposition
  sprites mid-frame, displaying more than 8 on screen
- **CIA timers**: Timer A/B on both CIAs, used for timing, serial
  bus, and NMI
- **IRQ/NMI**: proper interrupt handling with vector redirection
  ($FFFE/$FFFA)
- **Illegal opcodes**: ~28 undocumented 6502 opcodes used by some
  games and demos (SLO, RLA, SRE, RRA, SAX, LAX, DCP, ISC, ANC,
  ALR, ARR, SBX)
- **.D64 disk image support**: read sectors from a 1541 disk image
  via IDE disk attachment. Enough to LOAD programs from disk.

### Deliverable

Phase 5 is done when: commercial C64 games (Impossible Mission,
Boulder Dash, Maniac Mansion, Archon) are playable with sound.

---

## Phase 6 — The Plug (Codex → 6502)

Once the emulator works, build the reverse path: a Codex-to-6502
compiler plug that emits .prg files.

The 6502 plug receives Codex IR over serial (same as all other plugs)
and emits 6502 machine code:

- Zero page $02-$7F as the register file (126 bytes, ~15 locals)
- Software stack at $0200 for call frames
- Program code at $0800+
- Heap (bump allocator) growing down from $9FFF

Constraints the plug enforces:
- 8-bit arithmetic (Integer between 0 and 255)
- 16-bit addresses (pointers are pairs of zero-page bytes)
- No heap allocation in hot paths (zero page + stack only)
- Maximum ~38 KB program size

The test: write a game in Codex, compile through the 6502 plug,
load the .prg into the emulator, play it.

---

## File Layout

```
apps/c64/
  C64.codex              Main emulator: boot, main loop, .prg loader
  Cpu6502.codex          6502 CPU: decode, execute, cycle count
  Memory.codex           64 KB memory, bank switching, I/O dispatch
  VicII.codex            VIC-II: raster, characters, sprites, colors
  Sid.codex              SID: oscillators, envelopes, filter, mixing
  Cia.codex              CIA 1/2: keyboard matrix, joystick, timers
  Palette.codex          C64 color palette → XRGB conversion
  CharRom.codex          4 KB character generator ROM as static data
  KernalRom.codex        8 KB KERNAL ROM as static data
  BasicRom.codex         8 KB BASIC ROM as static data
  opening.codex          Entry point: init hardware, load .prg, run
  build.ps1              Build script
  codex.project.json     Project metadata

codex/plugs/c64/
  C64Plug.codex          IR → 6502 machine code emitter
  C64Encoder.codex       6502 instruction encoding
  build.ps1              Plug build script
```

---

## Build and Run

```powershell
# Build the emulator
pwsh apps/c64/build.ps1

# Run with a .prg file on the IDE disk
codex-vm -kernel build/output/c64.cdx -gop -gop-width 640 -gop-height 480 -mem 3072 -disk game.prg

# Or bake the .prg into the CDX at compile time
pwsh apps/c64/build.ps1 -Game games/boulder-dash.prg
```

---

## Why This Matters

The Codex project claims to be a complete computational substrate.
Running a Commodore 64 inside it — with display, audio, and keyboard
input — is a concrete demonstration that the claim is real. The 6502
is the simplest interesting CPU to emulate. The C64 is the most
beloved machine that used it. The combination tests every layer of
the stack: the compiler (building the emulator), the OS (GOP, HDA,
PS/2), the type system (bounded integers for 8-bit values, effects
for I/O), and the cultural proposition (the machine that started
home computing, reborn inside the machine that aims to finish it).

And SID music playing through HDA audio on bare metal would just
sound really good.
