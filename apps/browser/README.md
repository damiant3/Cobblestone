# Codex Browser

A bare-metal web browser built from first principles on the Codex stack: no HTML, no CSS, no JavaScript, no certificate authorities. Pages are compiled Codex programs with effect-typed capability profiles, delivered over content-addressed channels and rendered through a flex widget tree to VBE/HDA hardware.

## Modules

- **opening** — Entry point; loads disk-persisted history and trust store, initialises display, launches the persistent event loop
- **Browser** — Main state machine: tab bar, address bar, chrome layout, trust prompt UI, mouse hit-testing, keyboard dispatch, render pipeline
- **Tab** — Per-tab state: navigation history, scroll, loading/error/empty page templates, capability tracking, media player integration
- **AddressBar** — Full text editor with cursor, selection, backspace/delete, address normalisation, autocomplete suggestion list
- **BrowserEvent** — Keyboard shortcut mapping (Ctrl+T/W/L/R, Alt+Left/Right, F5, scroll/page keys), event polling via `uefi-read-key`
- **BrowserTheme** — Chrome colour palette, trust-tier indicator colours, page default colours, spacing and layout constants
- **ContentAddress** — CID-inspired 38-byte content hash format (SHA-256, codec-tagged), `codex://` address parsing, Ed25519-signed name records
- **TrustManager** — Trust store (grant/lookup/escalation), five capability tiers, Ed25519 signature verification
- **PageSandbox** — Capability gate (auto-trust Static, prompt higher tiers, source size gate), EROS factory axioms
- **PageRuntime** — Address resolution pipeline, built-in page registry (newtab, hello, about, dashboard, trust info, history, settings, media demo)
- **PageFetcher** — HTTP GET pipeline, response status handling, content-hash verification (SHA-256 header check)
- **DataChannel** — Binary wire protocol (uint16/32/64 LE encode/decode, DATA message framing), channel manager
- **ContentCache** — LRU content-hash-keyed page cache (64 entries default)
- **History** — Visit recording with dedup and visit counting, prefix search for address bar autocomplete
- **BrowserPersist** — Tab-separated serialisation of history and trust decisions into DiskFacts (kinds 20/21)
- **Display** — Bridges in-memory Framebuf to VBE linear framebuffer via `gfx-put-pixel`; dirty-rect optimised blit
- **AudioOutput** — Intel HDA bare-metal driver: GCTL reset, BDL setup, 48 kHz/16-bit/stereo stream configuration, PCM DMA write
- **MediaPlayer** — Video (keyframe + RLE delta decode, nearest-neighbor scaling), audio (PCM and WAV decode, HDA flush), player state machine
- **pages/hello** — Minimal static page demonstrating the widget tree pipeline
- **pages/about** — Browser info and capability tier summary
- **pages/dashboard** — System status dashboard with inline static data
- **pages/media-demo** — Media player demo page with mock video area and control layout

## Completeness

65% — All major subsystems are designed and implemented: browser state machine, tab management, address bar with autocomplete, trust prompt UI, capability tiers, LRU content cache, browsing history with persistence, binary data channel protocol, Intel HDA audio driver, VBE display blit, and a working media player. Six integration stubs remain (event polling returns timer ticks only; PageFetcher does not transmit over TCP; remote NamedAddress loading is stubbed; CDX cache loading is stubbed; in-browser .codex compilation is deferred). No bookmarks, no TrueType fonts, no multi-process tab isolation.

## Codex Conformance

Full — Every file is pure Codex. Hardware access goes through foreword primitives. Network I/O routes through the Codex Net foreword. Persistence uses DiskFacts/AppPersist. No foreign-language fragments appear anywhere.
