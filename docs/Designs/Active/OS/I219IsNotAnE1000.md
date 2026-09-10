# The I219 is not an e1000, and that is why thirty sittings taught us nothing

## What the part is

`8086:15b8` is Ethernet Connection (2) I219-V. **On this family the MAC is
inside the PCH and the I219 is essentially the PHY**, unlike the 825xx parts
which are a combined MAC and PHY on a card. `E1000e.codex:271` already says
this about MDIC being the door to the PHY, and stops there. The Management
Engine is always running on a PCH part and drives the same hardware.

The consequence for every document in this tree: **the I219's MAC CSR map is
in no document we hold.** A MAC-side offset can be corroborated by FAMILY
from the 82583V, never cited for the part. That is the standing caveat
`E1000_ServiceModel_Notes.md` opens with.

The driver is `codex/os/kernel/E1000e.codex`. Sitting rows are in
`docs/Hardware/HardwareSitting.md` and are not repeated here.

## The measurement this starts from

Sitting 6 NIC rows: registers respond, `link=1`, PHY brings up in 3.0 s,
`rdh-writable=y`, one frame queued with `sent=1 txdd=1`, `received=0`,
`wb=0`, `dd=0`, descriptor zero pristine. The host this box dialled saw **no
ARP frame at all**. Nothing moved in either direction while every register
answered correctly.

Five sittings asked a version of "is our ring right". None asked what part
this is.

**This does not establish the cause and must not be written up as if it did.**
It establishes that we run an 8254x-class driver against a part with a
documented and non-optional extra initialisation layer, and that one entry in
the table below has a named failure mode, the MAC stalling at 1 Gbps, whose
signature is link up, registers healthy, nothing moving.

## The rules this campaign runs under, and they are the point

The variables are not serially accessible and we made them serial. Damian has
been to the machine roughly 150 times for five working things. So:

1. **One boot answers everything.** Bus master, IOMMU, ULP, semaphore, K1,
   ring placement and writeback are independent and cost a few registers
   each. Never spend a trip on one of them.
2. **No stage may end the run.** Fuel caps everywhere; a flight always
   finishes and always reports.
3. **The recorder must outlive its subject.** A bank that dies at the sink
   eats the one number that separates arrived-but-invisible from
   nothing-arrived. `gp=` and `rn=` are on the always-painted row; the general
   fix is still owed.
4. **Nothing flies until the bed can express at least one of these failure
   modes.** A green arm from a model of the wrong part is not evidence, and
   flying on one is what spent the 150 trips.

### What rule 4 means exactly, so it cannot be argued down later

"The bed can express a failure mode" is an adjective. Here is the number.

**THE FLY GATE: one falsifiable PAIR, run on the I219 model, before any image
is composed.**

- **The kill arm.** A driver build with ONE required step omitted must come
  back **RED** in the bed.
- **The pass arm.** The same driver with that step present must come back
  **GREEN** on the same bed, same image, same run.

Both halves are required and the kill arm is the one that matters. A bed that
only ever agrees is the thing that spent 150 trips; an arm that has never been
seen to fail is not evidence that anything works (L-FALSIF). Do this for a
second requirement before flying if it is cheap, but one proven pair is the
floor, not the target. **Until that pair exists, composing a sitting is
forbidden**, and that is a standing instruction to the commander as much as to
anyone else, because the commander is who keeps composing them.

### A measurement flight is exempt from the fly gate, and has its own gate

A measurement payload reads registers and paints them, asserting nothing the
bed could falsify, therefore there is nothing for a pair to test.

**It is NOT exempt from proving its own instrument, and its gate is a READING
PAIR: every value the stage paints must be shown reading TWO DIFFERENT values
in the bed.** A stage that always paints `0x0000` is indistinguishable, on the
glass, from a board that genuinely has K1 disabled, and we would fly, read a
constant, and retire a live hypothesis on it. That is L-FALSIF pointed at a
reader instead of a checker, and it is worse for a measurement flight than for
a fix: a fix that does nothing shows up as an unchanged symptom, while a
reading that is a constant looks exactly like an answer. A value without its
pair is reported UNPROVEN on the glass rather than quietly painted beside the
proven ones.

### A flight carries what MAIN carries, not what the commander intended

A flight declared a MEASUREMENT flight is still composed from main, therefore
anything landed on main before composition is on the stick whatever the
commander said.

**RULE: anything not proven by the fly gate ships DISABLED BY DEFAULT, and the
default flips only when its pair is green.** An intention stated in a message
is not a control; the default in the code is the control. This is L-FALLBACK
one level up: do not let an unproven path be reachable just because nobody
meant to reach it.

Candidate LESSONS row, unpaid because it was caught before it cost anything: a
commander instruction that names what is NOT on a flight is unenforceable when
the flight is composed from main.

### The bed models only what the spec SPECIFIES

Where the spec is silent, the bed stays silent and **the question moves to the
board**. Modelling an unspecified sequence means inventing it, and a bed built
from our invention is our assumptions wearing a device id, which is the same
defect as a bed derived from our driver.

The direct consequence: some questions have no bed answer even in principle,
so a measurement sitting is the only instrument that reaches them. Which rows
those are is settled in one place only, "The remaining requirements" below.

**RULING: a bed must not advertise an identity it does not implement.** Either
implement the I219 semantics or advertise the 82574 it actually is, never
both. A bed that answers "what part is this" correctly and then behaves as a
different chip is worse than modelling the wrong part openly: an honest
mismatch gets caught the first time somebody compares device ids, and this one
cannot be caught that way at all. The family model now advertises `8086:100E`
(82540EM), which is what it decodes, with `15B8` reserved for the real I219
model.

**RULING, and it is what makes the arms worth anything: the driver is built
from the SPEC blind to the model, and the model is built from the SPEC blind
to the driver.** Neither reads the other until both are landed. Two
implementations of one document disagree in the places the document is
ambiguous, which is exactly where the hardware bites. One implementation
checked against a bed derived from it is a harness pointed at the half we
already have (L-ORACLE).

## Why the bed could never have told us (L-OPTIONAL, L-FREEDOM)

codex-vm models an e1000. The board has an I219. Every requirement below is
invisible to a model of the wrong part, therefore **every bed arm is green by
construction and stays green however wrong the driver is.** The bed is not
less faithful; it is answering a different question.

**The near-miss that proves what a board reading buys.** The bed powers
`770.17` up at `0x4000`. **The board reads `0xd104`, five bits apart.** Those
extra bits are the platform's firmware configuration and they exist in no
model we have. `e1000-k1-configure` read-modify-writes, therefore they
survive. **A constant write, which is the obvious implementation and the one a
bed alone would have fully vindicated, would have cleared five bits of
firmware state on every boot.** Because `e1000-k1-configure` ORs bit 13 and
leaves the rest, the board becomes `0xf104` and the five firmware bits
survive. Nothing in the bed could catch that, because the bed's power-up value
has no firmware bits in it to lose. This is
L-BEDTRUE one level over: not a default that is only true in the bed, but a
bed VALUE that is only true in the bed, and no number of additional bed arms
could have caught it.

**An arm asserting an exact register value quietly encourages the constant
write.** Assert the transmit, not the word.

## The board readings

| reading | value | what it settles |
|---|---|---|
| `pch 770.17` | `0xd104` | `giga-k1-dis = n`, `k1-en = y`. K1 is ENABLED at gigabit on this board, therefore the K1 layer is NEEDED and the hoped-for cheap outcome is dead. The register was reachable and answered a plausible live value, which also says the cited control exists on this part rather than only in the document |
| `mdio-gate open` | `n`, reads working | This part does not refuse an unowned MDIO caller, therefore the vendor driver's abort has no enforcement half here |
| BAR0 + `0x34` | `0x00000000` on a live path | Verdict `ZERO-ON-LIVE-PATH`. The bracket proves the PATH, not the OFFSET: an idle implemented register reads zero exactly like an unimplemented one |
| `gprc-before=3`, `stats gprc=0` | | Nothing reached the MAC during `nicring`'s own window. **It does not prove the stall**: a stalled MAC and a silent wire produce the same zero. What it rules out is frames arriving and the ring losing them, because a frame that reaches the MAC increments GPRC before any descriptor is involved |

**Separating a hole from an idle register at `0x34` needs a WRITE and
readback, which is not passive. RULED: do not run it.** It is an active write
to an unidentified offset on the only board we have, on the device under
repair, and if `0x34` is live it is an indirection register whose write starts
a kumeran cycle against the PHY we are bringing up. **No decision hangs on the
answer**, because the cited `770.17` is confirmed as both the mechanism and
necessary. **It comes back if `770.17` proves insufficient on a later
sitting**, at which point the question has a reason attached and earns the
risk. An instrument that cannot separate two cases is recorded as not
separating them, and the missing separation is bought only when something
downstream depends on which case is true.

**The instrument is already in place** for whichever reads a later sitting
wants: `pch-state` reads raw MMIO off BAR0, confirmed at
`00:1f.6 8086:15b8 B0=df400000 MAP=ok`.

## What the family requires that we do not do

From `drivers/net/ethernet/intel/e1000e/ich8lan.c`:

| requirement | Linux | ours |
|---|---|---|
| K1 disabled at 1 Gbps or the MAC STALLS | `e1000_configure_k1_ich8lan()` | **DONE, cited 770.17, ships ON** |
| SW/FW/HW semaphore before PHY and select MAC access | `e1000_acquire_swflag_ich8lan()`, `EXTCNF_CTRL.SWFLAG` | **DONE, ships ON, proceed-on-failure** |
| ULP forcibly disabled, because its state cannot be known | `e1000_disable_ulp_lpt_lp()` | **The row splits in two. The ENTRY half is built and ships OFF; the EXIT half is unspecified in the documents we hold and must not be guessed** |
| LCD config reloaded from NVM after PHY reset | `e1000_post_phy_reset_ich8lan()` | **WIRED, ships OFF.** `e1000-lcd-reload` is called from `na-phy-kick` with the device id threaded through `na-bring-up`, `na-bring-up-after` and the four stages that reach them. Before that it had no production caller at all, so the constant was a switch on a wire to nothing (L-UNCALLED) |
| PHY may be in SMBus mode owned by firmware | `e1e_force_smbus()`, `LANPHYPC` toggle | read half only; the LANPHYPC toggle is uncitable from what we hold |
| Latency Tolerance Reporting for LPT and newer | `e1000_platform_pm_pch_lpt()` | **UNCITABLE from what we hold** |

**The shape is not a count.** Two shipped ON, two wired and shipping OFF, one
built as its read half with the write declared unbuilt, one uncitable. **Three
of the original rows contain a half that cannot be written from what we hold
at all** (the ULP exit sequence, the LANPHYPC toggle, and LTR entirely), so
naming those absent-and-therefore-todo overstates the remaining work by three
items that no effort closes without a document nobody here has. That is
L-ADJECTIVE's second half: a number standing in for a shape.

**Re-run the grep rather than quoting any count from this file** (L-COUNT).

## Citations, and their strength differs

**`EXTCNF_CTRL` = MAC CSR `0x00F00`**, 82583V rev 2.6 section 9.2.2.15. Bits 5
= MDIO/NVM SW Ownership (SWFLAG), 6 = MDIO/NVM HW Ownership, 7 = MDIO MNG
Ownership. Protocol at section 4.5.2: a request is registered by WRITING 1b to
your own bit, and the requester is granted access only when that same bit
READS BACK 1b, which it does only if the other two are 0b; at most one is set
at any time; the owner writes 0b when done. SW and HW bits clear on reset, the
MNG bit only on LAN_PWR_GOOD or by firmware. Hardware sets its own bit while
loading the extended configuration area.

**CAVEAT, and it must travel with the number:** that is the 82583V's MAC CSR
map, and the I219's MAC is in the PCH. `0x00F00` is **corroborated by family,
not cited for the part**. Cite it as family corroboration or the citation
claims more than we have.

**K1: PHY page 770, register 17**, I219 rev 2.02 section 9.5.5.2. Bit 13
`Giga_K1_disable` ("When set, the I219 does not enter K1 while link speed at
1000 Mb/s"), bit 14 `K1 enable`, both RW and both defaulting to 0b. Section
9.3: register 31 at PHY address 01 is the page register and belongs to no
page. **Bit 14 carries a footnote nothing in our driver acts on: in SMBus mode
it is cleared, and the register must be reconfigured after switching back to
PCIe**, which ties this row to the SMBus row rather than leaving it
independent. **MODELLED and NOT cited: that K1 left enabled at 1 Gbps stalls
the MAC.** The datasheet documents the control, not the consequence; the
consequence is the vendor driver's behaviour and is the proposition this
campaign exists to test.

**ULP: PHY page 779, register 16, "ULP Configuration 1"**, I219 rev 2.02
section 9.5.7.1. Bits: 0 START ("when set the HW will start the auto ULP
configuration, auto cleared once configuration is done"), 1 SW_ACCESS, 2
ULP_IND ("Power Up from ULP indication"), 4 STICKY_ULP, 5 INBAND_EXIT, 6
WOL_HOST, 14 FORCE_ULP, 15 RESET_ULP_IND. Register 17 on the same page is ULP
Configuration 2, and 18 to 20 are ULP SW Control, SW Control and OBLCD
Control. This one IS cited for the part. **It is a PHY register, so reading it
needs MDIO working**, and section 9.2 gates that: "access using MDIO should be
done only when bit 10 in page 769 register 16 is set", with a 10 ms delay
required after an LCD reset before any MDIO access at all.

**Force SMBus: PHY page 769, register 23, bit 0**, RW, default 0b, section
9.5.3.4: "Force SMBus, reset on PCI reset de-assertion."

**LCD after PHY soft reset: section 5.2**, "Reset Operation", under PHY Soft
Reset: "A PHY reset caused by writing to bit 15 in MDIO register 0. Setting
the bit resets the PHY, but does not reset non-PHY parts. The PHY registers
are reset, but other I219 registers are not." Then the obligation: **"The
integrated LAN controller configures the LCD registers. Other I219 GbE LCD
registers do not need to be configured."**

**FWSM: no cited offset exists in this tree** and an uncited one must not be
given. The string appears in NEITHER datasheet, zero matches. Any offset would
come from the vendor driver, which is the one source this campaign forbids. If
a stage needs FWSM, that is a gap in our documents rather than a lookup.

**`KMRNCTRLSTA` and the kumeran indirection: ABSENT, and the absence is
stronger than a grep.** `KMRN` and `kumeran` are zero hits in both datasheets
in every casing, and by OFFSET the only MAC CSR map this tree holds runs
82583V 9.2.2.10 `FCT` at `0x00030` straight to 9.2.2.11 `VET` at `0x00038`,
**with nothing at `0x00034`**. The two `0x34` hits are PCI config space
`Cap_Ptr` (10.1.2.15), a different address space. A map with a HOLE where the
register should sit is a much stronger statement than "not mentioned".

The positive control that makes that a finding rather than a failed search:
the same sweep over the same two files reads `EXTCNF` 4/7, `MDIC` 6/7 and `K1`
28/4 in I219 and 82583V respectively.

**Applying our own accepted standard, family corroboration, comes back
NEGATIVE for `0x00034`**: the family document does not merely omit the
register, it allocates the neighbouring offsets and leaves that one empty. We
do not get to use family corroboration to accept `0x00F00` and then discard it
when it answers the other way. **It is not proof for THIS part** (an absence in
the wrong map cannot condemn a register on a part the map does not describe),
but combined with the board's zero read it was enough: **the kumeran path is
DELETED from `E1000e.codex`** rather than left unreachable, because Perforce
keeps the history and an uncalled path is a path nothing tests (L-UNCALLED).
Deleted: `e1000-k1-configure-kumeran`, `e1000-kmrn-read`, `e1000-kmrn-write`,
the five `KMRNCTRLSTA` constants and the three CTRL speed-forcing constants
only it used, 84 lines, nothing else in the tree referencing any of them.

**LTR: absent by name AND by concept.** Zero matches in both datasheets for
`Latency Toleranc`, `Tolerance Reporting`, `Snoop Latency`, `LTRSND`,
`LTROVR`, `\bLTR\b` and `LTRV`; the apparent
hits are `PLTRST#` and a WoL field name. By concept: `L1.2`, `OBFF`, `snoop`,
`service interval` and `ASPM` are all zero, `latency` occurs exactly once and
is about the K1 exit sequence, and all 43 `tolerance` hits are crystal
frequency and temperature. There is no vocabulary left for it to hide under.
LTR is a PCIe capability configured on the PCH side, and the PCH's CSR map is
in no document here. **A substring match is not a mention**, which is how an
earlier count answered `LTR 7` off `uLTRa Low Power`,
`str_wol_pkt_no_ind_from_fLTR_timeout` and `PCIECLKRQ`.

## The placement rule: state WHICH RESET clears it, and sit after that one

Twice, placement rather than content was the whole of the work, and both times
the code was correct and inert until it moved.

- **K1 lives in a PHY register that the PHY soft reset restores to its NVM
  value**, therefore it must be written AFTER `e1000-phy-bring-up`. A write
  placed before the reset is genuinely gone; the bed corroborates this rather
  than it being taken on report, because `tools/codex-vm.c` sets `i219_k1_reg`
  from `i219_k1_nvm` on reset.
- **SW ownership is cleared by the MAC reset**, therefore the semaphore is
  taken AFTER `e1000-reset` and held across the PHY work.

**Two resets, two registers, two placements, and each step in the other's
position is silently undone by a reset that is not looking at it.** Neither
failure announces itself: the register reads back correct at the moment it is
written and is wiped afterwards by something else. So the rule is not "state
where it sits relative to PHY reset" but **"state which reset clears it, and
sit after that one"**, and every requirement that writes PHY or LCD
configuration must state its answer.

**This is requirement 5 arriving by the back door.** The LCD-reload row and
the K1 placement are the same underlying behaviour: a PHY reset returns
configuration to its NVM state, so anything written before one must be
rewritten after it. We did not implement the requirement, we collided with its
mechanism while placing an unrelated write. 770.17 IS one of the registers
section 5.2 is talking about, therefore the LCD reload is the general
obligation and K1's placement is one instance of it.

## Requirement 1: K1

`e1000-k1-configure` writes the cited `770.17`, answers the register read
back, and runs AFTER bring-up. **`e1000-pch-k1-required` is TRUE**, and what
flipped it is the board reading rather than another bed green: `770.17 =
0xd104` says K1 is enabled with the disable bit clear on the actual part, so
the layer is NEEDED and not merely conforming.

**The bed's fly gate for K1**, one image, one run:

| bed | 770.17 at power-up | kill arm tx | pass arm tx |
|---|---|---|---|
| `-i219` (NVM leaves K1 on) | `0x4000` | **0, did not complete** | 1 |
| `-i219-k1-nvm 0` | `0x0000` | 1 | 1 |
| default 82540EM, no `-i219` | absent, MDIC answers error | 1 | 1 |

Row 1 is the gate: the same transmit that fails with K1 left at its NVM
setting succeeds once `Giga_K1_disable` is written, `0x4000` to `0x6000`, with
nothing else changed. Rows 2 and 3 are the controls that say the stall is
caused by the K1 bits rather than by the I219 path or by the arm. The power-up
value is the platform's NVM setting, which we do not have for this board,
therefore `-i219-k1-nvm` is a flag rather than an invention (`-i219-k1-nvm 1`
is the default and is the condition the campaign is about).

The driver-side pair is `codex/test/e1000-pch-k1` and
`codex/test/e1000-pch-k1-off`; its kill half re-runs `e1000-link-up` so that
the omission is produced by the arm's own calls rather than by borrowing
`e1000-pch-k1-required` to express it. **The control is what makes the zeros
attributable**: same binary, same I219 path, only the K1 bits differ.

**The first version of the arm was inverted, and the inversion is worth
keeping.** It measured RECEIVE, and the injected frame reached the ring BEFORE
the stall condition existed, so it reported frame-availability rather than MAC
progress: kill arm yes, pass arm no, exactly backwards. **Transmit is
initiated by the driver, in order, and cannot be satisfied in advance.** An
arm whose two halves come out the wrong way round is a defect in the arm, not
a finding.

**A green pair proves the driver CONFORMS TO THE CITATION. It does not prove
the part behaves as cited**, and no bed can. Conformance to a citation IS the
standard for flipping a required-flag to `True`, because a citation for the
part is the best evidence this tree can hold short of the board, and holding a
conforming driver disabled forever would make the fly gate unmeetable in
principle.

**`codex/test/i219-k1-mechanism` was built to assert a DISAGREEMENT between
the driver and the bed, and that premise is retired rather than its
expected.** This is L-INSTRUMENT: a test that reads a thing to observe A is
broken by that thing correctly learning to do B, and the repair is to re-point
it at a question it can still answer, NEVER to soften the assertion to match
the new output. Its re-aimed shape is the driver's K1 step setting
`Giga_K1_disable` with the MAC transmitting, and **sabotage of that step as
the kill arm**. **A driver change that retires an arm's premise and the
arm's re-aim must be ONE changelist**, because there is no order of two in
which both are green and R-GATE forbids leaving main red between them.

**The pair keeps a LEADING KILL produced by the arms themselves rather than by
a constant.** Both arms re-run the PHY reset, so the ladder stays kill, pass,
kill, pass. A pair whose leading kill is a shipped constant loses the state
that lets it say no the moment that constant flips.

**The gate cannot see any of this**: the battery is not run at the gate, so a
collision between a driver change and an arm is visible only to whoever runs
the arm.

## Requirement 2: the MDIO/NVM semaphore

`e1000-swflag-acquire`, `e1000-swflag-held`, `e1000-swflag-release`, gated by
`e1000-pch-swflag-required`. Arms: `codex/test/e1000-swflag`,
`e1000-swflag-mng`, `e1000-swflag-refused`, `e1000-swflag-k1` and
`e1000-swflag-strict`. **Ships ON, proceed-on-failure**, and the board decided
the policy: `mdio-gate open=n`
with reads working says this part does not refuse an unowned caller, and the
datasheet agrees the mechanism "does not block software accesses". What
remains is the ME race, which a bounded acquire narrows and an abort does not
narrow at all, because an abort converts a race into a refusal to bring the
NIC up.

**Cited in two halves with different strengths, and the weaker half is the one
the campaign is about.** CITED: the mechanism, 82583V 4.5.2 and 9.2.2.15. **NOT
CITED: that the part REFUSES MDIO without ownership.** The same section says
the mechanism "does not block software accesses to MDIO or the NVM, therefore
programmers can enable software to use or ignore this process at will", and
the bit table calls the register optional for the 82583V. **The bed PUNISHES
an unowned MDIO access, which is a modelling choice rather than a documented
behaviour**, so a kill arm proves our driver conforms to a bed stricter than
the citation, not that the board would refuse us. That direction is the safe
one, and it must not be read as evidence about what the hardware does.

**Two windows, not one long hold.** The semaphore taken in
`e1000-init-after-reset` is released when bring-up finishes, and
`e1000-pch-prepare` runs AFTER `e1000-init-at` returns, therefore the K1 step
is outside that window by construction and takes its own through
`e1000-k1-configure-guarded`. Holding across the whole of bring-up would
starve the ME for the length of a link wait.

**The two requirements stopped being independent the moment the first
shipped.** K1 required means the driver WRITES a PHY register during bring-up,
and without the semaphore that write goes out with SW ownership clear, which
is the exact unguarded access 4.5.2 exists to arbitrate. Neither flip is wrong
alone; **only the intermediate state is**.

**A coupling flagged from a document names the risk; only the call graph says
whether the guard reaches it.** The K1 step runs after `e1000-init-at`
returns, so it sat outside the bring-up guard entirely and taking the
semaphore inside init would not have covered it. `codex/test/e1000-swflag-k1`
proves the coupling: under `-i219-swflag`, init leaves `24576`, a wipe returns
`16384`, an UNGUARDED `e1000-k1-configure` leaves `16384` because MDIO refused
it, and the guarded call lands `24576`. Same function, same register, same
boot; the only difference is who holds bit 5. Neither `e1000-pch-k1` nor
`e1000-swflag` could see it, because each passes with the other requirement
broken.

**That arm's first version measured nothing and said so loudly**: every row
answered `-1`, because the arm's own reader reached 770.17 through MDIO
without holding the flag. **An instrument that reaches its subject through the
mechanism under test measures the mechanism.** The reader takes the semaphore
now, so the only unowned access in the boot is the one row 1 is about.

### The acquire loop: what was wrong and what the fix must preserve

The WRITE itself is right and the datasheet does not forbid it: 4.5.2 says a
request is registered by writing 1b to your own bit and the grant is denied by
that bit reading back 0b, so an acquire that saw MNG held and refused to write
could never acquire at all when firmware released. **What was wrong is
everything around it.** A full-register read-modify-write (`bit-or cur
e1000-extcnf-sw-own`, no mask) writes the MNG ownership bit back as 1b when
firmware holds it, registering a request on another agent's behalf against
4.5.2's "at most one bit is 1b at any time", and it writes back the extended
configuration area fields, which on I219 are the only thing the datasheet
documents this register for.

**Measured with an EXTCNF census in the bed, against the unfixed driver with
firmware holding MNG: `writes=6002 foreign=6002`.** Every single write was a
protocol violation. The registered figure of 2,000 was ONE call site's; the
acquire runs from `e1000-init-at`, `db3-init-after-reset` and
`e1000-k1-configure-guarded`. After the fix: **`writes=194 foreign=0`**, which
is 3 sites * 64 polls + 2 releases, with `final=00000080` unchanged so
firmware still holds MNG and nothing else moved.

**What the fix deliberately does not do: it does not write a bare `#0020`.**
Taken literally, "write ONLY the SW-ownership bit" zeroes the
extended-configuration fields, the one thing the I219 datasheet documents this
register for. Masking the three ownership bits off and setting ours fixes what
was measured wrong and leaves untouched what was not, rather than trading a
proven defect for an unproven one. The loop is 64 polls with a 100 us pause
instead of 2,000 immediate retries.

**THE DEFECT HAD NO FALSIFIER, AND THAT HAD TO BE FIXED FIRST.** The bed's
`i219_extcnf_write` computed `keep = mng`, preserving firmware's bit whatever
software wrote, so a driver that read-modify-wrote the whole register behaved
IDENTICALLY to one that wrote only its own. All four existing arms passed with
the defect present and with it fixed: a bed more forgiving than the spec
(L-GAP), so no amount of running the suite could have found this and no arm
could have proved the repair.

**THE SABOTAGE CAUGHT A VACUOUS ARM** in
`codex/test/e1000-swflag-strict`. Under `-i219-extcnf-strict` alone,
restoring the defect changed NOTHING. With MNG never held the register starts
at zero, so a read-modify-writing driver reads zero and writes only its own
bit and the defect cannot appear; with MNG held forever no acquire can succeed
however correct the protocol is, so both drivers answer 0. **Only a firmware
RELEASE separates them**, which is why `-i219-mng-release-after N` exists:
4.5.2 says the MNG bit clears when firmware clears it, so a release is the
ordinary case the poll loop was written for. Under `-i219-extcnf-strict
-i219-mng-release-after 3` the arm reads `acquire=1 held=yes` fixed and
`acquire=0 held=no` sabotaged. **That pair is the arm's whole value and it did
not exist until the sabotage demanded it.**

**THE STRICT FLAG IS NOT A CLAIM ABOUT THE PART and must stay off by
default.** Whether the real PCH enforces "at most one bit is 1b" is uncited.
The flag exists to express the failure mode so the driver can be measured
against the protocol, which is ours to get right whatever the part tolerates.
A bed that enforced it by default would be asserting the campaign's open
question as its answer.

## Requirement 3: the LCD reload after a PHY soft reset

**SHIPPING OFF.** `e1000-pch-lcd-reload-required` is False, `e1000-lcd-reload`
is the named omission point, and the pair is `codex/test/e1000-lcd-reload`
under `-i219`. The LCD registers are PHY registers, so the PHY SOFT RESET
clears them and the MAC reset does not, which places the reload at the same
boundary as K1 and the opposite one from the semaphore.

| row | what runs | 770.17 | send |
|---|---|---|---|
| left by init | `e1000-init` | 24576 | |
| 1 kill | `na-bring-up-after` | 16384 | 0 |
| 2 pass | `e1000-pch-prepare` | 24576 | 1 |
| 3 kill | `na-bring-up-after` | 16384 | 0 |
| 4 pass | `e1000-pch-prepare` | 24576 | 1 |

Both sides are the arm's own calls, therefore the ladder does not move when
the constant flips; only `shipped reload step` and `gate answers` do. **The
kill is `na-bring-up-after` rather than `na-bring-up`** because the latter
resets the MAC as well and a send failing after it could be the rings, which
is the confounder `e1000-pch-k1` avoids the same way.

**THE GATE'S OTHER SIDE WAS RUN, not reasoned about.** With the constant
flipped True in a local build the arm reads `shipped reload step: yes` and
`gate answers: 3`. **A gate that ships off is a path nothing calls
(L-UNCALLED) unless somebody runs the other arm once**, and the shipped
`.expected` records the False side.

**`e1000-init` PAYS THE OBLIGATION BY CONSTRUCTION AND NOTHING SAID SO.**
`e1000-pch-prepare` runs after `e1000-init-at` returns, therefore after the
BMCR reset inside `e1000-phy-bring-up`; the `left by init` row is the
evidence, and reordering those two lines now moves a row instead of silently
unmeeting the requirement.

**It is not `i219-k1-mechanism` again.** That arm resets through
`e1000-phy-bring-up` and reconfigures with a bare `e1000-k1-configure`, both
its own calls, and measures the register against transmit. This one puts a
DRIVER function on the kill side, so the row moves the day that function
starts reloading and moves back the day it stops.

## Requirement 4: ULP entry-disable

**Built as its entry half, ships OFF.** ULP **entry** is fully cited: rev 2.02
section 9.5.7.1, page 779 register 16, with STICKY_ULP bit 4 ("Enter ULP on
Link disconnect") and EN_ULP_LANPHYPC bit 10 ("Enable ULP on LAN disable") the
two conditions that put the part into the state whose exit we cannot specify.
`e1000-ulp-disable` clears exactly those, read-modify-write, answering on the
device as `e-ulp`. **The board already reads both clear** (`ulp 779.16=0800`,
sittings 8 and 9), so the write is a NO-OP there, and `0x0800` is bit 11
inside the Reserved 13:11 which the mask leaves alone.

**The ULP EXIT sequence is unspecified in the documents we hold, is not
attempted, and must not be guessed.**

**ULP cannot carry a fly-gate pair even though its registers are cited.**
`ULP_IND` at bit 2 is "Power Up FROM ULP indication", a status flag saying the
part HAS come up from ULP, not a state meaning it is IN ULP, and
`RESET_ULP_IND` at bit 15 clears that flag. A driver that never clears an
indication is not thereby broken, so the pair has no kill arm. Section 6.4
puts entry and exit on the WIRE, not in the driver: ULP is entered on link
disconnect with `STICKY_ULP`, and "once energy is detected the I219 will exit
ULP mode".

## Requirement 5: Force SMBus, and requirement 6: LTR

**Force SMBus is cited and fails the pair bar for a sharper reason.** 9.5.3.4
says the bit is "reset on PCI reset de-assertion", and section 5.2 agrees from
the other side: de-asserting PCIe reset "causes a switch from SMBus to PCIe".
**A host driver running over PCIe finds the bit CLEAR by specification**, so a
bed that started with it set to give the driver something to clear would be
contradicting the datasheet to manufacture an arm.

**LTR needs a document this tree does not have.** Nothing to cite, nothing to
read, no register to gate.

## The remaining requirements, measured against the documents we hold

**THE DISTINCTION THAT DECIDES IT: A CITED REGISTER IS NOT A CITED OBLIGATION,
AND ONLY AN OBLIGATION CAN CARRY A FLY-GATE PAIR.** A pair needs a DRIVER
ACTION that the document requires, so that omitting it is a defect rather than
a preference. A register the driver may write is not that.

| requirement | control cited | obligation cited | can carry a pair |
|---|---|---|---|
| K1 at 1 Gbps | 9.5.5.2 | vendor consequence | **YES, landed** |
| MDIO/NVM semaphore | 4.5.2, family offset | yes | **YES, landed** |
| LCD registers after PHY reset | 5.2 | **yes** | **YES, and it is next** |
| ULP | 9.5.7.1 | **no** | no |
| Force SMBus | 9.5.3.4 | **no** | no |
| LTR | absent | absent | **never** |

**A CENSUS KEYED TO THE OTHER IMPLEMENTATION'S VOCABULARY REPORTS ABSENT FOR A
REQUIREMENT THAT IS PRESENT.** `sw_lcd`, `SW LCD` and `LCD config` are zero in
both datasheets, and the LCD reload was reported absent on that basis; the
requirement is stated in the document's own words at section 5.2 instead. The
absence was manufactured by the filter.

**The next bed pair is the post-reset reconfiguration arm, not ULP.** The
model already has what it needs: `e1000_phy_reset_regs` restores 770.17 to its
NVM value on a PHY soft reset, which is the LCD-reload obligation in miniature
for one register, and **no arm exercises it** because the existing K1 pair
never issues a second reset. The pair to build is: configure K1, issue a PHY
soft reset, transmit without reconfiguring, which must come back RED, against
the same run reconfiguring after the reset, which must come back GREEN.

**ULP and Force SMBus are CLOSED as bed work for a stated reason rather than
deferred**, so nobody re-derives them. The standing argument that some
questions have no possible bed answer belongs to LTR and to ULP's driver-side
exit.

## What the pch-state stage asked the model for

**PCI command register: `-nic-bme-clear`.** The bed read `0x0007` on every
run, so a stage reading the command register had never seen the BME-clear
case. With the flag the NIC refuses the Bus Master Enable bit however often it
is written and does no DMA while it is clear. Measured: default reads command
7, BME 4, transmit 1; with the flag, command **3**, BME **0**, transmit
**0** -- enabled, addressable, answering registers, and moving nothing, which
is the state `Pci.codex`'s own prose already describes as giving no indication
why.

**DMAR: `-dmar`.** A DMAR table is published and linked into both the RSDT and
the XSDT at `ACPI_BASE + 0xA00` = `0xE0A00`. **HEADER ONLY**: signature,
length, checksum, host address width and flags, with NO remapping structures
behind it. That is enough to tell "an IOMMU is described" from "it is not" and
enough for nothing else.

**`EXTCNF_CTRL` varies, but only under `-i219`.** On the 82540EM model there
is no such register and the read falls through to the zero-initialised
register file, which is why a stage read a constant zero. Under `-i219` it
reads 0, `0x20` once SW ownership is acquired, and `0x80` under
`-i219-mng-holds`.

**Verification status differs across the three and must not be flattened.**
BME and `EXTCNF_CTRL` are verified by a guest probe on this bed. **DMAR is
verified by CONSTRUCTION only**: there is no ACPI support anywhere in
`codex/os` or `codex/foreword`, zero matches for `acpi` in either, so no guest
here can walk the RSDT. It is offered as a table that is written, not as a
table something has read.

**ONE FALSE PASS, AND IT IS THE USEFUL PART.** The first `-nic-bme-clear` run
reported transmit 0, which is what the feature predicts. It was wrong: the
flag had not parsed at all, so there was no NIC on the bus, `e1000-find`
answered None, and the probe's `tx` was the device-absent branch rather than
the DMA gate. **The tell was in the same output, `pci-command : 65535`, which
is a config read of a slot with nothing in it.** A pass that agrees with the
prediction is not evidence until the mechanism is checked, and an arm
reporting an all-ones config read has told you it found no device whatever
else it says.

## The semaphore bed pair

| bed | PHY read before | acquire reads back | PHY read after | after release |
|---|---|---|---|---|
| `-i219-swflag` | **-1, refused** | `0x20`, granted | `0x0154`, a real read | **-1**, door closed again |
| `-i219-mng-holds` | -1 | **0, refused** | -1 | -1 |
| `-i219` alone | `0x0154` | `0x20` | `0x0154` | `0x0154` |

Row 1 is the pair. **Row 2 is the control that matters most**: the request is
refused because firmware holds MNG, which is 4.5.2 exactly, and it is the case
a driver that assumes it always wins cannot survive. An acquire that cannot
fail is not evidence that the protocol was followed, and a function
unconditionally answering 1 passes row 1 and fails row 2. Row 3 says the flag
being off changes nothing, so every existing arm is untouched.

**The release half was not asked for and is kept because it fired.** After
writing 0b the door closes again, so the model is a semaphore rather than a
one-way latch, and an arm can tell a driver that releases from one that holds
forever.

**Enforcement is its OWN flag rather than riding `-i219`**, because two
requirements behind one switch cannot be told apart by an arm: a driver that
failed would fail for either reason.

**This requirement AGREED with the model on the first run**, which is the
blind build's other outcome and worth recording next to K1's disagreement.
Both sides read the same citation for the offset, the bits and the read-back
grant, so there was nothing to reconcile. **The method is not only a
disagreement detector**: when the document is unambiguous it produces two
implementations that match, and the agreement means something because neither
side saw the other.

## Still open

1. **asde is wedged for the third time**, and it is the only question the
   campaign has left outstanding.
2. **The ASDE path does not pay the LCD-reload obligation, and flipping the
   constant alone will not fix that.** Rows 1 and 3 of the reload ladder are
   `na-phy-kick` resetting the PHY with nothing behind it, so with
   `e1000-pch-k1-required` shipping True an ASDE arm hands whatever runs next
   a part stalled at gigabit. Before that flip both steps were off together
   and the path was merely incomplete; **the flip is what made it wrong**,
   which is the same intermediate-state argument that coupled the semaphore to
   K1. **`e1000-lcd-reload` needs a `dev-id` and the flight path does not
   carry one**: `dasd-run-part` and the `AsdeStageProbe` and `MscAlignProbe`
   sites take an MMIO base, so the constant's flip and a one-parameter thread
   through those three diag chapters are ONE change, and those are red's
   chapters. The note sits at `na-phy-kick` in the same shape as
   `e1000-reset`'s "it does not quiesce, and the caller must", because the
   obligation is invisible at the call site.
3. **The general recorder fix** named in campaign rule 3.
4. **The post-reset reconfiguration bed pair** described under "The remaining
   requirements".
