# GitHub Update 43

**Scope: main CLs 15085 onward, opened 2026-08-14.** Update 42 covered
14991 to 15084. Accumulate this cycle's themes here as they land; every
number in the final report gets re-measured at the release head, not
carried forward.

## An outside contributor found a live data-corruption bug

**GitHub PR 64 is absorbed** (red, main 15184 and 15190). Steve Howell's first
batch was five pieces, each gated and each on its own CL:

- **`net-recv-raw` rounded the `REP INSW` word count DOWN**, so an odd-length
  frame lost its last byte and the caller read one byte of the PREVIOUS frame
  at that offset. A real byte from the same stream, so it parsed and was never
  diagnosed. His measurement: 11 corrupt transfers in 15 on a 191 KB payload.
- **The stack learned ARP senders and never answered requests.**
  `arp-build-reply` had sat in `Ethernet.codex` with no caller: codex-vm's NAT
  knows the guest MAC by construction and never asks, so the gap was invisible
  here and killed a plug's connect under QEMU's slirp. The new
  `arp-reply-test` carries its own control, a request for another IP that must
  answer `arp-learned` and queue nothing, so a function that replies to
  everything cannot pass.
- **`bytes-to-text` was O(n^2) in 39 of 42 definition sites.** One definition
  now, in `plugs/common/PlugTypes.codex`, bundled into every transpiler plug.
  Differential on a 2,193,541-byte IR: the shared linear form emitted 563,768
  bytes in 42 s, the quadratic form restored went out of memory with no output.
- **The x86-64 deck-record intercept fired on the NAME**, so every plug kernel
  ran `__deck-enter`/`__deck-exit` against an allocator it never initialized.
  It fires on the defining chapter now.
- Ten commits of zig plug work, his design and his working versions,
  deliberately **not verified here**: no zig toolchain on this box and zig is
  not wired into `plug-oracle-test` (`plugs-backlog` 1.13).

His second batch produced the cycle's one new diagnostic. Two identical
`is otherwise -> st` arms sat in one `when` in `TypeCheckerInference` and the
second could not run; **CDX2096 UnreachableMatchArm now refuses any arm
following an unguarded catch-all**, a guarded one honoured. Error rather than
warning on evidence: the 270-unit app sweep finds no other instance.

### In the contributor's own words

Asked what he was doing and why, Steve Howell answered:

> Our long-term goal during this session was to make the zig plug reliable enough that it could stand in for the bare-metal frontend in text-emitting mode — and use the effort of getting there to find defects in Codex.  We progressed through layers of the compiler starting with the lexer. It's still in progress.

That is worth reading twice, because it explains the shape of everything above.
Every defect in this section is a by-product. He was not auditing our compiler;
he was trying to make one plug good enough to stand in for the real front end,
and the defects fell out of the attempt. A second implementation held to the
standard of agreeing with the first is the most productive instrument anyone
has pointed at this compiler, and it is the same principle as the DDC witness
and the independent rechecker, arrived at from the outside and aimed at a part
of the tree neither of those covers.

**It is still in progress and the PR stays open.** The five newer zig commits
on it are his and are not taken here.

### What we could NOT check, stated plainly

His central claim is that the emitted zig compiles and runs byte-identical to
bare metal on `Syntax/Lexer.codex` and on the whole of `Syntax/Parser.codex`
(2,059 lines, 18,812 tokens, 202 defs). **Nothing in this tree can execute that
arm.** There is no zig toolchain on the build box and zig is not wired into
`build/plug-oracle-test.ps1`, so that result is his measurement on his machine
and not ours. What was verified here is that the plug builds, emits for a real
subject, and gets the CCE text model right by hand-decode.

Two of his figures were re-measured rather than carried forward, and both moved
slightly. `bytes-to-text` was quadratic in 39 of 42 definition sites once his
own zig fix is counted, not 42 of 44. And his `repro-crash` probe does not page
fault on our seed as he predicted; it answers wrongly, which is the same defect
reading out as corruption rather than a trap. He had already said the severity
was allocation-pattern luck, and it is.

### It found a class, not just three defects

Three of his findings came out of one activity: bundling a SUBSET of the
compiler into a single unit, which is what a plug bundle is. The monolithic
build hides that class completely, because the compiler is assembled by glob
and any borrowed name is present anyway. Nothing here exercised the property,
so we were learning about it from a contributor.

`build/check-subset-cites.ps1` (red, main 15235) closes that gap: it compiles
every chapter as its own unit and lets the compiler answer. On its first run it
found `BootPaint.codex:246` calling `to-unicode` out of Foreword CCE while
citing nothing at all. The lesson is `L-SUBSET`, and unlike most rows in that
file it has a runner.

## The fleet emptied its memory into the docs that own the subjects

Main 15107 through 15130. A memory file is read by one agent and has no
trigger, while the init reading contract routes everyone to a doc at the
moment of relevance, so the traps moved rather than being summarised.

**`PerforceProcess.md` is a quick reference plus an indexed trap table**
(reek, 15111): 1334 lines and about 17k tokens of Symptom-Cause-Fix prose
became 423 lines and 7.7k, with no trap dropped and one stable id per row.
What was cut is narration.

**`CLAUDE.md` gained stable rule ids and four precedence tiers** (blu,
15130). The precedence already existed and was scattered, written into
whichever rule somebody had been burned by. The honesty rule was the
highest-order rule in the file and existed only as a clause inside a tier-4
rule about brevity; it is a rule of its own now. Ids because the ordinals
are load-bearing: 14 citations across 11 docs, and three different numbered
rule systems all cited as "rule N".

**The five-pass review of `PerforceProcess.md` found its opening safety
promise false** (red, 15124), in two places. It said the preflight catches a
dropped add and that the gate refuses to run against a workspace that does
not match the depot. Reproduced: untracked file on disk, `p4 status` naming
it, preflight printing `OK (nothing opened)`, exit 0.

## One source for the x86-64 identity memory map

**`MemoryMap` is a new foreword chapter** (reek, 15158). Five sites spelled
`3221225472` and `4294967296` by hand: the code generator that builds the
page tables, the e1000 and xHCI BAR checks, and two boot diagnostics. The
device window is the single gigabyte-sized page directory at index
`bare-metal-pd-count`, which is NOT the gigabyte after `bare-metal-ram-size`;
the two coincide only when RAM is a whole number of gigabytes, so a ram-size
derivation looks right at 3 GB and is not one.

Only one of the three BAR fates is loud, which is why the constants matter. A
memory BAR above the window is unmapped and faults on the first register read.
A BAR **below** it is mapped as ordinary RAM inside the arena `alloc-bytes`
hands out, so the driver and the heap point at the same bytes and nothing
faults at all.

## Trust: the readable-intermediate defense now has a runner

The diverse-double-compile witness rests on one property: a Thompson payload
survives the rebuild only if it reaches the readable IR, and is therefore
visible as text. That property was reasoned, not enforced. Two landings this
cycle make it concrete (val).

**The self-reproducing quine carrier is measured, not reasoned** (main 15141,
`docs/Test/Active/DDC-QUINE-ARM.md`). A trojaned `ir-emit-def`, run over CLEAN
source, emits its own definition byte-identically in one shot (D=2285 chars;
emitted == `replace(D, "@QQ@", ir-quote(D))`; both 4698). The measurement
corrected the framing: `ir-emit-def` is the CARRIER, it touches IR-text emission
only, and the shipped seed is CDX whose emit path never calls it, so the pure
quine is INERT in the shipped artifact. Defeating the byte-comparison needs a
codegen payload paired with the carrier, exactly as the 2026-08-11 frontend-IR
arm did. The survivor is greppable text either way, which is the
neutralization, not a defeat of the trust claim.

**The jonquil: a standing gate check for that survivor** (main 15170,
`build/jonquil.ps1`). The visibility claim now has a runner (L-BODY). On every
build the `jonquil` phase emits the compiler's own IR and FAILS if any
definition embeds, as string data, the IR-def header for its OWN name, the
self-reproduction signature of the DDC-QUINE-ARM construction. Named for the
narcissus, a def caught admiring its own reflection. Scope stated honestly: it
is a tripwire for that construction, NOT a complete quine detector (general
detection is undecidable, Rice), and it is ORTHOGONAL to the DDC witness (a
machine-code-only Thompson trojan leaves no IR trace, so it cannot see one and
does not replace the witness). Clean tree: 4700 defs scanned, 0 hits. It stands
in the gate beside the canary.

## The cost model has numbers now, and they inverted its own advice

**Items 3.1 and 3.2 of `CostModel.md` landed** (blu, main 15145). 3.2 is
eight lines in `emit-out-of-memory-handler`: the handler already held both
sides of the stack/heap collision in RBX and R12 and printed neither, so
`OUT OF MEMORY` was read as heap exhaustion whichever side had actually run
away. It now prints `SP=` and `HEAP=`. On the SUT the guard-page harness
gets `SP=00000000bdfffec8 HEAP=00000000b9e00028`, and `HEAP` lands 0x28 past
the guard address the harness computes independently, so the stack is
visibly 67 MB clear.

3.1 is the measured table, now in `DevelopersGuide.md` under `## Text`. It
corrected the design doc it came out of. **Section 3.5 was headed
"character-level Text access costs 1,040 bytes per character" and that is
not what was measured**: both of its rows called `to-unicode`, so it never
separated the accessor from the converter and charged the whole figure to
the accessor. Isolated, `char-code-at`, `char-at` + `char-code` and
`text-length` allocate **zero**, and `to-unicode` alone is the 1,040
(4,942,080 bytes over 4,752 characters). That inverts the advice: the old
reading sends a reader to `text-split`, which the section itself admits
every integer parser in the tree defeats, when the fix is dropping one call
per site and is what R-CCE already required.

**Second measured result, and it is the shape 3.3 exists to refuse.** `a &
b` allocates a new text of the combined length every call, so an
accumulator is quadratic: 200 appends building 2,000 characters retain
**203,200 bytes**, against **2,008** for `text-concat-list` over the same
pieces. 101x at n=200, and it grows with n.

**3.3 itself is untouched and still unscheduled.** It is the actual
proposal (a declared allocation bound in the `punctual` family, transitive,
refused at compile time) and its open question is unchanged: the type rules
got teeth because the rechecker abstains where the guide is silent, and
nothing equivalent exists for cost.

## The stack now checks a checksum it only ever wrote

**B5 closed** (blu). Nothing in `codex/os/net` verified a checksum on the way
in: `tcp-with-checksum` and `ip-checksum` were both build-only. Steve Howell
raised it in PR 64, and **it is why the other half of his PR was invisible** --
the DMA truncation fixed in that arc substituted one byte of the previous frame
into every odd-length receive, and the corrupted payload reached the parser
unchallenged because the only thing that could have caught a substituted byte
is a checksum nobody checked. Two independent defects, and the second is what
made the first silent rather than diagnosed.

The fix needed no new arithmetic. A one's-complement sum taken over a range
that INCLUDES the stored checksum folds to zero when the checksum holds, so
both checks are the existing `ip-checksum` applied to the received bytes.
Refusal is a silent drop with a named output, per RFC 1122 4.2.3.2: the
sequence and port numbers an ACK would quote are drawn from the bytes just
found untrustworthy.

**Eight test files had to be repaired, and that is the more interesting
result.** `tcp-reliability`, `tcp-seqwrap`, `net-io-clock`, `web-server-test`
and the four `web-mux-*` tests all built inbound frames with
`tcp-build-segment`, which leaves the checksum bytes zero, and never called
`tcp-with-checksum`. A real peer always computes that field and no fixture in
the tree did, so every hand-built frame in the battery was one a correct
receiver should reject. Each now computes it, and **all eight reproduce their
existing `.expected` byte for byte**, so the repair is faithful rather than an
accommodation.

**Writing the arm turned up a remotely reachable guest crash, and it is the
half a checksum cannot cover.** The checksum is computed over the bytes that
arrived, and on a DMA-truncated frame the IP header is one of them: it
validates while `total-length` still claims bytes that are not there, and
`ip-payload` then walks past the end of the list. Measured on a frame cut by
six bytes, the guest died `!EXC=06` inside `net-process-frame` with the two
disagreeing lengths sitting in R12 and R13 (52 actual, 58 claimed). It was
reachable before this work and the checksum check neither introduced it nor
could have caught it. `ip-length-valid` is a separate predicate from
`ip-header-valid` so truncation reports as `bad-ip-length` and corruption as
`bad-ip-checksum`, because they send an operator to different places. Claimed
length UNDER actual is normal and passes, since Ethernet pads every frame to
60 bytes.

**Done is an ablation, not a green run.** `codex/test/tcp-checksum-refuse`
flips one payload bit AFTER the checksum is computed and requires refusal,
against a control that leaves it alone and requires delivery. With the guard
replaced by `if False` the arm reports `dirty-verdict=received 18 bytes`,
`dirty-bytes=18`, `dirty-emitted=1`, against `bad-tcp-checksum`, 0 and 0 with
it in place, and every control line is unmoved across the ablation. The live
path is undisturbed: `cdx-serve-test` passes and `nat-conn-churn-test` answers
80 of 80 connections, because `codex-vm`'s NAT computes a correct
pseudo-header checksum.

## A dot-sourced loop variable made a harness unrunnable

`build/vm-config.ps1` is dot-sourced by 19 build scripts, and its search for
the QEMU install used `foreach ($root in @('D:\', 'C:\'))`. Dot-sourcing
runs in the caller's scope, so every caller that kept its own `$root` (the
workspace root, in the ones that have one) got `C:\` written over it and
then looked for its inputs there. Fixed in the generator and the shipped
script together (blu, main 15155).

## A test arm that could no longer fail was retired

`guard-page-test.ps1` carried a LEAP arm that ran the whole-compiler
`-IrCce` emit on the premise that it overruns the guard page. It does not
any more: that emit completes at a peak frontier of 1,305,881,760 bytes,
1.7 GB below the page, so the arm reported FAILED on a healthy tree.
Lowering `-MemMB` to bring the guard down to the workload was rejected on
measurement rather than taste -- at 1280 MB the compiler legitimately needs
more memory than exists, so the trip is correct behaviour and not a runaway
being caught, which is the same defect that got the `-Decks 450` arm
rejected on 2026-08-04. **What that costs is recorded rather than papered
over**: FIRE parks the frontier synthetically, so nothing now exercises the
guard under a genuine allocation walk, and the PASS banner says so (blu,
main 15167).

## The hypervisor can hold a guest

**A8 moved from "runs a guest" to "could run a guest safely"** (fester, main
15138, 15149, 15163, 15173). `DevHypervisor` launched with no EPT at all:
proc-based controls `0x1000080` with bit 31 clear, and `0x401E` / `0x201A`
never written, so guest physical WAS host physical and the guest CDX's
`__start` would have written the host desk's allocator cells at 28720 and
28728 before its first allocation. Secondary controls, a three-level EPT with
2 MB leaves and an EPTP now exist; VMXON, VMCS and the guest region are
allocated instead of nailed at 8/12/16/20 MB; host CR3 is `cpu-read-cr3`
rather than the guest PML4; and the guest's low 64 KB is initialised so
`__start` finds a RAM size where it looks for one.

`GuestConfig.memory-mb` was read at **zero sites** before this, so the
"256 MB guest" the plan described was never a fact (L-UNCALLED, L-PUBLISHED).
It is live now and guarded by a refusal rather than an unchecked heap bump.

Two corrections worth more than the feature. **The arena was not the wall**
the plan said it was; it had been measured 2026-08-13 and the entry had gone
stale. And **`vmcs-host-rip` needed no compiler change**: this lane reported
that it did, and that was wrong -- `vmlaunch-full` has always set HOST_RSP and
HOST_RIP in `emit-vmlaunch-full-helper`. The real defect was a mismatched
pair, `vm-launch-with-devices` taking the bare `vmlaunch` and then resuming
with `vmresume-full`. Verified by A/B on the artifact, because the symbol map
emits all four helpers unconditionally and could not have shown the opposite.

**The desk image's arena is measured at `-AllocPages 131072`** (512 MB). It
boots on real edk2 under OVMF in a 2048 MB machine and paints; the same image
at `-MemMB 256` never reaches the paint, so the bed can refuse. New
bed-runnable test `vmx-ept-table` asserts the EPT encoding and was falsified
by sabotage.

## The symbol map survives a compiler build

**`compile.ps1` appended the map flag only when `-Repl` was ABSENT** (fester,
main 15088), and `Invoke-BuildCdx` passes `-Repl` for every stage -- so every
compiler build produced a symbol map and threw it away. The coupling was in
the driver only: `emit-cdx` reads `repl` and `map` as independent flags and
the map never enters the binary. Gate green, hard fixed point in one pass,
`constants.hash` unchanged.

## Publication got a parking lot, and it caught a stale number on its first use

**`docs/PM/SomethingSeenDuringRelease.md` is new** (blu, 15109), for things
that are true all the time and only cost us at publication. Damian's framing
is the organising rule: publication and public are the same word, so a wrong
number is private until we publish it.

Both of its opening entries were worked in the run-up to this release (reek,
15207 and 15214). The doc counts had drifted again and differently: **13 of 61
claims at main 15194**, against 5 of 61 a fortnight earlier and a different
set both times. All corrected except the seed digest triple, which is measured
at the release head by definition. The second entry was the checker's own
header claiming it is not wired into `build.ps1`, which stopped being true
when the opt-in block landed, and told a reader the switch does not exist at
the one moment it matters.

## Open from Update 42

- **`COMPILER-3`: `-Repl` and non-repl compiles of the same source differ
  in 255,683 bytes.** Same function names at the same offsets, different
  bytes inside the bodies, and no gate can currently see it. Detail and the
  first experiment are in `codex/compiler/compiler-backlog.md`. Still open:
  15088 fixed the map's coupling to the mode, not the codegen difference, and
  the 16-byte difference it measured on an 84 KB program is a separate and
  understood one (`Exit` emits the shutdown epilogue, `ExitRepl` a jmp to the
  repl loop).

- **`COMPILER-5`: a hex literal past i64-max breaks the text round-trip.**
  Filed by reek during the A5 work.

- **27 skipped tests**, catalogued by `build/audit-skips.ps1`: 7 REAL, 6
  TRIVIAL stubs that assert nothing, 13 with no `.expected`, 1 STALE that
  now passes. The trivial stubs are the ones that read as coverage and are
  not.

- **The 0xFE8 RAM-size cell is a private ABI between the harness and the
  guest.** Steve Howell named it as a compromise during PR 63 and he is
  right: nothing in the compiler writes that cell, and the multiboot
  header does not set MEMORY_INFO, so the guest never asks the loader the
  question whose answer it is handed. Retiring the cheat is open work.
  Re-verified 2026-08-15 and it holds exactly: `__start` LOADS RSP from the
  cell (`X86_64Chapter.codex:376-377`) and every reference in the emitters is
  a load. Two documents said the opposite -- `cdx-to-pe.ps1` and
  `UsersHandbook.md` both credited bare-metal `__start` with filling it -- and
  both are corrected (main 15212). The writers are `codex-vm.c:13576`, QEMU's
  `-device loader,addr=0xfe8`, and the UEFI stub.

## Opened this cycle

- **`B5`: nothing verifies a received TCP checksum**, which is why PR 64's DMA
  truncation was silent rather than diagnosed. Done means an arm that FAILS:
  flip a payload byte after the checksum is computed and require refusal, with
  a control that leaves it alone.

- **The zig plug's output is unverified here** (`plugs-backlog` 1.13). No zig
  toolchain on this box, and zig is not wired into `plug-oracle-test`.

- **Nothing exercises the guard page under a genuine allocation walk** since
  the LEAP arm was retired. FIRE parks the frontier synthetically. A
  replacement needs a workload that overruns at NORMAL memory, which this tree
  no longer has.

- **The deck floor under `codex/build` has no slack left** (fester). Once the
  last four stub generators were back-ported, `BuildScript.codex` sits at
  exactly the 1.25 required margin, 51 of 64. OK and out of room are the same
  reading, and the next line added there reds the gate inside somebody else's
  CL.

## The release proof, at head 15253

Every number here was measured at the release head, not carried forward.

| proof | result |
|---|---|
| battery, `-Tier all` | 1463 tests, **1417 pass, 0 fail**, 46 skip |
| app sweep | **265 clean, 5 known-dirty, 0 regressions** |
| poison build (0xCD fill) | 1463 tests, **0 fail** |
| DDC witness | **HOLDS** -- 2,798,031 bytes both arms, **0 differing outside the signature** |
| standing gate | green, hard fixed point in one pass, `constants.hash` unchanged |
| oracles | scalar 2013/2013, vector 130/130, CCE 1485/1516 with 31 in documented gaps and 0 unexplained |

Seed `F3722EAC019ACD5A`, 2,798,031 bytes, content hash `93DCA70AF9BBBFE0`,
already the fixed point at the release head so no rebuild was due. The symbol
map WAS stale and is refreshed: 5194 lines against 5189, the five new names
being exactly the functions this cycle added. `seed/Codex.img` rebuilt to
`400D37EA`.

**The DDC is the one of those four that does not take the compiler's word for
anything**, and it is the reason the other three are worth running rather than
a substitute for them. A compiler carrying a Thompson trojan is a stable fixed
point too, and it passes a battery, a sweep and a poison build without
complaint.
