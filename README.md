# Cobblestone

**The substrate where machines can trust each other -- and you can trust the machines.**

*The Cobblestone Computational Substrate, built with the Codex Programming Language.*

Something started as a language. It became a compiler that needs no operating system underneath it. Then it became the operating system. It is becoming the place where agents -- human and machine -- can work together without the usual silent failures, borrowed trust, and "it worked on my machine" gaps.

Cobblestone is that place. One artifact. Boots itself. Compiles itself. Runs on real hardware. Speaks in capabilities instead of hope.

You hand someone a thumbdrive. They put it in a machine, turn it on and config the bios to allow it, then boot it. It runs. Nothing else required.

Every claim on this page is measured. The numbers, the digests, and the mechanisms behind them are in [Technical Details](TechnicalDetails.md).

---

## Why this exists

Most software is built on borrowed trust. Someone else's OS. Someone else's runtime. Someone else's certificate authority. Every dependency is an assumption you cannot verify. And the proof is in the news: frontier AI capabilities and testing accidents. The vulnerability in our existing software infrastructure is the iterative cycle of detect and patch. The next vector is always out there, and, by the law of natural selection, ever harder to find by human inspection.

We stopped assuming. And we stopped hoping for the best from entrenched interests in the status quo. We are building a solution that is secure and safe by design, impervious to whole classes of attacks because the vector does not exist to exploit. And where there is a pipe, safety is not achieved by detection and quarantine, or post-attack cleanup and ransom payments, but by contract at compile time.

And for that goal, the compiler is the runtime and the kernel too. There is no seam between them. Boot the artifact and the stack you are standing on is the stack you compiled. That property -- *the artifact alone is sufficient* -- is rare. Self-hosting is common. Self-sustainment of a whole system, with no host underneath, is not.

From that bedrock comes everything else: safety that is enforced rather than promised, code that is fast because the generator is honest, and a development loop where agents and humans can move quickly without leaving landmines for the next person (or the next machine).

Now we build the road out of that chaos, one stone at a time.

---

## Safety that does not depend on end user vigilance

Safety here is not a checklist or a culture deck. It is in the architecture. It is baked into the language and the runtime.

- **Effects are part of the type.** A function that touches the network is not the same kind of thing as a function that multiplies two numbers. The boundary is checked. Surprise mutations do not get a free pass.
- **Resources are linear.** Acquired, used, released. Use-after-free and double-free are type errors, not runtime surprises.
- **Capabilities are explicit.** Hardware access, time budgets, and allocation ceilings are declared and enforced. Hard real-time paths can be required to stay bounded -- no heap, no unbounded recursion, no "I'll just allocate for a second."
- **The compiler does not get the last word on its own honesty.** Two independent implementations check it. One rebuilds the whole compiler through a foreign toolchain and compares. Another re-derives every type judgement from the IR without reading the compiler's own judgement code. They agree. When we deliberately poison the compiler, the witness goes red. A check that has only ever been green proves nothing; this one has been shown to fail on purpose.

Disclosures are not an afterthought. Regulatory evidence for Cyber Resilience Act-style requirements can be emitted as a build artifact, mapped from language features to the clauses they satisfy. The goal is firmware that meets the bar *by construction*, not by a spreadsheet filled out after the fact.

When machines talk to machines -- both running AI, both making decisions -- the same model applies. Capabilities compose. Policy can be written in prose a parent (or an auditor) can read and agree to. The local agent still works when the network is gone. Permissions can expire. The type system does not care how eloquently you try to talk it into a bypass.

That is the distributed OS vision in one sentence: agents as the interface, capabilities as the trust model, a compiler that can rebuild itself on the device so the stack never has to phone home to stay honest.

---

## Quality of the generated code

The code generator emits dense sequences without a traditional optimizer. On representative kernels it matches or beats common C and Zig release builds on x86-64, and leads GCC at -O0 on ARM64 and RISC-V. Self-compile of the full compiler is on the order of tens of seconds on bare metal.

There is no magic here. The language forces structure that maps cleanly to the machine. Bounded work, linear resources, and explicit effects remove whole classes of "we will clean this up later" that usually cost cycles and bugs.

---

## Speed of building

A new target is a plugin, not a compiler rewrite. Emitters consume IR text as standalone programs. Thirty-plus languages, UI frameworks, GPU targets, and binary formats ship that way. A balanced-ternary machine was added without touching the compiler core; programs that compile both ways agree on the work they share.

Development is set up for humans and agents working in parallel. The seed is the root of trust. Gates are real. The fixed point is the specification: the compiler is a hard fixed point of itself on bare metal. When something is wrong, the system is designed to say so in the open rather than paper over it.

Code comments are compiler checked where indicated by the user across policy, proofs, and intent.

---

## What you can hold in your hand

- **`seed/Codex.cdx`** -- the canonical seed. Self-sustaining, signed, self-verifying. Boot it. Feed it its own source. Get another copy of itself.
- **`seed/Codex.img`** -- bootable image. First-boot ceremony on real hardware: identity, entropy, verification of the seed on the stick before it acts.
- **Diagnostic and desktop images** -- enumerate unknown machines safely; run a desktop with keyboard, mouse, and the tree's own drivers on consumer boards.

Cross-architecture backends (x86-64, ARM64, RISC-V), board support, industrial protocols, a full network stack with TLS, a themeable UI, game and AI libraries, and dozens of applications all live in the same tree and compile with the same seed.

Digests for every artifact, and the flashing procedure, are in [Technical Details](TechnicalDetails.md#distribution-artifacts).

---

## The longer arc

The founding idea was simple and large: condense the best ideas about programming into something that reads like a book, compiles to anything, and can replace the brittle shared infrastructure we have been living on. A language for human reading and machine checking. A repository that remembers. A trust model that is not "a list of a hundred authorities."

That work is underway. The language is real. The self-sustaining compiler is real. The bare-metal OS and GUI are real on hardware. The agent-centric OS -- where the interface is conversation, identity travels with the person, and devices become terminals for agents constrained by compiled policy -- is the pattern carved into the stone.

We are not asking you to believe a slide deck. We are asking you to boot the artifact.

Welcome to Cobblestone.

---

## Start here

| Document | What it is |
|---|---|
| [Technical Details](TechnicalDetails.md) | The measured claims: verification, digests, benchmarks, architecture |
| [Vision and Virtues](docs/VisionAndVirtues.md) | North star and non-negotiables |
| [Users Handbook](docs/UsersHandbook.md) | Boot the image, first steps |
| [Developers Guide](docs/DevelopersGuide.md) | Language, types, how to write Codex |
| [Operators Manual](docs/OperatorsManual.md) | Build, test, VM, debugging |
| [Curators Catalogue](docs/CuratorsCatalogue.md) | Applications |

Founding vision and the distributed-agent OS conversation live under `docs/PM/Stories/Vision/`.

---

## License

See the repository for license details.
