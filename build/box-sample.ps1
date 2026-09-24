# box-sample.ps1 -- record the box every few seconds while a run is live:
# free memory, guest count and working set, Renode, pwsh, cpu.
#
# Hand-written; no generator under codex/build/ emits this. Lifted from the
# commander's scratchpad sampler on 2026-09-08 for the diagnostic release
# (Damian: "measure the actual memory situation"), so a gate's memory profile
# is a CSV in the tree rather than a number remembered from a terminal.
#
# BOUNDED ON PURPOSE. -Seconds is mandatory: an unbounded sampler is the
# leftover class CLAUDE.md R-GATE names (a poller outliving the run it
# watched), and 21 of them hung Git for Windows on 2026-09-02. Launch it
# detached beside the run, with -Seconds a little longer than the run, and
# name the CSV in status.json.
#
#   pwsh -File build/box-sample.ps1 -Out docs/Agents/box-release-2026-09-23.csv -Seconds 5400
#
# -Out may not sit under build-output (the gate deletes it) or build/output
# (the gate renames it), and the header is rewritten whenever the file is gone.
#
# Columns: ts, freeGiB, guests (codex-vm, qemu, wasmtime), guestMB, renode,
# renodeMB, pwsh, pwshMB, cpu%, supervisors, supervisorMB. A codex-vm running
# -run-list builds no partition (tools/codex-vm.c, the early dispatch), so it
# is a supervisor and not a guest. Every VM-host process is also written per
# sample to <Out>.procs.csv as ts, pid, name, role, wsMB.
param(
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][int]$Seconds,
    [int]$IntervalSec = 5
)

$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out = [System.IO.Path]::GetFullPath($Out)
foreach ($cleaned in @((Join-Path $repo 'build-output'), (Join-Path $repo 'build\output'))) {
    if ($Out.StartsWith($cleaned + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "REFUSED: -Out $Out is under $cleaned, which the gate removes during a run."
        exit 2
    }
}
$procsOut = [System.IO.Path]::ChangeExtension($Out, '.procs.csv')
$header = "ts,freeGiB,guests,guestMB,renode,renodeMB,pwsh,pwshMB,cpu,supervisors,supervisorMB"
$procsHeader = "ts,pid,name,role,wsMB"
$hostFilter = "Name='codex-vm.exe' OR Name='qemu-system-x86_64.exe' OR Name='wasmtime.exe'"
$header | Set-Content $Out
$procsHeader | Set-Content $procsOut
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    try {
        $ts = Get-Date -Format 'HH:mm:ss'
        $os = Get-CimInstance Win32_OperatingSystem
        $free = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
        $hosts = @(Get-CimInstance Win32_Process -Filter $hostFilter | ForEach-Object {
            $role = if ($_.Name -eq 'codex-vm.exe' -and $_.CommandLine -match '(^|\s)-run-list(\s|$)') { 'supervisor' } else { 'guest' }
            [pscustomobject]@{ Pid = $_.ProcessId; Name = $_.Name; Role = $role; WsMB = [int]($_.WorkingSetSize / 1MB) }
        })
        $g = @($hosts | Where-Object { $_.Role -eq 'guest' })
        $s = @($hosts | Where-Object { $_.Role -eq 'supervisor' })
        $r = @(Get-Process Renode, renode, mono -ErrorAction SilentlyContinue)
        $p = @(Get-Process pwsh, powershell -ErrorAction SilentlyContinue)
        $cpu = (Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average
        $line = "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}" -f $ts, $free, $g.Count,
            [int](($g | Measure-Object WsMB -Sum).Sum), $r.Count,
            [int](($r | Measure-Object WorkingSet64 -Sum).Sum / 1MB), $p.Count,
            [int](($p | Measure-Object WorkingSet64 -Sum).Sum / 1MB), $cpu,
            $s.Count, [int](($s | Measure-Object WsMB -Sum).Sum)
        if (-not (Test-Path -PathType Leaf $Out)) { $header | Set-Content $Out }
        Add-Content $Out $line
        if ($hosts.Count -gt 0) {
            if (-not (Test-Path -PathType Leaf $procsOut)) { $procsHeader | Set-Content $procsOut }
            Add-Content $procsOut @($hosts | ForEach-Object { "{0},{1},{2},{3},{4}" -f $ts, $_.Pid, $_.Name, $_.Role, $_.WsMB })
        }
    } catch {}
    Start-Sleep -Seconds $IntervalSec
}
