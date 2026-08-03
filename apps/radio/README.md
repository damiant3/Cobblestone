# Codex Radio

A two-deck internet radio station for a DJ, with a mixer/crossfader, EQ, playlist queue, listener chat, and broadcast toggle.

## Modules

- **RadioTypes** -- TrackState (6 states), Track with BPM/waveform, DeckId (A/B), EqBand (low/mid/high), DeckState with loop/cue/filter/pitch/mute, MixerState with crossfader/master/monitor/auto-crossfade, StationState with listener count and broadcast flag, PlaylistEntry, ChatMessage
- **RadioStation** -- RadioApp aggregate state; deck load/volume; crossfader; playlist add/next; chat with 200-message ring buffer; broadcast toggle; now-playing derivation from crossfader position; full console widget composing all panels
- **TestRadio** -- 5 test sections covering track state, deck init, mixer defaults, broadcast toggle, and time formatting

## Completeness

55% -- Core data model and widget layout are clean and complete. Missing: no `opening` entry point, no audio playback integration (codex-vm Intel HDA would be the target), no file library loading, no pitch-shift or filter DSP, no WebRTC broadcast pipeline, no BPM sync or auto-crossfade execution, no effects panel content.

## Codex Conformance

Full -- All code is Codex. Audio output would be emitted through a plug targeting codex-vm's Intel HDA interface.
