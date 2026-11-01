# Foreword UI -- open capabilities

Quire-domain backlog for `codex/foreword/ui`. An entry says what is still
missing and nothing else; a closed entry is DELETED.

## Editor carries only the widget surface

The editing, search, replace and key API had no caller and was deleted in CL 50624 (root's ruling: `apps/works/ConsoleEditor` owns editing); `p4 describe 50624` holds it. What remains is the surface `codex/test/lib/editor-surface` pins.
