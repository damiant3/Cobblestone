# check-cap-tables.ps1 — verify the two capability bit-tables agree
#
# A program earns its authority by ONE of two paths: the boot grant the
# compiler emits (boot-cap-mask-for, over cap-* in X86_64Boot.codex) or the
# verified-load grant the loader computes (cdx-cap-to-kern-bits, over
# kern-cap-* in VerifiedLoader.codex). Both expand the SAME name->bit table
# by hand, in two files, because the compiler cannot cite the OS quire and
# the OS loader is not baked into the compiler. If bit N means Camera on one
# path and Microphone on the other, the same binary gets different authority
# depending on which door it came in through. That is BACKLOG 1.12.
#
# 1.13's in-compiler guard (check-cap-vocab-coherent) cannot reach across the
# quire boundary. This is the cross-quire half: parse both tables and fail the
# build if any capability's bit disagrees, or if a capability is present on
# one side and not the other. Drift becomes un-shippable.
#
# Usage: check-cap-tables.ps1   (prints MATCH + exit 0, or the drift + exit 1)

param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BootFile = Join-Path $Repo 'codex\compiler\Emit\X86_64Boot.codex'
$LoaderFile = Join-Path $Repo 'codex\os\verify\VerifiedLoader.codex'

# A capability bit sits in a 64-bit mask, so its position is < 64. This is
# what separates the bit constants from same-prefixed address cells such as
# cap-expiry-addr = 28936, which is not part of the table.
function Get-CapBits([string]$file, [string]$prefix) {
    $bits = @{}
    $rx = "^\s+$([regex]::Escape($prefix))(\S+)\s*:\s*Integer\s*=\s*(\d+)\s*$"
    foreach ($line in [System.IO.File]::ReadAllLines($file)) {
        if ($line -match $rx) {
            $val = [int]$matches[2]
            if ($val -lt 64) { $bits[$matches[1]] = $val }
        }
    }
    return $bits
}

$boot = Get-CapBits $BootFile 'cap-'
$kern = Get-CapBits $LoaderFile 'kern-cap-'

if ($boot.Count -eq 0 -or $kern.Count -eq 0) {
    Write-Host "ERROR: found no capability bits (boot=$($boot.Count), loader=$($kern.Count)) -- parser or file moved"
    exit 1
}

$errs = [System.Collections.Generic.List[string]]::new()
$names = ([string[]]$boot.Keys + [string[]]$kern.Keys | Sort-Object -Unique)
foreach ($n in $names) {
    $inB = $boot.ContainsKey($n)
    $inK = $kern.ContainsKey($n)
    if ($inB -and -not $inK) {
        $errs.Add("cap-$n = $($boot[$n]) in X86_64Boot has no kern-cap-$n in VerifiedLoader")
    } elseif ($inK -and -not $inB) {
        $errs.Add("kern-cap-$n = $($kern[$n]) in VerifiedLoader has no cap-$n in X86_64Boot")
    } elseif ($boot[$n] -ne $kern[$n]) {
        $errs.Add("bit drift for '$n': cap-$n = $($boot[$n]) vs kern-cap-$n = $($kern[$n])")
    }
}

if ($errs.Count -gt 0) {
    Write-Host "WARNING: capability bit tables DISAGREE (boot grant vs verified load) -- BACKLOG 1.12"
    foreach ($e in $errs) { Write-Host "  $e" }
    Write-Host "  Fix X86_64Boot.codex cap-* and VerifiedLoader.codex kern-cap-* so every capability maps to the same bit."
    exit 1
}

Write-Host "Capability bit tables MATCH ($($boot.Count) capabilities: boot cap-* == loader kern-cap-*)"
exit 0
