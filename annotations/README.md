# Codex Annotation System

Sidecar annotation layer for the Codex codebase. Annotations attach
durable, queryable metadata to code without touching source files.

See `docs/Designs/Active/Compiler/Annotations.md` for the full design.

## Directory Layout

```
annotations/
  codex/
    compiler/
      Emit/X86_64Chapter.json
      IR/Lowering.json
      IR/LambdaLifting.json
      Semantics/ChapterScoper.json
    plugs/
      pe/PeWriter.json
      riscv/RiscVLir.json
```

One `.json` file per source chapter, **mirroring the source path exactly**.
`codex/plugs/riscv/RiscVLir.codex` is annotated by
`annotations/codex/plugs/riscv/RiscVLir.json`: strip `.codex`, add `.json`,
put it under `annotations/`. The path is derivable by substitution, which is
the whole point, and it is checkable by eye against the source tree.

**No file obeyed that rule until 2026-07-27.** All five drifted, in two ways.
The four oldest dropped the `compiler` segment, so
`codex/compiler/Emit/X86_64Chapter.codex` was annotated by
`annotations/codex/Emit/X86_64Chapter.json`. `PeWriter` drifted further: its
chapter moved to `codex/plugs/pe/PeWriter.codex` and the sidecar stayed behind
under `Emit/`, pointing at a directory its chapter had left.

The second kind is the one worth noticing. A sidecar is anchored to a chapter
by PATH, so any chapter that moves silently orphans its annotations, and
nothing says so -- there is no build step, and a query for the moved chapter
simply answers nothing rather than answering wrongly. **When you move a
chapter, move its sidecar.** All five are now correct.

**This is where maintainer commentary goes.** The inline `@kind target body`
form was removed from the Codex language on 2026-07-27; `annotations/` is the
only annotation mechanism.

**This section described a directory that does not exist until
2026-07-27.** It showed the root as `codex.annotations/` and stated that
the tree "was renamed from root-level `annotations/` to
`codex.annotations/` (CL 1201) to follow the `codex.<quire>/` naming
convention used by every other top-level Codex tree." No such rename
happened, and there is no `codex.`-prefixed directory anywhere at root:
every sibling is `codex/`, `apps/`, `build/`, `docs/`, `shaders/`. The
convention itself was abandoned.

That was not free. `sidecar-path-for` in `apps/works/AnnotationsSidecar.codex`
was written against this paragraph and built
`codex.annotations/<quire>/<chapter>.json` for every lookup, so the one
function that knows how to find a sidecar named a path that has never
existed. It went unnoticed because its only caller was a test supplying
its own fixture. `codex/test/apps/annotation-query-test` asserts the built
path now.

## File Format

Each annotation file is a JSON array of annotation records:

```json
[
  {
    "target": "function:emit-start",
    "kind": "invariant",
    "author": "cam",
    "date": "2026-05-06",
    "body": "Stack must be 16-byte aligned before the call instruction.",
    "thread": null
  },
  {
    "target": "function:emit-start",
    "kind": "discussion",
    "author": "damian",
    "date": "2026-05-06",
    "body": "Why not use the UEFI-provided stack?",
    "thread": "t001"
  },
  {
    "target": "function:emit-start",
    "kind": "discussion",
    "author": "cam",
    "date": "2026-05-06",
    "body": "UEFI stack is small and we ExitBootServices before __start.",
    "thread": "t001"
  }
]
```

## Target Syntax

Annotations reference code by stable name, not line number:

- `function:name` -- a function definition
- `type:name` -- a type definition
- `section:name` -- a named section
- `chapter:name` -- an entire chapter
- `field:type.field` -- a record field
- `variant:type.ctor` -- a sum type constructor

## Annotation Kinds

| Kind | Use |
|------|-----|
| `invariant` | A constraint that must hold. Agents check these. |
| `rationale` | Why something is the way it is. Replaces "why" comments. |
| `warning` | A known hazard or gotcha. |
| `discussion` | A threaded conversation. Uses `thread` field. |
| `discovery` | An agent-discovered fact worth preserving. |
| `todo` | Work to be done (with optional deadline). |
| `doctrine` | A project-level rule anchored to specific code. |

## Reading them

`apps/works/AnnotationsQuery.codex` is the reader, and it is deliberately
an **optional side query**: not a build step, not a gate leg, and nothing
invokes it automatically. Not every annotation is worth reading, and none
is worth reading every time, so a rationale attached to a function nobody
is touching should not appear on a build that would otherwise say nothing.

It filters a chapter's annotations by kind or by target and renders one
line each:

```
annq-parse       chapter, json-text  -> List Annotation
annq-by-kind     xs, "invariant"     -> List Annotation
annq-by-target   xs, "function:foo"  -> List Annotation
annq-render      xs                  -> Text
annq-count-of-kind xs, "warning"     -> Integer
```

`AnnotationsSidecar` does the loading and folding into a store;
`AnnotationsQuery` works on the flat list `parse-sidecar` returns, because
the store is a HamtMap keyed by hash and by target, which answers "what is
on this target" well and cannot be walked to answer "what invariants exist
in this chapter" at all.

## Writing them

Annotation files are JSON and can be written with any editor. Adding an
annotation is appending a record to the array in the appropriate sidecar
file; the structure is in the `## File Format` section above.

There is no CLI. The original `annotate.ps1` was deleted in the CL 1141
PowerShell cleanup pass and the proposed Codex replacement was never
written. This section named it as `codex.build/annotate.ps1` and
`codex.works/AnnotationsCli.codex`; neither quire exists.

**Every file must be a JSON ARRAY**, even when it holds one record.
**Four of the five sidecars were bare objects until 2026-07-27** and would
every one have loaded as nothing: `sidecar-array-to-annotations` answers
`[]` for any JSON value that is not an array, silently. Only
`PeWriter.json`, the one with two records, was written as an array. So the
format this README specifies was the exception rather than the rule, and
nothing would have said so, because nothing read these files.
