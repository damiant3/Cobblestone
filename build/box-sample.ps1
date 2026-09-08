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
#   pwsh -File build/box-sample.ps1 -Out build-output/box-release.csv -Seconds 5400
#
# Columns: ts, freeGiB, guests (codex-vm, qemu, wasmtime), guestMB, renode,
# renodeMB, pwsh, pwshMB, cpu%. Reading it: the minimum of freeGiB is the
# floor the run touched; the row where guests peaks is the fan-out's true
# width (L-REQUEST: -Jobs is what was asked, guests is what ran).
param(
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][int]$Seconds,
    [int]$IntervalSec = 5
)

$ErrorActionPreference = 'Continue'
"ts,freeGiB,guests,guestMB,renode,renodeMB,pwsh,pwshMB,cpu" | Set-Content $Out
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        $free = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
        $g = @(Get-Process codex-vm, qemu-system-x86_64, wasmtime -ErrorAction SilentlyContinue)
        $r = @(Get-Process Renode, renode, mono -ErrorAction SilentlyContinue)
        $p = @(Get-Process pwsh, powershell -ErrorAction SilentlyContinue)
        $cpu = (Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average
        $line = "{0},{1},{2},{3},{4},{5},{6},{7},{8}" -f (Get-Date -Format 'HH:mm:ss'), $free, $g.Count,
            [int](($g | Measure-Object WorkingSet64 -Sum).Sum / 1MB), $r.Count,
            [int](($r | Measure-Object WorkingSet64 -Sum).Sum / 1MB), $p.Count,
            [int](($p | Measure-Object WorkingSet64 -Sum).Sum / 1MB), $cpu
        Add-Content $Out $line
    } catch {}
    Start-Sleep -Seconds $IntervalSec
}
