# Backlog — Outstanding Work

**Updated**: 2026-05-20

## Active — Ongoing

### USB Install (Gap 4)

| # | Item | Notes |
|---|------|-------|
| 8 | **USB MSC driver** | `UsbMassStorage.codex` in codex.kernel. Bulk-Only Transport, SCSI commands, device discovery via XHCI. Protocol test: `usb-msc-test.codex`. |
| 9 | **DriveManager USB integration** | `dm-enumerate-drives` probes USB after ATA. `dm-install-usb` writes sectors via MSC. `DriveInfo` has `di-is-usb` flag. |
| 10 | **DevConsole "Install to USB"** | Drive Manager menu wired. Dispatch filters USB drives, calls `dm-install-usb`. |
| 11 | **XHCI transfer ring completion** | Done. Command ring, event ring, DCBAA, port reset, Enable Slot, Address Device, Configure Endpoint, control transfers, bulk in/out. `usb-enable-slot`, `usb-bulk-in`, `usb-bulk-out`, `usb-get-config-desc` implemented. Test: `xhci-enum-test.codex`. |

### Compiler

| # | Item | Notes |
|---|------|-------|
| 1 | **Phase discipline rollout** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. All 6 frontend phases have per-phase build + phase-measure + phase-compact (CLs 500, 552, 644). Emitter wall enforced (CL 644). Open: lower/emit isolation, escape invariant, TCO reset removal, survey tightening. |
| 2 | **Per-function emit reclaim hardening** | CL 463 added heap-save/restore around each emit-function. CL 1563 decked all emit-phase durable data (__deck-enter/exit around init, deck-record codegen-carry-forward). |

## Recently Closed

| # | Item | Resolution |
|---|------|-----------|
| 3 | ~~handler-nested batch GPF~~ | **Fixed CL 1845.** Root cause: `lookup-expr-type` in Unifier used non-short-circuit `&` to guard a `list-at` access after binary search. When the key was not found (`pos == len`), `list-at entries len` read one element past the list into stale heap data from the previous REPL compilation. Fix: split into nested `if` so the access is only reached when `pos < len`. Also likely root cause of the plug crash (PLUG-CRASH-INVESTIGATION.md). Sweep: 105/105 pass, 0 fail. |
| 4 | ~~Short-circuit `&`/`\|` on Booleans~~ | **Fixed CL 1885.** `IrAnd`/`IrOr` now emit conditional jumps instead of bitwise ops. Right operand is only evaluated when needed. Eliminates the entire class of non-short-circuit guard bugs (including the CL 1845 root cause). Seed rebuild: 2,172,408 bytes, 105/105 pass. |
| 5 | ~~Plug crash (page fault in bsearch-text-pos)~~ | **Confirmed fixed by CL 1845 + seed rebuild.** See `docs/Test/PLUG-CRASH-INVESTIGATION.md` Session 6. |
| 6 | ~~Plug IrFieldStore/IrTry support~~ | **Fixed CL 1899/1906.** All 8 emitters handle mutable records and try/fail. `-IrCce` compiler mode fixed. Shared `IRTextParser` parses both nodes. |
| 7 | ~~Foreword library bugs~~ | **Fixed CL 1908.** Huffman (stale indices + dead walk), Graph DFS (visited state), Bresenham (dead branch), Convolution (div-by-zero), HexFormat (spacing). |
