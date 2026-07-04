# GitHub Update 31 -- 2026-07-04

Covers main CLs 6480-6993 (since Update 30 at CL 6479, 2026-07-01).
Three days, 88 copy-ups, four agent streams (fester 42, blu 34, val 13,
reek 1).

## The Vision Check -- Claims Become Compiler Errors

The headline is a campaign, not a feature. The founding documents say
Codex prevents whole classes of bug **by construction** -- use-after-free,
resource leaks, undeclared effects, silent integer truncation. For much
of the project those were promises the type checker described but did not
fully enforce. Update 31 is where the promises became compile errors.
Blu drove an adversarial-probe methodology: for each claim, write the
program that *should* be rejected, confirm the compiler currently accepts
it (the hole), then close the hole and flip the probe to a `.failing`
test that pins the rejection.

**Effect rows -- complete (CL 6574).** Effects are now first-class,
inferred data. Every arrow carries an effect row; higher-order functions
are effect-polymorphic through row variables (`map`/`par`/`race`/`fork`,
ListUtils, Collections, Iterate); the checker judgment carries an ambient
row with Koka-style shared-tail unification. The readers enforce it:
undeclared ambient effects (CDX2033), definition-boundary violations
against the dotted lattice (CDX2031), argument-boundary row unification
(CDX2090), handler discharge with clause-body inference (CDX2092), and a
rejection of effect brackets wrapping function types in value signatures
(CDX2094). The five stage-0 laundering probes (list-map, record field,
lazy, fork, handler clause) flipped from documenting holes to rejecting
them.

**Linear ownership -- complete (CLs 6846, 6863, 6873, 6888).** Linear
types now follow ownership through the whole program. A `let`-alias is a
move; the original name is dead afterward (dead-name diagnostics).
Ownership crosses call boundaries only through a `linear`-declared
parameter (CDX2065) and returns only through a `linear` return type
(CDX2066) -- `freeze` is the sanctioned door, by signature. Captures and
containers own what they stash: a capturing closure is call-once, a
handler clause or escaping closure may not capture a linear at all
(CDX2067). All nine adversarial laundering routes are closed; the ruling
is ownership-move semantics (Rust-like), chosen because multiplicity
fails the uniqueness promise behind copy-free `freeze` and in-place
update.

**Bounded signatures -- complete (CLs 6646, 6657, 6666, 6697, 6765).**
Bounded-integer parameters and returns used to be cosmetic -- `inc-byte`
declared `Integer between 0 and 255` would silently return 301. Now the
narrowing discipline that already guarded record fields runs at every
function boundary: static rejection of out-of-range literal arguments
(CDX2050), advisory on wider sources (CDX2051), proven-safe elision
(CDX2053), and Eiffel-style runtime precondition/postcondition guards
(UD2 trap on violation) where the value cannot be proven at compile time.
The self-host's own CDX2051 count went 66 -> 0 and the diagnostic was
promoted from warning to error. The static bounds prover was extended in
parallel (BoundsProverReach slices 1-3, CLs 6615/6628/6638): literal
constants, builtin return ranges (`list-length`, `__deck-pos`), and
let-local range flow now prove enough to elide the runtime check.

**Capabilities -- hardware effects (CLs 6897, 6925, 6955, 6968).** Port,
block, and MMIO intrinsics now carry `Device.Port` / `Device.Block` /
`Device.Mmio` effects, with a quire-level trust boundary: the owned stack
(compiler, OS, native backends) is exempt, forewords and apps are
enforced. Foreword device drivers were act-converted to declare the
effect they use. A program that pokes hardware without declaring the
capability no longer type-checks.

**Punctual -- tightened (CL 6976).** The real-time-safety walk was made
exhaustive: it now covers handler clauses and match guards, rejects
computed-head calls (CDX6001), and flags any ambient effect row in a
punctual body (CDX6004), with a tightened allowlist (only emitter-proven
O(1) builtins like `list-at`/`list-length` stay).

Every one of these ships with its probe catalog green in both directions
-- the sound programs compile, the unsound ones are rejected with the
right code.

## IoT Protocol Build-Out

Fester turned the encode quire into an industrial-protocol library. Each
module is a leaf the compiler does not cite (no seed change, fixed point
unaffected), verified with byte-exact known-answer tests against
independent reference encoders:

- **Field/industrial bus**: Modbus (RTU/TCP/ASCII, LRC + CRC-16),
  DNP3 (data-link + application, CRC-16/DNP), BACnet/IP (+ WriteProperty),
  KNX / KNXnet-IP, J1939 (29-bit CAN), CANopen (CiA 301), M-Bus
  (EN 13757), OPC UA (SecureConversation + OPN/GetEndpoints), IEC 104,
  EtherNet/IP, S7comm, Melsec, FINS, GOOSE, HART.
- **Wireless / mesh**: LoRaWAN (uplink, OTAA join with CMAC/ECB session
  keys, downlink, join-accept decrypt), Zigbee (NWK + APS), IEEE 802.15.4
  MAC framing, 6LoWPAN, BLE ATT/GATT, MQTT-SN.
- **MQTT / cloud**: Sparkplug B (industrial MQTT with float/double via
  RealBitcast), MQTT string-encoding fix verified end-to-end against
  mosquitto 2.1.2 (CONNECT/SUBSCRIBE/PUBLISH round-trip -- the wire bytes
  had been CCE codes, not ASCII/UTF-8), SNTP time sync.
- **Crypto + HAL**: AES-CMAC (RFC 4493), and a RS485/RS232 SerialLine HAL
  where the bus grant is a linear token released exactly once and a
  half-duplex violation is a compile error -- the linear-types work put to
  use.

## RealBitcast, RISC-V Plug Repair, and the Plug Gate (val)

**RealBitcast intrinsics (CL 6708).** `real-to-bits` / `bits-to-real` /
`real-approx-to-bits` / `bits-to-real-approx` -- f64/f32 IEEE-754 bit
access. Found and fixed a missing REX prefix in the `movd` encoders (was
silently reading the wrong register for R8-R15). Mirrored into the ARM64
and RISC-V plugs (CL 6728). Unblocked Sparkplug B float/double metrics.

**RISC-V plug repair campaign (CLs 6755, 6778, 6822, 6848, 6939).** The
RISC-V cross battery went from 0 booting to 132 passing on the committed
Renode board. Root causes fixed: a 5 GB boot stack pointer on a 1 GB
board (every test aborted at PC 0), a string-literal-drop from NOP
compaction deleting a rodata-fixup slot, Real-literal arguments
collapsing two operands into one register, a nested-let heap-binop
local-slot reclamation bug, a per-function TCO-state leak, and a
spill-slot register overflowing the RISC-V encoder into the funct3 field
(emitting `slti sp` and destroying the stack pointer). The QEMU RISC-V
cross path was also fixed so the battery runs on both emulators and
agrees. Renode was moved to a box-wide install resolved through
`build/renode-config.ps1`.

**Plug build gate (CL 6954).** `build.ps1` now builds the binary backend
plugs (riscv/arm64/elf/pe/img) with the signed SUT after BVT and hard-
fails on codegen errors (~25s). Plug CDX is untracked and was never gated
-- so a compiler change that tightened checks could dark-ship broken
backends. It did exactly that: blu's CDX2031/2051/2070 tightening (CL
6863) had silently broken all 52 plugs; val repaired every one (CL 6939)
and added the gate so it cannot recur silently.

**itoa minint fix (CL 6987).** `show` of the most-negative Integer
(-9223372036854775808) produced garbage: `__itoa` negated into a
magnitude, but `neg(minint)` stays negative, and the signed divide then
walked off. Fixed by dividing the magnitude unsigned. Rebuilt seed,
verified a one-pass hard fixed point on the main workspace.

## Memory Discipline -- the FabledTreasureMap (fester)

A campaign to drive the self-host's CDX2051 warning count down without
weakening the type contracts. Sum-constructor fields now pack by declared
width like record fields (CL 6544); a `PatchEntry` of four bounded bytes
collapsed to a single 32-bit value (CL 6567, seed shrank ~3 KB). When an
early slice widened load-bearing phase-allocator bounds to silence
warnings, the ruling was to **revert** (CLs 6580, 6592): those bounds are
domain documentation plus an automatic store-time check, and the CDX2051
they raise is a missing-static-prover signal, not client code to silence
by deleting the contract. This ruling is what the BoundsProverReach work
above answers directly.

## Odds and Ends

- **Idea Forge (val, CLs 6610, 6618)**: a webservice app (`apps/ideas`)
  with a reservation protocol, plus HTML-plug emitter fixes that benefit
  every page app -- a JS reserved-word sanitizer, the IR `/index` field
  suffix strip (was emitting JS division and corrupting themes), and five
  missing runtime builtins.
- **DynamicSurvey Phase 1 (fester, CL 6993)**: `compile.ps1` auto-retries
  on a CDX9002 deck overflow instead of failing.
- **CDX2053 noise (fester, CL 6991)**: trivial single-point proven-info
  is now silent; real-interval proofs still report. Self-compile CDX2053
  count 367 -> 14.
- **QuartermastersMap (blu, CL 6817)**: a delegation map of scoped "digs"
  (sweeps, plug ports, harness fixes) coordinating the crew.

## By the numbers

| Metric | Update 30 | Update 31 | Delta |
|--------|----------:|----------:|------:|
| Library modules | 508 | 533 | +25 |
| Foreword modules | 371 | 396 | +25 |
| Encode quire | 35 | 63 | +28 |
| Battery (full) | 207/207 | 296/0/15 | +89 |
| Copy-ups | 46 | 88 | +42 |
| Agent streams | 5 | 4 | -1 |
| BY-CONSTRUCTION claims enforced | partial | linear + effects + bounded + capability + punctual | -- |

Seed at push time: `seed/Codex.cdx`, 2,068,576 bytes (~1.97 MB), SHA-256
`8CA1E63BA4DEE7F7CA80821C490A7EE625E03A425345669E558FE29D70ABABF1`,
content hash `8E4B20BDE30D72446E29D5B07B6CB62DAC459A6176938DEE001AD0080D148F73`.

## Addendum -- foreword-all-compile and the first emit fix (through CL 7026)

Since the cut, the frontend blockers for `foreword-all-compile` (the test
that compiles every foreword together) were cleared and the first
emit-phase bug behind them was fixed:

- **Matrix4 unary-neg (fester CL 7002).** `-(vec3-dot ...)` mistyped as
  Integer because unary-expr synthesis read `is-real-type` on an
  unresolved unification variable; the one-line fix deep-resolves the
  operand type first (the pattern the adjacent field-access arm already
  used). Genuinely-ambiguous vars still default to Integer.
- **CDX2051 silent-narrow sweeps (fester, through CL 7014).** The last
  frontend blockers -- narrowing sites across ~14 core/game/ai modules
  and the ParticleSystem foreword -- closed with `__narrow`-at-store.
  The all-foreword concat now type-checks and reaches EMIT.
- **Minimal-leaf mispredict (fester CL 7025).** At EMIT the concat hit a
  pre-existing codegen halt, `CDX2000 minimal leaf mispredict`. A pure
  leaf shaped `let x = e in if c then lit else x` took the compact
  register-only fast path, but its local predictor under-counted the two
  scratch locals a value-position `if` allocates (the compare-left spill
  and the result), so the post-emission self-check halted rather than
  emit a spill the frame-less path has no room for. The fix teaches the
  predictor to charge those two, so an over-four-local shape is routed to
  the standard emitter (which sizes the frame dynamically) instead of
  mispredicting. A first attempt -- fall back to the standard emitter
  after the fast path already emitted -- passed every gate (one-pass
  fixed point, full battery, self-verify) yet silently hung the shape at
  runtime; the lesson, banked, is to decline the fast path up front, not
  emit-then-rewind. Proven end-to-end: the repro runs (3/0/0), not just
  compiles. `foreword-all-compile` stays skipped pending the remaining
  emit shapes -- the self-check now halts loudly on any it does not yet
  model, so nothing silently miscompiles.

Also landed: blu's capability stages (derived manifest + OS wiring) and a
claims-calibration pass (CLs 7009, 7012); reek's Magic card-format reorg
(CL 7020); and a round of file moves (CL 7026).

## What's next

Close the remaining vision-check legs (capability manifest wiring,
punctual coverage unification). Continue the RISC-V plug repair toward
full cross parity. Continue the emit-phase campaign behind
`foreword-all-compile` (the minimal-leaf mispredict was the first; the
loud self-check will surface the rest). Public GitHub push: catch up
README and top docs, refresh the seed digest, ship.
