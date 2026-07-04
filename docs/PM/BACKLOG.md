# Backlog — Outstanding Work

**Updated**: 2026-07-02

Open work only. Completed items are not archived here — the authoritative
record of a closure is its changelist description in Perforce.

## Active — Ongoing

### Fulfill the Vision Check (highest priority — no more surprises)

| # | Item | Notes |
|---|------|-------|
| 1 | **Adversarially verify every BY-CONSTRUCTION claim actually holds** | LINEAR TYPES LEG COMPLETE 2026-07-03 (blu): probed (CL 6819, nine adversarial probes) and the LinearOwnership campaign shipped same day, stages 2-4 (CLs 6856, 6868, 6883): let-local aliasing is a tracked ownership move; argument boundaries admit linears only through linear-declared parameters (CDX2065, freeze is the door by its own signature); bare linear returns demand a linear return type (CDX2066); let-bound capturing closures are call-once; container stashes make the container the owner; handler-clause and escaping-closure capture rejected (CDX2067). ALL NINE routes closed, catalog green both directions, plus positive guards. As-built record + residual edges: `docs/Designs/Compiler/Active/LinearOwnership.md`. CAPABILITIES + PUNCTUAL LEGS PROBED (stage 0) 2026-07-03: seven adversarial probes, all compile clean, gaps documented. Punctual (`docs/Designs/Compiler/Active/PunctualProbe.md`): four of the five checks are AST walks covering only 5 node shapes, so unary/act-block/computed-head calls launder, CDX6004 blocklists 3 effects by name, and the safe-builtin allowlist admits show/list-length. Capabilities (`docs/Designs/Compiler/Active/CapabilityProbe.md`): manifest hardcoded empty, raw I/O intrinsics typed pure (a pure function does unmediated port/memory/block I/O), boot grants all caps - "no undeclared I/O" holds for library wrappers, fails for the intrinsics beneath. Probes pinned as `punctual-launder-*` / `cap-launder-*`. Effects were closed by EffectRows; bounded integers hardened by BoundedSignatures. ALL FIVE LEGS NOW PROBED; linear fully enforced, the other four scoped honestly in ClaimsCalibration. Fix campaigns (punctual coverage unification; capability effect-rows-on-intrinsics + manifest wiring) are the follow-on work. Original motivation: the effect-laundering hole (CL 6494) showed a headline claim -- `KingsAndCourts.md`: "a compromised library cannot silently exfiltrate data because the effect would not type-check" -- was presented as a *present mechanism* and was simply false; nobody had probed it. This is distinct from `docs/Designs/Compiler/Active/ClaimsCalibration.md`, which checks whether a claim is honestly *labeled* aspirational-vs-done. This item is the *verification* pass: for each by-construction safety claim (linear types prevent use-after-free/double-free, bounded integers prevent overflow, effect types prevent undeclared I/O, capability model prevents unauthorized access, punctual bounds execution), write an adversarial probe that *tries to break it* -- pass the effectful function to the generic HOF, alias the linear resource through a data structure, overflow the bounded int through arithmetic the prover can't see, call the unauthorized effect through an indirection -- and confirm the compiler rejects it. Every probe that compiles clean is a gap to scope and file (like effect rows). Findings and residual-trust position go in ClaimsCalibration.md / TrustedComputingBase.md. Motivation: surface the gaps by construction-test now, not when a claim is quoted back to us. |

### USB Install (Gap 4)

| # | Item | Notes |
|---|------|-------|
| 1 | **End-to-end USB validation** | All driver/integration layers done (MSC, DriveManager, DevConsole, XHCI). Needs a physical USB stick test on Asus + Dell. RAM is 8GB with an MMIO-hole split; real hardware still needs a page-fault skip for the unmapped hole (deferred). |

### Compiler — Effect System

| # | Item | Notes |
|---|------|-------|
| 1 | **Full effect-row subtyping** | The argument-boundary effect check (CL 6494) closes the demonstrated laundering, but it is effect-subset checking on arguments, not a full effect system. Still open: a generic (type-variable) parameter carries no effect constraint, so an effectful function passed to a `map`-style `(a -> b)` is not caught; contravariant parameter positions are left lenient; `unify-at` strips `EffectfulTy` rather than enforcing subset. A complete fix adds effect variables and threads expected/actual polarity through all unify sites, so the check lives in unification instead of at the application boundary. |

### SMP — Cross-Architecture

| # | Item | Notes |
|---|------|-------|
| 1 | **SMP atomics + boot on ARM64 / RISC-V** | The full SMP stack (atomics, per-core bootstrap/scheduler/heap, IPI, lock-free channels) is complete for x86-64. The atomic builtins and the per-core boot path are not yet ported to the ARM64 and RISC-V backends. |

### GPU Compute

| # | Item | Notes |
|---|------|-------|
| 1 | **Dual-target GPU: libdevice path (K9)** | PTX + SPIR-V dual-target compilation is built and passing (K0-K8): foreword.gpu quire, Device/Gpu effects, both plugs, verifier integration, CUDA Driver API dispatch. K9 (libdevice linking for transcendental math) remains, deferred by design. |
