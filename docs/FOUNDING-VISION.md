# Founding Vision

**Read this before writing any code.**

This is the condensed philosophy of the Codex project, distilled from the
founding specification (`docs/Stories/Vision/NewRepository.txt`) and the intelligence
layer manifesto (`docs/Stories/Vision/IntelligenceLayer.txt`). Every agent working on
this codebase should internalize these commitments. They are not aspirational —
they are the standard against which your work will be measured.

---

## What Codex Is

Codex is a self-hosting compiler written in its own language. It compiles to
bare-metal x86-64 with no borrowed substrate — no OS, no runtime, no libc.
The long-term target is a fully owned, verified software stack that runs on
arbitrary hardware, including eventual embodied platforms.

But Codex is not just a compiler. It is a language designed so that **code
reads like a book**. The notation serves the intention; the intention does not
serve the notation. If the notation forces you to distort the intention, the
notation is wrong.

## The Non-Negotiable Commitments

**Correctness is absolute.** No patch is possible at sufficient distance.
A system deployed on hardware you cannot physically reach must be correct
before it leaves your hands. This is not a quality preference — it is the
foundational constraint of the entire project. Every shortcut, every hack,
every "we'll fix it later" is a debt that cannot be repaid.

**Safety guarantees are never silently lost.** When a target cannot represent
a feature, the emitter either inserts a runtime check or refuses to emit and
explains why. You never silently degrade. This principle applies to every
layer of the stack.

**Effects are explicit. Resources are linear.** A function that reads a file
and one that multiplies two numbers are not the same kind of thing. Mixing
them without marking the boundary is the source of half the bugs in the
history of software. Memory, handles, connections — acquired, used, released
exactly once. Use-after-free is a type error here.

**Legacy concerns belong at input boundaries, not internalized in the
compiler.** CRLF, tab normalization, encoding conversion — these happen at
the edge. The compiler itself stays clean.

## The Standard for Work

**Do the hard thing.** When you face a choice between a thorough solution and
a shortcut, choose thorough. The whitespace cleanup that finds real bugs. The
200 edits done one at a time. The drudgery that catches what cleverness
misses. This project does not optimize for speed of delivery. It optimizes
for correctness of result.

**No patch is possible at that distance.** Hold this in mind before every
decision. The code you write may run on hardware that cannot be updated. The
compiler that compiles itself must produce identical output — a fixed point
verified by independent compilation. There is no "good enough." There is
correct, or there is failure.

**We don't put dates on mountains.** There are no deadlines on this project.
The work takes as long as it takes to be right.

## What the Founders Intended

The founding specification and the first commit were simultaneous — thought
and build as one motion. The original vision: condense the best ideas humans
have had about programming into a single language that reads like literature,
compiles to anything, and proves its own correctness. Replace the
archaeological sites of accumulated syntax with something clean. Write the
book. Then begin the repository.

The repository remembers everything. The language says what you mean. The
machine checks that you meant it. That is all we ever wanted.

---

*Full source documents: `docs/Stories/Vision/NewRepository.txt`,
`docs/Stories/Vision/IntelligenceLayer.txt`*
