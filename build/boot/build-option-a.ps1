# Build an Option A bootable image: [Option A stub][CDX .text][CDX .rodata]
# wrapped in a GPT/FAT16 disk image.
#
# The stub (option_a_stub.asm) is assembled by ml64; its machine code is
# extracted and its placeholder immediates patched with the compiled CDX's
# real sizes/offsets. Validation prototype for the Option A boot path -- the
# proven sequence gets ported into the self-hosted blessed builder later.
#
#   pwsh build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex
[CmdletBinding()]
param(
    [string]$Src = 'apps/works/GopBoot.codex',
    [string]$Out = 'build/boot/optiona.img',
    [int]$AllocPages = 32768,  # 128 MB: page tables + memmap + Codex heap + stack
    # Embed the CDX seed on the ESP as CODEX.CDX so the booted payload can read
    # it back with its own drivers. '' skips it (a menu-only proof image).
    [string]$Seed = 'seed/Codex.cdx',
    [int]$TotalSectors = 32768   # 16 MB: PE + a 2.1 MB seed with room to spare
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..' '..')).Path
$bo   = Join-Path $repo 'build-output'
if (-not (Test-Path $bo)) { New-Item -ItemType Directory -Force $bo | Out-Null }

# ---- 1. Bundle + compile the Codex source to CDX ----
$bundle  = Join-Path $repo 'build/bundle-app.ps1'
$compile = Join-Path $repo 'build/compile.ps1'
$bundled = Join-Path $bo 'optiona-bundled.codex'
$cdxOut  = Join-Path $bo 'optiona.cdx'
$log     = Join-Path $bo 'optiona-compile.log'
Write-Host "[opt-a] bundling $Src"
& pwsh -NoProfile -File $bundle -Src (Join-Path $repo $Src) -Out $bundled
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $bundled)) { throw "bundle failed" }
Write-Host "[opt-a] compiling -> CDX (pet: interactive poll loop, no heap progress)"
& pwsh -NoProfile -File $compile -Src $bundled -Out $cdxOut -Log $log -Pet
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cdxOut)) {
    Get-Content $log -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "CDX compile failed"
}

$cdx = [System.IO.File]::ReadAllBytes($cdxOut)
function R64($o) { [BitConverter]::ToInt64($cdx, $o) }
function R32($o) { [BitConverter]::ToInt32($cdx, $o) }
$textOff   = R64 168
$textSz    = R64 176
$rodataOff = R64 184
$rodataSz  = R64 192
$entryOff  = R64 200
$debugOff  = R32 220

# ---- find 'opening' offset in the debug map (CCE: o p e n i n g) ----
$openingCce = [byte[]]@(16, 31, 13, 18, 17, 18, 29)
$openingOff = -1
if ($debugOff -gt 0 -and $debugOff -lt $cdx.Length) {
    $mapCount = R32 ($debugOff + 4)
    $mapStringsOff = R32 ($debugOff + 8)
    $stringsBase = $debugOff + $mapStringsOff
    $entriesBase = $debugOff + 12
    $strPos = -1
    for ($i = $stringsBase; $i -lt $cdx.Length - $openingCce.Length; $i++) {
        $m = $true
        for ($j = 0; $j -lt $openingCce.Length -and $m; $j++) { if ($cdx[$i+$j] -ne $openingCce[$j]) { $m = $false } }
        if ($m -and ($i + $openingCce.Length -ge $cdx.Length -or $cdx[$i + $openingCce.Length] -eq 0)) { $strPos = $i - $stringsBase; break }
    }
    if ($strPos -ge 0) {
        for ($i = 0; $i -lt $mapCount; $i++) {
            $e = $entriesBase + $i * 12
            if ((R32 ($e + 8)) -eq $strPos) { $openingOff = R32 $e; break }
        }
    }
}
if ($openingOff -lt 0) { Write-Host "[opt-a] WARN: opening not found, using entry"; $openingOff = $entryOff }
Write-Host ("[opt-a] text={0} rodata={1} opening=0x{2:X}" -f $textSz, $rodataSz, $openingOff)

# ---- 2. Assemble + link the stub, extract its .text machine code ----
$asm = Join-Path $here 'option_a_stub.asm'
$obj = Join-Path $here 'option_a_stub.obj'
$efi = Join-Path $here 'option_a_stub.efi'
$vs  = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$vcvars = "$vs\VC\Auxiliary\Build\vcvars64.bat"
cmd /c "`"$vcvars`" >nul 2>&1 && ml64 /nologo /c /Fo`"$obj`" `"$asm`" && link /NOLOGO /SUBSYSTEM:EFI_APPLICATION /ENTRY:efi_main /NODEFAULTLIB /MACHINE:X64 /OUT:`"$efi`" `"$obj`""
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $efi)) { throw "assemble/link failed" }

$pe = [System.IO.File]::ReadAllBytes($efi)
$peOff = [BitConverter]::ToInt32($pe, 60)
$numSec = [BitConverter]::ToUInt16($pe, $peOff + 6)
$optSz  = [BitConverter]::ToUInt16($pe, $peOff + 20)
$secTab = $peOff + 24 + $optSz
$stub = $null
for ($s = 0; $s -lt $numSec; $s++) {
    $so = $secTab + $s * 40
    $name = [System.Text.Encoding]::ASCII.GetString($pe, $so, 8).TrimEnd([char]0)
    if ($name -eq '.text') {
        $vsize = [BitConverter]::ToInt32($pe, $so + 8)
        $praw  = [BitConverter]::ToInt32($pe, $so + 20)
        $stub = New-Object byte[] $vsize
        [Array]::Copy($pe, $praw, $stub, 0, $vsize)
    }
}
if (-not $stub) { throw ".text not found in stub PE" }
Write-Host "[opt-a] stub machine code: $($stub.Length) bytes"

# ---- 3. Patch placeholder magics (LE) with real values ----
$dataVaddr = 0x100000 + (($textSz + 7) -band -8)
function Patch-Magic([byte[]]$buf, [uint32]$magic, [uint32]$val, [string]$label) {
    $mb = [BitConverter]::GetBytes($magic)
    $vb = [BitConverter]::GetBytes($val)
    $hits = 0
    for ($i = 0; $i -le $buf.Length - 4; $i++) {
        if ($buf[$i] -eq $mb[0] -and $buf[$i+1] -eq $mb[1] -and $buf[$i+2] -eq $mb[2] -and $buf[$i+3] -eq $mb[3]) {
            [Array]::Copy($vb, 0, $buf, $i, 4); $hits++
        }
    }
    if ($hits -lt 1) { throw "magic $label (0x$($magic.ToString('X'))) not found" }
    Write-Host "[opt-a]   patched $label x$hits -> $val"
}
Patch-Magic $stub 0x7A000001 ([uint32]$AllocPages) 'ALLOC_PAGES'
Patch-Magic $stub 0x7A000002 ([uint32]$textSz)     'TEXTSIZE'
Patch-Magic $stub 0x7A000003 ([uint32]$rodataSz)   'RODATASIZE'
Patch-Magic $stub 0x7A000004 ([uint32]$dataVaddr)  'DATAVADDR'
Patch-Magic $stub 0x7A000005 ([uint32]$openingOff) 'OPENING_OFF'

# ---- 4. Build the final PE: [stub][cdx .text][cdx .rodata] ----
$textBytes = New-Object byte[] $textSz
[Array]::Copy($cdx, $textOff, $textBytes, 0, $textSz)
$rodataBytes = New-Object byte[] $rodataSz
if ($rodataSz -gt 0) { [Array]::Copy($cdx, $rodataOff, $rodataBytes, 0, $rodataSz) }

$code = $stub + $textBytes + $rodataBytes
$FileAlign = 512; $SectionAlign = 4096; $HeaderSize = 512
$codeSize = $code.Length
$codeRaw = (($codeSize + $FileAlign - 1) -band (-bnot ($FileAlign - 1)))
$codeSecEnd = (($codeSize + $SectionAlign - 1) -band (-bnot ($SectionAlign - 1)))
$relocRva = $SectionAlign + $codeSecEnd
$imageSize = (($relocRva + $SectionAlign + $SectionAlign - 1) -band (-bnot ($SectionAlign - 1)))
$relocRawOff = $HeaderSize + $codeRaw

$hms = [System.IO.MemoryStream]::new(); $hbw = [System.IO.BinaryWriter]::new($hms)
$hbw.Write([byte[]]@(0x4D,0x5A)); $hbw.Write((New-Object byte[] 58)); $hbw.Write([int]128); $hbw.Write((New-Object byte[] (128-64)))
$hbw.Write([byte[]]@(0x50,0x45,0,0))
$hbw.Write([ushort]0x8664); $hbw.Write([ushort]2); $hbw.Write([int]0); $hbw.Write([int]0); $hbw.Write([int]0); $hbw.Write([ushort]240); $hbw.Write([ushort]0x0022)
$hbw.Write([ushort]0x020B); $hbw.Write([byte]0); $hbw.Write([byte]0)
$hbw.Write([int]$codeRaw); $hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([int]$SectionAlign)   # AddressOfEntryPoint = efi_main (start of .text)
$hbw.Write([int]$SectionAlign)   # BaseOfCode
$hbw.Write([long]0)              # ImageBase
$hbw.Write([int]$SectionAlign); $hbw.Write([int]$FileAlign)
$hbw.Write([ushort]0); $hbw.Write([ushort]0); $hbw.Write([ushort]0); $hbw.Write([ushort]0); $hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([int]0)
$hbw.Write([int]$imageSize); $hbw.Write([int]$HeaderSize); $hbw.Write([int]0)
$hbw.Write([ushort]10); $hbw.Write([ushort]0x0160)
$hbw.Write([long]0); $hbw.Write([long]0); $hbw.Write([long]0); $hbw.Write([long]0)
$hbw.Write([int]0); $hbw.Write([int]16)
for ($i = 0; $i -lt 16; $i++) { if ($i -eq 5) { $hbw.Write([int]$relocRva); $hbw.Write([int]8) } else { $hbw.Write([long]0) } }
$hbw.Write([System.Text.Encoding]::ASCII.GetBytes(".text`0`0`0"))
$hbw.Write([int]$codeSize); $hbw.Write([int]$SectionAlign); $hbw.Write([int]$codeRaw); $hbw.Write([int]$HeaderSize)
$hbw.Write([int]0); $hbw.Write([int]0); $hbw.Write([ushort]0); $hbw.Write([ushort]0); $hbw.Write([int]0x60000020)
$hbw.Write([System.Text.Encoding]::ASCII.GetBytes(".reloc`0`0"))
$hbw.Write([int]8); $hbw.Write([int]$relocRva); $hbw.Write([int]$FileAlign); $hbw.Write([int]$relocRawOff)
$hbw.Write([int]0); $hbw.Write([int]0); $hbw.Write([ushort]0); $hbw.Write([ushort]0); $hbw.Write([int]0x42000040)
$hbw.Close(); $headers = $hms.ToArray()
$headerPad = New-Object byte[] ($HeaderSize - $headers.Length)
$relocBlock = New-Object byte[] $FileAlign
[BitConverter]::GetBytes([int]$relocRva).CopyTo($relocBlock, 0)
[BitConverter]::GetBytes([int]8).CopyTo($relocBlock, 4)

$peOutFile = Join-Path $bo 'optiona.efi'
$oms = [System.IO.MemoryStream]::new()
$oms.Write($headers, 0, $headers.Length); $oms.Write($headerPad, 0, $headerPad.Length)
$oms.Write($code, 0, $code.Length)
$codePad = $codeRaw - $codeSize; if ($codePad -gt 0) { $oms.Write((New-Object byte[] $codePad), 0, $codePad) }
$oms.Write($relocBlock, 0, $relocBlock.Length)
[System.IO.File]::WriteAllBytes($peOutFile, $oms.ToArray())
Write-Host "[opt-a] PE: $((Get-Item $peOutFile).Length) bytes -> $peOutFile"

# ---- 5. Wrap into GPT/FAT16 image ----
$outAbs = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $repo $Out }
$imgArgs = @('-PeInput', $peOutFile, '-Out', $outAbs, '-TotalSectors', $TotalSectors)
if ($Seed) {
    $seedAbs = if ([System.IO.Path]::IsPathRooted($Seed)) { $Seed } else { Join-Path $repo $Seed }
    if (Test-Path $seedAbs) { $imgArgs += @('-Seed', $seedAbs) }
    else { Write-Host "[opt-a] WARN: seed not found at $seedAbs; image will carry no CODEX.CDX" }
}
& pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') @imgArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $outAbs)) { throw "build-img failed" }
Write-Host "Done: $outAbs"
Write-Host "  Visual:  tools/codex-vm.exe -kernel $outAbs -uefi -gop -screenshot build/boot/optiona.bmp"
Write-Host "  Strict:  tools/codex-vm.exe -kernel $outAbs -uefi-strict -headless"
