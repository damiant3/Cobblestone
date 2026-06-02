# Detect unused cites in Codex source files.
#
# For each `cites Quire chapter Name` in the input file, checks whether
# any definition from the cited chapter is referenced in the citing file.
# Prints warnings for cites where no definition name appears in the
# citing chapter's source text.
#
# Usage: lint-unused-cites.ps1 [-Src <file.codex>] [-All]
#   -Src   Lint a single file
#   -All   Lint all .codex files under codex/ and apps/
[CmdletBinding()]
param(
    [string]$Src,
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
    'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'
    'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'
    'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'
    'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'
    'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'
    'Magic' = 'apps\games\magic'; 'Games' = 'apps\games\classic'
    'Spark' = 'apps\spark'; 'Data' = 'apps\data'
    'Explorer' = 'apps\explorer'
}

$CitePat = '^\s*cites\s+(\w+)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$DefPat = '^\s{2}(\S+)\s*[:=(|]'
$TypeDefPat = '^\s{2}([A-Z][A-Za-z0-9]*)\s*[=(]'

function Get-ChapterDefs([string]$Path) {
    $names = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line -match $DefPat) { [void]$names.Add($matches[1]) }
        if ($line -match $TypeDefPat) { [void]$names.Add($matches[1]) }
    }
    return $names
}

function Lint-File([string]$FilePath) {
    $lines = [System.IO.File]::ReadAllLines($FilePath)
    $body = [System.IO.File]::ReadAllText($FilePath)
    $relPath = $FilePath
    if ($FilePath.StartsWith($Repo)) { $relPath = $FilePath.Substring($Repo.Length + 1) }
    $warnings = 0

    $lineNum = 0
    foreach ($line in $lines) {
        $lineNum++
        if ($line -match $CitePat) {
            $quire = $matches[1]
            $chapter = $matches[2]
            $dir = $QuireDirs[$quire]
            if (-not $dir) { continue }
            $citedPath = Join-Path $Repo (Join-Path $dir "$chapter.codex")
            if (-not (Test-Path -PathType Leaf $citedPath)) { continue }

            $defs = Get-ChapterDefs $citedPath
            $used = $false
            foreach ($name in $defs) {
                if ($name.Length -lt 2) { continue }
                if ($body.Contains($name)) {
                    $used = $true
                    break
                }
            }
            $defCount = @($defs).Count
            if (-not $used -and $defCount -gt 0) {
                Write-Host "  $($relPath):$($lineNum): unused cite: $quire chapter $chapter ($defCount defs, none referenced)"
                $warnings++
            }
        }
    }
    return $warnings
}

$totalWarnings = 0
$filesChecked = 0

if ($Src) {
    $totalWarnings = Lint-File (Resolve-Path $Src).Path
    $filesChecked = 1
} elseif ($All) {
    $files = @()
    foreach ($dir in @('codex\compiler', 'codex\foreword', 'codex\os', 'codex\plugs', 'apps')) {
        $fullDir = Join-Path $Repo $dir
        if (Test-Path $fullDir) {
            $files += Get-ChildItem $fullDir -Recurse -Filter '*.codex' -File
        }
    }
    foreach ($f in $files) {
        $w = Lint-File $f.FullName
        $totalWarnings += $w
        $filesChecked++
    }
}

Write-Host "Checked $filesChecked files, $totalWarnings unused cites found."
