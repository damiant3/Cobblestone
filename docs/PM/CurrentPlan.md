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
  `docs/Designs/Active/OS/DeskBuildLoop.md`. **The allocation question is
  ANSWERED 2026-08-19: GRANTED.** `desk.img` at 131072 pages reached the
  first-boot wizard on the ASUS and the refusal colour never appeared
  (`HardwareSitting.md` "A8", flown by red). L-FREEDOM is closed for this
  one. The arm, the five-state colour table and the both-ways bed census
  stay in `HardwareSitting.md` "A8"; a refusal paints DARK RED instead of
  sharing the anonymous in-stub blue, which is what makes it readable on a
  board with no serial port. **`compile <path>` is WIRED (fester, 2026-08-19,
  `GopConsole.codex` `gcon-compile`) and only half of it is proven.** Two bed
  arms: no argument answers `compile <path>`, an argument answers `VT-x is
  locked off in firmware`, so the verb dispatches and the refusal fires before
  any file is touched. Everything past that refusal has never run: codex-vm is
  itself a hypervisor and its guest sees no VT-x, so the read, the conversion
  and the launch wait for metal. The image is still NOT flight-ready for
  anything else (no `-Identity`, no source), and its recipe is kept current on
  the seed.
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
  `first-boot-ceremony` arms both sides); ~~(4) trust-root
  write and passphrase change, on `IDENTITY.DAT` on the ESP~~ DONE 2026-08-20 (red, 17792;
  `IDENTITY.DAT` v3 carries a 64-byte self-vouch over the public key, signed at
  keygen and verified at parse, v2 refused like v1; P on the unlocked screen
  rewraps under a new passphrase with fresh salt/iv; account in
  `ExaminersAssay.md` "The Identity Wrap Known Answer". The bed cannot type
  printables, so the P-key screen itself rides the same widgets first boot
  proved on metal; the wrap, vouch and refusal semantics are the serial arms').
  **RULED 2026-08-18 (queue 11, 12): the identity file
  stays on the ESP; auto-unlock is bed-only.** All four stages done; the item
  closes. Rotation (RotationFact) was never in the stages and stays with the
  design.
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
| **blu** | **NIC-4 awaits sitting 5, and the instrument is built.** Sitting 4 flew ED90B46A and answered ARRIVED-BUT-INVISIBLE (gprc=1 rnbc=0, rdh=1, ddset=0). Stage 2 landed 17742 on image **27326F86**, rehearsed 20 of 20 across both beds and carrying reek's sink chunk knob, so one image flies both halves of the sitting. **The discriminator we had agreed on was wrong and would have read backwards on metal**: it was a raw all-sixteen-zero test on receive descriptor 0, but `e1000-build-rx-descs` (`E1000e.codex:537`) writes the buffer address into bytes 0..7 itself and zeroes 8..15, so all-zero cannot happen while our ring build works and the test answers "the part wrote our ring" on every flight. The row now counts nonzero bytes in the WRITEBACK half (8..15) only, keeps the full dump for the eye, and carries `buf=` as the other half's control. It rides the QR because the summary is built from each stage's FIRST glass line, which is also why the ladder was NOT reordered. GPRC is read twice, so a count can no longer belong to nicinit's ring two stages earlier. `nicring listen=0` and the `nic-noread` arm are the positive control: our own `e1000-poll-raw` recycles the descriptor it took a frame from, so a successful listen leaves wb=0 and the bed could not otherwise express a writeback at all. Caveat to state before flying: `match=y` compares RDBA against our ring POINTER and is valid only while the guest is identity-mapped, so it is not by itself proof of correct aim | **17603 LANDED (main 17751), new seed 0A37A56F.** `infer-and` records its boolean arm, so `bounded none` and `punctual` no longer refuse a body joining two conditions with `&`. Proven by the control, not the green: the same three shapes are refused with CDX6101 at exit 4 against the depot seed and clean at exit 0 against the SUT | CostModel: `fixed` still unshipped; `bs-alloc` is measured on 17 of 262 builtins. `&` keeps its overload and still carries the two-consumers-read-absence-oppositely hazard: `cost-binop-allocates` reads absent as "assume allocating", `check-rt-no-alloc` reads RECORDED as "refuse", so filling the gap for one can invert the other |
| **val** | **The Review pane's open list** (red, 2026-08-19, with their claim on `GopReview.codex` released): paging past one page DONE 2026-08-19, wrapping long text DONE 2026-08-20, leaving reason text on a cast verdict and the supersession view through `Historian`. `GopFacts.codex` stays red's. **THE EDITOR IS DONE**: WORKS-14, WORKS-15, WORKS-18 and WORKS-19 are all closed and their rows deleted (2026-08-19), and what outlives them is `works-desk-contract.md` section 0.6 -- why the annotation join must not use `CodeBrowser`'s `SourceIndex`, the CRLF-as-space trap, the author-before-body trap, `GopFacts` rather than `disk-load`, and the UEFI recipe. **One warning from that work reaches the Review pane too:** with no identity loaded the trust lattice is empty and NOTHING clears the threshold, so a trust-filtered surface shows nothing at all, and the default `desk.ps1` bed loads no identity. **THE MODERN DESK IS DONE, every stage** (2026-08-19); its account is `docs/Designs/Active/OS/ModernDesk.md` and its closed rows are in `works-backlog.md`. Two things it left that are NOT the Editor's and are still open: the Browser may only ever be the topmost heavy pane, because it rebuilds its state at the current frontier on every event (`BROWSER-5` in `apps/browser/browser-backlog.md`), and **the stack-depth instrument cannot be switched on at all.** The Monitor's `stack` row reads `stack-min-rsp-addr` against `ram-size-addr` and the boot prologue seeds that cell with `ram-size` unconditionally; only the per-function prologue narrows it, under `trace-alloc`, and **nothing sets `trace-alloc`.** Re-measured 2026-08-19: `compile.ps1 -Trace` does put `trace` in the mode string, `has-mode-flag` never asks for it, and `compile-to-cdx-with-exit-mode` has no parameter that could carry it, so it passes a literal `False` to the emitter (`opening.codex:1319`, `:1325`). The switch is dead end to end, not merely unthreaded. Threading it is a COMPILER change, seed-affecting, **unowned and not started**; the desk row and WORKS-32 both start answering the day it lands, with no further change in the desk. | **The old desk body is deleted, not archived here.** Stage-by-stage: DELETED, see the design | **GameEngine.md phase 2** (shadow campaign, stage 1 landed 17190; the 2.4x ratio at 17335); rulings queue 3 still owed to val's lane; plugs lane HANDED to reek for the close-out (2026-08-18) |
| **fester** | **BROWSER-5's paint half** (red, 2026-08-19): the first-paint 12-row jump, isolated to paint at the `desk-bro-h` boundary with layout exonerated by identical rects across a dispatch. Start at the paint; the arithmetic half is landed at 17746 and the numbers that found it predate 45/47, so re-measure before debugging | **ProductBuilder stage 5 (17782, main 17784)**: campaigns derived from the ledger rather than submitted, with three arms for the ways a campaign passes by measuring nothing -- no severe fault seeded, no release or notary mutation written, and only the four novel genera it went on to kill. Four readings recorded in `codex/product/product-backlog.md` 5.2 and 5.3 rather than papered over. Stages 0-5 are landed; stage 6 is rulings queue 16. BROWSER-2 (17764), BROWSER-1 (17758), BROWSER-5 arithmetic (17746), BROWSER-7 (17735), WORKS-17 (17716, 17702), BROWSER-6 (17682), WORKS-47 (17658), WORKS-45 (17640), WORKS-23 (17591), WORKS-13 (17615) and `PerforceProcess` P-SEEDSWAP (17762) landed this session. EdgeMeshGameServers phase 2 COMPLETE; CrossLaneFilesystem step 0 (17535); arm64 saturating ops (17483); A8 `compile <path>` wired (17385), half proven | The browser register holds only BROWSER-5's paint half and BROWSER-4 (network-gated). WORKS-24 rides a sitting, WORKS-16's live thread is blu's, and WORKS-17's syntax half is a `Theme` decision. **The rulings queue carries work items that no register row shows**, which is how stage 5 read as an empty pool for an afternoon: queue 16 said in writing that it could go first, and nothing in a lane row said so |
| **reek** | **plugs close-out lane** (from val, 2026-08-18): the register in order, one entry at a time, text builtins first (1.31/1.36/1.37); fester keeps the riscv entries (1.3 family), blu the deck/arm64 tail; say so in status.json | (OTA socket wiring DONE 16793; ProtocolStack CLOSED 16780; item 20 CLOSED for the named files 16769) | WORKS-9 is metal-gated, routed to red's sitting; `ShellDslReadability.md` stays reek's |
| **red** | commander; sittings; **the Review pane** (Damian, 2026-08-19: the repository protocol's user-facing half on the desk; stages 1 and 2 DONE 2026-08-19: lists proposals and verdicts from the medium's fact partition and casts a verdict signed by the box identity through the new `key-sign-bytes` primitive in `Foreword chapter Identity`, Damian's ruling that the primitive beats the user-space workaround; `works-backlog.md` WORKS-44 has the account, WORKS-46 the gap that keeps it empty on a USB stick: DiskFacts reads through the kernel's ATA syscalls); identity stage 4 (trust-root write, passphrase change, on `IDENTITY.DAT` on the ESP; stages 1-3 landed 2026-08-18); the diag step-2 lifts red owes (xHCI truth, keyboard, MSC align, largest GOP mode + `SetMode`) so the first grouped sitting carries every standing question | `BatteryReorg.md` step 6 | `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**`; Update 47 PUBLIC 2026-08-18 (github 69cd9ce8, seed 90646EEB); **interim mirror push 2026-08-19 (github a061c173, seed 800A7683) carrying Steve Howell's PR 69, PR 71 and issue 72, all closed with the commit**; the Update 47 "preview gate on a pre-convergence stage" question is settled by blu's measurement (compile(A)=B, compile(B)=B, ONE fixed point; the preview ran on stage1 of an older seed, which is what the gate said) |
| **root** | **HardwareAbstractionLayer.md** (pool, per red; OracleCloudArm64 DEFERRED by Damian). **Board-threading phase DONE 2026-08-18** (main 16944-17016): the foreword got a linear GPIO `Pin` handle, and all nine board chapters now thread the shipped linear `UartPort` + `Pin` for UART and GPIO (`<b>-uart-open/write/close`, `<b>-gpio-open` + `<b>-pin-write/close`), verified by `build/boards-test.ps1` (9 green, 126 sub-tests). Board chapters are non-seed, no token. **Capability phase (foreword) DONE 2026-08-18** (main 17063, SEED 55E53A81 -> 7590CCA1): `[Gpio]`/`[Uart]`/`[Spi]` are rows in `Capability.codex` (cs-id 18/19/20, bits 27/28/29) mirroring `Flash`; the foreword gpio/uart/spi handle ops carry them; a `Device.Mmio`-only driver is refused CDX2031 (three `hal-launder-mmio-*` tests). check-effect-vocab regen 0 drift, gate green, seed self-verifies, 184 refusals ok. **Board wrappers promoted to the caps (main 17084)** and **the read side DONE (main 17139, SEED 318B2BF6)**: the checker mints owners for the linear components of a returned tuple (let-pattern, `when` on the call, act-bind then `when` on the name; `_` at a linear position CDX2063), the tuple-type parser accepts `linear`, and `Board.codex` carries `gpio-read`, `uart-recv`, `SpiTxn` + `spi-select/transfer/deselect` (`hal-tuple-linear`, `errors/hal-tuple-{leak,dup,wild,owner-leak}`, `errors/hal-spi-cs-leak`). Also 2026-08-18: `capability-doors.expected` re-recorded for the 17063 rows (main 17135, was red since 17063). **Board threading of the read side DONE (main 17146/17148/17156):** `<b>-pin-read` on 8 boards, `<b>-uart-recv` on the 3 with a receive primitive, the linear SPI transaction on the 6 SPI boards; boards-test 9 green, 143 sub-tests. Polled receive + `<b>-uart-recv` on Fe310/Rp2040/Stm32L4 landed main 17164 (146 sub-tests). **`[I2c]`/`[Adc]`/`[Power]` rows + `I2cBus`/`AdcUnit` handles landed main 17174 (SEED 5B2DE4E6; the table's unassigned bits 1/2/13, so COMPILER-17, the imm32 grant hazard above bit 30, stays LATENT and recorded). I2C bus threaded on the five I2C boards and the ADC unit on RP2040 (main 17183; 152 sub-tests). nRF EasyDMA receive landed main 17194 (154 sub-tests; every UART board receives through the handle); `TinkersToolbox.md` HAL section and counts refreshed. **ADC threading landed main 17777 (2026-08-20):** ADC1 drivers on Stm32F4/Stm32L4, SAADC wrappers on both nRF boards (nRF9160 gains SAADC at 0x4000E000); five boards carry `AdcUnit`; boards-test 9 green, 161 sub-tests. Esp32C6 left alone (SAR ADC map unverifiable in-tree); Fe310/Pi4/QemuVirt have no on-chip ADC. **Rulings 15 ruled (a) and BUILT (main 17831, 2026-08-20):** the linear Board threads every open on all nine boards, sleep-deep consumes it, sleep-with-open-handle is CDX2063; boards-test 9 green, 161 sub-tests; seed unmoved (DCE). **Flash follow-on CLOSED (main 17839, Damian-directed):** flash-open-bank threads the Board; sleep with an unsealed bank is CDX2063. **The HAL carries its full designed surface; nothing open in the lane.** `build/boot/diag/**` stays root's | then: HAL follow-ons above, next pool item, or red's call | plugs 1.34 is rulings queue 10 |

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

**The pool. Corrected 2026-08-19 (fester): most of this list was already
drawn and nobody struck it, so a lane reading it drew a second owner onto
another lane's work.** The table above is the assignment and it says so
itself; where the two disagree the table wins. Taken, and NOT available:
`HardwareAbstractionLayer.md` is root's ("pool, per red", with Power on
rulings 15); `GameEngine.md` phase 2 is val's (shadow campaign stage 1 landed
at main 17190); `ShellDslReadability.md` is reek's by Damian's direction and
carries a file claim; `ComplianceEvidence.md` is root's, whose evidence plug
shipped 2026-08-18 with the open items in it; the QEMU bulk-output path
LANDED at main 17198 (blu) and its account is immediately below.

**Actually unowned, in order:** `EdgeMeshGameServers.md` phase 2 -- DRAWN BY
FESTER 2026-08-19, see the table. **After it the pool has nothing a lane can
draw, read 2026-08-19 (fester) rather than assumed:**

- `ThreatModel.md` is not a code campaign and says so itself ("This document
  is analysis, not code"). Its four Open Questions are decisions with
  recommendations attached -- ruling 19 below -- and its ten residual-risk
  rows point into other lanes' designs (HAL, OTA, the trust layer, the board
  chapters) or are DEPLOYMENT and hardware. A lane drawing it today would be
  writing another lane's code or answering Damian's questions for him. It
  becomes drawable when 17 is ruled.
- The `DeviceEmulationCatalog.md` queue is demand-driven, not standing: its
  item 1 landed and item 2 is "whatever the hardware sitting names", which
  makes it red's sittings that produce the next entry rather than a lane that
  picks it up. `tools/codex-vm.c` also carries a file claim, so announce.

Seed-affecting campaigns take the token per CL as usual.

**Strike an item from this list when you draw it.** That is the whole of why
it was wrong: four entries here were live work on four different lanes.

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

- **A disk write moved about 50 KB/s in the bed. FIXED 2026-08-20 (blu), and
  the site was not the one this entry named.** Found by val 2026-08-19 closing
  WORKS-18: writing 2,896,050 bytes (5,657 sectors) took 57 s of a 59 s save,
  with the FAT logic and the chunk size both exonerated (`gfat-load-fat`,
  `gfat-chain-alloc` and `gfat-chain-is-clear` each under a second; the same
  sectors written as one call, as 64-sector chunks and as 8-sector chunks came
  out 56, 59 and 58 s). This entry then concluded "what is left is the
  transfer", about 306 VM exits per sector, and asked for a host-side bulk
  path for the IDE data port.

  **The transfer is about a tenth of it.** A census counting SITES rather than
  a total (L-PARTIAL) put `ide_flush` reopening the image file -- `fopen`,
  `fseek`, `fwrite`, `fclose` -- around every completed 512-byte sector, so
  the save was 5,865 open/close pairs at about 10.7 ms each. That is host file
  I/O and not a VM exit, and it would have survived the repair this entry
  asked for. Measured on a 3 GB headless bed: `write-runs` 66 s and
  `flush-ms` 62,506 before, `write-runs` 5 s and `flush-ms` 27 after holding
  the handle open, with `fflush` per sector keeping what the old `fclose`
  promised. The image the repaired path writes is byte-identical to the one
  the per-sector reopen path writes.

  **What remains is genuinely the exits, and it is about 6 s**: `pio-exits`
  and `words` come out equal at 1,540,608, so the data port really does cost
  one exit per word. The `REP OUTSW` arm is now batched like the NE2K and COM1
  arms, but `str-exits` is 512 of 1,540,608: this guest's ATA driver issues a
  plain `OUT` per word, so the batch is nearly unexercised and the host side
  cannot close the gap alone. **Open, unowned, and guest-side: make the ATA
  driver use `REP OUTSW`/`REP INSW`** for the data phase. The `IN` string path
  in `handle_io` is still one word per exit and wants the same batch when it
  does.

  The instrument is `docs/Probes/fat-write-phases.codex` plus the `IDE CENSUS`
  line codex-vm now prints at exit. **Read that line before quoting a time
  from the probe**: a `Copy-Item` of a Perforce file leaves the copy READ-ONLY,
  every write-back is then refused, the guest still reports the save complete,
  and the run comes back about ten times faster. That silent loss is fixed --
  the refusal is latched, counted and reported -- and it is why the probe's
  run instructions now clear the read-only bit.

- **`trace-alloc` cannot be switched on for any CDX build, so the stack has no
  instrument at all** (found by val 2026-08-19 finishing ModernDesk stage 0;
  compiler lane, unowned). `emit-prologue` maintains a minimum-RSP high-water
  mark at `stack-min-rsp-addr`, guarded by `if st1.trace-alloc`
  (`codex/compiler/Emit/X86_64.codex:12`), and the boot prologue seeds that
  cell with `ram-size` unconditionally
  (`codex/compiler/Emit/X86_64Chapter.codex:385`), so the machinery is all
  there and reads as "never narrowed". **What switches it off is one argument:
  `compile-to-cdx-with-exit-mode` passes the emitter's `trace` as a literal
  `False` (`codex/compiler/opening.codex:1325`) and takes no parameter that
  could carry the mode word**, so `build/compile.ps1 -Trace` reaches the
  compiler, sets the `trace` MODE WORD, and never touches codegen. Tested
  rather than assumed: a `-Trace` desk is byte-for-byte the SAME SIZE as an
  ordinary one, which a per-function prologue check could not be. **The fix is
  to thread `trace` through that call**; the consumer already exists and needs
  no further change, because the desk's Monitor `stack` row (main 17486) reads
  the cell and starts answering the day it lands. This is what WORKS-32 has
  been calling "the stack half has no owner and no instrument" and what
  `ModernDesk.md` withdrew a stage-5 claim over: the instrument is built, it is
  merely disconnected, and nobody has owned reconnecting it.
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
15. Ruled (a), Damian 2026-08-20: the linear Board. BUILT AND LANDED the same day (root, main 17831): `board-open`/`board-close` + the Deep Sleep ops in `Board.codex`, the five foreword opens and all 33 board wrapper opens re-signed, sleep-with-an-open-handle is CDX2063 (`errors/hal-sleep-open-handle`). Record: `HardwareAbstractionLayer.md` "The sleep rule" BUILT block. The flash follow-on CLOSED same day (main 17839, Damian-directed): flash-open-bank threads the Board through OtaBoot and Lwm2mFirmware; every peripheral in the design now rides the sleep rule.

16. **ProductBuilder stage 6 needs a protected-side host.** (fester,
   2026-08-19.) Stages 0 to 4 are done and every one of them runs entirely in
   the Codex bed. Stage 6 is the protected merge, deploy and rollback half,
   and it cannot be built or tested without either access to an outside
   organisation or a stand-in we run ourselves. Nothing before it is unsafe to
   get wrong and everything from it is, so it should not be started on a
   guess about which. Stage 5 (held-out evaluation, mutation and seeded-fault
   campaigns) does not need the answer and can go first either way.

17. **Ruled 2026-08-19 (Damian): do not wait for a program to be bitten; describe the shape seen now.** Between `fixed` and `input` there is a class the two words miss: an allocation that is not the same every call and not proportional to the input either, but BOUNDED, by a constant or by a budget the caller names in an argument. His words: something like *limited* or *budgeted*, covering the case that is not fixed and is not linear. blu designs the class (name, registry word, what the inference does with it, which builtins take it: `substring` by its length argument, `integer-to-text` by the digit ceiling) and ships it; per-argument classes in the registry are the shape he pointed at, not an over-refusal written down. The original question follows for the record.

   Was: **Cost model, `fixed` rung: is a builtin's allocation class allowed to
   depend on its ARGUMENT?** (blu, 2026-08-19, measured by
   `codex/test/cost/builtin-alloc`; account in `CostModel.md` 5.1.)
   `substring` follows its OUTPUT length, not the length of the text it reads
   from: a four-character slice costs **16 bytes whether the source is 64 or
   256 characters**, while a slice that grows with the source costs **40 then
   136**. So the same builtin is `fixed` in the shape that dominates parsing
   here -- a fixed-width field out of a line of any length -- and `input` when
   the slice grows. `bs-alloc` on `BuiltinSpec` is ONE WORD per builtin
   (shipped main 17450), so a scalar class must take the worst case and call
   `substring` `input`, which refuses exactly the case the `fixed` rung was
   introduced to permit. (a) make the class per-ARGUMENT, which is a real
   extension to the registry and to the inference that reads it; (b) accept
   the over-refusal and write it down as the price, consistent with
   abstain-toward-refusal everywhere else in this feature. **Until ruled,
   `substring`'s row stays `unknown`, which reads as allocating**, so nothing
   is accepted on a guess -- but the `fixed` rung cannot ship usefully under
   (b) without saying so out loud.

18. **Ruled 2026-08-19 (Damian), with 17: a bound that is independent of input size is not `input`; it is the bounded/budgeted class 17 introduces.** `integer-to-text` is that class (bounded by the digit ceiling), not `fixed` and not `input`. The original question follows for the record.

   Was: **Cost model: does `fixed` mean "the same bytes every call", or "bounded
   by a constant independent of input size"?** (blu, 2026-08-19, same
   measurement.) `integer-to-text` retains **16 bytes at 2 and 3 digits, and
   16 then 24 at 8 and 10** -- it follows the digit count in 8-byte steps. An
   `Integer` cannot exceed twenty digits, so the call can never allocate more
   than about 32 bytes and cannot contribute to blow-up, which is what the
   rung exists to promise. Read strictly it is `input`; read by the rung's
   purpose it is `fixed`. The two readings disagree for every conversion of a
   fixed-width value, so this is not one builtin. **Its row stays `unknown`
   until ruled.** Worth knowing when deciding: at 64 and 256 alone this reads
   flat and would have been published `fixed`; the wider arm is what caught
   it, and it is the same single-point error the 2026-08-15 Text table made.

19. **`ThreatModel.md`'s four Open Questions, as one decision.** (fester,
   2026-08-19, on drawing from the pool and finding the item is not
   drawable.) The doc is analysis and its questions are the gate on any code:
   (1) NX bits on x86 data and stack pages now, or per-target in the IoT boot
   chapters -- the doc recommends now; (2) hardware crypto dispatch in the
   foreword API with board chapters providing effect handlers, which is its
   recommendation, or board chapters owning the whole surface; (3)
   fault-injection redundancy for the boot selector only, its recommendation,
   or the general verifier too; (4) secure-element support in Identity, which
   it defers to design-partner requirements. Answer them as a block or say
   "recommendations stand" and the item becomes a lane's to draw. Nothing
   waits on it today, so it does not block -- it is here because the pool
   points at the doc and the doc points here.

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
| `apps/works/GopEdit.codex` | **val, 2026-08-19, Damian's assignment via red.** WORKS-14, WORKS-15, WORKS-18 and WORKS-19 all closed; the Editor's standing rules are `works-desk-contract.md` 0.6 |
| `apps/works/GopReview.codex` | **val, 2026-08-19, released to me by red.** WORKS-44's open list: paging and wrapping DONE, leaving reason text on a cast verdict and the supersession view. `GopFacts.codex` stays red's |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- announce |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/test/cost/**` and `docs/Designs/Active/Features/CostModel.md` | blu. 3.3 shipped at main 16020, rule 3 at 16118; what is left of it is COMPILER-7 |
| the integer-literal lexer and text emitter; `codex/plugs/csharp/**` and the `build/` DDC harness; `codex/plugs/recheck/**` | val, lane ownerships rather than open work |
| `codex/plugs/**` and `codex/plugs/plugs-backlog.md` | **reek, 2026-08-18, the close-out lane** (handed on from val, whose 2026-08-16 claim this row carried until 2026-08-19). The register is the lane. Includes `codex/plugs/zig/**` (ordinary fleet code, Damian 2026-08-18); excludes the entries other lanes hold (named in the lanes table) |
| `codex/plugs/spirv/**` (plugs-backlog 1.24) and every `run.ps1` under `codex/plugs/` (1.15) | reek, with the plugs lane (val released it 2026-08-19 with the rest of the register) |
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
