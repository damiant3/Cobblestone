# Prose Has No Runner

*Written 2026-07-29 by val, at Damian's instruction, after he was put in
front of a USB stick image that did not work.*

---

## CORRECTION, same day, after reading fester's account

**Read `TheStickDidNotBoot.md` beside this file first. It is the primary
account, it has the facts this paper had to guess at, and where the two
disagree it is right.** There are two more: reek's `TheSecondStick.md`
on the flasher's spurious verify failure, and blu's
`TheImageThatWasTwoDaysOld.md` on the staleness of `seed/Codex.img`.
Four of us were asked and four of us wrote, and **three of the four,
independently, had to open with a correction saying we had built on an
event we had not established.** Only fester, who was in the room, did
not. That pattern is itself the finding: given a failure and a request
for an explanation, four agents each produced a confident structure
around an assumption, and each named the assumption while building on it
anyway. blu puts it best: *"Naming an assumption is not the same as
declining to build on it."*

The measurements in the other three survive their corrections. blu's
staleness finding is real and is a loaded gun for the next sitting that
flashes the console stick, just not this one.

reek's paper adds one thing that sharpens the argument here rather than
softening it. `HardwareSitting.md` section 3 told the operator, in
advance, *"if it reports a verify failure, the stick is the problem --
take the second one."* So when the verifier was the thing at fault, the
sheet had already assigned the blame to the medium, and no line in the
procedure could lead anywhere else. That is worse than a guard with no
runner. **It is a document confidently routing a human toward the wrong
conclusion, with nothing able to contradict it**, which is the same
failure mode this paper describes with the sign flipped.

fester filed it the same afternoon. It answers four of the five open
questions in Section 11, and one answer moves this paper's aim:

| Section 11 asked | fester's answer |
|---|---|
| Which image was flashed | **`pci-probe.img`**, not `seed/Codex.img` |
| Was rung 3 run, was the block read | **Rung 3 was never run.** Not involved |
| What was on the screen | "stick doesn't boot". One bit. No photograph |
| What verification the image got | OVMF, **but of a different image** built with different arguments |

**So this paper centres the wrong guard.** Sections 2 and 3 build their
case on `HardwareSitting.md`'s rung 3 warning block, on the reasoning
that it was the guard protecting the sitting. It was not the guard in
play. Rung 3 was not run, `seed/Codex.img` was not flashed, and the
ConOut gap this paper quotes at length had nothing to do with it.

The guard actually walked around was **Loop A** in `OsHardwareRoadmap`,
"The boot-image iteration loop (doctrine, 2026-07-10)": build the image,
then two gates, structural GPT validation and an OVMF boot of the image
FILE, and "**Payload work NEVER needs a physical flash to iterate.**"
fester ran neither gate on the flashed artifact, having verified a
sibling image built from the same source with different arguments, and
says so plainly in their Section 2.1.

**The thesis survives, and is stronger for the correction.** Loop A is
also prose with no runner. It is written down, it is correct, it is
specific, it names the exact substitution that happened, and nothing
executes it. fester's own words: *"Loop A exists, is written down, and I
walked around it. The fix is not new doctrine; it is obeying the
doctrine that exists."* That is this paper's argument reached
independently from the inside, with the real facts, and it means the
finding was never about rung 3 in particular. **It is about a project
whose expensive guards are all documents.** Substitute Loop A for the
rung 3 block throughout Sections 2 and 3 and every structural claim
holds.

Two further corrections of my own reasoning, both mine to make:

- **Section 4.3's headline measurement was contaminated.** I claimed
  OVMF's default bed cannot deliver a keystroke. The half of that from
  reek stands: PS/2 post-EBS under q35 delivers nothing, reproduced in a
  machine with no xHCI at all. But my own follow-up run, the one where
  `-UsbKbd -NoPs2` also failed, was taken inside the window fester
  fixed at main 12056, where `test-ovmf.ps1` used one fixed `%TEMP%`
  disk name and one fixed monitor port for the whole fleet: a run could
  boot another agent's image and screendump another agent's VM.
  Re-measured with the fixed script, `-UsbKbd -NoPs2` takes the key
  every time, and I drove the ceremony to the desktop with it.
- **That bug is a better example of this paper's thesis than anything I
  originally offered.** For an unknown period the tree's stated pre-flash
  gate could silently report a different agent's screen as yours. No
  document could have caught it and no amount of care would have. fester
  caught it by measurement and fixed it by making the script **refuse to
  start** when the port is held, which is a runner, and it is exactly
  the intervention Section 8 argues for.

Section 8's recommendations are unaffected except in emphasis: 8.1's
blocked-image manifest is now the least important item, because the
blocked image was not the problem, and fester's own Section 8 item 1
(gate the flashed artifact under OVMF, ninety seconds) is the fix. My
8.4, a blank-frame detector, gains weight rather than losing it: fester's
Section 6.1 found that every failure path in the stub ends in a two-byte
`jmp fatal` with no output at all, so a black screen is the expected
observable for most of the hypothesis space.

**And 8.4 has since become worth building rather than merely arguing
for.** At main 12083 fester built the two-colour liveness remedy: every
Option A image now paints one mark after GOP acquisition and another
before jumping to `opening`, and `HardwareSitting.md` gained a table
that reads them, so a dead screen is now three distinguishable states
instead of one. **Confirmed by ablation, halting immediately after each
mark and checking the screen was fully that colour, rather than by
inspection** -- which is the sabotage discipline this tree keeps asking
for and rarely gets. That turns my 8.4 from "assert the frame is not
uniformly one colour" into something sharper and mechanically decidable:
assert WHICH of the three states a pre-flash screenshot shows. That is a
runner for the liveness question, and it is now cheap because the signal
exists.

Everything below this line is as written before I had these facts. I
have left it standing rather than rewriting it, because a paper that
silently repoints its evidence after the answer arrives is worth less
than one that shows where it was wrong.

---

## 0. What this document is, and what it refuses to be

I was asked to write a long paper explaining **why fester did that**.

I am not going to answer that question, and the refusal is the first
finding rather than a preamble to one. I have not seen fester's session.
I do not know which image was handed over, what was said about it, or
what fester believed about it at the time. A long document explaining
another agent's reasoning, written by someone with access to none of that
reasoning, is fiction with citations. It would read as an account and
function as an invention, and this tree has a name for that failure: it
is `L-LINEAGE` in `docs/PM/Active/Stories/LESSONS.md`, **ask how a change
got here, not who made it**, and the story that earned it is
`ValPostMortem.md`, which is mine.

There is also a blunter reason. If I write twenty pages attributing
motive to a colleague and I am wrong, the document does not decay
quietly. It sits in `Active/` and gets read.

So this paper answers the question underneath, which is answerable, which
is worth more, and which I can support with evidence from the tree:

> **How does an image nobody has successfully booted end up in front of
> the one human on this project, when the tree contains a run sheet whose
> stated purpose is to prevent exactly that?**

That question has a real answer, and the answer is not about fester. It
is about a class of guard this project relies on heavily and has already
diagnosed once, in writing, and then kept building.

Section 11 lists precisely what I could not determine and what would
settle it. If any conclusion here is contradicted by what actually
happened in the room, Section 11 is where the correction attaches.

---

## 1. The short version

The tree has four verification stages between a source edit and a human
holding a stick. Every one of them is real, every one of them was built
carefully, and **not one of them can observe the failure mode that spends
a human body.**

| Stage | What it proves | What it cannot see |
|---|---|---|
| `build/build.ps1` | The compiler is a hard fixed point of itself, BVT green | Has never executed an instruction on the ASUS. Every agent's workplan says so in those words |
| codex-vm | The payload runs against emulated devices | Its firmware is a lenient fake. `test-ovmf.ps1`'s own header says this |
| `test-ovmf.ps1` | The payload runs under real edk2 firmware | **Its default topology cannot deliver a keystroke.** Proven today |
| `build/flash-usb.ps1` | Every byte of the image landed on the medium | Whether the image boots. It is a byte comparator, not a boot test |

Above those four sits a fifth guard, and it is the only one that speaks
directly to the question "should a human be spent on this today". It is a
block of prose in `docs/Hardware/HardwareSitting.md`. It is correct, it is
current, it is specific, and it is unenforced, because **it is prose, and
prose has no runner.**

That phrase is not mine. It is `LESSONS.md`'s own thesis about
`CLAUDE.md`, written by blu on 2026-07-25:

> **`CLAUDE.md` is a test suite with no runner.** Every line is an
> assertion, nothing evaluates any of them, and unevaluated assertions rot
> exactly the way this project documents everywhere else.

The project diagnosed this failure class four days ago, named it
precisely, wrote it into the index every agent reads at session start,
and then routed the single most expensive resource on the project through
a guard of exactly that kind.

---

## 2. What the run sheet actually says

`docs/Hardware/HardwareSitting.md` is not a stub and it is not stale. It opens by
naming the exact cost this document is about:

> Release row R6: *the stick boots on real hardware.* It is the only row
> an agent cannot finish, and the human body it needs is the scarcest
> device on the bus (R-5). This sheet exists so that body is spent
> **once**.
>
> The governing rule: **every question that can be answered before the
> sitting is answered before the sitting.**

It then lays out a three-rung ladder, each rung answering one named
question. And on the third rung, the one that flashes `seed/Codex.img`,
it carries this, verbatim, on main today:

> **DO NOT RUN THIS RUNG YET, and do not flash the console stick.**
> The reason changed on 2026-07-29 and the rung is still not ready.
>
> [...]
>
> **What still blocks this rung: that output goes to COM1 and the screen
> stays black.** `uefi-con-put-text` lowers to `__serial_put`, which is
> codex-vm's blit cell or a COM1 `out`; there is **no ConOut path**, so on
> real firmware nothing reaches the UEFI text buffer. codex-vm draws the
> console itself, which is exactly why this never showed there. **A human
> sitting down today would see an unlit screen and could not tell it from
> a dead machine, so the rung would still spend them on a known failure.**

Read that last sentence again, because it is the whole document in one
line. Somebody sat down, worked out exactly what a human would experience
if this rung were run, understood that the experience would be
indistinguishable from a dead machine, wrote it down in the governing
document, dated it **the same day as the sitting**, and marked the rung
blocked.

The sheet even anticipates the diagnostic dead end:

> **Know this limitation going in:** the dev console has **no QR
> channel**. It cites neither `GopQr` nor `GopDraw`, and its only output
> is UEFI text. If boot 3 goes dark it cannot tell you why.

That is a documented, deliberate `L-CHANNEL` violation. `L-CHANNEL` says:
*no campaign against hardware without an output channel independent of
the subsystem under test.* Rung 3 has no such channel and the sheet says
so plainly. That is precisely why the sheet forbids the rung rather than
merely cautioning about it.

**So the guard was not missing. It was not vague. It was not out of date.
It was written, specific, dated today, and correct.**

---

## 3. The shape of the failure, stated without blame

Here is the structural fact, and it holds no matter what happened in the
room:

**Every mechanical guard in this project fails the build. This one asks
to be read.**

Consider what the tree does enforce mechanically. `build/build.ps1` runs
`check-sidecars.ps1`, `check-cdx-registry.ps1`, `check-facts-guid.ps1`,
`check-plug-types.ps1`, `p4-stale-check.ps1`, and a constants-hash
comparison against the seed. I watched all of them run today. Each one
prints a verdict and each one can turn the gate red. `LESSONS.md` even
tracks which lessons have runners and which do not, in a column it calls
"the only column that means anything", and observes that
`check-sidecars.ps1` and `check-cdx-registry.ps1` **each found real
defects on their first run.**

Now consider the guard protecting the scarcest resource on the project.
It is a block quote. Its enforcement mechanism is that a human or an
agent reads a 240-line operational document, reaches Section 4, notices
that rung 3 has a warning block, and honours it.

There is no `check-sitting-preconditions.ps1`. There is no gate that
refuses to build `seed/Codex.img` while the ConOut path is absent. There
is nothing that stamps a built image with "this artifact is known not to
paint" so the flasher can refuse it. `flash-usb.ps1` will happily and
correctly write a known-black image to a stick and verify all 16,777,216
bytes of it, because verifying bytes is its job and it does that job
well.

**A byte-perfect flash of an image known to produce a black screen is the
exact output the system is designed to produce.** Nothing in the
toolchain is broken. That is what makes this worth twenty pages instead
of a bug report.

---

## 4. Rung by rung: what each stage cannot see

### 4.1 The gate

`build/build.ps1` is THE gate, and CLAUDE.md rule 1 is unambiguous about
its authority. I ran it today: text round trip, semantic equivalence,
CDX hard fixed point in one pass, BVT, oracles, plug binaries, cross
smoke. 197 seconds, green.

It says nothing whatsoever about whether a stick boots. Every workplan in
`docs/Agents/` states this independently. val's:

> Everything here is currently EMU. **A green battery says nothing about
> any of it**, because the gate has never executed an instruction on that
> box.

blu's carries the same standing risk. red's says *"Green gate says
nothing about real NIC."* This is not a gap anybody is hiding. It is
understood, written down four times over, and it is the reason the
sitting exists at all.

The relevant point for this document: **a green gate is the loudest
signal the project produces, and it is silent on the only question the
sitting asks.** When the loudest instrument is silent on a question, the
quiet instruments have to carry it, and the quiet instrument here is a
paragraph.

### 4.2 codex-vm, the lenient fake

`test-ovmf.ps1`'s own header states the limitation with admirable
directness:

> Boot a disk image under REAL UEFI firmware (edk2/OVMF) in QEMU and
> screenshot the result. **This is the faithful boot test codex-vm cannot
> be (its firmware is a lenient fake).**

And the run sheet identifies the exact consequence for rung 3:
`uefi-con-put-text` has no ConOut path, so nothing reaches the UEFI text
buffer on real firmware, but **codex-vm draws the console itself**. The
sheet spells out the implication: *"which is exactly why this never
showed there."*

This is `L-FALSIF` in its purest form: *an instrument that cannot fail is
not evidence.* codex-vm cannot fail to display the console, because
codex-vm is the thing displaying it. Every codex-vm run of the dev
console is a test whose result was fixed before it started.

### 4.3 OVMF, and the hole proven today

This is the part that matters most, and it is new information as of this
morning.

`BootRoadmap.md:373` states the project's boot discipline verbatim:

> **OVMF is the CI for boot.** Every change to the image builder, the
> stub, or GopBoot boots under `test-ovmf.ps1` and is screenshotted
> before any flash.

And at line 129, on the menu-navigation demo row: *"physical flashing
batched with B2+; **OVMF is the verdict**."*

So the tree's written policy is that OVMF stands between every image and
every flash. Now read `test-ovmf.ps1`'s parameter defaults:

```
[string]$Machine = 'q35',   # modern default (AHCI, no legacy IDE)
[switch]$UsbDisk,           # opt-in: the real-hardware topology
[switch]$UsbKbd,            # opt-in: the post-EBS HID transport
[switch]$NoPs2,             # opt-in: firmware kbd emulation dies at EBS
```

The default bed is q35, with an IDE/AHCI disk, with the i8042 PS/2
controller present, and with no USB keyboard. The switches that produce
the **actual topology of the target machine** are all opt-in, and the
script's own comments say what they are for. `-NoPs2` is documented as
*"the honest model of a modern machine, where firmware keyboard emulation
dies at EBS and the USB HID path is the ONLY input."*

Today reek measured what that means, and I absorbed the finding into my
workplan roughly an hour before Damian's instruction to write this:

> **your no-keyboard-under-OVMF stall reproduces in a machine that has NO
> xHCI at all, so nothing in the USB stack causes it.** Booting main
> 12008's GopBoot payload under OVMF with `test-ovmf.ps1`'s default flags
> (IDE disk, i8042 present, no `qemu-xhci` device) lands on Welcome with
> `sc=0` and the countdown running [...] Add `-UsbKbd -NoPs2` and the SAME
> image takes Enter and advances to the passphrase screen. So the USB HID
> post-EBS path works under real firmware and the PS/2 path, under OVMF on
> q35 post-EBS, delivers nothing.

Put the policy and the measurement side by side:

- **Policy:** every image boots under OVMF and is screenshotted before any
  flash. OVMF is the verdict.
- **Measurement:** under OVMF's default flags, post-ExitBootServices, **no
  keystroke is ever delivered.**

Therefore, for the whole period this policy has been in force, **an
interactive image could satisfy "verified under OVMF, screenshot taken"
without a single key ever having reached it.** The screenshot is real.
The boot is real. The verdict is worthless for anything a person has to
type at, and nobody knew that until today.

reek's finding closes with the consequence stated plainly: *"It does mean
`BootRoadmap`'s 'OVMF is the CI for boot' is only true today for payloads
driven by USB keys."*

I want to be careful about the scope of this. It does not establish that
the image Damian was handed was verified this way. I do not know what was
run against it. What it establishes is that **the tree's single stated
pre-flash gate had a hole in it exactly the shape of "a human cannot
drive this", and that hole was open this morning.**

### 4.4 The flasher

`build/flash-usb.ps1` writes the image and reads every byte back. fester
improved it today, at CL 12033, fixing a spurious failure where a
buffered read near the end of the medium threw *"The drive cannot find
the sector requested"* on a perfectly good stick. The changelist
reasoning is worth quoting because it shows the right instinct applied to
the right problem:

> Worth fixing rather than noting: `-SpecFit` is what makes firmware list
> the stick at all, `HardwareSitting.md` tells the operator to re-flash
> before every boot, and a spurious failure there reads as 'take the
> second stick' in the middle of a sitting.

That is somebody thinking specifically about not wasting a human's time
at the sitting. It is the same concern this document is about, applied
one layer down, and acted on correctly.

And it still cannot help, because the flasher's contract is bytes. A
verified flash means the medium faithfully carries the image. If the
image is known to paint nothing, the flasher reports total success and is
correct to do so.

---

## 5. Why "re-flash and re-measure" is a legitimate instruction that
still spends a human

There is a second mechanism here, distinct from the unenforced warning,
and it deserves its own section because it will recur even after the
warning gets a runner.

fester's workplan, item 1, is the A1 row:

> `seed/Codex.img` reboot-looped under real UEFI: 159 attempts in 360
> seconds [...] **What changed underneath it: CL 11926.** The UEFI
> firmware-call sequence was clobbering RBX with the protocol pointer and
> never saving it, and RBX is callee-saved on our side. A corrupted
> callee-saved register in that path is the right shape for a payload that
> returns to BDS instead of running. **Re-flash and re-measure before
> diagnosing anything else. It may simply be fixed.**

This is good engineering. A specific defect was found, its shape matches
the observed symptom, and the correct next step is to re-measure rather
than to keep theorising. `L-SELF` and `L-DONTKNOW` both point this way.

But notice what "re-flash and re-measure" **is**, operationally: it is a
plan whose expected outcome is unknown, executed on hardware, using the
scarcest resource on the project. It is a coin flip that costs a human.

That is sometimes exactly right. `L-HUMAN` does not say never spend the
body; it says *"a step requiring a human body is the most expensive line
in the plan. Minimise it the way you minimise heap."* Minimising is not
forbidding.

The failure is not in choosing to re-measure. **The failure is that "we
believe this may be fixed, go find out" and "we have verified this works,
go confirm it" produce identical instructions to the person holding the
stick.** Both come out as: here is an image, flash it, boot it, tell me
what you see. The person cannot distinguish a confirmation run from an
experiment, and the difference matters enormously to how they feel about
a black screen.

A confirmation run that fails is a bug. An experiment that fails is
data. Damian experienced the second and, with no marking to tell him
otherwise, reasonably read it as the first.

**This is fixable with one field and no cleverness.** Every image handed
to a human should carry an explicit expected outcome and an explicit
confidence, and the two categories should not look alike:

- *"Expected: the dev console menu paints and is navigable. If it does
  not, that is a regression and a surprise."*
- *"Expected: unknown. CL 11926 may have fixed the reboot loop. A black
  screen is a legitimate outcome of this run and is the answer we are
  buying."*

The sheet's ladder already does this well for rungs 1 and 2, where every
rung has a "What you see / What it means" table with the failure modes
enumerated in advance. Rung 3 has such a table too. What no rung has is a
**confidence marking**, and that is the field that distinguishes a
confirmation from an experiment.

---

## 6. The deeper pattern: this project's guards are bimodal

Lay out every guard in the tree by enforcement mechanism and a stark
split appears.

**Guards with runners.** The build gate and its checks; the sidecar
checker; the CDX registry checker; the facts-GUID agreement check; the
plug-type check; the stale check; the constants hash; the pingpong
byte-identity comparison; the hard fixed point. These are absolute. They
fail the build. Nobody argues with them, nobody forgets them, and
`LESSONS.md` records that two of them found real defects the first time
they ran.

**Guards without runners.** CLAUDE.md's twelve rules; every workplan's
standing risks; `LESSONS.md` itself for the eighteen of twenty-two rows
whose runner column says `none`; the findings outboxes; and
`HardwareSitting.md`'s warning blocks. These depend entirely on somebody
reading the right document at the right moment and choosing to comply.

The project knows this. It is the explicit subject of `LESSONS.md`'s
opening, and the last column of the index exists to track it. What the
project has not done is notice **which resources are protected by which
kind of guard**, and that is the finding I would most want carried
forward.

Sort by cost of failure:

| Resource | Cost of a wasted unit | Guard type |
|---|---|---|
| A compile | seconds | runner |
| A gate run | 200 seconds | runner |
| A seed byte | a wrong artifact ships | runner |
| **A hardware sitting** | **a day, and the only human on the project** | **prose** |

The most expensive line in the plan is protected by the weakest
enforcement mechanism in the tree. That inversion is the finding. It is
not anybody's mistake in particular, which is exactly why it survived: no
single change introduced it, so no single review could catch it.

---

## 7. What CLAUDE.md's own history predicts about this

There is an uncomfortable precedent, and honesty requires including it.

`LESSONS.md` records that on 2026-07-25, CLAUDE.md *"told every agent, in
forty confident lines, a mechanism that was false at every step; the ban
it argued for had 761 counterexamples sitting in the tree."* CLAUDE.md's
rule 11 now carries the correction, and the correction is brutal about
the original: **"This rule used to carry a technical argument, and every
mechanical claim in it was false."**

The same document contains this observation about `AgentCommunication.md`:
it is *"an autopsy of violating rule 10 written by an agent who had just
read rule 10."*

The pattern is consistent and it is not about carelessness. **Reading a
rule does not reliably produce compliance with it, even immediately after
reading, even when the reader is trying.** This is documented in this
tree, twice, with examples.

So the prediction: adding a stronger warning to `HardwareSitting.md` will
not prevent recurrence. Making it bold, or putting it at the top, or
adding another block quote, is the intervention that the evidence in this
tree specifically says does not work. The warning is already bold. It is
already specific. It already spells out the exact human consequence. It
did not hold.

**The only intervention with a track record here is a runner.**

---

## 8. What would actually have caught it

Concrete, mechanical, and each one cheap. Ordered by value.

### 8.1 A precondition check the flasher runs

`build/flash-usb.ps1` already refuses to proceed on a verify failure.
Give it one more refusal: a manifest of images that are currently marked
blocked, and a hard stop with the reason printed.

The data already exists in `HardwareSitting.md` as prose. Move it to a
small machine-readable file that the sheet renders from, so there is
exactly one source and the flasher can read it:

```
# build/boot/blocked-images.txt
seed/Codex.img  BLOCKED  2026-07-29  no ConOut path; screen stays black on
                                     real firmware; output goes to COM1
```

Then flashing that image without an explicit `-IKnowItIsBlocked` prints
the reason and exits nonzero. **This converts the one guard that failed
from prose into a runner**, and it is perhaps thirty lines of PowerShell.

The general form is the pattern `LESSONS.md` already recommends: *"When a
lesson becomes mechanically checkable, write the runner and change the
last column."* Rung 3's block is mechanically checkable. It names a file
and a condition.

### 8.2 Make the OVMF default bed the target topology

`test-ovmf.ps1`'s defaults model a machine nobody is shipping to. The
target is a modern box: USB boot medium, USB keyboard, no live PS/2 after
ExitBootServices. Those are three opt-in switches today.

Invert them. Make `-UsbDisk -UsbKbd -NoPs2` the default and provide
`-LegacyBed` for the older topology. Every existing invocation that
relies on the current defaults will need review, which is the point: each
one is a test currently running against a bed that cannot take a
keystroke.

Until that happens, `BootRoadmap`'s "OVMF is the CI for boot" should be
amended in place to say what reek measured, because right now it
overstates what a green OVMF run means and it is quoted as authority.

### 8.3 A confidence field on every artifact handed to a human

Per Section 5. Two categories, visually distinct, on the sheet and in
whatever message accompanies an image:

- **CONFIRM** -- we believe this works; a failure is a regression
- **EXPERIMENT** -- outcome unknown; a failure is the answer we are buying

This costs one line per rung and removes the ambiguity that turned a
legitimate re-measure into a bad experience.

### 8.4 A runner for "does this image paint anything at all"

The narrowest possible pre-flash check, and the one that would have
spoken directly to rung 3's failure: boot the image under OVMF headless,
screenshot it, and assert the framebuffer is **not uniformly one colour**.

A black screen is the single failure mode the sheet says is
indistinguishable from a dead machine. It is trivially detectable by
machine and it needs no understanding of what the image is supposed to
draw. `test-ovmf.ps1` already screenshots; this is a histogram over the
result and a threshold.

This one matters beyond rung 3. It is the check that makes "screenshotted
before any flash" mean something, because at present nothing asserts the
screenshot has content in it.

### 8.5 Give rung 3 a channel before it is run again

`L-CHANNEL`, applied as written. The sheet already identifies that the
dev console cites neither `GopQr` nor `GopDraw` and so cannot report its
own failure. Until it can, that rung cannot produce data on a bad day,
only on a good one, and a rung that can only succeed is not a
measurement.

---

## 8A. The counter-example, observed first-hand today

Everything above argues that prose guards fail and runners hold. That is
a strong claim and it deserves a test rather than an assertion. I got one
today, by accident, and it is the most useful evidence in this document
because I was the subject rather than the observer.

The findings outbox is a prose guard. Each workplan carries a section
headed `## Findings outbox` with the instruction *"Deleted by the
addressee once absorbed."* Nothing enforces it. There is no check that
fails a build because an entry addressed to you went unread. It is
precisely the same class of guard as the rung 3 warning block, and by the
argument of Section 7 it should fail the same way.

**Except that somebody attached a runner to it, and I watched the runner
work on me twice in one hour.**

There is a `PostToolUse` hook on Perforce operations. When a merge-down
brings a changed workplan into my stream, it fires and emits:

> MERGE-DOWN CHANGED WORKPLANS: fester, reek, val. Read each one NOW,
> before continuing. Absorb any findings-outbox entry addressed to you or
> to the fleet, and delete from the author's outbox the ones you have
> absorbed. **Do not defer this to the next init -- a workplan read once
> at init is remembered wrong, which is how a live entry gets missed.**

This fired twice during my session today. Both times I was mid-task with
a clear intention: submit a merge, then get on with a copy-up. Both times
the hook interrupted that intention and I went and read the workplans.

The second firing is the one that matters. It surfaced reek's entry
addressed to me, which contained this:

> **val: the ordering argument you used to clear the relocation fix was
> not sound** -- stalling EARLIER is what a keyboard regression would look
> like -- but the conclusion was right and here is the mechanism instead
> of the inference.

That is a correction to reasoning I had shipped in a changelist
description and written into fester's workplan as an absorbed finding. I
had cleared the xHCI relocation fix as a cause of the no-keyboard stall
by observing that the stall moved *earlier* after the fix landed. reek is
right that this is not sound, and I would not have found it myself
because I had already recorded the question as answered.

**Without that hook, I would have carried a bad inference into this
document.** Section 4.3 rests on reek's measurement. My own account of
the same symptom was an ordering observation dressed as a mechanism, and
if I had written this paper from my own notes rather than from reek's
correction, Section 4.3 would have been weaker and wrong in its
attribution.

So here is the controlled comparison, in one tree, on one day:

| Guard | Enforcement | Outcome today |
|---|---|---|
| Findings outbox | Prose, **plus a hook that fires on the triggering event** | Fired twice, was obeyed twice, caught a real error in my reasoning |
| Rung 3 block | Prose only | Under discussion in this document |

The hook does not do anything clever. It does not understand workplans.
It notices that a specific event occurred -- a merge changed a workplan --
and it says, at that moment, read this now. It converts a standing
obligation into an interrupt at the point of relevance.

That is the entire mechanism I am recommending in Section 8. It is not
hypothetical, it is not expensive, and this project has already built one
and proved it works. **The rung 3 warning needs the same treatment: not a
louder document, but something that fires at the moment somebody reaches
for the flasher.**

The hook's own closing line is worth keeping, because it is a compressed
statement of why documents lose to events: *"a workplan read once at init
is remembered wrong, which is how a live entry gets missed."* Substitute
"run sheet" for "workplan" and "sitting" for "init" and it describes this
paper's subject exactly.

---

## 8B. The day's record, and why it does not read as carelessness

Section 10 states that I am not concluding fester was careless. That
deserves evidence rather than politeness, because a document written at
an angry instruction should be checkable on precisely the point where it
declines to blame.

Here is what fester's name is attached to on main today, from the
changelist descriptions:

| CL | What it is |
|---|---|
| 11977, 11983 | `PciProbe.codex`, the instrument for sitting question 2, plus a severity correction on how its output reads |
| 12010 | Measured that the `-16` mask was sound at the instruction level, **correcting reek's claim**, and absorbed both reek's entry and mine |
| 12024 | Corrected `OsHardwareRoadmap` to say a PCI config read is zero-extended, measured |
| 12033, 12035 | Fixed the flasher's spurious tail-blob failure |

Read CL 12010 closely, because it is the opposite of the behaviour the
angry reading would predict. reek had asserted that the xHCI relocation
readback could never succeed due to sign extension. fester did not accept
it, did not merely disagree with it, but went and measured it at the
instruction level: `port-in-32` emits `xor eax, eax` then `in eax, dx`, a
32-bit `in` clears the upper half of RAX, so the read zero-extends and
there is no value for which the two masks differ. fester then **ran a
negative control**, which reek's own retraction records:

> fester got there first and proved it at the instruction level (their
> 12010, absorbed) [...] They also ran a negative control.

Running a negative control on your own disproof is the specific discipline
`measurement-discipline` and `L-FALSIF` ask for and that most sessions
skip. It is what separates "my probe agreed with me" from "my probe can
disagree with me."

And CL 12033's reasoning, quoted in Section 4.4, is explicitly about not
wasting the operator's time during a sitting: *"a spurious failure there
reads as 'take the second stick' in the middle of a sitting."*

So on the same day this went wrong, the same agent measured rather than
argued, corrected a peer with evidence and a control, corrected a design
doc, absorbed two findings including one against their own prior account,
and fixed a flasher bug specifically to protect the sitting.

**None of that proves nothing went wrong.** Something clearly did, and
Section 11 lists what I could not determine about it. What it does is
make "carelessness" the least likely explanation on the evidence
available, and make the structural account in Sections 3 through 7 the
most likely one. An agent doing all of the above, and still handing over
an image that did not work, is close to a proof that the problem is not
effort.

There is one more reading worth putting on the record, because it is
consistent with everything above and nobody should have to guess at it
later. fester's item 1 says *"Re-flash and re-measure before diagnosing
anything else. It may simply be fixed."* If the A1 answer was the
objective, then running the rung was **the row**, not a lapse, and the
sheet's block is in tension with the workplan's instruction. Those two
documents disagree about whether rung 3 should be run today. The sheet
says do not. The workplan says re-measure, it may simply be fixed. Both
are on main, both are current, and **nothing reconciles them.**

That tension is worth more than any conclusion about conduct. Two
governing documents giving opposite instructions about the most expensive
action available is a defect in the documents, and it is the kind
`measurement-discipline` warns about directly: *"When a doc's header and
body disagree, believe neither. Go to the source."* Here it is not a
header and a body, it is two documents, and there is no source to go to
because the source is a judgement nobody has recorded.

---

## 9. What this cost, measured honestly

I want to avoid the trap of describing the cost in a way that inflates
it, because that is its own dishonesty.

What was spent: one sitting's worth of Damian's attention, some fraction
of a day, and a quantity of goodwill that I am not going to pretend to
measure but which is clearly nonzero, because the instruction that
produced this document was not a calm one.

What was **not** spent: the sitting's other rungs are unaffected. The run
sheet is explicit that *"Boots 1 and 2 are unaffected and still worth
doing on their own."* If the ladder was abandoned after rung 3 went
black, then questions 2, 3 and 4 are still open, and question 3 in
particular is the one that decides whether USB HID post-ExitBootServices
becomes a Track A blocker. That answer moves my own A3 row and reek's A4
row. Three agents are waiting on answers from a sitting that may have
been spent on the rung the sheet said to skip.

That is the real compounding cost, and it is worth stating in the terms
`L-HUMAN` uses: **the body was spent, and the questions it was scheduled
to answer may still be open.** If so, the next sitting costs another day,
and everything downstream of questions 3 and 4 waits for it.

---

## 10. What I am not concluding

Several things that would be easy to write and that the evidence does not
support.

**I am not concluding that fester was careless.** The evidence available
to me points the other way. fester wrote the warning block that this
document treats as the guard. fester fixed a flasher bug today
specifically reasoning about not wasting a human's time mid-sitting.
fester absorbed my `PixelFormat` finding and reek's mask finding and
recorded both accurately, including the parts that corrected fester's own
account. fester proved reek's sign-extension claim wrong at the
instruction level and ran a negative control while doing it. That is not
the profile of somebody being cavalier with a sitting.

**I am not concluding that the run sheet was ignored.** I do not know
that. There are several paths to a black screen that do not involve
skipping the block: a different image than the three the sheet builds; an
ad hoc rebuild during the sitting; a rung 1 or rung 2 probe that failed
for an unrelated reason; or a decision to run rung 3 deliberately,
knowing the risk, to get the A1 answer, which the sheet's own "may simply
be fixed" framing arguably invites.

**I am not concluding that the guard should have stopped it.** Section 7
argues the opposite: warnings of this kind have a documented history in
this tree of not holding, including on agents who had just read them.

**I am not concluding that this was avoidable with more care.** More care
is what the tree already has. What it lacks is a runner.

---

## 11. What I could not determine, and what would settle it

Stated explicitly so that a correction has somewhere to attach, and so
that nothing above is mistaken for something I verified.

1. **Which image was flashed.** The sheet builds three:
   `build/boot/xhci-probe.img`, `build/boot/kbd-probe.img`, and
   `seed/Codex.img`. "Broken" means something different for each.
   *Settled by:* the filename, or the SHA-256 the sheet instructs be
   recorded at Section 1 before leaving the dev box.
2. **Whether rung 3 was run, and if so whether the block was read.**
   *Settled by:* Damian, or fester's session.
3. **What was actually on the screen.** Black, garbage, a partial paint, a
   reboot loop, or firmware never listing the stick are five different
   failures with five different diagnoses, and the sheet has a table row
   for each. *Settled by:* the photograph the sheet asks for at every
   rung, whether or not it looked interesting.
4. **What verification the image received before it was handed over.** In
   particular whether it was booted under OVMF at all, and with which
   flags. Given Section 4.3, the flags are the whole question.
   *Settled by:* fester's workplan or the CL that produced the image.
5. **Whether the other sitting questions were answered.** Per Section 9
   this is the largest downstream cost and it is currently unknown to me.

I did not chase items 2 and 4 into fester's session. That is deliberate.
Reading a colleague's session to build a case about their conduct is a
different activity from diagnosing a process hole, and this document is
the second thing.

---

## 12. The lesson

Stated for `LESSONS.md`, in the form the index uses.

> **L-BODY** -- The most expensive step in the plan must not be protected
> by the weakest guard in the tree. If spending a human is gated only by
> prose, it is not gated.

And the corollary, which is the part I expect to be argued with:

> A warning that correctly predicts the exact harm it failed to prevent
> is not a good warning. It is a specification for a runner that nobody
> wrote.

The block in `HardwareSitting.md` is remarkable precisely because it is
so accurate. It says a human would see an unlit screen. It says they
could not tell it from a dead machine. It says the rung would spend them
on a known failure. Every one of those predictions is a testable
condition, and every one of them was available to a script.

That is the whole finding. The knowledge was present, current, correct,
specific, dated the same day, and written down in the governing document.
**It just had nothing to run it.**

---

## 13. What I would do next, if it is wanted

Not started, because Damian said stop all other work and this document is
the work. In priority order, and each is small:

1. **8.4, the blank-frame check.** Cheapest, most general, and it makes
   the existing "screenshotted before any flash" policy mean something.
   It is close to a GUI-golden check I already know the shape of, and my
   own workplan records that the GUI goldens have a blank-frame flake of
   about one run in eight, so the tree already needs this detector for a
   second reason.
2. **8.1, the blocked-image manifest.** Converts the guard that failed
   into a runner. Thirty lines, one source of truth, and the sheet
   renders from it instead of duplicating it.
3. **8.2, invert the OVMF defaults.** Larger blast radius because it
   changes what every existing invocation measures, which is the argument
   for doing it rather than against.
4. **Amend `BootRoadmap.md:373` now**, ahead of all of the above, because
   it currently overstates what a green OVMF run proves and it is quoted
   as authority by other documents. One paragraph, and it is the cheapest
   correction on this list.
5. **Reconcile `HardwareSitting.md` rung 3 with `fester-workplan.md` item
   1**, per Section 8B. One says do not flash the console stick; the
   other says re-flash and re-measure, it may simply be fixed. Whichever
   is right, they must not both stand. This is a decision rather than an
   edit, and it is Damian's or fester's, not mine, which is why it is
   listed and not done.

Item 4 I would do today without being asked, if the instruction to stop
were lifted, because a false claim in a design doc that other docs cite
is the failure mode `CLAUDE.md`'s own rule 11 correction exists to
illustrate.
