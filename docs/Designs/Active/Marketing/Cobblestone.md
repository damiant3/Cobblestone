# The Cobblestone Project -- the rename campaign

*Opened 2026-08-24 (red, from Damian's direction). One week, culminating in
a public push that carries the rename, the prism/REPL surface, and the new
home page.*

## The idea

"The new repository" was always the idea, not the moniker. The project's
public name becomes **the Cobblestone Project**: the OS and everything
branded outside the compiler becomes Cobblestone, and the language and
compiler stay **Codex**. The story the two names tell together is the
campaign's spine: **Cobblestone is the path; Codex is the stone it is paved
with.** (Cairnstone was considered and set aside as less obvious.)

The mood reference is Damian's cobblestone-path painting (an oil-textured
road through green hills toward a sunrise), at
`C:\Users\Damian\Pictures\Screenshots\Cobblestone.png` on the dev box. The
home page turns it into a parallax scroll. The asset is not yet in the
depot; it enters with the home-page work and ships with the culminating
push.

## Ruled by Damian, 2026-08-24, before this doc was written

1. **The GitHub repo is RENAMED IN PLACE** to **`damiant3/Cobblestone`**.
   Not a fresh repo: a rename keeps stars, issues, and Steve Howell's PR
   history, and GitHub installs permanent redirects from every old URL and
   clone remote, so `GitHubUpdate*` links and outside clones keep working.
2. **Artifacts keep their names.** `seed/Codex.cdx`, `seed/Codex.img`,
   `codex-vm`, `.codex`, CDX, CCE, the CDX diagnostic codes: all unchanged.
   Damian: "that is a layer deeper than this campaign envisions." The
   campaign lives at the strings-and-docs layer.
3. **The README masthead is DUAL-BRAND**: Cobblestone / built on Codex.
   The GitHubUpdate series keeps its name and numbering.
   (2026-08-25, main 19444/19447: README.md split into the business page,
   from Damian's draft, keeping the dual-brand line; every measured claim,
   digest and count moved whole to `TechnicalDetails.md`, and
   `check-doc-counts` plus the release skill were repointed there.)
4. Prism's scope ruling (same day, recorded in CurrentPlan) is a sibling of
   this campaign: the REPL surface is part of the culminating push.

## The boundary: what renames and what does not

**Becomes Cobblestone (the brand layer):**

- The GitHub repo name and the GitLab mirror description (GitLab repo
  `damiant3/Codex` keeps its slug this week; a follow-on can rename it, it
  is a backup nobody reads and not worth campaign risk).
- The OS identity on every live surface: desk chrome, boot ceremony,
  VGA banner, wizard, web app mastheads, the landing page hero.
- The public framing: README masthead, `UsersHandbook.md` where it names
  the OS, `CuratorsCatalogue.md`, the home page.

**Stays Codex (the language layer):** the language, the compiler, every
artifact and tool name (ruling 2), and every "Built with Codex" credit
line. A footer that says "Built with Codex" is CORRECT after the rename;
only brand labels change.

**Out of scope, deliberately, and not to be swept:**

- **Archives.** `docs/PM/Done/`, `docs/Designs/Done/`, `old/`, shipped
  GitHubUpdate texts. They are historical record; "Codex OS" in a 2026-03
  plan is what the project was called then. Same rule that protects `old/`.
- **Perforce depot and stream names (`//Codex/*`) and client NAMES
  (`BigWhite_Codex_*`).** Damian's ruling 2026-08-24: "leave the perforce
  depot stream itself alone. just the directory side changes." The depot
  keeping an old name is precedented: the depot numbering restart is
  already a recorded historical seam.

**Ruled IN 2026-08-24 (Damian, amending this doc's first draft): the
DIRECTORY side renames.** `D:\Projects\NewRepository-*` becomes
`D:\Projects\Cobblestone-*` (agent and `-main` workspaces both), with the
client-spec `Root:` fields updated to match. **Damian stands the fleet
down for this and red does it solo.**

**EXECUTED 2026-08-25 (red, during the stand-down).** GitHub repo renamed
(redirect from the old name verified live), ten agent/-main workspaces
renamed with specs updated and per-client `p4 have` probes green, five
agent memory dirs copied with matching file counts, and the live-doc sweep
landed with this edit. Completed in the same window once Damian closed his p4v/editor windows:
bare `D:\Projects\Cobblestone` (his open prism-backlog edit verified
intact through the move) and `Cobblestone-red-main` (git tip and remote
verified). The LAST act of the window, after this edit lands, is red's own
`Cobblestone-red` -- it cannot be renamed under a live session, so red
renames it, ends, and the next red session wakes in the new path with
memory already copied. The runbook below is the record of what ran:

1. Fleet down, verified: `p4 opened -C <client>` empty for every client,
   or the open files noted and preserved; no live sessions in any
   workspace.
2. Per workspace: edit the client spec `Root:` to the new path
   (`p4 client -o <name>`, change Root, `p4 client -i`), then
   `Rename-Item` the directory. Content is untouched and paths inside the
   tree are relative, so no sync is needed; spot-verify with
   `p4 -c <client> diff -se` (should list nothing unexpected).
3. **Claude session memory keys off the directory path.** Copy
   `C:\Users\Damian\.claude\projects\D--Projects-NewRepository-<x>` to the
   matching `D--Projects-Cobblestone-<x>` for every agent BEFORE first
   session in the renamed dir, or every agent wakes with amnesia.
4. `.p4config` (P4CLIENT names unchanged) and `.agentgrid` (agent name +
   coordination paths, no workspace path inside) need no edits; VERIFY the
   AgentGrid coordinator's own config does not map workspaces by path
   before relying on that.
5. The agent-identity rule survives: everything right of the FIRST `-` in
   `Cobblestone-red` is still `red`.
6. Sweep the docs that name workspace paths at execution time (grep, not
   this list): `CLAUDE.md` ("Working directory:
   `D:\Projects\NewRepository-XXX`"), `HardwareSitting.md` (16 path hits),
   `OperatorsManual.md`, `PerforceProcess.md`, `RiskyBusiness.md`,
   `PublicPush.md` (the workspace census script), `CoordinationProtocol.md`.
7. The `-main` workspaces carry the `.git`; a directory rename moves it
   intact, and the git remotes change for the REPO rename anyway
   (`git remote set-url github https://github.com/damiant3/Cobblestone.git`).
- **Test fixtures and demo data**, except the ERP demo company (below).

## Blast radius, measured 2026-08-24 (re-measure before sweeping, L-COUNT)

- `CodexOS|Codex OS|Codex.OS`: 170 occurrences in 74 files, **but the
  bulk is archives**. The LIVE surface is roughly 20-30 strings.
- **Re-measured 2026-08-25 (reek), markdown only, archives and
  GitHubUpdate texts excluded: 78 hits in 25 files, and the estimate
  above undercounts because it reads "live" as "not in an archive".**
  Most of the 78 is not brand surface and not one lane's to sweep:
  `DevelopersRulebook.md` 13, `apps/lens/DESIGN.md` 8,
  `DevelopersGuide.md` 7, `Build.md` 7, and 8 more in other lanes'
  active designs. Vision and Stories (`DistributedAgentOS.md` 7,
  `TheLongFlight.md`, `Milestones.md`) are history and stay by the
  archive rule; this doc's own 4 describe the rename. **The web lane's
  three named docs hold ONE hit between them** (`apps/works/README.md`;
  `UsersHandbook.md` and `CuratorsCatalogue.md` hold none, so the
  UsersHandbook row above is satisfied by there being nothing to do).
  The reference docs and the per-app design docs are the real remainder
  and have no owner.
- `NewRepository` in docs: 86 occurrences in 49 files; live ones are
  `PublicPush.md` (4), `OperatorsManual.md` (6), `RiskyBusiness.md` (3),
  `PerforceProcess.md` (1), `Annotations.md` (2), plus `HardwareSitting.md`
  (16, all local workspace PATHS, which stay by the boundary above).
- Brand labels found live: `apps/works/GopDesk.codex:433` (the taskbar
  `"CODEX"` label), `apps/landing/LandingPage.codex:101` (hero title),
  the `f-brand` footer label in `apps/landing/web/landing.html`,
  `apps/compliance/web/compliance.html`, `apps/iot/web/iot.html`,
  boot-path strings in `GopBoot.codex`, `DevConsoleBoot.codex`,
  `VgaShell.codex`, `WebServer.codex`, `AgentCoordinator.codex`,
  `apps/works/README.md`, `apps/market/MarketDb.codex`,
  `apps/cvmm/CvmmServer.codex`.
- ERP demo company `"CODEX"` (`apps/erp/*`): fictional data; renaming it
  to Cobblestone is free flavor, one lane's judgement, lowest priority.
- Line numbers above are readings from 2026-08-24 at main ~19169. Sweep by
  grep at the time of the work, not from this list.

Traps for the sweep (the first two were caught by a naive-reader probe of
this doc before it landed):

- **`"CODEX.CDX"` filename strings sit INSIDE the files val will edit and
  must not be touched.** `GopBoot.codex` (~293-326) and `GopWizard.codex`
  (~440) look up the seed by its ON-STICK FILE NAME, which is an artifact
  name and keeps its name by ruling 2. The brand strings and the filename
  strings live lines apart in the same files; edit by string, never by
  file-wide replace.
- **The FAT volume label `"CODEX OS"` (`DevConsoleBoot.codex` ~1468) is
  RULED IN: it becomes `COBBLESTONE`.** It is what a file manager shows
  when the stick is mounted, which makes it brand surface, not artifact
  name. A FAT volume label holds at most 11 characters and COBBLESTONE is
  exactly 11; verify the constant beside the label agrees before assuming
  the fit. (red's call, reversible in one line.)
- **The desk brand label is width-fitted now.** val's 9.3 fit
  (`comp-fit-text`) sizes chrome labels from `gfont-text-w`, so a longer
  brand word (COBBLESTONE is 11 glyphs to CODEX's 5) is safe in the
  chrome but must be LOOKED AT at 1024 wide, not assumed. The sidebar and
  taskbar both carry it.
- **A regex sweep will hit the language name.** "Codex" alone is almost
  always the LANGUAGE and stays. Sweep for the brand contexts (`"CODEX"`
  literals, "Codex OS", masthead lines), never for the bare word.

## The week

Day numbers are targets, not gates; the culminating push waits for its
contents, not for a date.

1. **Day 1 (done with this doc):** decisions ruled, radius measured, plan
   landed. Lanes told.
2. **Day 2, red (early rename confirmed by Damian 2026-08-24):** the
   GitHub rename (`damiant3/NewRepository` to `damiant3/Cobblestone`,
   Settings > Rename on GitHub), then the same day: `git remote set-url`
   in the live `-main` workspace, and the live doc references
   (`PublicPush.md`, `OperatorsManual.md`, `RiskyBusiness.md`,
   `PerforceProcess.md`). The rename is early so the week's pushes land
   under the new name and the redirects get their soak time before the
   culminating push.
2b. **Day 2-3, red SOLO with the fleet stood down (Damian's call on
   timing):** the directory-side rename per the runbook above. One sitting
   of work; the fleet comes back up in `D:\Projects\Cobblestone-*`.
3. **Days 2-4, val:** the on-device brand: desk chrome label, boot
   ceremony, wizard, VGA banner. val owns that surface and the label-fit
   machinery. Verify by capture at 1024 and 1600.
4. **Days 2-4, one lane (reek or blu, whoever frees first):** web surfaces
   (`landing`, `compliance`, `iot` mastheads; "Built with Codex" credits
   STAY), live docs (`UsersHandbook`, `CuratorsCatalogue`, works README),
   ERP demo company if drawn.
   **DONE by reek 2026-08-25** (reek 19272, 19273, 19274). The brand
   label was two source lines, not a file list: `apps/site/SiteTheme.codex`
   drives the nav logo and footer brand for all six themed pages
   (landing, compliance, iot, gpu, perf, realtime), plus landing's own
   hero title. The 35 hand-written games mastheads (portal index and 34
   classic pages) were drawn too, by this section's own instruction to
   sweep by grep rather than from its list. `CODEXMAGIC` is a product
   name and stays. The six regenerated artifacts also carry 13 days of
   html-plug drift, having last been regenerated at main 14754.
   **NOT drawn: the ERP demo company**, still free flavour and lowest
   priority.
5. **Days 3-6, fester:** prism/REPL continues (its own register:
   `apps/prism/prism-backlog.md`); the REPL surface is a culminating-push
   deliverable.
6. **Days 4-6, Damian + red:** the home page. Damian has a started draft
   (parallax scroll, the mountain window turned into the cobblestone path).
   **Searched 2026-08-24 and NOT FOUND in the depot**: prism is the
   compiler-spectrum app, landing is the aquarium showcase, gpushow's
   parallax is a GPU parallax-mapping demo, explorer's "cobblestone" is
   fantasy-biome data; the `D:\Projects` siblings held nothing either.
   Damian locates the draft; red integrates it; where it lives (repo root
   `index.html` via GitHub Pages, or `apps/landing`) is settled when the
   draft arrives.
7. **Day 7:** the culminating push: dual-brand README, home page,
   prism/REPL, the brand sweep, one GitHubUpdate account. **Candidate
   centerpiece (Damian, 2026-08-24): the crazy-boss page -- the compiler
   compiled to WASM, running in the browser, building ITSELF on a static
   page.** It rides fester's wasm-OOM fix (the OOM on compiler-scale IR is
   the exact blocker). Static hosting suffices for the campaign; serving
   it from CobblestoneOS itself is EXPLICITLY a separate later step and
   must not gate the push. Standard interim push unless Damian calls it a
   release (then the release skill and its battery apply).

## Register

Campaign items live HERE while the campaign is active; an item originating
in one app (prism, landing) stays in that app's backlog with this doc
naming the dependency. When the culminating push is public, this doc moves
to `docs/Designs/Done/Marketing/` with the push sha in this line.
