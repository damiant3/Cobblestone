# fester — workplan

## Status 2026-07-07: trust domain revived, probe rungs 1-2 shipped, RESTING

Lane: TheLongFlight (docs/TheLongFlight.md — the north-star plan,
read it) — currently Ascent II (Permission) + Ascent V (Seed) rungs,
plus general bring-up of the dormant trust/verify test surface.

Shipped to main today (all gated, streams identical at session end):
- docs/TheLongFlight.md — five-ascent north star (7237).
- PolicyProse v0: policy prose -> PolicyFact, Clarifier reflection,
  simulation through PolicyEngine (7239). Ascent II rung 1.
- Trust-domain revival (7246): CDX2051 bounded-store sweep across 20
  os modules, 41 apps tests verified green, 31 stale ".skip stub"
  sidecars deleted, 3 frozen-bug expecteds refreshed. Real fixes:
  Clarifier sort stability (<= -> <), SessionStore dead-at-expiry-tick,
  CdxVerifier clamps untrusted le16 cap fields to invalid-sentinel
  (graceful deny, not trap).
- Lease expiry boundary unified with sessions: dead AT the tick (7250).
- Harness: .disk sidecars run from a writable temp copy (7253); batch
  driver no longer stamps 'CDX repl' on test binaries (7257).
- SEED (7262, digest 6CFF0CC766310F4B02BF28F5A6E4ACC4): 'map' mode
  flag — symbol-map emission split out of the repl flag. ANY script
  hand-crafting a mode header must send 'CDX map' to get a MAP block;
  compile.ps1 adds it automatically for non-repl CDX. Batch test
  binaries are Exit-mode and halt on their own.
- WakeCeremony (7265): codex/os/verify/WakeCeremony.codex — capsule
  self-verification + introduction-or-refusal. Ascent V rung 2.
- Merge-down 7272: blu's NoAliasCodegen 0-2 + handoff skill absorbed;
  full gates green one-pass on blu's seed D2D10A3275A2A0D1EB432823A7F225BD.

Battery baseline (-Apps): 447 pass / 28 fail / 92 skip in ~410 s.
The 28 fails are pre-existing (9 erp-* + 19 others, list in
ExaminersAssay-adjacent memory); hold copy-ups to this baseline.

## OTHER AGENTS

- The 'map' mode flag (above) is the one behavior change that can
  touch you: bare "CDX" requests no longer emit the MAP block.
- blu: your battery phase-1 "VM died in batch" retry count varied
  6 -> 2 -> 0 across three same-day runs, different tests each time —
  consistent with your input-size/alignment crash firing at unlucky
  cumulative offsets in batch slots. The died-batch input files may be
  a free repro corpus.
- Expected files refreshed today (event-bus-test, capability-audit,
  access-control, trust-lattice, os-full-boot-demo, os-integration-demo)
  had frozen OLD-BUG outputs; if a merge resurrects them, actual is
  right, expected was the bug.

## Next

1. Ascent V rung 3: wire wake-ceremony into apps/works UefiBoot/
   FirstBoot over the real seed from the FAT partition; then Time
   Capsule v0 (build-boot-img embeds source + ceremony-on-boot).
2. Classify the ~58 remaining "stub"-skipped apps tests (networking,
   annotations, replay, gguf/neural/inference — the Ascent I leads).
3. Ascent II rung 2: [Negotiate]/[Supervise] builtin effects;
   LeaseManager quota counters; Clarifier feedback loop into
   ManagedAccounts enforcement.
