# Codex Mail

A three-pane email client (folder sidebar, message list, reading/compose pane) with end-to-end encryption support planned.

## Features

- Folder sidebar with Inbox, Sent, Drafts, Trash and "Compose" button
- Message list built dynamically from state keys with unread indicator and active-mail highlighting
- Reading pane showing subject, from/date, body, and Reply/Forward/Delete buttons
- Compose panel with To/Subject fields and Send/Save Draft buttons
- Five sample messages loaded at startup

## Completeness

50% — Three-pane layout and folder structure are solid. Sample data loads and compose open/close cycle works. Clicking a message item does not set active-mail (reading pane always shows empty state). Folder switching does not filter. Reply/Forward return 0. Encryption claim has no implementation. Compose panel lacks a body textarea.

## Codex Conformance

Full — Codex throughout with DOM/state stubs correctly declared at the plug boundary.
