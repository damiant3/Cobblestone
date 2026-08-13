# ddc-witness.ps1 -- steps 3 and 4 of the diverse double-compiling witness.
#
# OperatorsManual "Running the DDC end to end" gives steps 1 and 2 as
# commands and then says, in prose:
#
#   3. Same bytes to both arms: mode line, source, trailing 0x04, UTF-8 no
#      BOM. Feed to CodexCs.exe on stdin and keep stdout as BYTES.
#   4. Slice at the CDX1 magic, then compare to seed/Codex.cdx.
#
# Nothing ran those two steps. They were done by hand on 2026-08-10 and the
# DDC became a release gate at 14534 with its last two steps unscripted, so
# every release re-derived them under time pressure. This is the runner.
#
# THE INPUT IS SELF-VALIDATING, which is the whole design. Reconstructing
# the bytes compile.ps1 feeds the compiler is the step that can silently be
# wrong, and a wrong input would make the C# arm disagree for a reason that
# has nothing to do with trust. So the SAME input file goes to both arms,
# and the Codex arm reproducing seed/Codex.cdx is what proves the input is
# right before the C# arm's answer is allowed to mean anything. An arm that
# cannot reproduce the seed from these bytes fails the run as INCONCLUSIVE
# rather than reporting the other arm's number.
#
# Exit 0 witness holds, 1 witness RED, 2 could not run / inconclusive.
[CmdletBinding()]
param(
    [string]$Repo   = '',
    [string]$Seed   = '',
    [string]$Source = '',
    [string]$CsExe  = '',
    [int]$MemMB     = 3072,
    # The seed is built -Repl, so the arms must be handed the same mode line
    # or they are not compiling the same thing the release ships.
    [string]$Mode   = 'CDX repl'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Repo   -eq '') { $Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
if ($Seed   -eq '') { $Seed   = Join-Path $Repo 'seed\Codex.cdx' }
if ($Source -eq '') { $Source = Join-Path $Repo 'build\output\Codex.codex' }
if ($CsExe  -eq '') { $CsExe  = Join-Path $Repo 'build-output\ddc-arm\bin\Release\net9.0\CodexCs.exe' }

foreach ($p in @($Seed, $Source, $CsExe)) {
    if (-not (Test-Path -PathType Leaf $p)) {
        Write-Host "INCONCLUSIVE: missing $p" -ForegroundColor Yellow
        exit 2
    }
}

$work = Join-Path $Repo 'build-output\ddc'
New-Item -ItemType Directory -Force -Path $work | Out-Null

# ---------------------------------------------------------------- the input
# Replicates compile.ps1's construction (its lines 105-204): resolve the cite
# order against what the source already embeds, prepend any chapter the source
# does NOT carry, append the source, terminate with EOT. A fully bundled
# Codex.codex embeds every chapter, so the cite prefix is normally empty --
# but it is computed rather than assumed, because assuming it is how the two
# arms would come to be fed different programs.
Push-Location $Repo
try {
    . (Join-Path $PSScriptRoot 'quire-map.ps1')

    $srcLines = [System.IO.File]::ReadAllLines($Source)
    $seedSeen = @{}
    foreach ($line in $srcLines) {
        if ($line -match '^Chapter:\s*(\w+)--(.+?)\s*$') { $seedSeen["$($matches[1])::$($matches[2])"] = $true }
    }
    $ordered = Resolve-CiteOrder -RootLines $srcLines -Repo '.' -SeedSeen $seedSeen

    $sb = [System.Text.StringBuilder]::new(4194304)
    $citePrefix = 0
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) { [void]$sb.Append($l + "`n"); $citePrefix++ }
    foreach ($l in $srcLines) { [void]$sb.Append($l + "`n") }
    [void]$sb.Append([char]4)

    $inputFile = Join-Path $work 'ddc-input.bin'
    [System.IO.File]::WriteAllText($inputFile, "$Mode`n$($sb.ToString())", [System.Text.UTF8Encoding]::new($false))
} finally { Pop-Location }

$inBytes = (Get-Item $inputFile).Length
Write-Host ("input: {0:N0} bytes  (mode '{1}', {2} cite-prefix line(s), source {3:N0} bytes)" -f `
    $inBytes, $Mode, $citePrefix, (Get-Item $Source).Length)

# ------------------------------------------------------------- CDX1 slicing
# The C# arm writes diagnostics to stdout AHEAD of the binary (67,380 bytes of
# it on 2026-08-10), so the stream must be cut at the magic. Comparing the raw
# stream reports total disagreement and localises nothing.
function Get-CdxSlice {
    param([byte[]]$Bytes, [string]$Label)
    $magic = [byte[]](0x43, 0x44, 0x58, 0x31)   # "CDX1"
    $limit = $Bytes.Length - 4
    for ($i = 0; $i -le $limit; $i++) {
        if ($Bytes[$i] -eq $magic[0] -and $Bytes[$i+1] -eq $magic[1] -and
            $Bytes[$i+2] -eq $magic[2] -and $Bytes[$i+3] -eq $magic[3]) {
            if ($i -gt 0) { Write-Host ("  {0}: skipped {1:N0} bytes of preamble before CDX1" -f $Label, $i) }
            return $Bytes[$i..($Bytes.Length - 1)]
        }
    }
    return $null
}

# ------------------------------------------------------------ the Codex arm
$codexOut = Join-Path $work 'codex-arm.cdx'
if (Test-Path $codexOut) { Remove-Item -Force $codexOut }
$vm = Join-Path $Repo 'tools\codex-vm.exe'
& $vm -kernel $Seed -input $inputFile -output $codexOut -mem $MemMB -headless *> (Join-Path $work 'codex-arm.log')
if (-not (Test-Path -PathType Leaf $codexOut)) {
    Write-Host "INCONCLUSIVE: the Codex arm produced no output; the input bytes are unproven." -ForegroundColor Yellow
    exit 2
}

# --------------------------------------------------------------- the C# arm
$csRaw = Join-Path $work 'csharp-arm.raw'
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $CsExe
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.WorkingDirectory = $Repo
$proc = [System.Diagnostics.Process]::Start($psi)

# stdout as BYTES. A text read would mangle the binary through the console
# encoding and every comparison after it would be measuring the transport.
$outTask = $proc.StandardOutput.BaseStream.CopyToAsync(
    ($fs = [System.IO.File]::Create($csRaw)))
$errTask = $proc.StandardError.ReadToEndAsync()

$stdin = [System.IO.File]::ReadAllBytes($inputFile)
$proc.StandardInput.BaseStream.Write($stdin, 0, $stdin.Length)
$proc.StandardInput.BaseStream.Flush()
$proc.StandardInput.Close()

$proc.WaitForExit()
$outTask.Wait()
$fs.Close()
[System.IO.File]::WriteAllText((Join-Path $work 'csharp-arm.err'), $errTask.Result)

# ------------------------------------------------------------- the verdict
$seedBytes  = [System.IO.File]::ReadAllBytes($Seed)
$codexBytes = Get-CdxSlice ([System.IO.File]::ReadAllBytes($codexOut)) 'codex arm'
$csBytes    = Get-CdxSlice ([System.IO.File]::ReadAllBytes($csRaw))    'c# arm'

if ($null -eq $codexBytes) { Write-Host "INCONCLUSIVE: no CDX1 in the Codex arm's output" -ForegroundColor Yellow; exit 2 }
if ($null -eq $csBytes)    { Write-Host "INCONCLUSIVE: no CDX1 in the C# arm's output (exit $($proc.ExitCode))" -ForegroundColor Yellow; exit 2 }

# The signature region. Bytes 40..135 are stamped by the sign phase rather
# than emitted by the compiler, so they are expected to differ and nothing
# else is.
$SIG_LO = 40
$SIG_HI = 135

function Compare-ToSeed {
    param([byte[]]$Arm, [byte[]]$Seed, [string]$Label)
    if ($Arm.Length -ne $Seed.Length) {
        return [pscustomobject]@{ Label = $Label; SameLength = $false
            ArmLen = $Arm.Length; SeedLen = $Seed.Length; InSig = 0; OutSig = 0 }
    }
    $inSig = 0; $outSig = 0; $firstOut = -1
    for ($i = 0; $i -lt $Arm.Length; $i++) {
        if ($Arm[$i] -ne $Seed[$i]) {
            if ($i -ge $SIG_LO -and $i -le $SIG_HI) { $inSig++ }
            else { $outSig++; if ($firstOut -lt 0) { $firstOut = $i } }
        }
    }
    [pscustomobject]@{ Label = $Label; SameLength = $true; ArmLen = $Arm.Length
        SeedLen = $Seed.Length; InSig = $inSig; OutSig = $outSig; FirstOut = $firstOut }
}

$cx = Compare-ToSeed $codexBytes $seedBytes 'codex arm'
$cs = Compare-ToSeed $csBytes    $seedBytes 'c# arm'

foreach ($r in @($cx, $cs)) {
    if (-not $r.SameLength) {
        Write-Host ("  {0,-10} {1,12:N0} bytes vs seed {2,12:N0}  LENGTH MISMATCH" -f $r.Label, $r.ArmLen, $r.SeedLen) -ForegroundColor Red
    } else {
        Write-Host ("  {0,-10} {1,12:N0} bytes  differing: {2} in signature, {3} outside" -f $r.Label, $r.ArmLen, $r.InSig, $r.OutSig)
    }
}

# The control. If our reconstructed input does not reproduce the shipped seed
# through the compiler that BUILT it, the input is wrong and the C# arm's
# answer is about our reconstruction rather than about trust.
if (-not $cx.SameLength -or $cx.OutSig -gt 0) {
    Write-Host ""
    Write-Host "INCONCLUSIVE: the Codex arm did not reproduce the shipped seed from these" -ForegroundColor Yellow
    Write-Host "  bytes, so the input is not the input the seed was built from and the C#" -ForegroundColor Yellow
    Write-Host "  arm's result means nothing. Check -Mode (the seed is built -Repl) and" -ForegroundColor Yellow
    Write-Host "  that -Source is the concat the release actually used." -ForegroundColor Yellow
    Write-Host "  input: $inputFile"
    exit 2
}

Write-Host ""
if ($cs.SameLength -and $cs.OutSig -eq 0) {
    Write-Host "WITNESS HOLDS: the C# arm reproduced the seed byte for byte outside the" -ForegroundColor Green
    Write-Host "  signature region ($($cs.InSig) differing bytes, all within $SIG_LO..$SIG_HI)." -ForegroundColor Green
    exit 0
}

Write-Host "WITNESS RED." -ForegroundColor Red
if (-not $cs.SameLength) {
    Write-Host "  The C# arm's output is a different SIZE to the seed." -ForegroundColor Red
} else {
    Write-Host "  $($cs.OutSig) byte(s) differ OUTSIDE the signature region, first at offset $($cs.FirstOut)." -ForegroundColor Red
}
Write-Host "  A red witness is not automatically a trojan: read OperatorsManual" -ForegroundColor Red
Write-Host "  'The witness has a negative control' before concluding anything, and" -ForegroundColor Red
Write-Host "  check the plug was built with the seed under audit."
Write-Host "  arms: $work"
exit 1
