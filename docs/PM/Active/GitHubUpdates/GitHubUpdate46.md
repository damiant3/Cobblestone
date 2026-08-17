# GitHub Update 46

**Scope: main CLs 16149 onward, opened 2026-08-16.** Update 45 covered 15687
to 16148. Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 45

- **The ARM64 site serves once per boot.** `GET /` answers 200 and the next
  request in the same boot does not complete. Measured at the release: the
  guest serves TWO loops for ONE host request with identical path, `recv` and
  `resp` (a re-read), then takes a `Synchronous Exception at 0x401016E4`, and
  QEMU reports `virtio-net receive queue contains no in buffers` and `Guest
  moved used index from 10043 to 33`. So the host-visible timeout is the
  client's view of a dead guest. One candidate is eliminated (threading
  `r.ws-state` forward and dropping the `__heap-restore` does not move the
  arm) and one is recorded UNMEASURED with the probe that settles it:
  `transport-new` advances the heap by a full 32 MB against a 32 MB stub
  grant, so the frontier may reach the DMA constants at `#44000000`.
  `docs/Designs/Active/OS/OracleCloudArm64.md`.

- **Neither virtio driver derives its DMA regions from the stub's
  allocation.** Both carry hardcoded constants and a prose warning, and the
  regions have already been inside the guest's own memory once. A derivation
  would be better than the warning.

- **The csharp plug has no `peek-16`/`poke-16` row** in either builtin table,
  and nothing standing compares builtin emission tables across plugs.
  `check-plug-types.ps1` compares the IR TYPE wire forms and not these, so the
  gap bites the first time a compiler or foreword chapter calls a 16-bit
  accessor. The DDC is what would surface it. Harmless today because the
  compiler reaches neither.

- **The internal gate does not carry `codex/test/errors/`**, which is how a
  test whose premise had died reached main green in Update 45.

- **The battery harness can lose bytes from a batch stream** and file the
  survivors under the wrong names. Carried from Update 44; not seen since.

- **The zig plug's remaining defects** and `plugs-backlog` 1.2, 35 transpiler
  plugs leaking the field slot.

- **Nothing exercises the guard page under a genuine allocation walk.**

- **`build/plug-run.ps1` reports `OK` on a dead guest**, because it greps the
  VM's stderr for `TRUNCATED sent=` and the guest console is not on stderr.
  Found in passing during PR 66 and not fixed.

## Landed this cycle

*Nothing yet.*
