# Append one row to docs/Agents/box-holds.csv: the commander's box decision and
# the box as it was measured at that moment. Damian, 2026-09-08: "too many
# times I see an agent held up for the box with 20% cpu and 65% memory
# utilized only." Every HOLD and every GO is logged with the numbers that
# justified it, so the threshold can be tuned from evidence.
#
#   build/box-hold-log.ps1 -Lane val -Decision HOLD -Ask "1 guest 45 s" -Reason "two guests up beside a rehearsal"
#   build/box-hold-log.ps1 -Lane red -Decision GO   -Ask "BVT -Jobs 4"    -Reason "box empty"
#
# Columns: time, lane, decision, ask, reason, cpu%, freeGiB, totalGiB, mem%,
# guests, guestOwners, renode, qemu, pwshBuild. cpu% is a 2-second sample of
# the whole box; guestOwners is the workspace each codex-vm was launched from.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Lane,
    [Parameter(Mandatory)] [ValidateSet('HOLD', 'GO', 'DEFER')] [string]$Decision,
    [Parameter(Mandatory)] [string]$Ask,
    [Parameter(Mandatory)] [string]$Reason,
    [string]$Log = (Join-Path $PSScriptRoot '..\docs\Agents\box-holds.csv')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$os = Get-CimInstance Win32_OperatingSystem
$totalGiB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
$freeGiB = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
$memPct = [math]::Round(100 * (1 - $os.FreePhysicalMemory / $os.TotalVisibleMemorySize), 0)
$cpu = (Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 2 -MaxSamples 1).CounterSamples[0].CookedValue
$cpuPct = [math]::Round($cpu, 0)

$vms = @(Get-CimInstance Win32_Process -Filter "Name='codex-vm.exe'")
$owners = @($vms | ForEach-Object {
    if ($_.CommandLine -match 'Cobblestone-([a-z]+)') { $Matches[1] } else { '?' }
}) -join '+'
$renode = @(Get-Process -Name Renode -ErrorAction SilentlyContinue).Count
$qemu = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count
$pwshBuild = @(Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" | Where-Object {
    $_.CommandLine -match '-File .*(build\.ps1|bvt\.ps1|diag-arm\.ps1|test-cross|compile\.ps1)'
}).Count

$q = { param($s) '"' + ([string]$s -replace '"', '""') + '"' }
$row = @(
    (Get-Date -Format 'yyyy-MM-dd HH:mm'), $Lane, $Decision, (& $q $Ask), (& $q $Reason),
    $cpuPct, $freeGiB, $totalGiB, $memPct, $vms.Count, $owners, $renode, $qemu, $pwshBuild
) -join ','

$logPath = [System.IO.Path]::GetFullPath($Log)
# The log lives in the depot, so a submitted copy is read-only: open it for
# edit before appending. The rows ride up in the commander's next copy-up.
if ((Test-Path $logPath) -and (Get-Item $logPath).IsReadOnly) {
    & p4 edit $logPath 2>&1 | Out-Null
}
if (-not (Test-Path $logPath)) {
    Set-Content -Path $logPath -Value 'time,lane,decision,ask,reason,cpuPct,freeGiB,totalGiB,memPct,guests,guestOwners,renode,qemu,pwshBuild' -Encoding ascii
}
Add-Content -Path $logPath -Value $row -Encoding ascii
Write-Host $row
