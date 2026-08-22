# Build the diagnostic stick image: build/boot/diag/Diag.codex compiled by the
# depot seed, wrapped by cdx-to-pe.ps1 (-ExitBootServices), and laid on a
# GPT/FAT16 image whose ESP carries DIAG.ID -- the lock the payload requires
# before it writes DIAG.TXT (docs/Designs/Active/OS/DiagnosticStick.md).
#
#   build/boot/build-diag.ps1                       # -> build/boot/diag.img
#   build/boot/build-diag.ps1 -StdinCfg "scene off" # a ladder selection baked
#                                                    # into the stub's serial ring
#   build/boot/build-diag.ps1 -Cfg my.cfg           # a DIAG.CFG on the ESP
#
# The id is the SHA-256 prefix of the compiled payload. It travels twice: into
# the stub's serial ring (-Stdin, where the payload reads it as `id <hex>`) and
# onto the ESP as DIAG.ID. A stick whose DIAG.ID does not match the payload
# that booted is refused, so a stale stick from an older image cannot be
# written to by mistake, and the image needs no seed on it at all.
#
# Beside the image, build-output/diag.efi and diag.cdx are kept (the arms
# rebuild variants from the .efi) and build-output/diag-recipe.txt names the
# bytes: id, kernel digest, flags, image hash. That is the DIAG.RCP step 4 of
# the design will move onto the ESP itself.
[CmdletBinding()]
param(
    [string]$Out = 'build/boot/diag.img',
    # The compiler that builds the payload. Defaults to the depot seed because
    # an image that goes near a stick must have provenance; pass another only
    # for a dev loop.
    [string]$Kernel = 'seed/Codex.cdx',
    [int]$AllocPages = 32768,
    [int]$TotalSectors = 32768,
    # Extra DIAG.CFG lines baked into the stub's serial ring, newline separated
    # (`scene off`). The ring is 120 bytes and the id and kernel lines take 42.
    [string]$StdinCfg = '',
    # A DIAG.CFG file written to the ESP root. Read by the payload after the
    # bank opens, so it can only select stages that run after the bank.
    # EMPTY MEANS build/boot/diag-default.cfg, which names every non-passive
    # stage. It is not "no config": see the refusal below.
    [string]$Cfg = '',
    # Build anyway with one or more non-passive stages unnamed. For a genuine
    # one-off only. The shipping image uses the checked-in default cfg, never
    # this: an override the routine path takes stops being a signal, which is
    # how `flash-usb.ps1 -Rehearsed` came to certify nothing for a week.
    [switch]$AllowUnnamedStages
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..' '..')).Path
$bo   = Join-Path $repo 'build-output'
if (-not (Test-Path $bo)) { New-Item -ItemType Directory -Force $bo | Out-Null }

# AN ABSENT CFG KEY ARMS THE STAGE, so a cfg that names nothing arms everything.
#
# `dg-stage-enabled` is `diag-cfg-value c name /= "off"`, and a key that is not
# there reads as the empty string, which is not "off". Sitting 7 was composed by
# someone who believed the opposite, and the flight ran with every risky stage
# live. The build refuses that now: a non-passive stage must be NAMED, on or
# off, so arming one is a thing a composer typed rather than a thing that
# happened to them.
#
# The refusal runs BEFORE the compile. A build that is going to be refused
# should not cost two minutes first.
#
# The stage list is READ FROM Diag.codex rather than repeated here. A list
# duplicated in PowerShell drifts the first time a stage is added, and it
# drifts in the one direction that matters: a new risky stage nobody has to
# name.
$defaultCfg = Join-Path $repo 'build\boot\diag-default.cfg'
if (-not $Cfg) {
    if (-not (Test-Path -PathType Leaf $defaultCfg)) { throw "build/boot/diag-default.cfg is missing" }
    $Cfg = $defaultCfg
}
$cfgPre = $StdinCfg
if ($Cfg -and (Test-Path -PathType Leaf $Cfg)) { $cfgPre += "`n" + (Get-Content $Cfg -Raw) }

# A KEY NAMED TWICE IS A LINE THE BOX WILL NEVER READ.
#
# `diag-cfg-find` (DiagStage.codex) walks the config list and RETURNS ON ITS
# FIRST MATCH, and the list is the stub ring's lines followed by the ESP file's
# (`dg-ctx-with-cfg`), so a later line is dropped in silence and the ring beats
# the file for any key in both.
#
# That cost a composition report on 2026-08-21 (red): a config carrying `b3 on`
# and then `b3 peer=...` printed `b3 asked` here and answered no-peer on the
# box. This script matched ANY line, the runtime matched the FIRST, and the two
# were the same question asked twice in two places. It is the DARK word again:
# a second implementation that agrees until the day it does not.
#
# Both halves are fixed. The report below now asks the runtime's own question
# instead of a similar one; this refusal removes the ambiguity the two could
# disagree about at all. There is no override, because unlike an unnamed stage
# a duplicated key has no legitimate use: one stage's options go on one line.
$cfgEff = [ordered]@{}
$cfgDupes = @()
foreach ($cfgLine in ($cfgPre -split "`r?`n")) {
    $t = $cfgLine.Trim()
    if ($t.Length -eq 0) { continue }
    $sp = $t.IndexOf(' ')
    if ($sp -lt 0) { $k = $t; $v = 'on' } else { $k = $t.Substring(0, $sp); $v = $t.Substring($sp + 1).Trim() }
    if ($cfgEff.Contains($k)) { $cfgDupes += $k } else { $cfgEff[$k] = $v }
}
if ($cfgDupes.Count -gt 0) {
    Write-Host ''
    Write-Host "[diag] $($cfgDupes.Count) config key(s) named more than once:"
    foreach ($d in @($cfgDupes | Select-Object -Unique)) {
        Write-Host ("         {0}  kept `"{1}`", dropped every later line" -f $d, $cfgEff[$d])
    }
    Write-Host '  The box reads the FIRST line for a key and drops the rest, so a second'
    Write-Host '  line is not merged and not applied. Put one stage on one line. The'
    Write-Host '  stub ring is read before the ESP file, so a key in both is this too.'
    throw "refusing to build with $($cfgDupes.Count) duplicated config key(s)"
}

$ladderSrc = Get-Content (Join-Path $repo 'build\boot\diag\Diag.codex') -Raw
$stageNames = @{}
$nm = [regex]::Match($ladderSrc, 'dg-stage-name \(i\) =(.*?)\r?\n\r?\n', 'Singleline')
if (-not $nm.Success) { throw "cannot read dg-stage-name from Diag.codex" }
foreach ($mm in [regex]::Matches($nm.Groups[1].Value, 'i == (\d+) then "([a-z0-9-]+)"')) { $stageNames[[int]$mm.Groups[1].Value] = $mm.Groups[2].Value }
$riskM = [regex]::Match($ladderSrc, 'dg-stage-risk \(i\) =(.*?)\r?\n', 'Singleline')
if (-not $riskM.Success) { throw "cannot read dg-stage-risk from Diag.codex" }
$riskSrc = $riskM.Groups[1].Value
$stageCount = [int]([regex]::Match($ladderSrc, 'dg-stage-count : Integer = (\d+)').Groups[1].Value)
if ($stageCount -lt 1 -or $stageNames.Count -lt $stageCount) { throw "Diag.codex stage table did not parse: count=$stageCount names=$($stageNames.Count)" }

# `dg-stage-risk` is one nested if. Evaluating it here would be a second
# implementation of it; asking which stages it calls passive is not.
$passiveExplicit = @()
foreach ($mm in [regex]::Matches($riskSrc, 'i == (\d+) then diag-risk-passive')) { $passiveExplicit += [int]$mm.Groups[1].Value }
$writesFrom = [regex]::Match($riskSrc, 'i >= (\d+) then diag-risk-writes')
if (-not $writesFrom.Success) { throw "cannot read the writes threshold from dg-stage-risk" }
$firstRisky = [int]$writesFrom.Groups[1].Value

$mustName = @()
for ($i = 1; $i -le $stageCount; $i++) {
    if ($i -lt $firstRisky) { continue }
    if ($passiveExplicit -contains $i) { continue }
    $mustName += $stageNames[$i]
}
if ($mustName.Count -eq 0) { throw "no non-passive stages parsed from dg-stage-risk: the check would pass vacuously" }
$unnamed = @()
foreach ($s in $mustName) { if ($cfgPre -notmatch "(?m)^\s*$s\s+\S") { $unnamed += $s } }

if ($unnamed.Count -gt 0) {
    Write-Host ''
    Write-Host "[diag] $($unnamed.Count) of $($mustName.Count) non-passive stage(s) are not named in the config:"
    foreach ($s in $unnamed) { Write-Host "         $s" }
    if (-not $AllowUnnamedStages) {
        Write-Host '  An unnamed stage is ARMED, not off. Name each one on or off, or drop'
        Write-Host '  -Cfg to take build/boot/diag-default.cfg, which names all of them.'
        throw "refusing to build with $($unnamed.Count) unnamed non-passive stage(s)"
    }
    Write-Host '  -AllowUnnamedStages: building anyway. Every stage listed above is ARMED'
    Write-Host '  and no one named it. Do not fly this image on a sitting.'
}
Write-Host "[diag] cfg names all $($mustName.Count) non-passive stages: $(Split-Path $Cfg -Leaf)"

$src     = Join-Path $repo 'build/boot/diag/Diag.codex'
$bundled = Join-Path $bo 'diag-bundled.codex'
$cdxOut  = Join-Path $bo 'diag.cdx'
$log     = Join-Path $bo 'diag-compile.log'
$peOut   = Join-Path $bo 'diag.efi'
$idFile  = Join-Path $bo 'DIAG.ID'
$recipe  = Join-Path $bo 'diag-recipe.txt'

# Every state word a stage can answer must have a verdict row before the image
# is built: a stranger reads the row, and a missing one is a boot spent.
& pwsh -NoProfile -File (Join-Path $repo 'build/check-diag-verdicts.ps1')
if ($LASTEXITCODE -ne 0) { throw "check-diag-verdicts failed; add the verdict rows before building" }

Write-Host "[diag] bundling $src"
& pwsh -NoProfile -File (Join-Path $repo 'build/bundle-app.ps1') -Src $src -Out $bundled
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $bundled)) { throw "bundle failed" }

if (-not (Test-Path $Kernel)) { throw "-Kernel not found: $Kernel" }
$kernelAbs = (Resolve-Path $Kernel).Path
Write-Host "[diag] compiling with $kernelAbs"
$compileOut = & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $bundled -Out $cdxOut -Log $log -Pet -Kernel $kernelAbs 2>&1
$compileOut | ForEach-Object { Write-Host "  $_" }
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cdxOut)) {
    Get-Content $log -ErrorAction SilentlyContinue | Select-String 'error CDX' | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "CDX compile failed"
}
# compile.ps1 prints `kernel: <path> [<digest>]`; the digest is what the payload
# reports on its identity row, so it is read from the same line rather than
# recomputed here by a second method that could disagree.
$kline = ($compileOut | Where-Object { "$_" -match '^kernel: .*\[([0-9A-Fa-f]+)\]' } | Select-Object -First 1)
$kernelDigest = if ($kline -and ("$kline" -match '\[([0-9A-Fa-f]+)\]')) { $Matches[1] } else { 'unknown' }

$id = (Get-FileHash $cdxOut -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
$stdin = "id $id`nkernel $kernelDigest`n"
if ($StdinCfg) { $stdin += ($StdinCfg -replace '\r', '') + "`n" }
if ([Text.Encoding]::ASCII.GetByteCount($stdin) -gt 120) { throw "-StdinCfg too long: the ring holds 120 bytes and the id and kernel lines take $([Text.Encoding]::ASCII.GetByteCount("id $id`nkernel $kernelDigest`n"))" }

Write-Host "[diag] id=$id kernel=$kernelDigest"
& pwsh -NoProfile -File (Join-Path $repo 'build/cdx-to-pe.ps1') -CdxInput $cdxOut -Out $peOut -HeapPages $AllocPages -ExitBootServices -Stdin $stdin
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $peOut)) { throw "PE conversion failed" }

[IO.File]::WriteAllText($idFile, $id, [Text.ASCIIEncoding]::new())
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $repo $Out }

# DIAG.RCP: the recipe INSIDE the image (design gap 6). Everything that names
# the bytes except the image hash, which cannot be inside the bytes it hashes;
# diag-recipe.txt beside the image adds that line. The payload banks these
# lines as `rcp ...` right after the bank row, so a DIAG.TXT names what built it.
# THE STAMP IS THE LAST SUBMITTED CL, AND THE IMAGE IS BUILT FROM DISK. Those
# are two different questions and the stamp used to answer the first while
# claiming the second. Measured 2026-08-19: an image rebuilt mid-merge stamped
# `diag-src-cl=17355` while its bytes carried DiagSink from 17362, and the two
# images differed in exactly those two digits and nowhere else -- which is also
# the answer to whether this script is reproducible. It is; the stamp is not.
# A stamp that silently UNDERSTATES is worse than no stamp, so an open or
# unsubmitted diag source makes it say so rather than name a CL it is not.
$srcCl = 'unknown'
try {
    $streamLine = (& p4 -Ztag info 2>$null | Select-String '^\.\.\. clientStream (.*)$' | Select-Object -First 1)
    if ($streamLine) {
        $stream = $streamLine.Matches[0].Groups[1].Value
        $chg = (& p4 changes -m 1 "$stream/build/boot/diag/..." 2>$null | Select-Object -First 1)
        if ($chg -match '^Change (\d+)') { $srcCl = $Matches[1] }
        $open = @(& p4 opened "$stream/build/boot/diag/..." 2>$null | Where-Object { $_ -notmatch 'not opened' })
        if ($open.Count -gt 0) { $srcCl = "$srcCl+$($open.Count)open" }
    }
} catch { }
$rcpFile = Join-Path $bo 'DIAG.RCP'
$rcpLines = @(
    "id=$id",
    "kernel=$kernelDigest",
    "payload-sha256=$((Get-FileHash $cdxOut -Algorithm SHA256).Hash)",
    "efi-sha256=$((Get-FileHash $peOut -Algorithm SHA256).Hash)",
    "alloc-pages=$AllocPages",
    "total-sectors=$TotalSectors",
    "stdin=$($stdin -replace "`n", '|')",
    "cfg=$Cfg",
    "diag-src-cl=$srcCl"
)
[IO.File]::WriteAllText($rcpFile, ($rcpLines -join "`n") + "`n", [Text.ASCIIEncoding]::new())
$extra = @("DIAG.ID=$idFile", "DIAG.RCP=$rcpFile")
if ($Cfg) {
    if (-not (Test-Path -PathType Leaf $Cfg)) { throw "-Cfg not found: $Cfg" }
    $extra += "DIAG.CFG=$((Resolve-Path $Cfg).Path)"
}
& pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') -PeInput $peOut -Out $outAbs -TotalSectors $TotalSectors -Extra ($extra -join ';')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $outAbs)) { throw "build-img failed" }

# WHICH INSTRUMENTS ABOARD ARE ACTUALLY SWITCHED ON.
#
# A stage that reads its own configuration is only as good as the config the
# COMPOSER ships, and until 2026-08-20 nothing said which ones were dark. The
# fifth grouped sitting flew with reek's sink rung ladder and blu's B3 stage
# both in the image and both idle, because red built the stick with no
# DIAG.CFG at all: b3 answered "no-peer, DIAG.CFG names no peer" and the sink
# row returned the same single bit it had returned on the four sittings
# before it. Both stages behaved correctly. Nobody had asked them anything.
#
# This does not refuse -- a stick may legitimately leave b3 unasked -- it makes
# the omission LOUD at the moment of composition, which is the only moment
# anyone can still fix it. Damian is not the right person to discover it at
# the box.
#
# IT SAID "DARK" UNTIL 2026-08-21 AND THAT WORD COST A SITTING. red read
# `sink DARK` as sink being off; sink was never off, it ran the 2.7 MB write
# exactly as it always had, and the banner underneath called it "aboard and
# idle", which was false of that stage in the only way that mattered. The
# column says what the stage WILL DO now. A word for a state is worth less
# than a sentence about behaviour whenever the two can disagree.
#
# IT ASKS THE RUNTIME'S QUESTION, not one that resembles it. This block used to
# match ANY line of the composed config, so `b3 on` followed by `b3 peer=...`
# read as asked here and as no-peer on the box, which is what red hit on
# 2026-08-21. `$cfgEff` above is first-wins over ring-then-file, which is what
# `diag-cfg-find` does, and a duplicate key cannot reach here now anyway.
$gated = @(
    @{ Stage = 'b3';   Needs = 'b3 peer=<ip>:<port>'; On = ($cfgEff.Contains('b3') -and $cfgEff['b3'] -match 'peer=')
       Off = 'answers no-peer and dials nothing' },
    @{ Stage = 'sink'; Needs = 'sink ladder=1';       On = ($cfgEff.Contains('sink') -and $cfgEff['sink'] -match 'ladder=1')
       Off = 'STILL WRITES 2.7 MB, as one write, and returns one bit instead of a threshold' }
)
Write-Host ''
Write-Host '[diag] what the config-gated instruments will DO on this image:'
foreach ($g in $gated) {
    if ($g.On) { Write-Host ("  {0,-6} asked    ({1})" -f $g.Stage, $g.Needs) }
    else       { Write-Host ("  {0,-6} NOT ASKED -- {1}" -f $g.Stage, $g.Off) }
}
if (@($gated | Where-Object { -not $_.On }).Count -gt 0) {
    Write-Host '  A stage above is aboard with its question unasked. That is not the same as'
    Write-Host '  being off, and for sink it is not idle either. Pass -Cfg or -StdinCfg.'
}
Write-Host ''
$imgHash = (Get-FileHash $outAbs -Algorithm SHA256).Hash
$lines = @("image=$outAbs", "image-sha256=$imgHash", "kernel-path=$kernelAbs", "built=$([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))") + $rcpLines
[IO.File]::WriteAllText($recipe, ($lines -join "`r`n") + "`r`n", [Text.ASCIIEncoding]::new())
Write-Host "Done: $outAbs"
Write-Host "  sha256 $imgHash"
Write-Host "  recipe $recipe"
Write-Host "  arms:  build/boot/diag-arm.ps1"
