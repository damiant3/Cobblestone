# Public Push: the GitHub and GitLab Mirrors

How the Codex depot is mirrored to the public git remotes. This is the
process; the per-push changelog lives in
`docs/PM/Done/GitHubUpdates/GitHubUpdateN.md`, which are reports, not the
how-to.

## History: why Perforce is primary

Codex started on GitHub as the primary repository. Around commit 1750 the
project moved to Perforce and restarted numbering at 1, because GitHub was
too slow under heavy agent-coder load (the same strain the tech press was
reporting GitHub under at the time). The two histories were never
consolidated; a copy of the pre-move GitHub history is kept locally. That
consolidation is orthogonal to the push process and is not needed for it.

Perforce `//Codex/main` is the source of truth. The public git mirror is a
downstream publish, not a second master.

## Where the mirror lives

The public push is NOT done from an agent DEV workspace (those are
Perforce-only, no `.git`). It is done from a `-main` copy-up workspace: a
`.git` repo sits alongside the Perforce client root, Perforce syncs main
into that dir, and git tracks the same files and pushes them public.

**The agent Damian asks to push is in charge of that release.** Not a
standing owner, not the workspace a previous release happened to run from
-- the assignment is the authority, and it lasts for that push. Damian's
ruling, 2026-08-12.

Being in charge means finding the `.git` yourself. Every `-main` workspace
syncs the identical `//Codex/main` tree, so the `.git` can be copied
between them (`robocopy <src>\.git <dst>\.git /E`) and works at once: the
working tree already matches, so `git status` shows exactly the changes
since the last push. **Take it from the last agent that actually pushed --
the one whose remote state is current -- and either run the release there
or robocopy that `.git` to where you are working.**

**Find it by asking the remote, not by reading a name off this paragraph
and not by comparing dates.** Several `-main` workspaces hold a stale
`.git` from an older release and they are indistinguishable by name. A date
comparison is not enough either: measured 2026-08-12, `red-main` carried a
2026-08-09 commit that was NEWER than `reek-main`'s and had never been
pushed at all, so a newest-wins sort is ambiguous exactly where it matters.
The remote tip is the only thing that settles it:

```powershell
$remote = (git ls-remote https://github.com/damiant3/Cobblestone.git master) -split '\s+' |
          Select-Object -First 1
foreach ($w in Get-ChildItem D:\Projects -Directory -Filter 'Cobblestone-*-main') {
    if (-not (Test-Path "$($w.FullName)\.git")) { continue }
    $sha  = git -C $w.FullName rev-parse HEAD
    $date = git -C $w.FullName log -1 --format='%ad' --date=short
    git -C $w.FullName cat-file -e "$remote^{commit}" 2>$null; $known = $?
    $mark = if ($sha -eq $remote) { 'AT REMOTE TIP' }
            elseif ($known)       { 'has the tip, local commits ahead' }
            else                  { 'does NOT have the remote tip -- stale' }
    '{0,-26} {1} {2}  {3}' -f $w.Name, $date, $sha.Substring(0, 8), $mark
}
```

Take the one at the remote tip. "Has the tip, local commits ahead" is also
usable and means someone committed without pushing; find out why before you
build on it. Anything reporting stale is a copy from an older release and
must not be pushed from -- doing so re-uploads a history the remote already
has and leaves a divergence to reconcile without forcing.

Measured 2026-08-12 with the above, github master at `4175119c`:

| workspace | HEAD | |
|---|---|---|
| `fester-main` | 2026-07-08 `6ac4ee35` | stale (Update 34) |
| `reek-main` | 2026-07-24 `b1c50258` | stale (Update 36) |
| `red-main` | 2026-08-09 `9f92c703` | stale, and newer than reek's |
| `val-main` | 2026-08-10 `4175119c` | **at the remote tip** |

That table is a reading, not a rule. It is here to show what the command
prints and what the stale case looks like; re-run the command every time.
This section named a fixed workspace until 2026-08-12 and it had been wrong
for four days -- a named workspace in a doc is a count carried forward by
another route (L-COUNT).

## Remotes and branch mapping (mismatched, watch out)

- `github` -> github.com/damiant3/Cobblestone, HEAD branch **master**.
- `gitlab` -> gitlab.com/damiant3/Codex, HEAD branch **main**.
- The local branch is **master**.
- Push: `git push github master` and `git push gitlab master:main`.

**They are not equal in standing.** GitHub is the face of the project: it is
where people read it and where pull requests arrive. GitLab exists as a backup
in case GitHub goes away somehow, and Damian barely ever looks at it (his
words, 2026-08-13). Push both, but do not treat GitLab silence as a signal or
its state as a thing to reconcile against.

## The push procedure (one "Update N" commit per push)

1. Sync the main workspace to the intended head:
   `p4 -c BigWhite_Codex_fester_main sync`. Confirm `seed/Codex.cdx` is the
   intended fixed-point seed and the `TechnicalDetails.md` digest matches it.
2. Stage WITHOUT `git add -A`. `-A` re-publishes already-tracked files even
   inside a gitignored folder (that is how the secret folder leaked once).
   Use `git add -u` (all modified and deleted tracked files), then
   `git add <path>` for each new file from `git status --porcelain` `^??`.
   `.gitignore` excludes build-output/, test-output/, .p4config.
2b. **RECONCILE THE DEPOT AGAINST THE INDEX BEFORE COMMITTING. Staging by
    name loses files permanently, not transiently.** Step 2 stages with
    `git add -u`, which only touches files git ALREADY TRACKS, plus one
    `git add <path>` per new file read off `git status --porcelain`. A new
    file missed in that list is not picked up by any later push either,
    because `-u` will never see it: the omission is permanent and silent.
    Git also collapses a wholly-new directory into ONE `??` line, so a
    twenty-file addition is one entry to overlook.

    Re-measured 2026-09-07 evening (red), and re-measure again at the next
    release rather than quoting this (L-COUNT): 482 tracked files are in the
    depot and absent from `github/master`, 420 of them ruled out by
    `check-ignore`, leaving **62 survivors**. Six `build/*.ps1` checkers
    including `sign-seed.ps1`, five `docs/PM/Active/Stories/*.md`, twenty
    `build/boot` flight artifacts, six `codex/test` fixtures with their
    `.expected`, `codex/plugs/wasm/WasmStdio.codex` and the rest are ordinary
    additions, nothing about them is secret, and the mirror does publish
    `build/boot` images (`diag.img` is up), so those are a gap and not a
    policy.

    **RULED (Damian, 2026-09-07): the five third-party specifications are
    NOT redistributed**, and the rule is now enforced rather than
    remembered. `USB_2_0_Specification`, `xHCI_Specification`,
    `HID_1_11_Specification`, `Intel_I219_Datasheet` and
    `Intel_82583V_Datasheet`, each a `.pdf` and a `.txt` extract, are named
    file by file at the bottom of `.gitignore`, so the reconcile no longer
    proposes them: survivors went 62 to 55 on the re-run, with zero specs
    among them. They are named individually and NOT as
    `docs/Reference/*.pdf`, because that directory is published and carries
    other third-party PDFs whose terms allow it (a PLDI paper,
    `AMI_Aptio_V_Deep_Dive.pdf`); a blanket rule would silently withhold
    those too.

    `docs/Reference/CONTENTS.md` names each withheld document, the exact
    revision the depot holds, and a link to its publisher. A withheld file
    is otherwise invisible from outside and a reader cannot tell it from one
    somebody dropped, which is 2c's point. Add a row there when a document
    joins the ignore rule, or the next reader is back to guessing.

    **That file is NOT on the mirror** (measured 2026-09-08, blu:
    `git ls-tree -r github/master --name-only` matches nothing for it, and
    the reconcile lists it as a survivor). The one document that explains
    what is withheld is itself missing, which is 2c happening to 2c. Stage
    it explicitly at the next push.

    The reconcile is one command from the main workspace and it needs no
    network:

    ```powershell
    $mirror = @(git ls-tree -r github/master --name-only)          # forward slashes
    $depot  = @(p4 -c BigWhite_Codex_<agent>_main have | ForEach-Object {
                 $i = $_.IndexOf(' - '); if ($i -gt 0) { $_.Substring($i + 3).Trim() } })
    # subtract the workspace root and switch to forward slashes, compare, then
    # write the difference to an LF-ONLY file and feed THAT to check-ignore:
    [System.IO.File]::WriteAllText($f, (($diff -join "`n") + "`n"))
    cmd /c "git -C <repo> check-ignore --stdin < `"$f`""
    ```

    **Feed `check-ignore` LF, never CRLF, and never a PowerShell pipeline.**
    Measured 2026-09-08 (blu, the Update 56 reconcile), both directions on
    the same three paths: with LF input `check-ignore --stdin` returns every
    ignored path; with CRLF input it returns only the ones matched by a
    DIRECTORY rule and silently drops every one matched by an exact-path
    rule, because the trailing carriage return is part of the name it
    compares. A PowerShell `$paths | git check-ignore --stdin` sends CRLF, so
    the recipe as written until this date under-reported the ignored set by
    13 and listed all ten third-party specifications as survivors: the exact
    files the 2026-09-07 rule was added to withhold, offered back for
    staging on every run. The tell is a survivor list containing something
    you know is in `.gitignore`; the check is
    `git check-ignore -v <one path>`, which takes an argument rather than
    stdin and names the rule and line that matched.

    **A survivor list is what drives `git add`, so an under-reported ignore
    set is the expensive direction.** Verify any new ignore rule against a
    path it must match AND a path it must not, before trusting a run.

    **Anything left after check-ignore is a file nobody decided to withhold.**
    Do not read a large raw difference as a disaster: of 479 paths missing
    at the 2026-09-07 measurement, 435 were deliberate -- `apps/games/magic`,
    `assets/games`, `apps/wademo`, `apps/productbuilder`, `codex/product`,
    `build/boot/archive`, the third-party specification PDFs, and everything
    `*.exe` / `*.cdx`. The number that matters is the one that survives the
    ignore rules.

2c. **A DELIBERATE EXCLUSION IS INVISIBLE FROM OUTSIDE, AND THAT IS ITS OWN
    DEFECT.** Steve Howell's issue 123 reported three directories present in
    the depot and 404 on the mirror. All three are ruled exclusions with good
    reasons -- `codex/product` and `apps/productbuilder` are customer work
    (Damian, 2026-08-18), `apps/wademo` carries an NHGIS extract whose terms
    forbid redistribution -- and exactly ONE of them, `codex/product`, is a
    quire. But a reader of the public tree cannot tell a withheld directory
    from a lost one, so a contributor spends his time reporting our policy
    back to us. The mirror should say which paths are withheld and why,
    without naming what is in them.
3. Commit as author damiant, one line, comma-separated themes, no trailers.
   The Update-N report file is part of the same commit.
4. Push, NO force (standing rule). The github credential is usually cached;
   gitlab may reject until an interactive browser login refreshes the token,
   then re-run.

## Do not publish

- The signing key must never be published (CLAUDE.md rule 9). Scan
  `git status` for key/pem/pfx/env/credential/token before every push.
  Secret-scan false positives to ignore: `Keyboard.codex`,
  `identity-keygen`, `cap-launder-pure-key` (they match "key" but are
  source or tests), and `codex/test/fixtures/https/*` (a synthetic
  throwaway TLS test PKI -- CA, leaf, and a deliberately rogue leaf,
  private keys included BY DESIGN so the peer-verification tests run
  from a clone; inspected and shipped with Update 38).
- Third-party specification PDFs and their text extracts
  (`docs/Reference/*_Specification.*`, `*_Datasheet.*`) stay OUT of the
  public mirror: the depot may hold them for the audit trail, but
  republishing other parties' copyrighted documents is redistribution.
  Our own notes about them (`docs/Reference/*_Notes.md`) ship. Ruled
  during the Update 38 push, 2026-08-05.
- `apps/games/magic/`, the old basic Magic engine (21 core files), stays
  OUT of the public mirror. It is in `.gitignore`, but `.gitignore` only
  governs UNTRACKED files; it never untracks a file already committed. If
  any file in a gitignored folder was ever committed, `git add -A` keeps
  publishing it. Check `git ls-files apps/games/magic`. (The expanded
  commercial app `apps/games/codexmagic/` IS public and is different.)
  **`annotations/apps/games/magic/` is a different path and its publication
  is intentional** -- Damian ruled it accepted as public 2026-08-17, after
  the Update 45 push scan found 150 files there dating to Update 39; the
  code itself stays hidden as before, so do not re-raise this.
- **`build/boot/diag-sitting*.cfg` never ship.** Found at the Update 49
  pre-push scan, 2026-08-21: five of them were untracked and new, and every
  one names the box (`b3 peer=192.168.6.141:7 ip=192.168.6.200`). They are
  the sitting IMAGE's questions one file over, which is exactly what
  `build/check-shipping-images.ps1` keeps off the mirror for the image, and
  nothing kept them off for the cfg. `git add` them never; re-measure with
  `git status --porcelain | Select-String 'diag-sitting'` before every push.
  `diag-default.cfg` ships: it names stages and no address.
- Do not publish the 8 MB boot image or PNG snapshots; `git reset` them out
  of the stage. **`build/boot/kbd-diag-v16.img` stays up** -- public since
  2026-08-03 and Damian ruled it kept 2026-08-17, on the grounds that it does
  not hurt and might help someone. **build/boot/diag.img and
  build/boot/diag.rehearsed SHIP on the same grounds and by design
  (DiagnosticStick.md step 4): the stranger's instrument, 16 MB, no seed,
  no identity, its SHA-256 in the release notes.
  **A SITTING image must never be the one that ships, and there is now a
  runner: `build/check-shipping-images.ps1`.** The same file with a sitting
  config baked onto its ESP names the box it was built to interrogate:
  `diag-sitting6.cfg` is two lines and one is
  `b3 peer=192.168.6.141:7 ip=192.168.6.200`. On 2026-08-21 a bulk
  `p4 copy --from` carried a sitting image to main head inside a changelist
  about harness timing, and nothing in the tree would have refused it; it
  reached no mirror only because no push fell in that window, which is luck
  rather than a control. The check refuses ANY `DIAG.CFG` on a shipping
  image rather than arguing about which addresses are private enough to
  publish. Falsified both ways 2026-08-21: REFUSED on 63EFDB8A naming the
  two baked lines, OK on the default A92502F8.

## Divergence (Damian's rule: pull but take nothing, keep all local)

If a remote tip is not an ancestor of local HEAD, a plain push is rejected
and you must NOT force. `git fetch`, then
`git merge -s ours <remote>/<branch>` (records the remote as a parent but
keeps OUR tree entirely), then push as a fast-forward.

## A history rewrite (rare, e.g. purging a leaked folder)

A force-push after a history rewrite (`git filter-branch`) re-uploads
objects and is SLOW. Run it in the background (a 3-minute foreground
timeout blew mid-transfer). A git ref update is atomic: a killed push
either moved the ref or left the remote unchanged, never a partial history.
After a sensitive purge, `git ls-remote --heads <remote>` catches stray
branches (gitlab once kept a stale `master` alongside `main`);
`git push <remote> --delete <branch>` removes one. A force-push cannot
un-serve bytes already cloned or cached by GitHub or GitLab.

## The website mirror: CobblestoneWeb

github.com/damiant3/CobblestoneWeb serves the landing site through GitHub
Pages at https://damiant3.github.io/CobblestoneWeb/ (Settings > Pages:
branch master, folder `/`). It is a PUBLISHED ARTIFACT, not a source
tree: everything in it is assembled by `apps/landing/build.ps1` from
`LandingPage.codex` and the wasm plug, and its README says so. Edit in
the depot, rebuild, republish; a hand edit there is overwritten by the
next publish.

**Pushes here are independent of the code mirrors, in both directions
(Damian, 2026-08-27).** The site may publish before the code that builds
it reaches github/gitlab, or after; nothing in the bundle references the
code repo at runtime, so neither order can break the other. Keep
provenance instead of coupling: name the seed the bundle was built from
in the commit message.

The working copy is `D:\Projects\CobblestoneWeb` (git, remote `origin`,
branch master; the root `index.html` is a redirect to `landing.html` and
stays). **The root `CNAME` file is the custom domain
(cobblestoneproject.com, DNS on Damian's Cloudflare account, records
DNS-only so GitHub's cert provisioning can see the domain) and must
survive every republish** -- the robocopy in step 5 does not delete it,
but do not "clean" it away either: deleting it detaches the domain and
the site falls back to the github.io URL.

The update procedure:

1. `p4 edit apps/landing/web/landing.html
   apps/landing/web/compile/prism.html`. Both are tracked build outputs;
   against read-only files the build fails with "Access to the path ...
   is denied" (measured 2026-08-27).
2. **Rebuild the plugs the assembly rides before trusting it.** Both
   staleness failures happened on the first assembly, same day: an
   8-day-old wasm-plug binary died OUT OF MEMORY on compiler-scale IR
   (the fixes were in source, not in the binary it was graded through),
   and an 11-day html plug emitted a `landing.html` that differed from
   the fleet-built depot copy. `codex/plugs/wasm/build.ps1` and
   `codex/plugs/html/build.ps1` are the rebuilds.
3. `apps/landing/build.ps1` assembles `web/` whole.
4. **Compare the regenerated tracked artifacts against the depot before
   landing or shipping them, and take the better one per file.** The
   stdio lens modules (`javascript-stdio.wasm`, `csharp-stdio.wasm`,
   `evidence-stdio.wasm`) exist only where they were built; a box
   without them assembles a Prism with those lenses dark, 365 KB
   smaller, and shipping that over a lens-carrying depot copy is a
   regression that reads as a rebuild. On 2026-08-27 the depot copies
   won on both counts: `p4 revert` restored them and only the untracked
   `compile/` pieces shipped fresh.
   **Normalise line endings before you compare, or this step lies about
   `landing.html`.** The html plug emits LF and that file is CRLF in the
   workspace, so a rebuild that ADDED the hero CTA row still measured 104
   bytes SMALLER than the depot copy and `p4 diff` called all 325 lines
   changed: +221 content characters on one line against -325 line endings
   (reek, 2026-08-27). Smaller is the staleness signature, so the raw size
   reads as the trap when the rebuild is in fact the better file.
5. `robocopy apps\landing\web D:\Projects\CobblestoneWeb /E /XF
   *.landing-save`, then in `D:\Projects\CobblestoneWeb`: `git add -u`
   plus `git add <path>` for each new file the site SERVES, commit as
   damiant naming the seed, `git push origin master`.
   **Stage what the site serves, not what the bundle holds** (red's
   ruling, 2026-08-27, on reek's deviation). `git add -A` would have put
   the 48 sibling lens modules into an append-only public history and
   nothing ever requests them: `prism.html` resolves a module as
   `EMBED[file] ? b64ToBytes(...) : fetch(...)` with all 49 keys embedded,
   and `compile/index.html` fetches `codex-compiler.wasm` and no stdio
   module, which is why that one is the only module ever tracked there.
   Show those two facts before claiming a file is unserved; adding a
   module later is cheap and removing one from history is not.
5b. **Grade before you stage** (root, 2026-09-03, the Update 55 site rebuild).
   None of these is run by `build.ps1`, and the 2026-09-02 site shipped 48
   lens modules built with a wasm plug the wat-wrap fix had not reached:
   `codex/plugs/wasm/page-lens-test.ps1` (with `-Calibrate` first, then
   plain) and `page-bytes-test.ps1` over the modules the page copies;
   `page-wire-test.ps1` (same order) for the two the other two cannot
   reach, `riscv-stdio.wasm` and `arm64-stdio.wasm`, which the page ships
   like any lens; it needs both modules AND each
   `codex/plugs/<plug>/build.ps1`'s `<plug>-plug.cdx` oracle built first,
   and boots a guest per row, so ask for the box before it;
   `apps/c64/c64-verify.mjs`, `apps/mathbook/mb-verify.mjs`,
   `apps/starmap/sm-verify.mjs`, `apps/games/ar-verify.mjs` and
   `apps/games/page-verify.mjs`; `node apps/safari/sf-decode.mjs`;
   `apps/landing/check-links.ps1 -Web apps/landing/web -Live`; and
   `/experimental/` driven headless, which is the one arm that exercises
   the compiler module and two lenses together:
   `chrome --headless=new --dump-dom "file:///.../apps/landing/web/experimental/index.html?auto=1"`
   with `--allow-file-access-from-files` and a `--virtual-time-budget`,
   then read the `stage` boxes out of the DOM. A module the site serves
   that no grader ran is the fireworks skyline of 2026-09-02: it was never
   staged and the live page 404'd on it for a day.
6. The Pages deploy takes a minute or two. Verify with a request, not by
   assumption: `landing.html` and `compile/prism.html` both answer 200.

The secret-scan discipline above applies to this push like any other.
The bundle ships the compiler's own source (`compile/Codex.codex`) by
design: the self-compile page eats it.

## Before a RELEASE push (not on routine mirror updates)

- Poison build passes (uninitialized-field safety). Damian's call; it needs
  the battery.
- `seed/Codex.img` is a separate distribution artifact that drifts. Rebuild
  it if stale before a release; it is not part of a seed rebuild.
- Refresh `seed/Codex.map`: the `-Repl` seed build never emits the MAP
  block, so nothing else refreshes it.
- **Close out any outside pull requests whose work this push makes public** --
  thanks, the commit, the tweaks made on top, a link to the GitHubUpdate
  entry, then close. **They arrive on GitHub** (`damiant3/Cobblestone`);
  GitLab `damiant3/Codex` is a backup against GitHub going away and is barely
  looked at, so do not wait on its state. The whole procedure, including how
  such a PR gets into Perforce in the first place and why it is never merged
  where it was opened, is in `docs/Agents/PerforceProcess.md`, "An outside
  pull request". **The release agent is the one who owes the contributor
  this**, because the release is the moment the claim becomes checkable.
