SHELVED 2026-08-21 by Damian: vanity work. Nothing here is drawable, and
nothing here belongs in `codex/plugs/plugs-backlog.md`, which is the
close-out lane's register of work that is actually meant to be done. Do not
fold these back into it. If the Analytical Engine backend is ever wanted for
something, this is where its open items are.

1.1 - babbage does not perform a call to a user function. `bab-emit-call-def`
emits `. call <name>` as a COMMENT card, emits the argument computation, and
then stores literal zero as the result (`N000` / `S<target>`), so `argpos 5`
computes its argument correctly and answers 0 rather than the 14 the x86-64
oracle gives. Found 2026-08-21 while closing the let-binding defect in the
same emitter (main 18744) and untouched by that fix. The Analytical Engine
has no call instruction, so this is a design question about how a call is to
be represented, not a missing line.

1.2 - `bab-emit-apply-root` answers every non-name root by emitting the chain
ROOT alone and discarding the arguments. Noticed while reading 1.1; not
measured against a subject, so it is a reading rather than a finding.

There is no Analytical Engine and no emulator for one on this box, so
everything here is read rather than run, and no oracle covers this plug.
