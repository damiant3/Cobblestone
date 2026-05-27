# Batch compiler: boots one VM in REPL mode, feeds all test sources
# sequentially. One VM boot for the entire batch.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$ListFile,
    [Parameter(Mandatory=$true)] [string]$OutRoot,
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
. (Join-Path $PSScriptRoot 'vm-config.ps1')

$sources = @(Get-Content -Path $ListFile | Where-Object { $_.Trim() -ne '' })
if ($sources.Count -eq 0) { exit 0 }

$citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|CodexMagic|Games|Spark|Data|Explorer)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
    'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'
    'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'
    'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'
    'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'
    'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'
    'Magic' = 'apps\games\magic'; 'CodexMagic' = 'apps\games\codexmagic'; 'Games' = 'apps\games\classic'
    'Spark' = 'apps\spark'; 'Data' = 'apps\data'; 'Explorer' = 'apps\explorer'
}

function Resolve-Source {
    param([string]$SrcPath)
    $lines = [System.IO.File]::ReadAllLines($SrcPath)
    $queue = [System.Collections.Generic.Queue[hashtable]]::new()
    $seen = @{}; $embPat = '^Chapter:\s*(\w+)--(.+?)\s*$'
    foreach ($l in $lines) {
        if ($l -match $script:citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
        if ($l -match $embPat) { $seen["$($matches[1])::$($matches[2])"] = $true }
    }
    $ordered = @()
    while ($queue.Count -gt 0) {
        $cite = $queue.Dequeue()
        $key = "$($cite.Quire)::$($cite.Name)"
        if ($seen[$key]) { continue }; $seen[$key] = $true
        $fwPath = Join-Path $script:QuireDirs[$cite.Quire] "$($cite.Name).codex"
        if (-not (Test-Path -PathType Leaf $fwPath)) { return $null }
        $fwLines = [System.IO.File]::ReadAllLines($fwPath)
        foreach ($l in $fwLines) { if ($l -match $script:citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) } }
        $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Lines = $fwLines }
    }
    [array]::Reverse($ordered)
    $emitted = @{}; $sb = [System.Text.StringBuilder]::new(524288)
    foreach ($entry in $ordered) {
        $key = "$($entry.Quire)::$($entry.Name)"
        if ($emitted[$key]) { continue }; $emitted[$key] = $true
        $renamed = $false
        foreach ($l in $entry.Lines) {
            if (-not $renamed -and $l -match '^Chapter:\s*(.+?)\s*$') {
                [void]$sb.Append("Chapter: $($entry.Quire)--$($matches[1])`n"); $renamed = $true
            } else { [void]$sb.Append($l + "`n") }
        }
        [void]$sb.Append("`n`n")
    }
    foreach ($l in $lines) { [void]$sb.Append($l + "`n") }
    return $sb.ToString()
}

# Build combined REPL input
$inputSb = [System.Text.StringBuilder]::new(10485760)
$testNames = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $sources.Count; $i++) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($sources[$i])
    $testOut = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    $resolved = Resolve-Source $sources[$i]
    if ($null -eq $resolved) {
        "8" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
        "error 3010: foreword resolution failed" | Set-Content -Path (Join-Path $testOut 'build.log') -Encoding UTF8
        continue
    }
    $testNames.Add($name)
    $mode = if ($testNames.Count -eq 1) { "CDX repl`n" } else { "CDX`n" }
    [void]$inputSb.Append($mode)
    [void]$inputSb.Append($resolved)
    [void]$inputSb.Append([char]4)
}
if ($testNames.Count -eq 0) { exit 0 }

$inputFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($inputFile, $inputSb.ToString(), [System.Text.UTF8Encoding]::new($false))
$outputFile = [System.IO.Path]::GetTempFileName()
$stderrFile = [System.IO.Path]::GetTempFileName()
$Stage0 = Join-Path (Split-Path $PSScriptRoot) 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) { Write-Error "MISSING: $Stage0"; exit 2 }

$proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @(
    '-kernel', $Stage0, '-input', $inputFile, '-output', $outputFile, '-mem', '2048', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
$proc.WaitForExit(1800000)
if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force } catch {} }

# Parse output byte-by-byte: text lines interleaved with binary CDX blocks.
$raw = if (Test-Path $outputFile) { [System.IO.File]::ReadAllBytes($outputFile) } else { [byte[]]::new(0) }
$pos = 0; $testIdx = 0

function NextLine {
    if ($script:pos -ge $raw.Length) { return $null }
    $start = $script:pos
    while ($script:pos -lt $raw.Length -and $raw[$script:pos] -ne 10) { $script:pos++ }
    $end = $script:pos
    if ($script:pos -lt $raw.Length) { $script:pos++ }
    $len = $end - $start
    if ($len -gt 0 -and $raw[$start + $len - 1] -eq 13) { $len-- }
    if ($len -le 0) { return '' }
    return [System.Text.Encoding]::UTF8.GetString($raw, $start, $len)
}

function SkipBytes { param([int]$n); $script:pos = [Math]::Min($script:pos + $n, $raw.Length) }

while ($testIdx -lt $testNames.Count -and $pos -lt $raw.Length) {
    $name = $testNames[$testIdx]
    $testOut = Join-Path $OutRoot $name
    $logLines = [System.Collections.Generic.List[string]]::new()
    $exitCode = '4'

    :testloop while ($pos -lt $raw.Length) {
        $line = NextLine
        if ($null -eq $line) { break }

        if ($line.StartsWith('SIZE:')) {
            $binSize = 0
            if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }
            if ($binSize -gt 0 -and $pos + $binSize -le $raw.Length) {
                $binBytes = New-Object byte[] $binSize
                [Array]::Copy($raw, $pos, $binBytes, 0, $binSize)
                [System.IO.File]::WriteAllBytes((Join-Path $testOut "$name.cdx"), $binBytes)
                SkipBytes $binSize
                $mapLines = [System.Collections.Generic.List[string]]::new()
                [void]$mapLines.Add('# Codex Symbol Map')
                [void]$mapLines.Add('# Address         Size  Name')
                $inMap = $false
                while ($pos -lt $raw.Length) {
                    $tl = NextLine; if ($null -eq $tl) { break }
                    if ($tl.StartsWith('MAP:')) { $inMap = $true; continue }
                    if ($tl.StartsWith('MAP-END')) { $inMap = $false; continue }
                    if ($inMap -and $tl.StartsWith('0x')) { [void]$mapLines.Add($tl); continue }
                    if ($tl.StartsWith('STACK:')) { break }
                    if ($tl.StartsWith('HEAP:') -or $tl.StartsWith('WD:') -or $tl.StartsWith('PROF:')) { continue }
                }
                if ($mapLines.Count -gt 2) {
                    [System.IO.File]::WriteAllLines((Join-Path $testOut "$name.map"), $mapLines, [System.Text.UTF8Encoding]::new($false))
                }
                $exitCode = '0'
            }
            break testloop
        }
        elseif ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            $logLines.Add($line)
            while ($pos -lt $raw.Length) {
                $el = NextLine; if ($null -eq $el) { break }
                if ($el.StartsWith('STACK:')) { break }
                if ($el.StartsWith('HEAP:') -or $el.StartsWith('WD:')) { continue }
                $logLines.Add($el)
            }
            $exitCode = '7'
            break testloop
        }
        elseif ($line.StartsWith('!EXC')) {
            $logLines.Add($line); $exitCode = '4'; break
        }
        elseif ($line.StartsWith('WD:') -or $line.StartsWith('HEAP:') -or $line.StartsWith('STACK:')) {
            if ($line.StartsWith('STACK:')) { break testloop }
        }
        else { if ($line) { $logLines.Add($line) } }
    }

    [System.IO.File]::WriteAllLines((Join-Path $testOut 'build.log'), $logLines.ToArray(), [System.Text.UTF8Encoding]::new($false))
    $exitCode | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
    $testIdx++
}

while ($testIdx -lt $testNames.Count) {
    $name = $testNames[$testIdx]; $testOut = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    "99" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
    "FAIL: VM died before this test" | Set-Content -Path (Join-Path $testOut 'build.log') -Encoding UTF8
    $testIdx++
}

Remove-Item -Force $inputFile, $outputFile, $stderrFile -ErrorAction SilentlyContinue
Write-SweepLog "batch-done pcore=$PCore compiled=$($sources.Count)"
