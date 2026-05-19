# Codex User's Handbook

## VS Code Setup

Syntax highlighting, error squiggles, hover types, go-to-definition,
and completion for `.codex` files in Visual Studio Code.

### Prerequisites

1. **.NET 8 SDK** — `dotnet --version` should show `8.x.x` or higher.
2. **Node.js (LTS)** — `node --version` and `npm --version` should work.
3. **Visual Studio Code**.

### Build the extension (one-time)

```powershell
cd tools\vscode
npm install
npm run compile
```

### Install into VS Code

**Option A — Development mode (quickest)**

1. `Ctrl+Shift+P` → **"Developer: Install Extension from Location..."**
2. Browse to `tools\vscode` inside the repo root → **Select Folder**.
3. Reload when prompted.

**Option B — Package and install**

```powershell
cd tools\vscode
npm install -g @vscode/vsce
vsce package
```

Then `Ctrl+Shift+P` → **"Extensions: Install from VSIX..."** → select the `.vsix`.

### What you get

| Feature | Trigger |
|---------|---------|
| Syntax highlighting | Automatic on `.codex` files |
| Error squiggles | On save or change |
| Hover types | Mouse over any name |
| Go to definition | `F12` |
| Peek definition | `Alt+F12` |
| Completion | `Ctrl+Space` |
| Document outline | Explorer sidebar → Outline |

### Using a pre-built server (faster startup)

```powershell
dotnet publish src\Codex.Lsp\Codex.Lsp.csproj -c Release -r win-x64 --self-contained -o out\lsp
```

Set `codex.serverPath` in VS Code settings to the produced executable path.

### Troubleshooting

- **No highlighting** — check file extension is `.codex`, language mode shows "Codex" in status bar.
- **No squiggles/hover** — `View → Output → Codex Language Server` for errors. Run `dotnet build Codex.sln`.
- **"spawn dotnet ENOENT"** — .NET not on PATH. Restart VS Code or set `codex.serverPath`.
- **Missing project file** — open the repo root folder, not a subfolder.
