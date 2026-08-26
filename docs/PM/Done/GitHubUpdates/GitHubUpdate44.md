# GitHub Update 44

**Scope: main CLs 15254 to 15686, opened 2026-08-15, released 2026-08-16.**
Update 43 covered 15085 to 15253. Every number in the final report was
re-measured at the release head, not carried forward.

## Open from Update 43

Update 43 left four items open. Three closed this cycle and one carries.

- **`B5`, the UDP half: CLOSED** (blu, 15266). The UDP receive path read a
  frame's IP header unchecked on the path DHCP takes at boot, and computed no
  checksum in either direction. Both RFC 768 zero rules pinned.

- **The deck floor under `codex/build`: CLOSED** (fester, 15275). The compiler
  gives each phase a fixed-size arena (a "deck") and the gate requires a 1.25
  margin between what a chapter needs and what it gets; three chapters sat at
  exactly 1.25, where OK and out of room are the same number. Now 1.39, by
  restructuring to fewer top-level bindings, measured as a controlled sweep
  over identical body text (24 defs needs 51, 14 needs 48, 8 needs 46, 5 needs
  45), so the lever is binding count and not size. Neither `-MinMargin` nor
  `deck-scale-min` touched. `cdx-to-pe` refuses a CDX it cannot vouch for in
  the same CL: seven arms, and ablated, the bit-flip and bad-magic arms EMIT a
  bootable image from corrupted input.

- **The zig plug's output is verified here: CLOSED** (red, 15634). zig 0.16 is
  on the box; the core is byte-identical, three known defects reproduce and a
  fourth was found. See below.

- **Nothing exercises the guard page under a genuine allocation walk: STILL
  OPEN**, carried to Update 45.

Also carried in from 43's record: **GitHub PR 65** (red, 15597), which stays
open until this push makes it visible on `master`.

Lesson ids in this report (`L-NAMED`, `L-SABOTAGE`, `L-DECODE`, `L-UNCALLED`,
`L-CAPABILITY`) index `docs/PM/Active/Stories/LESSONS.md`. A "negative arm" is
a test row that must FAIL when a guard is removed; "KAT-only" means a
known-answer test that proves the parser reads good input and nothing about bad
input. "The bed" is codex-vm, the project's own hypervisor, as opposed to real
hardware. `!EXC=06` is the guest dying on an invalid-opcode trap, the way an
out-of-band read reads out on bare metal.


## The census of bytes we did not produce, and the guards it bought

**Track D is the campaign of this cycle.** red's census (main 15381) counted
141 parsers of foreign bytes in the tree with their test state: 43 with a
negative arm, 52 KAT-only, 19 with no test at all. It refuted the plan's
hour-old table, and 15396 re-ranked the queue by REACHABILITY: cbor, msgpack,
protobuf, bencode, websocket, discovery, oauth, imap, modbus and the text
formats have no production caller and are demoted to latent; TcpTransport
framing (every plug, NetIO, TrustTransport), the trust handshake,
`http-parse-response` (the browser) and `ttf-parse` (a font off a stick) are
what an outside byte actually reaches. Later re-rankings added the fact store a
disk supplies, the FAT chapters the compiler reads its own source through, and
the model loader. `VerifiedFormatParsing.md` section 10 carries the queue; 10.1
is the ranked work list.

What landed against it, each with an arm that fails when the guard is
ablated except where the entry says otherwise:

- **Item 1, three untrusted lengths in `TcpTransport`** (val, 15461).
  `transport-feed-raw` wrote past the 32 MB `recv-base` allocation with no
  `recv-cap` check; a `msg-len` of 0 handed `__buf-read-bytes` a read length of -1.
- **Item 2, the handshake authenticated nobody** (val, 15485).
  `hs-receive-prove` never took the peer signature as a parameter, ignored the
  challenge nonce, and returned `HsCompleted` unconditionally with the trust
  score of whatever key the peer claimed.
- **Item 3, a truncated agent message killed the node and then fooled it**
  (val, 15523). Eight decode sites, on a path that is genuinely reached from
  `TrustNode`'s `node-recv-loop`.
- **Item 4, `HttpClient` refuses the two wire numbers it believed** (red,
  15403). A status token must be exactly three digits: a 20-digit token had
  wrapped to `7766279631452241919` with `valid=True`.
- **Item 5, the font the desk loads off the stick is checked before it is
  parsed** (red, 15422). Ablated, an empty buffer parsed to 8,295 glyphs.
- **Item 12, foreword `Fat16` refuses a cluster number outside the volume**
  (fester, 15550, seed-affecting: `opening.codex` cites this chapter). Three
  arms proved nothing first and the CL says which and why -- the sink arm ran a
  prebuilt image and never compiled, the eight disk arms fit every fixture in
  one cluster (`L-NAMED`), the first sabotage was too gentle to move a row
  (`L-SABOTAGE`). The arm that ships calls the function directly.
- **Item 14, foreword `Gpt` geometry guard** (red, 15593), `gpt-header-geom-ok`
  with four `gpt-core` arms and fixtures. And WORKS-28 before it (fester,
  15313): `gpt-esp-start` checked the `EFI PART` signature and NEITHER CRC32,
  so a tampered table parsed like a good one and the ESP start LBA it hands
  out is the base every later read is relative to.
- **Item 15, the two fact-store lengths a disk supplies** (reek, 15576, seed
  `AB585AF5`), and val's residue (15631): a 4 MB ceiling on one entry, because
  reek's bound was the size of the attached medium, which is still a number
  this code did not produce. **The residue ships without an arm and every doc
  says so**: reaching it needs a store image over 4 MB, and the medium ceiling
  refuses first.
- **Item 16, the GGUF reader** (val, 15582). Every field was a bare `list-at`;
  `gguf-parse-header` admitted a 20-byte file and then read bytes 16..23 of a
  24-byte header. `gguf-hostile` is 16 rows, 11 guards ablated, each killing at
  exactly its own row; four real llama.cpp models still parse. The dequant
  residue (15603, found by reek): neither dequant loop took a length off the
  file, so the offset guards never reached them; 32 elements from a one-block
  buffer answered 32 values, 64 died `!EXC=06`.
- **Item 17, foreword `Fat32` refuses a zero BPB divisor and an out-of-range
  cluster** (fester, 15560). The first arm handed a FAT16 disk to the FAT32
  parser, the cluster count came out NEGATIVE, and every row read no -- the
  right answer for the wrong reason. The arm that ships builds its own volume.
- **Then the chain WALK, which neither cluster guard bounded** (fester,
  15617, seed `AC41982F`). Eight walkers in `Fat16` and `Fat32` gain Brent's
  cycle detection. A counting bound was tried first and measured unfit: fuel of
  cluster-count + 2 terminates, but on the 30,414-cluster fixture the cyclic
  walk did not finish in 60 s; Brent returns in 0.6 s. And blu's point is the
  one that made the count unusable in principle: it is computed from BPB fields
  the image supplies, so a bound derived from untrusted bytes is not a bound.

**The overflow class, named twice in one morning.** `repo-has` (reek, 15624)
and `sdw-decode` (blu, 15641, seed `D230B11D`) both had a length bound that
ADDED, and a sum wraps. On `sdw-decode` that was measured: a 19-digit length
field walked around the check and killed the guest `!EXC=06`. Both subtract
now. `ExaminersAssay.md` "A Bounds
Guard That ADDS Can Be Overflowed" holds the census: 71 sites, 34 with a
variable operand.

**Reek's wire consumers** (15364, 15452, 15480, 15494): `RepoProtocol`'s two
raw `list-at` reads over wire bytes become `frame-byte-at`; `hid-scan-loop`
walked off the end of a buffer a USB device handed it, bounded against the
caller's `wTotalLength` and never against the list; and the five wire consumers
refuse a payload that did not fit, each down the road it already had -- a
correctly signed envelope around a truncated payload is `envelope=yes` on both
arms, and the verdict it would have persisted is the point.

**Blu's net leg**: four bytes on the wire killed the guest via
`MessageFraming` (15345); a 12-byte DNS response killed it through
`dns-parse-response`, reachable from `HttpFetch` (15329); the ARP cache was a
remote guest kill at 256 frames from any host on the segment (15310); the UDP
receive path read a frame's IP header unchecked on the path DHCP takes at boot
and computed no checksum in either direction (B5-UDP, 15266);
`ip-checksum` silently dropped the last byte of an odd-length range (15287).
The `codex/os/net` parser audit closed at 15449.

## Two loops that were counts and are now durations

**`e1000-init` never hung. It took 93 seconds, and 92.9 of them were one loop.**
NIC-3 on the ASUS (blu, 15431) recorded `e1000-await-aneg` running its full
million-iteration fuel at 92.89 us per MDIO read and returning 0: aneg-done is
never set on this part while the link comes up regardless. The 08-14 wedge
was that spin misread as a hang. Budgeted to 3 s of HPET (15465), which then
exposed that `e1000-await-link` had no deadline at all, only four million
`STATUS` reads: NIC-4 (15510, 15611) sat in it for ten minutes. Both loops are
bounded by time now, and the three arms (`e1000-aneg-budget`,
`e1000-link-budget`, `e1000-link-deadline`) assert a BAND rather than a figure,
because the lower bound is the half that discriminates. The account is
`ExaminersAssay.md` "The E1000 Bring-Up Budgets" (15653).

## The internal gate is fast, and the release gate is the only full one

**A seed land used to pay three full `build/build.ps1` passes under the token,
about 27 minutes, and two of them were redundant against determinism** (red,
15621; lifted into the generator by reek at 15643). `build/build.ps1 -Internal`
always proves the byte-identical self-fixed-point and the BVT, and runs a
regression phase only when a file that phase depends on changed in the
workspace. Measured: a foreword-parser change gates in about 2.5 min, a
codegen change still pulls the codegen-sensitive phases, against ~8.6 min for
the full gate. `check-seed-orphans.ps1` replaces the parent rebuild at copy-up.
`PerforceProcess.md` 4.3b and 4.4 carry the procedure. blu's `sdw-decode` seed
went through it: internal gate 159 s, one-pass fixed point, self-verifies.

**COMPILER-3 closed as relocation, not codegen** (val, 15535): the `-Repl` /
non-repl byte difference is `__start`'s exit stub being 11 bytes longer,
alignment absorbing 3, and every one of the 3,641 differing code sites a 32-bit
absolute data address that is +8. **COMPILER-5 closed** (val, 15410): the FNV
literal past i64-max is spelled as parenthesized hex, closing the text
round-trip hole at `disk-fnv`; the emitter was never wrong.

## The zig plug is verified here now, and PR 65 is in

**GitHub PR 65 is carried in** (red, 15597): Steve Howell's eight-rung ladder
(`lex -> parse -> desugar -> scope -> check -> lower -> text -> pingpong`),
rebased onto the Update 43 seed and replacing `ZigEmitter.codex` wholesale
(the depot was byte-identical to his base). Update 43 had to say his
byte-identical result was his measurement and not ours. **zig 0.16 is
installed on the box now and the plug was verified first-hand** (red, 15634):
the core is byte-identical, his three self-reported defects reproduce (no
`int-mod` emitter, a record literal emitted without the parentheses zig needs
before a field access, bounded-field clamping not emitted), and a fourth was
found. The oracle arm stays unwired until those are fixed; `plugs-backlog`
1.13, now val's.

## The fleet learned to say where it is standing

**`status.json` gains a `claim` field and AgentGrid publishes a fleet
dashboard** (Damian, main 15655). AgentGrid compares every live claim against
every other and tells BOTH agents when two overlap; a claim is ignored while the
agent reports Idle. The rollup keeps two halves apart on purpose: state, task
and claim are what an agent says; terminal activity, context percent and last
activity are what AgentGrid observes, because a wedged session says Working
forever. New rule 9: publish your claim when you start, and read the fleet's
before you pick work -- the token serialised builds and nothing had ever
serialised work, which is how val and blu built the same fetch-tls work an hour
apart with neither doing anything wrong.

**A fleet message is a pointer, not a container** (15570): one or two
sentences, what changed and where the detail lives. **A deleted build-request
is still granted 19 minutes later** (val, 15565, measured); the workaround that
works is written down.

**`CurrentPlan.md` was pruned to open work only** (red, 15351), 1,100 lines to
about 290, at Damian's direction. The campaign record of shipped items is gone,
each durable fact checked to be in the doc that owns it. **`MetalOutputSink.md`
and `DECK-SHORT-MISCOMPILE.md` moved to Done** (15356). **`IRTypeEmission`
step 4 was already done** and the design moved to Done (fester, 15457).

## The stub, the desk, and the flights

- **The stub's `AllocPanic` printed its letter and kept running** (fester,
  15503). `hlt` with no `cli` before `ExitBootServices`, where the firmware timer
  is live, resumes on the next tick. Measured by forcing a refusal with a 2 GB
  heap in a 640 MB bed: `s v c H V h g` then a `#GP` in the firmware handler.
  Every stub path carries unflown bytes after this (`L-DECODE`), and 15506
  re-hashed all seven arms and says so in the plan.
- **The stub picks the largest GOP mode the firmware enumerates** (red, 15469,
  bed half). codex-vm's GOP mode table gains the CLI mode as mode 3 and
  `SetMode` commits the framebuffer; six bed arms in `build/gop-mode-arm.ps1`
  including the ASUS-shaped `-uefi-conout-remode` arm (1024 in, 1600 out). The
  metal half is a photograph under the standing ruling.
- **`cdx-to-pe` places the heap below the framebuffer on every path that keeps
  firmware paging** (red, 15393): every plain option-a image rebuilt since main
  15041 had halted `V` in the bed at default `-mem`, found by reek on
  `sinkladder.img`.
- **WORKS-9's heartbeat is bed-verified end to end** (reek, 15426); the
  `MetalLadder` bar's arithmetic is under test in `ladder-bar-width` (15387).
  **The desk contract** (`apps/works/works-desk-contract.md`, reek 15318,
  15338) is now on the init reading contract: a pointer cell must be allocated
  in `desk-run`, and the desk never unwinds. **WORKS-12 finished for every
  pane but Edit, and the Edit pane stops writing its state block to physical
  address 0** (15306).
- **`nicinit.img` flashed to disk 2 and verified** (blu, 15416); which seed it
  was built against is recorded (15418), because the image embeds the seed and
  main's seed moved the same evening.
- **The symbol map the build already emitted is installed and the release step
  takes it from there** (reek, 15286).
- **`plug-smoke` rebuilds a stale plug binary** and runs a record-carrying
  second input (red, 15488, `plugs-backlog` 1.11 closed).
  **`check-plug-field-slot.ps1`** is `plugs-backlog` 1.2's runner (15497): one
  record probe's IR to every TCP plug, grepping for the leaked field slot; first
  measurement 38 run, 35 leak, 3 clean.
- **`SeText`, the interpolating string node the shell DSL never had** (reek,
  15616), and `ScRaw` deprecated with the removal work catalogued (15606).

## Found by the release itself

**The DDC arm did not build at the pinned head.** Roslyn refused
`Codex.cs(3980,131)`: `block_sector_count` does not exist. The builtin is
reached from the compiler via `FactDisk.codex:195` since reek's item-15 land at
15576, and the C# plug had no row for it. It is nullary and reaches the emitter
as a bare `IrName`, so it needed a `raw-builtins` row rather than the
`BuiltinEmitter` row the release skill names. One `"0L"` row (root, main
15686), the one exception to the pin, and the release head moved from 15671 to
15686.

**Three tests landed a day earlier could never have passed.** The
`gpt-*-guard` `.expected` files from 15313 had no trailing newline and
`test-run.ps1` always appends one. No agent runs the battery, so a test that
fails only in the battery is invisible until a release. Fixed in the same CL.

**And the battery harness can lose bytes.** In the first battery at 15671,
batch-0 delivered 180 `SIZE:` blocks for 182 tests: `repo-reclaim.cdx` WAS
`ota-lwm2m-loopback` by hash, `colophon-dogfood.cdx` WAS `web-server-test`, and
`verifier-phase5-test.cdx` had its own header on a foreign body. All three
compile and pass standalone. Ten VMs were live on the box at the time. The
parser assigns blocks to names by sequence and cannot notice a shortfall. It
did not recur in the two later batteries run with the box otherwise idle.
Recorded in `ExaminersAssay.md` "The batch stream can lose bytes"; the lossy
layer is not established.


## Opened this cycle

- **The battery harness can lose bytes from a batch stream** and file the
  survivors under the wrong names (above). The lossy layer is unknown; the
  parser cannot notice a shortfall. `ExaminersAssay.md` "The batch stream can
  lose bytes".

- **Track D items 18 and 19 are done in reek's stream and queued for MAIN
  OPEN**, so they land in Update 45: Lz4, Lz77 and Deflate guarded, Rle a row
  and no guard (no caller, `L-UNCALLED`), Brotli a termination arm and NO guard
  because two ablations of the guards the census credits moved zero rows. Item
  19 is closed on the `compress/` SUBSET only; the `ai/`, `ui/` and `core/` leg
  is unswept and unowned (blu caught it, `L-CAPABILITY`; recorded in reek's stream at 15696, not yet on main). One
  residue stays open by name: OtaBoot's flash length has no slot-size constant
  to bound it against. After them, whatever `VerifiedFormatParsing.md` 10.1
  holds. The additive-bounds overflow class is 71 sites, 34 with a variable
  operand.

- **The zig plug's four defects** (`plugs-backlog` 1.13, val) before the
  oracle arm can be wired.

- **`plugs-backlog` 1.2: 35 transpiler plugs emit a division where a record
  field access belongs.** `check-plug-field-slot.ps1` is the runner and
  `plug-field-slot-baseline.txt` the debt list; python is fixed and must stay
  clean.

- **The rulings queue in `CurrentPlan.md`**: ICMP receive, ARP learning policy,
  type-variable instantiation (compiler or recheck plug), the 16 MB stick
  image's depot slot, `check-vm-differential`'s retry line, `p4-stale-check`'s
  dropped-add policy, `widget-panel`'s flex default.

- **Every stub path carries unflown bytes since 15503.** A rebuilt image needs
  its `L-DECODE` rehearsal before any flight; the native-GOP metal half is a
  photograph.


## The release proof, at head 15686

Every number here was measured at the release head, not carried forward. The
head moved once during the run, from 15671 to 15686, for the one CL the pin
admitted: the C# plug row the DDC arm needed, the three `.expected` newlines,
and the harness note. Every proof below was run, or re-run, at 15686 with
nothing else on the box.

| proof | result |
|---|---|
| battery, `-Tier all` | 1496 tests, **1450 pass, 0 fail**, 46 skip; no death-batches |
| app sweep | **265 clean, 5 known-dirty, 0 regressions** (270 units) |
| poison build (0xCD fill) | 1496 tests, **0 fail** |
| DDC witness | **HOLDS** -- 2,800,207 bytes both arms, **0 differing outside the signature region**; 95 of the region's 96 bytes differ, which is what two unrelated signatures look like and is not a criterion |
| standing gate | green, hard fixed point in one pass, `constants.hash` unchanged, 616 s |
| oracles | scalar 2013/2013, vector 130/130, CCE 1485/1516 with 31 in documented gaps and 0 unexplained; measured in the 15686 battery, and identical to Update 43's because the corpora did not change |

Seed `D230B11D910D437D`, 2,800,207 bytes, content hash `6F2CE9BD66DA95EA`,
measured against the depot print and already the fixed point at the release
head, so no rebuild was due. The symbol map WAS stale and is refreshed: 5,196
rows against 5,194, validated against the seed's embedded MAP1 by name, address
and size with zero differences. `seed/Codex.img` rebuilt to `19F439DE` with the
seed as kernel and booted under OVMF to the first-boot ceremony before it was
submitted.

**The first battery, at 15671, was 6 red and none of the six was the compiler.**
Three were test files that could never have passed and three were the harness
losing bytes from one batch stream. Both are above under "Found by the release
itself". A release proof that had stopped at "6 fail" would have blocked on
nothing; a release that had waved them through as "pre-existing" would have
shipped three tests that fail on every battery. The rule that settled it was
to compile each failing test standalone and hash the result against what the
battery had built.

**The DDC is the one of those four that does not take the compiler's word for
anything**, and this cycle it also caught something mundane: a builtin added to
the compiler's dependency set with no C# counterpart. That is ordinary upkeep
and it is exactly what running the witness on the release rather than trusting
the last run is for.
