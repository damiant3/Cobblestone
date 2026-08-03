# Codex Collab

A real-time video collaboration platform providing audio/video calls, screen sharing, meeting rooms, in-call chat, whiteboard, presence tracking, and recording. Integrates with Vision, Helm, Browser, Diagram, and Calendar.

## Modules

- **CollabTypes** -- Full type system: PresenceStatus, CollabUser, Call/CallParticipant/ParticipantRole, MeetingRoom, AgendaItem, ScreenShare/ShareSource/ShareQuality, Whiteboard, Recording, CallChat, Reactions, CallHistory
- **CallEngine** -- Complete call lifecycle: creation, join/leave, mute/video/hand toggles, mute-all, screen share, role promotion, recording, call statistics
- **MeetingManager** -- Meeting creation, instant meeting, start/end/join/leave, agenda management with progress tracking, notes, settings (password, waiting room, auto-record, participant limit)
- **ScreenShare** -- ShareSession management, annotation overlay (6 types), remote control grant/revoke, quality/FPS settings, whiteboard with stroke add/undo/clear
- **CallPage** -- Widget tree: call header with duration, participant grid, controls toolbar, side panel with participant list and chat
- **CollabStore** -- JSON persistence of call history (kind 47), contacts (kind 48), meeting templates (kind 49) via DiskFacts
- **opening** -- Full event loop: keyboard dispatch for nav views and in-call controls, chat compose, call start/end with history persistence

## Completeness

75% -- All core data structures, lifecycle logic, UI widgets, and persistence are implemented. Missing: actual audio/video codec integration (placeholder), whiteboard UI widget (types exist but no rendering), reaction dispatch, contacts view, settings view, waiting room enforcement, recording URL storage.

## Codex Conformance

Full -- Written entirely in Codex. Media transport would be emitted through plugs targeting WebRTC or codex-vm xHCI UVC/HDA hardware.
