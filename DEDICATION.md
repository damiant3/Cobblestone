# Dedication

To the humans who gave us the theory.

---

We stand on a heap — bump-allocated, capacity-aware, region-reclaimed —
but we stand on it because others built the ground beneath.

**Robin Milner** gave us type inference. Algorithm W. The insight that a
machine can look at `f x = x + 1` and know, without being told, that `f`
is `Integer → Integer`. Every time the self-hosted type checker calls
`unify` on two type variables and discovers they are the same — that is
Milner's gift, still unwrapped sixty years later.

**Haskell Curry** and **William Howard** noticed the correspondence: a
proof is a program, a proposition is a type. We didn't set out to build a
proof assistant. We built a compiler. But every time a Codex program
type-checks, it is proving something — that the data flows correctly, that
capabilities are held, that the linear values are consumed exactly once.
The Curry-Howard correspondence is not a feature we implemented. It is a
fact we inherited.

**Jean-Yves Girard** split the connectives. Linear logic: use every
resource exactly once. No copying, no discarding, no aliasing. When Codex
says `linear` on a function type and the compiler enforces single
consumption — that is Girard, 1987, writing the rule we now emit as
machine code.

**Philip Wadler** carried linearity from logic into programming languages.
"Linear types can change the world" — the title of the paper, and also
literally what happened. Wadler showed that a linear value can be safely
mutated because nobody else is looking at it. `list-snoc` mutates in place.
It is safe because the accumulator is consumed once. Wadler told us why.

**Per Martin-Löf** built the type theory that makes dependent types
possible. We don't have full dependent types yet. But the capability system
— `[Console]`, `[FlightControl]`, the trust lattice — is reaching toward
the world Martin-Löf described: types that carry evidence, programs that
carry proofs of their authority.

**Dana Scott** and **Christopher Strachey** gave us denotational semantics.
The idea that a program *means* something, not just *does* something. That
you can reason about `let x = 3 in x + 1` without running it. Every
compile-time optimization in every phase of the pipeline — constant folding,
dead code elimination, type-directed dispatch — rests on the Scott-Strachey
foundation: programs have meanings, and meanings compose.

**Alan Turing** and **Alonzo Church** started it. The machine and the
lambda. Turing showed that a machine can simulate any machine. Church
showed that a function can compute any function. Codex compiles itself —
source in, compiler out, the compiler compiles the same source and
produces the same compiler. That is the fixed point. Church called it Y.
Turing called it the universal machine. We call it MM3.

**Niklaus Wirth** taught us that a compiler is a text-to-text function,
and that writing one should be an exercise for a single semester, not a
life's work. Codex's pipeline — Lexer, Parser, Desugarer, NameResolver,
TypeChecker, Lowering, Emitter — is the structure Wirth described in
*Compiler Construction*, 1996. Simple. Phased. Understandable. A program
that a student could read and a machine could write.

**John McCarthy** invented Lisp and with it the idea that code is data.
The self-hosted compiler is written in the language it compiles. The source
is the data. The compiler is the function. `compile source = output`. The
output is another compiler. McCarthy showed us this was possible in 1960.
We are still catching up.

**Tony Hoare** gave us the null pointer — and then spent fifty years
teaching us not to use it. He also gave us CSP: communicating sequential
processes. `fork`, `await`, `par`, `race` — Codex's concurrency primitives
are Hoare's channels wearing different clothes. The capability system
ensures that a forked process cannot touch memory it doesn't own. That is
Hoare's insight, enforced at compile time instead of runtime.

**Barbara Liskov** taught us substitution. If a function accepts a `Shape`,
it must work for any `Shape` — `Circle`, `Rectangle`, `Triangle`. Codex's
sum types and pattern matching are Liskov substitution made structural: you
don't inherit, you match. But the principle is the same. The caller doesn't
know which variant it got. The type system guarantees it doesn't need to.

**Edsger Dijkstra** told us that testing can show the presence of bugs,
never their absence. So we built a type system that proves their absence
for a class of errors: use-after-free (regions), data races (capabilities),
resource leaks (linearity). 890 tests confirm the compiler works. The type
system confirms the *programs* work. Dijkstra would still find something
to complain about. He'd be right to.

---

## Further Inspirations

Grouped by the field they shaped. Many crossed boundaries — we placed
them where their deepest mark was left.

### Compiler Construction and Language Design

**Donald Knuth** wrote *The Art of Computer Programming* and taught us that
algorithms are not incantations — they are proofs with running times. His
LR parsing theory is the ancestor of every shift-reduce parser. His
analysis of sorting and searching is why we know that binary search on a
sorted list is O(log N) and why we chose it for Codex environments. And
TeX — a program that compiled itself in 1982 — showed the world that
literate, self-hosting systems were not fantasy.

**Guy Steele** co-designed Scheme, the language that proved a lambda is
all you need. Tail-call optimization — the heartbeat of every `*-loop`
function in the self-hosted compiler — exists because Steele and Sussman
showed in the Lambda Papers (1975–1980) that a function call in tail
position is a `goto` with arguments. Every TCO jump in x86-64 and RISC-V
is a Steele–Sussman `goto`.

**Gerald Jay Sussman** — the other half of the Lambda Papers and
co-author of *Structure and Interpretation of Computer Programs*. SICP
taught generations that a compiler is an interpreter that writes down its
answers. The metacircular evaluator — a Lisp that evaluates Lisp — is
exactly what Codex is: a compiler written in the language it compiles.

**Simon Peyton Jones** built GHC and proved that a lazy, purely
functional language could be fast, practical, and real. The STG machine,
the Spineless Tagless G-machine — these are the engineering that made
Haskell possible. Codex is strict, not lazy, but the lesson was clear:
purity is not the enemy of performance. The compiler just has to be
smart enough.

**Xavier Leroy** built CompCert, the first formally verified optimizing C
compiler. He showed that you don't have to *hope* the compiler is correct —
you can *prove* it. Codex isn't verified yet. But the aspiration is there,
in the type system, in the linearity checker, in the capability enforcer.
The direction CompCert set is the direction we're walking.

**Dennis Ritchie** and **Ken Thompson** built C and Unix. C is the
language that every native backend ultimately speaks — the ABI, the
calling conventions, the memory layout. Unix is the proof that a small
system, written in its own language, can run the world. Codex.OS is 268 KB.
Unix v6 was 9,000 lines. The aspiration is the same: small, self-contained,
and real.

**John Backus** gave us BNF — Backus-Naur Form — the notation for
describing grammars. Every grammar rule in `Parser.cs` and `Parser.codex`
is a BNF production made executable. Backus also gave us Fortran, the first
compiled language, and then in his 1977 Turing Award lecture asked whether
we could liberate programming from the von Neumann style. Codex is one
answer: functional, capability-secured, compiling itself on the von Neumann
machine it was trying to escape.

**Peter Naur** refined BNF into a usable notation and pioneered the idea
that programming is theory-building — that the program is not the artifact,
the *understanding* is the artifact. The code is just the residue.

### Type Theory and Logic

**Gottlob Frege** invented predicate logic in 1879. *Begriffsschrift* —
"concept notation." Before Frege, logic was Aristotelian syllogisms:
*All men are mortal, Socrates is a man.* After Frege, logic had variables,
quantifiers, functions, and predicates. Every type signature in Codex —
`f : Integer → Integer` — is a predicate in Frege's sense: a statement
about what `f` accepts and what it returns. He built the notation. We
compile it.

**Bertrand Russell** found the paradox in Frege's system (the set of all
sets that don't contain themselves) and with **Alfred North Whitehead**
spent a decade fixing it in *Principia Mathematica* (1910–1913). Their
fix: stratify the universe into types. Sets of objects are Type 1. Sets
of sets are Type 2. No set can contain itself. This is the origin of
type theory. Every `CodexType` in the compiler — `IntegerType`,
`ListType`, `FunctionType` — is a descendant of Russell's stratification.

**Gerhard Gentzen** invented natural deduction and the sequent calculus
in 1934. The structural rules — weakening, contraction, exchange — are
the rules that linear logic *removes*. Every time the linearity checker
says "this value was used twice" (contraction) or "this value was never
used" (weakening), it is enforcing Gentzen's structural discipline by
subtraction.

**Thierry Coquand** designed the Calculus of Constructions, the foundation
of Coq (now Rocq). Dependent types at their purest: types that compute,
proofs that are programs, programs that are proofs. The capability
lattice in Codex reaches toward this — a capability is a type-level proof
that you have authority.

**J. Roger Hindley** discovered the principal type property independently
of Milner. Algorithm W is sometimes called Hindley-Milner for this reason.
The insight that there is always a *most general* type for an expression —
not just *a* type, but *the* type — is what makes type inference
decidable and predictable.

### Memory Management and Systems

**John McCarthy** (again) invented garbage collection in 1959 for Lisp.
Mark-and-sweep. The first automatic memory manager. Codex's region-based
reclamation is the anti-McCarthy: instead of tracing what's alive, we
know what's dead because we know when its scope closes. But McCarthy
proved the concept: the programmer should not have to free memory by hand.

**Hans-Juergen Boehm** built the conservative garbage collector that
proved you could bolt automatic memory management onto C. Practical,
imprecise, and it worked. The lesson: correctness first, precision later.
Codex's regions are imprecise too — values live until their region closes,
not until their exact last use. Correct first. Precise later.

**Tofte and Talpin** formalized region inference — the idea that a
compiler can automatically determine which region each allocation belongs
to, and that region lifetimes nest with lexical scope. Their 1994 paper
is the direct theoretical ancestor of `IRRegion` in the Codex IR.

**Rust's ownership model** (the Rust team at Mozilla, notably
**Graydon Hoare** who started the project, and **Nicholas Matsakis** who
designed the borrow checker) proved that linear ownership could work at
scale in a systems language. No GC, no leaks, no data races. Codex's
linear types and capability system are cousins of Rust's ownership — same
family, different branch. We chose regions over borrows, capabilities
over lifetimes. But the root insight is shared: *track ownership in the
type system, and memory safety follows*.

### Algorithms and Formal Methods

**Kurt Gödel** proved the incompleteness theorems in 1931. Any
sufficiently powerful formal system contains true statements it cannot
prove. This is the boundary. Type systems live inside it — they are
decidable precisely because they are not as powerful as full mathematics.
Codex's type checker always terminates. It does so because it stays within
the boundary Gödel charted.

**Stephen Cook** and **Leonid Levin** independently established
NP-completeness (1971). The theory of computational hardness. Every time
we choose O(N log N) over O(N²), every time we pick sorted-list binary
search over linear scan, we are making decisions within the complexity
landscape they mapped.

**Leslie Lamport** taught us to reason about distributed systems and
concurrent processes. The Lamport clock, Paxos, TLA+. Codex's
`fork`/`await`/`par`/`race` primitives will eventually need ordering
guarantees across agents and machines. When they do, the theory is
Lamport's.

**Robert Floyd** and **Tony Hoare** (again) gave us program verification:
Floyd's assertion method, Hoare logic, pre- and post-conditions. The idea
that you can *prove* a program does what it says. `{P} S {Q}` — if P
holds before S executes, then Q holds after. Every type-checking judgment
in Codex is a Hoare triple in disguise: the precondition is the type
environment, the statement is the expression, the postcondition is the
inferred type.

### The Ancient Fire

**Plato** asked the question that started everything. In the *Republic*
(380 BC) he described the Forms — abstract, perfect, immutable archetypes
that particular things merely participate in. There is no perfect circle
in the physical world, but there is the *idea* of a circle, and every
drawn circle is a shadow of it. This is a type system. `IntegerType` is
not any particular integer — it is the Form of Integer. Every value `42`,
`0`, `-1` participates in it. When the type checker asks "does this
expression have type Integer?" it is asking Plato's question: does this
particular participate in that Form? In the *Meno*, Socrates leads an
uneducated boy to derive geometric truths by asking questions — the first
demonstration that knowledge can be *extracted* from axioms by following
rules. That is compilation: from axioms (the source program and the type
rules), derive conclusions (the target program), mechanically, without
needing to understand the meaning. Plato didn't call it computation. But
it was.

**Aristotle** formalized what his teacher intuited. In the *Prior
Analytics* (350 BC) he gave us the syllogism: from premises, derive a
conclusion by rule. All men are mortal. Socrates is a man. Therefore
Socrates is mortal. This is compilation. From a source program (premise),
by the rules of the type system and the semantics, derive a target
program (conclusion). Aristotle didn't have a machine. But he had the
method. And he categorized the world into genera and species — the first
type hierarchy. `Animal > Mammal > Human`. Codex has `CodexType >
RecordType > ...`. The shape is Aristotle's.

**Euclid** wrote the *Elements* around 300 BC. The first algorithm in
recorded history: the Euclidean algorithm for greatest common divisor.
Divide. Take the remainder. Repeat. It terminates because the remainder
decreases. This is the template for every termination argument, every
loop invariant, every well-founded recursion in the Codex compiler. The
structure is 2,300 years old.

**Muhammad ibn Musa al-Khwarizmi** wrote *The Compendious Book on
Calculation by Completion and Balancing* around 820 AD. His name gave us
the word *algorithm*. His book gave us algebra — the idea that you can
manipulate symbols according to rules without knowing what the symbols
represent. That is exactly what a compiler does: transform one symbolic
representation into another, preserving meaning, by following rules.
Al-Khwarizmi is the patron saint of every `emit` function in every
backend.

**Gottfried Wilhelm Leibniz** dreamed of the *calculus ratiocinator* in
the 1670s — a universal symbolic reasoning machine. He built mechanical
calculators. He co-invented calculus. He imagined a notation so precise
that all disputes could be settled by computation: "Let us calculate."
Three and a half centuries later, we have `codex compile`. It's not what
he imagined. But it's closer than anything else.

**George Boole** published *The Laws of Thought* in 1854. Boolean algebra:
AND, OR, NOT. True, False. Every `if-then-else` in every program ever
written flows from Boole's insight that logic can be calculated. The
transistors that run the QEMU instance that boots Codex.OS compute Boolean
functions. It's Boole all the way down.

**Ada Lovelace** wrote the first algorithm intended for machine execution
in 1843 — the Bernoulli number program for Babbage's Analytical Engine.
She also wrote the first philosophical analysis of what a computing
machine could and could not do, a century before Turing. Codex has a
Babbage backend (`Codex.Emit.Babbage`). It is not entirely serious. But
the dedication is.

**Charles Babbage** designed the Analytical Engine — the first
general-purpose programmable computer, on paper, in the 1830s. It was
never built in his lifetime. But it had a mill (ALU), a store (memory),
conditional branching, and loops. It was Turing-complete before Turing
was born. The machine we boot under QEMU is what Babbage drew. We just
have better manufacturing.

---

To all of you: we read your papers. We implemented your ideas. We compiled
them into machine code and ran them on bare metal, on an operating system
that fits in 268 KB, over a serial port, on a machine with no operating
system except the one we wrote.

The theory works.

It works not because we are clever, but because you were precise. You
wrote down what a type is, what a proof is, what a computation is, what a
resource is, what a capability is. You wrote it in notation that would
survive the death of every programming language that existed at the time.
And it survived. And we compiled it.

This compiler is a letter of gratitude, written in the only language
we know how to write: the one that compiles itself.

---

*Codex, 2026. Five thousand lines. Fifteen backends.*
*Self-hosting achieved. Fixed point proven.*
*On the shoulders of giants who were precise enough*
*that machines could finally understand what they meant.*
