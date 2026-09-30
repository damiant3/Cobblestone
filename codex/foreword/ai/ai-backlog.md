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
`codex/test/apps/clip-bpe-forge`, one of which is Latin-1, Cyrillic and curly
quotes. Four differences remain for text outside that line, each ungraded:
of ftfy's repairs only quote uncurling is made, so NFD accents (a macOS
paste), HTML entities, ligatures, fullwidth Latin, mojibake and control
characters all tokenize differently from Forge; a code point CCE cannot
carry is dropped when the prompt becomes CCE Text, before the tokenizer sees
it, where Forge emits its byte tokens; letters, digits and lowercasing above
ASCII follow a range table covering Latin through Hangul, not Unicode's full
categories (Vietnamese in U+1E00..U+1EFF is not a letter); a word-final capital sigma lowers to U+03C3 where Python
gives U+03C2. Grade each with a line in that test's oracle before changing
the table.
