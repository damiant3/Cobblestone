# Findings: six closed upstream, five standing, one fixed here

This directory holds the findings and the probes that make them runnable.
It is discussion material rather than a proposed addition to the tree --
drop it whenever, or take it; the zig-plug changes in this PR are separate
and stand on their own.

**Status re-checked against Update 45 / seed 270227BE, 2026-08-17.** Update 46
(`adfae029`, seed 12B07296) landed the same day and closed what was left of
finding 11; that is marked at its heading and not re-verified here, since the
ladder has not been re-banked against 46 yet.

Findings 1 through 5 were fixed by Update 43 and each is marked at its
heading. Finding 1's fix is verified present in this seed: `emit-net-recv-raw-helper`
carries the round-up as `st56a`/`st56b`, the same two instructions and the same
insertion point that were proposed (see PLUG_IR_TRANSPORT.md, now marked
resolved).

Two re-checked today and still standing:

- **Finding 7** -- no `IRExpr` map or fold exists in `codex/compiler`, so every
  plug still rewrites the walk.
- **Finding 10** -- `emit-record-set-builtin` still stores through the
  evaluated pointer and returns it, so `__record-set` still mutates and the
  `mutable` keyword still promises value semantics nothing delivers.

**Findings 6, 8 and 9 are not re-checked against this seed.** They were
measured on 2,798,031 and are recorded here as they were found.

**Finding 12 is new and fixed in this PR.** Finding 11 was withdrawn as filed --
the cause was ours -- and the one thing that survived it is closed in Update 46.

## 1. `net-recv-raw` truncates odd-length frames

**CLOSED in Update 43.** The count is rounded up before it reaches RBCR.
The follow-on closed too: TCP and IP checksums are now verified on
receive, and a frame claiming more bytes than it carries is refused.

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

**CLOSED in Update 43.** The intercept compares the chapter that defined
`deck-record` instead of matching the bare name.

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

**CLOSED in Update 43, and generalised past what was asked.** Rather than
fixing the copies, `PlugTypes` now holds the one linear definition every
plug shares.

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

**CLOSED in Update 43, by an instrument rather than a cite.** The cite was
added, and then `build/check-subset-cites.ps1` was written to build every
chapter against only what it cites -- which caught `BootPaint` borrowing
`to-unicode` with no cite on its first run. This was the better answer:
the finding was one instance of a class that only a subset build can see,
and the class is now measured from the inside.

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

**CLOSED in Update 43.** CDX2096 refuses an arm nothing can reach.

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

## 8. passes=text-plug changes the IR's type vocabulary, and nothing says so

`IR/Passes.codex` tells a plug that emits SOURCE to drop the inline passes,
and says why in so many words:

```
 A plug that emits SOURCE resolves a call by its name, so a pass that
 substitutes a body and deletes the call deletes the plug's only handle on
 it. The inline passes are therefore absent here and must stay absent.

  text-plug-ir-pipeline : List Text
  text-plug-ir-pipeline = ["fold-constants"]
```

Good advice, and taking it changes more than which calls survive. It changes
which TYPE CONSTRUCTORS reach the wire.

`probe-forall-sort.codex` in this directory is the reproducer: bundle it
with `codex/foreword/core/Sort.codex` and nothing else, compile IR-CCE, and
read the def. Compile the same `sort-by` inside a large unit under each
pipeline and the definition's type differs:

```
default-ir-pipeline    (fn (list (tvar 51)) (fn ... (list (tvar 51))))
text-plug-ir-pipeline  (forall 51 (fn (list (tvar 51)) (fn ... (list (tvar 51)))))
```

With the inline passes on, a polymorphic definition arrives already
specialised and its quantifier is gone. With them off it arrives quantified,
and `ForAllTy` appears on the wire where the machine-code plugs reading the
same IR-CCE never see it.

**What it cost us.** This plug handled `ForAllEff`, the effect-level
quantifier, in eight places, and `ForAllTy` in none -- so a quantified type
had no arrows to count, could not be peeled to find a return type, and had
no zig rendering at all. It surfaced as `sort-by` emitted with return type
`void` around a body returning a list, three phases away from the cause, and
only because zig objected to the mismatch. Six milestones had passed on the
default pipeline without ever meeting the constructor.

That is our defect to fix and we have fixed it. The finding is that nothing
warns a plug author this is coming. The prose above is careful to say the
inline passes matter to a source plug; it does not mention that following
the advice widens the type vocabulary the plug must handle. A plug developed
and tested against the default pipeline is not tested against the IR it will
actually receive once it does the recommended thing.

Worth a look across the fleet: any plug that emits source, uses
`text-plug-ir-pipeline`, and has no `ForAllTy` arm has the same hole. The
symptom is not a crash but a wrong type, which is the kind that travels.

Two smaller notes from the same trail, neither demonstrated to have fired,
both offered only as things we noticed:

`lower-def` reads a definition's type with `lookup-type-bsearch types rn`,
which answers `ErrorTy` from three separate paths -- empty list, position
past the end, name mismatch at the position found -- and never distinguishes
"no such binding" from "the binding is an error type". A definition that
silently loses its type is hard to trace back from, as this one was.

`zig-peel-return`'s analogue in any plug has the same shape: peeling N
arrows off a type with fewer than N returns what is left rather than
reporting the shortfall. Combined with the above, a missing type becomes a
plausible wrong type rather than an error.

## 9. An undefined type name in an annotation compiles without a word

Misspell a value name anywhere and CDX3002 says so. Misspell a TYPE name
in an annotation and nothing does: the unit compiles clean, runs, and
answers correctly around the phantom.

Three probes, each one chapter:

- `probe-phantom-field.codex` -- a record field typed `List PhantomType`,
  with `PhantomType` defined nowhere. Compiles with zero diagnostics; the
  record is constructed with `[]` for the phantom field, the program runs
  and prints its other field.
- `probe-phantom-bare.codex` -- the field typed bare `PhantomBare`, no
  List wrapper. Same silence.
- `probe-phantom-sig.codex` -- a definition signature
  `f : PhantomSig -> Integer`. Same silence, so the hole is not specific
  to field position: type names in annotations are simply never
  existence-checked the way value names are.

Found by bundling: our lir subject carried `CodegenState` before its
`TypeBinding` field type's chapter was in the unit, and bare metal
accepted it -- then again with `RenameEntry`, which `TypeEnv` names in a
field without citing ChapterScoper. The monolithic build always carries
every definition, so the miss costs nothing there; a subset build sails
through compilation and the mistake surfaces later, somewhere else, or
never. The zig plug is stricter than the seed here only by accident of
its target: zig demands the struct exist.

Seed F3722EAC (Update 43), QEMU/TCG, verified against all three probes
on 2026-08-16.

## 10. `__record-set` mutates, and only two lines in the tree depend on it

Every plug has to decide whether `__record-set r "f" v` returns a new
record or the same one modified. Nothing in the language says. The
declaration syntax offers a `mutable` keyword on records, which reads as
a promise that plain records are values -- and `CodegenState` is a plain
record.

Bare metal does not read it that way. `emit-record-set-builtin`
(X86_64Builtins.codex) evaluates the record to a pointer, stores the
field through it with `emit-narrow-store-proven`, and returns that same
pointer. There is no copy anywhere in the function, for mutable and
plain records alike. The C# plug agrees:

    _Buf.rset(record, __rs => { __rs.field = v; })

a lambda that assigns through a reference and hands the object back. So
the rule is that `__record-set` mutates. The `mutable` keyword selects
something else -- whatever it selects, it is not this.

The rule is invisible almost everywhere, because the ordinary shape is
bind-the-result-and-use-the-result: 354 `__record-set` calls in the fibx
subject and copy would serve for all but two of them. It takes an ALIAS
-- reading a binding made before the update -- for the two answers to
differ, and the x86 back end aliases in exactly two places:

`X86_64Helpers.codex:1539`

    in let st12c = emit-list-tail st12b
    in let st14 = emit-text-concat-list st12b      <- st12b, not st12c

`X86_64Helpers.codex:453-457`

    in let st25 = st-append-code st24 (mov-store reg-r13 reg-r15 0)
    in let st26 = st-append-code st25 (mov-rr reg-rax reg-r13)
    in let st27 = st-append-code st26 (pop-r r15 & r14 & r13 & r12 & rbx)
    in st-append-code st25 x86-ret                 <- st25, not st27

Both are correct under mutation and both read as typos, the second
especially: `emit-unicode-bytes-to-text-helper` sits twelve lines below
with the identical shape and ends `st-append-code st27 x86-ret`. A
reader cannot tell the pair apart by intent, only by running them.

Under value semantics the damage is silent and specific. The emitted
machine code loses `__list_tail`'s entire 69-byte body -- its name still
recorded in the offset table, so the function count is unchanged -- and
loses a 12-byte epilogue, leaving a helper that pushes five callee-saved
registers and never pops them. 81 bytes short out of 45,432, found only
by diffing the fibx subject's emitted code against bare metal.

Three things might be worth doing, in ascending order of appetite:

1. Say it somewhere. One sentence in the `__record-set` docs -- "returns
   the record, mutated in place" -- costs nothing and every future plug
   reads it.
2. Rewrite the two sites to use the binding they mean (`st12c`, `st27`).
   The output is identical under mutation and they stop reading as bugs.
3. Decide what `mutable` on a record declaration is for, given that
   plain records already have reference semantics. If it is vestigial,
   dropping it removes a promise the implementation does not keep.

Found by the fibx rung: the x86 code generator compiling fib, emitted
two ways. The zig plug had given plain records value semantics on the
strength of the declaration, which is why the divergence appeared at all
-- and it is now one representation, a pointer, matching bare metal and
C#. Seed F3722EAC (Update 43), QEMU/TCG, 2026-08-16.

## Finding 11 (WITHDRAWN as filed; what is left is narrower)

**The original claim was wrong and the cause was ours.** It reported a
DiagnosticBag reporting 72 errors with an empty diagnostics list, and asked why
the two targets disagreed about the number. The answer is that our harness
stands in for `opening.codex` and skipped a phase the driver runs:

    type-map      = build-type-def-map (checked.scoped.type-defs) ...
    stds          = sort-bindings (type-map & checked.all-bindings)
    resolved-defs = rewrite-ir-defs stds (ir.defs) resolve-ceiling

Without `type-map` the emitter's `st.type-defs` has no entry for a record
declared in the subject, so `resolve-constructed-ty` fails, and without
`rewrite-ir-defs` the IR still carries unresolved `ConstructedTy` annotations
into emission. Everything downstream followed from that. Nothing here was a
defect in the depot.

### What survives: two record layouts that disagree

**CLOSED in Update 46** (`adfae029`, seed 12B07296). The unresolved-type
fallback -- the by-list layout below -- is deleted, and the branch now raises
the same refusal `emit-field-access` raises, which is the asymmetry this
section asked about. The release gate and the BVT never took the branch, so it
was latent exactly as described. Credited to PR 67 in the change and in
`docs/PM/Active/GitHubUpdates/GitHubUpdate46.md`.

The account below is kept as filed. Nothing on our side worked around it: our
half was a harness that skipped RESOLVE, and running RESOLVE is the driver's
shape whether or not the fallback exists.

`emit-record` (X86_64Compound.codex:1751) chooses a layout by whether the
record's type resolved:

| | ranked by | offsets |
|---|---|---|
| type resolves -- `build-cce-byte-offsets` | `field-sort-key` = `width-prefix(width) & name` | cumulative field widths |
| type does not -- `emit-store-record-fields-by-list` | the raw field name | `rank * 8` |

Every reader uses the first rule unconditionally: `cce-byte-offset-and-type`
(via `accumulate-offset-width-sort`) in `emit-field-access` and in
`emit-record-set-builtin`. The two rules coincide only when every field is
eight bytes wide, because only then does the width prefix stop affecting the
order and the uniform slot match the real width.

`probe-record-layout.codex` is the smallest record where they part:

    Box = record { flag : Boolean, items : List Integer }

    resolved:  items @ 0   flag @ 8    size 9
    fallback:  flag  @ 0   items @ 8   size 16

A Box built through the fallback puts `flag` where `items` is read from, so
`b.items` yields the boolean -- 0 or 1 -- and `list-length` of that reads
`[0 - 8]`, which faults with CR2 at the top of the address space. That is
exactly the crash we chased: `__list_snoc + 3`, `RDI = 0`,
`CR2 = fffffffffffffff8`, called from `bag-add`.

### What is NOT established

**We could not reach the divergent path through `opening.codex`.** The probe
compiles and prints `3` through the normal driver, because the type resolves and
the first layout is used. We reached the second one only with a driver that
skips RESOLVE, which is our bug and now fixed. So this is a latent inconsistency
visible by inspection, not a demonstrated defect, and it may be unreachable in
practice.

The part that seems worth a look regardless is the asymmetry. Faced with the
same unresolved type, `emit-field-access` records an error and emits `ud2` --
it refuses, loudly. `emit-record` silently lays the record out by a different
rule than every reader will use. If the fallback is genuinely unreachable, it
could be an error instead; if it is reachable, it is silent corruption.


## 12. `__deck-set` is emitted without its argument, and zig will not compile the result

**Found 2026-08-17 against Update 45 / seed 270227BE. Fixed in this PR.**

`ZigEmitter.codex` mapped the builtin to a bare constant and never touched
`args`:

    ZigBuiltinEmitter { name = "__deck-set", emit = \args ctx d ty -> "0" },

Answering `0` is right. There is no deck in the zig target, so pointing the
deck cell somewhere is genuinely a no-op. Dropping the argument is the defect:
the argument is a binding at the call site, and zig refuses to compile a
binding whose only consumer disappeared.

    error: unused local constant
    b2: { const deck_base = cx_heap_save(); break :b2 b3: { _ = 0; ...

The remedy was already written two lines below, in the prelude, for the same
problem:

    // address-of answers 0 here by X86_64Compound's own account of
    // targets without it; the argument is still evaluated so its binding
    // stays used.
    fn cx_address_of(v: anytype) i64 { _ = v; return 0; }

So this is the second instance of a pattern that has a solved first instance
sitting beside it. `__deck-set` is the only other entry in the builtins table
that ignores `args` and takes one -- `__deck-enter`, `__deck-exit` and
`__deck-pos` also ignore `args`, and all three are nullary.

### Why it stayed latent

Both of the compiler's own callers use the address for something else as well,
so the binding survives on its second consumer:

    build (size) =
     let p = __heap-save
     in let deck-init = __deck-set p
     in let guarded = deck-reservation-guard p size      <- p used again

    init-phase-allocator =
     let base = __heap-save
     in let deck-init = __deck-set base
     in base                                             <- and here

`whole` transpiles both of those functions to zig and compiles clean. It took a
caller that only sets, which is what our harness prologue is once it names
`init-phase-allocator` to turn the deck intrinsic on.

The general shape is worth carrying to the other 43 plugs: a target that has no
analogue for a builtin still has to consume that builtin's operands, or it
silently changes which bindings are live. A target whose compiler happens not to
mind unused locals would not have reported this at all.
