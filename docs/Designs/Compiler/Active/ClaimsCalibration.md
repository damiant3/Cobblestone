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

- **"invalid proofs are type errors" → made TRUE in code, same day
  (2026-07-04, blu).** Stage-0 probes showed the proof CONTENT checks
  were real (wrong equations, unsound induction steps, false literal
  equalities all reject CDX2001) but **totality was not checked**: a
  circular proof term inhabited any proposition, silently, through
  six routes — self-referential def (`bad = bad`), mutual defs,
  generally-recursive prop-returning helper, `claim/proof/qed`
  self-reference, an induction step citing the claim being proven as
  a lemma (accepted as CHECKED, not CDX4022-unproven), and two
  induction proofs citing each other. Ruling: fix the code to match
  the goal instead of hedging the README. Fixed the same day:
  `check-proof-cycles` rejects any proof definition that reaches
  itself through the proof reference graph (CDX4023 CircularProof);
  relevance is judged on deep-resolved checked types so undeclared
  intermediaries are in the graph; the mention walk gained the
  missing `AInductionExpr` arm (which also extends punctual's cycle
  coverage). All six probes now live in `errors/*.failing`;
  `proof-assume-axiom` pins the CDX4021 axiom warning (the explicit
  `assume` door was already honest). Residual edges logged in
  `docs/Designs/Language/Active/ProofTotalityProbe.md` §6.2
  (cross-chapter DefMap if it ever ships; Option B positive grammar
  as optional hardening). The README sentence stands unqualified
  because the compiler now enforces it.

- **"A linear value must be used exactly once on every path" →
  scope-qualified (2026-07-03).** The DevelopersGuide stated the
  linearity guarantee in the present tense with no scope. The stage-0
  adversarial probes (blu, nine probes, all compile clean and execute
  the violation at runtime) show enforcement is *param-mention-only*:
  a declared `linear` parameter's direct mentions are counted; every
  indirection — let-local alias, closure capture, partial
  application, list/record stash, non-linear callee boundary,
  plain-typed return, mutable let-alias — launders the discipline
  silently. This is the linear-types analog of the effect-laundering
  hole (CL 6494), found the same way. The TypeChecker's own prose
  already admitted "tracking ownership through arbitrary let-bound
  locals is later work"; the guide now carries the same honesty
  (Current enforcement scope note). Probes:
  `codex/test/linear-launder-*.codex`, `mutable-launder-alias.codex`
  (each flipped to `.failing` as enforcement landed — the campaign
  completed 2026-07-03, stages 2-4: let-local aliasing is a tracked
  move, argument boundaries admit linears only through
  linear-declared parameters (CDX2065), a bare linear return demands
  a linear return type (CDX2066), let-bound capturing closures are
  call-once, container stashes make the container the owner, and
  handler-clause / escaping-closure capture is rejected outright
  (CDX2067). ALL NINE probes now live in errors/ and the catalog is
  green in both directions). Fix campaign (as-built record):
  `docs/Designs/Compiler/Active/LinearOwnership.md`. The
  KingsAndCourts / CodexIoTPlan "linear types prevent UAF/leaks"
  rows: the by-construction evidence class is now earned at the
  probe-catalog level. Residual honest edges, named in the design
  doc's stage 4 notes (locals minted from linear-returning calls;
  container literals in argument/tail position), keep this from
  being an unqualified totality claim — quote it with the catalog,
  not without it.

- **"punctual WCET proofs" → "punctual instruction-count bounds."**
  `punctual` reports an architecture-independent instruction count; the
  DevelopersGuide already says the compiler does not claim wall-clock time.
  Calling the count a "proof" was just inaccurate and muddied the one place
  "proof" means something real (the propositional-equality layer). Fixed in
  `KingsAndCourts.md` (CRA 1(d) row). The `[HardRealtime]` design doc may
  use the same loose wording for a *planned* timed-effect WCET system —
  that's a plan, so it reads as the goal; tighten only if/when it ships.

- **"`punctual` is proven to have bounded execution at compile time"
  → scope-qualified (2026-07-03, blu stage-0 probes).** The five
  structural checks (CDX6001-6005) are real, but four of the five
  (calls, heap, closures, bare I/O) are syntax-directed AST walks
  covering only {Apply, If, Let, Binary, Match arms}; anything outside
  that set launders. Four probes compile clean
  (`codex/test/punctual-launder-*`): a call through a computed head, a
  recursive call and a list literal under unary negation, a custom
  effect + lambda + recursive call inside an `act` block, and
  allocating/O(n) allowlisted builtins (`show`, `list-length`). The
  mutual-recursion cycle check (CDX6005) is the exception — it already
  has the exhaustive node coverage the other four need. Details +
  fix campaign: `docs/Designs/Compiler/Active/PunctualProbe.md`.
  Treat `punctual` today as enforced for the common shapes and a
  documented intention for the rest.

- **"signed, capability-scoped binaries ... no undeclared I/O ...
  rejected at load time" → scope-qualified (2026-07-03, blu stage-0
  probes).** The format, verifier (5 phases), policy engine, and
  kernel cap-bits all exist and are individually real, but the
  pipeline is not stitched together. The CDX capability manifest is
  hardcoded empty (`build-cdx` emits `cap-sz = 0`), so the verifier's
  capability and effect phases are vacuous on every compiler-produced
  binary; the raw I/O / memory / block intrinsics (`port-out-byte`,
  `poke-byte`, `block-write-sector`, ...) are typed with an EMPTY
  effect row, so a pure-signature function does unmediated hardware
  I/O; and boot grants process 0 every capability regardless of any
  manifest. Three probes compiled clean at stage 0
  (`codex/test/cap-launder-*`).
  CLOSED THROUGH STAGE 3 (blu CLs 6923/6946/6964/6999, 2026-07-03/04):
  `port-*` carry `Device.Port`, `block-*` carry `Device.Block`, the
  new `read-mmio`/`poke-mmio` carry `Device.Mmio` (heap `peek-*`/
  `poke-byte` intentionally pure by the 7.3 ruling); the enforced
  foreword io modules (Fat32, Gpt, GpuRender, InputSource, AppRunner)
  declare their effects; and the capability manifest is REAL — every
  binary carries a signed manifest derived from `opening`'s registered
  type (effects + covering capabilities, inside the hashed+signed
  content), with the verifier's cap/effect phases non-vacuous and the
  content-hash range fixed. All three `cap-launder-pure-*` probes are
  errors/.failing; `cap-manifest-derived` is the positive guard. The
  owned hardware stack (compiler/kernel/os/boards/plugs) is
  quire-exempt by ruling — a named TCB boundary (`CapabilityProbe.md`
  sec 7, recorded in `TrustedComputingBase.md`). STILL OPEN (stage 4,
  OS wiring, delegatable): boot still grants process 0 every
  capability and `granted-capabilities` is grant-all — the manifest is
  derived and verified but not yet ENFORCED by the loader/proc-table.
  Until stage 4 lands, "rejected at load time" remains the one
  unearned clause; say "carried and verified" instead. Details:
  `docs/Designs/Compiler/Active/CapabilityProbe.md` sec 7.5.

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
