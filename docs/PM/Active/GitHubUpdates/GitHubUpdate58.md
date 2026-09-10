# GitHub Update 58

**Release of 2026-09-10.** Update 58 contains compiler
correctness fixes, lower network allocation costs and clearer build verdicts.
The release also corrects the public account of the hardware keyboard path.
The compiler identity and distribution digests are in
[TechnicalDetails.md](../../../../TechnicalDetails.md#distribution-artifacts).

## Landed changes

**Equality and type inference.** List equality compares element contents;
separately allocated lists with equal contents no longer compare unequal
(main 24774). Vector comparisons resolve the operand type after unification,
giving application expressions the correct vector comparison result type
(25061). The associated type diagnostics
name unsupported wider vectors, wider masks and Vector/SizedVec mixtures.

**Decimal literals and the compiler's own IR.** The bare-metal decimal
parser now rounds Real literals using exact integer arithmetic and
round-half-to-even (25442). The release gate also exposed an allocation
reservation missing from the unlifted IR path. Both IR paths now reserve
space before equality helpers are attached, preserving the source-shaped
view required by the jonquil check (25466). The same landing adds Rust
SizedVec emission and updates two test contracts. The DDC prerequisite found
the same missing type arm in the C# emitter; that arm is now present and the
C# bootstrap witness passes (25471).

**Network allocation.** Receive processing passes payload ranges through
the network stack instead of copying payload lists at each layer (24854).
TCP checksum construction also sums the pseudo-header directly on the send
path (24862). Those changes remove allocations proportional to payload
length from the affected paths; the network measurements and their scope
are recorded in
[ProtocolStack.md](../../../Designs/Active/IoT/ProtocolStack.md).

**Build and desk safeguards.** Workflow failures have their own event and
status variants, so consumers can distinguish failure from completion
without interpreting a message (24993). The disk compile harness retries
the bounded image-open race after VM shutdown and checks complete reads
(25053). The desk refuses allocation marks outside the neighboring live
entries; the remaining desk allocation work stays open (25181).

**The hardware investigation.**
[The Lost Paradise](../Stories/TheLostParadise.md) assembles the flight
record, source evidence and recommendations after the final hardware
sitting. The report's remaining recommendations are open work, not claims
of completed repairs. The PS/2 correction below preserves the distinction
between a ceremony observed on hardware and the input mechanism previously
claimed for that ceremony.

## Release verification

Measurements below describe the release artifacts verified on 2026-09-10.

| Proof | Current result |
|---|---|
| Full release gate | Passed in 1,057.7 seconds; 3,058 compile/runtime checks with zero failures. |
| App-class sweep | 290 clean units and 3 declared baseline exceptions. Compilation coverage only. |
| IR fidelity | Reader self-test passed; all 3 verdict ablations matched; 8 real cases, zero unexpected verdicts. |
| Signed seed and symbol map | Candidate installed; all 5,700 sidecar symbols match embedded MAP1 names, addresses and sizes. |
| Full normal battery | 1,764 passed, zero failed, 49 declared exclusions; all oracle checks matched their contracts. |
| Poison build and full poison battery | 1,764 passed, zero failed, 49 declared exclusions; normal-kernel restoration verified. No retries, guest deaths or dropped-byte reports in either battery. |
| Diverse double-compiling | Passed. Both arms produced 3,358,838 bytes, with zero differences outside signature offsets 40..135. |
| Distribution images and shipping checks | Boot image rebuilt; embedded seed and source match the release inputs. Diagnostic image passed all 50 rehearsal arms across codex-vm and OVMF, and the shipping check accepts its default configuration. |
| Box profile | Sampled free-memory floor 3.74 GiB during overlapping normal-battery and DDC work. Peak VM-host process count 16, totaling 103 MiB working set, or 6.44 MiB/process. Actual guest peak is not recoverable from that counter. |

Both batteries retain the prescribed `-Tier all -Jobs 4` selection.
Declared exclusions remain deliberate selections and are reported separately
from unexpected failures. No sidecars or override flags are removed or added
to bypass the declared selection.

The [memory profile](../../../Agents/box-release-2026-09-10.csv) contains
686 retained samples. Its `guests` column counts VM-host processes, including
run-list supervisors that create no guest partition. Peak aggregate VM-host
working set was 3,802 MiB across four processes. Those process averages do
not establish per-guest RAM budgets; the existing admission bars remain.
The CSV header was restored after build cleanup removed the original header.

Diagnostic image SHA-256:
`96D330A29FF703956A199D5C96B7729EE054887E50DFE16F60C3AA983196DE40`.
The image contains no identity and uses the checked-in default configuration.

## Known limits

The Zig and wasm decimal parsers still use the earlier algorithm and can
disagree with the corrected seed. COMPILER-57 in the
[compiler backlog](../../../../codex/compiler/compiler-backlog.md) owns the
remaining parser work. The app sweep establishes compilation, not runtime
behavior. IR fidelity retains the expected `DROPPED` verdict for derived
bounded-integer ranges; zero unexpected verdicts does not claim every fact
survives lowering.

## Corrections to the earlier public record

- **Update 34 announced "the PS/2 keyboard works", and the word "PS/2" is
  withdrawn.** The first-boot ceremony of 2026-07-08 did run on the ASUS on
  glass, and the keyboard that typed it was a USB keyboard impersonated as
  PS/2 by the BIOS's SMM legacy emulation, not a keyboard read from a PS/2
  controller. Three days later, on 2026-07-11, the xHCI spec-fidelity
  campaign's BIOS/OS ownership handoff disabled that SMM emulation as
  collateral, and no amendment was filed at the time. The consequence ran
  forward for three weeks: the hardware ladder's rung 2 on 2026-07-29 read
  "no PS/2 on this board" from a board whose working input fallback the
  fleet had removed by its own hand. Update 34's companion claim that the
  read path was confirmed on metal rests on an OVMF run and a `0xFA` ACK
  byte rather than on a metal keystroke.

  What stands from Update 34 is the ceremony itself: the first-boot screen
  rendered on hardware, the passphrase was accepted, and an Ed25519
  fingerprint was printed from a keypair derived from hardware entropy.
  What is withdrawn is the mechanism named for the input path.

  The full account is `docs/PM/Active/Stories/TheSilentKeyboard.md`
  (section 1.1, the flight history, for the emulation and for the handoff
  that disabled it) and `docs/PM/Active/Stories/TheLostParadise/` (part 03,
  the sittings ledger, findings BLU-F19 and BLU-F21; part 10, finding
  CC-7). This paragraph is R-16 of that report's part 11.
