# V2 — Narration Layer Design

**Date**: 2026-03-22
**Author**: Cam (Claude Code CLI)
**Status**: In progress

---

## Goal

Make `.codex` files read as documents where prose is load-bearing, not
decorative. The compiler understands prose templates and validates them
against the notation they introduce.

---

## What Exists (C# Bootstrap)

| Component | File | Status |
|-----------|------|--------|
| ProseParser | `src/Codex.Syntax/ProseParser.cs` (341 lines) | Chapters, sections, prose blocks, notation blocks |
| Templates | `ProseParser.Templates.cs` (323 lines) | Record ("An X is a record containing:") and variant ("X is either:") |
| AST nodes | `SyntaxNodes.cs` | DocumentNode, ChapterNode, SectionNode, ProseBlockNode, NotationBlockNode |
| Desugarer | `Desugarer.cs` | Prose documents compile through full pipeline |
| CLI | `Program.Parse.cs`, `Program.Formatting.cs` | `parse` and `read` commands handle prose |
| Sample | `samples/prose-greeting.codex` | 15-line greeting document |
| Tests | ProseParserTests (179 lines), ProseTemplateTests (151 lines) | 14 tests total |

---

## What V2 Delivers (6 Phases)

### Phase 1: Function Declaration Templates

Parse: `To deposit (amount : Amount) into (account : Account):`

Add `TryMatchFunctionTemplate` to `ProseParser.Templates.cs`. Pattern: line
starts with `To `, contains parenthesized `(name : Type)` parameters, ends
with `:`. Optional `gives Type` clause for return type.

Store extracted info on `ProseBlockNode` via new `FunctionTemplateInfo` record.
Do NOT generate synthetic definitions — the notation block below is the real
definition. Template info is used for consistency checking in Phase 5.

**AST additions**:
- `FunctionTemplateInfo(FunctionName, Parameters, ReturnType?, Span)`
- `ProseBlockNode.FunctionTemplate` (optional property)

**Tests**: basic, multi-param, gives-clause, no-colon-no-match, with-notation-below

---

### Phase 2: Claim and Proof Templates

Parse: `Claim: reversing a list twice gives the original.`
Parse: `Proof: by induction on the list.`

Fix: `NotationBlockNode` currently drops claims and proofs silently.
Extend it to carry `Claims` and `Proofs` lists. Update `CollectDefinitions`
and `ParseNotationBlock` to propagate them.

Recognize `Claim:` and `Proof:` lines in `ParseProseOrTemplate` as template
markers, similar to record/variant detection.

**AST changes**:
- `NotationBlockNode` gains `Claims` and `Proofs` lists
- Both have default empty values (backward compatible)

**Tests**: claim collected, proof collected, claim+proof pair, no-notation fallback

---

### Phase 3: Transition Markers

Recognize `We say:` and `This is written:` as load-bearing boundaries between
commentary and formal notation.

Add `ProseTransitionKind` enum: `None | WeSay | ThisIsWritten | ToDefine`.
Store on `ProseBlockNode`. Detect in `ParseProseOrTemplate` when accumulated
prose ends with a transition phrase.

This enables Phase 5 (consistency checking) to distinguish load-bearing
notation from incidental examples.

**Tests**: we-say detected, this-is-written detected, plain prose = None

---

### Phase 4: Inline Code References

Extract backtick-delimited code refs (`` `greet` ``) and parenthesized
PascalCase type refs (`(Account)`) from prose text.

Add `InlineCodeRef(Code, Start, End)` and `InlineTypeRef(TypeName, Start, End)`.
Store on `ProseBlockNode` as lists. Scan during `ParseProseBlock`.

Update CLI `read` rendering to highlight inline refs.

**Tests**: backtick extracted, type ref extracted, no refs = empty lists

---

### Phase 5: Prose-Notation Consistency Checking

Post-pass after `ParseDocument()`. Walk chapters/sections checking adjacent
prose-notation pairs for consistency.

Checks:
- Function template name matches following definition name
- Function template parameters match definition parameters
- Record template fields match notation fields
- Variant template constructors match notation constructors

Diagnostics:
- `CDX1101`: prose function name != notation definition name
- `CDX1102`: prose parameter != notation parameter
- `CDX1103`: prose record field != notation field
- `CDX1104`: prose variant constructor != notation constructor

New file: `ProseParser.Validation.cs`

**Tests**: matching = no warning, name mismatch, param mismatch, field mismatch

---

### Phase 6: Enhanced Rendering and Sample

Update CLI `read` to render function templates, transitions, inline refs,
claims/proofs with distinct formatting.

Create `samples/prose-banking.codex` — a full banking domain example
exercising all V2 features: chapters, sections, prose templates for records,
variants, functions, claims, proofs, transition markers, inline refs.

---

## Session Scope

**This session**: Phases 1–3 (function templates, claims/proofs, transitions).
Highest value, most tractable. ~90 minutes.

**Follow-up session**: Phases 4–6 (inline refs, consistency checking, rendering).

---

## Reference Compiler Lock Justification

All changes are in `src/Codex.Syntax/` (parser) and `tools/Codex.Cli/` (CLI).
They extend the prose parser — a forward-looking feature for `.codex` authoring.
No changes to notation parser, desugarer, type checker, or emitters. All new
AST properties have default values (backward compatible). No existing tests break.

---

## Diagnostic Codes

| Code | Severity | Message |
|------|----------|---------|
| CDX1101 | Warning | Prose function name does not match notation definition |
| CDX1102 | Warning | Prose parameter does not match notation parameter |
| CDX1103 | Warning | Prose record field does not match notation field |
| CDX1104 | Warning | Prose variant constructor does not match notation constructor |

---

## The Vision Connection

From `docs/Vision/NewRepository.txt`: "The original human-language description
should be the program." From `docs/ForFun/Clarifier.txt`: a system that reflects
utterances back showing what was successfully communicated and what wasn't.

V2 is the foundation. The prose templates are the first step toward a compiler
that understands English structure — not through NLP, but through recognized
patterns that map to formal constructs. The compiler checks the mapping.
The prose is the human interface. The notation is the machine interface.
The type signature is the bridge between them.
