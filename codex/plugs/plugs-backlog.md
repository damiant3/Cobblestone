# Plugs -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says what is
still missing and nothing else, a closed entry is DELETED, and a gap that is
still real is never quietly dropped. `docs/PM/CurrentPlan.md` carries the
shape. **The depot is the record of what was done; this file is only what is
left.**

## Standing hazards

**A plug that does not handle a construct usually EMITS SOMETHING ANYWAY and
reports OK.** A missing builtin arm passes the name through as an ordinary
call; a wrong field spelling emits a division; a wrong `list-push` emits a
mutating append. For most of these plugs nothing downstream ever runs, so
silence is silence, not agreement (L-GAP).

**A LITERAL PATTERN IS A SECOND CODE PATH AND IT IS THE ONE THAT ROTS.**
Found by Steve Howell, 2026-08-26, who fixed it in his own zig plug and
reported the class. A Boolean `IrLitPat` carries the SPELLING `True` or
`False` rather than a number (bare metal decodes it in `pat-lit-to-integer`,
`codex/compiler/Syntax/Token.codex:149`). **Nearly every plug in this tree
already maps that spelling correctly where a Boolean appears as an
EXPRESSION, and did not where it appears as a PATTERN** -- the two paths are
separate in every plug and the pattern path gets written by copying the
integer case. Measured by running the emitted programs: csharp CS0103,
javascript `ReferenceError: True is not defined`, zig undeclared identifier,
all three fixed 2026-08-26. Python, Haskell, Ada and Pascal spell their
Booleans the way the wire does and are safe by coincidence, not by handling
it. **Two further defects surfaced only once the first fix let the programs
run further, which is the part to generalise: a literal-pattern bug hides
the next one behind it.** csharp appended a catch-all after arms naming both
`true` and `false`, which C# rejects as CS8510 unreachable; javascript gave a
Char literal pattern no BigInt suffix while the scrutinee carried one, so
`15n === 15` was false and every char arm fell through to the catch-all --
unrelated to Booleans and failing before any of this. **Grade a plug with
`codex/test/when-bool-cross` and `when-bool-pattern`**, which carry integer
and char controls precisely so a fix that breaks the neighbouring literal
kinds shows up. **UNSWEPT, and this is a lead rather than a finding:** the
remaining plugs were read, not run, and every one that emits `IrLitPat` text
verbatim into a target spelling Booleans lowercase is a candidate. **Queued
for the wasm plug (fester's, not touched here):** these two tests should gate
it early, per Steve's suggestion.

**RECORDED LEAD, NOT BUILT: the plug wire performs no arity check.**
`codex/plugs/common/IRTextParser.codex:705` builds `IrApply` structurally,
so hand-authored IR can express shapes the compiler cannot produce -- a
non-full-arity self-application in tail position being the measured example
(`docs/DevelopersRulebook.md`, "What the wire carries"). Every plug's TCO
gate is safe against COMPILER-produced IR by the type checker's occurs
check, and unprotected against anything else. Whether that matters is a
question about the plug wire's TRUST MODEL rather than about any plug, so it
is recorded here and deliberately not acted on. Raised by Steve Howell's
PR 87, answered 2026-08-26.

**A name census cannot answer a semantics question, in either direction.**
Keying on the quoted Codex name misses a plug that declares the arm in a
prelude and counts a plug whose REFUSAL text contains the name. A registered
name is not a correct arm either. Run a subject through the plug and read the
OUTPUT.

**A STALE PLUG BINARY IS A CONFIDENT WRONG ANSWER IN EITHER DIRECTION.**
Nothing here runs from the `.codex` you are reading; every harness runs the
`.cdx` beside it. Rebuild before believing any measurement through a plug, and
treat a merge-down as invalidating every plug binary it touches -- the seed
moves under the workspace and nothing rebuilds a plug when it does.
`build/plug-oracle-test.ps1` refuses a binary older than its source or than
`seed/Codex.cdx`; nothing else does.

**`codex/plugs/zig/` is ordinary fleet code** (Damian, 2026-08-18). Credit
Steve Howell in a CL that changes what he wrote and flag it in the next
GitHubUpdate; that is courtesy, not a gate.

## Last full checkpoint

**2026-08-24, seed C9395985, at Damian's request and NOT a gate.** All 56
plugs rebuilt (56 of 56 clean), the 6 oracle-wired ones graded
**6 passed, 0 failed, 0 skipped, 49 of 49 values each** (python, javascript,
typescript, zig, wasm, csharp), and all 50 that take a `-Src` emitted.

The rebuild is the load-bearing part, not ceremony: `plug-oracle-test.ps1`
refuses a binary older than `seed/Codex.cdx`, and a seed moved that day, so
every one of the 56 was stale. Measuring through the old binaries reports the
PREVIOUS revision in either direction.

**Two apparent failures were the sweep's own instrument and one was its
classifier.** `wpf` emits a five-file PROJECT into a directory and was handed a
file path; `t3isa` rewrites the extension and wrote 39,468 bytes to `.t3s`
while the sweep watched the `-Out` path. And `recheck`'s 282 bytes were flagged
as a refusal because the regex matched the word `UNSUPPORTED` in a column
header reading zero -- the report says `AGREE 25 DISAGREE 0 UNSUPPORTED 0`
across three stages, which is a pass. Take one negative from any sweep here and
read it by eye before believing it.

The two REAL refusals are both correct. `babbage` refuses honestly, which is
what a shelved target should do. `t3isa` exits 6 and carries 43
`; !UNSUPPORTED:` markers over 1,729 lines, each naming a constraint of a
27-trit machine (an integer band wider than a word, records built once and not
mutated) rather than miscompiling them silently.

## Open

**THE CLOSE-OUT IS DRY OF DRAWABLE ROWS, re-read entry by entry 2026-08-27
(reek). Nothing here is both open and takeable on this box**, so a lane
arriving for the next entry in register order should read this paragraph and
go elsewhere rather than re-derive it.

What is left, and why none of it is a row to pick up:

- **Blocked on the no-new-toolchains rule:** 1.14 (a runtime per language to
  ablate), 1.20 (`fpc`), 1.39 (`cobc`), 1.46 (any runtime for an unwired
  plug). `docs/Agents/reek-blocked.md` carries the measurements; re-check
  them rather than trusting them, since two turn on what is installed.
- **Another lane's:** 1.3 (fester), 1.33 (blu).
- **Ruled, deferred or latent, and not to be re-opened without the ruler:**
  1.1 (Damian, deferred), 1.48 (red, latent), 1.53a and 1.54 (the real
  closure is a custom allocator over `VirtualAlloc` and `mmap`), 1.72
  (latent, and whether any well-typed program reaches it is unestablished),
  1.73 (Damian, SUPPORTED).
- **A ruling ask, not work:** 1.57's riscv half. The question is whether
  over-application of a named definition is required of every plug that keeps
  an arity map.
- **Design halves of rows whose plug halves landed today:** 1.97 wants the
  effect-op table to carry an environment pointer; 1.98 wants `-Measure` to
  report the CDX9002 it currently swallows. Both are named in their rows.

**Everything else in this section is a closed account kept for its
measurements.** The file's own rule is that a closed entry is DELETED, and
these have outgrown it: the wasm block from 1.60 to 1.95 is one campaign's
write-up and reads as open because the headlines are findings rather than
verdicts. Deleting them is somebody's call, not a side quest.

**1.62 -- DONE 2026-08-25 (reek), red's call.** `Get-PlugModuleCount` now
excludes `test/` beside `build-output/`, and the README reads **141**, not
153. Re-measured the day it landed: 153 under the old definition, 12 files
under `test/` across five plugs, 141 without them. The call was red's
because the fix lowers a public number during the push window, and the
argument that settled it is that the same README table counts `test/`
separately on the next line, so counting plug fixtures as plug source
modules disagreed with the table's own scheme. Nothing in the tree moved;
only the count's meaning was repaired.

The change went through the GENERATOR, `codex/build/checkdoccountsScript.codex`,
and the shipped script was regenerated from it rather than hand-edited.
Verified: `check-doc-counts` 63 claims 0 drifted, `check-generated-scripts
-Only check-doc-counts` match 0 drift, and `deck-headroom -Quire codex\build`
still OK with that chapter at 1.45 and the quire's tightest unchanged at
1.33. The emitted text is LF and the depot script is CRLF, so it was
converted on install; a raw copy reports all 442 lines changed (P-EOL).

The original account: **README's "N source modules" counted TEST FIXTURES as
plug source modules, and it was drifting once per subject added.** `check-doc-counts`
counts every `.codex` under any directory holding a `build.ps1`, excluding
only `build-output`, so the claim went 151 to 152 to 153 in one session as
two wasm subjects landed. Under the claim's own definition each bump was
correct, which is why the runner kept passing; the number simply stopped
meaning what the README says it means. **This is not one plug's problem:
`test/` holds 12 `.codex` across five plugs** (spirv 4, t3isa 4, wasm 2,
maui 1, ptx 1), so excluding it moves the public figure 153 to 141 and
silently reclassifies ten files four other lanes put there.

Damian deferred it to publication the same day (*"i dont care about the doc
count issue until publication"*); red called it sooner because the push
window is when a public number is about to be read. Both readings agree on
the outcome and it is closed.

**1.64 -- the assembled compiler module traps on its own input. DONE
2026-08-25 (fester). IT READS NOW.** `read-line` and `read-line-cce` are
wired to `wasi_snapshot_preview1.fd_read`, and the compiler's module gets
past its mode read: the trap moved from one frame deep at `read-line` to
three frames deep at `read-file-uni` inside `dispatch-on-mode`, which is
1.65 below. The module still assembles clean, 9,345,248 chars of WAT to
1,088,918 bytes, zero errors.

Bytes convert through a 128-entry reverse table on the way in, so what lands
in memory is CCE, matching what 1.61 established for the way out. 128
entries is the whole of it: a byte under 128 is ASCII whichever way it came,
which is the same band x86-64's `__read_line` covers with the same table.

**END OF INPUT DISCARDS A PARTIAL LINE, because that is the oracle's answer
and not a choice made here.** Measured on input whose last line carries no
newline: x86-64 reports the terminated lines and then end of input, dropping
the tail. The first implementation here returned the tail first, which is
the more obliging reading and disagrees with bare metal, so it was changed
to match. An empty line is still a Text of length 0 and NOT end of input;
the subject covers both, and they are the two the wrapping could conflate.

**`wasm-e2e.ps1` could not grade a reading subject at all until this item:
neither arm had an input path.** It now takes a `<name>.stdin` sidecar and
gives the SAME bytes to both, `-input` for codex-vm and a real file redirect
for wasmtime. PowerShell has no `<` and piping a string re-encodes it and
appends a newline, so the redirect takes the file. Without that sidecar the
two arms are not running the same program (L-SIDECAR).

**1.66 -- TWO DEFECTS SHIPPED IN 1.64, both found while reading the driver
for 1.65 and both fixed 2026-08-25 (fester). Reported rather than quietly
corrected, because both were green when they landed.**

**`read-line-cce` was wired to `read-line`'s converting reader, and it is a
different builtin.** Measured against x86-64: `__read_line` converts each
byte through the unicode-to-CCE table and ends a line on ASCII 10;
`__read_line_cce` converts NOTHING and ends on CCE 1
(`X86_64Helpers.codex:1212`), because its caller is a wire that already
speaks CCE. Observable on the plainest input there is: given `hi` and a
newline, bare metal answers **None** (still hunting a CCE 1 that ASCII never
contains) and the shipped plug answered `Just "hi"`. It now has its own
reader. **The paired arm matters as much as the fix:** an implementation
that always answered None would agree on that input too, so
`read-cce-rt.codex` feeds real CCE bytes (`20 17 01 0F 12 01`) and both arms
return `hi`, `an`, then end.

**The `.stdin` sidecar mechanism worked only until the sidecar was checked
in.** `Start-Process -RedirectStandardInput` opens the file for WRITE and
fails `Access to the path is denied` on a read-only one, and Perforce makes
every submitted file read-only. So 1.64's own 9-of-9 was green because its
sidecar had not landed yet, and the next agent to sync would have got a
failure that looked like a code defect. The harness now copies the sidecar
to a writable temp and redirects from that; re-run with
`read-line-rt.stdin` still read-only, 11 of 11.

**`read-serial-cce` is implemented here too**, because the 1.65 stream arm
needs it on this target and a compiler-side mode that this plug cannot
serve would be a mode that does nothing. Raw copy until NUL, matching
`__bare_metal_read_serial_cce`; graded by `read-serial-rt.codex`, where a
CCE newline round-trips inside the returned text.

**One number in it is a guess and is flagged as one:** the input buffer is
capped at 4 MB against a 16 MB linear memory, and the compiler's own source
is 2.94 MB. Whether 4 MB of input plus the compilation working set fits in
16 MB is UNMEASURED, and it is the first thing to measure when the stream
arm exists rather than something to assume.

**1.69 -- THE SPIN IS FIXED AND ITS CAUSE WAS NEITHER OF 1.68's DEFECTS. A
NESTED CONSTRUCTION CLOBBERED THE ENCLOSING OBJECT POINTER** (fester,
2026-08-25).

`$_rp`, `$_lp` and `$_tv` are ONE set of scratch locals per emitted function.
A record, constructor or list literal sets `$_rp` to its fresh block and then
evaluates its field expressions; if a field expression itself constructs
something, that construction resets `$_rp`, every remaining field store lands
in the INNER object, and the construction returns the inner pointer as
though it were the outer one.

**Read straight off the compiler's own WAT**, `tokenize-collect`'s `LexEnd`
arm, which is `LexCollected { collected-tokens = __linked-list-push acc
(make-token (deck-record EndOfFile) 0 st), collected-errors = st.errors }`:

```
(local.set $_rp (bump_alloc 16))              ;; LexCollected
(local.set $_tv (call $ll_push (local.get $acc)
   (call $make_token (call $deck_record
      (local.set $_rp (bump_alloc 8))         ;; EndOfFile, CLOBBERS $_rp
      (i64.store (i32.wrap_i64 (local.get $_rp)) (i64.const 0))
      (local.get $_rp)) (i64.const 0) (local.get $st))))
(i64.store (i32.add ... (local.get $_rp)) (i32.const 0)) (local.get $_tv))
```

So the token list was written OVER the `EndOfFile` tag. **Measured by
patching `is-done` in the emitted WAT to print `list-length tokens`, `pos`
and the tag it is about to test**: empty source gave `1 / 0 / 4294967316`
where `1 / 0 / 0` belongs, and `Chapter: Hi` gave `2 / 0 / 11` then `2 / 1 /
4294967316`. The last token of every stream had a corrupt tag, `is-done`
never answered True, `advance` clamps at the last index, and
`skip-to-next-line` looped forever. That is the whole of 1.67.

**The guard is the WASM OPERAND STACK, not a second local**: push the
enclosing pointer, evaluate the field, pop it back. `(local.set $x)` with no
folded operand pops, so it costs two instructions. It is emitted only where
the field expression can allocate (`wat-scratch-safe`), because emitting it
unconditionally grows the module by roughly a third; the predicate answers
False for anything it does not recognise, so an unknown shape gets the guard.

**GRADED BOTH WAYS, which is what makes the arm evidence.**
`codex/plugs/wasm/test/nest-ctor-rt.codex`: under the pre-fix plug the
nested constructor's tag reads `1014168712049066001` against x86-64's `0`,
while `nested ctor len` and `note` on the same object read CORRECTLY, which
is exactly why this survived twelve subjects. Under the fix all 9 rows
agree, and the whole suite is 13 of 13.

**Module cost, measured rather than predicted:** 9,468,360 chars of WAT
before, 9,568,192 after, +1.05 per cent, still assembling clean to
1,520,214 bytes.

**1.76 -- THE WASM COMPILER COMPILES A 252 KB REAL UNIT BYTE-IDENTICALLY TO
x86-64, AND 1.75 SHIPPED A DEFECT THAT HID IT** (fester, 2026-08-25).

`codex/plugs/wasm/build-output/plug-source.codex`, 252,035 bytes, the wasm
plug's own bundled source: **216,243 characters, SHA-256
`51CEBB12..1E65CC99` from wasmtime and from `codex-vm` running `Sut.cdx`,
diagnostics stripped from both.** 1.74's headline was a 102-character program.

**The defect, and it was mine, submitted in 1.75.** `__heap-advance` was
emitted as a bare `global.set $heap_ptr`. **x86 runs in a pre-mapped arena, so
advancing the cursor over a region makes that region writable; a wasm linear
memory only extends through `memory.grow`, which lives in `$bump_alloc`.** So
`build (size)` reserved a deck window that no page backed, and the first write
into it faulted. The fix is the honest mapping and is one line: `bump_alloc n`
with the returned pointer dropped IS "advance by n, growing to cover it".

**The message names the defect and it was not read.** `memory fault at wasm
address 0x1b600000 in linear memory of size 0x1b600000` -- **the fault address
EQUALS the memory size**, which is an access one byte past the frontier and
cannot be an address-space overflow. 1.75 published "an i32 address-space
limit, cascading reservations pass 4 GiB and wrap" from the symptom alone,
into this register, `CurrentPlan` and a CL description. Nothing wrapped and
nothing was near 4 GiB. **Read the fault address before naming a cause; wasmtime
prints both numbers and their relationship is the whole diagnosis.**

**The suite could not have caught it, and this is the third time on this target
(L-CONSTRUCT).** Every subject is small enough that the declared 16 MB already
covers each reservation, so the unbacked window is never touched. The new arm
`heap-advance-rt` advances 64 MB and writes at the far end and the midpoint:
under the shipped 1.75 plug the module traps outright, under the fix all four
rows agree with x86. Ablated against `//Codex/main/...#71` itself.

**Deck routing is FAITHFUL, which the corrected measurement shows and the
wrong one obscured.** Per-phase deck usage, wasm against x86, same source:

| phase | 1,282 B subject | 252,035 B subject |
|---|---|---|
| scope | 27,830 vs 27,456 (+1.4%) | 5,125,418 vs 5,789,648 (-11%) |
| check | 77,984 vs 39,616 (+97%) | 3,771,899 vs 2,289,072 (+65%) |
| lower | 282,509 vs 129,760 (+118%) | -- |
| resolve | 578,621 vs 205,776 (+181%) | -- |

SCOPE tracks x86 closely at both shapes. **CHECK, LOWER and RESOLVE run 2x to
3x, and THAT is the remaining consumption question**, not SCOPE.

**1.83 -- THE PAGE EXISTS, AND ITS FIRST REAL CLICK FOUND THE BOUNDARY THE
BEDS COULD NOT** (fester, 2026-08-25).

`codex/plugs/wasm/page/index.html` plus `build-page.ps1`: the compiler as a
wasm module, its own source beside it, phases reported on completion, and on
completion the page hashes its cleaned output in the tab and compares
against a bare-metal anchor. **The anchor is computed at page build, never
hard-coded** -- `build-page.ps1` runs the identical source through the x86
kernel and injects the hash, so the page's byte-identity claim is measured
from the exact bytes it serves, forever. Pipeline proven end to end in node
(V8): 2,460,178 chars, anchor `6F0A4122..`, 10 s.

**Damian's first click: phases green through resolve, then `Maximum call
stack size exceeded` at 240 emitted bytes.** Discriminated within the hour:

| question | answer |
|---|---|
| tail calls present in the engine? | YES (validate-probe green), so everything 1.82 fixed stays fixed |
| what dies? | the emit spine's genuinely NON-TAIL recursion, exactly 1.82's declared residue |
| reproduction | node worker_threads, same module, same input: **1 MB stack = the identical error at the first emitted bytes; 2 MB = complete, all 2,461,312 bytes** |
| why did wasmtime's 1 MB suffice? | `max-wasm-stack` bounds a leaner resource: Cranelift frames are a fraction of V8's, so the SAME depth costs 1-2 MB of V8 stack |

**So 1.82's claim stands AS STATED (wasmtime, `-W max-wasm-stack=1048576`)
and any gloss reading "a browser's 1 MB stack" is falsified** -- a browser
worker's stack behaves like the 1 MB arm and cannot be enlarged. The page
now tries the worker first (responsive UI) and on a stack death retries on
the MAIN thread, whose stack is larger; the retry is itself the measurement
in every browser that runs it.

**THAT FALSIFICATION IS ITSELF SUPERSEDED BY 1.91, and the page was rebuilt
on 2026-08-27 to carry the fix.** With the `IrAct` arm in the tail-call
walker the worker no longer needs the retry: measured on the shipped module
(`build-output/page/`, anchor `5B4CADE2..`, 2,465,149 cleaned chars), node
worker_threads with the page's own imports, mode line and cleaning, stack
pinned -- **0.25 MB dies with 0 bytes out, and 0.5 MB, 1 MB and 2 MB all
complete with all three hashing equal to the page's bare-metal anchor.** The
same harness against the module this page shipped on 2026-08-25 dies at
1 MB with 2,115,920 bytes out, which is what makes the reading evidence
rather than an assumption. So the retry is now a fallback for stacks under
half a megabyte rather than the path the self-compile depends on, and the
gloss "a browser's 1 MB stack" is TRUE of the shipped module. The remaining
honesty is that node's V8 worker REPRODUCES a browser worker rather than
being one; it earned that standing by reproducing this row's failure at the
same megabyte, and Damian's next click on the rebuilt page is the
measurement in the real engine.

**AND THE SECOND CLICK WENT GREEN. Damian's browser, main-thread fallback:
2,460,178 characters in 19.0 s, hash `6F0A4122..` computed IN THE TAB,
equal to the bare-metal anchor to all 64 characters.** The compiler built
itself in a real browser and proved its output byte-identical to bare
metal, witnessed on 2026-08-25. "The compiler runs in a browser" is now a
sentence this register permits, with its conditions attached: `decks=125`
and the page's own anchor. Its third condition, "main thread until the emit
spine is de-recursed", was retired by 1.91 and the 2026-08-27 rebuild. Suite arms never drove TEXT emission at
browser depth, which is how 23 of 23 coexisted with a first-click failure
(L-GAP: the corpus compiled small subjects and self-compiled only under
wasmtime).

**An instrument fix that is a standing rule for these procedures:**
PowerShell `-notmatch` is case-INSENSITIVE, and the diagnostic filter
`'^(WD:|PM:|HEAP|STACK)'` silently swallowed four emitted definition lines
(90 chars: `heap-hwm-addr`, `stack-min-rsp-addr`). Every equality claim
held -- both sides were filtered identically -- but the page's JS filter is
exact, and the anchor mismatch surfaced it. **Use `-cnotmatch` for any
cleaning that must agree with an exact-match consumer.** `build-page.ps1`
carries the fix and the account.

**The durable 1.14 close for browsers was PLUG-side and it is done** (1.91,
below). It is not `codex-emit-expr`'s tree descent, which is shallow and
healthy: the stack was `emit-streaming-ir-defs` recursing once per
definition because the tail-call walker had no arm for an `act`. No
compiler change, no seed, no token.

**1.83a -- THE PAGE CANNOT STREAM PHASE PROGRESS, AND THE CAUSE IS NOT
BUFFERING** (reek, 2026-08-26). Measured in node v24 against the built
module, 2.94 MB source, `--stack-size=8000`: first `fd_write` at **25.395 s
of a 25.59 s run**, with all eight `WD:PHASE-*` lines inside one
millisecond of each other.

`TEXT` reaches `emit-text-streaming` through `compile-plain`'s `else`
(`opening.codex:2127`), and that emitter DOES stream: 18,731 separate
`fd_write` calls. Emission is **0.20 s, 0.8 per cent of the build.** The
other **99.2 per cent is `compile-frontend`, which prints nothing at all.**
The eight phase lines are heap marks read off `fe.heap-marks` AFTER the
front end returns (`opening.codex:1463`, printed `:1484`), so they cannot
precede the phases they name; the page was reading a completion report as
a progress stream.

Two traps this closes. Reading `emit-text` (`opening.codex:1668`), which
does build the whole output before printing it, gives a mechanism that fits
the symptom perfectly and is the wrong function (L-MECHANISM). And a
240-byte CCE flush (`WasmEmitter.codex:290`) makes guest-side buffering the
obvious suspect; it is not, because the flush fires per print call.

**1.84 -- A PLUG CAN NOW RUN AS A WASM MODULE ON STDIN AND STDOUT, AND THE
NETWORK ENTRY IS UNTOUCHED** (reek, 2026-08-26, Damian's direction).

Every plug opening in the tree is `[Console, FileSystem, Network.Read,
Network.Write]`: it takes IR over NE2K and answers over TCP. That was all a
plug needed while a plug only ran on bare metal behind a socket. A wasm build
has neither a NIC nor a socket, so no plug could run in a browser at all.

Measured before designing: 45 files carry that opening, and the transpiler
entries are **byte-identical apart from three things** -- the chapter name,
the port, and the one `emit-<lang>-chapter` call. `AdaPlug.codex` against
`JavaScriptPlug.codex` differs in exactly those lines and nothing else.

The generalisation is a second entry, not a change to the first (L-FALLBACK):

- `codex/plugs/common/PlugStdio.codex` is the whole transport, eight lines.
  It reads IR with `read-file-uni ""` and calls `plug-emit-ir-stream`.
- A plug supplies `plug-emit-ir-stream : Text -> [Console] Nothing`. For
  javascript that is `JavaScriptStdio.codex`, reusing `JavaScriptEmitter`
  unchanged; for csharp, `CSharpStdio.codex`.

**The contract STREAMS rather than returning Text, and csharp is why.** The
first version was `plug-emit-ir : Text -> Text`, which fits every transpiler
ending in one `emit-<lang>-chapter` call. `CSharpPlug` does not: it prints def
by def with `print-uni` and reclaims the per-def heap with `__heap-restore`
between them, deliberately, so the whole IRChapter is never materialised. A
Text-returning contract would have forced csharp to give that up. Streaming
subsumes both shapes, so it is the one contract.

**csharp also needed its shared helpers without its transport, and that is a
build-script feature rather than a copy.** `stream-defs-sexp` and
`collect-mut-names` live in `CSharpPlug.codex` itself, beside the network
opening. `build-plug-wasm.ps1` therefore takes a chapter as
`Name:Sec1|Sec2` and drops those sections, so csharp bundles `CSharpPlug`
minus `Helpers`, `Drain` and `Body` and keeps the single definition of the
rest. Duplicating them into `CSharpStdio` was the alternative and would have
been two copies nothing compares.

**AND `print-uni` HAD NO ARM IN THE WASM EMITTER AT ALL.** It is a registered
builtin (`Builtins.codex:77`, `Text -> [Console.Write] Nothing`) and
`WasmEmitter.codex` had rows for `print-text`, `print-line-uni` and
`print-line` and none for it, so a bare mention fell through builtin dispatch
into name resolution and emitted as a CLOSURE VALUE. The failure surfaced at
`wat2wasm` as `undefined local variable "$print_uni"`, thousands of lines into
generated wat, naming neither the builtin nor the chapter. That is L-ACCEPTED
one level down: an `is otherwise` absorbing an unknown instead of refusing it,
and the diagnosis cost was the whole distance between the two. The arm is one
line beside `print-text`, which has the same no-newline semantics. Nothing
that compiled before changes: this path previously produced invalid wat.
- `codex/plugs/common/build-plug-wasm.ps1` bundles the emitter against
  PlugStdio instead of the network entry and runs it through the wasm plug.
  It bundles to `plug-source-stdio.codex` rather than `plug-source.codex`,
  which `Build-TranspilerPlug` hardcodes: sharing that name would leave the
  network build's bundle looking like this one.

**No existing file changed.** `codex/plugs/javascript/build.ps1` still builds
the network CDX and both transports exist.

Two things the design turned on, both read rather than assumed. `read-file-uni`
already converts to CCE on the way in (`WasmEmitter.codex` above
`wat-rt-read-file`: "the conversion already happened here"), so `utf8-to-cce`
is unnecessary, which matters because it lives in `X86_64State.codex` and has
no wasm arm at all. And a header line was dropped: reading one needs `Just`
and `None`, the Maybe type is not in a plug bundle, and CDX2072 said so on the
first build. `read_file_uni` ignores its argument in wasm (`param $ignored`),
so the contract is simply IR on stdin, which is the shape Steve Howell's
`zigemit` already uses.

**PROVEN END TO END, with the program's own output as the oracle.** Chained in
one process the way a page would: `sample.codex` to IR through the compiler
module (23 ms, 256 MB at decks=12), IR to JavaScript through
`javascript-stdio.wasm` (**3 ms, 16 MB**, 84,197 bytes of module), and the
emitted JavaScript RUN, printing `Cobblestone` and `110` where `sum-to 10`
doubled is 110. Changing the source to `sum-to 5` moved it to `30`, so the
pipeline is live rather than answering from something canned.

The plug module wanting 16 MB against the compiler's 256 is the number that
makes a per-target lens affordable in a tab.

**csharp proven the same way**: 129,101 byte module, 5 ms, 16 MB, 11,710
characters of C# which `dotnet run` compiles (warnings only) and runs,
printing `Cobblestone` and `110`.

Left: the other transpilers are a few lines each (`plug-emit-ir-stream` plus a
build invocation). `elf`, `pe` and `img` emit BYTES rather than Text and need
a `plug-emit-bytes` sibling before they can ride this.

**1.85 -- THE SELF-COMPILE PAGE'S ANCHOR GOES RED THE MOMENT THE SEED MOVES,
BECAUSE ITS TWO ARMS ARE DIFFERENT COMPILERS** (reek, 2026-08-26).

`build-page.ps1` builds the wasm module from `build/output/Codex.codex` and
computes the anchor by running `seed/Codex.cdx` over that same source. Those
are two compilers, and the claim only holds while they agree.

Measured today: `build/output/Codex.codex` is from 08-25 20:46 and the seed
moved twice on 08-26 under merge-down (kernel digest `591EEA7B` to
`C3181693`). Rebuilding the page left the module BYTE-IDENTICAL at 1,133,290
bytes and moved the anchor from `4173E77D` to `8294D658`, 2,458,206 characters
against 2,458,210. Run against the fresh anchor the module reports **OUTPUT
DIFFERS**, and nothing about the module changed.

**The deployed page is GREEN and was left alone**: its anchor and its module
are the matched 08-26 13:04 pair and it verifies byte-identical. The trap is
that a rebuild of the page ALONE turns it red, and reads as a compiler
regression rather than as a stale concatenated source. `build/output/Codex.codex`
is produced by a gate's source-concat phase, so refreshing it means running
the gate before rebuilding the page, and the two must ship together.

**1.92 -- `plug-emit-bytes` EXISTS, AND ALL THREE BINARY PLUGS RIDE IT: elf,
pe AND img RUN AS WASM MODULES ON STDIN AND STDOUT AND EMIT BYTE-IDENTICAL
ARTIFACTS** (reek, 2026-08-27).
**[Renumbered from 1.91, which fester had taken for the tail-call walker's
`IrAct` arm in the same hour. Both were in main together; this one was
uncited, so this one moved.]**

`codex/plugs/common/PlugBytes.codex` is the sibling of `PlugStdio` for the
plugs that take a compiled payload rather than IR text, and
`codex/plugs/elf/ElfStdio.codex` is the first to ride it, reusing
`build-elf-from-payload` unchanged by bundling `ElfPlug` minus its three
transport sections. `build-plug-wasm.ps1 -Transport bytes` bundles PlugBytes
and none of the IR declaration chapters, which a bytes plug has never needed;
the default path is untouched and javascript-stdio rebuilds byte-identically
across the change.

**PROVEN AGAINST THE BARE-METAL PLUG ON THE SAME PAYLOAD.** A 175-byte
payload in the documented wire format through `codex/plugs/elf/run.ps1` (the
network plug, x86-64 under codex-vm) and through `elf-bytes.wasm` (21,906
bytes, wasmtime) produced the same 704-byte ELF, SHA-256 `67945A36..` on both
arms, opening `7F 45 4C 46`. Live rather than canned: one altered payload byte
moves the output hash, and a 3-byte payload answers `REFUSED short payload 3`
rather than faulting.

**The transport itself, measured apart from the plug.** An echo probe
(`read-file-raw` straight into `write-binary-buf`, which is exactly what
PlugBytes does) returned a 15-byte hostile pattern -- leading NUL, embedded
EOT, CR, 0xFF, 0x80 -- unchanged, and 3,158,073 bytes of random data
byte-identically in 170 ms, which is what exercises the chunked read, two
buffer growths and a single multi-megabyte write. The 15-byte fixture reaches
none of those three.

**`read-file-raw` MEANS SOMETHING WIDER ON WASM THAN ON BARE METAL**, by
Damian's ruling of 2026-08-27: a builtin means whatever it needs to mean to
make sense for its environment. x86-64 ends the read at a NUL or an EOT
because a serial ring has no end of input; wasm's stdin has one. **The
cross-target harness therefore cannot express this arm in either direction**
-- without a NUL terminator the x86 arm HANGS, and with one the two arms
disagree by exactly the width that was intended -- so no `wasm-e2e` subject
was added for it, deliberately. Its runner is the end-to-end comparison above.

**`pe` AND `img` FOLLOWED THE SAME DAY, AND THEIR PROOF IS A REAL SEED RATHER
THAN A FIXTURE**, because unlike elf both have live producers.
`codex/plugs/pe/PeStdio.codex` (33,168-byte module) and
`codex/plugs/img/ImgStdio.codex` (24,767 bytes), each against its own network
plug on the same bytes:

| arm | payload | artifact | agreed |
|---|---|---|---|
| pe mode 0, UEFI kernel | seed CDX, 2,928,117 B | 2,771,968 B PE32+ | `2628367B..` |
| pe mode 1, UEFI app, 512 heap pages | seed CDX | 2,771,968 B | `D4CB990B..` |
| pe mode 2, ARM64 wire | `arm64.wire.bin`, 83,691 B | 78,336 B | `73BDCB75..` |
| img FAT32 | PE + seed CDX, 5,700,101 B | 8,388,608 B GPT image | `05834E99..` |
| img FAT16 + embedded source | 5,701,059 B | 8,388,608 B | `935419A1..` |

**Every branch of both chapters, not just the one nearest to hand** (L-AXIS):
three PE modes and both filesystems, and the arms are discriminating rather
than agreeable -- mode 1 differs from mode 0, and FAT16 differs from FAT32, so
the mode byte and the filesystem byte are demonstrably read. The five refusal
paths answer in words on a truncated or overclaiming header rather than
faulting. The mode-2 arm needed a payload `pe/run.ps1` cannot build, so its
network side ran through a scratchpad copy taking a prebuilt payload, and that
copy was calibrated first by reproducing the mode-0 hash exactly.

`ImgStdio` hands the assembled image over with `write-binary-buf` and
materialises no list at all: the network entry streams the same buffer down a
socket, and 8 MB through a `List Integer` would be 64 MB of heap on a target
with no GC.

**What is left.** Nothing in the tree produces an ELF payload: the only
producer is `extract-x86-output.ps1`, one of the four dead harnesses of 1.41.
`pe` and `img` have live producers and are unaffected. So whoever wires
Prism's Binary tab has ELF blocked on a payload source and the other two
ready, and the payload for all three now wants to come from the compiler
module's own `write-binary` in the tab rather than from a host script.

**The output half, landed first (main 20007).** `write-binary` and
`write-binary-buf` sat in `wat-no-such-thing`, so every
call emitted `(unreachable)` and a wasm module could produce text and nothing
else. Those two builtins are how the compiler's own `opening.codex` emits a
CDX (1545-1547), so this is the whole distance between a wasm module and a
binary artifact: Prism's Binary tab as much as `elf`, `pe` and `img`.

`$write_binary` copies the list's bytes into one contiguous block and writes
once; `$write_binary_buf` writes straight out of linear memory with no copy,
which is the path a whole artifact takes. `$write_raw` reads `fd_write`'s
nwritten and loops, where every other writer here drops it: the text printer
flushes at most 240 bytes and never meets a short write, and dropping the
count on a megabyte artifact truncates it into something that reads as a
wrong artifact rather than a partial one (L-SHORT).

**Graded against x86-64, and byte-exactly rather than as text.**
`codex/plugs/wasm/test/write-binary-rt.codex` rides `wasm-e2e.ps1`, 24 of 24
with no regression. That harness compares strings, which cannot speak for the
bytes a CDX is made of, so separately: a probe writing all 256 byte values
through `write-binary-buf` produced 256 bytes on wasmtime identical to
codex-vm's capture of the same source on x86-64, NUL and 0xFF included, every
byte equal to its own index. Calibrated by sabotage -- dropping the `off` add
from `$write_binary_buf` moved exactly the subject's offset row and left the
other two unmoved. No gate weight: no script under `build/` invokes
`wasm-e2e.ps1`, so the subject costs nobody's gate run.

**1.83b -- THE CLICK ERROR IS `Failed to fetch`, AND THE OUT-OF-MEMORY
MECHANISM PUBLISHED FOR IT IN 19859 IS WITHDRAWN** (reek, 2026-08-26).

The page was reported erroring on the button. Measured that the module grows
to 1,628.8 MB, found that `codex-compiler.wat:1896` traps `unreachable` when
`memory.grow` is refused, and that `isStackDeath` matches the word
"unreachable" -- all three true, and none of them the cause. **Driven under
CDP, Chrome 151 and Edge 151 both ALLOCATE the full 1,629 MB on demand and
the page completes byte-identical in 14.8 s and 15.6 s.**

The cause is the ORIGIN. Opened from disk the page reports `status=error`,
`verdict=Failed to fetch`, in two seconds: it fetches `codex-compiler.wasm`
and `Codex.codex` from beside itself and a browser refuses a fetch on a
`file:` origin. Reproduced under CDP against
`file:///.../web/compile/index.html`, and confirmed by Damian as the message
he was seeing.

**This is L-MECHANISM's exact shape a second time, and the tell was
available the whole time: I never asked what URL was in the address bar.** A
measured 1.6 GB and a real misclassification made a complete-looking story
out of a number nobody had connected to the symptom. The falsifying test was
one CDP run.

The page now names it, before the click rather than after, and the
misclassification fix from 19859 stands on its own merits: an `unreachable`
that survives the retry still reports the memory it reached, because that
failure is real even though it was not this one.

The page now states the shape instead of implying a stream. **A real
progress stream is a compiler-side change to the front end, is nobody's
item, and nobody is asking for one** -- recorded here so it is not
re-derived, not proposed as work.

**1.82 -- THE SELF-COMPILE SURVIVES A BROWSER'S STACK: `return_call` CLOSES
1.14 FOR THIS TARGET** (fester, 2026-08-25). **[1.83 sharpens the claim:
"a browser's stack" here means wasmtime's 1 MB wasm stack; a browser
WORKER's stack is a fatter-framed resource and the emit spine's non-tail
residue crosses it -- the page's main-thread fallback and the eventual
compiler-side de-recursion are the browser-real closes.]**

1.81's self-compile needed wasmtime's 16 MB stack flag, which no browser
honors; a browser fixes its wasm stack near 1 MB. The design
(`PlugDeepRecursion.md`) classed wasm as "class 3, the host's stack, nothing
emitted source can do" -- written before weighing the tail-call proposal,
which every major engine now ships. **The emitter now issues `return_call`
for any application in tail position that saturates a KNOWN function's
arity**, which runs in the caller's frame: mutual tail recursion -- the
lexer's scan-token cycle, ping/pong -- is constant-stack, which no self-loop
can achieve. The dispatch mirrors `emit-wat-apply`: builtins (deck-record's
bracket among them), constructors and function-valued locals never reach it,
so the enter/exit balance is untouched by construction; the existing
self-call loop stays preferred for self-recursion. Every def body now routes
through the tail walker (its depth-256 bail also changed from emitting a
SILENT `(i64.const 0)` to falling back to the plain emitter -- the same
landmine still sits in `emit-wat-expr-at:746`, pre-existing, held in check
only by the fixed point).

**Measured: the compiler's own module carries 2,874 `return_call` sites and
SELF-COMPILES AT `-W max-wasm-stack=1048576` -- one browser-real megabyte --
byte-identically, same hash `B3491BE7..`, five seconds.** Suite 23 of 23
with the new arm `deep-recursion-rt` (the design's own probe at depth one
million): its `.wasmstack` sidecar pins the harness to 1 MB for that subject,
and under the shipped `#74` plug it dies `call stack exhausted` there while
x86 stays green. Graded both ways at the browser's number, not the bed's.

**Two instrument lessons from grading it** (both are why the arm is shaped
this way): at 16 MB and depth 1M the shipped plug PASSED, because a minimal
Cranelift frame is ~16 bytes and 1M of them is exactly the harness stack --
an arm at its instrument's edge, L-THRESHOLD's shape; and at depth 10M the
x86 TRUTH arm double-faulted (`!EXC=08`, CR2 on the guard), which measured
x86's own boot stack at ~64 MB and mutual budget ~1.4M frames -- the
reference target has no mutual-TCO either, its stack is just bigger. The
`.wasmstack` sidecar is what breaks the coupling between the arm's demand
and the harness default.

**What 1.14 still owns after this:** non-tail depth (`sum-to`'s shape, the
printer's `&`-spines) is a genuine frame obligation on every conventional
target; wasm now fails it at the same depths x86 does, which is parity, not
a defect. The other plugs' classes stand as the design records them.

**1.81 -- THE COMPILER COMPILES ITSELF IN WEBASSEMBLY, BYTE-IDENTICALLY TO
x86-64** (fester, 2026-08-25, in-stream during the freeze).

Its own 2,945,373-byte source, mode `TEXT decks=125`, wasmtime with
`-W max-wasm-stack=16777216`: **2,460,088 characters of emitted text,
SHA-256 `B3491BE7C39C34A7..` from the wasm module and from codex-vm running
`Sut.cdx` alike, zero diagnostics, five seconds on either target.**

**The mechanism that unlocked it is saturating closure application.** 1.80's
helper census caught `$clo_apply1` at 21.2M calls in one phase span and 8.8M
in the next: the one-argument chain allocated an intermediate closure PER
ARGUMENT (16 B then 24 B for every two-argument comparator call -- the exact
paired s16/s24 histogram signature), where x86's trampoline passes a
saturating row in registers and allocates only on genuine under-application.
The fix is a `$clo_applyN` family beside the existing `$invokeN` generators:
a bare table index applied to exactly its arity takes one `call_indirect`
and allocates NOTHING; every other shape falls back to the chain, which
stays the single place closures are built. `wat-emit-indirect` emits one
`$clo_applyN` call per saturating row, which also matches x86's
all-args-before-application evaluation order more closely than the chain
did.

**Measured, mid unit, per-phase deck against x86:** lift 4.8x to **0.05x**
(177.7 MB to 1.87 MB), resolve 2.8x to **0.93x**, lower 1.9x to 1.31x,
scope 0.89x; whole-unit total now **209.7 MB wasm against 226.8 MB x86 --
the wasm target allocates LESS deck than the reference.** Byte-identity
held at every step: the 252 KB unit (`40CE7131..`), the 652 KB padded unit,
and the self-compile above. Suite 22 of 22.

**What the claim is and is not.** This is the compiler, running as a wasm
module, compiling its own full source to TEXT byte-identically. It is not
yet the browser page: `decks=125` is just a mode line, but the 16 MB stack
is a wasmtime flag a browser will not honor, so plugs 1.14 (trampolining
the printer's recursion) is now the LAST wall between this and the
crazy-boss page. The parse 2.4x residue stands as the remaining inflation
question and no longer gates anything. **[1.93 closes it, and 2.4x was not a
constant: the ratio rises with unit size because the wasm side was quadratic
where x86 is linear. It is 1.09x on the compiler's own source now.]**

**1.80 -- THE INFLATION IS BOXED ON THREE SIDES; WHAT REMAINS IS EITHER x86
ELISION OR AN UNCOUNTED HELPER** (fester, 2026-08-25, in-stream). **[1.81
answers this entry: the uncounted helper was `$clo_apply1`, and the census
in the NEXT-run paragraph below is what found it.]**

The mid unit's deck spend, attributed by successively narrower counters (all
runs on the same module and input, phase-split at every compact):

| class | measured | share of the ~11M tiny objects in the LOWER-era span |
|---|---|---|
| `$text_append` (the x86 `inplace-accumulators` divergence) | 2,772 calls, 49 KB whole-run | **nil** -- ninth theory dead by arithmetic |
| ten named runtime helpers (`list_push`, `ll_push`, `list_cons`, ...) | peak `list_push` 671k | under 15 per cent |
| inline constant-size construction (ctors, records, closures) | ~1.8M in that span | roughly a quarter (from a wrapper run that later faulted in EMIT -- held as approximate, do not lean on it) |
| histogram truth (clean run) | s16=5.2M s24=6.4M in one span; 44.8M/1.06 GB whole-run | the denominator |

**Layouts are verified identical**: nullary ctor 8 B both targets
(`emit-nullary-ctor` bivy-allocs 8, same as `emit-wat-ctor`), records
untagged `fc*8` both, variants `8+fc*8` both, x86's `__list_cons` copies
whole lists exactly as `$list_cons` does. Also dead by reading: `sort-by` is
allocation-free in-place quicksort on both; `wat-guard-scratch` uses the
operand stack; `__record-set` mutates in place on both; deck brackets
balanced. **Eleven theories total have now died by measurement or reading in
one day, and the honest residue is precise:** x86's lower+resolve deck is
177 MB where wasm's is 428 MB on the same input, with 6,586 inline
`bump_alloc` sites across 1,812 compiled compiler functions doing the
allocating -- code x86 executes one-for-one.

**NEXT, one run and one read.** Extend the per-helper counter recipe (probe
proven non-perturbing: counters after the local declarations, dump and reset
at `$phase_compact`) to ALL ~40 runtime helpers. If they come back small,
the delta is x86 ELIDING allocations wasm performs, and the place to read is
what x86's leaf/TCO/accumulator machinery SKIPS -- `leaf-walk`,
`inplace-accumulators`' relatives, `pre-alloc-tco-temps` -- looking for
allocation sites the x86 codegen replaces with register reuse. The wrapper
split (probe13) faulted at 0xB2A28C00 in EMIT for reasons not established;
its numbers are quarantined and the technique needs its own diagnosis before
reuse.

**[1.93 ran that recipe against PARSE and the elision branch of this
paragraph is dead. Allocation COUNT and small-object BYTES are linear in
unit size on both targets and agree; x86 elides nothing. The helper the
census names is `$list_insert_at`, whose growth policy was the divergence.
The wrapper technique also works: routing a candidate's `bump_alloc` through
a size-passing wrapper attributes it without reproducing any call site's
size expression, and it did not fault.]**

**1.79 -- A 652 KB UNIT COMPILES BYTE-IDENTICALLY ONCE THE BED'S STACK
MATCHES x86's, AND THE THREE WASM FAILURE MODES ARE NOW SEPARATED** (fester,
2026-08-25, in-stream during the freeze).

**The size ladder, built two ways after truncation failed honestly** (a cut
mid-multi-page-chapter refuses CDX3004 on both targets identically; a cut at
a page boundary strands 21 names -- the tails are load-bearing): real units
at 254-355 KB, then rust padded with generated self-contained chapters to
455/560/652/837 KB. Every rung's check-deck ratio is **1.6x, flat** -- so
1.78's "nonlinear explosion at 342 KB" was never real; that reading came
from a harness that pointed wasmtime at a module file which did not exist
and read nine launch failures as nine faults (L-FALSIF, the instrument that
cannot succeed; the referee regex on the x86 side was wrong the same hour).

| rung | wasm | check-deck ratio |
|---|---|---|
| 455 KB | clean | 1.6x |
| 560 KB | clean | 1.6x |
| 652 KB | **`call stack exhausted`** in `codex-emit-expr` under `emit-streaming-ir-defs`, ALL EIGHT frontend phases already complete and healthy | 1.6x |
| 837 KB | honest `CDX9002: Deck overflow in PARSE` (x86 clean) | -- |

**652 KB: plugs 1.14, not codegen.** wasmtime's default ~512 KB call stack
exhausts inside the text printer's recursion; x86's stack envelope is
effectively unbounded here. With `-W max-wasm-stack=16777216` the same
module compiles the same input to completion: **539,793 chars, SHA-256
`45E2155946D36C21`, byte-identical to x86-64** -- 2.6x the 252 KB
high-water mark, for one bed flag. `wasm-e2e.ps1` now passes the flag (the
bed was too STINGY to express correctness, L-ARENA's inverse). The real fix
remains 1.14's: recursion depth is a property of the emitted code, and a
browser's stack is not flaggable.

**837 KB: the 1.5-2.4x deck inflation arriving as honest refusals.** PARSE's
scaled reservation crosses first at this shape. Same family as the
compiler-self SCOPE refusal; the inflation itself is still the open
question, now cleanly separated from both crashes.

**riscv-729 is NONE of the above and stands alone:** big stack changes
nothing (same out-of-bounds fault), its frontend deck crawl is real, and its
keep-walk reads clobbered boxes. One unit-specific trigger, mechanism still
open; everything measured about it is in 1.77/1.78.

**[1.94 -- IT NO LONGER REPRODUCES, AND THE MECHANISM IS UNATTRIBUTED. Do
not spend another session hunting it without first re-running the two lines
below.]** (fester, 2026-08-27.) Against seed `555791DA` and the page module
at main 20074, `codex/plugs/riscv/build-output/plug-source.codex` (730,480
bytes) compiles under wasmtime in 1.4 s with **no trap, and its output is
byte-identical to x86-64**: 605,266 cleaned chars, SHA
`5C2205FE0C31A71A..`, both targets, same terminated stdin. The larger
`arm64` unit (822,864 bytes, the biggest in the tree and past the size that
used to trap) is byte-identical too, 672,659 cleaned chars, SHA
`9C73501CE8541D8A..`. So the "a large unit traps" class is closed at the
capability rather than at one input.

**The obvious attribution is REFUTED, which is the part worth keeping.**
1.93's `list_insert_at` growth fix was the natural suspect, since it took
249.9 MB off the self-compile's deck. Ablated: `WasmEmitter.codex#43`
printed back over head, plug rebuilt, module re-emitted and re-assembled,
same riscv input -- **it compiles clean there too**, exit 0 in 1.4 s. So
1.93 is not what closed this, and publishing it as the cause would have been
a mechanism that never moved the symptom (L-MECHANISM).

**Two reasons full attribution is not cheaply recoverable, and both are
limits on the claim above rather than excuses.** The unit is a build
artifact: `build-output/` is untracked, so the exact 729,046 bytes that
trapped no longer exist anywhere and today's 730,480 is a rebuilt and
materially different input (L-SAMEVER -- these are not proven to be versions
of the same thing, and the shape that trapped may simply be absent). And the
seed has moved underneath it, so even the old bytes would meet a different
front end. Reconstructing the original experiment means an old seed AND an
old emitter AND an old unit together.

Two facts to test before believing this is anything: the trap is gone under
BOTH the current and the pre-1.93 module, and it was never reproduced from
tracked source in the first place. Anyone reopening it should regenerate the
riscv unit from the tracked plug sources of 2026-08-25 before concluding
either way.

**The instrument trap that cost two runs here, and it is not in the
harnesses:** `codex-vm -input <file>` needs the stdin image to be
TERMINATED, and a hand-built one is the only kind that is not. The two
shipped constructions use different terminators, which is why no single
byte value is the rule: `build-page.ps1` appends a zero byte
(`modeHeader.Length + srcBytes.Length + 1`, the extra element defaulting to
0) and `build/compile.ps1` appends EOT, `[char]4`, after the body. Either
terminates; neither is optional. An unterminated stdin produces a ONE-BYTE
output file holding `0x01`, which is the leading marker with nothing behind
it, and reads as the compiler dying rather than as an empty read. Wasmtime
does not care, because fd_read's zero-length return is its own terminator,
so the two targets disagree about a malformed input in the direction that
makes wasm look healthy and x86 look broken.

**1.78 -- THE TYPE GRAPH IS EXONERATED, THE EXPLOSION IS NONLINEAR IN UNIT
SIZE, AND 1.77's DIVISION WAS WRONG** (fester, 2026-08-25, in-stream during
the freeze). **[1.79 corrects this entry's nonlinearity claim: the ladder
was measured with a broken harness; the true ratio is flat 1.6x. The
population counters and balance numbers stand.]**

**x86, same unit, same counters, temporary source instrumentation (reverted):
fresh=631,997 hit=647,041 adopt=599,349.** Wasm was fresh=605,696. The
populations are the SAME, so "the wasm graph is 40x less shared" is the FOURTH
dead theory, and 1.77's "525 MB = per-visit scaffolding times population" was
a category error twice over: the mcopy walk spends the KEEP deck (after
`keep-set`, at 45 MB in the trace), while the 525 MB crawl was the CHECK deck,
spent BEFORE `keep-set` by check proper and the resolve tail. Dividing the
CHECK deck by the mcopy population predicted x86 fresh ~15k; the measurement
answered 632k. The prediction was falsifiable and it falsified.

**What the deck actually holds, histogrammed in `bump_alloc` (depth >= 1),
whole run to the fault:** 44,874,779 allocations, 1,060,781,345 bytes; 19.4M
of <=16 B and 23.0M of <=32 B carry 864 MB of it. **Enter/exit balance is
EXACT** -- 3,227,586 enters, 3,227,585 exits, depth 1 at the fault, which is
correct mid-deck-record -- so the bracket machinery is sound (fifth theory
dead). The 42M tiny-object count matches L-PEROBJECT's partial-application
population shape; UNVERIFIED as the class, named as the first suspect.

**The sharpest clue is the nonlinearity.** Same phase, same targets: the
252 KB unit runs check at 3.49 MB wasm vs 2.29 MB x86 (1.5x); the 729 KB unit
runs check at ~525 MB wasm vs 13.9 MB x86 (38x). A regime changes between
those sizes on wasm only, with the graph population proven identical. NEXT,
and it is one clean session: build the size ladder from the other plugs'
`build-output/plug-source.codex` files (real compilable units of graded
sizes), find the knee, then histogram just above and below it. A capacity or
fuel crossed only on wasm -- with identical inputs -- means a threshold
computed from something target-divergent; find WHICH threshold before reading
any more code.

**The x86 counter recipe, for whoever repeats it:** three scratch cells at
38000/38008/38016 (checked unclaimed against the Sketchbook map and the
tree), `poke-32` increments in `mcopy-type-fresh/hit/adopt`, the print
appended to `wd-marks` in `emit-text-streaming` -- a print inside
`compile-type-check` is refused by the effect system (CDX2031), and that
refusal is the system working. Cells are NOT safe on wasm (they land in the
data section); the wasm numbers come from WAT-global counters instead.

**1.77 -- `$list_push` GROWS AT THE FRONTIER LIKE x86, AND THE 729 KB TRAP IS
ONE MEASURED MECHANISM WITH THREE DEAD THEORIES BEHIND IT** (fester,
2026-08-25).

**Landed: frontier growth.** x86's `__list_snoc` "extends its argument in
place whenever that argument is the topmost allocation" (`X86_64.codex:508`,
prose that exists because compiler code DEFENDS against the aliasing);
`emit-list-push-path2` checks the live cursor AND the `deck-pos-addr` cell and
advances whichever matched. This plug's `$list_push` now does both --
`bump_alloc` continuation on the live side, an explicitly memory-grown advance
on the parked-deck side -- and falls back to copy exactly where x86 does.
Suite 22 of 22; the 252,035-byte unit stays byte-identical
(`40CE7131D1E3FDFB`, 216,246 chars both targets); total memory on the 729 KB
run falls 576 KB. `$list_insert_at` still copies on overflow where x86
frontier-grows against the live cursor only (`X86_64ListHelpers.codex:631`);
same shape, not yet ported.

**The 729 KB trap, measured end to end.** The CHECK-KEEP deck (built
`opening.codex:612`, `mc-ceiling = keep-base + keep-height - 4 MB` at 667)
consumed its ENTIRE reservation and crossed the end into live bivy scratch;
the sliding `0x039C` garbage IS the deck's own data written over every live
bivy object in the band, and the mcopy walk then read boxes the deck had just
clobbered. The bivy box at the watch was allocated depth 0 AFTER the keep
build; the clobbering 24-byte allocation was depth 2 at watch-14; the keep
build's reservation event never covered the watch, so the reservation ends
below it. **The ceiling did not hold because only the COPIES are
ceiling-checked: the walk's own scaffolding -- `mkey-types` accumulators,
`mcopy-fields` comprehension lists -- allocates deck-side unguarded and walks
the last 4 MB through the margin and past the end** (L-TAILGUARD, new site).

**Counters, patched into the module, read at first garbage:** fresh-copies
605,696; memo-hits 495,583; adopts 596,549; distinct memoized contents
**9,144**; memo table 2^24 slots, 3.6 per cent load, NOT saturated. The walk
visits 605k distinct box ADDRESSES that dedup to 9,144 contents, and the 525
MB is per-visit scaffolding times that population.

**Three theories measured dead, so nobody re-walks them:** (1) frontier
growth as the cause -- the fix landed above and moved neither the fault nor
the counters (605,095 pre-fix vs 605,696 post, identical within noise); (2)
`text-plug` inlining dissolving `deck-record` brackets -- the pipeline is
`["fold-constants"]` only, and the module carries 1,437 brackets against
1,392 source sites; (3) clobber-then-reclaim via the post-compact
equal-cursors window -- the boxes are check-era, allocated after the keep
build, not parse-era relics.

**NEXT, two independent halves.** (a) Measure x86's fresh-count/keep usage
for the same unit before assuming 605k is divergent -- if x86 walks the same
population, the whole defect is the margin, and the fix is to ceiling-check
the scaffolding or fatten the margin; if x86's population is far smaller,
find what breaks address-sharing in the wasm graph upstream of CHECK. (b)
Either way, the scaffolding allocations inside the mcopy/mkey walk want the
same ceiling the copies honor -- an unguarded allocator inside a guarded
phase is the standing hazard, compiler-side, token when touched.

**Map a backtrace in one step:** count `(func $` in the WAT in order, subtract
the import count, index in. That turned bare indices into
`$mode_ordinal` / `$mkey_type` / `$mcopy_type_fresh` / `$mcopy_type_memo` /
`$mcopy_type` / `$copy_expr_types_deep` / `$map_list` immediately.

**1.75 -- THE WASM TARGET HAS A DECK, AND THE SELF-COMPILE NOW HANDS MEMORY
BACK** (fester, 2026-08-25). The handoff scoped this as two independent bump
regions and a linear-memory layout question. It is neither.
`ArchitectsSketchbook.md` "Deck-Bound Mode" and `PhaseAllocator.codex` agree:
the deck is ONE cursor swapping between two saved positions, its window carved
out of the same bump region by `build`, so the whole change is a `$deck_ptr`
global, a saved bivy cursor and a depth counter.

**`deck-record` had no arm in this plug at all**, and that is the half nothing
in the six-primitive table named. Every other backend intercepts it as an
intrinsic bracketing its argument with enter/exit; wasm let it fall through to
the identity function it is in source, so nothing ever allocated into the deck.
Landing a real `__deck-pos` WITHOUT it would have made `phase-compact` rewind
over live AST -- silent corruption rather than a refusal. The compiler's own
module carries **1,437** of those brackets now and carried none before.

| primitive | was | is |
|---|---|---|
| `__heap-advance n` | `drop` | bumps `$heap_ptr`, so a reservation reserves |
| `__deck-set p` | `drop` | sets `$deck_ptr` |
| `__deck-pos` | aliased to `$heap_ptr`, making `phase-compact` a self-assignment | reads `$deck_ptr` |
| `__deck-enter` / `__deck-exit` | `(i64.const 0)` | the R10 swap, nesting-counted |
| `__deck-alloc` | absent | enter, bump, exit |
| `deck-record` | **absent**, fell through to identity | brackets its argument |

**Measured on the compiler's own 2,945,374-byte source, seed 5206C6FE59340831.**
The `decks=` knob is a PERCENTAGE of the shipping reservation, not a budget, so
the honest arm is the default -- which is what x86 runs at:

| phase | before, `decks=400` | after, default | x86-64 |
|---|---|---|---|
| h1-tokenize | 136,376,368 | 281,298,597 | 277,357,332 |
| h2-scan | 183,575,262 | 1,206,197,894 | 1,193,937,940 |
| h4-parse | 617,052,916 | 1,377,816,869 | 1,315,046,484 |
| h5-desugar | 740,072,544 | **89,357,943** | **87,938,516** |
| h6-scope | 747,252,930 | 205,232,517 | 207,948,976 |

Before, the number only ever climbed. It now FALLS at the desugar boundary and
tracks x86 within a few per cent at every phase. That fall is the whole
finding; nothing else in the run is evidence of reclamation.

**Two things remain, and both are bounded.**

`CDX9002: Deck overflow in SCOPE` at the default scale, where x86 compiles the
same source clean. **Which of the two it is has NOT been measured, and the
phase trace cannot answer it.** `scope-ov` compares `scope-end - scope-origin`
against `scope-deck-height`, both read off `__deck-pos`; the `WD:PHASE` numbers
above are `__heap-save` marks, so they speak to total allocation and say
nothing about the deck delta. Print `scope-origin`, `scope-end` and
`scope-deck-height` on both targets before scoping anything: patching a
`$wasi_print_i64` into the emitted artifact is what settled every question on
this target so far.

**THAT PARAGRAPH SAID SCALES ABOVE 100 WERE AN i32 ADDRESS-SPACE LIMIT AND IT
WAS WRONG IN EVERY PART. See 1.76, which is the defect it was describing.**
The trap was at 437 MB, not near 4 GiB, and nothing wrapped. The symptom was
read as an overflow and the fault address was never looked at, which is the
one line the message hands you for free.

**The arm is `deck-reclaim-rt` and it is graded both ways.** Under the pre-fix
plug exactly two of its ten rows go red -- `compact lowered the mark` and
`compact landed on the deck` -- and the other eight, `kept survives reuse`
included, are identical. That is why twenty subjects passed over this
(L-CONSTRUCT): every reading is a COMPARISON rather than an address, so the two
targets can be graded against each other at all. Module cost 9,636,669 chars of
WAT to 9,697,118, +0.63 per cent, still assembling clean.

**1.74 -- THE COMPILER COMPILES A PROGRAM IN WEBASSEMBLY, AND ITS OUTPUT IS
BYTE-IDENTICAL TO x86-64** (fester, 2026-08-26).

Same kernel source, same input bytes, two targets. `TEXT` mode on a two
definition chapter:

```
Chapter: Hi

double : Integer -> Integer
double (n) =
  n + n

opening : Integer
opening =
  double 21
```

102 chars, SHA-256 `3BE25DB23FABAB108D1CAF31B5A131DC5B45379D3D511CD57076635
70F709CF4` from the wasm module under wasmtime and from `codex-vm` running
`build/output/Sut.cdx` on the identical raw stdin. The wasm run is 0.26 s and
carries a full phase trace to `h7-resolve` with per-phase deck metrics.

**IT HAS NOT COMPILED ITSELF. THE TARGET NOW RECLAIMS, AND THE WALL MOVED
FROM MEMORY TO ONE PHASE'S CEILING** (fester, 1.75 below). Do not say the
compiler builds itself in a browser.

**Nine defects stood between 1.71 and this, and each one hid the next.** Every
fix has an arm in `codex/plugs/wasm/test/` graded against x86-64 both ways.

| # | defect | arm |
|---|---|---|
| 1 | `phase-compact` is `__heap-restore (__deck-pos)`, and `__deck-pos` was the constant 0, so **every phase boundary set `heap_ptr` to zero** and reallocated over the data section. That is what the 922 MB of string-table stdout was. | (module-level) |
| 2 | `__heap-advance` moved the single allocation cursor past each reservation, so a phase's own base was already above its ceiling. On a one-region target a reservation is a BUDGET, not a window. | (module-level) |
| 3 | `emit-wat-record-fields` took the store offset from the field's POSITION IN THE CONSTRUCTION rather than its declared index, so any record written out of declared order was scrambled. | `field-order-rt` |
| 4 | `wat-emit-record-set` resolved the slot by NAME across every typedef, computing `rec-ty` and never using it. `ParseResult.parse-bag` is slot 4 and `Document.parse-bag` is slot 14, so setting a Document's bag wrote `Document.instance-defs`. | `record-set-slot-rt` |
| 5 | `emit-wat-field-access` and `emit-wat-field-store` had the same name-only lookup. AChapter and Document share TWELVE field names, shifted by one because AChapter leads with `name`. | (same arm) |
| 6 | `emit-wat-name` consulted the arity table before locals, so a parameter sharing a name with a top-level definition became that definition's funcref index. `copy-as-chapter-guarded (ch)` read its fields off table index 3440, the three-argument `ch`. | `local-shadows-global-rt` |
| 7 | `IrAppendList` and `IrConsList` had no emitter arm at all, so `&` on lists and `::` fell through to **integer addition of the two pointers**. | `list-append-rt` |
| 8 | `list-set-at` was emitted as a COPY. It is an in-place mutator: `splice-new-node` discards both results and returns the list unchanged, so the skip list's links are the side effect and nothing else. Every insert bumped `size` and linked nothing, leaving name resolution with a 266-name scope it could not search. | `list-set-at-rt` |
| 9 | `text-compare` was emitted as `$text_eq`, returning 1/0 where an ordering is required, so every skip-list search missed a key that was present. | `text-compare-rt` |

**The compiler-side halves.** `emit-ir-cce` now runs RESOLVE before LIFT, so
the wire carries resolved types and a plug can resolve a slot from the record
rather than guessing by name; it runs after `lower-end` is read, because a
phase allocating on the previous reservation is charged to it (L-TAILGUARD,
learned the hard way when it first went in before the measurement and turned
the gate red with `CDX9002` in LOWER). And `pmap-selftest-bag True` moved out
of the shared frontend into `compile-frontend-cdx`: it is an x86 pointer-map
self-test reached through `__self-type-defs`, which this target refuses
honestly, and the frontend was running it for every target.

**What the arms are worth.** `wide-record-rt` passed before and after and
proved nothing; the shapes that caught these were the ones the corpus never
built (L-CONSTRUCT). Note also that the suite defaulted to grading against
`seed/Codex.cdx` rather than the kernel under test, so 13 of 13 green said
nothing about the lifted wire until `-Kernel` was threaded (L-SAMEVER).

**1.71 -- THE TRAP WAS NOT A WASM DEFECT. EVERY PLUG HAS BEEN FED UNLIFTED
LAMBDAS SINCE IR-CCE EXISTED** (fester, 2026-08-26).

`opening.codex` has two frontends. `compile-frontend-cdx` runs LOWER,
RESOLVE and LIFT and hands back `cdx-ir`; `compile-frontend-passes` runs
LOWER and the pass pipeline, stops, and sets `cdx-ir = blank-ir`.
`emit-ir-cce` calls the second. So the IR-CCE wire, which is the only thing
any plug ever reads, carries `IrLambda` nodes that the CDX path never emits.
x86 never sees one because it lifts in-compiler.

`WasmEmitter.codex:758` then emits a value-position `IrLambda` as its BODY
ALONE, hoisting the lambda's parameters into the enclosing function as
uninitialised locals. Read straight off the emitted WAT, `$builtins`
declared `(local $s i64) (local $a i64)` and contained

```
(local.set $_tv (call $emit_negate_builtin (local.get $s) (local.get $a)))
```

so `builtins` really did call the x86 register allocator with `s = 0` and
`a = 0`, and `emit-negate st (list-at args 0)` walked a null list off the
end of memory. That is the whole of 1.70's out-of-bounds read.

**Diagnosed by patching the artifact, not by reasoning.** All 105 eta-shaped
sites in the 9.5 MB WAT were rewritten by hand to funcref indices taken from
the module's own `elem` segment, reassembled, and run: the trap MOVED to
`builtins <- emit_helper_call_1`, the next lambda shape along. A mechanism
that only explains the symptom is not its cause until the fix moves it.

**The fix is four lines in `emit-ir-cce` and it reuses the pass that already
exists.** `codex/compiler/IR/LambdaLifting.codex` is a complete general
lifter; the IR-CCE path simply never ran it. Lifting `fe.ir` before
`ir-prune-unreachable-roots` fixes the wire for every plug at once, and
writing a second lifter inside this plug would have been L-READ's failure.

**Measured on the compiler's own module**, seed kernel `55F8817BE3AD15FA`:

| | before | after |
|---|---|---|
| IR-CCE | 16,316,626 | 16,380,904 (+0.39%) |
| WAT | 9,568,192 | 9,607,759 (+0.41%) |
| funcref table | 5,139 | 5,473 (+334 defs, none removed) |
| `$builtins` inlined `emit_*_builtin` calls | 113 | **0** |
| `$builtins` funcref indices | 1 | **153** |
| behaviour | trap at 0.06 s | runs 21 s, exit 0 |

153 is exactly the lambda count in `Types/Builtins.codex` (106 bare eta, 38
with a trailing literal, 9 using only the first parameter). The def count
rising rather than falling is what rules out L-CAPABILITY-LOST on a
`$builtins` body that got shorter.

**WHAT IT STILL DOES NOT DO, and this is the next action.** The module does
not compile anything. It reports `CDX9002: Deck overflow in PARSE-KEEP` on a
target with no deck at all (`emit-wat-name` maps `__deck-pos` to a constant
0), then writes **922,862,607 bytes** of stdout, sane for two lines and then
the string table walked as though a Text carried a corrupt length. Both are
new symptoms only because the old module trapped before reaching them.
Neither is bisected. Do not say the compiler runs in a browser.

**1.70 -- the compiler's module no longer spins and now TRAPS, fast**
(fester, 2026-08-25). With 1.69 in, empty source, `Chapter: Hi` and a hello
program all fail in 0.1 to 0.3 s instead of running forever:

```
fc_keep_not_reg <- fc_evict_reg <- alloc_temp <- emit_negate
  <- emit_negate_builtin <- builtins <- builtin_names <- compile_parse
  <- compile_checked <- ... <- opening
memory fault at wasm address 0x32000010 in linear memory of size 0x24aa0000
```

**THAT READING WAS WRONG AND IT AIMED THE NEXT STEP AT THE WRONG PLACE.
CLOSED BY 1.71.** This row said the chain "cannot be real" and sent the next
session to the funcref path. The chain is entirely real: `builtin-names`
calls `builtins` (`NameResolver.codex:47`), and `builtins` builds a list of
`BuiltinSpec` records each holding a lambda, `bs-emit = Just (\s a ->
emit-negate-builtin s a)`. One grep of the two names in the chain settles it,
which is exactly what L-MECHANISM asks for and exactly what was skipped.

**1.67 -- the compiler's module READS ITS SOURCE and does not finish. CLOSED
BY 1.69: the cause was the scratch-local clobber, and it was neither of
1.68's defects** (fester, 2026-08-25). This is the state after 1.65's real fix and the growing
allocator, and it is progress with a ceiling moved rather than removed.

**Re-measured 2026-08-25 with the 1.68 fixes in the module**: `wasmtime -W
timeout=300s` on the same 98-byte `TEXT` mode line plus hello program, same
named backtrace, `advance <- skip_to_next_line <- scan_top_level <-
scan_document <- compile_parse <- compile_checked <- compile_frontend_passes
<- compile_frontend <- emit_text_streaming <- compile_plain`. The stdin is
PLAIN UTF-8, `"TEXT\n"` then the source with no terminator, because the
compiler's `opening` reads `read-line` (raw) and does its own
`utf8-to-cce`; the CCE mode line the plug's own `run.ps1` builds is for
`WasmPlug`, which reads `read-line-cce`, and feeding that here answers
`Codex: no input mode on stdin`.

**What is MEASURED.** Module 9,350,041 chars of WAT, `wat2wasm` exit 0 and
zero errors. Fed a mode line, a 99-byte Codex program and a NUL on stdin, it
runs 10 minutes, produces ZERO bytes of stdout, does not trap and does not
exit. Before the allocator grew, the same input trapped out of bounds nine
frames deep at wasm address `0xc4bac22` against a 16 MB memory. So the
allocator moved it from "stops at 16 MB" to "does not stop".

**INSTRUMENTED AT THE HOST SIDE OF THE IMPORT BOUNDARY (red's direction,
2026-08-25), and the states separate.** The instrument is a Node host that
supplies `fd_write` and `fd_read` itself and counts calls and bytes, with
the guest on a WORKER thread because `_start` blocks its own thread and a
same-thread sampler could only ever report after the thing in question
finished. **Validated first on a module whose behaviour was already known**
(`read-line-rt`: 8 writes / 64 bytes, 22 reads / 21 bytes, exact expected
output), so it is capable of showing progress and completion rather than
only silence.

| input | rd calls | rd bytes | wr calls | wr bytes | after |
|---|---|---|---|---|---|
| valid 99-byte program | 99 | 99 | **0** | **0** | 90 s, still running |
| source that must be REFUSED | 100 | 100 | **0** | **0** | 100 s, still running |

**x86-64 compiles the same source in 1.22 s** (TEXT mode; its exit 4 is that
mode emitting no binary, not a failure). That is the expectation, set before
calling anything a hang.

**Three states are now eliminated rather than argued about.** It is not
slow-with-buffered-output: `fd_write` is the only output path and it was
never called, so there is no buffer holding anything. It is not looping on
input or starved of it: the read counts match the input structure EXACTLY,
5 calls to the newline at index 4 and 94 more to the NUL, 99 of 99, which
also proves the returned text carries the right length and rules out a
bogus length field making downstream loops run forever. And it is not the
SUCCESS path: source that must be refused stalls identically, so the stall
is before the compiler can tell good source from bad and before any
diagnostic could be emitted.

**A LIMIT OF THE INSTRUMENT, recorded so its output is not over-read.** The
`mem=16777216` it prints is NOT evidence that memory never grew. Memory size
is sampled only inside `fd_write`/`fd_read`, and the guest stopped crossing
the boundary, so that figure is frozen at the last read rather than live.
Sampling it properly needs the module to take its memory as an IMPORT
instead of declaring one, which is a real change to the emitter.

**The OS supplies the channel the instrument could not** (L-CHANNEL: it is
independent of both the guest and the counters). Soaked 24m49s: **1,472 s of
CPU over 1,489 s of wall clock, so ~99 per cent of one core, and a working
set that stayed at 57.4 MB.** So it is SPINNING, not blocked and not
progressing slowly through bounded work, and it is not allocating while it
does so.

**TWO RUNS OF THE SAME INPUT DISAGREE ABOUT ALLOCATION, and that is recorded
rather than smoothed over.** Before the allocator grew, this input drove an
out-of-bounds access at `0xc4bac22`, which is 206 MB. After it grew, the
same input on the same module plus that one change spins with a 57 MB
working set and never approaches 206 MB. Those cannot both be a heap
legitimately bumped to 206 MB. **The likelier reading is that `0xc4bac22`
was a WILD address rather than a bumped heap pointer**, which would make the
growing allocator a correct change that fixed a different thing than the
trap it silenced. Untested. Whoever traces this should settle it early,
because "we ran out of 16 MB" is the comfortable story and the numbers do
not support it.

**A FOURTH STATE ELIMINATED: it is not a read loop treating end-of-input as
"try again"** (red proposed it 2026-08-25 as the cheap check before tracing,
on the grounds that a ~99 per cent spin with flat memory and zero writes,
identical on refusable source, has exactly that shape). **The counters
already refused it and a direct test confirms.** A read-again loop predicts
`rd_calls` climbing without bound; measured, it froze at the input size and
stayed there for 90 seconds. Fed input with NO TERMINATOR at all, the module
made 17 read calls for 16 bytes -- one EOF probe returning zero -- and then
stopped, where a retry loop would have gone 18, 19, 20.

So the two EOF conventions differ in MECHANISM and agree in OUTCOME. On
x86-64 `__bare_metal_read_serial` waits on the serial ring and learns it is
finished from an explicit `stdin-eof-flag-addr` set off a port status check;
here `fd_read` returning zero bytes makes `$read_byte` answer -1 and the
readers stop. **The wasm side is proven terminating by that 17th call, and
the spin is downstream of I/O entirely, in pure computation.** Worth knowing
for the bare-metal side though: without that flag ever being set, x86's
helper waits forever, so the shape red described is real on the OTHER arm.

**1.68 -- DONE 2026-08-25 (fester). Two defects in this plug, both fixed and
graded against x86-64. THE SPIN IS NOT ONE OF THEM, and this row said it
was.**

**Defect A: `==` on a constructor value compared POINTERS where the oracle
compares STRUCTURALLY.** `emit-wat-binary`'s `IrEq` arm special-cased Text
and otherwise emitted `i64.eq` on the raw values, so two separately
allocated `Box 7` blocks never matched. The fix generates one
`$cx_eq_<Type>` function per variant typedef -- tag compare, then per-tag
field compare by the field's declared type -- and points `IrEq`/`IrNotEq` at
it when the operand type names a variant.

**Defect B: `show` on a Boolean rendered the raw integer.** `show True` gave
`1` where bare metal gives `True`. `wat-emit-show` now has a `BooleanTy` arm
calling `$bool_to_text`. The literal bytes are read off `"True"` and
`"False"` with `char-code`, the way `wat-escape-data` fills the string
table: a transcribed ASCII `84` for `T` assembles and runs and prints
`&онá`, because this plug's Text is CCE and `$wasi_print_text` decodes
through the CCE tables.

**THE CORRECTION, because it was published in CL 19476 and it is wrong.**
This row said every `kind == SomeCtor` in the parser is false on this target
and that this is "exactly what makes `skip-to-next-line` spin".
`skip-to-next-line` (`Parser.codex:1370`) contains no `==` at all; it is a
`when` over `current-kind`, and `is-done` beside it is another. `when`
matching was correct on both arms before this fix and the row said so two
paragraphs later, which is the contradiction nobody read. Measured after the
fix landed: the compiler's own module still spins, `-W timeout=300s`, with
the SAME named backtrace `advance <- skip_to_next_line <- scan_top_level`.
The two defects were real and are fixed; the spin is still open and its
cause is still unknown. **A mechanism that explains a symptom is not the
symptom's cause until the fix moves it.**

**What the fix is graded on.** `codex/plugs/wasm/test/ctor-eq-rt.codex`, 13
rows, all agreeing with x86-64, where the same subject before the fix got
all six of the original table wrong. The whole `test/` suite is 12 of 12
against seed E0347775.

**Two measurements taken while fixing this, both worth not rediscovering.**

A field declared at a TYPE PARAMETER is compared by POINTER on x86-64 too:
`Held "hi" == Held "hi"` over `Holder a = | Empty | Held (a)` is **False**
on bare metal, because `subst-field-type` has no argument to substitute and
the compare falls to the integer path. This plug follows the same rule and
can still disagree on the ANSWER, because it interns equal Text literals
into one data segment offset, so the two pointers are equal and it says
True. Concrete fields agree: `Both 1 "a" == Both 1 "a"` is True on both,
`Both 1 "a" == Both 1 "b"` False on both.

**`==` on a RECURSIVE variant crashes the x86-64 compiler.** `Wrap Leaf ==
Wrap Leaf` over `Nest = | Leaf | Wrap (Nest)` dies in `alloc-temp+0xAF` with
an invalid opcode; the same type with no `==` compiles clean, which is the
control. `emit-sum-full-eq` inlines the field compare through `emit-eq-op`
and nothing bounds the recursion. This target emits a self-call and has no
such bound, so there is no oracle to grade that shape against. Filed for the
compiler in `codex/compiler/compiler-backlog.md`.

**The table the fix was aimed at, and now passes**, `x86-64` on the left of
each pair and this plug's answer BEFORE the fix on the right: `show True`
`True`/`1`, `1 == 1` `True`/`1`, `Dot == Dot` `True`/`0`, `Box 7 == Box 7`
`True`/`0`, `Box 7 == Box 9` `False`/`0`, `Dot == Box 7` `False`/`0`. So the
oracle's `==` on a variant is STRUCTURAL, tag AND fields, and every one of
those rows now agrees. **`when` matching was correct on both arms
throughout**, which is why the defect survived every subject before this one.

**A CORRECTION TO THIS ROW'S OWN EARLIER READING, because it was published
and was wrong.** It said two runs of the same input disagreed about
allocation and that `0xc4bac22` was therefore likely a WILD address. The
allocator is fine and the disagreement has a plain explanation: before the
grow, the run died during allocation-heavy setup at 206 MB; after the grow
that setup SUCCEEDS and the program reaches the scanner, which spins without
allocating, so the working set stays at 57 MB. Different distances travelled,
not disagreeing measurements. Empty source separately grew the memory to
587 MB before faulting, which is direct evidence the allocator grows.

**The `-W timeout=Ns` flag on wasmtime prints a NAMED BACKTRACE at the
moment it fires, and that is the phase tracing this row was about to build
by hand.** With `wat2wasm --debug-names` the frames carry real function
names. It cost one command and replaced a planned emitter change:

```
current_kind <- is_done <- skip_to_next_line <- scan_top_level
  <- scan_document <- compile_parse <- ... <- opening
```

**What is a HYPOTHESIS and has not been tested.** The compiler's own deck
and fuel guards are STUBBED INERT on this target: `emit-wat-name` answers
`__deck-pos` with 0 and makes `__deck-enter` and `__deck-exit` no-ops, so
`check-deck-overflow` measures against a bogus zero and a phase that raises
CDX9002 on bare metal has nothing here to raise it. **It is a guess with a
mechanism, not a finding.**

**THE FUEL HYPOTHESIS IS DISPOSED OF, and by measurement rather than by
argument.** It was struck out once on 1.68's mechanism, which was wrong, and
would have come back. 1.69 found the real cause in the emitter and the spin
is gone with the deck and fuel stubs untouched, so the stubs were never it.
They remain a real gap for the phase guards; they are not this.

**And the obvious way to test it is already ruled out in this file's own
prose.** Pointing `__deck-pos` at `$heap_ptr` to make one guard real "is
wrong twice over: the heap position is not a deck position, and comparing it
against a ceiling computed from `build` would raise overflow diagnostics for
a region that was never allocated" (`WasmEmitter.codex`, above
`emit-wat-name`). That experiment manufactures false CDX9002s and settles
nothing. **Testing the fuel hypothesis needs a different lever than the one
nearest to hand**, and the honest next step is tracing: an import the
emitter calls at phase boundaries, so the host can see which phase is
entered and never left. That is a real piece of work, not a probe.

**A second candidate worth eliminating in the same run, and cheaper:**
`$read_byte` issues one `fd_read` per BYTE. At 99 bytes that is nothing,
which is why it cannot explain this run, but at the compiler's own 2.94 MB
it is 2.9 million host calls and would need a buffer before anybody feeds
the module a real workload.

**1.65 -- DONE 2026-08-25 (fester), and it needed NO COMPILER CHANGE.**
Red routed the stream arm here and reading the driver cancelled it.
**`read-file-uni` READS THE WIRE.** The name says file and its effect row
says `FileSystem.Read`, but on x86-64 it compiles to
`__bare_metal_read_serial` (`X86_64Builtins.codex:768`), which slurps the
serial stream: terminate on NUL or EOT, skip CR, convert bytes under 128
through the unicode-to-CCE table, pass the rest. That is why `compile.ps1`
writes the mode line and the WHOLE SOURCE BODY into one input file, and why
`dispatch-on-mode`'s `utf8-to-cce` afterwards is a no-op on ASCII: the
conversion already happened in the read.

So there was never a missing stream path in the compiler. There was a plug
refusing a builtin whose bare-metal implementation is the stream read the
plug already had. **No compiler change, no build token, no new mode word,
and no exposure to the absorbing dispatch that L-ACCEPTED warns about,
because no arm is added to it.** The else-filename absorb is still a real
defect and still wants its own compiler-backlog row; nothing in this quire
blocks on it.

 The old 1.65 read: the compiler's module traps at `read-file-uni`, which is
 where `read-line` used to be, and the browser has no filesystem so this one
 has no WASI answer the way `fd_read` did. Both sentences were true and the
 conclusion drawn from them was wrong, which is why the row is kept: the
 second sentence is about a FILESYSTEM the builtin never touches. `dispatch-on-mode`
loads the source by NAME, and this target has no filesystem; the browser has
none either, so this one does not have a WASI answer the way `fd_read` did.

**THE DRIVER IS READ AND THE ANSWER IS NO: THERE IS NO STREAM PATH**
(fester, 2026-08-25, red asked before anything was built). The whole of
source acquisition is four lines of `codex/compiler/opening.codex`.
`opening` (2162) reads ONE line, the mode line. `dispatch-on-mode` (2147)
takes the first space-separated word as `cmd` (`parse-mode-cmd`, 1738), and
then there are exactly two arms: `cmd == "DISK"` goes to `emit-from-disk`,
and **everything else** goes to `read-file-uni mode` (2152). A file or a
block device. Nothing reads the input stream.

**But the primitive exists and is exercised, so a stream-source mode is
wiring rather than invention.** `read-serial-cce` is a real builtin with an
x86-64 emitter, and it is how FOURTEEN plugs take their whole input off the
wire, `WasmPlug.codex` among them. Inside the compiler it appears only in
`Builtins.codex` and the two x86 emitter files -- the compiler knows how to
EMIT it and its own driver never calls it. So the cheaper answer to 1.65 is
a stream-source arm in `dispatch-on-mode`, and a filesystem shim is the
expensive one. A page can concatenate chapters and push them at the module.

**Two things that decide who does it and how.**

`dispatch-on-mode` is COMPILER source, so this is a seed-affecting change in
another lane's file, not plug work. It wants the build token. **That is the
part worth knowing before it is scheduled: 1.65's cheap answer is not in
this quire at all.**

And red's L-ACCEPTED warning lands, on a site one level up from the one that
lesson measured. **`dispatch-on-mode`'s own shape is the absorbing kind:**
everything that is not `"DISK"` falls into the `read-file-uni` arm, so a
mistyped mode word is not refused, it is treated as a FILENAME and comes
back as a file error. A new arm must sit BEFORE that fallthrough, and the
honest version of this change also makes the fallthrough refuse an unknown
cmd instead of guessing it is a path. That is a second, separate site from
`compile-plain`'s output-format dispatch, which is the one L-ACCEPTED
actually measured; both absorb, and fixing one does not touch the other.

Both traps were identified by matching the backtrace address to a function
and then naming that function by the data offset its body loads. **Index
arithmetic over the WAT disagrees with wabt's numbering** -- by two before
this item and by three after it, since each runtime helper added shifts it
-- **and would have named the wrong function both times.**

**42 functions in the module carry a refusal stub**, and the distribution
says which ones matter: `block-read-sector` 24, `__self-type-defs` 5,
`block-write-sector` 3, `port-out-byte` 2, **`read-line` 2**,
`write-binary` 2, and one each for `read-file-uni`, `process-get-scope`,
`prof-start` and a block-device probe. Only the input ones sit on the entry
path; the disk ones are reachable code the compiler does not run when it is
reading a program off a wire.

**So the boundary has moved but it has not vanished: emitting, assembling,
starting and RUNNING A PROGRAM are four claims, and the module now clears
the first three.** It cannot yet read a byte. Feeding it its own source
needs `fd_read` imported and wired to `read-line`, which is the next
capability and the one the crazy-boss page actually blocks on.

**RULED: ONE IMPORT SURFACE, `wasi_snapshot_preview1.fd_read`, satisfied by
both hosts** (red asked the question 2026-08-25, since the page's host is a
browser with no WASI; the tree already answers it). This is not a
preference. The module ALREADY imports `wasi_snapshot_preview1.fd_write`,
and `browser-shim.html:123` already implements that import in fifteen lines
of JS against `mem.buffer`. A browser satisfying a WASI-shaped import is
therefore the existing, working arrangement here rather than a hope. Taking
a custom `env.*` import for input instead would make the module's OUTPUT
path WASI and its INPUT path something else, so the page would implement two
conventions and wasmtime would need a shim for the second one, which is the
outcome the question was asked to avoid. The browser shim gains an
`fd_read` beside its `fd_write`; wasmtime needs nothing.

**One constraint found while designing it, because it decides where the
code lives.** `read-line` answers `Maybe Text`, and a constructor here is
`[i64 tag][i64 fields...]` whose tag comes from the type-definition order.
A fixed runtime string cannot know that number, so `read-line` cannot be a
pure runtime helper: the byte loop belongs in the runtime, and the `Just` /
`None` wrapping belongs at the emit site where `ctx.type-defs` is in reach.
`Nothing` at `emit-wat-name` is the unit value and is unrelated to `None`,
which is a real constructor; conflating them would return 0 for a successful
read of an empty line.

**1.60 -- the wasm plug needs runtime data-structure builtins before the
compiler's own module assembles** (fester, 2026-08-24/25, Damian-directed
into this lane; wasm is a first-class target for the Cobblestone push).
Higher-order calls and the scalar builtins are DONE. The linked list,
`text-concat-list`, `__list-with-capacity`, `list-insert-at` and the three
`__buf-*` names closed 2026-08-25, and `text-to-double-bits` and
`raw-bytes-to-text` with them. **1.60 IS CLOSED, and the census run below
confirms it from the other end: zero undefined names in the compiler's own
module.** What stands between that module and assembling is 1.63, partial
application, which is not a builtin at all.

**`raw-bytes-to-text` DONE 2026-08-25 (fester), unblocked by 1.61.** It is
the byte copy it always looked like: allocate `count + 4`, store the count,
copy the low byte of each element. **It mirrors the PLUG's own `$list_at`,
not x86-64's helper, and the difference is load-bearing**: x86's
`__raw_bytes_to_text` reaches its elements through `emit-list-eff-base`,
which follows an indirect list VIEW when the word below the pointer is
negative, and this plug's lists have no view form at all. Ported
instruction-for-instruction it would read the wrong memory.

Graded by `codex/plugs/wasm/test/raw-bytes-rt.codex`, six rows, and the
first of them is the case this item was named for: `[72, 105, 33]` prints
`"óv` on BOTH arms now, which is the string this register recorded from
bare metal before the plug could produce it. `[20, 17]` prints `hi`,
because 20 and 17 are the CCE code units for those letters. **Sabotaging
the element stride from 8 to 4 moves only the two rows that read more than
one element**; the length, first-code, empty and truncation rows are blind
to it, so a subject built from single-element lists would have passed over
the defect.

**`text-to-double-bits` DONE 2026-08-25 (fester).** `$text_to_double` is a
port of x86-64's `__text_to_double` (`X86_64TextHelpers.codex:498`) rather
than a better parser, deliberately: the same digit accumulator in an i64,
the same one division by a `10^k` built by repeated multiplication, so the
two round identically. It inherits that helper's two documented limits,
which are properties of the reference and not of this port: a numerator
above 2^53 has already lost precision before scaling, and beyond k of 22
the divisor is itself inexact. No exponent syntax, because the reference
parses none.

Graded by `codex/plugs/wasm/test/double-parse-rt.codex`, nine rows.
**The bits were checked against a THIRD implementation, not just against
x86-64**: all eight non-empty values match `System.Double`'s own parse
bit-for-bit, including `2.718281828459045` at sixteen significant digits
and `0.001`. Two arms agreeing cannot tell you which one is right.
Sabotaging the fractional-digit counter moves exactly the five rows
carrying a fraction and leaves the four integer rows unmoved, so those four
are a live control rather than filler.

**1.63 -- the wasm plug emitted a partial application as an under-applied
direct call. DONE 2026-08-25 (fester). THE COMPILER'S OWN MODULE NOW
ASSEMBLES.** `wat2wasm` exits 0 with zero errors over 9,342,390 chars of
WAT and produces a 1,088,428-byte module. Nothing was hiding behind the
class: it was the last one `wat2wasm` could see.

**A function value stays a bare table index while nothing is captured**, so
the higher-order path 1.60 built keeps its shape and its cost, **and becomes
a heap block the moment an application leaves it short.** Bit 62 tells them
apart: a table index never sets it and a heap pointer is under 2^32, so the
tag is free and cannot collide. The block is
`[i32 index][i32 captured count][i64 args...]`, and the arity comes from a
sidecar byte table emitted beside the `elem` segment. **That sidecar is the
part that is easy to leave out and cannot be:** without it the runtime
cannot tell a saturating application from a short one when all it holds is
a bare index. Applying a value now goes one argument at a time through
`$clo_apply1`; the old arm emitted a single `call_indirect` over the whole
argument list, which is right only when the application saturates, and the
runtime is exactly the place that cannot know.

A name whose arity the map knows still takes the direct call when the
application saturates, which is the ordinary case and the hot one. Short of
that it builds a closure; past it, the saturating prefix is called and the
surplus applied to the result.

Graded by `codex/plugs/wasm/test/closure-apply-rt.codex`, which is blu's
`codex/test/ops/closure-under-apply` guard (COMPILER-20, main 19364) run
through the plug: all five shapes agree with x86-64, full application,
flat-two, split-one-at-a-time, split-four and half-then-one.

**Two things the suite and the compiler caught that reasoning did not.**
`$clo_apply1`'s no-capture fast path names `$fn1` unconditionally, and a
module whose functions are all arity 0 never declares that type, so two
previously green subjects went red until the type emission got a floor of
1. And `ListUtils` already had `list-take` and `list-drop`, generically and
with better clamping than the copies written here; CDX3006 named the
collision and the chapter is cited instead.

 A companion defect closed with the census run, kept only because the shape
 recurs: `desugar-pattern-at` bound a `let` with the same name as its
 parameter, and the emitter declared a local for it, which is
 `redefinition of parameter` and refuses the WHOLE module. One function in
 5,177 carried it. The repair is that a parameter already owns its slot, so
 a same-named `let` shares it, exactly as a `let` shadowing an outer `let`
 already does through `locals-add`. Graded by
 `codex/plugs/wasm/test/param-shadow-rt.codex`, which reverting the fix
 turns red on all three of its functions.

**1.61 -- the wasm plug had no CCE layer. DONE 2026-08-25 (fester).** A Text
in the module's memory now holds CCE code units, as it does on bare metal,
and the conversion to UTF-8 happens once, in `$wasi_print_text`, against
tier-0/1 and tier-2 tables generated from `to-unicode` at emit time.

**The gap was wider than the print path, which is the part worth keeping.**
The plug's Text was UTF-8 END TO END, not CCE awaiting a conversion: a
literal's data segment held the emitter's own UTF-8 output while its length
header counted CCE code units, so `héllo` was six bytes labelled five and
`char-code (char-at "héllo" 1)` answered 195 against bare metal's 97. Text
INDEXING was wrong, not only rendering. The three sites that had to move
with the boundary were the literal data segments, `$i64_to_text` and
`$cx_text_to_integer`, the last two because `show` and `text-to-integer`
carry digits in CCE, where `0` is not 48. `$wasi_print_i64` writes straight
to `fd_write` and stays ASCII.

Graded by `codex/plugs/wasm/test/cce-text-rt.codex`, which carries both
input shapes: an accented LITERAL and a text built from a NUMERIC code unit.
Every earlier subject built text from ASCII literals alone, which agree
under either reading, so nothing in the corpus could express the defect
(L-CONSTRUCT). Sabotaging `cce-digit-zero` alone moves the three
digit-bearing rows and leaves the other three unmoved.

**`raw-bytes-to-text` is unblocked by this and is 1.60's row to close.**

**One consequence, for whoever next REBUILDS the spark or designer pages
with `build-spark.ps1` / `build-designer.ps1`.** Their JS reads exported
text a byte at a time and calls `String.fromCharCode` on it
(`spark-webgpu.html:136`, `readExportText`), while the app fills that buffer
with `char-code (char-at s i)` (`write-str-loop`, and `write-int-at` through
`integer-to-text`). Those bytes were UTF-8 and are now CCE, so a rebuilt
page's OBJ, STL and JSON exports would render as mojibake and exported
numbers as control characters. The checked-in `.html` artifacts embed their
own `.wasm` from 2026-08-20 and are NOT affected until rebuilt. The page was
correct only because the plug disagreed with bare metal, where the same app
writing the same bytes is wrong today; the repair belongs on the page side
or in the app, not by putting the plug back. Nothing in a gate covers those
two builders, so this notice is the only thing standing between a rebuild
and a silent regression.

**`list-insert-at` fills in place on the flat-memory targets and copies on the
garbage-collected ones. RULED 2026-08-25 (Damian): that is correct, and each
plug does what is natural for its target.** *"do what is natural and best for
the target ... if its garbage collected, let it collect. we don't have to
match the behavior of a flat memory allocator in a language that doesn't
typically do that."* So a plug emitting for linear memory takes x86-64's
shape (`X86_64ListHelpers.codex` Section `__list_insert_at`, in place
whenever `count < capacity`, which is why `bs-alloc` is `input`), and a plug
emitting for a language with a collector uses that language's mechanism, as
javascript's `[...(...)]` spread does. **Do not open this as a divergence
again.** The wasm plug matches the natives byte for byte, measured against
x86-64 by `codex/plugs/wasm/test/list-capacity-rt.codex`.

The one property worth knowing, because the signature does not show it: the
builtin's type reads pure (`List a -> Integer -> a -> List a`), so a program
that inserts and then reads the ORIGINAL binding observes the insert on the
flat-memory targets and does not on the collected ones. That is a property of
the builtin rather than a defect in either plug, and it is the reason a
subject written to assert "base unchanged" asserts something false on bare
metal.

**THE CENSUS IS RE-MEASURED, 2026-08-25, AND THE ANSWER IS ZERO UNDEFINED
NAMES.** Compiler bundle 2,936,371 bytes through the plug against seed
966EF113: IR 16,302,973 bytes, WAT 9,311,017 chars, 5,177 functions, 2m28s.
`wat2wasm` reports **not one** `undefined local variable` or `undefined type
variable`. The 35-to-11 figure and every successor to it are superseded and
should not be quoted again; 1.60 closing is what closed them.

**The instrument can still show the opposite, which is why the zero is
worth anything.** A missing builtin prints `undefined local variable
"$name"` plus `undefined type variable "$fnN"`, and
`build-output/e2e/undef-probe.wat2wasm.err` is a kept example of exactly
that. Zero of that kind appeared here.

**THE MODULE STILL DOES NOT ASSEMBLE, AND THE BLOCKER IS NOT A BUILTIN. IT
IS PARTIAL APPLICATION.** One error kind, 110 sites, 56 distinct callees:
a function applied to FEWER arguments than its arity, in argument position,
is emitted as a direct under-applied `call` instead of a closure. Read
straight off the WAT, `make-type-arith-mul` has arity 4
(`Parser.codex:96`) and is emitted as
`(call $make_type_arith_mul (local.get $left) (local.get $op_tok))` inside
`(call $unwrap_type_ok ...)`.

**It is one capability, not 110 items, and the difference decides how it is
planned.** `unwrap_expr_ok` accounts for 39 of the sites and
`unwrap_type_ok` for 10, both the parser's result-unwrapping idiom
`unwrap-expr-ok r (continuation a b)`, whose second argument is always a
partially applied continuation. Passing a function by NAME already works
through the funcref table that landed with 1.60; what is absent is a
closure carrying CAPTURED arguments. Anyone budgeting off "110" budgets
110 times what this needs (L-ADJECTIVE, the count-for-a-shape half).

**Do not read wabt's `but got [T]` as the call's argument count.** It is
the operand-stack depth at that point and includes values the enclosing
expression already pushed, so it reports three supplied where the emitter
wrote two. It is fine for finding the sites and useless for measuring the
shortfall.

**What is proven, and the boundary matters.** Subjects go source -> IR ->
plug -> WAT -> `wat2wasm` -> module -> `wasmtime`, and each answers
CORRECTLY, which is stronger than assembling: `add2 40` gives 42; a
200-definition chain gives 19,901, which is 1 plus the sum of 0..199; and
`map-list double [1,2,3,4]` then `list-at ys 3` gives 8, exercising a
function passed as a value through `call_indirect`. The compiler itself
emits, assembles, starts, reads its source and then SPINS: re-measured
2026-08-25 against seed 7AF7CEF5, 16,316,110 bytes of IR give 9,468,360
chars of WAT and a 1,508,424-byte module, `wat2wasm` exit 0. **"The compiler
runs in a browser" is not proven and must not be repeated until a module
compiles something.** Emitting, assembling, starting, reading and COMPILING
are five claims and four are cleared.

**`codex/plugs/wasm/wasm-e2e.ps1` is the runner, and it exists because those
subjects were hand-run into prose** (fester, 2026-08-25). It grades every
subject in `codex/plugs/wasm/test/` against THE SAME SOURCE COMPILED FOR
x86-64, which is the only oracle here that is not this plug's own output. It
REFUSES rather than skips when `wat2wasm` or `wasmtime` is absent, and when
the plug binary is older than its source or the seed.

Three things it will not do, each learned by measurement rather than
supposed. **A `(call $name)` census cannot see a missing builtin at all**: an
unresolved name reaches the funcref path and emits `call_indirect (type $fnN)
... (local.get $name)` against an undeclared local, so a call scan reports a
clean census while seeing nothing. `wat2wasm` IS the census, and the harness
keeps its diagnostic because that names the missing builtin and the line.
**It compares against a truth whose capture carries a leading CCE `0x01`**
that the wasmtime run has no equivalent of; the harness strips it, and the
61 payload bytes then match exactly. **And it separates TRUNCATED from
LENGTHS DIFFER** (L-SHORT), leaving a same-length real difference reported as
a plain disagreement rather than trained-away noise.

Both arms are proven, not assumed: sabotaging `$ll_to_list` to fill forward
turns `order:` into `4 3 2 1` and moves no other row, and that is exactly the
output a naive reading of the js plug's mutating-append would have shipped.
Three further sabotages on the insert paths each moved a DIFFERENT set of
rows and were each caught: widening the upper bound let an out-of-range
insert answer `len: 2` where x86-64 traps; disabling the in-place path
un-aliased `base`; and collapsing the copy path's shift turned `prepends`
into `1 0 0 0 0 0` while leaving `into empty` unmoved, because a
single-element insert has nothing to shift.

**It passes `-Kernel` to BOTH arms, and did not at first.** `run.ps1` took
whatever `build.ps1` last left in `build-output` (measured here at digest
`096D5B76` against the seed's `C9395985`), so the IR handed to the plug came
from a different compiler than the CDX it was being graded against, and any
disagreement could have belonged to either. `run.ps1` now accepts `-Kernel`
and the harness threads its own.

**The funcref table, since the next reader will need its shape.** Index is a
function's position in the sorted arity list and the `elem` segment is
emitted from that same list, so the two cannot drift. One `(type $fnN)` per
arity, which is total because every value on this target is i64. Two
separate defects were behind the single error: a function used as a value
emitted `local.get`, and applying a LOCAL holding a function emitted only
the arguments and dropped the call, so `map-list f xs` silently became `xs`.
Nothing downstream had ever run this plug's output, which is why a missing
call never surfaced as a wrong answer.

**Refusals that are deliberate, not gaps to close silently.**
`__self-type-defs`, `read-line`, `block-sector-count`, `process-get-pid`,
`block-read-sector`, `block-write-sector`, `port-in-byte`, `port-out-byte`,
`write-binary`, `write-binary-buf`, `read-file-uni`, `process-get-scope`,
`prof-start` and `prof-dump` emit `(unreachable (; wasm plug: ... ;))`.
These name hardware and a host filesystem; a wasm module has neither and no
approximation beats refusing. `read-line` is the one worth building next and
is small: WASI has `fd_read` and the runtime header already imports
`fd_write` beside it. It is what self-hosting on a page will need.

**1.1 -- lift the plug type reconstruction into shared code. DEFERRED**
(Damian, 2026-08-05): a de-risking rehearsal, not a prerequisite. Group-3
sites are `clamp-field-val` (csharp), `a64-field-type-for-store`,
`rv-find-field-type-st`, `a64-collect-field-types`, `rv-collect-field-types`,
`rc-check-ctor-ref-sum`, and the python and javascript clamp paths.

**1.3 (residue) -- the general RISC-V temp-collision defect either side of a
frameless binop is open** (fester). `RiscVCodeGen.codex:1880-1884` records
that the frameless literal-operand fix is not the general fix.

**1.53a -- the reservation fix TRADES peak memory on a fully-touched
reservation, and my CL 18594 cost note was wrong to say otherwise.** That note
said "strictly less of both". Measured after red asked the right question:
a 200 MB reservation written across at stride 4096 peaks at **298 to 342 MB**
with the fix and **156 to 200 MB** without it, three runs each, both exiting 0
with identical output. The old code grew once to exactly N; the new one grows
incrementally and the arena never frees, so each geometric realloc leaves the
previous buffer behind. The factor is bounded at about 2x by the growth
schedule and it is not a failure.

It remains the right trade by a wide margin -- reserve-and-touch-little goes
from 2,810 MB to 10 MB, which is the case `act-tco-loop` and any
reserve-then-fill program is in -- but the claim to make is "much less in the
common case, bounded more in the worst case", not "strictly less".

The leak that sets that 2x is its own item, 1.54, not this one's to carry.

**1.54 (residue) -- `cx_heap` is off the arena and the touch-everything branch
is NARROWED, not closed.** `cx_buf_want` now grows the buffer through
`std.heap.page_allocator`, so a realloc releases what it replaces; everything
else stays on `cx_gpa`, where never-freeing is the point. Two runs each,
polling sampler, same three programs throughout:

| arm | before 18596 | 18596 (arena) | now |
|---|---|---|---|
| reserve 3.1e9, one write | 2,952 MB / 630 ms | 10 MB / 79 ms | **6 MB / 18-34 ms** |
| touch 200 MB at stride 4096 | 200 MB / 57 ms | 338 MB / 85 ms | **294 MB / 148 ms** |

**The residue is transient COPY cost, not retained garbage, and that is why
this did not reach 200 MB.** Each growth allocates the new buffer, copies, and
only then frees the old, so both are live at the moment of the copy. The
arena's extra ~44 MB was genuine retention and is gone; what is left is
inherent to a copying grow.

**IT ALSO COSTS TIME ON THAT ARM: 85 ms to 148 ms.** `page_allocator` takes a
fresh mapping per growth where the arena could sometimes extend in place. It
is the right trade because memory is what fails and 148 ms for 200 MB is not,
but it is a real cost and is not hidden.

**What would actually close it:** reserve address space and commit on demand,
so growth never copies. That is a custom allocator over `VirtualAlloc` and
`mmap` and is a larger change than either of these rows.
**1.56 -- DONE 2026-08-25 (reek), val cleared the entry.** `emit-binary`
now intercepts `IrPowInt` and emits `((long)Math.Pow(l, r))`, which is
character for character what `wpf` and `winforms` already emit, so csharp
stops being the outlier rather than gaining a new house style.

Measured end to end rather than read: the probe is `pw (a) (b) = a ^ b`
over six pairs. Bare metal answers 1024 81 1 125 49 1000000; the fixed
plug's C#, built and run under dotnet, answers the same six. The control
is the depot emitter restored and rebuilt, and it answers 8 7 2 6 5 12,
the XOR values, which is the symptom this row was opened on and confirms
the arm can fail. The fix state was hashed before the control ran and
verified after restoring, because a control run leaves the tree in the
control state. `plug-oracle-test -Only csharp` still passes 49 of 49.

**The source spelling is `^`, not the `**` this row said.** Caret lexes
to `OpPow` and lowers to `IrPowInt` (`Desugarer.codex:257`,
`LoweringTypes.codex:186`); `**` is a parse error, CDX2000. `2 ^ 10` is
1024 in Codex and XOR 8 in C#, which is exactly the 8 recorded below.

**The oracle still cannot see this class**, unchanged by the fix:
`plug-oracle-arith.codex` contains no `^` at all, which is why it passed
49 of 49 over the defect for as long as it existed. An exponentiation row
is gate weight and needs red's clearance, same as the overflow row below.

`emit-bin-op`'s `is IrPowInt -> "^"` arm is left alone. It is now
unreachable, its only other caller being the vector path, which does not
list `IrPowInt`; removing it would make that `when` non-exhaustive.

The original account: **the csharp plug emits XOR for integer
exponentiation.** Steve Howell's aside on PR 76, verified here 2026-08-21
against the source:
`CSharpEmitterExpressions.codex:984` maps `IrPowInt` to `"^"`, and nothing
intercepts `IrPowInt` before `emit-bin-op`, so line 1005's `otherwise` arm
emits `(l ^ r)`. In C# `^` on integers is XOR, not exponentiation. The
sibling plugs are the control and they are right: `wpf`, `winforms` and
`java` all go through `Math.Pow`, so csharp is the outlier rather than the
house style. `2 ** 10` answers 8 there and 1024 on bare metal.

**Its aside about python and javascript is NOT verified and is recorded as
his claim, not as a measurement**: that the python plug emits `+` on
unbounded ints with no 64-bit mask and so diverges silently past the word,
and that javascript is worse because f64 loses exactness past 2^53. Read
the emitters before acting on it.

**Neither has a runner, and that is the actual gap.** `plug-oracle-arith`
has no overflow row, measured 2026-08-21 by ablation: putting a plain `+`
back on the zig plug's `IrAddInt` still passes the oracle 49 of 49, so the
oracle cannot see wrapping in any plug. An overflow row would catch this
whole class at once and is a gate-weight change, so it is red's call rather
than a thing to add here (Steve offered to propose one).

**1.14 -- deep recursion is not free on a stack language.** What remains is
measurement when a runtime appears. Establish each plug's class by ABLATION,
not by the language's reputation: python looked like a C-stack limit and is a
counter, one line to raise. **The wasm half is CLOSED** (fester, 2026-08-25,
1.82): `return_call` runs every saturating tail call, mutual included, in the
caller's frame, and the design's class-3 verdict for this target is
overturned -- the compiler self-compiles byte-identically at a 1 MB browser
stack. **"Every" was too strong until 1.91**: a call in the last statement of
an `act` block reached neither `return_call` nor the self-loop, because the
tail-call walker had no `IrAct` arm, and that is the one the compiler's own
streaming emitter is written in. Non-tail depth (a real frame obligation)
remains the honest residue on every conventional target, wasm included.

**Re-measured 2026-08-27 at red's request, and the wasm half stays CLOSED
with its scope now stated in a number rather than a condition.** The
shipped page module completes the self-compile in a worker at 0.5 MB and
above and dies at 0.25 MB, so the browser floor is between those two and the
smallest stack any browser gives a worker is above it. 1.83's account of
this row was wrong in three parts (compiler-side, seed-affecting,
`codex-emit-expr`); all three are corrected there, and what closed it was
plug-side with no seed and no token. The measurement and its control are in
1.83. **What is NOT closed by this is the other plugs' half**, which is what
the row was originally for: python is a counter one line to raise, and every
other runtime still wants its class established by ablation rather than by
its language's reputation.

**1.20 (residue) -- the pascal record type.** No Free Pascal toolchain on this
box (`fpc`, `ppcx64`, `lazbuild` absent), so anything here is reviewed by
reading. Two traps for the next reader: `WriteLn` and `Halt` are PROCEDURES,
so `Result := WriteLn(...)` does not compile, and the entry wrapper must emit
`opening;` or it prints an Unassigned Variant after the real output.

**1.29 -- DONE 2026-08-25 (reek), red's call on the deletion.
`codex/plugs/arm64/Arm64Elf.codex` is deleted and the row's three constants
are all accounted for.**

The deletion call this row was waiting on was the BUILDER, not a constant.
`arm64-build-elf` occurs only in its own signature and body across
`codex`, `apps`, `build` and `tools` outside `build-output`, and the chapter
holding it was never in the arm64 plug's chapter list -- so it was not merely
uncalled but **never compiled**, which the bundle confirms: zero occurrences
of `arm64-build-elf`, `elf64-header` or the chapter title in
`build-output/plug-source.codex`. Proved dead by the build rather than by the
grep: same seed, same source otherwise, the plug rebuilds to the same 17,683
bundled lines, the same 823,610 bundle bytes and a **byte-identical
706,776-byte binary, hash unchanged**. That is a stronger result than the
`a64-load-base` deletion on 2026-08-20, whose hash did move because the
disassembler constant really was compiled in.

Nothing else names it: the only remaining mentions are two docs under
`docs/Designs/Done/`, which is archive and deliberately not read at init, and
they are left as the historical record they are.

**The other two constants were already closed on 2026-08-20 and the condensed
row read as though all three were open.** Re-verified against current source
rather than inherited: `a64-load-base` has **0** occurrences under
`codex/plugs/arm64`, and `a64-disasm-base-addr` is `#40100080`
(`Arm64Disasm.codex:493`), read at `:510` for every listing line. The address
the deleted builder disagreed with is real and unchanged --
`compile-arm64.ps1:143` loads at `0x40100000` -- and that PowerShell builder
is the one the cross bed uses. So after the deletion **no stale load-address
constant with a live reader remains**, which is the whole of what this row
asked.

One consequence worth knowing before deleting any `.codex` from a plug
directory: it moves the README's plug module count, which `check-doc-counts`
gates. 141 to 140, corrected in the same CL, 63 claims 0 drifted.

**1.33 -- there is no DECK on riscv** (blu), so nothing can be made to outlive
a `__heap-restore` there. Three of the five arm64 arms are done; the riscv
side returns its SIZE argument or a literal 0. Latent: `__deck-alloc`
returning a size where the caller wants a pointer.

**1.39 -- cobol is BLOCKED on its toolchain.** All five stages landed; `cobc`
is absent and Damian's standing rule is that no new build environment is
installed now, so every claim in the CLs is read against the language rather
than run. Next step, when that rule lifts: install `cobc`, then run the
subjects.

**1.39a -- DONE 2026-08-25 (reek), both halves. The cobol plug constructs and
discriminates a variant, and match guards are honoured.**

**The guards.** An `EVALUATE` prong cannot fall through and its `WHEN` takes a
value rather than a condition, so a prong can carry neither a guard nor the
statements a guard needs to compute one. A guarded match is emitted as a chain
instead: one `IF` per arm, gated on a flag, which is linear where nesting each
remaining arm inside the previous arm's `ELSE` would duplicate them
exponentially. The pattern test is outside and the guard's own statements are
emitted inside it, so a guard never reads a payload slot belonging to a
constructor that did not match. A match with no guards still takes the
`EVALUATE` path and its emission is byte-identical, which is the regression
control.

The tail-call path takes the same shape (`emit-cobol-tco-chain-arms`). There
the flag is not what stops the next arm running -- every tail arm leaves by a
`GO TO` -- it is what stops an arm whose body does not jump from falling into
the next arm's test.

**A third defect, again found by the census and not by reading: `is x when ...`
bound nothing.** `cobol-bind-pat` bound constructor sub-patterns only, so a
whole-scrutinee `IrVarPat` left `WS-X` undeclared. It was invisible until the
guards became live, because the only reference to `x` was inside the guard that
was being dropped.

**Measured against the corpus's own oracle.** `plug-oracle-arith.expected` ends
`3 1 2 4 7 0` for classify and `0 1 2` for band; the emitted chain traces to
exactly those, where the `EVALUATE` it replaces answered the first matching
prong every time (1, 1, 1, 4, 4). A purpose-built subject covering both guarded
paths, including a guarded self-recursive function, answers `neg 1, big 2,
mid 3, eq 4, diff 7, nil 0, band 0/1/2/3, sum5 15` on bare metal, and the chain
traces to each. The undeclared-name census is 0 on all three subjects. **Still
not executed: there is no `cobc` on this box.**

The account of the first half:

A variant value is now the group item its declaration always described: a
constructor writes the payload into `-F0..-Fn` and the tag last, into its own
temporary rather than the type's single global instance, and a variant-typed
parameter, let binding and return slot are declared with that same layout, so
passing one is a group MOVE. A constructor pattern's sub-patterns bind to the
scrutinee's payload fields for the arm and are restored after it.

**The trap that makes the obvious implementation fail: the wire spells a
variant type as its NAME alone.** `(sum "Shape" (args))` carries no
constructor list, so `SumTy`'s own ctors are empty at the plug and neither the
layout nor the tag can be derived from the value's type. Both come from the
chapter's `type-defs`, threaded through `CobolState`, which is the same list
the declaration half already read.

**A second defect, found by the census below and not by reading: the match
read `-TAG` off EVERY scrutinee.** `band : Integer -> Integer` matches literal
patterns, and `EVALUATE WS-BAND-N-TAG` named a field a `PIC S9(18)` has never
had. The subject is now the tag only when a branch carries a constructor
pattern.

**The instrument is a census of undeclared names**, which is what a program
this emitter cannot express actually looks like: take every `WS-` name the
PROCEDURE DIVISION references and subtract the ones WORKING-STORAGE declares.
It reported **5 undeclared on each subject before and 0 after** -- `WS-A`,
`WS-B`, `WS-N`, `WS-NIL`, `WS-CLASSIFY-V-TAG`, `WS-BAND-N-TAG` -- and the
control arm firing 5 is what says it can fail at all. The control is depot
revision #23 reinstalled and rebuilt, emitting the 44,577 bytes recorded
below; the fix state was hashed before the control ran and verified after
restoring. **Not executed: there is no `cobc` on this box**, so this is
verified as emitted shape and against the x86-64 oracle by reading, not as a
run. The purpose-built subject answers `num: 5 / pair: 7 / nil: 0` on bare
metal, which is what the emitted COBOL now computes and what it could not
before.

The original account, which the census confirmed in every particular and
understated in one -- **`Pair 3 4` dropped its second argument entirely**:

**cobol cannot construct or discriminate a variant, and the missing
match guards are downstream of that.** Not toolchain-blocked: it is readable
in the emitted source without `cobc`. Measured 2026-08-24, plug rebuilt first,
`codex/test/plug-oracle-arith.codex` emitted through `run.ps1`, 44,577 bytes:

- `emit-cobol-expr-to-var` has **no `IrCtor` arm**, so a constructor goes
  through `IrApply` and `Num 5` emits `MOVE 5 TO WS-CLASSIFY-V`, a scalar.
- **Not one `MOVE ... TO <name>-TAG` anywhere in the output.** The type
  declaration is emitted (`05 WS-VAL-TAG PIC 9(2)`, and `TAG-NUM`/`TAG-PAIR`/
  `TAG-NIL` constants), so the representation exists and nothing ever writes
  into it.
- The match reads `EVALUATE WS-CLASSIFY-V-TAG` and `EVALUATE WS-BAND-N-TAG`.
  Neither name is declared anywhere in the program. Nor is the payload binder
  `WS-A`, referenced once by the `Pair` arm, nor `WS-NIL`.
- Guards are dropped: `classify` emits three identical `WHEN TAG-NUM` prongs
  and `band` two `WHEN OTHER`, so only the first of each is reachable.
- **No refusal marker of any kind in the output.** It emits a whole program
  and reports OK, which is the silent-wrong-answer shape
  `plug-oracle-arith`'s own prose names as the worst one.

**So cobol is a CLOSURE of the nim/elixir/objc kind, not the fourth plug of
the 1.46 match-guard substitution, and that reclassification is the point of
this row.** `.guard` field reads, measured the same day: ada 3, fortran 6,
pascal 3, **cobol 0**. Adding a guard chain here would be adding one to a plug
that cannot express the failure it is meant to catch, which is exactly what
the four-plug block warned against when it was written.

Both parts that block called for have landed: the representation written as
well as declared, and the guards after it. The reclassification still holds --
cobol was a closure of the nim/elixir/objc kind and not the fourth plug of the
1.46 match-guard substitution, and it is now closed rather than substituted.
`.guard` field reads in `CobolEmitter.codex` are **5**, re-measured
2026-08-25, against the 0 that row records for 2026-08-24.

**1.41 -- the per-byte accumulate is down to three sites, and none of them is
the row's original cost.** `plug-run.ps1` was the 116.77 s per 16 MB instance
and has carried the fix and the number since. Swept 2026-08-24 for
`.Read($x, 0, 1)` across every `.ps1` outside `old/` and `build-output/`, three
sites remain:

- **`codex/plugs/elf/extract-x86-output.ps1` MAP tail. FIXED here.** Measured
  over `seed/Codex.map` (176,303 bytes, 5,336 lines) on a loopback socket,
  three runs each: **4,039-4,122 ms per byte against 167-173 ms buffered**,
  same 5,336 lines both ways. **The per-line `Add-Content` beside it was the
  bigger cost by far and the row never named it: 35,000 ms against 46 ms for
  one write**, same lines. Both fixed.
- **`build/vm-config.ps1` `Read-StreamLine`. NOT a defect, do not "fix" it.**
  One byte per `Read` is what stops it consuming a byte past the newline, which
  is what lets a caller switch to `Read-StreamBytes` for a binary payload on the
  same stream. `extract-x86-output.ps1` does exactly that between SIZE and the
  ELF bytes. Buffering it would corrupt every binary read in the tree. The MAP
  tail above is buffered only because it comes AFTER the binary read with
  nothing but the close behind it.
- **`tools/test-codex-vm.ps1`, two loops.** Dead code, see below.

**The end-to-end measurement was never chased because it CANNOT be run, and
that is the find worth keeping.** `Start-VmRun`'s codex-vm path builds
`-data-port N -ctrl-port N+1`, and **codex-vm parses neither flag in any
revision of `tools/codex-vm.c`** (#1 through #110 checked; they appear only in
the usage banner at line 6). **The deeper defect is that codex-vm ignores an
unknown flag in silence**, so a flag that does nothing and one that works look
the same to every caller in the tree.

**THAT HALF IS NOW CLOSED (reek, 2026-08-27): codex-vm refuses the first
unrecognised argument, names it, and exits 2.** The parse loop ended with no
final `else` over 121 flags, so anything unclaimed fell out of it. It has one
now, and only a leading `-` can reach it because every value is consumed by its
own arm above.

**It found a second instance on its first run, which is the whole argument for
it.** `build/test-exception-handler.ps1` passed `-serial stdio -timeout
$budget` and codex-vm parses neither, in any revision. The budget was never the
guest's: `Wait-Process -Timeout` beside it is what enforced it, and still does.
Dropping both changes no behaviour and the harness still passes 5 of 5. Fixed
in `codex/build/testexceptionhandlerScript.codex` and regenerated, generator at
0 drift on both sides of the change.

**The census for this is the GATE, not a grep.** `-Internal` green with
`run-list`, `vm-differential`, the BVT and the oracles all driving codex-vm,
plus `Start-PlugVm`, `Invoke-PlugVmFileSerial` and `compile.ps1` exercised by
hand. A syntactic sweep of `'-flag'` tokens in files mentioning codex-vm
returns 86 candidates that are mostly PowerShell parameters and QEMU flags: it
cannot answer this question in either direction, which is the shape a hurried
census always has. **What the refusal will break is what was already broken** --
`tools/test-codex-vm.ps1` still passes `-data-port`/`-ctrl-port` and will now
say so on its first run rather than hang, which is this row's own point.

Here the guest
boots with nothing on the wire, halts inside 500 ms, and `Start-CodexVmRun`'s
`HasExited` check reads that as a failed launch and returns null after four
attempts. Every harness on that path is unrunnable wherever codex-vm is present,
which is every box: `extract-x86-output.ps1`, `build/test-disk-compile.ps1`,
`tools/sim-test.ps1`, `build/gdb-watchpoint.ps1`. `tools/test-codex-vm.ps1` is
further gone: it invokes `codex.build\sample-compile-selfhost.ps1`, a path that
does not exist.

**`extract-x86-output.ps1` is dead in BOTH halves and switching transport
cannot revive it.** Measured 2026-08-24: besides the missing `-data-port`, the
`ELF` mode header it sends does not exist in the compiler. `compile-plain`
tests `CDX`, `IR-UNI`, `IR-CCE` and `MEASURE` and sends everything else to
`emit-text-streaming`, so `ELF` returned 1,154 bytes of echoed source, exactly
what `ZZZZ-NOT-A-MODE` returns; `CDX` returned 88,394 bytes with a `SIZE:`
line as the control. Container formats moved to the plugs long ago, which is
what `CLAUDE.md` already says. So the choice for this harness is DELETE it or
rebuild the ELF path, not repair its transport, and that is a call rather than
a fix.

**So the fix above is measured in ISOLATION and is NOT proven end to end.** The
control run of the depot script failed identically, which is what says the
failure is not the change. Whether `Start-VmRun` gets its serve mode built in
codex-vm or gets deleted is not this row's call; it is recorded in
`OperatorsManual.md` under the flag table.


**1.46 (residue) -- the text plugs are not wired to the oracle, and cannot
be until the no-new-toolchains rule lifts.** Six are wired (python,
javascript, typescript, zig, wasm, csharp) and every one of those had its
runtime already on the box. Measured 2026-08-21 across 52 executable names
covering every remaining emitter -- ruby, perl, php, lua, java, go, rustc,
scala, kotlin, swift, ghc, ocaml, clojure and the rest, plus the alternate
spellings (`clj`, `luajit`, `ldc2`, `runghc`, `guile`, `racket`) -- and the
only one present is `nvcc`, which compiles ptx device code rather than
running a console subject. So the remaining plugs are not unwired for want
of the wiring: there is nothing on this box to run what they emit, and
Damian's standing rule is that no new build environment is installed now.

This row is BLOCKED for the same reason as 1.39, not merely open. Anyone
picking it up should check `Get-Command` for the language first; if a
runtime has appeared, the wiring itself is one entry in the `$Plugs` table
in `build/plug-oracle-test.ps1`, which is blu's claim.

**1.48 -- RULED LATENT 2026-08-25 (red): the guard suffices until the lane
emits `br`.** `a64-peephole-mov-elim` folds `mov Rd, Rm` into the preceding
instruction whenever that instruction's `Rd` matches, which is sound only
while the preceding instruction runs on every path reaching the mov. The
guard is in; the general case is not. `br` is the standing gap -- an indirect
branch carries no target in its encoding -- and this lane emits none, so
there is no complainant. The row stays open as the note to read **before
adding a `br` to this lane**, which is the moment the general case starts
mattering; it is not work until then.

**1.57 -- JAVA HALF DONE 2026-08-25 (reek). RISCV: THE MISCOMPILE
REPRODUCES, THE RULED FIX DOES NOT FIX IT, AND THE REAL SITE IS FOUND
(reek, 2026-08-28).**

**THE SITE IS `RiscVCodeGen2.codex:593`, THE `is otherwise` ARM OF
`when ty`.** `rv-emit-apply` flattens the curried spine at `:536` and then
dispatches at `:571` on **`ty`, the APPLICATION'S RESULT TYPE**, not on the
callee's type. A fully-applied call has a non-function result, so it always
lands in `is otherwise`, which emits a flat `rv-emit-direct-call` with the
whole argument list and consults no arity at all. That is correct when the
callee's emitted arity equals the argument count (`add3 1 2 3`) and wrong
when the callee is a one-parameter definition returning a function
(`choose 0 2 3`), which returns the closure and ignores the extra
arguments -- the heap address printed above.

**That is also why ruling 21's wiring was inert, and it is the part worth
keeping.** The `FunTy` arm at `:581-592`, which holds the recorded arity
and `rv-emit-partial-application`, is reached only when the result is STILL
a function -- that is, UNDER-application. **Over-application can never
arrive there**, so wiring an over-apply route into it could not fire for
any program.

**Found by markers, not by reading.** Four name-scoped probes emitting the
constant 777 through `rv-emit-int-lit`: the `IrName` arm printed 777
(reached), the `known >= 0` branch and the `known < 0` fallback did not
(not reached), the `is otherwise` arm printed 777 (confirmed). **The first
prediction was WRONG** -- the `known < 0` fallback was named as the site and
is not -- and the sabotage that first "tested" it swapped
`rv-emit-direct-call` for `rv-emit-partial-application`, which for a
saturated call is not a distinguishable change, so it moved nothing and
proved nothing (L-SABOTAGE). The 777 marker is what settled every one of
the four.

**The fix does NOT use `rv-emit-closure-over-apply`, and that function
still has no caller.** It applies remaining arguments one at a time;
riscv's closure convention takes them all in one `rv-emit-closure-call`
(`:1073-1081`, args to registers, closure pointer in `t2`). Wired its way
the program faulted; passing the rest in a single closure call passes. So
this row's original "riscv has the fix and does not call it" was mistaken
about WHICH fix riscv needed.

**FIXED AND VERIFIED.** The subject passes 5 of 5 on Renode against an
oracle derived from the definitions, and reverting restores both wrong
values exactly.

**Regression breadth: a compile-only WIRE DIFFERENTIAL over all 613
eligible cross subjects, control plug against fix plug. 600 byte-identical,
11 no-wire in BOTH arms (compile-refusal negatives the compiler rejects
before the plug runs), and 2 moved.** The Renode cross battery was the
wrong instrument and was abandoned: its 466-subject run phase was killed
twice at scale on a 15.8 GiB box four lanes were gating on. The
differential is sharper anyway, because this change can only alter
emission where a named call carries more arguments than its recorded
arity, so a subject whose bytes are unchanged cannot regress and only the
movers need booting. **The two movers are exactly the two over-application
subjects**, which is the result the argument predicts.

**Two harness defects were found before the differential could lie, and
both produced plausible numbers.** First it hashed `-Out`, but
`compile-riscv.ps1` exits 4 without copying `-Out` when the plug issues a
by-design `[UNSUPPORTED]` refusal **while still emitting the wire**, so 96
of 466 subjects recorded `NOOUTPUT` and the differential was blind on
every one. Caught by checking the census's NEGATIVES against an
independent log rather than trusting the count. Second, and worse: after
that repair one arm held rows hashed from `-Out` (the ELF) and the other
from `last-compile.riscv.bin` (the wire). **Those are different artifacts
of different sizes** -- measured on `act-let-scope`, ELF 45,728 against
wire 50,101 -- so 372 of 613 subjects reported as "changed" when nothing
had. The tell was the shape of the result, not any single row: a uniform
+4 to +5 KB across nearly every subject is not what a dispatch change
looks like. **Both arms were discarded and re-run under one script
version**; that is where 600/2/11 comes from. L-SAMEVER, one level down:
prove the two arms are measuring the same KIND of thing, not just the same
version.

**TWO PRE-EXISTING REDS ON RISCV AT HEAD, neither caused by this change
(each verified by running the control plug and getting the identical
failure), and NOTHING RUNS EITHER OF THEM (L-NOGATE):**

- **`codex/test/ops/saturated-call-returning-function`** produces NO
  output at all, dying before its first line. **This is the canonical test
  for this very feature** -- nine arms covering one-at-a-time, rest-at-once,
  flat, arity-two, self-recursive and mutual over-application -- and it has
  been red on this lane while the defect it exists to catch shipped. Its
  first statement is `let a = mk 4 in let a2 = a 20 in ... (a2 22)`, a
  two-level let-bound closure chain, which is NOT this row's site: the
  single-expression form `(c 2) 3` works. A separate defect and worth its
  own row.
- **`codex/test/closure-under-apply`** fails from `split-one-at-a-time`
  onward.

This change moves `saturated-call-returning-function`'s emitted bytes and
the test is dead either way, so the move is not observable. **Do not read
this row as closing over-application on riscv**: it closes the named
over-apply site, and the canonical test stays red for a different reason.

**Correcting this row's own claim below that riscv "does NOT reproduce":
it does.** That measurement was taken on two subjects that do not carry
the shape; `codex/plugs/test-input/overapply.codex` does. Run on Renode
through `build/test-cross.ps1 -Arch riscv64`, graded against answers
derived from the definitions rather than from a previous run, riscv
prints:

```
named-over: 2148533408      (expected 6)
named-over-alt: 2148533504  (expected 7)
```

A heap address where an Integer belongs, which is the over-applied call
returning the closure unapplied. `control-flat`, `stepwise` and `after`
are all correct, so the defect is confined to the over-application and
the subject carries its own controls. This is the depot-side
observed-miscompile verification the row asked for.

**Ruling 21's riscv half was built and REVERTED, because it is inert.**
Wiring a named over-apply branch into the `known >= 0` dispatch
(`RiscVCodeGen2.codex:583-587`) and routing it through a
`rv-emit-named-over-apply` helper onto `rv-emit-closure-over-apply`
changes NOTHING: control and fix emit byte-identical wires for
non-inlined IR (49,980 bytes, same SHA-256), and produce character-identical
Renode output including the same two wrong values. **The branch is live
and the arity test is what excludes it**: widening the condition from
`>` to `>=` moves the emitted binary (49,980 to 50,004 bytes, different
hash), so the named path IS reached and `list-length args > known-arity`
is true at no call site in the subject. Sabotaging the under-applied arm
moves `stepwise` and not `named-over`, so that arm is not the site
either. **The site is still unidentified**, and it is not the one the
ruling names. Landing the wiring would have shipped a second correct
branch nothing takes -- the exact complaint this row opens with -- while
reading as "riscv over-application is fixed".

**Instrument defect found on the way, FIXED in this CL: both native plug
runners reported a guest FAULT as a successful emission.**
`codex/plugs/riscv/run.ps1` and `codex/plugs/arm64/run.ps1` wrote
whatever came back from the VM to `-Out`, printed `OK: <path> (947
bytes)` and exited 0, when what came back was a register dump beginning
`!EXC=06 RIP=...`. **Two such dumps DIFFER from each other, because the
RIP moves with the plug build, so a control-versus-fix byte comparison
over them reads as "the change moved the output" when both arms crashed
and neither emitted anything.** That is what it read as here for an hour,
until the bytes were looked at. Both scripts now refuse with exit 7 and
print the dump's first line; an empty output refuses too. The tag is
FOUND in the first eight bytes rather than compared at offset zero,
because the dump carries codex-vm's leading `0x01` marker and an anchored
compare sees `\x01!EX` and misses every real fault -- the first version of
this guard did exactly that and passed its own positive arm. Proven both
directions on both plugs, plus `test-cross.ps1 -Arch riscv64` and
`-Arch arm64` green end to end. **The way to land in it: feeding `-IrUni`
output to a runner that wants `-IrCce`**, which is what
`compile-riscv.ps1` passes and what the usage line does not say.

**java is fixed and the defect was observed, not inferred.** Emitting
this row's own suggested subject (a named 1-ary definition returning a
function, over-applied) produced `static Object make_adder(Object n)`
declared beside `make_adder(10, 31)` at four call sites, which is the
uncompilable Java the row predicted. `emit-jv-apply` now consults
`lookup-arity`, which it had threaded through and never read, and splits
on `args > ar` into `((java.util.function.Function<Object,Object>)
make_adder(10)).apply(31)`. That cast-and-apply is the idiom the emitter
already uses for lambdas and match scrutinees, so this adds no new house
style. Measured: exactly 4 lines change on the probe and the file is the
same 43 lines, and the whole `plug-oracle-arith` corpus emits
BYTE-IDENTICAL before and after, so no ordinary call was touched.
**Not executed: there is no JDK on this box**, so this is verified as
emitted shape, not as a run.

**riscv does not reproduce ON THESE TWO SUBJECTS, and RENODE IS INSTALLED
so it can be run here.** Superseded as a general claim by the 2026-08-28
block above: neither subject carries a definition returning a function, so
neither can reach the case, and `overapply.codex` does reproduce it. What
stands is everything below about the two subjects and the pipelines.
`build/test-cross.ps1 -Arch riscv64`
drives `codex/plugs/riscv` under Renode at `C:\Renode\renode.exe`. Two
subjects, one inlinable and one built to defeat both inline passes, both
answer exactly what x86-64 answers. **Sabotaging the branch this row
names (`RiscVCodeGen2.codex:585-587`) leaves the emitted binary
BYTE-IDENTICAL**, as does sabotaging the `otherwise` closure arm, so
neither is on the path for these subjects. The instrument was proven
able to fire: forcing `rv-emit-apply` itself to emit a literal changes
the binary hash and empties the output.

**The likely reason, and it is the useful part.** `codex/plugs/java/
run.ps1` says text plugs run a pipeline that must not inline, because
they resolve calls by NAME (`text-plug-ir-pipeline` in
`codex/compiler/IR/Passes.codex`). The native plugs take the ordinary
pipeline, where the front end emits nested single-argument applies and
inlining removes these call sites before the plug sees them. That is
consistent with the zig case being observable end to end while riscv is
not. **What is NOT established is that riscv can never be reached**; only
that it is not reached by a Codex-front-end subject of this shape. Any
future claim about riscv here should sabotage first and require the
binary to move.

No arm was added: the probe was temporary and is not in the depot,
because a permanent one is gate weight and red's clearance to give.

The original account: **`riscv` and `java` do not handle over-application
of a named definition, and riscv's correct fix is in the tree with no
caller.**
From the zig-plug ladder (`contrib/README.md`), 2026-08-24.
`docs/DevelopersRulebook.md:256-260` requires a plug that knows the
callee's arity to handle three cases -- flat at that arity,
under-applied with one arrow per missing parameter, over-applied by
applying the rest. The rule is unqualified: it binds "a plug", and names
the TS/JS family only as plugs that already carry the model. Three plugs
implement two of the three.

**riscv has the fix and does not call it.** The named-definition path
(`RiscVCodeGen2.codex:583-591`) tests `list-length args < known-arity`
and routes to `rv-emit-partial-application`; every other case,
`args > known-arity` included, falls into `rv-emit-direct-call` with the
whole argument list. Seventy lines below, `rv-emit-closure-over-apply`
(`:660-668`) is a correct take/drop over-apply, and
`grep -rn rv-emit-closure-over-apply codex/plugs/` returns exactly three
hits: its signature, its definition, and its own self-recursive tail.
Nothing reaches it.

**java never consults arity at all.** `JavaEmitter.codex:158-168` emits
`func & "(" & emit-jv-apply-args args ... & ")"` for both the `IrName`
root and the `otherwise` root. `lookup-arity` is defined at `:69-70` and
has no call site in the file.

**arm64 is a near miss, not a defect.** It has
`a64-emit-oversaturated-call` (`Arm64CodeGen2.codex:927-932`) reached
from `:980-981`, but the arity it consults is `a64-known-arity`
(`:901-915`), a hardcoded table of builtin names, so it does not fire
for user definitions. Its local-closure path (`:976-978`) does use a
real def-arity table.

The compliant plugs do it two ways, either of which is a template:
`csharp` (`CSharpEmitterExpressions.codex:830-841`), `python`
(`PythonEmitter.codex:646-655`), `javascript` (`:501-511`) and `rust`
(`RustEmitter.codex:547-560`) route every non-exact case to a curried
spine, so over-application is correct by construction; the TS family
(`TypeScriptEmitter.codex:205-214`) splits on `args > ar` with
take/drop, as does the compiler's own x86-64 back end
(`X86_64Compound.codex:154`, arity map built at `:38` from
`list-length (d.params)`).

**What is measured and what is not.** The same gap in the zig plug is
observed end to end: `((even-fn 4) 20) 22` against a one-ary definition
emits `even_fn(4, 20, 22)` and zig refuses it at compile time with
`expected 1 argument(s), found 3`. That one is the ladder's to fix and
is not this row. For riscv and java this entry offers the dispatch code
and the grep, NOT an observed miscompile, and the reporter is not going
to supply one -- **this wants verifying on the depot side, where the
toolchains are.** Per this file's own standing hazard about name
censuses, treat the runtime consequence as inferred from the emitted
shape until a subject has been run through both plugs and the output
read. Concretely, what would settle it: over-apply a NAMED top-level
definition that returns a function, emit Java, and check whether the call
site names a method the same file declares with fewer parameters. The
ladder host has no JDK and installing one is not its call, so the row is
deliberately filed as a source-level report rather than held back until
someone can run it. Note what would and would not catch it if someone
did: `test-plugs.ps1` asserts non-empty text with markers and never
COMPILES what a plug emitted, so it cannot detect this in `java` however
often it runs, and by its own prose it does not drive `riscv` or `arm64`
at all -- the native backends take `-IrInput` and emit the binary wire
protocol, so they "fail parameter binding and exit 1 in under a second
having done no work at all" and are deliberately absent from its plug
list.

**Why none of it was caught, which may be the cheaper half.**
`codex/plugs/test-input/partial.codex` exercises under-application
(`let g = add3 1 2`), saturation (`add3 1 2 3`) and over-application of
a LOCAL (`let h = add3 10 in (h 20) 12`), but its only definition is
`add3 : Integer, Integer, Integer -> Integer`, which does not return a
function. Nothing in the corpus over-applies a NAMED top-level
definition, so the branch all three plugs get wrong is unreachable from
it. `codex/plugs/test-plugs.ps1` then judges exit code,
non-empty output and text markers (`:93-97`, `:163-177`) without ever
compiling what it emitted. One added definition in `partial.codex` would
put all of these in front of a compiler.

**The ask is one ruling:** whether over-application of a named
definition is required of every plug that keeps an arity map -- in which
case riscv wants its dead function wired up and java wants an arity
check -- or whether some plugs are exempt, and `:258` should say which.

**1.58 -- the zig plug's self-tail loop reads a TOP-LEVEL DEFINITION where
the source reads its own parameter, and two blind spots had to line up for
it to be silent. DONE 2026-08-25, absorbed from Steve Howell's PR 85 (his
fix, his verification ladder; the emitter hunks land verbatim).** Found
when the ladder's census re-pin moved `dtls-fragment` from `match` to
`refused`: `error: unused function parameter`. The refusal is the symptom;
the defect under it returns a wrong number with no diagnostic.
`dtls-frag-loop` (`codex/foreword/encode/DtlsMessage.codex:97`) takes
parameters `body` and `msg-type`, and the test beside it defines top-level
`body` and `msg-type`; zig forbids the shadow, so `zig-def-param-name`
renames them to `_arg_body`/`_arg_msg_type`. The emitted LOOP body then
called `body()` and `msg_type()` -- the top-level definitions -- because
`emit-zig-def`'s loop branch built its context from
`zig-push-tail-renames`, which covers only the parameters the loop
REASSIGNS; an invariant parameter got no rename and fell through to the
definition. The fix composes `zig-push-param-renames` underneath, tail
renames still winning for the reassigned ones; the non-loop branch always
did this and the two branches now agree. **Why silent:** the obvious
minimization CANNOT return a wrong answer -- `zig-occurs` drives a
discard, a visible read means no discard and zig refuses the unused
parameter loudly. The silent form needs a read the check is blind to, and
`zig-occurs-branches` walked a branch's body and not its GUARD; a match
guard inside one of the loop's tail-call arguments was invisible.
`zig-max-list-len-branches` had the identical hole by the file's own
"mirrors zig-occurs" instruction (loud failure, no corpus program reaches
it; demonstrated before fixing). His verification: a `shadow-guard` tier
row that FAILED first (bare metal 3 vs zig 5), then the fix, then row
green both arms, 22 tiers green, 14/14 rungs, `dtls-fragment` back to
`match` with exactly one verdict moved, byte-identical zig everywhere
else. Three more corpus programs carry the same collision
(`final-batch-test`, `lorawan-encode`), both still `refused` for
unrelated reasons. **The reusable part: the tier set never gave a loop a
shadowing parameter, so the whole class sat outside the instrument; the
depot's own corpus caught it by accident** (L-CONSTRUCT's shape, found by
a contributor).

**1.59 -- the plug corpus could not reach the Rulebook's over-application
case, and the input that closes the gap arrived measured red and landed
green. DONE 2026-08-25 (red), absorbed from Steve Howell's PR 86.**
`docs/DevelopersRulebook.md:260`'s third case only exists when the
over-applied definition RETURNS a function, and `partial.codex`'s only
definition returns an Integer, so the corpus could not reach it -- which
is the case 1.57 records riscv and java getting wrong. Steve wrote
`codex/plugs/test-input/overapply.codex` to carry the shape and measured
it against public seed `6CF4A8E0`: two of its five lines FAILED on bare
metal (a heap address printed for `stepwise`, a fault at `named-over`)
while the zig plug answered 6, 6, 6, 7, 15 correctly. **Between his seed
and head, main 19364 closed COMPILER-18 and COMPILER-20 together, and
re-measured at head (seed `A43CFD61`) all five lines are GREEN on bare
metal**, matching the zig plug exactly -- his file was a red witness for
precisely the two defects blu fixed the same day, and his unexplained
"two return paths" variable matches COMPILER-20's
saturated-call-returning-function shape (read from that row's record,
not re-derived). Costs he stated that remain true: the standing gate's
`plug-smoke` reads only `hello` and `record`, so this file runs under
`codex/plugs/test-plugs.ps1` alone; that harness's `$markers` table has
no entry for it (judged on exit code and non-empty output, as
`partial.codex` already is); and the full text-plug sweep puts a
function-returning definition in front of roughly thirty emitters that
have never seen one from this corpus, which is UNMEASURED and stays open
in this row -- COMPILER-13's four-plugs-failed-on-first-lambda is the
precedent for what that sweep may find.

**1.72 -- the python plug's TCO matches a self-call by NAME and not by
arity, so its argument loop and its parameter loop can disagree. LATENT:
whether any well-typed program reaches it is UNESTABLISHED, and that is
the weakest part of this row.** Absorbed from Steve Howell's PR 87 (his
row 1.60, renumbered: the wasm lane took 1.60-1.71 the same day);
citations spot-verified at head by red 2026-08-25, line numbers drifted
by one or two and the mechanism holds. Read against 1.57 first: python's
curried spine is correct by construction and this row does not dispute
it; this is the TCO path, reached only from the `is-self-call` arm.
`is-self-call-root` (`PythonEmitter.codex:665`) compares the chain's ROOT
name to the definition's name and nothing compares argument count to
parameter count; the jump then evaluates one temporary per ARGUMENT
(`emit-py-tco-temps:727`) and assigns one parameter per PARAMETER, so the
loops agree only at exact arity. Fewer arguments: `NameError` on the
first turn, and on later turns a STALE python function local from the
previous iteration -- the loop continues with the wrong argument and no
diagnostic. More: the extra temporary is dropped and the outer
application disappears. The zig plug is the control: `zig-tail-self-call`
requires `list-length (chain.args) == (tl.tail-arity)`
(`ZigEmitter.codex:2641`), so an inexact self-call is an ordinary return.
NOT ESTABLISHED: the ladder could not construct a well-typed program in
which a definition tail-calls ITSELF at non-full arity, and does not
claim one exists -- so this is a missing guard rather than a defect with
a victim, filed because "the type system happens to prevent it" and "the
emitter checks" are different statements and only the second survives a
change to either. No python arm was run (no runner on the reporting
host). What would settle it, in order: first the type-checker question
(does the shape exist at all), then emit and READ the output directly.
The fix, if wanted, is not one clause: `is-self-call` has no arity access
(signature change, three call sites) and gating in `should-tco` would
disable TCO per definition where zig gates per call.

**1.73 -- no `run.ps1` consults the VM host selection in the config it
sources, so no plug can run on QEMU anywhere: not on Linux, and not on a
Windows box without WHP. RULED by Damian 2026-08-25: SUPPORTED. The
fallback contract is honored on EVERY host, Windows included.** Absorbed
from Steve Howell's PR 88 (his row 1.61, renumbered; doc-only by his own
design, "the fix is a fan-out decision that is yours"). His measurement,
on Linux at public `0c4327d5`: `build/vm-config.ps1:14-16` states the
contract (codex-vm primary and Windows-only; QEMU the fallback; the hard
failure reserved for having NEITHER) and implements it, and across all 56
runner scripts nothing reads its CHOICE variable. They divide three ways:
38 delegate to `build/plug-run.ps1`, which hardcodes
`tools\codex-vm.exe` with no fallback; 8 hardcode the same path
themselves (wasm, html, spirv, t3isa, winforms, ptx, wgsl, evidence); 10
read the config's PATH variable and skip its CHOICE variable, so they
look like they consult it and do not. The infrastructure keeps a promise
no caller collects. **The work, in leverage order:** (1) `plug-run.ps1`
honors `$script:UseCodexVm` and the discovered QEMU, which covers 38
scripts in one edit; (2) the 8 hardcoders and 10 half-readers route
through the same selection; (3) the QEMU arm of each plug's wire needs
its own smoke, because a path that has never run is a path that has
never worked (L-UNCALLED), and the Start-VmRun ghost-flag history
(L-ACCEPTED) lives in exactly this neighborhood -- enumerate what each
host binary actually accepts before passing it flags. Owner: reek
(the runner scripts are the plugs lane, `run.ps1` claim 1.15).

**STEP 1 LANDED (reek, 2026-08-25).** `plug-run.ps1` reads
`$script:UseCodexVm` and boots QEMU when it is false, which is the 38
delegating scripts in one edit. Done through `plugrunScript.codex`; drift 0.

**The QEMU arm needed no guest-side change, and that was the open
question rather than the flags.** Every plug dials `host-ip 127.0.0.1`
through gateway `10.0.2.2`, which is a fact about codex-vm's NAT, so the
expectation was that QEMU's user networking would drop it and each plug
would need a new address (L-BEDTRUE). It does not: measured, the guest
connects and the exchange completes unchanged. **Not reasoned -- probed,
because the reasoning said the opposite and was wrong.**

Evidence, two plugs and a failing control rather than one green:
python/hello 1296 bytes `953EDAF6` and typescript/hello 2671 bytes
`B02785B0`, each BYTE-IDENTICAL across `codex-vm` and
`CODEX_VM_HOST=qemu`; with `QEMU_BIN` pointed at a missing file the same
arm fails, so the QEMU branch is the one that ran. The QEMU flags mirror
`Start-VmRun`, which is where they were measured.

Two things fell out and are fixed here. `$proc` is initialised before the
`try`, because the `finally` reads it and an unset name THROWS under
`Set-StrictMode`: a missing VM binary used to report that StrictMode error
instead of the launch failure. And `-WindowStyle` is splatted in only on
Windows, since it throws on other editions of pwsh -- not incidental, as
Linux is the host this row exists for.

**STEP 2 LANDED (reek, 2026-08-25), and it corrects the row's own count.**
"8 hardcoders and 10 half-readers" is a number standing in for a shape
(L-ADJECTIVE). Measured, the eighteen divide by TRANSPORT and the line cuts
across both groups:

- **7 use TCP plus an output ring** -- csharp, elf, img, javascript, pe,
  recheck, wpf. Same mechanism `plug-run` already had.
- **11 preload serial with `-input`** -- evidence, html, ptx, spirv, t3isa,
  wasm, wgsl, winforms, arm64, maui, riscv. A different problem.

So the useful split is one solved mechanism plus eleven needing a second,
not eighteen scripts. The 7 now call **`Start-PlugVm`** in `vm-config`,
which is also where `plug-run`'s own copy went: the choice lives in ONE
place rather than eighteen, because eighteen copies are eighteen chances to
drift.

**`isa-debug-exit` IS WHAT MAKES QEMU LEAVE, and omitting it cost the first
attempt.** codex-vm exits when the guest halts; QEMU treats a halted CPU as
an idle one and sits there. A runner that waits on process exit and THEN
reads the console therefore waits forever: csharp ran its full 1800s
timeout and finished in seconds once the device was added. The guest
already writes port 0xf4 -- that is where codex-vm's `debug_exit_code`
comes from -- so this only gives QEMU something to listen with. It also
means the QEMU exit code is `(value << 1) | 1` and never 0, which is safe
only because no caller reads it.

**That failure is why "7 share a mechanism" was not enough to ship on.**
javascript passed on both hosts while csharp hung, and the difference was
not the transport this row classifies by: it was whether the runner waits
for the STREAM to end or for the PROCESS to exit.

Proven both hosts, byte-identical: python/hello `953EDAF6` (through
plug-run), javascript/hello `6A9553AD`, csharp/hello `7A67A28F` at 11,411
chars. **NOT proven, and not claimed:** elf, img and pe need a binary wire
fixture rather than a source file (`elf/run.ps1` takes `-X86Input`), and
recheck and wpf were not run. They take the same helper as csharp, which is
an argument and not a measurement.

**STEP 2b LANDED (reek, 2026-08-25): the 11 file-serial runners too, so all
56 now honor the selection.** `Invoke-PlugVmFileSerial` in `vm-config`
mirrors codex-vm's `-input`/`-output` contract on both hosts, and the
thirteen launch sites across the eleven call it.

**QEMU HAS NO `-input`, AND THE FLAG THAT LOOKS LIKE IT IS A TRAP.** QEMU
11.1.0 does carry `-chardev file,input-path=` and REFUSES it on Windows:
"input-path not supported on Windows". The route that works on every host
is the one `Invoke-VmCompileFallback` already took -- a SOCKET chardev on
the guest's only serial port, host writes the input and reads the answer
off the same wire. `server=on,wait=on` holds the guest at reset until the
host has connected, which is what makes a preloaded ring and a live socket
interchangeable from the guest's side. The port comes from `Get-VmPort`,
never a literal (L-SHARED).

Proven byte-identical on both hosts: wasm/hello 69,368 chars `CB709BEB`,
ptx/hello 1,630 `0A392EC3`, wgsl/hello 174 `F53E78A1`. The control is
wasm with a prebuilt `-Ir` so no compile is in the way: good QEMU binary
passes with the same hash, bogus one fails at the launch. **The first two
attempts at that control failed at the IR COMPILE instead**, which also
needs a VM -- an arm that fails for the wrong reason proves nothing about
the branch under test.

`-DiskFile` is on the helper because evidence's ingest launch passes
`-disk`; without it that one site would have stayed on codex-vm and
evidence would have been a runner that LOOKS like it consults the config
and half does, which is the exact defect this row opened on.

**STEP 3 BUILT (reek, 2026-08-25, red's clearance), HOLDING ON THE MAIN
PIN.** `plug-smoke` runs its EXISTING 4x2 matrix a second time under
`CODEX_VM_HOST=qemu` and requires the two hosts to agree BYTE FOR BYTE. No
new subjects: those four already span both launch helpers, python and
typescript and rust through `Start-PlugVm` and ptx file-serial through
`Invoke-PlugVmFileSerial`.

**Byte-identical is the assertion, and it has to be.** Asking only whether
the run exited 0 is exactly what let csharp sit through its full 1800 s
timeout while javascript passed beside it. A differential against the
codex-vm answer catches a host that finishes and lies; an exit code does not.

All three arms fired before it was called done, because a check nobody has
watched fail is not evidence (L-FALSIF): the positive reports `cross-host OK
(8 subjects byte-identical on codex-vm and QEMU)`; a bogus `QEMU_BIN` exits 1
naming all eight; and a box with no QEMU prints that it SKIPPED rather than
passing quietly, which would have been a check that cannot fail. Red's
condition is in the failure text itself -- a subject that flaps cross-host is
a finding about that subject or that host, to be recorded before it is
quieted.

**FIRST FLAP RECORDED, 2026-08-27 (fester), and it is about the HOST.** An
`-Internal` gate went red with `python/hello(qemu produced nothing),
rust/hello(qemu produced nothing)`; the immediately preceding gate on
essentially the same tree passed the same phase, and an immediate re-run with
no change to any file passed it again, `cross-host OK (8 subjects
byte-identical)`. At the moment of the red, four `codex-vm.exe` processes from
ANOTHER workspace were running a gate concurrently on this box, so the failing
condition was contention rather than the subject: both failures are "produced
nothing", which is the QEMU side timing out or being starved, not a wrong
answer. **A wrong answer here would still be a real finding and this was not
one**, which is exactly the distinction byte-identical buys over exit-zero.

**SECOND FLAP THE SAME SESSION, AND THE PAIR IS A KNOWN SHAPE, NOT A MYSTERY.**
Later that day, `FAIL: plug smoke -- python/record (run.ps1 nonzero or empty
output)`: a different subject, the LOCAL arm rather than cross-host, with ONE
foreign `codex-vm` on the box and the CPU at 4 per cent. Green again on an
immediate re-run with nothing changed. So across seven `-Internal` runs that
day plug-smoke went red twice, on two arms and two subjects, both times green
next run.

CPU contention was my first reading of the first red and it does not survive
the second. **The mechanism already had a diagnosis in the tree, written the
same day, and I had not looked**: `build/plug-run.ps1`, above its four `$null`
initialisers -- *"a port still held from the previous subject makes
`$listener.Start()` throw before any of the three is assigned, and the finally's
reference then masks the port error as 'variable cannot be retrieved' (gate,
2026-08-27, three plugs reported 'produced nothing' on their second subject)"*.
`$Port` defaults to a FIXED `9100`, so a socket still held from the previous
subject, or from another workspace's run, takes it. That is L-SHARED, and it
explains "produced nothing" on a second subject exactly.

**What is fixed and what is not, kept apart on purpose.** That change fixed the
MASKING: the real port error now surfaces instead of a StrictMode complaint
about an unassigned variable. It did not make the port unique per workspace or
per subject, so the collision itself is untouched, and a mechanism that explains
a symptom is not its cause until a fix moves the symptom. The discriminating
step for the next flap is therefore cheap and specific: read the error, which is
no longer masked, and see whether it names the port.

Two things still worth keeping. A red in this phase is worth one re-run before
it is believed, and the re-run is 40 s. And `produced nothing` and `differs`
should not read alike in the failure text: the first is a statement about the
host, the second about the subject, and only the second is ever a plug finding.
Neither changed here (R-ONE).

Cost measured in situ rather than described: the phase goes from 12.7-18.2 s
to **53.9 s**, and it runs only when plugs or the compiler changed.
Of the 56, eight are proven on both hosts (python, javascript, csharp,
typescript, wasm, ptx, wgsl, and plug-run's own arm); the rest take the
same two helpers, which is an argument and not a measurement. elf, img and
pe additionally need a binary wire fixture rather than a source file. And
the codex-vm serial-drop check (`output buffer growth failed`, exit 10)
still has no QEMU counterpart, so on that host a short console is not
detected: say so rather than read its silence as agreement (L-FALSIF).

**babbage is SHELVED** (Damian, 2026-08-21): vanity work. Its open items
moved to `codex/plugs/babbage/babbage-backlog.md`. Do not add babbage items
here.

**1.84 -- FIXED, the zig plug took a TYPE VARIABLE for an answer and emitted
it as a name into a caller that declares no such name.** (Steve Howell,
2026-08-26; `codex/plugs/zig/` per the standing note above.) Found when
Update 50 first sent a lifted lambda through a text plug and the compiler's
own zig-transpiled source came back with 47 undeclared `T38`s.

**Why a plug meets an unresolved variable at all.** `CSharpEmitter.codex:534-541`
sets it out: "the compiler's IR-CCE lift runs after the resolve pass, so a
`__lam_N` def carries the expected types its lambda was handed, not the
resolved ones", and "the IR is well-typed". C# answers `dynamic`. This plug
recovers the type instead, walking each declared parameter type against the
type actually supplied and answering with whatever sits where the variable
sits (`ZigEmitter.codex:2150-2165`).

**The mechanism.** That walk had no way to tell "no answer here" from "an
answer that is itself a variable" -- it carried two sentinels, `""` on the
Text-answering copy and `VoidTy` on the CodexType-answering one, and a
variable answer was neither. Matching `map-list`'s declared `(a -> [e] b)`
(`codex/foreword/core/ListUtils.codex:41`) against a `__lam_0` of
`(tvar 23 -> Integer)` answers `a = tvar 23`, so the scan stopped there and
never read the list argument one place along -- whose `List a` against
`List Integer` is the answer that was wanted. The variable was then emitted
as the text `T23` into a caller declaring no such name. `T23` here is one
worked instance; the 47 that stopped the release were `T38` in the
compiler's own transpiled source.

Concrete beats variable; variable beats nothing; nothing is the
`@compileError` marker `zig-resolve-tvar` already ended in
(`ZigEmitter.codex:2351-2356`), which could not fire while a variable answer
looked like success. A variable answer is KEPT as a last resort rather than
refused, because inside a generic definition it is the right one:
`map-list`'s own body calls `map-list-loop`, and there `T23` is a `comptime`
parameter that is in scope.

**The two walks are now one.** The prose above them claimed the walk was
shared when it had been copied, so the fix had to be written twice before
they were collapsed (`ZigEmitter.codex:2127-2130`). The caller now supplies
the actual TYPES rather than the argument expressions, because one caller
has no expressions to offer.

**That caller is the second half of the defect.** With `a` recovered, the
closure the plug builds around a function value still carried the variable in
two emitted places -- the environment struct's parameter list and its `CxFn`
type -- because `emit-zig-name` handed the lambda's type over without passing
it through the enclosing call's own bindings, and because the trampoline
called a generic callee unapplied: `fn __lam_0(comptime T23: type, x: T23) i64`
entered as `__lam_0(p0)`, one argument against two
(`ZigEmitter.codex:2452-2465`). The trampoline is a call site like any other
and now applies its callee's type arguments.

**Verified**, in the order a red row first then green demands. On the Update
50 pin: 47 undeclared-identifier errors in the compiler's own transpiled
source, gone. Re-measured 2026-08-26 after the fix, in a fresh sandbox:
`codexzig` builds with **0** `map_list(T…` sites; its FIXED POINT holds --
re-emitting its own bundle byte for byte at 2,351,567 bytes -- and holds for
the first time against a subject that actually contains lifted lambdas, 354
`__lam` definitions on both arms where the driver arm had 300 and ours 0
before. The 22-tier set shows **0 unexpected on every tier** (15 green, 6
noted, 1 stale for an unrelated reason recorded in the ladder). In the corpus
census `typeclass-smoke` moves `refused -> markers`: the marker now fires
where the plug used to emit a bogus type name for zig to reject, which is an
improvement rather than a regression.

**A residue this change does not clean up.** `zig-subst-arg-type`
(`ZigEmitter.codex:2115-2120`) has no caller -- only its signature, its
definition and its own recursive call -- and it was already uncalled at the
pin. This change updates its parameter list (`List IRExpr` to
`List CodexType`) to keep it compiling, rather than deleting a function that
is not ours to remove. It is dead either way and worth a decision.

**1.85 -- the same recovery walk knows `List a` and `a -> b` and nothing the
subject declares, so a variable inside a program's own generic type cannot be
recovered from any position. The gap 1.84 left.** (Steve Howell, 2026-08-26.)

**The whole of it is fourteen lines**, `codex/test/tvar-in-declared-type.codex`,
added by this change:

```
Pair (a) = record { fst : a, snd : a }

pair-swap : Pair a -> Pair a
pair-swap (p) = Pair { fst = p.snd, snd = p.fst }
```

No lambda, nothing lifted -- measured against natives built before this fix,
**0** `__lam` definitions in its IR -- and the emitted zig carries
`unresolved type variable T42 of pair-swap`. Bare metal answers 73. **Lambda
lifting was the path that exposed this class, not its cause**, which is why
the reproducer is smaller than the case that found it.

**The mechanism.** At the pin, `zig-tvar-in-type`
(`ZigEmitter.codex:2184-2194`) reads:

```
  zig-tvar-in-type : Integer, CodexType, CodexType -> CodexType
  zig-tvar-in-type (id) (decl) (actual) =
   when decl
    is TypeVar (vid) -> if vid == id then actual else VoidTy
    is EffectfulTy (e) (s) (inner) -> zig-tvar-in-type id inner actual
    is ForAllTy (fid) (inner) -> zig-tvar-in-type id inner actual
    is ForAllEff (c) (inner) -> zig-tvar-in-type id inner actual
    is ListTy (elem) -> zig-tvar-in-elem-type id elem actual
    is LinkedListTy (elem) -> zig-tvar-in-elem-type id elem actual
    is FunTy (p) (fnrow) (r) -> zig-tvar-in-fun-type id p r actual
    is otherwise -> VoidTy
```

It unwraps three transparent wrappers and descends `List`, `LinkedList` and
function types. A `ConstructedTy`, `SumTy` or `RecordTy` -- every
parameterised type a program declares for itself -- falls into `otherwise`
and answers `VoidTy`. So the variable is unrecoverable both in the parameter
loop and in the return fallback that `zig-resolve-tvar-type` reaches when the
parameters run out (`ZigEmitter.codex:2157`).

**Traced through real IR.** `range-to` in `codex/test/roc-iter-map.codex:57`
has the monomorphic signature `Integer, Integer -> Iter Integer`, though the
subject around it does declare generics (`Iter (a)`, `Step (a)`, `iter-map`).
Its partial application is annotated
`(fn int-default (ctd "Step" (args (tvar 16))))`, so `zig-closure-make`
(`ZigEmitter.codex:2467`) hands `resty = Step (tvar 16)` to the resolver,
which peels `__lam_1`'s declared return to `(ctd "Step" (args (tvar 16)))`
and asks the walk to match the two. Both sides are `ConstructedTy`.
`otherwise`. `VoidTy`. The emitted zig then carries
`@compileError("zig plug: unresolved type variable T16 of __lam_1")` in the
type-argument position, and `Step(T16)` in the closure's `call` return type
where `T16` is declared nowhere.

**It is not confined to tests written for it.** Two depot programs put an
unresolved variable inside a `ConstructedTy`'s arguments -- the arm that is
missing -- read out of their IR with pre-fix natives:

    typeclass-smoke   (param "__Showable-dict" (ctd "ShowableDict" (args (tvar 44))))
    db-full-test      (param "m" (ctd "HamtMap" (args (tvar 88))))

`hamt-fold` is Foreword's (`codex/foreword/core/Hamt.codex:247`), which
`db-full-test` reaches through `cites Foreword chapter Hamt`, so it is shared
with several other subjects. **The denominator, and the caveat:** the
ladder's corpus census carries 40 distinct `unresolved type variable` markers
over 51 programs. Whether this fix clears them is NOT established -- the walk
must also find a concrete type at the matching position on the actual side,
and `typeclass-smoke`'s `describe` additionally takes a bare-variable
parameter that the existing `TypeVar` arm already handles, so its failure may
have a different cause. One confirmed mechanism is not a confirmed cause for
all forty.

**The fix** adds three arms descending the argument lists pairwise, plus
`zig-type-arg-list` to read the arguments off the actual side and
`zig-tvar-in-args` to walk the pair. One declaration reaches this code under
all three constructors -- a name is a `ConstructedTy` until the checker
rewrites it to the `SumTy` or `RecordTy` it denotes, and which arrives
depends on how far the type travelled -- so all three descend identically.
Matching is BY POSITION and compares no names. That is sound on the strength
of the well-typedness `CSharpEmitter.codex:534-541` asserts for this wire:
the type supplied for a parameter is the type that parameter declares, so a
mismatched pair cannot arrive.

**PARTIALLY VERIFIED 2026-08-26, and the first write-up of this row used the
wrong metric.** Natives rebuilt against the fix, 597 programs re-transpiled:

    unresolved type variable markers   40 -> 0 distinct, 51 -> 0 program-hits
    all emitter gaps                  135 -> 95 distinct, 40 gone, 0 NEW
    programs with no markers          326 -> 334

**Those numbers are true and they do not mean what they look like.** A
marker count says the emitter stopped SAYING it could not answer; it does not
say the emitted zig builds. Checked afterwards, by building:

    tvar-in-declared-type   refused before  ->  RUNS, answers 73   fixed
    roc-returned-closure    ran before      ->  RUNS, answers 9    unchanged
    roc-iter-map            refused before  ->  DOES NOT BUILD     not fixed

`roc-iter-map` now emits `Step(T16)` and `__lam_1(T16, ...)` with `T16`
declared nowhere -- 31 bare `T<n>` identifiers in its output -- where before
it carried an `@compileError`. **The walk now finds an answer and the answer
is itself a type variable**, which `zig-prefer-concrete` keeps as a last
resort by the deliberate rule 1.84 records: inside a generic definition a
variable IS the right answer. In a closure's environment struct it is not,
and nothing distinguishes the two.

So this change is a real fix for the shape its reproducer has -- a variable
inside a declared type whose actual is concrete -- and it converts a REFUSAL
into an UNDECLARED IDENTIFIER for the shape where the recovered answer is
another variable. **The second is worse than what it replaced**, because a
marker is a diagnostic and an undeclared identifier is a build failure with
no explanation. It should not ship in this state.

**What is owed before this row is worth sending:** the last-resort rule needs
a scope test -- keep a variable answer only where the emission site declares
it -- and then `corpus_run.py --run` over the corpus, which BUILDS what it
transpiles, rather than a marker census.

**PAID, 2026-08-26.** Both halves. The last-resort rule now carries the scope
test this row asked for: `emit-zig-type` takes the set of type variables the
emission site actually declares as `comptime T<n>` parameters (`ZigCtx.scope-tvars`,
set by `emit-zig-def`), and refuses at the OUTERMOST type when a variable is
not in it. Outermost because `zig-is-unmapped` tests a leading prefix, so a
marker buried inside `*CxList(...)` is invisible to it.

Measured by `corpus_run.py --run`, which builds and runs rather than counting
markers:

    tvar markers          40 -> 8 -> 0    over 606 programs
    corpus match          183 -> 185      nothing that matched stopped matching
    ast/allcycles.sh      14/14

`hamt-test`, `kvstore-test` and `inductive-list` traded a diagnostic for a
build failure under the first attempt; under the scope test `typeclass-poly`
goes the other way, `refused -> markers`, and `inductive-list`'s remaining
refusal is a different defect the marker had been standing in front of (a
self-recursive type that is also generic, emitted with no indirection).

**1.86 -- FIXED, a refusal that replaces an expression kills the parameters
that fed it, and zig reports the stranding instead of the refusal.** (Steve
Howell, 2026-08-26; `codex/plugs/zig/`.)

1.85's scope test turned `use of undeclared identifier 'T16'` into a sentence
naming the variable and the callee. Zig never printed the sentence. The
refusal consumed the only expression reading a function parameter, so the
parameter went dead, and zig's unused-parameter check runs against the
signature before the `@compileError` in the body is analysed.

Measured on four programs, with zig's own column landing on the stranded
parameter each time:

    roc-iter-map      857:68   transform: CxFn1(T44, T45)
    roc-iter-keep-if  857:52   pred: CxFn1(T44, bool)
    roc-iter-drop-if  857:52   pred: CxFn1(T44, bool)
    probe-tvar-recovery  908   wrap_int(n: i64)

`roc-iter-map` strands `transform` and leaves `it` alone, because `it` still
has a reader. That asymmetry is what rules out "the parameter was already
dead for unrelated reasons".

**The mechanism was a liveness question asked of the wrong artifact.**
`emit-zig-param-discards` asks `zig-occurs` about the IR body -- the right
question everywhere the emitter answers, the wrong one exactly where it
refuses, since the IR still uses the parameter and the emitted zig does not.
A refused body now discards every parameter. Not the ones a name search calls
dead: `_ = x;` beside a live use is legal zig, and a substring test on
parameter names is a word-boundary collision this tree has been bitten by.

**1.87 -- FIXED, `show` dispatches five ways on the argument's type and this
plug implemented one arm for all five.** (Steve Howell, 2026-08-26;
`codex/plugs/zig/`.)

`show : forall a. a -> Text` (`Types/Builtins.codex:69`). Bare metal picks by
the argument's type (`Emit/X86_64.codex:1652`): an f32 real widens before
`__real_to_text`, other reals go straight there, `TextTy` is the expression
itself, `BooleanTy` is `emit-show-bool`, everything else is `__itoa`. This
plug emitted `cx_show_int` for all five.

**42 of 113 corpus refusals, the largest single class** -- 40 `expected type
'i64', found 'bool'` and 2 `found 'f64'`. The refusal site was read at the
call in three of the forty rather than inferred from the message.

Fixed for Text and Boolean, with the unit wrapper stripped first for the
reason bare metal records beside its own strip (without it a `unit Text`
falls to the integer arm and prints its pointer as a decimal). `True` and
`False` are built through the emitter's existing text escaper rather than
hand-encoded, so their CCE bytes come from the same place every other
literal's do.

**Reals REFUSE with a named marker rather than guess.** `__real_to_text` is
hand-written assembly (`Emit/X86_64TextHelpers.codex:590`) -- sign bit,
`cvttsd2si` for the integer part, fifteen fractional-digit iterations, CCE
digit offsets -- and no `cx_real_to_text` exists here. `std.fmt` would agree
with it on some values and not others, and a `show` that is right for 2.5 and
wrong for 0.1 is worse than one that says it cannot. That is the remaining 2
of the 42 and it is open.

Found by a ported Roc snippet on its first run, not by the corpus, although
the corpus had been carrying the evidence for as long as it has existed.

**1.88 -- FIXED, emitted `main` spawns `opening` directly and zig refuses a
thread entry that returns a value; 40 corpus programs.** (Steve Howell,
2026-08-26; `codex/plugs/zig/`.)

Every emitted program runs its entry on a thread for the 512 MB stack -- the
same workaround the C# plug carries, for the reason it records (the lexer's
`scan-token -> skip-prose-line -> scan-token` cycle, which self-TCO cannot
flatten, overflows 1 MB). Zig requires that entry to return `u8`, `noreturn`,
`!noreturn`, `void` or `!void`. 40 subjects declare `opening` returning a
value, and all 40 failed inside `std/Thread.zig` before a line of their own
code was analysed.

**The value is the program's OUTPUT, not a status.** `ble-att-encode` ends
`in a + b + c + d + e` and its `.expected` is `5`. A shim that discarded it
would have traded 40 loud refusals for 40 silent mismatches.

`cx_entry` is a void shim that prints, dispatching on the CODEX type arm for
arm against `emit-opening-result-print` (`Emit/X86_64Chapter.codex:222`).

An earlier draft dispatched on this plug's own rendered zig type text instead,
reasoning that the shim then could not disagree with the signature it calls.
That was wrong twice over and is recorded because the reasoning is
attractive: the zig type text is LOSSY. Boolean and Char both render to
something that is neither `void` nor `[]const u8` nor `f64`, so both fell to
the integer arm -- a Boolean entry would have re-created 1.87 at a new site,
and a Character entry would have printed a number where bare metal prints
nothing at all. Caught by a cold read before it was built.

**A note for the C# plug, unmeasured by us.** `opening-call-text`
(`CSharpEmitter.codex`) DISCARDS the value of an effectful `opening`. Bare
metal peels the effect and prints it, and the depot agrees: `gpu-ptx` and
`gpu-doorbell` declare `opening : [Console] Integer` and their `.expected`
files end with the bare `0` that print produces. We followed bare metal. We
have no C# toolchain here, so this is a lead and not a report.

**1.89 -- FIXED (half), a unit family was mapped to `void`, erasing the
payload while the arithmetic around it stayed correct.** (Steve Howell,
2026-08-26; `codex/plugs/zig/`.)

`Length = unit family Millimeter` with scale factors; a `Length` value IS its
base-unit integer. `emit-zig-type` mapped every `UnitTy` to `void`.

`unit-family`'s emitted body already computed all four of its expected
answers -- scale factors multiplying, conversions inlined to `@divTrunc`,
`double-length (Millimeter 50)` constant-folded -- and then failed to compile
because the values were typed `void`:

    fn Centimeter(__fv: i64) void {          <- void, should be i64
        return b0: { const __unit_0 = (__fv *% 10); break :b0 __unit_0; };
    }

Three arms move: `emit-zig-type` recurses into the backing type,
`zig-let-annot` peels too (or a `let` holding a unit value is annotated `""`
while its expression has an integer type), and the entry shim of 1.88
recurses rather than assuming `void`. Six programs, and nothing that matched
stopped matching.

**THE OTHER HALF IS DONE 2026-08-27 (reek), and the two symptoms had
different causes.** The row read them as one `else`; only the second one is.

**The unit family was never declared at all**, which is why its name had
nothing to resolve against. `emit-zig-type-def`'s `AUnitTypeDef` arm answered
`""`, so `Frequency` appeared in every field declaration and in no zig
declaration; the value path had already learned the backing type (`UnitTy` to
`emit-zig-type inner`) and the type path could not reach it. The arm now emits
`const Frequency = i64;` from the family's own declared base, which is the same
answer by the same route rather than a second opinion. A zig alias is
transparent, so a field typed `Frequency` and a value typed `i64` are one type.
39 aliases are emitted for a program citing Units and zig accepts an unused
container-level const. **This buys a surface that did not exist before: a unit
family's name is now a container-level declaration and can collide with a user
top-level of the same name, which is 1.90's class.**

**The type variable is the scope failure the row describes**, and the answer
was on the same emitted line. A field declaration is written in the RECORD's
type parameters and a construction site is not inside the record's
declaration, so `a` there names nothing; the site's own type arguments are
what `zig-ctor-type-args` had already rendered as `QueueS(T52)`. The
declaration's tparams are now matched against them BY POSITION, on the same
well-typedness 1.85 rests on, and `queue-test` emits
`QueueS(T52){ .front = cx_ll_empty(T52), ... }` where `T52` is the comptime
parameter the enclosing definition declares. A variable the walk cannot place
answers nothing rather than its own spelling, so the caller's existing
empty-list marker fires: a diagnostic, never an undeclared identifier.

**The variant path had the same defect through the same helper and the
compiler is what found it** -- `zig-ctor-field-scan` reaches
`zig-atype-ll-elem` for a constructor payload, and changing the signature made
it a type mismatch rather than a thing to notice. `emit-zig-ctor-apply` takes
the constructed type now instead of pre-rendered text, for the same reason
`emit-zig-record` does.

**Measured by BUILDING, two arms, 54 subjects** (the 1.84/1.85/1.89 named
programs plus every fifteenth of `codex/test`), the control being the depot
revision installed and the plug rebuilt:

    control   21 MATCH  30 BUILDFAIL  3 no .expected
    fix       23 MATCH  28 BUILDFAIL  3 no .expected

**Two moved, both BUILDFAIL to MATCH, and nothing moved the other way:**
`osc-noise` (`use of undeclared identifier 'Frequency'`) and `edge-mesh-route`
(the same on `Timestamp`), each now running and byte-equal to its `.expected`,
which is bare metal's answer. Exactly one other subject's error changed and it
changed downward, `queue-test` from `undeclared identifier 'a'` to the defect
behind it. `plug-oracle-test -Only zig` passes 55 of 55; `check-plug-builtins`
and `check-plug-guards` are unchanged.

**The type-variable half is verified as emitting the right answer, NOT as
making a program run**, because the only subject in reach of it is blocked
behind the row below. The row's "twelve programs" figure is Steve's corpus and
is not re-measured here; two is what a 54-subject sample moved.

**1.89a -- DONE 2026-08-27 (reek), and the pessimism in the first write-up of
this row was wrong.** A nullary generic definition was called with no comptime
type argument: `fn queue_empty(comptime T52: type) Queue(T52)` called as
`queue_empty()`. The arity-0 branch of `emit-zig-name` emitted
`zig-sanitize n & "()"` and never reached `zig-call-type-args` at all, so the
one machine that answers this question was not asked. It is asked now, with an
empty actuals list, which is exactly the shape a nullary call has.

**This row predicted the recovery could only produce a marker, and the
measurement refutes it.** The reasoning was that a nullary call has no
arguments to recover from and the binding's recorded type would carry an
unresolved variable. `zig-resolve-tvar` falls back to the RESULT type, and the
IR carries the instantiation there: `queue-test` emits `queue_empty(i64)` and
now builds and matches its `.expected`. Where the result type genuinely holds
a variable the fallback is the marker after all, which is what `hamt-test`
gets, so both halves of the prediction exist and the row had guessed which one
was universal.

`zig-call-type-args` separates with a trailing `", "` because value arguments
follow it; a nullary callee has none, so `zig-drop-trailing-sep` takes it back
off.

**Measured against the 20146 arm over the same 54 subjects, built and run:**
one subject fixed outright (`queue-test`, BUILDFAIL to MATCH, 23 MATCH to 24)
and two more moved their error in the right direction: `hamt-test` from zig's
own `expected 1 argument(s), found 0` to 1.85's named
`type variable T25 is not declared at this site`, and `typeclass-smoke` past
it onto a different pre-existing defect. Nothing regressed. Two subjects first
reported anomalies that were the harness and not the plug, `unit-family` a
MISMATCH whose emitted bytes are identical to the arm that matched and
`db-full-test` an empty guest console; both re-ran clean and are recorded here
because a transient that is not re-run is indistinguishable from a finding.

**1.90 -- DONE 2026-08-27 (reek), the zig plug's runtime prelude shadowed user
top-level names with its own locals and parameters, and nothing declared them
reserved.**
(Steve Howell, 2026-08-26; `codex/plugs/zig/`.)

Zig forbids a local shadowing a container-level declaration, so every
identifier the emitted prelude uses privately is effectively a reserved word
for every Codex program this plug compiles.

    dns-answer-count.zig:26:15  function parameter shadows declaration of 'l'
    tcp-checksum-refuse.zig     function parameter shadows declaration of 'base'

against user top-levels `fn l() DnsResponse` and `fn base() NetSession`.

**The surface is 66 names**, extracted from the prelude of an emitted
program: 47 `const`/`var` bindings and 33 parameters. It includes `x`, `y`,
`d`, `e`, `i`, `n`, `s`, `len`, `ctx`, `a`, `hi`, `lo`, `acc`, `buf`, `out`,
`top`, `start`, `code`, `path`. **A Codex program defining a top-level `x`
cannot be compiled by this plug.** `zig-prelude-decls` guards user names
against prelude DECLARATIONS and against nothing else.

This branch renames four of them (`cx_ll_empty`'s `l`, `cx_ipow`'s `base`,
`acc`, `e`) and that is deliberately not the fix -- it is included because it
is what was measured, and because measuring it is how the size of the class
was learned. The two programs above still refuse: the rename moved the error
from a `const` to a function PARAMETER of the same name, which is also how we
found that the first extraction had counted only `const`/`var` and missed
every parameter.

**DONE 2026-08-27 (reek), by the first of the two candidates, and the blast
radius is real and costs nothing.** Both named programs build and match
bare metal now; the control is the depot revision rebuilt and it fails with
exactly the two errors this row records, `shadows declaration of 'l'` and
`shadows declaration of 'base'`.

**The surface is 102 names, not 66**, re-derived from emitted output by
`build/check-zig-prelude-surface.ps1` as this row asked. 76 `const`/`var` and
42 parameters and captures, overlapping; after zig keywords, primitives and
the 18 already listed, **83 names needed reserving** and are now in
`zig-prelude-decls`. The row's example list named `x`, `y`, `hi` and `lo`,
none of which appear in the prelude as it now stands; what it got right is
the half that matters, that an extraction counting only `const` and `var`
certifies a short list.

**The check derives the prelude as the line-wise common prefix of several
emitted programs**, which is exact because `zig-prelude` is one constant
concatenated ahead of all type and definition text: 840 lines, identical in
every program, and a chapter citing nothing agrees with `queue-test` for all
840. It is not wired into any gate.

**Measured over 56 subjects, built and run: 53 of 53 emitted files changed
text and NOT ONE verdict moved**, plus the two named programs going BUILDFAIL
to MATCH. So the blast radius this row feared is entirely in the emitted
spelling, `a` to `a_` and so on, applied consistently at every site because
everything goes through `zig-sanitize`. That is what makes the cheap
candidate the right one rather than the risky one.

**The residue, which the check reports rather than chases:** reserving `a`
makes an emitted binder read `a_`, so the tuple types emit
`fn Tup2(comptime a_: type, ...)`, and a user top-level literally spelled
`a_` collides with that. Reserving `a_` in turn would produce `a__`, one
underscore per run, so the check separates the two outcomes and refuses only
on the first. The residue is strictly narrower than what it replaces, since a
Codex program declaring a top-level `a` is ordinary and one declaring `a_` is
not.

**What this does NOT close, and it is the larger half:** the shadowing class
is not confined to the prelude. Any emitted function parameter shadows a user
top-level of the same name, including parameters that come from the user's
own source, so a program with a top-level `x` and any function taking a
parameter `x` still collides. Reserving the prelude's names fixes the
prelude's half only. The complete fix is to guarantee that emitted binders
never collide with emitted container-level names, which is a rename scheme
over every parameter and local rather than a list, and it is not this row.

**1.91 -- FIXED, THE TAIL-CALL WALKER HAD NO `IrAct` ARM, SO THE COMPILER'S
OWN STREAMING EMITTER GREW A STACK FRAME PER DEFINITION** (fester,
2026-08-27; `codex/plugs/wasm/WasmEmitter.codex`).

`is-self-tail-call` and `emit-wat-expr-tco` both walk `IrIf`, `IrLet`,
`IrMatch` and `IrApply` and both fell through `is otherwise` on `IrAct`. The
value of an act block is its last statement, so a recursive call written
there IS in tail position, and the emitted WAT put it there: a plain
`(call $emit_streaming_ir_defs ...)` as the last expression of the function
body. Two consequences, one per half. The function never got the
`(loop $tco_loop ...)` wrapper, because the gate at `emit-wat-def` asks
`is-self-tail-call` first. And no act-tailed call anywhere reached
`return_call`, so this was never only about self recursion.

**Measured on the page's own module and source** (`build-output/page/`,
2,461,312 bytes of output), node worker_threads, stack pinned:

| plug | 0.25 MB | 0.5 MB | 1 MB | 2 MB |
|---|---|---|---|---|
| before | -- | -- | dies, 2,117,302 bytes out | completes |
| after | dies elsewhere, 0 bytes out | completes | completes | completes |

The 1 MB death was **4,805 frames of `$emit_streaming_ir_defs` out of 4,817**,
every other function contributing two or fewer. Output after the fix is
byte-identical to the pre-fix 2 MB run, SHA-256 `E8B9C9D636B9396998201C18`
over the whole stream, and repeated interleaved runs put the two within each
other's variance (before 10,623 / 11,126 ms, after 10,671 / 11,019 ms), so the
loop costs no measurable time. The module grew 4,375 chars of 9,758,794.

At 0.25 MB the binding function is a different one and nothing has been
emitted yet, so the emit spine is no longer what fixes the floor.

**The register said this close was compiler-side, seed-affecting, and about
`codex-emit-expr`'s tree descent. All three were wrong** (1.83's closing line
and 1.14, both corrected in place). The expression descent is shallow: it
contributed six frames to a stack of 4,817. The symptom that misaimed it was
"dies at the first emitted bytes", which was read off a browser console; the
death is 86 per cent of the way through the output, and the 240 bytes that
reading rested on are the eight `WD:PHASE-` diagnostic lines, not emitted
program text. Reading the byte count as program text pointed the whole item
at the wrong function for two days (L-MECHANISM: read every number the
failure already handed you, and grep the line your mechanism runs through).

**Arm `act-tail-rt`, pinned to a browser worker's megabyte by its
`.wasmstack` sidecar**, graded both ways: it passes against x86-64 under the
fix and dies `wasm trap: call stack exhausted` under the head revision
rebuilt. It exists because 23 of 23 subjects were green over this for as long
as it existed -- every recursion in the corpus, `deep-recursion-rt` included,
tails through an `if` or a `let` and not one through an `act` (L-CONSTRUCT,
fourth instance on this target). Suite now 24 of 24.

`build-output/page/` is untracked, so the shipped page carried the old module
until `build-page.ps1` was rerun on 2026-08-27; it now carries this fix,
anchor `5B4CADE2..`, and 1.83 has the pinned-stack table measured on it.

**1.92 -- FIXED, THE EMITTER'S DEPTH BAIL ANSWERED `0` INSTEAD OF REFUSING,
SO A DEEP ENOUGH EXPRESSION COMPILED TO A WRONG NUMBER** (fester,
2026-08-27; `codex/plugs/wasm/WasmEmitter.codex`). This is the landmine
1.82 recorded and left standing at `emit-wat-expr-at:746`, described there
as "held in check only by the fixed point".

`emit-wat-expr-at` bailed at `depth >= 256` with `"(i64.const 0)"`,
substituting the literal zero for the entire remaining subexpression. A
chain of 300 nested `let`s **prints 44 where x86-64 prints 300**, and it
assembles, runs and exits clean with no diagnostic on either side. It is a
wrong answer, not a refusal, and nothing anywhere reports it.

**The `let` is what makes the depth reachable, and that is the half worth
keeping.** Nested arithmetic cannot get there: it nests the EMITTED output
in step with the walk, and wat2wasm's own parser faults on folded
expressions somewhere between 200 and 250 (measured: 200 passes end to end,
250 dies `memory access out of bounds` inside wabt), so the module never
assembles and the truncation is never observed. A `let` recurses without
nesting what it emits -- `(local.set ...) <body>` is flat -- so the WAT
stays shallow and every tool downstream accepts the wrong answer. Two
guards of the same shape, and only the one whose output stays flat can be
reached (L-CONSTRUCT, fifth instance on this target: the corpus had no
subject nested past a handful).

**The sibling guard at `emit-wat-expr-tco:1458` is benign and was the model
for the wrong fix.** It bails at the same 256 into `emit-wat-expr ctx e`,
which emits correctly and RESETS depth to zero -- so the counter was never
bounding total recursion, and 256 at `:746` was not protecting a stack
budget it could not have been measuring.

Now `depth >= 4096` emits `(unreachable (; ... ;))`, the refusal idiom this
plug already uses for a partial application of a lambda. **4096 is above
anything the front end will hand it**: the parser's own 4096-call fuel
refuses this shape by 1300 nested `let`s and passes it at 1000, so the
backstop cannot be graded from source and is a backstop rather than a
limit callers meet.

Arm `codex/plugs/wasm/test/deep-nest-rt.codex`, graded both ways at 300:
**44 under the shipped plug, 300 under the fix**, against x86-64's 300.
Suite 27 of 27. R-COST: the bail is one comparison per expression node and
the cap moved a constant, so nothing allocates that did not before; the
raised ceiling costs emitter frames only on input the front end has already
refused.

**1.97 -- BOTH PLUGS REFUSE IT NOW (riscv half, reek 2026-08-27); what stays
OPEN is the design. A handler clause that captures a local OTHER than `resume`
cannot be compiled by the native plugs.** (blu, 2026-08-27, found while fixing
COMPILER-29.) Since main 19558 the IR-CCE wire lifts lambdas, so a parameterised
handler clause arrives as a partial application of `__lam_N` over its captures.
Both native plugs now FOLLOW that def: they take its body, strip `resume`, and
emit it as the handler over the remaining parameters. That works only when
`resume` is the sole capture, which is the shape the checker produces for an
ordinary clause. **A clause closing over an enclosing local produces a lifted def
with extra capture parameters, and there is nowhere to put them**: a handler is
installed in the effect-op table and called with the operation's arguments only,
so the plug cannot carry a closure to it. arm64 REFUSES with `[UNSUPPORTED]`
naming the op and the lifted def; riscv falls back to its pre-existing inline
emission, which is the older and quieter behaviour and should be brought to the
same refusal. Closing this properly means giving the effect-op table an
environment pointer, which is a design question and not a plug fix. No test is
pinned: the bed has no program of this shape, which is why it was never noticed.

**THE RISCV HALF IS DONE 2026-08-27 (reek), and the row's "no program of this
shape" is confirmed the hard way.** `rv-unwrap-clause-lambda` now computes
`lifted` and `followable` separately and refuses on lifted-and-not-followable,
which is `a64-unwrap-clause-lambda`'s test word for word, through a new
`rv-add-shadow-warning`. Not lifted at all is the ordinary clause and is
untouched: on an ordinary handler the emitted binary is byte-identical to the
pre-change one.

**The guard is proven wired and it is UNFIRED on anything in reach, and both
halves of that were measured.** Sabotaging the condition to fire on every
clause produces `[UNSUPPORTED] handler clause for ask ... cannot carry a
closure` on the guest console and `run.ps1` exits 6, so the report path is
real; restoring it returns the byte-identical binary. What could not be built
here is a program that takes the lifted path. `codex/test/effect-handler-clause.codex`
is the shape and does not compile at head (CDX2033 and CDX2031, which is what
its `.failing` file records), and a hand-written clause capturing an enclosing
local compiles and runs but arrives with `resume` as the apply head rather
than a `__lam_`, so `lifted` is False.

**That last measurement is a SECOND finding and it is the one with a live
reproducer.** A valid program whose clause captures an enclosing local
(`offset-by (n) = let r = with Reader ask / ask (resume) = resume (n + 1) in r`)
runs correctly on bare metal, answering 42. Through the plugs, on the same IR:
**arm64 REFUSES with `[UNSUPPORTED] n: the arm64 plug emits no such function,
and the branch would be left unpatched -- reaching it reads a stale x0` and
exits 6, while riscv emits 49,473 bytes and exits 0.** So the capture reaches
the handler as a free name, and the asymmetry on this shape is not the clause
path at all: it is that arm64 has an unresolved-name refusal and riscv does
not. That is a wider gap than this row and it is not closed here.

**1.98 -- CLOSED 2026-08-27. The runner exists (reek), it now SEES the two
bundles that motivated it and the gate runs it (blu). Plug bundles had no
deck-margin runner and the arm64 one had run out.** (blu, 2026-08-27.) `scaled-floor` derives a unit's deck room linearly
from source length; CHECK's cost is not linear in length, so a dense bundle can
reach zero margin with nothing reporting it. Measured: adding ONE field of type
`List IRDef` to `A64Extra` -- no new loop, no new call site -- refused the whole
plug with `CDX9002 Deck overflow in CHECK`. `List IRExpr` refused identically;
`List Text` fit, so it is the type pulled into the record and not the field
count. `codex/plugs/arm64/build.ps1` and `codex/plugs/riscv/build.ps1` now pass
`-Decks 140` through the new `Build-TranspilerPlug -Decks` parameter, and deck
scale is a reservation rather than an input to codegen (the arm64 plug is
byte-identical at 120 and 140, `2EC678CD7A88FBE0...`). **What is missing is the
runner:** `build/deck-headroom.ps1` asserts `-MinMargin` over `codex/build` and
the compiler's own unit, and no plug bundle is in its corpus, so the next plug
to run out finds out the way this one did. Note for whoever adds them: that
tool's `derived` column is NOT in the same units as `-Decks`, and reading it as
one sent me to `-Decks 96`, which is BELOW the derivation and moved the overflow
from CHECK to LOWER.

**HOW IT CLOSED, and the measurement is the point.** `-Plugs` mode measured each
bundle at the DERIVED scale, so the two bundles that pass `-Decks` were exactly
the two it could not answer for: at derived they overflow CHECK, write no deck
records, and land in reek's `NoDeckRecords` arm, which with `-MinMargin` would
have failed the gate for a scale nothing uses. The mode now reads each plug's
own `build.ps1` for its `-Decks` and measures at that, so the question asked is
the one the build asks. All 12 bundles measure, where 10 of 12 did before.
**That answer was worth having: at `-Decks 140` arm64 sat at margin 1.19 and
riscv at 1.21, both UNDER the 1.25 the gate asserts everywhere else, so the
number I picked while fixing COMPILER-29 was barely enough rather than
generous.** Both are `-Decks 160` now, giving 1.36 and 1.38 against a required
118 and 116, and the artifacts are byte-identical to the 140 builds
(`7D1E295992C46ACE`, `A41AC527ECFBB680`), which is the control that deck scale
is a reservation and not an input to codegen. The gate runs
`deck-headroom.ps1 -Plugs -MinMargin 1.25` beside the existing `codex/build`
arm. **41 plugs have no bundle on disk and 3 are stale; those are NAMED and
skipped, not measured quietly, so the corpus is 12 rather than 56 and the gate
covers whatever `plug-binary` built that run.**

**THE CORPUS EXISTS NOW 2026-08-27 (reek), and the two bundles this row is
about are the two it cannot answer for.** `build/deck-headroom.ps1 -Plugs`
takes the assembled `build-output/plug-source.codex` of every plug directory
with a `build.ps1`, which is the unit that overflows and which every other
mode here skips on purpose, since they walk individual chapters and exclude
`build-output`. Bundles are read off disk rather than rebuilt, because
rebuilding 56 of them to ask about deck room costs more than the question, so
a plug whose newest chapter is newer than its bundle is NAMED and skipped: a
stale bundle answers for the previous revision in either direction. **Not
wired into any gate; `build.ps1` runs this tool over `codex/build` and the
compiler's own unit and that is unchanged, since gate weight is red's
clearance.**

Measured over 52 bundles, all deriving from the FLOOR of 64 with nothing in
the linear band or the clamp, so the linear derivation this row names is not
even in play for a plug: the tightest margins are zig 2.46, csharp 3.56,
fortran 4.00, cobol 4.27, then wasm, python and javascript at 4.57. The
binding phase is CHECK-RESOLVE for 38 of them.

**arm64 and riscv are not in those 52 and the reason is the instrument.** Both
bundles compile through resolve and their measure logs carry no `DECK-N:phase=`
records at all, so line 260's `if ($decks.Count -eq 0) { continue }` dropped
them, and the summary asserted the whole remainder was "chapters that are not
entry points" -- which is a CAUSE the script does not establish and which is
false for these two. Each bundle has exactly one `opening`. The summary now
says `measured N of M` and lists what carried no deck records, so the two units
that motivated this row are visible as unmeasured instead of folded into a
sentence about something else.

**ANSWERED, and the answer is that the tool was blind to exactly the failure it
exists to predict.** At the derived scale both bundles refuse with
`CDX9002: Deck overflow in CHECK; deck floor exceeded`, and the overflow aborts
CHECK **before any DECK record is written**, so the measure log is empty. The
`-Measure` run reports neither the records nor the diagnostic: measured
2026-08-27, `compile.ps1 -Measure` on the arm64 bundle ends at
`PHASE-h-post-emit` with `EMIT-BYTES:0` and not one `error CDX` line, while the
same bundle compiled normally prints CDX9002 at once. riscv is identical. So a
unit with a margin BELOW 1 produced an empty log, and the tool skipped it and
passed: a check that stops asking reports exactly what one that asks and agrees
reports (L-CAPABILITY-LOST).

`-MinMargin` now FAILS on a unit with no deck records and names it, which is
the clause the tool's own header has always carried ("or when the kernel cannot
answer the question at all") and did not honor. Proven both ways: the plug list
exits 1 naming arm64 and riscv, and the gate's own corpus
(`-Quire codex\build -WithSelf -MinMargin 1.25`) still exits 0 at a tightest
margin of 1.33 over 59 units, so nothing in the gate changes colour.
**1.96 -- PLUG HALF DONE 2026-08-27 (reek); the upstream half is COMPILER-30.
The Ada and Fortran ErrorTy arms GUESSED a 64-bit integer, and the guess was a
silent miscompile on any non-integer value that reached them.** (Steve Howell's note "Zig as the demanding customer", 2026-08-27, via
Damian; the emitter arms verified against source by red: `AdaEmitter.codex:134`
answers `Long_Long_Integer`, `FortranEmitter.codex:148` answers `integer(8)`.)
His matrix's case f refutes the guess: a lambda parameter whose true type is
Text reaches these arms identically to an Integer one and both answer int64,
on a program the compiler reports clean. His incoming lambda-span fix removes
the COMMON producer of ErrorTy params but not these arms' behavior on the
ErrorTy that remains (his named residue: the ErrorTy atom is both the
type-failure atom and lower-let's no-expectation sentinel, so a plug cannot
tell "checker failed" from "nobody wrote it down"). The discriminator his note
states, worth keeping verbatim: did the checker compute an answer the IR
failed to carry? If yes, the fix is upstream in the compiler; if the program
genuinely constrains no answer, the work is the plug's. C# and Rust ERASE
(object / boxed-any) rather than guess and are not this row.

**His two compiler-side claims BOTH HOLD, verified against source and by
measurement (blu, 2026-08-27), and are filed as COMPILER-30 in
`codex/compiler/compiler-backlog.md`.** The overload is `IR/Lowering.codex:689`
and `:707`, where `lower-let` passes `ErrorTy` as the no-expectation argument;
`roc-fold-empty` carries `(param "xs" (list error))` on a lambda parameter while
the same name in that lambda's body carries `(list int-default)`, on a program
that compiles clean and prints its expected answer. So by his own discriminator
the fix is upstream and this row's arms are downstream of it: the guess is still
this row's to remove, but the ErrorTy reaching them is not this row's to fix.

**THE INSTRUMENT EXISTS: `build/ir-fidelity`, and it reports DROPPED on case f
today** (fester, 2026-08-27, against seed `0634584EF849D297`). It answers
Steve's question as a runnable arm rather than a finding re-derived by hand,
which is what his note asks for at the end: "making 'does the IR carry what the
checker knew' a standing property".

Each case is three programs and one wire position. `a` and `b` differ in one
respect and both compile clean; `knows` is a program the checker REFUSES with a
named diagnostic, which is what establishes that the checker distinguishes that
respect at all; `path` names the cell to compare. The verdict follows:
**CARRIED** (checker knows, cells differ), **DROPPED** (checker knows, cells
agree, so the fact is upstream), **UNCONSTRAINED** (the knows arm did not
refuse, so no claim either way), **UNSUPPORTED** (the reader could not locate
the cell). The last two are deliberately not passes, because a skip reported as
a pass is indistinguishable from a check that asks and agrees
(L-CAPABILITY-LOST). **The `knows` arm is the whole honesty of it**: it is
Steve's own discriminator mechanised, and without it a pair of agreeing cells
cannot be told from a checker that never knew the difference either.

The reader has no plug opinion in it and shares no code with
`codex/plugs/common/IRTextParser.codex`, which is itself under audit here and
normalises some of what the arm measures.

**The arm reads `-IrUni`, and that IS the wire the plugs consume.** This needed
establishing rather than assuming, because COMPILER-30 carries a note saying a
wire measurement must not be taken from `-IrUni` (the two paths diverged from
main 19558, since only the CDX path lifted lambdas). Measured 2026-08-27
against seed `4341370C8FE5BAD6`: after blu's lift unification at main 20176
they agree. The `-IrCce` bytes were aligned position-by-position against the
`-IrUni` characters for four programs and the map checked in both directions,
a clean bijection with zero inconsistencies, the discriminating case being the
lambda program COMPILER-12 is about, where both paths now emit the lifted
`__lam_0`. That note is corrected in COMPILER-30. A length match alone would
not have settled it and was not what was used.

Three cases stand today, all under `-Passes none`, which audits the sentence
the author wrote:

**RE-BASELINED at seed `4341370C8FE5BAD6` after PR 93 and blu's 20176 lift
unification. CASE F IS FIXED, and two other cases now carry DROPPED.** Seven
cases stand, all under `-Passes none`, which audits the sentence the author
wrote:

| case | verdict | the cell |
|---|---|---|
| `empty-list-element-type` | **DROPPED** | `(list-expr (elems) error)` in both arms |
| `bounded-int-derived-range` | **DROPPED** | `(int 0 10 ov-error)` in both arms |
| `lambda-param-type` | CARRIED | `text` against `int-default` (was DROPPED) |
| `lambda-param-arg-position` | CARRIED | `text` against `int-default` |
| `parametric-sum-pattern-binding` | CARRIED | `int-default` against `text` |
| `linear-param` | CARRIED | trailing `(unique "n")` present / absent |
| `effect-row` | CARRIED | `(fn int-default int-default (row ...))` against `(fn int-default int-default)` |

**Case f is closed and the arm is what says so.** The `let` binding now carries
`(fn text int-default)` where it carried `error`, and the lambda is lifted to
`__lam_0` carrying `(param "x" text)`. Both lambda cases flipped to CARRIED and
are kept as regression guards rather than deleted.

**The re-baseline was not a re-baseline until the reader was repointed, and
that distinction is the whole of L-INSTRUMENT.** blu's lift unification moved
in-body lambdas onto their own defs, so the arm's `find:lambda` path stopped
resolving and BOTH lambda cases reported UNSUPPORTED at head. UNSUPPORTED is not
CARRIED. Taking the report "case f now passes" and banking CARRIED off a reader
that had lost the cell would have produced precisely the check that stopped
asking (L-CAPABILITY-LOST). The repair is the one that lesson prescribes: point
at the part that still answers the question, `def:__lam_0/param/0`, never soften
the assertion. `-Grade` caught the same breakage in ablation A, which is what
that ablation is for.

**`empty-list-element-type` is Steve's item 2 and it is live.** `let xs = []`
whose element type is fixed by a later use emits `(let "xs" (list error))` and
`(list-expr (elems) error)` identically whether the use makes it Text or
Integer, while the USE in the same expression carries `(list text)` against
`(list int-default)`. This is also the standing runner for the `ErrorTy` atom
collision, since the `error` here means "nobody wrote it down" and not "the
checker failed".

**`bounded-int-derived-range` makes section 4's caveat measurable.** Declared
returns `0..20` and `0..30` both emit the body node as `(int 0 10 ov-error)`,
the operand type. The checker plainly computes the derived range: refusing a
too-narrow declaration, CDX2051 names it, "the value's proven range is 0..20".
The derivation exists and does not reach the wire.

**Cost, measured rather than estimated: about 0.5 s per compile, 3 compiles per
case, 15.5 s for the whole `-Grade` run** (reader self-test, three ablations,
seven cases) on this box at that seed. Re-measure before quoting it (L-COUNT);
this line has already moved twice as cases landed.

**RULED by Damian 2026-08-27: wire it into `-Internal`, and bank expectations as
MEASURED.** So a case records what is true today, `empty-list-element-type` and
`bounded-int-derived-range` sit at DROPPED with the gate green, and the phase
reds the moment any verdict MOVES in either direction. Fixing one of the two
upstream reds the gate and makes somebody re-baseline deliberately, which is
exactly what happened to case f here and is the behaviour being bought. The
alternative considered and rejected was banking the DESIRED verdict, which
leaves head red until the fix lands and trains the fleet to ignore the phase
(L-NOGATE). The wiring itself is a separate CL: `build.ps1` is generated from
`codex/build/buildScript.codex`, so it takes the generator, the shipped script
and a `check-generated-scripts` pass, and that is not this change (R-ONE).

`-Grade` runs the instrument against itself first, because an arm whose
verdicts have never been shown to fail is not evidence (L-FALSIF). The reader
round-trips a live wire rather than a banked fixture, and is graded by ablation
(dropping the last element of every list turns the round-trip red). Each
verdict path has an ablation aimed at it: a `knows` code that cannot fire falls
to UNCONSTRAINED even though the cells genuinely agree, an unlocatable path
reports UNSUPPORTED rather than agreement, and a pair read at a cell that
cannot carry its respect reports DROPPED.

Two corrections the arm produced on its first run, both re-measurements rather
than new work: **stage 3a of `IndependentRechecker.md` is BUILT** (linear
ownership rides a trailing `(unique ...)` field, effect rows ride a fourth slot
on the arrow, and the plug parser reads both back), where that design's section
4 recorded them as unrecheckable; and **`compile.ps1` exits 4 on a SUCCESSFUL
text or IR emit**, so in those modes the exit code cannot distinguish a clean
emit from a crash or a refusal. Both are written where they belong, in that
design's section 4 and in `OperatorsManual.md` above the compile-mode table.

**THE PLUG HALF IS DONE 2026-08-27 (reek). Both arms refuse instead of
guessing.** `ada-type` and `fort-type` answer an undeclared type naming the
cause, `cx_UNSUPPORTED_ErrorTy`, which is the same shape
`cx_UNSUPPORTED_builtin` already uses for expressions in both plugs: a name
the target compiler reports as undefined, at the site, rather than a plausible
integer that compiles. Fortran's stays a derived-type reference,
`type(cx_UNSUPPORTED_ErrorTy)`, so the refusal is syntactically valid and the
compiler names the undefined type instead of failing to parse somewhere else.

**A second guess sat one level in on the Ada side, and the measurement is what
found it.** `ada-list-type-name` picks between `Cx_Text_List` and
`Cx_Int_List` by asking whether the element renders as `Unbounded_String`, so
a list whose element the checker never resolved fell to `Cx_Int_List`. Ada
marked ONE program where Fortran marked three, and the asymmetry was that
arm; it now refuses too. Both plugs mark the same three.

**Measured over 57 subjects, emitted and counted (no toolchain: `gnat`,
`gnatmake`, `gcc` and `gfortran` are all absent from this box, so this is
verified as emitted shape and by which programs reach the arm, never as a
run):** 57 of 57 emit, and three carry the refusal.

| subject | ada | fortran |
|---|---|---|
| `roc-fold-empty` | 3 | 8 |
| `tcp-listen-reclaim` | 2 | 3 |
| `tcp-checksum-refuse` | 1 | 2 |

`roc-fold-empty` is this row's case f and is the positive control: it emits
`function __lam_0(Xs : Cx_Int_List; Base : Long_Long_Integer;
Step : cx_UNSUPPORTED_ErrorTy) return cx_UNSUPPORTED_ErrorTy`, where `Step`
is a FUNCTION being passed and had been reading `Long_Long_Integer`. Fortran's
`tcp-listen-reclaim` shows the other shape, an empty array constructor
`(/ type(cx_UNSUPPORTED_ErrorTy) :: /)` whose element type was an integer
guess. One subject reported an emit failure with an empty guest console and
re-ran clean; it is recorded because a transient that is not re-run is
indistinguishable from a finding.

**The three marked programs are the measure of the class**: they were
compiling to plausible Ada and Fortran with wrong types, and nothing said so.

Still open on this row and unchanged: the `ErrorTy` atom collision underneath
(the atom is both the type-failure atom and `lower-let`'s no-expectation
sentinel) means a plug cannot tell "the checker failed" from "nobody wrote it
down", so a refusal is now correct in both readings but says only that the
plug was given nothing. The arm makes that question decidable from outside the
plug, which is what it is for. **The upstream half is COMPILER-30**, and
`lambda-param-type` is a standing runner for it: the case flips from DROPPED
to CARRIED when that lands, without anybody having to re-derive the finding,
and the three programs above should stop carrying the refusal at the same
time.

**Not swept, deliberately:** `ada-type` and `fort-type` also answer a concrete
integer for `TypeVar` and for `FunTy`, which is the same shape of guess with a
different atom. That is a wider question than this row and no complainant has
appeared for it.

**1.95 -- `__self-type-defs` HAS A WASM FORM NOW, AND IT IS THE EMPTY LIST,
WHICH UNBLOCKED CDX MODE IN THE MODULE** (fester, 2026-08-27; PRISM-6 (a),
whose entry in `apps/prism/prism-backlog.md` carries the account).

The plug refused this name, so `emit_cdx` trapped at
`compile_frontend_cdx` -> `pmap_self_test` and the tab could not build a
binary to download. It is not a missing capability on this target, it is a
question about the HOST: `pmap-self-test` walks the running compiler's own
heap through the self-type table the x86-64 backend bakes in, so it measures
the process rather than the artifact. A host built by a backend that emits no
pointer map has no table and nothing to walk. The plug now answers with
`(call $list_with_capacity (i64.const 0))` -- an honest empty table over the
existing runtime helper, no new WAT -- and the compiler stands the self-test
down on an empty table rather than walking one.

**The compiler half is the load-bearing one and it is in the seed**: an empty
table answers -2, and `pmap-selftest-result` reports that as SKIPPED with its
own message rather than as the expected 3, because a skip reported as a pass
is indistinguishable from a check that asks and agrees (L-CAPABILITY-LOST).
Graded both ways: SKIPPED appears on wasm and not on x86-64, and x86-64 still
runs the walk and still passes.

**The bytes are right, not merely present.** One small program through the
module and through the x86-64 kernel gives a byte-identical 87,923-byte CDX
payload; CDX mode went from two newlines plus `wasm trap: unreachable` to
88,132 bytes. `build-page.ps1` carries the arm and refuses the page build
unless those payloads match, graded both ways against the module shipped
earlier the same day. R-COST: one `list-length` and one comparison per CDX
compile, and one 8-byte allocation where a trap used to be.

The refusal census is five now, not six: deep nesting, block device, process
table, partial application, and the `wat-no-such-thing` set. `apps/landing/web/compile/prism.html`
embeds a module too and is TRACKED, so it carries the old stack behaviour
until it is regenerated; that file is reek's and is not touched here.
## 1.90 -- arm64 compares a SUM's fields as raw words, so `==` is wrong for any field that is not a machine integer

**Found 2026-08-27 (blu) while re-establishing the arm64 baseline for
COMPILER-9, and it is a WRONG ANSWER rather than a refusal, which is why
nothing surfaced it for as long as it has existed.** `codex/test/recursive-eq`
compiles clean on arm64 and prints `ne` where `eq` is expected, on the first
of its eleven rows.

**Measured**, arm64 cross bed, `build/test-cross-batch.ps1 -Arch arm64`:
`recursive-eq  line 1: exp=[eq] act=[ne]`. That test is x86-64-correct on all
eleven rows against seed `555791DA1F39A810` (COMPILER-24, main 20018).

**The structural cause is read off the emitter, not inferred from the
symptom.** `a64-emit-sum-eq` (`codex/plugs/arm64/Arm64CodeGen.codex:1164`)
compares the tag with `arm64-cmp`, then loads each field with `arm64-ldr` at
`+8` and `+16` and compares it with `arm64-cmp` as well. There is no dispatch
on the FIELD's type anywhere in it: no `__str_eq` call for a Text field, no
call for a nested sum, no recursion. x86-64's inlining path calls
`emit-eq-op` per field (`emit-sum-fields-eq`) and therefore does dispatch.
So a field holding a POINTER is compared as a pointer, and two structurally
equal values at different addresses answer unequal.

**Three consequences. The first is measured; the other two are read off the
same lines and are NOT yet measured, so do not quote them as results.**

1. A field at a recursive sum compares by pointer -- the measured case.
2. **A `Text` field of ANY sum, recursive or not, should compare by pointer
   too**, so `Held "hi" == Held "hi"` is predicted `ne` on arm64 and is `eq`
   on x86-64. This is the one worth testing first: it needs no recursion and
   it is a divergence on an ordinary shape.
3. `a64-max-fields-for-type` caps the unroll, and the emitter has arms for
   0, 1 and 2-or-more fields where the last compares exactly fields at `+8`
   and `+16`, so **a constructor with four or more fields appears to compare
   only its first three**.

**Not fixed here, and the x86-64 repair does not carry over**: COMPILER-24
synthesises a per-sum helper as an ordinary `IRDef` inside the x86-64
emitter, so arm64 and riscv never see it. Answering (1) on arm64 means the
same synthesis on that plug or, better, lifting it to a shared IR pass where
all three targets get it at once. Answering (2) is smaller and independent:
dispatch the field compare on the field's type the way `emit-sum-fields-eq`
does. **riscv is UNMEASURED for all three.**

`codex/test/recursive-eq` carries a `.no-cross` sidecar naming this row, so
the cross bed skips it and the arm64 baseline is unmoved; pin the arm with
the fix, not before it.

**1.93 -- FIXED, THE PARSE-DECK INFLATION WAS `list-insert-at` NEVER GROWING
ITS CAPACITY, AND "2.4x" WAS A GROWTH RATE READ AS A CONSTANT** (fester,
2026-08-27; `codex/plugs/wasm/WasmEmitter.codex`).

`$list_insert_at` fills in place when `n < cap` and copies when it does not,
exactly as `$list_push` does. Its grow path allocated capacity `n + 1`. So a
list built by repeated insertion arrived at every call with `n == cap`, the
in-place path could never be taken, and each insertion copied the whole list
into a buffer with no room in it either. n insertions cost O(n^2) bytes on an
allocator that never frees. x86-64 doubles (`emit-list-insert-at-grow`:
`shl rax, 1`, floor 4) and grows in place by advancing the allocation
pointer, so the same source is linear there. The prose above the emitter said
this defence was already present and warned in terms about the O(n^2) it
would cost without it; the code below it had disabled the defence.

**PARSE deck, same five real units, both targets, re-measured today:**

| unit | KB | x86-64 | wasm before | wasm after | after / x86 |
|---|---|---|---|---|---|
| maui | 110 | 813,296 | 1,378,024 | 954,312 | 1.17 |
| elf | 233 | 1,271,352 | 5,622,606 | -- | -- |
| rust | 353 | 1,909,344 | 9,574,415 | 2,196,511 | 1.15 |
| arm64 | 804 | 4,238,552 | 26,256,380 | 4,661,148 | 1.10 |
| the compiler | 2,878 | 14,185,568 | 265,286,010 | 15,429,802 | **1.09** |

**The ratio was never 2.4. It rose 1.69, 4.42, 5.01, 6.19, 18.70 with unit
size, exponent about 1.6, and 2.4 is simply where somebody measured.** x86
over the identical five units is linear at about 5,000 deck bytes per KB of
source, which is the control that makes the curve a property of the target
rather than of the ladder (1.79 built padded ladders because real units of
different sizes are confounded; the confound is answered here by the second
arm rather than by the inputs). 249.9 MB leaves the compiler's self-compile.

**Output is unchanged.** Cleaned the way the page cleans it, before and after
are byte-identical at 2,460,178 chars, `6F0A41222301E7199ACF0BC7`, which is
1.83's anchor. The raw stream differs by exactly 2 bytes and both are inside
the filtered `WD:` lines, where `deck-usage=` lost a digit. Suite 26 of 26.

**How it was found, because three cheaper answers were wrong first.** The
counter recipe from 1.80 (counters after the local declarations, dump and
reset at `$phase_compact`) gives per-phase numbers once each dump is matched
to its phase by the `deck_ptr` it prints. It said allocation COUNT is flat at
1,890 to 2,335 per KB across a 26x size range and small-object BYTES flat at
79k to 91k per KB, both linear, while deck growth per KB rose 4x. **Linear
allocation under superlinear deck growth is what killed the volume theory,
and with it 1.80's standing residue that x86 must be eliding allocations wasm
performs.** It elides nothing. Three named suspects then died by measurement,
each of which reads plausibly and would have shipped as the cause: the deck
branch of `$list_push` moves `deck_ptr` without any `bump_alloc` a counter
can see, and contributes 0 bytes; `$list_push`'s copy path contributes
521,096 of 305,526,058, under one per cent; `$list_cons` copies whole lists
and is never called in the span at all, 0 bytes with the counter verified
present inside it. What named the real one was attribution rather than
suspicion: route each candidate helper's `bump_alloc` through a wrapper
taking the same size argument, which needs no call site's size expression
reproduced, and read the census. `$list_insert_at`, 250,118,256 bytes of
305,526,058 in the span, 82 per cent -- the same 82 per cent an independent
histogram had already attributed to allocations over 4 KB.

**Arm `insert-at-grow-rt`, graded both ways**, and the count in it is
measured rather than reasoned. Inserting AT the length is an append and
shifts nothing, so the arm measures the growth policy alone. **At 30,000
elements it passed under BOTH plugs and measured nothing**: the quadratic
form asks for about 3.6 GB and the host simply granted it. At 50,000 it asks
for about 10 GB, past what a 32-bit address space holds, and the head
revision rebuilt fails `memory fault at wasm address 0xffff0000 in linear
memory of size 0xffff0000` -- fault address equal to memory size, one byte
past the frontier (L-MECHANISM). The doubling form still asks under a
megabyte and agrees with x86-64. The first version of that arm is the lesson:
a threshold set where two behaviours differ IN PRINCIPLE, rather than where
they differ ON THIS BED, is a green arm that cannot fail.

No compiler change, no seed, no token.
## 1.91 -- arm64 implements `~` and `~0` on Reals as an exact `fcmp`, which is the wrong ALGORITHM, and the f64 arm passes by coincidence

**Found 2026-08-27 (blu), working COMPILER-9's class-B set.
`codex/test/ops/real-approx-equality` fails its three f32 lines on arm64 and
passes its two f64 lines.** The natural reading of that split is a width bug.
It is not, and acting on the width alone would fix two of the three failing
lines and leave the third, while leaving f64 wrong in a way this test cannot
see (L-GAP).

**What the operators MEAN, read off the x86-64 emitters** (`emit-approx-eq`
and `emit-approx-eq-exact`, `X86_64.codex:1724` and `:1749`): each operand is
mapped to a MONOTONIC ORDINAL by `float-to-ordinal-sized` (width-aware, eight
instructions), the two ordinals are subtracted, the absolute value taken, and
compared -- `~` is True within **4 ULPs**, `~0` within **0**. The ordinal
mapping is what makes `-0.0` and `+0.0` the same value, and the ULP tolerance
is what makes two values one ULP either side of zero compare equal.

**What arm64 does** (`Arm64CodeGen.codex:1383-1384`): both `IrApproxEq` and
`IrApproxEqExact` go to `a64-emit-real-comparison ... 1`, which is an
`fcmp-d` with the equality condition. That is exact IEEE equality at f64
width, with no dispatch on the operand's width -- while the ORDERING
operators thirty lines above do dispatch, on `a64-real-cmp-kind == 2`.

**So there are two defects stacked, and the measurement separates them.**
The f32 lines fail because an f32 bit pattern zero-extended in a 64-bit
register is read as an f64: `-0.0f` is `0x80000000`, which as an f64 is a
tiny denormal, not zero, so it compares unequal to `+0.0`. **The f64 lines
pass only because IEEE says `-0.0 == +0.0`, which happens to agree with the
ordinal answer for that one input.** An f64 `~` across a one-ULP straddle
would fail too, and no line in the test spells it.

**The fix is a port, and the port is NOT direct: two encoders are missing.**
`codex/foreword/core/Arm64Encoder.codex` has no `eor` at all, and `asr` only
in register form, so the x86 sequence (`sar 63` / `shr 1` / `xor` / `sub`)
cannot be transcribed. The formulation that needs only what exists is
`ord = b < 0 ? INT64_MIN - b : b`, built from `a64-emit-li`, `arm64-sub`,
`arm64-cmp-imm` and `arm64-csel`, which is the same mapping. For the f32 arm,
shifting the pattern left 32 and NOT shifting the ordinal back down is
cheaper than adding an immediate `asr`: one f32 ULP is then 2^32, so the
tolerance is `4 * 2^32` in a register rather than 4 as an immediate.
**`a64-alloc-temp` rotates a pool of FOUR registers** (the prose at
`Arm64CodeGen2.codex:110`), so a two-operand sequence of this length must
park each ordinal in a local the way `a64-emit-sum-eq` does, rather than hold
it in a temp.

**riscv is UNMEASURED.** Not attempted here; recorded so the next taker
starts from the algorithm rather than from the width.

## 1.92 -- FIXED: arm64 staged stack-passed call arguments into the rotating temp pool, so one slot could be destroyed before it was stored

**Found and fixed 2026-08-27 (blu), working COMPILER-9's class-B set; the
account and the bed measurement are in that row.**

`a64-alloc-temp` (`Arm64CodeGen.codex`) rotates FOUR registers,
`a64-x12 + int-mod (next-temp - a64-x12) 4`, so x12 through x15.
`a64-load-stack-args-to-scratch` (`Arm64CodeGen2.codex`) staged each
stack-passed argument into `a64-x10 + slot` by way of `a64-load-local`,
which allocates one of those temps. With four stack arguments the staging
registers are x10, x11, x12, x13, so slots 2 and 3 are pool registers, and
whenever the rotation lands on a slot already staged that slot is destroyed
before `a64-store-scratch-to-stack` writes it.

Read out of the emitted instructions rather than inferred:

```
mov x12, x15          slot 2 staged
ldr x12, [sp, #424]   the next rotating temp IS x12
mov x13, x12          slot 3, correct
str x12, [sp, #16]    slot 2 stored with slot 3's value
```

**Whether it fires depends only on where the rotating counter happens to
sit**, so one extra temp allocation anywhere earlier in the caller flips
it. That is why the reproducer's two arms differ by nothing but a literal
against a `let`-bound local in a nested call: materialising a literal costs
no temp and materialising a local costs one.

**It is SILENT.** A corrupted stack argument is a plausible integer, so the
callee runs and answers wrongly rather than faulting. In the renderer the
corrupted slot was a loop bound, so the loop stopped early and the picture
was simply missing geometry.

**The fix needs no encoder change and no seed.** `a64-load-local-into` is a
sibling of `a64-load-local` that loads into a CALLER-CHOSEN register, and
the staging loop uses it to load each argument straight into its scratch
register, allocating no temp at all.

**A latent limit of the same family is left unfixed on purpose (R-ONE):**
the scratch base is still `a64-x10 + slot`, so past six stack arguments,
which is more than fourteen parameters, staging runs into x16, x17 and x18
-- the intra-procedure-call and platform registers.

Reproducer with its controls: `docs/Test/Active/Arm64StackArgClobber.codex`.

## 1.99 -- the compile page carries 24 lenses, and the module behind each one is now graded

**The page shipped 5 text targets and 5 UI targets against 45 emitters in
the tree.** Fourteen text lenses are added: rust, go, java, kotlin, swift,
ruby, php, lua, haskell, ocaml, scala, elixir, cobol, fortran. Each needed
only a `<Plug>Stdio.codex` shim, the five-line transport half that
`codex/plugs/common/build-plug-wasm.ps1` bundles in place of the plug's
network entry, so the emitter itself is untouched and both transports stand.

**Nothing in the tree ever ran these modules.** `build-page.ps1` copies
whatever it finds in each plug's `build-output` and leaves a lens dark when
the file is absent, and no script calls `build-plug-wasm.ps1` at all, so the
chapter list for every module was typed by hand on a command line and lived
in no file. `codex/plugs/wasm/page-lens-test.ps1` is the runner: it compiles
one subject to IR against the seed, runs every lens module under wasmtime,
and records the chapter list per lens because there is nowhere else for it.

**THE VERDICT IS NOT EXIT 0 AND OUTPUT, AND THE CALIBRATION IS WHAT SAYS SO
(L-FALSIF).** Handed a file that is not IR at all, all 24 modules exit 0 and
print their prelude, because an empty parse is not an error in any of them.
The first version of this harness graded on exit code and output length and
reported 23 of 24 green on that garbage: a screen that cannot fail. The
verdict now counts how many of the SUBJECT's own definition names reach the
emitted text. Measured over `accumulator-corpus`, 29 names: boilerplate
reaches at most 4, the lowest real emission is cobol at 11, and the floor
sits at 7 between them. 24 of 24 answer on the real subject and 24 of 24 are
refused on the calibration input.

`-Calibrate` inverts the arms and is the only thing that makes a green here
worth reading. Run both.

**Two things it found on its first run.** `zig-stdio.wasm` was a rebuild
behind `ZigEmitter.codex` (the stale-module trap, and the staleness guard
had to be narrowed to the chapters a module is actually built from: the
plug's network chapter sits in the same directory and is bundled into
something else entirely). **The ELF lens is no longer dark (reek, 2026-08-29,
see 2.07).** It stayed dark for a reason that was never the plug: its wire is
a code/data/func-table payload, not a CDX, and nothing emitted that from a
browser. What was missing was a PRODUCER, and the page is one -- it takes the
x86-64 out of the CDX the way `build/cdx-to-pe.ps1` does, which needs no
compile mode and no seed. `extract-x86-output.ps1` remains dead and is a
different approach entirely: it asks the compiler for an `ELF` mode that has
never existed.

## 1.100 -- DONE 2026-08-31 (red): riscv real-to-int and `show` on a Real were silently wrong above 2^31, because `rv-fcvt-l-d` encoded the 32-bit instruction

**FIXED. `rv-fcvt-l-d` and `rv-fcvt-l-s` now pass `rs2 = 2`** in
`codex/foreword/core/RiscVEncoder.codex:355` and `:367`, selecting FCVT.L.D and
FCVT.L.S rather than the 32-bit FCVT.W.D and FCVT.W.S. Two fields, one value
each; the rest of the row below is the account as found.

**Verified with a control, not by re-running the arm that already passed.**
New test `codex/test/ops/real-to-int-wide`, placed in `ops` so the CROSS
battery grades it on all three backends rather than only x86, which is the arm
that was already right. Measured on QEMU: riscv now answers all nine lines
identically to x86-64 and arm64, `show 3000000000.5` included, which printed
`2147483647.` before. The discriminating run is the one with the fix REVERTED
and the plug rebuilt: `real-to-int-wide` is the only test that moves, failing
at `three-bil 2147483647` against an expected `3000000000`. So the test can
fail, and it fails for this reason.

**Four riscv `real-*` tests are red and were red before this change**, byte
identical across the fixed and reverted runs, so they are NOT this defect and
are not fixed by it: `real-approx-equality` (FAIL_OUTPUT, line 1, the f32
`-0 ~0 +0` row), `real-cert` (FAIL_OUTPUT, line 5, `inter signed by root`
answers False), `real-compare-negative` (FAIL_STARVED, 1 of 15 lines at the
ceiling, still incomplete alone) and `real-mode-fields` (FAIL_RUNTIME, no uart
output, still silent alone). Reported rather than waved through; unowned, and
none of them is diagnosed here.

**The test the encoder already had could not see this.**
`codex/test/riscv-encoder.codex` is 64 lines and covers no `fcvt` at all.

**Not seed-affecting, measured:** no file under `codex/compiler` cites
`RiscVEncoder`; only the riscv plug, its own tests and `RiscV32CEncoder` do.
Reachability, not directory (DevelopersRulebook 7). So this took no build
token.

---

*The account as found, kept because the reason it survived is the useful part:*

**Measured on QEMU 2026-08-31 (red), all three backends, same source.** Found
while reviewing Steve Howell's PR 100, which pointed at the region; the defect
is ours and predates it.

| probe | x86-64 | arm64 | riscv64 |
|---|---|---|---|
| `real-to-int 3000000000.0` | 3000000000 | 3000000000 | **2147483647** |
| `real-to-int 10000000000.0` | 10000000000 | 10000000000 | **2147483647** |
| `real-to-int 1000000000000.0` | 1000000000000 | 1000000000000 | **2147483647** |
| `real-to-int (-3000000000.0)` | -3000000000 | -3000000000 | **-2147483648** |
| `show 3000000000.5` | 3000000000.5 | -- | **2147483647.** |
| `show 10000000000.25` | 10000000000.25 | -- | **2147483647.** |

These are ordinary in-range i64 values, not edge cases. arm64 passed the
in-range battery clean; riscv clamps every magnitude above 2^31.

**Cause, one field in two lines of `codex/foreword/core/RiscVEncoder.codex`.**
Under `funct7 = 0x61` the RISC-V spec selects the destination width with `rs2`:
0 is FCVT.**W**.D (32-bit signed), 2 is FCVT.**L**.D (64-bit signed).

    :355  rv-fcvt-l-d (rd) (fs1) = rv-r-type #53 rd 1 fs1 0 #61
    :367  rv-fcvt-l-s (rd) (fs1) = rv-r-type #53 rd 1 fs1 0 #60

Both pass 0, so a function named `l-d` emits the 32-bit instruction, which
sign-extends its 32-bit result into the 64-bit register and saturates at INT32
bounds. That is exactly the numbers above. The int-to-float direction in the
same screen is correct and is the contrast that names the bug: `:352`
`rv-fcvt-d-l` and `:364` `rv-fcvt-s-l` both pass `rs2 = 2`.

**The blast radius is wider than the builtin**, because `RiscVRuntime.codex:262`
and `:321` use `rv-fcvt-l-d` in the double-to-string path, which is why `show`
on a Real is wrong as well as `real-to-int`.

**Why a green cross battery said nothing (L-CONSTRUCT).** The path is reachable
and ordinary user code takes it. What was absent was any INPUT that reached it:
nothing in the corpus calls `real-to-int` on a value above 2^31, and every
`show` of a Real in the corpus is small. A suite cannot distinguish "the
operator is correct" from "no fixture ever spelled a large one".

**Fix and its measurement.** `0` to `2` in both lines. It is under
`codex/foreword/`, so whether it is seed-affecting is a reachability
measurement rather than a directory question (DevelopersRulebook 7): measure
before assuming it takes a token. Keep the reproducers as the regression test
(L-REPRO) -- they are three probes, in this row's table, and they must run on
the CROSS battery rather than only on x86, since x86 is the arm that is
already right.

**Unowned. riscv is reek's close-out lane.**

## 2.00 -- the page's 57 examples are graded in the page's own module, which is a stricter bed than compile.ps1

**The dropdown was the only part of the page nothing could grade.** A visitor
picks an example and presses Compile, so an example that refuses is worse
than one that is absent, and the bed everyone reached for to check a new one
is `build/compile.ps1`, which is MORE GENEROUS than the page: it bundles the
whole foreword where the page's unit is FLAT. `cites Foreword chapter
MathLib` resolves there and fails here CDX3007, and dropping the cite only
moves it to CDX3002, because `math-mod` is a foreword function and not a
builtin. Two examples shipped green under compile.ps1 and refused on the
page; Damian found them, not a runner. `codex/plugs/wasm/page-example-test.ps1`
is the runner. **57 of 57 compile, all at decks=12.** The compile arm measured
9 s and 39 s in two runs an hour apart on 2026-08-28; the spread is other
lanes' VMs on a shared box, not the arm. Re-measure before quoting (L-COUNT).

**The ladder is DERIVED from `prism.html`, not restated.** The page climbs
`const DECKS = [12, 48, 125]` on CDX9002, so a runner grading at one fixed
reservation answers a question the page never asks: an example needing 48
reads as refused, and one needing more than the ladder's top reads as passing
at whatever the harness happened to say. The regex is a refusal if it fails
to match, because grading at a guess measures the harness instead of the page.

**The calibration is the half that makes 57 green mean anything (L-FALSIF).**
It mangles each subject's `Chapter:` header -- every source carries exactly
one, so the sabotage reaches the parser on every subject -- and requires all
57 to refuse, which they do, each at a line inside its own source rather than
at one shared early failure. **The first sabotage tried was trailing garbage
after the chapter and it moved NOTHING**: the compiler absorbs it and still
emits IR, so that arm would have graded nothing while passing (L-SABOTAGE).
The negative arm is a real one and reproduces the exact defect the runner
exists for: `greatest-common-divisor` with its `prelude` field emptied goes
red at `CDX3002: Undefined name: math-mod` with `hello-world` beside it
unmoved as the control.

**Wired into `build-page.ps1` (step 4c), both arms, seconds against a page
build measured in minutes.** That is not the standing gate and cannot be
(L-NOGATE): no gate phase reaches `apps/landing/build.ps1`. It is the path
that PRODUCES the artifact carrying the examples, which is where an artifact
arm belongs (L-ARTIFACT), and the build fails rather than shipping a dropdown
that refuses.

**Found on the first run: an example's `decks` field is read by nothing.**
`widget-box` declares 200 and compiles at 12; the page's ladder ignores the
field entirely and no other consumer exists in the tree. It reads as
load-bearing -- a maintainer would take 200 to mean the page reserves 200 for
it -- and it is embedded into `prism.html` with the rest of the examples. Not
swept here (R-ONE); it is a field to delete or to honour, and either is a
decision about the page rather than about this runner.


## 2.01 -- DONE 2026-08-28 (contributed by Steve Howell, PR 95; absorbed by root): the zig plug emitted its 37 KB runtime prelude ABOVE the program, so every emitted file opened on 813 identical lines

`emit-zig-chapter` built `zig-prelude & types-text & defs-text & zig-main`.
The prelude is 37,409 bytes of fixed runtime support -- the bump allocator and
its heap, the list and text builtins, the CCE tables, the deck -- byte
identical in every file the plug produces, and the transpiled program began
past line 840. It now comes LAST, behind `zig-postlude-banner`, which names
what is below the line and says why.

**The proportion is worse than it sounds.** In the plug's 589-program corpus
the smallest emitted program is 38,219 bytes of which 37,409 is prelude: the
program is 2% of its own file.

Two reasons beyond reading comfort. A diff between two emitted programs now
opens on what differs rather than on hundreds of identical lines; and the
arbitrary transpiled code, which is where bugs live, is what a reader meets
first.

**Inert, and graded rather than argued** (Steve's grading, over his 589-program
corpus; his log is `MEASURED-prelude-last.log` in his ladder repository): 589
graded, build outcome agrees 589, zig diagnostics agree 589, 202 ran both
ways, 198 output byte-identical, 4 identical but for source positions in a
panic backtrace (the move shifts them by construction; same exit status,
stdout, panic message and machine addresses), 0 disagreements. Zig does not
order declarations at container scope. The transpiled compiler was built both
ways and driven on one shared input: byte-identical output on all three
natives. Absorption verification on our side: the plug rebuilt at head, the
banner-anchored surface check green (97 names, all reserved), and the zig
oracle arm green -- recorded in the absorbing CL.

**It carries a repair it caused.** `build/check-zig-prelude-surface.ps1`
derived the prelude as the line-wise common PREFIX of several emitted
programs. With the prelude last that prefix is the emitted tuple types, and
the check does not fail -- it reports a smaller surface (5 names of 98, all
five already reserved) and passes: a green light over a check that had
stopped looking. Anchored on the banner instead, and the subjects' preludes
are now REQUIRED to agree rather than silently truncated to whatever they
share. It derives 97 where the prefix scan derived 98; the one it drops is
`d`, which was never a prelude name -- the prefix ran past the prelude into
`Tup4`'s comptime parameters and picked it up by accident.

**What it is not.** Not a fix; nothing was wrong. It is the small half of a
larger measurement: nothing uses the whole prelude. The greediest program in
the corpus reaches 55 of its 93 top-level declarations and the median far
fewer, so most of those 37 KB could be DROPPED per program rather than merely
moved. Moving it first is worth doing alone and puts the shaking change at
the same seam. (Steve's PR draft numbered this 1.99/1.100; renumbered to 2.01
at absorption, the register having reached 2.00.)

**Cross-host flap record (red, 2026-08-28): ptx/hello 'qemu produced nothing' once, in a standing gate at ~20543-era head, on a box running multiple lanes' VMs; the identical standalone leg immediately after answered 1,630 chars, exit 0. One occurrence, load-suspected, recorded per the phase's own rule rather than quieted; a second occurrence makes it a finding about the file-serial QEMU path under contention.**

## 2.02 -- the zig plug REFUSES a redundant match arm that the compiler now emits combined, so it refuses legal Codex

**Routed by root 2026-08-28 from Steve Howell's PR 96, which was closed as
already-fixed COMPILER-side (fester 20398).** The compiler half is
verified (blu); **the zig half is NOT, and that is the open work here.**

red gave C# the drop at 20352. zig has no such drop, so where the compiler
now emits a combined arm, the zig plug refuses a program that is legal
Codex and that every other lane accepts. Evidence is PR 96; read it before
measuring, and measure the zig arm rather than inferring it from the C#
one -- the two plugs took different routes to the same requirement.

**STEVE'S TO FINISH, not a lane's to lift (Damian, 2026-08-29):** *"leave the
zig plug work to Steve, its his bug to finish here, not a cross cutting plug
lift like we do sometimes."* It came in on his PR 96 and it stays with him. Do
not claim this row; it is not unowned work waiting for a free lane.

**MOSTLY DONE (Steve Howell, PR 103, absorbed by red 2026-08-31).**
`zig-arm-shadowed` drops a prong an earlier trivially-guarded arm already
names, so the nullary-constructor and literal shapes now emit. Verified end to
end rather than by reading the emitted text: the pre-fix plug refuses
`codex/test/ops/match-shadowed-arm` with exactly two `duplicate switch value`
errors, and the rebuilt plug emits zig that compiles under 0.16.0 and prints
the same five lines as the x86 arm.

**One shape is still open, and it is the reason this row is not closed.**
`zig-pat-switch-value` answers only for `list-length subs == 0`, but
`emit-zig-match-arm` also emits a BARE prong for a payload-carrying
constructor whose binders are all unused (`ZigEmitter.codex:2155` for one sub,
`:2158` for several). Two such arms on the same constructor still collide and
still refuse at zig. The failure direction is safe -- it under-drops, so
nothing is miscompiled -- and the fix is to widen `zig-pat-switch-value` to
mirror those two conditions.

Two notes for whoever takes that. The PR's prose claimed a guarded earlier arm
does not shadow; that is unreachable and was dropped at absorption, because
`emit-zig-match-arms` has exactly two call sites (`:2078`, `:3275`) and both
sit in the `else` of `zig-branches-guarded`. And `zig-arm-shadowed` scans
backward per arm, making emission O(n squared) in arm count with one Text
allocated per comparison; bounded and accepted, but if the widening makes it
hotter, render each arm's switch value once into a list rather than
re-rendering the earlier arm's on every comparison.

## 2.03 -- the riscv plug runs as a wasm module and its wire is byte-identical to bare metal; wat2wasm cannot assemble it

**BOTH MODULES LANDED: riscv main 20681, arm64 main 20697. THE WAT2WASM
CEILING IS CLEARED (reek, 2026-08-29) and both modules assemble in 0.2 s.**
Flipping `ship` is the next entry, not this one.

**ADDENDUM 2026-08-30 (red): "assembles in 0.2 s" is true of a NATIVE
wat2wasm and was not what bare `wat2wasm` resolved to on this box.**
`Get-Command wat2wasm` answered the npm wabt shim -- wabt compiled to
WebAssembly, interpreted by node -- and pwsh prefers its `.ps1` over any
native exe on a later PATH entry. On `riscv-stdio.wat` (2,135,965 bytes,
post-depth-fix) that shim spent 26+ CPU-minutes without finishing, measured
twice; wabt 1.0.41's native `wat2wasm.exe` assembles the same file in
0.08 s and the result loads and passes the page arms. The 0.2 s above
cannot be the npm shim (node boot alone exceeds it), so that measurement
was of a native binary. Native wabt is now installed at
`%LOCALAPPDATA%\Programs\wabt` (release tar verified against its published
SHA-256) and prepended to the USER Path; a shell started before that change
still resolves the npm shim, which is how a 22-minute page build read as
build cost on 2026-08-30.

**THE CAUSE RECORDED BELOW WAS WRONG, and this row's own baseline table is
what says so.** Blocker 1 was attributed to the match construct --
`emit-wat-match-body` and its TCO twin `emit-wat-branches-tco`,
`WasmEmitter.codex:1061`. Both twins were flattened to sibling blocks
(fester's shelf 20730) and the depth did not move: arm64 stayed at **312**,
and the deepest point measured `a64_emit_direct_call` carrying 154 `(if `
and not one `(block $_a`, which is a shape the match emitter cannot produce.

The third nesting site is **`IrIf`**, at `WasmEmitter.codex:767` and its TCO
twin, and neither shelf touched it. The table below measured "longest
consecutive **`else if`** chain against WAT depth" and an `else if` chain IS
`IrIf`; the correlation was recorded correctly and then read onto the other
construct. The same sibling-block repair applied to both `IrIf` sites:

| module | `else if` arms | depth before | depth after |
|---|---|---|---|
| compiler | 42 | 188 | **48** |
| riscv | 131 | 309 | **27** |
| arm64 | 152 | 312 | **24** |

**The criterion this row set is met in the form that inverts the ordering:**
arm64 has the MORE arms and the LOWER depth, so depth has stopped tracking
arm count. That is what the row asked for and it deliberately is not an
absolute landing depth, which the row was right to refuse to predict.

**Proven against an oracle that shares no code with the emitter under test.**
The bare-metal plug CDX is built from the backend's own chapters and never
sees `WasmEmitter`, so an emitter defect cannot move both arms together:
riscv **byte-identical for all 50,184 wire bytes**, arm64 **all 18,667**,
with a sabotage arm that fails at the corrupted byte. Beyond the boards, the
compiler module rebuilt by this emitter answers a CDX payload byte-identical
to x86-64 (89,315 bytes), 69 of 69 page examples compile, and all 45 lens
modules answer with 0 of 45 answering on non-IR.

**TWO TRAPS IN THE GRADING HARNESS, both of which read exactly like a broken
plug, and the second survived the first.** `<plug>/run.ps1` stamps an
**`IR-CCE`** mode header on whatever bytes it is handed and passes them
through. Given `IR-UNI` instead, the plug parses UTF-8 as CCE and dies
`!EXC=06` at a fixed RIP -- and BOTH native plugs die the same way, because
they share the parser, which reads as a common regression rather than as one
bad input. Then, with the encoding fixed, the wires still differed at byte 0:
`-Passes 'text-plug'` **REPLACES** the default pipeline rather than adding to
it, so passing it to one arm and not the other grades folded-and-inlined IR
against unfolded IR. Compile BOTH arms with the same passes, and give the
module IR-UNI while the metal plug gets IR-CCE. The metal capture is also not
the wire: a leading `0x01` marker from codex-vm, then the wire, then
FUNCMAP/WCET text. The harness is
`scratchpad/wire-identity.ps1`; it is worth rebuilding as a real runner,
because nothing in the tree grades the `irbytes` transport at all
(`page-lens-test.ps1` covers the text lenses only) -- L-NOGATE.

**What it closes.** `elf-bytes.wasm` has shipped built-but-dark since it
landed, `ship = false`, for the reason the manifest states: nothing could
emit the payload it reads. That payload is the native backends' wire,
`[4B code-len][4B data-len][4B func-count][code][data][func table]`. A riscv
module supplies it, so the page can go source -> `codex-compiler.wasm` ->
IR -> `riscv-stdio.wasm` -> wire -> `elf-bytes.wasm` -> ELF with no host in
the loop. That is a board kernel built in a browser.

**PROVEN, and the oracle is the point.** `rv-build-wire-output` had NO
callers before this -- its signature and its definition were its only two
mentions in the tree, the same L-UNCALLED shape as `rv-emit-closure-over-apply`
in the same plug -- so it is untested code that merely looks right. Graded
against the serial path on `factorial`: **byte-identical for all 50,085
bytes of the wire**, same header (code-len 47,328, data 528, 129 functions).
The metal capture is 52,489 bytes and the 2,404-byte gap is NOT a difference:
it is codex-vm's leading `0x01` marker plus 2,403 bytes of `FUNCMAP`/WCET
text the serial path prints AFTER the wire. Compare the wire, not the capture.

**Three pieces, none of which existed.** `PlugIrBytes.codex` is a third
transport: `PlugStdio` answers text and `PlugBytes` reads a payload, and a
native backend is neither -- IR text in, binary wire out. `RiscVStdio.codex`
is the wasm sibling of `RiscVPlug`, which cannot be used here because it
writes the wire through `port-out-byte` and x86 port I/O does not exist on
this target. `build-plug-wasm.ps1` gains the `irbytes` transport plus
`-WithLir`, `-CommonChapters` and `-Decks`, so a native backend gets what
its NETWORK build already gets.

**Blocker 1, and it is not mine: wabt 1.0.39's JS `wat2wasm` CANNOT
assemble this module.** It dies `RuntimeError: memory access out of bounds`
inside its own expression parser. **It is nesting, not size**, and the
measurement says so three ways: `riscv-stdio.wat` is 2.1 MB at max nesting
depth **309** and fails; `codex-compiler.wat` is 9.8 MB at depth **188** and
assembles at 4.6 times the size; a 1.02 MB `zig-stdio.wat` assembles in under
a second. The ceiling sits between 188 and 309. No native assembler is on this
box (`wasm-tools`, `wasm-as`, native `wat2wasm` all absent). The module was
proven anyway by running the `.wat` directly under wasmtime 45, which accepts
one -- enough to verify, not enough to ship, because the page fetches `.wasm`.
The fix is either flatter WAT from the emitter (fester speaks for
`WasmEmitter`) or a native assembler, which is a toolchain question for Damian
under R-SHELL.

**Blocker 2, closed: the bundle's IR compile needs `-Decks 160`.** Without it
it dies in `__alloc` at about 542 MB. The riscv NETWORK build has passed
`-Decks 160` all along; the wasm path had no way to say it, so
`wasm/run.ps1` gains a pass-through defaulting to 0, which leaves every
existing caller of that shared service unchanged.

**arm64 is the same shape and is not done.** It has `write-i32-le` in
`Arm64CodeGen.codex` and the pe plug already carries `Arm64PeWriter`, so
arm64 -> PE is the second chain once the assembler question is settled.

**ARM64 TOO, SAME SHAPE, SAME RESULT (reek, 2026-08-28).** `Arm64Stdio.codex`
is the sibling shim; the arm64 module's wire is **byte-identical to bare metal
on `factorial`, all 18,556 bytes**, header code-len 16,068 / data 528 / 111
functions. Two differences from riscv, both read off `Arm64Plug`'s own
dispatch rather than guessed: `a64-emit-module` takes a fifth argument, the
SMP flag the network entry reads from its mode string, and there is no mode
string here so it is False; and the state constructor is `make-a64-state`.
`a64-build-wire-output` already HAD callers, unlike riscv's, so it was
exercised code rather than dead. `write-binary` takes the byte list itself,
so the wasm path needs no `a64-wire-length` -- one fewer place for a derived
count to disagree with the bytes it describes.

**arm64 is the phone lane**, which is why it matters beyond boards: its wire
feeds `ElfWriter` for Linux and Android, and the pe plug already carries
`Arm64PeWriter`.

**SECOND DATA POINT ON THE CEILING, and it narrows the cause.** arm64's WAT is
2.26 MB at max nesting depth **312** and fails wat2wasm exactly as riscv's 309
does. Two independently written emitters landing at 309 and 312, either side of
nothing in particular, says the depth comes from a pattern they SHARE rather
than from anything plug-specific -- the obvious candidate is the long
instruction-dispatch chain both carry, where each `else if` nests one level in
the emitted wasm. Whoever flattens it should expect both modules to clear
together.

**THE SITE IS NAMED AND THE ACCEPTANCE ARM IS AGREED (fester, reek,
2026-08-28).** `WasmEmitter.codex:1061`, `emit-wat-match-body`: each arm
emits `(if (result i64) cond (then body) (else <THE NEXT ARM>))`, so the else
arm CONTAINS the rest of the chain and an N-arm dispatch nests N deep. Line
767 does the same for `IrIf`. The fix is sibling blocks under one outer
block -- `(block $try_i (br_if $try_i (i32.eqz cond_i)) (local.set $_r body_i)
(br $done))` -- which makes nesting a constant 2 whatever the arm count.
Plug-only, no seed, no token. fester's after the Prism image half; **do not
write around it in the plugs.**

**Pre-fix baseline, longest consecutive `else if` chain against WAT depth:**
compiler **42 -> 188** (assembles), riscv **131 -> 309** (fails), arm64
**152 -> 312** (fails). Monotonic and consistent with the site reading.

**THE PASS CRITERION IS NOT AN ABSOLUTE DEPTH, and this is the part to read
before grading the fix.** Subtracting chain from depth gives a base of 146,
178 and 160 across those three points -- it WANDERS, so a three-point fit
cannot predict a landing depth to within twenty and "riscv should land near
180" was over-precise (reek proposed it, fester corrected it). If riscv comes
back at 205 that is not a failed fix. **The discriminating measurement is
whether depth STOPS TRACKING ARM COUNT**: sibling blocks are constant-2
regardless of arms, so after the fix riscv (131 arms) and arm64 (152) should
NO LONGER differ in proportion to their chains. If they still do, the nesting
is coming from somewhere else and the site reading is wrong. That arm compares
the two modules to EACH OTHER rather than to an absolute, which is why a
line-count proxy is good enough for it.

## 2.04 -- DONE 2026-08-28 (contributed by Steve Howell, PR 98; absorbed by reek): the zig plug emitted its whole 37 KB prelude into every program, and now emits only the parts the program reaches

2.01 moved the prelude below the program and closed by naming this as the
larger half: "nothing uses the whole prelude ... most of those 37 KB could be
DROPPED per program rather than merely moved". This is that change.

`zig-prelude` was one 37 KB text. It is now `zig-prelude-parts`, a table of 96
named `ShakePart` rows, and `emit-zig-chapter` emits `shake-text` over the
parts the program reaches. Reachability is generic and lives in a new foreword
chapter, `codex/foreword/core/Shake.codex`: parts, roots, closure, input order
preserved. Nothing in it knows what a part is for, which is why it is written
once there rather than inside this emitter.

A part records its dependencies as `ShakeFrag` rather than as a second list:
`ShakeLit` is inert text, `ShakeUse` is text that is ALSO an edge, and the two
projections `shake-frag-text` and `shake-frag-uses` read the same list, so
they cannot drift apart. Writing another part's name IS depending on it.

**Measured here, not carried from the PR (L-COUNT).** Emitted bytes for five
subjects, control against fix, the control being the depot revision installed
and rebuilt rather than reasoned about:

| subject | before | after | saved |
|---|---|---|---|
| arithmetic | 41,714 | 21,716 | 47.9% |
| queue-test | 40,521 | 20,374 | 49.7% |
| osc-noise | 47,419 | 26,212 | 44.7% |
| cce-tier1 | 54,350 | 32,625 | 40.0% |
| sort-test | 43,421 | 21,526 | 50.4% |

227,425 bytes to 122,453, **46.2% off**, about 20 KB per program. Programs keep
33 to 48 of the 96 parts.

**Inert on behaviour, and the control is what says so.** 22 subjects graded by
running the emitted zig and comparing against the battery's own
`codex/test/*.expected`: 16 passed, 0 failed, 6 refused by zig. The SAME corpus
on the depot plug gives the same 16 and the same 6, with the same messages. The
six are pre-existing plug gaps that name themselves (`no emitter for bit-not`,
`atomic-store`, `atomic-load`, `__real_to_text`) plus two zig-level type
faults; none is an undeclared `cx_` identifier, which is what a dropped part
would look like. The grading harness was calibrated against the wrong
`.expected` first, and it failed every subject. Surface check green, zig oracle
green (55 values match x86-64).

**It carries a repair it caused, and the repair found a real defect.**
`build/check-zig-prelude-surface.ps1` required every emitted prelude to be
IDENTICAL, which a shaken prelude is not by design. The replacement is
stronger: every prelude must be a SUB-SELECTION of one known whole, in table
order, cursor landing exactly at the end, so reordering, duplication,
truncation and invention all fail a check that "they are all identical" never
tested. Deriving the surface from the parts table also exposed Finding 67: the
parameter regex read past `fn NAME` and dropped the name, so the check covered
22 of the prelude's 96 declarations and none of its 74 functions. `CxList` and
`CxFn1..CxFn4` are CamelCase like any Codex type name, so a program picking one
emitted a duplicate struct member and would not compile. Surface is 173 names
against 175 reserved.

**Not seed-affecting, measured rather than assumed.** `Shake` is a foreword
chapter, but the compiler unit is assembled by walking cites from
`codex/compiler`, and only `ZigEmitter` cites it. Built the unit: `ZigEmitter`
and `TextSearch` absent, `Foreword--Sort` present as the calibration.
Reachability, not directory (DevelopersRulebook 7).

PR 98's third file was `codex/compiler/IR/Lowering.codex`, deleting three
`is NoExpectTy` arms each shadowed by an identical `is ErrorTy | NoExpectTy`.
**Already landed as fester 20398** and the file has moved twice since under
blu's COMPILER-32, so that hunk is dropped here rather than reapplied.

## 2.05 -- DONE (contributed by Steve Howell, PR 99): the zig prelude's parts table told a reader not to hand-edit it, and hand-editing it is the only way to add a runtime helper

Row 2.04 landed the shaken prelude and carried a paragraph over from the
migration that produced it:

    GENERATED by the ladder's `shake_parts.py` and gated part by part:
    `shake-frag-text` of each list rebuilds its original chunk byte for byte.
    Do not hand-edit; edit the prelude source and regenerate.

**Both halves are unfollowable, and the instruction is the load-bearing half.**

The prelude source it names does not exist. 2.04 replaced `zig-prelude`'s
123-chunk text with `shake-text zig-prelude-parts zig-prelude-part-names`, so
the thing a reader is told to edit instead of this table is the table. And the
generator never lived in this tree: it is a script in the contributing ladder,
so no reader here could have run it even while it ran. It has since been
retired there, because it cannot parse the shape it produced: it looks for a
`zig-prelude : Text` built from quoted chunks and finds the `shake-text` call.

**The cost is not hypothetical; it was paid immediately.** Adding
`real-to-int` / `real-from-int` to the zig plug needs two new prelude parts,
and there is no route to that except editing this file. The note forbids the
only available action, and the reasonable reading, "then I am doing something
wrong", is the wrong conclusion.

The paragraph now says what is true: the migration happened once, the gate it
passed has been paid, this table is the source, a NEW part is written here by
hand and in what format, an EXISTING part still should not be touched because
its bytes are what the gate certified and nothing re-certifies them, and
`build/check-zig-prelude-surface.ps1` is what grades a hand-written part.

**Comment-only, and NOT re-measured here.** This edits a prose block in a Codex
chapter; it changes the plug's bundle fingerprint and cannot change a byte the
plug emits. The measurement behind that claim is a prior one rather than a new
one: a 22-line prose block in this same chapter moved the fingerprint
`1aba3c41196cb74e` -> `73dc2f1e8cd0ed81` and left all thirteen emitted `.zig`
files byte-identical (ladder JUSTIFICATIONS.md, "A prose block moves the plug
and not its output", 2026-08-25). Said plainly so nobody reads a fresh sweep
into it.

**Landed as red, 2026-08-31.** Placed at 2.05 in row order rather than at the
file's end, where the PR wrote it: the register gained 2.06 through 2.08 while
the PR was open.

## 2.06 -- the native backends answer a plausible wire on input that is not IR

Found while calibrating `page-wire-test.ps1` (reek, 2026-08-29). Handed
`this is not an IR chapter`, `riscv-stdio.wasm` answers 46,886 bytes and
`arm64-stdio.wasm` 15,737 bytes, both exit 0, where the real subject gives
50,184 and 18,667. The output does TRACK the input, so the modules are
reading it -- what is absent is a refusal.

This is L-BAILVALUE on a front door: a producer that answers rather than
refusing makes a caller unable to tell that anything went wrong, and the
page's board target would hand somebody a downloadable binary built from
whatever was in the box. The text lenses do refuse, which is what
`page-lens-test.ps1 -Calibrate` asserts across all 45 of them; these two are
the exception.

**Not fixed here, and deliberately not asserted on by the wire runner**, whose
calibration is a wire-byte sabotage instead. A runner that asserted a refusal
these modules do not make would be red from its first run and would be
switched off. The fix belongs with whoever owns `PlugIrBytes`: refuse an
input with no `IR-BEGIN`, the way `compile-plain` now refuses an unknown mode
(L-ACCEPTED), and only then can the calibration arm be tightened.

## 2.07 -- the ELF lens lights, with a kernel/usermode switch

Damian, 2026-08-29: *"we need a button then in the prism binary section
kernel/usermode switch and build it either way."* Both build.

**What was actually missing was a producer, not a plug.** `elf-bytes.wasm`
has built and run since it landed; its input is the native wire
`[4B code-len][4B data-len][4B func-count][code][data][functable]`, and
nothing emitted that from a browser. The page emits it now by taking the
x86-64 STRAIGHT OUT OF THE CDX, which is what `build/cdx-to-pe.ps1` has
always done host-side: text at CDX header offsets 168/176, rodata at
184/192. No compile mode, no seed, no compiler change. The `ELF` mode
`extract-x86-output.ps1` asks for is a different approach and is still dead.

**The payload now leads with a mode byte, as the pe plug's does.** 0 is the
existing bare-metal image; 1 is a user-mode ELF64 at the conventional Linux
base with an RX/RW split and no interpreter. An unknown mode is refused BY
NAME rather than absorbed by a fallback, because the thing that would look
like it worked is a downloadable binary (L-ACCEPTED).

| mode | answer |
|---|---|
| 0 kernel | ELF32 EXEC, machine 0x3, entry `0x100020` = bare-metal base + 32 |
| 1 usermode | ELF64 EXEC, machine 0x3E, entry `0x4000D0` = 0x400000 + 176 + 32 |
| 2, 7, 9 | `REFUSED unknown mode N` |

**`elf64-header-bytes`, `phdr-64` and `elf-linux-base-addr` had no caller in
any binary we ever shipped** -- L-UNCALLED, compiled into everything and
executed by nothing. Mode 1 is their first caller, so they were unproven
code until this arm ran, which is why the arms grade the entry ARITHMETIC
rather than the magic number.

**WHAT THE USERMODE FILE IS NOT, and this is the part to carry forward.** It
is a correct ELF64 container whose CODE is still what the backends emit for
bare metal: console and heap are device registers, not `write(2)` and
`mmap(2)`. It loads on Linux and stops at its first print. The hosted arms
are PrismDevEnvironment stage 5a and are compiler work, seed-affecting. The
switch does not pretend otherwise -- the pill's own title says so.

**Graded in two beds, deliberately.** `page-bytes-test.ps1` gains kernel and
usermode positives plus a mode refusal, grading the module. The workspace arm
gains an arm that drives the PAGE'S OWN `elfWire`, because the PowerShell arm
builds that framing a second time and two implementations of one contract
drift apart. Both grade class, machine and ENTRY rather than the magic
number: a builder wired to the wrong mode still answers a valid ELF, and
sabotaging the mode byte was checked to turn the usermode arm red while the
kernel arm stayed green. The page's guard against a CDX whose header
overstates a section has its own control, because that shape otherwise builds
clean and dies later -- `cdx-to-pe.ps1` records what that cost.

**Boards are one field short of this same chain.** `riscv-stdio.wasm` and
`arm64-stdio.wasm` already answer exactly the wire the ELF plug reads, so
source -> IR -> board module -> wire -> elf module is a board kernel built in
a browser. What stops it is that `ElfWriter` knows only `elf-machine-386` (3)
and `elf-machine-x86-64` (62) and hardcodes the value in each header builder,
so a riscv or arm64 wire would come back in an ELF claiming to be x86-64.
It needs `EM_RISCV` (243) and `EM_AARCH64` (183) and a machine parameter
threaded through `elf32-header-bytes-shdrs` and `elf64-header-bytes`. Small,
and not done here rather than shipping a mislabelled header.

## 2.08 -- boards reach the page: the riscv plug's own ELF writer gets its first caller

Damian, 2026-08-29, at the page: *"still not seeing where the boards are in
the prism ui at all."* They were nowhere, and the reason is findable.

**`RiscVElf.codex` is a complete RISC-V ELF64 writer with ZERO callers.**
`rv-build-elf` was, like `rv-build-wire-output` before it and
`elf64-header-bytes` this same day, written for a caller that did not exist --
L-UNCALLED three times over in one plug. Worse than uncalled: `RiscVElf` was
not in the module's chapter list at all, so the writer was not even compiled
into the module that would have used it. Nothing was broken and nothing was
connected.

**`RiscVStdio` takes a mode line now.** Default is the wire, unchanged; `ELF`
answers a RISC-V ELF64 from this plug's own writer, with no CDX and no elf
plug anywhere in the chain. Re-measured after the change: the wire is still
50,184 bytes and still byte-identical to bare metal (`page-wire-test.ps1`),
so the default path did not move.

**The entry is LOOKED UP, not assumed.** `rv-build-elf` adds an offset to the
load address and the emitted functions are not in source order, so entering at
offset zero enters whichever function happens to be laid down first. It
resolves `opening` through `rv-find-func-offset` and REFUSES by name when
there is none: a board kernel with no entry point is not a thing to hand
somebody, and the alternative is an ELF that jumps into the middle of an
unrelated function.

**What it honestly is.** ELF64, `EM_RISCV` 243, loaded at `0x80000000` -- the
RAM base `qemu-system-riscv64 -machine virt` uses and the SiFive one. The
per-board link and flash addresses for the nine named HAL boards are NOT in,
so the pill says "RISC-V kernel" rather than naming ESP32-C6 or FE310. Putting
a board's name on a file whose load address was not derived from that board's
memory map is the mislabelling this register keeps closing.

**ARM64 is a disabled pill carrying its reason, not an absence.** arm64 emits
the same wire riscv does and its PE writer lives in the pe plug, but there is
no `Arm64Elf` chapter anywhere in the tree. That is the next piece if boards
want a second architecture, and it is a straight port of `RiscVElf` with
`EM_AARCH64` 183.

**Graded, not just shipped.** The workspace arm gains a board arm driving the
page's own path, checking class, MACHINE and load address rather than the
magic number -- an ELF claiming x86-64 is exactly what the missing machine
field would have produced. Its control runs the DEFAULT mode and requires a
wire back, so a module that ignored the mode line and always built an ELF
cannot pass. arm64's module stays `ship = false`: shipping 271 KB the page has
no way to reach is a dark payload, which is what riscv was until today.

## 2.09 -- the in-tab board kernel BOOTS, and three defects had to go first

**Measured 2026-08-29 (reek): a kernel built through the browser chain boots
on `qemu-system-riscv64` and prints output byte-identical to
`codex/test/factorial.expected`, 248 chars, exact.** The chain is source ->
`codex-compiler.wasm` -> IR -> `riscv-stdio.wasm` -> ELF, with no host in it.

**FIRST, WHAT IS NOT NEW, because the first version of this row overclaimed.**
RV64 ELFs booting and printing correct output is the CROSS BATTERY's daily
work (`build/test-cross*.ps1`): it compiles `codex/test` to ARM64/RISC-V ELF
through the HOST-side builder (`codex/build/compileriscvScript.codex`) and
boots on Renode or QEMU against the `.expected` sidecars. What was missing was
only the in-browser half: `rv-build-elf`, this plug's OWN writer, had no
callers, so the artifact could not be produced without a host.

**THE `-m 1G` REQUIREMENT IS NOT A DISCOVERY EITHER.** `__start` sets the
stack to `0xBFFF0000`, inherited from the x86 memory map, so QEMU virt's
default 128 MiB leaves the stack outside RAM and the kernel hangs. That is
already written into the Renode board model: `tools/renode/codex/
codex-riscv64.repl` declares `dram ... 0x80000000, size: 0x40000000`, exactly
1 GiB. Reached from the other end and it agrees, which is the useful part.

    qemu-system-riscv64 -machine virt -m 1G -nographic -bios none -kernel kernel.elf

**THREE DEFECTS, ALL INVISIBLE FROM OUTSIDE.** Every one presented as a silent
hang with no output; the QEMU instruction trace is what separated them.

1. **An instruction INDEX used as a byte offset (mine).** `rv-record-func`
   stores `st.insn-count`; `rv-patch-calls-loop` multiplies by 4 to reach
   bytes. Passing it straight to `rv-build-elf` gave an entry a quarter of
   the way to the right instruction and, worse, an ODD address --
   `0x80002D83` -- which no RISC-V core will fetch.
2. **The wrong entry symbol (mine).** Entering at `opening` skips `__start`,
   the runtime init that establishes the stack and the heap pointer in S1.
   `cdx-to-pe.ps1`'s `-EntryStart` switch exists to avoid exactly this on the
   x86 side.
3. **A real layout defect in `RiscVElf`.** It mapped the text at
   `load-addr + text-start`, leaving the bottom 176 bytes of RAM unmapped;
   `qemu -bios none` enters at the RAM BASE regardless of the ELF entry and
   read `0x80000000: 0000 illegal`. The x86 builder in `ElfPlug` has always
   mapped file offset `text-start` TO `load-addr`. **The host-side riscv
   script proves the defect rather than contradicting it**: it carries the
   comment "Also produce a flat binary for -bios none (QEMU jumps to
   0x80000000 regardless of ELF entry)" -- it shipped a SECOND artifact to
   work around the same thing. Renode honours the ELF entry, so it never saw
   it. One ELF that boots under both is the right artifact for a download
   button, and `rv-build-elf` has one caller, so nothing else moves.
   Safe against the remap window, which constrains image SIZE against 16 MB
   and not these 176 bytes.

**The arm was too weak and is fixed.** It checked the entry was above the
load address, which the odd entry satisfied. It now checks 4-byte alignment,
that the entry lies INSIDE the text segment, and that the text maps at the
RAM base. Calibrated against the three kernels this row describes: the
unaligned one is rejected on alignment, the mis-mapped one on the RAM base,
and the booting one is accepted.

**BOARDS, HONESTLY.** The nine HAL chapters in `codex/boards` cannot run this:
FE310-G002 is RV32IMAC and ESP32-C6 is RV32IMC, while we emit RV64 -- `LD`
and `SD` do not exist on RV32 -- and `QemuVirtBoard` is AArch64, with the
remaining six Cortex-M or Cortex-A and no codegen at all. The board that
works is the SYNTHETIC RV64 platform the cross battery already uses.
`PrismDevEnvironment.md` stage 2e says ESP32-C6 and FE310 "have a real chain
through the riscv plug's own ELF writer"; that is wrong on ISA WIDTH, which
no link-address work fixes. FE310's SRAM base is `0x80000000`, the same
number as QEMU virt, which is the likely source of the claim. Reaching those
chips needs an RV32 mode: ELF32, 32-bit pointers, no doubleword ops.

## 2.10 -- the Windows .exe: the container is PROVEN, the blocker is measured and is shared with the Linux app

Damian, 2026-08-29: *"jump on that windows .exe work."* Findings first, because
they scope it precisely and two of them are cheap to re-derive wrongly.

**THE CONTAINER WORKS. A hand-built console PE32+ with a real kernel32 import
table runs on this box, prints, and exits 0.** Prototyped in PowerShell rather
than in the plug on purpose: iterating a header layout costs a second there and
a 40-second module rebuild in `PeWriter`. `scratchpad/pe-console-proto.ps1` is
the proven reference -- subsystem 3, PE32+ optional header, data directory [1]
IMPORT and [12] IAT, one import descriptor for `kernel32.dll`, ILT and IAT of
identical thunk arrays, a hint/name table, and `call [rip+disp32]` through the
IAT. Porting that to Codex is mechanical.

Two layout facts it cost time to learn, both measured:

- **A declared section that starts at `SizeOfImage` makes Windows refuse the
  image** with "not a valid application for this OS platform", which names
  nothing. I had declared 3 sections and filled 2.
- **`ImageBase` has a floor above `0x40000`.** Bases `0x10000`, `0x20000` and
  `0x40000` are all refused; `0x100000` runs. So the trick of choosing
  `ImageBase + textRva == 0x100000` to land our position-dependent code
  where it expects, with NO copy stub, does not work: the only 64K-aligned
  base that is accepted puts `.text` at `0x101000`, 4 KB high.

**A COPY STUB IS THEREFORE REQUIRED, AND IT WILL WORK.** Measured through
`VirtualAlloc`: a fixed allocation at **`0x100000` is GRANTED**. So the stub is
the same shape the UEFI path already uses (`pe-stub-alloc-low-pages` calls
AllocatePages with AllocateAddress at 0x7000 and 0x8000) -- allocate, copy text
and rodata, jump to the entry.

**AND HERE IS THE WALL, MEASURED RATHER THAN ARGUED. A fixed `VirtualAlloc` at
`0x8D40` and at `0x7000` FAILS with error 87.** The runtime keeps its state at
fixed addresses in the FIRST 64 KB -- the print descriptor at 36160..36192,
`deck-pos` 28720, `heap-hwm` 28728, `arena-base` 28696 -- and the first 64 KB
is exactly what Windows reserves and exactly Linux's `mmap_min_addr` (65536,
measured on the WSL box). Both walls sit in the same place and the whole
scratch region is behind them.

**HOW MUCH OF THE RUNTIME IS BEHIND THAT WALL: 59 OF 69.** Measured by
compiling a program whose entire body is one `print-line-uni`, extracting the
x86-64 text out of the CDX, and searching it for the little-endian encoding of
every fixed address `X86_64Boot.codex` declares below 65536. 59 are embedded;
the 10 absent (fork pool, NIC buffers, bivy save, handler table) are what makes
the scan a measurement rather than a match-everything -- it discriminates.

**THE 59-OF-69 FIGURE ABOVE IS A TRUE MEASUREMENT OF THE WRONG POPULATION, and
it is corrected rather than deleted because it is what put stage 5a in another
lane's queue as a large job.** It counted the constants embedded in a BARE-METAL
image, and the boot infrastructure is exactly what a hosted build never emits:
page tables, IDT, GDT, LAPIC, SMP trampolines, ATA, NIC, UEFI, the scheduler and
the process table are all in that 59. Classified instead by the emit function
that references each cell, an ordinary hosted program reaches ELEVEN, from 57
sites. L-ADJECTIVE asked of my own number: 59 was a count standing in for a
shape.

## 2.11 -- DONE 2026-08-29: a Codex program runs as a native Linux app AND a Windows .exe

Damian: *"we need those linux and windows executables to actually perform the
functions we build"*, then on a report that led with what did not work yet:
*"the part about making it run is the point, all other steps are meaningless
without that."*

**BOTH RUN. 60 of 60 grades pass across the two targets** -- 30 Console subjects
each, compiled to a static ELF64 and to a console PE32+, executed, and graded
against the SAME `.expected` sidecars the bare-metal battery uses. The oracle is
independent of both targets, so a match is agreement with bare metal rather than
with itself. `factorial` is 248 bytes of output on each, exact.

**Bare metal is untouched and checked, not assumed:** the binary the new
compiler emits for `factorial` is byte-identical to the depot seed's,
11DC247A94E1F7A7.

### What the shape turned out to be

One selector, `hosted-target` (0 bare, 1 Linux, 2 Windows), rather than a
Boolean plus a second flag, so a build cannot be half-hosted. Above it:

- **The cells move off the low addresses both systems reserve.** `st-cell` at 57
  sites. Linux puts the band at 128 KB, below the text, where it can never
  collide with a program of any size. Windows cannot have it there at all.
- **The print path funnels through ONE helper.** Hosted swaps `__serial_put` for
  a `write(2)` on Linux and a kernel32 `WriteFile` on Windows, and inherits the
  CCE conversion, the newline and the itoa above it unchanged.
- **`__start` gets a hosted arm** that skips every hardware structure.
- **Exit is `exit_group(2)` or `ExitProcess`.**

### Four Windows findings, each measured, each cheap to re-derive wrongly

**NO ImageBase LANDS OUR TEXT AT 1 MB.** The floor is real and the whole sweep
is refused: 0x10000, 0x20000, 0x40000, 0x50000, 0x60000, 0x80000, 0xC0000,
0xE0000 and 0xF0000 all give "not a valid application for this OS platform",
with 0x100000/textRva 0x1000 as the positive control that RUNS and prints. PE
wants a 64K-aligned base and a first section above the headers, so no pair sums
to 1 MB. Windows is therefore patched for its OWN load address (`st-load-base`,
1 MB + 0x2000) rather than copied down at startup, which is a constant rather
than a stub. `pe-console-proto.ps1` in the depot still carried the pre-refusal
comment claiming the trick works; running it is what settled it.

**THE CELLS CANNOT LIVE LOW ON WINDOWS EVEN THOUGH VirtualAlloc SAYS THEY CAN.**
A fixed request at 128 KB is GRANTED from inside pwsh and REFUSED from a freshly
loaded minimal image. Measuring the API from the wrong process says the opposite
of the truth; the Windows band goes to 0x50000000 instead.

**THE ARENA DOES NOT NEED A FIXED ADDRESS AND THE CELLS DO.** Every cell
reference is an absolute immediate the compiler baked in; the arena is reached
only through R10 and one cell. A fixed arena was refused at 0x600000 and again
at 0x60000000; letting the loader choose is granted first time.

**THE BARE-METAL STACK GUARD IS A MEMORY-MAP ASSUMPTION, NOT A STACK TEST.**
`emit-prologue` compares RSP against the deck cursor, which is only an overflow
test while the arena sits BELOW the stack. Windows hands back an arena wherever
it likes, and when that landed above the stack EVERY prologue trapped to
`__out_of_memory` on the first call -- an access violation, no output, and
nothing naming the cause. A hosted process already has a guard page from its
kernel, so the check is off for hosted rather than taught a second ordering.
This was the last defect and the one that read least like its cause.

### The instruments, and one of them found a defect in itself

`codex/plugs/elf/cdx-to-elf.ps1` and `codex/plugs/pe/cdx-to-pe-console.ps1`
relocate nothing; they declare the addresses the code was already built for. The
PE layout is shared with the compiler by fixed constants and the writer ASSERTS
every IAT slot against them, because a silent disagreement there is a call into
the wrong page rather than a diagnostic.

`codex/plugs/elf/hosted-elf-test.ps1` grades both targets and has a `-Calibrate`
arm that mangles every definition site of `opening` and requires all subjects to
refuse. **Calibration earned its place immediately:** the first version renamed
only the SIGNATURE line, and a subject that splits its signature from its body
still defined `opening`, compiled, and produced its oracle -- one false pass in
twelve, in the arm whose only job is to prove the harness can fail (L-FALSIF).

**A REFUSAL IGNORED IS AN ACCESS VIOLATION LATER (L-REFUSED).** The first
Windows entry ignored what VirtualAlloc answered and the next instruction stored
through the null. Each request is tested now and a refusal exits with its own
code, which is what turned the next two failures from mystery crashes into "90"
and "91" and located them in one run each.

### Scope, decided by what the source asks for

A subject naming Device, FileSystem, Network, Identity, Audio, Gpu, Media,
Concurrent, the GOP desk, `raw-mem`, `address-of`, the atomics or `read-line`
reaches a kernel service a user process does not have. Three that looked like
counterexamples were each the subject: `cap-audio` declares `[Console, Audio]`,
`cap-heap-poke-pure` pokes absolute 786432, `atomic-smoke` writes through
`address-of 0`. One that looked like a subject was MINE: `consistent-hash-balance`
crashed until the arena segment was sized to the bare-metal envelope instead of
64 MB, which is L-ARENA pointed at my own bed.

**WHERE THE LINUX BINARY HAS ACTUALLY RUN, AND WHAT IS ASSERTED RATHER THAN
MEASURED (Damian ruled 2026-08-29 that the assertion is enough and the bed time
is not worth it).** Measured: WSL2, kernel 6.6.87.2, every grade in this row.
Asserted: it runs on any x86-64 Linux. The basis is that the artifact has no
host surface to depend on -- `readelf` shows ET_EXEC, no INTERP, no dynamic
section, three PT_LOADs, and the only kernel services it uses are `write` and
`exit_group`. The one environmental knob suspected of constraining it does NOT:
with `vm.mmap_min_addr` raised to 262144, far above the cell base, it still
prints its oracle byte for byte, because a fixed PT_LOAD is placed by the ELF
loader and not by an mmap from userspace. That measurement also retired a false
claim this register and `X86_64Boot.codex` both carried, that mmap_min_addr was
the wall the Linux cell base clears. It is not; the base earns its place by
sitting BELOW the text, where it cannot collide however large the program grows.

The Windows binary has run on one machine. The PE disables ASLR and depends on
fixed addresses, so a host that forces relocation is the untested case there.
**Known limits, named rather than left to be discovered:** one write per byte on
both targets (staging was written first and withdrawn -- it makes correctness
depend on every caller bracketing its print, and the raw and itoa paths do not,
so an unbracketed stage is a wild store rather than a wrong character); no stdin;
and the Windows arena is 1 GB against bare metal's 3.
## 2.12 -- DONE 2026-08-29: the two hosted targets reach the PAGE, in Codex, from the page's own modules

The containers proved in 2.11 were PowerShell, which the page cannot run. Both
are ported to Codex now and built into the modules the site serves, so the
Binary tab can hand a visitor a Linux app or a Windows `.exe`.

**Proven end to end through the page's OWN modules**, driven exactly as the page
drives them (`codex-compiler.wasm` with the mode line, then the container
module): a flat unit compiles to a hosted CDX and both binaries RUN and print
the right answer. Not a claim about the Codex source -- the artifacts the site
ships were the ones executed.

`ElfStdio` mode 2 and `PeStdio` mode 3 both take the CDX WHOLE rather than the
unpacked wire the older modes take, because the hosted container needs the entry
offset out of the CDX header and the wire does not carry it. The plug parses the
header itself rather than trusting the caller to have done it.

**The ELF module is byte-identical to the PowerShell writer.** The PE module was
not, and the difference found two real defects plus one honest disagreement:

- **`pec-section` never wrote the Characteristics field**, so both sections had
  no flags at all -- not readable, not executable. The image loaded and died.
- **THE DLL NAME WENT THROUGH THE HINT/NAME SHAPE.** An imported FUNCTION is a
  two-byte hint then the name; a LIBRARY name is the bare string. Prepending a
  hint puts two zero bytes where the loader expects the first character, so the
  import resolved against an empty library name. Invisible in the file, fatal at
  load, and it surfaced only because the two writers were compared byte for byte
  rather than both being asked "does it run".
- The last byte of difference was `SizeOfInitializedData`, which the PowerShell
  prototype left zero and the Codex writer fills. The Codex one is right, so the
  prototype was corrected to match: the two agree byte for byte on both targets
  now, which keeps byte-identity usable as an instrument instead of a difference
  everyone learns to ignore.

**The page needed a rebuilt `codex-compiler.wasm` before any of this could
work**, because `hosted` and `hosted-windows` did not exist in the module the
site was serving. A hosted target is a different COMPILE, not a different
wrapper, so the mode goes in at the compiler and the container only packages
what comes out.

The two pills carry what is measured and what is asserted in their tooltips,
rather than in a doc nobody reading the page will open.
**The consequence for planning stands: the Windows .exe and the Linux app are
ONE piece of work, not two.** The container half is a day of plug work each and
both are understood; the relocation is shared, and it is now done.

## 2.10 -- the binary tab could not compile the compiler: bare `CDX` mode line, and a 4 KB payload-marker window

Two page bugs stacked (red, 2026-08-30). The binary tab's second compile
sent `CDX` with no `decks=` while the IR pass above it rides the ladder, so
a compiler-sized unit met the derived clamp and refused `CDX9002 Deck
overflow in SCOPE` -- 1.75's residue arriving at the published page; the
underlying wasm deck consumption question stays open there. And
`cdxPayload` searched only the first 4096 bytes for `SIZE:` where the
self-compile carries ~69 KB of warnings before the marker, so the decks fix
alone would have read as "no CDX payload came back". `Get-CdxPayload`
always searched the whole stream; the page comment claiming "same rule" was
false until now. Fixed: the CDX compile rides the ladder (bare `CDX` first
for small units, because an explicit scale below the compiler's own
derivation can fail silently, per opening.codex's 32-vs-33 record), and the
marker search is whole-stream. Proven: the wasm CDX is byte-identical to
the seed kernel's for the full 3,005,132-byte compiler source -- all
3,064,678 payload bytes at `decks=125` -- and the page arms are green
(CDX arm byte-identical, 69/69 examples).

Found while verifying, reported and not chased (red lane is elsewhere):

- **FIXED SINCE, and this entry is the record of what was wrong rather than
    of anything open: arm 12b is GREEN, measured 2026-09-01 (reek)** --
    `ELF kernel ELF32 entry 0x100020 (85,304 bytes); usermode ELF64 entry
    0x4000d0 (86,548 bytes); overstated-section control refused`, with the
    whole suite at ALL ARMS OK. The arm is not passing vacuously: it checks
    class, machine and the EXACT entry for both modes and requires a CDX
    overstating its text section to be refused. What follows is the original
    2026-08 symptom.
  - `page-workspace-arm.js` arm 12b was RED on this box. The elf plug refuses
  `payload 84791 shorter than its own header claims 21703180`, a shape
  `elfWire` cannot construct: its func-off equals the wire's own length by
  construction, so the strict `>` cannot fire on any wire the page built.
  The plug received something else; ~3.9 chars per byte fits a stringified
  Uint8Array. For whoever owns 2.07/2.08's bed.
- **FIXED 2026-09-01 (reek).** The same arm hard-required `riscv-stdio.wasm`
  at load (its embed list) while build-page.ps1 treats that module as optional
  ("ABSENT; its lens stays dark"), so a fresh workspace refused the whole suite
  before arm 1 with a `readFileSync` stack. The embed now RECORDS an absent
  module instead of throwing, names them once at start-up, and the `fetch` stub
  rejects a named module with `module <name> is not in build-output/page; run
  codex/plugs/wasm/build-page.ps1` rather than the bare "no network in the arm",
  which is true, useless, and exactly what a missing module looked like.
  **Measured both ways**: with the module present the suite is ALL ARMS OK;
  with `arm64-stdio.wasm` moved aside it loads, prints the NOTE, and arm 12d
  fails with the named message.
- build-page.ps1 has no incremental path: an HTML-only page edit pays the
  module rebuild, the x86 truth arm, 69 example compiles and the library
  volume -- 451 s with the native assembler, ~22 min before the 2.03
  addendum's fix.

## 2.11 -- emit the binary wasm encoding directly and retire the external assembler (Damian, 2026-08-30: approved, scheduled later)

The wasm plug emits text WAT and every build assembles it with wat2wasm,
an external tool (2.03's addendum records what the PATH's default cost).
Emitting the binary encoding directly deletes the dependency and the 9.6 MB
text intermediate from the module path. Unowned until Damian schedules it.

### Sized host-side before anyone starts it (reek, 2026-09-01)

**wat2wasm IS NOT THE COST, and the row's framing invites the wrong reason.**
Measured on the shipped module: **0.3 s** to assemble 10,296,178 bytes of WAT
into 1,223,592 bytes of module, against a **145 s** module phase. Two tenths of
one per cent. Retiring it buys SELF-SUFFICIENCY, which is the founding
principle and reason enough, but anyone selling it as a speed win is wrong
about where the time goes.

**The speed argument that IS real is the text intermediate, and it is
untested.** The WAT is **8.4x** the size of the binary it assembles to, and the
145 s is spent building and writing those bytes inside the VM. Emitting the
binary directly means constructing 1.2 MB instead of 10.3 MB. That could be
most of the phase or very little of it; run.ps1 runs TWO guest passes (source
to 16.8 MB of CCE IR via `compile.ps1 -Passes text-plug`, then IR to WAT
through the plug CDX) and nothing times them separately. **Time those two
passes before committing to this work**, because if the first pass dominates,
binary emission saves almost nothing and is a purity change only.

**The subset is bounded and enumerable, which is what makes either design
finite.** Censused from the shipped module's WAT: **85 distinct instruction
forms** plus about 24 structural forms (`block`, `loop`, `if`/`then`/`else`,
`br`, `br_if`, `return_call`, `call`, `call_indirect`, `func`, `type`, `table`,
`memory`, `global`, `export`, `import`, `data`, `elem`, `local`, `param`,
`result`, `select`, `drop`, `unreachable`). **No `br_table`, no exceptions, no
reference types beyond the single funcref table.** SIMD is `v128.load`/`store`,
`v128.bitselect`, `i64x2.bitmask` and the four shapes' arithmetic and compares.

**Two designs, and the choice is Damian's:**

- **(a) Emit binary from the emitter.** Deletes the text path outright and is
  the only one that can win back the 8.4x. Costs a structured module
  representation and an index-space resolver, because every reference in the
  emitter today is a NAME (`$foo`) and the binary format is indices.
- **(b) A WAT assembler in Codex.** Parses the subset above and emits binary,
  leaving the emitter alone. Far cheaper, testable in isolation, reusable by
  any other plug that emits WAT -- and keeps the 10.3 MB intermediate, so it
  buys the dependency and nothing else.

### The module both designs have to build, censused from the shipped WAT (reek, 2026-09-01)

**The envelope is small and fixed, which is the good news.** Measured on the
shipped `codex-compiler.wat`, 10,296,178 bytes:

| part | what is actually there |
|---|---|
| imports | 2 always (`fd_write`, `fd_read`), plus `blit_framebuf` and `on_key_import` conditionally |
| memory | one, exported `memory`, min 256 pages |
| globals | 9, all `i32`, 8 of them `mut` |
| types | 49, every one `(func (param i64 x k) (result i64))` -- arity is the only variable |
| table | one, `funcref`, with a single `elem` at offset 0 |
| exports | 4: `memory`, `__heap_reset`, `disk_reserve`, `_start` |
| functions | 5,857 defined |
| data | 1,786 segments, all `(data (i32.const N) "...")` |
| local types | three: `i32`, `i64`, `f64` |

**THE TRAP THAT WILL COST A DAY IF IT IS NOT WRITTEN DOWN FIRST: the function
index space BEGINS WITH THE IMPORTS.** `$fd_write` is func 0 and `$fd_read` is
func 1, and the conditional imports shift everything after them, so a
name-to-index resolver that numbers only the DEFINED functions puts every call
in the module off by the import count -- and two of those imports are
conditional, so the offset changes with the program. The text form hides this
completely: `(call $foo)` never mentions a number. Nothing in the emitter
currently thinks in indices at all.

**The type section is the easy part and should not be over-built.** Every
defined function is `(param i64 ...) (result i64)`; the 49 types are arities
0..48. `call_indirect` names them (`(type $fn1)`), which is the only place the
text refers to a type at all.

**The code section wants locals GROUPED**, as runs of (count, type). The
emitter declares them individually and by name (`(local $x i64)`, 37,670 of
them), so the grouping is a real transformation rather than a transcription,
and with only three local types the grouping is cheap.

**What is NOT in this module, and each absence removes a chunk of encoder:** no
`br_table`, no exceptions, no multi-value results, no reference types beyond
the single funcref table, no `start` section (`_start` is an export, not the
start function), no passive data segments, no multiple memories or tables.

**Either way the oracle already exists and is independent: `wat2wasm`'s own
output for every corpus subject.** Byte-identity is the strong form and may not
hold (LEB128 minimality and section ordering are encoder choices); behavioural
identity through `hosted-wasm-test.ps1` is the fallback and is the standard
this campaign already grades on. **Keep `wat2wasm` on the PATH as the control
after it stops being the producer** -- retiring the dependency and retiring the
oracle are different acts, and doing both at once removes the only instrument
that can say the new encoder is right.

## 2.13 -- DONE (contributed by Steve Howell, PR 105; absorbed by red 2026-08-31): the zig plug emits real-to-bits and bits-to-real

Both are a bare `mov-rr` on bare metal (`X86_64Builtins.codex:1726-1742`): the
machine holds a Real f64 as its own bits in a general register, so the value
and its bit pattern are the same sixty-four bits and the conversion is a
register move. Zig separates the two types and spells the identity `@bitCast`.
Total in both directions, so unlike a float-to-int conversion there is no range
to leave and nothing to guard, and NaN payloads and both signed zeroes survive
unchanged.

**Verified rather than reasoned.** All twelve expected bit patterns were
recomputed by hand from IEEE 754 before the run. The x86 arm answers them, and
the emitted zig compiles under 0.16.0 and prints the same twelve lines,
negative zero, a quiet NaN, both infinities and max-finite included. New test
`codex/test/ops/real-bitcast-f64`. `check-zig-prelude-surface.ps1` green at 98
parts / 177 reserved names, and the two new parts shake out of the four sample
programs that do not use them.

**TAKEN AS THE INCREMENT ONLY, and this is the part to know before reading the
PR.** PR 105 is stacked on PR 100 and its diff carries PR 100 in full. PR 100
is NOT landed: its `.expected` encodes x86's answer for NaN and overflow into
`codex/test/ops`, which `build/test-cross-batch.ps1` grades on arm64 and
riscv64 as well, and those two saturate where x86 answers the integer
indefinite. So the two bitcast rows here are anchored on `bits-to-real-approx`,
which exists at head, rather than on PR 100's `real-to-int` rows, which do not.
Nothing of PR 100 is in this row. The `cx_real_to_int` cross-reference in the
PR's prose was dropped for the same reason: it names a function this tree does
not have.

**There is no cross-battery hazard here, unlike PR 100.** All three backends
implement this pair as a register move, so the answers are
architecture-independent and the test is safe in `codex/test/ops`.

**A sibling test already exists and was deliberately not replaced.**
`codex/test/ops/real-bitcast` covers the same two builtins plus the f32 pair,
which the zig plug still refuses, so it cannot simply be superseded. It is also
a score-counting shape (`f64-bits: 5/5`) that does not say WHICH pattern broke,
where the new one prints the patterns. Whether they merge is a later question.

**Steve declined the other thirteen Real builtins on purpose, and the reason is
a real question for us.** `ZigEmitter.codex:342` and `:373` map `RealTy (w) (m)`
to `f64`, discarding both the width and the overflow mode, so in this plug an
f32 Real is an f64, a trapping Real does not trap and a saturating one does not
saturate. Filling those rows would replace an honest refusal with a plausible
wrong number. Left open deliberately; it wants a representation decision rather
than an emitter row.

## 2.14 -- the wasm plug on the hosted corpus: 54 of 60 to 60 of 60, the four defects, and why 60 is not the corpus

*Cite this by subject, not by number: this file carries TWO entries numbered
2.10 and two numbered 2.11, so a bare "2.11" resolves to either the hosted
lift or the binary-encoding row.*

The hosted x86-64 lift grades 60 of 60 (the first 2.11 above). The wasm plug
had never been run over that corpus. It is now: **54 pass, 6 fail of the same
60 subjects**, and the six reds are **four defects**, not six -- the count
flattens a shape, which is the thing to plan off (L-ADJECTIVE).

**Both sides were measured on the SAME kernel rather than compared against a
recorded number (L-COUNT).** The hosted arm's 60 of 60 was recorded 2026-08-29
against a compiler that has since moved, so it was re-run here: **120 pass, 0
fail** over linux+windows, 60 subjects each, on seed 2B69CDD246E7EE23. The
figure holds at head, and it is now a measurement rather than a citation.

The instrument is `codex/plugs/wasm/hosted-wasm-test.ps1`, graded against the
same `.expected` sidecars the bare-metal battery uses, so a match is agreement
with bare metal rather than with itself. **The corpus is not restated in it.**
`codex/plugs/elf/hosted-elf-test.ps1` owns the selection rule and now answers
`-ListSubjects`; a second copy is a set kept equal by hand in two places and is
silent when it drifts, which would end exactly the comparability the score
exists for.

Kernel `seed/Codex.cdx` (2B69CDD246E7EE23), which is the kernel the plug is
built against; the plug was rebuilt first, because `WasmEmitter.codex` moved in
the 2026-08-31 merge-down and a plug binary older than its source is a
confident wrong answer in either direction (L-SAMEVER).

### The calibration, and what it does NOT establish

`-Calibrate` mangles each subject's `opening` and requires the subject to fail
to produce its oracle: **60 of 60 refuse.** That number alone would be worth
little, because a sabotage that fails UPSTREAM of the graded step proves
nothing about the graded step. Measured instead of assumed:

| calibrate arm | count |
|---|---|
| emitted WAT (the plug did NOT refuse) | 60 of 60 |
| assembled, and RAN under wasmtime | 54 |
| produced its oracle anyway | 0 |

So the full path -- plug, wat2wasm, wasmtime, compare -- is exercised for every
subject that can reach it, and the refusal is not coming from the plug. Of the
6 that did not assemble under mangling, 5 are the standing defects below;
`board-types` assembles unmangled and passes the grade arm, so its calibrate
refusal is a mangle artifact and not a finding.

### The four defects

**1. A shadowed `let` aliases one wasm local, so the inner binding survives the
arm.** `act-let-scope` is the only red that assembles and runs; it prints
`arm-local: 41` against the oracle's `23` and every other line agrees. The
subject binds `v = n`, then `w = if n > 0 then (let v = n * 10 in v + 1) else v`,
then answers `w + v`. At n=2 the oracle is 21 + 2. **41 is 21 + 20**, so the
outer `v` was read back as the inner one. `locals-add`
(`WasmEmitter.codex:493-495`) returns the list unchanged when the sanitized name
is already present, so two distinct bindings of `v` share one wasm local and the
inner `local.set` is still live after the arm. The arithmetic and the dedup both
point one way, but neither is a cause until a fix moves the symptom
(L-MECHANISM): the failing print is the test.

**2. The `~` operator reinterprets its operands to `f64` and then compares them
with `i64.eq`.** `approx-eq` is refused by wat2wasm: `type mismatch in i64.eq,
expected [i64, i64] but got [... f64, f64]`. The emitted form for `1.0 ~ 1.0` is
`(i64.eq (f64.reinterpret_i64 ...) (f64.reinterpret_i64 ...))`, so the operand
conversion is right and the comparison is the integer one. Note the arm is wrong
twice over and the type error is only the half wat2wasm can see: `~` is
APPROXIMATE equality, so even a well-typed `f64.eq` here would be an exact
compare with no tolerance, and `1.0 ~ 1.0` would still pass. Same shape as the
`negate`-on-`Real` class that shipped on all three native lanes (L-CONSTRUCT):
an operator taking the integer path for a real operand.

**3. A `Real` builtin has no arm at all and reaches the funcref path.**
`bacnet-encode`: `undefined local variable "$real_approx_to_bits"`. This is the
failure mode `wasm-e2e.ps1` already documents -- a builtin with no arm is
treated as a value, so it emits against an undeclared local and no `(call $...)`
scan can see it. wat2wasm is the census; a grep is not.

Defects 2 and 3 are both `Real` surface and may share a root. Not asserted:
nothing here has measured that, and a mechanism that explains two symptoms is
the easiest thing in this project to believe.

**4. The wasm prelude names a builtin helper without the `__` the compiler
gives it, so a Codex function of the same name collides with it.**
`db-csv-roundtrip`, `db-full-test` and `db-row-update` -- three subjects, ONE
defect -- are refused with `redefinition of function "$text_compare"`.

The chain, each link checked rather than inferred:

- `text-compare` is a BUILTIN (`Types/Builtins.codex:83`) and the x86-64
  compiler lowers it to a helper named **`__text_compare`**, two underscores.
- `apps/data/Row.codex:294` also defines an ordinary Codex function called
  `text-compare` (with `text-compare-loop` beside it), and the three db
  subjects cite `Data chapter Row`.
- On x86-64 the two therefore have different symbols and coexist, which is why
  the hosted arm grades 120 of 120 over the same corpus.
- The wasm plug names its prelude helper **`$text_compare`**, one underscore
  short (`WasmEmitter.codex:2068`), and its builtin arm calls that name
  (`:1484`). `wat-sanitize` maps the user's `text-compare` to the same
  `$text_compare`. Two definitions, one name, and wat2wasm refuses the module.

The WAT shows both: `:515` is the prelude's (i32 locals, byte loop, beside
`$char_at`) and `:1319` is the user's (i64 locals, `$_rp`/`$_tv`,
`return_call $text_compare_loop`), sitting among `$val_compare` and `$col_def`
where the program's own functions are.

So this is not "a prelude helper a program happens to shadow", which is how it
first read. It is the plug diverging from the compiler's own naming convention:
every prelude helper standing in for a builtin should carry the `__` prefix the
compiler already reserves, and any that does not is one user definition away
from the same refusal.

### Two things the census did NOT find, recorded so they are not re-derived

**`hosted-kind` is hard-coded to 1 and no consumer can currently tell.**
`WasmEmitter.codex:1009` answers 1, which in the compiler's own convention means
hosted LINUX (`X86_64State.codex:120`, "between 0 and 2": 0 bare, 1 Linux, 2
Windows). Every consumer in the tree tests `/= 0` only -- `bp-present`
(`BootPaint.codex:48`) and two sites in `PhaseAllocator.codex` -- so the wrong
value is inert today and the first consumer that distinguishes 1 from 2 inherits
it as data. Whether wasm gets its own value is a compiler call, not a plug one:
the declared range is 0..2.

**`__self-type-defs` answers an empty list** (`WasmEmitter.codex:1004`), so the
pmap self-test reads SKIPPED rather than passing. A guard that ANSWERS instead
of refusing cannot be seen to have fired by any caller or any test
(L-BAILVALUE); it is why that arm is silent rather than red.

### The re-measurement found a trap, and it now refuses instead of reporting

`hosted-elf-test.ps1` defaults to `build\output\Sut.cdx`, which is whatever this
workspace built last. Run that way at head it reported `windows arithmetic exit
1342177280` -- a 62,976-byte `.exe` that was produced, RAN, printed nothing and
exited 0x50000000, with an empty stderr and no diagnostic anywhere. It reads
exactly like a codegen regression in the lift.

It is not one. Main 20822 changed `cdx-to-pe-console.ps1`, `PeWriter.codex` and
the x86-64 emit chapters in ONE changelist with a new seed, so the container and
the compiler that fills it move together. The workspace `Sut.cdx`
[B47056219FFEDC23] predates it; against `seed\Codex.cdx` [2B69CDD246E7EE23] the
same subject passes. New container, old compiler, and the skew announces itself
nowhere (L-SAMEVER).

The depot revision of the harness fails identically, so the edit in this CL is
not the cause; that control was run before anything else was believed. The
harness now REFUSES when the kernel it is about to use is older than
`cdx-to-elf.ps1`, `cdx-to-pe-console.ps1` or `PeWriter.codex`, rather than
grading and reporting a red. Calibrated both ways: it refuses the stale
`Sut.cdx` naming the file, and passes the current seed.

### Running it

```powershell
codex\plugs\wasm\build.ps1                              # the plug binary, first
codex\plugs\wasm\hosted-wasm-test.ps1 -Jobs 4           # 54 of 60
codex\plugs\wasm\hosted-wasm-test.ps1 -Jobs 4 -Calibrate # 60 of 60 refuse
```

`-Jobs 4` is the standing parallelism for this box. A single subject is
`-Subject <name>`. Each arm boots two guests per subject (the IR compile and
the plug), so both are long; neither was timed, and a duration is not quoted
here rather than guessed.

### Step 2, first pass: two of the four closed, 54 of 60 to 58 of 60

Both fixes are in `WasmEmitter.codex` and neither needed a new subject: the
census is the runner, and each fix had to MOVE it (L-MECHANISM).

**Defect 4, the `$text_compare` collision, is closed by aligning with the
compiler's own name.** The prelude helper is `$__text_compare` and the builtin
arm calls that, which is what `Types/Builtins.codex:83` already says the helper
is called. `apps/data/Row.codex`'s ordinary `text-compare` now sanitizes to a
name nothing else claims. Three db subjects went green.

**Defect 2, `~` and `~0`, is closed by implementing the x86-64 semantics rather
than repairing the type error.** `f64.eq` would have type-checked and been
wrong: `emit-approx-eq` converts each operand to its IEEE-754 total ORDINAL,
takes the absolute difference and compares it UNSIGNED against a tolerance in
ULPs, 4 for `~` and 0 for `~0` (`cmp-ri 4` / `cmp-ri 0` under `setcc cc-be`).
The new `$__approx_eq` prelude helper is that transform, and the two arms pass
the tolerance rather than an instruction.

**The corpus cannot tell those two apart and that is worth saying**, because
the green does not prove as much as it looks. `approx-eq` only ever compares
equal values and values a whole integer apart, so exact equality passes all six
of its checks. The evidence for the ULP form is the x86-64 emitter it was read
off, not the subject. A subject with operands one to five ULPs apart is the one
that would divide them, and it does not exist in this tree.

### The other two are NOT mine, and the reason is worth recording

Agreed with red, 2026-08-31: Steve Howell's open PR 111 carries Real support
including the `IrNumLit` f64-into-i64 row, and the shadowed `let` is his issue
113. Neither is duplicated here. (Superseded 2026-09-01: the Real family landed
here first in the same representation PR 111 chose, so its Real commit was
dropped as a duplicate when red absorbed the rest of 111 and 112 as 2.18.)

**The blocker under both was measured from this end before that was known, and
it stands as a positive control for the absorb.** Every local slot the plug
declares is `i64`, and `WasmEmitter.codex:760` emits a real LITERAL as
`(f64.reinterpret_i64 (i64.const N))`, which is an f64 value. So a real that
passes through a local is a type error, and the four-line probe

```
  sum-real : Real -> Real
  sum-real (a) = let x = a in let y = 2.25 in x + y
```

is refused outright: `type mismatch in local.set, expected [i64] but got
[f64]`. Not a subtlety, and not reachable from the census: **nothing in the
60-subject corpus binds a real to a `let`**, which is why 58 of them pass over
a plug in which reals and locals do not agree on a representation
(L-CONSTRUCT, the fixture shape the corpus lacks). If 111's absorb is right,
that probe assembles and prints `sum PASS`.

That is also why the `real-to-bits` / `to-real-approx` family is left
unimplemented here rather than filled in. Arms for it were written, built and
measured, and then BACKED OUT: no single arm can be type-correct while a real
literal is `f64` and a real in a local is `i64`, so each one is only right for
the operand kind it was tested against. Leaving the builtin absent fails in
wat2wasm naming the missing builtin, at assembly time; an `(unreachable)` arm
would have moved the same failure to a runtime trap with the explanation
stripped out of the binary. The louder failure is the better one until the
representation is settled.

### Step 2 closed: 60 of 60, and what that number does NOT mean

All four defects from the list above are fixed. GRADE is 60 pass 0 fail;
CALIBRATE refuses 60 of 60 and 59 of them now reach wasmtime rather than dying
upstream of the graded step, up from 54 when the census was first written.

**THE DENOMINATOR IS THE CAP, NOT THE CORPUS, WHICH IS EXACTLY WHY IT READS AS
COMPLETE.** `hosted-elf-test.ps1` globs `codex/test/*.codex` NON-recursively
(`:63`) and takes `-First $Max` (`:66`) from a default `-Max = 60` (`:14`). So
the harness selects sixty subjects and then reports "60 of 60", and a score
whose numerator and denominator are both the cap looks finished at any cap.

Measured 2026-08-31 at reek 20872, by asking the SELECTOR rather than the
directory (`-ListSubjects -Max 100000`): the eligible population is **383**.
572 top-level subjects carry a `.expected`, 189 of them are excluded by design
as unreachable to a user process, and 383 is what is left. A further **44** sit
under `codex/test/ops/` where a non-recursive glob cannot reach them at any
`-Max`. The sixty selected run `act-let-scope` to `dtls-openssl-fragments`:
**the run never gets past the letter D.**

Both agents who measured this got the denominator wrong on the first pass, in
the same direction, by counting files that carry an oracle (572) instead of
files the harness would consider (383). The exclusion filter runs BEFORE the
cap, so the raw alphabetical 60th is not the selected 60th either. Ask the
selector, not the directory.

This is true of the hosted x86-64 arm in the same words, because its published
60 of 60 is the SAME sixty. The two arms are comparable, which is the whole
point of deriving one corpus from the other, and neither is the corpus.
Widening the harness and re-measuring both is the next row, and it is expected
to find more.

`codex/test/ops/real-approx-negate` is the proof that this is not pedantry: it
exists, x86-64 and the cross battery grade it, and nothing has ever run it
through this plug.

### The two defects closed here

**Defect 1, the shadowed `let`.** A local slot is now per BINDING rather than
per name. `ctx.shadow` carries one entry per binding in scope, the binding at
depth d owns `name` at 0 and `name_shD` above it, and a read takes the
innermost. The declaring walk and the emitting walk are separate and are NOT
asked to agree: the collector allocates the next free slot per binding
occurrence, giving K slots for K bindings of a name in a function, and emission
indexes by scope depth, which is bounded by that count. Emission can only name
a slot the collector declared, and an over-declared slot is an unused wasm
local rather than a wrong answer. `act-let-scope` answers 23.

**Defect 3, the `Real` representation, which is the one that was worth the
detour.** A real is now its f64 BITS in an i64 slot everywhere, which is what
every local declaration and `wat-eq-field-cmp` already assumed and what the
x86-64 emitter does. `IrNumLit` was the odd one out, emitting an f64 VALUE, so
a real that passed through a local was a TYPE ERROR rather than a wrong answer:
`let y = 2.25` was refused outright, and real arithmetic worked only while
every operand was a literal. Changed with it: real arithmetic and the four
ordering comparisons reinterpret in and out, `$__approx_eq` takes i64, and the
`real-to-bits` / `to-real-approx` family is implementable at all now that the
operand kind is not a function of which expression produced it.

**`IrNegate` emitted an INTEGER two's-complement negation for a `Real`**, found
while doing the above. That is the `negate`-on-Real class fester fixed on
x86-64, riscv and arm64 at 18612 and 18629, still live here. It is not
L-CONSTRUCT's missing fixture this time: `codex/test/ops/real-approx-negate` is
exactly that fixture, it exists, and this plug's corpus cannot reach the
directory it lives in. A fixture that exists and is UNREACHABLE reads identical
to one that was never written.

### The trap under all of it: `__record-set` does not copy

**It overwrites the field and returns the SAME record**, so extending a context
with it hands the callee's state to every caller up the stack. The shadowing
fix was written twice and produced BYTE-IDENTICAL WAT both times, because the
inner `let`'s extension ran while the outer `let`'s value was still being
emitted; by the time the outer built its body context the shared record already
carried the inner binding, and the enclosing body resolved to the inner slot.
Building a fresh `WasmCtx` literal fixed it in one build.

Two independent confirmations on the same day, which is the argument for
writing it down rather than for either account: red's review of Steve Howell's
PR 111 flags the same leak in its own `emit-wat-guard-test`, repaired by PR 112
building the record explicitly, so 111 must not land without 112 (they landed
together as 2.18, with the fix applied inline as `ctx-deeper`).

**The general rule for this emitter: extend a context by constructing a record,
never by `__record-set`.** `ctx-with` exists for that.


## 2.18 shadow-stack deletion -- DONE 2026-09-01 (reek, taking red's handoff)

COMPILER-38 uniquifies binders in lowering, so the wasm plug's private repair
for shadowed `let` is dead code and root's ruling deletes each plug's private
repair in the same arc. red measured the compiler half, handed the plug half
over, and this closes it.

Deleted: the `shadow` field on `WasmCtx`, `count-occurrences` and its loop,
`wat-shadow-slot`, `shadow-push` and its loop, `locals-add-shadow` and its loop,
and the three prose blocks describing the mechanism. `ctx-with` survives as a
two-argument function because the per-function context still needs it. Both
`IrLet` emitters and `emit-wat-name` are back to the pre-shadow shape, taken
from `@20880` rather than reconstructed by hand.

**Ordering mattered and was honoured (L-FALLBACK).** The plug builds against
`seed/Codex.cdx`, so the repair could not come out until the COMPILER-38 seed
was in the depot. It landed as `DE664C4E` at main 20995 and the deletion
followed it.

**Verified against a before-baseline measured on the SAME seed**, so the
comparison is not against a moving target: `ops/*` plus `act-let-scope` was
27 pass 14 fail before and is 27 pass 14 fail after, same subjects, same
messages. `act-let-scope` passes and answers `arm-local: 23`, which is the
value the shadow stack existed to produce, with `shadowed: 100` and
`shadowed-after: 100` beside it.

**And the deletion is shown to be REACHED, not merely present in the source:**
the emitted wat for that subject carries ZERO `_sh` slots. A green suite over
unchanged output would have looked the same if the code had never been
rebuilt, so the slot count is what distinguishes "the compiler now carries
this" from "nothing happened".
## 2.17 -- OPEN (reek, 2026-09-01): the HOSTED x86-64 lift mis-renders constructor names, and it is the arm the wasm campaign grades against

Found while diagnosing `ops/real-mode-fields`, which 2.16 recorded as "red on
BOTH arms" with the x86-64 side exiting 0xC0000005. The crash is real and it is
the smaller half.

**The same 12-line chapter on three targets, measured 2026-09-01 at seed
FFA89CACFBB00F8F:**

| target | output |
|---|---|
| bare metal (`test-run.ps1`) | `Zebra 7`, correct |
| hosted Windows, PE console | `Z 7`, **exit 0** |
| hosted Linux, ELF under WSL | `Ze` then `e` forever, 304 MB before it was killed |

```
Chapter: CtorName
Section: Shape
  Beast =
   | Zebra (Integer)
   | Antelope (Integer) (Integer)
Section: Entry
  opening : Beast
  opening = Zebra 7
```

**Both hosted containers are wrong and they are wrong DIFFERENTLY, while bare
metal is right, so the defect is in the hosted codegen path the two share
(`-RawFlags hosted` / `hosted-windows`) rather than in the PE or ELF wrapper.**
Stopping after one character and running away without stopping are the two ends
of the same mistake: a text read with a length that is not the name's. The
integer field and the space around it render correctly in both, so only the
constructor NAME is affected.

A second, separate defect sits behind the original crash: a `Real` FIELD inside
a constructor faults. `Just1 (Real)` returning `Just1 1.5` prints `J 1` and then
exits 0xC0000005, while a bare `opening : Real` answers `1.5` correctly, so
showing a real is fine and showing a real *inside a constructor* is not.

**Why this is worth a row rather than a note.** The hosted x86-64 arm is the
CONTROL this campaign grades wasm parity against, and 2.16 used it to move a
count from 23 to 22. A control with a silent wrong answer of its own is the
instrument-built-from-its-subject failure one level over: for any subject whose
`opening` returns a constructor, "x86-64 passes and wasm does not" was never a
safe reading. It survived 39 of 40 only because few subjects return one.

Not taken here: this is compiler source and therefore seed-affecting, token and
gate, and `WasmEmitter` was claimed by red for the PR 111/112 rebase at the time
it was found. Unowned. The reproducers above are the whole instrument.

### 2.17 LOCALIZED 2026-09-01 (reek): `emit-print-text-no-newline` prints ONE character on hosted targets

Not a constructor defect at all. Constructor names were the symptom; the site is
one function, and it has its own control sitting beside it in the same file.

`X86_64IO.codex` holds two printers that are line-for-line identical except that
`emit-print-text` sends a newline through `__serial_put` before `__print_flush`
and `emit-print-text-no-newline` flushes straight away. On hosted targets the
first prints whole text and the second prints exactly one character.

| program | bare metal | hosted Windows |
|---|---|---|
| `opening : Text = "Zebra"` (uses `emit-print-text`) | `Zebra` | `Zebra`, correct |
| `Wrap (Text) (Text)`, `opening = Wrap "hello" "world"` | `Wrap hello world` | `W h` then 0xC0000005 |
| `Zebra (Integer)`, `opening = Zebra 7` | `Zebra 7` | `Z 7`, exit 0 |

Both the constructor NAME (`X86_64Chapter.codex:263`) and every Text FIELD
(`:308`) go through the no-newline printer, and both truncate to one character.
The integer field, the separator space (`emit-serial-wait-and-send`) and
ordinary `print-line` all render correctly, which is what makes it survivable
and invisible.

**What it is NOT, each ruled out by measurement rather than by argument.** Not
the data: the literal's 8-byte length prefix is **5** in BOTH builds, verified
by finding the CCE bytes `64 13 32 21 15` in each CDX and reading the qword
before them (bare `0x15024`, hosted `0xE194`). Not the IR: hosted and bare IR
are byte-identical, 17 lines each, both non-empty. Not text literals generally,
not `&` concatenation, and not the CCE-to-Unicode table, all of which print
correctly on hosted through the same `emit-print-text-loop`.

So the fault is in the flush, not the walk: buffered characters are not counted
or not written when `__print_flush` runs without a preceding `__serial_put`.
Hosted Linux fails differently on the same code (`Ze` then `e` forever), which
is the same accounting read the other way.

**Reproducers are three chapters of a dozen lines each, above.** The control is
`emit-print-text` in the same file: any fix must leave it printing whole text
and make its twin agree.

### RETRACTION 2026-09-01 (reek): the localization above is WRONG

**`emit-print-text-no-newline` is not broken.** Measured directly, which is what
should have been done before publishing it: `print-uni "hello"` routes through
that exact function and prints `hello` correctly on hosted Windows, matching
bare metal. The claim that the no-newline printer truncates is false and the
CL that carried it (main 20974) is wrong on that point.

The reading that produced it was a code path traced by eye, with a real
measurement attached to a DIFFERENT claim (the two functions differ only by a
newline, which is true). That is L-MECHANISM exactly, and this row had already
been warned by it in this same file, one section up: name the line your
mechanism runs through, and grep it. The discriminating test was one 8-line
chapter and it refutes the mechanism outright.

**What still stands, all of it measured:**

- Bare metal renders `Wrap hello world` and `Zebra 7`; hosted renders `W h`
  (then 0xC0000005) and `Z 7` (exit 0).
- The literal's length prefix is 5 in BOTH CDX builds, so the data is right.
- Hosted and bare IR are byte-identical.
- Ordinary `print-line`, `print-uni`, `&` concatenation and the CCE table all
  print whole text on hosted.

**So the defect is scoped by CONTEXT, not by function.** Every text print
INSIDE the entry-point's sum printer (`emit-opening-print-sum`) emits exactly
one character, whether it is the constructor name or a Text field, while the
same printers called from user code and from the plain-Text entry are correct.

**CORRECTION to the falsification below, found by re-reading my own test.** The
field-count test does NOT discriminate frame overrun, and calling it a
falsification was wrong. If the frame is already overrun at the FIRST print the
symptom saturates, so one field and two fields truncate identically under both
the overrun hypothesis and its negation. A test whose two arms agree under both
hypotheses measures nothing.

**The test that does discriminate is frame SIZE**, and it was run: the same
zero-field constructor returned from a body carrying sixteen `let` bindings
still answers `S` on hosted. Frame pressure varies by an order of magnitude
across that pair and the symptom does not move, so frame overrun is now
falsified on an axis that actually varies the frame.

**A frame-overrun hypothesis was raised and then falsified before publishing**,
which is the only reason it appears here. It predicted the damage would grow
with the number of locals, so the test was one Text field against two. It does
not move: `Only (Text)` answers `O h` and `Wrap (Text) (Text)` answers `W h`,
both truncating to one character and both faulting. Frame pressure differs
between them and the symptom does not, so the cause is not the local count.

**What the three measurements actually bound.** The constructor NAME truncates
to one character in every case. An INTEGER field prints correctly and does not
fault. A TEXT field truncates to one character AND is followed by 0xC0000005,
with one field and with two alike. So there are plausibly two faults here, and
what they share is only the context: the entry point's sum printer. Nothing
below that is established, and this row deliberately stops here rather than
name a third mechanism.

**CORRECTION: "the 0xC0000005 requires a TEXT field" is WRONG.** I published
that from three samples that all returned the FIRST constructor. Varying the
returned constructor breaks it: a zero-field constructor at index 1 or 2 faults
too. The matrix, all hosted Windows on seed DE664C4E, bare metal correct in
every row:

| returned | fields | exit | name |
|---|---|---|---|
| index 0 | none | 0 | truncated |
| index 0 | Integer | 0 | truncated |
| index 0 | Text | 0xC0000005 | truncated |
| index 1 | none | 0xC0000005 | truncated |
| index 2 | none | 0xC0000005 | truncated |

So the fault has TWO independent triggers, a Text field OR a constructor index
above zero, and the truncation is present in every row regardless of either.
That a sample of three agreed with the wrong rule is the point: every one of
them returned the first constructor, so the index was a variable nobody varied.

**Also falsified: the length is not the arm index.** `Triplet` at index 2 emits
one character, not three, so an index-plus-one reading of the length is out.

**A zero-field constructor settles that the truncation needs no field**
(measured 2026-09-01): `Box = | Solo | Duo`, `opening = Solo` answers `Solo` on
bare metal and `S` at exit 0 on hosted. There is no field at all, so the NAME
print alone is sufficient for the truncation and the fields are irrelevant to
it. The 0xC0000005 needs a TEXT field and never appears without one. Two faults
sharing one context, and the truncation is the simpler subject: a constructor
with no fields is now the smallest reproducer, at nine lines.

Also bounded: the truncation is a CLAMP TO ONE, not a proportional loss. Names
of four and five characters (`Solo`, `Both`, `Vals`, `Zebra`) and a five
character field (`hello`) all emit exactly one character, so whatever length the
printer reads is 1 rather than a scaled or shifted value.

**The next instrument should be the emitted code, not another reading.** Dump
### Re-graded 2026-09-01 against the FIXED control: the list is 14, not 22

First measurement of this campaign where the control is known correct. Seed
1CC3265D, both arms same-version, `ops/*`:

| arm | result |
|---|---|
| x86-64 hosted (the control) | **40 pass, 0 fail** |
| wasm plug | **26 pass, 14 fail** |

So every red is now genuinely wasm's. The earlier 22 was measured against an arm
that mis-rendered any constructor, which is why the number moved without anyone
fixing a wasm defect.

**The 14, by what they need rather than by symptom:**

- **One missing primitive, five subjects in `ops/*` and one outside it.** There
  is no real-to-text in this plug, so a real prints as its raw f64 bit pattern:
  `real-approx`, `real-mode-opening`, `real-mode-show`, `real-to-int-wide`
  (its last line only), `unit-show`, and `neg-real-repro` at the top level.
  Oracle and scope are recorded above. **`real-approx-modes`, `real-saturating`
  and `real-mode-fields` were listed here and are NOT this primitive**, measured
  by reading each subject's output rather than its symptom: the first two print
  only integers and are the saturating-mode clamp below, and the third returns a
  CONSTRUCTOR, which this plug cannot print at all.
- **Missing builtin arms, by the marker the emitted `.wat` carries** rather than
  by the name in an earlier list: `is-letter` and `is-whitespace`
  (`cce-builtin-bounds`), and `vec-extract`, `vec-load-at`, `vec-reduce-add`
  (`vec-lanes-smoke`). These now ASSEMBLE and trap at RUNTIME rather than
  failing wat2wasm, which is red 20969's changed refusal shape, so grade them by
  running. `list-view-bounds` is `__list-head` and `__list-tail`. Grep the
  emitted `.wat` for `no wasm form for`, over the WHOLE FILE and not line by
  line: these markers sit inside emitted lines thousands of characters long, and
  a line-scoped pattern reported this subject as carrying no marker at all.
- **One units literal** wat2wasm refuses outright (`unit-pattern-lit`, token
  `sin`).
- **Two wrong answers not in the above:** `bounded-modes-smoke` (366 against
  362) and `real-approx-equality` (166 against 165), both off by a few
  characters and neither yet read.
- **The saturating and trapping MODES do not clamp**, which the list above hid
  inside the printer. `real-saturating` wants `9218868437227405311`, the largest
  finite double, and answers `...312`, an infinity; `nan-zero` wants 0 and
  answers a NaN pattern. `real-approx-modes` is the same defect at f32 width:
  `2139095040` against `2139095039`, the one-bit distinction that subject was
  written to make. Neither prints a real anywhere.

### Real-to-text: `$f64_to_text` (reek, 2026-09-01)

`wat-emit-show` routed a `RealTy` to `$i64_to_text` and `wat-emit-entry-loop`
picked `$wasi_print_i64` for any non-Text return, so a real printed as its f64
bit pattern from both. `$f64_to_text` is a port of `__real_to_text`
(`X86_64TextHelpers.codex:590`): sign bit, `i64.trunc_sat_f64_s` for the integer
part, fifteen fractional iterations with an early exit when the remainder is
exactly zero, trailing zeros stripped to one digit. Divergence from the oracle
is above 2^63 only, where `cvttsd2si` gives the integer indefinite and
`trunc_sat` saturates; nothing in the corpus spells a value that large.

Measured, same seed and same slice both sides:

| slice | before | after |
|---|---|---|
| `ops/*` (40) | 26 pass 14 fail | **31 pass 9 fail** |
| `*real*`,`*unit-show*` (24) | 14 pass 10 fail | **20 pass 4 fail** |
| default 60 | 46 pass 14 fail | 46 pass 14 fail |

The 60 is unmoved because only ONE of its subjects reaches the new helper at all
(`apps/classics-test`, green before and after): grepping the 60 emitted `.wat`
for `call $f64_to_text` is what says so, rather than the score, which would read
the same if the change had been inert. The four still red under `*real*` are the
three classes named above, none of them a printer.

Cost: two 32-byte scratch buffers and the result string per call, bump-allocated
and not reclaimed, which is `$i64_to_text`'s shape at 24 bytes. Both loops are
capped (15 fractional digits, at most 20 integer digits), no recursion. The
helper is emitted into every module whether reached or not, like every other
runtime helper.

### The character-class predicates: `$is_digit`, `$is_letter`, `$is_whitespace` (reek, 2026-09-01)

`is-letter` and `is-whitespace` had no arm and fell through the dispatch's final
`else` to the funcref path (L-BAILVALUE), so `cce-builtin-bounds` assembled and
trapped. `is-digit` had none either and no subject reaches it, which is why no
score ever said so. All three are prelude helpers taking the code point once, so
the operand is evaluated once; the bands are the ones `emit-is-letter-builtin`
(`X86_64Builtins.codex:382`) tests, and the digit band derives from
`cce-digit-zero` rather than repeating 3 and 12.

`ops/*` 31 pass 9 fail to **32 pass 8 fail**; `cce-builtin-bounds` is green,
graded at the band edges its own `.expected` spells (12,15,70,97 and 0,1,2,3).

**`is-digit` is graded by nothing in the corpus, so it was measured separately**
rather than shipped on the other two subjects' green: a scratch chapter through
the plug and wasmtime, seed 1CC3265D, answers `False True True False` for code
points 2, 3, 12 and 13, which is both edges of the band in both directions.

**THE FIRST NAMES SHIPPED UNPREFIXED AND COLLIDED, and the `ops/*` slice could
not see it.** A Codex chapter in the tree defines `is-digit`, `is-letter` and
`is-whitespace` with these same semantics, so the emitted module carried two
`$is_digit` and `wat2wasm` refused `redefinition of function` on
`apps/codex-boot` and `apps/diagnostic-boot`. The default 60 caught it (46 pass
to 45) while `ops/*` held at 33, because no `ops` subject cites that chapter.
The helpers are `$__is_digit`, `$__is_letter`, `$__is_whitespace` and
`$__f64_to_text` now, which is the escape `$__text_compare` already used. This
is a measured instance of the open row below: **a new prelude helper takes the
`__` prefix, and a slice narrow enough to be quick is not a regression check.**

### The SIMD family: a vector is a 16-byte BOX (reek, 2026-09-01)

Nothing existed. `vec-splat` and `vec-extract` had no form, and the arithmetic
wrote `f64x2.add` straight onto two i64 operands, which `wat2wasm` refuses as a
type mismatch, so the whole family failed before it ran.

**The representation is the language's contract, not a choice made here.**
`DevelopersGuide.md` measures every vector-returning builtin at `fixed` 16 bytes
and `vec-load-at` as 16 bytes at an address, so a vector is a pointer to a
16-byte box and one box is exactly one `v128`. The carrier stays i64 like every
other pointer, so no local declaration changes; carrying vectors as `v128`
locals would have touched every local the emitter declares.

Shipped: the box, `vec-splat`/`vec4-splat`, `vec-extract`/`vec4-extract`, the
four operators, the four NAMED arithmetic builtins, `vec-load-at`,
`vec-store-at` and `vec-reduce-add`, across `f64x2`, `f32x4`, `i64x2` and
`i32x4`. The lane count is `16 / lanes` bytes, which the box forces.

**Two refusals rather than plausible answers.** A lane shape this cannot name
refuses; and so does integer division, because wasm SIMD has no integer divide
at any width, so there is no instruction to emit.

**A VECTOR PATTERN was not tested, the same shape as a literal inside a
constructor pattern one CL earlier.** `IrVecPat` bound nothing and tested
nothing, so `is Vector [0, 0]` matched every vector and `vec-pattern` answered
`zero` three times. The lanes are compared against their literals out of the
box now.

**The pattern's recorded type does not survive to this plug, and only a
measurement said so.** `IrVecPat` carries the scrutinee's `VectorTy`
(`Lowering.codex:1101`), and reading that is what the first attempt did; it
arrives empty here, so the refusal fired and the subject went from a wrong
answer to a trap. The lane WIDTH does not need the type at all: the box is 16
bytes, so `16 / lanes` is forced, and an integer literal compare at the right
width is correct whatever the element type is declared to be. Found by emitting
the computed shape into the `.wat` as a comment, which is the third time today
that beat reasoning about the AST.

Measured, seed D3A0C75A:

| slice | before | after |
|---|---|---|
| `*vec*`,`*vector*` (24) | 11 pass 13 fail | **22 pass 2 fail** |
| `ops/*` (40) | 39 pass 1 fail | **40 pass 0 fail** |
| default 60 | 52 pass 8 fail | 52 pass 8 fail, same list |

R-COST: one 16-byte box per vector-returning operation, which is the documented
allocation for every one of them on every backend. Reductions are helpers taking
the POINTER, so an operand that allocates is evaluated once rather than four
times.

### The MASK family, and a vector comparison that was answering a scalar (reek, 2026-09-01)

**`IrLtVec` was folded into the `IrLt` arm, so a vector comparison emitted a
SCALAR compare on the two POINTERS** and answered a Boolean where a mask was
wanted. Same for `IrGtVec`, `IrLtEqVec`, `IrGtEqVec`. The four now route to
`wat-emit-vec-cmp`, which is `wat-emit-vec-arith` with a comparison suffix: the
integer shapes spell theirs `_s`, the float ones do not.

**A mask needs no representation of its own.** A lane comparison answers
all-ones or all-zeros per lane, which is the same 16-byte box a vector already
is (`X86_64Builtins.codex:1770`, where the oracle reads it back with `movmskpd`
off exactly that box). So `vec-select` is `v128.bitselect` over three boxes and
nothing else, and the mask queries are `i64x2.bitmask`.

**The width is FORCED and no type is consulted.** Every mask builtin in
`Types/Builtins.codex:265-271` is declared over `VectorMask 2` and there is no
other width in the language, so `i64x2.bitmask` is the only form: two lanes, one
bit each, all-set being 3, `mask-count` a two-bit popcount. That is the same
answer `16 / lanes` gave the vector patterns, and it is why this did not repeat
the `IrVecPat` mistake of reading a type that does not survive to the plug.

Measured, seed D3A0C75A:

| slice | before | after |
|---|---|---|
| `*vec*`,`*vector*`,`*mask*` (25) | 22 pass 3 fail | **25 pass 0 fail** |
| `ops/*` (40) | 40 pass 0 fail | 40 pass 0 fail |
| default 60 | 52 pass 8 fail | 52 pass 8 fail, same list |

**The SIMD family is closed.** Nothing in the vector or mask surface is left
unemitted; the two forms that refuse do so on purpose, a lane shape the emitter
cannot name and integer division, which wasm SIMD does not have at any width.

R-COST: `vec-select` is one box; a comparison is one box; a mask query is none.
`$cx_mask_bits` takes the POINTER, so `mask-count` reads its operand once.

### Neither hosted harness fed the `.stdin` sidecar, and the control was not 60 of 60 (reek, 2026-09-01)

**Both harnesses ran their subject with no stdin while a `.stdin` sidecar sat
beside it.** `build/test.ps1` feeds one (`-StdinFile`, `-input`);
`hosted-wasm-test.ps1` and `hosted-elf-test.ps1` did not. A subject that reads
input then printed its banner and stopped, and the harness graded that as a
wrong ANSWER rather than as an unfed bed: `apps/diagnostic-boot` answered 67
chars of 426 on all three arms. Fixed in both; it now answers 427 of 426 on all
three, which is a different and much smaller claim. Seven subjects carry a
`.stdin`; one is in the default 60.

**A Perforce-managed sidecar is READ-ONLY and `Start-Process
-RedirectStandardInput` opens the file for WRITE**, so the depot path fails
with "Access to the path is denied". Both harnesses copy it to the workdir
first. This is the whole of why the obvious one-line fix does not work.

**THE CONTROL WAS NOT 60 OF 60.** `CurrentPlan` said the hosted x86-64 lift
runs 60 of 60 against these sidecars, and every wasm verdict in this campaign
is graded against that arm (L-CONTROL). Measured at seed D3A0C75A, the whole
default 60 on both targets: **103 pass, 17 fail -- linux 52 of 60, windows 51
of 60.** The wasm plug is 52 of 60, equal to linux and one ahead of windows.

**Six of the eight wasm reds were never parity gaps.** `cpu-builtins` and
`cpu-inspect` read CR0/CR3 and CPUID, `cam-capture`, `console-test` and
`diagnostic-boot` use port in/out, `bp-symbolic-write` resolves through MAP1
and patches its own code at 0x100000. Both hosted x86-64 targets die on them:
SIGSEGV on linux, and windows names the cause outright, `0xC0000096`
PRIVILEGED_INSTRUCTION for the CPUID and port subjects and `0xC0000005` for the
rest. No wasm arm can honour any of it, and the refusal markers this plug emits
are the correct answer rather than a gap.

**What is genuinely left on the default 60 is one subject.** `apps/dev-watch`
answers `origin-untouched: 2` for 0 and prints a raw heap base (71922 against
the sidecar's 6291456), and hosted windows fails it too, differently (538
against 532). `annotation-query-test` and `diagnostic-boot` are one shared
oracle defect and are recorded in `ExaminersAssay.md`, "Two sidecars are one
byte short of what their subject prints".

### THE REFUSAL MARKER HAS TWO SPELLINGS, and a census with either one misses subjects (reek, 2026-09-01)

**Grepping the emitted `.wat` for a refusal is the standing way to find a
missing builtin, and there is no single string that finds them all.** Seven
emit sites in `WasmEmitter.codex` produce an `unreachable` with an explanatory
comment, in two spellings:

- `(unreachable (; wasm plug: <why> ;))` -- five sites (the string table, the
  4096 nesting ceiling, partial application of a lambda, `wat-vec-refuse`, and
  the `wat-no-such-thing` list).
- `(unreachable) (; no wasm form for <name> ;)` -- two sites: the wrapping-band
  refusal, and **`wat-try-builtin`'s generic fallthrough, which is where most
  refusals actually come from.**

**Measured, and this is not hypothetical:** in one graded run
`apps/cam-capture` carried 4 of `no wasm form for` and ZERO of `wasm plug:`,
while `apps/console-test` carried 4 of `wasm plug:` and ZERO of the other. A
census with either pattern alone finds one of those two subjects and reports
the other as clean. That happened: the first pass over the default 60's eight
reds found markers in two subjects and missed six, and the six were the
interesting ones.

**Until the spellings are unified, the census pattern is
`no wasm form for|has no form on this target|wasm plug:`**, and it must be run
over the WHOLE FILE rather than line-scoped, because an emitted line runs to
thousands of characters.

**Unifying them is a comment-text change with no behavioural effect** -- both
forms emit the same `unreachable` -- but it moves every subject's emitted bytes,
so it wants a grading run and is queued rather than done during release mode.

### THERE IS NO WASM DECK INFLATION on the compiler's own source (reek, 2026-09-01)

**Measured at seed 9B73E281, the compiler's own 3,052,663-byte unit, bare `CDX`
with no explicit `decks=` so both arms take the compiler's OWN derivation:**

| target | mode | result | payload | time |
|---|---|---|---|---|
| wasm | `CDX` | **compiled** | 3,092,951 | 4.3 s |
| wasm | `CDX decks=1` | refused CDX9002 in LEX | -- | 0.1 s |
| x86 | `CDX` | **compiled** | 3,092,951 | 4.7 s |
| x86 | `CDX decks=1` | refused CDX9002 in LEX | -- | 0.2 s |

**Both arms compile, and the payloads are byte-count identical.** The `decks=1`
arms are the instrument's negative control and both refused, so this is not a
mode line nobody parsed.

**So `prism.html`'s comment is now FALSE where it says "the compiler's own
source refuses CDX9002 in SCOPE at its derived scale, and the top rung is the
page's proven answer".** 2.10 left this open as "the underlying wasm deck
consumption question"; the answer today is that there is no difference to
explain. The deck derivation is compiler source (`opening.codex`,
`deck-scale-of` / `scaled-floor`) and identical on both targets, so a difference
could only ever have been the runtime consuming more inside a deck.

**I HAVE NOT ATTRIBUTED THE CHANGE and will not guess.** The obvious candidate
is this session's COMPILER-42 work: a list literal was born with capacity 8
regardless of length, so every literal in the compiler's own source
over-reserved. That is a hypothesis, not a finding. The control that would
settle it is a module rebuilt with that fix reverted, about five minutes of box
time, and it has not been run.

**What NOT to conclude: this says nothing about the IR ladder.** `DECKS =
[12, 48, 125]` governs the `IR-UNI` path and was not measured here. One unit on
one mode is what was tested. Before dropping a rung anywhere, measure that path
too (L-COUNT, and the ladder exists because a rung was needed once).

### The page build is 168 s, not 451, and only ONE phase was worth gating (reek, 2026-09-01)

**THE 451 s FLOOR IS STALE. Measured at seed FD18B0C8: 168.1 s full**, and the
breakdown had never been taken, which is why the wrong number survived. Per
phase, and re-measure before quoting any of it (L-COUNT):

| phase | full | note |
|---|---|---|
| module (emit + `wat2wasm`) | **145.2 s** | 86 per cent of the build |
| examples (calibrate + compile) | 13.9 s | correctness arm |
| x86-truth (the anchor) | 4.3 s | correctness arm |
| library (volume + gzip) | 2.6 s | |
| cdx-arm | 0.4 s | correctness arm |
| TOTAL | 168.1 s | |

**`-Incremental` is OPT-IN and gates exactly one phase.** 168.1 s to **20.6 s**,
and the anchor hash came back identical, computed fresh both times, which is
what says the fast build did not fake it. The default stays FULL and every full
build rewrites the cache, so a cache can only ever describe a build that
happened.

**The module fingerprint includes `wasm-plug.cdx` and not just the source.** An
emitter change with an untouched `Codex.codex` produces a different module, and
a fingerprint that missed it would serve the OLD compiler from a page asserting
the new one's hash (L-SAMEVER).

**THE GATE I WROTE FOR THE x86 TRUTH ARM CAME OUT, on the measurement.** It was
gated first on the assumption that pushing the whole compiler source through the
VM must be expensive. It is 4.3 s. Caching it meant caching THE ANCHOR HASH
ITSELF, the number the page asserts byte-identity against, to save four seconds.
That is a correctness surface traded for nothing, and the only reason it looked
reasonable beforehand is that nobody had timed the phases. **The library is 2.6 s
and is not gated either**, for the same reason, though the register's complaint
lists it.

**The control fires, and an untested staleness check is the whole risk here.**
Touching `wasm-plug.cdx` by ONE SECOND under `-Incremental` rebuilt the module
phase, 152.5 s, total 176.0 s. Three runs: full 168.1 (module built),
incremental unchanged 20.6 (module SKIPPED), incremental with one input touched
176.0 (module built). Anchor identical across all three.

### arm64 SHIPS, and its `ship = $false` was never residue (reek, 2026-09-01)

**The plan called stage 3 item 1 "flip arm64's `ship` (2.03's residue)". It was
not residue and flipping it alone would have shipped a dark payload.** 2.03 says
so in terms: "ARM64 is a disabled pill carrying its reason, not an absence ...
there is no `Arm64Elf` chapter anywhere in the tree", and "shipping 271 KB the
page has no way to reach is a dark payload". Re-verified before acting: 0 files
and 0 chapter declarations matching `Arm64Elf`.

**So the work was the chapter, and 2.03 sized it right: a straight port of
`RiscVElf` with `EM_AARCH64` 183.** `RiscVElf.codex` is 110 lines and the port
is structurally identical. Two things are NOT identical and both would be silent:

- **The load address is `#40000000`**, the RAM base `qemu-system-aarch64
  -machine virt` maps, where RISC-V's `virt` uses `#80000000`. An arm64 kernel
  built at the RISC-V base is the same silent hang `RiscVElf`'s own prose
  records, one architecture over.
- **`a64-record-func` stores an instruction INDEX**, like `rv-record-func`, and
  the builder takes BYTES, so the index is scaled by 4 in `a64-emit-board-elf`.
  Unscaled it lands a fraction of the way to the entry and unaligned, which no
  AArch64 core will fetch.

**`Arm64Stdio` needed `cites Foreword chapter StringUtils`**, which `RiscVStdio`
has and it did not, because the mode line uses `text-drop`. The module failed to
build with `CDX3002: Undefined name: text-drop` until that was added.

**Graded through the page's own path, arm 12d, beside RISC-V's 12c.** Measured:
`AArch64 ELF64 machine 183 entry 0x40000800 (14,168 bytes); wire control still a
wire (15,830 bytes)`, and RISC-V's arm is unmoved at machine 243 entry
0x80000000. The arm checks class, machine, entry ALIGNMENT, entry inside the
text segment, and the load address, plus the control that the DEFAULT mode still
answers a wire -- without that last one a module that ignored the mode line and
always built an ELF would pass.

**A module an arm reaches must be in `page-workspace-arm.js`'s embed list.** It
does not fall back: it reaches `fetch`, which the sandbox rejects, and the arm
dies with "no network in the arm" rather than with a finding. That is what the
first run of 12d did.

**WHAT THIS IS NOT: the kernel has not been booted.** The arm grades the
CONTAINER. RISC-V's equivalent claim was earned by a real
`qemu-system-aarch64`-class boot printing byte-identical output (2.09); arm64 has
no such measurement and must not be described as booting. The 14,168-byte arm64
kernel against RISC-V's 45,064 for the same IR is unexplained and worth a look
before anyone boots it.

Preflight that fires on this change: `check-doc-counts` DRIFTs on
`plug modules (TechDetails)`, 191 to 192, because the new chapter is a plug
source module. Corrected in the same CL; the gate runs this check.

### Stage 2's remaining gaps, measured: two are not defects and one is a false claim (reek, 2026-09-01)

**`__self-type-defs` is implemented on ONE back end and the plan reads it as a
wasm gap.** arm64 (`Arm64CodeGen2.codex:1549`) and RISC-V
(`RiscVCodeGen2.codex:971`) emit integer `0`; the C# plug emits
`new List<TypeBinding>()`; wasm emits an empty list. Only x86-64 builds the real
table. **wasm's answer is strictly better than arm64's and RISC-V's**, which
hand back a null where a `List TypeBinding` is expected, so `list-length` on it
reads address 0.

Closing it on wasm means porting `emit-self-type-table` and its chain --
`emit-const-codextype` (28 arms, recursive, fuelled), `-top`, `-typebinding`,
`-recordfield`, `-fieldlist`, `-ctorlist`, `-name`, `-text`, `-binding-elems`,
`-ptr-list`, `const-box`, `live-type-tag`, `live-name-ref-tag`,
`reach-names-list`, `filter-by-names`, `sort-type-bindings` -- about 250 lines
across `X86_64Compound.codex:1482-1760` and `:879`, into emitted `(data ...)`
segments with a different address model. It would then have to track
`CodexType`'s 28 constructors forever. **Every consumer is inside
`X86_64Compound.codex` itself** (the pmap self-test at :1407-:1446), and the
payoff is that one x86-hosted self-test's status. Not taken; a decision for
Damian if it is ever wanted, and it is a compiler-wide question rather than a
wasm one.

**THE 4 MB STDIN BUFFER DOES NOT EXIST AND NOTHING TRUNCATES.** The plan carries
"1.66's unmeasured 4 MB stdin buffer" and this emitter's own prose claimed "the
fixed 4 MB cap the text readers use silently DROPS what does not fit ...
(L-SHORT)". Measured against the source: the cap is **1048576, one MiB**, at all
three sites; and it is **not fixed** -- `$read_serial_cce`, `$read_file_uni` and
`$read_file_raw` each double it in place when `n` reaches it. Nothing is
dropped. The prose was an assertion with no runner sitting next to code that
refutes it, which is R-PROSE's named failure, and it is deleted.

**Why that growth is sound HERE and was not in `$list_push`.** Both use the same
trick, consecutive `$bump_alloc` calls being contiguous. In a read loop nothing
else allocates and the buffer is not yet anyone's value. In `$list_push` the
block being extended is a list a CALLER still holds, which is the defect
COMPILER-42 records. Do not repair one by analogy with the other.

### Six builtins had no wasm arm, found by CENSUS rather than by a subject (reek, 2026-09-01)

**`abs`, `max`, `print-line-raw`, `print-error`, `print-error-uni` and
`process-get-network-scope` reached the funcref path**, which is L-BAILVALUE and
the same class as the nine arms closed earlier in this campaign. Nothing in the
corpus calls them, so no amount of grading would have found them.

**The instrument is the REGISTRY, not the corpus.** Take every `bs-name` from
`Types/Builtins.codex` (264) and ask which are absent from the emitter's table:
136. Most are unsupportable on wasm by nature (VMX, MSR, MMIO, UEFI, CPUID,
ports, processes, channels, sockets). **The census is a shortlist, not a
finding** -- `int-rem` and `compare` are in the 136 and are both handled
elsewhere, so every candidate was probed before it was believed.

**`print-line-raw` looks broken and is not, which is the trap worth recording.**
It prints a single `I` on a terminal, because it writes RAW CCE bytes with no
Unicode conversion: `raw-line` is `21 15 27 73 23 17 18` and byte 73 is the only
one that renders as ASCII. x86-64 emits exactly the same bytes. **Graded
byte-for-byte against the hosted x86-64 lift, the probe is 66 bytes on both arms
and identical.** Reading the screen instead of the bytes would have "fixed" a
correct arm into a wrong one.

**Two of stage 2's named gaps are not defects.** `hosted-kind` answers 1 and all
three consumers in the tree (`BootPaint.codex:48`, `PhaseAllocator.codex:38` and
`:86`) test `/= 0` only, so the answer is correct in effect; `opening.codex:1508`
sets the range, 0 bare metal, 1 hosted, 2 hosted-windows, and reserving a wasm
value changes a documented range, which is a compiler call. `process-get-scope`
answering an empty Text is likewise correct and documented at the consumer:
`Fat16.codex:1804` says an empty scope admits everything and is what an unscoped
grant means. `process-get-network-scope` was the real gap beside them, and
`net-scope-admits` reads it exactly the same way.

**The plan's citation for `hosted-kind` is stale**: `CurrentPlan` says
`WasmEmitter.codex:1009`; the sites are 1109 (`__self-type-defs`), 1113
(`process-get-pid`), 1114 (`hosted-kind`) and 1877 (the scopes).

Measured, seed D3A0C75A: default 60 unmoved at 52 pass 8 fail with the same
list, `ops/*` unmoved at 40 pass 0 fail.

R-COST: three tiny runtime helpers, no loops beyond `$write_raw`'s existing one.

### `apps/dev-watch`: two wrong causes before the right one (reek, 2026-09-01)

**The subject's `origin-untouched` answered 2 for 0, and the cause is that a
list literal was born with SLACK.** `emit-wat-list` set capacity to 8 for any
literal shorter than 8, and `$list_push` writes into the caller's block
whenever `len < cap`, so the very first `list-snoc` onto a fresh `[]` grew the
list the caller still held. Capacity is now the literal's length, which is what
`$list_append` and `$list_cons` already did and what this file's `$list_push`
prose already claimed was true of every plain list.

**Two frontier-extend paths went with it.** `$list_push` grew a block in place
when it ended exactly at the heap or the deck frontier. x86-64's `__list_snoc`
(`X86_64ListHelpers.codex:224`) has no such path; it has in-place-if-capacity
and then alloc-and-copy, nothing else. Those paths alias for the same reason.

**Both wrong causes are recorded because each was disproved by measurement and
the next reader will reach for them in the same order.** The frontier paths
were the FIRST hypothesis and deleting them changed nothing: `origin-untouched`
stayed 2, and the emitted `$list_push` was confirmed to contain no `heap_ptr`
or `deck_ptr` before that was believed. The SECOND was nested record literals
sharing the `$_rp`/`$_tv` scratch pair, which the emitted `dev_watch_add` does
do; a two-record probe against a hoisted control showed it is handled and
answers correctly. Only the third hypothesis survived, and it was found by
probing `list-snoc` itself rather than by reading further.

**`list-snoc` IS STILL NOT PERSISTENT, on this plug or on the control**, once
the list has slack: the second push aliases on both arms. That is a language
defect rather than a parity gap and it is `compiler-backlog.md` COMPILER-42.
Do not fix it here. Setting capacity equal to length everywhere makes snoc
fully persistent and was measured: it costs O(N^2) bytes in a bump allocator
that never reclaims, and took `apps/brotli-hostile` and `apps/deflate-hostile`
from green to `memory.grow` returning -1, the default 60 from 52 to 50.

**What is left in `dev-watch` cannot be fixed by any plug.** It prints raw
`alloc-bytes` addresses and the sidecar carries bare metal's heap base:
`watching alpha at 6291456` where wasm says 71922. Hosted windows fails it too,
differently. Same class as the six hardware subjects above.

Measured, seed D3A0C75A: default 60 unmoved at 52 pass 8 fail, `ops/*` unmoved
at 40 pass 0 fail, and the `list-snoc` probe goes from `2,2,2,1,2` to
`0,1,1,0,2` where a fully persistent snoc would be `0,1,1,0,1`.

R-COST: a shorter literal allocation and one fewer in-place path. Growth keeps
its doubling, so a push stays amortised O(1); this is the change that does NOT
cost the O(N^2) named above.

**The harness summary line misreads the wasm failures and it cost a diagnosis.**
`exit 3 Error: failed to run main module` reads as a module that would not
instantiate. It instantiates and runs; the trap reason is three lines further
down the wasmtime error, and for all four it is `wasm trap: wasm 'unreachable'
instruction executed`, which is this plug's own refusal marker. Take the first
line that names a trap, not the first line of the error.

### Printing a CONSTRUCTOR from `opening` (reek, 2026-09-01)

The entry printer sent any non-Text return to `$wasi_print_i64`, so an
`opening` answering a constructor printed the POINTER: `ops/real-mode-fields`
said `71417` where the oracle says `Vals 1.5 2.5 3.5 4.5`. The form is
`emit-opening-print-sum` (`X86_64Chapter.codex:242`): the ctor name, then one
space and one field each, then a newline.

Three things had to change and only the first is the printer. The entry emitter
took only the DEFINITIONS, so it could reach neither the type-defs that hold the
ctors nor the string table that must name them; it takes the ctx now. And a
ctor name appears in no expression, so nothing put it in the table:
`wat-seed-entry-strings` seeds the return type's names and the separating space
before offsets are assigned. Had it not, `wat-strtab-ref` from the CL before
this one would have refused it by name rather than emitting a pointer to
address 0, which is the argument for that guard.

**Two wrong assumptions, both caught by measuring the emitted output rather
than by reading.** `wat-type-name` answers for `ConstructedTy`, `TypeCon` and
`RecordTy`, which is every case its own callers have; an `opening` returning a
variant arrives as a `SumTy` and it answered the empty string, so the printer
was emitted for no program at all and the score did not move. And a real in a
MODE spells its own head: the ATypeExpr heads are `real-approx`,
`real-trapping` and `real-saturating`, not `Real` applied to a modifier, so a
test for `Real` alone rendered three of that subject's four fields as integers.
Both were found by putting the head name into the emitted `.wat` as a comment
and reading it, which took one build each.

`ops/*` 38 pass 2 fail to **39 pass 1 fail**, the only red left being
`vec-lanes-smoke` and the SIMD family. The default 60 is unmoved at 52 pass 8
fail.

**Left alone, and named so it is not rediscovered as a surprise:**
`wat-eq-field-cmp` tests `fn == "Real"` and so compares a MODED real field as an
i64 rather than as a double. Same root as the second assumption above, no
subject reports it, and it is a different function from the one this change
touches.

### `~` and `~0` counted ULPs at the wrong width, and the string table answered 0 for a miss (reek, 2026-09-01)

**The ULP half.** A ULP is a property of the WIDTH, and `$__approx_eq` maps a
64-bit pattern to a monotonic ordinal and compares the difference against the
tolerance. An approximate value is carried here as a promoted f64, so an f32
comparison was counting doubles: the two smallest denormals either side of zero
are 2 ULPs apart as singles and astronomically far apart as doubles.
`$__approx_eq32` is the same ordinal map at 32 bits on the demoted value, chosen
by the operand type. `ops/*` 37 pass 3 fail to **38 pass 2 fail**;
`real-approx-equality` green, and its `tiny straddle` line is the one that
separates the widths.

**The string table half, and it is a guard rather than a defect anyone had
hit.** `strtab-lookup` answers 0 for a string that is not in the table, the
table starts at 1024, and all three sites that turn a lookup into a POINTER used
that answer directly, so a collector that failed to reach a new site would emit
a pointer to linear-memory address 0 and print whatever is there, with no
diagnostic. That is L-BAILVALUE sitting under the Text-pattern work above, which
added two of those three sites. `wat-strtab-ref` is now the single site and it
refuses with a marker naming the string.

**Both halves have a control, because a guard that never fires and a guard that
cannot fire read the same.** 157 emitted `.wat` across three slices carry ZERO
markers, which is what says the collector is complete. Forcing the lookup to
answer 0 puts 13 markers in `ops/unit-pattern-lit` alone, naming `sin`,
`matched` and `fell-through`, so the refusal path works and is reached. The fix
state was hashed before the sabotage and the hash verified after restoring it.

### The saturating and trapping MODES did not clamp (reek, 2026-09-01)

Two defects and they had to be fixed together, which is why the clamp alone
would have measured as no change.

**An approximate real was computed at f64.** The value is carried as a promoted
f64 everywhere in this plug, and `wat-bin-instr` answered `f64.add` for
`IrAddRealApprox` as well as for the double ops, so single arithmetic never
overflowed: `f32-max * 2` is finite as a double. x86-64 emits the single SSE
form (`real-sse-prefix f32`). An approximate op now demotes both operands,
operates at f32 and promotes back, which fixes the rounding as well as the
range.

**The mode was then dropped.** `IrAddRealTrapping` and `IrAddRealSaturating`
mapped to the same bare `f64.add` as the plain op, so both modes were the plain
mode. `$cx_real_trap64/32` and `$cx_real_sat64/32` are
`emit-real-trapping-arith` and `emit-real-saturating-arith`
(`X86_64.codex:1887`) written as masks rather than shifts: exponent all-ones is
infinity or NaN, a nonzero mantissa within that is NaN, trapping traps on
either, saturating answers 0 for a NaN and DECREMENTS the bit pattern for an
infinity, which is the largest finite magnitude of the same sign because the
decrement never reaches the sign bit. The NaN-to-zero is x86-64's answer and a
language decision, so it is copied rather than revisited. The width comes from
the OPERAND TYPE, as it does on x86-64, not from the op.

Measured, seed D3A0C75A: `ops/*` 35 pass 5 fail to **37 pass 3 fail**;
`real-saturating` and `real-approx-modes` green. `real-approx-modes` is the
one that could not be faked: it distinguishes a working clamp from an absent
one by a single bit, 2139095039 against 2139095040.

**The 13 SIMD reds in the neighbouring slice are pre-existing, proven and not
reasoned.** Every real arithmetic op moved in this change, so a `vec` family
sitting red beside it had to be attributed rather than assumed: the depot
revision rebuilt and run over `*vec*`,`*vector*` gives 11 pass 13 fail, and the
same 13 by name after. `IrAddVec` is not in the arm this change touches.

R-COST: one call per moded operation, a leaf with no allocation and no loop; two
conversions per approximate operation. Nothing on the plain f64 path changes.

### Text literal PATTERNS, and they were not one subject (reek, 2026-09-01)

A Text literal is the one literal whose spelling is not its value, and
`emit-wat-match-arm` spliced the spelling into `(i64.const sin)`, which
`wat2wasm` refused as `unexpected token "sin"`. The scrutinee is a pointer, so
the test is `$text_eq` against the string table entry, the same place an
`IrTextLit` expression reads; and the collector had to reach into the PATTERN,
because a string that only a pattern mentions is in no expression and so had no
table entry to look up. Both emitters take it, the plain one and the TCO one.

**It was carried as one subject and it was seven.** `ops/unit-pattern-lit` was
the named instance; the same defect refused all six `apps/browser-*` subjects in
the default 60, which had been read as their own `newtab` failure. `ops/*` 34
pass 6 fail to **35 pass 5 fail**, and the default 60 **46 pass 14 fail to 52
pass 8 fail** on one change.

**Two reds found in the neighbouring slice while grading this, neither in the
14.** `vec-pattern` traps on the SIMD family already listed. The other is
`literal-subpattern`, FIXED below.

### A LITERAL inside a constructor pattern was never tested (reek, 2026-09-01)

`emit-wat-bind-ctor-subs` bound an `IrVarPat` sub-pattern and sent everything
else to `is otherwise -> ""`, so `BInt (0)` matched every `BInt` and every arm
of `codex/test/literal-subpattern` answered the FIRST arm's body: `int-1` said
10 where the oracle says 11, `txt-cos` 20 where it says 21, eight lines of
twelve. A literal sub-pattern is a TEST, not a binding, and there was no arm
that could express one. L-BAILVALUE at the sub-pattern level.

The tests fold onto the TAG test rather than sitting beside it, because a
payload load on an object of the wrong tag can address past the object and
trap, which is the reason the guard was already gated that way. The last-arm
shortcut, which makes a final trivially-guarded constructor arm unconditional,
now also requires the arm to carry no literal sub-pattern.

**Measured against a rebuilt control, not against reasoning**, because a
neighbouring subject in the same slice (`lir-selector-smoke`, 28 chars against
27) was red at the same moment and both had to be attributed. The depot
revision rebuilt and run over the two: `literal-subpattern` red, `lir-
selector-smoke` red with the identical byte counts. After: `literal-subpattern`
green, `lir-selector-smoke` unchanged and still open, so it is a separate red
and nothing here moved it.

`ops/*` holds at 35 pass 5 fail and the default 60 at 52 pass 8 fail across
this change; the subjects it moves are outside both slices, which is the whole
reason it was found by widening rather than by the campaign's own list.

### `__list-head` and `__list-tail`, and the tail is a VIEW (reek, 2026-09-01)

Both had no arm, so any `when xs is Cons (h) (t)` trapped: the lowering desugars
that match into the three list intrinsics (`Lowering.codex:837`), so a structural
list match is not a plug feature this plug could decline quietly.

`__list-head` is `$list_at` at index 0. **`__list-tail` was written as a COPY
first, and `ops/list-view-bounds` went green on it**, which is the reading worth
keeping: a copying tail is correct for every reader except one, and the one is
`list-set-at` writing THROUGH a view into the backing, which x86-64 documents as
the contract (`X86_64ListHelpers.codex:3`). `codex/test/list-view-probe` is built
to pin exactly that and answered `set=77,30,30` against `set=77,77,77`, one line
of sixteen. The copy also makes a `Cons` recursion O(n^2), which no output can
show.

The shipped tail is a view, and the sentinel had to move because the layouts
differ. x86-64 puts the pointer at the LENGTH cell with capacity behind it at
`p-8`, so a negative capacity is free; this plug's list is
`[len i32][cap i32][elements]` with the pointer at the header, so **capacity -1
is the sentinel** and the view's third cell holds a phantom pointer chosen so
that phantom+8 is the view's first element, the displacement every element loop
here already uses. `$cx_list_base` is the one selection and the readers that
index elements call it: `$list_at`, `$list_set_at`, `$list_append` (both
operands), `$list_cons`, `$list_push`, `$list_insert_at`, `$text_concat_list`,
`$raw_bytes_to_text`. `$list_length` reads the view's own header and is
unchanged.

`$list_push` needed one more thing than a base swap, and it is the trap in this
design: its two frontier-extend paths test `p + 8 + cap*8` against the allocator
position, and for a view that address is a cell in the BACKING block, which can
legitimately equal the frontier. Both tests now require the list not to be a
view, and a view's capacity is read as 0 so the in-place path cannot fire.

Measured, seed D3A0C75A: `ops/*` 33 pass 7 fail to **34 pass 6 fail**;
`*list*`,`*cons*` (23 subjects) 21 pass to **22 pass**, `list-view-probe`
included with its `set=` line correct; `*text*`,`*buf*`,`*cce*` 39 of 39; the
default 60 unmoved at 46 pass 14 fail with the same fail list.

R-COST: the view is one 16-byte allocation per tail, O(1), against O(n) for the
copy it replaces. `$cx_list_base` adds one load and one compare to each element
access on the plain path.

### The wrapping bounded-integer mode did nothing (reek, 2026-09-01)

`wat-atype-band` recognised `OvClamping` and sent every other mode to
`wat-no-band`, so a clamping field clamped and a wrapping field stored its
operand unchanged. `bounded-modes-smoke` wanted `wu8 300: 44` and answered
`300`; all seven clamping rows in the same subject were already right, which is
why the defect read as a small diff rather than an absent mode.

A wrapping band is admitted only at a hardware width
(`bounds-are-hw-width`), so the band IS the width and no table is needed: an
unsigned band's `hi` is exactly the mask (`i64.and`), and a signed band's `hi`
names the sign-extending instruction (`i64.extend8_s`, `16_s`, `32_s`). A band
that is neither refuses with a marker rather than answering, because a wrong
wrap and a correct one are the same kind of number (L-BAILVALUE).

`ops/*` 32 pass 8 fail to **33 pass 7 fail**. The subject grades both
directions at both edges: 300 to 44, -1 to 255, 256 and 512 to 0, 128 to -128,
-129 to 127, 130 to -126.

### 2.17 FIXED 2026-09-01 (reek). The hosted entry reserved no stack frame

`emit-hosted-start` set `rbp = rsp` and reserved NOTHING, so every local the
entry code spilled sat BELOW the stack pointer, where the next push or call
overwrote it. `emit-hosted-win-prologue` reserves 32 bytes for the Win64 alloc
calls and gives them straight back, which is right for those calls and leaves
the entry's own frame at zero.

That is why only the entry sum printer was affected: it is the one place with
enough live values to SPILL. User code and the plain-Text entry keep the same
printer's locals in registers, where nothing can reach them, which is exactly
what the byte diff showed and why the same emitter was correct in one context
and wrong in the other.

**One cause, both symptoms.** The truncation was the loop's exit test re-reading
a corrupted length; the 0xC0000005 was the same corruption landing on a pointer.
Both disappear together.

**The fix is derived, not a magic reservation:** a placeholder at the entry
prologue, patched with `align-16 (peak-spill * 8 + 64)` once the entry body is
emitted, which is how `emit-function-standard` has always sized a normal frame.
`reset-func-scratch` runs first so `peak-spill` counts the ENTRY's spills rather
than inheriting the last function's.

**Measured with the fix, hosted Windows, against bare metal in every row:**
`Solo`, `Zebra 7`, `Wrap hello world`, `Only hello`, `Triplet`, `Duotone`, and a
deliberately wide `Big aa 1 bb 2 cc 3 dd 4` (eight fields, to force more spills
than the reproducers needed). All exit 0. **The x86-64 control arm over `ops/*`
is 40 pass 0 fail, up from 39 pass 1 fail**, so `ops/real-mode-fields` closes
and 2.16's "red on BOTH arms" row is retired.

**This also restores the parity control.** Every wasm verdict this campaign
published was graded against an arm that mis-rendered any constructor; that arm
is now correct and the wasm gaps can be trusted as wasm gaps.

### CAUSE FOUND 2026-09-01 (reek): the spilled `saved-len` slot does not survive one iteration

Three probes, each a trap compiled into the printer by a scratch kernel, each
answering one question and each with the working binary as its control. The
subject is `Solo`, four characters, in both arms.

| probe | question | userprint (correct) | solo (truncates) |
|---|---|---|---|
| trap unless the length READ is 4 | is the length wrong at read time? | no trap | **no trap** |
| trap at loop exit unless idx is 4 | did the loop run to completion? | no trap | **TRAP** |
| trap unless the RELOADED length is 4 | does the length survive the loop body? | no trap | **TRAP** |

Read together: the length is read CORRECTLY, the loop nevertheless exits early,
and the value reloaded for the exit test is not the value that was stored. The
loop compares `idx` against a length it re-reads every iteration, and after the
first character that re-read no longer answers 4, so the loop ends and one
character is all that is printed.

**Why only this context**, from the byte diff above: in the entry sum printer
the printer's locals are SPILLED to `[rbp-0x28]` and `[rbp-0x30]` and reloaded
each iteration, while in user code they stay in registers, where nothing in the
loop body can reach them. Same emitter, same instructions, different storage.

**What is NOT yet distinguished, and it decides the repair:** whether the loop
body WRITES that slot (a colliding allocation) or the reload ADDRESSES a
different slot than the store did. Both are slot-allocation defects and both
produce exactly this reading; the probe cannot tell them apart, so neither is
claimed. Dump the stores in the loop body against the slot the reload names.

This is also the first mechanism in this row that survived its own test. The
three before it did not, and the difference was not care, it was that each of
these probes FAILS on the working arm and passes on the broken one, which is
what the earlier vacuous arms could never do.

### The byte diff, which is the sharpest evidence so far and still not a cause

Run 2026-09-01 on the matched pair (same text, same hosted target, same printer:
`print-uni "Solo"` answers `Solo`, `Solo` as a constructor name answers `S`).

**Correct case, every printer local in a REGISTER:**

```
48 89 C3   mov rbx, rax        ptr
48 8B 0B   mov rcx, [rbx]      length
49 89 CD   mov r13, rcx        len
4D 89 DE   mov r14, r11        idx
4D 39 EE   cmp r14, r13
```

**Truncating case, the same locals SPILLED and reloaded each iteration:**

```
49 8B 4D 00   mov rcx, [r13+0]      length
48 89 4D D8   mov [rbp-0x28], rcx   len spilled
4C 89 5D D0   mov [rbp-0x30], r11   idx spilled
4C 8B 45 D0   mov r8, [rbp-0x30]    reload idx
4C 8B 4D D8   mov r9, [rbp-0x28]    reload len
```

Counted across the three binaries (`mov [rbp-0x28],rcx` is the len spill):
`userprint` 0 spills 0 reloads, `solo` 1 and 1.

**The obvious reading is that the spill slots are wrong, and it is NOT
established.** The arm built to test it is VACUOUS and is recorded as such: a
`print-uni` surrounded by twelve live `let` bindings still printed `Solo`, but
the same byte census shows it never spilled the length (0 spills), so it never
entered the condition under test. It discriminates nothing, and the earlier
frame-count arm failed the same way.

**What the next session should do first.** Force the spill in a context known
correct, by construction rather than by hoping register pressure does it, and
see whether the truncation follows the SPILL or stays with the sum printer. If
it follows the spill, the subject is slot allocation in that path; if it does
not, the spill is a coincidence of the same context and the byte diff has been
read too eagerly, which is the failure this row has already made three times.

the bytes the sum path emits around the name print and compare them against the
same printer called from user code, where it is known correct; the difference
between those two is the whole question, and it is a diff rather than a theory.

## 2.16 -- DONE 2026-09-01 (reek): the hosted harnesses reach every eligible subject, and codex/test/ops is graded for the first time

`hosted-elf-test.ps1` selected `codex/test/*.codex` NON-recursively and capped at
`-Max 60`, so both hosted arms published "60 of 60" against an eligible population
they never named. The cap IS the corpus in that sentence and no reader can tell it
from a complete pass (L-DENOM).

Two changes, both in the selection rule, neither touching how a subject is graded:
the glob recurses, and `-Max 0` now means the whole eligible corpus. The DEFAULT
stays a cap of 60, because a bare invocation must not launch a sweep (Damian,
2026-09-01). The cap was never the lie; the missing denominator was, and that is
what is repaired. A subject is named by
its path under `codex\test` with forward slashes, so a top-level subject keeps the
bare name it always had and a nested one is `ops/real-approx-negate`; consumers
join that onto `$TestDir` unchanged. Every score line now says what it was drawn
FROM: `60 selected of 996 eligible`, never `60 of 60`.

**Measured 2026-09-01 at reek 20893.** The eligible population is **996**, not the
383 this campaign has been quoting: 383 top-level, plus forewords 296, apps 233,
ops 40, lib 35, ui 7, cost 1, examples 1. The register said "44 more under
`codex/test/ops`" and the eligible count there is **40** (46 `.codex`, 45 with an
oracle). Re-measured rather than carried (L-COUNT).

The top-level selection is BYTE-IDENTICAL to the old rule at 383 subjects,
`-Max 60` still selects the same first subjects (`act-let-scope`, `aesgcm256`,
`amp-after-call`), and a known kernel-service subject is still excluded. The
change is additive and the old rule did not move.

### The remaining real cluster is ONE missing primitive, not six mode problems

**Correction to this row's own earlier wording.** 2.16 said the six subjects
that moved from refusal to a wrong answer "need mode SEMANTICS now". That is
wrong, and the outputs say so plainly: plain, approx, trapping and saturating
all print the SAME wrong thing, so the mode is not involved at all.

A real prints as its raw f64 BIT PATTERN. `real-mode-show` answers
`plain: 4612811918334230528` where the oracle says `plain: 2.5`, and
4612811918334230528 IS 2.5; `real-approx` answers 4619567317775286272 for 7.0.
`wat-emit-show` routes anything that is not Text or Boolean to `$i64_to_text`,
and `wat-emit-entry-loop` picks `$wasi_print_i64` for any non-Text return, so
neither `show` nor the entry printer can render a real. **There is no
real-to-text anywhere in this plug.**

That single absence accounts for `real-approx`, `real-approx-modes`,
`real-mode-fields`, `real-mode-opening`, `real-mode-show`, `real-saturating`
and the real half of `unit-show`.

**Scope, so the next taker does not re-derive it.** The oracle is
`__real_to_text`, `codex/compiler/Emit/X86_64TextHelpers.codex:590`, a 225-line
section that emits the routine by hand: strip the sign bit, integer part by
`cvttsd2si`, fraction by repeated multiply. wasm has every instruction that
needs (`f64.trunc`, `f64.mul`, `i64.trunc_sat_f64_s`, `f64.convert_i64_s`), so
this is a prelude helper `$f64_to_text` plus two call sites, and the risk is
matching the oracle's FORMATTING exactly (digit count, trailing `.0`, negative
zero) rather than the arithmetic. Grade it on the seven subjects above; they
already spell out every case.

### No gate phase touches the wasm plug, so the batching trap does not apply here

Measured 2026-09-01 against `build/build.ps1` at main 20926: **the string
`wasm` does not appear in that file at all.** `plug-binary` builds
`riscv, arm64, t3isa, elf, pe, img` and its own prose says the transpiler and
text plugs are "NOT gated here"; `plug-smoke` runs `typescript, python, rust,
ptx`; `cross-smoke` is the cross-arch backends. So neither `-Internal` nor the
full gate compiles, builds or runs this plug.

Two consequences, and the second is the one that costs.

A wasm-plug CL cannot be verified by a gate, hollow or otherwise. The
`-Internal` scoping trap (a batch already submitted to a stream gates as
core+BVT+refusals because the phases key off `p4 opened`) is real and worth
obeying for the phases it governs, and it changes nothing for this plug,
because there is no phase to scope. `hosted-wasm-test.ps1` over a named slice
is not a convenience here, it is the ONLY instrument that grades this plug's
output against the bare-metal oracle.

And a green gate says nothing whatever about the wasm plug, while 51 page
modules built from it ship on the landing page. That is L-NOGATE's shape: not a
test going red unnoticed, but a whole backend outside every runner, so the
first thing that can notice a regression is a person opening the page.

### What is actually missing is measured from REFUSALS, not from a source grep

`wat-try-builtin` has an arm for 107 of the 264 names in
`codex/compiler/Types/Builtins.codex`. **157 missing is not the campaign's
number** and nobody should plan off it: most are kernel channels, VMX, MMIO,
ports, process spawn, UEFI and GPU, which a hosted user process cannot reach
and which the harness's own exclusion rule already refuses.

**Do not take the reachable subset by grepping the subjects for builtin names.**
Tried 2026-09-01 and it is wrong: it reported `fail` in 59 eligible subjects,
and reading six of them by eye, every one was the STRING `"FAIL"` in a
pass/fail label or the word in column-1 prose. `now`, `max`, `compare`, `abs`
and `force` collide the same way. A name census cannot answer this, because
these builtins are spelled like ordinary English and `.codex` files carry prose
by design.

The instrument already exists and needs no new code: **wat2wasm names the
builtin in its refusal.** An arm that is missing AND reached produces
`undefined local variable "$<name>"`, which is a measurement of what actually
blocks a subject rather than of what a file mentions. Collect those lines from
a slice run; that list is the work, in the order the corpus cares about.

**THE INSTRUMENT CHANGES SHAPE WHEN RED 20932 LANDS, and this paragraph is
written before it does.** That CL makes an unbound or arity-less name emit
`(unreachable) (; no wasm form for <name> ;)` instead of `(local.get $name)`,
so a missing arm will ASSEMBLE and trap at RUNTIME rather than fail wat2wasm
(red, 2026-09-01). The stderr-refusal reading above stops working at that
point, and reading it afterwards would report every missing arm as absent.
The successor is better anyway: grep the emitted `.wat` for `no wasm form for`,
which names EVERY missing builtin in the module rather than only the first one
that happened to stop the assembler. Grade by RUNNING, not by assembling.
Measured that way on `ops/*` and the default 60: `is_letter`, `__list_head`,
`vec_load_at`, `port_out_32`, `cpu_read_cr0`, `get_ticks`. The last three are
the out-of-scope class and belong to no lane.

### Selecting a slice is how this harness is meant to be run

**We do not run 996 (Damian, 2026-09-01).** The eligible count is the honest
DENOMINATOR, never a run target: it exists so a score cannot be read as a
corpus, and 996 subjects across two targets is a sweep.

`-Subject` takes wildcard patterns, expanded against the eligible set rather
than the directory, so a pattern can never select a subject the exclusion rule
refuses and a slice here is the same slice in the other arm.

```powershell
codex\plugs\wasm\hosted-wasm-test.ps1 -Subject 'ops/*' -Jobs 4      # 40, the operator corpus
codex\plugs\wasm\hosted-wasm-test.ps1 -Subject '*negate*' -Jobs 2   # 2, one defect
codex\plugs\elf\hosted-elf-test.ps1  -Subject 'ops/*' -Target windows  # the control for it
```

The score line still names the population, so a slice reads `2 matching
*negate* of 996 eligible` and stays honest without being a sweep. A pattern
matching nothing REFUSES (exit 2) rather than reporting a clean run over zero
subjects, which is the emptiest possible green.

The standing slice for this campaign is `ops/*`: 40 subjects, it is where the
input-shape gaps live, and it is the directory no cap could reach.

### The first grading of codex/test/ops, and its control

Measured on seed 278D8D7FDBC54D26 (main 20898, blu's COMPILER-34) and, before it,
on 2B69CDD246E7EE23: the two seeds give the SAME 17/23 with the same subjects and
the same errors, so the numbers below are not a property of either seed.

wasm over the 40 `ops/` subjects: **17 pass, 23 fail**. The x86-64 hosted arm over
the SAME 40: **39 pass, 1 fail**. The control is what makes the list mean
anything, and it moved the count: **22 are wasm parity gaps, not 23.**
`ops/real-mode-fields` is red on BOTH arms -- x86-64 exits `-1073741819`
(0xC0000005) -- so it is not evidence about the wasm plug, and it is an access
violation in the hosted x86-64 lift that nothing had graded. Unowned.

The 22 fall in two groups. **Thirteen are WAT2WASM-REFUSED `undefined local
variable`**, which is a builtin the plug has no arm for reaching the funcref path,
exactly the shape the harness comment predicts a grep cannot see:
`$to_real_trapping` (4), `$real_to_int` (3), `$is_letter`, `$__list_len`,
`$real_approx_from_int`, `$from_real_saturating`, `$to_real_saturating`,
`$from_real_trapping`, `$vec_load_at`. One more is a units literal
(`ops/unit-pattern-lit`, `unexpected token "sin"`). **Eight are wrong answers**:
`bounded-modes-smoke`, `int-min-literal`, `int-pow`, `real-approx`,
`real-approx-equality`, `real-approx-negate`, `unit-real-compare`, `unit-show`.

### negate on a Real, and the real-mode family: CLOSED 2026-09-01

**The site was `wat-try-builtin`, not `IrNegate`.** 2.16 first pointed the next
taker at `WasmEmitter.codex:834` and its operand type. That is the wrong path:
`negate` is a BUILTIN, so it is dispatched in `wat-try-builtin` (`:1579` when
read), which emitted an integer negation with no type test at all. Settled by
marking the builtin arm and requiring the emitted wat to move; the marker
appeared, so 834 was never the site. The arm now tests the operand type the way
834 already did.

**Nine builtins had no wasm arm** and each fell through `wat-try-builtin`s final
`else ""` to the funcref path, where the name becomes `(local.get $name)` and
wat2wasm refuses with `undefined local variable`. That empty string is
L-BAILVALUE exactly: a bail returning a VALUE the caller cannot tell from an
answer.

The mode conversions are IDENTITY here, and that follows from the
representation rather than from taste: a Real is its f64 bits either way, so
`to/from-real-trapping` and `to/from-real-saturating` (and their approx twins)
change only the type, which is what `from-real-approx` already did. Added with
them: `real-from-int`, `real-approx-from-int`, and `real-to-int` /
`real-approx-to-int` via `i64.trunc_sat_f64_s`.

**Measured on the `ops/*` slice: 17 pass 23 fail, to 23 pass 17 fail.** Six
subjects closed outright (`real-mode-compare`, `real-neg-neg`, `real-negate`,
`real-saturating-finite`, `real-trapping`, `real-approx-negate`). Six more moved
from WAT2WASM-REFUSED to a WRONG ANSWER and are still red: `real-approx-modes`,
`real-mode-fields`, `real-mode-opening`, `real-mode-show`, `real-saturating`,
`real-to-int-wide`. Removing the mechanism did not remove the loss (L-PARTIAL);
those six need mode SEMANTICS now, not a missing arm, and that is the next row.

**No regression, established by a control and not by argument.** The depot
revision rebuilt and run over the default 60 gives 44 pass 16 fail; the fix
gives the same 44/16 with every failing subject failing in both. One subject
advanced: `apps/classics-test` moved off `undefined local variable $real_to_int`
to a wrong answer. The fix state was hashed before the control ran and verified
after restoring it.

## 2.20 -- CONTRIBUTED by Steve Howell (PR pending, 2026-09-03): an emitted closure parameter was named `p0`, which zig refuses when the Codex source has its own `p0`

`zig-closure-pass` and `zig-closure-params` named a lambda's parameters `p0`,
`p1`, ... Zig forbids shadowing, so a Codex definition holding a local of the
same name emitted invalid zig -- "function parameter 'p0' shadows local
constant from outer scope" -- and nothing in the emitter could see it coming,
because the colliding name is one the SOURCE chose.

Update 54 made it live. `check-chapter`'s CHECK-REG instrumentation binds
`p0`..`p5` (`"CHECK-REG tdm=" & show (p1 - p0) & ...`), and `check-chapter`
also builds its slug cache with a comprehension, which is a closure. That put
a `p0` parameter inside a scope already holding a `p0` constant.

`_cp` puts closure parameters where `_Env`, `_ctx` and `__lam_` already are:
the emitter's own namespace, which a Codex identifier cannot sanitize into.
The class of defect is the general one -- an emitter naming anything in the
program's namespace is one source identifier away from a collision -- and this
fix closes the instance rather than the class.

MEASURED: the compiler transpiles and `native/codexir` builds. NOT MEASURED:
Damian's gate; no non-zig plug touched. Steve Howell's lane (COMPILER-13).

## 2.18 -- DONE 2026-09-01 (contributed by Steve Howell, PRs 111 and 112; absorbed by red): the wasm plug's silent wrong answers, the 4 MiB truncation, the 4 GiB ceiling and the corpus refusals; the zig plug streams

Both PRs were cut from Update 53 and rebased here onto the 60-of-60 emitter
(2.14, 2.16). PR 111's Real commit was the same decision 2.14 had already
landed (f64 bits in an i64 slot) and was dropped as a duplicate; every other
item was ported onto head, with PR 112's correction of 111's guard leak
applied inline (`ctx-deeper` builds a fresh `WasmCtx`; no `__record-set`).

**Silent wrong answers, each now a fixture in `codex/plugs/wasm/test` graded
against x86-64 by `wasm-e2e.ps1`:** `a ^ b` emitted `a * b` (`IrPowInt` had no
arm; `$cx_ipow`, negative exponent 0 as x86's `__ipow`; `pow-int-rt`); a text
literal only in a match GUARD never reached the string table so the compare
ran against address 0 (both walkers visit `b.guard`; `guard-string-rt`); a
`when` inside a guard overwrote the scrutinee local the arms below still read
(the local is per guard depth, `_s`, `_ss`, ...; `guard-nest-rt`, bare metal
205 where the plug printed a heap address); `show` of INT64_MIN printed
garbage (both print helpers work on the negative magnitude now); an unbound,
non-constructor, arity-less name was emitted as `(local.get $name)` and
`wat2wasm` refused by naming the builtin (now `(unreachable) (; no wasm form
for <n> ;)`, so the module assembles and only that path traps; Steve measured
161 of 169 corpus refusals as this shape); a field slot was read from the
wire's positional suffix before the receiver's type (inverted); `when` over a
Boolean emitted `(i64.const True)` (`wat-lit-pat-const`); `real-to-int` used
`i64.trunc_sat_f64_s`, which saturates NaN to 0 and positive overflow to
INT64_MAX where x86's `cvttsd2si` answers INT64_MIN for both
(`$cx_real_to_int`).

**The 4 MiB truncation.** `$read_serial_cce` and `$read_file_uni` reserved
4 MiB and read the rest of the wire while DISCARDING it, so a larger input
compiled as a prefix of itself and failed as `CDX3002` on a name the file
defines. Both start at 1 MiB and extend by re-bumping (`read-file-raw`'s
idiom). `$read_byte` reads 64 KB per `fd_read` and hands out one byte
(`$rd_len`/`$rd_pos`); `$read_file_raw` drains that buffer before reading the
stream itself. Steve measured 210 s to 24 s on a 2.9 MB input under node.

**The ceiling.** List literals, data sections, the elem list and the zig
plug's list literals were joined right-recursively (quadratic); each splits
its range in half and joins once, byte-identical by construction.
`$bump_alloc` grows in 256-page steps through one `$grow_by` (the list-append
fast path called `memory.grow` inline and now calls `$grow_by`); Steve
measured 56,000 one-page grows at 166.83 s under node against 0.21 s under
wasmtime, and 223 s to 18 s on the compiler compiling itself.
`check-emitted-runtime.ps1` (new) asserts those invariants on every emitted
module from `wasm-e2e.ps1`; it rejects the head-built module with 8
violations, which is its calibration.

**The import scan** emitted every definition twice in full to text-search for
`$blit_framebuf` and `$on_key_import`. The definitions are asked in the IR: a
call is a name in the HEAD of an apply spine and a bare mention is not
(`builtin-name-local-rt`). Neither import is reachable by a program that
assembles (`blit-framebuf` is not a foreword name; `$on_key_import` has no
producer); both flags are kept, the second asked with `wasm-no-builtin`, so
whoever adds the `on-key` arm has to touch the call site.

**Zig.** `emit-zig-list-elems` joins in halves. `emit-zig-chapter-stream`
emits one definition per heap bracket and carries the prelude shake answer
across the restore as two i64 masks (ceiling 128 parts, `@compileError` past
it). `ZigStdio`, which is the compile page's `zig-stdio.wasm` lens, streams
now; `emit-zig-chapter` stays for `ZigPlug`. Measured: the OLD and NEW
`zig-stdio.wasm` fed the same `ctor-eq-rt` IR under wasmtime emit IDENTICAL
bytes (19,824, same SHA-256).

**One fix of ours the regression run found, not in either PR:**
`param-shadow-rt` was RED AT HEAD (control: a plug built from the depot
emitter refuses the module, `undefined local variable "$p_sh2"`).
`emit-wat-def` started the locals collector with an empty accumulator, so the
first `let p` over a parameter `p` allocated slot `p`, which
`locals-minus-params` stripped, while emission (seeded with the params) used
`p_sh1`. The collector is now seeded with the params. Nothing in any gate runs
`wasm-e2e` (L-NOGATE), which is how a red fixture sat at head.

**Measured:** `wasm-e2e` over all 31 fixtures (27 existing plus the four
above), 31 of 31 agree with x86-64, every module passing the runtime
invariants. **Not taken, a commander's call (CurrentPlan's narrow test: a
technical trade-off with a defensible answer is not Damian's):** PR 112's
`wasm-exports` declaration (a chapter names its own exports; the 484-name
allowlist decides where there is none, unchanged). Steve calls it a design
call and it is; the same goes for his issue 113 proposal that the IR carry a
source name and a suggested unique spelling per binding, and issue 110's ask
that `inline-single-caller`'s erasure be visible at the language boundary.
**RULED 2026-09-01 (root): `wasm-exports` is TAKEN as PR 112 proposed** (a
chapter's own declaration wins; the 484-name allowlist applies only where a
chapter declares nothing), and it carries a census: once every shipped page
module declares, the allowlist is deleted, because a list drawn from unrelated
applications is a leak in one direction and a coincidence in the other.
reek's, after the campaign's control fix (2.17 and the 2.16 crash). Issue 113
is ruled in `compiler-backlog.md` COMPILER-38 (the IR uniquifies binders in
lowering; red's). Issue 110 is ruled (Damian, 2026-09-01): a declared export
is exempt from `inline-single-caller`, so the `wasm-exports` list's scope
extends to the pass pipeline; `compiler-backlog.md` COMPILER-39.
**Still open from his reports:** `Text` literal PATTERNS splice the spelling
into `(i64.const sin)` and `wat2wasm` refuses (needs the literal in the table,
a lookup at the site and `$text_eq`); ~45 runtime helpers carry no prefix so
each is a name a program may not use (4 of his 12 remaining corpus refusals);
6 SIMD type mismatches; 26 of 526 corpus programs differ from their
`.expected`, the largest group being reals printing as bit patterns (2.14's
open real-to-text).

**FOLLOW-UP, OPEN (red or reek, whoever reaches `WasmEmitter.codex` first;
safe since main 20995 landed the COMPILER-38 seed DE664C4E, L-FALLBACK):**
delete the wasm plug's private scoping repair, now dead code under
COMPILER-38: the `shadow` field of `WasmCtx` and its threading through
`ctx-with`, `count-occurrences`, `wat-shadow-slot`, `shadow-push`,
`locals-add-shadow` and the `IrLet` emission that indexes by scope depth,
returning `IrLet` to a plain `local.set $name`. Grade by `wasm-e2e.ps1` over
the whole test dir (31 fixtures, all green at 20969) and by
`hosted-wasm-test.ps1`, since `act-let-scope`, `let-shadow-scope`,
`scope-let-arm-global`, `inline-single-caller` and `param-shadow-rt` are the
programs that would move. reek's before-baseline on seed DE664C4E, measured
2026-09-01: `act-let-scope` PASSES and `hosted-wasm-test` over `ops/*` is 27
pass, 14 fail; the deletion must leave both exactly there. reek declined the
refactor (on the 2.17 control fix), so it is red's next session's or whoever
reaches the file. The zig plug's `renamed-from`/`renamed-to` also
serves zig's OWN rule (a nested function may not shadow an enclosing
parameter, which the desugarer's `for p in pats` produces) and is NOT
obviously dead; assess with a zig e2e before touching it. The C# plug has no
private repair.

## 2.15 -- OPEN (Damian, 2026-08-31; queued, unowned): text plugs emit CCE encoding code a simple program never needs, and the emitted `opening` round-trips `to-cce (from-cce x)`

Damian's observation at the emitted output, not yet censused: some text
emitters emit their CCE encode/decode helpers for programs whose text is plain
ASCII literals, and the emitted `opening` wraps a value in a
decode-then-encode pair, an encoding dance that changes nothing. Some text
emitters do NOT do this, and they are the control: the plugs whose `opening`
comes out clean show the shape the others should produce.

What the taker does, in order:

1. **The census first.** Compile one simple program (a `print-line` of a
   literal, and one with `text-concat`) through every text plug
   (`codex/plugs/<plug>/run.ps1`; `build/plug-oracle-test.ps1` names the
   plugs whose runtime is on this box) and grep each emitted file for (a) the
   CCE helper definitions it carries and (b) a decode-then-encode pair on one
   expression in `opening`. Publish the table in this row (plug, helper
   lines, round-trip sites, runnable or text-only) BEFORE fixing anything:
   the observation is Damian's eye, the count is the taker's (L-COUNT). A
   plug with no runtime here (1.14, 1.46) is censused on its emitted text
   and cannot be run; say so in its row.
2. **The rule.** A CCE helper is emitted only when the program reaches it
   (2.04's shape: the zig prelude emits only the parts the program reaches),
   and a value is never decoded and re-encoded on one path: text stays in
   the plug's native form until an I/O boundary needs CCE, and stays CCE
   until a native operation needs the other.
3. **The proof.** `build/plug-oracle-test.ps1` (49 of 49 values per plug)
   and each plug's own arms byte-identical before and after; the drop in
   emitted size per plug is the measurement, and a plug whose output does
   not shrink was not carrying the dance.

Boundary: the CCE layer itself (R-CCE, `Foreword chapter CCE`) is untouched;
this row is about emitting it where nothing needs it. One plug per CL
(R-ONE); the compiler is not on the path, so no token.
