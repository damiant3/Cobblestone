# measure-survey.ps1 - profile per-phase deck USAGE for a workload, to inform
# survey heuristics. Resolves cites like compile.ps1, runs TEXT mode, extracts
# WD:PM deck-usage per phase, and reports density metrics over the full concat.
#
# Usage: measure-survey.ps1 -Src <source.codex> [-Survey "check-mul:60,..."]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [int]$MemMB = 3072,
    [string]$Survey = '',
    [string]$Kernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $Repo

$QuireDirs = @{ 'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'; 'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'; 'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'; 'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'; 'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'; 'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'; 'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'; 'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'; 'Magic' = 'apps\games\magic'; 'CodexMagic' = 'apps\games\codexmagic'; 'Games' = 'apps\games\classic'; 'Spark' = 'apps\spark'; 'Data' = 'apps\data'; 'Explorer' = 'apps\explorer' }
$citePat = '^\s*cites\s+(Codex|Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|CodexMagic|Games|Spark|Data|Explorer)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'

# Index codex/compiler chapters (Codex quire) by chapter Title -> file path,
# since they live in subdirs (Core/Emit/IR/Semantics/Syntax/Types/Ast).
$codexIndex = @{}
foreach ($f in Get-ChildItem (Join-Path $Repo 'codex\compiler') -Recurse -Filter *.codex) {
    foreach ($l in [System.IO.File]::ReadAllLines($f.FullName)) {
        if ($l -match '^Chapter:\s*(.+?)\s*$') { $codexIndex[$matches[1].Trim()] = $f.FullName; break }
    }
}
$queue = [System.Collections.Generic.Queue[hashtable]]::new()
$seen = @{}
foreach ($line in [System.IO.File]::ReadAllLines($Src)) {
    if ($line -match '^Chapter:\s*(\w+)--(.+?)\s*$') { $seen["$($matches[1])::$($matches[2])"] = $true }
    if ($line -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
}
$ordered = @()
while ($queue.Count -gt 0) {
    $cite = $queue.Dequeue(); $key = "$($cite.Quire)::$($cite.Name)"
    if ($seen[$key]) { continue }
    $seen[$key] = $true
    $fwPath = if ($cite.Quire -eq 'Codex') { $codexIndex[$cite.Name] } else { Join-Path $QuireDirs[$cite.Quire] "$($cite.Name).codex" }
    if (-not $fwPath -or -not (Test-Path -PathType Leaf $fwPath)) { Write-Host "MISSING cite: $($cite.Quire) $($cite.Name)"; continue }
    $lines = [System.IO.File]::ReadAllLines($fwPath)
    foreach ($l in $lines) { if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) } }
    $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Lines = $lines }
}
[array]::Reverse($ordered)
$emitted = @{}
$sb = [System.Text.StringBuilder]::new(2097152)
$mode = 'TEXT'
if ($Survey) { $mode = "$mode survey=$Survey" }
[void]$sb.Append("$mode`n")
$bodySb = [System.Text.StringBuilder]::new(2097152)
foreach ($entry in $ordered) {
    $key = "$($entry.Quire)::$($entry.Name)"
    if ($emitted[$key]) { continue }
    $emitted[$key] = $true
    $renamed = $false
    foreach ($l in $entry.Lines) {
        if (-not $renamed -and $l -match '^Chapter:\s*(.+?)\s*$') { [void]$bodySb.Append("Chapter: $($entry.Quire)--$($matches[1])`n"); $renamed = $true }
        else { [void]$bodySb.Append($l + "`n") }
    }
    [void]$bodySb.Append("`n`n")
}
foreach ($line in [System.IO.File]::ReadAllLines($Src)) { [void]$bodySb.Append($line + "`n") }
$body = $bodySb.ToString()
[void]$sb.Append($body); [void]$sb.Append([char]4)

# Density metrics over the full concatenated body
$S = $body.Length
$records  = ([regex]::Matches($body, '=\s*(mutable\s+)?record\s*\{')).Count
$variants = ([regex]::Matches($body, '(?m)^\s+\|\s')).Count            # variant ctor lines
$fields   = ([regex]::Matches($body, '(?m)^\s+[a-z][\w-]*\s*:\s')).Count # record-field-ish lines
$arrows   = ([regex]::Matches($body, '->')).Count
$whens    = ([regex]::Matches($body, '(?m)\bwhen\b')).Count

$vm = Join-Path $Repo 'tools\codex-vm.exe'
$in = [System.IO.Path]::GetTempFileName(); $out = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($in, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$kern = if ($Kernel) { $Kernel } else { Join-Path $Repo 'seed\Codex.cdx' }
& $vm -kernel $kern -input $in -output $out -mem $MemMB -headless 2>&1 | Out-Null
$o = [System.IO.File]::ReadAllText($out)
Remove-Item $in,$out -Force -EA SilentlyContinue
Pop-Location

$name = [System.IO.Path]::GetFileNameWithoutExtension($Src)
Write-Host ("WORKLOAD={0}  S={1}  records={2}  variant-ctors={3}  fields={4}  arrows={5}  whens={6}" -f $name,$S,$records,$variants,$fields,$arrows,$whens)
$halted = $o -match 'CODEGEN-HALTED|CDX9002|Deck overflow'
if ($halted) { Write-Host "  *** HALTED/OVERFLOW (survey too low or compile error) ***" }
$o -split "`n" | Where-Object { $_ -match 'WD:PM-' } | ForEach-Object {
    if ($_ -match 'WD:PM-(\w+):deck-origin=(\d+),deck-end=(\d+),deck-usage=(\d+),bivy-hwm=(\d+),bivy-usage=(\d+)') {
        $phase=$matches[1]; $du=[long]$matches[4]; $bu=[long]$matches[6]
        Write-Host ("  {0,-8} deck-usage={1,12:N0}  bivy-usage={2,12:N0}  deck/S={3,7:N1}  bivy/S={4,7:N1}" -f $phase,$du,$bu,($du/$S),($bu/$S))
    }
}
