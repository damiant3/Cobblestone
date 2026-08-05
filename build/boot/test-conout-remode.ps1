# The stub asks the firmware for its geometry AFTER it clears the screen.
#
# On AMI Aptio V the first real ConOut use activates the GraphicsConsole, and
# that activation sets its own graphics mode. Until 2026-08-02 build/cdx-to-pe.ps1
# read GOP Mode->Info and called ClearScreen ~200 bytes later, so every image it
# built published the splash mode's geometry (1920x1080, stride 2048) for a
# scanout the firmware had since switched to 1024 pixels per row. Every row the
# payload wrote then spanned two scanlines: glyphs stretched, alternate lines
# black, long-line tails overpainting the next row.
#
# That was found, and cured by swapping the two calls -- clear first, ask after.
# The cure has had no runner since. Nothing in the tree boots the bed that
# expresses it, so re-ordering those two blocks in cdx-to-pe.ps1, or writing a
# payload that reads the geometry before its own first ConOut call, puts the
# corruption back with every gate green. This is that runner.
#
# It is not in build/build.ps1: it compiles a payload and boots two VMs.
#
#   pwsh build/boot/test-conout-remode.ps1
#   pwsh build/boot/test-conout-remode.ps1 -Kernel seed/Codex.cdx -KeepArtifacts
[CmdletBinding()]
param(
    [string]$Kernel = '',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo

$vm = Join-Path $Repo 'tools\codex-vm.exe'
if (-not (Test-Path -PathType Leaf $vm)) { Write-Host "FAIL: $vm missing"; exit 1 }

$work = Join-Path $Repo 'build-output\conout-remode'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

$fail = 0
function Check ([string]$what, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $what" }
    else { Write-Host "  FAIL  $what"; if ($detail) { Write-Host "        $detail" }; $script:fail++ }
}

# ------------------------------------------------------------------ the image
# GeoTruth prints the published geometry and paints nothing. No seed, no font
# and no source on the ESP: this payload reads none of them, and each one is
# megabytes of image build for nothing.
Write-Host '[conout-remode] building the GeoTruth image...'
$img = Join-Path $work 'geotruth.img'
$buildArgs = @('-Src', 'build/boot/diag/GeoTruth.codex', '-Out', $img,
               '-Seed', '', '-Font', '', '-Source', '')
if ($Kernel -ne '') { $buildArgs += @('-Kernel', $Kernel) }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-option-a.ps1') @buildArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $img)) {
    Write-Host 'FAIL: could not build the GeoTruth image'
    exit 1
}

# ------------------------------------------------------------------- the boots
# The panel the ASUS reported: 1920 visible on a 2048-pixel scanline. The
# re-mode arm switches that to 1024x768/1024 at the stub's ClearScreen, which is
# the moment the two orderings diverge.
$BudgetMs = 45000

function Invoke-Arm ([string]$tag, [bool]$remode) {
    $o = Join-Path $work "$tag.out"; $e = Join-Path $work "$tag.err"
    $a = @('-uefi', '-kernel', $img, '-headless', '-output', $o, '-mem', '3072',
           '-gop', '-gop-width', '1920', '-gop-height', '1080', '-gop-stride', '2048')
    if ($remode) { $a += '-uefi-conout-remode' }
    $proc = Start-Process -FilePath $vm -ArgumentList $a -PassThru -WindowStyle Hidden -RedirectStandardError $e
    if (-not $proc.WaitForExit($BudgetMs)) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch { }
        Start-Sleep -Milliseconds 400
    }
    $text = ''
    if (Test-Path $o) {
        $fs = [System.IO.File]::Open($o, 'Open', 'Read', 'ReadWrite')
        try {
            $n = [int][Math]::Min(65536L, $fs.Length)
            $buf = New-Object byte[] $n
            [void]$fs.Read($buf, 0, $n)
            $text = ([System.Text.Encoding]::ASCII.GetString($buf)) -replace '[^\x20-\x7E\r\n]', ''
        } finally { $fs.Dispose() }
    }
    $errText = if (Test-Path $e) { (Get-Content $e -Raw -ErrorAction SilentlyContinue) } else { '' }
    $geo = $null
    if ($text -match 'GEOTRUTH w=(\d+) h=(\d+) stride=(\d+)') {
        $geo = @{ W = [int]$Matches[1]; H = [int]$Matches[2]; S = [int]$Matches[3] }
    }
    return @{ Geo = $geo; Out = $text; Err = $errText }
}

Write-Host '[conout-remode] arm 1: the panel as presented, no re-mode...'
$plain = Invoke-Arm 'plain' $false
Write-Host '[conout-remode] arm 2: the same image, ClearScreen re-modes the console...'
$remode = Invoke-Arm 'remode' $true

function Fmt ($g) { if ($g) { "$($g.W)x$($g.H) stride $($g.S)" } else { '(no GEOTRUTH line)' } }
Write-Host "  no re-mode: $(Fmt $plain.Geo)"
Write-Host "  re-moded  : $(Fmt $remode.Geo)"
Write-Host ''

# The control, and it is what makes the other two mean anything. Without a
# re-mode the payload must report the padded panel it was actually given. If
# this reads anything else the bed is not presenting 1920x1080/2048 at all, and
# the re-mode arm below is answering a question nobody asked.
Check 'without a re-mode the payload reads the panel it was given (1920x1080/2048)' `
    ($null -ne $plain.Geo -and $plain.Geo.W -eq 1920 -and $plain.Geo.H -eq 1080 -and $plain.Geo.S -eq 2048) `
    "read $(Fmt $plain.Geo); stderr: $($plain.Err)"

# The bed fired. A silent flag and a working fix are the same green otherwise.
Check 'the re-mode arm actually re-moded' `
    ($remode.Err -match 'ClearScreen re-moded GOP to 1024x768') $remode.Err

# The subject. The stub clears the screen and THEN reads Mode->Info, so what it
# publishes is the console's mode and not the splash mode. Reading 1920x1080/2048
# here is the 2026-08-02 corruption exactly: the stub asked before it cleared.
Check 'after a re-mode the payload reads the LIVE console mode (1024x768/1024)' `
    ($null -ne $remode.Geo -and $remode.Geo.W -eq 1024 -and $remode.Geo.H -eq 768 -and $remode.Geo.S -eq 1024) `
    "read $(Fmt $remode.Geo) -- the stub published the splash mode for a scanout that had changed"

# Belt and braces on the pair rather than on either number: a payload that
# printed a constant would satisfy exactly one of the two arms above and this
# says which shape the failure had.
Check 'the two arms disagree' `
    ($null -ne $plain.Geo -and $null -ne $remode.Geo -and $plain.Geo.S -ne $remode.Geo.S) `
    'both arms read the same stride, so nothing here is measuring the re-mode'

Write-Host ''
if ($fail -gt 0) {
    Write-Host "test-conout-remode: $fail assertion(s) FAILED"
    Write-Host "artifacts kept in $work"
    exit 1
}
if (-not $KeepArtifacts) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
Write-Host 'test-conout-remode: PASS'
exit 0
