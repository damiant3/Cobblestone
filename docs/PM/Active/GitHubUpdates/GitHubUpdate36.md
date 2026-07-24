# GitHub Update 36 -- 2026-07-24

Covers main CLs 8916-10468 (Update 35 covered through 8915). The detailed
sections below were written 2026-07-19 and cover the first 124 changelists
(8916-9386); the addendum at the end covers the arc from there to the
release head, where the middle-end campaign closed and the last
poison-masked defect was found and fixed.

The headline is a subtraction. The largest single body of work in this
cycle was a compression stack that reached RFC conformance, was validated
by independent decoders, passed every test in the tree, and was then
deleted in one changelist. The reason it was deleted is the most useful
thing in this update, and it is not "the code was bad."

The rest: a compiler that grew a retargetable low-level IR, a kernel that
talks to real USB devices across four endpoint types, a dozen places where
the compiler stopped quietly lying, and a memory fix that took one phase
from 254 MB to 11.6 MB.

---

## The headline: we built half a codec and called it a codec

Across roughly forty changelists, `Brotli` went from byte-aligned framing
with a Deflate payload to genuine RFC 7932: a real bit-stream, commands
carrying literals and copies, three prefix codes, run-length code lengths,
context modelling, block splitting, the 13,504-word static dictionary
recovered from a live oracle, all 121 word transforms, eight literal
trees, and finally distance block types. `Zstd` got real LZ sequences, an
FSE coder, and Huffman-coded literals inside sequence blocks. `Deflate`
learned to choose a block type per block instead of per stream.

Every one of those was measured, costed against not doing it so it could
not lose, and validated by an independent implementation: .NET's
`BrotliStream`, python `zstandard`, python `zlib`. The interop harnesses
had negative controls proving they could fail. The numbers were real. On
the last measurement our Brotli beat .NET's own encoder on one case.

**And the decoders could not read anything that came from outside.**

Handed four streams produced by .NET at quality 11, `brotli-decompress`
returned zero bytes. Four out of four, refused at the first bit, because
the reader accepted only the one window-size field our encoder happened to
write. Behind that sat four more subsettings, each of which desynchronises
silently rather than erroring.

The cause is a single question that was never asked. The interop harnesses
all asked *can a real implementation read our output?* None of them asked
*can we read a real implementation's output?* Those are different
questions, they validate different halves, and only one was ever put. A
decoder checked solely against its paired encoder is checked against
nothing: the two halves can share any assumption at all and agree forever.

That blind spot paid out three times in this cycle before anyone looked at
the blind spot itself. Context mode 2 was being ignored by the reader, so
third-party streams using it decoded to wrong bytes (ten oracle-built test
streams; the old reader got ten of ten wrong, exactly inverted). The
distance-block-type count was read and then never used. Then the window
field. Each was found, fixed, and reported as a fix. The fourth discovery
was that the harness would never have found any of them.

Seven chapters were deleted: `Brotli`, `BrotliDict`, `BrotliDictIndex`,
`Deflate`, `Fse`, `Gzip`, `Zstd`, with their tests, their generators and
their format notes. Fifty-five files. `Huffman`, `Lz77`, `Rle` and `Lz4`
survive; they are standalone primitives with their own tests and never
depended on the deleted set. The compiler was unaffected -- gate green,
hard fixed point in one pass, `constants.hash` unchanged.

Codex has no general-purpose compressor and no standard container format.
It never did. What it had was an encoder, and reports that called it a
codec.

The full account, with the changelists quoted, is in
`docs/PM/Active/Stories/BrotliBeatsOpus.md`. The operative lesson for
anyone rebuilding it: **write the reverse test first, and scope the item by
capability rather than by format feature.** Five feature items closed on
schedule while the capability stayed missing, which is exactly why five
consecutive "done" reports produced no convergence.

---

## The compiler grew a retargetable low-level IR

The middle end from Update 35 was x86-64 only. This cycle made it a target
description rather than a hardcoded machine.

The register file became data (LIR step 1), then the target descriptor was
proven against both ARM64-shaped and deliberately narrow register files
(step 2), then the retarget route was answered and found viable (step 3).
`LirTarget` validation and a second-target pin closed BACKLOG 3.13 and
3.14 by fixing them rather than by rewording them.

Two integrity fixes matter more than the feature work. **A pass list can no
longer silently drop a pass**: `passes=+name` is additive and
`run-ir-pipeline` reports the pipeline it actually ran. And the LIR dump
pin was recording an ablated program, because default passes were being
removed by `-Passes` -- the pin now records what ships.

BACKLOG 3.17 was excised rather than closed: the live-range hole was built,
measured, and does not pay (`my-gcd` 158 to 159 instructions). That is a
result, not a gap, and it lives in `LIR.md` section 9 where the next person
will find it before rebuilding it.

Also closed by fixing: **3.9**, CDX whole-program dead-code elimination --
emit now prunes unreachable definitions, and the seed lost 412 definitions
and 140,575 bytes. **3.8**, the emit-boundary prover carries proven ranges
through `emit-expr`. **3.12**, the checker derives `int-mod`'s range from
its divisor.

---

## The kernel talks to real USB hardware

Four endpoint types now work against a real device model, and each one was
found broken by making it actually run.

**Endpoint zero, then bulk, then interrupt, then isochronous.** The last
one reads a UVC camera frame over Isoch IN; codex-vm had been copying the
data and signalling nothing, so both halves needed fixing and
`usb-cam-frame` pins both. A two-tier hub transaction translator landed
with it, and codex-vm grew a second hub tier to test against.

**The mass-storage driver had never touched a device.** It ran on a stub
bulk pair and a stub descriptor read. Making it run against real sectors
surfaced four defects at once: both Bulk-Only Transport signature
constants were wrong (hand-converted from hex, one byte off each, so real
hardware rejects every command block), `INQUIRY` text decoded ASCII wire
bytes as CCE code points, two descriptor walks had no zero-length guard
and hung, and a stale transfer event was taken for a command completion --
re-addressing a configured slot and wiping its endpoint contexts. The
existing test had the signature bug pinned in its `.expected`.

The duplicate kernel USB stack was retired: `Xhci`, `UsbMassStorage` and
`UsbVideo` deleted with eleven tests, 1,432 lines of driver. `Usb` and
`UsbAudio` survive for `UsbHid` and `codex/os/dev`.

---

## The compiler stopped lying in a dozen small ways

This is blu's cycle and it is mostly deletions.

**`read-file` the builtin is gone.** It read serial, discarded its path
argument entirely, and beat the `FileSystem` effect operation at the call
site. Twenty-five transpiler emitters were retargeted to `read-text`,
which had no entry in any of them.

**Cross-chapter name collisions reached zero** in the compiler unit, and
`CDX3006` reports any new one. The diagnostic found five live plug
collisions on its first run.

**ASCII character codes where CCE was meant.** CCE space is 2, not 32;
digits are 3..12, not 48..57. `text-to-upper` and `text-to-lower` were
ASCII-only and did nothing at all in CCE, so letters passed through
unchanged.

**Escape roots, BACKLOG 2.1.** The blocker was never the call sites:
`emit-self-type-table` filtered `__self-type-defs` against a hardcoded
root list, so `CompileChecked` and `IRChapter` missed lookup and the walk
answered zero escapes -- indistinguishable from clean. Proven live, CHECK
went 0 to 5,158 and LOWER 0 to 899.

Also: `[Process]` is a carriable capability; `CDX2068` rejects a field
assignment whose write cannot be observed; `CDX3007` stopped false-firing
for every chapter that declares no definitions; string interpolation was
removed from the tree; and `row-set-by-name` exists because
`row-set-value` and `catalog-delete-row` were documented and did not.

---

## Memory: one phase went from 254 MB to 11.6 MB

`copy-sx-text` was rematerializing every text instead of sharing durable
ones, which `copy-sx-token` and `copy-sx-span` already did and which its
own prose already claimed it did. Its base parameter had been threaded
through thirty-six copiers unread.

PARSE-KEEP deck: **254 MB to 11.6 MB.** Headroom on the 384 MB floor went
from 1.59x to roughly 33x. LOWER is now the tightest deck at 1.20x and is
the row to watch.

---

## The accident reports moved into the read path

Seventeen post-mortems -- keyboard campaigns, silent definition drops,
confident wrong diagnoses, an agent communication autopsy -- had been
filed in `docs/PM/Done/Stories/`. `Done/` is the archive, and the session
initialization procedure explicitly instructs agents not to read it.

They are now in `docs/PM/Active/Stories/`, and `CLAUDE.md` and the init
skill both say that everything under `docs/PM/Active/` is read at every
session start.

This is not incidental to the compression story above. `TheSilentKeyboard`,
filed six days before the Brotli work went wrong, names the exact failure:
*"Validation that cannot fail is not validation; it is reassurance."* Its
probable cause is a campaign validated entirely against an instrument known
to be unable to reproduce the failure. `AgentLinuxInferno`, three months
earlier, describes a function that silently swallowed 269 definitions while
the compiler declared a verified fixed point -- *"each broken stage
producing the next, identical in their incompleteness, a perfect circle of
confident wrongness."*

Both were in the directory nobody read. There are eighteen now.

---

## By the numbers

| | |
|---|---|
| Changelists | 124 (main 8916-9386) |
| Compression chapters | 11 to 4 |
| Files deleted in the compression CL | 55 |
| Seed | 2.37 MB, hard fixed point, one pass |
| PARSE-KEEP deck | 254 MB to 11.6 MB |
| Seed definitions removed by CDX DCE | 412 (140,575 bytes) |
| Duplicate USB stack removed | 1,432 lines, 11 tests |
| Accident reports now read at init | 18 |

---

## What's next, and what is honestly open

**Compression is a hole, deliberately.** Nothing in the tree compresses
general data. Whether that gets rebuilt is an open decision, not a planned
increment. If it is rebuilt, the oracle test comes first and runs in both
directions.

**Deflate, Gzip and Zstd were never checked in the read direction either.**
Their harnesses were read at their final revisions and confirmed
one-directional. Whether their decoders could read foreign streams was
never determined, and the code is now deleted, so it cannot be determined.
That is stated because the alternative is implying a check that did not
happen.

**The register is growing again.** It was cut from 69,403 characters to
roughly 46,000 at CL 9036 on the rule that an entry states what is missing
and nothing else. It measures 78,595 characters over 61 entries today. The
rule did not change; adherence did.

**The LIR retarget is mid-route.** Steps 1 through 3 are done and Route A
is viable. ARM64 and RISC-V are not yet on it.

**The batteries.** A green gate is still not the absence of work, and the
full battery is still not run on an agent's initiative.

---

## Addendum, 2026-07-24: the middle end closed, and a poison-masked defect surfaced

The sections above stop at CL 9386. The arc from there to the release head
(CL 10468) has two headlines: a campaign that ended as a measured negative,
and a foundational bug that had been hiding behind the allocator's zero-fill
since the beginning.

**The x86-64 codegen-quality campaign closed as a negative.** The LIR
linear-scan selector reaches the emitted binary and is instruction-neutral
to +1 against the tree emitter across the nine benches. The question was
whether named-binding register allocation could beat the tree
*meaningfully*. The answer, measured, is that it cannot on this register
file: the spills that remain are the file, not the allocator (one program
under three register-file descriptors spills 13, 6 and 0 slots at 2, 4 and
10 callee-saved registers, and its peak simultaneous call-crossers is 6
against x86-64's pool of 4, so at least two values must live in memory
whatever the allocator does), and v1's loop-free single-def LIR has no
live-range holes to sharpen. The closing note is `LIR.md` section 12, which
exists so the next person does not re-run the investigation. The ARM64 and
RISC-V LIR retarget was dropped in the same decision: it was sprawl on
boards with no users, and it was never scoped.

**The platform-wide backlog register was retired.** `docs/PM/BACKLOG.md`
had become prose that nothing checked, with stale rows quoted as fact. It
was deleted. Application-domain registers (`apps/<app>/<app>-backlog.md`)
are unaffected; the shape and priority order now live in
`docs/PM/CurrentPlan.md`.

**A release battery, triaged honestly, found ten real defects among
eighteen failures**, all fixed. The sharpest was a miscompile: the emitter
skipped a join jump after a branch by reading the byte five back from the
code frontier and asking whether it was `0xE9` (a five-byte `jmp rel32`) --
but `0xE9` is also the ModRM byte of `add rcx, r13`, so a then-branch
ending in that add fell straight through into its else, and both arms
returned the else value. It appeared and vanished with the parameter count,
which is what made it look like a register-allocation bug for several
rounds. The fix records what the emitter wrote rather than reading the
bytes back to guess.

**The headline defect: the allocator's zero-fill was holding up a
foundational bug.** A poison build replaces `__alloc`'s zero fill with
`0xCD`, so any field read before it is written faults instead of reading a
plausible zero. Run against the compiler, it crashed on the first token of
every compile. The cause was `__linked_list_to_list`: it built its result
list without the capacity / view-tag cell that `list-at` reads at `[base-8]`
to tell a normal list from an O(1) slice-view. It relied on the heap being
zero there (`0` meaning "normal list"); under `0xCD` the word is negative,
`list-at` read every converted list as a view, computed a wild element
pointer, and returned garbage. This is the root of the REPL effect-row
corruption reported earlier in the cycle, which had been worked around at
one call site by avoiding the conversion. The fix writes the capacity cell,
matching the other list constructors -- two instructions -- and the poison
build now compiles the root suite with zero new failures. It is the exact
shape the poison gate exists to catch: a safety net that had quietly become
load-bearing.
