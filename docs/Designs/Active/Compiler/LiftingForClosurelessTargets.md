# Lifting for the closureless targets

*Proposal, reek 2026-09-07. PARKED (root 2026-09-25): not built until a toolchain
for one of the four targets is on this box, because its acceptance is running
the emitted program (plugs-backlog 1.59). Read, not run: every claim below about
a plug's behaviour comes from its emitter source, and the two claims about
emitted output come from `codex/plugs/test-output`.*

## What this is for

`ada`, `cobol`, `pascal` and `babbage` cannot express a value that is a
function, so they are the four plugs that plugs 1.59 could not repair. The
other 41 subject plugs now apply an over-application one argument at a time
and eta-wrap a partial application; these four cannot do either, because both
repairs produce a function value and none of these targets has one.

**What these four cannot express is a function VALUE arising from partial or
over-application**, which is what plugs 1.59 measures and what the closure
conversion below is for. No `IrLambda` node reaches any plug anywhere in the
17-subject corpus: `lambda.codex` is the only subject containing a lambda
literal, it contains two, and neither survives to the emitter.
`make-adder (n) = \x -> x + n` arrives with the lambda merged into the
definition's parameters, so every plug emits a two-parameter function (`def
make_adder(n, x)`, `function Make_adder(N, X)`); `(\x -> x + 100) 5` arrives
already lifted, so every plug emits `__lam_0(5)` and a definition of
`__lam_0` beside it.

## The interim guard, which is in place

All five closureless plugs emit the same `!UNSUPPORTED:` marker for an
`IrLambda`, and `test-plugs.ps1` fails any emission containing one, on every
subject. The reader is proven both ways: 6 markers in babbage's `overapply`
emission, 0 in python's, javascript's and kotlin's, so babbage is caught on
emission as well as on its exit code.

**The emitter half of that guard sits on a path nothing in the corpus reaches
(L-UNCALLED), so it has never fired and cannot be demonstrated with the
inputs we have.** No subject can reach it: the wire lifts every lambda,
a capturing lambda passed as an argument and one returned from a function
included (the probe under the next heading), so the marker is unreachable from
source rather than untested.

## What the wire requires

The IR a plug receives IS lambda-lifted (`prepare-method-ir`, `opening.codex`;
the wire contract in `DevelopersRulebook.md`). A lambda arrives as a top-level
`__lam_N` def taking its captured locals as leading parameters, and its value
as that def partially applied to them: `pick (n) = \y -> y + n` is
`(apply (name "__lam_1") (name "n"))` on the text-plug wire (val,
2026-09-25). So a function VALUE reaches these four targets in exactly one
form, a partial application of a named def, and the shape below needs no
capture analysis: the captured values are the supplied arguments.

## The reference implementation is zig, not a new invention

`zig-lambda-closure` (`ZigEmitter.codex:1893`) is closure conversion and is
the shape to copy: compute the captured names from the body against the
parameters, emit a struct holding the captures with a `call(ctx, params)`
function, and make the value `{ .ctx = <boxed captures>, .call = &Env.call }`.
Everything below is that, minus the function pointer.

## The proposed shape

A target with records and a `case` statement but no function pointers
represents a closure as a TAGGED RECORD and applies it through one dispatcher:

- Every lambda body, and every partial application of a named definition, is
  lifted to a top-level procedure taking the closure record and one argument.
- The closure record carries a `tag` naming which lifted body it is, plus one
  field per captured value.
- One generated `apply(clo, arg)` per unit dispatches on `tag`.
- A definition that returns a function returns a closure record.
- `choose 0 2 3` emits `apply(apply(choose(0), 2), 3)`.
- `add3 1` emits a closure record tagged for a lifted body that captures `1`
  and, applied twice more, calls `add3`.

This is uniform across all four; only the record and dispatch syntax differ.

## Per target

**ada** has records, discriminated variants and recursion, so the shape lands
directly. Start here: it is the only one of the four where nothing else is in
the way, and it is the target whose current output the 1.59 row already
documents in detail.

**pascal** has records and `case`. Its emitter already hoists statements
(`PasHoist`) because Pascal declares temporaries up front, so lifted
procedures have somewhere to go. `DevelopersRulebook.md:278` records that
pascal does not lift and is not a pattern to copy.

**cobol** is the hard one and should not be sized from the other three.
There are no pointers, no recursion in the classic dialect, and no dynamic
allocation, so the closure record becomes an entry in a WORKING-STORAGE
table, the tag an index, and `apply` an `EVALUATE` plus `PERFORM`. Whether
recursive closures are reachable at all decides whether this is worth doing;
that question should be answered before any code.

**babbage** may be out of reach and the honest answer may be that it stays
refusing. The Analytical Engine has no indirect call and no addressable
store in the sense the shape needs. Do not spend the ada work's budget here.

## The grader, and the part that cannot be closed here

The shape grader from 1.59 cannot settle this work. It matches a flat
over-application positively and treats everything else as correct, precisely
because enumerating correct spellings failed twice (L-INSTRUMENT); a
dispatcher-based emission is a spelling it has never seen and would pass
whether or not it works.

**The acceptance for lifting is running the emitted program**, because the
subject has a known answer: `overapply.codex` prints 6, 6, 6, 7, 15. Nothing
weaker distinguishes a correct dispatcher from a plausible one.

No toolchain for any of the four is on this box. So the first question for
whoever takes this is not a code question: **which of GNAT, Free Pascal or
GnuCOBOL can we put on the box**, and that choice should decide which target
goes first, overriding the ada-first ordering above if ada is the one we
cannot run. Building all four against a grader that cannot execute them would
repeat the 1.59 failure at four times the size.

## Not proposed

Changing the wire to deliver lifted IR. That would move work into the
compiler for every target including the ones that do not need it, and the
Rulebook's contract is deliberate.
