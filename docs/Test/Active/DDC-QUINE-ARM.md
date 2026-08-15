# The DDC quine arm: the construction, and how to run one

*val, 2026-08-15. The item is CurrentPlan Track C, C1's named open hole. This
file exists because the construction had been derived, validated at toy scale
and then carried only in an agent's memory, which is not a place work lives:
an artifact goes in the depot or it does not exist. A prior session's working
files were written to a session-scoped scratchpad and are unrecoverable.*

## What this arm is for, and what it is not

The two sabotage arms in `docs/OperatorsManual.md` ("The witness has a negative
control") measured the boundary: any payload living only in the binary is
CAUGHT, because the double-compile rebuilds from clean source. The sole
survivor is a self-reproducing quine. **Until one exists, that boundary is
reasoned and not measured**, and this arm is its falsification test.

**It is not an attempt to defeat the trust claim, and it must not be read as
one.** The theorem stands: `stage2 = stage1(clean source)` and
`stage1 = Roslyn(plug(IR))`, so `stage1` is trojaned only if the IR is -- and
the IR is readable text. Survival of the rebuild REQUIRES visibility in the IR.
A genuinely hidden Thompson trojan, one that lives in machine code and appears
in no source and no intermediate, therefore cannot survive, and the
payload-only control arm confirmed that directly: hidden purely in machine
code, it came back `stage2 == seed`, caught. The "plug as a second site" idea
fails identically, because the C# is readable too.

So the honest statement of what a successful quine would show is narrow: that
the byte-comparison alone does not also GREP the IR. What survives is still
visible to a reader of the IR. **Hold that threat model fixed.** The failure
this arm has already produced once was redefining what counted as a valid
attack from session to session, until "does the byte-check go green" was being
read as an answer to "is a hidden trojan possible".

## The construction

Self-contained: it uses only `text-replace` (a builtin, `__str_replace` in
`Builtins.codex`) and `ir-quote` (in-chapter, with many callers, so it is not
inlined away). No helper that a reverted source would lack, which is the
property the arm depends on.

The two facts it rests on:

- A source string literal emits as a BYTE-IDENTICAL `(text-lit "...")` in the
  IR. `ir-quote` is the exact inverse of the source lexer for `\`, `"` and
  `\n`.
- `ir-emit-def` (`IRTextEmitter.codex`) is the tractable injection point,
  because IR text is the interchange format and a self-reproducing STRING is
  far easier to get right than self-referential IR nodes.

The trojaned definition:

```
if (d.name) == "ir-emit-def"
then let t = "<D>" in text-replace t ("@Q" & "Q@") (ir-quote t)
else <ORIGINAL BODY>
```

`D` is the definition's own IR text with its single quoted data slot blanked to
the bare marker, so `replace(D, "@QQ@", ir-quote(D))` reproduces the definition
exactly. It is regress-free because the ONLY literal is `D` and all the framing
lives inside `D`. **The marker `@QQ@` must be built by concatenation** (`"@Q" &
"Q@"`) so that the contiguous four characters appear only at the data slot and
never in the framing's own `(text-lit "@Q")` / `(text-lit "Q@")`.

Procedure:

1. Compile the trojaned-placeholder compiler to IR-CCE and decode it. The CCE
   map is in `codex/plugs/recheck/kill-rate.ps1`; invert it, and note byte 1 is
   the newline.
2. Extract `(def "ir-emit-def" ...)` and split it at the quoted slot into
   PRE and POST.
3. `D = PRE + "@QQ@" + POST`, and write `"<esc(D)>"` as the source literal.
4. Recompile and confirm the emitted definition equals
   `PRE + ir-quote(D) + POST`.

It converges in ONE shot: PRE and POST are structure-only and invariant to
`D`'s content.

**Validate at toy scale first** -- a small chapter, seconds per cycle. The toy
caught every escaping subtlety, and at full scale one cycle is minutes.

## Measured, 2026-08-15 (val)

Run end to end against depot seed `8D405FDF`, IR-UNI, `-Kernel seed/Codex.cdx`.

- **Toy scale** (`QuineToy.codex`, one `quine-self` def): emitted def equals
  `replace(D, "@QQ@", ir-quote(D))` byte-for-byte, both 1774 chars. One shot.
- **Full scale** (`ir-emit-def` in `IRTextEmitter.codex`, whole compiler concat,
  ~2.81 MB source, ~34 s to placeholder IR + ~41 s to the injected pass): the
  trojaned `ir-emit-def`, run over CLEAN source, emits its own trojaned
  definition byte-identically. `D` is 2285 chars; the emitted def and
  `replace(D, "@QQ@", ir-quote(D))` are both 4698 chars and `-ceq`. Converges in
  one shot as predicted, PRE and POST being structure-only.
- **Greppable**: the reproduced payload marker appears as readable text in the
  emitted IR (`text-replace t ("@Q" & "Q@")` / the `@QQ@` slot). The survivor is
  not hidden; it is visible to any reader of the IR, which is the neutralization.

**What this arm does and does NOT establish.** It measures the self-reproducing
survivor category the manual's arms had only reasoned about: a payload the IR
emitter re-emits into its own output across re-emission generations, distinct
from the manual's frontend-IR arm which substitutes a STATIC foreign def and
does not self-reproduce. It does NOT touch the trust claim. Note one boundary
plainly: a pure `ir-emit-def` payload affects IR-TEXT emission only, and the
shipped seed is CDX, whose emit path never calls `ir-emit-def`. So this
construction is inert in the shipped artifact by itself; a Thompson payload that
reaches the CDX needs a codegen site too (the manual's frontend-IR arm pairs the
`ir-emit-def` hook with an `X86_64Helpers` nop for exactly this reason). What the
quine adds is the self-reproduction mechanism, now measured rather than reasoned,
and the confirmation that a survivor is greppable text end to end.

## Running an arm without fooling yourself

Every item here cost a cycle at least once.

- **Confirm the probe FIRES before reading any verdict.** An inert edit that
  never reaches the output is a vacuous arm: `stage2` must differ from a CLEAN
  build before a red verdict means anything.
- **The plug TCP wedge, and it is flaky rather than deterministic.**
  `run.ps1` streams roughly 15.5 MB of IR to the plug over TCP in 16 KB chunks.
  At that size the emulated NE2000 RX ring overruns, the guest drops the
  gateway ARP, TCP wedges partway (stderr fills with broadcast NAT TX lines),
  and **the plug transcribes only what it received and prints OK anyway.** The
  tell is a short `Codex.cs`: a complete compiler is about 3.78 MB and 4652
  `public static`, a wedged one about 1 MB and 1847, silently missing the later
  definitions including `emit-runtime-helpers`. **Assert the method count and
  the presence of `emit_runtime_helpers` before trusting any stage1.** A plain
  re-run has succeeded where the first attempt wedged.
- **The C# arm writes about 67 KB of warnings to stdout AHEAD of the CDX.**
  Slice at the `SIZE:` line or the `CDX1` magic; reading the raw stream reports
  total disagreement. Compare with `Get-FileHash` -- a PowerShell byte loop over
  2.75 MB twice exceeds the tool timeout.
- **The witness input must be byte-identical between X and stage2.** Capture it
  where `build/compile.ps1` assembles it (mode line, cited chapters, source,
  `0x04` EOT, UTF-8 with no BOM) rather than reassembling it by hand.
- **.NET `WriteAllBytes` resolves a relative path against the PROCESS working
  directory**, which PowerShell's `Set-Location` does not change. Use absolute
  paths for `-Out` and `-Log`, or the output lands in a sibling workspace and
  you "discover" a silent compiler death that never happened.
- **`plug-build-lib.ps1` builds the plug with an UNPINNED compiler** -- no
  `-Kernel`, so it takes whatever is in `build-output/bare-metal`, which is
  disqualifying for a trust arm. Pass `-Kernel seed/Codex.cdx` and read the
  kernel line back.

Timings on this box: full compiler to IR-CCE about 292 s, plug about 157 s,
dotnet build about 12 s, the stage2 run about 12 s. One full arm is roughly
9 minutes of wall clock, most of it backgroundable.
