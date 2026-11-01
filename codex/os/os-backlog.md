# OS quire -- open capabilities

Quire-domain backlog for `codex/os/**` (kernel, net, observe). The shape and
priority order for the platform live in `docs/PM/CurrentPlan.md`; anything
that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

Where an image already lives at an address, `verify-cdx-full-at` and
`evaluate-load-at` (`codex/os/verify`) verify it without the eight bytes per
byte a list costs; OTA Gate B (`ota-lwm2m-loopback`) does, and
`codex/test/apps/verify-cdx-at` holds both paths to the same verdicts. Every
other caller holds its image as a `List Integer` at the source (`BinaryStore`
through `CmdInstall` into `ShellCore exec-install` and `ProgramRegistry`,
`ShellDispatch dispatch-verify`, the loader arms), and that is not a gap: the
list is the source's own form, so the address path would only add a copy, and
a `BinaryStore` of buffers is a refactor nothing asks for (root, 2026-09-24,
L-LESS). Both paths stay.

`HttpClient` (`codex/os/net/HttpClient.codex`) keeps no response headers. Nothing
needs them at head: `apps/browser/PageFetcher` reads its own header block from the
raw bytes (2026-09-29).

Test coverage of `codex/os` (`pwsh build/coverage-census.ps1 -Tree codex/os -List`,
2026-10-31): all 179 library chapters are reached by a computing test.

- **USB HID gamepad driver** (Damian approved the project, 2026-10-31; taken: reek).
  `apps/works/GopUsbPad.codex` parses a HID report descriptor into a gamepad layout
  (buttons, axes, hat, one Report ID) and decodes reports without allocating, graded by
  `codex/test/apps/usb-pad-report`. codex-vm `-hid-pad` adds the gamepad as interface 2
  of the combo device (EP3 IN, DCI 7; its report descriptor is the test's descriptor A,
  answered for 0x22 with `wIndex` 2) and `-pad-file` drives it. `usb-hid-walk`
  (`apps/works/GopUsb.codex`) reads the report descriptor of every subclass 0 protocol 0
  HID interface (an interface GET_DESCRIPTOR, bmRequestType 0x81) and binds the first
  that parses as a gamepad as `ua-pad` (`pad-pump`, `pad-buttons-now`, `pad-axis-now`,
  `pad-hat-now`); any other stays a raw keyboard. Graded by `codex/test/apps/usb-hid-pad`.
  The desk carries it as `DeskApps.da-pad`, from a `desk-run` argument (`GopBoot` threads
  `att.ua-pad` through `boot-screens`, `run-menu` and `menu-loop`; `DeskVm` passes it directly);
  `desk-loop` pumps it beside `desk-input-step`, and the Monitor's input row reads
  `pad buttons 0x...` (`codex/test/apps/desk-pad-monitor`; under `-hid-pad` the row survives a
  Monitor close and reopen). No pane reads the pad beyond the Monitor. Open: on metal, a device
  that stalls the interface GET_DESCRIPTOR (HID 1.11 makes it mandatory) halts EP0 before
  the raw-keyboard fallback, which no bed has shown.
- `Kernel` OtaBoot boot path: no board boot calls `boot-run` (`codex/foreword/core/OtaBoot.codex`), so the A/B selector, its
  attempt limit and its rollback run only under `codex/test/apps/ota-boot-rollback` and `ota-confirm-boot`. A board's boot
  path calls `boot-run` with its record address, both slot addresses and its slot size; until one does, the compliance
  rows (ComplianceEvidence ETSI 5.4-4, NIST 8259A-5) say the boot side has no production caller.
- `Kernel` Arm64Boot: boots on QEMU `virt,gic-version=3` under `codex/test/arm64-gicv3-tick` (`build/test-cross-disk.ps1 -Arch arm64 -Test arm64-gicv3-tick`), which supplies the GICv3 distributor and redistributor init, VBAR_EL1 and the DAIF unmask beside the chapter; the timer IRQ advances the tick count. `arm64-irq-handler-code` acknowledges through GICv3 system registers (`ICC_IAR1_EL1`, `ICC_EOIR1_EL1`), which no Raspberry Pi has (a Pi 4's GIC-400 is GICv2; a Pi 3 has no GIC). `arm64-irq-handler-code-gicv2` takes the GICC base and acknowledges through 32-bit GICC_IAR and GICC_EOIR accesses, graded on QEMU virt's default GICv2 by `codex/test/arm64-gicv2-tick` in the arm64 cross batch. `codex/test/arm64-pi4-tick` boots the GICv2 path on QEMU `raspi4b` (GICD #FF841000, GICC #FF842000) to the same tick, compiled with `compile-arm64.ps1 -Raspi4b`: UART0 at #FE201000, the Device.Mmio gate's RAM ending at #80000000, cores 1-3 parked, EL3 dropped to secure EL1. Open: a metal Pi 4 run, Damian's body. The handler reloads CNTP_TVAL_EL0 with CNTFRQ_EL0, one tick a second after the first. The timer branch's fixed 28-byte skip holds only while `arm64-li` loads `arm64-ram-base + arm64-tick-count-addr` in one `movz` (true for #40080000). `codex/test/apps/arm64-boot-encoding` pins the table and handler words to hand-written Arm encodings.
- `Kernel` arm64 virtio trusts device values (Deferred: cross outputs are secondary, Damian 2026-10-31; root ruled the rows here). `codex/os/kernel/VirtioPci.codex` takes queue_size (common cfg +24) with no non-zero, power-of-two or upper bound and zeroes about 26 x qsize bytes into a 64 KB queue slot (`:178-186`), takes a used-ring id and a descriptor `next` as the next free head with no `< queue-size` check (`:219-224`, `:276-282`), uses notify_off x multiplier with no capability-length check (`:194-195`), and takes a capability's bar byte and offset with no BAR index, length or range check (`:110-128`), walking `next` with no fuel (`:96-106`); `codex/os/kernel/VirtioNet.codex` indexes its 32 x 1526-byte rx buffers by used id and reads `used-len - 12` bytes with neither checked (`:98-102`, `:156-163`). The x86 twins check every one: `VirtioPciX86.codex:33`, `:40`, `:64-73`, `:147-152`, `:193-199`, `VirtioNetX86.codex:156-162`, `VirtioBlk.codex:71`.
- `Dev` DateTimeSettings: the CVMM server's Date & Time view shows the CMOS clock as read once at
  server start (`boot-datetime`, `apps/cvmm/CvmmServer.codex`). A request reads no clock and nothing
  calls `dt-tick`, because `web-serve-framed-with` takes a pure route; a live clock needs a route that
  can read a port. `apps/cvmm/CvmmApp.codex` (`[Console]` only) still renders `datetime-new`.
- `Dev` QmkProtocol: nothing reads a board's VIA definition. The lighting channel (rgblight 2 or
  rgb_matrix 3) comes from `via-channel-from-lighting` on a name the caller supplies, and effect ids
  other than Off (0) and Solid Color (1), with their names, come from the caller.
- `Net` OAuthClient has no redirect callback: `oauth-authorize-url` sends `state`
  (`codex/os/net/OAuthClient.codex`, `os-state-nonce`) and nothing reads the redirect or calls
  `oauth-exchange-code` from one. The CL that builds the callback compares the returned `state`
  with the one sent and refuses a missing or differing value before exchanging the code (RFC 6749
  section 10.12), with a test arm for each (root, 2026-11-01).
- `Net` parser gaps with no caller (root, 2026-11-01: register, do not fix until a caller exists;
  the CL that adds the caller closes the row's gap with a refusal arm):
  - ImapClient `imap-parse-response-kind` (`codex/os/net/ImapClient.codex`) finds the status with
    `text-contains " OK "` anywhere in the line, tried before NO and BAD, so `A1 NO login failed,
    try OK later` reads as ImapOk, and it never compares the tag with the command's. RFC 3501
    section 7.1: `tag SP ("OK" / "NO" / "BAD") SP resp-text`. Nothing calls it, tests included.
  - MessageFraming `frame-decode-length`, `frame-decode-tag` and `frame-decode-body`
    (`codex/os/net/MessageFraming.codex`) carry no refusal: a body shorter than its declared length
    reads as the bytes present. Only `codex/test/apps/framing-test` and `codex/test/frame-short-buffer`
    call them; the live readers `frame-decode-text` and `frame-decode-bytes` carry `valid`.
  - Tftp `tftp-receive-data` (`codex/os/net/Tftp.codex`) takes a DATA packet from any sender:
    `server-port` is never set and no port reaches it. RFC 1350 section 4: a packet from a TID
    other than the transfer's is answered with error 5 and not taken. No transfer loop calls it.
