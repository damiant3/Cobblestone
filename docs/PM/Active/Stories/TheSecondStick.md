# The Second Stick

*A post-mortem on why a working USB stick image was presented for testing
as though it were broken. Written 2026-07-29 by reek, at Damian's
instruction, from the depot record.*

---

## 0. Correction, added after this paper was first written

**This paper answers a narrower question than the one I was asked, and I
had the wrong event in mind when I wrote it.** I learned this from
fester's `TheStickDidNotBoot.md`, which reached main at 12045 while I was
writing.

The event behind the complaint was that `pci-probe.img` was flashed to the
28.9 GB stick and **the ASUS TUF did not boot it.** That is a different
failure from the one below, and the one below does not explain it. Two
separate defects landed on the same stick on the same afternoon:

1. The flasher's verifier threw a spurious failure on a correctly written
   image. That is this paper, it is real, and it is fixed at main 12035.
2. The stick then did not boot on the target machine, and **that is
   unexplained.** fester's paper is the authority on it and its thesis is
   that they do not know why, because rung 1 was flown without an
   instrument that could report a payload which never executes.

So read this document as an account of a contributing defect in the flash
step, not as the answer to "why did the stick not boot." Where the
sections below say the perception of a broken image was caused by the
verifier, that is **overclaimed**: the verifier explains a spurious
failure during flashing, and it does not explain a machine that would not
boot. I have left the analysis intact rather than quietly rewriting it,
and section 8 already recorded that I had not established which rung was
run or what was on the glass. That caveat turned out to be the load-bearing
sentence in the paper.

Nothing in the mechanism, the arithmetic, or the run-sheet criticism below
is affected by this correction. The remedies in section 9 stand on their
own.

---

## 0a. The question, and the answer this paper actually gives

The question put to me was: **why did fester make me test a broken USB
stick image?**

The answer, stated first because that is the only honest place for it, and
scoped by the correction above:

**The image written to the stick was not broken, and the instrument that
judged it was. The run sheet then told you to read that instrument's
failure as a verdict on the stick.**

`build/flash-usb.ps1` wrote a correct image, flushed it to the device,
verified all 16,777,216 bytes successfully, and then threw an exception on
the readback of the last thirty-four sectors of the stick. The bytes on
the medium were right. The reader was wrong. And
`docs/Hardware/HardwareSitting.md`, section 3, closed with this instruction to the
operator:

> The flasher verifies the whole image by readback; if it reports a verify
> failure, the stick is the problem -- take the second one.

So when the tool failed, the sheet had already assigned the cause. It
named the medium. It did not admit the possibility that the verifier was
at fault, and there was no line in the procedure that could have led you
to the truth. You were routed into swapping hardware, re-flashing, and
spending the one resource this project calls the scarcest on the bus, on a
defect that lived in a PowerShell buffer size.

That is the whole of the answer. The rest of this document is the
evidence, the mechanism, the reason no gate caught it, what fester did
correctly, and what should change so this cannot recur.

It is worth saying plainly at the top what this document does **not**
conclude. There is no evidence of carelessness in the sense of a skipped
check or an unrun test, because there was no test to skip and no gate that
covers this file. There is a real and specific failure of judgement, and
it is not the code: it is that the same author wrote the instrument and
wrote the sentence telling the operator to trust the instrument, and
shipped the second without ever having exercised the first in the
configuration the sitting uses. I will make that case in section 6. It is
a narrower charge than "he handed me a broken image," and it is the one
the record supports.

---

## 1. What the record shows, with times

Everything below is from `p4 describe` and `p4 diff2` against the depot,
not from anyone's summary.

| Time | CL | What happened |
|---|---|---|
| 13:34:37 | fester 12009 | xHCI BAR readback: the `-16` mask investigation, answering a finding of mine |
| 13:54:38 | fester 12022 | `OsHardwareRoadmap`: a PCI config note |
| 14:01:30 | fester 12033 | **`flash-usb`: verify the SpecFit tail blobs through an unbuffered handle** |
| 14:01:39 | fester 12034 | merge down from main |
| 14:01:40 | fester 12035 | copy-up of the flasher fix to main |
| 14:10 | -- | your message to me |

The fix landed on main roughly nine minutes before you told me about it.
That interval matters: it means the defect was live in the tree at the
moment you were asked to flash, and that fester found it, diagnosed it
correctly, and fixed it under their own steam. Nobody had to tell them.

fester's own description of 12033 opens:

> The flasher wrote and flushed a perfect image, verified all 16,777,216
> bytes, then threw on the readback of the two SpecFit blobs at the disk's
> tail: 'The drive cannot find the sector requested'. Exit code 1 on a
> good stick.
>
> Cause is the verifier, not the medium.

So there is no dispute about the facts, and nothing in this document
contradicts fester's account. They wrote down what happened accurately and
promptly. The question is not what happened. The question is why it
reached you at all, and the answer to that is in the run sheet, not the
script.

---

## 2. The mechanism, exactly

The script opens one handle to the raw device:

```powershell
$diskPath = "\\.\PhysicalDrive$DiskNumber"
$fs = [System.IO.FileStream]::new($diskPath, [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::ReadWrite, 1048576,
    [System.IO.FileOptions]::WriteThrough)
```

That fifth argument, `1048576`, is a one-megabyte `FileStream` buffer. It
is there for the write, and for the write it is correct: a raw-device
write wants large aligned transfers, and `WriteThrough` plus an explicit
`Flush($true)` is what forces the bytes to the medium rather than the OS
cache.

Verification then happened in two stages through that same handle. Stage
one read back the whole image and compared it. Stage two, the one that
failed, re-read the GPT fix-up blobs (revision #3 of the file, the version
that was live when you flashed):

```powershell
foreach ($b in $blobs) {
    $off = $b.Lba * 512
    $fs.Seek($off, 'Begin') | Out-Null
    $vb = New-Object byte[] $b.Bytes.Length
    $got = 0
    while ($got -lt $vb.Length) { $n = $fs.Read($vb, $got, $vb.Length - $got); if ($n -le 0) { break }; $got += $n }
    ...
```

A buffered `FileStream.Read` does not issue the read you asked for. It
issues a read sized to its buffer and serves your request out of it. Ask
for 512 bytes and the stream asks the device for 1,048,576. On a file that
is harmless, because the filesystem returns a short read at end-of-file.
On a raw physical device it is not harmless: `ReadFile` against
`\\.\PhysicalDrive` past the final sector does not short-read, it fails,
and the failure surfaces as *The drive cannot find the sector requested*.

Now the arithmetic, taken from fester's measured numbers on the 28.9 GB
stick:

- the disk is **60,506,112** sectors
- `-SpecFit` places the backup GPT header at LBA **60,506,111**, the last
  sector on the medium
- it places the backup entry array at LBA **60,506,078**, thirty-four
  sectors from the end
- thirty-four sectors is **17,408 bytes**
- the buffer wants **1,048,576 bytes**, which is 2,048 sectors

So the verifier seeks to 17,408 bytes from the end of the device and asks
for a megabyte. It overruns the medium by 2,014 sectors and the device
refuses. It refuses *every time*, deterministically, on a perfectly
written stick, on any stick, of any size, whenever `-SpecFit` is used.

The fix is three lines of intent: read the blobs back through a separate
handle with a 512-byte buffer and `FileOptions.None`, leaving the write
path's 1 MB buffer alone. fester also added a short-read guard, because
the old loop could `break` out of a partial read and then compare a
zero-filled tail -- a second, quieter bug in the same six lines, which
would have reported success on garbage rather than failure on good bytes.

---

## 3. Why this defect was shaped exactly like the thing you were told it was

This is the part that turns a bug into a trap, and it is worth dwelling
on because it is the reason the run sheet's instruction was so costly.

The full-image verify **passed**. It reads bytes 0 through 16,777,216 --
the first 16 MB of a 28.9 GB device. A 1 MB read-ahead anywhere in that
range lands comfortably inside the medium. There is no overrun, no error,
and the stage reports success across every byte of the actual payload.

Only the tail blobs failed. And they failed because of where they are, not
because of what they contain.

Consider what that looks like to an operator. The image verifies. The
first two blobs, at LBA 0 and LBA 1, verify -- fester notes this
explicitly. Then the two structures at the far end of the stick fail to
read back. The signature of the defect is: *everything near the start of
this stick is fine, and the far end of this stick cannot be read.*

That is a textbook description of a worn or failing flash device. It is
also, almost word for word, the failure the run sheet had pre-emptively
attributed to stick wear. Section 3 of the sheet lists among the kit:

> A second, different stick, because stick wear is a documented cause of
> "same image, sometimes boots"

The project had already met genuine stick wear, had correctly written it
down, and had built an instruction around it. Then a tool defect arrived
wearing that exact costume, and the instruction fired on it. The operator
could not have distinguished them from the information the sheet provided,
because the sheet offered exactly one interpretation and it was the wrong
one.

I want to be careful not to make this sound like bad luck. It is not bad
luck that a tail-read bug mimics tail corruption; it is close to
inevitable, because both have the same observable. The lesson is not
"unlucky coincidence." The lesson is that **a diagnostic instruction that
names a single cause for a failure is only as good as the instrument's
inability to produce that failure by itself**, and nobody had established
that.

---

## 4. Why nothing caught it before you did

I checked this rather than assuming it, because "there is no test" is a
claim that is cheap to make and cheap to get wrong.

Searching the tree for anything that invokes the flasher returns exactly
two hits, both inside `build/flash-usb.ps1` itself, and both in the header
comment showing an operator how to call it. Nothing in `build/` runs it.
There is no `*flash*` test file anywhere in the repository. It is not in
`build/build.ps1`, which is the gate, and it could not be: the gate is a
text round-trip, a CDX fixed point, and the BVT, none of which involve a
block device.

So the coverage position for this file is: **zero automated coverage, by
construction.** And three properties of the defect made it unreachable by
any of the instruments this project does have:

1. **It needs a raw physical device.** A file-backed image short-reads at
   EOF and the bug does not fire. Every emulator path we own -- codex-vm,
   QEMU, the OVMF bed I was using earlier today -- consumes image *files*.
   None of them can express this.
2. **It needs `-SpecFit`.** Without it there are no tail blobs, so there
   is nothing positioned near the end of the medium to overrun from.
3. **It needs a real stick's geometry.** The overrun distance is a
   function of the device's sector count, which is why it is invisible
   until a physical device with a known size is in hand.

The intersection of those three conditions is precisely the operation
section 3 of the run sheet asks a human to perform, and nothing else in
the tree performs it. This is a clean instance of L-GAP: the suite's
silence about `flash-usb.ps1` was never evidence about `flash-usb.ps1`.
The green gate said nothing, correctly, and was read as though it said
something.

There is a sharper way to put it. The run sheet's own governing rule, at
line 8, is:

> **every question that can be answered before the sitting is answered
> before the sitting.** What is left is the set of questions only the
> target machine can answer.

"Does the flasher exit zero on a good stick?" is answerable before the
sitting. It requires a stick and an elevated shell on the dev box; it does
not require the ASUS, does not require the sitting, and does not require a
human at the target machine. It belongs to the first category and it was
treated as though it belonged to the second. The rule that would have
caught this is written at the top of the very document whose instruction
misled you.

---

## 5. What fester did right, because it bears on the answer

If this document only listed the failure it would give you a distorted
picture, and a distorted picture is not useful for deciding anything.

**The diagnosis is exemplary.** Faced with a tail-read failure, fester did
not accept their own script's arithmetic as truth. From 12033:

> Before the fix I confirmed the writes had in fact landed, by reading the
> three tail sectors back unbuffered and checking the structure rather
> than re-running the script's own arithmetic: primary
> AlternateLBA=60506111, backup header MyLBA=60506111 AlternateLBA=1
> EntryLBA=60506078, and the entry array carrying the ESP type GUID
> C12A7328-F81F-11D2-BA4B-00A0C93EC93B.

That is the right move, and it is the move I failed to make on my own work
earlier the same day. They went around the suspect instrument, read the
medium by an independent path, and checked *structure* rather than
recomputing the thing under suspicion. They established the bytes were
good before concluding the reader was bad. Then they fixed the reader,
confirmed all four blobs verify and the exit code is zero, and added the
short-read guard that closes the adjacent hole.

**The consequence was correctly identified.** The CL says why it was worth
fixing rather than noting:

> `-SpecFit` is what makes firmware list the stick at all,
> `HardwareSitting.md` tells the operator to re-flash before every boot,
> and a spurious failure there reads as 'take the second stick' in the
> middle of a sitting.

fester understood, unprompted, exactly the harm this document is about.
They just understood it after the fact rather than before, which is the
entire distance between a good engineer and a clean sitting.

**And their judgement on the neighbouring question was better than mine.**
Twenty-seven minutes before the flasher fix, fester's 12009 answered a
finding I had filed against their work. I had claimed the xHCI relocation
readback could never succeed, that a `-16` mask sign-extends, and that
therefore relocation had never once reported success -- and I had asked
them to reconcile their own hardware account against my claim. I was
wrong. fester checked before changing the doc I had flagged, established
at the instruction level that `port-in-32` emits `xor eax, eax` before
`in eax, dx` so the read zero-extends, measured both masks against the
depot seed, and ran a negative control to prove their probe could show
disagreement. They also caught a second error in my changelist that I had
not noticed. Their account needed no change; mine did.

I record that here for two reasons. It is relevant to how much weight this
post-mortem should carry, and it disposes of the reading that fester is
careless with evidence. On the same day, on an adjacent subsystem, they
were more careful with it than I was.

---

## 6. So what is the actual, narrow failure?

Strip out everything the record does not support and this is what is left.

**fester wrote the instrument and wrote the sentence that tells the
operator to trust the instrument, and shipped the sentence without having
run the instrument in the configuration the sentence is about.**

The sentence is: *if it reports a verify failure, the stick is the problem
-- take the second one.* It does three things, and the third is the
damaging one:

1. It tells the operator the flasher's verdict is authoritative. Defensible
   as a default.
2. It tells the operator what to do about a failure. Good procedure.
3. **It tells the operator what a failure means.** That is a diagnostic
   claim about a tool, and it was never tested.

The first two are procedure. The third is an assertion with no runner,
which is the failure mode `LESSONS.md` describes for `CLAUDE.md` itself:
every line an assertion, nothing evaluating any of them, and unevaluated
assertions rotting exactly the way this project documents everywhere else.
That sentence was such an assertion, sitting in the highest-consequence
document in the tree -- the one a human executes with their hands, without
an agent in the loop, on the scarcest device on the bus.

The correct form of that instruction, absent any evidence about the
flasher's own failure modes, is something like: *a verify failure means the
readback disagreed with what was written. That can be the medium or it can
be the verifier. Re-run once; if it fails identically in the same place,
suspect the tool, because a worn stick fails erratically and a bug fails
deterministically.* That version is honest about what is known, and it
contains within it the discriminator that would have saved your afternoon:
**this defect is perfectly reproducible and stick wear is not.** Two runs
on the same stick would have distinguished them, and the sheet asked for
one.

It is worth naming why that instruction was written the way it was, as
generously as the evidence allows. The sheet is built around minimising the
human's time. It is dense with pre-assigned verdicts -- read the tables in
section 4 and every row maps an observation to a meaning, so the operator
never has to stop and think. That design is right, and it is why the sheet
is good. But pre-assigning a verdict is only safe where the mapping has
been established, and for the flasher's failure it had not been. The same
instinct that makes the document usable is what made this line dangerous.

---

## 7. The pattern, which is bigger than fester

Three times in one day, on this project, a confident instrument reported
something false and a competent reader believed it. Two of those three were
me.

**One.** The flasher reported a verify failure on a good stick. The reader
believed the medium was bad, because the doc said so.

**Two.** I reasoned from source that a `-16` mask sign-extends and
concluded that relocation had never worked on any revision. I read the
code correctly and reasoned from it incorrectly, and I filed the
conclusion against another agent's hardware account as though it were
measured. It was not measured. fester measured it and it was false.

**Three.** Later the same day I queried QEMU's monitor for the xHCI BAR
twelve seconds into a boot, read `0xFE800000`, and treated that as the
firmware's placement. It was our own relocation write. I had sampled a
register *after my own code modified it* and called the value the
firmware's, then built two further hypotheses on top -- including a
non-existent compiler bug -- and spent two rebuild cycles on them. The
firmware's actual value was above 4 GB, which I only established by making
the diagnostic distinguish "never judged" from "judged zero."

The common shape is not incompetence. In all three cases the instrument
answered promptly, the answer was plausible, and nothing in the answer's
form disclosed how it could be wrong. `L-FALSIF` in the lessons index says
an instrument that cannot fail is not evidence. These are the adjacent
case, and it deserves its own line: **an instrument that fails in only one
way, and a document that tells you what that one way means, is a machine
for producing confident wrong conclusions.** The flasher could fail for
two distinct reasons and reported both identically. My diag cell could
mean two distinct things and rendered both as zero. In each case the fix
was the same in kind: make the instrument's output distinguish its own
failure modes.

That is why I do not think the useful reading of today is "fester handed me
a bad image." The useful reading is that this tree currently has several
instruments whose failure modes are undocumented and untested, one of them
sitting directly under a human's hands, and that the cost of that lands on
the most expensive line in the plan.

---

## 8. What it cost

`L-HUMAN`: a step requiring a human body is the most expensive line in the
plan, and should be minimised the way heap is minimised. The run sheet
opens by acknowledging this -- release row R6 is *the only row an agent
cannot finish*, and the sheet exists so that body is spent **once**.

What this defect spent:

- a flash cycle on a 28.9 GB stick, which is minutes, not seconds
- the swap to the second stick, per the instruction
- a second flash cycle, which failed identically, because the defect is
  deterministic
- the operator's confidence in the artifact, which is the expensive part

The last item is not recoverable by a fix. Once the tooling has told you
the medium is bad and it was lying, every subsequent green from that tool
costs you something to believe. That is the real damage and it is why this
document is worth its length.

I should be precise about what I have and have not established here. I know
the defect was live in the tree in your window. I know the run sheet told
you to blame the stick. I know fester found and fixed it at 14:01. I do
**not** know from the record which rung you ran, what exactly appeared on
your screen, or whether you swapped sticks. If what you saw was a verify
failure or a take-the-second-stick moment, this document is the
explanation. If what you saw was different -- a board that would not list
the stick, or a payload that booted to a black screen -- then the cause is
elsewhere and I would want to know what was on the glass before I said
another word about it. Section 9's remedies hold either way.

---

## 9. What should change

Ordered by how much each one buys.

**1. The run sheet must stop assigning a cause it has not earned.**
Replace the sentence in section 3. State what a verify failure means
(the readback disagreed), name both candidate causes, and give the
discriminator: re-run once on the same stick, and a byte-identical failure
in the same place indicts the tool, not the medium. This is a two-line
edit to `docs/Hardware/HardwareSitting.md` and it is the whole of the protection.

**2. Give the flasher a rehearsal step, on the dev box, before any
sitting.** The run sheet already has a precedent for this and it is a good
one: section 2 proves the QR telemetry channel end to end *before* it is
needed, on the actual image about to be flashed, because a channel you
have not exercised is not a channel. The flasher deserves the same
treatment and does not have it. One flash of one probe image to the real
stick, checked for exit code zero, added to section 1 alongside the digest
recording. It costs one flash cycle at the desk and it converts this
entire class of failure into a dev-box problem.

**3. Make the flasher distinguish its own failure modes.** A verify
mismatch and a read error are different events and should not surface as
the same exception. A failure to *read* the medium should say so, and
should say that it may be the tool. The fix fester landed is correct but
it repairs this instance; the shape stays repairable-once until the error
text separates "the bytes differ" from "I could not read the bytes."

**4. Consider whether the tail-blob verify wants to exist at all in that
form.** The blobs are re-read through a second handle now, and that works.
But the structural check fester performed by hand during the diagnosis --
confirm `AlternateLBA`, the backup header's `MyLBA` and `EntryLBA`, and
the ESP type GUID -- is a stronger statement than a byte compare against
the script's own computed blob, because it validates the thing firmware
will actually parse rather than validating that the script agrees with
itself. That is `L-ORACLE`: a harness validates only the half it points
at, and a byte compare against your own arithmetic points at the half you
already have.

**5. Do not add any of this to the gate or the battery.** It is operator
tooling, it needs a physical device, and it belongs in the run sheet's own
pre-flight, not in `build.ps1`.

---

## 10. The lesson, for the index

Proposed for `docs/PM/Active/Stories/LESSONS.md`, in that file's shape:

| id | lesson | stories | runner |
|---|---|---|---|
| L-BLAME | A procedure must not tell the operator what a tool's failure MEANS unless that tool's own failure modes have been exercised. Naming one cause for a failure the instrument can produce by itself routes the operator away from the truth. | TheSecondStick | none |

The adjacent lesson, which `L-FALSIF` half covers and which today argues
should be explicit: **make an instrument's output distinguish its own
failure modes.** The flasher rendered "bytes differ" and "cannot read" as
one exception. My xHCI diag rendered "judged zero" and "never judged" as
one zero. Both cost hours, and both fixes were a few lines at the point of
report.

---

## 10a. How this sits beside fester's account

`TheStickDidNotBoot.md` (fester, main 12045) is the paper on the primary
event and should be read first. The two do not conflict, and between them
they cover different halves of the same afternoon:

| | this paper | fester's paper |
|---|---|---|
| Event | the flash step reported failure on a good image | the ASUS did not boot the flashed stick |
| Status | cause found, fixed at main 12035 | unexplained, hypotheses enumerated |
| Root criticism | a doc asserted what a tool's failure MEANT, untested | rung 1 was flown with no instrument for a payload that never executes |
| Lesson | L-BLAME, proposed here | L-ORACLE, arriving in the form fester did not plan for |

The common root is worth naming because neither paper alone shows it:
**both failures are a reporting channel that could not express the thing
that happened.** The flasher could not say "I could not read the medium"
as distinct from "the bytes differ." The probe could not say "I never
executed" as distinct from anything at all. In both cases the operator was
left holding an observation that had exactly one available interpretation,
and in both cases that interpretation was wrong or empty.

---

## 11. In one paragraph, if this document is ever compressed

This paper covers the flash step only; the stick's failure to boot is
fester's `TheStickDidNotBoot.md` and is unexplained. On the flash step:
the image was good and the verifier was broken. `flash-usb.ps1` re-read
the `-SpecFit` GPT blobs at the last thirty-four sectors of the stick
through a handle carrying a 1 MB buffer, so the read overran the end of
the medium and the device refused it, deterministically, on any good
stick. The full-image verify passed because it never reads near the end,
which made the defect look exactly like tail wear. `HardwareSitting.md`
had already ruled that a verify failure means a bad stick, so the sheet
converted a tool bug into an instruction to swap hardware, and a human's
afternoon was spent on it. fester diagnosed it correctly, went around
their own instrument to prove the bytes had landed, fixed it at main
12035, and closed an adjacent short-read hole in the same change. The
narrow failure is not the buffer size: it is that a document asserted what
a tool's failure meant, and nobody had ever made that tool fail.
