# MCP Server Design — Codex as an AI Tool Provider

**Date**: 2026-03-20 (verified via system clock)
**Status**: Phase 1 Complete — core tools implemented, VS Code + Claude Desktop configs shipped

---

## What Is MCP?

The Model Context Protocol (MCP) is an open standard from Anthropic that lets AI models
call external tools via a structured JSON-RPC interface. Instead of the AI guessing about
your code, it can ask your tools directly: "type-check this file," "what does this function
return," "compile this and show me the errors."

LSP is for editors. MCP is for AI agents.

An MCP server exposes **tools** (functions the AI can call), **resources** (data the AI
can read), and **prompts** (templates for common interactions). The AI client (Claude,
Copilot, or any MCP-compatible agent) discovers and invokes these at runtime.

---

## Why Codex Needs This

Right now, when an AI agent works on Codex source, it does this:

```
1. Read a .codex file (text)
2. Guess what the types are based on training data
3. Suggest a change
4. Run `dotnet build` and parse terminal output for errors
5. Repeat
```

With an MCP server, it would do this:

```
1. Call codex/check to get precise diagnostics with spans and types
2. Call codex/hover to get the exact type of any expression
3. Call codex/complete to get valid completions at a position
4. Make an informed change
5. Call codex/check to verify
```

The difference: the AI never guesses. It asks the compiler. Every interaction is
type-checked, span-accurate, and semantically precise.

---

## Proposed Tools

### Core Compilation

| Tool | Input | Output | Description |
|------|-------|--------|-------------|
| `codex/check` | `{ file: string }` | Diagnostics array | Type-check a file, return errors/warnings with spans |
| `codex/build` | `{ path: string, targets?: string[] }` | Build result + output paths | Compile to one or more targets |
| `codex/run` | `{ file: string }` | Stdout + exit code | Compile and execute |
| `codex/parse` | `{ file: string, mode: "tokens" \| "cst" \| "ast" }` | Parse tree (JSON) | Inspect parse output at any stage |

### Type Information

| Tool | Input | Output | Description |
|------|-------|--------|-------------|
| `codex/hover` | `{ file, line, col }` | Type string + docs | Type of expression at position |
| `codex/definition` | `{ file, line, col }` | Location (file, line, col) | Go to definition |
| `codex/complete` | `{ file, line, col }` | Completion items | Valid completions at position |
| `codex/signature` | `{ name: string }` | Type signature | Look up any definition's type |

### Repository (Future — V1+)

| Tool | Input | Output | Description |
|------|-------|--------|-------------|
| `codex/facts` | `{ query: string }` | Fact list | Search the fact store |
| `codex/history` | `{ name: string }` | Fact history | All versions of a definition |
| `codex/propose` | `{ fact, justification }` | Proposal ID | Submit a change proposal |

### Bootstrap

| Tool | Input | Output | Description |
|------|-------|--------|-------------|
| `codex/bootstrap` | `{ stage?: number }` | Bootstrap result | Run self-hosting verification |
| `codex/diff-stages` | `{}` | Diff summary | Compare Stage 1 and Stage 2 output |

---

## Proposed Resources

Resources are data the AI can read without calling a tool.

| Resource | URI pattern | Description |
|----------|-------------|-------------|
| Source files | `codex://source/{path}` | Read any `.codex` file |
| Prelude | `codex://prelude/{module}` | Read a stdlib module |
| Type environment | `codex://types` | All types in scope for a file |
| Builtins | `codex://builtins` | All built-in functions with signatures |
| Project config | `codex://project` | `codex.project.json` contents |

---

## Implementation

### Architecture

```
AI Agent (Claude/Copilot)
    ↕ MCP protocol (JSON-RPC over stdio)
Codex MCP Server (C# or Codex)
    ↕ calls into
Codex.Cli / Codex.Lsp internals
    ↕
Compiler pipeline (Syntax → Types → IR → Emit)
```

The MCP server is a thin adapter over the same compiler pipeline the CLI and LSP use.
It doesn't duplicate logic — it exposes the existing `Analyzer`, `TypeChecker`, and
`Emitter` through MCP's tool interface.

### Where It Lives

```
tools/
├── Codex.Cli/           ← existing CLI
├── Codex.Bootstrap/     ← existing bootstrap
└── Codex.Mcp/           ← NEW: MCP server
    ├── Codex.Mcp.csproj
    ├── Program.cs        ← stdio JSON-RPC loop
    ├── ToolHandlers.cs   ← codex/check, codex/build, etc.
    └── ResourceHandlers.cs
```

### Transport

MCP supports stdio (like LSP). The server reads JSON-RPC requests from stdin and writes
responses to stdout. This is the same pattern as `Codex.Lsp` — in fact, much of the
plumbing can be shared.

### Relationship to LSP

LSP and MCP serve different clients but use the same compiler internals:

| | LSP | MCP |
|---|-----|-----|
| Client | Editor (VS Code, VS) | AI agent (Claude, Copilot) |
| Protocol | LSP (JSON-RPC) | MCP (JSON-RPC) |
| Transport | stdio | stdio |
| Focus | Real-time editing (didOpen, didChange) | Request/response (check this, build this) |
| State | Tracks open documents, incremental updates | Stateless per request |
| Shared code | `Analyzer`, `DocumentStore` | `Analyzer`, compiler pipeline |

Long term, the LSP and MCP servers could be the same process with two protocol handlers.
Short term, a separate `Codex.Mcp` project is simpler.

---

## Client Configuration

### Claude Desktop (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "codex": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Projects/NewRepository/tools/Codex.Mcp/Codex.Mcp.csproj", "--no-build"],
      "env": {}
    }
  }
}
```

### VS Code (via MCP extension)

```json
{
  "mcp.servers": {
    "codex": {
      "command": "dotnet",
      "args": ["run", "--project", "${workspaceFolder}/tools/Codex.Mcp/Codex.Mcp.csproj", "--no-build"]
    }
  }
}
```

### Copilot in VS (when MCP support ships)

Likely similar to the VS Code config. The MCP spec is designed to be editor-agnostic.

---

## Scope and Phasing

### Phase 1 — Core (build this first)

- `codex/check` — the single most valuable tool for AI-assisted development
- `codex/build` — compile to any target
- `codex/hover` — type at position
- Builtins resource — so the AI knows what's available

This is ~200 lines of C#. One afternoon.

### Phase 2 — Navigation

- `codex/definition`, `codex/complete`, `codex/signature`
- Source and prelude resources
- These reuse LSP handler logic directly

### Phase 3 — Repository Integration (V1+)

- `codex/facts`, `codex/history`, `codex/propose`
- These don't exist yet — they come after the V1 views work

### Phase 4 — Self-Hosted MCP Server

Write the MCP server in Codex itself. The server that helps AI agents write Codex
is itself written in Codex. The snake eats another tail.

---

## What This Enables

With an MCP server, any MCP-compatible AI can:

- **Write Codex code with zero hallucination about types** — it asks the compiler
- **Refactor with confidence** — check before and after, see exactly what changed
- **Explore the codebase semantically** — "what functions return `Maybe a`?" becomes a tool call
- **Run the bootstrap** — verify self-hosting without parsing terminal output
- **Contribute to the repository** — submit proposals through the formal process

This is the bridge between the Intelligence Layer manifesto and the Codex development
workflow. The AI doesn't work *around* the compiler — it works *through* it.

---

## Dependencies

- .NET 8 SDK (already required)
- An MCP client library for .NET, or a minimal hand-rolled JSON-RPC handler
  (the protocol is simple enough that a hand-rolled version is ~100 lines)
- The existing compiler pipeline (no new compiler work needed)

---

## Decision Required

**Name**: `Codex.Mcp` vs integrating into `Codex.Cli` as a `codex mcp` subcommand.

The subcommand approach (`codex mcp --stdio`) is simpler to distribute — one tool,
multiple modes. But it couples the CLI's argument parsing to the server lifecycle.
Separate project is cleaner for now, can merge later.

Recommendation: start with `Codex.Mcp` as a separate project, add a `codex mcp` alias later.
