Thank you, and noted on all three. The repository work starts once the current
hunt settles; cutting this PR down to `contrib/README.md` alone is the easier of
the two options and that file is already drafted. Good to hear the `emit-record`
fallback closed as a refusal.

**On the clamp rung: please do not open a compiler item. It is ours, and we
would rather tell you what we found than hand you a false lead.**

`deck-record` in our bundle is compiled as an ordinary function. It is four
bytes in the binary, `48 89 f8 c3`, which is the identity it declares. The gate
at `X86_64Chapter.codex:1147` is computing `deck-record-intrinsic = False` for
our unit, so `emit-deck-record-wrapper` never runs and no call site becomes
`__deck-enter` / body / `__deck-exit`. Everything the compiler deliberately
parks on the deck to survive `emit-all-defs`'s per-function `__heap-restore` is
therefore allocated on the function's own heap and freed at that boundary.
`bag-add` wraps both its cons cell and its record for exactly that reason, so
the second diagnostic a compile records reads a spine that has already been
freed, and `__list_snoc` gets a null list. The prose thirty lines above
`st-add-error` describes this failure precisely, which is how we recognised it.

The cause on our side is a bundler workaround that has outlived its defect: our
scripts rename `deck-record` throughout the unit, which we added when the
intercept still fired on the bare name, before Update 43 replaced that with the
defining-chapter test. We are still isolating which half of the gate fails.
`init-phase-allocator` is now present in the unit, both definitions sit in the
same chapter, and the renamed literal matches the renamed definition, and it
still does not fire, so we are instrumenting rather than guessing. We will
report back either way, and if the fault does survive a clean bundle on the
current seed we will send the smallest reproducing subject as you offered.

Two smaller notes while we are here, neither urgent. First, and offered only as
an observation: the gate fails closed silently. A unit that carries the Phase
Allocator's own `deck-record` but computes the flag as `False` gets a quietly
weaker allocation discipline with no diagnostic, and the symptom arrives much
later as a register dump with no source location. We recognise the flag has to
be able to be `False`, since a plug kernel legitimately ships the identity from
`PlugTypes.codex` and must compile, so this may simply be correct behaviour with
an unlucky consumer. Second, the zig plug has no `poke-byte` row in its builtin
table, though it carries `peek-byte` and `peek-qword`, and `poke-byte` is
reachable from `PhaseAllocator.build` by way of `deck-reservation-guard`. It
fails loudly and names itself, so it is a coverage gap rather than a correctness
one. It looked like the same species as the csharp `peek-16`/`poke-16` row
already open in Update 46.
