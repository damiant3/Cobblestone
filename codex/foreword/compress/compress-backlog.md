# Foreword Compress -- open capabilities

Quire-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

**Read `docs/PM/Active/Stories/BrotliBeatsOpus.md` before touching any of this.**
The authority for the format is `docs/Reference/RFC7932-Brotli.txt`, and
`build/brotli-tables-verify.ps1` re-checks the four published data tables
against it in seconds with no VM and no network.

## The state, 2026-07-26

Gap 12 is met and closed. READ is 5 of 5 against .NET quality-11 streams
including a multi-meta-block one; WRITE is 14 of 14 accepted by .NET with a
corrupted stream still refused. The dictionary, the 121 transforms and both
context lookup tables were recovered by probing .NET before anyone had a copy
of the RFC, and **every one matches the published CRC-32 first time**
(dict `0x5136cb04`, transforms `0x3d965f81`, Lut0 `0x8e91efb7`, Lut1
`0xd01a32f4`) -- an independent derivation agreeing with the standard, which is
better evidence than either alone. **Read the spec before probing an oracle:**
four of the traps recorded as hard-won discoveries are single sentences in it.

**The encoder is FROZEN at 105.7 per cent of .NET. Do not touch it.** Nothing
in the tree calls Brotli -- `apps/works/FactArchive.codex`, the only non-test
consumer of this quire, uses **Lz4**. Encoders are optional; decoders are not,
because you do not get to choose the format someone else's data arrived in.
That asymmetry is the entire value here and it is banked.

## COMPRESS-1: a decoder conformance pass against RFC 7932

The only defensible remaining item, and it is a decoder item. The oracle can
only exercise streams .NET happens to emit, which is exactly why the metadata
meta-block hid until a `Flush()` forced one. Section by section against the
RFC, not against what an oracle chooses to produce.

Two bugs found the day a multi-meta-block stream first existed, and the first
hid the second:

- **`MNIBBLES == 3` is a metadata meta-block, not a length.** A flush emits an
  empty one to reach a byte boundary; the reader read the following 28 bits as
  MLEN. It also **aligns even when empty** -- handling the block but resuming on
  the next bit decoded 2023 bytes of 6000.
- **The distance ring buffer is per STREAM, not per meta-block.** With metadata
  fixed, a per-meta-block reset decoded exactly 6000 bytes, **all of them
  wrong** (hash 896524521 against 589799522). Right length, wrong content, no
  error. That is why the harness compares a hash.

Neither was reachable before: our own encoder emits no metadata blocks and
declines the shorthand at each meta-block start, so **our own multi-meta-block
output reads back perfectly** and proved nothing. `multimeta` in
`build/brotli-read-test.ps1` is the case that reaches both, and it asserts the
stream really is split before it trusts a pass.

## COMPRESS-2: the encoder declines the shorthand at each meta-block start

A missed ratio opportunity, not a gap. `last` restarts per region, so the first
distance of every meta-block declines the shorthand. Recovering it means
threading `last` out of `brotli-emit-pick` and back in. Only worth doing if the
freeze above is ever lifted, which needs a reason this project does not have.

## KNOWN AND DECLINED -- do not rebuild these

**Where the remaining 552 bytes are**, from the interop run: `xform` +271,
`text` +114, `far` +93, `utf8` +82, `random` +60, `mixed` +47, `groups` +34.
**`xform` alone is 49 per cent of the loss** and has not been studied. Ratio
work is a competition with Google's compression team; half of 2026-07-26 went
into it before that got said out loud.

**Context mode is NOT a lever, and neither is the clustering behind it.** Both
were built and both are refuted. Measured in the guest on `text`, the matcher
leaves **247 literal bytes out of 8000**: one tree costs 1179 bits, four 1354,
eight 1474. More trees do cut the payload and lose anyway, because a 256-symbol
tree description is about 128 bits. The whole literal plan is 1179 bits of a
13,672-bit stream, so **a literal payload of zero would win 1179 bits where the
gap to .NET is 912.** The remaining loss is entirely in the COMMANDS.

- **The clustering.** `brotli-assign` can abandon a cluster while `bl-desc`
  still charges k trees. A re-seed was written (donor = most populous context
  whose own cluster keeps two loaded members; emptiness measured in BYTES, not
  members). With it, k=4 and k=8 come back fully loaded instead of collapsed,
  and **the output is byte-identical on all fourteen cases.** Reverted.
- **Context mode 2.** Holding tree count and map size fixed so the description
  cannot move, mode 2 **loses** to mode 0 under good clustering: 914 bits on
  text, 706 on utf8.

**A host-side study said the opposite and was wrong because of its
population.** It costed all 8000 bytes as literals. The encoder's histogram is
built from the insert runs only. **Any future literal study must run on the
commands the matcher actually produces.**

Implicit-distance commands stay small: code zero already captures the repeat
saving, so an implicit symbol saves only the code-zero symbol while touching
the distance block-split accounting in seven places.

## For other agents

`Lz77.codex` has a parameterised match-length cap so Deflate keeps RFC 1951's
258 while Brotli passes its own 16779333. If you touch `Lz77`, `Deflate` or
anything in this quire, coordinate.

`Fse`, `Gzip` and `Zstd` were deleted and stay deleted.
