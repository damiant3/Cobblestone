# Foreword AI -- open capabilities

Quire-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## The shared mixer

`Foreword chapter Random` supplies `mix-bits` and `rand-in-range` (CL 10493),
and its prose carries the whole account: why an unfolded multiply-add has no
usable low bits, why that is invisible to a balance check, and -- the part
that matters most -- **why it is only a defect where the consumer reads low
bits.** A remainder by a power of two or a bit-and is degenerate; a remainder
by a large non-power-of-two is often fine. Steering was recorded here as
broken on the strength of its expression, measured, and found fine.

**Read that prose before migrating anything, and measure the consumer rather
than grading the mixer.** `codex/test/mix-bits.codex` is the worked example
and keeps the old mixer as a live negative control.

**Do not touch `ElasticHash`, `FunnelHash`, `Lz4`, `Steering` or
`ImageTensor`.** The first two already run a full Murmur-style finalizer, Lz4
takes the HIGH bits, Steering's modulus saves it, and ImageTensor's mask is
its LCG modulus while the output comes from a division that reads the high
bits. All measured. They are named so nobody re-audits them.

**Every remaining grade is a HYPOTHESIS until its consumer is run.** Four
chapters were graded broken from their expressions; two of them (Steering,
ImageTensor) measured fine. Measure first, then migrate.
Migrating a chapter CHANGES ITS OUTPUT, so each is its own changelist with
its own re-recorded expectations.

## ClipBpe above ASCII

`ClipBpe` matches Forge's CLIPTokenizer on every line of
`codex/test/apps/clip-bpe-forge`, and its letter, digit and lowercase tables
agree with the oracle's `regex` classes and Python's `lower()` on every code
point CCE carries (2026-10-17). Forge runs ftfy 6.1.2 with its defaults, and of
its repairs only quote uncurling is made. Measured over the code points CCE
carries (its tier tables): ligature, width folding and NFD Latin accents are
unreachable, because CCE drops U+FB00.., fullwidth Latin and U+0300.. to `?`
before the tokenizer, as it drops Vietnamese (U+1E00..U+1EFF), where Forge emits
byte tokens. Reachable and not made: HTML entities, mojibake, C1 controls read
as cp1252 (U+0080..U+009F), control and formatting characters (U+206A..U+206F),
and NFC, which over carried text is 16 singleton and exclusion mappings
(U+2126 to U+03A9, U+0958..U+095F to base and nukta), 465 compositions (399
Hangul leading consonant and vowel, 66 kana, Arabic and Devanagari pairs) and
the canonical reordering of 159 carried combining marks. **Parked (root, 2026-10-17):** no boundary fits R-CCE without
touching every text, because a prompt reaches ClipBpe as CCE Text and the UTF-8
boundaries before it carry all input; the gain is prompts that arrive
decomposed, which our own input does not produce. Grade each repair with a line in
that test's oracle before changing the code.

## lora-resident exceeds the 60 s wall budget under load

`codex/test/apps/lora-resident` failed `wall budget exceeded` in two cite-gate
fan-outs on 2026-10-17 (red at `-Jobs 16`, root on seed A24CC2ED) and passes alone.
**Open:** a `lora-resident.wall` sized from a measured loaded run, as the four
GPU arms got (`flux-condition.wall` and its siblings).
