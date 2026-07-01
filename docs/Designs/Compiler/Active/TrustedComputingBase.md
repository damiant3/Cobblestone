# The Codex Trusted Computing Base

**Status:** Position doc / living record. 2026-06-30.
**Purpose:** State plainly what Codex *trusts* versus what it *checks or
proves*, and chart the path to shrinking the trusted set. Written partly
for external scrutiny (a formal-methods reader will ask this within one
message) and partly because an honest TCB statement is a prerequisite for
every correctness claim the project makes.

> A Trusted Computing Base is the set of components that must be correct
> for the system's guarantees to hold. A bug *inside* the TCB can violate
> every guarantee silently; a bug *outside* it is caught by the TCB. The
> engineering goal is always the same: make the TCB small, explicit, and
> auditable. Codex's marketing claim ("if we didn't build it, we don't
> trust it") is only as good as this list is honest.

---

## 1. Two TCBs: toolchain and runtime

Codex has two distinct trusted bases, and conflating them overstates the
guarantee. Keep them separate.

- **Runtime TCB** — what must be correct for a *deployed* Codex binary to
  behave as specified, once it is running on bare metal.
- **Toolchain TCB** — what must be correct for the *build* of that binary
  (and of the seed itself) to be trustworthy.

The runtime TCB is genuinely small — that is Codex's real structural
advantage. The toolchain TCB is larger and is where the honest caveats
live.

---

## 2. Runtime TCB (a deployed Codex binary on bare metal)

A signed CDX running on real hardware (not under `codex-vm`) depends on:

| Component | Why it is trusted | Status |
|---|---|---|
| The hardware (CPU, MMU, devices) | Physics; outside our control | Trusted, irreducible |
| The emitted machine code | Produced by the seed's emitter; correctness rests on the toolchain TCB below | Trusted-via-toolchain |
| The boot trampoline + page tables | Hand-written machine code in `X86_64IO.codex` / `X86_64Boot.codex` | Trusted, small, audited |
| The runtime helpers (`__alloc`, `__str_concat`, deck/bivy) | Hand-written codegen primitives | Trusted, small, audited |

What is **not** in the runtime TCB, by construction — this is the win:

- **No OS, no libc, no dynamic linker, no shell.** The attack surface is
  exactly the code we emitted. (`KingsAndCourts.md`, ETSI 5.6.)
- **Memory-safety violations** that other stacks leave to runtime: linear
  types reject use-after-free/double-free at compile time; bounded
  integers reject silent overflow; effect types reject undeclared I/O.
  These are checked, not trusted.

So for the deployed artifact, the trusted set is: the hardware, the
hand-written boot/runtime stubs, and (transitively) the correctness of the
toolchain that emitted the code. The first two are small and auditable.
The third is Section 3.

---

## 3. Toolchain TCB (building the seed and everything it compiles)

| Component | Role | Trusted because | Shrinkable? |
|---|---|---|---|
| **`seed/Codex.cdx`** | The compiler. Root of trust. | It is a hard fixed point of itself (Section 4) | Not removable; can be *re-derived* by independent paths |
| **`codex-vm.exe` / `codex-vm.c`** | ~6000-line C/WHP hypervisor that boots the seed and carries the build over serial | Hand-written C; not Codex; not self-verified | **Yes** — pure-Codex VMX host (Backlog #4) removes it from the self-host path |
| **The build scripts** (`build/*.ps1`) | Concat, gate orchestration, signing invocation | PowerShell, hand-written | Partially — being migrated PS1→Codex |
| **The project's Ed25519 signing key** | Authenticity of the seed and emitted CDX | Held out of band; signing is automatic | No — but its *scope* is auditable (one key, one purpose) |
| **The plugs** (50+: ELF, PE, ARM64, RISC-V, HTML, PTX, …) | Container/format and cross-arch codegen | Each is a Codex program (so compiled by the trusted seed), but its codegen is **tested, not proven** | **Yes** — translation validation (Section 5) |
| **The host OS + WHP + dev hardware** | Where the build runs | Standard platform trust during build only | Out of scope; build-time only |

The plugs deserve emphasis: they are written *in* Codex and therefore
inherit the seed's frontend guarantees, but the correctness of a plug's
emitter (IR → target bytes) is currently established by the cross-arch
test battery (ARM64 135/135, RISC-V 135/135), not by proof. A plug that
mis-encodes an instruction the battery doesn't exercise would be a
silent toolchain-TCB bug.

---

## 4. The central honest caveat: fixed point ≠ correctness

Codex's flagship correctness gate is the **hard fixed point**: the seed
compiles the compiler source to a byte-identical seed (text round-trip +
CDX byte-identity, `build/build.ps1`). This is a genuinely strong
property — it proves the compiler is a stable fixed point of itself and
that the emitter loses no information.

It does **not** prove the compiler is *correct*. A compiler can be a
perfectly stable fixed point of itself and be consistently wrong: it
implements *some* function from source to machine code, faithfully and
reproducibly, but nothing in the fixed-point check pins that function to
the Codex language specification. Self-consistency is not soundness.

This is the same structural gap Thompson named in "Reflections on
Trusting Trust": a self-reproducing toolchain reproduces its own
behavior, including any latent miscompilation, and the reproduction
itself offers no evidence the behavior is right. The historical lineage
sharpens the point — the current seed descends (BS1/BS1.1) from the now-
retired C# reference compiler, so any bias baked in there could in
principle persist undetected through the fixed point.

What actually backs correctness today, in descending order of strength:

1. **The type system**, enforced at compile time: linear (CDX2061/2063),
   effects, bounded integers, exhaustiveness, the static bounds prover
   (CDX4010). These are real, mechanical checks.
2. **The propositional-equality / proof layer** (`Refl`/`sym`/`trans`/
   `cong`, now sound after the CL that fixed the vacuous `PropEqTy`
   unifier arm — see `Induction.md`). Small today; the in-flight
   induction + normalizer work makes it load-bearing.
3. **The test battery** (~430 with `-Apps`, cross-arch parity) — empirical,
   not exhaustive.
4. **The fixed point** — consistency, per above.
5. **Diverse re-derivation:** BS3 rebuilds the seed standalone from the
   pingpong output on bare metal with no C# in the chain. This is the
   strongest existing answer to trusting-trust — an independent path to
   the same artifact — and it should be named as such.

The intellectually honest one-line statement: **Codex's correctness rests
on a strong type system plus extensive testing plus self-consistency; it
is not, today, backed by a machine-checked proof that the compiler
implements its specification.** Every external claim should be calibrated
to that sentence.

---

## 5. The shrink roadmap

Each item removes something from the TCB or converts a "trusted" entry
into a "checked/proven" one. Ordered by leverage.

1. **Pure-Codex VMX host (Backlog #4).** Removes `codex-vm.c` (~6000 lines
   of C) from the self-host toolchain TCB. The largest single non-Codex
   trusted component; eliminating it makes "no borrowed substrate"
   literally true for the build, not just the deployment.
2. **Proof layer to the flagship (`Induction.md` Stages 3–5).** A working
   δ/ι/β normalizer + structural induction lets *specific functions* carry
   machine-checked correctness proofs (`reverse (reverse xs) === xs` and
   beyond). This does not verify the whole compiler, but it moves chosen
   properties from "tested" to "proven," and it is the mechanism every
   later item below depends on.
3. **A reference IR semantics + translation validation for plugs.** Define
   an operational semantics for Codex IR and check each plug's output
   against it per-compile (the Fiat-Cryptography / CryptOpt move: validate
   the run, don't trust the emitter). Converts the 50+ plugs from
   trusted-by-testing to checked-per-build.
4. **Derived codecs (Narcissus-style).** Where Codex hand-writes
   encoder/decoder pairs (`codex.foreword.encode`), derive both from one
   spec and machine-check the round-trip. Removes hand-written
   serialization from the trusted set, format by format.
5. **Cost as a checked property (TiML-style `punctual`).** Promote
   `punctual`'s instruction budget to a size-indexed bound discharged by
   the bounds prover, so worst-case time becomes a checked type-level
   fact, not a reported number.

None of these claim a Coq-grade end-to-end theorem. They are the
realistic, in-language steps that each make the trusted set smaller and
the checked set larger.

---

## 6. Why this framing matters

The formal-methods tradition (Chlipala's Fiat Cryptography, Bedrock2,
the Lightbulb and FE310 crypto-server stacks; CompCert; seL4) is judged
almost entirely on TCB size and on what the top-level theorem actually
quantifies over. Codex will be judged the same way the moment it is shown
to that audience. The advantage Codex holds — a genuinely tiny *runtime*
TCB with no OS/libc beneath it — is real and worth stating loudly. The
gap Codex carries — *self-consistency is not a correctness proof* — is
equally real and must be stated in the same breath, because claiming
otherwise is the fastest way to lose a serious reader's trust. This doc
exists so that both are always said together.

See `docs/Reference/Chlipala-StructureAndGuarantees.md` for the external
research this connects to, and `docs/Designs/Language/Active/Induction.md`
for the proof-layer work that item 2 of the roadmap tracks.
