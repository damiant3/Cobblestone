# Sublime Text syntax for Codex

`Codex.sublime-syntax` is the Sublime Text grammar for `.codex` files. It
mirrors `tools/vscode/syntaxes/codex.tmLanguage.json` — when one changes,
update the other.

## Install

Copy `Codex.sublime-syntax` into a new `Codex/` folder under Sublime's
`Packages` directory (Preferences → Browse Packages…). Open a `.codex` file
to verify; the syntax indicator at the bottom-right should say **Codex**.

Full walkthrough with screenshots-equivalent steps: [docs/User/EDITORS.md](../../docs/User/EDITORS.md#sublime-text).

## Highlighting only

This package gives you syntax highlighting. There's no LSP integration —
that lives in the VS Code extension. Run that one if you want hover types,
go-to-definition, and completion.
