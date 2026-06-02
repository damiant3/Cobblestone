# Bundle a Codex application source file with all its transitive
# cited dependencies into a single .codex file for compilation.
#
# Usage: bundle-app.ps1 -Src <app.codex> -Out <bundled.codex>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out
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
$quireNames = ($QuireDirs.Keys | Sort-Object -Descending { $_.Length }) -join '|'
$CitePat = "^\s*cites\s+($quireNames)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)"

$srcPath = (Resolve-Path $Src).Path
$rootLines = [System.IO.File]::ReadAllLines($srcPath)

$queue = [System.Collections.Generic.Queue[hashtable]]::new()
$seen  = @{}
foreach ($l in $rootLines) {
    if ($l -match $CitePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
}

$ordered = @()
while ($queue.Count -gt 0) {
    $cite = $queue.Dequeue()
    $key = "$($cite.Quire)::$($cite.Name)"
    if ($seen[$key]) { continue }
    $seen[$key] = $true
    $dir = $QuireDirs[$cite.Quire]
    if (-not $dir) {
        [Console]::Error.WriteLine("UNKNOWN QUIRE: '$($cite.Quire)' in cite '$key'")
        exit 3
    }
    $fwPath = Join-Path $Repo (Join-Path $dir "$($cite.Name).codex")
    if (-not (Test-Path -PathType Leaf $fwPath)) {
        [Console]::Error.WriteLine("MISSING: cited $($cite.Quire) chapter '$($cite.Name)' (expected $fwPath)")
        exit 3
    }
    foreach ($l in [System.IO.File]::ReadAllLines($fwPath)) {
        if ($l -match $CitePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
    $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Path = $fwPath }
}

[array]::Reverse($ordered)
$lines = [System.Collections.Generic.List[string]]::new()
$emitted = @{}
foreach ($entry in $ordered) {
    $key = "$($entry.Quire)::$($entry.Name)"
    if ($emitted[$key]) { continue }
    $emitted[$key] = $true
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($entry.Path)) {
        if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $lines.Add("Chapter: $($entry.Quire)--$($matches[1])")
            $renamed = $true
        } else { $lines.Add($l) }
    }
    $lines.Add(''); $lines.Add('')
}

foreach ($l in $rootLines) { $lines.Add($l) }
$lines.Add('')

$body = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText($Out, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[bundle-app] $($emitted.Count) dependencies + root -> $Out ($($body.Length) bytes)"
