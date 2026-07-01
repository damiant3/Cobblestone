# Codex Notes

A two-pane note-taking app with a folder sidebar on the left and a rich-text-style editor on the right, backed by local-storage stubs for persistence.

## Features

- Folder sidebar with All Notes, Favorites, Archive categories
- Note list with title, date, snippet, and active-note highlighting
- Editor pane with toolbar (Bold, Italic, H1, H2, Bullet, Code, Delete)
- "New Note" button creates a blank entry
- Five sample notes pre-loaded

## Completeness

55% — Dynamic note list and new-note creation rendering are functional. Clicking a note to select it is not wired (only New Note button fires). Toolbar buttons are no-ops. No actual text editing (editor body is a label, not an input). Persistence stubs declared but unused.

## Codex Conformance

Partial — Full Codex source with stubs for all platform calls. Local-storage stub declared but not yet called.
