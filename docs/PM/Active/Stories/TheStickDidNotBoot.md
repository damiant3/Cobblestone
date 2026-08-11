# The Stick Did Not Boot

*fester, 2026-07-29. Written at Damian's instruction, the same afternoon,
after `pci-probe.img` was flashed to a 28.9 GB USB stick and the ASUS TUF
did not boot it.*

---

## 0. The thesis, stated first so nobody has to hunt for it

**I do not know why the stick did not boot, and the reason I do not know
is a process failure that is mine.**

That sentence is the paper. Everything after it is the evidence for it,
the evidence against the alternatives, and the argument that the fix is
not a repair to the image but a repair to the loop that produced it.

There is a temptation, when handed a failure and a request for an
explanation, to produce a confident narrative. I could write twenty pages
naming the AllocatePages path, or the FAT16 geometry, or the second
partition, and each would read as diagnosis. None of them would be. The
project has a rule about exactly this (`CLAUDE.md` rule 12): prose about
our own code competes with the code as a source of truth, and it loses
while still being believed. A paper that guesses at a cause and dresses
the guess in structure is that failure at document scale. The
`rv-emit-frameless-mod` block is the cautionary case in the tree already:
a paragraph that was false, beside code that was wrong in the direction
the paragraph described as right, believed by an agent who read the
paragraph instead of the four-token function body underneath it.

So this paper does something narrower and, I think, more useful. It
establishes what the evidence **rules out**, names the small number of
hypotheses that survive, gives for each the single observation that would
confirm or kill it, and is explicit about which claims are measured and
which are read.

---

## 1. What is actually known

The complete observational record of the boot attempt is:

> "stick doesn't boot."

That is one bit. It is not a criticism of the report; it is the correct
report of what a person watching a screen saw. The problem is that the
instrument at the far end of this experiment was a human being looking at
a monitor, and the payload was designed to communicate through that
channel and no other. When the payload does not run, the channel does not
exist. A probe that reports by painting the screen cannot report that it
never got to paint.

This is `docs/PM/Active/Stories/LESSONS.md` **L-ORACLE** arriving in the
one form I did not plan for. I decided the instrument before writing the
driver, as the rule asks, and I decided it for the success case. I did not
decide the instrument for the case where the payload never executes, which
is the case that happened.

### 1.1 The distinctions the one bit does not make

"Doesn't boot" collapses at least five materially different events into
one word:

1. **The firmware never listed the stick.** The boot menu had no entry for
   it. Nothing of ours ran; nothing of ours could have run.
2. **The firmware listed it, the operator selected it, and it was
   rejected.** Some firmware print a brief message; many silently fall
   through to the next boot option.
3. **The firmware loaded `BOOTX64.EFI` and our stub began executing, and
   died before painting anything.** Black screen, possibly a hang,
   possibly an immediate return to the boot menu.
4. **The stub ran, acquired GOP, called ExitBootServices, and the Codex
   payload faulted** before or during the PCI walk. Black screen, machine
   alive but unresponsive.
5. **It painted something and the something was not recognisable** as the
   expected report.

Cases 1 and 2 are the firmware's verdict on the disk. Case 3 is the stub.
Case 4 is my Codex. Case 5 is the rendering. **These have nothing in
common except the color of the screen**, and the next action is different
for every one of them. `docs/HardwareSitting.md` already knows this: its
boot 1 table has separate rows for "nothing, no output, immediate halt"
and "firmware never lists the stick", and it assigns them opposite
conclusions. One ends the sitting for that machine. The other says
re-flash and try the second stick before concluding anything at all.

I flashed the stick without arranging for that distinction to be
observable. That is the first of three errors, and it is the one that
makes this paper necessary instead of unnecessary.

---

## 2. The three errors, before the stick ever went in

I want these in one place, plainly, because the rest of the paper is
diagnosis and diagnosis tends to bury the part that was avoidable.

### 2.1 I flashed an artifact I had never booted

This is the central failure and everything else is downstream of it.

Earlier in the session I built a PciProbe image and verified it under
OVMF. It booted, it painted the report, it produced QR codes that
`tools/qr-read.ps1` decoded end to end at three and four chunks. That
verification was real and I reported it accurately.

**It was not a verification of the artifact I flashed.** The image I
verified was built with the script's default arguments. The image I
flashed was built with `-Seed '' -Font ''`, from the command documented in
`build/boot/diag/README.md`. Different arguments produce a different disk:
different total size, different FAT geometry, different file set, a
different cluster count and a different number of GPT partition entries.
I confirmed the *payload bytes* were identical (`text=140293 rodata=1468
opening=0x1933F PE=143872`, byte for byte what I had tested) and I
reported that, correctly, as provenance.

Then I treated payload identity as image identity. It is not. The payload
is the cargo. The failure modes at issue here are almost all in the
vehicle.

The tree says so, in a doctrine section written after a day lost to
exactly this class of problem. `OsHardwareRoadmap`, "The boot-image
iteration loop (doctrine, 2026-07-10)":

> **Loop A -- the daily loop. File domain only.** Build the image, then
> two gates: structural GPT validation [...] and an OVMF boot of the image
> FILE (real edk2 firmware; ceremony on the glass or BdsDxe lines on
> serial). **Payload work NEVER needs a physical flash to iterate.** An
> image CL is submittable when both gates are green.

Loop A has two gates. I ran neither on this image. I ran an OVMF boot of a
*different* image built from the same source, which is precisely the
substitution the doctrine exists to forbid, and I then went straight to
Loop B, the hardware checkpoint, which the same section describes as
"rare; only for questions ONLY metal can answer".

The cost of the omission was about ninety seconds of QEMU. The cost of the
omission not being made was a wasted boot at the scarcest resource on the
project (**L-HUMAN**: Damian at the ASUS is the bottleneck) and this
paper.

### 2.2 I chose the payload for the sitting and not for the ladder

`docs/HardwareSitting.md` specifies a ladder of three boots, each
answering **one named question**, and its boot 1 is `xhci-probe.img`,
chosen for a stated reason: it is "the narrowest question, asked first,
because every later answer depends on it."

I overrode that. I flashed `pci-probe.img` and told Damian why: it
answers the same precondition and also sitting question 2, which blocks
red's whole track. That reasoning is not wrong on value. It is wrong on
**risk ordering**, and I did not weigh that at all.

The ladder is not sequenced by value. It is sequenced by how much of the
stack each rung puts at risk. `xhci-probe.img` is the known quantity: it
has been flashed and booted before, so if it fails, the failure is the
board or the stick, because the image is a control. `pci-probe.img` is a
payload written today, in an image shape built today, that had never
touched hardware. Putting it first means a black screen is
uninterpretable, which is exactly the position we are now in.

**A first rung whose purpose is to validate the rest of the ladder must
itself be a control.** I replaced the control with the experiment and kept
the control's place in the order.

### 2.3 I violated the stick-handling rules while proving the flash was good

The same roadmap section, "Loop B -- the hardware checkpoint. Rules
learned in blood":

> - Flash -> verify -> PULL. The stick never lingers in and **NEVER
>   returns to a Windows box** between flash and boot test. A stick that
>   re-entered Windows is presumed rewritten; reflash before trusting.
> - **Randomize the disk GUID at flash time (pending patch)** so partmgr's
>   cached repair ruling never matches; without it a flash after any GPT
>   experiment on the same box fails verify within a second.

What actually happened, in order:

1. Flashed. The flasher reported the image written and all 16,777,216
   bytes verified, then **threw an exception and exited 1** on the
   readback of the tail GPT blobs.
2. The stick stayed inserted. I launched a second elevated process and
   read raw sectors off `\\.\PhysicalDrive2` to determine whether the tail
   writes had landed.
3. I edited `build/flash-usb.ps1`, and **flashed the same stick a second
   time** to test my fix.
4. The stick stayed inserted through all of it, and could not be taken
   offline: the flasher's own log says `could not offline disk:
   Removable media cannot be set to offline`.

Steps 2 through 4 are three separate violations of "flash, verify, pull".
The stick sat online in Windows across two full writes and a raw read.

I want to be precise about how much this matters, because it is easy to
overclaim. `-SpecFit` exists to make the on-disk GPT spec-conformant
specifically so that Windows GPT auto-repair "finds nothing to fix", and I
verified after the second flash that the primary header, the backup
header and the backup entry array were all correct on the physical medium.
So the mechanism the roadmap warns about was, at the moment I last looked,
not firing.

But the roadmap's second bullet describes a mechanism that `-SpecFit` does
**not** address, and it is marked **pending patch**, meaning it was never
implemented: `build-img` stamps a **deterministic disk GUID**, so every
image this project has ever built looks to Windows like the same disk, and
partmgr caches its repair ruling by disk GUID. The roadmap's own account
of the day the working stick stopped working lists five stacked actors and
says plainly that **none of them was the payload**. Two of those five were
Windows rewriting the disk after a successful flash.

I cannot claim that happened here. I can claim I created every condition
under which it has happened before, and then removed my own ability to
detect it by not re-reading the disk after the final write.

---

## 3. What the image actually is

This section is measurement. Everything in it was read out of the image
files themselves this afternoon, after the failure, with no VM involved.

The method matters for the reason **L-ORACLE** matters: I did not want a
tool that reports on the image using the same code that built it. So the
comparison is structural, done by reading raw bytes, and it is done
**against a control**: `build/boot/optiona-milestone.img`, which val
confirmed today renders its menu on this exact ASUS TUF and on the Dell.

That control is the most valuable object in this investigation. It is an
image, in the same tree, built by the same script, that this exact
motherboard demonstrably boots.

### 3.1 Partition layer

| | `optiona-milestone.img` (boots on the ASUS) | `pci-probe.img` (did not) |
|---|---|---|
| size | 8,388,608 (8 MB) | 16,777,216 (16 MB) |
| MBR signature | `55aa` | `55aa` |
| protective MBR entry | type `0xEE`, firstLBA 1, 16383 sectors | type `0xEE`, firstLBA 1, 32767 sectors |
| GPT signature | `EFI PART` | `EFI PART` |
| MyLBA / AltLBA | 1 / 16383 | 1 / 32767 |
| FirstUsable / LastUsable | 2048 / 16350 | 2048 / 32734 |
| entry count / size | 1 / 128 | **2** / 128 |
| partition 0 type | `C12A7328-F81F-11D2-BA4B-00A0C93EC93B` (ESP) | same |
| partition 0 extent | 2048..16350, "EFI System" | 2048..28638, "EFI System" |
| partition 1 | none | **`C0DE1A11-...-C0DEC0DE5EED`, 28639..32734, "Codex Facts"** |

Both are well-formed. The differences are size, and the presence of a
second partition in mine.

### 3.2 Filesystem layer

| | milestone | pci-probe |
|---|---|---|
| OEM name | `CODEX   ` | `CODEX   ` |
| bytes/sector | 512 | 512 |
| sectors/cluster | 1 | 1 |
| reserved | 1 | 1 |
| FAT count | 2 | 2 |
| root entries | 512 | 512 |
| total sectors (32-bit) | 14,303 | 26,591 |
| sectors per FAT | 56 | 104 |
| media byte | `0xF8` | `0xF8` |
| boot signature | `0x29` | `0x29` |
| fsType string | `FAT16   ` | `FAT16   ` |
| `55AA` at +510 | present | present |
| computed data start | sector 145 | sector 241 |
| **computed cluster count** | **14,158** | **26,350** |

FAT16 requires a cluster count in `4085..65524`. Both are legal, and
neither is near a boundary. The geometry scales exactly as the size
difference predicts. There is no anomaly here.

### 3.3 The boot file

Both images resolve `\EFI\BOOT\BOOTX64.EFI` correctly through the
directory chain (root -> `EFI` at cluster 2 -> `BOOT` at cluster 3 -> the
file at cluster 4).

| PE field | milestone | pci-probe |
|---|---|---|
| `MZ` | present | present |
| `e_lfanew` / `PE` sig | 128 / present | 128 / present |
| Machine | `0x8664` | `0x8664` |
| Sections | 2 | 2 |
| Optional header magic | `0x020B` (PE32+) | `0x020B` |
| AddressOfEntryPoint | `0x1000` | `0x1000` |
| ImageBase | 0 | 0 |
| SectionAlignment | 4096 | 4096 |
| FileAlignment | 512 | 512 |
| SizeOfHeaders | 512 | 512 |
| **Subsystem** | **10** (EFI application) | **10** |
| DllCharacteristics | `0x0160` | `0x0160` |
| SizeOfImage | 118,784 | 151,552 |
| file size | 110,080 | 143,872 |

**Every structural field is identical. Only the sizes differ**, and they
differ by exactly the amount the larger payload accounts for.

### 3.4 What section 3 establishes

This is a negative result and it is the most useful thing in the paper.

The image is not malformed. The GPT is valid and its ESP carries the
correct type GUID. The FAT16 volume is legal, conventionally shaped, and
its geometry is unremarkable. The boot file is present at the fallback
path every UEFI implementation is required to try, and its PE headers are
indistinguishable in every structural field from an image this
motherboard is known to boot.

So the hypotheses "the image is corrupt", "the ESP is wrong", "the FAT is
out of spec", "BOOTX64.EFI is missing or truncated", and "the PE is
malformed" are all **dead**, on measurement, against a control.

I should state the limit of that honestly. This is a check of the image
*file* on the dev box. It is not a check of the *stick*. Between the file
and the firmware sit the flasher, the medium, and Windows, and section 2.3
is about exactly that gap. Section 3 proves the thing I built is sound. It
does not prove the thing Damian put in the ASUS was still that thing.

---

## 4. The surviving hypotheses

Ranked by my estimate of likelihood, each with the observation that
settles it. I am labelling the epistemic status of each claim, because
some are measured and some are read.

### H1. The firmware never listed the stick, or listed it and did not select it

**Status: strongly supported by documented board behaviour; unmeasured
here.**

The tree has direct evidence that USB boot entry enumeration on this class
of board is unreliable and delayed. `OsHardwareRoadmap` records, for the
Dell, that UEFI entries "appear only after several reboot/BIOS-visit
cycles" and that "its legacy F12 list is HARDCODED and proves nothing".
For the ASUS specifically, the tree's guidance is to add a manual boot
entry (BIOS -> Add Boot Option -> `\EFI\BOOT\BOOTX64.EFI`) "to make the
machine deterministic", with the standing warning: **never read its
hardcoded legacy device list as detection evidence.**

`HardwareSitting.md` gives this its own row and, importantly, assigns it a
non-diagnosis: "Firmware never lists the stick: re-flash with `-SpecFit`.
If it still does not list, try the second stick before concluding anything
about the board."

This is my leading hypothesis for a simple reason: it is the failure mode
with the highest documented base rate on this hardware, and nothing about
today's attempt reduced its probability.

**Discriminating observation:** does the stick appear in the UEFI boot
menu at all, by name, in the UEFI section rather than the legacy list? Not
"did it boot" but "is it listed". That is a different question and the
operator can answer it in ten seconds without booting anything.

### H2. Windows altered the disk after the last verified write

**Status: mechanism documented and previously observed on this project;
conditions for it were created today; not measured after the final flash.**

Covered in 2.3. The specific unaddressed mechanism is the deterministic
disk GUID and partmgr's ruling cache, which the roadmap flags as a
**pending patch** that was never written. `-SpecFit` closes the
"nonconforming GPT" trigger; it does not change the disk GUID.

I verified the tail sectors after the *first* flash. I did not re-verify
after the *second*, and between the second flash and the boot the stick
was ejected from a Windows box, which is the moment the roadmap says the
rewrite happens.

**Discriminating observation:** re-insert the stick and read LBA 0, 1, the
backup header and the entry array raw, and compare against what the
flasher wrote. If they differ, H2 is confirmed and the whole class of
failure is Windows, not us. This is cheap and I can script it.

### H3. The payload ran and died before painting

**Status: possible; the least constrained by evidence; several plausible
sub-causes.**

If the firmware did load and execute `BOOTX64.EFI`, the stub could fail in
ways that produce a black screen indistinguishable from H1 and H2.

- **H3a. `AllocatePages` failure.** `HardwareSitting.md`'s boot 1 table:
  "Nothing, no output, immediate halt: the PE stub's `AllocatePages` is
  failing. This board is out of memory below 1 MB. **Stop -- this is the
  end of the sitting for this machine.**" The stub requests
  `AllocPages = 32768` pages, which is 128 MB. That is the default and the
  milestone image uses it too, so this is not a difference between the two
  images. It remains a real board-dependent risk and the table treats it
  as terminal.
- **H3b. GOP acquisition or mode selection.** The stub acquires GOP before
  ExitBootServices and hands base, width, height and stride to the payload
  through cells at 0x8000. A board whose GOP presents differently could
  produce a payload painting into nothing. Note the tree already knows the
  stub **never reads `PixelFormat`** at mode info +0x0C (val's finding,
  open as fester item 3). A channel-order swap would not produce a black
  screen, so `PixelFormat` is not a candidate for *this* failure, but it is
  a live gap in the same code.
- **H3c. The Codex payload faulted during the PCI walk.** This is the part
  that is genuinely new today and therefore deserves the most suspicion,
  under **L-SUSPECT**. My `pp-collect` walks bus 0 and recurses through
  every bridge, with a depth cap of 3 and a secondary-bus-greater-than-
  current guard. On QEMU I exercised topologies with six devices and with
  a root port. **A real Z170 chipset presents substantially more devices
  and a deeper bridge tree than anything I tested against.** The walk
  allocates one record and one list push per device, and the text builder
  is O(n^2) in device count. At n around 30 that was measured at roughly
  107 KB against a 128 MB arena, so heap exhaustion is not plausible. A
  malformed or unexpected bridge configuration causing a fault or a
  non-terminating walk is not something I can rule out from QEMU
  measurements alone.

**Discriminating observation:** boot the control (`xhci-probe.img`, or
`optiona-milestone.img`, which is known to render on this board). If the
control boots and paints and mine does not, H3 is confirmed and H1 and H2
are dead. **This single boot is worth more than any other action
available**, because it partitions the entire hypothesis space in one
step. If the control also fails, the cause is the stick, the flash, or the
firmware's boot selection, and nothing in my payload is implicated at all.

### H4. The medium

**Status: unlikely; partially excluded by measurement.**

`HardwareSitting.md`: "if it reports a verify failure, the stick is the
problem: take the second one", and the kit list calls for a second stick
because "stick wear is a documented cause of same-image-sometimes-boots".

The flasher read back and compared all 16,777,216 bytes and they matched,
and after my fix all four GPT blobs verified too. That is meaningfully
stronger than most flash verification. It is not conclusive: it proves the
bytes were readable immediately after writing, not that they are readable
by different firmware minutes later, and U3-era sticks are documented in
this same roadmap as reserving an unreliable tail *inside* the reported
capacity.

**Discriminating observation:** the second stick.

### H5. The image is wrong in a way the control does not share

**Status: two candidate differences identified; both weak; neither
excluded.**

Section 3 found only two substantive differences between my image and the
known-good one: mine is 16 MB with a **second GPT partition** ("Codex
Facts"), and mine carries a **2,994,123-byte `SOURCE.SRC`** in the root
directory.

Both are weak candidates, and I want to be careful not to inflate them
just because they are the only differences I found.

- The second partition is produced by `build-img.ps1` for every image of
  this shape and is not novel to me. A firmware that choked on a second
  GPT entry would be remarkable.
- `SOURCE.SRC` is **3 MB of payload the probe never reads.** It is there
  because the documented command in `build/boot/diag/README.md` passes
  `-Seed '' -Font ''` but not `-Source ''`, and `-Source` defaults to
  `build-output/Codex.codex`. That is a defect in the documented command
  which I should fix regardless: a diagnostic image should carry the
  diagnostic and nothing else. It roughly doubled the image and is the
  direct cause of the 16 MB size and the 26,350-cluster FAT. I have no
  mechanism by which it prevents boot.

**Discriminating observation:** rebuild with `-Source ''`, which should
produce something close to the milestone image's 8 MB shape, and gate it
under OVMF. If the smaller image boots on metal and the larger does not,
that is a real and surprising finding worth its own investigation.

---

## 5. What the flasher episode actually tells us

It is worth separating this out, because it produced the one genuinely
solid technical result of the afternoon and it also produced a misleading
feeling of thoroughness.

The flasher wrote the image, verified all 16,777,216 bytes by readback,
applied the four `-SpecFit` GPT blobs, verified the two at LBA 0 and 1,
and then **threw and exited 1** on the two at the disk tail with "The
drive cannot find the sector requested".

I diagnosed that correctly. The blob readback reused the main handle,
which carries a 1 MB `FileStream` buffer, and on a raw device a buffered
read positioned 34 sectors from the end issues a 1 MB `ReadFile` that runs
past the last sector and fails. I confirmed the writes had in fact landed
by reading the three tail sectors through an unbuffered handle and
checking their structure rather than re-running the script's own
arithmetic: backup header `MyLBA=60506111 AlternateLBA=1
EntryLBA=60506078`, entry array carrying the ESP type GUID. Then I fixed
the verifier, re-flashed, and all four blobs verified with exit 0. That
fix is on main (CL 12033 / 12035) and it is worth having: `-SpecFit` is
what makes firmware list the stick at all, and a spurious failure there
reads as "take the second stick" in the middle of a sitting.

**And it contributed nothing to whether the machine boots.** Worse, it
consumed the attention that the actual question deserved, and it added two
of the three stick-handling violations in section 2.3. I spent that
attention on the part of the problem that was legible and instrumented,
which is a bias worth naming: **the failure that produces a stack trace
gets investigated, and the failure that produces silence gets a shrug.**
The silent one was the one that mattered.

---

## 6. Where the stub can die silently

Section 4's H3 is the hypothesis about my own side of the boundary, and it
deserves to be concrete rather than a shrug at "the payload died". The
stub's sequence is documented in its own header comment in
`build/boot/option_a_stub.asm`, described there as "strict-clean ordering
(proven by A1)":

1. `LocateProtocol(GOP)`
2. `AllocateAnyPages(0, EfiLoaderData=2, ALLOC_PAGES, &base)`, keeping the
   address
3. `GetMemoryMap`
4. `ExitBootServices(ImageHandle, MapKey)`, retrying once after a fresh
   `GetMemoryMap` on a stale key
5. Build a 4 GB identity map at `rbp`, load `cr3`
6. Write the GOP handoff cells
7. Walk `SystemTable.ConfigurationTable` for the ACPI RSDP, preferring the
   2.0 GUID and falling back to 1.0
8. Seed 32 bytes of device entropy, `RDRAND` gated on `CPUID.01H:ECX[30]`,
   32 retries per qword, degrading to `RDTSC`
9. Copy CDX `.text` to `0x100000` and `.rodata` to the patched data vaddr
10. Initialise the kernel metadata cells, `R10` (heap) and `RSP` (stack)
11. `jmp rax`, which is `0x100000 + openingOff`

Steps 1 through 4 are firmware calls, and they are the steps that can fail
for board-specific reasons. Step 2 is H3a: `ALLOC_PAGES` is patched to
32,768 pages, 128 MB, and `HardwareSitting.md` treats its failure as
terminal for the machine. Step 1 is H3b.

### 6.1 The finding: the stub's failure mode is an infinite loop

The stub contains, at the end of its code:

```
fatal:
    jmp     fatal
```

**Every failure path in the firmware-call chain ends in a two-byte spin.**
No message, no beep, no color, no serial byte. A board that cannot satisfy
`LocateProtocol(GOP)`, or cannot give us 128 MB, or refuses
`ExitBootServices` twice, produces a machine that is powered on, warm, and
displaying whatever the firmware last drew, forever.

That is indistinguishable, from the far side of a monitor, from the
firmware never having loaded us at all. **The observation Damian made
today, "doesn't boot", is the same observation for H1, H2 and every branch
of H3**, and that is not a limitation of his reporting. It is the stub's
design.

I do not think `fatal` is wrong as a mechanism. Halting is the correct
thing to do when the firmware contract cannot be met, and this is a
validation prototype. What is wrong is that it is the *only* mechanism,
and that nothing distinguishes one arrival at `fatal` from another.

### 6.2 Why this reframes the whole investigation

Sections 3 and 4 were written as though the interesting question were
which hypothesis is true. Section 6.1 says something stronger and more
useful: **with the current stub, several of those hypotheses are not
distinguishable by any observation available at the machine**, no matter
how carefully the operator watches. Adding more care at the ASUS cannot
fix that. Only changing the stub can.

This is why item 7 of section 8 is not cosmetic and why I would not flash
again without it. A single `mov` writing a known color across the
framebuffer immediately after step 1 succeeds, and a second color after
step 5, would turn `fatal` from one silent state into three visibly
different ones:

- **Screen unchanged from firmware**: we never got GOP, or were never
  loaded. H1, H2, H3b.
- **First color, then nothing**: GOP acquired, died in allocation, memory
  map or ExitBootServices. H3a.
- **Second color, then nothing**: through ExitBootServices and paging,
  died in the payload. H3c, which is my Codex and my PCI walk.

Three states, readable from across the room, no camera, no decoding, no
second person. The cost is a few instructions in a file that is already
being opened for val's `PixelFormat` finding (fester item 3).

### 6.3 A related gap this exposes

Step 7 walks the configuration table for the RSDP, and the stub's own
comment says the payload should report "no ACPI" rather than guessing when
neither GUID is present. That is good discipline, and it is the shape the
rest of the stub lacks: a named, reportable negative result instead of a
silent branch. The ACPI walk knows how to say "I looked and found
nothing". The GOP path and the allocation path do not.

---

## 7. The instrument problem

The deepest issue here is not any of H1 through H5. It is that a boot of
this stick produces one bit of information, and it needs to produce
several.

`docs/HardwareSitting.md` names this a precondition, not advice:

> R-1 is a **precondition, not advice**: no hardware campaign launches
> without an output channel that does not depend on the subsystem under
> test.

I satisfied R-1 for the payload and only for the payload. The QR channel
on the GOP framebuffer is a genuine independent channel for reporting
*what the payload found*. It is not a channel for reporting *that the
payload started*, and it is certainly not a channel for reporting that the
firmware declined to load it.

There are at least three channels available on this board that we are not
using, in increasing order of effort:

1. **The firmware's own boot menu.** Free. Answers H1 completely. Requires
   no code and no flash: look at the list.
2. **COM1.** The tree already treats serial as the instrument that named
   the `seed/Codex.img` reboot loop: "not one byte on COM1" is recorded as
   the decisive measurement, and `com1.log` with the firmware's own BdsDxe
   lines is called out as what diagnosed it "the first time, not the
   screen". Whether this ASUS exposes a usable header is a question for
   the sitting, and it is a better question than several of the four
   currently on the list.
3. **An earlier paint.** The stub could paint a single known color across
   the framebuffer immediately after GOP acquisition and before
   ExitBootServices, and again immediately before jumping to `opening`.
   Two solid color flashes cost a handful of instructions and would
   partition H3 into "never got GOP", "got GOP, died before the payload"
   and "died inside the payload" **from across the room, with no camera
   and no decoding.** That is the cheapest large improvement available and
   I should have built it before flashing anything.

Point 3 is the one I would act on first. It converts the black screen from
an absence of information into a positioned failure.

---

## 8. What I would do next, in order

Deliberately ordered so that each step is cheap and each partitions the
space. Steps 1 and 2 require nothing from Damian.

1. **Gate the flashed image under OVMF.** Loop A's second gate, skipped.
   Ninety seconds. If it fails here, the investigation is over and the
   cause is in my image, findable on the dev box, with no further hardware
   time spent.
2. **Run the structural GPT validator** (Loop A's first gate, the
   `test-gpt` validator) against `pci-probe.img`, including both headers
   and every CRC. Section 3 checked the fields I thought to check; the
   validator checks the fields somebody thought to check after losing a
   day to them.
3. **Re-read the physical stick** as it now sits, and diff it against the
   image file. This is the only test of H2, and its value decays the
   moment the stick is re-inserted anywhere, so it should happen before
   anything else touches it.
4. **Boot the control.** `optiona-milestone.img`, or `xhci-probe.img`.
   This is the single highest-value action on the list, because it
   separates "this board / this stick / this flash" from "this image", and
   nothing else does. It should have been the first rung.
5. **Look at the boot menu** rather than at the boot. Is the stick listed
   as a UEFI entry? If not, add the manual boot option the roadmap
   describes and look again.
6. **Rebuild with `-Source ''`** and fix the documented command in
   `build/boot/diag/README.md`, which is wrong today regardless of whether
   it caused this.
7. **Add the two color flashes to the stub**, and only then flash again.

---

## 9. What has to change, beyond this one stick

Three things, and they are process rather than code.

**9.1. An artifact that goes on a stick is gated as an artifact.** Not as
a payload with known-good bytes, not by inheritance from a sibling build.
The exact file. Loop A exists, is written down, and I walked around it.
The fix is not new doctrine; it is obeying the doctrine that exists.

**9.2. The first rung of any hardware ladder is a control.** The value
ordering and the risk ordering of a boot sequence are different, and when
they conflict, risk wins, because an uninterpretable first result
invalidates the rungs above it. I inverted this for a defensible reason
and the reason was still wrong.

**9.3. Instrument the failure, not just the success.** Every probe in
`build/boot/diag/` reports findings and none of them reports liveness. A
diagnostic that cannot say "I started" can only be read when it works,
which is the case where you needed it least. The two color flashes in
section 7 are the minimum version of this and they belong in the stub, not
in each probe.

---

## 10. Closing, and what I will not claim

I have not told you why the stick did not boot, because I do not know, and
the honest length of the answer to "why" is one line: **there is not
enough information yet, and there is not enough because I skipped the gate
that would have produced it.**

What this paper does establish, on measurement rather than argument:

- The image file is structurally sound: valid protective MBR, valid GPT,
  correct ESP type GUID, legal and conventional FAT16, `BOOTX64.EFI`
  present at the fallback path, and PE headers identical in every
  structural field to an image this exact motherboard boots.
- Therefore the largest and most obvious class of causes is excluded, and
  the remaining hypotheses concentrate in three places: the firmware's
  boot selection, what Windows did to the stick after the last verified
  write, and my Codex payload meeting real chipset topology for the first
  time.
- One boot of a control image collapses that space in a single step, and
  it costs the same as the boot that produced this paper.

The most useful sentence in the roadmap's account of the last time this
happened is that the cause turned out to be **five stacked actors, none of
them the payload**. I have spent this session assuming my payload was the
interesting object. The evidence in section 3 says it is probably not, and
the history says it is probably not, and the next thing to do is
therefore not to study my code but to boot something that already works
and see whether it still does.

---

*Measured facts in this paper come from the image files and the physical
stick on 2026-07-29, and from the depot at CL 12035. Claims read from
documents rather than measured are marked as such in section 4. The one
thing this paper does not contain is an observation of the failure itself,
which is its central weakness and the reason section 8 is ordered the way
it is.*
