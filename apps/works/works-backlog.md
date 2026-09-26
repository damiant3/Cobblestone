# Works -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

**`works-desk-contract.md` is the standing rules for the desk** -- the `ds`
cell map and the rule about pointers in it, the base heap mark and how a pane
exits, the cursor bracket, why the palette arrives as a parameter. Read it
before adding a pane or taking a cell. This file is what is missing; that one
is what must not be broken.

## WORKS-68: a light pane's open state is freed by the next root rebuild

`desk-root-reclaim` frees back to where the current root began unless a LIVE
mark sits at or above where it ended, so an `-open` that builds state above
the current root and pushes no mark loses it at the next rebuild (a menu
dismiss, a hide, a pill restore) while `desk-loop` still carries it; with no
marks at all, any other window's close restores to the base mark and takes it
too.

**Sheets is FIXED 2026-09-23 (val)**: `desk-sheet-open` evicts the Browser and
pushes its own mark, as `desk-edit-open` does. Measured at 1600x900 on seed
1E936E74D45C317D, each arm one boot on head before and after: Sheets then the
menu opened and dismissed faulted (`!EXC=0e` in `gsh-tree` under
`desk-sheet-step`) and now renders the sheet and hit-tests a click; Sheets,
then the Clock opened and closed over it, drew an EMPTY sheet body and now
draws the sheet; close and reopen still returns to the desktop and back.

**`desk-rev-open` has the same shape and NO ARM CAN REACH IT** (2026-09-23,
val). It builds `grv-load`'s data above the root with no mark, but nothing
rebuilds the root while Review is live: `desk-root-reclaim` runs only from
the three close paths, `desk-menu-dismiss`, `desk-app-hide`,
`desk-pill-restore` and the Edit and 3D re-entries; `desk-rev-step` answers
only 1, 0 or -1, never hide or dismiss; Review is not in the window registry,
so `desk-wnd-to-desk` routes no click past it (two pill clicks over it
changed nothing on the glass); and its own close drops `da-review`. It
becomes live the day Review is a window or its step answers hide or dismiss,
and then it pushes a mark as Sheets does.

**The arm.** `tools/codex-vm.exe` with `desk.ps1`'s arguments, `-disk` a copy
of `seed/Codex.img`, `-mouse-file`, `-headless -screenshot`, stderr to a file:
`build/desk.ps1` starts the guest with no redirection, so a crash report goes
to a console nobody reads and the pane reads as frozen. Moves in samples of
at most 28 px. The recipe and the menu geometry are `ShellRefinement.md`,
"How to drive the desk for a reading"; a 32 s screenshot delay captures.

**Not established, and the same family:** `gsc-place` stores a fresh
`R3dTriState` into `sp-tgt` on every step, inside `desk-loop`'s iteration
frame, so between iterations `sp-tgt` names freed memory. Nothing reads it
there (the step rebuilds it before `gsc-frame` does), and it is the remark
obligation `works-desk-contract.md` states, unkept by the 3D panes.

## WORKS-59: `desk-root-reclaim` must not free a live pane's state

A mark records where a state WAS, so a reclaim decides on the address the
state left rather than the address it holds now, and only a mark whose pane
is dead may be reclaimed. Breaking this halts the guest on the second pill
restore with `!EXC=06`, rendering nothing at all rather than a bad frame.
`codex/test/desk-root-guard` pins the decision and the BVT runs it;
`GopDesk.codex` cites this at its mark stack.

## WORKS-58: a desktop rebuild frees the root it replaced

Minimise and pill restore each rebuild the desktop root, and a rebuild frees
the root it replaced, so the frontier is FLAT in the number of cycles rather
than climbing by one root per cycle. `works-desk-contract.md` cites this for
`ds` cells 224 and 228, `desk-root-cell` and `desk-rootend-cell`, which hold
the frontier immediately before and immediately after the current root.

## WORKS-57: a window close keeps every surviving pane's state

`desk-wnd-close-to` kills the closing pane's entry, reclaims dead entries
from the top, and restores to the reclaimed target or to nothing when a live
entry sits above; it does not hand down `desk-apps-empty`.
`desk-pill-restore` dispatches to the pane's own `-focus`, which RE-ENTERS a
live pane and opens a dead one, and that re-entry is not optional: without it
a close leaves a bare desktop and a restore paints an empty window.
`GopDesk.codex` cites this in three places.

## WORKS-56: the band's cached depth covers a band holding a pill

`dk-task-init` measures the band and a one-specimen strip separately and
caches the LARGER, so the content box reserves the depth the band can need
whatever the window state: 36 at 1280 and 72 at 1600, band empty equal to
band with a pill. The measure runs at boot and on an edge change and never
again, so a cache taken from the empty band under-reserves by 16 device
pixels the moment the first window is minimised, and the bottom of the
content box sits under the band. `codex/test/apps/desk-pane-origin` asserts
containment with an empty registry and with one minimised window; an
under-reserving cache turns `slot inside box` and `equal` to NO.

## WORKS-48: the Web Server pane and `ds` cell 248

`desk-focus-web` is the pane and `ds` cell 248 (`dk-web-cell`) is the block
the desk shares with the spawned web service. The pane offers Stop, Start,
the service's state, and the last sixteen requests its route answered.
`web-dispatch` takes an observer beside the route and calls it for EVERY
request with the response actually returned, so `/api/health` and
`/api/status`, which `web-standard` answers before the route is consulted,
reach the log as well. `works-desk-contract.md` cites cell 248 and
`PreemptiveScheduler.md` stage 3 holds the design.

## WORKS-25: every USB entry point picks its controller by what it finds there

**The contract: a caller takes the first xHCI controller that yields the
device the caller wants, never the first controller.** `GopUsbMsc.msc-hosts`,
`GopUsbKbd.kbd-hosts` and `CamCapture.cam-hosts` each recurse on ordinal over
`xhci-connect-scan`'s one PCI scan, bringing up each controller through
`xhci-connect-one` and stopping at the first that answers: a disk for the mass
storage chapter, a boot keyboard for the keyboard chapter, a frame at
`cam-port-index` for the camera. A controller that fails to initialise is
skipped rather than ending the walk, which is `GopUsb.usb-hosts`' rule. The
bound is `xhci-max-hosts`, 8.

`GopXhci.xhci-connect-first` is the deliberate first-controller entry and is
named for what it does. Its one caller is `codex/test/apps/usb-cam-frame`,
whose bed has exactly one controller.

**The mass-storage walk is graded by its side effect.** codex-vm's second
controller carries no device (the device models are global singletons attached
by root PORT, `docs/Designs/Active/Tools/DeviceEmulationCatalog.md`), so the
arms read the per-controller status cell instead of the result:
`codex/test/apps/usb-walk-past` (`-xhci-two -xhci-no-disk`) must leave `ctl1`
running, and `usb-walk-stop` (`-xhci-two`, a disk on `ctl0`) must leave `ctl1`
never opened.

**What is still unobservable, and it is the reason to keep this row.** The
keyboard and camera walks have no such arm. A keyboard on `ctl0` stops the old
walk and the new one at the same place, and codex-vm has no flag that removes
every keyboard (`-xhci-no-root-kbd` leaves the hub one), so no bed reaches
`kbd-hosts`' second ordinal. Until a device can sit behind `ctl1`, or a flag
unplugs every keyboard, an arm asserting "the keyboard was found" agrees with
both the old code and the new one (L-VACUOUS).

## Standing check: every chapter must compile on its own cites

```powershell
build/check-subset-cites.ps1 -Root apps\works -Jobs 8 -Kernel seed\Codex.cdx
```

**Run it after any cite change and after adding a chapter.** It builds each
chapter as its own unit -- the chapter plus the transitive closure of what it
cites -- and lets the compiler answer, so a chapter using a name it never cited
is caught here instead of by whoever next assembles a subset. That is L-SUBSET,
and the glob build cannot see it. The gate does NOT run it.

**It cannot see an error the code generator raises.** Each unit is the chapter behind an `opening = 0` entry, so nothing the chapter defines is reached and nothing is emitted: measured 2026-09-24, a chapter carrying 15 unresolved generated calls (CDX2040) produced the byte-identical binary on the seed that has them and the seed that fixed them. Compile the chapter FILE directly to see those.

**Read the coverage line, not the verdict.** A unit that does not compile is a
chapter that was not judged, and a run can have failures above a clean verdict.
Since 2026-08-21 the script names them and says INCOMPLETE rather than OK.

| # | Capability | State of the gap |
|---|---|---|

**THE FLUSH IS IN (blu, 2026-09-07), so what remains is the flight.** `scsi-op-sync-cache` (0x35) with LBA 0 and a block count of 0, stamped like TEST UNIT READY with a 10-byte block and IMMED left clear so the device cannot answer before committing; `msc-sync-cache`, then `usb-sync-cache` and `disk-sync-cache` on the dispatch layer. Those two answer THREE states and not a Boolean, because a medium we cannot flush must not read identically to one we did (L-BAILVALUE): 1 flushed, 0 no flush primitive for this medium, -1 issued and refused. `diag-bank-write-text` and the note writer both call it BETWEEN `gfat-write-file` and the `gfat-file-size` readback, which is the ordering the whole finding turns on: the readback is only a statement about flash if the commit precedes it. A -1 fails the bank rather than reporting a success nobody can trust; a 0 is neither a failure nor a certification. **The separator is now a FLIGHT and it is Damian's**: re-fly with the flush in, and if the bank survives the cache reading is the cause, while if it still dies at the same stage the fault is ours. **THE BED CAN NOW ANSWER PART OF IT, so "no bed can answer it, because codex-vm's BOT model commits in one step" no longer holds** (reek, 2026-09-07, plugs 2.46, main 23233): codex-vm accepts 0x35 and `-usb-writeback` makes a BOT write durable only at that commit, dropping what is uncommitted when the machine stops. Measured by hand on that flag: with the flush, 16 commits and `DIAG.TXT` whole; on a kernel six minutes older than the flush, 0 commits, 4,618,240 bytes pending, **`DIAG.TXT` ABSENT from the image while the guest still reported `bank=ok medium=usb`**. That is the mechanism demonstrated and shown to be invisible from inside the guest. It is NOT the cause of sittings 13 and 14, whose media died mid-run rather than at power-off (blu's precision, L-MECHANISM), and the flight remains the separator for that.

**THE `-usb-writeback` PAIR UNDER `diag-arm` DOES NOT SEPARATE YET, and the cause is the ABLATION, not the flag or the harness** (reek, 2026-09-07, from blu's shelf 23298, verified against blu's own census files rather than reasoned). Both arms: 186 `USB WRITEBACK: cached` lines and 186 `BOT-CENSUS: write` lines, so the flag reached the BOT write path and the medium is `usb` in both. Commits: `bank-writeback` 16, matching the hand run exactly; `bank-writeback-noflush` **1, not 0** -- `USB WRITEBACK: commit n=1 off=1049088 len=781824 pending-was=109568`. One commit is enough to leave `DIAG.TXT` on the medium, which is why both arms come back with the file present. **Two theories are refuted by that count.** The kill theory is wrong: `diag-arm` killing codex-vm at END cannot skip the drop, because the drop is the ABSENCE of `ide_flush` and uncommitted bytes never reach the image at all. And the summary line being absent from the console is BY DESIGN, not a symptom: the instrument is the per-write census lines, `fflush`ed one at a time, and `Invoke-Vm` sends the census to a FILE, never to stderr. **What is left for the next blu** is blu's own diagnosis: the ablation cell is set only after the ESP cfg is read and an unset cell reads as flush-wanted, so every bank write before the cfg is parsed still commits; carrying `flush off` in the STUB RING via `stdinCfg`, which is live before the first bank, is the fix. |
| WORKS-61 | **The identity is an ESP file and is absent from the fact store. PRICED 2026-09-07 AND IT BUYS NOTHING YET: do not take it until a CONSUMER exists.** | Priced by measurement rather than argument, and the answer is that the missing piece is not a representation. **Nothing in the desk consumes the identity at all**: no chapter under `apps/works` outside `GopWizard` references `Keypair` or `sign-content`, none loads a keypair from the stored identity, and `GopReview` and `GopEdit` (the two panes that write signed-looking things) reference neither. The only readers are `GopWizard` itself, for the unlock flow, and `GopBoot`'s `key-zero` for locking. So promoting the identity to a fact would add a second representation with no reader, which is the exact shape that made WORKS-46 closeable: a path nothing calls (L-UNCALLED). The prerequisite is something that needs to SIGN with the identity, and when that lands it will say what shape it wants. Until then this row is a note, not work. The identity IS taken and IS persisted. `GopWizard`'s `wizard-run` runs the full ceremony (passphrase, entropy, Ed25519 keygen) and `wz-save-identity` writes salt, IV, public key, the wrapped private key and the vouch to `IDENTITY.DAT` through `gfat-mount-esp`, on the desk-selected medium, with `wz-identity-present` and `wz-load-identity` reading it back. What `GopBoot.codex:389` means by "Persisting your identity here arrives with B3.5" is only that the identity is not a FACT in the store shown on that Storage screen, so nothing in the trust lattice or the repository protocol can reference it as one. The `sysdb-kind-identity` record in `IdentityManager` is a written format for exactly that, stranded on the kernel road layering forbids the desk to reach; a fact write would go beside `gfs-write-and-checkpoint`, the way `GopReview` persists a verdict. Found 2026-09-07 (red) auditing WORKS-46. B3.5 is named in the source and registered nowhere else. |
| WORKS-24 | **The Clock's RTC write works on metal; no bed can grade it** | The Clock accessory sets the time by writing the MC146818 inside a Status-B SET window. On the ASUS the diag ladder's `rtcw` stage (`build/boot/diag/DiagRtcW.codex`) read `accepted` (sitting 16, 2026-09-25, `HardwareSitting.md`): the seconds register took a write thirty away from its reading and read it back. `tools/codex-vm.c` drops every CMOS write, so the bed reads `ignored` and a regression in the write is visible only on metal; `codex/test/apps/clock-encode-test` grades the encoding in the bed. |
| WORKS-76 | **VMX entry hangs on the ASUS, so the desk's `compile <path>` launch cannot run there** | The launch (`vm-compile-cdx` and below; `docs/Designs/Active/OS/DeskBuildLoop.md`) needs VMXON and VMLAUNCH. On the ASUS, with `vmx=on hypervisor=n`, the diag ladder's `vmx` stage hung before its first note: its row read `running` on the glass and `DIAG.TXT` holds no `vmx` line (sitting 16, 2026-09-25, `HardwareSitting.md`). The stage notes only when the guest run returns, and `vm-device-loop` counts exits only, so a guest that never exits is bounded by the VMX-preemption timer alone: `vmcs-setup-controls` arms the VMX-preemption timer at its maximum count when IA32_VMX_PINBASED_CTLS allows it, so such a guest returns `crashed ... reason=preemption-timer` (exit 52), and the row head carries `ptimer=on|off`. **The bed proves** the control-word decision and the reason text (`codex/test/apps/vmx-preempt-control`, synthetic MSRs) and the `vmx-off` and `vmx-noguest` rows; it cannot reach VMX at all: codex-vm reads IA32_FEATURE_CONTROL as 1, and WHP nested virtualization fails `WHvSetupPartition` with 0xC0350005 (fester 2026-09-24). **Only metal answers:** whether the ASUS offers the timer (`ptimer=`), and whether the hang is a guest that never exits (the row then reads `preemption-timer`, after up to about a minute) or a wedge before or inside VMLAUNCH (the row still reads `running`). **Not done:** external-interrupt exiting. It needs the VMM to end each interrupt itself (`acknowledge interrupt on exit`, then an EOI), which is port I/O and so a `Device.Port` effect through `vm-device-loop` and every caller; and after any exit the host runs with IF clear and its GDTR and IDTR bases at 0, because `vmcs-setup-host-state` writes neither. Without it a host PIT interrupt taken inside the guest is ended by the guest's `out 0x20`, which the VMM swallows as an unowned port, so the host PIC stays in service after the run. |
| WORKS-16 | **An editor crash that went away, with no proof of what fixed it** | 2026-08-11: Damian opened `SOURCE.SRC` in the Edit pane on the interactive VM, saw the first screen render, and the VM died. **It does not happen any more** -- he opened the whole file and navigated it on the same path after main 14685. Four headless beds never reproduced it at any point (28 keys over 70 s, 50 keys at 300 ms, an open/close/reopen cycle, 120 scripted mouse samples across the list), so **the two defects fixed in 14683 are a plausible cause and not a demonstrated one.** The plausible half: he picked the file with the pointer, and until 14683 every hovered-row change ran a two-function cycle that both left stack frames behind and skipped its `__heap-restore`. That is the one path his run exercised heavily and the bed did not, because the bed's mouse never moved until the last arm. **New evidence 2026-08-11, and it argues against the fix explaining it.** Three headless runs that day ended with codex-vm exiting early, no frame captured, **and no `!EXC=` line, no "Guest halted", no watchpoint** -- the stderr simply stops mid-word. That is not the shape of a guest fault; it is the shape of the HOST process dying, which is also what "crashed the whole vm" describes. Other runs with the same command line and the same guest completed normally, so it is intermittent. Nobody has looked at whether codex-vm can fault on the host side under this load. **Left open deliberately.** If it stays gone through real use, delete this row; if it returns, the guest's own `!EXC=` line is the thing to capture (`build/desk.ps1 -Wait -Force -Disk seed/Codex.img` prints it to the launching console) rather than another headless arm. |
| WORKS-19 | **A keystroke at the top of a large file costs 30.3 ms on metal, in proportion to file size** | The buffer is flat, so an insert at offset 0 shifts the whole tail and reindexes every line. On the ASUS (sitting 16, 2026-09-25, the ladder's `edit` stage) one keystroke at the top of the 2,896,050-byte `SOURCE.SRC` costs 13,664 us of shift and 16,666 us of reindex, 30.3 ms; the 2-byte control costs 4 us, and the bed measures 11.0 ms for the same file. Open: whether typing lands at speed on the desk on metal, where each keystroke also pays the desk's own iteration; the buffer alone stays under a 60 ms keystroke interval at this size. A gap buffer is the repair if a larger file or that end-to-end reading says otherwise. Probe: `docs/Probes/edit-keystroke-cost.codex`. |
| WORKS-9 | **The ASUS shot-2 timeout is explained** (reek). **The flight card for the next sitting is `docs/Hardware/HardwareSitting.md`, "WHICH BYTES ARE QUEUED: REBUILT AND RE-REHEARSED 2026-08-18"** -- image, hash, flash command, what the operator watches, and what not to conclude. | **The three DRIVER defects behind it are fixed at main 14447** and the account is `docs/Hardware/HardwareSitting.md`, "THE SHOT-2 TIMEOUT ABOVE IS THREE DEFECTS DEEP". A retried chunk now survives a dropped completion, so a second sustained write is no longer lost either way. **What is NOT explained is the original question**: `xhci-fuel` is a spin count and nobody has converted it to a duration on that box, and no bed can, because codex-vm completes every transfer before the guest spins once, so `f` reads exactly 1000000 there. The instrument for the next flight is the `f`, `l`, `r` cells on the shot line rather than the reset-and-retry arm this row used to recommend: `f` is the SMALLEST fuel any COMPLETED transfer left behind, which reads off the transfers that succeeded instead of the one that failed, so a small `f` says the budget was marginal and an `f` near 1000000 says the fuel is innocent and the device stopped answering. **The arm blu originally routed here was wrong and is recorded as wrong so nobody rebuilds it**: it called `xhci-recover-endpoint`, which leads with Reset Endpoint, defined only for a HALTED endpoint, and a timeout leaves the endpoint RUNNING, so it answers Context State Error and recovery refuses. New bed levers for anyone reproducing this: `-usb-bot-drop N` and Bulk-Only Mass Storage Reset, both in `docs/OperatorsManual.md`. **SITTING 6, 2026-08-20: the ladder returned a NUMBER on metal for the first time.** Flown by red; banked here because the reading was GLASS ONLY and the bank dies at the sink (L-BANK), so a photograph was the only other copy. `sink ladder-stop done=4 rung-sectors=16 rung-bytes=8192 payload-bytes=65536 note=1, wr=128 cc=256 lba=2169 rty=1 ph=2 after=0 chunk=16`. Four sittings of one bit each reached this; it is the threshold reading and every later one is a comparison against it. |
| WORKS-6 | **The boot path verifies under Secure Boot** | Re-homed from BootRoadmap.md (B5.1) when that design moved to Done 2026-08-05: the shipped stub boots with Secure Boot off; signing the PE for Secure Boot has no design and no owner. It blocks no current flight. |
| WORKS-7 | **DevDebugger hardware watchpoints and VGA split-screen** | The x86-64 bare-metal #DB (vector 1) handler records a hit and resumes: count at 36360, DR6 at 36368, RIP at 36376 (`ArchitectsSketchbook.md`). `Dev chapter HwWatch` arms debug register 0 as an eight-byte write watch and reads the cells, and the debugger's "Watch Here" arms it beside the software watch, "Check Watches" reports the write count and the last writing RIP, "Clear Watches" disarms it. `codex/test/apps/cpu-debug-watch` and `hw-watch` are the arms; both measure data watchpoints only, so the RF path an execution breakpoint needs is unmeasured, and one hardware slot of the four is used. **Open:** the split-screen half waits for a spec: the console now draws on the GOP grid, not VGA text, and nothing says what the split holds. |
| WORKS-5 | **Serial REPL is reachable from the boot menu, and the Dev Console indexes the stick's tree** | **The Dev Console row works** (2026-09-24, OVMF under `test-ovmf.ps1 -UsbKbd -NoPs2`, seed 0291C387): GopBoot's `menu-dev` turns on `UefiConsole`'s GOP grid (`ugc-block`, 0x1E000: output, a 32-slot key ring, `ugc-yield`), spawns `enter-dev-console` at 8 MB, and feeds the USB keyboard into the ring while the console's idle pass yields; Down moves the console's selection, and Boot Cobblestone returns to the menu. Arms: `codex/test/apps/uefi-gop-console`, `-keys`, `-keys-proc`, `-keys-yield`. The telemetry at `ugc-block` 36 to 56 (pid, spawn status, child progress, pump passes, scancodes, console yields) reads with `-MonCmds "xp/16wx 0x1E000"`. The walk that reaches the console is one run, because the bed does not keep the identity the wizard writes: keys 28,28,28, the passphrase twice (20,18,31,20,28 each), entropy 30,48,46,32,28, twenty 57s over the keygen, 28 x4, six 57s, 28, 80, 28 at `-KeyDelayMs 1500 -Seconds 25`. **What remains.** Serial REPL means chaining from the payload into the REPL the compiler serves (the seed on the stick, built `-Repl`): design `docs/Designs/Active/OS/SerialRepl.md` (PROPOSAL; its open questions are measured first), no code yet. The Dev Console indexes the stick (2026-09-24, same OVMF walk plus Enter twice): GopBoot's child calls `enter-dev-console-stick`, which mounts the ESP and builds `StickSource` over `SOURCE.SRC` (serial: "Indexed 6610 definitions from SOURCE.SRC (66445 lines)"; the 6,610 are every indexed entry, 88 chapters + 632 sections + 5,890 signatures, and the signatures are 5,014 functions and 876 values, census of the 2026-09-07 build), and Browse Source, the viewer, search and `sym` read it, lines on demand. UefiBoot keeps the built-in list. Arms: `codex/test/apps/stick-source-index` (the real 3.1 MB file in an 8 MB spawned slot, a fragmented chain) and `dev-console-stick` (browse, viewer, search and `sym` through the console's dispatch). |
| WORKS-2 | **A GGUF this app WRITES is readable by llama.cpp** | The read direction is closed and measured (main 10603): `build/gguf-foreign-test.ps1` parses four real llama.cpp models, up to a 3.2 GB gemma3 with a 15.7 MB metadata block, agreeing with an independent host parse on version, tensor count, KV count, architecture, tensor-table offset and first tensor name. Nothing checks the other direction. `build/make-agent-bundle.ps1` writes the bundled model and `Foreword chapter Gguf` reads it, so a green `agent-bundle-test` still says only that the two halves agree with each other. Ollama is installed on this box, so the instrument exists: feed it a Codex-written GGUF and see whether it loads. Deferred by Damian 2026-07-26, not blocked. |
| WORKS-3 | **The diffusion app is finished** | `apps/works/GopDiffusion.codex` (ported from `apps/guios/DiffusionApp.codex`; the port closed the GuiDisplay/GuiTimer/GopRender gating) is an unfinished AssetForge UI: the Generate button dispatches to nothing. `df-draw-generating` fills a progress bar from a counter to 20 and produces no image; there is no model load, no sampler, no inference. Finishing it means the button doing something, which is a capability question and not a UI one -- the foreword has `ai/` (NeuralNet, Tensor, Activation) at the scale of `inference-demo` and `neural-test`, which is a long way from a latent diffusion sampler. **PARKED: no weights** (root, 2026-09-24). The file reader exists (`codex/foreword/ai/SafeTensors.codex`); the unpark trigger is diffusion weights on disk, and fetching them is Damian's call. |
| WORKS-67 | **A remarking pane still cannot be given a hole, because a REFUSED remark leaves the mark stale** | The corruption half is fixed: `desk-marks-remark` refuses a mark at or above the next entry's, or below the previous entry's, and answers 1 wrote, 0 no live entry, -1 refused, so a caller can tell a refusal from a write. `codex/test/desk-reuse` grades both refusals, holds the mark against each, and carries an ordinary remark that must still write, which is the Browser's own shape and the line a guard refusing everything would fail. **What is still open is the residue.** An entry a pane REUSED carries the span's base rather than an address that pane chose, so a reusing pane whose state moves has nowhere legitimate to record the move: writing breaks the order and is now refused, and refusing leaves the entry naming an address the state has left, which is exactly WORKS-59's failure (a reclaim then frees live memory). The guard therefore makes the combination safe to DISCOVER, not correct. It is unreachable at head, because the only remarking pane is the Browser (`desk-browser-reenter`) and the Browser is never given a hole: every heavy `-open` evicts it, so its live entry is always the top one. Closing this means a reusing pane can say its state moved, which needs a bit the 8-byte entry does not have, and the decision belongs with whoever wires a remarking pane to a hole. Design: `ShellRefinement.md`, D.5. |
