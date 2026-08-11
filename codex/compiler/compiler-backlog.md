# Compiler -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says
what is still missing and nothing else, a closed entry is DELETED, and
a gap that is still real is never quietly dropped. There is no
platform-wide register; `docs/PM/CurrentPlan.md` carries the shape.

| # | Capability | State of the gap |
|---|---|---|
| COMPILER-1 | **Runtime scope enforcement for effect grants** | Re-homed from CAPABILITY-REFINEMENT step 6 when that design moved to Done 2026-08-05. Compile-time scope enforcement is shipped and pinned (`lint-effect-scope` / `scope-grants` in the type checker; `codex/test/errors/scope-violation`, `scope-network-port`); the runtime half -- the syscall boundary checking a grant table at run time -- was never built. Depends on the compile-time grant data reaching the process capability table. Not blocking anything current. |

| COMPILER-2 | **An unterminated parenthesis run is accepted as the last definition in a file** | Appending `zzz-broken : Integer` / `zzz-broken = ((((` to the end of `codex/test/handler-smoke.codex` compiles clean and produces a CDX. **The cause is not established and the obvious reading is already refuted:** the lines sit after the `Page 1` marker, but that region IS parsed -- a duplicate of a name the chapter already defines, in the same position, raises `CDX3001` at `handler-smoke.codex:59:3`. The same two lines injected BEFORE the marker do fail to compile, so the position is load-bearing. Two untested candidates: the parser accepts an unclosed group at true end-of-input, or an uncited definition is name-bound (enough for CDX3001) without its body ever being checked -- the second would be a coverage hole and the more serious of the two. `zzz-broken` is cited by nothing in either arm, so the experiment that separates them is to CALL it from `opening` and see which arm moves. Found 2026-08-06 by reek while calibrating a sabotage arm; deferred the same day by Damian as not worth the detour. |
