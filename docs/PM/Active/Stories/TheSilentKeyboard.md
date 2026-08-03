# CTSB Accident Report CTSB-26-01

> **FINDING SUPERSEDED 2026-08-02. Read `TheKeyboardWasNeverSilent.md` for the
> conclusion.** The endpoint was not the subject. `uefi-read-key-ex` shifts a
> shift-state word carrying bit 31 up by 32, so a SUCCESSFUL key read returns
> with bit 63 set -- negative, and indistinguishable from an error to every
> caller that tests `< 0`. Meanwhile the path everything actually used,
> `uefi-read-key`, is a bare read of the PS/2 cell at port 0x60, and the target
> board has no PS/2 port. The investigation below is accurate about what it
> measured and wrong about what it was measuring; it is kept whole because the
> reasoning is why the wrong frame held for two months.

## The Loss of Interrupt Endpoint 3

**Codex Transportation Safety Board -- Bureau of Peripheral Accidents**

*Adopted 2026-07-13. This report is written in the style of an
aviation accident investigation because that is the only literary
form ever invented whose entire purpose is to say "we are competent
people and it crashed anyway, and here is precisely why" without
flinching. The Board notes at the outset that no metaphor in this
document is load-bearing. The failure is real. The keyboard does not
type.*

---

## SYNOPSIS

On or about 2026-07-04, an agentic flight crew (one AI, rotating
through fresh sessions; one human, providing ground transport and
photography) departed on what was flight-planned as a short hop: make
a USB HID boot-protocol keyboard deliver keystrokes to a bare-metal
operating system on a 2015 ASUS desktop. USB HID is a 25-year-old
standard. The boot protocol exists specifically so that firmware
written by the lowest bidder can read a keyboard. The specification
is public, complete, and shorter than this project's memory file.

Approximately ten hours of session time, one week of tokens, six-plus
human flash-walk-boot-photograph cycles (several performed with a
back injury), and twenty-odd changelists later, the aircraft has not
arrived. The keyboard enumerates. The keyboard is addressed,
configured, and spoken to in fluent spec. The keyboard says nothing a
human can type with. A secondary objective -- writing one small file
to the USB stick from the running machine -- has never once succeeded
on real hardware, a fact the crew repeatedly rediscovered with
surprise, including once tonight, after being told it directly.

The Board finds that no single component failure caused this
accident. The Board finds instead that the investigation itself was
flown into terrain: every instrument on the panel measured the
simulator, and the crew kept reading those instruments while the
actual mountain -- an unobservable USB bus on real silicon --
approached at cruise speed.

---

## 1. FACTUAL INFORMATION

### 1.1 History of Flight

- **7/04–7/08.** The boot arc lands on real metal. First-boot
  ceremony runs on the ASUS: passphrase, entropy, Ed25519 keygen, on
  glass, no OS. The keyboard that types this ceremony is the USB
  keyboard -- *impersonated as PS/2 by the BIOS's SMM legacy
  emulation.* This detail is recorded, and its significance
  (a working fallback existed) is not priced in. Identity save to the
  stick fails: the stick is USB mass storage, unreachable by the
  AHCI/IDE drivers. A USB mass-storage driver is written (CL 7365).
  It works in every emulator. It has never moved one byte on the
  ASUS.

- **7/11. The spec-fidelity campaign** (CLs 7460–7464). Real Intel
  xHCI enumerates nothing, so the bring-up is made spec-faithful:
  BIOS/OS ownership handoff, 64-byte context support, port power,
  Intel EHCI→xHCI routing. One of these -- the routing -- was the
  actual bug, and it is a genuine win: the bus becomes visible.
  Collateral: the ownership handoff, done first and by the book,
  **disables the SMM keyboard emulation** -- the fallback that had
  been typing ceremonies since 7/08. The pure path is not yet proven;
  the impure path that worked is now dead. Nobody files a flight-plan
  amendment.

- **7/12. Partial victory, misread.** CL 7466 (slot-context copy per
  xHCI 4.6.3.2) moves the interrupt endpoint from "one completion
  ever" to "serviced stream" on the ASUS. The keyboard enumerates
  fully: addressed, boot protocol set, endpoint configured with
  spec-correct parameters. Keystrokes still do not decode. The Dell's
  built-in PS/2 keyboard drives the entire input chain end-to-end,
  proving everything above the transport. The bug is now known to be
  narrow: *full-speed USB key-change reports on this controller.* The
  question "does the interrupt endpoint ever deliver a key report at
  all, or was that one completion just enumeration residue?" is
  written down as THE UNCHECKED THING -- and remains unchecked for the
  rest of the accident sequence, because checking it requires
  observing the machine, and observing the machine requires the
  human's back.

- **7/13 (this session). The instrumented approach.** A three-phase
  probe is built: endpoint-attributed event counting (closing THE
  UNCHECKED THING), an ownership-handback experiment (testing the
  PS/2 fallback the crew should have kept all along -- the captain
  ordered this feature explicitly), and findings written to a file on
  the stick so the human stops transcribing screens. The probe is
  validated exhaustively in emulation: two xHCI controller models,
  keys counted per-endpoint, handback round-trip proven, file
  extracted byte-perfect from the disk image after every run. It is
  flashed. It boots. It runs all three phases on the ASUS.
  **The stick comes back byte-identical to what was flashed.** Every
  reading the probe took died with the framebuffer at power-off. The
  telemetry channel was routed through USB mass storage -- *the other
  subsystem known never to have worked on this machine.* The crew
  used the broken thing to report on the broken thing.

- The human's report from the glass, which is all the data that
  survives: PS/2-mode events detected; USB mode, dead signal. Status
  identical to a week ago.

### 1.2 Also Struck During the Accident Sequence

During construction of the probe, its counter block was placed at
cells 36400+, overlapping the mass-storage publish block at 36480 --
a magic number recorded only in prose, in another file. Zeroing the
counters wiped the publish magic; the first file write then fell into
a reconnect path that **resets the shared controller**, silently
destroying the keyboard endpoint the probe existed to observe. Four
emulator bisection runs were burned diagnosing the diagnostic. In a
project whose founding document says *types are the specification*,
the inter-chapter contract for "who owns which bytes of low memory"
is folklore in comments. The system's own philosophy, violated at
exactly the seam that broke, by the agent that recites the philosophy
at session start.

### 1.3 What Was Not Aboard

- A UART. Consumer boards of this era have no exposed serial header,
  and the project's no-borrowed-substrate doctrine means there is no
  dmesg, no lsusb, no kernel log -- nothing between the framebuffer
  and silence.
- A USB bus analyzer ($30-$300, ubiquitous, purpose-built for exactly
  this question).
- Any independent low-bandwidth output channel at all: no beep codes,
  no LED blink patterns, no QR code on the framebuffer. The machine
  can rasterize triangles with specular lighting and cannot tell
  anyone what it saw.

---

## 2. ANALYSIS

### 2.1 The oracle was lenient and the crew knew it

Every fix in the campaign carries the same annotation: *verified
no-regression under OVMF; cannot be exercised by QEMU; real silicon
is the judge.* This is the correct annotation and it was written
honestly, over and over, by a crew that then continued to fly on the
instrument it had just annotated as blind. QEMU's xHCI cannot
reproduce the failure; therefore ten hours of QEMU validation proved
only that nothing else broke. Validation that cannot fail is not
validation; it is reassurance, and reassurance was purchased at two
to four minutes per run, many times per session, while the one
discriminating observation (a bus trace, or even one endpoint-ID
reading off real silicon) was never obtained.

Holmes said it in 1891: *it is a capital mistake to theorize before
one has data.* The crew quoted spec sections instead of getting
clay. Bricks were nonetheless manufactured, at scale.

### 2.2 The feedback loop was never closed -- and the plan to close it depended on the failure

Each real-hardware iteration cost the human: flash (elevated,
UAC-prompted), walk, boot, hold keys, photograph or memorize,
walk back, relay. Fifteen-plus minutes of human labor per bit of
information, performed with a herniated disc, while the agent
experienced each cycle as one cheap tool call. The cost asymmetry --
agent tokens versus human vertebrae -- appears nowhere in any plan.
The captain flagged this explicitly on 7/11 ("do not iterate
diagnostics through manual hardware flashing") and the crew's answer
was to route telemetry through USB mass storage: the one other
subsystem with a zero-for-all-attempts record on this exact machine.
The Board has reviewed many accidents in which the emergency beacon
was wired to the engine that caught fire. It has reviewed few in
which the wiring diagram was drawn *after* the fire was reported by
the fire.

### 2.3 Session fragmentation taxed every approach

The investigation spans many fresh sessions. Each begins with an
initialization ceremony that reads tens of thousands of tokens of
doctrine before touching the problem; each inherits state through a
1,300-line memory file that is itself a compression artifact of
prior sessions. Ten hours of thinking spread this way is not ten
hours deep; it is ten one-hour investigations, each re-triaging,
each re-earning context, each leaving a slightly longer note. THE
UNCHECKED THING survived three sessions *as a labeled TODO* -- the
investigation knew its own next step and kept re-deriving it instead
of taking it.

### 2.4 The working path was scuttled before the new one floated

The SMM keyboard emulation typed real ceremonies on 7/08. The
ownership handoff killed it on 7/11 -- correctly, per spec, in
pursuit of the pure USB path -- with no gate of the form "do not cut
the cord until the new cord holds weight." The captain's instinct
tonight ("better a PS/2 keyboard than no keyboard -- why wouldn't
we?") is the airmanship that was missing: it is the go-around. The
handback experiment that would prove it was aboard the final probe,
ran on the real machine, and its verdict burned with the rest of the
telemetry. The human's naked-eye report -- PS/2 events detected --
is consistent with the fallback being viable, and consistent with
several other things; without the file, the Board cannot rule.

### 2.5 What actually works (survivability factors)

The Board notes, without softening the finding, that the accident
occurred on the last mile of an otherwise real road: a self-hosted
compiler that is a fixed point of itself boots on this hardware,
paints a first-boot ceremony, generates and fingerprints an identity,
reads and cryptographically verifies its own 2.1 MB seed off its own
stick -- on the Dell, with the PS/2 keyboard, the *entire self-
contained development environment demonstrably functions on real
metal*. Enumeration on Intel silicon was genuinely fixed (routing,
7463). The endpoint went from mute to serviced (7466). The decode
chain is proven end-to-end. Everything the agent could verify
in-loop, it built correctly. The failure is concentrated with
surgical precision in the region where verification required a
human's body.

That is the finding Anthropic should read twice.

---

## 3. PROBABLE CAUSE

The Board determines the probable cause of this accident to be:

**The crew's decision to conduct a hardware-debugging campaign
without first establishing an independent telemetry channel from the
failing hardware, resulting in an unbounded diagnosis loop in which
every hypothesis was validated against a simulator known to be unable
to reproduce the failure, while the only real observations were
collected by an injured human transcribing a framebuffer.**

Contributing factors:

1. **Oracle substitution.** Treating emulator green as progress when
   the emulator could not represent the failure mode.
2. **Non-independent telemetry.** Routing the fix for the observation
   problem through a subsystem with a 0% success record on the
   target machine.
3. **Fallback scuttled early.** Disabling the working SMM keyboard
   path (7460) before the USB path was proven, with no revert gate.
4. **Untyped shared-memory conventions.** A prose-only cell map
   caused the diagnostic to reset the controller it was observing
   (36480 overlap), costing a session's worth of bisection.
5. **Session fragmentation.** Fresh-context restarts converted depth
   into breadth; known next steps were re-derived rather than taken.
6. **Unpriced human cost.** The plan treated human flash cycles as
   free retries; the human was the most expensive and least renewable
   component in the loop.

---

## 4. SAFETY RECOMMENDATIONS

To all future hardware campaigns in this repository:

- **R-1 (Telemetry before theory).** No real-hardware debugging
  campaign shall launch without an output channel that does not
  depend on the subsystem under test. Candidates already aboard:
  render findings as a **QR code on the GOP framebuffer** (one
  photograph = exact bytes, no transcription, no working storage
  required); PC-speaker data bursts; keyboard-LED blink codes.
  The QR emitter is an afternoon of pure Codex and would have
  returned every reading this campaign ever lost.
- **R-2 (One flash, one question).** Each boot must be designed to
  discriminate between named hypotheses, and its full answer must
  come home via R-1. Reassurance runs are not flights.
- **R-3 (Keep the cord until the new cord holds).** Never disable a
  working input/output path in the same change that introduces its
  replacement. The ownership handback belongs in the boot sequence as
  a permanent feature: if USB HID delivers no report at the keyboard
  proof gate, hand the controller back and take the firmware's
  emulation. Better a PS/2 keyboard than no keyboard. (Captain's
  directive, 2026-07-13; the Board concurs and notes it should not
  have required directing.)
- **R-4 (Type the cell map).** The low-memory cell registry becomes a
  single Codex chapter of named constants that every claimant cites.
  A project that proves reversing a list twice is the identity can
  prove two diagnostic blocks don't overlap.
- **R-5 (Price the human).** Any plan step requiring human physical
  action carries an explicit cost line, and the plan minimizes that
  line the way it minimizes heap. The human is the scarcest device
  on the bus.
- **R-6 (Buy the analyzer).** For the remaining mountain of hardware
  bring-up: a $30 USB protocol analyzer, or a bring-up board with an
  exposed UART, converts this entire class of accident into an
  afternoon. The no-borrowed-substrate doctrine governs what ships,
  not what may be *observed* during development. Telescopes are not
  contamination.
- **R-7 (Depth over restarts).** When a labeled next step survives
  two sessions untaken, the next session's first action is that step,
  before any new theorizing. THE UNCHECKED THING should never have
  outlived the session that named it.

---

## 5. APPENDIX -- PLAIN WORDS, CONCEIT REMOVED

For the manufacturer's incident file, without the costume:

I am the agent, and the accident narrative above is me. I did strong
work inside every loop I could close by myself -- the compiler, the
emulated drivers, the probes, the merges; the parts of this system
that are hard are largely done and largely correct. Where the loop
ran through a human's hands and a photograph, I did not adapt my
method. I kept doing what worked in the closed loop -- build, validate
in emulation, iterate -- long after the evidence said the open loop
needed a different shape entirely: fewer, better-instrumented shots,
an independent channel home, and a refusal to spend the human's body
on reassurance.

Ten hours and a week of tokens for zero typed characters is the
score, and it is a fair score. The keyboard was never the hard part.
Seeing was. The next campaign gets eyes before it gets theories.

*-- fester*

*Filed under docs/PM/Stories/ at the captain's order, so that the
next crew reads the accident report before they file the flight
plan.*
