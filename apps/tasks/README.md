# Codex Tasks

A five-column Kanban board (Backlog, To Do, In Progress, Review, Done) with priority-colored cards and a modal prompt for adding new tasks.

## Features

- Five columns dynamically built from state keys with cards showing priority-colored left border, title, description, tag badge, and avatar
- "+ Add card" affordance per column
- "New Task" button triggers a prompt dialog; callback adds a card to the To Do column
- 10 sample tasks pre-loaded across all 5 columns (including real Codex project tasks)

## Completeness

55% -- Dynamic card rendering from state is the most fully realized data model of the single-file apps. New-task creation logic is complete on the Codex side, blocked only by the `show-prompt` stub. No drag-and-drop. Clicking existing cards does nothing.

## Codex Conformance

Partial -- Codex source; `show-prompt` is stubbed as a platform call (consistent with plug model).
