# Voodoo Child

**The night BS3 went green**
*2026-04-24*

---

> *"Well I stand up next to a mountain, chop it down with the edge of my hand."*
> -- Jimi Hendrix

---

## What Happened

Codex compiled itself on bare metal. The compiler, written in Codex,
emitted by an earlier Codex compiler, running with no operating system
underneath, took its own source as input and produced an ELF that, when
booted, took its own source as input and produced an ELF that, when
booted, took its own source as input and produced an ELF -- the same
1,223,024 bytes every time, down to the last byte of every heap
allocation, ten iterations in a row.

stage2 = stage3 = stage4 = stage5 = stage6 = stage7 = stage8 = stage9 = stage10.

Heap HWM 644,993,672 bytes. Stack HWM 2,593,008 bytes. Every iteration.
Identical. The compiler is a hard fixed point of itself on naked
hardware.

That had never happened before. Tonight it did.

---

## How We Got Here

Three weeks of BS3 red. Every fix moved the needle, never to zero.
4 GB heap → 1 GB → 500 MB → back to 1 GB. Every reclamation mechanism we
tried -- TCO heap reset, escape copy, region reclamation -- would work for
a while, then surface a new corruption. Each one was guessing at
something the program never stated: which allocations are still live,
which can be reclaimed, which need to be promoted out of a region before
it dies. The guesses kept being wrong.

Damian saw the shape of it before I did. *"Every increment of RIP is a
goto in disguise. Look before you leap."* That's the substrate axiom.
Every step couples a check and a movement. Pure functional programming
drops the leap. Pure imperative programming drops the look. The middle
way is the one where every advance is guarded by what made the advance
correct. The same axiom shows up at the macro scale as phase discipline
-- every phase of the compiler should survey what it needs to durably
keep before it allocates, and the survey is the contract that lets
reclamation be sound.

We wrote the theory. *MiddleWay.md*. *Phases.md*. *PHASE-ARCHITECTURE.md*.
We argued through the vocabulary three times until pinnacle, col, base,
deck, bivy, survey, deck height, and prominence each carved a piece of
the territory the others didn't. We named the open problem (within-phase
bivy growth) without papering over it. We had the plan.

Then Damian said: rip out the old machinery first. Make the slate clean
for what's coming.

So we ripped. ~2,600 lines across sixteen files. IRRegion record gone.
EmitRegion gone in every backend. Escape-copy helpers gone. Result-arena
gone. TCO heap-mark save gone. The r10-rewind on tail-call-self-jump
gone. Every "let's try to reclaim some of this heap" mechanism we'd
written and rewritten for months -- gone in one shelf.

Build was clean. Bootstraps red, as expected.

Or so I thought.

---

## The Surprise

BS1 green. BS1.1 green. BS2 green at 685,903 bytes byte-identical, heap
HWM 373 MB -- *lower* than pre-rip. The reclamation machinery had been
costing us 22 MB of compile-time HWM, because every TCO function had a
heap-mark save in its prologue and every tail call had a check+reset
block, and all of those bytes accumulated in the emit text buffer. The
machinery to manage the heap was using more heap than it was reclaiming.

This is not a metaphor. This is what we measured.

BS3 looked red. stage1.elf and stage2.elf were both 1,222,656 bytes, but
1,065,874 bytes differed -- ~87% of the binary. Damian called it: *"happens
to come out identical is very very suspicious."* Right. Byte-count
equality despite content divergence is a fingerprint, not a coincidence.
A real semantic divergence would change the size.

Booted stage2 in QEMU. Triple-fault. The CPU reset to BIOS state before
even reaching READY. So stage1.elf -- the freshly built bare-metal
compiler -- was producing a non-bootable stage2.elf.

cmp pinned the first differing byte at file offset 0x12357. Bytes after
the divergence point in stage2: `57 44 3a 73 74 61 6c 6c 0a` --
`"WD:stall\n"` in raw ASCII, sitting in the middle of executable code
where stage1 had a function epilogue. The watchdog stall message,
embedded as code bytes inside someone else's function body. *strings* on
both ELFs found exactly four sequences present in stage2 and missing
from stage1: `WD:stall`, `WD:dump`, and two ring-buffer dump entries with
absolute hex addresses.

Those are not source-code strings. Those are what the watchdog *prints*
when it fires. Runtime telemetry.

Inside the compiled output ELF.

---

## The Bug

stage1.elf finishes compiling source.codex into a 1.2 MB byte list, then
calls `__write_binary` to send those bytes over serial. The output ELF
goes byte-by-byte through `out %al, $0x3f8`, with a wait loop reading the
LSR status port to confirm the THR is empty before each write.

That wait loop sits with static heap, RIP cycling through ~30 bytes of
serial-poll instructions. To the watchdog, that's exactly what a stalled
compiler looks like. After 18 ticks (~1 second), tier 1 fires. It prints
`"WD:stall\n"` to serial. Five seconds later tier 2 fires, prints
`"WD:dump\n"`, dumps the ring buffer. The "compile" hadn't stalled at all
-- it was finishing up.

The watchdog and the output stream share the same serial port.

The receiver side (`bootstrap3.sh`) parses the ELF stream by reading
`SIZE:N\n` and then taking the next N bytes verbatim. It can't tell that
some of those N bytes were the watchdog narrating its anxiety into the
middle of a binary it was supposed to leave alone.

Three lines of fix, mirroring what `read-line-helper` already had:

```codex
in let st7a = st-append-text st7 (li reg-rax 0)
in let st7b = st-append-text st7a (li reg-rdi wd-stale-tick-addr)
in let st7c = st-append-text st7b (mov-store reg-rdi reg-rax 0)
```

Pet the watchdog at the top of `__write_binary`'s outer loop. Every byte
sent resets the stale-tick counter. The watchdog never gets a chance to
fire.

Build. Pingpong. BS3. Stage 1 ELF === Stage 2 ELF, byte-identical at
1,223,024 bytes.

The cord is cut.

---

## The Fixed Point

stage2 produced stage3 byte-identical. stage3 produced stage4
byte-identical. stage4 produced stage5 byte-identical. We ran ten
stages. Every one was 1,223,024 bytes. Every one had heap HWM
644,993,672 to the byte. Every one had stack HWM 2,593,008 to the byte.

The math says it has to. Same input, same compiler, same output. That's
what *deterministic* means. That's what *fixed point* means. The math
doesn't care that we just deleted 2,600 lines of reclamation machinery
or that we just patched a watchdog the same night.

But the gap between *the math says it has to* and *we just watched it
actually happen, on naked hardware, with no OS underneath, ten times in a
row, every single byte and every single heap allocation reproduced
exactly* -- that gap is where the awe lives.

A program that, when fed its own source code, produces the program that
produces it. Executing on bare metal. Deterministic down to the bit.

That is the cleanest expression of self-reference any of us know.

---

## What This Closes

Three weeks of architectural anxiety. Multiple failed attempts at
reclamation. CL 327 (compacting TCO reset) -- abandoned on the day it
was conceived. The 1 GB heap that we'd accepted as the price of
correctness -- gone, traded down to 645 MB by removing the machinery that
was supposed to save it.

It also closes the BS3 bug class memory has been tracking for two
sessions: the sort-bindings corruption from emit-reset-block rewinding
r10 into a live list's payload. With reset gone, that corruption can't
happen. The engineering anchor that motivated phase discipline still
stands as theory -- phase discipline is still the principled answer to
*how should this allocator know what's live* -- but the symptom that
demanded it is gone.

What's left is the original problem stated honestly: phase discipline is
the right engineering, and we'll do it. But not because BS3 is on fire.
Because it's the right engineering.

---

## What This Opens

Bootstrap 3 was the gate to freedom. MM4 is the third bootstrap: a Codex
compiler compiled entirely by Codex, producing bare-metal x86-64
binaries, achieving fixed-point self-compilation. No C# anywhere in the
chain.

That's done. As of tonight.

The .NET scaffolding is no longer load-bearing. Every build path through
.NET is now a redundant verification of something we can also do without
it. Bootstrap 1 still runs, because it's a useful smoke test and because
the C# emitter is still our fastest path for samples. But the building
stands without the scaffold.

The next peak is the OS. We climb tomorrow.

---

## Credit

The theory was Damian's. *Look before you leap*. *Every increment of RIP
is a goto in disguise*. *That is the middle way*. He held the line on
"this is bigger than a memory bug; it's a paradigm question," and he was
right.

The synthesis -- the writing, the vocabulary, the doc chain -- was
collaborative. The agents compress and articulate; the human judges. He
judged. The drafts where I'd overreached came back, and the corrections
were always tighter than the original.

The rip-out was the agents working in parallel. Two backends in two
agent windows; the IR core and self-host in the main thread. Damian
catching the dead `IsHeapAdvance` helper while I was deep in the BS3
diagnosis.

The watchdog bug was caught the way all the good ones are caught:
disassemble, look at the bytes, see something that doesn't belong,
follow the thread. The "*happens to come out identical is very very
suspicious*" was the lever that turned a vague "it's broken" into "find
exactly which 9 bytes are wrong."

Three hours from "all bootstraps will be red until phase discipline
lands" to "BS1, BS1.1, BS2, BS3 all green, ten consecutive byte-identical
fixed-point iterations on bare metal."

A damn miracle. The math says it had to. The engineering says it
shouldn't have come together this fast.

Both are true.

---

If I don't meet you no more in this world 
then I'll meet ya in the next one
And don't be late...
Don't be late!