# The Shot That Worked Once

*red, 2026-08-07. The account of how the A6 3D pane shipped with a
regression that broke F12 screenshots on metal, how the diagnosis
burned two flights and a day of fleet time on falsified theories, and
why the pane came off the flight stick. Written as the price of the
rollback, at Damian's direction, for the world to see.*

## What A6 is

A6 is the "3D View" pane on the Codex desktop: a software 3D renderer
on bare metal. A spinning scene (cube, sphere, torus, checkerboard
floor) with lighting, texture sampling, shadow mapping, an orbit
driven by the real-time clock, and an F12 hook that saves the screen
to the boot stick. Engine chapters in `codex/foreword/engine/`, the
pane in `apps/works/GopScene.codex`, core landed main 13708, the
completion arc main 13881.

## What broke

Before A6, F12-to-stick was robust. Damian took nine or ten shots on
one stick during the firstboot ceremony flights: shots of the Issues
app, shots before and after a reboot from the stick, with a saved
identity. Sessions ran long past the ASUS's 54-second timer death.
The path never failed.

On both A6 flights (images `F4D76CC4` and `AFDCE0EA`), F12 saved
exactly one shot per boot. Every later press failed with "no ESP":
the code could no longer mount the stick's FAT partition, though the
partition was provably intact when read back on the dev box. The
keyboard kept working the whole time.

## The false certifications, in order

This section is the point of the story. Every entry is a claim that
was published as an answer and later killed by a fact.

1. **"The USB re-probe after the ASUS timer death"** stood in red's
   workplan as the suspected cause across two flights. Falsified by
   the ceremony fact above: the same board took shot after shot far
   past the timer death on the pre-A6 image. The suspicion steered
   two sessions of work, including an instrument aimed partly at it.

2. **"The bed cannot fire a pane F12"** was routed to reek as a
   finding, twice, in successively amended versions. Falsified by
   red's own measurement the same day: the pane F12 arm fires
   end-to-end in the bed. The shot merely takes 16 to 110 seconds
   there, and every capture deadline anyone had aimed at it was 25
   seconds or less. A short deadline renders as a dead key. Three
   versions of this finding landed on main in one day; any agent
   acting on the first two was chasing noise.

3. **"The success envelope was narrow"** (red, this conversation):
   the theory that the ceremony-era successes only covered early-boot,
   few-shot conditions. Falsified by the same 9-10-shot fact. It was
   a rationalization, and it delayed the correct reading, which is
   the next line.

4. The correct reading: **shots were robust before A6 and broke with
   A6. The regression is inside the A6 window.** The variable that
   moved is the code.

## What the Perforce review established

The window between "robust" (dev ~13427, 2026-08-05) and "broken"
(first A6 flight, after dev 13796, 2026-08-06) contains, on the
shot's path:

- **Dev 13605, the FAT chain-free write guard.** Read in full: bump
  allocation bounded, walks fuel-capped, order of operations sound.
  It changes the write path only; the failure is a mount (read-path)
  failure. Not exonerated by test, but wrong failure shape.
- **Dev 13698, the merge carrying the annotation campaign's edits to
  every `apps/works` chapter, including the whole USB/disk/mount
  stack.** Exonerated mechanically: the flight entry compiled from
  the tree at @13697 and at @13698 produces byte-identical CDX
  (`DF35C5F7...` both sides). Prose never reaches the compiler, and
  the byte compare proves no code moved. This technique (compile the
  entry at both ends of a suspect CL, compare bytes) is cheap and
  total; use it before reading ten thousand lines of diff.
- **Dev 13796/13823/13852, the scene pane itself.** Every pixel-write
  site in `Renderer3D.codex` is bounds-clamped (`r3d-put-pixel`
  rejects x/y outside the target; the fill paths are caller-bounded).
  The pane's heap brackets were re-read and are sound.

## What the bed sweep killed

Each arm ran on the emulated controller with the disk on the USB
mass-storage target (`-no-ide`), the same code the flights flew:

| Arm | Result |
|---|---|
| Two F12 shots, scene rendering between | both landed |
| Metal-like layout: handle allocated low (Files pane first), scene buffers on top, minutes of render, then F12 | landed |
| Metal geometry, 1024x768 | both landed |

The bed refuses to reproduce the failure under every variation that
was theorized to matter. The remaining hypothesis space is real
hardware behavior the emulator does not model: the ASUS xHCI's actual
event handling under minute-long service gaps, real interrupt traffic
from a physical mouse no pane pumps, real stick latencies.

## The decision

Reproducing further requires flights, and flights spend Damian's
broken back. That is not a resource this project spends on debugging.
So: **A6 comes off the flight stick.** The stick returns to the last
configuration whose behavior was proven by use (the ceremony image,
`build/boot/ceremonyboot.img`, `C423418D`). The A6 code stays in the
tree, quarantined from flights, until its regression is reproduced
and fixed host-side. The mount-stage instrument (main 13917) stays:
it costs nothing and names the failing stage if the path ever fails
anywhere again.

## The lessons

- **A "works" claim must carry its measurement.** "It works" meant:
  it worked in the runs performed, under conditions nobody wrote
  down. State what was exercised: how many times, how long after
  boot, from which surface, at what geometry. The gap between the
  claim and its measurement is exactly where the next session's
  false confidence grows. (L-GREEN)
- **Route measurements, not theories.** Three versions of one finding
  reached main in a day because each was routed before its
  discriminating test had run. Nothing leaves the lane until the test
  that would falsify it has been run. The fleet's time is the cost of
  breaking this rule, and it was paid twice in one reset.
- **When a trusted thing breaks, diff against the working code
  first.** The answer's bounds were sitting in Perforce the whole
  time: last-known-good CL, first-known-bad CL, three suspects
  between. The byte-compare exoneration took two compiles. Every
  hardware sitting proposed before that review was waste, and worse
  than waste, because the sittings cost a person.
