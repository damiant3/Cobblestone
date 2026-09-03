# Build every wasm module the compile page ships, from the one manifest
# (page-lenses.ps1). Before this script the modules were unbuildable from the
# tree: build-plug-wasm.ps1 existed and nothing called it, and the shipped
# modules survived only as base64 inside the deployed page (PRISM-7 stage 0).
#
#   codex/plugs/wasm/build-page-modules.ps1                # all rows
#   codex/plugs/wasm/build-page-modules.ps1 -Only pe,img   # some rows
#   codex/plugs/wasm/build-page-modules.ps1 -Missing       # absent modules only
#
# PARALLEL AT FOUR SLOTS since 2026-08-31, and the RAM measurement this header
# used to defer to is 15.8 GB total with 7.4 GB free under load, which is the
# same reasoning that put the batteries at -Jobs 4 rather than 8. The rows are
# independent: each bundles its own source and emits its own WAT. Serial, the
# set took about 11 minutes and left three slots idle behind the long tail.
#
# Every row carries a BUDGET and a row that overruns it is killed and named.
# Twice, this script sat on a silent riscv-stdio for 25 minutes: the .wat
# emitted, wat2wasm never started, and there was no process, no output and no
# timeout to read, so it looked exactly like work. -TimeoutSec is what turns
# that into a diagnosis instead of a nap.
[CmdletBinding()]
param(
    [string[]]$Only,
    [switch]$Missing,
    [string]$Kernel,
    [int]$Jobs = 4,
    [int]$TimeoutSec = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

. (Join-Path $PSScriptRoot 'page-lenses.ps1')

$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
if ($Only) {
    $known = @($PageModules | ForEach-Object { $_.plug })
    $unknown = @($Only | Where-Object { $known -notcontains $_ })
    if ($unknown) { Write-Host ("REFUSE: -Only names no module row: {0}" -f ($unknown -join ', ')); exit 2 }
}

$builder = Join-Path $Repo 'codex\plugs\common\build-plug-wasm.ps1'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$built = 0; $skipped = 0; $failed = @(); $timedOut = @()

# The rows to actually run, in the order to start them.
$rows = @()
foreach ($m in $PageModules) {
    if ($Only -and ($Only -notcontains $m.plug)) { continue }
    $target = Join-Path $Repo ("codex\plugs\{0}\build-output\{1}" -f $m.plug, $m.file)
    if ($Missing -and (Test-Path -PathType Leaf $target)) { $skipped++; continue }
    if ($m.transport -eq 'self') {
        $own = Join-Path $Repo ("codex\plugs\{0}\build-wasm.ps1" -f $m.plug)
        if (-not (Test-Path -PathType Leaf $own)) {
            Write-Host ("REFUSE: {0} is transport 'self' and has no build-wasm.ps1" -f $m.plug); exit 2
        }
        $rows += @{ plug = $m.plug; target = $target; script = $own; args = @('-Kernel', $Kernel) }
    } else {
        # The native rows carry three extra fields; a lens row carries none of
        # them and is unaffected.
        $extra = @()
        if ($m.ContainsKey('withLir') -and $m.withLir) { $extra += '-WithLir' }
        if ($m.ContainsKey('common')  -and $m.common)  { $extra += @('-CommonChapters', $m.common) }
        if ($m.ContainsKey('decks')   -and $m.decks)   { $extra += @('-Decks', $m.decks) }
        $rows += @{ plug = $m.plug; target = $target; script = $builder;
                    args = @('-Plug', $m.plug, '-Chapters', $m.chapters,
                             '-Transport', $m.transport, '-Kernel', $Kernel) + $extra }
    }
}

# HEAVY ROWS FIRST, and that is the whole of the speedup. The tail is what
# costs: riscv and arm64 bundle the largest sources and emit the largest WAT,
# so starting them last leaves three slots idle while one row finishes alone.
# Dealt first they overlap with the whole rest of the set.
$heavy = @('riscv', 'arm64')
$rows = @($rows | Where-Object { $heavy -contains $_.plug }) +
        @($rows | Where-Object { $heavy -notcontains $_.plug })

$logDir = Join-Path $Repo 'codex\plugs\wasm\build-output\module-logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Write-Host ("[modules] {0} row(s), {1} slot(s), {2} s per-row budget" -f $rows.Count, $Jobs, $TimeoutSec)

$queue = [System.Collections.Queue]::new(@($rows))
$live = @()

function Start-Row($r) {
    $out = Join-Path $logDir ("{0}.out" -f $r.plug)
    $err = Join-Path $logDir ("{0}.err" -f $r.plug)
    # Start-Process joins -ArgumentList with spaces and quotes nothing, so a
    # chapter selector carrying a space ('PePlug:Network Config|Drain|Body')
    # arrived at the builder as two arguments and four rows (csharp, img, pe,
    # elf) failed on a path made of the second half. The serial launcher this
    # replaced splatted the array and never met it. Quote what has whitespace.
    $quoted = @($r.args | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } })
    $p = Start-Process -FilePath 'pwsh' -PassThru -NoNewWindow `
         -ArgumentList (@('-NoProfile', '-File', $r.script) + $quoted) `
         -RedirectStandardOutput $out -RedirectStandardError $err
    Write-Host ("[modules] start {0}" -f $r.plug)
    @{ row = $r; proc = $p; out = $out; err = $err; t0 = [DateTime]::UtcNow }
}

while ($queue.Count -gt 0 -or $live.Count -gt 0) {
    while ($live.Count -lt $Jobs -and $queue.Count -gt 0) { $live += Start-Row $queue.Dequeue() }

    Start-Sleep -Milliseconds 500
    $still = @()
    foreach ($j in $live) {
        $age = ([DateTime]::UtcNow - $j.t0).TotalSeconds
        if (-not $j.proc.HasExited -and $age -lt $TimeoutSec) { $still += $j; continue }

        if (-not $j.proc.HasExited) {
            # A ROW THAT HANGS MUST REFUSE BY NAME. build-page-modules sat on a
            # silent riscv-stdio for 25 minutes twice (CurrentPlan, 2026-08-31):
            # the .wat emitted, wat2wasm never started, no process, no output, no
            # timeout, and the .wasm silently kept its old timestamp. A step that
            # hangs with no diagnostic is worse than one that fails, because the
            # flattering reading ("it is the slow row") is free and wrong.
            try { Stop-Process -Id $j.proc.Id -Force -ErrorAction Stop } catch { }
            $timedOut += $j.row.plug
            $failed += $j.row.plug
            Write-Host ("[modules] TIMEOUT: {0} made no progress in {1:N0} s; killed. Its log is {2}" -f `
                        $j.row.plug, $age, $j.out)
            continue
        }

        $ok = ($j.proc.ExitCode -eq 0) -and (Test-Path -PathType Leaf $j.row.target)
        if ($ok) {
            $built++
            Write-Host ("[modules] ok      {0} ({1:N0} s)" -f $j.row.plug, $age)
        } else {
            $failed += $j.row.plug
            Write-Host ("[modules] FAIL: {0} (exit {1}); its log is {2}" -f $j.row.plug, $j.proc.ExitCode, $j.out)
            Get-Content $j.err -ErrorAction SilentlyContinue | Select-Object -Last 8 |
                ForEach-Object { Write-Host ("    {0}" -f $_) }
        }
    }
    $live = $still
}

$sw.Stop()
Write-Host ''
Write-Host ("[modules] {0} built, {1} skipped, {2} failed, in {3:N0} s at {4} slot(s)" -f `
            $built, $skipped, $failed.Count, ($sw.ElapsedMilliseconds / 1000), $Jobs)
if ($timedOut.Count -gt 0) {
    Write-Host ("[modules] TIMED OUT: {0}" -f ($timedOut -join ', '))
}
if ($failed.Count -gt 0) {
    Write-Host ("[modules] failed: {0}" -f ($failed -join ', '))
    exit 1
}
exit 0
