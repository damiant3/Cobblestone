# Reek Handoff — 2026-06-10 (ERP buildout + memory ceiling)

State of the world for the next session. Plan doc:
`docs/Designs/Apps/Erp/Active/ErpBuildout.md` (CL 3706). Execute it
phase by phase; Phases 0-4 are DONE, Phase 5 is next.

## What landed (MutableRecords stream)

| CL | What |
|---|---|
| 3706 | ERP buildout plan doc submitted |
| 3707 | ERP quire registered (quire-map.ps1 + plug-build-lib $QuireOverrides 'apps\erp'='ERP') |
| 3709 | Data quire fixes flushed by first ERP link: Server.codex demo `opening` removed (blocked every program citing Server transitively), DbServer.db-lock-table LockTable->LockState, Session `execute`->`query` |
| 3710 | erp-gl-test (first ERP test ever) + FinGL log-new -> logger-new |
| 3713 | ErpConfig chapter (AccountMap/tax/period) threaded through all 5 posting chapters + erp-posting-test |
| 3714 | Scaling conventions prose + convert-currency round-half-up. PsProject SPI "bug" from the deep dive was NOT real |
| 3717 | Stub fixes: SumOfYears (month param), units-depreciation, closed-by-default periods, treasury unmatched-book, StopLoss/CatXL ceding, COGS at material standard cost. Latent FinTreasury bugs: param named `end`, `Equity` ctor collision -> EquitySecurity |
| 3720 | Six engine KATs, all hand-computed first-run matches; IsBanking monthly-payment fixed (mr 100x off + negative denominator, never compiled) |
| 3726 | ErpScenario month-in-the-life + golden 22-line test (TB 5875000==5875000, NI 88000, equation exact) |
| 3731 | Phase 3: FinGL write-through to Codex DB (4 tables) + gl-rebuild-from-db + erp-db-test (kill-and-rebuild) |
| 3732 | codex-vm MAX_MEM clamp 2GB -> 16GB (every -mem above 2048 was a silent placebo, incl. compile.ps1's old 4096 retry) |
| 3736 | bare-metal-ram-size 2GB -> 3GB + ALL VM launch sites to >= 3072 |
| 3738/3742 | Merge-down fester leaf emission; seed rebuilt two-pass -> one-pass, self-verified |
| 3743 | Copy-up of all the above to main (on-target rebuild waived by Damian) |
| 3744 | BACKLOG: non-contiguous memory item (the real 8GB+); 3GB low-RAM note on USB validation |
| 3753 | works/Http: HttpRequest.body field, http-parse-request captures after CRLF CRLF |
| 3754/3755 | Merge-down fester IrRemInt+leaf-inliner seed E9E869A8 (built on reek baseline); copy-up. Streams verified byte-identical |
| 3759 | Merge-down blu FishTank assets |
| 3761 | Phase 4: ErpServer JSON API (7 routes) + erp-server-test (9 golden lines) |

Battery baseline: 442 total, 301 pass, **15 known pre-existing reds**
(boot-init, datetime-test, disk-facts-multi-load, disk-facts-recover,
wave3-test compile; sha256/sha512/hmac/hkdf-vector/ed25519 x2/
disk-facts-read/channel/shell-repl/shell-session output). Compare
failure SETS, never exit codes. All 11 erp-* tests PASS_EXPECTED.

## Pending

- **Copy-up**: CLs 3759 + 3761 — DONE (on main via subsequent copy-ups).
- **Phase 5 (next)**: ErpPage.codex on the WebApp quire (cites
  WebRuntime/WebTheme/WebWidgets; port recipe in
  docs/Designs/Apps/WebApp/Active/BaseTemplate.md), output
  apps/erp/web/erp.html so build-apps.ps1 discovery + check-apps.ps1
  pick it up. KPI tiles with RAG status (BwAnalytics eval-kpi-status),
  trial balance table, AP/AR aging buckets, cash position. First cut
  renders scenario data embedded (pure widget tree); live fetch
  against the Phase 4 API is optional later.
- **Phase 6**: deepening backlog in the plan doc (FIFO layers,
  multi-level MRP, SoD, MDM dedupe, BW facts, ...).

## Watch-outs

1. **REPL-batch compile flake** (two sightings, nondeterministic
   FAIL_COMPILE/no-output with no other VM jobs): reproduce the exact
   batch with `build/test-compile-batch.ps1 -ListFile
   test-output/_batches/batch-N.txt -OutRoot <tmp>` before blaming a
   change. Promote to docs/Test/ on a third sighting.
2. **`cites Encode chapter Json`** — RESOLVED. All 13 app files now
   use the correct `cites Encode chapter Json`. MarketWeb fixed.
3. **Closure size**: batch compile slots are 3072 MB; the full
   ErpScenario+Data closure fits since the 3GB seed, but keep new test
   closures lean (erp-db-test deliberately stays on the FinGL closure).
4. **Real hardware**: the 3GB seed needs 3GB contiguous low RAM —
   flagged in BACKLOG item 1 for the next USB validation.
5. **p4 clean deletes codex/plugs/html/build-output/html-plug.cdx** —
   rebuild the plug before regenerating pages in Phase 5.
6. **post-journal's hamt-set mutates shared structure in place** —
   in tests, post each entry exactly once per state binding.
7. Subledger AP/AR lists in ErpState (erp-ap/erp-ar) are still
   API-state only; create/post routes for invoices arrive with the
   dashboard's needs.

## Ceremony reminders

Gates: shelve -> revert -> sync -f -> p4 clean codex/... apps/... ->
unshelve -> inspect -> battery. ASCII-only submit descriptions.
Memory/time verdict in every CL. One CL, one concern.
