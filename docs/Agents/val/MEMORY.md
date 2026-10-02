# Val resting state

INCOMPLETE: landing file-URL repair is shelved in DEV34134; the full-snapshot smoke remains RED for Spark Studio startup (`non-exhaustive match`), owned by blu. Root parked skill tuning and made landing the top priority; no landing change has been copied up or published.

## First action and workspace

First read the latest val inbox for the graphics destination and blu's Spark control/package proof. Then inspect opened files and preserve DEV34134 before merging current main. The candidate is still OPEN on disk and fully shelved; do not blindly unshelve over it. For merge-down: shelve -f34134, verify describe -S coverage against opened files, revert only this owned CL, merge/resolve/submit, then unshelve34134 (use -f for its verified add files if Perforce leaves them on disk). Follow PerforceProcess. Rebuild affected HTML/WASM plugs before regenerating pages after a seed change.

DEV: BigWhite_Codex_val, D:/Projects/Cobblestone-val, //Codex/val. MAIN: BigWhite_Codex_val_main, D:/Projects/Cobblestone-val-main, //Codex/main. Verify .p4config/.agentgrid ownership for the new session. Own mailbox D:/Projects/.agentgrid/val. Keep backend Codex, context null, task/claims/runs accurate. No owned run, browser, guest or token remains. val-workplan.md is empty.

Handoff triggered at measured75%: AgentGrid codex-app-server observation for session01a0f50f-a38c-7f93-a09d-6e7c35236167, input621650/effective828400, compactionValid true, sampled2026-10-02T00:06:08.818Z. Use fresh Codex telemetry after resumption; do not use measure-context.ps1's Claude transcript assumptions. Root sets75%, not70%.

## Current landing scope and shelf

Root instruction inbox/464fee075721c1237a2eecd86ecfa6a2.json: every landing page must run as file:// with no page server; census fetches, embed local assets at build time using Prism's pattern, test each page, add Valheim mod card and move graphics. Damian's quote is adc1667d51fc5425b7013142717cab50.json. The graphics source/destination is UNRESOLVED: asked root in outbox/sent/landing-graphics-scope.json. Do not guess a move.

DEV34134 contains16 files: .p4ignore; build/tool-catalog.json; apps/landing/build.ps1, LandingPage.codex; new embedded-assets.js, pack-file-assets.mjs, test-file-pages.mjs; web/{landing,imagegen,imagegen-browser}.html; web/{c64,data,experimental,games,mathbook,safari}/index.html. Some opened generated pages are still unchanged. Shelf coverage was verified against all opens. The runtime/packer/grader parse with node --check; catalog JSON parses. Full build, fresh compiler/page regeneration, whole-site final proof, card layout and copy-up are NOT RUN.

The candidate puts base64 assets into shared classic JS packs under ignored web/embedded, with a compiler pack shared by GPU pages. A fetch adapter serves matching local bytes through Response and forwards external requests. Arcade ES-module imports also need data-URL import maps; fetch interception alone cannot fix file-scheme module loading. Fishtank images use data URLs so canvas pixel readback remains origin-clean. The packer injects stable loader tags and handles implicit HTML head elements (Star Map). The grader independently compares decoded bytes/raw inputs against the manifest, uses real Edge file URLs, blocks external HTTP and starts no page server. It clicks all35 arcade games and checks core outputs; it is not an all-control proof for every app.

build.ps1 calls the packer after full assembly, stages ModBuilder even with -Page, adds -KeepStudio to preserve blu's pending page, stages cited GPU source chapters, and bounds its existing recursive output removal. The full builder has NOT been exercised with these edits. Read every invoked script before running it. The first51 packaged pages/13 packs/154 entries were produced from an existing full snapshot, not from a fresh build here. Source script comments still need relevant cleanup/review. New tool records are added but the final tool-catalog check has not run for this shelf.

## Proof truth and inputs

All paths below are under DEV build-output.

- val-landing-file-before: original C64 cannot fetch c64.wasm from file; arcade imports do not initialize. The initial grader incorrectly tried the module-private GAMES global; it now drives real category/game buttons.
- val-landing-file-first: C64 and all35 arcade games pass as file URLs.
- val-landing-file-core: C64/games/mathbook pass. Its data failure was a checker assumption: the page uses preformatted results, not tbody. The checker now requires its real ten-row salary result.
- val-landing-file-all:58 pages of the snapshot were attempted;56 passed, Spark failed, and Star Map was falsely required to create a WebGPU shader. Star Map uses 2D canvas.
- val-landing-file-targeted: corrected data and Star Map checks pass. Combining unique pages across these runs gives57 passed, Spark failed. Do not call the full site green.
- val-landing-full-input/site is a COPY of D:/Projects/Cobblestone-root/apps/landing/web, then packaged in scratch. Never promote root's local CL34062 edits as ours. Its provenance.json distinguishes root's seed at capture (5AA9...) from the build log's0FD86AE43ACF3693 prefix. This is not proof of a new full build or the latest Studio artifact.
- val-landing-fetch-census.json is the initial, incomplete DEV-tree census. val-landing-source-loads.txt includes source-side GPU/fireworks/fishtank/globe/starmap fetches. Complete the census on the final assembled tree, including imports and optional remote endpoints. Prism and Studio already embed their primary payloads; common generated runtime fetch helpers can be dormant.

Spark's observed stack is val-landing-file-all/results.json, sparkstudio.html: sa_root:3429 -> spark_config_texts -> ss_music_panel -> opening. Sent to blu in outbox/sent/spark-file-startup-failure.json. Blu now owns apps/spark source/builder/proofs and all-control file-URL fixes (inbox/ca69ceead3f27e4502ef779aca7465c6.json). Preserve apps/landing/web/sparkstudio.html until shared exact-byte evidence/review; it is not open in34134. Use -KeepStudio during unrelated landing assembly. No billed call or publication is authorized.

Valheim td12 source card links modbuilder/index.html and the public source directory. The source card is added but landing.html has NOT been regenerated or visually checked. Root's full snapshot already contains the standalone ModBuilder workspace. External HEAD link checks were rejected by automatic approval review (only reason: blocked by policy); the web tool also could not access the public site/GitHub URLs. Local ModBuilder startup passed. External link status remains unverified.

Remaining acceptance: merge main safely; review/grade packer byte handling and repeatability; rebuild/regenerate our actual bundle with current compiler and -KeepStudio; exercise compiler/source buttons (e.g. cube live compile and experimental) so their deferred fetches are covered; run every final page as file; integrate blu's reviewed Studio artifact; finish card layout and clarified graphics move; run catalog/link checks and required readers; land. Never use allow-file-access-from-files or an HTTP page server to make this proof pass.

## Seed and parked work

Current depot print is build-output/val-handoff-depot-seed.cdx:5AA9EBB7601DCB33620315F0E4B0259E97BB07E44EB4F43F1D462FE377815122. DEV still has0FD86AE43ACF36939E72BC968C3A98533C8FB80DA527E05BEEC72921D551806A; merge before the next native proof. No compiler seed was promoted by val.

Skill tuning is PARKED by root. MAIN34124 landed the holdout lock before any heuristic edit or holdout game: seed90000000+97*i+c, i0..9999, c0/1, two skill-swapped games,40000 games per adjacent pair/120000 total. Root confirmed paired score/SE and explicit seed-based shuffle-deck. Old33929 numbers were gemstone-first unshuffled starts and do not describe play. No grader or tuning code has been changed. AIGameplay, Locked ladder holdout owns the exact protocol. Resume only after root restores that priority.

Latest completed Magic unit is MAIN34113/DEV34112, docs34114: General response windows plus the shipped34085 manual /activate preparation correction.312 native/177 API, mock-browser and31 callers passed on0FD86AE4; simulator outputs unchanged. Evidence val-loyalty-window-final2, -final and -callers. Crafting's baseline Material collision remains excluded; no full-battery claim. Earlier completed units must not be replayed from the stale message queue.

Compiler heap/time behavior is unchanged by this handoff document.
