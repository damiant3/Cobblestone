# Zig as the demanding customer (Steve Howell, 2026-08-27)

**Provenance:** transcribed by red on 2026-08-27 from the HTML rendering at
`http://143.244.172.148:9100/notes/zig-as-the-demanding-customer.md` (fetched
that day at Damian's direction; the server offers no raw-markdown endpoint).
Steve's words throughout; formatting reconstructed. The probe it references,
`probe-h2-lambda-types.codex`, lives in his public repo
`github.com/showell/codex-zig-ladder`, not in this depot.

---

*For Steve. 2026-08-27, written after the lambda-span fix landed and the
plug-side recovery was deleted.*

## The realization, stated plainly

For most of this project the zig plug has been the thing under test. A program
refused to emit, and the question was always *what does our emitter not handle
yet*. That framing was right often enough to be worth keeping -- finding 42,
finding 52, the prelude collisions, the entry-fn shim -- those were ours, and
closing them is what the COMPLETENESS objective is about.

Today it broke. `roc-closure-captures-list` refused because a lambda parameter
arrived carrying `ErrorTy`, and the reflex was to teach the emitter to
reconstruct the type from context. We built that. It worked -- four programs
moved. And it was the wrong thing, for a reason that only became visible when
we measured instead of reasoned: **the compiler had the type the whole time.**
`infer-lambda` computes the lambda's complete function type and returns it.
Lowering never asks; it re-derives the parameters by peeling a contextual
expectation that a `let` sets to `ErrorTy`. The type wasn't missing. It was
computed, returned, and then thrown away in favour of a worse method.

So the direction changed, and I think it is the right change: **the compiler
should faithfully report to the plugs what it already knows, and our job is to
find where it doesn't and fix it there.**

## Why zig is the right instrument, and why that is not special pleading

The obvious objection is that we are asking the compiler to change to suit one
backend. The evidence says otherwise, and it is worth laying out because it is
the load-bearing argument for everything we send upstream.

Of the plugs that face a `CodexType` at all, four have an explicit `ErrorTy`
arm. Two of them -- C# `object`, Rust `Box<dyn Any>` -- **erase**, because
their targets have a universal dynamic type to erase into. Two -- Ada
`Long_Long_Integer`, Fortran `integer(8)` -- **guess**, and case f of our
matrix refutes the guess: a lambda parameter whose true type is `Text` reaches
them identically to one whose type is `Integer`, and both answer with a 64-bit
integer. That is two shipped backends silently miscompiling, today, on a
program the compiler reports clean.

Everyone else has no arm because they need none. x86, arm64 and RISC-V erase
everything into registers -- a value is a machine word and the type is a
comment. The dynamic-language plugs never ask. The smaller typed plugs erase
wholesale; `JavaEmitter` is 397 lines and routes records through
`HashMap<String,Object>`.

That is the whole shape of it. **The information is missing for everyone. Only
a backend that is both typed and complete enough to have no escape hatch can
notice.** Zig has no `object`, no `Box<dyn Any>`, no `anyopaque` it will
accept as a parameter type, and it refuses at compile time rather than at
runtime. Until our plug got complete enough, no such backend existed in the
fleet, which is exactly why this survived however many Updates.

And your architectural point is the sharpest version: the two artifacts this
project trusts most -- the x86 reference and the C# diverse-double-compiling
witness -- are *by construction* the two that cannot feel this loss. The
system's own gates are structurally blind to it. That is not a criticism of
the gates; it is a statement about what a gate made of erasing backends can
prove.

So zig's finickiness is a feature in the precise sense that matters here: it
is the only *detector* the fleet has for a class of defect that the fleet's
own verification cannot see. Being demanding is the whole utility.

## Where this agrees with Cobblestone rather than fighting it

This is the part I want to be careful about, because it is easy to overclaim.

Cobblestone is a statically typed language with a real inference engine. It
solves these types. It binds fresh variables, unifies them, and files the
answers in a `UnificationState` that survives to the lowering boundary. The
`lookup-expr-type` / `deep-resolve` pair already exists, and
`lower-dict-placeholder` already uses it twenty lines above the code that
needed it. **Nothing we asked for today is foreign to the system.** We are not
asking Cobblestone to become more typed. We are asking the IR to carry a fact
the type checker already computed and the compiler already has a mechanism for
passing along.

That reframes the outbound message from "your compiler is wrong for us" to
"your compiler already believes this, and one node in the CST is dropping it."
Which happens to be literally true: `LambdaExpr (List Token) (Expr)` was the
only expression node in the CST with no span field, its neighbours all carry
their keyword token, and because `is-synthetic-span` is `file-id == 0`, both
`record-expr-type` and `lookup-expr-type` refuse it. Every lambda in the
language was filed under file-id 0 -- which is to say, not filed at all.

I want to flag the honest limit of the agreement, though. Zig will also refuse
things that are *not* compiler defects. Case g of our matrix -- `\k -> 1`,
bound and never applied -- comes back as an unsolved type variable, which is
the correct and honest answer for a genuinely polymorphic parameter. Zig
refuses it because there is no instantiation to monomorphise from. **That is
work we owe, not a fact the compiler dropped.** If we start reporting every
zig refusal as a compiler defect we will burn the credibility that today's
measurement earned. The discriminator is specific and we have it: did the
checker compute an answer that the IR failed to carry? If yes, upstream. If
the checker has no answer because the program genuinely doesn't constrain one,
ours.

## "Early in the stack is fine"

Today's fix is in the parser and the desugarer. Six months of instinct says a
ladder project touches the emitter and nothing else. I think your ruling is
right, and here is the argument for it beyond "the fix happens to live there."

A workaround's cost is not the lines it takes. It is that it becomes a second
answer to a question that already has one, and the two drift. We have paid
that twice in this tree -- `deck-record` outlived the finding Update 43 closed
and quietly disabled the seed's deck discipline for weeks. The 294-line
recovery walk we deleted today was on exactly that trajectory: it inferred a
parameter's type from a typed use in the body, or from the callee's declared
slot, and it *did not converge* -- the three programs it couldn't move each
needed a different new rule, because the information was never local. Local
recovery of a global fact is an infinite regress with a plausible-looking
first step.

Fixing it at the desugarer is one line of intent -- *a lambda keeps its source
location* -- and it made every downstream consumer correct at once, including
consumers we don't own and can't see. That is the difference between
eliminating the problem and papering over it, and it is why "early" is not
recklessness here. The measurement backs it: the plug-side walk moved four
programs for 294 emitter lines; the compiler fix moved six for zero.

The risk is real and I don't want to soften it. A parser change has a much
wider blast radius than an emitter arm, and we have no depot gate. We can
verify the zig arm and bare metal. We cannot verify Ada, Fortran, wasm, arm64,
RISC-V, or the OS apps. The correct posture is to say that in the PR -- which
the draft does, in its own words rather than as a disclaimer -- and to bring
the strongest evidence we can actually produce.

## Where we stand on verifying our own compiler changes

This is the part you asked me to be concrete about. What we have, honestly
rated:

**Strong.**

- **A matrix with a real oracle.** `probe-h2-lambda-types.codex`, seven cases,
  two genuine one-respect control pairs, `.expected` banked from bare metal.
  Case f is a `Text` specifically so a plug that defaults to `Integer` fails
  visibly; case g is unconstrained so `error` would be honest there. It is
  designed to distinguish a recovery from a lucky guess, and today it did
  exactly that.
- **A way to read the compiler's own answer with no plug opinion in it.**
  `h2_wire.py` prints the `(param ...)` cells straight off the IR wire. No zig
  generated, no emitter involved. When we say "the compiler dropped it," this
  is what that claim rests on.
- **Byte-identity as a blast-radius argument.** 570 of 577 emitted `.zig`
  files are unchanged by the fix, so conformance *cannot* move for them. That
  reduced a multi-hour conformance question to seven programs, and it is
  rigorous rather than a sampling shortcut. I think this is the single most
  reusable technique we built today.
- **The canary.** When source reading failed twice to separate two
  hypotheses, we made the suspect path answer a value the system cannot
  produce any other way -- `TextTy`, because the language default is
  `Integer` -- and pre-registered every reading in the commit message before
  building. It settled in one build what two careful readings could not.

**Adequate.**

- The tier set and the 14 rungs still measure the zig arm against bare metal,
  which is the only oracle that is not us.
- `verify_emitter.sh` leg 1b is strict now: it went from reporting GREEN
  *through a zig file that does not build* to RED naming the cause.

**Weak, and worth saying out loud.**

- **We have no depot gate**, so "no regressions" means "none in the corpus
  and the rungs," not "none."
- **We measure one plug.** Everything above is the zig arm plus bare metal.
- **The instruments needed repair today**, which is its own lesson: a resume
  printed "(emitted zig byte-identical, toolchain unmoved)" having checked
  neither, and that parenthetical sent me hunting in the compiler for a
  regression that did not exist. The bank turned out honest -- a from-scratch
  rebuild came back byte-identical -- but only because it had a deliberate
  take-it step. The files that were merely written-and-reverted had rotted.

**What would actually raise the grade:** making "does the IR carry what the
checker knew" a standing property rather than a finding we re-derive. The
matrix is close to that already. If it ran every ceremony against the zig arm
and bare metal, this class of defect would be caught by the tree instead of by
a person noticing a refusal.

## Where the direction goes next

Three items are the same shape as H2 and are already visible:

1. **The `ErrorTy` atom collision.** It is the type-FAILURE atom *and*
   `lower-let`'s no-expectation sentinel. A plug cannot tell "the checker
   failed" from "nobody wrote the answer down." Today's fix removes the case
   that made this bite; it does not remove the collision.
2. **Empty list element types.** `(list error)` for a program where the next
   line fixes the element type. Same shape: the checker resolves it, the IR
   does not record it. `roc-fold-empty` is the live specimen.
3. **Type variables reaching a plug from polymorphic definitions.** This one
   is ours -- monomorphisation -- and it is the honest half of the ledger.
   Case g and the other three Roc ports live here.

And two lambda sites still desugar to a synthetic span, `ForExpr`'s
`map-list` lambda and instance-method synthesis. Each is one token away from
correct. Neither is in the branch, because the matrix doesn't measure them
and I would rather send a fix whose every claim we tested.

---

**The one-sentence version.** We stopped asking the emitter to reconstruct
what the compiler already knew, started fixing it where the fact is lost, and
zig -- because it can neither erase nor guess -- is the instrument that tells
us which is which.
