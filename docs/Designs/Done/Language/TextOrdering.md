# Text Ordering -- collation is not encoding

*Status: section 1 SHIPPED 2026-07-27 (blu) -- the refusal and the emitter
guard are in, with both directions fired. Sections 3-4, the `Collate`
chapter, remain open and are val's. Section 2 needs only the one prose
sentence, which travels with the Collate work.*

*Originally: operator refusal RULED 2026-07-27 -- Damian: "< > make no
sense in a text or character comparison." The refusal covers Text operands
and character operands both. Answers the two questions raised in val's
workplan the same day.*

## What shipped, and one deviation

`CDX2089 TextOrderingBanned` at CHECK, `CDX9012 OrderCompareOnText` in the
emitter behind it. Pins: `codex/test/errors/text-order-refused` and
`char-order-refused` (two files, not one -- a refusal with two sufficient
causes cannot be attributed to either, so a single test naming CDX2089
would pass with half the change reverted), plus
`codex/test/ops/text-order-allowed` as the positive control that equality
and `text-compare` were not caught by the same net.

**That deviation is closed (2026-07-28).** The hint shipped naming only
`text-compare` and `char-code`, because `Collate` did not exist yet and a
diagnostic pointing at a function nobody can call is a claim with no runner.
`Collate` landed, so the message now names **`text-collate`** rather than
`collate-key`: both exist, and `text-collate a b` is the one call a reader
of the diagnostic actually wants, where `collate-key` is the key-extraction
primitive underneath it. All three sites carry the same wording -- the
CHECK hint, the CDX9012 emitter guard behind it, and the `CdxCodes`
registry row.

Fact 1 was re-derived rather than quoted, and the first probe of it was
worthless: comparing two Text CONSTANTS answers `a<b` True, `b<a` False,
`a<a` False, which is what a working content comparison looks like. The
constants are laid out in source order, so a pointer compare and a content
compare agree by accident. The discriminating case is two separately built
copies of the SAME text: content says `a<a` is False and the compiler
answered **True**.

## The two measured facts this answers

1. `<` `>` `<=` `>=` on Text compare the operands as handed over, not by
   content: `"a" < "b"`, `"b" < "a"` and `"a" < "a"` all answer True
   (val, 2026-07-27, seed `EFC7FCD0`). Same silent-wrong class as the
   Real comparison inversion (main 10724).
2. `text-compare` orders by CCE code point, and CCE numbers letters by
   English frequency (e 13, t 14, a 15, d 22, c 24, b 32, z 38), so
   eight of thirteen measured letter pairs disagree with alphabetical
   order. That is correct for `SkipListText` and `RankedTextSet`, which
   need a consistent total order and nothing more, and wrong for a
   column a human reads.

## The principle

**Encoding order and collation are different things, and the language
must not let one impersonate the other.** Every ASCII-legacy language
fell into this because ASCII happens to be alphabetical for a single
case of a single script, so `strcmp` masquerades as collation until it
meets `Z < a` or an accent. CCE cannot even pretend -- its order is
frequency, visibly not the alphabet -- which is an advantage: Codex can
make the distinction explicit instead of inheriting the conflation.

Three orders exist and each gets its own name:

| order | what it is for | where it lives |
|---|---|---|
| identity (`==` `/=`) | same content or not | unchanged, structural |
| encoding order (`text-compare`) | a fast, consistent total order for data structures | unchanged; its prose must say plainly it is code-point order, not the alphabet |
| collation (`collate-key`) | the order a human reads | new, explicit, never implicit |

## 1. The ordering operators on Text and Char are REFUSED

`<` `>` `<=` `>=` on Text operands, and on character operands, become a
compile error at CHECK, with a hint naming both explicit choices:
`text-compare` for code-point order, `collate-key` for human order. A
character's order is its CCE code point, which is the same
frequency-not-alphabet order one letter at a time; code that genuinely
wants code-point arithmetic writes `char-code c` and compares the
Integer, which says what it is doing. Precedent: `==` on Real is CDX2085
because the obvious reading misleads; Text ordering is the same shape --
there is no single meaning to lower to, and any silent choice either
bakes encoding order into an operator (whose answers then look wrong to
every human reader: `"d" < "b"` is True in CCE) or smuggles a collation
table into codegen.

The emitter also gets a guard in the CDX9010 style: a TextTy operand
reaching the ordering comparator halts the build rather than emitting
the pointer compare. The checker refusal is the fix; the guard is what
keeps the current defect class from being reintroduced silently by a
future dispatch path, which is exactly how CDX9010 caught `unit Real`.

This fixes the live defect by making it unreachable. `DataTable` line
119 was the only Text ordering in the tree and val already fixed it;
`Sort.codex` is comparator-driven and was never exposed.

## 2. `text-compare` stays, honestly labelled

It is the structure order: total, fast, stable, and NOT alphabetical.
`SkipListText` and `RankedTextSet` keep it. Its prose gains one
sentence saying it orders by CCE code point and naming `collate-key`
for human-facing order. No rename -- callers are load-bearing and the
name does not claim alphabet.

## 3. Collation is a sort key, not a comparator

New foreword chapter `Collate`:

- `collate-key : Text -> Text` -- a strxfrm-style sort key such that
  `text-compare (collate-key a) (collate-key b)` IS collation order on
  `a` and `b`. Key extraction happens once per element, so every
  existing comparator-driven consumer (`sort-by`, `data-table-sort`)
  composes with zero new plumbing and no per-comparison table lookups.
- `text-collate : Text, Text -> Integer` -- convenience wrapper over
  the keys for one-off comparisons.
- The rank source is a static table indexed by CCE code point, built
  over the tier tables ONCE (a flat buffer, O(1) per character) -- not
  `to-unicode`, which re-materialises a 128-element list constant per
  call and, more to the point, would substitute Unicode code-point
  order for collation, which is the same conflation one encoding over.
- Weights, simple and documented: primary = script, then base letter
  in alphabetical order (accents fold to their base), digits before
  letters in numeric order; secondary = accent; tertiary = case.
  Beyond the ranked tiers, order falls back to code point within
  script, and the prose says so.

## 4. Locale honesty

The default is "Codex collation": deterministic, locale-free, and
documented as an approximation -- NOT a claim of correctness for any
particular language (Swedish files o-umlaut after z; German files it
with o; both are right). If locale-tailored collation is ever needed,
it arrives as a parameter to `collate-key`, never as a change to the
default's answers. Encoding order for machines, collation for humans,
equality untouched.

## Consumers and pins

- `data-table-sort` (currently uncalled) becomes the first
  `collate-key` consumer.
- The refusal lands as `codex/test/errors/text-order-refused.failing`;
  collation gets vectors pinned in `codex/test/ops/` (the
  operator-correctness axis), including the pairs where CCE and the
  alphabet disagree -- `d` before `b` in encoding order, `b` before `d`
  in collation -- so a regression in either direction is visible.

## Lane routing

The operator refusal and emitter guard are the operator-correctness
lane (reek's CDX9010 pattern). The `Collate` chapter is foreword
library work (val raised it and owns the consumer). The pins enter the
battery through the ops axis (red). Nothing here needs a seed until
the checker refusal lands.
