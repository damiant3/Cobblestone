# The Verifier

**Date**: 2026-05-01
**Status**: Design
**Depends on**: CDX binary format (done CL 542), Crypto primitives
(done CL 541), Capability system (done CLs 539-580), Type system (done)
**Unblocks**: Everything in Codex.OS. This is Face 2.

---

## The Problem

Every "software cannot hurt you" claim in the Codex vision depends on
a single program that type-checks untrusted code at install time. Not
sandboxed -- verified. A bug in the verifier IS a zero-day. Every other
design implicitly assumes it exists and works.

The verifier reads a CDX binary and proves four invariants before the
binary is allowed to execute:

1. **It only uses capabilities it was granted.** The binary's capability
   table is checked against the effective policy for the installing
   user/agent. No capability in the table → no corresponding syscall at
   runtime.

2. **It cannot access memory it doesn't own.** The type system's linear
   types and lack of raw pointers guarantee this. The verifier confirms
   type soundness of the binary's declarations.

3. **It cannot corrupt another program's state.** Process isolation
   (page tables, Ring 2) enforces hardware boundaries. The verifier
   confirms the binary does not request capabilities that would bypass
   these boundaries.

4. **It terminates within its fuel limit.** The verifier confirms that
   the binary's computation has a bounded budget. Non-terminating
   programs are rejected unless they explicitly declare `[Diverge]`
   and the policy permits it.

These four invariants eliminate: buffer overflows, use-after-free,
ransomware, privilege escalation, remote code execution, data races,
supply chain attacks. The attack surface collapses to the verifier
itself.

---

## What the Verifier Reads

A CDX binary (format in `CodexBinary.md`). The fixed header provides:

| Field | Offset | What the verifier does with it |
|-------|--------|-------------------------------|
| `content_hash` | 0x08 | Recompute SHA-256 of payload, compare |
| `author_key` | 0x28 | Look up in trust lattice |
| `signature` | 0x48 | Ed25519 verify over content_hash |
| `capabilities_offset/size` | 0x88 | Parse capability table, check against policy |
| `proofs_offset/size` | 0x98 | Optional: retrieve and verify proof facts |
| `trust_threshold` | 0xD8 | Compare against author's trust score |
| `fact_hashes` | 0xE0+ | Verify dependencies are in the local fact store |

---

## Verification Sequence

The verifier runs five phases in order. Each phase can reject the
binary. If any phase rejects, the binary is not loaded.

### Phase 1: Integrity

Recompute SHA-256 of everything from `capabilities_offset` to end of
file. Compare against `content_hash` in the header. Reject on mismatch.

Cost: O(n) in binary size. One pass. No trust decisions.

### Phase 2: Author

Look up `author_key` in the trust lattice. Compute the author's trust
score. If score < `trust_threshold`, reject.

Verify Ed25519 signature over `content_hash`. If invalid, reject.

Cost: O(1) signature verification + trust lattice lookup.

### Phase 3: Capabilities

Parse the capability table. For each entry:

1. Look up `capability_id` in the capability registry.
2. Check `direction` (Read/Write/ReadWrite) against the effective
   policy for the installing user.
3. Check `scope` against the policy's scope constraints.
4. Check `max_duration` against the policy's time-boxing limits.

If any requested capability is denied by the policy, reject.

The effective policy is composed from the trust lattice: device vendor
→ regulator → parent → school → self. Any layer can deny a capability.
The most restrictive layer wins.

Cost: O(c) where c = number of capability entries.

### Phase 4: Type Checking (the hard part)

Re-type-check the binary's declarations against its capability table.
This is where the four invariants are actually proven.

**What gets checked:**
- Every function's declared effects are a subset of the granted
  capabilities (CDX4001).
- Every effect operation respects direction (CDX4002) and scope.
- Linear resources are consumed exactly once.
- Pattern matches are exhaustive.
- No unbounded recursion without `[Diverge]` in the effect list.

**Decidability constraint:** The verifier must terminate on all inputs,
including adversarial ones. This means the type checker must operate on
a **decidable fragment** of the type system:

- **Hindley-Milner core**: Type inference with let-polymorphism is
  decidable. This covers the bulk of Codex programs.
- **Bounded quantification**: `Integer between L and H` is decidable
  (range check).
- **Effect tracking**: Effect row unification is decidable (linear
  scan of effect lists).
- **Linear types**: Linearity checking is decidable (count uses per
  binding).
- **Dependent types**: NOT generally decidable. Proof terms
  (`claim`/`proof` blocks) require normalization, which can diverge.
  The verifier uses a **fuel limit** on normalization.

**Fuel model:** The verifier allocates a fixed computation budget
(measured in reduction steps) for type-checking the binary. If the
budget is exhausted before type-checking completes, the binary is
rejected with a "verification timeout" error.

The fuel budget scales with binary size: `base_fuel + (text_size *
fuel_per_byte)`. The constants are compile-time parameters of the
verifier. A program that exhausts the budget is either pathologically
complex or adversarial -- either way, rejection is correct.

Proof normalization consumes fuel from the same budget. A binary that
carries complex proofs must request a proportionally larger budget via
its `proof_size` field, which the policy can cap.

**What is NOT re-checked:** The verifier trusts the compiler's code
generation. It does not disassemble or simulate the binary's machine
code. The invariant is: if the type system accepts the declarations,
and the compiler is a verified fixed point (MM4), then the emitted
code is correct. The verifier checks the declarations; the compiler
guarantees the translation.

Cost: O(n · log n) in program size for the HM core. O(n · f) where
f = fuel per proof term. Bounded by the fuel limit.

### Phase 5: Proofs (optional)

If the binary carries proof hashes and the policy requires proof
verification:

1. For each proof hash, retrieve the proof fact from the local fact
   store (or a trusted peer).
2. Normalize the proof term and check that it inhabits its claimed
   type.
3. If any proof is missing or invalid, reject.

Proof kinds (from the CDX header):
- `Termination` (0): Proof that the program terminates.
- `MemorySafety` (1): Proof that the program cannot corrupt memory.
- `CapabilityCompliance` (2): Proof that the program's runtime
  behavior matches its declared capabilities.
- `Custom` (3): Application-specific proofs.

The fast path (Phase 2 author trust is sufficient) skips this phase.
The paranoid path (safety-critical binaries like the verifier itself)
requires all proofs.

Cost: O(p · f) where p = number of proofs, f = fuel per proof.

---

## The Trusted Core

The verifier is the smallest critical program in Codex.OS. Its own
correctness is the foundation of the security model. To minimize the
attack surface:

**The verifier's own trusted computing base:**

1. The verifier's source code (Codex).
2. The Codex compiler (a verified fixed point).
3. The kernel's process isolation (page tables, Ring 2).
4. The crypto primitives (SHA-256, Ed25519).
5. The hardware (x86-64 ISA, memory controller).

Items 1-4 are under Codex's control. Item 5 is not.

**Verifying the verifier:** The verifier is itself a CDX binary. It
is type-checked by itself (the bootstrap verifier, compiled by the
fixed-point compiler). The bootstrap verifier carries `Termination`
and `MemorySafety` proofs. The paranoid path is mandatory for the
verifier itself.

This is circular but well-founded: the verifier is compiled by the
Codex compiler (fixed point proven), type-checked by the previous
version of itself, and its proofs are checked by the proof checker
(which is part of the verifier). The boot sequence loads the verifier
first, using a hardcoded trust anchor (the device's root key).

---

## What the Verifier Does NOT Do

- **Disassemble or simulate machine code.** The verifier trusts the
  compiler. It checks types, not instructions.
- **Monitor runtime behavior.** Verification is at install time. The
  capability system and hardware isolation enforce at runtime.
- **Replace the type system.** The verifier re-runs the type checker.
  It is not a separate analysis -- it is the same analysis, on untrusted
  input, with a fuel limit.
- **Handle network I/O.** The verifier reads a CDX binary from memory.
  How the binary arrived (network, USB, repository) is not its concern.
- **Manage the trust lattice.** The verifier queries the lattice; it
  does not maintain it.

---

## Implementation Plan

### Step 1: Extract the type checker into a verifiable module

The existing type checker (`Codex.Codex/Types/TypeChecker.codex`)
operates on AST nodes from the parser. The verifier needs to
type-check from CDX binary declarations, not from source text. This
requires:

- A CDX declaration reader that reconstructs type information from
  the binary's capability table and type metadata section.
- A standalone type-check entry point that accepts declarations (not
  AST) and a capability policy (not hardcoded grants).

The type checker's core logic is unchanged. The interface changes.

### Step 2: Add fuel-limited normalization

The proof checker (`claim`/`proof` syntax) currently runs without a
fuel limit. Add a reduction step counter:

- Each beta reduction, case split, or induction step consumes one
  unit of fuel.
- When fuel reaches zero, normalization halts and reports "fuel
  exhausted."
- The fuel budget is passed as a parameter, not hardcoded.

### Step 3: CDX verification entry point

Write the five-phase verification sequence as a Codex function:

```
verify-cdx : Bytes -> Policy -> VerifyResult
verify-cdx (binary) (policy) =
  let header = parse-cdx-header binary
  in let integrity = verify-integrity header binary
  in let author = verify-author header trust-lattice
  in let caps = verify-capabilities header policy
  in let types = verify-types header fuel-budget
  in let proofs = verify-proofs header fact-store fuel-budget
  in compose-results [integrity, author, caps, types, proofs]
```

This function is the verifier. It is pure (no effects except reading
the binary and querying the trust lattice / fact store). It returns a
result, not a side effect.

### Step 4: Bootstrap and self-verification

Compile the verifier with the fixed-point compiler. Run the verifier
on its own CDX binary. This is the bootstrap proof: the verifier
accepts itself.

### Step 5: Integrate with the kernel loader

The kernel's program loader calls `verify-cdx` before mapping the
binary into a process's address space. If verification fails, the
binary is not loaded and the loader returns an error to the requesting
agent.

---

## Sequencing

| Step | What | Effort | Blocks On |
|------|------|--------|-----------|
| 1 | Type checker extraction | Medium | Nothing |
| 2 | Fuel-limited normalization | Small | Nothing |
| 3 | CDX verification entry point | Medium | Steps 1, 2 |
| 4 | Bootstrap self-verification | Small | Step 3 |
| 5 | Kernel loader integration | Medium | Step 4 + kernel scheduler |

Steps 1 and 2 are independent of each other. Step 3 composes them.

---

## Open Questions

1. **Type metadata in CDX binaries.** The current CDX format carries
   capability tables but not full type signatures. The verifier needs
   type information to re-check. Options: (a) embed type signatures in
   the binary, (b) carry a hash of the type environment and retrieve it
   from the fact store, (c) re-derive types from the capability table
   alone. Option (a) is simplest but increases binary size.

2. **Fuel constants.** What are the right values for `base_fuel` and
   `fuel_per_byte`? Too low = legitimate programs rejected. Too high =
   adversarial programs can waste CPU. Needs empirical tuning on real
   programs (starting with the compiler itself).

3. **Proof retrieval.** Phase 5 needs access to the fact store. On a
   freshly booted system with no network, the fact store may be empty.
   Should the verifier require proofs to be bundled in the binary? Or
   is author trust sufficient for offline boot?

4. **Effect polymorphism.** A library binary may declare effect
   variables (`forall e. [e] a -> [e] b`). The verifier must check
   that instantiations of these variables respect the policy. This
   requires tracking effect variable bindings through the type checker.

5. **Incremental verification.** If a binary's dependencies haven't
   changed (fact hashes match), can the verifier cache its result? A
   verification cache keyed by content_hash would make re-verification
   O(1) for unchanged binaries.

---

## Background reading

- **Versioned E-Graphs** (Cesario, Zakhour, Weisenburger, Salvaneschi --
  Univ. St. Gallen, *Proc. ACM Program. Lang.* PLDI 2026, doi
  10.1145/3808249). Local copy:
  `docs/Reference/2026-Cesario-Zakhour-Weisenburger-Salvaneschi-Versioned-EGraphs-PLDI.pdf`.
  E-graph variant that maintains a hierarchy of equivalence relations
  on a shared term space, with version-labeled union-find edges and
  a "closest valid parent" walk that keeps `find` O(1) amortized.
  Reference data structure for proof checking that branches over
  alternative equality contexts (case-split, induction unfolds): every
  branch shares the term space, only branch-specific equalities are
  tagged. Prior approaches either clone the full e-graph per branch
  (memory blow-up) or share at the cost of dropping e-class analysis;
  versioned e-graphs do both. Implementation `Veg` in Rust, modeled on
  `egg`. Reported 25–70 MiB vs 1.5–3.5 GiB on synthetic sweeps with
  ~1000× run-time advantage over the prior best (Easter Egg).

  Relevance: Phase 5 (Proofs) normalizes proof terms with case-split
  and induction as fuel units (line 248). If the verifier's proof
  checker ends up exploring alternative equality contexts during
  normalization (proof-by-cases over a finite alphabet, induction
  hypothesis instantiation, etc.), versioned e-graphs are the
  candidate primitive. Out of scope until the proof checker is
  written; this is forward-looking citation, not a requirement.
