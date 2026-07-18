# GitHub Update 22 -- 2026-06-10

Covers main CLs 3254-3758 (since Update 21 at CL 3253, 2026-06-06).
Four days, ~77 copy-ups from six agent streams.

## x86-64 Codegen: the optimization campaign concludes

Sixteen optimization CLs took the four micro-benchmarks from interpreter-
class to native-class. Function-body instruction counts (full history in
`docs/Designs/Compiler/Active/CodegenAnalysis.md`):

| Bench | Update 21 | Now | C /Od | C /O2 | C#/F# JIT |
|-------|----------:|----:|------:|------:|----------:|
| fib   | 53        | 23  | 19    | 20    | 21        |
| fact  | ~43       | 17  | 16    | 15    | 15        |
| gcd   | ~50       | 23  | 18    | 14    | 9         |
| sum   | ~48       | 14  | 20    | 23    | 4         |

**sum-to-N now beats native C at every optimization level.** fact is one
instruction from C /Od. fib is within two of the .NET JITs. All numbers
are from the gate battery's stage1 (the self-applied compiler) -- every
optimization ships as a self-verifying hard fixed point.

The structural changes, in order:

- **Destination-driven emission + immediate ops** (CLs 3284-3400, 3518,
  3542): emit-to-local writes results directly to target registers;
  add/sub/mul/cmp with literal operands skip materialization; prologue
  NOP elision; per-compilation stack-guard flag.
- **emit-binary-reg-right + eval-tail-arg-direct** (CLs 3575-3606):
  register-local right operands skip the alloc-local park; TCO tail
  args evaluate straight into register temps.
- **TCO direct arg shuffle** (CL 3649): tail-call sites plan a
  dependency-safe parallel move straight into the parameter registers.
  sum's loop body is `add r12,rbx; lea rbx,[rbx-1]; jmp` -- F# JIT
  density. Seed -11,255 B.
- **Staged binary operands** (CL 3663): binary expressions no longer
  consume locals at all. Operands stage through R8/R9 (registers the
  allocator never assigns) with at most one transient machine-stack
  push/pop; named bindings stay in callee-saved registers instead of
  cascading to stack slots. Seed -37,403 B (-1.8%) in one CL.
- **Minimal leaf + near-leaf emission** (CLs 3695, 3702): functions that
  provably allocate no spills and need at most four register locals pay
  almost no frame ceremony -- exact pushes, no frame pointer, no frame
  placeholder, no NOPs. Pure leaves (no calls; tail self-recursion is a
  jmp) also skip the stack guard; near-leaves keep it (the guard chain
  through recursive calls is the heap-collision detector). The
  per-function ceremony floor for small helpers drops from ~17
  instructions to ~5.
- **IrRemInt + leaf inliner** (CL 3746): new IR op emitting idiv and
  reading the remainder from RDX -- exact truncated semantics for all
  inputs. A conservative leaf inliner substitutes pure-arithmetic
  callees at name/literal call sites and folds the X - (X / Y) * Y
  idiom to IrRemInt: math-mod call sites become a 4-instruction inline
  remainder. gcd's loop is mov/cqo/idiv/mov plus the TCO shuffle, in a
  guard-free pure-leaf frame.

Hard lessons, documented in KNOWN-CONDITIONS and the CodegenAnalysis
attempt log: an int-mod fold was rejected before shipping (int-mod is
floor mod with a sign fixup; the idiom is truncated remainder -- they
differ for negative dividends); the inliner originally deleted
deck-record allocation wrappers (deck-record's definition is the
identity, but the emitter intercepts the name -- region semantics
invisible at IR level); and IR-pass CLs must run the battery against
their own stage1, because pre-gate batteries on an old-seed SUT never
exercise self-application.

## Compiler and language

- **Mutable records** (MutableRecords stream): `mutable` keyword,
  in-place field assignment with unique-ownership enforcement
  (CDX2060/CDX2062), borrow-vs-move inference, `freeze`.
- **omit-frame-pointer + stack-guard flags** (CL 3412): per-compilation
  codegen toggles.
- **int-mod register-locals fast path**, standalone cmp-imm, imul-rri.
- **Parser upgrades** (MathBook stream): implicit multiplication, float
  literals, matrix syntax.
- **C# plug emits the full compiler**: 2,376 defs compile under
  `dotnet build` with 0 errors. Plug build refactor dedups ~3,000 lines
  across 48 build scripts into a shared plug-build-lib with DFS
  topological foreword resolution.
- **Build harness**: process-CWD anchoring fix (gates now run correctly
  from any shell against any workspace); quire-map single registry with
  DFS resolver wired into bundle-app/compile/test-compile.
- **codex-vm**: INPUT_BUF_MAX 16 MB; crash diagnostics with symbol
  resolution, disassembly, backtrace; durable IDE writes.
- **Memory ceiling raised to 3 GB**: bare-metal-ram-size 0x80000000 ->
  0xC0000000, exactly at the PCI MMIO floor -- the contiguous low map
  is now fully used. Non-contiguous memory (the real 8 GB+) is the
  tracked follow-up. ArchitectsSketchbook updated to match.

## Applications: 330 -> 628 modules across 46 apps

The headline: a full **ERP suite** (23 modules) built in three days --
GL with double-entry and 24-account COA, AP/AR with 3-way match and
aging, Materials (PO/goods receipt/inventory), HR (payroll, benefits,
time, recruiting), Treasury (cash, liquidity, debt, intercompany),
Controlling (cost/profit centers, product costing, CO-PA, ABC), Sales
and Distribution (pricing, quotes, orders, delivery, billing),
Production Planning (MRP, BOM, routing, capacity), Plant Maintenance,
Quality, Warehouse, Project Systems, BW analytics -- plus five industry
verticals: Real Estate (leases/CAM), Banking (deposits/loans/Basel
III/LCR), Insurance (policies/claims/reserves/reinsurance), Utilities
(meters/rates/outages/SAIDI), Healthcare. Phases 0-3 of the config
buildout thread ErpConfig (account map, tax, posting periods) through
all five posting chapters. The works/Http server gained HTTP
request-body capture (POST support) as the ErpServer prerequisite --
the suite is heading for a served web UI on the CDX HTTP stack.

New platform apps:

- **Browser** (22 modules): navigation shell, trust model, media
  player, data channels, content addressing -- the IPFS-CID +
  capability-security research writeup ships in the design doc.
- **FileShare** (8): content-addressed sharing -- 256 KB
  Merkle-verified pieces, Kademlia DHT with Ed25519-signed announces,
  rarest-first selection, tit-for-tat choking, trust-weighted peers.
- **Secrets** (8): AES-GCM vaults, PBKDF2, hash-chained audit log,
  team sharing via Diffie-Hellman, 10 secret types.
- **Diagram** (13): flowchart/ERD/UML/network/state-machine editors,
  edge routing, marquee selection, undo/redo.
- **Globe** (8): 11 live data feeds (aircraft, quakes, weather, ISS,
  wildfires, storms, volcanoes, solar flares, ocean buoys) plus GIS
  mode: road network, route planner, POIs, geocoding.
- **Star Atlas** (7): deep-sky catalog (125 objects), constellation
  stick figures, planetarium rendering.
- **MathBook** (17): symbolic CAS -- calculus, number theory, circuit
  analysis, proofs, stats -- with the parser upgrades to match.
- **Market** (17): full e-commerce -- products with flexible options,
  cart/tax/payment, shipping zones, coupons, reviews, digital
  products, bundles, subscriptions, four auction types with
  proxy/snipe bidding, merchants, drop-shipping, affiliates.
- **Workflow** (4): state-machine engine with SLA tracking, document
  gates, audit trail; title insurance, auto claims, and mortgage
  origination flows.
- **CVMM** (66): a full OS desktop environment -- system managers,
  productivity suite, composable monitoring, sync providers.
- **Vision / Helm / Collab** (13/12/8): computer-vision pipeline,
  operations bridge console, collaborative editing.
- **NetTool** (6): packet analyzer, port scanner. **Radio** (3): DJ
  console with dual decks and Web Audio.
- **FishTank** (14): boids AI at 1000 fish, particle systems, WebGPU
  bridge, WASM build -- plus a 3D creature pipeline: a creature
  database of 8 species (clownfish, angelfish, blue tang, ...) with
  GLB meshes, reference imagery, diffuse/normal/alpha texture sets,
  mesh decimation, and vertex colors flowing through the WASM plug.
- **Mesh OS layer** (codex.os): Raft consensus, SWIM gossip, health
  checking, load balancing, service proxy.
- **TimeService + Revocation** system services: RTC+NTP+HPET wall
  clock with TOTP; six Ed25519-signed revocation types replacing
  CRL/OCSP with trust-lattice evidence.
- **Parental controls**: guardian/child hierarchy, PIN auth, age
  policies, time limits, content filtering.
- **WebApp base template** (3): WebRuntime/WebTheme/WebWidgets quire
  with **20 Page apps** ported onto it -- chat, mail, music, notes,
  weather, tasks, photos, maps, news, podcasts, books, recorder,
  capture, publisher, imagetools, fitness, pomodoro, piano, markets,
  calendar.
- **Spark** grew to 89 modules: CAD workbench (Part/Sketch/Measure,
  ortho views, DXF/STL), Codex Designer (WYSIWYG UI builder), KvStore
  data layer, UV editor, audio engine (synth/ADSR/effects), timeline;
  the JS shim shrank from ~1100 to ~900 lines as panels migrate to
  Codex-generated WASM.
- **CodexMagic**: prismatic mana, deck tester, CDX-first API routing,
  clans, onboarding, marketplace, widget-tree pages, state
  persistence, parental-control integration, mobile app (8 modules).
- **App persistence layer**: AppPersist/AppLog kernel modules; all
  apps persist state across restarts.

## Stats

- Seed: 2,094,667 bytes, SHA-256
  `E9E869A80630BD35C62B42CF08997601C8306EC70C16D9917D475372348935AB`
- CDX hard fixed point on bare metal (SUT === stage1, one pass);
  battery 212 total / 202 pass / 0 fail / 10 skip, run against stage1
- 1,126 modules: 54 compiler, 238 foreword, 93 OS, 113 plug,
  628 application
- Seed trajectory across the codegen campaign: 2,191,873 ->
  2,094,667 (-4.4%) while adding leaf profiling, staged operands,
  TCO planning, and the inliner to the emitter itself
