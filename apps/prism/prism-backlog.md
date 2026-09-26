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
| PRISM-13 | **Mac as a target pill** | Damian 2026-09-23: Mac is a Binary-tab pill beside Windows .exe and Linux ELF, separate from game work. Prism produces only the ARM64 Darwin WIRE today (`codex/plugs/arm64/run.ps1 -Darwin`, `design/Active/GameTargets.md` GT5): code whose runtime makes raw BSD syscalls (`svc #0x80`, number in x16; `a64-emit-darwin-head` maps its own slab at `#200000000`), so it needs no library at run time. The external Mach-O wrapper is PR 144's contributor's with unestablished source and license; ours is `codex/plugs/macho` (`MachOWriter`, `MachOStdio`), the bytes module `macho-bytes.wasm` in `page-lenses.ps1` with `ship = $false`: payload mode byte 0 plus a DARWIN wire in, an ad-hoc linker-signed PIE executable out (`__PAGEZERO`, `__TEXT` with `__text` and `__const`, `__LINKEDIT`, `LC_MAIN`, `LC_LOAD_DYLINKER`, libSystem, `LC_BUILD_VERSION` macOS 11.0, `LC_SYMTAB`/`LC_DYSYMTAB`, `LC_CODE_SIGNATURE` with SHA-256 over 16 KiB pages, the layout `zig cc -target aarch64-macos` signs). The grade: `node apps/prism/test-macho.mjs --writer` compiles a subject through the seed and `arm64-stdio.wasm`, builds it, and runs 8 arms (a subject whose code length is 4 mod 8, structure and every page hash, every ADR into `__const` landing on a string entry, determinism, a flipped-byte control, three refusals); the ADR arm was calibrated by removing the align-8 gap, which it caught. The zig self-test (`node apps/prism/test-macho.mjs`) still passes. The Binary tab's "Mac app" pill chains the compiler's IR, `arm64-stdio.wasm` DARWIN and this module; `page-workspace-arm.js` arm 11b drives it through the page's Compile handler and grades the artifact with `gradeMacho`, and the pill and its run panel state packaging as verified and running as not. **Missing:** 13c a run on a Mac, the only runtime proof; on success the pill's title and run panel change from "NOT verified" to the measured result. Unverified until 13c: no `LC_DYLD_INFO_ONLY`, chained fixups or `LC_UUID` is emitted (the code has no imports and makes raw syscalls), and the Darwin head's `MAP_FIXED` slab at #200000000 has never met a real address space. |
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

1. **A long-lived service rather than a process per request.** Open: no
   host server in the tree provides one. The plugs' serve-again tag (3) that
   keeps a plug guest parked in `net-io-recv-idle` between payloads is still
   in the plugs; nothing on the host drives it.
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
