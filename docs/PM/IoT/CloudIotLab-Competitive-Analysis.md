# CloudIotLab -- Competitive Analysis and Maturity Assessment

**Prepared**: 2026-07-03 (fester session)
**Subject**: https://cloudiotlab.com/ -- "Industrial IoT Platform & Protocol Sandbox"
**Parent brand**: "Aviora" (leaked in server build path `/opt/cloudiotlab/Aviora/web/cloudiotlab`)
**Method note**: Findings below combine the site's indexed description with its
own publicly-served front-end source. The site runs a **Vite dev server in
production**, so its React source is directly readable. All probing was
unauthenticated GET requests only -- no credentials, no state mutation, no API
abuse. We did not exercise the backend beyond confirming it 404s like an Express
app.

---

## 1. What It Is

An early-stage **browser-based IIoT device/protocol prototyping SaaS**. The pitch:
prototype, test, and deploy IIoT gateway/device firmware **without hardware**,
then download deployment images to flash real evaluation boards.

Feature surface (from the shipped React route table and marketing copy):

- **Virtual Device Sandbox** (`/lab/virtual-device`) -- simulate a device in the
  cloud, pick sensors, monitor and interact with it live.
- **Protocol Studio** (`/lab/protocol-studio`, `/lab/protocol-studio/run`) --
  write custom protocol code, "upload code, run, and test in the browser."
- **Device Monitor** (`/lab/device/:slug`) -- full-screen live device dashboard.
- **Deployment images** -- download firmware images for eval boards / real
  hardware (higher tiers), OTA image hosting via FTP (top tier).
- **Time-series data + dashboards**, commands, alerts, view sharing.
- **Egress/integration**: TCP, MQTT, HTTPS, with a **built-in MQTT broker**.
- Standard SaaS chrome: accounts (JWT bearer auth, refresh scheduler, cloud
  "lab sessions" with defer-leave semantics), products/services/solutions
  marketing pages, blog, contact, request-demo, membership plans.

**Fieldbus/industrial protocols advertised**: LoRaWAN, **Modbus, RS485, RS232**.

**Pricing** (aspirational -- see maturity note; billing is not implemented):

| Plan | Price | Key limits |
|---|---|---|
| Guest | $0 | 1 active session, basic monitoring |
| Starter | $30/mo | 2 sessions, 2 sensors, 1 firmware package, eval-board images |
| Experienced | $50/mo | 4 sessions, 4 sensors, Protocol Studio, save 5 devices, images for own hardware |
| Professional | $100/mo | 5 sessions, 6 sensors, Protocol Studio, save 10 devices, custom sensor simulators, FTP OTA image hosting |

The target user is an **individual embedded/IIoT developer or SMB team** who
wants a zero-hardware prototyping loop. This is a prosumer developer tool, not
an enterprise platform, despite the "Industrial IoT Platform" framing.

---

## 2. Maturity Assessment -- Very Early / Pre-Revenue

The technical fingerprints point to a **solo developer or tiny team at
side-project / pre-seed stage**:

1. **Vite dev server serving production.** The apex domain ships
   `/@vite/client`, `/@react-refresh`, and raw `/src/*.jsx` with HMR -- i.e.
   `vite dev`, not a built/minified bundle. No production build, no CDN, single
   nginx/1.24.0 box on Ubuntu. This is the single strongest amateurism signal.
2. **No billing.** The "Choose plan" button calls
   `window.alert("Payment integration will be added in the next phase.")`.
   The company **cannot currently take money**. It is pre-revenue by definition.
3. **Full source + internal paths exposed.** Absolute build path
   `/opt/cloudiotlab/Aviora/web/cloudiotlab` and the entire component tree are
   readable. No source protection, no minification.
4. **Half-finished routes.** Blog post view, Resources, and an OEE calculator
   are built but commented out of the router. Work in progress.
5. **No public company/founder footprint.** No press, funding, LinkedIn
   company page, or startup-database entry surfaced. "Aviora" is unindexed.

**Verdict**: A promising but immature single-operator product. The core lab
loop (virtual device → protocol code → downloadable image) appears real and is
the interesting part; everything around it (billing, content, ops hardening) is
placeholder.

---

## 3. Who They Compete With

CloudIotLab straddles three established categories. It is not creating a new one.

**A. Browser-based embedded simulation (its closest head-to-head):**
- **Wokwi** -- the direct analog: in-browser microcontroller + sensor simulation,
  code, run, no hardware. More mature, larger community, PlatformIO integration.
- **Renode** (we already use it for ARM64/RISC-V board tests), **QEMU** -- the
  serious, free, self-hostable simulation stack. No cloud SaaS wrapper.
- **Arduino Cloud**, **PlatformIO** -- code-and-simulate developer loops.

**B. IIoT platform + protocol/dashboard tier:**
- **ThingsBoard**, **Mainflux/Magistrala** -- open-source IoT platforms with
  device simulation, MQTT brokers, dashboards. Free and self-hostable.
- **Inductive Automation Ignition** -- the industrial incumbent (Modbus/OPC-UA,
  MQTT/Sparkplug, SCADA). Enterprise-grade.
- **Losant, Datacake, ThingWorx, Kaa, Golioth** -- managed IoT/device-fleet
  clouds with simulation and OTA.
- **AWS IoT Core / Azure IoT Hub** -- the hyperscaler default (Google sunset
  Cloud IoT Core in 2023).

**C. Device simulation / SIM tooling:**
- **InfiSIM**, IoT-LAB (FIT), various "IoT sandbox" testbeds.

**Their would-be differentiator** vs all of the above: a single browser loop
that goes from simulated device → custom protocol code → **downloadable image
you flash onto a real eval board**. That bridge (sim to real hardware, no local
toolchain) is genuinely the interesting wedge -- but **Wokwi already occupies
most of it**, with more maturity and mindshare.

---

## 4. Relevance to Codex

Mapped against `docs/PM/Stories/Vision/CodexIoTPlan.md`. Three useful angles,
one hard boundary.

**(1) It flags a real protocol gap for our #1 target segment.** Our go-to-market
ranks **Industrial IoT first**, but the Codex protocol stack is MQTT / CoAP /
LwM2M -- the *telemetry* tier. CloudIotLab centers on **Modbus / RS485 / RS232 /
LoRaWAN**, the *fieldbus* protocols industrial customers actually speak to PLCs
and meters. We have none of these. This is the biggest actionable takeaway:
a **Modbus foreword encoder + an RS485/RS232 serial HAL** is table-stakes work
for the industrial pitch that we have not scoped. → Backlog candidate.

**(2) It is a candidate interop endpoint for a gap we already list.** Our Phase 2
"remaining" item is *end-to-end MQTT against a real broker* -- today we only
encode/decode packets and test on MMIO stubs. Their built-in MQTT broker + TCP
ingress is one such external endpoint (drive a codex-vm NIC at it via NAT +
`-portfwd`). **But** given the dev-build-in-prod fragility, the sane path is to
stand up our own Mosquitto/Magistrala broker locally for that test -- no external
dependency, consistent with "if we didn't build it, we don't trust it." Their
existence just confirms the test is worth building.

**(3) Competitive/market intel.** It validates that there is demand for
browser-based IIoT protocol prototyping and reveals what SMB developers will be
asked to pay ($30–100/mo). It also sits exactly where a future **Codex Device
Manager** (Phase 5) would operate -- and it makes **no memory-safety or
compliance-by-construction claim**, which is precisely our KingsAndCourts
differentiator. Their moat (browser prototyping) and ours (compile-time safety,
signed capability-scoped CDX, compliance as a build artifact) barely overlap.

**Hard boundary**: this is **reference / interop / competitive intel only** --
never a substrate we depend on or ship. Codex is a language/compiler/OS play;
CloudIotLab is a hosted developer tool. Different layer, different product.

---

## 5. Is Reaching Out Worthwhile?

**Short answer: low priority. Approachable, but low strategic leverage right now.**

- **As a customer / partner / acquisition target**: No. They are pre-revenue,
  single-operator, running a dev build in production. Too early and too small to
  move any Codex objective.
- **As a design partner for the industrial-protocol gap**: Marginal. They know
  the Modbus/RS485/LoRaWAN domain we are weak in, and a tiny team will answer a
  contact form. But we would be teaching more than learning, and we do not need
  their platform to build a Modbus encoder.
- **As a peer interop test target**: Possible and cheap -- if we want a real
  third-party MQTT broker to point Codex firmware at, they are one option. Lower
  risk to self-host our own.
- **Do NOT** rely on them for anything load-bearing. Their reliability is
  unproven and the ops posture is fragile.

**Recommendation**: Do not pay them for what we can build. We already built the
fieldbus capability their platform sells (see Section 6). Keep CloudIotLab on the
radar as a market reference only. No outreach warranted at their current maturity;
revisit only if they ship a real product (production build, working billing,
published team).

---

## 6. What We Built In Response

Rather than log a backlog item, we closed the biggest concrete gap this analysis
surfaced. Shipped in this changelist:

- **`codex/foreword/encode/Modbus.codex`** -- a Modbus fieldbus encoder per the
  Modbus Application Protocol v1.1b3 and Modbus over Serial Line v1.02. Covers
  the read PDUs (coils, discrete inputs, holding registers, input registers),
  the write PDUs (single coil, single register, multiple registers **and
  multiple coils** with LSB-first bit-packing), CRC-16/Modbus (reflected poly
  0xA001), **RTU framing** (unit address + CRC, for RS485/RS232) and **TCP
  framing** (7-byte MBAP header), plus response parsing (registers and coils)
  and exception handling. Self-contained (no cites), in the exact shape of the
  existing `Mqtt`/`Coap` encoders.
- **`codex/test/modbus-encode.codex` + `.expected`** -- a 12-check known-answer
  test. The CRC is verified against an independent implementation
  (`0x0A84` for `01 03 00 00 00 01`) *and* via the algorithm-independent
  whole-frame-CRC-is-zero property, so a wrong constant cannot mask a wrong
  algorithm; the coil packer is checked against the canonical spec example
  (`0xCD 0x01`) and via a pack/parse round-trip. `PASS_EXPECTED`.
- **`codex/foreword/core/SerialLine.codex`** -- an RS485/RS232 serial-line HAL
  layered on the existing UART config in `Board.codex`. The open port is a
  `mutable SerialPort` -- a uniquely-owned resource the type system forbids
  aliasing (CDX2062) -- that models the line discipline: RS232 and RS485
  full-duplex impose no direction constraint, while RS485 half-duplex gates
  transmission on an explicit driver-enable and counts a fault (moving no bytes)
  for any transfer that would violate the shared-bus discipline. The bus grant
  is a **`linear`** token that must be released exactly once; a handler that
  drops it is rejected at compile time.
- **`codex/test/serial-line.codex` + `.expected`** -- five packed-integer
  known-answer scenarios (RS485 half-duplex discipline, RS232, RS485
  full-duplex, direction control, linear bus-grant round-trip). `PASS_EXPECTED`.
- **`codex/test/errors/serial-bus-grant-leak.codex` + `.failing`** -- a negative
  test proving the linear typing is not decorative: dropping the bus grant is a
  compile error (CDX2063). `PASS_FAILING`.

This makes Modbus a first-class Codex protocol alongside MQTT/CoAP/LwM2M -- the
telemetry tier now has the fieldbus tier beside it -- and gives RS485/RS232 a
distinct, ownership-typed HAL resource above the per-board UART drivers that
Modbus RTU rides on. Gates: default battery 249 total, 0 fail; full build
one-pass hard fixed point.

**Still open (not built here, deliberately):** an MQTT-against-a-real-broker
integration test. That is a networking/test-harness effort whose result is
non-deterministic by nature; bundling a flaky test would violate the
zero-failures gate. When built, it should drive a codex-vm firmware's MQTT stack
against a *self-hosted* broker (Mosquitto/Magistrala) over the NE2K NIC +
`-portfwd` -- not an external SaaS like CloudIotLab, which stays a reference
target, never a dependency.

---

## Sources

- https://cloudiotlab.com/ (site + publicly-served front-end source)
- Category references: Wokwi, Renode, QEMU, ThingsBoard, Mainflux/Magistrala,
  Inductive Automation Ignition, Losant, Datacake, AWS IoT Core, Azure IoT Hub,
  InfiSIM, FIT IoT-LAB.
