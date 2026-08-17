# net-recv-raw drops the last byte of every odd-length frame

> **RESOLVED in the depot — kept as the record, not as a request.**
> Re-checked against seed 270227BE on 2026-08-17: the fix is in
> `emit-net-recv-raw-helper` (X86_64IPCHelpers.codex) as `st56a`/`st56b`,
> the same two instructions and the same insertion point proposed below:
>
>     in let st56a = st-append-code st56 (add-ri reg-rcx 1)
>     in let st56b = st-append-code st56a (and-ri reg-rcx (0 - 2))
>
> Recommendation 2 is also answered: `net-process-ip` drops a segment on
> `bad-tcp-checksum` before parsing it (NetworkStack.codex). That call was
> already present when this was written, which leaves the one question this
> document never settled -- how a substituted byte reached the parser with a
> checksum in front of it. The likeliest reading is that the byte the DMA
> dropped fell in Ethernet padding past the IP length rather than inside the
> checksummed payload, in which case the checksum was never wrong. Untested.
>
> Everything below is as submitted, in the present tense it was written in.

## What we recommend

1. **Take the fix.** It is in this PR, one commit, two added instructions
   in `emit-net-recv-raw-helper`. We could not compile it here (see
   below), so please build before trusting it.
2. **Look at receive-side TCP checksums.** A byte substituted inside a
   TCP payload reached the parser with nothing objecting. Whatever the
   reason, that is the layer that should have caught this, and it would
   have turned two days of archaeology into one dropped segment and a
   retransmit.
3. **Keep the `codex-vm` pad.** It stops being load bearing once the
   guest is fixed, but it costs nothing and it is correct.
4. **Do not certify a plug transfer by comparing outputs.** See "A
   corrupted byte is not always a visible one" below. This one surprised
   us and it invalidated one of our own measurements.

## The defect

`emit-net-recv-raw-helper` reads the frame body out of the NE2000 with a
word-granular `rep insw`:

```
   pop rcx          ; rcx = frame body length, in BYTES
   shr rcx, 1       ; -> word count, rounding DOWN
   cld
   rep insw         ; move rcx words into the caller's buffer
```

When the body length is odd, `rep insw` moves `len - 1` bytes and the
final byte is never read from the NIC. The helper still returns `len`, so
`ne2k-read-from-buf` reads all `len` bytes back out of the fixed buffer
at 33056. That buffer is never cleared between frames, so the last byte
of an odd-length frame comes back as **whatever the previous frame left
at that buffer offset**: a real, recent byte from the same stream. It is
always plausible, it never trips a parser, and nothing anywhere reports
it.

The send path already compensates. `ne2k-send-frame` in
`codex/os/kernel/Ne2k.codex` pads an odd frame and transmits
`len + (int-mod len 2)`. TX knows the DMA is word-granular; RX does not.

## The fix

Round the byte count up to even at `st56`, where it is popped and before
it is pushed back for RBCR. The existing `shr` at `st70` then derives the
right word count from it, and RBCR agrees with what is actually
transferred:

```
   pop rcx                       ; st56, rcx = body length
   add rcx, 1                    ; 48 83 C1 01   <- added
   and rcx, -2                   ; 48 83 E1 FE   <- added
   push rcx                      ; st57, and on into RBCR
   ...
   pop rcx                       ; st69
   shr rcx, 1                    ; st70, now exact
   rep insw
```

Safe: the length check above rejects any body over 1536, so an odd body
is at most 1535 and rounding up transfers 1536, exactly the receive
buffer's size, still clear of the TX buffer at 34592. The returned length
is unchanged, so the pad byte is never read back out. Jump patching is
unaffected, since every label position is a `code-len` snapshot taken
after the insertion and `patch-jcc-at` computes relative deltas.

**Not compiled here.** The concatenated compiler source is 2.78 MB and
our QEMU side-channel injects through a 1 MB ring, so we cannot self-host
on this machine. The change is offered as a diff in the file you own
rather than as a paragraph in a bug report.

## Why you have never seen it

`codex-vm` compensates. `ne2k_inject_rx` in `tools/codex-vm.c` pads every
odd frame before it reaches the ring, and names the mechanism exactly:

```c
/* Pad odd-length frames to even: the guest uses REP INSW (word DMA)
   which reads frame_len/2 words, truncating the last byte of odd frames.
   The ip-payload fix uses ip-total-length to ignore the padding byte. */
```

That went in with Update 30, and `ip-total-length` in
`codex/os/net/Ethernet.codex` is the guest half of the same
compensation. Two workarounds, cause untouched. The receive path is
therefore sound only against an emulator that pads for it: QEMU's
`ne2k_isa` pads to the 60-byte minimum, like real hardware, but not to
even. Everything below was measured there.

## Evidence

Twenty-five transfers of a 191 KB IR through the zig plug, wire captured
each time with `filter-dump`.

**Parity predicts every run.** Taking the frame that carries IR offset
36863 and asking only whether its body length is odd:

| frame carrying the offset | runs | outcome |
|---|---|---|
| odd body (923 bytes) | 11 | corrupt, every time |
| even body (1270 bytes) | 3 | clean |
| offset falls mid-frame | 11 | clean |

25 of 25, no exceptions.

**The stale byte is the predicted one.** The corrupting frame has a
923-byte body, so the untransferred byte is at buffer offset 922. The
previous frame through that buffer carries byte 2 (a CCE space) there.
The guest reported 2. The correct byte was 73, a dash, and
`scan-digits-end` emitted as `fn scan digits_end` -- not valid zig.

**The wire is innocent.** Reassembling the host-to-guest stream from a
corrupt run's capture yields the IR exactly: 191,126 bytes, no gaps, no
retransmits, byte 73 delivered correctly.

## One defect, three doses

The stalls and crashes we reported earlier as a "defect family" are the
same bug. Severity tracks how many odd frames the segmentation produces:

| odd frames | what you see |
|---|---|
| 0 | correct output |
| 1 | one substituted byte: a wrong program, or nothing visible |
| 33-37 | `!EXC=06`, a crash inside `parse-expr` |

An odd *chunk size* makes nearly every remainder frame odd, which is why
4097 was singularly bad: 37 and 33 odd frames in two runs, both crashing
at different RIPs inside `parse-expr` (+3791 and +2680) -- data-dependent,
the shape of a parser walking into mangled input rather than a fixed bug.

The capture clears the transport in the crash case: the guest ACKed all
191,132 bytes with zero retransmissions, transfer complete in 0.9s, then
a 10s gap and the crash. It receives everything and dies parsing it.

The dead stalls did not reproduce against the current plug (chunk 1500,
three runs, all completed). They were characterised before the linear
`bytes-to-text` fix. We are retiring the claim rather than restating it.

## A corrupted byte is not always a visible one

Our earlier table recorded chunk 1500 and 3000 as "0 of 4 corrupted".
That was wrong, and it is the most useful thing we learned by being
wrong.

Chunk 1500 carries exactly one odd frame every run and still emits
byte-identical output, because the truncation lands at IR offset 11999
and turns `int` into `lnt` inside a type expression the emitter never
consults. We confirmed it by handing the plug an IR with that byte
changed by hand: identical output.

So a passing output comparison only covers the bytes that reach the
output. **Only the capture can certify a transfer** -- no host-to-guest
frame with an odd body. We now do exactly that, one transfer plus a
proof, instead of the old transfer-twice-and-compare.

## Blind alleys, so nobody walks them again

- **It is not a CCE encoder/decoder bug.** The mangled name looks exactly
  like a CCE round-trip fault, and that is where we spent the first day.
  Decoding the IR on disk shows the byte correct; the damage happens on
  the way in.
- **It is not the plug's `bytes-to-text`.** That converts in 256-byte
  chunks and is the obvious suspect. Rebuilt with the chunk changed to
  200, the corruption still landed at the same offset.
- **The "256-byte alignment condition" is not a condition.** Every
  corrupting chunk size we first tried happened to be a multiple of 256.
  It is a coincidence of the sizes tried.
- **The site is not a property of the payload.** It moves with the frame
  layout, which is set by TCP segmentation, which is why host CPU load
  appeared to change a corruption rate. Nothing here is racy given the
  frames.
