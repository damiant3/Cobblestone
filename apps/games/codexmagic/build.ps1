# Build CodexMagic server: topo-sort app chapters, compile to CDX.
# compile.ps1 handles foreword/data dependency resolution automatically.
# Usage: apps/games/codexmagic/build.ps1 [-Entry MagicServer]
[CmdletBinding()]
param(
    [string]$Entry = 'MagicServer',
    [switch]$Repl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppDir    = (Resolve-Path $PSScriptRoot).Path
$Repo      = (Resolve-Path (Join-Path $AppDir '..\..\..')).Path
$OutDir    = Join-Path $Repo 'build-output'
$BundleSrc = Join-Path $OutDir 'codexmagic-bundle.codex'
$CdxOut    = Join-Path $OutDir 'codexmagic.cdx'
$LogOut    = Join-Path $OutDir 'codexmagic.log'

New-Item -ItemType Directory -Force $OutDir | Out-Null

$lines = [System.Collections.Generic.List[string]]::new()

function Add-File {
    param([string]$Path)
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $lines.Add("Chapter: CodexMagic--$($matches[1])")
            $renamed = $true
        } else { $lines.Add($l) }
    }
    $lines.Add(''); $lines.Add('')
}

# Topologically sort CodexMagic chapters. Entry file goes last.
# Find all files that define 'opening' — these are alternate entry points, exclude them
$entryFiles = [System.Collections.Generic.HashSet[string]]::new()
[void]$entryFiles.Add($Entry)
foreach ($f in Get-ChildItem $AppDir -Filter '*.codex' -File) {
    foreach ($l in [System.IO.File]::ReadAllLines($f.FullName)) {
        if ($l -match '^\s+opening\s*:') { [void]$entryFiles.Add($f.BaseName); break }
    }
}
$appFiles = @(Get-ChildItem $AppDir -Filter '*.codex' -File |
    Where-Object { -not $entryFiles.Contains($_.BaseName) })
$entryFile = Get-Item (Join-Path $AppDir "$Entry.codex") -ErrorAction Stop

$chapterPat = '^Chapter:\s*(.+?)\s*$'
$internalCite = '^\s*cites\s+CodexMagic\s+chapter\s+(.+?)(?:\s*\(|$)'
$chapterToStem = @{}
$stemToFile = @{}
$deps = @{}

foreach ($f in ($appFiles + @($entryFile))) {
    $stem = $f.BaseName
    $stemToFile[$stem] = $f
    $first = Get-Content -TotalCount 1 $f.FullName
    if ($first -match $chapterPat) {
        $cname = $matches[1]
        if (-not $chapterToStem.ContainsKey($cname)) { $chapterToStem[$cname] = $stem }
    }
    $deps[$stem] = @()
    foreach ($l in [System.IO.File]::ReadAllLines($f.FullName)) {
        if ($l -match $internalCite) { $deps[$stem] += $matches[1] }
    }
}

$result = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
$visited = [System.Collections.Generic.HashSet[string]]::new()
$visiting = [System.Collections.Generic.HashSet[string]]::new()

function Visit([string]$Stem) {
    if ($visited.Contains($Stem)) { return }
    if ($visiting.Contains($Stem)) { return }
    [void]$visiting.Add($Stem)
    foreach ($citedChapter in $deps[$Stem]) {
        $depStem = $chapterToStem[$citedChapter]
        if ($depStem -and $stemToFile.ContainsKey($depStem) -and $depStem -ne $Entry) {
            Visit $depStem
        }
    }
    [void]$visiting.Remove($Stem)
    [void]$visited.Add($Stem)
    if ($Stem -ne $Entry) { $result.Add($stemToFile[$Stem]) }
}

Visit $Entry

foreach ($f in $result) { Add-File -Path $f.FullName }
Add-File -Path $entryFile.FullName
Write-Host "[codexmagic] $($result.Count + 1) app chapters (entry: $Entry)" -ForegroundColor Cyan

$body = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[codexmagic] bundle: $($lines.Count) lines, $([math]::Round($body.Length / 1024)) KB -> $BundleSrc" -ForegroundColor Green

# compile.ps1 resolves cites Foreword/Data/etc transitively
$compileArgs = @('-NoProfile', '-File', (Join-Path $Repo 'build\compile.ps1'),
    '-Src', $BundleSrc, '-Out', $CdxOut, '-Log', $LogOut)
if ($Repl) { $compileArgs += '-Repl' }

Write-Host "[codexmagic] compiling..." -ForegroundColor Yellow
& pwsh @compileArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[codexmagic] COMPILE FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
    Write-Host "  Log: $LogOut" -ForegroundColor Gray
    exit 1
}

$size = (Get-Item $CdxOut).Length
Write-Host "[codexmagic] OK: $CdxOut ($($size.ToString('N0')) bytes)" -ForegroundColor Green
