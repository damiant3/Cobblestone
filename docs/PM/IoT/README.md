# IoT Project Area

**Created**: 2026-06-12
**Purpose**: All reference material, compliance mappings, architecture
specs, and planning documents for the Codex IoT initiative.

## Directory Structure

```
docs/PM/IoT/
  README.md              -- this file
  AGENT-PROMPT.md        -- master prompt for the planning agent
  Compliance/
    EU-CRA-Summary.md         -- EU Cyber Resilience Act (law, dates, mandates)
    ETSI-EN-303-645-Summary.md -- Consumer IoT cybersecurity standard
    NISTIR-8259-Summary.md     -- NIST IoT capability baseline
    IEC-62443-Summary.md       -- Industrial automation security (SL 1-4)
  Architecture/
    RISC-V-Specs-Index.md      -- RISC-V ISA catalog + existing backend ref
    ARM-Specs-Index.md         -- ARM Cortex-M/A architecture + existing backend ref
  Protocols/
    MQTT-v5-Reference.md       -- MQTT v5.0 full protocol reference
    CoAP-RFC7252-Reference.md  -- CoAP constrained protocol reference
    LwM2M-Reference.md        -- LwM2M device management protocol
    Matter-Thread-Reference.md -- Matter/Thread smart home protocols
  Hardware/
    STM32-Reference.md        -- ARM Cortex-M target (STM32F4/H7)
    ESP32-C6-Reference.md     -- RISC-V target (WiFi 6 + BLE 5 + Thread)
    RaspberryPi-Reference.md  -- ARM Cortex-A target (Pi 4/5, gateway)
  Reference/
    MarketData.md             -- Verified market data (adversarially checked)
```

## Key Upstream Documents

- `docs/PM/Stories/Vision/CodexIoTPlan.md` — the strategic prospectus
- `docs/VisionAndVirtues.md` — founding vision and non-negotiables
- `docs/DevelopersGuide.md` — language features and syntax
- `docs/ArchitectsSketchbook.md` — memory model and register conventions
- `docs/PM/Stories/Vision/DistributedAgentOS.md` — agent-centric OS vision

## Existing Backend Implementations (in old/, read-only)

- `old/src/Codex.Emit.RiscV/` — RISC-V encoder, emitter, codegen (FIRST backend)
- `old/src/Codex.Emit.Arm64/` — ARM64 encoder, emitter, codegen, ELF writer
