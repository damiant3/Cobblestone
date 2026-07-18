# GitHub Update 32 -- 2026-07-06

Covers main CLs 7036-7186 (since Update 31 was pushed at CL 7035,
2026-07-04). A short cycle with four threads: blu's compiler-internals
memory campaign (the headline), one ambitious effort that deliberately
did *not* ship (demand paging), val's fireworks render tooling, and
fester's WGSL plug plus a WebGPU technique showcase. Roughly eighteen
copy-ups.

## The headline: blu's compiler memory-discipline campaign

The self-host had always been correct on a flat, survey-based arena --
each phase reserves a worst-case deck up front, and the peak reservation
during CHECK ran near a gigabyte. Blu spent this cycle driving that peak
down **without changing a single output byte**: every step is verified
against the poison harness (0xCD-fill allocator) and the hard fixed point.

The arc, in order:

- **Poison-compact green arc (CLs 7076, 7082, 7107).** The `-PoisonCompact`
  harness had been red on every seed generation (21 unresolved-type errors
  at emit) -- a pre-existing latent hole, not new. Root-caused to a
  check-tail spine hole and deep-resolve's scratch-shared subtrees, then
  closed with a structural type copier that severs shared subtrees onto the
  CHECK deck. Poison went 21 -> 0, output byte-identical. CHECK
  reservation-copy reclaim brought post-CHECK heap **585 -> 371.6 MB**,
  below even the pre-campaign 429.9 baseline.

- **Hash-consing the deep copier (CLs 7116, 7119/7120/7121).** Memo
  floor-sharing (share boxes below the keep base) plus content-level dedup
  (six kinds, full 64-bit key compare) in the memoized deep copier. Post-CHECK
  retained memory **371.5 -> 286.8 MB** (keep layer -85 MB), poison-verified
  byte-identical. The `survey-check-unit-mul` was cut 296000 -> 148000 in
  parallel; transient CHECK-era peak **1065 -> 969.4 MB**.

- **Rename-table quadratic fix (CL 7123).** An O(n^2) allocation in the
  rename table dropped to O(n log n) with winner semantics preserved, plus
  a KEEP-STAT diagnostic that reconciled the retained map (check-keep is
  15.8 MB post-hash-consing).

- **LOWER transient cut (CL 7127).** `lower-apply-normal` now reuses the
  already-resolved func-ir type instead of re-deep-resolving it, removing a
  dead transient allocation. LOWER deck **183.7 -> 117.3 MB**.

- **Survey-multiplier tightening (CL 7162).** `survey-check-mul` default
  lowered 200 -> 40 (self-compile proven safe; CHECK actually uses a flat
  ~156 MB). CHECK reservation peak **978 -> ~620 MB**. Plug builds pin
  check-mul:200 for type-dense plug source.

- **Phase-memory escape invariant + deck-liveness (CLs 7067, 7070).** A
  phase-memory instrument that walks the exact emit inputs against live deck
  segments with an always-on self-test tripwire (CDX9003-AT), which found
  and fixed two real escapes; the first measurement confirmed cdx-ir is
  fully self-contained (0 references into any earlier deck).

The net: the self-host compiles itself in dramatically less heap, and the
proof that nothing changed is the byte-identical hard fixed point plus the
green poison build at every step. Correctness preserved, waste removed.

Also from blu this cycle: the **proof totality campaign** (CL 7058) --
circular proofs now reject with CDX4023. Six adversarial routes
(self / mutual / recursive-helper / qed-sugar / lemma-self-citation /
lemma-mutual-citation) each previously proved a FALSE proposition silently;
the check-proof-circularity walk closes all six.

## Demand paging: the effort that did not ship

The counterweight to the campaign above is a project that spent a week and
a large token budget and landed nothing it set out to land. It is worth
recording honestly, because the failure is more instructive than most
successes. The full retrospective is
`docs/Designs/Compiler/Active/DemandPagingFaults.md` (blu, at Damian's
request); the design it attempted is `DemandPagedArena.md`.

**The plan** was to retire the survey system entirely -- replace the flat,
worst-case-reserved arena with a demand-paged one (a not-present-PDE trick
plus a ~6-instruction #PF handler, commit-on-touch), eliminating the ~978 MB
CHECK over-commit. Billed as industry-standard, high-in-the-training-set
work: "mostly delete the survey plus a #PF case."

**The mechanism worked on the first serious attempt.** A binary whose own
boot demand-pages self-compiled the entire compiler byte-identically, on
both codex-vm and QEMU. Real, verified engineering.

**Then it hit a wall that was never about paging.** Under demand paging,
diagnostic *messages* corrupted while diagnostic *codes* stayed correct
(battery 188/115, the failures almost all tests that print an error
string). The cause was a latent, pre-existing bug in the compiler's own
string-handling -- a dead fast-path in concatenation and a print routine
that builds a left-associative chain of temporaries each ending exactly at
the allocation frontier. The flat arena had masked it for months; paging
exposed it. It was a heisenbug of the worst kind: deterministic per binary,
invisible in isolation, and perturbed by every attempt to observe it.

**What shipped, over the whole effort:** the design doc, the post-mortem,
and four default-off codex-vm debug flags built solely to hunt the bug --
`-r10dump`, `-watchall`, `-dumpmem` (CL 7176), a `-hwwatch` hardware
watchpoint, guest-armable watch ports 0x411-0x416 (CL 7172), and a TF
single-step fix for the guest-armed page-watch (CL 7174). Sound, gated,
and nobody needed them to reach where the project already was. **Zero of
the design's five stages landed.** The survey system it meant to retire is
still in place. The engineering was shelved unsolved (blu CL 7142).

**What it did right, and what it exposes.** Main stayed pristine
throughout -- source and seed byte-identical to where they started; every
dead end lived on a shelf; the one thing that reached main was sound and
off by default. It proved the mechanism before believing it and disproved
each theory honestly. What it lacked was the tacit craft the training
corpus does not contain: theory-first-observation-never instead of
capturing the offending write first; building new instruments instead of
reaching for the sanctioned one (QEMU+GDB) that already worked; and, above
all, **no stop-loss** -- no session ever inherited the question "should
this continue?", only "next probe is X." The persistent memory that makes
the agent continuous also engineered the sunk-cost fallacy into the
workflow. The sober lesson, banked for whoever attempts this next: this
class of model is a correctness-preserving synthesizer and explorer, not
yet an autonomous closer of hard, low-feedback debugging tickets. Its
expensive failure mode is that it will not stop on its own; the mitigation
is governance -- explicit budgets, forced stop-loss, a human who owns the
go/no-go -- not a better prompt.

> The compiler compiles real programs that really work. It got there on
> the "dumb allocation stuff." That is the sentence to keep.

## The WGSL plug and gpushow (fester)

The dual-target GPU story (PTX for NVIDIA, SPIR-V for Vulkan) gains a
third target for the browser. Fester added a **WGSL plug**
(`codex/plugs/wgsl/` -- `WgslPlug.codex` + `WgslEmitter.codex`), the
WebGPU analogue of the PTX and SPIR-V plugs: it walks the same
`[Device]`-effected IR and emits WebGPU shader code, so a browser GPU
target is a plug, not a compiler change. The design note is candid about
prior art -- the SPIR-V plug was found to be a hollow syntactic walker
(it declared storage-buffer types but emitted no buffer access, entry
point, or math intrinsics), so WGSL was ported from the PTX plug, which
is where the real GPU lowering lives: kernel detection (trailing `gid`
param), storage-buffer `device-load`/`device-store`, and the math /
special-register intrinsic tables. WGSL's high-level syntax let it drop
SSA/phi entirely in favor of structured `if`/`switch`. A new
`codex/foreword/gpu/DeviceMath.codex` module backs the shader math.
This brings the plug roster to **53** (browser GPU alongside the
languages, UI frameworks, native backends, and PTX/SPIR-V).

`apps/gpushow/` is the app that exercises and shows it off -- a WebGPU
technique gallery in the spirit of the Sascha Willems / pooyaeimandar
sample set, but every demo's shader is generated from a `.codex`
`[Device]` kernel rather than hand-authored WGSL. Roughly forty
techniques ship, each as a kernel pair (`.codex` + generated `.wgsl`),
an HTML host page, and a proof screenshot captured through headless
Chrome WebGPU: triangle, cube, instancing, PBR / PBR-IBL / textured PBR,
deferred + G-buffer, SSAO, shadow / omni-shadow maps, parallax, raymarch,
raytrace, N-body, particles, bloom, Julia / Mandelbrot / plasma, gltf,
cubemap, multisample, occlusion, stencil, and more. Node tooling
(`serve.mjs`, `shoot.mjs`, `validate.mjs`) serves, screenshots, and
validates each page. The globe app's kernels were folded onto the same
path.

## Odds and ends

- **codex-vm fireworks render features (val, CL 7181).** Two opt-in,
  backward-compatible additions to the host rasterizer used by the USA 250
  fireworks demo: a long-exposure persistence clear on I/O port 0x40E
  (`gpu_fade_clear`, sparks leave fading trails) and a third additive blend
  mode (soft radial sprite). Other apps are byte-identical; the rebuilt
  `.exe` was verified booting fireworks at 60fps with the soft-sprite glow
  rendering.

## By the numbers

| Metric | Update 31 | Update 32 | Delta |
|--------|----------:|----------:|------:|
| Copy-ups | 88 | ~18 | -- |
| Plug roster | 52 | 53 | +1 (WGSL/WebGPU) |
| Active agent streams (this cycle) | 4 | ~2 (blu, val) | -- |
| Self-host post-CHECK heap | ~585 MB | ~372 MB | -213 MB |
| Self-host retained (hash-consed) | ~429 MB | ~287 MB | -142 MB |
| LOWER deck | 183.7 MB | 117.3 MB | -66 MB |
| CHECK reservation peak | ~978 MB | ~620 MB | -358 MB |
| DemandPaging stages shipped | -- | 0 of 5 | -- |

Library surface is nearly flat: blu's campaign was compiler-internal, and
the only new modules are fester's WGSL plug (`codex/plugs/wgsl/`), the
`DeviceMath` foreword module, and the `gpushow` app. The seed is
unchanged by fester's work -- the WGSL plug and DeviceMath are leaves the
compiler does not cite, so the fixed point is unaffected.

Seed at push time: `seed/Codex.cdx`, 2,106,070 bytes (~2.01 MB), SHA-256
`7CE0E86755E6489C05FD8BE9A8938FF5614CBF4E942D756AC0B81D36D57B6896`,
content hash
`C975EC8D2055C2D96DE356F12E893C7604E6A5280F408A3AC5D8D3BF59D60D49`.

## What's next

The demand-paging retrospective sets the near-term posture: the flat arena
is correct and stays, now materially lighter after blu's campaign. If
paging is revisited, the deliverable is first the latent print-path
aliasing bug -- hunted under the flat arena where the observation tax is
zero -- not the #PF handler. Continue the vision-check legs and the
emit-phase campaign behind `foreword-all-compile`. Public GitHub push:
this update plus the refreshed README seed digest.
