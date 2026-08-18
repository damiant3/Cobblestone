# Codex ARM64 Bare-Metal on Oracle Cloud

## Goal

Boot Codex as the OS on Oracle Cloud Infrastructure (OCI) free-tier
ARM Ampere A1 instances using "Bring Your Own Image" (BYOI). The site
serves as a showcase: "this website is served by Codex on bare metal"
-- game store, code browser, everything running on the Codex kernel
with zero Linux.

OCI ARM VMs boot via UEFI and require VirtIO drivers (virtio-blk for
disk, virtio-net for networking). The existing TCP/IP stack, HTTP
parser, and web server routing are pure Codex and architecture-
independent -- only the hardware drivers need swapping.

**Ruling 2026-08-05 (Damian): KEPT in Active.** The phase checklist below is stale in the done direction -- `Arm64PeWriter`, `Gic`, `Arm64Timer`, `Arm64Pci`, `VirtioPci`, `VirtioNet` and `VirtioBlk` exist under `codex/os/kernel/` and `codex/plugs/`, and `build/build-arm64-img.ps1` / `build/boot-arm64.ps1` build and boot the image under QEMU + edk2. The honest remainder: validate the virtio drivers under QEMU (untested per OsHardwareRoadmap's inventory), then Phase 5 -- the OCI deployment -- which has not started.

**Measured 2026-08-16 (reek), and the pipeline did not build a bootable image until that day.** Two defects sat in front of every one of these phases and neither had a complainant: `boot-arm64.ps1` dialled the PE plug on 9100 after the port block was split to 9128 (main 15990), and behind that the pe plug truncated the PE at exactly 11,200 bytes and printed `OK` (main 15992, `plugs-backlog.md` 1.16). AAVMF refused the short PE `Unsupported` and dropped to the UEFI shell, which is what anyone running this pipeline saw. With both closed, `codex/test/arm64-web-server.codex` delivers a 77,824-byte PE whose section extents end exactly at 77,824, and QEMU boots INTO it: `PCI: 3 dev=4096`, `feat=32`, `rxq=256`, `Listening on port 80`.

**Serving is not green and that is the live question.** The accept loop reports `no-conn` every iteration. VirtIO-net enumerates and negotiates and has not been shown to move a frame in either direction (L-UNCALLED's shape: the driver exists and nothing has proved the path). VirtIO-blk is untouched by this arm -- `codex/os/kernel/VirtioBlk.codex` has no caller in the boot payload, so Phase 4 is unmeasured, not passing. The upload, VCN and smoke halves of Phase 5 need Damian's OCI account and are not blocked on any of this.

**"The SYN reaches the guest and the handshake does not complete" was WRONG, and this is what a wire dump says instead.** That sentence was inferred from a connection timing out rather than being refused, which is not evidence about what crossed the link. `-object filter-dump` on the netdev captured 176 bytes over 43 seconds: TWO frames, both ARP requests from slirp (`52:55:0a:00:02:02`) asking who has 10.0.2.15, and NOTHING from the guest -- not even the unprompted ARP `opening` sends at boot before any host traffic exists. The guest never answers, so slirp never resolves 10.0.2.15, so the SYN is never delivered at all. The dump is its own positive control: it captured slirp's frames, so the silence from the guest is a measurement and not a dead instrument. **TX is broken; RX is untested, because nothing has ever been addressed to a guest the gateway cannot resolve.**

**What `codex/test/arm64-virtio-tx-probe.codex` establishes, and what it does not.** It inlines one hand-built ARP into the TX queue rather than calling `virtio-net-send-frame`, whose closing `virtio-poll-used` has a 50-million-try cap that returns exactly what a success returns; the probe's poll reports its own try count so a timeout is legible. Measured on QEMU virt, `virtio-net-pci`:

| reading | value | what it rules out |
|---|---|---|
| `pcicmd` | 7 | memory space and BUS MASTER are on, so DMA is permitted |
| `bar4raw` | `0x1000400C` | 64-bit prefetchable BAR at `0x10004000`, matching `common-base` |
| `dev-hi` / `drv-hi` readback | 257 / 1 | VERSION_1 is offered AND accepted: the device is in modern mode, not legacy |
| `status` | 15 | ACK, DRIVER, FEATURES_OK, DRIVER_OK all set |
| q1 device readback | size 256, enable 1, desc/avail/used all exactly as written | the queue is configured and the DEVICE agrees with the driver's addresses |
| avail readback | `idx=1`, `ring[0]=0` | the driver's ring writes landed in guest RAM |
| `used-idx` after notify | 0 after 2,000,000 polls | the device never consumed the descriptor |
| `isr` | 0 | and never raised an interrupt |

**Three hypotheses died, and each is recorded so nobody re-chases it.** The 12-byte header is correct, not a legacy 10, because VERSION_1 is negotiated. The notify ADDRESS is not the fault: `queue_notify_off` reads **0 for queue 0 and queue 1 alike**, and notifying at `base + multiplier` and at `base` with the queue index as the value both leave `used-idx` at 0. And the common-config region really is the device, not stray RAM -- writing `0x1234` to the read-only `num_queues` field reads back 0, not `0x1234`.

**The defect: writes to the notify region never reach the device, while writes to the common-config region in the same BAR do.** Every reading in the table above is the GUEST's side, which is why this stalled -- a driver reading back its own writes is a self-grading oracle and it passed every time. QEMU's trace events are the independent instrument.

The measurement is a COUNT, because the identity of a traced notify is not otherwise legible. The probe issues **100** notify writes (two bursts of 50, one at `notify-addr`, one at `notify-base + multiplier`). The device's notify count does not move: 8 with the bursts against 5 to 8 without, and those belong to the firmware's own boot traffic. **100 writes, zero additional notifies.**

The control that makes this a measurement rather than a silence: **common-config writes demonstrably DO reach the device.** `virtio_set_status` traces our driver's status ladder as `val 1 -> 3 -> 11 -> 15`. The capability walk is not at fault either: the probe dumps the caps as exactly `t1 bar4+0`, `t3 bar4+4096`, `t4 bar4+8192`, `t2 bar4+12288`, so `notify-base = 0x10007000` is right.

**Four more candidates are eliminated. Do not re-chase them.**

- **BAR decoding.** The notify region reads back **0**, not `0xFFFFFFFF`, so something decodes it; an unclaimed MMIO read on this bus returns all-ones. Reads work there and writes do not.
- **Page-table mapping, including the BAR-size form of the question.** `a64pe-stub-setup-page-tables-at` maps 0-4GB with **2MB blocks** (`a64pe-stub-fill-pmd`), so `0x10004000` and `0x10007000` fall inside the SAME 2MB block and cannot differ in mapping or attributes by construction. No measurement needed; it is in the source.
- **Write width.** A byte-width notify (`poke-byte`, the only width ever PROVEN to reach this device) fails identically: `used-idx` stays 0.
- **The `num_queues` poison test is NOT the control it was described as** in an earlier revision. A write that never lands also reads back 0, so that test cannot separate "device ignored a read-only field" from "write never arrived". Only the `virtio_set_status` trace proves any write reaches the device.

## FIXED 2026-08-16: the guest transmits. First frame on the wire.

root's `peek-16`/`poke-16` builtins (main 16092, seed `31A5A0FDC22F91C5`) close it. Rebuilt the ARM64 plug and the PE plug against that seed and re-ran `codex/test/arm64-virtio-tx-probe.codex`; the wire dump carries two frames:

```
pkt 1 len=42 src=52:54:00:12:34:56 eth=0x0806 arp-op=1    <- OURS, the guest's ARP request
pkt 2 len=64 src=52:55:0a:00:02:02 eth=0x0806 arp-op=2    <- the gateway's REPLY, to our MAC
```

The guest put a frame on the wire and the far side answered it. On the serial side `used-idx` advances to 1 with `waited=0`, so the device consumed the descriptor immediately, and `num_queues` now reads **3** where it read 0 all day -- the field was never being read before, exactly as the root-cause section predicted.

**Measured with NO scaffolding.** The earlier BAR-into-the-high-window experiment was removed from the probe first: `bar4raw` is back to the firmware's `0x10004000` in the low window, and the frame goes out anyway. So the fix is root's builtin alone and nothing about the BAR window or the mapping was ever load-bearing for TX.

**`queue_select` still reads back 0, and that is NOT a failure.** It read 0 when the selection was genuinely broken, which is what pointed at the cause, and it reads 0 now that TX demonstrably works. The readback is not a usable indicator either way; `used-idx` advancing and a frame in the dump are. The probe's own text said otherwise and has been corrected.

## HTTP end to end, 2026-08-16: the forwarded connection completes

`arm64-web-server` re-run against seed `31A5A0FDC22F91C5` with `hostfwd`, and the whole round trip happens:

```
loop 3 ok:napinhealth recv=183 resp=99
```

Accept, a 183-byte request read up through `arm64-net-io-recv-raw`, a 99-byte response written back, and `curl` on the host receives a well-formed HTTP reply. **That is Phase 5's last local proof: RX and TX both carry real traffic through the full stack, not just ARP.**

## SERVING, 2026-08-16: the site answers 200

```
GET /            -> HTTP 200, 948 bytes, text/html, <title>Codex</title>
GET /api/health  -> HTTP 200, {"status":"ok"}
loop 2 ok:/api/health len=11 eq=MATCH codes= 81 15 31 17 81 20 13 15 23 14 20
```

**The cause was not any of the three candidates.** `print-line-uni` was faithful, `ws-eq` was right to say `nomatch`, and `http-slice` was innocent. Instrumenting the path with its CCE code points beside the text is what found it: `/` decoded as **18 on one request and 44 on the next** where the oracle says 81, while every other character in the same string stayed correct on both. **A value that changes between requests is clobbered memory, not a conversion fault**, and that is the observation the three named suspects could never have produced.

**The defect: the VirtIO DMA regions were inside the guest's own memory.** `a64pe-kernel-base` is `0x40100000` and the stub's `text-pages` includes an 8192-page (32 MB) heap grant, so the kernel's image and heap run to roughly `0x42100000`. `VirtioNet` was DMA-ing received frames into `0x40200000-0x40232000`, squarely inside it, corrupting whatever it landed on -- here one CCE table entry, which is why exactly one character of the path was wrong and the rest survived. Moved to `0x44000000`; `VirtioBlk` moved with it to `0x44100000`, having been in the same span and having survived only because its read is a single 512-byte transfer early in boot.

**The arithmetic is now done and the answer is a number, not a warning (reek, 2026-08-17).** The top of the stub's allocation is `stack-top` = `a64pe-kernel-base` + `text-pages` * 4096, and both live in `codex/plugs/pe/Arm64PeWriter.codex` (`:38`, `:415`, `:432`) -- not in either driver, which is why neither could do it. `text-pages` is `(text-size + rodata-size + 4095) / 4096 + a64pe-pt-size / 4096 + 8192`.

Measured against the shipped `BOOTAA64.EFI`, 78,336 bytes with one 77,016-byte `.text`: `text-pages` = 8,227, `stack-top` = `#42123000`, headroom below `#44000000` = **32,362,496 bytes**.

**The guard rail is not the image size, it is the `8192` literal, and that reframes the risk.** The image would have to grow 30.9 MB, four hundred times its present text, to reach the regions. The heap grant reaches them after 7,901 more pages, so **raising 8192 to 16384 -- the obvious next value -- puts `stack-top` at `#44123000` and both drivers' regions back inside the allocation, overrunning by 1.14 MB.** That is the same silent corruption this section is about.

**A derivation on the guest side is not available and this is the reason, so nobody re-attempts it.** The guest cannot compute `text-pages`: it is known only to the image builder. The stub does set SP to exactly `stack-top` at entry (`:397-398`), but SP has descended by the time a driver initialises and rounding it up is unsound -- once the stack crosses a boundary, the rounded value lands below `stack-top`, inside the allocation. There is no stack-pointer builtin either. So the honest options are for the stub to PUBLISH `stack-top` where the drivers read it, or for a build-time check to compare the two files the way `check-facts-guid.ps1` compares its three producers of one GUID. **The second is a decision about the gate and is red's or Damian's to make, not one to smuggle in; the numbers above are what it would assert.** Recorded and not built.

**One thing measured and NOT green: a SECOND request in the same boot times out.** Each of `/` and `/api/health` answers 200 in its own run; the next request in the same boot never completes. **Handed to blu 2026-08-16 (red's routing), and one candidate is already ELIMINATED so it is not bought twice.**

The obvious suspect was `ws-serve-loop` discarding `r.ws-state` and recursing on the original `st` after a `__heap-restore`, on the reasoning that the virtqueue counters (`avail-idx`, `last-used-idx`, `free-head`) are DEVICE-COUPLED: the device advances its used ring and never rolls back, so a driver reverting its own counters would disagree with the device from then on. **That reasoning may still be true but it is not sufficient, and the fix built on it does not work.** Threading `r.ws-state` forward and dropping the `__heap-restore` was built and run: `GET /api/health` answers 200 and the three requests after it in the same boot all time out, exactly as before. Reverted rather than landed, because an unverified change that does not move the arm is not worth carrying.

So the fault survives the state being threaded correctly, which points past the driver's counters and into the transport or the accept path -- `arm64-net-io-listen` re-listening on a session that has already been through `transport-close` is the next thing to look at, and it has NOT been measured.

**BACK TO REEK 2026-08-17 (red's routing; blu never started it). Read-only progress, no boot spent yet, and it changes the shape of the question.**

**The defect site is named, and it is in the PAYLOAD.** `codex/test/arm64-web-server.codex:139`: `ws-serve-loop` ends `in ws-serve-loop st (count + 1)` -- it recurses on `st`, the ORIGINAL state, discarding `r.ws-state` entirely. Every iteration therefore restarts from the post-`opening` `VirtioNetState`. **That single line accounts for BOTH of blu's new observations, which no previous account connected:** the device seeing the avail index revert to **33** is the driver's counters going back to their post-init value (`virtio-net-fill-rx` submits 32 buffers, so 33 is exactly where init leaves it), and `loop 1` re-reading `loop 0`'s bytes is `last-used-idx` reverting with them, so the driver re-consumes the same used-ring entry. Note QEMU's message is misleading and cost time to read: `Guest moved used index from 10043 to 33` is raised on the **AVAIL** ring going backwards, not the used ring.

**Two candidates ELIMINATED by reading, so nobody buys them:**

- **`vnet-state-addr` is dead.** `codex/os/kernel/VirtioNet.codex:66` declares `vnet-state-addr : Integer = #80100`, and `#80100` is not RAM at all on QEMU virt -- RAM starts at `0x40000000` and that address is in the low flash window. It looks exactly like a live defect. It has **one occurrence in the whole tree, its own declaration** (L-UNCALLED), so it cannot be the cause. Worth deleting on the next pass through that chapter.
- **The inner `__heap-restore` calls in `Arm64NetIO.codex` are CORRECT and are not the bug.** `arm64-net-io-accept:113-116` and `arm64-net-io-recv-poll:142-145` both restore ONLY on the `has-frame == False` path, where the poll returned the state unchanged and nothing allocated is retained. The frame-arrived path threads the state and does not restore. That is the right pattern and it should not be changed.

**Why blu's threading fix may have failed for a reason that is not about the payload, and this is the part to read before trying again.** The mechanism Codex provides for making a value outlive a `__heap-restore` is `deck-record`, and `codex/compiler/Core/DiagnosticBag.codex:31-35` states exactly this use. **`deck-record` is the identity function and its effect is an x86-64 EMITTER INTRINSIC that the arm64 plug does not have** -- `codex/plugs/arm64` mentions the name zero times, as does `codex/plugs/riscv`. Recorded as `plugs-backlog.md` 1.33. So on this lane there is no way to say "keep this across the restore", and a fix has to remove the restore rather than annotate around it.

### ARM RUN 2026-08-17 (reek): the first defect is FIXED and the second is isolated to one line

Three requests in one boot, plugs deliberately left at their 2026-08-16 build so the payload edit is the only variable against blu's run. Serial:

```
loop 0 ok:/api/health len=11 eq=MATCH codes= 81 15 31 17 81 20 13 15 23 14 20 recv=184 resp=105 avail=36 lastused=4 free=36
loop 1 no-conn avail=41 lastused=9  free=41
loop 2 no-conn avail=42 lastused=10 free=42
loop 3 no-conn avail=44 lastused=12 free=44
```

**Defect 1 is closed, and the counters are what say so.** `ws-serve-loop` now threads `r.ws-state` and takes no heap mark. `avail` advances 36 to 41 to 42 to 44 and `lastused` 4 to 9 to 10 to 12 where they previously reverted to 33 every iteration. Four things moved together: **the counters advance, the `Synchronous Exception` is gone, QEMU's stderr is EMPTY** (no `Guest moved used index`, no `receive queue contains no in buffers`, both of which fired before), **and one host request now produces exactly one loop line** instead of two identical ones. The counters advancing is also what rules out the reading blu's result could not be separated from: the change reached the binary.

**Defect 2, measured rather than predicted, and it is `Tcp.codex`.** Requests 1 and 2 still time out, but the guest is alive and looping and still consuming frames. `EvListen` reaches `TcpListening` in **exactly one** state handler, `tcp-step-closed:224`. In all eight other states it is `TcpStep { conn = conn, action = ActNone }` -- a no-op (`:239, :256, :272, :297, :315, :329, :341, :355`). And `EvClose` from ESTABLISHED does not go to CLOSED: `tcp-step-established:292-295` sends a FIN and moves to **`TcpFinWait1`**. So the serve loop closes, lands in `FinWait1`, calls `arm64-net-io-listen`, and that listen **does nothing**; `accept` never sees `TcpEstablished` and every later iteration reports `no-conn`. This is blu's predicted next step, now confirmed and localised.

**The shape of it is architectural, so it is a decision rather than a patch.** This stack has one `TcpConnection` serving as both the listening socket and the accepted connection, so closing the connection destroys the listener. A server keeps them separate. The three candidate fixes are: give the loop a listening socket distinct from the connection (correct, largest); have `arm64-net-io-close` drive the connection to `TcpClosed` outright, which for a server that does not need a graceful close is a reset rather than a FIN; or accept `EvListen` from the closing states, which is the smallest change and the least honest about TCP. **Not chosen here.**

**Harness note.** `build/boot-arm64.ps1` runs QEMU in the foreground with no timeout, so it cannot be an arm. The harness that produced the above is `docs/Probes/arm64-two-requests.ps1` (rescued from reek's session scratchpad at the 2026-08-17 relaunch; its `$Repo` is reek's workspace, set it to yours). It builds with `-NoBoot`, launches QEMU itself with a derived hostfwd port rather than the hardcoded 8080 (another agent booting arm64 would otherwise answer the requests, L-SHARED), and banks three channels -- guest serial, QEMU stderr, per-request client verdict -- because a crashed guest and a timed-out client are indistinguishable from the client alone, which is how the crash went unrecorded for a day. It refuses to report request results at all unless the guest announced `Listening on port 80`, after a first run where `Start-Process` split the firmware path at its space, QEMU never booted, and three connection refusals would otherwise have read exactly like this bug.

**CLOSED 2026-08-17 (blu), measured on the guest.** With the re-listen fix in
(main 16291: `net-listen` builds a fresh listening socket instead of stepping a
connection close left in FIN_WAIT_1), `docs/Probes/arm64-two-requests.ps1
-Requests 3` answered **all three in one boot -- `HTTP 200` 15 bytes, `HTTP
200` 15 bytes, `HTTP 200` 948 bytes** -- with three loop lines for three host
requests, `avail` 36 to 42 to 48 and `lastused` 4 to 10 to 16 advancing
monotonically, `eq=MATCH` on both `/api/health` paths, no `Synchronous
Exception`, and QEMU's stderr EMPTY.

### What the DEVICE says, measured by blu 2026-08-16 (parked here for the release)

Re-run of the unmodified payload against seed `1518687B510CA346` with the ARM64 and PE plugs rebuilt on it, `build/boot-arm64.ps1 -Src codex/test/arm64-web-server.codex`, two host requests to `http://localhost:8080`. **The timeout is not the whole event and the previous accounts stop one line too early.** `GET /` answers 200 with 948 bytes; `GET /api/health` times out; and the serial and QEMU together say this:

```
loop 0 ok:/ len=1 eq=nomatch codes= 81 recv=173 resp=1032
loop 1 ok:/ len=1 eq=nomatch codes= 81 recv=173 resp=1032

Synchronous Exception at 0x00000000401016E4
Recursive exception occurred while dumping the CPU state
qemu-system-aarch64: virtio-net receive queue contains no in buffers
qemu-system-aarch64: Slirp: Failed to send packet, ret: -1
qemu-system-aarch64: Guest moved used index from 10043 to 33
```

Three things there are new and none of them is a theory:

1. **The guest serves TWO requests for ONE host request, and the second is a re-read of the first.** `loop 1` reports the same path, the same `recv=173` and the same `resp=1032` as `loop 0`. So the second iteration re-consumed the first request's bytes rather than waiting for new ones.
2. **The guest CRASHES.** `Synchronous Exception at 0x401016E4`, then a recursive exception dumping CPU state. The host-visible symptom is a timeout because the guest is gone, so "the second request times out" is a description of the client, not of the guest.
3. **QEMU reports the driver moving the used index BACKWARDS, `10043 to 33`, and the RX queue running out of in-buffers.** That is the device's own account of the counter disagreement, and no previous revision had it.

**A candidate that fitted all three, and it is now DEAD BY MEASUREMENT.**
`transport-new` (`codex/os/net/TcpTransport.codex:19-25`) advances the heap by
`transport-recv-buf-cap` = 33,554,432, a full 32 MB, against a stub grant of
`8192` pages = exactly 32 MB (`Arm64PeWriter.codex:415`), so the frontier
reaching the DMA constants at `#44000000` would have overwritten the RX ring,
the queue region and the buffers, in that order. It does not reach them.
`build-arm64-img` now asserts the floor on every build and printed, on the
2026-08-17 run above, **`stack-top 0x42123000, floor 0x44000000, 32,362,496
bytes clear, 8192 heap pages`**. The arithmetic that made this look plausible
omitted where the frontier actually starts, which is the failure this whole
section keeps recording: reasoning about an address instead of reading one.
The cause was reek's defect 1 (the loop's `__heap-restore` reverting the
virtqueue counters) plus the re-listen defect, both now fixed, and the
`Synchronous Exception` and the backwards used index have not reappeared in
any run since.

## The 404 that preceded it, and why the first three suspects were wrong

**CLOSED 2026-08-17 (blu): it does not reproduce.** The probe run of that
date, three requests in one boot, reports `eq=MATCH` on both `/api/health`
requests with `len=11` and `codes= 81 15 31 17 81 20 13 15 23 14 20`, so the
path arrives, decodes and routes correctly. The three candidates named at the
end of this section were never measured and are now moot; they are left in
place because the reasoning that narrowed to them is still worth reading, not
because any of them is still a live lead.

**Which one it was is NOT claimed.** The behaviour is consistent with the
account this file already carries, the VirtIO DMA regions sitting inside the
guest's own heap and corrupting a single CCE table entry, which is why exactly
one character was wrong and the rest survived; reek's defect 1 removed the
`__heap-restore` that made that corruption reachable each iteration. Recording
"does not reproduce, and here is the measurement" is the honest close, rather
than picking a winner among three things nobody measured.

The routed path came out as `napinhealth` where `/api/health` was sent. **The wire dump settles which side is wrong:** the request on the link reads `GET /api/health HTTP/1.1` exactly, so the bytes arrive correct and the guest builds the wrong Text from them. Every `/` (0x2F) becomes `n` (0x6E) and every other character survives, so it is a single-code-point mapping fault in the bytes-to-Text conversion, not a framing or length error.

**`unicode-bytes-to-text` was the first suspect and it is EXONERATED, measured.** `codex/test/bytes-to-text-2f.codex` converts `[47, 97, 112, 105, 47, 104]` and prints the CCE points and the round trip; x86-64 is the oracle and the same source runs on both lanes, so a disagreement would be the defect and no `.expected` has to encode which side is right. They agree exactly:

```
x86-64  len=6 cce0=81 cce1=15 cce4=81 cce5=20 roundtrip=47 text=/api/h
ARM64   len=6 cce0=81 cce1=15 cce4=81 cce5=20 roundtrip=47 text=/api/h
```

`0x2F` maps to CCE 81 and back to 47 on both lanes, and the text renders correctly. **So the COMPILER-9 collision on that name is not the cause, and routing it to root on that basis would have been wrong.**

**Where the fault is NOT, therefore, and what is left.** `http-parse-request` (`apps/works/Http.codex`) reaches the same conversion through `http-bytes-to-text` = `unicode-bytes-to-text (http-slice ...)`, and the byte range it slices for the path is right by inspection. The remaining candidates, none measured: `http-slice`'s `list-push` accumulator, Text equality in `ws-standard-route`'s comparison, and the CCE-to-Unicode step inside `print-line-uni` itself -- the last of which would mean the DIAG is wrong and the 404 has a different cause entirely. **Naming them is not measuring them and this row does not claim otherwise.**

**Still not done:** Phase 5d's external smoke test needs 5b/5c and Damian's OCI account, which is a dependency rather than a residue. **There is no remaining LOCAL gap: the second-request timeout was the re-listen defect, fixed at main 16291 and confirmed on the guest at 16293, three requests answering 200 in one boot with QEMU's stderr empty.**

## Root cause, measured 2026-08-16: `queue_select` never takes, so the TX queue is never configured

`virtio-select-queue` is a `poke-16` to common-config offset 22. `poke-16` is not a 16-bit store (below): it is a 32-bit read-modify-write of the enclosing aligned word, so it lands as a **four-byte write at offset 20**, which is `device_status`. QEMU dispatches common-config writes by field and width, and that write is dropped. Measured directly:

```
QSEL after selecting 1: readback=0   status-half=15
```

**`queue_select` stays 0 forever.** Everything follows from it:

- `virtio-setup-queue vd 1 ...` reconfigures **queue 0** with the TX ring's addresses, clobbering RX. The "q1 device readback" that looked so reassuring was reading queue 0's slot, which is why it always agreed with the driver.
- **The TX queue is never configured, never enabled, never ready.** A notify for it can do nothing, which is the whole TX fault.
- **`num_queues` reading 0 is closed, and it was never `num_queues`.** `peek-16` at offset 18 is `peek-32` at 16 masked to the low half, so it returns `msix_config`, which is 0. The field was never being read.
- **This is exactly why Phase 4 passes.** VirtioBlk has ONE queue and only ever selects queue 0, and `queue_select` is already 0, so the dropped write costs it nothing. The control was pointing at the difference all along: not the BAR window, not the transport, but how many queues the driver has to select between.

**The repair is root's**, who owns the `poke-16` defect (real `peek-16`/`poke-16` builtins, seed-affecting). Nothing in `VirtioPci.codex` or `VirtioNet.codex` needs to change for this; they are correct against a working 16-bit store. **This corrects an earlier statement of mine that `poke-16` was not what breaks TX** -- Phase 4 bounded its severity as "blk reads correctly through it", and that bound was true and misleading: blk never exercises the one register where width matters.

**Eliminated on the way, and not to be re-chased: the BAR window.** Moving the net device's BAR4 into the high MMIO window by hand (`0x8000010000`, where VirtioBlk lives and the stub maps Device) changes nothing: negotiation still succeeds, `used-idx` stays 0, no frame. The window was a difference between the two devices but not the cause.

**Phase 4 is MEASURED AND PASSING (`codex/test/arm64-virtio-blk-probe.codex`), and it is what found the root cause above.** VirtioBlk had no caller in any payload, which is why the phase was unmeasured. It has one now, and it reads sector 0 off the disk the firmware booted this image from:

```
pci count=3 vendor=6900 device=4097
capacity=65536 sectors, queue-size=256 notify-addr=549755842560
read ok=True len=512 b450=238 b510=85 b511=170
```

The oracle is content, not a pointer: 238/85/170 are `0xEE` (GPT protective partition type), `0x55`, `0xAA`, derived from the image rather than captured from a run, so 512 zero bytes would fail it. **A first defect had to be fixed to get there**: `vblk-buf-base`, `vblk-queue-region` and `vblk-req-base` were `0x300000`/`0x310000`/`0x320000`, which on QEMU virt is FLASH (flash0 spans 0-0x03FFFFFF, and the trace names `virt.flash1` at `0x4000000`); RAM starts at `0x40000000`. They now sit in the 2MB block the stub already patches to device memory for VirtIO DMA, above the ranges VirtioNet occupies.

**What this proves is the control the net investigation never had: the VirtioPci transport WORKS.** Same `virtio-init-device`, same `virtio-negotiate`, same `virtio-setup-queue`, same `virtio-submit-desc`, same `virtio-notify`, same `virtio-poll-used`, same `poke-16` read-modify-write on every 16-bit register -- and a real DMA read completes with the right bytes. **So the TX fault is not in the shared transport and not in `poke-16`, and the notify path is sound where it is reachable.**

**The difference is the BAR window, and it is the sharpest lead on the record.** VirtioBlk's `notify-addr` is `0x8000002000`, in the HIGH MMIO window, which `a64pe-stub-setup-page-tables-at` maps through PUD1/PMD5 as Device memory. VirtioNet's BAR4 is at `0x10004000` in the 32-bit window, where writes to `+0x0000` land and writes to `+0x1000`, `+0x2000` and `+0x3000` reach no MemoryRegion at all. **The next measurement is why the low window's higher pages are dead when its first page is live** -- and forcing the net device's BARs into the high window (`arm64-pci-assign-bars`) is the cheap way to test whether the window is the whole story.

**`memory_region_ops_write` is the instrument that showed the guest's stores as QEMU sees them, and it found two things.**

**(a) `poke-16` does not emit a 16-bit store. It is a 32-bit read-modify-write of the enclosing aligned word -- and root corrected the CAUSE from source: it is not ARM64 codegen.** The compiler has no `peek-16`/`poke-16` builtin, so `VirtioPci.codex` defines them in Codex out of `peek-32`/mask/`poke-32`, and that definition runs on **every** lane including x86-64. The ARM64 runtime's `strh`/`ldrh` helpers registered under those names are correct and unreachable, because the chapter's own definition wins (L-UNCALLED). root owns the repair, which is seed-affecting: real builtins plus a RISC-V `lh`/`sh` pair. **Bounding its severity from the Phase 4 result: VirtioBlk completes a correct DMA read THROUGH this read-modify-write, so it is a real correctness defect but not what breaks TX.** On the virtio-net common config the trace carries **29 size-4 and 5 size-1 writes and not one size-2**. The poison write of `0x1234` to `num_queues` (offset 18) appears as `addr 0x10004010 value 0x1234ffff size 4` -- offset 16, four bytes, with `msix_config` merged in beside it. `virtio-select-queue` (a `poke-16` at offset 22) appears as `addr 0x10004014 value 0x1000f size 4`, rewriting `device_status` on every queue selection. In guest RAM this is invisible and harmless, which is exactly why the `peek-32` cross-check of the ring passed; **against MMIO it writes the wrong offset at the wrong width and drags neighbouring registers along.** Every 16-bit MMIO access in `VirtioPci.codex` is on this footing: `queue_select`, `queue_size`, `queue_enable`, `queue_notify_off`. This is a real defect independent of the TX fault and it is not yet fixed.

**(b) Nothing decodes at the notify address.** Across a whole boot, **zero** writes reach `0x10005xxx`, `0x10006xxx` or `0x10007xxx` -- the ISR, device-config and notify regions all receive nothing -- while `0x10004xxx` is busy. The only virtio regions QEMU reports writes to are `virtio-pci-common-virtio-net` (absolute addresses, so BAR4 base `0x10004000` is confirmed by QEMU itself) and the two devices' legacy I/O BARs at `0x3eff0xxx`.

**Dead-code elimination is NOT the explanation, tested directly:** a `poke-32` to the notify address whose return value is PRINTED (so it cannot be discarded) still produces no write at `0x10007000`. **The remaining ambiguity is stated rather than glossed:** `memory_region_ops_write` only traces writes that reach a MemoryRegion, so it cannot yet separate "the guest never issued the store" from "the store went into unassigned space". That is the next measurement, and `-d guest_errors` on the current image is the cheap way to ask it.

**A separate real defect, found while reading the stub and now FIXED (main 16051).** `a64pe-stub-fill-pmd` writes block descriptors as `phys-base | 0x401`: bit 0 valid, bit 10 AF, and **AttrIndx = 0**, which `MAIR_EL1` attr0 defines as Normal write-back cacheable. So **every PCI MMIO BAR is mapped Normal cacheable rather than Device-nGnRnE**; only the ECAM window, the high-MMIO window and one patched 2MB DMA block get device memory. QEMU under TCG does not model caches, so this is invisible here and is NOT the cause of the notify fault. **It would not have survived KVM, which is what OCI runs**, and MMIO through a cacheable mapping is exactly the class of fault that presents as "works in the emulator, dead on the metal". Fixed at main 16051: PMD0 entries 128-503 (`0x10000000-0x3EFFFFFF`) now take `AttrIndx=1` through a new `a64pe-stub-fill-pmd-device-range`. Entry 0 stays Normal deliberately, because the page tables live at `0x70000` and a translation walk through Device memory is CONSTRAINED UNPREDICTABLE. Verified non-regressive: the probe image boots and reaches every print, and the TX fault is unchanged by it, as predicted.

**Corrected from the previous revision: "every notify this driver issues arrives as queue 0" was WRONG.** Those `n 0` lines were the firmware's, not ours -- virtio-blk has a single queue, so `n 0` is what a WORKING device looks like there, and 38 of the 43 came from the disk that booted this image. Attributing them to our driver was reading a log without first establishing whose lines they were.

**`virtio_pci_notify_write` firing zero times is NOT evidence and must not be used as any.** It fires zero times for the working virtio-blk device too, so the event is blind in this build. The previous revision leaned on its absence.

**`num_queues` reading 0 is left open rather than folded into this.** It is a device-supplied field in a region the guest provably reaches, and nothing measured so far explains it.

Three hypotheses are dead and must not be re-chased: the header size, `poke-16`/`peek-16` (the ring is byte-correct when cross-read with `peek-32`, which is the accessor fester's working VirtioBlk uses), and writing `queue_size` before `queue_enable` (tried, no change, reverted). Fester's `codex/foreword/core/VirtioBlk.codex` is a working modern driver on this bus but over virtio-MMIO, not virtio-PCI, so its register offsets do not transfer; what does transfer is its discipline of never trusting a 16-bit accessor and reading the flags/index pair as one aligned 32-bit word.

## OCI Free Tier Specs

- 4 ARM Ampere A1 cores (Neoverse N1, ARMv8.2)
- 24GB RAM
- 200GB block storage
- 10TB/month bandwidth
- KVM hypervisor, paravirtualized (VirtIO-PCI)
- UEFI boot, custom QCOW2 or raw images

## Phase 0: ARM64 MMIO and Buffer Builtins

Add to `codex/plugs/arm64/Arm64Runtime.codex`:
- peek-byte, poke-byte (LDRB/STRB)
- peek-32, poke-32 (LDR/STR 32-bit)
- peek-qword, poke-qword (LDR/STR 64-bit)
- __buf-write-byte, __buf-read-bytes, __buf-write-bytes
- __heap-save, __heap-restore, __heap-advance

Add to ARM64 encoder (codex.foreword Arm64Encoder):
- DSB, DMB, ISB barrier instructions
- MRS/MSR for system register access

Reference: x86-64 equivalents in X86_64Helpers.codex lines 2920-2958.

Test: poke-32 to PL011 UART on QEMU virt.

## Phase 1: ARM64 UEFI Boot

1a. ARM64 PE Writer -- new codex/plugs/pe/Arm64PeWriter.codex
- Machine type 0xAA64
- UEFI entry stub: save SystemTable, AllocatePages, ExitBootServices
- 4-level page tables (4KB granule), MAIR, TCR, TTBR0, SCTLR
- Set SP, x28 (heap), branch to opening

1b. FAT boot filename -- BOOTAA64EFI in Fat16Writer/Fat32Writer

1c. Exception vector table -- codex/os/kernel/Arm64Boot.codex
- 16 entries, 2KB-aligned, VBAR_EL1

1d. Build script -- build/build-arm64-img.ps1
- Source -> IR -> ARM64 codegen -> PE32+ -> GPT+FAT16 -> qemu-img QCOW2

Test: QEMU aarch64 with AAVMF UEFI firmware boots to UART output.

## Phase 2: GIC and Timer

2a. GICv3 driver -- codex/os/kernel/Gic.codex
- GICD, GICR, ICC system registers (MRS/MSR)

2b. Generic Timer -- codex/os/kernel/Arm64Timer.codex
- CNTP_TVAL_EL0, CNTP_CTL_EL0, PPI 30

Test: timer tick visible on UART.

## Phase 3: VirtIO-PCI Networking

3a. ECAM PCI -- codex/os/kernel/Arm64Pci.codex
- ECAM at 0x4010000000 (QEMU virt)
- Reuse Pci.codex scanning/BAR logic

3b. VirtIO-PCI transport -- codex/os/kernel/VirtioPci.codex
- VirtIO 1.0 modern, capability-list discovery
- Virtqueue setup, feature negotiation, DSB/DMB barriers

3c. VirtIO-net -- codex/os/kernel/VirtioNet.codex
- RX/TX virtqueues, MAC from device config
- Same interface as ne2k-send-frame/ne2k-recv-frame

3d. ARM64 NetIO -- codex/os/net/Arm64NetIO.codex
- Drop-in for NetIO.codex, calls VirtIO instead of NE2K

Test: curl HTTP response via QEMU TAP bridge.

## Phase 4: VirtIO-blk and Static Content

4a. VirtIO-blk -- codex/os/kernel/VirtioBlk.codex
4b. FAT reader -- codex/os/kernel/FatReader.codex
4c. Static file server -- extend web server routing

Test: serve HTML file from disk partition.

## Phase 5: OCI Deployment

5a. Raw -> QCOW2 via qemu-img -- **DONE, and it is a build artifact rather than a recipe.**
    `build/build-arm64-img.ps1 -Qcow2` converts beside the raw image; the switch
    is in `codex/build/buildarm64imgScript.codex` and the shipped script was
    regenerated from it, `check-generated-scripts -Only build-arm64-img` reporting
    match / 0 drift. Measured: 32 MB virtual, 640 KB on disk.
    **The QCOW2 itself is what was booted, not the raw sibling it came from**
    (L-ARTIFACT): it reaches `Listening on port 80` under QEMU.
5b. Upload to OCI Object Storage, import as custom image -- needs Damian's account
5c. VCN security list (TCP 80/443), DHCP or static IP -- needs Damian's account
5d. Smoke test via serial console and external curl -- needs 5b/5c, and needs the
    NIC, which is blocked on the `poke-16` root cause above

## Build Order

Phase 0 -> Phase 1 -> Phase 2 -> Phase 3 -> Phase 4 -> Phase 5.
Full build before deployment. Each phase has a QEMU test gate.

## Licensing

Codex Fair Use License v1.0 (draft). Free for personal, education,
research, evaluation, auditing, non-commercial open-source. Commercial
use free below $100K aggregate revenue (rolling 12 months). Above
threshold requires commercial license.
