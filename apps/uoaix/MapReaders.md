# Legacy client map access

`cites Uoaix chapter MulReaders` exposes range planners and checked decoders
for the 1.25 client files. A decoder receives a raw byte-buffer address and
the actual bytes received, not capacity. The caller owns that buffer.
Every public planner/decoder returns `Result value Text`; propagate `Err`.
Never substitute a zero height or empty block for a failed read.

Start the adapter with `pwsh -File apps/uoaix/serve-mul.ps1 -ClientRoot
C:/Users/Damian/uo1998-client`. The default endpoint is loopback port 2595.
The adapter reads the original files in place, opens no writable handle,
and writes no client data to the repository, disk images, logs or fixtures.
Run only one adapter per port; a held port fails at startup.

| file ID | original file | range planner |
|---|---|---|
| 0 | MAP0.MUL | `mul-map-range x y`: whole 196-byte block |
| 1 | STAIDX0.MUL | `mul-staidx-range x y`: 12-byte index |
| 2 | STATICS0.MUL | `mul-index index-buffer 12 statics-file-size 7` |
| 3 | TILEDATA.MUL | `mul-land-data-range graphic` or `mul-item-data-range graphic` |
| 4 | MULTI.IDX | `mul-multi-index-range multi-id` |
| 5 | MULTI.MUL | `mul-index index-buffer 12 multi-file-size 12` |

`GET /size/<file-ID> HTTP/1.0` returns an 8-byte unsigned little-endian file
length. `GET /mul/<file-ID>/<offset>/<count> HTTP/1.0` returns exact raw bytes.
Terminate headers with CR LF CR LF. A successful response has status 200 and
an exact Content-Length; 400 rejects the request and 416 rejects the range.
The connection closes after the response. Count is at most 65536. A larger
indexed extent must be read in bounded chunks aligned to its record stride.
Zero count at EOF succeeds. Unsupported file IDs and paths are refused.

Run codex-vm with `-natmap 2595:2595`; the guest connects to the NAT host
address 10.0.2.2, port 2595. Reek owns the stage-1 connection and preload.
The adapter does not parse or decide map content. The guest must check HTTP
status, exact body length and all decoder results. The generic Text-valued
HttpClient response is unsuitable for binary MUL bytes.

For a terrain query, request the planned map block and decode with
`mul-land buffer 196 x y`, yielding `MulLand { graphic, z }`. Coordinates
are absolute in the map (`mul-map-width = 6144`, `mul-map-height = 4096`).
`mul-static buffer size index` yields
`MulStatic { graphic, x, y, z, hue }`; x and y are block-local, 0 through 7.
`mul-component buffer size index` yields signed x/y/z offsets and component
flags. An absent index yields a zero-length range, distinct from an error.

`mul-land-data` yields flags and texture. `mul-item-data` yields flags,
weight, layer, animation and height. Names remain in the original record;
the movement path does not allocate decoded names. `mul-land-passable`
rejects wet/impassable flags. `mul-item-surface` requires a surface and
rejects wet/impassable flags; `mul-item-standing-z` halves bridge height.
These predicates are ingredients, not a complete movement validator:
stage 1 must combine adjacent land heights, static/multi obstructions,
body clearance and step limits. Base MUL data is read without VERDATA or
server patch overlays. Client acceptance must establish whether overlays
affect the chosen walking area before claiming client parity.

All planners and individual decoders take constant time and allocate a
fixed-size result record/variant, with no file-sized allocation. Statics and
multis are decoded one record at a time; scanning an extent is linear in
its record count. A caller running indefinitely must consume results within
a scratch heap mark and restore after copying retained values. No decoder
retains the input buffer or creates a List Integer copy. The adapter retains
one 65536-byte data buffer and one 4096-byte request buffer, plus bounded
HTTP metadata; sockets have five-second receive/send timeouts.

`proofs/MulReadersProof.codex` is a focused native proof with fabricated
records, not part of the battery. Compile with explicit `-Kernel`, `-Src`,
`-Out` and `-Log` via `build/compile.ps1`, run via `build/test-run.ps1`, and
compare the complete output, with CR removed, against
`apps/uoaix/proofs/MulReadersProof.expected`. Runner exit zero alone is
insufficient. `proofs/test-mul-service.ps1 -ClientRoot <install> -Port 2595`
grades an already running adapter. Live adapter checks
must compare responses directly with read-only ranges from the install,
without saving response bodies. No client files belong in a changelist.

Format references: [ClassicUO MapLoader](https://github.com/ClassicUO/ClassicUO/blob/main/src/ClassicUO.Assets/MapLoader.cs),
[TileDataLoader](https://github.com/ClassicUO/ClassicUO/blob/main/src/ClassicUO.Assets/TileDataLoader.cs),
and [MultiLoader](https://github.com/ClassicUO/ClassicUO/blob/main/src/ClassicUO.Assets/MultiLoader.cs).
The native proof grades our layout handling; client screens remain the
stage-1 acceptance oracle specified in UOAIX.md.

Validation on 2026-10-04 used depot kernel `9752080A0276505E`: all 20 native
checks matched the exact oracle. A transposed x/y decoder failed the
asymmetric cell check; deleting an output line failed the exact comparison.
The live adapter passed its required 78 checks against the original install,
including 20 Britain map/index ranges, EOF, size, cap and request refusals.
The independent reader pass found and corrected the symmetric-cell proof,
missing output manifest and missing NAT setup. No client login or walking
screen has been graded by these reader proofs.
