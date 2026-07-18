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

The `.git` belongs to no single agent -- whoever runs the release holds
it. Every `-main` workspace syncs the identical `//Codex/main` tree, so the
`.git` can be copied between them (`robocopy <src>\.git <dst>\.git /E`) and
works at once: the working tree already matches, so `git status` shows
exactly the changes since the last push. Update 35 onward runs the release
from `NewRepository-reek-main`; earlier pushes ran from
`NewRepository-fester-main`. Either is valid -- the release agent's own
`-main` workspace is the default, and no one workspace is "in charge" of
the mirror.

## Remotes and branch mapping (mismatched, watch out)

- `github` -> github.com/damiant3/NewRepository, HEAD branch **master**.
- `gitlab` -> gitlab.com/damiant3/Codex, HEAD branch **main**.
- The local branch is **master**.
- Push: `git push github master` and `git push gitlab master:main`.

## The push procedure (one "Update N" commit per push)

1. Sync the main workspace to the intended head:
   `p4 -c BigWhite_Codex_fester_main sync`. Confirm `seed/Codex.cdx` is the
   intended fixed-point seed and the README digest matches it.
2. Stage WITHOUT `git add -A`. `-A` re-publishes already-tracked files even
   inside a gitignored folder (that is how the secret folder leaked once).
   Use `git add -u` (all modified and deleted tracked files), then
   `git add <path>` for each new file from `git status --porcelain` `^??`.
   `.gitignore` excludes build-output/, test-output/, .p4config.
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
  source or tests).
- `apps/games/magic/`, the old basic Magic engine (21 core files), stays
  OUT of the public mirror. It is in `.gitignore`, but `.gitignore` only
  governs UNTRACKED files; it never untracks a file already committed. If
  any file in a gitignored folder was ever committed, `git add -A` keeps
  publishing it. Check `git ls-files apps/games/magic`. (The expanded
  commercial app `apps/games/codexmagic/` IS public and is different.)
- Do not publish the 8 MB boot image or PNG snapshots; `git reset` them out
  of the stage.

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

## Before a RELEASE push (not on routine mirror updates)

- Poison build passes (uninitialized-field safety). Damian's call; it needs
  the battery.
- `seed/Codex.img` is a separate distribution artifact that drifts. Rebuild
  it if stale before a release; it is not part of a seed rebuild.
- Refresh `seed/Codex.map`: the `-Repl` seed build never emits the MAP
  block, so nothing else refreshes it.
