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
    [string]$Cfg = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..' '..')).Path
$bo   = Join-Path $repo 'build-output'
if (-not (Test-Path $bo)) { New-Item -ItemType Directory -Force $bo | Out-Null }

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

$imgHash = (Get-FileHash $outAbs -Algorithm SHA256).Hash
$lines = @("image=$outAbs", "image-sha256=$imgHash", "kernel-path=$kernelAbs", "built=$([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))") + $rcpLines
[IO.File]::WriteAllText($recipe, ($lines -join "`r`n") + "`r`n", [Text.ASCIIEncoding]::new())
Write-Host "Done: $outAbs"
Write-Host "  sha256 $imgHash"
Write-Host "  recipe $recipe"
Write-Host "  arms:  build/boot/diag-arm.ps1"
