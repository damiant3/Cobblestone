# GitHub Update 41

**Scope: main CLs 14771 onward, opened 2026-08-12.** Update 40 covered
14534 to 14770. Accumulate this cycle's themes here as they land; every
number in the final report gets re-measured at the release head, not
carried forward.

## The first outside pull request landed

**Steve Howell, PR 63 on the GitHub mirror**
(https://github.com/damiant3/NewRepository/pull/63). It is the first
contribution to this project from outside the fleet, and it landed.

`compile.ps1` and `vm-config.ps1` now fall back to QEMU when
`tools/codex-vm.exe` is absent, so the compiler can be driven from a box
that is not Windows. The design and the first working version are his.
Four tweaks were added here, each measured on Windows 11 rather than
argued:

- `CODEX_VM_HOST=qemu` forces the fallback on a box that HAS codex-vm.
  Without it the QEMU path cannot be exercised from Windows except by
  hiding the binary, and that is how it stayed dead: **measured, the
  Windows QEMU fallback in `Start-VmRun` had never booted on main.**
- The two boot gaps track the **accelerator, not the OS**. Under whpx,
  `-cpu max` wedges QEMU 11.0.0 (fixed in 11.1.0; the guard stays because
  11.0.0 still needs it and 11.1.0 only makes it redundant). The 0xFE8
  RAM-size cell is required on every accelerator on both versions.
- QEMU discovery prefers a side-loaded versioned install, newest first.
- The fallback read loop breaks when the guest process has exited. A read
  timeout and an abortive socket close arrive identically as an exception,
  so a dead guest spun the loop hot until the full deadline.

Steve also named the 0xFE8 private ABI as a compromise, and he is right:
nothing in the compiler ever writes that cell, and the multiboot header at
`X86_64IO.codex:4` does not set MEMORY_INFO, so the guest never asks the
loader the question whose answer it is being handed. Retiring the cheat is
open work, not done work.

## The QEMU signature is not a trust check, so we built one that is

`build/check-vm-differential.ps1` compiles one source through **both** VM
hosts and compares SHA-256 of the `.cdx` and the `.map`. It is a gate phase,
6s, and it skips and exits 0 on a machine with only one host.

The reason is trust rather than correctness. QEMU's Authenticode signature
cannot discriminate a good build from a hostile one: the publisher's signing
certificate expired 2023-09-12 and the binaries are still signed with it, so
verification returns the same failure whether or not that key is still in
the publisher's hands, and revocation has nothing left to revoke. **A check
that answers identically in both worlds carries no information.** Two hosts
that share no code, no accelerator and no author agreeing byte for byte
does.

It is not a proof. One sample, one source file, and it does not cover a seed
rebuild.

## VT-x is on the metal box, and the wall behind it was not there

The desk grew a Console pane, and with it the first surface that could ask
the machine a question without a serial cable. It asked the one that had
been blocking the in-box build loop since the hypervisor was written.

**`IA32_FEATURE_CONTROL` reads 5 on the ASUS** -- lock bit set,
VMX-outside-SMX set -- against the 1 codex-vm reports. `DevHypervisor` is a
real Intel VMX hypervisor written in Codex and everything in it gates on
that bit, so it had never been able to start.

**The durable half is the instrument, not the bit.** That MSR had been
measured twice under codex-vm, on two days through two different surfaces,
and the two readings agreed with each other and were both irrelevant to the
hardware. Reproducibility is not validity when the instrument is pointed at
the wrong machine.

**The memory wall behind it was measured and is not a wall.** `vm-compile`
takes the seed as a `List Integer`, and 2.7 MB of boxed integers had been
written down as the next blocker. A `seed` console command converts the
whole of `CODEX.CDX` and reports what it cost: **2,759,577 elements, 32 MiB
of heap, under a second**, against a 512 MB arena. `list-push` appends in
place rather than copying, so the appends are linear. Read first, then
measured; both agree.

## The desk becomes a desktop

- **A Programs launcher.** The sidebar was one button per app and was full
  at thirteen on a 1024x768 screen, so the fourteenth had nowhere to go.
  Apps now live in groups -- Accessories, Productivity, Graphics, Settings
  -- and an entry answers the SCANCODE its key would have produced, handed
  to the same dispatch a keystroke takes. There is no second dispatch table
  to fall out of agreement, and adding an app is one row in a list.
- **A Clock accessory** that reads the MC146818 and can set it: hour,
  minute, second, day, month, year, inside a Status-B SET window, encoded
  the way the part currently declares (BCD or binary, 12- or 24-hour).
  **codex-vm ignores every CMOS write, so no bed run can tell a correct
  write from a broken one** -- the encoding is therefore pinned by a test
  that round-trips every hour through all four modes and checks four values
  computed by hand from the spec. The write itself waits on one boot.
- **The timezone persists**, as eight bytes on the ESP beside the identity,
  and first boot asks for it. It is a display offset and is deliberately
  never written to the part, because the top bar reads that clock directly.
- **A fresh image need not cost a wizard.** `-Identity` puts an existing
  `IDENTITY.DAT` on the image at build time. A missing path is a hard error
  rather than a silent fall back to the wizard, which would otherwise be
  discovered at the machine after a flash.

## A design language is not a colour scheme

The desk had four "schemes" and every one was a palette over the same
surfaces, which is why choosing `lcars` gave LCARS orange and not LCARS.

**The missing primitive was the bevel.** A border says where a surface
ENDS; a bevel says which way it FACES, and it is deliberately not a
four-colour border -- a border with differing sides is a border, a bevel is
a light source. A `DesignLanguage` carries corner, relief, bevel width,
padding, gap, outline and gradient alongside the palette, and answers a
Theme, so nothing downstream knows it exists. Two ship: **flightdeck**,
matte and square with the ground recessed into wells and controls standing
proud of it, and **chevy**, turquoise lacquer under chrome trim. LCARS is
defined with no relief at all on purpose, because a bevel on one of its
blocks is the fastest way to stop it looking like LCARS.

Separately, the screen stopped flashing. Every pane was clearing all of
`w` by `h` before redrawing, once per keystroke, and with no back buffer
the eye catches the cleared frame. The panel style also carried a border
and a margin, so nested layout containers stacked frames -- a pane three
deep drew three. Both are gone, and one pane was deliberately left alone:
the appearance pane must keep its clear, because changing the scheme
changes the wall colour itself.

## What this release was proved against

Four proofs, all re-measured at this head rather than carried forward:

| Proof | What it covers | Result |
|---|---|---|
| Battery | depth | 1,451 tests, 1,405 pass, **0 fail**, 46 skip |
| App sweep | breadth over the front end | 264 clean, 5 known-dirty, **0 regressions** |
| Poison build | uninitialized-field safety | **0 fail** against the 0xCD seed |
| DDC witness | trusting-trust | both arms 2,759,577 bytes, **0 differing bytes outside the signature region** |

The DDC is the only one of the four that does not take the compiler's word
for anything: a compiler carrying a Thompson trojan is a stable fixed point
too, and passes the other three.

**Three of the digests this README published were wrong, and all three
would have shipped.** The seed's SHA-256 and MD5 were a generation stale;
so was the boot image's; and so was `build/boot/deskboot.img`, which is
built with `-Kernel seed/Codex.cdx` and therefore could not possibly have
survived a seed change unaltered. Nothing checks a published digest, which
is exactly why they rot -- each one is an assertion with no runner.

## The symbol map was refreshed, and the note telling you how was wrong

`seed/Codex.map` is the artifact that silently drifts: the seed is built
`-Repl`, `-Repl` emits no text MAP block, so neither a seed rebuild nor a
copy-to-main ever touches the map. It is refreshed by compiling the
compiler source NON-repl and taking the map that build emits.

Two things about that procedure cost real time and are now written down in
`OperatorsManual.md`. The map is a **sidecar `<out>.map` file**, not a
block in the build log, so grepping the log for `MAP:` finds nothing and
reads exactly like the step having failed. And the note claimed the
non-repl binary "differs from the `-Repl` seed only in unnamed padding",
which a one-command byte diff refutes: **255,683 bytes differ.** An agent
who checks is then left believing the map is unsafe to install, when it is
fine.

It is fine because the binaries share a layout without sharing bytes. The
two embedded MAP1 tables agree on all 5,126 function names and offsets,
with one size difference in `__start`, the last function in the image. So
the check that means something is not a diff of the binaries but a diff of
the maps: every address in the text map must sit at a constant delta from
its name in the seed's embedded map, and that delta must be the load base.
Measured on the installed file, one delta of `0x100000` across all 5,126
symbols, no unmatched names. That check fails loudly on the case that
actually hurts -- a map minted from source that has moved ahead of the
seed, which resolves every backtrace to a confident wrong answer.

The byte difference itself is now `COMPILER-3`. Two modes disagreeing on
the same source is a fixed-point claim with a hole in it, and no gate can
currently see it.

## Carried in from Update 40

- **`codex/test/engine-shadow` is skipped, not passing.** val's, from the
  shadow work at 14721/14732. The sidecar states its retirement condition:
  val confirms the measured output is what the 3x3 filtered compare is
  meant to produce, re-mints the `.expected`, and deletes the skip. Until
  then `build/audit-skips.ps1` reports it REAL. **The battery's 0 fail
  depends on this skip**, so retiring it is the first thing that should
  happen this cycle.
- **A5 has no owner** and both sticks are rebuilt and bed-verified.
- **The self-reproducing quine** the DDC witness cannot catch is still
  unbuilt.
- **192 bare `print-line` sites** still need judging rather than sweeping.
