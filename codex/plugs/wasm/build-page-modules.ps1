# Build every wasm module the compile page ships, from the one manifest
# (page-lenses.ps1). Before this script the modules were unbuildable from the
# tree: build-plug-wasm.ps1 existed and nothing called it, and the shipped
# modules survived only as base64 inside the deployed page (PRISM-7 stage 0).
#
#   codex/plugs/wasm/build-page-modules.ps1                # all rows
#   codex/plugs/wasm/build-page-modules.ps1 -Only pe,img   # some rows
#   codex/plugs/wasm/build-page-modules.ps1 -Missing       # absent modules only
#
# Sequential on purpose: each build boots a codex-vm for the wat emission, and
# the standing -Jobs ruling is about harnesses that are already parallel; if
# this ever needs to be one of them, deal rows across slots at -Jobs 4 and
# re-measure host RAM first.
[CmdletBinding()]
param(
    [string[]]$Only,
    [switch]$Missing,
    [string]$Kernel
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
$built = 0; $skipped = 0; $failed = @()

foreach ($m in $PageModules) {
    if ($Only -and ($Only -notcontains $m.plug)) { continue }
    $target = Join-Path $Repo ("codex\plugs\{0}\build-output\{1}" -f $m.plug, $m.file)
    if ($Missing -and (Test-Path -PathType Leaf $target)) { $skipped++; continue }

    if ($m.transport -eq 'self') {
        $own = Join-Path $Repo ("codex\plugs\{0}\build-wasm.ps1" -f $m.plug)
        if (-not (Test-Path -PathType Leaf $own)) {
            Write-Host ("REFUSE: {0} is transport 'self' and has no build-wasm.ps1" -f $m.plug); exit 2
        }
        & pwsh -NoProfile -File $own -Kernel $Kernel
    } else {
        # The native rows carry three extra fields; a lens row carries none of
        # them and is unaffected.
        $extra = @()
        if ($m.ContainsKey('withLir') -and $m.withLir) { $extra += '-WithLir' }
        if ($m.ContainsKey('common')  -and $m.common)  { $extra += @('-CommonChapters', $m.common) }
        if ($m.ContainsKey('decks')   -and $m.decks)   { $extra += @('-Decks', $m.decks) }
        & pwsh -NoProfile -File $builder -Plug $m.plug -Chapters $m.chapters `
            -Transport $m.transport -Kernel $Kernel @extra
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $target)) {
        $failed += $m.plug
        Write-Host ("[modules] FAIL: {0}" -f $m.plug)
    } else {
        $built++
    }
}

$sw.Stop()
Write-Host ''
Write-Host ("[modules] {0} built, {1} skipped, {2} failed, in {3:N0} s" -f `
            $built, $skipped, $failed.Count, ($sw.ElapsedMilliseconds / 1000))
if ($failed.Count -gt 0) {
    Write-Host ("[modules] failed: {0}" -f ($failed -join ', '))
    exit 1
}
exit 0
