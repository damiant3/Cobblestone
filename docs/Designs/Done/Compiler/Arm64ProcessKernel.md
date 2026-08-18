# ARM64 Process and Capability Kernel (plugs-backlog 1.10)

*SHIPPED at enforcement, 2026-08-16 (root): Stages 0-3 landed the same day
they were planned, plugs-backlog 1.10 is closed, and Stages 4 and 5 are
`plugs-backlog` 1.17 and 1.18 by red's ruling. Each stage's record sits under
its plan paragraph below; the "gap" section is the state BEFORE the campaign
and is kept because the line numbers it cites are the ones the CLs changed.*

*Proposal, root, 2026-08-16. Plan only; nothing built. Read before touching
`codex/plugs/arm64/`. Corrected the same day after a naive-reader pass
(R-NAIVE): the first draft's Stage 1 arms needed Stage 2 and Stage 5 to go
green, its Stage 4 rested on an x86-64 syscall path that the console does not
use, and its getters had the wrong signature. What is below is what the source
says.*

## The gap, measured from the source

`Arm64CodeGen2.codex:1499-1505` answers the seven process builtins with
literals: `process-get-pid` 0, `process-get-scope` and
`process-get-network-scope` "" (`a64-emit-empty-text`), `process-set-scope`,
`process-set-network-scope` and `process-restrict-cap` -1, `process-get-cap`
0. `read-file` / `read-file-raw` / `read-file-uni` / `uefi-read-file` are
refused outright at `:1472-1479` (`a64-emit-unsupported-read`), and
`net-send-raw` / `net-recv-raw` answer 0 at `:1506-1507`. There is no process
table in `Arm64Runtime.codex`; `__start` (`a64-emit-runtime`, `:1953`) sets
VBAR, CPACR, the PL011, the stack, the two CCE tables, then branches to the
uni or SMP boot. The vector table exists (`a64-rt-vector-table`, `:704`) and
**all 16 slots are patched to the one shared `a64-rt-fault-handler`**
(`:2151-2154`, handler at `:1893`), so an `SVC` today lands in the fault
printer. The disassembler knows `SVC` (`Arm64Disasm.codex:360`, decode `:474`)
but nothing emits one.

On x86-64 the same layer is:

- A process table at `proc-table-base` 20480 (`X86_64Boot.codex:23`), layout
  under "Process Table Layout" (`:410`): `proc-net-scope-offset` 24,
  `proc-exit-status-offset` 32, `proc-cap-offset` 56, `proc-scope-offset` 64
  (`:430-442`).
- Boot population in `emit-start` (`X86_64Chapter.codex:397-403`):
  `emit-process-setup`, then the manifest-derived `boot-cap-mask` over
  `manifest-cap-names` granted into proc 0, `emit-set-boot-scope` for the FS
  and net scopes, and `emit-grant-capability 1 cap-console` (proc **1**, bit
  0; proc 0's console bits come from the manifest mask). The manifest helpers
  (`manifest-cap-names`, `boot-cap-mask`, `manifest-opening-fs-scope`,
  `manifest-opening-net-scope`, `X86_64Chapter.codex:769-898`) read the
  `opening` binding's type (`:775-779`), not the type-defs.
- The getters take a **pid argument** with a range check:
  `process-get-cap pid` and `process-get-scope pid`
  (`X86_64IPCHelpers.codex:583-593`, `:725-735`). `process-restrict-cap`
  requires the CALLER to hold `cap-capability-admin` (bit 14) and the target
  pid to be below 16, clears the bit on the TARGET, answers 0 or -1
  (`X86_64ProcessHelpers.codex:625-659`).
- `emit-check-capability` (`X86_64Boot.codex:2753-2765`), a bit test on a
  process's capability word, at 12 call sites: the syscall arms for console
  read/write and block/identity (`X86_64Boot.codex:2777, 2793, 2862, 3025,
  3052, 3076`), and INLINE in the helpers for filesystem read/write
  (`X86_64Helpers.codex:1823, 1850`) and network read/write
  (`X86_64IPCHelpers.codex:2151, 2183, 2227, 2364`).
- **The syscall path is narrower than "every effect op".** The compiler emits
  `syscall` only for numbers 10-18 (block and identity,
  `X86_64Helpers.codex:3322-3324, 3848, 4415-4489`). Console write goes
  `emit-print-text` -> `__serial_put` -> `out dx,al` or the DMA buffer
  (`X86_64Helpers.codex:577-642`) and never meets `cap-console-write`; syscall
  1's arm exists (`X86_64Boot.codex:2781-2783`, one byte) and is not called by
  compiled code. Filesystem and network gates are inline. So the x86-64 shape
  to mirror is: inline gate at each effect helper, and an SVC only for the
  device families that go through the servicer.

Capability bit numbers live in foreword `Capability.codex` (`cap-console-write`
5, `cap-filesystem-read` 6, `cap-network-read` 8, `cap-capability-admin` 14,
`:25-28`), which a plug can cite.

The plug parses the IR chapter (`Arm64Plug.codex:52-53`: `parse-ir-chapter`,
then `a64-emit-module ... (parsed.type-defs)`). The manifest is NOT in
`type-defs` (that is `List ATypeDef`, `codex/plugs/common/IRTextParser.codex:27`); it is the
`opening` def's `type-val` in `parsed.chapter.defs` (`IRTextParser.codex:873,
878`), and the effect scopes survive the IR text (`codex/compiler/Emit/IRTextEmitter.codex:252`).
That is derivable plug-side without a wire change, and it has NOT been
measured: Stage 2's first act is to print the derived mask for `cap-direction`
and compare it to x86-64's.

The bed: `build/test-cross-batch.ps1 -Arch arm64 -UseQemu` compiles each
eligible test through `codex/plugs/arm64/compile-arm64.ps1`, boots it under
`D:\Program Files\qemu\qemu-system-aarch64.exe` (`:352`) and asserts UART
against `.expected`; output under `test-output-cross\arm64\<name>\` (`:59,
148-151`). It scans `codex\test\*.codex` and `codex\test\ops\*.codex` only
(`:67`); `-Filter <substring>` selects tests (`:41, 74`); a test is skipped for
`.skip/.slow/.fatal/.failing/.smp`, then `.no-cross`, then any machine sidecar
`.disk/.disk2/.disk-src/.vmargs/.keys` (`:76-91`). Prerequisite:
`codex/plugs/arm64/build.ps1` must have produced
`codex/plugs/arm64/build-output/arm64-plug.cdx` (`compile-arm64.ps1:33`).
`build/boot-arm64.ps1` is a different pipeline (UEFI PE via GPT image), not the
bed for one test.

34 `.no-cross` files today; 15 carry "the cross lane boots a bare runtime with
no kernel": `cap-audio`, `cap-block-denied`, `cap-direction`,
`cap-identity-denied`, `cap-media-families`, `cap-network-denied`,
`cap-process-family`, `fs-deny-runtime`, `nested-spawn`, `network-scope-deny`,
`network-scope-open`, `network-scope-spawn`, `proc-state-running`,
`process-exit-status`, `spawn-reuse`. Not all fifteen can lift here:

- `fs-deny-runtime` carries a `.disk` sidecar and is machine-sidecar excluded
  whatever its `.no-cross` says.
- `cap-block-denied`, `cap-identity-denied`, `cap-network-denied` (and
  `cap-gpu-*-denied`, not in the 15) strip the grant by `poke-32` at absolute
  address **20536** = x86-64 `proc-table-base` + `proc-cap-offset`. ARM64
  runtime data lives at `#40000000` and up (`a64-load-base` `#40000080`,
  `Arm64CodeGen3.codex:1827`; CCE tables `#40200000`, `Arm64Runtime.codex:750`),
  and 20536 is not RAM on QEMU virt. Those tests are x86-64-bound by their own
  text and cannot pass unchanged; they stay excluded with the reason rewritten
  to say so, unless the tests themselves gain a symbolic accessor (a change to
  x86-64 tests, outside this campaign, and a question below).
- `cap-audio`, `cap-media-families` need devices the bed does not attach.

So the acceptance list this campaign owns is: `cap-direction`,
`cap-process-family` (Stage 2); `network-scope-*` if the network becomes real
(Stage 3); `process-exit-status`, `spawn-reuse`, `proc-state-running`,
`nested-spawn` (Stage 5).

## Stages, each its own CL, each with a runner before it is believed

The order is the dependency order and the L-FALSIF order: the first thing
built is the arm that can say NO.

**Stage 0. A control that fails, at the UART.** Before any kernel code, on the
CURRENT plug: build the plug (`codex/plugs/arm64/build.ps1`), move
`codex/test/cap-direction.no-cross` and `codex/test/cap-process-family.no-cross`
aside locally (do not open them in Perforce), and run
`build/test-cross-batch.ps1 -Arch arm64 -UseQemu -Filter cap-direction`, then
`-Filter cap-process-family`. Both compile today (they use only
`process-get-cap`, `process-get-pid`, `bit-and`, `bit-shl`, `print-line-uni`)
and both must FAIL at the UART with all-zero bits against `.expected`
(`fs-read 1 / fs-write 0 / con-read 1 / con-write 1`; `process 1`). Record the
exact output. If either passes today, the test is not testing what its name
says (L-NAMED) and the plan is wrong before it starts. `process-exit-status`
is NOT a Stage 0 control: it calls `process-spawn` / `process-wait` /
`process-exit`, none of which the plug knows, so it dies at codegen
(`a64-emit-single-arg-call`, `Arm64CodeGen2.codex:1529`; unresolved-call warn
`Arm64CodeGen3.codex:1810`), which proves nothing about the bed.

*Stage 0 DONE 2026-08-16 (root), plug built at 640,850 bytes from the current
source. Both controls compile (2.8 s, 2.7 s) and both FAIL at the UART exactly
as predicted: `cap-direction` prints `fs-read 0 / fs-write 0 / con-read 0 /
con-write 0`, `cap-process-family` prints `process 0`
(`test-output-cross\arm64\<name>\runtime.actual`). The bed sees the subject;
the `.no-cross` files were moved aside for the run and put back, nothing
opened.*

**Stage 1. Process table and the seven builtins, x86-64 semantics.** A fixed
`a64-proc-table-base` region in the runtime's data window (address chosen and
recorded in the Stage 1 CL; the CCE tables at `#40200000`/`#40201000` are the
neighbours), entry layout copied by OFFSET (24/32/56/64, same entry size), so
the arch-independent library predicates that read these cells read the same
shape. A current-process cell that proc 0 owns at boot. The builtins get the
x86-64 signatures, not simplified ones: `process-get-cap pid`,
`process-get-scope pid`, `process-get-network-scope pid` with the pid range
check answering 0 / "" out of range; `process-restrict-cap` demanding bit 14
in the caller's word and pid below 16, clearing on the target, 0 / -1; the two
setters storing a Text pointer through the scope offsets. Runner: ONE new arm,
`arm64-proc-cells` under `codex/test/`, x86-64 first: it prints
`process-get-pid`, the raw cap word, a restrict attempted WITHOUT the admin
bit (must answer -1 and leave the word alone), and the scope text; its
`.expected` is what x86-64 prints, and the ARM64 build must print the same
lines. Ablation: stub `process-restrict-cap` back to -1 and the arm still
passes the refusal line, so the arm must ALSO carry a positive restrict once
Stage 2 grants bit 14 through the manifest; until then Stage 1's arm proves
table, pid, getters and the refusal, and says so. `cap-direction` and
`cap-process-family` stay red through Stage 1: they need the grant.

*Stage 1 DONE 2026-08-16 (root). Table at `#40202000` (16 x 256 bytes),
current-process cell at `#40203000`, empty-text cell at `#40203008`, the heap
base moved from `#40202000` to `#40204000` to make room; all zeroed by
`__start` after the CCE tables (`Arm64Runtime.codex` "Process Table"). The
first placement, `#40110000`, was wrong and the full cross bed caught it: the
ELF loads at `#40100000` (entry `0x40100880`; the `a64-load-base` `#40000080`
cited above is the DISASSEMBLER's base, not the load address), so any program
over 64 KB had its text zeroed at boot, and the 24 largest tests in the bed
starved or printed truncated lines while everything under 64 KB passed. Run
the FULL bed against a same-day baseline, not the arm alone; the arm passed
both times. Not examined here: the effect-handler table `htb = #40100000`
(`Arm64CodeGen3.codex:1589`, eight bytes per effect op) sits at the load
address itself, below the vector table at `#40100080`; whether the first 128
bytes there are free by design or by luck was not measured.*

*The seven builtins are runtime
functions recorded under their own names, so a bare `process-get-pid` resolves
through the ordinary call path with no codegen arm. One correction to the arm
as planned: `process-restrict-cap` and `process-set-scope` carry the
`[Capability]` effect in their types (`Types/Builtins.codex:132,140`), so any
test that calls them declares `Capability` and x86-64 grants bit 14 from the
manifest, which means "restrict without the admin bit" cannot be written on
x86-64. The refusal the arm witnesses is therefore the RANGE refusal (`pid 99`
on restrict and set-scope, -1 on both arches; ARM64 refuses it for the admin
bit today and for the range once Stage 2 grants). `codex/test/arm64-proc-cells`
prints `pid 0 / bad-cap -1 / restrict-99 -1 / set-scope-99 -1 / scope-99 0 /
net-scope-99 0`, recorded through `test-run.ps1` on x86-64 and green under
`-Arch arm64 -UseQemu`; the old plug answers `bad-cap 0` (measured in Stage 0
through `cap-direction`), which is the line that would catch a stub coming
back. The two controls still print zeros on the new plug, as this stage
predicts.*

**Stage 2. Boot population from the manifest.** Port `manifest-cap-names`,
`boot-cap-mask`, `manifest-opening-fs-scope`, `manifest-opening-net-scope`
into a chapter the plug bundles (candidate `codex/plugs/common/`, since RISC-V
has the identical gap and a plug cannot cite the x86-64 chapter), reading the
`opening` def's `type-val` from `parsed.chapter.defs`. First act: print the
derived mask for `cap-direction` and `cap-process-family` from the plug and
compare to x86-64's `boot-cap-mask` for the same source. Then `__start` gains
the analog of `X86_64Chapter.codex:398-403`: grant the mask into proc 0's
word, store the two scopes, grant console bit 0 to proc 1. Runner:
`cap-direction` and `cap-process-family` from the excluded set, `.no-cross`
deleted in the same CL, green under `-Arch arm64 -UseQemu`, plus the Stage 1
arm's positive restrict now that bit 14 can be declared. Ablation: grant-all
instead of the manifest mask and `cap-direction` must fail on `fs-write 0`
(L-CAPABILITY-LOST: a boot that grants everything reads right on every test
except the ones that check a bit is ABSENT, so `fs-write 0` is the line that
pays).

*Stage 2 DONE 2026-08-16 (root). `codex/plugs/common/PlugManifest.codex`
(`pm-*`, cites Foreword `Capability`, bundled through a new
`-CommonChapters` switch on `Build-TranspilerPlug` so RISC-V can opt in by
name) derives the mask, the FS scope and the net scope from the `opening`
def's `type-val` in `parsed.chapter.defs`; `a64-emit-module` stashes them in
`A64Extra` (`boot-cap-mask`, `boot-fs-scope`, `boot-net-scope`) and
`__start` grants proc 0 the mask, stores both scopes, and grants console bit
0 to proc 1 (`a64-emit-boot-grant`), the order and the targets of
`X86_64Chapter.codex:398-403`. The mask was compared to x86-64's by the only
instrument that matters: `cap-direction` prints `fs-read 1 / fs-write 0 /
con-read 1 / con-write 1` and `cap-process-family` prints `process 1`, both
unchanged and both green under `-Arch arm64 -UseQemu`, `.no-cross` deleted in
the same CL. Ablation measured: grant-all in place of the mask and
`cap-direction` fails on line 2, `exp=[fs-write 0] act=[fs-write 1]`.
`arm64-proc-cells` grew its positive arm (`admin 1`, `con-write-before 1`,
`restrict-self 0`, `con-write-after 0`, x86-64 recorded, ARM64 identical).
The four exclusions that cannot lift here carry the reason that names the
bar (`fs-deny-runtime`: sidecar plus no filesystem; the three `poke-32`
tests: absolute 20536), and Stages 4 and 5 are `plugs-backlog` 1.17 and 1.18
per red's ruling.*

**Stage 3. `a64-emit-check-capability` and inline gates on the effect paths
that exist.** The bit-test analog of `X86_64Boot.codex:2754-2765` (load the
target's word, `TST` against `1 << bit`), inserted INLINE the way x86-64 does
for filesystem and network (`X86_64Helpers.codex:1823, 1850`;
`X86_64IPCHelpers.codex:2151-2364`, read AND write), at the ARM64 helper for
each such path as it exists: today none does (`read-file*` refused, net 0), so
Stage 3 lands the check emitter with the network read/write gate if
`net-send-raw` / `net-recv-raw` become real (question 1), and names
`CrossLaneFilesystem.md` steps 2-5 (VirtioBlk, block builtins, unowned) as the
dependency for the filesystem gates rather than absorbing them. Runner: with
network real, `network-scope-deny` / `-open` from the excluded set (they need
no device the bed lacks; verify each compiles on the current plug first, the
Stage 0 way); without it, a new arm that restricts `cap-network-read` and
calls `net-recv-raw`, expecting the x86-64 refusal value, and its x86-64 twin
printing the same lines. Console is NOT gated on x86-64 (the print path
bypasses syscall 1), so no console-denied arm: it would exceed parity and have
no twin.

*Stage 3 DONE 2026-08-16 (root). `a64-rt-check-capability`
(`Arm64Runtime.codex` "Capability Gates"): the caller's word against one bit,
flags left for the caller's `b.eq` to its refusal, the shape of
`X86_64Boot.codex:2753-2765`. Gated inline, as x86-64 gates them: `net-status`
and `net-recv-raw` on `cap-network-read`, `net-send-raw` on
`cap-network-write`, `net-get-hwaddr` on read plus the range check; every
refusal answers -1 and every admitted answer is what x86-64 answers with no
NIC (0), the ruling on question 1 being that the network stays a device
story. The two literal-0 codegen arms became calls; `net-status` and
`net-get-hwaddr` had no arm at all and would have compiled to an unresolved
call. Runner: `codex/test/arm64-net-gate` (`granted True` before the
restrict, then `-1` from all four after; x86-64 recorded through
`test-run.ps1`, ARM64 identical); ablation measured, gate forced open and it
fails on `status-denied -1` vs `0`. And a finding the plan did not predict:
`network-scope-deny` and `network-scope-open` need no device at all, only the
scope cell Stage 2 stores and `net-scope-admits`, and they pass unchanged on
the Stage 2 plug (10 and 11); by the test's own text `deny` = 10 can only
come from a stored scope that names `ok.host`, which the old plug's "" cannot,
so both lift here. `network-scope-spawn` stays, for `process-spawn`
(1.18). Filesystem gates wait for the filesystem (fester,
`CrossLaneFilesystem.md` steps 2-5) and belong to whoever lands the block
path, with `a64-rt-check-capability` ready for them.*

**Stage 4. The SVC path, for the families x86-64 services.** x86-64 emits
`syscall` only for block and identity (numbers 10-18); the dispatch is
`X86_64Boot.codex:2773-3073`, roughly ten `cmp-ri reg-rax` arms. On ARM64 the
equivalent is `SVC #n` from those helpers into the synchronous-EL1 vector slot,
which today is patched to `a64-rt-fault-handler` with the other 15
(`Arm64Runtime.codex:2151-2154`); Stage 4 carves that one slot out of the
patch loop and dispatches on `n`. This stage is only reachable once a block or
identity path exists on ARM64 (`CrossLaneFilesystem.md`), so it is scoped and
sequenced here and not started until one does. Runner: `a64-dis-svc` confirms
an `SVC` at each serviced op; the arm for the family, and a direct-call bypass
arm that must be refused.

*Stage 4 DONE 2026-08-17 (root, plugs-backlog 1.17, row deleted). The three
block builtins are `svc #n` stubs (`a64-emit-svc-stub`: read 10, write 11,
sector-count 12, x86-64's numbers) and their former bodies are internal
(`__block_read_body`, `__block_write_body`, `__block_count_body`). Vector slot
4 (current EL, SPx, synchronous) now branches to `a64-emit-sync-handler`,
emitted after the fault handler and patched over the slot the sixteen-way
loop had filled: it reads ESR through the per-EL dispatch the fault handler
uses, takes EC #15 (SVC from AArch64) and falls to the fault handler for any
other synchronous exception with x9 intact; gates ONCE with x86-64's
`emit-block-elev-gate` shape (caller's word holds `cap-block-device`, bit 10,
OR the fs-elevated cell is non-zero), answers -1 on refusal, else dispatches
the ISS imm16 to the body by `bl` and `eret`s. The fs-elevated cell is 36232
mirrored at `#40008D88` (`a64-fs-elevated-addr`), zeroed by
`a64-emit-proc-table-init`, set to 1 and cleared inline by
`__fs-read-servicer` / `__fs-write-servicer` around their `fat16-servicer-*`
calls and reachable as no builtin (x86-64's rule, `X86_64Boot.codex:219`).
ELR/SPSR are not saved across the body: no vector on this lane enables an
interrupt. Runner, failing first: `codex/test/block-gate-restrict` (cites
`Foreword chapter VirtioBlk` so the driver links on this lane; `.disk` is the
128-sector fixture): x86-64 records `count-before 128 / restrict 0 /
count-after -1` (`test-run.ps1 -DiskFile`); the OLD plug answered
`count-after 128` (the bypass val's paragraph named, measured), the new plug
answers x86-64's lines under `build/test-cross-disk.ps1 -Arch arm64`. The
SVC at each stub is confirmed from the ELF word at the map address
(`D4000141`, `D4000161`, `D4000181` = svc #10/#11/#12; `a64-disasm-enabled`
is a compile-time False, so the disassembler is not the instrument).
Elevation measured by `fs-deny-runtime`, `fs-servicer` and `fs-layer` through
the disk runner, all PASS: their processes hold no `Device.Block` and reach
the disk only through the servicer's cell. Ablation: gate forced open and
`block-gate-restrict` fails on `count-after 128` vs `-1`. Direct calls to the
VirtioBlk driver's own Codex functions (`vb-read-auto` etc., reachable by any
chapter that cites `VirtioBlk`) are NOT behind this gate; x86-64 has no such
path because its ATA driver lives inside the syscall handler. That is a
distinct gap, `plugs-backlog` 1.34. PR 66 finding 6 (lambdas on the wire) was decided orthogonal to
this stage (the SVC path emits no closures) and is `compiler-backlog`
COMPILER-12.*

**Stage 5. Process lifecycle: spawn, exit status, wait.** `process-spawn`,
`process-wait`, `process-exit`; the four lifecycle tests
(`process-exit-status`, `spawn-reuse`, `proc-state-running`, `nested-spawn`)
are the acceptance arms and none compiles on the plug today. The x86-64
`__proc_entry` and exit-wake loop are the model. Largest stage; deferrable
without invalidating 1-3, and 1-3 are what the backlog entry names as the
missing enforcement (question 3).

**Stage 6. Lift the exclusions.** Delete each `.no-cross` whose reason is
"bare runtime with no kernel" as its stage lands, in the same CL, so the count
of exclusions is the progress meter and cannot drift; rewrite the reason on
the four that cannot lift here (`fs-deny-runtime`, the three `poke-32` tests)
in the Stage 2 CL so the reason names the actual bar.

## What this does not touch

- The x86-64 kernel and its tests. Nothing here is seed-affecting; the plug is
  a separate binary and `codex/plugs/arm64` is not in the compiler's
  dependency set. No build token.
- The RISC-V lane. It has the same gap; the shared-chapter choice in Stage 2
  is made so it can follow, but it is not this campaign.
- The wire. The plug already receives the `opening` type the manifest is
  derived from.

## Risks and costs (R-COST)

Memory: a process table is a fixed region and the getters are loads; nothing
here allocates per call. The scope Text stores hold pointers into the
program's own heap, as x86-64 does. Time: the capability check is a load and
a bit test on every gated op, the same cost x86-64 pays inline. The SVC round
trip on QEMU is only paid by the block/identity families, per operation, as
x86-64 pays `syscall` for them; the console never takes it, on either arch.

## Open questions, DECIDED 2026-08-16 (red, as commander; Damian may override)

1. `net-send-raw` / `net-recv-raw` **stay 0 in 1.10.** A real ARM64 network is
   a virtio-net device story, not a capability-kernel story. Stage 3 lands the
   check emitter with the no-network arm above (restrict `cap-network-read`,
   call `net-recv-raw`, x86-64 twin prints the same lines); the three
   `network-scope-*` tests keep `.no-cross` with the reason rewritten to name
   the missing device.
2. The manifest helper chapter goes in **`codex/plugs/common/`**.
3. **1.10 closes at enforcement: Stages 1-3 plus their Stage 6 lifts.** Stage 4
   (SVC path, blocked on `CrossLaneFilesystem.md` block/identity) and Stage 5
   (lifecycle) each get their own `plugs-backlog` entry naming the dependency,
   filed in the Stage 2 CL.
4. The three `cap-*-denied` tests **stay x86-64-only by design**; no x86-64
   test is touched in this campaign. Their `.no-cross` reason is rewritten to
   name the hardcoded 20536.
