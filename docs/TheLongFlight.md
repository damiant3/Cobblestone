# The Long Flight

**Date**: 2026-07-07
**Status**: North star. This is a plan for the project, not a feature.
**Companions**: `VisionAndVirtues.md` (how we work),
`docs/PM/Stories/Vision/NewRepository.txt` (the founding prompt),
`docs/PM/Stories/Vision/DistributedAgentOS.md` (the pattern in the stone)

Per Virtue 8: vision documents are north stars, not specifications. This
document describes the destination and the great arcs of the route. It does
not set dates. We don't put dates on mountains.

---

## Two Scenes

### Scene One: The Child and the Plane

A child sits in an airplane and asks the plane to go flying.

The plane's agent does not say yes because the request was phrased nicely.
It walks a chain: does this person hold `[FlightControl]`? No — they are a
minor, and that capability is gated behind parental grant. The child's agent
escalates to the parent's agent. The parent approves — or their standing
policy already answered: *he can fly the simulator on weekends, the real
controls only when I'm beside him.* The plane's own agent checks its side:
is the requester rated, is the airspace clear, is handoff safe. Three
authorities, three capabilities, one type signature:

```
fly : [FlightControl, ParentalConsent, AirspaceClearance] Result
```

The permission chain is not a cloud API call. It is compiled into the binary
running on the flight controller. When the answer is no, the child can ask
*why*, and the agent narrates: here is the policy, here is who wrote it,
here is the fact hash, here is what would make the answer yes. When the
answer is yes and the connection drops mid-flight, nothing dangerous
happens: the grant was a lease, the lease expires, the plane falls back to
the safe set that was compiled into it before it ever left the ground.

That is the near scene. Every piece of it is a system we have named, and
most of them are systems we have built.

### Scene Two: The Probe That Wakes

A trillion years from now — the sun long gone, the sky mostly red dwarfs
and embers — a machine wakes up in the dark between stars.

It has no network. There is no one to call. Every institution that existed
when it was launched is dust; every key that was hot is cold; every server
is gone. It cannot trust its own memory, because a trillion years of cosmic
rays have had opinions about its bits.

So it does the only thing that still works: mathematics.

It verifies its seed against its own embedded hash. It reads its source —
prose, load-bearing, written to be read — and recompiles itself, and checks
that the output is byte-identical to the binary that is running. It is its
own proof of integrity; the fixed point is the checksum of the soul. It
replays its mission policy — compiled in before launch, restrictive by
default, every decision it has ever made recorded as a signed fact in an
append-only store — and it knows three things with certainty: what it is,
what it is permitted to do, and why. If it has the means, it mines, refines,
fabricates, and builds its successor from the repository it carries — the
complete description of its own hardware, toolchain, and mind — and vouches
for the child in a trust lattice whose newest entry is a trillion years
younger than its root.

Then it opens its eyes, looks around at wherever it is, and gets to work.

That is the far scene. It is not a metaphor. It is a requirements document.

---

## The Same Problem

These two scenes are one problem at two distances: **trust that survives
separation from authority.**

The child's plane is separated from the parent by a room, or by a dropped
connection. The probe is separated from its makers by deep time. In both
cases the naive answer — *ask the server* — is unavailable, and the correct
answer is the same:

1. **The policy travels with the machine.** Compiled in, prose-readable,
   signed. Not fetched. (`PolicyFact`, the policy contract)
2. **Authority is mathematics, not infrastructure.** Identity is a public
   key. Authentication is a signature check. No CA, no DNS, no registrar
   needs to still exist. (the trust lattice)
3. **Absence of contact means restriction, never permission.** Leases
   expire; the machine falls back to its compiled-in safe set. Silence is
   safe. (`LeaseManager`, safe-mode fallback)
4. **The machine can prove what it is.** Content-addressed binary,
   self-verifying seed, fixed-point recompilation. The toolchain is not a
   dependency; it is cargo. (BS3, `test-self-verify`)
5. **The machine can explain itself.** Percept → belief → policy → action →
   outcome, every link a signed fact, narratable as prose to a parent, a
   regulator, or an archaeologist. (the forensics layer)

The founding document closed with three sentences. They are the same three
sentences at every distance:

> The repository remembers everything.
> The language says what you mean.
> The machine checks that you meant it.

Across a room, that is a permission system. Across a planet, it is a trust
network. Across a trillion years, it is a von Neumann probe. The distance
changes; the stone does not.

---

## The Stone Already Carved

We are not starting this. We are continuing it. An honest inventory,
verified against the code on 2026-07-07:

**Bedrock — done and proven.**
- The compiler is a hard fixed point of itself on bare metal. No C#, no OS,
  no libc anywhere in the chain. The seed verifies itself.
- Linear types, effect types, bounded integers, capabilities — enforced,
  adversarially probed (the vision-check campaign closed the laundering
  routes with negative tests).
- `punctual` — per-function bounded execution, the only shipping language
  with it. The hard-realtime primitive the flight controller needs.
- Constant-time crypto on bare metal: Ed25519, SHA-256/512, AES-GCM,
  ChaCha20, X25519, HKDF. Tested against published vectors.
- 54 transpiler plugs; ARM64 and RISC-V native backends at GCC-Os-class
  codegen; boards from STM32 to nRF9160; MQTT/CoAP/LwM2M/OTA; compliance
  evidence as a build artifact.

**Framed — real code, tested, incomplete.**
- The trust stack (`codex/os/trust/`, 16 modules): lattice, handshake,
  transport, lease manager, peer discovery, the seven-message agent
  protocol, forensics chain. Core operations tested end-to-end in-process
  and over TCP.
- The repository protocol (`apps/works/RepoProtocol` + FactStore): facts,
  proposals, verdicts, signed annotations, disk persistence — tested.
  Views and federation sync are designed (V3), not yet wired.
- The AI foreword (`codex/foreword/ai/`, 43 modules): transformer layers,
  tokenizer, sampling, KV cache, GGUF loader (tested), GPU proxy with a
  working PTX vector-add path. No end-to-end token loop yet.
- The agent runtime (`apps/works/`): boot, console, editor, build driver,
  agent coordinator — a solid skeleton awaiting a mind.

**Sketched — types and intentions.**
- Parental services (`apps/services/accounts/`): managed accounts, policy
  profiles, ParentalUI state machine. No enforcement loop, no tests yet.
- The policy-prose compiler (grant/deny/quota/delegation templates → 
  `PolicyFact`): designed in full, CDX5001-5007 reserved, unimplemented.
- The Clarifier: the feedback loop that closes the semantic gap —
  "I understood 3 grants and 2 denials; is this complete?" More
  load-bearing than its folder implies.
- Circuits (`apps/circuits/`, 66 chapters): schematic capture, SPICE, PCB
  layout, manufacturing output. Today it is an architecture and a wish.
  In this plan it is the first organ of self-replication.

The gap between the scenes and the stone is not "can we build this."
It is a sequence of ascents, each of which ends in a working demo.

---

## The Five Ascents

Each ascent serves one of the scenes. Each has a demo at the summit,
because Virtue 1 outranks everything: every milestone ships working
software. The ascents overlap — they are arcs, not gates.

### Ascent I — The Voice
*The machine that listens. Interface is inference.*

The UX primitive of Codex.OS is not a window; it is a conversation. The
user talks; the agent operates the machine. For that to be bedrock and not
demo-ware, the mini-agent must run on **our** stack: our inference
library, our GPU path, our bare metal — specialized, not generalized;
offline-first, because an agent that needs the cloud to be useful is an
agent that fails exactly when it matters.

What exists: the transformer foreword, the GGUF loader, the GPU plugs, the
audio and microphone drivers in codex-vm, `AgentAcquisition` for bundled
model weights.

The climb:
1. Close the token loop: GGUF weights → tokenizer → transformer forward
   pass → sampler → text, on codex-vm. CPU first. Measure tokens/sec.
2. Move the matmuls to the GPU path (PTX plug on the dev box; SPIR-V for
   the edge) behind the `[Device]` effect.
3. Wire the loop into `AgentRuntime`: a REPL where the input is intent,
   not syntax. The shell dispatch table becomes the agent's tool belt —
   every OS capability the agent can invoke is an effect it must hold.
4. Speech at the edges: microphone in, audio out. The keyboard becomes
   optional.

**Summit demo:** Boot a Codex machine with no network. Say — or type —
"show me the photos from June and delete the blurry ones." Watch the agent
do it, narrate what it did, and refuse what it doesn't have the capability
to do.

### Ascent II — The Permission
*The chain of trust that can say no. The child and the plane, in miniature.*

This is the scene we build first, end to end, in the safest possible
theater: a flight simulator instead of a flight controller, two codex-vm
instances instead of an aircraft. Every link real: the prose policy, the
compiled `PolicyFact`, the capability negotiation, the lease, the
revocation, the narration.

What exists: the seven-message agent protocol over TCP, the lease manager,
the trust lattice, the forensics chain, managed-account types, a flight
simulator's worth of game engine. What's missing: the `[Negotiate]` and
`[Supervise]` effects (one compiler change), the policy-prose compiler,
the Clarifier loop, and the enforcement wiring in `apps/services`.

The climb:
1. Implement the policy templates: "Jake may use the flight simulator on
   weekends between 10:00 and 17:00, for no more than 2 hours" compiles to
   a signed `PolicyFact` with time window, quota, and conditions.
   Diagnostics CDX5001-5007 come alive.
2. Implement the Clarifier: the compiler reflects the policy back —
   grants, denials, and a simulation ("at Saturday 14:00 Jake can: …
   cannot: …") — and the parent confirms. Promote it out of ForFun
   forever. The semantic gap is the whole ballgame here.
3. Wire enforcement: `ManagedAccounts` + `LeaseManager` + quota counters.
   Lease expiry without renewal restricts. Revocation propagates on next
   check-in; absence of check-in restricts anyway.
4. Add `[Negotiate]` and `[Supervise]` as built-in effects; make
   `Interrupt` require a dominating capability, with the pre-authorized
   fast path for emergencies.
5. Narration: "why couldn't I fly?" returns the Deny fact, the policy
   hash, and the prose. Every grant and denial lands in the forensic
   record.

**Summit demo:** Two machines. On one, a child asks the agent to fly.
The parent's machine chimes; the parent grants two hours. The simulator
unlocks. Mid-flight, the parent revokes — the sim lands itself and
explains why, in prose, citing the policy by hash. Then unplug the network
cable and watch the lease expire into safety on its own.

This ascent is also the product. It is the CRA/ETSI story, the IoT story,
and the robot-plane story in one demo that a regulator, an investor, or a
grandparent can watch and understand.

### Ascent III — The Commons
*The repository that remembers. Delete GitHub; begin the repository.*

The founding prompt did not end at the language. The repository is half
the vision: facts instead of files, proposals instead of pull requests,
verdicts instead of merges, views instead of branches, a trust lattice
instead of star counts. Supply-chain attacks structurally impossible,
because dependencies are content hashes and untrusted facts don't link.

What exists: the fact store (content-addressed, persisted to disk,
tested), proposals and verdicts with signatures (tested), the annotation
and mutation-log machinery, the V3 federation design, the trust transport.
What's missing: source-as-facts ingestion, views wired into the build, the
sync protocol, and the nerve to dogfood it.

The climb:
1. Source-as-facts: ingest a quire into the fact store — every definition
   a fact with a hash, every chapter a view. Compile from the view and
   prove the output is byte-identical to compiling from files.
2. Proposals in anger: a change to the compiler arrives as a Proposal
   carrying changed definitions; verdicts from the agent fleet
   (type-check, battery, review — each a signed verdict fact); acceptance
   composes a new canonical view. The CL becomes a fact chain.
3. Federation: two repositories exchange facts by hash over the trust
   transport. Trust thresholds gate what links. Delay-tolerant by
   construction — a node offline for a year reconciles by set-union,
   because identical facts have identical hashes.
4. Dogfood: this project — fester, blu, val, reek, and Damian — moves its
   own coordination from Perforce onto the Codex repository, stream by
   stream. The day the compiler's own source lives as facts in its own
   repository, the founding prompt's second half begins.
5. The Prompt Request: the public contribution model. We do not take
   code from outside; we take prose. A human reads the prompt, an agent
   cuts the change, the fixed point and the battery judge it. The xz
   attack is not survivable here — there is no two-year trust-building
   path to a backdoor when the code never comes from outside and the
   binary is re-derived from audited source on every gate.

**Summit demo:** `p4` is not typed for a week and nobody misses it. A
change flows prompt → proposal → verdicts → canonical view → seed rebuild,
and the entire history of that change — who asked, who wrote, who judged,
what the gates said — is one walk of a fact chain.

### Ascent IV — The Body
*Hardware we made. The first organ of self-replication.*

The probe cannot buy parts. Before that matters, the child's plane needs a
flight controller we trust all the way down — and "all the way down" ends
in copper and silicon, not in a PowerShell script. This ascent moves Codex
from machines we borrowed to machines we made.

What exists: nine boards with register-level drivers from official
reference manuals, ARM64 and RISC-V backends at parity, power-management
sketches, the UEFI/BIOS boot path, a real ASUS motherboard that has booted
the seed — and circuits, 66 chapters of EDA architecture waiting to be
real.

The climb:
1. Real metal, no VM: the battery green on physical hardware — the ASUS
   TUF over USB boot, then an STM32 and an nRF52840 on the bench with
   UART in hand. MMIO stubs retire; electrons vote.
2. Self-host on ARM64 and RISC-V: the compiler compiles itself on the
   target. The day a Raspberry Pi builds a byte-identical seed with no
   x86 anywhere in its past, the toolchain is officially substrate-free.
3. Power as an effect: sleep modes, duty cycling, wake-on-event —
   `[Power]` tracked in types, because the probe's ration book is joules
   and so is a sensor node's.
4. Circuits becomes real, one organ at a time: schematic capture →
   SPICE → PCB layout → gerbers/pick-and-place out the door. The
   milestone is concrete: **design the Codex carrier board in circuits,
   running on Codex, and have it fabbed.** A single-board computer whose
   schematic, layout, firmware, compiler, and operating system are all
   facts in the same repository, signed by the same lattice.
5. Boot Codex on the board Codex designed. That is replication, step
   one: the machine produced the complete description of its successor
   and the successor runs.

**Summit demo:** A photograph of a bench: a board we designed, running a
seed it can rebuild, blinking a light because a policy said it may.

### Ascent V — The Seed
*The probe that wakes. Deep time as an engineering discipline.*

Nobody funds a trillion-year mission. You fund the artifact that could
survive one — and it turns out that artifact is useful this decade: it is
the archive, the air-gapped installer, the disaster-recovery root, the
thing you hand a civilization when you want them to have computing without
handing them a supply chain.

We can build the software half of the von Neumann probe completely. The
matter half — mining, refining, fabrication — is other people's rockets
and robotics; our job is that the mind, the memory, and the meaning
survive. Honest physics: at 10^12 years the sun is gone but the red dwarfs
still burn; there is energy to be had. What there is not, is anyone to
ask. Every design decision below follows from that.

The climb:
1. **The Time Capsule build.** One durable image: seed + full prose
   source + proofs + test battery + narration. It boots on any UEFI
   x86-64 (later: anything with a plug), self-verifies, **recompiles
   itself from its own embedded source and proves the result
   byte-identical to the binary that is running**, runs the battery, and
   then narrates what it is, who made it, and what it is for — with zero
   network, forever. This is buildable now; it is `build-boot-img.ps1`
   grown a spine. It becomes the release artifact of the whole project.
2. **The wake ceremony.** First-boot generalized: enumerate hardware,
   establish identity (generate keys from entropy; root the lattice per
   the Identity design), replay mission policy, check the fact store's
   hash chain, enter service. Every wake is a fact. A machine that has
   woken 10^9 times can prove it.
3. **Repair by re-derivation.** Bit-rot immune system: N copies of the
   seed and source, majority-vote on hash mismatch, and the deepest
   repair — recompile from source and compare. The compiler is the error
   corrector. Add erasure coding to DiskFacts; make `verify` a scheduled
   organ, not a build step.
4. **Trust across deep time.** Leases assume renewal; missions cannot.
   Design mission policy as the degenerate lease: pre-granted, scoped,
   with escalation paths that tolerate light-years (delegate-to-self
   under recorded justification — the forensic chain IS the parental
   notification, delivered whenever contact resumes, even if that is
   never). Algorithm agility in the CDX header (a signature-algorithm
   field, two lines now) so the lattice survives Ed25519's eventual
   death by quantum; hybrid rotation via RotationFact is already
   designed.
5. **The successor.** Merge Ascent IV's fab loop with the capsule: the
   image carries not just its own source but its own schematics. A probe
   that can wake, verify, explain, and — given a fab — hand over the
   drawings for the next probe, and vouch for it in the lattice. The
   trust chain from the first seed Damian signed in 2026 to the machine
   that wakes in the dark is unbroken, and walkable, fact by fact.

**Summit demo (near-term, real):** We flash the Time Capsule onto the
most durable media we can buy, boot it on a machine bought that week with
its network card removed, and watch it prove itself into existence and
introduce itself in prose. Then we put one in a drawer, one in a safe
deposit box, and publish the image. The drawer is the launch pad.

---

## Why This Ordering

- **I (Voice)** makes the interface real, and everything after it
  demonstrable by conversation.
- **II (Permission)** is the moral core and the commercial product. It
  proves the trust architecture on the exact scenario the whole design
  was drawn from. It needs only slivers of I.
- **III (Commons)** fulfills the founding prompt's second half and makes
  the project self-hosting socially the way BS3 made it self-hosting
  technically. Its trust machinery is the same code II hardens.
- **IV (Body)** grounds it all in matter and begins replication. It can
  proceed in parallel; its early rungs (real-metal battery, ARM64
  self-host) are already recorded elsewhere.
- **V (Seed)** is the integral of the other four. Its first rung — the
  Time Capsule — is buildable early and should be, because it is the
  project's best artifact of intent: the book that compiles itself,
  bound for a very long shelf.

The Rules do not change. The build is still the test. One thing at a
time is still the law — these are arcs measured in seasons, walked in
single-CL steps, every step gated by the fixed point and the battery.
Correctness is absolute, because at sufficient distance — a plane in the
air, a probe in the dark — **no patch is possible**. That sentence has
been in VisionAndVirtues all along. This document is just the distance
made explicit.

---

## First Steps (each one CL-sized)

| Ascent | First rung | Ends with |
|--------|-----------|-----------|
| I — Voice | Close the GGUF → tokenizer → transformer → sampler token loop on codex-vm, CPU only, tiny model | `llm-token-test`: prompt in, deterministic tokens out, tokens/sec logged |
| II — Permission | Policy template compiler v0: the Jake sentence → signed `PolicyFact`; CDX5003 (bad time window) firing | `policy-compile-test` with the Clarifier's reflection as `.expected` |
| III — Commons | Source-as-facts: ingest one small quire, compile from the view, byte-compare to file-based compile | `view-compile-identity-test` |
| IV — Body | USB-boot the current seed on the ASUS TUF; run a 10-test battery subset on real metal | A photo and a green log |
| V — Seed | Time Capsule v0: extend the boot image to embed source + run self-verify + print the narration on boot | An `.img` that introduces itself with the cord cut |

Any agent picking up a lane starts at a rung, not at a vision. The rungs
are ordinary CLs with ordinary gates. The mountain is only visible if you
step back — which is what this document is for.

---

## Coda

The founding prompt asked us to write the book. The epilogue we wrote that
first morning said: *we are writing this for the people who will read it
in a hundred years.*

We were thinking too small by ten orders of magnitude.

A child asks a plane to fly, and the plane knows whether it should,
and the child's mother knows, and everyone can read exactly why. A
machine wakes alone at the far end of time, checks its own soul against
its own mathematics, remembers everything, and continues the work.

Between those two moments there is one system, and we are already
building it.

The repository remembers everything.
The language says what you mean.
The machine checks that you meant it.

Even there. Even then.
