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
| PRISM-5 | **Plug fan-out is a process per request** | **HALF DONE 2026-08-27 (reek): the six redundant COMPILES are gone; the six boots remain.** The gap was worse than this entry said. `Invoke-PlugOnSource` handed each plug's `run.ps1` a `-Src`, and every one of those runs `build/compile.ps1 -IrCce -Passes text-plug` again, so a six-plug press paid SIX compiles of one source as well as six boots, across six `/api/plug` requests. Five runners now take `-Ir` beside `-Src`, the shape `codex/plugs/csharp/run.ps1` already carried, and a new `/api/plugs` compiles the text-plug IR ONCE into the request's own directory and hands every plug the same bytes. Measured: the `-Ir` path against its `-Src` twin is byte-identical output on all six plugs and saves about 0.9 s each (python 1244 against 2241 ms, javascript 924/1791, rust 1227/2159, haskell 1230/2092, go 1271/2187, csharp 951/1829, medians of three). Through the server, paired runs on a loaded box: the batch was faster in 3 of 3 pairs, by 2.3, 8.8 and 1.3 s, against six `/api/plug` calls; the box is noisy enough that the paired comparison is the honest one and a median is not. **What remains is the sidecar**: six cold plug boots per press, which the design's Phase 3 asks to replace with plugs that stay alive on a port and accept repeated IR payloads. Nothing in the tree does that yet. **This is not a cache** and the 2026-08-24 ruling stands: the shared IR lives in the request's own directory and dies with it. **One UI consequence, stated rather than hidden:** the tabs used to fill in one at a time as each `/api/plug` returned, and now all six land together when the batch returns. **THE SIDECAR SHIPS 2026-08-27 (reek), ON A TAGGED WIRE, FOR PYTHON.** The
tag byte that already rides ahead of the IR decides it: `plug-run.ps1` sends
1 and keeps the run-once path it has today, and the pool sends **3**, which
means another payload may follow. `PythonPlug.codex` reads `recv.tag` and only
then reconnects, each iteration on its own source port because a client cannot
reuse a port it has just closed, bounded by a `serve-max` fuel of 64 so a host
that stops answering cannot make it spin. `apps/prism/server.ps1` keeps the
listener bound for its own lifetime, boots the plug VM once, and recycles at
56 payloads on its own terms rather than discovering a dead VM mid-request.
The generated `build/plug-run.ps1` is NOT touched: the pool speaks the wire
itself.

Measured: five payloads on one boot at **192, 67, 83, 62 and 75 ms, every one
byte-identical** to the run-once output, against about 1050 ms with a boot
each. The one-shot path is unchanged at **1049 ms median of three,
byte-identical**, which is the regression this design exists to avoid. The
control is the tag: sending 1 through the same pooled harness serves ONE
payload and the guest never reconnects, so the loop is genuinely the sender's
decision and not a loop that always runs.

**A sidecar outlives a hard-killed server, and the pool now reaps its own.**
Stopping the listener cleanly makes the guest's next connect fail and it exits
in 3 s; killing the owning process leaves the connect unanswered instead of
refused and the VM was still alive after 60 s, holding port 9131 so the next
`run.ps1` for python could not bind it. `Stop-Process` cannot be intercepted
from PowerShell, so the pool writes every VM pid it starts and reaps that list
at the next startup, killing a pid only when the live process's `Path` is the
VM binary this config would launch. Proven by orphaning one on purpose and
watching the next start reap it.

**THE OTHER PLUGS HAVE THE TAG TEST NOW AND EVERY SIDECAR IS SWITCHED OFF,
because a resident plug burns a whole core while it waits.** javascript, rust,
haskell and go carry the same `serve-once`/`serve-loop` as python; c# does not
and that is not an oversight, since its plug streams the emitted source through
`print-uni` def by def and the answer arrives on the guest CONSOLE, where the
pool's socket read finds nothing.

Each of the five was checked on both arms against the same program: the
one-shot path byte-identical and unchanged in cost (python 1040 ms, javascript
790, rust 1169, haskell 1045, go 1021), and four payloads on one boot
byte-identical at 63 to 110 ms after the first. The per-payload saving is real
and it is an order of magnitude.

**It does not survive at press level, and the three-point comparison is the
finding.** Same session, same build, same box, steady-state presses:

| sidecars | press 2 | press 3 |
|---|---|---|
| 0 | 5890 ms | 5959 ms |
| 1 (python) | 5914 ms | 5619 ms |
| 5 | 8499 ms | 9213 ms |

Zero and one are indistinguishable; five is a clear regression, and it drags
legs that are not sidecars at all -- c#'s run-once leg went from about 700 ms
to about 1050 ms in the same run. **The cause is measured, not inferred: an
idle sidecar consumed 9.94 CPU-seconds in 10 seconds of idling**, which is a
full core spinning in `net-io-recv-loop` waiting for the next payload. Five of
those saturate five cores, and the compile and the other plugs are competing
for the same box.

So the mechanism ships dormant. `sidecar` is `$false` on every row, the pool
and the tag support stay because they are proven and because flipping a flag
is what remains, and **the blocker is that the guest's wait is a spin rather
than a block.** That is `net-io-recv-loop` in `codex/os/net/`, not this row.
Until it yields, a resident plug costs about what it saves.

`/api/plugs` now logs each leg's elapsed ms and the compile's, because a
press-level total cannot say which leg is the cost and this box is shared with
other agents' VMs.

The earlier account of this, kept because the numbers are the argument: A `serve-loop` in `PythonPlug.codex`, serving a bounded 8 payloads and deriving each iteration's source port from the counter because a client cannot reuse a port it has just closed, served four payloads on ONE codex-vm boot at **250, 78, 89 and 90 ms, every one byte-identical to the one-shot output**, against about 1244 ms per payload with a boot each. So the saving is real and it is about 14x after the first. **What makes it unshippable is the one-shot callers, and that is the measurement to keep:** with the same looping plug, `run.ps1` went from 1129 ms to **3230 ms** and stayed correct, because after the first payload the guest keeps trying to reach a listener `plug-run.ps1` has already stopped and burns its `net-session-new` retry budget before giving up, while the host waits for the VM to exit. Every existing caller pays that, the oracle and `plug-smoke` included. **So the guest cannot decide to keep serving; the SENDER has to say so.** The wire already carries a tag byte ahead of the IR and `plug-run.ps1` sends 1, so a second tag meaning "another payload follows" leaves every existing caller on the path it has today and lets a pool opt in. That is the shape the next attempt should take, and it is a change to the generated `build/plug-run.ps1` through `codex/build/plugrunScript.codex` rather than a hand edit. **The design's own account of this is stale in a way that matters**: `PrismDesign.md` calls it "a minor change from the current run-once model" on the strength of plugs that "already" bind a port and accept connections, and no plug does. Every plug's `opening` DIALS OUT (`net-io-connect` to `host-ip` on its own port); PRISM-2 records the same inversion from the other side. The probe's own first reading was wrong for a related reason and it is worth naming: the reply carries NO length header, `plug-run.ps1` reads until the guest closes, and a probe that assumed a 5-byte reply header ate the first five bytes and reported 1249 against 1254. |

| PRISM-6 | **The shipped standalone page grows lenses, downloads, and a readable preset menu** (Damian, 2026-08-27, routed by red; the surface is `codex/plugs/wasm/page/prism.html` through `build-page.ps1`, and the live copy is cobblestoneproject.com/compile/prism.html) | Four asks, one press. (a) **Binary outputs become save/download buttons**: pe and img now run as wasm modules on the bytes transport (main 20059, proven byte-identical to their network twins), so a PE built in the tab is downloadable bytes and the browser's own save dialog is the delivery. (b) **A zig lens, plus whichever other text-target plugs are mature enough**: judge maturity by the plugs register rather than the directory listing, and keep the standing rule that a lens without its module stays dark rather than lying. (c) **The UI-framework outputters (react, vue, swiftui, winforms, and kin) as their own tab**, with code presets chosen to show the box model off; that likely means a preset or two written FOR layout rather than arithmetic. **The html outputter is explicitly on the list (Damian, 2026-08-27)**: the widget-tree-to-HTML/JS plug is the one that generates this very site&#39;s pages, so a preset whose widget-box tree becomes a rendered page is the box-model demo in one lens. (d) **DEFECT, seen live 2026-08-27: the code-preset dropdown renders light gray text on a white background.** Make it black on white; the select inherits the page's dim text color into a native menu the dark theme never reaches. |

**(b), (c) and (d) LANDED (reek, 2026-08-27). (a) IS BLOCKED ONE LEVEL DOWN AND
THE BLOCKER IS MEASURED.**

**(d)** was one rule. The open menu is drawn by the platform and paints white
however dark the page is, so inheriting `--text` put light gray on white;
`select option` now carries its own black-on-white pair and the closed control
keeps the page's.

**(b) and (c): eight new lenses, five of them a new UI tab.** Text gains
Python, TypeScript and Zig -- the three plugs the plugs register grades as
mature that were not already here, the six oracle-wired ones at 49 of 49 minus
javascript and csharp. UI is HTML, React, Vue, SwiftUI and WinForms, and the
HTML lens has a **Render the page** button that puts the emitted document in a
`sandbox`-attribute iframe: it lays out and runs nothing, because the artifact
is under inspection rather than trusted. Each lens is a four-line `XxStdio`
chapter over the emitter the network plug already uses; nothing existing moved.

**How far the lenses are proven, stated exactly.** Given the SAME IR, the
javascript, python and typescript modules are byte-identical to their network
plugs. The other six differ only where the page's IR carries fewer type-defs
than `compile.ps1 -Passes text-plug` does: for zig the first **816 lines agree
exactly** and divergence begins at the tuple type block, which is an input
difference and not an emitter one. It predates this change -- the shipped
javascript lens has always consumed that same IR. Whether the page should
compile with the text-plug pipeline is a real question and it is PRISM-6's
neighbour, not part of it.

**The preset written for layout is `widget-box`**, first in the menu under a
new `Interface` group: an outer column of three cards, a nested row, a
separator, spending padding, gap and direction visibly. It carries a 58 KB
prelude (Maybe, BoxModel, Layout, Theme, Widget, WebRuntime, WebTheme) because
the page compiles standalone, and it was verified by compiling it straight out
of `examples.json` the way the page does: zero diagnostics, and the emitted
HTML carries the tree's own labels with ten `gap` and seven `padding` rules.

The standalone page is **3,366,532 bytes**, up from 2,482,064: eight modules at
~880 KB is what the lenses cost a file that has to work from a `file:` origin.

**(a) IS FULLY CLOSED: THE BUTTONS EXIST AND THE FILE IS RIGHT** (reek, 2026-08-27, after fester's compiler half below). The Binary pills were still disabled with a tooltip saying the plugs needed a bytes entry point, which had not been true since plugs 1.92. PE and Disk image are live now; ELF stays dark and its tooltip says why, which is not the plug: nothing in the tree emits the x86 payload it reads.

The tab compiles a SECOND time in CDX mode, finds the payload the way `build-page.ps1` does (`SIZE:`, the count, then exactly that many bytes), and hands it to the pe module, then to the img module after it. **Both artifacts are byte-identical to the ones the bare-metal network plugs build from the same CDX**: PE `EC070C26..`, 85,504 bytes, opening `MZ`; the 8 MB GPT image `9C7BD7E0..`. That is the whole chain the button drives, run headlessly and graded, rather than a claim about a page nobody has pressed. **I did not boot the image and do not claim to have** -- the byte-identity is against artifacts this project already flies, which is the standard the rest of 1.92 was held to.

A plug refuses in WORDS on the same stream it would answer bytes on, so the page checks for `REFUSED` before enabling the save. A download button that hands somebody a text refusal named `BOOTX64.EFI` is the failure this guards.

**(a) IS CLOSED (fester, 2026-08-27), COMPILER-SIDE AS THIS ENTRY PREDICTED.**
The module emits CDX, and the bytes are RIGHT rather than merely present: the
same small program compiled through the module and through the x86-64 kernel
gives a **byte-identical 87,923-byte payload**. Where it used to answer two
newlines and `wasm trap: unreachable`, CDX mode now answers 88,132 bytes.

`pmap-self-test` walks the RUNNING compiler's own heap using the self-type
table the x86-64 backend bakes in, so it measures a property of the host
process and not of the artifact being emitted. A host built by a backend that
emits no pointer map has no table to bake and nothing to walk, so the
self-test stands down there instead of trapping: the wasm plug answers
`__self-type-defs` with an empty list (`$list_with_capacity 0`, the existing
runtime helper), `pmap-self-test` returns -2 for an empty table, and
`pmap-selftest-result` reports that as SKIPPED with its own message. **-2
rather than the expected 3 deliberately**, because reporting a skip as a pass
is how a check that has stopped asking becomes indistinguishable from one
that asks and agrees (L-CAPABILITY-LOST). Graded both ways: the SKIPPED line
appears on wasm and not on x86-64, and x86-64 still runs the walk and still
passes it.

The emitted artifact is untouched, which is the reason this is safe: nothing
in the CDX payload depends on the self-test, and the byte-identity above is
the proof rather than the argument.

**It has a runner.** `build-page.ps1` now compiles one small program in CDX
mode through the module it just assembled, compiles it through the x86-64
kernel, and refuses the page build unless the payloads match byte for byte.
Graded both ways against the module shipped earlier the same day, which traps
and fails the arm. It costs a couple of seconds on a build measured in
minutes, and it sits where the capability is consumed: a download button that
hands the user a wrong binary is worse than one that refuses.

What follows is the account of the blocker as it stood, kept because the
measurement is the reason the fix is shaped this way.

**IT COULD NOT BE DONE FROM THE PLUG SIDE, AND IT WAS NOT FOR WANT OF THE PLUGS.**
`elf`, `pe` and `img` all run as wasm modules now and emit bytes proven
byte-identical to their network twins (plugs 1.92). What the tab cannot do is
make them a PAYLOAD: a PE wants a CDX, and **the compiler module traps when
asked for one.** Measured 2026-08-27 on the page's own module, mode `CDX`
against `TEXT` as the control: TEXT gives 1,127 bytes, CDX gives two newlines
and `wasm trap: unreachable`. The backtrace names the line --
`__start` -> `emit_cdx` -> `compile_frontend_cdx` -> `pmap_selftest_bag` ->
`pmap_self_test` -- and the instruction is
`(unreachable (; wasm plug: __self-type-defs has no wasm form ;))`.
`compile-frontend-cdx` runs `pmap-selftest-bag True` unconditionally
(`opening.codex:898`), and `__self-type-defs` exists only for the x86 pointer-map
machinery (`X86_64Compound.codex`, `X86_64.codex:2208`).

So closing (a) is a COMPILER-side change, seed-affecting and wanting the token:
either give `__self-type-defs` a wasm form, or let that self-test be off on a
target with no pointer map to walk. That is a scope call rather than an
implementation detail, which is why it is written down here instead of taken.



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
