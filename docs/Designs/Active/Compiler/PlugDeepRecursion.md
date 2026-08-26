# Deep recursion through a plug

*Plugs 1.14. Raised by PR 64 (Steve Howell), stated for the whole quire
2026-08-16, measured first-hand the same day (val).*

## The gap

Codex source assumes deep recursion is free. Bare metal answers it with a
multi-gigabyte arena; a conventional runtime answers it with a fixed
stack, and .NET gives its main thread 1 MB. `Parser.codex` at 18,812
tokens overflowed zig's 8 MB main-thread stack, and ReleaseFast does not
rescue it: the calls sit inside labelled block expressions and LLVM does
not turn them into loops.

**The obvious answer is wrong, and this is the part to keep.** Emitting a
loop for self-tail-calls does not close it. The case that reaches the
limit is MUTUAL recursion -- the lexer's `scan-token` -> `skip-prose-line`
-> `scan-token` cycle -- which no self-TCO pass can flatten. `csharp` and
`zig` both close it by running the entry point on a thread with a 512 MB
stack; nothing else does.

## What is measured, and it is not what the entry predicted

`codex/test/plug-oracle-arith.codex` contains no recursion of any kind, so
before this nothing in the tree had ever asked a plug the question. The
probe is four definitions at two depths: `down` (self, tail), `ping`/`pong`
(mutual, tail), `sum-to` (self, NOT tail), each at 1,000 and 100,000.

| arm | `down` 1k | `down` 100k | `ping` 1k | `ping` 100k | `sum-to` 1k | `sum-to` 100k |
|---|---|---|---|---|---|---|
| x86-64 (truth) | 0 | 0 | 0 | 0 | 500500 | 5000050000 |
| `csharp` | 0 | 0 | 0 | 0 | 500500 | 5000050000 |
| `javascript` | 0 | 0 | 0 | **RangeError** | -- | -- |
| `wasm` | 0 | 0 | 0 | **call stack exhausted** | -- | -- |
| `python` | 0 | 0 | **RecursionError** | -- | -- | -- |

The probe, which is not in the depot until it can be wired (see below):

```
Section: Depth

  down : Integer -> Integer
  down (n) = if n <= 0 then 0 else down (n - 1)

  ping : Integer -> Integer
  ping (n) = if n <= 0 then 0 else pong (n - 1)

  pong : Integer -> Integer
  pong (n) = if n <= 0 then 1 else ping (n - 1)

  sum-to : Integer -> Integer
  sum-to (n) = if n <= 0 then 0 else n + sum-to (n - 1)

Section: Report

  opening : [Console] Nothing
  opening =
   act
    print-line-uni (show (down 1000))
    print-line-uni (show (down 100000))
    print-line-uni (show (ping 1000))
    print-line-uni (show (ping 100000))
    print-line-uni (show (sum-to 1000))
    print-line-uni (show (sum-to 100000))
```

Three things fall out of that table which the entry did not say.

**`csharp` is the control and it passes every row**, so the 512 MB thread
is not a plausible fix, it is a measured one, and the other rows mean
something because this one is green.

**Self-TCO is not the missing half, and the probe proves the plugs already
have it.** `down 100000` is green everywhere, because the python plug
emits `while True:` with a reassignment where the source recursed. The
failures are all `ping`, which is exactly the shape the entry names.

**`python` dies two orders of magnitude earlier than everyone else, and
for a different reason.** It fails at depth 1,000, not 100,000, because
CPython's `sys.setrecursionlimit` default is 1,000 -- a counter, not a
stack.

**And the fix for python is NOT the csharp fix, which is what four
ablations were for.** The plan first written here said python needed the
pair, a big-stack thread plus a raised counter, and that raising the
counter alone would crash. Measured on CPython 3.11.9, this box, on the
probe above:

| arm | result |
|---|---|
| 128 MB thread + `setrecursionlimit(10^6)` | all six rows, matches x86-64 |
| big stack, DEFAULT counter | `RecursionError` at depth 1,000 |
| raised counter, DEFAULT thread stack | all six rows, exit 0 |
| raised counter, NO thread at all | all six rows, exit 0 |

**The counter is the whole fix and the thread buys nothing here**, because
CPython 3.11 stopped consuming C stack for Python-to-Python calls: depth
is bounded by the counter and by nothing else. A fat-frame control (21
live locals, depth 100,000, main thread) also passes, so it is not that
the probe's frames are too small to notice.

Two consequences worth carrying. **The 512 MB constant cannot be shared:**
`threading.stack_size(512MB)` is REFUSED outright on this box
(`ValueError: size not valid`), and 128 MB is the largest power of two
CPython accepts here, so a plug that ports the csharp constant literally
raises at import before it runs a line. And **the answer is CPython
version dependent** -- on 3.10 and earlier the C stack does grow with
Python recursion -- so the emitted form should raise the counter
unconditionally and treat the thread as the fallback for an older runtime
rather than the mechanism.

## The shape of the fix, per target

Three classes, and which class a language is in decides whether there is
any work at all.

**Class 1, the stack is a thread property and the plug can ask for a
bigger one.** The `csharp`/`zig` fix ports directly: emit the entry point
as a thread with an explicit stack size, join it, and re-raise. This is
where the work is.

**Establish the class by ablation, not by the language's reputation.**
python looked like class 1 from the outside and is not: its limit is a
counter and its fix is one line. The ablation that settles it is cheap --
apply each half alone and see which one the failure follows -- and the
cost of skipping it is a plug carrying thread machinery that does nothing,
with a constant its runtime refuses.

**Class 2, the runtime grows the stack and there is nothing to do.** Go
segments goroutine stacks; BEAM (elixir) grows a process stack on the
heap; Haskell's stack is heap-allocated. Confirm rather than assume, with
the probe.

**Class 3, the language cannot express it and the honest close is a
recorded divergence**, the same shape 1.8 reached for `haskell`, `elixir`
and `clojure`. `javascript` is the clear instance: node's stack size is a
process flag (`--stack-size`), not something emitted source can set, so a
plug can only document it or emit a launcher.

**`wasm` was classed here as the same question and is NOT: it moved to a
fourth class this table did not anticipate, the language that EXPRESSES the
fix** (fester, 2026-08-25, plugs 1.82). The tail-call proposal
(`return_call`) ships in wasmtime and every major browser engine, and it
runs any tail call, mutual included, in the caller's frame. The wasm plug
now emits it for every application in tail position that saturates a known
arity, and the compiler self-compiles byte-identically at a 1 MB stack --
the browser's real number, no flag, no thread. The probe's `ping` row is
constant-stack there; `sum-to` remains a true frame obligation at parity
with x86, whose own boot stack this work incidentally measured at ~64 MB
(a 10M-deep mutual probe double-faults it -- the reference has no
mutual-TCO either, its stack is just bigger). A class-3 reading of a target
is worth rechecking against what its spec has shipped since the reading.

## The inventory

Read from source across all 54 plug directories, 2026-08-16. **Two plugs
enlarge the stack and both use exactly 512 MB. Every other plug that emits
a runnable entry point calls it directly on the default stack.**

| | plugs |
|---|---|
| **Enlarges the stack (2)** | `csharp` -- `opening-emit-entry` -> `opening-on-big-stack`, `CSharpEmitter.codex:774-798`, `new System.Threading.Thread(..., (int)(_stackMb * 1024 * 1024))` with `_stackMb = 512` at `:794`. `zig` -- `zig-main`, `ZigEmitter.codex:2343-2349`, `std.Thread.spawn(.{ .stack_size = 512 * 1024 * 1024 }, opening, .{})` then `t.join()`. |
| **No CPU entry wrapper, NA (10)** | `elf`, `img`, `pe` (container writers), `ptx`, `spirv`, `wgsl` (GPU: `opening` IS the kernel entry), `t3isa`, `arm64`, `riscv` (bare metal, a fixed boot SP), `babbage` (no thread concept on an Analytical Engine). |
| **Calls the entry directly on the default stack (the rest)** | `ada`, `angular`, `clojure`, `cobol`, `compose`, `d`, `electron`, `elixir`, `flutter`, `fortran`, `go`, `groovy`, `gtk`, `haskell`, `html`, `java`, `javascript`, `julia`, `kotlin`, `lua`, `maui`, `nim`, `objc`, `ocaml`, `pascal`, `perl`, `php`, `python`, `qt`, `react`, `ruby`, `rust`, `scala`, `scheme`, `svelte`, `swift`, `swiftui`, `typescript`, `vue`, `wasm`, `winforms`, `wpf`. |

Two rows of that were re-read by hand against the source before it was
recorded (`zig-main` at `ZigEmitter.codex:2343`, `emit-go-chapter` at
`GoEmitter.codex:412`) and both matched.

**And the scheduling fact that bounds this whole item: six target
toolchains exist on this box.** Re-measured 2026-08-19 (val) over 49
candidates and unchanged from reek's 37-candidate census of 2026-08-18:
`node`, `python`, `dotnet`, `zig`,
`wat2wasm` and `wasmtime`, and nothing else -- no `go`, `java`, `rustc`,
`ruby`, `perl`, `php`, `lua`, `julia`, `ghc`, `ocaml`, `swiftc`, `fpc`,
`gnatmake`, `gfortran`, `cobc` or BEAM. So of the 42 plugs in the third
row, exactly three can be EXECUTED here (`python`, `javascript`, `wasm`),
one is already done (`csharp`), and one is Steve's (`zig`). Everything
else can be taken as far as the emitted source and no further, which is
the standard `pascal` and `kotlin` already set in the register. A class
assignment for a plug whose runtime is absent is a reading, and the CL
says so.

## What must be true before any of this lands

**The arm comes first, and it is not wired until the plugs it grades are
fixed.** 1.13 already ruled this shape: a `gauge-store` arm was written
down rather than wired, because landing it would have taken the harness
from 4 of 4 green to 1 of 4 red without fixing what it exposed. The same
applies here, and the probe above is the arm.

Two properties the arm must keep:

- **Both shapes, self and mutual, at both depths.** A self-only arm is
  green on every plug that has TCO and says nothing about the defect. The
  pair is what separates "has a TCO pass" from "has a stack".
- **A non-tail row (`sum-to`).** Nothing can flatten it, so it is the row
  that cannot be answered by any pass, only by frames. It is also the only
  row whose ANSWER is a large number rather than 0, so a plug that returns
  a wrong value rather than dying is caught.

**Do not measure this by reading the emitted source.** Every failure above
is a runtime failure in a program that compiled. For the plugs whose
runtime is not on this box, this item can only be taken as far as the
emission, and the CL says so, exactly as `pascal` and `kotlin` did.

## Order

**EVERY PLUG THIS BOX CAN EXECUTE NOW PASSES, AND THE ARM IS WIRED
(2026-08-16).** The probe is a `Deep recursion` section in
`codex/test/plug-oracle-arith.codex` and its five rows are in the truth
set, which goes from 28 values to 33.

| plug | class | what closed it |
|---|---|---|
| `csharp` | 1 | already had it: `opening-on-big-stack`, a 512 MB thread |
| `zig` | 1 | already had it: `std.Thread.spawn` with `stack_size` 512 MB. Untouched; Steve's |
| `python` | counter | `sys.setrecursionlimit(1000000)`, one line, no thread |
| `javascript` | 1 | `worker_threads` with `resourceLimits.stackSizeMb = 512` |
| `wasm` | host | **nothing in the plug.** The module is correct and the host's limit is the constraint |

**`javascript` was the one that could have been written off as class 3.**
node's `--stack-size` is a process flag that emitted source cannot set,
which is a good argument for a recorded divergence and it is wrong:
`worker_threads` takes `resourceLimits.stackSizeMb` from inside the
program. Measured, 100,000 mutual frames raise `RangeError` on the main
thread and complete on a 512 MB worker. The emitted module re-runs itself
in the worker, so only the entry call is guarded by `isMainThread`; a
throw inside the worker is re-raised on the main thread rather than
swallowed, checked by running a program that throws and confirming exit 1
with the prior stdout intact.

**`wasm` needed no plug change and that is the answer, not a dodge.** With
`wasmtime run -W max-wasm-stack=268435456` the same module answers all six
rows; with the default it answers three and traps `call stack exhausted`.
Bare metal answers deep recursion with a multi-gigabyte arena, so a host
stack small enough to refuse it is grading the HOST. The oracle arm passes
the flag and says why.

**`zig`'s arm is GREEN and this item never owned it.** The arm was red for a
missing `list-snoc` emitter after 1.7 landed `snoc-len` in the oracle subject.
`ZigEmitter.codex:787` maps `list-snoc` to `cx_ll_push` and the arm passes all
40 values (reek, 2026-08-18). Rebuild the plug before re-measuring: a binary
older than its source is what produced the red reading, and the harness now
refuses one.

0. ~~The entry-point inventory~~ done, above.
1. ~~`python`~~ **DONE 2026-08-16.** `py-emit-entry`
   (`PythonEmitter.codex:929`) emits `import sys` and
   `sys.setrecursionlimit(1000000)` ahead of the entry call, and only when
   there IS an entry call, so a library-shaped emission is untouched. The
   probe now answers all six rows exactly as x86-64 does, where the depot
   plug died at depth 1,000; `plug-oracle-test.ps1 -Only python` is still
   28 of 28.
2. ~~The class-1 plugs whose runtimes are on this box~~ done: `javascript`
   fixed, `wasm` answered, `csharp` and `zig` already carried it.
3. **What is left is the 42 plugs whose runtime is NOT on this box**, and it
   is root's from 2026-08-17 (red's reassignment; val is fortran-deep), one
   CL per family, each read and not run:
   - JVM family (`java`, `kotlin`, `scala`, `groovy`, `clojure`, `compose`):
     DONE 2026-08-17, `Thread(null, runnable, "codex-main", 512L * 1024 *
     1024)` then start and join, the `csharp` constant; the emitted entry of
     `plug-oracle-arith` through all six was read. `compose` runs it from
     `onCreate` and joins on the UI thread, which is what its synchronous
     `opening()` call already did.
   - native family (`rust`, `d`, `swift`, `swiftui`, `objc`, `ada`, `pascal`):
     DONE 2026-08-17, each in the language's own thread-with-stack-size:
     rust `std::thread::Builder::new().stack_size(512 * 1024 * 1024)`, d
     `new core.thread.Thread(&opening, 512 * 1024 * 1024)`, swift/swiftui a
     `Thread` with `stackSize` and a `DispatchSemaphore` to wait, objc an
     `NSThread` with `stackSize` and `dispatch_semaphore`, ada a `task
     Codex_Main` with `pragma Storage_Size (512 * 1024 * 1024)` whose body is
     the entry (the main procedure waits for it as its master), pascal a
     `TThread` descendant created with `StackSize` 512 MB and `WaitFor` (uses
     `Classes`). `qt` is class 3: its program is JavaScript run by QML's own
     engine inside `ApplicationWindow`, and nothing emitted source can say
     sets that engine's stack; recorded, not worked.
   - node targets: `typescript` DONE 2026-08-17, the `javascript`
     `worker_threads` shape with `declare const require/__filename/process`
     so it typechecks without `@types/node`; and this one RAN, because node
     24 executes `.ts` directly: the oracle subject answers its first twelve
     values and dies at `int_mod`, a prelude gap that is now plugs-backlog
     1.39 (typescript as an oracle arm). `electron` is class 3, not class 1
     as the row first listed it: its program runs in Chromium's renderer,
     whose stack no emitted source can set.
   - `.NET` UI shells (`maui`, `wpf`, `winforms`): class 1 by mechanism (the
     `csharp` thread) but NOT applied: each calls `opening()` from a UI
     constructor and collects into `_output`, which the shell binds to the
     UI thread, so a worker writing it needs a dispatcher hop that nothing on
     this box can measure. Recorded; whoever runs one applies it with the hop.
   - `gtk` is Python GTK4: the `python` counter, `sys.setrecursionlimit
     (1000000)` in its imports, DONE 2026-08-17.
   - `fortran` (val, 2026-08-18). **Two separate questions were tangled in
     this row and only one of them is about depth.** The first is
     CONFORMANCE and is now fixed: every emitted function carried no
     `RECURSIVE` prefix and returned through its own name, so a recursive
     call was not standard-conforming at all under `-std=f95/f2003/f2008`,
     and depth was moot while any recursion was illegal. Both emission
     paths now emit `recursive function f(..) result(f_r)`; the account and
     its measurement are in plugs-backlog 1.7 stage 9.

     The second is DEPTH, and **fortran reads as class 3, recorded as a
     READING and NOT ablated.** A Fortran program has no standard way to
     ask for a bigger stack: there is no portable threading in the
     language, so the class-1 shape every other native target uses has
     nothing to bind to. The depth available is a property of the process
     (`ulimit -s`, or the linker's stack reserve on Windows) or of
     OpenMP's `OMP_STACKSIZE`, and all three are set OUTSIDE the emitted
     source, which is exactly `javascript`'s `--stack-size` situation and
     the same close.

     **Do not promote this to a finding without the ablation.** The design
     says establish the class by mechanism-plus-ablation and python is the
     standing reason: it read as class 1 from the outside and was a
     counter. There is no gfortran on this box, so nothing here has been
     run. When one appears the arm is already written: the oracle's rows 31
     and 33 recurse 100,000 deep (`ping 100000`, `sum-to 100000`), and
     `ping`/`pong` is the mutual pair no self-TCO pass can flatten. The
     order is: compile at default stack and confirm it dies; raise
     `ulimit -s` alone and see whether that ALONE fixes it; only then
     conclude the source has no lever.
   - class 2, nothing to emit, each a READING of the runtime's mechanism and
     not a measurement (2026-08-17): `go` (goroutine stacks grow, the main
     goroutine included, to a 1 GB default maximum on 64-bit); `elixir` (a
     BEAM process stack lives on the heap and grows); `haskell` (GHC's stack
     is heap-allocated and bounded by `-K`, default 80 percent of memory);
     `perl` (the interpreter stack is heap, deep recursion is a warning);
     `php` (call frames are heap, no depth limit without xdebug); `scheme`
     (the emitted program is implementation-neutral and every common
     implementation keeps continuations on the heap). Confirm with the probe
     when a runtime is present; a wrong reading here costs one row.
   - class 3, recorded divergences: `ruby` (VM stack size is the environment
     variable `RUBY_THREAD_VM_STACK_SIZE` read at startup, not settable from
     the program), `julia` (task stacks are fixed at spawn and not sized by
     the program), `lua` (Lua-to-Lua depth is bounded by `LUAI_MAXSTACK`
     slots, a build constant), `ocaml` (the main fiber runs on the system
     stack, `ulimit -s`), `nim` (no runtime stack size for the main thread),
     `cobol` (no thread), `flutter` (Dart isolates have fixed stacks),
     `electron`, `qt` and the browser targets `angular`, `react`, `vue`,
     `svelte`, `html` (the engine's stack is the host's). A caller of any of
     these may rely on self recursion (every plug loops it) and not on
     mutual or non-tail depth past the host's stack.
   - `fortran` stays val's.
   Six 
   toolchains exist here and no more, so every one of those can be taken
   only as far as the emitted source. The entry-point inventory above says
   where each one calls `opening`; the work per plug is one wrapper, and
   the honest CL says it was read and not run.
4. The class-2 confirmations, which are cheap and may empty a row. `go`
   (segmented goroutine stacks), `elixir` (BEAM grows a process stack on
   the heap) and `haskell` (heap-allocated stack) are the candidates to
   need nothing; none can be executed here to prove it.
5. ~~The class-3 divergences, recorded in the register~~ **DONE 2026-08-19
   (val), and it took the class-2 rows with it.** `plugs-backlog.md` 1.14 now
   carries "What a caller may rely on, per plug": one table, every plug in a
   class, and for each what a caller may and may not depend on. It lives in
   the REGISTER rather than only here because this design is Active and goes
   to `Done/` when the campaign closes, and a caller asking whether the ruby
   plug can recurse deeply should not have to find an archived design. The
   table states in its own text that every class-2 and class-3 row is a
   READING of the mechanism and not a measurement, with `javascript` and
   `python` named as the two the campaign paid for. **The bar for calling a
   plug class 3 is higher than it looks** -- `javascript` was the obvious
   candidate and turned out to be class 1.

**Re-measured 2026-08-19 (val) before proposing to archive this: plug-oracle
is 6 of 6, 49 values each.** All six plug binaries were STALE first and the
harness refused to score them, so nothing had re-scored these arms since the
emitters last moved; rebuild before reading the green.

**A red published from this campaign the same day was WRONG and is
retracted.** It said the typescript arm failed and offered a three-row table
claiming the seed and the VM had been ablated away. The failure reproduced,
but **all three rows built the plug with the same kernel**, so it was one
configuration run three times.
`codex/plugs/common/plug-build-lib.ps1` calls `compile.ps1` with no `-Kernel`,
which falls back to `build-output/bare-metal/Codex.cdx` -- neither the SUT nor
the seed, holding whichever kernel ran last, three days stale here. That is the
trap `CLAUDE.md` names under R-GATE, and knowing it was there did not stop me
walking into it: the seed I kept syncing was never the kernel doing the
building. Pointed at the depot seed, the same plug source passes. The durable
half is in `plugs-backlog.md` 1.14: every plug in the tree is built against
whatever kernel is lying in `build-output/bare-metal/`, which is a
reproducibility gap in the plug build.

**With that corrected, nothing in this campaign is red.** What remains is
runtime-gated: fortran's depth ablation wants gfortran, the class-2
confirmations want their runtimes, and the .NET UI shells want a dispatcher hop
nobody here can measure. Steps 0, 1, 2 and 5 are done and step 3 is done as far
as emission goes.

**The probe was not wired until the plugs it grades were fixed**, on
1.13's ruling. It is wired now, and five of the six arms pass every value of
the oracle subject: `python`, `javascript`, `zig`, `wasm` and
`csharp`. `codex/plugs/zig/**` is ordinary fleet code (Damian, 2026-08-18)
and is in scope like any other plug.
