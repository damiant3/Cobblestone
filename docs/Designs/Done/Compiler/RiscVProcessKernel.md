# RISC-V Process Kernel and Lifecycle (plugs-backlog 1.22)

*Proposal, root, 2026-08-16. Plan only; nothing built. The port of two shipped
ARM64 designs, `docs/Designs/Done/Compiler/Arm64ProcessKernel.md` (1.10: table,
seven builtins, manifest grant, capability gates) and
`Arm64ProcessLifecycle.md` (1.18: spawn, entry, exit, wait, yield, dispatch,
resume, the with-heap bound). Their stage records are the reference for every
runner and ablation below; this document names only what RISC-V changes. It
is the handoff if the campaign changes hands: a fresh session with these three
documents and the ARM64 source (`codex/plugs/arm64/Arm64Runtime.codex`,
sections "Process Table", "Capability Gates", "Process Lifecycle";
`Arm64CodeGen3.codex` `a64-emit-module`; `codex/plugs/common/PlugManifest.codex`)
can execute it.*

## The RISC-V plug as it stands (measured, file:line)

- Stubs: `RiscVCodeGen2.codex:963-971` answers `process-get-pid` 0,
  `process-get-scope`/`-network-scope` "" (`rv-emit-empty-text`), the two
  setters and `process-restrict-cap` -1, `process-get-cap` 0, `net-send-raw`
  and `net-recv-raw` 0. The generic tail at `:992-998` routes a 1-, 2- or
  n-argument builtin to a runtime function of the same name
  (`rv-emit-single-arg-call` `:784`, `rv-emit-two-arg-call` `:794`), so, as on
  ARM64, deleting a stub line and recording a runtime function under the
  builtin's name is the whole codegen change; an unrecorded name is the
  `unresolved call` warning at `RiscVCodeGen3.codex:1552`.
- Runtime registration: `rv-record-func` (`RiscVCodeGen.codex:204`),
  `rv-emit-block` (`:775`), raw lists via `rv-rt-emit` (`RiscVRuntime.codex:13`),
  `rv-emit-runtime` (`RiscVRuntime.codex:1811-2037`, `__start` at
  `:1813-1843`, the last helper `__unicode_to_cce` at `:2033`); user
  definitions shadow runtime names through `runtime-funcs`
  (`RiscVCodeGen3.codex:1384`, `:1580-1587`). Func-addr fixup exists:
  `rv-emit-load-func-addr` (`RiscVCodeGen.codex:764`, two nops -> `auipc`+`addi`,
  patched at `RiscVCodeGen3.codex:1554-1571`); `__start` uses it for the trap
  handler (`RiscVRuntime.codex:1830-1832`). Patching: `rv-patch-insn`
  (`RiscVCodeGen3.codex:431`) on state, `rv-patch-insn-raw`
  (`RiscVRuntime.codex:1112`) on lists; **both take an INSTRUCTION INDEX and
  multiply by four themselves**, unlike ARM64's byte offsets, and only the
  branch immediates are bytes (`(target - idx) * 4`): port the arithmetic,
  not the numbers.
- Map: flat `.bin` loaded at `#80000000` = RAM base (`test-cross-batch.ps1:361,
  416-418`), code then rodata; CCE tables `#80080000`/`#80080100`, try-fail flag
  `#80080200`, SMP AP marker `#80090000` (`RiscVRuntime.codex:709-710, 618,
  1838-1842`; `codex/test/smp-riscv-boot.codex:15`), effect-handler table
  `#80100000` (`RiscVCodeGen3.codex:1381`), memo slots `#80100200` (s0), heap
  s1 from `#80100400` (`RiscVRuntime.codex:1820-1823`), sp `#BFFF0000`
  (`:1817`), RAM 1 GiB (`-m 1024M`).
- **The remap is not ARM64's.** `rv-remap-addr-insns` (`RiscVRuntime.codex:630-639`)
  adds `#80000000` to EVERY address below `#80000000`, and all six
  peek/poke helpers use it (`:642-691`; registered with their `read-mmio` /
  `poke-mmio` aliases at `:1943-1958`). So x86-64's
  `proc-table-base` 20480 mirrors to `#80005000`, which is 20 KiB into the
  loaded code, and the tests' scratch `poke-byte 28000` (`#80006D60`) lands
  in code too for any program over 28 KB. The two lifecycle tests that read
  the table by absolute address (`proc-state-running`, `spawn-reuse`) and
  every test that pokes low scratch depend on that page being RAM the
  program owns.
- Conventions: closure pointer in **t2 (x7)**, code word at `[t2]`, captures
  from `+8`, no arity word (`RiscVCodeGen2.codex:1051-1058, 753-756`);
  arguments a0..a7; callee-saved locals **s2..s11** (`RiscVCodeGen.codex:170,
  334`), **s0** = memo base (runtime helpers also use it as a frame pointer
  and save it, `RiscVRuntime.codex:7, 23-28`), **s1** = heap (`:8`), plus ra
  and sp: fourteen slots to save at a switch (ARM64 saved thirteen).
- Extra: there is no `A64Extra` twin; the fields live flat in `RvState`
  (`RiscVCodeGen.codex:70-99`, defaults `:152-186`), which lacks `boot-cap-mask`,
  `boot-fs-scope`, `boot-net-scope`. `rv-emit-module`
  (`RiscVCodeGen3.codex:1375-1392`) has `chapter.defs` in scope (`:1385`).
  `PlugManifest` is not bundled: `codex/plugs/riscv/build.ps1:10` has no
  `-CommonChapters`.
- Trap: mtvec set to a save/restore stub with no dispatch
  (`rv-rt-trap-handler`, `RiscVRuntime.codex:1232-1262`); nothing emits
  `ecall` (`RiscVEncoder.codex:239` exists). The servicer path is the twin of
  1.17 and is not this campaign.
- Bed: `build/test-cross-batch.ps1 -Arch riscv64 -UseQemu`, `-Filter <name>`,
  results `test-output-cross\riscv64\<name>\runtime.actual`;
  `build/test-cross-smp.ps1 -Arch riscv64 -Test smp-riscv-boot` for SMP.
  `.no-cross` is one file per test for both lanes (23 in `codex/test` today,
  none riscv-specific), so every test the ARM64 campaigns lifted ALREADY RUNS
  in the riscv bed and fails there: `cap-direction`, `cap-process-family`,
  `network-scope-*`, `process-exit-status`, `proc-state-running`,
  `spawn-reuse`, `nested-spawn`, `arm64-proc-cells`, `arm64-net-gate` are in
  the baseline's FAIL list and are this campaign's acceptance arms as they
  stand; nothing is moved aside, and the six sidecars left over carry ARM64's
  reasons.

## Decisions

- **Reserve the first 32 KiB of the image for the x86-64 low-memory mirror.**
  `__start`'s first instruction becomes `j +#8000` and the code buffer is
  padded with zeros to `#8000` before the boot stub continues, so
  `[#80000000, #80008000)` is RAM the program owns: the process table at the
  mirror of 20480 (`#80005000`), the current-pid and empty-text cells at
  `#80006000`/`#80006008`, and the tests' scratch at 28000 (`#80006D60`) all
  land where the x86-64 map says. Thirty-two KiB of zeros per binary is the
  price (measure the `.bin` delta); the alternative, a table anywhere else, keeps `proc-state-running` and
  `spawn-reuse` excluded forever and leaves the scratch pokes corrupting code.
  The zeroing loop is then unnecessary for the table (the image ships zeros)
  but stays, cheap, so a re-entered `__start` is clean. **Emit the pad as one
  operation, not 8191 `rv-emit` calls**: each `rv-emit` copies the 29-field
  `RvState`, so the pad is written straight into `code-buf` (`poke-32` loop
  or `memset`) with `code-pos` and `insn-count` bumped once, and
  `rv-compact-nops` keeps zero words as they are (`RiscVCodeGen.codex:560-566,
  678-688`).
- **Pool at `#90000000`, 16 x 32 MiB, span to `#B0000000`**; heap from
  `#80100400` has 254 MiB before it; the stack top `#BFFF0000` is 255 MiB
  above it; the SMP marker `#80090000` is untouched. Pid from sp:
  `(sp - #90000000) >> 25`, 0 outside.
- **Entry map**: x86-64 offsets where a test or getter reads them (0, 24, 32,
  56, 64, 72 sp, 80 heap s1, 136 closure, 160 resume ra, 176 delivered a0,
  248 blocked-on); the callee-saved set s2..s11 at 88..128 and 184..208 in
  order, s0 at 216 (constant, saved anyway); fixed in the Stage 2 CL.
- **Closure call in `__proc_entry`**: t2 = closure, `ld t1, 0(t2)`, a0 = 0,
  `jalr ra, t1`; the code word is at offset 0 as on ARM64.
- **Extra fields**: `boot-cap-mask`, `boot-fs-scope`, `boot-net-scope` added
  flat to `RvState` and `empty-rv-state`, set in `rv-emit-module` from
  `pm-opening-cap-mask (chapter.defs)` etc.; `riscv/build.ps1` gains
  `-CommonChapters @('PlugManifest')`.
- **Gates**: `rv-rt-check-capability` leaves the tested word in a register
  (there is no flags register): `and t0, cap, mask` and the caller's
  `beq t0, zero, deny`.

## Stages (each its own CL, runner first, full riscv bed before and after)

Mechanics: `codex/plugs/riscv/build.ps1`, then
`build/test-cross-batch.ps1 -Arch riscv64 -UseQemu [-Filter <name>]`, outputs
in `test-output-cross\riscv64\<name>\runtime.actual`; a new `.expected` is
recorded on x86-64 through `build/compile.ps1 -Src ... -Out ... -Log ...` and
`build/test-run.ps1 -Kernel ... -OutFile ...` before the riscv run. Watch the
plug's own size against `-Survey 'lower-mul:120000'` in `riscv/build.ps1` as
the runtime grows.

**Stage 0. Baseline and controls.** Full `-Arch riscv64 -UseQemu` bed on the
current plug for the baseline (BatteryReorg.md's 259/119 is from 2026-07-28
and is not carried); then `-Filter` runs of `cap-direction`,
`cap-process-family`, `process-exit-status`, `proc-state-running`,
`arm64-proc-cells`: record the outputs (expect zeros, unresolved calls, and
for `proc-state-running` a read of code bytes at `#80005000`). No sidecar is
moved: none exists for these on either lane.

**Stage 1. Mirror window, table, the seven builtins, the manifest grant.**
One CL because the runners only separate after both: the `j +#8000` prefix
and the pad; the table at `#80005000` (16 entries of 256 bytes: state 0, net
scope 24, exit status 32, cap 56, fs scope 64), the current-pid and empty-text
cells at `#80006000`/`#80006008`; `rv-rt-current-slot` from sp; the seven
runtime functions ported line for line from `Arm64Runtime.codex` "Process
Table" (`process-get-pid`, `process-get-cap`, `process-get-scope`,
`process-get-network-scope`, `process-set-scope`,
`process-set-network-scope`, `process-restrict-cap`: pid range below 16,
`get-cap` -1 and the scope getters the empty text out of range, the setters
and restrict demanding admin bit 14 in the CALLER); proc 0 RUNNING at boot;
`-CommonChapters @('PlugManifest')`, the three `RvState` fields, `__start`
granting proc 0 the mask, storing both scopes and console to proc 1 in the
order of `X86_64Chapter.codex:398-403`; the stubs at
`RiscVCodeGen2.codex:963-969` deleted. Runners, all already in the riscv bed:
`arm64-proc-cells` (all eleven lines, `state-0 2` included), `cap-direction`,
`cap-process-family`, `network-scope-deny`, `network-scope-open`; ablations:
grant-all in place of the mask fails `cap-direction` on `fs-write 0`, and
Stage 0's zeros are the table's. `smp-riscv-boot` still PASS; full bed
against the baseline; the `.bin` size delta recorded.

*Stage 0 DONE 2026-08-16 (root). Baseline on the plug at main 15896: 285
PASS_EXPECTED / 8 compile-only / 14 refused / 157 FAIL of 464 eligible, 34.7
min. Controls in that run: `cap-direction` all zeros, `cap-process-family`
`process 0`, `arm64-proc-cells` `bad-cap 0`, `process-exit-status`
`2148533248 / 2148533336`, `proc-state-running` starved at three of four
lines, `nested-spawn` `val: 0`, `spawn-reuse` silent, `network-scope-deny`
`11` (the empty scope admits everything; `-open` passes trivially),
`arm64-net-gate` `hwaddr-9 9`.*

*Stage 1 DONE 2026-08-16 (root). `RiscVRuntime.codex` "Process Table":
`__start` opens with `j +#8000` and 8191 zero words (`rv-emit-mirror-window`),
table `#80005000`, cells `#80006000`/`#80006008`, pool constants; the seven
builtins as raw lists under their names, `rv-rt-current-slot-in-t0` from sp;
`rv-emit-proc-table-init` and `rv-emit-boot-grant` after the CCE tables;
`RvState` gains `boot-cap-mask`/`boot-fs-scope`/`boot-net-scope`, set in
`rv-emit-module` from `pm-opening-*`; `riscv/build.ps1` bundles
`PlugManifest`; the seven stubs deleted from `RiscVCodeGen2.codex`. One
RISC-V trap found by the first run: `rv-li` of any `#8xxxxxxx`/`#9xxxxxxx`
value goes through `rv-li-64`, whose scratch is t0, so `li t1, base` after
the slot was in t0 clobbered it and every admin-gated call refused
(`restrict-self -1`); bases are loaded first now and the section says so.
Runners: `arm64-proc-cells` (all eleven lines), `cap-direction`,
`cap-process-family`, `network-scope-deny` PASS_EXPECTED; ablation grant-all
fails `cap-direction` line 2 `fs-write 1`; `smp-riscv-boot` PASS.
`cap-direction.bin` 9,712 -> 43,384 bytes: the 32 KiB window plus the new
runtime. Full bed: see the CL.*

**Stage 2. Gates.** `rv-rt-check-capability`, the four network builtins gated
with x86-64's refusal values (`net-send-raw`/`net-recv-raw` stubs at `:970-971`
deleted; `net-status`, `net-get-hwaddr` recorded), runner `arm64-net-gate`
already in the bed; ablation gate open.

*Stage 2 DONE 2026-08-16 (root). `RiscVRuntime.codex` "Capability Gates":
`rv-rt-check-capability` leaves the tested bits in t0 (no flags register)
and the caller branches `beq t0, zero`; `net-status`, `net-recv-raw`
(read), `net-send-raw` (write), `net-get-hwaddr` (read plus range) recorded,
the two literal-0 stubs deleted. Runner `arm64-net-gate` PASS_EXPECTED on
riscv, all eight lines; ablation with the check forced open fails line 4
`status-denied 0`. Full bed: see the CL.*

*HANDOFF 2026-08-16 (root, bounced for context): Stage 3 is DRAFTED and
SHELVED as root CL 15965, unrun. It is the line-for-line port of
`Arm64Runtime.codex` "Process Lifecycle" with these RISC-V choices, all in
the section prose: fourteen-slot save (sp 72, s1 80, s2..s7 88..128, ra
160, delivered a0 176, s8..s11 184..208, s0 216), closure in t2 with
`ld t1, 0(t2)` / `jalr ra, t1`, every base loaded before t0 carries a value
(`rv-li-64` scratch), `bltu`/`bge`/`beq` in place of flag branches, the
with-heap bound as `bltu t1, t6` against `#2000000`. First act of the next
session is in `plugs-backlog` 1.22.***Stage 3. Lifecycle.** The scheduler ported from `Arm64Runtime.codex`
"Process Lifecycle" with the RISC-V register set and the fourteen-slot save;
`process-spawn`, `-with-heap`, `__proc_entry`, `process-exit-self`,
`process-exit`, `process-wait`, `process-yield`, `__idle_dispatch`,
`__process_resume`. Runners: `process-exit-status`, `nested-spawn`,
`proc-state-running`, `spawn-reuse`, `network-scope-spawn` unchanged;
ablations as 1.18's (no status delivery, no FREE store, no bound).

*Stage 3 DONE 2026-08-16 (root), root CL 15965. `RiscVRuntime.codex` "Process
Lifecycle" as drafted at the handoff: `__process_resume`, `__idle_dispatch`,
`__proc_entry`, `process-exit-self`, `process-exit`, `process-wait`,
`process-yield`, `process-spawn` and `process-spawn-with-heap` (one emitter,
`rv-emit-process-spawn-named`, the with-heap bound `bltu t1, t6` against
`#2000000`), the fourteen-slot save (sp 72, s1 80, s2..s7 88..128, ra 160,
delivered a0 176, s8..s11 184..208, s0 216), closure in t2 with `ld t1, 0(t2)`
/ `jalr ra, t1`. Four of the five runners went green on the first run of the
draft; `spawn-reuse` was SILENT, and the cause was not in the lifecycle. Read
from a QEMU `-d in_asm,exec` trace: `rounds` (`if n == 0 then 1 else act
... end`) was emitted FRAMELESS with `n` in a0, because `rv-body-needs-ra`
(`RiscVCodeGen3.codex`) answered False for an `IrAct` body (its `otherwise`
arm), so after `jal process-wait` the loop computed `addi a0, a0, -1` on the
wait's RETURN VALUE and never reached zero: an infinite spawn loop, and the
same shape lost `ra` in framed TCO functions (`skip-ra`). Fixed there:
`rv-has-any-call` now walks `IrIf`/`IrLet`/`IrMatch`/`IrList`/`IrRecord`/field
forms and answers True for `IrAct`, `IrHandle`, `IrTry` and the rest, and
`rv-body-needs-ra`'s `otherwise` defers to it. Runners: `process-exit-status`
(`40`/`60`), `nested-spawn` (`val: 7`), `proc-state-running` (`2 / 5 / 2 /
0`), `spawn-reuse` (`11`), `network-scope-spawn` (`10`), all PASS_EXPECTED
under `-Arch riscv64 -UseQemu`, unchanged. Ablations, each measured: no
status delivery, `process-exit-status` prints `0 / 0`; no FREE store,
`proc-state-running` ends `exited child is free: 2`; no with-heap bound,
`spawn-reuse` prints `10`. Full riscv bed against the Stage 2 close-out (290
pass / 152 fail of 464): **300 pass / 143 fail of 465** (the extra unit is
`text-append-alias`, restored to main the same day), diffed by FAIL LIST:
zero new failures, nine fixed, the five runners plus `fe310-drivers`,
`fork-reclaim`, `hal-flash-linear` and `sort-test`, which the classifier
repair lifted on its own. Re-run after the plug was rebuilt under the
COMPILER-8 seed `0D239110`: see the CL.*

**Stage 4. Record.** Every stage's record under its paragraph here; the
backlog entry deleted; this doc to `Designs/Done`; the ecall servicer path
filed as the twin of 1.17 if not already covered by it.

*Stage 4 DONE 2026-08-16 (root), in the Stage 3 CL: `plugs-backlog` 1.22
deleted; this document moved to `docs/Designs/Done/Compiler/`. The `ecall`
servicer path is already 1.17's twin by the register's own wording ("a Stage 4
for later, as 1.17") and waits on the same block path; nothing new is filed.
Open question 1 (the remap rewriting real MMIO) is untouched and stays here as
recorded; question 2 was answered in Stage 1.*

## What this does not touch

The x86-64 kernel and its tests, the ARM64 plug, fester's block path, the
riscv-only wrong-value cluster reek owns (`BatteryReorg.md` item 10). Not
seed-affecting; no build token.

## Risks and costs (R-COST)

As 1.10/1.18: fixed table, no allocation, a switch is fourteen stores and
loads. New here: 32 KiB of zeros per binary (measure the `.bin` size delta in
Stage 1's CL) and the remap policy itself, which this campaign uses and does
not change.

## Open questions

1. The remap of every address below `#80000000` also rewrites real MMIO
   (`read-mmio #10000000` -> `#90000000`); the survey found no comment and no
   test at a real MMIO address on this lane. This campaign leaves it alone;
   someone should say whether it is policy.
2. The three `cap-*-denied` `.no-cross` reasons cite ARM64's address; with the
   mirror window they read the real table on RISC-V too, but still need a
   device or the NIC. Rewrite the reason to say so, in Stage 1.
