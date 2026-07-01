# OMA LwM2M v1.2 Protocol Reference

**Source**: Open Mobile Alliance, OMA-TS-LightweightM2M_Core-V1_2
**Transport**: CoAP (RFC 7252) over UDP/DTLS or TCP/TLS

## Purpose

Lightweight Machine-to-Machine protocol for IoT device management.
This is the protocol that implements the lifecycle management the
EU CRA demands: bootstrap, registration, firmware update, device
monitoring, all standardized.

## Architecture

- **LwM2M Server**: Manages devices (registration, commands, firmware)
- **LwM2M Bootstrap Server**: Initial device provisioning
- **LwM2M Client**: Device-side agent
- Transport: CoAP (constrained devices) or HTTP (gateways)

## Four Interfaces

1. **Bootstrap**: Initial device provisioning and credential setup
2. **Registration**: Device registers with management server
3. **Device Management**: Read/write/execute/observe resources
4. **Information Reporting**: Periodic/event-driven telemetry

## Object/Resource Model

Hierarchical: Object / Object Instance / Resource / Resource Instance
Addressed by numeric path: /{ObjectID}/{InstanceID}/{ResourceID}

## Standard Objects

| ID | Name | Purpose |
|---|---|---|
| 0 | Security | Server URI, keys, bootstrap flag |
| 1 | Server | Registration lifetime, binding, notifications |
| 2 | Access Control | ACL for object instances |
| 3 | Device | Manufacturer, model, serial, firmware version, battery |
| 4 | Connectivity Monitoring | Network bearer, signal strength |
| 5 | Firmware Update | Package URI, state machine, update result |
| 6 | Location | Latitude, longitude, altitude, timestamp |
| 7 | Connectivity Statistics | TX/RX bytes, packet counts |

## Firmware Update (Object 5) — CRA Critical

State machine: Idle -> Downloading -> Downloaded -> Updating -> Idle
Resources:
- Package URI (server pushes firmware location)
- Package (direct binary push)
- Update (execute trigger)
- State (current state, observable)
- Update Result (success/failure code)

This object IS the OTA update mechanism that CRA requires. A Codex
LwM2M client would serve Object 5 backed by the CDX verifier —
a firmware update that fails signature or capability verification
reports Update Result = failure and stays on the current firmware.

## Implementation Notes for Codex

- Built on CoAP — implement CoAP first, then LwM2M on top
- Object model maps to Codex records (each object = a record type)
- Resource operations (Read/Write/Execute/Observe) are effects
- Bootstrap interface provisions device identity (Ed25519 keypair)
- Firmware Update object backed by CDX verifier
- Registration keeps device alive in fleet management server
- IPSO Smart Objects extend the model for sensors/actuators
- Interop targets: Eclipse Leshan (server), Eclipse Wakaama (client)
