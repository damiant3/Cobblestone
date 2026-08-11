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

1.2 - `build/test-disk-compile.ps1` is DEAD, for two reasons that compound. It
builds its image through this plug, so it inherits the 1400-byte truncation
above and the VM refuses the disk; and its default -SampleSrc,
`codex/test/absorb-outer-lambda.codex`, no longer exists. Nothing runs it, so
neither showed up.

The capability it was written for is NOT broken: built through the shipping
path (`build/build-img.ps1`, which never touches this plug), an 8 MB image
compiles from disk byte-identically to the host, measured 2026-08-08 while
prepping A5. So this is a dead harness over a working mechanism. Whoever fixes
1.1 should repoint it at a source that exists and decide whether it uses the
plug or the PowerShell builder.