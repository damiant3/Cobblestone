# Build plugs/groovy/build-output/groovy-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir   = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $PlugDir 'build-output'
$OutFile   = Join-Path $OutDir 'groovy-plug.cdx'
$BundleSrc = Join-Path $OutDir 'plug-source.codex'
$LogFile   = Join-Path $OutDir 'build.log'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$lines = [System.Collections.Generic.List[string]]::new()

function Add-Chapter {
    param([string]$Path, [string[]]$StripCites = @())
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        $skip = $false
        foreach ($sc in $StripCites) { if ($l -match "cites.*$sc") { $skip = $true } }
        if (-not $skip) { $lines.Add($l) }
    }
    $lines.Add(''); $lines.Add('')
}

Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\PlugTypes.codex')
Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\IRTextParser.codex')
Add-Chapter -Path (Join-Path $PlugDir 'GroovyEmitter.codex')
Add-Chapter -Path (Join-Path $PlugDir 'GroovyPlug.codex')

# -- Resolve foreword cites -------------------------------------------
$citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|Games|Spark|Data)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
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
}
$queue = [System.Collections.Generic.Queue[hashtable]]::new()
$seen = @{}
foreach ($l in $lines) {
    if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
}
$ordered = @()
while ($queue.Count -gt 0) {
    $cite = $queue.Dequeue()
    $key = "$($cite.Quire)::$($cite.Name)"
    if ($seen[$key]) { continue }
    $seen[$key] = $true
    $fwPath = Join-Path $Repo (Join-Path $QuireDirs[$cite.Quire] "$($cite.Name).codex")
    if (-not (Test-Path -PathType Leaf $fwPath)) {
        [Console]::Error.WriteLine("MISSING: cited $($cite.Quire) chapter '$($cite.Name)' (expected $fwPath)")
        exit 3
    }
    foreach ($l in [System.IO.File]::ReadAllLines($fwPath)) {
        if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
    $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Path = $fwPath }
}
[array]::Reverse($ordered)
$emitted = @{}
$preLines = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $ordered) {
    $key = "$($entry.Quire)::$($entry.Name)"
    if ($emitted[$key]) { continue }
    $emitted[$key] = $true
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($entry.Path)) {
        if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $preLines.Add("Chapter: $($entry.Quire)--$($matches[1])")
            $renamed = $true
        } else { $preLines.Add($l) }
    }
    $preLines.Add(''); $preLines.Add('')
}

$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[groovy-plug] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"


# -- Compile via compile.ps1 ------------------------------------------
$compileScript = Join-Path $Repo 'build' 'compile.ps1'
& pwsh -NoProfile -File $compileScript -Src $BundleSrc -Out $OutFile -Log $LogFile 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
    Get-Content $LogFile -ErrorAction SilentlyContinue | Select-Object -First 10 | ForEach-Object { [Console]::Error.WriteLine("  $_") }
    exit 5
}
$sz = (Get-Item $OutFile).Length
Write-Host "[groovy-plug] OK: $OutFile ($sz bytes)"
