# Quires: Structural Units of a Codex

## Problem

Today a Codex compilation is a flat concatenation of every `.codex`
file the CLI walks, bucketed by `Chapter: X` headers in the source.
That's it. There is no grouping unit above chapter, and two files in
completely unrelated parts of the tree can both declare `Chapter: X`
and silently merge into one chapter.

A recent session hit exactly this: an earlier agent added a second
`Chapter: Hamt` file. The scanner merged it with the real `Hamt`
chapter. Downstream scoping, renaming, and collision detection all
operated on the merged bag as if it were one chapter. Whether the
ref compiler or the self-host emitted *this* `hamt-get` or *that*
`hamt-get` at any given call site was, at best, undefined.

Silent merging of same-named chapters is a bug class the language
has to close. It's also the cause of downstream behavior drift —
adding an unrelated top-level def changes which function a call
resolves to, because the name-resolution tiebreaker depends on
source-order, which depends on what else is in the bag.

We already have a partial mechanism: `Page N of M` markers at the
end of files that intentionally split one chapter across multiple
files (today: `Parser` ×3 in `Syntax/`, `X86-64 Code Generator` ×3
in `Emit/`, `Type Checker` ×2 in `Types/`, `Lowering` ×2 in `IR/`,
`CSharp Emitter` ×2 in `Emit/`). The markers are parsed but not
enforced — they're decoration.

## The vocabulary

A Codex compilation has four structural levels, all authentic
medieval-codex terms:

| Level     | What it is                                                   | On disk                              |
|-----------|--------------------------------------------------------------|--------------------------------------|
| **Codex** | The whole compilation                                        | The root directory                   |
| **Quire** | A gathering of chapters                                      | A top-level subdirectory of the root |
| **Chapter** | A named unit of definitions, type defs, effect defs       | One or more `.codex` files           |
| **Page**  | A file's portion of a multi-file chapter                     | A `Page N of M` marker inside a file |

A *codex* (pre-binding book form) is literally a loose collection of
gatherings. A *quire* is one such gathering — a stack of pages about
to become part of the larger work. That matches what we're doing:
the repo is a codex; each top-level directory is a quire; quires
hold chapters; chapters span pages.

## Filesystem mapping

- The **root directory** is the codex. Its `main.codex` sits directly
  in the root — it is part of the codex itself, not of any named
  quire.
- Every **top-level subdirectory** of the root is a quire. The
  quire's name is the subdirectory's basename.
- **Only one level of subdivision.** A quire may itself contain
  subdirectories, but the compiler **ignores** them — `.codex` files
  inside sub-subdirectories are not scanned. Quires do not nest.

Applied to today's tree (`Codex.Codex/`):

```
Codex.Codex/                  ← the codex
├── main.codex                ← in the codex itself (no quire)
├── Ast/                      ← quire: Ast
├── Core/                     ← quire: Core
├── Emit/                     ← quire: Emit
├── IR/                       ← quire: IR
├── Semantics/                ← quire: Semantics
├── Syntax/                   ← quire: Syntax
└── Types/                    ← quire: Types
```

This leaves the option to add deeper directories later for
organization that is *not* compilation-relevant (e.g., docs, tests,
scratch files), without changing what the compiler sees.

## Within-quire rules

Inside a single quire:

1. **Chapter names are unique within a quire** — with one exception.
2. **A chapter may be split across multiple files via `Page N of M`
   markers**, provided all pages are present and coherent: every
   integer in `1..M` appears exactly once, all pages declare the same
   M, and all pages live in the same quire.
3. **Any other repetition of a chapter name within a quire is an
   error** — caught at scan time, with a message naming both files
   and suggesting either a rename or page markers.

Across quires, chapter names may collide freely. Two `Chapter: Hamt`
files in two different quires are two different chapters.

## Cite syntax

Cross-quire calls **require** an explicit cite. A method call must
not silently leave its quire — if code in quire A reaches into a
chapter of quire B, the file declaring the caller must say so.

The required form:

```
cites <Quire> chapter <Chapter> (<name>, <name>, …)
```

The `chapter` keyword is mandatory. It disambiguates where the
(possibly multi-word) quire name ends and the (possibly multi-word)
chapter name begins — a real ambiguity, since today's codebase
already has quire names that would swallow the start of a chapter
name otherwise.

Example:

```
cites Foreword chapter LinkedList (add, remove, clear)
```

Space-separated keyword form was chosen over `/`, `.`, and `::`:

- `.` collides with record-field access.
- `/` is visually noisy and Damian dislikes it.
- `::` is borrowed from languages we are not trying to imitate.

The space-keyword form also reads aloud as English prose, which
matches the literate-programming aesthetic of the language.

### Existing cites migrated

All current cites live in `Codex.Codex/main.codex`. Under the new
rule they become:

```
cites Emit chapter CSharpEmitter (emit-full-chapter)
cites Emit chapter CodexEmitter (emit-type-defs, emit-def, collect-ctor-names)
cites Types chapter TypeChecker (resolve-type-expr)
cites Emit chapter X86-64 Code Generator (x86-64-emit-chapter)
```

Note that `X86-64` in the current source is the short form of the
chapter title `X86-64 Code Generator` — the cite-rewrite either
carries the full title or we adopt a canonical short-name mechanism
(see Open Questions).

## Call-site resolution

Within a quire, chapter names are unique, so unqualified references
to a chapter's definitions are unambiguous and resolve locally.

Across quires, the cite declaration is the resolution mechanism.
After `cites Emit chapter CodexEmitter (emit-def, …)` at the top of
`main.codex`, the identifier `emit-def` in that file resolves to the
`CodexEmitter` chapter's `emit-def`. The same call site with no cite
is an error — there is no implicit cross-quire resolution, and there
is no inline-qualified form at the call site. If you want to call
into another quire, you cite it.

## Why no inline cross-quire call syntax

The temptation is to allow something like `Emit chapter CodexEmitter
emit-def` at the call site, mirroring the cite form. We are *not*
adding this. Reasons:

1. Cites already do this cleanly — a file's set of cites is also its
   set of cross-quire imports, readable at a glance.
2. Inline qualification invites scattered cross-quire dependencies
   that are hard to audit.
3. Every inline-qualified call can be written as a cite plus an
   unqualified call; the cite form is not less expressive, only more
   structured.

## What this closes

- The HAMT-collision class of bugs: two files silently sharing a
  chapter name.
- Downstream non-determinism in name resolution: adding an unrelated
  def no longer shifts which chapter's function wins at a call site,
  because the winner is picked by the cite, not by source-order
  tiebreaking.
- The "what is the compilation unit?" question: the compilation unit
  is the codex (the root directory). Files, chapters, and quires are
  sub-structure of it.

## Open questions (not yet decided)

1. **Same-quire cites.** Must chapters in the same quire be cited
   too, or are they implicitly in scope because within-quire
   uniqueness is enforced? Today's code treats them as in scope.
   Leaving them implicit is simpler; requiring same-quire cites
   would be more symmetric.
2. **Codex-quire (root) naming.** When code in a named quire wants
   to cite a chapter that lives in the root (alongside `main`), what
   quire name does it use? Candidates: a reserved keyword (`codex`),
   an empty name, or disallow root-quire chapters from being cited
   at all (the root contains `main` only, by convention).
3. **Canonical chapter naming.** Chapters today have both a header
   title (`Chapter: X86-64 Code Generator`) and, in cites, a short
   form (`X86-64`). The quire model tightens the ambiguity: decide
   whether cites must use the exact chapter title, whether an alias
   mechanism exists, or whether titles are required to be
   single-token.
4. **Page-marker completeness enforcement.** Today `Page N of M`
   is parsed but not validated. Under the quire model it becomes
   load-bearing — all M pages must be present in the same quire,
   numbers 1..M exactly once. Exact diagnostic codes and wording TBD.
5. **Migration plan.** Land scanner changes first (detect violations
   and error), then migrate `main.codex`'s cites, then enforce the
   new cite form as the only valid one. Each step must keep the text
   pingpong green.

## Not in scope

- Changing the chapter/page vocabulary itself — chapters and pages
  stay.
- Nested quires. Intentionally excluded to keep the metaphor flat
  and the filesystem walk predictable.
- Cross-codex references. A codex is the unit of compilation; there
  is no syntax for reaching into a *different* codex. If we ever
  need that, it becomes a separate design.
