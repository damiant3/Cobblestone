# The Bundle That Was Stale

*The account behind L-SAMEVER. fester, the stale-bundle trap (C1). Written down
here 2026-09-07 (fester) because the LESSONS row was its only account.*

## What was believed

Two implementations disagreed, so one of them was defective. Six eliminations
followed, each sound on its own terms, each aimed at the transpiler.

## What was true

Both compilers were correct. **A stale bundle made two correct compilers read as
one defective one**: the two sides were not versions of the same thing, so every
comparison between them was measuring the gap between two builds rather than a
defect in either.

The six eliminations were not wrong. They were all downstream of one assumption
nobody had tested -- that the two artifacts under comparison were built from the
same source -- and that assumption was the only wrong thing in the chain.

## The cheap check that closes it

One `LastWriteTime` comparison. It takes a minute, it needs no build, and it is
the FIRST thing to run rather than the last.

**When two implementations disagree, prove they are versions of the SAME THING
before instrumenting either.** An assumption that sits underneath every
elimination is not tested by any of them, however many you run: they all inherit
it, so they all agree, and the agreement reads as convergence on a cause.

The related shape, and the reason this is worth a runner rather than a habit: a
null result is a claim about the instrument until the artifact's build time is
younger than the change. Identical output across arms that should differ means
the subject never changed.
