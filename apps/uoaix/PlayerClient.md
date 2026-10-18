# Setting up a player client for UOAIX

UOAIX serves the 1998 Ultima Online client, `CLIENT.EXE` 1.25.32. The server owns every tree, crop and other plant a
player or NPC can fell, harvest or plant, so it can draw a tree falling, a stump and a sapling. The client draws its own
copy of those plants from its map statics (`STATICS0.MUL`, `STAIDX0.MUL`) and the server cannot remove a static from the
screen. A player's client therefore needs two changes to those two files before it connects:

1. **The flora strip** takes out the plants the server owns. Grass, flowers, ferns and reeds stay in the client.
2. **The decorator bake** adds the shard's own permanent decorations (`decor.cfg`) to the client's statics.

A client without them still plays, but every tree stands in the client's own art beside the server's: a felled tree
stays drawn, and baked decorations are missing.

Both steps write new files into an output folder and never touch the client install. Every input and output is checked
against SHA-256 hashes in `apps/uoaix/flora-reference.json`; a mismatch stops the step with a message and writes nothing.

## What you need

- A 1.25.32 client install whose `STAIDX0.MUL` and `STATICS0.MUL` hash to the `original` values in
  `apps/uoaix/flora-reference.json`. The strip refuses any other map files.
- PowerShell 7 (`pwsh`) and a copy of this repository (the two scripts read `CompositeProduction.codex`,
  `flora-reference.json` and `decor.cfg` from `apps/uoaix`).
- Two empty folders **outside** the repository, here `C:\uoaix\stripped` and `C:\uoaix\baked`. The scripts refuse an
  output folder inside the repository.

## Steps

Close the client first. From the repository root, with `<client>` the client install folder:

1. Back up the client's map statics:

   ```powershell
   New-Item -ItemType Directory -Force C:\uoaix\backup
   Copy-Item <client>\STAIDX0.MUL, <client>\STATICS0.MUL C:\uoaix\backup\
   ```

2. Strip the server's flora. Success prints a `STRIPPED rows=... trees=...` line and writes `STAIDX0.MUL`,
   `STATICS0.MUL`, `flora.flr` and a `flora.json` receipt:

   ```powershell
   pwsh -File apps/uoaix/transform-flora.ps1 -ClientRoot <client> -OutDir C:\uoaix\stripped
   ```

3. Bake the decorations onto the stripped files. Success prints `DECOR statics=N removed=M C:\uoaix\baked` (items added
   and items taken out) and writes
   `STAIDX0.MUL`, `STATICS0.MUL` and a `decor-statics.json` receipt:

   ```powershell
   pwsh -File apps/uoaix/decor-statics.ps1 -StrippedDir C:\uoaix\stripped -OutDir C:\uoaix\baked
   ```

4. Copy the two baked files over the client's:

   ```powershell
   Copy-Item C:\uoaix\baked\STAIDX0.MUL, C:\uoaix\baked\STATICS0.MUL <client>\
   ```

5. Point the client at the shard: edit the `LoginServer=` line of the client's own `<client>\LOGIN.CFG` to the host
   and port the shard's operator gives you, `LoginServer=<host>,<port>` (a shard on your own machine is
   `LoginServer=127.0.0.1,2593`).

6. Check in game: chop a tree with an axe until it is felled. A set-up client shows a stump where the tree stood; a
   client still drawing its own tree there did not take the step 4 copy.

## After the shard's decorations change

The decorations change when the shard bakes (`decor.cfg` changes in the repository). Run step 3 again from the same
stripped folder and repeat step 4. The strip (step 2) needs no rerun: its output depends only on the client and the
reference.

## Undo

Copy the two backed-up files from step 1 back into `<client>`. Without a backup, the strip rebuilds the originals from
its own output, verified against the `original` hashes:

```powershell
pwsh -File apps/uoaix/transform-flora.ps1 -ClientRoot C:\uoaix\stripped -OutDir C:\uoaix\restored -Restore
```

That writes the original `STAIDX0.MUL` and `STATICS0.MUL` to `C:\uoaix\restored` for you to copy into `<client>`.
