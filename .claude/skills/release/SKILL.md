---
name: release
description: Prove the build and publish a release to the public GitHub and GitLab mirrors. The full release gate -- battery, poison build, seed + map + img refresh, README digests, GitHubUpdate rotation, and the mirror push. Run ONLY when Damian calls for a public release, never on a routine copy-up.
shell: powershell
---

This is the release gate: the steps that turn a green main into a public
push. It runs ONLY when Damian asks for a release. The day-to-day gates
(text and CDX fixed point, BVT) already prove correctness; everything here
is public-facing proof and polish. Do not run it on a routine seed rebuild
or copy-up.

Details live in the docs; this skill is the ORDER and the gate. Do NOT
restate Perforce or push mechanics here -- follow
`docs/Agents/PerforceProcess.md` and `docs/Agents/PublicPush.md`, which are
the single source for those.

## Step 0 -- Preconditions
- main is green and you intend THIS head to be the release.
- No red gate anywhere in the fleet. A release proves the build works; a
  red gate means it does not.
- Sync the main client fully before anything else.

## Step 1 -- Prove the build end to end (the battery)
Run the FULL battery once, as the proof. This is the one sanctioned battery
run: a release IS the "proofing a build" exception to the standing
never-run-the-battery rule, and the battery's approval gate is what a
release provides. Zero failures. Record the tally. A skipped or failing
test is a release blocker, not a footnote.

## Step 2 -- Poison build (uninitialized-field safety)
Per `OperatorsManual.md` "Poison-Alloc Diagnostic Build": build a 0xCD-fill
seed and run the battery against it. Any failure is an uninitialized-field
read (`CR2=0xCDCD...`); fix it before release. This is the gate that proves
the zero-fill is a safety net, not a crutch holding something together.

## Step 3 -- Seed, map, and img
- **Seed:** if a rebuild is due, follow the Developer's Guide seed
  procedure; verify the DEPOT digest after submit (PerforceProcess.md).
- **Map:** refresh `seed/Codex.map`. The `-Repl` seed build never emits the
  MAP block, so nothing else refreshes it (OperatorsManual
  "Release-to-Public Gate", step 2). A stale map misresolves every crash.
- **Img:** rebuild `seed/Codex.img` (`build/build-boot-img.ps1`). It is a
  separate distribution artifact that drifts and is NOT part of a seed
  rebuild. A release ships a current img.

## Step 4 -- README and the GitHubUpdate report
- Update `README.md`: the seed digest and any capability claims that moved.
- Top off and rotate the update report: fill in the current
  `docs/PM/Active/GitHubUpdates/GitHubUpdateN.md` with this release's
  themes, then start the next `N+1` for the following cycle. The report
  ships IN the same commit as the release.

## Step 5 -- Push
Follow `docs/Agents/PublicPush.md` exactly: sync main, `git add -u` plus
explicit new paths (never `git add -A`), secret scan (the signing key and
`apps/games/magic/` never ship), one Update-N commit, push github master and
gitlab master:main, no force.

## Rules
- The battery and the poison build are the two proofs a release cannot
  skip. Everything else is polish; these two are correctness.
- Never force-push; never publish the signing key or `apps/games/magic/`.
- If any step is red, STOP and report. A release is the one thing that must
  never ship broken, because the public inherits it directly.
