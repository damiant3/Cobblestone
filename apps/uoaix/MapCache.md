# Installed whole-map cache

Install from the operator's original legacy client while the server is stopped:

```powershell
pwsh apps/uoaix/import-decoration.ps1 -ClientRoot <client-directory> -ReferenceRoot <uo-reference> -OutFile <local-britannia.dwd>
pwsh apps/uoaix/transform-flora.ps1 -ClientRoot <client-directory> -OutDir <local-flora-dir>
pwsh apps/uoaix/install-map-cache.ps1 -ClientRoot <client-directory> -WorldDisk <world.disk> -DecorationFile <local-britannia.dwd> -FloraFile <local-flora-dir>/flora.flr
```

`-ClientRoot` is the original client in both: the server's collision comes from
its statics, so the installer refuses the flora-stripped `STATICS0.MUL`. Without
`-FloraFile` the server logs `FLORA off` and draws no flora items, which leaves a
player who stripped their client without trees. The flora table's kind 3 rows are
furnishings `premises.cfg` strips from the player's statics; the installer
installs no collider for them.

Installation covers the entire6144 by4096 map. It reads the original MULs in
place, decodes land flags/heights and all movement-relevant static colliders,
and appends the immutable decoration dataset. Source hashes are checked again
before commit. The first64 MiB journal prefix is preserved byte-for-byte.
The file is opened exclusively, so a live writer prevents installation.

The cache is local operator data under UOAIX section2. Neither cache bytes nor
the world disk belong in the depot, a shipped image or a published artifact.
Workspace output is restricted to build-output. The installer does not create
MUL copies. A server boot reads only installed disk data; missing or corrupt
data refuses rather than falling back to the MUL bridge.

UMC2 begins at sector131072, after the64 MiB world journal. Its512-byte header
contains magic32434D55, version3, map width/height, block count393216, directory
bytes6291456, header FNV32 at48, total cache bytes at56, maximum static
colliders at64, first data sector12289 at72 and total collider count at80.
Decoration sector/length/FNV32 occupy88/96/104; map-data end sector is112;
the combined collision capacity is120. Source SHA-256 values begin at128 in
MAP0, STAIDX0, STATICS0, TILEDATA order. Item table sector/length/FNV32
occupy256/264/272: TILEDATA's item section (608256 bytes, from offset428032)
copied verbatim after the decoration, read once at boot for item weights and
names (`ItemTable.codex`). Length0 means a cache installed before the table;
the server then sends weight from gold only and falls back to its built-in
names. Flora table sector/length/FNV32 occupy280/288/296: the FLR1 `flora.flr`
(`WorldFlora.codex`) copied after the item table, which then ends where it
starts; length0 means no flora. The economy archive region (UOAIX-50) follows the
flora: start sector at304 and capacity in sectors at312 (524288, 256 MiB); zero
means a cache installed before the region, and otherwise the last of the extents above ends at its start sector. Total cache bytes at56 include the
region. A reinstall copies the region's used sectors to the new position before
it commits the header. Header FNV32 skips its own qword.
An installation-in-progress UMI2 header permits retry but cannot be booted.

The directory has one16-byte entry per8-square block, in MUL block order:
relative sector64, payload bytes32, payload FNV32. Payloads are sector aligned.
Each binds its block ID and cell count64, then64 seven-byte land rows: flags32,
signed z8 and collider count16. Grouped collider rows follow: flags32, signed
z8 and height8. Land flag bit 31, which no land TILEDATA entry sets, marks a
mountain or cave land tile from `MiningTiles.codex` (ServUO
`m_MountainAndCaveTiles`); mining reads it, because a walkable mountainside
carries no TILEDATA flag. The installer refuses a TILEDATA that sets the bit.
A version-2 cache has no marks and refuses boot with "rerun
install-map-cache.ps1"; rerunning it on the same world disk upgrades in place.
Seat art (`walk-chair`, Source-X `IsID_Chair`) is never a collider, whatever
its TILEDATA flags, so the castle throne and both loom benches seat
(`proofs/ChairSeatProof.codex`). A cache installed before that rule still holds
those colliders: rerun `install-map-cache.ps1` on the same world disk.
Each page is checked for extent, checksum, block identity and decoded bounds
before it is used. The optional DWD1 payload has a separate checksum.

`wmc-open device` reads only the header and reserves a reusable working set.
`wmc-cover cache x y` pages a64-square RAM window around the actor before a
step approaches its edge. This is not a world boundary: distant regions and
the actual map edges are addressable. Only the current directory sector and
decoded block are buffered. The full map is never loaded into guest RAM.
The installed maximum, plus a conservative bound for all decoration door
positions, determines collider capacity; installation refuses above256.

After a refill the composite calls `wd-rebind` before gameplay. The immutable
cache remains the collision base; saved door bits are overlaid once on that
fresh base. Any refill/rebind failure closes the owner. `wmc-decoration`
loads the install dataset once below connection scratch.

Cache generation streams source blocks and retains one tiledata table, the
directory and bounded block buffers. Guest page memory is proportional to
the working window and admitted collision capacity, not world area. Page
decode is linear in its records; page hits are constant-time checks. No new
allocation is retained on movement. Compiler heap/time behavior is unchanged.

`MapCacheProof.codex` runs without Network capabilities or a bridge. It checks
the legacy48-block semantic hash and terrain samples, crosses the former
Britain edge, loads a distant region and the map edge, and refuses out-of-map
coordinates. `WalkLoadProfile.codex` records the legacy bridge's request
bytes and HPET request/decode times per block. Client walking remains root's
end-to-end grade.
