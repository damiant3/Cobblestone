# Which transpiler plugs emit the IR's field SLOT as a division?
#
# The IR spells a record field as `name/slot`; a plug that copies it through
# emits `a/0`, which divides by zero for slot 0, is silently right for slot 1
# and silently wrong from slot 2 (plugs-backlog 1.2). No runtime is needed to
# SEE it: compile one probe to IR, feed the IR to every plug that runs over
# TCP, and grep each plug's output for the slot. python is the known-good arm
# (fixed 13199) and must come out clean, so a run where every plug "passes"
# is a broken grep, not a fixed tree.
#
#   build/check-plug-field-slot.ps1            # table + verdict against the baseline
#   build/check-plug-field-slot.ps1 -Update    # rewrite build/plug-field-slot-baseline.txt
#
# Exit 1 on a plug that leaks and is NOT in the baseline (a regression, or a
# new plug copying an old emitter), on python leaking (the oracle is dead),
# or on a plug whose run fails outright. A plug in the baseline that comes
# out clean is reported so the baseline can be shrunk with -Update; the
# baseline is a debt list and is meant to reach zero, one reviewed emitter
# at a time.
param(
    [switch]$Update,
    [int]$TimeoutSec = 120
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$baselinePath = Join-Path $repo 'build/plug-field-slot-baseline.txt'
$work = Join-Path ([IO.Path]::GetTempPath()) ("plugslot-" + (Split-Path $repo -Leaf))
New-Item -ItemType Directory -Force $work | Out-Null

$probe = Join-Path $work 'probe.codex'
@'
Chapter: SlotProbe

  Pair = record {
    a : Integer,
    b : Integer
  }

  opening : [Console] Nothing = act
   print-line-uni (integer-to-text (Pair { a = 7, b = 9 }).a)
   print-line-uni (integer-to-text (Pair { a = 7, b = 9 }).b)
  end
'@ | Set-Content $probe -Encoding ASCII
$ir = Join-Path $work 'probe.ir'
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $probe -Out $ir -Log (Join-Path $work 'probe.log') -IrCce *> (Join-Path $work 'compile.txt')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ir)) { Write-Host "FAIL: probe did not compile to IR"; Get-Content (Join-Path $work 'probe.log') | Select-Object -Last 5; exit 1 }

$baseline = @()
if (Test-Path $baselinePath) { $baseline = @(Get-Content $baselinePath | Where-Object { $_ -match '^[a-z0-9-]+$' }) }

$plugRun = Join-Path $repo 'build/plug-run.ps1'

# Five plugs do not speak the shared `plug-run.ps1` protocol and so were
# outside this check entirely until 2026-08-16: `maui`, `winforms`, `wgsl` and
# `ptx` use a serial pipeline (IR to a file, boot, read `-output`), and `wpf`
# hand-rolls TCP because it answers with a DIRECTORY of files. Skipping them
# was not neutral. Three of the four that emit a field reference at all were
# leaking, in exactly the bare-map-key shape the reachable plugs had, and
# nothing could see it while this check reported "0 of 38 clean".
#
# They are driven through their OWN run.ps1 with the probe as -Src, which also
# exercises the real service path including `-Passes 'text-plug'`, and the grep
# runs over whatever appears at -Out, file or directory. A plug that produces
# nothing is a RUN FAILURE, never a pass.
$ownRunner = @('maui', 'winforms', 'wpf', 'wgsl', 'ptx')

$rows = @(); $leak = @(); $failed = @(); $newLeak = @(); $healed = @()
foreach ($dir in Get-ChildItem (Join-Path $repo 'codex/plugs') -Directory | Sort-Object Name) {
    $runPs = Join-Path $dir.FullName 'run.ps1'
    if (-not (Test-Path $runPs)) { continue }
    $text = Get-Content $runPs -Raw
    $viaOwn = $ownRunner -contains $dir.Name
    if (-not $viaOwn) {
        if ($text -notmatch 'plug-run\.ps1') { continue }
        if ($text -notmatch '-Port\s+(\d+)') { continue }
    }
    $port = if ($viaOwn) { 0 } else { [int]$Matches[1] }
    $mem = 2048; if ($text -match '-MemMB\s+(\d+)') { $mem = [int]$Matches[1] }
    $name = $dir.Name
    $cdx = Join-Path $dir.FullName "build-output/$name-plug.cdx"
    if (-not (Test-Path $cdx)) {
        $b = Join-Path $dir.FullName 'build.ps1'
        if (Test-Path $b) { & pwsh -NoProfile -File $b *> (Join-Path $work "$name-build.log") }
    }
    if (-not (Test-Path $cdx)) { $failed += $name; $rows += "{0,-12} NO BINARY" -f $name; continue }
    $out = Join-Path $work "$name.out"
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    if ($viaOwn) {
        & pwsh -NoProfile -File $runPs -Src $probe -Out $out *> (Join-Path $work "$name-run.log")
    } else {
        & pwsh -NoProfile -File $plugRun -IrInput $ir -Out $out -PlugCdx $cdx -MemMB $mem -Port $port -TimeoutSec $TimeoutSec *> (Join-Path $work "$name-run.log")
    }
    $emitted = @()
    if (Test-Path $out) {
        $emitted = if ((Get-Item $out).PSIsContainer) { @(Get-ChildItem $out -Recurse -File) } else { @(Get-Item $out) }
    }
    $emitted = @($emitted | Where-Object { $_.Length -gt 0 })
    if ($LASTEXITCODE -ne 0 -or $emitted.Count -eq 0) { $failed += $name; $rows += "{0,-12} RUN FAILED (exit $LASTEXITCODE)" -f $name; continue }
    $hits = @($emitted | ForEach-Object { Select-String -Path $_.FullName -Pattern '\b[ab]/[01]\b' -AllMatches } | ForEach-Object { $_.Matches } | ForEach-Object { $_.Value })
    $inBase = $baseline -contains $name
    if ($hits.Count -gt 0) {
        $leak += $name
        if (-not $inBase) { $newLeak += $name }
        $rows += "{0,-12} LEAKS {1}{2}" -f $name, (($hits | Sort-Object -Unique) -join ' '), $(if ($inBase) { '' } else { '   NEW' })
    } else {
        if ($inBase) { $healed += $name }
        $rows += "{0,-12} clean{1}" -f $name, $(if ($inBase) { '   (in baseline: shrink it with -Update)' } else { '' })
    }
}
$rows | ForEach-Object { Write-Host "  $_" }
$total = $leak.Count + ($rows.Count - $leak.Count - $failed.Count)
Write-Host ("check-plug-field-slot: {0} plugs run, {1} leak the slot, {2} clean, {3} failed to run" -f ($rows.Count - $failed.Count), $leak.Count, ($rows.Count - $failed.Count - $leak.Count), $failed.Count)

if ($Update) {
    @('# plug-field-slot-baseline.txt -- generated by build/check-plug-field-slot.ps1 -Update',
      '#', '# Transpiler plugs known to emit the IR field slot as a division (plugs-backlog',
      '# 1.2). A DEBT LIST: the check fails on a leaker that is not here, and reports',
      '# one that is here and has stopped leaking so this file can shrink. Every name',
      '# below is a wrong program that exits 0.', '') + ($leak | Sort-Object) | Set-Content $baselinePath -Encoding ASCII
    Write-Host "baseline rewritten: $($leak.Count) plugs"
}
$bad = 0
if ($leak -contains 'python') { Write-Host "FAIL: python leaks the slot; the oracle no longer discriminates (it was fixed in 13199)"; $bad++ }
if ($newLeak.Count -gt 0 -and -not $Update) { Write-Host "FAIL: leaking and not in the baseline: $($newLeak -join ', ')"; $bad++ }
if ($failed.Count -gt 0) { Write-Host "FAIL: did not run: $($failed -join ', ') (logs in $work)"; $bad++ }
if ($healed.Count -gt 0 -and -not $Update) { Write-Host "note: clean but still in the baseline: $($healed -join ', ')" }
if ($bad -gt 0) { exit 1 }
exit 0
