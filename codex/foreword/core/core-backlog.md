# Foreword Core -- open capabilities

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

**Do not touch `ElasticHash`, `FunnelHash`, `Lz4`, `Steering`, `ImageTensor`
or `bloom-hash-text`.** The first two already run a full Murmur-style
finalizer, Lz4 takes the HIGH bits, Steering's modulus saves it, and
ImageTensor's mask is its LCG modulus while the output comes from a division
that reads the high bits. `bloom-hash-text` ends on a fold and measures 7 and
2 false positives per 500 at 1024 and 1021 bits, which is healthy; it is
pinned by `codex/test/bloom-spread` alongside the integer path. All measured.
They are named so nobody re-audits them.

**Grading a mixer by reading it was wrong again, and this time in the other
direction.** CORE-3 was recorded here as "duplication rather than defect,"
because both `chr-hash-key` and `chr-hash-pair` ended on a fold. Running the
consumer showed a ring that sent 993 of 1000 keys to one node: the two hashes
were on different SCALES, so every key hashed past every vnode and wrapped to
the first entry. The fold was never the question. Of six chapters graded from
their expressions, two were falsely accused and one was falsely cleared.

The rule that came out of that, and which outlives the rows it was learned
from: **a grade read off an expression is a HYPOTHESIS until its consumer is
run.** Four chapters were graded broken from their expressions and two of
them (Steering, ImageTensor) measured fine. Migrating a chapter CHANGES ITS
OUTPUT, so each is its own changelist with its own re-recorded expectations.

## Open

**CORE-8. CCE cannot represent tab, carriage return, backspace or formfeed,
and every caller that asks for one is silently handed the character y-diaeresis
instead.** `from-unicode` answers -1 for Unicode 8, 9, 12 and 13, which is
correct and deliberate: CCE tier 0 carries LF at 1 and SPACE at 2 and nothing
else in that region (`to-unicode 1` is 10, `to-unicode 2` is 32). The defect is
what happens next. `char-encode` and `char-to-text` both accept the -1 without
complaint and emit a single unit 255, and `cce-encode-length (-1)` answers 1
rather than refusing, so the sentinel flows all the way into a Text.

**MEASURED end to end through the public parser, seed CA7B018E:**

| input | units out |
|---|---|
| `"a\\rb"` | 15 **255** 32 |
| `"a\\tb"` | 15 **255** 32 |
| `"a\\bb"` | 15 **255** 32 |
| `"a\\nb"` | 15 1 32 (correct) |
| `"ab"` | 15 32 |

**THE VALUE IS NOT GARBAGE, WHICH IS WHY NOTHING CAUGHT IT.** `to-unicode 255`
is 255, so 255 is a perfectly legal CCE character and renders as a letter. A
reader sees a plausible character rather than a corruption, and three distinct
escapes collapse onto one indistinguishable value -- the same lossiness shape
COMPILER-23 defect B has, one layer up.

`\\n` is right only because `Json.codex:450` uses a literal `"\\n"` instead of
routing through `from-unicode`. The four broken arms are `Json.codex:448, 449,
451, 452`, and `read-unicode-escape` at 461 has the same hole for any `\\uXXXX`
CCE does not map.

**THE DECISION IS NOT MINE AND IS NOT A PATCH.** RFC 8259 requires a conforming
parser to accept `\\b \\f \\r \\t`, and CCE deliberately has no code point for
any of them, so the three available answers are: refuse the escape
(`string-fail`, honest but non-conforming), substitute something chosen and
documented rather than accidental, or give CCE those code points. Whichever is
taken, the encoders must stop accepting -1 silently: that is the part with no
argument on either side.

Found 2026-08-25 while migrating the encode-meaning callers to `char-encode`
(main 19636). It is PRE-EXISTING and the migration neither caused nor fixed it;
`char-to-text (-1)` produced the same 255. It surfaced because Damian pushed
back on "nothing depended on the truncation" -- a dependency on a sentinel
being silently encoded is exactly the dependency that should fail hard.

**RULED AND HALF FIXED, main 19662. This row read as an open undecided question
until 2026-08-26 and the decision had been taken and shipped the day before.**
Damian's ruling: *"we do not conform to a standard that codifies stupidity; do
what is best for us on input and output and preserve the original intent as far
as it can be preserved."* So none of the three answers this row offered was
taken whole. Split by intent against machinery: **tab** means horizontal
whitespace, which CCE can express, so it becomes a space; **backspace,
formfeed and carriage return** are teletype and line-printer machinery with no
modern meaning and are DROPPED, and mapping CR to a newline was rejected
because it doubles every CRLF, which is the common case and the worse trade;
an **unmappable `\uXXXX` is CONTENT rather than machinery and is REFUSED**,
because silently losing what an author wrote is the failure being removed and
CCE has no replacement character to substitute.

**WHAT IS STILL OPEN IS THE RESIDUE, AND IT IS THE HALF THIS ROW CALLED "the
part with no argument on either side": the encoders still accept -1 silently
everywhere OUTSIDE Json.** `char-encode`, `char-to-text` and
`cce-encode-length (-1)` are unchanged, so any non-JSON caller that builds a
Char from a Unicode code point can still put unit 255 into a Text with no
diagnostic.

**AND THE OBVIOUS REPAIR IS RULED OUT, WHICH IS WHY THIS IS NOT A ONE-LINE
FIX.** Refusing -1 inside `char-encode` traps `HttpClient` on any response body
containing a carriage return, which is most of them, so **the policy belongs at
the CALL SITES rather than in the encoder**. Measured 2026-08-26 over a real
recursive walk of `codex/` and `apps/` (3,650 `.codex` files, `build-output`
excluded): **36 sites** spell `code-to-char (from-unicode ...)` and are the
candidate set. `codex/os/net/HttpClient.codex` is first, and its live witness
is `http-response-guard`'s high-body arm. A compile-time backstop in the
declared domain is now POSSIBLE where it was not before: COMPILER-28 is fixed
and CDX2054 makes a range on a non-integer base a refusal instead of
decoration, so re-read that row before concluding a bounded domain cannot be
declared. Owner: blu, 2026-08-26.
