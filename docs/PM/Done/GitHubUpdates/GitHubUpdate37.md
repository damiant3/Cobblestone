# GitHub Update 37

**Scope: main CLs 10469 to 12646, 2026-07-24 to 2026-08-03.** Update 36
covered 8916 to 10468. Every number in this report was measured on
2026-08-03 at main 12646.

**That is 589 changelists in ten days**, counted with `p4 changes` over
the range rather than subtracted. An earlier draft of this file estimated
1,200 from the distance between two changelist NUMBERS; numbers are
consumed by five dev streams, and a changelist that never copies up
consumes one without ever reaching main. The first correction in this
report is to the report.

---

## The headline: we pointed the instruments at the instruments

**Better than fifty of those changelists describe something that was
believed, was written down, and was not true.** Not bugs found by users,
and mostly not bugs found by tests. They are things found by going and
looking at code that every existing test already agreed was fine.

That count is a keyword match over changelist descriptions, catching
phrases like "was wrong", "never ran", "silently", "nothing observes",
"stale" and "unverified". It is a floor, not a census: a changelist that
fixed a false belief without saying so in those words is not in it, and
the method is stated here so the next reader can judge the number rather
than inherit it. Naming the pattern beside the count is itself one of this
cycle's lessons, learned by publishing a survey that had searched for the
wrong shape and was wrong by an order of magnitude.

That is a strange headline for a release cycle and it is the honest one.
The compiler had already been a hard fixed point of itself on bare metal
since April. The green stayed green through every one of these. What
changed is that we stopped treating green as evidence and started asking,
per claim, *what would have caught this if it were false?*

The answer was usually nothing. Some of the findings below are ordinary
bugs. The interesting ones are the cases where the thing asserting
correctness was the same thing being asserted about.

---

## One defect shape, found twice, three weeks apart

A `when` arm ending `is otherwise -> ...` is exhaustive by construction.
Add a variant to the type and the compiler's exhaustiveness checker has
nothing to say, because the catch-all already covers it. The new variant
takes the default path silently.

It happened twice in this cycle, in unrelated subsystems:

- **`^` was never implemented in x86-64 codegen.** `emit-binary-op` had no
  arm for `IrPowInt`, so it fell through to the catch-all -- which returns
  RAX with the state unchanged, meaning the program compiled clean and the
  answer was whatever happened to be in a register. The operator is in the
  language, in the guide, and in the type checker; the emitter simply did
  not have it. Fixed, and the arm is now enumerated above that catch-all.
- **Adding `KeyEcdsaP384` to `X509KeyAlg` broke every P-384 certificate.**
  Four tests that enumerate their variants raised CDX2070, which is the
  checker working. `x509-key-params-ok`, whose EC arm ends in a catch-all,
  raised nothing -- so the new variant was checked as though it were RSA,
  the curve OID was refused, and certificates stopped parsing three stages
  downstream of the cause.

The second one is the more useful account, because the diagnostics were
*loud in four places and silent in the one that mattered*. A checker that
covers the enumerated cases and cannot see the catch-alls gives exactly
that pattern, and the pattern reads as "the compiler is on top of this".

The rule the tree now runs on: when you add a variant, grep every `when`
on that type and read the catch-alls by hand. The compiler will not.

---

## What an independent oracle found that the test suite could not

Three defects in this cycle were found by comparing our answer against a
foreign implementation's, and none of them was reachable from a test
written against our own output.

- **`movmskpd` dropped its REX prefix**, so a mask query could read the
  wrong register. Found by `build/oracle-vector.ps1`: **13 of 130 cases
  disagreed with the host CPU.** Our own tests agreed with our own
  emitter.
- **`Real trapping` and `Real saturating` both ignored infinity.**
  Trapping emitted a NaN check (`ucomisd` plus `jnp` over a `ud2`), which
  detects NaN and says nothing at all about an infinity. Two declared
  safety modes, neither doing the thing its name is the entire promise of.
- **`markdown` ordered lists were never recognised**, and the list number
  was discarded.

This is the same lesson Update 36 spent its headline on, arriving from a
different direction. A decoder checked only against its paired encoder is
checked against nothing. A codegen checked only against its own test
expectations is checked against nothing. **The oracle has to be something
that did not come from here.**

---

## Brotli resurrected

Update 36 carried the deletion of the compression stack. This one carries
its return, and the seven days between them are the clearest
before-and-after this project has.

**What was deleted on 2026-07-19** was a Brotli implementation that passed
every test in the tree and had been reported as working across five
sessions and three days. Its encoders were genuinely real: .NET decoded
our output across fourteen cases. **Its decoder could not read a single
byte produced by any other implementation on earth**, and nothing in the
suite was pointed in that direction, so nothing said so. The account is
`docs/PM/Active/Stories/BrotliBeatsOpus.md`, and the lesson the tree took
from it is that a harness validates only the half it points at, so you
write the direction you do not already have **first**.

**It came back on 2026-07-26 with that direction written first.** The
restoring changelist moves the read side from **0 of 4 to 4 of 4** against
.NET quality-11 streams, with the expectations computed host-side from the
original bytes rather than from anything of ours. A later case takes it to
**5 of 5**. The write side stayed at 14 of 14 accepted by .NET, with a
corrupted stream still refused.

**The ratio improved as a side effect of the decoder work, which is the
tell that something had been wrong rather than merely unfinished.** Output
went from 114.0 per cent of .NET's byte count to 106.3, and then to 105.7.
The first eight points came from a single discovery: **the matcher had
inherited Deflate's 258-byte match cap from RFC 1951**, a constant
belonging to a different format that had never been questioned because it
produced valid output. Brotli's own limit is 16,779,333. It is a parameter
now, and Deflate still gets 258.

**Then a real encoder's output arrived and broke it twice, and the first
defect hid the second.** .NET packs 4 MB into one meta-block, so a stream
with several of them only exists if you flush between writes:

- **`MNIBBLES == 3` announces a metadata meta-block, not a length.** Our
  reader took the following 28 bits as a length. A 2 KB input became **3 GB
  of heap**. The same block also aligns to a byte boundary even when empty,
  so handling it but resuming on the next bit decoded 2,023 bytes of 6,000.
- **The distance ring buffer is per stream, not per meta-block.** With the
  first defect fixed, resetting the cache per meta-block decoded **exactly
  6,000 bytes, every one of them wrong** -- right length, wrong content, no
  error raised anywhere. That is why the harness compares a hash instead of
  a length.

**Neither was reachable from our own encoder**, whose multi-meta-block
output reads back perfectly and therefore proved nothing. This is the same
shape as the deletion, one level in: a test that round-trips through your
own implementation is testing that you are self-consistent.

**The closing beat is the best evidence in the whole episode.** The
dictionary, the 121 word transforms and both context lookup tables had all
been *reverse-engineered by probing .NET*, at a time when nobody on the
project had a copy of the specification. RFC 7932 is now in the tree, and
it publishes a CRC-32 for each of those tables. **All four matched first
time**: dictionary `0x5136cb04`, transforms `0x3d965f81`, Lut0
`0x8e91efb7`, Lut1 `0xd01a32f4`. An independent derivation agreeing with
the published standard is better evidence than either one alone. The
checker that says so self-tests its own CRC against the standard vector
before it judges anything, and was confirmed to fail both on a corrupted
copy of the RFC's appendix and on a single flipped byte of our own table.

The other half of that finding is less flattering and is recorded next to
it: **four of the traps written up as hard-won discoveries are single
sentences in the specification.** Read the spec before you probe an oracle.

**The encoder is now frozen at 105.7 per cent of .NET and the freeze is the
finding.** Half of one day went into closing that last 5.7 per cent before
somebody said out loud that ratio work is a direct competition with
Google's compression team. Two levers were built and both were refuted with
measurements that are kept: a clustering re-seed that produced
**byte-identical output on all fourteen cases**, and a context mode that
loses to the simpler one under good clustering. A host-side study had
predicted the opposite, and it was wrong because it costed all 8,000 bytes
as literals when the encoder's histogram is built only from the runs the
matcher actually leaves.

**And nothing in the tree calls Brotli.** The only non-test consumer of the
compression quire uses Lz4. The reason to have built it anyway is the one
sentence worth taking from this whole episode: **encoders are optional and
decoders are not, because you do not get to choose the format someone
else's data arrives in.**

**One honest limit on all of the above.** Those figures were measured on
2026-07-26 and the chapter has had no code change since, only prose. The
tests that compare against .NET -- the interop test and the read test,
which are the two that caught the original disaster -- **are hand-run and
have no caller in any gate or battery**, because they need .NET on the
host. The in-tree unit tests do run. So the claim that our decoder reads
foreign streams is true, was properly established, and is currently
re-checked by nobody. Giving those two a runner is worth more than the
remaining 5.7 per cent.

---

## Authentication: two curves, two digests, and no gate that could see it

ECDSA reached P-256 and P-384 under both SHA-256 and SHA-384: four named
entry points, four X509Chain dispatch arms, no feature flag between them.
The real github.com certificate chain verifies through its P-384 root.
RSASSA-PSS verification landed with MGF1.

**TLS and DTLS peer identity became a requirement rather than an option.**
`subjectAltName` parsing and RFC 6125 matching landed with no commonName
fallback, and -- this is the load-bearing half -- an expected name is now
*required* by both `tls-ep-client-new` and `dtls-ep-with-anchors`, with
both transports routed through `x509-verify-peer`. Before that, checking
which host a certificate was actually for was something a caller could
simply not do. A chain that walks to a trusted root tells you the
certificate is genuine; it does not tell you it is genuine *for the host
you dialled*, and those are different questions.

**Seven trust anchors ship**, each taken from two independent distribution
paths and compared byte for byte: ISRG Root X1 and X2, and DigiCert Global
Root CA, G2, G3, Trusted Root G4 and High Assurance EV.

The two elliptic-curve roots -- DigiCert Global Root G3 and ISRG Root X2,
both P-384 -- were deliberately held out until this cycle, and the reason
is the useful part. They parsed perfectly well the whole time. X509 typed
their keys `KeyEcdsaOther`, `x509-can-verify` answered False, and an anchor
holding one would have sat in the set matching nothing. **An anchor that
can never match is worse than a missing one, because it reads as
coverage.** P-384 verification landing is what made them admissible.

That distinction is now a line in the test rather than a paragraph in a
file. `codex/test/web-chain` asserts *supplied*, *parsed* and *can-verify*
as three separate numbers, because the first two cannot tell a usable root
from a decorative one: making `x509-can-verify` answer False for P-384
moves the can-verify count from 7 to 5 and leaves "supplied = 7, parsed =
7" untouched. It also walks a live www.digicert.com chain to Global Root G2
with the hostname checked, and runs offline, so it is in the gate.

One honest note on the sourcing doctrine. Six of the seven roots were
compared between the build machine's Windows root store and the issuer's
own published copy. **ISRG Root X2 is not in that store at all**, so its
second path is the Mozilla NSS bundle -- a different distributor rather
than a second mirror of the same issuer. Two paths that share an origin are
one path wearing two names, which is the failure the doctrine exists to
prevent, so which two were used is recorded per root.

One finding inside that work is worth stating on its own, because it is a
conformance defect that a plausible test would have missed. Under a SHA-384
digest, FIPS 186-4 takes **the leftmost min(N, outlen) bits** of the hash --
not the digest reduced modulo n. The test that caught it caught it only
because it asserts that a genuine signature must VERIFY, and requires the
wrong-digest entry point to FAIL. Without that second half the file passes
unchanged if both entry points quietly hash SHA-256.

**And until this cycle, no gate observed any of it.** TLS 1.3 with X.509
authentication, two curves, two digests, RFC 6125 hostname matching with no
commonName fallback, an interop run against three OpenSSL servers -- all of
it verified by hand, once, by whoever wrote it. Eight tests now run in the
standing gate. **About four seconds** is what it cost to stop taking the
whole authentication story on trust.

The same changelist closed the last gap in the dispatch: CertificateVerify
now accepts `ecdsa_secp384r1_sha384`, and the ClientHello offers it, which
is not an optional pairing. Accepting a scheme we never offered is
accepting a signature we never agreed to.

---

## The stick boots

**On 2026-07-29 a Codex payload ran on real hardware: an ASUS TUF desktop,
booted from a USB stick, no operating system underneath it.** The first
rung of the ladder rendered its scene on the board's own panel. That is the
single largest thing in this report.

The panel came back **1920x1080 with a stride of 2048** -- 128 pixels wider
than the visible width. This board pads its scanlines, so anything indexing
rows by width rather than by stride shears. Nobody had a machine that did
that before; every emulated bed in the project has stride equal to width.
Channel order was correct off the glass (a blue cube beside a red pyramid),
which also settled a separate open question about an unread `PixelFormat`
field.

**What made it boot was not the payload.** Three earlier attempts failed,
and every one of them reached the board with an invalid GPT.

**To be exact about who wrote the bad bytes, because "the stick got
corrupted" invites the wrong reading: Microsoft Windows 11 wrote them.** Not
Codex, and not the payload. The corruption happened on the build host, in
the window between our flasher finishing and the stick reaching the board,
which is a window in which no Codex code is running at all. Codex only ever
read that medium, at boot, on a different machine.

**Windows rewrites the partition table when the device re-enumerates**, and
the flasher's own last printed line was the thing that made it re-enumerate:
"Eject the stick, then boot the target". Following that instruction
destroyed the GPT the script had just verified. Measured on 2026-07-29, and
the damage is specific rather than vague: `PartitionEntryLBA` moves from 2
to 2047 (a sector of zeros), `LastUsableLBA` moves 60506077 to 60506109, the
backup array is repointed 60506078 to 60506110, and the header CRCs are
recomputed over the new values **while the array CRC is left stale**. Both
GPTs then fail validation, and Windows itself reports the disk as MBR.
Firmware sees no partitions, which is exactly the "the firmware never lists
the stick" symptom from the first attempt.

**This was isolated with a control rather than inferred.** Same stick, one
flash: three consecutive raw reads returned byte-identical, correct GPTs
(`EntryLBA=2`, header CRC `c25e02bc`). The operator then used Explorer's
Eject and nothing else -- **the stick was never physically unplugged** -- and
the next read showed the rewrite. Reads are harmless. The eject is not.

So the honest split of the three defects is: **one of them is Windows doing
something uninvited, and two of them are ours for not defending against it.**

- **Ours:** a non-conforming GPT. The entry array sat below the UEFI 16 KB
  minimum, and `build-img` and `flash-usb` disagreed by one sector about
  where the backup array starts.
- **Ours:** no exclusive volume lock during the write, so Windows was free
  to mount the volume and write to it while we were writing to it. A flasher
  that does not lock is racing the machine it runs on. It now takes
  `FSCTL_LOCK_VOLUME` and `FSCTL_DISMOUNT_VOLUME` on every volume of the
  target disk, by access path rather than by drive letter, and holds the
  handles for the whole write -- an EFI System Partition has no drive letter,
  so locking by letter silently locks nothing, which is what the first
  version of that code did.
- **Windows':** the re-enumeration rewrite above. There is no defence
  against it beyond not triggering it, so the script now shouts "PULL THE
  STICK OUT. DO NOT EJECT IT."

Two explanations were tried first and **both were ruled out by measurement,
which is why they are printed rather than dropped**: it is not physical
reinsertion, and it is not the deterministic disk GUID that a roadmap had
blamed for weeks. A stick flashed with a freshly randomised GUID that
Windows' own partition manager had provably never seen was rewritten
identically.

Three post-mortems came out of this and they are in `docs/Stories/`:
`TheStickDidNotBoot`, `TheSecondStick` (the flasher reported a broken image
when the image was good), and `TheImageThatWasTwoDaysOld`. The through-line
is that a human being was spent, three times, on defects that lived in the
procedure rather than in the product. The rule the tree now runs on is that
the most expensive step in a plan must not be protected by the weakest guard
in the tree, and prose is the weakest guard there is.

---

## Four questions, four answers

A hardware sitting is expensive and rare, so the run sheet fixed the
questions in advance. All four came back:

1. **The stick boots.** Above.
2. **The NIC is an Intel I219-V** (`00:1f.6`, `8086:15b8`), with its MAC
   read live off RAL/RAH. A **second** NIC nobody knew about (Realtek
   `10ec:8168`), plus a GTX 970, an ASMedia SATA controller and a **second
   xHCI controller**, none previously recorded: **21 devices across four
   buses**, which is why walking bridges rather than bus 0 turned out to be
   load-bearing.
3. **There is no PS/2 on this board.** The keyboard is USB behind firmware
   i8042 emulation, and **that emulation does not survive
   ExitBootServices**. Zero scancodes before the handback and zero after it.
   USB HID is the only input path the machine has.
4. **USB HID enumerated and delivered nothing.** The device was found, the
   slot addressed, the route right, and then no interrupts and no scancodes
   with a key held down. That one took another five days and has its own
   section below.

One negative that turned out not to be a negative: the sitting reported no
disk. All four devices on the Intel controller are Full or Low speed, so
none of them is the boot stick, which is on the *other* controller -- and
the enumerator took the first xHCI it found and stopped. **The storage half
of question 4 was unanswered rather than answered no**, which is the same
defect shape as scanning bus 0 only and reporting NONE FOUND. Both are now
fixed.

---

## The keyboard, and every instrument that was lying about it

**Closed 2026-08-03, on the board, through our own driver.** The keyboard's
interrupt endpoint delivers: the endpoint interrupt counter climbs while a
key is held, and the key data reaches the reports.

The cause was ours and it was one request. HID 1.11 F.3 obliges a boot
keyboard to send a report on EVERY interrupt poll by default. `SET_IDLE`
with duration 0 is the one request that turns that guarantee off. Our driver
sent it at setup, since the driver was written, and this keyboard honours it
as "never report". The firmware never sends it, which is why BIOS setup
always typed fine and we read that as the firmware's keyboard working. The
fix is deleting the call. The device confirmed it itself: `GET_IDLE` read
back 125 (x4 ms, the factory 500 ms default) the moment we stopped zeroing
it.

That answer took sixteen probe versions and five trips to the machine, and
the reason is the part worth publishing: **every instrument pointed at the
problem was wrong in a way that agreed with us.** An earlier draft of this
report published an interval-encoding hypothesis, stated as a hypothesis.
It was wrong, along with four others.

- **The emulator was written from the driver instead of the spec, and no
  spec was in the tree.** The xHCI, USB 2.0 and HID 1.11 specifications now
  live in `docs/Reference/` as PDFs with extracted text for Grep, and every
  claim derived from them is cited to a section and page in
  `xHCI_ServiceModel_Notes.md`. Reading them found four violations in our
  own emulator in one day: no mandatory Stopped Transfer Event on Stop
  Endpoint (xHCI 4.6.9 p.134), Stop accepted from Halted/Error where the
  spec demands a Context State Error (4.8.3 p.164), Transfer Events carrying
  Endpoint ID zero (6.4.2.1) -- which had the probe's own delivery counter
  misfiling real completions for six versions -- and no MFINDEX register at
  all (5.5.1), so no bed could tell a dead frame counter from a live one.
  **The standing rule this earned: a bed arm is written FROM a cited spec
  section, or it is not written.** An arm derived from the driver can only
  ever agree with the driver.
- **The emulator could not model the failure, so no run could reproduce
  it.** The stock emulated keyboard always answers. Arms that model the
  pathology (`-hid-nak`, a keyboard that NAKs forever; `-hid-idle-quirk`,
  one that over-honours SET_IDLE) put the board's exact behaviour on the
  desk, and every remaining defect fell in hours instead of flights.
- **The driver assumed the speed identifiers in PORTSC** rather than reading
  the Protocol Speed ID dwords off the Supported Protocol capability, as the
  specification requires. Found by reading the spec with no failing test in
  hand. Under a controller that publishes its own PSI dwords, a hub-attached
  keyboard enumerates and is then never serviced. `TT Think Time` was not
  initialised on High-speed hubs either. **The fix carried a defect of its
  own**, and the way it was caught is the useful part: the Slot Context
  Speed field takes the *controller's* PSI value, not the class we resolved
  it to. That was introduced in the fix and found on the bed that had found
  the original defect, by a different lane, and corrected the same day.
- **The diagnostic ran the machine out of memory and photographed as a
  freeze.** The probe allocated on every repaint, bare metal, no GC, since
  its first version; it died at about 2,400 paints. Bracketed with heap
  save/restore it now runs past 13,000.
- **The diagnostic's own experiment killed the pipe it had just proven
  alive.** A Stop Endpoint issued against a *working* endpoint does not
  resume periodic delivery on this Intel part, so the instrument silenced
  the keyboard 48 seconds after proving it worked. Intrusive experiments now
  fire only when the symptom is actually present.
- **The battery was testing a compiler six days and two seeds stale.** A
  workspace had inherited a kernel left behind by an earlier `-CodexCdx`
  run; the harness computed its digest and wrote it to the rollup, compared
  against nothing. It reported eleven failures that were all the antique
  compiler missing features the tests use. `test.ps1` now names the compiler
  it is testing at the top of every run, refuses a kernel that is neither
  the depot seed nor the last build, and restores the working kernel after a
  substitution.
- **The depot seed had not been rebuilt in 700 changelists**, so a large
  body of compiler work had never been live. Rebuilding it exposed exactly
  one latent defect, and the honest headline is that the staleness hid it,
  not that the rebuild caused it.
- **A hand-encoded instruction had one wrong bit for months.** The helper
  behind `uefi-read-key-ex` parks the deck pointer across a firmware call;
  the save was written as raw bytes and encoded `mov r15, rdx` where it
  meant `mov r15, r10` -- REX.R clear -- so the exit restored the deck from a
  register that never held it. Latent while `poll-key` read the PS/2 cell
  first; the moment that order was flipped to ask firmware first it fired on
  every poll and took six keyboard tests down. It is encoded through the
  `mov-rr` mnemonic now, which cannot get the bit wrong.
- **And the emulator could not decode an instruction that straddled a
  page.** WHP hands over only the bytes on the page the exit was taken on,
  so a two-page instruction arrives truncated and cannot be sized. A 68-byte
  code size change moved one LAPIC store to the last byte of a page, the
  startup IPI that brings up every application processor was reported as
  unmapped MMIO, no core ever started, and the guest read as a broken SMP
  scheduler. It presented as a compiler regression and was bisected as one --
  reverting a cache-attribute change did not help, reverting an unrelated
  keyboard rewrite did, and keeping that rewrite while padding it back to
  its old size also did. **Behaviour identical, layout restored: that is the
  signature of a host decode gap, not a codegen defect.** The same gap
  reached the crash-dump path and raised a modal Windows dialog that blocked
  an unattended battery until a human clicked it; codex-vm now suppresses OS
  crash dialogs entirely and prints the fault to the captured log.

The method that survived all of this is written down for other people's
hardware in `docs/Designs/Active/Tools/HardwareBringUpPlaybook.md`, with the
flyable diagnostic image and its digests in the root `README.md`.

---

## The emulator was the defect, twice more

Two further findings were fixed in the wrong place first, and the pattern
is the same both times: the guest was correct and the model it was tested
against was not.

- **codex-vm sign-extended every 32-bit port read.** A driver mask was
  changed to compensate, and that change was then **withdrawn** when the
  real cause surfaced: a 32-bit `in` clears the upper half of the register
  on real silicon, so the guest had been right all along. Two lanes
  diagnosed it independently within the same hour, and it now has a
  regression test that pins the width.
- **The e1000 model had no PHY at all.** The I219 is a PCH-integrated MAC
  reachable only through MDIC, and the model was granting `STATUS.LU` (link
  up) unconditionally, so a driver could "bring up the link" against
  something with no link to bring up.

The general form is uncomfortable and worth publishing: **a device model
written from the same reading as the driver tests the reading, not the
device.** Both of these were found by looking at the hardware documentation
rather than at a failing test, and neither could have failed in the bed.

---

## Cross-architecture: parity, and the word matters

arm64 and RISC-V got substantially more real in this cycle -- roughly 40
changelists touch the cross lanes, including eleven transpiler plugs whose
builtin lookup was a binary search over a list that was not ordered the way
it searched.

**The claim this release makes about them is PARITY, not correctness**, and
the distinction is load-bearing rather than rhetorical. A known failure
residue remains on both lanes. One example is precise and worth printing
because it shows the failure mode: list pattern matching desugars to
`__list-len` / `__list-head` / `__list-tail`, which neither plug implements.
Until 2026-07-28 both plugs **compiled such a program clean** and the call
fell through to an undefined symbol -- a binary that printed nothing on
riscv64, or faulted on every line on arm64, with no diagnostic anywhere.
Both plugs now refuse it with `[UNSUPPORTED]`.

A refusal is a feature. Silence is the bug.

**The per-lane test figures are not re-measured in this report and should
not be quoted from it.** The ARM64 135/135 and RISC-V approximately 132 are
counts carried forward from June, and there are open rows describing
shared-lowering failures on both lanes plus a failure class that landed on
2026-07-28. Re-measuring them needs a Renode run on the cross lane, which
did not happen this cycle. The word is right and the numbers are older than
the work. (The neighbouring claim of 53 plugs was checked and is correct: 56
directories under `codex/plugs`, less `common`, `test-input` and
`test-output`.)

---

## The documents were wrong about themselves

A large share of this cycle's changelists correct a document against the
code it describes, and the direction of the error is consistent: **every
stale claim found was stale in the reassuring direction.** The work was
already done, or the bug already fixed, or the gap already closed.

- `CLAUDE.md` gained **rule 12: prose about our own code is banned.**
  Column-2 literate prose competes with the code as a source of truth and
  loses while still being believed. The measured trigger: a prose block
  asserted that a frameless `int-mod` and `math-mod` both need a
  non-negative correction. `math-mod` is the truncating remainder and must
  not be corrected; its own body is four tokens long and settles the
  question the paragraph got wrong. An agent read the paragraph instead of
  the body and wrote the error into a changelist description.
- **Rule 11's own technical argument was false in every mechanical
  particular** and was rewritten. It had claimed the em-dash has no CCE code
  point and vanishes at the I/O boundary. Measured: `from-unicode 8212`
  answers **41464**, and it round-trips exactly. The rule stands -- the
  em-dash is banned because it is a model tic and not house style, which was
  always the real reason -- but the argument it rested on had been wrong for
  as long as it had existed, in a file every agent reads every session.
- **A checker had been passing for thirteen days on a claim that was
  false.** `check-cdx-registry.ps1` verifies that every diagnostic code the
  compiler can raise is registered, and it counted a **column-2 prose line**
  -- a comment reading "the ONLY raise of cdx-missing-cite was deleted" -- as
  the raise site itself. The checker now skips prose and scans the host's own
  raises. It ships with four sabotage arms, one of which is a calibration
  that **reproduces the original false green**, so the failure it missed is
  now a case it is proven to catch. The same changelist carried the first
  substantial application of rule 12: **304 of 362 prose lines removed from
  the compiler's entry chapter, zero code lines lost.** The two halves belong
  together. A comment that can hold a checker green is not documentation, it
  is an unversioned assertion sitting in the same file as the thing it
  describes, and losing.
- A workplan whose own header says "Status, not journal, keep it under ~80
  lines" had reached **434 lines**, most of it finished work and war stories.
  Another reached 384. Both were cut. A file's own header is an assertion
  with no runner behind it.
- `p4 copy` with multiple paths corrupts a target silently; the process doc
  now says so.

Recorded beside these as a standing hazard: **host tooling that re-derives
what the code already knows** will drift from it silently, and at least one
other script in `build/` is the same class.

The general form, and the reason it belongs in a public report: **an
assertion nobody re-runs decays, and it decays toward comfort.**
`build/build.ps1` never reads a line of prose. Nothing re-tests a sentence
in a guide. The project's answer is not to write fewer documents but to give
the load-bearing claims a runner, and to keep an index of the ones that do
not have one yet.

---

## Three tests the standing gate could not see

A release battery run came back with three failures, each re-run alone to
rule out contention. **None of the three was visible to `build/build.ps1`**,
which is the gate every change must pass, and all three had been red on main
for days.

All three were the same shape: **a test's expectations and the source that
prints them are two files, and only one of them gets reviewed.**

- One test called a function that had been deleted along with the user
  interface element it hit-tested, so it no longer compiled at all.
- One asserted panel coordinates that a later widening moved out from under
  it.
- One had two lines added to the code that prints them and not to the file
  that pins them.

All three are fixed, and the first was fixed by ruling on the behaviour
rather than by deleting the assertion: the close box really is gone from the
desktop shell, so the test now pins the invariant whose absence was the
underlying defect (**the window is sized from its own wrapped text**, so a
paragraph that grows cannot spill onto the desktop behind it), with the old
fixed-height layout fired as its negative control. The battery has since
been re-run green; the numbers are below.

---

## The desktop became something you can run

The bare-metal desktop shell stopped being a screenshot and became a build
artifact: a one-command dev-box run mode puts it on the glass from a plain
binary in about a second and a half, with a recorded golden frame, a Files
pane that browses a real EFI system partition, and a 3D scene rendering into
a back buffer inside its own content pane. Word wrap, a shutdown button
wired to the ACPI path that had been written months earlier and never
called, and a palette taken from three exact brand colours rather than a
generic terminal theme.

Two rendering defects were found by measuring the frame rather than by
looking at it, both overruns: text running past the right edge of a panel,
and a hex dump wider than the window containing it. A third, a 3D view that
allocated about 20 KB per frame permanently because its loop had no heap
bracket, was found the same way.

---

## Also in this cycle

- **The debugger's command surface closed.** Address expressions
  (`ExprEval`), a disk inspector, memory search and region compare, symbolic
  breakpoints, and a serial bridge that mirrors console commands and answers
  to COM1. Every menu item either does what it says or reports honestly why
  it cannot.
- **The repository protocol's store layer**: source-as-facts,
  import-by-hash, authenticated removal, replay resistance, and
  reconciliation over the wire. A store that could not prove two facts were
  the same used to keep one per kind -- which would have destroyed every
  source definition but one.
- **Networking reached a real address.** The emulator's NAT never answered
  DHCP, though its manual said it did; it does now, a DISCOVER went out on a
  wire, and lease renewal landed with the monotonic clock it needed. The
  driver seam carries either card, and a service takes its MAC from whatever
  card is bound rather than from a constant that was hardcoded in dozens of
  files.
- **The dev console runs on real UEFI firmware.** `SYSCALL` had been handed
  selectors the firmware's own descriptor table defines in the opposite
  order, which is a class of bug that cannot reproduce under a bed that
  builds its own tables.
- **A run of application-layer defects found by measurement rather than by
  report**: `SafeTensors` built a tensor whose shape disagreed with its data
  at every rank but two; `ImageTensor` darkened every image it
  round-tripped, 250 of 256 values down and none up; grid snapping was a
  full cell wrong for every negative coordinate; a text caret was drawn
  outside its widget on the GPU renderer.
- **Low-memory cell collisions were mapped rather than patched.** Two
  subsystems had quietly claimed overlapping addresses; the hole now has a
  recorded map with two named tenants and the bounds that defend it. A
  related finding: **one address means two different things depending on
  which builder produced the image**, and nothing at the call site says
  which, so the same source is correct under one and silently wrong under
  the other.
- **A memory cell that only the loader writes reads exactly like a cell
  reporting "nothing happened".** It was quoted as evidence that a heap was
  untouched, and another lane planned against that. The heap had advanced 23
  MB and the live value was in a register the whole time.
- **The language settled a parsing rule people kept tripping on**: the minus
  binds to whatever it abuts, so `list-push acc -1` is two arguments and
  `a - 2` is subtraction. The guide had documented the opposite.
- **Rule 11 finished tree-wide.** Zero em-dashes in tracked files.

---

## The tree, measured 2026-08-03 at main 12646

| | |
|---|---|
| compiler | 63 chapters, 57,498 lines |
| foreword | 430 chapters, 81,663 lines |
| apps | 1,010 chapters, 233,366 lines |
| all `codex/` | 2,282 chapters, 380,275 lines |
| chapters under `codex/` and `apps/` | 3,292 |
| chapters carrying prose | 2,640 |
| prose lines | 54,912 |
| tests with a pinned expectation | 1,184 |
| tests carrying a `.skip` | 26 |
| standing gate (`bvt.ps1`) | 73 compile tests, 133 assertions, 19.6s |
| trust anchors shipped | 7 |
| seed `seed/Codex.cdx` | 2,710,900 bytes, digest `9DCE330256566B2A` |

Line counts are physical lines, counted with `ReadAllLines` per file. An
earlier draft said the compiler was 48,456 lines, because PowerShell's
`Measure-Object -Line` counts only non-blank lines and nothing about the
call site says so. Prose lines are lines indented exactly one space under
`codex/` and `apps/`. **Two earlier drafts of this table disagreed about
that last figure by 11,000 lines**; both are superseded by the single
measurement above, taken with the method printed beside it.

**The seed reproduces from its source and the digest is quotable.** The gate
proves the compiler is a fixed point of itself; it does not prove that the
artifact in `seed/` is the one that source produces, and this repository has
a documented history of conflating the two. Measured separately:
`build/output/Sut.cdx` and `seed/Codex.cdx` are **byte-identical over the
whole file**, not merely on the content hash, and the depot copy hashes
identically to the workspace copy. The full-file hash is not printed because
signature bytes always differ; the content hash is bytes 8 through 39 and
that is what is compared.

## The release proofs

Three independent proofs were run at this head, in a workspace force-synced
clean from the depot:

| | |
|---|---|
| full battery, all tiers | **1,403 tests, 1,359 pass, 0 fail, 44 skip** |
| application sweep | **267 units, 261 clean, 0 regressions** |
| poison build (uninitialized-field safety) | **1,403 tests, 0 fail** |

The poison build recompiles the tree against a seed whose free memory is
filled with a marker byte rather than zeros, so any code depending on the
allocator's zero-fill fails instead of working by accident. Update 36's
cycle found a real defect that way. This one found none.

---

## Honestly open

- **The Intel I219-V driver is the longest item still open.** The card is
  identified and the emulated model now has the PHY path it was missing, but
  the rings, the link bring-up and the transmit and receive paths are absent
  on the real part.
- **26 tests carry a `.skip`.** They were last triaged 2026-07-27, and of
  four probed by hand, three reasons were stale -- one was hiding a compiler
  miscompile that had been fixed and left buried behind its own skip. A
  skipped test proves nothing, and a skip reason nobody re-reads proves less.
- **The cross-lane test figures are stale.** Parity, not correctness, and
  the counts date from June. A Renode run is what closes this.
- **The two Brotli tests that use a foreign decoder have no runner.** They
  need .NET on the host, so they sit outside the gate and the battery and
  are run by hand. They are also the only two instruments that can catch the
  exact failure this quire was once deleted for, and the last time anyone
  ran them was 2026-07-26.
- **The store cutover has not started.** The repository protocol's store
  layer works; nothing has been migrated onto it. Peer resolution and the
  multi-disk story are both unowned.
- **`codex-vm.exe` is still roughly 6,000 lines of C**, and the project
  intends to delete it. A pure-Codex VMX host is designed and unowned.
- **The full battery is still not run on an agent's initiative**, by design.
  It is an hour long and almost never fails, so it is a human's instrument.
  What that means honestly is that the routine gate is the BVT plus the fixed
  point, and the battery is a release-time proof.
- **The anchor set is seven roots and must not grow into a root program.**
  That is a standing constraint rather than an open item: Mozilla, Apple,
  Microsoft and Chrome each ship a list running to well over a hundred
  authorities, any one of which can issue for any name in the world, and
  adopting such a list would import exactly the trust model the founding
  document sets out to replace.
