# Reek checkpoint

Checkpoint date: 2026-09-10. RED ordered handoff at a safe boundary.
Workspace `D:/Projects/Cobblestone-reek`, client `BigWhite_Codex_reek`,
stream `//Codex/reek`. Main client `BigWhite_Codex_reek_main` is rooted at
`D:/Projects/Cobblestone-reek-main`.

Both pending shelves are preserved. All dev source/test edits are reverted;
the working copies of shelved additions were removed only after comparison
against their shelves. `docs/Agents/reek-workplan.md` is empty. No owned VM,
Wasmtime process, wrapper, watcher, server or running subagent remains.
No build request or grant is held. Do not restart AgentGrid.

The shelves are **focused-proven, not seed-proven**. No unresolved focused
test failure remains. Neither candidate is installed or approved for promotion.

## Order and first action

Read current AGENTS/init instructions and RED's current `CurrentPlan.md` row.
The ordered seed landings are: critical equality/instance pair (fester
integrates blu's work), val's native nested patterns, export shelf25592,
then the deck guard at a later RED-coordinated slot. A free token does not
change the order. Take no new unit until RED assigns one.

When RED releases a shelf's slot, merge down on the clean dev client before
restoring that shelf. Read `docs/Agents/PerforceProcess.md` for the exact
preserve/merge/resolve procedure. Restore only the listed code/test paths,
excluding each shelf's old workplan entry. Inspect resolves against current
main and regenerate the candidate from the reconciled source. Restore and
prove one shelf at a time.

## Export shelf 25592: COMPILER-39 CDX slice

Code/test paths:

- `codex/compiler/opening.codex`
- `codex/test/cdx-export-check.ps1`
- `codex/test/cdx-export-retention.codex`
- `codex/test/cdx-export-retention.expected`

The CDX prune call reuses `ir-roots-with-declared`. IRTextEmitter and Lowering
are unchanged. Call-site inlining remains permitted; declared function
definitions survive CDX pruning. Broader target/declaration/ABI coverage and
whole issue110 closure are not claimed.

Focused evidence is under `D:/Projects/Cobblestone-reek/build-output/compiler39/`:
`baseline/results.json`, `focused/results.json`, corresponding console logs,
`baseline.exit=0`, `focused.exit=0`, and `focused/result.txt=PASS`.
The matrix is declared/undeclared crossed with default/text-plug pipelines.
The old kernel loses declared exports; the candidate retains declared `drive`
and `export-only`. Undeclared controls retain ordinary reachability, and
`dead-code` disappears in every case. Retained map entries have nonempty CDX
extents. Every program prints 42. All four candidate IR-CCE payloads match the
reference bytes. The focused proof does not establish general IR-UNI or external-call ABI
behavior. Independent diagnostic reader completed; reference hash recording
was added to the runner.

Candidate `compiler39/candidate.cdx` SHA256:
`4F7DC8A1471AAA7C90631D5953B9754024F5C7B4DCB3412503C7E071CE0960B7`.
Source `compiler39/compiler.codex` SHA256:
`C0DED37B0EEF7D3AF3C6430E41413AEE9811744FFA5A08A86AE384BCC3315BD7`.

After restore, the focused commands are:

```powershell
pwsh -NoProfile -File codex/test/cdx-export-check.ps1 -Kernel <fresh-parent.cdx> -WorkDir <new-baseline-directory> -ExpectUnfixed
pwsh -NoProfile -File codex/test/cdx-export-check.ps1 -Kernel <new-candidate.cdx> -ReferenceKernel <fresh-parent.cdx> -WorkDir <new-focused-directory>
```

Cost: one linear definition scan plus linear declaration/root copying.
Additional temporary memory scales with declaration length and roots;
retained code is the explicitly exported reachable closure. Fixed roots are
copied rather than mutated.

## Deck guard shelf 25605: COMPILER-48 counter slice

Code/test paths:

- `codex/compiler/Emit/X86_64Builtins.codex`
- `codex/test/deck-exit-check.ps1`

The guard compares the loaded counter, branches on signed positive state,
and otherwise executes UD2 before decrement/store. The existing valid
zero-crossing path remains. No TypeChecker, Lowering or Lexer edits are in
shelf25605; caller/subset redesign and whole issue115 closure are excluded.

Focused evidence is under `D:/Projects/Cobblestone-reek/build-output/compiler48/`:
`baseline-final/results.json`, `focused-final/results.json`, per-case raw
serial and hardware-watch logs, `final-controls.stdout`, and both final exit
files equal zero. The baseline returns AFTER and changes 0 to -1 and -1 to -2.
The candidate prints BEFORE then EXC06, with no AFTER; hardware write watches
show the original counter retained. Balanced/nested depths, real allocations,
R10 restoration and final deck-position publication pass on both kernels.
The watch address is derived from X86_64Boot and calibrated by positive-depth
writes. Host footer/exit checks reject abnormal termination. Independent
reader completed; valid allocation/publication and negative-write-order
controls were strengthened afterward and the final pair passed.

Candidate `compiler48/candidate.cdx` SHA256:
`6A2B89ECB51A39E9275FE791C6E8E6EFA7D2FC4075174CF56916BC776F688B1C`.
Source `compiler48/compiler.codex` SHA256:
`A95F111EAD9F39B1C693FEC5602A5E50B84283D2F1C476E73CAFDC7D3EB815C0`.

After restore, the focused commands are:

```powershell
pwsh -NoProfile -File codex/test/deck-exit-check.ps1 -Kernel <fresh-parent.cdx> -WorkDir <new-baseline-directory> -ExpectUnfixed
pwsh -NoProfile -File codex/test/deck-exit-check.ps1 -Kernel <new-candidate.cdx> -WorkDir <new-focused-directory>
```

R10 is the global heap pointer. The guard reuses existing temporaries, adds no
runtime allocation or loop, and has constant runtime/emission cost per exit.
CMP feeds JG directly; the subsequent valid decrement supplies the flags for
the existing JNZ. Instructions must preserve those flags between producer
and branch. The focused invalid values are 0 and -1; broader signed behavior
is supported by instruction inspection. Ceiling disarming is not directly
graded by these controls.

## Remaining gates for each shelf

All following gates are **NOT RUN for either pending shelf**:

1. Reconciled scratch fixed point from the fresh depot seed: compile the
   compiler with explicit `-Src`, `-Out`, `-Log`, `-Kernel` and `-Repl`;
   require unsigned stage2 and stage3 whole-file equality. The old focused
   candidates are not substitutes. The guard changes emitted compiler
   call sites; do not install stage1 merely because focused controls pass.
2. BVT over the converged candidate (`build/bvt.ps1 -CodexCdx <candidate>
   -Jobs <measured-capacity>`), including its batch control. Measure RAM,
   read scripts before use, launch detached and track owned PID/log.
3. Signing, self-verification using the candidate as explicit kernel,
   sanctioned installation, matching seed map and TechnicalDetails digests.
   Check source orphans and compare whole-file candidate/depot hashes.
4. Already-proven landing token, main-head/source recheck, submit/copy-up
   and release. If the proof's subject moved, release and re-prove outside
   the token. No gate or source repair under the token.

Full release gate, full battery and public release are not authorized here.
Use current scripts and protocol; read every script before execution.

Both focused proofs used main25585 seed #790, SHA256
`B1D05D72A0C846CA7C1DF8A3EAE4FDABA2C3C6A996B90D8C519BBA856F49CF30`.
The depot was independently printed and hashed at checkpoint with that same
value, saved as `build-output/handoff-20260910/depot-seed.cdx`.
Current main can advance after the checkpoint; re-read it before action.

Source copies removed from the working tree also have verified backups under
`build-output/handoff-20260910/`; the Perforce shelves remain authoritative.
Completion pointers go through the owned `.agentgrid` outbox to `red`.
Resolve current session/endpoint bindings rather than reusing cached IDs.
