# Val Handoff — 2026-06-13

## Session Summary

Massive feature session. Started with punctual Phase 2 WIP, ended with
unit families, implicit conversion, and 27 physical unit domains.

## What Shipped (main CLs 3981-4136, ~30 copy-ups)

### Unit Types + Unit Families
- `unit` keyword, `UnitTy` variant in CodexType, zero-overhead codegen erasure
- `unit family` keyword — domain-polymorphic units (`Length = unit family Millimeter`)
- Arithmetic preserves units, scalar ops work, cross-unit is type error
- Implicit conversion at call sites — pass Celsius where Kelvin expected
- Conversion synthesis from `1 Minute = 60 Second` declarations
- 27+ unit families in `codex/foreword/core/Units.codex` (physics, engineering, medical, nuclear)
- `Timestamp`/`Elapsed` families in DateTime, applied to RateLimiter

### Punctual (Hard Real-Time)
- WCET switched from byte count to instruction count (honest — compiler counts what it knows)
- Per-function instruction budgets (`punctual 128 name`)
- Structural checks walk let bindings + match arms (CDX6001-6005 all fixed)
- Missile warning example with Ada/Ravenscar side-by-side
- Comprehensive prior art survey in HardRealtime.md (5-tier landscape)
- Novel — no production language has per-function bounded-execution with instruction counting

### Test Consolidation
- 232 → 137 tests via 11 smoke bundles
- Battery 424s → 217s (full), BVT mode 18s
- Total build time 519s → ~135s

### Docs + GitHub
- DevelopersGuide: `unit` + `punctual` sections
- ExaminersAssay: updated test counts
- HardRealtime.md: 5-tier prior art survey + Ada comparison table
- UnitFamilies.md: design doc
- CodexIoTPlan.md: progress addendum
- GitHub/GitLab pushed (Update 23, commit 4d8de40b)
- LinkedIn post drafted

## Current State

- Main at CL 4136. All gates green. BVT 18s.
- Seed installed with all features.
- No pending CLs, no shelved work.
- Punctual WIP (CL 3965) was consumed — all features landed separately.

## What's Next

### Continue Spreading Unit Types to Forewords
Priority targets (from the Explore agent audit):
1. **Schedule.codex** — `every-seconds`/`every-minutes`/`every-hours` should use Duration family
2. **NTP.codex** — `NtpTimestamp` should use Timestamp family
3. **DHCP.codex** — lease time should be Elapsed
4. **VgaGraphics.codex** — pixel coords vs byte offsets (PixelX, PixelY, ByteOffset families)
5. **Tensor.codex** — Rows/Cols unit types to prevent dimension swaps
6. **MessageFraming.codex** — ByteOffset unit type for frame parsing

### Tighten Cross-Domain Safety
Currently `Celsius` unifies with `Meter` (both `unit Integer`). The
unifier should only accept cross-unit when a conversion function
`From-to-To` actually exists. Check in the unifier by probing the
type-def-map for the conversion function name.

### Temperature Implicit Conversion
Non-linear conversions (Celsius → Kelvin is `+ 273`, not `* N`)
can't use the auto-synthesis path. The manual functions exist and
work with implicit conversion, but only because the unifier is
permissive. Tightening cross-domain safety (above) will break
temperature implicit conversion unless we add affine conversion
support to the synthesis.

## Files Modified This Session

### Compiler (17 files across pipeline)
- Token.codex, Lexer.codex, ParserCore.codex, SyntaxNodes.codex, Parser.codex
- AstNodes.codex, Desugarer.codex
- CodexType.codex, CodexTypeHelpers.codex, TypeChecker.codex, TypeCheckerInference.codex, Unifier.codex
- IRChapter.codex, Lowering.codex, LambdaLifting.codex, ResolveTypes.codex
- X86_64.codex, X86_64State.codex, X86_64Compound.codex, X86_64Chapter.codex
- CodexEmitter.codex, IRTextEmitter.codex
- NameResolver.codex, ChapterScoper.codex, opening.codex, BuildSettings.codex, CdxCodes.codex

### Foreword
- Units.codex (new — 27 unit families)
- DateTime.codex (Timestamp/Elapsed families)
- RateLimiter.codex (uses Timestamp)
- Schedule.codex (renamed `unit` variable to `time-unit`)

### Tests
- 11 new smoke bundles replacing ~99 individual tests
- punctual-smoke, unit-smoke, unit-family, unit-family-mixed, units-foreword, implicit-convert
- rt-smoke (CDX6001-6005 including CDX6003 closure)
- Missile warning example

### Docs
- HardRealtime.md (comprehensive rewrite of prior art section)
- UnitFamilies.md (new design doc)
- DevelopersGuide.md (unit + punctual sections)
- ExaminersAssay.md (updated counts)
- CodexIoTPlan.md (progress addendum)
- GitHubUpdate23.md
- README.md (new milestone row, backend table, test counts)
