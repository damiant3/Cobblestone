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

Unless separately dated, states below were measured 2026-08-24 against head, not read off the
design. What compiles, boots and answers was established by running it.

| # | Capability | State of the gap |
|---|---|---|
| PRISM-12 | Game composition and Apple Silicon targets | Measured 2026-09-22: Windows Valheim emits one DLL from typed Codex models and C# interop. Linked construction pools and upgrades, including warning colors, pass focused checks. Natural Meadows neck populations retain initial spawns; clearance starts a persistent random 7-14 Valheim-day cooldown, tested across reload. The distant wispy arc shows 09:00-15:00 on days 1, 8, 15 and on, in clear weather only (Damian's rulings, 2026-09-23; artistic, not physical), installed as build 20260924.013121. GT4 still needs full manual mouse/controller acceptance. Solo/recipe limits, deployment identity and receipts are owned by `design/Active/GameTargets.md`. The Mac target is PRISM-13 and the mix-and-match mod page PRISM-14. |
| PRISM-13 | **Mac as a target pill** | Damian 2026-09-23: Mac is a Binary-tab pill beside Windows .exe and Linux ELF, separate from game work. Prism produces only the ARM64 Darwin WIRE today (`codex/plugs/arm64/run.ps1 -Darwin`, `design/Active/GameTargets.md` GT5): code whose runtime makes raw BSD syscalls (`svc #0x80`, number in x16; `a64-emit-darwin-head` maps its own slab at `#200000000`), so it needs no library at run time. The external Mach-O wrapper is PR 144's contributor's with unestablished source and license; ours is `codex/plugs/macho` (`MachOWriter`, `MachOStdio`), the bytes module `macho-bytes.wasm` in `page-lenses.ps1` with `ship = $false`: payload mode byte 0 plus a DARWIN wire in, an ad-hoc linker-signed PIE executable out (`__PAGEZERO`, `__TEXT` with `__text` and `__const`, `__LINKEDIT`, `LC_MAIN`, `LC_LOAD_DYLINKER`, libSystem, `LC_BUILD_VERSION` macOS 11.0, `LC_SYMTAB`/`LC_DYSYMTAB`, `LC_CODE_SIGNATURE` with SHA-256 over 16 KiB pages, the layout `zig cc -target aarch64-macos` signs). The grade: `node apps/prism/test-macho.mjs --writer` compiles a subject through the seed and `arm64-stdio.wasm`, builds it, and runs 8 arms (a subject whose code length is 4 mod 8, structure and every page hash, every ADR into `__const` landing on a string entry, determinism, a flipped-byte control, three refusals); the ADR arm was calibrated by removing the align-8 gap, which it caught. The zig self-test (`node apps/prism/test-macho.mjs`) still passes. The Binary tab's "Mac app" pill chains the compiler's IR, `arm64-stdio.wasm` DARWIN and this module; `page-workspace-arm.js` arm 11b drives it through the page's Compile handler and grades the artifact with `gradeMacho`, and the pill and its run panel state packaging as verified and running as not. **Missing:** 13c a run on a Mac, the only runtime proof; on success the pill's title and run panel change from "NOT verified" to the measured result. Unverified until 13c: no `LC_DYLD_INFO_ONLY`, chained fixups or `LC_UUID` is emitted (the code has no imports and makes raw syscalls), and the Darwin head's `MAP_FIXED` slab at #200000000 has never met a real address space. |
| PRISM-14 | **A game-mods page** | Damian 2026-09-23: game mods get their own page. LANDED: `mods.html` (built by `codex/plugs/wasm/build-page.ps1` from `apps/prism/mods/<game>/features.json`) lists each game and its features; Open in Prism composes the chosen features plus a generated entry (`prism.html#mod=valheim:arc,storage`), and the Unity lens emits exactly those features (`apps/prism/test-mods.mjs`, 7 arms). Missing: per-feature compatibility receipts (contracts in `design/Active/GameTargets.md`), and a composed subset packaged, installed and played through Targets Build, which today is proven only for the full mod. |
| PRISM-7 | **The dev-environment campaign (red, opened 2026-08-28, Damian's direction)** | Prism becomes a full local HTML/WASM dev environment: source tree on disk (File System Access API + OPFS), multi-file editor, worker compile, cite resolution in-tab, kernel builds through the existing in-memory pe/img chain, webpage builds through the html lens, user-mode `.exe`/ELF as a NEW hosted-runtime backend, and an optional Claude REPL/agent panel behind a provider interface. Design and stage register: `design/Active/PrismDevEnvironment.md`. Stage 0 (reproducible lens-module builds) closes this register's own recorded gap that nothing in the tree builds the 48 shipped wasm modules. |

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

1. **A long-lived service rather than a process per request** (met 2026-09-23).
   The plug half is resident: `apps/prism/server.ps1`
   keeps the python, javascript, rust, haskell and go plugs booted, each
   parked in `net-io-recv-idle` between payloads, at 51 to 129 ms per warm
   leg (2026-09-23); a guest idle 50 s exits and the next press boots it
   again. The compile runs in the server as `codex-compiler.wasm` under
   wasmtime (no VM, about 245 ms), and c# stays run-once
   because its answer arrives on the guest console.
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
