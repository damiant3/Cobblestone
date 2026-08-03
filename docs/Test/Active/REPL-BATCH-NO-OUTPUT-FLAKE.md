# REPL Batch No-Output Compile Flake

## Summary

Nondeterministically, a test in a Phase 1 REPL batch compile produces
NO per-test output (no `.exitcode`, no `build.log` in
`test-output/<name>/`). The classifier then defaults the exit code to
99: a `.expected` test reports FAIL_COMPILE, a `.failing` test reports
FAIL_WRONG_DIAGNOSTIC (empty log matches no codes). The same test
compiles clean in an exact positional re-run of the same batch list.

## Status: OPEN -- root cause unknown

Not load-related: sighting 3 occurred with no other VM work running.

## Sightings

1. 2026-06-10 -- `erp-db-test` failed its first compile, passed on
   retry at identical effective memory (the pre-CL-3732 `-mem 4096`
   "retry" was a clamped-to-2GB placebo, so memory did not change).
2. 2026-06-10 -- `erp-server-test` FAIL_COMPILE at position 52 of
   battery batch-3; compiled clean in an exact positional re-run.
3. 2026-06-11 -- `multiline-app-continuation` (errors test) at
   position 49 of 51 in batch-1 during the CL 3787 gate run:
   output directory entirely empty (no exitcode, no log) →
   FAIL_WRONG_DIAGNOSTIC. Exact batch re-run produced the correct
   single CDX1070 error and PASS.

## Diagnosis Protocol

Before blaming a change, reproduce the exact context:

```powershell
build/test-compile-batch.ps1 -ListFile test-output/_batches/batch-N.txt -OutRoot <tmp>
```

If the re-run passes, it was this flake. Note the failing test's
POSITION in the batch -- all three sightings are mid-to-tail of their
batch (52, 49), consistent with a per-iteration REPL state or serial
stream issue rather than a per-test one.

## Hypotheses (untested)

- WHP scheduling stall causing the harness to read a truncated
  serial stream and drop the tail test's section markers.
- REPL loop reset edge between iterations (heap/deck/eof flags) that
  occasionally eats the next compile's banner, desyncing the
  batch splitter in test-compile-batch.ps1.
- Host-side splitter losing the final section when the VM exits
  before the last flush.

## Mitigation: Retry (CL 4757/4763, 2026-06-18)

Phase 1a in test.ps1: after batch compilation, any test with exitcode
99 (VM died before reaching it) is retried individually via
compile.ps1 with its own fresh VM. This handles both heap exhaustion
(heavy tests at end of batch) and REPL serial desync (intermittent
no-output). The root cause remains uninvestigated but the retry
eliminates the user-visible flake.

## Next Steps (if retry is insufficient)

- Instrument test-compile-batch.ps1 to preserve the RAW batch serial
  stream on any missing-output test (currently discarded), so the
  next sighting shows whether the guest never emitted the section or
  the host lost it.
