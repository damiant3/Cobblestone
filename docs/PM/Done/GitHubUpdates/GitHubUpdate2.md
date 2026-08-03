# GitHub Update 2 -- CL 475 to CL 618 (2026-04-28 to 2026-05-01)

Previous update: CL 475 (commit `3757c94`).
This update: CL 618 (commit `2b38729`).

## Compiler Pipeline

**Phase modularization.** `compile-checked` split into four discrete
phase functions, each with its own result type:

| Function | Returns | Phase |
|----------|---------|-------|
| `compile-lex` | `LexResult` | tokenize, error check |
| `compile-parse` | `ParseResult` | scan, assignments, collisions, parse |
| `compile-desugar-and-scope` | `ScopeResult` | desugar, scope, resolve |
| `compile-type-check` | `CompileChecked` | type-check, capability check |

`compile-checked` is now a four-line orchestrator that calls each phase
and early-exits on errors.

**compile-text simplified.** Returns `Text` directly instead of
`CompileTextResult`. Callers manage their own heap-saves for measurement.

**compile-to-binary renamed to compile-to-elf.** New `compile-to-cdx`
alongside it for the CDX binary format.

**Phase diagnostics.** Every compile mode now emits `WD:PHASE-label:hwm`
lines showing heap position at each phase boundary. The harness strips
these (`WD:` prefix). `format-heap-marks` / `format-heap-marks-wd` with
configurable prefix. MEASURE mode retains `PHASE-` prefix for
`measure-compile.ps1` compatibility.

## Type Checker Bug Fix

**Locals leak across definitions.** `check-all-defs` shared a single
`TypeEnv` across all definitions. `infer-let-bindings` called
`env-bind-local` which uses `__record-set` (in-place mutation on bare
metal), so `let` bindings from one function's body leaked into subsequent
functions' type environments. A local `let pad-zeros = 55 - ...` in
`sha256-pad` shadowed the global `pad-zeros : Integer -> List Integer`
from ElfWriter, causing "Type mismatch: Integer vs Fun" errors in
unrelated code.

Fix: one line in `check-all-defs` -- reset `env.locals` to `[]` before
each `check-def`.

Bisected by adding the Sha256 foreword to the self-compile source and
progressively narrowing the trigger from 200 lines to the single
`sha256-pad` function.

## Lexer Optimizations

**LinkedList token accumulator.** `tokenize-loop` now accumulates tokens
in `LinkedList Token` (O(1) push) instead of `List Token` with
`list-snoc` (O(n) copy per append). One `__linked-list-to-list`
conversion at the boundary. Eliminates O(n^2) total allocation for n
tokens.

**LinkedList diagnostic accumulator.** Same treatment for `LexState.errors`.
`validate-escapes` renamed to `validate-escapes-into` -- takes an existing
`LinkedList Diagnostic` and pushes onto it instead of returning a fresh
list.

**Polymorphic linked-list builtins.** `__linked-list-empty`,
`__linked-list-push`, `__linked-list-to-list` changed from monomorphic
`LinkedList (List Integer)` to polymorphic `ForAll a => LinkedList a` in
TypeEnv.

**Bounded LexState fields.** `offset : Integer between 0 and 4294967295`,
`line/column/file-id : Integer between 0 and 65535`. Matches
`SourcePosition`/`SourceSpan` ranges from `Core/SourceText.codex`.

**One-liner constant style.** Character code constants in the lexer
collapsed from 3-line definitions to single-line
`cc-newline : Integer = char-code '\n'` form.

**tokenize-into simplified.** Dropped `pre-tokens` and `pre-errors`
parameters. The lexer creates its own linked lists internally.

## CDX Binary Format

New `--target cdx` / mode `CDX`. Emits CDX1 binaries with:

- 224-byte fixed header (magic `CDX1`, version, flags)
- SHA-256 content hash over text + rodata
- Capability table offset/size (currently 0 -- stub)
- Proof hash offset/size (currently 0 -- stub)
- Text and rodata section offsets/sizes
- Entry point offset, stack size (1 MB), heap size (512 MB)
- Trust score (5000) and reserved fields

Implementation: `x86-64-finalize-cdx` in X86_64Chapter.codex (mirrors
`x86-64-finalize-elf`), plus `cdx-build-header`, `cdx-le16/32/64`,
`cdx-zeros`, `cdx-hash-to-bytes` helpers. `CdxWriter.codex` provides
the higher-level `build-cdx` / `build-cdx-bare-metal` API.

`seed/Codex.cdx` -- the compiler in its own binary format, checked in
alongside the ELF seed.

## Foreword Library (15 new modules)

| Module | Category | Description |
|--------|----------|-------------|
| Sha256 | Crypto | SHA-256 hash (message schedule, compression, padding) |
| Sha512 | Crypto | SHA-512 hash |
| Hmac | Crypto | HMAC authentication (constant-time comparison) |
| Ed25519 | Crypto | Elliptic curve signatures (16-limb field arithmetic) |
| ProofOfWork | Crypto | Proof-of-work primitives |
| Hamt | Data | Hash array mapped trie |
| Queue | Data | Functional queue (front/back lists) |
| StringBuilder | Data | Mutable string builder |
| TextSearch | Data | Text search utilities |
| FactStore | Data | Distributed fact store with network serialization |
| CdxBinary | Binary | CDX format encoder/decoder/verifier |
| TrustLattice | OS | Trust score lattice (Hamt-backed) |
| PolicyEngine | OS | Rule-based policy evaluation (AllOf, AnyOf, Not) |
| AgentProtocol | OS | 7-message agent protocol |
| MessageFraming | OS | Length-prefixed message framing |
| Handshake | OS | Cryptographic handshake protocol |
| Forensics | OS | Audit trail reasoning chains |
| Concurrent | OS | Structured concurrency (fork/join/race/timeout) |

CL 615 removed `cites Codex chapter` lines from all forewords -- builtins
are globally available via TypeEnv without explicit cites.

## New Samples (29 new, 134 total)

**Crypto:** ed25519-test, sha256-test, sha512-test, hmac-test, pow-test
**Data:** hamt-test, queue-test, stringbuilder-test, textsearch-test, factstore-test
**OS:** trust-lattice-test, policy-test, agent-sim, forensics-test, handshake-test, framing-test, concurrent-test, with-timeout-test
**Codegen:** cdx-binary-test, bitwise-test, number-literal, effect-direction, write-binary-test
**Error diagnostics (.failing):** apply-non-function, arith-string-mix, cr-escape-text, tab-escape-text, infinite-type, record-no-comma, reserved-keyword-as-name, unknown-record-field, when-no-is

Sample battery: 125 pass, 0 fail, 9 skip (up from ~105 samples at CL 475).

## Tooling

**All .sh scripts deleted (CL 509).** PowerShell is the only harness
path. 14 shell scripts removed; all functionality ported to .ps1
equivalents.

**New/updated scripts:**

| Script | Purpose |
|--------|---------|
| `pingpong-self.ps1` | BS2 acceptance test (seed → SUT → stage1 === stage2) |
| `bootstrap3.ps1` | BS3 driver (ELF emits ELF, byte-identity gate) |
| `sweep.ps1` | Sample battery (parallel WHPX, `-Jobs N`) |
| `sample-compile-selfhost.ps1` | One-shot sample compile via SUT |
| `run-for-sweep.ps1` | Boot ELF in QEMU, capture serial output |
| `qemu-config.ps1` | WHPX config, dual-chardev, port allocation |
| `concat-codex-self.ps1` | Quire-aware source concatenation with foreword preloading |
| `measure-compile.ps1` | Per-phase heap mark profiling |
| `stress-sweep.ps1` | Multi-iteration sweep for stability |
| `clean-zombies.ps1` | Orphaned QEMU cleanup |

**Dual-chardev serial split (CL 396+).** COM1 = data (text/binary
output), COM2 = control (READY, HEAP, STACK, WD markers). All harness
scripts updated for two-channel protocol.

**WHPX BSOD mitigation.** `-machine kernel-irqchip=off` +
`-accel whpx,hyperv=off`. Stable at jobs=4. Per-agent port slots
(cam = 50200+, nib = 53900+) prevent collisions.

## Build and Test Procedures

### Full acceptance test (pingpong + sweep)

```powershell
tools/pingpong-self.ps1             # BS2: seed → SUT → stage1 === stage2
tools/sweep.ps1 -Jobs 4             # 134 samples, parallel
```

Pingpong phases:
1. Clean intermediates
2. Stage seed ELF + dump source via `concat-codex-self.ps1`
3. Self-build: seed compiles source → SUT (BINARY mode, ~47s)
4. Canary: SUT compiles + runs `samples/hello.codex`
5. Pingpong: SUT compiles source twice (TEXT mode), verify byte-identity

### Seed rebuild procedure

After any change to compiler source (`Codex.Codex/**/*.codex`):

```powershell
# 1. Run pingpong -- builds SUT from current seed, verifies fixed point
tools/pingpong-self.ps1

# 2. Copy SUT to seed
p4 edit seed/Codex.Codex.elf
Copy-Item build-output/bare-metal/Codex.Codex.elf seed/Codex.Codex.elf

# 3. Re-run pingpong with new seed -- verify hard fixed point
#    (seed compiles source → SUT byte-identical to seed)
tools/pingpong-self.ps1

# 4. Run full sweep
tools/sweep.ps1 -Jobs 4

# 5. Shelve/submit
p4 shelve -c <CL>
```

The hard fixed point gate: `seed.compile(source) == seed` byte-identical.
Both pingpong runs must PASS and sweep must be 0 fail before submit.

### Canary test (quick smoke test)

```powershell
tools/sample-compile-selfhost.ps1 -Src samples/hello.codex `
    -Out build-output/bare-metal/hello.elf -Log build-output/bare-metal/hello.log
tools/run-for-sweep.ps1 -Elf build-output/bare-metal/hello.elf `
    -OutFile build-output/bare-metal/hello.out
```

### Phase profiling

```powershell
tools/measure-compile.ps1
```

Prints per-phase heap positions and deltas:
```
PHASE-h0-start:4194728
PHASE-h1-tokenize:262509608
...
PHASE-h8-check:262509832
EMIT-BYTES:807406
```

### Bootstrap 3 (ELF emits ELF)

```powershell
tools/bootstrap3.ps1
```

Stage 1 ELF === stage 2 ELF byte-identical.

## Effect System Extensions

**Direction markers and scoped capabilities.** `EffectfulTy` extended
with a scope list. Parser supports dotted effect names
(`Capability.Direction`) and scope annotations. Steps 1-8 of capability
refinement implemented.

**`with-timeout` expression.** New `AWithTimeoutExpr` AST node, parsed
and lowered. Timeout mechanics in the Concurrent foreword.

**`check-opening-capabilities`.** Validates effect/capability constraints
on the `opening` entry point.

## Renamed Functions

| Old | New |
|-----|-----|
| `x86-64-emit-chapter` | `x86-64-emit-elf` |
| `x86-64-emit-chapter-with-exit-mode` | `x86-64-emit-elf-with-exit-mode` |
| `x86-64-emit-chapter-with-options` | `x86-64-emit-elf-with-options` |
| `x86-64-finalize` | `x86-64-finalize-elf` |
| `compile-to-binary` | `compile-to-elf` |
| `compile-to-binary-with-options` | `compile-to-elf-with-options` |
| `LexResult` (Lexer.codex) | `LexStep` |
| `validate-escapes` | `validate-escapes-into` |

## Seed

```
seed/Codex.Codex.elf    1,479,816 bytes    hard fixed point
seed/Codex.cdx          (new) CDX format   same compiler, Codex binary format
```

## Gate Status

| Gate | Status |
|------|--------|
| BS2 (pingpong) | PASS -- stage1 === stage2 byte-identical |
| BS3 (bootstrap3) | PASS -- ELF === ELF byte-identical |
| Sweep | 125 pass, 0 fail, 9 skip of 134 |

## Known Issues

- **WHPX host BSOD:** 6 incidents documented. Mitigated by
  `kernel-irqchip=off` + `hyperv=off`, stable at jobs=4.
  Upstream: gitlab.com/qemu-project/qemu/-/work_items/3460.
- **Tokenizer heap:** 243 MB for 864 KB source. O(n^2) eliminated but
  per-token cons cells still dominate. Next target for optimization.
