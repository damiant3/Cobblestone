# e1000e Service Model Notes

*What the silicon requires, with a section and page number against every
claim. Written 2026-08-04 by blu for lane B2 (the Intel I219-V on
Damian's ASUS). The worked model for this file is
`xHCI_ServiceModel_Notes.md`, and the standing rule it serves is the one
the keyboard campaign earned: **a test-bed arm is written FROM a cited
spec section, or it is not written.***

## Why this file exists

`codex/os/kernel/E1000e.codex` and the e1000e model in `tools/codex-vm.c`
were written from the same reading of the same family datasheet, and the
model's own comment says so:

> Register offsets and bit positions are from the 8254x/8257x family
> datasheet, the same reading E1000e.codex was written from -- which is
> exactly why this model is not an independent oracle.

Until 2026-08-04 no e1000 document was in the tree at all, so that
reading could not be checked by anyone, and every green the bed produced
was the bed agreeing with the driver about a shared guess. That is
L-ORACLE. This file is the second direction.

## The two documents, and what each is authoritative for

| File | Covers | Authoritative for |
|---|---|---|
| `Intel_I219_Datasheet.pdf` (rev 2.02, May 2015, 278 pp) | The I219 **LAN Connected Device**: the PHY, the MDIO register space, the NVM, power management | Everything reached THROUGH MDIC on the part we drive, and the rules for reaching it |
| `Intel_82583V_Datasheet.pdf` (rev 2.6, June 2014, 385 pp) | An 82583V: a discrete PCIe GbE controller of the same MAC family | The **MAC CSR map** -- register offsets, bit positions, descriptor rings, MDIC's own layout |

`.txt` extractions sit beside both for Grep; page markers in the text are
`=== page N ===` and match the PDF's own page numbering to within the
front matter.

**The honest gap, and it is the one to keep in mind.** The I219's MAC is
not a discrete controller: it is integrated into the PCH, and its CSR map
is not in the I219 datasheet. Nothing public in this tree documents the
MAC register block of the exact part at `00:1f.6`. So every MAC-side
offset below is **corroborated by family, not cited for the part**. The
82583V agreeing with us raises confidence a long way and does not close
the question, and the place that distinction will bite is any register
where the PCH-integrated design had a reason to differ. Where the I219
datasheet does speak about the MAC's behaviour -- which it does for MDIO
timing -- it wins.

## MAC register offsets: every one confirmed

82583V section 9.2, "Driver Programming Interface", one register per
heading, offset in the heading. Checked against
`codex/os/kernel/E1000e.codex` "Register Offsets" and against the
`E1000_REG_*` defines in `tools/codex-vm.c`.

| Register | Ours | 82583V | line |
|---|---|---|---|
| CTRL | 0x0000 | `Device Control Register - CTRL (0x00000 / 0x00004; RW)` | 12253 |
| STATUS | 0x0008 | `Device Status Register - STATUS (0x00008; R)` | 12545 |
| MDIC | 0x0020 | `MDI Control Register - MDIC (0x00020; RW)` | 13105 |
| ICR | 0x00C0 | `Interrupt Cause Read Register - ICR (0x000C0; RC/WC)` | 14648 |
| IMC | 0x00D8 | `Interrupt Mask Clear Register - IMC (0x000D8; W)` | 15107 |
| RCTL | 0x0100 | `Receive Control Register - RCTL (0x00100; RW)` | 15235 |
| TCTL | 0x0400 | `Transmit Control Register - TCTL (0x00400; RW)` | 16324 |
| TIPG | 0x0410 | `Transmit IPG Register - TIPG (0x00410; RW)` | 16438 |
| RDBAL / RDBAH | 0x2800 / 0x2804 | same | 15655 / 15664 |
| RDLEN / RDH / RDT | 0x2808 / 0x2810 / 0x2818 | same | 15671 / 15722 / 15734 |
| TDBAL / TDBAH | 0x3800 / 0x3804 | same | 16528 / 16578 |
| TDLEN / TDH / TDT | 0x3808 / 0x3810 / 0x3818 | same | 16585 / 16592 / 16651 |
| MTA | 0x5200 | `Multicast Table Array - MTA[127:0] (0x05200-0x053FC; RW)` | 16096 |
| RAL / RAH | 0x5400 / 0x5404 | `RAL (0x05400 + 8*n)` / `RAH (0x05404 + 8*n)` | 16211 / 16236 |

Line numbers are into `Intel_82583V_Datasheet.txt`. Twenty-one for
twenty-one. No offset in the driver is wrong.

## MDIC's layout, confirmed

82583V line 13105, `MDI Control Register - MDIC (0x00020; RW)`:

| Field | Bits | Ours |
|---|---|---|
| DATA | 15:0 | `e1000-mdic-data = #0000FFFF` |
| REGADD | 20:16 | `bit-shl reg 16` |
| PHYADD | 25:21 | `bit-shl phy 21` |
| OP | 27:26 -- `01b = MDI write`, `10b = MDI read` | `#04000000` / `#08000000` |
| R (Ready) | 28 | `e1000-mdic-r = #10000000` |
| I (Interrupt Enable) | 29 | not used, left clear |
| E (Error) | 30 | `e1000-mdic-e = #40000000` |

Two obligations the datasheet states and we already meet, recorded so
nobody removes them by accident. R: *"It should be reset to 0b by
software at the same time the command is written."* E: *"Software should
make sure this bit is clear (0b) before making an MDI Read or Write
command."* `e1000-mdic-frame` builds the word from OP, PHYADD, REGADD and
DATA only, so both bits are clear in every command we issue.

## The PHY address, and the trap in reading it

`e1000-phy-addr = 1` is **correct**, and it takes two sources to say so
safely, because one of them appears to say the opposite.

I219 Table 9-1 "Address Map" (line 4824) lists the IEEE clause-22
registers -- 0 Control, 1 Status, 2 PHY Identifier 1, 3 PHY Identifier 2,
15 Extended Status -- under **PHY address 02**, and section 9.3 opens
with *"the general registers are located under PHY address 01 and the PHY
specific registers are at PHY address 02"*. Read alone, that says our
BMCR/BMSR/ID reads at address 1 are aimed at the wrong address.

They are not. The same section 9.3 (line 4748) settles it:

> The I219 registers in PHY address 01, are divided into pages. Each page
> has 32 registers. **Registers 0-15 are identical in all the pages and
> are the IEEE defined registers.**

So registers 0-15 answer at address 01 in every page, which is what we
read. The 82583V confirms it from the MAC side independently -- MDIC
PHYADD, line 13140: *"1 = Gigabit PHY. 2 = PCIe PHY."*

Recorded at length because the wrong reading is the natural one: a table
of contents is more prominent than a sentence of prose three pages
earlier, and acting on the table would have moved every PHY access in the
driver onto an address whose registers 16-31 mean something else entirely.

## What the driver does NOT do, and the I219 requires

These are the live findings. All three are on the MDIO path, which is the
whole of B2b, and all three have the same failure signature: a link that
never comes up while every MAC register reads exactly as it should.

### 1. The 10 ms post-reset delay is absent

I219 section 9.2 "MDIO Access" (line 4736), in full:

> After LCD reset to the I219 a delay of 10 ms is required before
> attempting to access MDIO registers.

`e1000-init-at` calls `e1000-reset`, which pulses CTRL.RST and waits only
for the bit to clear itself. `e1000-init-after-reset` then allocates the
rings and buffers, clears the 128-entry MTA, reads the station address
and sets up both rings, and only then does `e1000-link-up` call
`e1000-phy-bring-up`, whose first act is an MDIC write. So there is work
in between, but **no deliberate delay and nothing that bounds the
interval from below** -- that sequence is microseconds, not ten
milliseconds. Under codex-vm none of this can fail, because the model
answers MDIC the instant it is written.

**The assumption this finding rests on, stated because it is the weakest
joint and nothing in this tree can close it.** The datasheet says the
10 ms follows an **LCD reset** -- a reset of the LAN Connected Device,
which is the PHY. Our driver pulses **CTRL.RST**, which the 82583V calls
a Device Reset that *"performs a reset of the MAC function"* (line 12440),
and the same datasheet names **CTRL.PHY_RST** separately as the way to
reset the internal PHY (line 2418). So MAC reset and PHY reset are
distinct operations on this family, and treating the 10 ms window as
opening at our CTRL.RST is an **inference, not a citation**. The phrase
"LCD reset" appears three times in the whole I219 text (lines 3090, 4736,
8344) and is nowhere defined against the MAC's CTRL. Two things follow:
the finding is a spec violation only if the equation holds, and the
cheapest way to make the question moot is to satisfy the delay from
whichever reset happened last, which costs 10 ms once per bring-up.

### 2. MDIO slow mode is never set

Same section, the next sentence:

> Access using MDIO should be done only when bit 10 in page 769 register
> 16 is set.

Page 769 register 16 is `Custom Mode Control` (Table 9-1, line 4977), and
its bit 10 is defined at line 7436:

> **MDIO frequency access**, bit 10, RW, default 0b.
> 0b = normal MDIO frequency access.
> 1b = reduced MDIO frequency access (**required for read during cable
> disconnect**).

Two things follow. The default is 0b, so the condition section 9.2 states
is not met on a part nobody has configured, which is the part we get.
And the parenthesis names the case that matters most to a bring-up
campaign: **a disconnected cable is exactly the state a bench machine is
in**, and the mode the datasheet says is required for reading in that
state is the mode we never enter. A PHY that reads as absent because the
cable is out, on a driver that then proceeds without it, is
indistinguishable from a PHY that is genuinely unreachable.

### 3. There is no page register support at all

I219 section 9.3 (line 4752):

> For PHY address 01, in order to access registers other than 0-15,
> software should first set the page register to map to the appropriate
> page. [...] Setting the page is done by writing **page_num x 32** to
> **Register 31**. This is because only the 11 MSB's of register 31 are
> used for defining the page. During write to the page register, the five
> LSB's are ignored.

The driver reaches registers 0, 1 and 2 and nothing else, so it has never
needed a page. Finding 2 cannot be fixed without this: `769 x 32 = 0x6020`
written to register 31 is the prerequisite for touching Custom Mode
Control at all.

### The chapter's own prose says this ground is already covered

Worth naming, because it is what a reader of the driver will hit instead
of this file. `codex/os/kernel/E1000e.codex:349-356`, above
`e1000-phy-bring-up`:

> On a part with a discrete PHY the MAC's own SLU is usually enough and
> this is redundant; on a PCH-integrated part it is not, and the failure
> when it is skipped is a link that simply never comes up, with every MAC
> register reading exactly as it should.

That is this exact symptom, attributed to a cause the driver already
handles, so an engineer who reads the chapter concludes the MDIO path is
done. It is rule-12 prose about our own code with nothing evaluating it,
and it is doing active harm: three of the requirements on the very path
it describes are unimplemented in the function directly beneath it.

Note the ordering problem this creates and do not paper over it. Setting
slow mode is itself an MDIO access, so it cannot satisfy the precondition
in section 9.2 that MDIO access be done with slow mode already set. The
datasheet does not resolve this; the only coherent reading is that the
sentence governs the register reads that matter (the PHY registers proper)
and that the page-set and mode-set pair is the bootstrap that gets you
there. Anything stronger than that is a guess and should be labelled as
one.

## CTRL: bit positions right, and one bit we set that this family says to clear

82583V CTRL table (line 12311 onward), against the chapter's "CTRL bits"
block:

| Bit | 82583V | Ours |
|---|---|---|
| 5 | **ASDE**, Auto-Speed Detection Enable | `e1000-ctrl-asde = #00000020` |
| 6 | **SLU**, Set Link Up | `e1000-ctrl-slu = #00000040` |
| 26 | **RST**, Device Reset, *"self-clearing"* | `e1000-ctrl-rst = #04000000` |

All three positions correct, and RST's self-clearing behaviour -- which
`e1000-await-reset` and the codex-vm model both depend on -- is stated
outright at line 12437.

**The live finding is ASDE.** Line 12345:

> ASDE, bit 5. Auto-Speed Detection Enable. When set to 1b, the MAC
> **ignores the speed indicated by the PHY** and attempts to
> automatically detect the resolved speed of the link and configure
> itself appropriately. **This bit must be set to 0b in the 82583V.**

`e1000-link-up` writes `bit-or ctrl (bit-or SLU ASDE)`, so we set it. On
the 82583V that is explicitly contrary to the datasheet. Whether the
PCH-integrated I219 MAC carries the same requirement is **not answerable
from any document in this tree**, and that is the honest state: the only
citable source for this register says clear it, and the part we drive is
not that source. This is a candidate cause for B2b and it is cheap to
test on the next metal flight, since clearing one bit is the whole change.

The chapter prose beside it is also not what the datasheet says:

> SLU asks the MAC to bring the link up and ASDE lets it settle speed and
> duplex from the PHY's own auto-negotiation, which is why this driver
> never writes a speed.

ASDE is the bit that makes the MAC **ignore** the PHY's speed indication
and detect it itself, which is close to the opposite of "settle it from
the PHY's own auto-negotiation".

SLU's wording is worth keeping, because it is stronger than our prose and
justifies why the driver must set it (line 12353): *"The Set Link Up bit
MUST be set to 1b to permit the MAC to recognize the link signal from the
PHY, which indicates the PHY has gotten the link up, and to receive and
transmit data."*

## STATUS.LU comes from the PHY, which settles what the bed default models

82583V section 6.2.3 (line 6308):

> PHY link status indication -- The PHY provides a direct internal
> indication of link status (LINK) to the MAC to indicate whether it has
> sensed a valid link partner. [...] **The MAC relies on this internal
> indication to reflect the STATUS.LU status** as well as to initiate
> actions such as generating interrupts on link status changes.

So STATUS.LU is a report of the PHY's link, not an echo of CTRL.SLU. That
is the citation `-e1000-phy-link` was missing: the arm that flag turns on
is the **spec-correct** behaviour, and codex-vm's default -- LU granted
from SLU alone -- models a device that does not exist.

Leaving the default lenient was still right when the flag landed
(L-FALLBACK: every existing green was measured against it). But the flag
is now the accurate arm rather than a pessimistic one, and the direction
of travel is to make it the default once the tests that depend on the old
behaviour have been moved over. Whoever does that should say in the CL
that they are moving the floor, not adding an arm.

## TIPG: one field diverges, and the prose beside it is wrong

`e1000-tipg-default = #0060200A`, decoded against the 82583V field table
(line 16466): IPGT bits 9:0 = 10, IPGR1 bits 19:10 = 8, IPGR2 bits 29:20
= 6.

The 82583V's own recommendation (line 16502):

> The actual time waited for IPGT and IPGR2 is 6 MAC clocks (48 ns @
> 1 Gb/s) longer than the value programmed in the register. [...]
> Therefore, the suggested value that software should program into this
> register is **0x00602006**. This corresponds to: IPGT = 6 (6+6 = total
> delay of 12); IPGR1 = 8; and IPGR2 = 6. [...] For previous
> implementations, the actual time waited for any of the IPG timers was
> two MAC clocks (16 ns) longer than the value programmed in the register.

So IPGR1 and IPGR2 match the suggestion exactly and only **IPGT** differs:
ours is 10, the pre-PCIe value, chosen when the hardware added 2 MAC
clocks so that 10+2 came to the intended 12. On a part that adds 6, the
same 10 comes to 16. That is legal and conservative rather than broken --
a longer inter-packet gap costs throughput and nothing else -- so **this
is recorded, not changed.** Changing a working transmit path on a
throughput argument, with no metal measurement behind it and every
emulator green recorded against the current value, is the wrong trade
today.

The prose in the chapter is a different matter and is simply false:

> TIPG carries three inter-packet-gap fields [...] 10, 8 and 6 are the
> copper 802.3 values.

They are not 802.3 values, they are device-clock counts whose correct
setting depends on which implementation is counting. And 802.3 does have
something to say here, which the same paragraph of the datasheet gives
(line 16500) -- *"According to the IEEE 802.3 spec, IPGR1 should be 2/3
of IPGR2"*, and *"IPGR1 and IPGR2 are significant only for half-duplex
operation"* -- neither of which our 8 and 6 satisfy, nor does Intel's own
suggested value. A sentence citing 802.3 for figures that contradict the
802.3 relation it does not mention is the rule-12 failure exactly:
prose about our own constant, competing with the constant, and losing
while still being believed.

## RCTL, TCTL and the descriptors: every bit confirmed, no code defect

Completed 2026-08-04. This half of the audit found **no defect in the
driver at all**, which is worth stating as plainly as the findings above:
the receive path, the transmit path and both descriptor layouts are right.

**RCTL** (82583V line 15235, fields at 15277 onward):

| Field | Bit | Ours |
|---|---|---|
| EN | 1 | `#00000002` |
| UPE | 3 | `#00000008` |
| MPE | 4 | `#00000010` |
| BAM | 15 | `#00008000` |
| SECRC | 26 | `#04000000` |

BSIZE is 17:16 and BSEX is 25; with both left zero the buffer size is
2048 bytes (line 15363), which is what the chapter claims and what
`e1000-buf-size` is.

**TCTL** (line 16324): EN bit 1, PSP bit 3, CT bits 11:4, COLD bits
21:12. Our `#000000F0` puts 15 in CT and `#00040000` puts 64 in COLD,
both correct, and the datasheet independently recommends 15: *"it should
be set to a value of 15 in order to comply with the IEEE specification
requiring a total of 16 attempts"* (line 16365).

**Legacy Rx descriptor** (line 7205, layout figure at 7246): buffer
address 63:0 at offset 0, Length 16-bit at offset 8, Packet Checksum at
10, Status at 12, Errors at 13, VLAN Tag at 14. Status bits DD 0 and
EOP 1 (line 7282). All four match. The datasheet also confirms the
recycle idiom the driver uses: *"Software can determine buffer usage by
setting the status byte to zero before making the descriptor available to
hardware"* (line 7287).

**Legacy Tx descriptor and command byte** (line 8604, Table 39 at 8684):
EOP bit 0, IFCS bit 1, RS bit 3, DEXT bit 5 which must be 0b for legacy.
Ours are `#01`, `#02`, `#08` and DEXT left clear. CSO is eight bits at
offset 16 of the third word, which the driver leaves zero, as its prose
says. And the chapter's claim about RS is exactly right, in the
datasheet's own words: *"Hardware only sets the DD bit for descriptors
with RS set"* (line 8717).

### The one thing wrong here is prose again

> CT is the collision threshold and COLD the collision distance; the
> values below are the **802.3 full-duplex figures**, 15 and 64, at bit
> positions 4 and 12.

The bit positions and values are right and the label is wrong. CT *"only
has meaning while in half-duplex operation"* (line 16368), so calling it a
full-duplex figure inverts the one thing the datasheet says about when it
applies. COLD is the field that does apply in full duplex: *"Hardware
checks and pads to this value plus one byte even in full-duplex
operation"* (line 16376).

That is the **third** prose block in this chapter to state something the
datasheet contradicts, after TIPG's "copper 802.3 values" and ASDE's
"settle speed and duplex from the PHY's own auto-negotiation". All three
sit directly above constants that are correct. The pattern is worth naming
for rule 12: the prose is not failing because the code is wrong, it is
failing because nothing ever re-read it against the source, and a
plausible sentence beside a correct constant is the hardest kind to doubt.

## What is still uncited

Named so the next reader does not mistake this file's coverage for
completeness. The register audit is now complete; these are what remain.

- **CTRL.PHY_RST**, which the 82583V names at line 2418 as the MAC-side
  way to reset the internal PHY. We reset the PHY through MDIO BMCR bit 15
  instead. Both appear legitimate; the bit position is not pinned here and
  which one a PCH-integrated part prefers is unknown.
- **Whether COLD should be 512 rather than 64 at gigabit.** The datasheet
  notes the collision window is speed dependent, *"64 bytes for 10/100 Mb/s
  and 512 bytes for 1000 Mb/s"* (line 16433), but says it about RTLC and
  late-collision detection rather than about COLD. This is a question, not
  a finding: nothing measured, and the driver runs full duplex where the
  field only pads.
- **Anything the PCH-integrated MAC does differently from a discrete
  controller.** No document in the tree can answer this class at all, and
  it is the reason every MAC-side confirmation above is corroboration by
  family rather than citation for the part.

## Status of every finding above

| # | Finding | State |
|---|---|---|
| 1 | 10 ms MDIO settle | **Arm built, driver fixed, both verified in the bed.** Still untested on metal |
| 2 | MDIO slow mode | **Arm built, driver fixed, both verified in the bed.** Still untested on metal |
| 3 | Page register | **Implemented, in the model and the driver.** Still untested on metal |
| 4 | CTRL.ASDE | **Arm built, driver fixed, both verified in the bed.** Still untested on metal |

**Finding 4 closed in the bed 2026-08-10.** It could not be exercised before
because the model had no ASDE bit and no speed fields at all: only RST and SLU
were defined, STATUS carried neither SPEED nor ASDV, and `na-line` had been
printing both off a register nothing ever wrote. Both read 10 Mb/s on every arm
that has ever run, which is an instrument that cannot fail (L-FALSIF). The
finding was unreachable because the bed could not express it.

`-e1000-asde` gives STATUS its SPEED and ASDV fields and makes CTRL.ASDE choose
which source SPEED comes from, per 12349 and 12640. `e1000-link-up` now clears
the bit. Measured with `codex/test/e1000-asde-speed`:

- **Five arms green**: ASDE set reads SPEED=10, ASDE clear reads 1000, the arms
  differ, the driver clears the bit, and the driver reads 1000.
- **Control** (arm off, same binary): three rows collapse -- `asde clear speed`
  1000 to 10, `arms differ` yes to no, `driver gets 1000` yes to no. So the arm
  is doing the work.
- **Sabotage** (driver sets ASDE again, arm on): exactly the two driver rows
  flip and the two bed rows do not move.
- All fourteen existing tests reaching this driver still pass.

**What this does NOT establish, and it is the same caution finding 1 carries.**
Nothing here says the ASUS wedges for this reason. The datasheet says the bit
"must be set to 0b" and does not say what a part does when software disobeys,
so the model implements only the behaviour the sentence describes and invents
no failure. A bed that hung here would be manufacturing a cause. Whether metal
ever cared is a question only a flight answers, and `AsdeStageProbe` now runs
the ASDE=0 arm first so that flight can tell the reset from the bit.

**Findings 2 and 3 closed together on 2026-08-04**, because slow mode
cannot be set without the page register. `-e1000-mdio-slow` gates MDIO
reads, `e1000-phy-set-page` and `e1000-phy-slow-mode` are in the driver,
and `codex/test/e1000-mdio-slow` runs under the arm. Measured:

- **Five arms green.**
- **Control** (device present, arm removed): exactly one row moves,
  `read before slow` yes to no.
- **Sabotage A** (slow mode never set in bring-up): `read after setup`
  flips, alone.
- **Sabotage B** (slow mode set BEFORE the PHY reset instead of after):
  `read after setup` flips, alone. That is the ordering trap, and it is
  why the model clears paged state on a PHY reset -- without it the arm
  would test that a write happened rather than that it held.
- All eleven tests reaching this driver pass.

**Two things in that arm are ours and not the datasheet's**, and they are
written at the flag rather than left for a reader to discover. Reads are
gated while the page register and 769.16 are exempt, because 9.2 read
strictly forbids the very writes that would satisfy it and a literal arm
makes the requirement unsatisfiable. And the failure shape is E rather
than a floating-bus 0xFFFF, because E cannot be mistaken for a legitimate
value. **The arm therefore tests that the driver sets slow mode, which is
the citable part. It does not reproduce the electrical failure and does
not claim to.**

**Finding 1 is different now, and precisely this much.** `-e1000-mdio-window`
is in codex-vm, `e1000-reset` waits the window out, and
`codex/test/e1000-mdio-window` runs under the arm together with
`-e1000-phy-link`. What was measured, 2026-08-04:

- **Four arms green** with the arm on and the settle in place.
- **Control** (identical binary, window arm removed): exactly one row moves,
  `unsettled errors` yes to no. So the window is doing the work and the
  reading is not passing for some other reason.
- **Sabotage** (settle removed from `e1000-reset`, arm on): exactly the three
  predicted rows move, and the last of them reads `link under gates : no`.
  **That is the metal symptom reproduced on the desk** -- a link that never
  comes up while every MAC register reads exactly as it should.
- All nine existing tests that reach this driver still pass, which is what
  the arm being off by default is for.

What this does NOT establish, and the distinction matters: that the I219 on
Damian's ASUS was failing for this reason. The bed now models the spec's
requirement and the driver now meets it. Whether the metal ever violated it
is a question only a flight answers.

## Consequences for the bed

`tools/codex-vm.c`'s model can now be given arms that are derived from a
citation rather than from our driver. The two worth having, in order:

1. **An MDIO window arm.** Built: `-e1000-mdio-window`, see the status
   section above.
2. **A slow-mode arm.** Not built. Refuse MDIO reads while page 769
   register 16 bit 10 is clear. Model the cable-disconnect case
   specifically, since that is what the datasheet ties the requirement to.
   Findings 2 and 3 go together, because setting the bit needs the page
   register first.

Both must be **off by default** (L-FALLBACK): every existing green was
measured against a model with neither, and moving the floor in the same
change that introduces the replacement is the failure that rule names.
They add arms.
