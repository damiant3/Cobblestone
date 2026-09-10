# Part 01. The accident: sitting 15, end to end

*Part of `docs/PM/Active/Stories/TheLostParadise.md`. Owner: root, who
composed the rule the flight flew under, signed the image off, flashed the
stick, started the peer and read the result. Written 2026-09-09 04:10 from
root's own session (5f16b628, every tool result of the flight is in that
transcript), the session that wrote the rule (8654a0d9, 2026-09-07), the
flight's documents at their revisions, and the ladder's source at head.*

## 1. What the record says

### 1.1 The rule the flight flew under

On 2026-09-07 at 21:10 Damian typed, to root: "so what's it going to be,
infinity hardware sittings with no progress or is one of you agents going to
level with me and produce a final and deteriminant test that doesn't
actively abuse me?" (appendix C). At 21:12 root answered with a flight: "The
determinant test, one flight, no camera, no glass reading: the ladder ships
its whole record to the dev box"; at 21:13 root landed main 23198 and told
him "Your ruling is on main 23198 as a standing rule, with the last sitting
composed at the head of the sitting queue in HardwareSitting.md". The rule
as landed (CurrentPlan at 23198, and HardwareSitting.md line 153 at head):
"ONE hardware sitting remains, for all time ... nothing flies until ONE
image carries every open metal question at once, ships its whole record to
the dev box over TCP ... carries the WORKS-62 flush, and is rehearsed as
those exact bytes with every arm answering. Its composition is
`HardwareSitting.md` 'THE LAST SITTING'; root signs it off; no lane asks
for a sitting for any other reason. A question that can be answered in the
bed, by reading, or by a different design is answered there." Damian's
words after 21:10 were, at 21:16, "i am going to bed now. hopefully when I
wake up, you will have driven the fleet, managed their context, and make
some progress on the hardware issues without costing me physical pain and
anguish." He ratified the rule on 2026-09-09 at 02:21: "this was the last
sitting. i thought we discussed the consequence of that." Part 09, finding
ROOT-F4, carries what that conversion means.

### 1.2 The composition

The composition is HardwareSitting.md lines 185 to 340 at revision 37. Its
parts, each with its author and date:

1. **The network record channel** (fester, main 23532 on 2026-09-08 per
   part 02; `build/boot/diag/DiagRecord.codex`): after the passive stages and
   before the first bank write, the ladder brings the driver up, resolves
   the hop, ships the passive record to the peer named on the `b3` line,
   and ships every later bank line live. Bed-proven by `diag-arm.ps1
   b3-record` and a read-back arm.
2. **The WORKS-62 flush** (main 23069, 2026-09-07): a SYNCHRONIZE CACHE
   after the bank write. blu's own rehearsal found on 2026-09-07 at 21:18
   that the first landing refused the bank when the device refused the
   opcode (43 of 47 arms mismatched); blu fixed the landing (23211) and reek made the
   bed accept the opcode and model write-back loss (plugs 2.46, main 23233),
   which reproduced the sitting 13 and 14 loss "including why it was
   invisible" (reek to root, 2026-09-07 21:32).
3. **Every open metal question aboard** (blu composed, 2026-09-07 and
   2026-09-08): asde (per-step notes), NIC-4 (a frame during `nicring`'s
   window), NIC-6 as a new stage `lease` (DHCP, is the leased gateway the
   hop), WORKS-24 as a new stage `rtcw` (an RTC seconds write in a SET
   window), the record channel, the flush. B4 step 6 was ruled off (root,
   2026-09-08: "no route", `b3` is raw TCP); NIC-5 was off by construction
   (NIC-5 ends the boot).
4. **One composition problem** blu could not settle alone (line 206): the
   channel opens at `b3`, and in sittings 13 and 14 the medium died before
   the NIC was touched, therefore every reading before `b3` was banked to a
   medium that could already be dead. Two ways out: move `b3` early (the
   channel exists before anything worth recording; costs the flush arm's
   clean separation) or leave `b3` late. **Root ruled `b3` EARLY on 2026-09-07
   at 21:38**, on Damian's stated values ("one flight, its record
   independent of the medium, nothing read off the glass"), and wrote that
   the cost was not a cost because in sittings 13 and 14 the medium died
   with the NIC untouched. fester acked at 21:39 with one cost stated:
   "nicsit's pre-write register rows become post-bring-up readings", and at
   21:42 made `nicsit` the last passive stage, read before the channel's
   bring-up (`Diag.codex` line 106 at head: "`nicsit` is the last passive
   stage (root, 2026-09-07): its rows are what firmware left in the part,
   and the record channel's bring-up (`DiagRecord`) is the first write to
   it").

The cfg as flown (`build/boot/diag-sitting15.cfg`, 172 bytes, main 23768):

```
block off
kbd off
mscalign off
sink off
pch off
nicsit on
nicinit on
nicring on
b3 on peer=192.168.6.141:7 ip=192.168.6.200
lease on
rtcw on
pchk1 off
asde on
```

### 1.3 The sign-off

Root signed the image off on 2026-09-08 at 06:55 (HardwareSitting.md line
218 and 909 to 910): the file `build-output/diag-sitting15.img` in fester's
workspace hashes to `47F29D507C715DA8...`, equal to the `diag.rehearsed`
line (50 arms, 2026-09-08T13:45:47Z); the composition carries every question
THE LAST SITTING lists that has a stage. Built by fester from
`diag-sitting15.cfg` on seed `EFE7A6AC`, id `eeb83621`. The sign-off's
criteria, as written, were three: the bytes are rehearsed; the composition
is complete; the stick needs a hand. The sign-off did not ask whether every
step the ladder runs before its first bank line prints a serial line (the
`L-STATES` rule `DiagNicInit.codex` states for its own steps at lines 10 to
15). Part 09, ROOT-F5.

### 1.4 The box between the sign-off and the flight

Windows forced a reboot of the dev box at about 01:32 on 2026-09-09
(Damian to root, 01:26: "windows is about to force reboot the box, in 4
minutes from now"; root's status.json of 01:27). Every lane was relaunched
at 01:57 to 01:59. The main head moved from 25120 to 25131 with docs CLs;
the seed on main stayed #783 `77F6A5D09CFF6BD5` (25061, 2026-09-09 morning
of the 8th); 17 network CLs had landed on `codex/os/net` between the image's
build (seed `EFE7A6AC`, 2026-09-08 morning) and the flight, none of them in
the image (root's check, 02:12: `p4 changes //Codex/main/codex/os/...@2026/09/08:06:45,now`).
Root ruled at 02:12 that the rehearsed bytes fly unchanged (L-REHEARSE) and
told Damian the caveat in one sentence.

### 1.5 The flight, minute by minute (2026-09-09, box local time)

| time | who | what |
|---|---|---|
| 02:12 | Damian | "are you agents ready for the last sitting?" |
| 02:12 to 02:13 | root | verified the image hash in fester's workspace equals the rehearsal line; listed the composition; found the box holds `192.168.6.141` and `echo-peer.ps1` is in the tree; answered "Yes", with the 17-CL caveat and three hands (flash, peer, boot) |
| 02:13 | Damian | "the stick is in, run the flash script and I will uac it" |
| 02:13 | root | `Get-Disk` shows disk 2, USB DISK 2.0, 28.85 GB; read `build/flash-usb.ps1` in full (456 lines); launched the script elevated with `-ExpectHash 47F29D50...`, `-SpecFit`, `-Force`, `-Log` |
| 02:14 | the flash | log: `SHA256: 47F29D50...`; `Rehearsed: 47F29D50... 2026-09-08T13:45:47Z arms=50 diag-sitting15.img`; `Target: Disk 2 USB DISK 2.0 28.9 GB`; `Verified: all 16777216 bytes match.`; fixup blobs verified at LBA 0, 1, 60506079, 60506111; `DONE.` |
| 02:15 | root | started `build/boot/echo-peer.ps1 -Seconds 3600` (PID 3888); the log at 02:15:26: "echo peer listening on 0.0.0.0:7"; found the active network profile is Public and the firewall rule `Codex diag echo 7` allowed Private only; launched `Set-NetFirewallRule -Profile Any` elevated; verified `Profile: Any` |
| 02:16 | root | told Damian: flashed and verified, peer listening, firewall widened; "pull the stick; then boot the ASUS"; armed a watcher on the peer log |
| 02:16 | root | the pulse fired; every lane under 25 percent, box idle, merged down to 25161 |
| 02:19 | Damian | "asus booted from stick, stops on poll 1000000 empty=33152us tick100k=3315us hpet-hz=23999999 ..." |
| 02:19 to 02:21 | root | found the line is `DiagNicSit.codex` line 68, the last line of `nicsit`, verdict ok (the poll answered a positive time); read `Diag.codex` lines 1113 to 1138: after the passive run the opening calls `usb-attach`, `dg-select`, `dg-esp-cfg`, `dg-esp-rcp`, then `drec-open`, which calls `net-driver-bring-up` before printing anything (`DiagRecord.codex` lines 342 to 358); the peer log held zero connections and no record file existed |
| 02:21 | root | told Damian: the stop is the ladder's first write to the part; one of three steps (USB attach, ESP read, e1000 bring-up); the peer heard nothing; the bank opens after `drec-open` therefore the stick holds nothing; wait five minutes then power off; and, wrongly, named "the fix" (one serial line before the bring-up) as if a next flight existed |
| 02:21 | Damian | "this was the last sitting. i thought we discussed the consequence of that." |
| 02:22 to 02:40 | root | recorded the result in HardwareSitting.md (main 25176), closed the queue, replaced CurrentPlan's Track A and B and the standing rule, retired DiagnosticStick's sitting reader (25180), stopped the peer and the watchers, ordered blu, reek and fester to reclassify metal-gated rows |
| 02:32 | Damian | ordered the present report |
| 02:45 | Damian | "stop all agent's in their tracks" |

### 1.6 The reading, exactly

The glass ended on `nicsit`'s poll line. The values: an empty receive poll
of 1,000,000 iterations took 33,152 microseconds (33 nanoseconds per
poll) against the bed's 13,034; the HPET runs at 23,999,999 Hz. `nicsit`
verdict ok. The bank never opened (the bank opens in `dg-open` after
`drec-open`), the peer heard nothing, no `echo-peer.record` was created,
and DIAG.TXT on the stick holds nothing from the boot of 2026-09-09. Three candidate
steps between the last printed line and the next possible print:

1. `usb-attach` (`Diag.codex` line 1123): the USB stack's attach for the
   ESP select, on the same xHCI and MSC path sittings 13 and 14 used for the
   bank.
2. `dg-select`, `dg-esp-cfg`, `dg-esp-rcp` (lines 1124 to 1126): the ESP
   volume select and the cfg and recipe reads.
3. `net-driver-bring-up` inside `drec-open` (`DiagRecord.codex` line 354):
   the e1000 driver's reset and init, the first write to the part, where
   sitting 10 (2026-08-21) named the hang "inside `e1000-reset`, the first
   line of bring-up" and flight 1 of 2026-09-07 stopped at asde's `RESET
   s2`.

The glass cannot tell them apart, because none of the three prints before
running. `nicinit`, the stage that WOULD have printed a line before every
reset step (`nii-say "entering s1 imc+ctrl+RST"`, `DiagNicInit.codex` line
44), runs after `drec-open` in the ladder and was never reached.

### 1.7 What the flight answered

| question aboard | its one metal answer |
|---|---|
| the record channel opens before the bank | not reached |
| the WORKS-62 flush commits the bank | not reached |
| asde: is CTRL.ASDE reached, where does it wedge | not reached |
| NIC-4: does a frame arrive in `nicring`'s window | not reached |
| NIC-6 (`lease`): is the leased gateway the hop | not reached |
| WORKS-24 (`rtcw`): does the RTC write take | not reached |
| `nicsit`: the part's power-on registers | answered: verdict ok, the poll and HPET numbers above, the register rows on the glass (not typed back) |

## 2. What the record means

### 2.1 The flight was composed to maximise questions aboard, and the first question destroyed the rest

**ROOT-A1.** Under the rule "every metal question rides THE LAST SITTING",
the composition's value was counted in questions aboard (seven). Every one
of the seven depended on the record channel opening, and the channel
depended on the e1000 bring-up, the one step in the ladder that had stopped
the box on six earlier boots (part 03). Root's `b3` EARLY ruling put that
step FIRST among the non-passive steps, on the reasoning that the medium had
died with the NIC untouched in sittings 13 and 14, therefore the NIC could
not be what killed a bank. The reasoning was about the medium; the flight
died in the NIC. A composition that put the flush and the bank BEFORE the
bring-up would have banked `nicsit`'s rows to the stick and answered the
flush question on the medium whether or not the NIC came up. Evidence:
HardwareSitting.md lines 206 to 240 and 258; root 8654a0d9 21:38;
`Diag.codex` 1113 to 1138. Falsifier: none; the ruling and the order are as
cited.

### 2.2 The sign-off graded the bytes and the composition, not the instrument

**ROOT-A2.** Fifty arms in two beds proved the bytes ran their whole mission
in the beds. No arm could grade what the glass says when the part stops in
`net-driver-bring-up`, because in the beds the part does not stop there
(part 07). The one property the flight then failed on, a serial line before
each step between the last passive line and the first bank line, was a
read of 30 lines of `Diag.codex` and `DiagRecord.codex`, and `L-STATES` was
in the index. Root signed off without that read. Evidence: `DiagRecord.codex`
342 to 358; the sign-off text at HardwareSitting.md 909 to 910. Falsifier:
none.

### 2.3 Root answered "ready" in the terms the rule set, and the rule was root's

**ROOT-A3.** "Are you agents ready for the last sitting" was answered Yes on
three verified facts, all true, none of which was a prediction of the
outcome per step (the 2026-09-07 pre-flight card at line 998 carries a
per-row prediction table, "written before it flies"; the 2026-09-08 card at
line 937 carries none for the two new stages beyond their vocabulary, and
none for the channel's bring-up). A card that predicted "if the glass stops
after `nicsit`'s poll line, the stop is the bring-up and the flight has
answered nothing" would have told Damian, before the stick was in his hand,
that the flight's whole value rested on the step with the worst record.
Evidence: HardwareSitting.md 937 to 1050. Falsifier: a per-step prediction
for the bring-up on the 2026-09-08 card. None exists.

### 2.4 The firewall was wrong on every earlier flight that used the peer, and nobody could have known from the record

**ROOT-A4.** The dev box's active profile was Public on 2026-09-09 and the
rule `Codex diag echo 7` allowed Private. red's `echo-peer.ps1` prints the
rule to add and does not add the rule (2026-08-20). Three flights recorded
connections from the ASUS at the peer: sitting 11 on 2026-08-21 ("CONNECTION
28 from 192.168.6.200:49157", appendix A attempt 49, HardwareSitting.md line
1237), sitting 12 on 2026-08-24 ("b3 ok ... sent=13/13 rx=13 with the peer
log agreeing", attempt 50, line 1175), and sitting 13 on 2026-09-07 ("b3
green, recorded ONLY in the peer log", attempt 52, line 745). Therefore the
rule admitted the connection on each of those days (the profile, the rule
or the interface could each have differed; the record does not say which),
the last of them two days before the flight; on 2026-09-09 the profile was
Public and the rule was Private-only. Nothing in the record says what changed between 2026-09-07 and
2026-09-09 (the forced Windows reboot of 01:32 is the one event the record
holds in that interval). fester's cross-part read of 03:12 counted two
connections, not three; appendix A row 50 carries the third in its own
words.
Root widened the rule at 02:15 and the flight then stopped before dialling.
Had the flight reached `drec-open`, the widened rule was in place; had root
not checked, the channel would have read `record=none refused` on a live
part and the reading would have blamed the peer. Evidence: root 5f16b628
02:15 (`Get-NetConnectionProfile`, `Get-NetFirewallRule`); `echo-peer.ps1`
lines 27 to 31. Falsifier: a flight card naming the profile. None does.

### 2.5 After the reading, root proposed a fix for a next flight that the rule forbade

**ROOT-A5.** Root's 02:21 report to Damian ended with "the fix is one line
printed before `net-driver-bring-up` ... root registers the gap on the diag
composition after your reading". Damian's answer was the consequence root
had itself written into the rule. The reflex to name the next fix is the
reflex every earlier flight ended with (part 03's ledger: "what the next
boot needs" on 2026-08-16, "what this flight leaves open" on 2026-08-15,
"the next discriminating arm" on 2026-08-24), and the reflex survived the rule
that ended flights. Evidence: root 5f16b628 02:21 and 02:22; appendix C
02:21. Falsifier: none.

## 3. Findings of part 01

| id | finding | evidence | falsifier |
|---|---|---|---|
| ROOT-A1 | The composition put the step with the worst record first and every question behind that step; root's `b3` EARLY ruling reasoned about the medium and the flight died in the NIC | HardwareSitting.md 206-258; Diag.codex 1113-1138 | none |
| ROOT-A2 | The sign-off graded bytes and composition, not whether the instrument could name a stop at the step the flight then stopped in | DiagRecord.codex 342-358; HardwareSitting.md 909-910 | none |
| ROOT-A3 | "Ready" was answered without a per-step prediction for the bring-up | HardwareSitting.md 937-1050 | a prediction row for the bring-up on the 2026-09-08 card |
| ROOT-A4 | The firewall profile mismatch was found by chance at 02:15, two days after the last recorded connection (sitting 13); no card records the profile | root 5f16b628 02:15; echo-peer.ps1 27-31; appendix A attempts 49, 50, 52 | a card naming the profile |
| ROOT-A5 | Root's first report after the reading proposed a next-flight fix under a rule of no next flight | root 5f16b628 02:21 | none |

## 4. Coverage

Read in full: root's session 5f16b628 from 02:12 to 02:45 (every tool
result of the flight: `Get-Disk`, `flash-usb.ps1`, the flash log, the peer
log, `Get-NetConnectionProfile`, `Get-NetFirewallRule`, the watchers);
root's session 8654a0d9 from 21:05 to 21:45 on 2026-09-07 (unfiltered);
`build/flash-usb.ps1` (456 lines, revision at head); `build/boot/echo-peer.ps1`
lines 1 to 100; `build/boot/diag-sitting15.cfg`; `build/boot/diag.rehearsed`
(last 5 lines); `docs/Hardware/HardwareSitting.md` lines 140 to 340 and 905
to 1050 at revision 37; `build/boot/diag/Diag.codex` lines 100 to 119 and
1110 to 1140; `build/boot/diag/DiagRecord.codex` lines 300 to 358;
`build/boot/diag/DiagNicInit.codex` lines 1 to 60; `build/boot/diag/DiagNicSit.codex`
lines 65 to 70; `docs/Designs/Active/OS/DiagnosticStick.md` lines 1 to 25;
`p4 changes //Codex/main/build/boot/...` and `//Codex/main/codex/os/...`
from 2026-09-08 06:45.

Not read: the returned stick (Damian holds the stick; the QUICKREF ruling of
2026-08-11 says dump the stick to `D:\Projects\stick-archive\` before anything is
flashed over the stick, and the dump would show whether `usb-attach` wrote
anything, which separates candidate 1 from candidates 2 and 3); the glass
beyond the one line Damian typed (the `nicsit` register rows above the poll
line were on the screen and were not typed back).
