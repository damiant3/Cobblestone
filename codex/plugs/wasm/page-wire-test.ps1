# Grade the page's NATIVE BACKEND modules -- the `irbytes` rows, which take IR
# text and answer a binary wire rather than source. `page-lens-test.ps1` grades
# the `ir` rows and `page-bytes-test.ps1` the `bytes` rows; between them the
# irbytes transport had no runner at all, so riscv and arm64 shipped graded by
# nothing (L-NOGATE). This is that runner.
#
# THE ORACLE SHARES NO CODE WITH THE SUBJECT, which is the whole point. The
# module is emitted by the wasm plug (WasmEmitter.codex); the bare-metal plug
# CDX is built from the backend's own chapters and never goes near that
# emitter. So a defect in the emitter cannot move both arms the same way, and
# agreement between them is evidence rather than a tautology.
#
# THREE MECHANICS THAT EACH READ EXACTLY LIKE A BROKEN PLUG. All three cost
# real time on 2026-08-29 and none of them is discoverable from a failure:
#
#   1. THE TWO ARMS TAKE DIFFERENT ENCODINGS. The module reads IR-UNI text.
#      `<plug>/run.ps1` stamps an IR-CCE mode header on whatever bytes it is
#      handed and passes them through, so it must be given IR-CCE. Fed IR-UNI
#      it does not refuse: it parses UTF-8 as CCE and dies `!EXC=06` at a fixed
#      RIP. Both native plugs die identically, because they share the parser,
#      which reads as a common regression rather than as one bad input.
#   2. BOTH ARMS MUST USE THE SAME PASSES. `-Passes 'text-plug'` REPLACES the
#      default pipeline rather than adding to it, so passing it to one arm and
#      not the other grades folded-and-inlined IR against unfolded IR. That
#      surfaces as a difference at wire byte 0 and reads like a codegen defect.
#   3. THE METAL CAPTURE IS NOT THE WIRE. codex-vm writes a leading 0x01
#      marker, and the plug prints FUNCMAP/WCET text after the wire, so the
#      wire is the middle. Comparing captures compares the tail too.
#
# -Calibrate is what makes a green mean anything, and it is a SABOTAGE rather
# than a garbage-input arm. Feeding these modules non-IR does not make them
# refuse -- they answer a plausible, shorter wire, which is a real gap recorded
# in plugs-backlog 2.06 and not something this runner can assert on. What can
# be asserted is that the COMPARISON fires: corrupt one byte of the module's
# wire and the run must go red at exactly that byte.
[CmdletBinding()]
param(
    [string]$Subject,
    [string]$Kernel,
    [string[]]$Only,
    [switch]$Calibrate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

foreach ($tool in @('wasmtime')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "REFUSE: $tool is not on PATH."; exit 2
    }
}

. (Join-Path $PSScriptRoot 'page-lenses.ps1')
$WIRES = @($PageModules | Where-Object { $_.transport -eq 'irbytes' })
if ($WIRES.Count -eq 0) { Write-Host 'REFUSE: the manifest carries no irbytes row.'; exit 2 }

# pwsh -File hands an array over as ONE string, so -Only a,b arrives as 'a,b'.
# Split it, and REFUSE on a name in no row: a filter selecting nothing runs
# zero subjects and still prints '0 failed', which is a screen that cannot fail
# dressed as a pass. Same reasoning as page-lens-test.ps1.
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
if ($Only) {
    $known = @($WIRES | ForEach-Object { $_.plug })
    $unknown = @($Only | Where-Object { $known -notcontains $_ })
    if ($unknown) { Write-Host ("REFUSE: -Only names no wire row: {0}" -f ($unknown -join ', ')); exit 2 }
    $WIRES = @($WIRES | Where-Object { $Only -contains $_.plug })
}

$work = Join-Path $PSScriptRoot 'build-output\wire-test'
New-Item -ItemType Directory -Force -Path $work | Out-Null
if (-not $Subject) { $Subject = Join-Path $Repo 'codex\test\factorial.codex' }
if (-not (Test-Path -PathType Leaf $Subject)) { Write-Host "REFUSE: no subject at $Subject"; exit 2 }

# One compile per encoding, shared by every row, both with the SAME passes.
$uniLog = Join-Path $work 'uni.log'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $Subject -Out (Join-Path $work 'uni.out') -Log $uniLog -IrUni -Passes 'text-plug' -Kernel $Kernel | Out-Null
$log = [IO.File]::ReadAllText($uniLog)
$a = $log.IndexOf('IR-BEGIN'); $b = $log.IndexOf('IR-END')
if ($a -lt 0 -or $b -lt $a) { Write-Host "REFUSE: the subject produced no IR-UNI. See $uniLog"; exit 2 }
$uniIr = Join-Path $work 'subject.uni.ir'
[IO.File]::WriteAllText($uniIr, $log.Substring($a + 9, $b - ($a + 9)).Trim(), [Text.UTF8Encoding]::new($false))

$cceIr = Join-Path $work 'subject.cce.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $Subject -Out $cceIr -Log (Join-Path $work 'cce.log') -IrCce -Passes 'text-plug' -Kernel $Kernel | Out-Null
if (-not (Test-Path -PathType Leaf $cceIr)) { Write-Host 'REFUSE: the subject produced no IR-CCE.'; exit 2 }

Write-Host ("[wire] subject : {0}" -f (Split-Path $Subject -Leaf))
Write-Host ("[wire] IR      : {0:N0} bytes uni, {1:N0} bytes cce, same passes" -f `
    (Get-Item $uniIr).Length, (Get-Item $cceIr).Length)
if ($Calibrate) { Write-Host '[wire] CALIBRATION: one wire byte is corrupted; every row must go RED at that byte.' }
Write-Host ''

$rows = @()
foreach ($W in $WIRES) {
    $plug = $W.plug
    $module = Join-Path $Repo "codex\plugs\$plug\build-output\$plug-stdio.wasm"
    if (-not (Test-Path -PathType Leaf $module)) {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'ABSENT'; bytes = 0; note = 'module not built' }
        continue
    }
    $metalCdx = Join-Path $Repo "codex\plugs\$plug\build-output\$plug-plug.cdx"
    if (-not (Test-Path -PathType Leaf $metalCdx)) {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'NOORACLE'; bytes = 0; note = "no $plug-plug.cdx; run codex/plugs/$plug/build.ps1" }
        continue
    }

    $wasmOut = Join-Path $work "$plug.wasm.bin"
    $p = Start-Process -FilePath 'wasmtime' `
        -ArgumentList @('-W', 'max-wasm-stack=16777216', $module) -NoNewWindow -PassThru `
        -RedirectStandardInput $uniIr -RedirectStandardOutput $wasmOut -RedirectStandardError "$wasmOut.err"
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'TRAP'; bytes = 0; note = "wasmtime exit $($p.ExitCode)" }
        continue
    }

    $metalOut = Join-Path $work "$plug.metal.bin"
    & pwsh -NoProfile -File (Join-Path $Repo "codex\plugs\$plug\run.ps1") -IrInput $cceIr -Out $metalOut 2>&1 | Out-Null
    if (-not (Test-Path -PathType Leaf $metalOut)) {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'NOMETAL'; bytes = 0; note = 'the oracle produced no capture' }
        continue
    }

    $w = [IO.File]::ReadAllBytes($wasmOut)
    $m = [IO.File]::ReadAllBytes($metalOut)
    if ($Calibrate -and $w.Length -gt 20) { $w[20] = $w[20] -bxor 0xFF }

    if ($w.Length -lt 12) {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'SHORT'; bytes = $w.Length; note = 'shorter than a wire header' }
        continue
    }
    # A guest FAULT is a register dump, never a wire. Name it rather than
    # comparing two failed runs: those differ because the RIP moves with the
    # build, so a byte comparison over them reads as a real difference.
    $head = [Text.Encoding]::ASCII.GetString($m, 0, [Math]::Min(8, $m.Length))
    if ($head -match '!EXC') {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'FAULT'; bytes = 0; note = "the oracle faulted: $($head.Trim())" }
        continue
    }

    $off = if ($m.Length -gt 0 -and $m[0] -eq 1) { 1 } else { 0 }
    if ($m.Length - $off -lt $w.Length) {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'TRUNC'; bytes = $w.Length
                                    note = "capture holds $($m.Length - $off) wire bytes, module produced $($w.Length)" }
        continue
    }
    $diff = -1
    for ($i = 0; $i -lt $w.Length; $i++) { if ($w[$i] -ne $m[$off + $i]) { $diff = $i; break } }
    if ($diff -ge 0) {
        $rows += [pscustomobject]@{ plug = $plug; verdict = 'DIFFERS'; bytes = $w.Length
                                    note = ("first difference at wire byte {0}: wasm 0x{1:X2} vs metal 0x{2:X2}" -f $diff, $w[$diff], $m[$off + $diff]) }
        continue
    }
    $code = [BitConverter]::ToUInt32($w,0); $data = [BitConverter]::ToUInt32($w,4); $funcs = [BitConverter]::ToUInt32($w,8)
    $rows += [pscustomobject]@{ plug = $plug; verdict = 'OK'; bytes = $w.Length
                                note = "code=$code data=$data funcs=$funcs, tail $($m.Length - $off - $w.Length) bytes" }
}

foreach ($r in $rows) {
    Write-Host ("  {0,-8} {1,-9} {2,9:N0}  {3}" -f $r.plug, $r.verdict, $r.bytes, $r.note)
}
Write-Host ''

$ok = @($rows | Where-Object { $_.verdict -eq 'OK' }).Count
$bad = $rows.Count - $ok
if ($Calibrate) {
    if ($ok -gt 0) {
        Write-Host "FAIL: CALIBRATION -- $ok row(s) matched with a corrupted wire byte, so the comparison is not firing."
        exit 1
    }
    Write-Host "[wire] CALIBRATION PASSED: no row matched a corrupted wire ($($rows.Count) graded)."
    exit 0
}
Write-Host ("[wire] {0} identical, {1} not, of {2} graded" -f $ok, $bad, $rows.Count)
if ($bad -gt 0) { exit 1 }
exit 0
