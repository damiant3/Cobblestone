# The `grounds` boundary -- chapter-level hardware-effect exemption

Status: MECHANISM SHIPPED (Damian + val, 2026-07-15). The `grounds`
keyword works end to end (parse -> Document/AChapter `ground-effects` ->
`UnificationState.chapter-grounds` -> `check-def-normal`, scoped, one-pass;
pinned by `grounds-port` and `errors/grounds-scope`). The legacy
`quire-effect-exempt` list is retained as a blanket fallback. What remains
is the migration campaign below -- moving `codex/os/` onto explicit
`grounds` and de-exempting the higher-order OS chapters -- which is what
actually closes the `codex/os/` hole in BACKLOG 1.8.

Implementation note (learned the hard way): the field could NOT be named
`grounds`, because `grounds` is now a keyword and the compiler's own
source uses the field as an identifier -- a keyword/identifier collision
that let the old seed build a new compiler which then could not compile
its own source. The field is `ground-effects`; only the keyword is
`grounds`.

## The problem (BACKLOG 1.8)

`quire-effect-exempt` (`TypeChecker.codex`) turns effect checking OFF for
whole quires -- originally `Kernel`, `Dev`, `Net`, `Sched`, `Trust`,
`Observe` (and `Replay`, `Verify`, both since migrated -- see progress
below), plus the compiler's own quires and the plugs. Inside an exempt
quire a driver touches ports and MMIO while typed pure, and the program
that consumes it never declares the effect and never earns the capability
bit. "Effects are explicit" has a hole the size of `codex/os/`.

(A dead entry lived in this list until the migration: `q == "Os"`, added
to exempt `codex.os.core`, never matched -- that quire's slug is `OS`
(uppercase, the quire-map key) and the comparison is case-sensitive, so
`codex.os.core` was effect-checked all along. Its four chapters are pure,
so nothing ever failed and nobody noticed. The dead entry was removed with
the Verify migration. Lesson: an exempt entry must match the quire's actual
slug casing exactly, and a "de-exemption" of a miscased entry is a no-op --
verify the entry is live before crediting the closure.)

The exemption is wrong in two ways at once:

1. **It is too coarse.** It exempts an ENTIRE quire, including the
   higher-order OS functions that orchestrate drivers and should declare
   their effects so their callers do too. The distinction Damian drew is
   real: kernel internals talking to the metal need no bookkeeping, but a
   higher-order OS function is a library consumed by ordinary code and
   must declare.

2. **It lives in the wrong place.** The exemption is a hardcoded string
   list baked into the compiler (`TypeChecker.codex`). The module does not
   declare its own status; the compiler asserts it from afar. That is the
   same drift 1.12/1.13/2.14 are about -- two lists that must agree by
   hand.

## The decision

A chapter declares itself the SOURCE of specific hardware effects with a
chapter-level `grounds` declaration, a sibling of `cites`/`quotes`/
`trusting`:

```
Chapter: Ne2k
  cites Kernel chapter Pci
  grounds Device.Port, Device.Mmio
```

Read: *this chapter is where these effects actually happen -- its
functions may perform them without declaring, because it is their
ground.* The metaphor is electrical grounding: this is where the
abstraction meets the metal.

### Why chapter-level, not quire-level

- **It dissolves the Kernel/Os split into per-file truth.** No need to
  decide "is the Os quire exempt" or to split quires. A driver chapter
  grounds its effects; a higher-order OS chapter in the same quire does
  not, so it must declare, and its callers inherit. The boundary becomes
  what it actually is: which FILES touch metal.
- **A quire has no honest home for it.** The only quire-level file is
  `codex.project.json` -- config, divorced from the source. A load-bearing
  safety property belongs three lines above the code that touches the
  hardware, in the `.codex` file, where a reader (and a regulator) sees it
  next to the MMIO.
- **It kills the hardcoded list.** The module becomes the single source of
  truth; the compiler reads the declaration instead of asserting from a
  baked-in string list.

### Why it names its effects (scoped, not blanket)

`grounds Device.Port, Device.Mmio` exempts the body ONLY for those
effects. A chapter that grounds `Device.Port` but performs `Device.Mmio`
is rejected (CDX2031) -- a driver cannot launder some OTHER effect through
its exemption. This is strictly stronger than today's blanket
quire-exemption.

### How it composes with the roots (the capability-graph roots)

A def's PUBLISHED type is unchanged by `grounds`: only what a def
explicitly DECLARES in its signature propagates to callers. So within a
grounding chapter:

- A function that DECLARES `[Device.Mmio]` in its signature publishes it
  to out-of-kernel callers (who must then declare it). This is a **root**
  of the capability graph -- the boundary Damian asked to mark.
- A function that PERFORMS `Device.Mmio` without declaring it is an exempt
  internal -- no bookkeeping, no propagation.

The key mechanism finding (val, 2026-07-15): `def-effect-exempt`
(`TypeChecker.codex:707`) only skips the BODY row-subset check; a
function's declared row already propagates to callers regardless. So
"roots publish, internals stay bare" needs no new propagation machinery --
only that a root writes the row it grounds.

## Implementation plan

Frontend only. Effects erase at codegen, so this is NOT a codegen change:
the seed stays byte-identical as long as the compiler's own chapters keep
their current (quire-exempt) behavior during migration. Cheap to gate.

1. **Lexer**: add `grounds` as a reserved keyword token (join
   `cites`/`quotes`/`trusting`).
2. **Parser**: `is-grounds-keyword` + `parse-grounds` reads a
   comma-separated effect list after `grounds`; store the grounded effect
   names on the chapter header record (beside cites/quotes/trust-floor).
3. **Threading**: build a `List (chapter-slug, List Name)` grounds table
   from the parsed document and thread it into the check phase (a field on
   the check-phase state, populated once). A field on `ADef` is rejected:
   ~25 `ADef` constructors across Desugarer/ChapterScoper/LambdaLifting/
   ResolveTypes/Lowering would each need updating.
4. **Type checker** (`check-def-normal`):
   - Look up the def's chapter grounds via `def.chapter-slug`.
   - CDX2031: instead of skip-if-exempt, call `check-effect-row-subset`
     with `declared-row UNION grounded-effects` -- union the grounded
     names into `declared-names` so exactly they are covered and any
     ungrounded effect is still flagged.
   - CDX2033 (effectful-let): keep the `effect-exempt` state flag set when
     the chapter grounds anything; a let performing an ungrounded effect is
     still caught by CDX2031 at the def boundary (the effect flows into the
     body row regardless of the let-position check).
   - Keep `quire-effect-exempt` as a legacy blanket fallback so nothing
     breaks mid-migration.
5. **Tests**: a NEW non-exempt-quire test chapter with `grounds
   Device.Port` compiles (positive); the same chapter grounding the wrong
   effect while performing Device.Port is CDX2031 (negative, proves
   scoping). Add a `grounds`-keyword reserved-word error test.

## Migration campaign (closes the codex/os/ hole)

**Progress (2026-07-15):**
- **Replay -- DONE (CL-1, main 8276).** Pure quire (encode/decode/verify over
  records); de-exemption was a no-op for callers. Also fixed 2 pre-existing
  CDX2051 bounded bugs the full compile surfaced (7 `__narrow`).
- **Verify -- DONE (CL-2).** `ProcessCaps` performs `poke-32`/`peek-32` (both
  EFFECT-FREE builtins -- empty-row in `TypeEnv`, since raw memory is not a
  tracked capability) and declares `[Capability]` on its privileged roots
  (`write-process-scope`, `apply-load-decision`); the other six chapters are
  pure. The quire was already effect-correct and de-exempted with NO chapter
  changes -- enforcement now guards against a future undeclared effect. Verified
  by compiling 11 Verify tests + running process-caps/verifier vs `.expected`.
- **Observe + Trust -- DONE (CL-3).** Both already effect-correct with NO chapter
  changes: `SystemInfo.gather-report` declares `[Identity]` and performs exactly
  that (`identity-whoami`; `process-get-pid` is a plain Integer binding, effect-
  free); Trust threads its transport as pure data (`let sr = transport-send-message
  ...`, no I/O effect). 23 Observe+Trust tests compile clean; a representative set
  runs vs `.expected`.
- **codex.os.core (`OS`) -- was never exempt** (dead `Os` entry, above); already
  enforced, no migration needed.
- **Sched -- DONE (CL-4).** De-exemption clean (no effect errors); fixed **5
  pre-existing non-effect bugs** the full compile surfaced (all fail with the depot
  seed too): SignalBus `count` + InitManifest `step-count`/`order`/`booted` +
  ProcessGroup `member-count` -- CDX2051 bounded fields assigned `list-length`/plain
  Integer (fixed with `__narrow`); and `process-group-test`'s `let exists` -- CDX1060
  (`exists` is a reserved keyword; renamed the test-local to `present`). 10 Sched
  tests compile clean, 9/10 run vs `.expected`. **`task-queue-test` has a SEPARATE
  pre-existing RUNTIME mismatch** (priority order `alert,serve,log` vs expected
  `alert,log,serve`; `pending=1` vs `2`) -- byte-identical on the depot seed, a
  TaskQueue-logic-or-stale-`.expected` bug orthogonal to this migration. Not fixed
  here; needs its own investigation.
- **Dev -- DONE (CL-5). FIRST quire to actually use `grounds`.** 28 chapters, but
  only 2 are drivers: `IoInspector` (`grounds Device.Port` -- port-in/out-byte),
  `AtaDebugger` (`grounds Device.Port, Device.Block` -- port I/O + block-read-sector;
  poke-byte is effect-free). Both have pure signatures whose bodies perform the
  effect in a `let` -- grounding makes them exempt internals (no propagation). The
  other 26 are app-settings modules (pure). De-exemption surfaced **10 pre-existing
  non-effect bugs** in those settings chapters (all fail with the depot seed): 9
  CDX2051 `__narrow` (AudioControl ad/av-volume, BackupRestore bk-progress,
  GamepadManager gp-dead-zone, QmkProtocol qr-h/s/v, PowerManager bat-percent,
  KeyboardRgb 5 qr-* sites) + **1 CDX2070** in `GamepadManager.gamepad-button-label`
  -- its `when` packed multiple `is` arms per line and the parser only took the first
  arm of each continuation line, silently dropping 12 of 17 buttons. Fix: one `is`
  per line (the documented style). Verified: all 28 Dev chapters compile clean;
  vga-terminal-demo (the only Dev test, drives the two drivers) compiles clean.
  **NOTE for later: multi-`is`-per-line `when` misparses** -- it was invisible here
  only because exhaustiveness caught it; a non-exhaustive-checkable match would
  silently mis-dispatch. Worth a parser fix or a lint.
- **Kernel -- DONE (2026-07-15, val).** 34 chapters, 8 drivers grounded:
  `Pci`, `GpuBridge`, `Vga`, `VgaGraphics`, `DiagnosticShell` (`grounds Device.Port` --
  `port-in/out-*`); `DriveManager`, `DiskFacts`, `AppPersist` (`grounds Device.Block` --
  `block-read/write-sector`). `Ne2k` already declared its `[Network.Read]`/`[Network.Write]`
  rows as roots (`net-send-raw`/`net-recv-raw` carry them) -- the only change it needed was
  act-converting `ne2k-recv-frame`'s body, a bare `let len = net-recv-raw` the exemption had
  hidden (CDX2033). No `Device.Mmio` grounding: the virtio chapters reach MMIO through
  `mmio-write-64` (a `poke-32` wrapper) and `mmio-barrier` (`__heap-save`), both effect-free,
  and the USB chapters read status with effect-free `peek-32`. Frontend-only, one-pass seed;
  the grounded-chapter test binaries are byte-identical seed-vs-SUT (effects erase).
  Verified via a test-SUT compiling pci-scan / vga / gpu-bridge / disk-facts-init /
  repo-source-fact / nic-ping / explorer-server-test / diagnostic-boot / codex-boot clean.
  **Pre-existing, left alone (orthogonal to effects, fail x86 name resolution with or without
  the exemption):** the arm64 kernel chapters (`Arm64Boot`/`Arm64Pci`/`Arm64Timer`, `Gic`)
  reference arm system-register builtins (`read-cntfrq-el0`, `write-icc-*`, `poke-qword`)
  that live only in the arm64 plug runtime, not the x86 `TypeEnv`; `DriveManager` references
  `poke-qword`/`uefi-partition-start` undefined in x86. These compile only via their real
  cross/UEFI contexts, never the x86 self-host.
- **Net -- DONE (2026-07-15, val). LAST QUIRE; the codex/os/ hole is closed.** 34 chapters,
  already largely effect-correct: `NetIO` and `HttpFetch` are the two roots and both declare
  their `[Network.Read]`/`[Network.Write]` rows with act-binds (NetIO was migrated 2026-07-13,
  its own prose explains it is "the layer where the frames stop being data and start being
  traffic"); everything below (TcpTransport, MessageFraming, Ethernet, Tcp, Udp, and the
  protocol byte-builders) is pure. Removing `Net` from the list surfaced ZERO effect errors.
  It did surface pre-existing bounded bugs (orthogonal to effects, red under the seed): `Ntp`
  (1 CDX2050 + 4 CDX2051), `Icmp` (4), `Dhcp` (2) -- fixed with `__narrow` plus two wrong
  `NtpResponse` field bounds (`version` 1..4 -> 0..7 and `stratum` 0..15 -> 0..255, both too
  tight for the 3-bit / full-byte wire fields). Also fixed `TcpSegment.flags` in
  network-stack-test and tcp-test (test-file `__narrow`); five previously-red Net tests
  (ntp/icmp/dhcp/network-stack/tcp) now compile and pass. Left for their own fix (pre-existing,
  not effect-related): `ImapClient` `\r` escapes (never compiled, cited by nothing),
  `OAuthClient` calls `uri-encode` where `Encode chapter Uri` exports `url-encode`, and a
  dormant cross-chapter `ActSend` constructor collision (2.18 class).

**The migration is complete.** `quire-effect-exempt` now holds only the compiler's own quires
(`Opening`/`Ast`/`Core`/`Emit`/`IR`/`Semantics`/`Syntax`/`Types`) and the plug backends
(`Riscv`/`Arm64`/`Pe`/`Elf`/`Img`). Every `codex/os/` quire is effect-enforced: drivers ground
the hardware effects they are the source of, higher-order chapters declare their rows as roots,
and "effects are explicit" now holds across the operating system.

After the mechanism ships, move `codex/os/` off the quire exemption
chapter by chapter:

- Add explicit `grounds <effects>` to each driver chapter that touches
  hardware; verify it still compiles.
- Give the driver chapters' boundary functions declared effect rows
  (roots), so consumers declare too.
- De-exempt the higher-order OS chapters (they get NO `grounds`): this is
  an act-conversion campaign of the shape CapabilityProbe.md documents
  (CDX2033 forbids effectful lets outside act-binds), not a signature
  sweep. Fat16 took 6 act-conversions; Fat32 (~74 sites) and Gpt (~52) are
  larger. Plan per-chapter.
- When a quire has no chapter left relying on the blanket exemption,
  remove it from `quire-effect-exempt`.
- The compiler's own quires and the plugs keep the exemption last (or get
  `grounds` on their serial/port chapters); they are the trusted toolchain
  and are lowest priority.

Only when `codex/os/` declares its effects is 1.8's "hole the size of
codex/os/" actually closed. The mechanism is the tool; the migration is
the closure.

## Open question for later

Whether the compiler's own I/O chapters (serial in the `Opening`/`Emit`
quires) should carry `grounds` too, or keep a minimal toolchain
exemption. Deferred -- the compiler is the trusted base and self-compiles;
this is polish, not a hole.
