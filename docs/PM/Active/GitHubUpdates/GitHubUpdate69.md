# GitHub Update 69

Covers main 38415 through 38905. `fail` now unwinds to the nearest `try` on
every native target and on the source plugs that have a runtime on the box, a
self-call in a `when` arm keeps its tail position, and UOAIX completes Magery
and hides what a client must not see. The release seed is `58D18336` (signed
and self-verified); full digests are recorded in `TechnicalDetails.md` at
release. Numbers in parentheses are main changelists.

## Compiler

- **COMPILER-129, `fail` unwinds to the nearest `try`**: on x86-64 a `try` builds a frame on its own stack, the chain's head lives in the running process's table entry, and a `fail` restores the frame and jumps to its handler, or prints its message and traps outside every `try` (red 38511). arm64 and riscv keep the frame in spill slots of the `try` function and wasm checks a failure flag after every call in a module that uses `fail` (red 38553). The source plugs raise the message: Python and JavaScript (red 38565), TypeScript (val 38752, from fester's shelf), Java graded under JDK 17 (fester 38784), C# graded under dotnet (fester 38793) and the HTML plug graded in headless Edge (fester 38802). The plugs with no runtime on the box are named as gaps in COMPILER-129's row. Arms: `fail-unwind`, `fail-unwind-deck`, `fail-outside-try`.
- **COMPILER-124, a self-call in a `when` arm is a tail call**: every `when` branch body keeps the match's tail position, so a serving loop written `when x is Ok (_) -> self` inside `act` runs flat instead of growing one frame per round (reek 38797).
- **COMPILER-109**: `Lowering` and `opening` lines over 128 columns rewritten with `let` bindings; program bytes unchanged (reek 38748, 38758).
- **CORE-12**: the checked SHA twins refuse non-octet input, binary-neutral (red 38524, fester's design); test-only keypair seeds and fileshare digests are held as bytes (fester 38681, 38719).
- **Plugs**: a WGSL helper with two buffer mappings is refused by name (reek 38639); the evidence plug's JSON strings go through `json-quote` (reek 38626).

## UOAIX

- **Magery is complete**: all 64 spells are built, the last stages being the summons (Blade Spirits, Summon Creature, Energy Vortex, the elementals, Summon Daemon), Resurrection, Polymorph, Gate Travel, Invisibility, Reveal, Dispel Field, Dispel and Mass Dispel (fester 38431 through 38661).
- **What a client must not see (UOAIX-84)**: a concealed mobile's events reach no other client, ghosts are unseen by the living unless revealed, the packet filter drops packets naming hidden or unseen mobiles, and a sound at an unrevealed ghost's tile is dropped for the living (red 38586, 38604, 38630, 38649; blu 38712).
- **Economy**: shops trade their silver up to gold each town step (UOAIX-96, blu 38652); smaller coin trades up at the treasury (UOAIX-92, blu 38590); craft and restock reconcile every lot of a shop (UOAIX-91, val 38468); an occupied town entrance is reached at a free tile beside it (blu 38532).
- **Farming**: the grapevine bed (UOAIX-58, val 38621); a planted bed is reaped, tended, watered or dug only from within 2 tiles (blu 38693).
- **Proofs and soaks**: soak-bot trigger families and a graded replay (UOAIX-82, reek 38542, 38582, 38731); a stale world counted by `CompositeReplay` boot 2, bisected and fixed (UOAIX-94, val 38742); `CivicLiveReplay` runs a 256-slot world (UOAIX-95, reek 38695); soak-bot vendor carts reach the menu's vendor (UOAIX-49 B, val 38808).

## Tools and release documents

- **COMPILER-123**: the `.wall` run-budget sidecar is honoured by the BVT's generator (red 38417) and by `test.ps1`'s phase 2 (red 38787).
- **Games page host**: provides WASI `random_get`, which Update 68's wasm programs import (red 38509).
- **degenerate-arms** ignores concatenated units and is written in the typed idiom (fester 38425, 38428).
- **Release record**: GitHub Updates 55 to 65 moved to `Done` (root 38777).

## Release proofs

- **Proven for this release (seed 58D18336)**: the full gate, including the CDX hard fixed point in one pass with the gate's compiler byte-identical to the depot seed, the text fixed point, the full test-compile (2,012 chapters) and test-run, the plug, generator (0 drift), deck-headroom, wasm and browser phases; the full app sweep (546 units, 543 clean, 3 known-dirty, 0 regressions); IR fidelity (0 unexpected); the diverse double-compiling witness (the Roslyn arm and the Codex arm both 3,883,795 bytes, 0 differing bytes outside the signature region); the battery `-Tier all` (2,280 subjects: 2,199 pass, 63 skipped, 18 failed); the poison build (2,280 subjects: 2,202 pass, 15 failed, no uninitialized-field read); `diag.img` rehearsed on all 60 arms on both beds; `seed/Codex.map` matches the seed's embedded MAP1 on all 6,480 rows.
- **Not graded at publication**: every battery and poison failure is a GPU diffusion subject over its wall-clock budget on a box loaded with other work; this cycle changed no GPU code, so those runs say nothing about it.
- **Box profile** (`docs/Agents/box-release-2026-10-07-u69.csv`): free memory bottomed at 14.95 GiB during the battery's batch compile at 16 guests; the peak was 17 guests during the poison battery's run phase, 194 MB working set per guest.
- **Fixed during the release**: the QEMU fallback under WHPX now boots `-cpu host`, because its default CPU model has no RDRAND and every guest it booted printed `no entropy` first (red 38873); `cover-arms` is marked bare-metal, because the `cover` flag exists only in the x86-64 code generator (red 38905). `t5-encoder-step` passes inside its `.wall` budget (COMPILER-123).

