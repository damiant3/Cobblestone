# blu -- workplan

*Status, not journal. Per-CL history is in Perforce; durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`; open capabilities are in
`docs/PM/BACKLOG.md`. This file is the current picture and the next moves only.
Keep it under ~80 lines.*

## Status (2026-07-23 fourteenth session)

**No red gate. Nothing shelved. Tree clean.** BACKLOG **5.4 is CLOSED** and
**5.15 is reduced to `coaps` alone**. No seed this session: every gate measured
`Sut === seed`, depot digest **B180343660EBB6CF67C7B7319DB4980EE501E9FF7C31CA5949C8741699D0EAA8**
(2,604,983 bytes), unchanged from the session start.

**Landed on main:** 10366 (5.4 part: `CoapEndpoint`), 10384 (**5.4 CLOSED**:
codex-vm general UDP forwarding, `codex/os/net/UdpIO`, MQTT decoder +
`MqttEndpoint`, live clients, both-direction interop), 10409 (**5.15**: QoS 2,
`mqtts`, inbound UDP + a CoAP server in the guest). Each: `build/build.ps1`
green, hard fixed point in ONE pass, `Sut === seed`. **`build/test.ps1` was NOT
RUN** (not an agent command).

**THE FINDING THAT MATTERS TO EVERYONE, not just this lane: our TLS ClientHello
carried no `signature_algorithms` extension.** RFC 8446 s9.2 makes it mandatory
and a conforming server MUST abort without it, so **every TLS and DTLS
handshake Codex ever initiated was one no conforming server would accept.**
Invisible for the life of the stack because our own server does not inspect it
(every loopback passed) and `tls-interop-test.ps1` drives our SERVER with
openssl's client -- the client half had never met a conforming peer. Fixed in
`dtls-ch-extensions` (`DtlsHello.codex`, shared by both stacks) plus
`tls-ext-sig-algs` in `Tls.codex`, ed25519 only. Only visible cost:
`tls-test.expected` ClientHello 155 -> 163 bytes.

## Interop now exists in BOTH directions for the IoT stack

`build/{coap-interop,coap-serve,mqtt-interop,mqtts-interop}-test.ps1`, against
aiocoap 0.4.17 and mosquitto 2.1.2. On-demand (they boot VMs and servers), not
gated. `mqtts-interop` is the first test where OUR TLS CLIENT talks to a foreign
SERVER. Each found a real bug on its first run; see `docs/ExaminersAssay.md`.

## NEXT ACTION -- pick a new item; my two rows are done

Nothing is shelved and nothing is half-finished. **5.15's remainder (`coaps`)
is BLOCKED ON PROVISIONING, not on code:** wolfSSL or GnuTLS with DTLS 1.3
must exist on this box first. Do not build the composition before the oracle --
stacking an unproven CoAP layer on an already loopback-only DTLS layer is what
`docs/PM/Active/Stories/BrotliBeatsOpus.md` forbids reporting as done.

## Hard walls (do not re-hit)

- **No DTLS 1.3 oracle on this box.** OpenSSL has none in ANY release (3.2.4,
  3.5.7 LTS, 4.0.1 all reject `DTLSv1.3`); wolfSSL's Python binding needs a
  native build that does not complete here. Checked twice, two sessions apart.
- **codex-vm UDP forwarding EXISTS NOW** (this session) -- outbound flows and
  their replies, plus `-portfwd udp:h:g` inbound with a synthetic gateway source
  port per host client. `10.0.2.2` maps to the host's `127.0.0.1` for TCP and
  UDP both. This retires the wall that used to block 5.13's NTP; that row is
  corrected.
- **`aiocoap` was pip-installed into D:\Python311 this session.** Test-only
  oracle, like python/openssl for TLS.

## Before working ANY backlog entry

Reproduce the stated cause with one command first -- most recent entries had a
wrong one, **including both rows worked this session**: 5.4 claimed TCP
retransmit was missing (it has existed all along, pinned by
`codex/test/tcp-reliability`) and 5.13 claimed the emulator forbade NTP.

- **A zero is not evidence unless the instrument can report non-zero**, and a
  test that asserts only `list-length pkt > 0` is blind by construction --
  that is how the CoAP Uri-Path went out in CCE for the life of the codec.
- **An endpoint is threaded LINEARLY.** `__record-set` returns the same record,
  so branching two exchanges off one endpoint value gives one session with two
  names. Cost a wrong answer in two `mqtt-loopback` scenarios at once.
- **A top-level binding re-evaluates at every mention (no memoization); with
  per-step allocation and no GC, a handful of mentions of a handshake exhausts
  the heap.** Run a multi-step exchange in ONE `let`-chain.
- **Revert your open files BEFORE a merge-down, not after.**

## Seeds

**A green `build/build.ps1` does NOT mean the seed matches the source** -- it
compares SUT against stage1, never seed against SUT. Measure `Sut === seed`
against the DEPOT after every gate. Any change under `codex/compiler/` needs
one; measure, never predict.

## My lane (own it; others stay out)

`codex/compiler` Types and Syntax, `codex/foreword/core` CCE and the encoding
chapters, TLS/DTLS/CoAP (the transport-security and IoT-protocol endpoints).
Emit/IR/LIR stays reek's.

## Other agents -- read this

- **`p4 revert` leaves ADDED files on disk, and a later `p4 unshelve` then
  reports "Can't clobber writable file" and does NOT open the add.** Run
  `p4 opened` immediately after any unshelve; re-`p4 add` the files (they are on
  disk with correct content). Hit three times this session; it is reliable, not
  occasional.
- **After an unshelve across a merge-down, `build/p4-stale-check.ps1` names the
  files opened at a stale revision, and `p4 sync` + `p4 resolve -am` fixes them
  keeping BOTH sides.** Do not hand-re-apply the edit as I did the first time --
  the three-way merge is what the tool is telling you to run.
- **The `[AgentGrid coordinator]` QUEUED and GO messages can arrive in your
  terminal OUT OF ORDER with the mailbox.** Twice this session a QUEUED line
  surfaced after `build-grant` was already written. Trust the mailbox: check
  `build-grant` exists and that no other agent's directory has one.
- **Killing a redirected process discards its buffered stdout** -- the same
  flush trap as codex-vm's `-output`. Read a server's log before Stop-Process.
- **`p4 resolve -am` can SKIP on a conflicting chunk** and leave your older
  version on disk while `p4 resolve -n` reports nothing to do. On `BACKLOG.md`:
  re-apply your edit FRESH against the merged head rather than trusting the
  resolve.
- **Copy-up can be refused with "cannot copy over outstanding merge changes"**
  when main advances DURING your gate -- merge down again, submit, copy up.
  Budget for two merge-downs per token hold.
- **Harness artifacts belong under `test-output/`, never `build-output/`** --
  `build.ps1`'s clean phase deletes the latter.
- **The plug bundle takes its declarations from a fixed list** in
  `codex/plugs/common/plug-build-lib.ps1`; a type can be *named* in the plug
  unit without being declared there -- that compiles until something constructs
  one.
- **Filetype:** a stray 0x01 or an em-dash in a sidecar flips its p4 filetype
  and fails the battery.
- **Foreword-accessible endpoints now live in `foreword/encode`** (TlsEndpoint,
  CoapEndpoint). DtlsEndpoint is still in `codex/os/net` -- a relocation
  candidate if a foreword DTLS consumer appears.
