# Merchants and Traders -- MiFID II RTS 6 and Electronic Trading

How Codex changes the cost of proving an algorithmic trading system is
what its operator says it is.

---

## The Claim

An investment firm running algorithmic trading in the EU carries an
obligation that is unusual among software regulations: it must be able
to demonstrate, to a competent authority, on demand, that its trading
system **does not behave in an unintended manner** (RTS 6 Article 5(1)),
and it must re-demonstrate that every year in a validation report signed
off by senior management (Article 9).

Nearly every firm meets this with process. Test plans, sign-off
workflows, change-control tickets, a self-assessment binder, an internal
audit that reads the binder. The artefacts describe the system. They do
not constrain it. A trading system and its compliance evidence can drift
apart on the next deploy, and the annual self-assessment is the
mechanism by which the industry has agreed to notice, twelve months
late.

Codex takes the position it takes everywhere else: **move the assertion
into the compiler, where it has a runner.** A bounded-execution
guarantee that a linter advises is not the same object as one the build
refuses to produce without. An order-value ceiling written in a config
file is not the same object as one carried in the type of the field.

**This document is a pitch, and pitches are where compliance claims go
to rot.** `KingsAndCourts.md` was swept on 2026-07-27 and twenty-one of
its rows did not survive contact with the compiler. So this one is
written with that outcome assumed: every row below carries an evidence
class, and one of the classes is **ABSENT**. Sections 7 and 8 are the
inventory of what Codex does not have, and they are longer than the
sections about what it does. That is the honest shape of the thing today
and it is not an apology. A firm reading this needs to know which
articles we move and which we do not touch, because the ones we do not
touch are the ones they will still be paying consultants for.

**Regulatory text verified 2026-08-11** against the article structure
and operative text of Commission Delegated Regulation (EU) 2017/589 and
(EU) 2017/574. Sources are listed at the end. Article numbers are quoted
because a claim against an unnamed article is not checkable, which is
the failure mode this project has already paid for once.

---

## 1. What RTS 6 Actually Asks For

RTS 6 is Commission Delegated Regulation (EU) 2017/589, the technical
standard under Article 17 of MiFID II (Directive 2014/65/EU). It runs to
29 articles in five chapters plus two annexes, and it applies to any
investment firm engaged in algorithmic trading, with the heaviest
obligations falling on firms using a **high-frequency algorithmic
trading technique**.

That last term is defined, not rhetorical. MiFID II Article 4(1)(40)
requires all three of: latency-minimising infrastructure (co-location,
proximity hosting, or high-speed DEA); order initiation, generation,
routing or execution determined by the system without human intervention
for individual orders; and **high message intraday rates**. Delegated
Regulation (EU) 2017/565 Article 19 puts numbers on the third: on
average at least 2 messages per second in any single instrument, or at
least 4 messages per second across all instruments on a venue. Two
messages per second is a low bar. A great many firms that do not think
of themselves as HFT are inside this definition, and the Article 28
record-keeping obligation is the one that hurts them.

| Chapter | Articles | Subject |
|---|---|---|
| I | 1-4 | Governance, compliance function, staffing, IT outsourcing |
| II | 5-18 | **Resilience of trading systems** |
| III | 19-23 | Direct electronic access |
| IV | 24-26 | General clearing members |
| V | 27-29 | HFT record keeping and final provisions |

Chapter II is where a language can matter. The others are about who
signs what.

| Art | Heading | What it demands, in one line |
|---|---|---|
| 5 | General methodology | Testing must show the system does not behave in an unintended manner, complies with venue rules, and does not contribute to disorderly trading conditions |
| 6 | Conformance testing | Prove the system interacts with the venue's matching logic as intended, on first access and after material updates |
| 7 | Testing environments | A test environment separated from production; responsibility stays with the firm even when the venue provides it |
| 8 | Controlled deployment | Pre-set limits on instruments, price/value/number of orders, strategy positions, and venues |
| 9 | Annual self-assessment | A validation report drawn by risk management, audited by internal audit, approved by senior management |
| 10 | Stress testing | High message volume and high trade volume tests at **twice** the previous six months' peak |
| 11 | Material changes | A designated reviewer before any material production change, proportionate to its magnitude |
| 12 | Kill functionality | Cancel immediately, as an emergency measure, any or all unexecuted orders on any or all venues, and identify the responsible algorithm, trader, desk or client per order |
| 13 | Market abuse surveillance | Automated detection of manipulation |
| 14 | Business continuity | |
| 15 | Pre-trade controls | Price collars, maximum order values, maximum order volumes, maximum message limits, repeated automated execution throttles |
| 16 | Real-time monitoring | Alerts generated **within five seconds** of the relevant event |
| 17 | Post-trade controls | Continuous exposure calculation, real-time reconciliation against venue/broker/clearer records |
| 18 | Security and access | IT security management, annual penetration testing |
| 28 | Order records | Record each submitted order immediately, in the Annex II format, kept five years |

---

## 2. Evidence Classes

Same four as `KingsAndCourts.md`, plus one this document needs and that
one did not have.

| Class | Meaning |
|---|---|
| BY-CONSTRUCTION | The language prevents the violation; the evidence is that the code compiled |
| MECHANISM | A runtime mechanism enforces the property |
| DEPLOYMENT | The provisioning or operating process ensures it |
| ORGANIZATIONAL | Process obligation, no code artefact can discharge it |
| **ABSENT** | **Codex does not have this. It is named so nobody counts it as met** |

The fifth class is the whole reason this document is worth reading. A
compliance matrix with no ABSENT rows is a matrix nobody checked.

---

## 3. Article 5 and the Determinism Argument

Article 5(1) is the load-bearing sentence for a language pitch: the
firm's development and testing methodology must ensure the trading
system **"does not behave in an unintended manner"**, and Article 5(4)
requires that it not contribute to disorderly trading conditions and
that it continue to work effectively in stressed market conditions.

"Does not behave in an unintended manner" is not a testable predicate in
C++. It is discharged, industry-wide, by an argument from coverage: we
tested a lot, we found nothing, sign here. The failure classes that
actually produce disorderly trading are the ones coverage is worst at.

### The classes Codex removes

| Failure class | Mechanism | Refusal | Class |
|---|---|---|---|
| Use-after-free, double-free of an order or session handle | `linear` types, exactly-once usage | CDX2061 (used twice), CDX2063 (never used) | BY-CONSTRUCTION |
| Undeclared I/O from a strategy (an algorithm that sends where nobody said it could) | Effect types; the effect row is part of the signature | CDX2031, CDX2033 (laundered through a plain `let`) | BY-CONSTRUCTION |
| An effect outside the granted capability vocabulary | Capability table, `codex/foreword/core/Capability.codex` | CDX4001 | BY-CONSTRUCTION |
| Out-of-range value in a bounded field (an order quantity, a price tick) | `Integer between L and H` | CDX2050 (out-of-range literal), CDX2051 (range not provable) | BY-CONSTRUCTION |
| Unbounded latency in a hot path | `punctual` | CDX6001-6005 | BY-CONSTRUCTION |
| Garbage-collection pause during a quote update | There is no garbage collector and no allocator in `punctual` code | CDX6002 | BY-CONSTRUCTION |
| Runtime, OS, or libc behaviour the firm did not write | The binary is a single signed CDX; there is no OS beneath it | -- | BY-CONSTRUCTION |

These refusals are not aspirational. Each is pinned by a chapter under
`codex/test/errors/` that fails the build if the refusal stops
happening: **173 refusal chapters in total, of which 7 are punctual, 13
linear and 15 effect** (measured 2026-08-11). The linear and effect sets
include a series of `-launder-` chapters, which are the interesting
ones: they are deliberate attempts to route a violation around the check
through a closure, a partial application, a record field, a list, a
higher-order function, or a `let`. A refusal that only survives the
naive case is a refusal a strategy author will get around by accident.

### `punctual`, and what it does and does not promise

`punctual` marks a function as having bounded execution. The compiler
enforces five structural restrictions:

| CDX | Restriction |
|---|---|
| CDX6001 | Cannot call non-punctual functions (transitivity) |
| CDX6002 | Cannot allocate |
| CDX6003 | Cannot use closures or lambdas |
| CDX6004 | Must be effect-free |
| CDX6005 | Cannot self-recurse |

`codex.foreword.punctual` is 8 chapters where every function is
punctual: clamped and saturating integer arithmetic, bit operations,
fast reciprocal and inverse square root, CORDIC trigonometry, byte-order
conversion. There are 112 `punctual` declarations across the tree
(measured 2026-08-11).

**What it does not promise, stated plainly because the last document
that soft-pedalled this had to be corrected in place:** the instruction
budget (`punctual 128 f`, default 256) is a **warning**, CDX6011, not a
build failure. The gate is `build/wcet-validate.ps1`, which is a
separate command.

That gate is, however, better than a static analysis. It compiles the
program, runs it under `codex-vm -wcet`, and counts **dynamic
instructions per invocation** using DR0-DR3 entry breakpoints and the
trap flag, observation-only, with no guest byte modified. It applies two
hard gates: observed <= declared budget, and observed <= the static
CDX6010 decode of the function's finished bytes. The second is the
interesting one. The static count is a superset of any dynamic path
because punctual code cannot loop, so an observation **above** the
static count means the decoder or the emitter is lying, and the run
fails. It is a WCET check that can catch its own instrument.

The compiler also does not claim wall-clock time. The count is
architecture-independent instruction count, not cycles and not
nanoseconds. Clock speed, pipeline, cache and memory behaviour are the
system integrator's, and any firm that tells a regulator otherwise on
the strength of this is overclaiming on our behalf.

### Why this matters for Article 5 specifically

The firm's methodology has to show the system does not behave
unintendedly. With Codex, a class of that argument stops being
"we tested for it" and becomes "the artefact could not have been
produced if it were present." That is a different kind of statement to
put in front of a competent authority, and it is cheaper to maintain,
because it does not decay between deploys. It survives the next commit
by construction rather than by anyone remembering to re-run anything.

The honest boundary: it covers memory safety, undeclared effects,
declared-range violations, and bounded execution. It does not cover
strategy logic. A perfectly type-safe algorithm can still be a momentum
strategy that amplifies a flash crash. Article 5(4)(d) is about market
behaviour, and no type system reaches it.

---

## 4. Article 15, and Limits That Live in the Type

Article 15 lists the pre-trade controls by name: price collars, maximum
order values, maximum order volumes, maximum messages limits, and
repeated automated execution throttles that disable the strategy after a
pre-determined number of repeated executions until re-enabled by
designated staff.

Every firm has these. They live in a configuration file, a risk gateway,
or a venue-side control, and the recurring incident is not that the
control is missing but that **some path reached the wire without passing
through it**. That is a reachability question, and reachability is
exactly what an effect system decides.

| Article 15 control | Codex expression | Class |
|---|---|---|
| Maximum order value / volume | `Integer between 0 and N` on the field; a literal outside it is CDX2050, a computed value whose range cannot be proven is CDX2051 | BY-CONSTRUCTION |
| Price collar | Bounded types plus `clamping` or `error` arithmetic mode, so the collar is the type of the price rather than a check before the send | BY-CONSTRUCTION (partial: see the wrapping caveat below) |
| Maximum messages limit | Token bucket, `codex/foreword/core/RateLimiter.codex` (`tb-try-consume`, capacity and refill-per-second) | MECHANISM |
| Repeated execution throttle | Fuel-capped iteration, `codex/foreword/core/Fuel.codex` (`FuelOk` / `FuelExhausted`, a total function that must be handled) | MECHANISM |
| No unaudited path to the venue | Send is an effect; a function that can send says so in its row, and CDX2031 refuses one that does not. The row cannot be laundered through a `let` (CDX2033), a closure, a partial application, or a record field | BY-CONSTRUCTION |
| Order flow follows the protocol in the right order | `codex/foreword/core/SessionTypes.codex`: a channel type carries its protocol state and each operation consumes the channel and returns it at the next state | BY-CONSTRUCTION |

**The caveat that has to be in this section and not a footnote: plain
`Integer` arithmetic wraps silently.** Overflow safety extends exactly
as far as declared bounds do. A price field typed `Integer` with a
mental note that it fits in 32 bits is not protected by anything, and a
firm adopting Codex for a risk layer has to actually declare the ranges
to get the guarantee. This is the single most likely way for a Codex
trading system to be built wrong while feeling safe, so it is named here
rather than left for someone to discover.

Two supporting pieces worth naming because they are the parts financial
code usually gets wrong:

- `codex/foreword/core/Decimal.codex` gives a mantissa-and-scale
  decimal, not a binary float, so a price is a price and not an
  approximation of one.
- Units of measure are in the type system. `codex/foreword/core/Units.codex`
  declares `unit family` types with a base unit and integer conversion
  factors, and `DateTime.codex` declares `Timestamp` as a unit family
  based on nanoseconds (`NanoStamp`, `MicroStamp`, `MilliStamp`,
  `SecondStamp`). A quantity in one unit cannot be silently used where
  another is expected. For a domain whose most famous losses include
  unit and scale confusion, this is not a small thing.

---

## 5. Article 12, and Proving the Kill Switch Can Run

Article 12 requires the firm to be able to cancel immediately, as an
emergency measure, any or all unexecuted orders on any or all venues,
and to identify which algorithm, trader, desk or client is responsible
for each order.

The failure mode nobody plans for is that the kill path is the one path
never exercised under the conditions that make it necessary. It calls
into an allocator under memory pressure; it takes a lock held by the
thread that is wedged; it logs before it cancels and the logger is the
thing that is stuck.

`punctual` is the direct answer, and it is a stronger one here than in
the latency argument that usually motivates it. A kill path declared
`punctual` **cannot allocate** (CDX6002), **cannot call anything
unbounded** (CDX6001, transitively), and **cannot recurse** (CDX6005).
The reasons that class of path fails at 3pm on a bad day are structurally
absent, and the absence is verified by the compiler on every build, not
by an annual test.

`build/wcet-validate.ps1` then gives the per-invocation instruction
count, which is the number a firm actually wants in the Article 9
validation report: not "the kill switch was tested and worked", but "the
kill path executes in at most N instructions and here is the machine
observation that says so".

**The honest boundary, and it is a large one.** `punctual` bounds the
code path. It does not bound the venue. Cancel-on-disconnect, mass
cancel messages, and the venue's own acknowledgement latency are outside
anything a compiler can see, and Article 12's "immediately" is measured
end to end. Codex removes one class of reason a kill switch fails. It
does not deliver a kill switch.

| Article 12 element | Codex | Class |
|---|---|---|
| Kill path structurally cannot allocate, block on unbounded work, or recurse | `punctual`, CDX6001/6002/6005 | BY-CONSTRUCTION |
| Machine-observed instruction bound on the kill path | `build/wcet-validate.ps1` per-invocation count under `codex-vm -wcet` | MECHANISM |
| Per-order attribution to algorithm, trader, desk, client | Nothing in the tree models an order, a desk, or a client | **ABSENT** |
| Mass cancel to a venue | No venue protocol exists (section 7) | **ABSENT** |

---

## 6. Articles 9, 11 and 28, and Content Addressing

This is the section where Codex has something structurally different to
offer rather than a better version of what exists.

### The version-identity problem

Article 9 requires an annual self-assessment of the firm's trading
systems, algorithms and strategies. Article 11 requires a designated
reviewer before any material change to the production environment, with
review proportionate to the change. Article 28 requires an HFT firm to
record each submitted order immediately in the Annex II format and keep
it five years, and Annex II Table 3 field 4 is **"execution within
firm"** and Table 2 field 6 is **"investment decision within firm"**.

Every one of these is a question about *which version of which algorithm
did this*, asked up to five years after the fact. The industry answers
it with a source control commit hash, a build number, a deployment
record, and a hope that the three agree.

### What Codex does instead

A CDX binary is **content-addressed**. Its identity is the hash of its
content, and it is Ed25519-signed. There is no build number that can
disagree with the artefact, because the name of the artefact is derived
from the artefact.

`codex/foreword/core/FactLog.codex` is an append-only on-disk log: a
512-byte-sector format with a 78-byte entry header carrying a 32-byte
content hash, a kind, an 8-byte timestamp and a content length, with a
superblock holding the log head and a generation counter.
`FactStore.codex` is the in-memory side, with facts carrying hash, kind,
content, author and timestamp, and fact kinds including
`DefinitionFact`, `ProofFact`, `VouchFact`, `PolicyFact` and
`CapabilityFact`.

| Obligation | Codex | Class |
|---|---|---|
| Art 11: identify precisely what changed between two production versions | Content-addressed binaries; the hash is the identity, and diffing two versions is exact rather than reconstructed | MECHANISM |
| Art 9: state which algorithm versions were in scope for the assessment | Same; a validation report can name hashes, and a hash cannot drift from what it names | MECHANISM |
| Art 28 / Annex II: five-year immutable order record | Append-only FactLog with per-entry content hash and timestamp | MECHANISM |
| Art 28: the record has not been altered in five years | Content hashes chain the log; the entry names its own content | MECHANISM |
| Art 9: senior management approval is attributable | Ed25519 signatures and the trust lattice, with `VouchFact` as the vouching primitive | MECHANISM + DEPLOYMENT |
| Art 9, 11: the review itself, the sign-off, the remediation | No artefact discharges this | ORGANIZATIONAL |
| Annex II Tables 2 and 3: the 28 and 31 required fields | No Annex II record type exists in the tree | **ABSENT** |

The pitch to a firm here is narrow and real: **the artefact-identity
half of MiFID II record keeping is a solved problem in Codex and an
unsolved one nearly everywhere else.** The record-format half is not
built.

### The toolchain question, which nobody asks and should

There is one more thing worth putting in front of a firm whose regulator
asks about supply chain. The Codex compiler is a **hard fixed point of
itself on bare metal**: it compiles itself end to end with no C, no C#,
no libc and no OS, and the output of that self-compile, compiled by
itself, is byte-identical to itself.

That means the Thompson trusting-trust question has an answer that is
not "we trust our vendor". A firm can rebuild the toolchain from the
seed and compare bytes. No other production trading toolchain offers
this, and IEC 62443-4-1 and the CRA are both drifting toward asking for
it.

---

## 7. What Codex Does Not Have

Read this section before believing any of the ones above. A firm that
adopted Codex for trading today would be building all of the following
themselves.

| Missing | Why it matters |
|---|---|
| **FIX protocol** | The universal order-entry and drop-copy protocol. Not in the tree. |
| **Binary venue protocols** (ITCH, OUCH, SBE, FAST, MDP3, PITCH) | Every actual market data feed and low-latency order gateway speaks one of these. None exist. |
| **Order book** | No book, no matching engine, no book-building from an incremental feed. |
| **Order, execution, position, or account model** | Nothing in the tree represents an order. `apps/market` is an e-commerce catalogue and `apps/markets` is a page; neither is a trading system, and the names are a trap. |
| **Annex II record schema** | The 28 fields of Table 2 and 31 of Table 3 are not modelled. |
| **PTP / IEEE 1588, GPS or PPS discipline** | See section 8. This is the one that blocks the RTS 25 claim outright. |
| **Kernel bypass, DPDK-class networking, NIC offload** | Codex has no OS to bypass, which is an advantage in principle, but the practice is unbuilt. |
| **FPGA target** | The transpiler targets Rust, WASM, LLVM IR and native x86-64, ARM64 and RISC-V. There is no HDL target, and the tick-to-trade tier of the industry is on FPGA. |
| **Any venue conformance test** (RTS 6 Article 6) | Article 6 requires proving interaction with a specific venue's matching logic. There is no venue to conform to. |
| **Market abuse surveillance** (Article 13) | Nothing. |
| **Exposure calculation and reconciliation** (Article 17) | Nothing. |

And the state of the network path, stated exactly, because this is where
a latency pitch would be tempted to round up:

A TCP/IP stack exists in source (`codex/os/net/`: Ethernet, Tcp, Udp,
Icmp, Dhcp, DnsResolver, NetworkStack, TcpTransport, and a TLS 1.3
endpoint with peer authentication). It runs under codex-vm's emulated
NE2K. **Bring-up on a real NIC is not finished.** The Intel I219-V
register audit is complete and the link path is proven in the bed, but
one arm deterministically wedges the machine on the real part and RX/TX
on real silicon has not been demonstrated. An I219-V is a motherboard
NIC and not a trading NIC in any case. Nobody should read "Codex has a
network stack" as "Codex has been measured on a wire".

---

## 8. Clocks: RTS 25, and the Claim We Cannot Make

RTS 25, Commission Delegated Regulation (EU) 2017/574, sets the business
clock accuracy every firm subject to RTS 6 must meet. For a firm using a
high-frequency algorithmic trading technique, the Annex table requires:

| Trading activity | Max divergence from UTC | Timestamp granularity |
|---|---|---|
| **High-frequency algorithmic trading technique** | **100 microseconds** | **1 microsecond or better** |
| All other trading activity | 1 millisecond | 1 millisecond or better |
| Voice, RFQ with human intervention, negotiated | 1 second | 1 second or better |

Granularity and divergence are two different obligations and Codex sits
differently on each.

**Granularity: satisfied, and typed.** `DateTime.codex` declares
`Timestamp` as a unit family based on nanoseconds, so a microsecond
timestamp is a distinguishable type rather than an integer with a
convention attached. `codex/os/kernel/Hpet.codex` reads the High
Precision Event Timer as a clock: it reads the tick period in
femtoseconds from GCAP_ID rather than assuming it, sets the enable bit
that firmware does not reliably leave set, and handles the 32-bit half
wrap. Sub-microsecond granularity is available and its unit is carried
in the type.

**Divergence from UTC: ABSENT, and this is a hard stop.** The tree has
an SNTPv4 client (`codex/os/net/Ntp.codex`, RFC 5905), which is pure
packet logic with no I/O attached. SNTP over a network does not
discipline a clock to 100 microseconds of UTC, and no honest reading of
that file says otherwise. Meeting the HFT row of the RTS 25 Annex needs
PTP (IEEE 1588) with hardware timestamping, or a GPS-disciplined
oscillator with a PPS input, plus a documented traceability chain. **None
of that exists in Codex.**

So the accurate statement of the RTS 25 position is: **Codex can
represent the timestamp the regulation requires and cannot yet source
it.** Half of a two-part obligation, and the half that is easier.

---

## 9. Prior Art

The comparison that matters to this audience is not against other
languages in the abstract but against what trading systems are actually
built in.

| | Memory safety | GC pauses | Bounded execution in the language | Toolchain verifiable | Venue protocols |
|---|---|---|---|:---:|---|
| **Codex** | Linear + effect + bounded types, compile-time | No GC, and `punctual` cannot allocate | **Yes, per function, opt-in, enforced** | **Yes -- hard fixed point of itself** | **None** |
| C++ | Manual; sanitizers and review | No GC; allocator jitter is the problem | No | No | All of them |
| Rust | Borrow checker | No GC; allocator jitter remains | No | No (bootstrapped through a binary) | Growing |
| Java (low-latency) | Yes | ZGC/Shenandoah reduce, never remove | No | No | All of them |
| C# | Yes | Same | No | No | All of them |
| Ada / SPARK | Yes, with proof | No GC in Ravenscar | Ravenscar profile, but **global** to the partition, and WCET needs aiT or RapiTime | No | Almost none |
| FPGA (Verilog/VHDL) | N/A | N/A | Yes, by construction of the substrate | No | Hand-built |

The row that is the actual pitch is the third one. Ada Ravenscar is the
closest prior art and it is a whole-partition profile: you adopt it for
everything or nothing. `punctual` is **per function and opt-in**, so a
firm can put the kill path, the risk check and the order-entry hot loop
under a compile-enforced bound while the strategy research code beside
them stays ordinary. That is the shape a trading firm can actually
adopt, because nobody is going to rewrite a whole book of business to
get a guarantee on the twelve functions that need it.

The row that is the actual obstacle is the last one.

---

## 10. The Pitch

To a trading firm, in the order the objections come.

**"We already pass our RTS 6 self-assessment."** You do, annually, with
a binder. The question is what the binder costs you, and what it is
worth on the day something goes wrong between two of them. Every claim
in it is an assertion with no runner. Codex is the argument that a
subset of those assertions should be compile errors instead, and the
subset is precisely the one that produces the incidents: a path to the
wire that skipped a control, a risk check that allocated, a kill switch
that had never run under pressure.

**"Determinism is what we buy C++ for."** C++ gives you no GC. It does
not give you a compiler that refuses to build a function that allocates
in a path you declared bounded, and it does not give you a
per-invocation machine-observed instruction count you can put in a
validation report. `punctual` plus `build/wcet-validate.ps1` is not a
faster C++. It is a different claim: not "this was fast when we measured
it" but "this cannot exceed N instructions and here is the observation
that failed to exceed it".

**"Our regulator will not accept a language nobody has heard of."**
Correct today, and the honest answer is that the evidence is what would
have to earn that, not the pitch. But note the direction of travel:
the CRA, IEC 62443-4-1 and the SEC's and ESMA's operational resilience
work are all converging on the same demand, which is provenance and
reproducibility of what you actually deployed. A content-addressed,
signed binary produced by a toolchain that is a verifiable fixed point
of itself is a stronger answer to that demand than anything the current
stack can give, and it is a stronger answer *by construction* rather
than by attestation.

**"What would we actually use it for first?"** Not the strategy. The
**risk layer**. It is the smallest surface with the highest consequence,
it is the part where "does not behave in an unintended manner" is
literally the requirement, and it is the part where `punctual`,
bounded integer types and effect-typed send paths do their most work per
line. A pre-trade control gateway written in Codex, sitting in front of
whatever the firm already has, is the adoption shape that is both
plausible and worth doing. The strategy code can stay where it is.

**"And what does it cost us?"** Everything in section 7. You would be
writing the FIX or binary venue protocol yourself, and the order model,
and the Annex II record schema, and you would be buying your clock
discipline elsewhere. That is a real project and this document does not
pretend otherwise. What you would get in exchange is the part of the
system that has to be right, being right in a way the build enforces,
on a substrate with no OS, no libc, no allocator and no runtime you did
not compile yourself.

---

## Cross-References

- `docs/KingsAndCourts.md` -- hard real-time, CRA, ETSI EN 303 645, IEC 62443
- `docs/ArchitectsSketchbook.md` -- allocators, decks, memory model
- `docs/OperatorsManual.md` -- `codex-vm -wcet`, build and profiling
- `codex/foreword/punctual/` -- the punctual library, 8 chapters
- `codex/foreword/core/Decimal.codex` -- mantissa-and-scale decimal
- `codex/foreword/core/Units.codex`, `DateTime.codex` -- unit families, `Timestamp`
- `codex/foreword/core/FactLog.codex`, `FactStore.codex` -- append-only fact record
- `codex/foreword/core/RateLimiter.codex`, `Fuel.codex` -- throttles and caps
- `codex/foreword/core/SessionTypes.codex` -- protocol-ordered channels
- `codex/os/kernel/Hpet.codex` -- the clock source
- `codex/test/errors/` -- the 173 refusal chapters
- `build/wcet-validate.ps1` -- the WCET gate

## Regulatory Sources

Verified 2026-08-11. Article structure and operative text read from the
consolidated text at legislation.gov.uk; the EU original is on EUR-Lex
at the same article numbering.

- Commission Delegated Regulation (EU) 2017/589 (RTS 6):
  https://eur-lex.europa.eu/eli/reg_del/2017/589/oj/eng
  and https://www.legislation.gov.uk/eur/2017/589/contents
- Commission Delegated Regulation (EU) 2017/574 (RTS 25, business clocks):
  https://www.legislation.gov.uk/eur/2017/574/annex
- Commission Delegated Regulation (EU) 2017/565, Article 19 (high
  message intraday rate)
- Directive 2014/65/EU (MiFID II), Article 4(1)(40) and Article 17
