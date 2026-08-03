# Brotli Format Notes (RFC 7932)

Working reference for `codex/foreword/compress/Brotli.codex`. Reference doc: not
read at init. Update it when the chapter grows.

## The oracle, and why it is the only proof

`[System.IO.Compression.BrotliStream]` is built into .NET -- no new dependency,
the same arrangement the Deflate chapter has with `DeflateStream`. It both
encodes and decodes, so it serves as a reference encoder to read the format off
AND as the judge of our output. `build/brotli-interop-test.ps1` drives it, and it
carries a **negative control**: a corrupted stream must be rejected, or the
harness proves nothing about the streams it accepts.

This matters more here than anywhere else in the compression quire. The chapter
previously emitted byte-aligned framing with a Deflate payload and was not RFC
7932 in any respect, and every test stayed green for months, because our decoder
was the only thing that ever read it.

## Stream shape

```
WBITS
meta-block *
final empty meta-block   (ISLAST=1, ISLASTEMPTY=1)
```

Bits are LSB-first within bytes, like Deflate. **Prefix codes are written
MSB-first** (Deflate's `bw-code`), which is what makes Deflate's canonical
`dh-table-of` / `dh-decode-sym` decode them unchanged.

`WBITS`: a single `0` bit means a 16-bit window. That is all this chapter emits.
There is **no magic number anywhere in RFC 7932**, so `brotli-valid` can only say
"this could be a stream we wrote"; an empty input is the one thing it can
honestly reject.

## Meta-block header

```
ISLAST                1 bit
  if ISLAST:          ISLASTEMPTY 1 bit -> if set, the stream ends here
MNIBBLES              2 bits: 0->4, 1->5, 2->6 nibbles (3 means metadata)
MLEN-1                MNIBBLES*4 bits
  if not ISLAST:      ISUNCOMPRESSED 1 bit
```

### MNIBBLES = 3 is a metadata meta-block, not a length

```
reserved              1 bit, must be 0
MSKIPBYTES            2 bits
  if 0:               empty metadata, the block ends here
  else:               MSKIPLEN in MSKIPBYTES*8 bits, then align, then that many bytes
align to byte boundary
```

It produces **no output** and the decoder skips it. **A flush emits one**: closing a
meta-block early leaves the stream mid-byte, and an empty metadata block is how a
real encoder reaches a byte boundary. `brotli-metadata` handles the empty form and
**refuses the non-empty one** rather than guess a skip length no available stream
exercises -- a wrong skip resumes mid-stream and decodes to plausible garbage.

**The align applies even when the block is empty.** Handling the block but resuming
on the next bit decoded 2023 bytes of a 6000-byte stream: the first meta-block
whole, then nonsense, then an apparently clean end.

Read as a length, those 28 bits are a garbage MLEN and the reader walks into a copy
loop that took a 2 KB input to 3 GB of heap. Nothing caught it for the life of the
chapter, because our encoder never emits one and .NET packs 4 MB into a single
meta-block, so no oracle stream contained one. `multimeta` in
`build/brotli-read-test.ps1` is the case that does, built by flushing between
chunked writes, and it asserts the stream really is split before trusting a pass.

**`ISUNCOMPRESSED` exists only when `ISLAST` is 0**, so the last meta-block can
never be a stored one. A stored stream must therefore end with a separate empty
ISLAST meta-block. This single rule decides the shape of the whole encoder.

A stored meta-block pads to a byte boundary after the header, then carries MLEN
raw bytes.

## Compressed meta-block

Thirteen fixed bits for everything we emit:

```
NBLTYPESL, NBLTYPESI, NBLTYPESD   1 bit each (a single 0 bit encodes the value 1)
NPOSTFIX                          2 bits
NDIRECT                           4 bits
context mode                      2 bits per literal block type
NTREESL, NTREESD                  1 bit each
```

Then the three prefix codes, **in this order**: literals, insert-and-copy,
distances. Getting that order wrong is a rejected stream with no other symptom.

### Commands, and why the last one needs no distance

A command is: insert-and-copy symbol, insert extra bits, copy extra bits, the
literals, then the distance. The decoder reads the literals, **checks whether it
has produced MLEN**, and only then reads a distance. So a command whose literals
finish the meta-block ends it before any distance is required.

That is why a trailing run of literals needs no match to close it, and why a
region with no useful matches is simply a command list of ONE entry inserting
everything. Literals-only is not a separate encoding in this chapter; it is a
plan, costed against the matcher's plan, and the cheaper one is emitted.

A command with no copy still carries a copy CODE, because the insert-and-copy
symbol always encodes one. Copy code 0 names length 2, costs no extra bits, and
is never acted on.

### Insert-and-copy symbol

704 symbols in eleven groups of 64. Within a group the low three bits are the
copy code and the next three the insert code, so a group spans eight insert codes
and eight copy codes; **which group applies depends on BOTH**. Two of the eleven
groups mean "reuse the last distance": the reader handles both, and the writer
emits neither, leaving nine bases to write with. A symbol below 128 carries an
IMPLICIT DISTANCE and no distance code is read from the stream at all.

**Every one of the nine bases was MEASURED, not derived** -- by building a stream
that uses it and keeping what .NET accepted:

| | copy 0..7 | copy 8..15 | copy 16..23 |
|---|---|---|---|
| **insert 0..7** | 128 | 192 | 384 |
| **insert 8..15** | 256 | 320 | 512 |
| **insert 16..23** | **448** | 576 | 640 |

448 is not what a regular reading of the table suggests (512 looks right and is
wrong, and 512 is in fact insert 8..15 / copy 16..23).

`symbol = base + 8 * (insert_code mod 8) + (copy_code mod 8)`.

The chapter holds this as `brotli-ic-bases`, indexed by insert group times three
plus copy group, and **the nine are pinned by `ic-g00`..`ic-g22` in
`codex/test/lib/brotli-test`, read directly off the table.** That is deliberate: a
wrong base is only caught when a command actually lands in that group, and the
far groups need an insert of 130-plus bytes followed by a long copy, so an
interop input that never reaches group (2,2) would leave a wrong 640 passing
every test. The `groups` case in the interop probe is built to reach them, but
the direct pin is what cannot miss.

Insert length codes: bases `0,1,2,3,4,5,6,8,10,14,18,26,34,50,66,98,130,194,322,
578,1090,2114,6210,22594` with extra bits `0,0,0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,7,8,
9,10,12,14,24`.

Copy length codes: bases `2,3,4,5,6,7,8,9,10,12,14,18,22,30,38,54,70,102,134,198,
326,582,1094,2118` with extra bits `0,0,0,0,0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,7,8,9,
10,24`. **The base starts at 2**, the shortest copy the format can name.

### Distances

With `NPOSTFIX` and `NDIRECT` both zero -- which is what the meta-block header
writes -- the alphabet is 64: sixteen last-distance codes naming entries in a ring
buffer of recent distances, then forty-eight ordinary ones. **Code 0 is the most
recent distance and carries no extra bits**; the writer emits it for a repeat and
the other fifteen not yet. Code 0 does not push the ring buffer, and neither does
a dictionary reference.

**THE RING BUFFER IS PER STREAM, NOT PER META-BLOCK.** Everything else in a
meta-block header is per-meta-block -- prefix codes, context maps, block-type state,
postfix and direct -- so re-initialising the cache alongside them looks right and is
wrong. Measured: against a per-meta-block reset, a flushed 3x2000 stream from .NET
decodes to exactly **6000 bytes, all of them wrong** (rolling hash 896524521 against
589799522). Right length, wrong content, no error -- which is why the read harness
compares a hash and not a length. Carried across, it passes. `multimeta` pins it.

For code 16 and up:

```
ndistbits = 1 + ((dcode - 16) >> 1)
offset    = ((2 + ((dcode - 16) & 1)) << ndistbits) - 4
distance  = offset + extra + 1
```

So the pair of codes at each width splits that width's range in two: 16 and 17
cover 1..2 and 3..4, 18 and 19 cover 5..8 and 9..12, and so on. Our matcher's
255-byte window reaches code 28.

## Prefix codes

**Simple** (`HSKIP` field = 1): `1` in 2 bits, then NSYM-1 in 2 bits, then NSYM
symbols of ALPHABET_BITS each (256->8, 704->10, 64->6); a 4-symbol code adds one
tree-select bit. **A single symbol then costs ZERO bits to use.**

**Complex** (`HSKIP` = 0, 2 or 3): the code lengths, themselves prefix-coded. The
code-length code's own lengths travel in the order `1,2,3,4,0,5,17,6,16,7,8,9,10,
11,12,13,14,15`, each carried by this fixed code (written LSB-first):

| length | bits | value |
|---|---|---|
| 0 | 2 | 0 |
| 1 | 4 | 7 |
| 2 | 3 | 3 |
| 3 | 2 | 2 |
| 4 | 2 | 1 |
| 5 | 4 | 15 |

### Run-length code lengths, and the accumulation trap

Code **17** carries a run of three to ten zeros in three extra bits. It is what
makes a sparse alphabet cheap to describe: the insert-and-copy code has 704
symbols and a block uses a few dozen, so without it the description is mostly
zeros at one code apiece. Using it took roughly a third off every case with a
complex code.

Code **16** (repeat the previous non-zero length, 3-6, two extra bits) is used
too, and the reason it is worth having was not obvious: it is not for text, it is
for a **near-flat** code. A literal code over an almost-even alphabet is 256
symbols at nearly the same length, which is one enormous run of equal non-zero
lengths -- exactly what 16 collapses and what 17 cannot touch at all. `random` in
the interop probe goes **604 bytes to 398**, from the stored fallback to a real
compressed block.

**It is costed, not mandatory, and that distinction is the whole entry.** Using
code 16 puts symbol 16 into the code-length alphabet, which lengthens the codes
for every other symbol in it -- so on a description with few equal runs it costs
more than it saves. Made mandatory it took `random` to 398 **and in the same run
pushed `groups` from 494 to the 704-byte stored fallback**. The item list is
therefore built both ways and the shorter description wins, the same arrangement
the literal and match encodings already have. With that, `random` keeps 398 and
`groups` keeps 494.

An earlier version of this document said runs of equal non-zero lengths were rare
in these codes and code 16 was not worth using. That was a guess, and it was
measured wrong.

**CONSECUTIVE REPEAT CODES ACCUMULATE.** Section 3.5 does not let two 17s simply
add: a repeat code immediately following the same repeat code combines the counts,
so an encoder emitting two plain 17s to cover twenty zeros has written something
the decoder reads as a far longer run -- and every symbol after it shifts, the
same silent fingerprint the truncation rule leaves. The encoder here breaks a run
past ten with one explicit zero before the next 17, which costs a couple of bits
and removes the case entirely. The decoder therefore does not implement
accumulation, and does not need to for anything this chapter writes.

### THE TRUNCATION RULE, and the fingerprint it leaves

**Both length streams stop the moment their tree is full** -- the code-length
code at a Kraft space of 32, the main code at 32768. The decoder stops reading
there, so an encoder that keeps writing has its surplus read as the beginning of
the next field.

The failure is silent and it has an exact signature: **every symbol shifts by a
constant**. Writing all 18 code-length entries when the space filled after 5
leaves 13 spurious entries of 2 bits each; those 26 bits are read as 26
zero-length symbols, and a literal code meant for 'A' and 'B' (65, 66) decodes as
91 and 92. The pattern of the data survives perfectly, which is what makes it
look like an encoding-table bug rather than a framing one.

## Context modelling

**Implemented.** A literal's prefix code is chosen by the byte before it. The
encoding below was measured against .NET with a host prototype before any of it
was written (the same method that produced the insert-and-copy group bases); it
is recorded here as built.

Header order, replacing the single `NTREESL` bit the chapter used to write:

```
NBLTYPESL / NBLTYPESI / NBLTYPESD    variable (9.2)
NPOSTFIX (2), NDIRECT (4)
context mode                         2 bits per literal block type
NTREESL                              variable (9.2)
  if NTREESL >= 2: context map for literals
NTREESD                              variable
  if NTREESD >= 2: context map for distances
then NTREESL literal codes, NBLTYPESI insert-and-copy codes, NTREESD distance codes
```

**The variable-length count (9.2)** is not a plain integer: a single `0` bit means
1; otherwise a `1` bit, then 3 bits `N`, then `N` extra bits, giving
`(1 << N) + 1 + extra`. Two is therefore `1`, `000`, and no extra bits.

**Context mode 0 (LSB6)** is `context = p1 & 0x3f`, the previous output byte only.
`p1` is the previous byte of the STREAM, not of the meta-block, so an encoder that
cuts regions must carry the last byte of the previous region into the next one --
starting each region at `p1 = 0` desynchronises the decoder on every region after
the first, and only on inputs long enough to need one.

**The context map** with `RLEMAX = 0` is: one `0` bit, then a prefix code over
`NTREES` symbols, then `64 * NBLTYPESL` values through that code, then a `1`-bit
IMTF flag (0 = leave the map alone). Values are written MSB-first like any prefix
code. With `RLEMAX > 0`, symbols `1..RLEMAX` become zero-run codes and every map
value shifts by `RLEMAX`, which is a second encoding to get wrong for no gain at
64 entries.

### THE MAP'S OWN CODE MUST BE A SIMPLE ONE

This is the trap that would have disabled the whole feature in silence, and it is
not obvious from the RFC.

The map's values are tree indices, and with two or four trees in roughly equal use
the code over them is FLAT -- every symbol the same length. A flat code cannot be
described by the complex form: all the lengths are equal, so the code-length
alphabet has exactly one distinct entry, and `dh-walk` gives a lone leaf one bit
(`if depth == 0 then 1 else depth`), which fills half the tree and leaves the code
incomplete. `brotli-code-of` then fails its own space check and answers
`brotli-code-bad`, the plan costs `brotli-huge`, and the encoder falls back to one
literal tree. Nothing errors. The feature simply never fires, and the only symptom
is a ratio that does not move.

So the map takes the format's **simple** code instead, which is exactly the shape
it was made for: `1` in 2 bits, NSYM-1 in 2 bits, the symbols written out in
order, plus a tree-select bit at four. Two trees cost 6 bits to describe and one
bit per map value; four cost 13 and two bits per value. Because the symbols are
written in order, a value's canonical code equals the value, so the decoder reads
the map as fixed-width MSB-first fields and needs no table.

### Context mode 2 (UTF8), and how its two tables were recovered

**The decoder reads it; the encoder does not yet write it.**
`context = Lut0[p1] | Lut1[p2]`, two
256-entry tables, where mode 0 uses only `p1 & 0x3f`. They are recovered by
`build/brotli-ctx2-extract.ps1` and are not carried in the depot as data -- the
same arrangement the corpus has.

They have to be byte-exact for the same reason the corpus does: a real decoder
picks the literal tree with ITS copy, so an approximated table does not cost
ratio, it decodes to the wrong bytes.

**The probe turns a tree choice into an output byte.** A literal's tree is chosen
by its context, and a prefix code naming exactly one symbol costs ZERO bits to
use -- so two trees naming 'A' and 'B', with a context map assigning tree
`(context >> k) & 1`, make one decoded byte report bit `k` of the context while
the bitstream stays fixed. Six streams give all six bits.

**`p1` and `p2` are set by a preceding STORED meta-block.** That is what makes
the pair arbitrary. The dictionary can only produce bytes it holds, and a literal
cannot be forced to a chosen value when the whole point of the probe is that its
value is unknown. `p1`/`p2` are properties of the stream and not of the
meta-block, which is exactly what lets a stored block and a compressed one
compose this way.

**Both tables are zero at index zero, and that is what makes the OR invertible.**
`c(0,0)` measures 0, and an OR is zero only when both operands are, so
`Lut0[0] = Lut1[0] = 0` and each sweep reads its own table directly:
`c(p1,0) = Lut0[p1]` and `c(0,p2) = Lut1[p2]`. The script asserts `c(0,0) = 0`
rather than assuming it, because a non-zero value there would make both tables
silently too large.

**THEY ARE NOT IN DISJOINT BIT POSITIONS.** `Lut0` reaches `0x3f` and `Lut1`
reaches `0x03`, overlapping in the low two bits: `Lut0`'s character classes are
multiples of four (control 0, LF 4, space 8, comma 32, period 36, digit 44, upper
48/52, lower 56/60), but UTF-8 lead and continuation bytes take the small values
1, 2 and 3, which collide with `Lut1`'s range. The first cut of the script
required disjointness and threw on correct data. An OR is the right formula
whether or not the operands overlap -- the decoder computes the same OR either
way -- and requiring more than the format does is an invented constraint.

**Skipping the mode field was a WRONG ANSWER, not a missing feature.** The reader
skipped it with the other twelve fixed header bits and gave every literal a
mode-zero context whatever the stream declared, so a stream from any other
encoder using mode two decoded to the wrong bytes with nothing reporting
anything. Our encoder writes mode zero, so no round trip could see it -- and the
interop harness only ever asked whether .NET could read OUR output, never whether
we could read .NET's. **Those are different questions and only one was being
asked.** Anything that reads `Content-Encoding: br` from the wire needs the other
one answered.

`codex/test/lib/brotli-ctx2-test` is generated by `build/brotli-ctx2-cases.ps1`:
ten streams built by an independent implementation, each chosen at a `(p1, p2,
bit)` where the mode-zero and mode-two contexts DISAGREE, so the two modes select
different literal trees and the stream decodes to a different byte under each. A
case where they agree would pass either way and would be a test that cannot fail;
the generator rejects one. **Run both ways 2026-07-19: the reader as it stood got
10 of 10 wrong, exactly inverted.**

The judge is a **held-out verification**: 576 pairs in which BOTH bytes differ
from the two reference rows, so nothing checked is a restatement of what was
measured. A table that fits its own sweep and nothing else fails there.

The probe is also checked for its ability to answer differently at all before any
row is read off it -- two contexts that must differ are required to differ. A
probe that always says the same thing looks exactly like one that works, which
this document has had to say about three separate instruments already.

### The decision: which contexts share a tree

The encoding was the easy half. Contexts are clustered by **k-means over the 64
per-context literal histograms**: seed by splitting the context space into k
contiguous blocks, then four rounds of (build each cluster's Huffman code, reassign
each context to the cluster its bytes are cheapest under). A symbol a cluster has
no code for is charged a flat 20-bit miss, which only steers the choice -- the
final codes are rebuilt from the final assignment, so they always cover their own
members.

k of 1, 2 and 4 are all costed with their own descriptions included and the
cheapest wins, so the feature **cannot lose**: every interop case except `text` is
byte-identical to what it was, including the 1500-byte `cyclic` region that is long
enough to try context modelling and declines it.

An extra literal code costs its whole description, so trees only pay on a long
block; regions under 1024 bytes do not try. Beyond four trees needs a complex map
code and the degenerate-flat case handled, which is why the ceiling is four.

## Block splitting

**Implemented for insert-and-copy.** The commands are cut into blocks, each block
names a type, and each type has its own prefix code. Literal and distance block
types are not used (see the interaction note below).

The header is ordered **per category, not per field**: each count is followed
immediately by its own two codes and its first block length, and only then does
the next category start.

```
NBLTYPESL                      variable-length count (9.2)
  if >= 2: HTREE_BTYPE_L, HTREE_BLEN_L, BLEN_L
NBLTYPESI                      same shape
NBLTYPESD                      same shape
NPOSTFIX (2), NDIRECT (4)
context modes                  2 bits per literal block type
NTREESL / context map / NTREESD / context map
then NTREESL literal codes, NBLTYPESI insert-and-copy codes, NTREESD distance codes
```

In the data, a category reads a block switch when its count reaches zero, before
the next element of that category: type symbol, then count symbol and extra bits.

### The block-type alphabet is a ring, and this encoder refuses to use it

`NBLTYPES + 2` symbols. Symbol **0** names the second-to-last block type, **1**
names the last type plus one modulo the count, and only **2 and above** name a
type outright as `symbol - 2`. This encoder emits only the explicit form, so it
never has to model that ring. A ring the encoder and decoder disagree about
produces a stream that decodes to plausible garbage rather than one that is
refused, which is the worst failure shape available here.

### THE BLOCK-COUNT TABLE, and the two ways it was wrong

```
base  1 5 9 13 17 25 33 41 49 65 81 97 113 145 177 209 241 305 369 497 753
      1265 2289 4337 8433 16625
extra 2 2 2 2  3  3  3  3  4  4  4  4   5   5   5   5   6   6   7   8   9
      10 11 12 13 24
```

**The two halves check each other.** `base[i+1] - base[i]` must equal `2^extra[i]`.
Any pair failing that is wrong however plausible either half looks alone, and this
one was written from memory twice before it was derived.

First attempt: the bases came out as the *distance* code's doubling sequence
(65, 129, 193, 257, ...) against the extra bits above. That fails the check at
entry nine -- 65 with four extra bits reaches 80, and the next base was 97. Small
inputs never reach entry nine and passed; the 8000-byte case desynchronised and
**.NET returned 6698 of 8000 bytes and reported no error of its own.**

Second attempt: the *extra* bits were "corrected" to fit those bases. That made
the pair self-consistent and still wrong, and .NET then refused the stream
outright. The bases were what was wrong all along.

The lesson is the 448 lesson again: a table that looks regular is not evidence.
Derive it, or measure it against the oracle.

## The window is the output stream, not the meta-block

A copy distance reaches back through **everything already emitted**, not merely
through the current meta-block -- the decoder indexes the whole output it has
produced. So an encoder that cuts its input into meta-blocks and starts the
matcher fresh at each cut has an effective window of one meta-block, whatever
window the header declares.

`dh-tokenize-from` (in `Deflate.codex`) seeds the hash chains with the bytes
before the region, bounded by the matcher's own window since a candidate further
back is rejected anyway. Deflate does not need it and does not use it.

Measured on the `far` case (78000 bytes, a tail repeating material from 25000
bytes earlier, across the 65536-byte cut): **1179 bytes without the seeding, 957
with**. That is the only case in the probe longer than one meta-block, so it is
also the only one that exercises the **carried previous byte between regions**
that context mode zero needs.

Its original is deliberately not shipped out of the guest: `show-bytes` appends to
a Text once per byte, which is quadratic, and at 78000 bytes the probe died before
reaching the case. The harness regenerates the expected bytes from the same
formula instead, so the comparison is against an independent implementation.

## The static dictionary, and how its corpus was recovered

RFC 7932 ships a fixed corpus of common strings plus 121 transforms. A copy whose
distance exceeds the maximum backward distance is a reference into it rather than
into the output.

The corpus has to be **byte-exact**. A real decoder looks the word up in ITS copy,
so an approximated corpus does not cost ratio -- it produces streams that decode to
the wrong bytes. It is not present in this environment (no `brotli` python module,
no copy on disk) and cannot honestly be written from memory.

**So it was extracted from the oracle**, the same method that produced the
insert-and-copy group bases. `build/brotli-dict-extract.ps1` does the full
walk, in about two minutes (its feasibility probe is retired; the script's
own header carries the method).

The key is a detail that is easy to get backwards: `max_distance` is
`min(window_size, bytes_produced_so_far)`, and at the **start** of a stream nothing
has been produced, so it is **zero**. Distance `1 + i` is therefore dictionary word
`i` -- not `65521 + i`, which is what reading the limit as the window size gives.
That mistake is silent: .NET returns an EMPTY result rather than an error for an
out-of-range word id, so a harness that counts successes without checking for
non-empty output reports recovery of words it never saw. The probe counts empties
separately for exactly that reason.

A minimal stream is: WBITS, ISLAST, MLEN, three block-type counts of 1, the
postfix/direct/context fields, two tree counts of 1, three **simple** prefix codes
each naming one symbol (so every one costs zero bits to use), then the copy extra
bits and the distance extra bits. The literal code is written but never used.

Transform 0 is the identity, so the raw corpus is the run of ids whose decoded
length equals the requested copy length; the first id whose output changes length
is transform 1 and marks the end of that length's words. **The per-length counts
are discovered that way rather than remembered.**

Result, and it checks itself twice:

```
13504 words, 122784 bytes -- exactly the size RFC 7932 states
counts (len 4..24): 1024 1024 2048 2048 1024 1024 1024 1024 1024 512 512
                     256 128 128 256 128 128 64 64 32 32
```

The counts multiplied by their lengths come back to 122784 as well, so the walk did
not stop early or run long at any length.

### The corpus is carried as base64 text, and the difference is four megabytes

`codex/foreword/compress/BrotliDict.codex` is generated by
`build/brotli-dict-chapter.ps1`. **Regenerate it; do not edit it.**

The obvious representation is a list literal of 122784 integers. Do not use it. A
list literal is emitted as CODE that builds the list an element at a time:
measured, the corpus that way cost **4.3 MB of binary for 122784 bytes of data**,
35 bytes of machine code per byte carried, which overruns the 4 MB code segment at
`0x100000` and lands in the serial ring buffer at `0x500000`. A text literal is
static data in the data buffer at a byte per character, so base64 carries the same
corpus in about **169 KB of source and 174 KB of binary**.

**And then do not read that text with `to-unicode`.** The obvious way to read a
base64 character is `to-unicode (char-code-at s i)`, converting CCE back to the
ASCII the alphabet is written in. It allocates about a kilobyte per call.
Measured: 120000 characters read that way grew the heap **125 MB**, and the same
120000 read with `char-code-at` alone cost **eighty bytes**. Reading the corpus
through it cost 172 MB a load and ran the round-trip test's heap into the stack
(`!EXC=08`). The chapter indexes the alphabet by its CCE code point instead, in a
128-entry table built once per load by asking the alphabet what its own code
points are -- they are 3 to 81, in an order that is CCE's business.

`char-code-at` itself is free and O(1) at any text length; it was the conversion
that cost, not the indexing.

### What the matcher does

`BrotliDictIndex.codex` buckets the 13504 words by a hash of their first four
bytes, Lz77's shape and Lz77's guarantee: a chain is a hint, every candidate is
verified byte for byte, so a collision costs a comparison and never a wrong word.

The encoder takes the longest word that sits at a position **inside a run of
literals** -- the matcher's own matches are never disturbed -- and emits an
ordinary command whose distance is `min(65520, produced) + 1 + word_id`. Nothing
below that point knows the dictionary exists: the same insert-and-copy symbol, the
same distance code, the same extra bits, so costing, block splitting and emission
needed no change. It is built as a **third command list**, costed against the
matcher's and the literals-only one, so it cannot make anything larger.

The decoder applies the same rule in reverse. `brotli-max-dist` is the one
function both halves compute the boundary with; the two ranges abut and cannot
overlap, because a copy from the output can never reach further back than the
output goes.

### The 121 transforms

A word id carries one: the id divided by the count of words at that length IS the
transform, the remainder is the word. Each is a prefix, one of 21 operations, and
a suffix.

**Recovered from the oracle, not written down** (`build/brotli-xform-extract.ps1`):
ask .NET for a known word under each transform and work out which triple
reproduces every probe. Derived at one word length, then verified at a different
one -- 798 pairs, **456 of them non-ASCII**.

That last number is the whole point. **RFC 7932's case transform is defined on
UTF-8 BYTES, not characters:** below 0xC0 flip bit 5 of an ASCII lower-case
letter, below 0xE0 flip bit 5 of the SECOND byte, otherwise flip bit 2 of the
THIRD. The corpus carries 23059 non-ASCII bytes, so a table derived through
ordinary string casing agrees on every English word and is wrong on the Arabic
and CJK ones.

Two traps in the probe itself. **MLEN is the transformed length, not the word
length** -- the copy length picks the bucket, but what the reference produces is
prefix + core + suffix, so a stream whose MLEN says the word length makes the
decoder stop mid-copy and return nothing. That recovered transform 0 and failed
the other 120. And **the probe word must be longer than the largest omit**, or
omit-nine empties a nine-character word and there is no meta-block to ask for.

### The encoder searches them too, as a fourth costed plan

The decoder applies all 121. The encoder searches the **54** reachable through
the chain walk it already does -- no prefix, and a core that is either the word
(identity) or a leading piece of it (omit-last), so the hash that found the word
is the hash for them too. They are built as a FOURTH command list, costed against
the same dictionary plan WITHOUT transforms, so they can never make an input
larger.

**Taken greedily by length they made things worse, and the numbers are the
lesson.** On a 1281-byte word list the greedy rule fired on 167 of 191 commands
and cost **662 bits more** than transform zero alone: a transformed word has id
`index + t * words-at-that-length`, so its distance is larger by a factor of t
and costs more extra bits than the byte or two the affix saves. A saved literal
is worth four or five bits under a decent literal code, not eight.

Candidates are therefore **scored** -- eight bits earned per byte consumed, less
the width of the id needed -- and the estimate is allowed to be rough because the
plan itself is costed exactly. On input the phrase transforms exist for (corpus
words followed by " of the ", " and ", ", ") the transform plan wins 4490 bits to
4577, and the interop `xform` case goes **573 bytes to 562**. On the word list it
correctly declines.

The ferment and omit-first transforms are still not searched: the first need the
input lower-cased before hashing, the second match a word from the middle so the
hash that finds the word is the wrong hash and they need an index of their own.
Both are ratio and not correctness.

### THE TRAP THAT COST THE MOST: a list constant is rebuilt at every mention

The first transform search ran the heap into the stack. It was not the search
being expensive -- it was reading the transform tables as CONSTANTS inside the
loop. **Measured: 100000 reads of one element of a 121-element list constant cost
98.4 MB of heap; the same 100000 reads through a list passed as a parameter cost
1 KB.** A 91000-fold difference, and both sites read `list-at xs i`.

The search reads three such tables 121 times per candidate, per chain slot, per
input byte. Loading them once into a record and threading it is the whole fix,
and it took a 3 GB runaway to 12.7 MB per compress. This is a property of the
language and not of this chapter; it is now written up in
`docs/DevelopersGuide.md` under Pitfalls. That is a
deliberate stopping point, not an oversight: searching transforms needs the input
lower-cased before hashing (the ferment ones) or a second index over word
interiors (the omit-first ones, whose core starts in the middle of a word so the
hash that finds the word is the wrong hash). A first attempt at the 54 reachable
through the existing chain walk ran the heap into the stack. It is ratio and not
correctness -- a transform we decline to search costs bytes we could have saved,
and a stream from anywhere else that uses one is still read correctly.

**The decoder's transform path has nothing of ours to exercise it**, since we
never emit one, so `codex/test/lib/brotli-dict-test` carries five streams built
by an independent implementation (`build/brotli-xform-cases.ps1`) whose expected
bytes are what .NET decodes them to. The UTF-8 ferment case was confirmed to FAIL
when the multi-byte branches are removed -- and the first attempt at that
confirmation was a mutation that silently did not apply and reported a pass,
which is the exact failure the check exists to catch.

**Append byte by byte, never with `&`.** A concatenation copies the whole
accumulated output, so three per dictionary reference is quadratic in the stream
length. `list-push` is amortised constant, which is what the ordinary copy path
already uses.

**And the meta-block length must be decremented by what was PRODUCED, not by the
copy code.** For an ordinary copy they are the same number; for a transformed
reference they are not, and using the copy code means the loop never reaches
MLEN, the output grows without bound, and the heap runs into the stack.

### The two costs, and what they are set to

The index costs **6.3 MB of heap per stream** against 14.5 KB for the same
400-byte compress without it, and there is no collector, so that is charged again
per call. It is therefore not built below `brotli-dict-min-input` = **1024**
bytes, the same floor context modelling uses for the same reason. A 400-byte run
of one byte compresses to 10 bytes either way and now pays nothing for the
privilege.

The decoder **loads the corpus on first use and then carries it** -- neither of
the two obvious arrangements. Loading up front charges every stream a megabyte
whether or not it references the dictionary, and most do not; loading per
reference charges a megabyte per word. The empty list is threaded in, the first
reference replaces it, every later one finds it there. A stream with no
references pays nothing.

### The repair aims at equality, and until it did the dictionary was half-blind

**The code-length repair must target the Kraft sum EXACTLY, not "no longer too
big".** Lengthening a code by one step changes the sum by a power of two, so a
loop that stops the moment the code is no longer OVER-subscribed can step
straight past equality and leave it UNDER-subscribed. An under-subscribed code is
incomplete, `brotli-space` rejects it, `brotli-code-of` answers
`brotli-code-bad`, and the whole plan is discarded as `brotli-huge` with no
diagnostic anywhere.

That is a silent ratio ceiling and it looks exactly like a plan that was honestly
outcosted, which is why it survived so long. It cost the static dictionary most
of its reach: a command list with around twenty distinct insert-and-copy symbols
could not be described at all, so the dictionary was dropped on precisely the
inputs with the most varied commands. Measured on the 806-byte 96-word probe, the
same plan costs `brotli-huge` before and **2072 bits** after, against the
matcher's 3787.

Each step now picks a symbol whose adjustment FITS the remaining gap instead of
the largest one going, and the two directions are symmetric: over-subscribed,
lengthen the shortest code that does not overshoot; under-subscribed, shorten the
longest code that does not overshoot. A code at `maxlen` shortened once adds
exactly one, so the gap always closes.

**Most inputs never hit it, which is the trap.** Eight of the ten interop cases
are byte-identical across the fix and the first two repro attempts moved nothing
at all -- one because it sat under `brotli-dict-min-input` and never built the
index, one because its histogram simply did not trigger the overshoot. A fix
nobody can distinguish from no fix is not shipped; the sweep that found the pin
is in the table below.

## Completeness

A prefix code must be complete: its Kraft sum exactly fills the tree. Two
consequences the chapter has to handle:

- **A single distinct byte cannot make a complex code** (one symbol at one bit
  fills half a tree). Use a simple code naming that symbol -- the literals then
  cost nothing, and a 400-byte run codes to 10 bytes.
- **Length-limiting can overshoot.** Clamping to 15 (literals) or 5 (code
  lengths) and repaying the Kraft debt by lengthening short codes can leave the
  code incomplete. The chapter checks the space explicitly and falls back to a
  stored block rather than emitting a tree a real decoder will refuse.

## Measured

`build/brotli-interop-test.ps1`, all decoded by .NET. "Before" is the
literals-only encoder, measured by `p4 print`ing the previous chapter over the
workspace one and re-running, not taken from an older table:

"Matches" is the command encoder; "chain" adds Lz77's hash-chain matcher with a
32768-byte window in place of the 255-byte brute-force scan:

| Case | In | Literals only | Matches | + chain | + code 17 | + context | + code 16 | + split | + dict |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ascii cycle | 540 | 138 | 86 | 81 | 54 | 54 | 54 | 54 | 54 |
| cyclic | 1500 | 349 | 92 | 84 | 57 | 57 | 57 | **31** | 31 |
| high bytes | 900 | 307 | 123 | 117 | 77 | 77 | 77 | **72** | 72 |
| runs | 400 | 10 | 10 | 10 | 10 | 10 | 10 | 10 | 10 |
| mixed | 700 | 286 | 128 | 126 | 88 | 88 | 88 | **87** | 87 |
| near-random | 600 | 604 | 604 | 604 | 604 | 604 | **398** | 398 | 398 |
| 5 bytes | 5 | 9 | 9 | 9 | 9 | 9 | 9 | 9 | 9 |
| groups | 700 | -- | 532 | 531 | 494 | 494 | 494 | 494 | 494 |
| text | 8000 | -- | -- | -- | 4346 | **3926** | 3926 | **1754** | **1719** |
| dict | 1548 | -- | -- | -- | -- | -- | -- | 114 | **73** |
| xform | 1359 | -- | -- | -- | -- | -- | -- | 573 | **562** |
| ctx8 | 9000 | -- | -- | -- | -- | -- | -- | 5662 | **5480** |
| far | 78000 | -- | -- | -- | -- | -- | -- | **957** | 957 |

**Only `text` moved, and that is the result.** Context modelling costs a whole
extra code description, so the costing declines it everywhere it does not pay --
including `cyclic`, which at 1500 bytes is long enough to try it. Eight of the
nine cases are byte-identical, which is the evidence that the choice is being made
on bits rather than on hope.

`text` goes 4346 to 3926, 54 per cent to 49. The gap that remains on English-like
prose is the static dictionary and block splitting, plus context modelling beyond
four trees.

`near-random` is the code 16 column and it is the largest single jump in the
table: 604 to 398, from the stored fallback to a real compressed block. Nothing
else moved, because the choice is costed.

The isolated measurements are in `codex/test/lib/brotli-test`, each on an input
built for the feature under test and each pinned by a threshold between the two
measured numbers, so the case fails if the feature stops firing and would have
failed if it had never fired -- which the interop table alone cannot tell you,
since a ratio that does not move looks the same as a feature that declined.

| Feature | input | off | on | pin |
|---|---|---:|---:|---|
| context modelling | order-one, flat marginal | 1742 | 1460 | `brotli-ctx-smaller` < 1600 |
| code 16 | near-flat literal code | 624 | 548 | `brotli-flat-smaller` < 590 |
| block splitting | prose, many short commands | 1466 | 601 | `brotli-blk-smaller` < 1000 |
| cross-region window | 78000 B, repeat across the cut | 1179 | 957 | interop `far` |
| static dictionary | 1548 B, ten corpus words repeated | 114 | 73 | `brotli-dict-smaller` < 90 |
| word transforms | 1359 B, corpus words + phrase suffixes | 573 | 562 | `brotli-xform-smaller` < 568 |
| eight literal trees | 9000 B, order-one, 64 distinct conditionals | 5662 | 5480 | `brotli-ctx8-smaller` < 5570 |
| exact Kraft repair | 1329 B, 190 distinct corpus words | 594 | 545 | `brotli-fit-exact` < 570 |

The repair's pin was found by SWEEPING, not by reasoning: six inputs from 909 to
1443 bytes were compressed under both versions and only two moved (1224 B 523 to
497, 1329 B 594 to 545). The other four are byte-identical, so a pin picked by
guessing which input "ought to" show it would most likely have been a test that
could not fail. Its round-trip passes under both versions -- a discarded plan is
not a wrong answer, only a silently worse one -- so the size is the only
instrument that can see it.

The dictionary's "off" column is measured by raising `brdix-min-word` above the
longest word in the corpus, which disables the search without removing any of the
machinery around it. Both numbers were run; the pin sits between them, so it fails
if the dictionary stops firing and would have failed had it never fired.

**The input must be at least `brotli-dict-min-input` bytes or the pin tests
nothing.** The first version of this case was 516 bytes, under the 1024 floor,
and the index was never built -- the case measured 114 both ways and could not
have failed. That is the block-splitting lesson again in a different costume: an
input that does not reach the feature is not a test of it.

**A pin has to exercise the feature, and the block-splitting one nearly did not.**
The first input tried was a cyclic half followed by a flat half -- two obviously
different shapes. The matcher reduced the cyclic half to a couple of long copies
and the flat half to one long insert, leaving about three commands in total, and
the costing rightly declined to split three commands. It measured 573 bytes both
ways: a test that cannot fail. Block splitting needs many short commands, which is
what prose gives and what a highly matchable input does not.

The unchanged rows are the informative ones. `runs` is a single repeated byte, so
it already took a simple literal code costing nothing per byte and there was
nothing for a match to add. `near-random` has no matches to find and still takes
the stored fallback. Very small inputs inflate on the fixed stream overhead
(WBITS, a meta-block header, the final empty block); that is the format, not the
encoder.

The "before" column is worth reading as evidence rather than as history: it is
the OPTIMAL literals-only encoding produced by the same code-building machinery,
so beating `cyclic` by 3.8x can only come from the copy half of a command -- and
an independent decoder reproducing the exact original bytes from that stream
means the distances and lengths are right, not merely accepted.

## Not implemented

**The 120 non-identity transforms.** The corpus and the matcher are in; transform
zero is all the encoder emits and all the decoder reads.

Block splitting for **literal and distance** block types. Literal block types
multiply the context map -- the tree is `context_map[block_type * 64 + context]` --
so they are the same piece of work as context modelling past four trees, and both
need a complex context-map code with the degenerate-flat case handled. Distance
block types would want their own map. Insert-and-copy has neither, which is why it
went first.

The ENCODER writes context mode zero only. The **decoder reads mode 2** as of
2026-07-19; costing mode 2 against mode 0 in the encoder is what remains.

Larger *declared* windows beyond 16 bits. The window that mattered was the
encoder's reach, not the header field, and that is closed above.

The decoder reads no distance context map and assumes `NTREESD` is 1, because
that is all the encoder writes.

The matcher is Lz77's, which reaches a 32768-byte window with matches to 258 --
RFC 1951's limits, which Brotli would allow it to exceed. It declines a match
that costs more than the literals it replaces, priced from the distinct-byte
count of the surrounding bytes, so a small alphabet takes fewer matches than a
byte-valued one on the same input shape.

The decoder here reads the subset this chapter emits -- in particular a simple
prefix code is always assumed to name exactly one symbol, which is the only
simple code the encoder writes. The interop harness is what proves the OUTPUT is
valid for a general decoder.
