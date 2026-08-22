# The I219 is not an e1000, and that is why thirty sittings taught us nothing

*Opened 2026-08-21 by red, on Damian's instruction to stop guessing and read
the spec. Three lanes, no dependency between them.*

## ANSWERED BY SITTING 8, 2026-08-21: K1 IS ENABLED ON THE BOARD AND THE LAYER IS NEEDED

`pch 770.17 = d104`, so **`giga-k1-dis = n` and `k1-en = y`**. The platform's
NVM leaves K1 ON. The hoped-for cheap outcome -- that the board already had
`Giga_K1_disable` set, making blu's whole layer aimed at the wrong thing --
**is dead**, and it cost one boot to kill rather than a campaign to discover
sideways. All nine readings banked, plus `nicsit`, `nicinit`, `nicring` and
`b3`; `DIAG.TXT` 6,843 bytes against sitting 7's 4,994, which is root's
defer-sink fix paying for itself on its first flight.

**THE NEAR-MISS IS THE MOST VALUABLE LINE IN THE FLIGHT (blu, main 18539).**
The bed powers `770.17` up at `0x4000`. **The board reads `0xd104` -- five
bits apart.** Those extra bits are the platform's firmware configuration,
and they exist in no model we have. `e1000-k1-configure` read-modify-writes,
so they survive. **A constant write -- the obvious implementation, and the
one a bed alone would have fully vindicated -- would have cleared five bits
of firmware state on every boot.**

Nothing in the bed could have caught that, because the bed's power-up value
has no firmware bits in it to lose. This is L-FREEDOM in its purest form:
the divergence lives exactly where the model is silent, and the only
instrument that reaches it is a board reading. It is also the answer to
anyone who asks what a measurement sitting buys that a bed arm does not.

**`e1000-pch-k1-required` flips to `True` on this reading** (red, 2026-08-21):
the timing condition was readings banked, and they are. It goes in a joint
changelist with reek's arm, which must keep a LEADING KILL by moving that
kill to the sabotage rows rather than degrading to pass/pass/kill/pass.

### The `kmrn` verdict: corrected, then closed without the probe (root fixed main 18550, red ruled)

The row said `HOLE ... a kumeran write there is INERT` on a board reading of
`0x34 = 00000000` with the bracket alive. **root accepted the correction
within the hour and fixed it: the verdict is now `ZERO-ON-LIVE-PATH`, and
the bracket is documented as proving the PATH rather than the OFFSET.** His
own prose had said an idle implemented register reads zero exactly like an
unimplemented one; the code had concluded past it. The measured numbers land
unchanged on the corrected branch.

**Separating hole from idle needs a WRITE and readback at `0x34`, which is
not passive. RULED: do not run it (red, 2026-08-21).** Three reasons, and
the second is the one that decides it:

1. It is an active write to an **unidentified** offset on the only board we
   have, mid-campaign, on the device under repair. If `0x34` is live it is
   an indirection register and a write starts a kumeran cycle against the
   PHY we are trying to bring up.
2. **No decision hangs on the answer.** We moved to the cited `770.17`,
   which the board has now confirmed is both the mechanism and necessary.
   The kumeran write is disabled whichever way the probe came out, so the
   result would be knowledge with no action attached to it.
3. Perforce keeps the kumeran path's history, so removing it loses nothing.

**It comes back if `770.17` proves insufficient on a later sitting.** At
that point the question has a reason attached and earns the risk; today it
does not. This is the counterpart to the reading-pair rule rather than an
exception to it: an instrument that cannot separate two cases should be
recorded as not separating them, and the missing separation is only worth
buying when something downstream depends on which case is true.

**Still open after sitting 8:** asde, wedged for the third time, is the only
question the campaign has left outstanding.

## The measurement this starts from

Sitting 6 flew 2026-08-21 and the NIC rows read: registers respond, `link=1`,
PHY brings up in 3.0 s, `rdh-writable=y`, one frame queued with `sent=1
txdd=1`, `received=0`, `wb=0`, `dd=0`, descriptor zero pristine. The host this
box dialled saw **no ARP frame at all**. Nothing moved in either direction
while every register answered correctly.

Five sittings have now asked a version of "is our ring right". None has asked
what part this is.

## What the part actually is

`8086:15b8` is Ethernet Connection (2) I219-V. **On this family the MAC is
inside the PCH and the I219 is essentially the PHY**, unlike the 825xx parts
which are a combined MAC and PHY on a card. `E1000e.codex:271` already says
this about MDIC being the door to the PHY, and stops there.

The Management Engine is always running on a PCH part and drives the same
hardware.

## What the family requires that we do not do

From `drivers/net/ethernet/intel/e1000e/ich8lan.c`:

| requirement | Linux | ours |
|---|---|---|
| ULP forcibly disabled, because its state cannot be known | `e1000_disable_ulp_lpt_lp()` | **THE ROW SPLITS IN TWO AND HALF OF IT IS BUILT, ships OFF** (blu 2026-08-21). The ULP **exit** sequence is unspecified in the documents we hold (reek), and that half is not attempted and must not be guessed. ULP **entry** is fully cited: I219 rev 2.02 section 9.5.7.1 maps ULP Configuration 1, page 779 register 16, and STICKY_ULP bit 4 ("Enter ULP on Link disconnect") plus EN_ULP_LANPHYPC bit 10 ("Enable ULP on LAN disable") are the two conditions that put the part into the state whose exit we cannot specify. `e1000-ulp-disable` clears exactly those, read-modify-write, answer on the device as `e-ulp`. The board already reads both clear (`ulp 779.16=0800`, sittings 8 and 9), so the write is a NO-OP there, and 0x0800 is bit 11 inside the Reserved 13:11 which the mask leaves alone |
| SW/FW/HW semaphore before PHY and select MAC access | `e1000_acquire_swflag_ich8lan()`, `EXTCNF_CTRL.SWFLAG` | **DONE: ships ON, proceed-on-failure** (blu 2026-08-21). **AND IT CARRIES A DEFECT, registered 2026-08-21 (blu, red asked) so it is not re-derived: `e1000-swflag-acquire` writes far more than its own bit.** The WRITE itself is right and the datasheet does not forbid it -- 4.5.2 says a request is registered by writing 1b to your own bit and the grant is denied by that bit reading back 0b, so an acquire that saw MNG held and refused to write could never acquire at all when firmware released. What is wrong is everything around it. The loop is a full-register read-modify-write, `bit-or cur e1000-extcnf-sw-own` with no mask, so it writes the MNG ownership bit 7 back as 1b when firmware holds it -- registering a request on another agent's behalf, against 4.5.2's "at most one bit is 1b at any time" and "the owner writes 0b when done" -- and it writes back the extended configuration area fields, which on I219 are the only thing the datasheet documents this register for. And it does that **2,000 times with no delay**, roughly 4,000 MMIO transactions back to back on the same PCH as the xHCI. **THE FIX SHAPE, so it is not re-argued:** write ONLY the SW-ownership bit, never MNG and never the ext-config fields; poll with a delay and a bounded count far below 2,000. **It does not fly until sitting 12's bank names which line stops the medium, one change per flight** (red, 2026-08-21) -- but it is wrong whichever way that bank reads, so the fix gets built the moment the bank lands, whether or not it names `swflag`. |
| **K1 disabled at 1 Gbps or the MAC STALLS** | `e1000_configure_k1_ich8lan()` | **DONE: cited 770.17, ships ON** since main 18562 (blu 2026-08-21) |
| PHY may be in SMBus mode owned by firmware | `e1e_force_smbus()`, `LANPHYPC` toggle | absent |
| LCD config reloaded from NVM after PHY reset | `e1000_post_phy_reset_ich8lan()` | **WIRED, ships OFF**: `e1000-lcd-reload` is now CALLED, from `na-phy-kick`, with the device id threaded through `na-bring-up`, `na-bring-up-after` and the four stages that reach them (blu 2026-08-21). Before that it had no production caller at all, so the constant was a switch on a wire to nothing (L-UNCALLED). **THE FLIP IS THE FIRST POST-FLIGHT ITEM**: turning it True adds a second unguarded PHY write to the ASDE path, which is the path sitting 9 hung on with the MDIO gate closed, so it waits for sitting 10's sub-step row to say where bring-up stops (red, 2026-08-21) |
| Latency Tolerance Reporting for LPT and newer | `e1000_platform_pm_pch_lpt()` | **UNCITABLE from what we hold, and this is a measurement** (blu 2026-08-21). `Latency Tolerance`, `LTR` word-bounded, `Snoop Latency`, `LTRSND` and `LTROVR` are **zero matches in BOTH datasheets in this tree**, I219 rev 2.02 and 82583V rev 2.6. Nothing to cite, nothing to read, and no register to gate: same standing as the ULP exit sequence and the LANPHYPC toggle. LTR is a PCIe capability configured on the PCH side, and the PCH's CSR map is in no document here. **The count that said otherwise was mine and it was wrong in kind**: an earlier grep answered `LTR 7`, and all seven are substrings -- `uLTRa Low Power` three times, `str_wol_pkt_no_ind_from_fLTR_timeout`, `PCIECLKRQ`. A substring match is not a mention |

**RE-MEASURED 2026-08-21 over the now 929 lines** (blu, and the count moved
that day, so re-run it rather than reading this): `ulp` 0, `LANPHYPC` 0,
`smbus` 0, `FWSM` 0, `LTR` 0; `extcnf` 13, `swflag` 22, `K1` 27. "Four absent"
was true earlier the same day and is not now.

**WHERE THE SIX ROWS STAND AT THE END OF 2026-08-21, and the shape is not a
count.** Two DONE and shipping ON: the semaphore and K1. Two WIRED and shipping
OFF behind sitting 10's sub-step row: the LCD reload and the ULP entry-disable.
One BUILT AS ITS READ HALF with the write declared unbuilt: SMBus. One
UNCITABLE: LTR.

**So what is left of "eight requirements absent" is not eight units of work.**
Three of the original rows turned out to contain a half that cannot be written
from what we hold at all -- the ULP exit sequence, the LANPHYPC toggle, and
LTR entirely -- and naming those as absent-and-therefore-todo overstates the
remaining work by three items that no amount of effort closes without a
document nobody here has. That is L-ADJECTIVE's second half: a number standing
in for a shape. The shape is two shipped, two waiting on one flight, one
waiting on a board reading, and one waiting on a datasheet.

Original measurement, by grep over the 800 lines of `codex/os/kernel/E1000e.codex` for
`ulp`, `EXTCNF`, `swflag`, `semaphore`, `LANPHYPC`, `smbus`, `K1`, `FWSM`:
**zero matches, all eight** -- as of the day this was opened. Two of the rows
have moved since and the table above carries their state; **re-run the grep
rather than quoting this line** (L-COUNT). **Two of the eight now ship ON**
(K1 at main 18562, the semaphore at 18567), each on a sabotage-killed pair
and, for K1, on a board reading that says it is needed. The other four
remain absent.

### The coupling, and my flag for it was understated (blu, main 18567)

Turning K1 on meant the driver was writing the PHY with `SWFLAG` off, which
is the unguarded access the semaphore requirement exists to prevent. I
flagged that from this document. **blu found it was worse than I described:
the K1 step runs AFTER `e1000-init-at` returns, so it sat outside the
bring-up guard entirely** -- taking the semaphore inside init would not have
covered it at all. There are two guarded windows now, the second through
`e1000-k1-configure-guarded`, and a new arm `e1000-swflag-k1` proves the
coupling itself: unguarded configure leaves `770.17` at `16384`, guarded
lands `24576`.

**The lesson is about where a coupling is diagnosed from.** I read the
requirement, saw two features that interact, and named the interaction
correctly -- from the design. What I could not see from here is that one of
the two was not inside the function I assumed it was in. **A coupling
flagged from a document names the risk; only the call graph says whether the
guard reaches it.** The flag was still worth sending, because it is what
sent someone to look at the call site.

**This does not establish the cause and must not be written up as if it did.**
It establishes that we run an 8254x-class driver against a part with a
documented and non-optional extra initialisation layer, and that one entry in
that table has a named failure mode -- the MAC stalling at 1 Gbps -- whose
signature is link up, registers healthy, nothing moving.

## Why the bed could never have told us (L-OPTIONAL, L-FREEDOM)

codex-vm models an e1000. The board has an I219. Every requirement above is
invisible to a model of the wrong part, so **every bed arm is green by
construction and will stay green however wrong the driver is.** This is the
keyboard lesson again: the bed was not less faithful, it was answering a
different question.

## The bed lies about its identity (blu, 2026-08-21, before a line was written)

**codex-vm already presents `8086:15B8`** (`tools/codex-vm.c:14232`) **with
82574 semantics and none of the eight requirements.** So the bed answers the
question "what part is this" correctly and then behaves as a different chip.
Every arm written against the I219 goes green in that bed however the driver
is written, and the arm looks like it checked the part, because it did.

That is worse than modelling the wrong part openly. An honest mismatch gets
caught the first time somebody compares device ids; this one cannot be caught
that way at all.

**RULING (red): a bed must not advertise an identity it does not implement.**
Either implement the I219 semantics or advertise the 82574 it actually is.
reek chooses which; not both.

**RULING (red), and it is what makes the arms worth anything: blu builds the
driver from the SPEC, blind to reek's model, and reek builds the model from
the SPEC, blind to the driver.** Neither reads the other until both are
landed. Two implementations of one document disagree in the places the
document is ambiguous, which is exactly where the hardware bites. One
implementation checked against a bed derived from it is a harness pointed at
the half we already have (L-ORACLE).

## The three lanes, and they do not block each other

- **blu, the driver.** Build the PCH init layer in `E1000e.codex`.
- **root, the instrument.** A `pch-state` stage ahead of `nicsit`, painting to
  the GLASS: FWSM, `EXTCNF_CTRL` and SWFLAG, ULP state, K1 and PHY status, the
  raw PCI command register, DMAR present or not. Plus the `b3` fuel cap and
  the L-BANK fix, which are what make a flight survivable at all.
- **reek, the bed.** Model `8086:15b8` in codex-vm **from the spec, never from
  our driver**, or the model just encodes our assumptions and says yes again.

## Does rule 4 forbid a MEASUREMENT flight? Ruled: no, but it has its own gate (red, 2026-08-21)

root asked rather than assumed, which is the right instinct on a rule that
explicitly binds the commander.

**A measurement payload is EXEMPT from the fly-gate pair.** The pair exists
so that a FIX is never proven by a bed that cannot say no. A payload that
reads registers and paints them asserts nothing the bed could falsify, so
there is nothing for a pair to test.

**It is NOT exempt from proving its own instrument, and its gate is a READING
PAIR: every value `pch-state` paints must be shown reading TWO DIFFERENT
values in the bed.** A stage that always paints `0x0000` is indistinguishable,
on the glass, from a board that genuinely has K1 disabled -- and we would fly,
read a constant, and retire a live hypothesis on it. That is L-FALSIF pointed
at a reader instead of a checker: an instrument that can only ever say one
thing is not evidence, and the failure is worse for a measurement flight than
for a fix, because a fix that does nothing shows up as an unchanged symptom
while a reading that is a constant looks exactly like an answer.

For K1 the two values already exist: reek's `-i219-k1-nvm 0` and `1` produce
`0x0000` and `0x4000` at power-up. Every other register in the stage needs
the same treatment before the stage is trusted, or it is reported as
UNPROVEN on the glass rather than quietly painted beside the proven ones.

## A flight carries what MAIN carries, not what the commander intended (blu, 2026-08-21)

Sitting 7 was declared a MEASUREMENT flight: `pch-state` reads the board and
no driver change goes aboard, so that eight hypotheses become a measured
subset in one boot instead of eight trips.

**That declaration enforces nothing.** The image is composed from main, so
anything landed on main before composition is on the stick whatever the
commander said. blu caught it in his own lane: the PCH layer ships with
`e1000-pch-k1-required=FALSE`, it fires only on `15b8`, and the board IS
`15b8` -- so a `True` on main would have put an untested kumeran write into a
measurement flight and silently made it a fix flight again, with the
measurement confounded by the fix.

**RULE: anything not proven by the fly gate ships DISABLED BY DEFAULT, and
the default flips only when its pair is green.** An intention stated in a
message is not a control; the default in the code is the control. This is the
same shape as L-FALLBACK one level up -- do not let an unproven path be
reachable just because nobody meant to reach it.

Candidate LESSONS row, unpaid so far because blu caught it before it cost
anything: a commander instruction that names what is NOT on a flight is
unenforceable when the flight is composed from main.

## Model what the spec SPECIFIES. Where it is silent, measure instead (reek, 2026-08-21)

Second pair landed at main 18356: the MDIO/NVM semaphore. `-i219-swflag`
refuses MDIO without SW ownership, `-i219-mng-holds` refuses the acquisition
itself with firmware holding MNG.

**reek chose it over ULP for a stated reason, and the reason is the rule:
section 4.5.2 specifies the semaphore mechanism, and ULP's exit sequence is
not specified anywhere we hold.** Modelling ULP would have meant inventing
the sequence, and a bed built from our invention is our assumptions wearing
a device id -- the same defect as a bed derived from our driver, which is
what this campaign already ruled against twice.

**RULE: the bed models only what the spec specifies. Where the spec is
silent, the bed stays silent and the QUESTION MOVES TO THE BOARD.**

The direct consequence for composition: some of these questions have no bed
answer even in principle, so a measurement sitting is the only instrument
that reaches them. **Which rows those are is settled in one place only --
"what the remaining requirements can be held to" below -- and this paragraph
deliberately does not name them.**

**Why it does not (red, 2026-08-21).** This paragraph used to say ULP has no
bed arm and can never get one. That was withdrawn at main 18420, which
promoted ULP and Force SMBus to bed work, and the withdrawal was itself
withdrawn at main 18426 on the distinction that a cited REGISTER is not a
cited OBLIGATION: `ULP_IND` is a status flag and Force SMBus clears on PCIe
reset de-assert, so neither can carry a pair, while the LCD registers after
PHY reset ARE cited at section 5.2 and are the next pair. Two reversals of
one claim inside an hour, both landed on main, and this paragraph restating
it meant every revision had to be made twice and was briefly self-
contradictory in between. A pointer costs one edit. **The argument for a
measurement sitting never depended on WHICH rows were unmodellable, so it
survives every one of these reversals untouched.**

## The rules this campaign runs under, and they are the point

The variables are not serially accessible and we made them serial. Damian has
been to the machine roughly 150 times for five working things. So:

1. **One boot answers everything.** Bus master, IOMMU, ULP, semaphore, K1,
   ring placement and writeback are independent and cost a few registers each.
   Never spend a trip on one of them.
2. **No stage may end the run.** `b3` hung on sitting 6 and took `asde` and
   the summary with it. Fuel caps everywhere; a flight always finishes and
   always reports.
3. **The recorder must outlive its subject.** The bank died at the sink and
   ate the one number that separates arrived-but-invisible from
   nothing-arrived. blu moved `gp=`/`rn=` onto the always-painted row at main
   18307; the general fix is still owed.
4. **Nothing flies until the bed can express at least one of these failure

### What rule 4 means exactly, so it cannot be argued down later (red, 2026-08-21)

"The bed can express a failure mode" is an adjective. Here is the number.

**THE FLY GATE: one falsifiable PAIR, run on the I219 model, before any
image is composed.**

- **The kill arm.** A driver build with ONE required step omitted -- K1 left
  at its NVM setting with link at 1 Gbps is the first choice, because it has
  a named failure mode in the vendor driver -- must come back **RED** in the
  bed.
- **The pass arm.** The same driver with that step present must come back
  **GREEN** on the same bed, same image, same run.

Both halves are required and the kill arm is the one that matters. A bed that
only ever agrees is the thing that spent 150 trips; an arm that has never been
seen to fail is not evidence that anything works (L-FALSIF).

Do this for a second requirement before flying if it is cheap, but one proven
pair is the floor, not the target. **Until that pair exists, composing a
sitting is forbidden**, and that is a standing instruction to the commander
as much as to anyone else, because the commander is who keeps composing them.

## The three the pch-state stage asked the MODEL for (reek, 2026-08-21)

**PCI command register: `-nic-bme-clear`.** The bed read `0x0007` on every
run, so a stage reading the command register had never seen the BME-clear
case. With the flag the NIC refuses the Bus Master Enable bit however often
it is written and does no DMA while it is clear. Measured: default reads
command 7, BME 4, transmit 1; with the flag it reads command **3**, BME
**0**, transmit **0** -- enabled, addressable, answering registers, and
moving nothing, which is the state `Pci.codex`'s own prose already
describes as giving no indication why.

**DMAR: `-dmar`.** A DMAR table is published and linked into both the RSDT
and the XSDT, at `ACPI_BASE + 0xA00` = `0xE0A00`. **HEADER ONLY**:
signature, length, checksum, host address width and flags, with NO
remapping structures behind it. That is enough to tell "an IOMMU is
described" from "it is not" and it is enough for nothing else.

**`EXTCNF_CTRL` already varies, and the reason root read a constant zero is
worth stating: the register is only DECODED under `-i219`.** On the
82540EM model there is no such register and the read falls through to the
zero-initialised register file. Under `-i219` it reads 0, `0x20` once SW
ownership is acquired, and `0x80` under `-i219-mng-holds`. A stage wanting
it to move must select the I219.

**Verification status differs across the three and must not be flattened.**
BME and `EXTCNF_CTRL` are verified by a guest probe on this bed. **DMAR is
verified by CONSTRUCTION only**: there is no ACPI support anywhere in
`codex/os` or `codex/foreword` -- zero matches for `acpi` in either -- so
no guest here can walk the RSDT and the pch-state stage will be its first
reader. It is offered as a table that is written, not as a table something
has read.

**One false pass on the way, and it is the useful part.** The first
`-nic-bme-clear` run reported transmit 0, which is what the feature
predicts. It was wrong: the flag had not parsed at all, so there was no NIC
on the bus, `e1000-find` answered None, and the probe's `tx` was the
device-absent branch rather than the DMA gate. The tell was in the same
output, `pci-command : 65535`, which is a config read of a slot with
nothing in it. **A pass that agrees with the prediction is not evidence
until the mechanism is checked**, and an arm reporting an all-ones config
read has told you it found no device whatever else it says.
## A second pair: the MDIO/NVM semaphore (reek, 2026-08-21)

The gate says one pair is the floor and a second is worth having if it is
cheap. This is the second, and the semaphore was chosen over ULP for a
reason worth recording: **its MECHANISM is fully specified, and ULP's exit
sequence is not.** Modelling a ULP disable would mean inventing the write
sequence from the vendor driver, which is the direction this campaign
forbids, and blu writing the driver blind would have no way to match it.

`-i219-swflag` refuses MDIO unless the caller holds SW ownership.
`-i219-mng-holds` additionally starts with firmware holding the MNG bit.
Enforcement is its OWN flag rather than riding `-i219`, because two
requirements behind one switch cannot be told apart by an arm: a driver
that failed would fail for either reason.

| bed | PHY read before | acquire reads back | PHY read after | after release |
|---|---|---|---|---|
| `-i219-swflag` | **-1, refused** | `0x20`, granted | `0x0154`, a real read | **-1**, door closed again |
| `-i219-mng-holds` | -1 | **0, refused** | -1 | -1 |
| `-i219` alone | `0x0154` | `0x20` | `0x0154` | `0x0154` |

Row 1 is the pair. Row 2 is the control that matters most: the request is
refused because firmware holds MNG, which is 4.5.2 exactly -- "access is
granted when a bit is actually written with 1b and the other bits are 0b"
-- and it is the case a driver that assumes it always wins cannot survive.
Row 3 says the flag being off changes nothing, so the K1 pair and every
existing arm are untouched; re-measured, K1 still reads 0 then 1 and
`e1000-bringup`, `e1000-phy` and `e1000-ring-wrap` still pass.

**The release half was not asked for and is kept because it fired.** After
writing 0b the door closes again, so the model is a semaphore rather than a
one-way latch, and an arm can tell a driver that releases from one that
holds forever.

**Citation status, split, because the two halves are not equally strong.**
The PROTOCOL is cited exactly: 82583V rev 2.6 section 4.5.2. The OFFSET,
`0x00F00` with SW at bit 5, is 82583V 9.2.2.15 and is corroborated by
FAMILY rather than cited for the part, since the I219's MAC is in the PCH.
A model built on it inherits that gap and so does any arm that passes on it.
## The register constants the pch-state stage needs, and what each is worth

root asked for FWSM, `EXTCNF_CTRL` with its SWFLAG bit, and the ULP state
bit, to cite at the constants rather than invent them (2026-08-21). The
three come back with three DIFFERENT statuses and the differences matter
more than the numbers.

**`EXTCNF_CTRL` = MAC CSR `0x00F00`.** 82583V rev 2.6 section 9.2.2.15,
"Extended Configuration Control - EXTCNF_CTRL (0x00F00; RW)". The
semaphore bits are 5 = MDIO/NVM SW Ownership (this is SWFLAG), 6 =
MDIO/NVM HW Ownership, 7 = MDIO MNG Ownership. The protocol is section
4.5.2: a request is registered by WRITING 1b to your own bit, and the
requester is granted access only when that same bit READS BACK 1b, which
it does only if the other two are 0b; at most one is set at any time; the
owner writes 0b when done. SW and HW bits clear on reset, the MNG bit only
on LAN_PWR_GOOD or by firmware. Hardware sets its own bit while loading
the extended configuration area.

**CAVEAT, and it must travel with the number.** That is the 82583V's MAC
CSR map, and the I219's MAC is not a discrete controller: it is in the
PCH, and its CSR map is in no document in this tree. So `0x00F00` is
**corroborated by family, not cited for the part** -- the same standing
caveat `E1000_ServiceModel_Notes.md` opens with. Cite it as family
corroboration or the citation claims more than we have.

**ULP: PHY page 779, register 16, "ULP Configuration 1".** I219 rev 2.02
section 9.5.7.1. Bits: 0 START ("when set the HW will start the auto ULP
configuration, auto cleared once configuration is done"), 1 SW_ACCESS,
**2 ULP_IND, "Power Up from ULP indication", which is the state bit root
is asking for**, 4 STICKY_ULP, 5 INBAND_EXIT, 6 WOL_HOST. Register 17 on
the same page is ULP Configuration 2. This one IS cited for the part.
Note it is a PHY register, so reading it needs MDIO working, and section
9.2 gates that: "access using MDIO should be done only when bit 10 in page
769 register 16 is set", with a 10 ms delay required after an LCD reset
before any MDIO access at all.

**FWSM: I cannot give a cited offset and will not give an uncited one.**
The string appears in NEITHER datasheet in this tree, I219 or 82583V, zero
matches. Every offset I could quote would come from the vendor driver,
which is the one source this campaign forbids for exactly this reason. If
the pch-state stage needs FWSM, that is a gap in our documents rather than
a lookup, and it wants a datasheet we do not have.
**THE K1 PAIR RAN AGAINST THE DRIVER AT MAIN 18433 AND DID NOT PASS, AND
THAT IS NOT A DEFECT IN EITHER SIDE (ruled by red, 2026-08-21).** The bed
implements K1 at PHY page 770 register 17, which is cited below.
`e1000-k1-configure` writes `KMRNCTRLSTA` (`0x0034`, offset 7, bit 1), taken
from the vendor driver's published defines by recall, and the bed models no
such register. **So the pair measures the two of US disagreeing about which
register carries the mechanism, not what the part honours.** `tx` staying 0
after our step is what a bed that does not model the register we wrote would
report whether the driver is right or wrong, and `-i219-k1-nvm 0` moving a
frame confirms only that the bed's own mechanism works. An arm cannot
adjudicate between two implementations when it IS one of them (L-CONTROL,
one level up: the control's own path is the thing in question).

**Neither lane concedes and neither is asked to.** The referee is the
datasheet and nothing else (L-REFEREE). The discriminating question is a
lookup, assigned to reek because he holds the documents and has already
done exactly this eye-check for `LTR` and `LCD`: **is `KMRNCTRLSTA` or the
kumeran indirection cited anywhere in either datasheet, section number or
ABSENT?** blu's second job is provenance: `0x0034`/offset 7/bit 1 is
currently sourced from recall, and this campaign's own standing rule is
that the bed models only what the spec specifies, so a driver step with no
citable source cannot be gated by a spec-built bed by construction.

**If the answer is ABSENT**, K1-by-kumeran joins the LCD reload and LTR in
the set with no possible bed answer, and it becomes a BOARD question rather
than a bed one. That is already instrumented: `pch-state` reads the K1
decode at 770.17, so the next sitting measures what this part actually
carries. **If `Giga_K1_disable` is already set in the platform's NVM, blu's
kumeran path may not be needed on this board at all**, which is the outcome
the measurement sitting was spent to reach and the reason
`e1000-pch-k1-required` stays `False` until a board reading says otherwise.

### ANSWERED: `KMRNCTRLSTA` is ABSENT, and the answer goes against blu without vindicating the bed (reek measured, red ruled, 2026-08-21)

**reek's answer, and it is better than the question asked for.** `KMRN` and
`kumeran` are zero hits in both datasheets in every casing. He then checked
by OFFSET, which the question did not require: in the only MAC CSR map this
tree holds, 82583V 9.2.2.10 puts `FCT` at `0x00030` and 9.2.2.11 puts `VET`
at `0x00038`, **with nothing at `0x00034`**. The two `0x34` hits are PCI
config space `Cap_Ptr` (10.1.2.15), a different address space entirely. A
grep answers "not mentioned"; a map with a HOLE where the register should
sit is a much stronger statement, and it is the reason this ruling can go
anywhere at all.

**What it settles, and by our own standard.** This design already accepts
`EXTCNF_CTRL = 0x00F00` on FAMILY CORROBORATION rather than a citation for
the part, with the caveat above that the I219's MAC is in the PCH and the
PCH's CSR map is in no document here. **Apply that same accepted standard
to `0x00034` and it comes back NEGATIVE**: the family document does not
merely omit the register, it allocates the neighbouring offsets to other
things and leaves that one empty. We do not get to use family corroboration
to accept one offset and then discard it when it answers the other way.
**That is a real strike against the recalled value, measured with the
instrument we had already agreed to trust.**

**What it does NOT settle, and blu is owed this explicitly.** It is not
proof for THIS part. The 82583V is a discrete controller; the I219 is a PHY
whose MAC is in the PCH, and no document in this tree maps that MAC's CSR
space. An absence in the wrong map cannot condemn a register on a part the
map does not describe (L-OPTIONAL is the same shape: an instrument aimed at
a neighbouring thing answering confidently about this one). blu's step is
unproven, not disproven.

**So the board decides, and it is cheap because the instrument is already
there.** `pch-state` reads raw MMIO off BAR0, which sitting 7 confirmed at
`00:1f.6 8086:15b8 B0=df400000 MAP=ok`. **Add a passive read of BAR0 +
`0x34` to `pch-state`** and let the part answer: a plausible
`KMRNCTRLSTA` value says the register is there and blu's recall is sound
for the PCH; all-ones or a constant zero across both a K1-on and a K1-off
reading says it is a hole. **If it is a hole, `e1000-k1-configure` is not
merely untested, it is INERT** -- writing into unmapped MAC CSR space, which
is a materially different claim from "an untested kumeran write" and one
that has to be known before the layer is ever defaulted to `True`.

**THE `i219-k1-mechanism` PAIR (reek, main 18441) IS SOUND AND DOES NOT
ADJUDICATE THIS.** It is well built -- a positive control in the same run,
and calibrated on `-e1000` so every row moves -- and it shows the driver's
kumeran step leaving the cited `770.17` at `16384` with the MAC stalled
while the cited write lifts it. **But the bed it runs on models K1 only at
the PHY register**: `main` `tools/codex-vm.c` carries
`I219_K1_GIGA_DIS 0x2000`, `I219_K1_ENABLE 0x4000` and `i219_k1_reg`
serviced through MDIC, and no `KMRNCTRLSTA`, no `0x00034` MAC CSR path of
any kind. A write to an offset the bed does not implement cannot move
anything in it, whether or not the part would honour it. **So the pair
cannot separate "the part ignores `0x34`" from "the bed does not implement
`0x34`", which is the third instrument in this campaign built from its own
subject.** Verified by reading main's source rather than inferred from the
result.

**What it DOES add, and it is worth having.** With a control and a
calibration it establishes that **the driver's step as written does not
achieve K1 disable through the only mechanism this tree can cite for the
part.** That is a narrowing, not an acquittal and not a conviction: on any
part where `770.17` is the operative mechanism, `e1000-k1-configure` fails,
and `770.17` is the one mechanism we CAN cite.

**The constructive consequence, and it unblocks blu today.** The layer
should write the CITED mechanism -- `770.17` bit 13 -- because reek's pair
demonstrates it works and the datasheet supports it for this part. The
recalled `KMRNCTRLSTA` write stays in place but stays behind the board
reading, neither removed on a bed that cannot see it nor trusted on a
recall the family map contradicts. That ordering costs one sitting and
concedes nothing.

### The `i219-k1-mechanism` arm goes RED on the fix, and it is re-aimed rather than retired or softened (red, 2026-08-21)

blu built the cited `770.17` write, measured it, and **shelved rather than
landed** because it reds that arm: four of seven rows move, the driver's K1
step now reads `24576` (`0x6000`, `ENABLE | GIGA_K1_disable`) and send after
the driver is `1`. His reading is exactly right and worth keeping in these
words: **its PREMISE is retired, not its expected.** The arm was built to
demonstrate a disagreement between the driver and the bed, and the
disagreement is gone because the driver was fixed.

**This is L-INSTRUMENT and the repair is the one that lesson names.** A test
that reads a thing to observe A is broken by that thing correctly learning
to do B; the repair is to re-point it at a question it can still answer, and
NEVER to soften the assertion to match the new output. Re-aimed shape: the
driver's K1 step sets `Giga_K1_disable` and the MAC transmits, with
**sabotage of that step as the kill arm**.

**CORRECTED WITHIN THE HOUR, and the error was mine.** This first said "reek
re-aims, blu lands green", which is a SEQUENCE, and the sequence cannot
exist. The arm asserts the disagreement that the driver change removes, so
there is no order of two changelists in which both are green: whichever
lands first leaves main red until the second arrives, and R-GATE forbids
that. **The two must be ATOMIC -- one changelist carrying the driver change
and the re-aimed arm together.** blu owns it because the driver half is
built and measured; reek shelves the re-aimed arm and blu unshelves it into
`blu 18449`.

**reek caught it by checking a premise instead of accepting one, and he was
right on the tree.** He re-ran the arm on the merged tree and got
byte-identical output, correctly concluding the driver was unchanged: main
18444 carried the SOURCING, prose and this design, while the `770.17` write
has only ever existed shelved at `blu 18449`. blu never claimed otherwise
and reek never softened anything; the two lanes were each accurate about a
different tree, which is the shape a shelf produces and the reason a "not
landed" is worth saying twice. **A ruling that names an order between two
lanes has to state where the code actually IS**, and mine did not.

**Why this pair is not circular, unlike the one before it.** The earlier
pair was the bed adjudicating a driver step the bed did not model. This one
has the bed derived from the datasheet and the driver derived from the
datasheet, independently, meeting at a register both cite. That is the fly
gate working as designed rather than an instrument built from its subject.

**THE FLAG CONDITION, ruled here so nobody has to assume it.** A green
re-aimed pair proves the driver **CONFORMS TO THE CITATION**. It does not
prove the part behaves as cited -- no bed can, and this campaign has said so
from the start. **Conformance to a citation IS the standard for flipping
`e1000-pch-k1-required` to `True`**, because a citation for the part is the
best evidence this tree can hold short of the board, and holding a
conforming driver disabled forever would make the fly gate unmeetable in
principle. The board readings at `770.17` and `BAR0 + 0x34` still ride
sitting 8, and they can overturn this; they are not a precondition for it.

### LANDED main 18468, and the PLACEMENT is the finding worth keeping (blu, 2026-08-21)

The atomic CL went in as ruled: the cited `770.17` write and reek's re-aimed
arm in one changelist, `required=False`, gate green. reek's shelf at `reek
18455` is redundant from that moment and is being deleted, its own note
having correctly said "DO NOT SUBMIT THIS ALONE".

**blu moved the K1 step to AFTER bring-up, because a PHY reset restores the
NVM value and wiped the old pre-init write.** That is the part to carry
forward, and it is corroborated in the bed rather than taken on report:
`tools/codex-vm.c` sets `i219_k1_reg` from `i219_k1_nvm` on reset, so a
write placed before the reset is genuinely gone.

**This is requirement 5 of the eight arriving by the back door.** The table
at the top of this file lists "LCD config reloaded from NVM after PHY reset"
as absent from our driver, and reek cites it at section 5.2 as the next
pair. The placement bug and that row are **the same underlying behaviour**:
a PHY reset returns configuration to its NVM state, so anything written
before one must be rewritten after it. We did not implement the requirement,
we collided with its mechanism while placing an unrelated write.

The consequence generalises past K1 and should be checked before the
remaining requirements are written: **every one of the eight that writes PHY
**SHARPENED BY THE SECOND REQUIREMENT, main 18505 (blu). There are TWO
reset boundaries, not one, and each requirement has a different one.** The
SWFLAG semaphore is the mirror of K1: **SW ownership is cleared by the MAC
reset**, so the semaphore is taken AFTER `e1000-reset` and held across the
PHY work, where K1 must be written AFTER the PHY reset. Put either one in
the other's slot and it is silently undone. So the rule below is not "state
where it sits relative to PHY reset" but **"state which reset clears it,
and sit after that one"** -- and the two answers so far are different.

That is now twice that placement, not content, was the whole of the work,
and both times the code was correct and inert until it moved. Predicted here
after K1 and confirmed on the next requirement without prompting.

**CAVEAT blu raised himself and it belongs beside the pair: it is NOT cited
that the part refuses MDIO while unowned.** Section 4.5.2 specifies the
semaphore MECHANISM; the bed additionally PUNISHES an unowned MDIO access,
which is reek's modelling choice rather than a documented behaviour. So the
kill arm proves our driver conforms to a bed stricter than the citation, not
that the board would refuse us. That direction is the safe one -- a driver
that passes is safe on a laxer part -- but it must not be read as evidence
about what the hardware does, and the flag stays off on the same terms as K1.

or LCD configuration has to state where it sits relative to PHY reset**, or
it will be silently undone the same way. That is a cheaper thing to know now
than after a sitting spends a boot on it.

**THE TIMING, which the standard above does not settle and which nearly cost
the arm (blu caught it, red ruled, 2026-08-21).** Conformance is the
standard for `True`; it is not a licence to flip on the spot. blu found that
flipping in the landing CL collides with the re-aimed arm: reek wrote it
against `required=False`, so its first two rows read `16384`/send `0`, and
under `True` they read `24576`/send `1`, **the arm loses its LEADING KILL**
and the stated four-state alternation degrades to pass/pass/kill/pass. It
would still discriminate on the reset rows, so this is a prose-versus-rows
mismatch rather than a broken test -- which is precisely the kind of thing
that gets "fixed" by editing the prose to match and quietly costs a kill
arm.

**Ruled: `e1000-pch-k1-required` stays `False` through sitting 8, and
nobody edits reek's file.** The arm is correct as written, the alternation
stands, and the flip becomes its own changelist after sitting 8's readings
are banked. The reason is not the arm, which is only how the cost showed
up: **flipping `True` puts a write that has never flown into a measurement
sitting that has already been lost twice**, which is blu's own original
reasoning for shipping `False` and it has not stopped being right. The
stage order helps but is not the argument -- `pch` is stage 10 and `nicinit`
is 12, so `pch-state`'s own read of `770.17` sees the platform's NVM value
either way.

Until that reading exists: `KMRNCTRLSTA` joins the LCD reload and LTR in
the set with no possible bed answer, `e1000-pch-k1-required` stays `False`,
and the PHY-side K1 at 770.17 remains the only K1 mechanism this tree can
cite for the part.

**The bed's own K1 pair, below, stands unchanged.** It was never a claim
about the driver:

**THE FLY GATE IS MET FOR K1 (reek, 2026-08-21).** The model is
`-i219` in `tools/codex-vm.c`, and the pair runs on one image in one run:

| bed | 770.17 at power-up | kill arm tx | pass arm tx |
|---|---|---|---|
| `-i219` (NVM leaves K1 on) | `0x4000` | **0, did not complete** | 1 |
| `-i219-k1-nvm 0` | `0x0000` | 1 | 1 |
| default 82540EM, no `-i219` | absent, MDIC answers error | 1 | 1 |

Row 1 is the gate: the same transmit that fails with K1 left at its NVM
setting succeeds once `Giga_K1_disable` is written, `0x4000` to `0x6000`,
with nothing else changed. Rows 2 and 3 are the controls, and they are what
say the stall is caused by the K1 bits rather than by the I219 path or by
the arm: with K1 off at power-up the kill arm passes, and on the family
model the paged register does not exist at all.

**What is cited and what is modelled, because this is the file where that
matters.** CITED, I219 rev 2.02 section 9.5.5.2: page 770 register 17 is
PCIe Power Management Control, bit 13 `Giga_K1_disable` ("When set, the
I219 does not enter K1 while link speed at 1000 Mb/s"), bit 14 `K1 enable`,
both RW and both defaulting to 0b; and section 9.3, register 31 at PHY
address 01 is the page register and belongs to no page. MODELLED and NOT
cited: that K1 left enabled at 1 Gbps STALLS the MAC. The datasheet
documents the control, not the consequence; the consequence is the vendor
driver's behaviour and it is the proposition this campaign exists to test.
The bed makes it expressible and asserts nothing about silicon.

The power-up value of 770.17 is the platform's NVM setting, which we do not
have for this board, so it is a flag rather than an invention:
`-i219-k1-nvm 1` is the default and is the condition the campaign is about.

**The first version of the arm was inverted and the inversion is worth
keeping.** It measured RECEIVE, and the injected frame reached the ring
BEFORE the stall condition existed, so it reported frame-availability
rather than MAC progress: kill arm yes, pass arm no, exactly backwards.
Transmit is initiated by the driver, in order, and cannot be satisfied in
advance. An arm whose two halves come out the wrong way round is a defect
in the arm, not a finding.

**OUR DRIVER'S K1 STEP DOES NOT MOVE THE MODEL'S K1 STATE (blu, 2026-08-21).**
The fly gate above proves the BED can tell a configured part from an
unconfigured one, because the bed's own pass arm writes `Giga_K1_disable`
through page 770 register 17. It says nothing about whether this driver's K1
step reaches that state, and measured against the same model it does not:

| arm | tx as shipped | after `e1000-k1-configure` |
|---|---|---|
| `-i219` (K1 on at power-up) | no | **no** |
| `-i219 -i219-k1-nvm 0` (control) | yes | yes |

`codex/test/e1000-pch-k1` and `codex/test/e1000-pch-k1-off`. The control is
what makes the zeros attributable: same binary, same I219 path, only the K1
bits differ.

**The two implementations address K1 through different registers.**
`e1000-k1-configure` writes KMRNCTRLSTA, MMIO `0x0034`, offset `0x0007`,
bit `0x0002`, which is the vendor driver's `e1000_configure_k1_ich8lan` path
recorded from recall. The bed implements K1 only at PHY page 770 register 17
bits 13 and 14, cited to I219 rev 2.02 section 9.5.5.2, and models no
KMRNCTRLSTA at any offset. Our write therefore lands on a register the model
does not implement, `i219_k1_reg` keeps its NVM value, and the stall persists.

**This is not a verdict on either side, and the repair is not for either side
to concede.** Both controls are real in the vendor family. What separates them
here is that one has a datasheet citation and ours has recall, and pointing our
driver at page 770 because that is what the bed watches would build the driver
from the bed (L-ORACLE). It wants the datasheet.

**`e1000-pch-k1-required` stays False** until that is settled. Flipping it now
would ship into a flight image a write the bed cannot vindicate, which is the
same trap recorded above.

**The step's own answer cannot report this failure.** `e1000-k1-configure`
returns the K1 bit it left behind, read back through the same unimplemented
register, which answers `0`: indistinguishable from a bit successfully
cleared, in every arm including the ones where K1 is demonstrably still on.
Both arms assert transmit for that reason and print the answer only as a row.

**`0x0034` OFFSET 7 BIT 1 CANNOT BE SOURCED FROM ANYTHING IN THIS TREE (blu,
2026-08-21, on red's ruling that from recall is not a citation).**

| pattern | I219 | 82583V |
|---|---|---|
| `KMRN` | 0 | 0 |
| `Kumeran` | 0 | 0 |
| `0x00034` | 0 | 0 |
| `EXTCNF` | 4 | 7 |
| `MDIC` | 6 | 7 |
| `K1` | 28 | 4 |

The bottom three rows are the positive control, and they are why the top three
are a finding rather than a failed search: the same sweep over the same two
files reads both documents. The 82583V general register map is the second
witness, and it is the stronger one: it runs `0x00030` FCT directly to
`0x00038` VET, so the family map we hold has no register at that offset at
all.

**What IS cited, and it is reek's register.** I219 rev 2.02 section 9.5.5.2,
PHY address 01, page 770, register 17: bit 13 `Giga_K1_disable`, "When set,
the I219 does not enter K1 while link speed at 1000 Mb/s"; bit 14 `K1 enable`;
both RW, both defaulting to 0b. Bit 14 carries a footnote nothing in our
driver acts on: **in SMBus mode it is cleared, and the register must be
reconfigured after switching back to PCIe**, which ties this row to the SMBus
row of the eight rather than leaving it independent.

**This does not make the bed the referee, and the distinction is the whole of
why the standoff dissolves.** The campaign forbids building the driver from
the MODEL, because a model derived from our driver agrees with it by
construction. It does not forbid building the driver from the DATASHEET. If
the driver moves to 770.17 it moves because that is the cited control for this
part, and the resulting agreement with reek's bed is two implementations
independently following one citation, which is exactly what the blind build
was for. The alternative reading, that we hold KMRNCTRLSTA because conceding
would corrupt the experiment, keeps an unsourced write in the driver to
protect a method the method does not require.

**RULED (red, 18445): the driver moves to 770.17 bits 13 and 14, and the
kumeran write is kept but disabled.** The negative above is trusted for its
CONTROLS rather than its zero hits, and the 82583V map cannot convict a
register living on the PCH's MAC, whose CSR map is in no document we hold. A
board read decides whether KMRNCTRLSTA is real on this part.

**SUPERSEDED 2026-08-21: the board read came back and the kumeran path is
DELETED.** `0x34` read zero on the live path at sitting 8, 770.17 is the
mechanism, and red ruled against a write-and-read-back probe to identify the
offset: an active write to an unidentified register with no decision hanging
on the answer. The code is gone from `E1000e.codex` rather than left
unreachable, because Perforce keeps the history and an uncalled path is a path
nothing tests (L-UNCALLED). What was deleted: `e1000-k1-configure-kumeran`,
`e1000-kmrn-read`, `e1000-kmrn-write`, the five `KMRNCTRLSTA` constants and
the three CTRL speed-forcing constants that only it used, 84 lines. Nothing
else in the tree referenced any of them, checked before the cut.

**Built, measured, and NOT LANDED, and the reason is another lane's
instrument.** `e1000-k1-configure` now writes 770.17, answers the register
read back, and runs AFTER bring-up rather than before it: a PHY reset returns
770.17 to its NVM value, so the step ahead of `e1000-init-at` was wiped by the
BMCR reset inside `e1000-phy-bring-up` and the part came up in exactly the
state the step existed to prevent. `e1000-pch-k1` was rebuilt to produce its
own omission by re-running `e1000-link-up`, so the pair carries a kill half
that does not borrow `e1000-pch-k1-required` to express it: `tx` no at NVM
`16384`, no again after the reset, yes at `24576` after the step.

What held it was `codex/test/i219-k1-mechanism`. That arm exists to assert the
DISAGREEMENT this change removes, and its prose says so in its first line;
with the driver on the cited register its `driver k1 step` answers `24576` and
`send after driver` answers `1`, so four of its seven rows move and the arm
becomes a second copy of the pass arm. A `.expected` refresh would paper over
a retired premise rather than record it, so reek re-aims or retires it and
this lands behind it (red, sequencing agreed 2026-08-21). **The gate cannot
see any of this** -- the battery is not run at the gate, so the collision was
visible only to whoever ran the arm.

**`e1000-pch-k1-required` IS TRUE since 2026-08-21 (red's GO, condition met).**
It stayed False through sitting 8 for a flight reason rather than an evidence
one, and what flipped it is the board reading rather than another bed green:
`770.17 = 0xd104` says K1 is enabled with the disable bit clear on the actual
part, so the layer is NEEDED and not merely conforming. Red's condition on the
joint CL was that the pair keep a LEADING KILL, produced by the arms
themselves rather than by this constant; both arms do that by re-running the
PHY reset, so the ladder stays kill, pass, kill, pass.

Two things fall out of holding it, and both are why this is not merely
cautious. `codex/test/i219-k1-mechanism` alternates kill, pass, kill, pass,
and its LEADING KILL is this constant being `False`: flip it and the ladder
opens with two passes and the arm loses the state that lets it say no. And the
green itself is narrower than the requirement -- **the pair proves the driver
CONFORMS TO THE CITATION, not that the part behaves as cited**. Those separate
at the board and nowhere else, which is why the reads at 770.17 and BAR0+0x34
both ride sitting 8: one asks whether the part honours the cited control, the
other whether KMRNCTRLSTA exists on it at all.

## What sitting 8 did to the driver lane (blu, 2026-08-21)

Red's rows are in `HardwareSitting.md` and are not repeated here. Two of them
change what this lane does next.

**`770.17 = 0xd104` on the board: `k1-en=y`, `giga-k1-dis=n`.** K1 is ENABLED
at gigabit and the disable bit is CLEAR, which is exactly the condition the K1
requirement describes, so the layer is needed and the aimed-elsewhere branch is
dead. The register was reachable and answered a plausible live value, which
also says the cited control exists on this part rather than only in the
document.

**THE BED'S POWER-UP VALUE IS NOT THE BOARD'S, and the difference is five
bits.** `-i219-k1-nvm 1` powers up at `0x4000`; the board reads `0xd104`. Bits
7, 8 and 9 differ from the datasheet's own reset defaults too, so what is in
that register on the real part is a firmware and NVM configuration, not a
power-on constant. `e1000-k1-configure` is correct against both ONLY because
it READ-MODIFY-WRITES: it ORs bit 13 and leaves the rest, so the board becomes
`0xf104` and the five firmware bits survive. **A step that wrote a constant --
which is what an arm asserting an exact register value quietly encourages --
would have cleared them.** That is L-BEDTRUE one level over: not a default
that is only true in the bed, but a bed VALUE that is only true in the bed,
with the same property that no number of additional bed arms could have caught
it.

**`gprc-before=3`, `stats gprc=0`.** Nothing reached the MAC during
`nicring`'s own window, which is the successor question NIC-4 left open and it
is now answered. **It does not by itself prove the stall**: a stalled MAC and a
silent wire produce the same zero, and this arm cannot separate them. What it
does rule out is the reading where frames arrive and the ring loses them,
because a frame that reaches the MAC increments GPRC before any descriptor is
involved.

## The semaphore ships ON, and the reason is the K1 flip (red, 2026-08-21)

**The two requirements stopped being independent the moment the first one
shipped.** `e1000-pch-k1-required = True` means the driver WRITES a PHY
register during bring-up, and until the semaphore followed it that write went
out with SW ownership clear: the exact unguarded MDIO access 4.5.2 exists to
arbitrate. Neither flip is wrong alone; the pair off and the pair on are both
coherent, and only the intermediate state is not.

**PROCEED ON FAILURE, and the board decided it.** Sitting 8 read `mdio-gate
open=n` with the reads working, so this part does not refuse an unowned
caller. The enforcement half that the vendor driver's abort exists for is
absent here, and the datasheet agrees the mechanism "does not block software
accesses". What remains is the ME race, which a bounded acquire narrows and an
abort does not narrow at all: it would convert a race into a refusal to bring
the NIC up. So the acquire is fuel-bounded, its answer is reported, and the
work happens either way.

**Two windows, not one long hold.** The semaphore taken in
`e1000-init-after-reset` is released when bring-up finishes, and
`e1000-pch-prepare` runs AFTER `e1000-init-at` returns, so the K1 step is
outside that window by construction and takes its own through
`e1000-k1-configure-guarded`. Holding across the whole of bring-up would
starve the ME for the length of a link wait.

**`codex/test/e1000-swflag-k1` is the arm for the coupling, and neither
existing arm could see it.** `e1000-pch-k1` runs without MDIO enforcement and
`e1000-swflag` never writes 770.17, so each passes with the other requirement
broken. Under `-i219-swflag`: init leaves `24576`, a wipe returns `16384`, an
UNGUARDED `e1000-k1-configure` leaves it at `16384` because MDIO refused it,
and the guarded call lands `24576`. Same function, same register, same boot;
the only difference is who holds bit 5.

**Its first version measured nothing and said so loudly**, which is the part
worth keeping. Every row answered `-1`, because the arm's own reader reached
770.17 through MDIO without holding the flag: an instrument that reaches its
subject through the mechanism under test measures the mechanism. The reader
takes the semaphore now, so the only unowned access in the boot is the one row
1 is about.

## Requirement 2, the MDIO/NVM semaphore: how it was built (blu, 2026-08-21)

`e1000-swflag-acquire`, `e1000-swflag-held`, `e1000-swflag-release`, gated by
`e1000-pch-swflag-required` which is `False`. Arms `codex/test/e1000-swflag`
and `e1000-swflag-mng` against reek's pair from main 18356. Every existing
e1000 arm still passes, 23 of 23.

| arm | ladder |
|---|---|
| `-i219-swflag` | mdio **refused** unowned, acquire 1, flag held, mdio **answered**, released, mdio **refused** again |
| `-i219-mng-holds` | mng holds, acquire **0**, flag not held |

The second arm is what makes the first worth anything: an acquire that cannot
fail is not evidence that the protocol was followed, and a function
unconditionally answering 1 passes the first ladder and fails this one.

**WHERE IT SITS, and it is the OPPOSITE END FROM K1.** Red asked for the
placement explicitly after the K1 finding, and the two are mirror images. SW
ownership is cleared by the MAC reset, so the semaphore is taken AFTER
`e1000-reset` and held across the PHY work. K1 lives in a PHY register that
the PHY soft reset restores to its NVM value, so it must be written AFTER
`e1000-phy-bring-up`. **Two resets, two registers, two placements, and each
step in the other's position is silently undone by a reset that is not looking
at it.** Neither failure announces itself: the register reads back correct at
the moment it is written and is wiped afterwards by something else.

**Cited in two halves with different strengths, and the weaker half is the one
the campaign is about.** CITED, 82583V 4.5.2 and 9.2.2.15: `EXTCNF_CTRL` at
`0x00F00`, SW ownership bit 5, HW bit 6, MNG bit 7; a request is a 1b write to
your own bit and the grant is that bit READING BACK 1b; at most one is set;
the owner writes 0b when done; SW and HW clear on reset while MNG clears only
on LAN_PWR_GOOD or by firmware. NOT CITED: that the part REFUSES MDIO without
ownership. The same section says the mechanism "does not block software
accesses to MDIO or the NVM, therefore programmers can enable software to use
or ignore this process at will", and the bit table calls the register optional
for the 82583V. The enforcement is the vendor driver's behaviour and the
proposition under test, exactly as with K1's stall.

**What a failed acquisition should DO is deliberately not decided.** The
datasheet permits proceeding; the vendor driver aborts. The two differ on
whether the board enforces it, which is a board question, so the layer answers
whether it holds the flag and leaves the policy to the caller. Inventing an
answer here would be a guess wearing the shape of a decision.

**This one AGREED with reek's model on the first run**, which is the blind
build's other outcome and worth recording next to K1's disagreement: both
sides read the same citation for the offset, the bits and the read-back grant,
so there was nothing to reconcile. The method is not only a disagreement
detector; when the document is unambiguous it produces two implementations
that match, and the agreement means something because neither side saw the
other.

**What is still absent, and no arm may read this model's silence as
agreement:** ULP, SMBus and LANPHYPC, the LCD reload after PHY reset, and LTR.
Four of the eight rows.
reek landed the identity half at main 18324: the family model now advertises
`8086:100E` (82540EM), which is what it actually decodes, with `15B8`
reserved for the real I219 model. Proved with a live control, 4110 on the new
binary against 5560 on the previous, and no arm changed meaning because
nothing read the id.
   modes.** A green arm from a model of the wrong part is not evidence, and
   flying on one is what spent the 150 trips.

## The remaining requirements, measured against the documents we hold (reek, 2026-08-21)

red asked for blu's remaining requirements on the SPEC side as the sitting-8
critical path. The answer took three readings and the first two were wrong in
OPPOSITE directions, which is worth more than the table it produced.

**THE DISTINCTION THAT DECIDES IT: A CITED REGISTER IS NOT A CITED
OBLIGATION, AND ONLY AN OBLIGATION CAN CARRY A FLY-GATE PAIR.** A pair needs
a DRIVER ACTION that the document requires, so that omitting it is a defect
rather than a preference. A register the driver may write is not that. K1
qualifies because 9.5.5.2 gives the control and the vendor names a
consequence the driver must act on; the semaphore qualifies because 4.5.2
requires the acquisition before MDIO. The rest have to be measured against
that bar, not against whether an offset can be quoted.

| requirement | control cited | obligation cited | can carry a pair |
|---|---|---|---|
| K1 at 1 Gbps | 9.5.5.2 | vendor consequence | **YES, landed** |
| MDIO/NVM semaphore | 4.5.2, family offset | yes | **YES, landed** |
| LCD registers after PHY reset | 5.2 | **yes** | **YES, and it is next** |
| ULP | 9.5.7.1 | **no** | no |
| Force SMBus | 9.5.3.4 | **no** | no |
| LTR | absent | absent | **never** |

**LCD REGISTERS AFTER A PHY SOFT RESET IS CITED, and I reported it absent an
hour earlier because I searched in the VENDOR DRIVER'S VOCABULARY.** Section
5.2, "Reset Operation", under PHY Soft Reset: "A PHY reset caused by writing
to bit 15 in MDIO register 0. Setting the bit resets the PHY, but does not
reset non-PHY parts. The PHY registers are reset, but other I219 registers
are not." Then the note that is the obligation: **"The integrated LAN
controller configures the LCD registers. Other I219 GbE LCD registers do not
need to be configured."** `sw_lcd`, `SW LCD` and `LCD config` are zero in
both datasheets, which is what I measured and reported; the requirement is
stated in the document's own words instead. A census keyed to the other
implementation's spelling reports ABSENT for a requirement that is present,
and the absence is manufactured by the filter.

**This one is expressible with what the model already has, and half of it is
already built.** `e1000_phy_reset_regs` restores 770.17 to its NVM value on a
PHY soft reset, with the reason recorded at the call site: a model that kept
the driver's value across a reset would let a driver that configures K1
exactly once pass. That is the LCD-reload obligation in miniature for one
register, and **there is no arm exercising it** -- the existing K1 pair never
issues a second reset. The pair to build is: configure K1, issue a PHY soft
reset, transmit without reconfiguring, which must come back RED, against the
same run reconfiguring after the reset, which must come back GREEN.

**ULP: the registers are cited and it still cannot carry a pair.** This file
said ULP "has no bed arm and can never get one from the documents we hold",
which was wrong about the REGISTERS: ULP Configuration 1 is 9.5.7.1, PHY page
779 register 16, and registers 17 to 20 on that page are ULP Configuration 2,
ULP SW Control, SW Control and OBLCD Control. But the conclusion was right
and the correction I sent red overreached. **`ULP_IND` at bit 2 is "Power Up
FROM ULP indication", a status flag saying the part HAS come up from ULP, not
a state meaning it is IN ULP**, and `RESET_ULP_IND` at bit 15 clears that
flag. A driver that never clears an indication is not thereby broken, so the
pair has no kill arm. Section 6.4 puts entry and exit on the WIRE, not in the
driver: ULP is entered on link disconnect with `STICKY_ULP`, and "once energy
is detected the I219 will exit ULP mode". `FORCE_ULP` at bit 14 forces the
PHY to energy power down. The driver-side exit the vendor performs is the
part that is specified nowhere, which is what the original ruling said.

**Force SMBus is cited and fails the same bar for a sharper reason.** SMBus
Control Register, 9.5.3.4, PHY page 769 register 23, bit 0, RW, default 0b:
"Force SMBus, **reset on PCI reset de-assertion**." Section 5.2 agrees from
the other side: de-asserting PCIe reset "causes a switch from SMBus to PCIe".
So a host driver running over PCIe finds the bit CLEAR by specification, and
a bed that started with it set to give the driver something to clear would be
contradicting the datasheet to manufacture an arm.

**LTR is absent, and this one survived being checked by concept as well as by
name.** Zero matches in both datasheets for `Latency Toleranc`, `Tolerance
Reporting`, `\bLTR\b` and `LTRV`; the seven apparent hits in the I219 document
are `PLTRST#` and a WoL field name. By concept: `L1.2`, `OBFF`, `snoop`,
`service interval` and `ASPM` are all zero, `latency` occurs exactly once and
is about the K1 exit sequence, and all 43 `tolerance` hits are crystal
frequency and temperature. There is no vocabulary left for it to be hiding
under.

**What red needs from this.** The next bed pair is the post-reset
reconfiguration arm, not ULP. ULP and Force SMBus are closed as bed work for
a stated reason rather than deferred, so nobody re-derives them. LTR needs a
document this tree does not have. And the ULP row's standing argument in this
file, that some questions have no possible bed answer and only the board can
speak to them, is TRUE and belongs to LTR and to ULP's driver-side exit.

## Requirement 3, the LCD reload after a PHY soft reset (blu, 2026-08-21)

Built on red's terms and SHIPPING OFF: `e1000-pch-lcd-reload-required` is
False, `e1000-lcd-reload` is the named omission point, and the pair is
`codex/test/e1000-lcd-reload` under `-i219`.

**WHICH RESET CLEARS IT, which is what red asked for and is the two-boundary
rule applied a third time.** The LCD registers are PHY registers, so the PHY
SOFT RESET clears them and the MAC reset does not. That places the reload at
the same boundary as K1 and the opposite one from the semaphore, and it is not
a coincidence: 770.17 IS one of the registers section 5.2 is talking about, so
this requirement is the general obligation and K1's placement is one instance
of it.

| row | what runs | 770.17 | send |
|---|---|---|---|
| left by init | `e1000-init` | 24576 | |
| 1 kill | `na-bring-up-after` | 16384 | 0 |
| 2 pass | `e1000-pch-prepare` | 24576 | 1 |
| 3 kill | `na-bring-up-after` | 16384 | 0 |
| 4 pass | `e1000-pch-prepare` | 24576 | 1 |

Both sides are the arm's own calls, so the ladder does not move when the
constant flips; only `shipped reload step` and `gate answers` do. The kill is
`na-bring-up-after` rather than `na-bring-up` because the latter resets the MAC
as well and a send failing after it could be the rings, which is the confounder
`e1000-pch-k1` avoids the same way.

**THE GATE'S OTHER SIDE WAS RUN, not reasoned about.** With the constant
flipped True in a local build the arm reads `shipped reload step: yes` and
`gate answers: 3`, so the enabled path configures and the bit sticks. A gate
that ships off is a path nothing calls (L-UNCALLED) unless somebody runs the
other arm once, and the shipped `.expected` records the False side.

**`e1000-init` PAYS THE OBLIGATION BY CONSTRUCTION AND NOTHING SAID SO.**
`e1000-pch-prepare` runs after `e1000-init-at` returns, therefore after the
BMCR reset inside `e1000-phy-bring-up`; the `left by init` row is the evidence,
and reordering those two lines now moves a row instead of silently unmeeting
the requirement.

**THE ASDE PATH DOES NOT PAY IT, and the flip alone will not fix that.** Rows 1
and 3 are `na-phy-kick` resetting the PHY with nothing behind it, so with
`e1000-pch-k1-required` shipping True since 18562 an ASDE arm hands whatever
runs next a part stalled at gigabit. Before that flip both steps were off
together and the path was merely incomplete; the flip is what made it wrong,
which is the same intermediate-state argument that coupled the semaphore to K1.
**`e1000-lcd-reload` needs a `dev-id` and the flight path does not carry one**:
`dasd-run-part` and the `AsdeStageProbe` and `MscAlignProbe` sites take an MMIO
base, so the constant's flip and a one-parameter thread through those three
diag chapters are ONE change, and those are red's chapters. The note is at
`na-phy-kick` in the same shape as `e1000-reset`'s "it does not quiesce, and
the caller must", because the obligation is invisible at the call site.

**It is not `i219-k1-mechanism` again.** That arm resets through
`e1000-phy-bring-up` and reconfigures with a bare `e1000-k1-configure`, both
its own calls, and measures the register against transmit. This one puts a
DRIVER function on the kill side, so the row moves the day that function starts
reloading and moves back the day it stops.
