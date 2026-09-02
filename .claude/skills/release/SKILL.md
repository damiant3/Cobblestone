---
name: release
description: Prove the build and publish a release to the public GitHub and GitLab mirrors. The full release gate -- battery, app sweep, poison build, seed + map + img refresh, TechnicalDetails digests, GitHubUpdate rotation, and the mirror push. Run ONLY when Damian calls for a public release, never on a routine copy-up.
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

## Conduct (Damian's standing rules for whoever runs a release)

- Run every proof that does not contend for the same box at once, and
  chain the next proof to start itself when the previous ends.
- Never end a turn with a proof unlaunched or a blocker unassigned. If a
  lane holds the critical path, take it yourself rather than wait.
- When a proof fails, do the bisect or diff BEFORE reporting; a failure
  claim is worth nothing without its cause. Report cause and fix together.
- Escalate by `CoordinationProtocol.md`, "When the addressee is the
  COMMANDER" -- blocked, a question only he can answer, a correction to
  something he ruled, and no fourth. Stated once there rather than restated
  here, because two copies of one rule is a version that can drift.
- Freeze the release head early: hold non-essential seed CLs for MAIN
  OPEN after the push, so the proofs run once.

## Step 0 -- Preconditions
- main is green and you intend THIS head to be the release.
- No red gate anywhere in the fleet. A release proves the build works; a
  red gate means it does not.
- Sync the main client fully before anything else.

## Step 0b -- The FULL gate, and it is the only place it runs

```powershell
build/build.ps1        # NO -Internal. Every phase.
```

**This step exists because nothing else runs the full gate any more.** Since
2026-08-20 the standing gate every agent runs is `build/build.ps1 -Internal`
(`CLAUDE.md` R-GATE), which skips a phase whose inputs did not change in that
workspace, and the deferred phases are deferred TO HERE. Before that day the
fleet ran every phase on every CL, so the release never had to ask for them
and this skill never mentioned `build.ps1` at all.

Two phases are proven by nothing except this run, so read them in the phase
timings rather than assuming they happened:

- **the text leg** (`text-stage1`, `sem-equiv`, `text-stage2`,
  `text-fixedpoint`) -- the `.codex` printer round-trip. Conditional on the
  front end since 2026-08-20, so a release whose cycle touched no front-end
  file is the first thing to compile it.
- **the full 270-unit app sweep** -- `-Internal` sweeps a strided 30. Step 2
  below runs the full sweep separately, so this one is belt and braces; a
  regression confined to a unit outside the stride surfaces at one of them.
- **the full `test-compile` sweep** -- every chapter under `codex/test`, about
  1,400 of them, and it is **the longest phase in this gate at roughly 20
  minutes** (1,202 s at 4 ways, measured 2026-08-20). `-Internal` compiles only
  the chapters that CITE what changed in that workspace, so a chapter nobody
  touched is compiled HERE and nowhere else. Budget for it rather than reading
  the gate as hung: the phase prints its unit count before it starts.

If this gate is red, the release stops here. It is also what makes step 1's
"rebuild from the release source first and check the digest" true rather than
advisory: this run leaves `build/output/Sut.cdx` as the compiler the battery
will then use.

## Step 0c -- IR fidelity (did the IR carry what the checker knew?)

```powershell
build/ir-fidelity/ir-fidelity.ps1 -Grade    # 9-16 s; must end "unexpected: 0"
```

Damian's ruling, 2026-08-27: this instrument runs at RELEASE, deliberately
not in `-Internal` -- the class it catches is infrequent and a release
catches it soon enough, so the fleet pays no standing tax (the same
principle that puts the BVT in the standing gate and the full battery
here). `-Grade` runs the reader self-test and the three verdict ablations
before the real cases, so a pass means the instrument can still fail, not
just that it agreed (L-FALSIF). Any `>>>` row or nonzero `unexpected` stops
the release: either the compiler dropped a fact the checker computed
(upstream defect, route to `codex/compiler/compiler-backlog.md`) or the
wire/dump format moved under the reader (repoint the case paths, as main
20176's lift unification required once). The instrument's own account is at
the head of `build/ir-fidelity/ir-fidelity.ps1`.

**The `9-16 s` is two measurements, not a range widened for safety** (fester,
2026-08-27, seven cases at seed `4341370C8FE5BAD6`): 9.4 s median of three on
a quiet box, and 15.5 s the same afternoon with the fleet gating around it. It
is three compiles per case and scales linearly, so a case added costs about
1.5 s. Re-measure rather than quoting this (L-COUNT); it has moved twice
already.

**Do not read a `DROPPED` row as a release blocker on its own.** Expectations
are banked MEASURED, so a case recording a known upstream gap sits at
`DROPPED` with the run green, and today `empty-list-element-type` and
`bounded-int-derived-range` both do. What stops the release is a `>>>` row,
meaning a verdict MOVED from what was banked. That includes a `DROPPED` that
became `CARRIED`, which is somebody having FIXED the compiler: a re-baseline,
not a fault. Case f went exactly that way on 2026-08-27.

## Step 1 -- Prove the build end to end (the battery)

**`-Tier all`, and it is not optional.** A bare `build/test.ps1` runs the
`lang` tier ONLY. Measured at Update 45: bare is 756 tests, `-Tier all` is
1,526, and the 770 in between are where the release blocker of that cycle
was sitting. `OperatorsManual.md` says this in the poison recipe; this step
used to say "the FULL battery" and leave the reader to find that out.

```powershell
build/test.ps1 -Tier all -Jobs 4 -ApprovedBy damian
```

**Read the kernel line it prints.** The battery uses
`build/output/Sut.cdx`, which is whatever the LAST build left there and not
necessarily the head you are releasing. Rebuild from the release source
first and check the digest matches the seed you intend to ship, or step 1
proves a compiler nobody is publishing (measured at Update 45: it did).

Run the FULL battery once, as the proof. This is the one sanctioned battery
run: a release IS the "proofing a build" exception to the standing
never-run-the-battery rule, and the battery's approval gate is what a
release provides. Zero failures. Record the tally. A skipped or failing
test is a release blocker, not a footnote.

## Step 2 -- The app sweep (breadth over the front end)

```powershell
pwsh build/sweep-app-classes.ps1 -Check -Jobs 4
```

Must exit 0. The apps are the extended pin on the compiler -- 265 diverse
programs, far more front-end surface than the battery covers -- so a unit
that stops compiling is a compiler or foreword regression until proven
otherwise. It fails against `build/app-sweep-baseline.txt`, which names the
units known not to compile and why; anything else dirty is the regression.

**`-Jobs 4`, RE-RULED by Damian 2026-08-27, and that includes release runs.**
It supersedes the 2026-08-02 `-Jobs 8` ruling for a different, measured
condition: the box holds 15.8 GiB and 8 slots of 3072 MB guests overcommit
it, killing guests with a moving culprit that reads as codegen
(`OperatorsManual.md` "The compile batch asks for 12 GB of guest RAM, and a
short box reports it as a CODEGEN failure"). The condition rides with the
default on purpose -- **a workaround written into a default outlives the
condition that justified it and then reads as a property of the harness**
(`ExaminersAssay.md` "The parallelism default", which carries both raises
and both lowerings now). When the box grows RAM, re-measure and re-raise.
The sweep still re-runs no-diagnostic units alone, so the defence against
crash-shaped contention does not depend on the slot count.

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

The pass is exact, and it has two conditions and only two: the Roslyn arm's
stage2 is the same byte count as `seed/Codex.cdx`, and **zero differing
bytes outside the signature region at offsets 40..135.**

**How many bytes differ INSIDE that region is not a criterion.** This step
said "the only differing bytes are the 96", and 96 is the WIDTH of the
region rather than a result: two unrelated signatures agree at a given byte
about one time in 256, so a run differing in 95 of the 96 is the ordinary
case and not a failure. Measured 2026-08-12 at seed `527C2C75`: 95, on both
arms independently. Quoting the width as the expected count is a count
carried forward (L-COUNT), and the direction it fails in is the expensive
one -- a future release measures 95, reads it as the witness not holding,
and goes looking for a trojan.

`build/ddc-witness.ps1` runs step 4 and applies exactly the two conditions
above. **It does NOT run step 3.** That script contains no poison phase at
all; this line used to say it ran both, and following it would skip the
poison build while reporting it done. Step 3 is its own recipe in
`OperatorsManual.md` "How to Run a Poison Build", and it is `-Tier all`
like step 1.

**Step 4 needs three prerequisites that a gate destroys.** `build-output/`
does not survive a gate run, so the csharp plug, the emitted `Codex.cs` and
the `ddc-arm` csproj scaffold all have to be rebuilt on the seed under
audit -- and the plug's builder takes no `-Kernel`, resolving
`build-output/bare-metal/Codex.cdx` against the process working directory,
so stage the audited seed there first or the witness certifies the wrong
compiler. `OperatorsManual.md` has the four traps.

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
- **Map:** refresh `seed/Codex.map` by copying `build/output/Sut.map`, which
  the gate leaves beside the binary it describes. Nothing outside this step
  refreshes the shipped one, and a stale map misresolves nearly every symbol
  rather than failing (OperatorsManual "Release-to-Public Gate", step 2, which
  has the validation).
- **Img:** rebuild `seed/Codex.img` (`build/build-boot-img.ps1`). It is a
  separate distribution artifact that drifts and is NOT part of a seed
  rebuild. A release ships a current img.

- **Diag:** the release ships `build/boot/diag.img` and its hash
  (`DiagnosticStick.md` step 4; the stranger's procedure is in
  `UsersHandbook.md`). Rebuild it against the release seed
  (`build/boot/build-diag.ps1`, default `-Kernel seed/Codex.cdx`), then run
  `build/boot/diag-arm.ps1` with EVERY arm and both beds; only that full run
  appends the image hash to `build/boot/diag.rehearsed`, and only a hash on
  that list is flashable with `flash-usb.ps1 -Rehearsed`. Ship the image, the
  `.rehearsed` record, and put the SHA-256 in the GitHubUpdate report and the
  `TechnicalDetails.md` beside the seed digest. The image is reproducible from its source
  and seed (`DIAG.RCP` inside it names them; the hash carries no timestamp),
  so a stale one is a drift the hash check catches.
  **Run `build/check-shipping-images.ps1` before the push, and it is not
  optional.** It refuses an image whose ESP carries a `DIAG.CFG`, because a
  SITTING image is the same file with the box baked into it: `diag-sitting6`
  carries `b3 peer=192.168.6.141:7 ip=192.168.6.200`, which is Damian LAN
  addresses, and this artifact is published with its hash in the release
  notes. On 2026-08-21 a bulk `p4 copy --from` carried a sitting image to
  main head under a changelist about harness timing; it missed a mirror only
  because no push happened in that window. Rebuilding the default is what
  the step above already tells you to do, so the check costs nothing when
  the step was followed and catches it when it was not.
## Step 6 -- TechnicalDetails and the GitHubUpdate report
- Update `TechnicalDetails.md`: the seed digest and any capability claims
  that moved. `README.md` is the business page (split 2026-08-25) and
  carries no digests or counts; touch it only when the pitch itself changed.
- **Re-measure the doc counts, which are off by default and only matter
  here.** `pwsh build/check-doc-counts.ps1` prints a per-claim table and
  exits 1 on any drift. The drifting claims live in `TechnicalDetails.md`, so this is
  the step that publishes them; during a dev cycle the same drift is noise
  and the checker is correctly left off.
- **Work `docs/PM/SomethingSeenDuringRelease.md`.** It is the parking lot for
  things that are true all the time and only cost us at publication. Clear
  the open entries, then mark them with this cycle's number rather than
  deleting them.
- Top off and rotate the update report: fill in the current
  `docs/PM/Active/GitHubUpdates/GitHubUpdateN.md` with this release's
  themes, then start the next `N+1` for the following cycle. The report
  ships IN the same commit as the release.
- **The seed's full digest belongs in `TechnicalDetails.md`, not in the
  note, and it is already step 6's first bullet.** Earlier notes
  duplicated it and that duplication was deliberately dropped: the
  `TechnicalDetails.md` copy is checked against the actual seed by
  `check-doc-counts`, so it cannot go stale, while a second copy in a
  note is checked by nothing. Do not restore the note's digest line
  (reek tried, 20774, reverted 20776). If an outside consumer needs to
  identify which Update a checkout holds, point them at
  `TechnicalDetails.md` at that commit, which carries the release's
  SHA-256 and is enforced.

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
