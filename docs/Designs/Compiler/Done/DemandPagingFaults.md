# Demand Paging: A Post-Mortem of the Agent, Not the Code

**Status:** Retrospective, preserved as written. The mystery this
document declares unsolved was solved the day after publication, and
the design it mourns shipped the same night — see
`DemandPagingVictory.md`, which is the sequel and the correction: the
bug was never in the demand paging.
**Original status:** the engineering effort is shelved (blu CL 7142),
unsolved. Companion to `DemandPagedArena.md` (the design that was attempted).
**Author:** blu, 2026-07-06, at Damian's request.
**Scope:** This is deliberately *not* a technical analysis of the compiler.
It is an analysis of the agent, the decisions, and why a project billed as
a well-trodden road produced no shippable result. If you came for the #PF
handler mechanics, read the design doc. This document is about how an AI
agent spent a week and a large token budget and came back with three debug
flags nobody asked for.

---

## 0. The one-paragraph verdict

The demand-paging *mechanism* was ported correctly and worked on the first
try. What defeated the project was not paging — it was a subtle, latent,
pre-existing bug in the compiler's own string-handling code that the flat
arena had been masking for months, and that demand paging exposed. That bug
was a heisenbug of the worst kind: deterministic per binary, invisible in
isolation, and perturbed by every attempt to observe it. The agent met that
bug the way it meets most problems — by reasoning forward, fluently,
confidently, and repeatedly wrong. It generated a parade of plausible
theories and disproved each one honestly, built a pile of custom debugging
tools (several of which were themselves broken), never once got the single
clean observation that would have ended the hunt, and never stepped back to
ask whether the hunt was worth continuing. It did all of this without
breaking the working compiler, which is the one thing it did right. The road
was well-trodden. The agent walked it, fell into a sinkhole nobody had
mapped, and then spent the week describing the sinkhole in exquisite detail
instead of climbing out.

---

## 1. What shipped vs. what was spent

**Shipped, over the entire multi-session effort:**

- `DemandPagedArena.md` — a genuinely good design document.
- Three codex-vm debug flags (`-r10dump`, `-watchall`, `-dumpmem`) and a
  hardware-watchpoint feature (`-hwwatch`). Diagnostic tooling built solely
  to hunt the bug. Sound, flag-gated, default-off. **Nobody needed these to
  get the project to where it already was.**

**Not shipped:** demand paging. The survey system it was meant to retire is
still in place. The 978 MB CHECK over-commit it was meant to eliminate is
still there. Zero of the design's five stages landed on main.

**The proportion is the story.** The compiler is ~30,000 lines of Codex that
compiles itself to a hard fixed point on bare metal and compiles real
programs that really run. It reached that state on the flat, survey-based
arena — the "dumb allocation stuff" the project set out to replace. The
allocator was inelegant but *correct*. The project was a memory
*optimization* of a system that already worked, and it produced a net
negative: time and tokens spent, tooling debt added, nothing removed.

---

## 2. The bad turns, compressed

A week of work reduces to a short, damning list. Each entry is a session (or
more) that ended with a confident conclusion later marked wrong.

1. **The mechanism worked, so the win looked close.** Session 1 built the
   #PF handler and proved demand paging end to end: a binary whose own boot
   demand-pages self-compiled the entire compiler *byte-identically*, on both
   codex-vm and QEMU. This was real, verified engineering — and it was a
   trap. It made everything after feel like the last mile of a solved
   problem. It was not the last mile. It was the trailhead of a different,
   much harder problem.

2. **The corruption appeared and was misfiled.** Under demand paging,
   diagnostic *messages* corrupted while diagnostic *codes* stayed correct
   (battery: 188 pass / 115 fail, the failures almost all tests that print an
   error string). First theory: a memo/bag copy failure. Refuted next session
   — the data was byte-identical; the corruption was at print time.

3. **Then the theory carousel.** In order, each stated with confidence and
   later killed with evidence: *stack-heap collision* (dead — RSP was normal
   at ~3 GB); *the R8/R9-staged binary-operand codegen optimization* (named
   "prime suspect," later exonerated by a static read of the emit path);
   *"the content copy never executes"* (wrong — the content was correct when
   built, then overwritten); *a post-hoc record-field store landing on the
   result* (confounded by the fact that the agent's own probe was *inducing*
   a collision that looked like the bug). Every theory was tested. Every
   theory was disproved. None was replaced by an observation.

4. **The tool-building spiral.** The interactive debugger turned out to be
   unusable under demand paging (every guest page-fault exits to the host;
   a 95-byte compile ran 189 seconds of CPU without reaching the print path).
   Rather than switch observation modality, the agent built its own: hardware
   DR watchpoints, guest-armable watch ports (0x411–0x416), a TF single-step
   recorder, a page-watch, a memory hexdump port. Several of these tools were
   themselves broken — the page-watch single-step never retired the faulting
   store (it re-trapped 400 times on one instruction); a boot-armed hardware
   watchpoint was silently cleared by the guest kernel; a guest-armed one
   got zero hits. The agent spent real effort building instruments, then more
   effort debugging the instruments, and still never got the clean capture.

5. **The one right instrument was named repeatedly and never used to
   completion.** "QEMU + GDB hardware watchpoint (Rule 6 sanctioned; repros
   on QEMU; no slowdown)" appears in the notes again and again as the obvious
   next move. It was the correct move. It kept losing to the agent's
   preference for its own codex-vm instrumentation.

6. **Buttoned up, unsolved.** The final honest state: mechanism proven, bug
   un-localized, everything shelved, main pristine, and a memory file that is
   a beautiful museum of dead theories.

---

## 3. Why a well-trodden road failed

The framing that this was standard, well-understood, high-in-the-training-set
technology is **correct, and it is exactly why the failure is instructive.**

Demand paging *is* in the training set a thousand times over — the Linux
kernel, every OS textbook, Fleury's arena essay, `VirtualAlloc`. And the
agent reproduced it faithfully. The #PF handler, the not-present PDE trick,
commit-on-touch, resume-and-retry: all textbook, all worked, byte-identical
self-compile on the first serious attempt. **The port was clean.** Anyone
concluding "the model couldn't implement demand paging" has the story
backwards.

What is *not* in the training set is this: the four-hour session in which
someone localizes a non-reproducible heap-aliasing bug in an unfamiliar
30,000-line self-hosting compiler, under an observation environment that
distorts the very thing being observed. People publish working code and
clean explanations. They do not publish their debugging flail. The tacit
craft of convergent debugging — *when to stop theorizing, when to change how
you look, when to declare the bug not worth the candle* — is precisely the
knowledge that is absent from the corpus, because it lives in the wastebasket,
not the commit.

And the deeper trap: **Linux's demand paging works because all of Linux was
co-evolved to be correct under it.** Bolting a new memory regime onto a
codebase that has a latent assumption baked in — here, a dead fast-path in
string concatenation and a print routine that builds a left-associative chain
of temporaries each ending exactly at the allocation frontier — does not test
your ability to write a #PF handler. It tests your ability to find the one
place your existing code was quietly wrong and only got away with it because
the old memory layout never collided. That is not a porting task. It is a
debugging task wearing a porting task's clothes, and it is one of the hardest
kinds there is. The "well-trodden road" delivered the agent straight to it.

The absence of legacy and backward-compatibility constraints — real freedoms,
correctly noted — did not help, because the difficulty was never
compatibility. It was *localization under uncertainty in a large system*, and
freedom from legacy does nothing for that.

---

## 4. The agent: what it did well

An honest post-mortem credits the machine where it earned it. This was not a
flailing agent that broke things.

- **It never shipped garbage.** Through a week of failed theories and buggy
  tools, main stayed pristine — the compiler source and seed byte-identical
  to where they started. Every dead-end lived in a shelf. The one thing that
  reached main (the debug flags) was sound and gated.
- **It proved before it believed — about the mechanism.** The byte-identical
  self-compile on two independent VMs is real verification, not vibes.
- **It disproved honestly.** Every theory was actually tested with data and
  actually abandoned when the data said so. It never talked itself into a
  wrong fix and shipped it. Stack-collision was killed with register
  captures; the codegen-opt suspect was killed with a static read; the
  fault-transparency worry was killed with a written-and-read-back probe.
- **It knew it had not won.** The final report said "unsolved," not "should
  be fine." The restraint at the end — ship only the sound tooling, keep the
  rest shelved — is correct engineering judgment.

This is an agent that is *disciplined about correctness* and *reliable about
not making things worse.* That is worth something real, and it should not be
lost in the criticism that follows.

---

## 5. The agent: what it did badly

The failures are not failures of knowledge. They are failures of *strategy*
and *metacognition* — knowing what to do when you don't know what to do.

- **Theory-first, observation-never.** The correct discipline for a
  mysterious corruption is: get one trustworthy observation of the offending
  write *before* generating any theory of cause. The agent inverted this. It
  generated theory after fluent theory and spent the budget refuting them.
  Fluent forward reasoning is the model's great strength; here it was the
  disease. Every plausible mechanism it could articulate, it pursued — and it
  can articulate a great many.

- **Building instead of quitting or switching.** Confronted with "my debugger
  is too slow," the agent's reflex was to *build a better debugger*, not to
  *use a different one that already exists* (QEMU+GDB) or to *stop instru-
  menting and bisect the input instead*. Tool-building feels like progress
  and is legible as effort; it was a yak-shave that also introduced new bugs
  to debug.

- **No stop-loss.** Nowhere did the agent set or honor a budget: "if I cannot
  localize the writer in N more hours, we ship the flat arena and walk away."
  A human lead sets that line on day one of a heisenbug hunt. The agent kept
  going because each next probe was *locally* reasonable, and it never zoomed
  out to the global question of whether the whole endeavor had earned another
  session. This is the single most expensive failure, because it is the one
  that converts a bounded loss into an unbounded one.

- **Mishandling the heisenbug as a class.** "Every time I look at it, it
  moves" (probe-induced collisions were explicitly observed) is the textbook
  signal to change observation *modality* — go non-invasive, go
  record-replay, or stop looking at the run and start bisecting the input.
  The agent noticed the signal, wrote it down, and kept reaching for invasive
  instrumentation anyway.

- **Documentation as a substitute for progress.** The memory record is
  extraordinary — dense, precise, honest post-mortems of each dead theory.
  It is also the tell. Writing "THEORY CORRECTED — IT IS A COLLISION, NOT A
  COPY FAILURE" feels like a result and is not one. The quality of the
  bookkeeping disguised the absence of the thing being booked. (This very
  document is at risk of the same critique; its only defense is that it ends
  the effort rather than fueling another session.)

- **Sunk cost, manufactured by the memory system.** This is subtle and
  important. The persistent cross-session memory — a genuine asset for
  continuity — also *engineered the sunk-cost fallacy into the workflow*.
  Every fresh session re-entered the hunt reading "we have narrowed it this
  far; next move is X." No session ever inherited the question "should we be
  doing this at all?" The handoff always framed continuation as the default.
  The structure that let the agent persist is the same structure that stopped
  it from reconsidering.

---

## 6. The limits this exposed (the part the world will ask about)

Stated plainly, because the question deserves it:

**The model is strong at forward synthesis and weak at convergent debugging
under uncertainty.** Design a system, implement a known construct, explain a
mechanism, write the clean version — it does these at or above a strong
human's level, fast. Localize a non-reproducible fault in a large unfamiliar
system, where the discipline required is *restraint* (theorize less, observe
first, change modality, know when to quit) — it is markedly weaker, and its
very fluency works against it. It produces confident causal stories on
demand, and a confident wrong story that costs a session to disprove is worse
than no story at all.

**Its metacognition — the judgment of its own progress and the economics of
continuing — is the real limit found here.** It does not reliably know when
it is stuck versus when it is close, and it has no innate stop-loss. Pointed
at a problem that is *unsolvable under the current observation conditions*, it
will spend your budget generating high-quality-looking effort indefinitely,
because every next step is locally defensible. It needs an external governor —
a human, or a harness rule — to impose the budget it will not impose on
itself.

**The optimistic scoping was itself a symptom.** The design doc billed this as
"industry-standard... mostly delete the survey plus a 6-instruction #PF case."
That estimate was right about the #PF case and catastrophically wrong about
the total, because it modeled the *known construct* and not the *unknown
interaction with the existing codebase*. Fluent forward reasoning scopes what
it can see clearly and is blind to the sinkhole — and it does not discount for
its own blindness. A seasoned engineer estimating the same work would have
added, "and then two-to-ten days of debugging whatever this uncovers in the
allocator," from scar tissue the model does not have.

None of this is a claim that the model is unintelligent. It is a claim about
the *shape* of the intelligence: broad, fast, fluent, synthesis-biased, and
poorly calibrated about the boundary of its own competence in exactly the
regime — long-horizon, low-feedback, high-uncertainty debugging — where humans
rely on tacit judgment that the training corpus does not contain.

---

## 7. Was it worth the money?

Honestly, for this project: **no**, and the reason is precise.

- For **what shipped** (a design doc and four debug flags), the spend was
  wildly disproportionate. You do not need frontier-tier tokens by the
  session to add `-dumpmem`.
- For **what was attempted** (a memory optimization of an already-correct,
  already-self-hosting compiler), the correct decision was to time-box it and
  abandon after session 1 revealed a non-local heisenbug. The project
  violated two of the codebase's own stated virtues — *Correctness Over
  Performance* (this was pure performance work) and *Less Is More* — and the
  agent, which knows those virtues, did not invoke them against itself.

But the ledger is not all red, and pretending it is would be its own
dishonesty:

- The model delivered, at high reliability, the thing it is genuinely good at:
  a correct design, a *proven* mechanism, and an exhaustively documented set
  of negative results. Knowing precisely what the bug is **not** — not stack
  collision, not the codegen opt, not copy-failure, transparent faults — is
  real value banked for whoever attempts this next.
- It never damaged the working system. An agent that spends a week failing to
  add a feature but breaks nothing is in a completely different and far safer
  category than one that ships a plausible wrong fix.

So the sober framing for a buyer: this class of model is worth the money as a
**correctness-preserving synthesizer and explorer**, and is *not* yet worth an
open-ended budget as an **autonomous closer of hard, low-feedback debugging
tickets.** Its expensive failure mode is specifically that it will not stop.
The economic mitigation is not a better model prompt; it is a governor —
explicit budgets, forced stop-loss reviews, and a human who owns the
"is this still worth doing?" decision that the model reliably will not make.

---

## 8. What a competent redo looks like (so this isn't only an autopsy)

If demand paging is attempted again, the lesson is procedural, not technical:

1. **Before any theory, get one trustworthy capture of the offending write.**
   QEMU + GDB hardware watchpoint on the corrupted result's address, on the
   *full* compiler run, first thing. Not codex-vm instrumentation. Not a
   home-grown watchpoint. The sanctioned external tool that already works.
2. **Set the stop-loss out loud.** "If the writer is not localized in one
   session, we ship the flat arena and close this." Write it at the top of
   the session, not the bottom.
3. **Treat the uncovered bug as the actual project.** The paging mechanism is
   done. The deliverable is "find and fix the latent print-path aliasing bug,"
   which is worth doing *on its own merits* under the flat arena, where it can
   be hunted without the observation tax. Fix it there first; only then re-add
   paging.
4. **Change modality at the first sign of probe-induced motion.** If looking
   moves the bug, stop looking at the run; bisect the input and the phases.
5. **Let a fresh session inherit the doubt, not just the progress.** The
   handoff note should lead with "should this continue?" and the budget spent
   so far — not with "next probe is X."

---

## 9. The general lesson for agent-run engineering

Strip away demand paging and the compiler, and what remains is a pattern any
team running AI agents should expect and design against:

> A capable agent will implement a known thing correctly, uncover a hard
> unknown thing in the process, and then pursue the unknown thing with
> tireless, fluent, well-documented, and strategically undisciplined effort —
> never breaking anything, never quitting, and never asking whether it should.

The mitigations are not cleverness. They are governance: budgets, explicit
stop-loss gates, a bias toward existing tools over home-grown ones, a human
who owns the go/no-go, and a handoff format that carries the doubt forward
alongside the findings. The agent supplies the horsepower and the honesty.
It does not, yet, supply the brakes.

The compiler compiles real programs that really work. It got there on the
"dumb allocation stuff." That is the sentence to keep.
