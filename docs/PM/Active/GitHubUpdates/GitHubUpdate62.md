# GitHub Update 62

**Release of 2026-09-24.**

Update 62 carries Steve Howell's empty-list and Zig plug reports, shared
literal tables, checked method templates, hardened arm64 and riscv native
code, a paced SMP scheduler tick, network-stack leak repairs, the shell
layout engine's sizing and control-state work, and a parser audit. The
release proof and the three regressions it found follow at the end.

## Steve Howell's reports and pull requests

- **Issue [153](https://github.com/damiant3/Cobblestone/issues/153) and PR
  [154](https://github.com/damiant3/Cobblestone/pull/154):** an empty list
  literal type-checked as any type, so `v : Integer = []` compiled clean. An
  empty list literal is now a `List` or a `LinkedList` and nothing else
  (main 26013), and PR 154's two refusal tests land with it. The issue's
  second shape, desugared tuples, revised records and derived `show` names
  losing their instantiated IR types, was fixed as COMPILER-85 (main 26027).
- **PR [155](https://github.com/damiant3/Cobblestone/pull/155):** the Zig
  plug emits `bit-not` (main 26007).
- **PR [156](https://github.com/damiant3/Cobblestone/pull/156):** the Zig
  plug discards an unused bind in an `act` block, as it already did for an
  unused `let` (main 26007).
- **Issue [157](https://github.com/damiant3/Cobblestone/issues/157):** item 1,
  a literal table rebuilt on every reference, is COMPILER-86 below. Items 2
  and 3: Zig plug allocations no longer bypass std's `0xAA` poisoning (main
  26044), and the plug refuses `ReleaseFast` and `ReleaseSmall`, benching at
  `ReleaseSafe` (main 26086).

The fixes and the initial measurements are his contribution, developed with
Claude; our additions are the regression fixtures and the release proof.

## Literal tables emitted once (COMPILER-86)

A literal Integer table that nothing writes is now emitted once into the
image instead of being rebuilt at each reference. Issue 157's reproducer went
from 528 bytes per read and an out-of-memory stop to zero bytes per read
(main 26048). With record fields and a left `&` operand covered (main 26370),
the compiler shares 38 of its own 41 such tables (measured 2026-09-23), and
the Zig plug shares literal constant tables too (main 26148). The three tables
still unshared are named in `docs/Designs/Active/Compiler/ConstantSharing.md`.

## Checked method templates

Closed direct calls with method-local type variables now reach typed backends
without leaving an unbound variable in the dictionary schema. The compiler
retains checked class, method and instance declarations in version 1 IR
metadata and creates one schema slot per distinct observed method/type demand.
Existing method bodies are reused; six uses of two methods produce five slots.
Unused declarations remain checked templates without invented runtime fields.

Instance declarations now carry the instance-head substitution through nested
signatures and superclass dictionary fields. Method-local type variables get
fresh variables per dictionary projection (main 26681), and a generated
dictionary projection is a direct method use (main 26753). Each copy of an
instance body now keys its own types (COMPILER-90, main 26818). Arbitrary
runtime or escaping polymorphic dictionaries retain their existing boundary.
The contract is `docs/Designs/Done/Compiler/MethodSpecialization.md`.

## Native arm64 and riscv hardening

The arm64 and riscv backends gained the checks x86-64 already had: a zero
divisor refuses; `list-at`, `list-set-at`, `char-at`, `substring` and vector
lane extraction are bounded; bounded parameters and returns are guarded; Real
overflow traps; an unresolved field index refuses; `alloc-bytes` returns
8-aligned pointers; frameless functions keep no callee-saved local; closure
application is arity-aware; and `address-of` is the identity on both targets.
The arm64 disassembler now tells `LSR` from `LSL`. Every subject is graded on
both beds through `.cross-fatal` sidecars.

## Scheduler tick pacing

codex-vm paces core 0 from its programmed PIT reload and joins its kick thread
at teardown, `pit-input-hz` and `pit-count` expose the exact PIT rate, and the
application-processor quantum is calibrated to 10 ms against the HPET (main
26205, 26248, 26353, 26376).

## Network stack leaks and compaction

The web mux leaked 21,028 bytes per request and now compacts (main 26738).
An arena pool replaces per-call allocation in the receive paths (main 26691),
NetIO's seven internal loops compact, as do `http-recv` and `https-pull`
(main 26695, 26710), frame parsing is bounded by length (main 26408), and a
retransmit arm that read emptiness before its own push is fixed (main 26861).
An arm64 web server now serves HTTP under QEMU UEFI (main 26970).

## Shell layout and control state

The layout allocator honours maximum, natural and explicit minimum, and
preferred sizes; a typed scroll viewport, cross-axis alignment and a baseline
guard land; and per-widget control-state flags (checked, invalid, busy and
the focus ring) replace the old `wn-state` field across the app quires, which
is deleted (main 26459 through 26674). The HTML plug styles from the same
flags (main 26648). Files and Edit list their contents in a window of visible
rows instead of reading a whole directory (main 26582, 26702).

## Desk fixes

A full repaint asks the top window to repaint, the menu no longer paints over
window content, the focused window repaints after a close, a closed Editor
drops its buffer, Sheets keeps its own heap mark through a root rebuild, pill
icons agree with the launcher, the start menu follows its selection, and the
new-tab overflow scrolls into reach.

## Parser audit

A chapter-by-chapter audit of the format parsers graded each guard and named
parsers nothing calls: PeerDiscovery, OAuthClient and ImapClient parse no
bytes, and VideoCodec's decode path is uncalled. Base64, CSV, INI, Bencode,
Protobuf, CBOR, MessagePack, WebSocket frames, QOI, BMP, the UI Markdown
parser and Hex each have a named guard row in the register.

## Prism, ACCP and the rest

Prism gained a WebAssembly compile route with a stdin box, an in-tab WAT
assembler, a Bench panel, an in-tab web server, a Mach-O writer, riscv and
ARM64 kernel run panels, and hosted HTTPS over TLS 1.3 with a development-only
self-signed Ed25519 certificate. ACCP, the Codex conduit behind the numerical
math and physics MCP tools, gained its MCP binding, math and physics
libraries, an experiment contract and a standalone runtime built from source.
Open fonts with compound glyphs reach the desktop, Editor, Files and 3D
labels. Smaller compiler closures: a missing token at end of file is reported
(COMPILER-35), the escape checker's pointer-map walk allocates nothing
(COMPILER-45), and a Real at or past 2^63 prints in exponent form on every
native backend (COMPILER-41). The bare gate now runs
`codex/test/cdx-export-check.ps1` over its stage-3 compiler, which closes
COMPILER-39's gate-runner item.

## Release proof

The release gate found seven defects that no lane gate reaches, and each was
fixed before the proof below:

- `section-title-keywords` refused with CDX2087: a constrained function over a
  class with one instance kept a free type variable its body then fixed
  (from main 25834). Such a function is now specialized to that instance, so a
  call at a type with no instance is a type error (main 27104).
- `list-equality` refused `[] == []` with an unresolved equality (from main
  26013; fixed main 27130).
- `gop-source-preview` called a font API that main 25898 had changed (fixed
  main 27101).
- The raw-shell ratchet refused a gate phase added without its record, and
  the tool-catalog check read the depot head instead of the checked-out tree
  (main 27093, 27157).
- Three Valheim entry chapters compiled only when assembled by their runner;
  they now cite their siblings (main 27168).
- Roslyn refused the C# compiler emitted for the DDC (CS0411 on an identity
  lambda from main 25834); the C# plug renders that lambda generically
  (main 27196).
- The diagnostic stick no longer compiled after a network-stack signature
  change (main 26695; fixed main 27200).

The full gate passed on seed `CC7DD455`: a one-pass hard fixed point, the
text leg, 219 declared refusals, all 1,666 test chapters compiling, 3,209
test-run checks with no failure, cross-architecture and plug smoke, 57
generators with no drift, both VM hosts agreeing, and the app sweep at 297
clean units, 4 known baseline failures and no regressions over 301. After
the gate's cross-architecture smoke failed once on an empty capture under
load, the remaining phases resumed from that point on the same build output.

The normal and poison batteries each passed with no failures. The battery
ran as three tier groups whose union is `-Tier all` (944, 384 and 626 passes;
51 declared skips), because one compile slot would have put all compiles in
a batch larger than codex-vm's 256 MB input limit. IR fidelity graded its
reader and verdict controls and reported nine cases with no unexpected
result.

Roslyn built the freshly emitted C# compiler. Both DDC arms produced
3,626,873 bytes with zero differences from the release seed outside offsets
40..135; 96 signature bytes differed. The symbol map matched all 6,081
embedded MAP1 rows by name, address and size. TechnicalDetails records the
seed digest.

Both boot images were rebuilt on the release seed. All 50 diagnostic
rehearsal arms passed across Codex VM and QEMU/OVMF with a 180-second minimum
arm allowance, and the shipping check confirmed the default configuration.
The diagnostic image SHA-256 is
`A507BE3B845E66DDE88D39BF6A125FAB576CB89679FCBA1AA32B08E7ADBC23A8`.

Five-second memory samples from 07:18 to 10:17 measured a free-memory floor
of 0.99 GiB at 07:41, when a diagnostic gate run and a candidate seed's BVT
overlapped: six guests with 4,601 MiB combined working set, about 767 MiB
per guest. Without that overlap the floor was 1.24 GiB, with four guests,
during the final gate's test phases. The sampled guest peak was eleven at
07:38, at 89 MiB combined because those guests had just started. These are sampled host observations,
not per-guest heap high-water guarantees.
