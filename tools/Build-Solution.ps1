<#
<#
.SYNOPSIS
    Dynamically generates Codex.sln from the repository directory tree.

.DESCRIPTION
    Scans the repo for .csproj files and document/sample files, then produces
    a byte-perfect VS2022 solution file (UTF-8 BOM, tab indentation, CRLF).

    No hardcoded file lists — add, move, or delete files and projects freely;
    just re-run this script to regenerate the solution.

.EXAMPLE
    pwsh -File tools/Build-Solution.ps1
    # Writes Codex.sln in the repo root
#>

param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Codex.sln')
)

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$RepoRoot   = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# ── Constants ────────────────────────────────────────────────────────────────
$T  = "`t"
$TT = "`t`t"
$NL = "`r`n"
$SolutionFolderType = '2150E333-8FDC-42A3-9474-1A3956D46DE8'
$CSharpProjectType  = 'FAE04EC0-301F-11D3-BF4B-00C04F79EFBC'

# ── Deterministic GUID helper ────────────────────────────────────────────────
# Produces a stable GUID from a string key so the .sln doesn't churn on every run.
function New-DeterministicGuid([string]$key) {
    $hash = [System.Security.Cryptography.SHA256]::HashData(
                [System.Text.Encoding]::UTF8.GetBytes($key))
    $hex = ($hash[0..15] | ForEach-Object { $_.ToString('X2') }) -join ''
    # Format as GUID: 8-4-4-4-12
    "$($hex.Substring(0,8))-$($hex.Substring(8,4))-$($hex.Substring(12,4))-$($hex.Substring(16,4))-$($hex.Substring(20,12))"
}

function G([string]$guid) { "{$($guid.ToUpper())}" }

# ── Well-known folder GUIDs (stable across runs) ────────────────────────────
$FolderGuids = @{}
function Get-FolderGuid([string]$key) {
    if (-not $FolderGuids.ContainsKey($key)) {
        $FolderGuids[$key] = New-DeterministicGuid "folder:$key"
    }
    $FolderGuids[$key]
}

# ── Discover .csproj files ───────────────────────────────────────────────────
$allCsproj = Get-ChildItem -Path $RepoRoot -Filter '*.csproj' -Recurse -File |
    ForEach-Object {
        $rel = $_.FullName.Substring($RepoRoot.Length + 1)
        # Skip anything under bin, obj, hidden dirs, or VS extension templates
        if ($rel -match '(^|[/\\])(bin|obj|\.vs|\.git|node_modules|ProjectTemplate)([/\\]|$)') { return }
        # Convention: Project.csproj must live in a folder named after the project.
        # e.g. src/Codex.Core/Codex.Core.csproj  —  folder = file stem
        $stem   = [System.IO.Path]::GetFileNameWithoutExtension($rel)
        $parent = Split-Path $rel -Parent | Split-Path -Leaf
        if ($stem -ne $parent) { return }
        # Normalize to backslash for .sln output and stable GUID keys
        $rel.Replace('/', '\')
    } | Where-Object { $_ } | Sort-Object

# Categorise each project
$csharpProjects = @()      # @( @(RelPath, GUID, ParentGUID), ... )
$emittersFolderGuid = Get-FolderGuid 'src\Emitters'
$srcGuid   = Get-FolderGuid 'src'
$testsGuid = Get-FolderGuid 'tests'
$toolsGuid = Get-FolderGuid 'tools'

foreach ($rel in $allCsproj) {
    $projGuid = New-DeterministicGuid "proj:$rel"

    $parent = ''
    if     ($rel -match '^src\\Codex\.Emit[\.\\\w]') { $parent = $emittersFolderGuid }
    elseif ($rel -match '^src\\')                     { $parent = $srcGuid }
    elseif ($rel -match '^tests\\')                   { $parent = $testsGuid }
    elseif ($rel -match '^tools\\')                   { $parent = $toolsGuid }

    $csharpProjects += ,@($rel, $projGuid, $parent)
}

# ── Discover solution-item files ─────────────────────────────────────────────
# Extensions that are build artifacts / binaries and should be excluded.
$excludeExtensions = @('.exe','.dll','.pdb','.runtimeconfig.json','.deps.json',
                       '.nupkg','.snupkg','.suo','.user','.snap')

function Get-SolutionItemFiles([string]$dir, [string]$filter) {
    if (-not (Test-Path $dir)) { return @() }
    $items = Get-ChildItem -Path $dir -File |
        Where-Object {
            $ext = $_.Extension.ToLowerInvariant()
            $ext -notin $excludeExtensions
        }
    if ($filter) {
        $items = $items | Where-Object { $_.Extension -eq $filter }
    }
    ($items | Sort-Object Name | ForEach-Object {
        $_.FullName.Substring($RepoRoot.Length + 1).Replace('/', '\')
    })
}

# ── Build solution folder tree ───────────────────────────────────────────────
# Each entry: @(Name, GUID, ParentGUID)
# Plus a parallel hashtable: GUID => file-list
$solutionFolders = [System.Collections.ArrayList]::new()
$solutionItems   = [ordered]@{}

# -- Top-level virtual groups --
$solutionItemsGuid = Get-FolderGuid 'Solution Items'
[void]$solutionFolders.Add(@('src',            $srcGuid,            ''))
[void]$solutionFolders.Add(@('tests',          $testsGuid,          ''))
[void]$solutionFolders.Add(@('tools',          $toolsGuid,          ''))
[void]$solutionFolders.Add(@('samples',        (Get-FolderGuid 'samples'), ''))
[void]$solutionFolders.Add(@('docs',           (Get-FolderGuid 'docs'),    ''))
[void]$solutionFolders.Add(@('Solution Items', $solutionItemsGuid,  ''))

# -- Emitters sub-folder under src --
[void]$solutionFolders.Add(@('Emitters', $emittersFolderGuid, $srcGuid))

# -- Root "Solution Items" files --
# Include well-known root config/doc files, plus .github/copilot-instructions.md
$rootFiles = @()
$rootPatterns = @('*.md','*.txt','.editorconfig','.gitattributes','.gitignore','*.props')
foreach ($pat in $rootPatterns) {
    $rootFiles += Get-ChildItem -Path $RepoRoot -Filter $pat -File |
        ForEach-Object { $_.FullName.Substring($RepoRoot.Length + 1).Replace('/', '\') }
}
# Also include .github files (but not workflow yaml, etc.)
if (Test-Path (Join-Path $RepoRoot '.github')) {
    $rootFiles += Get-ChildItem -Path (Join-Path $RepoRoot '.github') -File -Recurse |
        Where-Object { $_.Extension -in @('.md','.txt','.json') } |
        ForEach-Object { $_.FullName.Substring($RepoRoot.Length + 1).Replace('/', '\') }
}
$solutionItems[$solutionItemsGuid] = @($rootFiles | Sort-Object -Unique)

# -- samples (only .codex files) --
$samplesGuid = Get-FolderGuid 'samples'
$solutionItems[$samplesGuid] = @(Get-SolutionItemFiles (Join-Path $RepoRoot 'samples') '.codex')

# -- docs tree: auto-discover every subfolder and its files --
$docsGuid = Get-FolderGuid 'docs'
$docsRoot = Join-Path $RepoRoot 'docs'

if (Test-Path $docsRoot) {
    # Root-level doc files
    $solutionItems[$docsGuid] = @(Get-SolutionItemFiles $docsRoot $null)

    # Recursively discover sub-directories and create nested solution folders
    $allDocDirs = Get-ChildItem -Path $docsRoot -Directory -Recurse | Sort-Object FullName
    foreach ($d in $allDocDirs) {
        $relDir    = $d.FullName.Substring($RepoRoot.Length + 1).Replace('/', '\')   # e.g. docs\Designs\Backends
        $folderKey = $relDir
        $myGuid    = Get-FolderGuid $folderKey

        # Parent is either docs root or the parent subfolder
        $parentRelDir = Split-Path $relDir -Parent               # e.g. docs\Designs
        if ($parentRelDir -eq 'docs') {
            $parentGuid = $docsGuid
        } else {
            $parentGuid = Get-FolderGuid $parentRelDir
        }

        [void]$solutionFolders.Add(@($d.Name, $myGuid, $parentGuid))
        $files = Get-SolutionItemFiles $d.FullName $null
        if ($files.Count -gt 0) {
            $solutionItems[$myGuid] = $files
        }
    }
}

# -- .vscode folder under tools --
$vscodeDir = Join-Path $RepoRoot '.vscode'
if (Test-Path $vscodeDir) {
    $vscodeGuid = Get-FolderGuid '.vscode'
    [void]$solutionFolders.Add(@('vscode', $vscodeGuid, $toolsGuid))
    $vscodeFiles = Get-SolutionItemFiles $vscodeDir $null
    if ($vscodeFiles.Count -gt 0) {
        $solutionItems[$vscodeGuid] = $vscodeFiles
    }
}

# ── Build Configurations ─────────────────────────────────────────────────────
$configs = @(
    'Debug|Any CPU'
    'Debug|x64'
    'Debug|x86'
    'Release|Any CPU'
    'Release|x64'
    'Release|x86'
)

# ── Generate ─────────────────────────────────────────────────────────────────
$sb = [System.Text.StringBuilder]::new(64000)

# Header (VS expects blank line before the format line)
[void]$sb.Append($NL)
[void]$sb.Append("Microsoft Visual Studio Solution File, Format Version 12.00$NL")
[void]$sb.Append("# Visual Studio Version 17$NL")
[void]$sb.Append("VisualStudioVersion = 17.14.37111.16$NL")
[void]$sb.Append("MinimumVisualStudioVersion = 10.0.40219.1$NL")

# Solution Folders
foreach ($f in $solutionFolders) {
    [string]$name = $f[0]
    [string]$guid = $f[1]
    $items = $solutionItems[$guid]

    [void]$sb.Append("Project(`"$(G $SolutionFolderType)`") = `"$name`", `"$name`", `"$(G $guid)`"$NL")
    if ($items -and $items.Count -gt 0) {
        [void]$sb.Append("${T}ProjectSection(SolutionItems) = preProject$NL")
        foreach ($item in $items) {
            [void]$sb.Append("$TT$item = $item$NL")
        }
        [void]$sb.Append("${T}EndProjectSection$NL")
    }
    [void]$sb.Append("EndProject$NL")
}

# C# Projects
foreach ($p in $csharpProjects) {
    [string]$path = $p[0]
    [string]$guid = $p[1]
    [string]$name = [System.IO.Path]::GetFileNameWithoutExtension((Split-Path $path -Leaf))

    [void]$sb.Append("Project(`"$(G $CSharpProjectType)`") = `"$name`", `"$path`", `"$(G $guid)`"$NL")
    [void]$sb.Append("EndProject$NL")
}

# Global
[void]$sb.Append("Global$NL")

# Solution Configuration Platforms
[void]$sb.Append("${T}GlobalSection(SolutionConfigurationPlatforms) = preSolution$NL")
foreach ($c in $configs) {
    [void]$sb.Append("$TT$c = $c$NL")
}
[void]$sb.Append("${T}EndGlobalSection$NL")

# Project Configuration Platforms
[void]$sb.Append("${T}GlobalSection(ProjectConfigurationPlatforms) = postSolution$NL")
foreach ($p in $csharpProjects) {
    [string]$guid = G $p[1]
    foreach ($c in $configs) {
        [void]$sb.Append("$TT$guid.$c.ActiveCfg = Debug|Any CPU$NL")
        [void]$sb.Append("$TT$guid.$c.Build.0 = Debug|Any CPU$NL")
    }
}
[void]$sb.Append("${T}EndGlobalSection$NL")

# Nested Projects
[void]$sb.Append("${T}GlobalSection(NestedProjects) = preSolution$NL")

# Nest solution folders under their parents
foreach ($f in $solutionFolders) {
    [string]$guid   = $f[1]
    [string]$parent = $f[2]
    if ($parent -ne '') {
        [void]$sb.Append("$TT$(G $guid) = $(G $parent)$NL")
    }
}

# Nest C# projects under their parent folders
foreach ($p in $csharpProjects) {
    [string]$guid   = $p[1]
    [string]$parent = $p[2]
    if ($parent -ne '') {
        [void]$sb.Append("$TT$(G $guid) = $(G $parent)$NL")
    }
}

[void]$sb.Append("${T}EndGlobalSection$NL")

# Extensibility Globals
[void]$sb.Append("${T}GlobalSection(ExtensibilityGlobals) = postSolution$NL")
[void]$sb.Append("${TT}SolutionGuid = {AF02C109-BF5B-4C3E-87D8-31B2097DC5C8}$NL")
[void]$sb.Append("${T}EndGlobalSection$NL")

[void]$sb.Append("EndGlobal$NL")

# ── Write with UTF-8 BOM ────────────────────────────────────────────────────
$bom   = [byte[]]@(0xEF, 0xBB, 0xBF)
$bytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
$all   = [byte[]]::new($bom.Length + $bytes.Length)
[System.Buffer]::BlockCopy($bom, 0, $all, 0, $bom.Length)
[System.Buffer]::BlockCopy($bytes, 0, $all, $bom.Length, $bytes.Length)
[System.IO.File]::WriteAllBytes($OutputPath, $all)

# ── Summary ──────────────────────────────────────────────────────────────────
$check = [System.IO.File]::ReadAllBytes($OutputPath)
$hasBom = ($check[0] -eq 0xEF -and $check[1] -eq 0xBB -and $check[2] -eq 0xBF)
$hasTab = $check -contains 9
$lineCount = ($sb.ToString().Split("`n")).Count

Write-Host "Written: $OutputPath"
Write-Host "  Size:       $($all.Length) bytes"
Write-Host "  Lines:      $lineCount"
Write-Host "  UTF-8 BOM:  $hasBom"
Write-Host "  Has tabs:   $hasTab"
Write-Host "  Projects:   $($csharpProjects.Count)"
Write-Host "  Folders:    $($solutionFolders.Count)"
if (-not $hasBom) { Write-Error "BOM missing!" }
if (-not $hasTab) { Write-Error "Tabs missing - VS will reject this!" }
