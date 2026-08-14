# check-vm-differential.ps1 -- Compile one source through BOTH VM hosts and compare the bytes
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$Src = '',
    [string]$Kernel = '',
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Why this check exists, and it is trust rather than correctness.
#
# QEMU is a third-party binary and its Authenticode signature cannot tell a
# good build from a hostile one. The publisher's signing certificate expired
# 2023-09-12 and the binaries are still signed with it, so verification
# returns the same failure whether or not that key is still in the
# publisher's hands. Revocation is the mechanism that would normally carry
# the news, and there is nothing left to revoke. A check that answers
# identically in both worlds carries no information.
#
# Two independent VM hosts agreeing on the output byte for byte does carry
# information. codex-vm is ours and QEMU is theirs; they share no code, no
# accelerator and no author. A QEMU altered to corrupt compiler output has to
# reproduce the exact bytes our own hypervisor produces from the same source
# on the same machine.
#
# This is not a proof. It is one sample, and it does not catch an attacker
# who corrupts only a seed rebuild. It is an assertion with a runner, which
# is more than the certificate has ever been.
#
# Exit 0 on match, and 0 on a skip: a machine with only one host is the
# normal case, not a failure. Exit 1 on a byte mismatch, which is the whole
# reason the script exists.

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'vm-config.ps1')

if (-not $Src) { $Src = Join-Path $Repo 'codex' 'test' 'act-let-scope.codex' }
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed' 'Codex.cdx' }

# Host discovery in vm-config honours CODEX_VM_HOST, which this script sets
# per arm. Ask the filesystem instead so the skip decision is about what is
# installed rather than about how the last caller was configured.
$codexVmPresent = Test-Path -PathType Leaf (Join-Path $Repo 'tools' 'codex-vm.exe')
$qemuPresent = [bool]$script:FallbackVmBin

if (-not $codexVmPresent -or -not $qemuPresent) {
    $missing = if (-not $codexVmPresent) { 'codex-vm' } else { 'qemu' }
    if (-not $Quiet) {
        Write-Host "  vm-differential: SKIP (only one VM host on this machine; $missing missing)"
    }
    exit 0
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "vmdiff-$PID"
New-Item -ItemType Directory -Force -Path $work | Out-Null
$compile = Join-Path $PSScriptRoot 'compile.ps1'

# The QEMU arm runs FIRST, deliberately. It is the arm that can hang, and
# paying for the trusted compile before discovering that is waste.
function Invoke-Arm {
    param([string]$Name, [string]$HostVal)
    $cdx = Join-Path $work "$Name.cdx"
    $log = Join-Path $work "$Name.log"
    $prev = $env:CODEX_VM_HOST
    $env:CODEX_VM_HOST = $HostVal
    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & pwsh -NoProfile -File $compile -Src $Src -Out $cdx -Log $log -Kernel $Kernel *> $null
        $code = $LASTEXITCODE
        $sw.Stop()
    } finally {
        $env:CODEX_VM_HOST = $prev
    }
    if ($code -ne 0 -or -not (Test-Path -PathType Leaf $cdx)) {
        return @{ Ok = $false; Name = $Name; Code = $code }
    }
    return @{
        Ok = $true
        Name = $Name
        Cdx = (Get-FileHash $cdx -Algorithm SHA256).Hash
        Map = if (Test-Path "$cdx.map") { (Get-FileHash "$cdx.map" -Algorithm SHA256).Hash } else { '' }
        Seconds = [int]$sw.Elapsed.TotalSeconds
    }
}

$qemu = Invoke-Arm -Name 'qemu' -HostVal 'qemu'
if (-not $qemu.Ok) {
    Write-Host "  vm-differential: FAIL (the qemu arm did not produce a binary, exit $($qemu.Code))"
    Write-Host "      Detail: CODEX_VM_HOST=qemu pwsh build/compile.ps1 -Src $Src -Out out.cdx -Log out.log -Kernel $Kernel"
    exit 1
}

$cvm = Invoke-Arm -Name 'codexvm' -HostVal ''
if (-not $cvm.Ok) {
    Write-Host "  vm-differential: FAIL (the codex-vm arm did not produce a binary, exit $($cvm.Code))"
    exit 1
}

if ($qemu.Cdx -ne $cvm.Cdx) {
    Write-Host '  vm-differential: MISMATCH -- the two VM hosts disagree on the compiler output'
    Write-Host "      source:  $Src"
    Write-Host "      kernel:  $Kernel"
    Write-Host "      qemu:    $($qemu.Cdx)  [$($script:FallbackVmBin)]"
    Write-Host "      codexvm: $($cvm.Cdx)"
    Write-Host '      One of these two hosts is not running the program it was given. Keep'
    Write-Host "      the artifacts in $work and do not delete them."
    exit 1
}

if ($qemu.Map -ne $cvm.Map) {
    Write-Host '  vm-differential: MISMATCH -- binaries agree but the .map sidecars differ'
    Write-Host "      qemu:    $($qemu.Map)"
    Write-Host "      codexvm: $($cvm.Map)"
    exit 1
}

Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
if (-not $Quiet) {
    Write-Host "  vm-differential: OK (both hosts agree, $($qemu.Cdx.Substring(0, 16)), qemu $($qemu.Seconds)s / codex-vm $($cvm.Seconds)s)"
}
exit 0
