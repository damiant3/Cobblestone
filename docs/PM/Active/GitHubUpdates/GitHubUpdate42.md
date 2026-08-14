# GitHub Update 42

**Scope: main CLs 14991 onward, opened 2026-08-14.** Update 41 covered
14771 to 14990. Accumulate this cycle's themes here as they land; every
number in the final report gets re-measured at the release head, not
carried forward.

## Open from Update 41

- **`codex/test/engine-shadow` is skipped, not passing.** val's, from the
  shadow work at 14721/14732. The sidecar states its retirement condition:
  val confirms the measured output is what the 3x3 filtered compare is
  meant to produce, re-mints the `.expected`, and deletes the skip. Until
  then `build/audit-skips.ps1` reports it REAL. **The battery's 0 fail
  depends on this skip.** Carried in from Update 40 and still carried.

- **`COMPILER-3`: `-Repl` and non-repl compiles of the same source differ
  in 255,683 bytes.** Filed at the Update 41 release. Same function names
  at the same offsets, different bytes inside the bodies, and no gate can
  currently see it. Detail and the first experiment are in
  `codex/compiler/compiler-backlog.md`.

- **The 0xFE8 RAM-size cell is a private ABI between the harness and the
  guest.** Steve Howell named it as a compromise during PR 63 and he is
  right: nothing in the compiler writes that cell, and the multiboot
  header does not set MEMORY_INFO, so the guest never asks the loader the
  question whose answer it is handed. Retiring the cheat is open work.
