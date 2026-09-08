# Two Arms Went Red For A Fix

*The account behind L-INSTRUMENT. Written 2026-09-07 (fester) from red's CL
12901 of 2026-08-04, because the LESSONS row was the lesson's only account and
a row cannot carry what a lesson cost. Everything below is from that CL's own
description.*

## What was actually broken

`acpi-boot-rsdp` read the pointer the Option A stub published and had no other
source. A payload started without a stub -- a plain CDX on a bare `-gop` guest
is exactly that -- read zero and reported a machine with no ACPI, while
codex-vm's tables sat at `0xE0000` unread. That cost the Monitor pane its whole
acpi/lapic/ioapic/power block, and it is why `desk-shutdown`'s ACPI poweroff
could not work on a stubless boot.

The fix was the memory search ACPI 6.5 section 5.2.5.1 describes: the first
kilobyte of the EBDA (the segment word at `0x40E` shifted left four, and only
when it lands in `0x80000..0xA0000`), then `0xE0000..0xFFFFF`, on 16-byte
boundaries, validating each candidate rather than matching the signature. The
stub path was unchanged and still won (L-FALLBACK); the search was reached only
when the stub published nothing, or published something that was not an RSDP.

## The part this story is about

`gop-handoff` had three gate arms that read `acpi-boot-rsdp` in order to observe
WHICH SOURCE won. After the fallback landed, that function no longer answers
that question: it returns the same address whichever source was read.

**Two arms went red for a change that fixed something.** They were predicted to,
and they did.

The arms were not wrong to exist and the fix was not wrong to land. The arms
were reading a function whose job had legitimately widened, and the question
they were asking -- which cell did the value come from -- had stopped being a
question that function answers. Softening the assertion would have kept them
green and measuring nothing.

## The repair, and why it is a split rather than a loosened assertion

Source selection was split out as `acpi-stub-rsdp`, so the two questions stay
separable: *which cell*, and *is it real*. The arms were pointed at
`acpi-stub-rsdp`, which is the selection and nothing else. A fourth arm covers
the new path -- stub value not an RSDP (False), and `acpi-boot-rsdp` answering
917504 from the search. Removing the fallback moves that row to 0 and nothing
else, which is what makes the arm an arm rather than a restatement.

## The lesson (L-INSTRUMENT)

**A test that reads a function to observe A is broken by that function correctly
learning to do B.** Before changing a function, grep its callers for TESTS as
well as for code: a test is a caller whose question may be narrower than the
function's job. When such an arm goes red, the repair is to split the function
and point the arm at the part that still answers its question -- never to soften
the assertion, because an assertion loosened until it passes is an instrument
that cannot fail (L-FALSIF).

## Two things worth keeping from the same CL

**The sabotage table was predicted before it was run** (L-SABOTAGE). Four scan
arms against three sabotages -- step 16 to 1, and validate reduced to a
signature match:

| arm | subject | step 16->1 | validate -> sig only |
|---|---|---|---|
| aligned | +256 | +256 | +256 |
| unaligned | none | +264 | none |
| corrupt | none | none | +256 |
| empty | none | none | none |

Each sabotage moved exactly one row and a different one, so the arms measure the
step and the validation separately rather than agreeing with each other.

**Scanning memory means offering arbitrary bytes to a length field.**
`acpi-rsdp-ok` bounds the declared length before using it as a checksum loop
count: four bytes reading as four billion would walk the address space. The
bound has to be a property of the walk rather than a number the scanned bytes
declare, which is the same reasoning as the length bound removed from the old
code's stub-pointer path. No lesson id carries this yet.
