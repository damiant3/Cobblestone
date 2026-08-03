# GitHub Update 20 -- 2026-06-02

## Language

- **For-expressions.** `for x in xs do f x` syntactic sugar for map loops. Dogfooded across ~50 manual map loops in 19 files (apps, foreword, OS). Desugars to `list-map` with a lambda; the parser handles the `for`/`in`/`do` keyword triple.

## Compiler

- **CHECK phase heap reduction.** Eliminated 40.8 MB redundant `expr-type` resolution in CHECK post-processing -- LOWER already deep-resolves after lookup. Combined with CHECK deck-exit pattern (inference scratch to bivy), net saving ~80 MB peak heap on selfhost.
- **CDX RESOLVE optimization.** Reuses pre-resolved `all-bindings` from CHECK, eliminating a redundant `resolve-all-bindings` pass in the CDX path.
- **Dead field removal.** Removed unused `types` and `type-env` fields from `CompileChecked` record.
- **Bivy escape fix.** `deck-record` wrapper on `pre-sorted-ust` fixes a latent bivy escape where sorted UST data survived on bivy past phase-compact.
- **Fixed-point restoration.** CL 2975 (reek): restored `deck-record` on `lower-chapter` (accidentally reverted by a merge-down conflict resolution) and reverted `resolve-ty-deep` address-of short-circuit that leaked bivy pointers into deck data. Hard fixed point restored.
- **EOF settle counter.** Serial EOF detection now requires 64 consecutive empty polls before declaring EOF, replacing the single-shot boolean flag. Gives the UART FIFO adequate time to drain between compilations in REPL batch mode. Codegen change -- seed rebuilt (two-pass convergence).
- **Unused cites cleanup.** 609 unused `cites` directives removed across 246 files. Lint tool identifies unused chapter references; 6 Tuple cites in compiler retained (needed for tuple syntax desugaring).

## Testing

- **41 slow-to-regular promotions.** Tests previously gated behind `-Slow` now run in the standard battery (crypto, geometry, audio, UI, sort, stats, etc.).
- **19 new test implementations.** App-level tests for annotations, historian, narrator, JSON parse, sidecar load, trust explorer, verdict publish, and more.
- **9 disk test un-skips.** Block I/O, boot init, and disk-facts tests now run with `.disk` sidecars instead of being skipped.
- **Gate battery: 211 total, 201 pass, 0 fail, 10 skip** (up from 208/157/0/51).

## Apps

- **ExaminersAssay.md** -- new design document for the code examination framework.
- **Backup.codex** -- fixed sha256 API to use `sha256-to-hex(sha256(text-to-bytes(...)))` instead of retired `sha256-hex`.
- **Row.codex** -- fixed column encoding order (was reversed due to prepend instead of append in `row-encode-values`).
- **db-test.codex** -- switched to `print-line-uni` for Unicode output matching; restructured act-block nesting.
- **DevConsole** -- UEFI responsiveness improvements, code browser integration.
- **lint-unused-cites** -- new tool for identifying unused chapter references.

## README

- Seed digest updated (`88762D2C...`, 2,654,334 bytes).
- Test battery stats updated (211 total / 201 pass / 0 fail / 10 skip).
- Foreword Core count updated (91 modules).
- New milestone row: for-expressions + phase heap reduction + EOF settle.

## Stats

- Seed: 2,654,334 bytes (SHA256 `88762D2C23E737742E762BC32E83DC483359D6197FE9331A76B15AF8AF009AF2`)
- CDX hard fixed point on bare metal (SUT === stage1, one pass); 211 total / 201 pass / 0 fail / 10 skip
- 237 foreword modules, 54 compiler files, 107 plug files, 319 app modules
- 611 total test .codex files (including 205 app-level tests)
