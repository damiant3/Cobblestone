# Three findings, runnable from this branch

NOT FOR MERGE -- this directory exists so the findings are runnable; drop
it whenever. The zig-plug changes in this PR and the `net-recv-raw` fix
are separate and are meant to merge.

All three came out of one exercise: building a standalone subject that
does up-to-AST compilation (the real `Syntax/Lexer.codex`, then the
parser chapters, plus a dump harness), compiling it two ways -- seed on
bare metal as truth, and through the zig plug -- and requiring the two to
agree byte for byte. The same oracle discipline as `plug-oracle-test`,
with the compiler's own front end as the subject.

Two of the three only appear once a plug carries more payload than plugs
usually carry. Nothing here is exotic; it is ordinary code meeting a
bigger input.

## 1. `net-recv-raw` truncates odd-length frames

**Diagnosed, fixed in this PR, not compiled here.** Full write-up:
`PLUG_IR_TRANSPORT.md`. Read that one first if you read only one.

`emit-net-recv-raw-helper` derives its `rep insw` word count with
`shr rcx, 1`, which rounds down, so an odd-length frame loses its final
byte. The helper returns the full length anyway and the receive buffer is
never cleared, so that byte comes back as whatever the previous frame
left at the same offset. Silent, plausible, undiagnosed. Severity tracks
the number of odd frames: one gives a wrong program, 33-37 gives
`!EXC=06` inside `parse-expr`.

You have compensated for this before -- `ne2k_inject_rx` in
`tools/codex-vm.c` pads odd frames, with a comment naming the mechanism,
and `ip-total-length` is the guest half. Both are workarounds; the
receive path itself was never fixed, so it is sound only against an
emulator that pads for it. QEMU's `ne2k_isa` does not, and neither would
real hardware.

**Also worth your time:** nothing verifies receive-side TCP checksums. A
substituted payload byte reached the parser unchallenged.

## 2. The `deck-record` intercept fires on the name alone

**Reproducible, undecided -- we did not want to guess your intent.**

The x86-64 emitter intercepts any 1-argument call literally named
`deck-record` (`emit-apply` in
`codex/compiler/Emit/X86_64Compound.codex`) and emits `__deck-enter` /
evaluate-arg / `__deck-exit` instead of calling the function. In a unit
that never runs the compiler opening's phase-allocator initialization,
that corrupts allocator state and the program later reads garbage where a
pointer should be. Reproduces on the Update 40 and Update 41 seeds. The
decisive control: renaming the identity function to `my-id`,
byte-identical otherwise, passes.

`PlugTypes.codex` ships `deck-record : a -> a` so plug bundles
type-check outside the kernel, but the intercept fires on the name
regardless of who defined it -- so every plug kernel appears to execute
uninitialized deck enter/exit sequences today. The zig plug passes its
oracle anyway; our lexer subject died deterministically, which can only
be allocation-pattern luck.

Two contracts are possible and we did not want to pick one for you:
either units outside the compiler proper must initialize the phase
allocator (making the `PlugTypes` stub a trap), or `deck-record` outside
the compiler should degrade to a true identity (making the by-name
intercept want a guard -- perhaps firing only when the resolved callee is
the PhaseAllocator chapter's own def).

Each probe is self-contained, no cites, no other chapters. Compile with
`build/compile.ps1 -Src <file> -Out <out>.cdx` and run the cdx.

| file | deck-record | expected |
|---|---|---|
| `repro-crash.codex` | defined as `a -> a` identity, called at two sites | **page fault** (`!EXC` in `__linked_list_to_list`, garbage list pointer) |
| `control-renamed.codex` | byte-identical, every `deck-record` renamed `my-id` | passes: `toks 2` / `errs 0` |
| `probe-site-record.codex` | kept only around the record construction | page fault |
| `probe-site-ctor.codex` | kept only around nullary-ctor arguments | page fault |
| `probe-deck-init.codex` | as repro, plus `__deck-set __heap-save` first | still faults -- base init alone is not the fix |
| `probe-seeded-signal.codex` | none (control shape) | `toks 2` / `errs 1` / `e0 42` |

The shape is distilled from `Syntax/Lexer.codex` (`tokenize-collect`): a
state record threaded through a recursive collector via a variant
payload, with a LinkedList field read at the end.
`probe-seeded-signal.codex` is the honest-signal template -- a probe whose
expected output is an empty list cannot tell "correct" from "the misread
slot happened to hold zero", so it seeds 42 and demands it back.

## 3. `bytes-to-text` is O(n^2) in 42 of 44 plugs

**Fixed for the zig plug in this PR. The other 41 are untouched and will
hit the same wall at the same scale.**

Not a new discovery, and that is the point: `CSharpPlug.codex` and
`RecheckPlug.codex` already carry the linear version, with a comment
recording that the old accumulator "hung on the ~9.7MB compiler IR before
the plug emitted anything". The fix never propagated. The remaining
plugs still concatenate onto an accumulator per chunk, which copies the
accumulator every time.

For a 1.18 MB IR that is ~2.7 GB of allocation against a ~3 GB heap, so
the zig plug printed `OUT OF MEMORY` and emitted nothing.

One trap for anyone tempted by a smaller change: the 256-byte chunk is
deliberate. The inner loop is quadratic too, so cost is
`N^2/2C + N*C/2` and 256 sits near the `sqrt(N)` optimum. Raising it
alone makes things worse -- 8192 measured 4.6 GB and still died.
