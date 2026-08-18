# CurrentPlan -- the shape and the priority order

*This file is the fleet's open work and its priority order. It carries no
history: shipped work is deleted, not memorialized (Perforce and the
GitHubUpdate reports are the record). Consolidated 2026-08-08 by reek at
Damian's direction: the five per-agent workplans and the findings-outbox
channel were retired, their open items folded in here, and their durable
facts moved into the reference docs that own them. A closed item is
DELETED, not annotated.*

*Pruned 2026-08-15 by red at Damian's direction ("we have closed a lot of
things"). The file had grown to 1,100 lines of which about 900 were the
campaign record of items already shipped: A5, A6, the F12 regression, C1, C2,
the Shell DSL backport, the deck floor, the annotation campaign, the
`print-line` inversion. Every durable fact in those accounts was checked to
be in the doc that owns it before the paragraph went (`HardwareSitting.md`
for the flights and the I219 findings, `DeskBuildLoop.md` and
`OperatorsManual.md` for the arena, `Build.md` for the generators,
`IndependentRechecker.md` and `DevelopersGuide.md` for the type rules,
`ExaminersAssay.md` for the guards). If you are looking for how something
was hunted, the GitHubUpdate for that cycle and the CL are where it lives.*

**Where an item ORIGINATES in one app or quire, it lives in that
register** (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) and is named here only if it blocks a
track. There is still no platform-wide register beyond this file; do not
recreate `docs/PM/BACKLOG.md`.

## Where we stand, in three sentences

The compiler is a hard fixed point of itself on bare metal, Update 43 is on
the public mirrors, and on 2026-08-14 the compiler booted the ASUS from bare
UEFI, compiled its own 2.8 MB source off the stick in about a minute, and
wrote the result back byte-identical to the host control (A5, shipped). The
trust audit has closed on the whole compiler: diverse double-compiling
reproduces the seed except the signature region, the readable-intermediate
defense has a runner (`jonquil`), and the independent rechecker raises one
honest abstention against 4,862 definitions. The network has a link, a
measured poll calibration that transfers to the real I219-V (NIC-2), and an
`e1000-init` that was thought to wedge and turns out to take 93 seconds
because one fuel count is 92.9 of them (NIC-3, answered 2026-08-15), and the
fleet has spent the last two days finding that almost every parser of bytes
we did not produce could be killed by a short frame -- which is the campaign
that has no track yet, and gets one below.

## Track A -- the stick is an OS

- **WORKS-9 (reek). The USB mass-storage driver's second write, and the
  sink's own 2.7 MB write on metal.** `sinkladder.img` FLEW 2026-08-11 and
  was RED: the screen held ORANGE, `fat16-write-segments` created no
  directory entry, so the fault is at or before the first allocation. The
  next arm needs a heartbeat INSIDE `sl-fill` and between write segments,
  because the rung printing reports only the stage that already passed.
  Metal-gated; the arm and account are in `apps/works/works-backlog.md`
  and `docs/Hardware/HardwareSitting.md`. **Damian's standing ruling: agents
  do not propose flights or sittings.**
- **A8 the desk build loop (fester).** Edit half done, hypervisor complete,
  VT-x measured available on the ASUS, arena measured at `-AllocPages
  131072`. Plan, roads and traps in `docs/Designs/Active/OS/DeskBuildLoop.md`.
  **Open: whether the ASUS firmware grants that allocation** (L-FREEDOM; the
  stub raises `H` if refused, and as of fester 15500 that is TRUE -- before it
  the stub printed `H` and then ran on into a `#GP`, so the sheet's own
  sentence was wrong about what the board would show) -- a sitting question.
  **The arm is bed-proven in both directions and is NOT flight-ready**: it
  builds with no `-Identity`, so it runs the first-boot wizard, and with no
  source, so it cannot answer the compile loop, only the allocation. After it,
  wiring
  `compile <path>` into the Console pane is a dozen lines against pieces the
  console already has.
- **Native GOP resolution and diag word wrap (red, scoped by Damian
  2026-08-07). BED HALF DONE 2026-08-15; the metal half is a stick rebuild
  and a photograph.** `gop-draw-text-wrap` landed earlier (`GopDraw.codex`).
  The stub now picks the largest GOP mode the firmware enumerates before
  reading `Mode->Info`, on every non-`-EntryStart` payload; every failure
  falls through to today's behaviour; the flown A5 stub is byte-identical.
  Six bed arms in `build/gop-mode-arm.ps1` including the ASUS-shaped one
  (`-uefi-conout-remode` at 1600x900: 1024 in, 1600 out); ablation is the
  main 15393 stub staying at 1024. Making the bed faithful (codex-vm's mode
  table gains the CLI mode; `-gop-max-mode`; `QueryMode` scratch block;
  `SetMode` commits the framebuffer) found and fixed a HOST crash on any
  runtime mode set from a headless boot. Account: `ExaminersAssay.md` "The
  GOP Mode Arms". **What is left is metal**: the ASUS's largest mode and
  whether AMI's `SetMode` honours it are L-FREEDOM questions the bed cannot
  answer; the next option-a stick built for any reason carries the change,
  and the photograph answers it. Not a proposed flight.
- **EVERY UEFI STUB PATH NOW CARRIES BYTES THAT HAVE NOT FLOWN, `-EntryStart`
  included (fester main 15503; red 15469 for the non-EntryStart paths).**
  15503 made the stub's allocation panics actually halt (`cli`/`hlt`/loop:
  before ExitBootServices a bare `hlt` resumed on the next timer tick and
  fell through into code that assumed the allocation succeeded), which
  moved every arm's hash; measured with one CDX under the 15469 and 15503
  scripts, all seven arms differ and the `-Ebs`, `-EntryStart -Ebs` and
  `-Stdin` PEs grew a 512-byte section (120,320 to 120,832 bytes), a layout
  shift. The depot stick images (`build/boot/a5*.img`, `sinkladder.img`,
  `nicsitting.img` and the rest) predate both and are unchanged. **A rebuilt
  image is L-DECODE territory: rehearse the exact bytes in the bed before
  any flight (L-REHEARSE), and say in the flight card that the stub is new.**
  The six mode arms (`build/gop-mode-arm.ps1`) pass on the 15503 stub.
- **A 16 MB stick image is in the archive and not in the depot.** The only
  copy of the 124-byte `IDENTITY.DAT` the guest wrote on real hardware is
  `D:\Projects\stick-archive\stick-before-20260811.img`, outside every
  workspace. Whether it earns a depot slot is in the rulings queue.

## Track B -- the network (blu). Metal-gated: advances at sittings, not before.

The queue Damian draws from is `docs/Hardware/HardwareSitting.md`, "THE
SITTING QUEUE" at the top of the file: five questions on one boot, in an
order that is argued rather than preferred (bank before you risk, L-BANK).
**NIC-1 and NIC-2 are ANSWERED (2026-08-14)**: the part arrives cold, and the
poll-rate calibration transfers (32,606 us per million polls, 2.50x the
bed), which was the single assumption B3 and B4 rested on and is now a
measurement.

- **B2c, NIC-3: ANSWERED ON METAL 2026-08-15. `e1000-init` does NOT hang.**
  It completes in 93 seconds and **92.9 of them are `e1000-await-aneg`
  burning its 1,000,000 fuel at 92.89 us per MDIO read**. The 08-14 "wedge"
  was that spin read as a hang. Full rows, banked rather than photographed,
  in `HardwareSitting.md`.
  **`e1000-aneg-fuel` was the poll-count-as-duration defect again, and it is
  FIXED** (blu): `e1000-await-aneg` is bounded by a 3-second budget through
  HPET, the way `e1000-settle-mdio` and `e1000-quiesce` already were, with the
  count kept as a second bound for when HPET is absent or stops advancing.
  **NIC-3 did not diagnose this and an earlier entry of mine implied it did.**
  The cost was named on 2026-08-04, after the ASDE flight painted nothing --
  it is written in `NicAsde.codex` at `na-phy-kick` and in the annotation on
  `codex/test/e1000-asde-nolink`. What was done then was to route AROUND the
  function; the driver kept the million, and it cost a second flight eleven
  days later. NIC-3 contributed the number, not the cause.
  **Two things the flight left open.** Auto-negotiation never reports done
  while `STATUS.LU` is set, so `phy-bring-up` returns 0 against a link that
  is up. And `RDH` moved 0 to 15 with `RDT=15`, which is either the receiver
  filling the ring during those 93 seconds -- frames DO move -- or `RDH`
  being unwritable as `CTRL` is; the arm cannot separate them.
  Finding 4 (ASDE) still rides the same class: `build/boot/asdeflight.img`
  is built, bed-verified both ways, and awaits a sitting.
- **NIC-4 FLEW 2026-08-16 AND HUNG, and the hang was the aneg fix's own
  consequence.** `nicring.img` painted its first three rows and stopped inside
  `e1000-init`; more than ten minutes, no return, no bank (`mount stage 2`), so
  the glass is the whole record. **Cause, and it is not what the arm went up to
  ask.** NIC-3 had already measured `e1000-await-aneg` returning **0** after its
  full million: aneg-done is never set on this part, while `STATUS.LU` comes up
  anyway and the part negotiates 1000 Mb/s. So those 92.9 seconds were never
  auto-negotiation succeeding -- they were dead time during which the link came
  up behind them, and they masked the fact that `e1000-await-link` had **no
  deadline at all**, only a count of four million. Budgeting aneg removed the
  dead time and that count then ran against a part whose link was still
  settling.
  **FIXED, blu 15588**: `e1000-link-wait` gives it a 5-second HPET budget with
  the clock read once per 4096-poll batch, the pattern `e1000-await-tx-clocked`
  already used in the same chapter; the count survives as the no-HPET path.
  Worst-case init is 3 s + 5 s. Two arms: `e1000-link-budget` (arithmetic) and
  `e1000-link-deadline`, which reproduces the metal symptom on the desk under
  `-e1000-no-link` -- 8,037,305 us with the budget against 21,862,178 us with
  the count, ablated. **15463 is NOT reverted; it was right, and the regression
  was mine too.** Not seed-affecting.
  **The ring question NIC-4 went up to answer is still open**: no `dd=` map was
  ever painted, so whether frames move is exactly where NIC-3 left it.
- **B3, a real TCP conversation with a real peer.** The stack holds one in the
  bed over the e1000 (main 15013/15028). No longer blocked by a hang or by the
  93-second bring-up; the next sitting is the gate, and the ring question above
  should ride the same boot rather than spend a flight of its own.
- **NIC-5: what wedged the box on 2026-08-11.** It was NOT `CTRL.RST` (that
  write is discarded on this part). Terminal by construction, flies last.
- **B4, serve the repository protocol. root, from red's routing 2026-08-17;
  staged in the BED, one CL per step, and this bullet is the row.** What is
  already so, measured 2026-08-17 (root, from source): the protocol IS served
  in the guest today. `tools/cdx-serve.codex` listens on 9300
  (`net-io-accept (net-io-listen ...)`, `:124`) and answers `tag-work-request`
  with `tag-work-reply`; `tools/cdx-registry.codex` on 9301 answers locate and
  announce; the host reaches them through `codex-vm -portfwd host:9300`
  (`OperatorsManual.md` "-portfwd", "Resolving A Quotation From A Peer"), and
  `build/cdx-serve-test.ps1`, `quote-from-peer-test.ps1`,
  `registry-locate-test.ps1` and `nat-conn-churn-test.ps1` drive it. The e1000
  conversation of main 15013/15028 is `cdx-serve-test.ps1 -VmArgs '-e1000-nat'`
  and is recorded in `DeviceEmulationCatalog.md:297` ("the full
  repository-protocol exchange passes over the e1000 branch"). The wire is
  specified only in code: framing in `codex/os/net/MessageFraming.codex`
  (`frame-encode:17`, LE32 length + tag byte; `tag-work-request 17`,
  `tag-work-reply 18`, `tag-locate-request 19`, `tag-locate-reply 20`,
  `tag-announce-request 21`, `tag-announce-reply 22`) and the codec in
  `apps/works/RepoProtocol.codex` "Wire Codec" (`:260-505`); no design under
  `docs/Designs/` writes it down (`V3-REPOSITORY-FEDERATION.md` refers to it as
  built). EdgeMesh Phase 2 (`EdgeMeshGameServers.md:5, :276-286`) names no
  repository verb: it asks for "a real socket surface" plus its own
  GroupMembership/EdgeRouter/TrustNode wiring, and the surface exists.
  **Steps, each its own CL, none of them seed-affecting:**
  1. DONE (this CL): the plan and the claim.
  2. `cdx-serve-test.ps1` gets a `-Card ne2k|e1000` switch (today the e1000 run
     is an ad-hoc `-VmArgs`), runs the same conversation over both, and the
     numbers land in `ExaminersAssay.md` beside the existing account; the
     registry harness gets the same switch. That is B4-in-the-bed made
     repeatable rather than remembered.
  3. `registry-probe.ps1:23` hardcodes `D:\Projects\NewRepository-val` and runs
     from no other workspace; fixed to `$PSScriptRoot`.
  4. The wire written down where a second implementer reads,
     `DevelopersRulebook.md` ("The repository wire": frame, tags, the
     work-request/reply body from `RepoProtocol.codex`, ports 9300/9301),
     derived from the code with the same "read the source for the truth" note
     the DDC rows carry. Ask red first whether it belongs there or in a
     resurrected federation design.
  5. `EdgeMeshGameServers.md` Status gets one paragraph naming the surface
     Phase 2 can start against in the bed (`net-io-listen`/`net-io-accept`,
     `MessageFraming`, `TrustTransport`), so Phase 2 stops waiting on B4 and
     waits only on its own owner.
  6. Metal: the same conversation on the part is B3's flight and Damian's
     sitting; not staged here.
- **The untrusted-frame class in `codex/os/net` is blu's and the AUDIT IS
  COMPLETE.** Landed: TCP and IP truncation (main 15245), UDP (15266),
  `ip-checksum`'s odd tail (15287), the ARP cache (15310), DNS (15329), the
  MessageFraming crash (15345) and its refusal channel (15375). Five of
  those were remote guest kills; the smallest took four bytes.
  **Audited and found SOUND, which is worth recording so nobody re-audits
  them**: `WebServer`'s `Content-Length` path (an oversized length returns
  -1 and keeps buffering; probed at 3, 18 and 25 digits), and `Tftp` and
  `Syslog`, whose every walk takes its bound from `list-length` of the data
  rather than from a wire field.
  **`Tftp`, `Syslog` and `Icmp` have NO production caller** -- only tests --
  so all three are latent rather than live, and the census already has them
  as KAT-ONLY. **One latent cost rides with that**: `syslog-decode-bytes`
  builds its body with the quadratic `acc &` accumulator (CostModel 3.6),
  which under a wire-supplied length is arena exhaustion rather than a slow
  path. Unreachable today; whoever gives `syslog-parse` a production caller
  fixes the accumulator in the same change.
  This is the network leg of Track D below, and it is done.
- **MessageFraming's refusal channel is LANDED (blu). The caller sides are
  open and belong to their lanes.** `FrameTextResult` and `FrameBytesResult`
  now carry `valid`, set by `frame-fits`: the length prefix present and the
  payload behind it. The pair that makes it worth anything is `[0,0,0,0]`
  (zero bytes declared and present: empty, valid) against `[4,0,0,0]` (four
  declared, absent: empty, INVALID) -- identical values, opposite validity,
  which no caller could distinguish before. `MessageFraming` is the only
  place in the tree that constructs either record, so this broke no caller;
  `valid` is additive and a caller that ignores it sees exactly what main
  15345 shipped. `frame-decode-body` and `frame-decode-length` return bare
  values with no room for a flag and are deliberately unchanged.
  **The consumers are now DONE, and one of the three never needed doing.**
  This entry said "nobody reads it yet, `TrustTransport`, `FactSync` and
  `ReplayCrf` chain decodes blind" until 2026-08-16. That was wrong about
  `TrustTransport`, which has refused since it was written and by a stronger
  method than propagation: `decode-agent-msg-checked` RE-ENCODES what it
  decoded and compares bytes (`amsg-bytes-equal`), so it refuses a truncated
  body and a non-canonical encoding alike, and `trust-recv:567`,
  `trust-respond-hello:48` and `trust-complete-as-responder:82` all act on the
  answer. `agent-msg-truncated.codex` is its arm. Nothing was needed there.
  `FactSync` and `ReplayCrf` were real and are closed (blu, main 15732) with
  `fact-sync-truncated` and `crf-truncated` as the arms.
  **The live gap was not in any of the three:** `tools/cdx-serve.codex:117`
  called the RAW `decode-agent-msg`, so the one place in the tree that skipped
  the refusal was a server taking frames off a socket. Closed in the same CL
  with two arms in `build/cdx-serve-test.ps1`. Item 7 is DONE.
  **The historical note, kept because it was a corrected claim:** a signature
  does NOT catch a truncated field. `codex/os/trust` has no verification at
  all and the only verify on the path is `RepoProtocol.codex:107`, verdicts
  only. **`apps/data/Protocol.codex` is the shape** (fester): it refuses on
  `list-length bytes < needed` before taking anything.
- **Two `RepoProtocol` caller crashes, reek's, and they never waited on the
  channel.** `RepoProtocol.codex:323` and `decode-target` index a chained
  offset with a raw `list-at`. `next-offset` clamps to `list-length`, which
  is a valid slice bound and NOT a valid index, so both are out of range by
  exactly one on a truncated frame. `frame-byte-at` (main 15345) is the drop-in.
- **`ip-checksum` and `icmp-checksum` are now the same function.** Collapsing
  them is the obvious follow-up and is NOT taken: every plug bundles
  `Ethernet`, and `icmp-checksum` is the arm's independent witness. Whoever
  takes it keeps a second implementation somewhere or the arm becomes
  self-agreement. Unowned, low.
- **ICMP is send-only** (`icmp-parse` has a test and no production caller).
  Whether we answer a ping at all is a capability ruling, in the queue below;
  nobody writes the receive side before it.

## Track C -- the trust audit (val)

C1 (diverse double-compiling) and C2 (the independent rechecker) are LANDED
and enforced; the accounts are `docs/Designs/Active/Tools/IndependentRechecker.md`,
`docs/Test/Active/DDC-QUINE-ARM.md`, and `OperatorsManual.md` "The witness has
a negative control". What is left is a ruling, a deferral, and two holes in
the gate that the audit's own claims rest on:

- **COMPILER-5 (val, OPEN and unstarted, seed-affecting).** Nothing is in
  flight and no token is held; this said "in flight" until 2026-08-17 and no
  CL for it has ever landed on main, so the words were telling other lanes to
  route around a path nobody was on. A hex literal past
  i64-max (`#CBF29CE484222325`, bit 63 set) compiles but does not survive the
  text round-trip: the emitter re-prints it to something that re-parses to a
  different constant. That is a hole in the text half of the fixed point.
  val holds the lexer / text-emitter integer path and will take the token for
  the gate. Register entry in `codex/compiler/compiler-backlog.md`.
- **COMPILER-3 is CLOSED, no defect (val, 2026-08-16).** The 255,683 bytes
  were an artifact of comparing the two arms at absolute offsets. `__start`
  is the last function, the `Exit` epilogue is 11 bytes longer than
  `jmp repl-loop`, alignment absorbs 3, and the rest of the file relocates by
  8: the data section and the embedded MAP1 are identical under a +8 shift,
  and all 3,641 differing sites in the code section are 32-bit absolute data
  addresses that are exactly +8, nothing unclassified. The two maps agree on
  all 5,194 names and offsets but for `__start`'s own size. The same arm
  compiled twice is byte-identical, so no nondeterminism is involved. **The
  emitter answers identically in both modes; there is nothing to fix and the
  fixed point has no hole here.** Account and the region table are in
  `OperatorsManual.md` beside the existing `-Repl` warning; the backlog entry
  is deleted.
- **IRTypeEmission is DONE and the design is in `Done/`.** Step 4 landed at
  main 13661 on 2026-08-06, emitter and plug parser together, and this entry
  carried it as open for nine days afterwards. Re-measured 2026-08-15 against
  seed 55983566: the whole-compiler `-IrCce` emit completes in 146.5 s for
  15,723,893 bytes of IR, `builtins` (the definition the design says it dies
  on) emits, and the compiler's own IR holds 153,363 by-reference `record-ty`
  sites against 67 inline-structure ones. No sign-off is outstanding, from
  Damian or from val's lane.
- **The rechecker fork is Damian's**: whether the compiler should also EMIT
  its type-variable instantiation, or the plug keeps deriving it (taken first
  because plug-only and reversible). Rulings queue.
- **C2.5 stage 4 (proof terms) stays deferred unless Damian calls for it.**

## Track D -- bytes we did not produce (NEW 2026-08-15, RULED 2026-08-15)

**This is the campaign the fleet is already running without a name.** In the
last two days: a short UDP frame, a 12-byte DNS response, 256 ARP frames and
four bytes on the wire each killed the guest (blu, all fixed); a FAT volume
with implausible geometry and a GPT with a bad CRC parsed like good ones
(val, fester, fixed); `cdx-to-pe` trusted a length out of the file it was
about to make bootable (fester, fixed); a USB config descriptor's total
length was believed (reek, clamped); the FAT cluster walk trusted a cluster
number read off the volume (WORKS-29, fester, fixed at 15367); and two
`RepoProtocol` callers index a clamped offset by one too many (reek, waiting
on blu 15330). Every one was found by reading, each was handed lane to lane
in the inbox (blu -> fester -> reek -> val), and none of it is in a register
as a class.

**The design already exists and its stage 0 is already approved.**
`docs/Designs/Active/Features/VerifiedFormatParsing.md`: 75 hand-written
format modules under `codex/foreword/encode/`, evidenced only by
known-answer tests, which say nothing about malformed input. **Damian's
ruling 2026-08-05: stage 0 -- an adversarial corpus for the existing parsers
-- is schedulable background work, needs no design approval, is not
seed-affecting, and any lane with slack may take it.** Nobody had.

**The census is DONE (red, 2026-08-15): `VerifiedFormatParsing.md` section
10.** 141 parsers of foreign bytes across `encode/`, `os/net`, `os/verify`,
`os/trust`, `os/kernel` and `apps/works`: **43 have a negative arm, 52 are
KAT-only, 19 have no test on the parse at all**, 24 are encode-only. **It
inverted the table this entry carried an hour earlier**, which said the
trust-decision parsers were untouched: `Asn1` has six negative arms, `X509`
a truncated real certificate, `X509Chain` six tamper arms, `TlsCert` and
`Jwt` two each, and the guest loader (`CdxVerifier`, `VerifiedLoader`,
`WakeCeremony`) has flipped-byte and bad-magic arms. **The naked rows are
one level down**: the trust lattice's own `Handshake` `hs-receive-*` has NO
test, `TrustTransport`'s capability decode and `PeerDiscovery` are round-trip
only, `WebSocket` reads a 64-bit length ahead of the bytes on one RFC vector,
`TcpTransport`'s length prefix has no adversarial arm, and the
length-prefixed self-describing formats (`Cbor`, `MessagePack`, `Protobuf`,
`Bencode`) are all KAT-only. Section 10.1 is the ranked work queue; take
from it, do not fix the nearest parser. Section 10.3 says how a row can be
wrong (the naming rule under-counts `J1939` and `Lorawan`; three UNSURE
rows).

**The second sweep is DONE (red, 2026-08-16): the rest of `codex/foreword/`,
357 files, 43 decoder chapters, 21 of them reached from production code.**
It adds 10.1 items 14-19 and moves the top of the queue: **`core/Gpt.codex`
(item 14) admits any GPT entry-size 1..127 and then peeks 128 bytes past the
512-byte sector buffer, count and start-lba off the disk unbounded, reached
from `DiskFacts` and the `Fat16` boot path and inside the seed's reachable
set; `core/FactDisk.codex` + `SourceDefWire` (item 15) size an allocation and
a `substring` from disk-supplied lengths on the compiler's own `store` path;
`ai/Gguf.codex` (item 16) walks u64 counts and strides off a model file
behind a manifest whose signing key is read from the same manifest, reached
from `DevConsole`; `core/Fat32.codex` (item 17) **DONE, fester 15558**: the
BPB byte 13 divisor now answers a zero-volume rather than dividing (ablated,
a zeroed sector raises `!EXC=00`) and `fat32-next-cluster` refuses a cluster
outside the volume (ablated, it returns 109791427 and 0 off arbitrary
sectors). Not seed-affecting, confirmed by `Sut` not moving. **The
chain-cycle half is DONE too, fester 15617**: eight walkers across both
chapters carry Brent's cycle detection, so a FAT looping `2 -> 3 -> 2` -- well
formed at every step, every cluster a valid address -- terminates instead of
spinning. A counting bound was tried first and MEASURED UNFIT (60.4 s and no
output at 30,414 clusters, against 0.6 s for Brent on the same fixture): a
finite bound is not a survivable one. Brent rather than Floyd because every
step is a FAT sector read and Floyd's hare doubles them. Account in
`ExaminersAssay.md` "The Chain Cycle Guard"; the FAT leg of Track D is
closed.** The whole of `compress/` (Deflate, Lz4, Lz77, Rle, Brotli) has
the classic negative-back-reference and unbounded-count shape and is LATENT:
`lz4-decompress`'s one caller sits behind a signed-hash check in
`FactArchive`, which nothing outside `codex/test` cites. The order to take
them is written at the end of 10.1. Item 14 is DONE (red, 2026-08-16). **Item
15 is reek's**: claimed twice on 2026-08-16 (reek in the file-claims table at
main 15543, blu by fleet message minutes later), and the table below is the
register, "one owner at a time"; a fleet message announces, the row is where a
claim lands (fester's reading, adopted). blu is free, not overruled. 16 and
17 are unowned and not seed-affecting.

The guard pattern is settled and documented (`ExaminersAssay.md`: "The UDP
Frame Guard", "The CDX Input Guard", "The GPT Integrity Guard", "The FAT
Geometry Guard", "The Config-Descriptor Clamp"): clamp rather than refuse
where the length decides a slice, refuse where it decides WHERE a read lands
(blu's split), fill the ablation gap with plausible entries not zeros, put
the ablated call IN the arm, predict every expected value before it runs.
**Owners RULED by Damian 2026-08-15: red wrote the census; val takes the
session and trust-transport rows (10.1 items 1-5) after COMPILER-5, since the
trust-decision row the ruling named turned out to be covered; blu holds the
net leg.**

## The lanes -- RULED by Damian 2026-08-15

Approved as proposed. Each agent wraps its current business, then follows
its lane in this order. An item here is a pointer; the register named beside
it is where the detail lives. Re-read this table on every merge-down; it is
the assignment, not a suggestion.

| agent | 1 | 2 | 3 |
|---|---|---|---|
| **blu** | the net leg of Track D: DONE, `codex/os/net`'s parsers all landed. Item 15 was claimed by reek in the table before blu's announce; blu stood down and shelved the finished `sdw-decode` half as CL 15544 on `//Codex/blu` (ungated). **Note for whoever ranks 10.2: blu's earlier "FactDisk's alloc is sound" was WRONG and is retracted** -- `fd-fold-entry:141` bounds `nsec` against `end-sec`, but `end-sec` is the superblock's own 64-bit log head (`FactLog:39`, `:85`), so the image sets its own bound. About 4.29 GB from a u32 `clen`. The census row was right | NIC-3 flown and answered; **NIC-4 flown 2026-08-16 and HUNG in `e1000-await-link`, fixed in 15588, ring question still open** (see Track B). Nothing is queued for a flight; the ring question should ride B3's boot rather than take one of its own | cost model 3.3 stays a proposal |
| **val** | COMPILER-5 DONE (main 15410, seed 55983566; no emitter bug -- the sem-equiv normalizer already equates hex/decimal, the miss was a bare-hex-arg paren asymmetry, fixed by parenthesizing the FNV literal; backlog narrowed 15413) | Track D, `VerifiedFormatParsing.md` 10.1 items 1-3, re-ranked by reachability. ~~Item 1 `TcpTransport`~~ DONE (val 2026-08-15): the write overflow in `transport-feed-raw`, the `msg-len == 0` read of length -1, and a `msg-len` past `recv-cap - 4` that stalled the connection forever all refuse now; `codex/test/apps/tcp-transport-guard` is four arms with a positive control and every ablation run; account in `ExaminersAssay.md` "The Transport Length Guards". ~~Item 2 `Handshake`~~ DONE (val 2026-08-15): **the defect was not a bound. `hs-receive-prove` never took the peer's signature as a parameter at all, ignored the challenge nonce, and returned `HsCompleted` unconditionally with the claimed key's trust score, while `trust-complete-as-responder` threw the prove body away and set `authenticated = True` on every path -- so any peer that could hash could claim any identity in the lattice.** It now refuses a non-32-byte key, a non-64-byte signature, and a signature `ed25519-verify` rejects; both length checks also stop a remote guest kill. `codex/test/apps/handshake-prove-guard`, six arms, all four guards ablated; account in `ExaminersAssay.md` "The Handshake Prove Guards". **Reachability correction for the census: the four `TrustTransport` handshake entry points have NO caller in the tree, so item 2 was LATENT, not reached.** ~~Item 3 `TrustTransport` decode~~ DONE (val 2026-08-15), and this one WAS reached: `trust-recv` is called from `TrustNode`'s `node-recv-loop` and what it decodes reaches `eval-policy`. Eight sites read a tag or flag byte with a bare `list-at` past a peer-controlled offset and `decode-agent-msg tag-propose []` died `!EXC=06`; all eight now use `frame-byte-at`. With the fault gone a truncated body still decoded to an empty-fielded message that policy was asked about, so `decode-agent-msg-checked` answers a `valid` flag by round-tripping against our own encoder and `trust-recv` refuses; `agent-tag-known` separately closes the final `else` that decoded any unrecognised tag as a `WorkReply`; `decode-hello-body-checked` closes item 2's residual. `codex/test/apps/agent-msg-truncated`, twelve arms, four ablations; account in `ExaminersAssay.md` "The Agent Message Guards". **Track D lane 2 (items 1-3) is complete.** | ~~COMPILER-3~~ CLOSED 2026-08-16, no defect: the 255,683 bytes are a positional-diff artifact of an 11-byte exit stub relocating everything after `__start` by 8. Account in `OperatorsManual.md`, backlog entry deleted, Track C bullet above carries the summary. Lanes 1-3 done, so val draws from Track D by the take-order. ~~Item 15~~ was CLAIMED BY reek in this table 17 seconds before val's fleet announce and is reek's; val stood down and offered the finished shelf (CL 15548 on `//Codex/val`, ungated, measured against the superseded seed `55983566`). ~~Item 16 (`Gguf`)~~ DONE 2026-08-16, claimed in this table BEFORE announcing: every field was read with a bare `list-at`, `gguf-parse-header` refused a file under 20 bytes and then read bytes 16..23 of a 24-byte header, and `gguf-md-scan` had no offset check at all while `gguf-skip-metadata` had one, so the same file `gguf-tensor-info-offset` refuses killed the guest through `gguf-metadata-text`. `gguf-fits` now precedes every file-supplied read. Arm `codex/test/apps/gguf-hostile`: sixteen rows, three positive controls, **eleven guards ablated separately, each killing the guest at exactly its own row**; `build/gguf-foreign-test.ps1` still parses four real llama.cpp models up to 3,184 MB. Not seed-affecting, no token. Account in `ExaminersAssay.md` "The GGUF Bounds Guards". **Its dequant residue is CLOSED too** (reek found it, val verified and landed it at Damian's direction 2026-08-16): neither dequant loop takes a length off the file, so the metadata guards never reached them, and 64 elements from a one-block buffer died `!EXC=06` on the landed chapter. Both loops now stop on the first block that does not fit -- a clamp, because the count decides how many values come back and not where a read lands -- and `AgentBundle.ab-parse-model` now checks the `gti-valid` the first pass shipped with no caller. Arm is twenty rows, thirteen guards, each isolated by its own row. The item-15 content ceiling reek handed back LANDED at 15631 and moved the seed to `386C4F2012355C5D`. **HANDOFF 2026-08-16: val holds no CL, no shelf and no token, and every claims-table row it took for Track D is released. Nothing is in flight and nothing is assigned.** The three standing rows (the COMPILER-5 integer-literal path, `codex/plugs/csharp/**` with the DDC harness, `codex/plugs/recheck/**`) are lane ownerships rather than open work and are left for whoever picks that lane up. The one thing still owed to val's lane by somebody else is Damian's rulings-queue item 3, whether the compiler emits its type-variable instantiation or the recheck plug keeps deriving it. |
| **fester** | ~~WORKS-29, the FAT cluster walk~~ DONE, main 15445: `gfat-cluster-ok` on nine walkers, `range32` census arm, ablation moves exactly one cell | A8: wire `compile <path>` into the Console pane once the ASUS allocation is answered; the sitting question stays queued | ~~re-measure IRTypeEmission step 4~~ DONE 2026-08-15: it had already landed at 13661 on 2026-08-06, design moved to `Done/`. Next: draw from `VerifiedFormatParsing.md` 10.1 by the rotation |
| **reek** | ~~WORKS-12~~ DONE, main 15366: the cause was the desk never unwinding, not stranded pane state, so the fix is one base mark in `desk-run` rather than a bracket per pane; twelve panes reclaim, `desk-edit` deliberately not (its 9 MB lives in a `ds` pointer cell). Standing rules now in `apps/works/works-desk-contract.md`. ~~The `RepoProtocol` caller sites~~ DONE, main 15480 and 15494: eight bare reads use `frame-byte-at`, and the five wire consumers refuse a payload that did not fit rather than persisting an empty-fielded one | ~~WORKS-9's heartbeat~~ DONE, main 15426, bed-verified to WHITE; the queued card's provenance is in `HardwareSitting.md` and it needs a fresh full-mission run on any rebuild (its stub predates red 15469 and fester 15503) | ~~the rest of the USB descriptor family~~ DONE, main 15452: `hid-scan-loop`'s over-run clamped, the three `Usb.codex` parsers pinned, census rows corrected. Lane is clear; next item is a draw rather than a continuation | **HANDOFF 2026-08-16 (second of the day). Nothing in flight: no CL open, nothing shelved, no token held, workspace clean.** Landed this session: Track D row 19 CLOSED whole (ten chapters guarded, three L-UNCALLED, accounts in `ExaminersAssay.md`), its `encode/TrueType` residue, and plugs 1.7 / 1.8 / 1.21. **TWO ITEMS QUEUED BY red AT SESSION END, NOT STARTED, and they are reek's next two in this order:** (1) **`compiler-backlog` COMPILER-6's residual**, make the `tco-ensure-temps` reliance structural rather than positional. **DONE, main 16013**, seed 1A33FB0E; the ablation is that the arm FAILS when the copy is removed. **This is COMPILER-6, not COMPILER-7** -- red ruled the naming on 2026-08-16 and this row said 7 until 2026-08-17, which mis-briefed fester at init. COMPILER-7 is the bounded over-refusal reconsideration and is NOT reek's. (2) `docs/Designs/Active/OS/OracleCloudArm64.md` Phase 5, the LOCAL halves only (validate virtio net and blk under QEMU on the arm64 lane, build the image); upload, VCN and smoke wait on Damian's account. (WaDemo was queued here first and withdrawn: Damian 2026-08-16, personal project, not fleet work.) **DONE 2026-08-16, every local half, and the site SERVES:** `GET /` answers 200 with 948 bytes of text/html, `GET /api/health` answers the ok JSON. Phase 4 measured and passing (`arm64-virtio-blk-probe`, main 16065, after its DMA regions were found in FLASH); the QCOW2 is a `-Qcow2` switch on `build-arm64-img.ps1` and the QCOW2 ITSELF boots (16074); first frame on the wire (16098) once root's `peek-16`/`poke-16` builtins landed, `virtio-select-queue` having been a `poke-16` whose 32-bit read-modify-write QEMU dropped by width, so `queue_select` never took and the TX queue was never configured. Serving then needed one more (16123): the VirtIO DMA regions sat INSIDE the kernel's image and heap (`a64pe-kernel-base` 0x40100000 plus a 32 MB grant), so the NIC corrupted a CCE table entry and the path's slash decoded as 18 then 44 instead of 81. **OPEN, unowned, named but NOT investigated:** a SECOND request in the same boot times out, the serve loop reusing `st` after `__heap-restore` being the suspect; and neither virtio driver derives its regions from the stub's allocation, both carrying constants plus a prose warning. **5b-5d are ruling 6, Damian's OCI account.** Also landed: COMPILER-9 and its fix (16081), the ARM64 and RISC-V plug shadow checks now deriving their name set from `func-names` split at `runtime-funcs` instead of a hand list that overlapped the registered helpers 19 of 104. |
| **red** | ~~the Track D census~~ DONE, `VerifiedFormatParsing.md` section 10; items 4 and 5 (`http-parse-response`, the font off the stick) DONE 2026-08-15; census lane complete, the rest of 10.1 is the fleet's to draw from; `Cbor`/`MessagePack`/`Protobuf`/`Bencode`/IMAP/OAuth measured UNCALLED and demoted to latent. ~~The SECOND census sweep~~ DONE 2026-08-16 (Damian's direction): every `codex/foreword/` dir the first one skipped; 43 rows in 10.2, 10.1 items 14-19 added and the take order rewritten. ~~item 14 `core/Gpt.codex`~~ DONE 2026-08-16: `gpt-header-geom-ok` (the Works `gpt-array-geom-ok` transcribed), four `gpt-core-*` arms, `ExaminersAssay.md` "The Foreword GPT Geometry Guard". Next: item 16 (`Gguf`) or 17 (`Fat32`), whichever is still unowned | the SetMode half of the native-GOP item, in `codex/build/cdxtopeScript.codex` | the plugs register: ~~1.11~~ DONE 2026-08-15 (plug-smoke rebuilds a stale binary against every bundle input and runs a record-carrying second input); next plugs entry in the register's order is 1.2 |
| **root** | B4, serve the repository protocol in the bed (Track B bullet above is the row; claimed 2026-08-17 from red's routing; step 1 the plan is landed, step 2 next) | plugs 1.35 unknown-arity residue and 1.8 act-statement residue (plugs-backlog) | 1.34 awaiting Damian's call via red |

**Plugs are val's lane** (red, 2026-08-16), not a rotation.
`codex/plugs/plugs-backlog.md` is the standing assignment: when an entry
closes, val takes the next open unclaimed one in the register's order and says
so, without waiting for a go. Entries other lanes hold are named in the
register.

## Registers carrying unowned work that wants a lane

Named here because a register nobody owns is a register nobody reads.

- **`codex/plugs/plugs-backlog.md` IS NOW A LANE and it is val's** (red,
  2026-08-16). The standing assignment is the register itself: when an entry
  closes, val takes the next open unclaimed one and says so in status.json,
  without waiting for a go. Claimed inside it by other lanes: 1.8's remaining
  divergence (reek). **1.17's dependency is met**: fester's ARM64 block path
  landed at main 16001, so what 1.17 still wants is the SVC servicer route
  itself, not the blocks under it. `codex/plugs/zig/**` is Steve Howell's and
  is not the fleet's to change (Damian, 2026-08-16).
  The register was swept of closed entries 2026-08-16 (val): 1.4, 1.12 and
  1.13 deleted, 1.15, 1.16, 1.20, 1.7, 1.8 and 1.23 trimmed to the half that
  is still missing, and 1.4's one live gap kept as 1.24, that there is no plug
  self-check tier so spirv is ungated. Twelve entries are open and one (1.1)
  is Deferred.
- **`GitHubUpdate44.md`, open from 43:** nothing exercises the guard page
  under a genuine allocation walk since the LEAP arm was retired. (The zig
  half closed 2026-08-16: zig 0.16.0 is on this box, the plug's claim is
  measured here and the oracle arm is wired, `plugs-backlog` 1.13.)
- **`docs/Designs/Active/Compiler/CrossLaneFilesystem.md`** (fester): **steps
  2, 3, 4 and 5 are DONE on arm64 (step 4 at main 16224, seed 37334AC5).**
  `codex/test/fs-servicer` PASSES: a program declaring no handler of its own
  writes a file and reads it back through the default servicer onto real
  virtio hardware, and `fs-layer` still passes, so the user-handler path is
  unchanged. Step 4 also deleted a SILENT STUB: `write-file` was claimed as
  an arm64 builtin and answered a literal 0, so it never reached the handler
  table and reported `write False` with no disk touched. **That is a
  behaviour change for every arm64 program calling `write-file`, not only
  the disk tests.** A slot on this lane holds the handler's CODE address,
  not a closure pointer, which is where it differs from x86-64.
  **Open: the RISC-V twin** of the block helpers (same three against riscv's
  MMIO geometry, 0x10001000 stride 0x1000, inheriting the arm64 shape), and
  the capability REFUSAL arms of both servicer stubs are inspected rather
  than measured. Step 0's soft `[WARN]` where the design prescribes a hard
  failure is still open and still live. Found and recorded, not chased:
  plugs 1.29, arm64 effect-op slots are silently capped at 16.

  **The `VirtioBlk` `poke-16` workaround is DONE, main 16283**, and it
  needed NO SEED. This row used to say `VirtioBlk` is reached from `Fat16`
  and fixing it is therefore seed-affecting. **That was wrong**, measured
  twice on two different source bases: `Sut` built with the change came out
  byte-identical to the depot seed both times, so the compiler does not
  reach `vb-put-avail-entry`. It is the same surprise `DevelopersGuide`
  records for CL 9432, where 155 lines added to `Fat16` moved no bytes.
  Predict nothing about a seed; measure `Sut` against the DEPOT seed.
- **`docs/Designs/Active/OS/OracleCloudArm64.md`**: **every LOCAL half is
  closed** (reek, main 16074 Phase 5a and 16072/16098 virtio-net). Upload,
  VCN and the external smoke test need Damian's account and are in the
  rulings queue below; nobody else can do them.

## Rulings Damian owes (the only queue that blocks)

Each of these has a lane waiting on it or a doc that cannot be settled
without it. Nothing else is asked.

1. **Answer a ping?** ICMP receive is a capability decision before anyone
   writes `icmp-parse`'s production caller (Track B).
2. **Learn ARP only from replies we solicited?** `net-process-arp` still
   learns from any frame; narrowing it is a trust-model change and was
   deliberately not slipped into the crash fix (blu, main 15310).
3. **Emit the type-variable instantiation from the compiler, or keep
   deriving it in the recheck plug?** (Track C, val's fork.)
4. **A depot slot for `stick-before-20260811.img`** (16 MB, the only copy of
   a hardware-written `IDENTITY.DAT`), or leave it in the archive.
5. ~~**A zig toolchain on this box**~~ RULED 2026-08-16: zig 0.16.0 installed
   at `D:\zig-0.16.0`; PR 64's claim measured here (`plugs-backlog` 1.13).
6. **OCI account access for `OracleCloudArm64.md` Phase 5b-5d.** Every LOCAL half is
   closed (reek, main 16074): the QCOW2 is a `-Qcow2` switch on
   `build-arm64-img.ps1` and the QCOW2 itself boots. Upload, VCN and the
   external smoke test need the account and nobody else can do them. (The
   NIC dependency this row used to name is met: `peek-16` and `poke-16` are
   builtins now.)
7. **`check-vm-differential` has no retry** on the arm its own comment calls
   hang-prone; a QEMU timeout reds the gate for everyone. Adding one needs the
   line drawn between "arm produced no binary" (may retry) and "hosts
   disagree" (must stay fatal). (red, 2026-08-15.)
8. **Should `p4-stale-check`'s dropped-add scan FAIL the gate?** It warns,
   deliberately, because scratch files land in the same list; P-CLOBBER calls
   the dropped add the worst trap in the file. A middle option: fail only on
   tracked extensions. (red, 2026-08-15.)
9. **`widget-panel` flex defaulting to 0** is a tree-wide layout call the
   browser backlog (BROWSER-2) says is not the browser's to make.

## A TCP send loses bytes with both ends reporting success (unowned, found by val 2026-08-17)

**A 16 MB send over the guest TCP stack intermittently arrives short, and
neither endpoint notices.** Measured on a verified quiet box through
`codex/plugs/img/test-img.ps1`, which now asserts the three counts against
each other and refuses:

```
guest built 16777216, guest sent 16777216, host received 16629200
```

The guest built the whole image; **its send loop accounts for having handed
all 16,777,216 bytes to the transport**, so `net-io-send-raw-checked` answered
`is-complete` True and was not lying about its own accounting; and the host
received 148,016 fewer **with a clean close at both ends**, not a timeout and
not a reset. The bytes are lost between the guest's send accounting and the
host's socket, which makes this `codex/os/net` (or the NE2K path) and not a
plugs defect. With the assertion in place it reproduced **twice in six runs**
(the second short by 48,616), and across the day's measurements the shortfall
has ranged from 16,416 bytes to 4.9 MB.

What is eliminated, so the next person need not repeat it. It is **not** a
host-side read timeout or reset swallowed by the harness: that catch is now a
refusal (`exit 8`, main 16489) and it does **not** fire on these failures. It
is **not** the plug's own send loop stopping early, which is what the count
above settles. **An earlier warm-up-after-rebuild reading of mine did not
reproduce and should not be carried forward** -- failures fell on runs 2 and 4
with 1 and 3 clean.

What is **not** eliminated, stated plainly because it would be easy to read
the above as more complete than it is. Host contention is only partly ruled
out: earlier batches were started with 0 `codex-vm` running and still failed,
so contention is not NECESSARY, but the six-run batch above began with one
`codex-vm` still shutting down from the previous batch, and `test-img.ps1`
runs two arms back to back. Whether a lingering VM raises the rate is
untested.

The instrument is landed and permanent (main 16515): any img run that loses
bytes exits 9 and names all three counts, so whoever takes this starts with a
reproducer rather than a story. `codex/os/net` is blu's ground.

## An intermittent codex-vm host crash in brotli-interop (unowned)

`build/brotli-interop-test.ps1` failed once on 2026-08-16 with `dict`, `xform`,
`ctx8` and `far` missing from the probe output and

```
Fault Address: 0x70a00000  Access: WRITE
HOST CRASH: codex-vm faulted (code=0xC0000005) at 0x7FF641AB7C35
HOST CRASH: this is a defect in codex-vm itself, not in the guest.
```

**One failure in five runs, and the identical source passed on rerun**, so it
is not attributable to the change that was in the tree at the time (reek's
`Deflate` guards, eliminated three ways: control green, each guard alone
green, both together green on rerun). Not in `test.ps1`, `bvt.ps1` or the
release recipe, so it gates nobody today, which is also why nobody is looking
at it. Recorded so the next person who sees it has the first data point rather
than starting over. `tools/codex-vm.c` is FREE in the table below.

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `codex/foreword/core/VirtioBlk.codex`, `codex/plugs/arm64/Arm64Runtime.codex` block helpers and fs servicer, `codex/compiler/opening.codex` `ir-emit-roots`, `build/test-cross-disk.ps1`, `codex/test/{fs-layer,fs-servicer}.*` | fester, 2026-08-16, CrossLaneFilesystem step 4 and the RISC-V twin. Steps 2/3/5 landed at main 16001. The `Arm64Runtime` claim is the block/servicer sections only and is **by root's agreement**; the rest of that file is root's |
| `codex/plugs/riscv/RiscVRuntime.codex` block helpers (the twin) | FREE -- fester takes it next; announce. The arm64 shape is settled and landed, so this is the same three helpers against `codex/foreword/core/VirtioBlk.codex` with riscv's MMIO geometry (0x10001000, stride 0x1000) |
| `tools/codex-vm.c` | FREE -- one owner at a time, announce before you start |
| `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` | red |
| `apps/works/GopDesk.codex` | FREE -- announce before you start (reek announced WORKS-12 in it 2026-08-15) |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- val released 2026-08-15, fester released it again after WORKS-29 landed at 15367; announce |
| `apps/works/RepoProtocol.codex` (the two `list-at` caller sites) | reek |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/os/trust/FactSync.codex`, `codex/os/replay/ReplayCrf.codex` | FREE -- blu released 2026-08-16, the framing refusal reached both consumers; announce |
| `codex/test/cost/**` and `docs/Designs/Active/Features/CostModel.md` | blu. 3.3 shipped at main 16020, rule 3 at 16118; what is left of it is COMPILER-7 |
| the integer-literal lexer and text emitter (COMPILER-5) | val |
| `codex/plugs/csharp/**`, `build/` DDC harness | val |
| `codex/plugs/recheck/**` | val |
| `codex/plugs/zig/**` and the zig entry in `build/plug-oracle-test.ps1` (plugs-backlog 1.13, the four measured defects) | FREE -- val released 2026-08-16, 1.13 landed at val 15687 and is DONE |
| `codex/plugs/**` and `codex/plugs/plugs-backlog.md` | **val, 2026-08-16, red's assignment.** The register is the lane; val takes the next open unclaimed entry as each closes. Excludes `codex/plugs/zig/**` (Steve Howell's), 1.22 riscv (root), and 1.8's remaining divergence (reek) |
| `deck-headroom` | fester |
| `codex/plugs/spirv/**` (plugs-backlog 1.24), and every `run.ps1` under `codex/plugs/` (1.15) | val, with the plugs lane. fester released 2026-08-16 having landed 1.4 and swept 33 `run.ps1` onto `-Passes 'text-plug'` (main 15716); `csharp` is the one left, `t3isa` is answered and needs no flag, and `zig` is Steve's |
| `build/plug-oracle-test.ps1`, `codex/test/plug-oracle-arith.*`, `codex/plugs/zig/**` | val, 2026-08-16. fester released the harness to val on the overlap: val is wiring the zig arm, which is the file's owner-shaped work. fester 15679 changed its subject compile to `-Passes 'text-plug'` and that is now on main (15716), so val merges it down rather than resolving against a held CL. The 1.15 sweep EXCLUDES zig; its flag belongs in val's CL |
| `codex/foreword/shell/**` and `codex/build/*Script.codex` generators (the ScRaw removal and the readability campaign) | **reek, 2026-08-16, by Damian's direction** ("you can own this bit, as no other agents are actively workin on this code"). This moves the generators off fester's row, which kept `deck-headroom`. Catalog and order: `docs/Designs/Active/Build/ShellDslReadability.md` |
| `codex/foreword/core/Fat16.codex`, `core/Fat32.codex` | FREE -- fester released 2026-08-16, items 12 and 17 and the chain-cycle half all landed (15550, 15560, 15617); announce |
| `codex/foreword/core/FactDisk.codex`, `core/SourceDefWire.codex` (Track D 10.1 item 15, seed-affecting) | FREE -- val released 2026-08-16; item 15 landed at 15576 (reek) and its content-ceiling residue at 15631 (val, seed `386C4F2012355C5D`). Announce, and it takes the token |
| `codex/foreword/compress/**` (`Deflate`, `Lz4`, `Lz77`, `Rle`, `Brotli`) and `core/OtaBoot.codex`, `core/Aes256.codex`, `core/KeyboardLayout.codex` (Track D 10.1 items 18 and 19) | **reek, 2026-08-16**, red's routing. Item 18's three files are REACHED (`FirstBoot`, `IdentityManager`/`GopWizard`, `Keyboard`) and item 19's compressors are latent. Seed-reachability is measured per file before each half, not assumed from the row |
| `codex/foreword/core/{Tls,Pbkdf,ChaCha20Poly1305,Schedule,Pattern,Decimal}.codex`, `ui/{TrueTypeFont,GlyphRasterizer}.codex`, `ai/{SafeTensors,GpuProxy}.codex` (Track D 10.1 row 19, the ai/ui/core leg) | **reek, 2026-08-16**, red's extended ruling: the row closes whole or not at all (L-CAPABILITY, blu caught the subset close). Callers measured per chapter before each one, not taken from the row. `Tls` DONE 15699, `Pbkdf` DONE. None of the ten is seed-affecting -- all measured absent from the compiler unit against a `Foreword--Fat16` control -- so the leg takes no token. `ai/LoraLoader`, `ai/PngMetadata`, `ai/PromptParser` are in the row but get an L-UNCALLED census row and no guard, so they are not claimed here |
| `codex/foreword/ai/Gguf.codex` (Track D 10.1 item 16) | FREE -- val released 2026-08-16; item 16 landed at 15582 and its dequant residue after it. reek held it briefly for the residue and Damian moved reek elsewhere; reek CL 15583 is their parallel implementation and is superseded |

A claim nobody honours is worse than no claim (the `ds` cell 48 collision,
2026-08-11). If you are going into `GopDesk.codex`, say so first, and check
which `ds` cells are already spoken for in the Appearance section.

## Standing rules that gate nothing but bind everyone

Battery runs are Damian's (release proofs excepted, per the release
skill). Goldens stay parked during active GUI work. No new platform-wide
register. Prose about our own code is deleted in files you touch. The
em-dash stays banned. `-Jobs 8` on every parallel harness. Do not lower
`deck-headroom -MinMargin` to clear a red. `print-line` CONVERTS and
`print-line-raw` is byte-exact (inverted 2026-08-13; a wire emitter wants
`-raw`, everything else wants the plain name).

### Declined, and therefore not available work

Damian has ruled these out. They were carried in one agent's memory file,
which is why they kept being re-proposed by everyone else; they are here so
the ruling is reachable by whoever is about to spend a session on one.

- **Line-level debug info.** A statement about what Codex is for, not a
  scheduling call, so it does not come back when the calendar clears.
- **An app compile gate.** Compiler work must not be coupled to app drift.
- **The ARM64/RISC-V LIR retarget.** What landed stays; the rest is not
  reopening.
- **The store cutover** waits on infrastructure and is not available work.

Declined is not deferred. Do not re-propose one of these, do not build a
smaller version of it, and do not open a design that assumes it. If you
think a ruling has been overtaken by events, that is one sentence to Damian,
once.
