# Codex Language Extension

Syntax highlighting, project templates, and build integration for `.codex` files in Visual Studio 2022 and VS Code.

## Features

- **Syntax highlighting** for all Codex constructs (keywords, types, operators, strings, etc.)
- **Bracket matching** and **auto-closing pairs**
- **Smart indentation** for `let`, `if`, `when`, `act`, `where` blocks
- **Project template** — File > New > Project > search "Codex" > **Codex Project**
- **Build menu integration** — Ctrl+Shift+B compiles `.codex` files via the Codex CLI
- **Multi-target compilation** — C#, JavaScript, Rust, Python, C++, Go, Java, Ada, Fortran, COBOL, Babbage

## Install in Visual Studio 2022

Run the installer script:

```powershell
pwsh -File tools/Codex.VsExtension/install-vs.ps1
```

Then **restart Visual Studio**. This installs:
1. Syntax highlighting for `.codex` files (TextMate grammar)
2. Project template in **File > New > Project** (search for "Codex")

### Prerequisites for building

The project template runs `codex build .` when you press Build. You need `codex` on your PATH:

```powershell
# Option 1: Build the CLI and create an alias
dotnet build tools/Codex.Cli/Codex.Cli.csproj
Set-Alias codex 'dotnet run --project tools/Codex.Cli'

# Option 2: Use dotnet tool (when published)
dotnet tool install -g codex
```

## Creating a Codex Project

### From Visual Studio

1. **File > New > Project** (or File > New > Project from Template)
2. Search for **"Codex"**
3. Select **Codex Project**
4. Choose a name and location, click **Create**
5. Edit `main.codex`
6. Press **Ctrl+Shift+B** to compile — output appears in the Output window

### From the command line

```powershell
dotnet new codex -n MyProject
cd MyProject
codex build .
```

### What a Codex project looks like

```
MyProject/
├── MyProject.csproj        ← VS project file (SDK-style, opens in Solution Explorer)
├── codex.project.json      ← Codex project config (name, version, target, sources)
└── main.codex              ← Source code
```

The `.csproj` uses `Microsoft.NET.Sdk` as its base so VS can load it natively. A custom MSBuild target hooks into the Build action to call `codex build .`.

## How it works

The `.csproj` file is a standard SDK-style project that VS understands:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <CodexTarget>csharp</CodexTarget>
  </PropertyGroup>

  <ItemGroup>
    <None Include="**\*.codex" />
    <Compile Remove="**\*" />
  </ItemGroup>

  <Target Name="CodexBuild" BeforeTargets="Build">
    <Exec Command="codex build ." WorkingDirectory="$(MSBuildProjectDirectory)" />
  </Target>
</Project>
```

- VS loads it as a .NET project → Solution Explorer, Build menu, etc. all work
- `.codex` files appear in Solution Explorer as `None` items (with syntax highlighting)
- The `CodexBuild` target runs `codex build .` before the normal Build action
- `codex.project.json` controls the Codex-specific settings (name, target backend, sources)

## VSIX (syntax highlighting only)

If you only want syntax highlighting without the project template:

```powershell
pwsh -File tools/Codex.VsExtension/build-vsix.ps1
# Then double-click out/CodexLanguage.vsix
```

## Install in VS Code

Copy this folder to your VS Code extensions directory:

```powershell
# Windows
Copy-Item -Recurse tools/Codex.VsExtension "$env:USERPROFILE\.vscode\extensions\codex-language"

# macOS / Linux
cp -r tools/Codex.VsExtension ~/.vscode/extensions/codex-language
```

Then restart VS Code.

## Color mapping

| Codex construct | TextMate scope | Typical color |
|----------------|----------------|---------------|
| `let`, `if`, `when`, etc. | `keyword.control` | Purple/blue |
| `claim`, `proof`, `forall` | `keyword.other.proof` | Purple |
| `Integer`, `Text`, `Shape` | `entity.name.type` | Teal/green |
| `True`, `False` | `constant.language.boolean` | Blue |
| `"hello"` | `string.quoted.double` | Orange/brown |
| `42`, `3.14` | `constant.numeric` | Light green |
| `=`, `→`, `++` | `keyword.operator` | Gray/white |
| `@annotation` | `entity.name.tag` | Yellow |
| `Chapter:`, `Section:` | `keyword.control.header` | Bold purple |
| `my-function` | `variable.other` | Default text |
