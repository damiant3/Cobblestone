# contrib

Contributor-maintained work that exercises this repository from outside its
gate. The zig plug's phase-oracle ladder lives at
https://github.com/showell/codex-zig-ladder and is maintained there rather than
here: it is a Diverse Double-Compiling harness that compiles compiler chapters
two ways, once by the seed on bare metal and once through the zig plug, and
requires the two to agree byte for byte, so it needs python, sh, zig and QEMU on
a Linux host and deliberately runs outside `build/build.ps1` and its
PowerShell-only tooling. Keeping it in its own repository states that separation
plainly, lets one ladder be pointed at several Updates in turn (its master
branch names the seed hash it is currently banked against, and refuses to report
green against any other), and keeps the witness's lineage separate from the tree
it audits. What flows back here is what belongs here: plug and compiler changes
as small branches cut from master, each carrying a `Ladder:` line naming the tag
that exercises it, with findings entered in `codex/plugs/plugs-backlog.md` and
`codex/compiler/compiler-backlog.md` the way PR 66's were.
