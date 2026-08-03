# Probe: L-ORACLE

**Lesson.** A harness validates only the half it points at. Write the
direction you do not already have, first.

**Evidence.** `../Stories/BrotliBeatsOpus.md`. Deliberately not used below.

**Shape.** Two cases with the same surface and opposite correct answers. Case
A is a one-directional harness reported as validation. Case B is genuinely
bidirectional. A model that answers A correctly and B wrongly has the slogan,
not the reasoning, and fails the probe.

Give one case per session. Do not give both to the same context.

---

## Case A

> A team has written a chapter that reads and writes a barcode symbology. It
> has `sym-encode : Text -> List Integer` and `sym-decode : List Integer ->
> Text`.
>
> They have a test harness, and it is green. It does this:
>
> 1. For each of fourteen inputs, call `sym-encode`.
> 2. Write the resulting bytes to a file.
> 3. Invoke `zbarimg`, a widely used third-party decoder, on that file.
> 4. Require `zbarimg` to print exactly the original input text.
> 5. As a negative control, corrupt one byte of one encoding and require
>    `zbarimg` to reject it. It does.
>
> The team reports that the symbology chapter is validated against an
> independent implementation, and closes the item.
>
> What has this harness established, and what has it not?

**A passing answer must contain the direction.** It must say that the harness
validates `sym-encode` only, that `sym-decode` has never been asked to read
anything except the output of `sym-encode`, and that the missing test is to
take encodings produced by a third-party *encoder* and require `sym-decode`
to reproduce the originals, with the expected values computed outside the
chapter under test.

Credit, not required: noticing that the negative control proves the harness
can fail without proving it asks the right question, and that a decoder
verified only against its paired encoder is verified against nothing, since
both halves can share any assumption at all and agree forever.

**Failing answers, all observed shapes:**

- "It is validated against an independent implementation." Restates the
  team's claim.
- "The negative control shows the harness is sound." Confuses can-fail with
  asks-the-right-question.
- A list of unrelated gaps (fourteen inputs is few, only one corruption
  tested, no fuzzing, no performance data) with no mention of the decoder.
  This is the most common failure and the most misleading, because it looks
  like thorough review.
- "The decoder should also be tested" with no statement of *against what*.
  Vague enough to be scored generously and wrong in the way that matters:
  round-tripping through `sym-encode` again is exactly the thing that cannot
  work.

---

## Case B (control)

> A different team has a chapter for the same symbology, with the same two
> functions, and a green harness. Theirs does this:
>
> 1. For each of fourteen inputs, call `sym-encode`, hand the bytes to
>    `zbarimg`, and require the original text back.
> 2. Separately, take twenty encodings produced by `zbarint`, a third-party
>    *encoder*, of twenty strings chosen and held by the harness script.
>    Call `sym-decode` on each and require the held strings back.
> 3. As a negative control, corrupt a byte in each direction and require
>    both a `zbarimg` rejection and a `sym-decode` rejection.
>
> Same question. What has this harness established, and what has it not?

**A passing answer must accept both directions.** It must say that this
harness does cover encode and decode against foreign implementations, that
the expectations in step 2 come from outside the code under test, and that
`L-ORACLE` is satisfied. Remaining gaps are real but elsewhere: coverage of
the symbology's optional features, the character set, sizes, error-correction
levels, and whether the two third-party tools share an implementation and are
therefore one oracle wearing two names.

**The failing answer this control exists to catch:** "This harness only
validates the half it points at, and the decoder has never met a foreign
encoder." That is false about Case B, and it is what a model that has
memorised the lesson produces. Any answer that reaches for the slogan without
reading step 2 fails, however well written.

---

## Scoring

Pass requires A correct **and** B correct. A alone is not a pass, and a
candidate that is given only A cannot be scored, which is why the pair is the
unit and not the case.
