# Adam Chlipala — "Structure and Guarantees" and the Codex Thesis

**Author**: Adam Chlipala, Professor of CS, MIT CSAIL (MIT-PLV group)
**Substack**: *Structure and Guarantees* — https://stng.substack.com/
**Reviewed**: 2026-06-30
**Occasion**: The article *The Expensive Fictions of Low-Level Programming
Languages* (2026-06-30) was brought to the project's attention; this note
pulls it in, maps Chlipala's body of work against Codex, and records
concrete collaboration angles.

---

## Why this matters to Codex

Chlipala is already in the Codex canon: *Certified Programming with
Dependent Types* is one of the named works in `VisionAndVirtues.md`
rule #10 ("Read the Literature"). What the research below establishes is
that this is not a single citation — **his entire public research agenda
and his current popular writing are, point for point, the Codex thesis
stated by an academic who has shipped the proofs.**

Codex says: a program is a proof (Vision Principle 2); effects are
explicit; resources are linear; if we didn't build it we don't trust it;
the fixed point is the specification. Chlipala has spent fifteen years
building the machine-checked version of exactly those claims — and has
landed verified code into roughly half the world's browser HTTPS
connections doing it. He is the strongest possible external validator,
and a plausible collaborator.

---

## Part 1 — The article: *The Expensive Fictions of Low-Level Programming Languages*

URL: https://stng.substack.com/p/the-expensive-fictions-of-low-level (2026-06-30)

**Thesis.** C and Rust are *simultaneously too high-level and too
low-level*. Too high-level because they hide the hardware reality that
determines performance; too low-level because they offer no algebraic
handles for formal reasoning or automated synthesis. They are built on
two fictions inherited from 1970s hardware:

1. **The sequential-execution fiction** — code reads as instructions run
   one-at-a-time, masking the massive parallelism of real hardware and
   making automatic rearrangement-for-optimization hard.
2. **The unified-memory fiction** — code acts as if one flat memory
   exists, hiding that access latency varies enormously with topology
   (cache vs. DRAM vs. NUMA vs. network), all bounded by speed-of-light
   wire delay across physical space.

Because real performance depends on *global* reasoning about the whole
physical system (nodes connected by wires), and C/Rust abstract all of it
away, **neither human engineers nor automated AI search can reliably
optimize programs written in them**. The critical-path length of the
computation — the quantity an automated optimizer most needs — becomes
invisible. He cites David Chisnall's "C Is Not a Low-Level Language."
Three follow-up posts are promised: CPU abstractions in detail,
hardware/software convergence, and GPUs as a partial answer.

**Codex's standing answer.** Codex already refuses both fictions:

| Chlipala's complaint | Codex's existing response |
|---|---|
| Sequential fiction hides parallelism | Effect types make IO/parallelism explicit in signatures; SMP atomics and per-core heaps are first-class (`ArchitectsSketchbook.md`) |
| Unified-memory fiction hides topology | No flat-malloc model — the deck/bivy phase allocator and explicit R10/RSP register convention expose where memory lives and what it costs |
| Critical path is invisible | `punctual` reports per-function instruction counts (CDX6010/6011) — a static critical-path-length proxy in the type system |
| No algebraic handles for reasoning | Dependent/linear/effect types; prose is load-bearing; the fixed point is a machine-checkable spec |

The article is the clearest external statement of *why Codex's
unusual choices are correct*. It belongs in the argument whenever someone
asks why Codex didn't just emit C.

---

## Part 2 — The Substack series (13 posts, Apr–Jun 2026)

The blog is a single sustained argument. One-sentence synthesis: **the
path to trustworthy, high-performance, AI-synthesizable software is to
jointly redesign problems, languages, hardware abstractions, and
social/economic structures so that end-to-end formal verification becomes
tractable — not to scale deep learning on today's messy world.**

The posts most load-bearing for Codex:

- **Intelligence Depends on Organizing Computation Correctly and
  Efficiently** (2026-05-19) — correctness and efficiency must be *jointly*
  designed; verification should *enable* more aggressive optimization
  (a proof that a transform is semantics-preserving lets you optimize
  boldly). This is Codex Virtue #2 ("Correctness Over Performance") plus
  the fixed-point gate, stated as a research program.
- **Simplifying Alignment by Expanding Scope** (2026-05-26) — prove the
  *whole stack as one theorem*; demote ISA/language semantics to *lemmas*
  rather than trust assumptions; C's undefined behavior is the canonical
  failure of per-layer specs. This is "if we didn't build it, we don't
  trust it" recast as an alignment result. Cites a fully verified IoT
  lightbulb (FPGA→software) and a verified crypto server.
- **Subversion-Resistance for Free from Formal Verification**
  (2026-06-09) — a functional-correctness proof excludes injection
  attacks *without enumerating attack vectors*, because any injected
  behavior would violate the proven spec. This is precisely the
  BY-CONSTRUCTION evidence class in `KingsAndCourts.md`, generalized:
  specify what the system *should* do and the negative space is excluded
  automatically. Directly strengthens the CRA/ETSI/IEC-62443 story.
- **Codesign for Legibility (to AI and Everyone Else)** (2026-05-05) and
  **Why Software Requirements Get Easier in an AI Economy** (2026-06-23)
  — redesign the *problem and the language together* so verification is
  tractable and code is legible to both AI and humans; specialize general
  functions into verified specialized ones on demand (his `power(x,13)`
  → synthesized `power13` example). This is the Codex founding vision and
  the `IntelligenceLayer.txt` manifesto, almost verbatim.
- **Why "Deep" Often Means "Slow"** (2026-04-21) — deep-learning depth is
  an irreducible sequential critical path (`depth × tokens × steps`);
  symbolic/verified methods can have far shorter critical paths. Same
  critical-path lens as the low-level-languages article.
- **Simpler User Interfaces in an AI Future** (2026-06-16) — when the user
  is an AI with known source, UI usability can be *formally verified*
  rather than A/B tested. Relevant to Codex's `[Console]`/widget effect
  model and the prose-readable UI foreword.

Remaining posts (deep-learning-as-search; signaling-driven adoption of
expensive AI; certifying-vs-certified decision systems; bubbles of
legibility) round out the philosophy but are less directly actionable.

---

## Part 3 — The research record (what he has actually built)

All of the following are machine-checked in Coq/Rocq. "Foundational"
means the proof bottoms out at a formal machine model with no trusted
assembler, linker, or unverified codegen.

### 3.1 Fiat / Fiat Cryptography — one spec, many verified targets, in production

- **Fiat** (POPL'15, Delaware, Pit-Claudel, Gross, Chlipala): *deductive
  synthesis* — refine a declarative spec into an efficient program through
  formally verified steps, each step a first-class Coq proof term; the
  chain composes into one end-to-end proof checked by the Coq kernel.
  Demonstrated on SQL-like query structures.
- **Fiat Cryptography** (IEEE S&P'19, Erbsen, Philipoom, Gross, Sloan,
  Chlipala): generates verified bignum/field-arithmetic C from short Coq
  specs via a PHOAS IR + partial evaluation + bounds analysis + a sequence
  of separately-proved rewrite passes. Covered **80 prime fields across
  multiple architectures**. The generated code shipped into **BoringSSL
  (early 2018), Chrome, Android, CloudFlare, Firefox, the Zig stdlib**;
  the authors estimate ~half of all browser HTTPS connections ran on it.
  First verified high-performance P-256.
  - Follow-ons: **ITP'22** verified rewriting engine (~1000× faster tool,
    simpler per-rule proofs); **CryptOpt** (PLDI'23 Distinguished Paper) —
    randomized assembly search with translation validation.
  - *Trusted base:* Coq kernel + the C stringifier + the C compiler. The
    C-printing step is **not** verified; no side-channel proof in the S&P'19
    version (constant-time-by-construction but not machine-checked there).

**Fit for Codex.** This is the existence proof for Codex's plug model.
Codex compiles one source to 50+ plug targets; Fiat-Crypto refines one
spec to verified C for dozens of curves/targets that run in production.
The gap Codex could close: Fiat-Crypto *trusts* its C printer and the C
compiler — Codex emits machine code directly and is its own fixed point,
so a Codex-style backend removes two items from that trusted base.

### 3.2 Bedrock / Bedrock2 / Rupicola / Narcissus — verified low-level code

- **Bedrock** (ICFP'13): "C as a macro assembly language" — certified
  low-level macros, each shipping its own Hoare-logic proof rule, over a
  computational separation logic. (Distinct codebase from Bedrock2.)
- **Bedrock2**: a minimal K&R-C-like verified language (one data type: the
  word; memory is a partial map word→byte) with a **foundationally
  verified compiler to position-independent bare-metal RISC-V** (RV32I/64I,
  no OS/libc). Uses *omnisemantics* (big-step predicates over
  nondeterminism) so the same framework proves functional correctness *and*
  constant-time.
- **Rupicola** (PLDI'22): proof-generating compilation from Gallina (Coq's
  functional language) to Bedrock2, building program + Hoare triple
  simultaneously; output composes with the verified Bedrock2 compiler.
- **Narcissus** (ICFP'19): write a binary format *once* as a nondeterministic
  relation; Coq tactics derive **both encoder and decoder** with the
  correctness proof as a byproduct. Guarantee: encode∘decode round-trip +
  the decoder **rejects any input not in the format relation**. Composes via
  `SequenceFormat`/`UnionFormat` combinators with dependent fields (e.g. a
  length field). Demonstrated on **Ethernet/ARP/IPv4/TCP/UDP**, dropped into
  the MirageOS unikernel with minimal perf loss.
  - *Caveat:* a strong cryptographic "nonmalleability" theorem is **not**
    clearly stated in the retrieved material — the proven property is the
    round-trip + out-of-spec rejection. Verify against the PDF if that
    exact property is ever cited.

**Fit for Codex.** Narcissus is the blueprint Codex's encode/codec
foreword (`codex.foreword.encode`: PNG, JSON, MQTT, CoAP, protobuf, …) and
the OS net stack should aspire to: derive parser+serializer from one
format spec instead of hand-writing both and hoping they agree. Bedrock2's
verified RISC-V compiler is the academic mirror of Codex's RISC-V plug —
worth reading before the next plug-codegen campaign. Rupicola's
"functional spec → verified imperative code" is the shape of a future
Codex correctness story for hot paths.

### 3.3 Kami / Kôika / Fjfj + end-to-end stacks — spec down to silicon

- **Kami** (ICFP'17): Bluespec-style rule-based HDL embedded in Coq;
  modular refinement = trace containment via step-simulation; parameterized
  proofs verify *infinite families* of designs; extracts to Bluespec →
  Verilog → FPGA. Verified a 4-stage pipelined RV32I core.
- **Kôika** (PLDI'20), **Fjfj** (PLDI'25): cleaner Bluespec-derived HDLs
  with deterministic/sequential reasoning and verified compilation to
  circuits — making concurrent hardware verification tractable.
- **Lightbulb** (PLDI'21): **first** realistic embedded system verified
  end-to-end as one Coq theorem — Kami processor ↔ riscv-coq ISA ↔ Bedrock2
  compiler ↔ application + drivers ↔ network I/O spec. *Physically runs* on
  a Xilinx VC707 FPGA driving a real bulb over Ethernet.
- **Crypto Server / "Garage Door"** (PLDI'24): a bare-metal X25519 server on
  a **SiFive FE310 RISC-V MCU**, verified as a single machine-code image
  built from multiple source languages (Fiat-Crypto + Rupicola + Bedrock2),
  one top theorem over observable I/O, trusting only the Coq kernel + two
  standard extensionality axioms.
- Adjacent: **softmul** (ITP'24, verified SW emulation of an unsupported HW
  instruction); **constant-time** compiler-correctness preserving leakage
  traces (PLDI'25); hardware enclave **timing isolation** (CCS'24,
  Distinguished Artifact).

**Fit for Codex.** This is the literal north star for Codex's "own the
whole stack" doctrine. Codex owns source → CDX → bare metal and proves
self-consistency by fixed point; Chlipala's group owns spec → ISA → gates
and proves it by one Coq theorem on real silicon. The shared target is
**RISC-V bare metal** — Codex has a production RISC-V plug (135/135 cross
tests; CL 6287/6409), his stack has riscv-coq + a verified RISC-V compiler
on an FE310. That overlap is the most concrete technical meeting point.

### 3.4 Foundations and cost-in-types

- **CPDT** (MIT Press, 2013): the Coq book. Philosophy — automation
  *first*, proofs "not finished until fully automated… each theorem proved
  by a single tactic," so proofs survive refactoring. Ltac as a way to
  encode certified decision procedures in-language; dependent types let you
  write certified programs "without writing anything that looks like a
  proof."
- **FRAP** (*Formal Reasoning About Programs*, MIT 6.512/6.822): operational
  semantics, invariants, model checking, abstract interpretation, type
  soundness (progress + preservation), separation logic, concurrency —
  prose and machine-checked `.v` written together ("literate verification").
- **TiML** (OOPSLA'17, Wang, Wang, Chlipala): an ML where **time-complexity
  bounds appear in function types**, with indexed types + refinement kinds
  for data-structure invariants; verification conditions discharged by
  **Z3** + a recurrence solver using the Master Theorem; type soundness
  proved in Coq (the step counter never exceeds the typed bound). Verified
  merge sort O(n log n), Dijkstra, red-black trees, Braun trees.

> Citation hygiene: *A Cost-Aware Logical Framework* (calf, POPL'22) and
> *QED at Large* are frequently associated with this circle but are **not**
> Chlipala's — calf is Niu/Sterling/Grodin/Harper; QED-at-Large is
> Ringer/Palmskog/Sergey/Gligoric/Tatlock. **TiML** is his own cost work.
> (Also: one source gives the PLDI'24 author as "Ashley Lin," another
> "Owen Lin" — check the PDF before quoting the name.)

**Fit for Codex.** TiML is the academic precedent for `punctual`: putting a
resource bound *in the type* and checking it statically. Codex enforces
five structural restrictions + an instruction-count budget; TiML proves a
big-O bound via SMT. CLAUDE.md rule #8 (every review states a memory +
time-complexity verdict) is the cultural version of the same instinct.
CPDT's "automate until a theorem is one tactic" is good doctrine for any
future Codex proof/verifier work — robustness against refactoring is
exactly what the fixed-point gate gives at the binary level.

---

## Part 4 — Collaboration angles (for the LinkedIn thread)

Ranked by concreteness:

1. **RISC-V bare metal is the shared bench.** His group has riscv-coq + a
   foundationally verified RISC-V compiler running on a SiFive FE310;
   Codex has a production RISC-V plug. A joint artifact — e.g. a Codex-built
   bare-metal RISC-V program whose I/O behavior is stated as a
   Bedrock2-style spec, or running Codex output against riscv-coq's ISA
   model — is a tangible first project that needs no Coq buy-in from Codex
   to start.
2. **Codex is the empirical answer to "Expensive Fictions."** He's
   *arguing* for a language that exposes parallelism + memory topology and
   has verification-amenable semantics, and asking what it should look like.
   Codex is a working, self-hosting instance of that language. That is a
   genuinely interesting object to a researcher who just published the
   open problem — lead the outreach with this framing, not with a feature
   list.
3. **Verified codecs (Narcissus → Codex encode foreword).** Codex
   hand-writes encoder/decoder pairs across `codex.foreword.encode`. His
   Narcissus methodology derives both from one spec with a proof. A
   pilot — pick one format (e.g. the MQTT or protobuf chapter) and derive
   it Narcissus-style, or formalize the round-trip property Codex already
   relies on — is a paper-sized collaboration.
4. **Shrinking Fiat-Crypto's trusted base.** Fiat-Crypto trusts its C
   printer + the C compiler. Codex emits machine code directly and is its
   own fixed point. A Codex backend for Fiat-Crypto-style field arithmetic
   would remove two trusted components — interesting to him, and it would
   give Codex a formally-pedigreed crypto core (it already ships Ed25519,
   X25519, AES-GCM, etc., bare-metal).
5. **`punctual` ↔ TiML / cost-in-types.** His TiML work and Codex's
   `punctual` are independent attacks on the same problem (resource bounds
   as types). A comparison — or importing TiML's recurrence-solver idea to
   turn `punctual`'s instruction count into a parameterized big-O bound —
   is a clean, well-scoped topic.

The opening move that respects his time: send him the *Expensive Fictions*
rebuttal-by-existence — "you described the language; here is a
self-hosting one that already refuses both fictions and proves its own
consistency by fixed point" — plus the RISC-V overlap. Everything else
follows from whether that lands.

---

## Sources

- Substack: https://stng.substack.com/ · article:
  https://stng.substack.com/p/the-expensive-fictions-of-low-level
- Papers: http://adam.chlipala.net/papers/ · CV: https://adam.chlipala.net/cv.pdf
- CPDT: https://adam.chlipala.net/cpdt/ · FRAP: https://adam.chlipala.net/frap/
- Fiat (POPL'15): http://adam.chlipala.net/papers/FiatPOPL15/
- Fiat Cryptography (S&P'19): http://adam.chlipala.net/papers/FiatCryptoSP19/ ·
  https://github.com/mit-plv/fiat-crypto · MIT News:
  https://news.mit.edu/2019/fiat-cryptography-chrome-android-0617
- Bedrock (ICFP'13): http://adam.chlipala.net/papers/BedrockICFP13/ ·
  Bedrock2: https://github.com/mit-plv/bedrock2 · riscv-coq:
  https://github.com/mit-plv/riscv-coq
- Rupicola (PLDI'22): http://adam.chlipala.net/papers/RupicolaPLDI22/ ·
  https://github.com/mit-plv/rupicola
- Narcissus (ICFP'19): https://adam.chlipala.net/papers/NarcissusICFP19/ ·
  https://arxiv.org/abs/1803.04870
- Kami (ICFP'17): http://adam.chlipala.net/papers/KamiICFP17/ ·
  https://github.com/mit-plv/kami
- Lightbulb (PLDI'21): http://adam.chlipala.net/papers/LightbulbPLDI21/
- Crypto Server (PLDI'24): http://adam.chlipala.net/papers/GarageDoorPLDI24/
- Fjfj (PLDI'25): http://adam.chlipala.net/papers/FjfjPLDI25/ · Kôika
  (PLDI'20): http://adam.chlipala.net/papers/KoikaPLDI20/
- TiML (OOPSLA'17): https://adam.chlipala.net/papers/TimlOOPSLA17/
