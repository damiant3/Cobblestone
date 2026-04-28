# Editor Setup for Codex

Codex source files (`.codex`) are plain ASCII text — any editor will open them.
What follows is how to get **syntax highlighting** (and, where available,
language-server features like hover types and go-to-definition) in the editors
people actually use.

If you're a complete beginner, **VS Code is the recommended starting point** —
it has the most complete support (full LSP integration). **Sublime Text** is
the recommended choice if you want a fast, lightweight reader for browsing
large files.

| Editor | What you get | Setup time |
|---|---|---|
| [VS Code](#vs-code-recommended) | Highlighting + LSP (hover, go-to-def, completion) | ~5 min |
| [Sublime Text](#sublime-text) | Highlighting only | ~30 sec |
| [Zed / Helix / others](#other-editors) | None yet — open as plain text | n/a |

---

## VS Code (recommended)

Full setup is in [VSCODE-SETUP.md](VSCODE-SETUP.md) — read that for the
complete walkthrough including the language server (LSP). The 30-second version:

1. Open VS Code.
2. `Ctrl+Shift+P` → **Developer: Install Extension from Location...**
3. Browse to `tools\vscode` inside the repo and click **Select Folder**.
4. Reload when prompted.

That gets you highlighting immediately. The LSP server (hover types,
go-to-definition, error squiggles) needs the .NET 8 SDK plus
`dotnet build Codex.sln` — see VSCODE-SETUP.md for those steps.

---

## Sublime Text

Sublime Text doesn't need a build step. Drop one file in the right folder
and you're done. Highlighting only — no LSP integration.

### Install

1. Open Sublime Text.
2. Click **Preferences → Browse Packages...** — this opens the `Packages`
   folder in your file manager. The path is something like:
   - **Windows:** `%APPDATA%\Sublime Text\Packages\`
   - **macOS:** `~/Library/Application Support/Sublime Text/Packages/`
   - **Linux:** `~/.config/sublime-text/Packages/`
3. Inside `Packages`, create a new folder called `Codex`.
4. Copy `tools\sublime\Codex.sublime-syntax` (from this repo) into that new
   folder. The final path should be `Packages/Codex/Codex.sublime-syntax`.
5. Open any `.codex` file — highlighting appears immediately. No restart needed.

### Verify it's working

Open the bottom-right corner of the Sublime window. With a `.codex` file
focused, the syntax indicator should say **Codex**. If it says **Plain Text**,
click it and pick **Codex** from the dropdown — Sublime probably needs to
re-scan the Packages folder, which a click does.

### Why no LSP?

The Codex language server (the thing that powers go-to-definition and hover
types in VS Code) speaks the [LSP protocol](https://microsoft.github.io/language-server-protocol/),
and Sublime supports LSP via the **LSP package** by sublimehq. Setting that
up against the Codex server is possible but is a bigger project than this
walkthrough — please open an issue if you want it.

---

## Other editors

### Zed

Zed extensions require a [tree-sitter](https://tree-sitter.github.io/) grammar.
We don't have one yet — Codex's grammar is hand-rolled (see
`Codex.Codex/Syntax/Lexer.codex` and `Parser.codex`). Until someone writes a
tree-sitter port, Zed will open `.codex` files as plain text without
highlighting. The files are still readable — just visually flat.

If you want to volunteer a tree-sitter grammar, the keyword set in
`tools/vscode/syntaxes/codex.tmLanguage.json` is the source of truth.

### Vim / Neovim

No bundled syntax file. The same situation as Zed — you can open and edit
`.codex` files but won't get highlighting. If you write a `codex.vim`
syntax file, it can live alongside the others under `tools/vim/`.

### Helix

Helix uses tree-sitter exclusively, same constraint as Zed.

### Visual Studio (the IDE, not Code)

There's a `tools/Codex.VsExtension/` folder for the full Visual Studio IDE,
but it's not actively maintained — VS Code is the primary target. The
syntax grammar is kept in sync, so if you do load it, highlighting works.
Project templates and the LSP integration may lag behind VS Code.

---

## Adding a new editor

The grammar that drives all of this is **one file**:
`tools/vscode/syntaxes/codex.tmLanguage.json` (TextMate JSON format). The
Sublime `.sublime-syntax` is a hand-translated version of the same patterns.

If you add a new editor:

1. Read `Codex.Codex/Syntax/Token.codex` for the canonical token-kind list.
2. Read `Codex.Codex/Syntax/Lexer.codex`'s `classify-word` function for the
   actual keyword set (some token kinds in `Token.codex` are defined but
   not currently emitted by the lexer — don't list those as keywords).
3. Translate `tools/vscode/syntaxes/codex.tmLanguage.json` to your editor's
   syntax format.
4. Drop the artifact under `tools/<editor>/` and add a section to this doc.

When the language adds a new keyword, the chain is: Token.codex →
Lexer.codex → tmLanguage.json → all derived syntax files. Update them in
that order to keep them in sync.
