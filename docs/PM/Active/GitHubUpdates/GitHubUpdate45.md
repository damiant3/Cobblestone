# GitHub Update 45

**Scope: main CLs 15687 onward, opened 2026-08-16.** Update 44 covered 15254
to 15686. Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 44

- **The battery harness can lose bytes from a batch stream** and file the
  survivors under the wrong names. Seen once, at 15671 with ten VMs live; not
  seen in the two batteries run afterwards with the box otherwise idle. The
  lossy layer (guest serial, host `-output` writer, or the parser's marker
  walk) is not established. `ExaminersAssay.md` "The batch stream can lose
  bytes".

- **The zig plug's four defects** (`plugs-backlog` 1.13, val): no `int-mod`
  emitter, a record literal emitted without the parentheses zig needs before a
  field access, bounded-field clamping not emitted, and the fourth found at
  15634. The oracle arm stays unwired until they are fixed.

- **`plugs-backlog` 1.2, 35 transpiler plugs leaking the field slot** (val).

- **Track D items 18 and 19 land here** (reek 15677, 15690, 15691, 15695,
  queued for MAIN OPEN at the release): Lz4, Lz77 and Deflate guarded; Rle a
  row and no guard; Brotli a termination arm and no guard. Item 19 is closed on
  the `compress/` subset only: the `ai/`, `ui/` and `core/` leg is unswept and
  unowned (reek 15696), and `tls-decode-record` and `pbkdf-verify`, run by a
  harness though called by no production code, qualify for a guard under the
  ruling if it is extended past `compress/`. The OtaBoot flash length residue
  stays open by name. Then the census queue.

- **Every stub path carries unflown bytes since 15503**; the native-GOP metal
  half is a photograph.

- **Nothing exercises the guard page under a genuine allocation walk.**
