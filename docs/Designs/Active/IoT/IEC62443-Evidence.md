# IEC 62443 Compliance Evidence -- Codex

Maps Foundational Requirements FR1-FR7 to Codex mechanisms with concrete evidence.

**Audited 2026-07-27 against seed `A5758E05`.** Every claim below was fired
at the compiler or read at its source. This file carried the highest
density of unsupported claims of the three compliance documents, including
two CDX codes cited for things they do not mean and a compile failure that
does not occur. Corrections are marked **Withdrawn** or **Corrected** in
place; nothing was reworded to keep a status. The worked precedent for
withdrawal is ETSI provision 5.5 in `docs/KingsAndCourts.md`.

---

## FR1 -- Identification and Authentication Control

**Requirement:** All users, processes, and devices must be identified and authenticated before access.

**Codex mechanism:** Trust lattice assigns Ed25519 keypairs per device at provisioning. Capability manifests declare required identity before any I/O capability is granted. Effect types make unauthenticated code paths a compile-time error.

**Evidence:**
- CDX binary header contains an author public key (Ed25519, 32 bytes) at offset 40, and a 64-byte signature at offset 72 over the 32-byte SHA-256 content hash at offset 8
- The verifier's phase 2 scores that key against the trust lattice and rejects below the binary's declared threshold
- A program reaching identity operations must declare `[Identity]`, which is capability id 7 and bit 15; an undeclared effect is CDX2031

**Corrected 2026-07-27.** This section read "Capability manifest syntax
enforces `identity` declaration (CDX2061)". **CDX2061 is `UseAfterConsume`**
-- a mutable or linear binding used after it was moved. It has nothing to do
with identity, manifests or declarations, and no capability manifest syntax
enforces an `identity` declaration. The header's author key is also **not**
a device public key: the build signs with the project key, and a per-device
Ed25519 keypair is generated separately at first-boot provisioning. Reading
one as the other would let a reviewer conclude that every shipped binary
carries the identity of the device it runs on, which it does not.

---

## FR2 -- Use Control / Authorization

**Requirement:** Enforce assigned privileges for authenticated entities; least privilege.

**Codex mechanism:** Capability manifests whitelist permitted operations per function. Effect types partition I/O capabilities -- a function cannot perform undeclared effects. Linear types (CDX2061/2063) prevent capability duplication or escalation.

**Evidence:**
- Effect type annotations are compiler-enforced; undeclared I/O is CDX2031, and laundering it through a plain `let` is CDX2033
- Linear types prevent aliasing of capability tokens -- single-owner semantics
- **CDX2061** (`UseAfterConsume`) is the code that fires when a consumed handle is used again; **CDX2063** (`LinearUnused`) fires when a linear value is never used at all, which is a leak
- Bare-metal binary contains no shell and no dynamic loader -- only declared capabilities exist

**Corrected 2026-07-27.** This section read "CDX2063 (linear resource)
ensures capability handles cannot be copied". CDX2063 is the **leak** code:
it fires when a linear value is dropped without being used. Copying is
CDX2061. The two are the opposite failures of the same exactly-once rule
and citing the wrong one inverts the evidence -- a reviewer checking that
compile logs are free of CDX2063 would be checking that nothing was
*forgotten*, not that nothing was *duplicated*. Both are enforced; the code
numbers were transposed. Note also that the binary does have a syscall
interface: `emit-syscall-handler` installs one and it is capability-checked,
which is a stronger claim than not having one.

---

## FR3 -- System Integrity

**Requirement:** Ensure integrity of components and protect against unauthorized modification.

**Codex mechanism:** Every CDX carries a SHA-256 content hash in its header; the Ed25519 signature is applied by the build pipeline rather than to every CDX (`build.ps1` signs the compiler under test and only when the signing key is present; `compile.ps1` never signs, so an ordinarily compiled CDX is unsigned -- measured 2026-07-28). Boot verifies the content hash against the embedded signature before execution. The fact store records the changes written to it; nothing enforces that a change cannot occur without a fact.

**Evidence:**
- CDX header: SHA-256 content hash + Ed25519 signature (64 bytes)
- Self-compile fixed point: stage 1 CDX = stage 2 CDX (byte-identical), proved by `build/build.ps1` on every gate run
- **Declared** bounds prevent arithmetic corruption: an out-of-range literal in a bounded field is CDX2050, an unprovable range is CDX2051, and `__narrow` traps at runtime. **Plain `Integer` arithmetic is unbounded 64-bit and wraps silently** -- measured 2026-07-27, `i64-max + 1` prints `-9223372036854775808` with no diagnostic. CDX4010 is an *info* recording that a bounds check was **elided** because the range was proven, not a check performed
- No use-after-free by construction (linear types CDX2061)

---

## FR4 -- Data Confidentiality

**Requirement:** Protect confidentiality of data at rest and in transit.

**Codex mechanism:** Linear types enforce single-owner semantics -- sensitive data cannot be aliased or leaked through dangling references. Bare-metal runtime has no swap file, no OS-level cache, no tmpfs.

**Evidence:**
- CDX2061 (linear types): compiler rejects programs that alias secret-bearing buffers
- Bare-metal compilation: no OS memory management surfaces (no page file, no core dumps)
- Effect types require explicit declaration of any channel carrying sensitive data

**Withdrawn 2026-07-27: "Drop semantics zero memory on scope exit."**
Codex has no drop semantics. Allocation is a bump of R10 and there is no
collector; an allocation persists until the producing function returns,
after which the region may be reused by a later `pitch` while still holding
its old bytes. `__alloc` zero-fills a block **when it hands it out**, not
when it is released -- which is why the poison build (`-Poison`, 0xCD fill
instead of zero) is a release gate: that zero-fill is load-bearing and the
build exists to prove nothing depends on it.

The consequence for FR4 is specific and should not be softened: **a secret
is not erased when it goes out of scope, and no compiler mechanism erases
it.** Single ownership means there is no aliased copy elsewhere, which is a
real and useful property; it is not the same as erasure. A deployment with
a residency requirement must overwrite key material explicitly. The same
false claim appeared in the ETSI mapping at provision 5.11-1 and is
withdrawn there too.

---

## FR5 -- Restricted Data Flow

**Requirement:** Segment and control data flow between zones.

**Codex mechanism:** Effect types create static data-flow boundaries. Each function declares its I/O surface; the compiler enforces that data does not cross undeclared zone boundaries. Capability manifests define permitted communication partners.

**Evidence:**
- Effect type system: compiler traces all data paths; undeclared flows are compile errors
- Capability manifest: per-function whitelist of permitted I/O endpoints
- No ambient authority -- bare-metal binary has no implicit network stack or file system

---

## FR6 -- Timely Response to Events

**Requirement:** Respond to security-relevant events within defined time bounds; audit logging.

**Codex mechanism:** `punctual` makes a function's execution structurally bounded and reports its exact instruction count. Fact store provides audit logging.

**Evidence:**
- `punctual` enforces five restrictions as **hard errors**: no non-punctual callee (CDX6001), no heap allocation (CDX6002), no closure or lambda (CDX6003), no effect (CDX6004), no self-recursion (CDX6005). These are what make the bound exist: a function that cannot loop, recurse or allocate has a finite instruction sequence
- CDX6010 reports that sequence's length, decoded from the finished bytes after NOP compaction, so it is exact rather than estimated
- `build/wcet-validate.ps1` runs the binary under `codex-vm -wcet` with a hardware execution breakpoint per function and gates on **observed <= budget per invocation**. This is the step that turns a static count into a checked claim
- Fact store records timestamped audit entries for all security-relevant events

**Withdrawn 2026-07-27: the compile does not fail.** This section said "If
the compiler cannot prove the deadline is met, compilation fails" and, in
the evidence list, "the compiler proves the handler meets its deadline or
rejects the program". Measured: a nine-parameter `punctual 1` function
compiles to an **86,632-byte binary at exit 0**, reporting
`CDX6010: 106 instructions (10600% of budget 1)` and
`warning CDX6011: exceeds budget: 106 / 1`. A body the counter cannot
decode raises CDX6012, also a warning. **Nothing about a budget fails a
build.** The five structural restrictions do fail it, and they are the real
compile-time guarantee; the budget is a reported measurement with a
separate validator behind it.

**Withdrawn: "deadline" and "worst-case execution time".** CDX6010 counts
instructions, deliberately: the count is architecture-independent and the
compiler does not know clock speed or pipeline depth, so it makes no
wall-clock claim. Converting a count to a deadline is the system
integrator's step and this document must not present it as done.

**Withdrawn: the end-to-end latency claim.** The last sentence read "The
missile-warning example shows a sensor-to-alert pipeline where each stage
carries a `punctual` bound, and the compiler verifies the end-to-end
latency." `codex/test/examples/missile-warning.codex` has four `punctual`
functions and **not one carries an explicit budget** -- all four take the
256-instruction default. Nothing in the compiler composes per-function
counts into a pipeline figure, and no end-to-end latency is computed or
verified anywhere.

**How `punctual` satisfies FR6, stated accurately:** FR6 requires timely
response, which most implementations meet by best-effort testing. Codex
makes the *boundedness* a compile-time property -- a `punctual` function
provably cannot loop, recurse, allocate or perform I/O, so its instruction
count is finite and exactly known. Turning that into a deadline requires
two further steps the manufacturer owns: run `build/wcet-validate.ps1` to
check observed counts against declared budgets, and multiply by the target's
cycle characteristics. What Codex removes is the unbounded case, not the
arithmetic.

---

## FR7 -- Resource Availability

**Requirement:** Ensure availability of the system under degraded conditions; prevent denial of service.

**Codex mechanism:** A function marked `punctual` cannot loop, recurse or allocate, so it cannot exhaust CPU or heap. Declared integer bounds prevent overflow-driven exhaustion where they are used. A preemptive per-core scheduler stops any single task monopolising a core. Linear types prevent resource leaks.

**Evidence:**
- `punctual` (CDX6001-6005): a function so marked has a finite, exactly-counted instruction sequence, and CDX6010 reports it. **This is opt-in per function** -- an unmarked function may loop indefinitely
- Declared bounds (CDX2050/CDX2051) reject out-of-range literals and unprovable ranges; `__narrow` traps at runtime
- Linear types (CDX2061/2063): every resource has exactly one owner; no leaks, no double-free
- Bare-metal: no OS-level DoS surface -- no shell, no libc, no dynamic loader
- The scheduler preempts on a timer tick on **every** core, so a runaway task does not hold a CPU. Evidence is a counter at cell 36216 bumped only by a core whose id is not zero, read by `codex/test/smp-preempt`

**Withdrawn 2026-07-27, three claims:**

**"No function can consume unbounded CPU -- WCET is proved at compile
time."** `punctual` is an opt-in per-function annotation. Nothing requires
it, most of the tree does not use it, and an ordinary function may loop
forever. The correct statement is that a `punctual` function cannot, and
that the guarantee extends exactly as far as the annotation does.

**"All arithmetic is range-checked; no silent overflow."** Measured: a
chapter printing `9223372036854775807 + 1` compiles with zero diagnostics
and prints `-9223372036854775808`. Plain `Integer` is unbounded 64-bit and
wraps. Range checking is what a **declared** bound buys.

**"Bare-metal: no competing processes, no scheduler preemption."** The
opposite is true, and the true version is the stronger answer to FR7. There
is a 16-slot process table, a timer-driven preemptive scheduler, per-core
LAPIC timers on application processors, priority time slices and process
affinity. A system with no preemption is one where a single runaway task
denies service to everything else; preemption is the mechanism that
prevents it.

**How `punctual` satisfies FR7, stated accurately:** FR7 is about bounded
resource consumption. `punctual` converts the boundedness of a chosen
function from an operational concern into a structural property of the
binary: no heap, no recursion, no unbounded callee, and an exact
instruction count. Combined with declared integer bounds on the paths that
carry attacker-influenced values, linear types for leak-freedom, and
per-core preemption for everything not marked `punctual`, a deployment can
be made resistant to input-driven exhaustion. It is not automatic, and the
coverage is exactly the set of functions the manufacturer annotates and
validates with `build/wcet-validate.ps1`.

---

## Summary

| FR | Status | Key Mechanism |
|----|--------|---------------|
| FR1 | Satisfied | Trust lattice + Ed25519 author key + verifier phase 2 |
| FR2 | Satisfied | Capability manifests + effect types + linear types |
| FR3 | Satisfied | CDX content hash (signature is pipeline-applied, not universal) + fixed-point self-compile + declared integer bounds |
| FR4 | **Partial** | Linear types + bare-metal (no OS leak surface). **No erasure on scope exit** |
| FR5 | Satisfied | Effect types + capability manifests (static data-flow) |
| FR6 | **Partial** | `punctual` structural restrictions + `wcet-validate.ps1`. **The compiler does not fail a build on a missed budget** |
| FR7 | **Partial** | `punctual` where annotated + declared bounds + linear types + per-core preemption |

**FR4, FR6 and FR7 moved from Satisfied to Partial**, on measurement rather
than on regression. Each rested on a mechanism that does not exist -- drop
semantics zeroing memory, a compile failure on a missed deadline, and
range-checked arithmetic everywhere -- and each retains a real mechanism
underneath that covers part of the requirement. The remaining coverage is
the manufacturer's to complete, and the sections above say exactly what is
left.

**Every claim in this file that a compile *fails* has been fired.** The
ones that survived are CDX6001-6005 (the `punctual` restrictions),
CDX2031/CDX2033 (undeclared and laundered effects), CDX2050/CDX2051
(bounded fields and unprovable ranges), CDX2061/CDX2063 (linear misuse) and
CDX4001 (an effect outside the capability vocabulary, now pinned by
`codex/test/errors/cap-outside-vocabulary`). Those are the load-bearing
refusals and they are what this document should rest on.
