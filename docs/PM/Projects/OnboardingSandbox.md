# The Open Door: A Cloud Sandbox for Onboarding

**Prepared**: 2026-08-24
**Status**: Proposed. Awaiting fleet pickup. No code written yet.
**Owner**: unassigned (needs a lane)
**Decision needed from Damian**: approve the recommended path (Part IV) and
the Phase 0 gating test before any cloud spend.

---

## Executive Summary

Onboarding a new developer to Codex is a problem of establishing trust
*before* they trust the code. Asking a newcomer to boot an unknown
kernel on their own hardware -- even from a USB stick on a spare box --
is a psychological bar most will not clear. The ask "boot a kernel on
your PC" and the ask "run it in a throwaway VM" are different asks; the
second is one every developer already makes without fear, and it is the
one we should be making.

This project builds a **cloud-hosted sandbox where a prospective
developer can watch Codex boot and drive its desktop from inside a
browser, touching no local install and no local hardware.** The
recommended substrate is **QEMU booting `Codex.img` (the GopBoot
payload) headless with a VNC display, fronted by noVNC** so the only
client a developer needs is a web browser. Because the display is
browser-delivered, the host being Linux while our dev environment is
Windows stops mattering -- the developer never sees the host OS.

The plan is gated on one **local, free** test (Phase 0): does the
interactive `works` desktop actually paint and accept mouse/keyboard
under QEMU + OVMF + VNC on a Windows dev box? Everything downstream --
provider choice, multi-tenant spawning, cost -- is mechanical once that
question is answered yes, and cheap to abandon if the answer is no. We
spend nothing on cloud infrastructure until Phase 0 passes.

A parallel, zero-VM "first touch" (Tier 0) hosts the self-contained app
HTML pages we already emit, so a curious visitor can click through real
Codex applications in a browser with no VM at all.

---

## Part I: The Problem

### 1.1 The trust barrier is psychological, not technical

The reference machine has booted from bare UEFI, compiled its own source
off a USB stick, and written it back byte-identical. That is a
remarkable proof of the system -- and it is exactly the wrong first
experience to hand a newcomer. "Put this on a boot stick and restart
your machine into it" reads as a request to surrender the machine to an
unknown OS. The barrier is not that the software is unsafe; it is that
the *request* sounds unsafe. We need a first experience that is
obviously reversible and obviously sandboxed.

### 1.2 The environment is Windows-centric by design

Development happens on Windows with PowerShell. This is deliberate and
not up for renegotiation here (the `.sh`/WSL indirection was removed on
purpose). Almost every cloud VM service assumes a Linux guest, which is
the friction the developer already ran into with Oracle's free tier.
Any plan must not smuggle a Linux/WSL substrate back into the dev
workflow. The cloud sandbox is a *distribution and trial* surface for
newcomers, not a change to how we build.

### 1.3 The chicken-and-egg

Our product is a bare-metal OS. The cloud VM services on offer are built
to run Linux application workloads, not to host a guest OS someone
watches boot. We are an unusual tenant for every one of them. The plan
below resolves this by choosing the one portable, well-understood
substrate that treats "a guest OS someone watches boot" as its native
job: QEMU.

---

## Part II: Constraints From the Code (read before designing)

These are load-bearing facts an implementer must internalize. They are
established from the source, not assumed.

### 2.1 `codex-vm` is a Windows-only development tool, not a portable emulator

`tools/codex-vm.c` is built on the **Windows Hypervisor Platform**
(`WHvCreatePartition`, `WHvRunVirtualProcessor`, `WHvMapGpaRange`),
draws its window with **Win32** (`CreateWindowA`), and rasterizes
through **CUDA** (`nvcuda.dll`). It therefore requires Windows, hardware
virtualization exposed to it, and (for the fast display path) an NVIDIA
GPU. It exists so development is fast and pleasant on Damian's Windows
box. **It is not a candidate for the cloud sandbox** and must not be
treated as one. Do not attempt to port it or run it on Linux.

### 2.2 QEMU is the portable substrate, and it is already in the tree

`Codex.elf` (multiboot) and `Codex.img` (bootable UEFI image) boot under
QEMU today. `CODEX_VM_HOST=qemu` forces the standard harness onto QEMU;
`build/boot/diag-arm.ps1` already drives QEMU + OVMF; `build/boot-arm64.ps1`
is a clean example of a bounded QEMU boot with serial capture. QEMU has a
built-in VNC server, so a headless host can stream the framebuffer to a
remote browser with no extra display plumbing. **This is the substrate.**

### 2.3 The display path that works under QEMU is GOP-paint, not the text console

Per `docs/UsersHandbook.md` (UEFI firmware compatibility) and the OVMF
diag harness:

- **GopBoot paints the GOP framebuffer directly and flew 2026-08-05.**
  The OVMF diag asserts `scene=rendered, gopmode=honoured`. Graphics to
  a framebuffer under QEMU + OVMF is real.
- **The Dev Console text path has no ConOut path** -- "output goes to
  COM1 and nowhere else." A person watching a QEMU window sees nothing
  from it. `DevConsoleBoot` as a graphical payload still black-screens
  and reports OUT OF MEMORY under OVMF (open item **WORKS-5** in
  `apps/works/works-backlog.md`).
- **Therefore the sandbox must boot a GOP-painting payload**, not the
  text console. This sidesteps the ConOut gap entirely.

### 2.4 A green `codex-vm` boot is NOT evidence about QEMU or real firmware

`docs/UsersHandbook.md` states it directly: "codex-vm's default UEFI
emulation is more permissive than edk2 -- a green codex-vm boot is not
evidence about firmware." Every claim in this project must be validated
under QEMU + OVMF (or `codex-vm -uefi-strict`), never under default
`codex-vm`. This is the single most likely way the project fools itself.

---

## Part III: Options Considered

| Option | Substrate | Host OS | Display to dev | Free/cheap? | Verdict |
|---|---|---|---|---|---|
| **A. `codex-vm` on a cloud Windows VM** | WHP | Windows + nested virt | RDP (native Win32 window) | No (Azure Dv3+ only) | **Rejected as primary.** Highest fidelity, but requires a paid Windows VM with nested virtualization, and hands the dev an RDP client, not a browser. Keep in pocket for committed contributors. |
| **B. QEMU on a cloud Linux VM + noVNC** | QEMU (GopBoot) | Linux (KVM or TCG) | Browser (noVNC) | **Yes** | **Recommended.** Portable, browser-only display, host OS invisible to the dev. TCG runs free anywhere (slow); KVM runs fast where nested virt is available. |
| **C. Browser-only, static app HTML** | none | n/a | Browser | **Yes** | **Adopt in parallel as Tier 0.** Not "the OS running," but real Codex app UI with zero VM. Lowest possible bar for first contact. |
| **D. Cloudflare `computer` (the sister repo)** | Cloudflare Containers/DOs | Linux, ephemeral | n/a | n/a | **Rejected for this use.** Containers are Linux, ephemeral, request-scoped, no nested virt; cannot run `codex-vm` and are not built to hold a long-lived interactive VNC session. `computer` stays useful for headless CI and web-app hosting -- a separate consideration, not this project. |

### 3.1 Why B over A

A gives a developer the exact thing Damian sees, but at the cost of a
paid Windows VM (nested virtualization is required for WHP, and only
larger paid SKUs expose it) and an RDP client. B gives a developer a
browser tab. For the *trust-building first experience*, "open this link"
beats "install an RDP client and connect to a Windows box" decisively.
Fidelity we can add later for people who are already in; the first door
should be the widest one.

### 3.2 Provider survey (verify current specs at implementation time)

Cloud offerings drift; treat this as a starting map, not gospel. The
implementer must re-confirm nested-virt support and pricing before
committing.

| Provider | Fit | Note |
|---|---|---|
| **Oracle Free Tier (Ampere A1)** | TCG only | Always-free, but ARM + Linux. Cannot run x86 KVM. Can run `qemu-system-x86_64` in **TCG** (cross-arch emulation, slow) -- this is the trick the earlier attempt was missing. Free forever, "kick the tires" speed. |
| **Hetzner** | KVM or TCG | Cheap x86; nested virt available on dedicated and some cloud plans. Best steady-state price/performance if KVM is confirmed. |
| **Azure Dv3+** | KVM (Linux) or Option A (Windows) | Nested virt on Dv3+ (B-series does NOT expose it). The only clean home for Option A. Not free; trial credit covers early experiments. |
| **DigitalOcean droplets** | TCG only | Linux, no nested virt on standard droplets. TCG only, same as Oracle but not free. |

Recommendation: prove Phase 0 locally, then stand up **one Hetzner
instance** (KVM if confirmed, else fall back to Oracle-free TCG for a
zero-cost demo). Do not evaluate providers further until Phase 0 passes.

---

## Part IV: The Recommended Plan

### Phase 0 -- The gating test (LOCAL, FREE, do this first)

Boot `Codex.img` (GopBoot payload) under **QEMU + OVMF, headless, with a
VNC display and a USB HID device**, on the Windows dev box, and connect a
VNC client. Mirror `build/boot-arm64.ps1` structure but x86_64 + OVMF +
GopBoot; use the existing `qemu-system-x86_64.exe`.

**Acceptance criteria:**

1. The `works` desktop *paints* over VNC (not just serial output).
2. The desktop takes **mouse and keyboard** delivered by QEMU
   (`usb-tablet` / `usb-kbd`) -- i.e. the guest's xHCI HID driver binds
   QEMU's USB HID. The OVMF diag shows xHCI running, so this is
   plausible but unconfirmed.
3. Validated under real OVMF, not default `codex-vm` (see 2.4). Attach
   BOTH serial ports for diagnosis (`docs/UsersHandbook.md`).

**If yes:** the cloud steps are mechanical. **If no:** we have found the
real blocker -- GopBoot's interactive desktop and/or xHCI-HID-over-QEMU
-- before spending anything, and it becomes a concrete work item with
standalone product value (the same "renders on unseen firmware" muscle
Track A/the diagnostic stick cares about). Either outcome is a win;
this phase is pure information.

**Deliverable:** `build/boot-x86-vnc.ps1` (or similar) that a human can
run to reproduce the boot, plus a short findings note appended to this
doc recording what painted, what took input, and any gaps.

### Phase 1 -- Portable, headless boot script for a Linux host

Package the Phase 0 QEMU invocation as a Linux-runnable form (shell or a
small container recipe) that boots `Codex.img` with `-vnc`,
`-device usb-tablet`, OVMF pflash, and bounded resources. Front it with
**noVNC** so the display reaches a browser. No provider yet -- prove it
on any local Linux (or WSL as a throwaway *test target*, never as dev
substrate). Keep KVM optional: the script must also run under TCG so it
works where nested virt is absent.

**Acceptance:** same desktop, same input, reached from a browser tab
pointed at noVNC.

### Phase 2 -- One reference cloud instance + onboarding flow

Stand up a single instance on the chosen provider (Part 3.2). Document
the newcomer flow end to end: a URL, what they see, how to reset it.
Decide the reset model (see Phase 3). Confirm nested virt / KVM speed on
real hardware; fall back to TCG for a zero-cost demo tier if needed.

**Acceptance:** a person who has never seen Codex reaches a working
desktop from a link, with no install and no local hardware.

### Phase 3 -- (Optional) Per-developer instances and lifecycle

If the single shared instance proves the concept, add per-developer
spawn/teardown, idle timeout, and a cost ceiling. This is where a thin
control plane lives (spawn a QEMU+noVNC unit per session, tear it down
on idle). Keep it dumb: the earlier `Cloudflare computer` option is
explicitly out; a few lines of shell or a minimal orchestrator beats a
platform here.

### Tier 0 (parallel, independent) -- Zero-VM first touch

Host the self-contained app HTML pages the HTML plug already emits
(`docs/TheShimmeringPortal.md`; `build/build-apps.ps1`) on a static host
so a visitor can click through real Codex apps in a browser with no VM.
This is the widest possible door and has no dependency on Phase 0. It
can ship immediately.

---

## Part V: Risks and Open Questions

1. **Interactive desktop under QEMU is unproven (Phase 0 risk).** The
   GOP framebuffer paints (GopBoot flew); whether the full interactive
   `works` desktop comes up and takes input over a virtual display is
   the untested crux. This is why Phase 0 exists and is free.
2. **Input over VNC** depends on the guest xHCI HID driver binding
   QEMU's `usb-tablet`/`usb-kbd`. Plausible (xHCI runs under OVMF) but
   must be confirmed.
3. **WORKS-5** (DevConsoleBoot black screen + OOM under OVMF) is open;
   we route around it by using GopBoot, but an implementer should read
   it to understand the ConOut gap.
4. **TCG is slow.** A cross-arch TCG boot on Oracle-free ARM is a
   "kick the tires" experience, not a smooth one. KVM (Hetzner/Azure
   Dv3+) is the real experience; budget for it if the demo lands.
5. **`codex-vm` permissiveness (2.4)** is the standing trap: never
   accept a default-`codex-vm` green boot as evidence for the QEMU path.
6. **Provider specs drift** -- nested virt support and pricing must be
   re-verified at implementation time (prepared knowledge predates the
   build date).
7. **Cost/abuse of public instances** -- a publicly reachable VM that
   anyone can drive needs an idle timeout and a spend ceiling from day
   one (Phase 3), or a gated link.

---

## Part VI: Non-Goals

- **Not** replacing or porting `codex-vm`. It stays the Windows dev tool.
- **Not** changing the Windows/PowerShell dev workflow or reintroducing
  WSL/`.sh` as a build substrate.
- **Not** hosting the Codex kernel as a *production runtime* on anyone's
  cloud. This is an onboarding/trial surface only.
- **Not** using Cloudflare `computer` for the interactive sandbox
  (rejected in 3.0; it remains a candidate for headless CI and web-app
  hosting under a separate initiative).

---

## Part VII: Pointers for the Implementing Agent

Read before touching the subject (honor the on-demand reading contract):

- `tools/codex-vm.c` -- confirm the WHP/Win32/CUDA binding (2.1). Do not
  plan around running this on Linux.
- `docs/UsersHandbook.md` -- "UEFI firmware compatibility" / QEMU + OVMF
  section: GopBoot vs Dev Console, ConOut gap, the four boot fixes, the
  `-uefi-strict` and dual-serial guidance (2.3, 2.4, Phase 0).
- `docs/OperatorsManual.md` -- `CODEX_VM_HOST=qemu`, the QEMU boot path,
  `kernel-irqchip=off`.
- `build/boot-arm64.ps1` -- the clean bounded-QEMU-boot pattern to mirror
  for the x86 VNC script (Phase 0 deliverable).
- `build/boot/diag-arm.ps1` -- the existing OVMF harness (`scene`,
  `gopmode`, `xhci` stages); the shape of "prove it under OVMF."
- `apps/works/works-backlog.md` -- WORKS-5, the ConOut/OOM item.
- `docs/TheShimmeringPortal.md` and `build/build-apps.ps1` -- the app
  HTML output path for Tier 0.

## Definition of Done

- Phase 0 findings recorded in this doc (pass or fail, with specifics).
- If Phase 0 passes: a documented URL a newcomer can open to reach a live
  Codex desktop in a browser, with a reset story and a cost ceiling.
- Tier 0 app pages reachable at a static URL, independent of Phase 0.

---

*This file is a new, untracked addition; `p4 add` it before submit.
It captures the plan and the options considered as of 2026-08-24 so a
fleet agent can carry it. It is a north star for the effort, not a
specification -- update it as Phase 0 teaches us what is real.*
