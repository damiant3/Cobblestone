# CCE Encoding Integration — Platform Support

**Date**: 2026-03-26
**Author**: Agent Linux
**Status**: Design — future work

---

## The Problem

Files produced by the Codex compiler's internal diagnostics (`type-diag.txt`,
`unify-errors.txt`) contain raw CCE-encoded text. When Codex.OS becomes the
host, all text on the system will be CCE-native. External tools — text editors,
`cat`, `grep`, `diff`, hex editors, debuggers — display garbage unless they know
about CCE.

Today the blast radius is small: `.codex` source files and program output are
stored as UTF-8 (the compiler converts at I/O boundaries). But as the system
grows, CCE awareness needs to reach the host platform's encoding infrastructure.

## What Exists on Each Platform

### Linux: gconv modules + iconv + charmap files

Linux encoding support is built on three layers:

1. **`iconv`** — the standard command-line converter. Uses `-f` / `-t` to name
   encodings. Can also accept charmap file paths directly.

2. **gconv modules** — shared libraries (`.so`) loaded by `iconv_open(3)` at
   runtime. Live in `/usr/lib/gconv/` (or `/usr/lib64/gconv/`). Configured
   via `gconv-modules` text file. Custom modules can be loaded via the
   `GCONV_PATH` environment variable.

3. **charmap files** — POSIX-defined text files that describe a character set.
   Map symbolic character names to byte values. Used by `localedef` and `iconv`.
   Live in `/usr/share/i18n/charmaps/`.

**What we'd provide:**

- `CCE.charmap` — POSIX charmap file mapping CCE byte 0-127 to Unicode names.
  Enables `iconv -f ./CCE.charmap -t UTF-8 < file.cce > file.txt`.

- `cce_gconv.so` — gconv module that registers "CCE" as a named encoding.
  After installation: `iconv -f CCE -t UTF-8 < file.cce`. About 50 lines of C
  wrapping the 128-entry lookup table.

- Install path: `/usr/lib/gconv/cce_gconv.so` + entry in `gconv-modules`.
  Or user-local via `GCONV_PATH=~/.codex/gconv`.

### Windows / .NET: EncodingProvider

.NET's encoding system is extensible via `System.Text.Encoding.RegisterProvider`:

1. Subclass `EncodingProvider`, implement `GetEncoding(string name)` and
   `GetEncoding(int codepage)`.

2. Call `Encoding.RegisterProvider(new CceEncodingProvider())` at startup.

3. Now `Encoding.GetEncoding("CCE")` works throughout the .NET process —
   `StreamReader`, `StreamWriter`, `File.ReadAllText`, etc.

**What we'd provide:**

- `CceEncoding : Encoding` — custom `System.Text.Encoding` subclass. Implements
  `GetBytes`, `GetChars`, `GetByteCount`, `GetCharCount` using the 128-entry
  lookup table. Assigns a private-use code page number (e.g., 65400).

- `CceEncodingProvider : EncodingProvider` — registers "CCE" and "codex" as
  encoding names.

- NuGet package: `Codex.Text.Encoding.CCE`. One-liner to enable:
  `Encoding.RegisterProvider(CceEncodingProvider.Instance);`

### Editors

**VS Code**: Custom encoding support via extensions. A `codex-encoding`
extension would register CCE as a named encoding and handle file open/save
conversion. The extension API supports `registerTextDocumentContentProvider`
and custom file system providers.

**Vim/Neovim**: Can set `fileencoding` per-buffer. A `cce.vim` plugin would
add CCE to the encoding list and provide `strwidth` overrides.

**JetBrains**: Custom encoding support via plugin. Register CCE in the
IDE's charset list.

**Notepad++ / Sublime**: Plugin ecosystems support custom encodings.

**Codex.Editor** (future): Native CCE. No conversion needed.

## Phased Approach

### Phase 0: CLI converter (immediate, low effort)

A `codex encode` command:

```
codex encode --from utf8 --to cce < input > output
codex encode --from cce --to utf8 < input > output
```

Pure .NET, no platform dependencies. Uses the same lookup table already in the
compiler. Useful for inspecting diagnostic files right now.

### Phase 1: .NET EncodingProvider (near-term)

NuGet package with `CceEncodingProvider`. Enables CCE in any .NET tool:
`dotnet-dump`, `dotnet-trace`, custom analyzers. 10-20 lines of registration
code.

### Phase 2: Linux gconv module (medium-term)

`cce_gconv.so` + charmap file. Enables `iconv -f CCE -t UTF-8`, which means
`file`, `less`, `grep --encoding=CCE`, and any gconv-aware tool works. About
100 lines of C.

### Phase 3: Editor extensions (medium-term)

VS Code extension first (largest user base), vim plugin second. Each is a thin
wrapper around the lookup table + file I/O hooks.

### Phase 4: Codex.OS native (long-term)

When Codex.OS is the host, there's no conversion. The terminal, filesystem,
and all tools speak CCE natively. The editor IS the Codex editor. Unicode
exists only at the network boundary (receiving email, web content, etc.) and
in the Clarifier (translating between human languages).

## Design Constraint

The lookup table is the single source of truth. Every integration artifact —
charmap file, gconv module, .NET Encoding, editor plugin — is generated from
the same 128-entry array. When the encoding evolves (CCE v2), regenerate all
artifacts from the new table.

This is the same principle as the compiler: the CCE table defines the encoding,
everything else is derived.

## File Identification

How does a tool know a file is CCE-encoded?

| Method | Mechanism | When |
|--------|-----------|------|
| BOM | First 3 bytes: `0xCC 0xCE 0x01` (version 1) | Foreign filesystems |
| Extension | `.cce` suffix on raw CCE text files | Convention |
| Metadata | Filesystem extended attribute `user.encoding=cce-v1` | Linux (xattr) |
| Capability | OS-level metadata per inode | Codex.OS |
| Content | Heuristic: valid UTF-8 fails, all bytes 0-127 | Fallback detection |

For the compiler's diagnostic files, the simplest fix is adding the BOM or
changing the compiler to write them as UTF-8 (converting at the boundary like
everything else). The latter is probably the right short-term answer — these
files exist for human debugging, not machine consumption.
