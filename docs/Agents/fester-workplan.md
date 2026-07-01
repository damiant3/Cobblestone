# Fester Workplan

Updated 2026-06-30. Stream `//Codex/fester` (child of `//Codex/main`);
fester == main as of CL 6406. Copy-up client `BigWhite_Codex_fester_main`,
root `D:\Projects\NewRepository-fester-main`.

## Current state (all green, clean tree)

- Seed `seed/Codex.cdx` = SHA `7E66DB86...` (committed, one-pass).
- This session submitted: **CL 6413** (docs grooming — 34 designs
  Active->Done + 14 status-header fixes) and **CL 6414** (merge-down
  from main: val proof-equality TypeEnv/Unifier + proof tests +
  Induction.md, blu riscv plug, seed). Build verified one-pass green
  after the merge.
- Routine gate is `build/build.ps1` (BVT + self-host fixed point) ONLY.
  Full `test.ps1 -Apps` (~20 min) is rare and not probative for the
  compiler. See memory [[gate-policy-bvt-plus-selfhost]].

## PRIMARY TASK: full UTF-8 <-> CCE at the I/O boundaries (all tiers)

Damian's directive (2026-06-30): "fix the IO boundaries so we don't
strip the characters we support -- everything including chinese and
arabic." Confirmed via AskUserQuestion 2026-06-30: **Full, all tiers
(CJK + Arabic). Build it now.**

### CORRECTED MODEL (2026-06-30 deep dive -- supersedes "verbatim/excise" below)

The earlier "excise CCE / verbatim both boundaries" idea is REFUTED.
Internal text is frequency-coded CCE and the output CCE->unicode
conversion is load-bearing for ALL text (proven: verbatim-both broke the
build's SIGN step -- print-line-uni emitted raw CCE bytes). Keep CCE
internal. The real bug: the compiler only ever implemented UTF-8->CCE
for ASCII on INPUT and DROPS multibyte; and it carries an INCOMPLETE,
DIVERGENT CCE model vs the foreword.

Two divergent CCE models:
- Compiler `X86_64State.codex` (~line 190): inlined `cce-to-unicode-table`
  + `tier1-slice-bases` = 16 unicode slices incl CJK(19968) all as 2-byte
  CCE. Output `__cce_print_multi` emits <=2-byte UTF-8 -> STRUCTURALLY
  CANNOT emit CJK/em-dash (3-byte UTF-8). Incomplete stopgap.
- Foreword `codex/foreword/core/CCE.codex`: COMPLETE tier0(128) +
  tier1(11 blocks, 2-byte) + tier2(10 blocks incl CJK 19968, Hangul
  44032, General-Punct 8192[em-dash], emoji 127744; 3-byte CCE).
  `from-unicode`/`to-unicode`/`cce-encode`/`cce-decode-at` PROVEN to
  round-trip A/space/e-acute/Arabic(1575)/你(20320)/好/em-dash(8212)/😀
  (standalone test, this session).

### THE FIX = unify compiler I/O on the foreword tier0/1/2 model
Atomic refactor (no green intermediate state -- input alone breaks
self-compile). Touches ~6 bare-metal helpers + rodata + a TWO-PASS seed
rebuild:
1. INPUT `__bare_metal_read_serial` (X86_64Helpers ~874; read-file-uni,
   `opening.codex:1120`): currently <128->table store, >=128 DROP. ->
   read verbatim then decode UTF-8 -> unicode cp -> from-unicode (tier0
   table O(1); tier1/tier2 search) -> cce-encode -> store. CCE len <=
   UTF-8 len so in-place compaction is safe. (Or high-level: utf8 ->
   List Integer codepoints -> `unicode-bytes-to-text` extended to tier2.)
2. OUTPUT `__cce_print_multi` / `__cce_decode_unicode` (X86_64Helpers
   ~284/257): handle only tier1 2-byte -> <=2-byte UTF-8. Extend to
   3-byte CCE -> to-unicode(tier2) -> 3-byte UTF-8.
3. Align tables: reduce State `tier1-slice-bases` to foreword tier1 (drop
   CJK), add tier2 rodata (cce-tier2-block-table data + computed
   cce-bases). Update `__unicode_to_cce_tier1`, `__unicode_bytes_to_text`,
   `__text_to_unicode_bytes` to the unified model.
   Citing Foreword CCE wholesale collides (is-letter/is-digit dup-def);
   use a SELECTIVE cite of just the conversion fns, or inline them.
4. PROVE: tiny program with a CJK + Arabic string literal compiles
   (input converts) and runs with output bytes == source UTF-8 bytes;
   historian-test + db-full-test compile; build.ps1 one-pass green; then
   seed rebuild + copy-up.

Full detail in memory [[io-boundary-unicode-stripper]] (updated this
session with the corrected model + proof).

### ORIGINAL (now-superseded) directive note

### Proven root cause (do NOT re-investigate -- proof in memory [[io-boundary-unicode-stripper]])

INPUT boundary: `__bare_metal_read_serial`
(`codex/compiler/Emit/X86_64Helpers.codex`, `emit-bare-metal-read-serial`,
~line 874), reached via the `read-file-uni` builtin called by `opening`
(`codex/compiler/opening.codex:1120`, `source <- read-file-uni mode`).
The read loop has, per byte:
```
cmp rax, 128
jcc >= skip-high   ; if byte >= 128, DROP it (jumps past the store)
```
So every non-ASCII byte is deleted from source on input. PROOF: em-dash
(UTF-8 E2 80 94) at the start of a 1-space-indent prose line -> CDX1000;
same with 2 leading spaces -> no error (stripped char leaves col-2,
prose-skips); ASCII prose always fine. `char-code-at` uses movzx
(unsigned) so it is NOT a signedness issue. This breaks the skipped
tests **historian-test** (em-dash in prose) and almost certainly
**db-full-test** (multibyte string literal in Server.codex).

OUTPUT boundary already has Tier-1 machinery (`__cce_print_multi`,
`__cce_decode_unicode`, `__unicode_to_cce_tier1` in X86_64Helpers).
Asymmetry: output converts Tier-1, INPUT still strips. Output may still
need work for Tier-2/3 (3-byte CJK/emoji) -- verify with a round-trip.

`read-file-raw` / `__bare_metal_read_serial_raw` is BIT-ROTTED -- a
verbatim reader exists but pointing `opening` at it yields a SUT that
can't compile anything. Do not use it.

### Failed first attempt (reverted -- learn from it)

Edited `emit-bare-metal-read-serial` so bytes >=128 store verbatim
(jump the skip-table jcc to a captured `store-pos` = the store sequence,
skipping the unicode-to-cce table; <128 path kept byte-identical).
cdx-build SUCCEEDED, but `build/build.ps1` then FAILED at **text-stage1
("TEXT build produced no output")** -- the freshly-built SUT could not
emit TEXT. UNEXPLAINED: the change only alters emitted-CDX helpers, not
the SUT's own TEXT-mode execution. This is the crux to crack.

### Next steps (proof-driven -- use the debugger, do not guess)

1. Re-apply the verbatim-store edit (or a cleaner variant). It is a
   TWO-PASS codegen change: `build/build.ps1` once -> if it converged it
   produced `build/output/NewSeed.cdx` (stage1); install that as
   `build-output/bare-metal/Codex.cdx` and rebuild to a one-pass signed
   `Sut.cdx`.
2. To diagnose the text-stage1 crash: build the SUT, then run it in TEXT
   mode under `tools/codex-vm.exe -debug` (see OperatorsManual "Native
   Debugging Toolkit") feeding the concatenated compiler source with a
   `TEXT` mode line; capture the crash RIP and resolve via the embedded
   MAP1 / `build/resolve-rip.ps1`. Find WHY TEXT emit dies. The earlier
   `-Break` via compile.ps1 only reports the first hit and threw an array
   error on a no-cites file -- prefer interactive `-debug` or a small
   cites-bearing repro.
3. Reproduce historian quickly without a full build: `build/compile.ps1
   -Src codex/test/apps/historian-test.codex -Out <tmp> -Log <tmp>`
   (kernel is `build-output/bare-metal/Codex.cdx`). With the stripper
   present it gives CDX1000 at concat line ~4179 ("got 'is'"). After the
   fix it should compile.
4. Decide the encoding model and keep BOTH boundaries consistent:
   (A) input does FULL UTF-8->CCE for all tiers (matches existing CCE
   output machinery), or (B) both boundaries verbatim and the CCE
   conversion is excised (Less-Is-More; Damian leans this way). If
   verbatim, string-literal round-trip must be proven (compile a program
   with a CJK/Arabic string literal, run it, diff output bytes == input).
5. PROVE before submit: historian-test + db-full-test compile (un-skip
   them, update/remove their `.skip`), a multibyte round-trip test
   passes, `build/build.ps1` one-pass green. Then seed rebuild +
   copy-up per `docs/Agents/PerforceProcess.md`.

### Hypotheses for the text-stage1 failure (to test, not facts)

- The compiler's OWN source may contain non-ASCII the old stripper
  silently removed (e.g. unicode operator aliases the lexer accepts).
  If so, removing the strip changes what the SUT reads of its own source
  -> could shift parsing. Check: grep the concat for bytes >=128.
- Or the edit's emitted helper is malformed in a way canary (hello,
  tiny) doesn't exercise but the 1.6 MB TEXT job does. Disassemble the
  emitted `__bare_metal_read_serial` in the new SUT and verify the jcc
  offset / store path by hand (`bench/disasm-cdx.ps1`).

## SECONDARY: the other 4 skipped-test stragglers

From memory [[design-doc-grooming-and-open-bugs]]. Each is a real
triaged-but-unfixed bug behind a `.skip`:
- **historian-test-full** (`codex/test/apps/`): CDX2001 --
  `KeyManager.export-to-path` returns `None` (Maybe value) where
  `Nothing` (the type) is meant. Small library fix in
  `apps/works/KeyManager.codex`. Good cheap win, independent of the
  stripper.
- **db-full-test**: CDX1000 parse on a string literal in Server.codex
  -- LIKELY the same stripper root cause; recheck after the I/O fix.
- **foreword-all-compile**: CDX3001 duplicate type `Event` (UI quire vs
  engine quire collision). Naming fix.
- **db-test**: runtime heap blow-up (~2 GB for 3 multi-column rows).
  Memory/algorithm work in the db app.

## Working rules reminders

- PowerShell only, no Bash tool. ASCII-only in p4 submit descriptions.
- Codegen/helper changes are two-pass; converge to one-pass before
  installing the seed. Never copy up with a stale/non-one-pass seed.
- Read `docs/Agents/PerforceProcess.md` before any p4 op beyond edit/submit.
