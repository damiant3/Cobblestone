# lint-unused-cites.ps1 -- Detect unused cites in Codex source files
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
    [string]$Src,
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'quire-map.ps1')

$CitePat = '^\s*cites\s+(\w+)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$DefPat = '^\s{2}(\S+)\s*[:=(|]'
$TypeDefPat = '^\s{2}([A-Z][A-Za-z0-9]*)\s*[=(]'
$ProsePat = '^ [^ ]'
$CtorPat = '^\s+\|\s*([A-Z][A-Za-z0-9_-]*)'


function Get-ChapterDefs([string]$Path) {
    $names = ([System.Collections.Generic.HashSet[string]]::new())
    foreach ($line in ([System.IO.File]::ReadAllLines($Path))) {
        if (($line -match $DefPat)) {
            [void]$names.Add($matches[1])
        }
        if (($line -match $TypeDefPat)) {
            [void]$names.Add($matches[1])
        }
        if (($line -match $CtorPat)) {
            [void]$names.Add($matches[1])
        }
    }
    return $names
}


function Lint-File([string]$FilePath) {
    $lines = ([System.IO.File]::ReadAllLines($FilePath))
    $code = ([System.Text.StringBuilder]::new())
    foreach ($line in $lines) {
        if (($line -match $ProsePat)) {
            continue
        }
        if (($line -match $CitePat)) {
            continue
        }
        [void]$code.AppendLine($line)
    }
    $body = $code.ToString()

    $relPath = $FilePath
    if ($FilePath.StartsWith($Repo)) {
        $relPath = $FilePath.Substring(($Repo.Length + 1))
    }
    $warnings = 0
    $lineNum = 0

    foreach ($line in $lines) {
        $lineNum++
        if (($line -match $CitePat)) {
            $quire = $matches[1]
            $chapter = $matches[2]
            $dir = $QuireDirs[$quire]
            if ((-not $dir)) {
                continue
            }
            $citedPath = (Join-Path $Repo (Join-Path $dir ([string]$chapter + '.codex')))
            if ((-not (Test-Path -PathType Leaf $citedPath))) {
                continue
            }
            $defs = (Get-ChapterDefs $citedPath)
            $used = $false
            foreach ($name in $defs) {
                if (($name.Length -lt 2)) {
                    continue
                }
                if ($body.Contains($name)) {
                    $used = $true
                    break
                }
            }
            $defCount = @($defs).Count
            if (((-not $used) -and ($defCount -gt 0))) {
                Write-Host ([string]([string]([string]([string]([string]([string]([string]([string]([string]([string]'  ' + $relPath) + ':') + $lineNum) + ': unused cite: ') + $quire) + ' chapter ') + $chapter) + ' (') + $defCount) + ' defs, none referenced)')
                $warnings++
            }
        }
    }
    return $warnings
}


$totalWarnings = 0
$filesChecked = 0

if ($Src) {
    $totalWarnings = (Lint-File (Resolve-Path $Src).Path)
    $filesChecked = 1
}
if (((-not $Src) -and $All)) {
    $files = @()
    foreach ($dir in @('codex\compiler', 'codex\foreword', 'codex\os', 'codex\plugs', 'apps')) {
        $fullDir = (Join-Path $Repo $dir)
        if ((Test-Path -PathType Container $fullDir)) {
            $files += Get-ChildItem $fullDir -Recurse -Filter '*.codex' -File
        }
    }
    foreach ($f in $files) {
        $w = (Lint-File $f.FullName)
        $totalWarnings += $w
        $filesChecked++
    }
}

Write-Host ([string]([string]([string]([string]'Checked ' + $filesChecked) + ' files, ') + $totalWarnings) + ' unused cites found.')
