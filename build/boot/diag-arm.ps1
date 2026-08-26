# Rehearse the diagnostic stick: boot the EXACT image that would fly, in
# codex-vm and under OVMF, and require every channel to agree
# (docs/Designs/Active/OS/DiagnosticStick.md, "The bank path has a permanent
# runner"). Every arm here is a control as much as a check: the ones that must
# reach `bank=none` are what show the bank rows can say no.
#
#   build/boot/diag-arm.ps1                 # every arm
#   build/boot/diag-arm.ps1 -Only pass      # one arm
#   build/boot/diag-arm.ps1 -SkipOvmf       # codex-vm arms only (no QEMU)
#   build/boot/diag-arm.ps1 -Keep           # leave the working images
#
# Arms, and what each requires:
#
#   pass       codex-vm, image as boot medium and USB disk. Serial block from
#              `DIAG1` to `END` == DIAG.TXT read back off the disk image, row
#              for row; bank=ok; both stages reach a state; ends in END.
#   no-medium  codex-vm with no -disk at all: no USB mass-storage device, IDE
#              absent, so nothing carries an ESP. Ladder must still reach the
#              summary and say bank=none naming the mount stage.
#              (-usb-bot-drop 1 was tried first for this and is NOT a no-bank
#              arm any more: the MSC driver's recovery path re-issues the
#              transfer and the bank lands. Measured 2026-08-18.)
#   no-smbios  codex-vm -no-smbios: the ConfigurationTable carries no SMBIOS
#              entry; smbios=no-table and the box row says unnamed.
#   no-edid    codex-vm -no-edid: LocateProtocol finds no EDID; edid=absent.
#   edid-bad   codex-vm -edid-bad: the EDID checksum is wrong; edid=bad-checksum.
#   sink-ladder codex-vm with `sink ladder=1`: seven rungs of 1..64 sectors
#              against a 64 KB payload, and every one completes here because
#              this bed completes a transfer at any size. sink=ladder-all
#              done=7. The arm that shows the ladder RUNS.
#   sink-ladder-32 the same with -usb-bot-drop-len 16384 -usb-bot-drop-len-max
#              16384: the bed refuses exactly the 32-sector command, so the
#              ladder must answer rung=32 done=5.
#   sink-ladder-16 the same with the bed threshold at 8192: rung=16 done=4.
#              THE PAIR IS THE POINT. One arm cannot tell a ladder that
#              measures from a ladder that always stops in the same place;
#              two arms whose answers MOVE with the bed can. Both assert the
#              exact rung, not "a rung was named" -- an earlier version
#              asserted only that a rung appeared and passed on an
#              ordinal-keyed drop that was stopping rung 1 for an unrelated
#              reason (2026-08-20).
#
#   ON KEYING A DROP BY ORDINAL, which is what `sink-drop` below still does.
#   `-usb-bot-drop N` counts transfer events since boot, so it is a property
#   of the whole run and not of the thing under test. Inserting the xhci
#   stage at 8 moved this arm's drop out of the sink's DATA phase into its
#   MOUNT, and the arm reported mount-fail: a plausible word, not a nonsense
#   one, which is exactly what makes it dangerous. `-usb-bot-drop-len` keys
#   on the command's own dCBWDataTransferLength instead and cannot be moved
#   by anything upstream, which is why the ladder arms use it.
#   THE ARM'S INTENT IS "THE DROP LANDS IN THE SINK'S DATA PHASE". The number
#   is only how that intent is currently expressed, and it is the intent that
#   must survive a stage being added, not the number. RE-DERIVE IT BY
#   MEASUREMENT whenever the stage list changes, which is due again when ASDE
#   lands as stage 14. The method, so nobody has to guess: sweep
#   `-usb-bot-drop N` over the arm and read `stage=sink state=` for each N.
#   The band where it says `write-refused` with `wr=0` is the data phase;
#   below it the drop falls in the mount (`mount-fail`) and above it in the
#   readback (`read-fail`, with `wr=5364` because the write completed).
#   Measured 2026-08-20 on main plus the ladder: 500, 520, 560, 620, 700 and
#   740 all refuse the write; 770 and 800 read back. **Take the MIDDLE of the
#   band rather than an edge** -- 620 here -- because an edge is one inserted
#   stage away from meaning something else.
#   The middle rule then paid for itself the same day and that is why it is
#   written down rather than asserted: `gopmode` went in as stage 6 and the
#   list went to 13 stages between the sweep and the submit, and 620 still
#   lands in the data phase (all four sink arms green on the new list). An
#   edge of the band would not have.
#   A NUMBER AT THE EDGE OF A WORKING BAND IS A NUMBER THAT BREAKS ON THE
#   NEXT STAGE INSERT. That is the transferable part, and it is why the middle
#   is the answer rather than any N that happens to pass today.
#   RE-DERIVED 2026-08-21 FOR DEFER-SINK-TO-LAST, and this is the re-derivation
#   the paragraph above was waiting for. Sink now EXECUTES after every other
#   stage (its number, its cfg key and its glass row are unchanged), so every
#   stage that moved ahead of it pushed the data phase up the ordinal. Swept on
#   the deferred image: 800 lands in the mount, 830, 860, 890, 900, 1000, 1010,
#   1040 and 1070 all refuse the write, 1100 reads back. Band 830..1070, so the
#   middle is 950 and BOTH sink arms take it. The old 500 was not merely stale:
#   at 500 the blanket drop wedged nicinit and `bank-lost` still went green,
#   because that arm only ever read the bank row and a wedge anywhere produces
#   one. It asserted the stages after sink from 2026-08-21, which is the thing
#   the deferral exists to protect and the thing no arm was measuring.
#
#   THE TWO READINGS THAT DISAGREED ARE RECONCILED, BY MEASUREMENT (2026-08-20).
#   500 was reported reading `mount-fail` on a 13-stage ladder, and read
#   `write-refused` here on what should have been the same shape. Two
#   measurements of one configuration disagreeing is a finding, so it was
#   tested rather than argued: the pre-ladder `DiagSink.codex` was checked out
#   over the current tree, the image rebuilt, and the same numbers swept.
#
#       payload            band (write-refused)   at N=500      at N=620
#       pre-ladder sink     520 .. 800            mount-fail    write-refused
#       ladder sink         500 .. 740            write-refused write-refused
#
#   So BOTH readings were correct for the tree they were taken on and neither
#   was noise. **620 is inside the intersection of both bands (520..740)**, so
#   it holds on either shape, which is the property the middle was chosen for.
#
#   AND READ THE FIRST ROW AGAIN, because it says something worse than drift.
#   The floor was then measured rather than described: on the pre-ladder shape
#   501, 505, 510 and 515 all read `mount-fail` and 519 reads `write-refused`,
#   so the floor is in 516..519 and the shipped value of 500 sat SIXTEEN TO
#   NINETEEN EVENTS BELOW IT. Not on the edge, not one event either way: out
#   of band, with no margin to lose in the first place.
#   So the arm was not knocked out of calibration by a stage insert. It was
#   already miscalibrated for that shape, and the only reason it ever passed
#   is that the shape it had been calibrated against had a different band.
#   "500 broke when a stage was inserted" reads as bad luck; "500 was sixteen
#   events out of band" is a defect that was there waiting for anyone to
#   change the payload.
#   THE RULE, and how it got better, because the order is the useful part:
#   red proposed TAKE THE MIDDLE OF THE BAND, which is what sent me to sweep;
#   sweeping both shapes then showed the stronger form, that 620 holds because
#   it is in the INTERSECTION of the two measured bands (520..740) rather than
#   the centre of either. So: WHEN A VALUE MUST SURVIVE A CONFIGURATION
#   CHANGE, PUT IT IN THE INTERSECTION OF THE BANDS YOU HAVE MEASURED, not the
#   middle of the one in front of you. The middle rule is what made the
#   measurement happen; the intersection is what the measurement showed.
#   Both descriptions this paragraph replaced ("on the edge", "one event
#   either way") were ADJECTIVES STANDING IN FOR A NUMBER, and the number
#   changed the story both times: two people with the instrument in reach
#   spent three messages refining a word, and four runs settled it. Sweep the
#   boundary; do not characterise it. Recorded as L-ADJECTIVE.
#   The bands differ at BOTH ends, so this is a shift in the whole event
#   count and not just a stage appearing: the payload is itself read over the
#   same bulk endpoint at boot, so a change in its SIZE moves every ordinal
#   after it. That last sentence is the mechanism and is inferred, not
#   measured; the table above is measured.
#   IT CANNOT RESCUE `sink-drop`, and that is measured rather than assumed:
#   the 2.7 MB stage writes 32,768-byte commands and the diagnostic bank
#   writes 32,768-byte commands too, so no size threshold separates them and
#   a lever set to catch the sink kills the bank first (the row then reads
#   no-medium). So `sink-drop` stays ordinal-keyed and stays fragile, and
#   this paragraph is the warning rather than a CL nobody re-reads: if it
#   starts reporting mount-fail, something upstream moved and the number
#   needs re-deriving, NOT tuning until it goes green.
#   sink-drop  codex-vm -usb-bot-drop 500 -usb-bot-drops 4: four consecutive
#              transfer events swallowed inside the 2.7 MB data run, which is
#              past what the single retry recovers. sink=write-refused, and the
#              row carries wr=0 cc=256 lba=3574 rty=2 ph=1 after=0 -- the
#              medium refuses a SMALL write once the big one is refused. The
#              bank rewrite AFTER the stage still lands and DIAG.TXT comes back
#              whole, so this bed does NOT reproduce the metal bank death: the
#              wedge here is transient and the ASUS's was not.
#              ONE drop is not this arm: it recovers (rty=3) and the run
#              completes, and the index decides the state (300 recovers, 500
#              refuses, 1000 writes and fails the readback).
#              THE INDEX HAS TO FOLLOW THE LADDER, and the header says 500
#              above because that is what it was: it is 620 now (reek's sweep,
#              main 18057/18059/18061). WHY it moved is the part worth
#              keeping. dg-run-rest calls dg-bank-write after EVERY
#              dg-run-one INCLUDING A SKIPPED ONE, so every stage added ahead
#              of sink adds a bank rewrite and its transfers whether that
#              stage runs or not. gopmode at 6 and xhci at 8 added two, the
#              500th event moved out of the 2.7 MB data run and into sink's
#              MOUNT, and the arm read sink=mount-fail.
#              **NO CONFIG TAKES THOSE TRANSFERS AWAY.** Turning the added
#              stage off is the first thing anyone will try and it does
#              nothing, because a skipped stage still banks -- measured with
#              xhci off, which failed identically.
#              THE NUMBER IS CONFIRMED FROM TWO TREES IN OPPOSITE
#              DIRECTIONS: reek swept to 620, and blu independently measured
#              560, 600 and 650 all restoring write-refused on a 14-stage
#              tree, which brackets it. A number two people reached from
#              different directions is worth more than either sweep alone.
#   fat-full   codex-vm with every free cluster marked bad on the disk copy:
#              the mount and the DIAG.ID lock succeed and the WRITE is refused.
#              bank=none naming the write stage; no DIAG.TXT. (A read-only host
#              file does NOT force this: codex-vm serves the guest from memory
#              and only the flush fails, disk-arm.ps1 learned that.)
#   cfg-off    a second image built with `scene off` in the stub ring: the
#              scene stage reports skipped and the ladder banks the rest.
#   esp-cfg    a second image carrying DIAG.CFG on the ESP: the payload reads
#              it after the bank opens and reports cfg-file=1.
#   block-oob  a second image whose DIAG.CFG says block lba=999999999: the
#              block stage's one-sector write is aimed past the medium and the
#              device refuses it; block=write-refused, every other stage as
#              in pass. The forced-failure arm of the write-side stage.
#   sink-shift a second image whose DIAG.CFG says sink shift=1: the 2.7 MB
#              write lands and the streaming verify compares against a pattern
#              shifted by one, so every byte is reported bad; sink=bad-bytes,
#              block and the rest as in pass. The oracle proving it can say no.
#   nic-pass   codex-vm -e1000 -e1000-nat: the bed has NO Intel card unless asked, so every
#              other arm reads nicsit=no-part (dim, honest); with the card the
#              stage reads it (verdict ok, registers, the poll calibration) and
#              says ok; nicinit ok; the NAT answers the ring stage's ARP so it
#              reads frames. The card's own fault switches are the arms for the
#              init and ring stages.
#   nic-noread a variant carrying `nicring listen=0`, run with -e1000 -e1000-nat:
#              the ring stage sends its ARP and then idles without polling, so
#              nothing recycles the descriptor the NAT's reply landed in and the
#              writeback survives to be read. wb above zero with buf=y. It is the
#              only arm that shows the descriptor hexdump can read a writeback at
#              all; nic-pass cannot, because a successful listen recycles the
#              descriptor and leaves wb=0 behind.
#   nic-nolink codex-vm -e1000-no-link: STATUS.LU never sets; nicinit=no-link (the
#              step durations still bank), nicring=quiet, nicsit ok.
#   nic-invisible codex-vm -e1000 -e1000-nat -i219 -e1000-inject 1
#              -e1000-inject-armed: DiagNicRing's middle row, "frames arrived
#              and we cannot see them" (:124). gp above zero with rdh=0 and
#              dd=0 -- the MAC counted a frame in THIS stage's window and no
#              descriptor came back. Sitting 9 read exactly that off the ASUS.
#              Two separate things had to be fixed before it was expressible:
#              GPRC was counted AFTER the writeback so every fault read gprc=0
#              (main 18747), and the injector emptied at RCTL.EN so the frame
#              landed in nicinit and the stage read pre=1 gp=0.
#              -e1000-inject-armed holds the frames until the guest READS
#              GPRC, which is this stage's own opening action, so the arrival
#              is inside the window by construction. NOT keyed on an ordinal:
#              -e1000-inject-late 2 also works and is the -usb-bot-drop defect
#              this file warns about above, with a band one value wide.
#   nic-armed  the POSITIVE control for it: same frame, same timing, no stall,
#              so nicring=frames with rdh above zero. gp comes back above zero
#              in BOTH arms, which is the point -- gp alone separates neither,
#              and what distinguishes them is whether the ring shows the frame.
#              Without this arm, gp=1 beside an invisible frame could be a
#              model that never delivers anything.
#   nic-nomac  codex-vm -e1000-no-mac: RAL/RAH empty; nicinit=no-mac, nicsit ok.
#              nicring's own read of the same card is not asserted here (init
#              without a MAC still brings the receiver up on the model).
#   nic-nohpet codex-vm -e1000 -e1000-nat -no-hpet: the HPET window is dead, so
#              the declared tick period is zero and every reader of the rate has
#              to say so. Four of them do, and until 2026-08-19 nothing could
#              reach any of them: nicsit=no-hpet, nicinit=no-hpet,
#              nicring=no-hpet, and the scene stage -- which keeps its state
#              word -- says it in the glass instead (plain=no-clock), which this
#              arm asserts as well. The card is present and the NAT is
#              answering, so the three nic states are the clock talking and not
#              a missing part.
#   b3-pass    a variant whose DIAG.CFG names a peer, run with -e1000 -e1000-nat
#              against a real TCP listener started on the host. codex-vm's NAT
#              translates the gateway address to 127.0.0.1 when it opens a host
#              socket, so 10.0.2.2:P reaches that listener. The stage brings the
#              driver up, ARPs for the next hop, connects, sends, reads the echo
#              back and closes: b3=ok with rx equal to sent. It is the positive
#              control the other three are measured against, and without it
#              b3=no-reply and b3=refused would be indistinguishable from a
#              stage that never worked at all (L-FALSIF).
#   b3-noreply the same with a listener that accepts and then says nothing:
#              b3=no-reply. Our stack completed the handshake and the far end
#              did not answer, which is the one distinction a single state
#              word could not make.
#   b3-refused the same with NOTHING bound to the port: the NAT's own connect
#              fails, no SYN-ACK arrives, b3=refused.
#   b3-nopart  a peer named and no -e1000 at all: b3=no-part. Every other arm
#              names no peer and short circuits to no-peer before the part is
#              looked at, so this is the only arm that reaches the part check.
#   asde-differs codex-vm -e1000 -e1000-phy-link -e1000-asde: the model makes ASDE
#              actually change the resolved speed (10 with it set, 1000 with it
#              clear, as codex/test/e1000-asde-speed pins), so the two no-reset
#              arms come back DIFFERENT and the stage says differs. It is the
#              POSITIVE control and the reason it has to exist is nicring's
#              lesson: a stage whose interesting answer the bed cannot express
#              would have flown to metal with nothing having shown it can say
#              anything but same (L-FALSIF).
#   asde-ctrlro codex-vm -e1000 -e1000-ctrl-ro: CTRL writes are discarded whole,
#              so the SLU discriminator clears a set bit and reads it back still
#              set. asde=ctrl-ro, which DOMINATES the two arms above it -- if
#              nothing we wrote was written then neither arm changed anything.
#   b3-noaddr  a variant whose DIAG.CFG names a peer and NO ip=: b3=no-address.
#              The falsifier for the state, and the arm that closes a blind
#              spot rather than adding coverage. Until 2026-08-20 the stage
#              fell back to 10.0.2.15, which is the QEMU NAT guest address --
#              so in this bed the guess was RIGHT and all four b3 arms went
#              green on it, while on metal the same guess is off-subnet and
#              the stage answered refused with a verdict that misdirects. No
#              number of extra b3 arms could have found that, because every
#              one of them would have inherited the same coincidence. The four
#              arms above now name ip= explicitly; they were previously green
#              FOR THE WRONG REASON.
#   b3-dhcp    ip=dhcp, the NAMED opt-in: the stage asks the segment for an
#              address instead of refusing, and b3=ok. THE STATE IS NOT THE
#              WHOLE ASSERTION -- b3-pass reaches ok too, so this arm also
#              requires the row to carry `addr=dhcp` and the lease the NAT
#              handed out. That is what separates an address LEARNED from one
#              typed, and it is the half a guess could never produce: the bed
#              leases 10.0.2.15, which is precisely the address the old
#              invented default used, so the state alone cannot tell the two
#              apart and the lease facts can.
#   b3-nolease the falsifier for that state: ip=dhcp with -e1000 alone. The
#              card is present and the link comes up (no -e1000-no-link), and
#              without -e1000-nat there is no NAT and so no DHCP server, so
#              nothing answers the DISCOVERs. b3=no-lease, which is a fact
#              about the SEGMENT; no-address is a fact about DIAG.CFG, and
#              sending a reader to the wrong end of the wire is the whole
#              complaint that deleted the old default.
#   b3-short   the send side saying so, and the arm this table said could not
#              exist until the repeat knob landed. `short` fires when
#              net-io-send-raw-checked reports incomplete, and the only route
#              b3 has to it is the retransmit queue filling: net-rexmit-capacity
#              8 times net-mss 1400, so more than 11,200 bytes must be in flight
#              with nothing acking. `sendx=` repeats the SEND rather than
#              building one large payload, which is what makes the arm cheap:
#              one queue slot per repeat and no accumulator.
#              TWO LEVERS WERE TRIED AND THE FIRST ONE FAILED, which is worth
#              keeping because it looks right. `-e1000-no-tx-dd` stops transmit
#              COMPLETING and does not stop it HAPPENING: the frames still
#              reach the NAT, the acks still come back, the queue drains, and
#              the row read `sent=416/416 state=no-reply` -- a complete send
#              whose reply never arrived. What fills the queue is a peer that
#              never READS: its socket buffer fills, the NAT stops acking, and
#              the send stops part way at `sent=23686/106496`.
#              The other two routes to `short` stay closed by construction: the
#              exchange runs only in TcpEstablished, which rules out the
#              syn-sent shape, and CLOSE_WAIT is measured COMPLETE rather than
#              short (codex/test/apps/net-send-capped, `close-wait
#              complete=True sent=3`).
#   ovmf       QEMU + OVMF, the image on qemu-xhci usb-storage. Serial == bank,
#              bank=ok, and the summary QR decodes off the screendump
#              (tools/qr-read.ps1) to a body starting DIAG1;.
#   ovmf-ro    the same with the drive readonly=on: usb-storage refuses the
#              write; bank=none and the QR still decodes.
#
# The image is not rebuilt here. Rebuild with build/boot/build-diag.ps1 and
# this refuses to calibrate an image older than its sources.
#
# RUN build-diag.ps1 AFTER A GATE, NEVER BEFORE IT. build/build.ps1's clean
# phase empties build-output, and the arms that construct a variant image
# (sink-ladder, nic-noread, and anything else through New-Variant) read
# build-output/diag.efi, DIAG.ID and diag.cdx from there. With it emptied
# those arms report "(skipped: ... missing; run build-diag.ps1)" -- a SKIP
# and not a failure, which is easy to read as a regression in whatever you
# just changed. Measured 2026-08-21: gate, then arms, and sink-ladder skipped
# with everything about it healthy; build-diag again and it passed unchanged.
# The same clean is why a gate run between building an image and calibrating
# it costs you the variant arms.
[CmdletBinding()]
param(
    [string]$Img = 'build/boot/diag.img',
    [string]$Only = '',
    [switch]$Keep,
    [switch]$SkipOvmf,
    # codex-vm reaches END inside two seconds (measured 2026-08-18); the deadline
    # is only the backstop for a wedged arm, since the payload holds forever.
    [int]$Seconds = 30,
    [int]$OvmfSeconds = 100
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Vm   = Join-Path $Repo 'tools\codex-vm.exe'
$ImgAbs = if ([IO.Path]::IsPathRooted($Img)) { $Img } else { Join-Path $Repo $Img }
foreach ($f in @($Vm, $ImgAbs)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "FAIL: $f missing"; exit 1 }
}
$Efi = Join-Path $Repo 'build-output\diag.efi'

# REFUSE TO CALIBRATE A STALE IMAGE (ladder-arm.ps1's rule): a pass has to be a
# pass for the source on disk.
$imgTime = (Get-Item $ImgAbs).LastWriteTimeUtc
foreach ($s in @(Get-ChildItem (Join-Path $Repo 'build\boot\diag') -Filter 'Diag*.codex' -File)) {
    if ($s.LastWriteTimeUtc -gt $imgTime) {
        Write-Host "STALE: $($s.Name) is newer than $(Split-Path $ImgAbs -Leaf). Rebuild first: build/boot/build-diag.ps1"
        exit 1
    }
}

# THE OTHER HALF OF STALE, and it caught this ladder the day it was added: the
# image can be newer than every Diag*.codex and still have been compiled by a
# PREVIOUS seed. The seed moves on any merge-down and nothing rebuilds the
# image when it does, so a rehearsal record would certify an image the current
# compiler never built. Same defect the plug oracle had (main 17394).
$seedCdx = Join-Path $Repo 'seed\Codex.cdx'
if ((Test-Path $seedCdx) -and (Get-Item $seedCdx).LastWriteTimeUtc -gt $imgTime) {
    Write-Host "STALE: the seed is newer than $(Split-Path $ImgAbs -Leaf); it was compiled by the OLD compiler. Rebuild first: build/boot/build-diag.ps1"
    exit 1
}

& pwsh -NoProfile -File (Join-Path $Repo 'build\check-diag-verdicts.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: check-diag-verdicts is red; a state word has no verdict row'; exit 1 }

# Derived from the workspace, never a fixed path (L-SHARED).
$Work = Join-Path ([IO.Path]::GetTempPath()) ("diag-arm-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null

# THE SUBJECT MUST NOT MOVE UNDER THE RUN. Every arm copies $ImgAbs fresh, so
# a p4 sync or revert landing mid-rehearsal swaps the image for every arm
# after it and the run silently measures two different artifacts. On
# 2026-08-20 the handoff revert restored the depot image at 3:21:36 into a
# live rehearsal: arms before it booted payload be035dc8 kernel A7EDB7C6,
# asde-ctrlro at 3:23:02 booted 6e825d95 kernel A6D49D19 whose stage list
# predates gopmode, and the verdict read "(no gopmode stage row)" -- a
# defect-shaped answer to an integrity failure. The stale check above runs
# once at startup and cannot see this.
$SubjectHash = (Get-FileHash $ImgAbs -Algorithm SHA256).Hash
Write-Host "subject: $(Split-Path $ImgAbs -Leaf) $($SubjectHash.Substring(0,8))"

# THE SUITE MUST KNOW WHAT CONFIG THE SUBJECT CARRIES. A sitting image bakes
# its questions in (DIAG.CFG on the ESP, or the stub ring), and `sink
# ladder=1` moves the sink baseline from ok to ladder-all for every arm that
# was written against the default image. Six arms read as MISMATCH on
# 2026-08-20 for that reason alone, which is L-REHEARSE pulling against a
# suite that only knew one baseline.
# Derived from the recipe rather than a parameter, and only when the recipe
# describes THIS image: a flag someone has to remember is a flag that
# disagrees with the bytes (L-SAMEVER).

# MIRRORS diag-cfg-find (build/boot/diag/DiagStage.codex:127) AND MUST KEEP
# MIRRORING IT. The runtime takes the FIRST line whose key matches, reads a
# bare key as "on", and disables only on the exact value "off"
# (dg-stage-enabled, Diag.codex:129). Approximating any of those three reads a
# stage as running when the guest skips it, or the reverse. Nothing is trimmed
# here because the runtime does not trim either: "asde off " is not "off" to
# text-starts-with, so the stage runs, and a lenient parse here would expect a
# skip that never happens. The first-match rule is the one that bites -- "b3
# on" ahead of "b3 peer=..." is what made the composition report claim a peer
# the runtime never saw (main 18645).
function Get-CfgFirstValues([string]$text) {
    $m = @{}
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -eq '') { continue }
        $sp = $line.IndexOf(' ')
        if ($sp -lt 0) { $k = $line; $v = 'on' } else { $k = $line.Substring(0, $sp); $v = $line.Substring($sp + 1) }
        if (-not $m.ContainsKey($k)) { $m[$k] = $v }
    }
    return $m
}

$SubjectLadder = $false
$SubjectB3 = $false
$SubjectOff = @()
$SubjectCfgText = ''
$recipe = Join-Path $Repo 'build-output\diag-recipe.txt'
if (Test-Path $recipe) {
    $r = Get-Content $recipe
    $rh = ($r | Where-Object { $_.StartsWith('image-sha256=') } | Select-Object -First 1)
    if ($rh -and $rh.Substring(13) -eq $SubjectHash) {
        $cfgText = ($r | Where-Object { $_.StartsWith('stdin=') } | Select-Object -First 1) -replace '\|', "`n"
        $cf = ($r | Where-Object { $_.StartsWith('cfg=') } | Select-Object -First 1)
        if ($cf -and $cf.Length -gt 4) {
            $cp = $cf.Substring(4)
            if (-not [IO.Path]::IsPathRooted($cp)) { $cp = Join-Path $Repo $cp }
            if (Test-Path $cp) { $cfgText += "`n" + (Get-Content $cp -Raw) }
        }
        $SubjectLadder = $cfgText -match '(?m)^\s*sink\s+.*ladder=1'
        $SubjectB3 = $cfgText -match '(?m)^\s*b3\s+.*peer='
        $SubjectCfgText = $cfgText
        $SubjectOff = @((Get-CfgFirstValues $cfgText).GetEnumerator() | Where-Object { $_.Value -eq 'off' } | ForEach-Object { $_.Key })
        Write-Host "subject config: ladder=$(if ($SubjectLadder) { 'on' } else { 'off' }) (from the recipe for this image)"
        if ($SubjectOff.Count) { Write-Host "subject config: stages OFF: $($SubjectOff -join ', ') (every arm booting the subject expects state=skipped for these)" }
    } else {
        Write-Host 'subject config: unknown -- the recipe describes a different image, so the default baseline is used'
    }
}

function Assert-Subject([string]$name) {
    $h = (Get-FileHash $ImgAbs -Algorithm SHA256).Hash
    if ($h -eq $SubjectHash) { return }
    Write-Host "REFUSED before ${name}: $(Split-Path $ImgAbs -Leaf) changed under the run, $($SubjectHash.Substring(0,8)) -> $($h.Substring(0,8)). Every arm from here would measure a different image, and nothing printed past the swap is a measurement of the subject."
    exit 1
}

# A REAL PEER ON THE HOST, for the b3 stage. codex-vm's NAT translates the
# gateway address 10.0.2.2 to 127.0.0.1 when it opens a host socket
# (tools/codex-vm.c, nat_host_addr), so a guest that dials 10.0.2.2:P reaches
# a listener started here on port P. 'echo' writes back what it read, which is
# the only way b3 can reach ok; 'silent' completes the handshake and then says
# nothing, which is the only way it can reach no-reply with the connection up.
function Get-FreePort {
    $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $l.Start(); $p = $l.LocalEndpoint.Port; $l.Stop(); return $p
}

function Start-Peer([int]$port, [string]$mode) {
    $j = Start-Job -ScriptBlock {
        param($port, $mode)
        $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
        $l.Start()
        try {
            $deadline = (Get-Date).AddSeconds(180)
            while ((Get-Date) -lt $deadline) {
                if (-not $l.Pending()) { Start-Sleep -Milliseconds 50; continue }
                $c = $l.AcceptTcpClient()
                if ($mode -eq 'echo') {
                    $s = $c.GetStream(); $s.ReadTimeout = 30000
                    $buf = New-Object byte[] 4096
                    try {
                        $n = $s.Read($buf, 0, $buf.Length)
                        if ($n -gt 0) { $s.Write($buf, 0, $n); $s.Flush() }
                    } catch { }
                    Start-Sleep -Milliseconds 2000
                } else { Start-Sleep -Milliseconds 20000 }
                $c.Close()
            }
        } finally { $l.Stop() }
    } -ArgumentList $port, $mode
    # The listener has to be bound before the guest dials, or b3 reads refused
    # for a reason that is the harness and not the stack. The wait must NOT
    # connect to find out: 'silent' serves one connection at a time and holds
    # it for twenty seconds, so a probe connection would BE the conversation
    # and the guest would wait behind it.
    $ready = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $ready) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) { break }
        Start-Sleep -Milliseconds 100
    }
    return $j
}

function Stop-Peer($job) {
    if ($null -eq $job) { return }
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
}

function New-Copy([string]$name, [string]$from = $ImgAbs) {
    $script:SubjectArm = ($from -eq $ImgAbs)
    if ($script:SubjectArm) { Assert-Subject $name }
    $dst = Join-Path $Work $name
    Copy-Item $from $dst -Force
    Set-ItemProperty $dst -Name IsReadOnly -Value $false
    return $dst
}

# Boot in codex-vm with the image as both the boot medium (-kernel, whose
# BOOTX64.EFI the fake firmware runs) and the disk (-disk, which the xHCI
# mass-storage model serves and IDE answers as well). Wait, then read: the
# guest's serial goes to -output, flushed twice a second, and the payload
# holds its page in a spin the codex-vm watchdog ends on its own; the
# deadline is the backstop.
# The serial file is live under codex-vm, so open it shared rather than with
# Get-Content, which takes a lock the writer can trip over.
function Test-DiagEnd([string]$path) {
    if (-not (Test-Path $path)) { return $false }
    try {
        $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $sr = [IO.StreamReader]::new($fs)
        $txt = $sr.ReadToEnd()
        $sr.Close(); $fs.Close()
    } catch { return $false }
    return ($txt -match "(?m)^END\s*$")
}

function Invoke-Vm([string]$name, [string]$kernel, [string]$disk, [string[]]$extra, [int]$Budget = 0) {
    # NOT $seconds: PowerShell variable names are case-insensitive, so a
    # parameter named $seconds IS the script's $Seconds and the fallback
    # assigns it to itself. The deadline was zero and every arm read
    # "(no DIAG1 row on serial)" from a VM killed before it printed one.
    if ($Budget -le 0) { $Budget = $Seconds }
    $out = Join-Path $Work "$name.out"
    $err = Join-Path $Work "$name.err"
    Remove-Item $out, $err -ErrorAction SilentlyContinue
    $a = @('-kernel', $kernel, '-uefi', '-headless', '-output', $out)
    if ($disk) { $a += @('-disk', $disk) }
    # THE BED'S TRACE, KEPT BESIDE THE ARM'S OUTPUT. Every arm writes a census
    # of the medium it drove -- every BOT read and write with its LBA and
    # length -- so a rehearsal leaves something the board's own trace can be
    # diffed against row for row when it comes back. It goes to a FILE and not
    # to stderr because stderr does not survive: this harness redirects it per
    # arm and then KILLS codex-vm the moment END appears, and a killed process
    # does not drain its buffers. codex-vm flushes each line for the same
    # reason. With -Keep the whole working directory stays.
    $a += @('-census', (Join-Path $Work "$name.census"))
    if ($extra) { $a += $extra }
    $p = Start-Process -FilePath $Vm -ArgumentList $a -NoNewWindow -PassThru `
            -RedirectStandardError $err -RedirectStandardOutput (Join-Path $Work "$name.stdout")
    $deadline = (Get-Date).AddSeconds($Budget)
    # STOP WAITING FOR A PROCESS THAT FINISHED. The payload prints its whole
    # block and then HOLDS ITS PAGE forever by design, so the deadline was
    # never reached by the work finishing -- every arm cost the full budget.
    # Measured 2026-08-21: uniform 31s per arm at -Seconds 30, and the pass arm
    # answers whole, bank written, at -Seconds 3. Thirty arms times the
    # difference is about fourteen minutes per rehearsal spent on nothing.
    # Watch for END instead and keep the budget as the backstop, so a genuinely
    # wedged arm still costs what it costs and still reports.
    $settle = 0
    while (-not $p.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ($settle -eq 0 -and (Test-DiagEnd $out)) { $settle = 1; $deadline = (Get-Date).AddSeconds(1) }
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
    if (Test-Path $out) { return @(Get-Content $out -ErrorAction SilentlyContinue) }
    return @()
}

# The serial block: from the DIAG1 identity row to the END that follows the
# summary. The stub writes its own liveness marks to COM1 first, so the DIAG1
# row is looked for anywhere in its line rather than at column 0.
function Get-DiagBlock([string[]]$lines) {
    $block = @()
    $inBlock = $false
    foreach ($l in $lines) {
        if (-not $inBlock) {
            $i = $l.IndexOf('DIAG1 ')
            if ($i -ge 0) { $inBlock = $true; $block += $l.Substring($i) }
            continue
        }
        $block += $l
        if ($l -eq 'END') { break }
    }
    return ,$block
}

function Read-Bank([string]$disk, [string]$name) {
    $dir = Join-Path $Work "$name-bank"
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    $r = & pwsh -NoProfile -File (Join-Path $Repo 'build\read-stick.ps1') -ImageFile $disk -Name 'DIAG.TXT' -OutDir $dir 2>&1
    $f = Join-Path $dir 'DIAG.TXT'
    if (Test-Path $f) { return @(Get-Content $f) }
    return $null
}

# Live progress lines ("<stage> entering <step>") are serial-only BY DESIGN: a
# stage that hangs inside a step cannot bank, so the last such line on the wire
# is the L-STATES row that names the step; they are not rows of the record and
# the compare skips them.
function Compare-Rows([string[]]$a, [string[]]$b) {
    if ($null -eq $a -or $null -eq $b) { return 'one side missing' }
    $a = @($a | Where-Object { $_ -notmatch '^[a-z0-9]+ entering ' })
    $b = @($b | Where-Object { $_ -notmatch '^[a-z0-9]+ entering ' })
    if ($a.Count -ne $b.Count) { return "row count $($a.Count) vs $($b.Count)" }
    for ($i = 0; $i -lt $a.Count; $i++) {
        if ($a[$i] -ne $b[$i]) { return "row $i differs: [$($a[$i])] vs [$($b[$i])]" }
    }
    return ''
}

function Field([string[]]$block, [string]$prefix) {
    $l = $block | Where-Object { $_.StartsWith($prefix) } | Select-Object -First 1
    if ($l) { return $l } else { return '' }
}

# The ladder arms want the EXACT rung, not "a rung was named". The bed sets a
# size threshold with -usb-bot-drop-len, so the answer is known before the run
# and the arm fails if the row reports any other number. Anything less would
# pass on a ladder that always stops at the same place, which is the reading
# an ordinal-keyed drop produced while it looked correct.
function Judge-Ladder([string]$name, [string[]]$lines, [int]$wantRung, [int]$wantDone) {
    $block = Get-DiagBlock $lines
    $sink = Field $block 'stage=sink '
    $lad = ($block | Where-Object { $_ -match 'ladder done=' } | Select-Object -First 1)
    Write-Host "  ${name}: $lad"
    if ($block.Count -eq 0) { return '(no DIAG1 row on serial)' }
    if (-not $sink) { return '(no sink stage row)' }
    if (-not $sink.Contains('state=ladder-stop')) { return "sink row is [$sink]" }
    if ($lad -notmatch "rung-sectors=$wantRung\b") { return "the bed refuses at $wantRung sectors and the row says [$lad]" }
    if ($lad -notmatch "done=$wantDone\b") { return "expected $wantDone rungs banked below it: [$lad]" }
    return $expected[$name]
}

# Every free cluster marked bad, so gfat has nowhere to put DIAG.TXT. Copied
# from disk-arm.ps1's Set-FatFull; FAT16 only.
function Set-FatFull([string]$img) {
    $b = [IO.File]::ReadAllBytes($img)
    $p = 2048 * 512
    $bps = [BitConverter]::ToUInt16($b, $p + 11)
    $spc = $b[$p + 13]
    $rsvd = [BitConverter]::ToUInt16($b, $p + 14)
    $nfat = $b[$p + 16]
    $rootEnt = [BitConverter]::ToUInt16($b, $p + 17)
    $tot16 = [BitConverter]::ToUInt16($b, $p + 19)
    $fatSz = [BitConverter]::ToUInt16($b, $p + 22)
    $tot32 = [BitConverter]::ToUInt32($b, $p + 32)
    $total = if ($tot16 -ne 0) { [int]$tot16 } else { [int]$tot32 }
    $rootSectors = [int][math]::Ceiling(($rootEnt * 32) / $bps)
    $dataSectors = $total - ($rsvd + $nfat * $fatSz + $rootSectors)
    $clusters = [int][math]::Floor($dataSectors / $spc)
    $fatOff = $p + $rsvd * $bps
    $marked = 0
    for ($c = 2; $c -lt $clusters + 2; $c++) {
        $e = $fatOff + $c * 2
        if ($b[$e] -eq 0 -and $b[$e + 1] -eq 0) { $b[$e] = 0xF7; $b[$e + 1] = 0xFF; $marked++ }
    }
    for ($f = 1; $f -lt $nfat; $f++) {
        [Array]::Copy($b, $fatOff, $b, $fatOff + $f * $fatSz * $bps, $fatSz * $bps)
    }
    [IO.File]::WriteAllBytes($img, $b)
    return $marked
}

# A variant image from the stashed .efi with a different ring or an ESP file:
# same payload bytes, different stub or ESP.
function New-Variant([string]$name, [string]$stdinCfg, [string]$cfgFile) {
    $script:SubjectArm = $false
    if (-not (Test-Path $Efi)) { return '' }
    $idFile = Join-Path $Repo 'build-output\DIAG.ID'
    if (-not (Test-Path $idFile)) { return '' }
    $id = (Get-Content $idFile -Raw).Trim()
    $recipe = Join-Path $Repo 'build-output\diag-recipe.txt'
    $kernel = 'unknown'
    if (Test-Path $recipe) {
        $kl = Get-Content $recipe | Where-Object { $_.StartsWith('kernel=') } | Select-Object -First 1
        if ($kl) { $kernel = $kl.Substring(7) }
    }
    $cdx = Join-Path $Repo 'build-output\diag.cdx'
    $efi = Join-Path $Work "$name.efi"
    $stdin = "id $id`nkernel $kernel`n"
    if ($stdinCfg) { $stdin += $stdinCfg + "`n" }
    & pwsh -NoProfile -File (Join-Path $Repo 'build\cdx-to-pe.ps1') -CdxInput $cdx -Out $efi -HeapPages 32768 -ExitBootServices -Stdin $stdin 2>&1 | Out-Null
    if (-not (Test-Path $efi)) { return '' }
    $img = Join-Path $Work "$name.img"
    $extra = @("DIAG.ID=$idFile")
    $rcp = Join-Path $Repo 'build-output\DIAG.RCP'
    if (Test-Path $rcp) { $extra += "DIAG.RCP=$rcp" }
    if ($cfgFile) { $extra += "DIAG.CFG=$cfgFile" }
    & pwsh -NoProfile -File (Join-Path $Repo 'build\build-img.ps1') -PeInput $efi -Out $img -TotalSectors 32768 -Extra ($extra -join ';') 2>&1 | Out-Null
    if (-not (Test-Path $img)) { return '' }
    Set-ItemProperty $img -Name IsReadOnly -Value $false
    return $img
}

# A cfg that forces ONE stage on and otherwise carries the subject's own
# composition, for an arm that is ABOUT that stage. The subject's lines ride
# behind the forced one, so first-match (diag-cfg-find) takes the force and
# everything else stays as the sitting composed it: an asde arm on a sitting
# with "sink ladder=1" still sees the ladder, and its sink baseline still
# substitutes. Only the forced key is overridden.
function New-StageOnCfg([string]$stage) {
    $cfg = Join-Path $Work "$stage-on.cfg"
    $body = "$stage on`n"
    # id and kernel ride the stub ring, never the ESP; the rest is the composition.
    foreach ($line in (($SubjectCfgText -replace "`r`n", "`n") -split "`n")) {
        if ($line -eq '' -or $line -match '^(id|kernel)( |$)') { continue }
        $body += $line + "`n"
    }
    [IO.File]::WriteAllText($cfg, $body, [Text.ASCIIEncoding]::new())
    return $cfg
}

# Under OVMF (test-ovmf.ps1) the image rides qemu-xhci usb-storage, the serial
# lands in $env:TEMP\ovmf-serial-<tag>.log and the guest's writes land in the
# per-workspace disk copy $env:TEMP\ovmf-disk-<tag>.img, both named the way that
# script names them (L-SHARED). The screendump is what the QR is read from:
# tools/qr-read.ps1 is the decoder half of GopQr and its REPORT must start with
# the summary body's DIAG1; token.
function Invoke-Ovmf([string]$name, [bool]$readOnly) {
    $qemu = "D:\Program Files\qemu\qemu-system-x86_64.exe"
    if (-not (Test-Path $qemu)) { return '(skipped: QEMU not installed)' }
    $tag = (Split-Path $Repo -Leaf) -replace '[^A-Za-z0-9]',''
    $ser = Join-Path $env:TEMP "ovmf-serial-$tag.log"
    $disk = Join-Path $env:TEMP "ovmf-disk-$tag.img"
    $png = Join-Path $Work "$name.png"
    Assert-Subject $name
    Remove-Item $ser, $png -ErrorAction SilentlyContinue
    $a = @('-Img', $ImgAbs, '-Out', $png, '-UsbDisk', '-Seconds', $OvmfSeconds)
    if ($readOnly) { $a += '-ReadOnlyDisk' }
    $log = & pwsh -NoProfile -File (Join-Path $Repo 'build\boot\test-ovmf.ps1') @a 2>&1
    $log | ForEach-Object { Write-Host "  $_" } | Out-Null
    if (-not (Test-Path $ser)) { return '(no serial log from OVMF)' }
    $lines = @(Get-Content $ser)
    $block = Get-DiagBlock $lines
    if ($block.Count -eq 0) { return '(no DIAG1 row on serial)' }
    if ($block[-1] -ne 'END') { return "(serial block did not reach END; last: $($block[-1]))" }
    foreach ($st in @('smbios', 'edid', 'cpu', 'pci', 'scene', 'gopmode', 'block', 'xhci', 'sink', 'pch', 'nicsit', 'nicinit', 'nicring', 'b3', 'pchk1', 'asde')) { if (-not (Field $block "stage=$st ")) { return "(no $st stage row)" } }
    $bank = Field $block 'bank='
    $file = $null
    if (Test-Path $disk) { $file = Read-Bank $disk $name }
    if (-not $readOnly) {
        if (-not $bank.StartsWith('bank=ok')) { return "bank row is [$bank]" }
        if ($null -eq $file) { return 'bank=ok but no DIAG.TXT on the disk copy' }
        $d = Compare-Rows $block $file
        if ($d) { return "serial vs file: $d" }
    } else {
        if (-not $bank.StartsWith('bank=none')) { return "bank row is [$bank]" }
        if ($null -ne $file) { return 'bank=none but DIAG.TXT exists on the disk copy' }
    }
    if (-not (Test-Path $png)) { return '(no screendump)' }
    $qr = & pwsh -NoProfile -File (Join-Path $Repo 'tools\qr-read.ps1') -Path $png 2>&1
    $report = @($qr | ForEach-Object { "$_" })
    $i = [Array]::IndexOf($report, '================ REPORT ================')
    if ($i -lt 0) { return "QR did not decode: $($report | Where-Object { $_ -like '*[qr]*' } | Select-Object -Last 1)" }
    $body = $report[$i + 1]
    if (-not $body.StartsWith('DIAG1;')) { return "QR body is [$body]" }
    if ($report -match 'INCOMPLETE') { return 'QR decoded but INCOMPLETE' }
    return $expected[$name]
}

# The bed's own answers for every stage: codex-vm publishes SMBIOS (Codex
# Project Codex VM, its legacy 2.1 table plus a 3.0 entry), an EDID (CDX codex-vm dsp) and a hypervisor bit, so a passing
# boot there reads exactly this. The no-smbios/no-edid/edid-bad arms are the
# switches that show the three readers say no.
$bedStates = @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part'; b3 = 'no-peer'; pchk1 = 'no-part'; asde = 'no-part'; box = 'Codex Project Codex VM' }
# With no bank there is no medium selected, so the write-side stage says so and runs nothing.
$noBankStates = @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'no-medium'; sink = 'no-medium'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part'; b3 = 'no-peer'; pchk1 = 'no-part'; asde = 'no-part'; box = 'Codex Project Codex VM' }

$expected = [ordered]@{
    'pass'     = 'bank=ok serial==file every stage stated'
    'no-smbios' = 'smbios=no-table box=unnamed bank=ok'
    'no-edid'  = 'edid=absent bank=ok'
    'edid-bad' = 'edid=bad-checksum bank=ok'
    'gop-kept' = 'gopmode=KEPT with -gop-width 1600 -gop-height 900: the largest mode is already current so SetMode is never called. The falsifier for the gopmode row -- every other bed arm reads honoured, and a row that only ever says one word is not an instrument'
    'no-medium' = 'bank=none (mount) summary reached, no file'
    'fat-full' = 'bank=none (write refused) summary reached, no file'
    'cfg-off'  = 'scene skipped, bank=ok'
    'esp-cfg'  = 'cfg-file=1, bank=ok'
    'block-oob' = 'block=write-refused (LBA past the medium), bank=ok'
    'sink-chunk' = 'sink=ok at a NON-DEFAULT MSC chunk: sink chunk=8 in DIAG.CFG reaches cell 91 and the row reports chunk=8, so a flight can vary the transfer size'
    'sink-ladder' = 'sink=ladder-all: sink ladder=1 runs all seven rungs (1..64 sectors) and every one completes here, because this bed completes a transfer at any size. The arm that shows the ladder RUNS'
    'sink-ladder-32' = 'sink=ladder-stop rung=32 done=5: -usb-bot-drop-len 16384 refuses every command of 32 sectors or more, so the bed HAS a threshold and the ladder must report that exact number'
    'sink-ladder-16' = 'sink=ladder-stop rung=16 done=4: the same arm with the bed threshold moved to 8192, and the ladder must move with it. Two arms with DIFFERENT answers are what separate an instrument that measures from one that always says the same thing'
    'sink-revived' = 'the DRIVER RECOVERS and the row says so: the same death as sink-dies, with -usb-bot-revive-on-reset, so the target answers again after the Bulk-Only Mass Storage Reset the driver already sends. sink=recovered with rty=3 (msc-retry-ok) and bank=ok with the file whole, against sink-dies reading died with rty=2 and bank=lost off the same lever. This is the only arm in the tree where WORKS-9''s recovery path SUCCEEDS; every other one measures it failing. ABLATION: a driver that does not reset on no-event turns this red, measured that way -- with the no-event branch of msc-retry-chunk forced away the write never comes back and the row reads died, which is the same row the latched control gives'
        'sink-shift' = 'sink=bad-bytes (oracle shifted by one), bank=ok'
    'bank-lost' = 'the wedge persists past the sink stage: bank=lost naming sink, the SUMMARY no longer claims ok over a truncated file, and the six stages sink used to eat are on the medium behind a before-deferred marker'
    'sink-drop' = 'sink=write-refused, after=0 (a small write is refused straight after), and yet bank=ok with the file whole: the bed does NOT reproduce the metal bank death'
    'nic-pass'  = 'nicsit=ok nicinit=ok nicring=frames with -e1000 -e1000-nat (no card by default), bank=ok'
    'nic-noread' = 'nicring listen=0 in DIAG.CFG sends the ARP and idles without polling, so nothing recycles the descriptor: wb comes back ABOVE ZERO with buf=y, which is the one arm that shows the hexdump can read a writeback at all'
    'nic-nolink' = 'nicinit=no-link with -e1000-no-link; nicsit ok, nicring quiet, bank=ok'
    'nic-rdhro' = 'nicring rdh-writable=n with -e1000-rdh-ro: the part keeps the head and our write is dropped. THE FALSIFIER FOR NIC-4 ITSELF -- every other arm reads rdh-writable=y, and a field that has only ever said one word cannot tell frames-moving from RDH-is-not-ours on metal'
    'sink-dies' = 'the sink kills the target and the bank dies WITH the sink named: bank=lost at=sink with before-deferred still bank=ok, so the run was healthy right up to the write that killed it. -usb-bot-die-len 32768 -usb-bot-die-lba 3000, and the LBA is what aims it -- censused 2026-08-21, the bank issues 32768-byte writes too, at fixed lba 2049 and 2153, while the sink is one burst of sixty at 3548..7324. Without the lba key the same length kills the FAT write at 2049 and the bank never forms at all. THE CONTROL IS sink-ladder: the same image with no flag at all, where every rung completes and the sink returns ladder-all, so this arm reads a death and its pair reads a healthy medium'
    'xhci-two' = 'codex-vm -xhci-two: ctls=2 and the SECOND controller, the ASMedia 1b21:1242, comes up running. The model has carried two controllers for some time and no arm anywhere ran it -- measured 2026-08-21, zero hits for -xhci-two, -xhci-no-disk or -xhci-bar2 across every arm and every .vmargs. Without the flag there is no ctl1 row at all, so the assertion cannot be satisfied by a one-controller run'
    'nic-invisible' = 'nicring gp above zero with rdh=0 and dd=0 under -i219 -e1000-inject-armed: the MAC counted a frame in THIS stage window and no descriptor came back. Sitting 9 read that off the ASUS (gp=1 rnbc=0 ddset=0, aim rdba=ours) and no bed could produce it -- paired with nic-armed, which reads the SAME gp above zero and sees the frame, so gp is not the discriminator'
    'nic-armed' = 'the positive control for nic-invisible: same injected frame, same armed timing, no stall, so nicring reports frames with rdh above zero. Without it, gp=1 beside an invisible frame could be a model that never delivers anything. It drops -i219 to get there, so what it varies is DEVICE IDENTITY: it answers "does this bed deliver anything at all" and cannot answer "does K1 gate delivery". nic-k1-off is the arm for that'
        'nic-k1-off' = 'the K1 control for nic-invisible, and the one that holds the PART fixed: -i219 -i219-k1-nvm 0, same stage, same injected frame, same armed timing, so the only thing that moves between the two rows is whether K1 is enabled at power-up. The frame is written back and nicring reports frames with rdh above zero. ABLATION, and it is what this pair is worth: a model where K1 does not gate delivery makes the pair VACUOUS -- both rows report frames, nic-invisible goes red, and neither row says anything about K1. Measured that way on purpose, 2026-08-21: with i219-mac-stalled forced to 0 this arm still passed and nic-invisible failed, so the pair is exactly as good as i219-mac-stalled being keyed on K1 and no better'
    'nic-nomac' = 'nicinit=no-mac with -e1000-no-mac; nicsit ok, bank=ok'
    'nic-nohpet' = 'three nic stages no-hpet with -no-hpet, scene no-clock, bank=ok'
    'b3-pass'  = 'b3=ok: a real TCP conversation with a real host peer over the Intel part, connect to close, and the bytes that came back are the bytes DIAG.CFG said to expect'
    'nic-kills-msc' = 'bank=lost at=nicinit with -usb-bot-die-on-nic, and stage=b3 state=ok beside it: the medium stops answering at the first bulk write after the NIC is brought up, while the TCP conversation over that same NIC completes. Armed by RCTL.EN, which is nicinit''s, and every b3 step note reads banked=-1. Its control is b3-pass, the same run with the lever removed, which banks the full trail. NOTE the row it does NOT reproduce: sitting 11 lost the bank at b3''s rings-link with nicinit banked whole, so metal''s FIRST bring-up did not kill the medium'
    'b3-noreply' = 'b3=no-reply: the peer accepts and never answers, so the handshake is up and the exchange is not. The one arm that separates our stack from the far end'
    'b3-short' = 'b3=short with sendx=8192 against a peer that accepts and never reads: its socket buffer fills, the NAT stops acking, our retransmit queue fills and the send stops part way (measured sent=23686/106496). THE FALSIFIER FOR THE SEND SIDE -- every other b3 arm reports a complete send, and a sent= that has only ever matched what was asked for cannot tell a real send from an intended one'
    'b3-refused' = 'b3=refused: nothing is listening on the port, so no SYN-ACK ever arrives and the stage says the handshake, not the exchange, is what failed'
    'b3-nopart' = 'b3=no-part: a peer IS named and there is no card, which is the only arm that reaches the part check past the no-peer short circuit'
    'asde-differs' = 'asde=differs with -e1000-phy-link -e1000-asde: the two no-reset arms come back different, which is the positive control -- without it the stage could only ever be seen saying same'
    'asde-ctrlro' = 'asde=ctrl-ro with -e1000-ctrl-ro: a bit we cleared reads back set, so nothing this stage wrote was written and both arms are void'
    'b3-noaddr' = 'b3=no-address: a peer named with no ip=, so there is no address to dial FROM and the stage refuses instead of inventing one'
    'b3-clockstuck' = 'b3=clock-stuck with -hpet-frozen: the HPET window reads all-ones, a bogus nonzero rate over a counter that never moves, and the clock control at b3 entry refuses before bring-up; the three nic stages are off by cfg so nothing ahead of b3 spends its fuel on the same stuck clock'
    'b3-banklost' = 'b3 bank lost inside the reset sequence with -usb-bot-die-len 5632 -usb-bot-die-lba 3400: the medium dies on the first 11-sector bank write, one of the b3 reset-* notes (WHICH one drifts run to run with the digit widths in the note text, so the arm derives it from the trail instead of naming it), the step says so on serial the moment its note is refused, every note before it banked and every note after it reads banked=-1, and the medium itself ends at the step immediately before the refused one; the summary says bank=lost at=b3. Sitting 11 shape: the glass names where the medium DIED, not where the ladder noticed'
    'b3-dhcp'  = 'b3=ok with ip=dhcp: the address is LEARNED from the segment and the row proves it, carrying addr=dhcp and the lease the NAT handed out (ip=10.0.2.15 gw=10.0.2.2). The lease facts are what a guess cannot produce'
    'b3-nolease' = 'b3=no-lease with ip=dhcp and -e1000 alone: the card is present and the link is up, and with no NAT there is no DHCP server, so the stage says the SEGMENT did not answer rather than blaming DIAG.CFG. The falsifier for the state'
    'k1-taken' = 'pchk1=taken with -i219: the part powers up at the campaign condition (K1 enabled, Giga_K1_disable clear) and the readback after e1000-init shows the disable bit SET, so the driver step landed'
    'k1-blocked' = 'pchk1=no-mdio with -i219-mng-holds: firmware holds the MNG bit, the semaphore cannot be acquired and MDIO is refused, so the same binary moves this row off taken. Without it the stage could only ever be seen saying taken'
    'ovmf'     = 'bank=ok serial==file QR decodes'
    'ovmf-ro'  = 'bank=none QR decodes'
}
$actual = [ordered]@{}
$names = if ($Only) { @($Only) } else { @($expected.Keys) }
foreach ($n in $names) { if (-not $expected.Contains($n)) { Write-Host "FAIL: no arm '$n'"; exit 1 } }
if ($SkipOvmf) { $names = @($names | Where-Object { -not $_.StartsWith('ovmf') }) }

function Judge-Vm([string]$name, [string[]]$lines, [string]$disk, [bool]$wantBank, [string]$bankNote, [hashtable]$states) {
    # ONLY AN ARM BOOTING THE UNMODIFIED SUBJECT CARRIES THE SUBJECT'S CONFIG.
    # A New-Variant arm lays down its OWN DIAG.CFG, which replaces the sitting's,
    # so its baseline is the default one. Substituting for those too read eight
    # arms as MISMATCH on 2026-08-20, in the opposite direction from the six this
    # substitution exists to fix: the population is not homogeneous and a global
    # rule cannot see that.
    if ($script:SubjectArm -and $null -ne $states) {
        if ($SubjectLadder -and $states['sink'] -eq 'ok') {
            $states = $states.Clone()
            $states['sink'] = 'ladder-all'
        }
        # The ESP DIAG.CFG is read only AFTER the bank opens, so no-medium and
        # fat-full run the default baseline however the subject was built: one
        # has no medium to carry the file and the other never gets that far.
        if ($SubjectB3 -and $wantBank -and $states['b3'] -eq 'no-peer') {
            $states = $states.Clone()
            $states['b3'] = 'no-part'
        }
        # A COMPOSITION THAT TURNS A STAGE OFF WAS NOT REHEARSABLE AT ALL until
        # this substitution existed, which is L-REHEARSE with no expressible arm:
        # the composer either flew unrehearsed bytes or abandoned the
        # composition. Measured by red 2026-08-21 on a "asde off" sitting -- five
        # arms disagreed (pass, nic-pass, nic-nolink, asde-differs, asde-ctrlro),
        # every one of them on the same row, [stage=asde state=skipped risk=writes
        # cfg=off]. A general arm should not care what the shipped cfg composed;
        # a skipped row is the composition working, not a disagreement.
        # The two asde arms are NOT fixed here and must not be: an arm ABOUT a
        # stage that silently accepts the stage being skipped is an instrument
        # that cannot fail (L-FALSIF). They force the stage ON in their own cfg
        # instead, which also makes them New-Variant arms and so exempt from this
        # whole block.
        $off = @($SubjectOff | Where-Object { $states.ContainsKey($_) })
        if ($off.Count) {
            $states = $states.Clone()
            foreach ($st in $off) { $states[$st] = 'skipped' }
        }
    }
    $block = Get-DiagBlock $lines
    if ($block.Count -eq 0) { return '(no DIAG1 row on serial)' }
    if ($block[-1] -ne 'END') { return "(serial block did not reach END; last: $($block[-1]))" }
    if (-not (Field $block 'summary run=')) { return '(no summary row)' }
    $bank = Field $block 'bank='
    $file = Read-Bank $disk $name
    if ($wantBank) {
        if (-not $bank.StartsWith('bank=ok')) { return "bank row is [$bank]" }
        if ($null -eq $file) { return 'bank=ok but no DIAG.TXT on the disk' }
        $d = Compare-Rows $block $file
        if ($d) { return "serial vs file: $d" }
    } else {
        if (-not $bank.StartsWith('bank=none')) { return "bank row is [$bank]" }
        if ($bankNote -and -not $bank.Contains($bankNote)) { return "bank note is [$bank], wanted $bankNote" }
        if ($null -ne $file) { return 'bank=none but DIAG.TXT exists on the disk' }
    }
    foreach ($st in @('smbios', 'edid', 'cpu', 'pci', 'scene', 'gopmode', 'block', 'xhci', 'sink', 'pch', 'nicsit', 'nicinit', 'nicring', 'b3', 'pchk1', 'asde')) {
        $row = Field $block "stage=$st "
        if (-not $row) { return "(no $st stage row)" }
        if ($states.ContainsKey($st) -and -not $row.Contains("state=$($states[$st])")) { return "$st row is [$row]" }
    }
    if ($states.ContainsKey('box')) {
        $box = Field $block 'box='; if (-not $box.StartsWith('box=' + $states['box'])) { return "box row is [$box]" }
    }
    if ($name -eq 'esp-cfg' -and -not $bank.Contains('cfg-file=1')) { return "bank row is [$bank], wanted cfg-file=1" }
    return $expected[$name]
}

foreach ($name in $names) {
    Write-Host "[diag-arm] $name..."
    switch ($name) {
        'pass' {
            $k = New-Copy 'k-pass.img'
            $lines = Invoke-Vm 'pass' $k $k @()
            $actual['pass'] = Judge-Vm 'pass' $lines $k $true '' $bedStates
        }
        'no-smbios' {
            $k = New-Copy 'k-nosmbios.img'
            $lines = Invoke-Vm 'no-smbios' $k $k @('-no-smbios')
            $actual['no-smbios'] = Judge-Vm 'no-smbios' $lines $k $true '' @{ smbios = 'no-table'; box = 'unnamed'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured' }
        }
        'no-edid' {
            $k = New-Copy 'k-noedid.img'
            $lines = Invoke-Vm 'no-edid' $k $k @('-no-edid')
            $actual['no-edid'] = Judge-Vm 'no-edid' $lines $k $true '' @{ smbios = 'ok'; edid = 'absent'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured' }
        }
        'edid-bad' {
            $k = New-Copy 'k-edidbad.img'
            $lines = Invoke-Vm 'edid-bad' $k $k @('-edid-bad')
            $actual['edid-bad'] = Judge-Vm 'edid-bad' $lines $k $true '' @{ smbios = 'ok'; edid = 'bad-checksum'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured' }
        }
        'gop-kept' {
            # Predicted BEFORE the run and all five matched on 2026-08-20:
            # codex-vm's mode 3 is the -gop-width/-height pair, so the firmware
            # already boots in the largest mode it enumerates and the stub finds
            # best == current. max=4 before=3 chose=3 flags=1 (ran, not called)
            # and the status quadword stays the zero HandoffInit wrote.
            $k = New-Copy 'k-gopkept.img'
            $lines = Invoke-Vm 'gop-kept' $k $k @('-gop-width', '1600', '-gop-height', '900')
            $v = Judge-Vm 'gop-kept' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'kept'; xhci = 'running' }
            $row = (Field (Get-DiagBlock $lines) '  max=').Trim()
            if ($v -eq $expected['gop-kept'] -and $row -ne 'max=4 before=3 chose=3 flags=1 status=00000000') { $v = "gopmode bank row is [$row]" }
            $actual['gop-kept'] = $v
        }
        'no-medium' {
            $k = New-Copy 'k-nomedium.img'
            $lines = Invoke-Vm 'no-medium' $k '' @()
            $actual['no-medium'] = Judge-Vm 'no-medium' $lines $k $false 'mount stage' ($noBankStates + @{ xhci = 'no-disk' })
        }
        'bank-lost' {
            $k = New-Copy 'k-banklost.img'
            $lines = Invoke-Vm 'bank-lost' $k $k @('-usb-bot-drop', '950', '-usb-bot-drops', '100000')
            $block = Get-DiagBlock $lines
            $sink = Field $block 'stage=sink '
            $wr = ($block | Where-Object { $_ -match 'wr=' } | Select-Object -First 1)
            $sum = ($block | Where-Object { $_ -match 'bank=' } | Select-Object -Last 1)
            $file = Read-Bank $k 'bank-lost'
            Write-Host "  bank-lost: $wr"
            Write-Host "  bank-lost: $sum"
            Write-Host "  bank-lost: file rows $(if ($null -eq $file) { 'none' } else { $file.Count }) against serial $($block.Count)"
            # THE POINT OF THE ARM IS THE STAGES AFTER SINK, not the bank row.
            # A blanket drop wedges the medium wherever it lands and every
            # landing produces a bank=lost row, so the bank row alone went
            # green at 500 while the wedge was in nicinit. Sink is deferred to
            # last, so the six stages it used to eat must all be on the medium.
            $after = @('pch', 'nicsit', 'nicinit', 'nicring', 'b3', 'asde')
            $missingList = [System.Collections.Generic.List[string]]::new()
            foreach ($st in $after) {
                $hit = $false
                if ($null -ne $file) { foreach ($row in $file) { if ($row -like "*stage=$st *") { $hit = $true } } }
                if (-not $hit) { $missingList.Add($st) }
            }
            $missing = $missingList -join ', '
            $actual['bank-lost'] =
                if ($block.Count -eq 0) { '(no DIAG1 row on serial)' }
                elseif ($block[-1] -ne 'END') { "(serial did not reach END; last: $($block[-1]))" }
                elseif (-not $sum) { '(no bank row)' }
                elseif ($sum -match 'bank=ok') { "the bank still claims ok while the file is short: [$sum]" }
                elseif ($sum -notmatch 'bank=lost') { "bank row is [$sum]" }
                elseif (-not $sink) { '(no sink stage row)' }
                elseif ($sink -notmatch 'state=died') { "the drop missed the sink's data phase, re-derive the ordinal: sink row is [$sink]" }
                elseif ($sum -notmatch 'at=sink') { "the wedge landed somewhere other than sink, re-derive the ordinal: [$sum]" }
                elseif ($null -eq $file) { 'no DIAG.TXT at all; the bank never opened' }
                elseif ($missing -ne '') { "sink ate the bank again: DIAG.TXT is missing $missing" }
                elseif (-not ($file -like '*before-deferred *')) { 'no before-deferred marker in DIAG.TXT; the record cannot say sink had not run' }
                else { $expected['bank-lost'] }
        }
        'sink-drop' {
            $k = New-Copy 'k-sinkdrop.img'
            $lines = Invoke-Vm 'sink-drop' $k $k @('-usb-bot-drop', '950', '-usb-bot-drops', '4')
            $block = Get-DiagBlock $lines
            $sink = Field $block 'stage=sink '
            $wr = ($block | Where-Object { $_ -match 'wr=' } | Select-Object -First 1)
            $file = Read-Bank $k 'sink-drop'
            Write-Host "  sink-drop: $wr"
            # The bank OPENED here (bank=ok) and then died, which is the metal
            # shape: what fails is the rewrite AFTER the refused stage, so the
            # file on the disk is shorter than the serial. Judge-Vm cannot
            # express that -- its bank arm requires the two to agree.
            $actual['sink-drop'] =
                if ($block.Count -eq 0) { '(no DIAG1 row on serial)' }
                elseif ($block[-1] -ne 'END') { "(serial did not reach END; last: $($block[-1]))" }
                elseif (-not $sink) { '(no sink stage row)' }
                elseif (-not $sink.Contains("state=$(if ($SubjectLadder) { 'rung-1-refused' } else { 'died' })")) { "sink row is [$sink]" }
                elseif ($wr -notmatch 'after=0') { "the refusal did not wedge the medium: [$wr]" }
                elseif ($null -eq $file) { 'no DIAG.TXT at all; the bank never opened' }
                elseif ($file.Count -ne $block.Count) { "DIAG.TXT has $($file.Count) rows against the serial's $($block.Count); in this bed the rewrite after a refused sink LANDS, so a short file is a change worth reading" }
                else { $expected['sink-drop'] }
        }
        'fat-full' {
            $k = New-Copy 'k-fatfull.img'
            $marked = Set-FatFull $k
            Write-Host "  fat-full: $marked clusters marked bad"
            $lines = Invoke-Vm 'fat-full' $k $k @()
            $actual['fat-full'] = Judge-Vm 'fat-full' $lines $k $false 'write refused' ($noBankStates + @{ xhci = 'running' })
        }
        'cfg-off' {
            $k = New-Variant 'k-cfgoff' 'scene off' ''
            if (-not $k) { $actual['cfg-off'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'cfg-off' $k $k @()
                $actual['cfg-off'] = Judge-Vm 'cfg-off' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'skipped' }
            }
        }
        'esp-cfg' {
            $cfg = Join-Path $Work 'DIAG.CFG'
            [IO.File]::WriteAllText($cfg, "pci on`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-espcfg' '' $cfg
            if (-not $k) { $actual['esp-cfg'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'esp-cfg' $k $k @()
                $actual['esp-cfg'] = Judge-Vm 'esp-cfg' $lines $k $true '' $bedStates
            }
        }
        'block-oob' {
            $cfg = Join-Path $Work 'DIAG-oob.CFG'
            [IO.File]::WriteAllText($cfg, "block lba=999999999`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-blockoob' '' $cfg
            if (-not $k) { $actual['block-oob'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'block-oob' $k $k @()
                $actual['block-oob'] = Judge-Vm 'block-oob' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'write-refused'; sink = 'ok'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part' }
            }
        }
        'sink-chunk' {
            $cfg = Join-Path $Work 'DIAG-chunk.CFG'
            [IO.File]::WriteAllText($cfg, "sink chunk=8`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-sinkchunk' '' $cfg
            if (-not $k) { $actual['sink-chunk'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'sink-chunk' $k $k @()
                $block = Get-DiagBlock $lines
                $sink = Field $block 'stage=sink '
                $wr = ($block | Where-Object { $_ -match 'chunk=' } | Select-Object -First 1)
                Write-Host "  sink-chunk: $wr"
                $actual['sink-chunk'] =
                    if ($block.Count -eq 0) { '(no DIAG1 row on serial)' }
                    elseif (-not $sink) { '(no sink stage row)' }
                    elseif (-not $sink.Contains('state=ok')) { "sink row is [$sink]" }
                    elseif ($wr -notmatch 'chunk=8\b') { "the cfg key did not reach the MSC layer: [$wr]" }
                    else { $expected['sink-chunk'] }
            }
        }
        'sink-ladder' {
            $cfg = Join-Path $Work 'DIAG-ladder.CFG'
            [IO.File]::WriteAllText($cfg, "sink ladder=1`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-sinkladder' '' $cfg
            if (-not $k) { $actual['sink-ladder'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'sink-ladder' $k $k @()
                $block = Get-DiagBlock $lines
                $sink = Field $block 'stage=sink '
                $lad = ($block | Where-Object { $_ -match 'ladder done=' } | Select-Object -First 1)
                Write-Host "  sink-ladder: $lad"
                $actual['sink-ladder'] =
                    if ($block.Count -eq 0) { '(no DIAG1 row on serial)' }
                    elseif (-not $sink) { '(no sink stage row)' }
                    elseif (-not $sink.Contains('state=ladder-all')) { "sink row is [$sink]" }
                    elseif ($lad -notmatch 'done=7\b') { "not every rung ran: [$lad]" }
                    else { $expected['sink-ladder'] }
            }
        }
        'sink-ladder-32' {
            $cfg = Join-Path $Work 'DIAG-ladder32.CFG'
            [IO.File]::WriteAllText($cfg, "sink ladder=1`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-sinkladder32' '' $cfg
            if (-not $k) { $actual['sink-ladder-32'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'sink-ladder-32' $k $k @('-usb-bot-drop-len', '16384', '-usb-bot-drop-len-max', '16384')
                $actual['sink-ladder-32'] = Judge-Ladder 'sink-ladder-32' $lines 32 5
            }
        }
        'sink-ladder-16' {
            $cfg = Join-Path $Work 'DIAG-ladder16.CFG'
            [IO.File]::WriteAllText($cfg, "sink ladder=1`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-sinkladder16' '' $cfg
            if (-not $k) { $actual['sink-ladder-16'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'sink-ladder-16' $k $k @('-usb-bot-drop-len', '8192', '-usb-bot-drop-len-max', '8192')
                $actual['sink-ladder-16'] = Judge-Ladder 'sink-ladder-16' $lines 16 4
            }
        }
        'sink-revived' {
            # The positive half of sink-dies, and the ONLY arm where WORKS-9's
            # recovery path succeeds. Same lever, same LBA, same length: the
            # single thing that moves is whether the target answers again
            # after the Bulk-Only Mass Storage Reset the driver was already
            # sending and getting nothing back from.
            #
            # rty is what makes it an arm about RECOVERY rather than about the
            # medium. state=recovered alone would also be produced by a stage
            # that never faltered if the retry cell were stale, which is why
            # DiagSink clears it before its own write and why this asserts the
            # value rather than the word.
            $k = New-Copy 'k-sinkrevived.img'
            $lines = Invoke-Vm 'sink-revived' $k $k @('-usb-bot-die-len', '32768', '-usb-bot-die-lba', '3000', '-usb-bot-revive-on-reset')
            $block = Get-DiagBlock $lines
            $sum = ($block | Where-Object { $_ -match 'bank=' } | Select-Object -Last 1)
            $sink = Field $block 'stage=sink '
            $row = Field $block '  wr='
            Write-Host "  sink-revived: $sink"
            Write-Host "  sink-revived: $row"
            $actual['sink-revived'] =
                if ($block.Count -eq 0) { '(no DIAG1 row on serial)' }
                elseif ($sink -notmatch 'state=recovered') { "sink row is [$sink], wanted state=recovered (the write went through on the retry)" }
                elseif ($row -notmatch 'rty=3') { "answer row is [$row], wanted rty=3 (msc-retry-ok: the reset ran AND the retry carried it)" }
                elseif ($sum -notmatch 'bank=ok') { "summary row is [$sum], wanted bank=ok (the bank must survive a recovered write)" }
                else { $expected['sink-revived'] }
        }
        'sink-shift' {
            $cfg = Join-Path $Work 'DIAG-shift.CFG'
            [IO.File]::WriteAllText($cfg, "sink shift=1`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-sinkshift' '' $cfg
            if (-not $k) { $actual['sink-shift'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'sink-shift' $k $k @()
                $actual['sink-shift'] = Judge-Vm 'sink-shift' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; sink = 'bad-bytes'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part' }
            }
        }
        'nic-pass' {
            $k = New-Copy 'k-nicpass.img'
            $lines = Invoke-Vm 'nic-pass' $k $k @('-e1000', '-e1000-nat')
            $v = Judge-Vm 'nic-pass' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'ok'; nicinit = 'ok'; nicring = 'frames'; asde = 'same' }
            # GPRC counts at the MAC, before any descriptor. With the NAT
            # answering, it MUST have moved; a stats row reading gprc=0 beside
            # nicring=frames would mean the counter is a stub and the whole
            # discriminator is worthless. Paired with nic-nolink, which requires
            # gprc=0 on the same row, so neither arm can pass a dead counter.
            if ($v -eq $expected['nic-pass']) {
                $st = Field (Get-DiagBlock $lines) '  stats '
                if ($st -notmatch 'gprc=[1-9]') { $v = "stats row is [$st], wanted gprc above zero" }
                # gp= is the SAME number on the row that survives a dead bank,
                # and it must agree with the stats row it was copied from. Both
                # come from ONE read passed to two consumers; split that into
                # two reads and the counter clears in between, so exactly one of
                # these assertions goes red. That is why both are here rather
                # than either.
                $an = Field (Get-DiagBlock $lines) '  m='
                if ($an -notmatch 'gp=[1-9]') { $v = "answer row is [$an], wanted gp above zero" }
                elseif ($st -match 'gprc=(\d+)') {
                    $want = $Matches[1]
                    if ($an -notmatch ('gp=' + $want + '\b')) { $v = "answer gp= and stats gprc= disagree: [$an] vs [$st]" }
                }
            }
            # This arm CANNOT assert wb above zero, and finding that out is why
            # the row is shaped the way it is. e1000-poll-raw recycles the
            # descriptor it took a frame from, so when the listen succeeds our
            # own driver has zeroed the writeback half before the row is built:
            # the first run here read frames with rdh=1, buf=y and wb=0. What
            # this arm can hold is that the part advanced our head and our own
            # buffer address survived. The positive reading for wb is nic-noread,
            # which sends without polling so nothing recycles.
            if ($v -eq $expected['nic-pass']) {
                $an = Field (Get-DiagBlock $lines) '  m='
                if ($an -notmatch 'buf=y') { $v = "answer row is [$an], wanted buf=y" }
                elseif ($an -notmatch 'rdh=[1-9]') { $v = "answer row is [$an], wanted rdh above zero" }
            }
            $actual['nic-pass'] = $v
        }
        'nic-noread' {
            $cfg = Join-Path $Work 'DIAG-noread.CFG'
            [IO.File]::WriteAllText($cfg, "nicring listen=0`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-nicnoread' '' $cfg
            if (-not $k) { $actual['nic-noread'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'nic-noread' $k $k @('-e1000', '-e1000-nat')
                $an = Field (Get-DiagBlock $lines) '  m='
                Write-Host "  nic-noread: $an"
                # The whole point of the arm. Nothing polled, so nothing was
                # recycled, and the NAT's ARP reply must still be sitting in
                # descriptor 0 with its status and length written back.
                if (-not $an) { $actual['nic-noread'] = '(no nicring answer row)' }
                elseif ($an -notmatch 'ln=0') { $actual['nic-noread'] = "answer row is [$an], wanted ln=0 (the knob did not reach the stage)" }
                elseif ($an -notmatch 'wb=[1-9]') { $actual['nic-noread'] = "answer row is [$an], wanted wb above zero" }
                elseif ($an -notmatch 'buf=y') { $actual['nic-noread'] = "answer row is [$an], wanted buf=y" }
                else { $actual['nic-noread'] = $expected['nic-noread'] }
            }
        }
        'nic-nolink' {
            $k = New-Copy 'k-nicnolink.img'
            $lines = Invoke-Vm 'nic-nolink' $k $k @('-e1000-no-link')
            $v = Judge-Vm 'nic-nolink' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'ok'; nicinit = 'no-link'; nicring = 'quiet'; asde = 'no-link' }
            # The other half of the pair. This arm is the bed's version of the
            # ASUS signature from sitting 3 -- nicring quiet, nothing received --
            # and here it is quiet BECAUSE nothing arrived, which is what
            # gprc=0 says. A metal row reading quiet with gprc above zero is
            # therefore a different fault from anything this bed produces.
            if ($v -eq $expected['nic-nolink']) {
                $st = Field (Get-DiagBlock $lines) '  stats '
                if ($st -notmatch 'gprc=0\b') { $v = "stats row is [$st], wanted gprc=0" }
                # The positive half of the RDH pair. Every arm but nic-rdhro
                # must read rdh-writable=y, or that arm proves nothing: a field
                # stuck on one word passes its own falsifier and its own
                # control alike.
                $hd = Field (Get-DiagBlock $lines) '  link='
                if ($hd -notmatch 'rdh-writable=y') { $v = "head row is [$hd], wanted rdh-writable=y" }
                # The zero half of the gp= pair. nic-pass requires it above
                # zero on the same row; a field that only ever printed one
                # number would pass one of these two and not both.
                $an = Field (Get-DiagBlock $lines) '  m='
                if ($an -notmatch 'gp=0\b') { $v = "answer row is [$an], wanted gp=0" }
            }
            # And the negative direction for the hexdump: nothing arrived, so
            # nothing was written back and wb MUST be zero. An arm that reads wb
            # above zero here is counting our own buffer address, which is the
            # exact defect the 8..15 window exists to avoid.
            if ($v -eq $expected['nic-nolink']) {
                $an = Field (Get-DiagBlock $lines) '  m='
                if ($an -notmatch 'wb=0\b') { $v = "answer row is [$an], wanted wb=0" }
                elseif ($an -notmatch 'buf=y') { $v = "answer row is [$an], wanted buf=y" }
            }
            $actual['nic-nolink'] = $v
        }
        'nic-nomac' {
            $k = New-Copy 'k-nicnomac.img'
            $lines = Invoke-Vm 'nic-nomac' $k $k @('-e1000-no-mac')
            $actual['nic-nomac'] = Judge-Vm 'nic-nomac' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'ok'; nicinit = 'no-mac' }
        }
        'nic-nohpet' {
            $k = New-Copy 'k-nicnohpet.img'
            $lines = Invoke-Vm 'nic-nohpet' $k $k @('-e1000', '-e1000-nat', '-no-hpet')
            $v = Judge-Vm 'nic-nohpet' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'no-hpet'; nicinit = 'no-hpet'; nicring = 'no-hpet' }
            # The scene stage reads the same rate and keeps its state word, so
            # the state table above cannot see it. Its frame row is where it
            # says no, and a rendered scene that still claims a frame time
            # would mean the clockless path was never taken.
            if ($v -eq $expected['nic-nohpet']) {
                $frame = Field (Get-DiagBlock $lines) '  frame '
                if ($frame -notlike '*plain=no-clock*') { $v = "frame row is [$frame]" }
            }
            $actual['nic-nohpet'] = $v
        }
        'b3-pass' {
            $port = Get-FreePort
            $job = Start-Peer $port 'echo'
            $cfg = Join-Path $Work 'b3-pass.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15 expect=codex-diag-b3`n" -NoNewline
            $k = New-Variant 'b3-pass' '' $cfg
            if (-not $k) { $actual['b3-pass'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-pass' $k $k @('-e1000', '-e1000-nat')
                $v = Judge-Vm 'b3-pass' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'ok'; nicinit = 'ok'; nicring = 'frames'; b3 = 'ok' }
                # The mid-run bank is a claim the final file cannot carry,
                # because the ladder rewrites it when the stage returns. The
                # serial "entering" lines carry each note's write result, so
                # a run in which b3 banked nothing as it went is refused here
                # even though its rows are all correct.
                if ($v -eq $expected['b3-pass']) {
                    $notes = @($lines | Where-Object { $_ -match '^b3 entering .* banked=(-?\d+)$' })
                    $bad = @($notes | Where-Object { $_ -notmatch 'banked=[1-9]\d*$' })
                    if ($notes.Count -lt 20) { $v = "only $($notes.Count) b3 step notes on serial, wanted the clock, the seven reset operations, the six init-after-reset parts and the six steps from k1 to exchange" }
                    elseif ($bad.Count) { $v = "a b3 step note did not bank: [$($bad[0])]" }
                    else {
                        # Each note must APPEND: the bytes written grow with every
                        # step. A note that replaces the one before it writes the
                        # same length each time, which is how sitting 10 lost its
                        # clock row to the reset step (red, 2026-08-21).
                        $sizes = @($notes | ForEach-Object { [int]([regex]::Match($_, 'banked=(\d+)$').Groups[1].Value) })
                        for ($i = 1; $i -lt $sizes.Count; $i++) {
                            if ($sizes[$i] -le $sizes[$i - 1]) { $v = "b3 note $i did not append: banked=$($sizes[$i]) after banked=$($sizes[$i - 1])"; break }
                        }
                    }
                }
                $actual['b3-pass'] = $v
            }
            Stop-Peer $job
        }
        'nic-kills-msc' {
            # THE BED FOR THE CANDIDATE SITTING 11 NAMED: the xHCI/MSC path
            # dies when the I219 is brought up, both being on the PCH. Every
            # other medium-death lever here keys on a property of the WRITE
            # (its length, its LBA), so none of them can express a candidate
            # whose whole claim is that the write is innocent and its TIMING
            # is the fault. -usb-bot-die-on-nic keys on the OTHER DEVICE.
            #
            # ITS CONTROL IS b3-pass, which is this arm with the lever removed
            # and nothing else changed: same image, same DIAG.CFG shape, same
            # echo peer, same -e1000 -e1000-nat. There it banks the full trail
            # and every b3 note reports a growing banked=; here every one of
            # them reads banked=-1 and the summary says lost. Two arms whose
            # answers MOVE with the bed, which is what the ladder arms above
            # already had and the medium-death levers did not.
            #
            # AND THE ARM RECORDS A DISAGREEMENT WITH THE CANDIDATE, which is
            # the reason to read its rows rather than its verdict. Measured
            # 2026-08-21: the first observable of bring-up in this ladder is
            # RCTL.EN (not CTRL.SLU) and it is written by NICINIT, so the
            # medium dies at nicinit and the summary reads
            # `bank=lost at=nicinit`. Sitting 11 lost the bank at B3's
            # rings-link, with nicinit and nicring banked whole ahead of it.
            # So on metal the FIRST bring-up did not kill the medium and a
            # later one did, and the strict form of the candidate -- any I219
            # bring-up kills MSC -- is already refused by sitting 11's own
            # trail. What survives is the weaker and still useful form, that
            # SOME bring-up does. Do not read this arm's `at=nicinit` as the
            # metal shape reproduced; it is the candidate's own prediction,
            # and the gap between the two rows is the finding.
            $port = Get-FreePort
            $job = Start-Peer $port 'echo'
            $cfg = Join-Path $Work 'nic-kills-msc.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15 expect=codex-diag-b3`n" -NoNewline
            $k = New-Variant 'nic-kills-msc' '' $cfg
            if (-not $k) { $actual['nic-kills-msc'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'nic-kills-msc' $k $k @('-e1000', '-e1000-nat', '-usb-bot-die-on-nic')
                $block = Get-DiagBlock $lines
                # The lever's own two lines are on STDERR, not on the wire.
                # Asserting them is what separates "the medium died" from "the
                # medium died because this lever fired": without it a bank
                # death from any other cause would pass this arm.
                $errFile = Join-Path $Work 'nic-kills-msc.err'
                $vmErr = if (Test-Path $errFile) { [IO.File]::ReadAllText($errFile) } else { '' }
                $armed = ([regex]::Match($vmErr, 'die-on-nic: ARMED by (\S+);')).Groups[1].Value
                $pre = ($block | Where-Object { $_ -match 'before-deferred' } | Select-Object -First 1)
                $sum = ($block | Where-Object { $_ -match 'bank=' } | Select-Object -Last 1)
                $notes = @($lines | Where-Object { $_ -match '^b3 entering .* banked=(-?\d+)$' })
                $banked = @($notes | Where-Object { $_ -notmatch 'banked=-1$' })
                Write-Host "  nic-kills-msc: armed by $armed"
                Write-Host "  nic-kills-msc: $sum"
                $actual['nic-kills-msc'] =
                    if ($block.Count -eq 0) { '(no DIAG1 row on serial)' }
                    elseif (-not $armed) { 'the lever never armed: no ARMED line on codex-vm stderr, so nothing here is about the NIC' }
                    # Named rather than merely present. If the driver's order
                    # changes so CTRL.SLU comes first, that is a real move in
                    # the subject and this arm must say so rather than absorb
                    # it (the reading it records is RCTL.EN, from nicinit).
                    elseif ($armed -ne 'RCTL.EN') { "the first bring-up observable is now [$armed], not RCTL.EN: re-derive which stage writes it before trusting at= below" }
                    elseif ($vmErr -notmatch 'die-on-nic: target DIED') { 'the lever armed and never fired: no bulk write followed bring-up, so the medium was never killed' }
                    elseif ($sum -notmatch 'bank=lost') { "summary row is [$sum], wanted bank=lost" }
                    elseif ($sum -notmatch 'at=nicinit') { "summary row is [$sum], wanted at=nicinit (the first bring-up is nicinit's; see this arm's note on sitting 11)" }
                    # THE HALF THAT MAKES IT SITTING 11'S SHAPE rather than a
                    # plainly dead medium: the NIC conversation completes with
                    # the medium gone underneath it. b3 reaching ok here is
                    # what says the two subsystems failed independently.
                    elseif ((Field $block 'stage=b3 ') -notmatch 'state=ok') { "b3 row is [$(Field $block 'stage=b3 ')], wanted state=ok (the TCP exchange must survive the medium)" }
                    elseif ((Field $block 'stage=nicring ') -notmatch 'state=frames') { "nicring row is [$(Field $block 'stage=nicring ')], wanted state=frames" }
                    elseif ($notes.Count -lt 12) { "only $($notes.Count) b3 step notes on serial, wanted the clock, the seven reset operations and the bring-up steps" }
                    # Refused, not silently dropped. banked=-1 on every note is
                    # the driver reporting the write failed; a note that still
                    # reported a byte count would mean the death was invisible
                    # to the payload, which is a different reading entirely.
                    elseif ($banked.Count) { "a b3 step note banked after the medium died: [$($banked[0])]" }
                    else { $expected['nic-kills-msc'] }
            }
            Stop-Peer $job
        }
        'b3-noreply' {
            $port = Get-FreePort
            $job = Start-Peer $port 'silent'
            $cfg = Join-Path $Work 'b3-noreply.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15`n" -NoNewline
            $k = New-Variant 'b3-noreply' '' $cfg
            if (-not $k) { $actual['b3-noreply'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-noreply' $k $k @('-e1000', '-e1000-nat') 90
                $actual['b3-noreply'] = Judge-Vm 'b3-noreply' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'ok'; nicinit = 'ok'; nicring = 'frames'; b3 = 'no-reply' }
            }
            Stop-Peer $job
        }
        'b3-short' {
            # The send side saying so. sendx= repeats the send, one queue slot
            # apiece, against a peer that ACCEPTS AND NEVER READS: its socket
            # buffer fills, the NAT stops acking, and our retransmit queue
            # fills at net-rexmit-capacity.
            $port = Get-FreePort
            $job = Start-Peer $port 'silent'
            $cfg = Join-Path $Work 'b3-short.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15 sendx=8192`n" -NoNewline
            $k = New-Variant 'b3-short' '' $cfg
            if (-not $k) { $actual['b3-short'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-short' $k $k @('-e1000', '-e1000-nat') 180
                $actual['b3-short'] = Judge-Vm 'b3-short' $lines $k $true '' @{ b3 = 'short' }
            }
            Stop-Peer $job
        }
        'b3-refused' {
            # No listener at all on a port nothing is bound to: the NAT's own
            # connect fails and the handshake never completes.
            $port = Get-FreePort
            $cfg = Join-Path $Work 'b3-refused.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15`n" -NoNewline
            $k = New-Variant 'b3-refused' '' $cfg
            if (-not $k) { $actual['b3-refused'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-refused' $k $k @('-e1000', '-e1000-nat') 90
                $actual['b3-refused'] = Judge-Vm 'b3-refused' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'ok'; nicinit = 'ok'; nicring = 'frames'; b3 = 'refused' }
            }
        }
        'b3-nopart' {
            # A peer IS named and there is no card. The stage answers the part
            # question rather than the peer one, which is the only arm that
            # reaches past the no-peer short circuit every other arm takes.
            $cfg = Join-Path $Work 'b3-nopart.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:9300 ip=10.0.2.15`n" -NoNewline
            $k = New-Variant 'b3-nopart' '' $cfg
            if (-not $k) { $actual['b3-nopart'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-nopart' $k $k @()
                $actual['b3-nopart'] = Judge-Vm 'b3-nopart' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part'; b3 = 'no-part' }
            }
        }
        'asde-differs' {
            $k = New-Variant 'k-asdediff' '' (New-StageOnCfg 'asde')
            if (-not $k) { $actual['asde-differs'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'asde-differs' $k $k @('-e1000', '-e1000-nat', '-e1000-phy-link', '-e1000-asde') 90
                $actual['asde-differs'] = Judge-Vm 'asde-differs' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; asde = 'differs' }
            }
        }
        'asde-ctrlro' {
            $k = New-Variant 'k-asdectrlro' '' (New-StageOnCfg 'asde')
            if (-not $k) { $actual['asde-ctrlro'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'asde-ctrlro' $k $k @('-e1000', '-e1000-nat', '-e1000-ctrl-ro') 90
                $actual['asde-ctrlro'] = Judge-Vm 'asde-ctrlro' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; asde = 'ctrl-ro' }
            }
        }
        'b3-noaddr' {
            $cfg = Join-Path $Work 'b3-noaddr.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:9300
" -NoNewline
            $k = New-Variant 'b3-noaddr' '' $cfg
            if (-not $k) { $actual['b3-noaddr'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-noaddr' $k $k @('-e1000', '-e1000-nat')
                $actual['b3-noaddr'] = Judge-Vm 'b3-noaddr' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; b3 = 'no-address' }
            }
        }
        'nic-rdhro' {
            # NIC-4's own falsifier. -e1000-rdh-ro drops the driver's write to
            # RDH the way CTRL is dropped on the I219-V, so the stage's
            # write-7-and-read-back answers n. Nothing else in the suite can
            # produce that word, and until this arm existed the metal reading
            # would have been the FIRST time the n branch ever ran.
            $k = New-Copy 'k-nicrdhro.img'
            $lines = Invoke-Vm 'nic-rdhro' $k $k @('-e1000', '-e1000-nat', '-e1000-rdh-ro')
            $v = Judge-Vm 'nic-rdhro' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok' }
            if ($v -eq $expected['nic-rdhro']) {
                $hd = Field (Get-DiagBlock $lines) '  link='
                if ($hd -notmatch 'rdh-writable=n') { $v = "head row is [$hd], wanted rdh-writable=n" }
            }
            $actual['nic-rdhro'] = $v
        }
        'sink-dies' {
            # The metal shape, reached by an INTRINSIC key. bank-lost beside
            # this one reaches bank=lost at=sink too, but by wedging the
            # medium from transfer 950 onward -- an ordinal, with a band this
            # file already warns has to be re-derived whenever the stage list
            # moves. This arm keys on the command's own length AND its own
            # LBA, neither of which anything upstream can shift.
            #
            # Censused 2026-08-21 (-usb-bot-census): the bank's cycle is six
            # writes per banked row, 32768 at lba 2049, 20480 at 2113, 32768
            # at 2153, 20480 at 2217, 512 at 2257, plus its file data at
            # 2560..3584 bytes around 3475..3541. The sink is the tail: sixty
            # 32768-byte writes, 3548..7324. So len>=32768 AND lba>=3000
            # catches the sink and nothing else, with the gap from 2153 to
            # 3548 as margin.
            #
            # THE `before-deferred bank=ok` ROW IS HALF THE READING. It says
            # the run was healthy up to the write that killed it, which is
            # what distinguishes this from a medium that was never usable.
            $k = New-Copy 'k-sinkdies.img'
            $lines = Invoke-Vm 'sink-dies' $k $k @('-usb-bot-die-len', '32768', '-usb-bot-die-lba', '3000')
            $block = Get-DiagBlock $lines
            $pre = ($block | Where-Object { $_ -match 'before-deferred' } | Select-Object -First 1)
            $sum = ($block | Where-Object { $_ -match 'bank=' } | Select-Object -Last 1)
            Write-Host "  sink-dies: $pre"
            Write-Host "  sink-dies: $sum"
            $actual['sink-dies'] =
                if ($block.Count -eq 0) { '(no DIAG1 row on serial)' }
                elseif ($pre -notmatch 'bank=ok') { "before-deferred row is [$pre], wanted bank=ok (the run must be healthy up to the sink)" }
                elseif ($sum -notmatch 'bank=lost') { "summary row is [$sum], wanted bank=lost" }
                elseif ($sum -notmatch 'at=sink') { "summary row is [$sum], wanted at=sink" }
            elseif ((Field $block 'stage=sink ') -notmatch 'state=died') { "sink row is [$(Field $block 'stage=sink ')], wanted state=died (the target stopped answering; a refusal would have COMPLETED)" }
                else { $expected['sink-dies'] }
        }
        'xhci-two' {
            # The bed has modelled both of the ASUS's controllers for some
            # time and NOTHING HAS EVER RUN IT. A device model no arm
            # exercises is a model nobody has shown works, which is
            # L-UNCALLED one level out from the code it was built to test.
            #
            # What this arm holds is that the walk REACHES ordinal 1 and
            # brings it up. It cannot hold the other half -- that the walk
            # stops early once everything is found -- because usb-found-all
            # wants a keyboard AND a mouse AND a disk, and this bed has no
            # mouse model at all, so the walk never short-circuits here
            # whatever else is present. That is measured, not assumed, and
            # it is why -xhci-no-disk changes nothing about which
            # controllers come up.
            $k = New-Copy 'k-xhcitwo.img'
            $lines = Invoke-Vm 'xhci-two' $k $k @('-xhci-two')
            $v = Judge-Vm 'xhci-two' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok' }
            if ($v -eq $expected['xhci-two']) {
                $blk = Get-DiagBlock $lines
                $c = Field $blk '  ctls='
                $r = Field $blk '  ctl1 '
                if ($c -notmatch 'ctls=2') { $v = "controller count row is [$c], wanted ctls=2" }
                elseif (-not $r) { $v = '(no ctl1 row: the second controller was never enumerated)' }
                elseif ($r -notmatch '1b21:1242') { $v = "ctl1 row is [$r], wanted the ASMedia 1b21:1242" }
                elseif ($r -notmatch 'running') { $v = "ctl1 row is [$r], wanted running -- the walk reached it and did not bring it up" }
            }
            $actual['xhci-two'] = $v
        }
        'nic-invisible' {
            # DiagNicRing's middle row, "frames arrived and we cannot see
            # them" (:124), and until 2026-08-21 nothing here could produce
            # it. Two separate reasons, and both had to go:
            #
            # WHAT the MAC does. GPRC was counted after the descriptor
            # writeback, so every fault returned above it and read gprc=0,
            # which is the row ABOVE this one. Fixed at main 18747.
            #
            # WHEN the frame arrives. The injector empties its budget at the
            # RCTL.EN write, which happens in nicinit, so the frame landed in
            # the pre-stage window and the stage read pre=1 gp=0 -- the
            # two-read design working exactly as intended. -e1000-inject-armed
            # holds the frames until the guest READS GPRC, which is this
            # stage's own opening action, so what arrives is inside its window
            # by construction. Keyed on that read rather than on a count of
            # RDT writes deliberately: -e1000-inject-late 2 also works and is
            # an ORDINAL, the defect this file documents for -usb-bot-drop N,
            # and the band was one value wide.
            $k = New-Copy 'k-nicinvisible.img'
            $lines = Invoke-Vm 'nic-invisible' $k $k @('-e1000', '-e1000-nat', '-i219', '-e1000-inject', '1', '-e1000-inject-armed')
            $v = Judge-Vm 'nic-invisible' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok' }
            if ($v -eq $expected['nic-invisible']) {
                $an = Field (Get-DiagBlock $lines) '  m='
                if ($an -notmatch 'pre=0\b') { $v = "answer row is [$an], wanted pre=0 (the frame must arrive INSIDE this stage)" }
                elseif ($an -notmatch 'gp=[1-9]') { $v = "answer row is [$an], wanted gp above zero" }
                elseif ($an -notmatch 'rdh=0\b') { $v = "answer row is [$an], wanted rdh=0 (nothing written back)" }
                elseif ($an -notmatch 'dd=0\b') { $v = "answer row is [$an], wanted dd=0" }
            }
            $actual['nic-invisible'] = $v
        }
        'nic-armed' {
            # The positive control, and it is the half that makes the other
            # arm mean anything. Same frame, same armed timing, no stall: gp
            # comes back above zero HERE TOO and the frame is seen. So gp on
            # its own separates neither, and what distinguishes the two rows
            # is whether the ring shows the frame.
            $k = New-Copy 'k-nicarmed.img'
            $lines = Invoke-Vm 'nic-armed' $k $k @('-e1000', '-e1000-nat', '-e1000-inject', '1', '-e1000-inject-armed')
            $v = Judge-Vm 'nic-armed' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicring = 'frames' }
            if ($v -eq $expected['nic-armed']) {
                $an = Field (Get-DiagBlock $lines) '  m='
                if ($an -notmatch 'pre=0\b') { $v = "answer row is [$an], wanted pre=0" }
                elseif ($an -notmatch 'gp=[1-9]') { $v = "answer row is [$an], wanted gp above zero" }
                elseif ($an -notmatch 'rdh=[1-9]') { $v = "answer row is [$an], wanted rdh above zero (the frame IS visible here)" }
            }
            $actual['nic-armed'] = $v
        }
        'nic-k1-off' {
            # The K1 control, and the reason nic-armed is not one. nic-armed
            # gets its "no stall" by dropping -i219, which changes device
            # identity and every branch i219_present gates -- i219_mac_stalled
            # returns 0 on its FIRST line there, before K1 is looked at. So it
            # answers whether the bed delivers anything, not whether K1 gates
            # delivery. Here the part IS an I219 and the only thing that moves
            # is K1 at power-up, so DD landing here and not in nic-invisible
            # names the stall as the difference between the two rows.
            $k = New-Copy 'k-nick1off.img'
            $lines = Invoke-Vm 'nic-k1-off' $k $k @('-e1000', '-e1000-nat', '-i219', '-i219-k1-nvm', '0', '-e1000-inject', '1', '-e1000-inject-armed')
            $v = Judge-Vm 'nic-k1-off' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicring = 'frames' }
            if ($v -eq $expected['nic-k1-off']) {
                $an = Field (Get-DiagBlock $lines) '  m='
                if ($an -notmatch 'pre=0\b') { $v = "answer row is [$an], wanted pre=0 (the frame must arrive INSIDE this stage)" }
                elseif ($an -notmatch 'gp=[1-9]') { $v = "answer row is [$an], wanted gp above zero" }
                elseif ($an -notmatch 'rdh=[1-9]') { $v = "answer row is [$an], wanted rdh above zero (K1 off at power-up is no stall, so the frame IS visible)" }
            }
            $actual['nic-k1-off'] = $v
        }
        'b3-dhcp' {
            $port = Get-FreePort
            $job = Start-Peer $port 'echo'
            $cfg = Join-Path $Work 'b3-dhcp.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=dhcp expect=codex-diag-b3`n" -NoNewline
            $k = New-Variant 'b3-dhcp' '' $cfg
            if (-not $k) { $actual['b3-dhcp'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-dhcp' $k $k @('-e1000', '-e1000-nat') 90
                $v = Judge-Vm 'b3-dhcp' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'ok'; nicinit = 'ok'; nicring = 'frames'; b3 = 'ok' }
                # b3=ok is not the assertion on its own: b3-pass reaches it too,
                # and the bed leases the very address the deleted default used
                # to invent. The lease facts are the half a guess cannot forge.
                if ($v -eq $expected['b3-dhcp']) {
                    $blob = ($lines -join "`n")
                    if ($blob -notmatch 'addr=dhcp ip=10\.0\.2\.15 gw=10\.0\.2\.2') { $v = 'b3 row carries no addr=dhcp lease line' }
                }
                $actual['b3-dhcp'] = $v
            }
            Stop-Peer $job
        }
        'b3-nolease' {
            # -e1000 alone: the card is there and the link is up, and with no
            # NAT nothing serves DHCP. The peer is never dialled, so the port
            # need not exist.
            $cfg = Join-Path $Work 'b3-nolease.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:9300 ip=dhcp`n" -NoNewline
            $k = New-Variant 'b3-nolease' '' $cfg
            if (-not $k) { $actual['b3-nolease'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-nolease' $k $k @('-e1000') 90
                $actual['b3-nolease'] = Judge-Vm 'b3-nolease' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; b3 = 'no-lease' }
            }
        }
        'b3-clockstuck' {
            # -hpet-frozen reads the HPET window as all-ones, the undecoded
            # shape: period 0xFFFFFFFF derives a bogus but NONZERO rate, so every
            # clocked wait takes the clocked path against a counter that never
            # moves, bounded only by its read fuel. b3's clock control at entry
            # reads the counter across 100000 reads and refuses before bring-up.
            # nicsit, nicinit and nicring are turned off by this arm's own cfg,
            # because nicinit's link wait would spend 409 million STATUS reads
            # against the same stuck clock before b3 ever ran. The arm is also
            # the first to rehearse a cfg-off composition on a New-Variant arm,
            # so the three skipped rows are asserted outright rather than
            # substituted.
            $cfg = Join-Path $Work 'b3-clockstuck.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:9300 ip=10.0.2.15`nnicsit off`nnicinit off`nnicring off`n" -NoNewline
            $k = New-Variant 'b3-clockstuck' '' $cfg
            if (-not $k) { $actual['b3-clockstuck'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-clockstuck' $k $k @('-e1000', '-hpet-frozen') 90
                $v = Judge-Vm 'b3-clockstuck' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicsit = 'skipped'; nicinit = 'skipped'; nicring = 'skipped'; b3 = 'clock-stuck' }
                # The refusal must carry its own reading: the control is the
                # counter, not the rate, and a row that said clock-stuck with
                # clk=y would be a verdict with no measurement under it.
                if ($v -eq $expected['b3-clockstuck']) {
                    $row = Field (Get-DiagBlock $lines) '  clk='
                    if ($row -notlike '  clk=n *') { $v = "clock row is [$row], wanted clk=n" }
                }
                $actual['b3-clockstuck'] = $v
            }
        }
        'b3-banklost' {
            # SITTING 11's SHAPE: the medium stopped taking writes INSIDE b3's
            # bring-up and the ladder only noticed at pchk1's whole-stage write,
            # so the glass named the stage after the death. The step's own note
            # already answers -1 when its write is refused or its size readback
            # disagrees (DiagStage diag-note-bytes); this arm is the bed for the
            # step SAYING so where it happens. The death is keyed to the thing
            # under test and not to an ordinal: every bank rewrite takes a fresh
            # cluster (census: lba 3489, 3494, 3500, ... climbing) and its length
            # steps with the file, so -usb-bot-die-len 5632 at lba >= 3400 is
            # the first ELEVEN-SECTOR file write.
            #
            # RE-DERIVED 2026-08-24 (blu), AND THE KEY HAD 20 BYTES OF MARGIN.
            # Eleven sectors begins at 5121 bytes. This block used to say the
            # first eleven-sector write was the SEVENTH note, reset-imc-again at
            # 5141 bytes -- twenty bytes above the boundary. Measured off the
            # dying medium today that same note lands at 5107, a 34-byte drift,
            # which makes it a TEN-sector write; the first eleven-sector write is
            # now the EIGHTH note, reset-icr, and the medium ends at
            # reset-imc-again. The arm went red on main's own shipping diag.img
            # (1190FD4C), the image the ledger certified at arms=46 on
            # 2026-08-22, with neither the image nor this script changed since.
            # The bank size also drifts run to run (5106, 5107, 5108 measured
            # across three runs of the same image), so the margin was only ever
            # a few notes' worth of digits in the heap= and hpet= fields.
            #
            # THE EXPECTED NOTE IS DERIVED FROM THE TRAIL, NOT NAMED (taken
            # 2026-08-25 at the Update 50 release, red, after the re-pinned
            # literal went red a second time in one day: the bank size drifts
            # run to run, so a fixed note name sits at the edge of a working
            # band by construction, the header's own sink-drop lesson). The
            # arm requires the first refused note to be one of b3's reset-*
            # steps and the medium's last note to be the step immediately
            # before it ON SERIAL. Every falsifiable claim stands: the death
            # lands inside the reset sequence, the step says so the moment
            # its note is refused, the banked flags flip exactly at the
            # death, and the glass ordinal matches the serial trail. What is
            # no longer asserted is WHICH reset step dies, which was never
            # the arm's subject.
            #
            # The bank's FAT and directory writes
            # sit at 2049 and 2153, below the key, and the sink is deferred last.
            $port = Get-FreePort
            $job = Start-Peer $port 'echo'
            $cfg = Join-Path $Work 'b3-banklost.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15 expect=codex-diag-b3`n" -NoNewline
            $k = New-Variant 'b3-banklost' '' $cfg
            if (-not $k) { $actual['b3-banklost'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'b3-banklost' $k $k @('-e1000', '-e1000-nat', '-usb-bot-die-len', '5632', '-usb-bot-die-lba', '3400') 240
                $block = Get-DiagBlock $lines
                $notes = @($lines | Where-Object { $_ -match '^b3 entering (.+?) (ctrl=\d+ |settled=\d+ )?heap=\d+ banked=(-?\d+)$' })
                $lost = @($lines | Where-Object { $_ -match '^b3 bank lost at (.+)$' })
                $sum = ($block | Where-Object { $_ -match 'bank=' } | Select-Object -Last 1)
                $file = Read-Bank $k 'b3-banklost'
                $onMedium = @()
                if ($null -ne $file) { $onMedium = @($file | Where-Object { $_ -like 'stage=b3 step=*' }) }
                $lastOnMedium = if ($onMedium.Count) { [regex]::Match($onMedium[-1], '^stage=b3 step=(\S+)').Groups[1].Value } else { '' }
                Write-Host "  b3-banklost: lost=$($lost -join ' | ')"
                Write-Host "  b3-banklost: $sum"
                Write-Host "  b3-banklost: last b3 note on the medium: $lastOnMedium"
                $v = $null
                if ($block.Count -eq 0) { $v = '(no DIAG1 row on serial)' }
                elseif ($block[-1] -ne 'END') { $v = "(serial did not reach END; last: $($block[-1]))" }
                elseif (-not $lost.Count) { $v = 'no "b3 bank lost at" line: the step never said its note was refused' }
                else {
                    $first = [regex]::Match($lost[0], '^b3 bank lost at (.+)$').Groups[1].Value
                    $firstIdx = -1
                    for ($i = 0; $i -lt $notes.Count; $i++) { if ([regex]::Match($notes[$i], '^b3 entering (.+?) (ctrl=\d+ |settled=\d+ )?heap=\d+ banked=(-?\d+)$').Groups[1].Value -eq $first) { $firstIdx = $i; break } }
                    if ($first -notlike 'reset-*') { $v = "the first refused note is [$first], not one of the reset-* steps: the die landed outside the b3 reset sequence, re-derive the key from the census" }
                    elseif ($firstIdx -eq -1) { $v = "no b3 entering line for [$first]" }
                    elseif ($firstIdx -eq 0) { $v = "the first refused note [$first] is the first b3 note on serial, so no healthy note precedes the death" }
                    else {
                        # Every note before the first refusal banked; every note from it on was refused.
                        $seen = $false
                        foreach ($n in $notes) {
                            $m = [regex]::Match($n, '^b3 entering (.+?) (ctrl=\d+ |settled=\d+ )?heap=\d+ banked=(-?\d+)$')
                            $step = $m.Groups[1].Value; $banked = [int]$m.Groups[3].Value
                            if ($step -eq $first) { $seen = $true }
                            if (-not $seen -and $banked -le 0) { $v = "note [$n] was refused BEFORE the first bank-lost line"; break }
                            if ($seen -and $banked -gt 0) { $v = "note [$n] banked AFTER the medium died: the death is not latched or the readback lied"; break }
                        }
                        if (-not $v -and -not $seen) { $v = "no b3 entering line for [$first]" }
                        if (-not $v -and $notes.Count -lt 12) { $v = "only $($notes.Count) b3 step notes on serial" }
                        $prevStep = [regex]::Match($notes[$firstIdx - 1], '^b3 entering (.+?) (ctrl=\d+ |settled=\d+ )?heap=\d+ banked=(-?\d+)$').Groups[1].Value
                        if (-not $v -and $lastOnMedium -ne $prevStep) { $v = "the medium's last b3 note is [$lastOnMedium], wanted [$prevStep], the step before the refused one on serial" }
                        if (-not $v -and $sum -notmatch 'bank=lost') { $v = "bank row is [$sum], wanted bank=lost" }
                        if (-not $v -and $sum -notmatch 'at=b3') { $v = "bank row names [$sum]; the ladder should have noticed at b3, the stage that died" }
                        # THE SLOT PAINT IS TRANSIENT: dg-paint-result overwrites it with
                        # b3's own row when the stage returns, which on sitting 11 it did.
                        # So b3's FIRST row (the one the slot keeps and the QR carries) is
                        # stamped with the ORDINAL of the first refused note, counted the
                        # way the serial trail counts them; the stick's trail then ends at
                        # note N-1 and the glass says N, and a medium that ACCEPTED a write
                        # and lost it shows as a gap between the two.
                        if (-not $v) {
                            $ord = 0
                            for ($i = 0; $i -lt $notes.Count; $i++) { if ($notes[$i] -match '^b3 entering (.+?) (ctrl=\d+ |settled=\d+ )?heap=\d+ banked=-1$') { $ord = $i + 1; break } }
                            # On serial the stage row is `stage=b3 state=...` and its first
                            # glass line is the indented line after it.
                            $row = ''
                            for ($i = 0; $i -lt $block.Count - 1; $i++) { if ($block[$i] -match '^stage=b3 ') { $row = $block[$i + 1].Trim(); break } }
                            if ($row -notmatch " bank-lost-note=$ord(\s|$)") { $v = "b3 first row is [$row], wanted bank-lost-note=$ord, the ordinal of the first refused note on serial" }
                        }
                        if (-not $v) { $v = $expected['b3-banklost'] }
                    }
                }
                $actual['b3-banklost'] = $v
            }
            Stop-Peer $job
        }
        'k1-taken' {
            # BOTH K1 ARMS NAME A PEER, and that is not decoration. The K1 write
            # lives in e1000-init, which this ladder reaches only through
            # net-driver-bring-up in b3, and b3 short-circuits on no-peer BEFORE
            # bring-up. Without a peer nothing writes K1 and the stage reads the
            # NVM value: measured, this arm read not-taken until it dialled.
            # That is the gap the stage exists for, reproduced in the bed.
            $port = Get-FreePort
            $job = Start-Peer $port 'echo'
            # THE LISTEN AFTER THE WRITE rides this arm and its falsifier below
            # (red, 2026-08-21): one armed frame, released by the guest's first
            # GPRC read. nicring is turned off by this arm's cfg so that read is
            # pchk1's own and the frame lands inside ITS window; with K1 written
            # (taken) there is no stall and the driver's poll returns the frame,
            # arrived-visible.
            $cfg = Join-Path $Work 'k1-taken.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15 expect=codex-diag-b3`nnicring off`n" -NoNewline
            $k = New-Variant 'k1-taken' '' $cfg
            if (-not $k) { $actual['k1-taken'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'k1-taken' $k $k @('-e1000', '-e1000-nat', '-i219', '-e1000-inject', '1', '-e1000-inject-armed') 90
                $v = Judge-Vm 'k1-taken' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicring = 'skipped'; pchk1 = 'taken' }
                if ($v -eq $expected['k1-taken']) {
                    $ls = Field (Get-DiagBlock $lines) '  listen-after-k1 '
                    if ($ls -notmatch 'word=arrived-visible') { $v = "listen row is [$ls], wanted arrived-visible" }
                    elseif ($ls -notmatch 'gprc-after=[1-9]') { $v = "listen row is [$ls], wanted the MAC to count the frame inside the window" }
                }
                $actual['k1-taken'] = $v
            }
            Stop-Peer $job
        }
        'k1-blocked' {
            # The falsifier, and one variable from the arm above: same image,
            # same peer, same flags plus -i219-mng-holds. Firmware holds MNG, the
            # semaphore cannot be acquired and MDIO is refused, so the write is
            # ATTEMPTED and blocked rather than never reached. An arm that can
            # only ever read taken is not an instrument.
            $port = Get-FreePort
            $job = Start-Peer $port 'echo'
            # The same armed frame here lands with K1 NOT written (MNG holds),
            # so the part stalls: the MAC counts it, no DD lands, the driver's
            # poll sees nothing. arrived-invisible, off arrived-visible above
            # with K1 the only difference, which is the campaign's claim said by
            # the bed.
            $cfg = Join-Path $Work 'k1-blocked.cfg'
            Set-Content $cfg "b3 peer=10.0.2.2:$port ip=10.0.2.15 expect=codex-diag-b3`nnicring off`n" -NoNewline
            $k = New-Variant 'k1-blocked' '' $cfg
            if (-not $k) { $actual['k1-blocked'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'k1-blocked' $k $k @('-e1000', '-e1000-nat', '-i219', '-i219-mng-holds', '-e1000-inject', '1', '-e1000-inject-armed') 90
                $v = Judge-Vm 'k1-blocked' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; gopmode = 'honoured'; block = 'ok'; xhci = 'running'; sink = 'ok'; nicring = 'skipped'; pchk1 = 'no-mdio' }
                if ($v -eq $expected['k1-blocked']) {
                    $ls = Field (Get-DiagBlock $lines) '  listen-after-k1 '
                    if ($ls -notmatch 'word=arrived-invisible') { $v = "listen row is [$ls], wanted arrived-invisible (the stall with K1 unwritten)" }
                }
                $actual['k1-blocked'] = $v
            }
            Stop-Peer $job
        }
        'ovmf' { $actual['ovmf'] = Invoke-Ovmf 'ovmf' $false }
        'ovmf-ro' { $actual['ovmf-ro'] = Invoke-Ovmf 'ovmf-ro' $true }
    }
}

$bad = 0
Write-Host ''
Write-Host 'arm        expected                                        actual'
Write-Host '---------  ----------------------------------------------  ------'
foreach ($name in $names) {
    $e = $expected[$name]; $a = $actual[$name]
    if ($a -ne $e) { $bad++ }
    $mark = if ($a -eq $e) { 'ok' } else { "MISMATCH: $a" }
    Write-Host ("{0,-10} {1,-46}  {2}" -f $name, $e, $mark)
}
Write-Host ''
if (-not $Keep) { Remove-Item (Join-Path $Work '*.img'), (Join-Path $Work '*.efi') -Force -ErrorAction SilentlyContinue }
if ($bad -gt 0) { Write-Host "DIAG LADDER NOT REHEARSED: $bad arm(s) disagree"; exit 1 }
Write-Host 'Diag ladder rehearsed: every arm answered as it should.'

# The rehearsal record (L-REHEARSE): flash-usb.ps1 -Rehearsed refuses any image
# whose SHA-256 is not on this list. Only a FULL run writes it -- every arm,
# both beds -- because "boot-and-read green" is not "mission green"; -Only and
# -SkipOvmf runs are dev loops and leave the record alone, and say so.
$record = Join-Path $Repo 'build\boot\diag.rehearsed'
if ($Only -or $SkipOvmf) {
    Write-Host "  (partial rehearsal: $(if ($Only) { "-Only $Only" } else { '-SkipOvmf' }); $record NOT updated -- run every arm to make this image flashable)"
} else {
    $imgHash = (Get-FileHash $ImgAbs -Algorithm SHA256).Hash
    $line = "$imgHash  $([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))  arms=$($names.Count)  $(Split-Path $ImgAbs -Leaf)"
    $have = @(); if (Test-Path $record) { $have = @(Get-Content $record) }
    # Keyed on the arm count as well as the hash. The record's claim is that
    # this image answered every arm, and the arm count is part of that claim,
    # so an image rehearsed against a WIDER bed than last time earns a new
    # line rather than being told it is already there.
    if ($have | Where-Object { $_ -like "$imgHash *arms=$($names.Count) *" }) { Write-Host "  rehearsal record already carries $imgHash at arms=$($names.Count)" }
    else {
        [IO.File]::AppendAllText($record, $line + "`n", [Text.ASCIIEncoding]::new())
        Write-Host "  rehearsal record: $record += $imgHash (flash-usb.ps1 -Rehearsed accepts this image now)"
    }
}
exit 0
