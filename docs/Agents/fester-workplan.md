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