# Prism -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/prism/design/Active/`.

Every state below was measured 2026-08-24 against head, not read off the
design. What compiles, boots and answers was established by running it.

| # | Capability | State of the gap |
|---|---|---|
| PRISM-1 | **The compiler is not the server** | The design opens with "The compiler is the server" and says the web server is the same compiler booted in a different entry mode. What actually serves is `apps/prism/server.ps1`, a host PowerShell `HttpListener` that shells out to `build/compile.ps1` once per request and caches the result under `apps/prism/build-output/cache`. Measured: it comes up and answers, `/` 200 with 9,544 bytes and `/api/files` 200 with 1,224 bytes over a catalog of 26 hard-coded source paths. So the app is alive and the gap is architectural rather than a breakage. Phases 1 and 2 are host-side as well, not only the plug fan-out. `Prism.codex` itself compiles clean at head (181,308 bytes, 2.5 s against `build/output/Sut.cdx`), so the Codex half is a starting point and not a rewrite. **The route that looks obvious is closed, measured 2026-08-24, and this is the part to read before planning it.** Making the SERVING unit contain the compiler cannot be done by having `Prism.codex` cite the frontend. `compile-frontend-ir` and `ir-emit-roots` live in `Chapter: Opening`; there is no quire registered for `codex\compiler` in `build/quire-map.ps1`, and the compiler's own unit is assembled by GLOBBING (`concat-codex-self.ps1`), so its internal `cites Codex chapter ...` lines are satisfied by PRESENCE in that glob rather than by the registry. Cited into an app unit, every one of those becomes unresolvable, and a probe citing `Compiler chapter Opening` fails at the cite with CDX3010. Registering a quire for the compiler means editing `quire-map.ps1`, which is generated from `codex/build/quiremapScript.codex` and is reek's claim. So closing this entry is a COMPILER-side mode (the design's own `-mode prism`), which is seed-affecting, wants the build token, and would put a network stack in the seed's reachable set. That last consequence is the one to weigh first, and it is a question for Damian rather than an implementation detail. |
| PRISM-2 | **`run.ps1` does not stand up a serving Prism** | The two halves disagree about who listens. `run.ps1` boots the CDX with `-hostfwd tcp::8080-:9200`, which forwards a host port into a guest that is expected to LISTEN on 9200. The guest does the opposite: booted that way it sends one outbound SYN, `10.0.2.15:49152 -> 127.0.0.1:9200`, the NAT answers `connect failed for guest port 49152`, and the VM exits 0 having served nothing. The design says the inversion is deliberate ("the plugs listen, the compiler connects"), so what is missing is the peer that should be listening on 9200 plus a `run.ps1` invocation matching whichever direction is chosen. Until then `run.ps1` is not the way in; `server.ps1` is. |
| PRISM-5 | **Plug fan-out is a process per request** | `Invoke-PlugOnSource` shells out to each plug's own `run.ps1`, which boots a plug CDX, runs it once and exits. Fan-out is now LIVE on submitted source rather than reading a baked artifact, so the remaining gap is cost and not correctness: six plugs is six cold boots per press. The design's Phase 3 asks for sidecars that stay alive on a dedicated port and accept repeated IR payloads, which it calls "a minor change from the current run-once model". Nothing in the tree does that yet. This is on the critical path per the ruling below. |

## The essay-repl-server join (Damian, 2026-08-24, via red)

The item is to revive this app and integrate Steve Howell's
`essay-repl-server`, whose README names an online Codex REPL as its
milestone 2. A checkout sits outside every client root at
`D:\Projects\essay-repl-server-main`; nothing about it belongs in this
depot beyond what our own side must build, which is what this section is.

**Where the two meet.** That server already carries a four-stage pipeline
whose first two stages are ours by construction: Codex source to IR, then
IR to a target language. It reaches them as two native executables copied
out of a `codex-zig-ladder` checkout, `codexir` and `zigemit`, refreshed
by a script that stamps provenance including the seed the ladder is
banked against. So the integration is not a port. It is supplying a
Codex-side service those stages can call instead, and Prism is the only
thing in the tree shaped like that service.

**What our side must provide, in the order the gaps have to close:**

1. **A long-lived service rather than a process per request** (PRISM-5,
   and PRISM-1 for the compile half). Both systems still fork per
   request. The design's Phase 3 sidecar loop is the shape that fixes it
   for the plug half; the compile half needs the same treatment. This is
   the largest remaining item and it is the difference between a demo and
   a service.
2. **An answer to isolation before any submitted source is executed.**
   The compile and transpile stages transform text, but running the
   result executes generated code. Prism today runs on a developer box
   with no such requirement, and being a backend for a public surface
   introduces one that is ours to state rather than inherit.

**RULED 2026-08-24 (Damian, via red).** *"we definitely need
compile/transpile on the fly for the prism. the canned IR is not the
correct design."* The full scope is taken: PRISM-1 through PRISM-3, an
on-the-fly compile and fan-out, and the pre-baked IR path goes. The
cheaper shape that was on the table, closing PRISM-3 alone and leaving
fork-per-request in place, is declined and is not to be re-proposed as a
staging step: the canned path is the thing being removed, so reaching the
goal through it is the wrong direction.

**The canned IR is gone and the compile is live, on the host server.**
`run.ps1`'s pre-bake of five demo files, the IR disk cache, and the
in-memory IR and plug caches are all deleted; there is no cache of any
kind on the compile path, request-scoped or otherwise. `/api/compile` and
`/api/plug` take `{"source": "..."}` and work in a per-request directory
that is removed afterwards, so no request can read another's artifact.
Both are bounded at 60 s, the budget the test harness gives a kernel,
because the listener is single-threaded and a runaway compile would hold
it open. Proven on text that exists nowhere in the repo: `triple 14` came
back as `add-int (add-int 14 14) 14`, the pipeline having inlined the
leaf call, and the Python plug transpiled the same submitted text. The
arms that had to fail did: a program naming an undefined value answers
`status:"error"` carrying the compiler's own CDX3002, and two different
sources return two different IRs.

**What that leaves.** PRISM-1 is NOT closed and the ruling's "in-process"
is only half met: the compile is on the fly but it still runs in a
PowerShell host process that forks the compiler per request, not inside
the serving unit. The blocker is measured and recorded in PRISM-1 above.
PRISM-5 is now the critical path, not a later optimisation, because
on-the-fly fan-out is otherwise N cold plug boots per press.
