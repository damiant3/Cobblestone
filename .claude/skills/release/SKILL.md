---
name: release
description: Prove the build and publish a release to the public GitHub and GitLab mirrors. The full release gate -- battery, app sweep, poison build, seed + map + img refresh, README digests, GitHubUpdate rotation, and the mirror push. Run ONLY when Damian calls for a public release, never on a routine copy-up.
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

## Step 2 -- The app sweep (breadth over the front end)

```powershell
pwsh build/sweep-app-classes.ps1 -Check -Jobs 8
```

Must exit 0. The apps are the extended pin on the compiler -- 265 diverse
programs, far more front-end surface than the battery covers -- so a unit
that stops compiling is a compiler or foreword regression until proven
otherwise. It fails against `build/app-sweep-baseline.txt`, which names the
units known not to compile and why; anything else dirty is the regression.

**`-Jobs 8`, and that includes release runs.** Damian's ruling, 2026-08-02.
This step said `-Jobs 3` and told you not to raise it, on the grounds that
high parallelism produces units failing with no diagnostics at all. That
observation was real and its cause is dead: it was measured 2026-07-20, two
days before the box's unstable DDR5 XMP profile was corrected on 2026-07-22.
`ExaminersAssay.md` "The parallelism default" has the account, including the
general shape -- **a workaround written into a default outlives the condition
that justified it and then reads as a property of the harness.** The sweep
already re-runs no-diagnostic units alone (`sweep-app-classes.ps1`), so the
defence against real contention does not depend on the slot count.

Know what this does NOT prove. It proves the apps COMPILE and nothing more.
It cannot see a miscompile: the literal-pattern defect fixed in CL 9649 had
`apps/browser` evaluating every conditional as its then-branch and
`apps/cvmm` treating every operator as plus, and both were clean units the
whole time. Breadth here, depth in the battery; neither substitutes for the
other.

## Step 3 -- Poison build (uninitialized-field safety)
Per `OperatorsManual.md` "Poison-Alloc Diagnostic Build": build a 0xCD-fill
seed and run the battery against it. Any failure is an uninitialized-field
read (`CR2=0xCDCD...`); fix it before release. This is the gate that proves
the zero-fill is a safety net, not a crutch holding something together.

## Step 4 -- Diverse double-compiling (the trusting-trust witness)

Run the DDC end to end and compare against the seed being shipped. Recipe
and the traps are in `OperatorsManual.md`, "Running the DDC end to end".

The pass is exact: the Roslyn arm's stage2 is the same byte count as
`seed/Codex.cdx`, and the only differing bytes are the 96 in the signature
region at offsets 40..135. **Zero differing bytes outside that region.**

**This is the one proof the other three cannot substitute for.** The
battery, the sweep and the poison build all ask the compiler about itself;
a compiler carrying a Thompson trojan passes every one of them, because it
is a stable fixed point too. Only a second implementation with unrelated
lineage can answer the question, and Roslyn is the only one in reach.

**A new compiler builtin needs a matching entry in the C# plug's builtin
table** (`codex/plugs/csharp/CSharpEmitterExpressions.codex`), or the
emitted C# references a name that does not exist and Roslyn refuses it. That
is ordinary upkeep, the same as teaching any transpiler a new primitive;
the stub is `"0L"` wherever a hosted C# build has no such device. Running
the DDC on the release is what surfaces it if it was missed.

## Step 5 -- Seed, map, and img
- **Seed:** if a rebuild is due, follow the Developer's Guide seed
  procedure; verify the DEPOT digest after submit (PerforceProcess.md).
- **Map:** refresh `seed/Codex.map`. The `-Repl` seed build never emits the
  MAP block, so nothing else refreshes it (OperatorsManual
  "Release-to-Public Gate", step 2). A stale map misresolves every crash.
- **Img:** rebuild `seed/Codex.img` (`build/build-boot-img.ps1`). It is a
  separate distribution artifact that drifts and is NOT part of a seed
  rebuild. A release ships a current img.

## Step 6 -- README and the GitHubUpdate report
- Update `README.md`: the seed digest and any capability claims that moved.
- Top off and rotate the update report: fill in the current
  `docs/PM/Active/GitHubUpdates/GitHubUpdateN.md` with this release's
  themes, then start the next `N+1` for the following cycle. The report
  ships IN the same commit as the release.

## Step 7 -- Push
Follow `docs/Agents/PublicPush.md` exactly: sync main, `git add -u` plus
explicit new paths (never `git add -A`), secret scan (the signing key and
`apps/games/magic/` never ship), one Update-N commit, push github master and
gitlab master:main, no force.

## Rules
- The battery, the app sweep, the poison build and the DDC are the four
  proofs a release cannot skip. Everything else is polish; these four are
  correctness. They prove different things: the battery is depth, the sweep
  is breadth over the front end, the poison build is memory hygiene, and the
  DDC is the only one that does not take the compiler's word for anything.
- Never force-push; never publish the signing key or `apps/games/magic/`.
- If any step is red, STOP and report. A release is the one thing that must
  never ship broken, because the public inherits it directly.
