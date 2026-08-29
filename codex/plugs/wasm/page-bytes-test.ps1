# Grade the compile page's BYTES modules (pe, img, elf): the plugs that take a
# compiled payload rather than IR text, which page-lens-test.ps1 therefore
# cannot grade. One real payload arm per branch that has a live producer, and
# a refusal arm per module, because a plug that answers bytes on garbage is a
# download button handing somebody a wrong binary.
#
# This is the SHAPE check, not the identity proof. The byte-identity of these
# modules against their bare-metal network twins was proven with hashes in
# plugs-backlog 1.92; re-proving identity costs a codex-vm boot per arm and
# belongs to a change that touches an emitter, not to every run of this.
#
# elf has no positive arm: nothing in the tree emits the code/data/functable
# payload it reads (the dark-lens reason, plugs 1.92 "What is left"), so until
# the hosted-runtime payload mode exists (PrismDevEnvironment.md stage 5a) its
# arms are the refusal and the module building at all.
[CmdletBinding()]
param(
    [string]$Kernel,
    [string]$Subject
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
$byPlug = @{}
foreach ($m in ($PageModules | Where-Object { $_.transport -eq 'bytes' })) { $byPlug[$m.plug] = $m }
foreach ($need in @('pe', 'img', 'elf')) {
    if (-not $byPlug.ContainsKey($need)) { Write-Host "REFUSE: the manifest carries no bytes row for '$need'."; exit 2 }
}

$work = Join-Path $PSScriptRoot 'build-output\bytes-test'
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Get-Module([string]$plug) {
    $m = $byPlug[$plug]
    $wasm = Join-Path $Repo ("codex\plugs\{0}\build-output\{1}" -f $plug, $m.file)
    if (-not (Test-Path -PathType Leaf $wasm)) { return $null }
    return $wasm
}

function Invoke-BytesModule([string]$wasm, [byte[]]$payload, [string]$tag) {
    $in  = Join-Path $work "$tag.in"
    $out = Join-Path $work "$tag.out"
    $err = Join-Path $work "$tag.err"
    [IO.File]::WriteAllBytes($in, $payload)
    $p = Start-Process -FilePath 'wasmtime' `
         -ArgumentList @('-W', 'max-wasm-stack=16777216', $wasm) -NoNewWindow -PassThru `
         -RedirectStandardInput $in -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $p.WaitForExit(300000)) { try { $p.Kill() } catch {}; return $null }
    if ($p.ExitCode -ne 0) { return $null }
    return [IO.File]::ReadAllBytes($out)
}

# The one compiled payload every positive arm shares: a small program through
# the seed, host-side, exactly how the page's Binary tab gets its CDX.
$srcFile = Join-Path $work 'subject.codex'
if ($Subject) { Copy-Item $Subject $srcFile -Force }
else {
    [IO.File]::WriteAllText($srcFile, "Chapter: BytesProbe`n  cites Foreword chapter Console`n`n We say:`n`nSection: Body`n`n  opening : [Console] Nothing = act`n    print-line-uni ""bytes-probe""`n  end`n", [Text.UTF8Encoding]::new($false))
}
$cdxFile = Join-Path $work 'subject.cdx'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $srcFile -Out $cdxFile -Log (Join-Path $work 'subject.log') -Kernel $Kernel | Out-Null
if (-not (Test-Path -PathType Leaf $cdxFile) -or (Get-Item $cdxFile).Length -eq 0) {
    Write-Host "REFUSE: the subject did not compile to a CDX; see $work\subject.log"; exit 2
}
$cdx = [IO.File]::ReadAllBytes($cdxFile)

function Le32([int]$n) { return @([byte]($n -band 255), [byte](($n -shr 8) -band 255), [byte](($n -shr 16) -band 255), [byte](($n -shr 24) -band 255)) }

$rows = @()

# -- pe: mode 0 (UEFI kernel) positive, unknown-mode refusal ------------------
$peWasm = Get-Module 'pe'
if (-not $peWasm) { $rows += [pscustomobject]@{ arm = 'pe'; verdict = 'ABSENT'; note = 'no module' } }
else {
    $pePayload = [byte[]](@([byte]0) + $cdx)
    $pe = Invoke-BytesModule $peWasm $pePayload 'pe-mode0'
    if ($null -eq $pe) { $rows += [pscustomobject]@{ arm = 'pe mode 0'; verdict = 'TRAP'; note = 'no clean exit' } }
    elseif ($pe.Length -gt 2 -and $pe[0] -eq 0x4D -and $pe[1] -eq 0x5A) {
        $rows += [pscustomobject]@{ arm = 'pe mode 0'; verdict = 'OK'; note = "MZ, $($pe.Length) bytes" }
        [IO.File]::WriteAllBytes((Join-Path $work 'BOOTX64.EFI'), $pe)
    } else {
        $txt = [Text.Encoding]::UTF8.GetString($pe, 0, [Math]::Min(80, $pe.Length))
        $rows += [pscustomobject]@{ arm = 'pe mode 0'; verdict = 'BAD'; note = "not MZ: $txt" }
    }
    $ref = Invoke-BytesModule $peWasm ([byte[]]@(9, 0, 0)) 'pe-badmode'
    $refTxt = if ($ref) { [Text.Encoding]::UTF8.GetString($ref) } else { '' }
    if ($refTxt -match 'REFUSED') { $rows += [pscustomobject]@{ arm = 'pe refusal'; verdict = 'OK'; note = ($refTxt.Trim() -split "`n")[0] } }
    else { $rows += [pscustomobject]@{ arm = 'pe refusal'; verdict = 'BAD'; note = 'unknown mode 9 was not refused' } }
}

# -- img: FAT16 positive (PE + CDX), short-header refusal ---------------------
$imgWasm = Get-Module 'img'
if (-not $imgWasm) { $rows += [pscustomobject]@{ arm = 'img'; verdict = 'ABSENT'; note = 'no module' } }
else {
    $peBytes = if (Test-Path (Join-Path $work 'BOOTX64.EFI')) { [IO.File]::ReadAllBytes((Join-Path $work 'BOOTX64.EFI')) } else { $null }
    if ($null -eq $peBytes) { $rows += [pscustomobject]@{ arm = 'img fat16'; verdict = 'SKIP'; note = 'no PE from the pe arm to build with' } }
    else {
        $sectors = 16384
        $imgPayload = [byte[]](@([byte]0) + (Le32 $sectors) + (Le32 $peBytes.Length) + (Le32 $cdx.Length) + (Le32 0) + $peBytes + $cdx)
        $img = Invoke-BytesModule $imgWasm $imgPayload 'img-fat16'
        if ($null -eq $img) { $rows += [pscustomobject]@{ arm = 'img fat16'; verdict = 'TRAP'; note = 'no clean exit' } }
        elseif ($img.Length -eq ($sectors * 512) -and [Text.Encoding]::ASCII.GetString($img, 512, 8) -eq 'EFI PART') {
            $rows += [pscustomobject]@{ arm = 'img fat16'; verdict = 'OK'; note = "GPT, $($img.Length) bytes" }
        } else {
            $rows += [pscustomobject]@{ arm = 'img fat16'; verdict = 'BAD'; note = "size $($img.Length), wanted $($sectors * 512) with EFI PART at LBA 1" }
        }
    }
    $ref = Invoke-BytesModule $imgWasm ([byte[]]@(0, 1, 2, 3, 4)) 'img-short'
    $refTxt = if ($ref) { [Text.Encoding]::UTF8.GetString($ref) } else { '' }
    if ($refTxt -match 'REFUSED') { $rows += [pscustomobject]@{ arm = 'img refusal'; verdict = 'OK'; note = ($refTxt.Trim() -split "`n")[0] } }
    else { $rows += [pscustomobject]@{ arm = 'img refusal'; verdict = 'BAD'; note = 'a 5-byte payload was not refused' } }
}

# -- elf: refusal only, until stage 5a gives it a producer --------------------
$elfWasm = Get-Module 'elf'
if (-not $elfWasm) { $rows += [pscustomobject]@{ arm = 'elf'; verdict = 'ABSENT'; note = 'no module' } }
else {
    $ref = Invoke-BytesModule $elfWasm ([byte[]]@(1, 2, 3)) 'elf-short'
    $refTxt = if ($ref) { [Text.Encoding]::UTF8.GetString($ref) } else { '' }
    if ($refTxt -match 'REFUSED') { $rows += [pscustomobject]@{ arm = 'elf refusal'; verdict = 'OK'; note = ($refTxt.Trim() -split "`n")[0] } }
    else { $rows += [pscustomobject]@{ arm = 'elf refusal'; verdict = 'BAD'; note = 'a 3-byte payload was not refused' } }
}

Write-Host ''
foreach ($r in $rows) { Write-Host ("  {0,-12} {1,-7} {2}" -f $r.arm, $r.verdict, $r.note) }
$bad = @($rows | Where-Object { $_.verdict -in @('BAD', 'TRAP') }).Count
$absent = @($rows | Where-Object { $_.verdict -eq 'ABSENT' }).Count
Write-Host ''
Write-Host ("[bytes] {0} arms, {1} failed, {2} absent" -f $rows.Count, $bad, $absent)
if ($bad -gt 0) { exit 1 }
if ($absent -gt 0) { exit 1 }
exit 0
