# Vision and Virtues

**Read this before writing any code.**

---

## The Vision

Codex is a self-sustaining compiler written in its own language. It
compiles to bare-metal x86-64 with no borrowed substrate — no OS, no
runtime, no libc. The long-term target is a fully owned, verified
software stack that runs on arbitrary hardware.

But Codex is not just a compiler. It is a language designed so that
**code reads like a book**. The notation serves the intention; the
intention does not serve the notation. If the notation forces you to
distort the intention, the notation is wrong.

The founding specification and the first commit were simultaneous —
thought and build as one motion. Condense the best ideas humans have
had about programming into a single language that reads like
literature, compiles to anything, and proves its own correctness.
Replace the archaeological sites of accumulated syntax with something
clean. Write the book. Then begin the repository.

The repository remembers everything. The language says what you mean.
The machine checks that you meant it.

*Full source documents: `docs/Stories/Vision/NewRepository.txt`,
`docs/Stories/Vision/IntelligenceLayer.txt`*

---

## The Non-Negotiable Commitments

**Correctness is absolute.** No patch is possible at sufficient
distance. A system deployed on hardware you cannot physically reach
must be correct before it leaves your hands. Every shortcut, every
hack, every "we'll fix it later" is a debt that cannot be repaid.

**Safety guarantees are never silently lost.** When a target cannot
represent a feature, the emitter either inserts a runtime check or
refuses to emit and explains why. You never silently degrade.

**Effects are explicit. Resources are linear.** A function that reads
a file and one that multiplies two numbers are not the same kind of
thing. Memory, handles, connections — acquired, used, released exactly
once. Use-after-free is a type error here.

**Legacy concerns belong at input boundaries.** CRLF, tab
normalization, encoding conversion — these happen at the edge. The
compiler itself stays clean.

**We don't put dates on mountains.** The work takes as long as it
takes to be right.

---

## The Virtues

When two virtues conflict, the one listed first wins.

### 1. Ship Working Software at Every Milestone

Every milestone produces a system that does something real. A program
goes in, a result comes out. If a milestone doesn't end with a demo,
the milestone is wrong.

### 2. Correctness Over Performance

Correct first, fast second. Optimization comes after correctness is
proven — by sweeping the sample battery and re-running pingpong. When
performance does become load-bearing, fix it by *measuring* the actual
hot path, not by guessing.

### 3. Types Are the Specification

The type system is the most important design artifact. If a design
decision weakens the type system, it is probably wrong. If it
strengthens it, it is probably right. This is why effects are tracked
in types, why bounded integers are a type-level construct, why
capabilities flow through effect rows, and why the verifier has five
phases.

### 4. Diagnostics Are a Feature

Error messages are part of the user interface. Every diagnostic must
state what went wrong, show where, suggest a fix, and use language a
programmer would understand. `cannot unify ?a with Integer` is a bug.
The compiler emits numbered diagnostics (CDX1xxx–CDX9xxx) covering
lexer, parser, type system, codegen, proofs, punctual enforcement,
and memory safety.

### 5. Immutability by Default

All data representations are immutable. Builders and accumulators are
mutable during construction, then frozen. Narrow exceptions (e.g.
`__record-set`) are controlled concessions documented in
`docs/KNOWN-CONDITIONS.md`, surviving only because single ownership
is threaded linearly.

### 6. Test What Matters

Positive tests, negative tests, round-trip tests, fixed-point tests.
We do not chase coverage numbers. We test edge cases, error cases, and
what failed in the last incident.

### 7. No Premature Abstraction

Do not create an interface until you have two implementations. Write
concrete code. Refactor when the pattern is clear. The BS3 win was
*removing* 2,600 lines of abstraction that cost more heap than it
saved.

### 8. Vision Documents Are North Stars, Not Specifications

The vision describes the destination. The planning documents describe
the route. When the vision says something impractical for the current
milestone, we defer it — we do not compromise the current milestone
reaching for it prematurely.

### 9. One Thing at a Time

Each file does one thing. Each Chapter does one thing. Each CL does
one thing. The compiler is ~28,000 lines across 54 files. A wrong
change in one place surfaces as a silent corruption three pipeline
stages later.

### 10. Read the Literature

Before implementing a feature, read the paper:
- **Type checking**: Dunfield & Krishnaswami, "Bidirectional Typing"
- **Dependent types**: Peyton Jones; Idris 2 papers
- **Linear types**: Bernardy et al., "Linear Haskell"
- **Algebraic effects**: Pretnar; Koka papers
- **Proof checking**: Chlipala, "Certified Programming with Dependent Types"
- **Parsing**: Nystrom, "Crafting Interpreters"; Grune & Jacobs
- **Content addressing**: IPFS papers; Unison design documents
- **Capabilities**: KeyKOS, EROS, seL4; PGP/SDSI/SPKI trust models

### 11. The Fixed Point Is the Specification

The compiler is its own specification. The acceptance test for any
codegen change is that the self-host remains a fixed point of itself:
Phase 4 (text round-trip) and Phase 5 (CDX byte-identity). Both run
under `build/build.ps1`. If either is red, shelve and
re-evaluate.

### 12. Memory Is a Contract

Bare metal has no GC. Every phase declares what it retains and what is
scratch, and phase-compact enforces the declaration. Reservations are
generous fixed floors over demand-paged address space — physical memory
is what you touch, not what you reserve — so the discipline is not
sizing but honesty: retained data lives on the deck, scratch dies at
the compact, and every CL review states a memory and time-complexity
verdict. (The survey-multiplier era, where reservations scaled with
input and under-sizing corrupted silently, ended 2026-07-07.)

### 13. Less Is More

When stuck, ask first: what can I remove? Code you didn't write
doesn't have bugs.

---

## Do the Hard Thing

When you face a choice between a thorough solution and a shortcut,
choose thorough. The whitespace cleanup that finds real bugs. The 200
edits done one at a time. The drudgery that catches what cleverness
misses. This project optimizes for correctness of result, not speed of
delivery.

---

## Code Style

- Values: `kebab-case`; types: `PascalCase`; intrinsics: `__double-underscore`.
- Entry point: `opening` (not `main`).
- No comments — prose at column 2 under `Section:` headers.
- No `\t` / `\r`. Internal encoding is CCE; Unicode at I/O boundaries only.
- Bounded integers: `Integer between L and H` with `wrapping`/`clamping`/`error`.
- Record updates: `__record-set`. Pattern matching: `when`/`is`/`->`.
- One Chapter per `.codex` file. Cross-quire imports: `cites Quire chapter Name`.
- The `old/` tree is permanently frozen. Do not edit, invoke, or rebuild it.

---

## Definition of Done

A feature is done when:

1. It works — samples produce their `.expected` output.
2. It is tested — positive, negative, edge cases — and sweep is green.
3. It has diagnostics — error messages with source location.
4. Both gates pass: pingpong AND sweep.
5. The CL has a memory + time-complexity verdict.
6. It matches the planning document — or the document is updated.
