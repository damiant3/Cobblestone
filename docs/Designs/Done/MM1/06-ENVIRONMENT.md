# 06 — The Environment

## Overview

The Codex Environment is a unified application that replaces the editor, terminal, compiler, debugger, package manager, and repository browser. It is not a plugin for VS Code. It is not an IDE with a bolt-on terminal. It is a single coherent tool designed from scratch for reading, writing, verifying, and exploring Codex programs.

The bootstrap implementation is more modest — an LSP server that works with existing editors, plus a CLI. The full environment is a later milestone. But the design is here because it shapes decisions in every other layer.

> **Current status (March pi++ 2026)**: The bootstrap environment is operational:
> - **CLI** (`tools/Codex.Cli`): `codex run`, `codex build` (C#/JS/Rust), `codex check`, `codex parse`, `codex version`, plus repository commands (`init`, `publish`, `propose`, `verdict`, `vouch`, `history`)
> - **LSP server** (`src/Codex.Lsp`): diagnostics, hover (type + prose), document symbols, semantic tokens
> - **VS Code extension** (`editors/vscode`): syntax highlighting (TextMate grammar with Chapter/Section, keyword, type, operator, prose scoping), bracket matching, auto-indent, LSP client
> - **Three compilation backends**: C# (primary, self-hosting), JavaScript (Node.js), Rust
>
> The full custom environment (Reader, Writer, Verifier, Explorer as one app) remains aspirational.

---

## Components

### The Reader

**Purpose**: Present Codex source as formatted prose — like reading a book.

**What it does**:
- Renders chapters, sections, and definitions as typeset prose
- Code blocks are syntax-highlighted and properly indented
- Type signatures are formatted with alignment
- Proofs are rendered with mathematical notation where appropriate
- Cross-references are hyperlinked (click a type name to jump to its definition)
- The document outline (chapter/section structure) is always visible

**Key insight**: The Reader is not a viewer for raw source files. It is a *renderer* that takes the CST and presents it in the best possible format for human reading. The raw source is stored; the Reader presents it beautifully.

**Implementation**: The Reader is built on top of the CST. It walks the CST and produces formatted output. For the LSP/editor integration, this means providing:
- Semantic token classifications (for syntax highlighting)
- Document symbols (for the outline)
- Folding ranges (for collapsing sections)
- Inlay hints (for inferred types)

For the full environment, it means a custom rendering engine.

### The Writer

**Purpose**: Accept prose and notation input with live feedback.

**What it does**:
- As you type, the lexer and parser run continuously
- Syntax errors are highlighted inline, immediately
- Type errors appear as you finish a definition
- Auto-completion suggests:
  - Type-aware completions (functions that return the expected type)
  - Prose template completions ("An X is a record containing:")
  - Import suggestions (definitions from the repository that match)
- Refactoring support:
  - Rename (across all references)
  - Extract definition (select code → create named definition)
  - Inline definition (replace uses with body)
  - Change signature (update type and all call sites)

**Implementation**: The Writer is powered by the LSP server (`Codex.Lsp`). For the bootstrap phase, this integrates with VS Code, Neovim, or any LSP-capable editor. The full environment builds its own editor component.

### The Verifier

**Purpose**: Run the type checker and proof checker continuously, showing results inline.

**What it does**:
- Type checking runs on every change (debounced)
- Proof obligations are shown as "holes" that need to be filled
- When a proof is complete, a ✓ appears next to the claim
- Linearity errors are shown as "this resource was not consumed" or "this resource was used twice"
- Effect errors are shown as "this function uses [FileSystem] but is declared pure"
- Proof search can be invoked on demand: "find a proof for this obligation"

**Implementation**: The Verifier is the type checker (`Codex.Types`) + proof engine (`Codex.Proofs`) running in incremental mode. It caches results per definition and only re-checks what changed.

### The Explorer

**Purpose**: Navigate the repository — definitions, proposals, verdicts, trust records.

**What it does**:
- Browse all definitions in the current view
- Search by type signature ("show me all functions from List a to List a")
- Search by capability ("show me all sorting functions with O(n log n) complexity")
- View the trust profile of any definition
- View the dependency graph (what does this depend on? what depends on it?)
- View the supersession chain (version history of a definition)
- Browse proposals and their verdicts
- Issue verdicts on proposals

**Implementation**: The Explorer queries the repository index (`Codex.Repository`). In the bootstrap phase, it is a CLI command (`codex explore`). The full environment provides a graphical browser.

### The Executor

**Purpose**: Run programs and show effects as they happen.

**What it does**:
- Execute a Codex program (compile to the C# backend and run)
- Show the result
- For effectful programs, show effects as they happen (file reads, network calls, etc.)
- REPL mode: evaluate expressions interactively
- Debugger: step through execution, inspect values, see the effect stack

**Implementation**: The Executor compiles to C# (via `Codex.Emit.CSharp`), builds the output, and runs it. For the REPL, it wraps expressions in a minimal program and evaluates. The debugger is a stretch goal.

### The Narrator

**Purpose**: Explain any piece of code in plain English.

**What it does**:
- Hover over a definition → see its prose, its type, its proof record, its trust profile
- "Explain this function" → the Narrator composes the prose from the source with type information and proof status
- "What does this module do?" → the Narrator reads the chapter introduction and summarizes
- "Why was this changed?" → the Narrator reads the supersession justification

**Key constraint**: The Narrator is NOT an LLM. It is a structured explanation engine that reads the prose already in the source. Codex programs contain their own explanations. The Narrator's job is to *find and present* those explanations, not to *generate* them.

**Implementation**: The Narrator queries the semantic model (`Codex.Semantics`) and the repository (`Codex.Repository`) to compose explanations. It is a template-based system with access to the full semantic model.

### The Historian

**Purpose**: Show the full history of any definition.

**What it does**:
- Select a definition → see every version that ever existed
- See who wrote each version, when, and why
- See the structured diff between any two versions
- See the proposals and verdicts that led to each version change
- Timeline view: the definition's evolution over time

**Implementation**: The Historian queries the supersession chain and proposal/verdict records in the repository.

---

## Bootstrap Implementation Plan

The full environment is a major undertaking. For bootstrap, we build:

### Phase 1: CLI (`Codex.Cli`)

A command-line tool with subcommands:

```
codex check <file.codex>         — type-check a file, report diagnostics
codex build <file.codex>         — compile to C# and build
codex run <file.codex>           — compile, build, and execute
codex repl                       — interactive REPL
codex explain <name>             — Narrator explains a definition
codex search <type-signature>    — search repository by type
codex history <name>             — show version history
codex propose <file.codex>       — create a proposal
codex verdict <proposal-hash>    — issue a verdict
codex init                       — initialize a new Codex project
codex sync                       — synchronize with remote stores
```

### Phase 2: LSP Server (`Codex.Lsp`)

A Language Server Protocol implementation providing:
- Diagnostics (errors, warnings)
- Completion
- Hover (Narrator)
- Go to definition
- Find references
- Rename
- Document symbols (outline)
- Semantic tokens (syntax highlighting)
- Inlay hints (inferred types)
- Code actions (quick fixes)
- Folding ranges

This integrates with VS Code (via an extension), Neovim, Emacs, or any LSP client.

### Phase 3: VS Code Extension

A thin VS Code extension that:
- Activates the LSP server
- Provides Codex-specific UI:
  - The Reader panel (formatted prose view of the current file)
  - The Explorer panel (repository browser)
  - The Historian panel (version history)
- Custom syntax highlighting grammar
- Codex file icon

### Phase 4: Full Environment

The standalone Codex Environment application. Technology TBD — likely Avalonia UI (.NET cross-platform desktop) or a web-based application (Blazor).

---

## UX Principles

1. **Reading first, writing second**. The default view is the Reader. You switch to the Writer when you want to edit. Most time is spent reading.

2. **Verification is invisible when passing**. Green checkmarks fade into the background. Only failures demand attention.

3. **No modes**. There is no "build mode" or "debug mode" or "review mode." All capabilities are always available.

4. **Prose is prominent**. The prose is not gray comment text — it is the primary content. Code blocks are secondary, set in a different visual style.

5. **Everything is linked**. Every name is a hyperlink. Every type is explorable. Every fact is traceable to its source.

---

## Open Questions

1. **Full environment technology** — Avalonia (native .NET desktop), Blazor (web-based), Electron (cross-platform), or something else? Trade-offs between reach, performance, and development speed.

2. **Collaborative editing** — should the environment support real-time collaborative editing (like Google Docs)? This is orthogonal to the proposal protocol but might be useful during co-authoring.

3. **AI integration** — should the environment integrate with LLMs for prose generation, proof search, and code suggestion? The Narrator is explicitly NOT an LLM, but other components might benefit. This is a philosophical question as much as a technical one.

4. **Accessibility** — the environment must be accessible (screen readers, keyboard navigation, high contrast). This should be designed in from the start, not bolted on.
