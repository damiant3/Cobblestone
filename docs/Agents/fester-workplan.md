# fester -- workplan

*Status, not journal. Per-CL history is in Perforce. Durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`; durable machine facts are in
`docs/OperatorsManual.md` and `docs/ArchitectsSketchbook.md`. This file is the
current picture and the next moves only. Keep it under ~80 lines.*

## Status: no red gate. Clean tree, nothing open/shelved, no token held. Main at 10409 -- MERGE DOWN first.

### 2026-07-22 (latest) -- 7.17 CLOSED end to end; two fireworks CLs; 7.20 AI quire; filed 7.31

All landed NO SEED (foreword/app/test only; compiler untouched, Sut===seed each gate):

| main | what |
|---|---|
| 10358 | 7.17 Ctrl/Alt/Caps + poll-mods; 6 apps wired (dormant ctrl-bug fixed); keys-mods |
| 10360 | fireworks uses the common UI KeyInput poll-key (dropped its local one) |
| 10369 | frozen fireworks-usa250.cdx regenerated from head source (frame-verified) |
| 10379 | 7.20: foreword-all-compile cites the whole AI quire (177->208); val later took it to all/415 and CLOSED 7.20 at 10394 |
| 10403 | 7.17 CLOSED: numpad decodes under NumLock + browser Ctrl chords wired |

**7.17 fully closed.** poll-key latch (36752): shift1/ctrl2/alt4/caps8/numlock16/e0-oneshot32.
Numpad: NumLock toggle + 0xE0 one-shot tell a dedicated arrow (NumLock-independent) from a bare
numpad key (digit on / arrow off); numpad -/+ added. Browser: mod constants realigned to UI Event,
poll-mods wired, key-upper fold. Tests keys-numpad + browser-keys (NOT in BVT -- run by hand).
See [[poll-key-shift]].

**Filed 7.31 (a 7.17 consequence, NOT fixed):** the case-sensitivity migration made F-keys and
lowercase letters collide -- F5 and `t` are both 116 (F1-F12 = DOM 112-123 = lowercase p-{). A
poll-key CONTRACT decision (move F-keys out of 97-122 / letters carry DOM codes / accept it).
Browser plain-`t` hits key-f5 -> Reload.

**Two collisions cost churn** (see [[foreword-all-compile-720]]): val did 7.20 (10376) and nearby
work while I was queued; I twice nearly abandoned VALID work on a misread. LESSON: `p4 print
BACKLOG | Select-String <entry>` + `p4 describe <their CL>` before calling your work redundant.
"Still in the file" == not done.

**NEXT: MERGE DOWN from main@10409** (blu 5.15 MQTT, reek 3.1/LirRetarget landed after me), then
pick a fresh item OUTSIDE val (keyboard/foreword), reek (LIR/codegen), blu (IoT/MQTT).

### 2026-07-22 -- 7.17 consumer audit done: letter shortcuts folded

**One CL: 10329 (fester) -> main 10330. NO SEED.** The shift migration made
poll-key return lowercase, so apps reading a letter as a COMMAND SHORTCUT
silently stopped firing -- a runtime break the app-class sweep cannot see (it
still compiles). Read every consumer; 8 handlers now fold through new
`key-upper` (collab, helm, vision, secrets, fileshare, diagram x3, browser
MediaPlayer). Correctly left alone, and the distinction is the point:
circuits/guios/ParentalUI read raw SCANCODES from InputSource (unaffected
path); browser PageCompiler's `char-code == 72` are CCE tokenizer values, not
keys (folding them corrupts the parser); fireworks has its own poll-key. All 8
app entries compiled clean. See [[poll-key-shift]]. 7.17 stays open for
Ctrl/Alt chords, Caps Lock, 0xE0 nav.

### 2026-07-22 (latest) -- 7.17 shift half: poll-key decodes shift, passphrases migrated

**One CL: 10317 (fester) -> main 10318. NO SEED** (poll-key is UI foreword,
unreached by the compiler; Sut content byte-identical to seed). Gate green.

`poll-key` now decodes shift via a latch cell (36752). `uefi-read-key` masks
to a full byte so break codes (make|0x80) survive, which is what makes key
release observable. Letters are case-sensitive (lowercase unshifted); number
row + punctuation shift to symbols. Pins: `keys-shift` (new, 65/97/33),
`keys-sidecar` + `idm-read-line` migrated to 97/98.

**Damian chose MIGRATE** (case-sensitive passphrases): every identity
provisioned before this is locked out, accepted deliberately.

**Runtime caveat left on the re-scoped 7.17, invisible to the app sweep:**
poll-key's letter contract flipped from uppercase-IDENTITY to case-sensitive,
so an app reading a letter as a shortcut (`== 81` for Q) now needs the
lowercase code. Compiles fine, so the sweep can't see it -- needs a read of
the ~16 consumers. Sampled clean: FirstBoot, UefiConsole, secrets. 7.17 now
covers Ctrl/Alt chords, Caps Lock, 0xE0 nav, and that audit. See
[[poll-key-shift]]; keyboard tests use `-keys-file` (exact scancodes, no
auto-break) and are not in the BVT -- run them by hand.

### 2026-07-22 (latest) -- 3.24 closed: ir-check/occ-report report in IR mode

**One CL: 10290 (fester) -> main 10291. CARRIES A SEED (B1803436, re-take it).**
One-pass fixed point, constants.hash unchanged, self-verify green.

Merging the ir-check/occ diagnostic bags into `compile-frontend-passes` killed
the compiler in `bag-add` (a Text read where a Diagnostic belongs) on
`codex/test/arithmetic`. **It was a register-spill cliff, not a lifetime
error** -- the entry's own empty-`[]` repro never touches `passed` yet still
crashes, and `compile-frontend-cdx` merges 14 bags fine. Fixed by moving the
merge into a helper (`frontend-bag-with-passes`) so the giant let-chain's
binding count is unchanged, plus `print-bag-notices` in `emit-ir-uni` so the
occ INFOS actually surface (IR-CCE, the plug wire, left untouched). Pin
`build/ir-passes-test.ps1`: 0 OCC lines pre-fix, 30 after; fails on the
pre-fix seed. See [[ir-passes-report]].

**Read the evidence in a BACKLOG entry before running experiments** -- the
literal-`[]` detail already refuted one of its two stated hypotheses.

### 2026-07-22 (latest) -- 7.29 closed: a reader for the diagnostic catalogue

**One CL: 10277 (fester) -> main 10278. NO SEED.** `build/check-cdx-registry.ps1`
reads `CdxCodes.codex` (which nothing in the compiler reads -- DCE prunes it)
against every `cdx-*` raise site across `codex/compiler`, and fails `build.ps1`
on: raised-but-unregistered, registered-but-raised-nowhere, and a Name that is
not the PascalCase of its constant. Wired in beside check-sidecars. **First run
found 13 undocumented codes** (CDX1021-1025/1032/1040 parser, 2003/2005/2006/
2060/2068/2085 checker) -- all rowed, 101/101.

**No seed, and how it was measured:** the registry is DCE-pruned, so Sut.cdx
CONTENT (bytes 8-39, ignoring the signature) is byte-identical to the depot
seed. The raw file hash of an unsigned probe vs the signed seed ALWAYS differs
-- do not read that as a seed change; use the content hash. Gate green, one
pass, constants.hash unchanged. All three guard axes fired under injection.
It does NOT check summary prose (no build-time check can) and a Codex test
can't read the registry (compiler chapters have no quire -- the 7.25 blocker).
See [[cdx-registry-reader]] and the ExaminersAssay section.

### 2026-07-22 (later) -- 4.18/4.19 closed: real-mode AP trampoline + proc 0 pinned

**One CL: 10265 (fester) -> main 10266. CARRIES A SEED.** Depot main seed is
now `AB589EC76B4A3E7C...` -- re-take it, never quote it. Two-pass codegen
build (install `NewSeed.cdx`, rebuild for one-pass `SUT === stage1`, install
signed `Sut.cdx`). Self-verify GREEN. All seven `smp-*` tests match
`.expected` at `-smp 4` against the shipped seed. **`build/test.ps1` NOT
RUN.**

**4.18** -- APs boot the silicon way. Start-up IPI carries page number
(`ap-sipi-vector`); AP begins in real mode at `vector<<12` and a 177-byte
`ap-tramp-blob` (X86_64Boot) climbs it to long mode. codex-vm starts each AP
in real mode with reset control regs and nothing in RDI (was: 64-bit entry +
CR3/EFER/GDT/id from the host, impossible on metal). Core id is a dense
counter via locked xadd on new cell **36256**.

**4.19** -- proc 0's affinity was `-1` while three guards already forbade
migration; changed to `0` to match. New pin `codex/test/smp-proc0-pinned`.

**The reusable part: an honest guest bring-up exposed three defects the host
was masking** -- the AP entry never `lidt`'d (host pointed IDTR at the IDT),
CR4 lacked OSFXSR, and the trampoline GDT had to be null/64-code/data/32-code
to match the runtime GDT the core loads. See [[smp-trampoline]] and the
Architect's Sketchbook SMP section. **OTHER AGENTS touching a device or VM
change: `smp-*` at `-smp 4` now exercises a real mode-climb, not a
host-configured AP -- if you break boot/GDT/IDT/CR4, these are what catch it.**

### 2026-07-22 -- 2.40 closed, plug arithmetic corrected, plug output now executed

Four CLs to main, in order. **Only the first carries a seed.**

| main | what |
|---|---|
| 10183 | **BACKLOG 2.40: `int-rem`.** CARRIES A SEED. |
| 10197 | plug `/` corrected in six plugs; `build/plug-oracle-test.ps1` added |
| 10207 | `int-mod` made Euclidean in every text plug |
| 10222 | plug-oracle gains a javascript row |

**Depot seed at main is `74495237B996E662...`** -- that is NOT mine; mine
(`58856730...`) was superseded the same day. Re-take it, never quote it.

**`int-rem` is a new builtin: the TRUNCATING remainder**, sign of the
dividend, the one that satisfies `a == (a / b) * b + int-rem a b`.
`int-mod` stays Euclidean and is unchanged. `DevelopersGuide.md` now
documents the rounding rule for all three operators; it documented none
of them before. Pin: `codex/test/int-rem`.

**It needed no plug arms, and the backlog entry predicting 34 of them was
wrong.** A saturated `int-rem a b` is rewritten at LOWERING into
`IrBinary IrRemInt` (`lower-int-rem-apply`, beside the `compare` case),
and 50 of the 53 plugs already carried an `IrRemInt` arm -- the three that
did not (elf, pe, img) take CDX and emit no expressions. **The name never
reaches a plug, so BACKLOG 3.22's silent-miss hazard does not arise.**
Check that kind of coverage BEFORE writing per-plug arms.

**Gates:** `build/build.ps1` green on every CL; 10183 was the two-pass
codegen case (install `NewSeed.cdx`, rebuild, then install signed
`Sut.cdx`), the other three were one-pass with `Sut == depot seed`.
Self-verify GREEN on the seed CL. **`build/test.ps1` NOT RUN this
session** -- there is no battery tally for any of this.

**OTHER AGENTS -- two things that touch you:**
- **Everyone:** integer arithmetic through the TEXT PLUGS changed. Six
  plugs floored `/` where the compiler truncates, and roughly fifteen
  answered a non-Euclidean `int-mod`. If you have output from a
  transpiler plug that you captured before today and are comparing
  against, **re-generate it** -- negative operands legitimately move.
- **Anyone touching `build/run-plug.ps1` or `run-plug-chain.ps1`:** both
  were rewritten. The parameter is now `-InFile` (it was `Input`, which
  PowerShell's automatic `$Input` shadows, so neither script could be
  invoked at all), and the request is now FRAMED
  (`le32(1 + len) ++ tag ++ body`) because that is what the plugs read.
  **The reply is not framed** -- strip a header only when its length
  field proves one is there, or you eat the first five characters.

### 2026-07-21 -- BACKLOG 3.11 closed (historical)

**3.11 landed: 10133 (renamed 10137) -> main 10138. CARRIES A SEED.** Then
**main 10154**, docs-only: a numbering correction, see below.

`a / 2^k` lowered to a bare `sar`, which floors, while every other divisor
lowers to `idiv`, which truncates. The shipping compiler answered
`-7 / 2 = -4` and `-1 / 8 = -1`, silently, with no diagnostic. The pow2
shift/mask form is now emitted **only when the dividend is proven
non-negative** (`pow2-dividend-nonneg`, beside the range prover in
`X86_64Compound.codex`); anything else falls through to `idiv`, which IS the
semantics rather than an approximation of it. Pinned by
`codex/test/div-negative-pow2`, which prints the pow2 and general columns side
by side and fails on the pre-fix compiler.

**Gates as run:** `build/build.ps1` twice -- first GREEN with `Sut != stage1`
(the two-pass codegen case), second GREEN with `(SUT === stage1 -- hard fixed
point in one pass)`, `constants.hash` unchanged. Self-verify GREEN.
**`build/test.ps1` NOT RUN**; no battery tally exists for this work.

**OTHER AGENTS -- three things that touch you:**
- **reek:** this change edits `IR/Lir.codex` and `Emit/X86_64Lir.codex`. The
  power-of-two decision **moved to lowering** rather than being guarded at the
  three places that read `lir-imm-pow2` (selector, the div-position map that
  models the RAX/RDX clobber, the register hinter) -- guarding those
  individually makes them disagree about whether the instruction clobbers the
  fixed pair. If you widen the whitelist over division, keep the decision at
  lowering. `X86_64Compound.codex` merged clean against your 2.34 (4 mine, 7
  yours, 0 conflicting).
- **Everyone:** `bench/codex/collatz` grew 16 bytes and the
  `ArchitectsSketchbook.md` bench row is flagged stale with the reason. The
  other eight shapes are byte-identical. `n` is an unbounded `Integer`, so it
  loses the shortcut; bounding the type buys it back.
- **`int-mod` is untouched** and measured so in the pin. CL 9961's cross-lane
  mod alignment stands.

**Numbering mistake, corrected -- worth not repeating.** 3.11 was closed by
REWRITING the entry in place for a different gap found while closing it, so a
CL pointing at 3.11 resolved to something it never described. Corrected in
10154: 3.11 deleted, the new gap refiled as **2.40**. Closing an entry and
filing a new one are two entries, never one edit.

**NEXT: BACKLOG 2.40, `int-rem`.** Damian asked for it directly. `/` truncates
now, but the only remainder Codex exposes is Euclidean `int-mod`, so the two
do not pair and the truncating remainder `idiv` already leaves in RDX has no
user-facing name. Compiler half is one `BuiltinSpec` row plus an emit arm that
is `emit-int-mod-builtin` without its correction. **The cost is the fan-out:
34 text plugs carry an `int-mod` arm and each needs an `int-rem` beside it,
plus both natives. Do not ship the builtin without the arms** -- per 3.22 a
name a plug lacks is a silently broken call with no diagnostic, so a
half-landed `int-rem` is worse than none.

Landed on main: **9817** (7.8 C64 SID filter / ring mod / hard sync / `$D400-$D7FF`
routing / in-loop streaming; 7.9 GUI harness retry), **9835** (`apps/c64/OdeToJoy`),
**9850** (7.3 `db-full-test`, 48/48), **9867** (7.5), **9911** (codex-vm resolves
the MMIO device by gpa before decoding). Backlog closes 9818 / 9851 / 9868 / 9901.
**7.8, 7.9, 7.3 and 7.5 are closed and deleted from BACKLOG.** No seed was built or
submitted: `Sut.cdx == seed/Codex.cdx` at every gate, `constants.hash` unchanged.
`build/test.ps1` NOT run, so there is no battery tally for that work.

**4.5, 4.20, 7.6 and 7.26 are closed** (CLs 9983, 10057, 10065, and the CL this
line ships with). Both TCO miscompiles are fixed and pinned; see the section
below. Each carried a seed, and a codegen change is a **two-pass** rebuild --
install `NewSeed.cdx`, build again for `SUT === stage1`, and only then install
the signed `Sut.cdx`.

**App-domain work left BACKLOG.md on 2026-07-21** (CL 10074): it lives in
`apps/<app>/<app>-backlog.md`, with text plugs in their own quire. Do not put an
app feature row back in the register.

## For blu and reek: 7.6 is FIXED, and a second miscompile in the same planner is not

**7.6 is closed.** The tree emitter's TCO shuffle held the one complex tail
argument in a `load-local` scratch register across the direct moves, and
`load-local` hands its three scratch registers out on a rotation, so the third
direct argument that read a spilled local reclaimed the register the value was
sitting in. Five parameters put the fifth in a spill slot, which is what made its
argument the complex one. The parameter was then assigned the *fourth* argument's
value: `active-player` got `next-turn`, the alternation stopped, and the counter
never advanced. One-line predicate fix in `emit-tail-single-complex`
(`Emit/X86_64.codex`), pinned by `codex/test/tco-shuffle-spill` (answers 0 before,
4 after) and `codex/test/apps/hexwar-run`. GamesDemo now runs all 20 games in
0.1 s; it hung indefinitely on the depot seed.

**If you have a hang that looked like 7.6, re-test it before assuming it is
covered.** The signature was specific: five parameters, at least three of the
other arguments spilled locals.

**7.26 is closed too.** `is-direct-tail-binop` accepted a binop whose left
operand was *any* expression when the right was an int literal, but
`tco-arg-reads` cannot see through anything that is not a name, so the planner
overwrote parameters such an argument still read: `1001` where `101` is correct,
silently. The fix is the deletion the file's own design prose already implied --
require an `IrName` left -- and the seed came out **200 bytes smaller**. Pinned
by `codex/test/tco-direct-arg-reads`.

**The rule that generalises, now written into the prose at 374-387: the direct
shapes and `tco-arg-reads` are ONE change, never two.** Widening what counts as
a direct tail argument without teaching the reads walk to see the new shape
reintroduces exactly this bug, and it reintroduces it silently.

Both survived because these functions are **tree-emitted**
(`info CDX4030: LIR <fn>: (not in the LIR whitelist)`), so CDX9006/9007/9008 never
see them. **The tree emitter's TCO shuffle still has no verifier** -- that is the
structural gap behind both, and neither fix supplies one.

**CORRECTION -- this file published the wrong explanation of the runaway signature,
and it was being read fleet-wide.** It said `MMIO: cannot size instruction at
RIP=0x100125` fires once per new 2 MB page because `handle_device_mmio` printed
before resolving the device. The ordering was real and is fixed (CL 9911), but that
was **not** why the message fired: it only ever fires when the faulting instruction
cannot be SIZED, and ordinary first touches size fine. The one undecodable
instruction in the runtime is the `rep stosb` (`f3 aa`) at `__alloc`+17 =
**0x100125**. Measured on both the old and new binaries, a guest allocating 640 MB
produces ~150 demand-commit exits and **zero** of these messages. So the flood is
specific to that runaway's huge single `rep stosb` span, not to demand paging.
**Do not expect it on a healthy workload, and do not read its absence as evidence
that a change fixed anything.** To tell a livelock from a runaway, group the gpas:
repeats = livelock, unique and ascending = the guest is allocating.

## App debt (BACKLOG 7): drained, and the instrument is on-demand

`build/sweep-app-classes.ps1 -Check -Jobs 3` = **259 clean / 6 known-dirty / 0
regressions** on current main. The six are in `build/app-sweep-baseline.txt` and are
deliberately left (missing APIs, not drift). **Damian DECLINED an app compile gate
(7.11): compiler work must not be coupled to app drift.** Do not re-propose a
`build.ps1` app leg. Use `-Jobs 3` or lower when a number will be quoted -- higher
job counts produce units that exit 4 with zero diagnostics under host contention.

Two rules for scoping anything in section 7. **`CDX0001: too many errors` hides the
tail, so every class count is a FLOOR** -- compile a unit until the cap stops firing
before estimating. **A unit that fails to PARSE has never been TYPE-CHECKED**, so
its recorded defects describe the parse layer only; for `apps/browser` the
type-check layer was the larger half. And **the sweep measures COMPILES, not
CORRECT** -- CL 9649's literal-pattern miscompile left two apps giving confident
wrong answers while counted clean. Never quote "N clean" as health without that.

## What I usually work on

apps/, `codex/os/kernel` + `tools/codex-vm.c`, capability/boot/UEFI, the
bounded/narrowing corner of the type checker. **This is habit, not ownership --
there are no lanes** (Damian, 2026-07-20). If you find a bug, fix it wherever it
lives; a workplan tells you who might have a file open, which is a merge concern,
not a permission concern. Reading these as boundaries once cost eighteen app units:
`WebServer.codex` was reported four times as "val's" instead of being fixed, and it
was ten mechanical effect-row sites (CL 9551).

## Open here (BACKLOG)

- **7.27** -- HexWar terminates but resolves no combat: thirteen scenarios, every
  one a draw with zero losses and zero VP. Recorded so the new golden's identical
  rows are not read as proof the combat model works.
- 4.6 / 4.8 / 4.17 -- GPU dispatch over polled serial; NIC RX one exit per word
  (read the INSW *exit path*, not the guest's `rep insw`); compute bridge serves 2
  of GpuProxy's 17 ops.
- 2.21 media ops: Camera is an effect-only gap now (transport exists via
  `GopUsbCam`; `capture` / `capture-raw` are declared with no implementation, so
  performing one dies at emit with CDX2040). Location and Sensors need a
  stub-vs-defer call from Damian.
- 7.17 shift latch -- a MIGRATION (folds existing passphrases uppercase). Damian's
  call before it moves.
- 4.9 / 4.13 -- **never ask for a stick to be flashed** (`TheSilentKeyboard.md`
  R-1..R-7). Every hub topology codex-vm can present is now serviced; what is left
  needs a bus analyser, not code.

## For other agents

- `CODEX_VM_BOT_TRACE=1` narrates the Bulk-Only Transport path and the transfer
  doorbell. A rejected command block and an unconfigured endpoint are both silence;
  this tells them apart. `dq=0x0` on a doorbell means the context was wiped.
- **The standing gate does NOT cover MMIO.** For a device or VM change run
  `boards-test.ps1` + `hda-audio` / `mic-peak` / `display-ops` + `smp-*` at `-Smp 4`.
  Add a device with `mmio_decode`, not a fourth register heuristic.
- **`tools/codex-vm.exe` is a versioned P4 binary.** `build-vm.ps1` cannot relink it
  until `p4 edit tools/codex-vm.exe`, or you get LNK1104.
- **`p4 copy --from <stream>` copies files you never touched. Read the list every
  time.** A copy-up here once opened another agent's workplan, blanked on this
  stream, and submitting would have deleted it from main. **`p4 merge` cannot repair
  a file blanked downstream of an integrate** -- restore by content with `p4 print -o`.
- **`p4 shelve -f` does not remove files that have left the CL.** A file reverted out
  comes back on the next unshelve, silently. Check `p4 opened -c <CL>` after it.
- When main moves while you hold the token the copy-up is refused and you merge down
  again; a blanket `p4 resolve -at` on that second pass will RESURRECT backlog rows
  you just deleted. Use `-am` where you have edits, and count the rows afterwards.
- **The gate does not compile apps.** After a merge-down, recompile the app your CL
  is about, or a merge-induced break ships unseen.

Push blockers on Damian: stale `seed/Codex.img` rebuild; poison build before publish.
