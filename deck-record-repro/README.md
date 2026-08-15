# Seven findings, runnable from this branch

NOT FOR MERGE -- this directory exists so the findings are runnable; drop
it whenever. The zig-plug changes in this PR and the `net-recv-raw` fix
are separate and are meant to merge.

They all came out of one exercise, run as a ladder: for each compiler
phase, bundle the chapters that implement it into a standalone subject
with a dump harness, compile it two ways -- seed on bare metal as truth,
and through the zig plug -- and require the two to agree byte for byte.
The same oracle discipline as `plug-oracle-test`, with the compiler's own
source as the subject. Five rungs pass today: lex, parse, desugar, scope
and check, the last of them running `Types/TypeChecker.codex` and friends
over a real chapter.

Several of these only appear once a plug carries more payload than plugs
usually carry, and the later ones only appear once a plug meets code the
earlier rungs never reached. Nothing here is exotic; it is ordinary code
meeting a bigger input.

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

## 4. TypeChecker uses `capability-names` without citing Capability

Small, and only visible from outside a whole-foreword build.

`Types/TypeChecker.codex:3400` is

```
  capability-vocabulary : List Text
  capability-vocabulary = capability-names
```

and `capability-names` is defined in `foreword/core/Capability.codex:198`.
TypeChecker's cites are Build Settings, Phase Allocator and Tuple. There
is no cite for Capability.

Nothing is broken in the real build, because the whole foreword is
present and the name resolves. It surfaces when a subset of the compiler
is bundled into one unit -- we hit it building a type-check subject for
the plug oracle, where the bundler carries only what is named or cited and
the definition was simply absent.

Worth a one-line cite if you want the dependency declared. We mention it
because it is the same shape as the `deck-record` intercept above:
something the monolithic build makes invisible, which a subset build
notices immediately. If you care about the subset property -- and the plug
bundles are exactly that -- these are the cases that break it.

## 5. An unreachable match arm passes without a word

`Types/TypeCheckerInference.codex:665-666`, in `lint-arg-narrowing`:

```
   in when declared-param
    is IntegerTy (lo) (hi) (mode) ->
     ...
    is otherwise -> st
    is otherwise -> st
```

Two identical catch-alls at the same indentation, in the same `when`. The
second cannot be reached. It looks like a copy-paste slip rather than
intent, and it compiles silently.

We scanned the rest of `codex/compiler` for the same shape and this is the
**only** instance -- 97 other consecutive `is otherwise` pairs are nested
`when` expressions where an inner catch-all sits directly above an outer
one, which is legitimate. So the dead arm is a one-off; the diagnostic gap
is the finding.

**The gap.** Codex says a great deal about far subtler hazards. CDX3005
spends a paragraph on shadowing a builtin, and rightly -- it explains that
the danger is cost rather than answer, and cites the Hamt case that sent
four chapters quadratic. CDX1070 refuses an application that ends at a
newline and names three ways to fix it. Against that, an arm that can
never run seems like something you would want to hear about, and nothing
says anything.

We noticed because zig rejects a second `else` prong outright, so the
emitted code would not compile. Our plug drops the later catch-all, which
is safe precisely because it is unreachable -- but the reason we looked was
a zig error, not a codex one.

Offered as a diagnostic suggestion rather than a bug: an unreachable-arm
warning would have caught this, and the compiler already has the arm list
in hand where it checks exhaustiveness.

## 6. `lift-lambdas` exists, and runs after the plug wire is written

`codex/compiler/IR/LambdaLifting.codex` is a complete lambda-lifting pass:

```
lift-lambdas        : IRChapter, Integer -> IRChapter
FreeVar             = record { name : Text, type-val : CodexType }
collect-free-vars   : IRExpr, SkipListText, SkipListText, List FreeVar -> List FreeVar
build-lifted-params : List FreeVar, List IRParam -> List IRParam
build-partial-app   : Text, CodexType, List FreeVar, Integer, Integer, SourceSpan -> IRExpr
```

It has one caller, `opening.codex:831`, in the `cdx-chapter` path with its
own LIFT phase and deck budget. The plug path is `emit-ir-cce`, which runs
`compile-frontend-ir` and the named IR pipeline and never reaches it. So
the IR on the wire still contains lambdas that close over enclosing
locals, and nothing in the plug interface says so.

**What that cost here.** Zig admits a comptime constant into a nested
function and refuses a runtime value, so
`map-list (\a -> resolve-type-expr tdm a) args` cannot be emitted as
written: `tdm` is a parameter of the definition around it. Getting the
check rung green meant adding free-variable analysis to the zig emitter --
a list of the names that are runtime locals of the function being emitted,
threaded through definition parameters, let bindings, match binders and
act bindings, intersected with the names each lambda body mentions, with a
shadowing rule so a lambda parameter that hides an enclosing local is not
mistaken for a capture.

Almost none of that was necessary. `lift-lambdas` rewrites the use site as
a **partial application** of the lifted definition, and this plug already
handled partial application -- `zig-closure-make` builds the environment
struct and the trampoline, and predates this work. Lifted IR would have
arrived in a form the plug already knew how to emit.

`FreeVar` also carries `type-val`. The plug has no equivalent, so each
capture's field type is `@TypeOf` of the value hoisted outside the
environment struct, because read from within it the enclosing local is
exactly what zig will not name. That works, but it is a workaround for
type information this pass already computes.

**Not a request to move it.** Lifting is a pessimization for any target
that has closures, which is most of them: C#, JS, Haskell, Clojure and
Elixir all want the lambda to stay a lambda. Running it before the fork
would make the primary consumers worse to make the tertiary ones simpler.
Opt-in is the right shape, and the mechanism for that already exists and
is already used for a plug:

```
default-ir-pipeline   = ["fold-constants", "inline-leaf-calls", "inline-single-caller"]
text-plug-ir-pipeline = ["fold-constants"]
```

`lift-lambdas` is a phase with its own deck budget rather than a
registrable pass, so this is not a one-line change. But `passes=` is
already the place where a plug says which transforms it wants.

**The smaller export may be the better one.** This plug never wanted
lifting. It kept each lambda where it was and only needed to know the
lambda's free variables and their types. A plug targeting C would want the
whole pass; a plug targeting a language with closures wants neither;
this one wanted only the query. `collect-free-vars` is cheaper to expose
than `lift-lambdas` and serves more targets.

Offered as an observation with a measurement attached rather than a
request. "Tertiary plugs pay this, that is the deal" is a legitimate
answer.

## 7. There is no `IRExpr` map or fold, so every plug rewrites the walk

`Types/CodexTypeTree.codex` gives `codex-type-map-children` and
`codex-type-fold-children`, and consumers build on those instead of
re-enumerating the type constructors. `IRExpr` has no equivalent, so a plug
that needs to ask anything about an IR subtree enumerates all 24 of them
itself. This one has `zig-occurs`, a 24-arm `when` answering "does this
expression mention this name", used to decide whether a `let` binding is
dead, whether a parameter needs a discard, and which names a lambda
captures. Finding 6's free-variable analysis is built on it.

Stated as duplication rather than as a bug, because we went looking for the
obvious hazard and did not find it. The one arm in `zig-occurs` that does
not descend fully is `IrHandle`, which skips its clauses -- and this plug
also drops handler clauses at emission, so the walk is consistent with the
feature being unimplemented rather than with an oversight. `IrTry` and
`IrWithTimeout` looked like gaps at first glance and are not: their first
fields are `Integer`, not `IRExpr`.

So the cost we can demonstrate is only that the walk had to be written, not
that writing it went wrong. Worth weighing against the fact that the
precedent for the fix is already in the tree: the type tree got its map and
fold, and the IR did not.
