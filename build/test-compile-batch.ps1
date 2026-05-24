# Batch compiler: boots one VM, compiles multiple sources through the
# repl-loop. Each source is fed sequentially over the persistent TCP
# connection. The compiler resets its heap between compiles automatically.
#
# Input:  -ListFile  path to a file containing one source path per line
#         -OutRoot   base output directory (per-test subdirs created)
#         -PCore     P-core index for VM affinity
#
# Output: per-test files under $OutRoot/$name/:
#           $name.cdx   — compiled binary (if compile succeeded)
#           build.log   — compile log (always)
#           .exitcode   — "0" or "4" (compile ok / fail)
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

$Stage0 = Join-Path (Split-Path $PSScriptRoot) 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) {
    Write-Error "MISSING: $Stage0"
    exit 2
}

$sources = @(Get-Content -Path $ListFile | Where-Object { $_.Trim() -ne '' })
if ($sources.Count -eq 0) { exit 0 }

$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core';
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net';
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched';
    'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'; 'Signal' = 'codex\foreword\signal';
    'Compress' = 'codex\foreword\compress'; 'Encode' = 'codex\foreword\encode';
    'Math' = 'codex\foreword\math'; 'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai';
    'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'; 'Magic' = 'apps\games\magic'; 'CodexMagic' = 'apps\games\codexmagic'; 'Games' = 'apps\games\classic';
    'Spark' = 'apps\spark'; 'Data' = 'apps\data'
}
$citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|CodexMagic|Games|Spark|Data)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$embeddedPat = '^Chapter:\s*(\w+)--(.+?)\s*$'

function Resolve-Forewords {
    param([string]$SrcPath)
    $queue = [System.Collections.Generic.Queue[hashtable]]::new()
    $seen = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($SrcPath)) {
        if ($line -match $script:citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
        if ($line -match $script:embeddedPat) { $seen["$($matches[1])::$($matches[2])"] = $true }
    }
    $ordered = @()
    while ($queue.Count -gt 0) {
        $cite = $queue.Dequeue()
        $key = "$($cite.Quire)::$($cite.Name)"
        if ($seen[$key]) { continue }
        $seen[$key] = $true
        $fwPath = Join-Path $script:QuireDirs[$cite.Quire] "$($cite.Name).codex"
        if (-not (Test-Path -PathType Leaf $fwPath)) {
            Write-SweepLog "$($cite.Name) foreword not found at $fwPath"
            return $null
        }
        $lines = [System.IO.File]::ReadAllLines($fwPath)
        foreach ($l in $lines) {
            if ($l -match $script:citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
        }
        $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Lines = $lines }
    }
    [array]::Reverse($ordered)
    $emitted = @{}
    $sb = [System.Text.StringBuilder]::new(524288)
    foreach ($entry in $ordered) {
        $key = "$($entry.Quire)::$($entry.Name)"
        if ($emitted[$key]) { continue }
        $emitted[$key] = $true
        $renamed = $false
        foreach ($l in $entry.Lines) {
            if (-not $renamed -and $l -match '^Chapter:\s*(.+?)\s*$') {
                [void]$sb.Append("Chapter: $($entry.Quire)--$($matches[1])`n")
                $renamed = $true
            } else {
                [void]$sb.Append($l + "`n")
            }
        }
        [void]$sb.Append("`n`n")
    }
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($sb.ToString())
    return ,$bytes
}

function Compile-One {
    param(
        [System.IO.Stream]$DataStream,
        [System.IO.Stream]$CtrlStream,
        [string]$SrcPath,
        [string]$TestOutDir,
        [string]$Name,
        [bool]$IsFirst
    )
    $log = Join-Path $TestOutDir 'build.log'
    $bin = Join-Path $TestOutDir "$Name.cdx"
    $exitFile = Join-Path $TestOutDir '.exitcode'

    $fwBytes = Resolve-Forewords $SrcPath
    if ($null -eq $fwBytes) {
        "error 3010: foreword resolution failed" | Set-Content -Path $log -Encoding UTF8
        '8' | Set-Content -Path $exitFile -Encoding UTF8
        return
    }

    $hdr = [System.Text.Encoding]::UTF8.GetBytes("CDX`n")
    $srcBytes = [System.IO.File]::ReadAllBytes($SrcPath)
    $DataStream.Write($hdr, 0, $hdr.Length)
    if ($fwBytes.Length -gt 0) { $DataStream.Write($fwBytes, 0, $fwBytes.Length) }
    $DataStream.Write($srcBytes, 0, $srcBytes.Length)
    $DataStream.WriteByte(4)
    $DataStream.Flush()

    Set-Content -Path $log -Value '' -Encoding UTF8
    $binSize = 0
    $status = ''
    while ($true) {
        $line = Read-StreamLine -Stream $DataStream -TimeoutSec 30
        if ($null -eq $line) { $status = 'timeout'; break }
        if ($line.StartsWith('SIZE:')) {
            $tail = $line.Substring(5)
            if ($tail -match '^\d+') { $binSize = [int]$matches[0] }
            $status = 'size'
            break
        }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            Add-Content -Path $log -Value $line -Encoding UTF8
            $status = 'halted'
            break
        }
        if ($line.StartsWith('!EXC')) {
            $excLines = [System.Collections.Generic.List[string]]::new()
            $excLines.Add($line)
            for ($si = 0; $si -lt 4; $si++) {
                $sline = Read-StreamLine -Stream $DataStream -TimeoutSec 2
                if ($null -eq $sline) { break }
                $excLines.Add($sline)
                if (-not $sline.Contains('S[')) { break }
            }
            $report = Format-CrashReport $excLines.ToArray()
            foreach ($rl in $report) {
                Add-Content -Path $log -Value $rl -Encoding UTF8
                [Console]::Error.WriteLine("  $rl")
            }
            $status = 'fatal'
            break
        }
        if (-not $line.StartsWith('WD:')) {
            Add-Content -Path $log -Value $line -Encoding UTF8
        }
    }

    if ($status -eq 'fatal' -or $status -eq 'timeout') {
        '4' | Set-Content -Path $exitFile -Encoding UTF8
        throw "REPL-DEAD: $Name ($status)"
    }

    if ($status -eq 'size' -and $binSize -gt 0) {
        $readTimeout = if ($binSize -gt 1048576) { 300 } else { 60 }
        $bytes = Read-StreamBytes -Stream $DataStream -Count $binSize -TimeoutSec $readTimeout
        if ($null -ne $bytes -and $bytes.Length -eq $binSize -and $binSize -ge 100) {
            [System.IO.File]::WriteAllBytes($bin, $bytes)
            $mapLine = Read-StreamLine -Stream $DataStream -TimeoutSec 5
            if ($null -ne $mapLine -and $mapLine.StartsWith('MAP:')) {
                $mapCount = 0
                if ($mapLine.Substring(4) -match '^\d+') { $mapCount = [int]$matches[0] }
                if ($mapCount -gt 0) {
                    $mapFile = Join-Path $TestOutDir "$Name.map"
                    $mapSb = [System.Text.StringBuilder]::new($mapCount * 60)
                    [void]$mapSb.AppendLine('# Codex Symbol Map')
                    [void]$mapSb.AppendLine('# Address         Size  Name')
                    while ($true) {
                        $ml = Read-StreamLine -Stream $DataStream -TimeoutSec 5
                        if ($null -eq $ml -or $ml.StartsWith('MAP-END')) { break }
                        [void]$mapSb.AppendLine($ml)
                    }
                    [System.IO.File]::WriteAllText($mapFile, $mapSb.ToString(), [System.Text.UTF8Encoding]::new($false))
                }
            }
            '0' | Set-Content -Path $exitFile -Encoding UTF8
        } else {
            "Binary size mismatch: expected $binSize" | Add-Content -Path $log -Encoding UTF8
            '5' | Set-Content -Path $exitFile -Encoding UTF8
        }
    } elseif ($status -eq 'halted') {
        '4' | Set-Content -Path $exitFile -Encoding UTF8
    } else {
        '4' | Set-Content -Path $exitFile -Encoding UTF8
    }

    # Drain remaining data-channel output (WD lines, etc) until nothing left.
    while ($true) {
        $line = Read-StreamLine -Stream $DataStream -TimeoutSec 2
        if ($null -eq $line) { break }
        if (-not $line.StartsWith('WD:')) {
            Add-Content -Path $log -Value $line -Encoding UTF8
        }
    }

    # Drain ctrl channel until STACK: (signals repl-loop reset complete).
    # Sequence is: HEAP:<n>\n STACK:<n>\n then compiler waits for next input.
    $sawStack = $false
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and -not $sawStack) {
        $cline = Read-StreamLine -Stream $CtrlStream -TimeoutSec 5
        if ($null -eq $cline) { break }
        if ($cline.StartsWith('STACK:')) { $sawStack = $true }
    }
}

$run = $null
try {
    $run = Start-VmRun -Kernel $Stage0 -ConnectTimeoutSec 30 -MemMB 2048 -PCore $PCore
    if (-not $run) {
        Write-Error "VM failed to start"
        exit 3
    }

    if (-not (Read-VmReady -Conn $run.Conn -TimeoutSec 30)) {
        Write-Error "READY not received"
        exit 3
    }

    $dataStream = $run.Conn.Data.GetStream()
    $ctrlStream = $run.Conn.Ctrl.GetStream()

    for ($i = 0; $i -lt $sources.Count; $i++) {
        $src = $sources[$i]
        $name = [System.IO.Path]::GetFileNameWithoutExtension($src)
        $testOut = Join-Path $OutRoot $name
        New-Item -ItemType Directory -Force -Path $testOut | Out-Null
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-SweepLog "$name batch-compile [$($i+1)/$($sources.Count)] pcore=$PCore"

        try {
            Compile-One -DataStream $dataStream -CtrlStream $ctrlStream `
                        -SrcPath $src -TestOutDir $testOut -Name $name `
                        -IsFirst ($i -eq 0)
            $sw.Stop()
            Write-SweepLog "$name done $([math]::Round($sw.Elapsed.TotalSeconds,1))s pcore=$PCore"
        } catch {
            $sw.Stop()
            Write-SweepLog "$name batch-compile-crash ($([math]::Round($sw.Elapsed.TotalSeconds,1))s): $_"
            $exitFile = Join-Path $testOut '.exitcode'
            '99' | Set-Content -Path $exitFile -Encoding UTF8

            # Compiler crashed. Restart VM for remaining items.
            Close-Vm -Conn $run.Conn -Process $run.Process
            $run = Start-VmRun -Kernel $Stage0 -ConnectTimeoutSec 30 -MemMB 2048 -PCore $PCore
            if (-not $run) {
                Write-Error "VM restart failed at item $i"
                exit 3
            }
            if (-not (Read-VmReady -Conn $run.Conn -TimeoutSec 30)) {
                Write-Error "READY not received after restart"
                exit 3
            }
            $dataStream = $run.Conn.Data.GetStream()
            $ctrlStream = $run.Conn.Ctrl.GetStream()
        }
    }

    Write-SweepLog "batch-done pcore=$PCore compiled=$($sources.Count)"
    Close-Vm -Conn $run.Conn -Process $run.Process
    Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    $run = $null
} finally {
    if ($run) {
        Close-Vm -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
