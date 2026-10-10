# GitHub Update 68

Covers main 38372 through 38414. A short security update on top of Update 67: the
three fixes that needed a new seed, the gate that now runs the degenerate-input
census, and two UOAIX save breakers. The release seed is `E29C439B` (signed and
self-verified, fixed point 3 == 4 `5E383A66`); full digests are recorded in
`TechnicalDetails.md` at release. Numbers in parentheses are main changelists.

## Security

- **FAT16 grants no longer escape through `..`**: under a `FileSystem.Read "A/C"` grant, `A/C/../E/F.TXT` read and `A/C/../E/G.TXT` wrote outside the grant, because the prefix test ran on the raw path and the walk followed the directory's on-disk `..` entry. `fat16-scope-admits`, the gate all three callers share, now refuses a path with a `.` or `..` component; `fat16-dotdot-scope` (its own minted disk) is red on the old code on 3 arms and green now (reek, in red's seed arc 38406). Still open: a grant "A" admits "AB" (a prefix without a component boundary).
- **Hardware randomness refuses instead of answering**: RDRAND was retried without a bound, with no CPUID check and no refusal of a degenerate value, so the all-ones answer some AMD parts give after resume would have become every TLS key and device-seed word. `__hardware-random-word` now checks CPUID, retries at most 10 times and refuses 0 and all-ones; callers get `hardware-random-words : Integer -> Maybe (List Integer)`; a boot without entropy marks the device seed absent, prints `no entropy` and keeps booting, and IdentityManager, the GOP wizard and the TLS server refuse to make keys from it. codex-vm now reports the host's RDRAND bit, which it had hidden while RDRAND ran natively (val, in 38406; arm `entropy-refusal`). On wasm, a failed `random_get` refuses rather than reading stale memory, graded under wasmtime with a failing host, and red when the error code is ignored (val 38414).
- **COMPILER-128**: compiling the RSA Wycheproof suite as one literal of 66,304 elements trapped the compiler at its temporary-name bound; the temporary counter now wraps at 65520 through `temp-step` (reek, in 38406).
- **The release gate runs the degenerate-input census**: `build/checks/degenerate-arms.ps1`, which lists the foreword's trust decisions and which carry a degenerate-input arm, is now a gate step (fester 38391).
- **Diagnostic stick B3 under fast ACKs**: the `sendx` loop compacts per repeat, so its heap stays bounded when ACKs return as fast as the guest sends; arm `b3-flood` (reek 38374).
- **Held for the next seed arc**: CORE-12, the SHA family refusing non-octet input, reddened 5 tests in the arc's cite-gate and was pulled; it stays on fester's shelf 38341 and is not in this release.

## UOAIX

- **A shop's lots follow its consumption**: restock-making consumed listed goods (the smith's longsword eats iron ingots) at their average quality and cost, so a lot of better-than-average units claimed more than the shop held and the next save refused with "quality total". The lot reconciler now lowers lot quality and basis as well as wear; `proofs/ShopLotValueProof` reproduces the refusal on the old code (val 38398). This was the intermittent save breaker of the first fast-clock soak (UOAIX-82).
- **The NPC economy waits for the king's seed gold** (UOAIX-86 part 3, blu 38402).

## Tools and release documents

- **COMPILER-123 part 1**: the check-sidecars generator honours `.wall` (red 38411).
- **App sweep baseline**: `UefiTownBoot` and `WorldVirtioBoot` are fragments compiled only by their proof scripts, so the Update 67 sweep's two "regressions" were listed with that reason after both assembled units compiled clean (val 38388); the Update 67 report's sweep line was corrected (root 38394).
- **Release record**: `SomethingSeenDuringRelease` for Update 67 (root 38385).
</content>
</invoke>

## Release proofs

- **Proven for this release (seed E29C439B)**: the CDX hard fixed point in one pass, the text fixed point, the diverse double-compiling witness, the poison build (2,274 subjects, no uninitialized-field read), the battery (2,274 subjects, 2,202 pass), the plug, generator (0 known drift), vm-differential, UEFI console, deck-headroom and app-sweep phases (544 clean, 0 regressions).
- **Not cleared at publication**: 10 diffusion GPU subjects exceeded their wall budgets under release load (app tier). The games wasm bundle fails to instantiate because this update's wasm programs import WASI `random_get`, which the games page host does not yet provide; the public site is not republished by this update and still runs the previous bundle. The diagnostic image ships unchanged; its rebuild with the bounded send heap follows in the next push.
