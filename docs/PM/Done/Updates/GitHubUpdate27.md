# GitHub Update 27 -- 2026-06-24

Covers main CLs 5738-6065 (since Update 26 at CL 5737, 2026-06-22).
Three days, 70 copy-ups from four agent streams (fester, reek, val, blu).

## ARM64 Backend -- 86% → 96% Cross-Tests

31 CLs on the ARM64 plug, the largest push of any agent this cycle.

- **O(1) amortized list-push** (CL 6065) -- rewrote `list-empty`,
  `list-push`, `list-cons`, `list-append` to use x86-compatible
  capacity layout with capacity qword at `[ptr-8]`. Push is O(1)
  when `length < capacity` (in-place store, increment length,
  return same pointer). Grow path doubles capacity and copies.
  Initial capacity 4. Eliminates the O(n²) heap blow-up that
  caused font record corruption in TrueType cross-tests (~462 KB
  wasted for a 352-element list → ~3 KB).

- **list-cons argument order fix** -- was X0=list/X1=head, should
  be X0=head/X1=list to match codegen convention. Caused silent
  wrong results on any prepend operation.

- **try/fail mechanism** (CL 5991) -- full retry/fallback/failure,
  was previously a stub returning 0.

- **Closure arity** (CL 6011) -- use type-derived arity instead of
  name lookup (foreword functions got arity=1).

- **alloc-bytes runtime** (CL 6005) -- was clobbering X0 before
  using size argument.

- **f32 vector ops** (CL 6017) -- single-precision FP binary/cmp
  with `arm64-fcmp-s`/`fmov-to/from-fp-s` encoder. Saturating
  branch offset fix (+12 not +8).

- **peek/poke-byte address remap** (CL 6020) -- low addresses
  remapped to +0x40000000 for ARM64 RAM region on Renode.

- **vec-select output pointer** (CL 6023) -- use local instead of
  temp (temp clobbered by lane loop).

- **Field suffix matching** (CL 6032) -- TtfFont/TtfHead/TtfGlyph
  hardcoded field indices for foreword types.

- **Fast cross-test harness** (CL 5995) -- `test-cross-batch.ps1`
  cuts battery time from 27 min to 11 min with parallel Renode.

- Earlier session CLs (5746-5948): ARM64 disassembler, effect
  handler spill, arity-tagged closures, free-var scoping, sum-eq,
  O(1) list-set-at, 9+ param stack passing, Real negation, vec-cmp
  pointer fix, Renode board 256MB RAM.

## GUI OS -- TrueType Runtime + GOP Polish (reek)

16 CLs from the MutableRecords stream:

- **TrueTypeWriter runtime fixes** -- coordinate flag encoding,
  cmap length, pow2-length tables, head table field order.

- **GopRender** -- TrueType rendering bridge for the GOP
  framebuffer, threading support for font rasterization.

- **Dynamic GOP dimensions** -- resolution selection at runtime.

- **FontGen** -- procedural font generator, AI-to-TrueType
  pipeline. FontExplorer desktop app for font preview and training.

- **FontLoad** -- generated font integration, system font
  management.

- **TrackerApp cleanup** -- sprint engine, issue DB polish.

- **fontai** -- PTX MLP kernels for font style learning, training
  scripts, batch generation pipeline.

- **WinForms plug** -- AST traversal improvements, GPU dispatch
  builder.

## RISC-V Backend (val)

11 CLs from the CodexMagic stream:

- Multi-field record construction, partial application fixes.
- User-function dispatch, lambda captures.
- Cross-test progress documentation.

## Shell DSL + Infrastructure (blu)

12 CLs from the Mountain stream:

- **Shell DSL** -- Bash, PowerShell, Ksh emitters with typed AST.
  `ShellBuild` orchestrator for cross-shell script generation.

- **x86-64 TCO self-call emission** -- tail-call optimization fix.

- **Integration hygiene** -- Mouse record field tracking, stale
  field references cleaned across helm-full-test and related tests.

- **README + docs refresh** -- IRISA research harvest, proof
  reading reform.

## By the numbers

| Metric | Update 26 | Update 27 | Delta |
|--------|----------:|----------:|------:|
| Foreword modules | 360 | 367 | +7 |
| OS/Kernel modules | 147 | 137 | -10 (reorg) |
| App count | 57 | 59 | +2 |
| Seed size | 2.31 MB | 2.31 MB | -- |
| Seed digest | `EF14F3A5` | `E625476A` | -- |
| ARM64 Renode tests | 62 | ~131/137 (96%) | +69 |
| RISC-V Renode tests | 44 | ~50 | +6 |
| Copy-ups | 198 | 70 | -- |
| Days | 5 | 3 | -- |
| Agent streams | 4 | 4 | -- |

## What's next

ARM64 second-call glyph bug (ttf-glyph-for-char returns zeroed
fields on second invocation -- font intact, state corruption in
recursive parsing chain). RISC-V lambda captures. GUI OS window
management. Font hinting for crisp small sizes.
