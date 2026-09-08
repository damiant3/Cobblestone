# The Shadow That Measured Past Itself

*The account behind L-THRESHOLD and L-DILUTE. val's GPU shadow campaign,
2026-08-12 and 2026-08-13; written down here 2026-09-07 (fester) because the
LESSONS rows were their only account and a row cannot carry what a lesson cost.
Both lessons are from the same campaign and are two halves of one mistake:
measuring the wrong quantity and then believing the number.*

## A zero that was measured one unit past its subject (L-THRESHOLD)

A shadow-mask threshold of **18** was run against a luminance drop of **16.5**.
It returned **0 pixels** over the exact face carrying the defect, and that zero
was published as proof the artifact was pre-existing.

Nothing about a zero announces that it was measured one unit past its subject.
The instrument was working, the arithmetic was right, and the answer was
useless: a threshold set just above the effect returns a null that reads as an
all-clear, and reads that way to everyone downstream who sees only the verdict.

A **colour histogram settled the same question in one step**, because the pixels
were exactly the colour the shadow path substitutes. That is the difference
between the two instruments: a threshold answers *is this different ENOUGH*, and
a census of exact values answers *is this THE THING*.

**When you have a candidate mechanism, count its signature rather than
thresholding its magnitude.** A magnitude test needs you to have guessed the
magnitude; a signature test does not.

## A lever that looked wrong because the metric was mostly something else (L-DILUTE)

Sweeping a shadow bias 6 / 16 / 40 / 100 moved whole-sphere roughness
**442 -> 376 -> 365 -> 365**. Read straight, the lever stops working almost
immediately and bias was nearly dismissed as the wrong mechanism.

It was not. **The coarse facet edges were 365 of that 442.** The metric was
dominated by something the lever does not touch, so the part that WAS moving --
all of it, cleanly -- was hidden inside a number that mostly measured a
different thing.

Recast as a **difference against a shadows-OFF control**, the same sweep read
**350 -> 0 -> 0 -> 0**. Same data, same lever, and now the effect is total.

**When a lever barely moves your metric, ask what fraction of the metric your
subject actually is before concluding the lever is wrong.** A difference against
a control that REMOVES the subject beats an absolute count over a region,
because it subtracts everything you were not asking about.

## Why the two belong together

Both failures produce a confident number that is not about the thing under test.
The threshold produced a zero that was about the threshold; the sweep produced a
plateau that was about the facet edges. In each case the repair was not a better
statistic but a different QUANTITY: count the signature, or difference against a
control that removes the subject. Neither needed new data.
