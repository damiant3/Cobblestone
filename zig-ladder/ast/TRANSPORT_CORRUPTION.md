# Moved

This described the transport corruption while it was still a mystery. It
is solved, and the full write-up now lives with the repro fleet:

`~/showell_repos/NewRepository/deck-record-repro/PLUG_IR_TRANSPORT.md`

The short version: `net-recv-raw` derives its `rep insw` word count with
`shr rcx, 1`, which rounds down, so an odd-length frame loses its last
byte while the helper still returns the full length. The caller then
reads one stale byte left by the previous frame.

What this file used to recommend -- transfer twice at different chunk
sizes and require agreement -- was a vote, and it worked for the right
reason without knowing it. `plug_run_checked.py` now proves instead:
every write is even, the wire total is padded to even, and the capture is
read back to confirm no host-to-guest frame had an odd body. One transfer
instead of four. The agreement route is still in there as a fallback.
