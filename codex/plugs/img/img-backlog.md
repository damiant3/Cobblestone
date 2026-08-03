1.1 - `ImgPlug` truncates every image it sends to 1400 bytes. One chunk of
`send-buf-loop` arrives and the rest does not, and it has been that way for
as long as anyone has looked. Measured 2026-07-28 in both directions:
rebuilt from depot sources and run with the identical invocation it sends
1400 bytes, and so does a tree carrying unrelated changes, so it is neither
a regression nor anyone's working state.

The shipped stick is unaffected. `build/build-boot-img.ps1` goes through
`build/build-img.ps1`, which is PowerShell and never touches this plug.
What it does mean is that the plug's own GPT and FAT work is verified only
as far as the first chunk reaches: sectors 0-2 arrive and their base and
limit agree with `build-img` exactly at 28639/4096, while the FAT half is
unverified end to end, because no complete image comes out of the plug at
all.
