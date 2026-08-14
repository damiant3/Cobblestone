# GitHub Update 43

**Scope: main CLs 15085 onward, opened 2026-08-14.** Update 42 covered
14991 to 15084. Accumulate this cycle's themes here as they land; every
number in the final report gets re-measured at the release head, not
carried forward.

## Open from Update 42

- **`COMPILER-3`: `-Repl` and non-repl compiles of the same source differ
  in 255,683 bytes.** Same function names at the same offsets, different
  bytes inside the bodies, and no gate can currently see it. Detail and the
  first experiment are in `codex/compiler/compiler-backlog.md`.

- **`COMPILER-5`: a hex literal past i64-max breaks the text round-trip.**
  Filed by reek during the A5 work.

- **27 skipped tests**, catalogued by `build/audit-skips.ps1`: 7 REAL, 6
  TRIVIAL stubs that assert nothing, 13 with no `.expected`, 1 STALE that
  now passes. The trivial stubs are the ones that read as coverage and are
  not.

- **The 0xFE8 RAM-size cell is a private ABI between the harness and the
  guest.** Steve Howell named it as a compromise during PR 63 and he is
  right: nothing in the compiler writes that cell, and the multiboot
  header does not set MEMORY_INFO, so the guest never asks the loader the
  question whose answer it is handed. Retiring the cheat is open work.
