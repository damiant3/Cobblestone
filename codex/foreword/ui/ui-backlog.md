# Foreword UI -- open capabilities

Quire-domain backlog for `codex/foreword/ui`. An entry says what is still
missing and nothing else; a closed entry is DELETED.

## Editor: the editing, search and key API has no caller and four defects

Consumers reach only `editor-with-text`, `editor-widget` and
`ed-show-line-numbers` (`codex/test/lib/editor-surface` pins those). The rest is
uncalled (L-UNCALLED), source-read 2026-10-20 and not run:

- `editor-search` with an empty query leaves `ed-search-current` at its old
  index, so a following `editor-replace-current` reads `list-at` on an empty
  match list.
- `editor-replace-all` replaces only the first occurrence on each line, and with
  an empty query `ed-replace-first` prepends the replacement to every line.
- `editor-handle-key` passes key codes 32 to 126 to `code-to-char`, which is
  the identity on a CCE code, so printable keys insert the wrong characters.
- Every operation writes its input through `__record-set` and `list-set-at`
  (L-ALIAS), so a state kept before an edit changes with it.

Fix or delete before the first caller.
