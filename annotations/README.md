# Codex Annotation System

Sidecar annotation layer for the Codex codebase. Annotations attach
durable, queryable metadata to code without touching source files.

See `docs/Active/Compiler/Annotations.md` for the full design.

## Directory Layout

```
codex.annotations/
  codex/                  Compiler source annotations
    Emit/
      X86_64Chapter.json
      PeWriter.json
      ...
    IR/
      Lowering.json
      ...
  codex.foreword/         Foreword annotations
  codex.kernel/           Kernel annotations
  codex.os/               OS annotations
```

This quire was renamed from root-level `annotations/` to
`codex.annotations/` (CL 1201) to follow the `codex.<quire>/` naming
convention used by every other top-level Codex tree.

One `.json` file per source chapter, mirroring the source tree.

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

## Management

The original `codex.build/annotate.ps1` CLI was deleted in the CL 1141
PowerShell cleanup pass. A Codex-native replacement is the right
shape — see the addendum at the bottom of `docs/Active/Compiler/
Annotations.md` for the proposed `codex.works/AnnotationsCli.codex`
chapter and its operation surface.

In the meantime, annotation files are JSON and can be read or written
directly with any editor. Adding a new annotation is appending a
record to the array in the appropriate sidecar file. The structure is
defined in the `## File Format` section above and in the design doc.
