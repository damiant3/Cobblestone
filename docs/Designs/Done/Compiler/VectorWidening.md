# Packed vector widths

Every currently supported packed vector occupies 128 bits. `Vector 2 Real`
contains two f64 lanes; `Vector 4 (Real approximate)` contains four f32 lanes.

## Four-lane f32 contract

Arithmetic and comparisons use the ordinary operators. Comparisons produce
an inferred `VectorMask 4`; mask types have no surface declaration syntax.

| Operation | Four-lane name | Result |
|---|---|---|
| Broadcast | `vec4-splat` | Four equal f32 lanes |
| Extract | `vec4-extract` | One f32 lane |
| Mask predicates | `mask4-any`, `mask4-all`, `mask4-none` | Boolean |
| Mask population | `mask4-count` | Integer from 0 through 4 |
| Selection | `vec4-select` | Per-lane choice between two f32 vectors |
| Reduction | `vec4-reduce-add` | Left-to-right f32 sum |

The new mask consumers, selection and reduction are implemented on x86-64
and wasm. ARM64 and RISC-V explicitly refuse those names. Other plug targets
are not certified for the new family.

On x86-64, MOVMSKPS extracts four sign bits and the all-set pattern is 15.
Selection blends the full 128-bit payload, independent of lane width;
MOVUPD and the packed-double bitwise instructions preserve the f32 bit
patterns. No new register width or allocation size is required.

Mask queries and reduction add no runtime heap allocation beyond evaluation
of their operands. Selection allocates one 16-byte vector. Each consumer has
constant work for the fixed four-lane width.

`codex/test/vector-f32-mask` grades every four-bit mask, asymmetric selection
with distinct lane values, and order-sensitive f32 reduction. The existing
`vec-wide-refused` arm retains the two-lane contract of the unnumbered names.

## Remaining width work

`Vector 4 Real` and `Vector 8 (Real approximate)` need 256-bit storage and
YMM/AVX encoding, with runtime feature admission on machines lacking support.
Neither width is implemented by adding a lane-count parameter to the
128-bit emitters.

The registry keys types, allocation classes and emitters by builtin name.
Instantiation-keyed dispatch remains a separate design choice; the current
four-lane family introduces no such machinery.

Any additional width requires matching producer and consumer implementations,
explicit refusal on unsupported targets, and executed lane-sensitive tests.
Arithmetic retains the operator spelling rather than adding duplicate
`vec4-add` names.

## The 256-bit plan (COMPILER-77, val)

AVX is optional hardware (Damian, 2026-09-25): an artifact using a 256-bit
width runs where AVX is present and refuses by name where it is not. Each
stage is one proof of two headings.

**What the machine does today.** Boot sets CR4 to 0x620 (PAE, OSFXSR,
OSXMMEXCPT) on every core; CR4.OSXSAVE is clear, so any VEX instruction is
#UD until stage 3 admits AVX. codex-vm exposes XSAVE and AVX on an AVX host
and none under `-no-avx` (stage 2), so the bed serves both arms.

1. **FXSAVE per process. DONE:** each slot's area is the top page of its
   spawn region; `PreemptiveScheduler.md`, "Each process keeps its own XMM
   state", and `codex/test/xmm-preempt`.
2. **codex-vm exposes AVX. DONE:** the partition takes the host's XSAVE
   features, leaf 0's maximum is 0Dh, leaf 1 ECX carries XSAVE (26), AVX (28)
   and OSXSAVE (27, the asking processor's CR4 bit 18), leaf 0Dh answers
   XCR0 = 7 (576 bytes, 832 with AVX enabled), and an AP's CPUID is answered
   on that AP; `-no-avx` is a machine without either (`OperatorsManual.md`,
   `codex/test/cpuid-avx`, `cpuid-no-avx`). Not yet observed: a guest XSETBV
   of 7 and a VEX instruction on the bed, which stage 3 is the first to run.
3. **AVX admission at boot. DONE:** the boot processor (before interrupt
   setup) and each AP (before it counts itself ready) read CPUID leaf 1 and,
   with XSAVE and AVX both reported, set CR4.OSXSAVE and XSETBV XCR0 = 7; the
   boot processor zeroes and then sets `avx-admitted-addr` (36384,
   `ArchitectsSketchbook.md`). The switch saves with XSAVE/XRSTOR (mask 7) while
   the cell reads 1, zeroing the area's header first, and with FXSAVE/FXRSTOR
   otherwise; each area is its slot's 4 KB page. `codex/test/cpuid-avx` reads
   OSXSAVE and the 832-byte size back from codex-vm, which answers from the
   live CR4 and XCR0; `xmm-preempt` covers the switch on two cores.
   The Option A (UEFI) path, whose stub jumps to `opening` past `emit-start`,
   admits the same way from `emit-runtime-init-fn`; the diagnostic stick's
   `avx` stage grades it (`admitted` under codex-vm, `not-offered` under
   `-no-avx`).
4. **Types. DONE:** the checker admits `Vector 8 (Real approximate)` and
   `Vector 4 Real` through the `vec8-`/`mask8-` and `vec4d-`/`mask4d-` rows of
   `Types/Builtins.codex`. `VectorMaskTy` carries its vector's element type, so
   a mask of four `Real` lanes does not unify with one of four `Real
   approximate` lanes (`codex/test/errors/mask4-on-vec4d`, `mask4d-on-vec4`),
   and the literal lane check covers `vec8-extract` and `vec4d-extract`.
5. **x86-64 emission. DONE, proven on metal:** on the ASUS the diagnostic stick's `avx` stage read `admitted` (xsave, avx, avx2 and osxsave 1, `area-max=1088`) and all ten 256-bit lane rows matched, `lanes=10 match=10` (sitting 16, 2026-09-25, `HardwareSitting.md`). `vex` in
   `X86_64Encoder.codex` emits the two- and three-byte forms; the operators run
   VMOVUPD/VADD/VSUB/VMUL/VDIV/VCMP on YMM (`emit-wide-vec-op`), and each named
   builtin has an emitter per width (splat and extract through 128-bit stores and
   GPR loads, select through VANDPD/VANDNPD/VORPD, mask queries through
   VMOVMSKPD/PS, reduction left to right in scalar SSE). Every 256-bit sequence
   ends in VZEROUPPER. Any other vector wider than 16 bytes still refuses through
   `emit-vec256-pending`. Every 256-bit emitter sets `avx-used` in the codegen
   state; when it is set, `__start` (and `runtime-init`, the Option A path's
   admission point) reads `avx-admitted` right after admission and, on 0, prints `REFUSED: 256-bit vectors need AVX, which this processor
   does not provide` and exits through port 0xF4 with 1 before `opening` runs. A
   program that declares a 256-bit type and emits no 256-bit operation carries
   no check. The compile mode flag `avx-local` suppresses the check for a unit
   that reads `avx-admitted` (36384) itself before its 256-bit work, which is
   how the diagnostic ladder carries its VEX rows in the same payload
   (`codex/test/vector-wide-avx-local`, `vector-wide-avx-local-admitted`). A hosted target refuses 256-bit vectors at compile time: its start
   has no AVX admission. `codex/test/vector-wide` and `vector-wide-no-avx` are
   the codex-vm arms. The metal sitting runs the lanes inside the diagnostic stick's
   `avx` stage, which is compiled `avx-local` and runs its 256-bit rows only
   when the stage reads `admitted` (one image, one boot; root, 2026-09-25).
6. **Other targets refuse. DONE:** ARM64 and RISC-V refuse every `vec8-`,
   `vec4d-`, `mask8-` and `mask4d-` call through their unresolved-call path
   (`[UNSUPPORTED] <name>`, no binary), pinned by
   `codex/test/vec256-cross-refused.cross-refusal`. The wasm plug turns each of
   those names, and any vector arithmetic or comparison whose operand is wider
   than 16 bytes (`wat-vec-is-wide`), into a token wat2wasm rejects
   (`codex-refused-<name>-256-bit-vector-on-wasm`), so the module fails to
   assemble instead of trapping at run time. No runner grades the wasm arm
   (`plugs-backlog.md` 2.82). Text plugs stay
   uncertified.

**Builtin names (root, 2026-09-25): a distinct prefix per family,** never
dispatch on the instantiated type, because `splat` takes a `Real` either way
and only the expected type could tell the families apart (L-UNRESOLVED). The
eight-lane f32 family is `vec8-` with masks `mask8-`; the four-lane f64 family
is `vec4d-` with masks `mask4d-`, whose mask is 256 bits wide against the
128 bits of `mask4-`. A target without the width refuses the name.

**Cost.** Stage 1 adds 512 bytes per process slot and a FXSAVE/FXRSTOR pair
per switch; stage 2 raises that to 832 bytes and XSAVE/XRSTOR where AVX is
admitted. A 256-bit operation allocates one 32-byte result, as the 128-bit
ones allocate 16. The startup check is one load and one branch.

**Tests.** Lane-distinct values in every lane, a negative lane, and an
order-sensitive reduction per width (the `vector-f32-mask` pattern); the
refusal arm under codex-vm `-no-avx` asserting the refusal text and that no
lane result printed; stage 1's two-process arm. A green codex-vm arm is not
proof of metal (L-OPTIONAL): stage 5 closes on a hardware sitting.
