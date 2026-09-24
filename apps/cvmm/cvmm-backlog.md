# CVMM -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/cvmm/design/Active/`.

| # | Capability | State of the gap |
|---|---|---|
| CVMM-1 | **Phase 2: the managers serve real state** | Every manager currently serves *mock* state. |
| CVMM-2 | **`CvmmPersist` does not compile, and nothing cites it** | Measured 2026-09-24 with `build/compile.ps1 -Kernel seed\Codex.cdx` on seed DB67D635: five errors in its own body, CDX2006 (a record literal missing `gl-id`, `hp-material-count`, `mat-id`) at lines 612, 705 and 722, and CDX2001 (Integer vs Text) at 680 and 724. No chapter cites it, so no entry chapter compiles it and no gate has seen it (L-NOGATE); it is the only chapter that cites `Budget`, `Garden` and `TodoList`, which are therefore compiled by nothing either. |
