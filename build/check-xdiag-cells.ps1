# check-xdiag-cells.ps1 -- Fail the build when two owners claim the same xdiag cell.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [switch]$Quiet
)

# Fail the build when two owners claim the same xdiag cell.
# 
# The xdiag block is one flat array of 32-bit cells at xhci-diag (GopXhci),
# written by xdiag-put and read by xdiag-get. Every subsystem that wants a
# breadcrumb takes cells out of it, nothing arbitrates, and no compiler or
# gate has ever looked -- so two owners of one cell is green everywhere and
# only shows up as a wrong number on a photograph of a boot screen.
# 
# It happened. Until 2026-08-13 usb-hid-note wrote 80 + idx*4 for four
# devices, which is cells 80..95, on top of gfat-cell-stage 80,
# msc-cell-lastcc 81, msc-cell-phase 82, gfat-cell-write 83,
# xhci-cell-fuel-left 84, msc-cell-fuel-lo 85, msc-cell-fail-lba 86 and
# msc-cell-retry 87. The two writers run in order and neither reads the
# other, so the storage diagnostics were right and DeskBoot's HID table was
# quietly showing storage state as device descriptors. A cell the storage
# path did not write on a given boot kept its HID value instead, which is
# how an F12 mount failure came to report a report-buffer address as a FAT
# write stage.
# 
# WHAT THIS CHECKS, and it is deliberately narrow: named constants only,
# declared as `<name> : Integer = <n>` in a chapter that uses xdiag, plus
# the block claims declared in the table below. It does NOT parse literal
# `xdiag-put 19` calls -- there are ~90 of those in GopXhci alone and they
# are that chapter's own private map. The rule this enforces is the one
# that was broken: a subsystem taking a RANGE must declare it here.
# 
# Usage:  pwsh build/check-xdiag-cells.ps1
# Exit 0 clean, 1 on an overlap.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path


# Ranges a subsystem claims as a block, inclusive. A block is declared here
# because the code computes its cells rather than naming each one, so no
# constant scan can see them.

$blocks = @(@{ Name = 'xhci scalars (GopXhci private map)'; Lo = 0; Hi = 19 }, @{ Name = 'xhci port cells (20 + port, xhci-port-cells=8)'; Lo = 20; Hi = 27 }, @{ Name = 'xhci ownership/BAR scalars'; Lo = 28; Hi = 46 }, @{ Name = 'usb-hid-cell-count (the HID bind counter)'; Lo = 47; Hi = 47 }, @{ Name = 'xhci controller table (48 + i*4, 4 controllers)'; Lo = 48; Hi = 63 }, @{ Name = 'xhci-ep-base block (6 words)'; Lo = 64; Hi = 69 }, @{ Name = 'msc and FAT cells'; Lo = 70; Hi = 91 }, @{ Name = 'usb-hid-note block (usb-hid-cell-base + idx*4, 4 devices)'; Lo = 96; Hi = 111 })


# Constants that name a single cell. gfat-cell-* live inside the msc range
# by agreement (both are storage) and are listed so the map is complete.

$named = @{}
$files = @()
foreach ($r in @('apps', 'codex', 'build/boot')) {
    $files += @(Get-ChildItem $r -Recurse -Filter '*.codex' -File)
}
foreach ($f in $files) {
    $text = (Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue)
    if ((-not $text)) {
        continue
    }
    if ((-not ($text -match 'xdiag-(put|get)'))) {
        continue
    }
    # `-cell-` with the trailing hyphen, deliberately: the plurals
    # (msc-cells, med-cells, nvme-cells, um-cells, xhci-port-cells) are BASE
    # ADDRESSES of separate scratch regions, not indices into xdiag, and the
    # first run of this script flagged msc-cells at 36480 for exactly that.

    foreach ($m in ([regex]::Matches($text, '(?m)^\s+((?:msc|gfat|xhci)-cell-[\w-]*|usb-hid-cell-base)\s*:\s*Integer\s*=\s*(\d+)\s*$'))) {
        $name = $m.Groups[1].Value
        $val = [int]$m.Groups[2].Value
        if ((-not $named.ContainsKey($name))) {
            $named[$name] = @{ Cell = $val; File = $f.Name }
        }
    }
}


$problems = @()

# msc-cell-end is an EXCLUSIVE bound for xdiag-zero, not a stored cell, so
# it is allowed to equal another owner's first cell. Naming it here rather
# than special-casing silently.

$exclusiveBounds = @('msc-cell-end')

# Sorted, because .NET randomises string hashing per process: the shipped
# script walked $named.Keys in bucket order and printed the two owners of a
# shared cell in a different order on about one run in three. The verdict
# never moved, but a failure that reads differently each time is a failure
# nobody can diff.

$byCell = @{}
foreach ($k in ($named.Keys | Sort-Object)) {
    if (($exclusiveBounds -contains $k)) {
        continue
    }
    $c = $named[$k].Cell
    if ((-not $byCell.ContainsKey($c))) {
        $byCell[$c] = @()
    }
    $byCell[$c] = ($byCell[$c] + ([string]$k + ([string]' (' + ([string]$named[$k].File + ')'))))
}
foreach ($c in ($byCell.Keys | Sort-Object)) {
    if ((@($byCell[$c]).Count -gt 1)) {
        $problems += ([string]'cell ' + ([string]$c + ([string]': claimed by ' + ($byCell[$c] -join ' AND '))))
    }
}


for ($i = 0; ($i -lt @($blocks).Count); $i++) {
    for ($j = ($i + 1); ($j -lt @($blocks).Count); $j++) {
        $a = $blocks[$i]
        $b = $blocks[$j]
        if ((($a.Lo -le $b.Hi) -and ($b.Lo -le $a.Hi))) {
            $lo = ([Math]::Max($a.Lo, $b.Lo))
            $hi = ([Math]::Min($a.Hi, $b.Hi))
            $problems += ([string]'cells ' + ([string]$lo + ([string]'..' + ([string]$hi + ([string]': block ''' + ([string]$a.Name + ([string]''' overlaps block ''' + ([string]$b.Name + ''''))))))))
        }
    }
}


foreach ($k in ($named.Keys | Sort-Object)) {
    if (($exclusiveBounds -contains $k)) {
        continue
    }
    $c = $named[$k].Cell
    $inSome = $false
    foreach ($b in $blocks) {
        if ((($c -ge $b.Lo) -and ($c -le $b.Hi))) {
            $inSome = $true
            break
        }
    }
    if ((-not $inSome)) {
        $problems += ([string]'cell ' + ([string]$c + ([string]': ''' + ([string]$k + ([string]''' (' + ([string]$named[$k].File + ') is outside every declared block -- add a block row or move it'))))))
    }
}


if ((-not $Quiet)) {
    Write-Host ([string]'xdiag cell map (' + ([string]$named.Count + ([string]' named constants, ' + ([string]@($blocks).Count + ' declared blocks):'))))
    foreach ($b in ($blocks | Sort-Object Lo)) {
        Write-Host ('  {0,3}..{1,-3} {2}' -f $b.Lo, $b.Hi, $b.Name)
    }
}


if ((@($problems).Count -gt 0)) {
    Write-Host ''
    Write-Host 'check-xdiag-cells: FAIL' -ForegroundColor Red
    foreach ($p in $problems) {
        Write-Host ([string]'  ' + $p) -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'Two owners of one cell is green in every gate and shows up as a wrong'
    Write-Host 'number on a photograph of a boot screen. Move one, and update the'
    Write-Host 'registry in docs/OperatorsManual.md.'
    exit 1
}

Write-Host 'check-xdiag-cells: OK' -ForegroundColor Green
exit 0
