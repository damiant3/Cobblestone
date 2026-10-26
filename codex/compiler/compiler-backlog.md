# Compiler -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says
what is still missing and nothing else, a closed entry is DELETED, and
a gap that is still real is never quietly dropped. There is no
platform-wide register; `docs/PM/CurrentPlan.md` carries the shape.

**Next id: COMPILER-135.** A closed row is deleted, so the highest row in the
table is not the last id used (COMPILER-117 to 119 closed without a row here);
take this number for a new row and raise it in the same edit.

A deletion is proven seed-neutral only by the BINARY, never assumed: build the
candidate from the depot seed and compare it to that seed byte for byte outside
the signature window (offsets 40..135). COMPILER-64 came out identical and
shipped no seed; COMPILER-23's four unreferenced functions came out 228 bytes
smaller (main 24438), so code nothing references can still be present in the
shipped seed, and a removal that assumes otherwise lands a seed it never proved.

**Structural equality is minted in ONE place and every backend receives it.**
`eq-attach-helpers` (`opening.codex`) appends `eq-helper-defs` to the shared
IR, on both the x86-64 path and the IR wire the plugs consume, which is what
COMPILER-44 fixed after sending them to the emitter alone left every plug
comparing heap pointers. So a gap in derived equality is fixed once, in
`Emit/X86_64.codex` where the helpers are built, and NOT four times; an
estimate of four backend implementations was made and measured wrong on
2026-09-08. What decides whether a comparison reaches a helper at all is
`lower-eq-dispatch` (`IR/Lowering.codex`): it resolves the operand, takes the
type's name and its site actuals, and calls `__eq_<name>@<actuals>`; anything
it cannot name falls through to `IrBinary OpEq`, which for a heap value
compares the two POINTERS. That fallback is what made COMPILER-74 and
COMPILER-75 silent wrong answers rather than diagnostics, and it is the first
thing to check when a comparison answers False for two equal values.

| # | Capability | State of the gap |
|---|---|---|
| COMPILER-129 | **Java and the unaudited source plugs swallow a `try` that exhausts with no fallback** | `fail` unwinds to the nearest `try` and traps outside one on every native target. x86-64 (seed 7B644498): `try` builds a frame on its own stack per attempt (`emit-try-push-frame`, `Emit/X86_64.codex`), the chain's head lives in the running process's table entry at `try-frame-head-offset`, and `emit-fail-raise` restores the frame and jumps to the handler or prints the message and traps. arm64 and riscv keep the same frame in 16 and 18 spill slots of the `try` function, because both address spills from SP (`a64-emit-try`, `Arm64CodeGen2.codex`; `rv-emit-try`, `RiscVCodeGen2.codex`), and trap with `udf` and `ebreak` after printing. wasm (`emit-wat-try`, `WasmEmitter.codex`) has no unwind: a `fail` leaves its function at once, every call in a module that uses `fail` checks `$try_fail` on return (`wat-checked-call`), `$try_depth` counts active `try`s, and a `fail` with none traps (`unreachable`, no message printed); a module without `fail` emits byte-identical WAT. Arms on every target: `codex/test/fail-unwind`, `codex/test/fail-unwind-deck`, `codex/test/fail-outside-try` (`.fatal` on x86-64, graded by hand; `.cross-fatal` on arm64 and riscv). **Open:** the source plugs lower `trying` to native exceptions. Python, JavaScript, TypeScript, C#, Java, Perl and HTML raise `fail` with its message, fail outward on exhaustion with no fallback and on a failing fallback with no `on failure`, and stop on an uncaught `fail` with its message (graded by running `fail-unwind`, `try-fallback-fails` and `fail-outside-try` under `python`, `node`, `dotnet`, JDK 17 at `C:\Program Files (x86)\Android\openjdk\jdk-17.0.14\bin`, Git for Windows' perl 5.38 at `C:\Program Files\Git\usr\bin\perl.exe` (an uncaught `fail` dies with its message, exit 255), and headless Edge reading `#app` for HTML, where an uncaught `fail` prints its message on the page; TypeScript's `emit-ts-try` and HTML's `emit-js-try` are JavaScript's lowering, Java's is `_cx_try` in `JavaEmitter.codex`'s builtins, Perl's is `emit-pl-try`; a sabotaged exhaustion prints `never-exhausted` on C#, Java, Perl and HTML). Rust (`emit-rs-try`) still answers the empty fallback on exhaustion, and no Rust toolchain is on the box. Zig lowers `fail` as an error union (`ZigEmitter.codex`, Section: Failure): a definition that can let a failure escape (`zig-failing-set`, a fixed point over the defs) returns `anyerror!T`, every call to one catches into its handler (return, the enclosing `try` attempt's label, or `cx_fail_exit` in a lambda body), `try` is a labeled retry loop (`emit-zig-try`), and an uncaught failure prints its message and exits 1; only a call of `fail` raises, so a local named `fail` leaves its definition plain (`codex/test/zig-fail-local`, beside `zig-fail-raise`); graded the same way under `zig run`, sabotaged exhaustion prints `never-exhausted`, `plug-oracle-test -Only zig` 59/59. A failing definition reached as a closure value (named, partially applied, or a lambda) sets `cx_fail_pending` and returns undefined, and every closure invocation, over-application included, reads the flag and leaves through its handler, so a definition that invokes a closure is in the failing set too; a module with no failing definition emits neither (`codex/test/zig-fail-closure`). Census of the other source plugs (fester, 2026-10-07; none lowers `fail` with its message and none retries `max` times): the body alone, fallback dropped (angular, compose, electron, flutter, go, gtk, haskell, maui, objc, qt, react, svelte, swiftui, vue, winforms, wpf); catch everything and answer the fallback, so an exhausted `try` with no fallback is swallowed (clojure, d, elixir, groovy, julia, kotlin, lua, nim, ocaml, php, ruby, scala, scheme, swift); the body never runs, a constant stands in (ada `null`, fortran `0`, pascal `0`, cobol `CONTINUE` and `0`, babbage an `!UNSUPPORTED` card storing `0`). **Gap: every plug in this census and Rust is unfixed and ungradable here** (root, 2026-10-07: COMPILER-129 closes for Update 69 on the plugs a runtime on this box can grade, and no toolchain is installed for the rest): no toolchain for the language, or a GUI host the box cannot run headless (gtk imports `gi`; winforms, wpf and maui open windows; angular, react, svelte, vue, electron and qt need their frameworks). Owner: red (from fester, root 2026-10-16). |
| COMPILER-116 | **Constructor return annotations (GADT support): deferred, low priority** | Damian, 2026-10-01: "go ahead and log the gadt annotation work. its not hi pri though, unless we have a legit usecase." Implementing GADT support waits for a concrete program that needs it; a lane that finds one cites it here and asks root to raise the priority. Until then the parser deliberately refuses explicit constructor return annotations with CDX1080 `CtorReturnAnnotationUnsupported`, at the colon, before type checking. Ordinary constructor fields remain supported. Tracked crash repros are `codex/test/ctor-return-match.codex` and `ctor-return-no-match.codex`; removing only the annotations gives `ctor-return-control.codex`, which prints 0 then 7. On 2026-10-01, seed CC3FC5222D726096 (main 33794) trapped on both repros at `__list_set_at+0x18`, RIP 0x00101EFA. `build/compile.ps1` reported the same trap at requested 3072 MB and its automatic 8192 MB retry, heap 523.9 MB; these were capped VM runs, not proof of 8 GiB effective memory. The separate uncapped 8192 MB invocation triple-faulted during boot before parsing. Parser probes `ctor-return-parser.codex` and `ctor-return-recovery.codex` pin empty return metadata, EOF, following-constructor recovery and named refusal spans; `ctor-return-unused.codex` pins refusal independent of use. See the reproduction commands below. No GADT implementation belongs in COMPILER-109. |
| COMPILER-133 | **`par` and `race` have a type and no lowering** | Both rows type-check (`Types/Builtins.codex`), and a call fails CDX2040 "Unresolved call to 'par'" (likewise `race`), because nothing in `IR/Lowering.codex` or the emitters names them; `fork` and `await` lower through `IrFork` and `IrAwait`. No chapter in the tree calls either (2026-10-26). Owner: red. |
| COMPILER-134 | **The `s-*` session channels are broken from source** | `s-new`, `s-send`, `s-recv`, `s-recv-value` and `s-close` call the kernel channel helpers (`chan-kern-*`, `Emit/X86_64IPCHelpers.codex`), and their types thread a channel (`SChan (SSend a b)` to `SChan b`), but `s-send` and `s-recv` return the helper's `rax` (a status or the received value), so the next call receives that number as a channel: after `s-send c 64`, `s-recv-value` answers -1, and `s-close` on the result faults (`!EXC=0e`, RIP 6; scratch probes, seed 7BDD6E5F, 2026-10-26). The session type names cannot be written in source (CDX3008 `SChan`), only inferred. No chapter in the tree calls any of the five. Owner: red. |

## Constructor annotation reproduction

From the repository root, export the old seed to reproduce the trap. Use
`seed/Codex.cdx` as `-Kernel` to grade the installed refusal instead. The
negative sidecars require CDX1080 and its colon spans; a trap is never a pass.

```powershell
New-Item -ItemType Directory -Force build-output/compiler116-repro
p4 print -q -o build-output/compiler116-repro/cc3.cdx //Codex/main/seed/Codex.cdx@33794
pwsh build/compile.ps1 -Src codex/test/ctor-return-match.codex -Out build-output/compiler116-repro/match.cdx -Log build-output/compiler116-repro/match.log -Kernel build-output/compiler116-repro/cc3.cdx
pwsh build/compile.ps1 -Src codex/test/ctor-return-no-match.codex -Out build-output/compiler116-repro/no-match.cdx -Log build-output/compiler116-repro/no-match.log -Kernel build-output/compiler116-repro/cc3.cdx
pwsh build/compile.ps1 -Src codex/test/ctor-return-control.codex -Out build-output/compiler116-repro/control.cdx -Log build-output/compiler116-repro/control.log -Kernel build-output/compiler116-repro/cc3.cdx
pwsh build/test-run.ps1 -Kernel build-output/compiler116-repro/control.cdx -OutFile build-output/compiler116-repro/control.serial
```

The first two compiles fail on CC3; the control succeeds and its output must
equal `ctor-return-control.expected`. The original matched/no-match stderr
and corrected `reproduce.ps1` remain in
`D:/Projects/Cobblestone-reek/build-output/compiler109-ctor-fields/`.
That runner's success means the old trap was reproduced, not that annotated
constructors work. The tracked sources above are the durable repro inputs.

For the current parser and refusal fixtures, the sidecar-aware runner is:

```powershell
New-Item -ItemType Directory -Force build-output/compiler116-repro
Get-ChildItem codex/test/ctor-return-*.codex | ForEach-Object FullName | Set-Content build-output/compiler116-repro/subjects.txt
pwsh build/bvt.ps1 -CodexCdx seed/Codex.cdx -Jobs 1 -SubjectsFile build-output/compiler116-repro/subjects.txt
```
