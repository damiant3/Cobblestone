# Quires: Structural Units of a Codex

## Problem

Today a Codex compilation is a flat concatenation of every `.codex`
file the CLI walks, bucketed only by `Chapter: X` headers in the
source. There is no grouping unit above chapter, and two files in
completely unrelated parts of the tree can both declare `Chapter: X`
and silently merge into one chapter.

A recent session hit exactly this: an earlier agent added a second
`Chapter: Hamt` file. The scanner merged it with the original `Hamt`
chapter. Downstream scoping, renaming, and collision detection all
operated on the merged bag as if it were one chapter. Whether the
compiler emitted *this* `hamt-get` or *that* `hamt-get` at any given
call site was undefined.

Silent merging of same-named chapters is a bug class the language
has to close. It is also the cause of downstream behavior drift —
adding an unrelated top-level def today can change which function a
call resolves to, because the name-resolution tiebreaker depends on
source-order, which depends on what else is in the bag.

A partial mechanism already exists: `Page N of M` markers at the end
of files that intentionally split one chapter across multiple files
(today: `Parser` ×3 in `Syntax/`, `X86-64 Code Generator` ×3 in
`Emit/`, `Type Checker` ×2 in `Types/`, `Lowering` ×2 in `IR/`,
`CSharp Emitter` ×2 in `Emit/`). These markers are parsed but not
enforced — decoration only.

## The vocabulary

A Codex compilation has four structural levels, all authentic
medieval-codex terms:

| Level       | What it is                                              | On disk                              |
|-------------|---------------------------------------------------------|--------------------------------------|
| **Codex**   | The whole compilation                                   | The root directory                   |
| **Quire**   | A gathering of chapters                                 | A top-level subdirectory of the root |
| **Chapter** | A named unit of definitions, type defs, effect defs     | One or more `.codex` files           |
| **Page**    | A file's portion of a multi-file chapter                | A `Page N of M` marker inside a file |

A *codex* (pre-binding book form) is a loose collection of
gatherings. A *quire* is one such gathering — a stack of pages about
to become part of the larger work. That matches what we are doing:
the repo is a codex, each top-level directory is a quire, quires
hold chapters, chapters span pages.

## Filesystem mapping

- The **root directory** is the codex. It is the composition root.
  `main.codex` sits directly in the root — it is part of the codex
  itself, not of any named quire.
- Every **top-level subdirectory** of the root is a quire. The
  quire's name is the subdirectory's basename.
- **Only one level of subdivision.** A quire may contain deeper
  subdirectories, but the compiler **ignores** them — `.codex` files
  inside sub-subdirectories are not scanned. Quires do not nest.
- Every `.codex` file must declare a `Chapter: <name>` header.
  (Today's 39 source files all do; this formalizes the convention.)

Applied to today's tree (`Codex.Codex/`):

```
Codex.Codex/                  ← the codex (composition root)
├── main.codex                ← in the codex itself (no quire)
├── Ast/                      ← quire: Ast
├── Core/                     ← quire: Core
├── Emit/                     ← quire: Emit
├── IR/                       ← quire: IR
├── Semantics/                ← quire: Semantics
├── Syntax/                   ← quire: Syntax
└── Types/                    ← quire: Types
```

Deeper directories are available for organization that is *not*
compilation-relevant (docs, tests, scratch) without changing what
the compiler sees.

## Scoping rule

**The only implicit scope is the current chapter.** A def may
reference other defs, type defs, and effect defs declared in the
same chapter (whether on the same page or a different page of that
chapter) without any qualification. Every other reference requires
a cite — even to a chapter in the same quire.

Rationale: this makes `cites` the single, uniform mechanism for
expressing cross-chapter dependencies. A file's cites are its
complete inbound-dependency list, readable at a glance, regardless
of whether the dependency is in-quire or cross-quire. It also closes
the source-order-tiebreaker class of bugs — no unqualified name
can silently bind to a different chapter's def because someone
reordered the file walk.

## Within-quire rules

1. **Chapter names are unique within a quire.** Same-named chapters
   in the same quire are an error, caught at scan time, with a
   diagnostic naming both files and suggesting either a rename or
   page markers.
2. **A chapter may be split across multiple files via `Page N of M`
   markers.** All M pages must be present in the same quire, each
   integer in `1..M` appears exactly once, and every page declares
   the same M. Missing pages, duplicate page numbers, disagreeing M,
   or pages in different quires are compiler errors.
3. **Chapter names may collide freely across quires.** Two
   `Chapter: Hamt` files in two different quires are two different
   chapters. The quire name is part of the chapter's identity.

## Cite syntax

Every cross-chapter reference — same quire or not — requires an
explicit cite:

```
cites <Quire> chapter <Chapter Title> (<name>, <name>, …)
```

- The `chapter` keyword is mandatory. It disambiguates where the
  (possibly multi-word) quire name ends and the (possibly multi-word)
  chapter title begins. Without it, `cites Emit CSharp Emitter (…)`
  would be structurally ambiguous.
- **Chapter titles in cites use the exact title from the
  `Chapter:` header.** No shortening, no slugs, no canonical
  aliases. Multi-word titles are allowed and common.
- Quire names are the exact subdirectory basename.
- The parenthesized list is the set of names imported into local
  scope by this cite.

Example:

```
cites Emit chapter CSharp Emitter (emit-full-chapter)
```

Space-separated keyword form was chosen over `/`, `.`, and `::`:

- `.` collides with record-field access.
- `/` is visually noisy.
- `::` is borrowed from languages we are not imitating.

The space-keyword form also reads as English prose, matching the
literate-programming aesthetic.

### Self-quire cites

A chapter in quire `Syntax` that needs defs from another chapter in
quire `Syntax` uses the same cite form with its own quire name:

```
cites Syntax chapter Lexer (tokenize)
```

There is no abbreviated same-quire form. Uniformity wins over
brevity — every cite looks the same whether it crosses a quire
boundary or not.

### The root is not a cite target

The codex root (where `main.codex` lives) is the composition root.
Code in the root may cite into any quire; no quire may cite into
the root. There is no reserved quire name that refers to the codex
root. Defs that need to be shared belong in a quire.

### The migration table

All current cites live in `Codex.Codex/main.codex`. Under the new
rule they become:

```
cites Emit chapter CSharp Emitter (emit-full-chapter)
cites Emit chapter Codex Emitter (emit-type-defs, emit-def, collect-ctor-names)
cites Types chapter Type Checker (resolve-type-expr)
cites Emit chapter X86-64 Code Generator (x86-64-emit-chapter)
```

Today's source uses short forms (`CSharpEmitter`, `X86-64`) that are
inferred against the real multi-word headers (`CSharp Emitter`,
`X86-64 Code Generator`). The "full title only" rule retires that
inference layer.

## Call-site resolution

At a call site:

1. If the name is declared in the current chapter (any page), it
   resolves to that def. Done.
2. Otherwise, the name must be introduced by a cite at the top of
   the enclosing file. The cite's `(name, name, …)` list is the set
   of identifiers it brings into local scope, each resolving to the
   cited chapter's definition.
3. A name that is neither local-chapter nor cite-imported is an
   error.

There is no inline-qualified call syntax (`Emit chapter CodexEmitter
emit-def`). Every inline-qualified call is expressible as a cite
plus an unqualified call; the cite form is not less expressive, only
more structured, and keeps cross-chapter dependencies at the top of
each file.

## What this closes

- **Silent chapter-name collisions**, within a quire (now an error)
  and across quires (now well-defined — different quires mean
  different chapters).
- **Source-order-dependent name resolution.** Adding an unrelated
  top-level def can no longer shift which chapter's function wins
  at a call site, because the winner is picked by the cite, not by
  the tiebreaker.
- **The compilation-unit question.** The compilation unit is the
  codex (the root directory). Files, pages, chapters, and quires are
  all sub-structure of it.
- **The short-name-vs-title ambiguity.** Cites use the exact title.

## Migration

Single change, not phased. Do the minimum consistent set in one
branch:

1. Scanner: enforce within-quire chapter-name uniqueness (error on
   violation); enforce `Page N of M` coherence; stop scanning below
   depth 2.
2. Cite parser: require `<Quire> chapter <Chapter Title> (…)`.
3. Rewrite the four cites in `main.codex`.
4. Pingpong must stay green at the end of the branch.

Phased migration would leave the compiler in an inconsistent state
between steps and invite iteration burn. One branch, one merge.

## Not in scope

- **Nested quires.** Intentionally excluded — quires do not nest,
  to keep the metaphor flat and the filesystem walk predictable.
- **Inline cross-chapter call syntax.** Cites are the only
  cross-chapter mechanism.
- **Cross-codex references.** A codex is the unit of compilation and
  distribution. There is no syntax for reaching into a *different*
  codex. If you need code from elsewhere, bring it in as source.
  We are not building a library/linker model — that pushes us into
  versioning, ABI stability, and the rest of the 16-bit-era
  complexity that Codex is deliberately avoiding.
- **Changing the chapter/page vocabulary.** Chapters and pages stay.

## Encoding

Quire names and chapter titles are CCE text, same as all compiler
internals. Anything outside CCE Tier 0 is normalized to `?` at the
I/O boundary on the way in. Name a folder in Chinese and you will
get `????????`, and your cites will have to match the
`????????` the scanner produced. That is working as intended
until the compiler learns to handle tiers above T0 — at which point
the normalization layer is replaced, not the design.

## Open questions

1. **One chapter per file, or can a file declare multiple?** Today
   all 39 source files declare exactly one `Chapter:` header.
   Requiring "one chapter per file" simplifies the scanner and makes
   the file the natural page boundary. The alternative (multiple
   chapters per file) is not free and has no current use case.
   Defaulting to "one chapter per file" until someone names a case
   for the other.
