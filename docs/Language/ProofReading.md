# ProofReading — Document Structure Reform

The compiler's internal concept of "module" is being replaced with the
language's natural document structure. The word "module" is out of theme
and out of character for a language called Codex. The source files already
use Chapter headers — the compiler internals should reflect that.

## Document Hierarchy

    Chapter > Page > Section > Definition

- **Chapter** — the compilation unit. What was called "module." One logical
  grouping of related definitions sharing a single namespace. A chapter may
  span multiple files (pages).
- **Page** — one .codex file. A chapter with one file has one page. A chapter
  split across files has numbered pages. Pages of the same chapter share
  scope — no name mangling between them.
- **Section** — organizational grouping within a page. Already exists in
  the syntax (`Section: Name`). No semantic effect today.
- **Definition** — a function, type, or value binding.

## Page Markers

Every .codex file ends with a page marker as the last line.

Single-file chapter:

    Page 1

Multi-file chapter (3 pages):

    Page 1 of 3       (first file)
    Page 2 of 3       (second file)
    Page 3 of 3       (third file)

Each file keeps `Chapter: Name` at the top regardless of page number.
The page marker appears only at the bottom. Page 1 of a multi-file
chapter is distinguished from a single-file chapter by `of N`.

## Compiler Validation

The compiler collects all files sharing a Chapter name, reads their page
markers, and enforces:

- **Missing page:** Page 2 of 3 exists but Page 1 of 3 does not.
- **Gap:** Pages 1 and 3 of 3 exist but Page 2 of 3 does not.
- **Duplicate:** Two files claim the same page number for the same chapter.
- **No marker:** A file has no page marker at all.
- **Count mismatch:** Files disagree on total page count (one says "of 3,"
  another says "of 4").

## Prose Preservation

The current build pipeline strips prose from compiled output, leaving only
code. This is wrong. The prose is part of the source — it describes the
functionality and will eventually be validated against the code.

With prose preserved, the `Chapter:` header in the source IS the chapter
boundary. The synthetic `module: slug-name` markers injected into the
stripped code stream become unnecessary and will be removed.

### Current state (what's broken)

The prose is discarded at parse time. Nothing in the AST, IR, or emitter
carries Chapter titles, Section titles, or descriptive text. The IR has
`IRModuleSection.Name` which is just a slug like `"parser"`, not the
original prose.

### Minimal approach (pre-MM4)

Thread prose through the IR as opaque metadata. Do not parse or compile
the prose — just carry it and reproduce it.

1. **Parser** — capture Chapter title, Section titles, and prose text
   between headers as raw strings.
2. **AST** — store prose blocks on the Module and on section boundaries
   within definitions (or as a parallel list of prose segments).
3. **Lowering** — carry prose strings onto IRModuleSection as metadata
   fields: `ChapterTitle`, `SectionTitle`, `Prose` (raw text).
4. **CodexEmitter** — emit Chapter/Section headers and prose text before
   each section's definitions, reproducing the original document structure.

This gets Chapter and Section headers into stage1 output without requiring
the self-hosted compiler to understand prose semantically.

### Full approach (post-MM4)

Parse prose into AST nodes. Validate that prose describes the functionality
and that functionality is described by the prose. This is the long-term
goal but not required for the current milestone.

### Dead code to remove

- `module: slug-name` streaming markers
- `is-module-marker` function
- `extract-module-slug` function
- All marker injection/parsing machinery

## Remove: Export

Export declarations control which names from a chapter are visible to
chapters that cite it. If no export declarations exist, everything is
exported. In practice, zero .codex files use export — every chapter
already exports everything.

There is no compiler optimization benefit. The export machinery is
purely a name-filtering step in the NameResolver. It does not reduce
compile time, enable dead code elimination, or affect codegen.

Hiding definitions goes against the philosophy of the language. If you
cite a chapter, you should see all of it. Export is removed entirely.

Dead code to remove:
- ExportDecl (AST node)
- ExportedNames field on ResolvedModule
- ComputeExportedNames in NameResolver
- export keyword in Lexer
- Export parsing in Parser

## Rename: Import to Cite

The keyword `import` is out of theme. Books don't import — they cite.
The replacement is `Cite`, used as a declaration at the chapter level:

    Cite: Lexer, Collections

"Reference" was considered but reads as a directive to the user.
"Cite" is a declaration of dependency — uncommon enough to invite
discovery, familiar enough to be understood.

| Old               | New              |
|-------------------|------------------|
| import            | cite             |
| ImportDecl (AST)  | CiteDecl         |
| Imports           | Citations        |
| ImportedModules   | CitedChapters    |

## Rename: Module to Chapter

Across the compiler internals:

| Old                 | New                  |
|---------------------|----------------------|
| Module (AST)        | Chapter              |
| IRModule            | IRChapter            |
| ResolvedModule      | ResolvedChapter      |
| ModuleScoper        | ChapterScoper        |
| FileModuleLoader    | FileChapterLoader    |
| ProjectModuleLoader | ProjectChapterLoader |
| PreludeModuleLoader | PreludeChapterLoader |
| CompositeModuleLoader | CompositeChapterLoader |
| module-slug         | chapter-slug         |
| desugar-document doc module-name | desugar-document doc chapter-name |
| check-module        | check-chapter        |
| lower-module        | lower-chapter        |
| emit-full-module    | emit-full-chapter    |

## Current Multi-File Chapters

| Chapter        | Files (current)                                      | Pages |
|----------------|------------------------------------------------------|-------|
| Parser         | Parser, ParserExpressions, ParserCore                | 3     |
| C# Emitter     | CSharpEmitter, CSharpEmitterExpressions              | 2     |
| X86-64         | X86_64, X86_64Encoder, X86_64Helpers                 | 3     |
| Type Checker   | TypeChecker, TypeCheckerInference                    | 2     |
| Lowering       | Lowering, LoweringTypes                              | 2     |

## Status

- [ ] Document decisions (this file)
- [ ] Implement page marker parsing in lexer/parser
- [ ] Implement compiler validation for page completeness
- [ ] Add page markers to all 33 .codex files
- [ ] Rename module to chapter in C# compiler internals
- [ ] Rename module to chapter in self-hosted .codex code
- [ ] Remove module marker streaming machinery
- [ ] Preserve prose in emitter output
- [ ] Update ChapterScoper to treat same-chapter pages as shared namespace
