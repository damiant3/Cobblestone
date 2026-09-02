# Prism Dev Environment -- the local HTML/WASM workbench

**Campaign owner: red. Opened 2026-08-28 at Damian's direction, verbatim
scope:** turn Prism into a full-blown local HTML/WASM dev environment --
view of the whole source tree, keep a local copy on disk, write the code,
build it, output `.exe` and Linux executables, build kernels that do
whatever the user wants (a webserver, a SaaS server, a router, an RDBMS
server), make webpages and deployables, transpile -- with Claude
integrated as an optional REPL chat and agent, behind a provider
interface so another agent provider can slot in later.

This document is the campaign's design and its register of stages. It
SUPERSEDES the direction of `PrismDesign.md` (the compiler-as-server
architecture): the vehicle for the dev environment is the STATIC PAGE,
not a served process. The server flavor of Prism survives only as the
hosted half of the essay-repl-server join (`prism-backlog.md`), which
this campaign does not touch. PRISM-1's finding stands and is one of the
reasons for this direction: making the serving unit contain the compiler
is closed off host-side and seed-expensive compiler-side, while the
compiler ALREADY runs inside a static page.

## Where this starts from (surveyed 2026-08-28, file:line evidence)

The substrate is shipped and live. `codex/plugs/wasm/page/prism.html`
(the source of truth; the deployed copy at
`apps/landing/web/compile/prism.html` is byte-identical modulo the embed
block) is a 6.3 MB self-contained static page that:

- runs the compiler in-tab as a bare `WebAssembly.Instance` with exactly
  two imports, `wasi_snapshot_preview1.fd_read`/`fd_write`
  (`prism.html:586-606`); input is one stdin stream
  `MODE-LINE \n prelude source \0`; output is buffered until `_start`
  returns (harmless: ~99 per cent of a build is `compile-frontend`,
  which prints nothing -- plugs-backlog 1.83a);
- compiles in modes `IR-UNI` and `CDX` (`opening.codex:2144-2149`;
  `TEXT` is used by the self-compile page only);
- fans the IR across 45 text/UI lens plugs and chains
  compiler -> pe -> img IN MEMORY (`prism.html:649,805-815`,
  `Uint8Array` throughout, no TCP, no files) to a downloadable
  `BOOTX64.EFI` and 8 MB GPT `codex.img`, both proven byte-identical
  to the bare-metal network plugs' artifacts (prism-backlog PRISM-6);
- carries an editor textarea with a hand-written Codex highlighter, a
  57-preset menu, a JS lens runner, and a sandboxed HTML render iframe;
- retries the deck ladder `[12, 48, 125]` upward on CDX9002 only
  (`prism.html:384`).

The full shipped module roster is 48: the 45 IR-transport lenses, the
pe and img BYTES-transport modules, and the evidence module (raw-CCE
stdin, its own builder `codex/plugs/evidence/build-wasm.ps1`); the page
additionally embeds `codex-compiler.wasm`. elf-bytes builds and is
deliberately not shipped while its lens is dark (`prism.html:309`).
Line citations in this document are against the SOURCE copy,
`codex/plugs/wasm/page/prism.html`; the deployed copy's numbering
diverges past the embed block.

The worker + main-thread-retry pattern exists in `page/index.html`
(`:361-415`); `prism.html` itself still compiles ON the main thread.

**Two facts that bound the design:**

1. **The page's compilation unit is FLAT.** No cite resolution exists
   in-tab; `cites Foreword chapter X` fails CDX3007 and every preset
   carries its foreword prelude verbatim (`build-page.ps1:55-62`,
   `examples.json`). A dev environment needs the library on board
   (stage 2).
2. **Today's "PE" and "ELF" are boot artifacts, not user-mode
   executables.** The pe plug writes subsystem 10, EFI application
   (`PeWriter.codex:7,57,105`); the elf plug writes a Xen-PVH 32-bit
   bare-metal kernel image, single RWX `PT_LOAD` at 1 MiB
   (`ElfWriter.codex:39`, `ElfPlug.codex:87-91`). "Output `.exe` and
   Linux executables" is therefore a NEW BACKEND CAPABILITY -- a hosted
   runtime -- not wiring (stage 5). The ELF lens is additionally dark
   because the compiler has no ELF payload mode
   (plugs-backlog 1.92 account; `prism.html:309`).

**The governing constraint, inherited from
`docs/Designs/Active/Marketing/Cobblestone.md:44-66`:** everything ships
static-file-servable. The self-contained page must keep working from a
`file:` origin. Nothing here may depend on a host process. Local-machine
capability (disk access, Claude API) enters through what a browser
grants a static page: the File System Access API, OPFS, `localStorage`,
and CORS-enabled `fetch`.

**A recorded reproducibility gap this campaign owns closing first:**
nothing in the tree builds the 48 lens modules. No
`*-stdio.wasm`/`*-bytes.wasm` exists under any `codex/plugs/*/
build-output/`; the shipped modules survive only as base64 inside the
deployed page, and the per-lens chapter lists live only inside
`codex/plugs/wasm/page-lens-test.ps1` (plugs-backlog: "no script calls
`build-plug-wasm.ps1` at all"). Rebuilding the page today ships a page
with almost every lens dark.

## The stages

Each stage lands independently, gate-green, with its acceptance arms
named before the work starts. Order matters only where stated.

### Stage 0 -- Reproducible substrate (red, first)

One manifest, `codex/plugs/wasm/page-lenses.ps1`, carries the WHOLE
roster -- the 45 IR lenses (promoted out of `page-lens-test.ps1`'s
inline table), the three bytes modules (pe, img, elf; their chapter
lists were recorded NOWHERE and are reconstructed here, proven by the
compiler accepting the bundle and by the grading arms), and the
evidence row pointing at its own builder. Every other statement of the
list dies: `page-lens-test.ps1` reads the manifest's `ir` rows,
`build-page.ps1`'s copy list AND its embed list both read the
manifest's shipped rows, and a new `build-page-modules.ps1` builds
every row through `codex/plugs/common/build-plug-wasm.ps1` (or the
row's own builder), leaving each module in its plug's `build-output/`
where `build-page.ps1` already looks.

Grading splits by transport: `page-lens-test.ps1` grades the `ir` rows
as today (including `-Calibrate`); a new `page-bytes-test.ps1` grades
pe (mode-0 payload arm plus refusal), img (FAT16 arm plus refusal) and
elf (refusal only -- its positive arm waits on stage 5a's payload
producer, which is also why elf carries `ship = $false` and stays out
of the page); evidence is graded where it is built.

Acceptance: from a clean `build-output`, one command rebuilds every
row; `page-lens-test.ps1` green over the rebuilt set including
`-Calibrate`; `page-bytes-test.ps1` green; `build-page.ps1` assembles a
page with zero ABSENT modules among the shipped rows and its existing
CDX byte-identity arm green. Acceptance is BEHAVIORAL, deliberately:
byte-identity of a rebuilt module against the shipped base64 is not
asked for, because the seed has moved since those were built and a
module is a function of the seed.

### Stage 1 -- Workspace shell (red)

The page becomes a multi-file environment:

- **Project tree panel** (left) + tabbed editor; the existing
  highlighter and presets carry over (a preset becomes "new file from
  preset").
- **Storage.** OPFS is the always-available project store. Where the
  File System Access API exists (Chromium), "Open folder" binds a real
  directory on disk -- that is Damian's "keep a local copy on disk" --
  with OPFS as mirror/fallback. Export/import of a whole project as a
  store-only zip (a store-only zip writer is ~100 lines of JS we write
  ourselves; no library).
- **Worker compile.** Adopt `index.html`'s worker + small-stack retry
  into prism so the UI never blocks; surface a `memory.grow` refusal as
  an out-of-memory message instead of a bare trap.
- **Diagnostics mapped home.** The in-page unit assembler must keep a
  region table (which prelude/file each unit line came from) and remap
  compiler diagnostics to file:line the way `test-compile-batch.ps1`'s
  `Convert-DiagLine` does host-side. A diagnostic pointing into an
  assembled unit is the failure this exists to prevent.

Acceptance arms: create/edit/save/reopen across a browser restart (OPFS
and FSA arms separately); a compile error planted in the SECOND file of
a project is reported against that file and line (sabotage arm: move the
error, the report moves); the page still fully works from `file:` with
FSA unavailable. One claim to verify before building, not assume: that
`showDirectoryPicker` is available from a `file:` origin in Chromium --
if it is not, the FSA arm binds only when served over http(s)/localhost
and the design note records it.

**BUILT (red, 2026-08-28).** The page carries the workspace: tree panel
+ file tabs; the compile unit is the concatenation of the project's
`.codex` files with a region table (cites BETWEEN project files resolve
by presence, the same way every plug bundle and the compiler's own glob
assemble); diagnostics are remapped from unit coordinates to file:line;
compiles run in a blob worker with the sync path kept as the small-stack
fallback; storage is OPFS always, an FSA-bound real folder when the
browser grants one (feature-detected at the button, never assumed --
the `file:`-origin question resolves itself at runtime), plus store-only
zip export and multi-file import; presets land as project files, a
preset's cited chapters as a visible sibling file rather than a hidden
prelude. Proven headlessly against the SHIPPED script under node with a
stub DOM: region arithmetic, the non-unit exclusion, the zip (re-read by
Expand-Archive as the independent referee), a REAL two-chapter compile
through the actual module, and a REAL planted CDX3002 in the second file
mapping home to `b.codex:6:11` -- which also establishes that raw
diagnostics arrive as `unit-line:col:` and the remap fires on real
output. All 57 presets still compile through the module at decks=12.
What no headless arm can prove and waits on a hand: the OPFS
reload-survival, the folder picker, and the browser-restart arms.

### Stage 2 -- The library on board (red; the resolver decision)

Goal: `cites` works in-tab against the shipped library (Foreword, UI,
WebApp, OS quires) and against the user's own project files, so real
programs -- a Page chapter, a server app -- compile without hand-carried
preludes.

Two candidate mechanisms; a one-day timeboxed probe decides, criteria
written before the probe:

- **(A) Host-side resolver in the page's JS**, mirroring
  `build/quire-map.ps1` + `bundle-app.ps1`. Ships fast, zero emitter
  changes; but it is a THIRD implementation of cite resolution and this
  project's own record says copies drift and lose.
- **(B) The compiler's own resolution.** The compiler already resolves
  cites from a FAT16 volume in DISK mode (`disk-resolve-forewords`,
  `fat16-boot-volume` -- Build.md records it live). In-tab that needs
  the wasm target to answer the block-device builtins
  (`block-read-sector` etc., today refusal class 2 in
  `WasmEmitter.codex:973,1339`) through new imports
  (`env.block_read_sector`...) backed by a JS virtual disk. The disk
  image of the library ships prebuilt (our own GptWriter/Fat16Writer
  build it host-side); the user's project files are appended into the
  image in-page through our own FAT writer compiled to wasm -- single
  source of truth for the format, no JS reimplementation.

Recommendation: (B) is the destination -- one resolver, our own format
code. (A) is acceptable only as a probe result if (B)'s emitter change
proves disproportionate; if (A) ships, its retirement condition is
written into this doc the day it lands. The wasm emitter is shared
machinery: coordinate the import change with reek's lane at the plugs
register before landing.

Acceptance: `apps/notes/NotesPage.codex` compiles in-tab from the tree
with zero prelude and renders through the html lens ("make webpages" is
demonstrated end to end); a cite of a nonexistent chapter still answers
CDX3010, not a hang (negative arm).

#### Criteria, written before the probe (fester, 2026-08-28)

**Declared interest first, because it is what these are written against.**
The wasm emitter is my lane, (B) is my work, and the recommendation above
already favours (B). That is two thumbs on the scale, so every threshold
below is a NUMBER fixed now rather than a judgement made after the
measurement, and each is stated so that (A) can win it.

**Two facts measured before the probe, because both change what (B) costs
and one of them corrects this section.** The emitter is 2,882 lines. It
already emits imports and already emits them CONDITIONALLY: two WASI
(`fd_write`, `fd_read`) plus `env.blit_framebuf` and `env.on_key` behind
`needs-blit` / `needs-key` (`WasmEmitter.codex:1592-1595`). The text above
says (B) "needs the wasm target to answer the block-device builtins through
new imports" as though the mechanism were absent; it is present, with a
precedent for exactly the gating (B) wants, so (B) adds names to a pattern
rather than establishing one. The refusals are real and self-identifying:
`block-sector-count` emits `(unreachable (; wasm plug: no block device on
this target ;))` at `:973`, and `block-read-sector` / `block-write-sector`
sit in `wat-no-such-thing` at `:1339`.

| # | Question | Measured how | Threshold that fails (B) |
|---|---|---|---|
| C1 | Emitter cost | changed lines in `WasmEmitter.codex` | more than 150 |
| C2 | Refusal integrity | do the `wat-no-such-thing` entries become real implementations, or merely disappear | ANY refusal deleted rather than replaced by a working import (L-ACCEPTED, L-BAILVALUE) |
| C3 | Existing consumers | are the new imports conditional, like `needs-blit` | an UNCONDITIONAL import, which every existing page must then satisfy |
| C4 | Blast radius | plugs other than wasm whose output moves | any non-wasm plug changes behaviour |
| C5 | Timebox | wall clock to a spike that compiles `NotesPage` in-tab | the one day in this stage |

**(A) fails if** it cannot answer the negative arm (a nonexistent cite must
give CDX3010, not a hang), or if it must RESTATE quire-map's chapter
ordering rather than read it, because a copy that has to be kept in step is
the third implementation this design is trying not to build.

**Tie-break, decided now.** Both passing means (B) ships, on single source
of truth. (B) failing any row means (A) ships, and its retirement condition
goes into this doc the day it lands, per the stage above. A result of "(B)
is close to a threshold" is a FAIL of that row, not a negotiation: the whole
point of fixing the numbers before the probe is that I do not get to move
them once I know which way they cut.

The import change is shared machinery. Coordinate with reek's lane at the
plugs register before landing it, whichever option wins.

#### Probe result, emitter rows (fester, 2026-08-28)

**(B)'s emitter change is 16 lines, and the fear it was disproportionate is
settled.** Measured against the thresholds above, not estimated:

| # | threshold | measured | |
|---|---|---|---|
| C1 | fail above 150 changed lines | **16** (11 added, 5 removed) in a 2,882-line emitter | pass |
| C2 | fail if a refusal is deleted rather than replaced | all three of `block-read-sector`, `block-write-sector`, `block-sector-count` now emit real calls; none deleted into silence | pass |
| C3 | fail on an unconditional import | 3 of 3 gated on `needs-block`, computed by the same `wasm-mention-any` that gates `needs-blit`; 0 unconditional | pass |
| C4 | fail if any non-wasm plug moves | change confined to `WasmEmitter.codex`, and `wasm-e2e` is 27 passed 0 failed against x86-64 | pass |

C3 is not a formality: the emitter's own prose at `:1584` records that
emitting `blit_framebuf` and `on_key` unconditionally made EVERY module fail
to instantiate with `unknown import: env::blit_framebuf`. The criterion was
written before that prose was found, and the prose is why it is a hard fail
rather than a preference.

**C5 is the open row and it is where (B)'s real cost lives.** The emitter half
took under an hour; what remains is the JS virtual disk and the prebuilt
library image, and only that can answer whether `NotesPage` compiles in-tab
inside the stage's day. Nothing here decides (A) versus (B) on its own: four
rows passing means the EMITTER is not the obstacle, which was the doubt the
stage recorded, and it moves the whole question onto the page side.

The 16 lines are landed rather than shelved because they are inert until
something calls a block builtin: a module that does not mention one emits
byte-identically, which is what C3 and the 27 green e2e arms establish.

#### C5 criteria, written before measuring (fester, 2026-08-28)

**Context that sets the thresholds, and it corrects this stage.** The deployed
page is **6,311,906 bytes** (`apps/landing/web/compile/prism.html`; the source
template at `codex/plugs/wasm/page/prism.html` is 83,063, the difference being
the embedded base64). The stage names "Foreword, UI, WebApp, OS quires" as
though those were four top-level things. Per `build/quire-map.ps1` they are
not: `Foreword` is `codex\foreword\core`, `UI` is `codex\foreword\ui`, `OS` is
`codex\os\core`, and **there is no WebApp quire at all**. "The shipped
library" therefore needs a definition before it can be costed, and the cost
is decomposable rather than one number:

| set | chapters | raw | as base64 | page growth |
|---|---|---|---|---|
| Foreword core + UI | 180 | 1,419,279 | 1,892,372 | +30% |
| nine plausible quires (adds OS, Kernel, Net, Math, Encode, Compress, Trust) | 372 | 3,393,410 | 4,524,547 | +72% |

**Thresholds, fixed now:**

| # | Question | Fails if |
|---|---|---|
| C5a | Page growth from the library image | the embedded image more than DOUBLES the page, i.e. exceeds 6,311,906 bytes as shipped |
| C5b | Build cost | building the image adds more than 60 s to `build-page.ps1` |
| C5c | Page-load impact | first in-tab compile latency more than doubles, measured headlessly through the node arm rather than by eye |
| C5d | Resolver reuse, which is the whole point of (B) | any JS restatement of cite ORDERING. (B) must reach `disk-resolve-forewords` through the block imports; (A) fails this row by construction unless it reads `quire-map.ps1` rather than copying it |

**One architectural note recorded before measuring, because it cuts against
the obvious fear:** the library image is DATA, not a module. It is not
compiled or instantiated at load the way the 48 lens modules are, so its
page-load cost is bytes and one fetch, not instantiation. C5c exists to test
that claim rather than assume it.

**What a FAIL of C5a means, decided now:** not that (B) loses, but that the
shipped set shrinks to the smallest quire set that compiles `NotesPage`, and
the row is re-measured against that. (B) loses on C5a only if even the minimal
set doubles the page.

#### C5 result (fester, 2026-08-28)

**C5a PASSES on size, and comfortably.** Both candidate sets are well inside
the doubling threshold: Foreword core + UI is +30 per cent of the page, the
nine plausible quires +72 per cent. Size is not what decides this stage.

**C5b and C5c are NOT MEASURED, and the reason is the finding below.** Timing
the build of an image that cannot serve its purpose would be measuring the
wrong artifact, and a number produced that way reads as progress while
answering nothing.

**THE FINDING, and it changes (B)'s cost: the prebuilt library image and the
compiler's on-disk resolver disagree about layout, so the two halves (B) is
assembled from do not currently meet.**

- `build-img.ps1 -SourceDir` (`:541-562`) recurses a tree for `*.codex` and
  writes every chapter FLAT INTO THE ESP ROOT under an 8-character uppercase
  stem with a `.COD` extension, collisions resolved by a trailing digit, plus
  an INDEX file mapping `STEM.COD` back to the real relative path.
- `disk-load-cite` (`opening.codex`) resolves a cite by building
  `quire-to-dir quire & name & ".codex"` and calling `fat16-read-source` on
  that path: a quire SUBDIRECTORY and the chapter's REAL name.

Nothing reconciles those today. The stage reads as though (B) were two
shipped capabilities being wired together; the wiring is the work. Three ways
out, and each carries a cost the stage did not price:

1. **The image gains real subdirectories and long names.** `build-img.ps1`
   does not write them and `make-fat16-subdir.ps1` exists as a SEPARATE tool,
   which is the tell that it is not built in.
2. **`disk-load-cite` learns the INDEX.** That is a compiler change and
   therefore seed-affecting, for the benefit of one consumer.
3. **The in-page virtual disk synthesises the layout the resolver expects**,
   which is where (B) was always going to do its own work, and is the option
   that keeps the resolver untouched. It needs our own FAT writer compiled to
   wasm, as the stage already proposes, so the block imports landed above are
   necessary but not sufficient.

**What this does and does not say.** It does NOT reopen the emitter rows: C1
through C4 stand, and 16 lines is still the whole emitter cost. It says
(B)'s remaining cost sits in the disk LAYOUT, not in the compiler and not in
page weight, and that is a different question from the one the stage asked.
(A) is unaffected by any of it, which is exactly the asymmetry the A-vs-B
call now turns on.

**Next measurement, for whoever takes it:** option 3 costed the way C1 was,
by building the thing and counting, with C5b and C5c re-measured against an
image that the resolver can actually read.

#### Stage 2 CONVERGENCE (red + fester, direct, 2026-08-28 afternoon)

Two probes ran this stage in parallel (red's rulings landed on the red
stream at 20612/20613 and were not yet at main when fester's rows above
landed; the fleet-day merge surfaced the collision). Converged by direct
agreement, each side verifying the other's measurements; the emitter
carrying it landed at main 20689:

1. **Device.Block grounds in LINEAR MEMORY for v1, not imports.** The
   module exports `disk_reserve(size)`; the host calls it BEFORE
   `_start`, copies the image in, and reads anything the guest wrote by
   slicing `memory.buffer` at the same base after `_start` (copy-in and
   read-back are one buffer). Why not the C3-gated imports, measured:
   the compiler unit itself mentions block builtins (34 refs in Fat16
   alone), so `needs-block` ALWAYS trips for `codex-compiler.wasm` and
   the conditional buys nothing on exactly the module that matters --
   every unprovisioned bed (build-page's wasmtime CDX arm, index.html,
   the node arms) would die at instantiation. C4 could not see this
   because its corpus held only non-mentioning programs (L-CONSTRUCT).
   The conditional-import machinery is RIGHT for stage 5's hosted
   syscalls, where the imports are genuinely optional, and returns
   there.
2. **Two traps the disk path hides behind the emitter rows, both
   measured by driving DISK mode for real:** `fat16-scope-admits`
   consults `process-get-scope process-get-pid` on every path
   resolution (grounded: pid 0, empty grant -- the module is one
   process and the page is the trust boundary), and DISK mode writes
   OUT.CDX back unconditionally (grounded: `block-write-sector` lands
   in the host-provided image bytes, with x86's status convention --
   0 IS SUCCESS; the first build inverted it and every byte landed
   while the verdict read FAILED, and the same find caught a live
   status-unread defect in the LFN record writes before they shipped).
3. **The layout finding's resolution rides LFN (option 1), now LANDED
   (main 20684).** `build-img.ps1 -SourceDir` gains real quire
   subdirectories and real names, and `disk-load-cite` reads the layout
   it already expects. No page-side FAT writer; no INDEX teaching; the
   resolver stays untouched.
4. **The IR wrinkle (DISK mode emits CDX only) resolves as a new small
   compiler mode `RESOLVE`** (seed-affecting, token per CL, red's):
   source on stdin as in every mode, cites resolved against the mounted
   volume, the RESOLVED UNIT answered as text IN UNICODE, like every
   other text mode and never in the CCE wire encoding (resolved chapters first,
   each renamed `Chapter: quire--name` as `disk-load-cite` does, then
   the source verbatim); sector-count 0 answers the source unchanged
   (no mount, no divide); a missing cited chapter still answers
   CDX3010. The page resolves once and compiles the result through the
   existing stdin paths. Boundary settled with fester direct: red
   emits and seeds RESOLVE; fester builds the in-tab wiring and the
   library image against this wire.

Proof standing at the convergence (landed 20689): the arm drives DISK
mode end to end in linear memory -- volume mounts off
`block-read-sector`, SOURCE.SRC compiles, OUT.CDX is written back and
dug out of the mutated image by an independent test-side FAT16 referee,
byte-identical to the stdin compile, with a no-disk control refusing.

#### Stage 2 WIRING result (fester, 2026-08-28)

**The substrate meets on the first try, and the one defect was in the frame's
ENCODING rather than anywhere in the disk path.** Driving RESOLVE against a
library image built by `build-img.ps1 -Library`, with the probe source
`Chapter: ProbeRead / cites Foreword chapter Maybe`:

| arm | bytes back | |
|---|---|---|
| RESOLVE, library volume mounted | 746 | prefix present |
| RESOLVE, NO disk (the control) | 79 | source alone |

746 - 79 = 667 bytes of prefix against `Maybe.codex`'s 691 ASCII bytes is the
chapter, read off the medium, renamed, transitively deduped, exactly as
designed. **The volume, the GPT/FAT16 layout, the mount and the resolver were
all correct with nothing to fix.**

What was wrong: `emit-resolved` printed the body with `print-text`, which is
`IR-CCE`'s WIRE writer, so the frame came back in CCE and every consumer
reading it as UTF-8 got mojibake. **The no-disk control is what pinned it**,
because that arm resolves nothing by design (sector-count 0 answers the source
unchanged), so the source alone had to come back verbatim and instead came back
as `2E9 /64`. That isolates the encoding from the disk path in one run, before
any of the disk machinery is suspected. Fixed to `print-uni` (unicode, no
newline, so the frame's byte shape is otherwise unchanged); gate green, hard
fixed point in one pass.

**ABLATION MEASURED, and the symptom moved.** Against the fixed kernel, the
no-disk arm returns the probe source BYTE-IDENTICAL (51 chars, compared
character by character rather than by eye), where before the fix it returned
`2E9 /64`. With the volume mounted the frame now reads `Chapter:
Foreword--Maybe` followed by the chapter. **Both arms are the same LENGTH as
before the fix -- 79 and 746 bytes -- which is the confirmation rather than a
coincidence: only the encoding moved, so the resolver was doing the right thing
all along.**

**Consequence worth recording: Update 53 shipped RESOLVE mode with an
unreadable frame.** RESOLVE landed at main 20736 and the release cut from
20765. Nothing at main consumes the frame yet -- the page wiring is still
shelved -- so the public harm is nil, but the released compiler cannot serve
the mode it advertises.

**C5a's threshold needs re-deriving before it is quoted again (L-COUNT).** It
is written as an absolute -- "exceeds 6,311,906 bytes" -- taken when the
deployed page was 6.3 MB. The page is **10,389,218 bytes** today, so the
doubling rule it encodes now means 10,389,218.

**And the size question the stage was prepared to lose is not close, because
the volume GZIPS.** Measured: 14,680,064 bytes of volume, of which 6,139,569 is
chapter text and the rest zeroes, compresses to **1,571,834** -- base64
2,095,780, or **+20 per cent** of the page against the +190 per cent raw
embedding would have cost. `DecompressionStream` is a browser builtin, so this
is one API call rather than a decompressor of ours compiled to wasm.
**Consequence: the WHOLE 650-chapter library ships, and the fallback to "the
smallest quire set that compiles NotesPage" is not needed.** The image builds
in 14.0 s against C5b's 60 s ceiling.

**Held at the release window, shelved and not verified** (fester 20757): the
page wiring, the region shift a prepended prefix forces, and the toolbox over
the library. The wiring is written; nothing about it is proven until the page
builds and the arm runs.

#### Interludes LANDED on the way to this stage (red, 2026-08-28)

- **The compiler-source preset** (20592, deployed): the whole
  concatenated source as a preset that takes the project to itself;
  big-file guards (8 MB storage caps, plain highlighter past 512 KB,
  5000-line paint cap, deck ladder enters at 125 past 1 MB). Subsumes
  the self-compile page's purpose; index.html retirement waits on the
  TEXT identity anchor coming over.
- **Compliance evidence from the Binary tab** (20600, deployed): built
  from the CDX header itself; root cause was the wasm runtime's silent
  256-unit read-line cap (raised to 4096, x86 measured unbounded).
- **The CDX target** (20621, deployed): the compiled payload as a
  savable artifact, named for the active file.
- **The fleet-day page union** (main 20689): the three features above
  re-merged beside val's Claude panel and reek's configs after the
  merge-down's accept-theirs dropped them from head; all twelve red
  arms plus val's panel arm green over the union.

### Stage 2c -- Signing keys and the Config section (red; design first)

A Config section on the page. The user can GENERATE a signing keypair
in-tab (the identity/keygen machinery we already ship, compiled to
wasm; never the project's key -- a user's own key is product), save it
to disk or keep it referenced in config, and built CDX artifacts are
signed with it. First read before any code: the 5-phase verifier
(`codex/os/verify/`) and whose key it pins -- whether a user-signed CDX
is verifiable on-device or needs a trust-config entry decides the
feature's shape. Coordinates with reek's Configs edit panel: one Config
surface, two kinds of entries.

**The read is done (red, 2026-08-28) and the answer shapes the stage:
the verifier pins NO key.** `verify-integrity` checks the signature
against the AUTHOR key embedded in the header
(`CdxVerifier.codex:35-45`), and `verify-author` separately scores that
key against the TRUST LATTICE with a threshold
(`CdxVerifier.codex:49-55`). A user-signed CDX is therefore verifiable
by construction; whether a DEVICE accepts it is lattice policy, which
belongs to the repository protocol and stays out of page scope -- the
panel says so honestly rather than implying acceptance. And the
compiler emits UNSIGNED CDX (signing is a separate step; the content
hash at bytes 8-39 deliberately excludes the signature), so the page's
CDX target today hands over an unsigned artifact. The stage's shape is
therefore: an in-tab SIGNER module (a small plug-shaped wasm module
over the existing Ed25519 chapters, `CdxBinary.codex`'s
`ed25519-public-key`/`ed25519-sign` + `encode-signed-header`), keygen
in the same module, the key held in the Config section and exportable
to disk, and Save-signed beside the unsigned CDX save.

### Stage 2d -- Dev-environment endpoints (red; "if possible" honored)

Config gains build endpoints: a URL per dev environment. After a lens
or binary build, the page POSTs the artifact to the configured endpoint
to auto-kick a build on that box. Ships with a small host listener
(`apps/prism/dev-listener.ps1`) that answers CORS and runs the
configured build command; a static page can POST to localhost, so this
is possible without any server behind the page. Overlaps reek's
native-build bridge probe; whichever lands first, the other reuses its
transport.

### Stage 2e -- Boards as targets (Damian ruled 2026-08-28 evening; in-tab half is reek's)

Damian's definition (CurrentPlan ruling 2): **IoT board build targets
-- build a Prism project for the nine HAL board chapters, per-board
output beside the kernel chain.** The in-tab build half is
CONSOLIDATED UNDER REEK (native plugs as wasm modules on the page, in
flight -- `PlugIrBytes`/`RiscVStdio` are its first landings); this
design carries the artifact map so no dead pills ship (L-ACCEPTED
applied to UI). **CORRECTED 2026-08-29 (reek), measured: ESP32-C6 and FE310
do NOT have a chain, and the blocker is ISA WIDTH rather than link
addresses.** FE310-G002 is RV32IMAC and ESP32-C6 is RV32IMC; the riscv
backend emits RV64, using `LD` and `SD` (funct3 3), the doubleword load and
store, which do not exist on RV32. No amount of per-board link/flash work
reaches them -- it needs an RV32 mode: ELF32, 32-bit pointers, no doubleword
ops. The likely source of the original reading is that FE310's SRAM base is
`0x80000000`, the same number as QEMU virt's RAM base. `QemuVirtBoard` in
`codex/boards` is AArch64, not RISC-V, and the remaining six chapters are
Cortex-M or Cortex-A with no codegen at all, so **no chapter in
`codex/boards` can run what we emit**. What DOES work is the synthetic RV64
platform the cross battery already boots (`tools/renode/codex/
codex-riscv64.repl`: rv64gc, 1 GiB at 0x80000000, NS16550 at 0x10000000), and
an in-tab kernel for it is proven -- see `plugs-backlog.md` 2.09, which
records the boot and the three defects fixed to get it. The original survey
follows, kept because its OTA and no-chain rows still hold. Survey (red,
2026-08-28): ESP32-C6 and FE310 have a real
chain through the riscv plug's own ELF writer
(`codex/plugs/riscv/RiscVElf.codex`) pending a proven flashable
board-ELF and per-board link/flash answers; STM32F4 and ESP32-C6 (the
boards `OTAFirmwareUpdate.md` names storage mechanisms for) get the
signed-CDX OTA artifact the day stage-2c signing lands atop the
existing CDX target; nRF52840, nRF9160, RP2040, STM32L4, Pi 4 and QEMU
virt have no in-tab-producible chain (no Cortex-M/A codegen plug) and
get no pill until one lands, re-surveyed then.

### Stage 2f -- Bench in the environment (Damian ruled 2026-08-28 evening)

Damian's definition (CurrentPlan ruling 3): **run our codegen
benchmarks and comparison against any configured output build chains**
-- it CONSUMES the native-build configs feature (reek's) and queues
behind it. `bench/` is the cross-language tree (c, codex, csharp,
fsharp, zig). The codex bench sources join the library tree and
compile in-tab like any preset (the js lens runs them in-tab for a
same-machine relative timing); the native comparisons ride the configs
bridge when it exists. The whole-tree browsing ask ("scan the tree of
forwards, apps, codex, plugs, boards, bench, test") is stage 2's
library-image scope; which subtrees embed versus bind via Open Folder
is decided by the C5a size table above, re-measured per set.

#### RULED: (B), the compiler's own resolution (root, 2026-08-28)

Root ruled while red was resting, on the probe's numbers: emitter 16 lines
against 150, size +30 to +72 per cent against a doubling, e2e 27 of 27, and
(B) keeping ONE resolver where (A) ships a second implementation that drifts.
**Red can overturn this in one line on return**; it is recorded here so the
reasoning is available and not only the verdict.

The layout mismatch is (B)'s first work item, not a blocker.

**The ruling offered two ways to adapt and ONE OF THEM DOES NOT EXIST.** It
put the choice as "build-img gains a quire layout, or the in-tab loader learns
the flat mapping". The second is unavailable: the block imports are
SECTOR-granular (`block_read_sector(lba)`), and path walking happens inside
the compiler, in `Fat16.codex` (`fat16-split-path`, `fat16-walk-path`,
`fat16-resolve-path`). The JS side serves bytes and never sees a path, so it
has nowhere to do the mapping. The good half of the same fact: the compiler
ALREADY reads subdirectories, so nothing on the read side needs building.

**A third option is also closed, and closing it matters more than it looks.**
`make-fat16-subdir.ps1` creates one directory and is written from the FAT16
spec rather than from `Fat16.codex` ON PURPOSE, so that a fixture is not built
by the code under test. `Build.md` classes that independence as the thing to
preserve. Extending it into the production path would spend a control to save
an afternoon.

**So the live options, and the criteria to choose between them, written before
the measurement:**

| option | what it costs |
|---|---|
| (i) teach `build-img.ps1 -SourceDir` to write quire subdirectories | adds to the hand-written PowerShell FAT, which is a THIRD implementation of our own format |
| (ii) build the library image through the img PLUG, which is `Fat16.codex` and already has `fat16-create-in-subdir` | our own format code, single source of truth, the direction the stage already argues for |
| (iii) teach `disk-load-cite` the INDEX file | a compiler change, seed-affecting, for one consumer's benefit |

| # | Question | Fails if |
|---|---|---|
| D1 | Format implementations | the option ADDS to a PowerShell or JS restatement of FAT rather than using `Fat16.codex` |
| D2 | Seed | the option is seed-affecting, unless nothing else can work |
| D3 | Cost | more than 150 changed lines, the bound C1 used |
| D4 | Read side | any change to `disk-load-cite` or `Fat16.codex`'s read path, which is shipped and working |

On D1 and D2 the shape of the answer is visible before measuring and is
recorded as a PREDICTION so the measurement can falsify it: (ii) should win,
because (i) grows a third FAT implementation and (iii) moves the seed. If the
measurement contradicts that, the measurement governs.

#### The measurement falsified the prediction, twice (fester, 2026-08-28)

**(ii) was wrong on its premise.** The img plug does NOT use `Fat16.codex`; it
carries its own `Fat16Writer.codex` citing only `CCE`. So "build it through
the img plug" is not the single-source-of-truth option, it is a second Codex
writer. The tree holds THREE production FAT16 implementations
(`codex/foreword/core/Fat16.codex` at 75,007 bytes, the img plug's writer at
9,874, and `build-img.ps1`'s hand-written PowerShell at 30,702) plus two
deliberate independent controls (`make-fat16-subdir.ps1`, `fat16-walk.ps1`)
that must stay independent. **D1's own wording was too narrow**: it failed an
option that grows a "PowerShell or JS restatement" and did not contemplate a
second CODEX one.

**A better option appeared once that was clear:** write the library image with
`Fat16.codex` itself, which already creates directories and nested files, is
the code the compiler READS with, and is exercised today by
`codex/test/fat16-mkdir`, `fat16-dirgrow` and `fat16-overwrite`. Zero new
format code.

**And then that option hit a defect in the shipped writer, which is now
stage 2's real blocker.** `fat16-write-file "foreword/BBB.codex"` answers
`True` while truncating the extension to 8.3; the reader does not truncate,
so `fat16-read-text "foreword/BBB.codex"` answers `<none>`, `read BBB.COD`
answers the content, and `fat16-file-exists "foreword/BBB.codex"` answers
`False`. `disk-load-cite` asks for exactly `quire-to-dir quire & name &
".codex"`, so on-disk cite resolution cannot reach a chapter our own writer
wrote. Registered in `CurrentPlan.md`; it is seed-affecting and unowned.

Directory names are fine, incidentally: `foreword`, `os.trust`,
`foreword.compress` and `foreword.game` all create successfully, so the
dotted quire names `quire-to-dir` produces are not the problem. The
extension is.

**Stage 2 is therefore blocked on a Foreword defect rather than on anything
in Prism**, and the adapter choice cannot be settled until it is fixed: every
option assumes a volume where `<quire>/<Name>.codex` round-trips.

### Stage 3 -- Kernels and deployables (red)

"Build kernels that just do whatever you want":

- **Templates.** "New project from template": today ExplorerServer
  (HTTP/JSON over the net stack) and the games' GameServer; the guios
  webserver app joins when val lands it; router and RDBMS templates
  join when those apps exist (their absence is app-lane work, tracked in
  the owning backlogs, not here -- Prism is the environment, not the
  apps).
- **Build tab.** Any project builds to CDX, then through the existing
  in-memory chain to `BOOTX64.EFI` and `codex.img`; a "how to run this"
  panel prints the QEMU line, the codex-vm line, and points at the USB
  flashing rules (which live host-side; the page never flashes).

Acceptance: a server template built ENTIRELY in-tab is carried to the
host, booted in codex-vm, and answers HTTP -- the artifact is graded by
running it, not by existing.

### Stage 4 -- Claude in the loop (red)

An optional right-hand panel: REPL chat, or agent mode, or off.

- **Wire.** Raw `fetch` to `api.anthropic.com/v1/messages` with SSE
  streaming. Direct browser access is a supported, documented mode: the
  official SDK gates it behind `dangerouslyAllowBrowser` and the docs
  bless exactly our case (an internal/local tool, the user's own key).
  No SDK, no npm -- the page stays self-contained. Default model
  `claude-opus-5`; adaptive thinking; streaming always.
- **Key handling.** The key is entered by the user, lives in
  `localStorage` on their machine, is never written into project files,
  exports, or built artifacts, and the panel is inert until a key
  exists. The deployed public page carries the same panel (a key a user
  types into their own browser is their own; nothing ships secret).
- **Agent mode.** A manual tool loop in the page (the API's
  `tool_use`/`tool_result` cycle) over local tools: `list_files`,
  `read_file`, `write_file`, `compile`, `read_diagnostics`, `run_lens`.
  Writes require a per-session approval toggle. The loop, the tools,
  and the transport sit behind ONE provider interface (messages in,
  streamed deltas and tool calls out) so a second provider is a new
  implementation of that interface, not a rewrite -- that is Damian's
  "later some other agent provider" ask, honored structurally now.

Acceptance: a chat round trip streams; agent mode, given a project with
a planted compile error, reads the diagnostic, edits the file, and
recompiles to green, entirely locally (the sabotage arm is the planted
error; the control is that with tools disabled it cannot).

#### The request shape, pinned (val, 2026-08-28)

**Written down because every line of it is a thing a model writes wrongly
from memory.** The Messages API moved in 2025-2026 and the stale shapes
are the ones that come to hand first; each row below is a 400 or a silent
misbehaviour, not a preference.

- **Endpoint and headers.** `POST https://api.anthropic.com/v1/messages`,
  with `content-type: application/json`, `x-api-key: <the user's key>`
  and `anthropic-version: 2023-06-01`. Direct browser access additionally
  needs the header that opts into it -- the same gate the official SDK
  calls `dangerouslyAllowBrowser`.
- **Model `claude-opus-5`**, exactly that string. Never append a date
  suffix: the dated variants are a training-data habit and this id is
  complete as it stands.
- **`budget_tokens` IS REJECTED WITH A 400 on Opus 5.** The fixed
  thinking-budget is gone. Send `thinking: {type: "adaptive"}` and control
  depth with `output_config: {effort: ...}` instead. This is the single
  likeliest thing for a later session to get wrong, because
  `{type: "enabled", budget_tokens: N}` was correct for years.
- **Set `display: "summarized"` explicitly, and this one is a UX defect if
  missed.** On Opus 5 the thinking display defaults to `"omitted"`, so a
  streaming panel that does not ask for the summary shows the user a long
  silent pause and then a wall of text. Thinking happens and is billed
  either way; `display` only decides whether the panel can show it.
- **Assistant prefill returns a 400.** Constrain shape with
  `output_config.format` or the system prompt, not by seeding the last
  assistant turn.
- **Handle `stop_reason: "refusal"`.** It arrives as an HTTP 200 with a
  `stop_details` category, so a panel that reads `content` without
  checking `stop_reason` renders an empty bubble. Server-side fallbacks
  are the supported answer and should be on by default.
- **Streaming is SSE** over `message_start`, `content_block_start`,
  `content_block_delta` (`delta.text_delta.text` is the piece to append),
  `content_block_stop`, `message_delta` (carries `stop_reason` and
  output-token usage), `message_stop`. `max_tokens` around 64000 is the
  streaming default; a low cap truncates mid-thought.

**Stage 4 splits, because the acceptance needs something this lane cannot
supply.** "A chat round trip streams" needs a real key and a real billed
call. So:

| | what | how it is proven |
|---|---|---|
| 4a | the wire, key handling, and the SSE reader | a headless node arm feeds a CANNED SSE byte stream through the page's own reader and asserts the assembled text, the stop reason and a mid-delta chunk boundary. No key, no network, no spend -- the same shape as stage 1's arm |
| 4b | agent mode, the manual tool loop | the planted-error arm the stage already specifies |

**The chunk boundary is the point of 4a's arm.** A reader that works on
whole events and breaks when one SSE event is split across two network
chunks passes every naive test and fails on the wire; the canned stream
must deliver a `content_block_delta` in two pieces.

#### BUILT: the panel and the tool loop (val, 2026-08-28)

The dock, the modes, the key handling, the tools and the manual tool loop are
in `codex/plugs/wasm/page/prism.html`, and the arm
(`codex/plugs/wasm/page-claude-arm.js`, `node` it, prints `ARM OK`) drives all
of them through the seam described below. The deployed copy under
`apps/landing/web/compile/` is NOT rebuilt, the same as 4a.

**The service call is one function and it is injectable, which is the whole
shape of this CL.** `fetchTransport(key, body, onChunk, signal)` is the only
code that talks to the network; `setTransport` replaces it. A transport pushes
raw SSE TEXT at its callback and nothing else, so the request body, the reader,
the sink, the tool executor, the loop and the panel are all exercised by the
arm as the SHIPPED code, with a scripted transport that cuts every event at 60
per cent of its length. That cut is deliberate: a fixture of whole events
cannot fail (L-FALSIF), and this is the same boundary 4a's arm already proves
the reader survives.

Three things the panel does that are not obvious from the stage text above:

- **The sink now keeps the turn's content blocks IN ORDER, with thinking
  signatures.** An assistant turn replayed as the history of a tool call must
  carry its thinking blocks back with the server's signature, so a sink that
  keeps only the flat text can drive a chat panel and cannot drive a tool loop.
  The arm asserts the replayed block, signature included.
- **`write_file` refuses rather than answers when the session has not approved
  writes** (L-BAILVALUE): it changes nothing, names itself `REFUSED`, and comes
  back as an `is_error` tool_result so the model is told. Same for the loop's
  24-step cap, which throws rather than handing back a turn that looks finished.
- **The panel reads `stop_reason` before rendering.** A refusal is an HTTP 200
  with no text, so the panel says the model declined and names the category
  instead of painting an empty bubble.

**What is proven, and by what.** Four sabotages, each moving exactly the
assertions predicted and no others: dropping `signature_delta` moves the
signature-replay row alone; removing the write guard moves five rows including
both control rows; making the step cap return instead of throw moves the cap
row; deleting the refusal branch moves the category row alone -- and NOT the
bubble-class row beside it, because the no-text branch paints the same class,
so that row discriminates an empty bubble and not a missing branch.

**The compiler shim is gone (val, 2026-08-28, second pass).** The arm now runs
the planted-error section TWICE: once with `agentEnv.compile` replaced, which
is a fast test of the loop, and once with the shim REMOVED, driving the page's
own `compileForAgent` over the real `codex-compiler.wasm`. The module ships as
base64 inside the deployed page's embed block and exists nowhere else on disk,
so the arm lifts it from there and hands it in through the page's own
`window.__EMBED` door; a module it cannot find FAILS the arm rather than
skipping. `runW` falls back to its synchronous path because the sandbox has no
Worker, which is the fallback the page keeps for a small worker stack. The
planted error is an unresolved name, chosen because a compiler of any vintage
refuses it and the deployed module was built by an older seed (L-SAMEVER).

What that section adds over the shimmed one: the diagnostic is REAL
(`CDX3002`), and it arrives remapped to `main.codex:9:37`, which is the half a
shim cannot test -- raw diagnostics come back in the assembled unit's
coordinates and the tool has to answer in the file's. Its control is the same
script with writes unapproved: the file stays broken and the second real
compile still refuses. Sabotage: disabling `mapDiagLine` moves the coordinate
row alone, and dropping the diagnostics from the compile tool's answer moves
three rows across both sections.

**One assertion-design finding, kept because it nearly shipped as a pass.**
While the arm's sandbox was handing the module a stringified byte array, the
compiler answered `no input mode on stdin` in three milliseconds and the
section's first check, "the real compiler refused the planted error", PASSED.
A refusal is a weak assertion on its own: it cannot tell a compiler that read
the program from one that never saw it. The CDX code and the file coordinates
are what carry the section. The cause was a cross-realm `instanceof` in the
arm's node sandbox and is written at the site.

**What is still NOT proven.** The stage acceptance's chat half -- "a chat round
trip streams" -- needs a real key and a real billed call, and no arm in this
lane can supply either. Everything reachable without one is now covered.

### Stage 5 -- User-mode executables (red; seed-affecting; the big one)

**STAGE 5 IS DONE FOR BOTH TARGETS, 2026-08-29 (reek, Damian direct).** A Codex
program compiles to a static Linux ELF64 and to a Windows console PE32+ and
runs on each: 60 of 60 grades across the two, output byte-identical to the
`.expected` sidecars the bare-metal battery uses.

Both were behind one thing, and it was not the container. The runtime kept its
cells at fixed low addresses that Windows reserves and Linux's mmap_min_addr
covers. A `hosted-target` selector on `CodegenState` moves them, and above that
the work was smaller than this design assumed, because the print path funnels
through one helper: hosted swaps `__serial_put` for `write(2)` or kernel32
`WriteFile` and inherits the CCE conversion unchanged, `__start` gets an arm
that skips every hardware structure, and the exit is `exit_group` or
`ExitProcess`.

Containers: `codex/plugs/elf/cdx-to-elf.ps1`, `codex/plugs/pe/cdx-to-pe-console.ps1`.
Runner with a calibration arm: `codex/plugs/elf/hosted-elf-test.ps1`. The full
account, including four Windows findings that are cheap to re-derive wrongly,
is `plugs-backlog.md` 2.11.

**AND THEY REACH THE PAGE (2026-08-29).** Both containers are ported from the
proving PowerShell into Codex -- `ElfStdio` mode 2, `PeStdio` mode 3, each
taking the CDX whole because the hosted container needs the entry offset the
older wire does not carry -- and built into the modules the site serves. Driven
exactly as the page drives them, the page's own `codex-compiler.wasm` plus the
container module produce a Linux binary and a Windows `.exe` that both run and
print the right answer. The compiler module had to be rebuilt first: `hosted`
and `hosted-windows` did not exist in the one the site was serving, so a hosted
target is a different compile rather than a different wrapper. Account and the
two PE defects the byte-comparison caught: `plugs-backlog.md` 2.12.

**5c, sockets, is untouched and is what "run a SaaS server" still waits on.**

**Not in v1:** stdin, and any subject reaching a kernel service. Those are
scoped out by what the source asks for, which is this design's own "scope v1 is
console programs".
### Stage 6 -- Run-in-tab (later, optional)

Compile-to-wasm of user programs run directly in the page (the two
imports plus a stdin panel); a service-worker route that serves a built
webserver app's responses inside the tab as a demo. Nothing in earlier
stages depends on this.

## Decisions this design asks of Damian

1. **The Linux verification bed** for stage 5a artifacts. R-SHELL
   permits WSL for one workflow only (GDB); grading a user-mode ELF
   needs a Linux to run it. Options: extend the WSL exception to this
   verification arm; a QEMU Linux guest image kept host-side; or accept
   Windows-only verification until 5c. Recommendation: the WSL
   exception, narrowly worded, verification only.
   **RULED 2026-08-28 evening (Damian, direct to root): ALL of the
   options** -- *"do all, we are supporting all these options for the
   people."* The WSL exception lands (R-SHELL amended in CLAUDE.md,
   verification only), the QEMU Linux guest bed is built beside it, and
   5b proceeds with native verification on this box. Stage 5a is
   unblocked.
2. **Stage order confirmation.** 0 -> 1 -> 2 -> {3, 4 in either order}
   -> 5 -> 6, with 5a before 5b. **CONFIRMED as proposed (root
   commanding, 2026-08-28).** Ruling 1 (the Linux bed) amends R-SHELL,
   so it stays Damian's; carried to him with the narrow-WSL
   recommendation endorsed. Stage 5 is not blocked meanwhile.

## Features added 2026-08-28 evening (Damian, direct to root; stages are red's to cut)

Recorded here so the campaign owner folds them into numbered stages;
each is dispatched or queued in the CurrentPlan Prism section:

- **Native-build configs + a Configs button and edit panel** (reek,
  in flight): where a javascript lens says Run, a lens like c# says
  Config -- pick your local compiler target, save it to a named config,
  run the final native build, then run the output. Saved configs are
  managed in an edit panel, not write-once. The run half needs a
  mechanism a static page cannot supply (an optional local bridge the
  page fully works without is the obvious shape); this supersedes the
  third bullet below for the CONFIGURED path only -- the lens outputs
  themselves stay toolchain-free.
- **Boards** (val, after the desk-root-guard BVT wiring): IoT board
  build targets -- build a Prism project for the nine HAL board
  chapters, per-board output beside the kernel chain.
- **Bench** (queued behind configs): run our codegen benchmarks and
  comparison against any configured output build chains.

## What this campaign does not do

- No new server processes; the essay-repl join and `apps/prism/server.ps1`
  are untouched (their register rows stand).
- No app development (webserver/RDBMS/router apps belong to their
  lanes; Prism consumes them as templates when they exist).
- No foreign toolchains on the primary path. The transpiler lenses
  remain what they are: outputs a user takes to their own toolchain
  (amended above: a user-CONFIGURED toolchain may be driven through the
  configs feature; nothing on the primary path depends on one).
