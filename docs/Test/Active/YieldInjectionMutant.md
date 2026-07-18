# Testing Narrow-Window Concurrency Races with a Yield-Injection Mutant

Some scheduler races live in a window a handful of instructions wide. A
timer preempts every few million instructions, so the odds of a tick
landing in a 15-instruction window *and* the counterparty being scheduled
to exploit it are effectively nil. You cannot reproduce such a race by
running the program a million times; you reproduce it by *forcing* the
interleaving.

This is the technique that proved BACKLOG 4.11(g) — the channel-block
lost-wakeup — was real and that the fix closes it. It is a general tool for
any "commit-then-someone-runs" TOCTOU in the scheduler.

## The idea

The race needs some other process to run inside the window. So make it
run: **inject a `process-yield` into the window in a throwaway compiler
build.** A yield deterministically hands the core to the next READY
process, which does the racing operation, and then control comes back and
the victim proceeds into the window's tail. The race that a real timer hits
once in a blue moon now fires on every run.

The yield injection is **test-only**. It never ships. Only the fix ships.

## The mutant (channel-block lost-wakeup)

The window is between "decide to block" and "commit to BLOCKED" in
`emit-chan-send-block-helper` / `emit-chan-recv-block-helper`
(`codex/compiler/Emit/X86_64IPCHelpers.codex`). Inject one call to the
scheduler's own `process-yield` right after the capacity/emptiness check
falls through to the block path, before the context save:

```
; send-block, after `st16 = jcc cc-l 0` (channel full -> fall through):
   in let blkMut = emit-call-to st16 "process-yield"
   in let blk2 = emit-load-current-proc blkMut reg-rax     ; was: ... st16 reg-rax

; recv-block, after `st16 = jcc cc-e 0` (channel empty -> fall through):
   in let rblkMut = emit-call-to st16 "process-yield"
   in let blk2 = emit-load-current-proc rblkMut reg-rax    ; was: ... st16 reg-rax
```

`process-yield` saves the full callee-saved context (rbx, rbp, r12–r15) and
the return address, so a mid-function `call process-yield` is transparent:
the victim resumes at the instruction after the call with its live registers
intact and walks straight into the block commit. It only switches when there
is a READY replacement — which is exactly the racing counterparty — so no
test scaffolding is needed beyond arranging for that counterparty to exist.

## The drivers

Two functional tests set up the race and report completion, so a hang is
observable as missing output:

- `codex/test/apps/chan-lost-wakeup.codex` — a producer blocks on a full
  capacity-1 channel; a separate consumer drains it. Prints `done`.
- `codex/test/apps/chan-recv-wakeup.codex` — a consumer blocks on an empty
  capacity-1 channel; a separate producer fills it. Prints `rdone`.

Both use two *separate* spawned processes (not proc 0 as the counterparty),
because the injected yield must hand off to a READY process, and proc 0 is
blocked in `process-wait`. The channel is capacity 1 so the block path is
reached immediately.

## The bisection

Build a throwaway compiler with the mutant, compile the driver with it, and
boot it single-core with a timeout. The verdict is completion vs. hang:

```powershell
# 1. inject the mutant into X86_64IPCHelpers.codex (above)
build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile build/output/Codex.codex
build/compile.ps1 -Src build/output/Codex.codex -Out mutant-sut.cdx -Log m.log -Repl -Kernel seed/Codex.cdx
# 2. compile the driver WITH the mutant compiler
build/compile.ps1 -Src codex/test/apps/chan-lost-wakeup.codex -Out t.cdx -Log t.log -Kernel mutant-sut.cdx
# 3. boot; a hang (no output before the timeout) is the reproduced race
tools/codex-vm.exe -kernel t.cdx -headless -output t.out -smp 1 -mem 3072
```

The full matrix, each a separate throwaway build:

| build | send driver | recv driver |
|---|---|---|
| clean (no mutant) | `done` | `rdone` |
| mutant only | **HANG** | **HANG** |
| mutant + fix | `done` | `rdone` |

The mutant-only hang proves the race is real; the mutant+fix completion
proves the fix closes it *even with the window forced wide open*. That is
strictly stronger than "it passes without the mutant," which only proves
the fix does not break the cooperative path (the fix's new code is not even
reached cooperatively — the counterparty rendezvous-wakes the blocker before
the recheck matters).

## Instrumenting inside the mutant

When the fix under test does not work, the guest is halted and cannot be
attached to (`-trace-file`/`CODEX_VM_PROFILE` flush only on clean exit, and
a killed hang flushes nothing). Get the guest to report its own progress:
have the driver's processes `atomic-store` a progress marker to a scratch
cell after each step, and have proc 0 **yield-spin** a bounded number of
times (instead of `process-wait`, which would block proc 0) and then
`atomic-load` and print the markers. You can also emit probe stores from the
mutated compiler itself (`li rdi <cell>; mov-store rdi <reg> 0`) to read a
register at a specific point in the emitted code. This is how the 4.11(g)
fix was debugged: the markers localized the failure to the retry jump.

## The gotcha that cost the most time

The first fix hand-computed a backward `jmp` offset as
`target - (jmp_pos + 5)`. It was wrong: `st-append-code` buffers into a
flush cache, so `code-len` on an un-flushed state is not the instruction's
final position. **Use `patch-jmp-at` / `patch-jcc-at` for every non-trivial
jump** — they call `fc-flush` first and compute the offset against the
flushed layout. Emit the jump as a placeholder (`jmp 0`), capture its
position, and patch it once the target is known. The block/yield paths in
the emitter already follow this rule; a directly-computed backward offset is
the exception, and it is fragile.
