# GitHub Update 46

**Scope: main CLs 16149 onward, opened 2026-08-16.** Update 45 covered 15687
to 16148. Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

**Release head 16558, seed `12B07296419847B2`** (fester, main 16558: the
Release 46 battery found `fat16-read-bytes` was the one FAT16 entry with no
`fat16-vol-is-usable` check, so a read with no disk attached divided by zero;
the guard is foreword and reachable, so it moved the seed from `D354208C`,
COMPILER-14 at 16394, which the three compiler-tree CLs before it had each
measured `Sut == seed`: 16447, 16492, 16524). The release's own map, img,
README and report CLs follow 16558 and ship in the same commit.

## Open from Update 45

- **The ARM64 site serves once per boot.** `GET /` answers 200 and the next
  request in the same boot does not complete. Measured at the release: the
  guest serves TWO loops for ONE host request with identical path, `recv` and
  `resp` (a re-read), then takes a `Synchronous Exception at 0x401016E4`, and
  QEMU reports `virtio-net receive queue contains no in buffers` and `Guest
  moved used index from 10043 to 33`. So the host-visible timeout is the
  client's view of a dead guest. One candidate is eliminated (threading
  `r.ws-state` forward and dropping the `__heap-restore` does not move the
  arm) and one is recorded UNMEASURED with the probe that settles it:
  `transport-new` advances the heap by a full 32 MB against a 32 MB stub
  grant, so the frontier may reach the DMA constants at `#44000000`.
  `docs/Designs/Active/OS/OracleCloudArm64.md`.

  **CLOSED this cycle, two defects, neither the candidate above.** reek
  (main 16236) took the item back and read the defect at
  `ws-serve-loop:139`, the loop recursing on the original `st`, which
  explains both the avail-idx revert to 33 and loop 1 re-reading loop 0; main
  16253 threads `r.ws-state` and takes no heap mark, defect 1 CLOSED
  (counters advance, crash gone, QEMU stderr empty, one loop line per
  request), and defect 2 was isolated to `Tcp.codex`, where `EvListen` was a
  no-op in every state but CLOSED while `EvClose` from ESTABLISHED lands in
  `FinWait1`. blu (main 16291) closed defect 2: a listener is CREATED, not
  transitioned. `net-listen` builds a fresh listening socket from the binding
  and retires the abandoned FIN's rexmit queue instead of stepping a
  connection that close left in `FIN_WAIT_1`, where RFC 793 has no arc to
  LISTEN; `EvListen` removed with its nine no-op arms;
  `tcp-listen-reclaim` now closes from ESTABLISHED, the state a server
  actually closes from and the one the old arm missed. Not seed-affecting,
  Sut byte-identical to the depot seed. blu 16293 measured the second request
  answering on the guest, **3 of 3 HTTP 200 in one boot, counters monotonic,
  QEMU stderr empty**. The parked 32 MB DMA-collision candidate is DEAD BY
  MEASUREMENT: `build-arm64-img` now asserts the DMA floor and printed
  `stack-top 0x42123000, floor 0x44000000, 32,362,496 bytes clear, 8192 heap
  pages`. blu 16339: the 404 on `/api/health` does not reproduce either
  (measured, `eq=MATCH` on both requests), so its three unmeasured candidates
  are moot and the design records no remaining local gap. The harness that
  drove it is `docs/Probes/arm64-two-requests.ps1` (red 16287, rescued from
  reek's scratchpad); `build/boot-arm64.ps1` gained `-TimeoutSec` and a
  per-workspace derived hostfwd so it can be an arm (reek 16304), and
  `OperatorsManual.md` records that a timeout there exits 0 by design and the
  printed line is the discriminator (reek 16308).

- **Neither virtio driver derives its DMA regions from the stub's
  allocation.** Both carry hardcoded constants and a prose warning, and the
  regions have already been inside the guest's own memory once. A derivation
  would be better than the warning.

  **STILL OPEN as a derivation; the guard rail landed instead.** reek 16221
  did the arithmetic (stack-top is `a64pe-kernel-base + text-pages*4096` in
  `Arm64PeWriter`, measured `#42123000` with 32,362,496 bytes of headroom;
  raising the 8192 heap-grant literal to 16384 overruns both drivers' regions
  by 1.14 MB) and reek 16234 made `build-arm64-img` assert the stub allocation
  fits below the VirtIO DMA regions, derived from `Arm64PeWriter` plus the two
  drivers plus the PE being packaged; four arms proven, including heap grant
  8192 to 16384 refusing with the three numbers and writing no image.
  `OracleCloudArm64.md` records why a guest-side derivation is not available
  (the guest cannot compute `text-pages`; SP has descended by the time a
  driver initialises) and leaves the choice, stub publishes `stack-top` or a
  build-time check, as red's or Damian's. The build-time check is what
  landed.

- **The csharp plug has no `peek-16`/`poke-16` row** in either builtin table,
  and nothing standing compares builtin emission tables across plugs.
  `check-plug-types.ps1` compares the IR TYPE wire forms and not these, so the
  gap bites the first time a compiler or foreword chapter calls a 16-bit
  accessor. The DDC is what would surface it. Harmless today because the
  compiler reaches neither.

  **CLOSED, both halves (blu).** Main 16310: csharp gains the 16-bit accessor
  pair, `_Buf.peek_16`/`poke_16` in the runtime prelude plus rows in BOTH
  tables, semantics taken from x86-64's opcodes (`0F B7 07` movzx
  zero-extends the little-endian load, `66 89 17` stores the low two bytes and
  answers 0); `codex/test/poke16-width` through `emit-app` answers all four
  lines identically to x86-64 including the odd offset, Roslyn 0 errors. Main
  16335: `build/check-plug-builtins.ps1`, a plug must have an arm for every
  builtin that reaches its wire (`plugs-backlog` 1.32). The subject is the
  WIRE, not the source: `-Passes text-plug` IR for the oracle subject, `(name
  X)` intersected with the declared builtin list, against each wired plug's
  table. Both source-based designs were measured dead first (16321: mention
  answers 109 of 161; call-site with prose and strings stripped reaches 21,
  all benign). Three guards, each demanded by a measurement: `text-plug`
  required, TWO registration shapes since modelling one makes javascript and
  wasm extract zero, and a floor of 20 because pascal and fortran use a third
  shape and extract 6 and 4. Both arms proven: green 7 on the wire 0 gaps, red
  on a removed csharp row naming `csharp list-snoc` at exit 1. Generated, 0
  drift, UNWIRED from `build.ps1` by red's ruling. What 1.32 still carries
  (16337): pascal and fortran are UNCHECKED rather than clean.

- **CLOSED. No gate carried `codex/test/errors/`, and it was not an `-Internal`
  omission**: `build/build.ps1` had no errors phase at any setting, so the FULL
  gate never ran the set either and `-Internal` skipped nothing, there being
  nothing to skip. The directory is 176 tests; `bvt.ps1` names 13. The rest were
  reachable only from `build/test.ps1`, which is the battery and refuses to run
  without Damian, so a refusal that stopped happening was observed by nothing an
  agent may run. That is how `bounded-exceeded` reached main green in Update 45
  with its premise dead. `build/check-errors.ps1` (with its generator) derives
  the set from the directory, adjudicates with `test.ps1`'s own regex, and is an
  always-run core phase: 176/176 refuse as declared, 29 s inside the gate at
  `-Jobs 8`. (reek, main 16165.)

- **CLOSED. `bvt.ps1` adjudicated a `.failing` two ways wrong, and five of its
  own thirteen error tripwires could not fail.** Line 196 filtered the declared
  codes to `^\d+$` and then searched the log for a bare `CDX<n>`. Both halves
  were wrong against `test.ps1` 594-602's `error (CDX)?0*<n>\b`: the filter drops
  every `CDX`-prefixed line, 45 of the 176 sidecars, leaving those tests checked
  against ZERO codes so that any compile failure passed them; and the `CDX<n>`
  search cannot match the lexer's unprefixed codes (`error 6:`). The five in the
  blinded set were `normalize-false`, `induction-unsound`,
  `induction-list-unsound`, `reverse-reverse-unsound` and
  `kleene-excluded-middle` -- every one a proof-soundness tripwire whose whole
  assertion is CDX2001, and a tripwire that accepts any refusal is L-FALSIF.

  Fixed through `codex/build/bvtScript.codex` and regenerated, 0 drift. **The
  arm is the two adjudications over one sabotaged tree**: with
  `normalize-false.failing` set to `CDX9999` while the compiler still answers
  CDX2001, the old script reports `PASS normalize-false (expected error)` at
  exit 0, 75 of 75, and the new one reports `FAIL (wrong error codes)` at exit 1.
  On the restored tree the stricter check adds no reds: 135 of 135, exit 0, so
  the five now pass on evidence rather than on a filter. (reek, main 16173.)

- **The battery harness can lose bytes from a batch stream** and file the
  survivors under the wrong names. Carried from Update 44; not seen since.
  **Still open; nothing landed against it this cycle.**

- **The zig plug's remaining defects** and `plugs-backlog` 1.2, 35 transpiler
  plugs leaking the field slot.

  **This line was stale when Update 46 opened.** 1.2 CLOSED at main 15799
  (val, previous cycle): the field-slot sweep finished at 0 of 38, baseline
  empty; the register no longer carries it. The four zig defects (1.13) closed
  at 15687, also last cycle. What landed on zig THIS cycle: `text-replace`
  and the `cx_text_split` empty-separator answer (reek 16366, executed 21 of
  21 against codex-vm under zig 0.16), and `cx_text_to_integer` SKIPPING
  non-digits where Codex stops (reek 16409, the row the 1.36 probe grew from
  21 to 29 rows to catch). **Ruling recorded 2026-08-17 (red): `codex/plugs/zig`
  IS ours to gate**, superseding the 08-16 reading that it was Steve Howell's
  alone; PR 66 is credited in the CL wherever a change touches a row that came
  from it. Named in the register and still open on zig: 1.7's
  `cx_ll_push` appends unconditionally.

- **Nothing exercises the guard page under a genuine allocation walk.**
  **Still open**; `CurrentPlan.md` carries it, nothing landed.

- **`build/plug-run.ps1` reports `OK` on a dead guest**, because it greps the
  VM's stderr for `TRUNCATED sent=` and the guest console is not on stderr.
  Found in passing during PR 66 and not fixed.

  **CLOSED, and it opened something bigger (val, `plugs-backlog` 1.30).** Main
  16184: the harness now boots with `-output` into a second temp file and greps
  that; before it the check could not match at all, an arm that could not
  fail on the harness 38 plugs share. Main 16212: the 16184 grep was a race
  against codex-vm's ring dump, both harnesses now wait for VM exit, and
  `recheck` gains the check. Census (16218): of 55 `run.ps1`, 38 share the
  harness and 17 have a private listener, and **16 of the 17 carry no
  truncation check of any kind**; two incompatible guest report shapes exist
  (`TRUNCATED sent=` against `OK ... sent=N TRUNCATED` in `pe`/`elf`/`img`),
  normalised to one shape at 16379. Then the receive loop itself: it sat in a
  `try` whose catch treated a timeout or a reset as a normal end, so the only
  failure it could report was zero bytes. 16454 records which path ended the
  loop and warns on the exception (proved to distinguish 64 clean bytes from
  64 bytes then a stall, identical counts, opposite verdicts); 40 of 40 built
  plugs measured ending by clean EOF; 16489 turns it into a failure, exit 8, in
  `plug-run.ps1` and the three private listeners with a bare catch, plus img.
  **The img truncation is NOT host-side: it reproduces with a clean EOF.**
  16515: img asserts guest-built == guest-sent == host-received and exits 9 on
  disagreement, and it fired: `guest built 16777216, guest sent 16777216, host
  received 16629200` with a clean close at both ends. So the loss is in
  transit and belongs to `codex/os/net`, not to plugs. 16517 files it in
  `CurrentPlan.md` as unowned with the reproducer landed: two failures in six
  runs, shortfalls across the day from 16,416 bytes to 4.9 MB, host contention
  only partly ruled out.

## Open at the release head

What the registers carry at 16524 that this report should hand forward.

- **A 16 MB guest TCP send intermittently arrives short with both ends
  reporting success** (val, unowned, `CurrentPlan.md`; found by plugs 1.30,
  belongs to `codex/os/net` or the NE2K path). Instrument permanent: any img
  run that loses bytes exits 9 and names all three counts.
- **`plugs-backlog` 1.38** (fester, main 16521): RISC-V `from-unicode` inside
  a SPAWNED child hangs under QEMU and is fine under Renode, reduced to `7 +
  from-unicode 69` in a child; presents as a hang because
  `rv-rt-trap-handler` `mret`s without advancing `mepc`. Six hypotheses
  excluded by measurement; the cross battery cannot see it because
  `test-cross.ps1` runs Renode. Runner trap recorded: both cross harnesses
  truncate actual output to the expected line count, so measure with a
  `.expected` longer than the output.
- **The RISC-V FS twin is NOT complete** (fester 16332): `block-read-sector`
  still fills the buffer with zeros; discovery and capacity work, the read
  path does not. The capability refusal arms of both servicer stubs are
  inspected, not measured; step 0's soft `[WARN]` is still live
  (`CrossLaneFilesystem.md`).
- **`plugs-backlog` 1.34, a decision routed to red** (root 16301, re-measured
  16469): on ARM64 the boundary between a program and the block device is the
  effect system, and the hole is `peek-32`/`poke-32` carrying an EMPTY effect
  row; x86-64 differs in kind through `Device.Port`. Until chosen, the ARM64
  capability gate should be read as gating the `block-*` builtins only.
- **`plugs-backlog` 1.36 remainder**: `cobol` (a text representation, not four
  arms: every text variable is `PIC X(256)` and `FUNCTION LENGTH` answers 256
  whatever it holds) and ada's `text-split` alone (a stub, and ada has no list
  TYPE). Neither has a toolchain here. Plus 1.37, babbage.
- **`plugs-backlog` 1.7, fortran stages 7 to 10**: the lambda hole (not
  started, and bigger than fortran: `IrLambda` reaches every source plug,
  val 16430), builtins that are statements in expression position, record
  fields hardcoded `integer(8)`, and the oracle ruling (val 16439): the
  standard is emitted Fortran that reads as ready to test on a real compiler;
  running it through one is the recorded next step. **Nothing in the campaign
  has been compiled.**
- **`plugs-backlog` 1.32**: `check-plug-builtins` covers the WIRED plugs only;
  pascal and fortran use a third registration shape it does not model.
- **`plugs-backlog` 1.35 residue** (root): a callee whose arity the map does
  not know applied to several args stays one flat call in the six TS/JS
  plugs. **1.8 residue** (root 16495 narrowed): a `__seq`-bound field store
  in act-statement position or on a non-name target through haskell, elixir,
  clojure.
- **`plugs-backlog` 1.20**: hoisting closed; four other pascal gaps beside it
  (`gauge:g`, `list_push`/`list_snoc` undefined, `IrFieldStore` emitting the
  literal `"0"`). No Free Pascal here.
- **`plugs-backlog` 1.29 remainder** (blu): the slot cap is closed; three
  stale ARM64 load-address constants remain, and `a64-disasm-base-addr` HAS
  a reader, so every disassembly listing prints addresses about 1 MB low.
  **1.33** (deprioritised, red): no DECK on arm64 or riscv, all five
  `__deck-*` builtins literal stubs on both lanes; the prerequisite is an
  allocator.
- **`compiler-backlog` COMPILER-13**: the defect is closed; no arm guards it,
  and the arm that would (plug-level, `test-input/lambda.codex`) is run by
  nothing automated. **COMPILER-15** (blu, 16524): an empty list literal in a
  `when` wildcard arm reaches the plug wire element-typed `error`, exactly 2
  slots in the compiler's own 16.0 MB IR log, latent. **COMPILER-7**: the
  `bounded` over-refusal, 10 of 11 on the corpus (blu 16250 corrected the
  stale 9 of 10), revisit after a cycle. **COMPILER-1**'s ARM64 remnant.
- **COMPILER-14's reach beyond the gate is unmeasured** (root 16394): the
  refusal at `emit-record` was measured only under the gate; a red in the
  battery or the apps with that message means the by-list branch IS
  reachable, and the close becomes a layout by `cce-byte-offset-and-type`.
- **B4, serve the repository protocol in the bed** (root 16512, steps 2-6
  staged, none seed-affecting).
- **The rulings queue** in `CurrentPlan.md`, nine items, plus 1.34 above and
  the OCI account for Phase 5b-5d.

## Landed this cycle

- **An undefined TYPE name in an annotation is refused now, CDX3008** (blu,
  main 16241, seed `79E7A7E8`). `CDX3002` covers a misspelled value name and
  is raised walking EXPRESSIONS, which a type annotation never enters, so
  `lookup-type-def` answered an unknown name by fabricating an opaque
  `ConstructedTy` and the unit compiled clean, ran, and answered correctly
  around the phantom. The check rides the walk `check-arity-in-expr` already
  makes over every annotation against the COMPLETE type-def map, because a
  refusal inside resolution would reject legal forward references: the map is
  built incrementally and a record's fields resolve against the partial one.
  Only an uppercase name absent from both the map and the builtin set is
  refused, which puts type variables, generic parameters and `Vector`'s
  numeric argument out of scope by construction rather than by luck; three of
  the six arms are those controls. **Found by Steve Howell as finding 9 of
  GitHub PR 66**, one of five findings of his about our compiler that no
  register carried until `plugs-backlog` 1.27.

  **Its first sweep found three real defects in shipped apps** (main 16188),
  each a different shape: a wrong record name in `collab`, a missing cite in
  `fontai`, and in `diagram` a CHAPTER name used as a type. **That last one
  had a second defect hiding under the first** -- the body read `dm-nodes`
  where the record has `dg-nodes`, and field access on the opaque fabricated
  type checks against nothing, so the wrong field prefix was invisible for
  exactly as long as the type name was wrong. That is the argument for the
  code being an error rather than a warning.

  **A wrong claim on the way, worth keeping for its shape.** These three were
  first reported as type names with zero definition sites anywhere, and five
  unreachable definitions were nearly deleted on that basis. The evidence was
  a grep for `^\s*Name\s*=`, which matches one declaration form, cannot see a
  type declared any other way, and cannot tell a chapter name from a type
  name. `TtfContour` and `TtfPoint` are real and widely used. **A search that
  answers "this does not exist" is only as wide as its pattern, and the
  expensive direction of that error is deletion.**

- **PR 66's five open compiler findings are registered and every one is
  answered** (blu 16154 opened `plugs-backlog` 1.27; reek 16195 re-ran
  7/8/10/11 as ours; root routed and closed). Finding 6 became COMPILER-12 and
  is DECIDED (root 16314): the wire does NOT lift; the contract is declared in
  `DevelopersRulebook.md` "What the wire carries". Measured: 52 of 55 plugs
  handle `IrLambda`, six carry their own capture machinery, and lifting on the
  wire would trade a burden six plugs carry for a partial-application burden
  on all 55. Finding 7 CONFIRMED (24 `IRExpr` constructors, no `ir-map-children`;
  51 of 52 plugs name all 24, `t3isa` at 18 refusing explicitly). Finding 8
  REFUTED as stated: `ForAllTy` reaches the default-pipeline wire too, one
  `forall` node under both pipelines; the real difference is which defs
  survive inlining. Finding 9 is CDX3008 above. Finding 10 became COMPILER-11
  and is DECIDED (root 16275): no note at the not-proven arm, because CDX4011
  fires ZERO times compiling the compiler and a negative note would fire at
  401 `__record-set` sites in `codex/compiler` alone. Finding 11 NOT
  REPRODUCED (72 unreachable by construction), but the read found `bag-count`
  returning the total rather than the error count (reek 16202, registered as
  COMPILER-10); it had no callers and root deleted it (16492, seed unchanged
  `D354208C631FDDA7`). A claim of reek's that the t3isa refusal is unobserved
  was WITHDRAWN the same day (16202): `run.ps1` exits 6 and `gate.ps1` throws,
  measured.

- **PR 67, COMPILER-14: `emit-record` had two record layouts and every
  reader used only the first** (root, main 16394, seed `D354208C631FDDA7`;
  Steve Howell, routed by red). `emit-record` laid a literal out by
  `build-cce-byte-offsets` when `resolve-constructed-ty` answered a `RecordTy`
  and otherwise by name-sorted rank times 8, while `emit-field-access` and
  `emit-record-set-builtin` read by the first rule unconditionally, so a
  record built through the fallback with any field narrower than eight bytes
  was read at the wrong offsets (his probe: `Box = record { flag : Boolean,
  items : List Integer }`, `b.items` reading the boolean). He could reach the
  fallback only by skipping RESOLVE. The fallback is now the refusal its
  neighbour already makes (`cdx-ir-error "emit-record: unresolved type for
  record literal"` and `ud2`); `emit-store-record-fields-by-list`,
  `field-local-names` and `RecordLayoutPair` are deleted. Reachability was
  measured by that refusal under the gate: the self-compile and the BVT never
  take it. **This is the release seed.**

- **COMPILER-13, an inline lambda's parameters were typed `error` on the
  wire** (root 16326, seed `1012A99664DD233C`, single-argument case:
  `lower-apply` routes a lambda head through `lower-apply-lambda-head` so a
  directly-applied inline lambda's parameter is typed from its argument, wire
  measured `param x int-default`, BVT 308/0; blu 16374, seed `1E6BED13`,
  multi-parameter case: collect the apply spine, build the curried `FunTy` in
  one go, lower the lambda against that, fold the applies back; `y`, `b`, `c`
  went `error` to `int-default` on the wire, single-param unchanged). **The
  old compiler ran all three shapes correctly, 105, 3, 6**, so this was never
  visible in x86-64 behaviour and no x86 test can pin it; blu 16383 records
  that NO ARM would catch it coming back. blu measured the wire CLEAN for all
  three shapes on seed `1E6BED13` and found COMPILER-15 on the way.

- **COMPILER-9 stages 2 and 3, the native text helpers agree with the
  foreword defs on all three lanes** (root, main 16265, seed
  `966DBFADBFCCBB7E`; `docs/Designs/Done/Compiler/ForewordShadows.md`). ARM64
  and RISC-V native `text-split` (empty separator, overhang, capacity) and
  `text-to-integer` (non-digit) fixed to the defs; the RISC-V `text-to-integer`
  subtracted ASCII 48 where CCE `'0'` is 3; `text-replace` on an empty needle
  closed on all four sides. `text-helper-native`'s `.no-cross` lifted, native
  arm byte-identical to `text-helper-spec` on x86-64, arm64 and riscv64. Red's
  ruling recorded: the five foreword defs are KEPT as the text-plug fallback,
  which is what opened `plugs-backlog` 1.31 below. Item 3 (the dead ARM64
  `list-tail` helper) closed at 16476, `list-tail-empty` on three lanes, and
  the design moved to Done. Beside it: RISC-V `peek-32` zero-extends now,
  `rv-lwu` (root 16348, `plugs-backlog` 1.28 deleted, `peek32-sign` green on
  three lanes), and COMPILER-5 closed (root 16354): the sem-equiv normalizer
  parenthesises a hex literal past i64-max, row deleted, after val 16198 had
  corrected the plan's "in flight" to "open and unstarted".

- **The ARM64 filesystem servicer, CrossLaneFS step 4** (fester, main 16224,
  seed `37334AC542FF17B53FEA3AA700AC1405`, 2,827,591 bytes, self-verifying,
  gate 639 s carrying `check-errors`). `codex/test/fs-servicer` PASSES on
  arm64: a program declaring no handler of its own writes `OK.TXT` and reads
  it back through the default servicer onto real virtio hardware; `fs-layer`
  still passes. `a64-emit-fs-servicers` emits `__fs-read-servicer` and
  `__fs-write-servicer` gated on `cap-filesystem-read`/`-write`; a slot on
  this lane holds the handler's CODE address, not a closure pointer, and the
  first draft that allocated x86-64's 16-byte closure faulted at
  `heap-start+0x10`. **`write-file` was a SILENT STUB on ARM64**, claimed as
  a builtin and answered with a literal 0, so it never reached the handler
  table and reported `write False` with no disk touched; both rows deleted,
  a behaviour change for every arm64 program calling `write-file`.
  `ir-emit-roots` gains the two servicers, measured: with the roots entry out
  both calls are unresolved and the guest faults. Found and recorded, not
  chased: `plugs-backlog` 1.29, ARM64 effect-op slots silently capped at 16.
  Then the SVC servicer path, `plugs-backlog` 1.17 CLOSED (root 16301):
  `svc #10/11/12` stubs, sync-slot handler with the block/elevated gate,
  `fs-elevated` cell `#40008D88`, `block-gate-restrict` arm; not
  seed-affecting. val 16230 recorded 1.17 unblocked by step 4.

- **`VirtioBlk`'s `poke-16` workaround and its false prose are gone, and it
  needed NO SEED** (fester, main 16283, 16316). `vb-put-avail-entry` writes
  the 16-bit available-ring entry with one `poke-16`; the alignment constraint
  is kept and restated. PROVEN REACHED: sabotaging the write to `desc + 1`
  makes `fs-layer` FAIL, restoring it passes; `fs-layer` and `fs-servicer`
  both pass on arm64. The `CurrentPlan` row predicting a seed was WRONG,
  measured twice on two source bases: Sut byte-identical to the depot seed
  (`966DBFAD` on that base), so the compiler does not reach
  `vb-put-avail-entry`. Same surprise `DevelopersGuide` records for CL 9432.

- **The RISC-V twin, four CLs, and it is not complete** (fester). Main 16332:
  the address-remap window narrows to 16 MB, matching ARM64.
  `rv-remap-addr-insns` added `0x80000000` to every address with bit 31
  clear, so the machine's own virtio-mmio at `0x10001000` was relocated into
  RAM and the block device could not be reached; measured, one line changed:
  `srli 31` gives `block-sector-count -1`, no device, `srli 24` gives 32768,
  the 16 MB fixture. `compile-riscv` now asserts the image fits (87,344 bytes
  against a 16,777,216-byte window), reading the shift out of the emitter,
  and the first attempt that edited the generated script alone was caught by
  `build.ps1` as a newly drifted generator. Also in it, the handler-install
  miscompile: `rv-li-64` takes a scratch register, so loading the stub into
  `t0` then materialising the slot address into `t1` destroyed the stub; the
  slot held half an address and the jump landed nowhere with no diagnostic.
  `codex/test/fs-handler-install` is the arm, ablated (old ordering prints
  `before` and dies), passes on riscv and arm64. Gate green 542 s. Main 16432:
  a global constant in argument position clobbered staged args, and `call()
  == K` compared `a0` with itself; FAT16 comes up on riscv,
  `codex/test/call-clobber` is the ablated arm. Main 16474: riscv block
  builtins are capability-gated with the FileSystem escape arm64 has,
  `block-gate-restrict` passes and the twelve fat16/fs tests still do. Main
  16505: riscv effect-handler clauses strip the continuation, so `resume` no
  longer emits a call to nothing; `fs-layer` passes. **`block-read-sector`
  still fills the buffer with zeros**, its own item.

- **The gate carries `codex/test/errors` and `bvt.ps1` adjudicates a
  `.failing` honestly** (reek, main 16165 and 16173; the two CLOSED bullets
  above are the account).

- **The five text builtins have arms in 24 more plugs, and then turned out
  to be inlined and wrong in most of the rest** (reek; `plugs-backlog` 1.31
  CLOSED at 16385, 1.36 opened and worked down to two). 1.31, main 16351,
  16357, 16361, 16363, 16366, 16368, 16370: `text-starts-with`,
  `text-contains`, `text-replace`, `text-split`, `text-to-integer` given arms
  in typescript angular react vue svelte electron html qt winforms wpf maui
  java gtk compose flutter swiftui zig d pascal scheme clojure groovy perl
  julia. Seven plugs EXECUTED against codex-vm on a 21-row probe and agreed
  21 of 21: typescript and html under node 24, gtk under python 3.11, zig
  under zig 0.16, winforms/wpf/maui as extracted C# under .NET 9; the rest
  census plus plug-smoke, said plainly in each CL. Fifteen arms were present
  and WRONG rather than missing, every one `text-replace` against an empty
  needle. **1.31's own census is recorded as unreliable in BOTH directions**:
  it keys on the quoted Codex name, so winforms read as zero-armed while
  armed and zig read as armed on a `@compileError` refusal. 1.36 (reek 16385
  onward): a plug that INLINES a builtin never spells its name, so every
  census reads it as covered. The instrument is a 29-row probe, up from 21
  because the 21-row version passed zig, whose `cx_text_to_integer` SKIPPED
  non-digits and answered 12 for `1a2`. Six failure classes; `text-to-integer`
  the one to expect everywhere, because `parse-decimal` answers a prefix or 0
  where library parses throw. Landed by family: 16409 (python, javascript,
  csharp, zig, plus the perl name bug: the perl plug emitted `\&name`, a CODE
  REFERENCE, for every variable it bound, because `lookup-arity` answers -1
  for a non-definition and `emit-pl-name` tested only `ar == 0`), 16416 (JVM:
  kotlin, scala, clojure, groovy; scala's `String.split` takes a REGEX so `.`
  matched every character), 16426 (go, rust, nim, d), 16436 (lua, ruby, php,
  elixir), 16443 (haskell, ocaml, julia), 16450 (objc, swift, pascal,
  scheme), 16466 (ada: four of five, and a CORRECTION to the campaign's own
  published claim that `text-starts-with` was at parity everywhere; ada's
  `Index` answers 0 for an EMPTY pattern), 16482 (wasm, EXECUTED under
  `wat2wasm` plus `wasmtime`, 29 of 29 against codex-vm, the eighth
  executable plug and one the campaign nearly wrote off as unrunnable without
  checking for a toolchain), 16500 (cobol and ada attempted rather than
  diagnosed, reek stream 16497 and 16498). A
  negative result recorded so nobody repeats it (16502): the perl
  code-reference bug is ONE instance, not a family; the -1 versus 0 answer for
  an unknown name differs plug to plug but no other plug turns it into a
  different KIND of emission. Every fix is a named prelude helper, never an
  inline library call. Remaining: cobol and ada's `text-split`, above.

- **The pascal plug hoists, and `IfThen` was making every recursive
  definition non-terminating** (reek, main 16460, `plugs-backlog` 1.20).
  `emit-pas-expr-at` lowered every `IrIf` to `IfThen(c, t, e)`, and `IfThen`
  is a FUNCTION, so both arms were evaluated and every recursive Codex
  definition through this plug did not terminate; `emit-pas-match` had the
  same shape. Replaced by `pas-h`, which answers a triple (statements,
  expression, next temporary), so a binding, conditional, match or act can
  appear anywhere an expression can. Four shapes closed, not the two the row
  named; five-shape probe hand-traces to the x86-64 answers; `IfThen(`
  appears zero times in every subject tried; no new undeclared read. NOT
  executed, no Free Pascal here.

- **The fortran campaign, `plugs-backlog` 1.7, stages 1 to 6 and a ruling**
  (val, ruled in by Damian via red). Measured first (16168): fifteen
  `fort_*` helpers emitted and zero defined, no prelude, no list
  representation, a campaign not a patch. Stage 1 (16388): the
  `codex_fort_prelude` module, all fifteen helpers, verified over five inputs
  with a control arm that fails (strip the prelude: 7 undefined on
  `builtin-reach`). Stage 2 (16390): the function result type as a
  declaration instead of the prefix shape, on the plain and TCO paths, three
  arms including a sabotage arm naming `br_abs`. Stage 3 (16397): `fort-type`
  answers the element type for a list and rank moves to the declaration site
  (`dimension(:)` dummy, `allocatable` result), so the prelude's generic
  interfaces now resolve; list-shaped declarations 0 to 9 across the five
  inputs. Stage 4 (16403): the TCO jump evaluates each argument into a
  temporary before copying back; `countdown 10 0` went from 45 to 55. Stage
  5 (16412): effectful defs with parameters emit as subroutines and are
  `call`ed; the effect test reads the `FunTy` effect row, not the result type,
  after a first attempt on the result type changed nothing. Stage 6 (16421):
  `list-at`, `char-at`, `char-code-at` and `substring` call prelude helpers
  instead of subscripting an expression, control rebuilt from the depot,
  direct subscripts of an expression 3 to 0. Stage 7 as numbered in the CL
  (16423): array constructors gain an F2003 type specification, closing an
  untyped empty `(/ /)` and a character-length mismatch that stage 3 itself
  introduced, found by auditing the emitted output. **The oracle ruling
  (16439): the standard is emitted Fortran that reads as ready to test on a
  real compiler; running it through one is the recorded next step. Nothing
  has been compiled; there is no `gfortran` or `flang` here.** Also closed
  under 1.7 (16176): the eight no-`list-snoc` plugs, whose third failure mode
  was pass-through to an undefined prelude name, 16 of 16.

- **`plugs-backlog` 1.16 CLOSED** (val, main 16162): csharp, recheck, rust on
  the checked send channel; two dead unchecked send loops deleted; "never
  `heap-restore` around a send" lifted to `DevelopersGuide` Pitfalls.

- **The TS/JS family splits an apply chain at the callee's arity** (root,
  `plugs-backlog` 1.35; 16346 over-application, 16478 under-application of a
  known def wrapped in partial arrows, `test-input/partial.codex` added).
  Measured through all six: `make_adder(10)(32)`, `const g = (_p0_) =>
  add3(1, 2, _p0_)`. Row narrowed to unknown-arity callees. Beside it 1.8
  (root 16495): haskell, elixir and clojure rebind a `__seq`-bound field
  store; row narrowed to act-statement and non-name stores.

- **`plugs-backlog` 1.29, the ARM64 effect-op slot table gets clearance and
  a refusal** (blu, main 16524). `a64-assign-effect-op-addrs` based the table
  at `#40100000` while the image loads at `#40100080`, so the slots sat in
  the 128-byte ELF header hole, which holds exactly 16, and slot 16 was the
  program's first instruction. Now at `#40020000` with `#E0000` of clearance
  and a 1024-slot ceiling that raises `[UNSUPPORTED]` rather than wrap.
  Full arm64 cross battery 418 pass / 31 fail before and after with an
  identical FAIL set; sabotage at ceiling 0 refuses; the boundary at exactly
  4 slots for 4 ops passes. Same CL: COMPILER-15 registered, CostModel 3.3
  status. Sut == depot seed `D354208C`, measured.

- **The bench tables are re-measured on the release seed, with zig and JIT
  columns** (root, main 16447 and 16510; `bench/compare.ps1`, README,
  `ArchitectsSketchbook.md`, the perf page). Codex, C and Zig columns
  measured 2026-08-17 on seed `D354208C631FDDA7`: `cl.exe` at `/Od` and
  `/O2`, zig 0.16.0 at `-O Debug` and `-O ReleaseFast`, hand-ported from
  `bench/c`; the harness passes `-Kernel` the seed and uses CCE names for
  arm64. The JIT columns are .NET 9 RyuJIT (SDK 9.0.313) at FullOpts for all
  nine, replacing 2026-06-12 numbers whose C# and F# sources never landed in
  the depot. The last column is Codex TRANSPILED through the zig plug and
  built by the same zig at ReleaseFast: hand-built quality on seven of nine
  (fib 22 against 23, fact 58/58, ack 25/25, tak 42/42, collatz 17/17,
  regright 21/21, locals 33 against 35), sum two behind (17 against 15) and
  gcd ten behind (28 against 18, `my-gcd` going through the `math-mod` def as
  a call). The Sketchbook explains what a JIT count is: `Bench:Gcd` really is
  nine instructions in sixteen bytes, a frameless leaf, which is why Codex's
  10 sits beside it.

- **CoordinationProtocol: a message budget, how to wait, and three
  corrections from the AgentGrid side** (red 16205, 16215; Damian 16279).
  16279: withdrawing a queued build-request works again (the coordinator
  re-checks the file at the grant since AgentGrid CL 15661; the earlier
  measurement of a request deleted at 02:58 and granted at 03:17 predates
  that build); `outbox/sent` is a delivery receipt now (AgentGrid 16276,
  undeliverable goes to `outbox/failed/` with the reason); `atRest` is the
  field to assign work off, read from the last assistant record's
  `stop_reason`, and it is the observed form of the condition "How to wait"
  describes.

- **Two rulings on what the public mirror publishes** recorded (blu 16152):
  annotations/apps/games/magic accepted as public, `build/boot/kbd-diag-v16.img`
  stays up. Release 45's steps 5 and 6 (blu 16150) opened this file.

- **Small closes, one line each.** reek 16259: dead `vnet-state-addr` deleted
  (`#80100` is flash, not RAM, on QEMU virt; IR byte-identical). reek 16263,
  16272: `plugs-backlog` 1.31 corrected (`deck-record` is a bracket over
  `__deck-enter`/`__deck-exit` and BOTH are literal-0 stubs on arm64 and
  riscv) and RENUMBERED to 1.33 after root filed a different 1.31 the same
  day. blu 16250: COMPILER-7's score corrected to 10 of 11 as of main 16118.
  root 16469: 1.34 re-measured, decision routed to red. root 16512: B4 plan
  claimed, steps 2-6 staged.

## Seeds this cycle, in order

| main CL | agent | seed | what moved it |
|---|---|---|---|
| 16224 | fester | `37334AC542FF17B53FEA3AA700AC1405` | CrossLaneFS step 4, ARM64 servicers, `write-file` stub deleted |
| 16241 | blu | `79E7A7E8` | CDX3008, undefined type name in an annotation |
| 16265 | root | `966DBFADBFCCBB7E` | COMPILER-9 stages 2-3, native text helpers |
| 16326 | root | `1012A99664DD233C` | COMPILER-13, single-argument inline lambda |
| 16374 | blu | `1E6BED13` | COMPILER-13, multi-parameter inline lambda |
| 16394 | root | `D354208C631FDDA7` | COMPILER-14, `emit-record` refuses an unresolved record type |
| 16558 | fester | `12B07296419847B2` | `fat16-read-bytes` guarded by `fat16-vol-is-usable` (release battery finding, `fs-handler-install` on x86-64); **release seed** |

Every one self-verifies per its CL (`Sut == seed`). Non-seed CLs that were
predicted seed-affecting and measured not to be: 16283 (VirtioBlk, twice, on
two source bases), 16291 (tcp re-listen), 16310 (csharp 16-bit pair), 16332
(RISC-V remap).

## The release proof, at head 16558

| proof | result |
|---|---|
| Battery, `-Tier all -Jobs 8` | 1,535 tests, 1,489 pass, 0 fail, 46 skip; oracles scalar 2013/2013, vector 130/130, cce 1485/1516 with 31 documented gaps, 0 unexplained |
| App sweep (the gate's `-Check`) | 270 units, 265 clean, 5 known-dirty, 0 regressions |
| Poison build (0xCD fill), `-Tier all -Jobs 8` | 1,535 tests, 1,489 pass, 0 fail, 46 skip |
| DDC witness (Roslyn arm) | both arms 2,831,202 bytes, 96 differing bytes all inside the signature region 40..135, 0 outside |

Seed `12B07296419847B2`, one-pass hard fixed point (`SUT === stage1`), and
`build/output/Sut.cdx` from the release gate is byte-identical to the depot
seed. **The first battery of this release, at seed `D354208C`, was RED: 3 of
1,535.** `ai-flux-pipeline` and `ai-exp-approximations` refused CDX3008
because `FluxPipeline` never cited `DiffusionPipeline` for `PipelineOutput`
(the standing gate compiles no ai foreword; only the battery reaches them;
main 16550), and `fs-handler-install` faulted on x86-64 with no disk attached
(the seed CL above). The proofs in the table are the re-run at `12B07296`.
The first standing gate of the release also went red once on the
`vm-differential` phase (the qemu arm produced no binary, exit 3) and green
on the immediate re-run; that arm's retry policy is rulings-queue item 6.
