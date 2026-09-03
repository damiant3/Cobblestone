# Grade the compile page's BYTES modules (pe, img, elf, sign): the plugs that take a
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
# elf HAS positive arms now, one per container mode. What was missing was never
# the plug: it was a producer for the code/data/functable payload it reads. The
# page is that producer -- it takes the x86-64 straight out of the CDX the way
# build/cdx-to-pe.ps1 does -- so the lens is no longer dark and the arms below
# grade both modes rather than only a refusal.
#
# What stage 5a still owes is the CODE, not the container: the user-mode ELF64
# is a correct file whose instructions do bare-metal I/O, so it faults at its
# first print on a hosted OS until the write/mmap arms exist.
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

# -- elf: kernel and user-mode positives, plus two refusals -------------------
#
# elf HAD no positive arm, because nothing emitted the code/data/functable
# payload it reads. The page emits one now: it takes the x86-64 straight out of
# the CDX the way build/cdx-to-pe.ps1 does -- text at header offsets 168/176,
# rodata at 184/192 -- and puts a mode byte in front. So the arms below build
# the same wire the page builds, and grade the ELF headers that come back.
#
# The two modes are graded on the fields that DISTINGUISH them rather than on
# "an ELF came back": class, machine and entry. A builder wired to the wrong
# mode still answers a valid ELF, so a magic-number check would pass for both.
$elfWasm = Get-Module 'elf'
if (-not $elfWasm) { $rows += [pscustomobject]@{ arm = 'elf'; verdict = 'ABSENT'; note = 'no module' } }
else {
    $textOff = [BitConverter]::ToInt64($cdx, 168); $textSz = [BitConverter]::ToInt64($cdx, 176)
    $roOff = [BitConverter]::ToInt64($cdx, 184);   $roSz = [BitConverter]::ToInt64($cdx, 192)
    $sane = $textOff -ge 0 -and $textSz -ge 0 -and ($textOff + $textSz) -le $cdx.Length -and
            $roOff -ge 0 -and $roSz -ge 0 -and ($roOff + $roSz) -le $cdx.Length
    if (-not $sane) {
        $rows += [pscustomobject]@{ arm = 'elf'; verdict = 'BAD'; note = "the CDX header's sections run past the file" }
    } else {
        $code = $cdx[$textOff..($textOff + $textSz - 1)]
        $data = if ($roSz -gt 0) { $cdx[$roOff..($roOff + $roSz - 1)] } else { @() }
        foreach ($m in @(0, 1)) {
            $payload = [byte[]](@([byte]$m) + (Le32 $textSz) + (Le32 $roSz) + (Le32 0) + $code + $data)
            $elf = Invoke-BytesModule $elfWasm $payload "elf-mode$m"
            $label = if ($m -eq 0) { 'elf kernel' } else { 'elf usermode' }
            if ($null -eq $elf -or $elf.Length -lt 64) {
                $rows += [pscustomobject]@{ arm = $label; verdict = 'TRAP'; note = 'no clean exit or too short for a header' }
                continue
            }
            $isElf = $elf[0] -eq 0x7F -and $elf[1] -eq 0x45 -and $elf[2] -eq 0x4C -and $elf[3] -eq 0x46
            if (-not $isElf) {
                $head = [Text.Encoding]::ASCII.GetString($elf, 0, [Math]::Min(80, $elf.Length))
                $rows += [pscustomobject]@{ arm = $label; verdict = 'BAD'; note = "not an ELF: $($head.Trim())" }
                continue
            }
            $class = $elf[4]; $machine = [BitConverter]::ToUInt16($elf, 18)
            if ($m -eq 0) {
                $entry = [BitConverter]::ToUInt32($elf, 24)
                # ELF32, EM_386, entry 32 bytes into the bare-metal load address.
                $want = ($class -eq 1 -and $machine -eq 3 -and $entry -eq (1048576 + 32))
                $note = "ELF32 machine=0x{0:X} entry=0x{1:X} {2} bytes" -f $machine, $entry, $elf.Length
            } else {
                $entry = [BitConverter]::ToUInt64($elf, 24)
                # ELF64, EM_X86_64, entry past the header and two phdrs at the
                # conventional Linux base: 0x400000 + 176 + 32.
                $want = ($class -eq 2 -and $machine -eq 0x3E -and $entry -eq (4194304 + 176 + 32))
                $note = "ELF64 machine=0x{0:X} entry=0x{1:X} {2} bytes" -f $machine, $entry, $elf.Length
            }
            $rows += [pscustomobject]@{ arm = $label; verdict = $(if ($want) { 'OK' } else { 'BAD' }); note = $note }
        }
    }
    $ref = Invoke-BytesModule $elfWasm ([byte[]]@(1, 2, 3)) 'elf-short'
    $refTxt = if ($ref) { [Text.Encoding]::UTF8.GetString($ref) } else { '' }
    if ($refTxt -match 'REFUSED') { $rows += [pscustomobject]@{ arm = 'elf refusal'; verdict = 'OK'; note = ($refTxt.Trim() -split "`n")[0] } }
    else { $rows += [pscustomobject]@{ arm = 'elf refusal'; verdict = 'BAD'; note = 'a 3-byte payload was not refused' } }
    # An unknown MODE is a different refusal from a short payload, and it is the
    # one a new container target would trip (L-ACCEPTED). Graded separately so
    # a fallback that silently built a bare-metal image could not pass as this.
    $badMode = [byte[]](@([byte]9) + (Le32 0) + (Le32 0) + (Le32 0))
    $ref2 = Invoke-BytesModule $elfWasm $badMode 'elf-badmode'
    $ref2Txt = if ($ref2) { [Text.Encoding]::UTF8.GetString($ref2) } else { '' }
    if ($ref2Txt -match 'REFUSED unknown mode') { $rows += [pscustomobject]@{ arm = 'elf mode refusal'; verdict = 'OK'; note = ($ref2Txt.Trim() -split "`n")[0] } }
    else { $rows += [pscustomobject]@{ arm = 'elf mode refusal'; verdict = 'BAD'; note = 'mode 9 was not refused by name' } }
}

# -- sign: public key, signed CDX, unknown-mode refusal -----------------------
# The identity proof (wasm signature == bare-metal signature for one key and
# hash, and the signed CDX passing test-self-verify.ps1 with a flipped byte
# failing it) was run by hand at the module's landing; this grades the SHAPE:
# 32 bytes back for mode 0, the CDX back at its own length with only the key
# and signature fields moved for mode 1, and the mode-0 key sitting in the
# mode-1 field so the two answers agree with each other.
$signWasm = Get-Module 'sign'
if (-not $signWasm) { $rows += [pscustomobject]@{ arm = 'sign'; verdict = 'ABSENT'; note = 'no module' } }
else {
    $seed = [byte[]](1..32 | ForEach-Object { [byte](($_ * 7 + 3) % 256) })
    $pub = Invoke-BytesModule $signWasm ([byte[]](@([byte]0) + $seed)) 'sign-pub'
    if ($null -eq $pub) { $rows += [pscustomobject]@{ arm = 'sign pubkey'; verdict = 'TRAP'; note = 'no clean exit' } }
    elseif ($pub.Length -eq 32) { $rows += [pscustomobject]@{ arm = 'sign pubkey'; verdict = 'OK'; note = '32 bytes' } }
    else { $rows += [pscustomobject]@{ arm = 'sign pubkey'; verdict = 'BAD'; note = "$($pub.Length) bytes, wanted 32" } }
    $signed = Invoke-BytesModule $signWasm ([byte[]](@([byte]1) + $seed + $cdx)) 'sign-cdx'
    if ($null -eq $signed) { $rows += [pscustomobject]@{ arm = 'sign cdx'; verdict = 'TRAP'; note = 'no clean exit' } }
    elseif ($signed.Length -ne $cdx.Length) { $rows += [pscustomobject]@{ arm = 'sign cdx'; verdict = 'BAD'; note = "length $($signed.Length), wanted $($cdx.Length)" } }
    else {
        $moved = 0
        for ($i = 0; $i -lt $cdx.Length; $i++) { if (($i -lt 40 -or $i -ge 136) -and $signed[$i] -ne $cdx[$i]) { $moved++ } }
        $keyAgrees = ($null -ne $pub) -and $pub.Length -eq 32 -and ((($signed[40..71] | ForEach-Object { "$_" }) -join ',') -eq (($pub | ForEach-Object { "$_" }) -join ','))
        if ($moved -eq 0 -and $keyAgrees) { $rows += [pscustomobject]@{ arm = 'sign cdx'; verdict = 'OK'; note = 'only bytes 40..135 moved; key field equals the mode-0 key' } }
        else { $rows += [pscustomobject]@{ arm = 'sign cdx'; verdict = 'BAD'; note = "$moved byte(s) moved outside 40..135; key field agrees: $keyAgrees" } }
    }
    $ref = Invoke-BytesModule $signWasm ([byte[]](@([byte]7) + $seed)) 'sign-badmode'
    $refTxt = if ($ref) { [Text.Encoding]::UTF8.GetString($ref) } else { '' }
    if ($refTxt -match 'REFUSED unknown mode') { $rows += [pscustomobject]@{ arm = 'sign refusal'; verdict = 'OK'; note = ($refTxt.Trim() -split "`n")[0] } }
    else { $rows += [pscustomobject]@{ arm = 'sign refusal'; verdict = 'BAD'; note = 'mode 7 was not refused by name' } }
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
