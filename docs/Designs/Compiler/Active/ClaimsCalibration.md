# Claims Calibration — WIP Gap Register

**Status:** Living gap register. 2026-06-30.

Codex is a work in progress with no release and no promises made to anyone.
So this is **not** a pre-release scrub. The operating rule:

- **Aspirational claims are fine in aspirational / marketing docs.** The
  vision is allowed to describe the destination.
- **Plans are WIP.** A plan may state its goal in the present tense; it is
  understood as the target, not a shipped fact.
- **Partial implementations are "pending completion,"** not "done."
- **The only thing actually wrong is a present-tense *factual mislabel*** —
  calling X a thing it is not. Those get fixed.

What matters is whether the **goal and the path** hold up, not our current
position on the path. This register tracks claims a rigorous reader would
challenge, classified by which of the above buckets they fall in.

Companion: `TrustedComputingBase.md` (what we trust vs. check vs. prove).

---

## Fixed (factual mislabel)

- **"punctual WCET proofs" → "punctual instruction-count bounds."**
  `punctual` reports an architecture-independent instruction count; the
  DevelopersGuide already says the compiler does not claim wall-clock time.
  Calling the count a "proof" was just inaccurate and muddied the one place
  "proof" means something real (the propositional-equality layer). Fixed in
  `KingsAndCourts.md` (CRA 1(d) row). The `[HardRealtime]` design doc may
  use the same loose wording for a *planned* timed-effect WCET system —
  that's a plan, so it reads as the goal; tighten only if/when it ships.

## Fine as-is (aspirational / marketing — leave)

- **CLAUDE.md mission: "intended to be impervious to all known attack
  vectors by-design."** Already hedged with *intended to be*. It is a goal
  statement, which is exactly what it should be.
- **KingsAndCourts CRA table: "No exploitable vulnerabilities …
  BY-CONSTRUCTION," "evidence is the fact that the code compiled."** This is
  the regulated-market vision doc. As a destination it is the right
  ambition. A reader who wants the honest current footing is one click away
  in `TrustedComputingBase.md`, which states the residual trust assumptions
  (type-checker soundness, codegen fidelity, the C hypervisor in the build
  TCB). Vision states the aim; TCB states the position. Both true, kept
  side by side.
- **CodexIoTPlan: "the compiler proves the shipped binary is free of
  memory-safety bugs."** This lives in the IoT *strategic plan* — a WIP
  planning doc. Read as the target of the path, it is fine. The path to
  earning it (translation validation, verified codegen) is roadmap, not
  claim.

## The real question this register serves

Not "is our current position defensible to a hostile auditor" — we are not
auditing a release. It is: **does the path from where we are to those
aspirational claims actually close the gap?** That is tracked in
`TrustedComputingBase.md` §5 (the shrink roadmap) and in the proof-layer
work (`Induction.md`). If the path closes, the aspirational docs become
plain fact over time; if it doesn't, the gap shows up here first.
