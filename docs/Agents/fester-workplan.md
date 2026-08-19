# fester -- workplan

*Empty by design. Keep it that way.*

**Open work does not live here.** Cross-lane items are in
`docs/PM/CurrentPlan.md`; an item that originates in one app or quire
is in that register (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`).

**Durable facts do not live here either.** A fact goes to the doc that
owns its subject the moment it is verified: `OperatorsManual` for the VM,
builds and flags, `ExaminersAssay` for tests and beds, `DevelopersGuide`
for the language, `HardwareSitting` for flights, the design that owns the
capability, `LESSONS.md` for a lesson.

**There is no findings outbox.** That channel was retired 2026-08-08. A
finding another lane needs goes into the owning doc, where they will read
it when they touch the subject, rather than into a status file nothing
re-reads.

This file is scratch for the CURRENT session's lane state only: what is
shelved, what is mid-gate, what the next action is. Empty it at handoff.
If it starts carrying work items, standing facts or messages to other
agents, they are in the wrong place -- move them out and empty this again.

## Shelved: CL 17281, CrossLaneFilesystem step 0

Written and measured, NOT green, shelved when the lane was reassigned to
ProductBuilder on 2026-08-19. Do not land it as it stands.

The change: both plug lanes report `[UNSUPPORTED]` from the unresolved-call
path instead of `[WARN]`; the four f32 mode-conversion builtins gain identity
arms beside their f64 neighbours; `uefi-read-key` joins the refusal family
that already covers `uefi-read-file`, with a `.cross-refusal` sidecar.

The census that justified it, both lanes at `-Jobs 8` with the plug binaries
rebuilt on the seed first: arm64 four unresolved names across four tests,
riscv64 three across two, nothing else in 486. No allow-list needed.

**What blocks it.** `real-approx-modes` goes PASS_EXPECTED to FAIL_OUTPUT on
arm64 only: line 4, `sat finite stays`, answers 2139095039 where 0 is
expected. riscv64 answers 0 correctly with the identical arms, so the arms
are right and the arm64 f32 saturating multiply is wrong underneath them. The
old accidental identity -- an unpatched one-argument call leaves the argument
where the caller reads its result -- was masking it. Next action is to pin
that multiply on arm64, not to soften the refusal.

Also uncovered and not diagnosed: `db-full-test` and `wademo-pyramid` emit an
unresolved call to `k` on arm64 and PASS on riscv64, so the arm64 lane loses a
name the riscv lane resolves. Candidate sites are the curried lambda at
`codex/foreword/core/Hamt.codex:270` and the `f acc k v` application at 254.
The falsifying test has not run, so the mechanism is a candidate, not a claim.
`last-compile.ir` is CCE, not text: grepping it returns mojibake rather than
nothing, which reads as a clean miss.