1.2 - `build/test-disk-compile.ps1` names no compiler. Step 1a compiles the
sample and Step 2 boots the DISK compile from `build-output/bare-metal/Codex.cdx`,
which holds whichever compiler ran last, so a verdict from this script describes
that binary and not the depot seed. The fix is `-Kernel seed\Codex.cdx` at both
steps, in the script and in its generator under `codex/build/` (reek's claim).
The image plug it builds through sends whole images at 2026-09-24's head (FAT32
and FAT16, 8,388,608 of 8,388,608 bytes), and nothing else runs this script, so
the run after the kernel fix is its first verdict. The DISK mechanism itself
works through `build/build-img.ps1` (measured 2026-08-08).
