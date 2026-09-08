# The Sabotage That Changed Nothing

*The account behind L-SABOTAGE. Two instances: val, 2026-07-31 (`r3t-stride`),
and blu, 2026-08-25 (the python plug's `char-encode` arm). Written down here
2026-09-07 (fester) because the LESSONS row was their only account -- val's was
in a findings outbox, and outboxes were retired 2026-08-08.*

## First instance: a sabotage that moved FEWER rows than predicted

Setting `r3t-stride` to the visible width moved **two of three** rows, where all
three were predicted.

The two that moved were not the interesting part. The one that did not moved
nothing because `r3d-target-at` passes stride as an ARGUMENT and never reads the
field back, so the sabotaged field was not on the path being aimed at at all.

**A sabotage that moves fewer rows than predicted is telling you the code is
shaped differently than you wrote down, and it is worth more than one that moves
all of them.** A sabotage that moves everything confirms what you already
believed. A sabotage that moves less tells you where your model of the code and
the code disagree, which is the only thing you did not already have.

## Second instance, and it is the one that returns a CLEAN PASS

Verifying a `char-encode` arm in the python plug, the tier-1 LEAD mask was
sabotaged from **192 to 193**, and the oracle reported **PASS**.

That nearly shipped as "the harness cannot see this builtin". It sees it fine.
For the test input `c = 592` the shifted operand is **7**, and bit 0 was ALREADY
SET: `192 | 7` and `193 | 7` are both **199**. The sabotage was real, the arm was
real, and the two never met, because the corrupted bit could not be expressed by
the inputs the test actually uses.

Corrupting the CONTINUATION mask **128 to 129** produced the real failure at the
predicted value, **144 against 145**.

**CHOOSING A BIT TO CORRUPT IS NOT CHOOSING A BIT THE TEST INPUTS CAN EXPRESS.**
Before believing a control, compute what the sabotage does to the actual test
INPUTS rather than to the code. If the arithmetic says the output is unchanged,
the control proved nothing -- and the PASS underneath it means nothing either.

## The shared shape

Both halves are about reading a sabotage's result as a statement about the
SUBJECT when it is a statement about the PATH. Fewer rows moved than predicted
means the path is not what you drew; no rows moved means the inputs cannot reach
the branch (L-CONSTRUCT and L-VACUOUS are the same observation from other
directions). Neither is a passing control, and both are cheap to tell apart:
one arithmetic check on the actual inputs, before the run.
