# IEC 62443 — Industrial Automation and Control Systems Security

**Source**: IEC, Wikipedia (structure confirmed)
**Status**: Active; key parts published 2013-2024, some in development

## Standard Structure

### General (Series 1)
| Part | Title | Year |
|---|---|---|
| 62443-1-1 | Concepts and models | 2009 (TS) |
| 62443-1-5 | Scheme for security profiles | 2023 (TS) |
| 62443-1-6 | Industrial Internet of Things | In development |

### Policies & Procedures (Series 2)
| Part | Title | Year | Audience |
|---|---|---|---|
| 62443-2-1 | Security program requirements for IACS asset owners | 2024 (Ed 2) | Asset owners |
| 62443-2-3 | Patch management in IACS environment | 2015 (TR) | Asset owners |
| 62443-2-4 | Requirements for IACS service providers | 2023 (Ed 2) | Integrators |

### System (Series 3)
| Part | Title | Year | Audience |
|---|---|---|---|
| 62443-3-1 | Security technologies for IACS | 2009 (TR) | System designers |
| 62443-3-2 | Security risk assessment and system design | 2020 | System designers |
| 62443-3-3 | System security requirements and security levels | 2013 | System integrators |

### Component (Series 4) — MOST RELEVANT TO CODEX
| Part | Title | Year | Audience |
|---|---|---|---|
| 62443-4-1 | Secure product development lifecycle requirements | 2018 | Product suppliers |
| 62443-4-2 | Technical security requirements for IACS components | 2019 | Product suppliers |

### Evaluation (Series 6)
| Part | Title | Year |
|---|---|---|
| 62443-6-1 | Security evaluation methodology for 2-4 | 2024 (TS) |
| 62443-6-2 | Evaluation methodology for 4-2 | In development |

## Security Levels

| Level | Protection Against |
|---|---|
| SL 0 | No special requirement |
| SL 1 | Unintentional misuse |
| SL 2 | Simple intentional attacks with minimal resources |
| SL 3 | Sophisticated attacks with moderate resources |
| SL 4 | Advanced threats with extensive resources (nation-state) |

## Seven Foundational Requirements (FR)

1. **FR 1**: Identification and Authentication Control
2. **FR 2**: Use Control (authorization)
3. **FR 3**: System Integrity
4. **FR 4**: Data Confidentiality
5. **FR 5**: Restricted Data Flow
6. **FR 6**: Timely Response to Events — Codex mapping:
   `[HardRealtime]` compile-time WCET proofs guarantee bounded
   response time. Deadline miss detection via watchdog with fact
   store logging provides auditable evidence.
7. **FR 7**: Resource Availability — Codex mapping: linear types
   guarantee no resource leaks; `[HardRealtime]` guarantees bounded
   memory (no OOM) via region-only allocation with no heap. Effect
   types prevent unauthorized resource consumption.

## Zones and Conduits Model

System divided into zones (groups of assets with common security
requirements) connected by conduits (secure communication channels
between zones of different security levels).

## Codex Relevance

IEC 62443-4-1 (secure development lifecycle) and 62443-4-2 (component
security requirements) are the parts Codex must satisfy for industrial
IoT. The compile-time guarantees (memory safety, effect discipline,
signed binaries) map directly to FR 1-5. The trust lattice with
capability leases maps to the zones and conduits model.
