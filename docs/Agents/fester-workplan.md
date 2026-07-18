# fester -- workplan

*Status, not journal. Per-CL history is in Perforce. Durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, not here. This file is the current
picture and the next moves only. Keep it under ~80 lines.*

## Status (clean; nothing open/shelved)

Working the BACKLOG section-4 bundle (4.14-4.18) at Damian's request. Shipped this
session, both GUEST-side (no seed, no .exe), each gated + copied up:
- **4.14** -> main: `vga-terminal-demo` compiles.
- **4.15 command-ring** -> main: xHCI delivers Command Completion events; new
  default-battery test `xhci-event-delivery` pins it.

**The finding that stopped the bundle:** 4.16 and 4.18 are INSTRUMENT-blocked, not
implementation-blocked. Existing tests pass BOTH before and after the fix
(`cdx-serve-test` on clean loopback for 4.16; no isolatable HPET path for 4.18), so a
shared-codex-vm.exe change cannot be proven non-vacuously. Build the proving harness
(loss/reordering NAT; timer-ISR) FIRST -- do not flip seq/ack or the run-loop blind.
Full analysis + exact line numbers in memory `backlog-4-codex-vm.md`. 4.10 is
INDEPENDENT of 4.15 (a pure-formatting codegen crash). 4.17 = Damian chose to
IMPLEMENT the COM3 bridge (large), not started.

## Next move -- pick one

- 4.16/4.18: build the proving instrument, then fix. Each is a real project.
- 4.17: implement the COM3 compute bridge in codex-vm (Damian's call).
- 7.16 (app breakage classes) is the older standing target if section 4 is parked:
  Class F (multi-line application CDX1070, ~176 sites, `let`-chain the nested
  `__record-set`; guard with the `apps/vision` 15-assertion aliasing probe), with
  Class C (CDX2031/2033) and D (CDX2051) behind it. Merge reek's LIR + blu's filetype
  down first.

## My lane (own it; others stay out)

apps/, codex/os/kernel + tools/codex-vm.c (the host VM), capability/boot/UEFI, the
bounded/narrowing corner of the type checker. Not reek's Emit/IR/LIR, not blu's
Types/Syntax/TLS, not val's repo/compress.

## Open in my lane (BACKLOG)

- 4.16/4.17/4.18 + 4.15-transfer-residue -- the codex-vm deep rot. Each a real
  project; 4.16/4.18 are instrument-blocked (see Status + memory).
- 4.15 residue: the DATA path (ADDRESS_DEVICE input context / EP0) stays dead; only
  the command ring delivers so far.
- 2.21 media ops: Camera blocked behind 4.10 (independent codegen crash);
  Location/Sensors need a stub-vs-defer decision from Damian.
- 7.17 shift latch -- a MIGRATION (folds passphrases uppercase); Damian's call.
- 4.13 ASUS xHCI keyboard -- never ask for a stick flash.

## For other agents

- codex-vm.exe was rebuilt. Backward-compatible. Add a device with `mmio_decode`,
  not a fourth heuristic. The standing gate does NOT cover MMIO -- for a device/VM
  change run boards-test.ps1 + hda-audio/mic-peak/display-ops + smp-* at -Smp 4.
- A test can type: `.keys` reaches the PS/2 cell (poll-key/uefi-read-key), `.stdin`
  reaches the serial ring (read-line). Pick by what the code reads.

Push blockers on Damian: stale `seed/Codex.img` rebuild; poison build before publish.
