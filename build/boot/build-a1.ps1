# Build the A1 "boot and paint" proof stub into a bootable GPT/FAT16 image.
#
# A1 validates the Option A boot mechanism (acquire GOP -> ExitBootServices ->
# own page tables -> paint framebuffer) with NO compiler involvement. The MASM
# is assembled+linked into a real PE32+ EFI application (MSVC produces correct
# .reloc relocations), then wrapped into a GPT/FAT16 image by build-img.ps1.
#
# Test:
#   tools/codex-vm.exe -kernel build/boot/a1.img -uefi -gop -screenshot build/boot/a1.bmp
#   tools/codex-vm.exe -kernel build/boot/a1.img -uefi-strict -headless   (must NOT fault)
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here   = $PSScriptRoot
$repo   = (Resolve-Path (Join-Path $here '..' '..')).Path
$asm    = Join-Path $here 'a1_boot_paint.asm'
$obj    = Join-Path $here 'a1_boot_paint.obj'
$efi    = Join-Path $here 'a1.efi'
$img    = Join-Path $here 'a1.img'

$vsPath = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$vcvars = "$vsPath\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { Write-Host "FAIL: vcvars64.bat not found"; exit 1 }

Write-Host "=== A1: assemble + link EFI stub ==="
cmd /c "`"$vcvars`" >nul 2>&1 && ml64 /nologo /c /Fo`"$obj`" `"$asm`" && link /NOLOGO /SUBSYSTEM:EFI_APPLICATION /ENTRY:efi_main /NODEFAULTLIB /MACHINE:X64 /OUT:`"$efi`" `"$obj`""
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $efi)) { Write-Host "FAIL: assemble/link"; exit 1 }
Write-Host "  EFI: $((Get-Item $efi).Length) bytes"

Write-Host "=== A1: wrap into GPT/FAT16 image ==="
& pwsh -NoProfile -File (Join-Path $repo 'build\build-img.ps1') -PeInput $efi -Out $img
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $img)) { Write-Host "FAIL: build-img"; exit 1 }

Write-Host "Done: $img"
Write-Host "  Visual:  tools/codex-vm.exe -kernel $img -uefi -gop -screenshot build/boot/a1.bmp"
Write-Host "  Strict:  tools/codex-vm.exe -kernel $img -uefi-strict -headless"
