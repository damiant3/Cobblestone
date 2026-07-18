# Zstandard Format Notes (RFC 8878)

Working reference for the `codex/foreword/compress/Zstd.codex` chapter. Distilled
from RFC 8878 and the facebook/zstd `doc/zstd_compression_format.md`, plus what a
python-`zstandard` oracle (`build/zstd-interop-test.ps1`) proved on our own output.
Reference doc: not read at init. Update it when the chapter grows.

## Why this exists

Our Zstd round-trips through its own decoder, and that cannot see a bug both
halves share. One shipped for months: the frame wrote a two-byte
Frame_Content_Size, which a real decoder reads with a +256 offset (below), so
real zstd rejected every frame we ever emitted while our tests stayed green. The
lesson is the project's usual one: a round-trip cannot tell a compressor from a
pipe, and self-consistency is not validity. Validate against `zstandard`.

## Frame

```
Magic_Number         4 bytes LE = 0xFD2FB528  -> bytes 28 B5 2F FD
Frame_Header         2..14 bytes (see descriptor)
Data_Block(s)        one or more
[Content_Checksum]   4 bytes, only if the descriptor's checksum flag is set
```

### Frame_Header_Descriptor (first header byte)

| Bits | Field |
|------|-------|
| 7-6  | Frame_Content_Size_flag (FCS width selector) |
| 5    | Single_Segment_flag (1 = no Window_Descriptor byte) |
| 4    | Unused |
| 3    | Reserved (must be 0) |
| 2    | Content_Checksum_flag |
| 1-0  | Dictionary_ID_flag |

FCS width from the flag: 0 -> (Single_Segment ? 1 : 0) bytes; 1 -> 2 bytes;
2 -> 4 bytes; 3 -> 8 bytes. Header length =
`1 + (Single_Segment?0:1) + Dictionary_ID_bytes + FCS_bytes`, plus the 4 magic
bytes = 5 + ...

### THE FCS +256 TRAP

RFC 8878 3.1.1.1.4: when the FCS field is **two** bytes, the decoder ADDS 256 to
the stored value. A header that stores the size directly decodes 256 bytes too
large and the frame is rejected as corrupt. The one-byte and four-byte widths
carry no offset. We emit **Frame_Header_Descriptor 0xA0** = single-segment,
no-dictionary, four-byte FCS (a nine-byte header including magic), and write the
content size directly. `zstd-header-len` reads the descriptor back rather than
assuming its own shape.

## Block

Every block: a 3-byte little-endian `Block_Header`.

| Bits | Field |
|------|-------|
| 0    | Last_Block |
| 1-2  | Block_Type (0 Raw, 1 RLE, 2 Compressed, 3 reserved) |
| 3-23 | Block_Size |

Raw and Compressed: `Block_Size` is the size of Block_Content (excludes the
header). RLE: `Block_Size` is the regenerated length, and Block_Content is a
single byte. A Compressed_Block's content is a Literals_Section followed by a
Sequences_Section.

## Literals_Section (Compressed_Literals_Block, Size_Format 0)

Literals_Section_Header, 3 bytes, bits packed low-first:

| Bits | Field |
|------|-------|
| 0-1  | Literals_Block_Type (0 Raw, 1 RLE, 2 Compressed, 3 Treeless) |
| 2-3  | Size_Format |
| 4-13 | Regenerated_Size (10 bits, 0..1023) |
| 14-23| Compressed_Size (10 bits, 0..1023) |

Size_Format 00 = a **single** Huffman stream, both sizes 10 bits, **no jump
table**; the stream is exactly `Compressed_Size` bytes. Regenerated_Size is the
number of literal bytes; Compressed_Size is the size of (Huffman_Tree_Description
+ the single stream).

## Huffman_Tree_Description (direct weights)

First byte `headerByte`:
- `< 128`: `headerByte` = number of bytes of an FSE-compressed weight table. (We
  do NOT use this; direct is simpler and legal.)
- `>= 128`: **direct** weights. `Number_of_Weights = headerByte - 127`. Weights
  are 4 bits each, two per byte, **high nibble first**, so
  `ceil(Number_of_Weights / 2)` bytes. **The last non-zero weight is NOT
  written** -- the decoder deduces it.

### Weights, bits, and the implied last weight

A symbol's `Weight` (1..) sets its code length; `Weight 0` = absent.

```
Max_Number_of_Bits = log2( nearest power of 2 strictly greater than
                           SUM over present symbols of 2^(Weight-1) )
Number_of_Bits(sym) = (Weight>0) ? Max_Number_of_Bits + 1 - Weight : 0
```

The transmitted weights sum (as `2^(Weight-1)`) to something below a power of 2;
the implied last weight is whatever completes the sum to exactly that power of 2:
`last_weight = 1 + log2(nextPow2 - partialSum)` (and 0 if it completes exactly,
which cannot happen for a real last symbol).

Encoder direction (building weights from code lengths): pick
`Max_Number_of_Bits = max length`, then `Weight = Max_Number_of_Bits + 1 - length`
for each present symbol, `0` for absent. Drop the last present symbol's weight
from the byte stream.

### Canonical code assignment (distribute from the LONGEST codes)

zstd assigns codes starting from the lowest weight = **highest Number_of_Bits**,
opposite to RFC 1951 / Deflate. Sort present symbols by `(Number_of_Bits
descending, symbol ascending)` and:

```
code = 0 ; prevBits = firstSymbol.nbBits
for sym in sorted:
    if sym.nbBits < prevBits: code = code >> (prevBits - sym.nbBits); prevBits = sym.nbBits
    codeOf[sym] = code            -- emit MSB-first, nbBits wide
    code = code + 1
```

Worked example from the spec (A..F):

| Sym | Weight | Bits | Code |
|-----|-------:|-----:|------|
| E   | 1 | 4 | 0000 |
| F   | 1 | 4 | 0001 |
| C   | 2 | 3 | 001  |
| B   | 3 | 2 | 01   |
| A   | 4 | 1 | 1    |

## The Huffman bitstream is read BACKWARD

The decoder reads the stream's **last byte first**. In that last byte it skips
the high 0-padding and the first `1` bit it meets (the sentinel), and the useful
bits begin below it; it then continues into earlier bytes. Within a byte, bits
are consumed high-to-low. So the last byte cannot be zero.

### Clean encoder construction (no bignum needed)

Model the whole stream as an integer `N = (1 << L) | S`, where `L` is the total
code-bit count and `S` is the codes of the literals **in order**, each MSB-first,
packed with symbol 0's code in the highest bits. Serialize `N` little-endian:
byte j = `(N >> 8j) & 255`. The top byte holds the sentinel with 0-padding above
it -- exactly the format -- and the last byte is byte `M-1`, non-zero.

Because `N` is built by appending each code to the LOW end
(`N = (N << len) | code`, starting `N = 1` for the sentinel), its little-endian
bytes come out by feeding bits **LSB-first** in **reverse literal order** and
finishing with the sentinel bit:

```
for literal in REVERSE order: bw-bits(code[literal], length[literal])   -- LSB-first
bw-bits(1, 1)                                                            -- sentinel
bw-finish   -- pads the final (top) byte's high bits with zero
```

This is exactly Deflate's LSB-first `BitWriter` (`bw-bits`/`bw-finish`), reused.
The decoder mirrors it: start at the last byte, drop the top zeros and the first
set bit, then read codes MSB-first, walking bytes from high address to low.

## Sequences_Section

One leading byte is `Number_of_Sequences`. **`0x00` = no sequences**: the section
ends there and the block's output is exactly the decoded literals. A
Huffman-literals block with zero sequences needs no FSE tables at all -- it is the
smallest real (entropy-coded) Zstd block.

## What we implement

- Frame: 0xA0 descriptor, 4-byte FCS. (done)
- Raw, RLE blocks. (done, oracle-validated)
- Compressed block = Huffman literals (direct weights, single stream) + zero
  sequences, chosen per literal region only when it beats a raw block.
- Not implemented: FSE literals, sequences/matches (LZ), 4-stream literals,
  dictionaries, content checksum.
