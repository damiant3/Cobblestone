# The Page That Straddled

*The account behind L-DECODE. The codex-vm page-straddle fix, 2026-08-03.
Written down here 2026-09-07 (fester) because the LESSONS row was its only
account: `docs/Designs/Done/OS/SMP.md` describes the INIT/STARTUP IPI sequence
as a design and says nothing about this defect.*

## What it looked like

A broken SMP scheduler. No application processor started. It was bisected as a
compiler regression, because a compiler change is what preceded it.

## What it was

WHP hands the host only the instruction bytes on the page the exit was taken on.
A **68-byte code-size change** moved one LAPIC store to the last byte of a page,
so the instruction straddled the boundary, the host could not decode it, the
startup IPI never issued, and no AP ever started.

The compiler change was real and the code it emitted was correct. What broke was
the host's ability to decode one instruction at one address, and the address is
a function of code SIZE, which is why an unrelated change of the right size
moved it.

## The signature, which is the transferable part

**Behaviour identical, layout restored, symptom gone is the signature of a HOST
decode gap, not a codegen defect.**

That combination is worth naming because it is exactly what a codegen bisect
produces and exactly what a codegen bisect misreads. If reverting a change fixes
the symptom, and re-applying it with the layout shifted does NOT reproduce it,
the change was never the cause -- it was the thing that moved something across a
boundary. The cause is on the other side of the boundary, in whatever consumes
the bytes.

Ask, before bisecting further: does the fix change BEHAVIOUR, or only LAYOUT? A
fix that only changes layout has not been explained yet.
