# CoAP (RFC 7252) Protocol Reference

**Source**: IETF RFC 7252, https://datatracker.ietf.org/doc/html/rfc7252

## Message Types

| Type | Code | Semantics |
|---|---|---|
| CON | 0 | Confirmable -- requires ACK, exponential backoff retry |
| NON | 1 | Non-confirmable -- unreliable, no ACK needed |
| ACK | 2 | Acknowledgement of CON message |
| RST | 3 | Reset -- unable to process message |

## Method Codes

| Method | Semantics |
|---|---|
| GET | Safe, idempotent retrieval |
| POST | Non-idempotent creation/update |
| PUT | Idempotent resource replacement |
| DELETE | Idempotent resource removal |

## Response Codes (class.detail)

**Success (2.xx)**: 2.01 Created, 2.02 Deleted, 2.03 Valid,
2.04 Changed, 2.05 Content

**Client Error (4.xx)**: 4.00 Bad Request, 4.01 Unauthorized,
4.03 Forbidden, 4.04 Not Found, 4.05 Method Not Allowed,
4.13 Request Entity Too Large

**Server Error (5.xx)**: 5.00 Internal Server Error,
5.01 Not Implemented, 5.03 Service Unavailable

## Message Format (4-byte header)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|Ver| T |  TKL  |      Code     |          Message ID           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|   Token (0-8 bytes) ...
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|   Options (TLV, delta-encoded) ...
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|1 1 1 1 1 1 1 1|    Payload ...
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

- Version: 2 bits (must be 1)
- Type: 2 bits (CON/NON/ACK/RST)
- Token Length: 4 bits (0-8)
- Code: 8 bits (method or response code)
- Message ID: 16 bits (deduplication)

## Core Options

| # | Name | Format | Purpose |
|---|---|---|---|
| 3 | Uri-Host | string | Hostname |
| 7 | Uri-Port | uint | Port number |
| 11 | Uri-Path | string | Path segment (repeatable) |
| 12 | Content-Format | uint | Media type |
| 14 | Max-Age | uint | Cache freshness (default 60s) |
| 15 | Uri-Query | string | Query parameter |
| 17 | Accept | uint | Preferred response format |

## Key Extensions

- **Observe** (RFC 7641): Subscribe to resource changes
- **Block** (RFC 7959): Blockwise transfer for large payloads
- **Resource Directory** (RFC 9176): Resource registration/discovery

## Transport

- UDP port 5683 (coap://)
- DTLS port 5684 (coaps://)
- Default parameters: ACK_TIMEOUT=2s, MAX_RETRANSMIT=4,
  EXCHANGE_LIFETIME=247s

## Transmission Parameters

| Parameter | Default |
|---|---|
| ACK_TIMEOUT | 2 seconds |
| ACK_RANDOM_FACTOR | 1.5 |
| MAX_RETRANSMIT | 4 |
| NSTART | 1 |
| PROBING_RATE | 1 byte/second |

## Implementation Notes for Codex

- CoAP runs on UDP -- requires Codex UDP foreword (exists)
- 4-byte header + options = minimal memory footprint
- CON/ACK state machine maps to effect-typed state tracking
- DTLS security via existing crypto stack (ChaCha20/AES-GCM)
- Observe extension enables push-based telemetry
- Block transfer enables firmware update over constrained links
- CoRE Link Format for device/resource discovery
- Interop target: Eclipse Californium (Java, widely deployed)
