# The registry, asked three times, and nothing else.
#
# ONE QUESTION: after cdx-registry serves a request, does its accept loop come
# back round and does the NEXT request reach it? That is the whole of what has
# blocked peer discovery, and registry-locate-test.ps1 answers it only incidentally
# after ~10 minutes of compiling four guests. This reuses the cdx that harness
# already left in test-output/registry-locate and takes about ninety seconds.
#
# Run the full harness once first to produce the cdx; after that, iterate here.
# Rebuilding this by hand every time is what made previous sessions slow, which
# is why it is in the depot rather than in somebody's scratch directory.
#
# It prints the GUEST's own trace, which is the thing worth having: it shows
# `serve-one: accepting` / `accepted` / `handle-conn` / `answer-locate: sent`
# per request, so a server that is looping correctly looks different from one
# that is stuck, without inference from the outside.
#
#   pwsh build/registry-probe.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location 'D:\Projects\NewRepository-val'
. .\build\vm-config.ps1
. .\build\work-wire.ps1

$out    = 'test-output/registry-locate'
$regCdx = "$out/cdx-registry.cdx"
if (-not (Test-Path $regCdx)) { Write-Host "no $regCdx -- run the full harness once first"; exit 1 }

$port = 19660 + (Get-Random -Min 0 -Max 30)
$tag  = 'regonly'
Write-Host "registry cdx: $regCdx"
Write-Host "host port   : $port -> guest 9301"

$p = Start-Process -FilePath $script:CodexVmBin -PassThru -WindowStyle Hidden `
    -ArgumentList @('-kernel', $regCdx, '-output', "$out/$tag.out",
                    '-portfwd', "${port}:9301", '-mem', '3072', '-headless') `
    -RedirectStandardError "$out/$tag.err"

try {
    Start-Sleep -Seconds 22
    $bogus = '0' * 64

    Write-Host '--- ask 1 ---'
    $r1 = Invoke-WorkLocate -HostName '127.0.0.1' -Port $port -Hash $bogus -TimeoutSec 60
    if ($null -eq $r1) { Write-Host '  ask 1: NO ANSWER' } else { Write-Host "  ask 1: answered, peers=$($r1.Count)" }

    Write-Host '--- ask 2 ---'
    $r2 = Invoke-WorkLocate -HostName '127.0.0.1' -Port $port -Hash $bogus -TimeoutSec 60
    if ($null -eq $r2) { Write-Host '  ask 2: NO ANSWER' } else { Write-Host "  ask 2: answered, peers=$($r2.Count)" }

    Write-Host '--- ask 3 ---'
    $r3 = Invoke-WorkLocate -HostName '127.0.0.1' -Port $port -Hash $bogus -TimeoutSec 60
    if ($null -eq $r3) { Write-Host '  ask 3: NO ANSWER' } else { Write-Host "  ask 3: answered, peers=$($r3.Count)" }
} finally {
    Stop-VmGraceful -ProcessId $p.Id -TimeoutMs 15000
    Start-Sleep -Seconds 1
    Write-Host ''
    Write-Host '=== GUEST TRACE (registry.out) ==='
    Get-Content "$out/$tag.out" -ErrorAction SilentlyContinue
    Write-Host ''
    Write-Host '=== accepts / recv seen by the NAT ==='
    Get-Content "$out/$tag.err" -ErrorAction SilentlyContinue |
        Select-String 'accepted host client|PORTFWD recv' |
        ForEach-Object { $_.Line }
}
