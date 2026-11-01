# Circuits -- open capabilities

App-domain backlog. `docs/PM/CurrentPlan.md` carries the shape and the
priority order for the platform. Anything that is this application's own
behaviour lives here. An entry says what is still missing and nothing
else; a closed entry is DELETED rather than annotated.

| # | Capability | State of the gap |
|---|---|---|
| CIRCUITS-1 | Reference designators annotate in order | `SchematicEditor/SchematicAnnotate.codex` does not compile: lines 96 and 97 order Text with `<`/`>`, which is CDX2089 (measured 2026-10-26, seed 9E2000181819DC49). No entry chapter cites the chapter, so no build reaches it. The comparison needs `text-collate` or `text-compare` (Foreword chapter Collate). |
