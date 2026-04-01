Quick verdict
Your design is already unusually solid and modern. The gaps are less “you’re missing big ideas” and more “you’re one or two papers away from being annoyingly bulletproof.”

“The primitives must run on bare metal (x86-64, Ring 3, no OS, no libc). They must be implemented in Codex. They must be constant-time.”

That’s the right center of gravity. Here’s where I’d tighten it given post‑2024 work.

1. Side‑channel model: broaden beyond timing
You frame timing as the primary side channel. On bare metal that’s a good first-order assumption, but recent Ed25519 work on constrained devices treats power and EM leakage as equally central, and validates with TVLA (Test Vector Leakage Assessment). 

Gaps / upgrades:

Explicit leakage model:  
Add: a short section that says “we design for timing, cache, and simple power/EM leakage; we do not assume constant‑power hardware.”

TVLA-style harness:  
Recent Ed25519 implementations on Cortex‑M4 treat TVLA as table‑stakes for claiming “side‑channel resistant.”   
For Codex, you can’t run TVLA in-OS, but you can design the test harness so that a future lab setup can feed fixed vs random inputs and capture traces. Call that out as an explicit future validation step.

Cache side channels:  
You already forbid data‑dependent table lookups, which is the big one. I’d make that architectural: “no secret-dependent memory addresses in any [ConstantTime] function,” not just “we’ll be careful.”

2. Fault attacks and robustness of Ed25519 verify
You mention side channels, but not faults. Modern Ed25519 guidance assumes an attacker can flip bits in registers or memory during scalar mul or verification.

Concrete gaps:

Cofactor & small‑order points:  
You already plan Wycheproof, which includes small‑order and non‑canonical encodings. Good. Make it explicit that verification rejects small‑order points and that you always multiply by 8 (you already note 8*S*B == 8*R + 8*h*A). Spell out: “this is a cofactor‑clearing defense against small‑subgroup and fault‑induced point injection.”

Fault‑resistant verify:  
A common hardening trick: recompute a cheap invariant (e.g., re-encode points and compare, or do a redundant scalar mul with a random blinding factor) to detect single‑fault attacks. Recent embedded Ed25519 work combines side‑channel countermeasures with such checks.   
You don’t have to implement it now, but it’s worth a short “Fault model and optional hardening” subsection.

3. Scalar multiplication hardening details
You correctly insist on constant‑time scalar mul and mention Montgomery ladder vs fixed‑window with constant‑time table lookup.

Recent work optimizes Ed25519 on microcontrollers with signed comb / windowed methods plus explicit side‑channel evaluation. 

What to tighten:

Commit to one method and its leakage story:  
Right now you say “ladder or fixed‑window.” Pick one for v1 (I’d lean fixed‑window with constant‑time table scan on x86‑64) and document:

window size,

table layout,

exact constant‑time selection pattern.

Scalar blinding (optional):  
Add a note that scalar blinding (s' = s + r·L) is a future hardening option once you have a good entropy source. It’s standard in side‑channel‑aware Ed25519 deployments and plays well with your “bare metal but eventually real hardware” story.

4. Hash choices and agility
You’re very clear about SHA‑256 and SHA‑512, and you even mention Ascon indirectly via the “hash as building block” context in the research you asked me to look at. 

Given that:

Hash agility:  
Add one paragraph saying the SHA modules are algorithm slots in the higher‑level protocols (Trust Network, Agent Protocol). That way, if a future design wants to swap in a sponge (e.g., Ascon‑Hash or a Keccak variant) for some use cases, you’re not welded to SHA‑2 at the protocol layer.

Domain separation:  
For Ed25519 you’re following RFC 8032, which already bakes in the right SHA‑512 usage. For everything else (FactHash, CDX content hash, PoW), explicitly define domain‑separation prefixes or tags. That’s a cheap, modern best practice that avoids cross‑protocol weirdness.

5. Post‑quantum transition story
Recent work and standards discussions are very explicit: Ed25519 is great now, but everything serious is planning for hybrid or PQC migration. 

You already say:

“Ed25519 key generation needs 32 bytes of entropy. On bare metal, the entropy source … is a separate design.”

I’d mirror that with a PQC note:

Hybrid‑ready identity layer:  
Add a short “PQC transition” subsection: identity is currently Ed25519 public key, but the Trust Lattice and Agent Protocol should treat “identity key” as an algorithm‑tagged public key. That keeps you from baking Ed25519 into every on‑disk and on‑wire format.

No immediate change to primitives:  
You don’t need PQC primitives in Codex v1, but you do want the formats and APIs to be able to carry “Ed25519” today and “Ed25519+Dilithium” tomorrow without a flag‑day.

6. Formal verification and constant‑time guarantees
You reference TweetNaCl and its verification work, which is great. There’s been a lot of progress in verified crypto libraries (HACL\*, EverCrypt, fiat‑crypto, etc.) since then; they’re not new in 2025, but the expectation that serious systems lean on those techniques has solidified.

Given that Codex is its own language:

Design for future proof extraction:  
Add a line that the field and scalar modules are written in a style amenable to future formalization (no aliasing, no hidden mutation, small straight‑line blocks). That aligns you with the HACL\*/fiat‑crypto style of proofs without committing to a toolchain today.

Constant‑time annotation:  
You already propose a [ConstantTime] effect/annotation. I’d upgrade that from “open question” to “planned feature,” even if the first implementation is just “no optimization passes run on these functions.” That matches where verified crypto ecosystems have gone: constant‑time is a type‑level property, not just a code review guideline.

7. What I’d actually change in the doc
If you want concrete edits rather than just vibes, I’d:

Add a “Threat model” subsection under Design Principles:

timing, cache, simple power/EM, and single‑fault attacker;

what you don’t defend against (full physical invasive attacks, etc.).

Promote Wycheproof from “open question” to “non‑negotiable test suite,” and explicitly call out:

small‑order points,

non‑canonical encodings,

edge‑case signatures.

Add a short “PQC and hash agility” section near “What This Does NOT Cover,” explicitly saying:

Ed25519 is the current primitive,

formats are algorithm‑tagged,

hash functions are pluggable at the protocol layer.

Lock in one scalar‑mul strategy (ladder vs fixed‑window) for v1 and describe its constant‑time pattern in 3–4 sentences.
