# lwm2m-client-compile.ps1 -- compile tools/lwm2m-client.codex and refuse a
# tool that has drifted out of type with the chapters it cites.
#
# Hand-written; no generator under codex/build/ emits this.
#
# WHY THIS IS ONLY A COMPILE. The tool's own prose says it: there is no LwM2M
# server on this box, so the binary run against nothing reports that the
# datagrams left and none came back, which is a true statement about the
# socket and no statement at all about the protocol. build/coap-interop-test.ps1
# is the shape the interop arm would take once a server exists (Leshan or
# another), and this script is the part that is worth having before then.
#
# WHY IT EXISTS AT ALL. tools/ota-fetch.codex sat at head with two CDX2001
# type errors because Lwm2mFirmware's signatures moved under it and no script
# compiled it. build/check-tools.ps1 reports a tool no script names, and
# tools/lwm2m-client.codex was the one it found. A tool nothing compiles is a
# tool that is already broken and has not been told yet.

[CmdletBinding()]
param(
    [string]$Kernel = '',
    [switch]$KeepWork
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$src  = Join-Path $root 'tools\lwm2m-client.codex'

# The DEPOT seed, not build-output, because build-output/bare-metal/Codex.cdx
# holds whichever compiler ran last and a stale one turns a real failure into
# "my change did nothing" (OperatorsManual, "Pass -Kernel when you do").
if (-not $Kernel) { $Kernel = Join-Path $root 'seed\Codex.cdx' }

# Refuse rather than skip: a check that quietly does nothing when its inputs
# are missing reports green for having run nothing.
foreach ($p in @($src, $Kernel)) {
    if (-not (Test-Path -PathType Leaf $p)) {
        Write-Host "REFUSED: missing $p" -ForegroundColor Red
        exit 1
    }
}

$out = Join-Path $root 'build-output\lwm2m-client'
New-Item -ItemType Directory -Force $out | Out-Null
$cdx = Join-Path $out 'lwm2m-client.cdx'
$log = Join-Path $out 'lwm2m-client.cdx.log'
if (Test-Path $cdx) { Remove-Item $cdx -Force }

Write-Host "lwm2m-client-compile: compiling tools/lwm2m-client.codex"
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'compile.ps1') `
    -Src 'tools/lwm2m-client.codex' -Out $cdx -Log $log -Kernel $Kernel | Out-Null

# The artifact is the verdict, not the exit code: a compile that drops its
# output while reporting success is the failure check-compile-drop.ps1 exists
# for, and an error line in the log is the one that names the site.
$errors = @()
if (Test-Path $log) {
    $errors = @(Select-String -Path $log -Pattern ': error ' | ForEach-Object { $_.Line })
}

if ($errors.Count -gt 0) {
    Write-Host "lwm2m-client-compile: FAIL -- the tool does not compile against the chapters it cites" -ForegroundColor Red
    foreach ($e in $errors) { Write-Host "  $e" }
    exit 1
}

if (-not (Test-Path -PathType Leaf $cdx)) {
    Write-Host "lwm2m-client-compile: FAIL -- no error line and no artifact; see $log" -ForegroundColor Red
    exit 1
}

$size = (Get-Item $cdx).Length
if (-not $KeepWork) { Remove-Item $cdx -Force }
Write-Host "lwm2m-client-compile: OK -- $size bytes (compile only; no LwM2M server on this box)"
exit 0
