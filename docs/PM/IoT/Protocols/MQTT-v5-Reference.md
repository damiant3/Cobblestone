# MQTT v5.0 Protocol Reference

**Source**: OASIS Standard, https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html
**Also**: MQTT v3.1.1 (ISO/OASIS), MQTT-SN v1.2 (sensor networks)

## Control Packet Types

| Type | Name | Direction | Purpose |
|---|---|---|---|
| 1 | CONNECT | Client to Server | Connection request |
| 2 | CONNACK | Server to Client | Connection acknowledgment |
| 3 | PUBLISH | Bidirectional | Message delivery |
| 4 | PUBACK | Bidirectional | QoS 1 acknowledgment |
| 5 | PUBREC | Bidirectional | QoS 2 part 1 (received) |
| 6 | PUBREL | Bidirectional | QoS 2 part 2 (release) |
| 7 | PUBCOMP | Bidirectional | QoS 2 part 3 (complete) |
| 8 | SUBSCRIBE | Client to Server | Subscribe to topics |
| 9 | SUBACK | Server to Client | Subscription acknowledgment |
| 10 | UNSUBSCRIBE | Client to Server | Remove subscriptions |
| 11 | UNSUBACK | Server to Client | Unsubscribe confirmation |
| 12 | PINGREQ | Client to Server | Keep-alive ping |
| 13 | PINGRESP | Server to Client | Keep-alive response |
| 14 | DISCONNECT | Bidirectional | Graceful disconnect |
| 15 | AUTH | Bidirectional | Authentication exchange (v5 new) |

## QoS Levels

| Level | Guarantee | Use Case |
|---|---|---|
| 0 | At most once (fire-and-forget) | Ambient sensor data |
| 1 | At least once (may duplicate) | Telemetry where dupes are OK |
| 2 | Exactly once (4-step handshake) | Billing, actuation commands |

## v5.0 Key Features (vs v3.1.1)

- **Properties system**: Metadata on every packet type
- **Session Expiry Interval**: Configurable session persistence
- **Receive Maximum**: Flow control (concurrent QoS 1/2 limit)
- **Topic Aliases**: Integer aliases for repeated topic strings
- **Shared Subscriptions**: Load-balanced consumer groups
- **AUTH packet**: Multi-step authentication (SCRAM, etc.)
- **Reason Codes**: Granular error diagnostics (0x00 success, 0x80+ errors)
- **Will Delay Interval**: Delayed last-will publication
- **Message Expiry**: TTL per published message
- **User Properties**: Arbitrary key-value metadata

## Packet Structure

Fixed header (1-5 bytes): packet type (4 bits) + flags (4 bits) +
remaining length (1-4 bytes variable-length encoding).
Variable header: packet-type-specific fields + properties (v5).
Payload: packet-type-specific data.

## Keep Alive

Client must send a packet within the keep-alive interval. Server
closes connection after 1.5x the interval without activity.

## MQTT-SN (Sensor Networks)

MQTT-SN v1.2: publish/subscribe for non-TCP/IP networks (Zigbee,
LoRa, etc.). Uses UDP or other datagram transports. Gateway-based
architecture bridges to standard MQTT brokers.

Spec: https://www.oasis-open.org/committees/document.php?document_id=66091

## Implementation Notes for Codex

- MQTT client is an effect-typed resource: `[Network] MqttConnection`
- Linear type: connection must be closed on every code path
- QoS 2 state machine maps naturally to Codex's when/is pattern matching
- Topic aliases reduce memory allocation (no repeated string copies)
- TLS integration via existing foreword crypto stack
- Interop target: Mosquitto broker (open source, widely deployed)
