# Common plug build library. Source this from plug build scripts.
# Provides: Resolve-PlugForewords, Build-PlugCdx

$script:PlugBuildRepo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
. (Join-Path $script:PlugBuildRepo 'build' 'vm-config.ps1')

$script:QuireDirs = @{
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
$script:CitePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|Games|Spark|Data)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'

function Resolve-PlugForewords {
    param([System.Collections.Generic.List[string]]$Lines)
    $queue = [System.Collections.Generic.Queue[hashtable]]::new()
    $seen = @{}
    foreach ($l in $Lines) {
        if ($l -match $script:CitePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
    $ordered = @()
    while ($queue.Count -gt 0) {
        $cite = $queue.Dequeue()
        $key = "$($cite.Quire)::$($cite.Name)"
        if ($seen[$key]) { continue }
        $seen[$key] = $true
        $fwPath = Join-Path $script:PlugBuildRepo (Join-Path $script:QuireDirs[$cite.Quire] "$($cite.Name).codex")
        if (-not (Test-Path -PathType Leaf $fwPath)) {
            [Console]::Error.WriteLine("MISSING: cited $($cite.Quire) chapter '$($cite.Name)' (expected $fwPath)")
            exit 3
        }
        foreach ($l in [System.IO.File]::ReadAllLines($fwPath)) {
            if ($l -match $script:CitePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
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
    return $preLines
}

function Build-PlugCdx {
    param(
        [string]$BundleSrc,
        [string]$OutFile,
        [string]$LogFile,
        [string]$PlugName
    )
    $compileScript = Join-Path $script:PlugBuildRepo 'build' 'compile.ps1'
    & pwsh -NoProfile -File $compileScript -Src $BundleSrc -Out $OutFile -Log $LogFile 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
        Get-Content $LogFile -ErrorAction SilentlyContinue | Select-Object -First 10 | ForEach-Object { [Console]::Error.WriteLine("  $_") }
        exit 5
    }
    $sz = (Get-Item $OutFile).Length
    Write-Host "[$PlugName] OK: $OutFile ($sz bytes)"
}
