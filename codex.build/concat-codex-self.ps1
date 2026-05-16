# Concatenate codex/ source with quire-prefixed chapters and any
# cited foreword chapters preloaded. Writes to stdout (or -OutFile).
[CmdletBinding()]
param(
    [string]$CodexDir,
    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $CodexDir) { $CodexDir = Join-Path $Repo 'codex' }
$CodexDir   = (Resolve-Path $CodexDir).Path
$ForewordDir = Join-Path $Repo 'codex.foreword'

$lines = [System.Collections.Generic.List[string]]::new()

function Add-WithQuire {
    param([string]$Path, [string]$Quire)
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        if ((-not $renamed) -and $Quire -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $lines.Add("Chapter: ${Quire}--$($matches[1])")
            $renamed = $true
        } else {
            $lines.Add($l)
        }
    }
    $lines.Add('')
    $lines.Add('')
}

# 1. Cited forewords — scan compiler source for "cites Foreword chapter X",
#    then transitively resolve each foreword's own cites.
$citePat = '^\s*cites\s+Foreword\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$queue = [System.Collections.Generic.Queue[string]]::new()
Get-ChildItem $CodexDir -Recurse -Depth 2 -Filter '*.codex' -File | ForEach-Object {
    foreach ($l in [System.IO.File]::ReadAllLines($_.FullName)) {
        if ($l -match $citePat) { $queue.Enqueue($matches[1]) }
    }
}
$seen = [System.Collections.Generic.HashSet[string]]::new()
$ordered = @()
while ($queue.Count -gt 0) {
    $fw = $queue.Dequeue()
    if (-not $seen.Add($fw)) { continue }
    $fwPath = Join-Path $ForewordDir "$fw.codex"
    if (-not (Test-Path -PathType Leaf $fwPath)) { continue }
    $fwLines = [System.IO.File]::ReadAllLines($fwPath)
    foreach ($l in $fwLines) {
        if ($l -match $citePat) { $queue.Enqueue($matches[1]) }
    }
    $ordered += @{ Name = $fw; Path = $fwPath }
}
[array]::Reverse($ordered)
$emitted = [System.Collections.Generic.HashSet[string]]::new()
foreach ($entry in $ordered) {
    if (-not $emitted.Add($entry.Name)) { continue }
    Add-WithQuire -Path $entry.Path -Quire 'Foreword'
}

# Ordinal name comparer for FileInfo / DirectoryInfo arrays.
$nameCmp = [System.Collections.Generic.Comparer[object]]::Create({
    param($a, $b)
    [System.StringComparer]::Ordinal.Compare($a.Name, $b.Name)
})

# 2. Root .codex (depth 0), sorted ordinal.
$rootFiles = @(Get-ChildItem $CodexDir -Filter '*.codex' -File)
[Array]::Sort($rootFiles, $nameCmp)
foreach ($f in $rootFiles) {
    Add-WithQuire -Path $f.FullName -Quire ''
}

# 3. Subdirs (depth 1), each .codex prefixed with subdir name.
$subDirs = @(Get-ChildItem $CodexDir -Directory)
[Array]::Sort($subDirs, $nameCmp)
foreach ($d in $subDirs) {
    $quire = $d.Name
    $subFiles = @(Get-ChildItem $d.FullName -Filter '*.codex' -File)
    [Array]::Sort($subFiles, $nameCmp)
    foreach ($f in $subFiles) {
        Add-WithQuire -Path $f.FullName -Quire $quire
    }
}

$body = ($lines -join "`n") + "`n"
if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $body, [System.Text.UTF8Encoding]::new($false))
} else {
    [Console]::Out.Write($body)
}
