1.1 - `ImgPlug` truncates every image it sends to 1400 bytes. One chunk of
`send-buf-loop` arrives and the rest does not, and it has been that way for
as long as anyone has looked. Measured 2026-07-28 in both directions:
rebuilt from depot sources and run with the identical invocation it sends
1400 bytes, and so does a tree carrying unrelated changes, so it is neither
a regression nor anyone's working state.

**The fault is now located, 2026-08-11.** It is not a lost chunk or a
truncated write: the plug DIES. `!EXC=0e` (page fault) with
`CR2=0xfffffffffffffff8`, which is a list header read at `ptr - 8` off a
null pointer, inside `__list_concat_many + 0x76`. Resolved against
`build-output/img-plug.map`, the frames on the stack are

```
opening -> send-buf-loop -> net-send -> net-send-go
        -> wrap-tcp-in-ip-eth -> tcp-with-checksum -> __list_concat_many
```

so it is the SECOND trip through the send path that dies, after the
first 1400-byte segment is already on the wire. Size-independent:
`-TotalSectors` 128, 512, 8192 and 16384 all yield exactly 1400 bytes,
and `-Fat16` and the default FAT32 fail identically, so neither the FAT
half nor the image size is implicated. Not staleness either -- rebuilt
from current source the same day the six binary backends were rebuilt by
the gate, byte-for-byte the same 168316-byte plug, same failure.

`PePlug.codex` has a `send-buf-loop` that is line-for-line the same and
sends 11200 bytes over eight chunks without trouble, so the loop itself
is not the bug. The difference to chase is around it: ImgPlug's copy is
declared `[Network.Read, Network.Write]` where PePlug's is
`[Network.Write]` alone.

**And the harness reports OK regardless.** `codex/plugs/img/run.ps1`
receives with `while (read) {...} catch {}` and no completeness check, so
a plug that dies mid-stream is indistinguishable from a clean EOF and the
script prints `OK: <file> (1400 bytes)` and exits 0. That is the same
defect class as the bare `catch` fixed in `build/run-plug.ps1`, and it is
why a crashing plug has been reporting success for as long as this entry
has existed.

The shipped stick is unaffected. `build/build-boot-img.ps1` goes through
`build/build-img.ps1`, which is PowerShell and never touches this plug.
What it does mean is that the plug's own GPT and FAT work is verified only
as far as the first chunk reaches: sectors 0-2 arrive and their base and
limit agree with `build-img` exactly at 28639/4096, while the FAT half is
unverified end to end, because no complete image comes out of the plug at
all.

1.2 - `build/test-disk-compile.ps1` is DEAD, and one of its two reasons is
now fixed. It builds its image through this plug, so it inherits the
1400-byte truncation above and the VM refuses the disk; **that half is all
that remains.** Its default `-SampleSrc` pointed at
`codex/test/absorb-outer-lambda.codex`, deleted in change 1568, and was
repointed 2026-08-11 at `codex/test/field-range-proven.codex` -- cite-free,
which this path requires because the image plug embeds the sample raw as
`SOURCE.SRC` and the compiler reads it off the disk inside the VM with
nothing to resolve a `cites` line. Steps 1a to 1c now pass; step 2 is where
it stops. Nothing runs this script, so neither reason had ever surfaced.

The capability it was written for is NOT broken: built through the shipping
path (`build/build-img.ps1`, which never touches this plug), an 8 MB image
compiles from disk byte-identically to the host, measured 2026-08-08 while
prepping A5. So this is a dead harness over a working mechanism. Whoever fixes
1.1 should repoint it at a source that exists and decide whether it uses the
plug or the PowerShell builder.