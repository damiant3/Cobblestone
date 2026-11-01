# check-vm-kernel-load.ps1 -- codex-vm's kernel loader refuses a malformed ELF32
# or PE file and still loads a well-formed one.
#
# Each arm writes a kernel file and boots it with `-kernel` for at most 6 s:
#   elf-control   ELF32, one PT_LOAD at 0x100000 (cli; hlt): exit 0, a LOAD line
#   elf-wrap      PT_LOAD vaddr 0xFFFFF000 + filesz 0x2000 (wraps 32 bits): refused
#   elf-phoff     program headers at offset 0x7FFFFFF0 in a 100-byte file: refused
#   elf64         EI_CLASS 2: refused
#   pe-control    build/boot/diag.img under -uefi: BOOTX64.EFI extracted, PE entry set
#   pe-lfanew     MZ with e_lfanew 0xFFFFFFFC: refused
#   pe-rawwrap    one section, raw offset 0xFFFFF000 + size 0x1100 (wraps to 256): refused
#   pe-high       one section at VA 0x10000000 above the 16 MB load base: loaded
#   huge          a 3 GiB file (SetLength, nothing written) past 512 MB of RAM: refused
#   elf-entry, pe-entry, mb-entry   an entry at 0x50000000, outside what loaded: refused
# A codex-vm without those bounds ends elf-wrap, elf-phoff, pe-lfanew, pe-rawwrap
# and pe-high in HOST CRASH (exit 0xC0DE), and huge in a C runtime abort
# (0xC0000409): a 32-bit ftell answers -1 and the failed malloc goes to fread.
# It starts the three entry arms in unloaded memory and says nothing.
# Exit 0 when every arm passes, else 1.
#
#   pwsh build/check-vm-kernel-load.ps1 [-Vm tools/codex-vm.exe]
param([string]$Vm = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Vm) { $Vm = Join-Path $PSScriptRoot '..\tools\codex-vm.exe' }
$Vm = (Resolve-Path -LiteralPath $Vm).Path
$diagImg = (Resolve-Path (Join-Path $PSScriptRoot 'boot\diag.img')).Path
$ws = Split-Path -Leaf (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dir = Join-Path $env:TEMP ("vm-kernel-load-$ws-" + (Get-Date -Format 'yyyyMMddHHmmssfff'))
New-Item -ItemType Directory -Force $dir | Out-Null

function Put([byte[]]$Buf, $Value, [int]$At) { [BitConverter]::GetBytes($Value).CopyTo($Buf, $At) }

function New-Elf([byte]$Class, [uint32]$PhOff, [uint32]$VAddr, [uint32]$FileSz, [long]$Entry = -1) {
    $hdr = [byte[]]::new(84)
    $hdr[0] = 0x7F; $hdr[1] = 0x45; $hdr[2] = 0x4C; $hdr[3] = 0x46
    $hdr[4] = $Class; $hdr[5] = 1; $hdr[6] = 1
    Put $hdr ([uint16]2) 16; Put $hdr ([uint16]3) 18; Put $hdr ([uint32]$(if ($Entry -ge 0) { $Entry } else { $VAddr })) 24
    Put $hdr ([uint32]$PhOff) 28; Put $hdr ([uint16]52) 40; Put $hdr ([uint16]32) 42
    Put $hdr ([uint16]1) 44
    Put $hdr ([uint32]1) 52; Put $hdr ([uint32]84) 56; Put $hdr ([uint32]$VAddr) 60
    Put $hdr ([uint32]$VAddr) 64; Put $hdr ([uint32]$FileSz) 68; Put $hdr ([uint32]$FileSz) 72
    Put $hdr ([uint32]5) 76
    $body = [byte[]]::new($FileSz)
    $body[0] = 0xFA; $body[1] = 0xF4
    return [byte[]]($hdr + $body)
}

# PE32+ layout: e_lfanew at 60; COFF header at pe_off + 4 (NumberOfSections +6,
# SizeOfOptionalHeader +20); optional header at pe_off + 24 (AddressOfEntryPoint
# +16, ImageBase +24, SizeOfHeaders +60); 40-byte section headers after it
# (VirtualAddress +12, SizeOfRawData +16, PointerToRawData +20).
function New-Pe([uint32]$LfaNew, [uint32]$VAddr, [uint32]$RawSize, [uint32]$RawOff, [long]$Entry = -1) {
    $buf = [byte[]]::new(1024)
    $buf[0] = 0x4D; $buf[1] = 0x5A
    Put $buf ([uint32]$LfaNew) 60
    $pe = 64
    $buf[$pe] = 0x50; $buf[$pe + 1] = 0x45
    Put $buf ([uint16]0x8664) ($pe + 4); Put $buf ([uint16]1) ($pe + 6); Put $buf ([uint16]240) ($pe + 20)
    Put $buf ([uint32]$(if ($Entry -ge 0) { $Entry } else { $VAddr })) ($pe + 40); Put $buf ([uint64]0x1000000) ($pe + 48); Put $buf ([uint32]512) ($pe + 84)
    $sec = $pe + 24 + 240
    [Text.Encoding]::ASCII.GetBytes('.text').CopyTo($buf, $sec)
    Put $buf ([uint32]$RawSize) ($sec + 8); Put $buf ([uint32]$VAddr) ($sec + 12)
    Put $buf ([uint32]$RawSize) ($sec + 16); Put $buf ([uint32]$RawOff) ($sec + 20)
    $buf[512] = 0xFA; $buf[513] = 0xF4
    return $buf
}

# A raw image whose multiboot header (magic, flags with the a.out bit 16,
# checksum) names its entry at +28; cli; hlt at +32.
function New-Multiboot([uint32]$Entry) {
    $buf = [byte[]]::new(64)
    Put $buf ([uint32]0x1BADB002) 0; Put $buf ([uint32]0x10000) 4
    Put $buf ([uint32](0x100000000 - 0x1BADB002 - 0x10000)) 8; Put $buf ([uint32]$Entry) 28
    $buf[32] = 0xFA; $buf[33] = 0xF4
    return $buf
}

# Exit -1 accepts any exit or the 6 s timeout: the arm grades the loader, not the guest.
$arms = @(
    @{ Name = 'elf-control'; Bytes = (New-Elf 1 52 1048576 16);         Uefi = $false; Exit = 0;  Want = 'LOAD: vaddr=0x100000 filesz=16' }
    @{ Name = 'elf-wrap';    Bytes = (New-Elf 1 52 4294963200 8192);    Uefi = $false; Exit = 1;  Want = 'FATAL: ELF segment 0 (vaddr 0xfffff000' }
    @{ Name = 'elf-phoff';   Bytes = (New-Elf 1 2147483632 1048576 16); Uefi = $false; Exit = 1;  Want = 'FATAL: ELF program headers (offset 2147483632' }
    @{ Name = 'elf64';       Bytes = (New-Elf 2 52 1048576 16);         Uefi = $false; Exit = 1;  Want = 'FATAL: ELF kernel class 2 is not ELF32' }
    @{ Name = 'pe-control';  Path = $diagImg;                           Uefi = $true;  Exit = -1; Want = 'GPT: extracted BOOTX64.EFI' }
    @{ Name = 'pe-lfanew';   Bytes = (New-Pe 4294967292 4096 16 512);   Uefi = $true;  Exit = 1;  Want = 'FATAL: PE header offset 4294967292' }
    @{ Name = 'pe-rawwrap';  Bytes = (New-Pe 64 4096 4352 4294963200);  Uefi = $true;  Exit = 1;  Want = 'FATAL: PE section 0 (VA 0x1001000, raw offset 4294963200' }
    @{ Name = 'pe-high';     Bytes = (New-Pe 64 268435456 16 512);      Uefi = $true;  Exit = -1; Want = 'section .text: VA=0x11000000 size=0x10' }
    @{ Name = 'huge';        Length = 3221225472;                       Uefi = $false; Exit = 1;  Want = 'FATAL: kernel file' }
    @{ Name = 'elf-entry';   Bytes = (New-Elf 1 52 1048576 16 1342177280); Uefi = $false; Exit = 1; Want = 'FATAL: ELF entry 0x50000000 lies in no loaded segment' }
    @{ Name = 'pe-entry';    Bytes = (New-Pe 64 4096 16 512 1342177280);   Uefi = $true;  Exit = 1; Want = 'FATAL: PE entry RVA 0x50000000 lies in no loaded section' }
    @{ Name = 'mb-entry';    Bytes = (New-Multiboot 1342177280);           Uefi = $false; Exit = 1; Want = 'FATAL: multiboot entry 0x50000000 lies outside the image' }
)

$failed = 0
try {
    foreach ($a in $arms) {
        if ($a.ContainsKey('Path')) {
            $kernel = Join-Path $dir "$($a.Name).img"
            Copy-Item -LiteralPath $a.Path -Destination $kernel
            Set-ItemProperty -LiteralPath $kernel -Name IsReadOnly -Value $false
        } elseif ($a.ContainsKey('Length')) {
            $kernel = Join-Path $dir "$($a.Name).bin"
            $fs = [IO.File]::Create($kernel)
            try { $fs.SetLength($a.Length) } finally { $fs.Dispose() }
        } else {
            $kernel = Join-Path $dir "$($a.Name).bin"
            [IO.File]::WriteAllBytes($kernel, $a.Bytes)
        }
        $vmArgs = @('-kernel', $kernel, '-mem', '512', '-headless', '-output', (Join-Path $dir "$($a.Name).serial"))
        if ($a.Uefi) { $vmArgs += '-uefi' }
        $err = Join-Path $dir "$($a.Name).err"
        $p = Start-Process -FilePath $Vm -ArgumentList $vmArgs -WindowStyle Hidden -PassThru `
            -RedirectStandardError $err -RedirectStandardOutput (Join-Path $dir "$($a.Name).out")
        if (-not $p.WaitForExit(6000)) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(); $code = 'timeout' } else { $code = $p.ExitCode }
        $text = Get-Content -LiteralPath $err -Raw
        if ($null -eq $text) { $text = '' }
        $exitOk = ($a.Exit -eq -1 -and $code -ne 0xC0DE) -or ($code -eq $a.Exit)
        $ok = $exitOk -and $text.Contains($a.Want) -and -not $text.Contains('HOST CRASH')
        if (-not $ok) { $failed++ }
        $verdict = if ($ok) { 'PASS' } else { 'FAIL' }
        Write-Host ("{0} {1} exit={2}" -f $verdict, $a.Name, $code)
        if (-not $ok) { Write-Host ($text.Trim()) }
    }
} finally {
    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host ("{0} of {1} arms passed" -f ($arms.Count - $failed), $arms.Count)
if ($failed) { exit 1 }
exit 0
