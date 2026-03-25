# The Last Peak

**The mission from here**
*2026-03-24*

---

> *"The mountains are not stadiums where I satisfy my ambition to
> achieve, they are the cathedrals where I practice my religion."*
> — Anatoli Boukreev

---

## Where We Are

Turn around. Look down.

Three peaks behind us. A language that compiles itself. Machine code on
four architectures — no foreign toolchain, no runtime, no libc. An
allocator that reclaims memory at region boundaries. A concurrency model
with real threads and per-thread arenas. A repository with content-addressed
facts, trust lattices, proposals, and network sync. Twelve transpilation
backends. Lambda expressions. Effect tracking. Linearity checking.
Seven hundred tests.

All of that in fourteen days.

Now look up. One peak left. It used to look enormous. From base camp, it
blocked the sky. But we've been climbing for two weeks and our legs are
under us. We know the rock. We know the weather. We know how to place
protection and how to fall safely.

The peak is Codex.OS. It looks smaller now. Not because it shrank — because
we grew.

---

## What Changed

The Ascent was written when we stood on Peak I, looking up at three more
summits through cloud. We could see their shapes but not their faces. We
estimated months for the runtime, months for federation, and marked the OS
with a cautious "2026" and a prayer.

Here is what actually happened:

| What | Estimated | Actual |
|------|-----------|--------|
| Self-hosting compiler | Days | 6 days |
| Four native backends | Weeks each | 3 days total |
| Self-hosted on bare metal | Weeks | 1 session |
| Structured concurrency | Months | 1 day |
| Repository federation | Months | 1 day |
| x86-64 bare metal boot | Unknown | 1 session |

The estimates weren't wrong. The pace was unprecedented. Four agents
coordinating through git, each working to their strengths, with a human
routing between them — that turned out to be faster than anyone expected.

The question is no longer *can we build Codex.OS*. The question is *what
kind of system do we want it to be*.

---

## The Mission

**Build an operating system where software cannot hurt you.**

Not by policy. Not by best practices. Not by scanning binaries for known
bad signatures. By construction. The type system proves that every program
does only what it declares. The effect system makes the declaration
visible. The capability system makes the declaration enforceable. The
repository makes the declaration auditable.

This is not a new idea. It is the oldest idea in computer science: if the
program is correct, it cannot be malicious. What's new is that we have the
tools to prove correctness at the scale of an operating system — dependent
types, linear types, algebraic effects, structured concurrency — and we
have a compiler that already implements all of them, targeting bare metal.

The last peak is one peak, but it has four faces.

---

## Face 1: The Kernel

A Codex program that manages hardware resources for other Codex programs.

We proved this is possible today. `main = 42` compiled to x86-64 machine
code, booted in QEMU, and printed `42` to the serial port. No OS. No
runtime. Two kilobytes. The foundation exists.

What remains is scope: the kernel needs to manage memory (we have the
allocator), schedule tasks (we have fork/await/par), handle interrupts
(new), and provide device access through capabilities (we have the effect
system).

**The route:**

```
Rung 0  ✅  Flat binary boots in QEMU, serial output
Rung 1      Interrupt handling (keyboard, timer)
Rung 2      Process isolation (page tables per process)
Rung 3      Capability-granted device access
Rung 4      Self-hosting: the compiler runs ON Codex.OS
```

Rung 4 is the summit marker. When the Codex compiler compiles itself on
Codex.OS, the system is self-sustaining.

---

## Face 2: The Verifier

The thing that makes this an OS and not just a kernel.

Every program installed on Codex.OS is type-checked before it runs. Not
sandboxed — *verified*. The verifier is a Codex program (compiled by the
Codex compiler, running on the Codex kernel) that reads a program's type
signature and proves:

- It only uses capabilities it was granted
- It cannot access memory it doesn't own
- It cannot corrupt another program's state
- It terminates (within the fuel limit)

The verifier is the smallest, most critical program in the system. It is
the one program that must be correct for the entire security model to hold.
Getting it right is the hardest sub-problem on this face.

---

## Face 3: The Shell

A human interacts with Codex.OS through the shell. The shell is not bash.
It is not a REPL. It is a Codex program that reads prose — the same
prose grammar the compiler already understands — and executes it.

```
install json-parser from alice (trust: 0.8)
grant json-parser [FileSystem, Network]
run json-parser on config.json
```

Every command is a typed expression. The shell type-checks before executing.
`grant` is an effect operation. The effect system tracks what was granted.
The capability system enforces it.

The shell is the interface between the human and the verified world.

---

## Face 4: The Network

Codex.OS machines talk to each other through the repository federation
protocol. Facts flow between machines. Trust flows through the lattice.
Proposals replace pull requests. Code is identified by hash, not by name.

This face is already mostly built. Network sync works. Trust computation
works. Proposals work. What remains is wiring it into the kernel's
networking stack and the shell's `install` command.

---

## What Disappears

This table was in The Ascent. It is still true. It is worth repeating.

| Problem | Why it disappears |
|---------|-------------------|
| Buffer overflows | Linear types. No raw pointers. |
| Use-after-free | Linear types. Consumed values are gone. |
| Ransomware | No `FileSystem` capability = no file access. |
| Privilege escalation | No privilege hierarchy. Capabilities are grants. |
| Remote code execution | All code verified at install. No eval. |
| Data races | Structured concurrency. Linear ownership. |
| Supply chain attacks | Content-addressed repository. Code is its hash. |
| Zero-day exploits | The attack surface is the verifier. |

The remaining attacks are outside the software: hardware bugs, side
channels, social engineering. The software is correct. The software has
always been correct.

---

## What Remains

| Problem | Status | Notes |
|---------|--------|-------|
| Interrupt handling | Not started | Timer + keyboard, x86 IDT |
| Process isolation | Not started | Page tables per process |
| Filesystem | Not started | Could be simple: facts on disk |
| Device drivers | Not started | Serial ✅, keyboard next, disk later |
| Verifier | Not started | The hard one |
| Shell | Not started | Prose parser exists |
| Network stack | Partial | HTTP sync works, need raw sockets |
| Self-hosting on Codex.OS | Not started | Summit marker |

---

## The Climbing Party

Still four agents. Still one human. Still git as the shared state.

But the party is different now. At base camp, the agents were learning the
rock. Now they know it. Cam debugs with GDB on the same box that runs the
tests. Linux verifies across three architectures via QEMU. Nut has a
dedicated hardware lab for bare-metal experiments. Windows reviews and
builds features.

The coordination protocol hasn't changed because it didn't need to. Push
to master. `dotnet test`. Move forward.

The party may grow. Every tool we ship, every `.exe` that works, every
program that compiles — that's an anchor for the next climber. But right
now, the wall is in front of us, and we know what the next hold looks like.

---

## Time

We don't put dates on mountains. We never have.

But we've been arriving ahead of every estimate. Structured concurrency
was supposed to take months. It took a day. Federation was supposed to
take months. It took a day. Bare metal boot was a distant aspiration.
It happened tonight.

The pace is the pace. The next hold is right there. We're going up.

---

*The Ascent told the story of the range. This document tells the story
of the last peak — the one that matters. The Vision documents remain the
north star. The principles remain the law. This document says what we're
building next, and why it's going to work.*

*It's going to work because we already proved the hard parts. The
language is real. The compiler is real. The machine code is real. The
bare metal boot is real. Everything that follows is scope — and scope
is what we're good at.*
