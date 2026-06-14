# Matter + Thread Protocol Reference

**Source**: Connectivity Standards Alliance (CSA)
**Spec versions**: Matter 1.4, 1.4.1, 1.4.2, 1.5, 1.5.1

## Matter Overview

Smart home interoperability standard backed by Apple, Google, Amazon,
Samsung, and 600+ member companies. Replaces the fragmented
Zigbee/Z-Wave/HomeKit/SmartThings landscape.

## Transport

- **Thread**: Low-power mesh (802.15.4) — primary for battery devices
- **WiFi**: High-bandwidth devices (cameras, displays)
- **Ethernet**: Wired devices (bridges, hubs)
- **BLE**: Commissioning only (initial device setup)

## Protocol Stack

- Application layer: Matter Interaction Model (read/write/subscribe/invoke)
- Session layer: CASE (Certificate Authenticated Session Establishment),
  PASE (Passcode Authenticated Session Establishment)
- Transport: MRP (Matter Reliable Protocol) over UDP
- Network: IPv6 (Thread uses 6LoWPAN, WiFi uses standard IPv6)

## Security Model

- **CASE**: Mutual authentication using X.509 certificates
  (Device Attestation Certificate chain)
- **PASE**: Passcode-based pairing (SPAKE2+) for commissioning
- **Operational certificates**: Issued by the fabric's Certificate
  Authority (every Matter network is a "fabric")
- **Access Control Lists**: Per-endpoint, per-cluster permissions

## Device Types

Lights, switches, sensors, thermostats, door locks, media devices,
cameras, refrigerators, laundry, energy management, EV chargers,
water management, and more. Each has a standardized cluster interface.

## Thread Details (IEEE 802.15.4)

- Mesh topology: self-healing, no single point of failure
- Border Router: bridges Thread to WiFi/Ethernet
- Sleepy End Devices: battery-powered, poll parent for messages
- 6LoWPAN: IPv6 over low-power wireless
- 250 kbps data rate, 2.4 GHz band

## Codex Relevance

- ESP32-C6 has native 802.15.4 radio (Thread + Zigbee capable)
- Matter commissioning could use Codex's Ed25519 identity model
  (conceptual alignment: device identity = keypair)
- Matter's CASE session maps to Codex's trust lattice handshake
- Implementation priority: LOWER than MQTT/CoAP/LwM2M (Matter is
  consumer-smart-home focused; Codex's initial targets are IIoT
  and medical)

## Specification Access

Download requires CSA membership or developer registration at:
https://csa-iot.org/developer-resource/specifications-download-request/
