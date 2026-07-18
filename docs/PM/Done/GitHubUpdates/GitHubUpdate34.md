# GitHub Update 34 -- 2026-07-08

Covers main CLs 7228-7355 (since Update 33 covered through 7226). One
cycle, one headline, and it is the one the whole bare-metal effort has been
building toward: **Codex boots the first-boot ceremony on real hardware,
off a USB stick, with no OS beneath it.** Around it, blu turned the
capability model from a decoration into an enforced by-construction
guarantee, and the compiler picked up two soundness fixes it needed for the
boot path.

## The headline: it boots on metal

A 1 GB USB stick, flashed with `seed/Codex.img`, booted on a real UEFI
machine and ran its own first-boot ceremony -- every pixel drawn by a Codex
program the seed compiled, after the loader handed off at
`ExitBootServices`. Confirmed on hardware: the first-boot screen renders,
the **PS/2 keyboard works**, and the whole wizard runs -- passphrase
accepted, entropy page, an Ed25519 fingerprint printed from a keypair
derived from hardware entropy. No firmware protocols, no OS, no borrowed
runtime; the payload drives the GOP framebuffer, the keyboard, and the disk
controller itself.

Getting there was a stack, built and validated bottom-up under codex-vm and
real edk2/OVMF firmware before ever touching hardware:

- **Interactive on metal (B1).** The Option A loader stub does the
  strict-clean UEFI sequence (acquire GOP, allocate and keep pages,
  `GetMemoryMap`, `ExitBootServices`, build its own 4 GB page tables), then
  hands the framebuffer to a Codex payload that reads the real PS/2
  controller (ports 0x60/0x64) after boot services die. Responsive layout
  derives every position from the panel's real mode and doubles the bitmap
  font on high-DPI screens.
- **The ceremony (B2).** A GOP text field (echo, cursor, backspace, masked
  passphrase entry) driven by the raw keyboard through the kernel's own
  layout machinery; a first-boot wizard (welcome, passphrase, entropy,
  upstream, complete); an Ed25519 identity wrapped AES-256-CBC under an
  HKDF key from the passphrase. Real entropy: the loader fills a device-seed
  cell with RDRAND (RDTSC fallback), and the text field folds a per-keystroke
  HPET sample into the key material -- so the same typed input yields a
  different key on every machine.
- **Storage (B3).** Post-`ExitBootServices` the firmware's disk protocols
  are gone, so the payload drives the controller itself: a raw IDE PIO
  driver and an AHCI DMA driver (PCI discovery, bus-master enable, one
  `READ`/`WRITE DMA EXT` per run), a GPT + FAT16 reader mounting the ESP,
  and multi-sector transfers so a 2 MB read is one command, not 4,000. The
  machine reads its own 2.1 MB seed back off the stick and runs the
  **WakeCeremony** over it -- recomputes the content hash, checks the author
  signature, and speaks only what it verified or refuses to act. That is
  Ascent V rung 3: the machine proves its own toolchain, from its own
  medium, before it runs. It then writes the wrapped identity to the stick
  and, on a later boot, detects it and greets you back.

The honest edge, found by the hardware test itself: persisting to the boot
stick needs a USB mass-storage driver, because the stick is USB, which the
AHCI/IDE drivers cannot reach -- so "could not save" on real hardware is the
boot medium, not a write bug. A safety guard was added so the AHCI path only
ever writes to a volume carrying our own `CODEX.CDX` (never a random
internal disk). The USB mass-storage stack (xHCI + Bulk-Only Transport +
SCSI) is the next frontier; it is developable against codex-vm's emulated
xHCI device, and it pairs with the xHCI HID keyboard path for USB-only
laptops.

Two years of "the same stick sometimes boots" were also finally explained
and killed earlier in the cycle: a FAT12/16 cluster-count mislabel in the
image builder, plus the ASUS board's CSM/Fast-Boot config. The image now
boots on an ASUS TUF (2015 AMI), a Dell Inspiron, and OVMF.

## Capability enforcement, made real (blu)

Update 33 shipped the effect and capability *types*; this cycle made the
runtime check real. Previously the syscall capability check tested an
argument-mod-64 bit and branched on a flag that `bt` never sets -- a no-op
dressed as security. Now, stage by stage: boot grants process 0 exactly its
manifest's capability mask (an empty manifest means zero grants --
secure-by-default, literally), and the block, identity, spawn, and
capability-admin syscalls consult the real capability word with a `bt`/`CF`
test and deny with `-1`, device untouched. Gated kernel builtins carry
honest effect rows (`[Concurrent]`, `[Identity]`, `[Capability]`) so a
program earns a grant by declaring what it does; the spawn callback is
effect-polymorphic. `ProcessCaps` wires load-time grants into the process
table, runtime-proven.

The final piece landed at cycle end (main CL 7354): a **spawn-pool carve**
that fixed nested `process-spawn`. All three spawn helpers now cut a child's
heap and stack from a global pool cursor instead of the spawner's own
allocation frontier, which used to overlap a grandchild's stack. And a
diagnosis worth recording: `process-kill` had been typed pure while
runtime-gated on capability-admin, so a kill behind a spinning child
returned a silent `-1` -- which *looked* like "kill hangs the scheduler."
It was never a scheduler bug; the honest `[Capability]` row on `process-kill`
surfaced it. The last five pre-effect-rows `-Apps` process tests are now
green with unchanged expected output.

## The compiler needed two fixes for the boot path

- **Effectful-loop TCO.** `emit-act-stmts` threaded a cleared tail-position
  flag from an act-bind into the following statements, so any `act` loop
  with a bind before its tail self-call compiled the call as a real `call`
  and grew the stack -- fatal for an interactive poll loop. Fixed with the
  save/restore idiom the let/match emitters already used; the same sweep
  hardened the `handle`/`with-timeout`/`try` cleanup paths. A poll loop is
  now constant-stack (regression test `act-tco-loop`).
- **WatchdogPet mode.** The hang watchdog panics on a no-heap-progress loop
  -- exactly a poll loop. A new `pet` compile flag selects a mode that pets
  the watchdog from every prologue and every TCO loop head, so a boot menu
  can poll the keyboard forever without tripping it.

Also on the compiler: three CCE decode bugs in the foreword `Fat16` chapter,
all one family (a raw ASCII disk byte was fed to `code-to-char`, which wants
a CCE code). One of them meant every FAT name compare failed; another meant
`DISK` compile mode had been reading `SOURCE.SRC` as garbage; a third meant
no path with a `/` ever split. Fixed at the root with `from-unicode`.

## The builds got much faster

Update 33's profiler finding -- the self-compile spends ~78% of its time in
`__write_binary`, streaming the 2 MB CDX one byte at a time through a serial
port -- was acted on. blu batched that output path (a bulk-blit doorbell,
a `write-binary-buf` builtin, a staged print sink), and the wall-clock
dropped hard: a single self-compile went from **~22.5 s to ~6.5 s**, and the
full gate build (`build/build.ps1`) from **~170 s to ~90 s** -- the three
text-emitting stages, which rode the same byte-at-a-time path, halved. This
is the difference the profiler predicted, delivered; every compile-test
cycle in this cycle's boot work ran at the new speed.

## Odds and ends

- **codex-vm fidelity fixes**, all surfaced by driving real drivers against
  it: the GOP mode-info table now reports the CLI resolution (was hardcoded
  640x480); an IDE multi-sector read skipped every other sector
  (`ide_advance` double-advanced `buf_off`); an IDE single-OUT write to the
  data port was dropped entirely (only `REP OUTSW` was handled). None had
  ever been exercised before a driver issued the operation; real firmware
  was always correct, confirmed by cross-checking every read on OVMF.
- **Trust/verify domain revival (fester, CLs 7237-7265).** PolicyProse v0,
  WakeCeremony, lease-expiry boundary unification, and a CDX2051 sweep
  across ~20 OS modules revived 41 apps tests that had been skipped behind
  stale `.skip` stubs.
- **The Chlipala homework is done.** Update 33's design note
  (`PhysicalCostCodegen.md`) applied "Why Your CPU Works So Hard" to Codex
  and set one lever above the rest: the linear-types alias proof was dying
  at the checker instead of reaching codegen. blu's **NoAliasCodegen**
  campaign closed it -- WI-1 (a field-load cache in the x86 emitter, its
  invalidation narrowed by the linearity fact, then by fresh-allocation
  disjointness) and WI-2 (`VecArray`, typed 16-byte lane moves) shipped;
  the WI-3 remainder (a co-designed VM execution model) is deferred by
  ruling, with the WCET-validation slice shipped. The design doc is marked
  COMPLETE. A safety proof now buys speed, end to end -- the thesis the
  note argued, delivered. It carried its own hard lesson: the soundness
  invariant that any register write outside `alloc-temp` must evict the
  cache first, learned when the first cache-emitted compiler crashed at
  scale.
- **blu also shipped** a WCET plug accounting audit and PTX `Real`-is-f64.

## By the numbers

| Metric | Update 33 | Update 34 |
|--------|----------:|----------:|
| Copy-ups | ~6 | ~30 |
| Boots on real hardware | dev console (menu) | **full first-boot ceremony** |
| Storage post-EBS | none | AHCI + IDE PIO, GPT/FAT16 read+write |
| Capability check | typed only | **enforced (bt/CF, deny -1)** |
| Default battery (pass / fail / skip) | 304 / 0 / 15 | 321 / 0 / 15 |
| Self-compile wall-clock | ~22.5 s | **~6.5 s** |
| Full gate build | ~170 s | **~90 s** |
| Seed size | 2,112,715 B | 2,145,861 B |

The boot arc added its own regression battery: `act-tco-loop`,
`gop-text-field`, `ide-pio-read`, `ahci-encode`, `gop-fat16`, `gop-wake`,
`disk-write`, `fat-write` -- each validated against known-answer fixtures
built independently from the FAT/GPT/AHCI specs.

Seed at push time: `seed/Codex.cdx`, 2,145,861 bytes (~2.05 MB), SHA-256
`9DF129A5B46FD2AB2C5E4C03E0F11CDA932614C164975A8F733EEBAED571A26A`, content
hash prefix `9DF129A5`. Bootable image: `seed/Codex.img`, 16,777,216 bytes,
SHA-256 `45B4F41C16869FCE1CB001A6ABA2C557AE82098A6F20387DE8C5CAD1A1AB5DD0`.

## What's next

The USB mass-storage driver (xHCI + BOT + SCSI) is the one thing between the
ceremony and a stick that remembers you on real hardware -- both target
machines expose no AHCI/IDE for their USB boot medium. It pairs with the
xHCI HID keyboard for PS/2-less laptops. After that, the last ceremony rung
(B3.6): unlock the persisted identity with the passphrase on a returning
boot. Everything above storage is now proven on real silicon.
