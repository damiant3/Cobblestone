# Globe -- open capabilities

App-domain backlog. `docs/PM/CurrentPlan.md` carries the shape and the
priority order for the platform. Anything that is this application's own
behaviour lives here. An entry says what is still missing and nothing else;
a closed entry is DELETED rather than annotated.

| # | Capability | State of the gap |
|---|---|---|
| GLOBE-1 | **`BlackHoleSimd.codex` compiles** | The chapter calls `vec-insert`, `__real-approx-from-int` and `__real-approx-to-int`, none of which exists in any revision of the compiler (checked 2026-09-25 with `p4 grep -a` over `codex/compiler`), and spells its lane type `Vector 4 Real approximate` without the parentheses the type syntax needs. It has no `opening` and nothing cites it, so no sweep reaches it; it has never compiled. The fix is either a lane-insert builtin (a design question for the vector family, `docs/Designs/Done/Compiler/VectorWidening.md`) or rewriting the chapter over `vec4-splat` and pokes as `codex/test/vector-f32-mask` builds its vectors, then an entry or a citer so a gate compiles it. |
