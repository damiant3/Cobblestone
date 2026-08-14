# IoT Reference

Briefs of the external specifications, standards and datasheets the Codex
IoT work is built against. Every file here states what an outside authority
says, with a `Source` line naming it. Nothing here describes Codex.

Our own output about IoT lives elsewhere: the designs in
`docs/Designs/Active/IoT/`, and the project prospectus in
`docs/PM/Projects/CodexIoTPlan.md`.

## Compliance

| File | Standard |
|---|---|
| `Compliance/EU-CRA-Summary.md` | EU Cyber Resilience Act, Regulation 2024/2847 |
| `Compliance/ETSI-EN-303-645-Summary.md` | ETSI EN 303 645, consumer IoT security |
| `Compliance/NISTIR-8259-Summary.md` | NISTIR 8259 series, IoT capability baseline |
| `Compliance/IEC-62443-Summary.md` | IEC 62443, industrial automation security |

Each of these has a matching Codex-onto-standard mapping in
`docs/Designs/Active/IoT/`. The summary says what the standard requires; the
mapping says what Codex does about it. They are not copies of each other.

## Protocols

| File | Specification |
|---|---|
| `Protocols/MQTT-v5-Reference.md` | OASIS MQTT v5.0 |
| `Protocols/CoAP-RFC7252-Reference.md` | IETF RFC 7252 |
| `Protocols/LwM2M-Reference.md` | OMA LwM2M v1.2 |
| `Protocols/Matter-Thread-Reference.md` | Connectivity Standards Alliance, Matter and Thread |

## Architecture

| File | Source |
|---|---|
| `Architecture/ARM-Specs-Index.md` | ARM Developer documentation portal |
| `Architecture/RISC-V-Specs-Index.md` | RISC-V International |

## Hardware

| File | Part |
|---|---|
| `Hardware/STM32-Reference.md` | STM32F4/H7, ARM Cortex-M |
| `Hardware/ESP32-C6-Reference.md` | ESP32-C6, RISC-V |
| `Hardware/RaspberryPi-Reference.md` | Raspberry Pi 4/5, ARM Cortex-A gateway |

## Market

`MarketData.md` -- market figures with their sources and the vote each claim
survived. Not a specification; kept here because every claim in it is an
external report rather than a measurement of ours.
