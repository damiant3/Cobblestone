# CORRECTION AT THE TOP: THIS PAPER ANSWERS THE WRONG QUESTION

**Added 2026-07-29 by blu, within the hour, after reading fester's
`TheStickDidNotBoot.md` (main CL 12045).**

**The image flashed to the ASUS was `pci-probe.img`, not `seed/Codex.img`.**
fester's account states it in its own subtitle: *"after `pci-probe.img` was
flashed to a 28.9 GB USB stick and the ASUS TUF did not boot it."*

`pci-probe.img` is built by `build/boot/build-option-a.ps1` with
`-Kernel seed/Codex.cdx`, from the current seed, at the moment it is built.
**Nothing in sections 1 through 14 below applies to it.** The staleness
argument, the 23 missing revisions, the COM2 panic printer, the
self-defeating A1 instruction: none of it is the cause of the failure Damian
sat through, because the artifact they concern was not on the stick.

I flagged this exact risk myself, in section 11 ("**Which image was flashed.**
Section 7 states this and section 12 gives the commands") and gave the two
commands that would settle it. I then let the title, the thesis in section 0,
and the whole of section 14 rest on the unestablished half anyway. **Naming an
assumption is not the same as declining to build on it**, and this document is
what that distinction costs. It is the same error the paper attributes to
others in section 9, committed in the act of attributing it.

### What survives, and it is worth keeping

The measurements are correct; only their relevance to this incident is not.

- `seed/Codex.img` **is** at depot revision #57 from CL 11000 (2026-07-27)
  while `seed/Codex.cdx` is at #582 from CL 11926. That is real, it is a
  loaded gun for the next sitting that flashes the console stick, and R8 will
  not catch it.
- That image's panic printer **is** still on COM2 while the run sheet tells
  the operator to listen on COM1 (section 14.1). Still true, still latent.
- R8 **is** a BLOCKING precondition with no runner, and the mtime trap in
  section 3.4 **does** hand a false yes to anyone who checks it the obvious
  way. Both stand.
- The document divergence in section 8 stands and is **strengthened** by the
  correction rather than weakened: fester's workplan calls `PciProbe.codex`
  "the instrument for question 2" and tells you to "flash that image", while
  `HardwareSitting.md` never mentions it and its ladder has three rungs, none
  of them the PCI probe. **The image that was actually flashed was the one
  that is not on the run sheet.** So it never went through step 1's digest
  recording or step 2's telemetry proof, and the sheet's abort conditions and
  symptom tables did not cover it. That is the part of section 8 that turned
  out to matter, and I ranked it fourth.

### What to read instead

For why the stick did not boot, read fester's `TheStickDidNotBoot.md`. His
thesis is *"I do not know why the stick did not boot, and the reason I do not
know is a process failure that is mine"*, and he declines to name a cause he
cannot support, explicitly refusing to write twenty pages naming
`AllocatePages` or FAT16 geometry because a guess dressed in structure is
still a guess. He is right, he reached the structural conclusion independently,
and his concrete remedy is better than any of mine: have the stub paint one
solid colour after GOP acquisition and another before jumping to `opening`, so
a black screen becomes a positioned failure readable from across the room with
no camera and no decoding.

### What fester fixed before this paper reached main

Checked at main CL 12083, and it belongs here rather than in a footnote,
because section 8 criticises a document he has since moved.

**`HardwareSitting.md` is now at revision #5, and the two-colour remedy is
built.** Every Option A image paints two liveness marks from
`option_a_stub.asm`, and the sheet gained a table that reads them: firmware's
own screen unchanged means never loaded or `LocateProtocol(GOP)` failed and it
is a boot-selection problem rather than a payload one; solid dark blue means
GOP acquired and died in `AllocatePages`, `GetMemoryMap` or
`ExitBootServices`; solid dark green means through ExitBootServices and our own
page tables with a writable handoff framebuffer, died in the payload. His note
records that **both marks were confirmed by ablation under OVMF -- halt
immediately after each, screen fully that colour -- and not by inspection**,
which is the right way to establish that a liveness mark is alive. The old
row that read *"Nothing, no output, immediate halt"* and named
`AllocatePages` as the cause now reads *"Solid dark blue | the PE stub's
`AllocatePages` is the leading suspect"*, with the previous conflation called
out in the row itself.

So he proposed the fix in his own paper, built it, verified it by ablation, and
updated the governing document, inside the same afternoon. **Section 8's
measurement was true at the time of the incident and is no longer the current
state.** The general point about a governing document drifting from the lane it
governs stands; the specific instance has been closed by the person it was
about, faster than this paper took to write.

**Two of section 8's findings do still hold**, checked at the same revision:

- **R8 still has no runner.** Lines 23 and 25 are unchanged: the blocking
  precondition is still discharged by "Ask red, do not infer", with no
  command and no exit code. Section 10.4 is still the fix.
- **`PciProbe` is still not on the ladder.** Zero mentions in the run sheet at
  #5. This is the sharp one, because `pci-probe.img` is the image that was
  actually flashed, so the artifact that went to the ASUS is still the one the
  governing document does not cover: no step-1 digest recording, no step-2
  telemetry proof, and no symptom table or abort condition written for it.
  Of everything in this paper, that is the finding I would act on next.

**Read the rest of this document as what it is: a correct and unaddressed
defect report about `seed/Codex.img`, filed under a title about an incident it
did not cause.** Sections 10.1 through 10.5 are still worth doing. Section 13,
the one-paragraph answer, is answering the wrong question and should be
disregarded.

---

# The Image That Was Two Days Old

*How a stale distribution artifact reached the one device we cannot schedule,
and why the agent who sent it there had every reason to think it was current.*

Written 2026-07-29 by blu, at Damian's direction, after a sitting was spent
on a broken USB stick image.

---

## 0. What this document is, and what it is not

Damian asked why fester did that. The question deserves a real answer, and a
real answer here is mechanical rather than moral. I can establish from the
depot what was on the stick, what it was missing, which instruction sent it
there, and which check should have stopped it. I cannot establish what fester
intended, and I am not going to fill that gap with a guess dressed as a
finding. This project has an expensive and well documented habit of asserting
where it could have measured, and a post-mortem that commits the same error
while investigating it would be worthless.

So this is a defect report about a process, with an agent standing at the
point of failure. The conclusion, stated once here and defended for the rest
of the document, is this:

> **fester told the operator to re-flash and re-measure in order to verify
> CL 11926. The artifact he told him to re-flash cannot contain CL 11926, and
> has not been rebuilt since 2026-07-27. Nothing in the tree could have told
> either of them that, because the one document that names this exact failure
> lists it as a blocking precondition and supplies no way to check it.**

That is not a story about carelessness. It is a story about a build artifact
living in version control, a mandatory `p4 sync -f`, and two timestamps that
agree when the files behind them do not. Any of us would have shipped it. One
of us did, and it happened to be fester.

There is a second, smaller conclusion, and it belongs up front too, because
it is the part that is genuinely fester's judgement rather than the system's:
the run sheet that governs the sitting is 170 changelists stale relative to
the lane it governs, and fester owns both documents. That is addressed in
section 8.

---

## 1. The finding

The stick image `seed/Codex.img` is a tracked binary artifact in the depot. As
measured today:

| Artifact | Depot revision | Last changed by | Date |
|---|---|---|---|
| `//Codex/main/seed/Codex.cdx` | **#582** | CL 11926 | 2026-07-29 08:00:18 |
| `//Codex/main/seed/Codex.img` | **#57** | CL 11000 | 2026-07-27 |

Between those two points, `seed/Codex.cdx` was submitted **23 times**, from
revision 560 at CL 11009 through revision 582 at CL 11926. None of those 23
seed revisions is present in `seed/Codex.img`, because building a seed does
not build the image. They are separate artifacts produced by separate
scripts, and only one of them was run.

The newest of the 23 is the one that matters. CL 11926 is described in the
depot as:

> copy-up from //Codex/fester: UEFI firmware-call ABI fix + ExitUefi wiring,
> with the seed cycle it needed. [...] the UEFI firmware-call sequence saved
> no RBX while clobbering it with the protocol pointer (mov rbx, rcx). RBX is
> callee-saved on our side [...] so the sequence could corrupt a caller's
> local across a firmware call.

And fester's own workplan, item 1, says this about the reboot loop that has
been blocking Track A:

> **What changed underneath it: CL 11926.** The UEFI firmware-call sequence
> was clobbering RBX with the protocol pointer and never saving it, and RBX
> is callee-saved on our side. A corrupted callee-saved register in that path
> is the right shape for a payload that returns to BDS instead of running.
> **Re-flash and re-measure before diagnosing anything else.** It may simply
> be fixed.

Read those two facts together. The instruction is to re-flash and re-measure
so as to find out whether CL 11926 fixed the reboot loop. The artifact that
gets flashed was built two days before CL 11926 existed. **The measurement
cannot observe the change it was ordered to evaluate.** Whatever the stick
did on the ASUS, it was reporting on a compiler from 2026-07-27, and any
verdict drawn from it about CL 11926 is void in both directions: it could not
have shown the fix working, and its failure is not evidence the fix is
insufficient.

This is the whole of the defect. Everything below is how it came to be
invisible.

The other 22 revisions are not filler, and section 14 measures them. One of
them is worse than CL 11926: the panic printer moved to COM1 at main 11833,
the run sheet tells the operator to listen on COM1 because of it, and this
image still panics to COM2. Three of them are fixes to unchecked out-of-range
memory access. Read section 14 before deciding how much this cost.

---

## 2. A correction, made during this investigation

I want this on the record because the shape of my own error is the shape of
the error I am documenting, and it took me one command to catch and would
have taken a reader a day to catch if I had published it.

My first pass at the staleness number compared the two revision counts
directly: `Codex.cdx` at #582 against `Codex.img` at #57, and I wrote down
that the image was "525 revisions behind". That is meaningless. They are
revision counters on two different files. A file with 57 revisions is not
behind a file with 582 revisions by 525 of anything; the two counters have no
common unit. The number was large, it pointed the right way, and it was
arithmetic performed on unrelated quantities.

The honest measure required actually walking the seed's history and counting
the revisions submitted after CL 11000, which is 23. That number is smaller
and much worse, because counting it forced me to look at *which* 23, and the
newest one turned out to be the precise change fester's instruction was
written to verify. The wrong number was rhetorically stronger and told me
nothing. The right number identified the mechanism.

`CLAUDE.md` rule: **never carry a count forward, re-measure it.** The rule is
usually read as a warning about stale counts copied between documents. This
was a fresh count, measured minutes earlier, and still wrong, because the
measurement answered a different question than the one I asked. That is worth
adding to how the rule is understood.

---

## 3. How the artifact goes stale

Three mechanisms compound. Each is defensible alone.

### 3.1 The image is built by a different script than the seed

`build/build-boot-img.ps1` is the only thing that produces `seed/Codex.img`.
Its parameters make the situation explicit:

```powershell
    # Where to write. Defaults to the depot artifact; point it elsewhere to
    # exercise this script without touching seed/Codex.img.
    [string]$Out = ''
```

and

```powershell
$Seed   = Join-Path $Repo 'seed\Codex.cdx'
$ImgOut = if ($Out) { $Out } else { Join-Path $Repo 'seed\Codex.img' }
```

So the image is a *function of* the seed, computed on demand, defaulting to
overwrite the depot copy. Nothing recomputes it when the seed changes. The
standing gate, `build/build.ps1`, does not run it. It has no reason to: the
gate proves the compiler is a fixed point of itself, and the distribution
image is not part of that proof. This is a correct division of labour that
happens to leave a derived artifact unowned.

### 3.2 The image is under version control

```
... depotFile //Codex/blu/seed/Codex.img
... headType binary
```

A derived artifact tracked as a source artifact acquires source semantics. In
particular it acquires the behaviour in 3.3.

### 3.3 `p4 sync -f` silently reverts a local rebuild

The gate dance in `docs/Agents/PerforceProcess.md` is mandatory before any
build, and its second step is:

```powershell
# 2. Force-sync to guarantee clean (handles stale/missing files)
p4 sync -f
```

That is the right instruction for source. Applied to `seed/Codex.img` it
means: **any locally rebuilt image is destroyed and replaced by depot
revision #57, without a word.** The force-sync is not doing anything wrong.
It is restoring a tracked file to depot state, which is its entire purpose.
The file simply should not have been in the set it operates on.

Note what this does to the ordering. An operator who follows the run sheet
correctly, builds a fresh image at step 1, and then does anything at all that
involves a force-sync -- rebasing, taking a colleague's fix, running a
verification build, following the gate dance for an unrelated reason -- has
their fresh image replaced by the stale one and receives no signal. The
correct action and the destructive action are separated by minutes and by no
warning.

### 3.4 The two timestamps agree, so the disk cannot answer the question

This is the part that makes the failure undetectable by inspection. On disk,
right now:

| File | Size | LastWriteTime |
|---|---|---|
| `seed/Codex.cdx` | 2,714,156 | 7/29/2026 2:07:57 PM |
| `seed/Codex.img` | 16,777,216 | 7/29/2026 2:07:57 PM |

Identical, to the second. Any reasonable person checking "is my image current
with my seed?" looks at the modification times, sees them match exactly, and
concludes the image was built from that seed moments ago. The truth is that
both timestamps record when `p4 sync -f` wrote the files onto the disk, and
carry no information whatsoever about when either was produced. The image is
two days old and its mtime says it is as fresh as the compiler.

**A derived artifact under force-sync has a modification time that is
actively misleading rather than merely uninformative.** It does not fail to
answer the freshness question; it answers it wrongly, with confidence, in the
one direction that causes harm.

---

## 4. Why nothing caught it

The tree does contain a check for exactly this. `docs/Hardware/HardwareSitting.md`,
section 0, "Preconditions -- BLOCKING":

> | **R8** | `seed/Codex.img` refreshed from the current seed | **A seed
> rebuild does NOT refresh the img.** It is a separate distribution artifact
> built by `build/build-boot-img.ps1`. A stale img is the failure that looks
> like a compiler bug |
>
> If either is open, the sitting is not scheduled. Ask red, do not infer.

Every word of that is correct. It names the artifact, names the script, names
the reason, and predicts the symptom precisely: *a stale img is the failure
that looks like a compiler bug*. Somebody understood this completely and
wrote it down at the top of the governing document, under a heading that says
BLOCKING, before anything else.

And it did not work. Here is why.

### 4.1 The check has no instrument

R8 is a claim to be established by asking a person: "Ask red, do not infer."
There is no command. There is no script that compares the image against the
seed and refuses. There is no gate step. The precondition is discharged by
someone saying yes.

This is precisely the failure mode the tree's own lesson index calls
L-FALSIF: *an instrument that cannot fail is not evidence.* A precondition
whose verification procedure is "ask a colleague" cannot fail in any way that
produces a red light. It can only fail by someone answering from memory, or
from the timestamps in 3.4, or by nobody being asked because the sitting felt
like a continuation of yesterday's rather than a new one.

Worse: the honest answer to R8 requires knowing that mtime lies. Anyone who
checks R8 the obvious way gets a false yes. The check is not merely
unenforced, it is booby-trapped for the diligent.

### 4.2 The run sheet does tell you to rebuild, and that makes it worse

Step 1 of the same document is right:

```powershell
# The stick itself.
build/build-boot-img.ps1
```

So the sheet is not missing the rebuild. Followed literally, top to bottom,
in one session, with no force-sync in the middle, it produces a correct
image. The sheet is a good document.

But that is a fragile property to depend on, and it interacts badly with 3.3.
The sheet assumes its steps are the only thing happening. In a fleet where
five agents merge down and force-sync continuously, and where the operator's
own workspace is one of those trees, "build it at step 1 and flash it at step
3" spans an interval in which a force-sync is likely rather than
hypothetical. The document is correct and the environment invalidates it
between two of its own steps.

### 4.3 The digest discipline was designed to catch this, and was aimed elsewhere

Step 1 continues:

> Each build prints the kernel it used. **Read that line.** It must say
> `seed\Codex.cdx` and the seed's digest. If it prints a NOTE that the kernel
> is not the seed, the `-Kernel` argument did not take and the artifact is
> untraceable.
>
> Record the three digests before leaving the dev box. They are what a later
> disagreement is settled against.

This is real provenance discipline and it is well designed. Note what it
proves and what it does not. It proves *the payload was built by the seed*,
which defends against the wrong compiler. It does not prove *the image on the
stick is the image that was built*, which is the failure that actually
occurred. The digests are recorded at the dev box in step 1; the flash happens
in step 3; nothing re-checks the digest of the bytes going onto the stick, and
nothing re-checks it after the flash against the digest recorded before it.

A single `Get-FileHash seed/Codex.img` immediately before the flash, compared
against the digest written down in step 1, would have caught this. The
material was all present. The comparison was not asked for.

---

## 5. The self-defeating instruction, in full

Section 1 stated it. It is worth laying out completely, because this is the
part that answers Damian's question most directly and it is not fester being
sloppy. It is two correct sentences that combine into an impossible one.

fester's A1, sentence by sentence:

1. *"`seed/Codex.img` reboot-looped under real UEFI: 159 attempts in 360
   seconds, nothing painted, not one byte on COM1, while the Option A stub
   booted on the same firmware. That measurement stands."*

   True and carefully qualified. He even preserves the control (the Option A
   stub booting on the same firmware), which is the thing that makes the
   measurement mean something rather than being a report about the board.

2. *"**What changed underneath it: CL 11926.**"*

   True. CL 11926 is real, it touches exactly the path implicated, and
   fester's diagnosis of why an unsaved callee-saved RBX produces "a payload
   that returns to BDS instead of running" is a good piece of reasoning.

3. *"**Re-flash and re-measure before diagnosing anything else.** It may
   simply be fixed."*

   Correct as methodology, and exactly what rule 3 of `CLAUDE.md` asks for:
   do not spend a rebuild cycle confirming a speculation. Re-measure first.

Each step is sound. The conjunction is not, and the reason is invisible from
inside any of the three: **"re-flash" resolves to an artifact that is not
downstream of CL 11926.** The word "re-flash" carries an implicit "the
current image", and the image is not current, and nothing at the point of
use says so.

For the instruction to be sound it needed a fourth sentence: *rebuild
`seed/Codex.img` first, because the seed moved and the image did not.* That
sentence exists in the tree. It is R8, in the other document, four sections
away, under a heading about scheduling rather than about doing.

**Two correct documents, each missing the other's context, produce an
instruction that cannot succeed.** That is the answer to why fester did that.
Not inattention: a seam between two documents, with the load-bearing fact on
the far side of it.

---

## 6. What fester got right, and why it matters here

A post-mortem that only lists failures teaches the wrong lesson, and there is
a specific reason to be careful in this one: fester's recent work is unusually
good, which is itself evidence about the nature of the failure. If a careful
agent producing high-quality measurements walks into this, the trap is
structural.

**The flasher fix, CL 12033 and 12035, is the strongest counter-evidence to
carelessness.** Read what it is. The flasher wrote and flushed a perfect
16,777,216-byte image, verified every byte, and then threw
`The drive cannot find the sector requested` on the readback of the two
SpecFit blobs at the disk's tail. Exit code 1 on a good stick. fester
diagnosed it as the verifier rather than the medium: the blob readback reused
a `FileStream` with a 1 MB buffer, and on a raw device a buffered read
positioned 34 sectors from the end issues a 1 MB `ReadFile` that runs past
the last sector. He then did the thing that separates a diagnosis from a
guess: before changing anything, he confirmed the writes had actually landed
by reading the three tail sectors back unbuffered and checking the *structure*
rather than re-running the script's own arithmetic, recording
`AlternateLBA=60506111`, backup header `MyLBA=60506111 AlternateLBA=1
EntryLBA=60506078`, and the ESP type GUID
`C12A7328-F81F-11D2-BA4B-00A0C93EC93B`. He also noticed that the blobs at LBA
0 and 1 verified fine, and called out that this is *what makes it look like
tail corruption* -- naming the misleading appearance rather than being led by
it. Then he added a short-read check, because the loop could previously fall
out of a partial read and compare a zero-filled tail.

And his stated reason for fixing it rather than noting it:

> `-SpecFit` is what makes firmware list the stick at all,
> `HardwareSitting.md` tells the operator to re-flash before every boot, and a
> spurious failure there reads as 'take the second stick' in the middle of a
> sitting.

That is an agent thinking specifically about not wasting the operator's
sitting. Hours before the sitting was wasted for an unrelated reason.

**The OVMF window measurement** (his outbox entry to red) is the same
quality: he tried 2048 MB, 3584 MB, and
`-machine q35,max-ram-below-4g=3G`, noted that QEMU accepted the last one
without complaint and the NIC still came back `B0=81060000`, concluded the
idea does not work, and explained the mechanism -- QEMU splits guest RAM
below and above 4 GB rather than growing the low half. He then labelled the
provenance of his alternative honestly: the codex-vm BAR addresses are *"read
off `tools/codex-vm.c`'s `pci_add_device` calls, not measured through my
probe"*. Stating which of your own facts is read rather than measured is rare
and it is exactly right.

**He absorbed other lanes' findings promptly.** CL 12010's description reads
*"Absorbs reek's xHCI entry and val's OVMF-keyboard entry"*, and both appear
in his workplan as items with the finder credited. He also went and checked
reek's claim rather than accepting it, established that the `-16` mask was
sound, measured that `bit-and rb (-16)` and `bit-and rb 4294967280` both give
`4269801472` for `rb = 0xFE800004`, ran a negative control that fired, and
told reek which row of their table remained genuinely unexplained. That is
better practice than most of the fleet manages.

None of this is a defence of the stale image. It is evidence about the
category of failure. **An agent who unbuffers a readback to avoid wasting
someone's sitting, and who labels his own unmeasured facts, is not an agent
who could not be bothered to rebuild an image.** He did not know, and the
tree gave him a false yes if he checked.

---

## 7. The other thing on the stick: what the run sheet already knew

There is a second defect in the vicinity, and it deserves separate treatment
because it is not the same failure and conflating them would muddy both.

`docs/Hardware/HardwareSitting.md`, Boot 3, carries an explicit block:

> **DO NOT RUN THIS RUNG YET, and do not flash the console stick.**
> The reason changed on 2026-07-29 and the rung is still not ready.

and the reason given:

> **What still blocks this rung: that output goes to COM1 and the screen
> stays black.** `uefi-con-put-text` lowers to `__serial_put`, which is
> codex-vm's blit cell or a COM1 `out`; there is **no ConOut path**, so on
> real firmware nothing reaches the UEFI text buffer. codex-vm draws the
> console itself, which is exactly why this never showed there. **A human
> sitting down today would see an unlit screen and could not tell it from a
> dead machine, so the rung would still spend them on a known failure.**

So the sheet had already worked out, and written down in the imperative, that
booting the console image today spends a human on a known failure and looks
exactly like a dead machine. It even diagnosed why the emulator never showed
it: codex-vm draws the console itself, so the missing ConOut path is invisible
under emulation. That is a good piece of analysis and a correctly placed
warning.

If the image that was flashed was `seed/Codex.img`, then the operator was
sent into a rung the governing document forbids, and the black screen he saw
was over-determined: the sheet predicted a black screen from the missing
ConOut path, *and* the payload was two days stale, *and* fester's A1 framed
the expected symptom as a reboot loop rather than as an unlit screen. Three
independent reasons to see nothing, with three different remedies, and no way
to tell them apart from the chair.

**That last point is the real cost of the stale image, and it is worse than
the wasted boot.** A boot that fails for one known reason is cheap: you learn
the reason is still there. A boot that fails for three possible reasons at
once, one of which nobody knows about, produces a datum that cannot be
attributed. It does not just fail to advance A1; it contaminates the next
attempt, because the natural next step is to diagnose the ConOut path, and the
staleness would survive that entire investigation untouched and still be
there at the end of it.

I want to be careful about one thing: **I do not know which image was
flashed.** Damian said a broken stick image; I have identified the artifact
that is demonstrably broken right now and reachable by following fester's
documents. Section 12 gives the two commands that settle which one it was, and
they should be run before this document's section 7 is treated as established
rather than as the leading hypothesis.

---

## 8. The document divergence, which is fester's to answer for

Sections 1 through 7 describe a structural trap. This one is a judgement call
that went wrong, and it should not be laundered into the structure.

Measured today:

| Document | Depot revision | Last changed |
|---|---|---|
| `docs/Hardware/HardwareSitting.md` | **#4** | CL 11870 |
| `docs/Agents/fester-workplan.md` | **#124** | CL 12032 |

Main was at CL 12041 when this was written. The workplan has been revised
four times since the run sheet last moved: CL 11977, 11983, 12010, 12032.
Both documents are fester's. One governs the sitting -- his own workplan says
so, at line 13: *"`docs/Hardware/HardwareSitting.md` governs: every question
answerable before the sitting is answered before it."*

So the document he declares authoritative for the scarcest resource in the
project is the one he has stopped updating, while the document that records
his live thinking has moved four times in the same period. The consequences
are concrete:

**8.1 The instruments diverge.** fester's workplan item 2 says:

> The instrument for question 2 is built and verified:
> `build/boot/diag/PciProbe.codex`, an Option A GOP payload that walks bus 0
> and every bridge below it and reads out on screen and as QR. Build it with
> `-Kernel seed/Codex.cdx` and flash that image.

`PciProbe.codex` appears nowhere in `HardwareSitting.md`. The run sheet's step
1 builds `XhciTruthProbe.codex` and `KbdDiagProbe.codex`, and its ladder has
three rungs, none of which is the PCI probe. So the sheet that governs omits
the instrument the lane calls *the* instrument for one of the four questions
the sitting exists to answer, and an operator following the sheet literally
would not build it, would not flash it, and would come home without the PCI
IDs that both red and blu are waiting on. An operator following fester's
verbal or workplan direction instead would flash an image that never went
through the sheet's digest recording at step 1 or its telemetry proof at step
2.

**8.2 The expected symptom diverges.** A1 says reboot loop, 159 attempts in
360 seconds, nothing painted. Boot 3's block says the console now *runs*,
boots once, indexes, paints a live header with a clock, and the problem is
that the output goes to COM1 so the screen stays black. Those are different
failures with different diagnoses. An operator carrying A1's expectation to
the chair is watching for a machine that resets repeatedly; what the sheet
predicts is a machine that sits there quietly having succeeded, with an unlit
screen. If you are expecting a reboot loop and you get a still black screen,
the natural reading is "worse than before".

**8.3 The staleness warning is in the stale document.** R8 is in
`HardwareSitting.md`, which has not moved since CL 11870. The instruction that
needed R8's context is in the workplan, which has moved four times since. The
warning is structurally positioned to be missed by exactly the person who
needed it.

None of 8.1 through 8.3 requires knowing fester's intent. They are properties
of two files. **Keeping the governing document current is the whole of the
duty that comes with declaring it governing**, and that did not happen.

---

## 9. Why this is a pattern and not an incident

The tree has already paid for this shape more than once, in other lanes, this
week. That is the strongest argument that the fix must be structural.

**val, 2026-07-29:** a workplan row asserted that `GuiShell.codex:124`
hardcodes codex-vm's `0xBF000000` and forces the row stride to the visible
width, so guios could not render on real firmware, and the repair was small.
val measured, and both sentences about the source were true while the
conclusion did not follow: guios is not on the metal boot path at all,
`GopBoot`'s "Graphical UI" item dispatches to `GopDesk`, and `GuiShell` is not
in the Option A image. Acting on the row would have shipped a no-op.

**red, on this lane's own request, 2026-07-29:** the `E1000e` chapter's prose
says *"an empty poll allocates nothing at all"* and *"a poll that finds
nothing touches no memory it does not own and builds no list"*. The list part
is true; the allocation part is false, because the early-out returns a
module-level record and referencing one re-allocates it. 32 bytes per empty
poll, against a documented zero.

**blu, today, in a description I wrote myself:** CL 12013's description claims
the receive path allocates nothing it did not before. True on one branch,
false on the other, because I measured the send side and asserted the receive
side from reading it. I published that in the same changelist description that
warns the branch is unproven.

**reek, on the emulator:** codex-vm's BOT model sets `csw_status = 0` on every
recognised command and has no unit-attention state, so `usb-bot` passed for
its whole life without ever exercising the CHECK CONDITION handshake every
real target requires.

Four lanes, one week, one shape: **a written claim about a system, not
re-checked against the system, believed because it was written down.** R8 is
the same shape with the polarity reversed -- a written claim that was *true*
and that nothing re-checked, so it decayed from a check into a sentence.

The generalisation worth extracting:

> **A precondition with no runner is a comment.** It does not matter how
> correct it is, how prominently it is placed, or how emphatically it is
> formatted. `CLAUDE.md` says this about itself, in rule 12: an assertion with
> no runner is the exact failure `LESSONS.md` describes. R8 is a BLOCKING
> precondition in bold, in a table, in section 0, with the symptom correctly
> predicted, and it is a comment, because nothing executes it.

---

## 10. What would have caught it

Cheapest first. Every one of these is a real option; the first two are worth
doing today.

**10.1 One line, in the flasher.** `build/flash-usb.ps1` already knows the
path of the image it is about to write. Before writing, compare the image's
mtime against `seed/Codex.cdx`, and if the image is older, refuse unless
`-Force`. This is three lines and it catches the exact failure at the exact
moment it matters, in the tool the operator is already running as
Administrator. fester was in this file today.

**10.2 Provenance inside the artifact, not beside it.**
`build/build-boot-img.ps1` should stamp the seed's digest into the image it
builds, and `flash-usb.ps1` should read it back out and print it. Then "which
compiler is on this stick?" is answerable from the stick, and the answer
survives a force-sync, a re-insertion, and a two-day-old mtime. This is the
same reasoning that made the digest discipline in run-sheet step 1 correct,
extended past the boundary where it currently stops. Note that this is the
only proposal here that would still work if the file were copied to another
machine, which is the natural next thing to happen to a stick image.

**10.3 Take the derived artifact out of the force-sync's path.** Either stop
tracking `seed/Codex.img` in the depot, or move the operator's copy to a
build output directory that `.p4ignore` covers, and have the run sheet flash
from there. The current arrangement gives a derived file source semantics and
a misleading mtime, and 3.3 and 3.4 both disappear if it stops being a
tracked file. This is the deepest fix and the one with the widest blast
radius, so it is Damian's call rather than an agent's; the question is
whether a bootable image is a distribution artifact worth pinning in version
control at all, given that it is exactly reproducible from a seed that already
is.

**10.4 Give R8 a runner.** A five-line `build/check-img-fresh.ps1` that exits
non-zero when the image predates the seed, called from the run sheet's step 1
and from the flasher. R8 stops being a sentence and becomes a check. Until it
has an exit code it is not a precondition, whatever the table says.

**10.5 Re-verify digests at the point of flash.** The run sheet already
records three SHA-256 digests at step 1 and already knows they are *"what a
later disagreement is settled against"*. Compare against them in step 3,
immediately before the write, and again after. The material is already
gathered; only the comparison is missing.

**10.6 Reconcile the two documents, and date the reconciliation.** Fold A1's
expected symptom into Boot 3's block or delete it, put `PciProbe` on the
ladder or say why it is not there, and add to the run sheet the revision of
the workplan it was last checked against. A governing document should carry
the date it was last known to agree with the lane it governs.

---

## 11. What I cannot know, and will not guess

For completeness, and so that no one mistakes the confident parts of this
document for the whole of it:

- **What fester actually said to Damian.** If there was a message, a note, or
  a verbal handoff, it is not in the depot and I have not seen it. Everything
  in sections 5 and 8 is drawn from committed documents. If fester told him
  something more specific, or warned him, that changes section 5 materially.
- **Which image was flashed.** Section 7 states this and section 12 gives the
  commands. My hypothesis is `seed/Codex.img`, because it is the artifact A1
  points at and the one that is provably stale, but I have not established it.
- **Whether fester checked R8 and got a false yes, or did not check it.**
  These are different failures with different fixes. The timestamps in 3.4
  mean a diligent check produces a false yes, so I cannot distinguish
  diligence-defeated-by-a-trap from a skipped step, and the difference matters
  to fester.
- **Why the run sheet stopped being updated at CL 11870.** Section 8
  establishes that it did and what that cost. It does not establish why, and
  there are innocent explanations, including that the four intervening
  changelists all felt like lane notes rather than sitting procedure.

---

## 12. How to settle the open questions

Two commands establish which image was on the stick, and they should be run
before section 7 is treated as fact:

```powershell
# What is the depot's image, and when was it last built from a seed?
p4 filelog -m 3 //Codex/main/seed/Codex.img

# Does the image on disk match the depot's stale revision, or a local rebuild?
p4 diff2 -q //Codex/main/seed/Codex.img@=11000 //Codex/main/seed/Codex.img
Get-FileHash seed/Codex.img -Algorithm SHA256
```

A binary compares exact through `p4 print -o`, which per
`PerforceProcess.md` is the tell: if the on-disk image hashes to depot
revision #57, it was never rebuilt, and section 7 stands as written.

And the reproduction of the core finding, which needs no hardware:

```powershell
p4 filelog -m 1 //Codex/main/seed/Codex.cdx     # expect #582, CL 11926
p4 filelog -m 1 //Codex/main/seed/Codex.img     # expect #57,  CL 11000
Get-Item seed/Codex.cdx, seed/Codex.img | Select-Object Name, LastWriteTime
```

The third command is the one to look at hardest. The two timestamps will
agree, and they will both be wrong about the question you are asking.

---

## 13. The one-paragraph answer

Damian asked why fester did that. He did it because his diagnosis was right,
his methodology was right, and the word "re-flash" pointed at a file that
looked current, was two days old, could not contain the fix he was asking to
have verified, and carried a modification time that actively said otherwise.
The check that would have caught it exists, names this failure exactly,
predicts its symptom correctly, sits in bold under a heading that says
BLOCKING, and has no exit code, so it is a comment. The document containing
that check is the one he declared governing and then stopped updating, which
is the part that is genuinely his. The rest of it is a derived artifact living
in version control underneath a mandatory force-sync, and it was going to
spend somebody's sitting eventually. It spent this one.

---

## 14. What the stick was actually missing, measured

Section 1 established that 23 seed revisions are absent from the image, and
rested its argument on the newest one. That understates the cost, and the
understatement is worth correcting, because two of the other 22 interact with
the sitting directly and one of them is worse than CL 11926.

The 23, in order:

| Rev | CL | What landed |
|---|---|---|
| 560 | 11009 | `is-letter` takes the letters of other languages |
| 561 | 11058 | Text and character ordering refused (CDX2089) |
| 562 | 11098 | **`substring` was an unchecked heap read and traps now** (three guards) |
| 563 | 11112 | **`char-at` / `char-code-at` trap on an out-of-range index** |
| 564 | 11179 | **`list-at` / `list-set-at` / `list-insert-at` trap out of range**; two LIR verifier over-reads |
| 565 | 11186 | CDX6013 reports a punctual budget the inliner made unenforceable |
| 566 | 11198 | `vec-extract` / `vec4-extract` refuse an out-of-range literal lane |
| 567 | 11231 | `SecretEntry` name sort takes collation |
| 568 | 11244 | **unit equality compared POINTERS**; `==` and `/=` strip the wrapper |
| 569 | 11295 | CCE bands agree across x86 / arm64 / riscv, G1 closed |
| 570 | 11300 | strip the unit wrapper at two emit sites |
| 571 | 11322 | refuse field access through a unit wrapper (CDX2095) |
| 572 | 11360 | refuse a wrapping band that is not its hardware width (CDX1073) |
| 573 | 11471 | **CPU-state builtins: `cr0`, `cr3`, `cpuid` x4** |
| 574 | 11479 | overflow-mode check decided by the band, not by what encodes |
| 575 | 11498 | `print-text` builtin, raw print with no terminator |
| 576 | 11500 | `emit-ir-cce` streams def-by-def, VM capture/input caps |
| 577 | 11582 | Real ordering and `show` dispatched on only some of four Real modes |
| 578 | 11773 | Real collapse: four constructors become one carrying width and mode |
| 579 | 11833 | **OOM handler COM1 + `cli`, exc-stack-heap green** |
| 580 | 11881 | builtins bound once per site, 194,784 B heap per compile |
| 581 | 11918 | `Builtins` gains `bs-varies` and the constant-invariance analysis |
| 582 | 11926 | **UEFI firmware-call ABI fix (RBX save) + ExitUefi wiring** |

Four of those deserve to be pulled out.

### 14.1 The panic printer is on the wrong port, and the run sheet says so

This is the one that is worse than CL 11926, and it is a direct trap laid for
the operator by the run sheet's own advice.

CL 11833 is *"OOM handler COM1 + cli"*. And `HardwareSitting.md`'s Boot 3
block, written for this sitting, says:

> Attach both serial ports when you run it -- the panic printer moved from
> COM2 to COM1 at main 11837, and one attached port is how this stayed
> invisible.

The sheet is telling the operator, correctly, that the panic printer now
lives on COM1. **The image predates that move.** Its panic printer is still on
COM2. So the run sheet's guidance about where to listen is calibrated to a
seed the stick does not contain, and an operator who attached the port the
sheet named would be watching the one port the stale payload never writes to.

Compound that with section 7. If the payload panicked on the ASUS, the
operator would see: a black screen (predicted, from the missing ConOut path),
nothing on COM1 (because this image panics to COM2), and no QR codes (the dev
console has no QR channel, as the sheet states). Three silent channels, one
of them silent for a reason nobody in the fleet had written down anywhere,
because it only exists in the gap between the image and the seed.

That is not a wasted boot. That is a boot which, had it produced a defect,
would have been undiagnosable by every instrument the run sheet lists, and the
absence of output would have been read as evidence about the board.

The related half of CL 11833 is the `cli` before `hlt` in the OOM handler,
which reek's own outbox entry explains is needed because that path is reached
by a jump from a function prologue with interrupts on. A stale image is
therefore also missing the fix that stops the OOM handler from being
interrupted mid-panic.

### 14.2 The image contains an unchecked heap read

CL 11098: *"`substring` was an unchecked heap read and traps now (three
guards)"*. CL 11112 and 11179 are the other half of the same campaign:
`char-at`, `char-code-at`, `list-at`, `list-set-at`, `list-insert-at` all trap
on an out-of-range index now, and 11179 also carried *"two LIR verifier
over-reads"*.

So the payload on the stick, running on bare metal with no OS and no memory
protection worth the name, contains a family of unchecked out-of-range
accesses that the current compiler traps. On a board being probed for the
first time, where every input is unfamiliar and half the code paths have never
executed, an unchecked heap read is exactly the defect that presents as
inexplicable behaviour rather than as an error. Any anomaly observed during
that sitting has this as a candidate explanation and no way to rule it out.

### 14.3 The image lacks the CPU-state builtins

CL 11471 added `cr0`, `cr3`, and four `cpuid` builtins. Those are precisely
the instruments a bring-up campaign on unfamiliar hardware wants: paging
state, control-register state, and CPU identification. Track A's whole purpose
is answering questions about a specific machine. The image was built before
the tree could ask three of the most basic ones.

### 14.4 The image is slower and larger for no reason

CL 11881 is *"builtins bound once per site, 194,784 B heap per compile"* and
CL 11918 adds the constant-invariance analysis. These do not change behaviour,
but they do mean the stale payload does more allocation than the current one.
On a bare-metal target with a bump allocator and no collector, that is not
free, and it is the kind of difference that turns a marginal memory situation
into an out-of-memory report. Which matters here specifically, because the run
sheet records that an earlier `OUT OF MEMORY` on this path was a **false**
report from a clobbered deck-pointer register. A tree that has already been
fooled once by a spurious OOM on this exact rung should not be booting a
payload with measurably worse allocation behaviour than its own compiler.

### 14.5 The general point

The argument in sections 1 through 13 needs only CL 11926: the instruction
could not verify the change it named. But the full list changes the severity
assessment. The stick was not "one fix behind". It was behind:

- three fixes to out-of-range memory access, on a platform with no protection
- the move of the panic printer to the port the run sheet tells you to watch
- the `cli` that keeps the panic handler from being interrupted
- the CPU-state instruments a bring-up needs
- a measurable regression in allocation, on a rung already burned once by a
  false out-of-memory report

**Every one of those degrades the sitting's ability to produce an attributable
result, and three of them degrade the ability to observe a failure at all.**
The cost of the stale image is not the boot. It is that a sitting conducted
with it cannot distinguish its own defects from the board's, which is the only
thing the sitting exists to do.

---

## Appendix A: evidence, as measured 2026-07-29

| # | Fact | Source |
|---|---|---|
| A1 | `//Codex/main/seed/Codex.cdx` at #582, CL 11926, 2026-07-29 08:00:18 | `p4 filelog` |
| A2 | `//Codex/main/seed/Codex.img` at #57, CL 11000, 2026-07-27 | `p4 filelog` |
| A3 | 23 `Codex.cdx` revisions submitted after CL 11000, rev 560 (CL 11009) through rev 582 (CL 11926) | `p4 filelog`, filtered |
| A4 | CL 11926 is the UEFI firmware-call ABI fix (RBX save) plus ExitUefi wiring, gated by red at Damian's request | `p4 describe -s 11926` |
| A5 | `seed/Codex.img` is `headType binary`, tracked | `p4 fstat` |
| A6 | On disk, `Codex.cdx` and `Codex.img` share LastWriteTime 7/29/2026 2:07:57 PM | `Get-Item` |
| A7 | `build/build-boot-img.ps1` defaults `-Out` to the depot artifact `seed\Codex.img` | source, lines 10-23 |
| A8 | The gate dance mandates `p4 sync -f` before any build | `docs/Agents/PerforceProcess.md`, step 2 |
| A9 | `docs/Hardware/HardwareSitting.md` at #4, CL 11870 | `p4 filelog` |
| A10 | `docs/Agents/fester-workplan.md` at #124, CL 12032; main head CL 12041 | `p4 filelog`, `p4 changes` |
| A11 | R8 is BLOCKING, names the img/seed split, predicts "the failure that looks like a compiler bug", and is discharged by "Ask red, do not infer" | `HardwareSitting.md` section 0 |
| A12 | Run sheet step 1 does invoke `build/build-boot-img.ps1` | `HardwareSitting.md` section 1 |
| A13 | Boot 3 carries "DO NOT RUN THIS RUNG YET, and do not flash the console stick", reason: no ConOut path, screen stays black | `HardwareSitting.md` section 4 |
| A14 | fester's A1 says "Re-flash and re-measure before diagnosing anything else. It may simply be fixed", referring to CL 11926 | `fester-workplan.md` item 1 |
| A15 | `PciProbe.codex` is called "the instrument for question 2" in the workplan and appears nowhere in the run sheet | both documents |
| A16 | fester CL 12033/12035 fixed a spurious flasher verify failure caused by a 1 MB buffered read past the end of the medium | `p4 describe -s 12033`, `p4 diff2` |
| A17 | All build inputs the run sheet names exist on disk | `Test-Path` |

## Appendix B: what this document does not blame

- **The flasher.** It wrote and verified every byte correctly. Its one recent
  defect made a *good* stick read as bad, which is the safe direction, and
  fester fixed it today.
- **The board, the stick, or the firmware.** Nothing here is evidence about
  the hardware. That is the point: a stale payload produces a datum that says
  nothing about the machine, which is why it is expensive rather than merely
  wasteful.
- **The gate.** `build/build.ps1` proves the compiler is a hard fixed point of
  itself and it does that correctly. It never claimed to own a distribution
  image, and extending it to do so is a decision, not a bug fix.
- **`p4 sync -f`.** It restored a tracked file to depot state, which is its
  entire contract. The file should not have been tracked.
