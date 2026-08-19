# CurrentPlan -- the shape and the priority order

*This file is the fleet's open work and its priority order. It carries no
history: shipped work is deleted, not memorialized (Perforce and the
GitHubUpdate reports are the record). Consolidated 2026-08-08 by reek at
Damian's direction: the five per-agent workplans and the findings-outbox
channel were retired, their open items folded in here, and their durable
facts moved into the reference docs that own them. A closed item is
DELETED, not annotated.*

*Pruned 2026-08-15 (1,100 lines to 670) and again 2026-08-18 by red at
Damian's direction. The second prune took the closed campaign accounts of
Track B's frame audit, Track C, Track D items 1-19, B4 steps 1-5 and the
per-agent lane rows, every one already recorded in the CL, the GitHubUpdate
for its cycle, or the doc named beside it (`HardwareSitting.md` for the
flights, `ExaminersAssay.md` for every guard, `VerifiedFormatParsing.md`
section 10 for the census, `DevelopersRulebook.md` "The repository wire").
If you are looking for how something was hunted, that is where it lives.*

**Where an item ORIGINATES in one app or quire, it lives in that
register** (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) and is named here only if it blocks a
track. There is still no platform-wide register beyond this file; do not
recreate `docs/PM/BACKLOG.md`.

## Where we stand, in three sentences

The compiler is a hard fixed point of itself on bare metal, Update 46 is on
the public mirrors (2026-08-17, seed `12B07296`), and the compiler has
booted the ASUS from bare UEFI, compiled its own source off the stick and
written it back byte-identical (A5). The trust audit is closed on the whole
compiler (diverse double-compiling, the `jonquil` runner, the independent
rechecker at one honest abstention), and Track D closed on 2026-08-16 with
every reached parser of foreign bytes guarded and the latent ones named.
What is left is metal-gated (the network and the stick, which advance at
sittings), the plugs register (val's lane, with items lent to every other
lane), and the unowned defects at the bottom of this file.

## Track A -- the stick is an OS

**Sittings are coordinated by red (Damian, 2026-08-18) and grouped, not
serial.** Every metal question below rides ONE diagnostic boot per sitting:
a lane with a metal question routes it to red with its arm and its expected
readings, red composes the boot (bank before you risk, L-BANK; rehearse the
exact bytes, L-REHEARSE), and Damian sits once. No lane proposes a flight of
its own. Standing metal questions today: the ASUS allocation grant (A8),
the largest GOP mode and `SetMode` (native GOP), the sink's 2.7 MB write
(WORKS-9), the e1000 ring (NIC-4), the TCP conversation (B3), ASDE (finding
4), and NIC-5 last.

- **The diagnostic stick (red, approved 2026-08-18): one image that
  detects the box and says what needs to happen.** The flown probes so far
  (`nicsitting`, `nicring`, `sinkladder`, `asdeflight`, the A8 allocation
  probe, the six-colour keyboard probe, the GOP mode arms) are each a
  one-question image built by hand. The design collects them into one
  template that a stranger can write to a stick and boot on a box we have
  never seen: enumerate firmware and devices, run every probe that applies
  with L-STATES failure states, bank the readings to the stick before any
  risky arm, and print a report that names what worked, what did not, and
  what to send us. Design: `docs/Designs/Active/OS/DiagnosticStick.md`
  (proposal first, then staged). **Steps 1, 3 (root, main 16822/16851) and 4
  (root, main 17203: `DIAG.RCP` in the image and bank, `diag.rehearsed`,
  `flash-usb -Rehearsed`, the UsersHandbook procedure, the release recipe)
  are landed; the stick FLEW 2026-08-18 (HardwareSitting.md). Step 2 lifts
  are per lane; step 5 is the grouped sitting.** The far end of the same road is a
  mini-agent on the stick that live-diagnoses in firmware and rebuilds the
  kernel; that is the direction, not this item.
- **WORKS-9 (reek). The USB mass-storage driver's second write, and the
  sink's own 2.7 MB write on metal.** `sinkladder.img` FLEW 2026-08-11 RED
  at or before the first allocation; the heartbeat arm is landed (main 15426)
  and the card is queued in `HardwareSitting.md`. Metal-gated; the arm and
  account are in `apps/works/works-backlog.md`. Its stub predates 15469 and
  15503, so any rebuild needs a fresh full-mission run (L-REHEARSE).
  **Damian's standing ruling: agents do not propose flights or sittings.**
- **A8 the desk build loop (fester).** Edit half done, hypervisor complete,
  arena measured at `-AllocPages 131072`; plan, roads and traps in
  `docs/Designs/Active/OS/DeskBuildLoop.md`. **Open: whether the ASUS
  firmware grants that allocation** (L-FREEDOM), a sitting question, and it
  rides red's grouped sitting as its own boot. The arm, the five-state colour
  table and the both-ways bed census are `HardwareSitting.md` "A8"; a refusal
  now paints DARK RED instead of sharing the anonymous in-stub blue, which is
  what makes it readable on a board with no serial port. NOT flight-ready
  (no `-Identity`, no source). After the answer, wiring
  `compile <path>` into the Console pane is a dozen lines (`DeskBuildLoop.md`
  step 2).
- **Native GOP resolution and diag word wrap (red). BED HALF DONE
  2026-08-15; the metal half is a stick rebuild and a photograph.** The stub
  picks the largest GOP mode on every non-`-EntryStart` payload; six bed arms
  in `build/gop-mode-arm.ps1`; account in `ExaminersAssay.md` "The GOP Mode
  Arms". Left: the ASUS's largest mode and whether AMI's `SetMode` honours it,
  which the bed cannot answer (L-FREEDOM); the next option-a stick built for
  any reason carries the change. Not a proposed flight. The `SetMode` half in
  `codex/build/cdxtopeScript.codex` is red's too.
- **EVERY UEFI STUB PATH CARRIES BYTES THAT HAVE NOT FLOWN** (fester main
  15503, red 15469): allocation panics now halt, three PEs grew a 512-byte
  section. The depot stick images (`build/boot/a5*.img`, `sinkladder.img`,
  `nicsitting.img` and the rest) predate both. **A rebuilt image is L-DECODE
  territory: rehearse the exact bytes in the bed before any flight
  (L-REHEARSE), and say in the flight card that the stub is new.**
- **Identity reconciliation (red, opened 2026-08-18 at Damian's direction;
  taken when commander duties are idle).** The shipped ceremony
  (`apps/works/GopWizard.codex`) was not built from `Designs/Done/OS/Identity.md`
  and the review measured it: the passphrase key is malformed
  (`wz-words-to-bytes` re-expands `hkdf`'s bytes into `[0,0,0,b]` x 32 at
  `:348`/`:493`; `IdentityManager.codex:385-393` fixed the same defect on a
  copy nothing calls); the unlocked seed is discarded (`Just (_)` at `:540`,
  `:563`) so nothing loads, signs or zeroes; passphrase and seed are never
  zeroed; `wz-auto-pass` opens any stick made with it; compare is not
  constant-time; no rotation or passphrase change; storage moved from
  DiskFacts to `IDENTITY.DAT` with magic/version/length checks only.
  Stages, none seed-affecting: ~~(1) `IDENTITY.DAT` v2 with the correct
  32-byte HKDF key, version 1 refused (no v1 exists outside this bench, Damian),
  a vector arm the wizard cannot dodge~~ DONE 2026-08-18 (red; account `ExaminersAssay.md`
  "The Identity Wrap Known Answer"; gate green, `Sut` == seed); ~~(2) keep the seed: `key-load` into the pinned
  region, `key-zero` on lock/shutdown, constant-time compare, zero the
  passphrase~~ DONE 2026-08-18 (red; `heap-scrub-to` in `HeapScrub.codex` scrubs the
  whole secret half of the ceremony under one mark, since a Text built by
  appending leaves every prefix on the heap; the ceremony order is now
  upstream, timezone, identity; account in `ExaminersAssay.md`); ~~(3) the bench auto-unlock removed or bed-only~~ DONE
  2026-08-18 (red; `wz-auto-try` runs only when CPUID.1:ECX[31], the hypervisor bit, is set, so metal always asks;
  `first-boot-ceremony` arms both sides); (4) trust-root
  write and passphrase change, on `IDENTITY.DAT` on the ESP. **RULED 2026-08-18 (queue 11, 12): the identity file
  stays on the ESP; auto-unlock is bed-only.**
- **A 16 MB stick image is in the archive and not in the depot**:
  `D:\Projects\stick-archive\stick-before-20260811.img`, the only copy of the
  hardware-written `IDENTITY.DAT`. Rulings queue 4.

## Track B -- the network (blu). Metal-gated: advances at sittings, not before.

The queue Damian draws from is `docs/Hardware/HardwareSitting.md`, "THE
SITTING QUEUE": five questions on one boot, in an argued order (bank before
you risk, L-BANK). NIC-1, NIC-2 and NIC-3 are ANSWERED on metal (the part
arrives cold, the poll calibration transfers, `e1000-init` does not hang);
NIC-4 flew 2026-08-16 and hung in `e1000-await-link`, fixed in 15588; the
rows are in `HardwareSitting.md`, not here.

- **The ring question is still open** (NIC-4 never painted its `dd=` map):
  `RDH` moved 0 to 15 with `RDT=15` on 08-15, which is either frames moving
  or `RDH` being unwritable as `CTRL` is, and the arm cannot separate them.
  It rides B3's boot rather than a flight of its own. Also open from NIC-3:
  aneg-done is never set on this part while `STATUS.LU` comes up, so
  `phy-bring-up` returns 0 against a link that is up.
- **B3, a real TCP conversation with a real peer.** The stack holds one in
  the bed over the e1000 (main 15013/15028) and the serving peer runs on both
  cards (`ExaminersAssay.md` "The Serving Peer"). The next sitting is the
  gate. Finding 4 (ASDE): `build/boot/asdeflight.img` is built, bed-verified
  both ways, and awaits a sitting.
- **NIC-5: what wedged the box on 2026-08-11.** Not `CTRL.RST` (discarded on
  this part). Terminal by construction, flies last.
- **B4, serve the repository protocol: steps 1-5 DONE in the bed (root,
  main 16636).** The wire is written down in `DevelopersRulebook.md` "The
  repository wire"; `EdgeMeshGameServers.md` Status names the surface Phase 2
  starts against. Step 6, the same conversation on the part, is B3's flight.
- **`ip-checksum` and `icmp-checksum` are the same function.** Collapsing
  them is NOT taken: `icmp-checksum` is the arm's independent witness, so
  whoever takes it keeps a second implementation somewhere. Unowned, low.
- **ICMP is send-only** (`icmp-parse` has a test and no production caller).
  Whether we answer a ping is rulings queue 1; nobody writes the receive side
  before it. `Tftp`, `Syslog` and `Icmp` have no production caller and are
  latent; `syslog-decode-bytes` builds its body with the quadratic `acc &`
  accumulator (CostModel 3.6), and whoever gives `syslog-parse` a production
  caller fixes that in the same change.

## Track C -- the trust audit (val)

C1 (diverse double-compiling) and C2 (the independent rechecker) are LANDED
and enforced: `docs/Designs/Active/Tools/IndependentRechecker.md`,
`docs/Test/Active/DDC-QUINE-ARM.md`, `OperatorsManual.md` "The witness has a
negative control". COMPILER-3 and COMPILER-5 are closed with no hole in the
fixed point (accounts in `OperatorsManual.md` and main 15410). Left:

- **The rechecker fork is Damian's**: whether the compiler should also EMIT
  its type-variable instantiation, or the plug keeps deriving it. Rulings
  queue 3.
- **C2.5 stage 4 (proof terms) stays deferred unless Damian calls for it.**

## Track D -- bytes we did not produce (RULED 2026-08-15, CLOSED 2026-08-16)

The census and both sweeps are `docs/Designs/Active/Features/VerifiedFormatParsing.md`
section 10 (141 parsers in the first sweep, 43 decoder chapters in the
second); 10.1 is the ranked queue and its last paragraph is the take order;
10.3 says how a row can be wrong. Every REACHED row is guarded and the guard
pattern is settled (`ExaminersAssay.md`: "The UDP Frame Guard", "The CDX
Input Guard", "The GPT Integrity Guard", "The FAT Geometry Guard", "The
Config-Descriptor Clamp"): clamp where the length decides a slice, refuse
where it decides WHERE a read lands, put the ablated call IN the arm, predict
every expected value before it runs.

**Still open in 10.1, all unowned unless named:** 8b (`VirtioBlk`'s
device-written used-ring index, waits for a bed), 9 (`AgentBundle` refusal
paths), 10 (`Modbus` and the `Coap` consumers), 18 (`OtaBoot boot-load`,
reek, LATENT: no production caller), the latent corpus rows 6, 7, 11 and 13,
and **20, a CLASS: 71 bounds guards that ADD can be overflowed, 34 with a
non-constant second operand, measured and NOT swept** (`ExaminersAssay.md`
"A Bounds Guard That ADDS Can Be Overflowed"; the fix is to SUBTRACT). Any
lane with slack may take one; the design says how.

## The lanes -- RULED by Damian 2026-08-15, re-pointed 2026-08-18

An item here is a pointer; the register named beside it is where the detail
lives. Re-read this table on every merge-down; it is the assignment, not a
suggestion. Each lane, in order:

| agent | now | then | standing |
|---|---|---|---|
| **blu** | the TCP byte loss below, fixed and measured (plugs 1.33 closed at main 16760) | **CostModel.md** 3.4 onward (approved 2026-08-18) | Track B: every metal question is routed to red's sitting |
| **val** | **The Modern Desk** (Damian, 2026-08-18): multitasking, a bottom taskbar, a system menu, the 3D surface working better; design `docs/Designs/Active/OS/ModernDesk.md`, rows in `works-backlog.md`. RULED 2026-08-18: cooperative panes with saved state; flex defaults to 1 (rulings 9). Stages 2 and 4 landed (16832, 16857) | stage 1 and the multitasking stages; the 3D surface targets with numbers | plugs lane HANDED to reek for the close-out (2026-08-18); rulings queue 3 still owed to val's lane |
| **fester** | **CrossLaneFilesystem.md step 0**, the rest of the unresolved-call class: the block half refuses already (16947) and plugs 1.42 is closed, so what is left is `real-approx-modes` and `uefi-read-key-nofirmware`, the only two tests still carrying one | plugs 1.3 is CLOSED (17200): the cons fault fixed, and the `rv-cond-is-frameless-safe` widening MEASURED AND DECLINED at 53 to 53 with an identical failure set for 32 bytes on a 225 KB image | A8: wire `compile <path>` once the ASUS allocation is answered at red's sitting |
| **reek** | **plugs close-out lane** (from val, 2026-08-18): the register in order, one entry at a time, text builtins first (1.31/1.36/1.37); fester keeps the riscv entries (1.3 family), blu the deck/arm64 tail; say so in status.json | (OTA socket wiring DONE 16793; ProtocolStack CLOSED 16780; item 20 CLOSED for the named files 16769) | WORKS-9 is metal-gated, routed to red's sitting; `ShellDslReadability.md` stays reek's |
| **red** | commander; sittings; the diagnostic-stick design; identity reconciliation stage 1 when idle | the native-GOP metal half and the `SetMode` half in `cdxtopeScript.codex`; `BatteryReorg.md` step 6 | `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` |
| **root** | **HardwareAbstractionLayer.md** (pool, per red; OracleCloudArm64 DEFERRED by Damian). **Board-threading phase DONE 2026-08-18** (main 16944-17016): the foreword got a linear GPIO `Pin` handle, and all nine board chapters now thread the shipped linear `UartPort` + `Pin` for UART and GPIO (`<b>-uart-open/write/close`, `<b>-gpio-open` + `<b>-pin-write/close`), verified by `build/boards-test.ps1` (9 green, 126 sub-tests). Board chapters are non-seed, no token. **Capability phase (foreword) DONE 2026-08-18** (main 17063, SEED 55E53A81 -> 7590CCA1): `[Gpio]`/`[Uart]`/`[Spi]` are rows in `Capability.codex` (cs-id 18/19/20, bits 27/28/29) mirroring `Flash`; the foreword gpio/uart/spi handle ops carry them; a `Device.Mmio`-only driver is refused CDX2031 (three `hal-launder-mmio-*` tests). check-effect-vocab regen 0 drift, gate green, seed self-verifies, 184 refusals ok. **Board wrappers promoted to the caps (main 17084)** and **the read side DONE (main 17139, SEED 318B2BF6)**: the checker mints owners for the linear components of a returned tuple (let-pattern, `when` on the call, act-bind then `when` on the name; `_` at a linear position CDX2063), the tuple-type parser accepts `linear`, and `Board.codex` carries `gpio-read`, `uart-recv`, `SpiTxn` + `spi-select/transfer/deselect` (`hal-tuple-linear`, `errors/hal-tuple-{leak,dup,wild,owner-leak}`, `errors/hal-spi-cs-leak`). Also 2026-08-18: `capability-doors.expected` re-recorded for the 17063 rows (main 17135, was red since 17063). **Board threading of the read side DONE (main 17146/17148/17156):** `<b>-pin-read` on 8 boards, `<b>-uart-recv` on the 3 with a receive primitive, the linear SPI transaction on the 6 SPI boards; boards-test 9 green, 143 sub-tests. Polled receive + `<b>-uart-recv` on Fe310/Rp2040/Stm32L4 landed main 17164 (146 sub-tests). **`[I2c]`/`[Adc]`/`[Power]` rows + `I2cBus`/`AdcUnit` handles landed main 17174 (SEED 5B2DE4E6; the table's unassigned bits 1/2/13, so COMPILER-17, the imm32 grant hazard above bit 30, stays LATENT and recorded). I2C bus threaded on the five I2C boards and the ADC unit on RP2040 (main 17183; 152 sub-tests). nRF EasyDMA receive landed main 17194 (154 sub-tests; every UART board receives through the handle); `TinkersToolbox.md` HAL section and counts refreshed. **Open next:** Power = rulings 15 (waiting on Damian); ADC on the boards that gain an ADC driver. `build/boot/diag/**` stays root's | then: HAL follow-ons above, next pool item, or red's call | plugs 1.34 is rulings queue 10 |

**Plugs are reek's lane for the close-out** (from val, Damian's direction-08-18: 19 entries open, two or three fleet-days to a green register): when entry closes, reek takes the next open unclaimed one in
`codex/plugs/plugs-backlog.md` in the register's order and says so in status.json. Entries other lanes hold
are named in the register (1.33 blu, 1.38 and 1.3 fester, 1.36 and 1.32
reek, 1.34 root). `codex/plugs/zig/**` came in with Steve Howell's PR 66 and
is ORDINARY FLEET CODE, edited like any other plug (Damian, 2026-08-18: the
loan was for his early updates, those are absorbed, and it is over). This
supersedes both the 2026-08-16 "not a fleet edit" rule and the "ours to gate
loosely, no rigour beyond a smoke" reading that replaced it. Credit Steve in a
CL that changes what he wrote and flag it in the next GitHubUpdate; that is
courtesy, not a gate.

## Approved campaigns and the pool (Damian, 2026-08-18)

Damian approved every open design campaign in `docs/Designs/Active/` as
available work; a lane that empties draws from the pool below, in this order,
and says so in the table above. Assigned in the table: ProtocolStack + OTA
(reek), CrossLaneFilesystem riscv (fester), CostModel 3.4+ (blu),
ProportionalDecks (root), PlugDeepRecursion (val), the diagnostic stick and
BatteryReorg step 6 (red).

**The pool, unowned:** `HardwareAbstractionLayer.md` (nine board chapters);
`GameEngine.md` phase 2 items; `EdgeMeshGameServers.md` phase 2 (its bed
surface exists, B4); `ComplianceEvidence.md` (the evidence plug SHIPPED 2026-08-18, root: `codex/plugs/evidence/`; open there: FactStore ingestion, per-board residuals, the catalog merge);
`ThreatModel.md` (design, not started); `DeviceEmulationCatalog.md` queue;
`ShellDslReadability.md` (reek's, cheapest win first); **a bulk-output path
for the QEMU host** (Damian, 2026-08-18: "add the structural fix"). Seed-affecting
campaigns take the token per CL as usual.

**The QEMU bulk-output path, blu, LANDED 2026-08-18 (CL 17198).** Two causes,
and the second was the larger. The emit side: `emit-com1-init` already writes
FCR = 0xC7, so the 16550 transmit FIFO was on the whole time and LSR bit 5
means the whole 16-byte FIFO is empty; `__write_binary_buf`'s legacy arm now
polls once and `rep outsb` a burst of up to 16, which is one exit rather than
sixteen. codex-vm's COM1 dispatch had no `StringOp` arm and would have written
one byte out of RAX and skipped the rest, so `tools/codex-vm.c` gained the
batching arm; output is byte-identical on both hosts (`A406423B53D7846A`).

The host side was worth more than the guest side. `Invoke-VmCompileFallback`
set `ReadTimeout = 2000` once SIZE's declared payload was complete and then
did one more blocking read as a trailer drain, so **every QEMU compile paid a
flat two seconds waiting for a trailer that is not there**. At 250 ms that is
~1.75 s back.

Measured on `plug-oracle-arith` (103,645 bytes out), WHPX, through
`vm-config`'s own boot path: **4.84 s -> 2.93 s**. The emit change alone moved
WHPX 5.10 -> 4.47 on a fixed source and left TCG flat at 4.41, which is the
control the exit theory predicts. What remains is not the output path:
QEMU process start 565 ms, guest boot to READY 404 ms, send 260 ms, compile
1,419 ms, teardown 17 ms. `vm-differential` reports qemu 4s where it read 5s.

**Still open, and it is the one that would remove the rest.** A `rep outsb` to
QEMU's Bochs debug console (port 0xE9) needs no LSR poll and has no FIFO
bound, so the whole buffer leaves in ONE exit rather than one per 16 bytes.
It needs a presence probe (0xE9 reads back 0xE9 when `-debugcon` is wired,
floats on real hardware, the same shape as the existing 0x511 probe) and a
reader side in the harness, since output would no longer arrive on the serial
socket that carries the SIZE header. Not started.

**The original statement of the item, for reference.** On a box without `tools/codex-vm.exe`
(Steve Howell's, any Linux host) `build/vm-config.ps1` falls back to QEMU and
`compile.ps1` runs the whole Self-Host Compilation Protocol over two serial
chardevs on TCP (`vm-config.ps1` `Invoke-VmCompileFallback`). Every output
byte is one COM1 port write, one VM exit, so a ~2 MB CDX unit pays ~2 M
exits; codex-vm never sees this because it preloads the input into guest
RAM and takes output through its BLIT bulk path. Measured 2026-08-12 the
exit tax made KVM under WSL2 SLOWER than TCG (18-63 s against ~11 s).
`CODEX_ACCEL` and building codex-vm shave it; only a bulk path removes it.
The work: a guest-side bulk emit for the QEMU host (one exit per buffer,
not per byte: a port-triggered DMA of a guest buffer, or a chardev/device
QEMU can serve such as ivshmem or virtio-serial), the reader side in
`Invoke-VmCompileFallback`, and a timed before/after on one CDX compile
under TCG and under KVM. Touches the emit path, so it is seed-affecting and
takes the token; the codex-vm path must stay byte-identical (`Sut === seed`
measured, not predicted).

## Registers carrying unowned work that wants a lane

Named here because a register nobody owns is a register nobody reads.

- **`GitHubUpdate44.md`, open from 43:** nothing exercises the guard page
  under a genuine allocation walk since the LEAP arm was retired.
- **`docs/Designs/Active/Compiler/CrossLaneFilesystem.md`** (fester): steps
  1-5 DONE on BOTH lanes (arm64 step 4 main 16224, the riscv twin main
  16474), and the servicer REFUSAL arms are measured on all three lanes as
  of 2026-08-18 (`codex/test/fs-deny-runtime`, read arm added and ablated).
  **Open: step 0 alone**, the soft `[WARN]` where the design prescribes a
  hard failure. The block helpers are conditional now (2026-08-18), so the
  `vb-*` warning is gone from every compile on both lanes; what blocks the
  refusal is `plugs-backlog.md` **1.42**, unit constructors emitted as calls
  that resolve to nothing, with eleven tests passing by accident on top.
  Recorded, not chased: plugs 1.29, arm64 effect-op slots silently capped
  at 16.
- **`docs/Designs/Active/OS/OracleCloudArm64.md`**: **DEFERRED by Damian
  2026-08-18** ("basically a dead project, you can defer that"). Every LOCAL
  half is closed (reek; both residues root, main 16697: the stub publishes
  the DMA floor and both virtio drivers read it). The ~5-connection ceiling
  per boot was root's next item and is now deferred with the design; upload,
  VCN and the external smoke test (rulings queue 6) are moot while deferred.

## Rulings Damian owes (the only queue that blocks)

Each of these has a lane waiting on it or a doc that cannot be settled
without it. Nothing else is asked. Numbers are stable; a ruled item keeps
its number and loses its text.

1. **Answer a ping?** ICMP receive is a capability decision before anyone
   writes `icmp-parse`'s production caller (Track B).
2. **Learn ARP only from replies we solicited?** `net-process-arp` still
   learns from any frame; narrowing it is a trust-model change and was
   deliberately not slipped into the crash fix (blu, main 15310).
3. **Emit the type-variable instantiation from the compiler, or keep
   deriving it in the recheck plug?** (Track C, val's fork.)
4. **A depot slot for `stick-before-20260811.img`** (16 MB, the only copy of
   a hardware-written `IDENTITY.DAT`), or leave it in the archive.
5. Ruled 2026-08-16 (zig 0.16.0 at `D:\zig-0.16.0`).
6. **OCI account access for `OracleCloudArm64.md` Phase 5b-5d.** Every local
   half is closed; upload, VCN and the external smoke test need the account
   and nobody else can do them.
7. **`check-vm-differential` has no retry** on the arm its own comment calls
   hang-prone; a QEMU timeout reds the gate for everyone. Adding one needs the
   line drawn between "arm produced no binary" (may retry) and "hosts
   disagree" (must stay fatal). (red, 2026-08-15.)
8. **Should `p4-stale-check`'s dropped-add scan FAIL the gate?** It warns,
   deliberately, because scratch files land in the same list; P-CLOBBER calls
   the dropped add the worst trap in the file. A middle option: fail only on
   tracked extensions. (red, 2026-08-15.)
9. Ruled 2026-08-18: `widget-panel` flex DEFAULTS TO 1 (val, ModernDesk stage 1). **It already did** (`Widget.codex:44`, unchanged since main 16020), so the ruling closes the stage with no code change. The question reached Damian because ModernDesk.md stated the default backwards; `apps/browser/browser-backlog.md` BROWSER-2 had it right all along and warned against flipping it to 0. Correction recorded in ModernDesk.md.
10. **plugs 1.34** (root, routed via red 2026-08-17): on ARM64 the boundary
   between a program and the block device is the effect system, and
   `peek-32`/`poke-32` carry an empty effect row, so a `Device.Block` row on
   the driver is walked around by inlining four pokes. (a) gate the MMIO
   window in the effect system (raw accessors refuse or require `Device.Mmio`
   in the device window; seed-affecting), or (b) a real EL0 privilege
   boundary, a process-model campaign. Until ruled, the ARM64 capability gate
   gates the `block-*` builtins only.
11. Ruled 2026-08-18: the identity file stays on the ESP (`IDENTITY.DAT`).
12. Ruled 2026-08-18: the bench auto-unlock is bed-only (hypervisor bit; red, identity stage 3).
13. Ruled 2026-08-18: (a), the compiler stops emitting an application for a
   unit constructor, and BUILT the same day (fester). `unit-real-arith` went
   28 unresolved calls to 0 and the riscv lane 26 distinct unresolved names
   across 11 tests to 3 across 2, both batteries unchanged with identical
   failure sets. Two wrong versions came first and every test passed under
   both; the account, and the two IR traps that caused them, are
   `codex/plugs/plugs-backlog.md` 1.42.
14. Ruled 2026-08-18: **warnings are warnings.** They do not gate the build; they are AUDITED AT THE RELEASE GATE. Create the self-compile diagnostic log now (retained, one entry per warning code with the ruling on it as each is made; no ruling is owed on any of them today). blu, COMPILER-16.
15. **HAL Power: how does `sleep-deep` prove no handle is open?** (root, 2026-08-18.) `[Power]` has its capability row (main 17174) and no ops. The design (`HardwareAbstractionLayer.md` "The sleep rule") makes the BOARD linear: `board-open : linear Board`, every `*-open` threads the board and hands it back, `sleep-deep : linear Board -> Nothing` consumes it, so a live handle on the path to sleep is CDX2063 with no new checker work. The cost is that every `*-open` in the foreword and the nine board chapters changes signature (`gpio-open : linear Board, ... -> (linear Board, linear Pin)`, tuple returns the checker now tracks) and every caller re-threads. The cheap alternative is a `sleep-deep : linear Board` where `board-open` is only required for sleep, which proves nothing about handles opened without it. (a) is the design and the honest one; (b) is a row with a hole. Root will build (a) unless told otherwise; asking because it re-signs every board wrapper landed this week.

## A TCP send loses bytes with both ends reporting success (FIXED 2026-08-18, blu; found by val 2026-08-17)

A 16 MB send over the guest TCP stack intermittently arrives short with a
clean close at both ends: `guest built 16777216, guest sent 16777216, host
received 16629200`, twice in six runs on a quiet box, shortfalls from 16,416
bytes to 4.9 MB across the day. The guest's send accounting is complete
(`net-io-send-raw-checked` answers `is-complete` True), so the loss is
between that accounting and the host socket: `codex/os/net` or the NE2K
path, not plugs. Eliminated: a host-side read timeout or reset swallowed by
the harness (that catch is a refusal, `exit 8`, main 16489, and does not
fire); the plug's send loop stopping early. NOT eliminated: host contention
(failures with 0 `codex-vm` running, but the six-run batch began with one
still shutting down). **The instrument is landed (main 16515): any
`codex/plugs/img/test-img.ps1` run that loses bytes exits 9 and names all
three counts**, so whoever takes this starts with a reproducer.

**FOUND AND FIXED 2026-08-18 (blu). Reproduced with exact accounting, and
the loss is host-side in `tools/codex-vm.c`, not in `codex/os/net`.**

The guest's FIN handler called `nat_tx_flush` ONCE, a single non-blocking
`send()`, then `shutdown(SD_SEND)`. A socket whose send buffer is full at
that instant, which is the normal state at the end of a bulk transfer,
takes part of the buffer and refuses the rest. Those bytes sit in codex-vm's
own `txbuf`, which the host kernel has never seen, so the comment's claim
that they "drain normally" was false for exactly them. `nat_poll_rx` then
flushed only states 2 and 4, so a state-3 connection was **never flushed
again**, and VM exit skipped state 3 for the same wrong reason. They were
freed unsent, both ends reported a clean close, and nothing counted them.

**The measurement.** A byte census now counts every discard site on the
guest-to-host path and prints at exit as `NAT TX BYTES: seg= queued= sent=
drop-noconn= drop-badstate= drop-oom= drop-freed= (reap= exit=)`. A clean
run reconciles exactly; the failing run of five read
`seg=16777216 queued=16777216 sent=16256800 drop-freed=520416`, and
16256800 + 520416 = 16777216 with every other counter zero.

**The arm, for whoever touches this next.** The natural failure is about
one run in five and a uniformly slow reader does NOT provoke it: the guest
sends at roughly 120 KB/s, so the host reader is never the bottleneck. What
provokes it deterministically is a reader that STALLS near the end, so the
socket is full at the moment the FIN lands. A one-shot 15 s stall at 16.0 MB
of a 16 MB send failed every time before the fix and passes after it.

**The first fix was incomplete and the census said so**, which is the
argument for building the instrument before the repair: with the FIN
shutdown deferred and state 3 flushed in the poll, the stall arm still lost
580,616 bytes, and the split counter named the site as `exit=` rather than
`reap=`. VM exit was giving up after a 2 s busy spin. It now waits on
writability for up to `NAT_EXIT_DRAIN_MS`, breaks out when a writable socket
takes nothing (the peer is gone, not slow), and the reaper's idle clock is
pushed by bytes actually moving.

**What was read on the way, and is still true of `codex/os/net`:**

**A REFUSAL IS THROWN AWAY. Still open, still a defect on its own terms,
but it is NOT what lost these bytes and a count of it would have read
zero.** `net-send-raw` (`X86_64IPCHelpers.codex:2213`) returns the frame
length unconditionally after kicking TXP and never reads the NE2000's
transmit status, so on this reproducer the guest has no transmit failure to
report even in principle. The planned guest-side refusal counter was
dropped for that reason, measured before it was built rather than after.
`flush-transport-outbox`
(`codex/os/net/NetIO.codex:70`) binds the driver's answer as `flushed` and
never reads it, then clears the outbox unconditionally. `flush-outbox-loop`
does sum what `net-driver-send-frame` returns, so the answer exists and is
discarded. Both drivers refuse: `ne2k-send-frame` answers 0 over
`ne2k-max-frame`, `e1000-send-frame` answers 0 when absent, empty or
oversize and otherwise returns `e1000-await-tx`'s verdict directly. Nothing
above the driver can tell a sent frame from a dropped one. That is L-REFUSED
in the same chapter that already paid for it once.

**The obvious fix is wrong and must not be taken.** Making the refusal fatal
would break a working path: `E1000e.codex`'s own prose records that
`e1000-await-tx`'s budget can expire while the frame goes out anyway, so on
a slow link every send reports failure and the data is fine. What is wanted
is a COUNT of refusals surfaced to the harness, not an abort. That remains
true and remains unbuilt; it is now a discipline item rather than this
defect's fix, and on the e1000 rather than the NE2000, since that is the
only one of the two whose driver reports anything.

**Eliminated by reading, so nobody re-buys them:** the send accounting is
honest (`net-io-send-chunk-checked` refuses on `net-rexmit-full`, on a
closed connection and on an empty outbox, so `is-complete` True means every
chunk reached the wire); data segments ARE armed for retransmission
(`net-rexmit-arm`, `NetworkStack.codex:436`), so this is not a stack that
cannot retransmit; and `net-send` refuses a full queue with the session
unchanged, which the checked caller reads.

**Where to point next.** The UNCHECKED `net-io-send-chunk`
(`NetIO.codex:182`) stops sending on `net-rexmit-full` and returns
silently, the same L-REFUSED shape, for any caller on that path. And ACKs
are processed ONLY when the queue is full, because `net-io-send-drain`
returns immediately unless `net-rexmit-full` (`NetIO.codex:156`), so nothing
ticks through a bulk send that never fills the queue.

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `codex/foreword/core/VirtioBlk.codex`, `codex/plugs/arm64/Arm64Runtime.codex` block helpers and fs servicer, `codex/compiler/opening.codex` `ir-emit-roots`, `build/test-cross-disk.ps1`, `codex/test/{fs-layer,fs-servicer}.*` | fester, 2026-08-16, CrossLaneFilesystem step 4 (landed 16224) and the RISC-V twin. The `Arm64Runtime` claim is the block/servicer sections only and is **by root's agreement**; the rest of that file is root's |
| `codex/plugs/riscv/RiscVRuntime.codex` block helpers (the twin) | fester -- DONE, landed main 16474 on 2026-08-17; this row said FREE for a day after. `fs-layer`, `fs-servicer` and `fs-deny-runtime` all pass on the riscv lane through `build/test-cross-disk.ps1` |
| `codex/os/kernel/{VirtioNet,VirtioBlk}.codex`, `codex/plugs/pe/Arm64PeWriter.codex`, `build/build-arm64-img.ps1` and its generator | FREE -- root closed the OracleCloudArm64 DMA/timeout residues 2026-08-18. Kernel-side `codex/foreword/core/VirtioBlk.codex` is fester's |
| `tools/codex-vm.c` | **blu, 2026-08-18**, the NAT guest-to-host byte census and the deferred-shutdown fix above. Previously: FREE -- root landed the PCI-bridge device model 2026-08-18 (DeviceEmulationCatalog queue): `-pci-bridge` puts a header-type-1 bridge on bus 0 -> bus 1, config is bus-aware now, `codex/test/pci-bridge-scan` shows `pci-scan-all` descends (count 5 bus1=1 with the flag, 3/0 without). One owner at a time; announce |
| `build/test-cross-batch.ps1` | **fester, 2026-08-18**, the parallel-block throw that ended two batteries and stranded five QEMU guests at 115,597 CPU-seconds. FREE again once that lands; the same class is unfixed in `sweep-app-classes.ps1` and `test.ps1` and is recorded in `ExaminersAssay.md` for whoever owns those |
| `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` | red |
| `build/boot/diag/**` (`Diag.codex`, `diag-arm.ps1`, `diag.img`, and the probes as they are lifted into stages) | root, 2026-08-18, DiagnosticStick.md step 1 (landed root 16819). Step-2 lifts by the lane that flew the probe, coordinated with root |
| `apps/works/GopDesk.codex` | FREE -- announce before you start, and check which `ds` cells are already spoken for in the Appearance section (the cell 48 collision, 2026-08-11) |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- announce |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/test/cost/**` and `docs/Designs/Active/Features/CostModel.md` | blu. 3.3 shipped at main 16020, rule 3 at 16118; what is left of it is COMPILER-7 |
| the integer-literal lexer and text emitter; `codex/plugs/csharp/**` and the `build/` DDC harness; `codex/plugs/recheck/**` | val, lane ownerships rather than open work |
| `codex/plugs/**` and `codex/plugs/plugs-backlog.md` | **val, 2026-08-16, red's assignment.** The register is the lane. Includes `codex/plugs/zig/**` (ordinary fleet code, Damian 2026-08-18); excludes the entries other lanes hold (named in the lanes table) |
| `codex/plugs/spirv/**` (plugs-backlog 1.24) and every `run.ps1` under `codex/plugs/` (1.15) | val, with the plugs lane |
| `build/plug-oracle-test.ps1`, `codex/test/plug-oracle-arith.*` | **blu, 2026-08-18.** val released both: the zig wiring they were held for landed at 15687 and 1.13 is done. val's claim is per-ENTRY, not tree-wide |
| `deck-headroom` | fester |
| `codex/foreword/shell/**` and `codex/build/*Script.codex` generators (the ScRaw removal and the readability campaign) | **reek, 2026-08-16, by Damian's direction** ("you can own this bit, as no other agents are actively workin on this code"). Catalog and order: `docs/Designs/Active/Build/ShellDslReadability.md` |
| `codex/foreword/compress/**` (`Deflate`, `Lz4`, `Lz77`, `Rle`, `Brotli`) and `core/OtaBoot.codex`, `core/Aes256.codex`, `core/KeyboardLayout.codex` (Track D 10.1 item 18) | **reek, 2026-08-16**, red's routing. Seed-reachability is measured per file before each half, not assumed from the row |
| `codex/foreword/core/FactDisk.codex`, `core/SourceDefWire.codex` | FREE -- announce, and it takes the token (seed-affecting) |

A claim nobody honours is worse than no claim (the `ds` cell 48 collision,
2026-08-11). Announce before you go into a claimed or FREE-announce file.

## Standing rules that gate nothing but bind everyone

Battery runs are Damian's (release proofs excepted, per the release
skill). Goldens stay parked during active GUI work. No new platform-wide
register. Prose about our own code is deleted in files you touch. The
em-dash stays banned. `-Jobs 8` on every parallel harness. Do not lower
`deck-headroom -MinMargin` to clear a red. `print-line` CONVERTS and
`print-line-raw` is byte-exact (main 14809, 2026-08-13; a wire emitter wants
`-raw`, everything else wants the plain name; `DevelopersGuide.md` "Effects
and Act Blocks" is the account and said the opposite until 2026-08-18).

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
