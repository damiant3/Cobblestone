# Kernel Filesystem Servicer (BACKLOG 1.14)

**Status:** DONE -- closed 2026-07-14 (fester). Filed 2026-07-14 (fester);
route chosen by Damian 2026-07-14: *build the kernel FS servicer* (not
"reframe", not a vacuous effect-op guard). All three increments landed:
the elevation gate, the default servicer install, and the enforcement
tests. The servicer is a self-compiled **fixed point** (the seed rebuilt
from this source round-trips byte-identically and passes the BVT), not
only the one-pass dev SUT the interim note below describes. Pinned by
`codex/test/fs-servicer` (the main-process round-trip) and
`codex/test/fs-spawn-inherits` (the servicer survives a fork: a child
inherits `FileSystem.Write` and reaches the disk through the servicer
while holding no `Device.Block`). No regressions: `fs-layer`,
`scope-runtime-open`/`-deny`, `fat16-write` all still pass.

**One-line goal:** make `cap-filesystem-read` (bit 6) and
`cap-filesystem-write` (bit 7) load-bearing at runtime, so a program that
declared `[FileSystem.Read]` **cannot** write a file -- enforced by the
running machine, not only by the type checker.

---

## Why the obvious fixes do not work

The investigation that opened this campaign established three facts that
kill the easy answers, and every increment below is shaped by them.

1. **The runtime disk chokepoint already exists, and it is `Device.Block`.**
   `fat16-*` does not poke IDE ports in-process. `block-read-sector` and
   `block-write-sector` (`X86_64Helpers.codex`) emit `li rax, 10|11 ;
   syscall`. Syscalls are real `SYSCALL` instructions -- boot writes the
   handler into `IA32_LSTAR` (`X86_64Boot.codex:1578`, MSR 0xC0000082).
   `emit-block-read-syscall` / `emit-block-write-syscall`
   (`X86_64Boot.codex:2334–2394`) each call
   `emit-check-capability st cap-block-device` and return `-1` without
   driving the device if the bit is clear. **So reaching a sector already
   requires bit 10.**

2. **`Device.Block` dominates `FileSystem` (1.5's privilege ordering).**
   A process holding bit 10 can read and write every sector of the
   volume. Therefore a bit-6/7 test *at the block syscall* is either
   redundant (the caller has full block authority) or wrong (it denies a
   legitimate `Device.Block` layer). Direction cannot be enforced there.

3. **In the current handler model the direction is discharged at compile
   time and has no runtime carrier.** In `fs-layer` the app row
   `[FileSystem.Read, FileSystem.Write]` is discharged by the `with
   FileSystem` handler in `serve-files`, whose row is `[Device.Block]`,
   and `opening` carries only `[Console, Device.Block]`. The process that
   reaches the disk holds `Device.Block`, not the filesystem bits. A guard
   at `emit-effect-op-call` would test that same residual process and pass
   trivially -- **vacuous**.

The consequence: for the direction bit to mean something at runtime, the
**app must remain a process that holds only `FileSystem.Read` (never
`Device.Block`)**, and a **trusted party that is not the app** must check
the app's bit before it touches the disk. That trusted party is the
kernel.

---

## The threat model, stated plainly

The type checker already stops an *honestly compiled* `[FileSystem.Read]`
program from performing `write-file` -- the row would not type. The
runtime hole is a **binary whose manifest lies**: a hand-crafted or
tampered CDX whose declared caps say `FileSystem.Read` (so the loader
grants bit 6 only) but whose code performs a write. Nothing today reads
bit 7 to stop it. Closing 1.14 makes the grant bind the code, not just
the source.

---

## Architecture

A `[FileSystem.Read]` program holds bit 6 and **not** bit 10. It performs
`read-text` / `write-file` as ordinary `FileSystem` effect ops. With no
user `with FileSystem` in scope, the ops are serviced by a
**compiler-installed default handler** -- the servicer -- that the kernel
owns:

```
app: opening : [Console, FileSystem.Read "OK"]      (bit 6, scope "OK", NO bit 10)
  performs  read-text "OK.TXT"
     |
     v  handler-table slot for read-text  ->  DEFAULT SERVICER (kernel-emitted)
        1. check current proc has cap-filesystem-read     (process-get-cap; deny -> "")
        2. check scope admits the path                    (process-get-scope prefix)
        3. ELEVATE: set fs-elevated cell                  (kernel block authority on)
        4. fat16-read-text (fat16-init boot-part) path    (uses block syscalls 10/11)
        5. DE-ELEVATE: clear fs-elevated cell
        6. return the text (or "" on deny/absent)
```

`write-file` is the same shape against `cap-filesystem-write` and
`fat16-write-file`.

Two mechanisms make this sound:

### 1. The default handler is the only door, because the app lacks `Device.Block`

The app cannot call `block-write-sector` directly (bit 10 clear → syscall
returns `-1`) and cannot call `fat16-*` directly (they are
`[Device.Block]` and the row would not type / the grant is absent). Its
**only** route to a sector is the servicer, and the servicer checks bit
6/7 first. That is the chokepoint the backlog said did not exist; this
campaign builds it.

### 2. Elevation: the servicer drives the disk without the app holding `Device.Block`

The servicer reuses `fat16-*` wholesale, and `fat16-*` bottoms out in the
`cap-block-device`-gated block syscalls against the *current process* --
which is the app, without bit 10. So the block-syscall gate is widened:

> `emit-block-read-syscall` / `emit-block-write-syscall` proceed when the
> current process holds `cap-block-device` **OR** the kernel `fs-elevated`
> cell is non-zero.

The servicer sets `fs-elevated` immediately before the `fat16-*` call
(after bit 6/7 and scope are verified) and clears it immediately after,
on every path including the deny/absent path. `fs-elevated` is ambient
authority, deliberately contained: it is written only by kernel-emitted
servicer code, only around one `fat16-*` call, only once the app's FS cap
and scope have been checked. It is a save/restore (not a bare set/clear)
so a re-entrant servicer call -- a handler that itself performs an FS op --
does not clear elevation out from under its caller.

**Alternative considered and rejected for the first cut:** factor the raw
ATA read/write core out of the block syscalls into an ungated helper the
servicer calls directly, so no ambient cell exists. Cleaner in principle,
but it forks the sector primitives or forces a second `fat16` that uses
the raw path -- far more surface for a first increment. Revisit if
`fs-elevated` proves fragile.

---

## What already exists and is reused

- `process-get-cap` (runtime helper, `X86_64Helpers.codex`) -- reads the
  current proc's capability word. The servicer's bit-6/7 test.
- `process-get-scope` (runtime helper) -- reads the current proc's scope
  string; the boot ring (1.5) already writes it at `proc-scope-offset`
  (=64) and `process-spawn` copies it. `fat16-scope-admits` already uses
  it. The servicer's path check reuses `fat16-scope-admits` (do **not**
  re-derive the prefix relation -- 1.4's lesson).
- `emit-check-capability` (`X86_64Boot.codex:2237`) -- `bt`-tests one cap
  bit of the current proc, answer in CF. Used by the block syscalls and
  the widened gate.
- `fat16-read-text` / `fat16-write-file` / `fat16-list-root` -- the disk
  logic, unchanged.
- Handler-table machinery (`emit-push-handler-slots`,
  `emit-store-to-handler-table`, `find-effect-op-addr`,
  `emit-effect-op-call`, `X86_64.codex` / `X86_64Compound.codex`) -- the
  slots the default closures install into and that a user `with`
  overrides.

## What is new

- **`fs-elevated` kernel cell = 36224.** Verified free: `codex-vm.c` uses
  36152 (reserved output-ring trap), 36160/36168 (blit cells), and the
  guest uses 36200/36208/36216 (AP dispatch / uefi-systab / AP preempt).
  36224 is the next slot above `ap-preempt-count-addr` and is untouched by
  the host. Define beside the other cells in `X86_64Boot.codex`.
- **The default servicer closures** for `read-text` and `write-file`,
  emitted at init and stored into the two handler-table slots so a bare
  op reaches them. Written so a user `with FileSystem` still pushes over
  them and pops back to them.
- **Widened block-syscall gate** honoring `fs-elevated`.

## Two facts that shape the servicer emission (from reading the handler machinery)

- **Handlers here are strictly tail-resumptive, one-shot, and compile to
  a plain call.** `emit-handle-one-clause` (`X86_64.codex:1162`) builds a
  `resume` trampoline that is literally the identity (`mov rax, rdi;
  ret`), binds it, and stores the clause body's value -- a lambda over the
  op's args -- into the slot. `emit-effect-op-call` then calls that lambda
  with the op args and takes its return as the op's result. **So the
  default handler needs no continuation capture:** it is just a callable
  `\path -> <result>` (and `\path content -> <bool>`), installed as a
  closure whose `[0]` is the servicer code address. A user `with` overrides
  by pushing the slot (`emit-push-handler-slots`) and popping it back.

- **`fs-elevated` must NOT be reachable as a builtin or callable.** If
  `__fs-elevate` were an ordinary builtin, any program could call it and
  then `block-write-sector` and bypass every gate -- a universal capability
  bypass. Therefore the set/clear of `fs-elevated` is **inline machine
  code emitted only inside the compiler-generated servicer body**, never a
  named function in `fo-names`, never in any builtin table, never
  callable. The servicer body is hand-emitted (or a specially-emitted
  compiler function) precisely so elevation has no callable surface. This
  is the load-bearing security property of the whole design: the app's
  only door to the disk is the servicer, and the servicer is the only
  holder of elevation.

---

## Increments (each its own CL, each gated)

1. **Elevation gate. [SOURCE-COMPLETE 2026-07-14]** Added `fs-elevated-addr`
   = 36224 and `emit-block-elev-gate` (returns `CheckExpiryResult`), and
   routed all four block syscalls (read 10, write 11, sector-count 12,
   select 13) through it -- they now proceed on `cap-block-device OR
   fs-elevated`. Verified register-safe: the gate only clobbers `rax`,
   which every disk path overwrites before reading (`emit-ata-setup-lba`
   opens with `li rax, 224`), and `rdi`/`rsi` (LBA/buffer) are untouched.
   **Inert until increment 2** (cell is 0 at boot), so it is not landed
   alone -- it ships with the servicer, whose test demonstrates it.

2. **Default handler installation.** Concrete plan, verified feasible:

   - **Scope is already enforced by fat16.** `fat16-scope-admits` runs
     inside `fat16-resolve-path` / `fat16-create-file` / the root-listing
     pair (the boot ring). So the servicer adds **only** the direction
     check + elevation; it does not re-check scope.

   - **Two Codex workers** (ordinary `[Device.Block]` functions, e.g. in
     `Fat16.codex`): `fat16-servicer-read (path) = when fat16-read-text
     (fat16-init fat16-boot-partition-start) path is Just t -> t; None ->
     ""` and `fat16-servicer-write (path) (content) = fat16-write-file
     path content`. Nothing special -- they are the fat16 work the handler
     would otherwise inline.

   - **Two hand-emitted servicer stubs** (runtime helpers, NOT in
     `fo-names`/builtin tables -- the elevation must have no callable
     surface). Each: `emit-check-capability cap-filesystem-{read|write}`
     → on deny return the empty answer (`""` for read via an empty-Text
     constant; `xor rax,rax` = `False` for write); else set `fs-elevated`
     = 1 (inline `mov [fs-elevated-addr], 1`), `emit-call-to` the worker
     (path already in `rdi`, content in `rsi`), save `rax`, clear
     `fs-elevated`, restore `rax`, `ret`.

   - **Install** at init (near `assign-effect-op-addrs`,
     `X86_64Chapter.codex:1200`): for each of `"read-text"`/`"write-file"`
     that `find-effect-op-addr` resolves to a slot, allocate an 8-byte
     closure whose `[0]` is the stub address and store the closure pointer
     into the slot. A user `with FileSystem` still pushes/pops over it.

   **Footguns to respect:** (a) **stack alignment** -- the stub calls a
   non-leaf Codex worker; keep RSP 16-byte aligned at the `call` (the
   op-call path + the stub's own push/pop must net to alignment, or the
   worker's SSE spills fault). (b) **`emit-check-capability` clobbers
   rcx/r11 (saved/restored) and reads the current proc** -- confirm `rdi`
   (path) survives it; it does (only rcx/r11 touched). (c) the empty-Text
   constant for the read deny path -- reuse whatever `process-get-scope`
   returns as its static empty (it "never answers null").

3. **Enforcement tests + docs. [DONE 2026-07-14]** `codex/test/fs-servicer`
   pins the main-process round-trip; `codex/test/fs-spawn-inherits` pins
   the servicer surviving a fork (a child inherits `FileSystem.Write` and
   reaches the disk through the servicer with no `Device.Block`). The
   **denial** twin from the Tests section below is deliberately NOT added:
   `write-file` is an effect op, so an out-of-scope or wrong-direction
   literal is a compile error (CDX4002 / row rejection), not a runtime
   refusal -- an honestly compiled program cannot exercise it, and the
   runtime scope path is already pinned by `scope-runtime-deny`. The stub
   checks `cap-filesystem-{read,write}` before it elevates, so the deny
   path exists and is correct by inspection (see the residual note above).
   BACKLOG 1.14 removed (closed). (KingsAndCourts carries no specific
   filesystem-enforcement claim to correct -- it speaks of capabilities by
   construction generally, which remains accurate.)

The increments may collapse if 1 cannot be demonstrated without 2; in
that case land 1+2 together and keep the internal set/clear out of any
reachable surface.

## Built and validated (2026-07-14)

Increments 1 and 2 are implemented and validated **as a self-compiled
fixed point** -- the seed rebuilt from this source (`build/build.ps1`)
round-trips byte-identically and passes the BVT, so the servicer is in
the compiler that compiles itself, not only a one-pass dev SUT. (The
earlier interim note used dev SUT B7A4F972 before the fixed point was
taken.)

- **`codex/test/fs-servicer`** -- a program with `[Console,
  FileSystem.Read "OK", FileSystem.Write "OK"]` and **no Device.Block**
  performs `write-file` then `read-text` as bare ops. Output: `write
  True` / `read hello servicer`. The write landed and read back through
  the kernel servicer with the app holding no block authority -- proving
  the default handler installed (no null-slot crash), the direction bits
  were checked and passed, elevation let the block syscalls through, and
  fat16 round-tripped. Deterministic across runs.
- **No regressions**: `fs-layer` (own `with FileSystem` handler + a
  `Device.Block` layer), `scope-runtime-open` / `scope-runtime-deny`
  (direct fat16 + `Device.Block`), and `fat16-write` all still PASS -- the
  block-gate widening and the boot-time slot seed leave the
  `cap-block-device` path and the user-handler path untouched.

**What the test does NOT cover, stated plainly (as 1.5 did):** the
direction *denial* -- a `[FileSystem.Read]`-only program's write being
refused -- cannot be exercised by an honestly compiled program, because
`write-file` is an effect op and a read-only row that performs it is a
compile error (CDX4002 fires on an out-of-scope literal; the effect row
rejects the op outright). The runtime bit test in the stub is
defence-in-depth against a binary whose *manifest* and *code* disagree (a
forged or restricted-grant CDX); closing that honestly needs a spawn/load
that grants a subset of the code's declared caps -- a harness this CL does
not build. The stub does `emit-check-capability cap-filesystem-{read,
write}` before it elevates, so the deny path exists and is correct by
inspection; the positive path is battery-pinned by `fs-servicer`.

---

## Tests (the enforcement, not the grant)

The existing `cap-direction` proves the *grant* is right; this campaign
needs the *enforcement*. Model on `scope-runtime-open`/`-deny`: same code,
one declaration apart, so a denial is not confused with a broken
filesystem.

- **fs-read-only-denies-write** (the 1.14 poster child): `opening :
  [Console, FileSystem.Write "..."]`? No -- declare `[Console,
  FileSystem.Read "OK"]` and **no `Device.Block`**. `read-text "OK.TXT"`
  succeeds; `write-file "OK.TXT" ...` is **refused** (bit 7 absent). The
  refusal must be legible (the op returns `False` / a sentinel, not a
  crash).
- **fs-write-lands**: `[FileSystem.Write "OK"]` -- the write lands and
  reads back, proving the servicer is not simply denying everything.
- **fs-scope-holds**: out-of-scope path refused even with the right
  direction bit (scope reuse pinned).
- **direct-block-still-gated**: a program without `Device.Block` calling
  `block-write-sector` directly still gets `-1` -- elevation did not leak.
- **fs-spawn-inherits**: a spawned child performs the FS op and the cap +
  scope came with it (mirror `scope-runtime-spawn`: the child attempts
  **both** directions and reports `10*in + out` so a wrong-reason pass is
  visible).

## Memory / time-complexity verdict (to fill per CL)

Elevation is one cell write/read per FS op -- O(1), no allocation. The
servicer allocates exactly what `fat16-*` already allocates (one 512-byte
sector buffer per `block-read-sector`, bump-freed with the op). No new
retention across phases. State it explicitly in each CL.

---

## Confirmed feasible

- **The slots already exist.** `effect-op-names` is collected from every
  op in the cited `effect` *definitions* (`Lowering.codex:1187,1195` --
  `for op in (e.ops)`), not only handled ops. `assign-effect-op-addrs`
  (`X86_64Chapter.codex:1211`) gives each a slot at
  `handler-table-base-addr + i*8`. So whenever a program cites
  `FileSystem`, `read-text`/`write-file` have slots regardless of any
  `with`. The default install stores the servicer closure into the slot
  `find-effect-op-addr (st.effect-op-addrs) "read-text"` returns -- no new
  slot allocation needed.

## Open questions to resolve during increment 1/2

- **When are the default closures installed?** Find the init point that
  seeds the handler table (near where `effect-op-addrs` is built,
  `X86_64Chapter.codex:1200`) and emit the servicer bodies + the stores
  there. Install only for ops that resolved to a slot (i.e. only when
  `FileSystem` is cited), so a program that never mentions the effect
  pays nothing.
- **Re-entrancy of `fs-elevated`.** Save/restore vs flag; a servicer that
  calls a servicer (unlikely but must not corrupt). Save/restore chosen
  above; confirm no path clears it early.
- **`fat16-init` cost per op.** The servicer calls `fat16-init
  boot-part` per op (as `scope-runtime-*` already do). Acceptable; note it.
- **Interaction with a user `with FileSystem`.** A program that *does*
  provide its own handler (the `fs-layer` model) must be unchanged -- the
  push overrides the default slot, the pop restores it. Verify `fs-layer`
  still passes.
