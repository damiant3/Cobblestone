# ARM64 Process Lifecycle (plugs-backlog 1.18)

*SHIPPED 2026-08-16 (root): Stages 0-4 landed the same day they were planned;
plugs-backlog 1.18 is closed. Each stage's record sits under its plan
paragraph. Together with `Arm64ProcessKernel.md` (1.10) this leaves the ARM64
lane with x86-64's process model minus the timer: of the fifteen tests once
excluded as "bare runtime with no kernel", nine pass unchanged (`cap-direction`,
`cap-process-family`, the three `network-scope-*`, `process-exit-status`,
`spawn-reuse`, `proc-state-running`, `nested-spawn`) and the six that remain
wait on a device the bed does not attach (`cap-audio`, `cap-media-families`,
`fs-deny-runtime`) or are x86-64-only by design (the three `cap-*-denied`).*

*Proposal, root, 2026-08-16. Plan only; nothing built. Follows
`docs/Designs/Done/Compiler/Arm64ProcessKernel.md` (1.10), which built the
table this sits on. Read that first for the address map and the cross-bed
method.*

## The model being ported, measured from x86-64

Everything below is `codex/compiler/Emit/X86_64ProcessHelpers.codex` unless
named otherwise.

- **The table is the allocator.** 16 entries of 256 bytes at `proc-table-base`
  20480; state word at 0 (`X86_64Boot.codex:466-470`: free 0, ready 1,
  running 2, blocked 3, zombie 4), exit status at 32, closure at 136,
  blocked-on at 248, saved context at 72 (sp), 80 (heap pointer), 88-128 and
  176-232 (registers), 160 (resume pc). Proc 0 is RUNNING (2) from boot
  (`emit-create-process ... proc-state-running`, `X86_64Boot.codex:2475`) and is never a spawn target: every slot scan
  starts at 1 (`:305`, `:411`).
- **The spawn pool.** `spawn-pool-base` 1 GiB, 16 regions of 32 MiB
  (`spawn-slot-region-shift` 25), `X86_64Boot.codex:363-366`. A child's heap
  pointer starts at its region base and its stack top is base + 2 MiB
  (`__spawn_pool_carve`, `:279-287`; sizes `X86_64Boot.codex:361-362`). A slot
  freed by exit is found again by the first-free scan, so the region is reused
  and a spawn loop plateaus (the account at `:265-277`); `spawn-reuse` reads the region base
  from offset 80 to prove exactly that. **The running pid is a function of the
  stack pointer** (`emit-slot-from-addr`, `X86_64Boot.codex:1110-1122`: outside
  the pool answers 0), stored nowhere.
- **spawn** (`:289-391`): gate on `cap-process-create` (bit 12, granted by
  `Concurrent`), first free slot from 1, carve, write resume pc = `__proc_entry`,
  sp, heap, closure, inherit cap and both scopes, zero the saved registers and
  the exit status, publish READY last, answer the pid; -1 on deny or full.
  `process-spawn-with-heap` (`X86_64IPCHelpers.codex:1925-2129`) refuses when
  heap + 1 MiB stack exceeds the 32 MiB region (`:1995-2003`), so 33554432 is
  refused. **The parent never switches to the child**: it runs when the parent
  yields, waits or exits, or on a timer tick.
- **`__proc_entry`** (`:192-207`): pid from sp, closure from 136, call it with
  argument 0, store its return in exit status, fall into exit-self.
- **exit-self** (`:209-251`): state := FREE (not zombie), then the wake loop:
  every BLOCKED slot whose blocked-on is my pid gets my exit status in ITS
  saved-return slot and BLOCKED -> READY; then dispatch. Unary `process-exit`
  (`:253-263`) stores the status and joins it.
- **wait** (`:525-604`): target FREE or ZOMBIE -> answer its exit status now;
  else save own context (only callee-saved registers, sp, heap pointer, resume
  pc, saved-return 0), blocked-on := pid, state := BLOCKED, re-check the target
  (self-rescue if it died in the window), dispatch. Resumption returns from
  wait with the status the waker stored.
- **yield** (`:46-112`): scan forward from self+1 for READY, switch, self ->
  READY; 0 if nobody.
- **Without the timer this is purely cooperative**, and the ARM64 lane has no
  process timer, so the port is the cooperative model exactly and the four
  tests are deterministic on it. The deck is not per-process on either arch and
  ARM64 has no deck; nothing to port there.

## What the four acceptance tests need

| test | reads | needs |
|---|---|---|
| `process-exit-status` | wait status of a returning child (40) and a `process-exit` child (60) | spawn, `__proc_entry`, unary exit, wait |
| `proc-state-running` | `peek-qword 20480 (slot*256)`: 2 at boot, 2 after a round trip, child slot 0 after | the table AT the address x86-64 uses, state words |
| `spawn-reuse` | `peek-qword 20480 (pid*256+80)` equal across 21 rounds; `process-spawn-with-heap ... 33554432` = -1 | slot reuse, region base at 80, the with-heap bound |
| `nested-spawn` | a child that spawns and waits on a grandchild | two waiters blocked at once, inherited spawn capability, regions carved per slot |

Two of them read the table by absolute address, which is the same bar the
`cap-*-denied` tests could not clear in 1.10. It clears here for free: the
ARM64 runtime already mirrors x86-64 low memory for `peek-byte/16/32` and
`poke-byte/16/32` (`a64-rt-remap-addr`, `Arm64Runtime.codex:803-810`: an
address under 16 MiB gains `#40000000`), and the tests' `poke-byte 28000` goes
through it today. `peek-qword` and `poke-qword` (`:844-850`) do NOT remap, which
is the gap. So the table moves to the mirror of 20480, `#40005000`, the qword
pair joins the remap family, and every hardcoded-address test on x86-64 sees
the real ARM64 table, `cap-*-denied`'s `poke-32 20536` included (they still
need devices; that is not this campaign).

## Decisions

- **Table at `#40005000`** (mirror of 20480). The three absolute constants
  in `Arm64Runtime.codex` "Process Table" move together: `a64-proc-table-base`
  -> `#40005000`, and the two cells behind it, `a64-current-proc-addr`
  (`#40203000`) and `a64-proc-empty-text-addr` (`#40203008`, the text a
  refused scope read answers), -> `#40006000` and `#40006008`, so the zeroing
  loop's 514 qwords still cover all three. Then `a64-heap-start` returns to
  `#40202000`; leaving either cell behind at `#40203xxx` puts it under the
  first 4 KB of heap and `arm64-proc-cells` goes red on `scope-99`. Nothing
  else in the map is at `#40005000..#40006010`: the ELF loads at `#40100000`
  (its own header says `#400000`, `Arm64Elf.codex:82`; QEMU places it at the
  entry the 1.10 record measured), the fault flag is `#40000010`, and the
  tests' own mirror writes start at 28000 (`#40006D60`). What QEMU itself
  puts in low RAM under `-kernel` (DTB, boot stub) is NOT recorded anywhere in
  the tree; Stage 1's runner is what says the page is usable.
- **Current pid from `sp`, as x86-64 does**: `(sp - pool-base) >> 25`, 0
  outside the pool. Three readers change, not one: `a64-rt-process-get-pid`,
  `a64-rt-proc-caller-cap-in-x9` (behind `process-set-scope`,
  `process-set-network-scope`, `process-restrict-cap`) and
  `a64-rt-check-capability` (behind the four network gates). The cell stays
  only as the boot-time value 0 if a reader remains; the target is none.
- **Pool at `#50000000`, 16 x 32 MiB, span to `#70000000`.** Proc 0's heap
  from `#40202000` has 254 MiB before the pool. The SMP AP marker
  `a64-rt-ap-marker-addr` `#60000000` (`Arm64Runtime.codex:729`) sits at slot
  8's region base and moves to `#7E000000`, in the runtime AND in
  `codex/test/smp-arm64-boot.codex:15` (`ap-marker-addr`), which reads it by
  `peek-qword` (above 16 MiB, so the qword remap does not touch it); the
  runner is `build/test-cross-smp.ps1 -Arch arm64 -Test smp-arm64-boot`.
- **Entry layout by x86-64 offset where a test or a getter reads it** (0, 24,
  32, 56, 64, 72, 80, 136, 248). The callee-saved set on ARM64 is thirteen
  slots, not x86-64's six: x19..x27 (codegen locals, `Arm64CodeGen.codex:669`,
  prologue `:774-782`), x28 (heap, at 80 as x86-64's r10), x29, lr (resume pc,
  at 160), sp (72). Saved x0 (the delivered status) at 176, x86-64's
  saved-rax. The remaining registers take 88..128 and 184..232 in order; the
  exact map is fixed in the Stage 2 CL and recorded here.
- **Closure convention**: the closure pointer goes in x11, code at `[x11]`,
  arity at `+8`, captures from `+16` (`Arm64CodeGen2.codex:1592-1603`,
  `a64-tramp-load-captures` `:1719-1722`); `__proc_entry` calls with x11 =
  closure and x0 = 0, or every capturing child breaks silently.
- **Regions**: heap at base (x28), stack top at base + 2 MiB, no pretouch
  (QEMU RAM is not demand-faulted).

## Stages

The mechanics are the 1.10 ones (`Arm64ProcessKernel.md`, "The bed" and
Stage 0): build the plug with `codex/plugs/arm64/build.ps1`, move a test's
`.no-cross` aside locally, `build/test-cross-batch.ps1 -Arch arm64 -UseQemu
-Filter <name>`, read `test-output-cross\arm64\<name>\runtime.actual`; record
every new `.expected` on x86-64 through `build/compile.ps1` and
`build/test-run.ps1`; and run the FULL bed against a same-day baseline before
believing any stage (1.10 Stage 1 passed its arm while the bed was red).

**Stage 0. Controls that fail.** On the current plug (1.10 shipped): the
three tests compile with unresolved-call warnings for `process-spawn`,
`process-wait`, `process-exit`, `process-spawn-with-heap`, and print
whatever a stale x0 gives. Record the outputs.

*Stage 0 DONE 2026-08-16 (root), on the plug at main 15807:
`process-exit-status` prints `ret-status: 1075855360 / exit-status:
1075855440`; `nested-spawn` prints `val: 0`; `proc-state-running` prints
`proc 0 at boot: 0` (so `peek-qword 20480` reads pflash as zeros rather than
faulting, the reader's correction), `child ran: 0`, `proc 0 after a spawn
and wait: 0`, then `!A64FAULT ... FAR=0000004020409000` when the garbage pid
indexes the table; `spawn-reuse` faults the same way at
`FAR=0000004020405050`. Each carries the unresolved-call warnings above.*

**Stage 1. The table where x86-64 has it.** Move the table and its two cells
to `#40005000`/`#40006000`/`#40006008`, derive the pid from `sp` in all
three readers, remap `peek-qword`/`poke-qword`, store RUNNING (2) into proc
0's state word after the zeroing loop, restore `a64-heap-start` to
`#40202000`, move the AP marker. Runner: every 1.10 arm still green
(`arm64-proc-cells`, `arm64-net-gate`, `cap-direction`, `cap-process-family`,
`network-scope-deny/-open`), `smp-arm64-boot` under `test-cross-smp.ps1`, one
new line in `arm64-proc-cells` (`state-0 2` from `peek-qword 20480 0`,
recorded on x86-64 first), and the full bed against the 1.10 close-out
baseline (407 pass / 30 fail of 459). Ablation: the old address and the line
reads 0.

*Stage 1 DONE 2026-08-16 (root). Table at `#40005000`, cells at `#40006000`
and `#40006008`, heap back at `#40202000`, pid from `sp`
(`a64-rt-current-slot-in-x9`: `(sp - #50000000) >> 25`, 0 outside
`[#50000000, #70000000)`) in all three readers, `peek-qword`/`poke-qword`
through `a64-rt-remap-addr`, proc 0's state word 2 after the zeroing loop, AP
marker at `#7E000000` in the runtime and `smp-arm64-boot.codex`
(`test-cross-smp.ps1 -Arch arm64 -Test smp-arm64-boot`: PASS). Runner:
`arm64-proc-cells` grew `state-0 2` from `peek-qword 20480 0` (x86-64
recorded, ARM64 identical), the six 1.10 arms green, full bed 407 pass / 30
fail of 459 with zero new failures. Ablation is Stage 0's measurement: the
old table read `proc 0 at boot: 0` through the same `peek-qword`.*

**Stage 2. Spawn, entry, exit, wait, yield: the cooperative scheduler.**
`process-spawn`, `__proc_entry`, `process-exit-self`, `process-exit`,
`process-wait`, `process-yield`, `__idle_dispatch` (scan for READY; none ->
`wfi` loop, which is x86-64's `hlt` loop without a tick to end it) and
`__process_resume` (restore the callee-saved set, sp, heap, x0 := saved
status, `ret` to the resume lr). Runner: `process-exit-status`,
`proc-state-running`, `nested-spawn` lifted from `.no-cross` in this CL and
green under `-Arch arm64 -UseQemu`, unchanged. Ablation: skip the wake loop's
status copy and `process-exit-status` prints 0s; skip the FREE store and
`proc-state-running`'s last line reads 2.

*Stage 2 DONE 2026-08-16 (root). `Arm64Runtime.codex` "Process Lifecycle":
`__process_resume`, `__idle_dispatch`, `__proc_entry`, `process-exit-self`,
`process-exit`, `process-wait`, `process-yield`, `process-spawn`, all recorded
runtime functions so the builtins need no codegen arm (a one-argument call
resolves through the generic path, a bare `process-yield` through the name
path). Final entry map: sp 72, x28 80, x19..x24 88..128, closure 136, resume
lr 160, delivered x0 176, x25..x27 184..200, x29 208, blocked-on 248, exit
status 32, state 0. `__proc_entry`'s address is planted through the existing
func-addr fixup (`a64-emit-load-func-addr`), which is why these are
`A64State`-level emitters and not raw byte lists. Runner: `process-exit-status`
(`40`/`60`), `nested-spawn` (`val: 7`), `proc-state-running` (`2 / 5 / 2 /
0`) all PASS_EXPECTED unchanged; `network-scope-spawn` (spawn inheriting the
net scope, then `process-yield`) also passes and is lifted here rather than in
Stage 4; the four `.no-cross` deleted in this CL. Both ablations measured:
without the wake loop's status store `process-exit-status` prints `0 / 0`;
without the FREE store `proc-state-running` ends `exited child is free: 2`.
One defect on the way, caught by counting instructions to the ELR of the
first fault: `a64-emit-current-entry` used one register as both destination
and scratch and doubled the entry address to `0x8000A000`; x16 is the scratch
now. `spawn-reuse` prints `10`, the with-heap bound being Stage 3.*

**Stage 3. `process-spawn-with-heap` and the region bound.** The bound
`heap + 1 MiB > 32 MiB -> -1`; `spawn-reuse` lifted, green. Ablation: drop
the bound and the last digit reads 0.

*Stage 3 DONE 2026-08-16 (root). `process-spawn-with-heap` is `process-spawn`
with the region bytes taken from x1 + 1 MiB and refused above 32 MiB
(`a64-emit-process-spawn-named`, one emitter for both names; `b.hi` to the
same -1 exit). `spawn-reuse` prints `11` and is lifted; the ablation without
the compare prints `10`, the last digit the test exists for.*

**Stage 4. Lift and record.** `network-scope-spawn` (needs `process-yield`
and spawn) lifted if green; the four `.no-cross` deleted in the CLs that earn
them; `plugs-backlog` 1.18 deleted; this doc to `Designs/Done`.

*Stage 4 DONE 2026-08-16 (root), in the Stage 3 CL: all five exclusions this
campaign named are gone (`process-exit-status`, `nested-spawn`,
`proc-state-running`, `network-scope-spawn` in Stage 2, `spawn-reuse` here);
1.18 deleted from the register; this document moved to
`docs/Designs/Done/Compiler/`. Open question 1 answered by measurement (the AP
marker move was mine, `smp-arm64-boot` PASS); question 2 answered by the
test itself passing on the Stage 2 plug.*
## What this does not touch

The x86-64 kernel and its tests; fester's `VirtioBlk` chapter and 1.17; the
RISC-V lane. Not seed-affecting; no build token.

## Risks and costs (R-COST)

Memory: the pool is address space, not allocation; a spawn touches one
256-byte entry and writes nothing into the region beyond the child's own
frames. The wake loop is 16 iterations per exit; the first-free scan 15 per
spawn; both are what x86-64 pays. Time: a context switch is twelve stores and
twelve loads. The one thing to watch is the `wfi` idle: a blocked parent with
no runnable child parks the guest forever, which is x86-64's behaviour without
a timer, and the bed reports it as starvation rather than silence.

## Open questions

1. Does the AP marker move (`#60000000` -> `#7E000000`) need red's or fester's
   eye, or is it mine to make with `test-cross-smp.ps1` green?
2. Should `network-scope-spawn` count as this campaign's acceptance or stay
   with 1.10's network ruling (it needs only yield and spawn, no device)?
