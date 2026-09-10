# Deep recursion through a plug

*Plugs 1.14. Raised by PR 64 (Steve Howell).*

## The gap

Codex source assumes deep recursion is free. Bare metal answers it with a
multi-gigabyte arena; a conventional runtime answers it with a fixed
stack, and .NET gives its main thread 1 MB. `Parser.codex` at 18,812
tokens overflowed zig's 8 MB main-thread stack, and ReleaseFast does not
rescue it: the calls sit inside labelled block expressions and LLVM does
not turn them into loops.

**Emitting a loop for self-tail-calls does not close it.** The case that
reaches the limit is MUTUAL recursion, the lexer's `scan-token` ->
`skip-prose-line` -> `scan-token` cycle, which no self-TCO pass can
flatten. The plugs already have self-TCO: the python plug emits
`while True:` with a reassignment where the source recursed, and `down
100000` is green everywhere.

## The probe and what it measures

The probe is a `Deep recursion` section in
`codex/test/plug-oracle-arith.codex`, wired, and its five rows are in the
truth set. Four definitions at two depths: `down` (self, tail),
`ping`/`pong` (mutual, tail), `sum-to` (self, NOT tail), each at 1,000 and
100,000.

Two properties the arm must keep:

- **Both shapes, self and mutual, at both depths.** A self-only arm is
  green on every plug that has TCO and says nothing about the defect. The
  pair is what separates "has a TCO pass" from "has a stack".
- **A non-tail row (`sum-to`).** Nothing can flatten it, so it is the row
  that cannot be answered by any pass, only by frames. It is also the only
  row whose ANSWER is a large number rather than 0, so a plug that returns
  a wrong value rather than dying is caught.

**Do not measure this by reading the emitted source.** Every failure here
is a runtime failure in a program that compiled. For the plugs whose
runtime is not on this box, the item can be taken only as far as the
emission, and the CL says so.

## The classes, and how a plug is assigned one

Three classes, and which class a language is in decides whether there is
any work at all.

**Class 1, the stack is a thread property and the plug can ask for a
bigger one.** Emit the entry point as a thread with an explicit stack
size, join it, and re-raise.

**Class 2, the runtime grows the stack and there is nothing to do.** Go
segments goroutine stacks; BEAM (elixir) grows a process stack on the
heap; Haskell's stack is heap-allocated.

**Class 3, the language cannot express it and the honest close is a
recorded divergence**, the shape 1.8 reached for `haskell`, `elixir` and
`clojure`.

**Class 4, the language EXPRESSES the fix.** `wasm` is the instance. The
tail-call proposal (`return_call`) ships in wasmtime and every major
browser engine and runs any tail call, mutual included, in the caller's
frame. The wasm plug emits it for every application in tail position that
saturates a known arity, and the compiler self-compiles byte-identically
at a 1 MB stack, the browser's real number, with no flag and no thread.
`sum-to` remains a true frame obligation there, at parity with x86, whose
own boot stack measures ~64 MB (a 10M-deep mutual probe double-faults it;
the reference has no mutual-TCO either, its stack is just bigger).

**Establish the class by ablation, not by the language's reputation, and
recheck a class-3 reading against what the target's spec has shipped
since.** python looked like class 1 from the outside and is not: its limit
is a counter and its fix is one line. javascript looked like class 3 and
is class 1: node's `--stack-size` is a process flag emitted source cannot
set, but `worker_threads` takes `resourceLimits.stackSizeMb` from inside
the program. The ablation that settles a class is cheap, apply each half
alone and see which one the failure follows, and the cost of skipping it
is a plug carrying thread machinery that does nothing, with a constant its
runtime refuses.

## python is a counter, not a stack (measured CPython 3.11.9, this box)

| arm | result |
|---|---|
| 128 MB thread + `setrecursionlimit(10^6)` | all six rows, matches x86-64 |
| big stack, DEFAULT counter | `RecursionError` at depth 1,000 |
| raised counter, DEFAULT thread stack | all six rows, exit 0 |
| raised counter, NO thread at all | all six rows, exit 0 |

**The counter is the whole fix and the thread buys nothing**, because
CPython 3.11 stopped consuming C stack for Python-to-Python calls: depth
is bounded by the counter and by nothing else. A fat-frame control (21
live locals, depth 100,000, main thread) also passes, so it is not that
the probe's frames are too small to notice.

Two consequences. **The 512 MB constant cannot be shared:**
`threading.stack_size(512MB)` is REFUSED outright on this box
(`ValueError: size not valid`), and 128 MB is the largest power of two
CPython accepts here, so a plug that ports the csharp constant literally
raises at import before it runs a line. And **the answer is CPython
version dependent**: on 3.10 and earlier the C stack does grow with Python
recursion, so the emitted form raises the counter unconditionally and
treats the thread as the fallback for an older runtime rather than the
mechanism.

## The entry-point inventory (read from source across 54 plug directories, 2026-08-16)

**Two plugs enlarge the stack and both use exactly 512 MB. Every other
plug that emits a runnable entry point calls it directly on the default
stack.**

| | plugs |
|---|---|
| **Enlarges the stack (2)** | `csharp` -- `opening-emit-entry` -> `opening-on-big-stack`, `CSharpEmitter.codex:774-798`, `new System.Threading.Thread(..., (int)(_stackMb * 1024 * 1024))` with `_stackMb = 512` at `:794`. `zig` -- `zig-main`, `ZigEmitter.codex:2343-2349`, `std.Thread.spawn(.{ .stack_size = 512 * 1024 * 1024 }, opening, .{})` then `t.join()`. |
| **No CPU entry wrapper, NA (10)** | `elf`, `img`, `pe` (container writers), `ptx`, `spirv`, `wgsl` (GPU: `opening` IS the kernel entry), `t3isa`, `arm64`, `riscv` (bare metal, a fixed boot SP), `babbage` (no thread concept on an Analytical Engine). |
| **Calls the entry directly on the default stack (the rest)** | `ada`, `angular`, `clojure`, `cobol`, `compose`, `d`, `electron`, `elixir`, `flutter`, `fortran`, `go`, `groovy`, `gtk`, `haskell`, `html`, `java`, `javascript`, `julia`, `kotlin`, `lua`, `maui`, `nim`, `objc`, `ocaml`, `pascal`, `perl`, `php`, `python`, `qt`, `react`, `ruby`, `rust`, `scala`, `scheme`, `svelte`, `swift`, `swiftui`, `typescript`, `vue`, `wasm`, `winforms`, `wpf`. |

**Six target toolchains exist on this box** (re-measured 2026-08-19 over
49 candidates): `node`, `python`, `dotnet`, `zig`, `wat2wasm` and
`wasmtime`, and nothing else. No `go`, `java`, `rustc`, `ruby`, `perl`,
`php`, `lua`, `julia`, `ghc`, `ocaml`, `swiftc`, `fpc`, `gnatmake`,
`gfortran`, `cobc` or BEAM. So of the 42 plugs in the third row, exactly
three can be EXECUTED here (`python`, `javascript`, `wasm`), one is
already done (`csharp`), and one is Steve's (`zig`). Everything else can
be taken as far as the emitted source and no further. **A class assignment
for a plug whose runtime is absent is a reading, and the CL says so.**

## Where the campaign stands

Every plug this box can execute passes, and the arm is wired: `python`,
`javascript`, `zig`, `wasm` and `csharp`, five of the oracle's six arms,
every value. Class 1 is applied as far as emission goes across the JVM,
native and node families; `gtk` takes the python counter.

**The caller contract lives in the register, not here.**
`plugs-backlog.md` 1.14 carries "What a caller may rely on, per plug": one
table, every plug in a class, and for each what a caller may and may not
depend on, stating in its own text that every class-2 and class-3 row is a
READING of the mechanism and not a measurement. It is in the register
because this design goes to `Done/` when the campaign closes and a caller
asking whether the ruby plug can recurse deeply should not have to find an
archived design.

Corrections to the class table that the inventory's first reading got
wrong: `qt` is class 3, because its program is JavaScript run by QML's own
engine inside `ApplicationWindow` and nothing emitted source can say sets
that engine's stack. `electron` is class 3, because its program runs in
Chromium's renderer.

## What is open, all of it runtime-gated

- **fortran's depth ablation** (val) wants gfortran, which is not on this
  box. fortran reads as class 3: a Fortran program has no standard way to
  ask for a bigger stack, there is no portable threading in the language,
  and the depth available is a property of the process (`ulimit -s`, or
  the linker's stack reserve on Windows) or of OpenMP's `OMP_STACKSIZE`,
  all set OUTSIDE the emitted source. **That is a READING and must not be
  promoted to a finding without the ablation**, python being the standing
  reason. When gfortran appears the arm is already written: the oracle's
  rows 31 and 33 recurse 100,000 deep (`ping 100000`, `sum-to 100000`) and
  `ping`/`pong` is the mutual pair no self-TCO pass can flatten. The order
  is: compile at default stack and confirm it dies; raise `ulimit -s`
  alone and see whether that ALONE fixes it; only then conclude the source
  has no lever.
- **The class-2 confirmations**, which are cheap and may empty a row:
  `go` (goroutine stacks grow, the main goroutine included, to a 1 GB
  default maximum on 64-bit), `elixir` (a BEAM process stack lives on the
  heap and grows), `haskell` (GHC's stack is heap-allocated and bounded by
  `-K`, default 80 percent of memory), `perl` (the interpreter stack is
  heap, deep recursion is a warning), `php` (call frames are heap, no
  depth limit without xdebug), `scheme` (every common implementation keeps
  continuations on the heap). Each is a reading; confirm with the probe
  when a runtime is present. A wrong reading here costs one row.
- **The .NET UI shells** (`maui`, `wpf`, `winforms`) are class 1 by
  mechanism (the `csharp` thread) and NOT applied: each calls `opening()`
  from a UI constructor and collects into `_output`, which the shell binds
  to the UI thread, so a worker writing it needs a dispatcher hop that
  nothing on this box can measure. Whoever runs one applies it with the
  hop.
- **Nine of 71 `compile.ps1` invocations under `codex/plugs` pass no
  explicit `-Kernel`** (fester, 2026-09-08), so a run driver can build
  against whatever kernel ran last. They are named in `plugs-backlog.md`
  1.101, which owns them. The plug BUILD half is fixed:
  `codex/plugs/common/plug-build-lib.ps1:163` passes
  `-Kernel <repo>/seed/Codex.cdx` on its one `compile.ps1` call.

`codex/plugs/zig/**` is ordinary fleet code (Damian, 2026-08-18) and is in
scope like any other plug.
