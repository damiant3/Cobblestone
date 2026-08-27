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

**AND THE SECOND CLICK WENT GREEN. Damian's browser, main-thread fallback:
2,460,178 characters in 19.0 s, hash `6F0A4122..` computed IN THE TAB,
equal to the bare-metal anchor to all 64 characters.** The compiler built
itself in a real browser and proved its output byte-identical to bare
metal, witnessed on 2026-08-25. "The compiler runs in a browser" is now a
sentence this register permits, with its conditions attached: main thread
until the emit spine is de-recursed, `decks=125`, the page's own anchor. Suite arms never drove TEXT emission at
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

**The durable 1.14 close for browsers remains compiler-side**: de-recurse
or trampoline the emit spine (`codex-emit-expr`'s tree descent) so a
worker's stack suffices. Seed-affecting, token, Update 51 scope per red.

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
question and no longer gates anything.

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
stack. Non-tail depth (a real frame obligation) remains the honest residue on
every conventional target, wasm included.

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
the same to every caller in the tree; that half is unclaimed and is written up
in `OperatorsManual.md` above the flag table. Here the guest
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

**1.57 -- JAVA HALF DONE 2026-08-25 (reek). The RISCV half does not
reproduce and the row's premise about it needs narrowing.**

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

**riscv does NOT reproduce, and RENODE IS INSTALLED so it can be run
here.** The row filed the runtime consequence as inferred and asked for
depot-side verification; this is it. `build/test-cross.ps1 -Arch riscv64`
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

**NOT fixed, and reported here as open.** A record FIELD typed by a unit
family arrives as an `ATypeExpr` and takes `emit-zig-atype`, whose
`ANamedType` arm emits any unrecognized name verbatim
(`ZigEmitter.codex:445-447`): `ob_sample_rate: Frequency,` against a zig that
has never heard of `Frequency`. **11 more programs.** That same `else` also
emits a source-level TYPE VARIABLE verbatim -- `queue-test` emits
`QueueS(T52){ .front = cx_ll_empty(a), ... }`, where the definition declares
`comptime T52` and the field's element type is spelled `a` in the same
expression. And that path takes no scope and has no refusal, so every guard
from 1.85 walks straight past it. One `else`, two symptoms, twelve programs.

**1.90 -- OPEN, the zig plug's runtime prelude shadows user top-level names
with its own locals and parameters, and nothing declares them reserved.**
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

**Both candidate fixes are larger than they look.** Adding the 66 names to
`zig-prelude-decls` renames `i`, `n` and `s` through all 55 `zig-sanitize`
call sites -- type names, constructor names, parameters, definitions -- a
large blast radius through rename machinery to buy two programs. Renaming the
prelude's own 66 identifiers is correct and confined to text this plug owns,
but it is a rewrite over 931 lines of zig embedded in Codex string literals
where a subtle miss breaks every emitted program. Either wants its own change
and a check that re-derives the surface from emitted output -- and that check
must count parameters, or it will certify the same short list we did.
