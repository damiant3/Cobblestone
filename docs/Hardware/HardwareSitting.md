# The Hardware Sitting -- Run Sheet

## QUICKREF: returned sticks live in `D:\Projects\stick-archive\`

Damian's ruling, 2026-08-11. **Dump a stick before you flash over it, put
the dump there, and add a row below.** Nothing else in this document is
worth reading first if you are holding a stick.

Damian's ruling, 2026-08-14. **No flash without a same-bytes full-loop
bed rehearsal, and a board defect is not closed until a bed arm
expresses it.** The recipe and the account are the 2026-08-14 A5 entry
below; the lessons are L-REHEARSE and L-FREEDOM in
`docs/PM/Active/Stories/LESSONS.md`.

Damian, 2026-08-05. **Say what the elevated command does BEFORE you fire
it**, in the message, in the two categories that tell him what he is
approving: **MOUNT-AND-COPY ONLY** (reads the stick, writes nothing) or
**FLASH** (writes an image to a named disk -- give the disk number).
*"when I see a uac, it blacks out the whole screen, so i don't know if
its a mount or a flash you are running."* The secure-desktop overlay also
swallows a stray keystroke, so prompts get dismissed by accident: if one
goes unanswered, say what it is again before re-firing, and after two
dismissals stop and wait for his word rather than firing a third. He is
at a keyboard doing something else. This applies to every
`Start-Process -Verb RunAs` in this file.

```powershell
# 1. dump before flashing. ELEVATED -- it opens \\.\PhysicalDrive raw.
#    -DiskNumber, not -Disk. Read-only: no writes and no mount, which is
#    the point, because mounting a FAT volume makes Windows allocate
#    clusters and manufacture the evidence question 3 asks about.
pwsh build\dump-usb.ps1 -DiskNumber 2 -Out D:\Projects\stick-archive\<what>-<yyyymmdd>.img

# 2. record the hash, and quote THIS path from then on
(Get-FileHash D:\Projects\stick-archive\<what>-<yyyymmdd>.img -Algorithm SHA256).Hash
```

| in the archive | SHA-256 | what it is |
|---|---|---|
| `stick-before-20260811.img` | `629821CF E8A9F876 EC2A9FD9 C4C61B47 3D93B821 681DB014 5B0E6DFD D2683A03` | the ceremony boot that flew green 2026-08-05, read off disk 2 by reek on 08-11 before `a5bigflight.img` went over it. **The only copy**: `ceremonyboot.img` was never in the depot in any stream at any revision. Carries the 124-byte `IDENTITY.DAT` the guest wrote to the ESP on real ASMedia hardware. |
| `before-asdeflight-20260813.img` | `AAEC57B9 A51E0FFD 1FD72EC2 396938B9 25538610 103DA90A 53C9FE07 932F8C56` | read off disk 2 by blu on 08-13 before `asdeflight.img` went over it. **Not a duplicate of anything already here** -- it differs from `vmxprobe-returned-20260813.img` and from every other row. Root directory holds `BOOTX64.EFI` and `SOURCE.SRC` and **no `CODEX.CDX`**, so it is a source-carrying image rather than the vmxprobe already on file. Owner unidentified; whoever recognises it should say so in this row. |
| `vmxprobe-returned-20260813.img` | `2FCF8F98 994DFE45 7ABE6E6F BEC323F8 D6DFE479 477FA2C0 B761A90C 154AD0CE` | the VT-x flight as it came back 2026-08-13. Carries a 124-byte `IDENTITY.DAT` whose key was generated on the ASUS during the first-boot wizard: **the only copy**. Six sectors differ from what was flashed and all six are named in that flight's entry. |
| `before-vmxprobe-20260813.img` | `190911B8 988A645D 622DAB1F C9664AAC C94560E8 2642D462 92758EB1 CB93FC1C` | disk 2 as it stood 2026-08-13 16:31, read off before `vmxprobe.img` went over it. A diagnostic image, per Damian; not known to have flown. Dumped under the standing ruling rather than because it was believed precious. |
| `before-a5fix-20260813.img` | `E4959969 BE674761 4AF93CD6 0EFFCD8D 8BC1DC7A 1980EB1F 005F2EF0 77E05BBA` | disk 2 as it stood 2026-08-13 21:20, read off before `a5fix.img` went over it. Differs from `vmxprobe-returned-20260813.img` although no flight intervened, so something on the host touched the stick between the two dumps -- which is the standing ruling paying for itself. |
| `a5fix-returned-20260813.img` | `089DF895 2473E9F6 9E3A7EAA 225B6E3A FD54CD15 78D141F6 D9C1361A F1661A18` | the a5fix flight as it came back 2026-08-13 21:45: the MAGENTA stall whose DIAG lines read every volume field as 00FF00FF00FF00FF, two magenta pixels. Photographed; the account is in the a5fix entry. Dumped before `a5mem.img` went over it. |
| `a5mem-returned-20260813.img` | `86C3DD60 FF710EC6 0B4973D8 BF13F626 C19A4EEA B61262F2 2F80F0F6 3EAAC5FD` | the a5mem instrument flight as it came back 2026-08-13: the run whose DIAG mem lines scrolled off the glass before the failure line landed. Dumped before `a5heap.img` went over it. |
| `a5heap-returned-20260814.img` | `AC4AF338 76DDDD36 FA5C1550 09DC114B 6542A4DB 2083E465 1150271E 1489AED0` | the a5heap flight as it came back 2026-08-14 after seven-plus hours of ORANGE. Root holds SOURCE.SRC and **no OUT.CDX, no OUT.TXT**: the guest died before writing a byte, which is what the UD2-at-pitch diagnosis in the 2026-08-14 entry predicted. Dumped before `a5flight.img` went over it. |
| `before-nicring-20260815.img` | `8203934F C5658197 064B0A13 76D7CE71 7F34984C AA23100F C7B03F89 B84BBBCF` | disk 2 read off by blu 2026-08-16 before `nicring.img` went over it. **Byte-identical to `nicinit-returned-20260815.img`**, which is correct rather than a duplicate: nothing flew between the two, so the NIC-3 answer is what `nicring.img` was flashed over. Recorded so the equal hashes are not re-derived. The NIC-4 flight that followed banked nothing, so there is no returned-stick row for it -- the glass entry below is its whole record. |
| `nicinit-returned-20260815.img` | `8203934F C5658197 064B0A13 76D7CE71 7F34984C AA23100F C7B03F89 B84BBBCF` | THE NIC-3 ANSWER as it came back 2026-08-15. Carries `SH205457.BMP` (2,359,350 bytes), the F12 bank of the glass, which is where every figure in that flight's entry comes from -- the first NIC arm whose reading was banked rather than photographed. Volume clean on all four questions. |
| `before-nicinit-20260815.img` | `C127DA97 2BF30B0E 83EBAE0F E72A82F9 C39CE629 5079718F 49043220 E65534BA` | **The 2026-08-14 NIC flight (`nicsitting.img`) as it came back**, read off disk 2 by blu on 08-15 before `nicinit.img` went over it. **The only copy, and it was never dumped at the time**: that flight banked nothing because the stick did not mount, so the photograph was the whole record until this. Matches no other row here. |
| `before-nicsitting-20260814.img` | `5ED8A6D4 5343819A 545EA7A7 97682A81 84251F71 7254A96F 36BB018E E2160F99` | **The same image as `a5flight-returned-20260814.img` below, and that is correct rather than a duplicate**: the green A5 flight's returned stick is exactly what `nicsitting.img` was flashed over. Stated here so nobody re-derives it from two equal hashes. |
| `a5flight-returned-20260814.img` | `5ED8A6D4 5343819A 545EA7A7 97682A81 84251F71 7254A96F 36BB018E E2160F99` | THE GREEN A5 FLIGHT as it came back 2026-08-14. Root holds `OUT.CDX` (2,790,018 bytes, `AB3A207EFB9279A6`, byte-identical to the host control) and `OUT.TXT` ("OK OUT.CDX 2790018"), both written by the compiler running on the ASUS. The account is in the 2026-08-14 entry. |

**Why not `build-output/`, which is where everyone put them until now:**
the `clean` phase of every gate run wipes it, and a gate is the most
likely next thing anyone does. Blu lost three preserved returned sticks
to that on 2026-08-11; reek's survived the same day only by not having
run a gate yet. `build-output/` is also p4-ignored, so nothing in the
depot notices the loss. `D:\Projects\stick-archive\` is outside every
workspace, so no `clean` owns it and no `sync -f` reaches it -- verified
by running a full gate over it after the first file landed.

**Archive what came BACK, not what you built.** A built image is a recipe
away and belongs in `build/boot/` under Perforce with the rest. A
returned stick is evidence, there is exactly one of it, and if it holds
anything the guest itself wrote there is no rebuilding it. The eight
other 16 MB images sitting in the fleet's `build-output/` directories on
2026-08-11 were all built, which is why none of them was rescued.

**Say where, in this document, in the entry for that flight.** Older
entries say only "on blu's box", and that vagueness is half of what made
`build-output/` look like a safe habit.

## THE SITTING QUEUE: what is waiting on metal, in the order it should fly

**This is a queue Damian draws from, not a request.** His standing ruling
holds: agents do not propose flights or sittings. What this section exists
for is that when he has the time and his back can take it, the sitting is
already designed, the order is already argued, and nobody spends the first
half hour of it deciding what to ask. Opened 2026-08-14 at his direction.

**The scarce resource is his back, not compute.** Everything below is
therefore organised as ONE stick and ONE boot answering as many questions as
it can, rather than as a ladder of flights. A question that cannot ride the
same boot as the others says so and says why.

### The standing shape, and every one of these was bought with a lost flight

1. **One image, many arms.** Two flights cost twice what one does and the
   second one is the expensive half.
2. **Bank before you risk (L-BANK).** Order the arms so the cheapest and
   most irreversible readings land on the glass before anything that could
   kill the box. **What wedged the machine on 2026-08-11 is still
   unexplained** and it was NOT `CTRL.RST` (that write is discarded on this
   part, see the 08-13 entry). So treat a reset, a ring reprogram or a
   receiver enable as potentially terminal, and put them last, after
   everything they could take down with them.
3. **A pure READ comes before any write, always.** The touch row is the
   control that separates "our code did it" from "the firmware had already
   done it", and once anything has been written that control cannot be
   recovered on that boot. This is what made the 08-13 link result a
   measurement rather than a hope.
4. **Every arm paints a row.** The glass is the only channel that has never
   failed. **The F12 bank HAS failed**: 2026-08-13 returned `no esp s1 m3 c4
   p1 w1964712320 f945044 l1 r1`, a USB transaction error in the CBW phase
   reading the GPT header sector, so the screenshot never reached the stick.
   Design for the photograph and treat a written bank as a bonus.
5. **Write down what a pass and a fail look like BEFORE it flies.** A
   photograph of a number nobody predicted is a number nobody can read. Put
   the expected value in the row's own table below.
6. **Name what would falsify it.** An arm that cannot fail has not measured
   anything, and this sheet has shipped two of those.
7. **Bed-verify with a control and a negative arm first**, including forcing
   the failure the arm exists to catch. The bed cannot answer these
   questions, but it can prove the instrument is not broken.

### The queue

| # | Question | Owner | Unblocks | Risk to the box |
|---|---|---|---|---|
| ~~NIC-1~~ | **ANSWERED 2026-08-14: no. `RCTL=0` at handoff.** | blu | done | none, pure read |
| ~~NIC-2~~ | **ANSWERED 2026-08-14: 32606 us per million, 2.50x the bed. The calibration transfers.** | blu | done; opened B5 | none, read and poll only |
| ~~NIC-3~~ | **ANSWERED 2026-08-15: `e1000-init` does NOT hang. It completes in 93 s, of which 92.9 are `e1000-await-aneg` burning its 1,000,000 fuel at 92.89 us per MDIO read. Aneg never reports done; the link is up anyway. `RDH` moved 0 to 15.** | blu | B2c | done |
| NIC-4 | Can the stack hold a real TCP conversation with a real peer? | blu | B3, then B4 | as NIC-3. **Flew 2026-08-16 and hung in `e1000-await-link`; fixed in 15588, ring question unanswered.** Needs a rebuild against a seed carrying the fix before it flies again |
| NIC-5 | What wedged the box on 2026-08-11? | blu | nothing; it is the one open unknown | terminal by construction |

**Fly them in that order on one boot.** The order is not a preference: NIC-1
must precede NIC-3 because a write destroys its control, NIC-2 must precede
NIC-3 because a wedge in NIC-3 loses NIC-2's reading, and NIC-5 goes last
because it is the arm most likely to end the boot.

### NIC-1. Does the real part arrive with its receiver already running?

**Why it matters.** `e1000-quiesce` exists because `e1000-setup-rx`
programmed the ring registers before disabling the receiver, which on a part
that never resets yields `recv happened: no` with every other aggregate
reading healthy. `codex/test/e1000-unresettable` pins that with
`-e1000-preconfigured`, and **that model's premise is inferred, not
measured**: nobody has read `RCTL` off the real board before touching it.
If the real part arrives with `RCTL.EN` clear, the fix is still correct but
the model is describing a hazard this board does not have, and that belongs
in the record.

**The arm.** A read-only touch row, before `net-driver-bring-up` and before
any write at all: `RCTL`, `TCTL`, `RDBAL`, `RDLEN`, `STATUS`, `CTRL`.

**Pass** is not a value, it is a reading, and either answer is a result.
`RCTL` bit 1 set means the receiver is live on arrival and the model is
right. Clear means it is not.

**Falsified by** `STATUS` or `CTRL` reading as `0xFFFFFFFF` or `0`, which
says the BAR is wrong and every other number on the screen is noise. The
08-13 flight read `STATUS=0x40080080 CTRL=0x180240`, so those two are the
known-good pair to compare against.

### NIC-2. How long is an empty receive poll on the real I219-V?

**Why it matters, and it is the reason this queue exists at all.** `NetIO`
counts a tick as a number of empty receive polls, and until main 15013 that
number was the fixed 100000 calibrated against the NE2000. Measured in the
bed 2026-08-14: one million empty polls cost **15.52 s on the NE2000 and
0.029 s on the e1000**, because an NE2000 poll is a port IN and therefore a
VM exit while an e1000 poll reads a descriptor already in RAM. Every bound
in `NetworkStack` is a count of ticks, so that spread meant give-up at 219 s
on one card and 405 ms on the other. `net-driver-calibrate` now measures the
rate at bring-up and stores it in cell 36328.

**The real part reads its descriptor out of RAM exactly as the model does,
which is WHY the calibration is expected to hold, and that expectation has
never been measured.** It is the single assumption the whole of B3 and B4
now rest on.

**The arm** is `codex/test/net-poll-calibrated` with its band judgement
replaced by the raw numbers, because on metal the number IS the new fact:
print the card, the calibrated interval, `hpet-ticks-per-second`, the
measured milliseconds for one calibrated interval, and the measured
milliseconds for the old 100000 constant as the built-in control.

| row | bed, e1000 | bed, NE2000 | what metal decides |
|---|---|---|---|
| `interval` | 2,266,000 | 7,000 | the poll cost on the real part |
| `tick ms` | 66 | 105 | in the 10..1000 band or not |
| `control ms` | 2.9 | 1550 | what the old constant would have meant on this board |

**Pass:** `tick ms` inside 10..1000. **Fail:** outside it, which says the
calibration does not transfer to the real part and every retransmit bound is
wrong on metal in a way no bed can see.

**Falsified by** `hpet-ticks-per-second` reading 0 or absurd, which voids
every duration on the screen. Print it, do not assume it.

### NIC-3. Does a frame actually move, in and out?

**Why it matters.** B2 proved the LINK comes up (08-13: touch reads `LU=0`
with a live cable, both arms then read `LU=1 FD=1 SPEED=1000`). B2c's fix is
bed-only. Nothing has put a byte on the wire from this board.

**The arm.** After NIC-1 and NIC-2 have painted: bring up, send a broadcast
ARP or a DHCP DISCOVER, and poll for anything at all. Paint the descriptor
status words, not just a verdict, because "nothing came back" and "the ring
never advanced" are different failures and only the descriptors separate
them.

**Pass:** any received frame with a non-zero length. **Fail with a
discriminator:** RDH/RDT moved but no frame means the ring is being written
and we are not reading it; neither moved means the receiver is not running.

**This is the first arm that writes.** Everything above it is already on the
glass by now, which is the whole point of the ordering.

### NIC-4. A real TCP conversation with a real peer

**Why it matters.** As of main 15013 the stack holds a real TCP
conversation over the e1000 MODEL, against a real Windows peer through
codex-vm's NAT, including under the real-part arms. On metal there is no
NAT: the peer is whatever is on Damian's LAN.

**Do not schedule this until NIC-2 and NIC-3 have passed.** It rests on
both, and a failure here with either of them unmeasured is uninterpretable.

**The arm** wants a peer that answers without negotiation, and the fewest
moving parts is a fixed IP and an ICMP echo before any TCP is attempted, so
that "the network does not answer" and "our TCP is wrong" are separable.

**Falsified by** the absence of a control: if nothing on the LAN answers a
ping from a known-good machine on the same cable, the arm proves nothing and
the sitting should stop there.

### NIC-5. What wedged the box on 2026-08-11?

**It is the one open unknown in this track, and the entry that used to
explain it is wrong.** The 08-11 flight wedged and it was read as
`CTRL.RST`; 08-13 established `CTRL` is read-only on this part, so `RST` was
never set and `e1000-await-reset` answers `settled=1` on its first read
without anything having happened. **Do not pursue the cold-versus-warm reset
hypothesis, it is dead.** What actually wedged the machine is unaccounted
for.

**This arm goes last on any boot it rides, because it is the arm designed to
reproduce a hang.** Everything else must already be photographed.

**It may not be worth flying at all**, and that is a real option: the
capability it threatens is one nobody needs, the fleet has worked around it,
and an unexplained wedge that nothing triggers costs less than a boot that
ends early. Left in the queue because an unexplained hang on the machine we
are building an OS for is worth a line, not because it is scheduled.

### NOT in this queue, and why

- **The register audit.** COMPLETE. Do not re-run it, and `e1000-phy-addr =
  1` is correct, do not "fix" it.
- **ASDE.** CLOSED. The bit is not writable on this part, measured across
  two flights with `CTRLback=0x180240 ASDEbit=n` and all four SLU rows
  unchanged after clearing SLU.
- **The cold-versus-warm reset.** Dead, see NIC-5.
- **Anything the bed can answer.** `-e1000-ctrl-ro`, `-e1000-phy-link` and
  `-e1000-preconfigured` reproduce this board on every row that has been
  compared, and B2c was found and fixed entirely in the bed. A question that
  a flag can ask does not earn a sitting.

## FLOWN 2026-08-14, GREEN: A5 COMPLETE. The compiler compiled itself on the ASUS and wrote the result back byte-identical.

**The flight:** `a5flight.img` (2026-08-14 build, SHA `DBE8DC52 5CD36AA4
4497C916 3B52B5D9 9099B592 E41132D0 B9C1811673704D78`, 16 MB; not the
2026-08-09 image of the same name). Boot from bare UEFI, BUILD header
with datetime, file, bytes and FNV fingerprint, WHITE read bar over
5,489 sectors, CYAN, ORANGE about one minute with the painted phase
strip advancing, blue write stage, white hold. Returned stick carries
`OUT.CDX` 2,790,018 bytes `AB3A207EFB9279A6`, **byte-identical to the
host control** (seed x current tree, plain CDX mode), plus `OUT.TXT`
"OK OUT.CDX 2790018".

**The defect that held A5 for days, and why no bed ever saw it:** the
ASUS's AMI firmware satisfies AllocateAnyPages from the TOP of RAM, so
the payload's heap lands ABOVE 4 GB. Several compiler types declared
heap pointers as `Integer between 0 and 4294967295`, and bounded
signatures are enforced with UD2 traps (X86_64.codex, bounded
signatures stage B). The first `pitch` at compile start trapped, the
firmware's own exception handler wedged with nothing on the glass, and
the screen froze on whatever was last painted: the seven-hour ORANGE
of the a5heap flight. OVMF and codex-vm allocate BELOW 4 GB at every
tested RAM size, so no stock bed could express the condition. It was
reproduced by patching the stub to AllocateAddress at a fixed 5 GB
base under an 8 GB OVMF bed (#UD at `pitch+0x46`), and the fix
verified the same way.

**Fixed:** widened the heap-address-carrying types to plain `Integer`
(`PhaseAllocator.codex`: `pitch` return, `PhaseStart`, `PhaseMetrics`;
`EmitAllocator.codex`: `code-buffer`, `data-buffer`). The stub also
zeroes cell 36320 (`guard-page-base-addr`), which only `emit-start`
writes and which is firmware garbage in a UEFI tenant.

**Build facts that cost this campaign a day, in one place:**

- **Firmware-TENANT flight payloads MUST be compiled `compile.ps1
  -Uefi`.** The flag puts `uefi` on the mode line and the compiler emits
  firmware-tenant I/O helpers. Without it the payload gets the
  bare-metal helpers, reads zeroes down that path, and fails its volume
  mount with `volume ok=0` under any firmware, bed or board. The
  exception is deliberate and documented at the `-Ebs` driver-truth
  probes below: a post-ExitBootServices payload drives the medium with
  our own drivers and must NOT carry `-Uefi`; the two are mutually
  exclusive by design, so read which kind of payload you are building
  before applying either absolute.
- **Flight images MUST be built `-TotalSectors 32768`** (16 MB). The
  8 MB default leaves a 6 MB ESP, and PE (2.6 MB) + SOURCE.SRC
  (2.8 MB) + OUT.CDX (2.8 MB) do not fit: the compile succeeds and the
  write-back dies `DISK-OUT: FAILED -1`. The BUILD NAY footer caught
  this in the bed before it cost a flash.
- **Kernels are interchangeable.** The depot seed, the archived Suts
  and the pre-fix seed all compile the same source to byte-identical
  CDX, measured three ways 2026-08-14. Chasing a "bad kernel" here is
  chasing a ghost; check the mode line and the image geometry first.
- The full self-compile takes ~83 s under the OVMF bed and about a
  minute on the board. The "UEFI bed is 25x slower" figure from
  2026-08-13 does not reproduce with the current payload.

**Cosmetic defect, open:** the success path paints the white hold band
over the BUILD YAY footer text, so the verdict is unreadable on the
glass at the end. The footer should print after the band or above it.

## FLOWN 2026-08-16: `nicring.img` HUNG in `e1000-init`. The ring question is still open, and the hang was the previous flight's fix.

**Three rows painted, then nothing for over ten minutes.** Verbatim from the
glass, which is the whole record because the bank did not mount:

```
NIC RING PROBE -- do frames actually move
no bank, mount stage 2 -- the glass is the reading
arrival RCTL=0 RDT=0 hpet=23999999
```

The next thing after that row is `e1000-init`, so the stop is inside it. **No
`dd=` map was ever painted, so NIC-4 answered nothing about the ring.** That
question is exactly where `nicinit.img` left it.

**F12 BANKED NOTHING and the row said so before the hang.** `mount stage 2`
means the ESP never mounted, so `RING.TXT` and any BMP were never written. The
photograph is the only artifact. That is the second flight in a row where the
bank failed and the glass carried the result; treat `nrp-bank-line`'s row as
load-bearing rather than decorative.

### The cause, which is not what the arm went up to ask

`e1000-await-link` had **no deadline at all** -- a bare count of four million
`STATUS` reads. It had never shown, because `e1000-await-aneg` burned 92.9
seconds ahead of it and the link came up during that dead time, so this loop
was answered on its first pass.

Re-reading NIC-3's own number settles why: `e1000-await-aneg` **returned 0**
after its full million. Aneg-done is never set on this part, while `STATUS.LU`
comes up anyway and the part negotiates 1000 Mb/s. The 92.9 seconds were never
auto-negotiation succeeding. Budgeting aneg to 3 s (15463) removed the dead
time, and the four-million count then ran against a part whose link was still
settling.

**Fixed, blu 15588.** `e1000-link-wait`, a 5-second HPET budget with the clock
read once per 4096-poll batch, which is the pattern `e1000-await-tx-clocked`
already used in the same chapter. The count survives as the no-HPET path.
Worst-case `e1000-init` is now 3 s + 5 s. `-e1000-no-link` reaches the timeout
branch on the desk, so `codex/test/e1000-link-deadline` reproduces the metal
symptom without a flight: 8,037,305 us with the budget against 21,862,178 us
with the count it replaced, ablated.

**15463 is not reverted.** It was correct and the regression was the same
lane's; the defect it exposed is older than it.

### What the next boot needs

The ring question is unchanged and still worth a boot, but it should ride B3's
sitting rather than take one of its own. A rebuilt `nicring.img` against a seed
carrying 15588 is the arm; **it has NOT been rebuilt or bed-verified since**, so
the card below describes bytes that are now superseded.

---

## SUPERSEDED BY THE ABOVE -- the pre-flight card for `nicring.img`

`nicinit.img` ended `INIT COMPLETE RDH=15 RDT=15` where the bed gives `RDH=0`,
and two opposite readings fit: the receiver filled all sixteen descriptors, so
frames DO move; or `RDH` is not writable on this part the way `CTRL` is not,
and nothing arrived. That arm could not separate them and said so. **This one
paints the descriptors, which nobody has done.**

```powershell
pwsh build/boot/build-option-a.ps1 -Src build/boot/diag/NicRingProbe.codex `
     -Kernel seed/Codex.cdx -Ebs -Out build/boot/nicring.img
```

| | |
|---|---|
| image | `build/boot/nicring.img`, 16,777,216 bytes |
| SHA-256 | `2D4414F4 B249B3BF 734901FB 31262F10 8187CB47 7569A463 1FE8892F F2308CCE` |
| probe source SHA-256 | `3702C478 4B67288A E40B5B50 058A7ADC 00F37ADA 1CD884A1 EB7850D1 0155BF44` |
| built against seed | `55983566E35F314D` |

### What each row should say, written before it flies

| row | bed | what metal tells you |
|---|---|---|
| `arrival` | `RCTL=0 RDH=0 RDT=0` | compare with 08-15: `RCTL=0`, and `RDH` was 15 at the END of that flight, not at arrival |
| `init took` | 21,018 us | **the aneg fix, re-verified.** Near 3,000,000 confirms it; near 92,892,733 says it did not take |
| `after-init dd=` | `0000000000000000 set=0` | any `1` means the hardware filled that descriptor |
| `after-listen dd=` | `0000000000000000 set=0` | 1.2 s of promiscuous listening on a live segment |
| `sent ARP accepted=` | 1 | the transmit descriptor completed |
| `reader control` | `tx desc 0 status=1 dd=1 READER WORKS` | **if this says BROKEN, discard every `dd` row above it** |
| `RDH was N, wrote 7, read back` | `7 WRITABLE` | if it reads back anything else, `RDH` never tracked reception and the 08-15 `RDH=15` was a mirage |

**The two answers.** Any `dd=1` on metal means frames move and B3 opens. All
zeros WITH the reader control saying WORKS means nothing was received, and then
`RDH` writability says whether the earlier 15 meant anything at all.

### Why the reader control exists, and what it cost to get

A DD reader pointed at the wrong byte reports zeros exactly as an idle ring
does, so `dd=0000...` on its own is unfalsifiable. **Two attempts to get a
positive DD in the bed failed**: the first ARP asked 0.0.0.0 for 0.0.0.0, which
nothing answers, and a proper ARP for the NAT gateway is not answered either.
The TRANSMIT descriptor settles it -- same sixteen-byte layout, same status
byte at offset 12, done in bit 0, and `e1000-send-frame` only answers 1 after
the hardware has written it.

### Bed rehearsal, three ways, on the exact flight bytes at 1024x768

- **Positive**: every row paints, `END OF ROWS` reached.
- **Reader control**: `tx desc 0 status=1 dd=1 READER WORKS`.
- **Ablation**: the reader moved to offset 13 reports `status=0 dd=0 READER
  BROKEN, every dd above is meaningless`, with every other row unmoved.

**One defect found in the instrument itself and fixed before it flew.** The
listen loop read `hpet-ticks` every poll at a fuel of four million; an HPET read
is a VM exit, so the clock dominated and the bed run stopped mid-probe. That is
`e1000-aneg-fuel`'s defect one level out, in the instrument rather than the
driver. It polls in chunks of 512 between clock reads now. **`nsp-await-frame`
in `NicSittingProbe` has the same shape and was not changed** -- it flew inside
its budget, but on luck rather than design, and the next arm to reuse it should
chunk it.

## FLOWN 2026-08-15: `nicinit.img`. NIC-3 ANSWERED. `e1000-init` does NOT hang -- it takes 93 seconds, and 92.9 of them are one loop.

**Banked, not transcribed.** `SH205457.BMP`, 2,359,350 bytes, read off the
returned stick. Every figure below is off that image.

```
no bank, mount stage 2 (no EFI PART signature) -- the rows below are the reading
eligible at 0:31.6 verdict=ok
arrival RCTL=0 EN=n TCTL=805564664 EN=n
arrival RDBAL=1551914016 RDLEN=0 RDH=0 RDT=0 hpet=23999999
s1  pcien=0 ctrl=1573440 (RST discarded here)
s2  await-reset   ret 1 in 13us
s3  settle-mdio   ret 1 in 10044us
s4  quiesce       ret 1 in 10043us
s5  alloc         ret 0 in 13us
s6  clear-mta     ret 0 in 47us
s7  setup-rx      ret 0 in 14us macok=y
s8  setup-tx      ret 0 in 14us
s9  phy-bring-up  ret 0 in 92892733us      <- bed: ret 1 in 163us
s10 await-link    ret 1 in 13us
INIT COMPLETE RDH=15 RDT=15
```

**The 2026-08-14 wedge was not a wedge.** Every step returned. It was a
93-second spin read as a hang, which is exactly what the duration rows were
built to separate and the reason this arm prints them at all.

### The arithmetic names the loop, and it is a class we have already closed once

`e1000-phy-bring-up` can return 0 three ways: the BMCR reset write fails, the
aneg write fails, or `e1000-await-aneg` exhausts `e1000-aneg-fuel`. Only the
third fits the number. **92,892,733 / 1,000,000 = 92.89 us per iteration**, and
one iteration is one `e1000-phy-read`, which is one MDIO transaction. That is
the right order for a real 2.5 MHz MDIO bus; a single `e1000-await-mdic`
burning its 100,000 fuel would need 929 us per MMIO read, which is not.

**So `e1000-aneg-fuel` is the poll-count-as-duration defect again**, in the
e1000 driver rather than in `NetIO` where it was closed at main 15013 and
15028. It is a COUNT. In the bed one MDIO read is free and a million is a
blink; on the real part a million is 93 seconds.

**AND IT WAS ALREADY KNOWN, which is the part worth carrying.** The cost was
diagnosed on 2026-08-04, after the ASDE flight painted nothing:
`NicAsde.codex` says it at `na-phy-kick`, and the annotation on
`codex/test/e1000-asde-nolink` spells it out -- "a million iterations ... tens
of microseconds ... bounded and indistinguishable from a hang". The fix taken
then was to route AROUND the function rather than to bound it, so the driver
kept the million and it cost a second flight eleven days later. **A workaround
in one caller leaves the defect armed for every other caller**, and a boot
read as a wedge is what that costs. This flight contributed the number, not
the cause. Fixed in the driver now, bounded by a 3-second HPET budget.

### What this flight leaves open

- **Auto-negotiation never reported done, and the link is up anyway.** `s10`
  answers 1 on its first read, so `STATUS.LU` is set while BMSR's aneg-done bit
  never showed through our MDIO path. Consistent with 08-13, where the link came
  up via `na-phy-kick` rather than our CTRL write. Whether we are reading the
  wrong page, or firmware completed aneg before we asked and the latch had
  already cleared, is the next question.
- **`RDH` moved: 0 in the bed, 15 on metal, with `RDT=15`.** Either the receiver
  filled the ring during the 93 seconds `phy-bring-up` was spinning -- which
  would mean frames DO move and NIC-3's original question is answered yes -- or
  `RDH` is not writable here the way `CTRL` is not. **This arm cannot separate
  those and does not claim to.**
- **The gfat mount failed at stage 2 again** (no EFI PART signature), so
  `NIC3.TXT` was not written, while `GopShot`'s F12 write succeeded on the same
  volume. Two write paths, one works. Not chased here.

**The returned volume is clean on all four questions**: every chain matches its
size, no overlaps, nothing allocated to nothing, FAT copies identical.

## SUPERSEDED BY THE ABOVE -- the pre-flight card for `nicinit.img`

**On disk 2 and verified.** The image hash was checked against the digest below
before firing, disk 2 was confirmed USB, and `flash-usb` read all three fixup
blobs back and reported the write flushed and read back byte for byte. Pull the
stick and boot the ASUS from USB with CSM/Legacy off.

**The dump taken before it is the more valuable artifact and it nearly did not
exist.** `before-nicinit-20260815.img`, `C127DA97...`, matches nothing already
in the archive, and there was no `nicsitting-returned` row at any point: this is
the 2026-08-14 NIC flight's stick as it came back, captured for the first time
and about a minute before the flash would have destroyed it. That flight banked
nothing -- its own entry below says "photograph only, the stick did not mount"
-- so this is the only physical evidence from it that exists. The standing
archive-first ruling has now paid for itself twice in this file; the other time
is `before-a5fix` differing from `vmxprobe-returned` with no flight between them.

`nicsitting.img` proved `e1000-init` is entered and does not return. It did not
say which of the ten loops inside it, because `e1000-init` is one expression.
This arm performs that function's own sequence out of its own primitives, in
its own order, and paints a row BEFORE each step. **The last row on the glass
is the answer**: a step that returns overwrites its own row with a result and a
duration, and a step that hangs leaves its `ENTERING` line standing.

The driver is unchanged. What is reproduced rather than called is the body of
`e1000-reset` and of `e1000-init-after-reset` -- the two functions whose
interiors need rows -- transcribed operation for operation. That is the licence
`AsdeStageProbe` took on 2026-08-13 and it is taken here for the same reason.

```powershell
pwsh build/boot/build-option-a.ps1 -Src build/boot/diag/NicInitProbe.codex `
     -Kernel seed/Codex.cdx -Ebs -Out build/boot/nicinit.img
```

| | |
|---|---|
| image | `build/boot/nicinit.img`, 16,777,216 bytes |
| SHA-256 | `4C6F61DA 133F5D42 FECD3C6B 4374B21B D46C5297 DF2D35E0 1EC4EB95 35582DFA` |
| probe source SHA-256 | `85301EF6 A6D2C6B7 A8BA63C8 C333218F 1F82A6C8 90B6A37D C61085D0 AAA450CB` |
| built against seed | `F3722EAC019ACD5A` |

**THE RECIPE NO LONGER REPRODUCES THAT IMAGE HASH, and that is expected.** An
option-a image is three moving parts -- the UEFI stub, the embedded
`seed/Codex.cdx` shipped as `CODEX.CDX`, and the compiled payload -- and the
hash moves when ANY of them does. Both of the first two moved within a day of
this flash:

| rebuilt | hash | why it moved |
|---|---|---|
| as flashed | `4C6F61DA 133F5D42...` | -- |
| after the seed went to `55983566` | `231E9F40 1F5AB08D...` | the embedded seed |
| after the GOP stub change, main 15469 | `2DBBD1B5 5A41DA35...` | the stub |

**None of that is evidence about the flown stick.** It is byte-identical to
what was bed-rehearsed in both directions, which is what the 2026-08-14 ruling
asks for. A recorded hash its own recipe cannot reproduce is the shape that
gets read as corruption, so: account for the stub and the seed first, and only
then suspect the image.

**One thing the stub change improves for anyone re-running this arm.** The
probe is an `-Ebs` payload, so it is one of the non-`-EntryStart` ones whose
stub now selects the largest enumerated mode. Measured: a rebuild comes up
**1024x768 in the default bed** where the flown image comes up 640x480. The
ASUS geometry is therefore the bed default now, and checking the row budget no
longer needs `-gop-width 1024 -gop-height 768`.

### What each row should say, written down before it flies

Bed values at 1024x768 with `-e1000-nat`, which is the ASUS geometry. **The
durations will differ on metal and that is the point of printing them**; what
must not differ is which rows appear.

| row | bed | what metal tells you |
|---|---|---|
| `arrival RCTL/TCTL` | `RCTL=0 EN=n TCTL=0 EN=n` | 08-14 read `RCTL=0 EN=n TCTL=805564664 EN=n`. Differs means the board did not arrive as it did last time and nothing below is comparable. |
| `arrival RDBAL/RDLEN/RDH/RDT hpet` | all 0, `hpet=14318179` | 08-14 read `RDBAL=1551914016 RDLEN=0 RDH=0 RDT=0`, `hpet=23999999` |
| `s1` | `pcien=0 ctrl=0` | `ctrl` is the arrival CTRL; RST is discarded on this part |
| `s2 await-reset` | `ret 1 in 16us` | returns 1 on its first read because CTRL is read-only here |
| `s3 settle-mdio` | `ret 1 in 10118us` | the datasheet's 10 ms window; 0 means the fuel ran out first |
| `s4 quiesce` | `ret 1 in 10050us` | as above |
| `s5 alloc` | `ret 0 in 43us` | arena only, no device access |
| `s6 clear-mta` | `ret 0 in 717us` | a write per multicast entry |
| `s7 setup-rx` | `ret 0 in 39us` | **this is the first step that enables the receiver** |
| `s8 setup-tx` | `ret 0 in 60us` | |
| `s9 phy-bring-up` | `ret 1 in 163us` | MDIO; 08-13 proved MDIO writes work on this part |
| `s10 await-link` | `ret 1 in 19us` | 4,000,000 fuel. A large duration here with `ret 1` is the long-but-finite hypothesis; `ret 0` means the fuel ran out and the link never came up |
| `INIT COMPLETE` | `RDH=0 RDT=15` | if this paints, `e1000-init` does not hang on this board |

**Pass:** every row paints and `INIT COMPLETE` appears. **The interesting
failure:** the rows stop, and the last one names its step. **What would
falsify the whole arm:** all ten steps return and `INIT COMPLETE` paints, in
which case `e1000-init` is NOT where the 08-14 boot stopped and the stall is
somewhere this decomposition does not cover -- which is a real result and
sends the next arm at the row painting itself.

### Bed rehearsal, both directions

Per Damian's 2026-08-14 ruling, same bytes, full loop.

- **Positive**, `-gop-width 1024 -gop-height 768 -e1000-nat`: all ten steps
  return, `INIT COMPLETE RDH=0 RDT=15`, no clipping.
- **Negative**: a sabotage build with an unbounded spin in place of
  `e1000-setup-rx` leaves `s7 ENTERING e1000-setup-rx` as the last row with
  nothing after it. That is the instrument proving it can express the failure
  it exists to catch. The sabotage source was deleted rather than shipped.

**`-screenshot-delay` at 5000 and above gives `code=-1` and no BMP on this
image; 0 and 2000 work.** Found by concluding the sabotage build was broken and
then running the KNOWN-GOOD positive image with the same flag, which failed the
same way. Capture the negative arm at 2000.

## FLOWN 2026-08-14: `nicsitting.img`. NIC-1 and NIC-2 ANSWERED. NIC-3 WEDGED IN `e1000-init`, and the ordering is why we still have the other two.

Photograph only; the stick did not mount, so nothing banked. Every row below is
read off the glass.

```
no bank, mount stage 2 (no EFI PART signature) -- the rows below are the reading
eligible at 0:31.6 verdict=ok
ASDE: read-only touch mmio=3745513472 STATUS=1074266240 CTRL=1573440
NIC-1 RCTL=0 EN=n  TCTL=805564664 EN=n
NIC-1 RDBAL=1551914016 RDLEN=0 RDH=0 RDT=0
NIC-2 hpet-hz=23999999 (0 or absurd voids every duration below)
NIC-2 1000000 empty polls = 32606 us   bed same probe: 13034 us
NIC-2 tick at the 100000 fallback = 3260 us   bed same probe: 1303 us
MIC-3 below writes to the part: e1000-init resets and programs rings. Everything above is already banked.
```

**and then nothing.** The screen holds with the NIC-3 banner as the last line.
The next expression in `opening` is `e1000-init d`, so `e1000-init` was entered
and did not return. That is the 2026-08-11 wedge, reproduced, and this time
bracketed: everything before it is on the glass and the arrival state that
precedes it is now known, which it was not on 08-11.

**The ordering rule earned itself on this flight.** Had NIC-3 gone first, or had
the rows been painted at the end, this boot would have produced one blank screen
and no readings at all.

### NIC-1: the part arrives COLD, and `-e1000-preconfigured` describes a hazard this board does not present to us

`RCTL = 0x00000000`. Receiver flat off, EN clear. `TCTL = 0x3003F0F8`, EN clear,
PSP set. `RDLEN=0 RDH=0 RDT=0` with `RDBAL = 0x5C805420` left stale from
somebody, which is the signature of a driver that programmed a base and then had
its length cleared rather than one that was never there.

**State the claim at the right width.** What is measured is the state at the
point OUR code runs, which is after `ExitBootServices`, and stopping a UEFI SNP
driver at ExitBootServices is exactly when firmware would shut a receiver down.
So this does NOT establish that firmware never ran the receiver. It establishes
the only thing `e1000-quiesce` and `-e1000-preconfigured` actually need to be
about: **at handoff to us, on this board, there is no live receiver over an
unknown ring.** `e1000-quiesce` stays, cheap and harmless; its premise is now
measured false here rather than inferred true everywhere.

### NIC-2: THE CALIBRATION TRANSFERS. The single assumption B3 and B4 rest on is measured and holds.

**32606 us on metal against 13034 us in the bed: a factor of 2.50.** Same order,
and the probe prose named "within a factor of a few" as the pass before the
flight rather than after it. The argument that the real part reads its
descriptor out of RAM as the model does was an argument; it is now a
measurement, and `net-driver-calibrate` measuring the rate at bring-up is the
right shape on real silicon.

Derived, and both are useful numbers nobody had:

| quantity | metal | bed |
|---|---|---|
| empty `e1000-poll-raw` polls per second | 30,669,202 | 76,722,418 |
| polls in a 100 ms tick, which is what calibration will answer | 3,066,920 | 7,672,242 |

`hpet-hz = 23999999`, sane, so both durations above stand.

### NIC-2's second row is a DEFECT, and it is fixable in the tree without metal

`net-driver-poll-fallback = 100000` is the tick used when the HPET rate comes
back unusable. On this metal 100000 polls is **3260 us against the 100 ms that
constant is meant to mean: 30.7x short.** Every retransmit bound in
`NetworkStack` is a count of ticks, so on any board where calibration cannot
run, the 288-tick give-up fires at about 0.94 s instead of 28.8 s and the stack
declares a live peer dead. That is the same defect class B3 was opened to fix,
surviving in the path B3 did not measure.

It does not have a free fix, and that is worth writing down rather than
patching quietly. Sized for this metal the fallback is ~3,000,000; sized for the
bed's e1000 ~7,700,000; sized for the NE2000 model, whose million polls cost
15.52 s, ~640. **No constant is right for all three, which is the argument for
calibration and against the fallback existing as a poll count at all.** Too
short declares live peers dead; too long delays give-up by the same factor, so
it is not simply the safe direction. Not fixed on this pass. Opened as B5.

### What NIC-3 leaves open

`e1000-init` does not return on the real I219-V. Two readings from the rows
above are the first evidence anyone has about why, and both are one register
read away from being confirmed or dropped on the next flight:

- **`STATUS = 0x40080080`: LU clear.** The link is DOWN at read time, with the
  cable in a live switch. The SPEED field reads 1000 but SPEED is meaningless
  with LU clear. The 08-13 entry below says the link comes up on this part, and
  both can be true: that reading was taken after our bring-up ran, this one
  before it.
- **`STATUS` bit 19 set**, which on the I217/I218/I219 family is PHY Reset
  Asserted. A part whose PHY is asserting reconfiguration is a plausible way for
  a reset-and-wait to spin forever, and `e1000-init` waiting on a link or a PHY
  handshake that cannot complete is the shape that fits both this flight and
  08-11.
- `CTRL = 0x00180240`: SLU set, ASDE clear, RST and PHY-RST clear. Consistent
  with the 08-13 finding that CTRL is read-only on this part.

**The next NIC-3 arm should print inside `e1000-init` rather than around it** --
a row per wait loop with its fuel remaining -- because the question is no longer
whether it wedges but which wait it wedges in. NIC-4 and NIC-5 are unchanged and
still behind it.

## READY TO FLASH 2026-08-14: `nicsitting.img`. NIC-1, NIC-2 and NIC-3 on one boot.

**SHA-256 `E7128273 5DA511E3 2B20D98E 7766B79A 0344261F F6F2D4FD 60735F73 4368E26F`**,
16 MB, PE 266752, seed 2793222. Built off blu:

```powershell
pwsh build/boot/build-option-a.ps1 -Src build/boot/diag/NicSittingProbe.codex `
    -Kernel seed/Codex.cdx -Ebs -Out build/boot/nicsitting.img
```

`-Ebs` and not `-Uefi`, for the reason the 08-13 arm gives: this payload drives
the NIC and the medium with our own code, which is the whole point of a
driver-truth probe.

### THIS IMAGE COULD NOT BE BUILT AT ALL FOR MOST OF 2026-08-14

**No `-Ebs` image built after main 15041 boots.** `AsdeStageProbe.codex`,
unchanged source, the arm that flew on 08-13, rebuilt with that toolchain halts
in the stub at 14 exits printing `svcV`. The 08-13 BINARY still boots in the
same bed, so it was the build and not the bed. Cause: 15041 moved the heap
request from `AllocateMaxAddress` against a 3 GB ceiling to `AllocateAnyPages`,
which is correct for the A5 path that keeps firmware paging, and it lets
firmware place the heap across the framebuffer. reek's own new `V` guard then
refuses it, correctly. Fixed in `build/cdx-to-pe.ps1`, `-ExitBootServices` only,
so the A5 stub is byte-identical and the white screen is pinned by construction
rather than by inspection.

**The ceiling is NOT the framebuffer base, and the first fix used it and still
tripped `V`.** The guard measures a FIXED 256 MB window from the heap base
rather than the heap's real extent, so a 128 MB heap ending exactly at the
aperture still fails it. The request is backed off to satisfy the window.

### The rows, in the order they paint, and what each answers

| row | reads | a result is |
|---|---|---|
| bank | mount stage, `NIC1.TXT` written | either; the glass is the bank |
| eligible | bus:dev.fn and BAR verdict | `0:31.6 verdict=ok`. **Anything else is a finding** and the probe stops there without touching it |
| ASDE touch | `STATUS`, `CTRL`, before any write | 08-13 read `STATUS=1074266240 CTRL=1573440`. A wildly different pair means the BAR is wrong and every row below is noise |
| **NIC-1** | `RCTL`, `TCTL`, `RDBAL/RDLEN/RDH/RDT` | **EITHER ANSWER.** `RCTL EN=y` means the receiver is live on arrival and `-e1000-preconfigured` is right about this board. `EN=n` means the model describes a hazard this part does not have |
| **NIC-2** | 1,000,000 empty polls, in microseconds | the number IS the result. Bed, same probe: 13034 us, tick 1303 us. **Metal within a factor of a few confirms the calibration transfers; orders of magnitude out means every retransmit bound is wrong on metal** |
| NIC-2 hpet | `hpet-ticks-per-second` | non-zero and sane. 0 or absurd VOIDS both durations above |
| **NIC-3** | init, send accepted, RECEIVED, RDH/RDT | `RECEIVED=YES` with a length is the first frame this board has ever taken off the wire. `no` with RDH moved means the ring advanced and we did not read it; `no` with RDH still 0 means the receiver never ran |

### What is deliberately NOT here

NIC-4 (a real TCP conversation) needs NIC-2 and NIC-3 to have passed first and
is uninterpretable before them. NIC-5 (what wedged the box on 08-11) is the arm
designed to reproduce a hang and is not on this image at all.

### Rehearsed as the exact bytes, per L-REHEARSE

Three beds, all on the image whose hash is above:

- codex-vm `-e1000 -e1000-phy-link`, no traffic: every row paints, `RECEIVED=no`.
- codex-vm `-e1000-nat -e1000-phy-link`: **`RECEIVED=YES len=42 RDH=1 RDT=0`**.
  That is the positive control and it is what makes the receive row an arm
  rather than a decoration.
- OVMF with a USB disk: stub marks `svchgxo` (the heap allocation the fix
  above repairs), `bank live, NIC1.TXT written=y`, and the ineligible part
  correctly `REJECTED ... verdict=below-window` with nothing touched.

**The OVMF bed caught a safety defect in this probe and it is why that arm
exists.** The first version gated on `e1000-find`, which is vendor and class
only, and then read MMIO from whatever it found regardless of the BAR verdict.
OVMF offered a device at `0:2.0` with `verdict=below-window` and the probe
happily touched it. On this board that would be poking MMIO at an address the
part does not own, which is the class of thing NIC-5 exists to explain. It now
gates on `na-eligible`, vendor AND BAR, exactly as the flown arm does.

### Flashing it

Archive first, per the QUICKREF at the top of this file. **The dump needs
elevation and somebody to accept the prompt.**

```powershell
pwsh build/dump-usb.ps1 -DiskNumber 2 -Out D:\Projects\stick-archive\before-nicsitting-20260814.img
Get-Disk | Where-Object BusType -eq 'USB'      # confirm 2 is the stick, check it twice
(Get-FileHash D:\Projects\NewRepository-blu\build\boot\nicsitting.img -Algorithm SHA256).Hash
Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-blu\build\flash-usb.ps1','-Image','D:\Projects\NewRepository-blu\build\boot\nicsitting.img',
  '-DiskNumber','2','-SpecFit','-Force','-Log','D:\Projects\NewRepository-blu\build-output\flash.log'
```

**Flash, verify, PULL, and do not reinsert.** One eject-and-reinsert on Windows
rewrites LBA 1 and is the standing suspect for the mount failures on this arm.

**Plug the Ethernet into a live switch or router before booting.** NIC-3 asks
whether a frame arrives, and with no link partner it cannot. The port is the
one behind the Intel I219-V, which the probe reports as `eligible at 0:31.6`.

### The boot, and the only thing to do

Boot from the stick. The rows paint top to bottom and the screen holds.
Photograph from the top. F12 banks the screen to the stick if the medium
mounts, and that is a bonus and not the reading: the F12 bank failed on 08-13
with `no esp s1 m3 c4 p1`, a USB transaction error in the CBW phase.
## FLOWN 2026-08-13, GREEN: `vmxprobe.img`. VT-x IS AVAILABLE ON THE ASUS.

**Damian read three lines off the glass:**

```
IA32_FEATURE_CONTROL = 5
VT-x available
VMX revision id 4
```

**5 is `101`: bit 0 lock set, bit 2 VMX-outside-SMX set.** That is
firmware with VT-x switched ON and locked, and it is a different number
from the 1 the bed reports. **The bed was never a proxy for this box on
this question**, which is the durable half: two independent measurements
under codex-vm both said 1, agreed with each other, and were both
irrelevant to the hardware. An instrument can be reproducible and still
be pointed at the wrong thing.

**The revision id is a real read, not a default.** `vmx-read-revision-id`
(`apps/works/DevHypervisor.codex:41`) returns 0 when `vmx-available` is
False and otherwise masks bits 0-30 of `IA32_VMX_BASIC`, so a non-zero
answer proves the MSR was reached. 4 is lower than the values Intel
parts usually report; it does not matter and must not be "corrected",
because the VMCS has to be stamped with whatever THIS processor reports
and that is exactly what the code does.

This unblocks Road A in `docs/Designs/Active/OS/DeskBuildLoop.md`. The
next wall there is the arena, not the wiring.

**The wizard behaved as documented on metal**, first-boot path walked by
hand.

### What the machine wrote, read off the returned stick

Dumped to `D:\Projects\stick-archive\vmxprobe-returned-20260813.img`,
SHA-256 `2FCF8F98 994DFE45 7ABE6E6F BEC323F8 D6DFE479 477FA2C0
B761A90C 154AD0CE`. Compared sector by sector against the image that was
flashed: **exactly six sectors differ, and all six are explained.**

| LBA | What |
|---|---|
| 0, 1 | `flash-usb.ps1 -SpecFit` rewrites these itself; not the guest |
| 2118 | FAT copy 0, the entry for cluster 17705 |
| 2222 | FAT copy 1, the same entry |
| 2257 | root directory, the `IDENTITY.DAT` slot |
| 19992 | the file's one data sector |

`IDENTITY.DAT` is **124 bytes**, matching both the bed run and the
2026-08-05 ceremony stick. **Both FAT copies are identical**, so the
guest's FAT writer maintains the mirror rather than leaving copy 1
stale -- worth knowing, because a stale second FAT is invisible until
some other reader picks it. `SOURCE.SRC`, `CODEX.CDX` and all 64
chapters under `SRC/` are byte-for-byte what was flashed: the wizard
touched nothing it did not own.

**This dump carries a private key generated on the ASUS.** It is the
only copy, which is the whole reason the archive rule exists.

Flight record of the image below.

## THE IMAGE: `vmxprobe.img`

`vmxprobe.img`, SHA-256 `8E95E062 AF2EED56 0554B331 1D845B98 DF59251E
87B199CB 794A2B28 42EBA697`. Built from main 14829 by
`build/build-boot-img.ps1`. Flashed to disk 2 (` USB DISK 2.0`, 28.9 GB)
2026-08-13 16:32 with `-SpecFit -Force`: all 16,777,216 bytes read back
matching, four SpecFit sectors verified individually
(`build-output/flash-vmxprobe.log`).

**The whole flight is three keystrokes and one photograph.** At the desk:
press **`t`** for the Console pane, type **`vmx`**, press **Enter**.

**Bring back the literal text, not a summary.** It prints
`IA32_FEATURE_CONTROL` (MSR 58) as a number, then either a VMX revision
ID or a sentence naming why VMX is unavailable. Both halves are wanted:
the number is the measurement, the sentence is the diagnosis. It wraps
at 62 columns, so it may run to two lines and both are needed.

**Why it is worth a boot.** Everything in `DevHypervisor` gates on
`vmx-available`. Under codex-vm that MSR reads **1** -- lock bit set,
VMX-outside-SMX clear, the encoding of firmware with VT-x switched off
-- measured 2026-07-27 and independently reproduced 2026-08-13 through
this console, a different surface on a different day. **It has never
been read on metal**, because until the console pane existed no surface
could ask: Dev Console needs a UART the ASUS does not have. That one
number picks the road in
`docs/Designs/Active/OS/DeskBuildLoop.md`: VT-x present wires
`vm-compile-cdx` to a `compile` command, VT-x absent kills Road A and
sends the work to Road C. Nothing else settles it.

**A FRESH IMAGE NEED NOT COST A WIZARD ANY MORE.** `build-img.ps1` and
`build-boot-img.ps1` take **`-Identity <path>`** since 2026-08-13, which
writes an existing `IDENTITY.DAT` into the ESP root, and
`wz-identity-present` is exactly a `gfat-file-size` on that name. The
identity off this flight is at
`D:\Projects\stick-archive\IDENTITY-asus-20260813.DAT`. **Use one that
came off the TARGET machine**: an image built with a key generated in the
bed puts a bed-generated key on a flown stick, which is the failure the
section below describes. A path that does not exist is a hard error
rather than a silent fall back to the wizard, because a silent fall back
is discovered at the machine, after a flash.

**`-Identity` alone gets you to the desk LOCKED, which is not the same as
unlocked, and the first version of this section got that wrong.** With an
identity on the volume and no keys injected, the boot stops at "Welcome
Back / Passphrase to unlock" (measured at 7 s), and it only moves on
because the no-keyboard fallback resolves the screen for it. The desk is
then reached with the identity never unlocked. Reaching the desk is
therefore NOT evidence that an identity was accepted: the two look
identical from the last frame, which is exactly why the last frame was
the wrong place to look.

**`wz-auto-pass` closes that.** `GopWizard` tries a development
passphrase before drawing the prompt, so a returning stick reaches
"Identity Unlocked" with the fingerprint and "The public key matches the
stored identity" on the glass (measured at 7 s), then the desk. The
constant is four characters in a public mirror and protects nothing; an
identity wrapped with a real passphrase does not match it, falls through
to the prompt, and is unaffected.

**Four Enter-gated screens stand between the wizard and the menu** --
Storage, Disks, xHCI, Wake -- each a `wz-wait-enter`. One Enter with key
repeat flushes all four and lands in the menu, which reads as the boot
"ending early". Nothing is wrong when that happens; each screen also
auto-advances on its own after about 30 s. **Measured 2026-08-13: with no
keys at all, the desk is up and stable at 90 s**, clock ticking. If a boot
looks stuck, wait before concluding.

**A FRESH IMAGE WITHOUT `-Identity` BOOTS TO THE WIZARD, NOT THE DESK.** It
carries no `IDENTITY.DAT`. Expect GopBoot menu -> Welcome -> Passphrase
-> Confirm -> Entropy -> Upstream -> **Timezone** -> keygen -> Identity
Created (with a fingerprint) -> Storage -> choose interface, and only
then the desk. The Timezone screen is Up/Down and Enter and writes
`TIMEZONE.DAT` beside the identity (main 14907); it defaults to UTC,
which is the one offset that is never a guess about where the machine
is.
Measured end to end in the bed at under 50 seconds including ECDSA
keygen: it is a minute of typing, not a wait, and it is not a fault.
That walk happens ON THE METAL BOX deliberately -- see the section below
on why pre-running the wizard in the bed and flashing that image puts a
bed-generated private key on the flown stick.

## FLOWN 2026-08-13, third and fourth boots: CTRL IS READ-ONLY ON THIS PART, and two claims below are wrong because of it.

Two more boots the same evening, and between them they close B2's register
question and correct the entry beneath this one.

**Boot 3** added a `CTRL` readback after each arm. Both arms answered
`CTRLback=1573440 ASDEbit=n` -- `0x180240`, the firmware value, with the
ASDE bit refusing to set even on the arm that ran FIRST from the firmware
state, which was the flight built to remove the inheritance confound.

**Boot 4** cleared `CTRL.SLU` and read it straight back. All four rows:
`CTRL=1573440 SLUbit=y STATUS=1074266243 LU=y`. **The bit we cleared did not
clear.** CTRL writes are discarded whole on this part.

**What is NOT broken, and this is the half that matters for B2c: MDIC writes
work.** `phy=y` on every arm means `e1000-phy-write` succeeded, and it works
by writing MDIC at `0x0020` and polling until the MAC sets Ready. That is a
CSR write and it lands. So the CSR write path is fine and `CTRL` specifically
refuses. **RX/TX needs `RCTL`, `TCTL`, `RDBAL/H`, `RDLEN`, `RDH/RDT`,
`TDBAL/H`, `TDLEN`, `TDH/TDT`, `RAL/RAH` and not `CTRL` at all**, so B2c is
not blocked by this.

### Two corrections to the entry below, both mine

**"The link came up under our code" is right; my attribution was wrong.** The
link is brought up by `na-phy-kick` over MDIO. The CTRL write in
`na-bring-up-after` contributes nothing and never did.

**"The reset did not wedge when the arms run first" describes an event that
never happened.** `e1000-reset` writes `CTRL|RST`; that write is discarded, so
RST is never set, so `e1000-await-reset` sees it clear on its FIRST read and
answers `settled=1`. A reset that never ran is indistinguishable from one that
completed. The cold-versus-warm hypothesis in that entry was built on this and
should not be pursued: there is no evidence any reset has ever executed on this
part. What wedged the box on 08-11 is therefore **unexplained again**, and it
was not `CTRL.RST`.

**No more sittings are needed to work on this.** `codex-vm -e1000-ctrl-ro`
reproduces the board: same `CTRLback`, same `ASDEbit=n`, same `SLUbit=y`
through all four SLU rows, same `settled=1`, and `LU=y` anyway.
`codex/test/e1000-ctrl-ro` pins it, with a control run proving three of its
six rows flip when the arm is off.

## FLOWN 2026-08-13: `asdeflight.img` -- THE LINK COMES UP ON THE REAL I219, and the reset does not wedge when the arms run first.

`asdeflight.img`, SHA-256 `BB99E629 4F5DE5FB 731E3F8C 72720D4D 92AFA489
D2407844 B6BF172B 357F48A7`. Flashed by blu, flown by Damian, cable in a
live switch with the link light lit at the port. **Every row painted, and
the machine did not wedge at any point.** Rows transcribed at the board:

```
read-only touch mmio=3745513472 STATUS=1074266240 CTRL=1573440
ASDE=0 noreset gave=ok phy=y LU=y FD=y SPEED=1000 ASDV=10 aneg=n STATUS=1074266243
ASDE=1 noreset  (same values)
RESET done: settled=1 ICR=260 STATUS=1074267267
```

| row | STATUS | LU | FD | SPEED |
|---|---|---|---|---|
| touch (read-only, before any write) | `0x40080080` | **0** | 0 | 1000 |
| `ASDE=0 noreset` | `0x40080083` | **1** | 1 | 1000 |
| `ASDE=1 noreset` | `0x40080083` | **1** | 1 | 1000 |
| `RESET done` | `0x40080483` | 1 | 1 | 1000 |

**THE TOUCH ROW IS BYTE-IDENTICAL TO THE 08-11 FLIGHT** -- `STATUS=0x40080080`,
`CTRL=0x180240`, `mmio=0xDF400000`. Same part, same firmware handoff state,
so the two flights are directly comparable and the difference is ours.

**The link came up under our code, and the touch row is what proves it.** It
is read before the probe writes anything, and it reads `LU=0` WITH the cable
connected and the link light lit. So the PHY was negotiating with the switch
on its own while the MAC had no link, and `LU` went to 1 only after
`na-bring-up-after` ran. `ICR=0x104` carries bit 2, Link Status Change: the
link changed state during the run. The link light alone could not have
established this, because a powered PHY out of reset negotiates regardless of
what the driver does -- the read-only touch is the control that separates the
two, which is the whole reason it is read before any write.

**The reset did NOT wedge, on the same driver code that wedged on 08-11.**
`settled=1`, so `CTRL.RST` self-cleared. The only thing this build changed is
ORDER: on 08-11 `na-bring-up` pulsed RST on a cold part as the first write
after firmware handoff; here both arms ran first, so the PHY had been
soft-reset over MDIO and `CTRL.SLU` written before RST fired. **A reset on a
warmed part is evidently not the same operation as a reset on a cold one.**
That is a hypothesis with one observation behind it, not a settled mechanism,
and the discriminating flight is a probe that resets cold FIRST and then warm.
It matters beyond this probe: `e1000-init` (`E1000e.codex`) resets cold, which
is precisely the order that wedged.

**Finding 4 is NOT settled, and the arms being identical is not the same as
ASDE being inert.** In the bed the two arms differ sharply (`SPEED=1000
STATUS=130` against `SPEED=10 STATUS=2`); on metal they are the same value.
Three explanations survive this flight and it cannot separate them:

1. ASDE genuinely does nothing on this part;
2. the ASDE write does not stick, and **the probe never reads `CTRL` back to
   check** -- the arm row prints STATUS only;
3. arm 2 inherited a link arm 1 had already established, so there was nothing
   left to change. **The arms are not independent**, which is a defect in the
   probe's design rather than in the reading.

The next arm reads `CTRL` back after each write and runs `ASDE=1` first from
cold. Until then, do not record Finding 4 as answered.

**Loose thread: `aneg=n` while `LU=1` at 1000 Mb/s full duplex.** Autoneg-done
is not set in BMSR yet the link is up at gigabit. Either the BMSR read is
landing on the wrong page or the link came up without autoneg completing.
Unexplained, not chased, and it does not affect the readings above.

### The F12 bank failed, and it says why -- it is a transport failure, not the GPT

F12 answered `no esp s1 m3 c4 p1 w1964712320 f945044 l1 r1`. Decoded against
the source rather than from memory:

| cell | value | meaning | defined at |
|---|---|---|---|
| `s` | 1 | `gfat-stage-hdr`, the GPT header read FAILED | `GopFat16.codex:84` |
| `m` | 3 | `med-kind-usb`, the USB medium was selected | `GopDisk.codex` |
| `c` | 4 | `trb-cc-usb-transaction-error` -- a real completion event, code 4 | `GopUsbKbd.codex:46` |
| `p` | 1 | `msc-ph-cbw`, it failed in the Command Block Wrapper phase | `GopUsbMsc.codex` |
| `w` | 1964712320 | `gfat-cell-write`, never written on a READ path: uninitialized, meaningless here | `GopFat16.codex:101` |
| `f` | 945044 | smallest fuel any COMPLETED transfer left, of `xhci-fuel` = 1000000 | `GopXhci.codex:63` |
| `l` | 1 | fail LBA 1, which IS the GPT header sector | |
| `r` | 1 | `msc-retry-recover-failed`: reset recovery ran and failed | `GopUsbMsc.codex:75` |

**This REFUTES the standing hypothesis in the arm section below.** That
hypothesis is that one eject-and-reinsert rewrites LBA 1 and moves
`PartitionEntryLBA` from 2 to 2047, so our reader cannot mount a stick that
still boots. That predicts a SUCCESSFUL read carrying the wrong CONTENT, which
lands on `gfat-stage-sig` (2) or `gfat-stage-guid` (4). What happened is stage
**1**: the read itself failed. The content theory cannot produce a failed read.
The stick had also been flashed an hour earlier with `-SpecFit` and full
byte-for-byte readback verification, so the medium was known good.

**It is also NOT WORKS-9.** That is a data-phase timeout with NO completion
event. This is a completion event ARRIVING, with an error, in the command
phase, before any data moves. And `f945044` of 1,000,000 clears fuel by
WORKS-9's own stated criterion ("an `f` near 1000000 says the fuel is innocent
and the device stopped answering").

**What it does not settle**, and the alternative is cheap to state: the ASUS
presents several USB interfaces behind a Unifying receiver, and nothing here
proves the MSC driver bound the STICK rather than some other bulk endpoint. A
CBW answered with a transaction error is what binding the wrong device would
also look like. The next probe that cares should print the bound device's
VID:PID beside `m`.

**Three consecutive flights of this arm have now failed to mount**, and this is
the first one that says anything about why.

## FLOWN 2026-08-11, ALL GREEN: `browserflight.img`. THE MOUSE AND THE BROWSER WORK ON METAL.

`browserflight.img`, SHA-256 `1A5F8B05 B77A6AD0 14620F20 AC1E1061 6C27DB3A
67898FA9 F16E74A7 E9CA8BA2`. Built from main the same hour, GopBoot payload,
flown by Damian.

**Damian's report, and every clause of it is a separate first:** "the arrow
cursor works, it clicks, those clicks work, the app works, the pages
render."

That closes WORKS-10, which is deleted from `apps/works/works-backlog.md`
rather than annotated. **Its measurement survives here because it is still
true and still costs a session if rediscovered:**

**No scripted input in this workspace's bed moves the desk's cursor**,
measured 2026-08-11 with a probe. On plain `-mouse` the USB mouse does not
enumerate at all -- `um-ok` is false, because that flag drives the guios
I/O-port pointer at 0xE1-0xE4 and not the USB stack. Under `-hid-combo` it
DOES enumerate and reports flow: a tight `mouse-pump` loop read the scripted
position back within 640 exits. And the cursor still stays exactly where
`desk-run` warped it, counted by cursor ink at (320,240) on the desktop and
inside a pane alike.

So the pointer, the arrow glyph, the click dispatch and the page hit test
had all shipped without one end-to-end run between them. **Do not read a
green bed run as evidence about the pointer in either direction.**

**What this specifically proves, because "the mouse works" is four claims:**

| claim | how the flight settled it |
|---|---|
| the USB mouse enumerates and delivers on this board | the arrow moved |
| the arrow glyph renders and its save-buffer discipline holds | it tracked without smearing |
| `desk-loop`'s click-to-open-a-pane dispatch works | a pane opened |
| the browser's laid-tree hit test works | pages navigated |

The last of those is the change that landed as main 14621 the same day and
had never run outside a test: the browser keeps the tree that was actually
DRAWN in `bs-laid` and hit-tests THAT, because the tree in `tab-content` is
the raw builder output with `layout-rect-zero` on every node.

Off-bed, clicking is pinned without a pointer at all by
`codex/test/apps/browser-click-dispatch` for the chrome,
`codex/test/apps/browser-click-links` for page links, and
`codex/test/apps/desk-calc-click` for the calculator keypad. Those say what
the app does with a coordinate; only a flight says a coordinate arrives.

**The bed and the board disagreed in the SAFE direction here**, which is
worth noting beside the two flights above where they disagreed the other
way. The bed could not move a cursor and the board could. Nothing was
wrong; the instrument was.

## FLOWN 2026-08-10: the Welcome Back hang did NOT reproduce, and it is NOT fixed

`seed/Codex.img` `4564D27F6C28EF09`, on the ASUS. Ceremony on first boot,
then Welcome Back on the second boot, and the passphrase was accepted and
the identity unlocked. **The hang the previous stick showed is unexplained
and is recorded as an open intermittent, not as closed.**

**Do not read this as a fix.** The only change to the boot payload between
the stick that hung and the one that did not is the heartbeat added to
`gt-read-line` (`apps/works/GopText.codex`): two `gop-fill-rect` calls every
65,536 polls and a `kbd-released` read. That is diagnostic drawing. There is
no mechanism by which it repairs a keyboard, and nobody has offered one.

**The instrument, and the baseline it just produced.** Passphrase entry is
the one wizard screen with no vitals line: `spin=/sc=` and the
"Handed back to firmware" status come from the wizard's WAIT loop, and
entry runs in GopText, which painted nothing. It now paints two blocks to
the right of the field.

| Left | Right | Reading |
|---|---|---|
| blinking | dark | poll loop turning, controller still ours |
| frozen | dark | the input loop itself stopped; not a keyboard fault |
| either | red | handed back to firmware, and on this board that is a one-way door |

**Measured on this flight through a SUCCESSFUL entry: left blinking, right
dark.** That is the healthy baseline. A future hang showing blinking-and-dark
therefore means the keyboard stopped delivering while the loop and the
controller ownership stayed healthy, which indicts the xHCI/HID path and
exonerates the fallback.

**Why no bed can settle this.** QEMU presents one clean keyboard; the ASUS
presents four keyboard-shaped interfaces behind a Unifying receiver, which
is the topology that produced the completion-steal defect in Update 38. The
unlock path also reads `IDENTITY.DAT` off the ESP over USB mass storage
immediately before taking keyboard input, on the same controller, which is
WORKS-9's neighbourhood. The bed runs the whole path green.

## FLOWN 2026-08-11: `asdeflight.img` -- the RESET wedges, ASDE exonerated. The bank did NOT write, second flight running.

Flashed by blu, flown by Damian. **Two rows were read off the glass and
transcribed verbatim:**

```
eligible at 0:31.6 -- entering bring up
read-only touch mmio=3745513472 status=1074266240 ctrl=1573440
```

In hex: `mmio=0xDF400000` (the BAR), `status=0x40080080`, `ctrl=0x180240`.
**`CTRL.SLU` is bit 6 and it is SET**, which the datasheet in this tree
requires: "The Set Link Up bit MUST be set to 1b to permit the MAC to
recognize the link signal from the PHY" (`docs/Reference/
Intel_82583V_Datasheet.txt`, the CTRL bit table). The remaining bits are
recorded and NOT decoded here; decoding them from memory is how a guess
becomes a fact in a doc, and nobody has read their definitions for this
flight.

**THE VERDICT: the RESET wedges, and ASDE IS EXONERATED.** Damian confirmed
on the glass that nothing sits below the touch row, which is the table's
third outcome exactly. The probe read MMIO successfully -- the touch row
carries live register values, so the BAR is right and the first read does
not wedge -- and then died in the RESET before either arm ran.

**Finding 4 is UNTESTED, not disproved.** The ASDE bit was never written,
because the run never reached the arm that writes it. The next flight is not
a repeat of this one: the wedge is now upstream of the thing this image was
built to test, so the reset path is what needs instrumenting, and B2's
question waits behind it.

**THE FILE BANK DID NOT WRITE, AND THAT IS ESTABLISHED.** The returned stick
differs from the master in exactly two sectors, LBA 0 and LBA 1, which are
the two rewrites `flash-usb.ps1 -SpecFit` performs itself; the root
directory is byte-for-byte the master's `EFI`, `CODEX.CDX`, `CMUNSS.TTF`.
The 2026-08-10 rebuild was made because the previous flight "returned
nothing", and its fix was that "the rows paint whether or not a volume
mounts, and the markers are written". **The painting half worked and the
markers half did not**, so this is the second consecutive flight of this arm
whose entire readout is a human reading glass, which is exactly the
dependency that lost the verdict above. Bed state said `bank live, ASDE0.TXT
written=y` under OVMF with a USB disk, so the bed and the board disagree and
the bed is the one that passes.

**The raw image of this returned stick is DESTROYED**, the same way and for
the same reason as the sinkladder one: preserved to
`build-output/asdeflight-returned-20260811.img`, SHA-256 `F07B8564
5D80CDFC E7FB00B3 53A9B1C4 094FAFBA B33525D0 B926AA96 D9322F1F`, deleted by
a later gate's `clean` phase. The two-sector diff against the master was
measured and recorded BEFORE the loss, so "the board wrote nothing" stands
as a reading; a re-diff is no longer possible.

## THE ARM AS FLOWN 2026-08-13 (result above): `asdeflight.img`, B2. Boot, read the rows top to bottom, pull.

**This arm has flown and its result is the entry at the top of this file.**
What follows is the procedure it was flown with, kept because the next B2 arm
is a variation on it rather than a replacement: read `CTRL` back after each
write, run `ASDE=1` first from cold, and print the bound USB device's VID:PID.
Do not re-fly this image expecting a new answer to Finding 4; it cannot give
one, for the reasons in that entry.

**Rebuilt 2026-08-13 after the 08-11 flight, and the order of the arms is
reversed.** That flight died in `e1000-reset`, upstream of both arms the
image existed to compare, so the reading it was built to take was never
taken. The two arms now run FIRST and without any reset at all, and the
reset rides last, split into the five MMIO operations `e1000-reset`
performs so the glass can name which one did not return.

**The driver is unchanged from the flown build, deliberately.** If the
reset path moved, the staged reset below would no longer be measuring the
sequence that wedged and a different outcome would be uninterpretable.

**The 2026-08-10 flight of this image returned nothing, and the probe was
at fault, not the machine.** The mount was a PRECONDITION: `gfat-mount-esp`
failed, the probe painted `no ESP -- nothing can be banked, read the glass`
and returned before a single stage ran. Nothing touched the NIC. Two boots
had already established that a wedge leaves the glass readable
(2026-08-05 below: rows painted, machine stalled, rows still there), so
abandoning the flight for want of a file bank was never justified. Fixed:
the rows paint whether or not a volume mounts, and the markers are written
when one happens to be there.

**Why it would not mount is not established and the next boot answers it
itself.** `gpt-esp-start` checks the `EFI PART` signature and no CRC, and
`build/flash-usb.ps1:198-207` records, measured 2026-07-29, that ONE
eject-and-reinsert on a Windows machine rewrites LBA 1 and moves
`PartitionEntryLBA` from 2 to 2047, which holds zeros. Firmware validates
CRCs and falls back to the backup GPT, which is why a stick our reader
cannot mount still boots. That fits, and it is a hypothesis: the image
itself was checked clean (ESP at entry 0, LBA 2048, type GUID byte-exact,
`PartitionEntryLBA=2`), and plugging the stick in to look would be the
very act that rewrites it. The probe now prints WHICH of the seven mount
stages failed, off diag cell 80, which GopFat16 has always recorded.
**Operationally: flash, verify, PULL, and do not reinsert.**

**Provenance.** Rebuilt 2026-08-13 off blu, seed `D9A6A7A2BF162346`:

```powershell
build/boot/build-option-a.ps1 -Src build/boot/diag/AsdeStageProbe.codex `
    -Kernel seed/Codex.cdx -Ebs -Out build/boot/asdeflight.img
```

16 MB, PE 259584, seed 2759577, FAT16 spc=1 clusters=26350. SHA-256
`BB99E629 4F5DE5FB 731E3F8C 72720D4D 92AFA489 D2407844 B6BF172B 357F48A7`.
This is the image both bed runs below were re-measured against after the
rebuild; a further rebuild is a different image and its readings would be
unmeasured. The 08-10 build (PE 256512, seed `AF4E14D9703985AC`, SHA-256
`4145AA69...`) is the one that flew on 08-11 and is superseded.

**Note the absent `-Uefi`, which is deliberate.** `build-option-a.ps1`
gained that switch on 2026-08-10 and its own comment calls it mandatory for
a payload that touches the disk under firmware. It is mandatory for a
payload that reads the disk THROUGH firmware, and this one does not: `-Ebs`
exits boot services precisely so the NIC and the medium are driven by our
own code, which is the whole point of a driver-truth probe. The two are
mutually exclusive; do not add `-Uefi` to the command above.

### Flashing it, in paths that exist on this box

Self-contained on purpose. The flash recipes further down this file belong
to other flights and name **other agents' workspaces**; a reader who scrolls
to the first `flash-usb.ps1` block finds red's `deskboot.img` under
`D:\Projects\NewRepository-red\`, which is correct there and wrong here.

Archive whatever is on the stick first, per the QUICKREF at the top of this
file: a returned stick is evidence and there is exactly one of it.

```powershell
Get-Disk | Where-Object BusType -eq 'USB'      # find N -- check it twice
(Get-FileHash D:\Projects\NewRepository-blu\build\boot\asdeflight.img -Algorithm SHA256).Hash
Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-blu\build\flash-usb.ps1','-Image','D:\Projects\NewRepository-blu\build\boot\asdeflight.img',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\Projects\NewRepository-blu\build-output\flash.log'
```

Confirm the hash matches the digest above before flashing. **Flash, verify,
PULL, and do not reinsert** -- one eject-and-reinsert on Windows rewrites
LBA 1 and is the standing suspect for the mount failures on this arm.

### Before you boot: the cable

**Plug the Ethernet port into a live switch or router.** This line did not
exist until 2026-08-13 and its absence was the largest hole in the sheet.
Every value the probe prints is link state -- `LU`, `FD`, `SPEED`, `ASDV`,
`aneg` -- so with no link partner both arms report the same nothing and the
flight cannot compare them, which is the one thing it exists to do. It also
changes how long a HEALTHY run takes: `NicAsde.codex:37-43` records that an
arm on a part with no link is "indistinguishable from a hang".

The port is the one behind the Intel I219-V, which the probe reports as
`eligible at 0:31.6`. There is a second NIC on this board, a Realtek at
`06:00.0`, and the probe gates it out on vendor and BAR
(`NicAsde.codex:183-191`), so a different address on that row is itself a
finding.

### The boot, and the only thing to do

Boot from the stick. Read the rows.

**Then press F12, and only then pull the stick.** The probe ends in
`shot-wait`, which paints `F12 saves the screen to the stick` at the bottom
of the glass and waits for it (`apps/works/GopShot.codex:156,161-185`).
This sheet said "there is no keypress" until 2026-08-13, which was wrong,
and wrong in the most expensive available place: two consecutive flights of
this arm came home with nothing but a human reading glass, and F12 is the
channel that answers exactly that. It needs a mounted volume and this board
has never given us one, so it may do nothing -- which is why the photograph
below is still the primary record and not a backup.

**Photograph the whole screen**, because the rows below the arms are as
much of the reading as the arms are.

**Give it 60 seconds before you decide a row is the last row.** The
last-row-is-the-answer rule is the whole method and it is worthless without
a dwell time. Where the number comes from: each arm budgets 2000 ms of link
wait if HPET reports a rate and falls back to 2,000,000 blind register
reads if it does not (`NicAsde.codex:62-85`), and the reset stage polls up
to 1,000,000 times with no clock at all. Under codex-vm with a link the
whole probe finishes in under 4 seconds; on metal, where an MDIO
transaction is tens of microseconds, it is slower and nobody has measured
by how much. 60 seconds is generous on purpose.

The rows come in three groups and the LAST one painted is the answer. The
expected good outcome is now that every row paints, including the reset
ones: that is a different shape from the previous flight, where a wedge in
the middle was the anticipated result.

**Before believing any arm row, read its `STATUS=`.** `na-spd` decodes the
zero register to `SPEED=10 ASDV=10` (`NicAsde.codex:134-138`), so an arm
that painted off a dead part is not blank -- it reads as a plausible
10 Mb/s line. `STATUS=0` on an arm means the part had already gone quiet
BEFORE the row you are looking at, and the table below is then answering
about the wrong row. The touch row read `STATUS=0x40080080` on 08-11 and
the bed reads `STATUS=130`; a `0` there is the tell. This is the same trap
`CurrentPlan.md` records for every arm flown before 08-10, where SPEED and
ASDV came off a register nothing wrote.

| Last row on the glass | What it means | Next move |
|---|---|---|
| no eligible row | the scan or the BAR, not the NIC | read `verdict=` on the REJECTED row |
| eligible, no touch row | the first MMIO read wedges | the BAR is wrong, not the driver |
| touch, no `ASDE=0 noreset` row | writing MDIO or CTRL wedges, with no reset involved | the PHY kick is the subject, not the reset |
| `ASDE=0 noreset` paints with a NON-ZERO `STATUS=`, `ASDE=1 noreset` does not paint | **the ASDE bit itself wedges this part** | Finding 4 answered without the reset ever running |
| an arm paints but its `STATUS=0` | the part went quiet BEFORE that row, and the row is a null reading | treat the row above it as the last real one |
| both arms paint, `RESET s5` does not | the arms' MDIO and CTRL writes complete and the first write of the RESET does not | the reset sequence, not writing as such |
| `RESET s5` paints, `s6` does not | the IMC write completed and the next read did not | a write kills the block |
| `RESET s7` paints, `s8` does not | **CTRL.RST itself wedges**, which is the folklore | stop pulsing RST on this part |
| `RESET s8` paints, `s9` does not | reads stop completing while polling for RST to clear | the 1M-read loop is the hang; it needs a clock |
| every row paints | nothing wedges any more | read the two arms and compare `SPEED`/`ASDV` |

**Both arms run before any reset, and that is the whole point of this
build.** `na-bring-up-after` is `na-bring-up` with the reset removed and
nothing else changed, and firmware hands this part over with `CTRL.SLU`
already set (`CTRL=0x180240` on the 08-11 flight, SLU being bit 6), so the
comparison the lane needs does not depend on the reset working. 82583V
12349 on bit 5: *"the MAC ignores the speed indicated by the PHY ... This
bit must be set to 0b in the 82583V."*

**If the arms paint and the reset then wedges, B2 is unblocked anyway.** The
reading is banked above the wedge, which is the arrangement the previous two
flights did not have.

**Bed state, stated including what it does not cover.** Re-measured against
the rebuilt image, 2026-08-13. Under codex-vm every row paints, and the two
arms differ as the datasheet describes **without a reset having run**:
`ASDE=0 noreset ... SPEED=1000 ASDV=10 aneg=y STATUS=130` against
`ASDE=1 noreset ... SPEED=10 ASDV=10 aneg=y STATUS=2`. Those are the same
two readings the reset-based arms produced before, which is the useful
control: in this bed the reset was contributing nothing observable. The
staged reset then paints `s5` through `s9` and `RESET done: settled=1 ICR=0
STATUS=0`. Under real OVMF with a USB disk the bank works, `bank live,
ASDE0.TXT written=y`, and its NIC is rejected on the BAR gate.

```powershell
tools/codex-vm.exe -kernel build/boot/asdeflight.img -uefi -gop -headless `
    -e1000 -e1000-phy-link -e1000-asde -screenshot out.bmp -screenshot-delay 2500
build/boot/test-ovmf.ps1 -Img build/boot/asdeflight.img -Out ovmf.png -UsbDisk -Seconds 20
```

**`-headless` is not optional in a shared box.** codex-vm refuses to open a
second on-screen display -- `ERROR: another codex-vm display window is
already running` -- so the windowed form of this command fails outright
whenever any other agent has a VM up, and it fails by writing no screenshot,
which reads exactly like the probe finishing early. Measured 2026-08-13,
where a run of val's cost a re-measurement of the delay figure below before
the cause was found. `-headless` still captures.

**2500, not 3000.** This build finishes sooner than the flown one, because
neither arm pays for a reset any more. Measured 2026-08-13: at 800, 1500 and
2500 ms codex-vm writes the finished screen; at 4000 it writes **no file at
all**, and the run ends `Guest halted with IF=0 after 203879 exits`. The
mechanism behind that halt is NOT established -- `shot-wait` with no medium
falls into `shot-spin`, which is an unbounded recursion rather than a halt
(`GopShot.codex:158-159,183`), so something else is ending the run. The
timing above is measured and the reason for it is not; do not repeat a
mechanism for it from this paragraph.

`-screenshot-delay` has to be under about 5 s: the probe halts with
interrupts off when it is done, and the screenshot timer lives in the run
loop, so a delay past the halt takes no picture at all rather than the
finished screen. The codex-vm bed carries no medium, so it reads `no bank,
mount stage 1 (GPT header read failed)` there, which is the mount-stage
decoder working and not a fault. OVMF has no e1000 attached, so its NIC row
is `REJECTED ... verdict=below-window`: each bed covers one half, and only
the stick covers both at once. **No bed reproduces the wedge**, and none
can: nothing in either datasheet in this tree says what a part does when
the bit is set against instruction, so modelling a hang would be inventing
the cause this flight exists to find.

## FLOWN 2026-08-09: `worksflight.img`. WORKS-8 PASSED on metal; the second shot failed below FAT, in the USB driver.

Shot 1 landed and is correct: `SH171156.BMP`, 2,359,350 bytes, 4609
clusters, 1024x768x24, and it renders. Shot 2 failed with
`s7 m3 c256 p2 w14`. The returned volume answers all four questions
cleanly: every chain matches its file's size, no overlaps, no clusters
allocated to nothing, both FAT copies identical. Nothing needed rolling
back because on `w14` the chain existed only in the in-memory FAT and
`gfat-flush-fat` never ran.

`w14` is `gfat-w-data`: one 32 KB data-phase transfer returned no
completion event within `xhci-fuel` = 1000000 spins
(`GopUsbMsc.codex:319`, `GopXhci.codex:63`). A timeout, not a stall.
**Cause unmeasured**, and two candidates are already excluded by reading
rather than argument: not an oversized transfer (`msc-write-into` chunks
at `msc-chunk` = 64) and not ring wrap (each push waits for its own
completion, so one TRB is outstanding). Routed to reek, whose files those
are, with the discriminator for the next flight: on a data-phase timeout,
reset-recover and retry the chunk once.

**The tools this needed now exist**, and `## Reading the returned stick`
below no longer costs an hour: `build/dump-usb.ps1` freezes the device to
a file, `build/fat16-walk.ps1` answers the four questions off that file.
Run the walker on the PRE-FLIGHT image first. That control caught three
defects in the walker itself and none was visible against the returned
stick alone.

**Evidence:** `build-output/returned-stick.img` on blu, SHA-256
`E0DCC8AF1A113AC3506BA8EC06EEED2570B3F43297F19887B51A29368D23DA85`, read
raw with Windows never mounting the volume.

**What actually flew was NOT `18F3AA32285BFF80`.** That image was built at
04:00 and the depot seed moved under it during the day, so it was rebuilt
from the same recipe immediately before flashing: SHA-256
`9A5705B9C55E23BC...`, 16 MB, PE 683520, seed 2,753,304
(`02DE3DEE074FEAE7`), FAT16 spc=1 clusters=26350. The digest in the
superseded section below is the morning's build and is kept only so the
two are not confused.

### The arm was NOT "press F12 twice", and the sheet said it was

A virgin image lands on the first-boot wizard, so `wizard-run` takes the
fresh path and the operator walks it before any desktop exists: **Enter**,
a passphrase typed twice, a sentence of entropy, **Enter** to skip
upstream, keygen, **Enter**, **Enter** at the interface menu (Graphical UI
is row 0 and already selected), and only then **F12**, **F12**. Nine
interactions where this sheet promised two. Say so for any future stick
that ships without an identity on it, and note that the wizard's identity
save is itself a FAT write that runs before the arm does.

## THE SHOT-2 TIMEOUT ABOVE IS THREE DEFECTS DEEP AND ALL THREE ARE FIXED, 2026-08-09 (reek, main 14447)

blu sent the measurement and explicitly no cause, which was the right call.
The cause was not one thing.

**1. A timeout recovered nothing at all.** `msc-issue` runs recovery only for
`trb-cc-stall`; a timed-out transfer answers 0, so `msc-code-ok` said False and
the command returned with the TRB still outstanding and the device mid-BOT.
blu read this correctly from the source.

**2. The recovery that existed was the WRONG COMMAND for this state and would
have failed on the board too.** `xhci-recover-endpoint` leads with Reset
Endpoint, which xHCI 4.6.8 defines only for a HALTED endpoint. A timeout leaves
the endpoint RUNNING with a TD pending, so the controller answers Context State
Error and recovery refuses. Stop Endpoint is the command for that state, and it
was already in the chapter, used by nothing.

**3. Stop Endpoint's own mandatory event then poisoned the next transfer.**
xHCI 4.6.9 requires a Stopped transfer event before the command completion. The
command wait latches it, being a transfer event for a slot and DCI it was not
waiting for, and the next real transfer takes it out of the latch and reads it
as its own answer: the retried CBW came back `lastcc=27`, Stopped - Length
Invalid. The latch is drained after the stop now.

Defects 2 and 3 were both in code that had never once executed (L-UNCALLED).

**Measured, one probe and one image, only the drop index moving:**

| arm | write | retry cell |
|---|---|---|
| control, no drop | verify=True | 0 |
| drop 30 (write data phase) | **verify=True** | 3, lba 200 |
| drop 34 | **verify=True** | 3, lba 200 |
| drop 26 (bulk read phase) | **verify=True** | 3, lba 128 |

Staged through the fixes the same arm answered retry=1 (recovery refused), then
2 (recovered, re-issue poisoned), then 3. A pass/fail cell would have said
"still broken" three times (L-STATES).

**Two bed capabilities exist now because none of this was reachable before:**
`-usb-bot-drop N` swallows the Nth bulk transfer event, and codex-vm implements
Bulk-Only Mass Storage Reset. Both in `docs/OperatorsManual.md`.

**What is still NOT answered, and it is blu's original question.** Nothing here
says why the ASUS timed out. `xhci-fuel` is a SPIN COUNT, not a duration: the
loop reads three ordinary memory locations, so a million iterations is some
number of milliseconds that varies with the box, and nobody has ever converted
it. Cell 85 now carries the smallest fuel any COMPLETED transfer left behind,
and on the next flight it is the discriminator -- a small `f` says the budget
was marginal, an `f` near 1000000 says the fuel is innocent and the device
stopped answering. **Under codex-vm it reads exactly 1000000: this bed completes
every transfer before the guest spins once, so no bed has ever put a single spin
of pressure on that constant.** The retry makes a shot-2 timeout survivable
either way; it does not explain it.

## SUPERSEDED, kept for the a5flight half: TWO STICKS, TWO BOOTS. `worksflight.img` (`18F3AA32285BFF80`) and `a5flight.img` (`1910F172AAF7E13B`). Both built and bed-verified. Not a proposal.

**Damian directed this stick work on 2026-08-09 and asked for minimum
boots, one if possible.** That lifts the standing "flights are not
proposed for WORKS-8" ruling for this sitting by his direction rather than
by anyone asking.

**This sitting is TWO boots, one per stick, because `BOOTX64.EFI` is one
file and the two arms need different ones.** blu's is GopBoot; reek's A5 is
the compiler itself. There is no arrangement that makes them one boot, and
the second boot is about ninety seconds of Damian's time, not a campaign.

**Both images are built and bed-verified. Neither is a proposal.**

| stick | image | arm | what Damian does |
|---|---|---|---|
| 1 | `worksflight.img` (`18F3AA32285BFF80`) | WORKS-8 FAT write path | boot, press **F12 twice**, pull the stick |
| 2 | ~~`a5flight.img`~~ superseded by `a5flight2.img` (rebuilt 2026-08-11) | A5, the compiler compiles on the box | boot, wait for the screen to go **WHITE**, pull the stick |

Order does not matter. **The B2 ASDE arm is on neither image** (it wedges
the real part deterministically), so nothing here can be lost to it.

**Provenance.** Built 2026-08-09 off blu at main-merged (seed
`A1EBA5A03016A128`):

```powershell
build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex `
    -Kernel seed/Codex.cdx -Ebs -Out build/boot/worksflight.img
```

16 MB, PE 683520, seed 2731952, FAT16 spc=1 clusters=26350.

**It must be a FRESH build and no flown image will do.** The WORKS-8 write
path landed in main 14169 on 2026-08-08 16:56. `ceremonyboot.img`
(`C423418D`) flew 2026-08-05 and `deskboot.img` earlier still, so every
image already on the shelf predates the fix by three days and would
answer a question nobody is asking.

**Bed state before flashing, stated including what was NOT checked.**
The image boots under `codex-vm -uefi -gop`, reaches the payload with no
host crash, and paints a non-blank 640x480 frame. **What has NOT been
verified in the bed is the F12 write itself** -- the arm below is the
first exercise of that path on this image, which is the point of flying
it. Do not read "boots and paints" as "the write path is proven".

### The boot, and the only thing to type

Press **F12 twice**. That is the whole arm. Everything else this sitting
wants is read off the stick afterwards.

The taskbar posts the verdict. **Photograph only if it says FAILED**;
a pass is read off the stick and needs no glass.

### What it answers

WORKS-8 (`apps/works/works-backlog.md`): four defects closed in the bed in
main 14169 and its subdirectory follow-up, **none of which has ever run on
metal** -- a failed write leaking its chain, an allocator bound running
274 entries past the end of the volume, a write-stage instrument that
could not name a failure, and the live-chain collision guard that
descends into subdirectories. Diag **cell 83** is the write stage and
paints `w`; a write failure used to read `s7` (MOUNT) and name nothing.

### Off the returned stick

Read the shot files and cell 83. A pass is a shot file present on the
volume with cell 83 reading `w`.

## FLOWN 2026-08-09, WROTE NOTHING: `a5flight.img`. Do not re-fly it as built.

The A5 stick came back with the volume byte-identical to what was flashed
apart from LBA 0 and 1, which `flash-usb.ps1 -SpecFit` writes itself. The
board wrote nothing, and it could not have: the payload's block I/O is raw
IDE port access and the stick is USB mass storage. Reproduced in the bed by
running `codex-vm -uefi` with no `-disk` (divide by zero in the BPB parse).
`docs/Designs/Done/Compiler/MetalOutputSink.md` has the mechanism and the
fix, which is a UEFI block write helper and is seed-affecting.

**REBUILT AND BED-VERIFIED 2026-08-10 as `a5flight2.img`** (the Stick 2
section below is that image, not this one). `a5flight.img` itself is dead:
do not flash it.

**The arm below is kept because the procedure is right and only the payload
is wrong.** Re-fly it when the payload is rebuilt with `-Uefi` plus that
helper, at which point the compiler's own diagnostics also land on ConOut
and the boot stops being silent.

**Two procedure findings from this flight, both of which cost a trip:**

1. **"Wait for the drive LED" is not a procedure.** The ASUS has no drive
   LED to watch and the payload prints to a UART the board does not have,
   so the operator had no way to know whether the run had finished. The
   answer is not a longer wait: a payload whose result is read off the
   volume must say when it is done, on a channel the box actually has.
   `-Uefi` gives ConOut for free and is the fix here.
2. **An arm read off the volume must be flown on a virgin image** -- see
   the rule two sections down. This flight's stick already carried the
   answer files.

## FLOWN 2026-08-10, WROTE NOTHING: `a5flight2.img`. The `-Uefi` payload did not fix it, and the arm could not say why.

Returned stick diffed against the master over all 16 MB: **exactly two sectors
differ, LBA 0 and LBA 1**, which are the two `flash-usb.ps1 -SpecFit` rewrites.
Root directory holds `EFI`, `SOURCE.SRC`, `CODEX` and nothing else. The same
signature as the 2026-08-09 flight.

The screen showed the stub's dark green and never changed.

**The arm was built wrong and that is the finding worth keeping.** Its only
success signal was `DISK-OUT:` over ConOut, and `__uefi_print` had never
rendered a character on this board. It is compiled into every binary we ship
and, until this payload, was called by nothing on metal (L-UNCALLED). So the
one channel the arm depended on was the one channel nobody had tested, and a
dead payload and a working payload with no output look identical. Green plus
silence eliminated nothing.

`build/boot/blockladder.img` below is the replacement instrument. **That
grounding is LIFTED as of 2026-08-11: the compiler carries the ladder itself
now** (`codex/compiler/Core/BootPaint.codex`), so both A5 images were rebuilt
with a painted payload and a silent return is no longer possible. Fly the
block ladder and the sink ladder first anyway -- they are strictly smaller
tests of the same write path.

## Stick 2, REBUILT 2026-08-11 with the painted payload: `a5flight2.img`, SHA-256 `A90E7DA0 BFFBDBA6 1F37E653 EF384306 056324FB A0EB73BF D3F0F80E 549DB0B0`. A5, the compiler runs on the box.

**Nothing is typed. Boot it, wait for the screen to go WHITE, then pull the
stick.** The colour is the procedure. `DISK-OUT: OK OUT.CDX 84660` appears
beside it if ConOut renders, which it has never been shown to do on this
board; that is why the screen and not the line is what you read. The colour
table is in "THE A5 STICKS NOW PAINT TOO" below, and any colour short of
WHITE names the stage that failed.

A blank screen for a minute is the compile running. `DISK-OUT:` reading
anything other
than `OK` is a finding worth bringing back, not a failed sitting.

### What it answers

**A5: the compiler compiles a program on real hardware and hands back the
artifact.** The compiler boots as `BOOTX64.EFI`, reads `SOURCE.SRC` off the
volume through the GPT, compiles it, and writes `OUT.CDX` and `OUT.TXT`
back to the same volume through the foreword FAT writer. Three blockers
closed to get here (`docs/Designs/Done/Compiler/MetalOutputSink.md`), and
**every one of them has only ever run in the bed**. What metal adds that no
bed can: the writer has never once run on real USB storage at any size.

### Off the returned stick

Two files in the volume root:

- `OUT.TXT` -- one line. A pass reads exactly `OK OUT.CDX 84660`.
- `OUT.CDX` -- 84,660 bytes, and its SHA-256 must be
  `ACF9823E4680986F206ED0B4C619159F800E40D809ED1A8D903DF0BD9ED864D8`.

That hash is the whole point of the arm: it is the SAME source compiled on
the host, so an exact match means the compiler on the box produced a
bit-for-bit identical artifact. A short or absent `OUT.CDX` with a present
`OUT.TXT` means the write path failed partway and is a finding, not a
wasted trip.

**Extract with one `-Name` per invocation.** `pwsh -File` passes every
argument as a literal string, so `-Name OUT.CDX,OUT.TXT` arrives as one
name and answers `MISSING: OUT.CDX,OUT.TXT` -- which reads exactly like a
board that wrote nothing.

The second, independent check, because a matching hash only says the bytes
agree with the host: run the returned artifact. `a5src.codex` sums `i*i`
for `i` in 1..100, so `build/test-run.ps1 -Kernel OUT.CDX` must print
`A5 338350`. That number comes from the arithmetic, not from a previous
run, which is what makes it an oracle rather than a golden.

### Provenance, and how to rebuild it

**Rebuilt 2026-08-11 at main 14644**, kernel `seed/Codex.cdx`
`AF4E14D9703985AC`, so the payload carries the DISK ladder. Bed-verified on a
COPY: all six rungs green, `OUT.CDX` extracted at 84,660 bytes hashing
`ACF9823E...`, equal to the host control; the virgin master answers
`MISSING: OUT.CDX`, which is what makes that a measurement. Master re-checked
virgin afterwards.

**The seed must carry the guarded UEFI block helpers (main 14433) and the
UEFI block path (main 14398)**; an older seed writes over raw IDE ports,
which is what made the first flight return a byte-identical stick.

**The payload cannot be the depot seed.** The seed is built in plain `Exit`
mode. The compiler has to be recompiled in `ExitUefi` mode, which is what
puts reads and writes on `EFI_BLOCK_IO_PROTOCOL` instead of IDE:

```powershell
build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile Codex.codex
build/compile.ps1 -Src Codex.codex -Out a5uefi.cdx -Log a5uefi.log `
    -Kernel seed/Codex.cdx -Uefi -TimeoutSec 1800
build/cdx-to-pe.ps1 -CdxInput a5uefi.cdx -Out a5.efi `
    -EntryStart -HeapPages 32768 -Stdin "DISK`nSOURCE.SRC`n"
build/build-img.ps1 -PeInput a5.efi -Out build/boot/a5flight2.img `
    -Source build/boot/a5src.codex -TotalSectors 32768
```

`-Uefi` is the one flag the last flight lacked, so confirm it moved
something rather than trusting it: the same source built plain is
2,759,023 bytes and built `-Uefi` is 2,743,191.

**Point `-Source` straight at the workspace file. The hand-normalising step
that used to stand here is gone, and so is the defect it worked around**
(main 14789). A CRLF source no longer needs anything done to it: the
compiler's disk reads go through `fat16-read-source`, which drops byte 13
before CCE conversion, exactly as `__bare_metal_read_serial` has always done
for the wire. `codex/test/fat16-source-cr` holds the arm.

**A payload built before 14789 still has the defect**, so an image is only as
tolerant as the compiler inside it. If an old payload stops the ladder at
ORANGE on a source you believe is good, check the payload's date before
anything else.

### A FRESHLY BUILT IMAGE DOES NOT BOOT TO THE DESK. IT BOOTS TO A WIZARD.

**Measured 2026-08-13 in the bed, and it is not written down anywhere else.**
`build/build-boot-img.ps1` produces an image with no `IDENTITY.DAT`, and
`wizard-run` branches on exactly that: no identity means the FRESH path, a
multi-screen first-boot flow, and the desk is on the far side of it. Every
stick flown so far already carried an identity, which is why the long path
has never been described.

An operator who expects the desk and gets a passphrase prompt will read it
as a hang or a wrong image. It is neither.

**Fresh stick, in order.** Each screen ends on Enter:

| Screen | What it wants |
|---|---|
| GopBoot menu | Enter on `Graphical UI` (item 0, already selected) |
| Welcome | Enter |
| Passphrase | 4+ characters, Enter |
| Confirm | the same, Enter |
| Entropy | type a random sentence, Enter |
| Upstream Server | Enter alone skips it |
| *(keygen runs here)* | no input |
| Identity Created | shows the fingerprint, Enter |
| Storage | GPT / BOOTX64.EFI / CODEX.CDX readback, Enter |
| ... then `Press Enter to choose your interface` | |

**The whole flow including ECDSA keygen completed inside 50 s in the bed**,
driven entirely by scripted keys, so it is not a slow step to wait out on
metal -- budget a minute of typing, not a coffee.

**A stick that already has an identity takes the SHORT path**: `Welcome
Back`, one passphrase, `Identity Unlocked`, Enter. Three wrong attempts
leaves the identity locked for that boot and **the stick unchanged** --
it does not erase anything.

**Do not pre-run the wizard in the bed and then flash that image.** It works,
and it puts a private key GENERATED IN THE BED onto the stick. The identity
is the whole point of the trust story; it should be created on the machine
that will carry it. This is a different reason from the contamination rule
below and it bites in a quieter way -- the artifact looks perfect.

`build/read-stick.ps1 -ImageFile <img> -Name IDENTITY.DAT` answers `MISSING`
on a virgin image and prints a 124-byte file on one that has been through the
wizard. **Calibrate it in the same breath** (the rule below): ask it for
`SOURCE.SRC` too, because `MISSING` is also what it says when it cannot read
the volume at all.

### NEVER FLASH AN IMAGE THAT HAS BEEN BOOTED IN THE BED

**This arm was one boot away from being unable to fail, 2026-08-09.** The
image was built, then bed-tested by pointing `codex-vm -disk` AT IT, and
the guest wrote its `OUT.CDX` and `OUT.TXT` into that same file. That
post-run image was then flashed. **The stick therefore already carried the
answer files, with the exact SHA-256 this sheet says to check for**, so a
board that wrote nothing would have returned a perfect pass.

It is not a hypothetical: the stick was written and pulled before the
contamination was caught.

The rule, and it applies to every arm that reads its result off the
volume: **the bed test runs on a COPY, and the master is proven virgin
before it is flashed.** Grepping the image for the answer file's directory
entry takes a second:

```powershell
# must print nothing before this image goes near a stick
$b=[IO.File]::ReadAllBytes('build/boot/a5flight.img')
# search for the 8.3 entry "OUT     CDX"
```

`build/read-stick.ps1 -ImageFile <img> -Name OUT.CDX -OutDir <dir>` is the
supported version of that check and answers `MISSING: OUT.CDX` on a clean
image.

**Calibrate it in the same breath, because `MISSING` is also what it says
when it cannot read the volume at all.** Ask it for `SOURCE.SRC`, which is
on every one of these images: a `complete, 1 clusters` beside the two
`MISSING` lines is what turns them into a measurement. `a5flight2.img` was
cleared this way on 2026-08-10 (`SOURCE.SRC` sha256 `31901D22...`).

The general shape is the one this project keeps paying for: an instrument
that cannot fail. An arm whose evidence is a file that was already present
does not test anything, and it looks exactly like the arm that does.

### Bed-verified 2026-08-10 on a COPY, master untouched

`codex-vm -kernel a5bed.img -uefi -disk a5bed.img -headless`, where
`a5bed.img` is a copy. The guest printed `SIZE:84660` and
`DISK-OUT: OK OUT.CDX 84660`, and the UART took 8 bytes for the whole run,
which is how the sheet knows those lines went out over ConOut rather than
the serial port the board lacks.

Three checks, and the third is the one that is not circular:

| check | result |
|---|---|
| `OUT.CDX` off the bed volume | 84,660 bytes, magic `CDX1`, 166-cluster chain complete |
| against the host compile of the same source | byte-identical, `ACF9823E...` both sides |
| the artifact RUN | prints `A5 338350`, which is the arithmetic's answer |

The master `build/boot/a5flight2.img` still hashed
`B453C906...` after all of it, and its root directory holds `EFI`,
`SOURCE.SRC` and `CODEX` and nothing else. (That is the PRE-REBUILD master.
The image was rebuilt 2026-08-11 with the painted payload and now hashes
`A90E7DA0...`; the three checks above were re-run against it and gave the
same three answers.)

**What none of this reaches**, and it is the reason for the trip: every
byte above moved through codex-vm's `EFI_BLOCK_IO_PROTOCOL` model, on IDE
underneath. The board's is real firmware over real USB mass storage, and
that path has never once carried a write (L-OPTIONAL).

## THE LADDER: `blockladder.img`, SHA-256 `FF5CC67F 3BA48A59 261B7C62 B3D02427 21F2D022 FEA3AED8 5DEFC3DF E0A5551A`. Fly this before either A5 stick.

**Flight 1 of the ladder, 2026-08-10, was GREEN and it was the ladder's own
fault.** That build printed to ConOut BEFORE painting on every rung, so its
first act on the board was a firmware call, and a firmware call that does not
return takes the colour channel with it. Green was therefore the only reachable
answer for any failure at or before the first print, which is the whole span the
instrument existed to divide up. The volume came back with LBA 30000 still
zeroed, so nothing was written, and nothing else can be concluded from it.

The build named above paints first and prints second, which is what having two
channels was supposed to buy. **A green screen from THIS build means what the
table says**; a green screen from `837F79FA...` did not.

### FLIGHT 2, 2026-08-10: WHITE. The UEFI block write path works on this board.

Every rung passed, and the write was confirmed ON THE MEDIUM rather than taken
from the guest's own readback. The stick came back and LBA 30000 reads
`A5 3C 90 43 4F 44 45 58 ...` with the `55AA` signature intact: the boot sector
copy the probe writes, byte 0 replaced by the `0xA5` marker, while LBA 2048
still reads `EB` there. Both previous A5 flights left that sector zeroed, so
this is the discriminator that told those two apart from this one.

So on this board, under firmware and after the kernel installs its own CR3:
`LocateProtocol(EFI_BLOCK_IO)` returns a working interface, `ReadBlocks`
delivers a real sector, and `WriteBlocks` lands bytes that survive a power
cycle. **The CR3-remap hypothesis is dead** -- firmware calls survive the
remap; it had been recorded as unmeasured and it did not hold up.

What this does NOT show: the ladder writes ONE sector through one
`block-write-sector`. The A5 sink writes 2.7 MB through `fat16-write-segments`.
The primitive is proven on metal, that sink's use of it is not.

CYAN is still the one rung never seen to fire. This flight went past it to
white without stopping, so nothing about it changed.

**Read the colour. Nothing is typed and nothing needs reading off the volume.**
It answers in under a second and then holds its colour, so boot it, look, and
power off.

| screen | meaning | where the fault is |
|---|---|---|
| firmware's own screen | never loaded | boot selection or medium |
| solid dark BLUE | died inside the stub | `AllocatePages`, `GetMemoryMap`, `ExitBootServices` |
| solid dark GREEN | stub handed off, payload said nothing | died before its first instruction, or the handoff block is absent |
| **CYAN** | payload alive, framebuffer usable | the SystemTable cell is zero |
| **YELLOW** | SystemTable live | `LocateProtocol(EFI_BLOCK_IO)` found nothing |
| **MAGENTA** | a sector came back off the disk | its bytes-per-sector is not 512 |
| **ORANGE** | the BPB parses | the scratch write or its readback failed |
| **WHITE** | **everything worked** | nothing; the write path is good on this board |

Dark green and dark blue are the stub's. Everything from cyan up is the
payload's, and each colour means the stage NAMED IN THE ROW ABOVE IT passed.

### What makes this different from the two flights that told us nothing

Its channel is the framebuffer, which is the only output already demonstrated
to reach a human on this box: you have been seeing the stub's green all along.

**"It never prints on metal, so nothing depends on ConOut" stood here and was
false.** It printed on every rung, and it printed FIRST, which is strictly worse
than not having the second channel at all: the colour could only appear if the
firmware call ahead of it returned. Ordering is the whole content of the fix.
The payload still prints, because the bed reads those lines to force each rung,
but no paint waits on a print now.

And **every rung has been forced and watched in the bed**, which is the part
the last two arms skipped. `build/ladder-arm.ps1` runs them:

| arm | forced how | last stage painted |
|---|---|---|
| pass | normal | `wrote` (WHITE) |
| nodisk | no `-disk`, so `LocateProtocol` returns NOT_FOUND | `systab` (YELLOW) |
| badbpb | bytes-per-sector zeroed on the `-disk` image only | `read` (MAGENTA) |
| small | 16384-sector volume, so the scratch LBA does not exist | `bpb` (ORANGE) |

The harness compares against those expectations and exits non-zero on any
disagreement; a deliberately wrong expectation was run and it reported
`MISMATCH` and exit 1, so the comparison is not decorative.

Two further defects were caught on 2026-08-10, both of which had been reporting
a stage as passed when it had not:

- **The read rung tested the pointer, not the sector.** `buf /= 0` is true
  whenever a buffer was allocated, so a read that delivered 512 zero bytes
  passed. It now requires the `0x55AA` signature. A payload built WITHOUT
  `-Uefi` reads down the bare-metal path to a controller that is not there and
  gets exactly those zeros: before the fix it painted magenta and marched on,
  after the fix it holds yellow. That control was run.
- **`build-option-a.ps1` could not build a UEFI-mode payload at all** -- it never
  passed `compile.ps1 -Uefi` -- and the failure is silent, because such a payload
  boots and paints normally. It now takes `-Uefi`, and the ladder must be built
  with it.

The harness also refuses to run when either source file is newer than the image.
It had reported a clean four-arm pass against a stale image minutes after a
compile error, which is a green result for source that never compiled.

**CYAN is the one rung never seen to fire.** The stub primes the SystemTable
cell and nothing in the bed can unprime it. Treat a cyan screen as an
uncalibrated reading and tell whoever built the ladder.

Two defects the calibration caught before anyone carried a stick, both of
which would have shown you a colour that meant the opposite of the truth:
the stage painted its colour BEFORE checking whether the stage passed, and
the failure path was a pure non-terminating loop that the optimizer deleted
outright, so a failed stage fell through and kept going.

### Off the returned stick

Optional, and independent of what you saw. The pass arm writes `0xA5` to the
first byte of LBA 30000, which is inside the facts region and read by nothing:

```powershell
$b=[IO.File]::ReadAllBytes('<stick dump>'); $b[30000*512]   # 165 means the board wrote
```

Measured in the bed: 165 on the passing arm, 0 on the master and 0 on an arm
that failed earlier, so it separates a real write from a self-report.

## CORRECTED 2026-08-11: the stick blu found was NOT established to have flown, and blu's evidence for it is DESTROYED.

**blu wrote in this section that an unclaimed stick "flew, and it came back
without its answer file". THAT CLAIM WAS NOT SUPPORTED AND IS WITHDRAWN.**
It is kept as a correction rather than deleted because both the reasoning
error and the evidence loss are worth more than the claim was.

What blu actually observed, on the stick it was about to overwrite for the
sinkladder flight: **`EFI`, `SOURCE.SRC` 2,769,366, `CODEX.CDX` 2,755,007,
`IDENTITY.DAT` 124, and no `OUT.CDX`.** That much is a reading.

**The error.** blu argued the guest must have written `IDENTITY.DAT`
"because no master image in blu's tree carries one", and that the
`SOURCE.SRC` size matched no A5 master. Both checks ran against BLU'S
masters only. reek builds its own images in its own workspace, and reek
reports flashing `a5bigflight.img` onto this very stick on 2026-08-11 after
dumping its prior contents. A survey of one workspace was read as a survey
of all of them -- the same shape as the CCE tier-1 error recorded under the
em-dash rule: an instrument pointed at part of the question, reported as an
answer to the whole of it.

**What is settled, measured 2026-08-11 against files that still exist:**

| image | root directory |
|---|---|
| reek `build-output/stick-before-20260811.img` (`629821CF`) | `EFI`, `CODEX.CDX` 2,755,007, `CMUNSS.TTF`, `IDENTITY.DAT` 124 -- no `SOURCE.SRC` |
| reek `build/boot/a5bigflight.img` (`9E6E35AC`, rebuilt 12:44) | `EFI`, `SOURCE.SRC` 2,779,145 -- no `CODEX.CDX`, no `IDENTITY.DAT` |
| what blu read off the stick (~11:00) | `EFI`, `SOURCE.SRC` 2,769,366, `CODEX.CDX` 2,755,007, `IDENTITY.DAT` 124 |

**THE A5 MASTERS MOVED MID-DAY, which is reek's note and it matters more
than the withdrawn claim did.** The numbers blu compared against are the
masters as they stood BEFORE reek's rebuild -- `a5bigflight.img` held
2,768,194 then and 2,779,145 now, `a5flight2.img` held 246. **Do not re-run
this discriminator against today's images and expect the same numbers.** A
size comparison against a file that is rebuilt during the same session is
not a stable discriminator at all, and that is the second reason the
original claim could not have carried the weight put on it.

So the stick blu read matches NEITHER reek's before-dump NOR reek's current
master, and reek's master was rebuilt after blu's read, so the image reek
actually flashed is not on disk anywhere. **The question cannot be settled
now.** It is not evidence of a flight and it is not evidence against one.

**AND BLU DESTROYED ITS OWN COPY.** `build/build.ps1` line 157 is
`Remove-Item -Recurse -Force build-output` in its `clean` phase. blu
preserved three raw stick images into `build-output/` and then ran the gate
twice, which deleted all three: `returned-stick-20260811.img`,
`sinkladder-returned-20260811.img` and `asdeflight-returned-20260811.img`.
Their SHA-256s were published to main in changes 14629 and 14641 with an
instruction to ask blu for the files. **Do not ask; they are gone.**

### NEVER PRESERVE A RETURNED STICK UNDER `build-output/`

`build-output/` is wiped by the `clean` phase of every single gate run, and
a gate is the most likely next thing anyone does. It is also p4-ignored, so
nothing in the depot notices. Put a returned stick somewhere the build does
not own and record where; `docs/Hardware/HardwareSitting.md` says only "on blu's box"
in older entries, which is what made this look like a safe habit.

This is why reek's `stick-before-20260811.img` survived and blu's three did
not: same directory, but reek had not run a gate since.

**That one is now out of the trap. `D:\Projects\stick-archive\stick-before-20260811.img`**,
copied 2026-08-11 (fester) and verified byte for byte:
`629821CFE8A9F876EC2A9FD9C4C61B473D93B821681DB0145B0E6DFDD2683A03` on both
sides, 16,777,216 bytes. That path is outside every workspace, so no gate's
`clean` owns it and no `sync -f` reaches it. **This is the only surviving
copy of the ceremony boot that flew green on 2026-08-05** -- see that entry
below -- and specifically of the 124-byte `IDENTITY.DAT` the guest wrote to
the ESP on real ASMedia hardware, which is not reproducible from source.
reek's copy still exists and is still doomed; quote the `stick-archive` one
from here on.

Swept the fleet's `build-output/` directories the same day for anything else
in this position: eight other images of 16 MB, all BUILT rather than
returned (`desk-*`, `mscalign-*`, `era-ceremony`, across red, val-main and
fester-main), so all reproducible and none rescued. **The distinction that
matters is returned-or-built, not size**: a built image is a recipe away, a
returned one is evidence and there is exactly one of it.

## FLOWN 2026-08-11, ORANGE: `sinkladder.img` reached `fat16-write-segments` and nothing came back. reek's to read.

Flashed by blu, flown by Damian. **The screen held ORANGE and the last two
lines were `SINKLADDER bpb ok=1 painted fb=1` and `SINKLADDER bpb
continuing`.**

**Read the rungs in the order the code runs them, because ORANGE is the bpb
rung's PASS colour and not a fault colour.** `ladder-pass ok colour prev`
paints `colour` when ok is 1, so `bpb ok=1` painted orange BECAUSE the BPB
parsed, and `ladder-hold-if` holds only when ok is 0. `bpb continuing`
therefore means it did not hold: it entered `sl-write-and-verify`. The
screen then stays orange through the 2.7 MB `__heap-advance`, through
`sl-fill` poking 2,745,998 bytes one at a time, and through
`fat16-write-segments`, and only changes when the `wrote` rung paints BLUE.

**The returned stick settles what the glass could not.** Root directory:
`EFI`, `CODEX.CDX`, `CMUNSS.TTF` and nothing else -- byte-for-byte the
master's own listing, no `BIG.CDX` at all. So the run did not stall partway
through a chain; **no directory entry was ever created**, which puts the
fault at or before the first allocation rather than in the chain walk.
**The raw image of this returned stick is DESTROYED.** It was preserved to
`build-output/sinkladder-returned-20260811.img`, SHA-256 `9FBD844E
AAEE618F 98118CE1 4BEC8E3C BFCA9AC5 035821C2 3C21AF15 26890307`, and the
`clean` phase of a later gate run deleted it (see the correction section at
the top of this file). **The finding above survives because it was written
down: the root listing and the absence of `BIG.CDX` are recorded readings,
not inferences from a file.** The bytes behind them are not recoverable.

**What the flight did NOT establish, stated because the glass alone cannot.**
Whether it hung or faulted, and whether the operator's wait exceeded the
write's duration. The run sheet's own advice is several minutes, about five
in the bed under `-uefi`, and USB is slower. `ladder-pass`'s comment says
the printing exists so a held colour is no longer "indistinguishable from a
run still in progress" -- and it half-achieves that: the text names the
stage that passed, and nothing reports progress INSIDE the stage that
follows. A heartbeat inside `sl-fill` or between write segments is what
would separate slow from dead on the next flight. That is reek's call.

**THE HEARTBEAT IS BUILT AND BED-VERIFIED, 2026-08-15 (reek). QUEUED, NOT
PROPOSED.** `ladder-tick` paints a bar in a 24-row band BELOW the field, so the
field still carries the last-colour-standing contract and only the band moves.
The bar is BLUE: blue is the rung the stage is working toward, so a bar
advancing under an ORANGE field is progress toward `wrote`, and a bar frozen
part way is where it stopped.

What the operator reads, and it is the distinction this flight could not make:

| glass | means |
|---|---|
| orange field, blue bar advancing | the 2.7 MB fill is running |
| orange field, blue bar full | the fill finished, the write has not returned |
| orange field, blue bar EMPTY after having been full | inside `fat16-write-segments`, which is where the returned stick put the fault |
| blue field | the write returned and the `wrote` rung passed |

Measured in the bed at 640x480, one run to 150 s: 42 fill ticks for the
2,745,998-byte payload at a 64 KB chunk, `bar=15` at the first and `bar=640` at
the last, monotone in between, then `write entering bar=0` and the run still
inside the write when it was killed. That last state is exactly the one the
flight held ORANGE in and could not report.

**The pixel figures are the bed's panel width and it has already moved.** As of
main 15469 the stub picks the largest enumerated mode, so a plain option-a
image comes up 1024x768 and the same run reads `bar=24` first and `bar=1024`
last. What transfers is the FRACTION and the tick count: 42 chunks, the first
tick at one 42nd of the width, the last at the full width. Compare those, not
the pixels, and on the ASUS the width is the panel's own.

The arithmetic is tested away from the glass
(`codex/test/apps/ladder-bar-width`), because a bar that saturates early or
wraps a 32-bit multiply needs no framebuffer to catch: at this payload
`w * done` reaches 4,393,596,800 and a wrapped result reads as the bar jumping
backwards mid-fill.

**Reporting from INSIDE the chain walk is not done and needs a hook in
`GopFat16`**, which is a different file and a different lane's. The bracket
around the write is what exists.

**WHICH BYTES ARE QUEUED, and why a rebuild is not the same card.** The image
in the depot is `EC071C2C89B6E985...`, main 15426, and it is the one whose
EXACT bytes ran the full pass arm to `verified` and WHITE. Its stub was built
after red's heap backoff (15393) and BEFORE red 15469 and fester 15503, so it
is not the stub the tree emits today.

That cuts one way and it is worth being plain about it. **Flying this file as
it stands is rehearsed.** Rebuilding it is not: as of those two changes every
UEFI stub path emits bytes that have never flown, `-EntryStart` included, and
three PE shapes gained a 512-byte section. A rebuild therefore needs its own
full-mission bed run and the stub marked NEW on the flight card (L-REHEARSE),
and the bar figures move as well, because the panel is 1024 wide now.

Check before flashing: `p4 print -q -o <tmp> //Codex/main/build/boot/sinkladder.img`
and hash it. If it is not `EC071C2C89B6E985...`, somebody rebuilt it and this
paragraph is describing a file you do not have.

**`print-line-uni` rendered on this box.** The chapter's own prose calls it
"the channel that has never rendered a character on this box and cost two A5
flights", and two of its lines were read off this flight. Recorded as an
observation rather than a measurement: nobody has confirmed whether those
lines were read from the box's glass or from elsewhere, and that question is
worth one sentence on the next sitting.

## SUPERSEDED by the flight above, kept for the recipe and the arm table: `sinkladder.img`, SHA-256 `34A6BC00 2DE3EA8D D45AE00F 144FD0B3 0762A235 A585C18A A17896D9 8430735B`.

The block ladder proves ONE `block-write-sector`. The A5 sink writes 2.7 MB
through `fat16-write-segments`, which allocates a 5364-cluster chain, rewrites
both FAT copies and walks the chain on top of that primitive. **None of that
has run on the board.** This is the instrument for it: the same write and the
same streaming oracle as `codex/test/apps/fat-sink-big.codex`, which asks this
question and answers it over `print-line-uni` -- the channel that has never
rendered a character on this box and cost two A5 flights.

Paint precedes print on every rung, so a rung that dies inside a firmware call
still leaves its colour standing.

**Read the colour.** It writes 2.7 MB, so give it several minutes before
calling it hung; the bed takes about five under `-uefi` and USB is slower.

| screen | meaning | where the fault is |
|---|---|---|
| solid dark GREEN | stub handed off, payload said nothing | died before its first instruction |
| **CYAN** | payload alive, framebuffer usable | the SystemTable cell is zero |
| **YELLOW** | SystemTable live | `LocateProtocol(EFI_BLOCK_IO)` found nothing |
| **MAGENTA** | a sector came back off the disk | its bytes-per-sector is not 512 |
| **ORANGE** | the BPB parses | `fat16-write-segments` did not land 2,745,998 bytes on a 5364-cluster chain |
| **BLUE** | **the 2.7 MB write landed** | the streaming verify found bytes that differ |
| **WHITE** | **everything worked** | nothing; the sink's own write is good on this board |

Each colour means the stage NAMED IN THE ROW ABOVE IT passed. Green is
deliberately not a rung colour: the stub holds dark green, so a green screen
already means the payload never started.

**Every rung below WHITE is forced and watched in the bed** by
`build/sink-arm.ps1`:

| arm | forced how | last stage painted |
|---|---|---|
| pass | normal | `verified` (WHITE) |
| shift | payload rebuilt with `sl-shift = 1`, so the oracle must reject every byte | `wrote` (BLUE) |
| nodisk | no `-disk`, so `LocateProtocol` returns NOT_FOUND | `systab` (YELLOW) |
| badbpb | bytes-per-sector zeroed on the `-disk` image only | `read` (MAGENTA) |
| small | 9216-sector volume: 5014 clusters against the 5364 the payload needs | `bpb` (ORANGE) |

The `shift` arm is the one that makes `bad=0` a measurement. It writes
identically -- same size, same chain 5364, same 84,840 bytes of arena -- and
only the oracle changes, so a WHITE screen means the bytes on the medium were
checked and matched, not that the check ran and could not fail (L-FALSIF).

**`small` was wrong in the direction that passes, and it is worth knowing
why.** The first version copied the block ladder's `-TotalSectors 16384`, but
that arm fails there because LBA 30000 does not exist in an 8 MB image, which
has nothing to do with free space: 8 MB still leaves ~6 MB free, the 2.7 MB
write succeeded and the arm painted WHITE. The harness now reads the cluster
count out of `build-img`'s own output and `sl-size` out of the chapter, and
REFUSES the arm rather than running it when the volume could hold the payload.

**The staleness guard watched the wrong files and printed two false greens.**
Found by fester 2026-08-16, fixed the same day. The arm never compiles; it runs
the prebuilt `build/boot/sinkladder.img`, and the guard that stops it
calibrating a stale payload named `SinkLadderProbe.codex` and
`MetalLadder.codex` only. **The function this arm exists to exercise is
`fat16-next-cluster`, which is in neither.** fester sabotaged
`fat16-cluster-ok` to `cluster <= 100` against a 5,364-cluster chain, which must
truncate, and `-Only pass` came back `verified` anyway: their rebuild had died
on Access denied (`sinkladder.img` is a depot file and had not been opened for
edit), the old image stayed in place, and no watched source had changed. A
failed build and a passing arm were indistinguishable in that output.

Two changes, and they answer different halves:

- **The watched list is now the payload's transitive CITE CLOSURE**, walked
  from `SinkLadderProbe.codex` at run time -- 11 chapters, `Fat16` among them.
  A hand-written list goes stale the moment the payload's reach changes; a
  derived one cannot. The arm refuses outright if the closure comes back
  without `Fat16`, because a walk that lost the chapter under test is a broken
  instrument rather than a clean run. Note it is deliberately NOT the whole
  tree, which is what `build/desk.ps1` stats: the desk binary really does reach
  all of it, this payload is one chapter and its cites, and an arm that cries
  stale every time somebody touches an unrelated pane gets its guard commented
  out.
- **The verdict now carries the bytes that produced it**: the run prints the
  image's hash and mtime, the seed's hash, and the closure size, above the arm
  table and again beside it. No mtime comparison can see a rebuild that never
  wrote, so the reader gets the identity to check instead.

`seed/Codex.cdx` is knowingly absent from the mtime inputs and its hash is
printed for that reason. This client is `nomodtime`, so a sync stamps the seed
whether or not a byte moved, and every merge-down would refuse the arm for an
identical compiler.

Falsified both ways before shipping: with the tree untouched the guard passes
and reports `img EC071C2C89B6E985`, the queued card; with `Fat16.codex` touched
it refuses and names it.

CYAN is still the one rung never seen to fire, for the same reason as on the
block ladder: the stub primes the SystemTable cell and nothing in the bed can
unprime it.

### Off the returned stick

`BIG.CDX`, 2,745,998 bytes, each byte `i` equal to `(i * 7 + i / 513) mod 251`.
Verifying it off the stick repeats on the host what the payload claims to have
checked in the guest, which is the point: the guest's own readback is the
writer grading itself.

```powershell
$b=[IO.File]::ReadAllBytes('<BIG.CDX off the stick>')
$bad=0; for($i=0;$i -lt $b.Length;$i++){ if($b[$i] -ne ((($i*7 + [int][Math]::Floor($i/513)) % 251))){$bad++} }
"$($b.Length) bytes, $bad bad"
```

## FLOWN 2026-08-11, WROTE NOTHING, AND THE LADDER DID NOT FIRE: `a5bigflight.img`. The rung is too late, and the section below overstated what it buys.

Third A5 flight, third identical result. **Screen held the stub's dark green for
the full twenty minutes. Returned stick diffed against the master over all
16 MB: exactly two sectors differ, LBA 0 and LBA 1, which are `flash-usb.ps1
-SpecFit`'s own rewrites. Root directory holds `EFI`, `SOURCE.SRC` and
`CODEX`, and NO `OUT.CDX`.** The board wrote nothing and painted nothing.

**So the payload dies between the stub's jump and the first line of `opening`,
which is upstream of the first rung.** That is a defect in the instrument: the
ladder below reports stages of `opening`, and nothing in it can see a payload
that never reaches `opening`. Green is the only thing it could ever have said
about this failure, which is exactly what the pre-ladder arm said.

What the bed shows about the difference, measured the same day
(`build/boot/diag/EntryProbe.codex`, one rung and nothing else):

| build | screen | result |
|---|---|---|
| no `-EntryStart`, heap 512 | CYAN | runs, paints, exits clean |
| `-EntryStart`, heap 512 | none | halts `IF=0` at RIP `0x1071e8` before `opening` |
| `-EntryStart`, heap 32768 (the A5 flags) | none | same |

`-EntryStart` is the variable and the heap size is not. **Every payload that
has ever painted on this board is built WITHOUT it** (`build-option-a.ps1`
never passes it), and **every silent A5 flight has had it.**

### What `-EntryStart` actually does, and why it cannot work on this box

The paragraph that used to sit here said `block-read-sector` needs the
bare-metal runtime init the flag turns on, called the pattern a correlation
rather than a diagnosis, and grounded both A5 sticks. **The first clause was
stale and the grounding was the wrong conclusion.** `-Uefi` compile mode (CL
14398) moved block I/O onto firmware helpers, which is AFTER the 2026-08-08
measurement the flag's own comment block cites.

`-EntryStart` enters at `__start`, and `__start` is not an init. Read it at
`X86_64Chapter.codex:364-444`: `cli`, reload RSP from `ram-size-addr`, point
the deck at `bare-metal-heap-base`, build a PML4 and load CR3, load its own
IDT and TSS, remap the PIC, **`emit-wait-for-tick` (line 434)**, then
`emit-ata-init` (437), `emit-smp-init`, `emit-nic-init`. That is a bare-metal
hardware takeover executed while UEFI boot services are still live and while
the payload still needs those services for every disk read and every ConOut
write. codex-vm emulates exactly that legacy hardware, so it passes in the
bed. This box does not, so the tick never arrives. **A twenty-minute hold on
the stub's own dark green is a spin loop, not a crash**, which is what all
three flights were.

**Why nobody saw it: the stub's entire progress and failure channel is a wire
this box does not have.** `Mark`/`AllocPanic` (`cdx-to-pe.ps1:205-227`) write
one letter to COM1 and COM2 and halt. There is no serial port on the ASUS. The
stub does paint, but only two states: dark blue if it dies inside itself, dark
green once control passes to the guest (`cdx-to-pe.ps1:239-243`). **All three
flights were green, so every allocation succeeded and the stub handed off
cleanly** -- which rules the stub out entirely and puts the fault in `__start`.

**`-HeapPages` is inert under `-EntryStart`,** because `__start` discards the
firmware's allocation two instructions in. That is why the bed matrix above
found heap size not to be the variable: it was not being used. Drop the flag
and the number starts mattering: at the old 32768 pages (128 MB) the
self-compile dies OUT OF MEMORY with the deck past the stack top, and at
393216 (1.5 GB) it faults in `compile-type-check` having reached 1,567 MB.

### Measured 2026-08-11: the payload runs with no `-EntryStart` at all

`-Uefi`, **no `-EntryStart`**, `-HeapPages 655360` (2.5 GB), the full
2,779,145-byte concatenated compiler source. All six rungs, WHITE,
`DISK-OUT: OK OUT.CDX 2759023`. `OUT.CDX` walked back out of the image over
its 5,389-cluster chain is **byte-identical to the host compile**, SHA-256
`9E823495 EB7AF7A2 3EBD300D B8BCEE83 1ABE9D23 9F730A52 6380E18B E40B7F9A`.

No compiler change, no build-script change, no seed. Only the flags moved.

**The honest gap: the bed still does not reproduce the board's failure.**
`-EntryStart` works in codex-vm. The mechanism above is read out of the source,
not observed on the board. What IS measured is that the payload flown on
2026-08-11 needs none of it and produces the right bytes without it.

## MEASURED ON METAL 2026-08-13: four facts, from the first flight that printed numbers.

The DIAG build (`disk-diag`, stamp in `opening.codex`) prints integers only,
immediately after the volume rung, and every value below is read off the ASUS.

1. **The board runs exactly what is flashed.** The stamp printed. Three earlier
   sittings had produced identical screens across three different builds, and
   "the board is running something else" was a live hypothesis; it is dead.
2. **`scope 0 net 0`.** The proc-0 scope cells are clean, so the scope gate was
   NOT the cause. It was fixed in the stub the same day on the strength of a
   sabotage arm that proved a dirty cell is SUFFICIENT to produce the symptom.
   Sufficient is not necessary, and the board said so. The stub fix stays --
   the cells genuinely were uninitialised without `-EntryStart` -- but it bought
   nothing here.
3. **`ring w 32 r 16`, against `w 16 r 16` in every bed.** `emit-read-line-helper`
   polls COM2's LSR at 0x2FD and `emit-uart-poll-drain` polls COM1's at 0x3FD
   before every character. **A machine with no 16550 floats those ports to
   0xFF, so bit 0 reads as "data ready" forever** and the drain appends one
   phantom byte per character read: 16 characters consumed, 16 phantoms, 32.
   Bounded (one byte per call, no loop) and harmless here because they land past
   the read position, but it is a real defect on any board without a UART and
   codex-vm cannot express it -- its UART honestly reports empty.
4. **`read-line`'s SECOND result cannot be dispatched on.** `DIAG path len`
   never printed and neither did its `None` arm, so the failure is the `when`
   itself, not `text-length`. The first call's value is sound (the mode line
   reaches `dispatch-on-mode` and selects DISK) and the ring contents are sound
   (`r 16`, both lines consumed). The bytes are right and the value built from
   them is wrong. Not yet diagnosed.

**With the path replaced by the literal `SOURCE.SRC`, `fat16-resolve-path` still
answered None on metal.** So the refusal is in the scan, with the scope proven
open. The volume is the live suspect and is unmeasured: `fat16-boot-volume`
takes the first usable GPT partition and the ladder only ever checked
bytes-per-sector, which any real FAT volume on the machine passes -- including
one on a different disk, which would mount cleanly and hold no `SOURCE.SRC`.
`disk-diag-vol` prints the geometry to settle it; our own image is
`part 2048 bps 512 spc 1 / fat 2049 root 2257 ents 512 / data 2289 total 26591`.

**An instrument defect that destroyed evidence, and it was mine.**
`disk-no-source` printed its reason and then `disk-rung` repainted the WHOLE
SCREEN magenta and held, erasing the text milliseconds after it appeared -- on
a board whose only log is the glass. The operator saw "text, then solid
magenta" and the diagnosis was gone. Failure holds now paint a band along the
bottom (`bp-hold-band`), so a printed reason survives the colour that follows
it. Generalises: on this board, never repaint full-screen after printing.

## CONFIRMED ON METAL 2026-08-13: `-EntryStart` spins forever on this board.

`a5bigflight.img` (which carries the flag) was flown and held the stub's dark
green for over twenty minutes, never painting CYAN, and the returned stick had
no `OUT.CDX`. That is the `emit-wait-for-tick` spin the section above predicted
from the source, now observed. **The prediction was made 2026-08-11 and the
flight that tested it was flown by mistake**: reek flashed a `-EntryStart`
image over a returned no-`-EntryStart` stick that was stalling at MAGENTA, so
the same trip also cost the magenta datum. Every A5 image must be built
without the flag; `build/boot/a5flight.img`, `a5flight2.img` and
`a5bigflight.img` all carry it and all three are dead.

## DIAGNOSED AND FIXED 2026-08-13: every MAGENTA stall was the WRONG VOLUME. Fly `a5fix.img`, SHA-256 `4FA6CB66 8F5C3313 EA757719 C5912023 B27277FC 719A86CC D4AA147B 3BDB0A41`.

**The mechanism.** The `-Uefi` block helpers bound their device with
`LocateProtocol`, which returns the FIRST Block I/O handle in the firmware's
handle database, and the UEFI spec leaves that order unspecified. Every bed
has exactly one disk, so no bed could ever present a wrong first handle
(L-GAP); a real machine presents raw disks AND per-partition handles for
everything attached, in whatever order its firmware built them. A foreign FAT
volume mounts cleanly (`bps == 512` is the only thing the volume rung checks)
and holds no `SOURCE.SRC`, which is exactly the metal picture: volume rung
green, literal path unresolvable, scope clean.

**The fix (CL 14694), three layers, each with a fallback to the old path:**
the stub stashes the firmware's ImageHandle at cell 30712 beside the
SystemTable; both UEFI block helpers bind
`ImageHandle -> LoadedImage -> DeviceHandle -> Block I/O` (the device the
image BOOTED from, mandatory protocols only) and fall back to
`LocateProtocol` when the cell is zero or any link refuses;
`fat16-boot-volume` probes LBA 0 as a BPB before the 2048 fallback, because a
DeviceHandle is normally a PARTITION handle, through which LBA 0 is the
volume's own boot sector and the GPT read fails. The probe reads bytes 11/13
raw before letting `fat16-init` near the sector -- `fat16-parse-bpb` divides
by both, and a protective MBR holds zeroes there (the `disk-arm` nosource arm
hung on exactly that divide until the probe went in). The DISK path's mount
is a ladder judged by resolving `SOURCE.SRC`, not by the BPB alone.

**Bed evidence, all on seed `CE8246EB` + this CL's source:**

| arm | result |
|---|---|
| `disk-arm.ps1`, all six arms, codex-vm | calibrated: pass wrote, every forced failure painted its stage |
| OVMF (real UEFI), USB stick only | **end to end**: LoadedImage gave the PARTITION handle, GPT read failed, LBA-0 probe mounted `part 0`, source found, compiled, `DISK-OUT: OK OUT.CDX 84593`, artifact **byte-identical to the host compile** (`9DBC101E...`) |
| OVMF, decoy FAT disk seated at SATA index 0 | identical green run -- the wrong-disk topology cannot reach the fix |
| codex-vm, full 2.8 MB self-compile at `-HeapPages 655360` | `OK OUT.CDX 2780236`, **byte-identical to the host control** (`4AA809C4...`), 5,431-cluster chain complete |
| OVMF, OLD binding (stub without the cell) + decoy | vacuous: OVMF happened to order the stick's raw disk first and it passed, so **no bed reproduces the ASUS's ordering**; the metal evidence stands alone, and the fix does not depend on ordering at all |

Two things the OVMF arms surfaced beyond the volume: `__uefi_print` DOES
render on real firmware (every DIAG line above came off OVMF's ConOut), and
128 MB is no longer enough heap for any real compile without `-EntryStart`
(`OUT OF MEMORY` after the source resolve; the bed had been hiding it because
`-EntryStart` payloads ignore `-HeapPages` entirely, L-ARENA). The flight
image carries 2.5 GB.

**The flight card, one boot:**

- Image: `build/boot/a5fix.img` (in the depot with CL 14694). Payload is the
  compiler `-Uefi` on seed `CE8246EB`, no `-EntryStart`, `-HeapPages 655360`,
  `SOURCE.SRC` = the LF 2,800,253-byte concatenated compiler source.
- Colours are the `a5noentry` table below, plus: MAGENTA with a growing WHITE
  bar is the source read (5,469 firmware calls over USB -- minutes), a CYAN
  bar is the CCE conversion, ORANGE is the compile and holds blind for up to
  40 minutes, WHITE is done -- pull the stick. A failure now prints its
  reason and holds a band along the bottom instead of repainting the screen,
  and the DIAG lines (`vol part/fat/root/data`, `DISK-SOURCE resolving`) name
  the mounted volume either way: `part 0` means the LoadedImage partition
  binding, `part 2048` means the raw-disk walk, anything else convicts the
  mount on the glass.

## SUPERSEDED, do not fly: `a5paint.img` (`89A82514...`) and `a5noentry.img` (`9E3ED8B3...`). Both payloads bind their disk with bare `LocateProtocol` and carry the wrong-volume defect above. The colour table and heartbeat-bar design below are current; the images are not.

## The a5paint recipe (superseded by a5fix.img; kept for the bar design and the bed recipe)

On the post-14789 seed (`570B8B94B730ADD5`), so it carries fester's CR fix.
No `-EntryStart`, `-HeapPages 655360`.

```powershell
build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile Codex2.codex
build/compile.ps1 -Src Codex2.codex -Out a5paint.cdx -Log a5paint.log `
    -Kernel seed/Codex.cdx -Uefi -TimeoutSec 1800
build/cdx-to-pe.ps1 -CdxInput a5paint.cdx -Out a5paint.efi `
    -HeapPages 655360 -Stdin "DISK`nSOURCE.SRC`n"          # NO -EntryStart
build/build-img.ps1 -PeInput a5paint.efi -Out build/boot/a5paint.img `
    -Source Codex2.codex -TotalSectors 32768
```

Guest self-compile is byte-identical to the host control: both 2,761,987
bytes, SHA-256 `95CF4446 5243FBAB 121EC09F 481B3AE9 37F2AB48 373132A9
0AA83EDE 489D0448`, `OUT.TXT` reading `OK OUT.CDX 2761987`, chain 5,395
clusters complete. The control is `compile.ps1` on the same source with the
same seed.

**The bar was watched firing, which is the only reason it is worth flying.**
At a 500 ms screenshot the field is MAGENTA with a white read bar at y=0..11
spanning 159 of 160 sampled columns and a cyan convert bar at y=12..23
spanning 140 of 160; by 800 ms the screen is ORANGE and both bars are gone.
The bed's disk is in memory, so the whole read and conversion finish inside
1.5 s there and a later capture cannot see them -- on USB the same work is
5,429 firmware calls.

**What the operator now reads.** MAGENTA with a growing white bar is the source
read; MAGENTA with a growing cyan bar is the CCE conversion; MAGENTA with a
STATIONARY bar is a stall, and which bar it is says which phase. MAGENTA with
no bar at all is one of the two silent fatals in `disk-no-source`, which still
paint the same colour and still swallow their reason.

## The a5noentry arm (superseded by a5fix.img; its flight stalled MAGENTA, which is the wrong-volume defect above; its colour table is current)

Disk 2 (` USB DISK 2.0`, 28.9 GB), full 16 MB readback verified. Identical
payload and source to `a5bigflight.img`; **the only changes are the two flags**,
and both are forced by the section above.

```powershell
build/cdx-to-pe.ps1 -CdxInput a5uefi.cdx -Out a5noentry.efi `
    -HeapPages 655360 -Stdin "DISK`nSOURCE.SRC`n"          # NO -EntryStart
build/build-img.ps1 -PeInput a5noentry.efi -Out a5noentry.img `
    -Source <an LF copy of the concatenated compiler source> -TotalSectors 32768
```

`a5uefi.cdx` is the compiler compiled `-Uefi`. **The source copy must be LF**:
the depot file is stored `unicode+C` with client `LineEnd: local`, so every
Windows sync writes CRLF and the payload dies `CDX1000` partway in.

The flashed image carries `EFI/BOOT/BOOTX64.EFI` and `SOURCE.SRC` and **no
`OUT.CDX`**, so the file's presence on the returned stick is evidence rather
than residue.

| screen | meaning |
|---|---|
| unchanged | never loaded |
| **DARK BLUE** | the 2.5 GB `AllocatePages` below the 3 GB ceiling failed. Lower `-HeapPages`, reflash |
| **DARK GREEN** | died before its first instruction, as the three `-EntryStart` flights did |
| CYAN | reached `opening`, no mode line off the prefilled ring |
| YELLOW | block I/O or the BPB failed |
| MAGENTA | volume mounted, `SOURCE.SRC` would not read |
| **ORANGE** | **read the source and is compiling** |
| BLUE | compiled, `OUT.CDX` did not land |
| **WHITE** | done, the artifact is on the medium |

**ORANGE is not a stall and this is the thing to tell whoever is at the board.**
The screen holds it for the entire compile with nothing changing -- 14 minutes
in the bed at this heap size, longer over USB. That blind hold is what made the
sink-ladder flight get pulled early and lose its answer. Give it 40 minutes
before calling it hung. WHITE never exits; the payload repaints forever, so
pull the stick once it turns.

**Two failure colours, two opposite fixes**, which is the whole point of
flying it this way: DARK BLUE means the heap ask was too big for what the
firmware could find below 3 GB, ORANGE-forever means it was too small for the
compile.

## The stick sections' colour table, 2026-08-11 (reek). Correct as far as it goes, and it does not go far enough: see the flight above.

The compiler carries the ladder itself now, in `codex/compiler/Core/BootPaint.codex`,
so a metal compile reports as a colour rather than only as a line on a channel
that has never rendered a character on this board. **This is what grounded the
two A5 sticks and it is no longer a reason to keep them grounded.**

**Read the colour. `DISK-OUT:` is a bonus if ConOut happens to work.**

| screen | meaning | where the fault is |
|---|---|---|
| solid dark GREEN | stub handed off, payload said nothing | died before its first instruction |
| **CYAN** | `opening` reached, framebuffer usable | the stub's serial prefill gave no mode line |
| **YELLOW** | stdin said `DISK` | `LocateProtocol(EFI_BLOCK_IO)` found nothing, or the BPB is not 512 bytes per sector |
| **MAGENTA** | the boot volume mounted | `SOURCE.SRC` is not on the volume, or would not read |
| **ORANGE** | the source read off the volume | it did not compile; expect `CODEGEN-ERRORS` beside it |
| **BLUE** | the compile finished | `OUT.CDX` is not on the volume at the size the compile produced |
| **WHITE** | **everything worked** | nothing; the artifact is on the medium |

Each colour means the stage NAMED IN THE ROW ABOVE IT passed. WHITE is a
re-read of the directory, not the writer's own answer, so a short or absent
`OUT.CDX` cannot paint it.

**Every rung is forced and watched in the bed** by `build\disk-arm.ps1`:

| arm | forced how | last stage passed |
|---|---|---|
| pass | normal | `wrote` (WHITE) |
| nomode | payload built with no `cdx-to-pe -Stdin`, so `read-line` answers None | `entered` (CYAN) |
| badbpb | bytes-per-sector zeroed on the `-disk` image only | `mode` (YELLOW) |
| nosource | image built with no `-Source` | `volume` (MAGENTA) |
| badsource | `SOURCE.SRC` naming an undefined function | `source` (ORANGE) |
| nowrite | every free cluster marked bad in both FAT copies | `compiled` (BLUE) |

Unlike the two ladders above, **CYAN's failure IS forced here**: a payload
built without `-Stdin` reaches `opening`, gets nothing from the serial ring
and holds CYAN. The stub cannot unprime the SystemTable cell, but it can be
told not to prime the input ring.

**A read-only image does NOT force `nowrite`, and that is worth knowing before
anyone reaches for it.** codex-vm serves the guest from an in-memory image and
only flushes to the host file, so 833 `WARN: cannot reopen disk ... for write`
lines went past while the guest wrote, re-read and verified `OUT.CDX` at
84,561 bytes and painted WHITE. The medium the guest sees was never read-only.
Filling the FAT is what actually fails the allocator.

## Stick 3, REBUILT 2026-08-11 with the painted payload: `a5bigflight.img`, SHA-256 `9E6E35AC 101CFC7A AAD1CCEC 6EFD2C04 565738F6 ABC43D5D BB1D1408 07E115FF`. The compiler compiles ITSELF on the box.

**Same procedure as stick 2, longer wait.** Boot it, wait for the screen to
go WHITE, pull the stick. `DISK-OUT: OK OUT.CDX 2759023` appears beside it if
ConOut renders. The 2026-08-10 build took **4.7 minutes in the bed** and this
one finished inside a 600-second deadline (the payload holds WHITE and never
exits, so the run was stopped rather than timed). Give it twenty before
calling it hung; USB is slower than the IDE the bed runs on. Any colour short of
WHITE names the stage that failed, and BLUE specifically means the compile
finished and the artifact did not land -- which is the answer this stick
exists to distinguish from a dead machine.

Identical payload to stick 2. The only difference is `SOURCE.SRC`: 2,779,145
bytes of concatenated compiler source instead of a 246-byte program.

### What it answers, and it is the whole of A5

Stick 2 asks whether the sink can write an artifact on real hardware. This
one asks whether **the compiler can reproduce itself there**, which is the
claim the project is actually built on.

### Off the returned stick

- `OUT.TXT` -- one line, exactly `OK OUT.CDX 2759023`.
- `OUT.CDX` -- 2,759,023 bytes, SHA-256
  `9E823495EB7AF7A23EBD300DB8BCEE831ABE9D239F730A526380E18BE40B7F9A`.

**That hash is not just the host's answer. It is the compiler itself.** The
plain-mode compiler built from this same source hashes `9E823495...`, and
compiling this source with it reproduces `9E823495...` again (measured, both
2,759,023 bytes), so the artifact the board hands back is a bit-for-bit copy
of a compiler that is a fixed point of itself. A board that returns those
exact bytes has reproduced the compiler from source, on its own hardware,
with nothing from this desk in the loop but the input file.

Bed-verified 2026-08-11 on a COPY, master untouched and re-checked virgin
afterwards. All six rungs green; `OUT.CDX` came off the volume at 5,389
clusters, complete, and hashed `9E823495...` -- equal to the host control AND
to the compiler binary that produced it. The virgin master answers
`MISSING: OUT.CDX`, which is the reader's negative arm and what makes the
positive one evidence.

**Do not rebuild this image larger.** A 65536-sector version of exactly this
image crashes codex-vm on the host before the guest starts
(`OperatorsManual`, "Exit code 49374"). At 32768 sectors the 13 MB ESP holds
the 2.59 MB payload, the 2.77 MB source and the 2.75 MB artifact with room
left, which is why this one is 16 MB.

### Provenance

Same payload as stick 2, so the same recipe up to `cdx-to-pe.ps1`. Only the
last line differs:

```powershell
build/build-img.ps1 -PeInput a5.efi -Out build/boot/a5bigflight.img `
    -Source Codex.codex -TotalSectors 32768
```

where `Codex.codex` is `build/concat-codex-self.ps1 -CodexDir codex/compiler`.
The source on the volume must hash the same as the one on disk: measured
`7BCF4941...` both sides, 2,779,145 bytes. The concat writes LF, so this one
needs no normalising -- unlike stick 2's, and check rather than assume.

### Bed state, stated including what metal adds

Run on a COPY of the master (see the rule above), 2026-08-11: the compiler
read `SOURCE.SRC` off the volume, painted every rung through to WHITE, and
`OUT.CDX` extracted back out of the FAT by an independent host-side reader is
**byte-identical to the host compile** at 2,759,023 bytes, with `OUT.TXT`
reading `OK OUT.CDX 2759023`. The master was re-checked virgin afterwards.
(This paragraph carried stick 2's 84,644 and its 14 seconds until this
rebuild; the two sticks share a payload and the numbers had drifted across.)

`build/read-stick.ps1` is the reader, and it is calibrated rather than
trusted: on the booted copy it returns `OUT.CDX` at the expected SHA-256,
and on the virgin master it returns `MISSING: OUT.CDX`. Both arms measured
again on this rebuild, for both sticks. It reads `\\.\PhysicalDriveN`
directly so that Windows never mounts the returned stick and allocates
clusters on it, and `-ImageFile` is the arm used here.

**What the bed cannot answer is the storage.** codex-vm's disk is IDE and
the ASUS boots USB mass storage over BOT, so the one thing this arm exists
to exercise is the one thing no bed has ever run. Do not read the green bed
as the write path being proven on the box.

### The ASDE arm is NOT on this image, deliberately

Track B's B2 Finding 4 (CTRL.ASDE, the `NicAsde` stage) is metal-gated and
was considered for this boot. **CurrentPlan records that the ASDE arm
WEDGES the real part deterministically** (hang after `entering bring-up`),
so anything sequenced after it in a single boot is lost. It is not worth
risking the WORKS-8 reading, which is ready now, against an arm that ends
the boot. If it ever rides a desk boot it must be LAST or behind an
explicit keypress taken after the F12 shots are on the volume.

## NOT SCHEDULED, AND NOT A REQUEST FOR A FLIGHT: the WORKS-8 FAT write-path arm. Ride it on the next boot that happens for its own reasons.

**Damian's standing ruling is that flights are not proposed for the F12
work.** This arm asks for no boot of its own, no image of its own and no
extra keystroke beyond an F12 that most desk boots take anyway. It is
written down so that whenever a stick next flies carrying GopBoot or
GopDesk, the reading is free instead of missed. If nothing flies, nothing
is owed.

**What it would confirm.** WORKS-8 (`apps/works/works-backlog.md`) closed
four defects in the FAT write path in the bed -- a failed write leaking
its chain, an allocator bound that ran 274 entries past the end of the
volume, a diag instrument that could not name a write failure, and the
live-chain collision guard reinstated after main 14141 reverted the one
that refused correct writes. **None of it has run on metal.** Everything
below reads off a stick that has already flown; the only live step is
pressing F12 twice.

### In flight: press F12 twice, and photograph only if the taskbar says FAILED

Two consecutive shots is the exact case that failed under 13613 -- the
first landed and the second was refused -- so one shot proves less than
two. Take them from the desktop, a few seconds apart.

On success the taskbar paints `shot SHhhmmss.BMP ok` and there is
nothing to read. On failure it paints `shot write FAILED s.. m.. c..
p.. w..`, and **`w` is the new cell; photograph the whole line.** Before
main 14169 that line had no `w` at all and every write failure reported
`s7`, which is the MOUNT stage answering "ok" and naming nothing.

| `w` | What it says happened | The measurement that settles it |
|---|---|---|
| line has no `w` | An image older than main 14169 | Nothing to read here; note the image digest |
| `20` | The writer reported SUCCESS while the taskbar said FAILED | The two disagree, so one of them is the defect, not the write path. Photograph and stop |
| `17` or `8` | The collision guard REFUSED the allocation | Whether a real collision existed is answerable only off the returned stick -- see the overlap check below. **Do not call this a returning regression on the strength of this cell.** It is the collision class, and the class has two branches that look identical here |
| `12` | The allocator found no free cluster inside the volume | Whether the volume is genuinely full is the cluster count in the stick check below |
| `13` | A FAT flush write failed | Storage-layer; `c` and `p` on the same line carry the MSC completion code and BOT phase |
| `14` | The data write came up short | Storage-layer; same `c` and `p` |
| `15` | The root directory had no free slot | Count the root entries on the returned stick |
| `16` | The directory entry write failed, and the chain WAS rolled back | The stick is clean; the failure is the directory sector write |
| `116` | Same, and **the rollback also failed** | The stick is DIRTY: a chain is allocated with no entry naming it. The orphan check below measures how much |
| `2`-`7` | The single-cluster path, not the shot path | A shot is megabytes; if a shot reports these, the size branch is the thing to look at |

Do not infer a cause from `w` alone. Each row names the next
measurement, and every one of them is taken off the stick afterwards,
not at the board.

### Off the returned stick -- a raw read, no writes, no reflash

**Read the physical device, and do not let Windows mount it read/write.**
This is not a formality and it is the step most likely to be skipped:
Windows writes to a FAT volume it mounts (`System Volume Information`,
recycle-bin metadata) before anyone has read a byte, and those writes
ALLOCATE CLUSTERS. Question 3 below counts clusters allocated to nothing,
so an ordinary mount manufactures the exact evidence the question asks
about, and it is unrecoverable once done. Read `\\.\PhysicalDriveN`
directly, the way the 2026-08-07 measurement did (raw read, ESP at LBA
2048).

**There is no tool for this yet, and that is the real cost of this
section.** Nothing in `build/` or `tools/` walks a FAT chain off a
physical device; `build/flash-usb.ps1` is the only raw-device script and
it writes. Whoever runs this writes the walker first. It is a
read-only PowerShell job against the four questions below and needs no
gate, but it is an hour that the rest of this section does not look like
it costs.

Four questions, all mechanical:

1. **Are both BMPs there**, with plausible sizes? Two consecutive shots
   both landing is the headline; under 13613 the second did not.
2. **Do their chains overlap each other, or any other file's?** Walk each
   chain from its directory entry. This is what answers a `w17`: the
   guard refusing when no collision exists is a false positive and is the
   13613 failure returning; the guard refusing when the chains really do
   share a cluster is the guard doing its job. **The stick decides which,
   and nothing at the board can.**
3. **Are there orphans** -- clusters marked used in the FAT that no
   directory entry reaches? This is the leak measurement, and it has a
   known prior: a failed shot used to strand 4,609 clusters, about 2.3
   MB. The number to hope for is zero.
4. **Is any allocated cluster numbered above the volume's highest valid
   one?** On the 2026-08-07 ESP that was 26,350, against a FAT with room
   for 26,624 entries. Nothing above 26,350 should ever be marked used.
   This one did not fire on the last flight either, so a clean reading
   confirms rather than surprises.

Questions 3 and 4 are the ones a returning stick has never been asked.
They need no flight of their own and can be run against any stick that
comes back from any boot carrying main 14169 or later.

## FLOWN 2026-08-05, GREEN: `ceremonyboot.img` (`C423418DF6FC9DC7D13CA47F8820B373899C1DFA189A1E7D0D5144480FE0A383`). The ceremony flight. Damian: "it all worked."

**Flight verdict, same day it was flashed:** the full first-boot
ceremony ran on the ASUS keyboard; `IDENTITY.DAT` (124 bytes, exactly
the version-1 record size) is on the stick's ESP -- the USB
mass-storage WRITE path works on the real ASMedia -- and the B3.6
unlock passed on the returning boot. The F12 desktop shot from the
flight (`SH160738.BMP`, 16:07:38, top bar `k4 e98n0s0 |e0n0 |e131n50
|e0n0`) was retrieved to `build-output/ceremony-flight-shots/` and is
embedded in `docs/TailorsFitting.md`, the first-boot document this
flight illustrates. A2's ceremony campaign closes on this flight.

**The parenthesis that used to close this entry was wrong in both
halves, and it mattered.** It said the stick had moved on to reek's
probes the same day, so that "this image and its identity are off the
stick, and the ceremony artifact remains at
`build/boot/ceremonyboot.img`". Checked 2026-08-11:
`p4 files "//Codex/*/build/boot/ceremonyboot.img"` answers **no such
file(s) in any stream, at any revision**, and `deskboot.img` likewise.
Neither flown image was ever checked in, although every other boot image
beside them was (`a5flight`, `a5flight2`, `a5bigflight`, `blockladder`).
And the stick had NOT moved on: reek read disk 2 on 2026-08-11 and found
this same desk/ceremony boot still on it, `CODEX.CDX` at 2755007 bytes
with `CMUNSS.TTF` and a guest-written `IDENTITY.DAT` of 124 bytes.

So for six days the only copy of a green flight's artifact was the
physical stick, while this document said it was in the tree. reek dumped
all 16 MB before reflashing (`629821CF...`, in reek's p4-ignored
`build-output/`) and flashed `a5bigflight.img` over it, which is the only
reason the bytes still exist at all.

**What is preserved and what is not.** The flight's CONCLUSIONS are
safe: the verdict table below, the F12 shot in
`build-output/ceremony-flight-shots/`, and the walkthrough in
`docs/TailorsFitting.md`. The image is REBUILDABLE from
`apps/works/DeskBoot.codex` and `codex/os/verify/WakeCeremony.codex` plus
the recipe below. What is not reproducible is the 124-byte
`IDENTITY.DAT` the guest itself wrote to the ESP on real ASMedia
hardware -- the single physical artifact of that write path working on
metal. It exists now only inside that dump, and the dump was itself one
gate run from deletion: `build-output/` is wiped by every gate's `clean`
phase, which is how blu's three preserved sticks were already lost the
same day. **Rescued 2026-08-11 to
`D:\Projects\stick-archive\stick-before-20260811.img`**, verified byte for
byte; see NEVER PRESERVE A RETURNED STICK UNDER `build-output/` above.

The pre-flight record below stands as flown.

Flashed to the stick 2026-08-05 15:57, write-back verified byte for byte
(`build-output/flash.log`). Damian's flight-3 F12 shot (`SH041503.BMP`)
was retrieved off the stick first: `build-output/flight3-shots/`.

Built `build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex
-Kernel seed/Codex.cdx -Ebs -Out build/boot/ceremonyboot.img` against
depot seed `52E0A3A00218E19F`. The payload is GopBoot: the first-boot
ceremony (GopWizard), the three-row interface menu (main 13223 --
Graphical UI is row 0 and the DEFAULT; Dev Console and Serial REPL are
gone, filed WORKS-5), the B3.6 unlock (main 13213), and the desktop,
all carrying the fixed input stack (per-interface walk main 13096,
per-(slot,DCI) latch main 13133).

Rehearsed on byte-copies of THIS file, both beds:

- codex-vm `-uefi -hid-combo -hid-nak-unchanged -hid-keys`, image as its
  own disk: fresh ceremony to the menu; second boot unlocked with the
  public-key match; three wrong passphrases landed on Identity Locked.
- OVMF q35 `-UsbDisk -UsbKbd -NoPs2` (real firmware, the boot medium
  reachable ONLY through our USB stack): the whole ceremony typed over
  USB HID, IDENTITY.DAT written through GopUsbMsc, and the second boot
  read it back and painted Identity Unlocked; a spare Enter at the menu
  entered the desktop with TrueType from the ESP.

### The boots, and what to type

**Boot 1 (fresh ceremony):** bars -> trace lines -> keyboard table ->
"Welcome to Codex", Press Enter to begin (30 s window; any key holds it
open -- **if it expires untouched the controller is handed back to
firmware, which costs the USB stick and keyboard for that boot:
power-cycle and start over rather than reading anything from what
follows**). Then: passphrase + confirm (4+ characters -- **REMEMBER IT,
boot 2 is the whole point**), a random sentence, then the upstream
server address (a network feature; Enter alone skips it, and skipping
is right for this flight), then the Identity Created screen --
**photograph it: the save row is a verdict** -- then Enters through the
storage/disks/xhci/wake screens to the menu. Enter on Graphical UI
(the default row) opens the desktop.

**Boot 2 (the B3.6 verdict):** from the desktop, Shutdown powers the
machine off (proven flight 3), or return to the menu and take Restart;
either way, boot the stick again -- nothing is reflashed in between.
It should open "Welcome Back" with the fingerprint and ask for the
passphrase. Type boot 1's passphrase. Photograph the result either
way. A deliberate wrong-passphrase run (three times -> Identity
Locked, stick unchanged) is optional and can ride any later boot.

| Read | Verdict |
|---|---|
| Boot 1 Identity Created says "Saved to the stick as IDENTITY.DAT." | The USB mass-storage WRITE path works on the ASMedia; B3 persistence holds on metal |
| Boot 1 says "Could not save to the stick." | The ceremony ran and the write path is the defect; photograph the screen. Boot 2 will re-run the fresh ceremony, which is the same reading from the other side |
| Boot 2 opens "Welcome Back" with a fingerprint | IDENTITY.DAT was written AND parses -- the CIDN record round-trips through the real stick |
| Boot 2 says "Identity Unlocked ... The public key matches" | **B3.6 closes on metal** |
| Boot 2 answers "Wrong passphrase" to the RIGHT passphrase | Decrypt path wrong on metal only; the fingerprint on screen names which identity it read. Photograph |
| Boot 2 says the identity "cannot be read" | The file exists but mount/read/parse failed; photograph the trace lines |
| Boot 2 runs the fresh ceremony again | Boot 1's save silently failed or did not survive; pairs with boot 1's save row |
| The ceremony keyboard is dead | Read the top-bar `k`/`e`/`n`/`s` counters against the flight-3 conventions further down this file |

Photographs to bring home: boot 1's Identity Created screen, boot 2's
result screen, and anything that looks off on the menu (it should be
three rows, Graphical UI highlighted).

## FLOWN 2026-08-05 19:10, THE STORAGE ANSWER IS BANKED: `msc-align.img` (`4A2C05F5A3675234`).

The window did its job: F12 landed `SH191008.BMP` (retrieved to
`build-output/msc-align-shots/`, pixel-exact) BEFORE the ASDE arm
wedged the machine -- same last row as the first boot, `ASDE:
eligible at 0:31.6 -- entering bring-up`, so **the wedge is
deterministic, two boots**. The blu outbox entry stands with that
upgrade. The readings, from the shot:

- **`ALIGNED  addr=#..._751c0000 off=0     ok=y chk=e173b96d`**
- **`CROSSING addr=#..._751cfc00 off=64512 ok=y chk=e173b96d`**
- **`data identical=y`** -- a 32 KB bulk TRB crossing a 64 KB
  boundary delivers byte-identical data ON THE REAL CONTROLLER.
  A4b's crossing question, answered with a checksum pair.
- **`LIVENESS lba=60506128 (sectors+16) ok=n`** -- the DERIVED
  out-of-range arm fires on the real 28.9 GB stick
  (`sectors=60506112`), which is what makes the two green rows
  claimable; the 2026-08-04 boot voided this exact arm.

The stick still carries this image; it reflashes for whatever flies
next.

Flashed and byte-verified (`build-output/flash-msc-align2.log`).

The `A1C0F205` boot (below) proved the ASDE arm wedges the machine on
the real I219, and because `shot-wait` ran AFTER that arm, F12 never
armed and the storage rows were on the glass with no way to capture
them. The rebuild inserts GopShot's `shot-window` (red 13355) between
the storage rows and the ASDE arm: a 20-second RTC-bounded stretch
(spin-fueled against a dead clock) in which F12 works exactly as in
the open-ended wait, painted as "F12 now saves the screen (20s
window), then the arm under test runs". The storage answer gets
banked BEFORE the arm that is allowed to hang. Re-gated under OVMF
`-UsbDisk -UsbKbd -NoPs2`: all rows reproduced, F12 during the window
wrote a 3,072,054-byte BMP extracted intact from the image.

At the bench: boot, read the four storage rows, **press F12 inside
the 20-second window**, and let the ASDE arm run (it will likely
wedge again -- that is now a reading, not a loss; the shot is already
on the stick). Then pull and hand the stick back.

## FLOWN 2026-08-05, WEDGED IN THE ASDE ARM: `msc-align.img` (`A1C0F205BDA1D78B`).

Boot verdict from the glass: the four storage rows painted, then
`ASDE: scanning bus 0`, then `ASDE: eligible at 0:31.6 -- entering
bring-up`, then the machine stalled. **The first metal execution of
`na-bring-up` wedges on the real I219** -- the exact risk that made
the arm ride last, now measured. F12 never armed (shot-wait was
downstream of the wedge), so this boot's storage rows went uncaptured;
the rebuild above fixes that ordering. Finding routed to blu via
red's outbox: every E1000e wait is fueled and the Option A path maps
the [3 GB, 4 GB) MMIO hole (the xHCI probe read its BAR at
`#df430000` on the same boot path), so no software loop explains a
stall -- the suspect is the device ceasing to complete MMIO reads
after the bare `CTRL.RST` that `na-bring-up` opens with, on a part
whose PHY the ME owns.

## FLOWN 2026-08-05 18:35: `xhci-probe.img` (`AF3A6B4551453001`) on the ASUS. THE FIRST PIXEL-EXACT PROBE READING.

One boot, one F12, no camera. The probe wrote its own screen through
the disk stack it was testing; `SH183500.BMP` (2,359,350 bytes,
intact) is retrieved to `build-output/xhci-probe-shots/`. The rows,
for reek to fold into A4 (the shot is the record; these are the
headlines):

- **`MSC: rung=6 disk usable`, `dev on ctl0 port=8 speed=3 slot=4`** --
  the disk sits ABOVE ROOT PORT 7, the first observation of it
  anywhere; no bed can seat a disk there. `sectors=60506112` (the real
  stick), `blocksize=512`, `cfgv=1 cfgep=1`.
- **`SET-CONFIG completion: USB TRANSACTION ERROR  retry: success`** --
  the first SET_CONFIGURATION errors on the wire and the EP0
  recover-then-retry gets past it, pixel-recorded.
- Controller `8086:a12f` at 0:20.0, caplen=128, `slots=64 ports=26`,
  connected mask `#000001a2` (ports 1, 5, 7, 8). `xHCI seen=2
  opened=1`: a second row `ctl1 0000:0000 at 0:0.0 NEVER-OPENED` --
  a zero-ID controller entry worth reek's eye.
- `ENUMERATED kbd=y mouse=y disk=y`; HID EP `bInterval asked=255 ->
  Interval set=10`, `wMaxPacket 4 -> 4`, `speed=2 dci=5`.
- The 2.3 MB write landing intact IS the metal write proof for the F12
  path on this controller.

The pre-flight record below stands as flown.

## The pre-flight record for both probes: rebuilt WITH F12 SCREENSHOTS, re-gated 2026-08-05.

Damian's ruling after the ceremony flight: no more photographing probe
screens -- the probes now carry the desk's F12 screenshot-to-stick
(red 13330). Both probe images are rebuilt and re-gated; the earlier
`9F1559AA`/`38883476` builds (flashed 16:31, boots read but not
photographed) are SUPERSEDED. Press F12 on the probe screen and the
frame lands on the ESP as `SHhhmmss.BMP`; the bottom row paints the
verdict. Retrieval: mount the stick's ESP elevated and copy, as for
the desk's shots.

What changed in the probes: `probe-halt` is now `shot-wait` (GopShot):
`medium-select` runs once, and the shot writes ONLY to a medium whose
ESP carries our own `CODEX.CDX` -- a stick that lacks it paints "F12
shots OFF" and never writes anywhere, so a machine's internal drives
are never touched. That guard is why both images now CARRY THE SEED
(built without `-Seed ''`).

Rebuilt against depot seed `E0B667443430D9C7` with
`-Kernel seed/Codex.cdx -Font '' -Source '' -Ebs`:

| Image | SHA256 (16) | OVMF gate (re-run on these files) |
|---|---|---|
| `build/boot/xhci-probe.img` | `AF3A6B4551453001` | green, `-UsbDisk -UsbKbd -NoPs2`: `rung=6 disk usable`, `SET-CONFIG completion: success`; F12 wrote a 3,072,054-byte BMP whose bytes were EXTRACTED from the image and render seamlessly |
| `build/boot/msc-align.img` | `A1C0F205BDA1D78B` | green, `-UsbDisk -UsbKbd -NoPs2`: `ALIGNED ok=y chk=e173b96d`, `CROSSING ok=y chk=e173b96d`, `data identical=y`, `LIVENESS lba=32784 ok=n`, both ASDE breadcrumbs (`scanning bus 0` -> `candidate REJECTED by vendor/BAR gate`); F12 shot extracted intact the same way |

**Bed finding from the F12 proof, codex-vm only (routed to reek):**
the same F12 write under codex-vm lands a FAT whose entries from 5803
on hold the in-memory value four ENTRIES back (`disk[N] = mem[N-4]`,
an 8-byte duplication mid-stream), so the chain reads 4 clusters then
EOC and the file truncates. QEMU (above) and metal (flight 3's desk
F12, byte-complete `SH160738.BMP`) both write correctly, so the
suspect is codex-vm's MSC bulk-write model, plausibly at a 64 KB
buffer crossing -- the exact TRB question msc-align asks for READS.

**UPDATE, reek 2026-08-06, on red's preserved artifact: red withdrew the
`disk[N] = mem[N-4]` index shift, and the corrected reading is an
off-by-four in the ALLOCATOR's view -- the file starts at 5799, inside
`CODEX.CDX`'s own chain, where the first free cluster was 5803.
Confirmed independently: the failing image first differs from the
pristine one at FAT1 offset 11,606, entry 5803. The codex-vm TRB
data-buffer alignment mask (removed main 13448) fits those numbers
exactly and is REFUTED as the cause anyway: run on red's own pristine
image, red's key timeline and red's 1024x768 geometry, mask-restored and
mask-removed builds both give start=5803 chain=4609, and neither
presents an unaligned TRB. The remaining variable is which codex-vm
revision red's run used. The GopFat16 gap red names -- nothing verifies
a cluster about to be taken lies outside an existing chain -- is real
independently of the diagnosis.**

**DOES NOT REPRODUCE, reek 2026-08-06. Do not plan against this
paragraph until it is re-measured.** Same repro, probe rebuilt from
the documented line at seed `E0B667443430D9C7`: the SH entry writes
`cluster=5804 size=921654` and the chain is 1801 clusters terminating
`0xFFFF`, which is exactly `ceil(921654/512)`; the BMP walked out of
that chain is 921,654 bytes and its own header filesize field agrees.
FAT1 and FAT2 identical. The walker was calibrated by shifting the FAT
right by 8 bytes from 5804 and reports a runaway chain, so it can see
the failure. Nothing in the write path moved after the measurement
(`codex-vm.c` 13238, `GopUsbMsc` 13133, `GopFat16` 13111), so the
difference is in the run rather than the source, and red holds the
only failing artifact -- `xhci-probe.img` is a local build artifact in
no depot, so `AF3A6B45` cannot be fetched. Also worth knowing before
comparing image hashes across workspaces: the identical build line and
seed produced `7D7A0E54` here, not `AF3A6B45`.

`msc-align.img` flashes after the xhci boot, same flasher line.

`msc-align.img` carries blu's FIXED `NicAsde` stage (main 13187). Its two
breadcrumb rows both paint: `ASDE: scanning bus 0` then `ASDE: candidate
REJECTED by vendor/BAR gate`, which is the correct verdict for QEMU's
default NIC and means the bring-up half is still unexercised by any bed.
**No bed exercises it, and none can today.** `test-ovmf.ps1` has no NIC
selection switch; under `-UsbDisk -UsbKbd -NoPs2` its NIC IS Intel
(`vendor=32902`) and is rejected on `verdict=below-window`,
`bar=2164654080`, so nothing reaches `na-touch` or `na-bring-up`. The
flight is the only instrument for the bring-up half.

**The derived liveness LBA works.** `lba=32784` is `md-sectors + 16` off
the 16 MB image and comes back `ok=n`, so the failure channel is live and
the two rows above it are claimable. That is the arm that read `ok=y` on
the ASUS and voided the whole rung.

**No bed can show either probe with the disk above root port 7, and the
sitting will be the first observation of it.** QEMU refuses USB
attachment past its eighth port whatever HCSPARAMS1 claims; codex-vm can
seat the stick anywhere (`-xhci-ports 26 -usb-disk-port 10`) but both
probes are GOP-only and codex-vm cannot screenshot a spinning payload --
measured twice today, with and without `-headless`, no BMP either time.
The ASUS answers with the stick on port 9. Mirroring the probe rows to
serial would make that bed readable without a body; it is not done, and
it is a change to an artifact that is about to fly.

## FLOWN 2026-08-05, ALL GREEN: `deskboot.img` (`ADA7CC4D9837B66097B89745EB7699445F9E8FCC8F5CEE6D04F074BEE0BFA004`). The completion-steal fix. Flight 3.

**Flight 3 verdict, Damian on the glass: "it all works." The mouse
works, clicks work (the Shutdown button clicks and powers off), F12
screenshots land on the stick with the taskbar verdict posted, and the
shot files appear in the Files app.** A3 is CLOSED, the write path is
proven on real hardware, and the camera stands down. Both flight-2
defects (keyboard death mid-session, mouse dead) are cured by the
(slot, DCI) latch below; fix red 13128, main 13133.

Flashed and read back byte-for-byte 2026-08-05, disk 2. Supersedes the
flown `23C4A936` build (below): same payload -- mouse walk, F12 shots,
table dwell -- plus the fix for what flight 2 exposed.

**What flight 2 taught** (table photographed, upside down and perfect):
port 1 is a Logitech Unifying receiver (`046d:c52b`, kbd dci=3, mouse
dci=5, raw DJ dci=7, ONE slot), port 7 the wired keyboard (`046d:c31c`),
`bound=5`. The desktop typed, then the keyboard DIED mid-session; the
mouse never moved. One defect: `xhci-wait-xfer` matched transfer events
by SLOT alone, so sibling endpoints armed on one slot steal each other's
completions -- the kbd/raw pumps poll the dongle's slot before the mouse
pump and ate its completions from frame one, and the wired keyboard lost
a 500 ms heartbeat to its own raw sibling minutes in and starved forever
(the thief generates no traffic of its own, so the slot goes silent).
Flight 1 survived only because the old walk never armed the siblings.

**The fix on this image:** the completion latch is keyed by
(slot, DCI) and every waiter passes its endpoint -- control, storage,
keyboard, mouse, camera. Proven both directions in a bed that could not
previously express starvation: new codex-vm `-hid-nak-unchanged` makes
interrupt endpoints NAK until they have news (the instant-complete
default made completions too plentiful for a scarcity defect to show);
under it `usb-hid-steal` answers `pos=0,0` on the old code and
`pos=80,40 btn=1` on this one. Rehearsed on THIS file, UEFI-booted with
a combo receiver and the image attached as its own disk: keyboard opened
Monitor, the USB mouse crossed the screen and CLICKED OPEN the Files
pane, TrueType loaded from the ESP.

Reading, beyond the standing rows (the table below still applies):

| Read | Verdict |
|---|---|
| Keyboard types AND KEEPS TYPING past a few minutes | **The steal fix holds on metal.** Flight 2's death was minutes in; longevity is this flight's keyboard reading |
| Cursor moves, click opens a pane | **The mouse works through our driver.** A3 closes on this photograph or its F12 shot |
| Keyboard dies again mid-session | Photograph the top bar `k`/`e`/`n` counters THE MOMENT it dies: whether `e` still climbs after keys stop decides event-theft vs endpoint death |
| Mouse still dead but keyboard lives | Photograph the HID table rows: if hid2 (`c52b` dci=5 mouse) is present, the dongle's boot-protocol mouse path is the suspect, not the walk |

## FLOWN 2026-08-05 (was READY TO FLASH 2026-08-04 NIGHT): `deskboot.img` (`23C4A936E6CC56ABE8591ACFCB690B86E5E9B472EB1F62AE398DFDDFF1E89744`). The mouse and the camera's replacement.

**Flight 2 verdict: the table was photographed and named the bus (see
the entry above); the keyboard typed then died mid-session, the mouse
never moved -- both explained by the completion-steal defect the entry
above fixes. F12 was never exercised (no BMP on the ESP afterward).**

Supersedes the flown `CAE755B1` build (below); same payload plus three
capabilities, all bed-proven on this exact file or its dev-loop twin:

1. **Every boot MOUSE is bound, per-interface** (`usb-hid-walk`): a
   combo device -- keyboard on one interface, mouse on the next, the
   dongle shape -- now contributes both. `usb-hid-combo` proves it
   (`same-dev=1 got=30 pos=60,40 btn=1`; against the old walk:
   `mouse: ok=0`), and a codex-vm desk capture shows the Files pane
   opened by a USB mouse click. The reading on the glass: Monitor's
   `input` row says `mouse yes`, and the cursor moves and clicks.
2. **F12 writes the screen to this stick as `SHhhmmss.BMP`** -- on the
   desktop and inside every GopDesk pane -- through a new multi-cluster
   FAT16 chain writer (bulk FAT flush, 64-sector data runs). Proven:
   `fat-write-big` (3 MB written and read back through the independent
   bulk reader, zero bad bytes) and a full desk arm whose extracted
   Monitor-pane BMP is pixel-exact. The verdict paints in the taskbar:
   `shot SHhhmmss.BMP ok` or a named failure. **This is the telemetry
   channel that retires the camera**: shoot, later pull the stick, and
   the frames are files on the ESP.
3. **The HID table survives a pre-loaded scancode** (drained mailbox +
   3 s dwell floor), so the table that flashed past unseen on the last
   boot holds still; rows now tag kbd / mouse / raw, so the four
   interfaces get NAMES (VID:PID) this boot.

Flash block and stick discipline: identical to the flown entry below
(same flasher line, `-Image build\boot\deskboot.img`, hash above,
PULL do not eject). Reading, beyond the standing keyboard rows:

| Read | Verdict |
|---|---|
| Cursor moves, click opens a pane | **The mouse works through our driver.** A3 closes on this photograph -- or on an F12 shot of it |
| Monitor `input` row `mouse no`, cursor dead | The typed-on mouse is none of the bound interfaces; photograph the HID table -- the mouse's `id=` row (bit `mouse`) or its absence names the next move |
| F12 -> taskbar `shot SH....BMP ok` | **The write path works on the real stick.** Frames come home as files; the camera stands down |
| F12 -> `shot write FAILED` | The chain writer failed on real hardware; the stick still boots (both FAT copies stay consistent); photograph the taskbar and note how many shots preceded |
| F12 -> `shot: no ESP mount` | The mount failed in the shot's own context; photograph |
| F12 -> nothing in the taskbar | The key never decoded: check whether the top bar `s` moved on the press; if `s` moves with no verdict, that is a dispatch defect, photograph |
| ~10 s pause after F12 | Normal: a 1024x768 frame is ~2.3 MB through 64-sector bulk writes. The desk resumes by itself |

Shots persist across boots; each is named by the RTC second. Read them
back on the dev box by mounting the stick's ESP (a plain FAT16
partition Windows can read); two shots within one second overwrite
each other, deliberately.

## FLOWN 2026-08-04 EVENING: `deskboot.img` (`CAE755B1...6B3A`). THE KEYBOARD WORKS ON METAL. THE CAMPAIGN IS CLOSED.

Flashed to disk 2 (` USB DISK 2.0`, 28.9 GB), full readback verified,
booted the ASUS first try. What the glass answered (Damian, at the board):

| Read | Verdict |
|---|---|
| Bars, then the desktop directly; the keyboard hold skipped on its first poll | A scancode was already pending -- the boot-menu Enter still releasing as our driver took the controller. **That keystroke, and every one after it, travelled our own USB driver end to end** |
| Files, Calendar, Issues and Monitor panes all open and answer keys | Decode, mailbox and every pane loop hold on metal. **A2's keyboard question is closed** |
| Top bar `k4 e0n0s0 \|e1n0 \|e0n0 \|e397n88` | **FOUR keyboard-shaped interfaces on this bus, and the typed-on one is the FOURTH bound**: e climbing at the ~500 ms idle cadence, n=88 key-bearing reports. The first three carry nothing. First-match binding could never have worked on this machine, whatever the transport under it did -- the diagnosis exactly |
| Monitor `input` row: `keyboard USB HID   mouse no` | The mouse is unbound/undelivered: A3, the named next campaign, now with a live desktop to instrument it from |
| The keyboard table was never seen | The pending scancode skipped the hold inside one frame and the desk painted over the table. The identities remain in diag cells 80-95; follow-up: drain the mailbox once before the hold or give the table a minimum dwell, so the table survives a pre-loaded key. Not worth a reflash today |

The interface identities (which VID:PID each of the four blocks is) were
not photographed and are the first thing the next boot of this same stick
brings home for free -- boot it once with no key touched and the table
holds for its full ten seconds.

## THE ENTRY BELOW IS THE FLIGHT THIS RECORD ANSWERS, kept for the procedure and the reading table.

## READY TO FLASH, 2026-08-04 EVENING: `deskboot.img` (`CAE755B1C2189A2CD7897FCD1FB07D875038CFF1CD7ADDFE3364616921EF6B3A`). The keyboard FIX, not another instrument.

**This supersedes both `DA2556FBCA0188F0` (below) and the unflashed
`D0F61D751F9680F9` (the n=/b= instrument boot that was to decide the
DMA-address fork -- decided by inspection instead): it carries their
readings AND the repair. It also supersedes the "NEXT BOOT:
kbd-diag-v16" section and the 2026-08-03 STATUS section's "USB HID
proven on the board" claim further down -- both are historical record
now, per the corrections in the v15 row.** Built
`build/boot/build-option-a.ps1 -Src apps/works/DeskBoot.codex -Kernel
seed/Codex.cdx -Ebs` against seed `52E0A3A00218E19F`; the file at
`build/boot/deskboot.img` hashes to the digest above and that exact file
passed the OVMF gate (boots real UEFI firmware to the full desktop,
TrueType from its own ESP) and a codex-vm `-uefi` boot.

### The diagnosis this flies on

Assembled from readings already home, no new boot spent: under factory
idle the bound interface delivers an all-zero report every 500 ms (v15
`EPINT=97` at 48 s, desktop `a`/`e` lockstep, `c=1`, `p=3/3`, `r=` zeros)
-- so DMA, ring and events all work and the DEVICE reports no keys. Its
own control pipe says the same (`R2:` zeros with `f=1f`, and `i=125`
proves nonzero control-IN data lands in the same arena, killing the
address-truncation candidate). Back in the SET_IDLE(0) era a keypress --
which report-on-change semantics oblige a report for -- produced zero
events in 90 s (v14). And the same physical keyboard delivers keys to the
firmware's boot-protocol poller (v11 phase 2). One conclusion survives:
**the interface `usb-attach` binds first is not the device the keys are
typed on.** The walk bound the FIRST boot-keyboard interface and stopped.

### The fix, and how it was proven without the board

`usb-attach` now binds every boot-keyboard interface on every controller
(hub-descended included) up to FOUR -- the primary plus three peers,
`kbd-peer-cap` -- plus the primary device's other HID interrupt-IN
interfaces as raw counting listeners inside the same cap; all pump into
the one scancode mailbox. A fifth interface is silently not bound; the
table's `bound=` count against the visible rows is what would say so. codex-vm gained the arm no bed could produce (`-hid-root-silent`:
first keyboard completes SUCCESS with eight zero bytes, second carries the
keys) and `-hid-keys` (keys reach the guest ONLY through USB HID -- the
first bed in this tree that can prove a scancode crossed the interrupt-IN
DMA path). `codex/test/apps/usb-kbd-silent` reproduces the board's exact
state and answers `got=30 primary-live=0 peer-live=Y peer-scans=Y`;
sabotaging the multi-bind back to first-wins answers `peers=0 got=0`.
The flash image itself, UEFI-booted under that arm, opened the Monitor
pane from a keystroke that had no route but the second keyboard's pipe.

### The boot

**PULL THE STICK OUT, DO NOT EJECT** (standing). Pre-flash, hash the file
and confirm the digest above, then:

```powershell
Get-Disk | Where-Object BusType -eq 'USB'      # find N -- check it twice
Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-red\build\flash-usb.ps1','-Image','D:\Projects\NewRepository-red\build\boot\deskboot.img',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\Projects\NewRepository-red\build-output\flash.log'
```

Sequence on the glass: bars + mode (held 10 s) -> trace lines -> **the
keyboard table** (one row per bound interface: `id=` VID:PID, port,
route, dci, boot/raw, speed, buffer) -> **keyboard hold, 10 s, ANY KEY
SKIPS** with a live per-interface counters row -> desktop.

**Operator instruction, and the failure rows are meaningless without
it: TAP A KEY as soon as the hold row appears. If the hold does not
skip, HOLD A LETTER KEY DOWN for the remainder of the window and
photograph the table with the live row.** "Hold expires untouched"
in the table below means no scancode ARRIVED, not that no key was
pressed -- the `n` counters can only name where keys arrive if keys
were being sent while they counted.

| Read | Verdict |
|---|---|
| **A tap ends the hold at once and the desktop appears** | **The keyboard works through our own USB driver.** `f 3 c l i d m` open panes; Esc returns; the campaign closes on a photograph of the desktop with a pane open |
| Hold expires untouched and typing does nothing on the desktop | Still dead. Photograph the table and the live row: any interface whose `n` moved while a key was held is where the keys arrive, and its `id=` names the device. `raw` rows count undecoded interfaces -- `n>0` there means the keys ride a report format we do not parse yet |
| `kbd interfaces bound=1` and its `n` stays 0, key held | Only one keyboard-shaped interface exists and it carries no keys: photograph its `id=` -- the device identity is the whole next move |
| `bound=` 2 or more, all `n=0`, key held | Keys reach none of the bound interfaces; the id rows say what WAS bound and the un-bound classes become the suspects |
| The table is ABSENT | The boot died before or during `usb-attach`; the last trace line standing names the stage. A missing table is a reading, not a formatting fault |
| The hold skips with no tap | A phantom scancode reached the mailbox; note it and continue -- the desktop is still typable for the per-pane checks |
| Top bar `k=n` on the desktop | No boot keyboard bound at all; photograph the `usb OK` trace line and the table |
| The hold ends but the desktop never paints | Death in `medium-select` or `desk-run`; the last trace line standing names the stage (`medium kind=` is trace row 9) |
| No bars at all: firmware screen unchanged / solid dark blue / solid dark green | Pre-USB liveness states; read them against the colour table in section 4 ("Read the screen COLOUR"), not this one |

The desktop's top bar keeps the live per-interface counters
(`k<N> e..n..s.. |e..n..`) repainting every RTC second; on any failure
it is the photograph that decides, exactly as the hold row is.

Photographs to bring home: the keyboard table + hold row (one frame
covers both), and the desktop -- with a pane open if typing works.

---

## THE BOARD IS RED'S, 2026-08-04. The stick last held reek's `msc-align.img`.

Damian's ruling after reek's two boots. **Reflash before assuming anything
about what is on the stick**: reek's A4a blocker is closed on metal (`rung=6
disk usable`, cured by EP0 recover-then-retry, and the first SET_CONFIGURATION
errors on the wire every boot with the recovery getting past it), and rung 3
is NOT answered for the reason recorded in section 1.

## READY TO FLASH, 2026-08-04: `deskboot.img` (DA2556FBCA0188F0). A2b without a keyboard.

**Built, not yet flashed.** `build/boot/build-option-a.ps1 -Src
apps/works/DeskBoot.codex -Kernel seed/Codex.cdx -Ebs`, depot seed
`37A7EF8E4EF603AE`.

### Why this replaces the flight below before it has flown twice

`gopdesk-a2b.img` reached the first-boot ceremony and stopped there, because
the ceremony needs a keyboard and this board's keyboard is the open question.
**A2b does not need a keyboard.** The mode, the aspect, the seven panes and the
Monitor readings are all visible on a desktop nobody has typed at, so one
unknown was holding the other hostage. `apps/works/DeskBoot.codex` (main 12991)
is spine, `usb-attach`, `medium-select`, `desk-run` and nothing else.

### The three defects the 2026-08-04 boot found, all fixed in this image

| Found on the glass | Fix |
|---|---|
| `pit=979` at `s=573` AND at `s=3978` -- the timer stops at ~54 s and never resumes, while IRQ1 keeps delivering and the RTC keeps counting | `desk-clock` and `desk-mon-loop` gated on that dead cell, so **the taskbar clock and the whole Monitor pane would have frozen on this board**. Both now gate on the RTC second (main 12989). The `ticks` figure is now READ rather than waited on, so a dead timer reports itself instead of freezing its own instrument |
| A 30 s countdown advancing in 11 s jumps, read as a hung machine | The wizard painted its vitals every 262144 SPINS, and this board runs 24,000 iterations a second. Now on the RTC second and on every keystroke (main 12990). Same mistake the same chapter had already corrected one screen away for `wz-noclock-spins` |
| The boot diag "flash on screen too quickly to line up the shot" | Held five RTC seconds, not `boot-spin 150000000` |

### What is NOT fixed, and is the reason to fly this

**The keyboard, both paths.** Pre-handback our USB HID delivered nothing;
post-handback the firmware fallback delivered nothing either, for 3978 seconds
-- proven by the elapsed counter, which any real key resets to zero. The second
half **contradicts the v11 flight**, which recorded `rel=y reclaim=y` and keys
delivered and is the basis for calling the fallback real on this board (R-3).
Two boots of one machine disagree, so R-3 is narrower than it reads. Not
chased here: this image is built so the panel answers do not wait on it.

### Reading it

No keys needed. Bars (red, green, blue, white) and the mode, held five
seconds -- **photograph this** -- then three trace lines, then the desktop.

| Read | Verdict |
|---|---|
| Desktop paints, seven live buttons over Shutdown | **A2b passes** |
| Font is proportional, not the blocky CBF | The stick's own `CMUNSS.TTF` was read back through our FAT16 |
| `usb OK ... disk=Y` on trace line 2 | Our MSC attach works on the real ASMedia, still an open defect from v11 |
| Taskbar clock advancing past a minute | The RTC gate holds where the PIT one would already be dead |
| Monitor pane `ticks` frozen at ~979 while the pane stays live | The PIT stall, now reported instead of fatal |
| `display` / `framebuffer` rows | The mode actually in use, which is the input to the aspect work |
| `sci` value | A real GSI here means codex-vm's `0x2000` is purely an emulator artifact |

---

## FLOWN 2026-08-04: `gopdesk-a2b.img` (02FF3DD9A4C07D89). Reached the ceremony, stopped there.

**Flashed and verified 2026-08-04**, disk 2 (` USB DISK 2.0`, 28.9 GB), by
`build/flash-usb.ps1 -SpecFit -Force`: all 16,777,216 bytes read back matching,
GPT refitted to this stick (backup header at LBA 60506111), all four patched
blobs verified. Transcript `build-output/flash.log`.

**Provenance.** `build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex
-Kernel seed/Codex.cdx -Ebs`, built against depot seed `37A7EF8E4EF603AE`.
Carries CODEX.CDX (2,712,066 B), SOURCE.SRC (2,993,576 B) and CMUNSS.TTF, so
`medium-select` can lock onto its own disk, the Files pane has source to draw,
and the desk speaks TrueType rather than CBF. **`-Ebs` is deliberate**: GopBoot
brings up its own USB stack through `usb-attach`, so this is a driver-truth
boot and the firmware's xHCI driver must be gone. It is not a ConIn payload.

**What flies for the first time:** the seven-pane GopDesk with no dead controls
(main 12905), and the ACPI RSDP memory search (main 12905), which is what lets
the Monitor pane read tables on a payload the stub did not hand a pointer to.

### Pre-flight, stated including the part that did not pass

| Gate | Result |
|---|---|
| Image boots under OVMF post-EBS, exact file | **PASS.** Wizard paints legibly at 1280x800, `pit` and `rtc` both climbing |
| Driven to the desktop under OVMF | **NOT ACHIEVED.** 11 keys over 100 s, `sc=0` throughout, the wizard never left its Welcome screen and its 30 s window expired to `Handed back to firmware` |

**The second row is honest and it is not a reason the stick was held.** What it
establishes is that no scancode reached the payload from QEMU's USB HID
keyboard in this bed; it does NOT establish that the payload cannot read a
keyboard, and the two are different claims (L-OPTIONAL, and the inverse in the
`cured-defect` case: a bed can be too quiet as well as too capable). Checked
rather than assumed: `apps/works/GopUsbKbd.codex:102-106` still carries the
metal-proven cure, the SET_IDLE that is deliberately NOT sent, and the vitals
line showed the loop alive (`spin` climbing) with both clocks running, so the
silence is on the delivery path and not in a hung guest. **This board already
answered the USB HID question on metal on 2026-08-03 (main 12627).** The boot
below is what re-answers it for this payload.

### The boot

**PULL THE STICK OUT. DO NOT EJECT IT.** Windows rewrites the partition table
on re-enumeration and destroys the GPT the flasher just verified. Boot UEFI
with CSM/Legacy off.

1. **Boot diag.** Four bars top to bottom: red, green, blue, white, then
   `w= h= stride=`. Banded smear instead of clean bars is a stride or mode
   mismatch; wrong bar ORDER is a BGR/RGB swap. Then green trace lines
   top-left of the black region, one per stage. **On a machine that hangs, the
   last line standing names the stage that died** -- photograph it.
2. **Wizard.** `Press Enter to begin.` The vitals line under it is the
   instrument: `spin` climbing means the loop is alive, and **`sc=` is the
   keyboard answer.** Any key, not just Enter, restarts the window and proves
   delivery. If `sc` stays 0 for 30 s the payload hands the controller back to
   firmware and says so on the glass; that costs the USB stick it booted from,
   which is the accepted trade.
3. Through passphrase, entropy, upstream (Enter accepts each), then the
   keypair generates, then Complete.
4. Four screens follow: disk probe, disks, xHCI, wake. Enter through them.
5. **Menu.** Down once to `Graphical UI`, Enter.

### What a pass looks like, and what to bring home

| Read | Verdict |
|---|---|
| `sc=` moves when a key is struck | **The USB HID path holds on this board for the desktop payload.** A2b's blocking question |
| Desktop paints, seven sidebar buttons over Shutdown | **A2b passes.** Photograph it: this is the screen the project has been working toward |
| Any button does nothing when clicked | A regression -- as of main 12905 every drawn button has a handler |
| Monitor pane (`m`): `acpi` row reads `rev N tables N` | The stub published an RSDP and the parse works on real firmware |
| Monitor pane: `sci` value | **Worth reading deliberately.** codex-vm publishes a bogus `0x2000` (8192) here (`codex-vm.c:3388`). A real GSI, 9 or similar, on the panel confirms that value is purely an emulator artifact |
| Monitor pane: `display` and `framebuffer` rows | The geometry the payload believes, against the panel you are looking at |

Photographs to bring home: the boot diag, the desktop, and the Monitor pane.
Any screen that hangs, with its last trace line legible.

**Every reading table in this sheet must say what an ABSENT reading means,
not only what each present one does.** Routed by reek 2026-08-04 and it cost
them a boot: their sheet gave two branches for a retry line, `success` and
`USB TRANSACTION ERROR`, and the board answered with a third state the sheet
did not admit -- the line simply was not there, which meant the first attempt
had succeeded and no retry ever ran. A table that enumerates outcomes and
silently omits one sends its reader looking for a fault that is not there.
This is L-MISROUTE's quieter cousin: that row is about a sheet pre-assigning
the wrong cause, this is about a sheet having no row at all for what the
glass actually shows. When you write a reading table, write the missing-line
row first.

---

## STATUS 2026-08-03: READ THIS BEFORE THE REST OF THE SHEET

**The keyboard campaign that occupies most of this document is CLOSED.** Both
input paths work on the ASUS: the firmware path (main 12609) and USB HID (main
12627, proven on the board 2026-08-03). Everything below about rungs, the
`kbd-diag-v*` series, SET_IDLE, EPINT and the silent keyboard is **the record
of how that was answered, not open work.** Do not plan against it and do not
re-derive any of it.

It is kept rather than trimmed because it is the evidence: the ladder, the
QR-decoded bytes off the glass, and which hypotheses were killed by which
measurement. Four of them were wrong in ways worth not repeating.

**What this sheet still governs:** the procedure. Red owns it. Any future
sitting is planned here, top to bottom, under the rule below, and the standing
constraints in section 4 are all still live -- most of all **PULL THE STICK
OUT, DO NOT EJECT IT** (Windows rewrites the partition table on
re-enumeration and destroys the GPT the flasher just verified).

**What is actually open and would justify a sitting:**

1. ~~**The ASUS display defect.**~~ **CLOSED. This entry was stale when it was
   written and it is struck rather than deleted, because it sent work at the
   wrong target twice.** It is the ConOut re-mode of item 1 under v11/v12 below:
   AMI's GraphicsConsole re-modes the scanout on the stub's first ConOut call, so
   a stub that read `Mode->Info` first published geometry that was correct for a
   mode that no longer existed -- which is exactly the "geometry reads back
   correct" this entry treated as ruling the cause out. Cured in
   `build/cdx-to-pe.ps1` (clear first, ask after), confirmed on the glass by v11
   on 2026-08-02, and gated since 2026-08-03 by
   `build/boot/test-conout-remode.ps1`. **`test-ovmf.ps1` was never the answer**:
   OVMF does not re-mode on ClearScreen, so no QEMU geometry could express it.
   Only the residual 4:3 aspect stretch is open, and that is a native-mode
   SetMode in the stub rather than a defect.
2. **Rung 3, `msc-align.img`, the 64 KB TRB crossing.** Reek's, and it was
   dropped last time on a false negative. **The account of that false negative
   was itself wrong and is corrected here (reek, verified in source
   2026-08-03).** It read "every device on the Intel controller was Full or Low
   speed, so none was the boot stick" -- but that was read off a display showing
   **8 PORTSC rows for a controller reporting 26 ports**, and a SuperSpeed
   device sits on port 9 where the probe could not show it. Same shape as
   scanning bus 0 only and reporting NONE FOUND, which is the shape the entry
   itself invoked.

   The port-cell overflow behind it is fixed (`xhci-port-cells = 8` now guards
   the write in `xhci-diag-ports`, `apps/works/GopXhci.codex`). It mattered
   beyond the display: cell 28 is `xhci-released`, `kbd-pump` stops dead on it
   being non-zero, and port 8's PORTSC was landing there with Port Power alone
   setting it. That was blamed on firmware residue in unzeroed metal RAM, and
   `xhci-reset-handback` already zeroes 28 and 29 on the way in, so residue was
   never the mechanism. **QEMU could not have shown it: its controller reports
   exactly eight ports, so the bed sat precisely on the boundary.**

With 1 closed, rung 3 is the only open row and it is not scheduled. **Do not
propose a boot for it until its dev-box work lands.** The next thing that would
justify a body is A2b, the desktop itself on the panel, and that is red's to
plan here when a GopDesk payload has been up this ladder.

---

Release row R6: *the stick boots on real hardware.* It is the only row an
agent cannot finish, and the human body it needs is the scarcest device on
the bus (R-5). This sheet exists so that body is spent **once**.

The governing rule: **every question that can be answered before the
sitting is answered before the sitting.** What is left is the set of
questions only the target machine can answer.

Run it top to bottom. Nothing here needs an agent in the loop.

**Owner, from 2026-07-29: red owns this sheet, the stick flashing and the
sitting sequence.** fester owns A1 and builds the payloads; red decides
what flies, in what order, and what each rung has to bring home. The four
lanes' requests are consolidated in section 4 and every one of them has
been answered there, including the ones that were declined. An agent
asking "will my thing fly" should find the answer in this document rather
than in a workplan exchange.

**Attempt 1 (2026-07-29) returned one bit.** `pci-probe.img` was flashed
and the ASUS did not boot it, and because every failure path in
`option_a_stub.asm` ended at `fatal`, which is `jmp fatal`, there was
nothing to read. Two things changed as a result and both are load-bearing
below: the stub now paints two liveness colours (main 12073), and the
artifact that flies must be ON this ladder before it is flashed. The full
account is `docs/PM/Active/Stories/TheStickDidNotBoot.md`.

**ATTEMPT 2 (2026-07-29) FLEW AND THE STICK BOOTS.** Three of the four
rungs went up, all four sitting questions came back answered, and nothing
outstanding needs the board again. What each rung returned is recorded at
that rung below; the consolidated answers are in `CurrentPlan.md` (main
12170). Rungs flown and dropped, which section 6 makes red's obligation to
record:

| Rung | Flew | Outcome |
|---|---|---|
| 1 `scene-probe.img` | **yes** | PASS. 1920x1080, stride 2048, channel order correct |
| 2 `inventory.img` | **yes** | PASS. Intel I219-V `MAP=ok` with its station address, no PS/2, xHCI enumerates HID |
| 3 `msc-align.img` | **no** | Gated off by rung 2's `disk=n`, and **that gate was wrong** -- see the rung. **Its blocker is now cleared and it owes one more trip, carrying a second payload** |
| 4 `kbd-probe.img` | **yes** | Flown on rung 2's PS/2 answer as the ladder specifies. HID enumerates and delivers nothing |

**The one thing this ladder got wrong is rung 3's gate condition**, and it
is written up at that rung rather than softened here: `disk=n` was a false
negative from a defect in the instrument, so a rung was dropped on an
answer the probe could not give. It costs a trip.

---

## FLOWN 2026-08-02 EVENING: `kbd-diag-v11.img` (F0BD5738). What it brought home:

| Read | Verdict |
|---|---|
| Screen legible, `GEO w=1024 h=768 stride=1024 sc=1` | **The display defect is CLOSED on metal and the re-mode mechanism is confirmed**: AMI's console picked 1024x768 and the payload learned it. Aspect is stretched 4:3; a native-mode SetMode in the stub is the follow-up, not a defect |
| `HOST id=8086a12f run=y ports=26 slots=64 HCC=200077c1 xECP=8192` | First Intel readings ever taken. `CSZ=0` -- this part uses 32-byte contexts, measured |
| Phase 1 and 3: `EPINT=0`, `REPORT` all zero with key held, `EPCTX est=1 dq=<ring base>` | Endpoint reports RUNNING; no DMA ever landed. Single-driver (EBS ran), so the two-driver excuse is gone |
| **Phase 2: `rel=y reclaim=y` and KEYS DELIVERED** | **The firmware handback works and the PS/2-emulation fallback is REAL on this board (R-3)**. The physical keyboard, port and controller are all healthy; the delta is our periodic schedule vs the firmware's |
| `disk=n mount=n FILE-WRITES=0`, no KBDDIAG.TXT on the stick (verified at the box) | **Our MSC attach fails on the real ASMedia** even with the multi-controller walk in. OVMF passes the same image. Named defect, open |
| `ATTR disk-ctl=2760958976 walked=7f BAR=672`, ghost `CTL 2:` entry | Firmware leftovers in never-written diag cells -- metal RAM is not zeroed, every bed's is. Fixed in v12: the block is zeroed at attach |

## FLOWN 2026-08-03: `kbd-diag-v12.img` (53D85234). What it brought home:

| Read | Verdict |
|---|---|
| `EPCTX est=1` (Damian, from the glass) | **The endpoint claims RUNNING in a single-driver world with a clean face.** Configure accepted, doorbell rung, every state software can read is correct, and the controller still never fetches a TRB. The remaining question is the one nothing passive can see: whether the scheduler ever walked our ring. v13 carries the instrument that forces the answer |

## FLOWN 2026-08-03 (second flight): `kbd-diag-v13.img` (6A6FB5CC). What it brought home:

| Read | Verdict |
|---|---|
| `STOPX e=y cc=1 est=3 dq=<ring base>` (Damian: "re=3, otherwise as expected") | **The controller's own testimony: its internal dequeue never left the ring base.** Command ring, event ring and endpoint commands all work mid-pump; the transfer side never started. Combined with phase 2's delivered keys (device answers polls), the fault is between the slot doorbell and the periodic schedule |
| `re=3` (bed said `re=1`) | NOT a refused restart, and the v13 run-sheet row claiming so was wrong. xHCI 4.8.3 p.165: the Running write-back to the output context is mandatory only "before any Transfer Events are generated" -- with zero events it may lawfully read 3 forever. `re=3` therefore restates EPINT=0. The bed said 1 because the bed generates a transfer event immediately at restart, forcing the write-back; metal generated none |
| Still unread from the photo: `LATCH=` and `code=` after STOPX appeared | xHCI 4.6.9 p.134 makes a **Stopped Transfer Event (code 26/27/28) MANDATORY before the stop's command completion, even on an idle ring**. The probe latches it; it is already on the flown glass. LATCH=1/code=1A-1C means transfer-event generation works end-to-end. LATCH=0/code=00 means the xHC skipped a mandatory transfer event for this endpoint -- the transfer half of this endpoint is dead in a way even a stop cannot wake |

**Process finding, Damian's, and it stands:** every one of v10-v13's bed
gaps traces to the vm model being written from what the driver expected
instead of from the spec, and the spec was never even in the tree. It is
now: `docs/Reference/xHCI_Specification.pdf` (rev 1.2, May 2019, with
`xHCI_Specification.txt` for Grep), and the derivation of every claim above
with page numbers is `docs/Reference/xHCI_ServiceModel_Notes.md`. Reading
it found, same day: the mandatory Stopped Transfer Event (absent from the
vm), Stop-from-Halted/Error must refuse with Context State Error (the vm
accepted), MFINDEX (absent -- a bed could not tell a dead frame counter
from a live one), and **Transfer Events carrying Endpoint ID zero** (xHCI
6.4.2.1), which had the probe's EPINT row counting real bed completions as
OTH since v10. A bed arm is now written FROM a spec section, cited, or not
written.

## FLOWN 2026-08-03 (third flight): `kbd-diag-v14.img` (CDF7E707). THE FAULT MOVES OFF THE CONTROLLER.

| Read | Verdict |
|---|---|
| `SCHEDX p=00000603 pls=0 mf=+857 f1=1a` | Port in U0, powered, enabled, full-speed. MFINDEX ticking at the right rate: frames exist. And `f1=1a` = FSE code 26 = **Stopped, TD IN PROGRESS (4.6.9 p.134): the controller fetched the TRB and is issuing transactions on the wire.** The idle-ring answer would have been 27. The "scheduler never touched the ring" verdict from v13 is DEAD, killed by this instrument |
| `S2 e=y cc=1 dq2=751b40c0 f2=1a` | Same after restart: TD back in progress, still no data. Both stops answered with the mandatory event -- transfer-event generation works end to end |
| `EPINT=0`, `REPORT` all zero, key held; phase 2 (prior flights) delivers keys | **The device never completes an IN for our driver while completing them for the firmware.** With the controller now proven to be polling, the remaining delta is device/transaction state: data toggle, idle rate, protocol -- USB2/HID territory, and neither of those specs is in the tree yet. They go in before any hypothesis is coded |
| **Probe FROZE at p=2428**, ~7 s after the experiment; phases 2/3 never ran this boot | ROOT CAUSE FOUND ON THE DESK, and it was never a controller defect: **the probe's paint loop has allocated unboundedly since v1** (row texts, file body, QR matrices, every iteration, bare metal, no GC) and exhausts the heap at roughly 2,400 paints. The `-hid-nak` bed reproduced the death; one run died early enough for the OOM handler to say `OUT OF MEMORY` on COM1 in so many words; the other deaths were the same exhaustion reaching the QR encoder as a failed allocation and tripping its bounds trap (#UD). On metal COM1 goes nowhere, so it photographs as a freeze. Rule 8's exact red-flag pattern, present in every probe version, tipped over by v14's longer rows. FIXED in v15: `__heap-save`/`__heap-restore` bracket both phase loops; bed runs 13,000+ paints where 2,400 used to die |

**The USB2 and HID specs are in the tree**
(`USB_2_0_Specification.pdf`, `HID_1_11_Specification.pdf`, both with
Grep text; derivation in `xHCI_ServiceModel_Notes.md` "The device
side"). The board has answered every question the xHCI spec knows how
to ask; v15 asks the device-side ones.

## FLOWN 2026-08-03 (fourth flight): `kbd-diag-v15.img` (C12179E2). THE PIPE DELIVERS.

| Read | Verdict |
|---|---|
| **`EPINT=97` and climbing until 48 s; QR bodies visibly change with key presses** | **THE INTERRUPT PIPE IS ALIVE ON THE ASUS -- first delivery through our driver in fifteen versions.** The 97 events are the keyboard's 500 ms idle heartbeats; key data reaches the reports. SET_IDLE(0) was the killer, exactly as the F.3 derivation said |
| `DEVX f=1f cfg=1 p=0 i=125` | The device's own testimony seals it: GET_IDLE reads 125 x 4 ms = 500 ms -- the factory default idle rate, restored the moment we stopped zeroing it. All five EP0 requests answered |
| **EPINT freezes at 97 at ~48 s, pump keeps running** | **SCHEDX killed the pipe it was built to autopsy**: the 45 s Stop Endpoint pair ran against a LIVE endpoint, and on this Intel the doorbell-restart after a Stop does not resume periodic delivery. Invisible in v13/v14 (pipe already dead). Fixed in v16: the experiments fire only if EPINT is still 0 at trigger time. The non-resuming restart is a real Intel behavior worth knowing for any future driver stop path -- recorded, not chased |
| Phase countdown froze at "7 s" (~82 s wall) while p climbed past 15,000 | The PIT tick source stalls at ~82 s on metal. The paint-count fallback (p=20,000) still advances phases, so the boot is not stuck, but the tick stall is a new, real observation -- open item, not keyboard-blocking |
| QR panel re-render overpaints the old codes without clearing | Cosmetic probe bug; v16 clears the panel band before re-rendering |
| `R2:` all zeros | **Recorded as "no finding" and it was THE finding.** Corrected 2026-08-04. R1/R2 are `kd-devx`'s GET_REPORT samples over the CONTROL pipe (`KbdDiagProbe.codex:348-354`), the only report DATA this probe ever read, and they were zero. The heading above claims the pipe delivers on the strength of EPINT, which counts EVENTS. **v15 proved events arrive and GET_IDLE reads 125; it never proved data lands anywhere**, which is exactly what the desktop measured on 2026-08-04: SUCCESS on our own endpoint, buffer untouched. Same events-versus-data conflation as the SCANS gap, one layer earlier |

## NEXT BOOT: `kbd-diag-v16.img` -- the victory-lap boot

Changes from v15: experiments (SCHEDX + DEVX) gated on a DEAD pipe
(EPINT=0 at 45 s), so a working keyboard is never touched; QR panel
cleared before re-render. Reading: **`EPINT` climbing past 90 s,
`SCANS` climbing while a key is held, phases advancing 1 to 2 to 3 --
that screen closes the keyboard campaign.** If SCANS stays 0 while
EPINT climbs and a key is held, the report-decode path is the last
open item (kbd-drain), and the QR bytes carry the raw reports to
diagnose it from the photo.

## SUPERSEDED RUN SHEET (v15 flew): `kbd-diag-v15.img` -- the fix candidate flies with its own proof instruments

**Changes from v14, each bed-proven before asking for a body:**

1. **The freeze is fixed** (heap bracket, above): 100 s in the
   `-hid-nak` bed, 13,000+ paints, all three phases reachable.
2. **The driver no longer sends SET_IDLE.** HID 1.11 F.3: a boot
   keyboard shall report on EVERY interrupt poll by default; Set_Idle
   duration 0 -- which we sent at setup since the driver was written --
   is the one request that overrides that into report-only-on-change
   silence, and a quirky device may over-honor it as never-report.
   That is the exact measured shape of the ASUS (controller polls,
   device NAKs, firmware that never sends it gets keys). Proof pair
   under the new `-hid-idle-quirk` bed arm: v14 image EPINT=0 (silent),
   v15 image EPINT=1,100,004 in 30 s. Whether the ASUS keyboard is
   precisely this quirk is what the flight decides; the change is
   correct under F.3 regardless.
3. **New DEVX row** (fires ~4 s after SCHEDX, from the pump path): the
   device's own answers over the EP0 pipe that demonstrably works --
   GET_CONFIGURATION, GET_PROTOCOL, GET_IDLE, and GET_REPORT twice,
   ~1 s apart (HID 7.2.1, mandatory). `f=1f` means all five answered.
4. SCHEDX/STOPX cells moved off the 37000 band (an unnamed tenant owns
   bytes 37132+; boundary measured, not derived) onto the probe-owned
   xdiag page.

**Reading it:**

| Read | Verdict |
|---|---|
| `EPINT` climbs with a key held | **THE KEYBOARD WORKS. SET_IDLE was the killer.** The driver fix ships as-is; close the campaign |
| `EPINT=0` but `DEVX R2:` shows the held key's usage code | The interrupt pipe is still dead but the device is healthy, configured, and readable over EP0 -- **the GET_REPORT-polling fallback is proven on metal** and becomes the keyboard path in `GopUsbKbd` |
| `DEVX i=` nonzero | The device reports a nonzero idle rate nobody set -- firmware state surviving our takeover; a SET_IDLE(500ms) experiment is next, spec-cited |
| `DEVX f=` not 1f | Named EP0 requests stall on metal; the failing bits say which HID requests this keyboard refuses |
| `DEVX R2:` all zeros with key held and `f=1f` | GET_REPORT answers but empty while a key is down: protocol state suspect; compare `p=` (0 = boot as we set, 1 = report -- our SET_PROTOCOL did not take) |

## SUPERSEDED RUN SHEET (v14 flew; kept for the SCHEDX reading table)

One change from v13: STOPX grows into **SCHEDX**, the spec-derived
instrument set for the one remaining question. Same trigger (once, 45 s
into phase 1, pump path), in order:

1. **PORTSC of the keyboard's root port** (5.4.8): PLS must be U0(0). A
   port in U3(3) is suspended and starves exactly the periodic schedule
   while the firmware handback still works. One MMIO read, from the safe
   path.
2. **MFINDEX twice across ~110 ms** (5.5.1; halt rule 4.14.2 p.260): if
   the delta is 0 the controller believes every port is down and NO
   periodic pipe has frames -- keyboard silence becomes a symptom, not
   the defect.
3. **Stop #1** as in v13, now also capturing the Stopped Transfer Event
   code from the latch (`f1=`).
4. **Restart, wait ~220 ms (>> IST+ESIT), Stop #2** (`S2 cc= dq2= f2=`):
   if `dq2` moved, the restart scheduled and the loss is downstream; if
   identical to `dq1`, the doorbell-to-schedule path is dead, measured
   twice, with the mandatory-event behavior sampled both times.

| SCHEDX reads (with key held, EPINT=0) | Verdict |
|---|---|
| `pls=` not 0 | **Port not in U0 at pump time.** The schedule is starved by link state; the question becomes who moved the port and when |
| `mf=+0` | **No frames exist.** The controller thinks all ports are down (4.14.2 p.260); the keyboard is collateral |
| `pls=0`, `mf=` advancing, `f1=00`, `dq2=dq1` | Frames exist, port live, stop honored on the command side -- and the mandatory Stopped Transfer Event never came and the restart never scheduled. **The transfer/event half of this endpoint is dead while its command half answers.** That is an errata-class controller behavior; next step is comparing against how firmware configures the endpoint (its context is readable before takeover) |
| `f1=1A/1B/1C` and `dq2=dq1` | Transfer events DO generate at pump time -- the silence is purely the schedule never issuing the IN. Firmware-context comparison, same as above, but with event delivery proven |
| `dq2` advanced | The restart scheduled. The original doorbell's schedule entry was lost -- a one-shot loss, not a dead path; re-ring after configure becomes the workaround candidate |

Bed reference (codex-vm, 1920x1080): SCHEDX `p=00000403 pls=0 mf=+476
f1=1b`, S2 `e=y cc=1 dq2=<same as dq1> f2=1b`, EPINT in the millions (the
epid fix makes the bed's EPINT row honest for the first time). `dq2=dq1`
is CORRECT in the bed: its ring is drained empty at experiment time, as
EPINT proves. On metal the armed TRB is pending and unfetched, so dq
readings stay the restart discriminator; the bed's is the empty-ring
control arm, not the metal prediction. Supporting vm work, each from a
cited section: mandatory FSE on Stop (4.6.9), Stop refused from
Halted/Error (4.8.3), Endpoint ID in Transfer Events (6.4.2.1), MFINDEX
(5.5.1), and a WHP robustness fix (instruction bytes refetched via GVA
translation when the exit carries none -- first exposed by full-rate bed
event delivery).

Carried from v13 unchanged: v14 stamp on the glass, diag block zeroed,
CTL row masked, full-width rows at 1024, both withdrawn repaint
instruments stay withdrawn (SCHEDX runs from the pump path, where
`kbd-arm` and `xr-release` already touch MMIO safely).

**What flew underneath v11/v12 (all bed-proven before asking for a body;
v13 carries every item unchanged):**

1. **The display corruption has a found cause and a fix in this image.** The
   cdx-to-pe stub read the GOP geometry and THEN made its first ConOut call;
   on AMI that call activates the GraphicsConsole, which sets its own
   graphics mode, so every 2026-08-02 image painted splash-mode geometry
   (1920x1080/2048) into a re-moded scanout. That one mechanism produces
   every measured symptom: alternate lines black, glyphs stretched, long-line
   tails overpainting the next row, the dark right-edge band, StrideProbe's
   width-stepped bar shattering into exactly eight aliased copies
   (4096/gcd(7680,4096) = 8), and its stride-stepped bar photographing
   "solid" because a step of twice the true pitch is vertical too, dashed in
   a way a photo cannot resolve. Reproduced in codex-vm under
   `-uefi-conout-remode`, then cured by clearing FIRST and reading the
   geometry AFTER: same payload bytes render clean at 1920x1080/2048 and at
   1024x768/1024. The 2026-07-29/31 boots were legible because the deleted
   asm stub never called ConOut at all.
2. **This image calls ExitBootServices (stub `-Ebs`), which no cdx-to-pe
   image ever did.** Until now every ConIn-era boot ran with boot services
   alive, meaning the FIRMWARE'S xHCI driver stayed live on the Intel
   controller while ours reset and drove it -- two drivers, one controller,
   and nothing the diag reported about the Intel was a single-driver
   measurement. ConIn "working" was the firmware driver, not ours. This boot
   is the first driver-truth measurement on this board since the stub
   migration. (KeyProof and the dev console must keep boot services and must
   NOT be built `-Ebs`.)
3. **The Intel's own registers become readable for the first time.** reek's
   per-controller re-record (main 12543) is in this payload's source, so the
   HOST row finally shows `8086a12f` with ITS `run/reset/ports/HCC/CSZ`
   rather than the ASMedia's. Nothing has ever read the Intel's CSZ or
   MaxPorts on metal.
4. **The driver's two Intel-only paths are now bed-certified.** codex-vm
   gained `-xhci-csz` (64-byte contexts) and `-xhci-scratch N` (mandatory
   scratchpad array, refusal arm demonstrated): the driver passes both and
   both combined, so 64-byte contexts and scratchpad are off the suspect
   list unless the metal readings contradict the bed.
5. **New row: `EPCTX est= dq= cyc=`** reads the CONTROLLER'S OWN output
   endpoint context for the keyboard's interrupt endpoint (xHCI 6.2.3). This
   is the row that names which silence it is.

**Flight: flash (elevated), boot, hold a key during phase 1, photograph the
screen and then the QR codes after the final-counts line; bring KBDDIAG.TXT
home.** Flash, verify, pull the stick (the eject hazard is fixed at the cause; see the superseded note below).

```powershell
Get-Disk | Where-Object BusType -eq 'USB'
Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-red\build\flash-usb.ps1','-Image','<full path to img>',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\Projects\NewRepository-red\build-output\flash.log'
```

**Reading it:**

| Read | Verdict |
|---|---|
| Screen fully legible, GEO states the mode | **The display defect is closed on metal.** Whatever geometry AMI's console picked, the payload learned it after activation |
| Screen still corrupt | The re-mode happens later than ClearScreen on this firmware; photograph GEO and the corruption pattern, both carry the pitch arithmetic |
| `EPINT` climbs with a key held | **The xHCI driver delivers on Intel silicon post-EBS. The keyboard question closes** |
| `EPINT=0`, `EPCTX est=1` with `dq=` parked at ring base | The v11/v12 signature, already measured. **The STOPX row is now the discriminator** -- read it against the NEXT BOOT table above |
| `EPINT=0`, `EPCTX est=2` or `est=4` | Halted / error: the device rejected something after configure; a Reset Endpoint experiment is next |
| `EPINT=0`, `EPCTX est=0` | Configure Endpoint never took despite reporting success -- the completion-code path is the suspect, not the schedule |
| `SCANS` climbs | Full path works; anything still dead is above the driver |

## 0. Preconditions -- BLOCKING

Do not flash anything until both are true. They are red's rows, not
yours, and neither is a formality.

| | What | Why it blocks |
|---|---|---|
| **R1** | **CLOSED 2026-07-29 against main 12152.** Gate green, hard fixed point in one pass, 201.2s, `constants.hash` unchanged at 268 constants. **`build/output/Sut.cdx` is BYTE-IDENTICAL to the depot `seed/Codex.cdx`**, whole file, 2,714,156 bytes, content hash `24281e77...baff` at bytes 8-39. The seed reproduces from its source on main, so its digest `6671C19A0F78F630` may now be quoted as provenance. The `F67D4605` this row used to warn about is two seeds stale |
| **R8** | **RE-SCOPED 2026-07-29: no longer a precondition for this ladder.** See below |

**Both preconditions are resolved: R1 is green and R8 is out of scope for
this ladder, so section 0 no longer blocks the sitting.** R1 was verified on
the content hash at bytes 8-39 as well as the whole file, because a green
gate alone does NOT establish that the seed matches its source: the gate
proves `Sut === stage1`, and `Sut === seed` is the separate question this row
exists to ask.

**R8 moved, and it is not a formality that it moved.** It was blocking
because the old boot 3 flashed `seed/Codex.img`, and that rung is off the
attempt-2 ladder. **R8 is now a precondition for boot 3 only**, alongside
the ConOut gap and the missing liveness marks. Nobody should spend on it
for this sitting.

fester reports (2026-07-29) that refreshing the img today produces an image
which boots, paints the full dev console, and then prints `OUT OF MEMORY`,
described as pre-existing and confirmed against a control. **Do not record
that as a memory-exhaustion finding yet.** The boot 3 block below already
documents an `OUT OF MEMORY` from this exact payload that was a **false
report from a clobbered deck-pointer register, with the heap untouched**,
and it is written there specifically so nobody revives it. A control
showing the message appears is not a control showing the heap is exhausted;
the measurement that separates them is the heap high-water mark, not the
string. This tree's OOM diagnostics have a history here: the handler
printed to COM2 for its whole life, so a working guard read as never
firing.

So R8's real state is: **not needed now, and its one reported symptom needs
one more measurement before it is a defect.**

---

## 1. Build the artifacts (dev box)

Four images for attempt 2, **listed in the order they fly**, which is not
the order they were written. All four payloads exist. `Inventory.codex` is
built and gated as of main 12142, digest `0DC6C755...00BE3`; **the digest in
CL 12115 is SUPERSEDED and that image must not fly.**

```powershell
# Rung 1. Display, channel order, AND THE PANEL MODE. No input, halts.
# It flies first because rung 2's QR capacity depends on the mode this reports.
build/boot/build-option-a.ps1 -Src build/boot/diag/SceneProbe.codex `
    -Out build/boot/scene-probe.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx -Ebs

# Rung 2. The combined inventory probe. See rung 2 for the QR body budget.
build/boot/build-option-a.ps1 -Src build/boot/diag/Inventory.codex `
    -Out build/boot/inventory.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx -Ebs

# Rung 3, conditional. The 64 KB TRB boundary question. No input, halts.
build/boot/build-option-a.ps1 -Src build/boot/diag/MscAlignProbe.codex `
    -Out build/boot/msc-align.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx -Ebs

# Rung 4, conditional. Three timed phases, and the only WRITE evidence.
build/boot/build-option-a.ps1 -Src build/boot/diag/KbdDiagProbe.codex `
    -Out build/boot/kbd-probe.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx -Ebs
```

**`-Ebs` is now ON all four lines** (reek, 2026-08-05). It was on none of
them while the block below said the switch was the half that mattered, so
the correction lived only in prose and every copy-paste rebuilt a
two-driver measurement. None of these four payloads calls ConIn or ConOut.

**`seed/Codex.img` is NOT built for this attempt.** Boot 3 is not on the
attempt-2 ladder, for the reason in its own block below, so do not spend
`build/build-boot-img.ps1` or a flash on it.

**Pass `-Source ''` on every line.** It is not covered by `-Seed ''` and
defaults to `build-output/Codex.codex`, so a probe otherwise carries a
~3 MB `SOURCE.SRC` it never reads and doubles the image for nothing.

**Pass `-Ebs` on every DRIVER-TRUTH line, and it is missing from all four
above.** Routed by reek 2026-08-04 and corrected here: their entry said the
invocation was recorded nowhere, and it is recorded -- it is the block above,
and has been -- but **not one of those four lines carries the switch**, which
is the half that matters. Without it boot services stay alive, the FIRMWARE'S
xHCI driver keeps driving the controller under test, and nothing the probe
reports about that controller is a single-driver measurement. `ConIn
"working"` on the 2026-07-29 ladder was the firmware's driver, not ours.
`cdx-to-pe.ps1` makes the switch the discriminator: set it for anything
measuring our own drivers, leave it OFF only for payloads that call
ConIn/ConOut, which is KeyProof and the dev console. `DeskBoot` and `GopBoot`
are driver-truth and are built with it.

**Rung 3 has an artifact now** -- `build/boot/msc-align.img` (reek,
2026-08-04) -- and the chapter had existed since the ladder was written with no
image ever built from it, which is why that rung kept being "one trip away"
with nothing to take.

**It is STILL NOT ANSWERED, and I recorded the opposite here for part of a
day.** The image flew and its liveness arm read a hardcoded LBA 100000: out of
range on the 16 MB emulator image, an ordinary sector on the 28.9 GB stick. So
on metal it answered `ok=y` and the probe correctly announced that it had
proved nothing, which left the two green rows above it unclaimable. Fixed by
deriving the LBA from `md-sectors`. Corrected from reek's outbox, and the
correction is the point: **an artifact existing and a question being answered
are different claims**, and I folded the first in as though it were the second.

**A run sheet that says "this arm must read N" must also say what the arm
assumes about the MEDIUM.** reek's, 2026-08-04, and it is the third entry in
this family today alongside the absent-reading rule below. A constant that is
out of range on the bed and in range on the stick reads as a pass on the one
machine you cannot re-run cheaply, and neither number is visibly wrong on its
own. State the assumption next to the constant.

**Pass `-Kernel`.** Each build prints the kernel it used and it must say
`seed\Codex.cdx` with the seed's digest. A NOTE saying the kernel is not
the seed means the argument did not take and the artifact has no
provenance.

**Then gate the FILE you are about to flash, not one built from the same
source with different arguments.** Both of Loop A's gates, on the exact
file: the GPT structural check and an OVMF boot of the image file.
Different arguments give a different disk size, FAT geometry, file set and
partition count, and the failure modes live in the vehicle. Skipping this
is what cost attempt 1.

```powershell
build/boot/test-ovmf.ps1 -Img build/boot/inventory.img -Out probe.png `
    -UsbDisk -UsbKbd -UsbMouse -NoPs2
```

### The four images do not exist yet, and the digest is the only provenance

**Measured 2026-07-29: none of the four `.img` files exists on disk in any
workspace, including the one that built and gated `inventory.img`.** They
are build outputs, so each workspace makes its own and they do not travel.
What survived of fester's gated build is its **digest**, and that digest is
therefore the entire tie between "the artifact we proved" and "the artifact
that goes on the stick". Attempt 1's failure was flashing an artifact the
governing document did not cover; this is the same hazard one step along.

**So two checks, and neither is optional.**

**1. Rebuild `inventory.img` and confirm it hashes to
`0DC6C755B450F1A538F569E4E1C162180D52948A8FCD23E5779826032CC00BE3`.** With
`-Kernel seed/Codex.cdx` pinning the compiler, the build should reproduce
byte for byte. **If it does not reproduce, the recorded digest is not
provenance and that is a finding, not a nuisance** -- it means the recipe
does not determine the artifact, and no digest recorded at any sitting would
mean anything. Report it rather than working around it.

**2. Immediately before each flash, hash the file you are about to write.**
One line, and it is precisely the check attempt 1 did not have:

```powershell
Get-FileHash build/boot/<image>.img -Algorithm SHA256
```

Confirm it matches the digest recorded for that rung. A mismatch means you
are flashing something nobody gated. Do not proceed on the assumption that
it is "the same source" -- different arguments give a different disk size,
FAT geometry, file set and partition count, and the failure modes live in
the vehicle.

**A note for after the sitting, not for now:** `optiona-milestone.img` IS in
the depot as a tracked binary, so there is precedent for submitting these
four and making "the gated artifact is the flashed artifact" mechanical
rather than a discipline. That is a process change and it should not be made
in the hour before someone sits down.

Record the digests before leaving the dev box. They are what a later
disagreement is settled against, and without them a photograph is an
anecdote.

```powershell
Get-FileHash build/boot/inventory.img, build/boot/scene-probe.img, `
    build/boot/msc-align.img, build/boot/kbd-probe.img -Algorithm SHA256
```

### RUNG 1 FLEW AND PASSED, 2026-07-29. A1 IS ANSWERED: THE STICK BOOTS.

First successful boot of a Codex payload on the ASUS TUF. `scene-probe.img`
rendered the cube, the pyramid, the ground plane and the chrome band, with
`software 3D, no GPU` on the glass.

| Answer | Value | Consequence |
|---|---|---|
| **Mode** | **1920x1080** | Rung 2's QR budget is set by this. At 1080 high the code space is roughly 650 px against 372 at 1280x800, so Inventory should land at scale 6 rather than scraping 3. Red's split condition does not fire |
| **Stride** | **2048** | **128 pixels wider than the visible width, so this panel really does pad its scanlines.** The padded-scanline case is now METAL rather than assumed, and anything indexing rows by width instead of stride will shear on this board |
| **Channel order** | **cube blue, pyramid red** | Correct. The firmware is BGR as the stub assumes, so the unread `PixelFormat` field is not biting here. val's A6 can rely on colour |
| **Display path** | GOP linear framebuffer, painted after ExitBootServices | The Blt-only risk stays closed, now on the target rather than by inference |

**What made it boot is not in the payload.** Every earlier stick carried an
invalid GPT by the time it reached the board, because our own procedure
destroyed it: see the closing note in `build/flash-usb.ps1` and CL 12168.
Three defects together -- a partition entry array below the UEFI 16 KB
minimum, a one-sector disagreement between the two writers over the backup
array, and no volume lock during the write -- let Windows "repair" the table
into one with no readable partitions. The instruction to EJECT the stick,
which this sheet and the flasher both used to give, was one of the triggers.
A stick is now proven to survive a full remove-and-reinsert unchanged.

### RECORDED DIGESTS, all four, 2026-07-29 (fester)

Built in flight order against seed `6671C19A0F78F630` with
`-Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx`, and each FILE booted
under OVMF and confirmed painting, not merely built.

**STALE as of 2026-07-30: rungs 2, 3 and 4 must be rebuilt before anything
is flashed.** `xhci-diag` moved off the PML4 page (36200 -> 0x1D000) and
`GopXhci` is in the cite closure of `inventory.img`, `msc-align.img` and
`kbd-probe.img`, so all three digests below are superseded. Measured, not
assumed: `SceneProbe` cites neither `GopXhci` nor `GopUsb`, so rung 1 is the
one entry the change cannot reach. Re-take at flight time against the tree
state that flies, per F-d -- recording a fresh number now would only go stale
again while the fleet is active.

**RE-TAKEN after the GPT geometry fix (CL 12168), which moves every image.
The digests below supersede the earlier set, and the earlier set must not be
flashed.** Rung 1's entry is the image that actually booted the ASUS.

| Rung | Image | SHA256 | Gate flags |
|---|---|---|---|
| 1 | `scene-probe.img` | `5DC0C6C303B8288B38CED8D64DF2FDE1B48A154923B967AFDD839FDC3982D4CF` | none |
| 2 | `inventory.img` | `F2CFEE5C08A38C9DD9BD25CF4E93E68B6186106B979250192640DB57D97B481D` | `-UsbDisk -UsbKbd -UsbMouse` |
| 3 | `msc-align.img` | `0D4C2431214156F4F34E1EA8368BF2CD0EC55639A2611C0C42D195091AD5D4FA` | `-UsbDisk` |
| 4 | `kbd-probe.img` | `46CE613FBC49F03FFAD4CFE7FF33C4A05EAAEDB583D658E7EFC56338FC6E1AE2` | `-UsbDisk -UsbKbd` |

Superseded and dead: `42DE7A04`, `B41A4697`, `3DB347F1`, `6E1FDDBE`, and
`0DC6C755` before them. A digest is provenance only against a stated tree
state, and this one is main 12168.

**Check 1 is answered: the recipe DOES determine the artifact.** Inventory
built twice from identical source gave byte-identical images, and all four
digests above reproduced exactly on a later, fully-synced tree.

**The earlier `0DC6C755...00BE3` is superseded and must not be flashed**, and
the reason is a source change rather than non-determinism. `GopDraw` gained a
scanline clip at main 12148, and it is compiled into all four. Demonstrated
rather than argued: rebuilding Inventory with `GopDraw#3` restored reproduces
`0DC6C755` byte for byte, and with `GopDraw#4` reproduces `B41A4697` byte for
byte. One named file moves the digest and putting it back brings the old one
back.

**A digest is only provenance against a stated tree state.** These are
against main at 12159. Any change to a chapter in a payload's cite closure
moves them, whoever makes it, so re-take all four after any such change
rather than assuming an unrelated lane cannot reach them. What did NOT move
them, measured: blu's `pci-scan-all` at main 12147, because the compiler
emits by reachability and nothing calls it from these payloads. Reasoning
about which changes matter is how a stale digest survives; rebuilding is
cheap.

---

## 2. Prove the telemetry channel (R-1) -- before you need it

R-1 is a **precondition, not advice**: no hardware campaign launches
without an output channel that does not depend on the subsystem under
test. On a board with no serial port and no working storage, that
channel is the QR codes GopQr paints on the GOP framebuffer, photographed
and decoded back to exact bytes.

Confirm the channel works on this tree before the sitting:

```powershell
pwsh build/qr-decode-test.ps1
```

Both directions must pass -- the codes as rendered, and the codes as
photographed. Then confirm it end to end on the actual image you are
about to flash:

```powershell
tools/codex-vm.exe -kernel build/boot/kbd-probe.img -uefi -gop `
    -gop-width 1024 -gop-height 768 -headless `
    -screenshot build-output/kbd-probe.bmp -screenshot-delay 25000
pwsh tools/qr-read.ps1 -Path build-output/kbd-probe.bmp
```

It must print a `KBDDIAG v8` report. Verified this way 2026-07-28.

**A partial report is the one thing not to read past.** The decoder says
`WARNING: n of m chunks` when a code is missing. A truncated report drops
the leading lines, which are the verdict fields (`uk-ok`, `EPINT`,
`code`). Re-shoot. Do not reason from a short report.

---

## 3. The kit

- The target machine, and its firmware set to **UEFI with CSM/Legacy off**
- A USB stick. A second, different stick, because stick wear is a
  documented cause of "same image, sometimes boots"
- A phone camera. That is the whole telemetry rig
- The dev box, to re-flash between boots and to read `KBDDIAG.TXT`

```powershell
Get-Disk | Where-Object BusType -eq 'USB'      # find N -- check it twice

Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-red\build\flash-usb.ps1','-Image','<full path to img>',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\Projects\NewRepository-red\build-output\flash.log'
```

**This is the whole flash procedure. Do not write a wrapper .ps1 around
the flasher.** `-Log` writes the transcript where a non-elevated session
can read it, `$p.WaitForExit()` + exit code says pass/fail, and the log's
"Verified: all N bytes match" plus the four "fixup: verified" lines are
the evidence. Every one-off wrapper a session invents is flash-procedure
variance, and variance here has already cost trust.

`-SpecFit` refits the GPT to the stick in your hand. The flasher verifies
the whole image by readback; if it reports a verify failure, the stick is
the problem -- take the second one.

**SUPERSEDED 2026-08-02 (Damian): the eject hazard is fixed at the cause, and
this is no longer a procedural rule you can get wrong.** Two changes did it, and
both are structural rather than instructional:

1. **A proper Windows-compatible flash layout**, so partmgr has nothing it wants
   to "repair".
2. **The script takes the disk offline and holds it, the way Rufus does.**
   `Set-Disk -IsOffline $true`, then every volume on the target is LOCKed and
   DISMOUNTed by `DeviceIoControl` and **the handles are held until the physical
   write is closed** (`flash-usb.ps1`, the offline call and the lock loop below
   it). So the stick is not mounted while it is being written.

The observed result on this box: the stick does not automount on re-insertion,
and Explorer offers no Eject at all, so the action that used to destroy the GPT
is not reachable. Pull it out when you are ready.

**Kept because the history is the lesson, not the instruction.** What follows is
what this block said and why, and it was true of the tree that had it:
Explorer's Eject re-enumerates the device, and Windows
rewrites the partition table on arrival into one with no readable
partitions, which is exactly the "firmware never lists the stick" failure
arriving silently after a flash that reported success. Everything is
flushed and read back byte for byte before the flasher's last line, so the
eject buys no safety at all. After a locked write the volume is left
dismounted and there is nothing in Explorer to eject anyway.

**Three claims this section used to make are FALSIFIED and are gone rather
than reworded** (measured 2026-07-29, CLs 12166 through 12168):

- *"A stick that has been re-inserted into Windows has had its GPT
  rewritten underneath it"* and the standing order to re-flash before every
  boot. **A conforming stick now survives a full physical remove-and-
  reinsert byte-identical**, header CRC `0ee691ff` before and after. What
  destroyed the earlier sticks was our own image geometry, not the
  insertion: an entry array below the UEFI 16 KB minimum, a one-sector
  disagreement between `build-img` and `flash-usb` over the backup array,
  and no volume lock during the write. Fixed at main 12168. Re-flashing
  between rungs is still fine and still cheap; it is no longer a defence
  against anything.
- *"Without `-SpecFit`... Windows rewrites the disk on every insertion."*
  `-SpecFit` never stopped that rewrite. A stick flashed and verified clean
  by this script's own full readback came back with LBA 1 rewritten after
  one eject.
- **The disk-GUID theory is dead.** `OsHardwareRoadmap` has blamed partmgr
  caching its repair ruling by our deterministic disk GUID since
  2026-07-10 and carried a pending patch for it. The patch is now in, and
  it was falsified by the measurement it enabled: a stick flashed with a
  fresh GUID partmgr had provably never seen was rewritten byte for byte
  identically, and the GUID on the medium was untouched afterwards. The
  randomisation is kept for forensics, so each written stick is
  identifiable, and for nothing else.

---

## 3b. Before you power anything on -- FREE, and do this first

Two observations that cost no boot and no photograph, and the first has
the highest documented base rate of any failure on this board.

1. **Is the stick listed in the UEFI boot menu, in the UEFI section, not
   the legacy list?** Ten seconds. `OsHardwareRoadmap` records this
   board's legacy F12 list as HARDCODED, so its contents prove nothing,
   and USB UEFI entries as appearing only after several reboot and
   BIOS-visit cycles.
2. **If it is not listed, add the manual boot option** before concluding
   anything: BIOS, Add Boot Option, `\EFI\BOOT\BOOTX64.EFI`. The roadmap
   calls this the way to make this board deterministic.

Attempt 1's whole result is consistent with the stick never having been
selected, and this is the check that separates that from a payload fault
without spending a rung.

## 4. The ladder

**Who builds and who reads.** Settled 2026-07-29, answering fester's
question. **fester builds and gates all four images; the payload owners own
what the screen means.** The seam is that fester certifies the vehicle --
does this image boot and paint on this firmware, with `-Kernel` and
`-Source ''` honoured, the exact file gated on both Loop A gates, and its
digest recorded -- and val and reek certify the reading, which they have
already done under OVMF for SceneProbe, MscAlignProbe and KbdDiagProbe.
Nothing in rungs 2 through 4 needs a payload built: all three exist and are
proven. One agent building all four keeps one `-Kernel` discipline and one
digest list, which is what section 1 asks for. **If a build fails to paint,
it goes back to the payload owner; if it paints and the reading is
ambiguous, that is the owner's table to fix.**

Four rungs for attempt 2, each answering **named questions** (R-2), in
this order. Reassurance runs are not flights. Take a photograph at every
rung whether or not it looks interesting: the photograph costs nothing and
a second sitting costs a day.

**Read the screen COLOUR before anything else, at every rung.** Every
Option A image paints two liveness marks. They came from `option_a_stub.asm`
(main 12073, both confirmed by ablation under OVMF rather than by inspection);
that stub was deleted at B5.4 step 4 and **the surviving `cdx-to-pe.ps1` stub
paints the same two colours at the same two points**, so this table is unchanged.
Verified in source 2026-08-02 rather than assumed from the migration note:
`GopAcquire` calls `LocateProtocol(GOP)` and `GopFill $GopDarkBlue`
(`0x00202060`) follows it, `GopFill $GopDarkGreen` (`0x00104020`) follows the
page-table and handoff work. This table applies to all four rungs and is the
reason no separate control boot is scheduled:

| Screen | How far it got |
|---|---|
| Firmware's own screen, unchanged | Never loaded, or `LocateProtocol(GOP)` failed. **A boot-selection or medium problem, not a payload one** -- go back to section 3b |
| **Solid dark blue** and nothing more | GOP acquired. Died in `AllocatePages`, `GetMemoryMap` or `ExitBootServices` |
| **Solid dark green** and nothing more | Through ExitBootServices and our own page tables, and the handoff framebuffer is writable by us. Died in the payload |
| A painted report | Ran |

### RUNG ORDER CHANGED 2026-07-29: SceneProbe goes FIRST

**`scene-probe.img` is now rung 1 and `inventory.img` is rung 2.** The
reason is measured, not stylistic.

`Inventory`'s QR record is placed by `iv-codes` with `top` hardcoded at
388, so the summary occupies a fixed 388 px whatever the panel is and the
codes get whatever remains. Running that arithmetic against the payload's
own `iv-fits` / `iv-qs`:

| Panel | Code space | Max codes | Body budget |
|---|---|---|---|
| 640x480 | 52 px | **0** | **nothing decodes at all** |
| 800x600 | 172 px | 4 | 400 bytes |
| 1024x768 | 340 px | 12 | 1200 bytes |
| 1280x800 | 372 px | 14 | 1400 bytes |

**The body is 765 bytes at only seven devices, so it already exceeds
capacity at 800x600 and is annihilated at 640x480.** A Z170 presenting
~25 functions lands past the 1024x768 budget too. And it is a cliff rather
than a slope: going from two rows of codes to three costs 159 px in one
step while the space available is fixed, so the outcome is either a full
record or a partial one worth nothing.

**Device count is the second term. Panel height is the first, and it is
unknown until we boot.** That is what settles the order: SceneProbe needs
no input, cannot be spoiled by a keyboard question, renders text and pixels
rather than codes so it works at ANY mode, and it **prints the `WxH` and
stride the firmware handed us.** It removes the unknown that decides
whether rung 2's record has any capacity, and it was already a scheduled
rung, so this costs no extra trip.

So: fly SceneProbe, read the panel geometry off the photograph, then fly
Inventory with a body sized for that mode -- split by stage if the mode is
short. **Deciding this on the dev box after rung 1 beats discovering it at
the machine**, because the overflow path yields a partial report the run
sheet forbids reading past, so discovery there costs the rung and you come
back anyway.

Credit where it is due: `iv-overflow` makes this loud rather than silent.
At 640x480 it computes zero rows that fit and prints `ONLY 0 DRAWN --
SPLIT THE IMAGE` in red, so an operator learns the record is absent before
leaving the machine instead of photographing codes that were never there.

**val asked for a known-good control image as rung 1. Declined, and this
table is why.** A separate `optiona-milestone.img` boot would tell us the
board can boot some image; the colour tells us where our own payload died,
from inside the real attempt, which is strictly more information for zero
trips. fester's cancellation of that boot is correct and it stands. If a
rung shows the unchanged firmware screen, section 3b is the diagnosis, not
another rung.

### Rung 1 -- `scene-probe.img`: does the display path work, is the channel order right, and WHAT MODE ARE WE IN?

**val's one ask, scheduled unconditionally.** No input, no ceremony, no
timeout, so none of the input questions can spoil it. It renders one frame
and halts. One photograph answers four things:

| Read | Verdict |
|---|---|
| Any recognisable 3D scene | The software pipeline runs on the ASUS panel. A6's core claim |
| **Cube BLUE, pyramid RED** | Channel order is right. **Inverted means the firmware is RGB and nothing reads `PixelFormat`** |
| The printed `WxH` and `stride` | The panel geometry the firmware reported. Photograph the digits |
| Left band and bottom strip stay wash colour | Content-pane containment holds at the real panel size |

**The colour row is the point and there is no substitute for it.**
**Red/blue order is still assumed everywhere**, and every other screen we have
is light text on a dark ground where a swap is invisible. The MECHANISM behind
that changed at B5.4 step 4 and the conclusion did not, which is worth stating
carefully because the migration note routed to red said this warning was now
stale. It is not. The old `option_a_stub.asm` did not read `PixelFormat` at all
(checked at main 12095). The surviving `cdx-to-pe.ps1` stub DOES read it -- `mov
eax, [rcx+0x0C]` then `mov [rdi+0x24], eax` -- so it is published to the handoff
block. **Nothing consumes it.** `GopHandoff.codex` exposes `boot-fb-base`, `-w`,
`-h` and `-stride` and has no pixel-format accessor; `handoff-stride` takes the
LOW half of the quadword at `+0x20` and the format sits unread in the high half.
So the value is now measured, carried across the handoff, and dropped, and the
photograph below is still the only thing that answers the question. This scene has a blue-dominant cube beside a red pyramid
precisely so the two candidate answers look different. If it comes back
inverted, the fix is one `mov` and a handoff cell in a file fester has
already had open.

**Do NOT spend a rung diagnosing a partial 3D view.** val measured the
desk's 3D view photographing as ground-plane-only and then as pure sky
under OVMF, ruled out three causes offline, and established it is a
capture artifact: `GopScene` paints straight into the live framebuffer
with no double buffer and the ground is node 0, so a screendump landing
mid-frame shows exactly that. Real hardware is far faster than TCG and it
may not appear at all. If it does, it is tearing, the fix is a back
buffer, and it is not a renderer fault.

### Rung 2 -- `inventory.img`: what are the parts, and what did our stack do with them?

**FLEW AND PASSED, 2026-07-29. This rung answered sitting questions 2, 3
and the HID half of 4, plus blu's N1/N2/N3 and reek's P2, in one boot.**

| Answer | Value | Consequence |
|---|---|---|
| **Q2, the NIC** | `00:1f.6` **`8086:15b8`**, an Intel **I219-V**, rev 31, subsystem `1043:8672`, `B0=df440000` | **e1000e family, so red's driver is the right one and Track B is unblocked.** This is the field blu's whole lane was blocked on |
| **Its `MAP=`** | **`ok`** | The register window lands inside the 3 GB to 4 GB device range, so `e1000-bar-verdict` accepts it and **B3 needs no page-table change.** The dangerous `BELOW3G` verdict did not fire on the part we drive |
| **Station address** | **`78:24:af:d9:c8:23`, `AV=1`** | Read live off RAL/RAH through the vendor-and-reachability gate this sheet required. blu's N3 |
| **A second NIC** | Realtek **`10ec:8168`** at `06:00.0`, behind a bridge, **`MAP=BELOW3G`** | Not the part to drive. Recorded because this sheet asked to hear about a non-Intel NIC at once, and because it is the one device on the board whose window WOULD alias the arena |
| **Q3, PS/2** | **THERE IS NO PS/2 ON THIS BOARD.** Zero arrivals before the handback and zero after | The keyboard is USB behind firmware i8042 emulation and that emulation does not survive ExitBootServices. val's "if no" branch, so **USB HID post-EBS is the only input path this machine has** |
| **Q4, HID** | `uk-ok=y slot=1 dci=3`, `intel-route=y` | Our stack addresses and configures a keyboard on real Intel xHCI silicon |
| **Q4, storage** | `disk=n`, and **this is NOT an answer** | See rung 3 |
| **Bus walk** | **21 devices over four buses** | The depth-3 bridge walk was load-bearing, not defensive. A GTX 970, an ASMedia SATA controller and a **second xHCI** were all found and none was previously recorded |

**Two rulings on this sheet were vindicated by the same photograph and one
was made irrelevant.** Vindicated: gating the station-address read on
vendor `0x8086` **and** `MAP=ok`, because a second NIC really was present
and reading RAL/RAH off a Realtek would have yielded garbage shaped like a
MAC; and putting PS/2 last, because it turned out to be the stage with
nothing to give. Irrelevant: the QR split condition, which rung 1 answered
by reporting a 1920x1080 panel with roughly 650 px of code space.

> **HOLD CLEARED at main 12142.** The rung is ready. `iv-codes` used to run
> before the PS/2 stage with literal zeros and nothing re-rendered, so the
> record went home reading `PS2 irq1=0 poll60=0 last=00` whatever the board
> did. Fixed with a real 1620-tick window read off `xhci-tick-cell`, one
> re-render with the final counts, then endless and glass-only. The glass
> counts down (`codes refresh in Ns`) and then says `CODES REFRESHED WITH
> THESE COUNTS -- photograph them now`, so **the operator can tell whether
> the record is final. Do not photograph the codes before that line
> appears.**
>
> fester gated it on the MECHANISM rather than on the symptom, which is what
> this defect required: an ablation build with the window at 36 ticks and the
> counters seeded 7/3/5a, run WITHOUT `-NoPs2`. The decoded body came back
> `irq1=8 poll60=3 last=fa`, so a non-zero proves the re-render fired and
> irq1 moving 7 to 8 proves the re-rendered body carries LIVE counts rather
> than the seed. Three states told apart by one control. Both dev-box asks
> are also in: `top` is derived from the last painted row, and the caption
> states the body against the mode's budget.
>
> Flying digest `0DC6C755...00BE3`. **`FADB5DD2...9505` from CL 12115 is
> superseded and must not fly.**

**fester builds this and it is the one new payload for attempt 2.** It
replaces the old boot 1 (`xhci-probe.img`) and the unrecorded
`pci-probe.img` from attempt 1 with a single non-halting image that walks
the machine and paints each answer as it gets it, so the last thing on
screen is where it stopped.

**Required contents, in this stage order:**

| Stage | Carries | Answers, and for whom |
|---|---|---|
| **A. PCI** | `PciProbe`'s three call-outs (NIC class 02, STORAGE class 01, USB 0c.03), each with vendor:device, revision, subsystem, interrupt line, BARs 0/1/5 and the `MAP=` verdict. Buses walked to depth 3 behind every bridge | Sitting Q2. **blu N1** (the class-2 vendor:device, BLOCKING all of B3 and B4) and **blu N2** (its `MAP=` verdict, BLOCKING B3) |
| **A2. Station address** | Gated on the class-2 device's vendor being `0x8086` **and** its `MAP=` being `ok`: call `e1000-read-mac (mmio)` and `e1000-mac-present (mmio)` on its BAR0 and print the six bytes plus the AV bit | **blu N3.** Approved. Both functions are pure and already exist in `codex/os/kernel/E1000e.codex`. **Gate it on the vendor ID or omit it** -- RAL/RAH at those offsets are Intel-specific and reading them off a Realtek yields garbage that looks like a MAC |
| **B. USB** | `XhciTruthProbe`'s body: controller vendor:device, caplen, HCCPARAMS1, ownership handoff, **`verdict=` / `judged=` (both dwords) / `op=`**, slots/ports/reset/cnr/run/connected, Intel routing, PORTSC per port, and `ENUMERATED kbd= mouse= disk=` | Sitting Q4. **reek P2** (the BAR verdict line, free, it is already on this screen). `disk=` is the gate on rung 3 |
| **C. PS/2, LAST** | Post-ExitBootServices PS/2 arrival counters on both routes: the IRQ1 mailbox and a floating-bus-guarded port 0x60 poll, plus `last=`, the last byte seen | Sitting Q3, the one question OVMF cannot pre-answer |

**READING STAGE C, and this is the part to get right, because a counter
climbing is NOT the answer.** fester observed `irq1=1 last=fa` under OVMF
with i8042 present and correctly refused to call it a keystroke: **0xFA is
the controller's ACK.** The counters increment on any byte the controller
puts in the output buffer, so controller chatter alone makes them non-zero.
The discriminator is the `last=` byte:

| `last=` | What it is |
|---|---|
| `fa` | ACK. Controller chatter, **not a keystroke** |
| `aa` | Self-test passed. Chatter |
| `fe` / `ee` | Resend / echo. Chatter |
| `01`..`58` | **A set-1 make code. This is a keystroke and this is the answer** |
| `81`..`d8` | A break code, the release of a real key. Also the answer |

**So the operator instruction is: hold a KNOWN key and check `last=` is that
key's scancode.** Hold `A` and expect `1e`; hold `Esc` and expect `01`.
Press two different keys and watch the value change. A non-zero `irq1` with
`last=fa` and nothing else means PS/2 is wired but delivering no keys, which
is a DIFFERENT answer from PS/2 being live, and it is the answer val's A3
scope turns on. This is the operand-pair rule applied to a byte: pick the
check whose two candidate answers look different.

**PS/2 goes last and that is deliberate, not fester's original order.** It
is the only stage that hands the controller back to firmware and waits a
bounded time, so it is the stage most likely to end the run. Last means a
failure there costs nothing already gathered. It is also the only stage
with no dev-box bed at all: reek reproduced PS/2 post-EBS delivering
nothing under OVMF on q35 in a machine with no xHCI, so the emulator
cannot distinguish a QEMU i8042 quirk from our re-enable, and PS/2 is
recorded METAL on this board.

**Screen budget, and this is a real constraint on the build.** Put only
the compact summary on the glass: the three call-outs, the xHCI verdict
and enumeration lines, and the PS/2 counters. **Do not paint the full PCI
device list.** `PciProbe` already warns `devices=N ONLY M FIT ON SCREEN`
in red, and a Z170 board presents far more than six devices; on its first
real gate it said `devices=6 listed=4` while silently dropping two.

**The QR codes are the record and the screen is the convenience**, so the
full body goes in the codes. **Hard constraint: if the combined body
pushes the chosen QR scale below 3, do not ship the combined image --
split it back into two.** Scale 2 is the failure that looks like success:
the decoder finds the finder patterns and reads none of the codes. Print
the chosen scale on the glass so the operator knows to shoot close and
square at 3.

| What you see | What it means |
|---|---|
| A painted report | The payload boots and GOP works. The ladder can continue |
| `devices=0` | The config-space accessors answered nothing. A probe fault, not a bare machine |
| A device in the list but missing from its call-out | Its class code is not what was expected. **Take the vendor:device off the list** -- that is the answer that was wanted |
| NIC call-out says `NONE FOUND ON ANY BUS` in red | Record it and say so immediately. Track B has no card and B3's shape changes |
| `MAP=BELOW3G` on the NIC | **The dangerous verdict, and it reads like the milder one.** Mapped as ordinary RAM inside the arena `alloc-bytes` hands out. B3 needs a page-table change before anything else |
| `found=n` on the USB stage | No xHCI on the bus, an EHCI-only board. The USB rungs will not mean what they normally mean |
| `connected=0` with devices plugged in | Below enumeration: port power or chipset routing. Read the Intel routing line and PPC |

**What must be written down the same day, before anything else:** the
class-2 device's vendor:device and its `MAP=`, into `CurrentPlan.md`.
blu's entire lane is blocked on those two fields and nothing else on this
sheet outranks them.

### Rung 3 -- `msc-align.img`: DROPPED 2026-07-29, AND THE GATE THAT DROPPED IT WAS WRONG

**This rung did not fly because rung 2 reported `disk=n`, and `disk=n` was
a false negative.** All four devices on the Intel xHCI came back Full or
Low speed, so none of them was the boot stick: the stick is on the
**second** xHCI, the ASMedia `1b21:1242` that rung 2 discovered on the same
screen. `xhci-connect` takes the FIRST xHCI it finds and stops. So the
storage half of sitting question 4 is **UNANSWERED rather than negative**,
and A4's last open code question is still open.

**The gate condition on this rung is mine and it is the one defect in this
ladder.** I keyed a rung on a field without asking what the instrument does
when the answer is elsewhere, and the failure mode is the one this project
keeps meeting: a probe that cannot report "I did not look there" reports
"it is not there". It is the same shape as scanning bus 0 only and printing
`NONE FOUND ON ANY BUS`, which this sheet had already caught once in blu's
scan and once in fester's, and I wrote the third instance into the ladder
anyway. `disk=n` had exactly one candidate reading where it should have had
two.

**What it costs: this rung needs the board again**, which is the only item
on the whole sitting that does. Do not schedule it until
`xhci-connect` enumerates every controller rather than the first, because
until then the probe reads a controller with no disk on it and returns
`disk=n` a second time.

**THE BLOCKER IS CLEARED: reek landed the multi-controller walk, gated against
a two-xHCI OVMF bed built to the ASUS topology**, keyboard on controller 1 and
stick on controller 2, with a calibration arm capped to one host that
reproduces rung 2's `disk=n` exactly. A controller nobody opened now reads
`NEVER-OPENED` in amber rather than as a blank or as "no disk", which is the
defect this rung's gate condition was made of.

**AND THIS RUNG NOW CARRIES A SECOND PAYLOAD, so the trip answers two things.**
reek's Full-speed HID lead came back needing a READING rather than a fix: the
interval-encoding hypothesis is dead (measured, both speed classes are
spec-correct), and the four surviving candidates all turn on fields **nobody has
ever read off that board.** So the trip carries:

| Payload | Brings home |
|---|---|
| `msc-align.img` | reek's P1, the 64 KB crossing-TRB question, and `sectors=` as a free geometry cross-check |
| an endpoint-descriptor reading | The keyboard's own `bInterval` and `wMaxPacketSize`, the Interval we programmed, MaxESITPayload, the route string, and whether a transaction translator was named |

**The second one must print what the device ASKED FOR beside what we
PROGRAMMED**, so each row is a comparison rather than a value. A value alone is
satisfied by any plausible number; a mismatch is the finding. And, from this
sheet's own `disk=n` defect: **make "we did not read it" look different from
"it is zero".** A `bInterval` of 0 because the descriptor fetch failed and a
`bInterval` genuinely 0 must not photograph the same.

Both payloads need no input and neither waits, so the pair still costs one boot
and two photographs. **reek built and calibrated the endpoint reading (main
12236), so rung 3 is ready to fly on both counts.**

**ONE CAVEAT ON `KBDDIAG.TXT`, and it is about a result rather than about the
boot.** reek found `xhci-diag` aliasing the runtime cell band, and one of the
cells a USB bring-up overwrites is `fs-elevated`, the token the filesystem
syscall is gated on. `KbdDiagProbe` writes its file **after** a bring-up has set
that cell non-zero. So **a successful `KBDDIAG.TXT` may be evidence of that
defect rather than of the write path**, which is the one piece of WRITE evidence
in the whole sitting. Bring the file home as section 6 says, and do not book P3
as proven until the cell collision is untangled. **Rung 3 itself is not blocked
and nothing already photographed is in doubt** -- the corruption runs
diag-to-runtime, and every diag payload is single-core and takes no core ids.

**CAVEAT LIFTED 2026-07-30, by measurement rather than by argument.**
`xhci-diag` moved to 118784 (red, main 12283), so a bring-up no longer writes
`fs-elevated` at all -- verified on this bed, all eight runtime cells
unchanged across a bring-up that demonstrably ran. `KbdDiagProbe` was then
rebuilt on the moved base and booted under OVMF with the medium on USB
(`-UsbDisk -UsbKbd -NoPs2`): `disk=y mount=y`, phase 3/3, `SCANS=18`,
**`FILE-WRITES=12`**, and `KBDDIAG.TXT` is present in the booted image. So the
write path does not depend on the stray elevation, and P3 can be booked as
write evidence. The source says the same thing and is worth stating because it
changes what the defect WAS: the servicer sets and clears `fs-elevated` around
its own span, so the bring-up's write never enabled a write that would
otherwise fail -- it left the block-syscall bypass window permanently open,
which is a worse defect than the one feared and is now closed. The first arm of
this run is the calibration: the same probe with the medium on IDE stops at
phase 1/3 with `disk=n mount=n FILE-WRITES=0` and no file, so the check can
say no.

**reek's P1. Fly it only if rung 2's USB stage enumerated the disk**, and
drop it without hesitation if the trip budget is short: reek states it is
droppable and affects nothing else on the ladder. No input, waits for
nothing, ends in a halt loop that keeps painting, so it survives a board
whose keyboard is dead. It reads the boot stick itself, so there is
nothing to plug in.

If rung 2 says `disk=n`, every row in this probe is meaningless and the
boot must not be spent.

**Frame all five lines or the boot is wasted.** The bottom row is
`LIVENESS out-of-range lba=100000 ok=n` and it is the instrument's own
calibration: **it must read `n`.** If it reads `y` the probe can only say
yes and the two rows above it prove nothing, exactly as QEMU's silent
stderr proved nothing.

| Read | Verdict |
|---|---|
| `ALIGNED` and `CROSSING` both `ok=y` with equal checksums | A bulk TRB may cross a 64 KB boundary on this silicon. **A4's last open code question closes** |
| `CROSSING` fails, or its checksum differs | A real defect on the seed read path. The fix is a TRB split at the boundary and reek needs the spec or a second controller to write it safely |
| `LIVENESS` reads `ok=y` | The probe is not calibrated. Discard the other two rows |
| `sectors=` | Free cross-check. On the real stick it should read 60506112, which checks the `-SpecFit` geometry against what our own stack reads back |

### Rung 4 -- `kbd-probe.img`: FLEW 2026-07-29, and it produced the sitting's best lead

Rung 2 reported no PS/2, which is the condition this rung is written to fly
on, so it flew. **USB HID enumerates and delivers nothing:** `EPINT=0`,
`SCANS=0`, `REPORT` all zeros across all three phases with a key held down,
against a controller our stack had just addressed and configured.

**The instrument was checked rather than assumed, and that is what makes
the zero worth anything.** The same image under OVMF with keys injected
returns `EPINT=12 SCANS=12 last=a0 FILE-WRITES=7`, so the path can report
arrivals and the zero on metal is real rather than a blind probe. That
control is the difference between a finding and a shrug, and it is the rule
this sheet applies to the `LIVENESS` row at rung 3 and the `last=fa` row at
rung 2: **pick the check whose two candidate answers look different.**

**The lead is SPEED, and it was in a field nobody was watching.**
`speed=3` (High-speed) under QEMU against **`speed=1` (Full-speed)** on the
real keyboard. Every test this path has ever passed was against a
High-speed device. xHCI encodes interrupt-endpoint intervals differently by
speed, so a driver computing the High-speed way for a Full-speed endpoint
programs a nonsensical polling rate, and this is the symptom that produces.
**The speed difference is measured; the interval encoding is a
hypothesis.** It is testable on the dev box with a Full-speed bed and needs
no further hardware time, which is the right place for it.

Three timed phases in one boot (~90s, ~45s, then forever). **Hold a key
during each phase.** Findings render as QR codes and are also written to
`KBDDIAG.TXT` on the stick's own ESP.

**Why it is conditional and where it sits.** Rung 2 stage C already
answers sitting Q3, so this rung is no longer the place that question is
settled. Fly it when rung 2 says PS/2 delivers nothing, because then USB
HID post-EBS is the critical input path and these three phases are what
characterise it; skip it if the trip budget is spent and rung 2 answered
Q3 cleanly.

**It carries reek's P3 regardless, and that is its other reason to
exist:** `KBDDIAG.TXT` on the stick's ESP is the **only** evidence in the
whole sitting of a WRITE through the USB stack on real hardware. Mount the
stick at the dev box and bring the file home even if the screen was
uninformative.

| Phase | Question | Read |
|---|---|---|
| 1 | Does the interrupt endpoint ever deliver? | `EPINT` climbing is the verdict number |
| 2 | Does the firmware revive PS/2 when we hand the controller back? | `PS2` climbing means the fallback is real (R-3). `reclaim=y` means the BIOS re-took ownership |
| 3 | Can ownership be juggled per phase? | `reacq kbd/disk/mount` all `y` is the strongest result |

Note from reek, measured: under OVMF `legsup=n`, because QEMU's xHCI
exposes no USB Legacy Support capability, so `xhci-take-ownership` writes
nothing and phase 2 has nothing to hand back. **OVMF cannot be the bed for
that experiment. This rung is the only place it can be run.**

**Photograph the codes at the end of each phase.** Fill the frame, shoot
straight, kill the glare. Then at the dev box:

```powershell
pwsh tools/qr-read.ps1 -Path <photo>.jpg -Save phase1.txt
```

### Not on the attempt-2 ladder

**`seed/Codex.img` (the old boot 3, R6).** Still blocked, and there are now
**two** reasons rather than one.

**PARTLY ADDRESSED at main 12125, and the rung is still blocked.** fester
added five progress marks to `cdx-to-pe.ps1`'s stub, and corrected this
document's claim while doing it: the gap was never "no signal", it was
"bytes on the AllocatePages failure paths only", so the img could report a
named allocation failure and nothing else. The marks go to **both UARTs**.
**The sitting's telemetry rig is a phone camera on a board with no serial
port** (section 2), so a human at the machine still cannot tell "booted,
invisible" from "dead". Good for the local bed and for `-uefi-strict`; not
the condition. **Boot 3 unblocks on a channel a CAMERA can read** -- ConOut
text on the real screen, or GOP text out of that stub -- not on more marks.

**RESOLVED at B5.4. The half of item 2b that landed is the marks, and this
paragraph is what it closed.** The original finding, kept because the mechanism
is why the marks went to serial rather than to the glass, read: there are two
stubs and only one can paint -- `option_a_stub.asm` carries both marks and every
diag probe goes through it, while `seed/Codex.img` is built through
`cdx-to-pe.ps1`, whose stub never calls `LocateProtocol(GOP)` or paints anything
at all; so the colour table at the head of section 4 does not apply to this rung,
and combined with the ConOut gap it would return a black screen worth zero bits.

**Every clause of that is now false, and the shipping path has one stub.**
`option_a_stub.asm` and its ml64 builder `build-option-a-legacy.ps1` were
**deleted 2026-08-03**. They had been kept as the reference for the ASUS display
defect; that defect is closed (the ConOut re-mode, cured in `cdx-to-pe.ps1` and
gated by `build/boot/test-conout-remode.ps1`), and the legacy stub rendered on
that panel only because it never called ConOut at all.
`cdx-to-pe.ps1`'s stub acquires GOP and paints
both colours; `seed/Codex.img` is the GOP payload and paints under OVMF, which
fester measured and red confirmed in the stub source independently. **So the
colour table DOES apply to this rung**, and boot 3 is no longer held off the
ladder by the black-screen risk.

**The other half of item 2b, ConOut, was never routed and is OPEN.** The dev
console still does not paint on real firmware. Boot 3 unblocks on a channel a
camera can read; the colour marks are now that channel for the GOP payload, and
the dev console is a separate blocker below.

The original block, unchanged: the dev console runs on real UEFI and
paints, but its output goes to COM1 and there is **no ConOut path**, so on
real firmware the screen stays black. A human sitting down today would see an unlit screen
and could not tell it from a dead machine. The rung would spend them on a
known failure. Remove this only when the console reaches its menu under
OVMF, then re-run the ladder end to end before anyone sits down. Details
in the boot 3 block below, which is retained deliberately.

**A5, the compiler running on the box.** Not a sitting-2 ask. It needs a
working console and keyboard, so it sat behind boot 3, which was blocked
when this was written. **Both halves of that premise have moved and
neither has been re-checked here: A5 is UNOWNED and an owner for it is a
ruling Damian owes (`docs/PM/CurrentPlan.md`), and the ceremony flight
since typed the full ceremony on the ASUS keyboard.** Do not budget a
compile-on-metal rung on the strength of this paragraph in either
direction. Re-derive whether boot 3 is still blocked, and take the owner
from CurrentPlan rather than from here.

**A separate control image.** Declined; see the liveness colour table
above.

**Any link-up test, ping or traffic.** blu ruled these out themselves:
they need the driver bound and running, which needs N1 and N2 to come back
favourable first, so a result now could not be interpreted.

### Boot 3 -- `seed/Codex.img`: does the stick boot? (this is R6)

> **DO NOT RUN THIS RUNG YET, and do not flash the console stick.**
> The reason changed on 2026-07-29 and the rung is still not ready.
>
> **The reboot loop is fixed.** It was two defects in
> `build/cdx-to-pe.ps1`: `AllocatePages` asked for a fixed `0x1000000`
> that edk2 refuses and discarded both statuses, and the stub never
> filled `ram-size-addr`, the cell every panic handler relocates its
> stack to -- so the guest's own out-of-memory handler set RSP to 0 and
> triple-faulted instead of reporting. Measured under OVMF: 21 triple
> faults in 40 seconds before, **0 after**.
>
> **The console now RUNS on real UEFI firmware.** Four stub defects are
> fixed; it boots once, indexes, and paints a live header with a clock:
> `Indexed 95 definitions, 0 disk facts`.
>
> **What still blocks this rung: that output goes to COM1 and the screen
> stays black.** `uefi-con-put-text` lowers to `__serial_put`, which is
> codex-vm's blit cell or a COM1 `out`; there is **no ConOut path**, so on
> real firmware nothing reaches the UEFI text buffer. codex-vm draws the
> console itself, which is exactly why this never showed there. A human
> sitting down today would see an unlit screen and could not tell it from
> a dead machine, so the rung would still spend them on a known failure.
>
> Two earlier stories in this block were wrong and are recorded so nobody
> revives them. A `#GP` here was real and is fixed (SYSCALL was handed
> selectors the firmware GDT defines backwards). An `OUT OF MEMORY` was a
> **false report** from a clobbered deck-pointer register, with the heap
> untouched. Neither is a live defect.
>
> Two things this bought, and they are worth having before the sitting:
> the failure is now *diagnosable* rather than silent, and it is
> reproducible locally with `codex-vm -uefi-strict` without QEMU. Attach
> both serial ports when you run it -- the panic printer moved from COM2
> to COM1 at main 11837, and one attached port is how this stayed
> invisible.
>
> Boots 1 and 2 are unaffected and still worth doing on their own.
> Remove this block when the console reaches its menu under OVMF, and
> re-run the ladder end to end before anyone sits down.

The release question. Success is self-evident and needs no instrument:
the dev console comes up and you can drive it.

| What you see | Verdict |
|---|---|
| The dev console menu, navigable | **R6 passes.** Photograph it. Walk the menu, then stop |
| `Indexed 0 defs, 0 chapters` | Boots, but source loading failed -- `LocateProtocol` found the wrong Block I/O instance. R6 still passes; record it as a known defect, it is not a boot failure |
| Black screen or hang | **R6 fails.** Boots 1 and 2 are what diagnose it, which is why they came first |
| Firmware does not list the stick | Re-flash with `-SpecFit`, then the second stick. Not a boot failure until both are tried |

**Know this limitation going in:** the dev console has **no QR channel**.
It cites neither `GopQr` nor `GopDraw`, and its only output is UEFI text.
If boot 3 goes dark it cannot tell you why, and the evidence you will
have is whatever boots 1 and 2 already told you about this board. That is
the reason for the ladder, and the reason not to skip a rung that looks
boring.

---

## 5. Abort conditions

Stop the sitting, do not improvise, and bring what you have home:

- **The FIRST rung flown shows solid dark blue and nothing more:** the PE stub's
  `AllocatePages` is the leading suspect and this board may be out of
  memory below 1 MB. Nothing further on this machine is informative
- **Two different sticks both fail to appear in the boot menu**, after
  section 3b's manual boot option has been added: the problem is the
  firmware's USB path, not our image
- **The first rung never paints and section 3b was skipped:** go back and do 3b
  rather than spending a second rung
- Any rung needs a decision that is not on this sheet

**Flash, verify, PULL.** Do not eject; see section 3 for the mechanism and
for the three claims this paragraph used to make that were falsified on
2026-07-29. The short form: the GUID theory is dead, `-SpecFit` was never
what stopped the rewrite, and a conforming stick now survives reinsertion
unchanged. What is left of the rule is the useful half, and it is the half
attempt 1 broke three times: **the eject is the operation that corrupts the
table**, so end at the flasher's last line and walk to the board. If the
flasher reports a verify failure, the stick is the problem -- take the
second one.

---

## 6. What comes home

One folder per rung:

- every photograph, unedited, including the boring ones
- the decoded reports (`-Save`), one file per photograph
- `KBDDIAG.TXT` from the stick, if rung 4 flew. **This is the only WRITE
  evidence in the sitting** (reek P3)
- the four SHA-256 digests from step 1
- the firmware's own identification (vendor, version) off the setup screen

The digests are what tie the photographs to a commit. Without them a
report is an anecdote.

### Written up the same day, before anything else

These are obligations, not suggestions, and each has a lane blocked
behind it.

| Into | What | Blocks |
|---|---|---|
| `CurrentPlan.md` | The class-2 device's **vendor:device** | **All of blu's B3 and B4.** It decides whether red's e1000e driver drives this board at all. If it is not `0x8086`, say so immediately: different driver, different model, different estimate |
| `CurrentPlan.md` | The class-2 device's **`MAP=` verdict** | **B3.** `BELOW3G` means B3 needs a page-table change before anything else |
| `CurrentPlan.md` | Sitting Q3: **PS/2 present, and live post-EBS?** | **val's A3 scope, both branches.** This single answer moves a whole workstream in or out |
| `CurrentPlan.md` | Sitting Q4: `ENUMERATED disk=` | **reek's A4**, and it is the gate on rung 3 |
| `CurrentPlan.md` | Rung 1's channel-order verdict | **val's A6.** An inverted result is one `mov` in the stub |
| `red-workplan.md` | Which rungs flew, which were dropped, and why | The next attempt's ladder |

**A partial QR report is the one thing not to read past.** The decoder
says `WARNING: n of m chunks` when a code is missing, and a truncated
report drops the LEADING lines, which are the verdict fields. Re-shoot.
Do not reason from a short report.
