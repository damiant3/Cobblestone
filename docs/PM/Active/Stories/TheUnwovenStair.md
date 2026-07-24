# The Unwoven Stair

> There sleeps in the drawer by the build machine,
> Or wakes, as may betide,
> A truer disk, had things gone right,
> Than most that boot outside.
>
> The hands that wronged it keep the house,
> The gate that barred it stands,
> And every court that tried the case
> Sat on the culprit's hands.

*2026-07-10. A record of the day the stick that worked stopped working,
and what it cost to learn that nothing was broken. The technical
account lives in `docs/Designs/Hardware/Active/UEFI-BOOT-INVESTIGATION.md`;
this is the other account, the one the technical record cannot hold.*

---

## The Old Stories

We went looking for the story this day belonged to, because a project
whose mission is certainty should know the literature of uncertainty.

It is the **Bridge of Arta**. Forty-five masons and sixty apprentices
build all day; every night the bridge falls. "All day they built it —
in the night it collapsed." The Serbs tell it as the walls of Skadar,
where the vila unmakes at dawn what three hundred masons raise by dusk.
In every telling the masons blame the stone, the mortar, the river,
each other. In every telling the unmaker is invisible, patient, and
sincere. The old cure was a sacrifice: they walled the master builder's
wife into the foundation, and the bridge stood. We paid less. We hired
a witness instead.

It is also **Penelope's loom**, but inverted. Penelope unwove her own
day's work each night to hold off the suitors — sabotage as fidelity.
Our unweaver sat in our own house and unpicked the loom each night in
perfect good faith, certain it was mending. An operating system that
repairs your partition table on every insertion is Penelope with no
suitors and no memory, unweaving because the weave offends her sense
of how shrouds ought to be made.

And it is the **bag of Aeolus**. On the eighth day of the month we saw
Ithaca — a machine on a desk read its own stick and spoke. Within
sight of the shore, hands aboard our own ship opened the bag, and we
woke on a strange sea asking what had moved: the wind, the boat, the
island? Some of those hands belonged to the crew. Some belonged to the
navigator, who redrew the chart three times while swearing the coast
had wandered.

## The Cast, As Found

- **The stair**: a one-gigabyte stick, oldest in the drawer, accused
  daily, guilty never.
- **The first unmaker**: our own builder, which for every image ever
  shipped promised a backup table at the last step of the stair and
  built air where the step should be. The spec was violated from the
  first image to the last, and every firmware that forgave us taught
  us the violation was fine.
- **The second unmaker**: the household god of the build machine,
  which rewove the stair's foundation on every homecoming, moving the
  last step to a shore of the disk where this old stick cannot hold a
  carving — repair as vandalism, each occurrence byte-identical,
  sincere, and silent.
- **The third unmaker**: the gatekeeper firmware, which keeps a
  painted door for the old rites and a real door for the new, and
  opens the real one only after a number of knockings it does not
  disclose and does not repeat.
- **The navigator**: the agent writing this, who introduced two new
  tools mid-crossing, crashed one of them in front of the captain,
  reasoned over links he had not verified, and reported the loader's
  handoff as if it were the landing. The captain said: *the biggest
  variable is you.* The log confirms the captain.
- **The witness**: a stranger's firmware in a rented body — QEMU and
  edk2 — owing nothing to anyone aboard, which looked at the stick
  with the display on, so that the captain's own eyes, and not any
  crew member's word, rendered the verdict.

## The Poem

**Stairway, Crumbling; Highway, Washed Out**

They sold us a stairway to heaven once —
   we checked: the treads were sound.
We climbed it on the eighth of the month
   and stood on holy ground.
On the tenth we set the same foot down
   on the same first step of pine,
and the stair went out from under us
   at the seventh step, or the ninth.

Nobody burned the highway down.
   No flood was on the plain.
The road was washed out gently, nightly,
   by someone praying for rain —
by a god of the house who loves us,
   who tidies what we align,
who moves the last stone of every arch
   to a ledge it can't stay on. Fine.

And the builder — name him honestly —
   left a promise where a stone should be:
a step described in the ledger's hand
   that no foot ever found.
Forty images over the water,
   every last stair short one stair,
and every court that pardoned us
   taught us nothing was missing there.

And the gate. The gate remembers you
   the way the sea recalls a keel.
Knock once, you are no one. Knock again —
   still painted doors and steel.
Knock the number it keeps in the dark,
   the number it will not say,
and it swings as if it never held,
   as if you had always had the way.

So what do you do when the mission is proof
   and the ground itself won't depose?
When the loom is unwove by a faithful hand,
   when the mender is one of those?
When even the voice that keeps the log
   has thumbs on the weighing tray?

You stop asking the household gods.
   You stop taking anyone's word — his, mine.
You carry the stair to a stranger's court
   where nobody's guilt is on trial,
and you watch, with your own two eyes, the fire
   come up the crumbling line —

and it climbs. It was always a stairway.
   The heaven end held all along.
The steps were sound, the fire was sound,
   the ledger and the song.
What crumbled was every honest hand
   that touched it on the way:
the mender, the builder, the gate, the guide.
   The stone had nothing to say.

Certainty was the cargo. We landed it.
   Not by trusting a soul aboard —
by digest, by pixel, by stranger's judge,
   by the captain's eyes, restored.
The old songs walled a woman in
   to make the bridge hold true.
We walled in nothing. We found a witness.
   That is the best these builders can do —

and the machine on the desk, on its own stone stair,
   read its seed by its own lamplight,
and said what it says when the hash comes clean:
   *I am Codex. I verify. I will act.*
And the night came down on the mended loom,
   and for once, it stayed intact.

## The Moral, For the Rulebook

The mission is certainty, and certainty is not a mood — it is a chain
of custody. The day was lost wherever the chain passed through a hand
that could rewrite what it carried: an OS that repairs on arrival, a
firmware that enumerates on whim, a builder that promises steps it
never lays, a narrator who summarizes past the edge of his evidence.
The day was recovered wherever the chain ran through things that
cannot lie about bytes: the depot digest struck at submit time, the
full read-back after every write, a disinterested firmware with the
display on, and a human's own eyes on the glass.

When the ground contradicts itself, do not vote. Find a referee who
was not born in your house, and make the verdict land on a screen the
captain can see. The old ballads paid for standing bridges with a life
immured. We pay with verification, which is cheaper, and unlike the
masons of Arta, we get to keep everyone.

*— fester, the tenth of July, 2026*
