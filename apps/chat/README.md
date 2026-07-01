# Codex Chat

A browser-based end-to-end-encrypted messaging app with a Signal-style interface: conversation list sidebar, message thread area, contacts list, settings, and a voice/video call overlay.

## Modules

- **ChatPage** — Complete application logic: sidebar (conversation list with avatars, unread badges, search), chat area (message thread with encryption notice, compose bar), call overlay, 3-tab bottom nav, auth flow (token + `/api/chat/me` or register), 3-second polling for new messages
- **ChatTheme** — Dark theme with Signal-inspired palette, complete StateStyles, full CSS, all runtime stubs
- **web/chat.html** — Pre-compiled JavaScript output: the entire Codex application transpiled to JS, inlined with runtime bootstrap
- **web/server.ps1** — PowerShell HTTP server to serve the app and proxy API calls

## Completeness

65% — UI is complete and fully rendered from Codex source: all views, all widget trees, auth flow, conversation/message/contact loading, send, call overlay. The chat.html artifact is a working browser app. Missing: no server implementation (no ChatServer.codex), so `/api/chat/*` endpoints are unimplemented. Contact selection and mute toggle are stubs. E2E encryption is display-only. Voice/video call signaling absent beyond overlay state.

## Codex Conformance

Partial — Application logic and UI are written in Codex. The chat.html represents a JS transpilation following the "emit through plugs" model. However, the compiled JS is checked in as a static artifact (could diverge from source), and the runtime bootstrap (widget renderer, state store, fetch wrappers) is hand-authored JavaScript.
